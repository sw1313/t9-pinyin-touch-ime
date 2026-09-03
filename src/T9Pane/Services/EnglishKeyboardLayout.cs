namespace T9Pane.Services;

/// <summary>
/// 紧凑 26 键英文：三行字母，和九键同一套窄窗。
/// </summary>
internal static class EnglishKeyboardLayout
{
    public static readonly string[] Row1 = ["q", "w", "e", "r", "t", "y", "u", "i", "o", "p"];
    public static readonly string[] Row2 = ["a", "s", "d", "f", "g", "h", "j", "k", "l"];
    public static readonly string[] Row3 = ["z", "x", "c", "v", "b", "n", "m"];

    public static IReadOnlyList<IReadOnlyList<string>> Rows { get; } =
        [Row1, Row2, Row3];

    public static int LetterCount => Row1.Length + Row2.Length + Row3.Length;

    /// <summary>asdf 行按方格宽度内缩，保持错位且键仍接近正方形。</summary>
    public static double RowStagger(double unit) => unit * 0.35;

    public static string Face(string letter, bool shift)
    {
        if (string.IsNullOrEmpty(letter))
        {
            return letter;
        }

        return shift ? letter.ToUpperInvariant() : letter.ToLowerInvariant();
    }
}
