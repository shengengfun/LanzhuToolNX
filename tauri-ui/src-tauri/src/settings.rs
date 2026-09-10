use std::path::PathBuf;
use std::sync::Mutex;

use crate::spec::AppSettings;

static LOCK: Mutex<()> = Mutex::new(());

fn config_path() -> PathBuf {
    // 优先 %APPDATA%\LanzhuTool\settings.json；取不到就退回 exe 同级。
    let dir = std::env::var("APPDATA")
        .ok()
        .map(|a| PathBuf::from(a).join("LanzhuTool"))
        .unwrap_or_else(|| {
            std::env::current_exe()
                .ok()
                .and_then(|p| p.parent().map(|d| d.to_path_buf()))
                .unwrap_or_else(|| PathBuf::from("."))
        });
    let _ = std::fs::create_dir_all(&dir);
    dir.join("settings.json")
}

pub fn load() -> AppSettings {
    let _g = LOCK.lock();
    let p = config_path();
    match std::fs::read_to_string(&p) {
        Ok(s) => serde_json::from_str(&s).unwrap_or_default(),
        Err(_) => AppSettings::default(),
    }
}

pub fn save(s: &AppSettings) -> Result<(), String> {
    let _g = LOCK.lock();
    let p = config_path();
    let text = serde_json::to_string_pretty(s).map_err(|e| e.to_string())?;
    std::fs::write(&p, text).map_err(|e| format!("写入设置失败：{}", e))
}
