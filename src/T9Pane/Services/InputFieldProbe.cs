using System.Windows.Automation;
using System.Windows.Automation.Text;
using T9Pane.Native;

namespace T9Pane.Services;

internal readonly record struct InputContextKey(
    ulong Client,
    uint Epoch,
    ulong View,
    long FocusGeneration)
{
    public bool IsEmpty =>
        Client == 0 && Epoch == 0 && View == 0 && FocusGeneration == 0;

    public static InputContextKey ForFocus(IntPtr topLevel, long generation) =>
        new(unchecked((ulong)topLevel.ToInt64()), 0, 0, generation);
}

internal readonly record struct InputField(
    IntPtr TopLevel,
    NativeRect Caret,
    NativeRect Occluder,
    IntPtr Owner = default,
    InputContextKey Context = default,
    /// <summary>
    /// 光标坐标是否描述真实插入点(而不是按窗口矩形推算出来的)。
    /// 系统表面的首显要据此决定还要不要等更权威的坐标。
    /// </summary>
    bool CaretIsTrusted = false,
    string FieldId = "",
    NativeRect FieldBox = default,
    bool FromClicked = false,
    /// <summary>
    /// 矩形是折叠后的插入点，而不是长按选区的外框。
    /// 选区变大时不能拿来跟打字行，否则键盘会跟着手柄跳。
    /// </summary>
    bool IsInsertionCaret = true,
    bool HasRangeSelection = false);

internal static class InputFieldSelectionPolicy
{
    public static InputField NormalizeDesktopSurface(
        bool systemTextHost,
        InputField field,
        IntPtr foregroundRoot,
        long focusGeneration)
    {
        if (systemTextHost || foregroundRoot == IntPtr.Zero)
        {
            return field;
        }

        return field with
        {
            TopLevel = foregroundRoot,
            Owner = foregroundRoot,
            Context = InputContextKey.ForFocus(
                foregroundRoot,
                focusGeneration)
        };
    }

    public static bool IsSameFocusedGeometry(
        InputField uiField,
        InputField nativeField,
        int verticalTolerance = 8) =>
        uiField.TopLevel != IntPtr.Zero
        && uiField.TopLevel == nativeField.TopLevel
        && !uiField.Caret.IsEmpty
        && !nativeField.Caret.IsEmpty
        && nativeField.Caret.Bottom >= uiField.Caret.Top - verticalTolerance
        && nativeField.Caret.Top <= uiField.Caret.Bottom + verticalTolerance;

    /// <summary>
    /// 首显前是否还要再等一个更权威的坐标。
    ///
    /// 官方 ITfContextView.GetTextExt / TipTsfHelper：只在拿到插入点后摆。
    /// SearchHost 一类 XAML 表面从不提供原生 TSF 字段，所以只看 hasNativeField
    /// 就会让每一次首显都落进定时器兜底——表现就是开始菜单搜索框要多点一下。
    /// 坐标本身已经描述真实插入点时(UIA 光标、或用户刚点中的那个输入框)，
    /// 就没有什么可等的了，直接显示。
    /// uia/box 锚在外框顶边，桌面和系统表面都要等 TextPattern / GetTextExt，
    /// 否则键盘会压住真实行。
    /// </summary>
    public static bool NeedsAuthoritativeFirstShow(
        bool systemTextHost,
        bool hasUiField,
        InputField uiField,
        bool hasNativeField,
        InputField nativeField)
    {
        _ = systemTextHost;
        return hasUiField
            && !uiField.CaretIsTrusted
            && (!hasNativeField || !IsSameFocusedGeometry(uiField, nativeField));
    }

    public static bool TrySelect(
        bool systemTextHost,
        bool hasUiField,
        InputField uiField,
        bool hasNativeField,
        InputField nativeField,
        out InputField field)
    {
        field = default;
        if (hasUiField && hasNativeField && IsSameFocusedGeometry(uiField, nativeField))
        {
            // 资源管理器地址栏点一下会全选，TSF 光标在路径开头，
            // UIA 才有整条地址栏外框。丢掉外框后 480px 光标行套不住
            // 点在栏中段的第一次点击，键盘就要再点一下才出来。
            field = nativeField with
            {
                FieldBox = uiField.FieldBox.IsEmpty ? nativeField.FieldBox : uiField.FieldBox,
                FieldId = string.IsNullOrEmpty(uiField.FieldId) ? nativeField.FieldId : uiField.FieldId,
                FromClicked = uiField.FromClicked || nativeField.FromClicked,
                IsInsertionCaret = nativeField.IsInsertionCaret && uiField.IsInsertionCaret,
                HasRangeSelection = nativeField.HasRangeSelection || uiField.HasRangeSelection
            };
            return true;
        }

        if (hasUiField && hasNativeField)
        {
            // 两个坐标说的不是同一行：UIA 文本光标属于用户刚点的框，
            // 原生 TSF 光标经常还停在上一轮对话框/另一个搜索框上。
            // 只有 UIA 只拿到元素外框时（Word 长文），才继续信原生行坐标。
            field = systemTextHost || uiField.CaretIsTrusted
                ? uiField
                : nativeField;
            return true;
        }

        if (hasUiField)
        {
            field = uiField;
            return true;
        }

        if (hasNativeField)
        {
            field = nativeField;
            return true;
        }

        return false;
    }
}

internal static class SystemFallbackPolicy
{
    public static bool ShouldUse(
        bool systemTextSurface,
        bool hasProfileLease,
        bool nativeContextActive) =>
        systemTextSurface && hasProfileLease && !nativeContextActive;
}

internal static class SystemBackspacePolicy
{
    public static bool ShouldUseUia(
        bool foregroundSystemTextSurface,
        bool hasProfileLease,
        bool hasCapturedSystemTarget) =>
        hasCapturedSystemTarget
        || (foregroundSystemTextSurface && hasProfileLease);
}

internal static class AutomationSurfacePolicy
{
    public static bool AcceptsFocusedProcess(
        uint topPid,
        uint focusedPid,
        bool allowSystemBroker,
        bool focusedProcessIsBroker,
        bool intersectsTop,
        bool intersectsSearch,
        bool intersectsForeground = false,
        bool applicationFrame = false) =>
        topPid == 0
        || focusedPid == topPid
        || (applicationFrame && intersectsTop)
        || (allowSystemBroker
            && focusedProcessIsBroker
            && (intersectsTop || intersectsSearch || intersectsForeground));
}

internal enum PointerInputHit
{
    Unavailable,
    Outside,
    Inside
}

internal static class InputInvocationProbe
{
    public static bool Contains(NativeRect bounds, int x, int y, int tolerance = 0) =>
        !bounds.IsEmpty
        && x >= bounds.Left - tolerance
        && x < bounds.Right + tolerance
        && y >= bounds.Top - tolerance
        && y < bounds.Bottom + tolerance;

    public static bool IsPointInsideFocusedInput(int x, int y)
        => HitTestFocusedInput(x, y) == PointerInputHit.Inside;

    /// <summary>落点本身就是可输入的文本框。</summary>
    public static bool IsTextField(ControlType type) =>
        type == ControlType.Edit || type == ControlType.ComboBox;

    /// <summary>
    /// 点在这些控件上说明用户是在操作控件而不是要输入文本，往上找祖先也没有意义
    /// ——工具栏按钮的祖先链上照样挂着可输入的容器。
    /// </summary>
    public static bool StopsAtControl(ControlType type) =>
        type == ControlType.Button
        || type == ControlType.CheckBox
        || type == ControlType.RadioButton
        || type == ControlType.MenuItem
        || type == ControlType.Menu
        || type == ControlType.TabItem
        || type == ControlType.ListItem
        || type == ControlType.TreeItem
        || type == ControlType.DataItem
        || type == ControlType.Hyperlink
        || type == ControlType.Slider
        || type == ControlType.ScrollBar
        || type == ControlType.Thumb;

    /// <summary>
    /// 焦点落到这个控件是否表示用户离开了输入。
    /// TreeItem/DataItem 是 Cursor 文件树、资源管理器侧栏。
    /// ListItem 在搜索联想里仍属同一文档，由 searchSession 挡住；
    /// 键盘已经出来后点侧栏列表要收。
    /// </summary>
    public static bool SignalsLeftTextInput(ControlType type) =>
        StopsAtControl(type)
        && type != ControlType.Thumb
        && type != ControlType.ScrollBar;

    /// <summary>
    /// 官方 RequireTouchInEditControl：点在侧栏/页面上，即使稍后程序把焦点
    /// 送进编辑框也不弹。地址栏旁边的 Button 不是这一类。
    /// </summary>
    public static bool IsHardLeaveControl(ControlType type) =>
        type == ControlType.TreeItem
        || type == ControlType.ListItem
        || type == ControlType.DataItem
        || type == ControlType.TabItem
        || type == ControlType.Hyperlink;

    /// <summary>
    /// 长按之后的选区手柄和选区菜单。地址栏工具带、普通按钮不算。
    /// </summary>
    public static bool IsContextMenu(ControlType type) =>
        type == ControlType.Menu || type == ControlType.MenuItem;

    public static bool IsSelectionHandle(ControlType type) =>
        type == ControlType.Thumb;

    public static bool IsSelectionChrome(ControlType type) =>
        IsContextMenu(type) || IsSelectionHandle(type);

    public static bool IsConsoleOrTerminal(AutomationElement? element)
    {
        if (element is null)
        {
            return false;
        }

        try
        {
            var hwnd = new IntPtr(element.Current.NativeWindowHandle);
            var foreground = NativeMethods.GetAncestor(
                NativeMethods.GetForegroundWindow(),
                NativeMethods.GaRoot);
            return ConsoleInputSurface.IsEditable(
                element.Current.ControlType,
                element.Current.Name,
                element.Current.AutomationId,
                hwnd,
                foreground);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Chromium 的 contenteditable 常报成 Document/Group，不是 Edit。
    /// 对话框那种矮框可以当成输入区；覆盖整页的 Document 是页面本身，点它就是离开。
    /// </summary>
    public static bool IsCompactEditable(
        ControlType type,
        double width,
        double height,
        bool keyboardFocusable) =>
        keyboardFocusable
        && (type == ControlType.Document
            || type == ControlType.Group
            || type == ControlType.Custom)
        && width >= 40
        && height is >= 12 and <= 160;

    /// <summary>
    /// 整页 Document/Pane/Window。点网页空白时焦点常落到这里，不是输入框。
    /// </summary>
    public static bool SignalsLeftPageSurface(
        ControlType type,
        double width,
        double height,
        bool keyboardFocusable) =>
        StopsAtContainer(type)
        && !IsCompactEditable(type, width, height, keyboardFocusable)
        && height > 160;

    /// <summary>
    /// 走到这些容器就该停。它们本身不是输入框，而且 Chromium 会把整个网页暴露成
    /// 一个覆盖全窗口的 Document，继续往上找会把窗口里任何一次点击都判成命中。
    /// </summary>
    public static bool StopsAtContainer(ControlType type) =>
        type == ControlType.Document
        || type == ControlType.Pane
        || type == ControlType.Window;

    /// <summary>
    /// FromPoint 停在容器上时的落点判定。
    /// 紧凑可编辑框、点中的子输入、或落在当前授权框里：Inside。
    /// 其余是看不清，不是离开——新框的 UIA 经常还没挂到树上，收成 Outside
    /// 会把换框的第一次点击吃掉。离开由“落点不在授权框里”单独处理。
    /// </summary>
    /// <summary>
    /// 资源管理器地址栏未进入编辑前，落点是面包屑按钮，不是 Edit。
    /// 面包屑在 ComboBox / 地址栏容器里：等它变成输入框，不能当场收成 Outside。
    /// 普通工具栏按钮仍是离开。
    /// </summary>
    public static PointerInputHit ClassifyAddressBandChrome(
        bool inComboBox,
        bool inAddressBand) =>
        inComboBox
            ? PointerInputHit.Inside
            : inAddressBand
                ? PointerInputHit.Unavailable
                : PointerInputHit.Outside;

    private static PointerInputHit ClassifyStoppedControl(AutomationElement node)
    {
        try
        {
            var ancestor = TreeWalker.ControlViewWalker.GetParent(node);
            for (var depth = 0; ancestor is not null && depth < 6; depth++)
            {
                var type = ancestor.Current.ControlType;
                if (type == ControlType.ComboBox || type == ControlType.Edit)
                {
                    NoteClickedField(ancestor);
                    return ClassifyAddressBandChrome(inComboBox: true, inAddressBand: false);
                }

                var box = ancestor.Current.BoundingRectangle;
                if (LooksLikeAddressBand(
                        ancestor.Current.Name,
                        ancestor.Current.AutomationId,
                        box.Height))
                {
                    return ClassifyAddressBandChrome(inComboBox: false, inAddressBand: true);
                }

                if (StopsAtContainer(type))
                {
                    break;
                }

                ancestor = TreeWalker.ControlViewWalker.GetParent(ancestor);
            }
        }
        catch
        {
            // 树在面包屑切到编辑框时会拆掉。
        }

        return PointerInputHit.Outside;
    }

    public static bool LooksLikeAddressBand(
        string? name,
        string? automationId,
        double height) =>
        height is >= 16 and <= 64
        && (HasAddressBandToken(name) || HasAddressBandToken(automationId));

    private static bool HasAddressBandToken(string? text) =>
        !string.IsNullOrWhiteSpace(text)
        && (text.Contains("Address", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Breadcrumb", StringComparison.OrdinalIgnoreCase)
            || text.Contains("addressband", StringComparison.OrdinalIgnoreCase)
            || text.Contains("地址", StringComparison.Ordinal));

    public static PointerInputHit ClassifyContainerHit(
        bool compactEditable,
        bool foundCompactChild,
        bool clickInsideAuthorizedField = false,
        bool consoleOrTerminal = false) =>
        compactEditable || foundCompactChild || clickInsideAuthorizedField || consoleOrTerminal
            ? PointerInputHit.Inside
            : PointerInputHit.Unavailable;

    /// <summary>
    /// 焦点在文本框上但落点不在那个框里：焦点只用来认框，认完对不上就退回落点本身。
    /// 不能把 Unavailable 改成 Outside，否则换框时第一次点击会被立刻收掉。
    /// </summary>
    public static PointerInputHit ClassifyMissedFocusedField(PointerInputHit pointHit) =>
        pointHit;

    /// <summary>祖先链最多走几层。输入框外面通常只隔着一两层包装节点。</summary>
    private const int AncestorProbeDepth = 5;

    private static readonly object ClickedGate = new();
    private static AutomationElement? _clickedField;
    private static long _clickedTicks;

    private static void NoteClickedField(AutomationElement element)
    {
        lock (ClickedGate)
        {
            _clickedField = element;
            _clickedTicks = DateTime.UtcNow.Ticks;
        }
    }

    /// <summary>
    /// 新的一次物理点击、或焦点离开当前表面时必须清掉，否则会拿旧框去定位，
    /// 键盘就落在上一个点击的位置。
    ///
    /// 注意只能在"新点击"时清，不能在每次重判时清：同一次点击要反复重判
    /// (表面交接需要时间)，那时清掉会把已经认出来的输入框丢掉。
    /// </summary>
    internal static void ClearClickedField()
    {
        lock (ClickedGate)
        {
            _clickedField = null;
            _clickedTicks = 0;
        }
    }

    /// <summary>
    /// 用户最近一次点中的、且**当前仍持有键盘焦点**的输入框。
    ///
    /// 焦点校验不能省。官方模型里"焦点移到非文本控件"就是隐藏条件，
    /// 少了这一步，焦点离开后这个框还在交坐标，键盘就赖在原地不走；
    /// 在同一个应用里换输入框时，交出来的还是上一个框的位置。
    /// </summary>
    internal static AutomationElement? RecentClickedField(TimeSpan maxAge)
    {
        AutomationElement? candidate;
        lock (ClickedGate)
        {
            if (_clickedField is null || _clickedTicks == 0)
            {
                return null;
            }

            var age = DateTime.UtcNow.Ticks - _clickedTicks;
            if (age < 0 || age > maxAge.Ticks)
            {
                return null;
            }

            candidate = _clickedField;
        }

        try
        {
            // 不能要求它现在仍持有焦点。Cursor 会把焦点留在聊天框，
            // 用户点的是对话框；要焦点就会把点中的框丢掉，键盘摆到错位置。
            // 焦点离开到按钮由 ClearClickedField / FocusLeft 处理。
            _ = candidate.Current.BoundingRectangle;
            return candidate;
        }
        catch
        {
            // 元素已随宿主销毁。
            ClearClickedField();
            return null;
        }
    }

    /// <summary>
    /// 判断这一次物理点击是不是"点进了文本输入区"。
    ///
    /// 必须问落点的控件，不能问当前焦点元素：鼠标按下的瞬间焦点还没移到刚点的框上，
    /// 拿焦点框的外框去套点击坐标必然落空，于是真实的文本框点击被判成"不是输入意图"，
    /// 用户就得再点一次才弹。FocusedElement 本身在跨进程交接时还会额外滞后。
    ///
    /// 落点常常是输入框内部的子节点（占位文字、行容器），所以要向上找几层；
    /// 但遇到按钮一类可操作控件、或到了整页容器，就必须停下来。
    /// </summary>
    public static PointerInputHit HitTestPointerTarget(
        int x,
        int y,
        NativeRect authorizedField = default)
    {
        if (OfficialSipHit.IsKeyboardSurface(x, y))
        {
            return PointerInputHit.Unavailable;
        }

        try
        {
            var node = AutomationElement.FromPoint(new System.Windows.Point(x, y));
            for (var depth = 0; node is not null && depth < AncestorProbeDepth; depth++)
            {
                var type = node.Current.ControlType;
                if (IsTextField(type))
                {
                    // 记下来给定位复用。UIA 焦点常常报在包裹输入框的容器上，
                    // 那时要靠这个元素才能知道插入点在哪；去子树里搜是跨进程
                    // 全量遍历，放在每秒跑好几次的定位路径上会直接把程序拖死。
                    NoteClickedField(node);
                    return PointerInputHit.Inside;
                }

                if (StopsAtControl(type))
                {
                    return ClassifyStoppedControl(node);
                }

                if (StopsAtContainer(type))
                {
                    var box = node.Current.BoundingRectangle;
                    var compact = IsCompactEditable(
                        type,
                        box.Width,
                        box.Height,
                        node.Current.IsKeyboardFocusable || node.Current.HasKeyboardFocus);
                    if (compact || IsConsoleOrTerminal(node))
                    {
                        NoteClickedField(node);
                        return PointerInputHit.Inside;
                    }

                    if (TryFindCompactChildAt(node, x, y) is { } child)
                    {
                        NoteClickedField(child);
                        return PointerInputHit.Inside;
                    }

                    return ClassifyContainerHit(
                        compactEditable: false,
                        foundCompactChild: false,
                        clickInsideAuthorizedField: Contains(authorizedField, x, y, EdgeTolerance),
                        consoleOrTerminal: false);
                }

                node = TreeWalker.ControlViewWalker.GetParent(node);
            }
        }
        catch
        {
            // AppContainer 里的 UWP 表面可能拒绝按点命中，退回焦点框判定。
        }

        return ClassifyContainerHit(
            compactEditable: false,
            foundCompactChild: false,
            clickInsideAuthorizedField: Contains(authorizedField, x, y, EdgeTolerance));
    }

    /// <summary>
    /// FromPoint 常停在整页 Document 上。只看它的直接子级（不是全树 Descendants），
    /// 对话框那种紧凑输入框就挂在这一层或再下一层。
    /// </summary>
    private static AutomationElement? TryFindCompactChildAt(
        AutomationElement parent,
        int x,
        int y)
    {
        try
        {
            var child = TreeWalker.ControlViewWalker.GetFirstChild(parent);
            for (var i = 0; child is not null && i < 16; i++)
            {
                var box = child.Current.BoundingRectangle;
                if (!box.IsEmpty
                    && x >= box.Left
                    && x < box.Right
                    && y >= box.Top
                    && y < box.Bottom)
                {
                    var type = child.Current.ControlType;
                    if (IsTextField(type)
                        || IsCompactEditable(
                            type,
                            box.Width,
                            box.Height,
                            child.Current.IsKeyboardFocusable || child.Current.HasKeyboardFocus))
                    {
                        return child;
                    }

                    var nested = TreeWalker.ControlViewWalker.GetFirstChild(child);
                    for (var j = 0; nested is not null && j < 16; j++)
                    {
                        var nestedBox = nested.Current.BoundingRectangle;
                        if (!nestedBox.IsEmpty
                            && x >= nestedBox.Left
                            && x < nestedBox.Right
                            && y >= nestedBox.Top
                            && y < nestedBox.Bottom)
                        {
                            var nestedType = nested.Current.ControlType;
                            if (IsTextField(nestedType)
                                || IsCompactEditable(
                                    nestedType,
                                    nestedBox.Width,
                                    nestedBox.Height,
                                    nested.Current.IsKeyboardFocusable
                                    || nested.Current.HasKeyboardFocus))
                            {
                                return nested;
                            }
                        }

                        nested = TreeWalker.ControlViewWalker.GetNextSibling(nested);
                    }
                }

                child = TreeWalker.ControlViewWalker.GetNextSibling(child);
            }
        }
        catch
        {
            // 子树在探测中消失。
        }

        return null;
    }

    /// <summary>
    /// 这一次物理点击是否表示"用户要在文本框里输入"。
    ///
    /// 用户的要求是"必须手动点到文本区才弹"，所以**落点**是唯一的肯定判据，
    /// 焦点只用来确定"落点属于哪个框"。
    ///
    /// 焦点单独不能当判据。切换会话、切到前台这类操作会把焦点自动放进输入框，
    /// 只看"焦点是不是文本框"就等于任何一次点击都命中——Unigram 切群组时
    /// 点群组列表也会弹出键盘，正是这个原因。
    ///
    /// 但也不能只问 FromPoint：鼠标按下那一刻焦点还没交接完，点在输入框内边距上时
    /// FromPoint 返回的是外层容器，明明点在框里却判不出来，用户得再点一次。
    /// 所以焦点落到文本框之后，再用它的矩形去核对这一下的落点——矩形本身就含内边距，
    /// 核对通过说明用户点的确实是这个框。待决点击会被重判，焦点交接完成后自然对上。
    /// </summary>
    public static PointerInputHit HitTestFocusedInput(
        int x,
        int y,
        NativeRect authorizedField = default,
        PointerInputHit? probedTarget = null)
    {
        var clickedTextField = probedTarget
            ?? HitTestPointerTarget(x, y, authorizedField);
        if (clickedTextField == PointerInputHit.Inside)
        {
            return PointerInputHit.Inside;
        }

        try
        {
            var cache = FocusedFieldCache.Fresh(FocusHandoffWindow);
            var live = AutomationElement.FocusedElement;
            var cacheHit = ClassifyFocusedClick(cache, x, y, clickedTextField);
            if (cacheHit == PointerInputHit.Inside)
            {
                return PointerInputHit.Inside;
            }

            var liveHit = ReferenceEquals(cache, live)
                ? cacheHit
                : ClassifyFocusedClick(live, x, y, clickedTextField);
            if (liveHit == PointerInputHit.Inside)
            {
                return PointerInputHit.Inside;
            }

            if (cacheHit == PointerInputHit.Outside || liveHit == PointerInputHit.Outside)
            {
                return PointerInputHit.Outside;
            }

            return clickedTextField;
        }
        catch
        {
            return clickedTextField;
        }
    }

    /// <summary>
    /// 当前 UIA 焦点是不是输入框。给 TSF 文档离开用：换框途中焦点仍在 Edit
    /// 就还不能藏；落到整页 Document / 按钮才是真离开。
    /// 必须问 live FocusedElement，不能用焦点缓存——旧 Edit 会把失焦挡住。
    /// </summary>
    public static bool FocusedIsLeaveControl()
    {
        try
        {
            var focused = AutomationElement.FocusedElement;
            if (focused is null)
            {
                return false;
            }

            var type = focused.Current.ControlType;
            if (IsSelectionChrome(type) || IsTextField(type))
            {
                return false;
            }

            if (SignalsLeftTextInput(type))
            {
                return true;
            }

            var box = focused.Current.BoundingRectangle;
            return SignalsLeftPageSurface(
                type,
                box.Width,
                box.Height,
                focused.Current.IsKeyboardFocusable || focused.Current.HasKeyboardFocus);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 焦点落定后（官方 IsUIBusy=False / TipTsfHelper Input 优先级）再问：
    /// 是不是点在侧栏、页面上。Button 不算，留给地址栏铬。
    /// </summary>
    public static bool FocusedIsOwnPane()
    {
        try
        {
            var focused = CurrentFocusedElement() ?? AutomationElement.FocusedElement;
            return focused is not null
                && TrayFocusPolicy.IgnoreOwnProcess(
                    unchecked((uint)focused.Current.ProcessId),
                    unchecked((uint)Environment.ProcessId));
        }
        catch
        {
            return false;
        }
    }

    public static bool FocusedIsHardLeave()
    {
        try
        {
            var focused = CurrentFocusedElement();
            if (focused is null || FocusedIsOwnPane())
            {
                return false;
            }

            var type = focused.Current.ControlType;
            if (IsSelectionChrome(type) || IsTextField(type) || IsConsoleOrTerminal(focused))
            {
                return false;
            }

            if (IsHardLeaveControl(type))
            {
                return true;
            }

            var box = focused.Current.BoundingRectangle;
            return SignalsLeftPageSurface(
                type,
                box.Width,
                box.Height,
                focused.Current.IsKeyboardFocusable || focused.Current.HasKeyboardFocus);
        }
        catch
        {
            return false;
        }
    }

    public static bool FocusedLooksLikeTextInput()
    {
        try
        {
            var focused = CurrentFocusedElement();
            if (focused is null)
            {
                return false;
            }

            var type = focused.Current.ControlType;
            if (IsTextField(type) || IsConsoleOrTerminal(focused))
            {
                return true;
            }

            var box = focused.Current.BoundingRectangle;
            return IsCompactEditable(
                type,
                box.Width,
                box.Height,
                focused.Current.IsKeyboardFocusable || focused.Current.HasKeyboardFocus);
        }
        catch
        {
            return false;
        }
    }

    private static PointerInputHit ClassifyFocusedClick(
        AutomationElement? focused,
        int x,
        int y,
        PointerInputHit fallback)
    {
        if (focused is null
            || !focused.Current.IsEnabled
            || !focused.Current.HasKeyboardFocus)
        {
            return fallback;
        }

        var type = focused.Current.ControlType;
        if (IsTextField(type))
        {
            if (ClickLandedOn(focused, x, y))
            {
                NoteClickedField(focused);
                return PointerInputHit.Inside;
            }

            return ClassifyMissedFocusedField(fallback);
        }

        if (StopsAtContainer(type))
        {
            var box = focused.Current.BoundingRectangle;
            if ((IsCompactEditable(
                    type,
                    box.Width,
                    box.Height,
                    focused.Current.IsKeyboardFocusable || focused.Current.HasKeyboardFocus)
                || IsConsoleOrTerminal(focused))
                && ClickLandedOn(focused, x, y))
            {
                NoteClickedField(focused);
                return PointerInputHit.Inside;
            }

            return fallback;
        }

        if (StopsAtControl(type))
        {
            return PointerInputHit.Outside;
        }

        return fallback;
    }

    /// <summary>
    /// 落点是否在这个输入框上。外框已经含内边距，所以只额外放宽边框那几像素，
    /// 用来兜住点在框线上的情况。放宽得再多就会把紧挨输入框的按钮也算进来。
    /// </summary>
    private const int EdgeTolerance = 3;

    private static bool ClickLandedOn(AutomationElement field, int x, int y)
    {
        try
        {
            var box = field.Current.BoundingRectangle;
            if (box.IsEmpty)
            {
                return false;
            }

            var slop = TouchDevicePolicy.EdgeTolerance(TouchDevicePolicy.CurrentPreferTouchHitSlop());
            return x >= box.Left - slop
                && x < box.Right + slop
                && y >= box.Top - slop
                && y < box.Bottom + slop;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 焦点事件送来的元素能盖过 FocusedElement 多久。跨进程交接焦点时
    /// FocusedElement 会在这段时间里继续返回旧元素。
    /// </summary>
    private static readonly TimeSpan FocusHandoffWindow =
        TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// AutomationElement.FocusedElement 在跨进程焦点交接后会继续返回旧
    /// Edit。焦点事件送来的元素才是这一拍的权威焦点，必须先看缓存。
    /// 第一下点到按钮时若仍读旧 Edit，键盘会 Stay，第二下才收。
    /// </summary>
    private static AutomationElement? CurrentFocusedElement() =>
        FocusedFieldCache.Fresh(FocusHandoffWindow)
        ?? AutomationElement.FocusedElement;

}

internal static class InputFieldProbe
{
    private static readonly CaretQualityGate Quality = new();

    public static bool TryGetFocusedTaskbarSearch(out InputField field)
    {
        field = default;
        var top = Root(NativeMethods.GetForegroundWindow());
        if (top == IntPtr.Zero
            || ShellProcess.Name(top) != "explorer"
            || !ShellProcess.IsTrayChrome(top))
        {
            return false;
        }

        var caret = TryReadAutomation(top, allowSystemBroker: true);
        if (caret.IsEmpty)
        {
            return false;
        }

        field = new InputField(
            top,
            caret,
            default,
            CaretIsTrusted: CaretQualityGate.Rank($"uia/{LastAutomationSource}") >= 3,
            FieldId: LastFieldId,
            FieldBox: LastFieldBox,
            IsInsertionCaret: LastCaretIsInsertion,
            HasRangeSelection: LastHasRangeSelection);
        return true;
    }

    public static bool TryGet(IReadOnlyCollection<IntPtr> ignored, IntPtr hint, out InputField field)
    {
        field = default;
        var top = Root(NativeMethods.GetForegroundWindow());
        if (top == IntPtr.Zero || ignored.Contains(top))
        {
            top = Root(hint);
        }

        if (top == IntPtr.Zero || ignored.Contains(top))
        {
            return false;
        }

        if (ShellProcess.IsTrayChrome(top) && !ShellProcess.IsSystemFlyout(top))
        {
            if (ShellProcess.Name(top) != "explorer"
                || !ShellProcess.TryFindVisibleSearch(out top, out _))
            {
                return false;
            }
        }
        else if (ShellProcess.HandsOffToSearchHost(ShellProcess.Name(top))
                 && ShellProcess.TryFindVisibleSearch(out var searchHost, out _))
        {
            top = searchHost;
        }

        NativeMethods.GetWindowRect(top, out var window);
        var systemSurface = ShellProcess.IsSystemFlyout(top) || ShellProcess.IsSearch(top);
        var framedSurface = ShellProcess.IsApplicationFrameWindow(top);
        var uiaFirst = systemSurface || framedSurface;
        var occluder = systemSurface
            ? window
            : default;
        LastFieldId = string.Empty;
        LastFieldBox = default;
        LastCaretIsInsertion = true;
        LastHasRangeSelection = false;
        // 必须先问点中的框。Cursor 会把焦点留在聊天框，UIA/TSF 光标都是那个框的；
        // 先读焦点就会把键盘摆到错位置。注释一直这么写，实现却先读了焦点。
        var caret = TryReadClickedFieldCaret(out var source);
        var fromClicked = !caret.IsEmpty;
        if (caret.IsEmpty)
        {
            caret = uiaFirst
                ? TryReadAutomation(top, allowSystemBroker: true)
                : TryReadCaret();
            source = uiaFirst ? $"uia/{LastAutomationSource}" : "caret";
        }

        if (caret.IsEmpty)
        {
            caret = uiaFirst
                ? TryReadCaret()
                : TryReadAutomation(top, allowSystemBroker: framedSurface);
            source = uiaFirst ? "caret" : $"uia/{LastAutomationSource}";
        }

        if (caret.IsEmpty && ConsoleInputSurface.IsWindow(top))
        {
            caret = TryReadConsoleInsertion(top, element: null);
            source = "console";
        }

        // 这里原先还有一条"按窗口矩形编造坐标"的兜底(FindSearchBox)。已经删除：
        // 它对 XAML 表面枚举不到子窗口，只能拿窗口矩形推算，实测给出的
        // (28,73)/(85,73) 之类的值与真实的两个搜索框位置(y=87 和 y=1037)都不符。
        // 更糟的是它比真坐标先到，会把"允许重新定位"这一次机会消耗掉，等 100~600ms
        // 后真坐标到达时键盘已经不能再移动了——这就是"键盘停在上一个点击位置"。
        // 官方的候选窗定位模型里没有推测坐标：只在拿到权威矩形时才移动窗口。
        // 拿不到就这一轮不给结果，由重试驱动下一轮，实测真坐标 100~600ms 内必到。
        if (caret.IsEmpty)
        {
            LogOnce(
                $"取输入框失败 表面={ShellProcess.Name(top)} 来源=无 "
                + $"否因={(LastReject.Length == 0 ? "未记录" : LastReject)}");
            return false;
        }

        var held = Quality.Apply(top, LastFieldId, ref caret, ref source);
        LogOnce(
            $"取输入框 表面={ShellProcess.Name(top)} 来源={source}{(held ? "(沿用)" : "")} "
            + $"光标=({caret.Left},{caret.Top})-({caret.Right},{caret.Bottom})");

        // GetTextExt / TextPattern 才是插入点。uia/box 锚在外框顶边，
        // 官方候选窗不会拿 BoundingRectangle 去 CFS_EXCLUDE。
        field = new InputField(
            top,
            caret,
            occluder,
            CaretIsTrusted: CaretQualityGate.Rank(source) >= 3 || fromClicked,
            FieldId: LastFieldId,
            FieldBox: LastFieldBox,
            FromClicked: fromClicked,
            IsInsertionCaret: LastCaretIsInsertion,
            HasRangeSelection: LastHasRangeSelection);
        return true;
    }

    private static NativeRect TryReadCaret()
    {
        var info = new GuiThreadInfo { Size = System.Runtime.InteropServices.Marshal.SizeOf<GuiThreadInfo>() };
        if (!NativeMethods.GetGUIThreadInfo(0, ref info) || info.Caret == IntPtr.Zero || info.CaretRect.IsEmpty)
        {
            return default;
        }

        var tl = new NativePoint { X = info.CaretRect.Left, Y = info.CaretRect.Top };
        var br = new NativePoint { X = info.CaretRect.Right, Y = info.CaretRect.Bottom };
        if (!NativeMethods.ClientToScreen(info.Caret, ref tl) || !NativeMethods.ClientToScreen(info.Caret, ref br))
        {
            return default;
        }

        LastCaretIsInsertion = true;
        LastHasRangeSelection = false;
        return new NativeRect
        {
            Left = tl.X,
            Top = tl.Y,
            Right = Math.Max(tl.X + 2, br.X),
            Bottom = Math.Max(tl.Y + 18, br.Y)
        };
    }

    private static NativeRect TryReadAutomation(IntPtr top, bool allowSystemBroker = false)
    {
        try
        {
            // 优先用焦点事件送来的元素。FocusedElement 在跨进程交接焦点后会滞后
            // 一小段时间继续返回旧元素，点击瞬间正好落在这段窗口里。
            var focused = FocusedFieldCache.Fresh(TimeSpan.FromMilliseconds(500))
                ?? AutomationElement.FocusedElement;
            if (focused is null)
            {
                LastReject = "无焦点元素";
                return default;
            }

            NativeMethods.GetWindowThreadProcessId(top, out var topPid);
            var focusedPid = unchecked((uint)focused.Current.ProcessId);

            var type = focused.Current.ControlType;
            // Chromium 报出来的焦点元素并不总是输入框本身。contenteditable 的输入框
            // 拿到焦点时，UIA 焦点会落在包裹它的容器上——实测 Cursor 的对话框会报
            // Document(覆盖整个窗口)，也会报 Group。白名单只收 Edit/ComboBox
            // 就等于永远取不到光标，键盘也就不弹。
            // 这些容器的外框不能当输入框外框用(Document 的外框就是整个窗口)，
            // 但可以从它们出发去找真实插入点。
            var container = focusedPid == topPid
                && (type == ControlType.Document
                    || type == ControlType.Group
                    || type == ControlType.Custom);
            if (!container && type != ControlType.Edit && type != ControlType.ComboBox)
            {
                LastReject = $"控件类型={type.ProgrammaticName}";
                return default;
            }

            if (container)
            {
                return TryReadContainerCaret(focused);
            }

            var bounds = focused.Current.BoundingRectangle;
            // 上限放宽到多行文本框：真正的光标位置由 TextPattern 给出，
            // 外框高度只用来校验元素属于目标表面。
            if (bounds.IsEmpty || bounds.Height is < 12 or > 600)
            {
                LastReject = $"外框高度={(bounds.IsEmpty ? "空" : ((int)bounds.Height).ToString())}";
                return default;
            }

            var boundsRect = new NativeRect
            {
                Left = (int)bounds.Left,
                Top = (int)bounds.Top,
                Right = (int)bounds.Right,
                Bottom = (int)bounds.Bottom
            };
            var intersectsTop = false;
            var intersectsSearch = false;
            var intersectsForeground = false;
            if (topPid != 0 && focusedPid != topPid)
            {
                NativeMethods.GetWindowRect(top, out var topRect);
                intersectsTop = topRect.Intersects(boundsRect);
                intersectsSearch = ShellProcess.TryFindVisibleSearch(
                    out _,
                    out var searchRect)
                    && searchRect.Intersects(boundsRect);
                // 点进开始菜单搜索后，输入焦点可能仍在 StartMenuExperienceHost 的树里，
                // 而 top 已经重定向到 SearchHost。换页过程中两个窗口的矩形并不重合，
                // 只比对 top 会把真实输入框判为"不属于本表面"，退回编造坐标。
                if (!intersectsTop && !intersectsSearch)
                {
                    var foregroundRoot = NativeMethods.GetAncestor(
                        NativeMethods.GetForegroundWindow(),
                        NativeMethods.GaRoot);
                    intersectsForeground = foregroundRoot != IntPtr.Zero
                        && NativeMethods.GetWindowRect(foregroundRoot, out var fgRect)
                        && fgRect.Intersects(boundsRect);
                }
            }
            if (!AutomationSurfacePolicy.AcceptsFocusedProcess(
                    topPid,
                    focusedPid,
                    allowSystemBroker,
                    ShellProcess.IsSystemTextClient(focusedPid),
                    intersectsTop,
                    intersectsSearch,
                    intersectsForeground,
                    ShellProcess.IsApplicationFrameWindow(top)))
            {
                LastReject = $"归属校验 pid={focusedPid} 表面pid={topPid}";
                return default;
            }

            RememberField(focused);
            var caret = TryReadTextPatternCaret(focused);
            if (!caret.IsEmpty)
            {
                LastAutomationSource = "text";
                LastReject = string.Empty;
                return caret;
            }

            LastAutomationSource = "box";
            LastReject = string.Empty;

            // 没有 TextPattern 时只能用外框。高度必须收窄成光标的量级：
            // 多行框的外框有上百像素高，整框当光标会把键盘放到离插入点很远的地方。
            return new NativeRect
            {
                Left = boundsRect.Left + 4,
                Top = boundsRect.Top + 2,
                Right = boundsRect.Left + 6,
                Bottom = boundsRect.Top
                    + Math.Clamp(boundsRect.Height - 2, 18, CaretFallbackHeight)
            };
        }
        catch (Exception ex)
        {
            LastReject = $"异常={ex.GetType().Name}";
            return default;
        }
    }

    /// <summary>
    /// UIA 焦点报在容器节点(Document/Group)上时的定位。
    ///
    /// 先要求宿主确有 TSF 编辑上下文：否则用户只是在只读的历史消息里选中一段文字，
    /// 甚至只是把窗口切到前台，也会被当成要输入而弹出键盘。
    ///
    /// 然后按两条路取插入点。选区优先——它就是光标所在处；输入框为空时选区是退化的、
    /// 前后都扩不出字符，取不到矩形，这时改去子树里找真正带键盘焦点的输入控件。
    /// Chromium 会在那个节点上置 HasKeyboardFocus，即便 UIA 焦点报在容器上。
    /// </summary>
    private static NativeRect TryReadContainerCaret(AutomationElement container)
    {
        // 唯一可用的依据是用户点中的那个输入框。
        //
        // 绝不能去读容器自己的 TextPattern 选区：那是**页面级**选区，与焦点输入框的
        // 插入点没有必然关系，可以落在页面里任何地方。实测它会把键盘定位到上一轮
        // 对话框上——一个自信但错误的坐标，比取不到更糟，因为取不到还能退到别的依据。
        var editor = InputInvocationProbe.RecentClickedField(ClickedFieldLifetime);
        if (editor is null)
        {
            LastReject = "容器无点中的输入框";
            return default;
        }

        // 点中的框自己的选区是可信的：它属于那个框，不是页面级选区。
        RememberField(editor);
        var caret = TryReadTextPatternCaret(editor);
        if (!caret.IsEmpty)
        {
            LastAutomationSource = "text";
            LastReject = string.Empty;
            return caret;
        }

        var box = CaretFromElementBox(editor);
        if (!box.IsEmpty)
        {
            LastAutomationSource = "box";
            LastReject = string.Empty;
            return box;
        }

        if (InputInvocationProbe.IsConsoleOrTerminal(editor)
            || ConsoleInputSurface.IsWindow(ElementHwnd(editor)))
        {
            var console = TryReadConsoleInsertion(ElementHwnd(editor), editor);
            if (!console.IsEmpty)
            {
                LastAutomationSource = "console";
                LastReject = string.Empty;
                return console;
            }
        }

        LastReject = "点中的输入框外框不可用";
        return default;
    }

    /// <summary>
    /// 把输入框外框折算成一个光标量级的矩形。整框不能直接当光标用：
    /// 多行框有上百像素高，会把键盘放到离插入点很远的地方。
    /// </summary>
    private static NativeRect CaretFromElementBox(AutomationElement element)
    {
        try
        {
            var box = element.Current.BoundingRectangle;
            if (box.IsEmpty || box.Height is < 12 or > 600)
            {
                return default;
            }

            LastCaretIsInsertion = false;
            LastHasRangeSelection = false;
            return new NativeRect
            {
                Left = (int)box.Left + 4,
                Top = (int)box.Top + 2,
                Right = (int)box.Left + 6,
                Bottom = (int)box.Top
                    + Math.Clamp((int)box.Height - 2, 18, CaretFallbackHeight)
            };
        }
        catch
        {
            return default;
        }
    }

    /// <summary>
    /// 用户刚点中的那个输入框给出的位置。
    ///
    /// 这是**比 UIA 焦点元素更可靠**的依据，不是兜底猜测。实测 Chromium 会把上一轮
    /// 的输入框继续报成焦点元素，据此定位就把键盘放到上一个对话框上；而"这一下点的
    /// 是哪个框"是确定的事实。所以先问它，再问焦点。
    ///
    /// 优先读这个框自己的选区(属于它、不是页面级选区)，拿不到时退到它的外框。
    /// TSF 在没有组合串时给不出坐标——Chromium 的 GetTextExt 只在插入点后面没有
    /// 文本时才返回光标，否则给 TS_E_NOLAYOUT，而这不是布局时序问题、它永远不会
    /// 回调 OnLayoutChange，按规范去等就是无限等待。所以第一次点击必然没有权威
    /// 光标，若因此不显示，用户就得再点一次。
    /// </summary>
    private static NativeRect TryReadClickedFieldCaret(out string source)
    {
        source = "clicked";
        var element = InputInvocationProbe.RecentClickedField(ClickedFieldLifetime);
        if (element is null)
        {
            return default;
        }

        RememberField(element);
        var caret = TryReadTextPatternCaret(element);
        if (!caret.IsEmpty)
        {
            source = "uia/text";
            return caret;
        }

        var box = CaretFromElementBox(element);
        if (!box.IsEmpty)
        {
            return box;
        }

        if (InputInvocationProbe.IsConsoleOrTerminal(element)
            || ConsoleInputSurface.IsWindow(ElementHwnd(element)))
        {
            var console = TryReadConsoleInsertion(ElementHwnd(element), element);
            if (!console.IsEmpty)
            {
                source = "console";
                return console;
            }
        }

        return default;
    }

    private static IntPtr ElementHwnd(AutomationElement element)
    {
        try
        {
            return new IntPtr(element.Current.NativeWindowHandle);
        }
        catch
        {
            return IntPtr.Zero;
        }
    }

    /// <summary>
    /// 控制台/终端没有 Edit 子节点。优先 Win32 插入符，否则用客户区底行——
    /// 那是提示符所在的真实客户区，不是按窗口外框编造。
    /// </summary>
    private static NativeRect TryReadConsoleInsertion(IntPtr hwnd, AutomationElement? element)
    {
        if (hwnd == IntPtr.Zero && element is not null)
        {
            hwnd = ElementHwnd(element);
        }

        if (hwnd == IntPtr.Zero)
        {
            hwnd = NativeMethods.GetAncestor(
                NativeMethods.GetForegroundWindow(),
                NativeMethods.GaRoot);
        }

        var gui = TryReadCaret();
        if (!gui.IsEmpty)
        {
            if (element is not null)
            {
                RememberField(element);
            }
            else
            {
                RememberConsoleField(hwnd);
            }

            LastAutomationSource = "caret";
            LastReject = string.Empty;
            return gui;
        }

        if (element is not null)
        {
            try
            {
                var bounds = element.Current.BoundingRectangle;
                if (!bounds.IsEmpty && bounds.Height >= 12)
                {
                    RememberField(element);
                    LastCaretIsInsertion = true;
                    LastHasRangeSelection = false;
                    LastAutomationSource = "box";
                    LastReject = string.Empty;
                    return new NativeRect
                    {
                        Left = (int)bounds.Left + 8,
                        Top = (int)bounds.Bottom - 22,
                        Right = (int)bounds.Left + 10,
                        Bottom = (int)bounds.Bottom - 4
                    };
                }
            }
            catch
            {
                // 元素已随宿主销毁。
            }
        }

        if (!ConsoleInputSurface.IsWindow(hwnd)
            || !NativeMethods.GetClientRect(hwnd, out var client)
            || client.IsEmpty)
        {
            return default;
        }

        var origin = new NativePoint { X = 0, Y = 0 };
        var corner = new NativePoint { X = client.Right, Y = client.Bottom };
        var caretTop = new NativePoint { X = 8, Y = Math.Max(0, client.Bottom - 22) };
        var caretBottom = new NativePoint { X = 10, Y = Math.Max(caretTop.Y + 18, client.Bottom - 2) };
        if (!NativeMethods.ClientToScreen(hwnd, ref origin)
            || !NativeMethods.ClientToScreen(hwnd, ref corner)
            || !NativeMethods.ClientToScreen(hwnd, ref caretTop)
            || !NativeMethods.ClientToScreen(hwnd, ref caretBottom))
        {
            return default;
        }

        LastFieldId = $"console:{hwnd.ToInt64():X}";
        LastFieldBox = new NativeRect
        {
            Left = origin.X,
            Top = origin.Y,
            Right = corner.X,
            Bottom = corner.Y
        };
        LastCaretIsInsertion = true;
        LastHasRangeSelection = false;
        LastAutomationSource = "box";
        LastReject = string.Empty;
        return new NativeRect
        {
            Left = caretTop.X,
            Top = caretTop.Y,
            Right = caretBottom.X,
            Bottom = caretBottom.Y
        };
    }

    private static void RememberConsoleField(IntPtr hwnd)
    {
        if (!NativeMethods.GetClientRect(hwnd, out var client) || client.IsEmpty)
        {
            return;
        }

        var origin = new NativePoint { X = 0, Y = 0 };
        var corner = new NativePoint { X = client.Right, Y = client.Bottom };
        if (!NativeMethods.ClientToScreen(hwnd, ref origin)
            || !NativeMethods.ClientToScreen(hwnd, ref corner))
        {
            return;
        }

        LastFieldId = $"console:{hwnd.ToInt64():X}";
        LastFieldBox = new NativeRect
        {
            Left = origin.X,
            Top = origin.Y,
            Right = corner.X,
            Bottom = corner.Y
        };
    }

    private static string _lastLogged = string.Empty;

    /// <summary>
    /// 定位每秒要跑好几次，结果通常一模一样。逐条记下来会刷掉几千行，
    /// 真正有用的状态变化反而被埋掉，写日志本身也是开销。
    /// </summary>
    private static void LogOnce(string line)
    {
        if (line == _lastLogged)
        {
            return;
        }

        _lastLogged = line;
        Log.Info(line);
    }

    /// <summary>
    /// 点中的输入框能用多久。焦点一直在这个框上时它始终有效，
    /// 所以时限只用来兜住"用户早就走开了"的情况。
    /// </summary>
    private static readonly TimeSpan ClickedFieldLifetime = TimeSpan.FromMinutes(2);

    private const int CaretFallbackHeight = 40;

    /// <summary>诊断用：上一次 UIA 取值走的是 TextPattern 光标还是外框兜底。</summary>
    private static string LastAutomationSource = "box";

    /// <summary>
    /// 上一次坐标出自哪个输入框元素。空串表示这次坐标不是从某个具体元素读到的
    /// (Win32 光标、按窗口矩形编造)，无法参与身份比较。
    /// </summary>
    private static string LastFieldId = string.Empty;
    private static NativeRect LastFieldBox;
    private static bool LastCaretIsInsertion = true;
    private static bool LastHasRangeSelection;

    private static void RememberField(AutomationElement element)
    {
        LastFieldId = FieldId(element);
        try
        {
            var box = element.Current.BoundingRectangle;
            if (box.IsEmpty)
            {
                LastFieldBox = default;
                return;
            }

            LastFieldBox = new NativeRect
            {
                Left = (int)box.Left,
                Top = (int)box.Top,
                Right = (int)box.Right,
                Bottom = (int)box.Bottom
            };
        }
        catch
        {
            LastFieldBox = default;
        }
    }

    /// <summary>
    /// 输入框身份。RuntimeId 是 UIA 给元素的规范身份。
    ///
    /// 必须有这个东西才能区分开始菜单搜索框和任务栏搜索框：它们共用同一个
    /// SearchHost 顶层窗口，只凭窗口句柄这两个框是同一个，于是互切时会被当成
    /// "同一个框的坐标变差了"而继续沿用旧坐标，键盘停在上一个点击位置。
    /// </summary>
    private static string FieldId(AutomationElement element)
    {
        try
        {
            var id = element.GetRuntimeId();
            return id is null || id.Length == 0 ? string.Empty : string.Join(".", id);
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// 诊断用：上一次 UIA 取值被否掉的原因。取不到光标时键盘就不弹，
    /// 而"取不到"有好几条互不相干的路径，不记下来只能靠猜。
    /// </summary>
    private static string LastReject = string.Empty;

    /// <summary>
    /// 用 TextPattern 取折叠后的插入点矩形，不用整段选区外框。
    ///
    /// 官方专门取光标的 ITextProvider2::GetCaretRange 在 Chromium 上不可用
    /// (它只实现了 ITextProvider 和 ITextEditProvider)，而 GetBoundingRectangles
    /// 对退化(空)范围按规范返回空数组，所以纯光标的选区直接问是拿不到矩形的。
    /// 把退化范围向后扩一个字符使其非退化，取回的第一个矩形就是光标那一行；
    /// 光标在文本末尾时无法向后扩，改为向前扩并取末尾矩形的右边缘。
    /// 非空选区只折叠到终点再取一小段，避免长按手柄把“光标行”拉到选区另一头。
    /// </summary>
    private static NativeRect TryReadTextPatternCaret(AutomationElement element)
    {
        try
        {
            if (element.GetCurrentPattern(TextPattern.Pattern) is not TextPattern text)
            {
                return default;
            }

            var selection = text.GetSelection();
            if (selection is null || selection.Length == 0)
            {
                return default;
            }

            var range = selection[0];
            var insertion = range.CompareEndpoints(
                TextPatternRangeEndpoint.Start,
                range,
                TextPatternRangeEndpoint.End) == 0;
            LastCaretIsInsertion = insertion;
            LastHasRangeSelection = !insertion;
            if (!insertion)
            {
                range = range.Clone();
                range.MoveEndpointByRange(
                    TextPatternRangeEndpoint.Start,
                    range,
                    TextPatternRangeEndpoint.End);
            }

            return ReadRangeCaret(range);
        }
        catch
        {
            return default;
        }
    }

    private static NativeRect ReadRangeCaret(TextPatternRange range)
    {
        var forward = true;
        var rects = range.GetBoundingRectangles();
        if (rects.Length == 0)
        {
            var ahead = range.Clone();
            if (ahead.MoveEndpointByUnit(
                    TextPatternRangeEndpoint.End,
                    TextUnit.Character,
                    1) != 0)
            {
                rects = ahead.GetBoundingRectangles();
            }
        }
        if (rects.Length == 0)
        {
            var behind = range.Clone();
            if (behind.MoveEndpointByUnit(
                    TextPatternRangeEndpoint.Start,
                    TextUnit.Character,
                    -1) != 0)
            {
                forward = false;
                rects = behind.GetBoundingRectangles();
            }
        }
        if (rects.Length == 0)
        {
            return default;
        }

        var line = rects[0];
        if (line.IsEmpty || line.Height is < 8 or > 120)
        {
            return default;
        }

        var x = (int)(forward ? line.Left : line.Right);
        return new NativeRect
        {
            Left = x,
            Top = (int)line.Top,
            Right = x + 2,
            Bottom = (int)(line.Top + Math.Max(18, line.Height))
        };
    }

    private static IntPtr Root(IntPtr hwnd) =>
        hwnd == IntPtr.Zero ? IntPtr.Zero : NativeMethods.GetAncestor(hwnd, NativeMethods.GaRoot);
}
