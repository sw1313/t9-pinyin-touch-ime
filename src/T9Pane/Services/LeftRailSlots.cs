namespace T9Pane.Services;

/// <summary>
/// 左侧选词可见格数固定。瀑布流按这个高度滚，不够的格子留空，不要把键拉高。
/// </summary>
internal static class LeftRailSlots
{
    public const int Count = 5;

    public static IReadOnlyList<string?> Page(IReadOnlyList<string> items, int page)
    {
        var skip = Math.Max(0, page) * Count;
        var slots = new string?[Count];
        for (var i = 0; i < Count && skip + i < items.Count; i++)
        {
            slots[i] = items[skip + i];
        }

        return slots;
    }
}
