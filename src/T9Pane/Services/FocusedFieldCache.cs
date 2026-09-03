using System.Windows.Automation;

namespace T9Pane.Services;

/// <summary>
/// UIA 焦点变更事件送来的焦点元素。
///
/// AutomationElement.FocusedElement 是滞后的：跨进程交接焦点时它会在一段时间内
/// 继续返回上一个元素。开始菜单搜索框和任务栏搜索框复用同一个 SearchHost 表面，
/// 交接时窗口不变、只有输入框位置变，于是点击瞬间读到的是上一个框的坐标，
/// 键盘就停在原地或判定“点击不在框内”而不弹出。
///
/// 焦点事件回调拿到的元素才是权威的，这里把它存下来，供随后的定位优先使用。
/// 只在很短的时效内覆盖 FocusedElement——那正好是它滞后的那段窗口；
/// 超时后仍然回到 FocusedElement，避免焦点在没有事件的情况下丢失时用错元素。
/// </summary>
internal static class FocusedFieldCache
{
    private static readonly object Gate = new();
    private static AutomationElement? _element;
    private static long _stampTicks;

    public static void Note(AutomationElement? element)
    {
        lock (Gate)
        {
            _element = element;
            _stampTicks = DateTime.UtcNow.Ticks;
        }
    }

    public static void Invalidate()
    {
        lock (Gate)
        {
            _element = null;
            _stampTicks = 0;
        }
    }

    public static AutomationElement? Fresh(TimeSpan maxAge)
    {
        lock (Gate)
        {
            if (_element is null)
            {
                return null;
            }

            return IsFresh(_stampTicks, DateTime.UtcNow.Ticks, maxAge)
                ? _element
                : null;
        }
    }

    internal static bool IsFresh(long stampTicks, long nowTicks, TimeSpan maxAge) =>
        stampTicks != 0
        && nowTicks >= stampTicks
        && nowTicks - stampTicks <= maxAge.Ticks;
}
