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

use crate::spec::{AudioSpec, ExtractSpec, MuxSpec, TrimSpec, VideoSpec};
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
    let use_gpu = spec.use_gpu || spec.hybrid;

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
    let encoder: &str = if use_gpu {
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
    if !use_gpu {
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

    if spec.mode != 0 {
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
            temp_ext(&spec.format)
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

/// 镜像原版 `btnmux_Click`：ffmpeg 直封装，裸流时补帧率和 PAR。
pub fn mux(spec: &MuxSpec, tools: &str) -> String {
    let mut sb = String::new();
    sb.push_str(&quote(&tool(tools, "ffmpeg.exe")));

    let lower = spec.video.to_lowercase();
    let is_raw = lower.ends_with(".264") || lower.ends_with(".h264") || lower.ends_with(".hevc");

    if is_raw && !spec.fps.is_empty() && spec.fps != "auto" {
        sb.push_str(&format!(" -r {}", spec.fps));
    }

    sb.push_str(&format!(" -i \"{}\"", spec.video));
    if !spec.audio.is_empty() {
        sb.push_str(&format!(" -i \"{}\"", spec.audio));
    }

    sb.push_str(" -map 0:v -c:v copy");
    if !spec.audio.is_empty() {
        sb.push_str(" -map 1:a -c:a copy");
    }

    if is_raw && !spec.par.is_empty() && spec.par != "1:1" {
        if lower.ends_with(".hevc") {
            sb.push_str(&format!(" -bsf:v hevc_metadata=sample_aspect_ratio={}", spec.par));
        } else {
            sb.push_str(&format!(" -bsf:v h264_metadata=sample_aspect_ratio={}", spec.par));
        }
    }

    sb.push_str(&format!(" -sn -y \"{}\"", spec.output));
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
    use crate::spec::{AudioSpec, TrimSpec, VideoSpec};

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
        let spec = crate::spec::MuxSpec {
            video: "C:\\v.h264".into(),
            audio: "C:\\a.ac3".into(),
            output: "C:\\o.mp4".into(),
            fps: "23.976".into(),
            par: "32:27".into(),
        };
        let c = mux(&spec, T);
        assert_eq!(
            c,
            "\"ffmpeg.exe\" -r 23.976 -i \"C:\\v.h264\" -i \"C:\\a.ac3\" \
-map 0:v -c:v copy -map 1:a -c:a copy \
-bsf:v h264_metadata=sample_aspect_ratio=32:27 -sn -y \"C:\\o.mp4\"\r\n"
        );
    }

    #[test]
    fn mux_skips_raw_only_options_for_mp4() {
        let spec = crate::spec::MuxSpec {
            video: "v.mp4".into(),
            audio: String::new(),
            output: "o.mp4".into(),
            fps: "60".into(),
            par: "32:27".into(),
        };
        let c = mux(&spec, T);
        assert!(!c.contains("-r 60"), "非裸流不该加 -r: {}", c);
        assert!(!c.contains("metadata"), "非裸流不该加 bsf: {}", c);
        assert!(!c.contains("-map 1:a"), "没有音频就不该映射: {}", c);
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
}
