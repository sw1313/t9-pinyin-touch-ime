namespace T9Pane.Services;

internal readonly record struct TabletTipBackup(
    bool Held,
    bool HadEnableDesktopModeAutoInvoke,
    int EnableDesktopModeAutoInvoke,
    bool HadTouchKeyboardTapInvoke,
    int TouchKeyboardTapInvoke);

/// <summary>
/// 切到 T9 时把「显示触摸键盘」改成「从不」；切走、退出、卸载再写回原值。
/// Win11 对应设置 → 时间和语言 → 输入 → 触摸键盘。
/// </summary>
internal static class OfficialTouchKeyboardPolicy
{
    public const int Never = 0;
    public const int WhenNoKeyboardAttached = 1;
    public const int Always = 2;

    public static bool ShouldSuppress(bool paneEnabled, bool t9Selected) =>
        paneEnabled && t9Selected;

    /// <summary>
    /// 已经握着备份时，不能把当前的「从不」当成用户原值再存一遍。
    /// </summary>
    public static TabletTipBackup CaptureBackup(
        bool alreadyHeld,
        TabletTipBackup existing,
        bool hadEnable,
        int enable,
        bool hadTap,
        int tap)
    {
        if (alreadyHeld)
        {
            return existing with { Held = true };
        }

        return new TabletTipBackup(true, hadEnable, enable, hadTap, tap);
    }

    public static bool ShouldWriteLegacyAutoInvoke(bool legacyExists) => legacyExists;
}
