using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Rectangle = System.Windows.Shapes.Rectangle;

namespace T9Pane.Overlay;

internal static class KeyGlyphs
{
    public static FrameworkElement WindowsFlag(Brush fill)
    {
        const double pane = 7;
        const double gap = 2.2;
        var size = pane * 2 + gap;
        var canvas = new Canvas { Width = size, Height = size };
        AddPane(canvas, fill, 0, 0, pane);
        AddPane(canvas, fill, pane + gap, 0, pane);
        AddPane(canvas, fill, 0, pane + gap, pane);
        AddPane(canvas, fill, pane + gap, pane + gap, pane);
        return Box(canvas, 18, 18);
    }

    // 路径按 Material / Gboard 功能键隐喻：退格是左删键，
    // Caps 是上箭头加横杠，回车是折返箭头，Shift 是上箭头。
    public const string BackspacePath =
        "M22,3H7C6.31,3 5.77,3.35 5.41,3.88L0,12 5.41,20.12C5.77,20.65 6.31,21 7,21H22A2,2 0 0,0 24,19V5A2,2 0 0,0 22,3M19,15.59L17.59,17 15,14.41 12.41,17 11,15.59 13.59,13 11,10.41 12.41,9 15,11.59 17.59,9 19,10.41 16.41,13Z";

    public const string EnterPath =
        "M19,7V11H5.83L9.41,7.41 8,6 2,12 8,18 9.41,16.59 5.83,13H21V7Z";

    public const string ShiftPath =
        "M12,6 19,14H15V20H9V14H5Z";

    public const string CapsPath =
        "M12,5 19,13H15V17H9V13H5ZM8,19H16V21H8Z";

    public static FrameworkElement Backspace(Brush fill) => Glyph(BackspacePath, fill, 22);

    public static FrameworkElement Enter(Brush fill) => Glyph(EnterPath, fill, 20);

    public static FrameworkElement Shift(bool on, Brush fill) =>
        on ? Glyph(ShiftPath, fill, 18) : Outline(ShiftPath, fill, 18);

    public static FrameworkElement Caps(bool on, Brush fill) =>
        on ? Glyph(CapsPath, fill, 18) : Outline(CapsPath, fill, 18);

    public static FrameworkElement ChevronUp(Brush fill) =>
        Glyph("M7,14 12,8 17,14Z", fill, 16);

    public static FrameworkElement ChevronDown(Brush fill) =>
        Glyph("M7,10 12,16 17,10Z", fill, 16);

    public static FrameworkElement ChevronLeft(Brush fill) =>
        Glyph("M14,7 8,12 14,17Z", fill, 16);

    public static FrameworkElement ChevronRight(Brush fill) =>
        Glyph("M10,7 16,12 10,17Z", fill, 16);

    public static FrameworkElement Lock(bool locked, Brush fill)
    {
        var canvas = new Canvas { Width = 16, Height = 18 };
        var body = new Rectangle
        {
            Width = 10,
            Height = 8,
            RadiusX = 1.6,
            RadiusY = 1.6,
            Fill = fill
        };
        Canvas.SetLeft(body, 3);
        Canvas.SetTop(body, 9);
        canvas.Children.Add(body);

        var shackle = new Path
        {
            Stroke = fill,
            StrokeThickness = 1.8,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Data = locked
                ? Geometry.Parse("M5,9 V5.2 A3,3 0 0 1 11,5.2 V9")
                : Geometry.Parse("M5,9 V5.2 A3,3 0 0 1 11,5.2 V6.4")
        };
        canvas.Children.Add(shackle);

        var keyhole = new Ellipse
        {
            Width = 2.2,
            Height = 2.2,
            Fill = Brushes.White
        };
        Canvas.SetLeft(keyhole, 6.9);
        Canvas.SetTop(keyhole, 11.4);
        canvas.Children.Add(keyhole);
        return Box(canvas, 16, 18);
    }

    private static void AddPane(Canvas canvas, Brush fill, double x, double y, double size)
    {
        var pane = new Rectangle
        {
            Width = size,
            Height = size,
            RadiusX = 0.7,
            RadiusY = 0.7,
            Fill = fill
        };
        Canvas.SetLeft(pane, x);
        Canvas.SetTop(pane, y);
        canvas.Children.Add(pane);
    }

    private static Viewbox Glyph(string data, Brush fill, double size) =>
        Box(new Path
        {
            Data = Geometry.Parse(data),
            Fill = fill,
            Stretch = Stretch.Uniform
        }, size, size);

    private static Viewbox Outline(string data, Brush fill, double size) =>
        Box(new Path
        {
            Data = Geometry.Parse(data),
            Stroke = fill,
            StrokeThickness = 1.7,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Fill = Brushes.Transparent,
            Stretch = Stretch.Uniform
        }, size, size);

    private static Viewbox Box(UIElement child, double width, double height) =>
        new()
        {
            Width = width,
            Height = height,
            Stretch = Stretch.Uniform,
            IsHitTestVisible = false,
            Child = child
        };
}
