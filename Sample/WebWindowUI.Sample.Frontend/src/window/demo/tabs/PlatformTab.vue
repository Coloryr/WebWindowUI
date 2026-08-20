<script setup lang="ts">
// 平台特性 tab：前端按钮 → .NET 命令 platformAction(action) → 控制器执行 IPlatform 调用
// （托盘/通知/剪贴板/对话框），结果回写模型状态属性增量推送。
const props = defineProps<{ model: any }>()
</script>

<template>
  <section class="card">
    <h2>系统托盘（ITrayIcon）</h2>
    <div class="row">
      <button @click="props.model.platformAction('create-tray')">创建托盘</button>
      <button @click="props.model.platformAction('delete-tray')">删除托盘</button>
      <button @click="props.model.platformAction('toggle-tray')">
        {{ props.model.trayVisible ? '隐藏托盘' : '显示托盘' }}
      </button>
      <button @click="props.model.platformAction('balloon')">气泡通知</button>
    </div>
    <label class="field">
      提示文本
      <input v-model="props.model.trayTip" />
    </label>
    <div class="row">
      <label class="field">气泡标题 <input v-model="props.model.balloonTitle" /></label>
      <label class="field">气泡正文 <input v-model="props.model.balloonText" /></label>
    </div>
    <p class="hint">
      右键托盘弹出菜单（显示/隐藏窗口、气泡样式子菜单、退出）；单击/双击事件回传按钮类型与坐标。
    </p>
  </section>

  <section class="card">
    <h2>系统通知（INotification）</h2>
    <label class="field">
      通知正文
      <input v-model="props.model.notificationText" />
    </label>
    <div class="row">
      <button @click="props.model.platformAction('notify')">显示通知</button>
    </div>
    <p class="hint">点击通知气泡 → 最近事件更新（Linux 依赖 libnotify，无通知服务时静默）。</p>
  </section>

  <section class="card">
    <h2>剪贴板（IClipboard）</h2>
    <label class="field">
      文本
      <input v-model="props.model.clipboardText" />
    </label>
    <div class="row">
      <button @click="props.model.platformAction('copy')">复制到剪贴板</button>
      <button @click="props.model.platformAction('paste')">从剪贴板粘贴</button>
    </div>
  </section>

  <section class="card">
    <h2>平台对话框（IPlatformDialog）</h2>
    <div class="row">
      <button @click="props.model.platformAction('message-box')">系统消息框</button>
      <button @click="props.model.platformAction('open-file')">打开文件</button>
      <button @click="props.model.platformAction('open-folder')">选择路径</button>
      <button @click="props.model.platformAction('save-file')">保存文件</button>
      <button @click="props.model.platformAction('save-folder')">保存路径</button>
    </div>
  </section>

  <section class="card">
    <h2>最近事件</h2>
    <p class="event">{{ props.model.lastEvent }}</p>
  </section>
</template>
