import * as React from 'react'
import { open as openDialog, save as saveDialog } from '@tauri-apps/plugin-dialog'
import * as api from './lib/api'
import { applyAccent, applyBackground, applyTheme, applyUiScale } from './lib/appearance'
import type { AppSettings, AudioSpec, VideoSpec } from './lib/types'

/* ================================================================== *
 * 文件对话框封装 —— 统一"过滤器"写法，页面里不用重复样板
 * ================================================================== */

export const VIDEO_FILTERS = [
  {
    name: '视频',
    extensions: ['mp4', 'mkv', 'mov', 'flv', 'avi', 'ts', 'm2ts', 'wmv', '264', 'h264', 'hevc', 'avs'],
  },
  { name: '所有文件', extensions: ['*'] },
]

export const AUDIO_FILTERS = [
  { name: '音频', extensions: ['mp4', 'aac', 'mp2', 'mp3', 'm4a', 'ac3', 'flac', 'wav', 'mka'] },
  { name: '所有文件', extensions: ['*'] },
]

export const SUB_FILTERS = [
  { name: '字幕', extensions: ['ass', 'srt', 'ssa', 'sub', 'idx'] },
  { name: '所有文件', extensions: ['*'] },
]

export async function pickFile(title: string, filters = VIDEO_FILTERS) {
  const r = await openDialog({ title, multiple: false, directory: false, filters })
  return typeof r === 'string' ? r : null
}

export async function pickFiles(title: string, filters = VIDEO_FILTERS) {
  const r = await openDialog({ title, multiple: true, directory: false, filters })
  if (!r) return []
  return Array.isArray(r) ? r : [r]
}

export async function pickFolder(title: string) {
  const r = await openDialog({ title, directory: true, multiple: false })
  return typeof r === 'string' ? r : null
}

export async function pickSave(
  title: string,
  defaultPath: string,
  filters: { name: string; extensions: string[] }[],
) {
  const r = await saveDialog({ title, defaultPath, filters })
  return typeof r === 'string' ? r : null
}

/* ================================================================== *
 * 拖放导入
 *
 * 坑：Tauri 会把 WebView 的原生拖放拦掉，所以 HTML5 的 onDrop **根本不会触发** ——
 * 这就是之前"拖动导入完全不能用"的原因。必须改用 Tauri 自己的拖放事件。
 *
 * 做法：每个拖放区登记一个 id，全局只挂一只 Tauri 监听，
 * 拿到坐标后用 elementFromPoint 命中 [data-drop-id] 再派发。
 * 浏览器预览（没有 Tauri 运行时）时照旧走 HTML5 事件。
 * ================================================================== */

type DropHandler = (paths: string[]) => void

const dropZones = new Map<string, { el: HTMLElement | null; onDrop: DropHandler }>()
let dropSeq = 0

export function registerDropZone(onDrop: DropHandler) {
  const id = `dz${++dropSeq}`
  dropZones.set(id, { el: null, onDrop })
  return id
}

function zoneAt(x: number, y: number): string | null {
  const el = document.elementFromPoint(x, y) as HTMLElement | null
  const z = el?.closest('[data-drop-id]') as HTMLElement | null
  return z?.getAttribute('data-drop-id') ?? null
}

/**
 * 把一块区域变成拖放目标。
 * 返回要展开到元素上的属性，以及当前是否被拖到（用于高亮）。
 */
export function useDropZone(onDrop: DropHandler | undefined) {
  const [over, setOver] = React.useState(false)
  // id 必须在**渲染期**就确定，否则首帧的 data-drop-id 是空的，命中检测会失败
  const idRef = React.useRef<string>('')
  if (!idRef.current) idRef.current = `dz${++dropSeq}`

  const cbRef = React.useRef(onDrop)
  cbRef.current = onDrop
  const enabled = !!onDrop

  React.useEffect(() => {
    if (!enabled) return
    const id = idRef.current
    dropZones.set(id, { el: null, onDrop: (paths) => cbRef.current?.(paths) })
    const off = onDragState((active, hoverId) => setOver(active && hoverId === id))
    return () => {
      dropZones.delete(id)
      off()
    }
  }, [enabled])

  if (!enabled) return { over: false, props: {} as Record<string, unknown> }

  return {
    over,
    props: {
      'data-drop-id': idRef.current,
      onDragOver: (e: React.DragEvent) => {
        e.preventDefault()
        setOver(true)
      },
      onDragLeave: () => setOver(false),
      onDrop: (e: React.DragEvent) => {
        // 浏览器预览时 Tauri 不在，靠 HTML5 事件兼顾
        e.preventDefault()
        setOver(false)
        const files = Array.from(e.dataTransfer?.files ?? []) as (File & { path?: string })[]
        const paths = files.map((f) => f.path).filter((p): p is string => !!p)
        if (paths.length) cbRef.current?.(paths)
      },
    } as Record<string, unknown>,
  }
}

/* 拖放状态广播：全局监听只挂一只，多个拖放区靠订阅更新高亮 */
type DragListener = (active: boolean, hoverId: string | null) => void
const dragListeners = new Set<DragListener>()

function onDragState(fn: DragListener) {
  dragListeners.add(fn)
  return () => dragListeners.delete(fn)
}

function broadcast(active: boolean, hoverId: string | null) {
  dragListeners.forEach((f) => f(active, hoverId))
}

/* ================================================================== *
 * 日志与运行状态
 * ================================================================== */

export interface LogLine {
  id: number
  text: string
  stream: 'stdout' | 'stderr' | 'app'
}

/** 提示条；`action` 用于「完成后关机」这类需要反悔机会的操作。 */
export interface ToastState {
  text: string
  kind: 'info' | 'error'
  action?: { label: string; run: () => void }
}

/** 设置的默认值。`api.loadSettings()` 拿不到后端时也用这一份。 */
export const DEFAULT_SETTINGS: AppSettings = {
  toolsDir: '',
  outputDir: '',
  language: 'zh-CN',
  mirrors: [],
  threadsDefault: 'auto',
  defaultFormat: 'H.264 8bit',
  autoScrollLog: true,
  maxLogLines: 4000,
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
}

/** 最近文件列表上限。 */
export const MAX_RECENT = 10

let logSeq = 0

interface AppCtx {
  settings: AppSettings
  patchSettings: (p: Partial<AppSettings>) => void
  /** 记一笔"最近打开"（去重 + 置顶 + 封顶 10 条） */
  pushRecent: (path: string) => void

  video: VideoSpec
  patchVideo: (p: Partial<VideoSpec>) => void

  audio: AudioSpec
  patchAudio: (p: Partial<AudioSpec>) => void

  log: LogLine[]
  appendLog: (text: string, stream?: LogLine['stream']) => void
  clearLog: () => void

  running: boolean
  paused: boolean
  progress: { done: number; total: number } | null
  runningCmd: string

  run: (commands: string[], label?: string) => Promise<void>
  cancel: () => void
  togglePause: () => void
  toast: ToastState | null
  notify: (text: string, kind?: 'info' | 'error', action?: ToastState['action']) => void
}

const Ctx = React.createContext<AppCtx | null>(null)

export function useApp() {
  const c = React.useContext(Ctx)
  if (!c) throw new Error('useApp 必须在 AppProvider 内使用')
  return c
}

export function AppProvider({ children }: { children: React.ReactNode }) {
  const [settings, setSettings] = React.useState<AppSettings>(DEFAULT_SETTINGS)
  const [video, setVideo] = React.useState<VideoSpec>(defaultVideo())
  const [audio, setAudio] = React.useState<AudioSpec>(defaultAudio())
  const [log, setLog] = React.useState<LogLine[]>([])
  const [runId, setRunId] = React.useState<number | null>(null)
  const [paused, setPaused] = React.useState(false)
  const [progress, setProgress] = React.useState<{ done: number; total: number } | null>(null)
  const [runningCmd, setRunningCmd] = React.useState('')
  const [toast, setToast] = React.useState<ToastState | null>(null)
  const [scheme, setScheme] = React.useState<'light' | 'dark'>('light')

  // 事件回调里想拿到"最新"的设置/视频，但又不想因为依赖它们而重挂监听
  const settingsRef = React.useRef(settings)
  settingsRef.current = settings
  const videoRef = React.useRef(video)
  videoRef.current = video

  const appendLog = React.useCallback(
    (text: string, stream: LogLine['stream'] = 'app') => {
      setLog((prev) => {
        const next = [...prev, { id: ++logSeq, text, stream }]
        // 上限可配，避免长时间压制把内存撑爆
        const cap = settings.maxLogLines > 0 ? settings.maxLogLines : 4000
        return next.length > cap ? next.slice(next.length - cap) : next
      })
    },
    [settings.maxLogLines],
  )

  const clearLog = React.useCallback(() => setLog([]), [])

  const notify = React.useCallback(
    (text: string, kind: 'info' | 'error' = 'info', action?: ToastState['action']) => {
      setToast({ text, kind, action })
      // 带操作的提示要多留一会儿，否则用户根本来不及点
      window.setTimeout(() => setToast(null), action ? 15000 : 4200)
    },
    [],
  )

  /* ---------------- 外观：主题 / 强调色 / 背景 / 缩放 ---------------- */

  React.useEffect(() => {
    const mq = window.matchMedia('(prefers-color-scheme: dark)')
    const sync = () => setScheme(applyTheme(settings.theme))
    sync()
    mq.addEventListener('change', sync)
    return () => mq.removeEventListener('change', sync)
  }, [settings.theme])

  React.useEffect(() => {
    applyAccent(settings.accent, settings.accentCustom, scheme === 'dark')
  }, [settings.accent, settings.accentCustom, scheme])

  React.useEffect(() => {
    applyUiScale(settings.uiScale)
  }, [settings.uiScale])

  React.useEffect(() => {
    if (!settings.background) {
      applyBackground('')
      return
    }
    let alive = true
    api
      .mediaUrl(settings.background)
      .then((u) => alive && applyBackground(u))
      .catch(() => void 0)
    return () => {
      alive = false
    }
  }, [settings.background])

  // 载入设置
  React.useEffect(() => {
    api
      .loadSettings()
      .then(setSettings)
      .catch(() => void 0)
  }, [])

  // 订阅运行事件
  React.useEffect(() => {
    const un: (() => void)[] = []
    let alive = true

    api
      .onOutput((e) => appendLog(e.line, e.stream))
      .then((f) => (alive ? un.push(f) : f()))
    api
      .onDone((e) => {
        if (!alive) return
        setRunId(null)
        setPaused(false)
        setRunningCmd('')
        const ok = e.code === 0
        appendLog(
          ok
            ? `===== 完成，耗时 ${(e.elapsedMs / 1000).toFixed(1)}s =====`
            : `===== 结束，退出码 ${e.code ?? '未知'} =====`,
          'app',
        )

        if (ok) {
          // 窗口不在前台时提醒一下：几十分钟的任务，没人愿意守着屏幕
          void (async () => {
            const focused = await api.isWindowFocused()
            if (focused) return
            const s = settingsRef.current
            if (s.notifyOnFinish) {
              const mins = Math.round(e.elapsedMs / 60000)
              const body =
                mins >= 1 ? `任务已完成，耗时约 ${mins} 分钟` : '任务已完成'
              await api.notifyUser('岚珠工具箱', body)
            }
            await api.flashWindow()
          })()

          // 「完成后关机」——原版就有，这里补上取消的机会
          if (videoRef.current.autoShutdown) {
            setVideo((v) => ({ ...v, autoShutdown: false }))
            api
              .systemShutdown(60)
              .then(() =>
                notify('压制完成，系统将在 60 秒后关机', 'info', {
                  label: '取消关机',
                  run: () => {
                    api.abortShutdown().catch(() => void 0)
                    notify('已取消关机')
                  },
                }),
              )
              .catch((err) => notify(String(err), 'error'))
          }
        }
      })
      .then((f) => (alive ? un.push(f) : f()))
    api
      .onProgress((e) => setProgress({ done: e.done, total: e.total }))
      .then((f) => (alive ? un.push(f) : f()))

    return () => {
      alive = false
      un.forEach((f) => f())
    }
  }, [appendLog, notify])

  const run = React.useCallback(
    async (commands: string[], label = '') => {
      const clean = commands.map((c) => c.trim()).filter(Boolean)
      if (clean.length === 0) {
        notify('没有可执行的命令', 'error')
        return
      }
      setRunningCmd(label || clean[0])
      setProgress({ done: 0, total: clean.length })
      setPaused(false)
      appendLog(`===== 开始执行 ${clean.length} 条命令 =====`, 'app')
      clean.forEach((c) => appendLog(`> ${c}`))
      try {
        const id = await api.runCommands(clean.join('\r\n'), '', clean.length)
        setRunId(id)
      } catch (e) {
        appendLog(`启动失败：${String(e)}`, 'stderr')
        notify(String(e), 'error')
        setRunId(null)
      }
    },
    [appendLog, notify],
  )

  // 全局拖放监听：Tauri 拦截了 WebView 的原生拖放，只能用它自己的事件。
  // 只挂一只，按坐标命中 [data-drop-id] 再派发。
  React.useEffect(() => {
    if (!api.isTauri()) return
    let alive = true
    let un: (() => void) | undefined

    import('@tauri-apps/api/webview')
      .then(({ getCurrentWebview }) =>
        getCurrentWebview().onDragDropEvent((ev) => {
          const p = ev.payload
          if (p.type === 'leave') {
            broadcast(false, null)
            return
          }
          // 事件里是物理像素，要折回 CSS 像素才能喂给 elementFromPoint
          const dpr = window.devicePixelRatio || 1
          const id = zoneAt(Math.round(p.position.x / dpr), Math.round(p.position.y / dpr))
          if (p.type === 'drop') {
            broadcast(false, null)
            if (id) dropZones.get(id)?.onDrop(p.paths as string[])
            else if (p.paths?.length) {
              // 命中失败通常是缩放比/坐标算法的问题。留一行日志，别让用户对着"没反应"干猜。
              appendLog(`拖放的落点没有对应的拖放区（${p.paths.length} 个文件）`, 'stderr')
            }
          } else {
            broadcast(true, id)
          }
        }),
      )
      .then((f) => {
        if (alive) un = f
        else f()
      })
      .catch(() => void 0)

    return () => {
      alive = false
      un?.()
    }
  }, [appendLog])

  const cancel = React.useCallback(() => {
    if (runId == null) return
    api.cancelRun(runId).catch(() => void 0)
    appendLog('===== 已请求中止 =====', 'app')
    setPaused(false)
  }, [runId, appendLog])

  const togglePause = React.useCallback(() => {
    if (runId == null) return
    const next = !paused
    api
      .pauseRun(runId, next)
      .then(() => {
        setPaused(next)
        appendLog(next ? '===== 已暂停 =====' : '===== 已继续 =====', 'app')
      })
      .catch((e) => notify(String(e), 'error'))
  }, [runId, paused, appendLog, notify])

  const patchSettings = React.useCallback((p: Partial<AppSettings>) => {
    setSettings((s) => {
      const next = { ...s, ...p }
      api.saveSettings(next).catch(() => void 0)
      return next
    })
  }, [])

  const pushRecent = React.useCallback((path: string) => {
    if (!path) return
    setSettings((s) => {
      const list = [path, ...s.recentFiles.filter((p) => p !== path)].slice(0, MAX_RECENT)
      const next = { ...s, recentFiles: list }
      api.saveSettings(next).catch(() => void 0)
      return next
    })
  }, [])

  const value: AppCtx = {
    settings,
    patchSettings,
    pushRecent,
    video,
    patchVideo: (p) => setVideo((s) => ({ ...s, ...p })),
    audio,
    patchAudio: (p) => setAudio((s) => ({ ...s, ...p })),
    log,
    appendLog,
    clearLog,
    running: runId != null,
    paused,
    progress,
    runningCmd,
    run,
    cancel,
    togglePause,
    toast,
    notify,
  }

  return <Ctx.Provider value={value}>{children}</Ctx.Provider>
}

/**
 * 把本地路径变成 WebView 能加载的 URL（走自绘的 lzmedia 协议，支持 Range）。
 *
 * 预览窗 / 波形 / 背景图都靠它 —— 在浏览器预览模式下后端不在，
 * 这时返回空串，调用方走各自的降级分支。
 */
export function useMediaUrl(path: string) {
  const [url, setUrl] = React.useState('')
  React.useEffect(() => {
    if (!path) {
      setUrl('')
      return
    }
    let alive = true
    api
      .mediaUrl(path)
      .then((u) => alive && setUrl(u))
      .catch(() => alive && setUrl(''))
    return () => {
      alive = false
    }
  }, [path])
  return url
}

/* 默认值与原版 InitParameter() 保持一致 */
export function defaultVideo(): VideoSpec {
  return {
    input: '',
    output: '',
    subtitle: '',
    format: 'H.264 8bit',
    mode: 1,
    crf: 23.5,
    bitrate: 800,
    customParams: '',
    extraParams: '',
    width: 0,
    height: 0,
    maintainResolution: false,
    seek: 0,
    frames: 0,
    threads: 'auto',
    priority: 2,
    useGpu: false,
    hybrid: false,
    gpuKind: 'nvenc',
    gpuIndex: 0,
    audioMode: 0,
    audioParams: '--abitrate 128',
    container: 'mp4',
    autoShutdown: false,
  }
}

function defaultAudio(): AudioSpec {
  return {
    input: '',
    output: '',
    encoder: 0,
    useBitrate: true,
    bitrate: '128',
    customParams: '',
  }
}
