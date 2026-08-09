<script setup lang="ts">
import { computed } from 'vue'
import { bindMainWindowModel } from '../../models/MainWindowModel'

// 强类型模型：与 .NET 侧 MainWindowModel 对应，双向绑定全部走它。
// bindMainWindowModel 由生成器在 MainWindowModel.ts 里产出：创建实例 + 传 descriptor 给 webwindowui-bridge。
// window.__model 暴露给宿主（自动化测试 / 调试）读取页面模型状态。
// 本窗口只演示「模型双向绑定」：name 输入即回写，count/message 由 .NET 定时器推送。
const model = bindMainWindowModel()
;(window as any).__model = model

const extraText = computed(() => JSON.stringify(model.extra, null, 2))
</script>

<template>
  <div class="shell">
    <header class="header">
      <img class="logo" src="/logo.svg" alt="logo" />
      <div>
        <h1>主窗口</h1>
        <p class="subtitle">
          窗口路径 <code>main</code> · 演示「模型双向绑定」
        </p>
      </div>
    </header>

    <main>
      <section class="card">
        <h2>Model 双向绑定（TypeScript）</h2>
        <p>
          <code>name</code> 输入框与 .NET 端 <code>MainWindowModel</code> 双向绑定（输入即回写）；
          <code>count</code> 由 .NET 定时器每秒推送、<code>message</code> 每 5 秒改写。数据经 WebView2
          <code>postMessage</code> 以 protobuf 双向流动，前端为强类型模型类。
        </p>
        <label class="row">
          <span>Name（双向绑定）</span>
          <input class="input" v-model="model.name" placeholder="输入后回写给 .NET" />
        </label>
        <div class="row">
          <span>Count（.NET 推送）</span>
          <strong>{{ model.count }}</strong>
        </div>
        <div class="row">
          <span>Message（.NET 修改）</span>
          <code>{{ model.message }}</code>
        </div>
        <div class="row">
          <span>Extra（object 属性）</span>
          <pre class="code">{{ extraText }}</pre>
        </div>
      </section>
    </main>
  </div>
</template>
