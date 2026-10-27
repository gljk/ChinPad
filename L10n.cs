using System.Globalization;

namespace ChinPad;

/// <summary>App text only: never changes the culture used by logs, JSON or diagnostic output.</summary>
internal static class L10n
{
    static readonly List<string> _missingKeys = [];
    public static IReadOnlyList<string> MissingKeys => _missingKeys;
    public static string Current { get; private set; } = "en";
    public static CultureInfo Culture { get; private set; } = CultureInfo.GetCultureInfo("en-US");
    public static event Action? Changed;

    public static void Apply(string languageSetting)
    {
        string language = languageSetting switch
        {
            "en" => "en",
            "sr-Latn" => "sr-Latn",
            _ => CultureInfo.CurrentUICulture.Name.StartsWith("sr", StringComparison.OrdinalIgnoreCase) ? "sr-Latn" : "en",
        };
        if (Current == language) return;
        Current = language;
        Culture = CultureInfo.GetCultureInfo(language == "sr-Latn" ? "sr-Latn-RS" : "en-US");
        Changed?.Invoke();
    }

    public static string T(string key)
    {
        if (Current == "sr-Latn" && Strings.SerbianLatin.TryGetValue(key, out string? translated)) return translated;
        if (Current == "sr-Latn" || !Strings.English.ContainsKey(key))
            if (!_missingKeys.Contains(key)) _missingKeys.Add(key);
        return Strings.English.TryGetValue(key, out string? english) ? english : key;
    }

    public static string F(string key, params object?[] args) => string.Format(Culture, T(key), args);

    // Exception messages belong in the log. OS/framework prose may be English even in a Serbian UI.
    public static string Error(Exception ex) => F(ex switch
    {
        UnauthorizedAccessException => "Error.Access",
        IOException => "Error.File",
        _ => "Error.Other",
    }, ex.HResult);

    public static string HotkeyRejection(string text, string reason) => F("Hotkey.Rejected", text,
        T(reason.Contains("without Ctrl/Alt/Shift/Win", StringComparison.Ordinal) ? "Hotkey.Typing" : "Hotkey.Invalid"));
}
