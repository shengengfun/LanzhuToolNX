use crate::spec::{AudioStreamInfo, MediaInfo, VideoStreamInfo};
use crate::tools::tool;
use std::process::Command;

/// 用 ffprobe 探测媒体信息。
///
/// 原版用的是 MediaInfoDLL（进程内 COM）；这里改用 ffprobe 的 JSON 输出，
/// 好处是不用 COM、不用额外的 MediaInfo.exe，字段也更规整。
/// `raw` 里仍然保留 ffprobe 原文，方便"MediaInfo"页直接展示。
pub fn probe(tools: &str, path: &str) -> MediaInfo {
    let mut info = MediaInfo {
        path: path.to_string(),
        ..Default::default()
    };

    let exists = std::path::Path::new(path).is_file();
    info.exists = exists;
    if !exists {
        info.raw = "文件不存在、非有效文件或者文件夹 无视频信息".into();
        return info;
    }

    if let Ok(md) = std::fs::metadata(path) {
        info.size_bytes = md.len() as i64;
    }

    let out = Command::new(tool(tools, "ffprobe.exe"))
        .args([
            "-v",
            "quiet",
            "-print_format",
            "json",
            "-show_format",
            "-show_streams",
        ])
        .arg(path)
        .output();

    let Ok(out) = out else {
        info.raw = "ffprobe 启动失败，请检查工具目录".into();
        return info;
    };

    let text = String::from_utf8_lossy(&out.stdout).to_string();
    info.raw = text.clone();

    let Ok(json) = serde_json::from_str::<serde_json::Value>(&text) else {
        info.raw = format!("ffprobe 输出无法解析：\n{}", text);
        return info;
    };

    if let Some(fmt) = json.get("format") {
        info.container = fmt
            .get("format_name")
            .and_then(|v| v.as_str())
            .unwrap_or("")
            .to_string();
        info.duration_sec = fmt
            .get("duration")
            .and_then(|v| v.as_str())
            .and_then(|s| s.parse::<f64>().ok())
            .unwrap_or(0.0);
        info.bitrate = fmt
            .get("bit_rate")
            .and_then(|v| v.as_str())
            .and_then(|s| s.parse::<i64>().ok())
            .unwrap_or(0);
    }

    if let Some(streams) = json.get("streams").and_then(|v| v.as_array()) {
        for s in streams {
            let kind = s.get("codec_type").and_then(|v| v.as_str()).unwrap_or("");
            match kind {
                "video" if info.video.is_none() => {
                    let pix = s
                        .get("pix_fmt")
                        .and_then(|v| v.as_str())
                        .unwrap_or("")
                        .to_string();
                    let bit_depth = if pix.contains("12") {
                        12
                    } else if pix.contains("10") {
                        10
                    } else {
                        8
                    };
                    info.video = Some(VideoStreamInfo {
                        codec: s
                            .get("codec_name")
                            .and_then(|v| v.as_str())
                            .unwrap_or("")
                            .to_uppercase(),
                        width: s.get("width").and_then(|v| v.as_i64()).unwrap_or(0),
                        height: s.get("height").and_then(|v| v.as_i64()).unwrap_or(0),
                        fps: parse_fps(
                            s.get("avg_frame_rate").and_then(|v| v.as_str()).unwrap_or(""),
                        ),
                        pix_fmt: pix,
                        bit_depth,
                    });
                }
                "audio" if info.audio.is_none() => {
                    info.audio = Some(AudioStreamInfo {
                        codec: s
                            .get("codec_name")
                            .and_then(|v| v.as_str())
                            .unwrap_or("")
                            .to_uppercase(),
                        channels: s.get("channels").and_then(|v| v.as_i64()).unwrap_or(0),
                        sample_rate: s
                            .get("sample_rate")
                            .and_then(|v| v.as_str())
                            .and_then(|x| x.parse::<i64>().ok())
                            .unwrap_or(0),
                        bitrate: s
                            .get("bit_rate")
                            .and_then(|v| v.as_str())
                            .and_then(|x| x.parse::<i64>().ok())
                            .unwrap_or(0),
                    });
                }
                _ => {}
            }
        }
    }

    info
}

fn parse_fps(s: &str) -> f64 {
    let mut it = s.split('/');
    let a = it.next().and_then(|v| v.parse::<f64>().ok()).unwrap_or(0.0);
    let b = it.next().and_then(|v| v.parse::<f64>().ok()).unwrap_or(0.0);
    if b > 0.0 {
        (a / b * 1000.0).round() / 1000.0
    } else {
        a
    }
}
