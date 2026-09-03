using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace T9Pane.Services;

internal static class UiAccessInstall
{
    public static string InstallDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "T9Pane");

    public static string InstalledExe => Path.Combine(InstallDirectory, "T9Pane.exe");

    public static bool IsRunningFromInstall =>
        string.Equals(
            Path.GetFullPath(AppContext.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar),
            Path.GetFullPath(InstallDirectory).TrimEnd(Path.DirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);

    public static bool TryHandoffToInstalled()
    {
        if (UiAccessToken.Has() || IsRunningFromInstall || !File.Exists(InstalledExe))
        {
            return false;
        }

        Log.Info("已安装高层副本，切换到 Program Files 进程");
        Process.Start(new ProcessStartInfo(InstalledExe) { UseShellExecute = true });
        return true;
    }

    public static bool RequestElevatedInstall()
    {
        var script = Path.Combine(AppContext.BaseDirectory, "Tools", "Install-UiAccess.ps1");
        var manifest = Path.Combine(AppContext.BaseDirectory, "app.uia.manifest");
        if (!File.Exists(script) || !File.Exists(manifest))
        {
            Log.Error($"找不到安装脚本或清单：{script}");
            return false;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "pwsh.exe",
                Arguments =
                    $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\" -Source \"{AppContext.BaseDirectory.TrimEnd('\\')}\" -Manifest \"{manifest}\" -WaitForPid {Environment.ProcessId}",
                UseShellExecute = true,
                Verb = "runas"
            });
            Log.Info("已请求管理员安装高层权限，请在 UAC 点“是”");
            return true;
        }
        catch (Win32Exception)
        {
            Log.Warn("用户取消了 UAC，无法安装高层权限");
            return false;
        }
    }
}
