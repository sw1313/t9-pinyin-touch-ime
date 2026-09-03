using T9Pane.Native;

namespace T9Pane.Services;

internal static class KeyboardPositionSession
{
    public static bool ShouldKeepSessionPosition(
        bool hasPosition,
        bool repositionRequested) =>
        hasPosition && !repositionRequested;

    public static bool IsSameSurfaceContext(
        InputContextKey current,
        InputContextKey next)
    {
        if (current.IsEmpty || next.IsEmpty)
        {
            return false;
        }

        if (current.Epoch != 0 || next.Epoch != 0)
        {
            return current.Client != 0
                && current.Client == next.Client
                && (current.View == 0
                    || next.View == 0
                    || current.View == next.View);
        }

        return current == next;
    }

    public static bool ShouldRestart(
        bool visible,
        IntPtr currentHost,
        IntPtr nextHost,
        InputContextKey currentContext = default,
        InputContextKey nextContext = default) =>
        visible
        && ((currentHost != IntPtr.Zero
                && nextHost != IntPtr.Zero
                && currentHost != nextHost)
            || (!nextContext.IsEmpty
                && !IsSameSurfaceContext(currentContext, nextContext)));

    public static bool ShouldKeepMovedPosition(
        bool movedByUser,
        NativeRect previousAnchor,
        NativeRect nextAnchor,
        int tolerance = 12) =>
        movedByUser
        && !previousAnchor.IsEmpty
        && !nextAnchor.IsEmpty
        && Math.Abs(previousAnchor.Left - nextAnchor.Left) <= tolerance
        && Math.Abs(previousAnchor.Top - nextAnchor.Top) <= tolerance;

    public static bool ShouldHoldForSameLine(
        bool visible,
        bool sameHost,
        bool sameContext,
        bool hasPosition,
        NativeRect previousAnchor,
        NativeRect nextAnchor,
        int lineTolerance = 4) =>
        visible
        && sameHost
        && sameContext
        && hasPosition
        && !previousAnchor.IsEmpty
        && !nextAnchor.IsEmpty
        && Math.Abs(previousAnchor.Top - nextAnchor.Top) <= lineTolerance;

    public static bool ShouldFollowTypingLine(
        NativeRect previousCaret,
        NativeRect nextCaret,
        int lineTolerance = 4) =>
        !previousCaret.IsEmpty
        && !nextCaret.IsEmpty
        && Math.Abs(previousCaret.Top - nextCaret.Top) > lineTolerance;
}

internal static class KeyboardPlacer
{
    private const int Gap = 8;

    public static NativeRect Place(InputField field, int width, int height)
    {
        var caret = field.Caret.IsEmpty
            ? new NativeRect { Left = 80, Top = 80, Right = 82, Bottom = 104 }
            : field.Caret;
        return Place(field, width, height, WorkOf(caret));
    }

    public static NativeRect Place(InputField field, int width, int height, NativeRect work)
    {
        var caret = field.Caret;
        if (caret.IsEmpty)
        {
            caret = new NativeRect { Left = 80, Top = 80, Right = 82, Bottom = 104 };
        }

        width = Math.Min(width, Math.Max(160, work.Width - Gap * 2));
        height = Math.Min(height, Math.Max(160, work.Height - Gap * 2));

        if (!field.Occluder.IsEmpty)
        {
            return PlaceByFlyout(field, width, height, work);
        }

        var left = Math.Clamp(caret.Left, work.Left + Gap, work.Right - width - Gap);
        return PlaceAround(ExcludeTypingLine(field, caret), left, width, height, work);
    }

    /// <summary>
    /// CFS_EXCLUDE：候选窗不得进入排除区。大键盘优先翻到打字行上方，
    /// 上方不够再落到下方。夹到工作区时不得穿过打字行——那会把光标盖住。
    /// </summary>
    public static NativeRect PlaceAround(
        NativeRect exclude,
        int left,
        int width,
        int height,
        NativeRect work)
    {
        var above = exclude.Top - Gap - height;
        if (above >= work.Top + Gap)
        {
            return Box(left, above, width, height);
        }

        var below = exclude.Bottom + Gap;
        if (below + height <= work.Bottom - Gap)
        {
            return Box(left, below, width, height);
        }

        var roomBelow = work.Bottom - exclude.Bottom;
        var roomAbove = exclude.Top - work.Top;
        if (roomBelow >= roomAbove && roomBelow > Gap)
        {
            var top = Math.Min(below, work.Bottom - height - Gap);
            return Box(left, top >= exclude.Bottom ? top : below, width, height);
        }

        var pinnedAbove = Math.Max(above, work.Top + Gap);
        return Box(
            left,
            pinnedAbove + height <= exclude.Top ? pinnedAbove : above,
            width,
            height);
    }

    private static NativeRect ExcludeTypingLine(InputField field, NativeRect caret)
    {
        if (field.FieldBox.IsEmpty || field.FieldBox.Height > 72)
        {
            return caret;
        }

        return new NativeRect
        {
            Left = Math.Min(caret.Left, field.FieldBox.Left),
            Top = Math.Min(caret.Top, field.FieldBox.Top),
            Right = Math.Max(caret.Right, field.FieldBox.Right),
            Bottom = Math.Max(caret.Bottom, field.FieldBox.Bottom)
        };
    }

    private static NativeRect PlaceByFlyout(InputField field, int width, int height, NativeRect work)
    {
        var caret = field.Caret.IsEmpty ? field.Occluder : field.Caret;
        var fly = field.Occluder;
        var left = caret.Width < 40 && !fly.IsEmpty
            ? fly.Left + Math.Max(0, (fly.Width - width) / 2)
            : caret.Left;
        left = Math.Clamp(left, work.Left + Gap, work.Right - width - Gap);
        return PlaceAround(caret, left, width, height, work);
    }

    public static NativeRect Preview(int width, int height)
    {
        var work = WorkOf(new NativeRect { Left = 0, Top = 0, Right = 1, Bottom = 1 });
        return Place(new InputField(IntPtr.Zero, new NativeRect
        {
            Left = work.Left + 40,
            Top = work.Bottom - 72,
            Right = work.Left + 42,
            Bottom = work.Bottom - 48
        }, default), width, height);
    }

    private static NativeRect Box(int left, int top, int width, int height) => new()
    {
        Left = left,
        Top = top,
        Right = left + width,
        Bottom = top + height
    };

    private static NativeRect WorkOf(NativeRect seed)
    {
        if (NativeMethods.TryGetMonitorWork(seed, out var work) && !work.IsEmpty)
        {
            return work;
        }

        var screen = System.Windows.Forms.Screen.PrimaryScreen?.WorkingArea
                     ?? new System.Drawing.Rectangle(0, 0, 1280, 800);
        return new NativeRect
        {
            Left = screen.Left,
            Top = screen.Top,
            Right = screen.Right,
            Bottom = screen.Bottom
        };
    }
}
