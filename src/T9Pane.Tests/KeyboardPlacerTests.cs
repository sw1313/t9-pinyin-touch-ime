using T9Pane.Native;
using T9Pane.Services;

namespace T9Pane.Tests;

public class KeyboardPlacerTests
{
    [Fact]
    public void Flyout_search_box_places_keyboard_over_menu_not_outside()
    {
        var fly = Rect(400, 200, 1200, 900);
        var search = Rect(420, 840, 980, 888);
        var work = Rect(0, 0, 1920, 1080);
        var box = KeyboardPlacer.Place(new InputField(IntPtr.Zero, search, fly), 620, 360, work);

        Assert.True(box.Intersects(fly), "九键必须叠在开始菜单上，不能整块在菜单外");
        Assert.True(box.Bottom <= search.Top + 8, "九键应贴在搜索框上方");
        Assert.True(box.Top < fly.Bottom - 80, "不能再落到菜单底边外侧");
    }

    [Fact]
    public void Ordinary_caret_stays_above_when_there_is_room()
    {
        var caret = Rect(200, 400, 202, 424);
        var work = Rect(0, 0, 1920, 1080);
        var box = KeyboardPlacer.Place(new InputField(IntPtr.Zero, caret, default), 620, 360, work);

        Assert.True(box.Bottom <= caret.Top);
        Assert.False(box.IsEmpty);
    }

    [Fact]
    public void Tight_work_area_does_not_clamp_over_the_caret_line()
    {
        var caret = Rect(200, 480, 202, 506);
        var work = Rect(0, 0, 1920, 1040);
        var box = KeyboardPlacer.Place(
            new InputField(IntPtr.Zero, caret, default), 620, 540, work);

        Assert.False(box.Intersects(caret), "夹到工作区时不能把打字行盖住");
        Assert.True(box.Bottom <= caret.Top || box.Top >= caret.Bottom);
    }

    [Fact]
    public void Chat_compose_box_taller_than_72_is_still_excluded()
    {
        var caret = Rect(480, 1140, 482, 1170);
        var fieldBox = Rect(400, 1080, 980, 1190);
        var work = Rect(0, 0, 1920, 1200);
        var box = KeyboardPlacer.Place(
            new InputField(
                IntPtr.Zero,
                caret,
                default,
                FieldBox: fieldBox),
            620,
            360,
            work);

        Assert.False(box.Intersects(fieldBox), "组合框高于 72 时仍不能盖住输入行");
        Assert.True(box.Bottom <= fieldBox.Top);
    }

    [Fact]
    public void Untrusted_box_caret_on_tall_field_excludes_the_bottom_line()
    {
        var boxTop = Rect(1058, 202, 1060, 226);
        var fieldBox = Rect(400, 200, 1200, 1180);
        var work = Rect(0, 0, 1920, 1200);
        var placed = KeyboardPlacer.Place(
            new InputField(
                IntPtr.Zero,
                boxTop,
                default,
                CaretIsTrusted: false,
                FieldBox: fieldBox),
            620,
            360,
            work);

        var typingLine = Rect(400, 1132, 1200, 1180);
        Assert.False(placed.Intersects(typingLine), "外框顶边不能把底行组合区盖住");
    }

    [Fact]
    public void Compact_field_box_is_excluded_like_cfs_exclude()
    {
        var caret = Rect(420, 844, 422, 868);
        var fieldBox = Rect(400, 840, 980, 888);
        var work = Rect(0, 0, 1920, 1080);
        var box = KeyboardPlacer.Place(
            new InputField(
                IntPtr.Zero,
                caret,
                default,
                FieldBox: fieldBox),
            620,
            360,
            work);

        Assert.False(box.Intersects(fieldBox));
        Assert.True(box.Bottom <= fieldBox.Top);
    }

    [Fact]
    public void Changing_application_restarts_visible_position_session()
    {
        Assert.True(KeyboardPositionSession.ShouldRestart(
            visible: true,
            currentHost: new IntPtr(100),
            nextHost: new IntPtr(200)));
    }

    [Fact]
    public void Same_application_keeps_dragged_position_until_hidden()
    {
        Assert.False(KeyboardPositionSession.ShouldRestart(
            visible: true,
            currentHost: new IntPtr(100),
            nextHost: new IntPtr(100)));
        Assert.False(KeyboardPositionSession.ShouldRestart(
            visible: false,
            currentHost: new IntPtr(100),
            nextHost: new IntPtr(200)));
        Assert.False(KeyboardPositionSession.ShouldTearDownBeforePlace(
            restart: true,
            nextRequiresHostRender: true));
        Assert.True(KeyboardPositionSession.ShouldTearDownBeforePlace(
            restart: true,
            nextRequiresHostRender: false));
        Assert.False(KeyboardPositionSession.ShouldTearDownBeforePlace(
            restart: false,
            nextRequiresHostRender: false));
    }

    [Fact]
    public void Moved_position_is_kept_only_while_caret_anchor_is_unchanged()
    {
        var first = Rect(200, 100, 820, 460);
        var sameCaret = Rect(206, 106, 826, 466);
        var secondCaret = Rect(600, 420, 1220, 780);

        Assert.True(KeyboardPositionSession.ShouldKeepMovedPosition(
            movedByUser: true,
            previousAnchor: first,
            nextAnchor: sameCaret));
        Assert.False(KeyboardPositionSession.ShouldKeepMovedPosition(
            movedByUser: true,
            previousAnchor: first,
            nextAnchor: secondCaret));
    }

    [Fact]
    public void System_search_rejects_native_caret_from_different_surface()
    {
        var ui = new InputField(new IntPtr(10), Rect(370, 8, 372, 40), default);
        var native = new InputField(new IntPtr(20), Rect(36, 8, 38, 40), default);

        Assert.True(InputFieldSelectionPolicy.TrySelect(
            systemTextHost: true,
            hasUiField: true,
            ui,
            hasNativeField: true,
            native,
            out var selected));
        Assert.Equal(ui, selected);
        Assert.True(InputFieldSelectionPolicy.NeedsAuthoritativeFirstShow(
            systemTextHost: true,
            hasUiField: true,
            ui,
            hasNativeField: true,
            native));
    }

    [Fact]
    public void Same_row_keeps_uia_field_box_so_address_bar_first_click_belongs()
    {
        var addressBox = Rect(200, 50, 1600, 90);
        var ui = new InputField(
            new IntPtr(10),
            Rect(265, 59, 267, 82),
            default,
            CaretIsTrusted: true,
            FieldId: "explorer.address",
            FieldBox: addressBox);
        var native = new InputField(
            new IntPtr(10),
            Rect(427, 59, 429, 82),
            default);

        Assert.True(InputFieldSelectionPolicy.TrySelect(
            systemTextHost: false,
            hasUiField: true,
            ui,
            hasNativeField: true,
            native,
            out var selected));
        Assert.Equal(native.Caret, selected.Caret);
        Assert.Equal(addressBox, selected.FieldBox);
        Assert.Equal("explorer.address", selected.FieldId);
        Assert.True(FieldClickPolicy.Belongs(selected.FieldBox, selected.Caret, 974, 66));
        Assert.False(FieldClickPolicy.Belongs(default, native.Caret, 974, 66));
    }

    [Fact]
    public void Uwp_uses_authoritative_native_tsf_field_on_same_surface()
    {
        var ui = new InputField(new IntPtr(10), Rect(370, 8, 372, 40), default);
        var native = new InputField(new IntPtr(10), Rect(36, 8, 38, 40), default);

        Assert.True(InputFieldSelectionPolicy.TrySelect(
            systemTextHost: true,
            hasUiField: true,
            ui,
            hasNativeField: true,
            native,
            out var selected));
        Assert.Equal(native, selected);
        Assert.False(InputFieldSelectionPolicy.NeedsAuthoritativeFirstShow(
            systemTextHost: true,
            hasUiField: true,
            ui,
            hasNativeField: true,
            native));
    }

    [Fact]
    public void Search_rejects_stale_native_caret_on_same_window_but_other_row()
    {
        var focusedTopSearch = new InputField(
            new IntPtr(10),
            Rect(60, 40, 62, 92),
            default);
        var staleTaskbarCaret = new InputField(
            new IntPtr(10),
            Rect(100, 1010, 102, 1034),
            default);

        Assert.True(InputFieldSelectionPolicy.NeedsAuthoritativeFirstShow(
            systemTextHost: true,
            hasUiField: true,
            focusedTopSearch,
            hasNativeField: true,
            staleTaskbarCaret));
    }

    [Fact]
    public void Desktop_ui_focus_can_use_more_precise_native_caret()
    {
        var ui = new InputField(new IntPtr(10), Rect(200, 400, 202, 424), default);
        var native = new InputField(new IntPtr(10), Rect(520, 640, 522, 664), default);

        Assert.True(InputFieldSelectionPolicy.TrySelect(
            systemTextHost: false,
            hasUiField: true,
            ui,
            hasNativeField: true,
            native,
            out var selected));
        Assert.Equal(native.Caret, selected.Caret);
    }

    [Fact]
    public void Trusted_uia_caret_beats_stale_native_on_another_dialog()
    {
        // 日志 16:21:40：UIA 已是新对话框 (1016,931)，原生还停在上一轮 (986,106)。
        var ui = new InputField(
            new IntPtr(10),
            Rect(1016, 931, 1018, 957),
            default,
            CaretIsTrusted: true);
        var native = new InputField(
            new IntPtr(10),
            Rect(986, 106, 988, 132),
            default);

        Assert.True(InputFieldSelectionPolicy.TrySelect(
            systemTextHost: false,
            hasUiField: true,
            ui,
            hasNativeField: true,
            native,
            out var selected));
        Assert.Equal(ui.Caret, selected.Caret);
    }

    [Fact]
    public void Taskbar_search_rejects_start_menu_caret_on_shared_searchhost()
    {
        var host = Rect(0, 0, 800, 1080);
        var startMenu = Rect(168, 87, 170, 111);
        var taskbar = Rect(118, 1037, 120, 1060);

        Assert.False(SearchCaretPolicy.Matches(
            PointerInvocationOrigin.TaskbarSearch, startMenu, host));
        Assert.True(SearchCaretPolicy.Matches(
            PointerInvocationOrigin.TaskbarSearch, taskbar, host));
        Assert.True(SearchCaretPolicy.Matches(
            PointerInvocationOrigin.StartMenuSurface, startMenu, host));
        Assert.False(SearchCaretPolicy.Matches(
            PointerInvocationOrigin.StartMenuSurface, taskbar, host));
    }

    [Fact]
    public void Word_uses_native_tsf_caret_without_a_uia_edit_control()
    {
        var native = new InputField(new IntPtr(10), Rect(520, 640, 522, 664), default);

        Assert.True(InputFieldSelectionPolicy.TrySelect(
            systemTextHost: false,
            hasUiField: false,
            default,
            hasNativeField: true,
            native,
            out var selected));
        Assert.Equal(native, selected);
    }

    [Fact]
    public void Desktop_native_and_uia_fallback_share_stable_foreground_surface()
    {
        var native = new InputField(
            new IntPtr(10),
            Rect(520, 640, 522, 664),
            default,
            Context: new InputContextKey(10, 4, 200, 0));
        var fallback = new InputField(
            new IntPtr(20),
            Rect(520, 640, 522, 664),
            default,
            Context: InputContextKey.ForFocus(new IntPtr(20), 7));

        var normalizedNative = InputFieldSelectionPolicy.NormalizeDesktopSurface(
            systemTextHost: false,
            native,
            foregroundRoot: new IntPtr(20),
            focusGeneration: 7);
        var normalizedFallback = InputFieldSelectionPolicy.NormalizeDesktopSurface(
            systemTextHost: false,
            fallback,
            foregroundRoot: new IntPtr(20),
            focusGeneration: 7);

        Assert.Equal(native.Caret, normalizedNative.Caret);
        Assert.Equal(new IntPtr(20), normalizedNative.TopLevel);
        Assert.Equal(normalizedFallback.Context, normalizedNative.Context);
        Assert.False(KeyboardPositionSession.ShouldRestart(
            visible: true,
            currentHost: normalizedNative.TopLevel,
            nextHost: normalizedFallback.TopLevel,
            currentContext: normalizedNative.Context,
            nextContext: normalizedFallback.Context));
    }

    [Fact]
    public void Same_line_holds_position_but_new_line_repositions_immediately()
    {
        Assert.True(KeyboardPositionSession.ShouldHoldForSameLine(
            visible: true,
            sameHost: true,
            sameContext: true,
            hasPosition: true,
            previousAnchor: Rect(200, 400, 820, 760),
            nextAnchor: Rect(440, 402, 1060, 762)));
        Assert.False(KeyboardPositionSession.ShouldHoldForSameLine(
            visible: true,
            sameHost: true,
            sameContext: true,
            hasPosition: true,
            previousAnchor: Rect(200, 400, 820, 760),
            nextAnchor: Rect(200, 426, 820, 786)));
        Assert.False(KeyboardPositionSession.ShouldFollowTypingLine(
            Rect(200, 400, 202, 422),
            Rect(240, 402, 242, 424)));
        Assert.True(KeyboardPositionSession.ShouldFollowTypingLine(
            Rect(200, 400, 202, 422),
            Rect(200, 426, 202, 448)));
        var held = KeyboardPositionSession.PinHorizontal(
            Rect(200, 400, 820, 760),
            Rect(440, 448, 1060, 808));
        Assert.Equal(200, held.Left);
        Assert.Equal(448, held.Top);
        Assert.Equal(620, held.Width);
        Assert.Equal(820, held.Right);
        Assert.False(KeyboardPositionSession.ShouldMoveVisibleWindow(
            sameRect: true,
            hostModeChanged: false));
        Assert.True(KeyboardPositionSession.ShouldMoveVisibleWindow(
            sameRect: false,
            hostModeChanged: false));
        Assert.True(KeyboardPositionSession.ShouldMoveVisibleWindow(
            sameRect: true,
            hostModeChanged: true));
        var box = new NativeRect { Left = 400, Top = 1060, Right = 900, Bottom = 1120 };
        var caret = new NativeRect { Left = 748, Top = 1080, Right = 750, Bottom = 1110 };
        var other = new NativeRect { Left = 419, Top = 959, Right = 421, Bottom = 997 };
        Assert.True(KeyboardPositionSession.CaretBelongsToAuthorizedField(
            box, caret, caret, box));
        Assert.False(KeyboardPositionSession.CaretBelongsToAuthorizedField(
            box, caret, other, default));
        Assert.False(KeyboardPositionSession.ShouldReplaceAuthorizedField(
            box,
            caret,
            other,
            default,
            incomingFromClicked: false,
            nativeAndUiAgree: false,
            nativeOnly: false));
        Assert.True(KeyboardPositionSession.ShouldReplaceAuthorizedField(
            box,
            caret,
            other,
            default,
            incomingFromClicked: false,
            nativeAndUiAgree: true,
            nativeOnly: false));
        Assert.True(KeyboardPositionSession.ShouldReplaceAuthorizedField(
            box,
            caret,
            other,
            default,
            incomingFromClicked: false,
            nativeAndUiAgree: false,
            nativeOnly: false,
            focusEntered: true,
            incomingCaretTrusted: true));
        Assert.True(KeyboardPositionSession.ShouldReplaceAuthorizedField(
            box,
            caret,
            other,
            default,
            incomingFromClicked: false,
            nativeAndUiAgree: false,
            nativeOnly: false,
            authorizedFieldId: "search",
            incomingFieldId: "address"));
        Assert.True(KeyboardPositionSession.ShouldReplaceAuthorizedField(
            box,
            caret,
            other,
            default,
            incomingFromClicked: false,
            nativeAndUiAgree: false,
            nativeOnly: false,
            manualTap: true));
        Assert.False(KeyboardPositionSession.ShouldFollowTypingLine(
            caret,
            new NativeRect { Left = 760, Top = 1080, Right = 762, Bottom = 1110 }));
        Assert.True(KeyboardPositionSession.ShouldFollowCaretTap(
            caret,
            new NativeRect { Left = 760, Top = 1080, Right = 762, Bottom = 1110 }));
        var sameLineTap = new NativeRect { Left = 760, Top = 1080, Right = 762, Bottom = 1110 };
        var otherFieldCaret = new NativeRect { Left = 485, Top = 1142, Right = 487, Bottom = 1172 };
        Assert.False(KeyboardPositionSession.LooksLikeAnotherField(
            box, caret, sameLineTap, box));
        Assert.True(KeyboardPositionSession.LooksLikeAnotherField(
            box, caret, otherFieldCaret, default));
        Assert.False(KeyboardPositionSession.ShouldHideWhenTapLeavesAuthorizedField(
            alreadyVisible: true,
            hasExternalGesture: true,
            caretBelongs: false,
            anotherField: false));
        Assert.True(KeyboardPositionSession.ShouldHideWhenTapLeavesAuthorizedField(
            alreadyVisible: true,
            hasExternalGesture: true,
            caretBelongs: false,
            anotherField: true));
        Assert.False(KeyboardPositionSession.ShouldHideWhenTapLeavesAuthorizedField(
            alreadyVisible: true,
            hasExternalGesture: true,
            caretBelongs: true,
            anotherField: true));
        Assert.False(KeyboardPositionSession.ShouldHideWhenTapLeavesAuthorizedField(
            alreadyVisible: true,
            hasExternalGesture: true,
            caretBelongs: false,
            anotherField: true,
            surfaceChanged: true));
        Assert.False(KeyboardPositionSession.ShouldHideWhenTapLeavesAuthorizedField(
            alreadyVisible: true,
            hasExternalGesture: true,
            caretBelongs: false,
            anotherField: true,
            searchSession: true));
        Assert.False(KeyboardPositionSession.ShouldFollowTypingLine(
            Rect(200, 400, 202, 422),
            Rect(200, 526, 480, 548),
            previousIsInsertion: true,
            nextIsInsertion: false));
    }

    [Fact]
    public void Layout_follows_the_caret_only_while_inputting()
    {
        Assert.True(KeyboardAnchorPolicy.ShouldFollowInput(
            pinned: false,
            hasInputCaret: true));
        Assert.False(KeyboardAnchorPolicy.ShouldFollowInput(
            pinned: false,
            hasInputCaret: false));
        Assert.False(KeyboardAnchorPolicy.ShouldFollowInput(
            pinned: true,
            hasInputCaret: true));
        Assert.True(KeyboardAnchorPolicy.ShouldClearDragAnchorOnUnlock(
            wasPinned: true,
            nowPinned: false));
        Assert.False(KeyboardAnchorPolicy.ShouldClearDragAnchorOnUnlock(
            wasPinned: false,
            nowPinned: false));
        Assert.True(KeyboardAnchorPolicy.ShouldRelayoutOnUnlock(
            nowPinned: false,
            hasInputCaret: true));
        Assert.False(KeyboardAnchorPolicy.ShouldRelayoutOnUnlock(
            nowPinned: false,
            hasInputCaret: false));
        Assert.True(KeyboardPinPolicy.ShouldHideOnUnlock(
            nowPinned: false,
            stillAuthorized: false));
        Assert.False(KeyboardPinPolicy.ShouldHideOnUnlock(
            nowPinned: false,
            stillAuthorized: true));
    }

    [Fact]
    public void Pinned_keyboard_ignores_auto_hide_and_auto_move()
    {
        Assert.True(KeyboardPinPolicy.ShouldAutoHide(false));
        Assert.False(KeyboardPinPolicy.ShouldAutoHide(true));
        Assert.True(KeyboardPinPolicy.ShouldKeepSessionPosition(
            pinned: true,
            hasPosition: true,
            repositionRequested: true));
        Assert.False(KeyboardPinPolicy.ShouldKeepSessionPosition(
            pinned: false,
            hasPosition: true,
            repositionRequested: true));
        Assert.False(KeyboardPinPolicy.ShouldRestart(pinned: true, wouldRestart: true));
        Assert.True(KeyboardPinPolicy.ShouldRestart(pinned: false, wouldRestart: true));
        Assert.False(KeyboardPinPolicy.ShouldHideForEmptyRect(pinned: true, rectEmpty: true));
        Assert.True(KeyboardPinPolicy.ShouldHideForEmptyRect(pinned: false, rectEmpty: true));
    }

    [Fact]
    public void Input_session_moves_only_after_explicit_reposition_request()
    {
        Assert.True(KeyboardPositionSession.ShouldKeepSessionPosition(
            hasPosition: true,
            repositionRequested: false));
        Assert.False(KeyboardPositionSession.ShouldKeepSessionPosition(
            hasPosition: true,
            repositionRequested: true));
        Assert.False(KeyboardPositionSession.ShouldKeepSessionPosition(
            hasPosition: false,
            repositionRequested: false));
    }

    [Fact]
    public void New_context_restarts_position_even_when_host_is_unchanged()
    {
        var oldContext = new InputContextKey(10, 3, 100, 0);
        var newContext = new InputContextKey(10, 4, 200, 0);

        Assert.True(KeyboardPositionSession.ShouldRestart(
            visible: true,
            currentHost: new IntPtr(50),
            nextHost: new IntPtr(50),
            currentContext: oldContext,
            nextContext: newContext));
        Assert.False(KeyboardPositionSession.ShouldHoldForSameLine(
            visible: true,
            sameHost: true,
            sameContext: false,
            hasPosition: true,
            previousAnchor: Rect(200, 400, 820, 760),
            nextAnchor: Rect(200, 401, 820, 761)));
    }

    [Fact]
    public void New_epoch_on_same_native_view_does_not_flash_visible_keyboard()
    {
        var oldContext = new InputContextKey(10, 3, 100, 0);
        var newContext = new InputContextKey(10, 4, 100, 0);

        Assert.True(KeyboardPositionSession.IsSameSurfaceContext(oldContext, newContext));
        Assert.False(KeyboardPositionSession.ShouldRestart(
            visible: true,
            currentHost: new IntPtr(50),
            nextHost: new IntPtr(50),
            currentContext: oldContext,
            nextContext: newContext));
    }

    [Fact]
    public void Manual_refocus_generation_still_restarts_same_uia_surface()
    {
        var oldContext = InputContextKey.ForFocus(new IntPtr(50), 3);
        var newContext = InputContextKey.ForFocus(new IntPtr(50), 4);

        Assert.False(KeyboardPositionSession.IsSameSurfaceContext(oldContext, newContext));
        Assert.True(KeyboardPositionSession.ShouldRestart(
            visible: true,
            currentHost: new IntPtr(50),
            nextHost: new IntPtr(50),
            currentContext: oldContext,
            nextContext: newContext));
    }

    private static NativeRect Rect(int l, int t, int r, int b) => new()
    {
        Left = l,
        Top = t,
        Right = r,
        Bottom = b
    };
}
