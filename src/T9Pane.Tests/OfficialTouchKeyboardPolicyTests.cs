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
    public void Missing_legacy_key_is_left_alone()
    {
        Assert.False(OfficialTouchKeyboardPolicy.ShouldWriteLegacyAutoInvoke(false));
        Assert.True(OfficialTouchKeyboardPolicy.ShouldWriteLegacyAutoInvoke(true));
    }
}
