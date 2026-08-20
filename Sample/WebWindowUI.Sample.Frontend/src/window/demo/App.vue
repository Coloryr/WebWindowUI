<script setup lang="ts">
import { ref } from 'vue'
import { bindDemoModel } from '../../models/DemoModel'
import { bindLauncherModel } from '../../models/LauncherModel'
import { bindMainWindowModel } from '../../models/MainWindowModel'
import { bindTodoListModel } from '../../models/TodoListModel'
import { bindSettingsModel } from '../../models/SettingsModel'
import { bindAboutModel } from '../../models/AboutModel'
import { bindNestedParentModel } from '../../models/NestedParentModel'
import { bindNestedListModel } from '../../models/NestedListModel'
import { bindMultiWindowModel } from '../../models/MultiWindowModel'
import { bindPlatformModel } from '../../models/PlatformModel'

import LauncherTab from './tabs/LauncherTab.vue'
import MainTab from './tabs/MainTab.vue'
import TodosTab from './tabs/TodosTab.vue'
import ResourcesTab from './tabs/ResourcesTab.vue'
import SettingsTab from './tabs/SettingsTab.vue'
import AboutTab from './tabs/AboutTab.vue'
import NestedTab from './tabs/NestedTab.vue'
import NestedListTab from './tabs/NestedListTab.vue'
import MultiTab from './tabs/MultiTab.vue'
import PlatformTab from './tabs/PlatformTab.vue'

// 单窗口单模型约束：桥的 onReceive 是覆盖式，同页并行绑多模型只有最后一个能收下行。
// 故综合窗口用「动态换绑」：tab 点击 → demoModel.switchModel（.NET 切 Window.Model）
// + bindXxxModel()（重绑新模型，Ready 后 .NET 补快照）。旧模型停止收推、watch 泄漏无害。
const BINDERS: Record<string, () => any> = {
  launcher: bindLauncherModel,
  demo: bindDemoModel,
  main: bindMainWindowModel,
  todos: bindTodoListModel,
  settings: bindSettingsModel,
  about: bindAboutModel,
  nested: bindNestedParentModel,
  'nested-list': bindNestedListModel,
  multi: bindMultiWindowModel,
  platform: bindPlatformModel,
}

const TABS = [
  { name: 'launcher', label: '目录' },
  { name: 'main', label: '模型绑定' },
  { name: 'todos', label: '待办列表' },
  { name: 'resources', label: '资源通道' },
  { name: 'settings', label: '设置' },
  { name: 'about', label: '关于' },
  { name: 'nested', label: '模型嵌套' },
  { name: 'nested-list', label: '嵌套列表' },
  { name: 'multi', label: '多窗口' },
  { name: 'platform', label: '平台特性' },
]

// 测试模式（?model=xxx）：直接绑目标模型、不显示 tab 栏（E2E 直达：测试语义 = 窗口路径 → __model 类型）。
// 人工模式：绑 demo 模型 + tab 栏动态换绑（首页为「目录」）。
const params = new URLSearchParams(location.search)
const isTestMode = params.has('model')
const testModel = params.get('model') ?? ''

const active = ref('launcher')
const current = ref<any>(null)
let demoModel: any = null

function switchTo(name: string) {
  if (name === 'resources') {
    active.value = name
    current.value = null
    return
  }
  if (!BINDERS[name]) return
  if (demoModel) demoModel.switchModel(name) // .NET 切 Window.Model → 补快照给新绑定模型
  current.value = BINDERS[name]()
  ;(window as any).__model = current.value
  active.value = name
}

if (isTestMode) {
  if (testModel === 'resources') {
    active.value = 'resources'
  } else if (BINDERS[testModel]) {
    current.value = BINDERS[testModel]()
    ;(window as any).__model = current.value
    active.value = testModel
  }
} else {
  demoModel = bindDemoModel()
  ;(window as any).__model = demoModel

  // demoModel 必须先捕获 .NET 实例 id（首张快照含 modelInstanceId）再切首页——否则同步 init 立刻
  // 重绑覆盖其 onReceive、快照被后续绑定吞掉，demoModel 的 invoke 永远不带实例 id → tab 切换 /
  // openMulti 命令路由到当前模型而非 demo 实例（目录能进、切功能 tab / 开子窗全断）。
  active.value = 'boot'
  const enterHome = () =>
    demoModel && typeof demoModel._modelInstanceId === 'number' && demoModel._modelInstanceId > 0
      ? switchTo('launcher') // 默认进目录（切 Window.Model 到 launcher 实例并绑定）
      : setTimeout(enterHome, 20)
  enterHome()
}
</script>

<template>
  <div class="shell demo-shell">
    <header class="header">
      <img class="logo" src="/logo.svg" alt="logo" />
      <div>
        <h1>WebWindowUI 综合演示</h1>
        <p class="subtitle">
          窗口路径 <code>demo</code> · 全部功能合并一个窗口，tab 切换 + 动态换绑模型（多窗口 / 嵌套详情为子演示）
        </p>
      </div>
    </header>

    <nav v-if="!isTestMode" class="tabs">
      <button
        v-for="t in TABS"
        :key="t.name"
        class="tab"
        :class="{ active: active === t.name }"
        @click="switchTo(t.name)"
      >
        {{ t.label }}
      </button>
    </nav>

    <main>
      <LauncherTab v-if="active === 'launcher'" :model="current" @select="switchTo" />
      <MainTab v-else-if="active === 'main'" :model="current" />
      <TodosTab v-else-if="active === 'todos'" :model="current" />
      <ResourcesTab v-else-if="active === 'resources'" />
      <SettingsTab v-else-if="active === 'settings'" :model="current" />
      <AboutTab v-else-if="active === 'about'" :model="current" />
      <NestedTab v-else-if="active === 'nested'" :model="current" />
      <NestedListTab v-else-if="active === 'nested-list'" :model="current" />
      <MultiTab v-else-if="active === 'multi'" :model="current" :demo="demoModel" />
      <PlatformTab v-else-if="active === 'platform'" :model="current" />
      <p v-else class="muted">{{ active === 'boot' ? '正在连接 .NET 模型…' : '未知模型：' + (testModel || active) }}</p>
    </main>
  </div>
</template>
