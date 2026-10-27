using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using static ChinPad.Native.InputNative;

namespace ChinPad;

/// <summary>Optional speed-dependent gain (settings key "acceleration").</summary>
internal sealed record AccelerationSettings
{
    public bool Enabled { get; init; } = false;
    /// <summary>gain = clamp((v / threshold)^(exponent - 1), 1, 4) while v &gt; threshold; 1 = no acceleration.</summary>
    public double Exponent { get; init; } = 1.5;
    /// <summary>Contact speed in mm/s above which the gain starts to rise.</summary>
    public double ThresholdMmPerS { get; init; } = 30;
}

/// <summary>
/// ClickLock (settings key "clickLock", 0.5.0): a zone button held down for at least HoldMs stays down after the
/// physical release, so the chin can drag and select text without pressing; the next press ends it.
/// </summary>
internal sealed record ClickLockSettings
{
    public bool Enabled { get; init; } = false;
    /// <summary>How long (ms) the pad has to be held pressed before the button locks; 300 - 3000.</summary>
    public double HoldMs { get; init; } = 600;
}

/// <summary>What pressing the pad does while a touch classified in the zone is down: that button's click, or nothing ("none" = dead area).</summary>
internal enum ZoneAction { Left, Right, Middle, None }

/// <summary>
/// One button zone (settings key "zones"): a rectangle in fractions of the pad, x from 0 (left edge) to 1
/// (right edge), y from 0 (top: the edge away from you) to 1 (bottom: the edge nearest you), both ends
/// included. In JSON: { "action": "left", "x": [0.0, 0.5], "y": [0.72, 1.0] }.
/// </summary>
internal sealed record ZoneSettings(ZoneAction Action, double XMin, double XMax, double YMin, double YMax)
{
    /// <summary>
    /// The default layout: the bottom 28 % of the pad (21.5 mm of 76.8 mm), left half = left click, right half =
    /// right click. On this laptop's Synaptics pad the chin landed 61-71 mm from the top edge when aiming for the
    /// strip, and touches meant to move the cursor landed at 41 mm or less.
    /// </summary>
    public static readonly IReadOnlyList<ZoneSettings> Defaults =
    [
        new(ZoneAction.Left, 0.0, 0.5, 0.72, 1.0),
        new(ZoneAction.Right, 0.5, 1.0, 0.72, 1.0),
    ];

    /// <summary>Set by the JSON reader when the entry cannot be read; Validated drops such a zone with this text as the note.</summary>
    public string? Problem { get; init; }

    public string ActionName => Action.ToString().ToLowerInvariant();

    public bool Contains(double fx, double fy) => fx >= XMin && fx <= XMax && fy >= YMin && fy <= YMax;

    /// <summary>The first zone that contains the point (where zones overlap, the first one listed wins); null = the move area.</summary>
    public static ZoneSettings? Find(IReadOnlyList<ZoneSettings> zones, double fx, double fy)
    {
        foreach (ZoneSettings z in zones)
            if (z.Contains(fx, fy)) return z;
        return null;
    }

    /// <summary>Why the ranges are unusable (each must be inside 0..1 with min &lt; max), or null when they are fine.</summary>
    public string? RangeProblem(int number)
    {
        static bool Ok(double min, double max) => min >= 0 && max <= 1 && min < max; // false for NaN too
        if (!Ok(XMin, XMax)) return string.Create(CultureInfo.InvariantCulture, $"zone {number} ({ActionName}): x [{XMin}, {XMax}] must be inside 0..1 with min < max");
        if (!Ok(YMin, YMax)) return string.Create(CultureInfo.InvariantCulture, $"zone {number} ({ActionName}): y [{YMin}, {YMax}] must be inside 0..1 with min < max");
        return null;
    }

    /// <summary>One line of JSON: { "action": "left", "x": [0.0, 0.5], "y": [0.72, 1.0] } (without spaces when compact).</summary>
    public string ToJson(bool compact)
    {
        string sep = compact ? "" : " ";
        return $"{{{sep}\"action\":{sep}\"{ActionName}\",{sep}\"x\":{sep}[{Num(XMin)},{sep}{Num(XMax)}],{sep}\"y\":{sep}[{Num(YMin)},{sep}{Num(YMax)}]{sep}}}";

        // Round-trip format, always with a decimal point so that 0 and 1 read as fractions (0.0, 1.0).
        static string Num(double v)
        {
            string s = v.ToString("R", CultureInfo.InvariantCulture);
            return s.Contains('.') || s.Contains('E') ? s : s + ".0";
        }
    }
}

/// <summary>
/// Reads and writes "zones". Reading never throws because of one zone's content: an entry that cannot be
/// read becomes a zone with Problem set, which Validated drops with a note, so one mistyped zone does not
/// make the whole file unreadable. A value that is not a list reads as null (Validated: default zones,
/// with a note). Writing puts each zone on one line, which keeps the file easy to edit by hand.
/// </summary>
internal sealed class ZoneListConverter : JsonConverter<IReadOnlyList<ZoneSettings>>
{
    public override IReadOnlyList<ZoneSettings>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using JsonDocument doc = JsonDocument.ParseValue(ref reader);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;
        var zones = new List<ZoneSettings>();
        foreach (JsonElement e in doc.RootElement.EnumerateArray()) zones.Add(ReadZone(e, zones.Count + 1));
        return zones;
    }

    public override void Write(Utf8JsonWriter writer, IReadOnlyList<ZoneSettings> zones, JsonSerializerOptions options)
    {
        if (!options.WriteIndented || zones.Count == 0)
        {
            writer.WriteRawValue("[" + string.Join(",", zones.Select(z => z.ToJson(compact: true))) + "]");
            return;
        }
        // The writer does not indent a raw value; indent it to match the rest of the file.
        string inner = options.NewLine + new string(options.IndentCharacter, options.IndentSize * (writer.CurrentDepth + 1));
        string outer = options.NewLine + new string(options.IndentCharacter, options.IndentSize * writer.CurrentDepth);
        writer.WriteRawValue("[" + inner + string.Join("," + inner, zones.Select(z => z.ToJson(compact: false))) + outer + "]");
    }

    /// <summary>One zone object; keys are case-insensitive, other keys are ignored (e.g. a "comment").</summary>
    static ZoneSettings ReadZone(JsonElement e, int number)
    {
        ZoneSettings Bad(string why) => new(ZoneAction.None, 0, 0, 0, 0) { Problem = $"zone {number}: {why}" };
        if (e.ValueKind != JsonValueKind.Object)
            return Bad("expected an object like { \"action\": \"left\", \"x\": [0.0, 0.5], \"y\": [0.72, 1.0] }");

        JsonElement? action = null, x = null, y = null;
        foreach (JsonProperty p in e.EnumerateObject())
        {
            switch (p.Name.ToLowerInvariant())
            {
                case "action": action = p.Value; break;
                case "x": x = p.Value; break;
                case "y": y = p.Value; break;
            }
        }

        string actionText = action is { ValueKind: JsonValueKind.String } a ? a.GetString()!.Trim().ToLowerInvariant() : "";
        ZoneAction? kind = actionText switch
        {
            "left" => ZoneAction.Left,
            "right" => ZoneAction.Right,
            "middle" => ZoneAction.Middle,
            "none" => ZoneAction.None,
            _ => null,
        };
        if (kind is null)
            return Bad($"\"action\" {(action is null ? "is missing" : action.Value.GetRawText())}; use \"left\", \"right\", \"middle\" or \"none\"");
        if (Pair(x) is not (double x0, double x1))
            return Bad($"\"x\" {(x is null ? "is missing" : x.Value.GetRawText())}; expected two numbers [min, max]");
        if (Pair(y) is not (double y0, double y1))
            return Bad($"\"y\" {(y is null ? "is missing" : y.Value.GetRawText())}; expected two numbers [min, max]");
        return new ZoneSettings(kind.Value, x0, x1, y0, y1);

        static (double, double)? Pair(JsonElement? v)
        {
            if (v is not { ValueKind: JsonValueKind.Array } arr || arr.GetArrayLength() != 2) return null;
            JsonElement p0 = arr[0], p1 = arr[1];
            if (p0.ValueKind != JsonValueKind.Number || p1.ValueKind != JsonValueKind.Number) return null;
            return (p0.GetDouble(), p1.GetDouble());
        }
    }
}

/// <summary>
/// Where settings.json and chinpad.log live; --diag writes its CSV and diag-caps.txt there too. Portable mode
/// when a settings.json exists next to the exe: everything stays in the exe's folder, as up to 0.3.1.
/// Otherwise %LOCALAPPDATA%\ChinPad (created when missing), so the exe may sit in Downloads or another folder
/// the user never looks into. If that folder cannot be created, the exe's folder is used (Problem says why).
/// </summary>
internal sealed record SettingsPaths(string Folder, bool Portable, string ExeDir, string? Problem = null)
{
    public const string SettingsFileName = "settings.json";
    public const string LogFileName = "chinpad.log";

    public string SettingsFile => Path.Combine(Folder, SettingsFileName);
    public string LogFile => Path.Combine(Folder, LogFileName);

    /// <param name="localAppData">The local application data root; null = the user's (tests pass a scratch folder).</param>
    /// <param name="create">Create the folder when it is missing (false: only work out where it would be).</param>
    public static SettingsPaths Resolve(string exeDir, string? localAppData = null, bool create = true)
    {
        if (File.Exists(Path.Combine(exeDir, SettingsFileName))) return new SettingsPaths(exeDir, true, exeDir);
        // Smoke tests supply an isolated data root; normal launches always use the Windows folder.
        string root = localAppData ?? Environment.GetEnvironmentVariable("CHINPAD_TEST_LOCALAPPDATA")
            ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrEmpty(root)) return new SettingsPaths(exeDir, false, exeDir, "the local application data folder is unknown");
        string folder = Path.Combine(root, "ChinPad");
        if (create)
        {
            try
            {
                Directory.CreateDirectory(folder);
            }
            catch (Exception ex)
            {
                return new SettingsPaths(exeDir, false, exeDir, $"cannot create {folder}: {ex.Message}");
            }
        }
        return new SettingsPaths(folder, false, exeDir);
    }

    /// <summary>One line for the log and system.txt.</summary>
    public string Describe() =>
        Problem is not null ? $"settings and log in {Folder}, next to ChinPad.exe ({Problem})"
        : Portable ? $"portable mode: settings.json found next to ChinPad.exe, so settings and log are in {Folder}"
        : $"settings and log in {Folder} (there is no settings.json next to ChinPad.exe; putting one there switches to portable mode)";
}

/// <summary>
/// The settings window's "Strip height" slider. It only works on the standard layout: exactly two zones, a
/// "left" one over x 0..0.5 and a "right" one over x 0.5..1, both with the same y range reaching the bottom
/// edge (y max 1.0). Any other layout is custom and stays a matter of settings.json.
/// </summary>
internal static class StripLayout
{
    /// <summary>True for the standard layout; top = the strip's upper edge (y min, a fraction of the pad height).</summary>
    public static bool IsStandard(IReadOnlyList<ZoneSettings> zones, out double top)
    {
        top = 0;
        if (zones.Count != 2) return false;
        ZoneSettings? left = zones.FirstOrDefault(z => z.Action == ZoneAction.Left);
        ZoneSettings? right = zones.FirstOrDefault(z => z.Action == ZoneAction.Right);
        if (left is null || right is null) return false;
        if (left.XMin != 0 || left.XMax != 0.5 || right.XMin != 0.5 || right.XMax != 1) return false;
        if (left.YMin != right.YMin || left.YMax != 1 || right.YMax != 1) return false;
        top = left.YMin;
        return true;
    }

    /// <summary>Strip height in mm for a top edge at <paramref name="top"/> (fraction) on a pad this many mm high.</summary>
    public static double HeightMm(double top, double padHeightMm) => (1 - top) * padHeightMm;

    /// <summary>The top edge (fraction, 4 decimals) of a strip this many mm high.</summary>
    public static double TopFor(double heightMm, double padHeightMm) =>
        padHeightMm > 0 ? Math.Round(Math.Clamp(1 - heightMm / padHeightMm, 0.01, 0.99), 4) : 0.72;

    /// <summary>The same zones (order and x ranges kept) with the strip's top edge moved.</summary>
    public static IReadOnlyList<ZoneSettings> WithTop(IReadOnlyList<ZoneSettings> zones, double top) =>
        zones.Select(z => z with { YMin = top }).ToList();
}

/// <summary>
/// settings.json (camelCase keys; where it lives: SettingsPaths). Missing keys take the default, unknown keys are ignored
/// (and logged). When the file lacks keys it is written back once with all keys, so every knob is visible.
/// Values are range-checked on load; out-of-range values are clamped with a note.
/// </summary>
internal sealed record AppSettings
{
    /// <summary>
    /// Case-insensitive substring of the raw-input device path, for example "SYNA2BA6". Empty = all touchpads
    /// (each gets its own frame assembler; on this laptop the chin is on the Synaptics pad). The diagnostic
    /// logs every touchpad regardless and only prints which ones this selects. The settings window's "Apply to"
    /// list writes the hardware-ID part of one pad's path (PadChoice.DeviceKey). When no touchpad found contains
    /// it (a pad that is unplugged), the background mode uses all touchpads until it is back (0.5.0).
    /// </summary>
    public string Device { get; init; } = "";
    /// <summary>
    /// 1 = every contact is ours (touchpad Off in Settings); 2 = palm-flagged contacts only ("let Windows handle
    /// fingers"); 3 = like 1 plus a mouse hook. Default 3 since 0.5.0 (the configuration in use on this laptop).
    /// </summary>
    public int Strategy { get; init; } = 3;
    /// <summary>Strategy 2: once a contact has reported confidence 0 it stays ours until it lifts.</summary>
    public bool ClaimOnce { get; init; } = true;
    public double SpeedPxPerMm { get; init; } = 12.0;
    public AccelerationSettings Acceleration { get; init; } = new();
    /// <summary>No movement until the contact moved this far from where it started (or where ChinPad took it over).</summary>
    public double DeadzoneMm { get; init; } = 0.3;
    /// <summary>EMA on the contact position: 0 = off .. 0.9 = heavy (alpha = 1 - smoothing per frame).</summary>
    public double Smoothing { get; init; } = 0.3;
    /// <summary>Upper limit of one injected step in pixels (protects against firmware position jumps).</summary>
    public double MaxStepPx { get; init; } = 150;
    /// <summary>"relative" (OS pointer speed/precision apply) or "absolute" (bypasses OS ballistics).</summary>
    public string MoveMode { get; init; } = "relative";
    /// <summary>Global pause/resume hotkey, e.g. "Ctrl+Alt+P". Empty = none.</summary>
    public string Hotkey { get; init; } = "Ctrl+Alt+P";
    public bool StartPaused { get; init; } = false;
    /// <summary>auto follows the Windows UI language; Serbian always uses Latin script.</summary>
    public string Language { get; init; } = "auto";
    /// <summary>Button zones (Phase 3): a touch classified in one never moves the cursor; pressing the pad while it is down clicks.</summary>
    [JsonConverter(typeof(ZoneListConverter))]
    public IReadOnlyList<ZoneSettings> Zones { get; init; } = ZoneSettings.Defaults;
    /// <summary>
    /// A new touch is undecided (never moves the cursor) for this long after its first report and is then
    /// classified by where it is at that moment; a press or a lift decides earlier. 0 = by the first report.
    /// </summary>
    public double ZoneSettleMs { get; init; } = 40;
    /// <summary>"landing": classified once, for the whole touch; "position" (default since 0.5.0): a settled touch is in a zone while it is inside one.</summary>
    public string ZoneMode { get; init; } = "position";
    /// <summary>A press with no zone touch down: "ignore" (the accidental-click fix) or "passthrough" (bit 0/1/2 = left/right/middle click).</summary>
    public string PressOutsideZones { get; init; } = "ignore";
    /// <summary>Drop Windows' own mouse button events while ChinPad owns a touch (low-level mouse hook).</summary>
    public bool HookClicks { get; init; } = true;
    /// <summary>A short beep for each click ChinPad sends.</summary>
    public bool ClickBeep { get; init; } = true;
    /// <summary>
    /// Contacts of one touchpad closer than this (mm) are one touch: some pads report a chin as two contacts.
    /// The cursor hands over to the partner without a jump, a partner takes the classification of the touch it
    /// belongs to, and merged zone touches press as one. 0 = off (every contact is a touch of its own, as in 0.3.1).
    /// </summary>
    public double MergeMm { get; init; } = 20;
    /// <summary>ClickLock (0.5.0): a long press keeps the button down until the next press. Off by default.</summary>
    public ClickLockSettings ClickLock { get; init; } = new();
    /// <summary>
    /// A second press of the same button within DoubleClickWindowMs after the previous click's down is dropped
    /// entirely (nothing is sent), so an accidental second press never becomes a double click (0.5.0).
    /// </summary>
    public bool BlockDoubleClick { get; init; } = true;
    /// <summary>The window of BlockDoubleClick in ms, 100 - 1500; 0 = Windows' double-click time (GetDoubleClickTime).</summary>
    public double DoubleClickWindowMs { get; init; } = 500;

    [JsonIgnore]
    public bool AbsoluteMoves => string.Equals(MoveMode, "absolute", StringComparison.OrdinalIgnoreCase);

    [JsonIgnore]
    public bool Passthrough => string.Equals(PressOutsideZones, "passthrough", StringComparison.OrdinalIgnoreCase);

    [JsonIgnore]
    public bool PositionZones => string.Equals(ZoneMode, "position", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// A record compares a list by reference, so two loads of the same file would never be equal. Settings
    /// are equal when they serialize to the same JSON (every key, the zones included).
    /// </summary>
    public bool Equals(AppSettings? other) => other is not null && ToCompactJson() == other.ToCompactJson();

    public override int GetHashCode() => ToCompactJson().GetHashCode(StringComparison.Ordinal);

    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // a local file, not HTML: keep '+' in "Ctrl+Alt+P" readable instead of a \u escape
    };

    static readonly JsonSerializerOptions CompactJson = new(Json) { WriteIndented = false };

    static readonly JsonDocumentOptions DocOptions = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    /// <summary>
    /// Reads, validates and (if keys are missing) rewrites the file. Throws when the file cannot be read or
    /// is not valid JSON for these settings; the caller then keeps what it had.
    /// </summary>
    public static AppSettings Load(string path, List<string> notes)
    {
        string text = File.ReadAllText(path);
        var missing = new List<string>();
        using (JsonDocument doc = JsonDocument.Parse(text, DocOptions))
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                throw new JsonException("the file must contain one JSON object { ... }");
            var unknown = new List<string>();
            CompareKeys(doc.RootElement, JsonSerializer.SerializeToElement(new AppSettings(), Json), "", missing, unknown);
            foreach (string u in unknown) notes.Add($"unknown key \"{u}\" ignored");
        }

        AppSettings s = (JsonSerializer.Deserialize<AppSettings>(text, Json) ?? new AppSettings()).Validated(notes);
        if (missing.Count > 0)
        {
            try
            {
                Save(path, s);
                notes.Add($"added missing keys ({string.Join(", ", missing)}); the file now lists every setting");
            }
            catch (Exception ex)
            {
                notes.Add($"missing keys ({string.Join(", ", missing)}) could not be written back: {ex.Message}");
            }
        }
        return s;
    }

    public static void Save(string path, AppSettings settings) =>
        File.WriteAllText(path, JsonSerializer.Serialize(settings, Json) + Environment.NewLine);

    /// <summary>All values on one line, for the log.</summary>
    public string ToCompactJson() => JsonSerializer.Serialize(this, CompactJson);

    /// <summary>Diag / caps entry point: like the overload below with defaults as the fallback; validation notes are appended to the note.</summary>
    public static AppSettings LoadOrCreate(string path, out string note)
    {
        AppSettings s = LoadOrCreate(path, null, out note, out List<string> details);
        if (details.Count > 0) note += ": " + string.Join("; ", details);
        return s;
    }

    /// <summary>
    /// Loads the file, or creates it with defaults when missing. A broken file is reported and left untouched,
    /// and <paramref name="fallback"/> (the settings in use) is kept, or the defaults when there are none yet.
    /// </summary>
    public static AppSettings LoadOrCreate(string path, AppSettings? fallback, out string note, out List<string> details)
    {
        details = [];
        if (!File.Exists(path))
        {
            AppSettings defaults = fallback ?? new AppSettings();
            try
            {
                Save(path, defaults);
                note = fallback is null ? "created with defaults" : "missing, written again with the current values";
            }
            catch (Exception ex)
            {
                note = "missing, could not create it: " + ex.Message;
            }
            return defaults;
        }
        try
        {
            AppSettings s = Load(path, details);
            note = details.Count == 0 ? "loaded" : "loaded with notes";
            return s;
        }
        catch (Exception ex)
        {
            note = (fallback is null ? "unreadable, using defaults: " : "unreadable, keeping the previous settings: ") + ex.Message;
            return fallback ?? new AppSettings();
        }
    }

    /// <summary>Lists the keys of <paramref name="full"/> absent from <paramref name="file"/> and the file keys it does not know (case-insensitive).</summary>
    static void CompareKeys(JsonElement file, JsonElement full, string prefix, List<string> missing, List<string> unknown)
    {
        foreach (JsonProperty p in full.EnumerateObject())
        {
            JsonElement? f = Find(file, p.Name);
            if (f is null)
                missing.Add(prefix + p.Name);
            else if (p.Value.ValueKind == JsonValueKind.Object)
            {
                if (f.Value.ValueKind == JsonValueKind.Object) CompareKeys(f.Value, p.Value, prefix + p.Name + ".", missing, unknown);
                else missing.Add(prefix + p.Name); // e.g. "acceleration": null
            }
        }
        foreach (JsonProperty q in file.EnumerateObject())
            if (Find(full, q.Name) is null) unknown.Add(prefix + q.Name);

        static JsonElement? Find(JsonElement obj, string name)
        {
            foreach (JsonProperty p in obj.EnumerateObject())
                if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) return p.Value;
            return null;
        }
    }

    /// <summary>Range check of every value. Out-of-range numbers are clamped, invalid choices take the default; each fix adds a note.</summary>
    public AppSettings Validated(List<string> notes)
    {
        var d = new AppSettings();
        double Range(string name, double v, double min, double max)
        {
            if (v >= min && v <= max) return v;
            double c = double.IsNaN(v) ? min : Math.Clamp(v, min, max);
            notes.Add(string.Create(Inv, $"{name} {v} is outside {min}..{max}, using {c}"));
            return c;
        }

        int strategy = Strategy;
        if (strategy is < 1 or > 3)
        {
            notes.Add(string.Create(Inv, $"strategy {Strategy} is not 1, 2 or 3, using {d.Strategy}"));
            strategy = d.Strategy;
        }
        string moveMode = (MoveMode ?? "").Trim().ToLowerInvariant();
        if (moveMode is not ("relative" or "absolute"))
        {
            notes.Add($"moveMode \"{MoveMode}\" is not \"relative\" or \"absolute\", using \"{d.MoveMode}\"");
            moveMode = d.MoveMode;
        }
        string language = (Language ?? "").Trim().ToLowerInvariant() switch
        {
            "auto" => "auto", "en" => "en", "sr-latn" => "sr-Latn", _ => "",
        };
        if (language.Length == 0)
        {
            notes.Add($"language \"{Language}\" is not \"auto\", \"en\" or \"sr-Latn\", using \"{d.Language}\"");
            language = d.Language;
        }
        string hotkey = (Hotkey ?? "").Trim();
        if (hotkey.Length > 0 && !HotkeySpec.TryParse(hotkey, out _, out string why))
        {
            notes.Add($"hotkey \"{hotkey}\": {why}; using \"{d.Hotkey}\"");
            hotkey = d.Hotkey;
        }
        AccelerationSettings a = Acceleration ?? new AccelerationSettings();
        ClickLockSettings lk = ClickLock ?? new ClickLockSettings();
        double window = DoubleClickWindowMs;
        if (window != 0 && !(window >= 100 && window <= 1500))
        {
            double c = double.IsNaN(window) ? d.DoubleClickWindowMs : Math.Clamp(window, 100, 1500);
            notes.Add(string.Create(Inv, $"doubleClickWindowMs {window} is outside 100..1500 (or 0 = Windows' double-click time), using {c}"));
            window = c;
        }

        var zones = new List<ZoneSettings>();
        if (Zones is null)
        {
            notes.Add("zones is not a list [ ... ], using the default zones");
            zones.AddRange(ZoneSettings.Defaults);
        }
        else
        {
            for (int i = 0; i < Zones.Count; i++)
            {
                ZoneSettings z = Zones[i];
                string? problem = z.Problem ?? z.RangeProblem(i + 1);
                if (problem is null) zones.Add(z);
                else notes.Add(problem + "; this zone is ignored");
            }
        }
        string press = (PressOutsideZones ?? "").Trim().ToLowerInvariant();
        if (press is not ("ignore" or "passthrough"))
        {
            notes.Add($"pressOutsideZones \"{PressOutsideZones}\" is not \"ignore\" or \"passthrough\", using \"{d.PressOutsideZones}\"");
            press = d.PressOutsideZones;
        }
        string zoneMode = (ZoneMode ?? "").Trim().ToLowerInvariant();
        if (zoneMode is not ("landing" or "position"))
        {
            notes.Add($"zoneMode \"{ZoneMode}\" is not \"landing\" or \"position\", using \"{d.ZoneMode}\"");
            zoneMode = d.ZoneMode;
        }

        return this with
        {
            Device = (Device ?? "").Trim(),
            Language = language,
            Strategy = strategy,
            SpeedPxPerMm = Range("speedPxPerMm", SpeedPxPerMm, 0.1, 200),
            Acceleration = a with
            {
                Exponent = Range("acceleration.exponent", a.Exponent, 1, 4),
                ThresholdMmPerS = Range("acceleration.thresholdMmPerS", a.ThresholdMmPerS, 1, 1000),
            },
            DeadzoneMm = Range("deadzoneMm", DeadzoneMm, 0, 20),
            Smoothing = Range("smoothing", Smoothing, 0, 0.9),
            MaxStepPx = Range("maxStepPx", MaxStepPx, 1, 5000),
            MoveMode = moveMode,
            Hotkey = hotkey,
            Zones = zones,
            ZoneSettleMs = Range("zoneSettleMs", ZoneSettleMs, 0, 500),
            ZoneMode = zoneMode,
            PressOutsideZones = press,
            MergeMm = Range("mergeMm", MergeMm, 0, 60),
            ClickLock = lk with { HoldMs = Range("clickLock.holdMs", lk.HoldMs, 300, 3000) },
            DoubleClickWindowMs = window,
        };
    }
}

/// <summary>
/// A hotkey such as "Ctrl+Alt+P": modifiers Ctrl/Control, Alt, Shift, Win joined with '+', then one key
/// name as System.Windows.Forms.Keys spells it (letters, digits 0-9, F1-F24, Pause, Scroll, Home, ...).
/// </summary>
internal readonly record struct HotkeySpec(uint Modifiers, Keys Key)
{
    static readonly Dictionary<string, Keys> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Esc"] = Keys.Escape, ["Del"] = Keys.Delete, ["Ins"] = Keys.Insert, ["PgUp"] = Keys.PageUp,
        ["PgDn"] = Keys.PageDown, ["ScrollLock"] = Keys.Scroll, ["Plus"] = Keys.Oemplus, ["Minus"] = Keys.OemMinus,
    };

    static readonly HashSet<Keys> NotAllowed =
    [
        Keys.None, Keys.LButton, Keys.RButton, Keys.MButton, Keys.XButton1, Keys.XButton2, Keys.Cancel,
        Keys.ShiftKey, Keys.ControlKey, Keys.Menu, Keys.LWin, Keys.RWin,
        Keys.LShiftKey, Keys.RShiftKey, Keys.LControlKey, Keys.RControlKey, Keys.LMenu, Keys.RMenu,
    ];

    public static bool TryParse(string? text, out HotkeySpec spec, out string error)
    {
        spec = default;
        string[] parts = (text ?? "").Split('+', StringSplitOptions.TrimEntries);
        if (parts.Length == 0 || parts.Any(p => p.Length == 0))
        {
            error = "expected modifiers and a key joined with '+', e.g. Ctrl+Alt+P";
            return false;
        }

        uint mods = 0;
        foreach (string m in parts[..^1])
        {
            uint bit = m.ToLowerInvariant() switch
            {
                "ctrl" or "control" => MOD_CONTROL,
                "alt" => MOD_ALT,
                "shift" => MOD_SHIFT,
                "win" or "windows" => MOD_WIN,
                _ => 0,
            };
            if (bit == 0)
            {
                error = $"unknown modifier \"{m}\" (use Ctrl, Alt, Shift, Win)";
                return false;
            }
            mods |= bit;
        }

        string name = parts[^1];
        Keys key;
        if (name.Length == 1 && name[0] is >= '0' and <= '9')
            key = Keys.D0 + (name[0] - '0');       // "5" would otherwise parse as the number 5 (XButton1)
        else if (Aliases.TryGetValue(name, out Keys alias))
            key = alias;
        else if (name.All(char.IsAsciiDigit) || name.Contains(',') ||
                 !Enum.TryParse(name, ignoreCase: true, out key))
        {
            error = $"unknown key \"{name}\"";
            return false;
        }

        if ((key & Keys.Modifiers) != 0 || (int)key > 0xFE || NotAllowed.Contains(key))
        {
            error = $"\"{name}\" cannot be the hotkey's key";
            return false;
        }
        if (mods == 0 && (key is >= Keys.A and <= Keys.Z or >= Keys.D0 and <= Keys.D9 or Keys.Space))
        {
            error = $"\"{name}\" without Ctrl/Alt/Shift/Win would stop that key from typing";
            return false;
        }
        spec = new HotkeySpec(mods, key);
        error = "";
        return true;
    }

    public override string ToString()
    {
        var parts = new List<string>();
        if ((Modifiers & MOD_CONTROL) != 0) parts.Add("Ctrl");
        if ((Modifiers & MOD_ALT) != 0) parts.Add("Alt");
        if ((Modifiers & MOD_SHIFT) != 0) parts.Add("Shift");
        if ((Modifiers & MOD_WIN) != 0) parts.Add("Win");
        parts.Add(Key is >= Keys.D0 and <= Keys.D9 ? ((char)('0' + (Key - Keys.D0))).ToString() : Key.ToString());
        return string.Join("+", parts);
    }
}
