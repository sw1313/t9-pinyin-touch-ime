using System.Windows;
using System.Windows.Media;
using Button = System.Windows.Controls.Button;

namespace T9Pane.Overlay;

/// <summary>
/// 触摸按键反馈：整颗键等比缩小，抬起回弹。不加遮罩、不下沉。
/// </summary>
internal static class TouchKeyVisual
{
    public const double PressScale = 0.94;

    public static void Press(Button? button, bool animate = true) =>
        Apply(button, animate, PressScale, "Pressed");

    public static void Release(Button? button, bool animate = true) =>
        Apply(button, animate, 1.0, "Normal");

    private static void Apply(Button? button, bool animate, double scale, string state)
    {
        if (button is null)
        {
            return;
        }

        if (animate)
        {
            VisualStateManager.GoToState(button, state, useTransitions: true);
            return;
        }

        // 系统浮层给用户看的是抓下来的位图，而状态机里按下和回弹都是 50~90ms 的
        // Storyboard：抓帧那一刻动画才刚起步，位图上还是没缩放的原样，看起来就是
        // "触摸点击没有按下动画"。useTransitions:false 只跳过状态间的过渡，
        // 跳不过状态自身 Storyboard 的时长。这条路径直接把缩放写到终态，
        // 让当前这一帧就带上按下效果。
        button.ApplyTemplate();
        if (button.Template?.FindName("KeyScale", button) is not ScaleTransform key)
        {
            VisualStateManager.GoToState(button, state, useTransitions: false);
            return;
        }

        // 本地可见窗口那条路走的是 Storyboard，会在属性上留下动画时钟，
        // 不先摘掉的话直接赋值不生效。
        key.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        key.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        key.ScaleX = scale;
        key.ScaleY = scale;
    }
}
