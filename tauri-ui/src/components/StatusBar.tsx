import * as React from 'react'
import { Activity, Cpu, GaugeCircle, HardDrive, MemoryStick } from 'lucide-react'
import { useApp } from '~/state'
import * as api from '~/lib/api'
import type { SysStats } from '~/lib/types'

/**
 * 底栏：就绪指示灯 + 当前任务 + 实时性能监控。
 *
 * 进度**不画在这里**：底栏那条横贯整屏的进度条又长又抢眼，
 * 真正该看进度的地方是任务栏图标（后端 `taskbar.rs` 用 ITaskbarList3 画的绿色进度）。
 * 这里只留一句"压到哪个文件了、多少 %"，翻页时一眼能看到，也不占地方。
 *
 * 性能数据（CPU/GPU/内存）由后端 `sysmon` 后台线程采样，前端 2 秒拉一次现成值，
 * 所以这里不会给界面带来任何额外卡顿。
 */
export function StatusBar() {
  const { running, progress, runningCmd, settings } = useApp()
  const [time, setTime] = React.useState(() => new Date())
  const [stats, setStats] = React.useState<SysStats | null>(null)

  React.useEffect(() => {
    const t = window.setInterval(() => setTime(new Date()), 1000)
    return () => window.clearInterval(t)
  }, [])

  // 性能采样：运行中更密集（2s），空闲时 3s 就够
  React.useEffect(() => {
    if (!settings.showMonitor) {
      setStats(null)
      return
    }
    let alive = true
    const tick = () =>
      api
        .systemStats()
        .then((s) => alive && setStats(s))
        .catch(() => void 0)
    tick()
    const t = window.setInterval(tick, running ? 2000 : 3000)
    return () => {
      alive = false
      window.clearInterval(t)
    }
  }, [settings.showMonitor, running])

  const hasProgress = !!progress && progress.total > 0
  const pct = hasProgress ? Math.max(0, Math.min(100, progress!.percent * 100)) : 0
  const file = progress?.file ? baseName(progress.file) : ''

  return (
    <footer className="relative flex h-8 shrink-0 items-center gap-3 border-t border-border/60 bg-card/70 px-3 text-[11.5px] text-muted-foreground backdrop-blur">
      <span className="flex items-center gap-1.5">
        <span
          className={`size-1.5 rounded-full ${running ? 'animate-pulse bg-primary' : 'bg-primary/70'}`}
        />
        {running ? '正在压制' : '就绪'}
      </span>

      {running && (
        <>
          <span className="shrink-0 font-semibold tabular-nums text-foreground">
            {hasProgress ? `${pct.toFixed(1)}%` : '…'}
          </span>
          {hasProgress && progress!.total > 1 && (
            <span className="shrink-0 tabular-nums">
              {progress!.done + 1 > progress!.total ? progress!.total : progress!.done + 1}/
              {progress!.total}
            </span>
          )}
          {/* 当前文件比命令串有用得多：长任务里用户最想知道"现在压到哪个了" */}
          <span className="max-w-[280px] truncate" title={progress?.file || runningCmd}>
            {file || runningCmd}
          </span>
          {!!progress?.speed && (
            <span className="shrink-0 tabular-nums opacity-80">{progress.speed.toFixed(2)}x</span>
          )}
        </>
      )}

      <span className="flex-1" />

      {stats && settings.showMonitor && <Monitor stats={stats} />}

      <span className="flex items-center gap-1.5">
        <Activity className="size-3" />
        {time.toLocaleTimeString('zh-CN', { hour12: false })}
      </span>
    </footer>
  )
}

/** 只取文件名 —— 底栏没有横着放整条路径的余地。 */
function baseName(p: string) {
  const i = Math.max(p.lastIndexOf('\\'), p.lastIndexOf('/'))
  return i >= 0 ? p.slice(i + 1) : p
}

/** 性能监控区：CPU / GPU / 内存（取不到的项显示 --，绝不显示假数字）。 */
function Monitor({ stats }: { stats: SysStats }) {
  const memPct = pctOf(
    stats.memTotal > 0 ? (stats.memUsed / stats.memTotal) * 100 : -1,
  )

  return (
    <div className="flex items-center gap-2.5">
      {stats.procs > 0 && (
        <span className="hidden items-center gap-1 rounded-md bg-primary/12 px-1.5 py-0.5 text-primary xl:flex">
          <GaugeCircle className="size-3" />
          {stats.procs} 进程
        </span>
      )}

      <Meter icon={<Cpu className="size-3" />} label="CPU" pct={pctOf(stats.cpu)} />
      <Meter
        icon={<HardDrive className="size-3" />}
        label={stats.gpuName ? shortGpu(stats.gpuName) : 'GPU'}
        pct={pctOf(stats.gpu)}
      />
      <Meter
        icon={<MemoryStick className="size-3" />}
        label="内存"
        pct={memPct}
        detail={stats.memTotal > 0 ? `${gb(stats.memUsed)}/${gb(stats.memTotal)}G` : undefined}
      />
    </div>
  )
}

function Meter({
  icon,
  label,
  pct,
  detail,
}: {
  icon: React.ReactNode
  label: string
  pct: number
  detail?: string
}) {
  const unknown = pct < 0
  return (
    <span className="flex items-center gap-1.5 tabular-nums" title={label}>
      {icon}
      <span className="max-w-[96px] truncate">{label}</span>
      <span className={unknown ? 'opacity-60' : 'font-semibold text-foreground'}>
        {unknown ? '--' : `${pct}%`}
      </span>
      {detail && <span className="opacity-70">{detail}</span>}
    </span>
  )
}

function gb(bytes: number) {
  return (bytes / 1024 / 1024 / 1024).toFixed(1)
}

/** 监视器上只看整数：`12.345678901234%` 这种精度没有任何意义，还把底栏撑爆。 */
const pctOf = (v: number) => (v >= 0 ? Math.round(v) : -1)

/** 显卡名太长（"NVIDIA GeForce RTX 4070 Ti SUPER"）会挤爆底栏，留前几个词就行。 */
function shortGpu(name: string) {
  const n = name.replace(/\((R|TM)\)/g, '').trim()
  return n.length > 18 ? `${n.slice(0, 17)}…` : n
}

