using T9Pane.Native;

namespace T9Pane.Services;

/// <summary>
/// 符号盘点完回上层时，按进符号盘之前的左上角摆，只改尺寸。
/// 中途改点了别的来源页（26键→符号→拼音）不能沿用宽盘坐标，否则窄盘会偏走。
/// </summary>
internal static class BoardPlaceResume
{
    public static bool ShouldKeepPlace(KeyboardSurface origin, KeyboardSurface destination) =>
        origin == destination;

    public static NativeRect At(NativeRect before, int width, int height)
    {
        if (before.IsEmpty || width <= 0 || height <= 0)
        {
            return before;
        }

        return new NativeRect
        {
            Left = before.Left,
            Top = before.Top,
            Right = before.Left + width,
            Bottom = before.Top + height
        };
    }

    /// <summary>
    /// 键盘贴在输入行上方。切盘改高度时钉住底边，只往上收或往上长。
    /// 钉顶边会把更高的数字盘底边压进输入行，或把假光标摆到屏幕顶。
    /// </summary>
    public static NativeRect ResizePinnedBottom(NativeRect previous, int width, int height)
    {
        if (previous.IsEmpty || width <= 0 || height <= 0)
        {
            return previous;
        }

        return new NativeRect
        {
            Left = previous.Left,
            Top = previous.Bottom - height,
            Right = previous.Left + width,
            Bottom = previous.Bottom
        };
    }
}
