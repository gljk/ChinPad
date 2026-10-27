using System.Text.Json;

namespace ChinPad;

internal enum WizardStep
{
    Welcome, SelectPad, ChinTest, ChinVerdict, LeftButton, RightButton, Zones,
    Sweep, SweepResult, Still, ClickLeft, ClickRight, TryIt, Finish, NoPad, Closed,
}
internal enum ChinVerdict { NotMeasured, Good, JoinTouches, NotReported }
internal enum WizardKey { Continue, Back, Skip }
internal readonly record struct WizardPoint(double X, double Y);
internal readonly record struct WizardContact(int Id, double X, double Y, bool Tip, bool Confidence);
internal sealed record WizardPad(int Index, string Path, string Device, PadGeometry Geometry, int Number)
{
    public string Label => PadChoice.Label(Number, Path, Geometry.WidthMm, Geometry.HeightMm);
}
internal sealed record WizardFrame(int PadIndex, IReadOnlyList<WizardContact> Contacts, uint Buttons, double AtMs)
{
    public static WizardFrame From(int index, PadGeometry geometry, Frame frame, double atMs) => new(index,
        frame.Contacts.Select(c =>
        {
            (double x, double y) = geometry.Mm(c.X, c.Y);
            return new WizardContact(c.ContactId, x, y, c.Tip, c.Confidence);
        }).ToArray(), frame.Buttons, atMs);
}

/// <summary>
/// No controls, screen APIs, wall clock, input injection or file access. All times are monotonic milliseconds
/// supplied by the caller. Frames are complete FrameAssembler scans converted to millimetres. Tick also runs
/// when a pad is silent, so missing input cannot masquerade as a long chin contact or a successful hold.
/// </summary>
internal sealed class WizardLogic
{
    const double FreshMs = 100, HoldMs = 2000;
    readonly double _screenWidth;
    readonly Dictionary<int, WizardFrame> _latest = [];
    readonly Dictionary<int, double> _reported = [];
    readonly Dictionary<int, (double Start, double Last, bool Confidence)> _chinTracks = [];
    readonly List<double> _pairDistances = [], _sweeps = [];
    readonly List<WizardPoint> _still = [], _left = [], _right = [];
    IReadOnlyList<WizardPad> _pads;
    double _now, _entered, _lastFrame = double.NegativeInfinity, _holdStart = double.NaN;
    double _chinStart = double.NaN, _runStart = double.NaN, _stillStart = double.NaN;
    double _landingStart = double.NaN, _sweepStart, _sweepExtreme, _sweepLastMotion;
    WizardPoint? _settled;
    bool _liftRequired, _holdNeedsLift, _sweepActive, _latestDown;
    int _sweepDirection;
    uint _buttons;
    (int Index, double At)? _candidate;

    public WizardLogic(IReadOnlyList<WizardPad> pads, AppSettings settings, double screenWidthPixels, double nowMs)
    {
        _pads = pads;
        Settings = settings;
        _screenWidth = Math.Max(1, screenWidthPixels);
        _now = _entered = nowMs;
        Step = pads.Count == 0 ? WizardStep.NoPad : WizardStep.Welcome;
    }

    public event Action? Changed;
    public event Action<AppSettings>? SettingsProduced;
    public event Action<MouseButton>? ClickAccepted;
    public event Action<string>? DiagnosticsProduced;
    public WizardStep Step { get; private set; }
    public AppSettings Settings { get; private set; }
    public IReadOnlyList<WizardPad> Pads => _pads;
    public WizardPad? Selected { get; private set; }
    public IReadOnlyList<WizardPoint> LeftLandings => _left;
    public IReadOnlyList<WizardPoint> RightLandings => _right;
    public ChinVerdict Verdict { get; private set; }
    public int ContactsSeen { get; private set; }
    public int MaxContacts { get; private set; }
    public int ReportingGaps { get; private set; }
    public int PressesSeen { get; private set; }
    public int LowConfidenceSamples { get; private set; }
    public int ConfidenceFlips { get; private set; }
    public double LongestRunMs { get; private set; }
    public double TypicalDistanceMm { get; private set; }
    public double SweepSpanMm { get; private set; }
    public double JitterMm { get; private set; }
    public bool SplitFallback { get; private set; }
    public bool ZonesKept { get; private set; }
    public bool SweepTimedOut { get; private set; }
    public bool ClickTimedOut { get; private set; }
    public bool LeftClickPassed { get; private set; }
    public bool RightClickPassed { get; private set; }
    public int TargetsHit { get; private set; }
    public int SweepsCompleted => _sweeps.Count;
    public bool TryOut => Step == WizardStep.TryIt;
    public bool Waiting => Step is WizardStep.Welcome or WizardStep.ChinVerdict or WizardStep.Zones
        or WizardStep.SweepResult or WizardStep.TryIt or WizardStep.Finish or WizardStep.NoPad
        || Step == WizardStep.SelectPad && Selected is not null
        || Step is WizardStep.ClickLeft or WizardStep.ClickRight && ClickTimedOut;
    public bool CanContinue => Waiting;
    public bool CanBack => Step is not (WizardStep.Welcome or WizardStep.NoPad or WizardStep.Closed);
    public bool CanSkip => Step is not (WizardStep.Welcome or WizardStep.Finish or WizardStep.NoPad or WizardStep.Closed);
    public double HoldProgress => Waiting && !double.IsNaN(_holdStart) ? Math.Clamp((_now - _holdStart) / HoldMs, 0, 1) : 0;
    public int StepNumber => Step switch
    {
        WizardStep.Welcome => 1, WizardStep.SelectPad => 2, WizardStep.ChinTest or WizardStep.ChinVerdict => 3,
        WizardStep.LeftButton or WizardStep.RightButton => 4, WizardStep.Zones => 5,
        WizardStep.Sweep or WizardStep.SweepResult => 6, WizardStep.Still => 7,
        WizardStep.ClickLeft or WizardStep.ClickRight => 8, WizardStep.TryIt => 9, _ => 10,
    };
    public double SecondsRemaining => Step switch
    {
        WizardStep.ChinTest => Math.Max(0, (double.IsNaN(_chinStart) ? 20000 - (_now - _entered) : 5000 - (_now - _chinStart)) / 1000),
        WizardStep.Sweep => Math.Max(0, (30000 - (_now - _entered)) / 1000),
        WizardStep.Still => Math.Max(0, (double.IsNaN(_stillStart) ? 3000 : 3000 - (_now - _stillStart)) / 1000),
        WizardStep.ClickLeft or WizardStep.ClickRight => Math.Max(0, (15000 - (_now - _entered)) / 1000),
        _ => 0,
    };
    public double MeasurementProgress => Step switch
    {
        WizardStep.ChinTest => double.IsNaN(_chinStart) ? 0 : Math.Clamp((_now - _chinStart) / 5000, 0, 1),
        WizardStep.LeftButton => _left.Count / 3.0, WizardStep.RightButton => _right.Count / 3.0,
        WizardStep.Sweep => Math.Min(1, _sweeps.Count / 2.0),
        WizardStep.Still => double.IsNaN(_stillStart) ? 0 : Math.Clamp((_now - _stillStart) / 3000, 0, 1),
        WizardStep.ClickLeft or WizardStep.ClickRight => Math.Clamp((_now - _entered) / 15000, 0, 1),
        WizardStep.TryIt => TargetsHit / 3.0, _ => 0,
    };
    public bool Reporting(int index) => _reported.TryGetValue(index, out double at) && _now - at < PadChoice.ReportingMs;
    public WizardFrame? LiveFrame(int index) => _latest.TryGetValue(index, out WizardFrame? f) && _now - f.AtMs <= FreshMs ? f : null;

    public void UpdatePads(IReadOnlyList<WizardPad> pads, double atMs)
    {
        _now = Math.Max(_now, atMs);
        _pads = pads;
        if (pads.Count == 0 || Selected is not null && !pads.Any(p => p.Index == Selected.Index && p.Path == Selected.Path))
            Enter(WizardStep.NoPad);
        Changed?.Invoke();
    }

    public void OnFrame(WizardFrame frame)
    {
        if (frame.AtMs < _now || Step == WizardStep.Closed || !_pads.Any(p => p.Index == frame.PadIndex)) return;
        _now = frame.AtMs;
        _reported[frame.PadIndex] = _now;
        _latest[frame.PadIndex] = frame;
        var down = frame.Contacts.Where(c => c.Tip && double.IsFinite(c.X) && double.IsFinite(c.Y)).OrderBy(c => c.Id).ToArray();

        // Defer a multi-pad choice to the next Tick: reports within 30 ms count as simultaneous.
        // Preference is independent of Windows' enumeration / WM_INPUT delivery order.
        if (Step == WizardStep.SelectPad && Selected is null && down.Length > 0)
        {
            if (_candidate is null) _candidate = (frame.PadIndex, _now);
            else if (_now - _candidate.Value.At <= 30 && !PadChoice.IsExternal(_pads.Single(p => p.Index == frame.PadIndex).Path))
                _candidate = (frame.PadIndex, _candidate.Value.At);
        }
        if (Selected is not null && frame.PadIndex != Selected.Index) { Changed?.Invoke(); return; }
        bool wasDown = double.IsFinite(_lastFrame) && _now - _lastFrame <= FreshMs && _latestDown;
        bool gap = _latestDown && _now - _lastFrame > FreshMs;
        _lastFrame = _now;
        _latestDown = Selected is null
            ? _latest.Values.Any(f => _now - f.AtMs <= FreshMs && f.Contacts.Any(c => c.Tip))
            : down.Length > 0;
        if (!_latestDown) { _holdStart = double.NaN; _holdNeedsLift = false; }
        else if (Waiting && !_holdNeedsLift && (double.IsNaN(_holdStart) || !wasDown)) _holdStart = _now;
        uint pressed = frame.Buttons & ~_buttons;
        _buttons = frame.Buttons;
        if (Step == WizardStep.ChinTest && double.IsNaN(_chinStart) && _now - _entered >= 20000)
        {
            EndChin();
            Changed?.Invoke();
            return;
        }
        if (Step is WizardStep.ClickLeft or WizardStep.ClickRight && _now - _entered >= 15000)
            Tick(_now);
        WizardStep before = Step;
        switch (Step)
        {
            case WizardStep.ChinTest: MeasureChin(down, pressed, gap); break;
            case WizardStep.LeftButton:
            case WizardStep.RightButton: MeasureLanding(down, gap); break;
            case WizardStep.Sweep: MeasureSweep(down, gap); break;
            case WizardStep.Still: MeasureStill(down, gap); break;
            case WizardStep.ClickLeft:
            case WizardStep.ClickRight:
                if (!ClickTimedOut && (pressed & 1) != 0 && Selected is { } pad)
                {
                    ZoneAction want = Step == WizardStep.ClickLeft ? ZoneAction.Left : ZoneAction.Right;
                    if (down.Any(c => ClickEngine.ClassifyMm(Settings.Zones, pad.Geometry, c.X, c.Y)?.Action == want))
                    {
                        if (want == ZoneAction.Left) LeftClickPassed = true; else RightClickPassed = true;
                        ClickAccepted?.Invoke(want == ZoneAction.Left ? MouseButton.Left : MouseButton.Right);
                        Enter(want == ZoneAction.Left ? WizardStep.ClickRight : WizardStep.TryIt);
                    }
                }
                break;
        }
        if (before == Step) Tick(_now); else Changed?.Invoke();
    }

    public void Tick(double atMs)
    {
        _now = Math.Max(_now, atMs);
        if (Step == WizardStep.Closed) return;
        if (Step == WizardStep.SelectPad && Selected is null && _candidate is { } pick && _now - pick.At >= 30)
            Select(_pads.Single(p => p.Index == pick.Index));
        bool stale = _now - _lastFrame > FreshMs;
        if (stale)
        {
            _holdStart = double.NaN;
            if (Step == WizardStep.Still) { _stillStart = double.NaN; _still.Clear(); }
        }
        switch (Step)
        {
            case WizardStep.ChinTest:
                if ((!double.IsNaN(_chinStart) && _now - _chinStart >= 5000) || double.IsNaN(_chinStart) && _now - _entered >= 20000)
                    EndChin();
                break;
            case WizardStep.Sweep:
                if (_sweepActive && _now - _sweepLastMotion >= 700) EndSweep();
                if (_sweeps.Count >= 2) EndSweeps();
                else if (_now - _entered >= 30000) { SweepTimedOut = true; Enter(WizardStep.SweepResult); }
                break;
            case WizardStep.Still:
                if (!stale && !double.IsNaN(_stillStart) && _now - _stillStart >= 3000 && _still.Count >= 30)
                {
                    JitterMm = JitterRadius(_still);
                    Produce(Settings with { DeadzoneMm = Math.Clamp(JitterMm * 1.2, 0.2, 2.0), Smoothing = JitterMm > 1 ? 0.5 : 0.3 });
                    DiagnosticsProduced?.Invoke(DiagnosticText());
                    Enter(WizardStep.ClickLeft);
                }
                break;
            case WizardStep.ClickLeft:
            case WizardStep.ClickRight:
                if (_now - _entered >= 15000 && !ClickTimedOut)
                {
                    ClickTimedOut = true;
                    _holdNeedsLift = false;
                    _holdStart = double.NaN;
                }
                break;
        }
        if (Waiting && HoldProgress >= 1) Command(WizardKey.Continue, _now);
        Changed?.Invoke();
    }

    public void Command(WizardKey key, double atMs)
    {
        _now = Math.Max(_now, atMs);
        if (key == WizardKey.Back)
        {
            if (!CanBack) return;
            Enter(Step switch
            {
                WizardStep.SelectPad => WizardStep.Welcome,
                WizardStep.ChinTest or WizardStep.ChinVerdict => WizardStep.SelectPad,
                WizardStep.LeftButton => WizardStep.ChinVerdict,
                WizardStep.RightButton or WizardStep.Zones => WizardStep.LeftButton,
                WizardStep.Sweep => WizardStep.Zones,
                WizardStep.SweepResult => WizardStep.Sweep,
                WizardStep.Still => WizardStep.Sweep,
                WizardStep.ClickLeft => WizardStep.Still,
                WizardStep.ClickRight => WizardStep.ClickLeft,
                WizardStep.TryIt => WizardStep.ClickLeft,
                _ => WizardStep.TryIt,
            });
        }
        else if (key == WizardKey.Skip && CanSkip)
        {
            if (Step == WizardStep.SelectPad && Selected is null) Select(_pads.OrderBy(p => PadChoice.IsExternal(p.Path)).First());
            if (Step == WizardStep.LeftButton) _left.Clear();
            if (Step == WizardStep.RightButton) _right.Clear();
            Enter(Step switch
            {
                WizardStep.SelectPad => WizardStep.ChinTest,
                WizardStep.ChinTest => WizardStep.LeftButton,
                WizardStep.ChinVerdict when Verdict == ChinVerdict.NotReported => WizardStep.Finish,
                WizardStep.ChinVerdict => WizardStep.LeftButton,
                WizardStep.LeftButton => WizardStep.RightButton,
                WizardStep.RightButton => WizardStep.Zones,
                WizardStep.Zones => WizardStep.Sweep,
                WizardStep.Sweep or WizardStep.SweepResult => WizardStep.Still,
                WizardStep.Still => WizardStep.ClickLeft,
                WizardStep.ClickLeft => WizardStep.ClickRight,
                WizardStep.ClickRight => WizardStep.TryIt,
                _ => WizardStep.Finish,
            });
        }
        else if (key == WizardKey.Continue && CanContinue)
        {
            Enter(Step switch
            {
                WizardStep.Welcome => WizardStep.SelectPad, WizardStep.SelectPad => WizardStep.ChinTest,
                WizardStep.ChinVerdict => WizardStep.LeftButton, WizardStep.Zones => WizardStep.Sweep,
                WizardStep.SweepResult => WizardStep.Still, WizardStep.ClickLeft => WizardStep.ClickRight,
                WizardStep.ClickRight => WizardStep.TryIt, WizardStep.TryIt => WizardStep.Finish, _ => WizardStep.Closed,
            });
        }
        Changed?.Invoke();
    }

    void Select(WizardPad pad)
    {
        Selected = pad;
        Produce(Settings with { Device = pad.Device });
        _candidate = null;
        _lastFrame = double.NegativeInfinity;
        _latestDown = false;
        _holdStart = double.NaN;
    }

    void Enter(WizardStep next)
    {
        Step = next;
        _entered = _now;
        _holdStart = double.NaN;
        _holdNeedsLift = _latestDown;
        _landingStart = double.NaN;
        _settled = null;
        _liftRequired = _latestDown;
        ClickTimedOut = false;
        if (next == WizardStep.SelectPad)
        {
            Selected = null;
            _candidate = null;
            if (_pads.Count == 1) Select(_pads[0]);
        }
        if (next == WizardStep.ChinTest)
        {
            Verdict = ChinVerdict.NotMeasured;
            ContactsSeen = MaxContacts = ReportingGaps = PressesSeen = LowConfidenceSamples = ConfidenceFlips = 0;
            LongestRunMs = TypicalDistanceMm = 0;
            _chinStart = _runStart = double.NaN;
            _chinTracks.Clear();
            _pairDistances.Clear();
        }
        if (next == WizardStep.LeftButton) { _left.Clear(); _right.Clear(); }
        if (next == WizardStep.RightButton) _right.Clear();
        if (next == WizardStep.Zones) ComputeZones();
        if (next == WizardStep.Sweep)
        {
            _sweeps.Clear(); _sweepActive = false; SweepTimedOut = false; SweepSpanMm = 0;
        }
        if (next == WizardStep.Still) { _still.Clear(); _stillStart = double.NaN; }
        if (next == WizardStep.ClickLeft) { LeftClickPassed = RightClickPassed = false; }
        if (next == WizardStep.TryIt) TargetsHit = 0;
    }

    void MeasureChin(WizardContact[] down, uint pressed, bool gap)
    {
        if (pressed != 0) PressesSeen++;
        MaxContacts = Math.Max(MaxContacts, down.Length);
        if (gap && down.Length > 0) ReportingGaps++;
        var ids = down.Select(c => c.Id).ToHashSet();
        foreach (int gone in _chinTracks.Keys.Where(id => !ids.Contains(id)).ToArray()) _chinTracks.Remove(gone);
        foreach (WizardContact c in down)
        {
            if (!_chinTracks.TryGetValue(c.Id, out var old) || _now - old.Last > FreshMs) { ContactsSeen++; old = (_now, _now, c.Confidence); }
            if (old.Confidence != c.Confidence) ConfidenceFlips++;
            if (!c.Confidence) LowConfidenceSamples++;
            _chinTracks[c.Id] = (old.Start, _now, c.Confidence);
        }
        if (down.Length == 0) { _runStart = double.NaN; return; }
        if (double.IsNaN(_chinStart)) _chinStart = _now;
        if (double.IsNaN(_runStart) || gap) _runStart = _now;
        LongestRunMs = Math.Max(LongestRunMs, _now - _runStart);
        if (down.Length >= 2)
        {
            double distance = 0;
            for (int i = 0; i < down.Length; i++)
                for (int j = i + 1; j < down.Length; j++)
                    distance = Math.Max(distance, Distance(new(down[i].X, down[i].Y), new(down[j].X, down[j].Y)));
            _pairDistances.Add(distance);
        }
    }

    void EndChin()
    {
        TypicalDistanceMm = Percentile(_pairDistances, 0.5);
        Verdict = LongestRunMs < 500 ? ChinVerdict.NotReported : MaxContacts >= 2 ? ChinVerdict.JoinTouches : ChinVerdict.Good;
        if (Verdict == ChinVerdict.JoinTouches) Produce(Settings with { MergeMm = Math.Min(60, TypicalDistanceMm + 10) });
        DiagnosticsProduced?.Invoke(DiagnosticText());
        Enter(WizardStep.ChinVerdict);
    }

    void MeasureLanding(WizardContact[] down, bool gap)
    {
        if (_liftRequired) { if (down.Length == 0) _liftRequired = false; return; }
        if (gap) { _landingStart = double.NaN; _settled = null; _liftRequired = down.Length > 0; return; }
        if (down.Length > 0)
        {
            if (double.IsNaN(_landingStart)) _landingStart = _now;
            if (_settled is null && _now - _landingStart >= 40) _settled = new(down[0].X, down[0].Y);
        }
        else if (!double.IsNaN(_landingStart))
        {
            if (_now - _landingStart >= 150 && _settled is { } at)
            {
                List<WizardPoint> into = Step == WizardStep.LeftButton ? _left : _right;
                into.Add(at);
                if (into.Count == 3) Enter(Step == WizardStep.LeftButton ? WizardStep.RightButton : WizardStep.Zones);
            }
            _landingStart = double.NaN;
            _settled = null;
        }
    }

    void ComputeZones()
    {
        ZonesKept = _left.Count != 3 || _right.Count != 3 || Selected is null;
        if (ZonesKept) return;
        double w = Selected!.Geometry.WidthMm, h = Selected.Geometry.HeightMm;
        double lx = _left.Average(p => p.X), rx = _right.Average(p => p.X);
        SplitFallback = rx - lx < 10;
        double split = SplitFallback ? 0.5 : Math.Clamp((lx + rx) / (2 * w), 0.3, 0.7);
        WizardPoint[] all = _left.Concat(_right).ToArray();
        bool top = all.All(p => p.Y <= h / 3);
        // On a pad shorter than 25 mm, preserving 40% movement takes priority over the 15 mm minimum.
        double height = Math.Min(h * 0.6, Math.Max(15, top ? all.Max(p => p.Y) + 8 : h - all.Min(p => p.Y) + 8));
        double y0 = top ? 0 : 1 - height / h, y1 = top ? height / h : 1;
        Produce(Settings with { Zones = [new(ZoneAction.Left, 0, split, y0, y1), new(ZoneAction.Right, split, 1, y0, y1)], ZoneMode = "position" });
        DiagnosticsProduced?.Invoke(DiagnosticText());
    }

    void MeasureSweep(WizardContact[] down, bool gap)
    {
        if (down.Length == 0 || gap) { EndSweep(); if (down.Length == 0) return; }
        double x = down[0].X;
        if (!_sweepActive)
        {
            _sweepActive = true; _sweepStart = _sweepExtreme = x; _sweepDirection = 0; _sweepLastMotion = _now;
            return;
        }
        if (_sweepDirection == 0) _sweepDirection = Math.Sign(x - _sweepStart);
        double advance = (x - _sweepExtreme) * _sweepDirection;
        if (advance > 0) { _sweepExtreme = x; if (advance >= 0.1) _sweepLastMotion = _now; }
        else if (advance <= -3)
        {
            double end = _sweepExtreme;
            EndSweep();
            _sweepActive = true; _sweepStart = end; _sweepExtreme = x; _sweepDirection = Math.Sign(x - end); _sweepLastMotion = _now;
        }
    }
    void EndSweep()
    {
        if (!_sweepActive) return;
        double span = Math.Abs(_sweepExtreme - _sweepStart);
        if (span >= 5) _sweeps.Add(span);
        _sweepActive = false;
    }
    void EndSweeps()
    {
        SweepSpanMm = _sweeps.OrderDescending().Take(2).Average();
        Produce(Settings with { SpeedPxPerMm = SpeedFor(_screenWidth, SweepSpanMm) });
        DiagnosticsProduced?.Invoke(DiagnosticText());
        Enter(WizardStep.SweepResult);
    }
    public static double SpeedFor(double screenWidth, double spanMm) => Math.Clamp(screenWidth / (Math.Max(0.001, spanMm) * 1.15), 4, 60);

    void MeasureStill(WizardContact[] down, bool gap)
    {
        if (down.Length == 0 || gap) { _stillStart = double.NaN; _still.Clear(); }
        if (down.Length == 0) return;
        if (double.IsNaN(_stillStart)) _stillStart = _now;
        _still.Add(new(down[0].X, down[0].Y));
    }
    public static double JitterRadius(IReadOnlyList<WizardPoint> points)
    {
        if (points.Count == 0) return 0;
        var mean = new WizardPoint(points.Average(p => p.X), points.Average(p => p.Y));
        return Percentile(points.Select(p => Distance(p, mean)).ToArray(), 0.95);
    }
    static double Distance(WizardPoint a, WizardPoint b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
    static double Percentile(IReadOnlyList<double> values, double p) => values.Count == 0 ? 0 : values.Order().ElementAt(Math.Clamp((int)Math.Ceiling(values.Count * p) - 1, 0, values.Count - 1));

    public void HitTarget() { if (!TryOut || TargetsHit >= 3) return; TargetsHit++; Changed?.Invoke(); }
    public void ChangeLanguage(string language) => Produce(Settings with { Language = language });
    public void AdjustSpeed(int direction) { if (TryOut) Produce(Settings with { SpeedPxPerMm = Math.Clamp(Settings.SpeedPxPerMm + direction * 2, 4, 60) }); }
    public void AdjustStrip(int direction)
    {
        if (!TryOut || Selected is null || Settings.Zones.Count != 2) return;
        double h = Selected.Geometry.HeightMm;
        ZoneSettings first = Settings.Zones[0];
        bool top = first.YMin == 0;
        double height = Math.Clamp((first.YMax - first.YMin) * h + direction * 2, Math.Min(15, h * 0.6), h * 0.6);
        Produce(Settings with { Zones = Settings.Zones.Select(z => z with { YMin = top ? 0 : 1 - height / h, YMax = top ? height / h : 1 }).ToArray() });
    }
    void Produce(AppSettings settings)
    {
        Settings = settings.Validated([]);
        SettingsProduced?.Invoke(Settings);
        Changed?.Invoke();
    }
    public string DiagnosticText() => JsonSerializer.Serialize(new
    {
        version = "0.7.0", pad = Selected?.Device, verdict = Verdict.ToString(), ContactsSeen, LongestRunMs, MaxContacts,
        TypicalDistanceMm, ReportingGaps, PressesSeen, LowConfidenceSamples, ConfidenceFlips,
        leftLandings = _left, rightLandings = _right, SplitFallback, ZonesKept, SweepSpanMm, JitterMm,
        LeftClickPassed, RightClickPassed, TargetsHit,
    }, new JsonSerializerOptions { WriteIndented = true });

    /// <summary>Rendering seam: production transitions and measurements always use frames / commands.</summary>
    internal void InjectState(WizardStep step, double atMs, ChinVerdict? verdict = null)
    {
        _now = atMs;
        if (Selected is null && _pads.Count > 0) Selected = _pads[0];
        Enter(step);
        if (verdict is { } v) { Verdict = v; ContactsSeen = v == ChinVerdict.NotReported ? 3 : 1; }
        Changed?.Invoke();
    }
}
