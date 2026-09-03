using System.Collections.Concurrent;
using System.Diagnostics;
using System.Windows.Automation;
using System.Windows.Automation.Text;
using T9Pane.Native;

namespace T9Pane.Services;

internal readonly record struct SystemTextEditPlan(string Value, int CaretOffset)
{
    public static SystemTextEditPlan AtSelection(string before, string inserted, string after) =>
        new(before + inserted + after, before.Length + inserted.Length);

    public static SystemTextEditPlan Backspace(string before, string selected, string after) =>
        selected.Length > 0
            ? AtSelection(before, "", after)
            : before.Length > 0
                ? AtSelection(before[..^1], "", after)
                : AtSelection("", "", after);
}

/// <summary>
/// 开始菜单 / 搜索等 XAML 系统框不走 Win32 消息泵：PostMessage 会被丢掉，
/// SendInput 也常被 AppContainer + UIPI 挡掉。这里用 UIA ValuePattern 写入，
/// 失败再剪贴板 + Ctrl+V。不需要在搜索框里显示拼音组词。
///
/// UIA 每个属性读写都是跨进程 COM 往返，放在 WPF 的 STA UI 线程上会让退格
/// 明显慢半拍。因此所有 UIA 操作都投递到一条专用 MTA 线程串行执行，并在这条
/// 线程上维护一份文本 / 光标的本地模型：连续输入时只发一次 SetValue，
/// 只有空闲一段时间后才回读一次真实状态。
/// </summary>
internal static class SystemBoxInput
{
    /// <summary>模型可复用的空闲上限，超过就回读一次真实文本，防止外部改动后错位。</summary>
    private const double ModelIdleMs = 500;

    private enum Op
    {
        Capture,
        Clear,
        Insert,
        Backspace
    }

    private readonly record struct Request(Op Kind, string Text, IntPtr FallbackWindow);

    private static readonly BlockingCollection<Request> Queue =
        new(new ConcurrentQueue<Request>(), 512);

    private static int _hasCaptured;

    // 以下状态只在 UIA 工作线程上访问，无需加锁。
    private static AutomationElement? _box;
    private static ValuePattern? _value;
    private static TextPattern? _text;
    private static string _content = "";
    private static int _caret;
    private static int _selectionLength;
    private static bool _modelValid;
    private static long _lastEditTicks;

    static SystemBoxInput()
    {
        var worker = new Thread(Pump)
        {
            IsBackground = true,
            Name = "T9Pane.Uia"
        };
        worker.SetApartmentState(ApartmentState.MTA);
        worker.Start();
    }

    public static bool HasCapturedBox => Volatile.Read(ref _hasCaptured) != 0;

    public static void ClearCapture()
    {
        // 立刻落下标志：宁可少走 UIA 分支，也不要用过期的“已捕获”去误判。
        Volatile.Write(ref _hasCaptured, 0);
        Post(new Request(Op.Clear, "", default));
    }

    public static bool CaptureFocused()
    {
        Post(new Request(Op.Capture, "", default));
        return HasCapturedBox;
    }

    public static bool TryInsert(string text, IntPtr fallbackWindow = default)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        return Post(new Request(Op.Insert, text, fallbackWindow));
    }

    public static bool TryBackspace(IntPtr fallbackWindow = default) =>
        Post(new Request(Op.Backspace, "", fallbackWindow));

    private static bool Post(Request request)
    {
        try
        {
            return Queue.TryAdd(request);
        }
        catch
        {
            return false;
        }
    }

    private static void Pump()
    {
        foreach (var request in Queue.GetConsumingEnumerable())
        {
            try
            {
                switch (request.Kind)
                {
                    case Op.Clear:
                        Invalidate();
                        break;
                    case Op.Capture:
                        Capture();
                        break;
                    case Op.Insert:
                        Insert(request.Text, request.FallbackWindow);
                        break;
                    case Op.Backspace:
                        Backspace(request.FallbackWindow);
                        break;
                }
            }
            catch (Exception ex)
            {
                Log.Warn($"系统框 UIA 队列: {ex.Message}");
                Invalidate();
            }
        }
    }

    private static void Capture()
    {
        if (!Rebind(takeFocus: false))
        {
            Invalidate();
        }
    }

    private static void Insert(string text, IntPtr fallbackWindow)
    {
        using var scope = Perf.Begin("uia.insert");
        if (EnsureModel(takeFocus: true) && _value is not null)
        {
            var plan = SystemTextEditPlan.AtSelection(
                Before(),
                text,
                After());
            if (Apply(plan))
            {
                return;
            }
        }

        if (fallbackWindow != IntPtr.Zero)
        {
            Log.Warn("系统框 UIA 写入失败，改用剪贴板");
        }

        Paste(text);
    }

    private static void Backspace(IntPtr fallbackWindow)
    {
        using var scope = Perf.Begin("uia.backspace");
        if (EnsureModel(takeFocus: false) && _value is not null)
        {
            var plan = SystemTextEditPlan.Backspace(
                Before(),
                Selected(),
                After());
            if (Apply(plan))
            {
                return;
            }
        }

        TextOutput.SendVirtualKey(NativeMethods.VkBack, fallbackWindow);
    }

    private static string Before() => _content[.._caret];

    private static string Selected() =>
        _content.Substring(_caret, _selectionLength);

    private static string After() => _content[(_caret + _selectionLength)..];

    private static bool Apply(SystemTextEditPlan plan)
    {
        try
        {
            _value!.SetValue(plan.Value);
        }
        catch (Exception ex)
        {
            Log.Warn($"系统框 UIA 写入: {ex.Message}");
            Invalidate();
            return false;
        }

        _content = plan.Value;
        _caret = plan.CaretOffset;
        _selectionLength = 0;
        _lastEditTicks = Stopwatch.GetTimestamp();

        if (NeedsCaretMove(plan.CaretOffset, plan.Value.Length))
        {
            MoveCaret(plan.CaretOffset);
        }

        return true;
    }

    /// <summary>
    /// 追加和退格都把光标留在末尾，提供方 SetValue 之后本就会落到末尾。
    /// 只有光标停在文本中间时才值得再花几次跨进程调用去搬它。
    /// </summary>
    internal static bool NeedsCaretMove(int caretOffset, int valueLength) =>
        caretOffset < valueLength;

    private static bool EnsureModel(bool takeFocus)
    {
        if (_modelValid && _box is not null && _value is not null)
        {
            var idleMs =
                (Stopwatch.GetTimestamp() - _lastEditTicks)
                * 1000.0
                / Stopwatch.Frequency;
            if (idleMs <= ModelIdleMs)
            {
                return true;
            }
        }

        return Rebind(takeFocus);
    }

    private static bool Rebind(bool takeFocus)
    {
        using var scope = Perf.Begin("uia.rebind");
        Invalidate();
        var box = FindBox();
        if (box is null)
        {
            return false;
        }

        if (!TryBindValue(box, out var value) || value is null)
        {
            return false;
        }

        if (takeFocus)
        {
            try
            {
                box.SetFocus();
            }
            catch
            {
                // 部分搜索框不允许外部抢焦点，仍尝试 SetValue
            }
        }

        _box = box;
        _value = value;
        _text = TryBindText(box);
        _content = ReadValue(value);
        ReadSelection(_text, _content, out _caret, out _selectionLength);
        _modelValid = true;
        _lastEditTicks = Stopwatch.GetTimestamp();
        Volatile.Write(ref _hasCaptured, 1);
        return true;
    }

    private static void Invalidate()
    {
        _box = null;
        _value = null;
        _text = null;
        _content = "";
        _caret = 0;
        _selectionLength = 0;
        _modelValid = false;
        Volatile.Write(ref _hasCaptured, 0);
    }

    private static bool TryBindValue(
        AutomationElement box,
        out ValuePattern? value)
    {
        value = null;
        try
        {
            if (box.GetCachedPattern(ValuePattern.Pattern) is ValuePattern cached)
            {
                value = cached;
                return true;
            }
        }
        catch
        {
            // 元素不是从带缓存的请求里取出来的，退回实时查询
        }

        try
        {
            if (box.TryGetCurrentPattern(ValuePattern.Pattern, out var raw)
                && raw is ValuePattern live
                && !live.Current.IsReadOnly)
            {
                value = live;
                return true;
            }
        }
        catch
        {
            // 提供方拒绝 ValuePattern
        }

        return false;
    }

    private static TextPattern? TryBindText(AutomationElement box)
    {
        try
        {
            return box.TryGetCurrentPattern(TextPattern.Pattern, out var raw)
                && raw is TextPattern pattern
                ? pattern
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static string ReadValue(ValuePattern value)
    {
        try
        {
            return value.Cached.Value ?? "";
        }
        catch
        {
            // 没有缓存就实时读
        }

        try
        {
            return value.Current.Value ?? "";
        }
        catch
        {
            return "";
        }
    }

    private static void ReadSelection(
        TextPattern? pattern,
        string content,
        out int caret,
        out int selectionLength)
    {
        caret = content.Length;
        selectionLength = 0;
        if (pattern is null)
        {
            return;
        }

        try
        {
            var selected = pattern.GetSelection().FirstOrDefault();
            if (selected is null)
            {
                return;
            }

            var leading = pattern.DocumentRange.Clone();
            leading.MoveEndpointByRange(
                TextPatternRangeEndpoint.End,
                selected,
                TextPatternRangeEndpoint.Start);
            var before = (leading.GetText(-1) ?? "").Length;
            var selectedText = (selected.GetText(-1) ?? "").Length;
            if (before > content.Length)
            {
                return;
            }

            caret = before;
            selectionLength = Math.Min(selectedText, content.Length - before);
        }
        catch
        {
            caret = content.Length;
            selectionLength = 0;
        }
    }

    private static void MoveCaret(int offset)
    {
        try
        {
            if (_text is not null)
            {
                var caret = _text.DocumentRange.Clone();
                caret.MoveEndpointByRange(
                    TextPatternRangeEndpoint.End,
                    caret,
                    TextPatternRangeEndpoint.Start);
                caret.Move(TextUnit.Character, Math.Max(0, offset));
                caret.Select();
                return;
            }
        }
        catch
        {
            // 不支持 TextPattern 的系统框继续使用键盘回退
        }

        TextOutput.SendChord(NativeMethods.VkControl, NativeMethods.VkEnd);
    }

    /// <summary>
    /// 一次 CacheRequest 把控件类型、可用性、ValuePattern 及其只读标志和当前值
    /// 全部带回来，把原来 5 次跨进程往返压成 1 次。
    /// </summary>
    private static CacheRequest BoxCacheRequest()
    {
        var request = new CacheRequest
        {
            AutomationElementMode = AutomationElementMode.Full,
            TreeFilter = Automation.RawViewCondition
        };
        request.Add(AutomationElement.ControlTypeProperty);
        request.Add(AutomationElement.IsEnabledProperty);
        request.Add(ValuePattern.Pattern);
        request.Add(ValuePattern.IsReadOnlyProperty);
        request.Add(ValuePattern.ValueProperty);
        return request;
    }

    private static AutomationElement? FindBox()
    {
        var request = BoxCacheRequest();

        try
        {
            using (request.Activate())
            {
                var focused = AutomationElement.FocusedElement;
                if (LooksLikeBox(focused))
                {
                    return focused;
                }
            }
        }
        catch
        {
            // FocusedElement 在部分系统浮层上会抛 COM 异常
        }

        var fg = NativeMethods.GetForegroundWindow();
        if (fg == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            using (request.Activate())
            {
                var root = AutomationElement.FromHandle(fg);
                if (root is null)
                {
                    return null;
                }

                foreach (var type in new[] { ControlType.Edit, ControlType.ComboBox })
                {
                    try
                    {
                        var hit = root.FindFirst(
                            TreeScope.Descendants,
                            new AndCondition(
                                new PropertyCondition(
                                    AutomationElement.ControlTypeProperty,
                                    type),
                                new PropertyCondition(
                                    AutomationElement.HasKeyboardFocusProperty,
                                    true)));
                        if (LooksLikeBox(hit))
                        {
                            return hit;
                        }
                    }
                    catch
                    {
                        // 继续试下一种
                    }
                }
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    private static bool LooksLikeBox(AutomationElement? element)
    {
        if (element is null)
        {
            return false;
        }

        try
        {
            if (element.Cached.ControlType is not { } cachedType
                || (cachedType != ControlType.Edit
                    && cachedType != ControlType.ComboBox)
                || !element.Cached.IsEnabled)
            {
                return false;
            }

            return element.GetCachedPattern(ValuePattern.Pattern)
                    is ValuePattern value
                && !value.Cached.IsReadOnly;
        }
        catch
        {
            // 缓存缺项时退回实时读取
        }

        try
        {
            var type = element.Current.ControlType;
            return (type == ControlType.Edit || type == ControlType.ComboBox)
                && element.Current.IsEnabled
                && element.TryGetCurrentPattern(ValuePattern.Pattern, out var raw)
                && raw is ValuePattern value
                && !value.Current.IsReadOnly;
        }
        catch
        {
            return false;
        }
    }

    private static void Paste(string text)
    {
        var app = System.Windows.Application.Current;
        if (app is null)
        {
            Log.Warn("系统框剪贴板兜底缺少 UI 线程，已跳过");
            return;
        }

        // 剪贴板必须在 STA 线程上访问，回到 UI 线程执行。
        app.Dispatcher.Invoke(() => DoPaste(text));
    }

    private static void DoPaste(string text)
    {
        var previous = "";
        var had = false;
        try
        {
            if (System.Windows.Clipboard.ContainsText())
            {
                previous = System.Windows.Clipboard.GetText();
                had = true;
            }
        }
        catch
        {
            had = false;
        }

        System.Windows.Clipboard.SetText(text);
        TextOutput.SendChord(NativeMethods.VkControl, NativeMethods.VkV);

        var restore = previous;
        var keep = had;
        var timer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            try
            {
                if (keep)
                {
                    System.Windows.Clipboard.SetText(restore);
                }
                else
                {
                    System.Windows.Clipboard.Clear();
                }
            }
            catch
            {
                // 剪贴板被其它程序占用时忽略
            }
        };
        timer.Start();
    }
}
