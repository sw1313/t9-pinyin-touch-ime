using System.Windows;
using Button = System.Windows.Controls.Button;

namespace T9Pane.Overlay;

/// <summary>
/// 触摸按键反馈：整颗键等比缩小，抬起回弹。不加遮罩、不下沉。
/// </summary>
internal static class TouchKeyVisual
{
    public const double PressScale = 0.94;

    public static void Press(Button? button, bool animate = true)
    {
        if (button is null)
        {
            return;
        }

        VisualStateManager.GoToState(button, "Pressed", useTransitions: animate);
    }

    public static void Release(Button? button, bool animate = true)
    {
        if (button is null)
        {
            return;
        }

        VisualStateManager.GoToState(button, "Normal", useTransitions: animate);
    }
}
