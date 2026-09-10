use serde::Serialize;
use std::collections::HashMap;
use std::io::{BufRead, BufReader, Write};
use std::process::{Child, Command, Stdio};
use std::sync::atomic::{AtomicU64, Ordering};
use std::sync::Mutex;
use tauri::{AppHandle, Emitter, Manager};

/// 运行中的子进程表，用于取消。
#[derive(Default)]
pub struct RunState {
    seq: AtomicU64,
    kids: Mutex<HashMap<u64, u32>>, // run id -> pid
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
    done: u32,
    total: u32,
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

fn count_commands(commands: &str) -> u32 {
    commands
        .lines()
        .filter(|l| {
            let t = l.trim();
            !t.is_empty() && !t.starts_with("echo") && !t.starts_with("del ") && !t.starts_with("rem")
        })
        .count() as u32
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
    let total = count_commands(&commands).max(1);

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

    // 进度：脚本里每完成一个文件会 echo 一行标记，拿它当真实进度信号
    let done = std::sync::Arc::new(AtomicU64::new(0));

    let h_out = stdout.map(|s| {
        let a = app.clone();
        let d = done.clone();
        std::thread::spawn(move || pump(&a, id, Box::new(BufReader::new(s)), "stdout", d, total))
    });
    let h_err = stderr.map(|s| {
        let a = app.clone();
        let d = done.clone();
        std::thread::spawn(move || pump(&a, id, Box::new(BufReader::new(s)), "stderr", d, total))
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

/// 逐行抽读子进程输出并转发给前端。
fn pump(
    app: &AppHandle,
    id: u64,
    rd: Box<dyn BufRead + Send>,
    stream: &str,
    done: std::sync::Arc<AtomicU64>,
    total: u32,
) {
    const MARKER: &str = "===== one file is completed! =====";
    for line in rd.lines().map_while(Result::ok) {
        let _ = app.emit(
            "run://output",
            OutputPayload {
                id,
                line: line.clone(),
                stream: stream.to_string(),
            },
        );
        if line.contains(MARKER) {
            let d = done.fetch_add(1, Ordering::SeqCst) + 1;
            let _ = app.emit(
                "run://progress",
                ProgressPayload {
                    id,
                    done: d as u32,
                    total,
                },
            );
        }
    }
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
    Ok(())
}

#[cfg(not(windows))]
pub fn set_paused(_app: &AppHandle, _id: u64, _paused: bool) -> Result<(), String> {
    Err("暂停功能目前只支持 Windows".into())
}
