using System.ComponentModel;
using WebWindowUI.Sample.Items;

namespace WebWindowUI.Sample;

/// <summary>
/// 主窗口：演示「模型双向绑定」。对应前端 src/window/main/。
/// ownTimer=true 时每秒递增 Count、每 5 秒改写 Message，演示 .NET → 前端实时推送；
/// 前端输入框回写 Name/Extra 写回模型（双向绑定）。
/// </summary>
internal sealed class MainWindow : WebWindow
{
    private readonly Timer? _timer;

    public MainWindow(MainWindowModel model, bool ownTimer = true)
        : base("main", "主窗口", width: 800, height: 640)
    {
        Model = model;
        if (!ownTimer)
            return;

        _timer = new Timer(_ =>
        {
            model.Count++;
            if (model.Count % 5 == 0)
                model.Message = $"已运行 {model.Count} 秒";
        }, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }
}

/// <summary>
/// 待办列表窗口：演示「List&lt;Model&gt; 在 Vue 层一一对应」。对应前端 src/window/todos/。
/// 定时器每 8 秒直接 model.Todos.Add()：ObservableCollection 增删触发 CollectionChanged，
/// 框架订阅后自动整列表推送（无需整体替换列表属性）。
/// </summary>
internal sealed class TodosWindow : WebWindow
{
    private readonly Timer _timer;
    private int _autoTodo;

    public TodosWindow() : base("todos", "待办列表", width: 820, height: 640)
    {
        TodoListModel model = new();
        Model = model;

        _timer = new Timer(_ =>
        {
            model.Todos.Add(new TodoItemModel { Title = $"自动任务 {++_autoTodo}", Done = _autoTodo % 2 == 0 });
        }, null, TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(8));
    }
}

/// <summary>
/// 资源与数据通道窗口：演示 app://（UI 静态资源，WebResourceResolver）与
/// appbin://（专用数据通道，DataProvider）。对应前端 src/window/resources/，本页不绑定模型。
/// </summary>
internal sealed class ResourcesWindow : WebWindow
{
    public ResourcesWindow()
        : base("resources", "资源与数据通道", new WebWindowOptions { DataResolver = DataProvider.Resolve }, width: 900, height: 640)
    {
    }
}

/// <summary>
/// 多窗口演示窗口：演示「一个 model 给多个窗口用，互不干扰」。对应前端 src/window/multi/。
/// 同一 MultiWindowModel 实例可绑多个窗口（共享A/B），任一窗口改动全广播、其余跟随；
/// 同类不同实例（独立窗口）各走各的。ownTimer=true 时每秒递增 Count（仅共享A / 独立窗口驱动）。
/// </summary>
internal sealed class MultiWindow : WebWindow
{
    private readonly Timer? _timer;

    public MultiWindow(MultiWindowModel model, string title, bool ownTimer)
        : base("multi", title, width: 780, height: 640)
    {
        Model = model;
        if (!ownTimer)
            return;

        _timer = new Timer(_ => model.Count++, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }
}

/// <summary>设置窗口：对应前端 src/window/settings/，绑定 SettingsModel（多类型模型）。</summary>
internal sealed class SettingsWindow : WebWindow
{
    private readonly Timer _timer;

    public SettingsWindow() : base("settings", "设置", width: 900, height: 600)
    {
        SettingsModel model = new();
        Model = model;

        // 演示 .NET → 前端跨线程推送：定时器在线程池线程回调，平台层 marshal 回 UI 线程再发消息
        _timer = new Timer(_ =>
        {
            model.LastBackup = DateTime.Now;
        }, null, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(3));
    }
}

/// <summary>关于窗口：对应前端 src/window/about/，绑定 AboutModel。</summary>
internal sealed class AboutWindow : WebWindow
{
    public AboutWindow() : base("about", "关于", width: 700, height: 500)
    {
        Model = new AboutModel();
    }
}

/// <summary>
/// 模型嵌套窗口：对应前端 src/window/nested/。演示「模型里嵌套模型 + 嵌套详情子窗口」。
/// 父窗口绑定 NestedParentModel，其 Detail 是另一个 WebWindowModel 实例（单 POCO 属性 →
/// ModelValue 兜底/序数键，父窗口只读展示）；「打开嵌套详情」命令触发 OpenDetailRequested 事件，
/// 这里打开绑定同一个 Detail 实例的 NestedDetailWindow（master-detail）。子窗口编辑 Detail 内部字段
/// 时，NestedParentModel 重推整个 Detail，父窗口展示实时跟随（见 NestedParentModel.OnDetailChangedInner）。
/// </summary>
internal sealed class NestedWindow : WebWindow
{
    private readonly NestedParentModel _model;
    private NestedDetailWindow? _detailWindow;

    public NestedWindow() : base("nested", "模型嵌套窗口", width: 820, height: 640)
    {
        _model = new NestedParentModel
        {
            Title = "模型嵌套示例",
            Detail = new NestedDetailModel { Name = "初始嵌套模型", Level = 1 },
        };
        Model = _model;
        _model.OpenDetailRequested += OnOpenDetail;
        Closed += () => _model.OpenDetailRequested -= OnOpenDetail;
    }

    private void OnOpenDetail()
    {
        if (_model.Detail is null)
            return;

        // 复用未关闭的详情窗口；关闭后置空，下次点击重建（同一 Detail 实例，多窗口共享广播）。
        if (_detailWindow is null)
        {
            _detailWindow = new NestedDetailWindow(_model.Detail);
            _detailWindow.Closed += () => _detailWindow = null;
        }
        _detailWindow.Show();
        _detailWindow.Activate();
    }
}

/// <summary>
/// 嵌套详情子窗口：对应前端 src/window/nested-detail/。由 NestedWindow 打开，绑定父窗口的
/// 同一个 NestedDetailModel 实例——实例既是父窗口的嵌套属性值、又是本窗口的根模型，强类型双向编辑。
/// </summary>
internal sealed class NestedDetailWindow : WebWindow
{
    public NestedDetailWindow(NestedDetailModel model)
        : base("nested-detail", "嵌套详情", width: 640, height: 520)
    {
        Model = model;
    }
}

/// <summary>
/// 列表嵌套窗口：对应前端 src/window/nested-list/。演示「List&lt;Model&gt; 嵌套 + 列表项详情子窗口」。
/// 父窗口绑定 NestedListModel，其 Items 是 List&lt;NestedListItemModel&gt;（typed repeated），每个元素
/// 内部又嵌套 List&lt;NestedItemTagModel&gt;（Tags）与单模型 NestedItemMetaModel（Meta）。
/// 前端点元素触发 OpenItem(index) 命令 → OpenItemRequested 事件 → 这里打开绑定同一元素实例的
/// NestedListItemWindow（master-detail）。子窗口编辑元素字段/内层 Tags 后，NestedListModel 重推整个
/// Items，父窗口列表实时跟随（见 NestedListModel.OnItemChanged）。
/// </summary>
internal sealed class NestedListWindow : WebWindow
{
    private readonly NestedListModel _model;
    private readonly Dictionary<NestedListItemModel, NestedListItemWindow> _detailWindows = new();

    public NestedListWindow() : base("nested-list", "List<>嵌套窗口", width: 860, height: 660)
    {
        _model = new NestedListModel
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
        Model = _model;
        _model.OpenItemRequested += OnOpenItem;
        Closed += () => _model.OpenItemRequested -= OnOpenItem;
    }

    private void OnOpenItem(int index)
    {
        if (index < 0 || index >= _model.Items.Count)
            return;

        NestedListItemModel item = _model.Items[index];
        if (_detailWindows.TryGetValue(item, out NestedListItemWindow? win))
        {
            win.Show();
            win.Activate();
            return;
        }

        // 绑定父列表里的同一个元素实例（master-detail）；关闭后移除记录，下次可重建。
        NestedListItemWindow created = new(item);
        created.Closed += () => _detailWindows.Remove(item);
        _detailWindows[item] = created;
        created.Show();
        created.Activate();
    }
}

/// <summary>
/// 列表项详情子窗口：对应前端 src/window/nested-list-item/。由 NestedListWindow 打开，绑定父列表的
/// 同一个 NestedListItemModel 元素实例——元素既是父列表的 typed repeated 元素、又是本窗口的根模型，
/// 其内层 Tags（List&lt;NestedItemTagModel&gt;）在子窗口是根层 typed repeated → 增删改全部双向。
/// </summary>
internal sealed class NestedListItemWindow : WebWindow
{
    public NestedListItemWindow(NestedListItemModel model)
        : base("nested-list-item", "列表项详情", width: 660, height: 560)
    {
        Model = model;
    }
}

/// <summary>
/// 入口（启动器）窗口：演示「前端按钮 → .NET 命令（MVVM Command）」。对应前端 src/window/launcher/。
/// 前端按钮调用生成的 model.openWindow()/model.commandWithArg(path) → 桥发 ModelInvoke →
/// .NET 执行 [RelayCommand] 方法 → OpenRequested 事件到这里开窗；已打开的窗口去重激活，
/// 关闭后移除记录可再次打开。同时保留 LauncherModel.Request 回写通道（ModelSet 直接写属性，
/// 双向绑定回写演示）。两种通道最终都调 Open(path)。
/// </summary>
internal sealed class LauncherWindow : WebWindow
{
    private readonly Dictionary<string, WebWindow[]> _open = new();
    private readonly LauncherModel _model;

    public LauncherWindow() : base("launcher", "WebWindowUI 示例入口", width: 760, height: 640)
    {
        _model = new LauncherModel();
        Model = _model;

        _model.PropertyChanged += OnModelRequestChanged; // 回写通道：Request 变 → 开窗
        _model.OpenRequested += OnOpenRequested;         // 命令通道：ModelInvoke → 开窗
        Closed += () =>
        {
            _model.PropertyChanged -= OnModelRequestChanged;
            _model.OpenRequested -= OnOpenRequested;
        };
    }

    private void OnOpenRequested(string path) => Open(path);

    private void OnModelRequestChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(LauncherModel.Request))
            return;

        string? path = _model.Request;
        if (string.IsNullOrEmpty(path))
            return;

        // 清空命令：延迟到当前前端回写（TrySetProperty 的回声抑制窗口）结束后执行，
        // 让 Request=null 能推回前端 → 同一按钮第二次点击（值从 null 变回路径）仍会触发。
        // 线程池线程设置模型属性安全（PostMessage 按线程 id marshal 回 UI 线程）。
        Task.Run(() => _model.Request = null);

        Open(path);
    }

    private void Open(string path)
    {
        if (_open.TryGetValue(path, out WebWindow[]? existing))
        {
            foreach (WebWindow window in existing)
                window.Activate();
            return;
        }

        WebWindow[] created = path switch
        {
            "main" => [new MainWindow(new MainWindowModel())],
            "todos" => [new TodosWindow()],
            "resources" => [new ResourcesWindow()],
            "multi" => CreateMultiGroup(),
            "settings" => [new SettingsWindow()],
            "about" => [new AboutWindow()],
            "nested" => [new NestedWindow()],
            "nested-list" => [new NestedListWindow()],
            _ => [],
        };
        if (created.Length == 0)
            return;

        _open[path] = created;
        foreach (WebWindow window in created)
        {
            using (Stream? iconStream = WebResourceResolver.Resolve("icon/app.ico"))
            {
                if (iconStream is not null)
                    window.SetIcon(WindowIcon.FromStream(iconStream));
            }
            window.Show();
            window.Closed += () => _open.Remove(path);
        }
    }

    /// <summary>多窗口共享演示一次开 3 个窗口：共享A/B 绑同一 MultiWindowModel 实例 + 独立实例。</summary>
    private static WebWindow[] CreateMultiGroup()
    {
        MultiWindowModel shared = new("共享实例");
        return
        [
            new MultiWindow(shared, "共享窗口 A（主，定时驱动）", ownTimer: true),
            new MultiWindow(shared, "共享窗口 B（同一模型）", ownTimer: false),
            new MultiWindow(new MultiWindowModel("独立实例"), "独立实例窗口", ownTimer: true),
        ];
    }
}

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // app:// 自定义协议，静态资源由类库内置的 WebResourceResolver 提供（Vue+Vite 构建产物）。
        // 每个窗口继承 WebWindow，构造时传入窗口路径；平台由类库在编译期按操作系统自动选择，
        // 这里不接触任何平台 API。
        //
        // 不再一次性启动全部窗口：只打开一个入口（launcher），按钮点击按需启动各功能子窗口——
        //   main       → 模型双向绑定（MainWindowModel：Name/Count/Message/Extra）
        //   todos      → List<Model> 在 Vue 层一一对应（TodoListModel + TodoItemModel）
        //   resources  → app:// 资源 + appbin:// 数据通道（不绑定模型）
        //   multi      → 一个 model 给多个窗口用，互不干扰（MultiWindowModel，一次开 3 个）
        //   settings   → 多类型模型 + 跨线程推送（SettingsModel）
        //   about      → 静态内容（AboutModel）
        //   nested     → 模型嵌套窗口：NestedParentModel.Detail 嵌套 NestedDetailModel，
        //                子窗口（nested-detail）绑定同一 Detail 实例（master-detail）
        //   nested-list→ List<>嵌套窗口：Items=List<NestedListItemModel>，元素内部再嵌套
        //                List<NestedItemTagModel>（Tags）与 NestedItemMetaModel（Meta），
        //                子窗口（nested-list-item）绑定同一元素实例
        LauncherWindow launcher = new();
        launcher.Show();

        // 运行当前平台的消息循环（Windows 上是 Win32），直到最后一个窗口关闭
        WebWindow.RunMessageLoop();
    }
}
