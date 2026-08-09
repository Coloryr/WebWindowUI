<script setup lang="ts">
import { bindNestedDetailModel } from '../../models/NestedDetailModel'

// 嵌套详情子窗口：绑定父窗口传进来的同一个 NestedDetailModel 实例（本窗口的根模型）。
// name/level 是强类型字段，双向编辑 → 经 ModelSet 写回 .NET 同一个实例 → 父窗口的
// NestedParentModel 重推整个 detail，父窗口展示实时更新。
const model = bindNestedDetailModel()
;(window as any).__model = model
</script>

<template>
  <div class="shell">
    <header class="header">
      <img class="logo" src="/logo.svg" alt="logo" />
      <div>
        <h1>嵌套详情</h1>
        <p class="subtitle">
          窗口路径 <code>nested-detail</code> · 同一 NestedDetailModel 实例的强类型编辑
        </p>
      </div>
    </header>

    <main>
      <section class="card">
        <h2>NestedDetailModel（嵌套详情子窗口根模型）</h2>
        <p>
          本窗口由父窗口「模型嵌套窗口」打开，绑定的是父窗口 <code>NestedParentModel.Detail</code>
          的<strong>同一个实例</strong>（master-detail）。这里改 <code>name</code>/<code>level</code>
          → 写回 .NET 实例 → 父窗口展示实时跟随。
        </p>
        <div class="row">
          <span>name</span>
          <input class="input" v-model="model.name" placeholder="名称" />
        </div>
        <div class="row">
          <span>level</span>
          <input class="input" type="number" v-model.number="model.level" placeholder="层级" />
        </div>
      </section>
    </main>
  </div>
</template>
