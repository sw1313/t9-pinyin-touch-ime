using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Interop;
using System.Windows.Media;
using T9Pane.Native;
using Application = System.Windows.Application;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;

namespace T9Pane.Services;

internal sealed class TrayHost : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly AppSettings _settings;
    private readonly Action _onChanged;
    private readonly Action _onPreview;
    private readonly Action _onReload;
    private readonly Action _onInstall;
    private readonly Action _onExit;
    private readonly Action _onOpacity;
    private readonly Action _onSkin;
    private Window? _menu;

    public TrayHost(
        AppSettings settings,
        Action onChanged,
        Action onPreview,
        Action onReload,
        Action onInstall,
        Action onExit,
        Action onOpacity,
        Action onSkin)
    {
        _settings = settings;
        _onChanged = onChanged;
        _onPreview = onPreview;
        _onReload = onReload;
        _onInstall = onInstall;
        _onExit = onExit;
        _onOpacity = onOpacity;
        _onSkin = onSkin;
        _icon = new NotifyIcon
        {
            Text = "T9 九键输入法",
            Icon = CreateIcon(),
            Visible = true
        };
        _icon.MouseUp += (_, e) =>
        {
            if (e.Button != MouseButtons.Right)
            {
                return;
            }

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher is null)
            {
                return;
            }

            dispatcher.BeginInvoke(ShowMenu);
        };
        _icon.DoubleClick += (_, _) =>
        {
            _settings.Enabled = !_settings.Enabled;
            _settings.Save();
            _onChanged();
        };
    }

    private void ShowMenu()
    {
        CloseMenu();
        var items = new StackPanel();
        AddCheck(items, "启用九键面板", _settings.Enabled, () =>
        {
            _settings.Enabled = !_settings.Enabled;
            SaveAndNotify();
        });
        AddCheck(items, "预览模式（不依赖触摸键盘）", _settings.PreviewMode, () =>
        {
            _settings.PreviewMode = !_settings.PreviewMode;
            SaveAndNotify();
            _onPreview();
        });
        AddCheck(items, "开机启动", _settings.AutoStart, () =>
        {
            _settings.AutoStart = !_settings.AutoStart;
            SaveAndNotify();
        });
        items.Children.Add(Rule());
        AddItem(items, "调整键盘透明度", _onOpacity);
        AddItem(items, "添加键盘图片", _onSkin);
        items.Children.Add(Rule());
        AddItem(items, "重新加载词库", _onReload);
        AddItem(items, "打开用户词库", OpenUserLexicon);
        AddItem(items, "打开日志", () => OpenFile(AppSettings.LogPath));
        AddItem(items, "安装到输入法选择器（Win+空格可切换）", _onInstall);
        items.Children.Add(Rule());
        AddItem(items, "退出", _onExit);

        var menu = new Window
        {
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            ShowActivated = true,
            Topmost = true,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -2000,
            Top = -2000,
            SizeToContent = SizeToContent.WidthAndHeight,
            Background = System.Windows.Media.Brushes.White,
            Content = new Border
            {
                BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(214, 220, 229)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(0, 4, 0, 4),
                Child = items
            }
        };
        menu.Deactivated += OnMenuDeactivated;
        menu.Closed += (_, _) =>
        {
            if (ReferenceEquals(_menu, menu))
            {
                _menu = null;
            }
        };
        _menu = menu;
        menu.Show();
        PlaceMenu(menu);
        menu.Activate();
    }

    private void OnMenuDeactivated(object? sender, EventArgs e)
    {
        if (_menu is not { } menu)
        {
            return;
        }

        if (!TrayMenuPolicy.ShouldDismissOnDeactivate(menu.IsMouseOver))
        {
            return;
        }

        CloseMenu();
    }

    private void CloseMenu()
    {
        if (_menu is null)
        {
            return;
        }

        var menu = _menu;
        _menu = null;
        menu.Deactivated -= OnMenuDeactivated;
        menu.Close();
    }

    private static void PlaceMenu(Window menu)
    {
        var cursor = System.Windows.Forms.Control.MousePosition;
        menu.UpdateLayout();
        var hwnd = new WindowInteropHelper(menu).EnsureHandle();
        var dpi = NativeMethods.GetDpiForWindow(hwnd);
        if (dpi == 0)
        {
            dpi = 96;
        }

        var work = SystemParameters.WorkArea;
        var (left, top) = TrayMenuPolicy.Place(
            cursor.X * 96.0 / dpi,
            cursor.Y * 96.0 / dpi,
            Math.Max(menu.ActualWidth, 1),
            Math.Max(menu.ActualHeight, 1),
            work.Left,
            work.Top,
            work.Right,
            work.Bottom);
        menu.Left = left;
        menu.Top = top;
    }

    private void AddItem(StackPanel items, string text, Action action) =>
        items.Children.Add(MenuButton(text, action));

    private void AddCheck(StackPanel items, string text, bool isChecked, Action action) =>
        items.Children.Add(MenuButton(isChecked ? "✓  " + text : "    " + text, action));

    private System.Windows.Controls.Button MenuButton(string text, Action action)
    {
        var button = new System.Windows.Controls.Button
        {
            Content = text,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(14, 6, 28, 6),
            Margin = new Thickness(0),
            BorderThickness = new Thickness(0),
            Background = System.Windows.Media.Brushes.Transparent,
            Focusable = false
        };
        button.Click += (_, _) =>
        {
            CloseMenu();
            action();
        };
        button.MouseEnter += OnMenuHover;
        button.MouseLeave += OnMenuLeave;
        return button;
    }

    private static void OnMenuHover(object sender, MouseEventArgs e)
    {
        if (sender is System.Windows.Controls.Button button)
        {
            button.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(221, 233, 255));
        }
    }

    private static void OnMenuLeave(object sender, MouseEventArgs e)
    {
        if (sender is System.Windows.Controls.Button button)
        {
            button.Background = System.Windows.Media.Brushes.Transparent;
        }
    }

    private static Border Rule() => new()
    {
        Height = 1,
        Margin = new Thickness(8, 4, 8, 4),
        Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(232, 236, 241))
    };

    public void Tip(string text)
    {
        _icon.BalloonTipTitle = "T9Pane";
        _icon.BalloonTipText = text;
        _icon.ShowBalloonTip(8000);
    }

    private void SaveAndNotify()
    {
        _settings.Save();
        _onChanged();
    }

    private static void OpenUserLexicon()
    {
        if (!File.Exists(AppSettings.UserLexiconPath))
        {
            File.WriteAllText(AppSettings.UserLexiconPath, "# 补充词，格式与小白T9/Rime 相同：词语<TAB>拼音(空格分音节)<TAB>词频\n你好\tni hao\t9000\n");
        }

        OpenFile(AppSettings.UserLexiconPath);
    }

    private static void OpenFile(string path)
    {
        if (!File.Exists(path))
        {
            File.WriteAllText(path, "");
        }

        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    private static Icon CreateIcon()
    {
        var bitmap = new Bitmap(32, 32);
        using var g = Graphics.FromImage(bitmap);
        g.Clear(System.Drawing.Color.FromArgb(28, 28, 28));
        using var font = new Font("Segoe UI", 11, System.Drawing.FontStyle.Bold);
        using var brush = new SolidBrush(System.Drawing.Color.FromArgb(96, 205, 255));
        g.DrawString("T9", font, brush, 2, 6);
        return Icon.FromHandle(bitmap.GetHicon());
    }

    public void Dispose()
    {
        CloseMenu();
        _icon.Visible = false;
        _icon.Dispose();
    }
}
