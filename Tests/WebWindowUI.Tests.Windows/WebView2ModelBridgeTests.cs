using System.Collections;
using WebWindowUI.Sample;
using WebWindowUI.Sample.Items;
using WebWindowUI.Tests.Windows.Support;
using Xunit;

namespace WebWindowUI.Tests.Windows;

/// <summary>
/// 真 WebView2 端到端测试：真实 CoreWebView2 + 真实构建产物 wwwroot。
/// 每个测试经 WebView2TestHarness 在 STA 泵线程上打开一个窗口、绑定模型、等页面桥接就绪。
/// </summary>
[Collection("webview2")]
public class WebView2ModelBridgeTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(90);

    [Fact]
    public async Task Snapshot_ReachesDom()
    {
        var model = new MainWindowModel
        {
            Name = "小明",
            Count = 7,
            Message = "hi",
            Extra = new Dictionary<string, object> { ["a"] = 1 },
        };

        await WebView2TestHarness.RunMainWindowAsync(model, async win =>
        {
            // 快照进 DOM：count 初始值能证明 .NET 快照到了前端（区别于 TS 默认 0）
            await WebView2TestHarness.WaitJsAsync(win, "window.__model.count === 7", "快照 count");
            await WebView2TestHarness.WaitJsAsync(win, "window.__model.name === \"小明\"", "快照 name");
            await WebView2TestHarness.WaitJsAsync(win, "window.__model.message === \"hi\"", "快照 message");
            await WebView2TestHarness.WaitJsAsync(win, "JSON.stringify(window.__model.extra) === '{\"a\":1}'", "快照 extra");
        }, Timeout);
    }

    [Fact]
    public async Task IncrementalUpdate_ReachesDom()
    {
        var model = new MainWindowModel { Name = "小明", Count = 5, Message = "old" };

        await WebView2TestHarness.RunMainWindowAsync(model, async win =>
        {
            await WebView2TestHarness.WaitJsAsync(win, "window.__model.count === 5", "初始快照");

            // .NET → 前端：走生成器产出的 MainWindowModelUpdate 增量路径
            model.Count = 42;
            model.Message = "updated";

            await WebView2TestHarness.WaitJsAsync(win, "window.__model.count === 42", "增量 count");
            await WebView2TestHarness.WaitJsAsync(win, "window.__model.message === \"updated\"", "增量 message");
        }, Timeout);
    }

    [Fact]
    public async Task ModelInstanceId_PositiveAndStable()
    {
        var model = new MainWindowModel { Name = "小明", Count = 1 };

        await WebView2TestHarness.RunMainWindowAsync(model, async win =>
        {
            // 桥从首个快照捕获实例 ID 并暴露为非枚举 _modelInstanceId（不进 Object.keys watch 循环）
            await WebView2TestHarness.WaitJsAsync(win, "window.__model._modelInstanceId > 0", "实例 ID 为正数");
            string first = await win.ExecuteScriptAsync("window.__model._modelInstanceId");

            // 属性增量后实例 ID 不变：同实例消息不被防串守卫误杀
            model.Count = 2;
            await WebView2TestHarness.WaitJsAsync(win, "window.__model.count === 2", "增量到达");
            string second = await win.ExecuteScriptAsync("window.__model._modelInstanceId");
            Assert.Equal(first, second);
        }, Timeout);
    }

    [Fact]
    public async Task FrontendWriteBack_DotNet()
    {
        var model = new MainWindowModel { Name = "小明", Count = 7, Message = "hi" };

        await WebView2TestHarness.RunMainWindowAsync(model, async win =>
        {
            await WebView2TestHarness.WaitJsAsync(win, "window.__model.count === 7", "就绪");

            // 前端 → .NET：ModelSet → TrySetProperty → 属性写回
            await win.ExecuteScriptAsync("window.__model.name = 'from-js'; 0");
            await WebView2TestHarness.WaitDotNetAsync(() => model.Name == "from-js", ".NET Name 被前端回写");

            // 类型不匹配：count='bad' → TryFromModelValue 拒绝，Count 不变
            int countBefore = model.Count;
            await win.ExecuteScriptAsync("window.__model.count = 'bad'; 0");
            await Task.Delay(300); // 等回写消息到达（即便被拒绝也要先到）
            Assert.Equal(countBefore, model.Count);
        }, Timeout);
    }

    [Fact]
    public async Task NulBytes_ThroughStringChannel()
    {
        var model = new MainWindowModel { Name = "小明", Count = 1, Message = "a\0b" };

        await WebView2TestHarness.RunMainWindowAsync(model, async win =>
        {
            await WebView2TestHarness.WaitJsAsync(win, "window.__model.count === 1", "就绪");

            // message = "a\0b"：索引 1 是 NUL（charCode 0）——WebView2 字符串通道 + 编解码器不丢 NUL
            await WebView2TestHarness.WaitJsAsync(
                win,
                "window.__model.message.length === 3 && window.__model.message.charCodeAt(1) === 0",
                "NUL 字节穿越");

            // 反斜杠也要无损：消息改为 "a\\b"（单个反斜杠）
            model.Message = "a\\b";
            await WebView2TestHarness.WaitJsAsync(
                win,
                "window.__model.message.length === 3 && window.__model.message.charCodeAt(1) === 92",
                "反斜杠穿越");
        }, Timeout);
    }

    [Fact]
    public async Task Extra_Object_Bidirectional()
    {
        var model = new MainWindowModel
        {
            Name = "小明",
            Count = 2,
            Extra = new Dictionary<string, object>
            {
                ["k"] = 1,
                ["nested"] = new Dictionary<string, object> { ["x"] = "y" },
            },
        };

        await WebView2TestHarness.RunMainWindowAsync(model, async win =>
        {
            await WebView2TestHarness.WaitJsAsync(win, "window.__model.count === 2", "就绪");

            // .NET → JS：嵌套 object 递归展开
            await WebView2TestHarness.WaitJsAsync(
                win,
                "window.__model.extra.k === 1 && window.__model.extra.nested.x === \"y\"",
                ".NET→JS Extra 嵌套对象");

            // JS → .NET：object 回写，内含数组
            await win.ExecuteScriptAsync("window.__model.extra = { a: [1, 2, 3] }; 0");
            await WebView2TestHarness.WaitDotNetAsync(
                () => model.Extra is IDictionary dict
                    && dict["a"] is IList list && list.Count == 3,
                "JS→.NET Extra 含数组");
        }, Timeout);
    }

    [Fact]
    public async Task Settings_MultiModel_RealRun()
    {
        var model = new SettingsModel();

        await WebView2TestHarness.RunWindowAsync("settings", "设置", model, async win =>
        {
            await WebView2TestHarness.WaitJsAsync(win, "window.__model.theme === 'light'", "设置快照 theme");
            // int64 字段：protobufjs Long → number 转换后才是 1048576
            await WebView2TestHarness.WaitJsAsync(win, "window.__model.totalBytes === 1048576", "int64 Long→number");
            await WebView2TestHarness.WaitJsAsync(win, "window.__model.tags.length === 2 && window.__model.tags[0] === 'bridge'", "tags 数组");
            await WebView2TestHarness.WaitJsAsync(win, "window.__model.config && window.__model.config.proxy === 'auto'", "config 对象");
            await WebView2TestHarness.WaitJsAsync(win, "window.__model.syncMode === 0", "枚举兜底 number");

            // 回写 int64：JS number → .NET long
            await win.ExecuteScriptAsync("window.__model.totalBytes = 2048; 0");
            await WebView2TestHarness.WaitDotNetAsync(() => model.TotalBytes == 2048L, "回写 totalBytes");
        }, Timeout);
    }

    [Fact]
    public async Task SharedModel_OneInstance_TwoWindows_Broadcast()
    {
        var model = new MultiWindowModel("共享实例") { Name = "小明", Count = 5 };

        await WebView2TestHarness.RunTwoWindowsSharedModelAsync(model, async (winA, winB) =>
        {
            // 两个窗口都收初始快照（各自订阅了同一模型实例），只读标签 instanceId 照常下发
            await WebView2TestHarness.WaitJsAsync(winA, "window.__model.count === 5 && window.__model.name === \"小明\" && window.__model.instanceId === \"共享实例\"", "A 快照");
            await WebView2TestHarness.WaitJsAsync(winB, "window.__model.count === 5 && window.__model.name === \"小明\"", "B 快照");

            // .NET 侧改动 → 广播给所有绑定窗口
            model.Count = 42;
            await WebView2TestHarness.WaitJsAsync(winA, "window.__model.count === 42", "A 收 .NET 广播");
            await WebView2TestHarness.WaitJsAsync(winB, "window.__model.count === 42", "B 收 .NET 广播");

            // 前端 A 回写 → .NET 应用后排除源窗口广播 → B 跟随，A 不回声
            await winA.ExecuteScriptAsync("window.__model.name = 'from-A'; 0");
            await WebView2TestHarness.WaitDotNetAsync(() => model.Name == "from-A", ".NET Name 被 A 回写");
            await WebView2TestHarness.WaitJsAsync(winB, "window.__model.name === \"from-A\"", "B 收到跨窗口广播");
            await Task.Delay(300);
            Assert.Equal("\"from-A\"", await winA.ExecuteScriptAsync("window.__model.name")); // 无回声
        }, Timeout);
    }

    [Fact]
    public async Task Launcher_RequestWriteBack_ReachesDotNet()
    {
        var model = new LauncherModel();

        await WebView2TestHarness.RunWindowAsync("launcher", "示例入口", model, async win =>
        {
            // 入口页就绪：快照里 request 为默认空串
            await WebView2TestHarness.WaitJsAsync(win, "window.__model.request === ''", "快照 request");

            // 模拟按钮点击（等价 open('todos')）：回写窗口路径 → .NET Request
            await win.ExecuteScriptAsync("window.__model.request = 'todos'; 0");
            await WebView2TestHarness.WaitDotNetAsync(() => model.Request == "todos", ".NET Request 被前端回写");

            // 再次点击同一按钮（值未变）不触发 watch —— 值相同前端不重发，Request 保持 "todos"
        }, Timeout);
    }

    [Fact]
    public async Task Launcher_CommandInvoke_ExecutesDotNetCommand()
    {
        var model = new LauncherModel();
        string? opened = null;
        model.OpenRequested += p => opened = p; // 命令方法触发的事件，在 .NET UI 线程回调

        await WebView2TestHarness.RunWindowAsync("launcher", "示例入口", model, async win =>
        {
            // 无参命令 openWindow() → 桥发 ModelInvoke{commandId:0} → .NET OpenWindowCommand 执行
            await win.ExecuteScriptAsync("window.__model.openWindow(); 0");
            await WebView2TestHarness.WaitDotNetAsync(() => opened == "main", "无参命令触发 .NET");

            // 带参命令：默认 buttonEnable=false → CanExecute 门控拒绝（opened 保持 "main"）
            await win.ExecuteScriptAsync("window.__model.commandWithArg('todos'); 0");
            await Task.Delay(300);
            Assert.Equal("main", opened);

            // 启用门控源：buttonEnable 经 ModelSet 回写 .NET
            await win.ExecuteScriptAsync("window.__model.buttonEnable = true; 0");
            await WebView2TestHarness.WaitDotNetAsync(() => model.ButtonEnable, "buttonEnable 回写 .NET");

            // 带参命令现在可执行 → 参数 "todos" 传回 .NET → OpenRequested("todos")
            await win.ExecuteScriptAsync("window.__model.commandWithArg('todos'); 0");
            await WebView2TestHarness.WaitDotNetAsync(() => opened == "todos", "带参命令触发 .NET");
        }, Timeout);
    }

    [Fact]
    public async Task DemoWindow_MultiModelHost_RoutesRegisteredInstance_WhenNotCurrent()
    {
        var demo = new DemoModel();
        string? switched = null;
        demo.SwitchRequested += name => switched = name; // 路由目标：已注册、非当前实例

        await WebView2TestHarness.RunWindowAsync("demo", "综合演示", demo, async win =>
        {
            // 综合页测试模式 ?model=demo：绑定 DemoModel 并捕获注入实例 id
            await WebView2TestHarness.WaitJsAsync(win, "window.__model.switchModel !== undefined", "demo 命令就绪");

            // 关键：注册实例 + 当前 Model 置空（置空不推快照 → 桥保持 demo 实例 id 不变）
            win.Window.RegisterModel(demo);
            win.Model = null;

            // switchModel invoke 带 demo 实例 id（已注册但不再是当前 Model）→ 必须经 _routedModels 路由回 demo，
            // 否则实例守卫会把它当旧实例在途消息丢弃 → tab 切换机制失效（合并窗口最高风险链路）
            await win.ExecuteScriptAsync("window.__model.switchModel('todos'); 0");
            await WebView2TestHarness.WaitDotNetAsync(() => switched == "todos", "已注册非当前实例命令经路由执行");
        }, Timeout);
    }

    [Fact]
    public async Task DemoPage_ManualMode_TabBar_CommandsRouteAndShowModelData()
    {
        // 人工模式（无 ?model query）：demoModel 绑定后须先捕获 .NET 实例 id 再切首页。
        // 若同步 init 立刻重绑覆盖 demoModel 的 onReceive → 其 invoke 不带实例 id →
        // tab 切换 / openMulti 命令路由到当前模型而非 demo 实例（合并窗口用户报告「多窗口和
        // 平台特性用不了、嵌套窗口打不开」的根因）。本测试驱动真实 tab 栏验证路由。
        var demo = new DemoModel();
        var launcher = new LauncherModel();
        var platform = new PlatformModel();
        var multi = new MultiWindowModel("共享实例");

        await StaThreadPump.Instance.RunAsync(async () =>
        {
            var win = new TestWindow("demo", "综合演示"); // 无 selector → 人工模式
            try
            {
                win.Model = demo; // 当前模型 = demo，首张快照携带 demo 实例 id
                win.Window.RegisterModel(demo);
                win.Window.RegisterModel(launcher);
                win.Window.RegisterModel(platform);
                win.Window.RegisterModel(multi);

                demo.SwitchRequested += name =>
                {
                    switch (name)
                    {
                        case "launcher": win.Model = launcher; break;
                        case "platform": win.Model = platform; break;
                        case "multi": win.Model = multi; break;
                    }
                };
                bool multiOpened = false;
                demo.MultiRequested += () => multiOpened = true;

                var nav = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                win.Loaded += (_, _) => nav.TrySetResult(true);
                win.Show();
                await nav.Task.WaitAsync(Timeout);

                await WebView2TestHarness.WaitJsAsync(win, "typeof window.__model === 'object'", "桥接就绪");
                // 目录 tab 就绪（= demoModel 已捕获实例 id 后 switchTo('launcher') 完成）
                await WebView2TestHarness.WaitJsAsync(win, "typeof window.__model.request === 'string'", "目录 tab 就绪");

                // 关键断言：tab 栏切「平台特性」→ switchModel 带 demo 实例 id 路由到 demo →
                // win.Model=platform → 快照 → 前端显示 .NET 实例默认数据（路由正确；否则空快照）
                await win.ExecuteScriptAsync("[...document.querySelectorAll('.tab')].find(b => b.textContent === '平台特性').click(); 0");
                await WebView2TestHarness.WaitJsAsync(win,
                    "window.__model.notificationText === '来自 WebWindowUI 的系统通知'",
                    "tab 栏切平台特性显示 .NET 实例数据");

                // 平台命令（platformAction）路由到已注册实例执行
                string? platformAction = null;
                platform.PlatformRequested += a => platformAction = a;
                await win.ExecuteScriptAsync("window.__model.platformAction('notify'); 0");
                await WebView2TestHarness.WaitDotNetAsync(() => platformAction == "notify", "平台命令路由执行");

                // 切「多窗口」→ 共享实例数据到达（tab 栏路由再次验证）
                await win.ExecuteScriptAsync("[...document.querySelectorAll('.tab')].find(b => b.textContent === '多窗口').click(); 0");
                await WebView2TestHarness.WaitJsAsync(win,
                    "window.__model.instanceId === '共享实例'",
                    "多窗口 tab 显示共享实例数据");

                // openMulti 命令（demoModel 非当前绑定实例）→ 路由到 demo 实例
                await win.ExecuteScriptAsync("[...document.querySelectorAll('button')].find(b => b.textContent.includes('打开多窗口共享演示')).click(); 0");
                await WebView2TestHarness.WaitDotNetAsync(() => multiOpened, "openMulti 路由到 demo 实例");
            }
            finally
            {
                win.Close();
            }
        });
    }

    [Fact]
    public async Task DemoWindow_PlatformActions_Execute()
    {
        // 真实 DemoWindow（headless）端到端：平台 tab 按钮 → platformAction 命令路由到
        // _platformModel → OnPlatformAction 执行真实 IPlatform 调用 → 状态属性增量推送前端。
        // 从 JS 断言 trayVisible / lastEvent，证明「创建托盘/隐藏托盘/复制」整条链在真实控制器里可用
        // （用户报告平台特性全断：旧窗口的托盘/对话框按钮在合并后由本 tab 承接，路由修复后仍须验证执行）。
        await StaThreadPump.Instance.RunAsync(async () =>
        {
            var demo = new DemoWindow(headless: true);
            try
            {
                var demoWindow = demo.Window;
                var nav = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                demoWindow.Loaded += (_, _) => nav.TrySetResult(true);
                demoWindow.Show();
                await nav.Task.WaitAsync(Timeout);

                await WebView2TestHarness.WaitJsAsync(demoWindow, "typeof window.__model === 'object'", "桥接就绪");
                await WebView2TestHarness.WaitJsAsync(demoWindow, "typeof window.__model.request === 'string'", "目录 tab 就绪");

                // 平台 tab（真实人工模式路由）
                await demoWindow.ExecuteScriptAsync("[...document.querySelectorAll('.tab')].find(b => b.textContent === '平台特性').click(); 0");
                await WebView2TestHarness.WaitJsAsync(demoWindow, "window.__model.trayVisible === false", "平台 tab 就绪");

                // 创建托盘 → OnPlatformAction('create-tray') → CreateTray → TrayVisible=true + LastEvent 回推
                await demoWindow.ExecuteScriptAsync("[...document.querySelectorAll('button')].find(b => b.textContent === '创建托盘').click(); 0");
                await WebView2TestHarness.WaitJsAsync(demoWindow, "window.__model.trayVisible === true", "创建托盘→trayVisible true");
                await WebView2TestHarness.WaitJsAsync(demoWindow, "window.__model.lastEvent.includes('托盘已创建')", "创建托盘→LastEvent");

                // 隐藏托盘（toggle-tray）→ TrayVisible=false（按钮文本含插值空白，须 trim）
                await demoWindow.ExecuteScriptAsync("(function(){var b=[...document.querySelectorAll('button')].find(b=>b.textContent.trim()==='隐藏托盘'); if(b) b.click(); return b?1:0;})(); 0");
                await Task.Delay(2000);
                string dump = await demoWindow.ExecuteScriptAsync(
                    "JSON.stringify({trayVisible: window.__model.trayVisible, lastEvent: window.__model.lastEvent})");
                Assert.Fail($"DIAG after toggle-click: {dump}");
                await WebView2TestHarness.WaitJsAsync(demoWindow, "window.__model.trayVisible === false", "隐藏托盘→trayVisible false");

                // 复制（剪贴板内容为空）→ LastEvent 提示
                await demoWindow.ExecuteScriptAsync("[...document.querySelectorAll('button')].find(b => b.textContent === '复制到剪贴板').click(); 0");
                await WebView2TestHarness.WaitJsAsync(demoWindow, "window.__model.lastEvent.includes('剪贴板：内容为空')", "复制空内容→LastEvent");
            }
            finally
            {
                demo.Window.Close(null);
            }
        });
    }

    [Fact]
    public async Task TodoList_TypedList_Bidirectional()
    {
        var model = new TodoListModel
        {
            Todos =
            {
                new TodoItemModel { Title = "t1", Done = true },
                new TodoItemModel { Title = "t2", Done = false },
            },
        };

        await WebView2TestHarness.RunWindowAsync("todos", "待办列表", model, async win =>
        {
            // typed repeated：快照里 todos 是强类型数组，逐元素字段可读（一一对应）
            await WebView2TestHarness.WaitJsAsync(
                win,
                "window.__model.todos.length === 2 && window.__model.todos[0].title === \"t1\" && window.__model.todos[0].done === true",
                "快照 typed todos");
            await WebView2TestHarness.WaitJsAsync(
                win,
                "window.__model.todos[1].title === \"t2\" && window.__model.todos[1].done === false",
                "快照 todos[1]");

            // 快照来源的元素带 _modelInstanceId（生成器合成字段 → 桥抽出为不可枚举），与 .NET ModelInstanceId 一一对应
            var item = model.Todos[0];
            var itemId = item.ModelInstanceId;
            await WebView2TestHarness.WaitJsAsync(
                win,
                $"window.__model.todos[0]._modelInstanceId === {itemId}",
                "快照元素 _modelInstanceId 对应 .NET");

            // .NET 追加：ObservableCollection.Add 触发 CollectionChanged → 框架发 Insert 差量补丁，前端原地 splice（无需整列推送）
            var added = new TodoItemModel { Title = "t3", Done = false };
            model.Todos.Add(added);
            await WebView2TestHarness.WaitJsAsync(win, "window.__model.todos.length === 3 && window.__model.todos[2].title === \"t3\"", ".NET 追加 t3");
            await WebView2TestHarness.WaitJsAsync(
                win,
                $"window.__model.todos[2]._modelInstanceId === {added.ModelInstanceId}",
                "Insert 补丁元素带 _modelInstanceId");

            // 前端改元素字段 → 元素级 set（按 _modelInstanceId 寻址）→ .NET 原地写、元素实例保持
            await win.ExecuteScriptAsync("window.__model.todos[0].title = 'renamed'; 0");
            await WebView2TestHarness.WaitDotNetAsync(() => model.Todos[0].Title == "renamed", ".NET todos[0] 被前端回写");
            Assert.True(ReferenceEquals(model.Todos[0], item)); // 元素级写回保持实例（旧整列重建已不复存在）
            Assert.True(model.Todos[0].Done); // 未动字段保持

            // .NET 直接改元素属性 → 逐元素推送（ElementSet 补丁）→ 前端该项跟随、实例不重建
            model.Todos[0].Done = false;
            await WebView2TestHarness.WaitJsAsync(
                win,
                $"window.__model.todos[0].done === false && window.__model.todos[0]._modelInstanceId === {itemId}",
                ".NET 改元素属性逐项推送");
            Assert.True(ReferenceEquals(model.Todos[0], item));

            // 非快照来源的元素（Insert 补丁新增）也能被逐项编辑 → .NET 命中（覆盖补丁路径寻址）
            await win.ExecuteScriptAsync("window.__model.todos[2].title = 't3-edited'; 0");
            await WebView2TestHarness.WaitDotNetAsync(() => model.Todos[2].Title == "t3-edited", ".NET todos[2]（Insert 来源）被前端回写");
            Assert.True(ReferenceEquals(model.Todos[2], added));

            // .NET 删除：RemoveAt → Remove 差量补丁 → 前端数组原地 splice，不重建其余元素
            model.Todos.RemoveAt(0);
            await WebView2TestHarness.WaitJsAsync(win, "window.__model.todos.length === 2 && window.__model.todos[0].title === \"t2\"", ".NET 删除首个");
        }, Timeout);
    }

    [Fact]
    public async Task TodoList_SharedModel_ElementEdit_BroadcastsToOtherWindow()
    {
        var model = new TodoListModel
        {
            Todos =
            {
                new TodoItemModel { Title = "t1", Done = true },
                new TodoItemModel { Title = "t2", Done = false },
            },
        };

        // 双窗口绑同一模型实例（"todos" 页面），验证元素级写回后跨窗口只推被改元素、源窗口不回声。
        await WebView2TestHarness.RunTwoWindowsSharedModelAsync("todos", "编辑A", "只读B", model, async (winA, winB) =>
        {
            await WebView2TestHarness.WaitJsAsync(winA, "window.__model.todos.length === 2", "A 快照就绪");
            await WebView2TestHarness.WaitJsAsync(winB, "window.__model.todos.length === 2", "B 快照就绪");

            var item = model.Todos[0];
            var itemId = item.ModelInstanceId;

            // A 窗改元素 title → 元素级 set → B 窗该项跟随（跨窗口 ElementSet 广播）
            await winA.ExecuteScriptAsync("window.__model.todos[0].title = 'renamed'; 0");
            await WebView2TestHarness.WaitJsAsync(
                winB,
                $"window.__model.todos[0].title === \"renamed\" && window.__model.todos[0]._modelInstanceId === {itemId}",
                "B 窗该项跟随元素级广播");
            Assert.True(ReferenceEquals(model.Todos[0], item)); // 写回保持实例

            // B 窗未做任何操作：改 title 后 A 窗不回声重建（元素实例与 done 未动）
            await WebView2TestHarness.WaitJsAsync(winA, "window.__model.todos[0].done === true", "A 窗元素未被回声重建");
            Assert.True(model.Todos[0].Done);
        }, Timeout);
    }

    [Fact]
    public async Task Nested_SingleModelProperty_ReachesDom_And_Repush()
    {
        var detail = new NestedDetailModel { Name = "初始", Level = 2 };
        var model = new NestedParentModel { Title = "父窗口", Detail = detail };

        await WebView2TestHarness.RunWindowAsync("nested", "模型嵌套", model, async win =>
        {
            // 单 POCO 属性（ModelValue 兜底）：序数键 "1"=name、"2"=level 到达前端
            await WebView2TestHarness.WaitJsAsync(
                win,
                "window.__model.title === \"父窗口\" && window.__model.detail && window.__model.detail['1'] === \"初始\" && window.__model.detail['2'] === 2",
                "快照嵌套 detail（序数键）");

            // 嵌套模型内部变化（等价子窗口编辑同一实例）→ 父模型重推整个 Detail → 前端跟随
            detail.Name = "改后";
            await WebView2TestHarness.WaitJsAsync(win, "window.__model.detail['1'] === \"改后\"", "父窗口重推嵌套 Detail");
        }, Timeout);
    }

    [Fact]
    public async Task NestedDetailWindow_SameInstance_Bidirectional()
    {
        var detail = new NestedDetailModel { Name = "初始", Level = 1 };

        // 子窗口单独绑同一实例（与父窗口 NestedParentModel.Detail 共享）：强类型双向编辑
        await WebView2TestHarness.RunWindowAsync("nested-detail", "嵌套详情", detail, async win =>
        {
            await WebView2TestHarness.WaitJsAsync(win, "window.__model.name === \"初始\" && window.__model.level === 1", "子窗口强类型快照");

            await win.ExecuteScriptAsync("window.__model.name = 'from-js'; 0");
            await WebView2TestHarness.WaitDotNetAsync(() => detail.Name == "from-js", "子窗口回写同一实例");

            await win.ExecuteScriptAsync("window.__model.level = 5; 0");
            await WebView2TestHarness.WaitDotNetAsync(() => detail.Level == 5, "子窗口回写 level");
        }, Timeout);
    }

    [Fact]
    public async Task NestedListItemWindow_Tags_TypedRepeated_Bidirectional()
    {
        var item = new NestedListItemModel
        {
            Title = "t",
            Priority = 3,
            Tags = { new NestedItemTagModel { Name = "核心" } },
        };

        // 子窗口以元素为根模型：tags 是根层 typed repeated → 强类型数组，增删改全部双向
        await WebView2TestHarness.RunWindowAsync("nested-list-item", "列表项详情", item, async win =>
        {
            await WebView2TestHarness.WaitJsAsync(
                win,
                "window.__model.title === \"t\" && window.__model.tags.length === 1 && window.__model.tags[0].name === \"核心\"",
                "快照 typed tags");

            // 前端加标签 → 整列回写 .NET（序数键重建）
            await win.ExecuteScriptAsync("window.__model.tags.push({ name: '新增' }); 0");
            await WebView2TestHarness.WaitDotNetAsync(() => item.Tags.Count == 2 && item.Tags[1].Name == "新增", "前端加标签回写 .NET");

            // 前端改标签名 → .NET 更新
            await win.ExecuteScriptAsync("window.__model.tags[0].name = '改名'; 0");
            await WebView2TestHarness.WaitDotNetAsync(() => item.Tags[0].Name == "改名", "前端改标签名回写 .NET");
        }, Timeout);
    }

    [Fact]
    public async Task NestedList_Parent_Items_Snapshot_And_Repush()
    {
        var item = new NestedListItemModel
        {
            Title = "评审",
            Tags = { new NestedItemTagModel { Name = "核心" } },
            Meta = new NestedItemMetaModel { Author = "张三", Note = "备注" },
        };
        var model = new NestedListModel();
        model.Items.Add(item);

        await WebView2TestHarness.RunWindowAsync("nested-list", "List嵌套", model, async win =>
        {
            // 全量快照：typed items 命名键元素；内层嵌套 tags 在快照里可直接读 tag.name
            await WebView2TestHarness.WaitJsAsync(
                win,
                "window.__model.items.length === 1 && window.__model.items[0].title === \"评审\" && window.__model.items[0].tags[0].name === \"核心\"",
                "快照 items + 嵌套 tags");

            // .NET 改元素（等价子窗口编辑同一实例）→ 父模型重推整个 Items → 前端跟随
            item.Title = "改后";
            await WebView2TestHarness.WaitJsAsync(win, "window.__model.items[0].title === \"改后\"", "父窗口重推 Items");

            // 元素内层 tags 增删（等价子窗口在根层改）→ 父模型重推
            item.Tags.Add(new NestedItemTagModel { Name = "新增" });
            await WebView2TestHarness.WaitJsAsync(win, "window.__model.items[0].tags.length === 2", "嵌套 tags 增推");
        }, Timeout);
    }

    [Fact]
    public async Task ObservableDictionary_DotNetMutation_AutoPushesToFrontend()
    {
        // 字典原地改（dict[k]=v / Add）像 ObservableCollection 一样自动推前端：ObservableDictionary 抛
        // CollectionChanged → 框架整属性重推 → 前端对象整体替换。
        var model = new NestedListModel();
        Assert.Equal(3, model.Counts["items"]);

        await WebView2TestHarness.RunWindowAsync("nested-list", "List嵌套", model, async win =>
        {
            await WebView2TestHarness.WaitJsAsync(
                win,
                "window.__model.counts && window.__model.counts.items === 3 && window.__model.counts.tags === 4",
                "快照 counts");

            // .NET 侧覆盖已有键 → 自动推前端
            model.Counts["items"] = 99;
            await WebView2TestHarness.WaitJsAsync(win, "window.__model.counts.items === 99", ".NET 原地改 dict[k] 自动推送");

            // .NET 侧新增键 → 自动推前端
            model.Counts["extra"] = 5;
            await WebView2TestHarness.WaitJsAsync(win, "window.__model.counts.extra === 5", ".NET Add 新键自动推送");
        }, Timeout);
    }

    [Fact]
    public async Task ObservableDictionary_FrontendEdit_WritesBackToDotNet()
    {
        var model = new NestedListModel();

        await WebView2TestHarness.RunWindowAsync("nested-list", "List嵌套", model, async win =>
        {
            await WebView2TestHarness.WaitJsAsync(win, "window.__model.counts && window.__model.counts.items === 3", "就绪 counts");

            // 前端原地改字典值 → 深 watch 整字典 name 键回写 .NET（整属性重建）
            await win.ExecuteScriptAsync("window.__model.counts.items = 7; 0");
            await WebView2TestHarness.WaitDotNetAsync(() => model.Counts["items"] == 7, ".NET Counts[items] 被前端回写");
            Assert.Equal(4, model.Counts["tags"]); // 未动键保持

            // 前端新增键 → .NET 出现
            await win.ExecuteScriptAsync("window.__model.counts.extra = 2; 0");
            await WebView2TestHarness.WaitDotNetAsync(() => model.Counts.TryGetValue("extra", out int v) && v == 2, ".NET Counts[extra] 前端新增回写");
        }, Timeout);
    }

    [Fact]
    public async Task GetOnlyCollection_FrontendPush_ReachesDotNet()
    {
        // 显式 get-only ObservableCollection（不加 [ObservableProperty]）：前端整列回写 → 生成器原地
        // 清空重建（保留实例）→ .NET 拿到新元素。
        var item = new NestedListItemModel { Title = "评审", Priority = 3 };
        var model = new NestedListModel();
        model.Items.Add(item);

        await WebView2TestHarness.RunWindowAsync("nested-list", "List嵌套", model, async win =>
        {
            await WebView2TestHarness.WaitJsAsync(win, "window.__model.items.length === 1", "快照 items");

            // 全字段 push（typed repeated 对 bool/值类型缺字段拒写）：title=1 done=2 priority=3 tags=4 meta=5
            await win.ExecuteScriptAsync(
                "window.__model.items.push({ title: 'js', done: false, priority: 2, tags: [], meta: {} }); 0");
            await WebView2TestHarness.WaitDotNetAsync(
                () => model.Items.Count == 2 && model.Items[1].Title == "js" && !model.Items[1].Done && model.Items[1].Priority == 2,
                ".NET Items 前端整列回写重建");
        }, Timeout);
    }
}

