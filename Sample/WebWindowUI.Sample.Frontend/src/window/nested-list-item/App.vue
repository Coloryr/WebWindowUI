<script setup lang="ts">
import { bindNestedListItemModel } from '../../models/items/NestedListItemModel'
import { NestedItemTagModel } from '../../models/items/NestedItemTagModel'

// 列表项详情子窗口：绑定父列表的同一个 NestedListItemModel 元素实例（本窗口的根模型）。
// title/done/priority 强类型双向编辑；tags 在子窗口是根层 typed repeated → 增删改
// （整列回写 + 元素序数键）全部双向；meta 是单 POCO → 序数键只读展示（翻译回命名键）。
const model = bindNestedListItemModel()
;(window as any).__model = model

function addTag() {
  const t = new NestedItemTagModel()
  t.name = `新标签 ${model.tags.length + 1}`
  model.tags.push(t)
}

// NestedItemMetaModel 字段号声明序：author=1、note=2。
const META_FIELDS: Record<number, string> = { 1: 'author', 2: 'note' }
function metaView(): Record<string, unknown> {
  const m = (model.meta ?? {}) as Record<string, unknown>
  const out: Record<string, unknown> = {}
  for (const [num, name] of Object.entries(META_FIELDS)) {
    if (m[num] !== undefined) out[name] = m[num]
  }
  return out
}
</script>

<template>
  <div class="shell">
    <header class="header">
      <img class="logo" src="/logo.svg" alt="logo" />
      <div>
        <h1>列表项详情</h1>
        <p class="subtitle">
          窗口路径 <code>nested-list-item</code> · 同一 NestedListItemModel 元素实例的强类型编辑
        </p>
      </div>
    </header>

    <main>
      <section class="card">
        <h2>NestedListItemModel（列表项详情子窗口根模型）</h2>
        <p>
          由父窗口「List&lt;&gt;嵌套窗口」打开，绑定父列表里<strong>同一个元素实例</strong>
          （master-detail）。元素字段与内层 <code>tags</code> 强类型双向编辑 → 写回 .NET 实例
          → 父窗口列表实时跟随。
        </p>
        <div class="row">
          <span>title</span>
          <input class="input" v-model="model.title" placeholder="标题" />
        </div>
        <div class="row">
          <span>done</span>
          <label class="toggle">
            <input type="checkbox" v-model="model.done" /> {{ model.done ? '完成' : '未完成' }}
          </label>
        </div>
        <div class="row">
          <span>priority</span>
          <input class="input" type="number" v-model.number="model.priority" placeholder="优先级" />
        </div>
      </section>

      <section class="card">
        <h2>tags（元素内层 List&lt;NestedItemTagModel&gt;）</h2>
        <p>
          在子窗口根层是 typed repeated → 增删改全部双向。同一 List&lt;Model&gt; 在父窗口里是
          元素内层嵌套、在子窗口里是根层，都强类型一一对应。
        </p>
        <div v-for="(tag, i) in model.tags" :key="i" class="row">
          <input class="input" v-model="tag.name" placeholder="标签名" />
          <button class="btn" @click="model.tags.splice(i, 1)">删除</button>
        </div>
        <div class="row">
          <button class="btn primary" @click="addTag">添加标签</button>
          <span v-if="!model.tags.length" class="muted">（空标签列表，点「添加标签」开始）</span>
        </div>
      </section>

      <section class="card">
        <h2>meta（元素内层单模型，只读）</h2>
        <p><code>meta</code> 是单 POCO 属性 → ModelValue 兜底 / 序数键，这里翻译回命名键只读展示。</p>
        <div class="row"><span>作者</span><code class="code">{{ metaView().author ?? '—' }}</code></div>
        <div class="row"><span>备注</span><code class="code">{{ metaView().note ?? '—' }}</code></div>
      </section>
    </main>
  </div>
</template>
