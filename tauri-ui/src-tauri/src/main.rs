// 发布版不弹控制台窗口
#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

mod cmd;
mod media;
mod probe;
mod run;
mod settings;
mod spec;
mod sysmon;
mod tools;
mod tools_fetch;
mod tray;

use spec::*;
use std::path::PathBuf;
use tauri::Manager;

/// 临时文件目录：原版是 workPath 下的 temp/，这里放到系统临时目录，避免污染安装目录。
fn temp_dir() -> String {
    let d = std::env::temp_dir().join("LanzhuTool").join("temp");
    let _ = std::fs::create_dir_all(&d);
    d.to_string_lossy().to_string()
}

fn tools_dir() -> String {
    let s = settings::load();
    tools::resolve_tools_dir(&s.tools_dir)
}

/* ================================================================== *
 * 命令预览：只拼命令行、不执行
 * ================================================================== */

#[tauri::command(rename_all = "camelCase")]
fn plan_video(spec: VideoSpec, audio: AudioSpec) -> Result<Vec<String>, String> {
    let tools = tools_dir();
    let tp = temp_dir();

    if spec.input.trim().is_empty() {
        return Err("请先选择输入视频".into());
    }
    if spec.output.trim().is_empty() {
        return Err("请先选择输出文件".into());
    }

    // 原版靠 MediaInfo 判断有无音轨、音轨格式；这里用 ffprobe。
    let info = probe::probe(&tools, &spec.input);
    let has_audio = info.audio.is_some();
    let audio_format = info.audio.map(|a| a.codec).unwrap_or_default();

    let mut a = audio;
    a.input = spec.input.clone();

    let bat = cmd::video_pipeline(
        &spec,
        &a,
        &tools,
        &tp,
        &spec.input,
        &spec.output,
        &spec.subtitle,
        has_audio,
        &audio_format,
    );

    Ok(bat
        .lines()
        .map(|l| l.trim_end_matches('\r').to_string())
        .filter(|l| !l.trim().is_empty())
        .collect())
}

#[tauri::command(rename_all = "camelCase")]
fn plan_audio(spec: AudioSpec) -> Result<Vec<String>, String> {
    let tools = tools_dir();
    if spec.input.trim().is_empty() {
        return Err("请先选择输入音频".into());
    }
    if spec.output.trim().is_empty() {
        return Err("请先选择输出文件".into());
    }
    let bat = cmd::audiobat(&spec, &tools);
    Ok(bat
        .lines()
        .map(|l| l.trim_end_matches('\r').to_string())
        .filter(|l| !l.trim().is_empty())
        .collect())
}

#[tauri::command(rename_all = "camelCase")]
fn plan_mux(spec: MuxSpec) -> Result<Vec<String>, String> {
    let tools = tools_dir();
    if spec.video.trim().is_empty() {
        return Err("请选择视频文件".into());
    }
    if spec.output.trim().is_empty() {
        return Err("请选择输出文件".into());
    }
    Ok(cmd::mux(&spec, &tools)
        .lines()
        .map(|l| l.trim_end_matches('\r').to_string())
        .filter(|l| !l.trim().is_empty())
        .collect())
}

#[tauri::command(rename_all = "camelCase")]
fn plan_extract(spec: ExtractSpec) -> Result<Vec<String>, String> {
    let tools = tools_dir();
    if spec.input.trim().is_empty() {
        return Err("请选择来源文件".into());
    }
    if spec.output.trim().is_empty() {
        return Err("请选择输出文件".into());
    }
    Ok(vec![cmd::extract(&spec, &tools).trim_end().to_string()])
}

#[tauri::command(rename_all = "camelCase")]
fn plan_avs(spec: AvsSpec, audio: AudioSpec) -> Result<Vec<String>, String> {
    let tools = tools_dir();
    let tp = temp_dir();
    if spec.script_path.trim().is_empty() {
        return Err("请先选择脚本保存位置".into());
    }
    if spec.spec.output.trim().is_empty() {
        return Err("请先选择输出文件".into());
    }

    let mut a = audio;
    a.input = spec.script_path.clone();

    let bat = cmd::video_pipeline(
        &spec.spec,
        &a,
        &tools,
        &tp,
        &spec.script_path,
        &spec.spec.output,
        "",
        false,
        "",
    );
    Ok(bat
        .lines()
        .map(|l| l.trim_end_matches('\r').to_string())
        .filter(|l| !l.trim().is_empty())
        .collect())
}

/// 批量：每个文件独立一条流水线，串成一个大脚本。
/// 输出命名沿用原版规则：`<输出目录>\<文件名>_<格式后缀><扩展名>`。
#[tauri::command(rename_all = "camelCase")]
fn plan_batch(
    inputs: Vec<String>,
    spec: VideoSpec,
    audio: AudioSpec,
    output_dir: String,
    embed_subtitle: bool,
) -> Result<Vec<String>, String> {
    let tools = tools_dir();
    let tp = temp_dir();

    if inputs.is_empty() {
        return Err("请先添加要压制的视频".into());
    }
    if output_dir.trim().is_empty() {
        return Err("请先选择输出路径".into());
    }

    let suffix = match cmd::preset_kind(&spec.format) {
        cmd::PresetKind::Hevc => "hevc",
        cmd::PresetKind::Mov => "mov",
        cmd::PresetKind::Flv => "flv",
        cmd::PresetKind::H264 => "h264",
    };
    let ext = cmd::output_ext(&spec.format);

    let mut all: Vec<String> = Vec::new();
    for input in inputs.iter() {
        let stem = std::path::Path::new(input)
            .file_stem()
            .map(|s| s.to_string_lossy().to_string())
            .unwrap_or_else(|| "output".into());

        let output = PathBuf::from(&output_dir)
            .join(format!("{}_{}{}", stem, suffix, ext))
            .to_string_lossy()
            .to_string();

        let info = probe::probe(&tools, input);
        let has_audio = info.audio.is_some();
        let audio_format = info.audio.map(|a| a.codec).unwrap_or_default();

        // 内嵌字幕：优先找同名 .ass/.srt（原版 GetSubtitlePath 的行为）
        let sub = if embed_subtitle {
            find_subtitle(input)
        } else {
            String::new()
        };

        let mut a = audio.clone();
        a.input = input.clone();

        let bat = cmd::video_pipeline(
            &spec,
            &a,
            &tools,
            &tp,
            input,
            &output,
            &sub,
            has_audio,
            &audio_format,
        );

        all.extend(
            bat.lines()
                .map(|l| l.trim_end_matches('\r').to_string())
                .filter(|l| !l.trim().is_empty()),
        );
    }

    Ok(all)
}

fn find_subtitle(input: &str) -> String {
    find_subtitle_with(input, "none")
}

/// 找同名字幕。`lang` 是原版 `x264BatchSubSpecialLanguage` 的等价物：
/// 取值 `none` / `zh` / `zh-Hans` / `jp` …，非 none 时优先找 `<名字>.<lang>.ass`。
fn find_subtitle_with(input: &str, lang: &str) -> String {
    let p = std::path::Path::new(input);
    let stem = p
        .file_stem()
        .map(|s| s.to_string_lossy().to_string())
        .unwrap_or_default();
    let Some(dir) = p.parent() else {
        return String::new();
    };
    let suffix = if lang.trim().is_empty() || lang == "none" {
        String::new()
    } else {
        format!(".{}", lang.trim())
    };
    for ext in [".ass", ".srt", ".ssa", ".sub"] {
        let c = dir.join(format!("{}{}{}", stem, suffix, ext));
        if c.is_file() {
            return c.to_string_lossy().to_string();
        }
    }
    // 带语言后缀的没找到时，回落到不带后缀的同名字幕
    if !suffix.is_empty() {
        for ext in [".ass", ".srt", ".ssa", ".sub"] {
            let c = dir.join(format!("{}{}", stem, ext));
            if c.is_file() {
                return c.to_string_lossy().to_string();
            }
        }
    }
    String::new()
}

/* ================================================================== *
 * 执行
 * ================================================================== */

#[tauri::command(rename_all = "camelCase")]
fn run_commands(
    app: tauri::AppHandle,
    commands: Vec<String>,
    work_count: u32,
) -> Result<u64, String> {
    let cwd = tools_dir();
    run::spawn_commands(app, commands.join("\r\n"), cwd, work_count)
}

#[tauri::command(rename_all = "camelCase")]
fn cancel_run(app: tauri::AppHandle, id: u64) -> Result<(), String> {
    run::cancel(&app, id)
}

/// 暂停 / 继续当前任务。会挂起整棵进程树，所以 ffmpeg / x264 子进程也一起停。
#[tauri::command(rename_all = "camelCase")]
fn pause_run(app: tauri::AppHandle, id: u64, paused: bool) -> Result<(), String> {
    run::set_paused(&app, id, paused)
}

/* ================================================================== *
 * 探测与设置
 * ================================================================== */

#[tauri::command(rename_all = "camelCase")]
fn probe_media(path: String) -> MediaInfo {
    probe::probe(&tools_dir(), &path)
}

#[tauri::command(rename_all = "camelCase")]
fn load_settings() -> AppSettings {
    settings::load()
}

#[tauri::command(rename_all = "camelCase")]
fn save_settings(settings: AppSettings) -> Result<(), String> {
    settings::save(&settings)
}

#[tauri::command(rename_all = "camelCase")]
fn resolve_tools_dir() -> String {
    tools_dir()
}

/// 把工具名解析成绝对路径。
/// 给「常用」页那些一次性的 ffmpeg 小工具用：前端拼模板，后端只提供真实路径，
/// 这样两边都不用重复实现"递归找 exe"的逻辑。
#[tauri::command(rename_all = "camelCase")]
fn resolve_tool(name: String) -> String {
    tools::tool(&tools_dir(), &name)
}

/* ================================================================== *
 * 工具在线下载 / 离线包
 * ================================================================== */

#[tauri::command]
fn tool_packages() -> Vec<tools_fetch::PkgStatus> {
    tools_fetch::status(&tools_dir())
}

#[tauri::command]
fn default_mirrors() -> Vec<String> {
    tools_fetch::DEFAULT_MIRRORS.iter().map(|s| s.to_string()).collect()
}

/// 真正往里写文件的目录。
///
/// 装到 `Program Files` 之后，exe 同级的 tools/ 是**不可写**的，
/// 这时自动退到 `%APPDATA%\LanzhuTool\tools` 并记进设置 ——
/// 否则用户点"下载"会直接报一个莫名其妙的拒绝访问。
#[tauri::command]
fn download_target() -> String {
    let cur = tools_dir();
    if dir_writable(&cur) {
        return cur;
    }

    let base = std::env::var("APPDATA")
        .map(std::path::PathBuf::from)
        .unwrap_or_else(|_| std::env::temp_dir())
        .join("LanzhuTool")
        .join("tools");
    let _ = std::fs::create_dir_all(&base);
    let alt = base.to_string_lossy().to_string();

    if alt != cur && !cur.is_empty() {
        let mut s = settings::load();
        s.tools_dir = alt.clone();
        let _ = settings::save(&s);
    }
    alt
}

fn dir_writable(dir: &str) -> bool {
    let p = std::path::Path::new(dir);
    if !p.is_dir() {
        // 不存在的话，看能不能创建
        return std::fs::create_dir_all(p).is_ok();
    }
    let probe = p.join(".lanzhu_write_test");
    match std::fs::File::create(&probe) {
        Ok(_) => {
            let _ = std::fs::remove_file(&probe);
            true
        }
        Err(_) => false,
    }
}

#[tauri::command(rename_all = "camelCase")]
fn download_tools(
    app: tauri::AppHandle,
    ids: Vec<String>,
    tools_dir: String,
    mirrors: Vec<String>,
) -> Result<(), String> {
    tools::reset_cache();
    tools_fetch::download(app, ids, tools_dir, mirrors)
}

#[tauri::command]
fn cancel_tools_download(app: tauri::AppHandle) {
    tools_fetch::cancel(&app)
}

#[tauri::command(rename_all = "camelCase")]
fn import_offline_tools(path: String, tools_dir: String) -> Result<String, String> {
    let r = tools_fetch::import_offline(path, tools_dir)?;
    tools::reset_cache();
    Ok(r)
}

#[tauri::command(rename_all = "camelCase")]
fn export_offline_tools(app: tauri::AppHandle, options: tools_fetch::ExportOptions) -> Result<(), String> {
    tools_fetch::export_offline(app, options)
}

#[tauri::command(rename_all = "camelCase")]
fn list_bundled_tools() -> Vec<String> {
    tools::list_tools(&tools_dir())
}

#[tauri::command(rename_all = "camelCase")]
fn read_text_file(path: String) -> Result<String, String> {
    std::fs::read_to_string(&path).map_err(|e| format!("读取失败：{}", e))
}

#[tauri::command(rename_all = "camelCase")]
fn write_text_file(path: String, content: String) -> Result<(), String> {
    std::fs::write(&path, content).map_err(|e| format!("写入失败：{}", e))
}

/// 枚举 GPU：原版用 WMI 拿到显卡名，再按名字里的 (AMF)/(QSV) 判断厂商。
/// 这里用 PowerShell CIM 取同样的信息，并直接给出后端类型。
#[tauri::command(rename_all = "camelCase")]
fn detect_gpus() -> Vec<GpuInfo> {
    use std::os::windows::process::CommandExt;
    const CREATE_NO_WINDOW: u32 = 0x0800_0000;

    let out = std::process::Command::new("powershell.exe")
        .args([
            "-NoProfile",
            "-NonInteractive",
            "-Command",
            "Get-CimInstance Win32_VideoController | Select-Object -ExpandProperty Name",
        ])
        .creation_flags(CREATE_NO_WINDOW)
        .output();

    let Ok(out) = out else { return vec![] };
    let text = String::from_utf8_lossy(&out.stdout).to_string();

    let mut list = Vec::new();
    for (i, line) in text.lines().enumerate() {
        let name = line.trim();
        if name.is_empty() {
            continue;
        }
        let kind = if name.contains("NVIDIA") {
            "nvenc"
        } else if name.contains("Intel") {
            "qsv"
        } else {
            "amf"
        };
        list.push(GpuInfo {
            index: i as i32,
            label: name.to_string(),
            kind: kind.to_string(),
        });
    }
    if list.is_empty() {
        list.push(GpuInfo {
            index: 0,
            label: "默认GPU".into(),
            kind: "nvenc".into(),
        });
    }
    list
}

/* ================================================================== *
 * 粗剪 / 波形 / 本地媒体
 * ================================================================== */

/// 粗剪命令：视频与音频共用一套 `trim()`。
#[tauri::command(rename_all = "camelCase")]
fn plan_trim(spec: TrimSpec) -> Result<Vec<String>, String> {
    let tools = tools_dir();
    if spec.input.trim().is_empty() {
        return Err("请先选择要剪的文件".into());
    }
    if spec.output.trim().is_empty() {
        return Err("请先指定输出文件".into());
    }
    if spec.end.is_finite() && spec.end > 0.0 && spec.end <= spec.start {
        return Err("终点必须大于起点".into());
    }
    Ok(vec![cmd::trim(&spec, &tools).trim_end().to_string()])
}

/// 生成波形图（返回 data URI）。耗时较长，放到阻塞线程池里跑，不占住 IPC 线程。
#[tauri::command(rename_all = "camelCase")]
async fn make_waveform(
    path: String,
    width: u32,
    height: u32,
    color: String,
) -> Result<String, String> {
    let tools = tools_dir();
    tauri::async_runtime::spawn_blocking(move || {
        media::waveform(&tools, &path, width, height, &color)
    })
    .await
    .map_err(|e| format!("生成波形任务失败：{}", e))?
}

/// 同名字幕探测：导入视频时自动匹配 `.ass/.srt/.ssa/.sub`。
#[tauri::command(rename_all = "camelCase")]
fn detect_subtitle(input: String, lang: String) -> Option<String> {
    let p = find_subtitle_with(&input, &lang);
    if p.is_empty() {
        None
    } else {
        Some(p)
    }
}

/// 把本地绝对路径翻译成 WebView 能加载的 URL。
///
/// 自定义 scheme 在各平台的 URL 形态不同（Windows 是 `http://<scheme>.localhost/`），
/// 所以这件事交给后端决定，前端只管把路径丢进来。
#[tauri::command(rename_all = "camelCase")]
fn media_url(path: String) -> String {
    let encoded = percent_encode(&path.replace('\\', "/"));
    #[cfg(windows)]
    {
        format!("http://{}.localhost/{}", media::SCHEME, encoded)
    }
    #[cfg(not(windows))]
    {
        format!("{}://localhost/{}", media::SCHEME, encoded)
    }
}

fn percent_encode(s: &str) -> String {
    let mut out = String::with_capacity(s.len() * 2);
    for b in s.bytes() {
        let keep = b.is_ascii_alphanumeric() || matches!(b, b'-' | b'_' | b'.' | b'~' | b'/');
        if keep {
            out.push(b as char);
        } else {
            out.push_str(&format!("%{:02X}", b));
        }
    }
    out
}

/* ================================================================== *
 * 系统监控 / 托盘 / 关机
 * ================================================================== */

/// CPU / 内存 / GPU 快照。后台线程每 2 秒刷一次，这里只取现成值。
#[tauri::command]
fn system_stats() -> SysStats {
    sysmon::snapshot()
}

#[tauri::command(rename_all = "camelCase")]
fn hide_to_tray(app: tauri::AppHandle) {
    tray::hide_main(&app);
}

#[tauri::command(rename_all = "camelCase")]
fn show_window(app: tauri::AppHandle) {
    tray::show_main(&app);
}

/// 定时关机。原版的「完成后关机」就是这个，只是以前没接到界面上。
/// 真的执行前还会在界面上给 60 秒反悔时间（见前端 `shutdown /a`）。
#[tauri::command(rename_all = "camelCase")]
fn system_shutdown(seconds: u32) -> Result<(), String> {
    let s = seconds.clamp(10, 3600);
    let mut cmd = std::process::Command::new("shutdown.exe");
    cmd.args(["/s", "/t", &s.to_string()]);
    #[cfg(windows)]
    {
        use std::os::windows::process::CommandExt;
        const CREATE_NO_WINDOW: u32 = 0x0800_0000;
        cmd.creation_flags(CREATE_NO_WINDOW);
    }
    cmd.spawn()
        .map(|_| ())
        .map_err(|e| format!("无法安排关机：{}", e))
}

#[tauri::command(rename_all = "camelCase")]
fn abort_shutdown() -> Result<(), String> {
    let mut cmd = std::process::Command::new("shutdown.exe");
    cmd.arg("/a");
    #[cfg(windows)]
    {
        use std::os::windows::process::CommandExt;
        const CREATE_NO_WINDOW: u32 = 0x0800_0000;
        cmd.creation_flags(CREATE_NO_WINDOW);
    }
    cmd.output().map(|_| ()).map_err(|e| e.to_string())
}

fn main() {
    tauri::Builder::default()
        .plugin(tauri_plugin_dialog::init())
        .plugin(tauri_plugin_opener::init())
        .plugin(tauri_plugin_notification::init())
        // 本地素材（视频预览 / 波形 / 背景图）走自绘协议，见 media.rs
        .register_asynchronous_uri_scheme_protocol(media::SCHEME, media::handle)
        .manage(run::RunState::default())
        .manage(tools_fetch::FetchState::default())
        .setup(|app| {
            if let Some(w) = app.get_webview_window("main") {
                let _ = w.set_title("岚珠工具箱");
            }
            sysmon::start();
            if let Err(e) = tray::init(app.handle()) {
                // 托盘建不起来（极少见）不该拦住整个应用
                eprintln!("托盘初始化失败：{}", e);
            }
            Ok(())
        })
        .on_window_event(|window, event| match event {
            tauri::WindowEvent::CloseRequested { api, .. } => {
                if settings::load().close_to_tray {
                    api.prevent_close();
                    let _ = window.hide();
                }
            }
            tauri::WindowEvent::Resized(_) => {
                // 只有真的最小化了才去读设置文件：拖拽改变尺寸时会高频触发这个事件，
                // 每次都读盘 + 解析 JSON 是白白浪费。
                if window.is_minimized().unwrap_or(false) && settings::load().minimize_to_tray {
                    let _ = window.hide();
                }
            }
            _ => {}
        })
        .invoke_handler(tauri::generate_handler![
            plan_video,
            plan_audio,
            plan_mux,
            plan_extract,
            plan_avs,
            plan_batch,
            plan_trim,
            make_waveform,
            detect_subtitle,
            media_url,
            run_commands,
            cancel_run,
            pause_run,
            probe_media,
            load_settings,
            save_settings,
            resolve_tools_dir,
            resolve_tool,
            tool_packages,
            default_mirrors,
            download_target,
            download_tools,
            cancel_tools_download,
            import_offline_tools,
            export_offline_tools,
            list_bundled_tools,
            read_text_file,
            write_text_file,
            detect_gpus,
            system_stats,
            hide_to_tray,
            show_window,
            system_shutdown,
            abort_shutdown,
        ])
        .run(tauri::generate_context!())
        .expect("启动 Tauri 应用失败");
}
