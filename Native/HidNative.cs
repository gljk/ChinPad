using System.Runtime.InteropServices;

namespace ChinPad.Native;

/// <summary>
/// hid.dll HID parser ("HidP_*").
/// A HID device describes its reports in a report descriptor; Windows compiles that into "preparsed
/// data". With it the HidP_* functions can pull any control (X, Contact ID, Tip Switch, ...) out of a
/// raw report without us knowing where its bits sit, which keeps ChinPad free of device-specific offsets.
///
/// Vocabulary:
///  - usage page + usage name a control: 0x01/0x30 = Generic Desktop X, 0x0D/0x42 = Digitizer Tip Switch.
///  - a link collection is one HID collection inside the top-level collection. A Precision Touchpad has
///    one "Finger" collection per contact slot, each holding Tip, Confidence, Contact ID, X, Y, ...
///    Index 0 is the top-level collection itself.
///  - button caps describe 1-bit on/off controls (read with HidP_GetUsages), value caps describe
///    numbers (read with HidP_GetUsageValue).
///  - a cap can cover a range of usages (IsRange) or a single usage; ExpandUsages handles both.
/// </summary>
internal static class HidNative
{
    // HIDP_REPORT_TYPE
    public const int HidP_Input = 0;

    // NTSTATUS codes from hidpi.h (facility 0x11). Declared as uint so they compare without casts.
    public const uint HIDP_STATUS_SUCCESS = 0x00110000;
    public const uint HIDP_STATUS_INVALID_PREPARSED_DATA = 0xC0110001;
    public const uint HIDP_STATUS_INVALID_REPORT_TYPE = 0xC0110002;
    public const uint HIDP_STATUS_INVALID_REPORT_LENGTH = 0xC0110003;
    public const uint HIDP_STATUS_USAGE_NOT_FOUND = 0xC0110004;       // usage not in this collection: "not here"
    public const uint HIDP_STATUS_BUFFER_TOO_SMALL = 0xC0110007;
    public const uint HIDP_STATUS_INCOMPATIBLE_REPORT_ID = 0xC011000A; // usage exists, but in another report ID: "not in this report"

    /// <summary>
    /// HIDP_CAPS, 64 bytes. The USHORT Reserved[17] block (offsets 10..43) is left as a gap of the explicit
    /// layout instead of a managed array, so the struct stays blittable.
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 64)]
    public struct HIDP_CAPS
    {
        [FieldOffset(0)] public ushort Usage;
        [FieldOffset(2)] public ushort UsagePage;
        [FieldOffset(4)] public ushort InputReportByteLength;   // includes the report-ID byte
        [FieldOffset(6)] public ushort OutputReportByteLength;
        [FieldOffset(8)] public ushort FeatureReportByteLength;
        // [FieldOffset(10)] USHORT Reserved[17]
        [FieldOffset(44)] public ushort NumberLinkCollectionNodes;
        [FieldOffset(46)] public ushort NumberInputButtonCaps;
        [FieldOffset(48)] public ushort NumberInputValueCaps;
        [FieldOffset(50)] public ushort NumberInputDataIndices;
        [FieldOffset(52)] public ushort NumberOutputButtonCaps;
        [FieldOffset(54)] public ushort NumberOutputValueCaps;
        [FieldOffset(56)] public ushort NumberOutputDataIndices;
        [FieldOffset(58)] public ushort NumberFeatureButtonCaps;
        [FieldOffset(60)] public ushort NumberFeatureValueCaps;
        [FieldOffset(62)] public ushort NumberFeatureDataIndices;
    }

    /// <summary>
    /// HIDP_LINK_COLLECTION_NODE, 24 bytes on x64. Nodes form a tree: Parent / FirstChild / NextSibling
    /// are indices into the node array. The ULONG bit field holds CollectionType (bits 0..7: 0 Physical,
    /// 1 Application, 2 Logical, ...) and IsAlias (bit 8).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct HIDP_LINK_COLLECTION_NODE
    {
        public ushort LinkUsage;
        public ushort LinkUsagePage;
        public ushort Parent;
        public ushort NumberOfChildren;
        public ushort NextSibling;
        public ushort FirstChild;
        public uint Bits;
        public IntPtr UserContext;

        public readonly byte CollectionType => (byte)(Bits & 0xFF);
        public readonly bool IsAlias => ((Bits >> 8) & 1) != 0;
    }

    /// <summary>
    /// HIDP_BUTTON_CAPS, 72 bytes. BOOLEAN fields are single bytes; they are declared as byte with bool
    /// accessors so the struct is blittable. Bytes 16..55 are ReportCount (newer SDKs) and reserved space.
    /// The Range / NotRange union starts at offset 56; the fields of both variants are declared flat.
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 72)]
    public struct HIDP_BUTTON_CAPS
    {
        [FieldOffset(0)] public ushort UsagePage;
        [FieldOffset(2)] public byte ReportID;
        [FieldOffset(3)] public byte isAlias;
        [FieldOffset(4)] public ushort BitField;
        [FieldOffset(6)] public ushort LinkCollection;   // index into the link collection node array
        [FieldOffset(8)] public ushort LinkUsage;
        [FieldOffset(10)] public ushort LinkUsagePage;
        [FieldOffset(12)] public byte isRange;
        [FieldOffset(13)] public byte isStringRange;
        [FieldOffset(14)] public byte isDesignatorRange;
        [FieldOffset(15)] public byte isAbsolute;
        [FieldOffset(16)] public ushort ReportCount;
        // Range variant
        [FieldOffset(56)] public ushort UsageMin;
        [FieldOffset(58)] public ushort UsageMax;
        [FieldOffset(60)] public ushort StringMin;
        [FieldOffset(62)] public ushort StringMax;
        [FieldOffset(64)] public ushort DesignatorMin;
        [FieldOffset(66)] public ushort DesignatorMax;
        [FieldOffset(68)] public ushort DataIndexMin;
        [FieldOffset(70)] public ushort DataIndexMax;
        // NotRange variant (same bytes)
        [FieldOffset(56)] public ushort Usage;
        [FieldOffset(60)] public ushort StringIndex;
        [FieldOffset(64)] public ushort DesignatorIndex;
        [FieldOffset(68)] public ushort DataIndex;

        public readonly bool IsAlias => isAlias != 0;
        public readonly bool IsRange => isRange != 0;
        public readonly bool IsAbsolute => isAbsolute != 0;
        public readonly bool Covers(ushort usage) => IsRange ? usage >= UsageMin && usage <= UsageMax : usage == Usage;
        public readonly IEnumerable<ushort> Usages(int limit = 256) => ExpandUsages(IsRange, UsageMin, UsageMax, Usage, limit);
    }

    /// <summary>
    /// HIDP_VALUE_CAPS, 72 bytes, same header as HIDP_BUTTON_CAPS plus the value description:
    /// BitSize / ReportCount, the logical range (what the report carries), the physical range with its
    /// unit (what it means, e.g. centimetres) and the 4-bit unit exponent. Union at offset 56 as above.
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 72)]
    public struct HIDP_VALUE_CAPS
    {
        [FieldOffset(0)] public ushort UsagePage;
        [FieldOffset(2)] public byte ReportID;
        [FieldOffset(3)] public byte isAlias;
        [FieldOffset(4)] public ushort BitField;
        [FieldOffset(6)] public ushort LinkCollection;
        [FieldOffset(8)] public ushort LinkUsage;
        [FieldOffset(10)] public ushort LinkUsagePage;
        [FieldOffset(12)] public byte isRange;
        [FieldOffset(13)] public byte isStringRange;
        [FieldOffset(14)] public byte isDesignatorRange;
        [FieldOffset(15)] public byte isAbsolute;
        [FieldOffset(16)] public byte hasNull;
        [FieldOffset(17)] public byte Reserved;
        [FieldOffset(18)] public ushort BitSize;
        [FieldOffset(20)] public ushort ReportCount;
        // [FieldOffset(22)] USHORT Reserved2[5]
        [FieldOffset(32)] public uint UnitsExp;
        [FieldOffset(36)] public uint Units;
        [FieldOffset(40)] public int LogicalMin;
        [FieldOffset(44)] public int LogicalMax;
        [FieldOffset(48)] public int PhysicalMin;
        [FieldOffset(52)] public int PhysicalMax;
        // Range variant
        [FieldOffset(56)] public ushort UsageMin;
        [FieldOffset(58)] public ushort UsageMax;
        [FieldOffset(60)] public ushort StringMin;
        [FieldOffset(62)] public ushort StringMax;
        [FieldOffset(64)] public ushort DesignatorMin;
        [FieldOffset(66)] public ushort DesignatorMax;
        [FieldOffset(68)] public ushort DataIndexMin;
        [FieldOffset(70)] public ushort DataIndexMax;
        // NotRange variant (same bytes)
        [FieldOffset(56)] public ushort Usage;
        [FieldOffset(60)] public ushort StringIndex;
        [FieldOffset(64)] public ushort DesignatorIndex;
        [FieldOffset(68)] public ushort DataIndex;

        public readonly bool IsAlias => isAlias != 0;
        public readonly bool IsRange => isRange != 0;
        public readonly bool IsAbsolute => isAbsolute != 0;
        public readonly bool HasNull => hasNull != 0;
        public readonly bool Covers(ushort usage) => IsRange ? usage >= UsageMin && usage <= UsageMax : usage == Usage;
        public readonly IEnumerable<ushort> Usages(int limit = 256) => ExpandUsages(IsRange, UsageMin, UsageMax, Usage, limit);
    }

    /// <summary>
    /// Turns a cap into its individual usages: a range cap (IsRange) covers UsageMin..UsageMax, otherwise
    /// the cap names one usage. The limit guards against vendor caps that declare huge ranges.
    /// </summary>
    public static IEnumerable<ushort> ExpandUsages(bool isRange, ushort usageMin, ushort usageMax, ushort usage, int limit = 256)
    {
        if (!isRange)
        {
            yield return usage;
            yield break;
        }
        int n = 0;
        for (int u = usageMin; u <= usageMax && n < limit; u++, n++)
            yield return (ushort)u;
    }

    [DllImport("hid.dll")]
    public static extern uint HidP_GetCaps(IntPtr PreparsedData, out HIDP_CAPS Capabilities);

    [DllImport("hid.dll")]
    public static extern uint HidP_GetLinkCollectionNodes([Out] HIDP_LINK_COLLECTION_NODE[] LinkCollectionNodes,
        ref uint LinkCollectionNodesLength, IntPtr PreparsedData);

    [DllImport("hid.dll")]
    public static extern uint HidP_GetButtonCaps(int ReportType, [Out] HIDP_BUTTON_CAPS[] ButtonCaps,
        ref ushort ButtonCapsLength, IntPtr PreparsedData);

    [DllImport("hid.dll")]
    public static extern uint HidP_GetValueCaps(int ReportType, [Out] HIDP_VALUE_CAPS[] ValueCaps,
        ref ushort ValueCapsLength, IntPtr PreparsedData);

    /// <summary>Largest number of "on" buttons HidP_GetUsages can return for this page (0 = all pages).</summary>
    [DllImport("hid.dll")]
    public static extern uint HidP_MaxUsageListLength(int ReportType, ushort UsagePage, IntPtr PreparsedData);

    /// <summary>
    /// Lists the button usages of one page that are ON in this report, limited to one link collection
    /// (0 = the whole top-level collection). UsageLength: in = array size, out = entries filled.
    /// The report buffer must start with its report-ID byte.
    /// </summary>
    [DllImport("hid.dll")]
    public static extern uint HidP_GetUsages(int ReportType, ushort UsagePage, ushort LinkCollection,
        [In, Out] ushort[] UsageList, ref uint UsageLength, IntPtr PreparsedData, [In] byte[] Report, uint ReportLength);

    /// <summary>
    /// Reads one value usage from a report. The value is returned as raw bits (no sign extension).
    /// The report buffer must start with its report-ID byte.
    /// </summary>
    [DllImport("hid.dll")]
    public static extern uint HidP_GetUsageValue(int ReportType, ushort UsagePage, ushort LinkCollection, ushort Usage,
        out uint UsageValue, IntPtr PreparsedData, [In] byte[] Report, uint ReportLength);
}

/// <summary>Start-up self check: the interop structs must have exactly the native sizes.</summary>
internal static class NativeLayout
{
    public static string Describe()
    {
        bool x64 = IntPtr.Size == 8;
        var checks = new (string Name, int Actual, int Expected)[]
        {
            ("HIDP_CAPS", Marshal.SizeOf<HidNative.HIDP_CAPS>(), 64),
            ("HIDP_LINK_COLLECTION_NODE", Marshal.SizeOf<HidNative.HIDP_LINK_COLLECTION_NODE>(), x64 ? 24 : 20),
            ("HIDP_BUTTON_CAPS", Marshal.SizeOf<HidNative.HIDP_BUTTON_CAPS>(), 72),
            ("HIDP_VALUE_CAPS", Marshal.SizeOf<HidNative.HIDP_VALUE_CAPS>(), 72),
            ("RAWINPUTDEVICELIST", Marshal.SizeOf<RawInputNative.RAWINPUTDEVICELIST>(), x64 ? 16 : 8),
            ("RAWINPUTDEVICE", Marshal.SizeOf<RawInputNative.RAWINPUTDEVICE>(), x64 ? 16 : 12),
            ("RAWINPUTHEADER", Marshal.SizeOf<RawInputNative.RAWINPUTHEADER>(), x64 ? 24 : 16),
            ("RAWHID", Marshal.SizeOf<RawInputNative.RAWHID>(), 8),
            ("RID_DEVICE_INFO", Marshal.SizeOf<RawInputNative.RID_DEVICE_INFO>(), 32),
        };
        var bad = checks.Where(c => c.Actual != c.Expected).ToList();
        if (bad.Count == 0)
            return "struct sizes OK: " + string.Join(", ", checks.Select(c => $"{c.Name} {c.Actual}"));
        return "STRUCT SIZE MISMATCH (parsing will be wrong): " +
               string.Join(", ", bad.Select(c => $"{c.Name} is {c.Actual}, expected {c.Expected}"));
    }
}
