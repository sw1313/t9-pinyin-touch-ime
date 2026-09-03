namespace T9Pane.Services;

internal readonly record struct HeldSurfaceSnapshot(
    TouchModifierPhase Shift,
    TouchModifierPhase Ctrl,
    TouchModifierPhase Alt,
    TouchModifierPhase Win,
    bool Fn,
    bool Caps);

/// <summary>
/// 收起时要松开的表面状态。传统触摸键盘收起后修饰键和 Fn 面都回到默认；
/// Caps 是锁定，下次打开仍保持。
/// </summary>
internal static class HeldSurfacePolicy
{
    public static HeldSurfaceSnapshot Dismiss(HeldSurfaceSnapshot current) =>
        new(
            TouchModifierPhase.Off,
            TouchModifierPhase.Off,
            TouchModifierPhase.Off,
            TouchModifierPhase.Off,
            Fn: false,
            Caps: current.Caps);

    /// <summary>
    /// 收起时再往系统浮层推一帧，IME 会晚几百毫秒回 HostRender 已显示，
    /// 看起来就是消失后又闪一下。键面已经要拆，不必再画松开态。
    /// </summary>
    public static bool MustPublishHostBeforeHide(bool hosting)
    {
        _ = hosting;
        return false;
    }
}
