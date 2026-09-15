import { clsx, type ClassValue } from 'clsx'
import { twMerge } from 'tailwind-merge'

/** 等价于参考项目的 cn()：类名合并 + Tailwind 冲突消解。 */
export function cn(...inputs: ClassValue[]) {
  return twMerge(clsx(inputs))
}

/**
 * 把 Windows 路径拆成 目录 / 文件名（不含扩展名） / 扩展名。
 *
 * ⚠️ `dir` **带结尾分隔符**（`"D:\\a\\b.mp4"` → `"D:\\a\\"`），
 * 因为下游一律用 `dir + 文件名` 拼路径。以前这里把分隔符切掉了，
 * 结果 `changeExt()` / 抽取页拼出来的路径全是 `D:\Transname.mp4` 这种缺分隔符的乱码。
 */
export function splitPath(p: string) {
  const norm = p.replace(/\\/g, '/')
  const i = norm.lastIndexOf('/')
  const dir = i >= 0 ? p.slice(0, i + 1) : ''
  const base = i >= 0 ? norm.slice(i + 1) : norm
  const j = base.lastIndexOf('.')
  return {
    dir,
    stem: j > 0 ? base.slice(0, j) : base,
    ext: j > 0 ? base.slice(j) : '',
  }
}

/** 换掉扩展名（目录、文件名原样保留）。 */
export function changeExt(p: string, ext: string) {
  const { dir, stem } = splitPath(p)
  return `${dir}${stem}${ext}`
}

export function fileStem(p: string) {
  return splitPath(p).stem
}

/**
 * 「压制格式」→ 输出扩展名。
 * 和 Rust 侧 `cmd::output_ext()` 保持同一张表（那边是命令行拼装的唯一真相）。
 */
export function extForFormat(format: string) {
  if (format === 'MOV') return '.mov'
  if (format === 'FLV') return '.flv'
  return '.mp4'
}

const pad2 = (n: number) => String(n).padStart(2, '0')

/** 秒 → `1:23:45.678` / `12:34.567`，粗剪的时间轴一直用它。 */
export function fmtTime(sec: number) {
  const v = Number.isFinite(sec) && sec > 0 ? sec : 0
  const ms = Math.round((v % 1) * 1000)
  const s = Math.floor(v)
  const h = Math.floor(s / 3600)
  const m = Math.floor((s % 3600) / 60)
  const ss = s % 60
  const base = h > 0 ? `${h}:${pad2(m)}:${pad2(ss)}` : `${m}:${pad2(ss)}`
  return `${base}.${String(ms).padStart(3, '0')}`
}

/**
 * `1:23.5` / `83.5` / `1:02:03` → 秒。解析不了返回 null（调用方保留原值）。
 */
export function parseTime(text: string): number | null {
  const t = text.trim()
  if (!t) return null
  if (/^\d+(\.\d+)?$/.test(t)) return Number(t)
  const parts = t.split(':')
  if (parts.length > 3) return null
  let total = 0
  for (const part of parts) {
    const v = Number(part.trim())
    if (!Number.isFinite(v) || v < 0) return null
    total = total * 60 + v
  }
  return total
}

/** 数字 + 单位的人类可读大小。 */
export function humanSize(bytes: number) {
  if (!bytes || bytes <= 0) return '--'
  const u = ['B', 'KB', 'MB', 'GB', 'TB']
  let i = 0
  let v = bytes
  while (v >= 1024 && i < u.length - 1) {
    v /= 1024
    i++
  }
  return `${v.toFixed(v >= 100 || i === 0 ? 0 : 2)} ${u[i]}`
}
