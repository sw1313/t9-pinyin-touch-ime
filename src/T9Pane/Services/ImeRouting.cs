namespace T9Pane.Services;

internal static class ImeRouting
{
    public static bool IsUsable(ImeClient client) =>
        client.Pid != 0 || client.Hwnd != IntPtr.Zero;

    public static bool TryPick(
        IReadOnlyList<ImeClient> clients,
        uint foregroundPid,
        bool brokeredForeground,
        Func<uint, bool> isBrokerProcess,
        out ImeClient? client)
    {
        client = null;
        if (clients.Count == 0)
        {
            return false;
        }

        var exact = clients.FirstOrDefault(c =>
            c.ProfileActive && HasDirectFocus(c) && IsUsable(c)
            && PidOf(c) == foregroundPid && foregroundPid != 0);
        if (exact is not null)
        {
            client = exact;
            return true;
        }

        if (brokeredForeground)
        {
            var shell = clients
                .Where(c =>
                c.ProfileActive && HasDocumentLease(c)
                && (c.ContextSequence == 0 || c.ThreadFocused)
                && IsUsable(c) && isBrokerProcess(PidOf(c)))
                .OrderByDescending(c => c.ObservationOrder)
                .FirstOrDefault();
            if (shell is null)
            {
                return false;
            }

            client = shell;
            return true;
        }

        return false;
    }

    public static bool TryPickContextView(
        IReadOnlyList<ImeClient> clients,
        Func<IntPtr, bool> belongsToForegroundSurface,
        out ImeClient? client)
        => TryPickContextViewByScore(
            clients,
            view => belongsToForegroundSurface(view) ? 1 : 0,
            out client);

    public static bool TryPickContextViewByScore(
        IReadOnlyList<ImeClient> clients,
        Func<IntPtr, int> foregroundSurfaceScore,
        out ImeClient? client)
    {
        client = clients
            .Where(candidate =>
                candidate.ContextSequence > 0
                && candidate.ProfileActive
                && candidate.DocumentFocused
                && candidate.ViewHwnd != IntPtr.Zero
                && IsUsable(candidate))
            .Select(candidate => new
            {
                Client = candidate,
                Score = foregroundSurfaceScore(candidate.ViewHwnd)
            })
            .Where(candidate => candidate.Score > 0)
            .OrderByDescending(candidate => candidate.Score)
            .ThenByDescending(candidate => candidate.Client.ObservationOrder)
            .Select(candidate => candidate.Client)
            .FirstOrDefault();
        return client is not null;
    }

    public static bool TryPickHostClient(
        IReadOnlyList<ImeClient> clients,
        ulong preferredHwnd,
        out ImeClient? client)
    {
        client = clients
            .Where(candidate =>
                preferredHwnd != 0
                && candidate.ProfileActive
                && IsUsable(candidate)
                && unchecked((ulong)candidate.Hwnd.ToInt64()) == preferredHwnd)
            .OrderByDescending(candidate => candidate.ObservationOrder)
            .FirstOrDefault();
        return client is not null;
    }

    public static bool TryPickVisibleCommandTarget(
        IReadOnlyList<ImeClient> clients,
        ulong visibleHostHwnd,
        uint foregroundPid,
        Func<IntPtr, bool> belongsToForegroundSurface,
        out ImeClient? client)
    {
        client = null;
        if (visibleHostHwnd == 0)
        {
            return false;
        }

        client = clients
            .Where(candidate =>
                IsUsable(candidate)
                && unchecked((ulong)candidate.Hwnd.ToInt64()) == visibleHostHwnd
                && (PidOf(candidate) == foregroundPid
                    || (candidate.ViewHwnd != IntPtr.Zero
                        && belongsToForegroundSurface(candidate.ViewHwnd))))
            .OrderByDescending(candidate => candidate.ObservationOrder)
            .FirstOrDefault();
        return client is not null;
    }

    public static bool TryPickSurfaceCommandTarget(
        IReadOnlyList<ImeClient> clients,
        uint foregroundPid,
        Func<IntPtr, bool> belongsToForegroundSurface,
        out ImeClient? client)
    {
        client = clients
            .Where(candidate =>
                candidate.ProfileActive
                && candidate.ContextSequence > 0
                && IsUsable(candidate)
                && (PidOf(candidate) == foregroundPid
                    || (candidate.ViewHwnd != IntPtr.Zero
                        && belongsToForegroundSurface(candidate.ViewHwnd))))
            .OrderByDescending(candidate => candidate.ObservationOrder)
            .FirstOrDefault();
        return client is not null;
    }

    public static bool HasDirectFocus(ImeClient client) =>
        client.Focused || client.DocumentFocused || client.ThreadFocused;

    public static bool HasDocumentLease(ImeClient client) =>
        client.DocumentFocused;

    public static uint PidOf(ImeClient client)
    {
        if (client.Pid != 0)
        {
            return client.Pid;
        }

        if (client.Hwnd == IntPtr.Zero)
        {
            return 0;
        }

        Native.NativeMethods.GetWindowThreadProcessId(client.Hwnd, out var pid);
        return pid;
    }
}

internal static class HostResponsePolicy
{
    public static bool IsCurrent(ulong responseClient, ulong visibleClient) =>
        visibleClient != 0
        && (responseClient == 0 || responseClient == visibleClient);
}

internal static class ImeActivationPolicy
{
    public static bool IsFocusedNotification(string json) =>
        !json.Contains("\"focus\":0", StringComparison.Ordinal);
}

internal static class StateSequencePolicy
{
    public static bool ShouldApply(uint incoming, uint current) =>
        incoming == 0 || incoming > current;
}

/// <summary>
/// 官方 GetTextExt 在手柄弹出、选区动画时会返回 TS_E_NOLAYOUT（layoutState=2）。
/// 那只表示这一拍还没排完版，不是“输入框没了”。清掉上次光标会让键盘弹不出或被藏。
/// </summary>
internal static class NativeCaretPolicy
{
    public static Native.NativeRect Apply(
        Native.NativeRect current,
        Native.NativeRect incoming,
        int layoutState,
        bool documentActive,
        bool epochAdvanced)
    {
        if (!documentActive)
        {
            return default;
        }

        if (epochAdvanced)
        {
            return layoutState == 1 ? incoming : default;
        }

        return layoutState == 1 ? incoming : current;
    }
}

internal static class ImeClientState
{
    public static bool ApplyDocumentFocus(ImeClient client, bool focused, uint sequence)
    {
        if (!StateSequencePolicy.ShouldApply(sequence, client.DocumentSequence))
        {
            return false;
        }

        client.DocumentFocused = focused;
        client.DocumentSequence = sequence;
        return true;
    }

    public static bool ApplyExclusiveDocumentFocus(
        IReadOnlyList<ImeClient> clients,
        ImeClient selected,
        bool focused,
        uint sequence,
        uint processId)
    {
        if (!ApplyDocumentFocus(selected, focused, sequence))
        {
            return false;
        }

        if (focused && processId != 0)
        {
            foreach (var other in clients.Where(client =>
                         client != selected && ImeRouting.PidOf(client) == processId))
            {
                other.DocumentFocused = false;
            }
        }
        return true;
    }

    public static bool ApplyThreadFocus(ImeClient client, bool focused, uint sequence)
    {
        if (!StateSequencePolicy.ShouldApply(sequence, client.ThreadSequence))
        {
            return false;
        }

        client.ThreadFocused = focused;
        client.ThreadSequence = sequence;
        return true;
    }

    public static bool ApplyExclusiveThreadFocus(
        IReadOnlyList<ImeClient> clients,
        ImeClient selected,
        bool focused,
        uint sequence,
        uint processId)
    {
        if (!ApplyThreadFocus(selected, focused, sequence))
        {
            return false;
        }

        if (focused && processId != 0)
        {
            foreach (var other in clients.Where(client =>
                         client != selected && ImeRouting.PidOf(client) == processId))
            {
                other.ThreadFocused = false;
            }
        }
        return true;
    }

    public static bool ApplyProfile(ImeClient client, bool active, uint sequence)
    {
        if (!StateSequencePolicy.ShouldApply(sequence, client.ProfileSequence))
        {
            return false;
        }

        client.ProfileActive = active;
        client.ProfileSequence = sequence;
        return true;
    }

    public static bool ApplyProfileToProcess(
        IReadOnlyList<ImeClient> clients,
        uint processId,
        bool active,
        uint sequence)
    {
        if (processId == 0)
        {
            return false;
        }

        var applied = false;
        foreach (var client in clients.Where(candidate => ImeRouting.PidOf(candidate) == processId))
        {
            if (ApplyProfile(client, active, sequence))
            {
                applied = true;
            }
        }

        return applied;
    }

    public static bool ApplyContext(
        ImeClient client,
        bool active,
        bool profileActive,
        bool threadFocused,
        uint sequence,
        uint epoch,
        int layoutState,
        Native.NativeRect caret,
        Native.NativeRect screen,
        IntPtr viewHwnd,
        bool hasRangeSelection = false)
    {
        if (client.ContextEpoch != 0 && epoch < client.ContextEpoch)
        {
            return false;
        }
        if (epoch == client.ContextEpoch
            && !StateSequencePolicy.ShouldApply(sequence, client.ContextSequence))
        {
            return false;
        }

        var epochAdvanced = client.ContextEpoch != 0 && epoch > client.ContextEpoch;
        client.ContextSequence = sequence;
        client.ContextEpoch = epoch;
        client.DocumentFocused = active;
        client.ThreadFocused = threadFocused;
        client.ProfileActive = profileActive;
        client.DocumentSequence = Math.Max(client.DocumentSequence, sequence);
        client.ThreadSequence = Math.Max(client.ThreadSequence, sequence);
        client.ProfileSequence = Math.Max(client.ProfileSequence, sequence);
        client.LayoutState = layoutState;
        client.HasRangeSelection = active && hasRangeSelection;
        client.NativeCaret = NativeCaretPolicy.Apply(
            client.NativeCaret,
            caret,
            layoutState,
            active,
            epochAdvanced);
        client.NativeScreen = screen;
        client.ViewHwnd = viewHwnd;
        return true;
    }
}
