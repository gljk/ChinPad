using System.Globalization;
using ChinPad.Native;

namespace ChinPad;

/// <summary>
/// A button event ChinPad sent, for the log, the beep and the tray tooltip. Lock: not an event sent to Windows
/// but the moment ClickLock locked the button (Down is true then; the double beep marks it).
/// </summary>
internal readonly record struct ClickNotice(MouseButton Button, bool Down, string Text, bool Lock = false);

/// <summary>One contact as ClickEngine sees it (logical units), for the settings window's live drawing.</summary>
internal readonly record struct ContactInfo(int Id, int X, int Y, bool Decided, ZoneSettings? Zone, bool Mine);

/// <summary>
/// The last press of the pad (button bit 0/1/2 going down): where (logical units of Device; HasPosition false
/// when no contact was down), when (the frame time), and what ChinPad made of it ("left", "right", "middle",
/// "ignored", "Windows").
/// </summary>
internal readonly record struct PressInfo(IntPtr Device, bool HasPosition, int X, int Y, double AtMs, string Outcome);

/// <summary>
/// Button zones: decides when pressing the pad clicks, and which button. Pure logic like CursorEngine: it
/// gets the completed frames of every touchpad and hands button downs and ups to an IMover.
///
/// Classification: a new contact (device + contact ID) is UNDECIDED until zoneSettleMs after its first
/// report (a chin rolls a few mm while it settles), then it is classified by its position in that frame:
/// in a zone (the first listed zone that contains it) or in the move area. A press (button bit 0 -> 1)
/// decides the undecided contacts of that pad at once, by where they are; a lift decides by the last
/// position (for the counters only). zoneSettleMs 0: by the first report (LandX/LandY), as in 0.3.0. No
/// zones: decided at once (nothing to decide). Then, by zoneMode:
///  - "landing": the classification holds for the whole touch; sliding into or out of a zone changes nothing.
///  - "position": a decided contact is in the zone it is inside in each frame (null outside every zone).
///    A button ChinPad holds stays down until the physical release even if the touch leaves the zone.
/// A contact is a zone contact when it is decided, in a zone, and ChinPad's: in strategies 1 and 3 every
/// tip-on contact, in strategy 2 only a contact that has reported Confidence 0 at least once (claimed;
/// sticky whatever claimOnce says), so a finger in a zone stays with Windows. Settling and the claim run
/// in parallel: the classification happens at settle time whether or not the contact is claimed yet.
/// CursorEngine never drives a contact of ChinPad's that is undecided or a zone contact (it asks
/// BlocksCursor); a "none" zone contact does nothing.
///
/// Presses come from the frame's button bits (on a clickpad, bit 0 is the whole surface):
///  - bit 0 goes 0 -> 1 while exactly one zone contact of that pad is down and its zone is left, right or
///    middle: that button goes down. It goes up when bit 0 goes 1 -> 0, when the contact lifts or
///    vanishes, on pause, when the touchpads change, and at exit. Every down is paired with an up.
///  - a "none" zone contact down, two or more zone contacts down, or bit 1/2 with a zone contact down:
///    the press is ignored.
///  - no zone contact down: when Windows' own click gets through anyway (strategy 2 or 3 and BlockClicks
///    is false, e.g. only a finger is down), the press is Windows' business and ChinPad does nothing.
///    Otherwise "ignore" drops it (the accidental-click fix) and "passthrough" sends bit 0 / 1 / 2 as a
///    left / right / middle click at the cursor.
///  - a button ChinPad already holds down is not pressed a second time (another pad, another bit).
/// Paused: nothing is sent, and a button ChinPad holds is released at once.
///
/// Merging (mergeMm &gt; 0; some pads report a chin as two contacts): contacts of one pad within mergeMm of
/// each other are one touch.
///  - A contact that is still undecided while a decided contact of its pad is within mergeMm (one that is
///    down, or one that ended in this frame) takes that contact's classification at once instead of waiting
///    for its own settle window (the nearest one's). Undecided contacts are handled earliest first, so of
///    two contacts that land together the first one settles by its position and the second follows it.
///  - For a press, zone contacts within mergeMm of each other (chains included) count as one zone touch;
///    the zone of the earliest of them wins. So "two zone touches down" means two separate touches.
///  - When the contact that holds a button lifts or vanishes while a partner of ChinPad's is still within
///    mergeMm of it, the partner keeps the button down (the press is still going on).
/// mergeMm 0: none of this, exactly as in 0.3.1.
///
/// ClickLock (clickLock.enabled, 0.5.0): a zone button whose press has lasted clickLock.holdMs (checked at every
/// frame of any pad and by Tick) is locked: it stays down after the physical release, after the contact lifts
/// (a partner's lift included), until the next press that ends it: a press with a touch in a left, right or
/// middle zone, a press of button 2 or 3, any press with "passthrough", or a press Windows clicks for itself.
/// That press only sends the pending up (no new click, not subject to blockDoubleClick). A press ChinPad would
/// ignore anyway (no zone touch with "ignore", a dead-zone touch alone) leaves the lock alone. Pause, a change
/// of touchpads, exit and switching clickLock off release it too. While locked the chin moves the cursor as
/// usual: that is the drag. Passthrough buttons never lock.
///
/// blockDoubleClick (0.5.0): a press that would send a button down within doubleClickWindowMs (0 = Windows'
/// double-click time) after the previous down of the same button is dropped entirely: nothing is sent, its
/// release sends nothing, it is counted (DoubleClicksBlocked) and does not move the window on. False: as 0.4.0.
///
/// BlockClicks (read by the mouse-hook thread): hookClicks is on, not paused, and a contact ChinPad owns
/// is down on a touchpad (strategy 2: claimed; 1 and 3: any), plus 100 ms after the last one lifted.
/// Threading: everything else runs on the UI thread.
/// </summary>
internal sealed class ClickEngine
{
    const double TailMs = 100;        // keep dropping Windows' clicks this long after ChinPad's last touch lifted
    const double StaleFrameMs = 250;  // a "touch down" older than this no longer blocks (the pad went silent)

    /// <summary>One contact from its first report to its lift.</summary>
    sealed class Contact
    {
        public required IntPtr Device { get; init; }
        public required int Id { get; init; }
        public required double FirstSeenMs { get; init; }
        public bool Decided;          // classified (settled, pressed, or lifted)
        public ZoneSettings? Zone;    // once Decided: its zone; null = the move area
        public bool Claimed;  // has reported Confidence 0 during this touch
        public bool Counted;  // counted as a zone or move touch
        public bool Seen;     // present in the frame being processed
        public int X, Y;      // last tip-on position, logical units
        public int LandX, LandY;  // first report of the touch (zoneSettleMs 0 decides by it)
    }

    /// <summary>
    /// A button ChinPad holds down: pressed by bit Bit of Device, for a zone Contact (null = passthrough).
    /// Locked (ClickLock): no longer tied to the bit or the contact; only a press, pause or exit releases it.
    /// </summary>
    sealed record Held(MouseButton Button, IntPtr Device, int Bit, Contact? Contact, double DownMs, bool Locked = false);

    readonly IMover _mover;
    readonly Dictionary<IntPtr, (PadGeometry Geometry, string Name)> _devices = [];
    readonly Dictionary<IntPtr, uint> _buttons = [];   // button bits of each pad's previous frame
    readonly Dictionary<(IntPtr, int), Contact> _contacts = [];
    readonly List<Held> _held = [];
    readonly List<Contact> _ended = [];  // contacts of the pad being processed that ended in this frame (merge sources)
    readonly Dictionary<MouseButton, double> _lastDownMs = [];  // blockDoubleClick: when each button last went down
    AppSettings _s;
    double _lastFrameMs;

    // Shared with the hook thread.
    volatile bool _paused, _hookClicks, _ownedDown;
    double _lastOwnedMs = double.NegativeInfinity;
    double _releasedMs = double.NegativeInfinity;

    public ClickEngine(IMover mover, AppSettings settings)
    {
        _mover = mover;
        _s = settings;
        _hookClicks = settings.HookClicks;
        _paused = settings.StartPaused;
    }

    /// <summary>Raised on the UI thread for every button down and up ChinPad sends.</summary>
    public event Action<ClickNotice>? Notice;

    public long LeftClicks { get; private set; }
    public long RightClicks { get; private set; }
    public long MiddleClicks { get; private set; }
    /// <summary>Presses ChinPad swallowed (no zone touch, a dead zone, several zone touches, ...).</summary>
    public long PressesIgnored { get; private set; }
    public string LastIgnoredReason { get; private set; } = "";
    /// <summary>Presses with no ChinPad touch down while Windows' own click gets through: left to Windows.</summary>
    public long PressesForWindows { get; private set; }
    public long ZoneTouches { get; private set; }
    public long MoveTouches { get; private set; }
    public bool Holding => _held.Count > 0;
    /// <summary>Merging: contacts that took a neighbouring contact's classification instead of settling on their own.</summary>
    public long MergedPartners { get; private set; }
    /// <summary>Merging: presses that counted several zone contacts as one touch.</summary>
    public long MergedPresses { get; private set; }
    /// <summary>Merging: held buttons kept down by a partner when the contact holding them ended.</summary>
    public long HoldsKept { get; private set; }
    /// <summary>The last press of any pad, for the settings window's press marker; null before the first one.</summary>
    public PressInfo? LastPress { get; private set; }
    /// <summary>blockDoubleClick: second presses dropped, and the last one in words (for the log).</summary>
    public long DoubleClicksBlocked { get; private set; }
    public string LastDoubleClickText { get; private set; } = "";
    /// <summary>ClickLock: buttons locked so far.</summary>
    public long ClickLocks { get; private set; }
    /// <summary>The button ClickLock keeps down now, or null.</summary>
    public MouseButton? LockedButton => _held.Find(h => h.Locked)?.Button;
    /// <summary>True while a zone button is held that ClickLock may still lock (BackgroundMode then calls Tick).</summary>
    public bool LockPending => _s.ClickLock.Enabled && _held.Exists(h => !h.Locked && h.Contact is not null);

    /// <summary>Windows' double-click time in ms (blockDoubleClick with doubleClickWindowMs 0). A seam: tests replace it.</summary>
    public Func<double> WindowsDoubleClickMs { get; set; } = static () => InputNative.GetDoubleClickTime();

    /// <summary>The blockDoubleClick window in use: doubleClickWindowMs, or Windows' double-click time when that is 0.</summary>
    public double DoubleClickWindowMs => _s.DoubleClickWindowMs > 0 ? _s.DoubleClickWindowMs : WindowsDoubleClickMs();

    public string CountersText => string.Create(CultureInfo.InvariantCulture,
        $"clicks sent: left {LeftClicks}, right {RightClicks}, middle {MiddleClicks}; presses ignored {PressesIgnored}, left to Windows {PressesForWindows}; touches: {ZoneTouches} zone touches, {MoveTouches} move touches; second clicks ignored {DoubleClicksBlocked}; click locks {ClickLocks}");

    public string MergeText => string.Create(CultureInfo.InvariantCulture,
        $"{MergedPartners} contacts merged into a neighbouring touch, {MergedPresses} presses with merged zone contacts, {HoldsKept} held buttons kept by a partner");

    /// <summary>Paused: nothing is sent and the hook drops no clicks. Pausing releases a button ChinPad holds.</summary>
    public bool Paused
    {
        get => _paused;
        set
        {
            _paused = value;
            if (value) ReleaseAll("paused");
        }
    }

    /// <summary>
    /// New settings (live reload). Touches already decided keep their zone (in "position" mode: until they move).
    /// Switching clickLock off releases a locked button.
    /// </summary>
    public void Apply(AppSettings settings)
    {
        bool lockWasOn = _s.ClickLock.Enabled;
        _s = settings;
        _hookClicks = settings.HookClicks;
        if (lockWasOn && !settings.ClickLock.Enabled) ReleaseLocked("click lock switched off");
    }

    /// <summary>ClickLock between frames (a pad that stops reporting while held): locks a button whose press has lasted holdMs.</summary>
    public void Tick(double nowMs) => CheckLocks(nowMs);

    public void AddDevice(IntPtr handle, PadGeometry geometry, string name = "") =>
        _devices[handle] = (geometry, name.Length > 0 ? name : _devices.Count.ToString(CultureInfo.InvariantCulture));

    public void ClearDevices()
    {
        ReleaseAll("touchpads changed");
        _devices.Clear();
        _buttons.Clear();
        _contacts.Clear();
        _ended.Clear();
        _ownedDown = false;
    }

    /// <summary>True when the contact is down, decided, in a zone (any action, "none" included) and ChinPad's.</summary>
    public bool IsZoneContact(IntPtr device, int contactId) =>
        _contacts.TryGetValue((device, contactId), out Contact? c) && c.Decided && c.Zone is not null && Mine(c);

    /// <summary>
    /// CursorEngine's hook: true when the contact is ChinPad's and either still undecided (settling) or a
    /// zone contact. CursorEngine never drives such a contact. Must be called after OnFrame saw the frame.
    /// </summary>
    public bool BlocksCursor(IntPtr device, int contactId) =>
        _contacts.TryGetValue((device, contactId), out Contact? c) && Mine(c) && (!c.Decided || c.Zone is not null);

    /// <summary>The contacts of one pad that are down now (after the last frame), for the live drawing.</summary>
    public void Contacts(IntPtr device, List<ContactInfo> into)
    {
        foreach (Contact c in _contacts.Values)
            if (c.Device == device) into.Add(new ContactInfo(c.Id, c.X, c.Y, c.Decided, c.Zone, Mine(c)));
    }

    /// <summary>The zone a touch at (x, y) belongs to (null = the move area). The diagnostic uses it too.</summary>
    public static ZoneSettings? Classify(IReadOnlyList<ZoneSettings> zones, PadGeometry g, int x, int y)
    {
        (double fx, double fy) = g.Fraction(x, y);
        return ZoneSettings.Find(zones, fx, fy);
    }

    /// <summary>The same position classifier for wizard frames already expressed in millimetres.</summary>
    public static ZoneSettings? ClassifyMm(IReadOnlyList<ZoneSettings> zones, PadGeometry g, double x, double y) =>
        ZoneSettings.Find(zones, g.WidthMm > 0 ? Math.Clamp(x / g.WidthMm, 0, 1) : 0,
            g.HeightMm > 0 ? Math.Clamp(y / g.HeightMm, 0, 1) : 0);

    /// <summary>Called from the hook thread; reads only volatile / atomically written fields.</summary>
    public bool BlockClicks => BlockClicksAt(Clock.NowMs);

    public bool BlockClicksAt(double nowMs)
    {
        if (!_hookClicks || _paused) return false;
        if (_ownedDown) return nowMs - Volatile.Read(ref _lastOwnedMs) < StaleFrameMs;
        return nowMs - Volatile.Read(ref _releasedMs) < TailMs;
    }

    /// <summary>One completed frame of one touchpad, with that touchpad's contact tracks. Call it before CursorEngine.OnFrame.</summary>
    public void OnFrame(IntPtr device, Frame frame, IReadOnlyDictionary<int, ContactTrack> tracks, double nowMs)
    {
        if (!_devices.TryGetValue(device, out var dev)) return;
        _lastFrameMs = nowMs;
        _ended.Clear();
        CheckLocks(nowMs); // before this frame's lifts and releases: a press that lasted holdMs is locked first

        // 1. Contacts: note claims and positions (zoneMode "position": reclassify decided touches), drop lifted
        //    ones, then classify the touches that have settled, earliest first (merging: or follow a partner).
        foreach (Contact c in _contacts.Values)
            if (c.Device == device) c.Seen = false;
        foreach (ContactReading r in frame.Contacts)
        {
            var key = (device, r.ContactId);
            _contacts.TryGetValue(key, out Contact? c);
            if (!r.Tip)
            {
                if (c is not null) Remove(key, c, "contact lifted while pressed");
                continue;
            }
            tracks.TryGetValue(r.ContactId, out ContactTrack? t);
            double firstSeen = t?.FirstSeenMs ?? nowMs;
            if (c is not null && c.FirstSeenMs != firstSeen)
            {
                Remove(key, c, "contact ID reused"); // a new touch with the same ID
                c = null;
            }
            if (c is null)
            {
                c = new Contact { Device = device, Id = r.ContactId, FirstSeenMs = firstSeen, LandX = t?.LandX ?? r.X, LandY = t?.LandY ?? r.Y };
                _contacts[key] = c;
            }
            c.Seen = true;
            c.X = r.X;
            c.Y = r.Y;
            if (!r.Confidence) c.Claimed = true;
            if (c.Decided && _s.PositionZones) c.Zone = Classify(_s.Zones, dev.Geometry, r.X, r.Y);
        }
        foreach (Contact c in _contacts.Values.Where(c => c.Device == device && !c.Seen).ToList())
            Remove((device, c.Id), c, "contact vanished while pressed"); // missing from a complete frame without a tip-off
        DecidePending(device, dev.Geometry, nowMs, pressed: false);
        UpdateBlocking(nowMs);

        // 2. Button edges. A press decides the touches of this pad that are still settling, by where they are now.
        uint before = _buttons.GetValueOrDefault(device), now = frame.Buttons;
        _buttons[device] = now;
        if ((now & ~before & 7u) != 0) DecidePending(device, dev.Geometry, nowMs, pressed: true);
        for (int bit = 0; bit < 3; bit++)
        {
            uint mask = 1u << bit;
            if ((now & mask) != 0 && (before & mask) == 0) Press(device, dev, bit, nowMs);
            else if ((now & mask) == 0 && (before & mask) != 0) Release(device, bit, "button released", nowMs);
        }
    }

    /// <summary>Releases every button ChinPad holds (pause, touchpads changed, exit).</summary>
    public void ReleaseAll(string reason)
    {
        while (_held.Count > 0) Up(_held[^1], reason, _lastFrameMs);
    }

    void ReleaseLocked(string reason, double? nowMs = null)
    {
        foreach (Held h in _held.Where(h => h.Locked).ToList()) Up(h, reason, nowMs ?? _lastFrameMs);
    }

    /// <summary>ClickLock: a zone button (not passthrough) pressed for holdMs or longer is locked, with a notice (the double beep).</summary>
    void CheckLocks(double nowMs)
    {
        if (!_s.ClickLock.Enabled || _paused) return;
        for (int i = 0; i < _held.Count; i++)
        {
            Held h = _held[i];
            if (h.Locked || h.Contact is null || nowMs - h.DownMs < _s.ClickLock.HoldMs) continue;
            _held[i] = h with { Locked = true };
            ClickLocks++;
            Notice?.Invoke(new ClickNotice(h.Button, true, string.Create(CultureInfo.InvariantCulture,
                $"click {Name(h.Button)} locked (pressed {nowMs - h.DownMs:0} ms, clickLock holdMs {_s.ClickLock.HoldMs:0}): it stays down until the next press"), Lock: true));
        }
    }

    /// <summary>
    /// Why a press ends a click lock ("a press in the right zone"), or null when it does not: ChinPad would ignore
    /// it anyway (no zone touch with "ignore", only a dead-zone touch), so it leaves the lock alone.
    /// </summary>
    string? LockEndingPress(IntPtr device, int bit, double nowMs)
    {
        if (bit != 0) return string.Create(CultureInfo.InvariantCulture, $"a press of button {bit + 1}");
        List<Contact> zoneDown = _contacts.Values.Where(c => c.Device == device && c.Decided && c.Zone is not null && Mine(c)).ToList();
        Contact? z = zoneDown.Where(c => c.Zone!.Action != ZoneAction.None).OrderBy(c => c.FirstSeenMs).ThenBy(c => c.Id).FirstOrDefault();
        if (z is not null) return $"a press in the {z.Zone!.ActionName} zone";
        if (zoneDown.Count > 0) return null;
        if (_s.Strategy != 1 && !BlockClicksAt(nowMs)) return "a press that Windows clicks for itself";
        if (_s.Passthrough) return "a press of button 1 (passthrough)";
        return null;
    }

    /// <summary>blockDoubleClick: true (and counted) when b went down less than the window ago; the press is then dropped.</summary>
    bool SecondClick(MouseButton b, double nowMs)
    {
        if (!_s.BlockDoubleClick || !_lastDownMs.TryGetValue(b, out double last)) return false;
        double window = DoubleClickWindowMs, since = nowMs - last;
        if (since >= window) return false;
        DoubleClicksBlocked++;
        LastDoubleClickText = string.Create(CultureInfo.InvariantCulture, $"{Name(b)} pressed again {since:0} ms after the previous click (window {window:0} ms)");
        return true;
    }

    /// <summary>Strategy 2: only claimed contacts are ChinPad's; 1 and 3: every tip-on contact.</summary>
    bool Mine(Contact c) => _s.Strategy != 2 || c.Claimed;

    /// <summary>
    /// Classifies the undecided contacts of one pad, earliest first: one with a decided partner within mergeMm
    /// takes the partner's classification; otherwise zoneSettleMs 0 or no zones: by the first report; settled
    /// (zoneSettleMs passed) or pressed: by where it is now. Then counts them.
    /// </summary>
    void DecidePending(IntPtr device, PadGeometry g, double nowMs, bool pressed)
    {
        List<Contact>? pending = null;
        foreach (Contact c in _contacts.Values)
            if (c.Device == device && !c.Decided) (pending ??= []).Add(c);
        if (pending is not null)
        {
            if (pending.Count > 1) pending.Sort((a, b) => a.FirstSeenMs != b.FirstSeenMs ? a.FirstSeenMs.CompareTo(b.FirstSeenMs) : a.Id.CompareTo(b.Id));
            foreach (Contact c in pending)
            {
                if (MergeSource(c, g) is { } source)
                {
                    c.Decided = true;
                    c.Zone = source.Zone;
                    MergedPartners++;
                }
                else if (_s.ZoneSettleMs <= 0 || _s.Zones.Count == 0) Decide(c, g, c.LandX, c.LandY);
                else if (pressed || nowMs - c.FirstSeenMs >= _s.ZoneSettleMs) Decide(c, g, c.X, c.Y);
            }
        }
        foreach (Contact c in _contacts.Values)
            if (c.Device == device) Count(c);
    }

    /// <summary>The nearest decided contact of the same pad within mergeMm of c (down now, or ended in this frame); null when mergeMm is 0.</summary>
    Contact? MergeSource(Contact c, PadGeometry g)
    {
        if (_s.MergeMm <= 0) return null;
        Contact? best = null;
        double bestMm = double.MaxValue;
        void Consider(Contact o)
        {
            if (ReferenceEquals(o, c) || o.Device != c.Device || !o.Decided) return;
            double mm = DistanceMm(g, c, o);
            if (mm <= _s.MergeMm && mm < bestMm)
            {
                best = o;
                bestMm = mm;
            }
        }
        foreach (Contact o in _contacts.Values) Consider(o);
        foreach (Contact o in _ended) Consider(o);
        return best;
    }

    static double DistanceMm(PadGeometry g, Contact a, Contact b)
    {
        double dx = (a.X - b.X) * g.MmPerUnitX, dy = (a.Y - b.Y) * g.MmPerUnitY;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>
    /// The zone contacts of a press grouped into touches: contacts within mergeMm of each other (directly or
    /// through a chain) are one touch. Each touch is returned as its earliest contact (lowest ID on a tie) and
    /// its number of contacts, earliest touch first. mergeMm 0: every contact is a touch of its own.
    /// </summary>
    List<(Contact First, int Count)> Touches(List<Contact> contacts, PadGeometry g)
    {
        var parent = new int[contacts.Count];
        for (int i = 0; i < parent.Length; i++) parent[i] = i;
        int Root(int i)
        {
            while (parent[i] != i) i = parent[i];
            return i;
        }
        if (_s.MergeMm > 0)
            for (int i = 0; i < contacts.Count; i++)
                for (int j = i + 1; j < contacts.Count; j++)
                    if (DistanceMm(g, contacts[i], contacts[j]) <= _s.MergeMm) parent[Root(i)] = Root(j);
        return contacts
            .Select((c, i) => (Contact: c, Root: Root(i)))
            .GroupBy(x => x.Root)
            .Select(group => (First: group.Select(x => x.Contact).OrderBy(c => c.FirstSeenMs).ThenBy(c => c.Id).First(), Count: group.Count()))
            .OrderBy(t => t.First.FirstSeenMs).ThenBy(t => t.First.Id)
            .ToList();
    }

    void Decide(Contact c, PadGeometry g, int x, int y)
    {
        c.Decided = true;
        c.Zone = Classify(_s.Zones, g, x, y);
    }

    /// <summary>Counts a decided contact of ChinPad's once, as a zone or a move touch.</summary>
    void Count(Contact c)
    {
        if (c.Counted || !c.Decided || !Mine(c)) return;
        c.Counted = true;
        if (c.Zone is null) MoveTouches++;
        else ZoneTouches++;
    }

    void Press(IntPtr device, (PadGeometry Geometry, string Name) dev, int bit, double nowMs)
    {
        if (_paused) return;
        if (_held.Exists(h => h.Locked) && LockEndingPress(device, bit, nowMs) is string why)
        {
            // This press only ends the lock: the pending up, nothing else (and blockDoubleClick does not apply).
            ReleaseLocked("lock released by " + why, nowMs);
            Contact? mark = _contacts.Values.Where(c => c.Device == device)
                .OrderBy(c => c.Zone is null || c.Zone.Action == ZoneAction.None).ThenBy(c => !Mine(c)).ThenBy(c => c.FirstSeenMs).FirstOrDefault();
            NotePress(device, mark, nowMs, "lock released");
            return;
        }
        List<Contact> zoneDown = _contacts.Values.Where(c => c.Device == device && c.Decided && c.Zone is not null && Mine(c)).ToList();
        if (zoneDown.Count > 0)
        {
            List<(Contact First, int Count)> touches = Touches(zoneDown, dev.Geometry);
            Contact z = touches[0].First;
            string outcome = "ignored";
            if (bit != 0) Ignore(string.Create(CultureInfo.InvariantCulture, $"button {bit + 1} pressed while a zone touch is down"));
            else if (touches.Any(t => t.First.Zone!.Action == ZoneAction.None)) Ignore("pressed while a touch in a \"none\" zone is down");
            else if (touches.Count > 1) Ignore(string.Create(CultureInfo.InvariantCulture, $"pressed with {touches.Count} zone touches down"));
            else if (SecondClick(ButtonOf(z.Zone!.Action), nowMs))
            {
                outcome = "second click ignored";
            }
            else
            {
                (double x, double y) = dev.Geometry.Mm(z.X, z.Y);
                int merged = touches[0].Count;
                if (merged > 1) MergedPresses++;
                if (Down(ButtonOf(z.Zone!.Action), device, bit, z, nowMs,
                        string.Create(CultureInfo.InvariantCulture, $"{z.Zone.ActionName} zone, pad {dev.Name} contact {z.Id} at {x:0.0}, {y:0.0} mm{(merged > 1 ? $", {merged} contacts merged into one touch" : "")}")))
                    outcome = z.Zone.ActionName;
            }
            NotePress(device, z, nowMs, outcome);
            return;
        }

        // Where the press marker goes when no zone touch is down: ChinPad's earliest contact (any contact if none is ChinPad's).
        Contact? at = _contacts.Values.Where(c => c.Device == device).OrderBy(c => !Mine(c)).ThenBy(c => c.FirstSeenMs).FirstOrDefault();

        // No zone touch down. If Windows' own click is not being dropped, the press is Windows' (a finger).
        if (_s.Strategy != 1 && !BlockClicksAt(nowMs))
        {
            PressesForWindows++;
            NotePress(device, at, nowMs, "Windows");
            return;
        }
        if (!_s.Passthrough)
        {
            Ignore(bit == 0 ? "pressed with no zone touch down" : string.Create(CultureInfo.InvariantCulture, $"button {bit + 1} pressed (pressOutsideZones is \"ignore\")"));
            NotePress(device, at, nowMs, "ignored");
            return;
        }
        MouseButton b = bit switch { 0 => MouseButton.Left, 1 => MouseButton.Right, _ => MouseButton.Middle };
        if (SecondClick(b, nowMs))
        {
            NotePress(device, at, nowMs, "second click ignored");
            return;
        }
        bool sent = Down(b, device, bit, null, nowMs, string.Create(CultureInfo.InvariantCulture, $"passthrough of button {bit + 1}, pad {dev.Name}"));
        NotePress(device, at, nowMs, sent ? Name(b) : "ignored");
    }

    void NotePress(IntPtr device, Contact? at, double nowMs, string outcome) =>
        LastPress = new PressInfo(device, at is not null, at?.X ?? 0, at?.Y ?? 0, nowMs, outcome);

    void Release(IntPtr device, int bit, string reason, double nowMs)
    {
        Held? h = _held.Find(x => !x.Locked && x.Device == device && x.Bit == bit); // a locked button stays down
        if (h is not null) Up(h, reason, nowMs);
    }

    /// <summary>
    /// A contact ended: forget it, and release the button it holds, unless a partner of ChinPad's within mergeMm
    /// is still down (it then holds the button) or the button is locked (ClickLock: lifts never release it).
    /// Still undecided: classified by its last position, for the counters.
    /// </summary>
    void Remove((IntPtr, int) key, Contact c, string reason)
    {
        _contacts.Remove(key);
        PadGeometry? g = _devices.TryGetValue(c.Device, out var dev) ? dev.Geometry : null;
        if (!c.Decided && g is not null)
        {
            Decide(c, g, c.X, c.Y);
            Count(c);
        }
        _ended.Add(c);
        int i = _held.FindIndex(x => !x.Locked && ReferenceEquals(x.Contact, c));
        if (i < 0) return;
        Contact? partner = g is null || _s.MergeMm <= 0 ? null
            : _contacts.Values
                .Where(o => o.Device == c.Device && Mine(o) && DistanceMm(g, o, c) <= _s.MergeMm)
                .OrderBy(o => DistanceMm(g, o, c))
                .FirstOrDefault();
        if (partner is not null)
        {
            _held[i] = _held[i] with { Contact = partner };
            HoldsKept++;
        }
        else
        {
            Up(_held[i], reason, _lastFrameMs);
        }
    }

    /// <summary>Sends a button down; false when ChinPad already holds that button (the press is then ignored).</summary>
    bool Down(MouseButton b, IntPtr device, int bit, Contact? contact, double nowMs, string what)
    {
        if (_held.Any(h => h.Button == b))
        {
            Ignore($"{Name(b)} is already held down by ChinPad");
            return false;
        }
        _mover.Button(b, true);
        _held.Add(new Held(b, device, bit, contact, nowMs));
        _lastDownMs[b] = nowMs;
        if (b == MouseButton.Left) LeftClicks++;
        else if (b == MouseButton.Right) RightClicks++;
        else MiddleClicks++;
        Notice?.Invoke(new ClickNotice(b, true, $"click {Name(b)} down ({what})"));
        return true;
    }

    void Up(Held h, string reason, double nowMs)
    {
        _held.Remove(h);
        _mover.Button(h.Button, false);
        Notice?.Invoke(new ClickNotice(h.Button, false,
            string.Create(CultureInfo.InvariantCulture, $"click {Name(h.Button)} up ({reason}, held {Math.Max(0, nowMs - h.DownMs):0} ms)")));
    }

    void Ignore(string reason)
    {
        PressesIgnored++;
        LastIgnoredReason = reason;
    }

    /// <summary>Is a contact ChinPad owns down on any touchpad? Same pattern as CursorEngine's strategy-3 bookkeeping.</summary>
    void UpdateBlocking(double nowMs)
    {
        bool any = false;
        foreach (Contact c in _contacts.Values)
            if (Mine(c)) { any = true; break; }
        if (any)
        {
            Volatile.Write(ref _lastOwnedMs, nowMs);
            _ownedDown = true;
        }
        else if (_ownedDown)
        {
            Volatile.Write(ref _releasedMs, nowMs);
            _ownedDown = false;
        }
    }

    static MouseButton ButtonOf(ZoneAction a) => a switch
    {
        ZoneAction.Right => MouseButton.Right,
        ZoneAction.Middle => MouseButton.Middle,
        _ => MouseButton.Left,
    };

    public static string Name(MouseButton b) => b.ToString().ToLowerInvariant();

    /// <summary>The zones in mm on one pad, for the log: one line per zone, plus a line per overlap.</summary>
    public static List<string> DescribeZones(IReadOnlyList<ZoneSettings> zones, PadGeometry g)
    {
        var lines = new List<string>();
        if (zones.Count == 0) lines.Add("no zones: every touch moves the cursor");
        for (int i = 0; i < zones.Count; i++)
        {
            ZoneSettings z = zones[i];
            double x0 = z.XMin * g.WidthMm, x1 = z.XMax * g.WidthMm, y0 = z.YMin * g.HeightMm, y1 = z.YMax * g.HeightMm;
            lines.Add(string.Create(CultureInfo.InvariantCulture,
                $"zone {i + 1} {z.ActionName}: x {x0:0.0}-{x1:0.0} mm, y {y0:0.0}-{y1:0.0} mm from the top-left corner ({x1 - x0:0.0} x {y1 - y0:0.0} mm)"));
            for (int j = 0; j < i; j++)
            {
                ZoneSettings o = zones[j];
                if (z.XMin < o.XMax && o.XMin < z.XMax && z.YMin < o.YMax && o.YMin < z.YMax)
                    lines.Add(string.Create(CultureInfo.InvariantCulture, $"zones {j + 1} and {i + 1} overlap; zone {j + 1} wins there (first match)"));
            }
        }
        return lines;
    }
}
