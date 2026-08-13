using WebWindowUI.Core;

namespace WebWindowUI.Sample;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Console.WriteLine("示例工程启动");

        WebWindowResource.RegisterCustomRoute("bin", new DataProvider());

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

        WebWindowUIPlatform.Run();
    }
}
