namespace T9Pane.Services;

/// <summary>
/// 对齐官方触摸键盘热路径，而不是事后补延迟：
/// 按下当帧必须看到缩放；文档里的组词串先于候选条；
/// 手指还按着时不得拆掉整盘。
/// </summary>
internal static class KeyFeedbackPolicy
{
    public static bool InstantPress => true;

    public static bool ComposeBeforeCandidates => true;

    public static bool CanRebuildFaces(bool hostPressed, bool localPressed) =>
        !hostPressed && !localPressed;
}
