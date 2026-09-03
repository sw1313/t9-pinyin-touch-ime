namespace T9Pane.Services;

/// <summary>
/// 九键排序：全拼、组句、全拼前缀，然后才是组词和简拼。
/// 组词/组句按「还剩多少码没吃掉」排，吃得越干净越靠前。
/// </summary>
internal static class T9MatchRank
{
    public static int Kind(string kind) => kind switch
    {
        "短语" => 8,
        "短语前缀" => 7,
        "全拼" => 6,
        "组句" => 5,
        "全拼前缀" => 4,
        "组词" => 3,
        "简拼" => 2,
        _ => 1
    };

    public static int Leftover(string kind, int codeLength, int typedLength) =>
        kind is "组词" or "组句" or "简拼组词"
            ? Math.Max(0, typedLength - codeLength)
            : Math.Max(0, codeLength - typedLength);

    public static IReadOnlyList<T9Candidate> Order(
        IEnumerable<T9Candidate> hits,
        int typedLength,
        int take)
    {
        return hits
            .OrderByDescending(hit => Kind(hit.MatchKind))
            .ThenBy(hit => Leftover(hit.MatchKind, T9Engine.ToDigits(hit.Pinyin).Length, typedLength))
            .ThenByDescending(hit => hit.Frequency)
            .ThenBy(hit => hit.Word.Length)
            .Take(take)
            .ToList();
    }
}
