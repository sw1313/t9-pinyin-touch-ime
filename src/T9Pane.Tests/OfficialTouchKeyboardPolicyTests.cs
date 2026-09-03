using T9Pane.Services;

namespace T9Pane.Tests;

public class OfficialTouchKeyboardPolicyTests
{
    [Fact]
    public void Suppresses_only_when_t9_is_selected_and_pane_is_on()
    {
        Assert.True(OfficialTouchKeyboardPolicy.ShouldSuppress(true, true));
        Assert.False(OfficialTouchKeyboardPolicy.ShouldSuppress(false, true));
        Assert.False(OfficialTouchKeyboardPolicy.ShouldSuppress(true, false));
        Assert.False(OfficialTouchKeyboardPolicy.ShouldSuppress(false, false));
    }

    [Fact]
    public void Live_t9_is_only_the_official_profile_sink()
    {
        Assert.True(OfficialTouchKeyboardPolicy.IsT9Live(true));
        Assert.False(OfficialTouchKeyboardPolicy.IsT9Live(false));
    }

    [Fact]
    public void First_hold_saves_when_no_keyboard_attached()
    {
        var backup = OfficialTouchKeyboardPolicy.CaptureBackup(
            alreadyHeld: false,
            existing: default,
            hadEnable: true,
            enable: 1,
            hadTap: true,
            tap: OfficialTouchKeyboardPolicy.WhenNoKeyboardAttached);

        Assert.True(backup.Held);
        Assert.True(backup.HadTouchKeyboardTapInvoke);
        Assert.Equal(OfficialTouchKeyboardPolicy.WhenNoKeyboardAttached, backup.TouchKeyboardTapInvoke);
        Assert.True(backup.HadEnableDesktopModeAutoInvoke);
        Assert.Equal(1, backup.EnableDesktopModeAutoInvoke);
    }

    [Fact]
    public void Already_holding_keeps_original_never_overwrites_with_zero()
    {
        var existing = new TabletTipBackup(
            Held: true,
            HadEnableDesktopModeAutoInvoke: true,
            EnableDesktopModeAutoInvoke: 1,
            HadTouchKeyboardTapInvoke: true,
            TouchKeyboardTapInvoke: OfficialTouchKeyboardPolicy.WhenNoKeyboardAttached);

        var next = OfficialTouchKeyboardPolicy.CaptureBackup(
            alreadyHeld: true,
            existing,
            hadEnable: true,
            enable: OfficialTouchKeyboardPolicy.Never,
            hadTap: true,
            tap: OfficialTouchKeyboardPolicy.Never);

        Assert.Equal(existing, next);
    }

    [Fact]
    public void Hold_always_writes_the_legacy_auto_invoke_key()
    {
        Assert.True(OfficialTouchKeyboardPolicy.ShouldWriteLegacyAutoInvoke(false));
        Assert.True(OfficialTouchKeyboardPolicy.ShouldWriteLegacyAutoInvoke(true));
    }

    [Fact]
    public void Official_show_touch_keyboard_prefers_tabtip_tap_invoke()
    {
        var chosen = OfficialTouchKeyboardPolicy.PreferOfficialTap(
            hadTabletTip: true,
            tabletTip: OfficialTouchKeyboardPolicy.WhenNoKeyboardAttached,
            hadInput: true,
            input: OfficialTouchKeyboardPolicy.Never);

        Assert.True(chosen.Had);
        Assert.Equal(OfficialTouchKeyboardPolicy.WhenNoKeyboardAttached, chosen.Value);
    }

    [Fact]
    public void Official_tap_falls_back_to_input_settings_when_tabtip_missing()
    {
        var chosen = OfficialTouchKeyboardPolicy.PreferOfficialTap(
            hadTabletTip: false,
            tabletTip: 0,
            hadInput: true,
            input: OfficialTouchKeyboardPolicy.Always);

        Assert.True(chosen.Had);
        Assert.Equal(OfficialTouchKeyboardPolicy.Always, chosen.Value);
    }

    [Fact]
    public void Legacy_tabtip_is_used_when_win11_key_is_missing()
    {
        var chosen = OfficialTouchKeyboardPolicy.PreferUserValue(
            hadModern: false,
            modern: 0,
            hadLegacy: true,
            legacy: OfficialTouchKeyboardPolicy.Always);

        Assert.True(chosen.Had);
        Assert.Equal(OfficialTouchKeyboardPolicy.Always, chosen.Value);
    }

    [Fact]
    public void First_hold_saves_win11_settings_invocation_policy()
    {
        var backup = OfficialTouchKeyboardPolicy.CaptureBackup(
            alreadyHeld: false,
            existing: default,
            hadEnable: false,
            enable: 0,
            hadTap: false,
            tap: 0,
            hadInvocation: true,
            invocation: OfficialTouchKeyboardPolicy.WhenNoKeyboardAttached);

        Assert.True(backup.HadTouchKeyboardInvocationPolicy);
        Assert.Equal(OfficialTouchKeyboardPolicy.WhenNoKeyboardAttached, backup.TouchKeyboardInvocationPolicy);
        Assert.False(backup.HadTouchKeyboardTapInvoke);
    }

    [Fact]
    public void Already_holding_keeps_original_invocation_policy()
    {
        var existing = new TabletTipBackup(
            Held: true,
            HadEnableDesktopModeAutoInvoke: false,
            EnableDesktopModeAutoInvoke: 0,
            HadTouchKeyboardTapInvoke: false,
            TouchKeyboardTapInvoke: 0,
            HadTouchKeyboardInvocationPolicy: true,
            TouchKeyboardInvocationPolicy: OfficialTouchKeyboardPolicy.WhenNoKeyboardAttached);

        var next = OfficialTouchKeyboardPolicy.CaptureBackup(
            alreadyHeld: true,
            existing,
            hadEnable: false,
            enable: 0,
            hadTap: false,
            tap: 0,
            hadInvocation: true,
            invocation: OfficialTouchKeyboardPolicy.Never);

        Assert.Equal(existing, next);
    }
}
