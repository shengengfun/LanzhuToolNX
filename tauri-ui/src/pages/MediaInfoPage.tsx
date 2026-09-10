import * as React from 'react'
import { Clock, FileSearch, Loader2, Trash2 } from 'lucide-react'
import { Badge, Button, Card, EmptyState, Field, GroupCard, Row } from '~/components/ui'
import { PathRow } from '~/components/common'
import { useApp, pickFile, useDropZone } from '~/state'
import * as api from '~/lib/api'
import type { MediaInfo } from '~/lib/types'
import { splitPath } from '~/lib/utils'

export function MediaInfoPage() {
  const { notify, settings, patchSettings, pushRecent } = useApp()
  const [path, setPath] = React.useState('')
  const [info, setInfo] = React.useState<MediaInfo | null>(null)
  const [busy, setBusy] = React.useState(false)

  const probe = React.useCallback(
    async (p: string) => {
      if (!p) return
      setPath(p)
      setBusy(true)
      try {
        setInfo(await api.probeMedia(p))
        pushRecent(p)
      } catch (e) {
        setInfo(null)
        notify(String(e).replace(/^Error:\s*/, ''), 'error')
      } finally {
        setBusy(false)
      }
    },
    [notify, pushRecent],
  )

  const drop = useDropZone((paths) => void probe(paths[0]))

  return (
    <div className="flex h-full min-h-0 max-w-[900px] flex-col gap-3 px-0.5 pt-1 pr-1">
      <Card className="space-y-2 p-3">
        <PathRow
          label="文件"
          value={path}
          onChange={setPath}
          onBrowse={() => void pickFile('选择媒体文件').then((p) => p && void probe(p))}
          onDropFile={(paths) => void probe(paths[0])}
        />
        <Row className="gap-2">
          <Button size="sm" variant="outline" disabled={!path || busy} onClick={() => void probe(path)}>
            {busy ? <Loader2 className="size-3.5 animate-spin" /> : <FileSearch className="size-3.5" />}
            解析
          </Button>
          <span className="flex-1" />
          {info && <Badge variant={info.exists ? 'success' : 'destructive'}>{info.exists ? '存在' : '不存在'}</Badge>}
        </Row>
      </Card>

      {settings.recentFiles.length > 0 && (
        <GroupCard
          title="最近打开"
          right={
            <Button
              size="sm"
              variant="ghost"
              className="h-5 px-1.5 text-[11px]"
              onClick={() => patchSettings({ recentFiles: [] })}
            >
              <Trash2 className="size-3" />
              清空
            </Button>
          }
        >
          <div className="flex flex-wrap gap-1.5">
            {settings.recentFiles.map((p) => (
              <button
                key={p}
                type="button"
                title={p}
                onClick={() => void probe(p)}
                className="flex max-w-[280px] items-center gap-1.5 rounded-lg border border-border/60 bg-muted/30 px-2 py-1 text-left text-[11.5px] transition-colors hover:border-primary/50 hover:bg-primary/8"
              >
                <Clock className="size-3 shrink-0 text-muted-foreground" />
                <span className="truncate font-medium">{splitPath(p).stem}</span>
                <span className="shrink-0 font-mono text-[10.5px] text-muted-foreground">
                  {splitPath(p).ext}
                </span>
              </button>
            ))}
          </div>
        </GroupCard>
      )}

      <div
        {...drop.props}
        className={`flex min-h-0 flex-1 flex-col rounded-2xl transition-colors ${
          drop.over ? 'ring-2 ring-primary/60' : ''
        }`}
      >
        {!info ? (
          <Card className="flex min-h-0 flex-1 items-center justify-center">
            <EmptyState title="把文件拖进来" />
          </Card>
        ) : (
          <div className="flex min-h-0 flex-1 flex-col gap-3 overflow-y-auto pr-1">
            <GroupCard title="常规">
              <div className="grid grid-cols-2 gap-x-4 gap-y-1.5">
                <KV k="容器" v={info.container || '—'} />
                <KV k="时长" v={info.durationSec > 0 ? `${info.durationSec.toFixed(3)} s` : '—'} />
                <KV k="大小" v={info.sizeBytes > 0 ? `${(info.sizeBytes / 1024 / 1024).toFixed(2)} MiB` : '—'} />
                <KV k="总码率" v={info.bitrate > 0 ? `${Math.round(info.bitrate / 1000)} kb/s` : '—'} />
              </div>
            </GroupCard>

            {info.video && (
              <GroupCard title="视频轨">
                <div className="grid grid-cols-2 gap-x-4 gap-y-1.5">
                  <KV k="编码" v={info.video.codec} />
                  <KV k="分辨率" v={`${info.video.width} × ${info.video.height}`} />
                  <KV k="帧率" v={`${info.video.fps} fps`} />
                  <KV k="像素格式" v={info.video.pixFmt || '—'} />
                  <KV k="位深" v={info.video.bitDepth ? `${info.video.bitDepth} bit` : '—'} />
                </div>
              </GroupCard>
            )}

            {info.audio && (
              <GroupCard title="音频轨">
                <div className="grid grid-cols-2 gap-x-4 gap-y-1.5">
                  <KV k="编码" v={info.audio.codec} />
                  <KV k="声道" v={`${info.audio.channels}`} />
                  <KV k="采样率" v={`${info.audio.sampleRate} Hz`} />
                  <KV k="码率" v={info.audio.bitrate > 0 ? `${Math.round(info.audio.bitrate / 1000)} kb/s` : '—'} />
                </div>
              </GroupCard>
            )}

            <GroupCard title="原始输出" className="flex min-h-0 flex-1 flex-col">
              <pre
                data-selectable
                className="min-h-[160px] flex-1 overflow-auto rounded-xl bg-muted/25 p-2.5 font-mono text-[11px] leading-relaxed whitespace-pre-wrap"
              >
                {info.raw || '—'}
              </pre>
            </GroupCard>
          </div>
        )}
      </div>
    </div>
  )
}

function KV({ k, v }: { k: string; v: string }) {
  return (
    <Field label={k} labelWidth={64}>
      <span className="truncate font-mono text-[12px]">{v}</span>
    </Field>
  )
}
