<script setup lang="ts">
import { bindNotesModel } from '../../models/NotesModel'

// 只读监看窗：绑定与「编辑」窗口同一 NotesModel 实例，改动全广播实时跟随。
// window.__model 暴露给宿主（自动化测试 / 调试）读取页面模型状态。
const model = bindNotesModel()
;(window as any).__model = model
</script>

<template>
  <div class="wall">
    <header class="head">
      <h1>共享便签 · 监看</h1>
      <span class="count">{{ model.total }} 条</span>
    </header>
    <p class="status">{{ model.status }}</p>

    <div class="grid" v-if="model.notes.length">
      <div v-for="(n, i) in model.notes" :key="i" class="card">
        <div class="meta">
          <span class="author">{{ n.author }}</span>
          <span class="time">{{ n.time }}</span>
        </div>
        <p class="text">{{ n.text }}</p>
      </div>
    </div>
    <p class="empty" v-else>等待便签……（在「编辑」窗口输入并发送）</p>
  </div>
</template>
