<script setup lang="ts">
import { ref, onMounted } from 'vue'

// 本窗口只演示「自定义 scheme：资源与数据通道」这一个功能，不绑定模型。
//   app://     —— UI 静态资源（wwwroot，WebResourceResolver 提供），fetch 任意文件；
//   appbin://  —— 与 UI 资源分开的专用数据通道，承载大块/二进制数据（DataProvider 提供）。

const dataText = ref('')
const isLoading = ref(false)

const blobSize = ref<number | null>(null)
const blobLoading = ref(false)
const binText = ref('')

async function loadData() {
  isLoading.value = true
  try {
    const res = await fetch('app://localhost/data.json')
    dataText.value = await res.text()
  } catch (err) {
    dataText.value = `加载失败：${err}`
  } finally {
    isLoading.value = false
  }
}

async function loadBlob() {
  blobLoading.value = true
  try {
    const res = await fetch('appbin://localhost/bin/blob.bin')
    const buf = await res.arrayBuffer()
    blobSize.value = buf.byteLength
    const txt = await (await fetch('appbin://localhost/bin/hello.txt')).text()
    binText.value = txt
  } catch (err) {
    blobSize.value = null
    binText.value = `加载失败：${err}`
  } finally {
    blobLoading.value = false
  }
}

onMounted(() => {
  loadData()
  loadBlob()
})
</script>

<template>
  <div class="shell">
    <header class="header">
      <img class="logo" src="/logo.svg" alt="logo" />
      <div>
        <h1>资源与数据通道</h1>
        <p class="subtitle">
          窗口路径 <code>resources</code> · 演示「资源（app://）与数据通道（appbin://）」
        </p>
      </div>
    </header>

    <main>
      <section class="card">
        <h2>资源请求（app://）</h2>
        <p>
          UI 静态资源走 <code>app://localhost</code>（wwwroot，WebResourceResolver）。
          下方从 <code>app://localhost/data.json</code> 读取一个 JSON 文件。
        </p>
        <button class="btn" :disabled="isLoading" @click="loadData">
          {{ isLoading ? '请求中…' : 'fetch 读取 app://localhost/data.json' }}
        </button>
        <pre v-if="dataText" class="code">{{ dataText }}</pre>
      </section>

      <section class="card">
        <h2>数据通道（appbin://）</h2>
        <p>
          与 UI 资源分开的专用 scheme，用于大块/二进制数据。下方从
          <code>appbin://localhost/bin/blob.bin</code> 取 2 MB 字节流。
        </p>
        <button class="btn" :disabled="blobLoading" @click="loadBlob">
          {{ blobLoading ? '请求中…' : 'fetch 读取 appbin:// 二进制数据' }}
        </button>
        <div v-if="blobSize !== null" class="row">
          <span>blob.bin 字节数</span>
          <strong>{{ blobSize.toLocaleString() }} bytes</strong>
        </div>
        <pre v-if="binText" class="code">{{ binText }}</pre>
      </section>
    </main>
  </div>
</template>
