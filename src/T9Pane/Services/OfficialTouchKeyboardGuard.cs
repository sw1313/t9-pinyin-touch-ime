using Microsoft.Win32;
using T9Pane.Native;

namespace T9Pane.Services;

/// <summary>
/// 握着 T9 时把官方「显示触摸键盘」改成从不；切走、退出再写回原值。
/// 官方项是 TabletTip\1.7\TouchKeyboardTapInvoke（设置页改下拉框只写它）。
/// input\Settings 再写一份给新键盘。通知用同步 WM_SETTINGCHANGE。
/// </summary>
internal sealed class OfficialTouchKeyboardGuard : IDisposable
{
    internal const string TabletTipPath = OfficialTouchKeyboardPolicy.TabletTipPath;
    internal const string EnableName = "EnableDesktopModeAutoInvoke";
    internal const string TapName = "TouchKeyboardTapInvoke";
    internal const string InvocationName = OfficialTouchKeyboardPolicy.InvocationPolicyName;
    internal const string HeldName = "T9Pane.Backup.Active";
    internal const string HadEnableName = "T9Pane.Backup.HadEnableDesktopModeAutoInvoke";
    internal const string EnableBackupName = "T9Pane.Backup.EnableDesktopModeAutoInvoke";
    internal const string HadTapName = "T9Pane.Backup.HadTouchKeyboardTapInvoke";
    internal const string TapBackupName = "T9Pane.Backup.TouchKeyboardTapInvoke";
    internal const string HadInvocationName = "T9Pane.Backup.HadTouchKeyboardInvocationPolicy";
    internal const string InvocationBackupName = "T9Pane.Backup.TouchKeyboardInvocationPolicy";

    private bool _holding;

    public void Sync(bool suppress)
    {
        if (suppress)
        {
            var firstHold = !_holding;
            Hold();
            if (firstHold && _holding)
            {
                SipSuppressor.HideOfficial();
            }

            return;
        }

        Restore();
    }

    public void Dispose() => Restore();

    private void Hold()
    {
        if (_holding)
        {
            return;
        }

        try
        {
            using var tip = Registry.CurrentUser.CreateSubKey(OfficialTouchKeyboardPolicy.TabletTipPath);
            using var input = Registry.CurrentUser.CreateSubKey(OfficialTouchKeyboardPolicy.InputSettingsPath);
            if (tip is null)
            {
                return;
            }

            var already = ReadDword(tip, HeldName) == 1;
            var existing = ReadBackup(tip);
            var hadTipEnable = TryReadDword(tip, EnableName, out var tipEnable);
            var hadTipTap = TryReadDword(tip, TapName, out var tipTap);
            var inputEnable = 0;
            var inputTap = 0;
            var hadInputEnable = input is not null && TryReadDword(input, EnableName, out inputEnable);
            var hadInputTap = input is not null && TryReadDword(input, TapName, out inputTap);

            var (hadEnable, enable) = OfficialTouchKeyboardPolicy.PreferUserValue(
                hadInputEnable,
                inputEnable,
                hadTipEnable,
                tipEnable);
            var (hadTap, tap) = OfficialTouchKeyboardPolicy.PreferOfficialTap(
                hadTipTap,
                tipTap,
                hadInputTap,
                inputTap);
            var invocation = 0;
            var hadInvocation = input is not null && TryReadDword(input, InvocationName, out invocation);
            var backup = OfficialTouchKeyboardPolicy.CaptureBackup(
                already,
                existing,
                hadEnable,
                enable,
                hadTap,
                tap,
                hadInvocation,
                invocation);
            WriteBackup(tip, backup);
            WriteNever(tip, writeInvocationPolicy: false);
            if (input is not null)
            {
                WriteNever(input, writeInvocationPolicy: true);
            }

            NotifyHosts();
            Log.Info(
                "已把系统「显示触摸键盘」改成从不"
                + $"（官方 Tap={ReadDword(tip, TapName)}）");
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
            using var tip = Registry.CurrentUser.CreateSubKey(OfficialTouchKeyboardPolicy.TabletTipPath);
            using var input = Registry.CurrentUser.CreateSubKey(OfficialTouchKeyboardPolicy.InputSettingsPath);
            if (tip is null)
            {
                _holding = false;
                return;
            }

            if (ReadDword(tip, HeldName) != 1)
            {
                _holding = false;
                return;
            }

            var backup = ReadBackup(tip);
            RestoreDword(tip, TapName, backup.HadTouchKeyboardTapInvoke, backup.TouchKeyboardTapInvoke);
            RestoreDword(tip, EnableName, backup.HadEnableDesktopModeAutoInvoke, backup.EnableDesktopModeAutoInvoke);
            if (input is not null)
            {
                RestoreDword(input, TapName, backup.HadTouchKeyboardTapInvoke, backup.TouchKeyboardTapInvoke);
                RestoreDword(input, EnableName, backup.HadEnableDesktopModeAutoInvoke, backup.EnableDesktopModeAutoInvoke);
                RestoreDword(
                    input,
                    InvocationName,
                    backup.HadTouchKeyboardInvocationPolicy,
                    backup.TouchKeyboardInvocationPolicy);
            }

            foreach (var name in new[]
                     {
                         HeldName,
                         HadEnableName,
                         EnableBackupName,
                         HadTapName,
                         TapBackupName,
                         HadInvocationName,
                         InvocationBackupName
                     })
            {
                tip.DeleteValue(name, false);
            }

            NotifyHosts();
            _holding = false;
            Log.Info("已恢复系统「显示触摸键盘」原设置");
        }
        catch (Exception ex)
        {
            Log.Warn($"恢复触摸键盘设置失败: {ex.Message}");
        }
    }

    private static void WriteNever(RegistryKey key, bool writeInvocationPolicy)
    {
        key.SetValue(TapName, OfficialTouchKeyboardPolicy.Never, RegistryValueKind.DWord);
        key.SetValue(EnableName, OfficialTouchKeyboardPolicy.Never, RegistryValueKind.DWord);
        if (writeInvocationPolicy)
        {
            key.SetValue(InvocationName, OfficialTouchKeyboardPolicy.Never, RegistryValueKind.DWord);
        }
    }

    private static int ReadHeld()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(OfficialTouchKeyboardPolicy.TabletTipPath);
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
            ReadDword(key, TapBackupName),
            ReadDword(key, HadInvocationName) == 1,
            ReadDword(key, InvocationBackupName));

    private static void WriteBackup(RegistryKey key, TabletTipBackup backup)
    {
        key.SetValue(HeldName, 1, RegistryValueKind.DWord);
        key.SetValue(HadEnableName, backup.HadEnableDesktopModeAutoInvoke ? 1 : 0, RegistryValueKind.DWord);
        key.SetValue(EnableBackupName, backup.EnableDesktopModeAutoInvoke, RegistryValueKind.DWord);
        key.SetValue(HadTapName, backup.HadTouchKeyboardTapInvoke ? 1 : 0, RegistryValueKind.DWord);
        key.SetValue(TapBackupName, backup.TouchKeyboardTapInvoke, RegistryValueKind.DWord);
        key.SetValue(HadInvocationName, backup.HadTouchKeyboardInvocationPolicy ? 1 : 0, RegistryValueKind.DWord);
        key.SetValue(InvocationBackupName, backup.TouchKeyboardInvocationPolicy, RegistryValueKind.DWord);
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
        switch (key.GetValue(name))
        {
            case int raw:
                value = raw;
                return true;
            case uint unsigned:
                value = unchecked((int)unsigned);
                return true;
            default:
                return false;
        }
    }

    private static int ReadDword(RegistryKey key, string name) =>
        TryReadDword(key, name, out var raw) ? raw : 0;

    internal static void NotifyHosts()
    {
        // 老 TabTip 只认同步广播，lParam 必须为空。PostMessage / SendNotifyMessage 不刷新。
        NativeMethods.SendMessageTimeout(
            NativeMethods.HwndBroadcast,
            NativeMethods.WmSettingChange,
            IntPtr.Zero,
            IntPtr.Zero,
            NativeMethods.SmtoAbortIfHung,
            200,
            out _);
        // 26200 设置页还盯着 input\Settings 这条路径。
        NativeMethods.SendMessageTimeout(
            NativeMethods.HwndBroadcast,
            NativeMethods.WmSettingChange,
            IntPtr.Zero,
            @"Software\Microsoft\Input\Settings",
            NativeMethods.SmtoAbortIfHung,
            200,
            out _);
    }
}
