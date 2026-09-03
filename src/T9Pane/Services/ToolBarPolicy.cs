namespace T9Pane.Services;

/// <summary>
/// 数字盘和符号盘更常改错，底栏主键改成退格。
/// </summary>
internal static class ToolBarPolicy
{
    public static bool BackspaceInsteadOfEnter(bool numberPad, bool symbolBoard) =>
        numberPad || symbolBoard;
}
