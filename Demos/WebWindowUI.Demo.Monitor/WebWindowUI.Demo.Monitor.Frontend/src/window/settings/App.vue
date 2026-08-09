<script setup lang="ts">
import { bindMonitorSettingsModel } from '../../models/MonitorSettingsModel'

// 设置窗口绑定 MonitorModel.Settings 同一子实例（master-detail）：它既是主窗口的嵌套属性值、
// 又是本窗口的根模型 → 强类型双向编辑。改 pollIntervalMs 后主窗口收到 Settings 重推 → 重建定时器。
// window.__model 暴露给宿主（自动化测试 / 调试）读取页面模型状态。
const model = bindMonitorSettingsModel()
;(window as any).__model = model
</script>

<template>
  <div class="shell">
    <header class="header">
      <h1>监控设置</h1>
      <p class="subtitle">
        绑定 MonitorModel.Settings 同一实例（master-detail）· 改动全广播、即时生效
      </p>
    </header>

    <main>
      <section class="card">
        <label class="field">
          <span>采样间隔（毫秒）</span>
          <input class="input" type="number" min="200" max="10000" v-model.number="model.pollIntervalMs" />
        </label>
        <p class="muted">主窗口定时器订阅 Settings 变化，改完立即重建 —— 不必重启。</p>
      </section>

      <section class="card">
        <label class="field">
          <span>进程表最多显示条数</span>
          <input class="input" type="number" min="1" max="20" v-model.number="model.maxProcesses" />
        </label>
      </section>

      <section class="card">
        <label class="check">
          <input type="checkbox" v-model="model.showProcesses" />
          <span>显示进程表</span>
        </label>
      </section>

      <section class="card">
        <p class="field-label">主题</p>
        <div class="seg">
          <button :class="['seg-btn', model.theme === 'light' && 'on']" @click="model.theme = 'light'">浅色</button>
          <button :class="['seg-btn', model.theme === 'dark' && 'on']" @click="model.theme = 'dark'">深色</button>
        </div>
      </section>
    </main>
  </div>
</template>
