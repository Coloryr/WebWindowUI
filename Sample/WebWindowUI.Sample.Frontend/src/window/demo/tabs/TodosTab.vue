<script setup lang="ts">
import { TodoItemModel } from '../../../models/items/TodoItemModel'

// 待办列表 tab：todos 是 .NET 端 List<TodoItemModel>，前端强类型为 TodoItemModel[] 逐元素 v-for，
// 勾选 / 改名 / 增删即整列表回写 .NET；.NET 计时器每 8 秒追加「自动任务」。
const props = defineProps<{ model: any }>()

function addTodo() {
  const t = new TodoItemModel()
  t.title = `新任务 ${props.model.todos.length + 1}`
  props.model.todos.push(t)
}
</script>

<template>
  <section class="card">
    <h2>
      Todos（List&lt;Model&gt; 双向绑定）
      <span v-if="props.model.todos.length" class="badge">{{ props.model.todos.length }} 项任务</span>
    </h2>
    <p>
      <code>todos</code> 是 .NET 端 <code>List&lt;TodoItemModel&gt;</code>，前端强类型为
      <code>TodoItemModel[]</code> 逐元素一一对应：勾选 / 改名 / 增删即整列表回写 .NET；
      .NET 计时器每 8 秒追加「自动任务」（整体替换列表属性触发推送）。
    </p>
    <div v-for="(todo, i) in props.model.todos" :key="i" class="todo-row">
      <input class="todo-check" type="checkbox" v-model="todo.done" :title="todo.done ? '完成' : '未完成'" />
      <input class="input" v-model="todo.title" placeholder="任务标题" />
      <button class="btn" @click="props.model.todos.splice(i, 1)">删除</button>
    </div>
    <div class="row">
      <button class="btn primary" @click="addTodo">添加任务</button>
      <span v-if="!props.model.todos.length" class="muted">（空列表，点「添加任务」开始）</span>
    </div>
  </section>
</template>
