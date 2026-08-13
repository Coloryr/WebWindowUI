using System.ComponentModel;
using WebWindowUI.Core;

namespace WebWindowUI.Sample;

/// <summary>
/// 入口（启动器）窗口（前端 src/window/launcher/）：演示「前端按钮 → .NET 命令（MVVM Command）」。
/// 命令/回写两种通道最终都调 Open(path) 开窗，已打开的窗口去重激活、关闭后移除记录。
/// </summary>
internal sealed class LauncherWindow : WebWindow
{
    private readonly Dictionary<string, WebWindow[]> _open = [];
    private readonly LauncherModel _model;

    public LauncherWindow() : base(new WebWindowOptions("launcher")
    {
        Title = "WebWindowUI 示例入口",
        Width = 760,
        Height = 640
    })
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
            "main" => [new MainWindow()],
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
            using (Stream? iconStream = WebWindowResource.Resolve("icon/app.ico"))
            {
                if (iconStream is not null)
                    window.SetIcon(WindowIcon.FromStream(iconStream));
            }
            window.Show();
            window.Closed += () => _open.Remove(path);
        }
    }

    /// <summary>
    /// 多窗口共享演示一次开 3 个窗口：共享A/B 绑同一 MultiWindowModel 实例 + 独立实例。
    /// </summary>
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
