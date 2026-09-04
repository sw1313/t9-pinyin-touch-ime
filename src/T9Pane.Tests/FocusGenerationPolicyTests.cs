using T9Pane.Services;

namespace T9Pane.Tests;

/// <summary>
/// 代号会进 InputContextKey，而定位靠上下文相不相同来决定「同一行就别动」和
/// 「要不要收起重来」。打字时焦点事件照样会来，所以代号必须只跟着前台窗口走。
/// </summary>
public class FocusGenerationPolicyTests
{
    private static readonly IntPtr Notepad = new(0x1234);
    private static readonly IntPtr Browser = new(0x5678);

    [Fact]
    public void Focus_events_inside_the_same_window_keep_the_generation()
    {
        Assert.False(FocusGenerationPolicy.ShouldAdvance(Notepad, Notepad));
    }

    [Fact]
    public void Switching_window_advances_the_generation()
    {
        Assert.True(FocusGenerationPolicy.ShouldAdvance(Notepad, Browser));
    }

    [Fact]
    public void First_foreground_advances_from_nothing()
    {
        Assert.True(FocusGenerationPolicy.ShouldAdvance(IntPtr.Zero, Notepad));
    }

    [Fact]
    public void Losing_the_foreground_holds_the_generation()
    {
        // 前台短暂拿不到就换代的话，回来时会再换一次，键盘白白重启两趟。
        Assert.False(FocusGenerationPolicy.ShouldAdvance(Notepad, IntPtr.Zero));
    }

    [Fact]
    public void Context_stays_the_same_while_typing_in_one_window()
    {
        // 这是上面那条规则真正要保住的东西：同一窗口里连打，上下文不能变。
        var before = InputContextKey.ForFocus(Notepad, 7);
        var after = InputContextKey.ForFocus(Notepad, 7);

        Assert.True(KeyboardPositionSession.IsSameSurfaceContext(before, after));
        Assert.False(KeyboardPositionSession.ShouldRestart(
            visible: true,
            currentHost: Notepad,
            nextHost: Notepad,
            currentContext: before,
            nextContext: after));
    }

    [Fact]
    public void A_bumped_generation_would_restart_the_keyboard()
    {
        // 反过来记一笔：代号一变就会走重启，这正是打字时键盘被刷新的路径。
        var before = InputContextKey.ForFocus(Notepad, 7);
        var after = InputContextKey.ForFocus(Notepad, 8);

        Assert.False(KeyboardPositionSession.IsSameSurfaceContext(before, after));
        Assert.True(KeyboardPositionSession.ShouldRestart(
            visible: true,
            currentHost: Notepad,
            nextHost: Notepad,
            currentContext: before,
            nextContext: after));
    }
}
