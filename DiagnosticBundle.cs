using System.Drawing;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using ChinPad.Native;
using Microsoft.Win32;

namespace ChinPad;

/// <summary>
/// "Save diagnostic bundle...": records the touchpad for 15 s while a small always-on-top window counts down,
/// then asks where to save and writes one zip that someone helping from afar can read: caps.txt (the touchpad
/// capabilities dump), settings.json, chinpad.log (the last 2000 lines), recording.csv (the 15 s of reports,
/// the --diag CSV format) and system.txt (ChinPad and Windows version, displays, settings location, counters).
/// Everything stays on this computer; nothing is sent anywhere. The cursor keeps working while it records:
/// the source (BackgroundMode) copies its reports into the recorder from its normal input path.
/// Runs on the UI thread; the save dialog is modal but pumps messages, so raw input keeps flowing.
/// </summary>
internal sealed class DiagnosticBundle
{
    /// <summary>What the bundle needs from the running app.</summary>
    public interface ISource
    {
        AppSettings Settings { get; }
        SettingsPaths Paths { get; }
        /// <summary>Start (recorder) or stop (null) copying every touchpad report into the recorder.</summary>
        void Record(ReportRecorder? recorder);
        string CapsDump();
        /// <param name="recording">What was recorded, one line, for the end of system.txt.</param>
        string SystemText(string recording);
        /// <summary>Where the save dialog starts.</summary>
        string DefaultFolder { get; }
        void Log(string line);
    }

    public const int LogLines = 2000;
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>Recording length in seconds (automated checks shorten it).</summary>
    internal static int Seconds { get; set; } = 15;
    /// <summary>Automated checks: picks the zip path instead of the save dialog (owner, folder, file name -> path or null).</summary>
    internal static Func<IWin32Window?, string, string, string?>? ChoosePathOverride { get; set; }
    /// <summary>Automated checks: no message box at the end.</summary>
    internal static bool Quiet { get; set; }

    readonly ISource _source;
    readonly DateTime _started = DateTime.Now;
    readonly string _tempDir;
    readonly ReportRecorder _recorder;
    readonly RecordingForm _window;
    double _startMs;
    bool _finished;

    /// <summary>Raised once at the end: the saved zip's path, or null (cancelled, not saved, or failed).</summary>
    public event Action<string?>? Finished;

    public DiagnosticBundle(ISource source)
    {
        _source = source;
        _tempDir = Path.Combine(Path.GetTempPath(), $"ChinPad-bundle-{_started.ToString("yyyyMMdd-HHmmss", Inv)}-{Environment.ProcessId}");
        _recorder = new ReportRecorder(source.Settings);
        _window = new RecordingForm(Math.Max(1, Seconds));
        _window.Done += completed => _window.BeginInvoke(() => Finish(completed));
    }

    public static string DefaultFileName(DateTime t) => "chinpad-bundle-" + t.ToString("yyyyMMdd-HHmmss", Inv) + ".zip";

    /// <summary>Opens the recording file and the countdown window. Throws when the temporary folder cannot be written.</summary>
    public void Start()
    {
        try
        {
            Directory.CreateDirectory(_tempDir);
            _recorder.Open(Path.Combine(_tempDir, "recording.csv"));
            _startMs = Clock.NowMs;
            _source.Record(_recorder);
            _source.Log(string.Create(Inv, $"diagnostic bundle: recording {Seconds} s of touchpad reports"));
            _window.Show();
        }
        catch
        {
            Cancel(); // also removes the recording window's language subscription
            throw;
        }
    }

    public void BringToFront()
    {
        if (_window.IsDisposed) return;
        _window.TopMost = true;
        _window.BringToFront();
    }

    /// <summary>Stops without saving (ChinPad exits).</summary>
    public void Cancel()
    {
        if (_finished) return;
        Finish(completed: false);
    }

    void Finish(bool completed)
    {
        if (_finished) return;
        _finished = true;
        _source.Record(null);
        double seconds = (Clock.NowMs - _startMs) / 1000.0;
        _recorder.Close();
        string? saved = null;
        try
        {
            if (!completed)
            {
                _source.Log("diagnostic bundle: cancelled, nothing saved");
                return;
            }
            _window.ShowSaving();
            string folder = _source.DefaultFolder;
            string name = DefaultFileName(DateTime.Now);
            string? path = ChoosePathOverride is { } choose ? choose(_window, folder, name) : AskPath(_window, folder, name);
            if (path is null)
            {
                _source.Log("diagnostic bundle: not saved (the save dialog was cancelled)");
                return;
            }
            string recording = string.Create(Inv, $"recorded {seconds:0.0} s from {_started:HH:mm:ss}: {_recorder.Rows} CSV rows");
            Write(path, recording);
            long bytes = new FileInfo(path).Length;
            _source.Log(string.Create(Inv, $"diagnostic bundle saved: {path} ({bytes / 1024.0:0} KB; {recording})"));
            saved = path;
            if (!Quiet)
                AppDialog.Show(_window, L10n.F("Bundle.Saved", path), "ChinPad");
        }
        catch (Exception ex)
        {
            _source.Log("diagnostic bundle could not be saved: " + ex.Message);
            if (!Quiet)
                AppDialog.Show(_window, L10n.F("Bundle.SaveFailed", L10n.Error(ex)), "ChinPad");
        }
        finally
        {
            _window.CloseNow();
            try { Directory.Delete(_tempDir, recursive: true); } catch (Exception) { }
            Finished?.Invoke(saved);
        }
    }

    static string? AskPath(IWin32Window owner, string folder, string name)
    {
        using var dialog = new SaveFileDialog
        {
            Title = L10n.T("Bundle.SaveTitle"),
            FileName = name,
            InitialDirectory = folder,
            Filter = L10n.T("Bundle.Filter"),
            DefaultExt = "zip",
            AddExtension = true,
            OverwritePrompt = true,
        };
        return dialog.ShowDialog(owner) == DialogResult.OK ? dialog.FileName : null;
    }

    /// <summary>Writes the zip (replacing an existing file of that name).</summary>
    void Write(string zipPath, string recording)
    {
        SettingsPaths paths = _source.Paths;
        string settingsText = File.Exists(paths.SettingsFile)
            ? ReadShared(paths.SettingsFile)
            : "(no settings.json at " + paths.SettingsFile + "; the settings in use:)\r\n" + _source.Settings.ToCompactJson();
        string temp = zipPath + ".tmp";
        using (var zip = ZipFile.Open(temp, ZipArchiveMode.Create))
        {
            AddText(zip, "caps.txt", _source.CapsDump());
            AddText(zip, "settings.json", settingsText);
            AddText(zip, "chinpad.log", LastLines(paths.LogFile, LogLines));
            zip.CreateEntryFromFile(_recorder.Path, "recording.csv", CompressionLevel.Optimal);
            AddText(zip, "system.txt", _source.SystemText(recording));
        }
        File.Move(temp, zipPath, overwrite: true);
    }

    static void AddText(ZipArchive zip, string name, string text)
    {
        ZipArchiveEntry e = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var w = new StreamWriter(e.Open(), new UTF8Encoding(false));
        w.Write(text);
    }

    /// <summary>Reads a file another writer may have open (the log is appended line by line).</summary>
    static string ReadShared(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var r = new StreamReader(fs, Encoding.UTF8);
        return r.ReadToEnd();
    }

    /// <summary>The last n lines of a text file ("" when it does not exist).</summary>
    public static string LastLines(string path, int n)
    {
        if (!File.Exists(path)) return "(no log file at " + path + ")";
        string[] lines = ReadShared(path).Split('\n');
        int count = lines.Length > 0 && lines[^1].Length == 0 ? lines.Length - 1 : lines.Length;
        int from = Math.Max(0, count - n);
        var sb = new StringBuilder();
        if (from > 0) sb.Append(string.Create(Inv, $"(the first {from} lines are left out; the last {n} follow)\r\n"));
        for (int i = from; i < count; i++) sb.Append(lines[i].TrimEnd('\r')).Append("\r\n");
        return sb.ToString();
    }

    // ------------------------------------------------------------------------------------------------
    // System information for system.txt
    // ------------------------------------------------------------------------------------------------

    /// <summary>"Windows 11 Pro, version 25H2, build 26200.6899, 64-bit" from the registry (ProductName still says Windows 10 on 11).</summary>
    public static string WindowsText()
    {
        try
        {
            using RegistryKey? k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            string product = k?.GetValue("ProductName") as string ?? "Windows";
            string display = k?.GetValue("DisplayVersion") as string ?? "";
            string build = k?.GetValue("CurrentBuild") as string ?? Environment.OSVersion.Version.Build.ToString(Inv);
            string ubr = k?.GetValue("UBR") is int u ? "." + u.ToString(Inv) : "";
            if (int.TryParse(build, NumberStyles.Integer, Inv, out int b) && b >= 22000) product = product.Replace("Windows 10", "Windows 11");
            return $"{product}{(display.Length > 0 ? ", version " + display : "")}, build {build}{ubr}, {(Environment.Is64BitOperatingSystem ? "64" : "32")}-bit";
        }
        catch (Exception ex)
        {
            return Environment.OSVersion.VersionString + " (registry: " + ex.Message + ")";
        }
    }

    /// <summary>One line per display: name, primary, bounds and working area in pixels, scaling.</summary>
    public static List<string> DisplayLines()
    {
        var lines = new List<string>();
        foreach (Screen s in Screen.AllScreens)
        {
            string scale = "scaling unknown";
            try
            {
                Rectangle b = s.Bounds;
                var center = new InputNative.POINT { X = b.X + b.Width / 2, Y = b.Y + b.Height / 2 };
                IntPtr mon = InputNative.MonitorFromPoint(center, InputNative.MONITOR_DEFAULTTONEAREST);
                if (InputNative.GetDpiForMonitor(mon, InputNative.MDT_EFFECTIVE_DPI, out uint dpi, out _) == 0)
                    scale = string.Create(Inv, $"scaling {dpi * 100 / 96} % ({dpi} dpi)");
            }
            catch (Exception)
            {
                // shcore.dll missing: leave it unknown
            }
            Rectangle r = s.Bounds, w = s.WorkingArea;
            lines.Add(string.Create(Inv,
                $"{s.DeviceName}{(s.Primary ? " (primary)" : "")}: {r.Width} x {r.Height} at ({r.X}, {r.Y}), working area {w.Width} x {w.Height} at ({w.X}, {w.Y}), {scale}"));
        }
        return lines;
    }

    /// <summary>Windows' mouse settings that change how ChinPad's relative moves land.</summary>
    public static string MouseText()
    {
        try
        {
            using RegistryKey? k = Registry.CurrentUser.OpenSubKey(@"Control Panel\Mouse");
            string speed = k?.GetValue("MouseSensitivity") as string ?? "?";
            string accel = k?.GetValue("MouseSpeed") as string ?? "?";
            return $"pointer speed {speed} of 20, Enhance pointer precision {(accel == "0" ? "off" : accel == "?" ? "unknown" : "on")} (MouseSpeed {accel})";
        }
        catch (Exception ex)
        {
            return "unknown (" + ex.Message + ")";
        }
    }

    /// <summary>Windows' own touchpad settings (HKCU ... PrecisionTouchPad and its Status key), one "name = value" per line.</summary>
    public static List<string> TouchpadSettingsLines()
    {
        var lines = new List<string>();
        try
        {
            const string key = @"Software\Microsoft\Windows\CurrentVersion\PrecisionTouchPad";
            foreach (string sub in new[] { key, key + @"\Status" })
            {
                using RegistryKey? k = Registry.CurrentUser.OpenSubKey(sub);
                if (k is null) continue;
                foreach (string name in k.GetValueNames().OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
                {
                    object? v = k.GetValue(name);
                    string text = v switch
                    {
                        byte[] bytes => Convert.ToHexString(bytes),
                        string[] many => string.Join("; ", many),
                        null => "",
                        _ => Convert.ToString(v, Inv) ?? "",
                    };
                    lines.Add($"{(sub == key ? "" : "Status\\")}{(name.Length == 0 ? "(default)" : name)} = {text}");
                }
            }
            if (lines.Count == 0) lines.Add("(no PrecisionTouchPad settings found)");
        }
        catch (Exception ex)
        {
            lines.Add("cannot read: " + ex.Message);
        }
        return lines;
    }

    // ------------------------------------------------------------------------------------------------

    /// <summary>
    /// The small always-on-top window shown while recording: countdown, what to do, Cancel. It does not take
    /// the focus when it appears (the user may be typing elsewhere), and it closes itself when done.
    /// </summary>
    sealed class RecordingForm : Form
    {
        readonly Label _count, _text;
        readonly Button _cancel;
        readonly System.Windows.Forms.Timer _timer = new() { Interval = 250 };
        readonly double _endMs;
        bool _done, _closing, _saving;

        /// <summary>Raised once: true = the time ran out, false = Cancel or the window was closed.</summary>
        public event Action<bool>? Done;

        public RecordingForm(int seconds)
        {
            SuspendLayout();
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Segoe UI", 12F);
            Text = L10n.T("Bundle.Title");
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = true;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Padding = new Padding(16);

            var layout = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, Dock = DockStyle.Fill };
            _count = new Label
            {
                AutoSize = true,
                Font = new Font("Segoe UI", 28F, FontStyle.Bold),
                Text = seconds.ToString(Inv),
                Anchor = AnchorStyles.None,
            };
            _text = new Label
            {
                AutoSize = true,
                MaximumSize = new Size(460, 0),
                Text = L10n.T("Bundle.Instructions"),
                Margin = new Padding(3, 3, 3, 12),
            };
            _cancel = new Button
            {
                Text = L10n.T("Bundle.Cancel"),
                AutoSize = true,
                MinimumSize = new Size(140, 44),
                Anchor = AnchorStyles.Right,
            };
            _cancel.Click += (_, _) => End(false);
            layout.Controls.Add(_count);
            layout.Controls.Add(_text);
            layout.Controls.Add(_cancel);
            Controls.Add(layout);
            CancelButton = _cancel;
            ResumeLayout(false);
            PerformLayout();

            L10n.Changed += Relabel;
            _endMs = Clock.NowMs + seconds * 1000.0;
            _timer.Tick += (_, _) => Tick();
        }

        /// <summary>Appear without taking the focus from the window the user works in.</summary>
        protected override bool ShowWithoutActivation => true;

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            // Top centre of the screen the cursor is on, clear of the taskbar.
            Rectangle wa = Screen.FromPoint(Cursor.Position).WorkingArea;
            Location = new Point(wa.X + (wa.Width - Width) / 2, wa.Y + wa.Height / 12);
            _timer.Start();
        }

        void Tick()
        {
            double left = _endMs - Clock.NowMs;
            _count.Text = Math.Max(0, (int)Math.Ceiling(left / 1000.0)).ToString(Inv);
            if (left <= 0) End(true);
        }

        /// <summary>After the recording: the window stays up as the save dialog's owner.</summary>
        public void ShowSaving()
        {
            _saving = true;
            Relabel();
            _cancel.Enabled = false;
        }

        void Relabel()
        {
            if (IsDisposed) return;
            Text = L10n.T("Bundle.Title");
            _text.Text = L10n.T(_saving ? "Bundle.Saving" : "Bundle.Instructions");
            _cancel.Text = L10n.T("Bundle.Cancel");
            if (_saving) _count.Text = L10n.T("Bundle.Done");
        }

        void End(bool completed)
        {
            if (_done) return;
            _done = true;
            _timer.Stop();
            Done?.Invoke(completed);
        }

        /// <summary>The close box cancels like the Cancel button; the owner closes the window when it has finished.</summary>
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            if (e.CloseReason != CloseReason.UserClosing || _closing) return;
            e.Cancel = true;
            End(false);
        }

        /// <summary>Closes the window for good (the owner calls it).</summary>
        public void CloseNow()
        {
            if (IsDisposed) return;
            _closing = true;
            Close();
            Dispose();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                L10n.Changed -= Relabel;
                _timer.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
