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
