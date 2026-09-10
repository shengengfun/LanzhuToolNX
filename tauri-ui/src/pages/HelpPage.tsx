import { ExternalLink, Github, Info } from 'lucide-react'
import { Badge, Card, Row } from '~/components/ui'

export function HelpPage() {
  return (
    <div className="flex h-full min-h-0 max-w-[880px] flex-col gap-3 overflow-y-auto px-0.5 pt-1 pr-1">
      <Card className="p-4">
        <Row className="gap-3">
          <span className="flex size-11 items-center justify-center rounded-2xl bg-primary text-xl font-bold text-primary-foreground shadow-sm">
            岚
          </span>
          <div>
            <div className="flex items-center gap-2">
              <span className="text-[15px] font-semibold">岚珠工具箱</span>
              <Badge>v1.1.0</Badge>
            </div>
            <div className="mt-0.5 text-[12px] text-muted-foreground">
              原 WinForms 版的界面重构，压制逻辑按原命令行模板逐条对齐。
            </div>
          </div>
        </Row>
      </Card>

      <Card className="flex items-center gap-3 p-3">
        <Info className="size-4 shrink-0 text-muted-foreground" />
        <span className="text-[12px] text-muted-foreground">项目主页</span>
        <span className="flex-1" />
        <Row className="gap-1.5">
          <Github className="size-3.5 text-muted-foreground" />
          <span className="text-[11.5px] text-muted-foreground">shengengfun</span>
          <ExternalLink className="size-3 text-muted-foreground" />
        </Row>
      </Card>
    </div>
  )
}
