<script setup lang="ts">
import { bindLauncherModel } from '../../models/LauncherModel'

// 入口（启动器）页：演示「前端按钮 → .NET 命令（MVVM Command）」。
// 按钮点击调用生成器在 LauncherModel.ts 里产出的命令方法：
//   model.openWindow()         → 无参命令，触发 .NET OpenWindowCommand → 打开主窗口
//   model.commandWithArg(path) → 带参命令，触发 .NET CommandWithArgCommand → 打开对应窗口
// 桥把命令调用编码成 ModelInvoke{ command, value } 发给 .NET；commandWithArg 受
// CanExecute = ButtonEnable 门控——buttonEnable 为 false 时 .NET 拒绝执行，按钮同步禁用。
// bindLauncherModel 由生成器产出：创建实例 + 传 descriptor 给 webwindowui-bridge。
// window.__model 暴露给宿主（自动化测试 / 调试）读取页面模型状态。
const model = bindLauncherModel()
;(window as any).__model = model
</script>

<template>
  <div class="shell">
    <header class="header">
      <img class="logo" src="/logo.svg" alt="logo" />
      <div>
        <h1>WebWindowUI 示例入口</h1>
        <p class="subtitle">
          窗口路径 <code>launcher</code> · 每个功能一个窗口，按钮点击按需启动
        </p>
      </div>
    </header>

    <main>
      <section class="card">
        <h2>选择一个功能窗口</h2>
        <p>
          按钮调用 .NET 命令（MVVM Command）：<code>commandWithArg(path)</code> 打开对应窗口，
          受 <code>CanExecute = ButtonEnable</code> 门控；<label class="toggle">
            <input type="checkbox" v-model="model.buttonEnable" /> 启用带参命令
          </label>
        </p>

        <div class="grid">
          <button class="item" @click="model.openWindow()">
            <strong>模型双向绑定</strong>
            <span>无参命令 OpenWindow：MainWindowModel：Name / Count / Message / Extra</span>
          </button>
          <button class="item" :disabled="!model.buttonEnable" @click="model.commandWithArg('todos')">
            <strong>List&lt;Model&gt; 一一对应</strong>
            <span>TodoListModel：ObservableCollection 原地增删自动推送</span>
          </button>
          <button class="item" :disabled="!model.buttonEnable" @click="model.commandWithArg('resources')">
            <strong>资源与数据通道</strong>
            <span>app:// 静态资源 + appbin:// 二进制数据通道</span>
          </button>
          <button class="item" :disabled="!model.buttonEnable" @click="model.commandWithArg('multi')">
            <strong>多窗口共享 / 独立</strong>
            <span>MultiWindowModel：共享 A/B + 独立实例（一次开 3 个）</span>
          </button>
          <button class="item" :disabled="!model.buttonEnable" @click="model.commandWithArg('settings')">
            <strong>设置</strong>
            <span>SettingsModel：多类型字段 + 跨线程推送</span>
          </button>
          <button class="item" :disabled="!model.buttonEnable" @click="model.commandWithArg('about')">
            <strong>关于</strong>
            <span>AboutModel：静态信息页</span>
          </button>
          <button class="item" :disabled="!model.buttonEnable" @click="model.commandWithArg('nested')">
            <strong>模型嵌套窗口</strong>
            <span>NestedParentModel.Detail 嵌套 NestedDetailModel + 详情子窗口</span>
          </button>
          <button class="item" :disabled="!model.buttonEnable" @click="model.commandWithArg('nested-list')">
            <strong>List&lt;&gt;嵌套窗口</strong>
            <span>Items=List&lt;NestedListItemModel&gt;，元素内嵌套 Tags/Meta + 列表项详情子窗口</span>
          </button>
        </div>
      </section>
    </main>
  </div>
</template>
