<script setup lang="ts">
// 目录 tab：复用「入口」按钮网格 → 综合窗口内跳转功能 tab。
// 按钮仍调 .NET 命令（MVVM Command + CanExecute=ButtonEnable 门控演示），
// 同时 emit('select') 让 App.vue 切 tab（switchTo 经 demo 命令权威切换 Window.Model）。
const props = defineProps<{ model: any }>()
const emit = defineEmits<{ (e: 'select', name: string): void }>()

function openMain() {
  props.model.openWindow() // 无参命令，不受 CanExecute 门控
  emit('select', 'main')
}

function open(name: string) {
  props.model.commandWithArg(name) // 带参命令，受 CanExecute 门控
  emit('select', name)
}
</script>

<template>
  <section class="card">
    <h2>选择一个功能（tab 切换，多窗口 / 嵌套详情为子演示）</h2>
    <p>
      按钮调用 .NET 命令（MVVM Command）：<code>commandWithArg(path)</code> 切换对应功能，受
      <code>CanExecute = ButtonEnable</code> 门控；<label class="toggle">
        <input type="checkbox" v-model="props.model.buttonEnable" /> 启用带参命令
      </label>
    </p>

    <div class="grid">
      <button class="item" @click="openMain()">
        <strong>模型双向绑定</strong>
        <span>MainWindowModel：Name / Count / Message / Extra（无参命令 OpenWindow）</span>
      </button>
      <button class="item" :disabled="!props.model.buttonEnable" @click="open('todos')">
        <strong>待办列表</strong>
        <span>TodoListModel：ObservableCollection 原地增删自动推送</span>
      </button>
      <button class="item" :disabled="!props.model.buttonEnable" @click="open('resources')">
        <strong>资源与数据通道</strong>
        <span>app:// 静态资源 + appdata:// 二进制数据通道</span>
      </button>
      <button class="item" :disabled="!props.model.buttonEnable" @click="open('multi')">
        <strong>多窗口共享 / 独立</strong>
        <span>MultiWindowModel：共享 A/B + 独立实例（子演示开 3 个窗口）</span>
      </button>
      <button class="item" :disabled="!props.model.buttonEnable" @click="open('settings')">
        <strong>设置</strong>
        <span>SettingsModel：多类型字段 + 跨线程推送</span>
      </button>
      <button class="item" :disabled="!props.model.buttonEnable" @click="open('about')">
        <strong>关于</strong>
        <span>AboutModel：静态信息页</span>
      </button>
      <button class="item" :disabled="!props.model.buttonEnable" @click="open('nested')">
        <strong>模型嵌套窗口</strong>
        <span>NestedParentModel.Detail 嵌套 NestedDetailModel + 详情子窗口</span>
      </button>
      <button class="item" :disabled="!props.model.buttonEnable" @click="open('nested-list')">
        <strong>List&lt;&gt;嵌套窗口</strong>
        <span>Items=List&lt;NestedListItemModel&gt;，元素内嵌套 Tags/Meta + 列表项详情子窗口</span>
      </button>
      <button class="item" :disabled="!props.model.buttonEnable" @click="open('platform')">
        <strong>平台特性</strong>
        <span>PlatformModel：系统托盘 / 通知 / 剪贴板 / 对话框（IPlatform）</span>
      </button>
    </div>
  </section>
</template>
