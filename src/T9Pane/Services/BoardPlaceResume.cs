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
}
