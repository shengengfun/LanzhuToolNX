//! 托盘图标。
//!
//! 长任务（压制一部番要几十分钟）最小化/关到托盘是刚需：
//! - 左键单击 / 双击 → 把主窗口叫回来
//! - 右键 → 菜单：显示、隐藏、暂停/终止当前任务、打开输出/工具目录、退出
//!
//! 「关闭按钮（X）收回托盘」是**默认行为**（设置里可以关掉），
//! 「最小化时收到托盘」由设置控制，见 `main.rs` 的 `on_window_event`。

use tauri::menu::{Menu, MenuItem, PredefinedMenuItem};
use tauri::tray::{MouseButton, MouseButtonState, TrayIconBuilder, TrayIconEvent};
use tauri::{AppHandle, Manager};

pub const TRAY_ID: &str = "main";

pub fn init(app: &AppHandle) -> tauri::Result<()> {
    let show = MenuItem::with_id(app, "show", "显示主窗口", true, None::<&str>)?;
    let hide = MenuItem::with_id(app, "hide", "隐藏到托盘", true, None::<&str>)?;
    let toggle_pause =
        MenuItem::with_id(app, "toggle_pause", "暂停 / 继续当前任务", true, None::<&str>)?;
    let cancel = MenuItem::with_id(app, "cancel", "终止当前任务", true, None::<&str>)?;
    let open_out = MenuItem::with_id(app, "open_output", "打开输出目录", true, None::<&str>)?;
    let open_tools = MenuItem::with_id(app, "open_tools", "打开工具目录", true, None::<&str>)?;
    let quit = MenuItem::with_id(app, "quit", "退出岚珠工具箱", true, None::<&str>)?;

    let menu = Menu::with_items(
        app,
        &[
            &show,
            &hide,
            &PredefinedMenuItem::separator(app)?,
            &toggle_pause,
            &cancel,
            &PredefinedMenuItem::separator(app)?,
            &open_out,
            &open_tools,
            &PredefinedMenuItem::separator(app)?,
            &quit,
        ],
    )?;

    let mut builder = TrayIconBuilder::with_id(TRAY_ID)
        .tooltip("岚珠工具箱")
        .menu(&menu)
        // 左键留给"显示窗口"，菜单走右键 —— 和常见桌面软件一致
        .show_menu_on_left_click(false)
        .on_menu_event(|app, event| match event.id().as_ref() {
            "show" => show_main(app),
            "hide" => hide_main(app),
            "toggle_pause" => crate::run::toggle_pause_current(app),
            "cancel" => crate::run::cancel_current(app),
            "open_output" => open_dir(app, crate::settings::load().output_dir),
            "open_tools" => {
                let s = crate::settings::load();
                open_dir(app, crate::tools::resolve_tools_dir(&s.tools_dir))
            }
            "quit" => app.exit(0),
            _ => {}
        })
        .on_tray_icon_event(|tray, event| match event {
            TrayIconEvent::Click {
                button: MouseButton::Left,
                button_state: MouseButtonState::Up,
                ..
            }
            | TrayIconEvent::DoubleClick {
                button: MouseButton::Left,
                ..
            } => show_main(tray.app_handle()),
            _ => {}
        });

    if let Some(icon) = app.default_window_icon().cloned() {
        builder = builder.icon(icon);
    }

    builder.build(app)?;
    Ok(())
}

pub fn show_main(app: &AppHandle) {
    if let Some(w) = app.get_webview_window("main") {
        let _ = w.show();
        let _ = w.unminimize();
        let _ = w.set_focus();
    }
}

pub fn hide_main(app: &AppHandle) {
    if let Some(w) = app.get_webview_window("main") {
        let _ = w.hide();
    }
}

/// 用系统默认程序打开目录；路径为空就不做任何事（免得打开一个莫名其妙的地方）。
fn open_dir(app: &AppHandle, dir: String) {
    use tauri_plugin_opener::OpenerExt;

    let target = dir.trim().to_string();
    if target.is_empty() {
        return;
    }
    let _ = app.opener().open_path(target, None::<&str>);
}
