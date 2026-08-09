import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
import { readdirSync } from 'node:fs'
import { resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

// 窗口模式：src/window/<窗口名>/ 就是一个窗口页面。
// Vite 根目录设为 src，输出时保留 src 内的相对路径 →
// src/window/main/index.html → wwwroot/window/main/index.html，
// 于是窗口路径 "main" 正好对应 app://localhost/window/main/index.html。
// wwwroot 里 window/ 放窗口页面，其余任意文件夹（icon/ 等）来自 public/。
const projectDir = fileURLToPath(new URL('.', import.meta.url))
const srcDir = resolve(projectDir, 'src')
const windowsDir = resolve(srcDir, 'window')

// 构建输出目录：由前端工程 .csproj（WebWindowUIBuildFrontend）经 WWWROOT_DIR 环境变量传入（消费方产物目录的 wwwroot，
// 如 WebWindowUI.Demo.Todo\bin\...\wwwroot），vite 直接写进产物文件夹、不落在工程内；
// 单独跑 npm run build（未设环境变量）时回退到本工程根 wwwroot（gitignore 覆盖）。
const outDir = process.env.WWWROOT_DIR
  ? resolve(process.env.WWWROOT_DIR)
  : resolve(projectDir, 'wwwroot')

// 压缩/混淆只对 Release 开启（WWUI_CONFIGURATION 由 WebWindowUIBuildFrontend 传入 $(Configuration)）：
// Debug 保持可读产物（不压缩空白、不改写标识符），开发时直接在产物里看/改/断点；
// 手动跑 npm run build（未设环境变量）按生产处理（压缩）。minify=false 同时关掉 JS 压缩与标识符混淆，
// cssMinify 单独显式跟随（vite 5.4+ 与 minify 解耦）。sourcemap 仅 Debug 内联，方便浏览器断点定位源码。
// Release 用 true 而非 'esbuild'：本仓库 vite 8 是 rolldown 内核，写死 'esbuild' 会去加载未安装的 esbuild 包报错。
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
  // app:// 自定义协议下必须用相对路径；Vite 会按每个窗口的输出位置计算相对 assets 路径
  base: './',
  // 共享静态资源（logo.svg 等），构建时复制到 wwwroot 根目录
  publicDir: resolve(projectDir, 'public'),
  // 唯一 package.json / node_modules 都在本工程根（webwindowui-bridge 是 npm 注册表依赖，
  // 按版本装进 node_modules），dev 模式只允许服务本工程根内的文件
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
