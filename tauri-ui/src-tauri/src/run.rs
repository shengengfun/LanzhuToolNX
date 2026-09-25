use serde::Serialize;
use std::collections::HashMap;
use std::io::{Read, Write};
use std::process::{Child, Command, Stdio};
use std::sync::atomic::{AtomicU64, Ordering};
use std::sync::{Arc, Mutex};
use tauri::{AppHandle, Emitter, Manager};

/// 运行中的子进程表，用于取消。
#[derive(Default)]
pub struct RunState {
    seq: AtomicU64,
    kids: Mutex<HashMap<u64, u32>>, // run id -> pid
    /// 当前处于暂停态的任务（托盘菜单要靠它决定是"暂停"还是"继续"）
    paused: Mutex<std::collections::HashSet<u64>>,
}

#[derive(Clone, Serialize)]
#[serde(rename_all = "camelCase")]
struct OutputPayload {
    id: u64,
    line: String,
    stream: String,
}

#[derive(Clone, Serialize)]
#[serde(rename_all = "camelCase")]
struct DonePayload {
    id: u64,
    code: Option<i32>,
    elapsed_ms: u64,
}

#[derive(Clone, Serialize)]
#[serde(rename_all = "camelCase")]
struct ProgressPayload {
    id: u64,
    /// 已完成的工作单元（文件）数
    done: u32,
    /// 工作单元总数
    total: u32,
    /// 整体进度 0.0-1.0（含当前文件的内部进度）
    percent: f64,
    /// 当前正在处理的原文件路径
    file: String,
    /// ffmpeg 已编码帧数与速度（拿不到时为 0）
    frame: u64,
    speed: f64,
}

/// 把命令行写成 .bat 再交给 cmd 执行 —— 和原版 `WriteBatFile` + `WorkingForm` 一致。
///
/// 之所以不逐条 spawn：原版的命令大量使用管道（`ffmpeg ... pipe:|neroAacEnc ...`），
/// 那种写法只有在 cmd 里才有正确的管道语义，拆成两次 spawn 会直接坏掉。
fn write_bat(commands: &str) -> Result<std::path::PathBuf, String> {
    let dir = std::env::temp_dir().join("LanzhuTool");
    std::fs::create_dir_all(&dir).map_err(|e| e.to_string())?;

    let name = format!(
        "lanzhutool_{}.bat",
        std::time::SystemTime::now()
            .duration_since(std::time::UNIX_EPOCH)
            .map(|d| d.as_nanos())
            .unwrap_or(0)
    );
    let path = dir.join(name);

    let mut f = std::fs::File::create(&path).map_err(|e| e.to_string())?;
    // 原版写 .bat 时会在开头切到 UTF-8 代码页，否则中日文路径会乱码。
    // 这里确保 UTF-8 无 BOM，并显式 chcp 65001。
    f.write_all("@echo off\r\nchcp 65001>nul\r\n".as_bytes())
        .map_err(|e| e.to_string())?;
    f.write_all(commands.as_bytes()).map_err(|e| e.to_string())?;
    f.write_all(b"\r\n").ok();
    Ok(path)
}

/// 一个工作单元（= 一个文件）跑完时脚本会 echo 这一行。
const MARKER: &str = "===== one file is completed! =====";

/// 数出这批命令里有几个工作单元。
///
/// **不能**用行数当分母：一个文件的流水线有 3~5 条命令（抽音频 / 压制 / 封装 …），
/// 用行数算出来的"进度"只有跑完那一刻才会跳一下。脚本里每个文件末尾都 echo 一次
/// 上面的标记，拿它当单位才和原版 `WorkQueued` 对得上。
fn count_units(commands: &str) -> u32 {
    let n = commands.lines().filter(|l| l.contains(MARKER)).count();
    n.max(1) as u32
}

/// 从命令串里抠出 `-frames:v N`（界面的「编码帧数」）。
///
/// 指定了帧数时 ffmpeg 的 `time=` 只走到 N/fps 秒，拿源片时长当分母会让进度条
/// 永远停在个位数，所以要按 `min(时长, N/fps)` 校正。
fn frame_limit(commands: &str) -> Option<u64> {
    let i = commands.find("-frames:v")?;
    let rest = commands[i + 9..].trim_start();
    let digits: String = rest.chars().take_while(|c| c.is_ascii_digit()).collect();
    digits.parse().ok().filter(|v| *v > 0)
}

/// 进度追踪器。stdout / stderr 两个读取线程共用一个。
#[derive(Default)]
struct Tracker {
    total: u32,
    done: u32,
    /// 当前这一步的媒体时长（秒），来自 ffmpeg 的 `Duration: 00:01:23.45`
    duration: f64,
    /// 当前这一步的帧率，来自 ffmpeg 的 `Video: ... 23.98 fps`
    fps: f64,
    /// 当前步骤内部进度 0.0-1.0
    step: f64,
    frame: u64,
    speed: f64,
    /// 当前正在处理的原文件
    file: String,
    /// 界面的「编码帧数」，非 0 时用来校正分母
    limit_frames: u64,
    /// 上一次向前端推送进度/日志的时间（给逐帧刷屏的 rife / cugan 限流）
    last_emit: Option<std::time::Instant>,
}

impl Tracker {
    fn denominator(&self) -> f64 {
        let mut d = self.duration;
        if self.limit_frames > 0 && self.fps > 0.0 {
            let by_frames = self.limit_frames as f64 / self.fps;
            if d <= 0.0 || by_frames < d {
                d = by_frames;
            }
        }
        d
    }

    /// 整体进度：已完成文件数 + 当前文件内部进度。
    fn percent(&self) -> f64 {
        let total = self.total.max(1) as f64;
        let within = self.step.clamp(0.0, 1.0);
        (((self.done as f64) + within) / total).clamp(0.0, 1.0)
    }
}

/* ------------------------------------------------------------------ *
 * 输出解析
 *
 * ⚠️ 必须自己按字节切行，**不能**用 `BufRead::lines()`：
 * ffmpeg 的进度行（`frame= 248 fps= 30 ... time=00:00:10.34`）结尾是 **`\r`
 * 而不是 `\n`** —— 一行编码进度要等到编码彻底结束、管道被关掉时才吐出来，
 * 表现就是"日志里只有开头的压制参数，压制过程中一个字都不刷新"。
 * 原版用的是 .NET 的 `OutputDataReceived`，它把 `\r` 也当行结束符，所以没这个问题。
 * ------------------------------------------------------------------ */

/// 拆出 `Duration: 00:01:23.45`
fn parse_duration(line: &str) -> Option<f64> {
    let i = line.find("Duration:")?;
    let rest = &line[i + 9..];
    let end = rest.find(',').unwrap_or(rest.len());
    parse_hms(rest[..end].trim())
}

/// `HH:MM:SS.ss` → 秒
fn parse_hms(t: &str) -> Option<f64> {
    let mut it = t.split(':');
    let h: f64 = it.next()?.trim().parse().ok()?;
    let m: f64 = it.next()?.trim().parse().ok()?;
    let s: f64 = it.next()?.trim().parse().ok()?;
    Some(h * 3600.0 + m * 60.0 + s)
}

/// 拆出 `time=00:00:10.34`（也可能是 `time=-00:00:00.01`）
fn parse_time_field(line: &str) -> Option<f64> {
    let i = line.find("time=")?;
    let rest = &line[i + 5..];
    let raw: String = rest
        .chars()
        .take_while(|c| c.is_ascii_digit() || *c == ':' || *c == '.' || *c == '-')
        .collect();
    parse_hms(raw.trim_start_matches('-'))
}

/// 拆出 `frame=  248`
fn parse_frame(line: &str) -> Option<u64> {
    let i = line.find("frame=")?;
    let rest = line[i + 6..].trim_start();
    let digits: String = rest.chars().take_while(|c| c.is_ascii_digit()).collect();
    digits.parse().ok()
}

/// 拆出 `23.98 fps` / `25 fps`
fn parse_fps(line: &str) -> Option<f64> {
    let i = line.find(" fps")?;
    let before = &line[..i];
    let start = before
        .rfind(|c: char| !(c.is_ascii_digit() || c == '.'))
        .map(|p| p + 1)
        .unwrap_or(0);
    let v: f64 = before[start..].parse().ok()?;
    (v > 0.0 && v < 1000.0).then_some(v)
}

/// 拆出 `speed=1.2x`
fn parse_speed(line: &str) -> Option<f64> {
    let i = line.find("speed=")?;
    let rest = &line[i + 6..];
    let digits: String = rest
        .chars()
        .take_while(|c| c.is_ascii_digit() || *c == '.')
        .collect();
    digits.parse().ok()
}

/// 拆出 `from 'D:\Trans\a.mp4'`（ffmpeg 每开一个输入都会打这一行）
fn parse_input(line: &str) -> Option<String> {
    let i = line.find("from '")?;
    let rest = &line[i + 6..];
    let end = rest.find('\'')?;
    Some(rest[..end].to_string())
}

/// 一行输出 → 进度更新。
///
/// 返回 `true` 表示这是一条"进度行"（已经拿去更新进度条了，要不要进日志由调用方决定）。
fn feed(t: &mut Tracker, line: &str) -> bool {
    // 1) 新的输入文件 / 新的步骤：ffmpeg 会重新报一次时长与帧率
    if line.contains("Duration:") {
        if let Some(d) = parse_duration(line) {
            t.duration = d;
            t.step = 0.0;
            t.frame = 0;
        }
    }
    if line.contains("Video:") {
        if let Some(f) = parse_fps(line) {
            t.fps = f;
        }
    }
    if let Some(p) = parse_input(line) {
        if !p.starts_with("pipe:") {
            t.file = p;
        }
    }

    // 2) 编码进度：优先用 time=（最可靠），其次 frame=，再退到百分比
    if let Some(sec) = parse_time_field(line) {
        let d = t.denominator();
        if d > 0.0 {
            t.step = sec / d;
        }
        if let Some(f) = parse_frame(line) {
            t.frame = f;
        }
        if let Some(s) = parse_speed(line) {
            t.speed = s;
        }
        return true;
    }
    if let Some(f) = parse_frame(line) {
        t.frame = f;
        let d = t.denominator();
        if d > 0.0 && t.fps > 0.0 {
            t.step = (f as f64 / t.fps) / d;
        }
        if let Some(s) = parse_speed(line) {
            t.speed = s;
        }
        return true;
    }
    // rife / realcugan 这类模型工具只打百分比
    let trimmed = line.trim();
    if let Some(p) = trimmed.strip_suffix('%') {
        if !p.is_empty() && p.chars().all(|c| c.is_ascii_digit() || c == '.') {
            if let Ok(v) = p.parse::<f64>() {
                t.step = v / 100.0;
                return true;
            }
        }
    }
    false
}

/// 把进度推给前端 + Windows 任务栏。返回是否真的推了（限流时返回 false）。
fn push_progress(app: &AppHandle, id: u64, t: &mut Tracker, force: bool) -> bool {
    // 逐帧刷屏的工具（rife 每帧一行）限流到 ~8 次/秒
    let now = std::time::Instant::now();
    if !force {
        if let Some(last) = t.last_emit {
            if now.duration_since(last).as_millis() < 120 {
                return false;
            }
        }
    }
    t.last_emit = Some(now);

    let payload = ProgressPayload {
        id,
        done: t.done,
        total: t.total,
        percent: t.percent(),
        file: t.file.clone(),
        frame: t.frame,
        speed: t.speed,
    };
    crate::taskbar::progress(payload.percent);
    let _ = app.emit("run://progress", payload);
    true
}

/// 逐行抽读子进程输出并转发给前端。
fn pump(app: &AppHandle, id: u64, mut rd: Box<dyn Read + Send>, stream: &str, tr: Arc<Mutex<Tracker>>) {
    let mut buf: Vec<u8> = Vec::with_capacity(512);
    let mut chunk = vec![0u8; 16 * 1024];
    loop {
        let n = match rd.read(&mut chunk) {
            Ok(0) => break,
            Ok(n) => n,
            Err(_) => break,
        };
        for &b in &chunk[..n] {
            // `\r` 与 `\n` 都当行结束符（`\r\n` 会因为 buf 已空而自然跳过）
            if b == b'\r' || b == b'\n' {
                if !buf.is_empty() {
                    let line = String::from_utf8_lossy(&buf).to_string();
                    buf.clear();
                    handle_line(app, id, &line, stream, &tr);
                }
            } else {
                buf.push(b);
            }
        }
        // 万一某个工具打了一行超长的东西（没有任何换行），别让内存无界增长
        if buf.len() > 256 * 1024 {
            let line = String::from_utf8_lossy(&buf).to_string();
            buf.clear();
            handle_line(app, id, &line, stream, &tr);
        }
    }
    if !buf.is_empty() {
        let line = String::from_utf8_lossy(&buf).to_string();
        handle_line(app, id, &line, stream, &tr);
    }
}

fn handle_line(app: &AppHandle, id: u64, line: &str, stream: &str, tr: &Arc<Mutex<Tracker>>) {
    let is_marker = line.contains(MARKER);

    let (is_progress, pushed) = {
        let mut t = tr.lock().unwrap();
        let is_progress = feed(&mut t, line);
        if is_marker {
            t.done += 1;
            t.step = 0.0;
            t.duration = 0.0;
            t.frame = 0;
            t.speed = 0.0;
        }
        let pushed = if is_progress || is_marker {
            push_progress(app, id, &mut t, is_marker)
        } else {
            false
        };
        (is_progress, pushed)
    };

    // 进度行也照样进日志（原版就是这样：日志里能看到一串 frame= 实时刷过）。
    // 限流没推出去的那些就不记了 —— 逐帧工具一秒几十行会把日志冲爆。
    if !is_progress || pushed {
        let _ = app.emit(
            "run://output",
            OutputPayload {
                id,
                line: line.to_string(),
                stream: stream.to_string(),
            },
        );
    }
}

/// 执行一批命令，把输出按行发回前端。
pub fn spawn_commands(
    app: AppHandle,
    commands: String,
    cwd: String,
    _work_count: u32,
) -> Result<u64, String> {
    let state = app.state::<RunState>();
    let id = state.seq.fetch_add(1, Ordering::SeqCst) + 1;

    let bat = write_bat(&commands)?;
    let total = count_units(&commands);

    let mut cmd = Command::new("cmd.exe");
    cmd.arg("/D")
        .arg("/Q")
        .arg("/C")
        .arg(&bat)
        .current_dir(if cwd.is_empty() { "." } else { &cwd })
        .stdout(Stdio::piped())
        .stderr(Stdio::piped())
        .stdin(Stdio::null());

    #[cfg(windows)]
    {
        use std::os::windows::process::CommandExt;
        const CREATE_NO_WINDOW: u32 = 0x0800_0000;
        cmd.creation_flags(CREATE_NO_WINDOW);
    }

    let started = std::time::Instant::now();
    let mut child: Child = cmd.spawn().map_err(|e| format!("启动 cmd 失败：{}", e))?;
    let pid = child.id();
    state.kids.lock().unwrap().insert(id, pid);

    let stdout = child.stdout.take();
    let stderr = child.stderr.take();

    // 进度：脚本里每个文件跑完会 echo 一行标记，ffmpeg 的 `frame=/time=` 行
    // 则给出当前文件内部的进度 —— 两者合起来就是原版 WorkingForm 的进度条。
    let tracker = Arc::new(Mutex::new(Tracker {
        total,
        limit_frames: frame_limit(&commands).unwrap_or(0),
        ..Default::default()
    }));
    crate::taskbar::state(crate::taskbar::ProgressState::Normal);

    let h_out = stdout.map(|s| {
        let a = app.clone();
        let t = tracker.clone();
        std::thread::spawn(move || pump(&a, id, Box::new(s), "stdout", t))
    });
    let h_err = stderr.map(|s| {
        let a = app.clone();
        let t = tracker.clone();
        std::thread::spawn(move || pump(&a, id, Box::new(s), "stderr", t))
    });

    // 退出码只能等进程结束再取
    let a3 = app.clone();
    std::thread::spawn(move || {
        let code = child.wait().ok().and_then(|s| s.code());
        if let Some(h) = h_out {
            let _ = h.join();
        }
        if let Some(h) = h_err {
            let _ = h.join();
        }

        {
            let st = a3.state::<RunState>();
            st.kids.lock().unwrap().remove(&id);
            st.paused.lock().unwrap().remove(&id);
        }

        crate::taskbar::state(if code == Some(0) {
            crate::taskbar::ProgressState::None
        } else {
            crate::taskbar::ProgressState::Error
        });
        if code != Some(0) {
            // 失败时把红条留一会儿，用户切回来还能看出"刚才那次是失败的"
            std::thread::spawn(|| {
                std::thread::sleep(std::time::Duration::from_secs(6));
                crate::taskbar::state(crate::taskbar::ProgressState::None);
            });
        }

        let _ = std::fs::remove_file(&bat);
        let _ = a3.emit(
            "run://done",
            DonePayload {
                id,
                code,
                elapsed_ms: started.elapsed().as_millis() as u64,
            },
        );
    });

    Ok(id)
}

/// 取消：先 taskkill /T 杀掉整棵进程树（ffmpeg 下面可能挂着子进程），再兜底 kill。
pub fn cancel(app: &AppHandle, id: u64) -> Result<(), String> {
    let state = app.state::<RunState>();
    let pid = state.kids.lock().unwrap().get(&id).copied();
    let Some(pid) = pid else { return Ok(()) };

    let mut k = Command::new("taskkill");
    k.args(["/T", "/F", "/PID", &pid.to_string()]);
    #[cfg(windows)]
    {
        use std::os::windows::process::CommandExt;
        const CREATE_NO_WINDOW: u32 = 0x0800_0000;
        k.creation_flags(CREATE_NO_WINDOW);
    }
    let _ = k.output();
    crate::taskbar::state(crate::taskbar::ProgressState::None);
    Ok(())
}

/* ------------------------------------------------------------------ *
 * 暂停 / 继续
 *
 * 不能只挂 cmd.exe —— 真正干活的是它下面的 ffmpeg / x264 子进程，
 * 所以必须遍历整棵进程树，逐个 NtSuspendProcess。
 * ntdll 的 NtSuspendProcess 会一次性挂起该进程的全部线程，
 * 比自己枚举 Thread32First 再 SuspendThread 稳妥得多。
 * ------------------------------------------------------------------ */

#[cfg(windows)]
mod winproc {
    use std::ffi::c_void;

    #[link(name = "ntdll")]
    extern "system" {
        pub fn NtSuspendProcess(handle: *mut c_void) -> i32;
        pub fn NtResumeProcess(handle: *mut c_void) -> i32;
    }

    #[link(name = "kernel32")]
    extern "system" {
        pub fn OpenProcess(access: u32, inherit: i32, pid: u32) -> *mut c_void;
        pub fn CloseHandle(h: *mut c_void) -> i32;
        pub fn CreateToolhelp32Snapshot(flags: u32, pid: u32) -> *mut c_void;
        pub fn Process32First(h: *mut c_void, entry: *mut ProcessEntry32W) -> i32;
        pub fn Process32Next(h: *mut c_void, entry: *mut ProcessEntry32W) -> i32;
    }

    pub const PROCESS_SUSPEND_RESUME: u32 = 0x0800;
    pub const TH32CS_SNAPPROCESS: u32 = 0x0000_0002;
    pub const INVALID_HANDLE_VALUE: isize = -1;

    #[repr(C)]
    pub struct ProcessEntry32W {
        pub size: u32,
        pub usage: u32,
        pub pid: u32,
        pub heap: usize,
        pub module: u32,
        pub threads: u32,
        pub parent: u32,
        pub pri_class: i32,
        pub flags: u32,
        pub exe: [u16; 260],
    }

    impl Default for ProcessEntry32W {
        fn default() -> Self {
            Self {
                size: std::mem::size_of::<ProcessEntry32W>() as u32,
                usage: 0,
                pid: 0,
                heap: 0,
                module: 0,
                threads: 0,
                parent: 0,
                pri_class: 0,
                flags: 0,
                exe: [0; 260],
            }
        }
    }

    /// 取 pid 及其全部后代进程。
    pub fn descendants(root: u32) -> Vec<u32> {
        let mut pairs: Vec<(u32, u32)> = Vec::new();
        unsafe {
            let snap = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
            if snap as isize == INVALID_HANDLE_VALUE {
                return vec![root];
            }
            let mut e = ProcessEntry32W::default();
            if Process32First(snap, &mut e) != 0 {
                loop {
                    pairs.push((e.pid, e.parent));
                    let mut next = ProcessEntry32W::default();
                    if Process32Next(snap, &mut next) == 0 {
                        break;
                    }
                    e = next;
                }
            }
            CloseHandle(snap);
        }

        let mut out = vec![root];
        let mut changed = true;
        while changed {
            changed = false;
            for (pid, parent) in &pairs {
                if out.contains(parent) && !out.contains(pid) {
                    out.push(*pid);
                    changed = true;
                }
            }
        }
        out
    }
}

/// 暂停 / 继续整棵进程树。
#[cfg(windows)]
pub fn set_paused(app: &AppHandle, id: u64, paused: bool) -> Result<(), String> {
    let state = app.state::<RunState>();
    let pid = state.kids.lock().unwrap().get(&id).copied();
    let Some(pid) = pid else {
        return Err("任务已结束".into());
    };

    let _ = app.emit(
        "run://paused",
        serde_json::json!({ "id": id, "paused": paused }),
    );
    {
        let mut set = state.paused.lock().unwrap();
        if paused {
            set.insert(id);
        } else {
            set.remove(&id);
        }
    }

    let tree = winproc::descendants(pid);
    for p in tree {
        unsafe {
            let h = winproc::OpenProcess(winproc::PROCESS_SUSPEND_RESUME, 0, p);
            if h.is_null() {
                continue;
            }
            if paused {
                winproc::NtSuspendProcess(h);
            } else {
                winproc::NtResumeProcess(h);
            }
            winproc::CloseHandle(h);
        }
    }
    crate::taskbar::state(if paused {
        crate::taskbar::ProgressState::Paused
    } else {
        crate::taskbar::ProgressState::Normal
    });
    Ok(())
}

#[cfg(not(windows))]
pub fn set_paused(_app: &AppHandle, _id: u64, _paused: bool) -> Result<(), String> {
    Err("暂停功能目前只支持 Windows".into())
}

/* ------------------------------------------------------------------ *
 * 托盘菜单用的"当前任务"操作
 * ------------------------------------------------------------------ */

/// 当前正在跑的任务 id（同时只跑一个，取最大 id 就是最新的）。
pub fn current_id(state: &RunState) -> Option<u64> {
    state.kids.lock().ok()?.keys().copied().max()
}

/// 托盘菜单：暂停 / 继续当前任务。
pub fn toggle_pause_current(app: &AppHandle) {
    let state = app.state::<RunState>();
    let Some(id) = current_id(&state) else { return };
    let was_paused = state.paused.lock().map(|s| s.contains(&id)).unwrap_or(false);
    let _ = set_paused(app, id, !was_paused);
}

/// 托盘菜单：终止当前任务。
pub fn cancel_current(app: &AppHandle) {
    let state = app.state::<RunState>();
    if let Some(id) = current_id(&state) {
        let _ = cancel(app, id);
    }
}

/// 前端想知道"现在有没有任务在跑"时用。
pub fn is_running(app: &AppHandle) -> bool {
    current_id(&app.state::<RunState>()).is_some()
}

/* ------------------------------------------------------------------ *
 * 测试
 *
 * 进度这条链路以前是"看着有、其实永远不动"（`\r` 不带 `\n`，`lines()` 读不出来），
 * 而且只能在真机上跑长片才能发现。这里把解析部分锁住：几行 ffmpeg 真实输出进去，
 * 百分比要能对得上。
 * ------------------------------------------------------------------ */

#[cfg(test)]
mod tests {
    use super::*;

    /// ffmpeg 的进度行（stderr），末尾是 `\r` —— 这里给的是去掉行尾的样子
    const STATS: &str = "frame=  248 fps= 30 q=28.0 size=    1024kB time=00:00:10.34 bitrate= 810.2kbits/s speed=1.24x";

    fn tracker() -> Tracker {
        Tracker {
            total: 2,
            limit_frames: 0,
            ..Default::default()
        }
    }

    #[test]
    fn parses_ffmpeg_progress_line() {
        assert_eq!(parse_frame(STATS), Some(248));
        assert_eq!(parse_time_field(STATS), Some(10.34));
        assert_eq!(parse_speed(STATS), Some(1.24));
    }

    #[test]
    fn parses_duration_and_fps_from_banner() {
        assert_eq!(
            parse_duration("  Duration: 00:02:03.45, start: 0.000000, bitrate: 1200 kb/s"),
            Some(123.45)
        );
        assert_eq!(
            parse_fps("  Stream #0:0: Video: h264, yuv420p, 1920x1080, 23.98 fps, 23.98 tbr"),
            Some(23.98)
        );
        assert_eq!(parse_fps("  Stream #0:1: Audio: aac, 48000 Hz, stereo, fltp, 128 kb/s"), None);
    }

    #[test]
    fn parses_input_path() {
        assert_eq!(
            parse_input("Input #0, mov,mp4,m4a,3gp,3g2,mj2, from 'D:\\Trans\\a.mp4':"),
            Some("D:\\Trans\\a.mp4".into())
        );
    }

    /// 一整段真实形状的日志：开头是输入信息，然后一路 frame= 刷到结束。
    #[test]
    fn percent_advances_while_encoding() {
        let mut t = tracker();
        // 开头这些是"输入信息"，不是进度行（返回值 false）
        assert!(!feed(&mut t, "Input #0, mov,mp4,m4a,3gp,3g2,mj2, from 'D:\\Trans\\a.mp4':"));
        assert!(!feed(&mut t, "  Duration: 00:01:40.00, start: 0.000000, bitrate: 1200 kb/s"));
        assert!(!feed(&mut t, "  Stream #0:0: Video: h264, yuv420p, 1920x1080, 25 fps, 25 tbr"));
        assert_eq!(t.percent(), 0.0);

        // 压到一半
        let line = STATS.replace("time=00:00:10.34", "time=00:00:50.00");
        assert!(feed(&mut t, &line));
        assert!((t.percent() - 0.25).abs() < 0.001, "{}", t.percent());

        // 第二个文件跑完（标记行由 handle_line 处理，这里手算一遍）
        t.done = 1;
        t.step = 1.0;
        assert!((t.percent() - 1.0).abs() < 0.001);
    }

    /// `-frames:v N` 时 ffmpeg 的 time= 只走到 N/fps，分母要跟着换，
    /// 否则「编码帧数」一填，进度条就永远停在个位数。
    #[test]
    fn frame_limit_shrinks_denominator() {
        let mut t = tracker();
        t.limit_frames = 250;
        t.fps = 25.0;
        feed(&mut t, "  Duration: 00:01:40.00, start: 0.000000");
        assert!((t.denominator() - 10.0).abs() < 0.001);
        assert!(feed(&mut t, "frame= 125 fps= 25 time=00:00:05.00 speed=1x"));
        assert!((t.step - 0.5).abs() < 0.001, "{}", t.step);
    }

    /// rife / realcugan 只打百分比，也要能推动进度条。
    #[test]
    fn accepts_bare_percentage_lines() {
        let mut t = tracker();
        assert!(feed(&mut t, "  42.5%"));
        assert!((t.step - 0.425).abs() < 0.001);
        // 普通输出不能被误当成百分比
        assert!(!feed(&mut t, "Metadata:"));
    }

    /// 分母是"文件数"（脚本里的标记行），不是命令行数。
    #[test]
    fn total_counts_markers_not_lines() {
        let bat = "ffmpeg -i a.mp4 out.mp4\r\ndel out.mp4\r\necho ===== one file is completed! =====\r\n\
                   ffmpeg -i b.mp4 out2.mp4\r\necho ===== one file is completed! =====\r\n";
        assert_eq!(count_units(bat), 2);
        // 没有标记时兜底为 1，免得除以 0
        assert_eq!(count_units("ffmpeg -i a.mp4 out.mp4"), 1);
    }

    #[test]
    fn finds_frame_limit_flag() {
        assert_eq!(frame_limit("ffmpeg -i a.mp4 -frames:v 100 out.mp4"), Some(100));
        assert_eq!(frame_limit("ffmpeg -i a.mp4 out.mp4"), None);
    }
}
