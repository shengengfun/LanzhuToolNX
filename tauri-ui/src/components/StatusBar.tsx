import * as React from 'react'
import { Activity, Cpu, GaugeCircle, HardDrive, MemoryStick } from 'lucide-react'
import { useApp } from '~/state'
import * as api from '~/lib/api'
import type { SysStats } from '~/lib/types'
import { Progress } from './ui'

/**
 * 底栏：就绪指示灯 + 总体压制进度 + 实时性能监控。
 *
 * 为什么进度放这里而不是侧栏：
 * 侧栏是"去哪一页"，底栏是"现在什么状态"。长任务的进度属于状态，
 * 而且放在底栏后，无论用户翻到哪一页都看得见 —— 侧栏那个小圆点做不到这点。
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
  const pct = hasProgress ? Math.round((progress!.done / progress!.total) * 100) : 0

  return (
    <footer className="relative flex h-8 shrink-0 items-center gap-3 border-t border-border/60 bg-card/70 px-3 text-[11.5px] text-muted-foreground backdrop-blur">
      {/* 总体进度：贴在底栏上沿的一条细线，任何页面都看得见 */}
      {running && (
        <div className="absolute inset-x-0 top-0 h-0.5 bg-muted/60">
          <div
            className="h-full bg-primary transition-all duration-500"
            style={{ width: `${pct}%` }}
          />
        </div>
      )}

      <span className="flex items-center gap-1.5">
        <span
          className={`size-1.5 rounded-full ${running ? 'animate-pulse bg-primary' : 'bg-primary/70'}`}
        />
        {running ? '正在压制' : '就绪'}
      </span>

      {running && (
        <>
          <div className="flex w-44 items-center gap-2">
            <Progress value={pct} />
            <span className="shrink-0 tabular-nums">
              {hasProgress ? `${pct}% · ${progress!.done}/${progress!.total}` : '…'}
            </span>
          </div>
          <span className="max-w-[320px] truncate font-mono text-[11px] opacity-80">
            {runningCmd}
          </span>
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

/** 性能监控区：CPU / GPU / 内存（取不到的项显示 --，绝不显示假数字）。 */
function Monitor({ stats }: { stats: SysStats }) {
  const memPct = stats.memTotal > 0 ? Math.round((stats.memUsed / stats.memTotal) * 100) : -1
  const gpuPct = stats.gpu >= 0 ? Math.round(stats.gpu) : -1

  return (
    <div className="flex items-center gap-2.5">
      {stats.procs > 0 && (
        <span className="hidden items-center gap-1 rounded-md bg-primary/12 px-1.5 py-0.5 text-primary xl:flex">
          <GaugeCircle className="size-3" />
          {stats.procs} 进程
        </span>
      )}

      <Meter icon={<Cpu className="size-3" />} label="CPU" pct={stats.cpu >= 0 ? stats.cpu : -1} />
      <Meter
        icon={<HardDrive className="size-3" />}
        label={stats.gpuName ? shortGpu(stats.gpuName) : 'GPU'}
        pct={gpuPct}
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

/** 显卡名太长（"NVIDIA GeForce RTX 4070 Ti SUPER"）会挤爆底栏，留前几个词就行。 */
function shortGpu(name: string) {
  const n = name.replace(/\((R|TM)\)/g, '').trim()
  return n.length > 18 ? `${n.slice(0, 17)}…` : n
}

