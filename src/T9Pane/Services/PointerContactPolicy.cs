namespace T9Pane.Services;

/// <summary>
/// 官方触摸手势：点按是按下再抬起；长按由系统转成右键并弹出菜单。
/// 按下瞬间弹出/挪动高层窗口会取消系统的 press-and-hold，菜单就出不来；
/// uiAccess 盘面盖在插入点上会挡住已经弹出的菜单。
/// </summary>
internal static class PointerContactPolicy
{
    public static bool ShouldShowOnContactStart => false;

    public static bool ShouldRepositionOnContactStart => false;

    public static bool IsHoldBurst(bool contactDown) => contactDown;

    public static bool ShouldCompleteTap(bool contextMenu) => !contextMenu;

    public static bool ShouldYieldToContextMenu(bool contextMenu) =>
        ShouldYieldToContextMenu(contextMenu, overlayOwnsContact: false);

    /// <summary>
    /// 右键让开只针对应用内容区。盘面上的长按是连发，不是 WM_CONTEXTMENU。
    /// </summary>
    public static bool ShouldYieldToContextMenu(
        bool contextMenu,
        bool overlayOwnsContact) =>
        ShouldYieldToContextMenu(contextMenu, overlayOwnsContact, focusedText: false);

    /// <summary>
    /// 官方 InputPane：焦点在 Menu 才让开；焦点已回到 Text 就 Show。
    /// 盘面上的长按是连发，系统升格出来的右键不能当成应用菜单。
    /// </summary>
    public static bool ShouldYieldToContextMenu(
        bool contextMenu,
        bool overlayOwnsContact,
        bool focusedText) =>
        contextMenu && !overlayOwnsContact && !focusedText;
}

/// <summary>
/// 系统浮层位图键盘显示时，WPF 窗本身是藏着的。会话必须把宿主盘面算作已显示。
/// </summary>
internal static class KeyboardSurfacePolicy
{
    public static bool IsShown(bool wpfVisible, bool hosting) =>
        wpfVisible || hosting;
}
