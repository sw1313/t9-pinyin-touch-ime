using T9Pane.Native;
using T9Pane.Services;

namespace T9Pane.Tests;

public class SipLifecycleTests
{
    public SipLifecycleTests() => SipLifecycle.Shared.ResetForTests();

    [Fact]
    public void Opening_a_window_with_focus_does_not_show()
    {
        Assert.Equal(
            SipPhase.Hidden,
            SipLifecyclePolicy.NextPhase(
                SipPhase.Hidden,
                SipGesture.None,
                focusIsText: true,
                hasExternalGesture: false));
        Assert.False(SipLifecyclePolicy.ShouldShowForTouchFocus(
            hasExternalGesture: false,
            focusIsText: true));
        Assert.False(SipLifecyclePolicy.AllowsShow(SipGesture.None, focusIsText: true));
    }

    [Fact]
    public void Touch_on_edit_then_text_focus_shows()
    {
        Assert.Equal(SipGesture.OnEdit, SipLifecyclePolicy.ClassifySettled(true, false));
        Assert.Equal(
            SipPhase.Visible,
            SipLifecyclePolicy.NextPhase(
                SipPhase.Hidden,
                SipGesture.OnEdit,
                focusIsText: true,
                hasExternalGesture: true));
        Assert.True(SipLifecyclePolicy.ShouldReposition(SipGesture.OnEdit));
    }

    [Fact]
    public void Leave_hides_every_time()
    {
        var afterFirst = SipLifecyclePolicy.NextPhase(
            SipPhase.Visible,
            SipGesture.OnLeave,
            focusIsText: false,
            hasExternalGesture: true);
        var afterSecond = SipLifecyclePolicy.NextPhase(
            afterFirst,
            SipGesture.OnLeave,
            focusIsText: false,
            hasExternalGesture: true);
        Assert.Equal(SipPhase.Hidden, afterFirst);
        Assert.Equal(SipPhase.Hidden, afterSecond);
        Assert.True(SipLifecyclePolicy.ShouldHideOnFocusSettled(
            visible: true,
            overlayOwnsContact: false,
            focusIsText: false,
            hardLeave: true,
            selectionChrome: false));
    }

    [Fact]
    public void Overlay_contact_does_not_reposition_or_hide()
    {
        Assert.False(SipLifecyclePolicy.ShouldReposition(SipGesture.OnOverlay));
        Assert.Equal(
            SipPhase.Visible,
            SipLifecyclePolicy.NextPhase(
                SipPhase.Visible,
                SipGesture.OnOverlay,
                focusIsText: true,
                hasExternalGesture: false));
        Assert.False(SipLifecyclePolicy.ShouldHideOnFocusSettled(
            visible: true,
            overlayOwnsContact: true,
            focusIsText: false,
            hardLeave: true,
            selectionChrome: false));
    }

    [Fact]
    public void Leave_clears_the_finger_so_tsf_cannot_show_again()
    {
        SipLifecycle.Shared.NoteExternalTouch();
        Assert.True(SipLifecycle.Shared.HasRecentExternalGesture());
        SipLifecycle.Shared.NoteLeave();
        Assert.Equal(SipGesture.OnLeave, SipLifecycle.Shared.Gesture);
        Assert.False(SipLifecycle.Shared.HasRecentExternalGesture());
    }

    [Fact]
    public void Overlay_claim_clears_external_gesture()
    {
        SipLifecycle.Shared.NoteExternalTouch();
        Assert.True(SipLifecycle.Shared.HasRecentExternalGesture());
        SipLifecycle.Shared.NoteOverlayContact();
        Assert.Equal(SipGesture.OnOverlay, SipLifecycle.Shared.Gesture);
        Assert.False(SipLifecycle.Shared.HasRecentExternalGesture());
        Assert.True(SipLifecycle.Shared.OwnsOverlayContact(false));
        Assert.True(SipLifecycle.Shared.HasRecentTouch());
    }

    [Fact]
    public void Tablet_path_skips_click_geometry()
    {
        Assert.True(SipLifecyclePolicy.ShouldIgnoreClickGeometry(
            hasExternalGesture: true,
            overlayOwnsContact: false,
            hasScreenPoint: false));
        Assert.True(SipLifecyclePolicy.ShouldIgnoreClickGeometry(
            hasExternalGesture: false,
            overlayOwnsContact: true,
            hasScreenPoint: false));
        Assert.False(SipLifecyclePolicy.ShouldIgnoreClickGeometry(
            hasExternalGesture: true,
            overlayOwnsContact: false,
            hasScreenPoint: true));
    }

    [Fact]
    public void Same_line_typing_does_not_follow_as_new_field()
    {
        var sameLine = new NativeRect { Left = 200, Top = 400, Right = 202, Bottom = 422 };
        var sameLineMoved = new NativeRect { Left = 240, Top = 402, Right = 242, Bottom = 424 };
        var newLine = new NativeRect { Left = 200, Top = 426, Right = 202, Bottom = 448 };
        Assert.False(KeyboardPositionSession.ShouldFollowTypingLine(sameLine, sameLineMoved));
        Assert.False(KeyboardPositionSession.ShouldFollowTypingLine(
            sameLine,
            new NativeRect { Left = 200, Top = 412, Right = 202, Bottom = 434 }));
        Assert.True(KeyboardPositionSession.ShouldFollowTypingLine(sameLine, newLine));
        Assert.True(SipLifecyclePolicy.ShouldStayForKeyboardFocus(
            overlayLive: false,
            overlayContactFresh: false,
            focusedOwnPane: true));
        Assert.True(SipLifecyclePolicy.ShouldStayForKeyboardFocus(
            overlayLive: true,
            overlayContactFresh: false,
            focusedOwnPane: false));
        Assert.False(SipLifecyclePolicy.ShouldStayForKeyboardFocus(
            overlayLive: false,
            overlayContactFresh: false,
            focusedOwnPane: false));
        Assert.False(SipLifecyclePolicy.ShouldReposition(SipGesture.None));
        Assert.False(SipLifecyclePolicy.ShouldReposition(SipGesture.OnOverlay));
    }

    [Fact]
    public void Hidden_non_text_is_pending_visible_non_text_is_leave()
    {
        Assert.Equal(
            SipGesture.None,
            SipLifecyclePolicy.ClassifySettled(false, false, alreadyVisible: false));
        Assert.Equal(
            SipGesture.OnLeave,
            SipLifecyclePolicy.ClassifySettled(false, false, alreadyVisible: true));
        Assert.Equal(
            SipGesture.None,
            SipLifecyclePolicy.ClassifySettled(false, false, holdForSearch: true, alreadyVisible: true));
        Assert.True(SipLifecyclePolicy.ShouldHideWhenFieldMissing(
            alreadyShown: true,
            searchSession: false,
            selectionChrome: false,
            leaveArmed: false,
            gesture: SipGesture.OnLeave));
        Assert.False(SipLifecyclePolicy.ShouldHideWhenFieldMissing(
            alreadyShown: false,
            searchSession: false,
            selectionChrome: false,
            leaveArmed: false,
            gesture: SipGesture.None));
        Assert.False(SipLifecyclePolicy.ShouldHideWhenFieldMissing(
            alreadyShown: true,
            searchSession: true,
            selectionChrome: false,
            leaveArmed: false,
            gesture: SipGesture.None));
        Assert.False(SipLifecyclePolicy.ShouldHideWhenFieldMissing(
            alreadyShown: true,
            searchSession: false,
            selectionChrome: false,
            leaveArmed: false,
            gesture: SipGesture.None,
            hasExternalGesture: true));
        Assert.False(SipLifecyclePolicy.ShouldHideWhenFieldMissing(
            alreadyShown: true,
            searchSession: true,
            selectionChrome: false,
            leaveArmed: false,
            gesture: SipGesture.None,
            hasExternalGesture: true));
    }

    [Fact]
    public void Official_show_only_after_a_finger_on_text()
    {
        Assert.Equal(
            SipVisibilityAction.Stay,
            SipLifecyclePolicy.Decide(
                alreadyVisible: false,
                overlayLive: false,
                overlayContactFresh: false,
                focusIsText: true,
                hardLeave: false,
                selectionChrome: false,
                hasExternalGesture: false));
        Assert.Equal(
            SipVisibilityAction.Show,
            SipLifecyclePolicy.Decide(
                alreadyVisible: false,
                overlayLive: false,
                overlayContactFresh: false,
                focusIsText: true,
                hardLeave: false,
                selectionChrome: false,
                hasExternalGesture: true));
    }

    [Fact]
    public void Official_hide_on_the_next_finger_that_leaves_text()
    {
        Assert.Equal(
            SipVisibilityAction.Hide,
            SipLifecyclePolicy.Decide(
                alreadyVisible: true,
                overlayLive: false,
                overlayContactFresh: false,
                focusIsText: false,
                hardLeave: true,
                selectionChrome: false,
                hasExternalGesture: true));
        Assert.Equal(
            SipVisibilityAction.Hide,
            SipLifecyclePolicy.Decide(
                alreadyVisible: true,
                overlayLive: false,
                overlayContactFresh: false,
                focusIsText: false,
                hardLeave: false,
                selectionChrome: false,
                hasExternalGesture: true));
        Assert.Equal(
            SipVisibilityAction.Hide,
            SipLifecyclePolicy.Decide(
                alreadyVisible: true,
                overlayLive: false,
                overlayContactFresh: false,
                focusIsText: true,
                hardLeave: true,
                selectionChrome: false,
                hasExternalGesture: false));
        Assert.Equal(
            SipVisibilityAction.Stay,
            SipLifecyclePolicy.Decide(
                alreadyVisible: true,
                overlayLive: false,
                overlayContactFresh: false,
                focusIsText: false,
                hardLeave: true,
                selectionChrome: false,
                hasExternalGesture: true,
                contextMenu: false,
                searchSession: true));
    }

    [Fact]
    public void Next_finger_is_not_swallowed_just_because_keyboard_is_up()
    {
        Assert.Equal(
            SipVisibilityAction.Relayout,
            SipLifecyclePolicy.Decide(
                alreadyVisible: true,
                overlayLive: false,
                overlayContactFresh: false,
                focusIsText: true,
                hardLeave: false,
                selectionChrome: false,
                hasExternalGesture: true));
        Assert.Equal(
            SipVisibilityAction.Pending,
            SipLifecyclePolicy.Decide(
                alreadyVisible: false,
                overlayLive: false,
                overlayContactFresh: false,
                focusIsText: false,
                hardLeave: false,
                selectionChrome: false,
                hasExternalGesture: true));
    }

    [Fact]
    public void Typing_on_the_board_does_not_hide_or_reinvoke()
    {
        Assert.Equal(
            SipVisibilityAction.Stay,
            SipLifecyclePolicy.Decide(
                alreadyVisible: true,
                overlayLive: true,
                overlayContactFresh: true,
                focusIsText: true,
                hardLeave: false,
                selectionChrome: false,
                hasExternalGesture: false));
        Assert.Equal(
            SipVisibilityAction.Stay,
            SipLifecyclePolicy.Decide(
                alreadyVisible: true,
                overlayLive: false,
                overlayContactFresh: true,
                focusIsText: false,
                hardLeave: false,
                selectionChrome: false,
                hasExternalGesture: false));
    }

    [Fact]
    public void Visible_keyboard_moves_only_for_new_field_or_new_line()
    {
        Assert.True(SipLifecyclePolicy.ShouldRepositionNow(
            alreadyVisible: false,
            fieldChanged: false,
            lineChanged: false));
        Assert.False(SipLifecyclePolicy.ShouldRepositionNow(
            alreadyVisible: true,
            fieldChanged: false,
            lineChanged: false));
        Assert.True(SipLifecyclePolicy.ShouldRepositionNow(
            alreadyVisible: true,
            fieldChanged: true,
            lineChanged: false));
        Assert.True(SipLifecyclePolicy.ShouldRepositionNow(
            alreadyVisible: true,
            fieldChanged: false,
            lineChanged: true));
        Assert.True(SipLifecyclePolicy.ShouldRepositionNow(
            alreadyVisible: true,
            fieldChanged: false,
            lineChanged: false,
            caretTapped: true));
        Assert.False(SipLifecyclePolicy.ShouldRepositionNow(
            alreadyVisible: true,
            fieldChanged: false,
            lineChanged: false,
            caretTapped: false));
        Assert.True(SipLifecyclePolicy.FieldIdentityChanged("search", "address"));
        Assert.False(SipLifecyclePolicy.FieldIdentityChanged("search", "search"));
        Assert.True(KeyboardSurfacePolicy.IsShown(wpfVisible: false, hosting: true));
        Assert.False(SipLifecyclePolicy.ShouldRepositionNow(
            alreadyVisible: KeyboardSurfacePolicy.IsShown(false, true),
            fieldChanged: false,
            lineChanged: false));
    }

    [Fact]
    public void Overlay_hold_does_not_yield_the_keyboard_to_a_context_menu()
    {
        Assert.False(PointerContactPolicy.ShouldYieldToContextMenu(
            contextMenu: true,
            overlayOwnsContact: true));
        Assert.True(PointerContactPolicy.ShouldYieldToContextMenu(
            contextMenu: true,
            overlayOwnsContact: false));
        Assert.False(PointerContactPolicy.ShouldYieldToContextMenu(
            contextMenu: false,
            overlayOwnsContact: false));
        Assert.False(PointerContactPolicy.ShouldYieldToContextMenu(
            contextMenu: true,
            overlayOwnsContact: false,
            focusedText: true));
        Assert.Equal(
            NativeMethods.TabletDisablePressAndHold
            | NativeMethods.TabletDisablePenTapFeedback
            | NativeMethods.TabletDisablePenBarrelFeedback
            | NativeMethods.TabletDisableFlicks,
            TabletGesturePolicy.QueryStatus());
        Assert.Equal(
            SipVisibilityAction.Stay,
            SipLifecyclePolicy.Decide(
                alreadyVisible: true,
                overlayLive: true,
                overlayContactFresh: false,
                focusIsText: false,
                hardLeave: false,
                selectionChrome: false,
                hasExternalGesture: true,
                contextMenu: true));
    }
}
