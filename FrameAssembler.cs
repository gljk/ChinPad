using System.Diagnostics;

namespace ChinPad;

/// <summary>Monotonic millisecond clock (Stopwatch) shared by the assembler, the live view and the CSV.</summary>
internal static class Clock
{
    static readonly long Start = Stopwatch.GetTimestamp();
    public static double NowMs => Stopwatch.GetElapsedTime(Start).TotalMilliseconds;
}

/// <summary>What is known about one contact ID during one touch, from its first report to its lift.</summary>
internal sealed class ContactTrack
{
    public required int ContactId { get; init; }
    public required double FirstSeenMs { get; init; }
    /// <summary>Position of the first report of this touch (logical units): with zoneSettleMs 0 it decides the button zone.</summary>
    public int LandX { get; init; }
    public int LandY { get; init; }
    public double LastSeenMs { get; set; }
    public double LastConfidenceChangeMs { get; set; }
    public bool Tip { get; set; }
    public bool Confidence { get; set; }
    public int ConfidenceFlips { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int? W { get; set; }
    public int? H { get; set; }
    public int? Pressure { get; set; }
    /// <summary>Ended: either a tip-off report arrived, or the contact vanished from a complete frame.</summary>
    public bool Lifted { get; set; }
    /// <summary>Ended without a tip-off report ("dropped while still down").</summary>
    public bool Vanished { get; set; }
    /// <summary>When the end was noticed (used to keep lifted rows on screen for a moment).</summary>
    public double LiftedMs { get; set; }

    double EndMs(double nowMs) => Lifted ? LastSeenMs : nowMs;
    /// <summary>ms since first seen; for an ended contact: how long it lasted (first to last report).</summary>
    public double AgeMs(double nowMs) => EndMs(nowMs) - FirstSeenMs;
    public double MsSinceConfidenceChange(double nowMs) => EndMs(nowMs) - LastConfidenceChangeMs;
}

/// <summary>The contacts of one scan of the sensor, possibly gathered from several reports.</summary>
internal sealed class Frame
{
    public required long Number { get; init; }
    /// <summary>Contact Count of the report that opened the frame (null if the device or report has none).</summary>
    public int? DeclaredCount { get; init; }
    public int Remaining { get; set; }
    public int Reports { get; set; }
    public int? ScanTime { get; set; }
    public uint Buttons { get; set; }
    /// <summary>A new frame started before this one received all its contacts.</summary>
    public bool Incomplete { get; set; }
    public List<ContactReading> Contacts { get; } = [];
}

/// <summary>A reading of the report worth logging: InFrame = counted as a contact; otherwise data beyond Contact Count.</summary>
internal readonly record struct ReadingOutcome(ContactReading Reading, bool InFrame, string Note, double? AgeMs);

/// <summary>A contact that was down in the previous frame and is simply missing from this one (no tip-off).</summary>
internal readonly record struct VanishedContact(int ContactId, double DurationMs, bool LastConfidence);

/// <summary>What the assembler made of one report.</summary>
internal sealed class ReportOutcome
{
    public long FrameNumber { get; set; }
    public List<ReadingOutcome> Readings { get; } = [];
    public List<VanishedContact> Vanished { get; } = [];
    /// <summary>Set when this report completed a frame.</summary>
    public Frame? Completed { get; set; }
    /// <summary>This report opened a new frame while the previous one was still waiting for contacts.</summary>
    public bool SupersededIncompleteFrame { get; set; }
}

/// <summary>
/// Turns HID reports into frames and tracks each contact ID over time.
///
/// Precision Touchpads send contacts in one of two ways:
///  - parallel mode: every report carries all contacts (Contact Count = number of contacts, and the
///    device has at least that many finger collections);
///  - hybrid mode: the device has fewer finger collections than it can track (e.g. 5 slots, 10 contacts).
///    The first report of a frame carries Contact Count = N and the first slots; follow-up reports carry
///    Contact Count = 0 and the remaining contacts in their first slots.
/// One rule handles both: Contact Count > 0 opens a frame with remaining = N; each report contributes
/// min(remaining, finger collections in this report) contacts, taken in collection order; the frame is
/// complete when remaining reaches 0. Slots beyond the count are padding and are not contacts (if they
/// still hold data, it is reported as "beyond-count" for the diagnostic). A report with Contact Count 0
/// and no open frame is a frame with no contacts (its button state still counts). Devices without a
/// Contact Count usage get one frame per report made of the slots that carry data.
///
/// Per contact ID it keeps first/last-seen time, the last confidence and when it last changed, and it
/// detects "vanished" contacts: down in an earlier frame, missing from a complete frame, never lifted.
/// </summary>
internal sealed class FrameAssembler
{
    readonly bool _useContactCount;
    readonly Dictionary<int, ContactTrack> _tracks = [];
    Frame? _open;
    long _lastFrameNumber;

    public FrameAssembler(bool deviceHasContactCount) => _useContactCount = deviceHasContactCount;

    public IReadOnlyDictionary<int, ContactTrack> Tracks => _tracks;
    public long CompletedFrames { get; private set; }
    public long IncompleteFrames { get; private set; }
    public Frame? LastFrame { get; private set; }

    public ReportOutcome Process(ContactReport report, double nowMs)
    {
        var outcome = new ReportOutcome();
        int? count = _useContactCount ? report.ContactCount : null;
        bool selfContained = false; // no Contact Count to go by: this report is a whole frame

        if (count > 0)
        {
            if (_open is not null)
            {
                // The device started a new frame although the previous one never got all its contacts.
                _open.Incomplete = true;
                IncompleteFrames++;
                LastFrame = _open;
                outcome.SupersededIncompleteFrame = true;
            }
            _open = NewFrame(count, count.Value);
        }
        else if (_open is null)
        {
            selfContained = count is null;
            _open = NewFrame(count, 0); // count 0: an empty frame; count absent: one frame per report
        }
        // else: continuation report of a hybrid frame (Contact Count 0 or absent while a frame is open).

        Frame frame = _open;
        frame.Reports++;
        frame.Buttons |= report.Buttons;
        frame.ScanTime ??= report.ScanTime;
        outcome.FrameNumber = frame.Number;

        int take = selfContained ? report.Contacts.Count : Math.Min(frame.Remaining, report.Contacts.Count);
        for (int i = 0; i < report.Contacts.Count; i++)
        {
            ContactReading c = report.Contacts[i];
            if (i < take)
            {
                if (!c.HasData) continue; // an empty slot inside the count: nothing to track
                frame.Contacts.Add(c);
                string note = UpdateTrack(c, nowMs, out double age);
                outcome.Readings.Add(new ReadingOutcome(c, true, note, age));
            }
            else if (c.HasData)
            {
                double? age = _tracks.TryGetValue(c.ContactId, out ContactTrack? t) ? t.AgeMs(nowMs) : null;
                outcome.Readings.Add(new ReadingOutcome(c, false, "beyond-count", age));
            }
        }

        if (!selfContained) frame.Remaining -= take;
        if (selfContained || frame.Remaining <= 0) Complete(frame, nowMs, outcome);
        return outcome;
    }

    /// <summary>Forget contacts that ended more than keepLiftedMs ago (the live view shows them greyed until then).</summary>
    public void Prune(double nowMs, double keepLiftedMs)
    {
        List<int>? dead = null;
        foreach (ContactTrack t in _tracks.Values)
            if (t.Lifted && nowMs - t.LiftedMs > keepLiftedMs)
                (dead ??= []).Add(t.ContactId);
        if (dead is not null)
            foreach (int id in dead) _tracks.Remove(id);
    }

    Frame NewFrame(int? declared, int remaining) =>
        new() { Number = ++_lastFrameNumber, DeclaredCount = declared, Remaining = remaining };

    void Complete(Frame frame, double nowMs, ReportOutcome outcome)
    {
        _open = null;
        CompletedFrames++;
        LastFrame = frame;
        outcome.Completed = frame;

        // Vanished: still down (never lifted) but absent from this complete frame. Only complete frames are
        // checked, so a hybrid frame that is still being assembled cannot raise false alarms.
        var present = new HashSet<int>();
        foreach (ContactReading c in frame.Contacts) present.Add(c.ContactId);
        foreach (ContactTrack t in _tracks.Values)
        {
            if (t.Lifted || present.Contains(t.ContactId)) continue;
            t.Lifted = true;
            t.Vanished = true;
            t.LiftedMs = nowMs;
            outcome.Vanished.Add(new VanishedContact(t.ContactId, t.AgeMs(nowMs), t.Confidence));
        }
    }

    /// <summary>Updates the track of one contact; returns the CSV note ("confidence-flip" or "").</summary>
    string UpdateTrack(in ContactReading c, double nowMs, out double ageMs)
    {
        string note = "";
        if (!_tracks.TryGetValue(c.ContactId, out ContactTrack? t) || (t.Lifted && c.Tip))
        {
            // First report of a new touch, or an ID reused after its lift: start a fresh track.
            t = new ContactTrack
            {
                ContactId = c.ContactId,
                FirstSeenMs = nowMs,
                LandX = c.X,
                LandY = c.Y,
                LastConfidenceChangeMs = nowMs,
                Confidence = c.Confidence,
            };
            _tracks[c.ContactId] = t;
        }
        else if (t.Lifted)
        {
            // Another tip-off report for a contact that already ended: keep the track as it ended.
            ageMs = t.AgeMs(nowMs);
            return note;
        }
        else if (t.Confidence != c.Confidence)
        {
            note = "confidence-flip";
            t.ConfidenceFlips++;
            t.LastConfidenceChangeMs = nowMs;
        }

        t.Tip = c.Tip;
        t.Confidence = c.Confidence;
        t.X = c.X;
        t.Y = c.Y;
        t.W = c.W;
        t.H = c.H;
        t.Pressure = c.Pressure;
        t.LastSeenMs = nowMs;
        if (!c.Tip)
        {
            t.Lifted = true; // normal lift: the device sent the contact once more with Tip Switch off
            t.LiftedMs = nowMs;
        }
        ageMs = t.AgeMs(nowMs);
        return note;
    }
}
