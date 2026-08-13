# webwindowui-bridge

**WebWindowUI 前后端 protobuf 双向绑定桥**（Vue3 + WebView2 / WebKitGTK / WKWebView / CEF）。纯 TS，`peerDependencies` = `vue ^3.2` + `protobufjs ^7.6`。已发布到 npm（当前 **0.1.6**）。

## 用法

前端工程 `npm install webwindowui-bridge`，在窗口页里：

```ts
import { bindModel } from 'webwindowui-bridge'
import generatedJson from '../bridge/main_window_model.json' // 生成器产出的完整模型 descriptor（信封已内联、自包含）

const model = bindModel({ title: '' }, generatedJson)
```

每个模型实例调用一次。TS 模型镜像（生成器产出）在文件末尾封装 `bindXxx()` 绑定助手（含 descriptor import）。

## 组成

| 文件 | 职责 |
|------|------|
| `channel.ts` | 平台自适应消息通道：WebView2（`chrome.webview`）/ WebKitGTK、WKWebView（`window.webkit.messageHandlers.wwui`）/ CEF（同源 `app://<host>/__wwui` 的 fetch POST）。发送通道**每次重新探测**（WebKitGTK 把 script message handler 同步进 web 进程有延迟，模块作用域一次性解析缓存会永远拿 null）；下行回调经 `window.wwuiReceive` / `chrome.webview 'message'` 事件 |
| `codec.ts` | NUL 转义 Latin-1 载荷（WebView2 消息通道在 NUL 处截断，protobuf 字节普遍含 0x00，须转义无损通过；WebKit/WKWebView 一并统一） |
| `value.ts` | `ModelValue` 值树 ↔ JS 值转换（`modelValueToJs`/`jsToModelValue`，`_modelInstanceId` 不可枚举收敛） |
| `protocol.ts` | 信封类型常量（WebMessage oneof 成员、CollectionPatch action）+ `ModelCommandHost` 基类（承载 `_commandChannel` 契约，命令模型继承；桥用 `Object.defineProperty` 注入**不可枚举** `_commandChannel`） |
| `bind-model.ts` | `bindModel(model, generatedJson)`：Vue `reactive` + `watch` 双向绑定、深 watch 回写、typed-repeated 元素级（构建期烘焙 `__repeatedFields`）、集合差量 `applyPatch` 原地 splice、实例 ID 防串守卫 |
| `index.ts` | 导出入口 |

## 协议要点

- **无消息名**：`modelId` = 完整消息名 FNV-1a 哈希、`commandId` = `[RelayCommand]` 声明序（.NET 与 TS 两侧同函数产出）。
- **消息流**：.NET → JS `full`（初始快照）/`snapshot`（通用回退）/`update`（单属性增量）/`patch`（集合差量）；JS → .NET `ready`/`set`（回写，含元素级）/`invoke`（命令）。
- **`modelInstanceId`（信封级 int64）**：桥从首个 full/snapshot 捕获为**不可枚举** `model._modelInstanceId`（不进 `Object.keys` watch 循环），对后续消息防串守卫（窗口换绑后旧实例在途消息丢弃，0 = 旧桥容忍）。

## durable 坑

- **协议契约一律构建期定死为字符串字面量**：typed-repeated 的字段表烘焙成模型镜像类 `static ['__repeatedFields']`（字符串字面量键）。**不要用运行时 `constructor.name` → `lookupType` 反射取元素字段表**——Release minified bundle（vite 8 / rolldown）会把 class 绑定名改名，typed-repeated 补丁退化成序数键挂掉。
- **元素级 `patch.elementInstanceId` 是 protobufjs Long**，与元素 `_modelInstanceId`（JS number）比较须先 `normalize()`（→toNumber），直接 `===` 恒 false → 元素级补丁静默不落。
- **结构回写（push/splice/替换 → 整列 ModelSet）须给每个元素补 `_modelInstanceId`**（`sendElementList`），否则 .NET 按 Clear+Add 重建、元素实例全换新、旧 id 失效。
- **本地调试**：纯浏览器场景无通道，`postMessage` 返回 false，不发不崩。

## 升级/本地迭代

- 升级：`npm install webwindowui-bridge@^0.1.6`，升级后 touch `vite.config.ts` 强制 vite 重建。
- **未发布**的本地桥迭代：物理拷进 `node_modules/webwindowui-bridge`（npm link 符号链接被 rolldown 解析到真实路径、无依赖报 `Failed to resolve import "protobufjs"`）+ touch `vite.config.ts` 强制重建，再 grep bundle 验证。桥是 code-split 的（`bind-model-*.js` 大 chunk），grep `main-*.js` 查不到桥逻辑、须 grep `bind-model-*.js`。
