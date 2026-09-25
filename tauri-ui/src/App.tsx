import * as React from 'react'
import { AlertCircle, CheckCircle2, FolderSearch } from 'lucide-react'
import { TitleBar } from './components/TitleBar'
import { Sidebar, type PageId } from './components/Sidebar'
import { OutputPanel } from './components/OutputPanel'
import { StatusBar } from './components/StatusBar'
import { Button } from './components/ui'
import { AppProvider, useApp, useWorkspaceValue } from './state'
import * as api from './lib/api'
import logo from './assets/logo.png'
import { VideoPage } from './pages/VideoPage'
import { AudioPage } from './pages/AudioPage'
import { MuxExtractPage } from './pages/MuxPage'
import { AvsPage } from './pages/AvsPage'
import { EnhancePage } from './pages/EnhancePage'
import { MiscPage } from './pages/MiscPage'
import { MediaInfoPage } from './pages/MediaInfoPage'
import { SettingsPage } from './pages/SettingsPage'
import { HelpPage } from './pages/HelpPage'

export default function App() {
  return (
    <AppProvider>
      <Shell />
    </AppProvider>
  )
}

function Shell() {
  // 上次停留在哪一页也记下来（原版重启后回到同一个页签）
  const [page, setPage] = useWorkspaceValue<PageId>('page', 'video')
  const [booted, setBooted] = React.useState(false)
  const [splashArmed, setSplashArmed] = React.useState(false)
  const { running, ready, settings } = useApp()

  // 启动画面：**立刻**盖上，等设置读完就撤。
  //
  // 以前是反过来的 —— 先白屏等设置，读完再故意停 1.4 秒，于是用户看到的
  // 就是"一个写着『正在准备工具链』的界面卡在那儿"，既不准也白等。
  // 现在它只在真正需要遮的这段时间（WebView 起来 + 设置读出来）存在，
  // 并且最少显示 550ms，免得一闪而过更显廉价。
  //
  // 延迟 120ms 才"上膛"是为了尊重关掉启动画面的用户：设置读得快时它根本不会出现。
  React.useEffect(() => {
    const t = window.setTimeout(() => setSplashArmed(true), 120)
    return () => window.clearTimeout(t)
  }, [])

  const showSplash = splashArmed && settings.showSplash && !booted
  const showToolsBanner = ready && !showSplash

  return (
    <div className="app-backdrop flex h-full flex-col">
      <TitleBar title="岚珠工具箱" version={__APP_VERSION__} />

      {showToolsBanner && <ToolsBanner onGoSettings={() => setPage('settings')} />}

      <div className="flex min-h-0 flex-1">
        <Sidebar page={page} onSelect={setPage} running={running} />

        {/**
          * 这里以前挂着一个「页标题」栏（“视频压制”四个字 + 下划线）。
          * 左栏的导航已经写清楚了当前在哪，那就没必要再占一行 —— 去掉后
          * 每个页面白赚 ~26px 高度，窗口也能跟着缩一截。
          */}
        <main className="flex min-h-0 min-w-0 flex-1 gap-3 p-3">
          <section className="flex min-h-0 min-w-0 flex-1 flex-col">
            <div className="min-h-0 flex-1">
              {page === 'video' && <VideoPage />}
              {page === 'audio' && <AudioPage />}
              {page === 'mux' && <MuxExtractPage />}
              {page === 'avs' && <AvsPage />}
              {page === 'enhance' && <EnhancePage />}
              {page === 'misc' && <MiscPage />}
              {page === 'mediainfo' && <MediaInfoPage />}
              {page === 'settings' && <SettingsPage />}
              {page === 'help' && <HelpPage />}
            </div>
          </section>

          <OutputPanel />
        </main>
      </div>

      <StatusBar />
      <Toast />
      {showSplash && <Splash ready={ready} onDone={() => setBooted(true)} />}
    </div>
  )
}

/**
 * 启动画面。
 *
 * 不做成独立小窗口，是因为真正耗时的只有"WebView 起来 + 设置读出来"，
 * 这段时间主窗口本来就是白的；在上面盖一层反而是最干净的做法
 * （也不会多出一个需要管生命周期的窗口）。
 *
 * 原版 SplashForm 就是一张 logo 图淡入，没有进度也没有文案 ——
 * 这里保持同样的克制，只在底下留一条呼吸的进度线。
 */
function Splash({ ready, onDone }: { ready: boolean; onDone: () => void }) {
  const [fading, setFading] = React.useState(false)
  const shownAt = React.useRef(Date.now())

  React.useEffect(() => {
    if (!ready) return
    // 最少显示 550ms：一闪而过比多停一会儿更难受
    const wait = Math.max(0, 550 - (Date.now() - shownAt.current))
    const t1 = window.setTimeout(() => setFading(true), wait)
    const t2 = window.setTimeout(onDone, wait + 320)
    return () => {
      window.clearTimeout(t1)
      window.clearTimeout(t2)
    }
  }, [ready, onDone])

  return (
    <div
      className={`app-backdrop fixed inset-0 z-[200] flex flex-col items-center justify-center gap-4 transition-opacity duration-300 ${
        fading ? 'opacity-0' : 'opacity-100'
      }`}
    >
      <img
        src={logo}
        alt="岚珠工具箱"
        draggable={false}
        className="size-20 rounded-3xl object-cover shadow-lg ring-1 ring-border/60"
      />
      <div className="text-center">
        <div className="text-[16px] tracking-wide">
          <span className="font-bold text-primary">岚珠</span>
          <span className="font-semibold text-foreground">工具箱</span>
        </div>
        <div className="mt-1 text-[12px] text-muted-foreground">v{__APP_VERSION__}</div>
      </div>
      <div className="h-1 w-44 overflow-hidden rounded-full bg-muted">
        <div
          className={`h-full rounded-full bg-primary transition-all duration-500 ${
            ready ? 'w-full' : 'w-1/3'
          } ${ready ? '' : 'animate-pulse'}`}
        />
      </div>
    </div>
  )
}

/**
 * 找不到 ffmpeg/ffprobe 时在顶部挂一条警告。
 *
 * 安装版最容易踩这个坑：装到 Program Files 之后，800MB 的 tools/ 并不在身边，
 * 此时所有压制都会失败。与其让用户对着报错猜，不如直接把入口摆出来。
 */
function ToolsBanner({ onGoSettings }: { onGoSettings: () => void }) {
  const { settings } = useApp()
  const [missing, setMissing] = React.useState(false)
  const [dir, setDir] = React.useState('')

  React.useEffect(() => {
    let alive = true
    Promise.all([api.resolveToolsDir(), api.listBundledTools()])
      .then(([d, tools]) => {
        if (!alive) return
        const lower = tools.map((t) => t.toLowerCase())
        setDir(d)
        setMissing(!lower.includes('ffmpeg.exe') || !lower.includes('ffprobe.exe'))
      })
      .catch(() => void 0)
    return () => {
      alive = false
    }
    // toolsDir 变了要重新判断（用户在设置页改完就立刻生效）
  }, [settings.toolsDir])

  if (!missing) return null

  return (
    <div className="flex shrink-0 items-center gap-2 border-b border-amber-300/60 bg-amber-50 px-3 py-1.5 text-[12px] text-amber-900">
      <FolderSearch className="size-3.5 shrink-0" />
      <span className="min-w-0 truncate">
        未找到 <span className="font-mono">ffmpeg.exe</span> /{' '}
        <span className="font-mono">ffprobe.exe</span>（{dir}）
      </span>
      <span className="flex-1" />
      <Button size="sm" variant="outline" onClick={onGoSettings}>
        指定工具目录
      </Button>
    </div>
  )
}

function Toast() {
  const { toast } = useApp()
  if (!toast) return null
  const err = toast.kind === 'error'
  return (
    <div className="pointer-events-none fixed bottom-12 left-1/2 z-[100] -translate-x-1/2">
      <div
        className={`pointer-events-auto flex items-center gap-2 rounded-xl border px-3.5 py-2 text-[12.5px] shadow-lg backdrop-blur ${
          err
            ? 'border-destructive/40 bg-destructive/12 text-destructive'
            : 'border-border/70 bg-card/95 text-foreground'
        }`}
      >
        {err ? <AlertCircle className="size-4" /> : <CheckCircle2 className="size-4 text-primary" />}
        <span className="max-w-[520px]">{toast.text}</span>
        {toast.action && (
          <Button
            size="sm"
            variant="outline"
            className="ml-1 h-6 px-2 text-[12px]"
            onClick={() => toast.action?.run()}
          >
            {toast.action.label}
          </Button>
        )}
      </div>
    </div>
  )
}
