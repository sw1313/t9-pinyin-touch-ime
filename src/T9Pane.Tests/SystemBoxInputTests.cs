using T9Pane.Services;

namespace T9Pane.Tests;

public class SystemBoxInputTests
{
    [Fact]
    public void Insert_at_end_places_caret_after_inserted_chinese_text()
    {
        var plan = SystemTextEditPlan.AtSelection("Windows", "搜索", "");

        Assert.Equal("Windows搜索", plan.Value);
        Assert.Equal(plan.Value.Length, plan.CaretOffset);
    }

    [Fact]
    public void Insert_replaces_selection_and_places_caret_before_suffix()
    {
        var plan = SystemTextEditPlan.AtSelection("开始", "菜单", "搜索");

        Assert.Equal("开始菜单搜索", plan.Value);
        Assert.Equal("开始菜单".Length, plan.CaretOffset);
    }

    [Fact]
    public void Touch_backspace_deletes_before_caret_and_keeps_suffix()
    {
        var plan = SystemTextEditPlan.Backspace("搜索框", "", "内容");

        Assert.Equal("搜索内容", plan.Value);
        Assert.Equal("搜索".Length, plan.CaretOffset);
    }

    [Fact]
    public void Touch_backspace_deletes_selected_text()
    {
        var plan = SystemTextEditPlan.Backspace("开始", "菜单", "搜索");

        Assert.Equal("开始搜索", plan.Value);
        Assert.Equal("开始".Length, plan.CaretOffset);
    }

    [Fact]
    public void Caret_move_is_skipped_when_the_edit_lands_at_the_end()
    {
        // 搜索框里追加和退格都落在末尾，这时省掉搬光标的几次跨进程调用。
        var append = SystemTextEditPlan.AtSelection("Windows", "搜索", "");
        Assert.False(SystemBoxInput.NeedsCaretMove(
            append.CaretOffset,
            append.Value.Length));

        var trailingBackspace = SystemTextEditPlan.Backspace("搜索框", "", "");
        Assert.False(SystemBoxInput.NeedsCaretMove(
            trailingBackspace.CaretOffset,
            trailingBackspace.Value.Length));

        // 光标停在中间时仍然必须搬，否则下一个字符会跑到末尾。
        var midBackspace = SystemTextEditPlan.Backspace("搜索框", "", "内容");
        Assert.True(SystemBoxInput.NeedsCaretMove(
            midBackspace.CaretOffset,
            midBackspace.Value.Length));
    }

    [Fact]
    public void Uia_fallback_is_allowed_only_for_t9_system_surface_without_native_context()
    {
        Assert.True(SystemFallbackPolicy.ShouldUse(
            systemTextSurface: true,
            hasProfileLease: true,
            nativeContextActive: false));
        Assert.False(SystemFallbackPolicy.ShouldUse(
            systemTextSurface: false,
            hasProfileLease: true,
            nativeContextActive: false));
        Assert.False(SystemFallbackPolicy.ShouldUse(
            systemTextSurface: true,
            hasProfileLease: false,
            nativeContextActive: false));
        Assert.False(SystemFallbackPolicy.ShouldUse(
            systemTextSurface: true,
            hasProfileLease: true,
            nativeContextActive: true));
    }

    [Fact]
    public void Repeated_uwp_backspace_does_not_depend_on_native_context_acknowledgement()
    {
        Assert.True(SystemBackspacePolicy.ShouldUseUia(
            foregroundSystemTextSurface: true,
            hasProfileLease: true,
            hasCapturedSystemTarget: false));
        Assert.True(SystemBackspacePolicy.ShouldUseUia(
            foregroundSystemTextSurface: false,
            hasProfileLease: false,
            hasCapturedSystemTarget: true));
        Assert.False(SystemBackspacePolicy.ShouldUseUia(
            foregroundSystemTextSurface: true,
            hasProfileLease: false,
            hasCapturedSystemTarget: false));
        Assert.False(SystemBackspacePolicy.ShouldUseUia(
            foregroundSystemTextSurface: false,
            hasProfileLease: true,
            hasCapturedSystemTarget: false));
    }

    [Fact]
    public void Taskbar_search_accepts_related_searchhost_focus_during_handoff()
    {
        Assert.True(AutomationSurfacePolicy.AcceptsFocusedProcess(
            topPid: 10,
            focusedPid: 20,
            allowSystemBroker: true,
            focusedProcessIsBroker: true,
            intersectsTop: false,
            intersectsSearch: true));
        Assert.False(AutomationSurfacePolicy.AcceptsFocusedProcess(
            topPid: 10,
            focusedPid: 30,
            allowSystemBroker: true,
            focusedProcessIsBroker: false,
            intersectsTop: false,
            intersectsSearch: true));
    }
}
