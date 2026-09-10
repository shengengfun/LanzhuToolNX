import React from 'react'
import ReactDOM from 'react-dom/client'
import App from './App'
import './globals.css'

// 桌面应用里不需要浏览器右键菜单和选中行为
document.addEventListener('contextmenu', (e) => e.preventDefault())

ReactDOM.createRoot(document.getElementById('root') as HTMLElement).render(
  <React.StrictMode>
    <App />
  </React.StrictMode>,
)
