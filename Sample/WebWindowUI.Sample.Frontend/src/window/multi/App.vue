<script setup lang="ts">
import { bindMultiWindowModel } from '../../models/MultiWindowModel'

// 强类型模型：与 .NET 侧 MultiWindowModel 对应。本窗口只演示「一个 model 给多个窗口用」：
//   共享实例 —— 两个窗口（共享A / 共享B）绑同一个 MultiWindowModel 实例：count 由 .NET 每秒推送，
//              任一侧改 name 经广播同步到另一侧（跨窗口联动）；
//   独立实例 —— 独立窗口持有自己的 MultiWindowModel：count/name 各走各，与共享对互不干扰。
// 窗口用 InstanceId（只读标签）区分：共享对两个窗口显示同一实例 id，独立窗口显示另一个。
// bindMultiWindowModel 由生成器在 MultiWindowModel.ts 里产出：创建实例 + 传 descriptor 给 webwindowui-bridge。
// window.__model 暴露给宿主（自动化测试 / 调试）读取页面模型状态。
const model = bindMultiWindowModel()
;(window as any).__model = model
</script>

<template>
  <div class="shell">
    <header class="header">
      <img class="logo" src="/logo.svg" alt="logo" />
      <div>
        <h1>多窗口模型</h1>
        <p class="subtitle">
          窗口路径 <code>multi</code> · 实例 <code>{{ model.instanceId }}</code> · 演示「多窗口共享 / 独立」
        </p>
      </div>
    </header>

    <main>
      <section class="card">
        <h2>一个 model 给多个窗口用，互不干扰</h2>
        <p>
          本页开了 <strong>3 个窗口</strong>：共享A / 共享B 绑<strong>同一个</strong>
          <code>MultiWindowModel</code> 实例（count 每秒推送、改 name 即跨窗口同步）；
          独立实例窗口用<strong>另一个</strong>实例（count/name 各走各，互不影响）。
          看标题栏 <code>实例 id</code> 即可区分。
        </p>
        <label class="row">
          <span>Name（共享实例下任一窗口改，其余窗口跟随）</span>
          <input class="input" v-model="model.name" placeholder="输入后回写给 .NET 并广播" />
        </label>
        <div class="row">
          <span>Count（.NET 定时器推送）</span>
          <strong>{{ model.count }}</strong>
        </div>
        <div class="row">
          <span>实例标识（只读，区分共享/独立）</span>
          <code>{{ model.instanceId }}</code>
        </div>
      </section>
    </main>
  </div>
</template>
