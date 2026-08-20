<script setup lang="ts">
// 设置 tab：SettingsModel 多类型字段 + 跨线程推送（lastBackup 每 3 秒）。
const props = defineProps<{ model: any }>()
</script>

<template>
  <section class="card">
    <h2>Model 双向绑定（SettingsModel，多类型）</h2>
    <div class="row">
      <span>主题（string）</span>
      <select v-model="props.model.theme" class="select">
        <option value="light">浅色</option>
        <option value="dark">深色</option>
      </select>
    </div>
    <label class="row">
      <span>自动保存（bool）</span>
      <input v-model="props.model.autoSave" type="checkbox" />
    </label>
    <div class="row">
      <span>每页最大条目数（int）</span>
      <input v-model.number="props.model.maxItems" type="number" class="input" />
    </div>
    <div class="row">
      <span>同步进度（double）</span>
      <strong>{{ (props.model.progress * 100).toFixed(0) }}%</strong>
    </div>
    <div class="row">
      <span>已同步字节数（long/int64）</span>
      <strong>{{ props.model.totalBytes.toLocaleString() }} bytes</strong>
    </div>
    <div class="row">
      <span>实例标识（Guid → string）</span>
      <code>{{ props.model.instanceId }}</code>
    </div>
    <div class="row">
      <span>上次备份（DateTime，.NET 每 3 秒推送）</span>
      <code>{{ props.model.lastBackup }}</code>
    </div>
    <div class="row">
      <span>保留历史（TimeSpan → string）</span>
      <code>{{ props.model.keepHistory }}</code>
    </div>
    <div class="row">
      <span>同步模式（枚举 → number）</span>
      <select v-model="props.model.syncMode" class="select">
        <option :value="0">自动</option>
        <option :value="1">手动</option>
      </select>
    </div>
    <div class="row">
      <span>标签（List&lt;string&gt; → repeated string）</span>
      <code>{{ props.model.tags.join('、') }}</code>
    </div>
    <div class="row">
      <span>扩展配置（object → ModelValue）</span>
      <pre class="code">{{ JSON.stringify(props.model.config, null, 2) }}</pre>
    </div>
  </section>
</template>
