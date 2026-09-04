namespace T9Pane.Services;

/// <summary>
/// 搜狗 / 小白 T9：先选一个字或短词后，吃掉对应编码，剩余码继续查。
/// 选整词或选了覆盖全部输入的候选则一次上屏。
/// </summary>
internal static class CompositionConsumePolicy
{
    public static int ConsumeDigits(string matchKind, string pinyin, string typedDigits)
    {
        if (string.IsNullOrEmpty(typedDigits))
        {
            return 0;
        }

        var full = T9Engine.ToDigits(pinyin);
        var initials = T9Engine.ToDigits(T9Engine.SyllableInitials(pinyin));
        if (matchKind is "组词" or "组句"
            && typedDigits.StartsWith(full, StringComparison.Ordinal)
            && full.Length >= 2)
        {
            return Math.Min(full.Length, typedDigits.Length);
        }

        if (matchKind is "简拼组词"
            && typedDigits.StartsWith(initials, StringComparison.Ordinal)
            && initials.Length >= 2)
        {
            return Math.Min(initials.Length, typedDigits.Length);
        }

        return typedDigits.Length;
    }

    public static int ConsumeLetters(string matchKind, string pinyin, string typedLetters)
    {
        var typed = T9Engine.CompactLetters(typedLetters);
        if (typed.Length == 0)
        {
            return 0;
        }

        var compact = T9Engine.CompactLetters(pinyin);
        var initials = T9Engine.SyllableInitials(pinyin);
        if (matchKind is "组词" or "组句"
            && typed.StartsWith(compact, StringComparison.Ordinal)
            && compact.Length >= 2)
        {
            return Math.Min(compact.Length, typed.Length);
        }

        if (matchKind is "简拼组词"
            && typed.StartsWith(initials, StringComparison.Ordinal)
            && initials.Length >= 2)
        {
            return Math.Min(initials.Length, typed.Length);
        }

        return typed.Length;
    }

    public static bool HasRemainder(int consumed, int typedLength) =>
        consumed > 0 && consumed < typedLength;
}
