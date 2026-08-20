<script setup lang="ts">
// 列表嵌套 tab：Items 是 List<NestedListItemModel>（typed repeated），元素内嵌套 tags / meta。
// 列表只读展示，点「打开详情窗口」开绑定同一元素实例的子窗口编辑，改动经父模型重推实时跟随。
const props = defineProps<{ model: any }>()

// NestedItemTagModel 字段号声明序：name=1。
function tagName(tag: unknown): string {
  const t = (tag ?? {}) as Record<string, unknown>
  return (t.name as string) ?? (t['1'] as string) ?? '(未命名)'
}

// NestedItemMetaModel 字段号声明序：author=1、note=2。
const META_FIELDS: Record<number, string> = { 1: 'author', 2: 'note' }
function metaView(meta: unknown): Record<string, unknown> {
  const m = (meta ?? {}) as Record<string, unknown>
  const out: Record<string, unknown> = {}
  for (const [num, name] of Object.entries(META_FIELDS)) {
    if (m[num] !== undefined) out[name] = m[num]
  }
  return out
}
</script>

<template>
  <section class="card">
    <h2>Items（List&lt;NestedListItemModel&gt;）</h2>
    <p>
      <code>items</code> 是 typed repeated；每个元素又嵌套 <code>tags</code>（List&lt;NestedItemTagModel&gt;）
      与 <code>meta</code>（NestedItemMetaModel）。列表只读展示，点「打开详情窗口」打开绑定
      <strong>同一元素实例</strong>的子窗口编辑；子窗口改动经父模型重推，这里实时跟随。
    </p>
    <div v-for="(item, i) in props.model.items" :key="i" class="item-card">
      <div class="item-head">
        <strong>{{ item.title || '(未命名)' }}</strong>
        <span class="badge">优先级 {{ item.priority }}</span>
        <span class="badge" :class="{ done: item.done }">{{ item.done ? '完成' : '未完成' }}</span>
      </div>
      <div class="item-sub muted">
        tags：
        <span v-if="item.tags.length" class="tags">
          <span v-for="(tag, j) in item.tags" :key="j" class="tag">{{ tagName(tag) }}</span>
        </span>
        <span v-else>（无标签）</span>
      </div>
      <div class="item-sub muted" v-if="metaView(item.meta).author">
        作者 {{ metaView(item.meta).author }} · 备注 {{ metaView(item.meta).note || '—' }}
      </div>
      <div class="item-actions">
        <button class="btn" @click="props.model.openItem(i)">打开详情窗口</button>
      </div>
    </div>
  </section>

  <section class="card">
    <h2>Counts（ObservableDictionary&lt;string, int&gt;）</h2>
    <p>
      字典属性：<strong>.NET 侧原地改</strong>（<code>dict[k] = v</code> / Add / Remove）经 CollectionChanged
      自动整属性重推前端；<strong>前端原地改</strong>经深 watch 整字典回写 .NET。下方两个按钮分别演示两个方向。
    </p>
    <div class="counts">
      <div v-for="(v, k) in props.model.counts" :key="k" class="count-row">
        <code>{{ k }}</code>
        <span class="badge">{{ v }}</span>
      </div>
    </div>
    <div class="item-actions">
      <button class="btn" @click="props.model.bump('items')">
        .NET 侧 Bump items（命令 → Counts["items"]++）
      </button>
      <button
        class="btn"
        @click="props.model.counts['extra'] = ((props.model.counts['extra'] as number) ?? 0) + 1"
      >
        前端侧 extra++（深 watch 回写 .NET）
      </button>
    </div>
  </section>
</template>
