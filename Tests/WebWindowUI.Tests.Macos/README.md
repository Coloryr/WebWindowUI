# WebWindowUI.Tests.Macos

**macOS 桥 E2E**（19 场景，独立 slnx `WebWindowUI.Tests.Macos.slnx`，不计入主 slnx 回归数）。

## 为什么是独立 Exe 而非 xunit

macOS 主队列（`MacOSMessageLoopSynchronizationContext.Post` 唤醒路径）与 WKWebView 回调（导航/消息/scheme/JS 求值）都绑定**进程主线程**——testhost 占着主线程跑 VSTest 消息循环、后台泵排不干主队列收不到导航（/tmp/macpumptest 实测 `mainQueueFired=0 navFired=0`）。故本工程是 **Exe 自带 Main**：

- Main 即主线程，裸 `CFRunLoopRunInMode` 泵排干主队列并派发 WKWebView 事件（`[DllImport]` CoreFoundation，CFString mode 须强引用保 Handle），顺次跑全部场景。
- 场景经 `MacOSMessageLoopSynchronizationContext.Instance.Post` 投递回主队列、harness 直接 await（顶层泵在 Main）。
- **TerminateGuard**：`NSApplicationDelegate` 覆写 `ApplicationShouldTerminate` 返回 `Cancel` 吞掉「最后窗口关闭 → NSApplication.Terminate」（测试用 Main 返回退出，不走 App 终止流程）。
- 不引 xunit，自研 `Assert`/`MacOSTestRunner`。

## 组成

| 文件 | 职责 |
|------|------|
| `MacOSTestProgram.cs` | Main：`_ = typeof(MacOSPlatform)` 触发 `[ModuleInitializer]` 注册（主线程）→ 设 app delegate → 跑场景 |
| `MacOSTestRunner.cs` | 场景顺序执行 + 顶层泵 |
| `MacOSTestHarness.cs` | 建窗/等 bridge ready/JS 求值助手 |
| `MacOSModelBridgeTests.cs` | 19 个桥场景 |
| `Assert.cs` | 自研断言 |

## 构建 / 运行

```bash
dotnet build Tests/WebWindowUI.Tests.Macos/WebWindowUI.Tests.Macos.csproj -c Debug -p:ValidateXcodeVersion=false
bin/Debug/net10.0-macos/osx-arm64/WebWindowUI.Tests.Macos.app/Contents/MacOS/WebWindowUI.Tests.Macos
```

- `-p:ValidateXcodeVersion=false`：SDK 26.5 要 Xcode 26.6、机器 26.5，全局属性绕过并随 ProjectReference 传到 Sample。
- wwwroot 经 `_WWUI_MacOSBundleWwwroot` 拷进 MonoBundle，Debug 磁盘回退读 `AppContext.BaseDirectory\wwwroot`。

## durable 坑

- **`; 0` 后缀**：macOS `ExecuteScriptAsync` 包 `JSON.stringify(script)`，多语句脚本是语法错误；从 WebView2 借来的脚本（`foo(); 0`）须去掉 `; 0`。
- 跑前必须确认 Sample 前端 node_modules 的桥是最新（0.1.6，`npm install webwindowui-bridge@^0.1.6`，升级后 touch `vite.config.ts` 强制重建）；旧桥（0.1.5）产物无 `_modelInstanceId` 捕获逻辑 → 三个实例 ID 用例超时（数据快照照常到达，迷惑性强）。
