namespace T9Pane.Services;

/// <summary>
/// 安卓英文键盘的联想开关：开是蓝色带下划线的 abc，关是灰色，状态永久保存。
/// </summary>
internal static class EnglishPredictPolicy
{
    public const string Label = "abc";

    public static bool Applies(bool englishBoard, bool fullLatin) =>
        englishBoard || fullLatin;

    public static bool Composes(bool enabled, bool englishBoard, bool fullLatin) =>
        enabled && Applies(englishBoard, fullLatin);

    public static bool ShowsAccent(bool enabled) => enabled;

    public static bool ShowsUnderline(bool enabled) => enabled;
}
