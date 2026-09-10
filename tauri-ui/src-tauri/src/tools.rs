use std::path::{Path, PathBuf};
use std::sync::Mutex;

/// 工具目录解析 + 可执行文件查找。
///
/// 原版是靠 `organize_tools.ps1` 把 tools/ 下的 exe 全平铺到工作目录。
/// 这里不复制文件，改成"按名字递归找一次然后缓存"，好处是不污染工作目录，
/// 也不需要用户先跑一遍整理脚本。
static CACHE: Mutex<Option<Vec<(String, PathBuf)>>> = Mutex::new(None);

/// 用双引号包住路径（等价原版的 `Util.FormatPath`）。
pub fn quote(p: &str) -> String {
    format!("\"{}\"", p)
}

/// 给子进程加 `CREATE_NO_WINDOW`。
///
/// GUI 应用里 spawn 控制台程序会**闪一下黑框** —— 用户报的
/// 「拖入视频的时候会弹命令提示符」就是 ffprobe 探测时冒出来的。
/// 所有 spawn 控制台工具的地方都必须走这个函数。
pub fn no_window(cmd: &mut std::process::Command) -> &mut std::process::Command {
    #[cfg(windows)]
    {
        use std::os::windows::process::CommandExt;
        const CREATE_NO_WINDOW: u32 = 0x0800_0000;
        cmd.creation_flags(CREATE_NO_WINDOW);
    }
    cmd
}

fn exe_dir() -> PathBuf {
    std::env::current_exe()
        .ok()
        .and_then(|p| p.parent().map(|d| d.to_path_buf()))
        .unwrap_or_else(|| PathBuf::from("."))
}

/// 工具目录优先级：
///   1. 环境变量 LANZHUTOOL_TOOLS（便于绿色部署 / 多版本共存）
///   2. 设置里手动指定的目录
///   3. exe 同级的 tools/
///   4. 上两级目录下的 tools/
///   5. %APPDATA%\LanzhuTool\tools（免安装时可以直接把工具丢这儿）
///   6. 开发期仓库里的 tools/
pub fn resolve_tools_dir(override_dir: &str) -> String {
    if let Ok(env) = std::env::var("LANZHUTOOL_TOOLS") {
        let p = PathBuf::from(env.trim());
        if p.is_dir() {
            return p.to_string_lossy().to_string();
        }
    }

    if !override_dir.trim().is_empty() {
        let p = PathBuf::from(override_dir.trim());
        if p.is_dir() {
            return p.to_string_lossy().to_string();
        }
    }

    let base = exe_dir();
    let mut candidates: Vec<PathBuf> = vec![
        base.join("tools"),
        base.join("..").join("tools"),
        base.join("..").join("..").join("tools"),
    ];

    if let Ok(appdata) = std::env::var("APPDATA") {
        candidates.push(PathBuf::from(appdata).join("LanzhuTool").join("tools"));
    }

    // 开发期 / 本地使用：仓库里那份 800MB 的工具集
    candidates.push(PathBuf::from(r"D:\Project\LanzhuToolNX\tools"));
    candidates.push(PathBuf::from(r"D:\Project\LanzhuToolNX\mp4box\bin\Debug\tools"));

    for c in candidates.iter() {
        if c.is_dir() {
            return clean_path(c);
        }
    }

    // 一个都没找到：返回 exe 同级的 tools/（不存在也没关系，
    // 前端会拿 list_bundled_tools() 的结果弹"找不到工具"的提示）
    base.join("tools").to_string_lossy().to_string()
}

/// `canonicalize()` 在 Windows 上会给出 `\\?\D:\...` 这种扩展前缀。
/// 它能用，但显示在界面上很脏，而且交给某些工具会不认，所以统一去掉。
fn clean_path(p: &Path) -> String {
    let s = p.canonicalize().unwrap_or_else(|_| p.to_path_buf());
    let t = s.to_string_lossy().to_string();
    t.strip_prefix(r"\\?\").map(|x| x.to_string()).unwrap_or(t)
}

fn scan(root: &Path, out: &mut Vec<(String, PathBuf)>, depth: usize) {
    if depth > 4 {
        return;
    }
    let Ok(rd) = std::fs::read_dir(root) else { return };
    for entry in rd.flatten() {
        let path = entry.path();
        if path.is_dir() {
            scan(&path, out, depth + 1);
        } else if let Some(name) = path.file_name().and_then(|s| s.to_str()) {
            out.push((name.to_ascii_lowercase(), path.clone()));
        }
    }
}

fn find(tools_dir: &str, name: &str) -> Option<PathBuf> {
    let key = name.to_ascii_lowercase();

    {
        let guard = CACHE.lock().unwrap();
        if let Some(list) = guard.as_ref() {
            if let Some((_, p)) = list.iter().find(|(n, _)| *n == key) {
                return Some(p.clone());
            }
        }
    }

    // 未命中：扫描一次并整体缓存
    let mut list: Vec<(String, PathBuf)> = Vec::new();
    scan(Path::new(tools_dir), &mut list, 0);
    let hit = list.iter().find(|(n, _)| *n == key).map(|(_, p)| p.clone());
    *CACHE.lock().unwrap() = Some(list);

    hit
}

/// 取工具的完整路径；找不到就退回工具名本身（让 cmd 从 PATH 里找，报错也更直观）。
pub fn tool(tools_dir: &str, name: &str) -> String {
    match find(tools_dir, name) {
        Some(p) => p.to_string_lossy().to_string(),
        None => name.to_string(),
    }
}

/// 清掉索引缓存。装完新工具、或改了工具目录之后必须调用，
/// 否则还会拿旧索引去解析路径。
pub fn reset_cache() {
    *CACHE.lock().unwrap() = None;
}

/// 列出所有能找到的工具名，给"设置"页做体检。
pub fn list_tools(tools_dir: &str) -> Vec<String> {
    let mut list: Vec<(String, PathBuf)> = Vec::new();
    scan(Path::new(tools_dir), &mut list, 0);
    *CACHE.lock().unwrap() = Some(list.clone());
    let mut names: Vec<String> = list.into_iter().map(|(n, _)| n).collect();
    names.sort();
    names.dedup();
    names
}
