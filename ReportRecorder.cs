using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;

namespace ChinPad;

/// <summary>
/// The report CSV of "--diag" and of the diagnostic bundle: one row per contact reading of each report (the
/// contacts of the frame, plus slots beyond Contact Count that still carry data, noted "beyond-count"), a row
/// with empty contact columns for a report with none, one row per contact that vanished when the report
/// completed a frame, and marker rows. The zone column follows the background mode's classification rule
/// (zones, zoneSettleMs, zoneMode of the settings given at construction) but ignores ownership and merging.
/// Columns: see CsvHeader and the README ("CSV columns").
/// Threading: rows come from the UI thread; Flush and Close may also come from another thread (the console
/// closing), so the writer is guarded by a lock.
/// </summary>
internal sealed class ReportRecorder : IDisposable
{
    public const string CsvHeader = "t_ms,wallclock,device,report_id,frame,contact_count,scan_time,contact_id,tip,confidence,x,y,w,h,button,age_ms,note,marker,pressure,zone";
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>The zone decision of one touch, like ClickEngine's: Zone null = undecided; X/Y = last tip-on position.</summary>
    sealed class ZoneState
    {
        public string? Zone;
        public int X, Y;
        public bool Seen; // X/Y hold a tip-on position
    }

    readonly IReadOnlyList<ZoneSettings> _zones;
    readonly double _settleMs;
    readonly bool _positionZones;
    readonly ConditionalWeakTable<ContactTrack, ZoneState> _zoneStates = new(); // per touch: a reused ID gets a new track
    readonly object _lock = new();
    StreamWriter? _csv;
    long _rows;

    public ReportRecorder(AppSettings settings)
    {
        _zones = settings.Zones;
        _settleMs = settings.ZoneSettleMs;
        _positionZones = settings.PositionZones;
    }

    public string Path { get; private set; } = "";
    public long Rows => Interlocked.Read(ref _rows);
    public int Markers { get; private set; }

    /// <summary>Creates (or overwrites) the CSV and writes the header. Throws when the file cannot be created.</summary>
    public void Open(string path)
    {
        var csv = new StreamWriter(path, false, new UTF8Encoding(false));
        csv.WriteLine(CsvHeader);
        csv.Flush();
        lock (_lock)
        {
            _csv = csv;
            Path = path;
        }
    }

    /// <summary>
    /// The rows of one report: one per contact reading (the contacts of the frame and the slots beyond Contact
    /// Count that still carry data), one with empty contact columns if there are none, and one per contact that
    /// vanished when this report completed a frame. <paramref name="pressed"/>: a button bit went 0 -> 1 in
    /// this report (it decides the touches that are still settling).
    /// </summary>
    public void WriteReport(int device, PadGeometry geometry, FrameAssembler asm, ContactReport r, ReportOutcome o, double t, bool pressed)
    {
        string wall = Wall();
        string pending = o.SupersededIncompleteFrame ? "prev-frame-incomplete" : "";
        foreach (ReadingOutcome ro in o.Readings)
        {
            ContactReading c = ro.Reading;
            Row(t, wall, device, r.ReportId, o.FrameNumber, r.ContactCount, r.ScanTime, c.ContactId, c.Tip, c.Confidence,
                c.X, c.Y, c.W, c.H, r.Buttons, ro.AgeMs, JoinNotes(ro.Note, pending), "", c.Pressure, ro.InFrame ? UpdateZone(geometry, asm, c, t, pressed) : "");
            pending = "";
        }
        if (o.Readings.Count == 0)
            Row(t, wall, device, r.ReportId, o.FrameNumber, r.ContactCount, r.ScanTime, null, null, null,
                null, null, null, null, r.Buttons, null, pending, "", null, "");
        foreach (VanishedContact v in o.Vanished)
            Row(t, wall, device, r.ReportId, o.Completed?.Number ?? o.FrameNumber, r.ContactCount, r.ScanTime, v.ContactId, null, null,
                null, null, null, null, r.Buttons, v.DurationMs, "vanished", "", null, Ended(geometry, asm, v.ContactId));
    }

    /// <summary>A marker row ("finger", "chin", ...) at time t, flushed at once.</summary>
    public void Marker(string name, double t)
    {
        Row(t, Wall(), null, null, null, null, null, null, null, null, null, null, null, null, null, null, "", name, null, "");
        Markers++;
        Flush();
    }

    public void Row(double t, string wall, int? dev, byte? reportId, long? frame, int? count, int? scan, int? id, bool? tip, bool? conf,
                    int? x, int? y, int? w, int? h, uint? buttons, double? age, string note, string marker, int? pressure, string zone)
    {
        string line = string.Join(',',
            t.ToString("0.00", Inv), wall, N(dev), N(reportId), N(frame), N(count), N(scan), N(id), B(tip), B(conf),
            N(x), N(y), N(w), N(h), N(buttons), age is double a ? a.ToString("0.0", Inv) : "", note, marker, N(pressure), zone);
        lock (_lock)
        {
            if (_csv is null) return;
            _csv.WriteLine(line);
            _rows++;
        }
    }

    public void Flush()
    {
        lock (_lock)
        {
            try { _csv?.Flush(); } catch (Exception) { }
        }
    }

    /// <summary>Flushes and closes the file; later rows are dropped. Safe to call more than once.</summary>
    public void Close()
    {
        lock (_lock)
        {
            try
            {
                _csv?.Flush();
                _csv?.Dispose();
            }
            catch (Exception) { }
            _csv = null;
        }
    }

    public void Dispose() => Close();

    /// <summary>The zone of a touch as decided by its rows so far ("undecided" while it settles; "" if it has no row yet).</summary>
    public string ZoneText(ContactTrack t) =>
        _zoneStates.TryGetValue(t, out ZoneState? z) ? z.Zone ?? "undecided" : "";

    /// <summary>
    /// The zone column for one reading, by ClickEngine's rule: "undecided" until zoneSettleMs after the
    /// touch's first report, then the zone where it was at that moment (left/right/middle/none, or move);
    /// zoneMode "position": where it is now. A press or a lift decides at once; zoneSettleMs 0 or no zones:
    /// the landing position. Unlike the background mode it ignores ownership (a strategy-2 finger shows its
    /// zone too) and merging, and it goes by report rather than by frame (the same on the Synaptics pad here,
    /// one report a frame).
    /// </summary>
    string UpdateZone(PadGeometry g, FrameAssembler asm, in ContactReading c, double now, bool pressed)
    {
        if (!asm.Tracks.TryGetValue(c.ContactId, out ContactTrack? t)) return "";
        ZoneState z = _zoneStates.GetValue(t, _ => new ZoneState());
        if (!c.Tip) return Decided(g, t, z); // lift: an undecided touch is decided by its last tip-on position
        if (z.Zone is null)
        {
            if (_settleMs <= 0 || _zones.Count == 0) z.Zone = ZoneName(g, t.LandX, t.LandY);
            else if (pressed || now - t.FirstSeenMs >= _settleMs) z.Zone = ZoneName(g, c.X, c.Y);
        }
        else if (_positionZones)
        {
            z.Zone = ZoneName(g, c.X, c.Y);
        }
        (z.X, z.Y, z.Seen) = (c.X, c.Y, true);
        return z.Zone ?? "undecided";
    }

    /// <summary>The zone column of a contact that vanished: undecided touches are decided by their last position.</summary>
    string Ended(PadGeometry g, FrameAssembler asm, int contactId) =>
        asm.Tracks.TryGetValue(contactId, out ContactTrack? t) ? Decided(g, t, _zoneStates.GetValue(t, _ => new ZoneState())) : "";

    /// <summary>The zone of a touch that ended; still undecided: decided now by its last tip-on position.</summary>
    string Decided(PadGeometry g, ContactTrack t, ZoneState z) =>
        z.Zone ??= z.Seen ? ZoneName(g, z.X, z.Y) : ZoneName(g, t.LandX, t.LandY);

    string ZoneName(PadGeometry g, int x, int y) => ClickEngine.Classify(_zones, g, x, y)?.ActionName ?? "move";

    static string N<T>(T? v) where T : struct, IFormattable => v is T x ? x.ToString(null, Inv) : "";
    static string B(bool? v) => v is null ? "" : v.Value ? "1" : "0";
    static string Wall() => DateTime.Now.ToString("HH:mm:ss.fff", Inv);
    static string JoinNotes(string a, string b) => a.Length == 0 ? b : b.Length == 0 ? a : a + ";" + b;
}
