<script setup lang="ts">
import { computed, ref } from 'vue'
import { bindImageGalleryModel } from '../../models/ImageGalleryModel'
import type { ImageItemModel } from '../../models/ImageItemModel'

// 强类型模型：与 .NET 侧 ImageGalleryModel 对应。items 是 typed repeated（前端 ImageItemModel[]），
// 每个元素携带 byte[] 图片字节（后端发送图片）；上传/删除/刷新走命令在 .NET 侧改磁盘并差量推送列表。
// window.__model 暴露给宿主（自动化测试 / 调试）读取页面模型状态。
const model = bindImageGalleryModel()
;(window as any).__model = model

// blob URL 缓存：同一个条目只创建一次（WeakMap 键 = 条目对象，条目被移除后自动可回收）。
// 不把 url 存进条目对象——深 watch 会把多余的字段序列化回 .NET。
const blobCache = new WeakMap<ImageItemModel, string>()

function mimeOf(name: string): string {
  const ext = name.slice(name.lastIndexOf('.')).toLowerCase()
  switch (ext) {
    case '.png': return 'image/png'
    case '.jpg':
    case '.jpeg': return 'image/jpeg'
    case '.gif': return 'image/gif'
    case '.webp': return 'image/webp'
    case '.bmp': return 'image/bmp'
    case '.svg': return 'image/svg+xml'
    default: return 'image/*'
  }
}

/** 条目字节 → blob URL（后端发来的 byte[] 在浏览器里直接渲染）。 */
function itemUrl(item: ImageItemModel): string {
  let url = blobCache.get(item)
  if (url === undefined) {
    const bytes = item.data
    url = bytes && bytes.length > 0
      ? URL.createObjectURL(new Blob([new Uint8Array(bytes)], { type: mimeOf(item.name) }))
      : ''
    blobCache.set(item, url)
  }
  return url
}

function fmtSize(bytes: number): string {
  if (bytes >= 1024 * 1024) return (bytes / 1024 / 1024).toFixed(1) + ' MB'
  if (bytes >= 1024) return Math.round(bytes / 1024) + ' KB'
  return bytes + ' B'
}

// ---- 上传，两种模式 ----
// 字节上传：<input type="file"> 打开 WebView 文件选择 → 前端把文件读成 byte[] 回传（后端写盘）。
//   源文件地址：WebView2 的 File 带非标准 path 属性，可取到本地完整路径；其余平台为 undefined → 空串。
// 路径上传：直接调后端命令 → .NET 侧弹【系统原生文件选择器】→ 后端自读源文件拷入存储目录。
const fileInput = ref<HTMLInputElement>()
function pickBytes() {
  fileInput.value?.click()
}
async function onFileChange(e: Event) {
  const input = e.target as HTMLInputElement
  const file = input.files?.[0]
  if (!file) return
  const buf = await file.arrayBuffer()
  const srcPath = (file as any).path ?? ''
  model.uploadBytes({ name: file.name, data: new Uint8Array(buf), path: srcPath })
  input.value = '' // 允许再次选择同一文件
}
function uploadByPath() {
  model.pickFile()
}

// ---- 大图查看：点击缩略图打开 lightbox（数据已在列表里，纯前端展示）----
const viewing = ref<ImageItemModel | null>(null)
const previewUrl = computed(() => (viewing.value ? itemUrl(viewing.value) : ''))

function remove(item: ImageItemModel) {
  model.remove(model.items.indexOf(item))
  if (viewing.value === item) viewing.value = null
}
function refresh() {
  model.refresh()
}
</script>

<template>
  <div class="shell">
    <header class="header">
      <img class="logo" src="/logo.svg" alt="logo" />
      <div>
        <h1>图片画廊</h1>
        <p class="subtitle">窗口路径 <code>main</code> · byte[] 图片下发 + 双模式上传（字节 / 路径·系统原生选择）+ 列表查看</p>
      </div>
    </header>

    <main>
      <section class="card">
        <div class="toolbar">
          <div>
            <button class="btn" @click="pickBytes">字节上传</button>
            <button class="btn ghost" @click="uploadByPath">路径上传</button>
            <button class="btn ghost" @click="refresh">重新扫描</button>
          </div>
          <input
            ref="fileInput"
            class="hidden-file"
            type="file"
            accept="image/*"
            @change="onFileChange"
          />
        </div>
        <p class="status">
          {{ model.status }}
          <span class="store">{{ model.storeDir }}</span>
        </p>
      </section>

      <section class="card" v-if="model.items.length">
        <div class="grid">
          <figure v-for="(item, i) in model.items" :key="item.name + i" class="thumb">
            <img class="thumb-img" :src="itemUrl(item)" :alt="item.name" @click="viewing = item" />
            <figcaption class="meta">
              <span class="name" :title="item.name">{{ item.name }}</span>
              <span class="path" :title="item.path">{{ item.path }}</span>
              <span class="dim">{{ fmtSize(item.size) }} · {{ item.modified }}</span>
            </figcaption>
            <button class="del" title="删除" @click="remove(item)">✕</button>
          </figure>
        </div>
      </section>
      <section class="card empty-card" v-else>
        <p class="empty">目录里还没有图片，点「上传图片」选一张本地图片吧。</p>
      </section>
    </main>

    <!-- 大图 lightbox -->
    <div v-if="viewing" class="lightbox" @click.self="viewing = null">
      <img class="lightbox-img" :src="previewUrl" :alt="viewing?.name" />
      <button class="lightbox-close" @click="viewing = null">✕</button>
      <p class="lightbox-name">{{ viewing?.name }}</p>
      <p class="lightbox-path">{{ viewing?.path }}</p>
    </div>
  </div>
</template>
