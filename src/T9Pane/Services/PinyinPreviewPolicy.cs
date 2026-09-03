using System.Text;

namespace T9Pane.Services;

/// <summary>
/// 组字栏只显示已经按下的编码。搜狗 / 微软拼音 / 小白 T9 都是：
/// 候选可以提前出「天气」，拼音栏仍停在 tianq / tian'q，不把没打的 i 写上去。
/// </summary>
internal static class PinyinPreviewPolicy
{
    public static string FromTypedDigits(string digits, string? candidatePinyin)
    {
        if (string.IsNullOrEmpty(digits))
        {
            return "";
        }

        var syllables = new List<string>();
        foreach (var raw in (candidatePinyin ?? "").Split(
                     [' ', '\'', '’', '-'],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            var compact = T9Engine.CompactLetters(raw);
            if (compact.Length > 0)
            {
                syllables.Add(compact);
            }
        }

        if (syllables.Count == 0)
        {
            return Fallback(digits);
        }

        var parts = new List<string>();
        var used = 0;
        foreach (var syllable in syllables)
        {
            if (used >= digits.Length)
            {
                break;
            }

            var take = Math.Min(syllable.Length, digits.Length - used);
            parts.Add(syllable[..take]);
            used += take;
        }

        if (used < digits.Length)
        {
            var extra = Fallback(digits[used..]);
            if (parts.Count == 0)
            {
                return extra;
            }

            parts[^1] += extra;
        }

        return string.Join("'", parts);
    }

    private static string Fallback(string digits)
    {
        var sb = new StringBuilder(digits.Length);
        foreach (var digit in digits)
        {
            var letters = T9Engine.LettersForKey(digit);
            if (letters.Length > 0)
            {
                sb.Append(letters[0]);
            }
        }

        return sb.ToString();
    }
}
