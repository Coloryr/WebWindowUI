<script setup lang="ts">
import { ref, onMounted } from 'vue'

// 资源与数据通道 tab：不绑模型。app:// 静态资源（wwwroot）+ appdata:// 用户自定义路由。
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
    const res = await fetch('appdata://bin/blob.bin')
    const buf = await res.arrayBuffer()
    blobSize.value = buf.byteLength
    const txt = await (await fetch('appdata://bin/hello.txt')).text()
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
    <h2>数据通道（appdata://）</h2>
    <p>
      与 UI 资源分开的专用 scheme，用于用户自定义路由。下方从
      <code>appdata://bin/blob.bin</code> 取 2 MB 字节流。
    </p>
    <button class="btn" :disabled="blobLoading" @click="loadBlob">
      {{ blobLoading ? '请求中…' : 'fetch 读取 appdata:// 二进制数据' }}
    </button>
    <div v-if="blobSize !== null" class="row">
      <span>blob.bin 字节数</span>
      <strong>{{ blobSize.toLocaleString() }} bytes</strong>
    </div>
    <pre v-if="binText" class="code">{{ binText }}</pre>
  </section>
</template>
