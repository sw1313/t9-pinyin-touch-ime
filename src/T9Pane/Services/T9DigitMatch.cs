namespace T9Pane.Services;

/// <summary>
/// 九键匹配。手机是先切出已经拼完的词，剩余编码继续查。
/// 只认「词码以输入为前缀」时，超过两字输入就会空。
/// </summary>
internal static class T9DigitMatch
{
    public static string? Classify(string fullDigits, string initialDigits, string typed)
    {
        if (string.IsNullOrEmpty(typed) || string.IsNullOrEmpty(fullDigits))
        {
            return null;
        }

        if (fullDigits.StartsWith(typed, StringComparison.Ordinal))
        {
            return fullDigits.Length == typed.Length ? "全拼" : "全拼前缀";
        }

        if (typed.StartsWith(fullDigits, StringComparison.Ordinal) && fullDigits.Length >= 2)
        {
            return "组词";
        }

        if (string.IsNullOrEmpty(initialDigits))
        {
            return null;
        }

        if (initialDigits.StartsWith(typed, StringComparison.Ordinal))
        {
            return initialDigits.Length == typed.Length ? "简拼" : "简拼前缀";
        }

        if (typed.StartsWith(initialDigits, StringComparison.Ordinal) && initialDigits.Length >= 2)
        {
            return "简拼组词";
        }

        return null;
    }

    public static bool CanLeadPhrase(string kind, int wordLength, int codeLength) =>
        kind is "组词" or "全拼"
        && wordLength >= 2
        && codeLength >= 4;
}
