import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
import { readdirSync } from 'node:fs'
import { resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

// 窗口模式：src/window/<窗口名>/ 就是一个窗口页面。
// Vite 根目录设为 src，输出时保留 src 内的相对路径 →
// src/window/main/index.html → wwwroot/window/main/index.html，
// 于是窗口路径 "main" 正好对应 app://localhost/window/main/index.html。
const projectDir = fileURLToPath(new URL('.', import.meta.url))
const srcDir = resolve(projectDir, 'src')
const windowsDir = resolve(srcDir, 'window')

// 构建输出目录：由前端工程 .csproj（WebWindowUIBuildFrontend）经 WWWROOT_DIR 环境变量传入（消费方产物目录的 wwwroot，
// 如 WebWindowUI.Demo.ImageGallery\bin\...\wwwroot），vite 直接写进产物文件夹、不落在工程内；
// 单独跑 npm run build（未设环境变量）时回退到本工程根 wwwroot（gitignore 覆盖）。
const outDir = process.env.WWWROOT_DIR
  ? resolve(process.env.WWWROOT_DIR)
  : resolve(projectDir, 'wwwroot')

const isRelease = process.env.WWUI_CONFIGURATION === 'Release'
const windowNames = readdirSync(windowsDir, { withFileTypes: true })
  .filter(d => d.isDirectory() && !d.name.startsWith('.'))
  .map(d => d.name)

const input = Object.fromEntries(
  windowNames.map(name => [name, resolve(windowsDir, name, 'index.html')])
)

export default defineConfig({
  root: srcDir,
  plugins: [vue()],
  base: './',
  publicDir: resolve(projectDir, 'public'),
  server: {
    fs: { allow: [resolve(projectDir)] },
  },
  build: {
    outDir,
    emptyOutDir: true,
    minify: isRelease ? true : false,
    cssMinify: isRelease ? true : false,
    sourcemap: isRelease ? false : 'inline',
    rollupOptions: { input },
  },
})
