# CEF 平台调试任务

> 记录 2026-08-14 的 CEF 平台调试工作，供在新计算机继续。
> **不要信任本仓库 README/CLAUDE.md 里的旧 CEF 记录**（用户多次强调记录有误，以本文档 + 实际日志为准）。

## 任务目标

修复 WebWindowUI CEF 平台的崩溃（`STATUS_STACK_BUFFER_OVERRUN` / `0xC0000409` fastfail）。
**当前主阻塞**：页面 renderer 加载 chrome://gpu 后 ~1s，renderer 进程 `0xC0000409 @ libcef.dll+0x42240be` 确定性崩溃（9 次全同偏移）。
对照基准：`E:\temp_code\CefGlue\CefGlue.Demo.Avalonia`（同 CEF 150.0.11 + 同 CefGlue + 同 chrome://gpu + 窗口模式，**不崩**）。
目标顺序：先让 chrome://gpu 通过 → 再把 `CefWindow.Address` 换回项目 URL（`WebWindowResource.GetWindowIndexUrl(options.WindowPath)`）。渲染结果问用户，不用截图。

## 已确认的关键事实（勿再推翻）

- **`0xC0000409`(STATUS_STACK_BUFFER_OVERRUN) = `__fastfail`，程序自查自裁**（未必是字面栈溢出）。本崩溃是 **V8 CHECK 失败**：崩溃寄存器 ~10 个全被毒化成 `0xBEEFE5AD`（= V8 失败路径 `SetAllRegistersToPoisonValue` 签名），fastfail 码 **57（0x39）**（不在标准 Windows FAST_FAIL 表，V8/Chromium 专属）。
- **崩溃在 renderer 进程，不是 browser**。判别法：minidump 线程分析——崩溃 dump 无任何线程 pump Win32 消息循环（无 user32.dll 栈帧），全 ntdll/libcef 等待线程 → renderer。**模块集判别法不可靠**：same-exe 模式下 renderer 也把 WebWindowUI.Core/Backend/Mvvm/Platforms.Cef 全加载（.NET 加载入口程序集引用链），据此误判过 BROWSER。browser 进程（最后一次运行 PID 40332）存活 ≥9 分钟未崩；只有 GPU 进程在实验移除 SwiftShader flags 时崩（`ContextResult::kFatalFailure: Failed to create shared context for virtualization`，`exit_code=-2147483645`，browser 自动重初始化）。
- **崩溃栈全在 libcef**，0x4224xxxx 区有递归簇（0x4226157 ≥6 次、0x4225ddc/0x422593f 重复）→ 同一热函数循环/递归里的 CHECK。
- **已排除 6 变量**（各配置下全部仍复现）：
  1. StackDebug/RenderTrace 文件锁（只让 browser 崩 0xe0434352，移除后 browser 稳定）。
  2. SwiftShader GL flags（`--use-gl=angle --use-angle=swiftshader --enable-unsafe-swiftshader --ignore-gpu-blocklist`，有/无都崩）。
  3. `NoSandbox`（CefGlue 传 `IntPtr.Zero` sandbox_info → Chromium 自动加 `--no-sandbox`，两边 renderer 都有，无差异）。
  4. Custom schemes app/appdata（含 `IsDisplayIsolated`；移除也崩）。
  5. `--start-stack-profiler`（Chromium 自加，页面 renderer 两边都没有）。
  6. 隐藏宿主 + SetParent 重挂载（直接嵌可见窗口也崩）。
- **托管 renderer 代码是 Demo 子集**：`third-party/CefGlue/CefGlue.BrowserProcess.Core/Handlers/RenderProcessHandler.cs` 与上游 diff 仅 4 处二分注释——`_javascriptToNativeDispatcher`/`_frameDelivery`/`_sharedFrameDelivery` 未实例化、`_inputChannel.Install(context)` 注释掉，`_javascriptExecutionEngine` 激活。Demo 全部激活且不崩 → 托管 renderer 差异排除（工作集的子集不可能是崩溃源）。
- **与 Demo 完全对齐的部分**（非差异）：CEF 二进制同源（NuGet `chromiumembeddedframework.runtime.win-x64` 150.0.11，`C:\temp\cef150\runtime-bin`）；CefGlue 同源码（vendored `third-party/CefGlue` == `E:\temp_code\CefGlue` 上游；`CefGlue/Interop/version.g.cs` 确认 `CEF_VERSION="150.0.11+gb887805+chromium-150.0.7871.115"`，与二进制匹配，**无版本错配**）；CefSettings 几乎相同（RootCachePath+Verbose+LogFile，`WindowlessRenderingEnabled` 均 false 窗口模式，同 `CefRuntimeLoader` 强制 `UncaughtExceptionStackSize=100` + Windows `MultiThreadedMessageLoop=true`）；URL 同 chrome://gpu；same-exe 子进程（`CefSubProcess.Run(args,true)`，Program.cs 首行，--type= 存在则走 ExecuteProcess 不返回）。
- Demo 每次启动也崩 1 次但码是 `0xe0434352`（它自己的 StackDebug 文件锁 bug，dump `Xilium.CefGlue.Demo.Avalonia.exe.19676.dmp`），页面 renderer 存活，与我们无关。

## 崩溃 dump 分析（minidump，无 cdb/windbg，用 Python skelsec-minidump）

- dump 在 `%LOCALAPPDATA%\CrashDumps\WebWindowUI.Sample.exe.<pid>.dmp`（9 个，mtime 23:11–23:38 各实验），全 renderer。
- skelsec API：`from minidump.minidumpfile import MinidumpFile; mf = MinidumpFile.parse(path)`；异常 `mf.exception.exception_records[0].ExceptionRecord`（`ExceptionCode_raw`/`ExceptionAddress`/`ExceptionInformation`）；模块 `m.baseaddress/endaddress/name`（name 可能 bytes utf-16-le）；线程 `t.ThreadId/t.ContextObject`（`.Rip/.Rsp/.Rbp`）；内存段 `s.inrange(addr)` + `s.read(addr,8,mf.file_handle)`（`mf.file_handle` 必传）。
- 脚本：`C:\tmp\classify_dumps.py`（模块分类——**勿再用它判 browser/renderer**）、`C:\tmp\thread_analysis.py`（线程栈判别，正确法）。
- 崩溃详情（dump 45616）：`ExceptionAddress=0x7ff94a8b40be = libcef.dll+0x42240be`；`ExceptionInformation=[57, 栈地址, 0x4000xxxx]`；Rip=Rsp 区全 `0xBEEFE5AD`（寄存器毒化）。

## 下一步

1. **capstone 反汇编 libcef.dll 定位 CHECK 消息**（capstone 5.0.7 已装）：
   - `C:/temp/cef150/runtime-bin/libcef.dll`（275658240 B）偏移 0x42240be 处应见 `int 29h`（__fastfail，码在 ECX=0x39 前置）。
   - 回看 ~2KB 找 V8 CHECK 序列：`mov ecx,0x39` + RIP 相对 `lea rdx,[rip+msg]` 加载断言字符串 → 按 PE 节表 RVA→文件偏移读 .rdata 的 CHECK 文本，即可命名确切失败点（比符号更直接）。
2. **renderer 命令行逐字段对比**（当前最小配置 vs Demo）：重跑抓 `--type=renderer` 命令行找残余 switch 差异（此前对比"几乎一致"，需当前配置重确认）。
3. 拿到 CHECK 名后修根因 → 恢复下方"当前代码状态"的原始配置 → chrome://gpu 通过 → 换项目 URL。
4. 收尾：还原 RenderProcessHandler 二分注释、清理 `Platforms/WebWindowUI.CefSubProcess` 临时工程、更新本文档 + 记忆。

## 当前代码状态（实验剥离态，须恢复）

- `CefPlatform.cs` Init()：无 `NoSandbox`、无 SwiftShader flags、无 custom schemes（`CefRuntimeLoader.Initialize(settings)`）。**原始配置**：`NoSandbox=true` + flags `enable-unsafe-swiftshader`/`ignore-gpu-blocklist`/`use-gl=angle`/`use-angle=swiftshader` + customSchemes app/appdata（各 `IsDisplayIsolated=true`；flags 理由：VM 无硬件 GPU，D3D11 ANGLE 建上下文必崩）。
- `Win32CefControl.cs`：Attach 只 `Resize+=NotifySize`；GetHostViewHandle 返回可见窗口；InitializeRender 空。**原始配置**：`Attach` 建 `Win32BrowserHost.CreateHiddenHost()` 隐藏宿主；`GetHostViewHandle` 返回隐藏宿主；`InitializeRender` 经 `WebWindowPlatform.Current.RunOnUiThread(... Win32BrowserHost.Reparent(browserHandle, native.WindowHandle, rc.Width, rc.Height) ...)`（对齐 CefGlue.Avalonia，隐藏宿主保证 DevTools 弹窗独立顶层窗口）。
- `CefWindow.cs` line 74-76：`Address = "chrome://gpu"`（原行 `Address = WebWindowResource.GetWindowIndexUrl(options.WindowPath)` 注释着）。
- `RenderProcessHandler.cs`：4 处二分注释（见上），须还原。
- `Platforms/WebWindowUI.CefSubProcess/`：临时 same-exe 子进程工程，完成后退删。

## 环境事实（新计算机/续调试要用）

- CEF 日志 `C:\Users\40206\Desktop\logs\cef_debug.log`（每次启动截断；时间戳 `MMdd/HHmmss.mmm` **无冒号**，grep 别用带冒号模式）。
- 崩溃 PID 从 WER `AppSessionGuid` 十六进制取（如 0xC384=50052），`$pid` 是 PowerShell 只读内置变量不能用来取。
- 验证纪律：跑完必须 `cmd //c "taskkill /IM WebWindowUI.Sample.exe /F"`；绝不动 Windows SearchHost 的 msedgewebview2.exe；bash `&` 启动的进程宿主 shell 一结束就死，用 Start-Process。
- 构建：`dotnet build Sample/WebWindowUI.Sample/WebWindowUI.Sample.csproj -c Debug`；运行 `cd Sample/WebWindowUI.Sample/bin/Debug/net10.0 && ./WebWindowUI.Sample.exe`。
- 无 cdb/windbg；Python 3.13.7 + winget 可用（备选 winget 装 WinDbg）。
