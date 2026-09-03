namespace T9Pane.Services;

/// <summary>
/// 触摸键的触发时机。抬起才触发本身就带半拍延迟，因此没有独立长按动作、
/// 又不位于滑动手势起始区域的键改为按下即触发。
/// 九宫格键的长按是多击字母，与轻点语义不同，必须留在抬起触发；
    /// 候选条、侧栏和符号盘是滑动瀑布的起点，也必须留在抬起触发。
/// </summary>
internal static class KeyTapTimingPolicy
{
    public static bool IsImmediate(bool hasDistinctLongPress, bool gestureRegion) =>
        !hasDistinctLongPress && !gestureRegion;
}

/// <summary>
/// 系统浮层（HostRender）路径上，原生按下与抬起是两条独立通知。
/// 按下已经执行过动作时，随后的抬起必须被丢弃，否则一次点击出两次输入。
/// </summary>
internal sealed class HostPressGate
{
    private bool _handled;

    public bool PressHandled => _handled;

    public void NotePressHandled() => _handled = true;

    /// <summary>抬起事件是否应当被丢弃；取用后立即复位。</summary>
    public bool ConsumeRelease()
    {
        var handled = _handled;
        _handled = false;
        return handled;
    }

    public void Reset() => _handled = false;
}
