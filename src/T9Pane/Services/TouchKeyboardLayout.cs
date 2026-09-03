using T9Pane.Native;

namespace T9Pane.Services;

internal static class TouchKeyboardLayout
{
    public static bool IsFloating(NativeRect keyboard)
    {
        if (keyboard.IsEmpty || !NativeMethods.TryGetMonitorWork(keyboard, out var work))
        {
            return true;
        }

        var notFullWidth = keyboard.Width < work.Width * 0.72;
        var notDockedBottom = Math.Abs(keyboard.Bottom - work.Bottom) > 64;
        return notFullWidth || notDockedBottom;
    }

    /// <summary>
    /// 只覆盖 Win11 触摸键盘的字母键区，数字行、符号、空格、退格、Ctrl 等保持系统原样。
    /// </summary>
    public static NativeRect GetLetterRect(NativeRect keyboard)
    {
        if (keyboard.IsEmpty)
        {
            return keyboard;
        }

        double left, top, right, bottom;
        if (IsFloating(keyboard))
        {
            // 浮动小键盘 / 传统键盘：顶栏 + 数字行，左右功能键，底栏修饰键。
            left = 0.09;
            right = 0.88;
            top = 0.27;
            bottom = 0.80;
        }
        else
        {
            // 贴底默认触摸键盘：没有完整数字行，底栏是 &123 / 空格 / 回车。
            left = 0.015;
            right = 0.985;
            top = 0.05;
            bottom = 0.76;
        }

        return new NativeRect
        {
            Left = keyboard.Left + (int)Math.Round(keyboard.Width * left),
            Top = keyboard.Top + (int)Math.Round(keyboard.Height * top),
            Right = keyboard.Left + (int)Math.Round(keyboard.Width * right),
            Bottom = keyboard.Top + (int)Math.Round(keyboard.Height * bottom)
        };
    }
}
