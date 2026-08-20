<script setup lang="ts">
import { computed } from 'vue'

// 关于 tab：AboutModel 静态信息（双向绑定已打通）。
const props = defineProps<{ model: any }>()

// byte[] → bytes → Uint8Array，展示成 hex
const iconHashHex = computed(() =>
  Array.from(props.model.iconHash).map((b: number) => b.toString(16).padStart(2, '0')).join(' '),
)
</script>

<template>
  <section class="card">
    <h2>关于信息（AboutModel 双向绑定）</h2>
    <dl class="info">
      <dt>应用名（string）</dt>
      <dd>{{ props.model.appName }}</dd>
      <dt>版本（string）</dt>
      <dd>{{ props.model.version }}</dd>
      <dt>构建日期（DateTime → string）</dt>
      <dd>{{ props.model.buildDate }}</dd>
      <dt>仓库地址（string）</dt>
      <dd>{{ props.model.repoUrl }}</dd>
      <dt>贡献者（List&lt;string&gt; → repeated string）</dt>
      <dd>{{ props.model.contributors.join('、') }}</dd>
      <dt>功能特性（string[] → repeated string）</dt>
      <dd>{{ props.model.features.join('、') }}</dd>
      <dt>图标哈希（byte[] → bytes）</dt>
      <dd><code>{{ iconHashHex }}</code></dd>
      <dt>元数据（object → ModelValue）</dt>
      <dd><pre class="code">{{ JSON.stringify(props.model.metadata, null, 2) }}</pre></dd>
    </dl>
  </section>
</template>
