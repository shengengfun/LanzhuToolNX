import * as React from 'react'
import { Copy, ListChecks, Save, ScrollText, Trash2 } from 'lucide-react'
import { Button, EmptyState, Row, Select } from './ui'
import { cn } from '~/lib/utils'
import { useApp, pickSave } from '~/state'
import * as api from '~/lib/api'

const LOG_LEVEL_LABEL: Record<string, string> = {
  all: '全部',
  warn: '警告↑',
  error: '仅错误',
}

type Tab = 'log' | 'task'

type LineLevel = 'error' | 'warn' | 'info'

/** 把一行输出归到三档之一，「记录范围」按这个过滤。 */
function lineLevel(t: string): LineLevel {
  if (/\berror\b|失败|错误|Invalid|No such|is not recognized|不是内部或外部命令/i.test(t)) {
    return 'error'
  }
  if (/\bwarn|warning|警告/i.test(t)) return 'warn'
  return 'info'
}

/** 是否按当前「记录范围」展示这一行。 */
function passLevel(t: string, level: string) {
  const l = lineLevel(t)
  if (level === 'error') return l === 'error'
  if (level === 'warn') return l === 'error' || l === 'warn'
  return true
}

const LEVEL_COLOR = (t: string) => {
  const l = lineLevel(t)
  if (l === 'error') return 'text-destructive'
  if (l === 'warn') return 'text-amber-600'
  if (t.startsWith('>')) return 'text-primary'
  if (t.startsWith('=====')) return 'font-semibold text-muted-foreground'
  return ''
}

export function OutputPanel() {
  const { log, clearLog, running, progress, runningCmd, settings, patchSettings, notify } = useApp()
  const [tab, setTab] = React.useState<Tab>('log')
  const [autoScroll, setAutoScroll] = React.useState(settings.autoScrollLog)
  const [collapsed, setCollapsed] = React.useState(false)
  const boxRef = React.useRef<HTMLDivElement>(null)

  // 跟随设置面板里的默认值
  React.useEffect(() => setAutoScroll(settings.autoScrollLog), [settings.autoScrollLog])

  React.useEffect(() => {
    if (!autoScroll || tab !== 'log') return
    const el = boxRef.current
    if (el) el.scrollTop = el.scrollHeight
  }, [log, autoScroll, tab])

  // 记录范围（全部 / 警告 / 错误）—— 界面只显示这一档，复制/保存也跟着走
  const level = settings.logLevel || 'all'
  const shown = React.useMemo(() => log.filter((l) => passLevel(l.text, level)), [log, level])
  const hidden = log.length - shown.length
  const text = shown.map((l) => l.text).join('\n')

  return (
    <aside
      className={cn(
        'flex shrink-0 flex-col overflow-hidden rounded-2xl border border-border/70 bg-card/80 shadow-sm backdrop-blur transition-all duration-200',
        collapsed ? 'w-11' : 'w-[300px]',
      )}
    >
      {/* 头部：折叠 + 标签 */}
      <div className="flex h-10 shrink-0 items-center gap-1 border-b border-border/60 px-2">
        <button
          type="button"
          onClick={() => setCollapsed((v) => !v)}
          title={collapsed ? '展开输出区' : '收起输出区'}
          className="flex size-7 items-center justify-center rounded-lg text-muted-foreground hover:bg-accent/60"
        >
          <ListChecks className={cn('size-4 transition-transform', collapsed && 'rotate-180')} />
        </button>

        {!collapsed && (
          <>
            <TabBtn active={tab === 'log'} onClick={() => setTab('log')} icon={<ScrollText className="size-3.5" />} text="日志" />
            <TabBtn active={tab === 'task'} onClick={() => setTab('task')} icon={<ListChecks className="size-3.5" />} text="任务" />
          </>
        )}
      </div>

      {collapsed ? (
        <div className="flex flex-1 items-center justify-center">
          <span className="text-[11px] text-muted-foreground [writing-mode:vertical-rl]">输出</span>
        </div>
      ) : (
        <>
          {/* 工具行（把「自动滚动」挤在这行会显示不开，所以它单独一行） */}
          <div className="flex shrink-0 items-center gap-1 border-b border-border/60 px-2 py-1.5">
            <Button
              size="sm"
              variant="outline"
              disabled={!text}
              onClick={() =>
                void pickSave('保存日志', `lanzhulog_${Date.now()}.log`, [
                  { name: '日志', extensions: ['log', 'txt'] },
                ]).then((p) =>
                  p
                    ? api
                        .writeTextFile(p, text)
                        .then(() => notify('日志已保存'))
                        .catch((e) => notify(String(e), 'error'))
                    : void 0,
                )
              }
            >
              <Save className="size-3" />
              保存日志
            </Button>
            <Button
              size="sm"
              variant="outline"
              disabled={!text}
              onClick={() =>
                navigator.clipboard.writeText(text).then(
                  () => notify('日志已复制'),
                  () => notify('复制失败', 'error'),
                )
              }
            >
              <Copy className="size-3" />
              复制
            </Button>
            <Button size="sm" variant="outline" disabled={!text} onClick={clearLog}>
              <Trash2 className="size-3" />
              清空
            </Button>
          </div>

          {/* 选项行：自动滚动 + 记录范围 */}
          <div className="flex shrink-0 items-center gap-2 border-b border-border/60 px-2 py-1 text-[11.5px] text-muted-foreground">
            <label className="flex cursor-pointer items-center gap-1.5">
              <input
                type="checkbox"
                checked={autoScroll}
                onChange={(e) => setAutoScroll(e.target.checked)}
                className="accent-[var(--primary)]"
              />
              自动滚动
            </label>
            <span className="flex-1" />
            {hidden > 0 && <span className="opacity-70">已滤 {hidden} 行</span>}
            <Select
              className="w-[104px]"
              value={LOG_LEVEL_LABEL[level] ?? '全部'}
              onValueChange={(v) =>
                patchSettings({
                  logLevel: Object.entries(LOG_LEVEL_LABEL).find(([, l]) => l === v)?.[0] ?? 'all',
                })
              }
              options={Object.values(LOG_LEVEL_LABEL)}
            />
          </div>

          {/* 内容 */}
          {tab === 'log' && (
            <div
              ref={boxRef}
              data-selectable
              className="min-h-0 flex-1 overflow-auto bg-muted/20 p-2.5 font-mono text-[11.5px] leading-[1.6] whitespace-pre-wrap"
            >
              {shown.length === 0 ? (
                <EmptyState
                  title={log.length === 0 ? '暂无输出' : '当前记录范围下没有内容'}
                />
              ) : (
                shown.map((l) => (
                  <div key={l.id} className={cn('break-all', LEVEL_COLOR(l.text))}>
                    {l.text}
                  </div>
                ))
              )}
            </div>
          )}

          {tab === 'task' && (
            <div className="min-h-0 flex-1 overflow-auto p-3">
              {running || progress ? (
                <div className="space-y-2">
                  <div className="rounded-xl bg-muted/35 p-2.5 font-mono text-[11.5px] break-all">
                    {runningCmd}
                  </div>
                  <Row className="gap-3 text-[12px] text-muted-foreground">
                    <span>
                      进度 {progress ? `${progress.done}/${progress.total}` : '0/0'}
                    </span>
                    <span>{running ? '运行中' : '已结束'}</span>
                  </Row>
                </div>
              ) : (
                <EmptyState title="暂无任务" />
              )}
            </div>
          )}
        </>
      )}
    </aside>
  )
}

function TabBtn({
  active,
  onClick,
  icon,
  text,
}: {
  active: boolean
  onClick: () => void
  icon: React.ReactNode
  text: string
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={cn(
        'flex items-center gap-1.5 rounded-lg px-2 py-1 text-[12px] transition-colors',
        active
          ? 'bg-primary/12 font-semibold text-primary'
          : 'text-muted-foreground hover:bg-accent/60',
      )}
    >
      {icon}
      {text}
    </button>
  )
}
