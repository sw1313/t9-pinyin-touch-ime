using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace T9Pane.Services;

internal static class HostFrame
{
    // 840x400 @150% 一帧约 2MB，每次按键新建位图和数组会持续把大对象堆撑起来，
    // 触发 Gen2 回收并表现为输入抖动。尺寸不变时复用同一份。
    private static RenderTargetBitmap? _bitmap;
    private static byte[]? _pixels;
    private static int _width;
    private static int _height;

    public static bool NeedsRepublish(
        bool sameHost,
        bool sameContext,
        bool hostReady) =>
        !sameHost || !sameContext || !hostReady;

    /// <summary>
    /// HostRender 起来后不能把本地窗 SWP_HIDE：WPF 一藏布局会收矮，
    /// RenderTargetBitmap 停在第一张图。挪到屏外并保持显示。
    /// </summary>
    public const int ParkOffset = -32000;

    public static bool ShouldParkLocalWindow(bool hosting, bool hostReady) =>
        hosting && hostReady;

    /// <summary>
    /// 建盘后的第一帧必须等 Loaded，不能排在 Render。
    /// Render 早于新子树的布局，九键第一次弹出就会画出盘面、点不中键。
    /// </summary>
    public static DispatcherPriority PublishPriority => DispatcherPriority.Loaded;

    internal static bool CanReuseBuffer(
        int cachedWidth,
        int cachedHeight,
        bool hasBuffer,
        int width,
        int height) =>
        hasBuffer && cachedWidth == width && cachedHeight == height;

    /// <summary>
    /// 刚 Rebuild 的子树只跑 UpdateLayout 不够：RenderTargetBitmap 会抓到上一帧。
    /// WPF 文档/社区做法是先 Measure/Arrange，再把 Dispatcher 冲到 Loaded。
    /// https://stackoverflow.com/questions/33691195
    /// https://learn.microsoft.com/dotnet/api/system.windows.threading.dispatcherpriority
    /// </summary>
    internal static System.Windows.Size ContentSize(
        double actualWidth,
        double actualHeight,
        double width,
        double height)
    {
        var w = actualWidth > 1 && !double.IsNaN(actualWidth) ? actualWidth : width;
        var h = actualHeight > 1 && !double.IsNaN(actualHeight) ? actualHeight : height;
        if (double.IsNaN(w) || w < 1)
        {
            w = 1;
        }

        if (double.IsNaN(h) || h < 1)
        {
            h = 1;
        }

        return new System.Windows.Size(w, h);
    }

    public static void Prepare(Window window)
    {
        if (window.Content is not UIElement element)
        {
            window.UpdateLayout();
            return;
        }

        var size = ContentSize(window.Width, window.Height, window.Width, window.Height);
        element.InvalidateMeasure();
        element.Measure(size);
        element.Arrange(new System.Windows.Rect(size));
        window.UpdateLayout();
    }

    public static void FlushLayout(Dispatcher dispatcher)
    {
        if (dispatcher.CheckAccess())
        {
            dispatcher.Invoke(() => { }, DispatcherPriority.Loaded);
            return;
        }

        dispatcher.Invoke(() => dispatcher.Invoke(() => { }, DispatcherPriority.Loaded));
    }

    public static byte[]? Capture(Window window, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (window.Content is not Visual visual)
        {
            return null;
        }

        using var scope = Perf.Begin("host.capture");
        Prepare(window);
        var dpi = VisualTreeHelper.GetDpi(window);
        var layout = ContentSize(window.Width, window.Height, window.Width, window.Height);
        width = Math.Max(8, (int)Math.Round(layout.Width * dpi.DpiScaleX));
        height = Math.Max(8, (int)Math.Round(layout.Height * dpi.DpiScaleY));

        if (!CanReuseBuffer(_width, _height, _bitmap is not null && _pixels is not null, width, height))
        {
            _bitmap = new RenderTargetBitmap(
                width,
                height,
                dpi.PixelsPerInchX,
                dpi.PixelsPerInchY,
                PixelFormats.Pbgra32);
            _pixels = new byte[width * height * 4];
            _width = width;
            _height = height;
        }

        // 复用的位图必须先清空，否则上一帧的像素会在透明区域残留。
        _bitmap!.Clear();
        _bitmap.Render(visual);
        _bitmap.CopyPixels(_pixels!, width * 4, 0);
        return _pixels;
    }
}
