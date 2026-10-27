using Microsoft.Win32;

namespace ChinPad;

/// <summary>
/// "Start at login" in the tray menu: the per-user Run key HKCU\Software\Microsoft\Windows\CurrentVersion\Run,
/// value "ChinPad", data = the quoted path of the exe. Explorer starts it at sign-in, as the user, without
/// elevation; no admin rights are needed to set or remove it.
/// </summary>
internal static class StartAtLogin
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string ValueName = "ChinPad";

    // Tests in restricted sessions use an in-memory value; production always uses HKCU Run.
    internal static Func<string?>? ReadOverride { get; set; }
    internal static Action<string?>? WriteOverride { get; set; }

    /// <summary>The command line registered under "ChinPad", or null when there is none.</summary>
    public static string? Current()
    {
        if (ReadOverride is { } read) return read();
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
        return key?.GetValue(ValueName) as string;
    }

    public static string CommandFor(string exePath) => "\"" + exePath + "\"";

    /// <summary>True when the Run value starts exactly this exe (another path, e.g. an older copy, does not count).</summary>
    public static bool IsEnabled(string exePath) =>
        string.Equals(Current(), CommandFor(exePath), StringComparison.OrdinalIgnoreCase);

    /// <summary>Registers this exe; replaces a value that pointed elsewhere. Throws when the key cannot be written.</summary>
    public static void Enable(string exePath)
    {
        if (WriteOverride is { } write) { write(CommandFor(exePath)); return; }
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
        key.SetValue(ValueName, CommandFor(exePath), RegistryValueKind.String);
    }

    public static void Disable()
    {
        if (WriteOverride is { } write) { write(null); return; }
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        key?.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
