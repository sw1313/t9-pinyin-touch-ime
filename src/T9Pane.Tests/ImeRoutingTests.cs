using T9Pane.Native;
using T9Pane.Services;

namespace T9Pane.Tests;

public class ImeRoutingTests
{
    [Fact]
    public void Zero_hwnd_and_pid_is_not_usable()
    {
        Assert.False(ImeRouting.IsUsable(new ImeClient()));
    }

    [Fact]
    public void Flyout_does_not_pick_empty_or_unrelated_client()
    {
        var clients = new List<ImeClient>
        {
            new(),
            new() { Pid = 50, Hwnd = new IntPtr(1) }
        };

        var ok = ImeRouting.TryPick(clients, 999, brokeredForeground: true, pid => pid is 123, out var picked);

        Assert.False(ok);
        Assert.Null(picked);
    }

    [Fact]
    public void Ordinary_window_does_not_pick_client_from_another_process()
    {
        var clients = new List<ImeClient>
        {
            new() { Pid = 123, Hwnd = new IntPtr(1), Focused = true }
        };

        var ok = ImeRouting.TryPick(clients, 999, brokeredForeground: false, _ => false, out var picked);

        Assert.False(ok);
        Assert.Null(picked);
    }

    [Fact]
    public void Flyout_picks_searchhost_client_even_if_foreground_pid_differs()
    {
        var clients = new List<ImeClient>
        {
            new() { Pid = 50, Hwnd = new IntPtr(1) },
            new() { Pid = 123, Hwnd = new IntPtr(2), DocumentFocused = true }
        };

        var ok = ImeRouting.TryPick(clients, 888, brokeredForeground: true, pid => pid == 123, out var picked);

        Assert.True(ok);
        Assert.Equal(123u, picked!.Pid);
    }

    [Fact]
    public void Foreground_pid_wins()
    {
        var clients = new List<ImeClient>
        {
            new() { Pid = 123, Hwnd = new IntPtr(2) },
            new() { Pid = 888, Hwnd = new IntPtr(3), Focused = true }
        };

        var ok = ImeRouting.TryPick(clients, 888, brokeredForeground: true, pid => pid == 123, out var picked);

        Assert.True(ok);
        Assert.Equal(888u, picked!.Pid);
    }

    [Fact]
    public void Visible_keyboard_command_returns_to_rendering_client_during_transient_focus_loss()
    {
        var visibleHwnd = new IntPtr(22);
        var visibleView = new IntPtr(220);
        var clients = new List<ImeClient>
        {
            new()
            {
                Pid = 123,
                Hwnd = visibleHwnd,
                ViewHwnd = visibleView,
                DocumentFocused = false,
                ThreadFocused = false,
                ObservationOrder = 10
            }
        };

        var ok = ImeRouting.TryPickVisibleCommandTarget(
            clients,
            unchecked((ulong)visibleHwnd.ToInt64()),
            foregroundPid: 999,
            view => view == visibleView,
            out var picked);

        Assert.True(ok);
        Assert.Same(clients[0], picked);
    }

    [Fact]
    public void Visible_keyboard_command_never_targets_an_unrelated_surface()
    {
        var clients = new List<ImeClient>
        {
            new()
            {
                Pid = 123,
                Hwnd = new IntPtr(22),
                ViewHwnd = new IntPtr(220),
                ObservationOrder = 10
            }
        };

        Assert.False(ImeRouting.TryPickVisibleCommandTarget(
            clients,
            visibleHostHwnd: 22,
            foregroundPid: 999,
            _ => false,
            out _));
    }

    [Fact]
    public void Desktop_command_uses_latest_context_on_same_surface_during_focus_gap()
    {
        var view = new IntPtr(330);
        var client = new ImeClient
        {
            Pid = 333,
            Hwnd = new IntPtr(33),
            ViewHwnd = view,
            ContextSequence = 4,
            DocumentFocused = false,
            ThreadFocused = false,
            ObservationOrder = 20
        };

        Assert.True(ImeRouting.TryPickSurfaceCommandTarget(
            [client],
            foregroundPid: 333,
            candidateView => candidateView == view,
            out var picked));
        Assert.Same(client, picked);
    }

    [Fact]
    public void Focused_instance_wins_when_process_has_multiple_tsf_instances()
    {
        var clients = new List<ImeClient>
        {
            new() { Pid = 123, Hwnd = new IntPtr(1) },
            new() { Pid = 123, Hwnd = new IntPtr(2), Focused = true }
        };

        var ok = ImeRouting.TryPick(clients, 123, brokeredForeground: true, _ => true, out var picked);

        Assert.True(ok);
        Assert.Equal(new IntPtr(2), picked!.Hwnd);
    }

    [Fact]
    public void Unfocused_t9_instance_cannot_survive_microsoft_pinyin_switch()
    {
        var clients = new List<ImeClient>
        {
            new() { Pid = 123, Hwnd = new IntPtr(1), Focused = false }
        };

        Assert.False(ImeRouting.TryPick(
            clients,
            foregroundPid: 123,
            brokeredForeground: false,
            _ => false,
            out _));
    }

    [Fact]
    public void Broker_never_reuses_client_without_current_document_lease()
    {
        var clients = new List<ImeClient>
        {
            new() { Pid = 123, Hwnd = new IntPtr(1), Focused = false }
        };

        Assert.False(ImeRouting.TryPick(
            clients, 999, true, pid => pid == 123, out _));
        clients[0].DocumentFocused = true;
        Assert.True(ImeRouting.TryPick(
            clients, 999, true, pid => pid == 123, out var picked));
        Assert.Equal(new IntPtr(1), picked!.Hwnd);
    }

    [Fact]
    public void Reconnected_client_state_preserves_focus_and_old_client_compatibility()
    {
        Assert.True(ImeActivationPolicy.IsFocusedNotification(
            """{"t":"on","hwnd":1,"pid":2,"focus":1}"""));
        Assert.False(ImeActivationPolicy.IsFocusedNotification(
            """{"t":"on","hwnd":1,"pid":2,"focus":0}"""));
        Assert.True(ImeActivationPolicy.IsFocusedNotification(
            """{"t":"on","hwnd":1,"pid":2}"""));
    }

    [Theory]
    [InlineData("searchhost")]
    [InlineData("startmenuexperiencehost")]
    [InlineData("explorer")]
    public void Windows_shell_tsf_hosts_are_valid_flyout_clients(string processName)
    {
        Assert.True(ImeHost.IsFlyoutHostProcessName(processName));
    }

    [Theory]
    [InlineData("applicationframehost")]
    [InlineData("textinputhost")]
    [InlineData("systemsettings")]
    [InlineData("winstore.app")]
    [InlineData("explorer")]
    [InlineData("searchhost")]
    public void Uwp_and_shell_brokers_are_valid_system_text_clients(string processName)
    {
        Assert.True(ShellProcess.IsSystemTextClientName(processName));
    }

    [Fact]
    public void Start_menu_field_is_accepted_when_it_only_matches_the_foreground_window()
    {
        // top 已重定向到 SearchHost，但输入框仍在 StartMenuExperienceHost 的树里，
        // 换页过程中它既不落在 SearchHost 矩形里也不落在搜索窗口矩形里。
        Assert.True(AutomationSurfacePolicy.AcceptsFocusedProcess(
            topPid: 100,
            focusedPid: 200,
            allowSystemBroker: true,
            focusedProcessIsBroker: true,
            intersectsTop: false,
            intersectsSearch: false,
            intersectsForeground: true));
    }

    [Fact]
    public void Unrelated_process_field_is_still_rejected_without_any_geometry_match()
    {
        Assert.False(AutomationSurfacePolicy.AcceptsFocusedProcess(
            topPid: 100,
            focusedPid: 200,
            allowSystemBroker: true,
            focusedProcessIsBroker: true,
            intersectsTop: false,
            intersectsSearch: false,
            intersectsForeground: false));

        // 非 broker 进程即使几何相交也不接受。
        Assert.False(AutomationSurfacePolicy.AcceptsFocusedProcess(
            topPid: 100,
            focusedPid: 200,
            allowSystemBroker: true,
            focusedProcessIsBroker: false,
            intersectsTop: true,
            intersectsSearch: true,
            intersectsForeground: true));

        // UWP 输入框在应用进程里，顶层却是 ApplicationFrameHost。
        Assert.True(AutomationSurfacePolicy.AcceptsFocusedProcess(
            topPid: 100,
            focusedPid: 200,
            allowSystemBroker: false,
            focusedProcessIsBroker: false,
            intersectsTop: true,
            intersectsSearch: false,
            intersectsForeground: false,
            applicationFrame: true));
        Assert.False(AutomationSurfacePolicy.AcceptsFocusedProcess(
            topPid: 100,
            focusedPid: 200,
            allowSystemBroker: false,
            focusedProcessIsBroker: false,
            intersectsTop: false,
            intersectsSearch: false,
            intersectsForeground: false,
            applicationFrame: true));
    }

    [Theory]
    [InlineData("explorer")]
    [InlineData("startmenuexperiencehost")]
    public void Shell_hosts_that_defer_text_input_redirect_to_search_host(string processName)
    {
        Assert.True(ShellProcess.HandsOffToSearchHost(processName));
    }

    [Theory]
    [InlineData("searchhost")]
    [InlineData("searchapp")]
    [InlineData("notepad")]
    [InlineData("shellexperiencehost")]
    public void Hosts_that_own_their_text_field_are_not_redirected(string processName)
    {
        Assert.False(ShellProcess.HandsOffToSearchHost(processName));
    }

    [Fact]
    public void Start_menu_to_search_host_is_the_same_search_session()
    {
        Assert.True(ShellProcess.IsSearchSessionSurfaceName(
            "startmenuexperiencehost", trayChrome: false));
        Assert.True(ShellProcess.IsSearchSessionSurfaceName(
            "searchhost", trayChrome: false));
        Assert.True(ShellProcess.IsSearchSessionSurfaceName(
            "explorer", trayChrome: true));
        Assert.False(ShellProcess.IsSearchSessionSurfaceName(
            "explorer", trayChrome: false));
        Assert.False(ShellProcess.IsSearchSessionSurfaceName(
            "cursor", trayChrome: false));
        Assert.True(ShellProcess.IsSearchHandoffName(
            "startmenuexperiencehost",
            fromTray: false,
            "searchhost",
            toTray: false));
        Assert.True(ShellProcess.IsSearchHandoffName(
            "explorer",
            fromTray: true,
            "searchhost",
            toTray: false));
        Assert.False(ShellProcess.IsSearchHandoffName(
            "cursor",
            fromTray: false,
            "startmenuexperiencehost",
            toTray: false));
        Assert.False(ShellProcess.IsSearchHandoffName(
            "searchhost",
            fromTray: false,
            "cursor",
            toTray: false));
    }

    [Fact]
    public void Packaged_apps_use_native_tsf_instead_of_process_specific_uia()
    {
        Assert.False(ShellProcess.IsSystemTextSurfaceName(
            processName: "notepad",
            systemFlyout: false,
            trayChrome: false));
        Assert.False(ShellProcess.IsSystemTextSurfaceName(
            processName: "winstore.app",
            systemFlyout: false,
            trayChrome: false));
        Assert.False(ShellProcess.IsSystemTextSurfaceName(
            processName: "systemsettings",
            systemFlyout: false,
            trayChrome: false));
        Assert.False(ShellProcess.IsSystemTextSurfaceName(
            processName: "applicationframehost",
            systemFlyout: false,
            trayChrome: false));
        Assert.False(ShellProcess.IsSystemTextSurfaceName(
            processName: "textinputhost",
            systemFlyout: false,
            trayChrome: false));
    }

    [Fact]
    public void Packaged_app_can_route_to_application_frame_host()
    {
        var clients = new List<ImeClient>
        {
            new()
            {
                Pid = 33728,
                Hwnd = new IntPtr(7),
                DocumentFocused = true,
                ThreadFocused = false
            }
        };

        Assert.True(ImeRouting.TryPick(
            clients,
            foregroundPid: 29368,
            brokeredForeground: true,
            pid => pid == 33728,
            out var picked));
        Assert.Equal(33728u, picked!.Pid);
    }

    [Fact]
    public void Immersive_view_routes_by_surface_without_application_whitelist()
    {
        var view = new IntPtr(91);
        var client = new ImeClient
        {
            Pid = 33728,
            Hwnd = new IntPtr(7),
            ViewHwnd = view,
            ContextSequence = 4,
            ProfileActive = true,
            DocumentFocused = true
        };

        Assert.True(ImeRouting.TryPickContextView(
            [client],
            candidateView => candidateView == view,
            out var picked));
        Assert.Same(client, picked);
        Assert.True(ShellProcess.IsApplicationFrameClass("ApplicationFrameWindow"));
        Assert.True(ShellProcess.SubstantiallyOverlaps(
            new NativeRect { Left = 100, Top = 100, Right = 1100, Bottom = 800 },
            new NativeRect { Left = 110, Top = 140, Right = 1090, Bottom = 790 }));
        Assert.False(ShellProcess.SubstantiallyOverlaps(
            new NativeRect { Left = 100, Top = 100, Right = 1100, Bottom = 800 },
            new NativeRect { Left = 1400, Top = 100, Right = 1900, Bottom = 800 }));
    }

    [Fact]
    public void Ime_message_window_profile_off_revokes_every_client_in_that_process()
    {
        var editor = new ImeClient
        {
            Pid = 33728,
            Hwnd = new IntPtr(7),
            ProfileActive = true,
            ProfileSequence = 3
        };
        var otherProcess = new ImeClient
        {
            Pid = 99,
            Hwnd = new IntPtr(8),
            ProfileActive = true,
            ProfileSequence = 3
        };

        Assert.True(ImeClientState.ApplyProfileToProcess(
            [editor, otherProcess],
            processId: 33728,
            active: false,
            sequence: 10));
        Assert.False(editor.ProfileActive);
        Assert.True(otherProcess.ProfileActive);
        Assert.False(ImeRouting.TryPick(
            [editor],
            foregroundPid: 33728,
            brokeredForeground: false,
            _ => false,
            out _));
    }

    [Fact]
    public void Broker_client_marked_as_other_profile_cannot_show_t9()
    {
        var clients = new List<ImeClient>
        {
            new()
            {
                Pid = 33728,
                Hwnd = new IntPtr(7),
                Focused = true,
                ProfileActive = false
            }
        };

        Assert.False(ImeRouting.TryPick(
            clients,
            foregroundPid: 29368,
            brokeredForeground: true,
            pid => pid == 33728,
            out _));
    }

    [Fact]
    public void Losing_broker_thread_focus_does_not_revoke_document_lease()
    {
        var client = new ImeClient
        {
            Pid = 33728,
            Hwnd = new IntPtr(7),
            DocumentFocused = true,
            ThreadFocused = false
        };

        Assert.True(ImeRouting.HasDocumentLease(client));
        Assert.True(ImeRouting.TryPick(
            [client],
            foregroundPid: 29368,
            brokeredForeground: true,
            pid => pid == 33728,
            out _));
    }

    [Fact]
    public void State_messages_cannot_move_backwards()
    {
        Assert.True(StateSequencePolicy.ShouldApply(incoming: 7, current: 6));
        Assert.False(StateSequencePolicy.ShouldApply(incoming: 6, current: 6));
        Assert.False(StateSequencePolicy.ShouldApply(incoming: 5, current: 6));
        Assert.True(StateSequencePolicy.ShouldApply(incoming: 0, current: 6));
    }

    [Fact]
    public void Out_of_order_focus_events_cannot_resurrect_or_hide_a_newer_document()
    {
        var client = new ImeClient { ProfileActive = true };

        Assert.True(ImeClientState.ApplyDocumentFocus(client, focused: true, sequence: 10));
        Assert.False(ImeClientState.ApplyDocumentFocus(client, focused: false, sequence: 9));
        Assert.True(client.DocumentFocused);

        Assert.True(ImeClientState.ApplyDocumentFocus(client, focused: false, sequence: 11));
        Assert.False(ImeClientState.ApplyDocumentFocus(client, focused: true, sequence: 10));
        Assert.False(client.DocumentFocused);
    }

    [Fact]
    public void Inactive_start_menu_profile_cannot_revoke_active_search_surface()
    {
        var startMenu = new ImeClient
        {
            Pid = 10,
            Hwnd = new IntPtr(1),
            ViewHwnd = new IntPtr(101),
            ContextSequence = 8,
            ProfileActive = true,
            DocumentFocused = true,
            ThreadFocused = true
        };
        var search = new ImeClient
        {
            Pid = 20,
            Hwnd = new IntPtr(2),
            ViewHwnd = new IntPtr(202),
            ContextSequence = 9,
            ProfileActive = true,
            DocumentFocused = true,
            ThreadFocused = true
        };

        Assert.True(ImeClientState.ApplyProfile(startMenu, active: false, sequence: 12));
        Assert.True(search.ProfileActive);
        Assert.True(ImeRouting.TryPickContextView(
            [startMenu, search],
            view => view == new IntPtr(202),
            out var picked));
        Assert.Same(search, picked);
    }

    [Fact]
    public void Word_context_gap_keeps_visible_keyboard_only_briefly_on_same_host()
    {
        Assert.True(DesktopContextGracePolicy.ShouldBridge(
            overlayVisible: true,
            sameForegroundHost: true,
            profileActive: true,
            elapsed: TimeSpan.FromMilliseconds(120)));
        Assert.False(DesktopContextGracePolicy.ShouldBridge(
            overlayVisible: true,
            sameForegroundHost: false,
            profileActive: true,
            elapsed: TimeSpan.FromMilliseconds(120)));
        Assert.False(DesktopContextGracePolicy.ShouldBridge(
            overlayVisible: true,
            sameForegroundHost: true,
            profileActive: true,
            elapsed: TimeSpan.FromMilliseconds(800)));
    }

    [Fact]
    public void New_context_epoch_invalidates_old_caret_and_rejects_late_packets()
    {
        var client = new ImeClient { Pid = 42, Hwnd = new IntPtr(7) };
        var caret = new NativeRect { Left = 100, Top = 200, Right = 102, Bottom = 224 };

        Assert.True(ImeClientState.ApplyContext(
            client, true, true, true, 10, 3, 1, caret, default, IntPtr.Zero));
        Assert.Equal(3u, client.ContextEpoch);
        Assert.Equal(caret, client.NativeCaret);

        Assert.True(ImeClientState.ApplyContext(
            client, false, true, false, 11, 4, 0, default, default, IntPtr.Zero));
        Assert.Equal(4u, client.ContextEpoch);
        Assert.True(client.NativeCaret.IsEmpty);
        Assert.False(client.DocumentFocused);

        Assert.False(ImeClientState.ApplyContext(
            client, true, true, true, 10, 3, 1, caret, default, IntPtr.Zero));
        Assert.False(client.DocumentFocused);
        Assert.True(client.NativeCaret.IsEmpty);

        Assert.False(ImeClientState.ApplyContext(
            client, true, true, true, 99, 3, 1, caret, default, IntPtr.Zero));
        Assert.False(client.DocumentFocused);
        Assert.True(client.NativeCaret.IsEmpty);
    }

    [Fact]
    public void Layout_pending_keeps_authoritative_uwp_context_active()
    {
        var client = new ImeClient { Pid = 42, Hwnd = new IntPtr(7) };
        var viewport = new NativeRect { Left = 10, Top = 20, Right = 1010, Bottom = 720 };

        Assert.True(ImeClientState.ApplyContext(
            client, true, true, true, 5, 2, 2, default, viewport, IntPtr.Zero));
        Assert.True(ImeRouting.HasDocumentLease(client));
        Assert.True(client.NativeCaret.IsEmpty);
        Assert.Equal(viewport, client.NativeScreen);
    }

    [Fact]
    public void Native_context_cannot_hijack_an_unrelated_desktop_foreground()
    {
        var client = new ImeClient
        {
            Pid = 42,
            Hwnd = new IntPtr(7),
            ContextSequence = 8,
            ProfileActive = true,
            DocumentFocused = true,
            ThreadFocused = true,
            ObservationOrder = 20
        };

        Assert.False(ImeRouting.TryPick(
            [client],
            foregroundPid: 999,
            brokeredForeground: false,
            _ => false,
            out var picked));
        Assert.Null(picked);
    }

    [Fact]
    public void Native_broker_context_cannot_show_after_thread_focus_is_lost()
    {
        var client = new ImeClient
        {
            Pid = 42,
            Hwnd = new IntPtr(7),
            ContextSequence = 8,
            ProfileActive = true,
            DocumentFocused = true,
            ThreadFocused = false
        };

        Assert.False(ImeRouting.TryPick(
            [client],
            foregroundPid: 999,
            brokeredForeground: true,
            pid => pid == 42,
            out _));
    }

    [Fact]
    public void Independent_search_uses_its_matching_context_view_not_start_menu()
    {
        var startMenu = new ImeClient
        {
            Pid = 10,
            Hwnd = new IntPtr(1),
            ViewHwnd = new IntPtr(101),
            ContextSequence = 8,
            ProfileActive = true,
            DocumentFocused = true,
            ThreadFocused = true,
            ObservationOrder = 30
        };
        var independentSearch = new ImeClient
        {
            Pid = 20,
            Hwnd = new IntPtr(2),
            ViewHwnd = new IntPtr(202),
            ContextSequence = 5,
            ProfileActive = true,
            DocumentFocused = true,
            ThreadFocused = true,
            ObservationOrder = 20
        };

        Assert.True(ImeRouting.TryPickContextView(
            [startMenu, independentSearch],
            view => view == new IntPtr(202),
            out var picked));
        Assert.Same(independentSearch, picked);
    }

    [Fact]
    public void Exact_search_root_wins_even_when_old_surface_reports_later()
    {
        var startMenu = new ImeClient
        {
            Hwnd = new IntPtr(1),
            ViewHwnd = new IntPtr(101),
            ContextSequence = 9,
            ProfileActive = true,
            DocumentFocused = true,
            ObservationOrder = 50
        };
        var independentSearch = new ImeClient
        {
            Hwnd = new IntPtr(2),
            ViewHwnd = new IntPtr(202),
            ContextSequence = 8,
            ProfileActive = true,
            DocumentFocused = true,
            ObservationOrder = 40
        };

        Assert.True(ImeRouting.TryPickContextViewByScore(
            [startMenu, independentSearch],
            view => view == independentSearch.ViewHwnd ? 400 : 200,
            out var picked));
        Assert.Same(independentSearch, picked);
    }

    [Fact]
    public void Same_searchhost_process_without_overlap_is_not_same_surface()
    {
        Assert.Equal(0, SurfaceRelationPolicy.Score(
            sameRoot: false,
            sameRootOwner: false,
            sameProcess: true,
            applicationFrame: false,
            viewVisible: true,
            substantiallyOverlaps: false));
        Assert.Equal(400, SurfaceRelationPolicy.Score(
            sameRoot: true,
            sameRootOwner: false,
            sameProcess: true,
            applicationFrame: false,
            viewVisible: true,
            substantiallyOverlaps: false));
    }

    [Fact]
    public void Independent_search_view_survives_transient_thread_focus_loss()
    {
        var search = new ImeClient
        {
            Pid = 20,
            Hwnd = new IntPtr(2),
            ViewHwnd = new IntPtr(202),
            ContextSequence = 5,
            ProfileActive = true,
            DocumentFocused = true,
            ThreadFocused = false
        };

        Assert.True(ImeRouting.TryPickContextView(
            [search],
            view => view == new IntPtr(202),
            out var picked));
        Assert.Same(search, picked);
    }

    [Fact]
    public void Host_frame_stays_on_context_client_during_search_surface_handoff()
    {
        var requested = new ImeClient
        {
            Pid = 20,
            Hwnd = new IntPtr(2),
            ProfileActive = true,
            ObservationOrder = 10
        };
        var newerUnrelated = new ImeClient
        {
            Pid = 21,
            Hwnd = new IntPtr(3),
            ProfileActive = true,
            ObservationOrder = 100
        };

        Assert.True(ImeRouting.TryPickHostClient(
            [newerUnrelated, requested],
            preferredHwnd: 2,
            out var picked));
        Assert.Same(requested, picked);
    }

    [Fact]
    public void Late_focus_packet_cannot_clear_newer_context_state()
    {
        var selected = new ImeClient
        {
            Pid = 42,
            Hwnd = new IntPtr(7),
            DocumentFocused = true,
            DocumentSequence = 10
        };
        var peer = new ImeClient
        {
            Pid = 42,
            Hwnd = new IntPtr(8),
            DocumentFocused = true,
            DocumentSequence = 11
        };

        Assert.False(ImeClientState.ApplyExclusiveDocumentFocus(
            [selected, peer], selected, true, sequence: 5, processId: 42));
        Assert.True(selected.DocumentFocused);
        Assert.True(peer.DocumentFocused);
    }

    [Fact]
    public void Malformed_deactivate_cannot_clear_live_clients()
    {
        Assert.True(ImeHost.ShouldIgnoreDeactivate(0, 0));
        Assert.False(ImeHost.ShouldIgnoreDeactivate(123, 0));
        Assert.False(ImeHost.ShouldIgnoreDeactivate(0, 456));
    }

    [Fact]
    public void Stale_host_response_cannot_hide_current_band_window()
    {
        Assert.False(HostResponsePolicy.IsCurrent(responseClient: 11, visibleClient: 22));
        Assert.False(HostResponsePolicy.IsCurrent(responseClient: 11, visibleClient: 0));
        Assert.True(HostResponsePolicy.IsCurrent(responseClient: 22, visibleClient: 22));
    }
}
