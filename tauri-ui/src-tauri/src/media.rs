//! 本地媒体访问：自绘 `lzmedia://` 协议 + 波形图生成。
//!
//! ## 为什么不用 Tauri 自带的 asset 协议
//!
//! asset 协议要开 `protocol-asset` feature + 在 `tauri.conf.json` 里配 scope，
//! 而这个项目要放行的是**用户任意位置**的素材（视频粗剪、背景图），
//! 配 `**` 等于把 scope 关掉，还要多背一个 feature 和潜在的不匹配报错。
//!
//! 自己注册一个 scheme 只需几十行，而且**能自己实现 Range**——
//! 视频预览能不能拖动进度条、能不能秒开，全看服务端认不认 `Range: bytes=`。
//! 直接返回整个文件会让几十 GB 的素材把内存吃爆，所以这里是真·流式分片读取。
//!
//! Windows 下自定义 scheme 的 URL 形态是 `http://lzmedia.localhost/<percent-encoded-path>`。

use std::io::{Read, Seek, SeekFrom};
use std::path::Path;

use tauri::http::{header, Request, Response, StatusCode};

pub const SCHEME: &str = "lzmedia";

/// 单次响应的体量上限。浏览器按 Range 要分片，很少会要整个大文件；
/// 真出现"不带 Range 的大文件请求"（某些 WebView 版本会先探一下），
/// 直接拒绝比把 4GB 读进内存然后 OOM 好得多。
const MAX_BODY: u64 = 256 * 1024 * 1024;

/* ================================================================== *
 * 协议处理
 * ================================================================== */

pub fn handle<R: tauri::Runtime>(
    _ctx: tauri::UriSchemeContext<'_, R>,
    req: Request<Vec<u8>>,
    responder: tauri::UriSchemeResponder,
) {
    let range = req
        .headers()
        .get(header::RANGE)
        .and_then(|v| v.to_str().ok())
        .map(|s| s.to_string());

    let Some(path) = req.uri().path().strip_prefix('/').map(percent_decode) else {
        responder.respond(text(400, "无效路径"));
        return;
    };
    if path.is_empty() {
        responder.respond(text(400, "无效路径"));
        return;
    }

    // 读盘放到独立线程：协议处理器在 WebView 的 IO 线程上跑，别把它堵住
    std::thread::spawn(move || {
        responder.respond(build_response(&path, range.as_deref()));
    });
}

fn build_response(path: &str, range: Option<&str>) -> Response<Vec<u8>> {
    let p = Path::new(path);
    let meta = match std::fs::metadata(p) {
        Ok(m) if m.is_file() => m,
        _ => return text(404, "文件不存在"),
    };
    let len = meta.len();
    if len == 0 {
        return text(416, "空文件");
    }

    let (start, end, partial) = match range.and_then(parse_range) {
        // 只认单段 Range（媒体播放器不会用多段）
        Some((s, e)) => {
            let s = s.min(len - 1);
            let e = e.unwrap_or(len - 1).min(len - 1);
            if s > e {
                return text(416, "Range 越界");
            }
            (s, e, true)
        }
        None => (0, len - 1, false),
    };

    let count = end - start + 1;
    if count > MAX_BODY {
        return text(413, "请求区间过大");
    }

    let mut buf = vec![0u8; count as usize];
    let read = std::fs::File::open(p).and_then(|mut f| {
        f.seek(SeekFrom::Start(start))?;
        f.read_exact(&mut buf)?;
        Ok(())
    });
    if read.is_err() {
        return text(500, "读取失败");
    }

    let mut b = Response::builder()
        .status(if partial { 206 } else { 200 })
        .header(header::CONTENT_TYPE, mime_of(path))
        .header(header::ACCEPT_RANGES, "bytes")
        .header(header::CONTENT_LENGTH, count.to_string())
        .header(header::CACHE_CONTROL, "no-store")
        // 开发模式下页面来自 http://localhost:1420，跨源请求要放行
        .header(header::ACCESS_CONTROL_ALLOW_ORIGIN, "*");

    if partial {
        b = b.header(
            header::CONTENT_RANGE,
            format!("bytes {}-{}/{}", start, end, len),
        );
    }

    b.body(buf).unwrap_or_else(|_| text(500, "构造响应失败"))
}

/// 解析 `bytes=start-end` / `bytes=start-`；不合法返回 None（当作整段处理）。
fn parse_range(v: &str) -> Option<(u64, Option<u64>)> {
    let v = v.trim();
    let spec = v.strip_prefix("bytes=")?;
    if spec.contains(',') {
        return None;
    }
    let (a, b) = spec.split_once('-')?;
    let start = a.trim().parse::<u64>().ok()?;
    let end = b.trim();
    Some((start, if end.is_empty() { None } else { end.parse().ok() }))
}

fn text(code: u16, msg: &str) -> Response<Vec<u8>> {
    Response::builder()
        .status(StatusCode::from_u16(code).unwrap_or(StatusCode::BAD_REQUEST))
        .header(header::CONTENT_TYPE, "text/plain; charset=utf-8")
        .header(header::ACCESS_CONTROL_ALLOW_ORIGIN, "*")
        .body(msg.as_bytes().to_vec())
        .unwrap()
}

fn percent_decode(s: &str) -> String {
    let bytes = s.as_bytes();
    let mut out: Vec<u8> = Vec::with_capacity(bytes.len());
    let mut i = 0;
    while i < bytes.len() {
        if bytes[i] == b'%' && i + 2 < bytes.len() {
            let h = hex(bytes[i + 1]);
            let l = hex(bytes[i + 2]);
            if let (Some(h), Some(l)) = (h, l) {
                out.push(h << 4 | l);
                i += 3;
                continue;
            }
        }
        out.push(bytes[i]);
        i += 1;
    }
    String::from_utf8_lossy(&out).to_string()
}

fn hex(c: u8) -> Option<u8> {
    match c {
        b'0'..=b'9' => Some(c - b'0'),
        b'a'..=b'f' => Some(c - b'a' + 10),
        b'A'..=b'F' => Some(c - b'A' + 10),
        _ => None,
    }
}

fn mime_of(path: &str) -> &'static str {
    let ext = Path::new(path)
        .extension()
        .map(|e| e.to_string_lossy().to_lowercase())
        .unwrap_or_default();
    match ext.as_str() {
        "mp4" | "m4v" => "video/mp4",
        "m4a" | "aac" => "audio/mp4",
        "mkv" => "video/x-matroska",
        "webm" => "video/webm",
        "mov" => "video/quicktime",
        "avi" => "video/x-msvideo",
        "flv" => "video/x-flv",
        "wmv" | "asf" => "video/x-ms-wmv",
        "ts" | "m2ts" => "video/mp2t",
        "mpg" | "mpeg" => "video/mpeg",
        "mp3" => "audio/mpeg",
        "ac3" => "audio/ac3",
        "flac" => "audio/flac",
        "wav" => "audio/wav",
        "ogg" | "opus" => "audio/ogg",
        "mka" => "audio/x-matroska",
        "png" => "image/png",
        "jpg" | "jpeg" => "image/jpeg",
        "webp" => "image/webp",
        "gif" => "image/gif",
        "bmp" => "image/bmp",
        "avif" => "image/avif",
        _ => "application/octet-stream",
    }
}

/* ================================================================== *
 * 波形图
 * ================================================================== */

/// 用 ffmpeg 的 `showwavespic` 画一张波形图，直接以 data URI 返回。
///
/// 不走文件 + 协议是因为波形图很小（几十 KB），data URI 最省事：
/// 不用管路径转义、不用管缓存失效，改一次参数就换一张图。
pub fn waveform(
    tools: &str,
    input: &str,
    width: u32,
    height: u32,
    color: &str,
) -> Result<String, String> {
    use std::process::Command;

    let w = width.clamp(200, 4096);
    let h = height.clamp(40, 600);
    // ffmpeg 的颜色值不接受 `#`，统一成 0xRRGGBB
    let c = color.trim().trim_start_matches('#');
    let c = if c.is_empty() {
        "2F9E79".to_string()
    } else {
        c.to_uppercase()
    };

    let dir = std::env::temp_dir().join("LanzhuTool").join("temp");
    std::fs::create_dir_all(&dir).map_err(|e| format!("创建临时目录失败：{}", e))?;
    let out = dir.join(format!(
        "wave_{}.png",
        std::time::SystemTime::now()
            .duration_since(std::time::UNIX_EPOCH)
            .map(|d| d.as_nanos())
            .unwrap_or(0)
    ));

    let filter = format!(
        "[0:a]aformat=channel_layouts=mono,showwavespic=s={}x{}:colors=0x{}",
        w, h, c
    );

    let mut cmd = Command::new(crate::tools::tool(tools, "ffmpeg.exe"));
    cmd.args(["-hide_banner", "-nostdin", "-v", "error"])
        .arg("-i")
        .arg(input)
        .args(["-filter_complex", &filter, "-frames:v", "1", "-y"])
        .arg(&out);

    #[cfg(windows)]
    {
        use std::os::windows::process::CommandExt;
        const CREATE_NO_WINDOW: u32 = 0x0800_0000;
        cmd.creation_flags(CREATE_NO_WINDOW);
    }

    let res = cmd.output().map_err(|e| format!("ffmpeg 启动失败：{}", e))?;
    if !res.status.success() {
        let err = String::from_utf8_lossy(&res.stderr);
        let first = err.lines().find(|l| !l.trim().is_empty()).unwrap_or("");
        let _ = std::fs::remove_file(&out);
        return Err(if first.is_empty() {
            "生成波形失败（该文件可能没有音轨）".into()
        } else {
            format!("生成波形失败：{}", first.trim())
        });
    }

    let bytes = std::fs::read(&out).map_err(|e| format!("读取波形失败：{}", e))?;
    let _ = std::fs::remove_file(&out);
    Ok(format!("data:image/png;base64,{}", base64(&bytes)))
}

/// 极简 base64 编码（只为内联图片，不值得为此拉一个依赖进来）。
pub fn base64(data: &[u8]) -> String {
    const T: &[u8; 64] = b"ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
    let mut out = String::with_capacity((data.len() + 2) / 3 * 4);
    for chunk in data.chunks(3) {
        let b0 = chunk[0] as u32;
        let b1 = *chunk.get(1).unwrap_or(&0) as u32;
        let b2 = *chunk.get(2).unwrap_or(&0) as u32;
        let n = (b0 << 16) | (b1 << 8) | b2;
        out.push(T[(n >> 18) as usize & 63] as char);
        out.push(T[(n >> 12) as usize & 63] as char);
        out.push(if chunk.len() > 1 {
            T[(n >> 6) as usize & 63] as char
        } else {
            '='
        });
        out.push(if chunk.len() > 2 {
            T[n as usize & 63] as char
        } else {
            '='
        });
    }
    out
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn base64_matches_known_values() {
        assert_eq!(base64(b""), "");
        assert_eq!(base64(b"f"), "Zg==");
        assert_eq!(base64(b"fo"), "Zm8=");
        assert_eq!(base64(b"foo"), "Zm9v");
        assert_eq!(base64(b"foobar"), "Zm9vYmFy");
        assert_eq!(base64(&[0xff, 0x00, 0x7f]), "/wB/");
    }

    #[test]
    fn range_parsing() {
        assert_eq!(parse_range("bytes=0-"), Some((0, None)));
        assert_eq!(parse_range("bytes=100-200"), Some((100, Some(200))));
        assert_eq!(parse_range("bytes=0-1,5-6"), None);
        assert_eq!(parse_range("items=0-1"), None);
        assert_eq!(parse_range("bytes=abc-"), None);
    }

    #[test]
    fn percent_decoding_windows_paths() {
        assert_eq!(
            percent_decode("C%3A%5CUsers%5Ca%20b%2Fc.mp4"),
            r"C:\Users\a b/c.mp4"
        );
        // 非法转义原样保留，不能把路径吃掉
        assert_eq!(percent_decode("a%zzb"), "a%zzb");
    }

    #[test]
    fn mime_by_extension() {
        assert_eq!(mime_of(r"D:\a\b.MP4"), "video/mp4");
        assert_eq!(mime_of("x.mkv"), "video/x-matroska");
        assert_eq!(mime_of("x.unknown"), "application/octet-stream");
    }
}
