using WebWindowUI.Core;
using WebWindowUI.Core.Platform;
using WebWindowUI.Sample.Items;

namespace WebWindowUI.Sample;

/// <summary>
/// 综合演示窗口控制器：全部功能合并进一个窗口（前端 src/window/demo/，tab 切换 + 动态换绑模型）。
/// 持有 9 个共享模型实例 + 4 个推送定时器；DemoModel 命令驱动切 Window.Model（OnSwitch），
/// 嵌套详情 / 列表项详情 / 多窗口共享作为子演示复用真实子窗口。平台特性（托盘/通知/剪贴板/对话框）
/// 由 PlatformModel 命令驱动经 IPlatform 执行。全部实例经 RegisterModel 注册——前端换绑时非当前
/// 绑定实例的在途消息按 ModelInstanceId 路由（见 WebWindow.OnBackendMessageReceived）。
/// </summary>
internal sealed class DemoWindow
{
    private readonly WebWindow _window;
    private readonly IPlatform _platform;

    private readonly DemoModel _demo = new();
    private readonly MainWindowModel _main = new();
    private readonly TodoListModel _todos = new();
    private readonly SettingsModel _settings = new();
    private readonly AboutModel _about = new();
    private readonly NestedParentModel _nested = new()
    {
        Title = "模型嵌套示例",
        Detail = new NestedDetailModel { Name = "初始嵌套模型", Level = 1 },
    };
    private readonly NestedListModel _nestedList = new()
    {
        Title = "List<>嵌套示例",
        Items =
        {
            new NestedListItemModel
            {
                Title = "设计评审",
                Priority = 1,
                Meta = new NestedItemMetaModel { Author = "张三", Note = "评审待办拆分" },
                Tags = { new NestedItemTagModel { Name = "核心" }, new NestedItemTagModel { Name = "待定" } },
            },
            new NestedListItemModel
            {
                Title = "代码审查",
                Priority = 2,
                Meta = new NestedItemMetaModel { Author = "李四", Note = "重点看写回路径" },
                Tags = { new NestedItemTagModel { Name = "后端" } },
            },
            new NestedListItemModel { Title = "文档整理", Priority = 3 },
        },
    };
    private readonly PlatformModel _platformModel = new();
    private readonly LauncherModel _launcher = new();
    private readonly MultiWindowModel _multi = new("共享实例");

    private readonly List<Timer> _timers = [];
    private readonly List<WebWindow> _multiWindows = [];
    private readonly Dictionary<NestedListItemModel, NestedListItemWindow> _detailWindows = [];
    private ITrayIcon? _tray;
    private NestedDetailWindow? _detailWindow;
    private int _autoTodo;

    /// <summary>
    /// 框架窗口（构造即创建；Show 由宿主负责）。
    /// </summary>
    public WebWindow Window => _window;

    /// <summary>
    /// 无头模式（测试用）：窗口永不显示，导航/桥接/命令照常。
    /// </summary>
    /// <param name="headless">是否无头。</param>
    public DemoWindow(bool headless = false)
    {
        _platform = WebWindowPlatform.Current;
        _window = _platform.CreateWindow(new WebWindowOptions("demo")
        {
            Title = "综合演示",
            Width = 900,
            Height = 720,
            Headless = headless,
        });
        _window.Model = _demo;

        // 注册全部模型实例：前端 tab 切换换绑时，非当前绑定实例的在途消息按实例 id 路由
        _window.RegisterModel(_demo);
        _window.RegisterModel(_main);
        _window.RegisterModel(_todos);
        _window.RegisterModel(_settings);
        _window.RegisterModel(_about);
        _window.RegisterModel(_nested);
        _window.RegisterModel(_nestedList);
        _window.RegisterModel(_platformModel);
        _window.RegisterModel(_launcher);
        _window.RegisterModel(_multi);

        _demo.SwitchRequested += OnSwitch;
        _demo.MultiRequested += OnOpenMulti;
        _nested.OpenDetailRequested += OnOpenDetail;
        _nestedList.OpenItemRequested += OnOpenItem;
        _platformModel.PlatformRequested += OnPlatformAction;
        _launcher.OpenRequested += OnLauncherOpen;

        // 推送定时器：main 1s Count++（每 5 秒改 Message）/ todos 8s 自动任务 / settings 3s LastBackup /
        // multi 共享实例 1s Count++（驱动共享窗口 A/B）
        _timers.Add(new Timer(_ => TickMain(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)));
        _timers.Add(new Timer(_ => _todos.Todos.Add(new TodoItemModel
        {
            Title = $"自动任务 {++_autoTodo}",
            Done = _autoTodo % 2 == 0,
        }), null, TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(8)));
        _timers.Add(new Timer(_ => _settings.LastBackup = DateTime.Now, null, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(3)));
        _timers.Add(new Timer(_ => _multi.Count++, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)));

        _window.Closed += (_, _) => OnClosed();
    }

    private void TickMain()
    {
        _main.Count++;
        if (_main.Count % 5 == 0)
            _main.Message = $"已运行 {_main.Count} 秒";
    }

    /// <summary>
    /// 切功能 tab：把 Window.Model 换成对应实例（前端随后绑新模型、Ready 后补快照）。
    /// "home" 回锚目录（demo 模型），"resources" 无模型不切。
    /// </summary>
    /// <param name="name">功能名（launcher/main/todos/settings/about/nested/nested-list/multi/platform）。</param>
    private void OnSwitch(string name)
    {
        WebWindowModel? target = name switch
        {
            "launcher" => _launcher,
            "main" => _main,
            "todos" => _todos,
            "settings" => _settings,
            "about" => _about,
            "nested" => _nested,
            "nested-list" => _nestedList,
            "multi" => _multi,
            "platform" => _platformModel,
            "home" => _demo,
            _ => null,
        };
        if (target is not null)
            _window.Model = target;
    }

    /// <summary>
    /// 目录 tab 的 launcher 命令开窗请求：等价于切到对应功能 tab。
    /// </summary>
    /// <param name="path">功能名。</param>
    private void OnLauncherOpen(string path) => OnSwitch(path);

    /// <summary>
    /// 打开多窗口共享演示（3 个真实窗口：共享A/B 绑共享实例 + 独立实例）；已开则激活复用。
    /// </summary>
    private void OnOpenMulti()
    {
        if (_multiWindows.Count > 0)
        {
            foreach (WebWindow window in _multiWindows)
                window.Activate();
            return;
        }

        WebWindow[] created =
        [
            new MultiWindow(_multi, "共享窗口 A（同一模型）", ownTimer: false).Window,
            new MultiWindow(_multi, "共享窗口 B（同一模型）", ownTimer: false).Window,
            new MultiWindow(new MultiWindowModel("独立实例"), "独立实例窗口", ownTimer: true).Window,
        ];
        foreach (WebWindow window in created)
        {
            _multiWindows.Add(window);
            window.Closed += (_, _) => _multiWindows.Remove(window);
            window.Show();
        }
    }

    /// <summary>
    /// 打开嵌套详情子窗口（绑定父模型同一个 Detail 实例，master-detail）；关闭后重建、复用实例。
    /// </summary>
    private void OnOpenDetail()
    {
        NestedDetailModel? detail = _nested.Detail;
        if (detail is null)
            return;

        if (_detailWindow is null)
        {
            _detailWindow = new NestedDetailWindow(detail);
            _detailWindow.Window.Closed += (_, _) => _detailWindow = null;
        }
        _detailWindow.Window.Show();
        _detailWindow.Window.Activate();
    }

    /// <summary>
    /// 打开列表项详情子窗口（绑定父列表同一元素实例，master-detail）；关闭后移除记录可重建。
    /// </summary>
    /// <param name="index">被点元素索引。</param>
    private void OnOpenItem(int index)
    {
        if (index < 0 || index >= _nestedList.Items.Count)
            return;

        NestedListItemModel item = _nestedList.Items[index];
        if (_detailWindows.TryGetValue(item, out NestedListItemWindow? win))
        {
            win.Window.Show();
            win.Window.Activate();
            return;
        }

        NestedListItemWindow created = new(item);
        created.Window.Closed += (_, _) => _detailWindows.Remove(item);
        _detailWindows[item] = created;
        created.Window.Show();
        created.Window.Activate();
    }

    /// <summary>
    /// 平台动作分派：按前端命令动作名执行对应 IPlatform 调用（命令在 UI 线程执行，对话框/GTK 调用安全）。
    /// </summary>
    /// <param name="action">动作名（create-tray / delete-tray / toggle-tray / balloon /
    /// notify / message-box / open-file / open-folder / save-file / save-folder / copy / paste）。</param>
    private void OnPlatformAction(string action)
    {
        switch (action)
        {
            case "create-tray": CreateTray(); break;
            case "delete-tray": DeleteTray(); break;
            case "toggle-tray": ToggleTray(); break;
            case "balloon": _tray?.ShowBalloon(_platformModel.BalloonTitle, _platformModel.BalloonText); break;
            case "notify": _platform.Notification.Show("WebWindowUI", _platformModel.NotificationText); break;
            case "message-box": _platform.Dialog.ShowMessageBox("WebWindowUI", "这是一个系统消息框（错误样式演示）。", error: true); break;
            case "open-file": OpenFileDialog(); break;
            case "open-folder": OpenFolderDialog(); break;
            case "save-file": SaveFileDialog(); break;
            case "save-folder": SaveFolderDialog(); break;
            case "copy": CopyClipboard(); break;
            case "paste": PasteClipboard(); break;
        }
    }

    /// <summary>
    /// 创建托盘图标（窗口图标 + 提示 + 右键菜单），订阅点击事件；重复创建幂等。
    /// </summary>
    private void CreateTray()
    {
        if (_tray is not null)
            return;

        _tray = _platform.CreateTrayIcon(_window);
        _tray.Click += OnTrayClick;
        _tray.DoubleClick += OnTrayDoubleClick;
        _tray.SetTip(_platformModel.TrayTip);

        // 右键菜单：显示/隐藏窗口 + 气泡样式子菜单 + 退出（窗口关闭时托盘自动移除）
        var show = new PopupMenu { Name = "显示窗口" };
        show.Click += () => _window.Activate();
        var hide = new PopupMenu { Name = "隐藏窗口" };
        hide.Click += () => _window.Hide();

        var balloon = new PopupMenu { Name = "气泡通知" };
        var info = new PopupMenu { Name = "信息", IsChecked = true };
        info.Click += () => _tray?.ShowBalloon(_platformModel.BalloonTitle, _platformModel.BalloonText, TrayIconType.Info);
        var warning = new PopupMenu { Name = "警告" };
        warning.Click += () => _tray?.ShowBalloon(_platformModel.BalloonTitle, _platformModel.BalloonText, TrayIconType.Warning);
        var error = new PopupMenu { Name = "错误" };
        error.Click += () => _tray?.ShowBalloon(_platformModel.BalloonTitle, _platformModel.BalloonText, TrayIconType.Error);
        balloon.Menus.AddRange([info, warning, error]);

        var quit = new PopupMenu { Name = "退出" };
        quit.Click += () => _window.Close(null);

        _tray.SetMenu(new PopupMenu
        {
            Menus = [show, hide, new() { IsSeparator = true }, balloon, new() { IsSeparator = true }, quit],
        });

        using (Stream? iconStream = WebWindowResource.Resolve("icon/app.ico"))
        {
            if (iconStream is not null)
                _tray.SetIcon(WindowIcon.FromStream(iconStream));
        }

        _platformModel.TrayVisible = true;
        _platformModel.LastEvent = "托盘已创建（右键试试菜单）";
    }

    /// <summary>
    /// 移除托盘图标（解绑事件 + Delete；窗口关闭时原生层也会自动移除，此处幂等）。
    /// </summary>
    private void DeleteTray()
    {
        if (_tray is null)
            return;
        _tray.Click -= OnTrayClick;
        _tray.DoubleClick -= OnTrayDoubleClick;
        _tray.Delete();
        _tray = null;
        _platformModel.TrayVisible = false;
        _platformModel.LastEvent = "托盘已移除";
    }

    /// <summary>
    /// 切换托盘可见性（未创建时先创建）。
    /// </summary>
    private void ToggleTray()
    {
        if (_tray is null)
        {
            CreateTray();
            return;
        }
        bool visible = !_platformModel.TrayVisible;
        _tray.SetVisible(visible);
        _platformModel.TrayVisible = visible;
        _platformModel.LastEvent = visible ? "托盘已显示" : "托盘已隐藏（图标仍在通知区占位）";
    }

    /// <summary>
    /// 托盘单击：事件携带按钮类型与屏幕坐标。
    /// </summary>
    private void OnTrayClick(TrayClickEvent evt)
        => _platformModel.LastEvent = $"托盘单击：{MapClick(evt.Type)} @ ({evt.Position.X},{evt.Position.Y})";

    /// <summary>
    /// 托盘双击（Linux GtkStatusIcon 多数桌面合并为单击，可能不触发）。
    /// </summary>
    private void OnTrayDoubleClick(TrayClickEvent evt)
        => _platformModel.LastEvent = $"托盘双击：{MapClick(evt.Type)} @ ({evt.Position.X},{evt.Position.Y})";

    /// <summary>
    /// 把托盘点击按钮类型映射为中文名。
    /// </summary>
    /// <param name="type">点击类型。</param>
    private static string MapClick(TrayClickType type) => type switch
    {
        TrayClickType.Right => "右键",
        TrayClickType.Middle => "中键",
        _ => "左键",
    };

    /// <summary>
    /// 复制模型剪贴板文本到系统剪贴板。
    /// </summary>
    private void CopyClipboard()
    {
        if (string.IsNullOrEmpty(_platformModel.ClipboardText))
        {
            _platformModel.LastEvent = "剪贴板：内容为空，未复制";
            return;
        }
        _platform.Clipboard.SetClipboardData(new ClipboardTextData { Type = ClipboardDataType.Text, Text = _platformModel.ClipboardText });
        _platformModel.LastEvent = "剪贴板：已复制文本";
    }

    /// <summary>
    /// 从系统剪贴板读文本回填模型（仅文本；无文本时提示）。
    /// </summary>
    private void PasteClipboard()
    {
        if (_platform.Clipboard.GetClipboardData() is ClipboardTextData { Text: string text })
        {
            _platformModel.ClipboardText = text;
            _platformModel.LastEvent = "剪贴板：已粘贴文本";
        }
        else
        {
            _platformModel.LastEvent = "剪贴板：无文本内容";
        }
    }

    /// <summary>
    /// 打开系统文件选择对话框（多选），结果显示到 LastEvent。
    /// </summary>
    private void OpenFileDialog()
    {
        var result = _platform.Dialog.OpenFileDialog(new SelectDialogOption
        {
            Title = "选择一个文件",
            Filter = "所有文件|*.*",
            SelectMustExist = true,
        });
        _platformModel.LastEvent = result is null or { Count: 0 }
            ? "打开文件：已取消"
            : "打开文件：" + string.Join("、", result);
    }

    /// <summary>
    /// 打开系统目录选择对话框，结果显示到 LastEvent。
    /// </summary>
    private void OpenFolderDialog()
    {
        var result = _platform.Dialog.OpenFolderDialog(new SelectDialogOption
        {
            Title = "选择一个目录",
            SelectMustExist = true,
        });
        _platformModel.LastEvent = result is null or { Count: 0 }
            ? "打开目录：已取消"
            : "打开目录：" + string.Join("、", result);
    }

    /// <summary>
    /// 打开系统目录选择对话框作为保存目标目录（无独立「保存目录」对话框，复用目录选择），结果到 LastEvent。
    /// </summary>
    private void SaveFolderDialog()
    {
        var result = _platform.Dialog.OpenFolderDialog(new SelectDialogOption
        {
            Title = "选择保存目录",
            SelectMustExist = true,
        });
        _platformModel.LastEvent = result is null or { Count: 0 }
            ? "保存目录：已取消"
            : "保存目录：" + string.Join("、", result);
    }

    /// <summary>
    /// 打开系统保存对话框，结果显示到 LastEvent。
    /// </summary>
    private void SaveFileDialog()
    {
        var result = _platform.Dialog.SaveFileDialog(new SelectDialogOption
        {
            Title = "保存文件",
            Filter = "文本文件|*.txt",
        });
        _platformModel.LastEvent = string.IsNullOrEmpty(result) ? "保存文件：已取消" : "保存文件：" + result;
    }

    /// <summary>
    /// 窗口关闭：解绑事件、释放定时器、删除托盘。
    /// </summary>
    private void OnClosed()
    {
        _demo.SwitchRequested -= OnSwitch;
        _demo.MultiRequested -= OnOpenMulti;
        _nested.OpenDetailRequested -= OnOpenDetail;
        _nestedList.OpenItemRequested -= OnOpenItem;
        _platformModel.PlatformRequested -= OnPlatformAction;
        _launcher.OpenRequested -= OnLauncherOpen;

        foreach (Timer timer in _timers)
            timer.Dispose();
        _timers.Clear();

        if (_tray is not null)
        {
            _tray.Click -= OnTrayClick;
            _tray.DoubleClick -= OnTrayDoubleClick;
            _tray.Delete();
            _tray = null;
        }
    }
}
