<script setup lang="ts">
import { computed } from 'vue'
import { bindNestedParentModel } from '../../models/NestedParentModel'

// 模型嵌套窗口：NestedParentModel.Detail 是另一个 WebWindowModel 实例（单 POCO 属性）。
// 单 POCO 属性在 descriptor 里是 ModelValue 兜底 → .NET 以序数键（proto 字段号）下发，
// detail 是 { "1": name, "2": level }。这里按 NestedDetailModel 的字段号声明序翻译回命名键展示。
const model = bindNestedParentModel()
;(window as any).__model = model

// NestedDetailModel 字段号声明序：name=1、level=2（与 .NET NestedDetailModel.cs 一致）。
const DETAIL_FIELDS: Record<number, string> = { 1: 'name', 2: 'level' }
const detailView = computed<Record<string, unknown>>(() => {
  const d = (model.detail ?? {}) as Record<string, unknown>
  const out: Record<string, unknown> = {}
  for (const [num, name] of Object.entries(DETAIL_FIELDS)) {
    if (d[num] !== undefined) out[name] = d[num]
  }
  return out
})
</script>

<template>
  <div class="shell">
    <header class="header">
      <img class="logo" src="/logo.svg" alt="logo" />
      <div>
        <h1>模型嵌套窗口</h1>
        <p class="subtitle">
          窗口路径 <code>nested</code> · 模型里嵌套模型 + 详情子窗口（master-detail）
        </p>
      </div>
    </header>

    <main>
      <section class="card">
        <h2>父窗口模型（NestedParentModel）</h2>
        <p>
          <code>title</code> 是普通字段（双向绑定）；<code>detail</code> 是另一个
          WebWindowModel 实例（NestedDetailModel）——单 POCO 属性 → ModelValue 兜底、序数键下发，
          这里按字段号翻译回命名键只读展示。「打开嵌套详情窗口」打开绑定<strong>同一个</strong>
          Detail 实例的子窗口（master-detail），子窗口强类型编辑后父窗口展示实时跟随。
        </p>
        <div class="row">
          <span>title（双向绑定）</span>
          <input class="input" v-model="model.title" placeholder="窗口标题" />
        </div>
        <div class="row">
          <span>detail.name（嵌套模型字段）</span>
          <code class="code">{{ detailView.name ?? '(未设置)' }}</code>
        </div>
        <div class="row">
          <span>detail.level（嵌套模型字段）</span>
          <code class="code">{{ detailView.level ?? 0 }}</code>
        </div>
        <div class="row">
          <span>detail 原始序数键对象</span>
          <code class="code">{{ JSON.stringify(model.detail) }}</code>
        </div>
        <div class="row">
          <button class="btn primary" @click="model.openDetail()">打开嵌套详情窗口</button>
          <span class="muted">同一 NestedDetailModel 实例 → 子窗口修改会同步回父窗口</span>
        </div>
      </section>
    </main>
  </div>
</template>
