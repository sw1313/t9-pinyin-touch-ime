namespace T9Pane.Services;

internal readonly record struct TabletTipBackup(
    bool Held,
    bool HadEnableDesktopModeAutoInvoke,
    int EnableDesktopModeAutoInvoke,
    bool HadTouchKeyboardTapInvoke,
    int TouchKeyboardTapInvoke,
    bool HadTouchKeyboardInvocationPolicy = false,
    int TouchKeyboardInvocationPolicy = 0);

/// <summary>
/// 切到 T9 时把「显示触摸键盘」改成「从不」；切走、退出、卸载再写回原值。
/// Win11 对应设置 → 时间和语言 → 输入 → 触摸键盘。
/// </summary>
internal static class OfficialTouchKeyboardPolicy
{
    public const int Never = 0;
    public const int WhenNoKeyboardAttached = 1;
    public const int Always = 2;

    /// <summary>
    /// 设置页「显示触摸键盘」官方写入点。本机改下拉框只动这个 DWORD：0 从不 / 1 未连接 / 2 始终。
    /// </summary>
    public const string TabletTipPath = @"Software\Microsoft\TabletTip\1.7";

    /// <summary>公开 Typing 对照表；运行时仍写一份，但不拿它当用户原值。</summary>
    public const string InputSettingsPath = @"Software\Microsoft\input\Settings";

    public const string InvocationPolicyName = "TouchKeyboardInvocationPolicy";

    public static readonly string[] WritePaths = [TabletTipPath, InputSettingsPath];

    /// <summary>
    /// 两边都有值时，以设置页实际写的 TabletTip TapInvoke 为准。
    /// input\Settings 里的 0 可能是我们上次留下的，不能当成用户原值。
    /// </summary>
    public static (bool Had, int Value) PreferOfficialTap(
        bool hadTabletTip,
        int tabletTip,
        bool hadInput,
        int input) =>
        hadTabletTip ? (true, tabletTip) : (hadInput, input);

    public static (bool Had, int Value) PreferUserValue(
        bool hadModern,
        int modern,
        bool hadLegacy,
        int legacy) =>
        hadModern ? (true, modern) : (hadLegacy, legacy);

    public static bool ShouldSuppress(bool paneEnabled, bool t9Selected) =>
        paneEnabled && t9Selected;

    /// <summary>
    /// 只认官方 GetActiveProfile 报的「当前 TIP 是 T9」。
    /// 打开设置页会丢掉文档/前台租约，那些不能用来松手。
    /// </summary>
    public static bool IsT9Live(bool officialProfile) => officialProfile;

    /// <summary>
    /// 已经握着备份时，不能把当前的「从不」当成用户原值再存一遍。
    /// </summary>
    public static TabletTipBackup CaptureBackup(
        bool alreadyHeld,
        TabletTipBackup existing,
        bool hadEnable,
        int enable,
        bool hadTap,
        int tap,
        bool hadInvocation = false,
        int invocation = 0)
    {
        if (alreadyHeld)
        {
            return existing with { Held = true };
        }

        return new TabletTipBackup(true, hadEnable, enable, hadTap, tap, hadInvocation, invocation);
    }

    public static bool ShouldWriteLegacyAutoInvoke(bool _) => true;
}
