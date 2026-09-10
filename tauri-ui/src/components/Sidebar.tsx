import type { LucideIcon } from 'lucide-react'
import {
  Film,
  HelpCircle,
  Info,
  Music,
  Package,
  Settings,
  Wand2,
  Wrench,
} from 'lucide-react'
import { cn } from '~/lib/utils'

export type PageId =
  | 'video'
  | 'audio'
  | 'mux'
  | 'avs'
  | 'misc'
  | 'mediainfo'
  | 'settings'
  | 'help'

export const NAV_MAIN: { id: PageId; label: string; icon: LucideIcon; hint: string }[] = [
  { id: 'video', label: '视频', icon: Film, hint: '视频压制 / 批量压制' },
  { id: 'audio', label: '音频', icon: Music, hint: '音频转码 / 抽取 / 波形粗剪' },
  { id: 'mux', label: '封装抽取', icon: Package, hint: '重新封装，或抽出视频 / 音频 / 轨道' },
  { id: 'avs', label: 'AVS', icon: Wand2, hint: 'AviSynth 脚本压制' },
  { id: 'misc', label: '常用', icon: Wrench, hint: '粗剪 / 单图视频 / 黑帧 等小工具' },
]

export const NAV_SUB: { id: PageId; label: string; icon: LucideIcon; hint: string }[] = [
  { id: 'mediainfo', label: 'MediaInfo', icon: Info, hint: '媒体信息查看' },
  { id: 'settings', label: '设置', icon: Settings, hint: '工具目录与输出目录' },
  { id: 'help', label: '帮助', icon: HelpCircle, hint: '使用说明与关于' },
]

export function Sidebar({
  page,
  onSelect,
}: {
  page: PageId
  onSelect: (p: PageId) => void
  /** 保留参数以兼容旧调用方；侧栏不再显示运行状态（底栏有就绪灯+总进度） */
  running?: boolean
}) {
  return (
    <nav className="flex w-[74px] shrink-0 flex-col items-stretch gap-1 border-r border-border/60 bg-card/55 py-2 backdrop-blur">
      {NAV_MAIN.map((n) => (
        <NavItem key={n.id} item={n} active={page === n.id} onClick={() => onSelect(n.id)} />
      ))}

      <div className="mx-auto my-1 h-px w-8 bg-border/70" />

      {NAV_SUB.map((n) => (
        <NavItem key={n.id} item={n} active={page === n.id} onClick={() => onSelect(n.id)} />
      ))}

      {/**
        * 这里以前有个「空闲 / 压制中」的小圆点 + 文字。
        * 它和底栏那个就绪灯完全重复，而且底栏还能顺便显示总进度，
        * 所以左栏这块直接去掉，把空间留给导航。
        */}
      <div className="flex-1" />
    </nav>
  )
}

function NavItem({
  item,
  active,
  onClick,
}: {
  item: { label: string; icon: LucideIcon; hint: string }
  active: boolean
  onClick: () => void
}) {
  const Icon = item.icon
  return (
    <button
      type="button"
      title={item.hint}
      onClick={onClick}
      className={cn(
        'mx-1.5 flex flex-col items-center gap-1 rounded-xl py-2 transition-all duration-150',
        active
          ? 'bg-primary/12 text-primary shadow-2xs'
          : 'text-muted-foreground hover:bg-accent/60 hover:text-foreground',
      )}
    >
      <Icon className="size-[18px]" strokeWidth={active ? 2.4 : 1.9} />
      <span className={cn('text-[11px]', active && 'font-semibold')}>{item.label}</span>
    </button>
  )
}
