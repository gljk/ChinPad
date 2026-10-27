namespace ChinPad;

/// <summary>Help keys match settings.json names; the text lives in the language dictionaries.</summary>
internal static class HelpTexts
{
    public const string Legend = "legend";
    public static IReadOnlyCollection<string> Keys { get; } = [
        "speedPxPerMm",
        "smoothing",
        "deadzoneMm",
        "zones",
        "zoneMode",
        "zoneSettleMs",
        "mergeMm",
        "clickBeep",
        "clickLock",
        "clickLock.holdMs",
        "blockDoubleClick",
        "doubleClickWindowMs",
        "device",
        "strategy",
        "startAtLogin",
        "startPaused",
        "hotkey",
        "legend",
        "language",
    ];
    public static string For(string key) => L10n.T("Help." + key);
}
