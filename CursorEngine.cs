using System.Globalization;

namespace ChinPad;

internal enum MouseButton { Left, Right, Middle }

/// <summary>
/// Receives what the engines decide: whole-pixel cursor steps (CursorEngine) and button downs/ups
/// (ClickEngine). Win32Mover in the app, a recorder in tests.
/// </summary>
internal interface IMover
{
    void Move(int dx, int dy);
    void Button(MouseButton button, bool down);
}

/// <summary>How to turn one touchpad's logical units and scan-time ticks into millimetres, pad fractions and milliseconds.</summary>
internal sealed record PadGeometry(double MmPerUnitX, double MmPerUnitY, double OriginX, double OriginY,
    double ScanTickMs, int ScanModulus, string Note, double SpanX = 0, double SpanY = 0)
{
    /// <summary>Size of the pad in mm (logical span times mm per unit).</summary>
    public double WidthMm => SpanX * MmPerUnitX;
    public double HeightMm => SpanY * MmPerUnitY;

    /// <summary>A position in logical units as fractions of the pad, 0..1 from the left / top edge (clamped).</summary>
    public (double Fx, double Fy) Fraction(int x, int y) =>
        (SpanX > 0 ? Math.Clamp((x - OriginX) / SpanX, 0, 1) : 0, SpanY > 0 ? Math.Clamp((y - OriginY) / SpanY, 0, 1) : 0);

    /// <summary>A position in logical units in mm from the top-left corner.</summary>
    public (double X, double Y) Mm(int x, int y) => ((x - OriginX) * MmPerUnitX, (y - OriginY) * MmPerUnitY);

    /// <summary>
    /// mm per logical unit from the physical extent of X and Y (PTP requires it). Fallback when it is missing:
    /// a pad 100 mm wide with the aspect of the logical ranges, i.e. both axes 100 mm / X logical span.
    /// Scan time: 100 us ticks wrapping at 65536 unless the field says otherwise (unit seconds, exponent).
    /// </summary>
    public static PadGeometry FromFields(ValueField? x, ValueField? y, ValueField? scan)
    {
        double xSpan = x is null ? 0 : x.LogicalMax - x.LogicalMin;
        double ySpan = y is null ? 0 : y.LogicalMax - y.LogicalMin;
        double fallback = xSpan > 0 ? 100.0 / xSpan : 0.1;
        double mmX = x?.PhysicalSpanMm is double px && xSpan > 0 ? px / xSpan : fallback;
        double mmY = y?.PhysicalSpanMm is double py && ySpan > 0 ? py / ySpan : mmX;
        string note = x?.PhysicalSpanMm is not null && y?.PhysicalSpanMm is not null
            ? string.Create(CultureInfo.InvariantCulture, $"{xSpan * mmX:0.0} x {ySpan * mmY:0.0} mm")
            : string.Create(CultureInfo.InvariantCulture, $"no physical size reported, assuming {xSpan * mmX:0.0} x {ySpan * mmY:0.0} mm");

        double tickMs = 0.1;
        if (scan is { Units: 0x1001 }) tickMs = Math.Pow(10, HidUnits.Exponent(scan.UnitsExp)) * 1000.0;
        int modulus = scan is null ? 65536 : (int)Math.Clamp((long)scan.LogicalMax - scan.LogicalMin + 1, 2, int.MaxValue);
        return new PadGeometry(mmX, mmY, x?.LogicalMin ?? 0, y?.LogicalMin ?? 0, tickMs, modulus, note, xSpan, ySpan);
    }
}

/// <summary>
/// Decides which touchpad contact drives the cursor and turns its motion into cursor steps. Pure logic:
/// it gets the completed frames of every touchpad (FrameAssembler) and hands whole pixels to an IMover.
///
/// Ownership ("ours" = ChinPad moves the cursor for it), by strategy:
///  1 and 3: every contact with Tip Switch on;
///  2: a contact that reported Confidence 0 (a palm to Windows). With claimOnce it stays ours until it
///     lifts, ignoring the single-report blips back to Confidence 1 the firmware sends; without it, only
///     while Confidence is 0.
/// In every strategy, a contact the blocksCursor hook reports (ClickEngine: still settling after it
/// landed, or classified in a button zone or a dead zone) is not ours: it can click but never moves the
/// cursor. When the hook lets go of a contact (settled in the move area, or zoneMode "position" and it
/// slid out of the zone), ChinPad takes it over from where it is, with the deadzone armed again.
/// Positions are tracked from a contact's first report even while it is not ours, so a take-over starts
/// from the last known position (no jump to where the touch began). One contact drives at a time: the
/// driver keeps driving until it lifts (or stops being ours); then the owned contact with the lowest
/// contact ID takes over. Others are ignored, not summed.
///
/// Merging (mergeMm &gt; 0; some pads report a chin as two contacts): when the driver lifts or vanishes and
/// another owned contact of the same pad is within mergeMm of the driver's last position, that partner
/// takes over in the same frame (the nearest one): from its own last position, so the cursor does not
/// jump, and with the deadzone state of the driver (a driver that had left the deadzone hands over a
/// partner that moves at once). While the driver is down a partner is ignored like any other contact.
///
/// Per frame for the driver: logical -> mm -> EMA smoothing -> deadzone (hysteresis from the take-over
/// point) -> delta mm -> * speedPxPerMm * acceleration gain -> clamp to maxStepPx -> sub-pixel remainder
/// -> whole pixels to the mover.
///
/// Threading: everything runs on the UI thread except BlockMoves, which the strategy-3 hook thread reads.
/// </summary>
internal sealed class CursorEngine
{
    const double HoldAfterLiftMs = 50;   // strategy 3: keep blocking Windows' moves this long after the last confident contact
    const double StaleFrameMs = 250;     // strategy 3: a "confident contact down" older than this no longer blocks (pad went silent)
    const double MaxScanDtMs = 100;      // larger scan-time steps are not trusted for the velocity

    /// <summary>One contact (device + contact ID) from its first report to its lift.</summary>
    sealed class Contact
    {
        public required IntPtr Device { get; init; }
        public required int Id { get; init; }
        public required int DeviceOrder { get; init; }
        public required double FirstSeenMs { get; init; }
        public bool Confidence;
        public bool Claimed;            // has reported Confidence 0 at least once during this touch
        public double X, Y;             // smoothed position, mm
        public double SpeedMmPerS;      // of the smoothed position over the last frame
        public int? LastScan;
        public double LastMs;
        public bool Seen;               // present in the frame being processed
        // While driving:
        public double AnchorX, AnchorY; // where the deadzone is measured from (start or take-over point)
        public double SentX, SentY;     // position the cursor has been moved to, in pad mm
        public bool Released;           // the deadzone has been left
        public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"pad {DeviceOrder} contact {Id}");
    }

    readonly IMover _mover;
    readonly Func<IntPtr, int, bool>? _blocksCursor;
    readonly Dictionary<IntPtr, (PadGeometry Geometry, int Order)> _devices = [];
    readonly Dictionary<(IntPtr, int), Contact> _contacts = [];
    readonly List<(Contact C, double X, double Y)> _pending = [];
    AppSettings _s;
    Contact? _driver;
    Contact? _lostDriver;   // the driver, when it ended in the frame being processed (merge hand-over)
    double _remX, _remY;

    // Shared with the hook thread.
    volatile int _strategy;
    volatile bool _paused;
    volatile bool _confidentDown;
    double _lastConfidentMs = double.NegativeInfinity;
    double _releasedMs = double.NegativeInfinity;

    /// <param name="blocksCursor">
    /// (device, contact ID) -> true when the contact must not drive the cursor now: still settling, or a
    /// zone contact (ClickEngine.BlocksCursor, which must have seen the frame first). Null: no zones.
    /// </param>
    public CursorEngine(IMover mover, AppSettings settings, Func<IntPtr, int, bool>? blocksCursor = null)
    {
        _mover = mover;
        _blocksCursor = blocksCursor;
        _s = settings;
        _strategy = settings.Strategy;
        _paused = settings.StartPaused;
    }

    public long Frames { get; private set; }
    public long InjectedMoves { get; private set; }
    public long InjectedPixels { get; private set; }  // sum of |dx| + |dy|, for the log
    public int Strategy => _strategy;
    public string DriverText => _driver?.ToString() ?? "none";
    /// <summary>Times the driver lifted or vanished and a partner within mergeMm took over without a jump.</summary>
    public long MergeHandOvers { get; private set; }

    /// <summary>The contact that moves the cursor now, if any (the settings window colours it).</summary>
    public bool IsDriver(IntPtr device, int contactId) => _driver is { } d && d.Device == device && d.Id == contactId;

    /// <summary>Paused: positions are still tracked, nothing is emitted, the hook blocks nothing. Resuming takes over from the current position.</summary>
    public bool Paused
    {
        get => _paused;
        set
        {
            _paused = value;
            if (value) _driver = null;
        }
    }

    /// <summary>New settings (live reload). The current driver keeps driving if it is still ours.</summary>
    public void Apply(AppSettings settings)
    {
        _s = settings;
        _strategy = settings.Strategy;
        if (_driver is not null && !Owned(_driver)) _driver = null;
    }

    public void AddDevice(IntPtr handle, PadGeometry geometry) => _devices[handle] = (geometry, _devices.Count);

    public void ClearDevices()
    {
        _devices.Clear();
        _contacts.Clear();
        _driver = null;
        _lostDriver = null;
        _confidentDown = false;
    }

    /// <summary>
    /// Strategy 3: Windows' own cursor moves are dropped while this is true: a confident contact (one
    /// Windows acts on) is down on a touchpad, and for 50 ms after the last one went away. Never while paused.
    /// Called from the hook thread; reads only volatile / atomically written fields.
    /// </summary>
    public bool BlockMoves => BlockMovesAt(Clock.NowMs);

    public bool BlockMovesAt(double nowMs)
    {
        if (_strategy != 3 || _paused) return false;
        if (_confidentDown) return nowMs - Volatile.Read(ref _lastConfidentMs) < StaleFrameMs;
        return nowMs - Volatile.Read(ref _releasedMs) < HoldAfterLiftMs;
    }

    /// <summary>One completed frame of one touchpad, with that touchpad's contact tracks.</summary>
    public void OnFrame(IntPtr device, Frame frame, IReadOnlyDictionary<int, ContactTrack> tracks, double nowMs)
    {
        if (!_devices.TryGetValue(device, out var dev)) return;
        Frames++;
        PadGeometry g = dev.Geometry;

        // 1. Contact states: new touches, tip/confidence, lifts. Positions are applied in step 3, so that a
        //    take-over in step 2 starts from the position known before this frame.
        foreach (Contact c in _contacts.Values)
            if (c.Device == device) c.Seen = false;
        _pending.Clear();
        foreach (ContactReading r in frame.Contacts)
        {
            var key = (device, r.ContactId);
            _contacts.TryGetValue(key, out Contact? c);
            if (!r.Tip)
            {
                if (c is not null) Remove(key, c); // normal lift (tip-off report); its position is not used
                continue;
            }
            double firstSeen = tracks.TryGetValue(r.ContactId, out ContactTrack? t) ? t.FirstSeenMs : nowMs;
            double x = (r.X - g.OriginX) * g.MmPerUnitX, y = (r.Y - g.OriginY) * g.MmPerUnitY;
            if (c is not null && c.FirstSeenMs != firstSeen)
            {
                Remove(key, c);  // the ID was reused by a new touch
                c = null;
            }
            if (c is null)
            {
                c = new Contact { Device = device, Id = r.ContactId, DeviceOrder = dev.Order, FirstSeenMs = firstSeen, X = x, Y = y, LastMs = nowMs, LastScan = frame.ScanTime };
                _contacts[key] = c;
            }
            c.Seen = true;
            c.Confidence = r.Confidence;
            if (!r.Confidence) c.Claimed = true;
            _pending.Add((c, x, y));
        }
        foreach (Contact c in _contacts.Values.Where(c => c.Device == device && !c.Seen).ToList())
            Remove((device, c.Id), c);     // vanished from a complete frame without a tip-off report
        UpdateBlocking(nowMs);

        // 2. Who drives. A driver that ended in this frame hands over to a partner within mergeMm first.
        if (_driver is not null && (_paused || !Owned(_driver))) _driver = null;
        if (_driver is null && !_paused)
        {
            if (_lostDriver is { } lost && Partner(lost) is { } partner)
            {
                TakeOver(partner, keepReleased: lost.Released);
                MergeHandOvers++;
            }
            else
            {
                Contact? next = _contacts.Values.Where(Owned).OrderBy(c => c.Id).ThenBy(c => c.DeviceOrder).FirstOrDefault();
                if (next is not null) TakeOver(next);
            }
        }
        _lostDriver = null;

        // 3. Positions (EMA) and speed of every contact of this frame.
        double alpha = 1.0 - _s.Smoothing;
        foreach ((Contact c, double x, double y) in _pending)
        {
            double dt = FrameDtMs(c, frame.ScanTime, nowMs, g);
            double px = c.X, py = c.Y;
            if (alpha >= 1.0) { c.X = x; c.Y = y; }
            else { c.X += alpha * (x - c.X); c.Y += alpha * (y - c.Y); }
            c.SpeedMmPerS = dt > 0 ? Math.Sqrt(Sq(c.X - px) + Sq(c.Y - py)) / (dt / 1000.0) : 0;
            c.LastScan = frame.ScanTime ?? c.LastScan;
            c.LastMs = nowMs;
        }

        // 4. Move the cursor for the driver, if it is on this touchpad and was in this frame.
        if (_driver is { Seen: true } d && d.Device == device) Drive(d);
    }

    bool Owned(Contact c) => _strategy switch
    {
        2 => _s.ClaimOnce ? c.Claimed : !c.Confidence,
        _ => true, // 1 and 3: every tip-on contact (only tip-on contacts are kept)
    } && !(_blocksCursor?.Invoke(c.Device, c.Id) ?? false);

    /// <param name="keepReleased">Merge hand-over: the partner continues past the deadzone if the driver was.</param>
    void TakeOver(Contact c, bool keepReleased = false)
    {
        _driver = c;
        c.AnchorX = c.SentX = c.X;
        c.AnchorY = c.SentY = c.Y;
        c.Released = keepReleased || _s.DeadzoneMm <= 0;
        _remX = _remY = 0;
    }

    /// <summary>The owned contact of the same pad nearest to where the lost driver was, if within mergeMm (0 = off).</summary>
    Contact? Partner(Contact lost)
    {
        if (_s.MergeMm <= 0) return null;
        Contact? best = null;
        double bestMm = double.MaxValue;
        foreach (Contact c in _contacts.Values)
        {
            if (c.Device != lost.Device || ReferenceEquals(c, lost) || !Owned(c)) continue;
            double mm = Math.Sqrt(Sq(c.X - lost.X) + Sq(c.Y - lost.Y));
            if (mm <= _s.MergeMm && mm < bestMm)
            {
                best = c;
                bestMm = mm;
            }
        }
        return best;
    }

    void Remove((IntPtr, int) key, Contact c)
    {
        _contacts.Remove(key);
        if (ReferenceEquals(_driver, c))
        {
            _driver = null;
            _lostDriver = c;
        }
    }

    void Drive(Contact c)
    {
        if (!c.Released)
        {
            if (Math.Sqrt(Sq(c.X - c.AnchorX) + Sq(c.Y - c.AnchorY)) < _s.DeadzoneMm) return;
            c.Released = true; // from here on every delta counts; this first one is measured from the anchor
        }
        double dxMm = c.X - c.SentX, dyMm = c.Y - c.SentY;
        c.SentX = c.X;
        c.SentY = c.Y;

        double k = _s.SpeedPxPerMm * Gain(c.SpeedMmPerS);
        double px = dxMm * k, py = dyMm * k;
        double len = Math.Sqrt(px * px + py * py);
        if (len > _s.MaxStepPx)
        {
            px *= _s.MaxStepPx / len;
            py *= _s.MaxStepPx / len;
        }

        _remX += px;
        _remY += py;
        // Whole pixels toward zero; the fraction stays for the next frame. The tiny bias keeps 11.9999999999
        // (floating-point sum of exact steps) from losing a pixel.
        int ix = (int)Math.Truncate(_remX + Math.Sign(_remX) * 1e-9);
        int iy = (int)Math.Truncate(_remY + Math.Sign(_remY) * 1e-9);
        _remX -= ix;
        _remY -= iy;
        if (ix == 0 && iy == 0) return;
        _mover.Move(ix, iy);
        InjectedMoves++;
        InjectedPixels += Math.Abs(ix) + Math.Abs(iy);
    }

    /// <summary>gain = clamp((v / threshold)^(exponent - 1), 1, 4) above the threshold, else 1.</summary>
    double Gain(double v)
    {
        AccelerationSettings a = _s.Acceleration;
        if (!a.Enabled || v <= a.ThresholdMmPerS) return 1.0;
        return Math.Clamp(Math.Pow(v / a.ThresholdMmPerS, a.Exponent - 1.0), 1.0, 4.0);
    }

    /// <summary>Time since the contact's previous frame: from the device's scan time if present and plausible, else the wall clock.</summary>
    static double FrameDtMs(Contact c, int? scan, double nowMs, PadGeometry g)
    {
        if (scan is int s && c.LastScan is int last)
        {
            int ticks = ((s - last) % g.ScanModulus + g.ScanModulus) % g.ScanModulus;
            double dt = ticks * g.ScanTickMs;
            if (dt > 0 && dt <= MaxScanDtMs) return dt;
        }
        return Math.Max(0, nowMs - c.LastMs);
    }

    /// <summary>Strategy 3 bookkeeping: is a contact Windows acts on (tip and confidence) down on any touchpad?</summary>
    void UpdateBlocking(double nowMs)
    {
        bool any = false;
        foreach (Contact c in _contacts.Values)
            if (c.Confidence) { any = true; break; }
        if (any)
        {
            Volatile.Write(ref _lastConfidentMs, nowMs);
            _confidentDown = true;
        }
        else if (_confidentDown)
        {
            Volatile.Write(ref _releasedMs, nowMs);
            _confidentDown = false;
        }
    }

    static double Sq(double v) => v * v;
}
