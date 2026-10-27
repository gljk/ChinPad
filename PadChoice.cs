using System.Globalization;
using System.Text.RegularExpressions;

namespace ChinPad;

/// <summary>
/// One entry of the settings window's "Apply to" list: its number in the list (1 = first touchpad), the readable
/// label, the settings "device" value that selects exactly this pad, the full path, the index used in the log
/// (#0, #1, ...), whether it sent reports in the last 2 s, and whether the background mode uses it now.
/// </summary>
internal sealed record TouchpadChoice(int Number, string Label, string Device, string Path, int Index, bool Reporting, bool InUse);

/// <summary>
/// The touchpads as the settings window's "Apply to" list shows them (0.5.0): readable labels and the "device"
/// value that selects exactly one pad. Pure string logic, so it can be checked with made-up paths.
/// A raw-input path is \\?\HID#hardware ID#instance ID#{interface class}. Built-in pads (I2C, like the Synaptics
/// one here) have an ACPI hardware ID such as "SYNA2BA6&amp;Col02"; USB and Bluetooth devices name their vendor:
/// "VID_046D&amp;PID_C548&amp;MI_03" (USB), "{00001124-...}_VID&amp;0002046d_PID&amp;b035&amp;Col02" (Bluetooth),
/// "{00001812-...}_Dev_VID&amp;02045e_PID&amp;0b13_REV&amp;..." (Bluetooth LE). A pad with a vendor ID is "external".
/// </summary>
internal static class PadChoice
{
    /// <summary>The words of the labels, in one place for a later translation.</summary>
    public static class Text
    {
        public static string Touchpad => L10n.T("Pad.Touchpad");
        public static string BuiltIn => L10n.T("Pad.BuiltIn");
        public static string External => L10n.T("Pad.External");
        public static string ReportingNow => L10n.T("Pad.ReportingNow");
    }

    /// <summary>How long after its last report a pad still counts as "reporting now".</summary>
    public const double ReportingMs = 2000;

    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    const RegexOptions Opt = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
    static readonly Regex ColPart = new("&Col[0-9A-F]+", Opt);
    static readonly Regex UsbVid = new(@"VID_([0-9A-F]{4})", Opt);
    static readonly Regex UsbPid = new(@"PID_([0-9A-F]{4})", Opt);
    static readonly Regex BtVid = new(@"VID&([0-9A-F]{4,8})", Opt);   // Bluetooth: a source prefix, then the 4-digit vendor ID
    static readonly Regex BtPid = new(@"PID&([0-9A-F]{4})", Opt);

    /// <summary>Vendor names for the vendor IDs of common touchpads; others show the hex ID only.</summary>
    static readonly Dictionary<uint, string> Vendors = new()
    {
        [0x046D] = "Logitech", [0x06CB] = "Synaptics", [0x04F3] = "ELAN", [0x045E] = "Microsoft",
        [0x05AC] = "Apple", [0x1A7C] = "Razer", [0x2808] = "Goodix", [0x27C6] = "Goodix",
    };

    /// <summary>The hardware-ID part of a path, e.g. "SYNA2BA6&amp;Col02" (the whole path when it has no '#').</summary>
    public static string HardwareId(string path)
    {
        string[] p = path.Split('#');
        return p.Length > 1 ? p[1] : path;
    }

    /// <summary>Vendor and product ID named in the path (USB "VID_xxxx&amp;PID_xxxx", Bluetooth "VID&amp;..xxxx_PID&amp;xxxx"); null for built-in pads.</summary>
    public static (uint Vid, uint Pid)? VidPid(string path)
    {
        string id = HardwareId(path);
        Match v = UsbVid.Match(id), p = UsbPid.Match(id);
        if (!v.Success)
        {
            v = BtVid.Match(id);
            p = BtPid.Match(id);
        }
        if (!v.Success) return null;
        uint vid = uint.Parse(v.Groups[1].Value[^4..], NumberStyles.HexNumber, Inv);
        uint pid = p.Success ? uint.Parse(p.Groups[1].Value, NumberStyles.HexNumber, Inv) : 0;
        return (vid, pid);
    }

    /// <summary>True for USB and Bluetooth devices (the path names a vendor ID).</summary>
    public static bool IsExternal(string path) => VidPid(path) is not null;

    public static string? VendorName(uint vid) => Vendors.TryGetValue(vid, out string? name) ? name : null;

    /// <summary>
    /// "Touchpad 1: built-in SYNA2BA6 (132 x 77 mm)" or "Touchpad 2: external Logitech 046D:C548 (117 x 76 mm)";
    /// an unknown vendor shows only the hex IDs, and the size is left out when the pad does not report it.
    /// </summary>
    public static string Label(int number, string path, double? widthMm, double? heightMm, bool english = false)
    {
        string T(string key) => english ? Strings.English[key] : L10n.T(key);
        string F(string key, params object[] args) => string.Format(english ? Inv : L10n.Culture, T(key), args);
        string what;
        if (VidPid(path) is (uint vid, uint pid))
        {
            string ids = string.Create(Inv, $"{vid:X4}:{pid:X4}");
            what = VendorName(vid) is string name ? $"{T("Pad.External")} {name} {ids}" : $"{T("Pad.External")} {ids}";
        }
        else
        {
            what = $"{T("Pad.BuiltIn")} {ColPart.Replace(HardwareId(path), "")}";
        }
        string size = widthMm is double w && heightMm is double h ? F("Pad.Size", w, h) : "";
        return F("Pad.Label", T("Pad.Touchpad"), number, what, size);
    }

    /// <summary>
    /// The settings "device" value that selects exactly this path among <paramref name="all"/> (case-insensitive
    /// substring, as TouchpadDevice.Select matches): the hardware ID ("SYNA2BA6&amp;Col02",
    /// "VID_046D&amp;PID_C548&amp;MI_03"); for two identical pads, hardware and instance ID; else the whole path.
    /// </summary>
    public static string DeviceKey(string path, IReadOnlyList<string> all)
    {
        string[] p = path.Split('#');
        var candidates = new List<string>();
        if (p.Length > 1 && p[1].Length > 0) candidates.Add(p[1]);
        if (p.Length > 2 && p[2].Length > 0) candidates.Add(p[1] + "#" + p[2]);
        foreach (string c in candidates)
            if (all.Count(o => o.Contains(c, StringComparison.OrdinalIgnoreCase)) <= 1) return c;
        return path;
    }

    /// <summary>Built-in pads first, then external ones, each in the order Windows lists them: the "Apply to" order and numbers.</summary>
    public static List<TouchpadDevice> Ordered(IEnumerable<TouchpadDevice> pads) =>
        pads.OrderBy(d => IsExternal(d.Path)).ThenBy(d => d.Index).ToList();

    /// <summary>The "Apply to" entries for these pads (numbers, labels, device keys); reporting and in-use come from the caller.</summary>
    public static List<TouchpadChoice> Choices(IReadOnlyList<TouchpadDevice> all, Func<TouchpadDevice, bool> reporting, Func<TouchpadDevice, bool> inUse)
    {
        var paths = all.Select(d => d.Path).ToList();
        var list = new List<TouchpadChoice>(all.Count);
        foreach (TouchpadDevice d in Ordered(all))
        {
            FingerCollection? f = d.Fingers.FirstOrDefault();
            int n = list.Count + 1;
            list.Add(new TouchpadChoice(n, Label(n, d.Path, f?.X?.PhysicalSpanMm, f?.Y?.PhysicalSpanMm), DeviceKey(d.Path, paths), d.Path, d.Index,
                reporting(d), inUse(d)));
        }
        return list;
    }
}

/// <summary>
/// The touchpads found, kept up to date when touchpads are plugged in or removed (0.5.0). Refresh enumerates
/// again: a pad that is still there (same raw-input handle and path) keeps its TouchpadDevice and its number
/// (#N in the log); a new one gets the lowest free number; removed ones are disposed. Select applies the settings
/// "device", falling back to all touchpads while the one it names is not connected. Enumerate is the seam the
/// checks replace to simulate a pad that comes and goes.
/// </summary>
internal sealed class TouchpadSet : IDisposable
{
    public delegate List<TouchpadDevice> Enumerator(out List<string> errors);

    public Enumerator Enumerate { get; set; }
    /// <summary>The touchpads found, by number. The list object stays the same; Refresh changes its content.</summary>
    public List<TouchpadDevice> All { get; } = [];
    /// <summary>Touchpads that could not be opened (path: reason).</summary>
    public List<string> Errors { get; } = [];

    public TouchpadSet(Enumerator? enumerate = null)
    {
        Enumerate = enumerate ?? TouchpadDevice.EnumerateAll;
        All.AddRange(Enumerate(out List<string> errors));
        Errors.AddRange(errors);
    }

    public bool Contains(IntPtr handle) => All.Exists(d => d.Handle == handle);

    /// <summary>Enumerates again. Returns the changes as log lines (added, removed, could not open); empty when nothing changed.</summary>
    public List<string> Refresh()
    {
        List<TouchpadDevice> found = Enumerate(out List<string> errors);
        var kept = new List<TouchpadDevice>();
        var added = new List<TouchpadDevice>();
        foreach (TouchpadDevice d in found)
        {
            TouchpadDevice? old = All.Find(o => o.Handle == d.Handle && string.Equals(o.Path, d.Path, StringComparison.OrdinalIgnoreCase) && !kept.Contains(o));
            if (old is null)
            {
                added.Add(d);
                continue;
            }
            kept.Add(old);
            if (!ReferenceEquals(old, d)) d.Dispose();
        }
        List<TouchpadDevice> removed = All.Where(o => !kept.Contains(o)).ToList();
        var used = kept.Select(k => k.Index).ToHashSet();
        foreach (TouchpadDevice d in added)
        {
            int i = 0;
            while (used.Contains(i)) i++;
            d.Index = i;
            used.Add(i);
        }

        var lines = new List<string>();
        foreach (TouchpadDevice d in removed) lines.Add($"touchpad removed: #{d.Index} {d.ShortName}");
        foreach (TouchpadDevice d in added) lines.Add($"touchpad added: #{d.Index} {d.ShortName}: {d.Fingers.Count} finger collections, {d.SummaryLine()}");
        foreach (string e in errors.Where(e => !Errors.Contains(e))) lines.Add("could not open: " + e);
        foreach (TouchpadDevice d in removed) d.Dispose();
        All.Clear();
        All.AddRange(kept.Concat(added).OrderBy(d => d.Index));
        Errors.Clear();
        Errors.AddRange(errors);
        return lines;
    }

    /// <summary>
    /// The touchpads the settings "device" selects (TouchpadDevice.Select). When it names a touchpad that is not
    /// connected while others are: all of them, and <paramref name="fallback"/> says so (a line for the log).
    /// </summary>
    public List<TouchpadDevice> Select(string? device, out string? fallback)
    {
        fallback = null;
        List<TouchpadDevice> chosen = TouchpadDevice.Select(All, device);
        if (chosen.Count > 0 || All.Count == 0) return chosen;
        fallback = $"no touchpad path contains \"{device}\" (settings \"device\"; not connected?): using all touchpads until it is back";
        return All.ToList();
    }

    public void Dispose()
    {
        foreach (TouchpadDevice d in All) d.Dispose();
        All.Clear();
    }
}
