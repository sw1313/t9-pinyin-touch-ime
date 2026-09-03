using System.IO;
using System.Windows;
using System.Windows.Controls;
using T9Pane.Services;
using ComboBox = System.Windows.Controls.ComboBox;
using ComboBoxItem = System.Windows.Controls.ComboBoxItem;
using Orientation = System.Windows.Controls.Orientation;

namespace T9Pane.Overlay;

internal static class AppearanceDialog
{
    public static void ShowOpacity(T9OverlayWindow overlay, AppSettings settings)
    {
        var label = new TextBlock { Margin = new Thickness(0, 0, 0, 8) };
        var slider = new Slider
        {
            Minimum = 25,
            Maximum = 100,
            TickFrequency = 5,
            IsSnapToTickEnabled = true,
            Value = KeyboardSkinPolicy.ClampOverlay(settings.OverlayOpacity) * 100
        };
        void Apply()
        {
            settings.OverlayOpacity = KeyboardSkinPolicy.ClampOverlay(slider.Value / 100);
            label.Text = $"键盘透明度 {(int)Math.Round(settings.OverlayOpacity * 100)}%";
            overlay.ApplyAppearance();
        }

        slider.ValueChanged += (_, _) => Apply();
        Apply();
        var body = new StackPanel();
        body.Children.Add(label);
        body.Children.Add(slider);
        ShowWindow("调整键盘透明度", 360, 150, body, settings);
    }

    public static void ShowSkin(T9OverlayWindow overlay, AppSettings settings)
    {
        var keyBox = new ComboBox { Margin = new Thickness(0, 0, 0, 10) };
        foreach (var key in KeyboardSkinPolicy.AllKeys)
        {
            keyBox.Items.Add(new ComboBoxItem
            {
                Content = KeyboardSkinPolicy.Title(key),
                Tag = key
            });
        }

        var current = overlay.CurrentSkinKey;
        keyBox.SelectedIndex = Math.Max(0, KeyboardSkinPolicy.AllKeys.ToList().IndexOf(current));
        var pathText = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8)
        };
        var opacityLabel = new TextBlock { Margin = new Thickness(0, 10, 0, 4) };
        var slider = new Slider
        {
            Minimum = 5,
            Maximum = 100,
            TickFrequency = 5,
            IsSnapToTickEnabled = true
        };

        string SelectedKey() =>
            (keyBox.SelectedItem as ComboBoxItem)?.Tag as string ?? KeyboardSkinPolicy.Compact;

        void Refresh()
        {
            var skin = KeyboardSkinPolicy.For(settings, SelectedKey());
            pathText.Text = string.IsNullOrWhiteSpace(skin.Path) || !File.Exists(skin.Path)
                ? "尚未添加图片（不压比例，按当前盘面尺寸使用）"
                : skin.Path;
            slider.Value = KeyboardSkinPolicy.ClampImage(skin.Opacity) * 100;
            opacityLabel.Text = $"图片透明度 {(int)Math.Round(slider.Value)}%";
        }

        void ApplyImage()
        {
            var skin = KeyboardSkinPolicy.For(settings, SelectedKey());
            skin.Opacity = KeyboardSkinPolicy.ClampImage(slider.Value / 100);
            opacityLabel.Text = $"图片透明度 {(int)Math.Round(skin.Opacity * 100)}%";
            overlay.ApplyAppearance();
        }

        keyBox.SelectionChanged += (_, _) => Refresh();
        slider.ValueChanged += (_, _) => ApplyImage();

        var browse = new System.Windows.Controls.Button
        {
            Content = "选择图片",
            Margin = new Thickness(0, 0, 8, 0),
            Padding = new Thickness(12, 4, 12, 4)
        };
        browse.Click += (_, _) =>
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "图片|*.png;*.jpg;*.jpeg;*.bmp;*.webp|所有文件|*.*",
                Title = "选择键盘背景图"
            };
            if (dialog.ShowDialog() != true)
            {
                return;
            }

            var key = SelectedKey();
            var destDir = Path.Combine(AppSettings.AppDataDirectory, "skins");
            Directory.CreateDirectory(destDir);
            var dest = Path.Combine(destDir, key + Path.GetExtension(dialog.FileName));
            File.Copy(dialog.FileName, dest, true);
            KeyboardSkinPolicy.For(settings, key).Path = dest;
            settings.Save();
            Refresh();
            overlay.ApplyAppearance();
        };

        var clear = new System.Windows.Controls.Button
        {
            Content = "清除图片",
            Padding = new Thickness(12, 4, 12, 4)
        };
        clear.Click += (_, _) =>
        {
            KeyboardSkinPolicy.For(settings, SelectedKey()).Path = null;
            settings.Save();
            Refresh();
            overlay.ApplyAppearance();
        };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 8)
        };
        buttons.Children.Add(browse);
        buttons.Children.Add(clear);

        var root = new StackPanel();
        root.Children.Add(new TextBlock
        {
            Text = "每个尺寸各用一张图，图片保持原比例。",
            Margin = new Thickness(0, 0, 0, 8)
        });
        root.Children.Add(keyBox);
        root.Children.Add(pathText);
        root.Children.Add(buttons);
        root.Children.Add(opacityLabel);
        root.Children.Add(slider);
        Refresh();
        ShowWindow("添加键盘图片", 420, 280, root, settings);
    }

    private static void ShowWindow(
        string title,
        double width,
        double height,
        System.Windows.UIElement body,
        AppSettings settings,
        TextBlock? extra = null)
    {
        var panel = new StackPanel { Margin = new Thickness(16) };
        if (extra is not null)
        {
            panel.Children.Add(extra);
        }

        panel.Children.Add(body);
        var window = new Window
        {
            Title = title,
            Width = width,
            Height = height,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ResizeMode = ResizeMode.NoResize,
            Topmost = true,
            Content = panel
        };
        window.Closed += (_, _) => settings.Save();
        window.Show();
    }
}
