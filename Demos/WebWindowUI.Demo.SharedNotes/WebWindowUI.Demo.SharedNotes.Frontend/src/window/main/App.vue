<script setup lang="ts">
import { computed } from 'vue'
import { bindNotesModel } from '../../models/NotesModel'
import type { NoteModel } from '../../models/NoteModel'

// 与 .NET 侧 NotesModel 双向绑定。本窗口与「监看」窗口绑定同一实例：
// 这里发送/删除经命令走 .NET，广播到所有订阅者；监看窗口实时跟随。
// window.__model 暴露给宿主（自动化测试 / 调试）读取页面模型状态。
const model = bindNotesModel()
;(window as any).__model = model

const list = computed(() => model.notes) // typed repeated：前端 TodoItemModel[] 实时数组

function send() {
  model.send()
}
function remove(n: NoteModel) {
  model.remove(model.notes.indexOf(n))
}
</script>

<template>
  <div class="shell">
    <header class="header">
      <img class="logo" src="/logo.svg" alt="logo" />
      <div>
        <h1>共享便签 · 编辑</h1>
        <p class="subtitle">
          窗口路径 <code>main</code> · 与「监看」窗口共享同一个 NotesModel 实例 —— 发送/删除全广播、实时同步
        </p>
      </div>
    </header>

    <main>
      <section class="card">
        <h2>写一张便签</h2>
        <div class="add-row">
          <input class="input" v-model="model.input" placeholder="输入内容，回车或点发送" @keyup.enter="send" />
          <button class="btn" @click="send">发送</button>
        </div>
        <p class="status">{{ model.status }}</p>
      </section>

      <section class="card">
        <div class="toolbar">
          <h2>全部便签（{{ model.total }}）</h2>
          <span class="hint">shared NotesModel · multi-subscriber broadcast</span>
        </div>
        <ul class="list" v-if="list.length">
          <li v-for="(n, i) in list" :key="i" class="note">
            <div class="note-head">
              <span class="author">{{ n.author }}</span>
              <span class="time">{{ n.time }}</span>
            </div>
            <p class="text">{{ n.text }}</p>
            <button class="btn ghost small" @click="remove(n)">删除</button>
          </li>
        </ul>
        <p class="empty" v-else>还没有便签 —— 输入并发送，所有窗口会实时出现</p>
      </section>
    </main>
  </div>
</template>
