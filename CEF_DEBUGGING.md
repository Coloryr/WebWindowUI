# CEF 平台调试任务

> 记录 2026-08-14 的 CEF 平台调试工作,供在新计算机继续。
> **不要信任本仓库 README/CLAUDE.md 里的旧 CEF 记录**(用户多次强调记录有误,以本文档 + 实际日志为准)。

## 任务目标

修复 WebWindowUI CEF 平台的两个崩溃(都是 `STATUS_STACK_BUFFER_OVERRUN` / `0xC0000409` fastfail):

1. **launcher 崩溃**:`protobuf.Root.fromJSON(descriptor)` 解析模型描述符时,V8 fastfail,页面显示错误页。
2. **DevTools 窗口崩溃/即开即关**:F12 开 DevTools 时,DevTools 前端(V8)崩溃,窗口闪退。

## 已确认的关键事实

- **`0xC0000409`(STATUS_STACK_BUFFER_OVERRUN)是 CEF 150/151 的 V8 fastfail**(libcef.dll 里 CHECK 失败)。WebView2(新版 Chromium)无此问题。
- **触发点**:
  - protobufjs 描述符解析(`protobuf.Root.fromJSON`),描述符含递归 ModelValue(ModelValue→ModelValueList/ModelValueMap→ModelValue)+ 引用它的模型/Update 类型。
  - DevTools 前端(devtools:// 重 JS)。
- **已验证的规避**:
  - `base + LauncherModel + LauncherModelUpdate`(Launcher 不引用 ModelValue)解析**不崩**。
  - 打破 ModelValue 递归(非递归 ModelValue + About + Update)解析**不崩**。
- **无效的尝试**(都实测无效):
  - CEF 150 vs 151(都崩;CEF 151 升级没修,偏移从 0x42240be 变 0x44c0bbe)。
  - `--disable-gpu`、`--use-gl=disabled`(用户明确:不是 GPU 问题,不再动 GPU)。
  - 自定义 scheme / data: 页面 / chrome://gpu(都崩;scheme 不是元凶)。
  - 隐藏宿主 + 重挂载 / 完整注册类隐藏窗口(DevTools 时好时坏)。
  - 对齐 Avalonia 窗口样式(WS_EX_NOREDIRECTIONBITMAP 破坏渲染,已回退)。
  - 对齐 Avalonia CefSettings(无效)。
- **Avalonia demo(CEF 150)的 DevTools 稳定**(用户确认,反复 F12 不闪退)。精确差异未定位,可能是 CefGlue.Common + Avalonia 原生宿主的深层交互。

## 已完成的重构(在仓库中)

1. **Vendored CefGlue**:`third-party/CefGlue/`(CefGlue / CefGlue.Common / CefGlue.Common.Shared / CefGlue.BrowserProcess.Core),针对 CEF 151 用 `upgrade-cef.ps1` 重生成(也含 CefGlue.Interop.Gen 生成器)。
2. **浏览器托管层换成 CefGlue.Common 自带实现**:
   - `CefWindow : Xilium.CefGlue.Common.BaseCefBrowser`(链接 `BaseCefBrowser.cs` partial + `BaseCefBrowser.Address.cs` 提供 Address 实现)。
   - `Win32CefControl : IControl`(**隐藏宿主 + SetParent 重挂载**,对齐 Avalonia:`GetHostViewHandle` 返回隐藏窗口,`InitializeRender` 重挂载进可见窗口)。Natives.Windows 新增公开 `Win32BrowserHost`(CreateHiddenHost/Reparent)。
   - `CefPlatform` 用 `CefRuntimeLoader.Initialize`(schemes 经 CustomScheme)。
   - **给 vendored CefGlue.Common 的改动**:`InternalsVisibleTo("WebWindowUI.Platforms.Cef")` + `CommonBrowserAdapter` 加 `BrowserClosed` 事件/`CloseBrowser` + `BaseCefBrowser` 暴露。**DevTools 关闭也触发 BrowserClosed——CefWindow.OnBrowserClosed 必须只对主浏览器(`ReferenceEquals(browser, UnderlyingBrowser)`)销毁窗口**。
3. **Windows 运行时**:手动下载 CEF 151 二进制,`CefRuntimeDir`(当前 `C:\temp\cef150\runtime-bin`)经 Content 传播到 app 输出。NuGet 的 `chromiumembeddedframework.runtime` / `CefGlue.Next` 止步 150。

## 当前代码状态(需要还原的临时改动)

- `CefWindow.cs`:`Address` 被临时设为 data: URL(或 chrome://gpu via Task.Run)。**需还原为 `WebWindowResource.GetWindowIndexUrl(options.WindowPath)`**。
- `CefPlatform.cs`:`--use-gl=disabled` flag 可能残留(用户说无效,可移除)。

## 操作流程(新计算机)

### 1. 仓库与依赖

```bash
# clone 仓库(含 third-party/CefGlue)
# 下载 CEF 151 发行版(需 include/ + Release/ + Resources/ + cmake/)
#   https://cef-builds.spotifycdn.com/cef_binary_151.3.17%2Bgf059e67%2Bchromium-151.0.7922.138_windows64_minimal.tar.bz2
# 解压到 C:\temp\cef151\cef_binary_...
# 合并:cp -r Resources/* Release/  (icudtl.dat/*.pak/locales 放 libcef.dll 旁)
# 建运行时目录 C:\temp\cef150\runtime-bin(从 chromiumembeddedframework.runtime.win-x64 150.0.11 包提取:
#   ~/.nuget/packages/chromiumembeddedframework.runtime.win-x64/150.0.11/runtimes/win-x64/native/* + locales)
```

### 2. 构建 Sample(CEF 平台)

```bash
# 若用 CEF 150(推荐,已重生成 vendored 到 150):
#   cd third-party/CefGlue && powershell -File upgrade-cef.ps1 "150.0.11+gb887805+chromium-150.0.7871.115"
#   (需先复制 upgrade-cef.ps1 + CefGlue.Interop.Gen 到 third-party/CefGlue)
dotnet build Sample/WebWindowUI.Sample/WebWindowUI.Sample.csproj -c Debug
```

### 3. 运行 + 测试

```bash
cd Sample/WebWindowUI.Sample/bin/Debug/net10.0 && ./WebWindowUI.Sample.exe
# 观察:主窗口渲染? F12 DevTools?
# 日志:C:\Users\<user>\Desktop\logs\cef_debug.log
# 崩溃:Windows 事件日志(Application, Id=1000)
```

### 4. 官方 C 语言示例(验证 CEF 本身,确认 DevTools 是否在这台机器崩)

**源码从 GitHub 下载(不存仓库)**。

```bash
# 从 GitHub 下载官方 C API 示例源码(纯 C):
#   https://github.com/chromiumembedded/cef/tree/master/tests/cefsimple_capi
#   (文件:cefsimple_win.c simple_app.c simple_browser_list.c simple_display_handler.c
#    simple_handler.c simple_handler_win.c simple_life_span_handler.c simple_load_handler.c
#    simple_views.c simple_views.h simple_utils.h ref_counted.h resource.h)
# 示例:
#   BASE="https://raw.githubusercontent.com/chromiumembedded/cef/master/tests/cefsimple_capi"
#   curl -sL --fail "$BASE/cefsimple_win.c" -o cefsimple_win.c   # ... 等
# 放入 CEF 发行版的 tests/cefsimple_capi/
# 用 clang-cl 编译(lld-link 链接):
#   MSYS2_ARG_CONV_EXCL='*' clang-cl /std:c11 -DUNICODE -D_UNICODE -DCEF_API_VERSION=15101 \
#     -I<CEF> -I<CEF>/include /c *.c
#   lld-link /out:cefsimple_capi.exe *.obj <CEF>/Release/libcef.lib \
#     /libpath:<UCRT> /libpath:<VC> /libpath:<SDK> \
#     ucrt.lib vcruntime.lib msvcrt.lib kernel32.lib user32.lib ... \
#     /subsystem:windows /entry:wWinMain /machine:x64
#   cp -r <CEF>/Release/* <CEF>/Resources/* .   # 拷运行时
# 运行,按 F12 开 DevTools。若官方示例 DevTools 正常 → CefGlue 集成问题;若也崩 → CEF/VM 本身。
# 注意:Git Bash 会转译 / 开头的参数,必须设 MSYS2_ARG_CONV_EXCL='*'。
# 注意:MSVC 不支持 C11 atomics(编译报 __STDC_NO_ATOMICS__),必须用 clang-cl。
```

## 待办(下一步)

1. **修复 launcher protobufjs 崩溃**:改描述符规避(每模型独立 descriptor 或打破 ModelValue 递归)——已验证不崩。可先做个实验验证。
2. **DevTools**:进程内窗口不稳定,可靠方案 = **远程调试**(`--remote-debugging-port=9333` + 外部 Chrome `chrome://inspect`)。
3. 若官方 C 示例在这台机器 DevTools 正常,则继续对比 CefGlue 集成差异;若也崩,则 CEF 150/151 在这台 VM 上 DevTools 无法用,放弃进程内 DevTools。
