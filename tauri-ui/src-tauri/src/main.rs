// 发布版不弹控制台窗口
#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

mod cmd;
mod media;
mod meme;
mod probe;
mod run;
mod settings;
mod spec;
mod sysmon;
mod taskbar;
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
 * 「AVS 应用到常规压制全局」
 * ================================================================== */

/// 把 AVS 脚本的源行改指到 `input` 后写到临时目录，返回这个 `.avs` 路径。
///
/// 只有开了开关、脚本非空才返回 `Some`；写盘失败也返回 `None`（宁可照常压，
/// 也不能因为一个临时文件让整个任务起不来）。
fn write_global_avs(script: &str, input: &str, stem: &str) -> Option<String> {
    if script.trim().is_empty() || input.trim().is_empty() {
        return None;
    }
    let path = std::path::Path::new(&temp_dir()).join(format!("{}_global.avs", stem));
    let body = cmd::retarget_avs_source(script, input);
    std::fs::write(&path, body).ok()?;
    Some(path.to_string_lossy().to_string())
}

fn stem_of(path: &str) -> String {
    std::path::Path::new(path)
        .file_stem()
        .map(|s| s.to_string_lossy().to_string())
        .unwrap_or_else(|| "input".into())
}

/// 这一趟该不该走 AVS：该走就返回生成的 `.avs` 路径（画面的新来源）。
fn avs_override(spec: &VideoSpec) -> Option<String> {
    if !spec.avs_apply {
        return None;
    }
    write_global_avs(&spec.avs_script, &spec.input, &stem_of(&spec.input))
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

    // 开了「应用到常规压制全局」：画面走脚本生成的 .avs，音频仍从源文件抽
    let avs = avs_override(&spec);
    let video_input = avs.clone().unwrap_or_else(|| spec.input.clone());
    let sub = if avs.is_some() { "" } else { spec.subtitle.as_str() };

    let bat = cmd::video_pipeline(
        &spec,
        &a,
        &tools,
        &tp,
        &spec.input,
        &video_input,
        &spec.output,
        sub,
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

    // 原版 `btnAVS9_Click`：「压制音频」时音轨是从**源视频**抽的，不是从 .avs。
    let src = if spec.source.trim().is_empty() {
        cmd::avs_source_path(&spec.script).unwrap_or_default()
    } else {
        spec.source.clone()
    };
    let (has_audio, audio_format) = if spec.with_audio && !src.trim().is_empty() {
        let info = probe::probe(&tools, &src);
        (
            info.audio.is_some(),
            info.audio.map(|a| a.codec).unwrap_or_default(),
        )
    } else {
        (false, String::new())
    };

    let mut a = audio;
    a.input = if has_audio { src.clone() } else { spec.script_path.clone() };

    // 临时文件按源视频命名（原版用的就是 `namevideo9`）
    let input = if src.trim().is_empty() { &spec.script_path } else { &src };

    let bat = cmd::video_pipeline(
        &spec.spec,
        &a,
        &tools,
        &tp,
        input,
        &spec.script_path,
        &spec.spec.output,
        "",
        has_audio,
        &audio_format,
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

    let suffix = cmd::video_suffix(&spec);
    let ext = cmd::container_ext(&spec);

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
        let mut sub = if embed_subtitle {
            find_subtitle(input)
        } else {
            String::new()
        };

        // 开了「应用到常规压制全局」：这个文件的画面改走它自己的 .avs
        let video_input = if spec.avs_apply {
            match write_global_avs(&spec.avs_script, input, &stem) {
                Some(p) => {
                    sub = String::new(); // 字幕已写进脚本，别再叠一次
                    p
                }
                None => input.clone(),
            }
        } else {
            input.clone()
        };

        let mut a = audio.clone();
        a.input = input.clone();

        let bat = cmd::video_pipeline(
            &spec,
            &a,
            &tools,
            &tp,
            input,
            &video_input,
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
 * 默认输出文件名
 *
 * 原版这些规则写在各个 `*_TextChanged` 里，换壳时丢了 —— 表现就是
 * 「输出名 = 输入名」，点开始会把源文件覆盖掉。规则本体在 `cmd` 里。
 * ================================================================== */

/// 单文件压制：`<源名>_<h264|hevc|mov|flv|预设名><扩展名>`，重名则退到 `_new_file(n)_…`。
#[tauri::command(rename_all = "camelCase")]
fn default_video_output(spec: VideoSpec) -> String {
    if spec.input.trim().is_empty() {
        return String::new();
    }
    cmd::default_video_output(&spec)
}

/// 音频：`_AAC.mp4` / `_WAV.wav` / `_FLAC.flac` …… 跟着编码器走。
#[tauri::command(rename_all = "camelCase")]
fn default_audio_output(input: String, encoder: usize) -> String {
    if input.trim().is_empty() {
        return String::new();
    }
    cmd::default_audio_output(&input, encoder)
}

/// 封装：`_Mux.mp4`。
#[tauri::command(rename_all = "camelCase")]
fn default_mux_output(video: String) -> String {
    if video.trim().is_empty() {
        return String::new();
    }
    cmd::default_mux_output(&video)
}

/// AVS：脚本里 `Source("…")` 指到的源文件旁出 `_AVS.mp4`。
#[tauri::command(rename_all = "camelCase")]
fn default_avs_output(source: String) -> String {
    if source.trim().is_empty() {
        return String::new();
    }
    cmd::default_avs_output(&source)
}

/* ================================================================== *
 * 插帧 / 超分
 * ================================================================== */

/// 拼插帧 / 超分的命令行。
///
/// 源信息（帧率 / 分辨率 / 有无音轨）在这里现探：这些值直接决定命令怎么拼
/// （`minterpolate` 要源帧率、`scale` 要源分辨率、音轨要不要 `-map`），
/// 让前端把探测结果传回来反而多一条可能不一致的路径。
#[tauri::command(rename_all = "camelCase")]
fn plan_enhance(spec: EnhanceSpec) -> Result<Vec<String>, String> {
    let tools = tools_dir();
    if spec.input.trim().is_empty() {
        return Err("请先选择要处理的视频".into());
    }
    if spec.output.trim().is_empty() {
        return Err("请先指定输出文件".into());
    }
    if !spec.interp && !spec.upscale {
        return Err("插帧和超分至少要开一个".into());
    }

    let info = probe::probe(&tools, &spec.input);
    if !info.exists {
        return Err("源文件不存在或无法读取".into());
    }
    let v = info.video.unwrap_or_default();
    let src = cmd::EnhanceSource {
        fps: v.fps,
        width: v.width,
        height: v.height,
        has_audio: info.audio.is_some(),
    };

    let lines = cmd::enhance_pipeline(&spec, &src, &tools, &temp_dir());
    if lines.is_empty() {
        return Err("没有可执行的步骤：请选择插帧或超分的引擎".into());
    }
    Ok(lines)
}

/// 插帧 / 超分的默认输出：源文件旁边（重名会退到 `_new_file(n)`）。
#[tauri::command(rename_all = "camelCase")]
fn default_enhance_output(spec: EnhanceSpec) -> String {
    if spec.input.trim().is_empty() {
        return String::new();
    }
    cmd::default_enhance_output(&spec)
}

/// 扫出跟某个可执行文件同级的子目录（权重目录都不是可执行文件，只能扫）。
fn scan_subdirs(dir: &std::path::Path, prefix: &str) -> Vec<String> {
    let mut out: Vec<String> = Vec::new();
    if let Ok(rd) = std::fs::read_dir(dir) {
        for e in rd.flatten() {
            if !e.path().is_dir() {
                continue;
            }
            if let Some(n) = e.file_name().to_str() {
                if n.to_lowercase().starts_with(prefix) {
                    out.push(n.to_string());
                }
            }
        }
    }
    out.sort();
    out
}

/// 扫 `models/*.param` 得到 realesrgan 的模型名。
/// 文件名形如 `realesr-animevideov3-x2.param`，要先把 `-x2` 这次级后缀去掉。
fn scan_esr_models(dir: &std::path::Path) -> Vec<String> {
    let mut out: Vec<String> = Vec::new();
    if let Ok(rd) = std::fs::read_dir(dir) {
        for e in rd.flatten() {
            let Some(name) = e.file_name().to_str().map(|s| s.to_string()) else {
                continue;
            };
            let Some(stem) = name.strip_suffix(".param") else {
                continue;
            };
            let base = match stem.rsplit_once("-x") {
                Some((b, s)) if s.len() == 1 && s.chars().all(|c| c.is_ascii_digit()) => b,
                _ => stem,
            };
            if !out.iter().any(|x| x == base) {
                out.push(base.to_string());
            }
        }
    }
    out.sort();
    out
}

fn model_tool(tools: &str, exe_name: &str, id: &str, name: &str, hint: &str) -> (spec::ModelTool, std::path::PathBuf) {
    let path = tools::tool(tools, exe_name);
    let p = std::path::PathBuf::from(&path);
    let ok = p.is_file();
    let dir = p
        .parent()
        .map(|d| d.to_path_buf())
        .unwrap_or_else(|| std::path::PathBuf::from("."));
    (
        spec::ModelTool {
            id: id.into(),
            name: name.into(),
            exe: if ok { path } else { String::new() },
            models: Vec::new(),
            custom_frames: Vec::new(),
            hint: hint.into(),
        },
        dir,
    )
}

/// 探测三个本地模型工具是否就绪，以及各自有哪些权重。
///
/// 只认「可执行文件同级目录」这个约定（就是官方发布的压缩包解出来的形状），
/// 所以用户把 zip 原样解到 tools/ 下就能被认出来，不需要手工整理。
#[tauri::command(rename_all = "camelCase")]
fn enhance_tools() -> Vec<spec::ModelTool> {
    let tools = tools_dir();
    let mut out = Vec::new();

    let (mut rife, rife_dir) = model_tool(
        &tools,
        "rife-ncnn-vulkan.exe",
        "rife",
        "RIFE（插帧）",
        "任意 Vulkan 显卡都能跑，不需要 CUDA；2 倍插帧时不传 -n，因此 rife-anime 也能用",
    );
    if !rife.exe.is_empty() {
        rife.models = scan_subdirs(&rife_dir, "rife");
        // rife 的源码里是 `model.find("rife-v4")` 才允许自定义帧数
        rife.custom_frames = rife
            .models
            .iter()
            .filter(|m| m.contains("rife-v4"))
            .cloned()
            .collect();
    }
    out.push(rife);

    let (mut esr, esr_dir) = model_tool(
        &tools,
        "realesrgan-ncnn-vulkan.exe",
        "realesrgan",
        "Real-ESRGAN（超分）",
        "通用/动画两套权重；realesrgan-x4plus 系只有 4 倍",
    );
    if !esr.exe.is_empty() {
        esr.models = scan_esr_models(&esr_dir.join("models"));
    }
    out.push(esr);

    let (mut cugan, cugan_dir) = model_tool(
        &tools,
        "realcugan-ncnn-vulkan.exe",
        "realcugan",
        "Real-CUGAN（超分）",
        "动画向，带降噪；models-se 最均衡，models-nose 只有 2 倍",
    );
    if !cugan.exe.is_empty() {
        cugan.models = scan_subdirs(&cugan_dir, "models");
    }
    out.push(cugan);

    out
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

/// `tools/avs/plugins` 里有哪些外置滤镜 / 脚本（AVS 页底下那个框）。
///
/// 只读一层目录：这个目录里是平铺的 dll/avsi，深挖没意义；
/// AviSynth.dll 在上一层（`avs/`），单独探一下。
#[tauri::command(rename_all = "camelCase")]
fn list_avs_plugins() -> AvsPlugins {
    let avs_dir = std::path::Path::new(&tools_dir()).join("avs");
    let plugins_dir = avs_dir.join("plugins");
    let mut plugins: Vec<AvsPlugin> = Vec::new();

    if let Ok(rd) = std::fs::read_dir(&plugins_dir) {
        for entry in rd.flatten() {
            let path = entry.path();
            if !path.is_file() {
                continue;
            }
            let Some(name) = path.file_name().and_then(|s| s.to_str()) else {
                continue;
            };
            let ext = path
                .extension()
                .and_then(|s| s.to_str())
                .unwrap_or("")
                .to_ascii_lowercase();
            let kind = match ext.as_str() {
                "dll" => "filter",
                "avs" | "avsi" => "script",
                _ => continue,
            };
            plugins.push(AvsPlugin {
                name: name.to_string(),
                kind: kind.to_string(),
            });
        }
    }

    plugins.sort_by(|a, b| {
        // 滤镜在前、脚本在后，同类按名字排（和原版下拉框的观感一致）
        a.kind.cmp(&b.kind).then_with(|| a.name.cmp(&b.name))
    });

    AvsPlugins {
        dir: plugins_dir.to_string_lossy().to_string(),
        avisynth: avs_dir.join("AviSynth.dll").is_file(),
        plugins,
    }
}

#[tauri::command(rename_all = "camelCase")]
fn read_text_file(path: String) -> Result<String, String> {
    std::fs::read_to_string(&path).map_err(|e| format!("读取失败：{}", e))
}
#[tauri::command(rename_all = "camelCase")]
fn write_text_file(path: String, content: String) -> Result<(), String> {
    std::fs::write(&path, content).map_err(|e| format!("写入失败：{}", e))
}

/// 用系统默认程序打开一个本地文件。
///
/// 原版里双击路径框就能打开对应的文件（`txtvideo_MouseDoubleClick` 那批），
/// 前端用的是「自绘标题栏 + 无边框窗口」，走系统 shell 最省事；
/// 在这里调插件而不是从前端调，是为了不额外配一套路径作用域。
#[tauri::command(rename_all = "camelCase")]
fn open_local(app: tauri::AppHandle, path: String) -> Result<(), String> {
    use tauri_plugin_opener::OpenerExt;
    if !std::path::Path::new(&path).exists() {
        return Err("文件不存在".into());
    }
    app.opener()
        .open_path(path, None::<&str>)
        .map_err(|e| e.to_string())
}

/// 枚举 GPU：原版用 WMI 拿到显卡名，再按名字里的 (AMF)/(QSV) 判断厂商。
/// 这里用 PowerShell CIM 取同样的信息，并直接给出后端类型。
///
/// 结果**缓存到进程退出**：这玩意要起一个 PowerShell（几百毫秒起），
/// 而视频页在挂载时就会问一次 —— 不缓存的话每次冷启动都要白等一下。
#[tauri::command(rename_all = "camelCase")]
fn detect_gpus() -> Vec<GpuInfo> {
    static CACHE: std::sync::OnceLock<Vec<GpuInfo>> = std::sync::OnceLock::new();
    CACHE.get().cloned().unwrap_or_else(|| {
        let list = detect_gpus_uncached();
        let _ = CACHE.set(list.clone());
        list
    })
}

fn detect_gpus_uncached() -> Vec<GpuInfo> {
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

/// 彩蛋：随机烂梗。网络请求放到阻塞线程池，别把 IPC 线程占住。
#[tauri::command(rename_all = "camelCase")]
async fn random_meme(url: String) -> Meme {
    tauri::async_runtime::spawn_blocking(move || meme::fetch(&url))
        .await
        .unwrap_or_default()
}

/// 批量封装 / 容器转换（原版 `btnBatchMP4_Click`）。
///
/// 目标格式和源文件相同就跳过；源音轨不是 AAC 且目标不是 mkv 时顺手转 AAC。
#[tauri::command(rename_all = "camelCase")]
fn plan_batch_mux(spec: BatchMuxSpec) -> Result<Vec<String>, String> {
    let tools = tools_dir();
    if spec.inputs.is_empty() {
        return Err("请先添加要转换的视频".into());
    }
    let target = spec.format.trim().to_lowercase();
    if target.is_empty() {
        return Err("请选择目标容器".into());
    }

    let mut all: Vec<String> = Vec::new();
    for input in spec.inputs.iter() {
        if input.trim().is_empty() {
            continue;
        }
        let ext = std::path::Path::new(input)
            .extension()
            .map(|e| e.to_string_lossy().to_lowercase())
            .unwrap_or_default();
        if ext == target {
            continue; // 已经是目标格式
        }

        let out = cmd::convert_output(input, &target, &spec.output_dir);
        let info = probe::probe(&tools, input);
        let audio = info
            .audio
            .map(|a| a.codec.to_lowercase())
            .unwrap_or_default();
        // 原版规则：音轨不是 AAC 且目标不是 mkv → 转 AAC（mkv 什么都能装，不用转）
        let transcode = !audio.is_empty() && audio != "aac" && target != "mkv";
        all.push(
            cmd::convert_container_cmd(
                &tools,
                input,
                &out,
                &target,
                &spec.aac_encoder,
                transcode,
            )
            .trim_end()
            .to_string(),
        );
    }

    if all.is_empty() {
        return Err("这些文件已经是目标格式了，无需转换".into());
    }
    Ok(all)
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
                // 任务栏进度条画在窗口的任务栏按钮上，句柄给 taskbar 模块存一份
                if let Ok(h) = w.hwnd() {
                    taskbar::set_hwnd(h.0 as isize);
                }
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
                // 默认：点 ✕ 就是退出。只有开了「托盘模式」才收进托盘，
                // 免得用户点了关闭却找不到窗口、进程还赖着不走。
                if settings::load().minimize_to_tray {
                    api.prevent_close();
                    let _ = window.hide();
                } else {
                    window.app_handle().exit(0);
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
            plan_enhance,
            plan_batch,
            plan_trim,
            plan_batch_mux,
            make_waveform,
            detect_subtitle,
            media_url,
            default_video_output,
            default_audio_output,
            default_mux_output,
            default_avs_output,
            default_enhance_output,
            enhance_tools,
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
            list_avs_plugins,
            read_text_file,
            write_text_file,
            open_local,
            detect_gpus,
            system_stats,
            hide_to_tray,
            show_window,
            random_meme,
            system_shutdown,
            abort_shutdown,
        ])
        .run(tauri::generate_context!())
        .expect("启动 Tauri 应用失败");
}
