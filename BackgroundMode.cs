using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using ChinPad.Native;

namespace ChinPad;

/// <summary>How the settings window draws a touch.</summary>
internal enum DotState
{
    /// <summary>Just landed, not classified yet (zoneSettleMs).</summary>
    Undecided,
    /// <summary>In a click zone: does not move the cursor, a press clicks.</summary>
    Zone,
    /// <summary>The touch that moves the cursor now.</summary>
    Moving,
    /// <summary>ChinPad's, in the move area, but not the one moving the cursor (paused, or a second touch).</summary>
    Move,
    /// <summary>Not ChinPad's: a finger that Windows handles (strategy 2).</summary>
    Windows,
}

/// <summary>One touch on a pad: contact ID, position in mm from the top-left corner, state; Zone = the zone's action when State is Zone.</summary>
internal readonly record struct ContactDot(int Id, double XMm, double YMm, DotState State, ZoneAction? Zone);

/// <summary>The last press of a pad, in mm (HasPosition false when no touch was down), with its frame time (Clock ms) and outcome.</summary>
internal readonly record struct PressMark(bool HasPosition, double XMm, double YMm, double AtMs, string Outcome);

/// <summary>
/// One touchpad in use, for the settings window's live drawing: index and name as in the log, geometry,
/// reports so far, when it last had a touch (Clock ms, 0 = never), its touches now, its last press, and its
/// label as in the "Apply to" list (0.5.0; "" when unknown).
/// </summary>
internal sealed record PadSnapshot(int Index, string Name, PadGeometry Geometry, long Reports, double LastContactMs,
    IReadOnlyList<ContactDot> Contacts, PressMark? Press, string Label = "");

/// <summary>
/// Everything the settings window shows live: the pads, pause state, click counters, a failed component (or
/// null), and the button ClickLock keeps down (or null, 0.5.0).
/// </summary>
internal sealed record LiveState(IReadOnlyList<PadSnapshot> Pads, bool Paused, long LeftClicks, long RightClicks, long MiddleClicks,
    long WindowsClicksDropped, string? Problem, MouseButton? Locked = null);

/// <summary>
/// "ChinPad.exe" without arguments: no console, a tray icon, the cursor follows the chin, and pressing the
/// pad while a touch classified in a button zone is down clicks. Everything runs on the UI thread (raw
/// input, engines, SendInput, tray, timers, settings reload, the settings window, the diagnostic bundle)
/// except the mouse hook (strategy 3 and/or hookClicks), which has its own thread, and the click beep
/// (thread pool). settings.json and chinpad.log live where SettingsPaths says (next to the exe in portable
/// mode, else %LOCALAPPDATA%\ChinPad). Per frame, ClickEngine sees the frame before CursorEngine, which asks
/// it which contacts it must not drive (still settling, or zone contacts).
/// 0.5.0: touchpads plugged in or removed are noticed (WM_INPUT_DEVICE_CHANGE, 500 ms debounce) or looked up
/// again by hand (the settings window's Refresh); the selection is then applied again (TouchpadSet). Every
/// touchpad's last report time is kept, so the "Apply to" list can mark the one being touched.
/// </summary>
internal sealed class BackgroundMode : IDisposable, ISettingsHost, DiagnosticBundle.ISource
{
    const int HotkeyId = 1;
    const int DebounceMs = 300;
    const int PlugDebounceMs = 500;  // touchpads plugged in or removed: enumerate again this long after the last notification
    const int LockTickMs = 50;       // ClickLock between frames
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>A touchpad in use and the assembler that turns its reports into frames.</summary>
    sealed class Pad(TouchpadDevice dev, PadGeometry geometry)
    {
        public TouchpadDevice Dev { get; } = dev;
        public PadGeometry Geometry { get; } = geometry;
        public FrameAssembler Asm { get; } = new(dev.ContactCount is not null);
        public long Reports, Errors;
        public uint LastButtons;       // button bits of the last report (the bundle recording's "pressed")
        public double LastContactMs;   // Clock ms of the last frame with a touch; 0 = none yet
    }

    readonly SettingsPaths _paths;
    readonly string _settingsPath, _readmePath, _exePath;
    readonly DateTime _startedAt = DateTime.Now;
    readonly List<string> _enumErrors;
    readonly List<ContactInfo> _infos = [];
    readonly FileLog _log;
    readonly int? _seconds;
    readonly TouchpadSet _set;                    // every touchpad found (kept up to date on hot-plug)
    readonly List<TouchpadDevice> _all;          // = _set.All
    readonly Dictionary<IntPtr, Pad> _pads = []; // the ones selected by settings "device"
    readonly Dictionary<IntPtr, double> _lastReportMs = []; // every touchpad's last report (Clock ms), selected or not
    readonly InputWindow _window = new();
    readonly Win32Mover _mover = new();
    readonly ClickEngine _clicks;
    readonly CursorEngine _engine;
    readonly MouseHook _hook;
    readonly TrayIcon _tray = new();
    readonly Control _marshal = new();           // FileSystemWatcher events are marshalled to the UI thread through it
    readonly List<System.Windows.Forms.Timer> _timers = [];
    readonly System.Windows.Forms.Timer _debounce = new() { Interval = DebounceMs };
    readonly System.Windows.Forms.Timer _plugDebounce = new() { Interval = PlugDebounceMs };
    readonly System.Windows.Forms.Timer _lockTimer = new() { Interval = LockTickMs };
    FileSystemWatcher? _watcher;
    AppSettings _settings;
    string? _rawInputProblem, _deviceProblem, _hookProblem;
    string _hotkeyText = "", _hotkeyProblem = "", _hotkeyBeforeSuspend = "";
    bool _hotkeySuspended;
    int _hotkeyErrorCode;
    string? _selfWrite;                 // why settings.json was last written by ChinPad itself (the reload's log line)
    SettingsForm? _form;
    AboutForm? _about;
    WizardForm? _wizard;
    readonly bool _firstRun;
    bool _wizardRequested, _pausedBeforeWizard, _startupWizardPause;
    volatile bool _wizardCaptures;
    double _wizardContactMs = double.NegativeInfinity;
    EventWaitHandle? _wizardRequest;
    DiagnosticBundle? _bundle;
    ReportRecorder? _recording;         // the bundle's recording, while it runs
    string _stopReason = "Exit in the tray menu";
    long _otherReports, _loggedSendFailures, _loggedIgnored, _loggedSecondClicks;
    double _lastFailureLogMs = double.NegativeInfinity, _lastIgnoredLogMs = double.NegativeInfinity, _lastSecondClickLogMs = double.NegativeInfinity;
    string? _deviceFallback;            // why all touchpads are used although "device" names one (it is not connected)
    bool _stopped;

    /// <summary>Raised on the UI thread when the pause state, the settings in use, the pause key or the touchpads found changed (settings window).</summary>
    public event Action? StateChanged;

    /// <summary>Automated checks: no beeps (the click beep and ClickLock's double beep).</summary>
    internal static bool Silent { get; set; }

    /// <summary>Entry point from Program.Main. Returns the process exit code.</summary>
    public static int Run(string exeDir, int? seconds, bool wizard = false)
    {
        SettingsPaths paths = SettingsPaths.Resolve(exeDir);
        var log = new FileLog(paths.LogFile);
        try
        {
            // Two instances would both move the cursor (double speed) and fight over the hotkey.
            using var instance = new Mutex(false, @"Local\ChinPad.Background", out bool firstInstance);
            if (!firstInstance)
            {
                if (wizard)
                {
                    // A second launch may beat the first process's event creation by a few milliseconds.
                    for (int attempt = 0; attempt < 40; attempt++)
                    {
                        try
                        {
                            using EventWaitHandle request = EventWaitHandle.OpenExisting(@"Local\ChinPad.Wizard");
                            request.Set();
                            return 0;
                        }
                        catch (WaitHandleCannotBeOpenedException) { Thread.Sleep(50); }
                    }
                }
                log.Write("another ChinPad background instance is already running in this session; this one exits");
                return 4;
            }

            // Publish the request endpoint before device enumeration / window construction can take time.
            using var requestEvent = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\ChinPad.Wizard");

            // Per-monitor DPI awareness before any window exists: crisp tray menu on the 200 % panel, and
            // cursor coordinates in physical pixels for the absolute move mode.
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (_, e) => log.Write("error on the UI thread: " + e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (_, e) => log.Write("unhandled exception: " + e.ExceptionObject);

            using var mode = new BackgroundMode(exeDir, paths, log, seconds);
            mode._wizardRequested = wizard || requestEvent.WaitOne(0);
            mode._wizardRequest = requestEvent;
            mode.Start();
            Application.Run(new ApplicationContext());
            mode.Stop();
            return 0;
        }
        catch (Exception ex)
        {
            log.Write("fatal error: " + ex);
            return 1;
        }
    }

    BackgroundMode(string exeDir, SettingsPaths paths, FileLog log, int? seconds)
    {
        _log = log;
        _seconds = seconds;
        _paths = paths;
        _settingsPath = paths.SettingsFile;
        _readmePath = Path.Combine(exeDir, "README.md");
        _exePath = Environment.ProcessPath ?? Path.Combine(exeDir, "ChinPad.exe");

        _log.Write(string.Create(Inv,
            $"ChinPad {Version} background mode starting (pid {Environment.ProcessId}, Windows {Environment.OSVersion.Version}, {(Environment.Is64BitProcess ? "64" : "32")}-bit){(seconds is int s ? $", stops after {s} s (--seconds)" : "")}"));
        _log.Write(paths.Describe());
        _log.Write(InputNative.DescribeLayout(out bool layoutOk));
        if (!layoutOk) _rawInputProblem = "interop struct size mismatch";

        _firstRun = !File.Exists(_settingsPath);
        _settings = AppSettings.LoadOrCreate(_settingsPath, null, out string note, out List<string> details);
        L10n.Apply(_settings.Language);
        LogLanguage();
        _log.Write($"settings.json {note}: {_settings.ToCompactJson()}");
        foreach (string d in details) _log.Write("  settings: " + d);

        _clicks = new ClickEngine(_mover, _settings);
        _clicks.Notice += OnClickNotice;
        _engine = new CursorEngine(_mover, _settings, _clicks.BlocksCursor);
        _hook = new MouseHook(() => WizardOwnsInput || _engine.BlockMoves, () => WizardOwnsInput || _clicks.BlockClicks);
        _set = new TouchpadSet();
        _all = _set.All;
        _enumErrors = _set.Errors;
        _log.Write(string.Create(Inv, $"touchpads found: {_all.Count}"));
        foreach (TouchpadDevice d in _all)
            _log.Write(string.Create(Inv, $"  #{d.Index} {d.ShortName}: {d.Fingers.Count} finger collections, {d.SummaryLine()}"));
        foreach (string e in _enumErrors) _log.Write("  could not open: " + e);
        L10n.Changed += OnLanguageChanged;
    }

    static string Version => typeof(BackgroundMode).Assembly.GetName().Version?.ToString(3) ?? "?";

    void Start()
    {
        // Own input from the first registered report, before the modeless window is shown.
        if (_firstRun || _wizardRequested)
        {
            _startupWizardPause = true;
            _pausedBeforeWizard = _engine.Paused;
            _engine.Paused = _clicks.Paused = true;
        }
        _ = _marshal.Handle; // create the marshalling window on this (UI) thread

        _window.HidReport += OnHidReport;
        _window.HotKey += id =>
        {
            if (id == HotkeyId) SetPaused(!_engine.Paused, "hotkey");
        };
        _window.DeviceChange += OnDeviceChange;
        _plugDebounce.Tick += (_, _) =>
        {
            _plugDebounce.Stop();
            Reenumerate("a touchpad was plugged in or removed");
        };
        _lockTimer.Tick += (_, _) =>
        {
            _clicks.Tick(Clock.NowMs);
            if (!_clicks.LockPending) _lockTimer.Stop();
        };
        try
        {
            _window.Register(TouchpadDevice.PageDigitizer, TouchpadDevice.UsageTouchPad, devNotify: true);
            _log.Write("raw input registered for touchpads (usage page 0x0D, usage 0x05, RIDEV_INPUTSINK | RIDEV_DEVNOTIFY: touchpads plugged in or removed are noticed)");
        }
        catch (Exception ex)
        {
            _rawInputProblem = "raw input registration failed";
            _log.Write("raw input registration failed: " + ex.Message);
        }

        SelectPads();
        LogClickSettings();
        LogStrategyNote();
        _mover.Absolute = _settings.AbsoluteMoves;
        ApplyHook();
        ApplyHotkey();
        _log.Write("start at login: " + DescribeStartAtLogin());
        _log.Write(_engine.Paused ? "starting paused (startPaused)" : "active");

        _tray.ShowSettingsWindow += ShowSettingsWindow;
        _tray.SaveBundle += StartBundle;
        _tray.TogglePause += () => SetPaused(!_engine.Paused, "tray");
        _tray.ReloadSettings += () => ReloadSettings("tray menu");
        _tray.OpenSettingsFile += () => OpenFile(_settingsPath);
        _tray.OpenLog += () => OpenFile(_log.Path);
        _tray.OpenReadme += () => OpenFile(_readmePath);
        _tray.ToggleStartAtLogin += ToggleStartAtLogin;
        _tray.ShowAboutWindow += ShowAboutWindow;
        _tray.MenuOpening += RefreshMenu;
        _tray.ExitRequested += () => RequestExit("Exit in the tray menu");
        UpdateTray();
        RefreshMenu();
        _tray.Show();

        // settings.json: reload 300 ms after the last change event, on the UI thread.
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            string why = _selfWrite is null ? "settings.json changed" : "written by the " + _selfWrite;
            _selfWrite = null;
            ReloadSettings(why);
        };
        try
        {
            _watcher = new FileSystemWatcher(Path.GetDirectoryName(_settingsPath)!, Path.GetFileName(_settingsPath))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.CreationTime,
                SynchronizingObject = _marshal,
            };
            _watcher.Changed += OnSettingsFileEvent;
            _watcher.Created += OnSettingsFileEvent;
            _watcher.Renamed += OnSettingsFileEvent;
            _watcher.Deleted += OnSettingsFileEvent;
            _watcher.EnableRaisingEvents = true;
        }
        catch (Exception ex)
        {
            _log.Write("cannot watch settings.json for changes (use Reload settings in the tray menu): " + ex.Message);
        }

        AddTimer(1000, Housekeeping);
        AddTimer(100, () => { if (_wizardRequest?.WaitOne(0) == true) ShowWizard(); });
        if (_firstRun || _wizardRequested) _marshal.BeginInvoke(ShowWizard);
        if (_seconds is int s) AddTimer(s * 1000, () => RequestExit($"--seconds {s} elapsed"));
    }

    /// <summary>After the message loop ended: undo everything in reverse order and log the totals.</summary>
    void Stop()
    {
        if (_stopped) return;
        _stopped = true;
        L10n.Changed -= OnLanguageChanged;
        foreach (System.Windows.Forms.Timer t in _timers) t.Stop();
        _debounce.Stop();
        _plugDebounce.Stop();
        _lockTimer.Stop();
        _watcher?.Dispose();
        _watcher = null;
        _bundle?.Cancel();
        _recording = null;
        _wizardCaptures = false;
        _wizard?.Dispose();
        _wizard = null;
        _about?.Dispose();
        _about = null;
        _form?.Dispose();
        _form = null;
        _clicks.ReleaseAll("ChinPad stopping"); // never leave a button down in Windows
        _hook.Stop();
        _window.UnregisterHotKey(HotkeyId);
        _window.Unregister();
        _tray.Dispose();
        _log.Write(string.Create(Inv,
            $"stopping ({_stopReason}): {_engine.Frames} frames, {_engine.InjectedMoves} cursor moves ({_engine.InjectedPixels} px), " +
            $"the hook dropped {_hook.Dropped} Windows moves and {_hook.DroppedClicks} Windows clicks, {_mover.Failures} SendInput failures, {_otherReports} reports from touchpads not in use"));
        _log.Write("  " + _clicks.CountersText);
        _log.Write(string.Create(Inv, $"  merging (mergeMm {_settings.MergeMm:0.#}): {_clicks.MergeText}, {_engine.MergeHandOvers} cursor hand-overs to a partner"));
        foreach (Pad p in _pads.Values)
            _log.Write(string.Create(Inv, $"  #{p.Dev.Index} {p.Dev.ShortName}: {p.Reports} reports, {p.Asm.CompletedFrames} frames, {p.Errors} errors"));
        _log.Write("stopped");
    }

    public void Dispose()
    {
        Stop();
        foreach (System.Windows.Forms.Timer t in _timers) t.Dispose();
        _debounce.Dispose();
        _plugDebounce.Dispose();
        _lockTimer.Dispose();
        _hook.Dispose();
        _window.Dispose();
        _marshal.Dispose();
        _set.Dispose();
    }

    void AddTimer(int intervalMs, Action tick)
    {
        var t = new System.Windows.Forms.Timer { Interval = intervalMs };
        t.Tick += (_, _) => tick();
        t.Start();
        _timers.Add(t);
    }

    void RequestExit(string reason)
    {
        _stopReason = reason;
        Application.ExitThread(); // ends Application.Run in Run
    }

    // ------------------------------------------------------------------------------------------------
    // Input
    // ------------------------------------------------------------------------------------------------

    void OnHidReport(IntPtr hDevice, byte[] report)
    {
        double now = Clock.NowMs;
        _lastReportMs[hDevice] = now; // "reporting now" in the settings window's list, for every touchpad
        if (!_pads.TryGetValue(hDevice, out Pad? p))
        {
            _otherReports++; // a touchpad excluded by "device", or one plugged in that is not looked up yet
            return;
        }
        try
        {
            p.Reports++;
            ContactReport r = p.Dev.ParseReport(report);
            ReportOutcome o = p.Asm.Process(r, now);
            if (o.Completed is Frame frame)
            {
                _clicks.OnFrame(hDevice, frame, p.Asm.Tracks, now); // first: classifies new touches for CursorEngine
                _engine.OnFrame(hDevice, frame, p.Asm.Tracks, now);
                if (frame.Contacts.Count > 0) p.LastContactMs = now;
                if (frame.Contacts.Any(c => c.Tip)) Volatile.Write(ref _wizardContactMs, now);
                _wizard?.Feed(WizardFrame.From(p.Dev.Index, p.Geometry, frame, now));
            }
            uint before = p.LastButtons;
            p.LastButtons = r.Buttons;
            _recording?.WriteReport(p.Dev.Index, p.Geometry, p.Asm, r, o, now, pressed: (r.Buttons & ~before & 7u) != 0);
        }
        catch (Exception ex)
        {
            // One odd report must not stop the cursor; log the first few.
            if (++p.Errors <= 5) _log.Write($"error in a report from #{p.Dev.Index}: {ex.Message}");
        }
    }

    /// <summary>Once a second: forget lifted contacts, report ignored presses and SendInput failures (each at most once a minute).</summary>
    void Housekeeping()
    {
        double now = Clock.NowMs;
        foreach (Pad p in _pads.Values) p.Asm.Prune(now, 1000);
        long ignored = _clicks.PressesIgnored;
        if (ignored != _loggedIgnored && now - _lastIgnoredLogMs >= 60_000)
        {
            _log.Write(string.Create(Inv, $"presses ignored: {ignored - _loggedIgnored} since the last note (the last one: {_clicks.LastIgnoredReason}); {ignored} so far"));
            _loggedIgnored = ignored;
            _lastIgnoredLogMs = now;
        }
        long second = _clicks.DoubleClicksBlocked;
        if (second != _loggedSecondClicks && now - _lastSecondClickLogMs >= 60_000)
        {
            _log.Write(string.Create(Inv, $"second clicks ignored (blockDoubleClick): {second - _loggedSecondClicks} since the last note (the last one: {_clicks.LastDoubleClickText}); {second} so far"));
            _loggedSecondClicks = second;
            _lastSecondClickLogMs = now;
        }
        if (_mover.Failures != _loggedSendFailures && now - _lastFailureLogMs > 60_000)
        {
            _log.Write(string.Create(Inv, $"SendInput failed {_mover.Failures - _loggedSendFailures} times (last error {_mover.LastError})"));
            _loggedSendFailures = _mover.Failures;
            _lastFailureLogMs = now;
        }
    }

    /// <summary>
    /// Uses the touchpads whose path contains settings "device" (all when empty, and all while the one it names is
    /// not connected), each with its own assembler. keepIfSame (after a hot-plug): when that gives the pads already
    /// in use, nothing is reset, so a touch or a locked button on them carries on.
    /// </summary>
    void SelectPads(bool keepIfSame = false)
    {
        List<TouchpadDevice> chosen = _set.Select(_settings.Device, out string? fallback);
        if (_wizard is not null) { chosen = _all.ToList(); fallback = null; }
        bool fallbackChanged = fallback != _deviceFallback;
        _deviceFallback = fallback;
        if (keepIfSame && chosen.Count == _pads.Count && chosen.All(d => _pads.TryGetValue(d.Handle, out Pad? p) && ReferenceEquals(p.Dev, d)))
        {
            _log.Write("  touchpads in use unchanged: " + (chosen.Count == 0 ? "none" : string.Join(", ", chosen.Select(d => $"#{d.Index} {d.ShortName}"))));
            if (fallback is not null && fallbackChanged) _log.Write(fallback);
            return;
        }
        _pads.Clear();
        _engine.ClearDevices();
        _clicks.ClearDevices();
        if (fallback is not null) _log.Write(fallback);
        foreach (TouchpadDevice d in chosen)
        {
            FingerCollection? f = d.Fingers.FirstOrDefault();
            PadGeometry g = PadGeometry.FromFields(f?.X, f?.Y, d.ScanTime);
            _pads[d.Handle] = new Pad(d, g);
            _engine.AddDevice(d.Handle, g);
            _clicks.AddDevice(d.Handle, g, "#" + d.Index.ToString(Inv));
            _log.Write($"using touchpad #{d.Index} {d.ShortName} ({g.Note})");
            LogZones(d, g);
        }
        _deviceProblem = _pads.Count > 0 ? null
            : _all.Count == 0 ? "no touchpad found"
            : $"no touchpad path contains \"{_settings.Device}\"";
        if (_deviceProblem is not null) _log.Write(_deviceProblem);
    }

    /// <summary>WM_INPUT_DEVICE_CHANGE: a touchpad we do not know arrived, or one we use left; enumerate again after 500 ms of quiet.</summary>
    void OnDeviceChange(bool arrived, IntPtr handle)
    {
        if (_stopped || arrived == _set.Contains(handle)) return; // the arrivals reported right after registering are the pads we have
        _plugDebounce.Stop();
        _plugDebounce.Start();
    }

    /// <summary>Enumerates the touchpads again (hot-plug, or the settings window's Refresh) and applies the selection again.</summary>
    void Reenumerate(string why)
    {
        if (_stopped) return;
        List<string> changes;
        try
        {
            changes = _set.Refresh();
        }
        catch (Exception ex)
        {
            _log.Write($"touchpads could not be looked up again ({why}): {ex.Message}");
            return;
        }
        foreach (IntPtr gone in _lastReportMs.Keys.Where(h => !_set.Contains(h)).ToList()) _lastReportMs.Remove(gone);
        if (changes.Count == 0)
        {
            _log.Write(string.Create(Inv, $"touchpads looked up again ({why}): no change, {_all.Count} found"));
            StateChanged?.Invoke();
            return;
        }
        _log.Write(string.Create(Inv, $"touchpads changed ({why}): {_all.Count} found"));
        foreach (string line in changes) _log.Write("  " + line);
        SelectPads(keepIfSame: true);
        _wizard?.Logic.UpdatePads(WizardPads(), Clock.NowMs);
        UpdateTray();
        StateChanged?.Invoke();
    }

    // ------------------------------------------------------------------------------------------------
    // Pause, hotkey, hook, tray
    // ------------------------------------------------------------------------------------------------

    void SetPaused(bool paused, string source)
    {
        if (_wizard is not null && !_wizard.Logic.TryOut) paused = true;
        _engine.Paused = paused;
        _clicks.Paused = paused; // releases a button ChinPad holds
        _log.Write(string.Create(Inv, $"{(paused ? "paused" : "resumed")} ({source}); so far {_engine.InjectedMoves} cursor moves, {_clicks.LeftClicks + _clicks.RightClicks + _clicks.MiddleClicks} clicks"));
        UpdateTray();
        StateChanged?.Invoke();
    }

    /// <param name="quiet">Only log failures (the settings window gives the key back after recording a new one).</param>
    void ApplyHotkey(bool quiet = false)
    {
        _window.UnregisterHotKey(HotkeyId);
        _hotkeyText = "";
        _hotkeyProblem = "";
        _hotkeyErrorCode = 0;
        if (_hotkeySuspended)
        {
            // The settings window is recording a new key; it calls SuspendHotkey(false) when done.
        }
        else if (_settings.Hotkey.Length == 0)
        {
            if (!quiet) _log.Write("no hotkey configured (\"hotkey\": \"\"); pause/resume from the tray icon");
        }
        else if (!HotkeySpec.TryParse(_settings.Hotkey, out HotkeySpec spec, out string why))
        {
            _hotkeyProblem = $"{_settings.Hotkey} cannot be used: {why}";
            _log.Write($"hotkey \"{_settings.Hotkey}\" not usable: {why}");
        }
        else if (_window.RegisterHotKey(HotkeyId, spec.Modifiers, (uint)spec.Key, out int error))
        {
            _hotkeyText = spec.ToString();
            if (!quiet) _log.Write($"hotkey {_hotkeyText} registered (pause/resume)");
        }
        else
        {
            _hotkeyErrorCode = error;
            _hotkeyProblem = error == 1409
                ? $"{spec} is already used by another program; choose another key"
                : $"{spec} does not work: {new Win32Exception(error).Message}";
            _log.Write(string.Create(Inv,
                $"hotkey {spec} could not be registered: {new Win32Exception(error).Message} (error {error}; 1409 = another program uses it). Pause/resume from the tray icon, or choose another \"hotkey\""));
        }
        UpdateTray();
        StateChanged?.Invoke();
    }

    /// <summary>The low-level hook runs when strategy 3 (moves) or hookClicks (clicks) needs it, or both.</summary>
    void ApplyHook()
    {
        string purpose = _wizard is not null ? "first-time setup owns the touchpad" : HookPurpose(_settings);
        if (purpose.Length == 0)
        {
            if (_hook.Running)
            {
                _hook.Stop();
                _log.Write(string.Create(Inv, $"mouse hook removed (strategy {_settings.Strategy} and hookClicks false: nothing to filter)"));
            }
            else
            {
                _log.Write(string.Create(Inv, $"no mouse hook (strategy {_settings.Strategy} and hookClicks false): Windows' own clicks are not filtered"));
            }
            _hookProblem = null;
            return;
        }
        if (_hook.Running)
        {
            _log.Write("mouse hook kept: " + purpose);
            return;
        }
        if (_hook.Start(out string error))
        {
            _hookProblem = null;
            _log.Write("mouse hook installed: " + purpose);
        }
        else
        {
            _hookProblem = "mouse hook failed";
            _log.Write(error + "; so this is not done: " + purpose);
        }
    }

    static string HookPurpose(AppSettings s)
    {
        var parts = new List<string>();
        if (s.Strategy == 3) parts.Add("strategy 3 drops Windows' own moves while a confident contact is down");
        if (s.HookClicks) parts.Add("hookClicks drops Windows' own left/right/middle clicks while ChinPad owns a touch and 100 ms after");
        return string.Join("; ", parts);
    }

    // Keep diagnostic descriptions in English; only presentation uses localized text.
    string? UiProblem => _rawInputProblem is not null
        ? L10n.T(_rawInputProblem == "interop struct size mismatch" ? "Problem.Interop" : "Problem.RawInput")
        : _deviceProblem is not null ? L10n.T(_all.Count == 0 ? "Problem.NoPad" : "Problem.Selection")
        : _hookProblem is not null ? L10n.T("Problem.Hook") : null;

    string UiHotkeyProblem => _hotkeyProblem.Length == 0 ? ""
        : _hotkeyErrorCode == 1409 ? L10n.F("Hotkey.InUse", _settings.Hotkey)
        : _hotkeyErrorCode != 0 ? L10n.F("Hotkey.Failed", _settings.Hotkey, _hotkeyErrorCode)
        : L10n.HotkeyRejection(_settings.Hotkey, "");

    void LogLanguage() => _log.Write($"language: {L10n.Current} (setting {_settings.Language}, Windows UI {CultureInfo.CurrentUICulture.Name})");

    void OnLanguageChanged()
    {
        UpdateTray();
        StateChanged?.Invoke();
    }

    void UpdateTray() => _tray.Update(_engine.Paused, _settings.Strategy, UiProblem, _hotkeyText);

    // ------------------------------------------------------------------------------------------------
    // Settings and files
    // ------------------------------------------------------------------------------------------------

    void OnSettingsFileEvent(object sender, FileSystemEventArgs e)
    {
        _debounce.Stop(); // restart: editors often write a file in several steps
        _debounce.Start();
    }

    /// <summary>Applies changed settings live. A file that cannot be read keeps the current settings.</summary>
    void ReloadSettings(string why)
    {
        AppSettings old = _settings;
        AppSettings s = AppSettings.LoadOrCreate(_settingsPath, old, out string note, out List<string> details);
        _log.Write($"settings reloaded ({why}): {note}");
        foreach (string d in details) _log.Write("  settings: " + d);
        if (s == old)
        {
            _log.Write("  no changes");
            return;
        }
        _settings = s;
        if (s.Language != old.Language)
        {
            L10n.Apply(s.Language);
            LogLanguage();
        }
        _log.Write("  now: " + s.ToCompactJson());
        _clicks.Apply(s);
        _engine.Apply(s);
        _mover.Absolute = s.AbsoluteMoves;
        if (s.Strategy != old.Strategy || s.HookClicks != old.HookClicks) ApplyHook();
        if (s.Strategy != old.Strategy) LogStrategyNote();
        if (s.Hotkey != old.Hotkey) ApplyHotkey();
        bool devicesChanged = !string.Equals(s.Device, old.Device, StringComparison.OrdinalIgnoreCase);
        if (devicesChanged) SelectPads(keepIfSame: _wizard is not null); // logs the zones too
        else if (!s.Zones.SequenceEqual(old.Zones))
        {
            _log.Write("  zones changed (touches already decided keep their zone):");
            foreach (Pad p in _pads.Values) LogZones(p.Dev, p.Geometry);
        }
        if (s.PressOutsideZones != old.PressOutsideZones || s.ClickBeep != old.ClickBeep ||
            s.ZoneSettleMs != old.ZoneSettleMs || s.ZoneMode != old.ZoneMode || s.MergeMm != old.MergeMm ||
            s.ClickLock != old.ClickLock || s.BlockDoubleClick != old.BlockDoubleClick || s.DoubleClickWindowMs != old.DoubleClickWindowMs) LogClickSettings();
        if (!s.ClickLock.Enabled) _lockTimer.Stop();
        _tray.SetLocked(_clicks.LockedButton);
        UpdateTray();
        StateChanged?.Invoke();
    }

    // ------------------------------------------------------------------------------------------------
    // Clicks
    // ------------------------------------------------------------------------------------------------

    void LogZones(TouchpadDevice d, PadGeometry g)
    {
        foreach (string line in ClickEngine.DescribeZones(_settings.Zones, g))
            _log.Write($"  #{d.Index} {line}");
    }

    void LogClickSettings() =>
        _log.Write((_settings.ZoneSettleMs > 0
                       ? string.Create(Inv, $"clicks: a new touch is classified {_settings.ZoneSettleMs:0.#} ms after it lands (zoneSettleMs; a press decides earlier) by where it is then; ")
                       : "clicks: a new touch is classified by where it lands (zoneSettleMs 0); ") +
                   (_settings.PositionZones
                       ? "zoneMode position: a touch inside a zone does not move the cursor, outside it moves; "
                       : "zoneMode landing: a zone touch never moves the cursor, a move touch keeps moving even inside a zone; ") +
                   $"pressing the pad while a zone touch is down clicks that zone's button; " +
                   $"other presses: {(_settings.Passthrough ? "passthrough (button 1/2/3 = left/right/middle at the cursor)" : "ignored")}; " +
                   $"clickBeep {(_settings.ClickBeep ? "on" : "off")}; " +
                   (_settings.MergeMm > 0
                       ? string.Create(Inv, $"mergeMm {_settings.MergeMm:0.#}: contacts of one pad closer than that are one touch; ")
                       : "mergeMm 0: no merging; ") +
                   (_settings.ClickLock.Enabled
                       ? string.Create(Inv, $"clickLock on: a press held {_settings.ClickLock.HoldMs:0} ms keeps the button down until the next press; ")
                       : "clickLock off; ") +
                   (_settings.BlockDoubleClick
                       ? string.Create(Inv, $"blockDoubleClick on: a second press of the same button within {_clicks.DoubleClickWindowMs:0} ms of a click is dropped{(_settings.DoubleClickWindowMs == 0 ? " (Windows' double-click time)" : "")}")
                       : "blockDoubleClick off"));

    /// <summary>Strategy 1 only works as meant with the touchpad switched Off in Windows; say so in the log.</summary>
    void LogStrategyNote()
    {
        if (_settings.Strategy == 1)
            _log.Write("strategy 1: switch the touchpad Off in Settings > Bluetooth & devices > Touchpad so Windows does not move or click too; hookClicks is then unnecessary");
    }

    /// <summary>
    /// Every button event ChinPad sends: log it; on a down also beep and show it in the tooltip. ClickLock's lock
    /// moment: a double beep (even with clickBeep off: it says when the press may end) and "left button locked".
    /// </summary>
    void OnClickNotice(ClickNotice n)
    {
        _log.Write(n.Text);
        if (n.Lock)
        {
            BeepTwice(n.Button);
        }
        else if (n.Down)
        {
            _tray.SetLastClick(n.Button, DateTime.Now);
            if (_settings.ClickBeep) Beep(n.Button);
            if (_clicks.LockPending) _lockTimer.Start();
        }
        _tray.SetLocked(_clicks.LockedButton);
    }

    /// <summary>The tray tooltip's line while ClickLock keeps a button down ("left button locked"), else "".</summary>

    static int BeepFrequency(MouseButton b) => b switch { MouseButton.Left => 1200, MouseButton.Right => 800, _ => 1000 };

    /// <summary>~60 ms beep, higher for left than for right. Console.Beep blocks for its duration, so it runs on the thread pool.</summary>
    static void Beep(MouseButton b)
    {
        if (Silent) return;
        ThreadPool.QueueUserWorkItem(static f =>
        {
            try { Console.Beep(f, 60); } catch (Exception) { } // no sound device: no beep
        }, BeepFrequency(b), preferLocal: false);
    }

    /// <summary>ClickLock locked the button: two short tones (50 ms, 60 ms apart), on the thread pool.</summary>
    static void BeepTwice(MouseButton b)
    {
        if (Silent) return;
        ThreadPool.QueueUserWorkItem(static f =>
        {
            try
            {
                Console.Beep(f, 50);
                Thread.Sleep(60);
                Console.Beep(f, 50);
            }
            catch (Exception)
            {
                // no sound device: no beep
            }
        }, BeepFrequency(b) + 200, preferLocal: false);
    }

    // ------------------------------------------------------------------------------------------------
    // Start at login and the menu state
    // ------------------------------------------------------------------------------------------------

    void RefreshMenu()
    {
        bool startAtLogin;
        try { startAtLogin = StartAtLogin.IsEnabled(_exePath); }
        catch (Exception) { startAtLogin = false; }
        _tray.SetMenuState(startAtLogin, File.Exists(_readmePath));
    }

    string DescribeStartAtLogin()
    {
        try
        {
            string? current = StartAtLogin.Current();
            if (current is null) return "off";
            return StartAtLogin.IsEnabled(_exePath) ? "on (HKCU Run key \"ChinPad\")" : $"the HKCU Run key \"ChinPad\" starts another exe: {current}";
        }
        catch (Exception ex)
        {
            return "cannot read the Run key: " + ex.Message;
        }
    }

    bool StartAtLoginOn()
    {
        try { return StartAtLogin.IsEnabled(_exePath); }
        catch (Exception) { return false; }
    }

    void ToggleStartAtLogin() => SetStartAtLogin(!StartAtLoginOn());

    void SetStartAtLogin(bool on)
    {
        try
        {
            if (!on)
            {
                if (StartAtLogin.Current() is null) return;
                StartAtLogin.Disable();
                _log.Write("start at login turned off (HKCU Run key value \"ChinPad\" removed)");
            }
            else if (!StartAtLogin.IsEnabled(_exePath))
            {
                string? previous = StartAtLogin.Current();
                StartAtLogin.Enable(_exePath);
                _log.Write($"start at login turned on: HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Run \"ChinPad\" = {StartAtLogin.CommandFor(_exePath)}" +
                           (previous is null ? "" : $" (it was {previous})"));
            }
        }
        catch (Exception ex)
        {
            _log.Write("start at login could not be changed: " + ex.Message);
        }
        RefreshMenu();
        StateChanged?.Invoke();
    }

    // ------------------------------------------------------------------------------------------------
    // Settings window (ISettingsHost) and the diagnostic bundle (DiagnosticBundle.ISource)
    // ------------------------------------------------------------------------------------------------

    bool WizardOwnsInput => _wizardCaptures && Clock.NowMs - Volatile.Read(ref _wizardContactMs) < 150;

    List<WizardPad> WizardPads()
    {
        List<TouchpadChoice> choices = Choices();
        return choices.Select(c =>
        {
            TouchpadDevice d = _all.Single(d => d.Index == c.Index);
            FingerCollection? f = d.Fingers.FirstOrDefault();
            return new WizardPad(c.Index, c.Path, c.Device, PadGeometry.FromFields(f?.X, f?.Y, d.ScanTime), c.Number);
        }).ToList();
    }

    void ISettingsHost.ShowWizard() => ShowWizard();

    void ShowWizard()
    {
        if (_stopped) return;
        if (_wizard is { IsDisposed: false }) { _wizard.Show(); _wizard.Activate(); return; }
        _bundle?.Cancel();
        _form?.Hide();
        if (!_startupWizardPause) _pausedBeforeWizard = _engine.Paused;
        _startupWizardPause = false;
        var logic = new WizardLogic(WizardPads(), _settings, Screen.PrimaryScreen?.Bounds.Width ?? 1920, Clock.NowMs);
        var form = new WizardForm(this, logic);
        _wizard = form;
        _wizardCaptures = true;
        SetPaused(true, "first-time setup");
        SelectPads(keepIfSame: true);
        ApplyHook();
        logic.ClickAccepted += Beep;
        logic.DiagnosticsProduced += SaveWizardDiagnostics;
        form.TryOutChanged += trying =>
        {
            _wizardCaptures = !trying;
            SetPaused(!trying, "first-time setup try-out");
        };
        form.Finished += () =>
        {
            SaveWizardDiagnostics(logic.DiagnosticText());
            logic.ClickAccepted -= Beep;
            logic.DiagnosticsProduced -= SaveWizardDiagnostics;
            _wizard = null;
            _wizardCaptures = false;
            SelectPads(keepIfSame: true);
            SetPaused(_pausedBeforeWizard, "first-time setup closed");
            ApplyHook();
        };
        _log.Write("first-time setup opened");
        form.Show(); // modeless: --seconds and the tray Exit continue to work
    }

    void SaveWizardDiagnostics(string text)
    {
        try { File.WriteAllText(Path.Combine(_paths.Folder, "wizard-measurements.json"), text); }
        catch (Exception ex) { _log.Write("wizard measurements could not be saved: " + ex.Message); }
    }

    void ShowAboutWindow()
    {
        if (_stopped) return;
        try
        {
            if (_about is null || _about.IsDisposed) _about = new AboutForm(_paths);
            _about.Show();
            _about.Activate();
        }
        catch (Exception ex)
        {
            _log.Write("the About window could not be opened: " + ex);
        }
    }

    void ShowSettingsWindow()
    {
        if (_stopped) return;
        if (_wizard is { IsDisposed: false }) { _wizard.Activate(); return; }
        try
        {
            if (_form is null || _form.IsDisposed) _form = new SettingsForm(this, TrayIcon.MakeAppIcon());
            _form.ShowAndActivate();
        }
        catch (Exception ex)
        {
            _log.Write("the settings window could not be opened: " + ex);
        }
    }

    void StartBundle()
    {
        if (_stopped) return;
        if (_bundle is not null)
        {
            _bundle.BringToFront();
            return;
        }
        try
        {
            var bundle = new DiagnosticBundle(this);
            bundle.Finished += _ => _bundle = null;
            _bundle = bundle;
            bundle.Start();
        }
        catch (Exception ex)
        {
            _bundle = null;
            _recording = null;
            _log.Write("the diagnostic bundle could not be started: " + ex.Message);
            AppDialog.Show(null, L10n.F("Bundle.StartFailed", L10n.Error(ex)), "ChinPad");
        }
    }

    /// <summary>The settings window's live view: per pad its touches (mm, state) and last press, plus the counters.</summary>
    public LiveState Live()
    {
        var pads = new List<PadSnapshot>(_pads.Count);
        List<TouchpadChoice>? choices = null;
        foreach (Pad p in _pads.Values.OrderBy(p => p.Dev.Index))
        {
            _infos.Clear();
            _clicks.Contacts(p.Dev.Handle, _infos);
            var dots = new List<ContactDot>(_infos.Count);
            foreach (ContactInfo c in _infos.OrderBy(c => c.Id))
            {
                (double x, double y) = p.Geometry.Mm(c.X, c.Y);
                DotState state = !c.Mine ? DotState.Windows
                    : !c.Decided ? DotState.Undecided
                    : c.Zone is not null ? DotState.Zone
                    : _engine.IsDriver(p.Dev.Handle, c.Id) ? DotState.Moving
                    : DotState.Move;
                dots.Add(new ContactDot(c.Id, x, y, state, c.Zone?.Action));
            }
            PressMark? press = null;
            if (_clicks.LastPress is PressInfo lp && lp.Device == p.Dev.Handle)
            {
                (double px, double py) = p.Geometry.Mm(lp.X, lp.Y);
                press = new PressMark(lp.HasPosition, px, py, lp.AtMs, lp.Outcome);
            }
            choices ??= Choices();
            string label = choices.Find(c => c.Index == p.Dev.Index)?.Label ?? "";
            pads.Add(new PadSnapshot(p.Dev.Index, p.Dev.ShortName, p.Geometry, p.Reports, p.LastContactMs, dots, press, label));
        }
        return new LiveState(pads, _engine.Paused, _clicks.LeftClicks, _clicks.RightClicks, _clicks.MiddleClicks, _hook.DroppedClicks,
            UiProblem, _clicks.LockedButton);
    }

    /// <summary>The "Apply to" entries: every touchpad found, built-in ones first, with "reporting now" for the last 2 s.</summary>
    List<TouchpadChoice> Choices()
    {
        double now = Clock.NowMs;
        return PadChoice.Choices(_all,
            d => _lastReportMs.TryGetValue(d.Handle, out double t) && now - t < PadChoice.ReportingMs,
            d => _pads.ContainsKey(d.Handle));
    }

    AppSettings ISettingsHost.Settings => _settings;
    AppSettings DiagnosticBundle.ISource.Settings => _settings;
    public SettingsPaths Paths => _paths;
    bool ISettingsHost.Paused => _engine.Paused;
    void ISettingsHost.SetPaused(bool paused, string source) => SetPaused(paused, source);
    string ISettingsHost.HotkeyProblem => UiHotkeyProblem;
    bool ISettingsHost.StartAtLoginEnabled => StartAtLoginOn();
    void ISettingsHost.SetStartAtLogin(bool on) => SetStartAtLogin(on);
    void ISettingsHost.SaveDiagnosticBundle() => StartBundle();
    IReadOnlyList<TouchpadChoice> ISettingsHost.Touchpads() => Choices();
    void ISettingsHost.RefreshTouchpads() => Reenumerate("Refresh in the settings window");
    void ISettingsHost.OpenFile(string path) => OpenFile(path);

    void ISettingsHost.SuspendHotkey(bool suspended)
    {
        if (suspended == _hotkeySuspended) return;
        _hotkeySuspended = suspended;
        if (suspended)
        {
            _hotkeyBeforeSuspend = _hotkeyText;
            ApplyHotkey(quiet: true);
            return;
        }
        // Log the registration only when the window recorded a different key.
        string now = HotkeySpec.TryParse(_settings.Hotkey, out HotkeySpec spec, out _) ? spec.ToString() : _settings.Hotkey;
        ApplyHotkey(quiet: now == _hotkeyBeforeSuspend);
    }

    void ISettingsHost.WriteSettings(AppSettings settings, string why)
    {
        try { AppSettings.Save(_settingsPath, settings); }
        catch (Exception ex)
        {
            _log.Write("settings could not be saved: " + ex);
            throw;
        }
        _selfWrite = why;
        // Setup values must be reloaded before its next step / try-out can use them.
        if (settings.Language != _settings.Language || why == "first-time setup")
        {
            _debounce.Stop();
            _selfWrite = null;
            ReloadSettings("written by the " + why);
        }
    }

    void DiagnosticBundle.ISource.Record(ReportRecorder? recorder) => _recording = recorder;
    void DiagnosticBundle.ISource.Log(string line) => _log.Write(line);

    string DiagnosticBundle.ISource.DefaultFolder
    {
        get
        {
            if (_paths.Portable) return _paths.Folder;
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            return desktop.Length > 0 ? desktop : _paths.Folder;
        }
    }

    string DiagnosticBundle.ISource.CapsDump() => TouchpadDevice.DumpAll(_all, _enumErrors, _settings, "in use, " + _settingsPath);

    /// <summary>system.txt of the bundle: versions, displays, settings location, the pads and every counter.</summary>
    string DiagnosticBundle.ISource.SystemText(string recording)
    {
        var sb = new StringBuilder();
        void Line(string s) => sb.Append(s).Append("\r\n");
        TimeSpan up = DateTime.Now - _startedAt;
        Line($"ChinPad {Version} diagnostic bundle, made {DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", Inv)} (local time)");
        Line("ChinPad did not send this anywhere; it is a file on this computer until someone passes it on.");
        Line("");
        Line($"ChinPad: {_exePath}, pid {Environment.ProcessId}, running since {_startedAt.ToString("yyyy-MM-dd HH:mm:ss", Inv)} ({(int)up.TotalHours}:{up.Minutes:00}:{up.Seconds:00}), .NET {Environment.Version}, {(Environment.Is64BitProcess ? "64" : "32")}-bit");
        Line("Windows: " + DiagnosticBundle.WindowsText());
        Line("Settings: " + _paths.Describe());
        Line($"  strategy {_settings.Strategy}, zoneMode {_settings.ZoneMode}, hookClicks {(_settings.HookClicks ? "true" : "false")}, mergeMm {_settings.MergeMm.ToString("0.#", Inv)}, zoneSettleMs {_settings.ZoneSettleMs.ToString("0.#", Inv)}, speedPxPerMm {_settings.SpeedPxPerMm.ToString("0.##", Inv)}, moveMode {_settings.MoveMode}");
        Line(string.Create(Inv, $"  clickLock {(_settings.ClickLock.Enabled ? $"on ({_settings.ClickLock.HoldMs:0} ms)" : "off")}, blockDoubleClick {(_settings.BlockDoubleClick ? $"on ({_clicks.DoubleClickWindowMs:0} ms{(_settings.DoubleClickWindowMs == 0 ? ", Windows' double-click time" : "")})" : "off")}; locked now: {(_clicks.LockedButton is MouseButton lb ? ClickEngine.Name(lb) : "none")}"));
        Line($"  in use: {_settings.ToCompactJson()}");
        Line($"State: {(_engine.Paused ? "paused" : "active")}; pause key: {(_hotkeyText.Length > 0 ? _hotkeyText : "none")}{(_hotkeyProblem.Length > 0 ? " (" + _hotkeyProblem + ")" : "")}; start at login: {DescribeStartAtLogin()}; problem: {_rawInputProblem ?? _deviceProblem ?? _hookProblem ?? "none"}");
        Line("Displays:");
        foreach (string d in DiagnosticBundle.DisplayLines()) Line("  " + d);
        Line("Mouse (Windows): " + DiagnosticBundle.MouseText());
        Line("Touchpad settings (Windows, HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\PrecisionTouchPad):");
        foreach (string t in DiagnosticBundle.TouchpadSettingsLines()) Line("  " + t);
        Line(string.Create(Inv, $"Touchpads: {_all.Count} found, {_pads.Count} in use (settings \"device\" = \"{_settings.Device}\"{(_deviceFallback is null ? "" : "; not connected, so all touchpads are used")})"));
        foreach (TouchpadChoice c in Choices())
        {
            FingerCollection? fields = _all.Find(d => d.Index == c.Index)?.Fingers.FirstOrDefault();
            string label = PadChoice.Label(c.Number, c.Path, fields?.X?.PhysicalSpanMm, fields?.Y?.PhysicalSpanMm, english: true);
            Line($"  in the settings window: {label} = \"device\": \"{c.Device}\" (#{c.Index.ToString(Inv)}{(c.InUse ? ", in use" : "")})");
        }
        foreach (TouchpadDevice d in _all)
        {
            Pad? p = _pads.Values.FirstOrDefault(x => x.Dev == d);
            if (p is null)
            {
                Line($"  #{d.Index} {d.ShortName}: not in use");
                continue;
            }
            PadGeometry g = p.Geometry;
            Line(string.Create(Inv,
                $"  #{d.Index} {d.ShortName}: {g.Note}; logical span {g.SpanX} x {g.SpanY}, {g.MmPerUnitX:0.#####} x {g.MmPerUnitY:0.#####} mm per unit; {p.Reports} reports, {p.Asm.CompletedFrames} frames, {p.Asm.IncompleteFrames} incomplete, {p.Errors} errors"));
            foreach (string z in ClickEngine.DescribeZones(_settings.Zones, g)) Line("     " + z);
        }
        foreach (string e in _enumErrors) Line("  could not open: " + e);
        Line("Counters since start:");
        Line(string.Create(Inv, $"  cursor: {_engine.Frames} frames, {_engine.InjectedMoves} moves ({_engine.InjectedPixels} px), {_engine.MergeHandOvers} hand-overs to a merged partner, driver now {_engine.DriverText}"));
        Line("  " + _clicks.CountersText);
        Line("  merging: " + _clicks.MergeText);
        Line(string.Create(Inv, $"  Windows' own input dropped: {_hook.Dropped} moves, {_hook.DroppedClicks} clicks; SendInput failures {_mover.Failures}; reports from touchpads not in use {_otherReports}"));
        if (_clicks.PressesIgnored > 0) Line("  last ignored press: " + _clicks.LastIgnoredReason);
        string wizardPath = Path.Combine(_paths.Folder, "wizard-measurements.json");
        if (File.Exists(wizardPath))
        {
            try { Line("First-time setup measurements (including firmware confidence):"); Line(File.ReadAllText(wizardPath)); }
            catch (Exception ex) { Line("First-time setup measurements unavailable: " + ex.Message); }
        }
        Line("Recording: " + recording);
        return sb.ToString();
    }

    /// <summary>Opens a file with its associated program; Notepad when there is none.</summary>
    void OpenFile(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            try
            {
                Process.Start(new ProcessStartInfo("notepad.exe", "\"" + path + "\"") { UseShellExecute = true });
            }
            catch (Exception ex2)
            {
                _log.Write($"cannot open {path}: {ex.Message} / {ex2.Message}");
            }
        }
    }
}

/// <summary>chinpad.log (in the settings folder): appended line by line, emptied at start when over 1 MB. Thread-safe, never throws.</summary>
internal sealed class FileLog
{
    const long MaxBytes = 1024 * 1024;
    static readonly UTF8Encoding Utf8 = new(false);
    readonly object _lock = new();

    public string Path { get; }

    public FileLog(string path)
    {
        Path = path;
        try
        {
            var info = new FileInfo(path);
            if (info.Exists && info.Length > MaxBytes)
            {
                File.WriteAllText(path, "", Utf8);
                Write(string.Create(CultureInfo.InvariantCulture, $"log emptied (it had grown to {info.Length} bytes)"));
            }
        }
        catch (Exception)
        {
            // logging is best effort
        }
    }

    public void Write(string message)
    {
        string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture) + "  " + message + Environment.NewLine;
        lock (_lock)
        {
            try { File.AppendAllText(Path, line, Utf8); } catch (Exception) { }
        }
    }
}
