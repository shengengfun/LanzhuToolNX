//! 彩蛋：从 sb6657.cn 随机偷一条烂梗。
//!
//! 这个站点的接口形态不稳定（也可能直接连不上），所以策略是：
//! 1. 依次试几个常见路径，**HTTP 404 就换下一个，连不上就直接放弃**
//!    （避免站点整个不可达时白白等 5 个超时）；
//! 2. 抓到的东西不管是 JSON 还是 HTML 都尽量把正文挖出来；
//! 3. 全失败就回落到内置列表，并且在界面上标明「本地兜底」——
//!    宁可让用户看到一条真的梗，也不要显示一个转圈或者报错。

use crate::spec::Meme;
use serde_json::Value;
use std::time::Duration;

/// 候选接口。第一个能在设置里覆盖（用户自己找到接口可以填）。
const CANDIDATES: &[&str] = &[
    "https://sb6657.cn/api/random",
    "https://sb6657.cn/api/rand",
    "https://sb6657.cn/api/meme",
    "https://sb6657.cn/api/word",
    "https://sb6657.cn/",
];

const UA: &str = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) LanzhuToolBox/1.2";

/// 抓一条；抓不到就走本地列表。
pub fn fetch(custom_url: &str) -> Meme {
    let mut urls: Vec<String> = Vec::new();
    let custom = custom_url.trim();
    if !custom.is_empty() {
        urls.push(custom.to_string());
    }
    urls.extend(CANDIDATES.iter().map(|s| s.to_string()));

    for url in urls {
        match ureq::get(&url)
            .timeout(Duration::from_secs(3))
            .set("User-Agent", UA)
            .call()
        {
            Ok(resp) => {
                let body = resp.into_string().unwrap_or_default();
                if let Some(text) = extract(&body) {
                    return Meme {
                        text,
                        source: "sb6657.cn".into(),
                        fallback: false,
                    };
                }
            }
            // 接口不存在（404/500）→ 换下一个候选
            Err(ureq::Error::Status(_, _)) => continue,
            // 连不上 / DNS 挂了 → 再试也是白等
            Err(_) => break,
        }
    }

    Meme {
        text: local(),
        source: String::new(),
        fallback: true,
    }
}

/// 从响应里挖正文：先当 JSON 找常见字段，再当 HTML 去标签。
fn extract(body: &str) -> Option<String> {
    let trimmed = body.trim();
    if trimmed.is_empty() {
        return None;
    }

    if let Ok(json) = serde_json::from_str::<Value>(trimmed) {
        if let Some(s) = pluck(&json) {
            return clean(&s);
        }
    }

    // HTML / 纯文本：去标签后取第一行有意义的文字
    let text = strip_tags(trimmed);
    for line in text.lines() {
        if let Some(s) = clean(line) {
            return Some(s);
        }
    }
    None
}

/// 递归找 JSON 里第一条像"梗"的字符串。
fn pluck(v: &Value) -> Option<String> {
    match v {
        Value::String(s) => {
            let t = s.trim();
            if t.is_empty() || t.len() > 600 {
                None
            } else {
                Some(t.to_string())
            }
        }
        Value::Array(items) => items.iter().find_map(pluck),
        Value::Object(map) => {
            for key in [
                "text", "msg", "message", "content", "meme", "sentence", "word", "joke",
                "data", "result", "value", "html",
            ] {
                if let Some(found) = map.get(key).and_then(pluck) {
                    return Some(found);
                }
            }
            None
        }
        _ => None,
    }
}

/// 去掉 HTML 标签与常见实体。
///
/// `<script>` / `<style>` 连**内容**一起丢——只去标签的话，
/// `body{color:red}` 这类 CSS 会被当成正文的第一个候选词。
fn strip_tags(s: &str) -> String {
    let mut out = String::with_capacity(s.len());
    let chars: Vec<char> = s.chars().collect();

    let mut in_tag = false;
    let mut closing = false;
    let mut name_done = false;
    let mut name = String::new();
    let mut skipping = false; // 正在 script/style 的正文里

    for &c in &chars {
        if !in_tag {
            if c == '<' {
                in_tag = true;
                closing = false;
                name_done = false;
                name.clear();
                continue;
            }
            if !skipping {
                out.push(c);
            }
            continue;
        }

        // 标签内部
        if c == '>' {
            in_tag = false;
            let n = name.to_lowercase();
            let is_block = n.starts_with("script") || n.starts_with("style");
            if is_block {
                skipping = !closing;
            }
            out.push('\n');
            continue;
        }
        if name.is_empty() && c == '/' {
            closing = true;
            continue;
        }
        if c.is_whitespace() {
            name_done = true;
            continue;
        }
        if !name_done {
            name.push(c);
        }
    }

    out.replace("&nbsp;", " ")
        .replace("&quot;", "\"")
        .replace("&#39;", "'")
        .replace("&lt;", "<")
        .replace("&gt;", ">")
        .replace("&amp;", "&")
}

/// 收尾清理：压掉多余空白、挡掉明显不是正文的行（脚本、样式、纯符号）。
fn clean(s: &str) -> Option<String> {
    let t = s.split_whitespace().collect::<Vec<_>>().join(" ");
    if t.chars().count() < 4 || t.chars().count() > 120 {
        return None;
    }
    let lowered = t.to_lowercase();
    if lowered.contains("function")
        || lowered.contains("var ")
        || lowered.contains("<!doctype")
        || lowered.contains("http-equiv")
        || lowered.starts_with('{')
        || lowered.starts_with('[')
    {
        return None;
    }
    // 至少要有一个中日韩字或字母，纯符号/纯数字不要
    if !t.chars().any(|c| c.is_alphanumeric() || !c.is_ascii()) {
        return None;
    }
    Some(t)
}

/// 内置兜底列表。挑的都是不冒犯、看得懂的程序员/压制圈味道。
const LOCAL: &[&str] = &[
    "别问，问就是重编码。",
    "能跑就别动它。",
    "这不是 BUG，这是特性。",
    "进度条是会骗人的。",
    "你管这玩意儿叫 4K？",
    "显卡：我尽力了。",
    "CPU 已放飞自我。",
    "先备份，再勇敢。",
    "再等五分钟就好了（五分钟前也是这么说的）。",
    "剪掉的都是精华。",
    "世界上只有两种编码器：慢的，和更慢的。",
    "缓存清了，心也空了。",
    "码率不够，滤镜来凑。",
    "画质这东西，加钱就能解决。",
    "帧率掉到 24，突然就有了电影感。",
];

fn local() -> String {
    let n = std::time::SystemTime::now()
        .duration_since(std::time::UNIX_EPOCH)
        .map(|d| d.subsec_nanos() as usize)
        .unwrap_or(0);
    LOCAL[n % LOCAL.len()].to_string()
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn extracts_plain_string_json() {
        assert_eq!(extract("{\"text\":\"你好世界\"}"), Some("你好世界".into()));
        assert_eq!(extract("\"直接就是字符串\""), Some("直接就是字符串".into()));
    }

    #[test]
    fn extracts_nested_and_array_json() {
        assert_eq!(
            extract("{\"code\":0,\"data\":{\"content\":\"嵌套的梗\"}}"),
            Some("嵌套的梗".into())
        );
        assert_eq!(extract("[{\"meme\":\"数组里的梗\"}]"), Some("数组里的梗".into()));
    }

    #[test]
    fn extracts_from_html_body() {
        let html = "<html><head><style>body{color:red}</style></head>\
                    <body><h1>今天也要好好编码</h1><p>下一条</p></body></html>";
        let got = extract(html).unwrap();
        assert!(got.contains("今天也要好好编码"), "{got}");
    }

    #[test]
    fn rejects_garbage_lines() {
        assert_eq!(extract("<script>function x(){var a=1;}</script>"), None);
        assert_eq!(extract(""), None);
        assert_eq!(extract("123"), None); // 太短
    }

    #[test]
    fn local_fallback_is_non_empty() {
        assert!(!local().is_empty());
        assert!(LOCAL.iter().all(|s| !s.is_empty()));
    }
}
