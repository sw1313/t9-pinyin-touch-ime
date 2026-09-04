using T9Pane.Native;
using T9Pane.Services;

namespace T9Pane.Tests;

public class SipSuppressionPolicyTests
{
    private static NativeRect Rect(int left, int top, int right, int bottom) =>
        new() { Left = left, Top = top, Right = right, Bottom = bottom };

    [Fact]
    public void Pointer_hook_stays_on_once_the_language_bar_is_t9()
    {
        Assert.True(SipSuppressionPolicy.ShouldEnablePointerHookForProfile(
            canCommitForeground: false,
            hasForegroundProfileLease: false,
            hasSystemProfileLease: false,
            officialT9Selected: true));
        Assert.False(SipSuppressionPolicy.ShouldEnablePointerHookForProfile(
            canCommitForeground: false,
            hasForegroundProfileLease: false,
            hasSystemProfileLease: false,
            officialT9Selected: false));
        Assert.False(SipSuppressionPolicy.ShouldEnablePointerHookForProfile(
            canCommitForeground: true,
            hasForegroundProfileLease: true,
            hasSystemProfileLease: true,
            officialT9Selected: false));
        Assert.True(SipSuppressionPolicy.ShouldSuppressOfficialSip(officialT9Selected: true));
        Assert.False(SipSuppressionPolicy.ShouldSuppressOfficialSip(officialT9Selected: false));
    }

    [Fact]
    public void Official_sip_process_covers_textinputhost_tabtip_and_osk()
    {
        Assert.True(SipSuppressionPolicy.IsOfficialSipProcess("TextInputHost"));
        Assert.True(SipSuppressionPolicy.IsOfficialSipProcess(@"C:\Windows\System32\TabTip.exe"));
        Assert.True(SipSuppressionPolicy.IsOfficialSipProcess("osk.exe"));
        Assert.True(SipSuppressionPolicy.IsOfficialSipProcess(
            "WindowsInternal.ComposableShell.Experiences.TextInput.InputApp"));
        Assert.False(SipSuppressionPolicy.IsOfficialSipProcess("explorer"));
        Assert.False(SipSuppressionPolicy.IsOfficialSipProcess(""));
    }

    [Fact]
    public void Desktop_docked_keyboard_still_matches()
    {
        var work = Rect(0, 0, 1920, 1080);
        var keyboard = Rect(0, 720, 1920, 1080);

        Assert.True(SipSuppressionPolicy.LooksLikeTouchKeyboard(keyboard, work));
        Assert.True(SipSuppressionPolicy.LooksLikeSuppressibleSip(keyboard, work));
    }

    [Fact]
    public void Tablet_portrait_keyboard_taller_than_half_screen_is_still_a_keyboard()
    {
        var work = Rect(0, 0, 800, 1280);
        var keyboard = Rect(0, 680, 800, 1280);

        Assert.True(keyboard.Height > work.Height * 0.45);
        Assert.True(SipSuppressionPolicy.LooksLikeTouchKeyboard(keyboard, work));
        Assert.True(SipSuppressionPolicy.LooksLikeSuppressibleSip(keyboard, work));
        Assert.False(SipSuppressionPolicy.IsFullscreenSipHost(keyboard, work));
    }

    [Fact]
    public void Fullscreen_textinputhost_is_not_treated_as_the_keyboard()
    {
        var work = Rect(0, 0, 1280, 800);
        var host = Rect(0, 0, 1280, 800);

        Assert.True(SipSuppressionPolicy.IsFullscreenSipHost(host, work));
        Assert.False(SipSuppressionPolicy.LooksLikeTouchKeyboard(host, work));
        Assert.False(SipSuppressionPolicy.LooksLikeSuppressibleSip(host, work));
        Assert.False(SipSuppressionPolicy.IsOfficialSipSurface(true, host, work));
    }

    [Fact]
    public void Point_on_official_keyboard_chrome_is_unavailable_not_outside()
    {
        var work = Rect(0, 0, 800, 1280);
        var keyboard = Rect(0, 800, 800, 1280);

        Assert.True(SipSuppressionPolicy.IsOfficialSipSurface(true, keyboard, work));
        Assert.False(SipSuppressionPolicy.IsOfficialSipSurface(false, keyboard, work));
        Assert.False(SipSuppressionPolicy.IsOfficialSipSurface(true, Rect(0, 0, 800, 1280), work));
        Assert.True(SipSuppressionPolicy.LooksLikeSipHitSurface(Rect(40, 1180, 120, 1260), work));
        Assert.False(SipSuppressionPolicy.LooksLikeSipHitSurface(Rect(0, 200, 800, 1000), work));
    }

    [Fact]
    public void Touch_hit_slop_is_wider_than_mouse_but_not_huge()
    {
        Assert.False(TouchDevicePolicy.PreferTouchHitSlop(false));
        Assert.True(TouchDevicePolicy.PreferTouchHitSlop(true));
        Assert.Equal(3, TouchDevicePolicy.EdgeTolerance(false));
        Assert.Equal(16, TouchDevicePolicy.EdgeTolerance(true));
        Assert.True(TouchDevicePolicy.EdgeTolerance(true) < 48);
    }
}
