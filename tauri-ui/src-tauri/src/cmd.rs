//! 命令行拼装 —— 逐字镜像原版 `mp4box/MainForm.cs` 里的引擎方法。
//!
//! 原版的表格：
//!   `ffmuxbat`                    → [`ffmuxbat`]
//!   `BuildFfmpegVideoCommand`     → [`build_video`]（`x264bat`/`x265bat` 都是它的薄封装）
//!   `audiobat`                    → [`audiobat`]
//!   `ExtractAudio`                → [`extract_audio`]
//!   `VideoBatch`                  → [`video_pipeline`]
//!   `btnmux_Click`                → [`mux`]
//!   `ExtractTrack` / `ExtractAV`  → [`extract`]
//!
//! 每个函数末尾都补 `\r\n`，和原版一致（原版把多行命令喂给 WorkingForm 再写进 .bat）。

use crate::spec::{AudioSpec, EnhanceSpec, ExtractSpec, MuxSpec, TrimSpec, VideoSpec};
use crate::tools::{quote, tool};

/// 数字去掉多余的小数尾巴：`23.5` → "23.5"、`800.0` → "800"。
fn n(v: f64) -> String {
    if (v.fract()).abs() < f64::EPSILON {
        format!("{}", v as i64)
    } else {
        format!("{}", v)
    }
}

#[derive(PartialEq, Clone, Copy)]
pub enum PresetKind {
    H264,
    Hevc,
    Mov,
    Flv,
}

/// 镜像原版 `GetSelectedVideoPresetKind`。
pub fn preset_kind(format: &str) -> PresetKind {
    let t = format.to_lowercase();
    if t.contains("hevc") {
        PresetKind::Hevc
    } else if t.contains("mov") {
        PresetKind::Mov
    } else if t.contains("flv") {
        PresetKind::Flv
    } else {
        PresetKind::H264
    }
}

/// 镜像原版 `GetSelectedVideoBitDepth`。注意必须先判 12，否则 "12bit" 会被 "1" 前缀误命中。
pub fn preset_bit_depth(format: &str) -> i64 {
    let t = format.to_lowercase();
    if t.contains("12bit") {
        12
    } else if t.contains("10bit") {
        10
    } else {
        8
    }
}

pub fn output_ext(format: &str) -> &'static str {
    match preset_kind(format) {
        PresetKind::Mov => ".mov",
        PresetKind::Flv => ".flv",
        _ => ".mp4",
    }
}

pub fn temp_ext(format: &str) -> &'static str {
    match preset_kind(format) {
        PresetKind::Hevc => ".hevc",
        PresetKind::Mov => ".mov",
        PresetKind::Flv => ".flv",
        _ => ".mp4",
    }
}

/// 镜像原版 `getAudioExt`。
/// 输出扩展名：预设模式跟预设的容器走，否则按「压制格式」推。
pub fn container_ext(spec: &VideoSpec) -> String {
    if spec.mode == 3 && !spec.preset_container.trim().is_empty() {
        format!(
            ".{}",
            spec.preset_container.trim().trim_start_matches('.').to_lowercase()
        )
    } else {
        output_ext(&spec.format).to_string()
    }
}

/// 中间临时视频文件的扩展名（预设模式用容器扩展名；否则沿用原版那张表）。
pub fn temp_container_ext(spec: &VideoSpec) -> String {
    if spec.mode == 3 && !spec.preset_container.trim().is_empty() {
        container_ext(spec)
    } else {
        temp_ext(&spec.format).to_string()
    }
}

pub fn audio_ext(encoder: usize) -> &'static str {
    match encoder {
        0 => ".mp4",
        1 => ".m4a",
        2 => ".wav",
        3 => ".m4a",
        4 => ".flac",
        5 => ".m4a",
        6 => ".ac3",
        _ => ".aac",
    }
}

/* ------------------------------------------------------------------ *
 * 默认输出文件名 —— 原版这些规则都写在各个 `*_TextChanged` 里，
 * 换壳的时候只搬了命令行、把命名丢了，结果是"输出名和输入名一模一样"，
 * 点开始就会把源文件覆盖掉。这里逐条搬回来。
 * ------------------------------------------------------------------ */

/// 在源文件旁边拼一个"带后缀"的名字：`<目录>\<文件名><后缀>`。
///
/// 后缀是**含扩展名的整段**（如 `_h264.mp4`），因为 `Util.ChangeExt` 就是
/// 把扩展名整体换掉：`ChangeExt("1.mp4", "_h264.mp4")` → `1_h264.mp4`。
pub fn beside(input: &str, suffix_with_ext: &str) -> String {
    let p = std::path::Path::new(input);
    let stem = p
        .file_stem()
        .map(|s| s.to_string_lossy().to_string())
        .unwrap_or_else(|| "output".into());
    let dir = p
        .parent()
        .map(|d| d.to_path_buf())
        .unwrap_or_else(|| std::path::PathBuf::from("."));
    dir.join(format!("{}{}", stem, suffix_with_ext))
        .to_string_lossy()
        .to_string()
}

/// 镜像原版 `GetSelectedVideoOutputSuffix`。
/// 预设模式（mode 3）用预设名，其余按"压制格式"推。
pub fn video_suffix(spec: &VideoSpec) -> String {
    if spec.mode == 3 && !spec.preset_name.trim().is_empty() {
        let s: String = spec
            .preset_name
            .chars()
            .filter(|c| !"\\/:*?\"<>|".contains(*c))
            .collect();
        return s.replace(' ', "_");
    }
    match preset_kind(&spec.format) {
        PresetKind::Hevc => "hevc".to_string(),
        PresetKind::Mov => "mov".to_string(),
        PresetKind::Flv => "flv".to_string(),
        PresetKind::H264 => "h264".to_string(),
    }
}

/// 镜像原版 `x264VideoTextBox_TextChanged`：
/// `1.mp4` --h264--> `1_h264.mp4`；这个名字已被占用时退到
/// `1_new_file(1)_h264.mp4`、`1_new_file(2)_h264.mp4`……
pub fn default_video_output(spec: &VideoSpec) -> String {
    let suffix = video_suffix(spec);
    let ext = container_ext(spec);
    let mut out = beside(&spec.input, &format!("_{}{}", suffix, ext));

    let mut n = 1;
    // 原版的循环条件：等于输入文件、或文件已存在
    while out.eq_ignore_ascii_case(spec.input.trim()) || std::path::Path::new(&out).exists() {
        out = beside(
            &spec.input,
            &format!("_new_file({})_{}{}", n, suffix, ext),
        );
        n += 1;
    }
    out
}

/// 镜像原版 `AudioEncoderComboBox_SelectedIndexChanged` 与音频页刷新用的那张表。
pub fn default_audio_output(input: &str, encoder: usize) -> String {
    let suffix = match encoder {
        0 => "_AAC.mp4",
        1 => "_AAC.m4a",
        2 => "_WAV.wav",
        3 => "_ALAC.m4a",
        4 => "_FLAC.flac",
        5 => "_AAC.m4a",
        6 => "_AC3.ac3",
        _ => "_AAC.aac",
    };
    beside(input, suffix)
}

/// 镜像原版封装页：`_Mux.mp4`。
pub fn default_mux_output(video: &str) -> String {
    beside(video, "_Mux.mp4")
}

/// 镜像原版 `txtAVS_TextChanged`：从脚本里 `Source("...")` 指到的源文件旁出 `_AVS.mp4`。
pub fn default_avs_output(source: &str) -> String {
    beside(source, "_AVS.mp4")
}

/// 镜像原版 `EscapeFfmpegFilterPath`：subtitles 滤镜用 ':' 分隔参数，
/// 所以 Windows 盘符的冒号即使加了引号也必须转义。
fn escape_filter_path(path: &str) -> String {
    let p = path.replace('\\', "/").replace(':', "\\:").replace('\'', "\\'");
    format!("'{}'", p)
}

/// 镜像原版 `ffmuxbat`。
pub fn ffmuxbat(tools: &str, input1: &str, input2: &str, output: &str) -> String {
    format!(
        "{} -i \"{}\" -i \"{}\" -sn -map 0:v -map 1:a -c copy -y \"{}\"\r\n",
        quote(&tool(tools, "ffmpeg.exe")),
        input1,
        input2,
        output
    )
}

/// 镜像原版 `BuildFfmpegVideoCommand`。
///
/// `pass`：0 = 单遍，1/2 = 二遍法的一遍/二遍。原版调用时传 0 表示"不是二遍"。
pub fn build_video(
    spec: &VideoSpec,
    tools: &str,
    input: &str,
    output: &str,
    pass: u32,
    sub: &str,
) -> String {
    let kind = preset_kind(&spec.format);
    let bit_depth = preset_bit_depth(&spec.format);
    let use_hevc = kind == PresetKind::Hevc;
    // 预设模式（mode = 3）自己带编码器与参数：这时软件/GPU 的分支全部让位，
    // 否则会和预设里的 `-c:v` / `-pix_fmt` 打架。
    let preset_mode = spec.mode == 3 && !spec.preset_encoder.trim().is_empty();
    let use_gpu = (spec.use_gpu || spec.hybrid) && !preset_mode;

    // ---- GPU 编码器选择（镜像原版的厂商分支）----
    let mut gpu_encoder: Option<&'static str> = None;
    let mut hwaccel: Option<&'static str> = None;
    let mut hwaccel_fmt: Option<&'static str> = None;
    if use_gpu {
        match spec.gpu_kind.as_str() {
            "amf" => {
                gpu_encoder = Some(if use_hevc { "hevc_amf" } else { "h264_amf" });
                hwaccel = Some("d3d11va");
                hwaccel_fmt = Some("d3d11");
            }
            "qsv" => {
                gpu_encoder = Some(if use_hevc { "hevc_qsv" } else { "h264_qsv" });
                hwaccel = Some("qsv");
                hwaccel_fmt = Some("qsv");
            }
            _ => {
                gpu_encoder = Some(if use_hevc { "hevc_nvenc" } else { "h264_nvenc" });
                hwaccel = Some("cuda");
                hwaccel_fmt = Some("cuda");
            }
        }
    }
    let encoder: &str = if preset_mode {
        spec.preset_encoder.trim()
    } else if use_gpu {
        gpu_encoder.unwrap()
    } else if use_hevc {
        "libx265"
    } else {
        "libx264"
    };
    let g = gpu_encoder.unwrap_or("");

    let mut sb = String::new();
    sb.push_str(&quote(&tool(tools, "ffmpeg.exe")));

    if use_gpu {
        if let Some(h) = hwaccel {
            sb.push_str(" -hwaccel ");
            sb.push_str(h);
            if let Some(f) = hwaccel_fmt {
                sb.push_str(" -hwaccel_output_format ");
                sb.push_str(f);
            }
        }
    }

    // 多 GPU 设备选择：NVENC 用 -gpu（WMI 序号）、QSV 用 -qsv_device、AMF 用 -adapter
    if use_gpu && spec.gpu_index >= 0 {
        if g.contains("nvenc") {
            sb.push_str(&format!(" -gpu {}", spec.gpu_index));
        } else if g.contains("qsv") {
            sb.push_str(&format!(" -qsv_device {}", spec.gpu_index));
        } else if g.contains("amf") && spec.gpu_index > 0 {
            sb.push_str(&format!(" -adapter {}", spec.gpu_index));
        }
    }

    sb.push_str(" -y -i \"");
    sb.push_str(input);
    sb.push('"');
    sb.push_str(" -an -sn");

    if spec.seek != 0 {
        sb.push_str(&format!(" -ss {}", spec.seek));
    }

    let threads = spec.threads.trim().to_lowercase();
    if threads.is_empty() || threads == "0" || threads == "auto" {
        sb.push_str(" -threads 0");
    } else if let Ok(t) = threads.parse::<i64>() {
        if t > 0 {
            sb.push_str(&format!(" -threads {}", t));
        } else {
            sb.push_str(" -threads 0");
        }
    } else {
        sb.push_str(" -threads 0");
    }

    // ---- 像素格式 ----
    let mut pix = "yuv420p".to_string();
    if encoder == "libx265" {
        if bit_depth == 12 {
            pix = "yuv420p12le".into();
        } else if bit_depth == 10 {
            pix = "yuv420p10le".into();
        }
    } else if g.contains("hevc_") && bit_depth == 10 {
        pix = "p010le".into();
    }
    // GPU 硬件加速时绝不能加 -pix_fmt：硬解输出的硬件帧会被自动插入的
    // auto_scale 滤镜尝试转成软件帧而失败（-40 Function not implemented）。
    // 预设模式也不加：像素格式该写在预设参数里（ProRes 要 yuv422p10le 这种）。
    if !use_gpu && !preset_mode {
        sb.push_str(&format!(" -pix_fmt {}", pix));
    }

    // ---- 滤镜链 ----
    let mut filters: Vec<String> = Vec::new();
    let scale = spec.width != 0 && spec.height != 0 && !spec.maintain_resolution;
    let need_cpu_filters = scale || !sub.is_empty();
    let mut hw_head = 0usize;

    if use_gpu && need_cpu_filters {
        filters.push("hwdownload".into());
        hw_head += 1;
        let f = if g.contains("h264_") {
            "format=nv12".to_string()
        } else if g.contains("hevc_") {
            if bit_depth == 10 {
                "format=p010le".to_string()
            } else {
                "format=yuv420p".to_string()
            }
        } else if encoder == "libx265" {
            if bit_depth == 12 {
                "format=yuv420p12le".to_string()
            } else if bit_depth == 10 {
                "format=yuv420p10le".to_string()
            } else {
                "format=yuv420p".to_string()
            }
        } else {
            "format=yuv420p".to_string()
        };
        filters.push(f);
        hw_head += 1;
    }

    if scale {
        filters.push(format!(
            "scale={}:{}:flags=lanczos",
            spec.width, spec.height
        ));
    }
    if !sub.is_empty() {
        filters.push(format!("subtitles={}", escape_filter_path(sub)));
    }

    if use_gpu && need_cpu_filters && filters.len() > hw_head {
        filters.push(if hwaccel == Some("cuda") {
            "hwupload_cuda".into()
        } else {
            "hwupload".into()
        });
    } else if use_gpu && !need_cpu_filters {
        filters.clear();
    }

    if !filters.is_empty() {
        sb.push_str(" -vf \"");
        sb.push_str(&filters.join(","));
        sb.push('"');
    }

    sb.push_str(" -c:v ");
    sb.push_str(encoder);

    // ---- 质量 / 码率 ----
    match spec.mode {
        0 => {
            if !spec.custom_params.trim().is_empty() {
                sb.push(' ');
                sb.push_str(spec.custom_params.trim());
            }
        }
        3 => {
            // 预设：参数原样拼上（包含 -profile:v / -qscale:v / -pix_fmt 这些）
            if !spec.preset_params.trim().is_empty() {
                sb.push(' ');
                sb.push_str(spec.preset_params.trim());
            }
        }
        1 => {
            let v = n(spec.crf);
            if g.contains("amf") {
                sb.push_str(&format!(" -rc cqp -qp {}", v));
            } else if g.contains("qsv") {
                sb.push_str(&format!(" -global_quality {}", v));
            } else if use_gpu {
                sb.push_str(&format!(" -rc vbr -cq {}", v));
            } else {
                sb.push_str(&format!(" -crf {}", v));
            }
        }
        2 => {
            sb.push_str(&format!(" -pass {} -b:v {}k", pass, n(spec.bitrate)));
            if g.contains("amf") {
                sb.push_str(" -rc vbr_peak");
            } else if use_gpu && !g.contains("qsv") {
                sb.push_str(" -rc vbr");
            }
        }
        _ => {}
    }

    if spec.mode == 1 || spec.mode == 2 {
        if !spec.extra_params.trim().is_empty() {
            sb.push(' ');
            sb.push_str(spec.extra_params.trim());
        } else if use_gpu {
            if g.contains("amf") {
                sb.push_str(" -quality balanced");
            } else if g.contains("qsv") {
                sb.push_str(" -preset medium");
            } else {
                sb.push_str(" -preset p5");
            }
        } else if use_hevc {
            sb.push_str(" -preset medium");
        } else {
            sb.push_str(" -preset fast");
        }
    }

    if spec.frames != 0 {
        sb.push_str(&format!(" -frames:v {}", spec.frames));
    }

    if spec.mode == 2 && pass == 1 {
        sb.push_str(" -f null NUL");
    } else if !output.is_empty() {
        sb.push_str(" \"");
        sb.push_str(output);
        sb.push('"');
    }

    sb.push_str("\r\n");
    sb
}

/// 镜像原版 `audiobat`。返回单条（可能含管道）命令。
pub fn audiobat(spec: &AudioSpec, tools: &str) -> String {
    let ffmpeg = tool(tools, "ffmpeg.exe");
    let input = &spec.input;
    let mut output = spec.output.clone();

    let ffmpeg_pipe = format!(
        "{} -i \"{}\" -vn -sn -v 0 -c:a pcm_s16le -f wav pipe:|",
        quote(&ffmpeg),
        input
    );

    let cmd = match spec.encoder {
        0 => {
            let exe = quote(&tool(tools, "neroAacEnc.exe"));
            if spec.use_bitrate {
                let br = 1000 * spec.bitrate.trim().parse::<i64>().unwrap_or(128);
                format!(
                    "{}{} -ignorelength -lc -br {} -if - -of \"{}\"",
                    ffmpeg_pipe, exe, br, output
                )
            } else {
                format!(
                    "{}{} -ignorelength {} -if - -of \"{}\"",
                    ffmpeg_pipe,
                    exe,
                    spec.custom_params.trim(),
                    output
                )
            }
        }
        1 => {
            let exe = quote(&tool(tools, "qaac.exe"));
            if spec.use_bitrate {
                format!(
                    "{}{} -q 2 --ignorelength -c {} - -o \"{}\"",
                    ffmpeg_pipe,
                    exe,
                    spec.bitrate.trim(),
                    output
                )
            } else {
                format!(
                    "{}{} --ignorelength {} - -o \"{}\"",
                    ffmpeg_pipe,
                    exe,
                    spec.custom_params.trim(),
                    output
                )
            }
        }
        2 => {
            // WAV：注意原版把输出扩展名强制改成 .wav，并且不走管道
            if output.to_lowercase().ends_with(".aac") {
                let stem = output[..output.len() - 4].to_string();
                output = format!("{}.wav", stem);
            }
            format!("{} -y -i \"{}\" -f wav \"{}\"", quote(&ffmpeg), input, output)
        }
        3 => format!(
            "{}{} --ignorelength - -o \"{}\"",
            ffmpeg_pipe,
            quote(&tool(tools, "refalac.exe")),
            output
        ),
        4 => format!(
            "{}{} -f --ignore-chunk-sizes -5 - -o \"{}\"",
            ffmpeg_pipe,
            quote(&tool(tools, "flac.exe")),
            output
        ),
        5 => {
            let exe = quote(&tool(tools, "fdkaac.exe"));
            if spec.use_bitrate {
                format!(
                    "{}{} --ignorelength -b {} - -o \"{}\"",
                    ffmpeg_pipe,
                    exe,
                    spec.bitrate.trim(),
                    output
                )
            } else {
                format!(
                    "{}{} --ignorelength {} - -o \"{}\"",
                    ffmpeg_pipe,
                    exe,
                    spec.custom_params.trim(),
                    output
                )
            }
        }
        6 => format!(
            "{} -i \"{}\" -c:a ac3 -b:a {}k \"{}\"",
            quote(&ffmpeg),
            input,
            spec.bitrate.trim(),
            output
        ),
        _ => String::new(),
    };

    format!("{}\r\n", cmd)
}

/// 镜像原版 `ExtractAudio`（`-c:a copy` 直接抽取音轨）。
pub fn extract_audio(tools: &str, input: &str, outfile: &str, stream_index: i64) -> String {
    format!(
        "{}{} -i {} -vn -sn -c:a copy -y -map 0:a:{} {}\r\n",
        quote(&tool(tools, "ffmpeg.exe")),
        "",
        quote(input),
        stream_index,
        quote(outfile)
    )
}

/// 镜像原版 `VideoBatch` —— 视频压制的主链路。
///
/// 顺序：抽/压音频 → 压视频 → 封装 → 删临时文件 → 打完成标记。
/// `has_audio` / `audio_format` 需要调用方先用 [`crate::probe`] 探好再传进来。
pub fn video_pipeline(
    spec: &VideoSpec,
    audio: &AudioSpec,
    tools: &str,
    temp_dir: &str,
    input: &str,
    output: &str,
    sub: &str,
    has_audio: bool,
    audio_format: &str,
) -> String {
    let input_name = std::path::Path::new(input)
        .file_stem()
        .map(|s| s.to_string_lossy().to_string())
        .unwrap_or_else(|| "input".into());

    let mut temp_audio = std::path::Path::new(temp_dir)
        .join(format!("{}_atemp{}", input_name, audio_ext(audio.encoder)))
        .to_string_lossy()
        .to_string();

    let mut audio_mode = spec.audio_mode;
    if !has_audio {
        audio_mode = 1; // 没有音轨时强制"不压制音频"
    }

    let aextract = match audio_mode {
        0 => {
            let mut a = audio.clone();
            a.input = input.to_string();
            a.output = temp_audio.clone();
            audiobat(&a, tools)
        }
        1 => String::new(),
        2 => {
            if audio_format.eq_ignore_ascii_case("aac") {
                temp_audio = std::path::Path::new(temp_dir)
                    .join(format!("{}_atemp.aac", input_name))
                    .to_string_lossy()
                    .to_string();
                extract_audio(tools, input, &temp_audio, 0)
            } else {
                let mut a = audio.clone();
                a.input = input.to_string();
                a.output = temp_audio.clone();
                audiobat(&a, tools)
            }
        }
        _ => String::new(),
    };

    let temp_video = std::path::Path::new(temp_dir)
        .join(format!(
            "{}_vtemp{}",
            input_name,
            temp_container_ext(spec)
        ))
        .to_string_lossy()
        .to_string();

    let mut x264 = if spec.mode == 2 {
        format!(
            "{}\r\n{}",
            build_video(spec, tools, input, &temp_video, 1, sub),
            build_video(spec, tools, input, &temp_video, 2, sub)
        )
    } else {
        build_video(spec, tools, input, &temp_video, 0, sub)
    };

    // 不压制音频时视频直接落最终文件，省掉一次封装
    if audio_mode == 1 || !has_audio {
        x264 = x264.replace(&temp_video, output);
    }
    x264.push_str("\r\n");

    let mux = ffmuxbat(tools, &temp_video, &temp_audio, output);

    let mut bat = if audio_mode != 1 && has_audio {
        format!("{}{}{} \r\n", aextract, x264, mux)
    } else {
        format!("{} \r\n", x264)
    };

    bat.push_str(&format!("del \"{}\"\r\n", temp_audio));
    bat.push_str(&format!("del \"{}\"\r\n", temp_video));
    bat.push_str("echo ===== one file is completed! =====\r\n");
    bat
}

/// 重新封装。
///
/// 在原版 `btnmux_Click`（视频 + 单音轨）基础上把音轨做成**列表**：
/// 可以一次挂多条外部音轨，按列表顺序映射成多条输出音轨；
/// `keep_source_audio = false` 且挂了音轨时，就是原版 `MuxReplaceAudioButton`
/// 的「替换音频」语义。
///
/// `-c:a:N` 里的 `N` 是**输出里的音频流序号**，所以源音轨占 0 时要跟着往后数。
pub fn mux(spec: &MuxSpec, tools: &str) -> String {
    let mut sb = String::new();
    sb.push_str(&quote(&tool(tools, "ffmpeg.exe")));

    let lower = spec.video.to_lowercase();
    let is_raw = lower.ends_with(".264") || lower.ends_with(".h264") || lower.ends_with(".hevc");

    if is_raw && !spec.fps.is_empty() && spec.fps != "auto" {
        sb.push_str(&format!(" -r {}", spec.fps));
    }

    sb.push_str(&format!(" -i \"{}\"", spec.video));

    let audios: Vec<&String> = spec
        .audios
        .iter()
        .filter(|a| !a.trim().is_empty())
        .collect();
    for a in &audios {
        sb.push_str(&format!(" -i \"{}\"", a));
    }

    sb.push_str(" -map 0:v -c:v copy");

    if is_raw && !spec.par.is_empty() && spec.par != "1:1" {
        if lower.ends_with(".hevc") {
            sb.push_str(&format!(" -bsf:v hevc_metadata=sample_aspect_ratio={}", spec.par));
        } else {
            sb.push_str(&format!(" -bsf:v h264_metadata=sample_aspect_ratio={}", spec.par));
        }
    }

    let mut index: usize = 0;
    if spec.keep_source_audio {
        // `?` 让"源文件本来就没音轨"也不至于整个失败
        sb.push_str(" -map 0:a? -c:a copy");
        index += 1;
    }
    for (i, _) in audios.iter().enumerate() {
        // 输入 0 是视频，所以外部音轨从 1 开始
        sb.push_str(&format!(" -map {}:a:0 -c:a:{} copy", i + 1, index));
        index += 1;
    }

    // 既不留源音轨、也没挂外部音轨 → 明确做成无声视频，
    // 否则 ffmpeg 会自己挑一条音轨塞进去（原版没这句，是个隐患）
    if !spec.keep_source_audio && audios.is_empty() {
        sb.push_str(" -an");
    }

    let fmt = spec.format.trim().to_lowercase();
    if !fmt.is_empty() {
        sb.push_str(&format!(" -f {}", fmt));
    }

    sb.push_str(" -map_metadata 0 -sn -y \"");
    sb.push_str(&spec.output);
    sb.push('"');
    sb.push_str("\r\n");
    sb
}

/// 转换后的输出路径：`<目录>\<原名>.<目标扩展名>`。
/// `output_dir` 留空则写在源文件旁边。
pub fn convert_output(input: &str, format: &str, output_dir: &str) -> String {
    let p = std::path::Path::new(input);
    let stem = p
        .file_stem()
        .map(|s| s.to_string_lossy().to_string())
        .unwrap_or_else(|| "output".into());
    let ext = format.trim().trim_start_matches('.');
    let dir = if output_dir.trim().is_empty() {
        p.parent()
            .map(|d| d.to_path_buf())
            .unwrap_or_else(|| std::path::PathBuf::from("."))
    } else {
        std::path::PathBuf::from(output_dir)
    };
    dir.join(format!("{}.{}", stem, ext))
        .to_string_lossy()
        .to_string()
}

/// 批量封装转换的一条命令（原版 `btnBatchMP4_Click` 的循环体）。
///
/// 原版的规则：源音轨不是 AAC、且目标容器不是 mkv 时，顺手把音频转成 AAC
/// （`-strict -2` 是给 ffmpeg 内置 aac 编码器用的），否则整条流直接复制。
pub fn convert_container_cmd(
    tools: &str,
    input: &str,
    output: &str,
    format: &str,
    aac_encoder: &str,
    transcode_audio: bool,
) -> String {
    let ffmpeg = quote(&tool(tools, "ffmpeg.exe"));
    let mut sb = format!("{} -y -i \"{}\" -c:v copy", ffmpeg, input);
    if transcode_audio {
        let enc = if aac_encoder.trim().is_empty() {
            "aac"
        } else {
            aac_encoder.trim()
        };
        sb.push_str(&format!(" -c:a {} -strict -2", enc));
    } else {
        sb.push_str(" -c copy");
    }
    let f = format.trim().to_lowercase();
    if !f.is_empty() {
        sb.push_str(&format!(" -f {}", f));
    }
    sb.push_str(&format!(" \"{}\"", output));
    sb.push_str("\r\n");
    sb
}

/// 镜像原版 `ExtractAV` / `ExtractTrack`。
///
/// 注意：原版视频抽取那段的实现被注释掉了，实际生效的是 `ExtractAV(namevideo,"v",0)`，
/// 命令行形式与保留下来的注释一致（`-an -sn -c:v:0 copy`），这里按注释里的形态还原。
pub fn extract(spec: &ExtractSpec, tools: &str) -> String {
    let ffmpeg = quote(&tool(tools, "ffmpeg.exe"));
    match spec.kind.as_str() {
        "video" => format!(
            "{} -i {} -an -sn -c:v:0 copy {}\r\n",
            ffmpeg,
            quote(&spec.input),
            quote(&spec.output)
        ),
        "track" => format!(
            "{} -i {} -map 0:{} -c copy {}\r\n",
            ffmpeg,
            quote(&spec.input),
            spec.stream_index,
            quote(&spec.output)
        ),
        "mkv" => format!(
            "{} tracks {} {}:{}\r\n",
            quote(&tool(tools, "mkvextract.exe")),
            quote(&spec.input),
            spec.stream_index,
            quote(&spec.output)
        ),
        // 默认按音频抽
        _ => format!(
            "{} -i {} -vn -sn -c:a copy -y -map 0:a:{} {}\r\n",
            ffmpeg,
            quote(&spec.input),
            spec.stream_index,
            quote(&spec.output)
        ),
    }
}

/// 粗剪（视频 / 音频通用）。
///
/// 两种模式：
/// - `reencode = false`：`-c copy` 流复制。**秒切**，但切点会被吸到最近的关键帧上，
///   所以起点可能比设定值早一点点。优点是无损、几乎瞬时。
/// - `reencode = true`：重编码，切点精确到帧，还能顺手改质量/码率。
///
/// `-ss` 一律放在 `-i` **前面**（输入定位）：这是 ffmpeg 快得多的那条路，
/// 而且重编码时前置 `-ss` 同样是帧精确的。
pub fn trim(spec: &TrimSpec, tools: &str) -> String {
    let ffmpeg = quote(&tool(tools, "ffmpeg.exe"));
    let mut sb = String::new();

    let start = if spec.start.is_finite() && spec.start > 0.0 {
        spec.start
    } else {
        0.0
    };
    let dur = if spec.end.is_finite() && spec.end > start {
        spec.end - start
    } else {
        0.0 // 0 表示"一直到结尾"
    };

    sb.push_str(&ffmpeg);
    if start > 0.0 {
        sb.push_str(&format!(" -ss {}", secs(start)));
    }
    sb.push_str(&format!(" -i \"{}\"", spec.input));
    if dur > 0.0 {
        sb.push_str(&format!(" -t {}", secs(dur)));
    }
    if spec.audio_only {
        sb.push_str(" -vn");
    }

    if spec.reencode {
        if !spec.audio_only {
            sb.push_str(" -c:v libx264 -preset medium -crf 20");
        }
        sb.push_str(&format!(" -c:a aac -b:a {}k", spec.audio_bitrate));
        let extra = spec.params.trim();
        if !extra.is_empty() {
            sb.push(' ');
            sb.push_str(extra);
        }
    } else {
        sb.push_str(" -c copy -avoid_negative_ts make_zero");
    }

    let lower = spec.output.to_lowercase();
    if (lower.ends_with(".mp4") || lower.ends_with(".mov")) && !spec.audio_only {
        sb.push_str(" -movflags +faststart");
    }

    sb.push_str(" -map_metadata 0");
    sb.push_str(&format!(" -y \"{}\"", spec.output));
    sb.push_str("\r\n");
    sb
}

/* ================================================================== *
 * 插帧 / 超分
 *
 * ⚠️ 这一块**不是**原版的镜像 —— 原版没有这个功能，规则是从零定的。
 *
 * 两条路：
 *   1. 纯滤镜：一条 ffmpeg 命令搞定（`minterpolate` / `scale` / `xbr`）。
 *   2. 本地模型：`rife` / `realesrgan` / `realcugan` 三个 ncnn-vulkan 程序。
 *      它们只吃**图片序列**，所以必须 拆帧 → 推理 → 合帧 三趟。
 *
 * 踩过的两个坑（都是实测出来的，别改回去）：
 *   - **`.bat` 里写 `%08d` 会被 cmd 当成 `%0` 展开**，变成
 *     `...\lanzhutool_123.bat8d.png`。图片序列模式必须写 `%%08d`。
 *     同理 `for %%A in (...)` 也要双百分号。
 *   - 模型的 `-o` **目录必须事先存在**，否则 rife 会以为那是文件名、
 *     去猜扩展名然后报 `invalid outputpath extension type`。
 *     所以每条模型命令前面都先 `if not exist ... mkdir ...`。
 * ================================================================== */

/// 流水线要知道的源信息（由 [`crate::probe`] 探好后传进来）。
pub struct EnhanceSource {
    pub fps: f64,
    pub width: i64,
    pub height: i64,
    pub has_audio: bool,
}

/// 帧率的命令行写法：`47.952` / `60`。
/// 先按 3 位小数收一下，免得把 `47.952000000000001` 这种浮点尾巴写进命令行。
fn fps_text(v: f64) -> String {
    n((v * 1000.0).round() / 1000.0)
}

/// yuv420p 要求宽高都是偶数，多出来的那一行/列直接抹掉。
fn even(v: i64) -> i64 {
    v & !1
}

fn gcd(a: i64, b: i64) -> i64 {
    if b == 0 {
        if a == 0 { 1 } else { a.abs() }
    } else {
        gcd(b, a % b)
    }
}

/// 把 `60/23.976` 约成 `2503/1000`。
///
/// 为什么要约分：帧数是在 **.bat 里用 `set /a` 算的**，而 cmd 的整数是
/// 32 位的。`帧数 × 60000` 对一部两小时的片子就直接溢出了，
/// 约分之后乘数控制在几百，实际能撑到几十万帧。
fn reduce(mut a: i64, mut b: i64) -> (i64, i64) {
    if a == 0 || b == 0 {
        return (2, 1);
    }
    let g = gcd(a, b);
    a /= g;
    b /= g;
    (a, b)
}

/// 插帧之后的帧率。`interp_fps > 0` 时以它为准，否则按倍数推。
fn interp_target(spec: &EnhanceSpec, src_fps: f64) -> f64 {
    if !spec.interp {
        return src_fps;
    }
    if spec.interp_fps > 0.0 {
        spec.interp_fps
    } else {
        src_fps * spec.interp_mult.max(1.0)
    }
}

/// 目标尺寸：用户指定优先，否则「源尺寸 × 倍数」，两者都抹成偶数。
/// 返回 `(0, 0)` 表示源尺寸也未知（极少见，这时退回 `iw*2` 那种表达式）。
fn target_size(spec: &EnhanceSpec, w: i64, h: i64, mult: f64) -> (i64, i64) {
    if spec.width > 0 && spec.height > 0 {
        return (even(spec.width).max(2), even(spec.height).max(2));
    }
    if w > 0 && h > 0 {
        let tw = (w as f64 * mult).round() as i64;
        let th = (h as f64 * mult).round() as i64;
        return (even(tw).max(2), even(th).max(2));
    }
    (0, 0)
}

/// `scale` 滤镜。源尺寸已知就把数字算死（可读、可断言），
/// 不知道才用表达式，并且用 `trunc(.../2)*2` 把结果钳成偶数。
fn scale_filter(w: i64, h: i64, flags: &str) -> String {
    if w > 0 && h > 0 {
        format!("scale={}:{}:flags={}", w, h, flags)
    } else {
        format!("scale=trunc(iw/2)*2:trunc(ih/2)*2:flags={}", flags)
    }
}

/// 内置插帧：`minterpolate`。
///
/// `mci` 是运动补偿插值（真正"算"出中间帧），另外两个模式
/// （`blend` 混帧、`dup` 复制帧）只是给个对照，画质差但快得多。
fn minterpolate_filter(spec: &EnhanceSpec, fps: f64) -> String {
    let mode = match spec.interp_mode.trim() {
        "blend" => "blend",
        "dup" => "dup",
        _ => "mci",
    };
    if mode == "mci" {
        format!(
            "minterpolate=fps={}:mi_mode=mci:mc_mode=aobmc:me_mode=bidir:vsbmc=1",
            fps_text(fps)
        )
    } else {
        format!("minterpolate=fps={}:mi_mode={}", fps_text(fps), mode)
    }
}

/// 内置超分。只有这三个走滤镜；其余（realesrgan / realcugan）是本地模型，返回 `None`。
///
/// `xbr` / `hqx` 是给**动画线条和像素画**用的：它们的输出是"硬边"的，
/// 真人影片上会显得很脏 —— 所以界面上的说明必须写清楚。
/// `nnedi` 看着最合适（神经网络边缘插值），但 ffmpeg 的 nnedi 滤镜
/// 要求外挂 `nnedi3_weights.bin`，本机实测报 `No weights file provided,
/// aborting!` —— 不想为它多带一个权重文件，所以没做进来。
fn builtin_upscale(spec: &EnhanceSpec, w: i64, h: i64, mult: f64) -> Option<String> {
    let m = (mult.round() as i64).clamp(2, 4);
    match spec.upscale_engine.trim() {
        "xbr" => Some(format!("xbr=n={}", m)),
        "hqx" => Some(format!("hqx={}", m)),
        "lanczos" => {
            let (tw, th) = target_size(spec, w, h, mult);
            Some(scale_filter(tw, th, "lanczos"))
        }
        _ => None,
    }
}

/// 编码器 + 参数。前端给一组预设（`lib/enhancePresets.ts`），
/// 后端只负责原样拼；两个字段都空时按容器给一份能用的默认值。
fn enhance_encode_args(spec: &EnhanceSpec) -> String {
    let enc = if spec.encoder.trim().is_empty() {
        if spec.container.eq_ignore_ascii_case("mkv") {
            "libx265"
        } else {
            "libx264"
        }
    } else {
        spec.encoder.trim()
    };
    let params = if spec.encode_params.trim().is_empty() {
        if enc.contains("x265") {
            "-crf 20 -preset medium -pix_fmt yuv420p"
        } else {
            "-crf 18 -preset medium -pix_fmt yuv420p"
        }
    } else {
        spec.encode_params.trim()
    };
    format!("-c:v {} {}", enc, params)
}

/// 音轨参数。`has_audio = false` 时无论如何都是 `-an`。
fn enhance_audio_args(spec: &EnhanceSpec, has_audio: bool) -> &'static str {
    if !has_audio || spec.audio == "none" {
        "-an"
    } else if spec.audio == "aac" {
        "-c:a aac -b:a 192k"
    } else {
        "-c:a copy"
    }
}

/// 输出文件名后缀：把用到的引擎和倍数压进去，一眼看得出这条片子经过了什么。
/// `1.mp4` → `1_rife2x_cugan2x.mp4`，指定了目标帧率时写成 `_rife60fps_…`。
pub fn enhance_suffix(spec: &EnhanceSpec) -> String {
    let mut s = String::new();
    if spec.interp {
        let tag = if spec.interp_engine.trim() == "rife" {
            "rife"
        } else {
            "fi"
        };
        let v = if spec.interp_fps > 0.0 {
            format!("{}fps", fps_text(spec.interp_fps))
        } else {
            format!("{}x", n(spec.interp_mult))
        };
        s.push_str(&format!("_{}{}", tag, v));
    }
    if spec.upscale {
        let tag = match spec.upscale_engine.trim() {
            "realesrgan" => "esr",
            "realcugan" => "cugan",
            other if !other.is_empty() => other,
            _ => "up",
        };
        s.push_str(&format!("_{}{}x", tag, n(spec.upscale_mult)));
    }
    s
}

/// 默认输出：源文件旁边 `<源名><后缀>.<容器>`，重名退到 `_new_file(n)`。
/// 和 [`default_video_output`] 同一套规则 —— 绝不能和输入同名。
pub fn default_enhance_output(spec: &EnhanceSpec) -> String {
    let suffix = enhance_suffix(spec);
    let ext = {
        let e = spec.container.trim().trim_start_matches('.').to_lowercase();
        if e.is_empty() { "mp4".to_string() } else { e }
    };
    let mut out = beside(&spec.input, &format!("{}.{}", suffix, ext));
    let mut i = 1;
    while out.eq_ignore_ascii_case(spec.input.trim()) || std::path::Path::new(&out).exists() {
        out = beside(
            &spec.input,
            &format!("_new_file({}){}.{}", i, suffix, ext),
        );
        i += 1;
    }
    out
}

/// 插帧 / 超分流水线。返回**逐行**的命令（和 `plan_*` 的约定一致）。
pub fn enhance_pipeline(
    spec: &EnhanceSpec,
    src: &EnhanceSource,
    tools: &str,
    temp_dir: &str,
) -> Vec<String> {
    let input = spec.input.trim();
    let output = spec.output.trim();
    let ffmpeg = quote(&tool(tools, "ffmpeg.exe"));
    let src_fps = if src.fps > 0.0 { src.fps } else { 25.0 };

    let want_interp = spec.interp && !spec.interp_engine.trim().is_empty();
    let want_up = spec.upscale && !spec.upscale_engine.trim().is_empty();
    let out_fps = if want_interp { interp_target(spec, src_fps) } else { src_fps };
    let up_mult = if want_up { spec.upscale_mult.max(1.0) } else { 1.0 };

    let use_rife = want_interp && spec.interp_engine.trim() == "rife";
    let use_gan = want_up && matches!(spec.upscale_engine.trim(), "realesrgan" | "realcugan");
    let audio = enhance_audio_args(spec, src.has_audio);
    let enc = enhance_encode_args(spec);
    let mut lines: Vec<String> = Vec::new();

    /* ---------------- 纯滤镜：一条命令 ---------------- */
    if !use_rife && !use_gan {
        let mut filters: Vec<String> = Vec::new();
        if want_interp {
            filters.push(minterpolate_filter(spec, out_fps));
        }
        if want_up {
            if let Some(f) = builtin_upscale(spec, src.width, src.height, up_mult) {
                filters.push(f);
            }
        }
        if filters.is_empty() {
            return lines;
        }

        lines.push(format!("rem ===== 插帧 / 超分（内置滤镜）====="));
        let mut c = format!(
            "{} -y -i \"{}\" -sn -vf \"{}\" {}",
            ffmpeg,
            input,
            filters.join(","),
            enc
        );
        c.push_str(&format!(" {} \"{}\"", audio, output));
        lines.push(c);
        lines.push("echo ===== one file is completed! =====".into());
        return lines;
    }

    /* ---------------- 本地模型：拆帧 → 推理 → 合帧 ---------------- */
    let stem = std::path::Path::new(input)
        .file_stem()
        .map(|s| s.to_string_lossy().to_string())
        .unwrap_or_else(|| "clip".into());
    let work = std::path::Path::new(temp_dir)
        .join(format!("{}_enh", stem))
        .to_string_lossy()
        .to_string();
    let frames = format!("{}\\f", work);
    let interp = format!("{}\\i", work);
    let sr = format!("{}\\s", work);

    lines.push("rem ===== 插帧 / 超分（本地模型）=====".into());
    lines.push(format!("if not exist \"{}\" mkdir \"{}\"", temp_dir, temp_dir));
    // 先清空工作目录：上一次跑到一半被终止的话，里面还留着旧的 PNG，
    // 数出来的帧数会偏大，rife 也会把那些陈旧帧一起插进去。
    lines.push(format!("if exist \"{}\" rmdir /s /q \"{}\"", work, work));
    lines.push(format!("if not exist \"{}\" mkdir \"{}\"", work, work));
    lines.push(format!("if not exist \"{}\" mkdir \"{}\"", frames, frames));

    // 抽帧。`%%08d` 不能被简化成 `%08d`（见文件头注释）。
    lines.push(format!(
        "{} -y -i \"{}\" -vsync 0 -pix_fmt rgb24 \"{}\\%%08d.png\"",
        ffmpeg, input, frames
    ));

    // 中间帧序列目录，逐级往下传
    let mut seq_dir = frames.clone();

    if use_rife {
        lines.push(format!("if not exist \"{}\" mkdir \"{}\"", interp, interp));
        let model_dir = quote(&model_dir(tools, "rife-ncnn-vulkan.exe", spec.interp_model.trim()));

        // rife 的 `-n` 是**目标总帧数**，默认 = 输入帧数 × 2。
        // 只有 rife-v4 系模型支持自定义 `-n`，所以 2 倍这条路故意不传 `-n`，
        // 这样 rife-anime（v1.8）也能用。
        let exact_2x = (out_fps - src_fps * 2.0).abs() < 0.005;
        let mut n_flag = String::new();
        if !exact_2x {
            let (num, den) = reduce((out_fps * 1000.0).round() as i64, (src_fps * 1000.0).round() as i64);
            // 用 Path::join 拼通配符：手写反斜杠在 Rust 字符串里要写 `\\`，
            // 而这个字符串最后还要给 cmd 看，两层转义叠在一起极容易写错。
            let glob = std::path::Path::new(&frames)
                .join("*.png")
                .to_string_lossy()
                .to_string();
            lines.push("set /a LC=0".into());
            lines.push(format!("for %%A in (\"{}\") do set /a LC+=1", glob));
            // 向上取整：ceil(LC*num/den) = (LC*num + den - 1) / den。
            // cmd 的 `set /a` 是截断除法，不补这个 `den-1` 会少算最后一帧；
            // `den-1` 先在这里算好，免得 .bat 里出现 `+2-1` 这种碍眼的写法。
            lines.push(format!("set /a LT=(LC*{}+{})/{}", num, den - 1, den));
            n_flag = " -n %LT%".to_string();
        }

        let mut c = format!(
            "{} -i \"{}\" -o \"{}\" -m {}{} -g {}{}",
            quote(&tool(tools, "rife-ncnn-vulkan.exe")),
            frames,
            interp,
            model_dir,
            n_flag,
            spec.gpu_index,
            jobs_flag(&spec.jobs)
        );
        if spec.interp_tta {
            c.push_str(" -x");
        }
        lines.push(c);
        seq_dir = interp;
    }

    if use_gan {
        lines.push(format!("if not exist \"{}\" mkdir \"{}\"", sr, sr));
        let mult = (up_mult.round() as i64).clamp(2, 4);
        if spec.upscale_engine.trim() == "realesrgan" {
            lines.push(format!(
                "{} -i \"{}\" -o \"{}\" -s {} -n {} -m {} -g {}{}",
                quote(&tool(tools, "realesrgan-ncnn-vulkan.exe")),
                seq_dir,
                sr,
                mult,
                spec.upscale_model.trim(),
                quote(&model_dir(tools, "realesrgan-ncnn-vulkan.exe", "models")),
                spec.gpu_index,
                jobs_flag(&spec.jobs)
            ));
        } else {
            // realcugan 的 `-s` 配合模型目录里的文件（up2x/up3x/up4x）一起生效
            lines.push(format!(
                "{} -i \"{}\" -o \"{}\" -s {} -n {} -m {} -g {}{}",
                quote(&tool(tools, "realcugan-ncnn-vulkan.exe")),
                seq_dir,
                sr,
                mult,
                spec.cugan_denoise,
                quote(&model_dir(tools, "realcugan-ncnn-vulkan.exe", spec.cugan_model.trim())),
                spec.gpu_index,
                jobs_flag(&spec.jobs)
            ));
        }
        seq_dir = sr;
    }

    // 合帧 + 音轨。帧数正好是"倍数"倍，所以 `-framerate` 直接给算好的帧率。
    let mut vf = String::new();
    if want_up && !use_gan {
        // 走了模型超分就不要再 scale（模型已经给足尺寸）
        if let Some(f) = builtin_upscale(spec, src.width, src.height, up_mult) {
            vf = format!(" -vf \"{}\"", f);
        }
    } else if use_gan && spec.width > 0 && spec.height > 0 {
        // 模型放大之后再收口到用户指定的分辨率
        let (tw, th) = target_size(spec, 0, 0, 1.0);
        vf = format!(" -vf \"{}\"", scale_filter(tw, th, "lanczos"));
    }

    lines.push(format!(
        "{} -y -framerate {} -i \"{}\\%%08d.png\" -i \"{}\" -map 0:v:0 -map 1:a:0? -sn{} {} {} \"{}\"",
        ffmpeg,
        fps_text(out_fps),
        seq_dir,
        input,
        vf,
        enc,
        audio,
        output
    ));

    lines.push(format!("rmdir /s /q \"{}\"", work));
    lines.push("echo ===== one file is completed! =====".into());
    lines
}

/// `-j load:proc:save`，留空就不传（模型自带默认值）。
fn jobs_flag(jobs: &str) -> String {
    let j = jobs.trim();
    // 只接受 `a:b:c` 这种形状，别把界面上的手滑原样送进命令行
    let ok = j.split(':').count() == 3 && j.split(':').all(|p| p.parse::<u32>().is_ok());
    if ok {
        format!(" -j {}", j)
    } else {
        String::new()
    }
}

/// 模型目录：**和可执行文件同级**。
///
/// 注意不要用 tools 根去拼 —— 压缩包解出来是
/// `tools/video/enhance/<工具目录>/`，权重就躺在 exe 旁边，
/// 而 `tools/models` 根本不存在。
fn model_dir(tools: &str, exe_name: &str, rel: &str) -> String {
    let exe = tool(tools, exe_name);
    let base = std::path::Path::new(&exe)
        .parent()
        .map(|p| p.to_path_buf())
        .unwrap_or_else(|| std::path::PathBuf::from("."));
    base.join(rel).to_string_lossy().to_string()
}

/// 秒 -> 命令行数字（去掉多余的 0，避免出现 `-ss 12.000`）
fn secs(v: f64) -> String {
    let s = format!("{:.3}", v);
    let s = s.trim_end_matches('0').trim_end_matches('.');
    if s.is_empty() {
        "0".into()
    } else {
        s.to_string()
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::spec::{AudioSpec, EnhanceSpec, TrimSpec, VideoSpec};

    /// 指向一个不存在的目录，让 `tool()` 原样返回工具名，
    /// 这样断言就是机器无关的（否则会带出本机 tools 的绝对路径）。
    const T: &str = "C:\\__no_such_tools__";

    #[test]
    fn ffmux_matches_original() {
        assert_eq!(
            ffmuxbat(T, "in1.mp4", "in2.aac", "out.mp4"),
            "\"ffmpeg.exe\" -i \"in1.mp4\" -i \"in2.aac\" -sn -map 0:v -map 1:a -c copy -y \"out.mp4\"\r\n"
        );
    }

    #[test]
    fn crf_mode_cpu_h264() {
        let mut s = VideoSpec::default();
        s.mode = 1;
        s.crf = 23.5;
        assert_eq!(
            build_video(&s, T, "in.mp4", "out.mp4", 0, ""),
            "\"ffmpeg.exe\" -y -i \"in.mp4\" -an -sn -threads 0 -pix_fmt yuv420p \
-c:v libx264 -crf 23.5 -preset fast \"out.mp4\"\r\n"
        );
    }

    #[test]
    fn crf_mode_hevc_10bit_gets_10le_pixfmt() {
        let mut s = VideoSpec::default();
        s.format = "HEVC 10bit".into();
        s.mode = 1;
        s.crf = 20.0;
        let c = build_video(&s, T, "a.mp4", "o.mp4", 0, "");
        assert!(c.contains("-pix_fmt yuv420p10le"), "{}", c);
        assert!(c.contains("-c:v libx265"), "{}", c);
        // 没显式给附加参数时，HEVC 走 medium 而不是 fast
        assert!(c.contains("-preset medium"), "{}", c);
    }

    #[test]
    fn crf_mode_integer_crf_has_no_decimal() {
        let mut s = VideoSpec::default();
        s.mode = 1;
        s.crf = 24.0;
        assert!(build_video(&s, T, "i.mp4", "o.mp4", 0, "").contains("-crf 24 "));
    }

    /// 这个用例覆盖了四条最容易写错的规则：
    ///   1. GPU 模式绝对不能加 -pix_fmt（会触发 auto_scale 失败 -40）
    ///   2. 滤镜链顺序 hwdownload → format → scale/subtitles → hwupload_cuda
    ///   3. -gpu N 要带上设备序号
    ///   4. subtitles 滤镜里盘符的冒号必须转义
    #[test]
    fn gpu_filter_chain_with_scale_and_subtitle() {
        let mut s = VideoSpec::default();
        s.format = "HEVC 10bit".into();
        s.mode = 1;
        s.crf = 20.0;
        s.use_gpu = true;
        s.gpu_kind = "nvenc".into();
        s.gpu_index = 1;
        s.width = 1280;
        s.height = 720;

        let c = build_video(&s, T, "a.mp4", "o.mp4", 0, "C:\\sub dir\\a.ass");

        assert!(!c.contains("-pix_fmt"), "GPU 模式不该出现 -pix_fmt: {}", c);
        assert!(c.contains("-hwaccel cuda -hwaccel_output_format cuda"), "{}", c);
        assert!(c.contains("-gpu 1"), "{}", c);
        assert!(c.contains("-c:v hevc_nvenc"), "{}", c);
        // NVENC 的质量参数不是 -crf
        assert!(c.contains("-rc vbr -cq 20"), "{}", c);
        assert!(c.contains("-preset p5"), "{}", c);
        assert!(
            c.contains(
                "-vf \"hwdownload,format=p010le,scale=1280:720:flags=lanczos,\
subtitles='C\\:/sub dir/a.ass',hwupload_cuda\""
            ),
            "滤镜链不对:\n{}",
            c
        );
    }

    #[test]
    fn gpu_without_cpu_filters_clears_filter_chain() {
        let mut s = VideoSpec::default();
        s.mode = 1;
        s.crf = 23.0;
        s.use_gpu = true;
        s.gpu_kind = "qsv".into();
        let c = build_video(&s, T, "i.mp4", "o.mp4", 0, "");
        assert!(!c.contains("-vf"), "没有 CPU 滤镜时不该有 -vf: {}", c);
        assert!(c.contains("-hwaccel qsv"), "{}", c);
        assert!(c.contains("-c:v h264_qsv"), "{}", c);
        // QSV 用 -global_quality
        assert!(c.contains("-global_quality 23"), "{}", c);
    }

    #[test]
    fn two_pass_first_pass_goes_to_nul() {
        let mut s = VideoSpec::default();
        s.mode = 2;
        s.bitrate = 800.0;
        let p1 = build_video(&s, T, "i.mp4", "o.mp4", 1, "");
        let p2 = build_video(&s, T, "i.mp4", "o.mp4", 2, "");
        assert!(p1.contains("-pass 1 -b:v 800k"), "{}", p1);
        assert!(p1.trim_end().ends_with("-f null NUL"), "{}", p1);
        assert!(!p1.contains("o.mp4"), "第一遍不该写输出文件: {}", p1);
        assert!(p2.contains("-pass 2 -b:v 800k"), "{}", p2);
        assert!(p2.contains("\"o.mp4\""), "{}", p2);
    }

    #[test]
    fn custom_mode_uses_raw_parameters() {
        let mut s = VideoSpec::default();
        s.mode = 0;
        s.custom_params = "--crf 21 --aq-mode 2".into();
        let c = build_video(&s, T, "i.mp4", "o.mp4", 0, "");
        assert!(c.contains("-c:v libx264 --crf 21 --aq-mode 2"), "{}", c);
    }

    #[test]
    fn audio_nero_pipe_matches_original() {
        let mut a = AudioSpec::default();
        a.input = "in.mp4".into();
        a.output = "out.mp4".into();
        a.encoder = 0;
        a.use_bitrate = true;
        a.bitrate = "128".into();
        assert_eq!(
            audiobat(&a, T),
            "\"ffmpeg.exe\" -i \"in.mp4\" -vn -sn -v 0 -c:a pcm_s16le -f wav pipe:|\
\"neroAacEnc.exe\" -ignorelength -lc -br 128000 -if - -of \"out.mp4\"\r\n"
        );
    }

    #[test]
    fn audio_wav_uses_ffmpeg_directly_and_forces_wav_ext() {
        let mut a = AudioSpec::default();
        a.input = "in.mp4".into();
        a.output = "out.aac".into();
        a.encoder = 2;
        let c = audiobat(&a, T);
        assert_eq!(c, "\"ffmpeg.exe\" -y -i \"in.mp4\" -f wav \"out.wav\"\r\n");
    }

    #[test]
    fn audio_custom_params_replace_bitrate() {
        let mut a = AudioSpec::default();
        a.input = "i.mp4".into();
        a.output = "o.flac".into();
        a.encoder = 4;
        a.use_bitrate = false;
        a.custom_params = "-5".into();
        assert_eq!(
            audiobat(&a, T),
            "\"ffmpeg.exe\" -i \"i.mp4\" -vn -sn -v 0 -c:a pcm_s16le -f wav pipe:|\
\"flac.exe\" -f --ignore-chunk-sizes -5 - -o \"o.flac\"\r\n"
        );
    }

    /// 没有音轨时：视频直接落最终文件、不封装、也要清理临时音频。
    ///
    /// 注意：原版在「不压制音频」分支里也会照样输出一条 `del 临时视频`，
    /// 即使那个临时文件根本没被创建过。这个无害的冗余被如实保留了下来，
    /// 所以**不能**断言脚本里不出现 `_vtemp`。
    #[test]
    fn pipeline_without_audio_writes_output_directly() {
        let s = VideoSpec::default();
        let a = AudioSpec::default();
        let bat = video_pipeline(&s, &a, T, "C:\\TEMP", "in.mp4", "out.mp4", "", false, "");

        // 关键：视频那条命令的输出参数必须直接指向最终文件，而不是中间临时文件
        let video_line = bat
            .lines()
            .find(|l| l.contains("libx264"))
            .expect("没有找到视频编码命令");
        assert!(
            video_line.trim_end().ends_with("\"out.mp4\""),
            "视频命令没直接写最终文件: {}",
            video_line
        );

        // 没有音轨就不该出现封装步骤
        assert!(!bat.contains("-map 1:a -c copy"), "不该有封装步骤:\n{}", bat);
        assert!(bat.contains("del \"C:\\TEMP\\in_atemp.mp4\""), "{}", bat);
        assert!(bat.contains("===== one file is completed! ====="), "{}", bat);
    }

    /// 有音轨时：抽音频 → 压视频 → 封装 → 删临时文件，顺序不能乱。
    #[test]
    fn pipeline_with_audio_order_is_audio_video_mux() {
        let mut s = VideoSpec::default();
        s.audio_mode = 0;
        let a = AudioSpec::default();
        let bat = video_pipeline(&s, &a, T, "C:\\TEMP", "in.mp4", "out.mp4", "", true, "aac");

        let i_audio = bat.find("neroAacEnc.exe").expect("缺少音频编码步骤");
        let i_video = bat.find("libx264").expect("缺少视频编码步骤");
        let i_mux = bat.find("-map 1:a -c copy").expect("缺少封装步骤");
        assert!(i_audio < i_video, "音频必须在视频之前:\n{}", bat);
        assert!(i_video < i_mux, "封装必须在视频之后:\n{}", bat);
        assert!(bat.contains("_vtemp.mp4"), "{}", bat);
    }

    /// audioMode=2 且源音轨是 AAC 时，走无损抽取而不是重压。
    #[test]
    fn pipeline_copy_audio_uses_stream_copy() {
        let mut s = VideoSpec::default();
        s.audio_mode = 2;
        let a = AudioSpec::default();
        let bat = video_pipeline(&s, &a, T, "C:\\TEMP", "in.mp4", "out.mp4", "", true, "aac");
        assert!(bat.contains("-c:a copy -y -map 0:a:0"), "{}", bat);
        assert!(bat.contains("in_atemp.aac"), "{}", bat);
    }

    #[test]
    fn mux_raw_stream_adds_fps_and_par() {
        let spec = MuxSpec {
            video: "C:\\v.h264".into(),
            audios: vec!["C:\\a.ac3".into()],
            output: "C:\\o.mp4".into(),
            fps: "23.976".into(),
            par: "32:27".into(),
            ..Default::default()
        };
        let c = mux(&spec, T);
        assert_eq!(
            c,
            "\"ffmpeg.exe\" -r 23.976 -i \"C:\\v.h264\" -i \"C:\\a.ac3\" \
-map 0:v -c:v copy -bsf:v h264_metadata=sample_aspect_ratio=32:27 \
-map 0:a? -c:a copy -map 1:a:0 -c:a:1 copy -f mp4 -map_metadata 0 -sn -y \"C:\\o.mp4\"\r\n"
        );
    }

    #[test]
    fn mux_skips_raw_only_options_for_mp4() {
        let spec = MuxSpec {
            video: "v.mp4".into(),
            output: "o.mp4".into(),
            fps: "60".into(),
            par: "32:27".into(),
            ..Default::default()
        };
        let c = mux(&spec, T);
        assert!(!c.contains("-r 60"), "非裸流不该加 -r: {}", c);
        assert!(!c.contains("sample_aspect_ratio"), "非裸流不该加 bsf: {}", c);
        // 没挂外部音轨但保留源音轨 → 仍要映射源音轨
        assert!(c.contains(" -map 0:a? -c:a copy"), "{}", c);
        assert!(!c.contains("-map 1:"), "没挂音轨就不该映射第二条输入: {}", c);
    }

    /// 多音轨：按挂载顺序映射，`-c:a:N` 的序号要跟着源音轨往后数。
    #[test]
    fn mux_multi_track_numbers_streams_in_order() {
        let spec = MuxSpec {
            video: "v.mkv".into(),
            audios: vec!["jp.flac".into(), "cn.ac3".into(), "en.aac".into()],
            output: "o.mkv".into(),
            format: "mkv".into(),
            ..Default::default()
        };
        let c = mux(&spec, T);
        assert!(c.contains(" -i \"jp.flac\" -i \"cn.ac3\" -i \"en.aac\""), "{}", c);
        assert!(c.contains(" -map 1:a:0 -c:a:1 copy"), "{}", c);
        assert!(c.contains(" -map 2:a:0 -c:a:2 copy"), "{}", c);
        assert!(c.contains(" -map 3:a:0 -c:a:3 copy"), "{}", c);
        assert!(c.contains(" -f mkv"), "{}", c);
    }

    /// 替换音频：不保留源音轨，挂的音轨从第 0 条开始编号。
    #[test]
    fn mux_replace_audio_drops_source_track() {
        let spec = MuxSpec {
            video: "v.mp4".into(),
            audios: vec!["new.aac".into()],
            output: "o.mp4".into(),
            keep_source_audio: false,
            ..Default::default()
        };
        let c = mux(&spec, T);
        assert!(!c.contains("-map 0:a"), "替换音频时不该保留源音轨: {}", c);
        assert!(c.contains(" -map 1:a:0 -c:a:0 copy"), "{}", c);
    }

    /// 既不留源音轨、也不挂外部音轨 → 明确静音，不能让 ffmpeg 自己挑。
    #[test]
    fn mux_without_any_audio_is_explicitly_silent() {
        let spec = MuxSpec {
            video: "v.mp4".into(),
            output: "o.mp4".into(),
            keep_source_audio: false,
            ..Default::default()
        };
        let c = mux(&spec, T);
        assert!(c.contains(" -an"), "{}", c);
        assert!(!c.contains("a?"), "{}", c);
    }

    /// 空字符串音轨条目要被忽略（界面里删空的行不该产生 `-i ""`）。
    #[test]
    fn mux_ignores_blank_audio_entries() {
        let spec = MuxSpec {
            video: "v.mp4".into(),
            audios: vec![String::new(), "  ".into(), "a.ac3".into()],
            output: "o.mp4".into(),
            keep_source_audio: false,
            ..Default::default()
        };
        let c = mux(&spec, T);
        assert_eq!(c.matches(" -i ").count(), 2, "只有视频 + 1 条音轨: {}", c);
        assert!(c.contains(" -map 1:a:0 -c:a:0 copy"), "{}", c);
    }

    #[test]
    fn convert_output_keeps_name_and_swaps_extension() {
        assert_eq!(
            convert_output("C:\\dir\\a.mp4", "mkv", ""),
            "C:\\dir\\a.mkv"
        );
        assert_eq!(
            convert_output("C:\\dir\\a.mp4", ".flv", "D:\\out"),
            "D:\\out\\a.flv"
        );
    }

    /// 原版 `x264VideoTextBox_TextChanged` 的默认输出名。
    #[test]
    fn default_video_output_appends_format_suffix() {
        let mut s = VideoSpec {
            input: "C:\\__no_such_dir__\\1.mp4".into(),
            format: "H.264 8bit".into(),
            ..Default::default()
        };
        assert_eq!(default_video_output(&s), "C:\\__no_such_dir__\\1_h264.mp4");

        s.format = "HEVC 10bit".into();
        assert_eq!(default_video_output(&s), "C:\\__no_such_dir__\\1_hevc.mp4");

        s.format = "MOV".into();
        assert_eq!(default_video_output(&s), "C:\\__no_such_dir__\\1_mov.mov");

        s.format = "FLV".into();
        assert_eq!(default_video_output(&s), "C:\\__no_such_dir__\\1_flv.flv");

        // 预设模式：后缀用预设名（去掉非法字符），扩展名跟预设容器
        s.mode = 3;
        s.preset_name = "ProRes HQ".into();
        s.preset_container = "mov".into();
        assert_eq!(
            default_video_output(&s),
            "C:\\__no_such_dir__\\1_ProRes_HQ.mov"
        );
    }

    /// 同名文件已存在时要退到 `_new_file(n)_`，不能覆盖（原版的 while 循环）。
    #[test]
    fn default_video_output_avoids_overwrite() {
        let dir = std::env::temp_dir().join("lanzhutool_naming_test");
        std::fs::remove_dir_all(&dir).ok();
        std::fs::create_dir_all(&dir).unwrap();
        let input = dir.join("v.mp4");
        std::fs::write(&input, b"x").unwrap();
        std::fs::write(dir.join("v_h264.mp4"), b"x").unwrap();
        std::fs::write(dir.join("v_new_file(1)_h264.mp4"), b"x").unwrap();

        let s = VideoSpec {
            input: input.to_string_lossy().to_string(),
            format: "H.264 8bit".into(),
            ..Default::default()
        };
        let out = default_video_output(&s);
        assert_eq!(
            std::path::Path::new(&out)
                .file_name()
                .unwrap()
                .to_string_lossy(),
            "v_new_file(2)_h264.mp4"
        );

        std::fs::remove_dir_all(&dir).ok();
    }

    #[test]
    fn default_audio_output_follows_encoder() {
        let p = "D:\\a\\music.flac";
        assert_eq!(default_audio_output(p, 0), "D:\\a\\music_AAC.mp4");
        assert_eq!(default_audio_output(p, 1), "D:\\a\\music_AAC.m4a");
        assert_eq!(default_audio_output(p, 2), "D:\\a\\music_WAV.wav");
        assert_eq!(default_audio_output(p, 3), "D:\\a\\music_ALAC.m4a");
        assert_eq!(default_audio_output(p, 4), "D:\\a\\music_FLAC.flac");
        assert_eq!(default_audio_output(p, 5), "D:\\a\\music_AAC.m4a");
        assert_eq!(default_audio_output(p, 6), "D:\\a\\music_AC3.ac3");
        assert_eq!(default_audio_output(p, 9), "D:\\a\\music_AAC.aac");
    }

    #[test]
    fn default_mux_and_avs_suffixes() {
        assert_eq!(default_mux_output("D:\\a\\1.mp4"), "D:\\a\\1_Mux.mp4");
        assert_eq!(default_avs_output("D:\\a\\1.mkv"), "D:\\a\\1_AVS.mp4");
    }

    #[test]
    fn convert_container_uses_copy_or_aac() {
        let copy = convert_container_cmd(T, "in.flv", "out.mp4", "mp4", "aac", false);
        assert_eq!(
            copy,
            "\"ffmpeg.exe\" -y -i \"in.flv\" -c:v copy -c copy -f mp4 \"out.mp4\"\r\n"
        );
        let conv = convert_container_cmd(T, "in.flv", "out.mp4", "mp4", "libfdk_aac", true);
        assert_eq!(
            conv,
            "\"ffmpeg.exe\" -y -i \"in.flv\" -c:v copy -c:a libfdk_aac -strict -2 -f mp4 \"out.mp4\"\r\n"
        );
    }

    #[test]
    fn preset_kind_and_bit_depth_parse() {
        assert!(preset_kind("H.264 8bit") == PresetKind::H264);
        assert!(preset_kind("HEVC 10bit") == PresetKind::Hevc);
        assert!(preset_kind("MOV") == PresetKind::Mov);
        assert!(preset_kind("FLV") == PresetKind::Flv);

        // 必须先判 12bit，否则 "12bit" 里的 "1" 不是问题，但顺序写反会误判成 10/8
        assert_eq!(preset_bit_depth("HEVC 12bit"), 12);
        assert_eq!(preset_bit_depth("HEVC 10bit"), 10);
        assert_eq!(preset_bit_depth("H.264 8bit"), 8);
    }

    /// 预设模式：编码器/参数/容器全部听预设的，自动推导的那些一概不出现。
    #[test]
    fn preset_mode_uses_preset_encoder_and_params() {
        let s = VideoSpec {
            mode: 3,
            preset_name: "ProRes 422 (Proxy)".into(),
            preset_encoder: "prores_ks".into(),
            preset_params: "-profile:v 0 -pix_fmt yuv422p10le -qscale:v 9".into(),
            preset_container: "mov".into(),
            // 就算界面里开着 GPU 加速，预设模式也必须忽略它，否则编码器会打架
            use_gpu: true,
            ..Default::default()
        };
        let c = build_video(&s, T, "in.mp4", "out.mov", 0, "");
        assert!(
            c.contains(" -c:v prores_ks -profile:v 0 -pix_fmt yuv422p10le -qscale:v 9"),
            "{}",
            c
        );
        // 预设自带 pix_fmt，不能再被自动补一个 yuv420p
        assert_eq!(c.matches("pix_fmt").count(), 1, "{}", c);
        // 也不该出现 CRF / 默认 preset / GPU 编码器
        assert!(!c.contains("-crf"), "{}", c);
        assert!(!c.contains("-preset fast"), "{}", c);
        assert!(!c.contains("nvenc"), "{}", c);

        // 容器跟着预设走
        assert_eq!(container_ext(&s), ".mov");
        assert_eq!(temp_container_ext(&s), ".mov");
        // 非预设模式仍然按原版那张表
        let d = VideoSpec::default();
        assert_eq!(container_ext(&d), ".mp4");
        assert_eq!(temp_container_ext(&d), ".mp4");
    }

    #[test]
    fn output_and_temp_extensions_match_original() {
        assert_eq!(output_ext("MOV"), ".mov");
        assert_eq!(output_ext("FLV"), ".flv");
        assert_eq!(output_ext("HEVC 10bit"), ".mp4");
        assert_eq!(temp_ext("HEVC 10bit"), ".hevc");
        assert_eq!(temp_ext("MOV"), ".mov");
        assert_eq!(temp_ext("H.264 8bit"), ".mp4");
    }

    #[test]
    fn audio_ext_table_matches_original() {
        assert_eq!(audio_ext(0), ".mp4");
        assert_eq!(audio_ext(1), ".m4a");
        assert_eq!(audio_ext(2), ".wav");
        assert_eq!(audio_ext(3), ".m4a");
        assert_eq!(audio_ext(4), ".flac");
        assert_eq!(audio_ext(5), ".m4a");
        assert_eq!(audio_ext(6), ".ac3");
    }

    #[test]
    fn trim_stream_copy_is_keyframe_fast_and_silent() {
        let spec = TrimSpec {
            input: r"C:\in\a.mp4".into(),
            output: r"C:\out\a_cut.mp4".into(),
            start: 12.5,
            end: 30.0,
            ..Default::default()
        };
        let c = trim(&spec, T);
        assert_eq!(
            c,
            "\"ffmpeg.exe\" -ss 12.5 -i \"C:\\in\\a.mp4\" -t 17.5 -c copy -avoid_negative_ts make_zero -movflags +faststart -map_metadata 0 -y \"C:\\out\\a_cut.mp4\"\r\n"
        );
    }

    #[test]
    fn trim_reencode_appends_user_params_last_so_they_win() {
        let spec = TrimSpec {
            input: "in.mkv".into(),
            output: "out.mkv".into(),
            start: 0.0,
            end: 0.0, // 到结尾
            reencode: true,
            params: "-crf 18 -preset slow".into(),
            audio_bitrate: 320,
            ..Default::default()
        };
        let c = trim(&spec, T);
        // 起点 0 不写 -ss；终点未知不写 -t
        assert_eq!(
            c,
            "\"ffmpeg.exe\" -i \"in.mkv\" -c:v libx264 -preset medium -crf 20 -c:a aac -b:a 320k -crf 18 -preset slow -map_metadata 0 -y \"out.mkv\"\r\n"
        );
        // mkv 不加 faststart
        assert!(!c.contains("faststart"));
    }

    #[test]
    fn trim_audio_only_drops_video_stream() {
        let spec = TrimSpec {
            input: "in.mp4".into(),
            output: "out.m4a".into(),
            start: 1.0,
            end: 2.0,
            audio_only: true,
            reencode: false,
            ..Default::default()
        };
        let c = trim(&spec, T);
        assert!(c.contains(" -vn"));
        assert!(c.contains(" -c copy"));
        assert!(!c.contains("libx264"));
    }

    /* ============================================================== *
     * 插帧 / 超分
     * ============================================================== */

    /// `%08d` 在 .bat 里会被 cmd 当成 `%0` 展开成脚本自己的路径
    /// （实测：`...\lanzhutool_123.bat8d.png`）。
    /// 所以图片序列模式必须写 `%%08d`；把双百分号摘掉后还能找到单百分号 = 有地方写漏了。
    fn has_unescaped_percent(s: &str) -> bool {
        s.replace("%%08d", "").contains("%08d")
    }

    fn src_1080() -> EnhanceSource {
        EnhanceSource {
            fps: 23.976,
            width: 1920,
            height: 1080,
            has_audio: true,
        }
    }

    /// 两边都是内置滤镜 → 一条命令搞定，连临时文件都不需要。
    #[test]
    fn enhance_builtin_is_a_single_command() {
        let spec = EnhanceSpec {
            input: "in.mp4".into(),
            output: "out.mp4".into(),
            interp: true,
            interp_engine: "minterpolate".into(),
            // 0 = 按倍数推（23.976 × 2 = 47.952）
            interp_fps: 0.0,
            interp_mult: 2.0,
            upscale: true,
            upscale_engine: "lanczos".into(),
            upscale_mult: 2.0,
            ..Default::default()
        };
        let src = EnhanceSource {
            fps: 23.976,
            width: 640,
            height: 480,
            has_audio: true,
        };
        let lines = enhance_pipeline(&spec, &src, T, "C:\\TEMP");

        assert_eq!(lines.len(), 3, "{:?}", lines);
        assert_eq!(
            lines[1],
            "\"ffmpeg.exe\" -y -i \"in.mp4\" -sn -vf \"\
minterpolate=fps=47.952:mi_mode=mci:mc_mode=aobmc:me_mode=bidir:vsbmc=1,\
scale=1280:960:flags=lanczos\" \
-c:v libx264 -crf 18 -preset medium -pix_fmt yuv420p -c:a copy \"out.mp4\""
        );
        // 内置路径不拆帧，不该出现任何图片序列占位符
        assert!(!lines[1].contains("%08d"), "{}", lines[1]);
    }

    /// 只开超分时不该塞插帧滤镜；`blend` 模式不能带 mci 的那几个参数。
    #[test]
    fn enhance_filter_chain_reflects_enabled_steps() {
        let mut spec = EnhanceSpec {
            input: "in.mkv".into(),
            output: "out.mkv".into(),
            upscale: true,
            upscale_engine: "xbr".into(),
            upscale_mult: 2.0,
            ..Default::default()
        };
        let lines = enhance_pipeline(&spec, &src_1080(), T, "C:\\TEMP");
        assert!(lines[1].contains("-vf \"xbr=n=2\""), "{}", lines[1]);
        assert!(!lines[1].contains("minterpolate"), "{}", lines[1]);

        spec.interp = true;
        spec.interp_engine = "minterpolate".into();
        spec.interp_mode = "blend".into();
        spec.interp_fps = 60.0;
        let lines = enhance_pipeline(&spec, &src_1080(), T, "C:\\TEMP");
        assert!(
            lines[1].contains("-vf \"minterpolate=fps=60:mi_mode=blend,xbr=n=2\""),
            "{}",
            lines[1]
        );
        assert!(!lines[1].contains("mc_mode"), "blend 模式不该带运动补偿参数");
    }

    /// 本地模型：拆帧 → 插帧 → 超分 → 合帧，顺序固定；每一步的输出目录都要先建。
    #[test]
    fn enhance_model_pipeline_creates_dirs_and_escapes_percent() {
        let spec = EnhanceSpec {
            input: "C:\\in\\clip.mp4".into(),
            output: "C:\\in\\clip_rife2x_cugan2x.mp4".into(),
            interp: true,
            interp_engine: "rife".into(),
            interp_fps: 0.0,
            interp_mult: 2.0,
            upscale: true,
            upscale_engine: "realcugan".into(),
            upscale_mult: 2.0,
            cugan_model: "models-se".into(),
            ..Default::default()
        };
        let lines = enhance_pipeline(&spec, &src_1080(), T, "C:\\TEMP");
        let text = lines.join("\n");

        assert!(
            !has_unescaped_percent(&text),
            "图片序列必须写 %%08d，否则 cmd 会把 %0 展开成脚本路径：\n{}",
            text
        );
        assert!(
            text.contains("C:\\TEMP\\clip_enh\\f\\%%08d.png"),
            "抽帧路径不对:\n{}",
            text
        );

        let i_frames = lines.iter().position(|l| l.contains("clip_enh\\f\\%%08d.png")).unwrap();
        let i_rife = lines.iter().position(|l| l.contains("rife-ncnn-vulkan.exe")).unwrap();
        let i_cugan = lines.iter().position(|l| l.contains("realcugan-ncnn-vulkan.exe")).unwrap();
        let i_encode = lines.iter().position(|l| l.contains("-framerate")).unwrap();
        assert!(i_frames < i_rife, "先拆帧再插帧:\n{}", text);
        assert!(i_rife < i_cugan, "先插帧再超分:\n{}", text);
        assert!(i_cugan < i_encode, "最后才合帧:\n{}", text);

        // 输出目录必须先存在，否则 rife 报 "invalid outputpath extension type"
        for (line_no, dir) in [
            (i_rife, "clip_enh\\i"),
            (i_cugan, "clip_enh\\s"),
        ] {
            let mkdir = lines
                .iter()
                .take(line_no)
                .any(|l| l.starts_with("if not exist") && l.contains(dir));
            assert!(mkdir, "{} 之前没有 mkdir:\n{}", dir, text);
        }

        // 2 倍是 rife 的默认行为，故意不传 -n（这样 rife-anime 那种非 v4 模型也能用）。
        // 注意只看 rife 那一行 —— realcugan 的降噪参数恰好也叫 `-n`。
        let rife_line = &lines[i_rife];
        assert!(!rife_line.contains(" -n "), "2 倍不该传 -n: {}", rife_line);
        assert!(!text.contains("set /a"), "不需要数帧数:\n{}", text);

        // 合帧：帧率是算好的，音轨从第 2 个输入拿
        assert!(text.contains("-framerate 47.952"), "{}", text);
        assert!(text.contains("-map 0:v:0 -map 1:a:0?"), "{}", text);
        assert!(text.contains("-c:a copy"), "{}", text);

        // 上一轮跑到一半被终止的话会留下旧 PNG，数帧/插帧都会错，必须先清空
        let i_clean = lines
            .iter()
            .position(|l| l.contains("rmdir /s /q") && l.contains("clip_enh\""))
            .expect("缺少清理工作目录的步骤");
        assert!(i_clean < i_frames, "清理必须发生在抽帧之前:\n{}", text);
        assert!(lines.last().unwrap().contains("one file is completed"), "{}", text);
    }

    /// 自定义目标帧率：只能数出实际帧数再算，`-n` 是**目标总帧数**。
    #[test]
    fn enhance_rife_custom_fps_counts_frames() {
        let spec = EnhanceSpec {
            input: "in.mp4".into(),
            output: "out.mp4".into(),
            interp: true,
            interp_engine: "rife".into(),
            interp_fps: 60.0,
            upscale: false,
            audio: "none".into(),
            ..Default::default()
        };
        let src = EnhanceSource {
            fps: 24.0,
            width: 1280,
            height: 720,
            has_audio: true,
        };
        let text = enhance_pipeline(&spec, &src, T, "C:\\TEMP").join("\n");

        // 60/24 约分为 5/2，向上取整 → (LC*5+1)/2
        assert!(text.contains("set /a LT=(LC*5+1)/2"), "{}", text);
        assert!(text.contains(" -n %LT%"), "{}", text);
        assert!(text.contains("-framerate 60"), "{}", text);
        // 容器里明明有音轨，但用户选了「丢弃音频」
        assert!(text.contains(" -an "), "{}", text);
        assert!(!text.contains("-c:a copy"), "{}", text);
    }

    /// 倍数 3 倍也要走 `-n`（因为默认只有 2 倍）。
    #[test]
    fn enhance_rife_3x_uses_numframe_too() {
        let spec = EnhanceSpec {
            input: "in.mp4".into(),
            output: "out.mp4".into(),
            interp: true,
            interp_engine: "rife".into(),
            interp_fps: 0.0,
            interp_mult: 3.0,
            ..Default::default()
        };
        let src = EnhanceSource {
            fps: 23.976,
            width: 1920,
            height: 1080,
            has_audio: false,
        };
        let text = enhance_pipeline(&spec, &src, T, "C:\\TEMP").join("\n");
        // 23.976 × 3 = 71.928，71.928/23.976 过约分后是 3/1，向上取整 = (LC*3+0)/1
        assert!(text.contains("set /a LT=(LC*3+0)/1"), "{}", text);
        assert!(text.contains("-framerate 71.928"), "{}", text);
    }

    /// 权重目录必须跟**可执行文件同级**。
    /// 之前写成 `tools/models` —— 那是从压缩包解出来时的真实形状，
    /// 但 tools 根下根本没有 models，命令会直接失败。
    #[test]
    fn enhance_model_dir_follows_the_exe() {
        let dir = std::env::temp_dir().join("lanzhutool_enh_model_test");
        std::fs::remove_dir_all(&dir).ok();
        std::fs::create_dir_all(dir.join("rife-v4.6")).unwrap();
        std::fs::write(dir.join("rife-ncnn-vulkan.exe"), b"x").unwrap();
        let tools = dir.to_string_lossy().to_string();

        let spec = EnhanceSpec {
            input: "in.mp4".into(),
            output: "out.mp4".into(),
            interp: true,
            interp_engine: "rife".into(),
            interp_fps: 60.0,
            ..Default::default()
        };
        let text = enhance_pipeline(&spec, &src_1080(), &tools, "C:\\TEMP").join("\n");

        let want = format!("-m \"{}\"", dir.join("rife-v4.6").to_string_lossy());
        assert!(text.contains(&want), "期望 {}：\n{}", want, text);

        crate::tools::reset_cache();
        std::fs::remove_dir_all(&dir).ok();
    }

    /// 后缀把「用了什么引擎 + 多大倍数」写进文件名，一眼看得懂。
    #[test]
    fn enhance_suffix_names_engines_and_multipliers() {
        let mut s = EnhanceSpec {
            interp: true,
            interp_engine: "rife".into(),
            interp_fps: 0.0,
            interp_mult: 2.0,
            upscale: true,
            upscale_engine: "realcugan".into(),
            upscale_mult: 2.0,
            ..Default::default()
        };
        assert_eq!(enhance_suffix(&s), "_rife2x_cugan2x");

        s.interp_engine = "minterpolate".into();
        s.interp_fps = 60.0;
        assert_eq!(enhance_suffix(&s), "_fi60fps_cugan2x");

        s.upscale = false;
        s.interp_fps = 0.0;
        s.interp_mult = 3.0;
        assert_eq!(enhance_suffix(&s), "_fi3x");

        s.upscale = true;
        s.upscale_engine = "realesrgan".into();
        assert_eq!(enhance_suffix(&s), "_fi3x_esr2x");
    }

    /// 默认输出：前缀在源文件旁边，重名要退避 —— 绝不能覆盖源文件。
    #[test]
    fn default_enhance_output_avoids_overwrite() {
        let dir = std::env::temp_dir().join("lanzhutool_enh_naming_test");
        std::fs::remove_dir_all(&dir).ok();
        std::fs::create_dir_all(&dir).unwrap();
        let input = dir.join("v.mp4");
        std::fs::write(&input, b"x").unwrap();

        let spec = EnhanceSpec {
            input: input.to_string_lossy().to_string(),
            interp: true,
            interp_engine: "rife".into(),
            interp_fps: 0.0,
            interp_mult: 2.0,
            ..Default::default()
        };
        let out = default_enhance_output(&spec);
        assert_eq!(
            std::path::Path::new(&out).file_name().unwrap().to_string_lossy(),
            "v_rife2x.mp4"
        );

        std::fs::write(dir.join("v_rife2x.mp4"), b"x").unwrap();
        let out = default_enhance_output(&spec);
        assert_eq!(
            std::path::Path::new(&out).file_name().unwrap().to_string_lossy(),
            "v_new_file(1)_rife2x.mp4"
        );

        std::fs::remove_dir_all(&dir).ok();
    }

    /// 容器决定默认编码器；`-j` 只接受 `a:b:c` 这种形状，别把界面的手滑送进命令行。
    #[test]
    fn enhance_encoder_and_jobs_defaults() {
        let mut spec = EnhanceSpec {
            upscale: true,
            upscale_engine: "lanczos".into(),
            container: "mkv".into(),
            ..Default::default()
        };
        let lines = enhance_pipeline(&spec, &src_1080(), T, "C:\\TEMP");
        assert!(lines[1].contains("-c:v libx265 -crf 20"), "{}", lines[1]);

        spec.container = "mp4".into();
        let lines = enhance_pipeline(&spec, &src_1080(), T, "C:\\TEMP");
        assert!(lines[1].contains("-c:v libx264 -crf 18"), "{}", lines[1]);

        // 手写编码器/参数时原样采用
        spec.encoder = "h264_nvenc".into();
        spec.encode_params = "-cq 23 -preset p5".into();
        let lines = enhance_pipeline(&spec, &src_1080(), T, "C:\\TEMP");
        assert!(
            lines[1].contains("-c:v h264_nvenc -cq 23 -preset p5"),
            "{}",
            lines[1]
        );

        assert_eq!(jobs_flag("2:2:2"), " -j 2:2:2");
        assert_eq!(jobs_flag(""), "");
        assert_eq!(jobs_flag("2:2"), "", "位数不对就不传");
        assert_eq!(jobs_flag("a:b:c"), "", "非数字就不传");
    }
}
