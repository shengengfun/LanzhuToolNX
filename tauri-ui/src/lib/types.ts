/*
 * 与 src-tauri/src/spec.rs 一一对应的类型。
 * 后端按这份 spec 拼出和原版完全一致的命令行。
 */

export type VideoPresetKind = 'H264' | 'HEVC' | 'MOV' | 'FLV'

/** 「压制格式」下拉项，与原版 x264ExeComboBox 完全一致。 */
export const VIDEO_FORMATS = [
  'H.264 8bit',
  'H.264 10bit',
  'H.264 12bit',
  'HEVC 8bit',
  'HEVC 10bit',
  'HEVC 12bit',
  'MOV',
  'FLV',
] as const

export const AUDIO_ENCODERS = ['NeroAAC', 'QAAC', 'WAV', 'ALAC', 'FLAC', 'FDKAAC', 'AC3'] as const
export const AUDIO_BITRATES = ['64', '96', '128', '160', '192', '256', '320'] as const

export const AUDIO_MODES = ['压制音频', '不压制音频', '复制音频', '外部音频'] as const
export const DEMUXERS = ['auto', 'ffmpeg', 'mkvextract', 'MP4Box'] as const
export const CONTAINERS = ['mp4', 'mkv', 'mov', 'flv'] as const

/** GPU 后端；原版靠下拉项里的 (AMF)/(QSV) 字样识别。 */
export type GpuKind = 'nvenc' | 'qsv' | 'amf'

export interface VideoSpec {
  input: string
  output: string
  subtitle: string
  /** 「压制格式」文本，如 "H.264 8bit" / "HEVC 10bit" */
  format: string

  /** 0=自定义参数 1=质量(CRF) 2=二遍码率 3=压制预设 */
  mode: 0 | 1 | 2 | 3
  crf: number
  bitrate: number
  customParams: string
  extraParams: string

  // ---- 压制预设（mode = 3 时生效）----
  presetName: string
  presetEncoder: string
  presetParams: string
  presetContainer: string

  width: number
  height: number
  maintainResolution: boolean

  seek: number
  frames: number
  threads: string
  priority: number

  useGpu: boolean
  hybrid: boolean
  gpuKind: GpuKind
  gpuIndex: number

  audioMode: number
  audioParams: string
  container: string
  autoShutdown: boolean
}

export interface AudioSpec {
  input: string
  output: string
  /** AudioEncoderComboBox 的索引：0 NeroAAC / 1 QAAC / 2 WAV / 3 ALAC / 4 FLAC / 5 FDKAAC / 6 AC3 */
  encoder: number
  /** true = 用码率，false = 用自定义参数 */
  useBitrate: boolean
  bitrate: string
  customParams: string
}

export interface MuxSpec {
  video: string
  /** 外部音频文件（可以有多个，按顺序映射成多条音轨） */
  audios: string[]
  output: string
  /** 裸流时的帧率，"auto" 或数字 */
  fps: string
  /** 裸流时的像素宽高比，如 "32:27" */
  par: string
  /** 是否保留源文件自带的音轨（关掉=用外部音频替掉源音轨） */
  keepSourceAudio: boolean
  /** 输出容器（mp4/mkv/mov/flv/avi/f4v） */
  format: string
}

/** 批量封装 / 容器转换（原版「封装转换」）。 */
export interface BatchMuxSpec {
  inputs: string[]
  /** 目标容器：mp4 / mkv / mov / flv / avi / f4v */
  format: string
  /** 音频不是 AAC 且目标不是 mkv 时，用哪个 AAC 编码器转码 */
  aacEncoder: string
  /** 输出目录；留空则写在源文件旁边 */
  outputDir: string
}

/** 一条烂梗。 */
export interface Meme {
  text: string
  /** 实际取到内容的来源 */
  source: string
  /** true = 网站没连上，用的是内置文案 */
  fallback: boolean
}

/** 目标容器列表（原版 MuxFormatComboBox 的六个选项）。 */
export const MUX_FORMATS = ['mp4', 'mkv', 'mov', 'flv', 'avi', 'f4v'] as const

/** AAC 编码器（原版 MuxAacEncoderComboBox）。 */
export const AAC_ENCODERS = ['aac', 'libfdk_aac', 'libfaac'] as const

export interface ExtractSpec {
  input: string
  output: string
  /** video=抽视频轨 / audio=抽音频轨 / track=抽指定流 / mkv=mkvextract 抽轨 */
  kind: 'video' | 'audio' | 'track' | 'mkv'
  streamIndex: number
}

export interface AvsSpec {
  /** AVS 脚本全文 */
  script: string
  /** 脚本写到哪个文件；后端先落盘再把它当输入喂给 ffmpeg */
  scriptPath: string
  /** 压制参数，和普通视频完全一致 */
  spec: VideoSpec
}

export interface RunRequest {
  /** 已拼好的完整命令行；每行一条 */
  commands: string
  /** 工作目录，工具所在处 */
  cwd: string
  /** 同时执行的任务数，用于进度条分母 */
  workCount: number
}

/* ================================================================== *
 * 插帧 / 超分
 *
 * 引擎分两类：
 *   - 内置 = ffmpeg 滤镜，装完就能用；
 *   - 本地模型 = tools/ 下的 ncnn-vulkan 程序 + 权重文件，
 *     必须走「拆帧 → 推理 → 合帧」三趟。
 * `kind` 决定界面上的分组和提示，也是判断要不要拆帧的依据。
 * ================================================================== */

export type EnhanceEngineKind = 'builtin' | 'model'

export interface EnhanceEngine {
  id: string
  label: string
  kind: EnhanceEngineKind
  /** 一句话说明，直接显示在界面上 */
  desc: string
  /** 可用倍数；realesrgan/realcugan 受模型限制，由 `scalesOfModel` 再收一次 */
  scales: readonly number[]
  /** 本地模型时才有的可执行文件名（用于跟 enhance_tools 的结果做匹配） */
  tool?: 'rife' | 'realesrgan' | 'realcugan'
}

export const INTERP_ENGINES: readonly EnhanceEngine[] = [
  {
    id: 'minterpolate',
    label: '运动补偿插值（内置）',
    kind: 'builtin',
    desc: 'ffmpeg 的 minterpolate。不需要任何额外文件，但只用 CPU，慢且容易在快速运动处出鬼影。',
    scales: [2, 3, 4],
  },
  {
    id: 'rife',
    label: 'RIFE（本地模型 · 推荐）',
    kind: 'model',
    desc: '实时中间流估计，动画/实拍都稳。跑在 Vulkan 上，核显也能用，不需要 CUDA 或 Python。',
    scales: [2, 3, 4],
    tool: 'rife',
  },
]

/** minterpolate 的 mi_mode。`mci` 才真的"算"出中间帧。 */
export const INTERP_MODES = [
  { id: 'mci', label: 'mci 运动补偿（默认）', desc: '画质最好，也最慢' },
  { id: 'blend', label: 'blend 混帧', desc: '两条帧叠一半，快但会糊' },
  { id: 'dup', label: 'dup 复制帧', desc: '只补时间轴不补画面' },
] as const

export const UPSCALE_ENGINES: readonly EnhanceEngine[] = [
  {
    id: 'lanczos',
    label: 'Lanczos（内置）',
    kind: 'builtin',
    desc: 'ffmpeg 自带的高质量重采样。快、稳、不挑素材，但只是"放大"，不会补线条细节。',
    scales: [2, 3, 4],
  },
  {
    id: 'xbr',
    label: 'xBR（内置）',
    kind: 'builtin',
    desc: '针对线条和像素画的放大算法。2D 动画效果好，真人实拍会显得很"脏"。',
    scales: [2, 3, 4],
  },
  {
    id: 'hqx',
    label: 'hqx（内置）',
    kind: 'builtin',
    desc: '和 xBR 同类的像素画放大，边缘更硬。同样只推荐用在 2D 动画上。',
    scales: [2, 3, 4],
  },
  {
    id: 'realesrgan',
    label: 'Real-ESRGAN（本地模型）',
    kind: 'model',
    desc: '通用超分网络，提供实拍与动画两套权重。realesrgan-x4plus 系只有 4 倍。',
    scales: [4],
    tool: 'realesrgan',
  },
  {
    id: 'realcugan',
    label: 'Real-CUGAN（本地模型 · 动画向）',
    kind: 'model',
    desc: '动画专用，自带降噪等级；models-se 最均衡，models-nose 只能 2 倍。',
    scales: [2, 3, 4],
    tool: 'realcugan',
  },
]

/**
 * Real-ESRGAN 的模型名与它支持的倍数。
 * 权重文件名形如 `realesr-animevideov3-x2`，`-x4plus` 那套只有 4 倍。
 */
export const ESR_MODELS = [
  { id: 'realesr-animevideov3', label: 'realesr-animevideov3（动画·轻量）', scales: [2, 3, 4] },
  { id: 'realesrgan-x4plus', label: 'realesrgan-x4plus（通用重模型）', scales: [4] },
  { id: 'realesrgan-x4plus-anime', label: 'realesrgan-x4plus-anime（动画重模型）', scales: [4] },
] as const

export const CUGAN_MODELS = [
  { id: 'models-se', label: 'models-se（均衡，推荐）', scales: [2, 3, 4] },
  { id: 'models-pro', label: 'models-pro（更强，更慢）', scales: [2, 3, 4] },
  { id: 'models-nose', label: 'models-nose（不降噪，仅 2 倍）', scales: [2] },
] as const

/** Real-CUGAN 的降噪等级（`-n`）。 */
export const CUGAN_DENOISE = [
  { value: -1, label: '-1 关闭' },
  { value: 0, label: '0 最轻' },
  { value: 1, label: '1' },
  { value: 2, label: '2' },
  { value: 3, label: '3 最强' },
] as const

/** 编码预设：encoder + 参数，后端原样拼接。 */
export const ENHANCE_ENCODERS = [
  { id: 'h264-hq', label: 'H.264 高质量（CRF 18）', encoder: 'libx264', params: '-crf 18 -preset medium -pix_fmt yuv420p' },
  { id: 'h264-fast', label: 'H.264 快速（CRF 20）', encoder: 'libx264', params: '-crf 20 -preset veryfast -pix_fmt yuv420p' },
  { id: 'h265-10bit', label: 'HEVC 10bit（CRF 20）', encoder: 'libx265', params: '-crf 20 -preset medium -pix_fmt yuv420p10le' },
  { id: 'custom', label: '自定义参数', encoder: '', params: '' },
] as const

export const ENHANCE_AUDIO = [
  { id: 'copy', label: '复制原音轨（最快）' },
  { id: 'aac', label: '转 AAC 192k' },
  { id: 'none', label: '不要音轨' },
] as const

export const ENHANCE_CONTAINERS = ['mp4', 'mkv', 'mov'] as const

export interface EnhanceSpec {
  input: string
  output: string

  // ---- 插帧 ----
  interp: boolean
  interpEngine: string
  /** > 0 时优先于 interpMult */
  interpFps: number
  interpMult: number
  interpModel: string
  interpMode: string
  interpTta: boolean

  // ---- 超分 ----
  upscale: boolean
  upscaleEngine: string
  upscaleMult: number
  /** 0 = 保持放大后的尺寸 */
  width: number
  height: number
  upscaleModel: string
  cuganModel: string
  cuganDenoise: number

  // ---- 推理 ----
  gpuIndex: number
  jobs: string

  // ---- 音频 / 编码 ----
  audio: string
  encoder: string
  encodeParams: string
  container: string
}

/** 一个本地模型工具的就绪情况（`enhance_tools` 的返回）。 */
export interface ModelTool {
  id: 'rife' | 'realesrgan' | 'realcugan' | string
  name: string
  /** 空 = 没装 */
  exe: string
  models: string[]
  /** 支持自定义帧数的模型（只有 rife-v4 系） */
  customFrames: string[]
  hint: string
}


export interface MediaInfo {
  path: string
  exists: boolean
  container: string
  durationSec: number
  sizeBytes: number
  bitrate: number
  video: {
    codec: string
    width: number
    height: number
    fps: number
    pixFmt: string
    bitDepth: number
  } | null
  audio: {
    codec: string
    channels: number
    sampleRate: number
    bitrate: number
  } | null
  raw: string
}

export interface AppSettings {
  toolsDir: string
  outputDir: string
  language: string
  /** 下载工具时依次尝试的镜像前缀；末尾留空串表示回落直连 */
  mirrors: string[]

  // ---- 编码默认值 ----
  threadsDefault: string
  defaultFormat: string

  // ---- 日志 ----
  autoScrollLog: boolean
  maxLogLines: number
  /** 记录范围：all | warn | error */
  logLevel: string

  // ---- 外观 ----
  /** 'light' | 'dark' | 'system' */
  theme: string
  /** 强调色预设 id，见 lib/appearance.ts */
  accent: string
  /** 自定义强调色 #rrggbb，非空时优先 */
  accentCustom: string
  /** 自定义背景图绝对路径 */
  background: string
  /** 界面缩放 0.9 / 1 / 1.1 */
  uiScale: number

  // ---- 托盘 / 通知 ----
  closeToTray: boolean
  minimizeToTray: boolean
  notifyOnFinish: boolean
  /** 底栏显示 CPU/GPU/内存占用 */
  showMonitor: boolean

  // ---- 最近打开 ----
  recentFiles: string[]

  // ---- 启动画面 / 彩蛋 ----
  /** 启动时显示 splash；关掉就直接进主界面 */
  showSplash: boolean
  /** 烂梗来源 URL；留空用内置候选 */
  memeUrl: string

  // ---- 压制预设（用户自建） ----
  presets: EncodePreset[]
}

/**
 * 一条压制预设。
 *
 * 内置的写在前端 `lib/encodePresets.ts`，用户自建的存进设置文件，
 * 两边共用一个结构；`builtin` 用来决定能不能删/改。
 */
export interface EncodePreset {
  id: string
  name: string
  /** 分类：常用 / 网络 / 中间格式 / 存档 / 设备 */
  group: string
  desc: string
  /** 容器扩展名（不带点）：mp4 / mov / mkv / avi */
  container: string
  /** 编码器：libx264 / libx265 / prores_ks / dnxhd / libsvtav1 … */
  encoder: string
  /** 编码参数，原样拼到 `-c:v <encoder>` 后面 */
  params: string
  /** 目标分辨率；0 = 保持原分辨率 */
  width: number
  height: number
  /** 目标帧率；0 = 不改变 */
  fps: number
  /** 推荐用：源高度 ≥ 这个值时才推荐 */
  minSourceHeight: number
  builtin: boolean
}

/** 粗剪（视频/音频通用）；时间单位是秒，end<=start 表示到结尾。 */

export interface TrimSpec {
  input: string
  output: string
  start: number
  end: number
  /** 只留音频 */
  audioOnly: boolean
  /** true = 重编码（切点精确），false = 流复制（秒切无损） */
  reencode: boolean
  params: string
  audioBitrate: number
}

/** 系统性能快照（后端 sysmon 每 2 秒刷新）。 */
export interface SysStats {
  cpu: number
  memUsed: number
  memTotal: number
  gpu: number
  gpuName: string
  gpuMemUsed: number
  gpuMemTotal: number
  /** 本工具拉起的编码类进程数 */
  procs: number
}

/** 一个可下载的工具包（后端清单里的一项）。 */
export interface ToolPackage {
  id: string
  name: string
  desc: string
  required: boolean
  approxMb: number
  installed: boolean
  missing: string[]
  dest: string
  /** false 表示官网没有稳定直链（如 NeroAAC），只能用离线包导入 */
  downloadable: boolean
}

export interface ToolProgress {
  pkg: string
  /** fetch | download | extract | done | error | start | export */
  phase: string
  message: string
  got: number
  total: number
  percent: number
  speedKbps: number
}
