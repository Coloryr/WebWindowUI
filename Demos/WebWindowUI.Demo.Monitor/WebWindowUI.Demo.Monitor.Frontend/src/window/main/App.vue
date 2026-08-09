<script setup lang="ts">
import { computed } from 'vue'
import { bindMonitorModel } from '../../models/MonitorModel'
import type { ProcessModel } from '../../models/ProcessModel'

// 与 .NET 侧 MonitorModel 双向绑定：采样定时器在线程池线程每 1s 推一次（跨线程 marshal）。
// settings 是嵌套子模型 MonitorSettings（ModelValue 下发/序数键）——主窗口只读展示，
// 设置窗口（settings）绑定同一子实例强类型编辑（master-detail）。
// window.__model 暴露给宿主（自动化测试 / 调试）读取页面模型状态。
const model = bindMonitorModel()
;(window as any).__model = model

// MonitorSettings 字段号声明序：PollIntervalMs=1、MaxProcesses=2、ShowProcesses=3、Theme=4
// （与 .NET MonitorSettings.cs 一致，生成器按声明序编号）。这里把序数键翻回命名键展示。
const settingsView = computed<{ pollIntervalMs?: number; maxProcesses?: number; showProcesses?: boolean; theme?: string }>(() => {
  const s = (model.settings ?? {}) as Record<string, unknown>
  return {
    pollIntervalMs: s['1'] as number | undefined,
    maxProcesses: s['2'] as number | undefined,
    showProcesses: s['3'] as boolean | undefined,
    theme: s['4'] as string | undefined,
  }
})

const processes = computed(() =>
  [...model.processes].sort((a, b) => b.cpu - a.cpu),
)

function cpuColor(v: number): string {
  if (v >= 75) return '#dc2626'
  if (v >= 40) return '#f59e0b'
  return '#22c55e'
}
</script>

<template>
  <div class="shell">
    <header class="header">
      <img class="logo" src="/logo.svg" alt="logo" />
      <div>
        <h1>系统监控</h1>
        <p class="subtitle">
          窗口路径 <code>main</code> · 跨线程实时推送 + 嵌套模型设置窗口（master-detail）
        </p>
      </div>
    </header>

    <main>
      <section class="card">
        <h2>实时负载</h2>
        <div class="gauge">
          <span class="gauge-label">CPU {{ Math.round(model.cpuUsage) }}%</span>
          <div class="bar"><div class="fill" :style="{ width: model.cpuUsage + '%', background: cpuColor(model.cpuUsage) }"></div></div>
        </div>
        <div class="gauge">
          <span class="gauge-label">内存 {{ Math.round(model.memoryUsage) }}%</span>
          <div class="bar"><div class="fill" :style="{ width: model.memoryUsage + '%', background: cpuColor(model.memoryUsage) }"></div></div>
        </div>
        <p class="muted">已运行 <code>{{ model.uptime }}</code> · 采样间隔 {{ settingsView.pollIntervalMs ?? '-' }} ms（在设置窗口修改，即时生效）</p>
      </section>

      <section class="card">
        <div class="toolbar">
          <h2>进程</h2>
          <span class="hint">typed repeated · 每轮采样原地清空重建自动推送</span>
        </div>
        <table class="table" v-if="processes.length">
          <thead>
            <tr><th>进程</th><th>PID</th><th>CPU %</th><th>内存 MB</th></tr>
          </thead>
          <tbody>
            <tr v-for="p in processes" :key="p.pid">
              <td>{{ p.name }}</td>
              <td class="mono">{{ p.pid }}</td>
              <td><span class="pill" :style="{ background: cpuColor(p.cpu) }">{{ p.cpu }}</span></td>
              <td class="mono">{{ p.memory }}</td>
            </tr>
          </tbody>
        </table>
        <p class="empty" v-else>进程表已关闭 —— 在设置窗口打开「显示进程表」</p>
      </section>

      <section class="card">
        <div class="toolbar">
          <h2>嵌套设置（序数键只读展示）</h2>
          <span class="hint">MonitorModel.Settings → ModelValue / 序数键</span>
        </div>
        <ul class="settings">
          <li><span>pollIntervalMs</span><code>{{ settingsView.pollIntervalMs }}</code></li>
          <li><span>maxProcesses</span><code>{{ settingsView.maxProcesses }}</code></li>
          <li><span>showProcesses</span><code>{{ settingsView.showProcesses }}</code></li>
          <li><span>theme</span><code>{{ settingsView.theme }}</code></li>
        </ul>
        <p class="muted">编辑在「监控设置」窗口进行 —— 它绑定的是同一 Settings 实例（master-detail），改动全广播。</p>
      </section>
    </main>
  </div>
</template>
