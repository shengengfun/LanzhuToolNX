//! 系统性能监控（CPU / 内存 / GPU / 编码进程数）。
//!
//! 设计取舍：
//! - CPU / 内存 直接用 kernel32 的 `GetSystemTimes` + `GlobalMemoryStatusEx`，
//!   **零依赖、零进程开销**，2 秒一次完全无感。
//! - GPU 没有稳定且轻量的系统 API：
//!   有 N 卡时优先问 `nvidia-smi`（~40ms，数据最准）；
//!   否则退回 Windows 性能计数器（`\GPU Engine(*)\Utilization Percentage`），
//!   这条查询要起一个 PowerShell，所以单独降到 6 秒一次，并在后台线程里跑，
//!   绝不阻塞界面。
//! - 结果写进一个全局快照，前端轮询 `system_stats` 拿现成值。
//!
//! 所有查询都做失败兜底：取不到的项返回 -1，界面显示「--」而不是崩掉。

use crate::spec::SysStats;
use std::sync::{Mutex, OnceLock};
use std::time::Duration;

static STATS: OnceLock<Mutex<SysStats>> = OnceLock::new();
static STARTED: OnceLock<()> = OnceLock::new();

fn cell() -> &'static Mutex<SysStats> {
    STATS.get_or_init(|| Mutex::new(SysStats::default()))
}

/// 启动后台采样线程（重复调用无效果）。
pub fn start() {
    if STARTED.set(()).is_err() {
        return;
    }
    std::thread::spawn(|| {
        let mut cpu = CpuSampler::new();
        let mut tick: u32 = 0;
        loop {
            let (used, total) = memory();
            let mut s = SysStats {
                cpu: cpu.sample(),
                mem_used: used,
                mem_total: total,
                gpu: -1.0,
                gpu_name: String::new(),
                gpu_mem_used: 0,
                gpu_mem_total: 0,
                procs: count_encoder_procs(),
            };

            // GPU 第一次立刻取，之后每 3 个 tick（≈6s）刷新一次
            if tick % 3 == 0 {
                if let Some(g) = gpu() {
                    s.gpu = g.util;
                    s.gpu_name = g.name;
                    s.gpu_mem_used = g.mem_used;
                    s.gpu_mem_total = g.mem_total;
                } else {
                    // 保持上一次的成功值，避免界面数字来回跳
                    let prev = cell().lock().map(|p| p.clone()).unwrap_or_default();
                    s.gpu = prev.gpu;
                    s.gpu_name = prev.gpu_name;
                    s.gpu_mem_used = prev.gpu_mem_used;
                    s.gpu_mem_total = prev.gpu_mem_total;
                }
            } else {
                let prev = cell().lock().map(|p| p.clone()).unwrap_or_default();
                s.gpu = prev.gpu;
                s.gpu_name = prev.gpu_name;
                s.gpu_mem_used = prev.gpu_mem_used;
                s.gpu_mem_total = prev.gpu_mem_total;
            }

            if let Ok(mut g) = cell().lock() {
                *g = s;
            }
            tick = tick.wrapping_add(1);
            std::thread::sleep(Duration::from_secs(2));
        }
    });
}

/// 取当前快照（不可变克隆，前端调用）。
pub fn snapshot() -> SysStats {
    start();
    cell().lock().map(|s| s.clone()).unwrap_or_default()
}

/* ================================================================== *
 * CPU —— GetSystemTimes 差分
 * ================================================================== */

#[repr(C)]
#[derive(Default, Clone, Copy)]
struct FileTime {
    low: u32,
    high: u32,
}

impl FileTime {
    fn ticks(self) -> u64 {
        ((self.high as u64) << 32) | self.low as u64
    }
}

#[repr(C)]
#[derive(Default, Clone, Copy)]
struct MemoryStatusEx {
    length: u32,
    memory_load: u32,
    total_phys: u64,
    avail_phys: u64,
    total_page_file: u64,
    avail_page_file: u64,
    total_virtual: u64,
    avail_virtual: u64,
    avail_extended_virtual: u64,
}

#[repr(C)]
struct ProcessEntry32 {
    size: u32,
    usage: u32,
    process_id: u32,
    default_heap_id: usize,
    module_id: u32,
    threads: u32,
    parent_process_id: u32,
    pri_class_base: i32,
    flags: u32,
    exe_file: [u16; 260],
}

#[link(name = "kernel32")]
extern "system" {
    fn GetSystemTimes(idle: *mut FileTime, kernel: *mut FileTime, user: *mut FileTime) -> i32;
    fn GlobalMemoryStatusEx(buf: *mut MemoryStatusEx) -> i32;
    fn CreateToolhelp32Snapshot(flags: u32, pid: u32) -> *mut std::ffi::c_void;
    // 注意用 *W 版本：run.rs 里声明的是 ANSI 版 `Process32First`，
    // 同名但签名不同会触发 clashing_extern_declarations 警告。
    fn Process32FirstW(snap: *mut std::ffi::c_void, entry: *mut ProcessEntry32) -> i32;
    fn Process32NextW(snap: *mut std::ffi::c_void, entry: *mut ProcessEntry32) -> i32;
    fn CloseHandle(handle: *mut std::ffi::c_void) -> i32;
}

struct CpuSampler {
    last_idle: u64,
    last_total: u64,
}

impl CpuSampler {
    fn new() -> Self {
        let (idle, total) = raw_times();
        Self {
            last_idle: idle,
            last_total: total,
        }
    }

    /// 两次采样之间的 CPU 占用（0-100）。首次调用返回 0。
    fn sample(&mut self) -> f64 {
        let (idle, total) = raw_times();
        let d_idle = idle.saturating_sub(self.last_idle);
        let d_total = total.saturating_sub(self.last_total);
        self.last_idle = idle;
        self.last_total = total;
        if d_total == 0 {
            return 0.0;
        }
        let busy = d_total.saturating_sub(d_idle) as f64;
        (busy * 100.0 / d_total as f64).clamp(0.0, 100.0)
    }
}

fn raw_times() -> (u64, u64) {
    let mut idle = FileTime::default();
    let mut kernel = FileTime::default();
    let mut user = FileTime::default();
    let ok = unsafe { GetSystemTimes(&mut idle, &mut kernel, &mut user) };
    if ok == 0 {
        return (0, 0);
    }
    // kernel 时间**已经包含** idle，所以总时间 = kernel + user
    (idle.ticks(), kernel.ticks() + user.ticks())
}

fn memory() -> (u64, u64) {
    let mut m = MemoryStatusEx::default();
    m.length = std::mem::size_of::<MemoryStatusEx>() as u32;
    let ok = unsafe { GlobalMemoryStatusEx(&mut m) };
    if ok == 0 {
        return (0, 0);
    }
    (m.total_phys.saturating_sub(m.avail_phys), m.total_phys)
}

/// 统计当前正在跑的编码相关进程数（ffmpeg / x264 / x265 / ffprobe / mkvmerge…）。
fn count_encoder_procs() -> u32 {
    const TH32CS_SNAPPROCESS: u32 = 0x0000_0002;
    const INVALID: isize = -1;
    let snap = unsafe { CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0) };
    if snap as isize == INVALID || snap.is_null() {
        return 0;
    }
    let mut n = 0u32;
    let mut e: ProcessEntry32 = unsafe { std::mem::zeroed() };
    e.size = std::mem::size_of::<ProcessEntry32>() as u32;
    let mut ok = unsafe { Process32FirstW(snap, &mut e) };
    while ok != 0 {
        let end = e.exe_file.iter().position(|c| *c == 0).unwrap_or(0);
        let name = String::from_utf16_lossy(&e.exe_file[..end]).to_lowercase();
        if name.starts_with("ffmpeg")
            || name.starts_with("x264")
            || name.starts_with("x265")
            || name.starts_with("ffprobe")
            || name.starts_with("mkvmerge")
            || name.starts_with("mp4box")
            || name.starts_with("qaac")
            || name.starts_with("neroaacenc")
        {
            n += 1;
        }
        ok = unsafe { Process32NextW(snap, &mut e) };
    }
    unsafe {
        CloseHandle(snap);
    }
    n
}

/* ================================================================== *
 * GPU
 * ================================================================== */

struct GpuSample {
    util: f64,
    name: String,
    mem_used: u64,
    mem_total: u64,
}

fn no_window(cmd: &mut std::process::Command) -> &mut std::process::Command {
    #[cfg(windows)]
    {
        use std::os::windows::process::CommandExt;
        const CREATE_NO_WINDOW: u32 = 0x0800_0000;
        cmd.creation_flags(CREATE_NO_WINDOW);
    }
    cmd
}

fn nvidia_smi_path() -> Option<String> {
    let candidates = [
        "nvidia-smi.exe".to_string(),
        r"C:\Windows\System32\nvidia-smi.exe".to_string(),
        format!(
            r"{}\NVIDIA Corporation\NVSMI\nvidia-smi.exe",
            std::env::var("ProgramFiles").unwrap_or_else(|_| r"C:\Program Files".into())
        ),
    ];
    for c in candidates {
        if c.contains('\\') && !std::path::Path::new(&c).is_file() {
            continue;
        }
        let out = no_window(&mut std::process::Command::new(&c))
            .args(["--query-gpu=name", "--format=csv,noheader"])
            .output();
        if out.map(|o| o.status.success()).unwrap_or(false) {
            return Some(c);
        }
    }
    None
}

fn gpu() -> Option<GpuSample> {
    if let Some(exe) = nvidia_smi_path() {
        if let Some(g) = gpu_via_nvidia_smi(&exe) {
            return Some(g);
        }
    }
    gpu_via_counters()
}

fn gpu_via_nvidia_smi(exe: &str) -> Option<GpuSample> {
    let out = no_window(&mut std::process::Command::new(exe))
        .args([
            "--query-gpu=name,utilization.gpu,memory.used,memory.total",
            "--format=csv,noheader,nounits",
        ])
        .output()
        .ok()?;
    if !out.status.success() {
        return None;
    }
    let text = String::from_utf8_lossy(&out.stdout);
    let line = text.lines().next()?;
    let f: Vec<&str> = line.split(',').map(|s| s.trim()).collect();
    if f.len() < 4 {
        return None;
    }
    Some(GpuSample {
        name: f[0].to_string(),
        util: f[1].parse().unwrap_or(-1.0),
        mem_used: f[2].parse::<u64>().unwrap_or(0) * 1024 * 1024,
        mem_total: f[3].parse::<u64>().unwrap_or(0) * 1024 * 1024,
    })
}

/// AMD / Intel 走 Windows 性能计数器。取不到就返回 None（界面显示「--」）。
fn gpu_via_counters() -> Option<GpuSample> {
    // 名字单独取一次并缓存，别每轮都问 CIM
    let name = gpu_name_cached();
    let script = concat!(
        "$ErrorActionPreference='SilentlyContinue';",
        "$u=(Get-Counter '\\GPU Engine(*)\\Utilization Percentage').CounterSamples",
        " | Measure-Object -Property CookedValue -Sum;",
        "$m=(Get-Counter '\\GPU Adapter Memory(*)\\Dedicated Usage').CounterSamples",
        " | Measure-Object -Property CookedValue -Sum;",
        "\"$([math]::Round($u.Sum,1))|$([int64]$m.Sum)\""
    );
    let out = no_window(&mut std::process::Command::new("powershell.exe"))
        .args(["-NoProfile", "-NonInteractive", "-Command", script])
        .output()
        .ok()?;
    if !out.status.success() {
        return None;
    }
    let text = String::from_utf8_lossy(&out.stdout);
    let line = text.lines().find(|l| l.contains('|'))?;
    let (u, m) = line.trim().split_once('|')?;
    let util: f64 = u.trim().parse().unwrap_or(-1.0);
    if util < 0.0 {
        return None;
    }
    Some(GpuSample {
        name,
        // 性能计数器是多引擎求和，可能 >100，夹到 100
        util: util.clamp(0.0, 100.0),
        mem_used: m.trim().parse().unwrap_or(0),
        mem_total: 0,
    })
}

fn gpu_name_cached() -> String {
    static NAME: OnceLock<Mutex<String>> = OnceLock::new();
    let cell = NAME.get_or_init(|| Mutex::new(String::new()));
    if let Ok(s) = cell.lock() {
        if !s.is_empty() {
            return s.clone();
        }
    }
    let out = no_window(&mut std::process::Command::new("powershell.exe"))
        .args([
            "-NoProfile",
            "-NonInteractive",
            "-Command",
            "Get-CimInstance Win32_VideoController | Select-Object -First 1 -ExpandProperty Name",
        ])
        .output();
    let name = out
        .ok()
        .map(|o| String::from_utf8_lossy(&o.stdout).trim().to_string())
        .unwrap_or_default();
    if let Ok(mut s) = cell.lock() {
        *s = name.clone();
    }
    name
}
