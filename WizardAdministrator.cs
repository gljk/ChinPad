using System.Diagnostics;
using System.Security.Principal;
using System.Text;

namespace ChinPad;

/// <summary>Option a only. No task is registered until the user explicitly presses the setup button.</summary>
internal static class WizardAdministrator
{
    static string Literal(string text) => "'" + text.Replace("'", "''") + "'";

    // The original interactive user is captured BEFORE elevation (credentials may be for another account).
    public static string CommandText(string exe, string user) => $$"""
        $ErrorActionPreference = 'Stop'
        $exe = {{Literal(exe)}}
        $user = {{Literal(user)}}
        $action = New-ScheduledTaskAction -Execute $exe -WorkingDirectory (Split-Path $exe)
        $trigger = New-ScheduledTaskTrigger -AtLogOn -User $user
        $principal = New-ScheduledTaskPrincipal -UserId $user -LogonType Interactive -RunLevel Highest
        $settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit ([TimeSpan]::Zero) -MultipleInstances IgnoreNew
        Register-ScheduledTask -TaskName 'ChinPad' -Action $action -Trigger $trigger -Principal $principal -Settings $settings
        """;

    public static string Encoded(string command) => Convert.ToBase64String(Encoding.Unicode.GetBytes(command));

    public static async Task<bool> SetupAsync(string exe)
    {
        string user = WindowsIdentity.GetCurrent().Name;
        // ShellExecute uses Start-Process powershell -Verb RunAs semantics, with one UAC prompt.
        // Encode the outer script as well: paths, apostrophes and shell metacharacters stay literal.
        string launch = "$ErrorActionPreference = 'Stop'; $p = Start-Process powershell.exe -Verb RunAs -WindowStyle Hidden -PassThru -Wait -ArgumentList " +
            Literal("-NoProfile -NonInteractive -EncodedCommand " + Encoded(CommandText(exe, user))) +
            "; exit $p.ExitCode";
        using Process? elevated = Process.Start(new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
            Arguments = "-NoProfile -NonInteractive -EncodedCommand " + Encoded(launch),
        });
        if (elevated is null) return false;
        await elevated.WaitForExitAsync();
        if (elevated.ExitCode != 0) return false;
        return await ExistsAsync();
    }

    static async Task<bool> ExistsAsync()
    {
        using Process? query = Process.Start(new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
            Arguments = "-NoProfile -NonInteractive -EncodedCommand " + Encoded(
                "if (Get-ScheduledTask -TaskName 'ChinPad' -ErrorAction SilentlyContinue) { exit 0 } else { exit 1 }"),
        });
        if (query is null) return false;
        await query.WaitForExitAsync();
        return query.ExitCode == 0;
    }
}
