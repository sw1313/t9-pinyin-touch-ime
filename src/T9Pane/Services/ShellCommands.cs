using System.Runtime.InteropServices;
using T9Pane.Native;

namespace T9Pane.Services;

/// <summary>
/// 打开开始菜单、切窗口。实现来自已验证的开源任务栏，不是猜测：
/// RetroBar / ManagedShell 的 IImmersiveLauncher.ShowStartView，
/// 以及 ManagedShell.ShellHelper.ShowStartMenu 的 Win 单击。
/// </summary>
internal static class ShellCommands
{
    private static readonly Guid ClsidImmersiveShell = new("C2F03A33-21F5-47FA-B4BB-156362A2F239");
    private static readonly Guid SidImmersiveLauncher = new("6F86E01C-C649-4D61-BE23-F1322DDECA9D");
    private static readonly Guid IidImmersiveLauncher = new("D8D60399-A0F1-F987-5551-321FD1B49864");

    public static void AllowExplorerFocus()
    {
        var progman = NativeMethods.FindWindow("Progman", "Program Manager");
        if (progman == IntPtr.Zero)
        {
            return;
        }

        NativeMethods.GetWindowThreadProcessId(progman, out var pid);
        if (pid != 0)
        {
            NativeMethods.AllowSetForegroundWindow(pid);
        }
    }

    public static bool OpenStartMenu()
    {
        AllowExplorerFocus();
        TextOutput.ReleaseKey(NativeMethods.VkLWin);
        if (TryImmersiveStart())
        {
            Log.Info("开始菜单 origin=immersive-launcher");
            return true;
        }

        if (TextOutput.ClickVirtual(NativeMethods.VkLWin))
        {
            Log.Info("开始菜单 origin=win-click");
            return true;
        }

        if (TextOutput.ClickVirtual(NativeMethods.VkEscape, NativeMethods.VkControl))
        {
            Log.Info("开始菜单 origin=ctrl-esc");
            return true;
        }

        Log.Warn($"开始菜单失败 uiAccess={UiAccessToken.Has()} inputSize={Marshal.SizeOf<INPUT>()}");
        return false;
    }

    public static bool KeepAltForSwitcher(ushort vk) =>
        vk is NativeMethods.VkTab
            or NativeMethods.VkLeft
            or NativeMethods.VkRight
            or NativeMethods.VkUp
            or NativeMethods.VkDown
            or NativeMethods.VkEscape;

    private static bool TryImmersiveStart()
    {
        try
        {
            var shell = (INativeServiceProvider)Activator.CreateInstance(
                Type.GetTypeFromCLSID(ClsidImmersiveShell, throwOnError: true)!)!;
            var service = SidImmersiveLauncher;
            var iid = IidImmersiveLauncher;
            if (shell.QueryService(ref service, ref iid, out var launcherObj) != 0
                || launcherObj is not IImmersiveLauncher launcher)
            {
                return false;
            }

            return launcher.ShowStartView(
                ImmersiveLauncherShowMethod.StartButton,
                ImmersiveLauncherShowFlags.IgnoreSetForegroundError) == 0;
        }
        catch (Exception ex)
        {
            Log.Warn($"ImmersiveLauncher: {ex.Message}");
            return false;
        }
    }

    private enum ImmersiveLauncherShowMethod
    {
        StartButton = 0xB
    }

    [Flags]
    private enum ImmersiveLauncherShowFlags
    {
        IgnoreSetForegroundError = 0x4
    }

    [ComImport]
    [Guid("6D5140C1-7436-11CE-8034-00AA006009FA")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface INativeServiceProvider
    {
        [PreserveSig]
        int QueryService(ref Guid guidService, ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object? ppvObject);
    }

    [ComImport]
    [Guid("D8D60399-A0F1-F987-5551-321FD1B49864")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IImmersiveLauncher
    {
        [PreserveSig]
        int ShowStartView(ImmersiveLauncherShowMethod showMethod, ImmersiveLauncherShowFlags showFlags);
    }
}
