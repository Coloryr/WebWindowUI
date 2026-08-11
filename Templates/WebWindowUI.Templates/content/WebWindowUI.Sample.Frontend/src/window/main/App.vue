<script setup lang="ts">
import { bindMainModel } from '../../models/MainModel'

// 强类型模型：与 .NET 侧 MainModel 对应，双向绑定全部走它。
// bindMainModel 由生成器在 MainModel.ts 里产出：创建实例 + 传 descriptor 给 webwindowui-bridge。
// window.__model 暴露给宿主（自动化测试 / 调试）读取页面模型状态。
const model = bindMainModel()
;(window as any).__model = model
</script>

<template>
  <div class="shell">
    <header class="header">
      <img class="logo" src="/logo.svg" alt="logo" />
      <div>
        <h1>{{ model.title }}</h1>
        <p class="subtitle">窗口路径 <code>main</code> · WebWindowUI 模板骨架</p>
      </div>
    </header>

    <main>
      <section class="card">
        <h2>模型双向绑定 + MVVM 命令</h2>
        <p>
          <code>title</code> 输入框与 .NET 端 <code>MainModel</code> 双向绑定（输入即回写）；
          <code>count</code> 点按钮经 <code>model.bump()</code> → ModelInvoke → .NET 执行命令 → 推送回来。
        </p>
        <label class="row">
          <span>Title（双向绑定）</span>
          <input class="input" v-model="model.title" placeholder="输入后回写给 .NET" />
        </label>
        <div class="row">
          <span>Count（.NET 命令自增）</span>
          <strong>{{ model.count }}</strong>
        </div>
        <button class="btn" @click="model.bump()">Bump</button>
      </section>
    </main>
  </div>
</template>
