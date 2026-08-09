<script setup lang="ts">
import { computed, ref } from 'vue'
import { bindTodoListModel } from '../../models/TodoListModel'
import type { TodoItemModel } from '../../models/TodoItemModel'

// 强类型模型：与 .NET 侧 TodoListModel 对应。items 是 typed repeated（前端 TodoItemModel[]），
// 勾选/增删整列表回写 .NET；增删改经命令（addTitle/toggle/remove/clearCompleted）触发 .NET 持久化。
// window.__model 暴露给宿主（自动化测试 / 调试）读取页面模型状态。
const model = bindTodoListModel()
;(window as any).__model = model

type Filter = 'all' | 'active' | 'done'
const filter = ref<Filter>('all')

const total = computed(() => model.items.length)
const active = computed(() => model.items.filter(i => !i.done).length)
const done = computed(() => total.value - active.value)

const filtered = computed(() => {
  if (filter.value === 'active') return model.items.filter(i => !i.done)
  if (filter.value === 'done') return model.items.filter(i => i.done)
  return model.items
})

const setFilter = (f: Filter) => (filter.value = f)

function add() {
  const t = model.newTitle.trim()
  if (!t) return
  model.addTitle(t) // 命令：.NET 加入列表 + 保存；NewTitle 由命令内清空并推回
  model.newTitle = ''
}
function toggle(item: TodoItemModel) {
  model.toggle(model.items.indexOf(item))
}
function remove(item: TodoItemModel) {
  model.remove(model.items.indexOf(item))
}
function clearDone() {
  model.clearCompleted()
}
</script>

<template>
  <div class="shell">
    <header class="header">
      <img class="logo" src="/logo.svg" alt="logo" />
      <div>
        <h1>待办事项</h1>
        <p class="subtitle">窗口路径 <code>main</code> · List&lt;Model&gt; 双向绑定 + 命令 + 持久化</p>
      </div>
    </header>

    <main>
      <section class="card">
        <h2>新增任务</h2>
        <div class="add-row">
          <input class="input" v-model="model.newTitle" placeholder="输入任务标题，回车或点添加" @keyup.enter="add" />
          <button class="btn" @click="add">添加</button>
        </div>
        <p class="status">{{ model.status }}</p>
      </section>

      <section class="card">
        <div class="toolbar">
          <div class="tabs">
            <button :class="['tab', filter === 'all' && 'tab-on']" @click="setFilter('all')">全部 {{ total }}</button>
            <button :class="['tab', filter === 'active' && 'tab-on']" @click="setFilter('active')">未完成 {{ active }}</button>
            <button :class="['tab', filter === 'done' && 'tab-on']" @click="setFilter('done')">已完成 {{ done }}</button>
          </div>
          <button class="btn ghost" :disabled="done === 0" @click="clearDone">清除已完成</button>
        </div>

        <ul class="list" v-if="filtered.length">
          <li v-for="item in filtered" :key="item.title + item.createdAt" class="todo" :class="{ 'todo-done': item.done }">
            <label class="check">
              <input type="checkbox" :checked="item.done" @change="toggle(item)" />
            </label>
            <span class="prio" :class="'prio-' + item.priority">{{ item.priority }}</span>
            <span class="title">{{ item.title }}</span>
            <span class="created">{{ item.createdAt }}</span>
            <button class="btn ghost small" @click="remove(item)">删除</button>
          </li>
        </ul>
        <p class="empty" v-else>{{ filter === 'all' ? '还没有任务，添加一个吧' : '没有匹配的任务' }}</p>
      </section>
    </main>
  </div>
</template>
