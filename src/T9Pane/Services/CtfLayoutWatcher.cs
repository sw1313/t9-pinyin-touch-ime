using System.Threading;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
using T9Pane.Native;

namespace T9Pane.Services;

/// <summary>
/// 语言默认 TIP（CTF Assemblies）变化时通知。切语言栏当前 TIP 不会写这里。
/// </summary>
internal sealed class CtfLayoutWatcher : IDisposable
{
    private const string AssembliesPath = @"Software\Microsoft\CTF\Assemblies";
    private readonly Action _changed;
    private readonly CancellationTokenSource _cts = new();
    private readonly Thread _thread;

    public CtfLayoutWatcher(Action changed)
    {
        _changed = changed;
        _thread = new Thread(Watch)
        {
            IsBackground = true,
            Name = "T9-CTF-Layout"
        };
        _thread.Start();
    }

    public void Dispose()
    {
        _cts.Cancel();
        try
        {
            if (!_thread.Join(500))
            {
                // 等 RegNotify 超时后线程自己退
            }
        }
        catch
        {
            // 退出中
        }

        _cts.Dispose();
    }

    private void Watch()
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(AssembliesPath, writable: false);
            if (key is null)
            {
                return;
            }

            using var signal = new AutoResetEvent(false);
            while (!_cts.IsCancellationRequested)
            {
                var hr = NativeMethods.RegNotifyChangeKeyValue(
                    key.Handle,
                    true,
                    NativeMethods.RegNotifyChangeName | NativeMethods.RegNotifyChangeLastSet,
                    signal.SafeWaitHandle,
                    true);
                if (hr != 0)
                {
                    Log.Warn($"订阅语言栏布局失败: 0x{hr:X}");
                    return;
                }

                if (WaitHandle.WaitAny([signal, _cts.Token.WaitHandle]) != 0)
                {
                    return;
                }

                try
                {
                    _changed();
                }
                catch (Exception ex)
                {
                    Log.Warn($"语言栏布局通知: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"语言栏布局监视: {ex.Message}");
        }
    }
}
