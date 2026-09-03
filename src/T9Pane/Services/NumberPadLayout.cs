namespace T9Pane.Services;

/// <summary>
/// 数字盘末行用 X，方便身份证校验位；逗号走符号盘。
/// </summary>
internal static class NumberPadLayout
{
    public static readonly string[] Keys =
    [
        "1", "2", "3",
        "4", "5", "6",
        "7", "8", "9",
        "X", "0", "."
    ];
}
