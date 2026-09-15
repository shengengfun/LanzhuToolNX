import { invoke } from '@tauri-apps/api/core'
import { listen, type UnlistenFn } from '@tauri-apps/api/event'
import type {
  AppSettings,
  AudioSpec,
  AvsSpec,
  BatchMuxSpec,
  ExtractSpec,
  MediaInfo,
  Meme,
  MuxSpec,
  SysStats,
  ToolPackage,
  ToolProgress,
  TrimSpec,
  VideoSpec,
} from './types'

/* ------------------------------------------------------------------ *
 * 环境守卫
 *
 * 在纯浏览器里跑（`npm run dev` 直接开浏览器预览 UI）时没有 Tauri 运行时，
 * 直接 invoke 会抛错并把整个界面炸成白屏。这里统一降级成返回兜底值，
 * 于是所有页面都能在浏览器里预览、调样式，不必每次都启 Rust 后端。
 * ------------------------------------------------------------------ */

const IN_TAURI =
  typeof window !== 'undefined' && Object.prototype.hasOwnProperty.call(window, '__TAURI_INTERNALS__')

export const isTauri = () => IN_TAURI

async function call<T>(cmd: string, args: Record<string, unknown>, fallback: T): Promise<T> {
  if (!IN_TAURI) {
    console.info('[preview] 跳过后端调用:', cmd)
    return fallback
  }
  return invoke<T>(cmd, args)
}

async function on<T>(event: string, cb: (payload: T) => void): Promise<UnlistenFn> {
  if (!IN_TAURI) return () => void 0
  return listen<T>(event, (e) => cb(e.payload))
}

/* ------------------------------------------------------------------ *
 * 命令拼接：全部在后端完成，模板逐字来自原版 MainForm.cs。
 * 前端只负责把界面状态交过去，保证「预览到的命令」就是「将要执行的命令」。
 * ------------------------------------------------------------------ */

export const planVideo = (s: VideoSpec, audio: AudioSpec) =>
  call<string[]>('plan_video', { spec: s, audio }, [])

export const planAudio = (s: AudioSpec) => call<string[]>('plan_audio', { spec: s }, [])

export const planMux = (s: MuxSpec) => call<string[]>('plan_mux', { spec: s }, [])

/** 批量封装 / 容器转换。 */
export const planBatchMux = (s: BatchMuxSpec) => call<string[]>('plan_batch_mux', { spec: s }, [])

/** 彩蛋：随机烂梗。网络不通时会自动回落内置文案。 */
export const randomMeme = (url = '') =>
  call<Meme>('random_meme', { url }, {
    text: '（预览模式）别问，问就是重编码。',
    source: '',
    fallback: true,
  })

export const planExtract = (s: ExtractSpec) => call<string[]>('plan_extract', { spec: s }, [])

export const planAvs = (s: AvsSpec, audio: AudioSpec) =>
  call<string[]>('plan_avs', { spec: s, audio }, [])

/** 批量：每个文件一条独立流水线，后端串成一个大脚本。 */
export const planBatch = (
  inputs: string[],
  spec: VideoSpec,
  audio: AudioSpec,
  outputDir: string,
  embedSubtitle: boolean,
) => call<string[]>('plan_batch', { inputs, spec, audio, outputDir, embedSubtitle }, [])

/* ------------------------------------------------------------------ *
 * 粗剪 / 波形 / 本地素材
 * ------------------------------------------------------------------ */

/** 粗剪命令（视频与音频共用）。 */
export const planTrim = (spec: TrimSpec) => call<string[]>('plan_trim', { spec }, [])

/** 生成波形图，返回可直接进 <img src> 的 data URI。 */
export const makeWaveform = (path: string, width = 1400, height = 160, color = '2F9E79') =>
  call<string>('make_waveform', { path, width, height, color }, '')

/** 找同名字幕（.ass/.srt/.ssa/.sub）；找不到返回 null。 */
export const detectSubtitle = (input: string, lang = 'none') =>
  call<string | null>('detect_subtitle', { input, lang }, null)

/** 本地绝对路径 -> WebView 可加载的 URL（走自绘 lzmedia 协议，支持 Range）。 */
export const mediaUrl = (path: string) =>
  call<string>('media_url', { path }, isTauri() ? '' : path)

/* ------------------------------------------------------------------ *
 * 系统监控 / 托盘 / 关机
 * ------------------------------------------------------------------ */

export const systemStats = () =>
  call<SysStats>('system_stats', {}, {
    cpu: -1,
    memUsed: 0,
    memTotal: 0,
    gpu: -1,
    gpuName: '',
    gpuMemUsed: 0,
    gpuMemTotal: 0,
    procs: 0,
  })

export const hideToTray = () => call<void>('hide_to_tray', {}, undefined)

export const showWindow = () => call<void>('show_window', {}, undefined)

/** 安排定时关机（秒）。 */
export const systemShutdown = (seconds = 60) =>
  call<void>('system_shutdown', { seconds }, undefined)

/** 取捎关机计划。 */
export const abortShutdown = () => call<void>('abort_shutdown', {}, undefined)

/* ------------------------------------------------------------------ *
 * 执行与探测
 * ------------------------------------------------------------------ */

/** 执行一批命令。`commands` 必须是**数组**（后端是 `Vec<String>`），
 *  传拼好的整串会被 Tauri 判成 `invalid args ... expected a sequence`。 */
export const runCommands = (commands: string[], workCount = 1) =>
  call<number>('run_commands', { commands, workCount }, -1)

export const cancelRun = (id: number) => call<void>('cancel_run', { id }, undefined)

/** 暂停 / 继续。后端会挂起整棵进程树，ffmpeg 子进程一起停。 */
export const pauseRun = (id: number, paused: boolean) =>
  call<void>('pause_run', { id, paused }, undefined)

export const probeMedia = (path: string) =>
  call<MediaInfo>('probe_media', { path }, {
    path,
    exists: false,
    container: '',
    durationSec: 0,
    sizeBytes: 0,
    bitrate: 0,
    video: null,
    audio: null,
    raw: '（预览模式，未接后端）',
  })

export const loadSettings = () =>
  call<AppSettings>('load_settings', {}, {
    toolsDir: '',
    outputDir: '',
    language: 'zh-CN',
    mirrors: [],
    threadsDefault: 'auto',
    defaultFormat: 'H.264 8bit',
    autoScrollLog: true,
    maxLogLines: 4000,
    logLevel: 'all',
    theme: 'system',
    accent: 'lanzhu',
    accentCustom: '',
    background: '',
    uiScale: 1,
    closeToTray: false,
    minimizeToTray: false,
    notifyOnFinish: true,
    showMonitor: true,
    recentFiles: [],
    showSplash: true,
    memeUrl: '',
    presets: [],
  })

export const saveSettings = (s: AppSettings) =>
  call<void>('save_settings', { settings: s }, undefined)

export const detectGpus = () =>
  call<{ index: number; label: string; kind: 'nvenc' | 'qsv' | 'amf' }[]>('detect_gpus', {}, [
    { index: 0, label: '默认GPU（预览模式）', kind: 'nvenc' },
  ])

export const readTextFile = (path: string) => call<string>('read_text_file', { path }, '')

export const writeTextFile = (path: string, content: string) =>
  call<void>('write_text_file', { path, content }, undefined)

export const listBundledTools = () => call<string[]>('list_bundled_tools', {}, [])

/** 默认工具目录：包内 tools/ 或用户配置的目录。 */
export const resolveToolsDir = () => call<string>('resolve_tools_dir', {}, '（预览模式）')

/** 把工具名（如 ffmpeg.exe）解析成绝对路径。 */
export const resolveTool = (name: string) => call<string>('resolve_tool', { name }, name)

/* ------------------------------------------------------------------ *
 * 运行期事件
 * ------------------------------------------------------------------ */

export interface OutputEvent {
  id: number
  line: string
  stream: 'stdout' | 'stderr'
}

export interface DoneEvent {
  id: number
  code: number | null
  elapsedMs: number
}

export interface ProgressEvent {
  id: number
  done: number
  total: number
}

export const onOutput = (cb: (e: OutputEvent) => void) => on<OutputEvent>('run://output', cb)

export const onDone = (cb: (e: DoneEvent) => void) => on<DoneEvent>('run://done', cb)

export const onProgress = (cb: (e: ProgressEvent) => void) => on<ProgressEvent>('run://progress', cb)

export const onTaskStart = (
  cb: (e: { id: number; index: number; total: number; command: string }) => void,
) => on<{ id: number; index: number; total: number; command: string }>('run://task', cb)

/* ------------------------------------------------------------------ *
 * 工具在线下载 / 离线包
 * ------------------------------------------------------------------ */

export const toolPackages = () => call<ToolPackage[]>('tool_packages', {}, [])

export const defaultMirrors = () => call<string[]>('default_mirrors', {}, [])

/** 真正可写的工具目录（装到 Program Files 时会自动退到 %APPDATA%）。 */
export const downloadTarget = () => call<string>('download_target', {}, '')

export const downloadTools = (ids: string[], toolsDir: string, mirrors: string[]) =>
  call<void>('download_tools', { ids, toolsDir, mirrors }, undefined)

export const cancelToolsDownload = () => call<void>('cancel_tools_download', {}, undefined)

export const importOfflineTools = (path: string, toolsDir: string) =>
  call<string>('import_offline_tools', { path, toolsDir }, '')

export const exportOfflineTools = (options: {
  output: string
  toolsDir: string
  excludeFfplay: boolean
}) => call<void>('export_offline_tools', { options }, undefined)

export const onToolProgress = (cb: (e: ToolProgress) => void) =>
  on<ToolProgress>('tools://progress', cb)

export const onToolsFinished = (cb: (e: { failed: string[] }) => void) =>
  on<{ failed: string[] }>('tools://finished', cb)

export const onToolsExported = (cb: (e: { ok: boolean; files?: number; error?: string }) => void) =>
  on<{ ok: boolean; files?: number; error?: string }>('tools://exported', cb)

/* ------------------------------------------------------------------ *
 * 完成提醒
 * ------------------------------------------------------------------ */

/**
 * 系统通知（任务跑完 / 失败）。
 *
 * 走 Tauri 的 notification 插件而不是 Web 的 `Notification`：
 * WebView2 里后者经常被静默拒掉，长任务结束却什么提示都没有是最糟的体验。
 */
export async function notifyUser(title: string, body: string) {
  if (!IN_TAURI) return
  try {
    const m = await import('@tauri-apps/plugin-notification')
    let granted = await m.isPermissionGranted()
    if (!granted) granted = (await m.requestPermission()) === 'granted'
    if (granted) m.sendNotification({ title, body })
  } catch {
    /* 通知不是关键路径，失败就静默 */
  }
}

/** 让任务栏图标闪一下（窗口不在前台时用）。2 = Critical，会一直闪到用户点进来。 */
export async function flashWindow() {
  if (!IN_TAURI) return
  try {
    const { getCurrentWindow } = await import('@tauri-apps/api/window')
    await getCurrentWindow().requestUserAttention(2)
  } catch {
    /* 同上 */
  }
}

/** 窗口现在是不是在前台（用来决定要不要提醒）。 */
export async function isWindowFocused() {
  if (!IN_TAURI) return true
  try {
    const { getCurrentWindow } = await import('@tauri-apps/api/window')
    return await getCurrentWindow().isFocused()
  } catch {
    return true
  }
}
