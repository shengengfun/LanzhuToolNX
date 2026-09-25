//! Windows 任务栏进度条（原版 `WorkingForm.UpdateTaskBar` 的等价物）。
//!
//! 原版用 `ProgressTaskbar.cs` 里的 `ITaskbarList3`，Tauri 没有暴露这个能力，
//! 所以这里直接手写一张 COM 虚表：**只用得上 3 个方法**，
//! 比拖一个 `windows` crate（几 MB 依赖）进来划算得多。
//!
//! 线程模型：`ITaskbarList3` 属于 STA，所以专门起一条常驻线程做
//! `CoInitializeEx` + `CoCreateInstance`，其余线程通过 channel 把
//! "进度 / 状态" 丢进去，由它调用。COM 调用失败一律静默 ——
//! 任务栏进度条画不出来不该影响压制本身。

use std::sync::mpsc::{channel, Sender};
use std::sync::{Mutex, OnceLock};

/// 任务栏进度状态（TBPFLAG）
#[derive(Clone, Copy, PartialEq)]
pub enum ProgressState {
    None,
    Normal,
    Paused,
    Error,
}

impl ProgressState {
    fn flag(self) -> u32 {
        match self {
            // TBPF_NOPROGRESS
            ProgressState::None => 0x0,
            // TBPF_NORMAL
            ProgressState::Normal => 0x2,
            // TBPF_ERROR
            ProgressState::Error => 0x4,
            // TBPF_PAUSED
            ProgressState::Paused => 0x8,
        }
    }
}

enum Msg {
    Value(u64, u64),
    State(u32),
}

static TX: OnceLock<Mutex<Sender<Msg>>> = OnceLock::new();
/// 主窗口句柄（HWND 存成 isize），由 `main.rs` 在 setup 里写入
static HWND: Mutex<Option<isize>> = Mutex::new(None);

/// 记下主窗口句柄。菜单栏/任务栏的进度都画在这个窗口的按钮上。
pub fn set_hwnd(h: isize) {
    if let Ok(mut g) = HWND.lock() {
        *g = Some(h);
    }
}

fn hwnd() -> Option<isize> {
    HWND.lock().ok().and_then(|g| *g)
}

fn tx() -> &'static Mutex<Sender<Msg>> {
    TX.get_or_init(|| {
        let (t, r) = channel::<Msg>();
        std::thread::Builder::new()
            .name("lanzhu-taskbar".into())
            .spawn(move || com::worker(r))
            .ok();
        Mutex::new(t)
    })
}

/// 上报进度（0.0-1.0）。调用方在任意线程。
pub fn progress(percent: f64) {
    let v = percent.clamp(0.0, 1.0);
    if let Ok(t) = tx().lock() {
        let _ = t.send(Msg::Value((v * 1000.0).round() as u64, 1000));
    }
}

/// 切换状态（开始 / 暂停 / 失败 / 清空）。
pub fn state(s: ProgressState) {
    if let Ok(t) = tx().lock() {
        let _ = t.send(Msg::State(s.flag()));
    }
}

#[cfg(windows)]
mod com {
    use super::*;
    use std::ffi::c_void;

    #[repr(C)]
    struct Guid {
        d1: u32,
        d2: u16,
        d3: u16,
        d4: [u8; 8],
    }

    const CLSID_TASKBAR_LIST: Guid = Guid {
        d1: 0x56FD_F344,
        d2: 0xFD6D,
        d3: 0x11D0,
        d4: [0x95, 0x8A, 0x00, 0x60, 0x97, 0xC9, 0xA0, 0x90],
    };
    const IID_TASKBAR_LIST3: Guid = Guid {
        d1: 0xEA1A_FB91,
        d2: 0x9E28,
        d3: 0x4B86,
        d4: [0x90, 0xE9, 0x9E, 0x9F, 0x8A, 0x5E, 0xEA, 0x84],
    };
    const CLSCTX_INPROC_SERVER: u32 = 0x1;
    /// COINIT_APARTMENTTHREADED
    const COINIT_APARTMENTTHREADED: u32 = 0x2;

    #[link(name = "ole32")]
    extern "system" {
        fn CoInitializeEx(reserved: *mut c_void, coinit: u32) -> i32;
        fn CoCreateInstance(
            rclsid: *const Guid,
            punkouter: *mut c_void,
            dwclsctx: u32,
            riid: *const Guid,
            ppv: *mut *mut c_void,
        ) -> i32;
        fn CoUninitialize();
    }

    type HrInitFn = unsafe extern "system" fn(*mut c_void) -> i32;
    type SetProgressValueFn = unsafe extern "system" fn(*mut c_void, isize, u64, u64) -> i32;
    type SetProgressStateFn = unsafe extern "system" fn(*mut c_void, isize, u32) -> i32;

    /// ITaskbarList3 的对象。
    ///
    /// 虚表布局（只列用到的）：
    ///   IUnknown           0 QueryInterface / 1 AddRef / 2 Release
    ///   ITaskbarList       3 HrInit / 4 AddTab / 5 DeleteTab / 6 ActivateTab / 7 SetActiveAlt
    ///   ITaskbarList2      8 MarkFullscreenWindow
    ///   ITaskbarList3      9 SetProgressValue / 10 SetProgressState / ...
    pub struct TaskbarCom {
        ptr: *mut c_void,
    }

    unsafe impl Send for TaskbarCom {}

    impl TaskbarCom {
        fn vtable(&self) -> *const *const c_void {
            unsafe { *(self.ptr as *const *const *const c_void) }
        }

        pub fn new() -> Option<Self> {
            unsafe {
                if CoInitializeEx(std::ptr::null_mut(), COINIT_APARTMENTTHREADED) < 0 {
                    return None;
                }
                let mut raw: *mut c_void = std::ptr::null_mut();
                let hr = CoCreateInstance(
                    &CLSID_TASKBAR_LIST,
                    std::ptr::null_mut(),
                    CLSCTX_INPROC_SERVER,
                    &IID_TASKBAR_LIST3,
                    &mut raw,
                );
                if hr < 0 || raw.is_null() {
                    CoUninitialize();
                    return None;
                }
                let this = TaskbarCom { ptr: raw };
                let init: HrInitFn = std::mem::transmute(*this.vtable().add(3));
                if init(raw) < 0 {
                    return None;
                }
                Some(this)
            }
        }

        fn set_value(&self, hwnd: isize, done: u64, total: u64) {
            unsafe {
                let f: SetProgressValueFn = std::mem::transmute(*self.vtable().add(9));
                f(self.ptr, hwnd, done, total);
            }
        }

        fn set_state(&self, hwnd: isize, flag: u32) {
            unsafe {
                let f: SetProgressStateFn = std::mem::transmute(*self.vtable().add(10));
                f(self.ptr, hwnd, flag);
            }
        }
    }

    impl Drop for TaskbarCom {
        fn drop(&mut self) {
            unsafe {
                let release: unsafe extern "system" fn(*mut c_void) -> u32 =
                    std::mem::transmute(*self.vtable().add(2));
                release(self.ptr);
                CoUninitialize();
            }
        }
    }

    /// 常驻线程：懒建 COM 对象，然后按消息调用。
    pub fn worker(rx: std::sync::mpsc::Receiver<Msg>) {
        let mut obj: Option<TaskbarCom> = None;
        // 第一次收到消息时窗口肯定已经建好了（进度更新发生在任务开始之后）
        let last_state = std::cell::Cell::new(u32::MAX);
        while let Ok(msg) = rx.recv() {
            if obj.is_none() {
                obj = TaskbarCom::new();
            }
            let Some(o) = obj.as_ref() else { continue };
            let Some(h) = hwnd() else { continue };
            match msg {
                Msg::Value(done, total) => o.set_value(h, done, total),
                Msg::State(flag) => {
                    if last_state.get() != flag {
                        last_state.set(flag);
                        o.set_state(h, flag);
                    }
                }
            }
        }
    }
}

#[cfg(not(windows))]
mod com {
    use super::*;
    use std::ffi::c_void;

    /// 非 Windows 平台：没有任务栏进度条这回事，收到消息直接丢弃。
    pub fn worker(_rx: std::sync::mpsc::Receiver<Msg>) {}
}
