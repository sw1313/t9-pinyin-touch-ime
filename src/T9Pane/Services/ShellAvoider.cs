using System.IO;
using T9Pane.Native;

namespace T9Pane.Services;

internal sealed class ShellAvoider : IDisposable
{
    private static readonly HashSet<string> ShellProcesses =
    [
        "searchhost",
        "searchapp",
        "searchui",
        "searchapp.desktop",
        "startmenuexperiencehost"
    ];

    private static readonly HashSet<string> SkipClasses =
    [
        "shell_traywnd", "shell_secondarytraywnd", "progman", "workerw",
        "notifyiconoverflowwindow", "t9pane", "foregroundstaging"
    ];

    private readonly Dictionary<IntPtr, NativeRect> _originals = [];
    private bool _logged;

    public static bool IsShellProcess(string processName) =>
        ShellProcesses.Contains(processName.ToLowerInvariant());

    public void Apply(NativeRect keyboard, IReadOnlyCollection<IntPtr> ignored)
    {
        if (keyboard.IsEmpty)
        {
            Restore();
            return;
        }

        var live = new HashSet<IntPtr>();
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (!TryFit(hwnd, keyboard, ignored))
            {
                return true;
            }

            live.Add(hwnd);
            return true;
        }, IntPtr.Zero);

        foreach (var hwnd in _originals.Keys.Where(x => !live.Contains(x)).ToList())
        {
            RestoreOne(hwnd);
            _originals.Remove(hwnd);
        }
    }

    public void Restore()
    {
        foreach (var hwnd in _originals.Keys.ToList())
        {
            RestoreOne(hwnd);
        }

        _originals.Clear();
    }

    public void Dispose() => Restore();

    private bool TryFit(IntPtr hwnd, NativeRect keyboard, IReadOnlyCollection<IntPtr> ignored)
    {
        if (hwnd == IntPtr.Zero || ignored.Contains(hwnd) || !NativeMethods.IsWindowVisible(hwnd))
        {
            return false;
        }

        if (NativeMethods.IsCloaked(hwnd) || !NativeMethods.GetWindowRect(hwnd, out var current) || current.IsEmpty)
        {
            return false;
        }

        var className = NativeMethods.GetWindowClass(hwnd).ToLowerInvariant();
        if (SkipClasses.Contains(className) || className.StartsWith("ime", StringComparison.Ordinal))
        {
            return false;
        }

        NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
        var process = Path.GetFileNameWithoutExtension(NativeMethods.GetProcessPath(pid)).ToLowerInvariant();
        var title = NativeMethods.GetWindowTitle(hwnd);
        var isShell = IsShellProcess(process)
                      || ((title is "搜索" or "Search") && className.Contains("corewindow", StringComparison.Ordinal));
        if (!isShell || current.Width < 240 || current.Height < 200)
        {
            return false;
        }

        if (current.Bottom <= keyboard.Top + 6)
        {
            return _originals.ContainsKey(hwnd);
        }

        if (!_originals.ContainsKey(hwnd))
        {
            _originals[hwnd] = current;
            if (!_logged)
            {
                _logged = true;
                Log.Info($"搜索框避让 {process}/{className} {current.Width}x{current.Height} → 底边 {keyboard.Top}");
            }
        }

        var top = current.Top;
        var height = keyboard.Top - top;
        if (height < 220)
        {
            NativeMethods.TryGetMonitorWork(current, out var work);
            var workTop = work.IsEmpty ? 0 : work.Top;
            top = Math.Max(workTop, keyboard.Top - Math.Max(220, _originals[hwnd].Height));
            height = keyboard.Top - top;
        }

        height = Math.Max(180, height);
        var flags = NativeMethods.SwpNoZOrder | NativeMethods.SwpNoActivate | NativeMethods.SwpAsyncWindowPos;
        if (!NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, current.Left, top, current.Width, height, (uint)flags))
        {
            NativeMethods.MoveWindow(hwnd, current.Left, top, current.Width, height, true);
        }

        return true;
    }

    private void RestoreOne(IntPtr hwnd)
    {
        if (!NativeMethods.IsWindow(hwnd) || !_originals.TryGetValue(hwnd, out var original))
        {
            return;
        }

        var flags = NativeMethods.SwpNoZOrder | NativeMethods.SwpNoActivate | NativeMethods.SwpAsyncWindowPos;
        NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, original.Left, original.Top, original.Width, original.Height, (uint)flags);
    }
}
