namespace T9Pane.Services;

/// <summary>
/// 中文组词时回车上屏联想条里的拼音/英文，空格仍上屏汉字。
/// </summary>
internal static class EnterCommitPolicy
{
    public static string? LatinText(
        bool composingChinese,
        string letters,
        string preview,
        IEnumerable<T9Candidate> candidates)
    {
        if (!composingChinese)
        {
            return null;
        }

        foreach (var hit in candidates)
        {
            if (T9Latin.IsLatinWord(hit.Word))
            {
                return hit.Word;
            }
        }

        if (!string.IsNullOrEmpty(letters))
        {
            return letters;
        }

        if (string.IsNullOrEmpty(preview))
        {
            return null;
        }

        return preview.Replace("'", "", StringComparison.Ordinal);
    }
}
