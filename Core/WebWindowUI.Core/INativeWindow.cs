using System.Drawing;

namespace WebWindowUI.Core;

public interface INativeWindow
{
    event Action? Destory;
    event Action? Resize;
    IntPtr WindowHandle { get; }
    void Show();
    void Hide();
    void Close();
    void Activate();
    void SetTitle(string title);
    void SetIcon(WindowIcon icon);
    Rectangle GetSize();
}
