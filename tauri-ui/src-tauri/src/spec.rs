use serde::{Deserialize, Serialize};

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", default)]
pub struct VideoSpec {
    pub input: String,
    pub output: String,
    pub subtitle: String,
    /// 压制格式文本，取值见 VIDEO_FORMATS（"H.264 8bit" / "HEVC 10bit" ...）
    pub format: String,

    /// 0 = 自定义参数，1 = 质量(CRF/CQ)，2 = 二遍码率
    pub mode: u32,
    pub crf: f64,
    pub bitrate: f64,
    pub custom_params: String,
    pub extra_params: String,

    pub width: i64,
    pub height: i64,
    pub maintain_resolution: bool,

    pub seek: i64,
    pub frames: i64,
    pub threads: String,
    pub priority: i32,

    pub use_gpu: bool,
    pub hybrid: bool,
    /// "nvenc" | "qsv" | "amf"
    pub gpu_kind: String,
    pub gpu_index: i32,

    /// 0 压制音频 / 1 不压制音频 / 2 复制音频 / 3 外部音频
    pub audio_mode: i32,
    pub audio_params: String,
    pub container: String,
    pub auto_shutdown: bool,
}

impl Default for VideoSpec {
    fn default() -> Self {
        Self {
            input: String::new(),
            output: String::new(),
            subtitle: String::new(),
            format: "H.264 8bit".into(),
            mode: 1,
            crf: 23.5,
            bitrate: 800.0,
            custom_params: String::new(),
            extra_params: String::new(),
            width: 0,
            height: 0,
            maintain_resolution: false,
            seek: 0,
            frames: 0,
            threads: String::new(),
            priority: 2,
            use_gpu: false,
            hybrid: false,
            gpu_kind: "nvenc".into(),
            gpu_index: 0,
            audio_mode: 0,
            audio_params: "--abitrate 128".into(),
            container: "mp4".into(),
            auto_shutdown: false,
        }
    }
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", default)]
pub struct AudioSpec {
    pub input: String,
    pub output: String,
    /// 0 NeroAAC / 1 QAAC / 2 WAV / 3 ALAC / 4 FLAC / 5 FDKAAC / 6 AC3
    pub encoder: usize,
    pub use_bitrate: bool,
    pub bitrate: String,
    pub custom_params: String,
}

impl Default for AudioSpec {
    fn default() -> Self {
        Self {
            input: String::new(),
            output: String::new(),
            encoder: 0,
            use_bitrate: true,
            bitrate: "128".into(),
            custom_params: String::new(),
        }
    }
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", default)]
pub struct MuxSpec {
    pub video: String,
    pub audio: String,
    pub output: String,
    /// 裸流(.264/.h264/.hevc)时的帧率，取值 "auto" 或数字
    pub fps: String,
    /// 裸流时的像素宽高比，如 "32:27"
    pub par: String,
}

impl Default for MuxSpec {
    fn default() -> Self {
        Self {
            video: String::new(),
            audio: String::new(),
            output: String::new(),
            fps: "auto".into(),
            par: "1:1".into(),
        }
    }
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", default)]
pub struct ExtractSpec {
    pub input: String,
    pub output: String,
    /// "video" | "audio" | "track" | "mkv"
    pub kind: String,
    pub stream_index: i64,
}

impl Default for ExtractSpec {
    fn default() -> Self {
        Self {
            input: String::new(),
            output: String::new(),
            kind: "video".into(),
            stream_index: 0,
        }
    }
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", default)]
pub struct AvsSpec {
    pub script: String,
    pub script_path: String,
    /// AVS 走的是和普通视频完全一样的压制链路，只是输入换成 .avs
    pub spec: VideoSpec,
}

impl Default for AvsSpec {
    fn default() -> Self {
        Self {
            script: String::new(),
            script_path: String::new(),
            spec: VideoSpec::default(),
        }
    }
}

fn d_true() -> bool {
    true
}

fn d_max_log() -> u32 {
    4000
}

fn d_format() -> String {
    "H.264 8bit".into()
}

fn d_threads() -> String {
    "auto".into()
}

fn d_theme() -> String {
    "system".into()
}

fn d_accent() -> String {
    "lanzhu".into()
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", default)]
pub struct AppSettings {
    pub tools_dir: String,
    pub output_dir: String,
    pub language: String,
    /// 下载工具时依次尝试的镜像前缀；末尾留空串表示回落直连
    pub mirrors: Vec<String>,

    // ---- 编码默认值 ----
    #[serde(default = "d_threads")]
    pub threads_default: String,
    #[serde(default = "d_format")]
    pub default_format: String,

    // ---- 日志 ----
    #[serde(default = "d_true")]
    pub auto_scroll_log: bool,
    #[serde(default = "d_max_log")]
    pub max_log_lines: u32,

    // ---- 外观 ----
    /// "light" | "dark" | "system"
    #[serde(default = "d_theme")]
    pub theme: String,
    /// 强调色预设 id（见前端 lib/appearance.ts）
    #[serde(default = "d_accent")]
    pub accent: String,
    /// 自定义强调色 #rrggbb；非空时优先于 accent
    #[serde(default)]
    pub accent_custom: String,
    /// 自定义背景图绝对路径
    #[serde(default)]
    pub background: String,
    /// 界面缩放（0.85 / 1.0 / 1.15），只作用于字号与间距令牌
    #[serde(default = "d_one")]
    pub ui_scale: f64,

    // ---- 托盘 / 通知 ----
    #[serde(default)]
    pub close_to_tray: bool,
    #[serde(default)]
    pub minimize_to_tray: bool,
    #[serde(default = "d_true")]
    pub notify_on_finish: bool,
    #[serde(default = "d_true")]
    pub show_monitor: bool,

    // ---- 最近打开（MediaInfo / 粗剪） ----
    #[serde(default)]
    pub recent_files: Vec<String>,
}

fn d_one() -> f64 {
    1.0
}

impl Default for AppSettings {
    fn default() -> Self {
        Self {
            tools_dir: String::new(),
            output_dir: String::new(),
            language: "zh-CN".into(),
            mirrors: Vec::new(),
            threads_default: d_threads(),
            default_format: d_format(),
            auto_scroll_log: true,
            max_log_lines: d_max_log(),
            theme: d_theme(),
            accent: d_accent(),
            accent_custom: String::new(),
            background: String::new(),
            ui_scale: 1.0,
            close_to_tray: false,
            minimize_to_tray: false,
            notify_on_finish: true,
            show_monitor: true,
            recent_files: Vec::new(),
        }
    }
}

/// 粗剪（视频/音频通用）。
///
/// 时间单位一律是秒；`end <= start` 表示"一直到结尾"。
#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", default)]
pub struct TrimSpec {
    pub input: String,
    pub output: String,
    pub start: f64,
    pub end: f64,
    /// true = 只留音频（丢掉视频）
    pub audio_only: bool,
    /// true = 重编码（切点精确、可改码率）；false = 流复制（秒切、无损、快）
    pub reencode: bool,
    /// 重编码时的额外参数，如 "-crf 20 -preset 6"
    pub params: String,
    /// 重编码时的音频码率（kbps）
    pub audio_bitrate: u32,
}

impl Default for TrimSpec {
    fn default() -> Self {
        Self {
            input: String::new(),
            output: String::new(),
            start: 0.0,
            end: 0.0,
            audio_only: false,
            reencode: false,
            params: String::new(),
            audio_bitrate: 192,
        }
    }
}

/// 系统性能快照（CPU / 内存 / GPU），由 sysmon 后台线程刷新。
#[derive(Debug, Clone, Serialize, Deserialize, Default)]
#[serde(rename_all = "camelCase", default)]
pub struct SysStats {
    /// 0-100，-1 表示暂时取不到
    pub cpu: f64,
    pub mem_used: u64,
    pub mem_total: u64,
    /// 0-100，-1 表示取不到（非 N 卡且没有计数器权限等）
    pub gpu: f64,
    pub gpu_name: String,
    pub gpu_mem_used: u64,
    pub gpu_mem_total: u64,
    /// 本工具当前拉起的 ffmpeg 类进程数
    pub procs: u32,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct GpuInfo {
    pub index: i32,
    pub label: String,
    pub kind: String,
}

#[derive(Debug, Clone, Serialize, Deserialize, Default)]
#[serde(rename_all = "camelCase", default)]
pub struct VideoStreamInfo {
    pub codec: String,
    pub width: i64,
    pub height: i64,
    pub fps: f64,
    pub pix_fmt: String,
    pub bit_depth: i64,
}

#[derive(Debug, Clone, Serialize, Deserialize, Default)]
#[serde(rename_all = "camelCase", default)]
pub struct AudioStreamInfo {
    pub codec: String,
    pub channels: i64,
    pub sample_rate: i64,
    pub bitrate: i64,
}

#[derive(Debug, Clone, Serialize, Deserialize, Default)]
#[serde(rename_all = "camelCase", default)]
pub struct MediaInfo {
    pub path: String,
    pub exists: bool,
    pub container: String,
    pub duration_sec: f64,
    pub size_bytes: i64,
    pub bitrate: i64,
    pub video: Option<VideoStreamInfo>,
    pub audio: Option<AudioStreamInfo>,
    pub raw: String,
}
