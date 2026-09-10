import * as React from 'react'
import {
  AlertTriangle,
  CheckCircle2,
  Download,
  Loader2,
  Package,
  PackagePlus,
  Square,
  Upload,
  XCircle,
} from 'lucide-react'
import { Badge, Button, Checkbox, GroupCard, Row, Textarea } from './ui'
import { useApp } from '~/state'
import * as api from '~/lib/api'
import type { ToolPackage, ToolProgress } from '~/lib/types'
import { cn } from '~/lib/utils'
import { pickFolder, pickSave, pickFile } from '~/state'

type Busy = { pkg: string; phase: string; message: string; percent: number } | null

export function ToolsFetch({ onChanged }: { onChanged: () => void }) {
  const { settings, patchSettings, notify } = useApp()
  const [pkgs, setPkgs] = React.useState<ToolPackage[]>([])
  const [mirrors, setMirrors] = React.useState<string[]>([])
  const [editMirrors, setEditMirrors] = React.useState(false)
  const [busy, setBusy] = React.useState<Busy>(null)
  const [selected, setSelected] = React.useState<Set<string>>(new Set())

  const reload = React.useCallback(async () => {
    try {
      const [list, dm] = await Promise.all([api.toolPackages(), api.defaultMirrors()])
      setPkgs(list)
      setMirrors(settings.mirrors.length ? settings.mirrors : dm)
      // 默认勾选：必装的 + 还没装的
      const auto = new Set<string>()
      list.forEach((p) => {
        if (!p.installed && p.downloadable && p.required) auto.add(p.id)
      })
      setSelected((prev) => (prev.size ? prev : auto))
    } catch {
      setPkgs([])
    }
  }, [settings.mirrors])

  React.useEffect(() => {
    void reload()
  }, [reload])

  // 订阅下载进度
  React.useEffect(() => {
    const un: (() => void)[] = []
    let alive = true

    api
      .onToolProgress((p: ToolProgress) => {
        if (!alive) return
        if (p.phase === 'done' || p.phase === 'error') {
          setBusy({ pkg: p.pkg, phase: p.phase, message: p.message, percent: p.phase === 'done' ? 100 : 0 })
        } else {
          setBusy({ pkg: p.pkg, phase: p.phase, message: p.message, percent: p.percent })
        }
      })
      .then((f) => (alive ? un.push(f) : f()))

    api
      .onToolsFinished((e) => {
        if (!alive) return
        setBusy(null)
        void reload().then(onChanged)
        if (e.failed.length) notify(`有 ${e.failed.length} 个包失败：${e.failed[0]}`, 'error')
        else notify('工具获取完成')
      })
      .then((f) => (alive ? un.push(f) : f()))

    api
      .onToolsExported((e) => {
        if (!alive) return
        setBusy(null)
        if (e.ok) notify(`离线包已导出，共 ${e.files} 个文件`)
        else notify(e.error ?? '导出失败', 'error')
      })
      .then((f) => (alive ? un.push(f) : f()))

    return () => {
      alive = false
      un.forEach((f) => f())
    }
  }, [reload, notify, onChanged])

  const doDownload = async (ids: string[]) => {
    if (!ids.length) {
      notify('没有勾选任何工具包', 'error')
      return
    }
    const target = await api.downloadTarget()
    if (settings.toolsDir !== target) patchSettings({ toolsDir: target })
    setBusy({ pkg: '', phase: 'start', message: '正在准备…', percent: 0 })
    try {
      await api.downloadTools(ids, target, mirrors)
    } catch (e) {
      setBusy(null)
      notify(String(e).replace(/^Error:\s*/, ''), 'error')
    }
  }

  const missingRequired = pkgs.filter((p) => p.required && !p.installed)

  return (
    <GroupCard
      title="工具获取"
      right={
        missingRequired.length > 0 ? (
          <Badge variant="destructive">缺 {missingRequired.length} 项核心工具</Badge>
        ) : (
          <Badge variant="success">核心工具齐备</Badge>
        )
      }
    >
      {/* ---------- 镜像源 ---------- */}
      <div className="mb-3 rounded-xl bg-muted/30 p-2.5">
        <Row className="gap-2">
          <span className="text-[12px] font-semibold text-muted-foreground">下载加速源</span>
          <span className="flex-1" />
          <Button size="sm" variant="ghost" onClick={() => setEditMirrors((v) => !v)}>
            {editMirrors ? '收起' : '编辑'}
          </Button>
        </Row>

        {editMirrors ? (
          <>
            <Textarea
              value={mirrors.join('\n')}
              onChange={(e) => setMirrors(e.target.value.split('\n'))}
              spellCheck={false}
              className="mt-2 min-h-[92px] text-[11.5px]"
              placeholder="每行一个前缀，例如 https://ghfast.top/&#10;空的一行表示回落直连"
            />
            <Row className="mt-2 gap-2">
              <Button
                size="sm"
                onClick={() => {
                  patchSettings({ mirrors: mirrors.filter((m) => m.trim() !== '\n') })
                  notify('镜像列表已保存')
                }}
              >
                保存
              </Button>
              <Button
                size="sm"
                variant="outline"
                onClick={() =>
                  void api.defaultMirrors().then((d) => {
                    setMirrors(d)
                    patchSettings({ mirrors: d })
                  })
                }
              >
                恢复默认
              </Button>
              <span className="text-[11px] text-muted-foreground">
                按顺序尝试，全部失败才回落直连
              </span>
            </Row>
          </>
        ) : (
          <div className="mt-1.5 space-y-0.5">
            {mirrors.map((m, i) => (
              <div key={i} className="truncate font-mono text-[11px] text-muted-foreground">
                {i + 1}. {m.trim() || '（直连 github.com）'}
              </div>
            ))}
          </div>
        )}
      </div>

      {/* ---------- 包列表 ---------- */}
      <div className="space-y-1.5">
        {pkgs.length === 0 && (
          <div className="flex items-center gap-2 py-3 text-[12px] text-muted-foreground">
            <Loader2 className="size-3.5 animate-spin" />
            正在扫描已安装的工具…
          </div>
        )}

        {pkgs.map((p) => (
          <div
            key={p.id}
            className={cn(
              'flex items-center gap-2.5 rounded-xl border px-2.5 py-2 transition-colors',
              p.installed
                ? 'border-border/50 bg-card/50'
                : p.required
                  ? 'border-destructive/35 bg-destructive/6'
                  : 'border-border/50 bg-muted/20',
            )}
          >
            <Checkbox
              checked={selected.has(p.id)}
              onCheckedChange={(v) => {
                setSelected((s) => {
                  const n = new Set(s)
                  if (v) n.add(p.id)
                  else n.delete(p.id)
                  return n
                })
              }}
              disabled={p.installed || !p.downloadable}
            />

            <div className="min-w-0 flex-1">
              <Row className="gap-2">
                <span className="text-[12.5px] font-semibold">{p.name}</span>
                {p.required && <Badge variant="accent">必需</Badge>}
                {p.installed ? (
                  <CheckCircle2 className="size-3.5 text-primary" />
                ) : (
                  <XCircle className="size-3.5 text-destructive/70" />
                )}
              </Row>
              <div className="truncate text-[11.5px] text-muted-foreground" title={p.desc}>
                {p.desc}
              </div>
              {!p.installed && p.missing.length > 0 && (
                <div className="truncate font-mono text-[11px] text-destructive/80">
                  缺：{p.missing.join(', ')}
                </div>
              )}
            </div>

            <span className="shrink-0 text-[11px] text-muted-foreground tabular-nums">
              {p.approxMb} MB
            </span>

            {p.installed ? (
              <span className="shrink-0 text-[11px] text-primary">已安装</span>
            ) : p.downloadable ? (
              <Button
                size="sm"
                variant="outline"
                disabled={!!busy}
                onClick={() => void doDownload([p.id])}
              >
                <Download className="size-3" />
                下载
              </Button>
            ) : (
              <span className="shrink-0 text-[11px] text-muted-foreground">需离线导入</span>
            )}
          </div>
        ))}
      </div>

      {/* ---------- 进度 ---------- */}
      {busy && (
        <div className="mt-3 rounded-xl border border-border/60 bg-card/70 p-2.5">
          <Row className="gap-2">
            <Loader2
              className={cn('size-3.5 shrink-0', busy.phase === 'error' ? 'text-destructive' : 'animate-spin text-primary')}
            />
            <span className="min-w-0 flex-1 truncate text-[12px]">
              {busy.phase === 'error' ? '失败：' : ''}
              {busy.message}
            </span>
            {busy.percent > 0 && (
              <span className="shrink-0 text-[11px] tabular-nums text-muted-foreground">
                {busy.percent.toFixed(0)}%
              </span>
            )}
            <Button size="sm" variant="destructive" onClick={() => void api.cancelToolsDownload()}>
              <Square className="size-3" />
              取消
            </Button>
          </Row>
          {busy.percent > 0 && (
            <div className="mt-2 h-1 w-full overflow-hidden rounded-full bg-muted">
              <div
                className="h-full rounded-full bg-primary transition-all"
                style={{ width: `${Math.min(100, busy.percent)}%` }}
              />
            </div>
          )}
        </div>
      )}

      {/* ---------- 操作 ---------- */}
      <Row className="mt-3 flex-wrap gap-2">
        <Button size="sm" variant="default" disabled={!!busy} onClick={() => void doDownload([...selected])}>
          <Download className="size-3.5" />
          下载勾选项（{selected.size}）
        </Button>
        <Button
          size="sm"
          variant="outline"
          disabled={!!busy || missingRequired.length === 0}
          onClick={() => void doDownload(missingRequired.filter((p) => p.downloadable).map((p) => p.id))}
        >
          只补缺失的核心项
        </Button>
        <span className="flex-1" />
        <Button
          size="sm"
          variant="secondary"
          disabled={!!busy}
          onClick={() =>
            void pickFile('选择离线工具包', [
              { name: '压缩包', extensions: ['zip', '7z'] },
              { name: '所有文件', extensions: ['*'] },
            ]).then(async (p) => {
              if (!p) return
              const target = await api.downloadTarget()
              try {
                const msg = await api.importOfflineTools(p, target)
                notify(msg)
                await reload()
                onChanged()
              } catch (e) {
                notify(String(e).replace(/^Error:\s*/, ''), 'error')
              }
            })
          }
        >
          <PackagePlus className="size-3.5" />
          从离线包导入
        </Button>
        <Button
          size="sm"
          variant="secondary"
          disabled={!!busy}
          onClick={() =>
            void pickFolder('选择包含工具的文件夹').then(async (dir) => {
              if (!dir) return
              const target = await api.downloadTarget()
              try {
                const msg = await api.importOfflineTools(dir, target)
                notify(msg)
                await reload()
                onChanged()
              } catch (e) {
                notify(String(e).replace(/^Error:\s*/, ''), 'error')
              }
            })
          }
        >
          <Upload className="size-3.5" />
          从文件夹导入
        </Button>
        <Button
          size="sm"
          variant="secondary"
          disabled={!!busy}
          onClick={() =>
            void pickSave('导出离线工具包', 'lanzhutool-tools.zip', [
              { name: '压缩包', extensions: ['zip'] },
            ]).then(async (out) => {
              if (!out) return
              const target = await api.resolveToolsDir()
              setBusy({ pkg: 'export', phase: 'export', message: '正在打包…', percent: 0 })
              try {
                await api.exportOfflineTools({ output: out, toolsDir: target, excludeFfplay: true })
              } catch (e) {
                setBusy(null)
                notify(String(e).replace(/^Error:\s*/, ''), 'error')
              }
            })
          }
        >
          <Package className="size-3.5" />
          导出离线包
        </Button>
      </Row>

      <Row className="mt-3 gap-2 text-[11.5px] text-muted-foreground">
        <AlertTriangle className="mt-0.5 size-3.5 shrink-0 text-amber-500" />
        <span>下载失败时可用离线包导入 / 导出。</span>
      </Row>
    </GroupCard>
  )
}
