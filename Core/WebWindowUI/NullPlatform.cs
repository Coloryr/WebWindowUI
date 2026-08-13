using WebWindowUI.Core;

namespace WebWindowUI;

public class NullPlatform : IWebWindowPlatform
{
    public NullPlatform()
    {
        throw new NotImplementedException();
    }
    public IWindowBackend CreateWindow(WebWindowOptions options)
    {
        throw new NotImplementedException();
    }

    public bool IsUiThread()
    {
        throw new NotImplementedException();
    }

    public string[]? OpenFileDialog(string title, string filter, string? initialDirectory = null, bool fileMustExist = true, bool allowMultiSelect = true)
    {
        throw new NotImplementedException();
    }

    public void RunMessageLoop()
    {
        throw new NotImplementedException();
    }

    public void RunOnUiThread(Action action)
    {
        throw new NotImplementedException();
    }

    public string? SaveFileDialog(string title, string filter, string? defaultFileName = null, string? defaultExt = null)
    {
        throw new NotImplementedException();
    }

    public void ShowMessageBox(string title, string message, bool error)
    {
        throw new NotImplementedException();
    }
}
