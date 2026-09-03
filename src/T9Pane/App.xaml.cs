using System.Threading;
using System.Windows;
using T9Pane.Native;
using T9Pane.Overlay;
using T9Pane.Services;
using Application = System.Windows.Application;

namespace T9Pane;

public partial class App
{
    private Mutex? _mutex;
    private AppSettings _settings = new();
    private T9Engine _engine = new();
    private readonly ImeCatalog _catalog = new();
    private ForegroundTracker? _foreground;
    private PointerIntentTracker? _pointerIntent;
    private ChromiumAccessibilityActivator? _chromiumA11y;
    private T9OverlayWindow? _overlay;
    private KeyboardSession? _session;
    private TrayHost? _tray;
    private readonly WindowFitter _fitter = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        System.Windows.Forms.WindowsFormsSynchronizationContext.AutoInstall = false;

        if (e.Args.Any(a => a.Equals("/register", StringComparison.OrdinalIgnoreCase)))
        {
            ImeRegister.Run(true);
            Shutdown();
            return;
        }

        if (e.Args.Any(a => a.Equals("/unregister", StringComparison.OrdinalIgnoreCase)))
        {
            ImeRegister.Run(false);
            Shutdown();
            return;
        }

        if (UiAccessInstall.TryHandoffToInstalled())
        {
            Shutdown();
            return;
        }

        _mutex = new Mutex(true, @"Local\T9Pane.SingleInstance", out var created);
        if (!created)
        {
            Shutdown();
            return;
        }

        _settings = AppSettings.Load();
        if (_settings.AutoStart)
        {
            _settings.Save();
        }
        ImeRegister.PointToNewestDll();
        ImeRegister.RepairProfileEnablement();
        _settings.OutputMode = OutputMode.BuiltInT9;
        _catalog.Refresh();
        _engine.Load(_catalog, _settings.ExtraLexiconDirectories);
        _foreground = new ForegroundTracker(work => Dispatcher.BeginInvoke(work));
        _chromiumA11y = new ChromiumAccessibilityActivator();
        _overlay = new T9OverlayWindow(_engine, _settings, _foreground);
        _session = new KeyboardSession(_settings, _overlay, _foreground);
        _pointerIntent = new PointerIntentTracker(work => Dispatcher.BeginInvoke(work));
        _pointerIntent.PointerDown += (x, y, target, targetPid, origin) =>
        {
            if (!PointerIntentTrackingPolicy.IsKeyboardWindow(
                    NativeMethods.GetWindowClass(target)))
            {
                var targetProcess = ShellProcess.Name(targetPid);
                if (origin == PointerInvocationOrigin.Unknown
                    && targetProcess == "startmenuexperiencehost")
                {
                    origin = PointerInvocationOrigin.StartMenuSurface;
                }
                if (origin != PointerInvocationOrigin.Unknown
                    || targetProcess is "startmenuexperiencehost"
                        or "searchhost")
                {
                    Log.Info(
                        $"系统指针按下 pid={targetPid} process={targetProcess} "
                        + $"origin={origin} x={x} y={y}");
                }
                _session?.NotePointerInput(x, y, origin);
            }
        };
        ImeHost.Shared.Start();
        // 这里必须逐条通知立即同步：开始菜单搜索框由 StartMenuExperienceHost 转交给
        // SearchHost，授权发生在交接完成之前，靠随后每一条 TSF 通知补一次同步才能把
        // 键盘放上去。合并或降低优先级会让第一次点击弹不出来。
        ImeHost.Shared.Changed += () => Dispatcher.BeginInvoke(() =>
        {
            UpdatePointerTracking();
            _session.NoteImeChanged();
            _session.Sync(ImeHost.Shared.HasDocumentFocus);
        });
        _foreground.Changed += () => Dispatcher.BeginInvoke(() =>
        {
            UpdatePointerTracking();
            _pointerIntent?.RefreshShellTargets();
            _chromiumA11y?.NoteForeground(_foreground.LastTarget);
            _session?.Sync();
        });
        _tray = new TrayHost(
            _settings,
            () => Sync(),
            ReloadLexicon,
            ExitApp,
            () => AppearanceDialog.ShowOpacity(_overlay, _settings),
            () => AppearanceDialog.ShowSkin(_overlay, _settings));

        Log.Info($"T9 九键输入法后端启动，{_catalog.Summary}，词库 {_engine.Count} 条");
        UpdatePointerTracking();
        Sync();
    }

    private void Sync() => _session?.Sync();

    private void UpdatePointerTracking() =>
        _pointerIntent?.SetEnabled(PointerIntentTrackingPolicy.ShouldEnable(
            ImeHost.Shared.CanCommitForeground(),
            ImeHost.Shared.HasForegroundProfileLease(),
            ImeHost.Shared.HasSystemProfileLease()));

    private void ReloadLexicon()
    {
        _catalog.Refresh();
        _engine.Load(_catalog, _settings.ExtraLexiconDirectories);
        _overlay?.RefreshChrome();
        Log.Info($"已重新加载词库：{_engine.SourceDescription}");
    }

    private void ExitApp()
    {
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _pointerIntent?.Dispose();
        _chromiumA11y?.Dispose();
        _session?.Shutdown();
        ImeHost.Shared.Dispose();
        _overlay?.HideOverlay();
        _fitter.Restore();
        _foreground?.Dispose();
        _tray?.Dispose();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
