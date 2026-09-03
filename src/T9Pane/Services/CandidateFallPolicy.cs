namespace T9Pane.Services;

/// <summary>
/// 展开选字时左边仍要能选拼音，点同一个音节再取消过滤。
/// </summary>
internal static class CandidateFallPolicy
{
    public static bool ShowsPinyinRail(bool expanded, bool composingChinese) =>
        expanded && composingChinese;

    public static bool CanExpand(bool pinyinBoard, bool fullBoard, bool latin, bool pinyin26 = false, bool englishBoard = false) =>
        pinyinBoard || pinyin26 || fullBoard || englishBoard;

    public static bool CanExpand(KeyboardSurface surface) =>
        surface is KeyboardSurface.Pinyin
            or KeyboardSurface.Pinyin26
            or KeyboardSurface.English
            or KeyboardSurface.Full;

    public static bool UsesLatinMarks(bool englishBoard, bool latin) =>
        englishBoard || latin;

    public static bool ComposingChinese(bool pinyinBoard, bool fullBoard, bool latin, bool pinyin26 = false) =>
        pinyinBoard || pinyin26 || (fullBoard && !latin);

    /// <summary>
    /// 点完联想词要拆掉展开盘，回到九键主界面。
    /// </summary>
    public static bool RebuildHomeAfterCommit(bool wasExpanded) => wasExpanded;

    public static string? ToggleSyllable(string? current, string tapped)
    {
        if (string.IsNullOrEmpty(tapped))
        {
            return current;
        }

        return string.Equals(current, tapped, StringComparison.Ordinal) ? null : tapped;
    }
}
