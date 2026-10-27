using System.Globalization;
using ChinPad.Native;

namespace ChinPad;

internal enum RunMode { Background, Wizard, Help, Caps, Diag, Version, EnableStartAtLogin, DisableStartAtLogin }

/// <summary>How the process got its console (it is a WinExe, so it has none at start).</summary>
internal enum ConsoleKind
{
    Redirected, // stdout is a file or pipe (e.g. "ChinPad.exe --caps > caps.txt"): write there, no console needed
    Attached,   // attached to the console of the terminal that started us
    Allocated,  // our own new console window (double-click, shortcut, or the live diagnostic)
    None,       // no console could be obtained; output is lost, the CSV still works
}

internal static class Program
{
    const string Usage =
        "Usage:\r\n" +
        "  ChinPad.exe [--seconds N]          background mode: tray icon, the chin moves the cursor (log: chinpad.log)\r\n" +
        "  ChinPad.exe --diag [--seconds N]   live diagnostic: caps dump, live contact table, CSV log\r\n" +
        "  ChinPad.exe --wizard               first-time setup (also opens automatically without settings)\r\n" +
        "  ChinPad.exe --caps                 print the touchpad capabilities dump and exit\r\n" +
        "  ChinPad.exe --version              print the version and exit\r\n" +
        "  ChinPad.exe --enable-start-at-login register this executable for sign-in and exit\r\n" +
        "  ChinPad.exe --disable-start-at-login remove the sign-in entry and exit\r\n" +
        "Keys in --diag: F = 'finger' marker, C = 'chin' marker, D = rate mode, Q or Esc = save and quit.\r\n" +
        "--seconds N stops the background mode or the diagnostic after N seconds (for automated checks).\r\n" +
        "settings.json, chinpad.log, the CSV and diag-caps.txt are next to ChinPad.exe when a settings.json is there\r\n" +
        "(portable mode), otherwise in %LOCALAPPDATA%\\ChinPad.";

    [STAThread]
    static int Main(string[] args)
    {
        (RunMode mode, int? seconds, string? argError) = ParseArgs(args);

        // Background mode never has a console (it logs to chinpad.log).
        if (mode is RunMode.Background or RunMode.Wizard && argError is null)
            return BackgroundMode.Run(ExeDir, seconds, wizard: mode == RunMode.Wizard);

        // OutputType is WinExe, so there is no console yet. Connect one BEFORE the first Console call:
        // .NET binds Console.Out / Console.In to the standard handles on first use.
        ConsoleKind console = ConsoleHost.Setup(ownWindow: mode == RunMode.Diag, SettingsPaths.Resolve(ExeDir, create: false).Folder,
            allocateWindow: mode is not (RunMode.EnableStartAtLogin or RunMode.DisableStartAtLogin));

        int code;
        try
        {
            code = Run(mode, seconds, argError, console);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("ChinPad: fatal error: " + ex);
            code = 1;
        }

        // A console we opened ourselves disappears with the process. Keep it open until a key is pressed so
        // the output stays readable, including the diag's "stopped ... CSV: <path>" summary after Q / Esc.
        // With --seconds (automated runs) never block.
        if (console == ConsoleKind.Allocated && mode is not (RunMode.Version or RunMode.EnableStartAtLogin or RunMode.DisableStartAtLogin) &&
            (mode != RunMode.Diag || seconds is null))
            ConsoleHost.WaitForKey();
        return code;
    }

    static string ExeDir => Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;

    static (RunMode Mode, int? Seconds, string? Error) ParseArgs(string[] args)
    {
        RunMode mode = RunMode.Background;
        int? seconds = null;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--version":
                    mode = RunMode.Version;
                    break;
                case "--enable-start-at-login":
                    mode = RunMode.EnableStartAtLogin;
                    break;
                case "--disable-start-at-login":
                    mode = RunMode.DisableStartAtLogin;
                    break;
                case "--wizard":
                    mode = RunMode.Wizard;
                    break;
                case "--diag":
                    mode = RunMode.Diag;
                    break;
                case "--caps":
                    mode = RunMode.Caps;
                    break;
                case "--seconds":
                    if (i + 1 >= args.Length ||
                        !int.TryParse(args[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int s) ||
                        s < 1 || s > 86400)
                        return (mode, null, "--seconds needs a number of seconds between 1 and 86400");
                    seconds = s;
                    i++;
                    break;
                case "--help":
                case "-h":
                case "/?":
                    mode = RunMode.Help;
                    break;
                default:
                    return (mode, null, "unknown argument: " + args[i]);
            }
        }
        return (mode, seconds, null);
    }

    static int Run(RunMode mode, int? seconds, string? argError, ConsoleKind console)
    {
        // When attached to a terminal, the shell has usually printed its prompt already; start on a fresh line.
        if (console == ConsoleKind.Attached) Console.WriteLine();

        if (argError is not null)
        {
            Console.Error.WriteLine("ChinPad: " + argError);
            Console.WriteLine(Usage);
            return 2;
        }
        if (mode == RunMode.Help)
        {
            Console.WriteLine(Usage);
            return 0;
        }

        if (mode == RunMode.Version)
        {
            Console.WriteLine($"{AppInfo.Name} {AppInfo.Version}");
            return 0;
        }
        if (mode is RunMode.EnableStartAtLogin or RunMode.DisableStartAtLogin)
        {
            if (mode == RunMode.EnableStartAtLogin)
                StartAtLogin.Enable(Environment.ProcessPath ?? Path.Combine(ExeDir, "ChinPad.exe"));
            else
                StartAtLogin.Disable();
            return 0;
        }

        // settings.json next to the exe (portable mode) or in %LOCALAPPDATA%\ChinPad; the CSV and diag-caps.txt go there too.
        SettingsPaths paths = SettingsPaths.Resolve(ExeDir);
        AppSettings settings = AppSettings.LoadOrCreate(paths.SettingsFile, out string settingsNote);
        settingsNote += ", " + paths.SettingsFile;

        switch (mode)
        {
            case RunMode.Caps:
                return RunCaps(settings, settingsNote);
            default:
                return RunDiag(settings, settingsNote, paths.Folder, seconds, console);
        }
    }

    /// <summary>--caps: dump and exit, without registering for raw input.</summary>
    static int RunCaps(AppSettings settings, string settingsNote)
    {
        List<TouchpadDevice> devices = TouchpadDevice.EnumerateAll(out List<string> errors);
        try
        {
            Console.Write(TouchpadDevice.DumpAll(devices, errors, settings, settingsNote));
            return devices.Count > 0 ? 0 : 3;
        }
        finally
        {
            foreach (TouchpadDevice d in devices) d.Dispose();
        }
    }

    /// <summary>
    /// --diag: a WinForms message loop on this (STA) thread delivers WM_INPUT to the hidden InputWindow
    /// and runs the timers. DiagMode ends the loop with Application.ExitThread (Q / Esc / --seconds).
    /// </summary>
    static int RunDiag(AppSettings settings, string settingsNote, string outDir, int? seconds, ConsoleKind console)
    {
        ConsoleHost.TrySetTitle("ChinPad diag");
        List<TouchpadDevice> devices = TouchpadDevice.EnumerateAll(out List<string> errors);
        try
        {
            string dump = TouchpadDevice.DumpAll(devices, errors, settings, settingsNote);

            // A hidden diagnostic must not pop up the WinForms exception dialog; report on the console instead.
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (_, e) => Console.Error.WriteLine("ChinPad: UI thread error: " + e.Exception.Message);

            using var window = new InputWindow();
            using var diag = new DiagMode(devices, window, outDir, dump,
                plain: console is ConsoleKind.Redirected or ConsoleKind.None, seconds, settings);
            if (!diag.Start()) return 3;
            Application.Run(new ApplicationContext());
            diag.Finish();
            return 0;
        }
        finally
        {
            foreach (TouchpadDevice d in devices) d.Dispose();
        }
    }
}

/// <summary>
/// Console plumbing for a WinExe.
///  - stdout already redirected to a file or pipe: use it as is.
///  - started from a terminal: AttachConsole(ATTACH_PARENT_PROCESS). Fine for --caps. For the live
///    diagnostic it is not: a shell does not wait for a GUI-subsystem exe, so it would print its prompt
///    over the live table and read the same keystrokes. The diagnostic therefore leaves a one-line note
///    in the terminal and opens its own console window instead.
///  - no parent console (double-click, shortcut): AllocConsole().
/// </summary>
internal static class ConsoleHost
{
    /// <param name="outDir">Where the diagnostic saves its files (for the note left in the terminal).</param>
    public static ConsoleKind Setup(bool ownWindow, string outDir, bool allocateWindow = true)
    {
        if (IsFileOrPipe(ConsoleNative.STD_OUTPUT_HANDLE))
            return ConsoleKind.Redirected;

        if (ConsoleNative.AttachConsole(ConsoleNative.ATTACH_PARENT_PROCESS))
        {
            if (!ownWindow)
            {
                EnsureStdHandles();
                return ConsoleKind.Attached;
            }
            WriteToConsole("\r\nChinPad: the live diagnostic opens in its own console window " +
                           $"(this terminal does not wait for it). Caps dump and CSV are saved in {outDir}\r\n");
            ConsoleNative.FreeConsole();
        }

        if (allocateWindow && ConsoleNative.AllocConsole())
        {
            EnsureStdHandles();
            return ConsoleKind.Allocated;
        }
        return ConsoleKind.None;
    }

    public static void WaitForKey()
    {
        try
        {
            Console.WriteLine();
            Console.Write("Press any key to close this window.");
            Console.ReadKey(intercept: true);
        }
        catch (Exception)
        {
            // no console input
        }
    }

    public static void TrySetTitle(string title)
    {
        try { Console.Title = title; } catch (Exception) { }
    }

    static bool IsFileOrPipe(int stdHandle)
    {
        IntPtr h = ConsoleNative.GetStdHandle(stdHandle);
        if (h == IntPtr.Zero || h == ConsoleNative.INVALID_HANDLE_VALUE) return false;
        uint type = ConsoleNative.GetFileType(h);
        return type is ConsoleNative.FILE_TYPE_DISK or ConsoleNative.FILE_TYPE_PIPE;
    }

    /// <summary>
    /// AttachConsole / AllocConsole normally set the standard handles. If one is still missing or invalid,
    /// point it at the console explicitly (CONOUT$ / CONIN$) so .NET's Console finds it.
    /// </summary>
    static void EnsureStdHandles()
    {
        Fix(ConsoleNative.STD_OUTPUT_HANDLE, "CONOUT$");
        Fix(ConsoleNative.STD_ERROR_HANDLE, "CONOUT$");
        Fix(ConsoleNative.STD_INPUT_HANDLE, "CONIN$");

        static void Fix(int std, string device)
        {
            IntPtr h = ConsoleNative.GetStdHandle(std);
            if (h != IntPtr.Zero && h != ConsoleNative.INVALID_HANDLE_VALUE &&
                ConsoleNative.GetFileType(h) != ConsoleNative.FILE_TYPE_UNKNOWN)
                return;
            IntPtr c = OpenConsoleDevice(device);
            if (c != ConsoleNative.INVALID_HANDLE_VALUE) ConsoleNative.SetStdHandle(std, c);
        }
    }

    static IntPtr OpenConsoleDevice(string device) =>
        ConsoleNative.CreateFileW(device, ConsoleNative.GENERIC_READ | ConsoleNative.GENERIC_WRITE,
            ConsoleNative.FILE_SHARE_READ | ConsoleNative.FILE_SHARE_WRITE, IntPtr.Zero, ConsoleNative.OPEN_EXISTING, 0, IntPtr.Zero);

    /// <summary>Writes straight to the attached console without touching .NET's Console (which would bind to it).</summary>
    static void WriteToConsole(string text)
    {
        IntPtr h = OpenConsoleDevice("CONOUT$");
        if (h == ConsoleNative.INVALID_HANDLE_VALUE) return;
        ConsoleNative.WriteConsoleW(h, text, (uint)text.Length, out _, IntPtr.Zero);
        ConsoleNative.CloseHandle(h);
    }
}
