using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using T9Pane.Native;

namespace T9Pane.Services;

internal sealed class ImeClient
{
    public IntPtr Hwnd { get; set; }
    public uint Pid { get; set; }
    public bool Focused { get; set; }
    public bool DocumentFocused { get; set; }
    public bool ThreadFocused { get; set; }
    public bool ProfileActive { get; set; } = true;
    public uint DocumentSequence { get; set; }
    public uint ThreadSequence { get; set; }
    public uint ProfileSequence { get; set; }
    public uint ContextSequence { get; set; }
    public uint ContextEpoch { get; set; }
    public int LayoutState { get; set; }
    public NativeRect NativeCaret { get; set; }
    public NativeRect NativeScreen { get; set; }
    public IntPtr ViewHwnd { get; set; }
    public uint ActiveFlags { get; set; }
    public bool Immersive { get; set; }
    public bool UiElementOnly { get; set; }
    public long ObservationOrder { get; set; }
}

internal sealed class ImeHost : IDisposable
{
    public static ImeHost Shared { get; } = new();

    public const int KindCompose = 1;
    public const int KindCommit = 2;
    public const int KindCancel = 3;
    public const int KindBackspace = 4;
    public const int KindLift = 5;
    public const int KindFrame = 6;
    public const int KindQueryState = 7;
    public const int KindReturn = 8;
    public const int MaxPacketBytes = 16 * 1024 * 1024;

    private readonly System.Collections.Concurrent.BlockingCollection<string> _notifications =
        new(new System.Collections.Concurrent.ConcurrentQueue<string>(), 2048);

    private readonly byte[] _frameHeader = new byte[16];
    private readonly object _gate = new();
    private readonly List<ImeClient> _clients = [];
    private readonly ImeCommandChannels _cmdPipes = new();
    private ulong _visibleHostHwnd;
    private long _observationSequence;
    private CancellationTokenSource? _cts;

    public event Action? Changed;
    public event Action<int, int>? HostPress;
    public event Action<int, int>? HostHit;
    public event Action<int, int, int, int>? HostSwipe;
    public event Action<int, int>? HostMoved;
    public event Action<bool>? HostVisibilityChanged;
    public bool HasDocumentFocus { get; private set; } = true;

    private void RaiseChanged() => Changed?.Invoke();

    public bool HasClient
    {
        get
        {
            lock (_gate)
            {
                Prune();
                return _clients.Count > 0;
            }
        }
    }

    public bool OwnsForeground()
    {
        var fg = NativeMethods.GetForegroundWindow();
        if (fg == IntPtr.Zero || ShellProcess.IsTrayChrome(fg))
        {
            return false;
        }

        NativeMethods.GetWindowThreadProcessId(fg, out var pid);
        lock (_gate)
        {
            Prune();
            return _clients.Any(c =>
                PidOf(c) == pid
                || (c.ViewHwnd != IntPtr.Zero
                    && ShellProcess.BelongsToForegroundSurface(
                        fg,
                        c.ViewHwnd)));
        }
    }

    public void Start()
    {
        if (_cts is not null)
        {
            return;
        }

        _cts = new CancellationTokenSource();
        DiscoverExistingClients();
        var token = _cts.Token;

        // 每个管道名只挂一条监听循环：并发监听会让通知的处理顺序偏离到达顺序，
        // 而 TSF 状态是靠序号逐条推进的，乱序会丢掉“焦点离开”这类关键通知。
        // 延迟收益改由“读完立刻重新挂起、消息交队列处理”获得，顺序保持不变。
        Task.Run(() => ProcessNotifications(token), token);
        Task.Run(() => Listen(token, PipeAcl.NotificationPipe), token);
        Task.Run(() => Listen(token, PipeAcl.AppContainerNotificationPipe), token);
        Task.Run(() => ListenCmd(token, PipeAcl.CommandPipe), token);
        Task.Run(() => ListenCmd(token, PipeAcl.AppContainerCommandPipe), token);
    }

    private void ProcessNotifications(CancellationToken token)
    {
        try
        {
            foreach (var json in _notifications.GetConsumingEnumerable(token))
            {
                try
                {
                    Handle(json);
                }
                catch (Exception ex)
                {
                    Log.Warn($"IME 通知处理: {ex.Message}");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 退出中
        }
        catch (Exception ex)
        {
            Log.Warn($"IME 通知队列: {ex.Message}");
        }
    }

    private void DiscoverExistingClients()
    {
        var after = IntPtr.Zero;
        var discovered = 0;
        while (true)
        {
            var hwnd = NativeMethods.FindWindowEx(
                NativeMethods.HwndMessage,
                after,
                "T9Ime.Msg",
                null);
            if (hwnd == IntPtr.Zero)
            {
                break;
            }

            after = hwnd;
            NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == 0)
            {
                continue;
            }

            lock (_gate)
            {
                if (_clients.All(client => client.Hwnd != hwnd))
                {
                    _clients.Add(new ImeClient
                    {
                        Hwnd = hwnd,
                        Pid = pid,
                        ProfileActive = false
                    });
                    discovered++;
                }
            }
        }

        if (discovered > 0)
        {
            Log.Info($"已恢复 {discovered} 个运行中 T9 客户端");
        }
    }

    public bool CanCommitForeground()
    {
        lock (_gate)
        {
            return TryPick(out _, out _);
        }
    }

    public bool HasForegroundProfileLease()
    {
        var foreground = NativeMethods.GetAncestor(
            NativeMethods.GetForegroundWindow(),
            NativeMethods.GaRoot);
        return HasProfileLeaseFor(foreground);
    }

    public bool HasProfileLeaseFor(IntPtr target)
    {
        lock (_gate)
        {
            Prune();
            NativeMethods.GetWindowThreadProcessId(target, out var targetPid);
            if (targetPid != 0
                && _clients.Any(client =>
                    client.ProfileActive
                    && ImeRouting.IsUsable(client)
                    && (ImeRouting.PidOf(client) == targetPid
                        || (client.ViewHwnd != IntPtr.Zero
                            && ShellProcess.BelongsToForegroundSurface(
                                target,
                                client.ViewHwnd)))))
            {
                return true;
            }

            if (ShellProcess.Name(target) == "explorer"
                && ShellProcess.TryFindVisibleSearch(out var search, out _))
            {
                NativeMethods.GetWindowThreadProcessId(search, out var searchPid);
                return searchPid != 0
                    && _clients.Any(client =>
                        client.ProfileActive
                        && ImeRouting.IsUsable(client)
                        && ImeRouting.PidOf(client) == searchPid);
            }

            return false;
        }
    }

    public bool HasSystemProfileLease()
    {
        lock (_gate)
        {
            Prune();
            // Explorer's taskbar search box does not create its own TIP context
            // until SearchHost opens. Require an observed (not default) active
            // T9 profile from TSF to bridge only that bootstrap interval.
            return _clients.Any(client =>
                client.ProfileActive
                && client.ProfileSequence > 0
                && ImeRouting.IsUsable(client)
                && ImeRouting.PidOf(client) != 0);
        }
    }

    public bool TryGetNativeInputField(out InputField field)
    {
        field = default;
        ImeClient? client;
        lock (_gate)
        {
            if (!TryPickClient(out client) || client is null)
            {
                return false;
            }
        }

        var top = IntPtr.Zero;
        if (client.ViewHwnd != IntPtr.Zero)
        {
            top = NativeMethods.GetAncestor(client.ViewHwnd, NativeMethods.GaRoot);
        }
        var foregroundRoot = NativeMethods.GetAncestor(
            NativeMethods.GetForegroundWindow(),
            NativeMethods.GaRoot);
        if (foregroundRoot != IntPtr.Zero
            && client.ViewHwnd != IntPtr.Zero
            && ShellProcess.BelongsToForegroundSurface(
                foregroundRoot,
                client.ViewHwnd))
        {
            top = foregroundRoot;
        }
        if (top == IntPtr.Zero)
        {
            top = foregroundRoot;
        }
        if (top == IntPtr.Zero)
        {
            return false;
        }

        var caret = client.NativeCaret;
        if (caret.IsEmpty)
        {
            return false;
        }
        if (!client.NativeScreen.IsEmpty && !client.NativeScreen.Intersects(caret))
        {
            return false;
        }

        NativeMethods.GetWindowRect(top, out var window);
        var occluder = ShellProcess.IsSystemFlyout(top) || ShellProcess.IsSearch(top)
            ? window
            : default;
        field = new InputField(
            top,
            caret,
            occluder,
            client.ViewHwnd == IntPtr.Zero ? top : client.ViewHwnd,
            new InputContextKey(
                unchecked((ulong)client.Hwnd.ToInt64()),
                client.ContextEpoch,
                unchecked((ulong)client.ViewHwnd.ToInt64()),
                0));
        return true;
    }

    public void Compose(string text) => Send(KindCompose, text);

    public void Commit(string text) => Send(KindCommit, text);

    public void Cancel() => Send(KindCancel, "");

    public bool Backspace() => Send(KindBackspace, "");

    public bool SendReturn() => Send(KindReturn, "");

    public void HideHost()
    {
        ImeCommandChannel[] channels;
        lock (_gate)
        {
            channels = _cmdPipes.Snapshot();
        }

        var payload = Encoding.Unicode.GetBytes("hide\0");
        foreach (var channel in channels)
        {
            SendPipe(new IntPtr((long)channel.Hwnd), channel.Pid, KindLift, payload);
        }
        _visibleHostHwnd = 0;
    }

    public bool ShowHost(
        NativeRect rect,
        byte[] pixels,
        int width,
        int height,
        ulong preferredClient)
    {
        if (pixels.Length == 0 || pixels.Length > MaxPacketBytes - 16 || width < 8 || height < 8)
        {
            return false;
        }

        // 帧头单独写，不再为了拼一个 16 字节前缀而整帧拷贝一份到大对象堆。
        var header = _frameHeader;
        BitConverter.TryWriteBytes(header.AsSpan(0, 4), rect.Left);
        BitConverter.TryWriteBytes(header.AsSpan(4, 4), rect.Top);
        BitConverter.TryWriteBytes(header.AsSpan(8, 4), width);
        BitConverter.TryWriteBytes(header.AsSpan(12, 4), height);
        return SendRaw(KindFrame, header, pixels, preferredClient);
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts = null;
        try
        {
            _notifications.CompleteAdding();
        }
        catch
        {
            // 已经关过了
        }
        lock (_gate)
        {
            _cmdPipes.Dispose();
        }
    }

    private bool Send(int kind, string text)
    {
        IntPtr hwnd;
        uint pid;
        lock (_gate)
        {
            if (!TryPickCommandTarget(out hwnd, out pid))
            {
                var foreground = NativeMethods.GetForegroundWindow();
                NativeMethods.GetWindowThreadProcessId(foreground, out var foregroundPid);
                var candidates = string.Join(
                    ";",
                    _clients
                        .OrderByDescending(client => client.ObservationOrder)
                        .Take(4)
                        .Select(client =>
                            $"{ImeRouting.PidOf(client)}/{unchecked((ulong)client.Hwnd.ToInt64())}"
                            + $"/p{(client.ProfileActive ? 1 : 0)}"
                            + $"/d{(client.DocumentFocused ? 1 : 0)}"
                            + $"/t{(client.ThreadFocused ? 1 : 0)}"
                            + $"/c{client.ContextSequence}"));
                Log.Warn(
                    $"IME 上屏无目标 kind={kind} fg={foregroundPid}/{unchecked((ulong)foreground.ToInt64())} "
                    + $"visible={_visibleHostHwnd} clients={candidates}");
                return false;
            }
        }

        if (SendPipe(hwnd, pid, kind, Encoding.Unicode.GetBytes((text ?? "") + "\0")))
        {
            return true;
        }

        if (hwnd == IntPtr.Zero || !NativeMethods.IsWindow(hwnd))
        {
            Log.Warn($"IME 上屏：管道失败且窗口无效 pid={pid}");
            return false;
        }

        var payload = text ?? "";
        var bytes = Encoding.Unicode.GetBytes(payload + "\0");
        var pinned = System.Runtime.InteropServices.Marshal.AllocHGlobal(bytes.Length);
        try
        {
            System.Runtime.InteropServices.Marshal.Copy(bytes, 0, pinned, bytes.Length);
            var cds = new CopyDataStruct
            {
                DwData = new IntPtr(kind),
                CbData = bytes.Length,
                LpData = pinned
            };
            NativeMethods.SendMessage(hwnd, NativeMethods.WmCopyData, IntPtr.Zero, ref cds);
            return true;
        }
        finally
        {
            System.Runtime.InteropServices.Marshal.FreeHGlobal(pinned);
        }
    }

    private bool SendRaw(int kind, byte[] payload, ulong preferredClient = 0) =>
        SendRaw(kind, payload, null, preferredClient);

    private bool SendRaw(
        int kind,
        byte[] payload,
        byte[]? body,
        ulong preferredClient = 0)
    {
        IntPtr hwnd;
        uint pid;
        lock (_gate)
        {
            if (ImeRouting.TryPickHostClient(
                    _clients,
                    preferredClient,
                    out var preferred)
                && preferred is not null)
            {
                hwnd = preferred.Hwnd;
                pid = ImeRouting.PidOf(preferred);
            }
            else if (!TryPick(out hwnd, out pid))
            {
                Log.Warn("IME 宿主帧：当前前台没有 T9 客户端");
                return false;
            }
        }

        var target = unchecked((ulong)hwnd.ToInt64());
        if (kind == KindFrame && _visibleHostHwnd != 0 && _visibleHostHwnd != target)
        {
            ImeCommandChannel? previous;
            lock (_gate)
            {
                _cmdPipes.TryGet(_visibleHostHwnd, 0, out previous);
            }

            if (previous is not null)
            {
                SendPipe(
                    new IntPtr((long)previous.Hwnd),
                    previous.Pid,
                    KindLift,
                    Encoding.Unicode.GetBytes("hide\0"));
            }
        }

        if (kind == KindFrame)
        {
            _visibleHostHwnd = target;
        }

        if (!SendPipe(hwnd, pid, kind, payload, body))
        {
            if (kind == KindFrame && _visibleHostHwnd == target)
            {
                _visibleHostHwnd = 0;
            }
            Log.Warn($"IME 宿主帧发送失败 pid={pid}");
            return false;
        }
        return true;
    }

    private bool SendPipe(
        IntPtr hwnd,
        uint pid,
        int kind,
        byte[] payload,
        byte[]? body = null)
    {
        if (pid == 0)
        {
            return false;
        }

        ImeCommandChannel? channel;
        lock (_gate)
        {
            var key = unchecked((ulong)hwnd.ToInt64());
            _cmdPipes.TryGet(key, pid, out channel);
        }

        var pipe = channel?.Pipe;
        if (pipe is null || !pipe.IsConnected)
        {
            Log.Warn($"IME 命令通道未接通 pid={pid} hwnd={unchecked((ulong)hwnd.ToInt64())}");
            return false;
        }

        try
        {
            var total = payload.Length + (body?.Length ?? 0);
            var header = new byte[8];
            BitConverter.GetBytes(kind).CopyTo(header, 0);
            BitConverter.GetBytes(total).CopyTo(header, 4);
            lock (pipe)
            {
                pipe.Write(header, 0, header.Length);
                if (payload.Length > 0)
                {
                    pipe.Write(payload, 0, payload.Length);
                }

                if (body is { Length: > 0 })
                {
                    pipe.Write(body, 0, body.Length);
                }

                pipe.Flush();
            }

            return true;
        }
        catch (Exception ex)
        {
            Log.Warn($"IME 命令通道写入 pid={pid}: {ex.Message}");
            lock (_gate)
            {
                if (_cmdPipes.RemoveIfSame(channel!.Hwnd, pipe, out var dead))
                {
                    dead!.Pipe.Dispose();
                }
            }

            return false;
        }
    }

    private void ListenCmd(CancellationToken token, string pipeName)
    {
        while (!token.IsCancellationRequested)
        {
            NamedPipeServerStream? pipe = null;
            try
            {
                pipe = NamedPipeServerStreamAcl.Create(
                    pipeName,
                    PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous,
                    2 * 1024 * 1024,
                    256,
                    PipeAcl.ForHostAndAppContainer());
                pipe.WaitForConnectionAsync(token).GetAwaiter().GetResult();
                var hello = new byte[12];
                pipe.ReadExactly(hello);
                var pid = BitConverter.ToUInt32(hello, 0);
                var hwnd = BitConverter.ToUInt64(hello, 4);
                if (pid == 0 || hwnd == 0)
                {
                    Log.Warn($"IME 命令通道握手无效 pid={pid} hwnd={hwnd}");
                    pipe.Dispose();
                    continue;
                }

                lock (_gate)
                {
                    _cmdPipes.Add(new ImeCommandChannel(pid, hwnd, pipe));
                    var client = _clients.FirstOrDefault(candidate =>
                        unchecked((ulong)candidate.Hwnd.ToInt64()) == hwnd);
                    if (client is null)
                    {
                        client = new ImeClient
                        {
                            Hwnd = new IntPtr((long)hwnd),
                            ProfileActive = false
                        };
                        _clients.Add(client);
                    }
                    client.Pid = pid;
                }

                Log.Info($"IME 命令通道已接通 pid={pid} hwnd={hwnd}");
                SendPipe(new IntPtr((long)hwnd), pid, KindQueryState, []);
                RaiseChanged();
                pipe = null;
            }
            catch (OperationCanceledException)
            {
                pipe?.Dispose();
                return;
            }
            catch (Exception ex)
            {
                pipe?.Dispose();
                Log.Warn($"IME 命令通道: {ex.Message}");
            }
        }
    }

    private void Listen(CancellationToken token, string pipeName)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                string json;
                using (var pipe = NamedPipeServerStreamAcl.Create(
                    pipeName,
                    PipeDirection.In,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous,
                    256,
                    256,
                    PipeAcl.ForHostAndAppContainer()))
                {
                    pipe.WaitForConnectionAsync(token).GetAwaiter().GetResult();
                    using var reader = new StreamReader(pipe, Encoding.UTF8, false, 256, true);
                    json = reader.ReadToEnd();
                }

                if (!_notifications.TryAdd(json))
                {
                    Log.Warn("IME 通知队列已满，丢弃一条通知");
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                Log.Warn($"IME 管道: {ex.Message}");
            }
        }
    }

    private void Handle(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        if (json.Contains("\"t\":\"on\"", StringComparison.Ordinal))
        {
            var hwnd = ReadULong(json, "hwnd");
            var pid = ReadUInt(json, "pid");
            var focused = ImeActivationPolicy.IsFocusedNotification(json);
            var hasDocumentState = json.Contains("\"doc\":", StringComparison.Ordinal);
            var hasThreadState = json.Contains("\"thread\":", StringComparison.Ordinal);
            var documentFocused = hasDocumentState ? ReadUInt(json, "doc") != 0 : focused;
            var threadFocused = hasThreadState ? ReadUInt(json, "thread") != 0 : focused;
            var sequence = ReadUInt(json, "seq");
            var activeFlags = ReadUInt(json, "activeFlags");
            var immersive = ReadUInt(json, "immersive") != 0;
            var uiElementOnly = ReadUInt(json, "uiElementOnly") != 0;
            if (ShouldIgnoreDeactivate(hwnd, pid))
            {
                Log.Warn("IME 激活缺少 hwnd/pid，忽略");
                return;
            }

            var accepted = false;
            var effectiveDocumentFocused = false;
            lock (_gate)
            {
                var effectivePid = pid;
                if (effectivePid == 0 && hwnd != 0)
                {
                    NativeMethods.GetWindowThreadProcessId(new IntPtr((long)hwnd), out effectivePid);
                }
                var client = _clients.FirstOrDefault(candidate =>
                    hwnd != 0 && unchecked((ulong)candidate.Hwnd.ToInt64()) == hwnd);
                if (client is null)
                {
                    client = new ImeClient
                    {
                        Hwnd = hwnd == 0 ? IntPtr.Zero : new IntPtr((long)hwnd)
                    };
                    _clients.Add(client);
                }
                client.Pid = effectivePid;
                client.ActiveFlags = activeFlags;
                client.Immersive = immersive;
                client.UiElementOnly = uiElementOnly;
                client.Focused = !hasDocumentState && !hasThreadState && focused;
                var documentApplied = ImeClientState.ApplyExclusiveDocumentFocus(
                    _clients, client, documentFocused, sequence, effectivePid);
                var threadApplied = ImeClientState.ApplyExclusiveThreadFocus(
                    _clients, client, threadFocused, sequence, effectivePid);
                ImeClientState.ApplyProfile(client, active: true, sequence);
                accepted = documentApplied || threadApplied;
                effectiveDocumentFocused = client.DocumentFocused;
            }

            if (!accepted)
            {
                return;
            }
            HasDocumentFocus = effectiveDocumentFocused;
            Log.Info(
                $"T9 九键已激活 pid={pid} hwnd={hwnd} doc={documentFocused} thread={threadFocused} seq={sequence}");
            RaiseChanged();
            return;
        }

        if (json.Contains("\"t\":\"off\"", StringComparison.Ordinal))
        {
            var hwnd = ReadULong(json, "hwnd");
            var pid = ReadUInt(json, "pid");
            if (hwnd == 0 && pid == 0)
            {
                Log.Warn("IME 停用消息缺少 hwnd/pid，重新读取 TSF 当前配置");
                RaiseChanged();
                _ = Task.Delay(250).ContinueWith(
                    _ => RaiseChanged(),
                    CancellationToken.None,
                    TaskContinuationOptions.None,
                    TaskScheduler.Default);
                return;
            }

            lock (_gate)
            {
                // Deactivate 报的是 IME 消息窗，不是客户端文档窗；按 pid 清掉该进程全部 client。
                _clients.RemoveAll(c =>
                    (hwnd != 0 && unchecked((ulong)c.Hwnd.ToInt64()) == hwnd)
                    || (pid != 0 && c.Pid == pid));

                if (hwnd != 0 && _cmdPipes.Remove(hwnd, out var channel))
                {
                    channel.Pipe.Dispose();
                }
            }

            HasDocumentFocus = HasClient;
            Log.Info($"T9 九键已在进程 {pid} 停用");
            RaiseChanged();
            return;
        }

        if (json.Contains("\"t\":\"profile\"", StringComparison.Ordinal))
        {
            var active = json.Contains("\"on\":1", StringComparison.Ordinal);
            var hwnd = ReadULong(json, "hwnd");
            var pid = ReadUInt(json, "pid");
            var sequence = ReadUInt(json, "seq");
            if (pid == 0 && hwnd != 0)
            {
                NativeMethods.GetWindowThreadProcessId(new IntPtr((long)hwnd), out pid);
            }

            var stateApplied = false;
            var deactivatedVisibleHost = false;
            lock (_gate)
            {
                var selected = _clients.FirstOrDefault(client =>
                    hwnd != 0 && unchecked((ulong)client.Hwnd.ToInt64()) == hwnd);
                if (selected is not null)
                {
                    stateApplied = ImeClientState.ApplyProfile(selected, active, sequence);
                }
                else
                {
                    // profile 通知的 hwnd 是 IME 消息窗，对不上文档窗时按进程落地。
                    stateApplied = ImeClientState.ApplyProfileToProcess(
                        _clients,
                        pid,
                        active,
                        sequence)
                        || !active;
                }

                deactivatedVisibleHost = stateApplied
                    && !active
                    && _clients.Any(client =>
                        unchecked((ulong)client.Hwnd.ToInt64()) == _visibleHostHwnd
                        && !client.ProfileActive);
            }

            if (!stateApplied)
            {
                return;
            }
            if (deactivatedVisibleHost)
            {
                HideHost();
            }
            Log.Info($"TSF 当前配置 {(active ? "T9" : "其他输入法")} pid={pid} hwnd={hwnd}");
            RaiseChanged();
            return;
        }

        if (json.Contains("\"t\":\"context\"", StringComparison.Ordinal))
        {
            var active = ReadUInt(json, "on") != 0;
            var profileActive = ReadUInt(json, "profile") != 0;
            var threadFocused = ReadUInt(json, "thread") != 0;
            var hwnd = ReadULong(json, "hwnd");
            var pid = ReadUInt(json, "pid");
            var sequence = ReadUInt(json, "seq");
            var epoch = ReadUInt(json, "epoch");
            var layout = (int)ReadUInt(json, "layout");
            var activeFlags = ReadUInt(json, "activeFlags");
            var immersive = ReadUInt(json, "immersive") != 0;
            var uiElementOnly = ReadUInt(json, "uiElementOnly") != 0;
            if (pid == 0 && hwnd != 0)
            {
                NativeMethods.GetWindowThreadProcessId(new IntPtr((long)hwnd), out pid);
            }

            lock (_gate)
            {
                var selected = _clients.FirstOrDefault(client =>
                    hwnd != 0 && unchecked((ulong)client.Hwnd.ToInt64()) == hwnd);
                if (selected is null && hwnd != 0)
                {
                    selected = new ImeClient
                    {
                        Hwnd = new IntPtr((long)hwnd),
                        Pid = pid,
                        ProfileActive = false
                    };
                    _clients.Add(selected);
                }
                if (selected is null
                    || !ImeClientState.ApplyContext(
                        selected,
                        active,
                        profileActive,
                        threadFocused,
                        sequence,
                        epoch,
                        layout,
                        new NativeRect
                        {
                            Left = ReadInt(json, "x"),
                            Top = ReadInt(json, "y"),
                            Right = ReadInt(json, "r"),
                            Bottom = ReadInt(json, "b")
                        },
                        new NativeRect
                        {
                            Left = ReadInt(json, "sx"),
                            Top = ReadInt(json, "sy"),
                            Right = ReadInt(json, "sr"),
                            Bottom = ReadInt(json, "sb")
                        },
                        new IntPtr((long)ReadULong(json, "view"))))
                {
                    return;
                }

                if (active && threadFocused)
                {
                    foreach (var other in _clients.Where(client =>
                                 client != selected && ImeRouting.PidOf(client) == pid))
                    {
                        other.DocumentFocused = false;
                    }
                }

                selected.Pid = pid;
                selected.ActiveFlags = activeFlags;
                selected.Immersive = immersive;
                selected.UiElementOnly = uiElementOnly;
                selected.ObservationOrder = Interlocked.Increment(ref _observationSequence);
            }

            HasDocumentFocus = active;
            Log.Info(
                $"TSF 上下文 {(active ? "有效" : "无效")} pid={pid} hwnd={hwnd} "
                + $"epoch={epoch} layout={layout} seq={sequence}");
            RaiseChanged();
            return;
        }

        if (json.Contains("\"t\":\"focus\"", StringComparison.Ordinal))
        {
            var focused = !json.Contains("\"on\":0", StringComparison.Ordinal);
            var hwnd = ReadULong(json, "hwnd");
            var pid = ReadUInt(json, "pid");
            var sequence = ReadUInt(json, "seq");
            var documentEvent = json.Contains("\"kind\":\"doc\"", StringComparison.Ordinal);
            var threadEvent = json.Contains("\"kind\":\"thread\"", StringComparison.Ordinal);
            var stateApplied = false;
            var effectiveDocumentFocused = false;
            lock (_gate)
            {
                var effectivePid = pid;
                if (effectivePid == 0 && hwnd != 0)
                {
                    NativeMethods.GetWindowThreadProcessId(new IntPtr((long)hwnd), out effectivePid);
                }
                var selected = _clients.FirstOrDefault(client =>
                    hwnd != 0 && unchecked((ulong)client.Hwnd.ToInt64()) == hwnd);
                if (selected is null && hwnd != 0)
                {
                    selected = new ImeClient
                    {
                        Hwnd = new IntPtr((long)hwnd),
                        Pid = effectivePid,
                        ProfileActive = false
                    };
                    _clients.Add(selected);
                }
                if (selected is not null)
                {
                    if (documentEvent)
                    {
                        stateApplied = ImeClientState.ApplyExclusiveDocumentFocus(
                            _clients, selected, focused, sequence, effectivePid);
                    }
                    else if (threadEvent)
                    {
                        stateApplied = ImeClientState.ApplyExclusiveThreadFocus(
                            _clients, selected, focused, sequence, effectivePid);
                    }
                    else if (!documentEvent && !threadEvent)
                    {
                        selected.Focused = focused;
                        stateApplied = true;
                    }

                    effectiveDocumentFocused = selected.DocumentFocused;
                }
                if (stateApplied
                    && focused
                    && effectivePid != 0
                    && !documentEvent
                    && !threadEvent)
                {
                    foreach (var client in _clients.Where(client =>
                                 client != selected
                                 && ImeRouting.PidOf(client) == effectivePid))
                    {
                        client.Focused = false;
                    }
                }
            }

            if (!stateApplied)
            {
                return;
            }
            if (documentEvent || (!documentEvent && !threadEvent))
            {
                HasDocumentFocus = effectiveDocumentFocused;
            }
            Log.Info(
                $"TSF {(documentEvent ? "文档" : threadEvent ? "线程" : "兼容")}焦点 "
                + $"{(focused ? "进入" : "离开")} pid={pid} hwnd={hwnd} seq={sequence}");
            RaiseChanged();
            return;
        }

        if (json.Contains("\"t\":\"press\"", StringComparison.Ordinal))
        {
            HostPress?.Invoke(
                (int)ReadUInt(json, "x"),
                (int)ReadUInt(json, "y"));
            return;
        }

        if (json.Contains("\"t\":\"hit\"", StringComparison.Ordinal))
        {
            HostHit?.Invoke(
                (int)ReadUInt(json, "x"),
                (int)ReadUInt(json, "y"));
            return;
        }

        if (json.Contains("\"t\":\"swipe\"", StringComparison.Ordinal))
        {
            HostSwipe?.Invoke(
                (int)ReadUInt(json, "x1"),
                (int)ReadUInt(json, "y1"),
                (int)ReadUInt(json, "x2"),
                (int)ReadUInt(json, "y2"));
            return;
        }

        if (json.Contains("\"t\":\"moved\"", StringComparison.Ordinal))
        {
            HostMoved?.Invoke(
                ReadInt(json, "x"),
                ReadInt(json, "y"));
            return;
        }

        if (json.Contains("\"t\":\"host\"", StringComparison.Ordinal))
        {
            var shown = json.Contains("\"on\":1", StringComparison.Ordinal);
            var client = ReadULong(json, "client");
            var error = ReadUInt(json, "err");
            if (!HostResponsePolicy.IsCurrent(client, _visibleHostHwnd))
            {
                return;
            }

            if (shown)
            {
                Log.Info(
                    $"系统浮层 HostRender 已显示 band={ReadUInt(json, "band")} "
                    + $"child={ReadUInt(json, "child")} hwnd={ReadULong(json, "hwnd")} "
                    + $"owner={ReadULong(json, "owner")} view={ReadULong(json, "view")}");
                HostVisibilityChanged?.Invoke(true);
            }
            else if (error != 0)
            {
                Log.Warn($"系统浮层 HostRender 失败 err={error}");
                HostVisibilityChanged?.Invoke(false);
            }
            return;
        }

        if (json.Contains("\"t\":\"apply\"", StringComparison.Ordinal))
        {
            Log.Warn(
                $"TSF 编辑失败 pid={ReadUInt(json, "pid")} "
                + $"kind={ReadInt(json, "kind")} hr=0x{unchecked((uint)ReadInt(json, "hr")):X8}");
            return;
        }

    }

    private bool TryPick(out IntPtr hwnd, out uint pid)
    {
        hwnd = IntPtr.Zero;
        pid = 0;
        if (!TryPickClient(out var client) || client is null)
        {
            return false;
        }

        hwnd = client.Hwnd;
        pid = ImeRouting.PidOf(client);
        return pid != 0;
    }

    private bool TryPickCommandTarget(out IntPtr hwnd, out uint pid)
    {
        hwnd = IntPtr.Zero;
        pid = 0;
        Prune();
        var foreground = NativeMethods.GetForegroundWindow();
        NativeMethods.GetWindowThreadProcessId(foreground, out var foregroundPid);
        if (ImeRouting.TryPickVisibleCommandTarget(
                _clients,
                _visibleHostHwnd,
                foregroundPid,
                view => IsSameSurface(foreground, view),
                out var visible)
            && visible is not null)
        {
            hwnd = visible.Hwnd;
            pid = ImeRouting.PidOf(visible);
            return pid != 0;
        }

        if (TryPick(out hwnd, out pid))
        {
            return true;
        }

        if (ImeRouting.TryPickSurfaceCommandTarget(
                _clients,
                foregroundPid,
                view => IsSameSurface(foreground, view),
                out var surface)
            && surface is not null)
        {
            hwnd = surface.Hwnd;
            pid = ImeRouting.PidOf(surface);
            return pid != 0;
        }

        return false;
    }

    private bool TryPickClient(out ImeClient? client)
    {
        Prune();
        var fg = NativeMethods.GetForegroundWindow();
        var surface = ShellProcess.ResolveForegroundSurface(fg);
        NativeMethods.GetWindowThreadProcessId(fg, out var fgPid);
        var brokeredForeground = ShellProcess.IsForegroundBrokeredSurface();
        if (ImeRouting.TryPickContextViewByScore(
                _clients,
                view => ShellProcess.ForegroundSurfaceScore(surface, view),
                out client))
        {
            return true;
        }
        return ImeRouting.TryPick(
                _clients,
                fgPid,
                brokeredForeground,
                ShellProcess.IsSystemTextClient,
                out client)
            && client is not null;
    }

    private static bool IsSameSurface(IntPtr foreground, IntPtr contextView)
        => ShellProcess.BelongsToForegroundSurface(
            ShellProcess.ResolveForegroundSurface(foreground),
            contextView);

    internal static bool IsFlyoutHostProcessName(string name) =>
        name is "searchhost" or "startmenuexperiencehost" or "searchapp"
            or "searchui" or "shellexperiencehost" or "explorer";

    internal static bool ShouldIgnoreDeactivate(ulong hwnd, uint pid) =>
        hwnd == 0 && pid == 0;

    private void Prune()
    {
        _clients.RemoveAll(c => c.Hwnd != IntPtr.Zero && !NativeMethods.IsWindow(c.Hwnd));
    }

    private static uint PidOf(ImeClient client)
    {
        if (client.Pid != 0)
        {
            return client.Pid;
        }

        if (client.Hwnd == IntPtr.Zero)
        {
            return 0;
        }

        NativeMethods.GetWindowThreadProcessId(client.Hwnd, out var pid);
        return pid;
    }

    private static uint ReadUInt(string json, string name)
    {
        var match = Regex.Match(json, $"\"{name}\":(\\d+)");
        return match.Success && ulong.TryParse(match.Groups[1].Value, out var value)
            ? (uint)value
            : 0;
    }

    private static int ReadInt(string json, string name)
    {
        var match = Regex.Match(json, $"\"{name}\":(-?\\d+)");
        return match.Success && int.TryParse(match.Groups[1].Value, out var value)
            ? value
            : 0;
    }

    private static ulong ReadULong(string json, string name)
    {
        var match = Regex.Match(json, $"\"{name}\":(\\d+)");
        return match.Success && ulong.TryParse(match.Groups[1].Value, out var value)
            ? value
            : 0;
    }
}
