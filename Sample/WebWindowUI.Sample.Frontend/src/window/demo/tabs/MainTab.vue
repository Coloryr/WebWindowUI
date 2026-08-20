<script setup lang="ts">
import { computed } from 'vue'

// 模型双向绑定 tab：props.model 是 App.vue 经 bindMainWindowModel 绑定的响应式模型。
// name 输入即回写，count/message 由 .NET 定时器推送。
const props = defineProps<{ model: any }>()
const extraText = computed(() => JSON.stringify(props.model.extra, null, 2))
</script>

<template>
  <section class="card">
    <h2>Model 双向绑定（TypeScript）</h2>
    <p>
      <code>name</code> 输入框与 .NET 端 <code>MainWindowModel</code> 双向绑定（输入即回写）；
      <code>count</code> 由 .NET 定时器每秒推送、<code>message</code> 每 5 秒改写。数据经
      <code>postMessage</code> 以 protobuf 双向流动，前端为强类型模型类。
    </p>
    <label class="row">
      <span>Name（双向绑定）</span>
      <input class="input" v-model="props.model.name" placeholder="输入后回写给 .NET" />
    </label>
    <div class="row">
      <span>Count（.NET 推送）</span>
      <strong>{{ props.model.count }}</strong>
    </div>
    <div class="row">
      <span>Message（.NET 修改）</span>
      <code>{{ props.model.message }}</code>
    </div>
    <div class="row">
      <span>Extra（object 属性）</span>
      <pre class="code">{{ extraText }}</pre>
    </div>
  </section>
</template>
