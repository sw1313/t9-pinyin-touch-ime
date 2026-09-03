namespace T9Pane.Services;

internal readonly record struct HostHitRegion<T>(
    double Left,
    double Top,
    double Right,
    double Bottom,
    T Target)
    where T : class
{
    public bool Contains(double x, double y) =>
        x >= Left && x < Right && y >= Top && y < Bottom;
}

internal static class HostHitMap
{
    public static T? Find<T>(IReadOnlyList<HostHitRegion<T>> regions, double x, double y)
        where T : class
    {
        for (var index = regions.Count - 1; index >= 0; index--)
        {
            if (regions[index].Contains(x, y))
            {
                return regions[index].Target;
            }
        }

        return null;
    }
}

internal sealed class HostActionMap<T> where T : class
{
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<T, Action> _actions = new();

    public void Clear() => _actions.Clear();

    public void Bind(T target, Action action)
    {
        _actions.Remove(target);
        _actions.Add(target, action);
    }

    public bool TryInvoke(T target)
    {
        if (!_actions.TryGetValue(target, out var action))
        {
            return false;
        }

        action();
        return true;
    }
}
