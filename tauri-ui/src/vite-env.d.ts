/// <reference types="vite/client" />

// 图片资源的模块声明（Vite 会把这些当成静态资源 URL 处理）。
// 项目里只用到 png/svg/ico 这几种。
declare module '*.png' {
  const src: string
  export default src
}

declare module '*.jpg' {
  const src: string
  export default src
}

declare module '*.svg' {
  const src: string
  export default src
}
