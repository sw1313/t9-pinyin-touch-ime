namespace T9Pane.Services;

internal enum TouchModifierPhase
{
    Off,
    Held,
    Locked
}

/// <summary>
/// 对齐 Windows 传统触摸键盘（TabTip Traditional），不是辅助功能 Sticky Keys。
/// Microsoft Press《Using Windows 11》：Ctrl/Alt/Win 是 sticky，点一下高亮，
/// 再和下一个键组合成快捷键；Win 点两下打开开始菜单。
/// Microsoft Q&A：传统布局没有 Shift Lock，双击 Shift 不会锁定。
/// 因此第二次点（含连点）一律解除；只有 Win 的第二次点发出开始菜单。
/// </summary>
internal static class TouchModifierPolicy
{
    public static TouchModifierPhase Tap(TouchModifierPhase current, bool windowsKey)
    {
        return current == TouchModifierPhase.Off
            ? TouchModifierPhase.Held
            : TouchModifierPhase.Off;
    }

    public static bool SecondTapFiresKey(TouchModifierPhase current, bool windowsKey) =>
        windowsKey && current == TouchModifierPhase.Held;

    public static bool IsOn(TouchModifierPhase phase) =>
        phase != TouchModifierPhase.Off;

    public static TouchModifierPhase Consume(TouchModifierPhase current) =>
        current == TouchModifierPhase.Held ? TouchModifierPhase.Off : current;
}
