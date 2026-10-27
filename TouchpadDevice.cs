using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using ChinPad.Native;
using static ChinPad.Native.HidNative;
using static ChinPad.Native.RawInputNative;

namespace ChinPad;

/// <summary>
/// One entry of GetRawInputDeviceList with its path and, for HID devices, its top-level usage.
/// Raw input lists every HID top-level collection (TLC) separately; mouse and keyboard TLCs are typed
/// RIM_TYPEMOUSE / RIM_TYPEKEYBOARD instead of RIM_TYPEHID (their usage is then implied: 0x01/0x02, 0x01/0x06).
/// </summary>
internal sealed record RawDeviceEntry(IntPtr Handle, uint Type, string Path,
    uint VendorId, uint ProductId, uint Version, ushort UsagePage, ushort Usage)
{
    static readonly Regex ColPart = new("&Col[0-9A-F]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public string TypeName => Type switch
    {
        RIM_TYPEMOUSE => "mouse",
        RIM_TYPEKEYBOARD => "keyboard",
        RIM_TYPEHID => "hid",
        _ => "type" + Type.ToString(CultureInfo.InvariantCulture),
    };

    /// <summary>Hardware-ID part of the path, e.g. "SYNA2BA6&amp;Col02" or "VID_046D&amp;PID_C548&amp;MI_03&amp;Col05".</summary>
    public string ShortName
    {
        get
        {
            string[] p = Path.Split('#');
            return p.Length > 1 ? p[1] : Path;
        }
    }

    /// <summary>
    /// Key shared by all TLCs of one physical device. A path looks like
    ///   \\?\HID#SYNA2BA6&amp;Col02#5&amp;2c6f8e0b&amp;0&amp;0001#{4d1e55b2-f16f-11cf-88cb-001111000030}
    ///   [0]     [1] hardware ID     [2] instance ID              [3] interface class GUID
    /// The "&amp;ColNN" part of [1] and the last "&amp;NNNN" part of [2] are the collection number, and [3]
    /// differs between HID, mouse and keyboard interfaces, so all three are dropped. Paths without a
    /// "&amp;Col" part belong to single-collection devices and have no siblings.
    /// </summary>
    public string PhysicalKey
    {
        get
        {
            string[] p = Path.Split('#');
            if (p.Length < 3) return Path.ToUpperInvariant();
            Match m = ColPart.Match(p[1]);
            if (!m.Success) return Path.ToUpperInvariant();
            string hardwareId = p[1].Remove(m.Index, m.Length);
            int amp = p[2].LastIndexOf('&');
            string instance = amp > 0 ? p[2][..amp] : p[2];
            return (p[0] + "#" + hardwareId + "#" + instance).ToUpperInvariant();
        }
    }

    public static List<RawDeviceEntry> ListAll()
    {
        uint itemSize = (uint)Marshal.SizeOf<RAWINPUTDEVICELIST>();
        RAWINPUTDEVICELIST[] items;
        for (int attempt = 0; ; attempt++)
        {
            uint count = 0;
            if (GetRawInputDeviceList(null, ref count, itemSize) == uint.MaxValue)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "GetRawInputDeviceList (count) failed");
            items = new RAWINPUTDEVICELIST[count];
            uint got = GetRawInputDeviceList(items, ref count, itemSize);
            if (got != uint.MaxValue)
            {
                Array.Resize(ref items, (int)got);
                break;
            }
            // ERROR_INSUFFICIENT_BUFFER (122): a device arrived between the two calls, so ask again.
            int err = Marshal.GetLastWin32Error();
            if (err != 122 || attempt >= 3)
                throw new Win32Exception(err, "GetRawInputDeviceList failed");
        }

        var list = new List<RawDeviceEntry>(items.Length);
        foreach (RAWINPUTDEVICELIST item in items)
        {
            string path = ReadDeviceName(item.hDevice);
            ushort page = 0, usage = 0;
            uint vid = 0, pid = 0, version = 0;
            uint type = item.dwType;
            if (type == RIM_TYPEMOUSE)
            {
                (page, usage) = ((ushort)0x01, (ushort)0x02);
            }
            else if (type == RIM_TYPEKEYBOARD)
            {
                (page, usage) = ((ushort)0x01, (ushort)0x06);
            }
            else
            {
                // HID, or a dwType the list does not document (3 on this machine for the Synaptics
                // "Microsoft Input Configuration Device" TLC). RID_DEVICE_INFO is asked either way; for
                // the undocumented type it does not answer HID, so the dump shows "type3" with no usage.
                var info = new RID_DEVICE_INFO { cbSize = (uint)Marshal.SizeOf<RID_DEVICE_INFO>() };
                uint size = info.cbSize;
                if (GetRawInputDeviceInfo(item.hDevice, RIDI_DEVICEINFO, ref info, ref size) != uint.MaxValue &&
                    info.dwType == RIM_TYPEHID)
                {
                    type = RIM_TYPEHID;
                    vid = info.hidVendorId;
                    pid = info.hidProductId;
                    version = info.hidVersionNumber;
                    page = info.hidUsagePage;
                    usage = info.hidUsage;
                }
            }
            list.Add(new RawDeviceEntry(item.hDevice, type, path, vid, pid, version, page, usage));
        }
        return list;
    }

    static string ReadDeviceName(IntPtr hDevice)
    {
        uint chars = 0;
        GetRawInputDeviceInfo(hDevice, RIDI_DEVICENAME, IntPtr.Zero, ref chars);
        if (chars == 0) return "";
        IntPtr buffer = Marshal.AllocHGlobal((int)(chars + 1) * 2);
        try
        {
            if (GetRawInputDeviceInfo(hDevice, RIDI_DEVICENAME, buffer, ref chars) == uint.MaxValue) return "";
            return Marshal.PtrToStringUni(buffer) ?? "";
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}

/// <summary>Where one value usage lives (collection, report ID) and how to interpret it.</summary>
internal sealed record ValueField(ushort Page, ushort Usage, ushort Collection, byte ReportId, ushort BitSize, ushort ReportCount,
    int LogicalMin, int LogicalMax, int PhysicalMin, int PhysicalMax, uint UnitsExp, uint Units)
{
    public static ValueField From(in HIDP_VALUE_CAPS v, ushort usage) =>
        new(v.UsagePage, usage, v.LinkCollection, v.ReportID, v.BitSize, v.ReportCount,
            v.LogicalMin, v.LogicalMax, v.PhysicalMin, v.PhysicalMax, v.UnitsExp, v.Units);

    /// <summary>Physical extent in millimetres if the unit is a length (cm or inch), else null.</summary>
    public double? PhysicalSpanMm => HidUnits.SpanMm(PhysicalMin, PhysicalMax, Units, UnitsExp);

    /// <summary>
    /// HidP_GetUsageValue returns the raw bits. A field with a negative logical minimum is signed, so
    /// sign-extend it from BitSize bits. (Touchpad coordinates are normally unsigned.)
    /// </summary>
    public int Decode(uint raw)
    {
        if (LogicalMin < 0 && BitSize is > 0 and < 32 && (raw & (1u << (BitSize - 1))) != 0)
            return (int)(raw | ~((1u << BitSize) - 1));
        return (int)raw;
    }
}

/// <summary>An on/off usage such as Tip Switch or Confidence. Usually a button cap; some descriptors make it a 1-bit value.</summary>
internal sealed record BoolField(ushort Usage, ushort Collection, byte ReportId, bool IsButton, ValueField? Value);

/// <summary>One finger (contact slot) collection and the usages found in it.</summary>
internal sealed class FingerCollection
{
    public required ushort Collection { get; init; }
    public required int Order { get; init; }                 // position among finger collections (0-based)
    public required IReadOnlySet<byte> ReportIds { get; init; }
    public ValueField? ContactId { get; init; }
    public ValueField? X { get; init; }
    public ValueField? Y { get; init; }
    public ValueField? Width { get; init; }
    public ValueField? Height { get; init; }
    /// <summary>Tip Pressure (0x0D/0x30): optional in PTP; many pads (the Synaptics one here) do not report it.</summary>
    public ValueField? Pressure { get; init; }
    public BoolField? Tip { get; init; }
    public BoolField? Confidence { get; init; }
}

/// <summary>One finger collection read from one report. Pressure is null when the pad has no Tip Pressure usage.</summary>
internal readonly record struct ContactReading(ushort Collection, int ContactId, bool Tip, bool Confidence,
    int X, int Y, int? W, int? H, bool HasData, int? Pressure = null);

/// <summary>Everything ChinPad reads from one HID input report.</summary>
internal readonly record struct ContactReport(byte ReportId, int? ContactCount, int? ScanTime, uint Buttons,
    List<ContactReading> Contacts);

/// <summary>
/// One Precision Touchpad TLC (usage page 0x0D, usage 0x05) found through raw input.
/// Holds the preparsed data for its whole lifetime (HidP_* need it for every report), discovers the
/// finger collections and frame-level usages from the caps, produces the caps dump and parses reports.
/// Nothing here depends on vendor or product IDs.
/// </summary>
internal sealed class TouchpadDevice : IDisposable
{
    // HID usage pages and usages (HID Usage Tables 1.x).
    public const ushort PageGenericDesktop = 0x01, PageButton = 0x09, PageDigitizer = 0x0D;
    public const ushort UsageX = 0x30, UsageY = 0x31;
    public const ushort UsageTouchPad = 0x05, UsageTipPressure = 0x30, UsageTipSwitch = 0x42, UsageConfidence = 0x47, UsageWidth = 0x48,
        UsageHeight = 0x49, UsageContactId = 0x51, UsageContactCount = 0x54, UsageScanTime = 0x56;

    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    IntPtr _preparsed;
    readonly ushort[] _usageBuffer;

    /// <summary>Number in the log and the caps dump (#0, #1, ...): the enumeration order; after a hot-plug TouchpadSet renumbers new pads.</summary>
    public int Index { get; set; }
    public RawDeviceEntry Entry { get; }
    public IntPtr Handle => Entry.Handle;
    public string Path => Entry.Path;
    public string ShortName => Entry.ShortName;
    public uint PreparsedSize { get; }
    public HIDP_CAPS Caps { get; }
    public HIDP_LINK_COLLECTION_NODE[] Nodes { get; } = [];
    public HIDP_BUTTON_CAPS[] ButtonCaps { get; } = [];
    public HIDP_VALUE_CAPS[] ValueCaps { get; } = [];
    public IReadOnlyList<RawDeviceEntry> Siblings { get; }

    public IReadOnlyList<FingerCollection> Fingers { get; private set; } = [];
    /// <summary>True when no Contact ID (0x51) exists and finger collections were found via Tip Switch instead.</summary>
    public bool FingersByTipFallback { get; private set; }
    public ValueField? ContactCount { get; private set; }
    public ValueField? ScanTime { get; private set; }
    /// <summary>Link collections that hold page-0x09 buttons (the physical click button of a clickpad).</summary>
    public IReadOnlyList<ushort> ButtonCollections { get; private set; } = [];
    public IReadOnlyList<ushort> ButtonUsages { get; private set; } = [];
    public IReadOnlyList<byte> ReportIds { get; private set; } = [];

    /// <summary>HidP status codes other than success / not-in-this-report seen while parsing (diagnostic only).</summary>
    public long UnexpectedStatusCount { get; private set; }
    public uint LastUnexpectedStatus { get; private set; }
    /// <summary>Reports whose length differed from InputReportByteLength (they are padded or cut to fit).</summary>
    public long LengthMismatchCount { get; private set; }

    TouchpadDevice(int index, RawDeviceEntry entry, IReadOnlyList<RawDeviceEntry> all)
    {
        Index = index;
        Entry = entry;
        Siblings = all.Where(o => o.Handle != entry.Handle &&
                                  string.Equals(o.PhysicalKey, entry.PhysicalKey, StringComparison.OrdinalIgnoreCase))
                      .ToList();

        _preparsed = ReadPreparsedData(entry.Handle, out uint size);
        PreparsedSize = size;
        try
        {
            Check(HidP_GetCaps(_preparsed, out HIDP_CAPS caps), "HidP_GetCaps");
            Caps = caps;

            uint nodeCount = caps.NumberLinkCollectionNodes;
            var nodes = new HIDP_LINK_COLLECTION_NODE[nodeCount];
            if (nodeCount > 0)
                Check(HidP_GetLinkCollectionNodes(nodes, ref nodeCount, _preparsed), "HidP_GetLinkCollectionNodes");
            Nodes = nodes[..(int)Math.Min(nodeCount, (uint)nodes.Length)];

            ushort buttonCount = caps.NumberInputButtonCaps;
            var buttons = new HIDP_BUTTON_CAPS[buttonCount];
            if (buttonCount > 0)
                Check(HidP_GetButtonCaps(HidP_Input, buttons, ref buttonCount, _preparsed), "HidP_GetButtonCaps");
            ButtonCaps = buttons[..Math.Min(buttonCount, buttons.Length)];

            ushort valueCount = caps.NumberInputValueCaps;
            var values = new HIDP_VALUE_CAPS[valueCount];
            if (valueCount > 0)
                Check(HidP_GetValueCaps(HidP_Input, values, ref valueCount, _preparsed), "HidP_GetValueCaps");
            ValueCaps = values[..Math.Min(valueCount, values.Length)];

            // Usage page 0 = all pages: the most "on" buttons any single report can have.
            _usageBuffer = new ushort[Math.Max(64, (int)HidP_MaxUsageListLength(HidP_Input, 0, _preparsed))];
            Analyze();
        }
        catch
        {
            Marshal.FreeHGlobal(_preparsed);
            _preparsed = IntPtr.Zero;
            throw;
        }
    }

    /// <summary>All raw-input HID TLCs with usage page 0x0D / usage 0x05. Devices that fail to initialise are listed in errors.</summary>
    public static List<TouchpadDevice> EnumerateAll(out List<string> errors)
    {
        errors = [];
        List<RawDeviceEntry> all = RawDeviceEntry.ListAll();
        var result = new List<TouchpadDevice>();
        foreach (RawDeviceEntry e in all)
        {
            if (e.Type != RIM_TYPEHID || e.UsagePage != PageDigitizer || e.Usage != UsageTouchPad) continue;
            try
            {
                result.Add(new TouchpadDevice(result.Count, e, all));
            }
            catch (Exception ex)
            {
                errors.Add($"{e.Path}: {ex.Message}");
            }
        }
        return result;
    }

    /// <summary>The touchpads the settings "device" substring selects: all when empty, else those whose path contains it.</summary>
    public static List<TouchpadDevice> Select(IReadOnlyList<TouchpadDevice> devices, string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter)) return devices.ToList();
        return devices.Where(d => d.Path.Contains(filter.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
    }

    /// <summary>
    /// The preparsed data is an opaque blob that every HidP_* call needs. It is copied into unmanaged
    /// memory once and kept until Dispose (HidP functions take a pointer to it).
    /// </summary>
    static IntPtr ReadPreparsedData(IntPtr hDevice, out uint size)
    {
        size = 0;
        if (GetRawInputDeviceInfo(hDevice, RIDI_PREPARSEDDATA, IntPtr.Zero, ref size) != 0 || size == 0)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "GetRawInputDeviceInfo(RIDI_PREPARSEDDATA) returned no size");
        IntPtr p = Marshal.AllocHGlobal((int)size);
        if (GetRawInputDeviceInfo(hDevice, RIDI_PREPARSEDDATA, p, ref size) == uint.MaxValue)
        {
            int err = Marshal.GetLastWin32Error();
            Marshal.FreeHGlobal(p);
            throw new Win32Exception(err, "GetRawInputDeviceInfo(RIDI_PREPARSEDDATA) failed");
        }
        return p;
    }

    static void Check(uint status, string what)
    {
        if (status != HIDP_STATUS_SUCCESS)
            throw new InvalidOperationException($"{what} failed with status 0x{status:X8}");
    }

    /// <summary>
    /// Works out the device's structure from its caps:
    ///  - finger collections = link collections with an input value cap 0x0D/0x51 (Contact ID);
    ///    fallback for pads without Contact ID: collections with a Tip Switch (0x0D/0x42), IDs by order;
    ///  - per finger collection: X, Y, Width, Height, Tip Pressure, Tip, Confidence;
    ///  - frame-level usages (Contact Count, Scan Time, page-0x09 buttons) wherever their caps say they are;
    ///  - the set of input report IDs.
    /// </summary>
    void Analyze()
    {
        var ids = new SortedSet<byte>();
        foreach (HIDP_BUTTON_CAPS b in ButtonCaps) ids.Add(b.ReportID);
        foreach (HIDP_VALUE_CAPS v in ValueCaps) ids.Add(v.ReportID);
        ReportIds = ids.ToList();

        List<ushort> fingerCollections = ValueCaps
            .Where(v => v.UsagePage == PageDigitizer && v.Covers(UsageContactId))
            .Select(v => v.LinkCollection).Distinct().OrderBy(c => c).ToList();
        if (fingerCollections.Count == 0)
        {
            FingersByTipFallback = true;
            fingerCollections = ButtonCaps.Where(b => b.UsagePage == PageDigitizer && b.Covers(UsageTipSwitch)).Select(b => b.LinkCollection)
                .Concat(ValueCaps.Where(v => v.UsagePage == PageDigitizer && v.Covers(UsageTipSwitch)).Select(v => v.LinkCollection))
                .Distinct().OrderBy(c => c).ToList();
        }

        var fingers = new List<FingerCollection>();
        foreach (ushort coll in fingerCollections)
        {
            var reportIds = new HashSet<byte>(
                ButtonCaps.Where(b => b.LinkCollection == coll).Select(b => b.ReportID)
                    .Concat(ValueCaps.Where(v => v.LinkCollection == coll).Select(v => v.ReportID)));
            fingers.Add(new FingerCollection
            {
                Collection = coll,
                Order = fingers.Count,
                ReportIds = reportIds,
                ContactId = FindValue(PageDigitizer, UsageContactId, coll),
                X = FindValue(PageGenericDesktop, UsageX, coll),
                Y = FindValue(PageGenericDesktop, UsageY, coll),
                Width = FindValue(PageDigitizer, UsageWidth, coll),
                Height = FindValue(PageDigitizer, UsageHeight, coll),
                Pressure = FindValue(PageDigitizer, UsageTipPressure, coll),
                Tip = FindBool(UsageTipSwitch, coll),
                Confidence = FindBool(UsageConfidence, coll),
            });
        }
        Fingers = fingers;

        ContactCount = FindValue(PageDigitizer, UsageContactCount, null);
        ScanTime = FindValue(PageDigitizer, UsageScanTime, null);

        var pageButtons = ButtonCaps.Where(b => b.UsagePage == PageButton).ToList();
        ButtonCollections = pageButtons.Select(b => b.LinkCollection).Distinct().OrderBy(c => c).ToList();
        ButtonUsages = pageButtons.SelectMany(b => b.Usages()).Distinct().OrderBy(u => u).ToList();
    }

    ValueField? FindValue(ushort page, ushort usage, ushort? collection)
    {
        foreach (HIDP_VALUE_CAPS v in ValueCaps)
            if (v.UsagePage == page && v.Covers(usage) && (collection is null || v.LinkCollection == collection))
                return ValueField.From(v, usage);
        return null;
    }

    BoolField? FindBool(ushort usage, ushort collection)
    {
        foreach (HIDP_BUTTON_CAPS b in ButtonCaps)
            if (b.UsagePage == PageDigitizer && b.Covers(usage) && b.LinkCollection == collection)
                return new BoolField(usage, collection, b.ReportID, true, null);
        ValueField? asValue = FindValue(PageDigitizer, usage, collection);
        return asValue is null ? null : new BoolField(usage, collection, asValue.ReportId, false, asValue);
    }

    // ------------------------------------------------------------------------------------------------
    // Report parsing
    // ------------------------------------------------------------------------------------------------

    /// <summary>
    /// Parses one input report. The buffer must start with the report-ID byte exactly as RAWHID delivers
    /// it: the HidP_* functions use that byte to pick the right field layout, and they answer
    /// HIDP_STATUS_INCOMPATIBLE_REPORT_ID for usages that live in a different report. That is how
    /// multi-report-ID devices are handled without hardcoding anything, so the byte is never stripped.
    /// </summary>
    public ContactReport ParseReport(byte[] report)
    {
        if (report.Length != Caps.InputReportByteLength && Caps.InputReportByteLength > 0)
        {
            // HidP_* insist on ReportLength == InputReportByteLength; pad or cut (should not happen with raw input).
            LengthMismatchCount++;
            var fixedLength = new byte[Caps.InputReportByteLength];
            Buffer.BlockCopy(report, 0, fixedLength, 0, Math.Min(report.Length, fixedLength.Length));
            report = fixedLength;
        }

        byte reportId = report.Length > 0 ? report[0] : (byte)0;
        int? count = GetValue(ContactCount, report);
        int? scan = GetValue(ScanTime, report);

        uint buttons = 0;
        foreach (ushort coll in ButtonCollections)
        {
            int n = GetOnButtons(PageButton, coll, report);
            for (int i = 0; i < n; i++)
            {
                ushort u = _usageBuffer[i];
                if (u is >= 1 and <= 32) buttons |= 1u << (u - 1);
            }
        }

        var contacts = new List<ContactReading>(Fingers.Count);
        foreach (FingerCollection f in Fingers)
        {
            if (!f.ReportIds.Contains(reportId)) continue; // this finger slot is not part of this report ID

            // One HidP_GetUsages call returns every digitizer button that is ON in this collection.
            bool tipOn = false, confidenceOn = false;
            int n = GetOnButtons(PageDigitizer, f.Collection, report);
            for (int i = 0; i < n; i++)
            {
                if (_usageBuffer[i] == UsageTipSwitch) tipOn = true;
                else if (_usageBuffer[i] == UsageConfidence) confidenceOn = true;
            }
            bool tip = f.Tip switch
            {
                null => false,
                { IsButton: true } => tipOn,
                { Value: var v } => (GetValue(v, report) ?? 0) != 0,
            };
            bool confidence = f.Confidence switch
            {
                null => false,
                { IsButton: true } => confidenceOn,
                { Value: var v } => (GetValue(v, report) ?? 0) != 0,
            };

            int rawId = GetValue(f.ContactId, report) ?? 0;
            int contactId = f.ContactId is null ? f.Order : rawId; // fallback: IDs by collection order
            int x = GetValue(f.X, report) ?? 0;
            int y = GetValue(f.Y, report) ?? 0;
            int? w = GetValue(f.Width, report);
            int? h = GetValue(f.Height, report);
            int? pressure = GetValue(f.Pressure, report);
            bool hasData = tip || confidence || rawId != 0 || x != 0 || y != 0;
            contacts.Add(new ContactReading(f.Collection, contactId, tip, confidence, x, y, w, h, hasData, pressure));
        }
        return new ContactReport(reportId, count, scan, buttons, contacts);
    }

    /// <summary>Reads a value usage; null when the usage is absent from this report (or the device).</summary>
    int? GetValue(ValueField? f, byte[] report)
    {
        if (f is null) return null;
        uint status = HidP_GetUsageValue(HidP_Input, f.Page, f.Collection, f.Usage, out uint raw, _preparsed, report, (uint)report.Length);
        if (status == HIDP_STATUS_SUCCESS) return f.Decode(raw);
        NoteStatus(status);
        return null;
    }

    /// <summary>Fills _usageBuffer with the ON buttons of one page in one collection; returns how many.</summary>
    int GetOnButtons(ushort page, ushort collection, byte[] report)
    {
        uint length = (uint)_usageBuffer.Length;
        uint status = HidP_GetUsages(HidP_Input, page, collection, _usageBuffer, ref length, _preparsed, report, (uint)report.Length);
        if (status == HIDP_STATUS_SUCCESS) return (int)Math.Min(length, (uint)_usageBuffer.Length);
        NoteStatus(status);
        return 0;
    }

    void NoteStatus(uint status)
    {
        // "Not in this report" is normal for multi-report devices; anything else is counted for the live view.
        if (status is HIDP_STATUS_INCOMPATIBLE_REPORT_ID or HIDP_STATUS_USAGE_NOT_FOUND) return;
        UnexpectedStatusCount++;
        LastUnexpectedStatus = status;
    }

    // ------------------------------------------------------------------------------------------------
    // Caps dump
    // ------------------------------------------------------------------------------------------------

    /// <summary>Header plus the dump of every touchpad, as printed by --caps / --diag and saved to diag-caps.txt.</summary>
    public static string DumpAll(IReadOnlyList<TouchpadDevice> devices, IReadOnlyList<string> errors, AppSettings settings, string settingsNote)
    {
        var sb = new StringBuilder();
        string version = typeof(TouchpadDevice).Assembly.GetName().Version?.ToString(3) ?? "?";
        sb.AppendLine(string.Create(Inv, $"ChinPad {version} capabilities dump, {DateTime.Now:yyyy-MM-dd HH:mm:ss}"));
        sb.AppendLine(string.Create(Inv, $"OS {Environment.OSVersion.VersionString}, {(Environment.Is64BitProcess ? "64" : "32")}-bit process, .NET {Environment.Version}"));
        sb.AppendLine(NativeLayout.Describe());
        sb.AppendLine(string.Create(Inv, $"Touchpads found (raw-input HID, usage page 0x0D, usage 0x05): {devices.Count}"));
        foreach (string e in errors) sb.AppendLine("  could not open: " + e);

        List<TouchpadDevice> selected = Select(devices, settings.Device);
        string selection = selected.Count > 0
            ? "background mode uses " + string.Join(", ", selected.Select(d => $"#{d.Index} {d.ShortName}"))
            : devices.Count == 0 ? "no touchpad to use" : $"no touchpad path contains \"{settings.Device}\", so the background mode uses all of them";
        sb.AppendLine($"settings.json ({settingsNote}): \"device\" = \"{settings.Device}\"{(string.IsNullOrWhiteSpace(settings.Device) ? " (empty = all touchpads)" : "")} -> {selection}");
        sb.AppendLine("  (diag mode logs every touchpad regardless; a non-empty \"device\" limits the background mode to the touchpads whose path contains it)");
        foreach (TouchpadDevice d in devices)
        {
            sb.AppendLine();
            sb.Append(d.Dump());
        }
        return sb.ToString();
    }

    /// <summary>Full description of this touchpad: identity, sibling TLCs, collection tree, every input cap, summary.</summary>
    public string Dump()
    {
        var sb = new StringBuilder();
        void L(string s) => sb.AppendLine(s);
        var fingerSet = Fingers.Select(f => (int)f.Collection).ToHashSet();

        L(string.Create(Inv, $"=== Touchpad #{Index}: {ShortName} ==="));
        L($"  path          {Path}");
        L(string.Create(Inv, $"  handle        0x{Handle.ToInt64():X}"));
        L(string.Create(Inv, $"  VID/PID       {Entry.VendorId:X4}:{Entry.ProductId:X4}  version 0x{Entry.Version:X4}"));
        L($"  top level     {UsageText(Caps.UsagePage, Caps.Usage)}");
        L(string.Create(Inv, $"  report bytes  input {Caps.InputReportByteLength} (includes the report-ID byte), output {Caps.OutputReportByteLength}, feature {Caps.FeatureReportByteLength}"));
        L(string.Create(Inv, $"  caps          link collections {Caps.NumberLinkCollectionNodes}, input button caps {Caps.NumberInputButtonCaps}, input value caps {Caps.NumberInputValueCaps}, input data indices {Caps.NumberInputDataIndices}, preparsed data {PreparsedSize} bytes"));

        L("  other top-level collections of the same physical device:");
        if (Siblings.Count == 0) L("    (none found)");
        foreach (RawDeviceEntry s in Siblings)
            L($"    {s.TypeName,-8} {UsageText(s.UsagePage, s.Usage),-48} {s.ShortName}");

        L("  link collections (index: parent, type, usage):");
        for (int i = 0; i < Nodes.Length; i++)
        {
            HIDP_LINK_COLLECTION_NODE n = Nodes[i];
            L(string.Create(Inv, $"    [{i}] parent {n.Parent,2}  {CollectionTypeName(n.CollectionType),-12} {UsageText(n.LinkUsagePage, n.LinkUsage),-44} children {n.NumberOfChildren}{(n.IsAlias ? "  alias" : "")}{(fingerSet.Contains(i) ? "  <- finger" : "")}"));
        }

        L("  input caps per link collection (rid = report ID):");
        int maxCollection = Math.Max(Nodes.Length - 1,
            Math.Max(ButtonCaps.Select(b => (int)b.LinkCollection).DefaultIfEmpty(0).Max(),
                     ValueCaps.Select(v => (int)v.LinkCollection).DefaultIfEmpty(0).Max()));
        for (int c = 0; c <= maxCollection; c++)
        {
            var buttons = ButtonCaps.Where(b => b.LinkCollection == c).ToList();
            var values = ValueCaps.Where(v => v.LinkCollection == c).ToList();
            if (buttons.Count == 0 && values.Count == 0) continue;
            L(string.Create(Inv, $"    collection {c}{(fingerSet.Contains(c) ? " [finger]" : "")}:"));
            foreach (HIDP_BUTTON_CAPS b in buttons)
                L(string.Create(Inv, $"      button rid {b.ReportID,3}  {CapUsageText(b.UsagePage, b.IsRange, b.UsageMin, b.UsageMax, b.Usage)}{(b.IsAlias ? "  alias" : "")}"));
            foreach (HIDP_VALUE_CAPS v in values)
            {
                double? mm = HidUnits.SpanMm(v.PhysicalMin, v.PhysicalMax, v.Units, v.UnitsExp);
                L(string.Create(Inv,
                    $"      value  rid {v.ReportID,3}  {CapUsageText(v.UsagePage, v.IsRange, v.UsageMin, v.UsageMax, v.Usage),-40} bits {v.BitSize} x{v.ReportCount}  logical {v.LogicalMin}..{v.LogicalMax}  physical {v.PhysicalMin}..{v.PhysicalMax}  unitExp 0x{v.UnitsExp:X} ({HidUnits.Exponent(v.UnitsExp)})  units {HidUnits.Name(v.Units)}{(mm is double m ? "  = " + m.ToString("0.0", Inv) + " mm" : "")}{(v.IsAbsolute ? "" : "  relative")}{(v.IsAlias ? "  alias" : "")}"));
            }
        }

        L($"  input report IDs: {(ReportIds.Count == 0 ? "none" : string.Join(",", ReportIds))}");
        L($"  finger collections: {FingerList()} " +
          (FingersByTipFallback
              ? "(no Contact ID 0x51 anywhere: collections with Tip Switch 0x42, contact IDs by collection order)"
              : "(collections with Contact ID 0x51)"));
        L($"  frame level: Contact Count {LocationText(ContactCount)}; Scan Time {LocationText(ScanTime)}; buttons page 0x09: " +
          (ButtonUsages.Count == 0 ? "none" : $"usages {string.Join(",", ButtonUsages)} in collection(s) {string.Join(",", ButtonCollections)}"));
        FingerCollection? f0 = Fingers.FirstOrDefault();
        if (f0 is not null)
        {
            L(string.Create(Inv, $"  finger fields (collection {f0.Collection}; the others normally match):"));
            L($"    X           {FieldText(f0.X)}");
            L($"    Y           {FieldText(f0.Y)}");
            L($"    Width       {FieldText(f0.Width)}");
            L($"    Height      {FieldText(f0.Height)}");
            L($"    Pressure    {FieldText(f0.Pressure)}");
            L($"    Contact ID  {FieldText(f0.ContactId)}");
            L($"    Tip         {BoolText(f0.Tip)}");
            L($"    Confidence  {BoolText(f0.Confidence)}");
        }
        L("  " + SummaryLine());
        return sb.ToString();
    }

    /// <summary>The one-line yes/no summary that ends each device dump.</summary>
    public string SummaryLine()
    {
        static string YN(bool b) => b ? "yes" : "no";
        bool Has(Func<FingerCollection, object?> pick) => Fingers.Any(f => pick(f) is not null);
        return $"has: ContactID {YN(!FingersByTipFallback && Has(f => f.ContactId))}, Tip {YN(Has(f => f.Tip))}, " +
               $"Confidence {YN(Has(f => f.Confidence))}, X {YN(Has(f => f.X))}, Y {YN(Has(f => f.Y))}, " +
               $"Width {YN(Has(f => f.Width))}, Height {YN(Has(f => f.Height))}, Pressure {YN(Has(f => f.Pressure))}, ContactCount {YN(ContactCount is not null)}, " +
               $"ScanTime {YN(ScanTime is not null)}, Buttons {YN(ButtonUsages.Count > 0)}; " +
               $"finger collections: {FingerList()}; report IDs: {(ReportIds.Count == 0 ? "none" : string.Join(",", ReportIds))}";
    }

    string FingerList() => Fingers.Count == 0 ? "none" : string.Join(",", Fingers.Select(f => f.Collection));

    static string LocationText(ValueField? f) => f is null
        ? "absent"
        : string.Create(Inv, $"in collection {f.Collection} rid {f.ReportId} (logical {f.LogicalMin}..{f.LogicalMax}, {f.BitSize} bits)");

    static string FieldText(ValueField? f)
    {
        if (f is null) return "absent";
        var s = new StringBuilder(string.Create(Inv, $"rid {f.ReportId}, {f.BitSize} bits, logical {f.LogicalMin}..{f.LogicalMax}"));
        s.Append(string.Create(Inv, $", physical {f.PhysicalMin}..{f.PhysicalMax}, unitExp {HidUnits.Exponent(f.UnitsExp)}, units {HidUnits.Name(f.Units)}"));
        if (f.PhysicalSpanMm is double mm) s.Append(string.Create(Inv, $" -> {mm:0.0} mm"));
        return s.ToString();
    }

    static string BoolText(BoolField? b) => b is null
        ? "absent"
        : string.Create(Inv, $"rid {b.ReportId}, {(b.IsButton ? "button" : "1-bit value")}");

    static string CollectionTypeName(byte type) => type switch
    {
        0 => "Physical",
        1 => "Application",
        2 => "Logical",
        3 => "Report",
        4 => "NamedArray",
        5 => "UsageSwitch",
        6 => "UsageModifier",
        _ => "type 0x" + type.ToString("X2", Inv),
    };

    static string CapUsageText(ushort page, bool isRange, ushort min, ushort max, ushort usage) => isRange
        ? string.Create(Inv, $"0x{page:X4}/0x{min:X4}..0x{max:X4} {HidUsageNames.Page(page)} range")
        : UsageText(page, usage);

    public static string UsageText(ushort page, ushort usage)
    {
        string name = HidUsageNames.Usage(page, usage);
        return string.Create(Inv, $"0x{page:X4}/0x{usage:X4}{(name.Length > 0 ? " " + name : "")}");
    }

    public void Dispose()
    {
        if (_preparsed != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_preparsed);
            _preparsed = IntPtr.Zero;
        }
    }
}

/// <summary>HID unit decoding (HID 1.11 section 6.2.2.7). Enough for touchpads: length and time.</summary>
internal static class HidUnits
{
    /// <summary>The unit exponent is a 4-bit two's-complement nibble: 0x0..0x7 = 0..7, 0x8..0xF = -8..-1 (0xE = -2).</summary>
    public static int Exponent(uint unitsExp)
    {
        int n = (int)(unitsExp & 0xF);
        return n >= 8 ? n - 16 : n;
    }

    /// <summary>
    /// The unit code is nibble-packed: nibble 0 = system (1 SI linear, 3 English linear, ...), nibble 1 =
    /// length exponent, nibble 3 = time exponent. 0x11 = centimetre, 0x13 = inch, 0x1001 = second.
    /// </summary>
    public static string Name(uint units) => units switch
    {
        0 => "none",
        0x11 => "cm",
        0x13 => "inch",
        0x1001 or 0x1003 => "s",
        _ => "0x" + units.ToString("X", CultureInfo.InvariantCulture),
    };

    public static double? SpanMm(int physicalMin, int physicalMax, uint units, uint unitsExp)
    {
        double? mmPerUnit = units switch { 0x11 => 10.0, 0x13 => 25.4, _ => null };
        if (mmPerUnit is null || physicalMax <= physicalMin) return null;
        return (physicalMax - physicalMin) * Math.Pow(10, Exponent(unitsExp)) * mmPerUnit.Value;
    }
}

/// <summary>Names for the usages that matter here (HID Usage Tables). Unknown ones print as hex only.</summary>
internal static class HidUsageNames
{
    public static string Page(ushort page) => page switch
    {
        0x01 => "Generic Desktop",
        0x07 => "Keyboard",
        0x08 => "LED",
        0x09 => "Button",
        0x0C => "Consumer",
        0x0D => "Digitizer",
        >= 0xFF00 => "Vendor",
        _ => "",
    };

    public static string Usage(ushort page, ushort usage) => (page, usage) switch
    {
        (0x01, 0x01) => "Pointer",
        (0x01, 0x02) => "Mouse",
        (0x01, 0x06) => "Keyboard",
        (0x01, 0x30) => "X",
        (0x01, 0x31) => "Y",
        (0x01, 0x38) => "Wheel",
        (0x01, 0x80) => "System Control",
        (0x09, _) => "Button " + usage.ToString(CultureInfo.InvariantCulture),
        (0x0C, 0x01) => "Consumer Control",
        (0x0D, 0x01) => "Digitizer",
        (0x0D, 0x02) => "Pen",
        (0x0D, 0x04) => "Touch Screen",
        (0x0D, 0x05) => "Touch Pad",
        (0x0D, 0x0E) => "Device Configuration",
        (0x0D, 0x20) => "Stylus",
        (0x0D, 0x22) => "Finger",
        (0x0D, 0x23) => "Device Settings",
        (0x0D, 0x30) => "Tip Pressure",
        (0x0D, 0x32) => "In Range",
        (0x0D, 0x42) => "Tip Switch",
        (0x0D, 0x47) => "Confidence",
        (0x0D, 0x48) => "Width",
        (0x0D, 0x49) => "Height",
        (0x0D, 0x51) => "Contact ID",
        (0x0D, 0x52) => "Device Mode",
        (0x0D, 0x53) => "Device Identifier",
        (0x0D, 0x54) => "Contact Count",
        (0x0D, 0x55) => "Contact Count Maximum",
        (0x0D, 0x56) => "Scan Time",
        (0x0D, 0x57) => "Surface Switch",
        (0x0D, 0x58) => "Button Switch",
        (0x0D, 0x59) => "Pad Type",
        ( >= 0xFF00, _) => "(vendor)",
        _ => "",
    };
}
