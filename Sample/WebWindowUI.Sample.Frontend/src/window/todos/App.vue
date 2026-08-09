<script setup lang="ts">
import { bindTodoListModel } from '../../models/TodoListModel'
import { TodoItemModel } from '../../models/items/TodoItemModel'

// 强类型模型：与 .NET 侧 TodoListModel 对应。本窗口只演示「List<Model> 一一对应」：
// todos 是 .NET 端 List<TodoItemModel>，前端强类型为 TodoItemModel[] 逐元素 v-for，
// 勾选 / 改名 / 增删即整列表回写 .NET；.NET 计时器每 8 秒追加「自动任务」。
// bindTodoListModel 由生成器在 TodoListModel.ts 里产出：创建实例 + 传 descriptor 给 webwindowui-bridge。
// window.__model 暴露给宿主（自动化测试 / 调试）读取页面模型状态。
const model = bindTodoListModel()
;(window as any).__model = model

function addTodo() {
  const t = new TodoItemModel()
  t.title = `新任务 ${model.todos.length + 1}`
  model.todos.push(t)
}
</script>

<template>
  <div class="shell">
    <header class="header">
      <img class="logo" src="/logo.svg" alt="logo" />
      <div>
        <h1>待办列表</h1>
        <p class="subtitle">
          窗口路径 <code>todos</code> · 演示「List&lt;Model&gt; 一一对应」
          <span v-if="model.todos.length" class="badge">{{ model.todos.length }} 项任务</span>
        </p>
      </div>
    </header>

    <main>
      <section class="card">
        <h2>Todos（List&lt;Model&gt; 双向绑定）</h2>
        <p>
          <code>todos</code> 是 .NET 端 <code>List&lt;TodoItemModel&gt;</code>，前端强类型为
          <code>TodoItemModel[]</code> 逐元素一一对应：勾选 / 改名 / 增删即整列表回写 .NET；
          .NET 计时器每 8 秒追加「自动任务」（整体替换列表属性触发推送）。
        </p>
        <div v-for="(todo, i) in model.todos" :key="i" class="todo-row">
          <input class="todo-check" type="checkbox" v-model="todo.done" :title="todo.done ? '完成' : '未完成'" />
          <input class="input" v-model="todo.title" placeholder="任务标题" />
          <button class="btn" @click="model.todos.splice(i, 1)">删除</button>
        </div>
        <div class="row">
          <button class="btn primary" @click="addTodo">添加任务</button>
          <span v-if="!model.todos.length" class="muted">（空列表，点「添加任务」开始）</span>
        </div>
      </section>
    </main>
  </div>
</template>
