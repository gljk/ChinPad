namespace ChinPad;

/// <summary>Shared release information. ProjectPage is where users look for new versions.</summary>
internal static class AppInfo
{
    public const string Name = "ChinPad";
    public const string ProjectPage = "github.com/gljk/ChinPad";
    public static string Version => typeof(AppInfo).Assembly.GetName().Version?.ToString(3) ?? "?";
}
