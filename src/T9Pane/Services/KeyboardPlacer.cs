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

    /// <summary>
    /// 换到系统浮层是官方 Relayout，不要先拆掉 WPF 盘面。
    /// 四五版之前能打字，就是宿主位图还没起来时 WPF 仍接着点。
    /// </summary>
    public static bool ShouldTearDownBeforePlace(
        bool restart,
        bool nextRequiresHostRender) =>
        restart && !nextRequiresHostRender;

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
        bool previousIsInsertion = true,
        bool nextIsInsertion = true,
        int lineTolerance = 20) =>
        previousIsInsertion
        && nextIsInsertion
        && !previousCaret.IsEmpty
        && !nextCaret.IsEmpty
        && Math.Abs(previousCaret.Top - nextCaret.Top) > lineTolerance;

    /// <summary>
    /// 换行只跟 Y。X 仍用上一次摆放，避免组词时 GetTextExt 光标右移
    /// 把整窗带着挪。
    /// </summary>
    public static NativeRect PinHorizontal(NativeRect previous, NativeRect next)
    {
        if (previous.IsEmpty || next.IsEmpty)
        {
            return next;
        }

        var width = next.Width;
        var height = next.Height;
        return new NativeRect
        {
            Left = previous.Left,
            Top = next.Top,
            Right = previous.Left + width,
            Bottom = next.Top + height
        };
    }

    /// <summary>
    /// 可见且矩形没变时不要再 SetWindowPos。透明分层窗每次重摆都会抖一两像素。
    /// </summary>
    public static bool ShouldMoveVisibleWindow(bool sameRect, bool hostModeChanged) =>
        !sameRect || hostModeChanged;

    /// <summary>
    /// 官方 GetTextExt 只描述当前焦点文档。同页多个 Edit 时，UIA 会交替交出
    /// 另一个框的 TextPattern / 外框，Y 一变键盘就跳。锁在已授权框里。
    /// 整页 Document 外框不能当锁，只认上一次光标附近。
    /// </summary>
    public static bool CaretBelongsToAuthorizedField(
        NativeRect authorizedBox,
        NativeRect lastCaret,
        NativeRect incomingCaret,
        NativeRect incomingBox,
        int slop = 24)
    {
        if (authorizedBox.IsEmpty && lastCaret.IsEmpty)
        {
            return true;
        }

        if (!authorizedBox.IsEmpty
            && authorizedBox.Bottom - authorizedBox.Top <= 160)
        {
            if (!incomingCaret.IsEmpty
                && incomingCaret.Left >= authorizedBox.Left - slop
                && incomingCaret.Left <= authorizedBox.Right + slop
                && incomingCaret.Top >= authorizedBox.Top - slop
                && incomingCaret.Top <= authorizedBox.Bottom + slop)
            {
                return true;
            }

            if (!incomingBox.IsEmpty
                && incomingBox.Bottom - incomingBox.Top <= 160
                && incomingBox.Left < authorizedBox.Right + slop
                && incomingBox.Right > authorizedBox.Left - slop
                && incomingBox.Top < authorizedBox.Bottom + slop
                && incomingBox.Bottom > authorizedBox.Top - slop)
            {
                return true;
            }
        }

        if (lastCaret.IsEmpty || incomingCaret.IsEmpty)
        {
            return false;
        }

        return Math.Abs(incomingCaret.Top - lastCaret.Top) <= 48
            && Math.Abs(incomingCaret.Left - lastCaret.Left) <= 240;
    }

    /// <summary>
    /// 换框：用户点中了新框，或 TSF 与 UIA 同时指到旧框外面。
    /// 只有 UIA 漂到另一个 Edit 时不换——日志里 uia/text 与 uia/box 互跳就是这条。
    /// </summary>
    public static bool ShouldReplaceAuthorizedField(
        NativeRect authorizedBox,
        NativeRect lastCaret,
        NativeRect incomingCaret,
        NativeRect incomingBox,
        bool incomingFromClicked,
        bool nativeAndUiAgree,
        bool nativeOnly,
        bool focusEntered = false,
        bool incomingCaretTrusted = false,
        string authorizedFieldId = "",
        string incomingFieldId = "",
        bool manualTap = false)
    {
        if (manualTap
            || incomingFromClicked
            || (authorizedBox.IsEmpty && lastCaret.IsEmpty))
        {
            return true;
        }

        if (CaretBelongsToAuthorizedField(
                authorizedBox,
                lastCaret,
                incomingCaret,
                incomingBox))
        {
            return false;
        }

        var differentId =
            !string.IsNullOrEmpty(authorizedFieldId)
            && !string.IsNullOrEmpty(incomingFieldId)
            && !string.Equals(authorizedFieldId, incomingFieldId, StringComparison.Ordinal);

        // 资源管理器搜索框 → 地址栏：UIA 已是新框的 Text 光标，TSF 还停在旧框。
        // 只有 UIA 外框乱跳时不换（CaretIsTrusted=false）。
        return nativeAndUiAgree
            || nativeOnly
            || differentId
            || (focusEntered && incomingCaretTrusted)
            || (incomingCaretTrusted && string.IsNullOrEmpty(authorizedFieldId));
    }

    /// <summary>
    /// 两个矮输入框互不重叠，就是另一处 Edit，不是同行换光标。
    /// 整页 Document 外框不参与，避免误判。
    /// </summary>
    public static bool IsSeparateEditField(
        NativeRect authorizedBox,
        NativeRect incomingBox,
        int slop = 24)
    {
        if (authorizedBox.IsEmpty || incomingBox.IsEmpty)
        {
            return false;
        }

        if (authorizedBox.Bottom - authorizedBox.Top > 160
            || incomingBox.Bottom - incomingBox.Top > 160)
        {
            return false;
        }

        return incomingBox.Right < authorizedBox.Left - slop
            || incomingBox.Left > authorizedBox.Right + slop
            || incomingBox.Bottom < authorizedBox.Top - slop
            || incomingBox.Top > authorizedBox.Bottom + slop;
    }

    /// <summary>
    /// 平板 HID 没有落点时，UIA 会把另一个框交过来。Y、X 都跳开，
    /// 或两个矮框互不重叠，才算离开当前输入框。
    /// </summary>
    public static bool LooksLikeAnotherField(
        NativeRect authorizedBox,
        NativeRect lastCaret,
        NativeRect incomingCaret,
        NativeRect incomingBox)
    {
        if (IsSeparateEditField(authorizedBox, incomingBox))
        {
            return true;
        }

        if (lastCaret.IsEmpty || incomingCaret.IsEmpty)
        {
            return false;
        }

        return Math.Abs(incomingCaret.Top - lastCaret.Top) > 48
            && Math.Abs(incomingCaret.Left - lastCaret.Left) > 240;
    }

    /// <summary>
    /// 已经弹出时，框外手指落到另一个输入框必须收起，不能先跳过去。
    /// 同行点击只换光标，不算离开。
    /// </summary>
    public static bool ShouldHideWhenTapLeavesAuthorizedField(
        bool alreadyVisible,
        bool hasExternalGesture,
        bool caretBelongs,
        bool anotherField,
        bool surfaceChanged = false,
        bool searchSession = false) =>
        !searchSession
        && alreadyVisible
        && hasExternalGesture
        && !caretBelongs
        && anotherField
        && !surfaceChanged;

    public static bool ShouldFollowCaretTap(
        NativeRect previousCaret,
        NativeRect nextCaret,
        int slop = 2) =>
        !previousCaret.IsEmpty
        && !nextCaret.IsEmpty
        && (Math.Abs(previousCaret.Left - nextCaret.Left) > slop
            || Math.Abs(previousCaret.Top - nextCaret.Top) > slop);
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
        var below = exclude.Bottom + Gap;
        var aboveBox = Box(left, above, width, height);
        if (above >= work.Top + Gap && !aboveBox.Intersects(exclude))
        {
            return aboveBox;
        }

        var belowBox = Box(left, below, width, height);
        if (below + height <= work.Bottom - Gap && !belowBox.Intersects(exclude))
        {
            return belowBox;
        }

        var roomBelow = work.Bottom - exclude.Bottom;
        var roomAbove = exclude.Top - work.Top;
        if (roomBelow >= roomAbove && roomBelow > Gap)
        {
            var top = Math.Min(below, work.Bottom - height - Gap);
            return AvoidExclude(
                Box(left, top >= exclude.Bottom ? top : below, width, height),
                exclude,
                left,
                width,
                height,
                work);
        }

        var pinnedAbove = Math.Max(above, work.Top + Gap);
        return AvoidExclude(
            Box(
                left,
                pinnedAbove + height <= exclude.Top ? pinnedAbove : above,
                width,
                height),
            exclude,
            left,
            width,
            height,
            work);
    }

    /// <summary>
    /// 官方：用户必须看得见正在打的框。夹到工作区时宁可溢出，不能穿过排除区。
    /// </summary>
    public static NativeRect AvoidExclude(
        NativeRect box,
        NativeRect exclude,
        int left,
        int width,
        int height,
        NativeRect work)
    {
        if (exclude.IsEmpty || !box.Intersects(exclude))
        {
            return box;
        }

        var above = exclude.Top - Gap - height;
        if (above >= work.Top)
        {
            return Box(left, above, width, height);
        }

        return Box(left, exclude.Bottom + Gap, width, height);
    }

    private static NativeRect ExcludeTypingLine(InputField field, NativeRect caret)
    {
        if (field.FieldBox.IsEmpty)
        {
            return caret;
        }

        // 单行/组合框整框排除。72 太矮，Cursor 组合区经常 80~160。
        if (field.FieldBox.Height <= 160)
        {
            return new NativeRect
            {
                Left = Math.Min(caret.Left, field.FieldBox.Left),
                Top = Math.Min(caret.Top, field.FieldBox.Top),
                Right = Math.Max(caret.Right, field.FieldBox.Right),
                Bottom = Math.Max(caret.Bottom, field.FieldBox.Bottom)
            };
        }

        // uia/box 锚在大框顶边。真实插入点常在底行（聊天组合），
        // 按顶边摆会把整框盖住。
        if (!field.CaretIsTrusted && caret.Top <= field.FieldBox.Top + 24)
        {
            var lineTop = Math.Max(field.FieldBox.Top, field.FieldBox.Bottom - 48);
            return new NativeRect
            {
                Left = field.FieldBox.Left,
                Top = lineTop,
                Right = field.FieldBox.Right,
                Bottom = field.FieldBox.Bottom
            };
        }

        return new NativeRect
        {
            Left = Math.Min(caret.Left, field.FieldBox.Left),
            Top = Math.Max(field.FieldBox.Top, caret.Top - 4),
            Right = Math.Max(caret.Right, field.FieldBox.Right),
            Bottom = Math.Min(
                field.FieldBox.Bottom,
                Math.Max(caret.Bottom, caret.Top + 28) + 4)
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
