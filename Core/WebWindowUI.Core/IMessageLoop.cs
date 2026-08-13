namespace WebWindowUI.Core;

public interface IMessageLoop
{
    void InitMessageLoop();
    void MessageLoop();
    bool IsUiThread();
    void RunOnUiThread(Action action);
}
