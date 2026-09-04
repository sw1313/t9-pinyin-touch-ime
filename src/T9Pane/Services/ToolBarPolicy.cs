namespace T9Pane.Services;

/// <summary>
/// 数字盘、符号盘、展开选字时，底栏右下角是退格而不是回车。
/// </summary>
internal static class ToolBarPolicy
{
    public static bool BackspaceInsteadOfEnter(
        bool numberPad,
        bool symbolBoard,
        bool candidatesExpanded = false) =>
        numberPad || symbolBoard || candidatesExpanded;
}
