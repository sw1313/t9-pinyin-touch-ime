namespace T9Pane.Services;

/// <summary>
/// 符号默认点一个就回上层。锁上才连续输入。锁只由锁键翻转，
/// 点符号、离盘、重建盘面都不能改。最近用过的排在「最近」。
/// </summary>
internal static class SymbolPanelPolicy
{
    public const int RecentLimit = 24;

    public static bool StayAfterPick(bool locked) => locked;

    public static bool ClearsOnLeave => false;

    public static bool ClearsOnPick => false;

    public static IReadOnlyList<string> Remember(IReadOnlyList<string> recent, string symbol)
    {
        if (string.IsNullOrEmpty(symbol))
        {
            return recent;
        }

        var next = new List<string>(RecentLimit) { symbol };
        foreach (var item in recent)
        {
            if (next.Count >= RecentLimit)
            {
                break;
            }

            if (!string.Equals(item, symbol, StringComparison.Ordinal))
            {
                next.Add(item);
            }
        }

        return next;
    }
}
