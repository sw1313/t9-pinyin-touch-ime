namespace T9Pane.Services;

/// <summary>
/// 合法音节表。RIME ScriptTranslator / 微软拼音 / 小白 T9 都是：
/// 先把输入切成音节图（大约四百个音节），侧栏的拼音组合来自这张图；
/// 词库只按已经切出来的路径去查，不拿数字前缀扫整库。
/// </summary>
internal sealed class T9SyllableTable
{
    private readonly List<T9Syllable> _all = [];
    private readonly HashSet<string> _texts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<string>> _byDigits = new(StringComparer.Ordinal);

    public int Count => _all.Count;

    public void Clear()
    {
        _all.Clear();
        _texts.Clear();
        _byDigits.Clear();
    }

    public void Add(string text)
    {
        var compact = T9Engine.CompactLetters(text);
        if (compact.Length == 0 || !_texts.Add(compact))
        {
            return;
        }

        var digits = T9Engine.ToDigits(compact);
        if (digits.Length == 0)
        {
            _texts.Remove(compact);
            return;
        }

        _all.Add(new T9Syllable(compact, digits));
        if (!_byDigits.TryGetValue(digits, out var list))
        {
            list = [];
            _byDigits[digits] = list;
        }

        list.Add(compact);
    }

    /// <summary>
    /// 当前音节：数字码以已输入为前缀（含正好拼完）。
    /// 短于输入的完整音节是上一截，留给组词，不进侧栏。
    /// </summary>
    public IReadOnlyList<string> MatchCurrent(string rest)
    {
        if (string.IsNullOrEmpty(rest))
        {
            return [];
        }

        var matched = new List<T9Syllable>();
        foreach (var syllable in _all)
        {
            if (syllable.Digits.StartsWith(rest, StringComparison.Ordinal))
            {
                matched.Add(syllable);
            }
        }

        matched.Sort(CompareCurrent);
        var items = new string[matched.Count];
        for (var i = 0; i < matched.Count; i++)
        {
            items[i] = matched[i].Text;
        }

        return items;
    }

    public IReadOnlyList<string> MatchCurrentLetters(string rest)
    {
        if (string.IsNullOrEmpty(rest))
        {
            return [];
        }

        var matched = new List<T9Syllable>();
        foreach (var syllable in _all)
        {
            if (syllable.Text.StartsWith(rest, StringComparison.Ordinal))
            {
                matched.Add(syllable);
            }
        }

        matched.Sort(CompareCurrentLetters);
        var items = new string[matched.Count];
        for (var i = 0; i < matched.Count; i++)
        {
            items[i] = matched[i].Text;
        }

        return items;
    }

    /// <summary>
    /// 查词用：当前音节补全，加上已经拼完、后面还有码的短音节。
    /// </summary>
    public IReadOnlyList<string> MatchForWords(string typed)
    {
        if (string.IsNullOrEmpty(typed))
        {
            return [];
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var items = new List<string>();
        foreach (var syllable in _all)
        {
            if ((syllable.Digits.StartsWith(typed, StringComparison.Ordinal)
                 || typed.StartsWith(syllable.Digits, StringComparison.Ordinal))
                && seen.Add(syllable.Text))
            {
                items.Add(syllable.Text);
            }
        }

        return items;
    }

    public IReadOnlyList<string> MatchLettersForWords(string typed)
    {
        if (string.IsNullOrEmpty(typed))
        {
            return [];
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var items = new List<string>();
        foreach (var syllable in _all)
        {
            if ((syllable.Text.StartsWith(typed, StringComparison.Ordinal)
                 || typed.StartsWith(syllable.Text, StringComparison.Ordinal))
                && seen.Add(syllable.Text))
            {
                items.Add(syllable.Text);
            }
        }

        return items;
    }

    public static string RemainingDigits(string digits, IReadOnlyList<string>? confirmed)
    {
        if (string.IsNullOrEmpty(digits) || confirmed is null || confirmed.Count == 0)
        {
            return digits ?? "";
        }

        var used = 0;
        foreach (var syllable in confirmed)
        {
            used += T9Engine.ToDigits(syllable).Length;
        }

        return used >= digits.Length ? "" : digits[used..];
    }

    public static string RemainingLetters(string letters, IReadOnlyList<string>? confirmed)
    {
        var typed = T9Engine.CompactLetters(letters);
        if (typed.Length == 0 || confirmed is null || confirmed.Count == 0)
        {
            return typed;
        }

        var used = 0;
        foreach (var syllable in confirmed)
        {
            used += T9Engine.CompactLetters(syllable).Length;
        }

        return used >= typed.Length ? "" : typed[used..];
    }

    private static int CompareCurrent(T9Syllable left, T9Syllable right)
    {
        var byLength = left.Digits.Length.CompareTo(right.Digits.Length);
        return byLength != 0 ? byLength : string.CompareOrdinal(left.Text, right.Text);
    }

    private static int CompareCurrentLetters(T9Syllable left, T9Syllable right)
    {
        var byLength = left.Text.Length.CompareTo(right.Text.Length);
        return byLength != 0 ? byLength : string.CompareOrdinal(left.Text, right.Text);
    }

    private readonly record struct T9Syllable(string Text, string Digits);
}
