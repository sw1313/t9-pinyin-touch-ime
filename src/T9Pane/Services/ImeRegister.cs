using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace T9Pane.Services;

internal static class ImeRegister
{
    public const string TipId = "0804:{A7E91C20-4B3D-4F18-9C2A-1B8E6D0A1001}{A7E91C20-4B3D-4F18-9C2A-1B8E6D0A1002}";

    public static string InstallDir => AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);

    public static void PointToNewestDll()
    {
        const string clsid = "{A7E91C20-4B3D-4F18-9C2A-1B8E6D0A1001}";
        TryPoint(Path.Combine(InstallDir, "x64"), $@"Software\Classes\CLSID\{clsid}\InprocServer32");
        TryPoint(Path.Combine(InstallDir, "x86"), $@"Software\Classes\Wow6432Node\CLSID\{clsid}\InprocServer32");
    }

    private static void TryPoint(string dir, string keyPath)
    {
        var dll = NewestDll(dir);
        if (string.IsNullOrEmpty(dll) || !File.Exists(dll))
        {
            return;
        }

        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(keyPath);
            key?.SetValue(null, dll, RegistryValueKind.String);
            key?.SetValue("ThreadingModel", "Apartment", RegistryValueKind.String);
            Log.Info($"IME 指向 {dll}");
        }
        catch (Exception ex)
        {
            Log.Warn($"写入 IME 路径失败: {ex.Message}");
        }
    }

    private static string? NewestDll(string dir)
    {
        if (!Directory.Exists(dir))
        {
            return null;
        }

        var versioned = Directory.GetFiles(dir, "T9Ime.*.dll")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
        return versioned ?? Path.Combine(dir, "T9Ime.dll");
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int ClearDefaultFn();

    public static void RepairProfileEnablement()
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(
                @"Software\Microsoft\CTF\TIP\{A7E91C20-4B3D-4F18-9C2A-1B8E6D0A1001}\LanguageProfile\0x00000804\{A7E91C20-4B3D-4F18-9C2A-1B8E6D0A1002}");
            key?.DeleteValue("EnableByDefault", false);
        }
        catch (Exception ex)
        {
            Log.Warn($"清理 EnableByDefault 失败: {ex.Message}");
        }

        var dll = NewestDll(Path.Combine(InstallDir, Environment.Is64BitProcess ? "x64" : "x86"));
        if (string.IsNullOrEmpty(dll) || !File.Exists(dll))
        {
            return;
        }

        try
        {
            var lib = NativeLibrary.Load(dll);
            if (NativeLibrary.TryGetExport(lib, "T9ImeClearDefault", out var fn))
            {
                var clear = Marshal.GetDelegateForFunctionPointer<ClearDefaultFn>(fn);
                var hr = clear();
                Log.Info($"已修复 T9 配置启用状态 hr=0x{hr:X8}");
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"T9ImeClearDefault: {ex.Message}");
        }
    }

    public static bool Run(bool enable)
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\T9Pane");
        key?.SetValue("InstallDir", InstallDir);

        var ok = true;
        foreach (var arch in new[] { "x64", "x86" })
        {
            var dll = NewestDll(Path.Combine(InstallDir, arch));

            if (string.IsNullOrEmpty(dll) || !File.Exists(dll))
            {
                Log.Warn($"未找到 {Path.Combine(InstallDir, arch, "T9Ime*.dll")}");
                ok = false;
                continue;
            }

            var args = enable ? $"\"{dll}\"" : $"/u \"{dll}\"";
            var psi = new ProcessStartInfo
            {
                FileName = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                    arch == "x86"
                        ? @"SysWOW64\regsvr32.exe"
                        : @"System32\regsvr32.exe"),
                Arguments = $"/s {args}",
                UseShellExecute = true,
                Verb = "runas"
            };

            try
            {
                using var p = Process.Start(psi);
                p?.WaitForExit(20000);
                Log.Info($"{(enable ? "注册" : "注销")} {arch} T9Ime 退出码 {p?.ExitCode}");
                if (p?.ExitCode != 0)
                {
                    ok = false;
                }
            }
            catch (Exception ex)
            {
                Log.Error($"{arch} 注册失败: {ex.Message}");
                ok = false;
            }
        }

        if (!AddToLanguageBar(enable))
        {
            ok = false;
        }

        return ok;
    }

    public static bool AddToLanguageBar(bool enable)
    {
        try
        {
            if (!InstallLayoutOrTip(TipId, enable ? 0u : 1u))
            {
                var args = enable
                    ? $"input.dll,InstallLayoutOrTip \"{TipId}\""
                    : $"input.dll,InstallLayoutOrTip \"{TipId}\" 1";
                using var p = Process.Start(new ProcessStartInfo
                {
                    FileName = "rundll32.exe",
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                p?.WaitForExit(15000);
                Log.Info($"InstallLayoutOrTip rundll32 退出码 {p?.ExitCode}");
                return p?.ExitCode == 0;
            }

            Log.Info(enable ? "已写入输入法选择器：T9 九键" : "已从输入法选择器移除 T9 九键");
            return true;
        }
        catch (Exception ex)
        {
            Log.Error($"写入语言栏失败: {ex.Message}");
            return false;
        }
    }

    [DllImport("input.dll", CharSet = CharSet.Unicode, EntryPoint = "InstallLayoutOrTip")]
    private static extern bool InstallLayoutOrTip(string psz, uint dwFlags);
}
