using T9Pane.Native;

namespace T9Pane.Services;

/// <summary>
/// 对齐系统触摸键盘 / 屏幕键盘的连发：按下先执行一次，
/// 超过 <see cref="SPI_GETKEYBOARDDELAY"/> 后按 <see cref="SPI_GETKEYBOARDSPEED"/> 重复。
/// 退格、删除、方向键连发；回车和空格不连发（空格长按在官方盘上是语言/符号）。
/// </summary>
internal static class KeyRepeatPolicy
{
    public static bool Repeats(FullKeyAction action) =>
        action is FullKeyAction.Backspace
            or FullKeyAction.Delete
            or FullKeyAction.Left
            or FullKeyAction.Right
            or FullKeyAction.Up
            or FullKeyAction.Down;

    public static TimeSpan DelayFromKeyboardDelay(int delay) =>
        TimeSpan.FromMilliseconds(250 * (1 + Math.Clamp(delay, 0, 3)));

    public static TimeSpan IntervalFromKeyboardSpeed(int speed)
    {
        var clamped = Math.Clamp(speed, 0, 31);
        var ms = 400.0 - clamped * (400.0 - 1000.0 / 30.0) / 31.0;
        return TimeSpan.FromMilliseconds(ms);
    }

    public static TimeSpan InitialDelay
    {
        get
        {
            if (!NativeMethods.SystemParametersInfo(
                    NativeMethods.SpiGetKeyboardDelay,
                    0,
                    out var delay,
                    0))
            {
                delay = 1;
            }

            return DelayFromKeyboardDelay(delay);
        }
    }

    public static TimeSpan RepeatInterval
    {
        get
        {
            if (!NativeMethods.SystemParametersInfo(
                    NativeMethods.SpiGetKeyboardSpeed,
                    0,
                    out var speed,
                    0))
            {
                speed = 31;
            }

            return IntervalFromKeyboardSpeed(speed);
        }
    }
}
