/**
 * 外观：主题（亮/暗/跟随系统）+ 强调色 + 自定义背景 + 界面缩放。
 *
 * 思路与参考项目 ShiorikoTrans 的 `lib/appearance.ts` 一致：
 * 界面里所有颜色都来自 CSS 变量（globals.css 的 `:root` / `.dark`），
 * 所以改外观 = 改这几个变量，不需要重新渲染 React 树，也不需要任何状态同步。
 */

export interface Hsl {
  h: number
  s: number
  l: number
}

export interface AccentPreset {
  id: string
  name: string
  /** 色板预览用的十六进制（仅用于 UI 上的小圆点） */
  color: string
  light: Hsl
  dark: Hsl
}

export const DEFAULT_ACCENT = 'lanzhu'

export const ACCENT_PRESETS: AccentPreset[] = [
  // 默认色 = 原版岚珠工具箱的绿，选它时**不覆盖**变量，完全跟随 globals.css
  { id: 'lanzhu', name: '岚珠绿', color: '#37b484', light: { h: 157, s: 55, l: 45 }, dark: { h: 158, s: 62, l: 54 } },
  { id: 'blue', name: '晴空蓝', color: '#1677d3', light: { h: 205, s: 74, l: 43 }, dark: { h: 212, s: 95, l: 58 } },
  { id: 'sky', name: '天青', color: '#0284c7', light: { h: 199, s: 89, l: 39 }, dark: { h: 199, s: 92, l: 60 } },
  { id: 'teal', name: '松石', color: '#0d9488', light: { h: 173, s: 80, l: 32 }, dark: { h: 172, s: 70, l: 52 } },
  { id: 'violet', name: '紫罗兰', color: '#7c3aed', light: { h: 262, s: 83, l: 58 }, dark: { h: 258, s: 90, l: 68 } },
  { id: 'rose', name: '莓红', color: '#e11d48', light: { h: 348, s: 77, l: 48 }, dark: { h: 348, s: 90, l: 62 } },
  { id: 'amber', name: '琥珀', color: '#d97706', light: { h: 32, s: 95, l: 44 }, dark: { h: 38, s: 92, l: 56 } },
  { id: 'graphite', name: '石墨', color: '#475569', light: { h: 215, s: 25, l: 35 }, dark: { h: 213, s: 20, l: 65 } },
]

/** `#rgb` / `#rrggbb` → HSL；非法输入返回 null。 */
export function hexToHsl(hex: string): Hsl | null {
  let v = hex.trim().replace(/^#/, '')
  if (v.length === 3) {
    v = v
      .split('')
      .map((c) => c + c)
      .join('')
  }
  if (!/^[0-9a-fA-F]{6}$/.test(v)) return null
  const n = parseInt(v, 16)
  const r = ((n >> 16) & 255) / 255
  const g = ((n >> 8) & 255) / 255
  const b = (n & 255) / 255
  const max = Math.max(r, g, b)
  const min = Math.min(r, g, b)
  const l = (max + min) / 2
  let h = 0
  let s = 0
  if (max !== min) {
    const d = max - min
    s = l > 0.5 ? d / (2 - max - min) : d / (max + min)
    if (max === r) h = (g - b) / d + (g < b ? 6 : 0)
    else if (max === g) h = (b - r) / d + 2
    else h = (r - g) / d + 4
    h *= 60
  }
  return { h: Math.round(h), s: Math.round(s * 100), l: Math.round(l * 100) }
}

const hsl = (c: Hsl) => `hsl(${c.h} ${c.s}% ${c.l}%)`

/**
 * 承载"主色"的全部变量。改这些就能让按钮、焦点环、进度条、选中态一起换色 ——
 * 因为它们本来就都引用同一批令牌。
 */
const ACCENT_VARS = ['--primary', '--ring', '--success', '--accent-foreground'] as const

/** 把强调色应用到 <html>；custom 非空时优先于预设。 */
export function applyAccent(presetId: string, customHex: string, dark: boolean) {
  const el = document.documentElement
  for (const v of ACCENT_VARS) el.style.removeProperty(v)

  let resolved: Hsl | null = null
  const custom = customHex.trim()
  if (custom) {
    const h = hexToHsl(custom)
    // 暗色下把强调色提亮一点，否则深底上糊成一团
    if (h) resolved = dark ? { h: h.h, s: Math.min(100, h.s + 12), l: Math.min(70, h.l + 20) } : h
  }
  if (!resolved && presetId !== DEFAULT_ACCENT) {
    const p = ACCENT_PRESETS.find((x) => x.id === presetId)
    if (p) resolved = dark ? p.dark : p.light
  }
  if (!resolved) return // 默认色：直接用 globals.css 里的值

  const c = hsl(resolved)
  for (const v of ACCENT_VARS) el.style.setProperty(v, c)
}

/** 主题：light / dark / system。返回最终生效的色板（供状态栏等显示）。 */
export function applyTheme(theme: string): 'light' | 'dark' {
  const prefersDark =
    typeof window !== 'undefined' && window.matchMedia
      ? window.matchMedia('(prefers-color-scheme: dark)').matches
      : false
  const dark = theme === 'dark' || (theme === 'system' && prefersDark)
  document.documentElement.classList.toggle('dark', dark)
  // 让原生控件（滚动条、输入框、颜色选择器）跟着走
  document.documentElement.style.colorScheme = dark ? 'dark' : 'light'
  return dark ? 'dark' : 'light'
}

/** 自定义背景图（传空串则清除）。url 由后端 mediaUrl 生成。 */
export function applyBackground(url: string) {
  const el = document.documentElement
  if (!url) {
    el.style.removeProperty('--app-bg-image')
    el.style.removeProperty('--app-bg-overlay')
    return
  }
  el.style.setProperty('--app-bg-image', `url("${url}")`)
  el.style.setProperty(
    '--app-bg-overlay',
    'linear-gradient(hsl(0 0% 100% / 0.78), hsl(0 0% 100% / 0.78))',
  )
}

/** 界面缩放。Chromium 的 `zoom` 会连布局视口一起缩放，所以整体比例是安全的。 */
export function applyUiScale(scale: number) {
  const z = Number.isFinite(scale) && scale > 0.5 && scale < 1.6 ? scale : 1
  document.documentElement.style.zoom = z === 1 ? '' : String(z)
}

/**
 * 当前强调色的十六进制值。
 *
 * 波形图是后端用 ffmpeg 画的，命令行里只能给十六进制，所以得把
 * CSS 变量里的 `hsl(h s% l%)` 反解成 `RRGGBB` —— 这样波形颜色会自动跟随主题。
 */
export function accentHex(fallback = '2F9E79') {
  if (typeof window === 'undefined') return fallback
  const raw = getComputedStyle(document.documentElement).getPropertyValue('--primary').trim()
  const m = raw.match(/hsl\(\s*([\d.]+)\s*[, ]\s*([\d.]+)%\s*[, ]\s*([\d.]+)%/)
  if (!m) return fallback
  const h = Number(m[1])
  const s = Number(m[2]) / 100
  const l = Number(m[3]) / 100
  const c = (1 - Math.abs(2 * l - 1)) * s
  const x = c * (1 - Math.abs(((h / 60) % 2) - 1))
  const mm = l - c / 2
  const seg = Math.floor(h / 60) % 6
  const rgb: [number, number, number] =
    seg === 0
      ? [c, x, 0]
      : seg === 1
        ? [x, c, 0]
        : seg === 2
          ? [0, c, x]
          : seg === 3
            ? [0, x, c]
            : seg === 4
              ? [x, 0, c]
              : [c, 0, x]
  return rgb
    .map((v) => Math.round((v + mm) * 255).toString(16).padStart(2, '0'))
    .join('')
    .toUpperCase()
}
