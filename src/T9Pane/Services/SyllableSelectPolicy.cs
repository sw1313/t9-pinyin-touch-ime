namespace T9Pane.Services;

/// <summary>
/// 搜狗 / 小白 T9 / Xime：左侧选音节是「确认当前字的读音，再列下一个字」。
/// 再点刚确认的那个音节则取消这一步。
/// </summary>
internal static class SyllableSelectPolicy
{
    public static IReadOnlyList<string> Confirm(
        IReadOnlyList<string> confirmed,
        string tapped)
    {
        if (string.IsNullOrEmpty(tapped))
        {
            return confirmed;
        }

        var next = new string[confirmed.Count + 1];
        for (var i = 0; i < confirmed.Count; i++)
        {
            next[i] = confirmed[i];
        }

        next[^1] = tapped;
        return next;
    }

    public static bool Matches(string pinyin, IReadOnlyList<string> confirmed)
    {
        if (confirmed.Count == 0)
        {
            return true;
        }

        var parts = T9Engine.Syllables(pinyin);
        if (parts.Count < confirmed.Count)
        {
            return false;
        }

        for (var i = 0; i < confirmed.Count; i++)
        {
            if (!string.Equals(parts[i], confirmed[i], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    public static IReadOnlyList<string> Rail(
        IEnumerable<string> candidatePinyins,
        IReadOnlyList<string> confirmed)
    {
        var index = confirmed.Count;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var items = new List<string>();
        foreach (var pinyin in candidatePinyins)
        {
            if (!Matches(pinyin, confirmed))
            {
                continue;
            }

            var syllable = T9Engine.SyllableAt(pinyin, index);
            if (syllable.Length > 0 && seen.Add(syllable))
            {
                items.Add(syllable);
            }
        }

        return items;
    }

    public static string Display(IReadOnlyList<string> confirmed, string typedPreview)
    {
        if (confirmed.Count == 0)
        {
            return typedPreview;
        }

        var head = string.Join("'", confirmed);
        if (string.IsNullOrEmpty(typedPreview))
        {
            return head;
        }

        if (typedPreview.StartsWith(head, StringComparison.Ordinal))
        {
            return typedPreview;
        }

        return head;
    }

    public static IReadOnlyList<string> Pop(IReadOnlyList<string> confirmed)
    {
        if (confirmed.Count == 0)
        {
            return confirmed;
        }

        if (confirmed.Count == 1)
        {
            return [];
        }

        var popped = new string[confirmed.Count - 1];
        for (var i = 0; i < popped.Length; i++)
        {
            popped[i] = confirmed[i];
        }

        return popped;
    }
}
