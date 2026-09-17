use serde::{Deserialize, Serialize};

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", default)]
pub struct VideoSpec {
    pub input: String,
    pub output: String,
    pub subtitle: String,
    /// 压制格式文本，取值见 VIDEO_FORMATS（"H.264 8bit" / "HEVC 10bit" ...）
    pub format: String,

    /// 0 = 自定义参数，1 = 质量(CRF/CQ)，2 = 二遍码率，3 = 压制预设
    pub mode: u32,
    pub crf: f64,
    pub bitrate: f64,
    pub custom_params: String,
    pub extra_params: String,

    // ---- 压制预设（mode = 3 时生效）----
    /// 预设名（只用于界面/日志）
    pub preset_name: String,
    /// 编码器，如 "libx264" / "prores_ks" / "libaom-av1"
    pub preset_encoder: String,
    /// 编码参数，原样拼到 `-c:v <encoder>` 后面
    pub preset_params: String,
    /// 容器扩展名（不带点），如 "mov" / "mkv"
    pub preset_container: String,

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
            preset_name: String::new(),
            preset_encoder: String::new(),
            preset_params: String::new(),
            preset_container: String::new(),
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
    /// 外部音频文件（可以有多个，按顺序映射成多条音轨）
    pub audios: Vec<String>,
    pub output: String,
    /// 裸流(.264/.h264/.hevc)时的帧率，取值 "auto" 或数字
    pub fps: String,
    /// 裸流时的像素宽高比，如 "32:27"
    pub par: String,
    /// 是否把源文件自带的音轨也带上（关掉=用外部音频替掉源音轨）
    #[serde(default = "d_true")]
    pub keep_source_audio: bool,
    /// 输出容器（mp4/mkv/mov/flv/avi/f4v），决定扩展名与 faststart
    #[serde(default = "d_mp4")]
    pub format: String,
}

fn d_mp4() -> String {
    "mp4".into()
}

impl Default for MuxSpec {
    fn default() -> Self {
        Self {
            video: String::new(),
            audios: Vec::new(),
            output: String::new(),
            fps: "auto".into(),
            par: "1:1".into(),
            keep_source_audio: true,
            format: d_mp4(),
        }
    }
}

/// 批量封装 / 转换（原版 `btnBatchMP4_Click`）。
#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", default)]
pub struct BatchMuxSpec {
    pub inputs: Vec<String>,
    /// 目标容器：mp4 / mkv / mov / flv / avi / f4v
    pub format: String,
    /// 音频不是 AAC 且目标容器不是 mkv 时，用哪个 AAC 编码器转码
    pub aac_encoder: String,
    /// 输出目录；留空则写在源文件旁边
    pub output_dir: String,
}

impl Default for BatchMuxSpec {
    fn default() -> Self {
        Self {
            inputs: Vec::new(),
            format: "mp4".into(),
            aac_encoder: "aac".into(),
            output_dir: String::new(),
        }
    }
}

/// 一条烂梗。
#[derive(Debug, Clone, Serialize, Deserialize, Default)]
#[serde(rename_all = "camelCase", default)]
pub struct Meme {
    pub text: String,
    /// 实际取到内容的来源（空=内置兜底）
    pub source: String,
    /// 本次取的是内置兜底文案（网站没连上）
    pub fallback: bool,
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

/// 插帧 / 超分。
///
/// 这里有两条**本质不同**的路子，界面上要让用户看得见这个区别：
///
/// - **内置**（`minterpolate` / `scale` / `xbr`）：纯 ffmpeg 滤镜，
///   不需要任何额外文件，装完就能用；代价是画质与速度都一般，
///   而且 `minterpolate` 是纯 CPU 的（很慢）。
/// - **本地模型**（`rife` / `realesrgan` / `realcugan`）：tools/ 下的
///   ncnn-vulkan 可执行文件 + 权重文件，跑在 Vulkan 上，
///   **不需要 CUDA / Python / PyTorch**，核显也能跑。
///   代价是必须把视频拆成图片序列、处理完再重新编码（中间要过一遍 PNG）。
///
/// 两种可以混搭（例如 RIFE 插帧 + 内置 lanczos 超分），
/// 只要有一边用了模型就整条走"拆帧"流程。
#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", default)]
pub struct EnhanceSpec {
    pub input: String,
    pub output: String,

    // ---- 插帧 ----
    #[serde(default)]
    pub interp: bool,
    /// 引擎 id：`minterpolate`（内置）/ `rife`（本地模型）
    #[serde(default = "d_minterpolate")]
    pub interp_engine: String,
    /// 目标帧率；> 0 时优先，0 表示按 `interp_mult` 倍数推
    #[serde(default)]
    pub interp_fps: f64,
    /// 帧率倍数
    #[serde(default = "d_two")]
    pub interp_mult: f64,
    /// rife 的模型目录名（rife-v4.6 / rife-anime …）
    #[serde(default = "d_rife_model")]
    pub interp_model: String,
    /// minterpolate 的 `mi_mode`：mci（运动补偿）/ blend / dup
    #[serde(default = "d_mci")]
    pub interp_mode: String,
    /// rife 的 TTA 模式（更准、慢一倍）
    #[serde(default)]
    pub interp_tta: bool,

    // ---- 超分 ----
    #[serde(default)]
    pub upscale: bool,
    /// 引擎 id：`lanczos` / `xbr`（内置）/ `realesrgan` / `realcugan`（本地模型）
    #[serde(default = "d_lanczos")]
    pub upscale_engine: String,
    /// 放大倍数
    #[serde(default = "d_two")]
    pub upscale_mult: f64,
    /// 最终目标分辨率；0 = 保持放大后的原始尺寸
    #[serde(default)]
    pub width: i64,
    #[serde(default)]
    pub height: i64,
    /// realesrgan 的模型名（realesr-animevideov3 / realesrgan-x4plus / …）
    #[serde(default = "d_esr_model")]
    pub upscale_model: String,
    /// realcugan 的模型目录名（models-se / models-pro / models-nose）
    #[serde(default = "d_cugan_model")]
    pub cugan_model: String,
    /// realcugan 的降噪等级：-1 关闭 / 0..3 越大越强
    #[serde(default = "d_neg_one")]
    pub cugan_denoise: i32,

    // ---- 模型推理 ----
    /// Vulkan 设备序号；-1 = 强制走 CPU（慢，但没有可用显卡时的兜底）
    #[serde(default)]
    pub gpu_index: i32,
    /// `-j load:proc:save`；大分辨率要调小，小图多线程反而更快
    #[serde(default = "d_jobs")]
    pub jobs: String,

    // ---- 音频 / 编码 ----
    /// 音轨处理：`copy` 复制 / `aac` 转 AAC / `none` 丢弃
    #[serde(default = "d_copy")]
    pub audio: String,
    /// 编码器，留空按 `container` 推（mp4 → libx264，mkv → libx265? 不，仍 libx264）
    #[serde(default)]
    pub encoder: String,
    /// 编码参数，原样拼在 `-c:v <encoder>` 后面
    #[serde(default)]
    pub encode_params: String,
    /// 容器扩展名（不带点）
    #[serde(default = "d_mp4_ext")]
    pub container: String,
}

fn d_minterpolate() -> String {
    "minterpolate".into()
}
fn d_mci() -> String {
    "mci".into()
}
fn d_rife_model() -> String {
    "rife-v4.6".into()
}
fn d_lanczos() -> String {
    "lanczos".into()
}
fn d_esr_model() -> String {
    "realesr-animevideov3".into()
}
fn d_cugan_model() -> String {
    "models-se".into()
}
fn d_two() -> f64 {
    2.0
}
fn d_neg_one() -> i32 {
    -1
}
fn d_jobs() -> String {
    "2:2:2".into()
}
fn d_copy() -> String {
    "copy".into()
}
fn d_mp4_ext() -> String {
    "mp4".into()
}

impl Default for EnhanceSpec {
    fn default() -> Self {
        Self {
            input: String::new(),
            output: String::new(),
            interp: false,
            interp_engine: d_minterpolate(),
            interp_fps: 60.0,
            interp_mult: d_two(),
            interp_model: d_rife_model(),
            interp_mode: d_mci(),
            interp_tta: false,
            upscale: false,
            upscale_engine: d_lanczos(),
            upscale_mult: d_two(),
            width: 0,
            height: 0,
            upscale_model: d_esr_model(),
            cugan_model: d_cugan_model(),
            cugan_denoise: d_neg_one(),
            gpu_index: 0,
            jobs: d_jobs(),
            audio: d_copy(),
            encoder: String::new(),
            encode_params: String::new(),
            container: d_mp4_ext(),
        }
    }
}

/// 一个本地模型工具的就绪情况（给界面的「模型状态」卡片用）。
#[derive(Debug, Clone, Serialize, Deserialize, Default)]
#[serde(rename_all = "camelCase", default)]
pub struct ModelTool {
    /// `rife` | `realesrgan` | `realcugan`
    pub id: String,
    pub name: String,
    /// 可执行文件绝对路径；空 = 没装
    pub exe: String,
    /// 能用的模型（rife 是模型目录名，realesrgan 是模型名）
    pub models: Vec<String>,
    /// 这些模型里哪些支持自定义帧数（只有 rife-v4 系支持 `-n`）
    pub custom_frames: Vec<String>,
    /// 说明（界面上灰字显示）
    pub hint: String,
}

fn d_true() -> bool {
    true
}

fn d_max_log() -> u32 {
    4000
}

fn d_log_level() -> String {
    "all".into()
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

/// 一条压制预设。
///
/// 内置预设写在前端 `lib/encodePresets.ts` 里（改起来快、不用重编译），
/// 用户自建的存进设置文件 —— 所以这里是 serde 全字段的镜像结构。
#[derive(Debug, Clone, Serialize, Deserialize, Default)]
#[serde(rename_all = "camelCase", default)]
pub struct EncodePreset {
    pub id: String,
    pub name: String,
    /// 分类：常用 / 网络 / 中间格式 / 存档 / 设备
    pub group: String,
    pub desc: String,
    /// 容器扩展名（不带点）：mp4 / mov / mkv / avi
    pub container: String,
    /// 编码器：libx264 / libx265 / prores_ks / dnxhd / libaom-av1 / libsvtav1 …
    pub encoder: String,
    /// 编码参数，原样拼到 `-c:v <encoder>` 后面
    pub params: String,
    /// 目标分辨率；0 = 保持原分辨率
    pub width: i64,
    pub height: i64,
    /// 目标帧率；0 = 不改变
    pub fps: f64,
    /// 推荐用：源高度 ≥ 这个值时才推荐（避免把小片子放大）
    pub min_source_height: i64,
    /// true = 内置预设（不可删除）
    pub builtin: bool,
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
    /// 记录范围：all | warn | error
    #[serde(default = "d_log_level")]
    pub log_level: String,

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

    // ---- 启动画面 / 彩蛋 ----
    /// 启动时显示 splash；关掉就直接进主界面
    #[serde(default = "d_true")]
    pub show_splash: bool,
    /// 烂梗来源 URL；留空则用内置候选列表
    #[serde(default)]
    pub meme_url: String,

    // ---- 压制预设（用户自建）----
    #[serde(default)]
    pub presets: Vec<EncodePreset>,
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
            log_level: d_log_level(),
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
            show_splash: true,
            meme_url: String::new(),
            presets: Vec::new(),
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
