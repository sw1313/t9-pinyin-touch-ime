namespace T9Pane.Services;

internal static class SipSuppressor
{
    public static void HideOfficial()
    {
        // 不隐藏官方虚拟键盘：T9 只在自己被选中时出现，系统 SIP 由官方输入法自己管。
    }
}
