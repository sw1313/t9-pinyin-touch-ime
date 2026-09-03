using Microsoft.Win32;
using T9Pane.Native;

namespace T9Pane.Services;

/// <summary>
/// 握着 T9 时把系统「显示触摸键盘」改成从不；松手时写回原值。
/// 备份写在同一 TabletTip 键下，卸载程序杀进程后也能恢复。
/// </summary>
internal sealed class OfficialTouchKeyboardGuard : IDisposable
{
    internal const string TabletTipPath = @"Software\Microsoft\TabletTip\1.7";
    internal const string EnableName = "EnableDesktopModeAutoInvoke";
    internal const string TapName = "TouchKeyboardTapInvoke";
    internal const string HeldName = "T9Pane.Backup.Active";
    internal const string HadEnableName = "T9Pane.Backup.HadEnableDesktopModeAutoInvoke";
    internal const string EnableBackupName = "T9Pane.Backup.EnableDesktopModeAutoInvoke";
    internal const string HadTapName = "T9Pane.Backup.HadTouchKeyboardTapInvoke";
    internal const string TapBackupName = "T9Pane.Backup.TouchKeyboardTapInvoke";

    private readonly InputPaneMonitor _monitor = new();
    private bool _holding;
    private bool _started;

    public OfficialTouchKeyboardGuard()
    {
        _monitor.Changed += snapshot =>
        {
            if (_holding && snapshot.Visible)
            {
                SipSuppressor.HideOfficial();
            }
        };
    }

    public void Sync(bool suppress)
    {
        if (suppress)
        {
            Hold();
            if (!_started)
            {
                _monitor.Start();
                _started = true;
            }

            _monitor.PollNow();
            if (_monitor.Current.Visible)
            {
                SipSuppressor.HideOfficial();
            }

            return;
        }

        if (_started)
        {
            _monitor.Stop();
            _started = false;
        }

        Restore();
    }

    public void Dispose()
    {
        _monitor.Dispose();
        Restore();
    }

    private void Hold()
    {
        if (_holding)
        {
            return;
        }

        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(TabletTipPath);
            if (key is null)
            {
                return;
            }

            var already = ReadDword(key, HeldName) == 1;
            var existing = ReadBackup(key);
            var hadEnable = TryReadDword(key, EnableName, out var enable);
            var hadTap = TryReadDword(key, TapName, out var tap);
            var backup = OfficialTouchKeyboardPolicy.CaptureBackup(
                already,
                existing,
                hadEnable,
                enable,
                hadTap,
                tap);
            WriteBackup(key, backup);
            key.SetValue(TapName, OfficialTouchKeyboardPolicy.Never, RegistryValueKind.DWord);
            if (OfficialTouchKeyboardPolicy.ShouldWriteLegacyAutoInvoke(hadEnable))
            {
                key.SetValue(EnableName, OfficialTouchKeyboardPolicy.Never, RegistryValueKind.DWord);
            }

            if (!_holding)
            {
                NotifyTabletTip();
                Log.Info("已把系统「显示触摸键盘」改成从不（切走 T9 后恢复）");
            }

            _holding = true;
        }
        catch (Exception ex)
        {
            Log.Warn($"写入触摸键盘设置失败: {ex.Message}");
        }
    }

    private void Restore()
    {
        if (!_holding && ReadHeld() != 1)
        {
            return;
        }

        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(TabletTipPath);
            if (key is null)
            {
                _holding = false;
                return;
            }

            if (ReadDword(key, HeldName) != 1)
            {
                _holding = false;
                return;
            }

            var backup = ReadBackup(key);
            RestoreDword(key, TapName, backup.HadTouchKeyboardTapInvoke, backup.TouchKeyboardTapInvoke);
            RestoreDword(key, EnableName, backup.HadEnableDesktopModeAutoInvoke, backup.EnableDesktopModeAutoInvoke);
            foreach (var name in new[] { HeldName, HadEnableName, EnableBackupName, HadTapName, TapBackupName })
            {
                key.DeleteValue(name, false);
            }

            NotifyTabletTip();
            _holding = false;
            Log.Info("已恢复系统「显示触摸键盘」原设置");
        }
        catch (Exception ex)
        {
            Log.Warn($"恢复触摸键盘设置失败: {ex.Message}");
        }
    }

    private static int ReadHeld()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(TabletTipPath);
            return key is null ? 0 : ReadDword(key, HeldName);
        }
        catch
        {
            return 0;
        }
    }

    private static TabletTipBackup ReadBackup(RegistryKey key) =>
        new(
            ReadDword(key, HeldName) == 1,
            ReadDword(key, HadEnableName) == 1,
            ReadDword(key, EnableBackupName),
            ReadDword(key, HadTapName) == 1,
            ReadDword(key, TapBackupName));

    private static void WriteBackup(RegistryKey key, TabletTipBackup backup)
    {
        key.SetValue(HeldName, 1, RegistryValueKind.DWord);
        key.SetValue(HadEnableName, backup.HadEnableDesktopModeAutoInvoke ? 1 : 0, RegistryValueKind.DWord);
        key.SetValue(EnableBackupName, backup.EnableDesktopModeAutoInvoke, RegistryValueKind.DWord);
        key.SetValue(HadTapName, backup.HadTouchKeyboardTapInvoke ? 1 : 0, RegistryValueKind.DWord);
        key.SetValue(TapBackupName, backup.TouchKeyboardTapInvoke, RegistryValueKind.DWord);
    }

    private static void RestoreDword(RegistryKey key, string name, bool had, int value)
    {
        if (had)
        {
            key.SetValue(name, value, RegistryValueKind.DWord);
            return;
        }

        key.DeleteValue(name, false);
    }

    private static bool TryReadDword(RegistryKey key, string name, out int value)
    {
        value = 0;
        if (key.GetValue(name) is not int raw)
        {
            return false;
        }

        value = raw;
        return true;
    }

    private static int ReadDword(RegistryKey key, string name) =>
        key.GetValue(name) is int raw ? raw : 0;

    internal static void NotifyTabletTip()
    {
        NativeMethods.SendMessageTimeout(
            NativeMethods.HwndBroadcast,
            NativeMethods.WmSettingChange,
            IntPtr.Zero,
            IntPtr.Zero,
            NativeMethods.SmtoAbortIfHung,
            800,
            out _);
    }
}
