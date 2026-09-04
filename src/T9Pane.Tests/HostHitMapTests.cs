using System.Windows.Automation;
using System.Windows.Threading;
using T9Pane.Native;
using T9Pane.Services;

namespace T9Pane.Tests;

public class HostHitMapTests
{
    [Fact]
    public void Hosted_hidden_window_still_collects_laid_out_keys()
    {
        Assert.True(HostHitMap.ShouldCollect(
            layoutVisible: true,
            enabled: true,
            renderWidth: 80,
            renderHeight: 64));
        Assert.False(HostHitMap.ShouldCollect(
            layoutVisible: false,
            enabled: true,
            renderWidth: 80,
            renderHeight: 64));
        Assert.False(HostHitMap.ShouldCollect(
            layoutVisible: true,
            enabled: true,
            renderWidth: 1,
            renderHeight: 64));
        Assert.False(HostHitMap.ShouldReplaceRegions(incomingCount: 0, existingCount: 12));
        Assert.True(HostHitMap.ShouldReplaceRegions(incomingCount: 12, existingCount: 12));
        Assert.True(HostHitMap.ShouldReplaceRegions(incomingCount: 0, existingCount: 0));
        Assert.True(HostHitMap.CoversFrameHeight(380, 400));
        Assert.False(HostHitMap.CoversFrameHeight(120, 400));
        Assert.True(HostHitMap.IsLayoutCollapsed(80, 404));
        Assert.False(HostHitMap.IsLayoutCollapsed(404, 404));
        Assert.False(HostHitMap.ShouldReplaceRegions(8, 16, incomingCoversFrame: false));
        Assert.True(HostHitMap.CountsAsExpected(layoutVisible: true, enabled: true));
        Assert.False(HostHitMap.CountsAsExpected(layoutVisible: false, enabled: true));
        Assert.True(HostHitMap.ShouldRetryAfterRebuild(collected: 4, expected: 16));
        Assert.False(HostHitMap.ShouldRetryAfterRebuild(collected: 16, expected: 16));
        Assert.False(HostHitMap.ShouldRetryAfterRebuild(collected: 0, expected: 0));
        Assert.Equal(DispatcherPriority.Loaded, HostFrame.PublishPriority);
    }

    [Fact]
    public void Captured_pixel_coordinates_select_the_button_region()
    {
        HostHitRegion<string>[] regions =
        [
            new(100, 200, 300, 320, "5"),
            new(300, 200, 500, 320, "6")
        ];

        Assert.Equal("5", HostHitMap.Find(regions, 208, 276));
        Assert.Equal("6", HostHitMap.Find(regions, 400, 276));
        Assert.Null(HostHitMap.Find(regions, 99, 276));
    }

    [Fact]
    public void Dpi_virtualized_search_host_clicks_map_onto_the_physical_frame()
    {
        // 0.1.48 开始菜单搜索：客户区 400×404（96 DPI），帧 600×606（150%），
        // 点 (231,316) 对不上物理命中图；还原后应落在同一颗键上。
        var mapped = HostHitMap.MapClientToFrame(231, 316, 400, 404, 600, 606);
        Assert.Equal(347, mapped.X);
        Assert.Equal(474, mapped.Y);
        Assert.Equal((231, 316), HostHitMap.MapClientToFrame(231, 316, 600, 606, 600, 606));

        HostHitRegion<string>[] regions =
        [
            new(300, 420, 400, 520, "8")
        ];
        Assert.Null(HostHitMap.Find(regions, 231, 316));
        Assert.Equal("8", HostHitMap.Find(regions, mapped.X, mapped.Y));
    }

    [Fact]
    public void Search_host_pixel_click_maps_onto_dip_key_regions()
    {
        // 0.1.51 开始菜单搜索：命中图 34 键仍在 DIP（DEF 约 244–332,132–220），
        // Band 上报的是 150% 位图像素 (394,257)。换到布局空间后应点中。
        HostHitRegion<string>[] keys =
        [
            new(244, 132, 332, 220, "DEF"),
            new(156, 308, 244, 396, "TUV")
        ];

        Assert.Equal(
            "DEF",
            HostHitMap.FindLayout(keys, 394, 257, 400, 404, 600, 606));
        Assert.Equal(
            "TUV",
            HostHitMap.FindLayout(keys, 231, 316, 400, 404, 600, 606));
        Assert.Equal(
            "DEF",
            HostHitMap.FindLayout(keys, 270, 170, 400, 404, 400, 404));
        Assert.Null(HostHitMap.FindLayout(keys, 20, 20, 400, 404, 600, 606));
    }

    [Fact]
    public void Scaled_click_never_falls_back_to_the_wrong_key_via_raw_coordinates()
    {
        // 150% 下物理坐标几乎总落在 DIP 命中图范围内。先试原值就会稳定命中错键，
        // 而且一条「未命中」都不会报——看着像修好了，实际按哪儿都错位。
        HostHitRegion<string>[] board =
        [
            new(60, 132, 148, 220, "分词"),
            new(148, 220, 236, 308, "JKL")
        ];

        // 点「分词」正中，150% 下上报 (156,264)：原值会落进 JKL。
        Assert.Equal("JKL", HostHitMap.Find(board, 156, 264));
        Assert.Equal(
            "分词",
            HostHitMap.FindLayout(board, 156, 264, 400, 404, 600, 606, out var space));
        Assert.Equal("px→dip", space);

        // 缩放为 1 时仍按原值判定。
        Assert.Equal(
            "JKL",
            HostHitMap.FindLayout(board, 156, 264, 400, 404, 400, 404, out var same));
        Assert.Equal("raw", same);
    }

    [Fact]
    public void Search_compose_payload_keeps_pinyin_and_packs_candidates()
    {
        var packed = SearchCandidatePayload.Encode("nihao", ["你好", "拟好", "逆号"]);
        Assert.Equal("nihao", SearchCandidatePayload.ComposeText(packed));
        Assert.Contains('\u001e', packed);
        Assert.Equal("nihao", SearchCandidatePayload.ComposeText("nihao"));
        Assert.Equal("", SearchCandidatePayload.ComposeText(""));
        Assert.Equal(ImeHost.KindSearchCandidates, 9);
    }

    [Fact]
    public void Candidate_buttons_upsert_into_an_existing_hit_map()
    {
        var keys = new List<HostHitRegion<string>>
        {
            new(60, 132, 148, 220, "分词"),
            new(332, 80, 392, 168, "退格")
        };
        var candidates = new List<HostHitRegion<string>>
        {
            new(100, 50, 160, 78, "你好")
        };

        HostHitMap.Upsert(keys, candidates);
        Assert.Equal(3, keys.Count);
        Assert.Equal("你好", HostHitMap.Find(keys, 120, 60));
        Assert.Equal("退格", HostHitMap.Find(keys, 350, 120));

        HostHitMap.Upsert(keys, [new(110, 50, 170, 78, "你好")]);
        Assert.Equal(3, keys.Count);
        Assert.Equal("你好", HostHitMap.Find(keys, 165, 60));

        HostHitMap.RemoveTarget(keys, "你好");
        Assert.Null(HostHitMap.Find(keys, 165, 60));
    }

    [Fact]
    public void Hosted_dynamic_button_invokes_its_bound_tap_action()
    {
        var actions = new HostActionMap<object>();
        var dynamicButton = new object();
        var staticButton = new object();
        var invoked = false;

        actions.Bind(dynamicButton, () => invoked = true);

        Assert.True(actions.TryInvoke(dynamicButton));
        Assert.True(invoked);
        Assert.False(actions.TryInvoke(staticButton));
    }

    [Fact]
    public void Only_current_t9_profile_allows_keyboard_visibility()
    {
        Assert.True(KeyboardVisibilityPolicy.ShouldShow(true, false, true));
        Assert.False(KeyboardVisibilityPolicy.ShouldShow(true, false, false));
        Assert.False(KeyboardVisibilityPolicy.ShouldShow(false, false, true));
        Assert.False(KeyboardVisibilityPolicy.ShouldShow(true, true, true));
        Assert.False(KeyboardVisibilityPolicy.ShouldShow(
            enabled: true,
            userDismissed: false,
            t9ContextActive: true,
            invocationAuthorized: false));
    }

    [Fact]
    public void Visibility_requires_a_focused_t9_client_in_every_kind_of_window()
    {
        Assert.False(KeyboardVisibilityPolicy.IsT9ContextActive(hasFocusedClient: false));
        Assert.True(KeyboardVisibilityPolicy.IsT9ContextActive(hasFocusedClient: true));
    }

    [Fact]
    public void Programmatic_refocus_after_clicking_group_does_not_authorize_keyboard()
    {
        Assert.False(KeyboardInvocationPolicy.ShouldAuthorize(
            pointerInsideFocusedInput: false,
            systemTextHost: false));
    }

    [Fact]
    public void Direct_input_click_authorizes_keyboard()
    {
        Assert.True(KeyboardInvocationPolicy.ShouldAuthorize(
            pointerInsideFocusedInput: true,
            systemTextHost: false));
        Assert.True(InputInvocationProbe.Contains(
            new NativeRect { Left = 100, Top = 200, Right = 500, Bottom = 260 },
            x: 240,
            y: 230));
        Assert.False(InputInvocationProbe.Contains(
            new NativeRect { Left = 100, Top = 200, Right = 500, Bottom = 260 },
            x: 40,
            y: 230));
    }

    [Fact]
    public void Dismissed_keyboard_reopens_only_after_direct_input_click()
    {
        Assert.False(KeyboardVisibilityPolicy.ShouldShow(
            enabled: true,
            userDismissed: true,
            t9ContextActive: true,
            invocationAuthorized: false));

        var authorized = KeyboardInvocationPolicy.ShouldAuthorize(
            pointerInsideFocusedInput: true,
            systemTextHost: false);

        Assert.True(authorized);
        Assert.True(KeyboardVisibilityPolicy.ShouldShow(
            enabled: true,
            userDismissed: false,
            t9ContextActive: true,
            invocationAuthorized: authorized));
    }

    [Fact]
    public void Taskbar_search_entry_authorizes_system_search()
    {
        Assert.True(KeyboardInvocationPolicy.ShouldAuthorize(
            pointerInsideFocusedInput: false,
            systemTextHost: true,
            origin: PointerInvocationOrigin.TaskbarSearch));
        Assert.True(KeyboardInvocationPolicy.ShouldAuthorize(
            pointerInsideFocusedInput: false,
            systemTextHost: true,
            origin: PointerInvocationOrigin.StartMenuSearch));
    }

    [Fact]
    public void Authorized_system_search_handoff_ignores_launcher_coordinates()
    {
        Assert.True(KeyboardInvocationPolicy.ShouldAuthorize(
            pointerInsideFocusedInput: false,
            systemTextHost: true,
            previouslyAuthorized: true,
            userDismissed: false));
    }

    [Fact]
    public void Existing_authorization_does_not_bypass_normal_or_dismissed_surfaces()
    {
        Assert.False(KeyboardInvocationPolicy.ShouldAuthorize(
            pointerInsideFocusedInput: false,
            systemTextHost: false,
            previouslyAuthorized: true,
            userDismissed: false));
        Assert.False(KeyboardInvocationPolicy.ShouldAuthorize(
            pointerInsideFocusedInput: false,
            systemTextHost: true,
            previouslyAuthorized: true,
            userDismissed: true));
    }

    [Fact]
    public void Program_switch_disarms_but_internal_input_surface_change_does_not()
    {
        Assert.True(KeyboardInvocationPolicy.ShouldDisarmForForegroundChange(
            new IntPtr(100),
            new IntPtr(200)));
        Assert.False(KeyboardInvocationPolicy.ShouldDisarmForForegroundChange(
            new IntPtr(100),
            new IntPtr(100)));
        Assert.False(KeyboardInvocationPolicy.ShouldDisarmForForegroundChange(
            new IntPtr(100),
            IntPtr.Zero));
        // 开始菜单把输入交给 SearchHost 不是换应用。
        Assert.False(KeyboardInvocationPolicy.ShouldDisarmForForegroundChange(
            new IntPtr(100),
            new IntPtr(200),
            searchHandoff: true));
    }

    [Fact]
    public void Search_click_waits_for_authoritative_searchhost_context()
    {
        Assert.False(KeyboardInvocationPolicy.TryResolvePointer(
            PointerInputHit.Unavailable,
            systemTextHost: false,
            origin: PointerInvocationOrigin.TaskbarSearch,
            previouslyAuthorized: false,
            userDismissed: false,
            expired: false,
            out _));
        Assert.False(KeyboardInvocationPolicy.TryResolvePointer(
            PointerInputHit.Unavailable,
            systemTextHost: true,
            origin: PointerInvocationOrigin.TaskbarSearch,
            previouslyAuthorized: false,
            userDismissed: false,
            expired: false,
            out _));
        Assert.True(KeyboardInvocationPolicy.TryResolvePointer(
            PointerInputHit.Inside,
            systemTextHost: true,
            origin: PointerInvocationOrigin.TaskbarSearch,
            previouslyAuthorized: false,
            userDismissed: false,
            expired: false,
            out var authorized));
        Assert.True(authorized);
    }

    [Fact]
    public void Menu_search_click_is_not_consumed_before_uia_focus_is_ready()
    {
        Assert.False(KeyboardInvocationPolicy.TryResolvePointer(
            PointerInputHit.Unavailable,
            systemTextHost: true,
            origin: PointerInvocationOrigin.Unknown,
            previouslyAuthorized: false,
            userDismissed: false,
            expired: false,
            out _));
        Assert.True(KeyboardInvocationPolicy.TryResolvePointer(
            PointerInputHit.Inside,
            systemTextHost: true,
            origin: PointerInvocationOrigin.Unknown,
            previouslyAuthorized: false,
            userDismissed: false,
            expired: false,
            out var authorized));
        Assert.True(authorized);
    }

    [Fact]
    public void Classified_start_menu_search_waits_until_click_hits_focused_input()
    {
        Assert.False(KeyboardInvocationPolicy.TryResolvePointer(
            PointerInputHit.Unavailable,
            systemTextHost: true,
            origin: PointerInvocationOrigin.StartMenuSearch,
            previouslyAuthorized: false,
            userDismissed: false,
            expired: false,
            out _));
        Assert.True(KeyboardInvocationPolicy.TryResolvePointer(
            PointerInputHit.Inside,
            systemTextHost: true,
            origin: PointerInvocationOrigin.StartMenuSearch,
            previouslyAuthorized: false,
            userDismissed: false,
            expired: false,
            out var authorized));
        Assert.True(authorized);
    }

    [Fact]
    public void Start_menu_surface_click_waits_until_replacement_search_box_is_focused()
    {
        Assert.False(KeyboardInvocationPolicy.TryResolvePointer(
            PointerInputHit.Outside,
            systemTextHost: true,
            origin: PointerInvocationOrigin.StartMenuSurface,
            previouslyAuthorized: false,
            userDismissed: false,
            expired: false,
            out _));
        Assert.True(KeyboardInvocationPolicy.TryResolvePointer(
            PointerInputHit.Inside,
            systemTextHost: true,
            origin: PointerInvocationOrigin.StartMenuSurface,
            previouslyAuthorized: false,
            userDismissed: false,
            expired: false,
            out var authorized));
        Assert.True(authorized);
    }

    [Fact]
    public void Non_search_start_menu_click_expires_without_authorization()
    {
        Assert.True(KeyboardInvocationPolicy.TryResolvePointer(
            PointerInputHit.Outside,
            systemTextHost: true,
            origin: PointerInvocationOrigin.StartMenuSurface,
            previouslyAuthorized: false,
            userDismissed: false,
            expired: true,
            out var authorized));
        Assert.False(authorized);
    }

    [Fact]
    public void Unavailable_hit_waits_then_expires_without_spinning()
    {
        Assert.False(KeyboardInvocationPolicy.TryResolvePointer(
            PointerInputHit.Unavailable,
            systemTextHost: false,
            origin: PointerInvocationOrigin.Unknown,
            previouslyAuthorized: true,
            userDismissed: false,
            expired: false,
            out _));
        Assert.True(KeyboardInvocationPolicy.TryResolvePointer(
            PointerInputHit.Unavailable,
            systemTextHost: false,
            origin: PointerInvocationOrigin.Unknown,
            previouslyAuthorized: true,
            userDismissed: false,
            expired: true,
            out var stillAuthorized));
        Assert.False(stillAuthorized);
        Assert.True(KeyboardInvocationPolicy.TryResolvePointer(
            PointerInputHit.Unavailable,
            systemTextHost: false,
            origin: PointerInvocationOrigin.Unknown,
            previouslyAuthorized: false,
            userDismissed: false,
            expired: true,
            out var authorized));
        Assert.False(authorized);
    }

    [Fact]
    public void Touch_invocation_promotes_chromium_document_hits()
    {
        Assert.Equal(
            PointerInputHit.Inside,
            TouchInvocationPolicy.Promote(
                PointerInputHit.Unavailable,
                textIntent: true,
                authorized: false,
                clickInsideAuthorizedField: false));
        Assert.Equal(
            PointerInputHit.Outside,
            TouchInvocationPolicy.Promote(
                PointerInputHit.Unavailable,
                textIntent: true,
                authorized: true,
                clickInsideAuthorizedField: false));
        Assert.Equal(
            PointerInputHit.Inside,
            TouchInvocationPolicy.Promote(
                PointerInputHit.Unavailable,
                textIntent: false,
                authorized: true,
                clickInsideAuthorizedField: true));
        Assert.Equal(
            PointerInputHit.Unavailable,
            TouchInvocationPolicy.Promote(
                PointerInputHit.Unavailable,
                textIntent: false,
                authorized: false,
                clickInsideAuthorizedField: false));
        Assert.False(TouchInvocationPolicy.CountsAsLeaveClick(PointerInputHit.Unavailable));
        Assert.True(TouchInvocationPolicy.CountsAsLeaveClick(PointerInputHit.Outside));
        Assert.True(TouchInvocationPolicy.IsTextIntent(true, false, false));
        Assert.True(TouchInvocationPolicy.IsPromotedTouch(0xFF515700));
        Assert.False(TouchInvocationPolicy.IsPromotedTouch(0));
    }

    [Fact]
    public void Touch_down_dedupes_promoted_mouse_and_hid()
    {
        const long tick = TimeSpan.TicksPerMillisecond;
        Assert.True(PointerIntentTrackingPolicy.IsDuplicateDown(
            10, 10, 12, 11, 50 * tick, 10 * tick));
        Assert.False(PointerIntentTrackingPolicy.IsDuplicateDown(
            10, 10, 12, 11, 80 * tick, 10 * tick));
        Assert.True(PointerIntentTrackingPolicy.IsNewContactBurst(90 * tick, 10 * tick));
        Assert.False(PointerIntentTrackingPolicy.IsNewContactBurst(50 * tick, 10 * tick));
    }

    [Fact]
    public void Document_focus_lost_hides_only_on_unresolved_leave_click()
    {
        // TSF 无效 + 点的不是输入 + UIA 也不是输入：真离开。
        Assert.True(KeyboardInvocationPolicy.ShouldDismissForLostDocument(
            documentFocused: false,
            uiaLooksLikeTextInput: false,
            searchSession: false,
            hasUnresolvedLeaveClick: true));
        // 换框途中 UIA 还在 Edit，不藏。
        Assert.False(KeyboardInvocationPolicy.ShouldDismissForLostDocument(
            documentFocused: false,
            uiaLooksLikeTextInput: true,
            searchSession: false,
            hasUnresolvedLeaveClick: true));
        // SearchHost 交接中的无效上下文不藏。
        Assert.False(KeyboardInvocationPolicy.ShouldDismissForLostDocument(
            documentFocused: false,
            uiaLooksLikeTextInput: false,
            searchSession: true,
            hasUnresolvedLeaveClick: true));
        // 没有这一次离开点击，只是 Chromium 短暂 SetFocus(null)，不藏。
        // 平板也不能豁免：日志里授权后十几毫秒就「文档焦点离开」再立刻再授权。
        Assert.False(KeyboardInvocationPolicy.ShouldDismissForLostDocument(
            documentFocused: false,
            uiaLooksLikeTextInput: false,
            searchSession: false,
            hasUnresolvedLeaveClick: false));
        Assert.False(KeyboardInvocationPolicy.ShouldReauthorizeAfterRevoke(true));
        Assert.True(KeyboardInvocationPolicy.ShouldReauthorizeAfterRevoke(false));
    }

    [Fact]
    public void Click_outside_authorized_field_releases_without_consuming_a_new_input()
    {
        Assert.True(KeyboardInvocationPolicy.ShouldReleaseAuthorizedField(
            authorized: true,
            clickInsideAuthorizedField: false,
            PointerInputHit.Unavailable));
        Assert.True(KeyboardInvocationPolicy.ShouldReleaseAuthorizedField(
            authorized: true,
            clickInsideAuthorizedField: false,
            PointerInputHit.Outside));
        Assert.False(KeyboardInvocationPolicy.ShouldReleaseAuthorizedField(
            authorized: true,
            clickInsideAuthorizedField: false,
            PointerInputHit.Inside));
        Assert.False(KeyboardInvocationPolicy.ShouldReleaseAuthorizedField(
            authorized: true,
            clickInsideAuthorizedField: true,
            PointerInputHit.Unavailable));
        Assert.False(KeyboardInvocationPolicy.ShouldReleaseAuthorizedField(
            authorized: false,
            clickInsideAuthorizedField: false,
            PointerInputHit.Unavailable));
        Assert.True(KeyboardInvocationPolicy.ShouldConsumeLeaveClick(PointerInputHit.Outside));
        Assert.False(KeyboardInvocationPolicy.ShouldConsumeLeaveClick(PointerInputHit.Unavailable));
        Assert.False(KeyboardInvocationPolicy.ShouldConsumeLeaveClick(PointerInputHit.Inside));
        Assert.True(KeyboardInvocationPolicy.ShouldHoldFocusLeftForSearch(
            pendingSearchInvocation: true,
            keyboardAlreadyShown: false));
        Assert.False(KeyboardInvocationPolicy.ShouldHoldFocusLeftForSearch(
            pendingSearchInvocation: true,
            keyboardAlreadyShown: true));
        Assert.False(KeyboardInvocationPolicy.ShouldHoldFocusLeftForSearch(
            pendingSearchInvocation: false,
            keyboardAlreadyShown: false));
    }

    [Fact]
    public void Authorized_field_is_not_replaced_by_stale_focus_caret()
    {
        Assert.True(KeyboardInvocationPolicy.ShouldAdoptIncomingField(
            pendingClick: true,
            incomingFromClickedField: false,
            authorizedFieldId: "",
            incomingFieldId: "search"));
        Assert.False(KeyboardInvocationPolicy.ShouldAdoptIncomingField(
            pendingClick: true,
            incomingFromClickedField: false,
            authorizedFieldId: "dialog",
            incomingFieldId: "composer"));
        Assert.True(KeyboardInvocationPolicy.ShouldAdoptIncomingField(
            pendingClick: true,
            incomingFromClickedField: true,
            authorizedFieldId: "dialog",
            incomingFieldId: "title"));
        Assert.False(KeyboardInvocationPolicy.ShouldAdoptIncomingField(
            pendingClick: false,
            incomingFromClickedField: false,
            authorizedFieldId: "dialog",
            incomingFieldId: "composer"));
        Assert.True(KeyboardInvocationPolicy.ShouldAdoptIncomingField(
            pendingClick: false,
            incomingFromClickedField: false,
            authorizedFieldId: "dialog",
            incomingFieldId: "dialog"));
        Assert.True(KeyboardInvocationPolicy.ShouldAdoptIncomingField(
            pendingClick: false,
            incomingFromClickedField: false,
            authorizedFieldId: "",
            incomingFieldId: "composer"));
    }

    [Fact]
    public void Click_belongs_to_the_field_box_not_the_other_row()
    {
        var title = new NativeRect { Left = 900, Top = 90, Right = 1400, Bottom = 140 };
        var composer = new NativeRect { Left = 900, Top = 920, Right = 1400, Bottom = 980 };
        var titleCaret = new NativeRect { Left = 1112, Top = 106, Right = 1114, Bottom = 132 };
        var composerCaret = new NativeRect { Left = 1016, Top = 931, Right = 1018, Bottom = 957 };

        Assert.True(FieldClickPolicy.Belongs(title, titleCaret, 1110, 110));
        Assert.False(FieldClickPolicy.Belongs(title, titleCaret, 1020, 940));
        Assert.False(FieldClickPolicy.Belongs(composer, composerCaret, 1110, 110));
        Assert.True(FieldClickPolicy.Belongs(composer, composerCaret, 1020, 940));
        // 只有光标、没有外框时，用光标所在行，不能跨到另一行。
        Assert.True(FieldClickPolicy.Belongs(default, composerCaret, 1100, 940));
        Assert.False(FieldClickPolicy.Belongs(default, composerCaret, 1110, 110));
        // 点空白：焦点仍交聊天框，套不住就不能提升成 Inside。
        Assert.False(FieldClickPolicy.Belongs(composer, composerCaret, 400, 400));
        // 点标题框时焦点光标仍是聊天框：不能拿来摆窗。
        Assert.False(FieldClickPolicy.Trusts(
            fromClicked: false, composer, composerCaret, 1110, 110));
        Assert.True(FieldClickPolicy.Trusts(
            fromClicked: true, composer, composerCaret, 1110, 110));
        Assert.True(FieldClickPolicy.Trusts(
            fromClicked: false, title, titleCaret, 1110, 110));
    }

    [Fact]
    public void Collapsed_cursor_title_first_click_opens_nearby_compact_field()
    {
        // 日志 08:35:14：从下面聊天框点到上面折叠框 (1559,173)，
        // 展开后光标在 (1694,201)。严格套框失败，展开认框必须过。
        var openedCaret = new NativeRect { Left = 1694, Top = 201, Right = 1696, Bottom = 227 };
        var composer = new NativeRect { Left = 900, Top = 920, Right = 1400, Bottom = 980 };
        var composerCaret = new NativeRect { Left = 1016, Top = 931, Right = 1018, Bottom = 957 };

        Assert.False(FieldClickPolicy.Belongs(default, openedCaret, 1559, 173));
        Assert.True(FieldClickPolicy.OpenedBy(default, openedCaret, 1559, 173));
        Assert.True(FieldClickPolicy.Trusts(
            fromClicked: false, default, openedCaret, 1559, 173));
        Assert.False(FieldClickPolicy.OpenedBy(composer, composerCaret, 1559, 173));
        Assert.False(FieldClickPolicy.OpenedBy(composer, composerCaret, 1110, 110));
        Assert.True(FieldClickPolicy.OpenedBy(composer, composerCaret, 1020, 940));
    }

    [Fact]
    public void Search_origins_are_a_search_session()
    {
        Assert.True(KeyboardInvocationPolicy.IsSearchInvocation(
            PointerInvocationOrigin.StartMenuSurface));
        Assert.True(KeyboardInvocationPolicy.IsSearchInvocation(
            PointerInvocationOrigin.StartMenuSearch));
        Assert.True(KeyboardInvocationPolicy.IsSearchInvocation(
            PointerInvocationOrigin.TaskbarSearch));
        Assert.False(KeyboardInvocationPolicy.IsSearchInvocation(
            PointerInvocationOrigin.Unknown));
    }

    [Fact]
    public void Explorer_address_bar_chrome_waits_instead_of_treating_breadcrumb_as_leave()
    {
        Assert.Equal(
            PointerInputHit.Unavailable,
            InputInvocationProbe.ClassifyAddressBandChrome(
                inComboBox: false,
                inAddressBand: true));
        Assert.Equal(
            PointerInputHit.Inside,
            InputInvocationProbe.ClassifyAddressBandChrome(
                inComboBox: true,
                inAddressBand: false));
        Assert.Equal(
            PointerInputHit.Outside,
            InputInvocationProbe.ClassifyAddressBandChrome(
                inComboBox: false,
                inAddressBand: false));
        Assert.True(InputInvocationProbe.LooksLikeAddressBand(
            "Address Band Root",
            "addressband",
            height: 36));
        Assert.True(InputInvocationProbe.LooksLikeAddressBand(
            "面包屑",
            "Breadcrumb Parent",
            height: 32));
        Assert.False(InputInvocationProbe.LooksLikeAddressBand(
            "Back",
            "backbutton",
            height: 32));
        Assert.False(InputInvocationProbe.LooksLikeAddressBand(
            "Address Band Root",
            "addressband",
            height: 800));
    }

    [Fact]
    public void Compact_contenteditable_is_an_input_large_page_document_is_not()
    {
        Assert.True(InputInvocationProbe.IsCompactEditable(
            ControlType.Document, width: 280, height: 36, keyboardFocusable: true));
        Assert.False(InputInvocationProbe.IsCompactEditable(
            ControlType.Document, width: 1600, height: 900, keyboardFocusable: true));
        Assert.False(InputInvocationProbe.IsCompactEditable(
            ControlType.Document, width: 280, height: 36, keyboardFocusable: false));
        // 整页容器看不清时不能收成 Outside，否则换框第一次点击会被吃掉。
        Assert.Equal(
            PointerInputHit.Unavailable,
            InputInvocationProbe.ClassifyContainerHit(
                compactEditable: false,
                foundCompactChild: false));
        Assert.Equal(
            PointerInputHit.Inside,
            InputInvocationProbe.ClassifyContainerHit(
                compactEditable: true,
                foundCompactChild: false));
        Assert.Equal(
            PointerInputHit.Inside,
            InputInvocationProbe.ClassifyContainerHit(
                compactEditable: false,
                foundCompactChild: true));
        Assert.Equal(
            PointerInputHit.Inside,
            InputInvocationProbe.ClassifyContainerHit(
                compactEditable: false,
                foundCompactChild: false,
                clickInsideAuthorizedField: true));
        Assert.Equal(
            PointerInputHit.Unavailable,
            InputInvocationProbe.ClassifyMissedFocusedField(PointerInputHit.Unavailable));
        Assert.Equal(
            PointerInputHit.Outside,
            InputInvocationProbe.ClassifyMissedFocusedField(PointerInputHit.Outside));
        Assert.Equal(
            PointerInputHit.Inside,
            InputInvocationProbe.ClassifyMissedFocusedField(PointerInputHit.Inside));
    }

    [Fact]
    public void Search_suggestion_list_item_is_not_leaving_the_input()
    {
        // 侧栏列表/文件树是离开；搜索联想仍由 searchSession 在会话里挡住。
        Assert.True(InputInvocationProbe.SignalsLeftTextInput(ControlType.ListItem));
        Assert.True(InputInvocationProbe.SignalsLeftTextInput(ControlType.TreeItem));
        Assert.True(InputInvocationProbe.SignalsLeftTextInput(ControlType.Button));
        Assert.True(InputInvocationProbe.StopsAtControl(ControlType.ListItem));
        Assert.False(InputInvocationProbe.SignalsLeftTextInput(ControlType.Thumb));
        Assert.False(InputInvocationProbe.SignalsLeftTextInput(ControlType.ScrollBar));
        Assert.True(InputInvocationProbe.StopsAtControl(ControlType.Thumb));
        Assert.True(KeyboardInvocationPolicy.ShouldIgnoreFocusLeft(
            documentFocused: true,
            slateDevice: true,
            selectionChrome: true));
        Assert.False(KeyboardInvocationPolicy.ShouldIgnoreFocusLeft(
            documentFocused: true,
            slateDevice: true,
            selectionChrome: false));
        Assert.False(KeyboardInvocationPolicy.ShouldIgnoreFocusLeft(
            documentFocused: false,
            slateDevice: true,
            selectionChrome: true));
        Assert.False(KeyboardInvocationPolicy.ShouldIgnoreFocusLeft(
            documentFocused: true,
            slateDevice: false,
            selectionChrome: true));
        Assert.True(InputInvocationProbe.IsSelectionChrome(ControlType.Menu));
        Assert.True(InputInvocationProbe.IsSelectionChrome(ControlType.Thumb));
        Assert.False(InputInvocationProbe.IsSelectionChrome(ControlType.Button));
        Assert.False(InputInvocationProbe.IsSelectionChrome(ControlType.ToolBar));
        Assert.True(InputInvocationProbe.SignalsLeftPageSurface(
            ControlType.Document, width: 1600, height: 900, keyboardFocusable: true));
        Assert.False(InputInvocationProbe.SignalsLeftPageSurface(
            ControlType.Document, width: 280, height: 36, keyboardFocusable: true));
        Assert.True(KeyboardInvocationPolicy.ShouldAuthorizeSlateOcclusion(
            slateDevice: true,
            hasInputField: true,
            focusEnteredTextInput: true,
            pointerOnOfficialSip: false,
            pageSurface: true));
        Assert.False(KeyboardInvocationPolicy.ShouldAuthorizeSlateOcclusion(
            slateDevice: true,
            hasInputField: true,
            focusEnteredTextInput: false,
            pointerOnOfficialSip: false,
            pageSurface: true));
    }

    [Fact]
    public void Start_button_consumes_intent_without_authorizing_search()
    {
        Assert.True(KeyboardInvocationPolicy.TryResolvePointer(
            PointerInputHit.Unavailable,
            systemTextHost: true,
            origin: PointerInvocationOrigin.TaskbarStart,
            previouslyAuthorized: true,
            userDismissed: false,
            expired: false,
            out var authorized));
        Assert.False(authorized);
    }

    [Fact]
    public void Cached_shell_hit_map_distinguishes_start_and_search_entries()
    {
        ShellInvocationTarget[] targets =
        [
            new(
                PointerInvocationOrigin.TaskbarStart,
                new NativeRect { Left = 0, Top = 0, Right = 50, Bottom = 50 }),
            new(
                PointerInvocationOrigin.TaskbarSearch,
                new NativeRect { Left = 50, Top = 0, Right = 100, Bottom = 50 }),
            new(
                PointerInvocationOrigin.StartMenuSearch,
                new NativeRect { Left = 0, Top = 50, Right = 100, Bottom = 100 })
        ];

        Assert.Equal(
            PointerInvocationOrigin.TaskbarStart,
            PointerIntentTrackingPolicy.ClassifyShellPoint(targets, 25, 25));
        Assert.Equal(
            PointerInvocationOrigin.TaskbarSearch,
            PointerIntentTrackingPolicy.ClassifyShellPoint(targets, 75, 25));
        Assert.Equal(
            PointerInvocationOrigin.StartMenuSearch,
            PointerIntentTrackingPolicy.ClassifyShellPoint(targets, 75, 75));
        Assert.Equal(
            PointerInvocationOrigin.Unknown,
            PointerIntentTrackingPolicy.ClassifyShellPoint(targets, 125, 25));
        Assert.True(KeyboardInvocationPolicy.ShouldAuthorize(
            pointerInsideFocusedInput: false,
            systemTextHost: true,
            origin: PointerInvocationOrigin.TaskbarSearch));
        Assert.False(KeyboardInvocationPolicy.ShouldAuthorize(
            pointerInsideFocusedInput: false,
            systemTextHost: true,
            origin: PointerInvocationOrigin.TaskbarStart,
            previouslyAuthorized: true,
            userDismissed: false));
    }

    [Fact]
    public void Address_bar_select_all_shows_touch_selection_hides()
    {
        Assert.False(SelectionVisibilityPolicy.ShouldHide(touchSelectionChrome: false));
        Assert.False(SelectionVisibilityPolicy.ShouldHide(
            touchSelectionChrome: true,
            rangeSelected: false));
        Assert.True(SelectionVisibilityPolicy.ShouldHide(
            touchSelectionChrome: true,
            rangeSelected: true));
        Assert.True(SipVisibilityPolicy.ShouldHideAfterTouchSettle(
            touchSettled: true,
            overlayOwnsTouch: false,
            focusedLooksLikeText: false,
            focusEnteredText: false,
            selectionChrome: false));
        Assert.False(SipVisibilityPolicy.ShouldHideAfterTouchSettle(
            touchSettled: true,
            overlayOwnsTouch: false,
            focusedLooksLikeText: false,
            focusEnteredText: false,
            selectionChrome: false,
            nearbyFieldChrome: false,
            searchSession: true));
        Assert.False(SipVisibilityPolicy.ShouldHideAfterTouchSettle(
            touchSettled: true,
            overlayOwnsTouch: false,
            focusedLooksLikeText: true,
            focusEnteredText: false,
            selectionChrome: false));
        Assert.False(SipVisibilityPolicy.ShouldHideAfterTouchSettle(
            touchSettled: true,
            overlayOwnsTouch: true,
            focusedLooksLikeText: false,
            focusEnteredText: false,
            selectionChrome: false));
        Assert.True(SipVisibilityPolicy.ShouldHideAfterTouchSettle(
            touchSettled: true,
            overlayOwnsTouch: false,
            focusedLooksLikeText: false,
            focusEnteredText: false,
            selectionChrome: false,
            nearbyFieldChrome: false));
        Assert.False(SipVisibilityPolicy.ShouldHideAfterTouchSettle(
            touchSettled: true,
            overlayOwnsTouch: false,
            focusedLooksLikeText: false,
            focusEnteredText: false,
            selectionChrome: false,
            nearbyFieldChrome: true));
        var address = new NativeRect { Left = 80, Top = 40, Right = 900, Bottom = 80 };
        var refresh = new NativeRect { Left = 860, Top = 44, Right = 896, Bottom = 76 };
        var sidebar = new NativeRect { Left = 8, Top = 160, Right = 280, Bottom = 800 };
        Assert.True(SipVisibilityPolicy.IsNearbyFieldChrome(address, refresh));
        Assert.False(SipVisibilityPolicy.IsNearbyFieldChrome(address, sidebar));
        Assert.False(TouchFocusLeavePolicy.ShouldRevokeNow(
            focusLeft: true,
            selectionChrome: false,
            looksLikeText: false,
            documentFocused: true,
            touchInvocation: true));
        Assert.True(TouchFocusLeavePolicy.ShouldRevokeNow(
            focusLeft: true,
            selectionChrome: false,
            looksLikeText: false,
            documentFocused: false,
            touchInvocation: true));
        Assert.True(TouchFocusLeavePolicy.ShouldRevokeNow(
            focusLeft: true,
            selectionChrome: false,
            looksLikeText: false,
            documentFocused: true,
            touchInvocation: false));
        Assert.False(TouchFocusLeavePolicy.ShouldRevokeNow(
            focusLeft: true,
            selectionChrome: true,
            looksLikeText: false,
            documentFocused: false,
            touchInvocation: true));
        Assert.True(TouchFocusLeavePolicy.ShouldRevokeForPageTap(
            touchDismissArmed: true,
            pageSurface: true,
            selectionChrome: false,
            looksLikeText: false,
            focusEntered: false));
        Assert.False(TouchFocusLeavePolicy.ShouldRevokeForPageTap(
            touchDismissArmed: true,
            pageSurface: true,
            selectionChrome: false,
            looksLikeText: true,
            focusEntered: false));
        Assert.True(TouchFocusLeavePolicy.ShouldRevokeAfterConfirm(
            confirmExpired: true,
            selectionChrome: false,
            looksLikeText: false,
            focusEntered: false,
            pageSurface: false,
            focusLeft: true,
            touchDismissArmed: false));
        Assert.False(TouchFocusLeavePolicy.ShouldRevokeAfterConfirm(
            confirmExpired: true,
            selectionChrome: false,
            looksLikeText: true,
            focusEntered: false,
            pageSurface: true,
            focusLeft: true,
            touchDismissArmed: true));
    }

    [Fact]
    public void Touch_focus_shows_without_matching_a_stale_cursor()
    {
        Assert.True(TouchInvocationPolicy.ShouldShowForTouchFocus(
            touchInvocation: true,
            documentHasCaret: true,
            focusEntered: false,
            focusedLooksLikeText: false,
            editTap: true));
        Assert.True(TouchInvocationPolicy.ShouldShowForTouchFocus(
            touchInvocation: true,
            documentHasCaret: false,
            focusEntered: true,
            focusedLooksLikeText: false,
            editTap: true));
        Assert.False(TouchInvocationPolicy.ShouldShowForTouchFocus(
            touchInvocation: true,
            documentHasCaret: true,
            focusEntered: true,
            focusedLooksLikeText: true,
            editTap: false));
        Assert.False(TouchInvocationPolicy.ShouldShowForTouchFocus(
            touchInvocation: false,
            documentHasCaret: true,
            focusEntered: true,
            focusedLooksLikeText: true,
            editTap: true));
        Assert.Equal(EditTouch.KindOnLeave, EditTouchPolicy.Classify(onText: false, onLeave: true));
        Assert.Equal(EditTouch.KindOnEdit, EditTouchPolicy.Classify(onText: true, onLeave: false));
        Assert.False(EditTouchPolicy.AllowsShow(
            EditTouch.KindOnLeave,
            nowLooksLikeText: true,
            ageMs: 80));
        Assert.True(EditTouchPolicy.AllowsShow(
            EditTouch.KindOnEdit,
            nowLooksLikeText: true,
            ageMs: 80));
        Assert.True(EditTouchPolicy.AllowsShow(
            EditTouch.KindPending,
            nowLooksLikeText: true,
            ageMs: 80));
        Assert.False(EditTouchPolicy.AllowsShow(
            EditTouch.KindPending,
            nowLooksLikeText: true,
            ageMs: 400));
        Assert.True(InputInvocationProbe.IsHardLeaveControl(
            System.Windows.Automation.ControlType.TreeItem));
        Assert.False(InputInvocationProbe.IsHardLeaveControl(
            System.Windows.Automation.ControlType.Button));
        Assert.True(SipFocusTrackingPolicy.ShouldIgnoreClickGeometry(
            sipTouchGesture: true,
            overlayOwnsTouch: false,
            hasScreenPoint: false));
        Assert.True(SipFocusTrackingPolicy.ShouldIgnoreClickGeometry(
            sipTouchGesture: true,
            overlayOwnsTouch: true,
            hasScreenPoint: false));
        Assert.False(SipFocusTrackingPolicy.ShouldIgnoreClickGeometry(
            sipTouchGesture: true,
            overlayOwnsTouch: false,
            hasScreenPoint: true));
        Assert.True(SipFocusTrackingPolicy.ShouldRepositionForTouchInvoke(
            sipTouchGesture: true,
            overlayOwnsTouch: false));
        Assert.False(SipFocusTrackingPolicy.ShouldRepositionForTouchInvoke(
            sipTouchGesture: true,
            overlayOwnsTouch: true));
        Assert.False(SipFocusTrackingPolicy.ShouldRepositionForTouchInvoke(
            sipTouchGesture: false,
            overlayOwnsTouch: false));
        Assert.True(SipFocusTrackingPolicy.ShouldHoldHideForFocusQuery(
            authorized: false,
            gestureRecent: true,
            focusedText: true));
        Assert.False(SipFocusTrackingPolicy.ShouldHoldHideForFocusQuery(
            authorized: false,
            gestureRecent: true,
            focusedText: false));
        Assert.True(SipFocusTrackingPolicy.OwnsCurrentContact(
            liveOverOverlay: true,
            overlayContactFresh: false));
        Assert.True(SipFocusTrackingPolicy.OwnsCurrentContact(
            liveOverOverlay: false,
            overlayContactFresh: true));
        Assert.False(SipFocusTrackingPolicy.OwnsCurrentContact(
            liveOverOverlay: false,
            overlayContactFresh: false));
        Assert.True(SipVisibilityPolicy.ShouldHideAfterTouchSettle(
            touchSettled: true,
            overlayOwnsTouch: false,
            focusedLooksLikeText: false,
            focusEnteredText: false,
            selectionChrome: false));
        Assert.False(SipVisibilityPolicy.ShouldHideAfterTouchSettle(
            touchSettled: true,
            overlayOwnsTouch: true,
            focusedLooksLikeText: false,
            focusEnteredText: false,
            selectionChrome: false));
        Assert.Equal(
            EditTouch.KindPending,
            EditTouchPolicy.Classify(onText: false, onLeave: false));
        Assert.True(FieldClickPolicy.Trusts(
            fromClicked: false,
            default,
            new NativeRect { Left = 681, Top = 1044, Right = 683, Bottom = 1076 },
            987,
            362,
            hasScreenPoint: false,
            touchInvocation: true));
        Assert.False(FieldClickPolicy.Trusts(
            fromClicked: false,
            default,
            new NativeRect { Left = 681, Top = 1044, Right = 683, Bottom = 1076 },
            987,
            362,
            hasScreenPoint: true,
            touchInvocation: true));
        Assert.True(TouchDevicePolicy.PreferTouchInvocation(
            hasTouchScreen: true,
            slateMode: false,
            recentTouch: true));
        Assert.False(TouchDevicePolicy.PreferTouchInvocation(
            hasTouchScreen: true,
            slateMode: false,
            recentTouch: false));
    }

    [Fact]
    public void Slate_focus_can_open_keyboard_without_a_promoted_mouse_click()
    {
        Assert.True(KeyboardInvocationPolicy.ShouldAuthorizeSlateFocus(
            slateDevice: true,
            focusedLooksLikeTextInput: true));
        Assert.False(KeyboardInvocationPolicy.ShouldAuthorizeSlateFocus(
            slateDevice: false,
            focusedLooksLikeTextInput: true));
        Assert.False(KeyboardInvocationPolicy.ShouldAuthorizeSlateFocus(
            slateDevice: true,
            focusedLooksLikeTextInput: false));
        Assert.True(KeyboardInvocationPolicy.ShouldAwaitSlateFocus(
            searchSession: false,
            slateDevice: true,
            focusEnteredTextInput: true));
        Assert.False(KeyboardInvocationPolicy.ShouldAwaitSlateFocus(
            searchSession: false,
            slateDevice: false,
            focusEnteredTextInput: true));
        Assert.True(KeyboardInvocationPolicy.ShouldAwaitSlateFocus(
            searchSession: true,
            slateDevice: false,
            focusEnteredTextInput: false));
        Assert.True(KeyboardInvocationPolicy.ShouldAuthorizeSlateOcclusion(
            slateDevice: true,
            hasInputField: true,
            focusEnteredTextInput: false,
            pointerOnOfficialSip: true));
        Assert.True(KeyboardInvocationPolicy.ShouldAuthorizeSlateOcclusion(
            slateDevice: true,
            hasInputField: true,
            focusEnteredTextInput: true,
            pointerOnOfficialSip: false));
        Assert.False(KeyboardInvocationPolicy.ShouldAuthorizeSlateOcclusion(
            slateDevice: true,
            hasInputField: true,
            focusEnteredTextInput: false,
            pointerOnOfficialSip: false));
        Assert.False(KeyboardInvocationPolicy.ShouldAuthorizeSlateOcclusion(
            slateDevice: false,
            hasInputField: true,
            focusEnteredTextInput: true,
            pointerOnOfficialSip: true));
    }

    [Fact]
    public void Global_pointer_hook_runs_only_for_an_active_foreground_t9_profile()
    {
        Assert.False(PointerIntentTrackingPolicy.ShouldEnable(
            canCommitForeground: true,
            hasForegroundProfileLease: false,
            hasObservedActiveProfile: false));
        Assert.False(PointerIntentTrackingPolicy.ShouldEnable(
            canCommitForeground: false,
            hasForegroundProfileLease: true,
            hasObservedActiveProfile: false));
        Assert.True(PointerIntentTrackingPolicy.ShouldEnable(
            canCommitForeground: false,
            hasForegroundProfileLease: false,
            hasObservedActiveProfile: true));
        Assert.False(PointerIntentTrackingPolicy.ShouldEnable(
            canCommitForeground: false,
            hasForegroundProfileLease: false,
            hasObservedActiveProfile: false));
        Assert.True(PointerIntentTrackingPolicy.ShouldEnableForSession(
            canCommitForeground: false,
            hasForegroundProfileLease: false,
            hasSystemProfileLease: false,
            officialT9Selected: true));
        Assert.False(PointerIntentTrackingPolicy.ShouldEnableForSession(
            canCommitForeground: true,
            hasForegroundProfileLease: true,
            hasSystemProfileLease: true,
            officialT9Selected: false));
    }

    [Fact]
    public void Hosted_keyboard_click_is_not_treated_as_external_input_intent()
    {
        Assert.True(PointerIntentTrackingPolicy.IsKeyboardWindow("T9Ime.BandHost"));
        Assert.False(PointerIntentTrackingPolicy.IsKeyboardWindow(
            "ApplicationFrameWindow"));
        Assert.True(PointerIntentTrackingPolicy.IsOverlayContact(
            keyboardWindow: true,
            hostPointerLive: false));
        Assert.True(PointerIntentTrackingPolicy.IsOverlayContact(
            keyboardWindow: false,
            hostPointerLive: true));
        Assert.False(PointerIntentTrackingPolicy.IsOverlayContact(
            keyboardWindow: false,
            hostPointerLive: false));
        Assert.True(PointerIntentTrackingPolicy.IsSipWindow(
            "HwndWrapper[T9Pane;;1]",
            windowPid: 42,
            ourPid: 42));
        Assert.False(PointerIntentTrackingPolicy.IsSipWindow(
            "ApplicationFrameWindow",
            windowPid: 99,
            ourPid: 42));
        Assert.Equal(0x15u, TouchInvocationPolicy.PromotedPointerId(0xFF515715));
        Assert.Equal(0u, TouchInvocationPolicy.PromotedPointerId(0));
    }

    [Fact]
    public void Stable_host_does_not_republish_an_unchanged_full_frame()
    {
        Assert.False(HostFrame.NeedsRepublish(
            sameHost: true,
            sameContext: true,
            hostReady: true));
        Assert.True(HostFrame.NeedsRepublish(
            sameHost: false,
            sameContext: true,
            hostReady: true));
        Assert.True(HostFrame.NeedsRepublish(
            sameHost: true,
            sameContext: true,
            hostReady: false));
        Assert.True(HostFrame.ShouldParkLocalWindow(hosting: true, hostReady: true));
        Assert.False(HostFrame.ShouldParkLocalWindow(hosting: true, hostReady: false));
        Assert.False(HostFrame.ShouldParkLocalWindow(hosting: false, hostReady: true));
    }

    [Fact]
    public void Hidden_host_window_falls_back_to_the_design_size_for_capture()
    {
        var hidden = HostFrame.ContentSize(0, 0, 620, 360);
        Assert.Equal(620, hidden.Width);
        Assert.Equal(360, hidden.Height);
        var collapsed = HostFrame.ContentSize(double.NaN, double.NaN, 620, 360);
        Assert.Equal(620, collapsed.Width);
        Assert.Equal(360, collapsed.Height);
        var visible = HostFrame.ContentSize(930, 540, 620, 360);
        Assert.Equal(930, visible.Width);
        Assert.Equal(540, visible.Height);
    }

    [Fact]
    public void Frame_buffer_is_reused_only_while_the_pixel_size_is_unchanged()
    {
        Assert.True(HostFrame.CanReuseBuffer(
            cachedWidth: 930,
            cachedHeight: 540,
            hasBuffer: true,
            width: 930,
            height: 540));
        Assert.False(HostFrame.CanReuseBuffer(
            cachedWidth: 930,
            cachedHeight: 540,
            hasBuffer: false,
            width: 930,
            height: 540));
        Assert.False(HostFrame.CanReuseBuffer(
            cachedWidth: 930,
            cachedHeight: 540,
            hasBuffer: true,
            width: 620,
            height: 360));
    }

    [Fact]
    public void Keys_without_a_distinct_long_press_fire_on_press()
    {
        // 退格、字母、数字：长按等于轻点，且不是滑动起点，可以按下即触发。
        Assert.True(KeyTapTimingPolicy.IsImmediate(
            hasDistinctLongPress: false,
            gestureRegion: false));

        // 九宫格键长按是多击字母，必须留在抬起触发。
        Assert.False(KeyTapTimingPolicy.IsImmediate(
            hasDistinctLongPress: true,
            gestureRegion: false));

        // 候选侧栏和符号盘是滑动翻页的起点。
        Assert.False(KeyTapTimingPolicy.IsImmediate(
            hasDistinctLongPress: false,
            gestureRegion: true));
    }

    [Fact]
    public void Focus_event_burst_is_collapsed_into_one_pass_plus_a_trailing_pass()
    {
        var gate = new TrailingEdgeGate();

        Assert.True(gate.TryEnter());
        // 处理期间连来的事件不再各自排队，但也不能被丢掉。
        Assert.False(gate.TryEnter());
        Assert.False(gate.TryEnter());

        // 期间来过事件，必须补跑一轮，让最后一条事件携带的新坐标被读到。
        Assert.True(gate.ShouldRerun());
        Assert.False(gate.ShouldRerun());
    }

    [Fact]
    public void Single_focus_event_does_not_schedule_a_trailing_pass()
    {
        var gate = new TrailingEdgeGate();

        Assert.True(gate.TryEnter());
        Assert.False(gate.ShouldRerun());

        // 收尾后回到空闲，下一条事件重新安排处理。
        Assert.True(gate.TryEnter());
    }

    [Fact]
    public void Focus_event_arriving_during_the_trailing_pass_is_not_dropped()
    {
        var gate = new TrailingEdgeGate();

        Assert.True(gate.TryEnter());
        Assert.False(gate.TryEnter());
        Assert.True(gate.ShouldRerun());

        // 补跑期间又来一条，仍然要再补一轮，否则最后那条又丢了。
        Assert.False(gate.TryEnter());
        Assert.True(gate.ShouldRerun());
        Assert.False(gate.ShouldRerun());
    }

    [Fact]
    public void Trusted_caret_shows_immediately_on_system_surfaces()
    {
        var caret = new NativeRect { Left = 111, Top = 87, Right = 113, Bottom = 111 };
        var surface = new IntPtr(0x1234);

        // SearchHost 这类 XAML 表面从不提供原生 TSF 字段。坐标已经描述真实插入点时
        // 不该再等——原来恒等于"要等"，表现就是开始菜单搜索框要多点一下。
        Assert.False(InputFieldSelectionPolicy.NeedsAuthoritativeFirstShow(
            systemTextHost: true,
            hasUiField: true,
            new InputField(surface, caret, default, CaretIsTrusted: true),
            hasNativeField: false,
            default));

        // 按窗口矩形推算出来的坐标仍然要等更权威的值。
        Assert.True(InputFieldSelectionPolicy.NeedsAuthoritativeFirstShow(
            systemTextHost: true,
            hasUiField: true,
            new InputField(surface, caret, default, CaretIsTrusted: false),
            hasNativeField: false,
            default));

        // 桌面 uia/box 同样要等插入点，否则会按外框顶边摆到输入行上。
        Assert.True(InputFieldSelectionPolicy.NeedsAuthoritativeFirstShow(
            systemTextHost: false,
            hasUiField: true,
            new InputField(surface, caret, default, CaretIsTrusted: false),
            hasNativeField: false,
            default));
    }

    [Fact]
    public void Clicking_a_text_field_is_recognised_from_the_pointer_target()
    {
        // 落点就是输入框：这是最常见的一次点击，必须立刻判成命中，
        // 否则用户要再点一下才弹。
        Assert.True(InputInvocationProbe.IsTextField(ControlType.Edit));
        Assert.True(InputInvocationProbe.IsTextField(ControlType.ComboBox));

        // 容器不算输入框本身，但可以继续往上找。
        Assert.False(InputInvocationProbe.IsTextField(ControlType.Group));
        Assert.False(InputInvocationProbe.IsTextField(ControlType.Text));
    }

    [Fact]
    public void Clicking_a_control_or_the_page_root_is_not_an_input_intent()
    {
        // 点按钮就是点按钮，不能因为它的祖先里有可输入容器就弹键盘。
        Assert.True(InputInvocationProbe.StopsAtControl(ControlType.Button));
        Assert.True(InputInvocationProbe.StopsAtControl(ControlType.TabItem));
        Assert.True(InputInvocationProbe.StopsAtControl(ControlType.ListItem));
        Assert.True(InputInvocationProbe.StopsAtControl(ControlType.Menu));
        Assert.False(InputInvocationProbe.StopsAtControl(ControlType.Edit));

        // Chromium 把整页暴露成覆盖全窗口的 Document，走到它就必须停，
        // 否则窗口里任何一次点击都会被判成命中。
        Assert.True(InputInvocationProbe.StopsAtContainer(ControlType.Document));
        Assert.True(InputInvocationProbe.StopsAtContainer(ControlType.Pane));
        Assert.True(InputInvocationProbe.StopsAtContainer(ControlType.Window));

        // 中间的包装节点不能停，否则找不到外面那层输入框。
        Assert.False(InputInvocationProbe.StopsAtContainer(ControlType.Group));
        Assert.False(InputInvocationProbe.StopsAtContainer(ControlType.Custom));
        Assert.False(InputInvocationProbe.StopsAtContainer(ControlType.Text));
    }

    [Fact]
    public void Degraded_caret_sample_reuses_the_last_reliable_one()
    {
        var now = DateTime.UtcNow.Ticks;
        var hold = CaretQualityGate.Hold;

        // 外框兜底(1)不能覆盖刚拿到的真实光标(3)，否则键盘会跳到框顶压住输入行。
        Assert.True(CaretQualityGate.PrefersCached(
            CaretQualityGate.Rank("uia/text"),
            CaretQualityGate.Rank("uia/box"),
            sameField: true,
            now - TimeSpan.FromMilliseconds(120).Ticks,
            now,
            hold));

        // 好样本回来时必须立刻采用，不能被旧值挡住。
        Assert.False(CaretQualityGate.PrefersCached(
            CaretQualityGate.Rank("uia/box"),
            CaretQualityGate.Rank("uia/text"),
            sameField: true,
            now - TimeSpan.FromMilliseconds(120).Ticks,
            now,
            hold));

        // 同等质量按新值走，光标本来就会随打字移动。
        Assert.False(CaretQualityGate.PrefersCached(
            CaretQualityGate.Rank("caret"),
            CaretQualityGate.Rank("uia/text"),
            sameField: true,
            now - TimeSpan.FromMilliseconds(10).Ticks,
            now,
            hold));

        // 用户这一下点中的框比任意元素外框可靠，不能被外框顶掉。
        Assert.True(CaretQualityGate.PrefersCached(
            CaretQualityGate.Rank("clicked"),
            CaretQualityGate.Rank("uia/box"),
            sameField: true,
            now - TimeSpan.FromMilliseconds(120).Ticks,
            now,
            hold));

        // 但真实插入点仍然优先于它。
        Assert.False(CaretQualityGate.PrefersCached(
            CaretQualityGate.Rank("clicked"),
            CaretQualityGate.Rank("uia/text"),
            sameField: true,
            now - TimeSpan.FromMilliseconds(120).Ticks,
            now,
            hold));
    }

    [Fact]
    public void Caret_sample_hold_does_not_outlive_the_window_or_cross_surfaces()
    {
        var now = DateTime.UtcNow.Ticks;
        var hold = CaretQualityGate.Hold;

        // 超时后宁可用差坐标，也不能长期停在旧位置。
        Assert.False(CaretQualityGate.PrefersCached(
            CaretQualityGate.Rank("uia/text"),
            CaretQualityGate.Rank("uia/box"),
            sameField: true,
            now - TimeSpan.FromMilliseconds(1500).Ticks,
            now,
            hold));

        // 换了框，旧坐标毫无意义。
        Assert.False(CaretQualityGate.PrefersCached(
            CaretQualityGate.Rank("uia/text"),
            CaretQualityGate.Rank("uia/box"),
            sameField: false,
            now - TimeSpan.FromMilliseconds(120).Ticks,
            now,
            hold));
    }

    [Fact]
    public void Focus_sitting_in_a_text_box_is_not_by_itself_an_input_intent()
    {
        // 切换会话/切到前台会把焦点自动放进输入框。此时点别处不能算输入意图，
        // 否则 Unigram 切群组时点群组列表也会弹出键盘。
        // 落点是肯定判据，焦点只用来确定"落点属于哪个框"。
        Assert.False(KeyboardInvocationPolicy.ShouldAuthorize(
            pointerInsideFocusedInput: false,
            systemTextHost: false,
            origin: PointerInvocationOrigin.Unknown,
            previouslyAuthorized: true,
            userDismissed: false));

        // 真点在框上才授权。
        Assert.True(KeyboardInvocationPolicy.ShouldAuthorize(
            pointerInsideFocusedInput: true,
            systemTextHost: false,
            origin: PointerInvocationOrigin.Unknown,
            previouslyAuthorized: false,
            userDismissed: false));
    }

    [Fact]
    public void Two_search_boxes_sharing_one_window_are_not_the_same_field()
    {
        // 开始菜单搜索框与任务栏搜索框共用同一个 SearchHost 顶层窗口。
        // 只按窗口判定"同一个框"时，互切的那一下会被当成"同一个框坐标变差了"，
        // 于是沿用上一个框的坐标——键盘停在上一个点击位置。必须比到元素身份。
        var surface = new IntPtr(0x4321);
        var gate = new CaretQualityGate();

        var startMenuBox = "42.1.7";
        var taskbarBox = "42.1.9";

        // 开始菜单搜索框，真实插入点。
        var caret = new NativeRect { Left = 111, Top = 87, Right = 113, Bottom = 111 };
        var source = "uia/text";
        Assert.False(gate.Apply(surface, startMenuBox, ref caret, ref source));

        // 切到任务栏搜索框，第一拍只拿到外框(质量更低)。窗口没变，但框变了，
        // 所以不能沿用——沿用就等于停在 y=87 那个开始菜单的位置上。
        var switched = new NativeRect { Left = 118, Top = 1037, Right = 120, Bottom = 1060 };
        var switchedSource = "uia/box";
        Assert.False(gate.Apply(surface, taskbarBox, ref switched, ref switchedSource));
        Assert.Equal(1037, switched.Top);

        // 同一个框内质量下降，仍然要沿用，这是闸门本来的用途。
        var degraded = new NativeRect { Left = 118, Top = 1200, Right = 120, Bottom = 1240 };
        var degradedSource = "searchbox";
        Assert.True(gate.Apply(surface, taskbarBox, ref degraded, ref degradedSource));
        Assert.Equal(1037, degraded.Top);
    }

    [Fact]
    public void Search_text_and_box_on_the_same_chrome_keep_the_insertion_point()
    {
        var surface = new IntPtr(0x4321);
        var gate = new CaretQualityGate();
        var caret = new NativeRect { Left = 134, Top = 107, Right = 136, Bottom = 131 };
        var source = "uia/text";
        Assert.False(gate.Apply(surface, "search/text", ref caret, ref source));

        var box = new NativeRect { Left = 72, Top = 100, Right = 420, Bottom = 148 };
        var boxSource = "uia/box";
        Assert.True(gate.Apply(surface, "search/box", ref box, ref boxSource));
        Assert.Equal(134, box.Left);
        Assert.Equal(107, box.Top);
        Assert.Equal("uia/text", boxSource);

        Assert.True(CaretQualityGate.IsSameSearchChrome(
            sameSurface: true,
            CaretQualityGate.Rank("uia/text"),
            CaretQualityGate.Rank("uia/box"),
            new NativeRect { Left = 134, Top = 107, Right = 136, Bottom = 131 },
            new NativeRect { Left = 72, Top = 100, Right = 420, Bottom = 148 }));
        Assert.False(CaretQualityGate.IsSameSearchChrome(
            sameSurface: true,
            CaretQualityGate.Rank("uia/text"),
            CaretQualityGate.Rank("uia/box"),
            new NativeRect { Left = 111, Top = 87, Right = 113, Bottom = 111 },
            new NativeRect { Left = 118, Top = 1037, Right = 120, Bottom = 1060 }));
    }

    [Fact]
    public void Switching_field_on_one_surface_never_reuses_the_other_box_position()
    {
        // 回归锁：两个系统搜索框的真实位置相差近千像素(y=87 与 y=1037)。
        // 只要闸门把它们当成同一个框，切换那一下就会把键盘留在上一个框上。
        var surface = new IntPtr(0x4321);
        var gate = new CaretQualityGate();

        var caret = new NativeRect { Left = 111, Top = 87, Right = 113, Bottom = 111 };
        var source = "uia/text";
        gate.Apply(surface, "42.1.7", ref caret, ref source);

        // 换框后哪怕样本质量更低，也必须采用新坐标。
        foreach (var degraded in new[] { "uia/box", "clicked" })
        {
            var fresh = new NativeRect { Left = 118, Top = 1037, Right = 120, Bottom = 1060 };
            var freshSource = degraded;
            var held = gate.Apply(surface, "42.1.9", ref fresh, ref freshSource);

            Assert.False(held);
            Assert.Equal(1037, fresh.Top);
            Assert.Equal(degraded, freshSource);
        }
    }

    [Fact]
    public void Focus_snapshot_overrides_focused_element_only_inside_the_lag_window()
    {
        var now = DateTime.UtcNow.Ticks;
        var window = TimeSpan.FromMilliseconds(500);

        Assert.True(FocusedFieldCache.IsFresh(
            now - TimeSpan.FromMilliseconds(100).Ticks,
            now,
            window));
        Assert.False(FocusedFieldCache.IsFresh(
            now - TimeSpan.FromMilliseconds(900).Ticks,
            now,
            window));
        Assert.False(FocusedFieldCache.IsFresh(0, now, window));
    }

    [Fact]
    public void Host_press_that_already_typed_swallows_the_matching_release()
    {
        var gate = new HostPressGate();

        gate.NotePressHandled();
        Assert.True(gate.PressHandled);
        Assert.True(gate.ConsumeRelease());

        // 取用一次后即复位，抬起只能吃掉一次。
        Assert.False(gate.ConsumeRelease());
    }

    [Fact]
    public void Host_press_on_a_deferred_key_lets_the_release_do_the_work()
    {
        var gate = new HostPressGate();

        gate.Reset();
        Assert.False(gate.ConsumeRelease());
    }

}

