<script setup lang="ts">
import { computed } from 'vue'
import { bindAboutModel } from '../../models/AboutModel'

// 强类型模型：与 .NET 侧 AboutModel 对应（静态信息，双向绑定已打通）。
// bindAboutModel 由生成器在 AboutModel.ts 里产出：创建实例 + 传 descriptor 给 webwindowui-bridge。
const model = bindAboutModel()
;(window as any).__model = model

// byte[] → bytes → Uint8Array，展示成 hex
const iconHashHex = computed(() =>
  Array.from(model.iconHash).map((b) => b.toString(16).padStart(2, '0')).join(' '),
)
</script>

<template>
  <div class="shell">
    <header class="header">
      <img class="logo" src="/logo.svg" alt="logo" />
      <div>
        <h1>关于 WebWindowUI</h1>
        <p class="subtitle">窗口路径 <code>about</code> · 对应 <code>src/window/about/</code></p>
      </div>
    </header>

    <main>
      <section class="card">
        <h2>关于信息（AboutModel 双向绑定）</h2>
        <dl class="info">
          <dt>应用名（string）</dt>
          <dd>{{ model.appName }}</dd>
          <dt>版本（string）</dt>
          <dd>{{ model.version }}</dd>
          <dt>构建日期（DateTime → string）</dt>
          <dd>{{ model.buildDate }}</dd>
          <dt>仓库地址（string）</dt>
          <dd>{{ model.repoUrl }}</dd>
          <dt>贡献者（List&lt;string&gt; → repeated string）</dt>
          <dd>{{ model.contributors.join('、') }}</dd>
          <dt>功能特性（string[] → repeated string）</dt>
          <dd>{{ model.features.join('、') }}</dd>
          <dt>图标哈希（byte[] → bytes）</dt>
          <dd><code>{{ iconHashHex }}</code></dd>
          <dt>元数据（object → ModelValue）</dt>
          <dd><pre class="code">{{ JSON.stringify(model.metadata, null, 2) }}</pre></dd>
        </dl>
      </section>
    </main>
  </div>
</template>
