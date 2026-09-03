using T9Pane.Native;

namespace T9Pane.Services;

/// <summary>
/// 语言栏换 TIP 时系统发 EVENT_OBJECT_IME_CHANGE。只当触发器，真正读官方 GetActiveProfile。
/// </summary>
internal sealed class ImeLayoutWatcher : IDisposable
{
    private readonly Action _changed;
    private readonly NativeMethods.WinEventDelegate _callback;
    private readonly IntPtr _hook;

    public ImeLayoutWatcher(Action changed)
    {
        _changed = changed;
        _callback = OnEvent;
        _hook = NativeMethods.SetWinEventHook(
            NativeMethods.EventObjectImeShow,
            NativeMethods.EventObjectImeChange,
            IntPtr.Zero,
            _callback,
            0,
            0,
            NativeMethods.WineventOutofcontext | NativeMethods.WineventSkipownprocess);
        if (_hook == IntPtr.Zero)
        {
            Log.Warn("订阅 EVENT_OBJECT_IME_CHANGE 失败");
        }
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
        {
            NativeMethods.UnhookWinEvent(_hook);
        }
    }

    private void OnEvent(IntPtr hook, uint type, IntPtr hwnd, int objectId, int childId, uint thread, uint time)
    {
        _ = hook;
        _ = type;
        _ = hwnd;
        _ = objectId;
        _ = childId;
        _ = thread;
        _ = time;
        try
        {
            _changed();
        }
        catch (Exception ex)
        {
            Log.Warn($"输入法切换通知: {ex.Message}");
        }
    }
}
