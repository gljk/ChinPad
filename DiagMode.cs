using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace ChinPad;

/// <summary>
/// Phase 1, "ChinPad.exe --diag": shows what the touchpad firmware really reports for a finger and for a
/// chin, and logs every report to CSV for later analysis.
///  - caps dump for every touchpad (printed and written to diag-caps.txt),
///  - live table per contact ID: tip, confidence, position, age, ms since confidence last changed, and
///    the button zone of the touch (settings "zones", "zoneSettleMs" and "zoneMode", the background mode's
///    classification rule: "undecided" while the touch settles),
///  - rate mode (D): reports/s and frames/s per device, to see whether raw reports still arrive with the
///    touchpad switched Off in Settings,
///  - F / C write "finger" / "chin" marker rows into the CSV, Q / Esc flush the CSV and quit.
/// Everything runs on the UI thread (WM_INPUT, WinForms timers, keys marshalled from the key thread),
/// so only the CSV writer (ReportRecorder) needs a lock: the console-close handler may flush it from
/// another thread. The CSV and diag-caps.txt go where settings.json is (SettingsPaths).
/// </summary>
internal sealed class DiagMode : IDisposable
{
    const double LiftedVisibleMs = 1000;  // ended contacts stay on screen (greyed) this long
    const double StaleMs = 300;           // a contact that is down but has not been reported for this long
    const string KeyLegend = "Keys: F = 'finger' marker   C = 'chin' marker   D = rate mode on/off   Q or Esc = save and quit";
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>Per-touchpad counters and the assembler that owns its frames and contact tracks.</summary>
    sealed class DevState(TouchpadDevice dev)
    {
        public TouchpadDevice Dev { get; } = dev;
        public FrameAssembler Asm { get; } = new(dev.ContactCount is not null);
        public PadGeometry Geometry { get; } = PadGeometry.FromFields(dev.Fingers.FirstOrDefault()?.X, dev.Fingers.FirstOrDefault()?.Y, dev.ScanTime);
        public long Reports;
        public long ReportsAtTick, FramesAtTick;
        public double ReportRate, FrameRate;
        public byte LastReportId;
        public int? LastScan;
        public uint LastButtons;
        public long Errors;
        public string? LastError;
    }

    readonly List<DevState> _devs = [];
    readonly Dictionary<IntPtr, DevState> _byHandle = [];
    readonly InputWindow _window;
    readonly string _outDir;
    readonly string _dump;
    readonly bool _plain;     // output redirected: no cursor positioning, one status line per second
    readonly int? _seconds;
    readonly ReportRecorder _rec;
    readonly Control _marshal = new(); // hidden control: BeginInvoke target for the key thread
    readonly List<System.Windows.Forms.Timer> _timers = [];
    string _csvPath = "";
    long _unknownReports;
    bool _rateMode;
    volatile bool _keysUnavailable;
    bool _exitRequested, _finished;
    string _exitReason = "";
    string _status = "Yellow rows: tip on but confidence off (what Windows ignores as a palm).";
    int _regionTop = -1, _regionHeight, _winW, _winH;
    double _lastRateMs;
    PosixSignalRegistration? _consoleClose;

    /// <param name="outDir">Where diag-caps.txt and the CSV go (the settings folder).</param>
    public DiagMode(IReadOnlyList<TouchpadDevice> devices, InputWindow window, string outDir, string capsDump, bool plain, int? seconds,
        AppSettings settings)
    {
        _rec = new ReportRecorder(settings);
        _window = window;
        _outDir = outDir;
        _dump = capsDump;
        _plain = plain;
        _seconds = seconds;
        foreach (TouchpadDevice d in devices)
        {
            var s = new DevState(d);
            _devs.Add(s);
            _byHandle[d.Handle] = s;
        }
    }

    /// <summary>Prints the dump, opens the CSV, registers raw input and starts timers and the key thread.</summary>
    public bool Start()
    {
        Console.Write(_dump);
        string capsPath = Path.Combine(_outDir, "diag-caps.txt");
        try
        {
            File.WriteAllText(capsPath, _dump, new UTF8Encoding(false));
            Console.WriteLine("Caps dump saved to " + capsPath);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Could not write {capsPath}: {ex.Message}");
        }

        Console.WriteLine();
        Console.WriteLine(KeyLegend);
        Console.WriteLine("Live table: one row per contact ID. Yellow = tip on, confidence off. Grey = lifted (shown 1 s).");

        if (_devs.Count == 0)
        {
            Console.WriteLine("No touchpad (usage page 0x0D, usage 0x05) was found, so there is nothing to log.");
            return false;
        }

        _csvPath = Path.Combine(_outDir, "chinpad-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", Inv) + ".csv");
        _rec.Open(_csvPath);
        Console.WriteLine("CSV log: " + _csvPath);

        _ = _marshal.Handle; // create the marshalling window now, on this (UI) thread
        _window.HidReport += OnHidReport;
        _window.Register(TouchpadDevice.PageDigitizer, TouchpadDevice.UsageTouchPad);

        AddTimer(50, OnDrawTick);    // ~20 Hz live view
        AddTimer(1000, OnRateTick);  // rates, CSV flush, rate view / status line
        if (_seconds is int s)
            AddTimer(s * 1000, () => RequestExit($"--seconds {s} elapsed"));

        Console.CancelKeyPress += OnCancelKeyPress;
        try
        {
            // Closing the console window (CTRL_CLOSE_EVENT, mapped to SIGHUP by .NET) kills the process
            // after the handler returns: flush what we have.
            _consoleClose = PosixSignalRegistration.Create(PosixSignal.SIGHUP, _ => FlushCsv());
        }
        catch (Exception)
        {
            // not available: Q / Esc / Ctrl+C still flush
        }
        new Thread(KeyLoop) { IsBackground = true, Name = "ChinPad keys" }.Start();

        _lastRateMs = Clock.NowMs;
        if (_plain)
        {
            Console.WriteLine("Output is redirected: no live table, one status line per second instead." +
                              (_seconds is null ? " Use --seconds N to stop automatically." : ""));
        }
        else
        {
            try { Console.CursorVisible = false; } catch (Exception) { }
            Draw();
        }
        return true;
    }

    /// <summary>Called after the message loop ended: stop everything, close the CSV, print a summary.</summary>
    public void Finish()
    {
        if (_finished) return;
        _finished = true;
        foreach (System.Windows.Forms.Timer t in _timers)
        {
            t.Stop();
            t.Dispose();
        }
        _timers.Clear();
        Console.CancelKeyPress -= OnCancelKeyPress;
        _window.HidReport -= OnHidReport;
        _window.Unregister();
        CloseCsv();

        if (!_plain)
        {
            try
            {
                Console.ResetColor();
                if (_regionTop >= 0)
                    Console.SetCursorPosition(0, Math.Min(_regionTop + _regionHeight, Console.BufferHeight - 1));
                Console.CursorVisible = true;
            }
            catch (Exception) { }
        }
        Console.WriteLine();
        Console.WriteLine($"ChinPad diag stopped ({_exitReason}). CSV: {_csvPath} ({_rec.Rows} rows, {_rec.Markers} markers).");
        foreach (DevState d in _devs)
            Console.WriteLine(string.Create(Inv,
                $"  #{d.Dev.Index} {d.Dev.ShortName}: {d.Reports} reports, {d.Asm.CompletedFrames} frames, {d.Asm.IncompleteFrames} incomplete, {d.Errors} errors"));
        if (_unknownReports > 0)
            Console.WriteLine($"  {_unknownReports} reports from unknown raw-input handles were ignored (device plugged in after start?).");
    }

    public void Dispose()
    {
        foreach (System.Windows.Forms.Timer t in _timers) t.Dispose();
        _timers.Clear();
        CloseCsv();
        _consoleClose?.Dispose();
        _marshal.Dispose();
    }

    void AddTimer(int intervalMs, Action tick)
    {
        var t = new System.Windows.Forms.Timer { Interval = intervalMs };
        t.Tick += (_, _) => tick();
        t.Start();
        _timers.Add(t);
    }

    // ------------------------------------------------------------------------------------------------
    // Input
    // ------------------------------------------------------------------------------------------------

    void OnHidReport(IntPtr hDevice, byte[] report)
    {
        double now = Clock.NowMs;
        if (!_byHandle.TryGetValue(hDevice, out DevState? d))
        {
            _unknownReports++;
            return;
        }
        try
        {
            d.Reports++;
            ContactReport r = d.Dev.ParseReport(report);
            d.LastReportId = r.ReportId;
            d.LastScan = r.ScanTime ?? d.LastScan;
            bool pressed = (r.Buttons & ~d.LastButtons & 7u) != 0; // a press decides touches still settling
            d.LastButtons = r.Buttons;
            ReportOutcome o = d.Asm.Process(r, now);
            _rec.WriteReport(d.Dev.Index, d.Geometry, d.Asm, r, o, now, pressed);
        }
        catch (Exception ex)
        {
            // Never let one odd report stop the logging; the count shows up in the live header.
            d.Errors++;
            d.LastError = ex.Message;
        }
    }

    void KeyLoop()
    {
        try
        {
            while (true)
            {
                ConsoleKeyInfo key = Console.ReadKey(intercept: true);
                _marshal.BeginInvoke((MethodInvoker)(() => OnKey(key)));
            }
        }
        catch (Exception)
        {
            // No console input (redirected or detached) or shutting down: keys are simply unavailable.
            _keysUnavailable = true;
        }
    }

    void OnKey(ConsoleKeyInfo key)
    {
        switch (key.Key)
        {
            case ConsoleKey.F:
                Marker("finger");
                break;
            case ConsoleKey.C:
                Marker("chin");
                break;
            case ConsoleKey.D:
                _rateMode = !_rateMode;
                _status = _rateMode ? "rate mode" : "live view";
                if (_plain) Console.WriteLine(_status);
                Draw();
                break;
            case ConsoleKey.Q:
            case ConsoleKey.Escape:
                RequestExit(key.Key == ConsoleKey.Q ? "Q pressed" : "Esc pressed");
                break;
        }
    }

    void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
    {
        e.Cancel = true; // leave through the normal path so the CSV is flushed and closed
        try { _marshal.BeginInvoke((MethodInvoker)(() => RequestExit("Ctrl+C"))); } catch (Exception) { }
    }

    void RequestExit(string reason)
    {
        if (_exitRequested) return;
        _exitRequested = true;
        _exitReason = reason;
        FlushCsv();
        Application.ExitThread(); // ends Application.Run in Program.Main
    }

    // ------------------------------------------------------------------------------------------------
    // CSV (ReportRecorder writes the rows)
    // ------------------------------------------------------------------------------------------------

    /// <summary>The live table's zone column: what the CSV rows decided so far.</summary>
    string ZoneText(ContactTrack t) => _rec.ZoneText(t);

    void Marker(string name)
    {
        double t = Clock.NowMs;
        _rec.Marker(name, t);
        _status = string.Create(Inv, $"marker '{name}' written at t={t / 1000:0.0} s");
        if (_plain) Console.WriteLine(_status);
    }

    void FlushCsv() => _rec.Flush();

    void CloseCsv() => _rec.Close();

    // ------------------------------------------------------------------------------------------------
    // Timers and drawing
    // ------------------------------------------------------------------------------------------------

    void OnDrawTick()
    {
        double now = Clock.NowMs;
        foreach (DevState d in _devs) d.Asm.Prune(now, LiftedVisibleMs);
        if (!_plain && !_rateMode) Draw();
    }

    void OnRateTick()
    {
        double now = Clock.NowMs;
        double seconds = Math.Max(0.001, (now - _lastRateMs) / 1000.0);
        _lastRateMs = now;
        foreach (DevState d in _devs)
        {
            d.ReportRate = (d.Reports - d.ReportsAtTick) / seconds;
            d.ReportsAtTick = d.Reports;
            long frames = d.Asm.CompletedFrames;
            d.FrameRate = (frames - d.FramesAtTick) / seconds;
            d.FramesAtTick = frames;
        }
        FlushCsv();
        if (_plain) Console.WriteLine(StatusLine(now));
        else if (_rateMode) Draw();
    }

    string StatusLine(double now)
    {
        var sb = new StringBuilder(string.Create(Inv, $"t={now / 1000,6:0.0}s"));
        foreach (DevState d in _devs)
            sb.Append(string.Create(Inv, $"  | #{d.Dev.Index} {Trunc(d.Dev.ShortName, 26)}: {d.ReportRate,4:0} rep/s {d.FrameRate,4:0} fr/s ({d.Reports} reports)"));
        sb.Append(string.Create(Inv, $"  | csv rows {_rec.Rows}"));
        return sb.ToString();
    }

    /// <summary>
    /// Redraws the fixed screen region in place with Console.SetCursorPosition. Every line is padded to
    /// the full width so nothing from the previous frame survives. Console errors (window resized very
    /// small, console closing) only skip this frame.
    /// </summary>
    void Draw()
    {
        if (_plain) return;
        try
        {
            EnsureRegion();
            int width = Math.Clamp(_winW - 1, 20, 120);
            var lines = new List<(string Text, ConsoleColor? Color)>(_regionHeight);
            if (_rateMode) BuildRateView(lines);
            else BuildLiveView(lines, Clock.NowMs);

            for (int i = 0; i < _regionHeight; i++)
            {
                string text;
                ConsoleColor? color;
                if (i == _regionHeight - 1)
                {
                    text = KeyLegend + (_keysUnavailable ? "  (keys unavailable: no console input)" : "");
                    color = ConsoleColor.Cyan;
                }
                else if (i < lines.Count)
                {
                    (text, color) = lines[i];
                }
                else
                {
                    (text, color) = ("", null);
                }
                Console.SetCursorPosition(0, _regionTop + i);
                if (color is ConsoleColor c) Console.ForegroundColor = c;
                Console.Write(Fit(text, width));
                if (color is not null) Console.ResetColor();
            }
            Console.SetCursorPosition(0, _regionTop + _regionHeight - 1);
        }
        catch (Exception)
        {
            // console too small, resized or closing: skip this frame
        }
    }

    /// <summary>
    /// The first time, reserve the region below the caps dump by printing blank lines (the dump stays in
    /// the scroll-back and in diag-caps.txt). After a window resize, clear and start at the top.
    /// </summary>
    void EnsureRegion()
    {
        int w = Console.WindowWidth, h = Console.WindowHeight;
        if (_regionTop >= 0 && w == _winW && h == _winH) return;
        bool resized = _regionTop >= 0;
        _winW = w;
        _winH = h;
        _regionHeight = Math.Clamp(h - 1, 6, 32);
        if (resized)
        {
            Console.Clear();
            _regionTop = 0;
            return;
        }
        for (int i = 0; i < _regionHeight; i++) Console.WriteLine();
        _regionTop = Math.Max(0, Console.CursorTop - _regionHeight);
    }

    void BuildLiveView(List<(string, ConsoleColor?)> lines, double now)
    {
        lines.Add((string.Create(Inv,
            $"ChinPad diag (live)   t={now / 1000:0.0}s   CSV {Path.GetFileName(_csvPath)}: {_rec.Rows} rows, {_rec.Markers} markers"), ConsoleColor.White));
        foreach (DevState d in _devs)
        {
            Frame? f = d.Asm.LastFrame;
            string count = f?.DeclaredCount?.ToString(Inv) ?? "-";
            string scan = d.LastScan?.ToString(Inv) ?? "-";
            string problems = "";
            if (d.Errors > 0) problems += string.Create(Inv, $"  errors {d.Errors}: {d.LastError}");
            if (d.Dev.UnexpectedStatusCount > 0) problems += string.Create(Inv, $"  hidp 0x{d.Dev.LastUnexpectedStatus:X8} x{d.Dev.UnexpectedStatusCount}");
            lines.Add((string.Create(Inv,
                $"#{d.Dev.Index} {Trunc(d.Dev.ShortName, 26),-26} {d.ReportRate,4:0} rep/s {d.FrameRate,4:0} fr/s  count {count,-2} scan {scan,-5}  btn {d.LastButtons}  rid {d.LastReportId}  incompl {d.Asm.IncompleteFrames}{problems}"),
                d.Errors > 0 ? ConsoleColor.Red : (ConsoleColor?)null));
        }
        lines.Add((_status, ConsoleColor.Yellow));
        lines.Add((string.Create(Inv,
            $"  {"dev",-4} {"id",3} {"tip",3} {"conf",4} {"x",6} {"y",6} {"w",6} {"h",6} {"age_ms",8} {"conf_ms",8} {"flips",5} {"zone",-9}  state"), ConsoleColor.Gray));

        var rows = _devs
            .SelectMany(d => d.Asm.Tracks.Values.Select(t => (Dev: d.Dev.Index, State: d, Track: t)))
            .OrderBy(r => r.Track.Lifted).ThenBy(r => r.Dev).ThenBy(r => r.Track.ContactId)
            .ToList();
        int room = Math.Max(1, _regionHeight - 1 - lines.Count); // the last line is the key legend
        if (rows.Count == 0) lines.Add(("  (no contacts: touch the pad)", ConsoleColor.DarkGray));
        for (int i = 0; i < rows.Count; i++)
        {
            if (rows.Count > room && i == room - 1)
            {
                lines.Add((string.Create(Inv, $"  ... {rows.Count - i} more"), ConsoleColor.DarkGray));
                break;
            }
            (int dev, _, ContactTrack t) = rows[i];
            string zone = ZoneText(t);
            double silent = now - t.LastSeenMs;
            string state;
            ConsoleColor color;
            if (t.Lifted)
            {
                state = t.Vanished ? "VANISHED (no tip-off)" : "lifted";
                color = ConsoleColor.DarkGray;
            }
            else if (silent > StaleMs)
            {
                state = string.Create(Inv, $"STALE (no report {silent:0}ms)");
                color = ConsoleColor.DarkYellow;
            }
            else if (t.Tip && !t.Confidence)
            {
                state = "down, confidence 0";
                color = ConsoleColor.Yellow;
            }
            else
            {
                state = "down";
                color = ConsoleColor.Green;
            }
            lines.Add((string.Create(Inv,
                $"  #{dev,-3} {t.ContactId,3} {B(t.Tip),3} {B(t.Confidence),4} {t.X,6} {t.Y,6} {Opt(t.W),6} {Opt(t.H),6} {t.AgeMs(now),8:0} {t.MsSinceConfidenceChange(now),8:0} {t.ConfidenceFlips,5} {zone,-9}  {state}"),
                color));
        }
    }

    void BuildRateView(List<(string, ConsoleColor?)> lines)
    {
        lines.Add(("RATE MODE: raw reports per second, updated every second   (D = back to the live table)", ConsoleColor.White));
        lines.Add(("Turn the touchpad Off in Settings > Bluetooth & devices > Touchpad, then touch the pad.", ConsoleColor.Gray));
        lines.Add(("If reports/s still moves, raw reports arrive even with the touchpad Off.", ConsoleColor.Gray));
        lines.Add(("", null));
        foreach (DevState d in _devs)
        {
            lines.Add((string.Create(Inv,
                $"#{d.Dev.Index} {d.Dev.ShortName}   total {d.Reports} reports, {d.Asm.CompletedFrames} frames"), ConsoleColor.White));
            lines.Add(("   reports/s                         frames/s", ConsoleColor.Gray));
            string[] a = BigNumber((long)Math.Round(d.ReportRate));
            string[] b = BigNumber((long)Math.Round(d.FrameRate));
            ConsoleColor color = d.ReportRate > 0 ? ConsoleColor.Green : ConsoleColor.DarkGray;
            for (int r = 0; r < a.Length; r++)
                lines.Add(("   " + a[r].PadRight(34) + b[r], color));
            lines.Add(("", null));
        }
    }

    // 3x5 digit font, drawn two characters per pixel so the numbers are readable from a distance.
    static readonly string[][] Digits =
    [
        ["###", "# #", "# #", "# #", "###"],
        [" # ", "## ", " # ", " # ", "###"],
        ["###", "  #", "###", "#  ", "###"],
        ["###", "  #", "###", "  #", "###"],
        ["# #", "# #", "###", "  #", "  #"],
        ["###", "#  ", "###", "  #", "###"],
        ["###", "#  ", "###", "# #", "###"],
        ["###", "  #", "  #", "  #", "  #"],
        ["###", "# #", "###", "# #", "###"],
        ["###", "# #", "###", "  #", "###"],
    ];

    static string[] BigNumber(long value)
    {
        string s = Math.Clamp(value, 0, 9999).ToString(Inv);
        var rows = new string[5];
        for (int r = 0; r < 5; r++)
            rows[r] = string.Join("  ", s.Select(ch => Digits[ch - '0'][r].Replace("#", "##").Replace(" ", "  ")));
        return rows;
    }

    static string Fit(string s, int width) => s.Length >= width ? s[..width] : s.PadRight(width);
    static string Trunc(string s, int n) => s.Length <= n ? s : s[..n];
    static string Opt(int? v) => v?.ToString(Inv) ?? "-";
    static string B(bool v) => v ? "1" : "0";
}
