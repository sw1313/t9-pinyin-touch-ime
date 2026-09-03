namespace T9Pane.Services;

/// <summary>
/// 英文词和网址短语。搜狗/微软都是中文模式下打 h、www、http 直接出补全。
/// </summary>
internal static class T9Latin
{
    public static bool IsLatinWord(string word)
    {
        if (string.IsNullOrEmpty(word))
        {
            return false;
        }

        if (IsShortcut(word))
        {
            return true;
        }

        var letter = false;
        foreach (var ch in word)
        {
            if (ch > 0x7F && ch is not '\'' and not '’')
            {
                return false;
            }

            letter |= char.IsLetter(ch);
        }

        return letter;
    }

    public static bool IsShortcut(string word) =>
        word.Contains("://", StringComparison.Ordinal)
        || word.StartsWith("www.", StringComparison.Ordinal)
        || word.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)
        || word.StartsWith('@')
        || (word.StartsWith('.') && word.Length is >= 3 and <= 6);

    public static string? Kind(string word, string trigger, string typed)
    {
        var match = T9Engine.ClassifyLetterMatch(trigger, typed);
        if (match is null)
        {
            return null;
        }

        if (!IsShortcut(word))
        {
            return match;
        }

        return match == "全拼" ? "短语" : "短语前缀";
    }
}
