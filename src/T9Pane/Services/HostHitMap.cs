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
    /// <summary>
    /// 宿主位图显示时 WPF 窗是藏着的，<c>IsVisible</c> 全是 false。
    /// 命中图只看布局是否还在、键是否可用，不能跟窗口 HWND 显隐绑死。
    /// </summary>
    public static bool ShouldCollect(
        bool layoutVisible,
        bool enabled,
        double renderWidth,
        double renderHeight) =>
        layoutVisible && enabled && renderWidth >= 2 && renderHeight >= 2;

    /// <summary>
    /// 藏窗后再抓一帧如果一颗键都收不到，必须留下上一帧，否则盘面还在、键全点不中。
    /// </summary>
    public static bool ShouldReplaceRegions(int incomingCount, int existingCount) =>
        ShouldReplaceRegions(incomingCount, existingCount, incomingCoversFrame: true);

    /// <summary>
    /// 藏着的 WPF 窗 ActualHeight 会收矮，命中图只盖住标题/候选那一截。
    /// 画面仍是整盘时，矮图不能替换高图，否则底下的键全点不中。
    /// </summary>
    public static bool CoversFrameHeight(
        double maxBottom,
        double frameHeight,
        double minRatio = 0.7) =>
        frameHeight <= 0 || maxBottom >= frameHeight * minRatio;

    public static bool IsLayoutCollapsed(double actualHeight, double designHeight) =>
        designHeight > 1 && actualHeight > 0 && actualHeight < designHeight * 0.8;

    public static bool ShouldReplaceRegions(
        int incomingCount,
        int existingCount,
        bool incomingCoversFrame) =>
        (incomingCount > 0 && incomingCoversFrame) || existingCount == 0;

    /// <summary>
    /// 拼音九键是默认盘，第一次弹出窗口尺寸不变，刚 Clear/Add 的 3×3
    /// 往往还没有 RenderSize。切到 26 键会改窗宽，布局被逼完整走一遍。
    /// 命中图少过树上已有的键时，必须冲完布局再抓一帧，不能拿空图上屏。
    /// </summary>
    public static bool CountsAsExpected(bool layoutVisible, bool enabled) =>
        layoutVisible && enabled;

    public static bool ShouldRetryAfterRebuild(int collected, int expected) =>
        expected > 0 && collected < expected;

    /// <summary>
    /// 候选条是打字后才出现的。整图若因藏窗变矮而不能替换，
    /// 仍要把新出现的候选按钮补进现有命中图，否则画面有字、点到的却是退格。
    /// </summary>
    public static void Upsert<T>(
        List<HostHitRegion<T>> regions,
        IReadOnlyList<HostHitRegion<T>> incoming)
        where T : class
    {
        for (var index = 0; index < incoming.Count; index++)
        {
            var item = incoming[index];
            var existing = -1;
            for (var scan = 0; scan < regions.Count; scan++)
            {
                if (ReferenceEquals(regions[scan].Target, item.Target))
                {
                    existing = scan;
                    break;
                }
            }

            if (existing >= 0)
            {
                regions[existing] = item;
            }
            else
            {
                regions.Add(item);
            }
        }
    }

    public static void RemoveTarget<T>(List<HostHitRegion<T>> regions, T target)
        where T : class
    {
        for (var index = regions.Count - 1; index >= 0; index--)
        {
            if (ReferenceEquals(regions[index].Target, target))
            {
                regions.RemoveAt(index);
            }
        }
    }

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

    /// <summary>
    /// 命中图按 FrameBorder 的 DIP 收，Band 窗上报的是位图像素。
    ///
    /// 缩放不是 1 时必须先做像素→DIP，不能先拿原值碰运气：150% 下帧 600×606、
    /// 命中图 400×404，物理坐标几乎总落在命中图范围内，先试原值就会稳定命中
    /// 错的那颗键，而且永远不会报未命中。原值只留作 DLL 已经换算过的兜底。
    /// </summary>
    public static T? FindLayout<T>(
        IReadOnlyList<HostHitRegion<T>> regions,
        double x,
        double y,
        double layoutWidth,
        double layoutHeight,
        double frameWidth,
        double frameHeight)
        where T : class =>
        FindLayout(
            regions,
            x,
            y,
            layoutWidth,
            layoutHeight,
            frameWidth,
            frameHeight,
            out _);

    public static T? FindLayout<T>(
        IReadOnlyList<HostHitRegion<T>> regions,
        double x,
        double y,
        double layoutWidth,
        double layoutHeight,
        double frameWidth,
        double frameHeight,
        out string space)
        where T : class
    {
        space = "raw";
        if (layoutWidth <= 1
            || layoutHeight <= 1
            || frameWidth <= 1
            || frameHeight <= 1
            || (Math.Abs(frameWidth - layoutWidth) < 0.5
                && Math.Abs(frameHeight - layoutHeight) < 0.5))
        {
            return Find(regions, x, y);
        }

        var hit = Find(
            regions,
            x * layoutWidth / frameWidth,
            y * layoutHeight / frameHeight);
        if (hit is not null)
        {
            space = "px→dip";
            return hit;
        }

        hit = Find(regions, x, y);
        if (hit is not null)
        {
            return hit;
        }

        space = "dip→px";
        return Find(
            regions,
            x * frameWidth / layoutWidth,
            y * frameHeight / layoutHeight);
    }

    public static (double Left, double Top, double Right, double Bottom) UnionBounds<T>(
        IReadOnlyList<HostHitRegion<T>> regions)
        where T : class
    {
        if (regions.Count == 0)
        {
            return (0, 0, 0, 0);
        }

        var left = regions[0].Left;
        var top = regions[0].Top;
        var right = regions[0].Right;
        var bottom = regions[0].Bottom;
        for (var index = 1; index < regions.Count; index++)
        {
            var region = regions[index];
            if (region.Left < left)
            {
                left = region.Left;
            }

            if (region.Top < top)
            {
                top = region.Top;
            }

            if (region.Right > right)
            {
                right = region.Right;
            }

            if (region.Bottom > bottom)
            {
                bottom = region.Bottom;
            }
        }

        return (left, top, right, bottom);
    }

    /// <summary>
    /// SearchHost / Start 里的 IME 窗跟 T9Pane 不在同一个 DPI 感知上下文。
    /// <c>ScreenToClient</c> 会交出 96 DPI 逻辑坐标，命中图却是 PerMonitor 物理像素。
    /// 用客户区对帧尺寸做一次比例还原，两边一致时保持原值。
    /// </summary>
    public static (int X, int Y) MapClientToFrame(
        int x,
        int y,
        int clientWidth,
        int clientHeight,
        int frameWidth,
        int frameHeight)
    {
        if (clientWidth <= 0
            || clientHeight <= 0
            || frameWidth <= 0
            || frameHeight <= 0
            || (clientWidth == frameWidth && clientHeight == frameHeight))
        {
            return (x, y);
        }

        return (Scale(x, clientWidth, frameWidth), Scale(y, clientHeight, frameHeight));
    }

    internal static int Scale(int value, int from, int to) =>
        from <= 0 || to <= 0 || from == to
            ? value
            : (int)(((long)value * to + from / 2) / from);
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
