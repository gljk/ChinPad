using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using ChinPad.Native;

namespace ChinPad;

/// <summary>What the settings window needs from the running app (BackgroundMode).</summary>
internal interface ISettingsHost
{
    /// <summary>The settings in use (after the last reload of settings.json).</summary>
    AppSettings Settings { get; }
    SettingsPaths Paths { get; }
    bool Paused { get; }
    void SetPaused(bool paused, string source);
    /// <summary>"" when the pause key works (or there is none); otherwise why it does not, in plain words.</summary>
    string HotkeyProblem { get; }
    /// <summary>While the window records a new pause key, the current one must not fire.</summary>
    void SuspendHotkey(bool suspended);
    /// <summary>The live state for the drawing, polled about 30 times a second.</summary>
    LiveState Live();
    /// <summary>Writes settings.json (AppSettings.Save); the file watcher applies it. Throws when the file cannot be written.</summary>
    void WriteSettings(AppSettings settings, string why);
    bool StartAtLoginEnabled { get; }
    void SetStartAtLogin(bool on);
    void SaveDiagnosticBundle();
    void OpenFile(string path);
    /// <summary>The "Apply to" list (0.5.0): every touchpad found, built-in ones first, with "reporting now"; polled twice a second.</summary>
    IReadOnlyList<TouchpadChoice> Touchpads();
    /// <summary>The Refresh button: look for touchpads again and apply the selection again.</summary>
    void RefreshTouchpads();
    /// <summary>Pause, settings, pause key, start-at-login state or the touchpads found changed.</summary>
    void ShowWizard() { }
    event Action? StateChanged;
}

/// <summary>
/// Every text the settings window and its touchpad drawing show, through the language dictionaries. The
/// explanations behind the "?" buttons are in HelpTexts, the words of the touchpad labels in PadChoice.Text.
/// An '&amp;' marks the Alt+letter of a control.
/// </summary>
internal static class SettingsText
{
    public static string Title => L10n.T("Settings.Title");
    public static string PadNone => L10n.T("Settings.PadNone");
    public static string Pause => L10n.T("Settings.Pause");
    public static string Resume => L10n.T("Settings.Resume");
    public static string MovingHeading => L10n.T("Settings.MovingHeading");
    public static string Speed => L10n.T("Settings.Speed");
    public static string Smoothing => L10n.T("Settings.Smoothing");
    public static string Deadzone => L10n.T("Settings.Deadzone");
    public static string ClickingHeading => L10n.T("Settings.ClickingHeading");
    public static string Strip => L10n.T("Settings.Strip");
    public static string StripModes => L10n.T("Settings.StripModes");
    public static string Position => L10n.T("Settings.Position");
    public static string Landing => L10n.T("Settings.Landing");
    public static string StripModesHelp => L10n.T("Settings.StripModesHelp");
    public static string Settle => L10n.T("Settings.Settle");
    public static string Merge => L10n.T("Settings.Merge");
    public static string Beep => L10n.T("Settings.Beep");
    public static string ClickLock => L10n.T("Settings.ClickLock");
    public static string ClickLockHold => L10n.T("Settings.ClickLockHold");
    public static string DoubleClick => L10n.T("Settings.DoubleClick");
    public static string DoubleClickWindow => L10n.T("Settings.DoubleClickWindow");
    public static string GeneralHeading => L10n.T("Settings.GeneralHeading");
    public static string ApplyTo => L10n.T("Settings.ApplyTo");
    public static string AllTouchpads => L10n.T("Settings.AllTouchpads");
    public static string Refresh => L10n.T("Settings.Refresh");
    public static string Fingers => L10n.T("Settings.Fingers");
    public static string FingersExpert => L10n.T("Settings.FingersExpert");
    public static string Login => L10n.T("Settings.Login");
    public static string StartPaused => L10n.T("Settings.StartPaused");
    public static string PauseKey => L10n.T("Settings.PauseKey");
    public static string PauseKeyName => L10n.T("Settings.PauseKeyName");
    public static string PauseKeyHint => L10n.T("Settings.PauseKeyHint");
    public static string PauseKeyRecording => L10n.T("Settings.PauseKeyRecording");
    public static string NoKey => L10n.T("Settings.NoKey");
    public static string Bundle => L10n.T("Settings.Bundle");
    public static string BundleTip => L10n.T("Settings.BundleTip");
    public static string OpenLog => L10n.T("Settings.OpenLog");
    public static string OpenSettings => L10n.T("Settings.OpenSettings");
    public static string Restore => L10n.T("Settings.Restore");
    public static string Wizard => L10n.T("Settings.Wizard");
    public static string WizardTip => L10n.T("Settings.WizardTip");
    public static string Close => L10n.T("Settings.Close");
    public static string NoPadInUse => L10n.T("Settings.NoPadInUse");
    public static string CustomLayout => L10n.T("Settings.CustomLayout");
    public static string Off => L10n.T("Settings.Off");
    public static string StartAtLoginFailed => L10n.T("Settings.StartAtLoginFailed");
    public static string RestoreTitle => L10n.T("Settings.RestoreTitle");
    public static string RestoreQuestion => L10n.T("Settings.RestoreQuestion");
    public static string Paused => L10n.T("Settings.Paused");
    public static string HelpClose => L10n.T("Settings.HelpClose");
    public static string HelpLegend => L10n.T("Settings.HelpLegend");
    public static string LessButton => L10n.T("Settings.LessButton");
    public static string MoreButton => L10n.T("Settings.MoreButton");
    public static string TouchNone => L10n.T("Settings.TouchNone");
    public static string TouchDead => L10n.T("Settings.TouchDead");
    public static string TouchMoving => L10n.T("Settings.TouchMoving");
    public static string TouchMove => L10n.T("Settings.TouchMove");
    public static string TouchLanded => L10n.T("Settings.TouchLanded");
    public static string TouchFinger => L10n.T("Settings.TouchFinger");
    public static string DrawingName => L10n.T("Settings.DrawingName");
    public static string DrawingNone => L10n.T("Settings.DrawingNone");
    public static string KeyboardSide => L10n.T("Settings.KeyboardSide");
    public static string MoveAreaNoZones => L10n.T("Settings.MoveAreaNoZones");
    public static string MoveArea => L10n.T("Settings.MoveArea");
    public static string PressPrefix => L10n.T("Settings.PressPrefix");
    public static string LegendMoving => L10n.T("Settings.LegendMoving");
    public static string LegendMove => L10n.T("Settings.LegendMove");
    public static string LegendZone => L10n.T("Settings.LegendZone");
    public static string LegendUndecided => L10n.T("Settings.LegendUndecided");
    public static string LegendWindows => L10n.T("Settings.LegendWindows");

    public static string Saved(DateTime t) => L10n.F("Settings.Saved", t);
    public static string SaveFailed(string why) => L10n.F("Settings.SaveFailed", why);
    public static string SettingsFile(string path, bool portable) => L10n.F("Settings.SettingsFile", path, portable ? L10n.T("Settings.Portable") : "");
    public static string PadTitle(int index, string name, double wMm, double hMm) => L10n.F("Settings.PadTitle", index, name, wMm, hMm);
    public static string Live(string touch, long clicks, string detail, long dropped) => L10n.F("Settings.Live", touch, clicks, detail, dropped);
    public static string ClickDetail(long left, long right, long middle) => L10n.F("Settings.ClickDetail", left, right, middle > 0 ? L10n.F("Settings.MiddleDetail", middle) : "");
    public static string Problem(string what) => L10n.F("Settings.Problem", what);
    public static string Locked(MouseButton b) => L10n.T(b switch { MouseButton.Left => "Settings.LockedLeft", MouseButton.Right => "Settings.LockedRight", _ => "Settings.LockedMiddle" });
    public static string ButtonName(MouseButton b) => L10n.T(b switch { MouseButton.Left => "Button.Left", MouseButton.Right => "Button.Right", _ => "Button.Middle" });
    public static string HelpFor(string setting) => L10n.F("Settings.HelpFor", setting.Replace("&", ""));
    public static string DeviceMissing(string device) => L10n.F("Settings.DeviceMissing", device);
    public static string DeviceCustom(string device, int count) => L10n.F("Settings.DeviceCustom", device, count);
    public static string PxPerMm(double v) => L10n.F("Value.PxPerMm", v);
    public static string SmoothingValue(double v) => v == 0 ? Off : L10n.F("Value.Smoothing", v);
    public static string Mm1(double v) => L10n.F("Value.Mm1", v);
    public static string Ms(double v) => L10n.F("Value.Ms", v);
    public static string MergeValue(double v) => v == 0 ? Off : L10n.F("Value.Mm", v);
    public static string WindowsDoubleClick(uint ms) => L10n.F("Value.WindowsDoubleClick", ms);
    public static string TouchZone(ZoneAction a) => L10n.T("Touch." + a);
    public static string ZoneName(ZoneAction a) => L10n.T("Zone." + a);
    public static string PressOutcome(string outcome) => L10n.T(outcome switch
    {
        "left" => "Press.Left", "right" => "Press.Right", "middle" => "Press.Middle",
        "locked" => "Press.Locked", "unlocked" => "Press.Unlocked",
        "second click ignored" => "Press.Blocked", "lock released" => "Press.Unlocked", "Windows" => "Press.Windows", "failed" => "Press.Failed", _ => "Press.Ignored",
    });
}

/// <summary>
/// "Settings..." in the tray menu (and a double-click on the tray icon): one window for people who drive the
/// cursor with the chin. Left: the touchpad drawn to scale with its click zones and the touches on it, live.
/// Right: sliders, check boxes, the touchpad choice and the pause key, which apply at once by writing
/// settings.json (sliders 150 ms after the last change; the app's file watcher then reloads it). Large fonts and
/// controls (buttons at least 44 px high at 100 % scaling), DPI-aware, every control reachable with Tab and most
/// with an Alt+letter. Next to every setting a round "?" (0.5.0) explains it: as a tooltip on hover, and in a
/// popup on a click that stays until it is clicked, Esc is pressed or another "?" is clicked (a chin cannot
/// hover precisely). Closing only hides the window. It runs on the UI thread together with the engines, so
/// every handler is short.
/// </summary>
internal class SettingsForm : Form
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    const int RefreshMs = 33;      // ~30 Hz live drawing
    const int WriteDelayMs = 150;  // a slider is written this long after its last change
    const int DevicesMs = 500;     // the "Apply to" list is refreshed twice a second ("reporting now")

    readonly ISettingsHost _host;
    readonly PadView _pad = new();
    readonly Label _padTitle, _live, _where, _status, _hotkeyHint, _hotkeyError;
    readonly Button _pause, _refreshPads;
    readonly SliderRow _speed, _smoothing, _deadzone, _strip, _settle, _merge, _lockHold, _doubleWindow;
    readonly CheckBox _beep, _fingers, _login, _startPaused, _clickLock, _blockDouble;
    readonly RadioButton _position, _landing;
    readonly ComboBox _device, _language;
    readonly List<Action> _localize = [];
    readonly HotkeyBox _hotkey;
    readonly List<HelpButton> _helps = [];
    readonly ToolTip _tips = new() { AutoPopDelay = 30000, InitialDelay = 400, ReshowDelay = 200 };
    readonly System.Windows.Forms.Timer _refresh = new() { Interval = RefreshMs };
    readonly System.Windows.Forms.Timer _debounce = new() { Interval = WriteDelayMs };
    readonly System.Windows.Forms.Timer _devicesTimer = new() { Interval = DevicesMs };
    readonly Icon? _icon;
    AppSettings _pending;     // what the controls show; written when the debounce runs out
    AppSettings? _written;    // the last settings this window wrote (null: none since the last outside change)
    bool _loading;            // controls are being set from settings: their events write nothing
    bool _hotkeySuspended;
    DateTime? _savedAt;
    Exception? _saveError;
    string _liveText = "", _padKey = "", _deviceShape = "";
    int _shownPad = -1;       // TouchpadDevice.Index of the pad drawn
    double _padHeightMm;      // of the pad drawn (strip slider); 0 = none
    int _tab;
    HelpPopup? _popup;        // the "?" text shown after a click, until it is closed

    public SettingsForm(ISettingsHost host, Icon? icon = null)
    {
        _host = host;
        _pending = host.Settings;
        _icon = icon;

        SuspendLayout();
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 11F);
        Text = SettingsText.Title;
        if (icon is not null) Icon = icon;
        StartPosition = FormStartPosition.Manual;
        ClientSize = new Size(1280, 900);
        DoubleBuffered = true;
        MinimumSize = new Size(820, 560);
        ShowInTaskbar = true;

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Padding = new Padding(12), TabIndex = _tab++ };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 39));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 61));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        // ---- Left: the live pad ------------------------------------------------------------------------
        var left = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Margin = new Padding(0, 0, 16, 0), TabIndex = _tab++ };
        left.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _padTitle = Heading(() => PadChoice.Text.Touchpad);
        _padTitle.Anchor = AnchorStyles.Left;
        // The title with the "?" that explains the drawing and its colours.
        var titleRow = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2, RowCount = 1, Dock = DockStyle.Fill, Margin = Padding.Empty };
        titleRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        titleRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 52));
        titleRow.Controls.Add(_padTitle, 0, 0);
        HelpButton legendHelp = NewHelp(HelpTexts.Legend, () => SettingsText.HelpLegend);
        legendHelp.Anchor = AnchorStyles.Right;
        legendHelp.TabIndex = 0;
        titleRow.Controls.Add(legendHelp, 1, 0);
        _pad.Dock = DockStyle.Fill;
        _pad.MinimumSize = new Size(300, 220);
        _pad.Margin = new Padding(0, 4, 0, 4);
        _live = new Label { AutoSize = true, Margin = new Padding(0, 6, 0, 6), UseMnemonic = false };
        _pause = BigButton(() => SettingsText.Pause);
        _pause.MinimumSize = new Size(260, 52);
        _pause.Font = new Font("Segoe UI", 12F, FontStyle.Bold);
        _pause.Click += (_, _) => _host.SetPaused(!_host.Paused, "settings window");
        _where = new Label { AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(0, 8, 0, 0), UseMnemonic = false };
        left.Controls.Add(titleRow, 0, 0);
        left.Controls.Add(_pad, 0, 1);
        left.Controls.Add(_live, 0, 2);
        left.Controls.Add(_pause, 0, 3);
        left.Controls.Add(_where, 0, 4);
        // Tab order of the left column: the Pause button first (as in 0.4.0), then the drawing's "?".
        _pause.TabIndex = 0;
        titleRow.TabIndex = 1;
        _pad.TabIndex = 2;
        left.Resize += (_, _) => FitLabels(left);

        // ---- Right: the settings ----------------------------------------------------------------------
        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, TabIndex = _tab++ };
        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 6,
            Padding = new Padding(0, 0, 8, 0), TabIndex = 0,
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));      // 0 name
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 50));  // 1 minus
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));  // 2 slider
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 50));  // 3 plus
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));      // 4 value
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));      // 5 "?"
        scroll.Controls.Add(grid);

        AddWide(grid, Heading(() => SettingsText.MovingHeading), 6);
        _speed = AddSlider(grid, () => SettingsText.Speed, 4, 120, 1, 2, SettingsText.PxPerMm, "speedPxPerMm");
        _smoothing = AddSlider(grid, () => SettingsText.Smoothing, 0, 18, 1, 20, SettingsText.SmoothingValue, "smoothing");
        _deadzone = AddSlider(grid, () => SettingsText.Deadzone, 0, 30, 1, 10, SettingsText.Mm1, "deadzoneMm");

        AddWide(grid, Heading(() => SettingsText.ClickingHeading), 6);
        _strip = AddSlider(grid, () => SettingsText.Strip, 8, 40, 1, 1, SettingsText.Mm1, "zones");
        var modes = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, Dock = DockStyle.Fill, Margin = new Padding(0, 4, 0, 4) };
        modes.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        modes.Controls.Add(new Label { Text = SettingsText.StripModes, AutoSize = true, Margin = new Padding(3, 4, 3, 2) });
        _position = new WrapRadioButton { Text = SettingsText.Position, MinimumSize = new Size(0, 34), TabIndex = 0 };
        _landing = new WrapRadioButton { Text = SettingsText.Landing, MinimumSize = new Size(0, 34), TabIndex = 1 };
        modes.Controls.Add(_position);
        modes.Controls.Add(_landing);
        _position.AccessibleDescription = _landing.AccessibleDescription = HelpTexts.For("zoneMode");
        int modesRow = AddWide(grid, modes);
        AddHelp(grid, modesRow, "zoneMode", () => SettingsText.StripModesHelp);
        _settle = AddSlider(grid, () => SettingsText.Settle, 0, 40, 5, 1, SettingsText.Ms, "zoneSettleMs");
        _merge = AddSlider(grid, () => SettingsText.Merge, 0, 60, 1, 1, SettingsText.MergeValue, "mergeMm");
        _beep = AddCheck(grid, () => SettingsText.Beep, "clickBeep");
        _clickLock = AddCheck(grid, () => SettingsText.ClickLock, "clickLock");
        _lockHold = AddSlider(grid, () => SettingsText.ClickLockHold, 6, 60, 50, 1, SettingsText.Ms, "clickLock.holdMs", indent: true);
        _blockDouble = AddCheck(grid, () => SettingsText.DoubleClick, "blockDoubleClick");
        _doubleWindow = AddSlider(grid, () => SettingsText.DoubleClickWindow, 2, 30, 50, 1,
            v => v == 0 ? SettingsText.WindowsDoubleClick(InputNative.GetDoubleClickTime()) : SettingsText.Ms(v), "doubleClickWindowMs", indent: true);

        AddWide(grid, Heading(() => SettingsText.GeneralHeading), 6);
        var languageLabel = new Label { AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 10, 12, 3) };
        _language = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, Margin = new Padding(3, 6, 3, 6), TabIndex = _tab++ };
        int languageRow = grid.RowCount++;
        grid.Controls.Add(languageLabel, 0, languageRow);
        grid.Controls.Add(_language, 1, languageRow);
        grid.SetColumnSpan(_language, 4);
        AddHelp(grid, languageRow, "language", () => L10n.T("Language.Label"));
        _language.SelectedIndexChanged += (_, _) =>
        {
            if (_loading || _language.SelectedIndex < 0) return;
            string language = new[] { "auto", "en", "sr-Latn" }[_language.SelectedIndex];
            Change(s => s with { Language = language }, now: true);
            // Apply only after a successful write. The host updates the tray as part of that write.
            if (_written?.Language == language) L10n.Apply(language);
        };
        // "Apply to" with Refresh above the right end of the list; the list gets the whole width, as its entries are long.
        int deviceRow = grid.RowCount++, listRow = grid.RowCount++;
        var deviceLabel = new Label { Text = SettingsText.ApplyTo, AutoSize = true, Anchor = AnchorStyles.Left | AnchorStyles.Bottom, Margin = new Padding(3, 10, 12, 4), TabIndex = _tab++ };
        _device = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, Margin = new Padding(3, 2, 3, 4), TabIndex = _tab++,
            MaxDropDownItems = 10, AccessibleName = SettingsText.ApplyTo.Replace("&", ""), AccessibleDescription = HelpTexts.For("device"),
        };
        _refreshPads = BigButton(() => SettingsText.Refresh);
        _refreshPads.MinimumSize = new Size(100, 46);
        _refreshPads.Margin = new Padding(8, 4, 3, 2);
        _refreshPads.Anchor = AnchorStyles.Right;
        _refreshPads.TabIndex = _tab++;
        grid.Controls.Add(deviceLabel, 0, deviceRow);
        grid.SetColumnSpan(deviceLabel, 4);
        grid.Controls.Add(_refreshPads, 4, deviceRow);
        grid.Controls.Add(_device, 0, listRow);
        grid.SetColumnSpan(_device, 5);
        AddHelp(grid, listRow, "device", () => SettingsText.ApplyTo);
        _fingers = AddCheck(grid, () => SettingsText.Fingers, "strategy");
        _login = AddCheck(grid, () => SettingsText.Login, "startAtLogin");
        _startPaused = AddCheck(grid, () => SettingsText.StartPaused, "startPaused");

        var keyLabel = new Label { Text = SettingsText.PauseKey, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 10, 12, 3), TabIndex = _tab++ };
        _hotkey = new HotkeyBox
        {
            Dock = DockStyle.Fill, ReadOnly = true, BackColor = SystemColors.Window, ShortcutsEnabled = false, TabIndex = _tab++,
            Margin = new Padding(3, 8, 3, 3), AccessibleName = SettingsText.PauseKeyName, AccessibleDescription = HelpTexts.For("hotkey"),
        };
        _hotkeyHint = new Label { AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(3, 0, 3, 0), Text = SettingsText.PauseKeyHint };
        _hotkeyError = new Label { AutoSize = true, ForeColor = Color.Firebrick, Margin = new Padding(3, 0, 3, 6), UseMnemonic = false };
        int keyRow = grid.RowCount++;
        grid.Controls.Add(keyLabel, 0, keyRow);
        grid.Controls.Add(_hotkey, 1, keyRow);
        grid.SetColumnSpan(_hotkey, 3);
        AddHelp(grid, keyRow, "hotkey", () => SettingsText.PauseKeyName);
        AddWide(grid, _hotkeyHint, 6);
        AddWide(grid, _hotkeyError, 6);

        // ---- Bottom: buttons ---------------------------------------------------------------------------
        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true,
            Margin = new Padding(0, 12, 0, 0), TabIndex = _tab++,
        };
        Button bundle = BigButton(() => SettingsText.Bundle);
        bundle.Click += (_, _) => _host.SaveDiagnosticBundle();
        _tips.SetToolTip(bundle, SettingsText.BundleTip);
        Button log = BigButton(() => SettingsText.OpenLog);
        log.Click += (_, _) => _host.OpenFile(_host.Paths.LogFile);
        Button file = BigButton(() => SettingsText.OpenSettings);
        file.Click += (_, _) => { Flush(); _host.OpenFile(_host.Paths.SettingsFile); };
        Button restore = BigButton(() => SettingsText.Restore);
        restore.Click += (_, _) => RestoreDefaults();
        Button wizard = BigButton(() => SettingsText.Wizard);
        wizard.Click += (_, _) => { Flush(); Hide(); _host.ShowWizard(); };
        // Keep the same button-bar layout when opening setup.
        var wizardHolder = new Panel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = Padding.Empty, TabStop = false };
        wizardHolder.Controls.Add(wizard);
        _tips.SetToolTip(wizardHolder, SettingsText.WizardTip);
        _tips.SetToolTip(wizard, SettingsText.WizardTip);
        wizard.AccessibleDescription = SettingsText.WizardTip;
        Button close = BigButton(() => SettingsText.Close);
        close.Click += (_, _) => Hide();
        _status = new Label { AutoSize = true, Margin = new Padding(12, 14, 3, 3), UseMnemonic = false };
        foreach (Control c in new Control[] { bundle, log, file, restore, wizardHolder, close, _status })
        {
            c.TabIndex = _tab++;
            bar.Controls.Add(c);
        }
        CancelButton = close;

        root.Controls.Add(left, 0, 0);
        root.Controls.Add(scroll, 1, 0);
        root.Controls.Add(bar, 0, 1);
        root.SetColumnSpan(bar, 2);
        Controls.Add(root);

        BindText(languageLabel, () => L10n.T("Language.Label"));
        BindText(modes.Controls[0], () => SettingsText.StripModes);
        BindText(_position, () => SettingsText.Position);
        BindText(_landing, () => SettingsText.Landing);
        BindText(deviceLabel, () => SettingsText.ApplyTo);
        BindText(keyLabel, () => SettingsText.PauseKey);
        _localize.Add(() =>
        {
            _device.AccessibleName = SettingsText.ApplyTo.Replace("&", "");
            _device.AccessibleDescription = HelpTexts.For("device");
            _hotkey.AccessibleName = SettingsText.PauseKeyName;
            _hotkey.AccessibleDescription = HelpTexts.For("hotkey");
            _position.AccessibleDescription = _landing.AccessibleDescription = HelpTexts.For("zoneMode");
            _language.AccessibleName = L10n.T("Language.Label");
            _language.AccessibleDescription = HelpTexts.For("language");
            _pad.AccessibleName = SettingsText.DrawingName;
            _hotkeyHint.Text = _hotkeySuspended ? SettingsText.PauseKeyRecording : SettingsText.PauseKeyHint;
            _tips.SetToolTip(bundle, SettingsText.BundleTip);
            _tips.SetToolTip(wizardHolder, SettingsText.WizardTip);
            _tips.SetToolTip(wizard, SettingsText.WizardTip);
            wizard.AccessibleDescription = SettingsText.WizardTip;
            _language.Items.Clear();
            _language.Items.AddRange([L10n.T("Language.Auto"), L10n.T("Language.English"), L10n.T("Language.Serbian")]);
        });
        // Bound widths allow all prose to wrap without forcing the slider's column off screen.
        grid.Layout += (_, _) =>
        {
            int width = Math.Max(100, grid.ClientSize.Width - grid.Padding.Horizontal);
            int caption = Math.Max(95, (int)(width * 0.29));
            foreach (SliderRow row in new[] { _speed, _smoothing, _deadzone, _strip, _settle, _merge, _lockHold, _doubleWindow })
                row.Name.MaximumSize = new Size(Math.Max(70, caption - row.Name.Margin.Horizontal), 0);
            _hotkeyHint.MaximumSize = _hotkeyError.MaximumSize = new Size(Math.Max(100, width - LogicalToDeviceUnits(12)), 0);
        };
        L10n.Changed += Relabel;
        Relabel();

        // ---- Behaviour -------------------------------------------------------------------------------
        _speed.Changed += v => Change(s => s with { SpeedPxPerMm = v });
        _smoothing.Changed += v => Change(s => s with { Smoothing = v });
        _deadzone.Changed += v => Change(s => s with { DeadzoneMm = v });
        _strip.Changed += mm => Change(s => StripLayout.IsStandard(s.Zones, out _) && _padHeightMm > 0
            ? s with { Zones = StripLayout.WithTop(s.Zones, StripLayout.TopFor(mm, _padHeightMm)) }
            : s);
        _settle.Changed += v => Change(s => s with { ZoneSettleMs = v });
        _merge.Changed += v => Change(s => s with { MergeMm = v });
        _beep.CheckedChanged += (_, _) => Change(s => s with { ClickBeep = _beep.Checked }, now: true);
        _clickLock.CheckedChanged += (_, _) => Change(s => s with { ClickLock = s.ClickLock with { Enabled = _clickLock.Checked } }, now: true);
        _lockHold.Changed += v => Change(s => s with { ClickLock = s.ClickLock with { HoldMs = v } });
        _blockDouble.CheckedChanged += (_, _) => Change(s => s with { BlockDoubleClick = _blockDouble.Checked }, now: true);
        _doubleWindow.Changed += v => Change(s => s with { DoubleClickWindowMs = v });
        _fingers.CheckedChanged += (_, _) => Change(s => s.Strategy == 1 ? s : s with { Strategy = _fingers.Checked ? 2 : 3 }, now: true);
        _startPaused.CheckedChanged += (_, _) => Change(s => s with { StartPaused = _startPaused.Checked }, now: true);
        _position.CheckedChanged += (_, _) => { if (_position.Checked) Change(s => s with { ZoneMode = "position" }, now: true); };
        _landing.CheckedChanged += (_, _) => { if (_landing.Checked) Change(s => s with { ZoneMode = "landing" }, now: true); };
        _login.CheckedChanged += (_, _) => SetStartAtLogin(_login.Checked);
        // The list: written when the choice is made (not while the open list is browsed with the arrow keys).
        _device.SelectionChangeCommitted += (_, _) => { if (!_device.DroppedDown) CommitDevice(); };
        _device.DropDownClosed += (_, _) => CommitDevice();
        _refreshPads.Click += (_, _) =>
        {
            _host.RefreshTouchpads();
            LoadDevices(rebuild: true);
        };
        _hotkey.Captured += OnHotkeyCaptured;
        _hotkey.Rejected += why => _hotkeyError.Text = why;
        _hotkey.GotFocus += (_, _) => SuspendHotkey(true);
        _hotkey.LostFocus += (_, _) => SuspendHotkey(false);
        _debounce.Tick += (_, _) => Flush();
        _refresh.Tick += (_, _) => RefreshLive();
        _devicesTimer.Tick += (_, _) => LoadDevices();
        _host.StateChanged += OnHostStateChanged;

        ResumeLayout(false);
        PerformLayout();
    }

    // ------------------------------------------------------------------------------------------------
    // Showing and hiding
    // ------------------------------------------------------------------------------------------------

    /// <summary>Shows the window (or brings it back to the front) with the settings in use.</summary>
    public void ShowAndActivate()
    {
        if (!Visible)
        {
            Show();
        }
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate();
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        // On the screen where the cursor is, no bigger than its working area.
        Rectangle wa = Screen.FromPoint(Cursor.Position).WorkingArea;
        Size = new Size(Math.Min(Width, wa.Width * 95 / 100), Math.Min(Height, wa.Height * 95 / 100));
        Location = new Point(wa.X + (wa.Width - Width) / 2, wa.Y + (wa.Height - Height) / 2);
    }

    /// <summary>Moved to a screen with other scaling: Windows rescales the window; keep it inside that screen's working area.</summary>
    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        HideHelp();
        base.OnDpiChanged(e);
        BeginInvoke(FitToScreen);
    }

    void FitToScreen()
    {
        if (IsDisposed || WindowState != FormWindowState.Normal) return;
        Rectangle wa = Screen.FromControl(this).WorkingArea;
        int w = Math.Min(Width, wa.Width), h = Math.Min(Height, wa.Height);
        int x = Math.Clamp(Left, wa.Left, wa.Right - w), y = Math.Clamp(Top, wa.Top, wa.Bottom - h);
        if (w != Width || h != Height || x != Left || y != Top) Bounds = new Rectangle(x, y, w, h);
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (Visible)
        {
            _pending = _host.Settings;
            _written = null;
            _status.Text = "";
            _savedAt = null;
            _saveError = null;
            _hotkeyError.Text = _host.HotkeyProblem;
            LoadControls(_pending);
            UpdatePauseButton();
            RefreshLive();
            _refresh.Start();
            _devicesTimer.Start();
        }
        else
        {
            _refresh.Stop();
            _devicesTimer.Stop();
            HideHelp();
            Flush();
            SuspendHotkey(false);
        }
    }

    /// <summary>The close box hides the window; ChinPad keeps running. Exiting ChinPad closes it for real.</summary>
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
        }
        else
        {
            _refresh.Stop();
            _devicesTimer.Stop();
            Flush();
            SuspendHotkey(false);
        }
    }

    /// <summary>Esc closes an open "?" popup first (and only that); the next Esc closes the window as before.</summary>
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape && _popup is not null && !_device.DroppedDown)
        {
            HideHelp();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (_popup is not null) PlaceHelp(_popup);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            L10n.Changed -= Relabel;
            _host.StateChanged -= OnHostStateChanged;
            _refresh.Dispose();
            _debounce.Dispose();
            _devicesTimer.Dispose();
            _tips.Dispose();
            _icon?.Dispose();
        }
        base.Dispose(disposing);
    }

    // ------------------------------------------------------------------------------------------------
    // Settings <-> controls
    // ------------------------------------------------------------------------------------------------

    void LoadControls(AppSettings s)
    {
        bool wasLoading = _loading;
        _loading = true;
        try
        {
            _language.SelectedIndex = s.Language switch { "en" => 1, "sr-Latn" => 2, _ => 0 };
            _speed.Set(s.SpeedPxPerMm);
            _smoothing.Set(s.Smoothing);
            _deadzone.Set(s.DeadzoneMm);
            _settle.Set(s.ZoneSettleMs);
            _merge.Set(s.MergeMm);
            LoadStrip(s);
            _position.Checked = s.PositionZones;
            _landing.Checked = !s.PositionZones;
            _beep.Checked = s.ClickBeep;
            _clickLock.Checked = s.ClickLock.Enabled;
            _lockHold.Set(s.ClickLock.HoldMs);
            _blockDouble.Checked = s.BlockDoubleClick;
            _doubleWindow.Set(s.DoubleClickWindowMs);
            if (s.Strategy == 1)
            {
                _fingers.Checked = false;
                _fingers.Enabled = false;
                _fingers.Text = SettingsText.FingersExpert;
            }
            else
            {
                _fingers.Enabled = true;
                _fingers.Text = SettingsText.Fingers;
                _fingers.Checked = s.Strategy == 2;
            }
            _startPaused.Checked = s.StartPaused;
            _login.Checked = _host.StartAtLoginEnabled;
            if (!_hotkeySuspended) _hotkey.Text = HotkeyText(s.Hotkey);
            _where.Text = SettingsText.SettingsFile(_host.Paths.SettingsFile, _host.Paths.Portable);
        }
        finally
        {
            _loading = wasLoading;
        }
        LoadDevices();
    }

    /// <summary>The strip slider works for the standard two-zone layout on a known pad; otherwise it is disabled with a note.</summary>
    void LoadStrip(AppSettings s)
    {
        bool was = _loading;
        _loading = true;
        try
        {
            if (_padHeightMm <= 0) _strip.Disable(SettingsText.NoPadInUse);
            else if (!StripLayout.IsStandard(s.Zones, out double top)) _strip.Disable(SettingsText.CustomLayout);
            else _strip.Set(StripLayout.HeightMm(top, _padHeightMm));
        }
        finally
        {
            _loading = was;
        }
    }

    T BindText<T>(T control, Func<string> text) where T : Control
    {
        control.Text = text();
        _localize.Add(() => control.Text = text());
        return control;
    }

    void Relabel()
    {
        if (IsDisposed) return;
        string? helpKey = _popup?.Source.Key;
        HideHelp();
        SuspendLayout();
        bool loading = _loading;
        _loading = true;
        try
        {
            Text = SettingsText.Title;
            foreach (Action update in _localize) update();
            _padKey = "\0";
            _liveText = "";
            _hotkeyError.Text = _host.HotkeyProblem;
            LoadControls(_pending);
            UpdatePauseButton();
            LoadDevices(rebuild: true);
            RefreshLive();
            if (_saveError is not null) _status.Text = SettingsText.SaveFailed(L10n.Error(_saveError));
            else if (_savedAt is DateTime saved) _status.Text = SettingsText.Saved(saved);
        }
        finally
        {
            _loading = loading;
            ResumeLayout(true);
        }
        _pad.Invalidate();
        if (helpKey is not null && _helps.Find(h => h.Key == helpKey) is HelpButton help) ShowHelp(help);
    }

    static string HotkeyText(string hotkey) => hotkey.Length == 0 ? SettingsText.NoKey : hotkey;

    /// <summary>A control changed: update the pending settings and write them now or after the debounce.</summary>
    void Change(Func<AppSettings, AppSettings> edit, bool now = false)
    {
        if (_loading) return;
        _pending = edit(_pending);
        _debounce.Stop();
        if (now) Flush();
        else _debounce.Start();
    }

    /// <summary>Writes the pending settings if they differ from what is in the file (as far as this window knows).</summary>
    void Flush()
    {
        _debounce.Stop();
        if (_pending == (_written ?? _host.Settings)) return;
        try
        {
            _host.WriteSettings(_pending, "settings window");
            _written = _pending;
            _status.ForeColor = SystemColors.ControlText;
            _savedAt = DateTime.Now;
            _saveError = null;
            _status.Text = SettingsText.Saved(_savedAt.Value);
        }
        catch (Exception ex)
        {
            _status.ForeColor = Color.Firebrick;
            _saveError = ex;
            _status.Text = SettingsText.SaveFailed(L10n.Error(ex));
        }
    }

    /// <summary>The app reloaded settings, paused or resumed, the pause key or the touchpads found changed.</summary>
    void OnHostStateChanged()
    {
        if (!Visible || IsDisposed) return;
        UpdatePauseButton();
        if (!_hotkeySuspended) _hotkeyError.Text = _host.HotkeyProblem;
        LoadDevices();
        if (_debounce.Enabled) return;                       // this window has a newer value waiting
        AppSettings s = _host.Settings;
        if (_written is not null && s == _written) return;   // our own write came back
        if (s == _pending) return;
        _pending = s;                                        // changed outside (settings.json edited, tray menu)
        _written = null;
        LoadControls(s);
    }

    void UpdatePauseButton()
    {
        bool paused = _host.Paused;
        string key = _host.Settings.Hotkey.Length > 0 && _host.HotkeyProblem.Length == 0 ? $" ({_host.Settings.Hotkey})" : "";
        _pause.Text = (paused ? SettingsText.Resume : SettingsText.Pause) + key;
        _pause.BackColor = paused ? Color.FromArgb(0xFF, 0xF4, 0xCE) : SystemColors.Control;
        _pause.UseVisualStyleBackColor = !paused;
    }

    void SetStartAtLogin(bool on)
    {
        if (_loading) return;
        _host.SetStartAtLogin(on);
        _loading = true;
        try { _login.Checked = _host.StartAtLoginEnabled; }
        finally { _loading = false; }
        if (_login.Checked != on)
        {
            _status.ForeColor = Color.Firebrick;
            _status.Text = SettingsText.StartAtLoginFailed;
        }
    }

    void SuspendHotkey(bool on)
    {
        if (on == _hotkeySuspended) return;
        _hotkeySuspended = on;
        _host.SuspendHotkey(on);
        _hotkeyHint.Text = on ? SettingsText.PauseKeyRecording : SettingsText.PauseKeyHint;
        if (!on)
        {
            _hotkey.Text = HotkeyText(_pending.Hotkey);
            _hotkeyError.Text = _host.HotkeyProblem;
        }
    }

    void OnHotkeyCaptured(string text)
    {
        _hotkey.Text = HotkeyText(text);
        _hotkeyError.Text = "";
        Change(s => s with { Hotkey = text }, now: true);
    }

    void RestoreDefaults()
    {
        DialogResult answer = AppDialog.Show(this, SettingsText.RestoreQuestion, SettingsText.RestoreTitle, confirm: true);
        if (answer != DialogResult.Yes) return;
        string file = _host.Paths.SettingsFile;
        try
        {
            if (File.Exists(file)) File.Copy(file, Path.Combine(Path.GetDirectoryName(file)!, "settings.before-defaults.json"), overwrite: true);
        }
        catch (Exception)
        {
            // the copy is a courtesy; restoring goes on
        }
        _pending = new AppSettings();
        LoadControls(_pending);
        _written = null;
        Flush();
    }

    // ------------------------------------------------------------------------------------------------
    // "Apply to": which touchpad
    // ------------------------------------------------------------------------------------------------

    /// <summary>An "Apply to" entry: the text shown and the settings "device" value it stands for.</summary>
    sealed class DeviceItem(string text, string device)
    {
        public string Text = text;
        public readonly string Device = device;
        public override string ToString() => Text;
    }

    /// <summary>
    /// Fills the list: "All touchpads", then every touchpad found ("..., reporting now" while it reports), plus an
    /// entry for a "device" value that names no single pad. When the entries stay the same, only their texts are
    /// updated, and the entry shown (even one being browsed in the open list) stays where it is.
    /// </summary>
    void LoadDevices(bool rebuild = false)
    {
        IReadOnlyList<TouchpadChoice> pads = _host.Touchpads();
        string device = _pending.Device;
        var wanted = new List<DeviceItem> { new(SettingsText.AllTouchpads, "") };
        foreach (TouchpadChoice p in pads) wanted.Add(new(p.Reporting ? p.Label + PadChoice.Text.ReportingNow : p.Label, p.Device));
        int selected = 0;
        if (device.Length > 0)
        {
            int exact = -1, containing = -1, matches = 0;
            for (int i = 0; i < pads.Count; i++)
            {
                if (exact < 0 && string.Equals(pads[i].Device, device, StringComparison.OrdinalIgnoreCase)) exact = i;
                if (pads[i].Path.Contains(device, StringComparison.OrdinalIgnoreCase))
                {
                    matches++;
                    containing = i;
                }
            }
            if (exact >= 0) selected = exact + 1;
            else if (matches == 1) selected = containing + 1;
            else
            {
                wanted.Add(new(matches == 0 ? SettingsText.DeviceMissing(device) : SettingsText.DeviceCustom(device, matches), device));
                selected = wanted.Count - 1;
            }
        }
        string shape = string.Join("\n", wanted.Select(w => w.Device)) + "\n#" + selected.ToString(Inv);
        bool was = _loading;
        _loading = true;
        try
        {
            if (rebuild || shape != _deviceShape || _device.Items.Count != wanted.Count)
            {
                _deviceShape = shape;
                _device.BeginUpdate();
                _device.Items.Clear();
                foreach (DeviceItem w in wanted) _device.Items.Add(w);
                _device.SelectedIndex = selected;
                _device.EndUpdate();
                int widest = wanted.Max(w => TextRenderer.MeasureText(w.Text, _device.Font).Width) + _device.LogicalToDeviceUnits(40);
                _device.DropDownWidth = Math.Max(_device.Width, widest);
            }
            else
            {
                int keep = _device.SelectedIndex;
                for (int i = 0; i < wanted.Count; i++)
                {
                    if (_device.Items[i] is DeviceItem item && item.Text != wanted[i].Text)
                    {
                        item.Text = wanted[i].Text;
                        _device.Items[i] = item; // the combo box redraws the entry; the selection is kept
                    }
                }
                if (_device.SelectedIndex != keep) _device.SelectedIndex = keep;
            }
        }
        finally
        {
            _loading = was;
        }
    }

    /// <summary>The entry shown was chosen: write its "device" value (the app then uses that touchpad).</summary>
    void CommitDevice()
    {
        if (_loading || _device.SelectedItem is not DeviceItem item) return;
        if (string.Equals(item.Device, _pending.Device, StringComparison.Ordinal)) return;
        Change(s => s with { Device = item.Device }, now: true);
        LoadDevices();
    }

    // ------------------------------------------------------------------------------------------------
    // "?" help
    // ------------------------------------------------------------------------------------------------

    HelpButton NewHelp(string key, Func<string> settingName)
    {
        var b = new HelpButton(key, SettingsText.HelpFor(settingName()));
        b.Click += (_, _) => ShowHelp(b);
        _tips.SetToolTip(b, b.HelpText);
        _helps.Add(b);
        _localize.Add(() =>
        {
            b.AccessibleName = SettingsText.HelpFor(settingName());
            b.AccessibleDescription = b.HelpText;
            _tips.SetToolTip(b, b.HelpText);
        });
        return b;
    }

    void AddHelp(TableLayoutPanel grid, int row, string key, Func<string> settingName)
    {
        HelpButton b = NewHelp(key, settingName);
        b.TabIndex = _tab++;
        grid.Controls.Add(b, 5, row);
    }

    /// <summary>A click on a "?": its text in a popup next to it, closing the one open before; the same "?" again closes it.</summary>
    void ShowHelp(HelpButton source)
    {
        bool same = _popup?.Source == source;
        HideHelp();
        if (same) return;
        _tips.Hide(source);
        var popup = new HelpPopup(source, SettingsText.HelpClose) { Font = Font };
        popup.CloseRequested += HideHelp;
        Controls.Add(popup);
        popup.BringToFront();
        _popup = popup;
        PlaceHelp(popup);
    }

    void HideHelp()
    {
        if (_popup is null) return;
        HelpPopup p = _popup;
        _popup = null;
        Controls.Remove(p);
        p.Dispose();
    }

    /// <summary>Under its "?" (above it when there is no room), right-aligned with it, inside the window.</summary>
    void PlaceHelp(HelpPopup p)
    {
        Size size = p.Measure(ClientSize.Width - LogicalToDeviceUnits(16));
        Rectangle b = RectangleToClient(p.Source.RectangleToScreen(p.Source.ClientRectangle));
        int gap = LogicalToDeviceUnits(4), edge = LogicalToDeviceUnits(4);
        int x = Math.Clamp(b.Right - size.Width, edge, Math.Max(edge, ClientSize.Width - size.Width - edge));
        int y = b.Bottom + gap;
        if (y + size.Height > ClientSize.Height - edge) y = b.Top - gap - size.Height;
        y = Math.Clamp(y, edge, Math.Max(edge, ClientSize.Height - size.Height - edge));
        var bounds = new Rectangle(x, y, size.Width, size.Height);
        if (p.Bounds != bounds) p.Bounds = bounds;
    }

    // ------------------------------------------------------------------------------------------------
    // Live view
    // ------------------------------------------------------------------------------------------------

    void RefreshLive()
    {
        LiveState live = _host.Live();
        PadSnapshot? pad = PickPad(live.Pads);
        string key = pad is null ? "" : string.Create(Inv, $"{pad.Index}|{pad.Geometry.WidthMm}|{pad.Geometry.HeightMm}|{pad.Label}");
        if (key != _padKey)
        {
            _padKey = key;
            _shownPad = pad?.Index ?? -1;
            _padHeightMm = pad?.Geometry.HeightMm ?? 0;
            _padTitle.Text = pad is null ? SettingsText.PadNone
                : pad.Label.Length > 0 ? pad.Label
                : SettingsText.PadTitle(pad.Index, pad.Name, pad.Geometry.WidthMm, pad.Geometry.HeightMm);
            LoadStrip(_pending);
        }
        _pad.Show(pad, _pending.Zones, live.Paused);

        long clicks = live.LeftClicks + live.RightClicks + live.MiddleClicks;
        string detail = clicks == 0 ? "" : SettingsText.ClickDetail(live.LeftClicks, live.RightClicks, live.MiddleClicks);
        string text = SettingsText.Live(TouchText(pad), clicks, detail, live.WindowsClicksDropped);
        if (live.Paused) text = SettingsText.Paused + "\r\n" + text;
        if (live.Locked is MouseButton locked) text += "\r\n" + SettingsText.Locked(locked);
        if (live.Problem is not null) text += "\r\n" + SettingsText.Problem(live.Problem);
        if (text != _liveText)
        {
            _liveText = text;
            _live.Text = text;
        }
        if (_popup is not null) PlaceHelp(_popup); // follows its "?" when the settings are scrolled
    }

    /// <summary>The pad drawn: the one touched last; before any touch, the one with the most reports; else the first.</summary>
    PadSnapshot? PickPad(IReadOnlyList<PadSnapshot> pads)
    {
        if (pads.Count == 0) return null;
        PadSnapshot? shown = pads.FirstOrDefault(p => p.Index == _shownPad);
        PadSnapshot latest = pads.OrderByDescending(p => p.LastContactMs).ThenByDescending(p => p.Reports).ThenBy(p => p.Index).First();
        if (shown is null) return latest;
        return latest.LastContactMs > shown.LastContactMs ? latest : shown;
    }

    static string TouchText(PadSnapshot? pad)
    {
        if (pad is null || pad.Contacts.Count == 0) return SettingsText.TouchNone;
        foreach (ContactDot d in pad.Contacts)
            if (d.State == DotState.Zone)
                return d.Zone is ZoneAction.None or null ? SettingsText.TouchDead : SettingsText.TouchZone(d.Zone.Value);
        if (pad.Contacts.Any(d => d.State == DotState.Moving)) return SettingsText.TouchMoving;
        if (pad.Contacts.Any(d => d.State == DotState.Move)) return SettingsText.TouchMove;
        if (pad.Contacts.Any(d => d.State == DotState.Undecided)) return SettingsText.TouchLanded;
        return SettingsText.TouchFinger;
    }

    // ------------------------------------------------------------------------------------------------
    // Building blocks
    // ------------------------------------------------------------------------------------------------

    Label Heading(Func<string> text) => BindText(new Label()
    {
        AutoSize = true,
        Font = new Font("Segoe UI", 12.5F, FontStyle.Bold),
        Margin = new Padding(0, 8, 3, 2),
        UseMnemonic = false,
    }, text);

    Button BigButton(Func<string> text) => BindText(new Button()
    {
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        MinimumSize = new Size(120, 46),
        Padding = new Padding(10, 2, 10, 2),
        Margin = new Padding(0, 0, 10, 8),
        UseVisualStyleBackColor = true,
    }, text);

    /// <summary>A control over the first <paramref name="span"/> columns of a new row (5: the "?" column stays free); returns the row.</summary>
    int AddWide(TableLayoutPanel grid, Control c, int span = 5)
    {
        int row = grid.RowCount++;
        c.TabIndex = _tab++;
        grid.Controls.Add(c, 0, row);
        grid.SetColumnSpan(c, span);
        return row;
    }

    CheckBox AddCheck(TableLayoutPanel grid, Func<string> text, string helpKey)
    {
        var c = BindText(new WrapCheckBox { MinimumSize = new Size(0, 36), Margin = new Padding(3, 1, 3, 1), AccessibleDescription = HelpTexts.For(helpKey) }, text);
        _localize.Add(() => c.AccessibleDescription = HelpTexts.For(helpKey));
        int row = AddWide(grid, c);
        AddHelp(grid, row, helpKey, text);
        return c;
    }

    SliderRow AddSlider(TableLayoutPanel grid, Func<string> name, int minTicks, int maxTicks, int num, int den, Func<double, string> format, string helpKey, bool indent = false)
    {
        var row = new SliderRow(name(), minTicks, maxTicks, num, den, format);
        if (indent) row.Name.Margin = new Padding(32, 3, 12, 3); // belongs to the check box above
        int r = grid.RowCount++;
        row.Name.TabIndex = _tab++;
        row.Bar.TabIndex = _tab++;
        grid.Controls.Add(row.Name, 0, r);
        grid.Controls.Add(row.Minus, 1, r);
        grid.Controls.Add(row.Bar, 2, r);
        grid.Controls.Add(row.Plus, 3, r);
        grid.Controls.Add(row.Value, 4, r);
        _localize.Add(() =>
        {
            row.Name.Text = name();
            row.Bar.AccessibleName = name().Replace("&", "");
            row.Bar.AccessibleDescription = HelpTexts.For(helpKey);
            row.Minus.AccessibleName = SettingsText.LessButton;
            row.Plus.AccessibleName = SettingsText.MoreButton;
        });
        AddHelp(grid, r, helpKey, name);
        return row;
    }

    /// <summary>Lets the left column's labels wrap at its width.</summary>
    void FitLabels(Control column)
    {
        int w = Math.Max(100, column.ClientSize.Width - 8);
        _live.MaximumSize = new Size(w, 0);
        _where.MaximumSize = new Size(w, 0);
        _padTitle.MaximumSize = new Size(Math.Max(60, w - LogicalToDeviceUnits(64)), 0); // room for the "?"
    }

    /// <summary>
    /// One slider: name label (with its Alt+letter), "-" button, track bar, "+" button, value label. The track
    /// bar works in whole ticks; value = tick * num / den, so 0.35 is exactly 7 / 20 and not 7 * 0.05.
    /// The buttons are not Tab stops: the arrow keys move the track bar.
    /// </summary>
    sealed class SliderRow
    {
        public readonly Label Name, Value;
        public readonly Button Minus, Plus;
        public readonly TrackBar Bar;
        readonly int _minTicks, _num, _den;
        readonly Func<double, string> _format;
        bool _setting;

        /// <summary>The user moved the slider (keyboard, mouse or the buttons); the argument is the new value.</summary>
        public event Action<double>? Changed;

        public SliderRow(string name, int minTicks, int maxTicks, int num, int den, Func<double, string> format)
        {
            _minTicks = minTicks;
            _num = num;
            _den = den;
            _format = format;
            Name = new Label { Text = name, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 3, 12, 3) };
            int range = maxTicks - minTicks;
            Bar = new TrackBar
            {
                Minimum = 0, Maximum = range, SmallChange = 1, LargeChange = Math.Max(1, range / 10),
                TickStyle = TickStyle.BottomRight, TickFrequency = Math.Max(1, range / 10),
                AutoSize = false, Height = 46, Dock = DockStyle.Fill, Margin = new Padding(0, 2, 0, 2),
                AccessibleName = name.Replace("&", ""),
            };
            Minus = StepButton("-", SettingsText.LessButton);
            Plus = StepButton("+", SettingsText.MoreButton);
            Value = new Label { AutoSize = true, Anchor = AnchorStyles.Left, MinimumSize = new Size(96, 0), MaximumSize = new Size(190, 0), Margin = new Padding(8, 3, 3, 3), UseMnemonic = false };
            Bar.ValueChanged += (_, _) =>
            {
                if (_setting) return;
                double v = ValueAt(Bar.Value);
                Value.Text = _format(v);
                Changed?.Invoke(v);
            };
            Minus.Click += (_, _) => { if (Bar.Enabled) Bar.Value = Math.Max(Bar.Minimum, Bar.Value - 1); };
            Plus.Click += (_, _) => { if (Bar.Enabled) Bar.Value = Math.Min(Bar.Maximum, Bar.Value + 1); };
        }

        static Button StepButton(string text, string accessible) => new()
        {
            Text = text,
            Size = new Size(46, 46),
            Font = new Font("Segoe UI", 14F, FontStyle.Bold),
            TabStop = false,
            Margin = new Padding(2, 1, 2, 1),
            AccessibleName = accessible,
            UseVisualStyleBackColor = true,
        };

        double ValueAt(int tick) => (double)(_minTicks + tick) * _num / _den;

        /// <summary>Shows a value from the settings (nearest tick; the label shows the exact value) without raising Changed.</summary>
        public void Set(double v)
        {
            _setting = true;
            try
            {
                int tick = (int)Math.Round(v * _den / _num) - _minTicks;
                Bar.Value = Math.Clamp(tick, Bar.Minimum, Bar.Maximum);
                Value.Text = _format(v);
                Enable(true);
            }
            finally
            {
                _setting = false;
            }
        }

        public void Disable(string note)
        {
            Value.Text = note;
            Enable(false);
        }

        void Enable(bool on) => Bar.Enabled = Minus.Enabled = Plus.Enabled = on;
    }

    /// <summary>
    /// A check box whose text wraps onto further lines when its columns are too narrow for it (a narrow window,
    /// large scaling, and later longer translated texts) instead of being cut off: it takes the width of its
    /// cells (anchored left and right) and makes itself as high as the wrapped text needs.
    /// </summary>
    sealed class WrapCheckBox : CheckBox
    {
        public WrapCheckBox()
        {
            AutoSize = false;
            Anchor = AnchorStyles.Left | AnchorStyles.Right;
            Width = 100; // the cells decide the width; a small start does not widen the columns
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            FitHeight();
        }

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            FitHeight();
        }

        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            FitHeight();
        }

        void FitHeight()
        {
            int textWidth = Math.Max(20, Width - LogicalToDeviceUnits(28) - Padding.Horizontal); // the box and its gap
            Size t = TextRenderer.MeasureText(Text, Font, new Size(textWidth, int.MaxValue), TextFormatFlags.WordBreak);
            int h = Math.Max(MinimumSize.Height, t.Height + LogicalToDeviceUnits(10));
            if (Height != h) Height = h;
        }
    }

    sealed class WrapRadioButton : RadioButton
    {
        public WrapRadioButton()
        {
            AutoSize = false;
            Anchor = AnchorStyles.Left | AnchorStyles.Right;
            Width = 100; // the cells decide the width; a small start does not widen the columns
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            FitHeight();
        }

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            FitHeight();
        }

        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            FitHeight();
        }

        void FitHeight()
        {
            int textWidth = Math.Max(20, Width - LogicalToDeviceUnits(28) - Padding.Horizontal); // the box and its gap
            Size t = TextRenderer.MeasureText(Text, Font, new Size(textWidth, int.MaxValue), TextFormatFlags.WordBreak);
            int h = Math.Max(MinimumSize.Height, t.Height + LogicalToDeviceUnits(10));
            if (Height != h) Height = h;
        }
    }


    /// <summary>
    /// The pause key box: click it and press a key combination; it reports the combination as text
    /// ("Ctrl+Alt+P") when HotkeySpec accepts it, or why not. Backspace or Delete = no key. Tab, Shift+Tab,
    /// Esc and Enter keep their usual meaning so the window stays usable from the keyboard.
    /// </summary>
    sealed class HotkeyBox : TextBox
    {
        public event Action<string>? Captured;
        public event Action<string>? Rejected;

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            Keys key = keyData & Keys.KeyCode, mods = keyData & Keys.Modifiers;
            if ((mods == Keys.None && key is Keys.Tab or Keys.Escape or Keys.Enter) || (mods == Keys.Shift && key == Keys.Tab))
                return base.ProcessCmdKey(ref msg, keyData);
            if (key is Keys.ShiftKey or Keys.ControlKey or Keys.Menu or Keys.LWin or Keys.RWin or Keys.None)
                return true; // a modifier alone: wait for the key
            if (mods == Keys.None && key is Keys.Back or Keys.Delete)
            {
                Captured?.Invoke("");
                return true;
            }
            var parts = new List<string>();
            if ((mods & Keys.Control) != 0) parts.Add("Ctrl");
            if ((mods & Keys.Alt) != 0) parts.Add("Alt");
            if ((mods & Keys.Shift) != 0) parts.Add("Shift");
            parts.Add(key is >= Keys.D0 and <= Keys.D9 ? ((char)('0' + (key - Keys.D0))).ToString() : key.ToString());
            string text = string.Join("+", parts);
            if (HotkeySpec.TryParse(text, out HotkeySpec spec, out string why)) Captured?.Invoke(spec.ToString());
            else Rejected?.Invoke(L10n.HotkeyRejection(text, why));
            return true;
        }
    }
}

/// <summary>
/// A round "?" next to a setting (0.5.0): 36 px at 100 % scaling, a Tab stop (Space or Enter opens it) without an
/// Alt+letter. Hovering shows its text as a tooltip; a click shows it in a HelpPopup. Key = the settings.json
/// name of the setting it explains (HelpTexts).
/// </summary>
internal sealed class HelpButton : Button
{
    bool _hover, _pressed;

    public string Key { get; }
    public string HelpText => HelpTexts.For(Key);

    public HelpButton(string key, string accessibleName)
    {
        Key = key;
        Text = "?";
        UseMnemonic = false;
        TabStop = true;
        Size = new Size(36, 36);
        MinimumSize = new Size(36, 36);
        Margin = new Padding(8, 3, 3, 3);
        Anchor = AnchorStyles.Left;
        Font = new Font("Segoe UI", 12F, FontStyle.Bold);
        AccessibleName = accessibleName;
        AccessibleDescription = HelpText;
        Cursor = Cursors.Hand;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = _pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { _pressed = true; Invalidate(); } base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.Clear(Parent?.BackColor ?? SystemColors.Control);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float s = DeviceDpi / 96f;
        float inset = 2f * s;
        var r = new RectangleF(inset, inset, Width - 2 * inset - 1, Height - 2 * inset - 1);
        Color edge = Color.FromArgb(0x2F, 0x6B, 0xD0);
        Color fill = _pressed ? Color.FromArgb(0xB5, 0xCE, 0xF5) : _hover ? Color.FromArgb(0xD3, 0xE3, 0xFB) : Color.FromArgb(0xEE, 0xF4, 0xFD);
        using (var brush = new SolidBrush(fill)) g.FillEllipse(brush, r);
        using (var pen = new Pen(edge, (Focused ? 2.5f : 1.5f) * s)) g.DrawEllipse(pen, r);
        if (Focused && ShowFocusCues)
        {
            using var dots = new Pen(edge, 1f * s) { DashStyle = DashStyle.Dot };
            g.DrawEllipse(dots, RectangleF.Inflate(r, -4f * s, -4f * s));
        }
        TextRenderer.DrawText(g, "?", Font, ClientRectangle, Color.FromArgb(0x1A, 0x47, 0x9A),
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
    }
}

/// <summary>
/// The text of one "?" shown after a click, drawn over the settings window next to its button. It stays until it
/// is clicked (CloseRequested), Esc is pressed or another "?" is clicked (SettingsForm), and never takes the focus.
/// A child of the window rather than a window of its own, so it always has the window's scaling.
/// </summary>
internal sealed class HelpPopup : Control
{
    const TextFormatFlags Flags = TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.TextBoxControl;
    readonly string _hint;

    public HelpButton Source { get; }
    public string HelpText { get; }
    public event Action? CloseRequested;

    public HelpPopup(HelpButton source, string hint)
    {
        Source = source;
        HelpText = source.HelpText;
        _hint = hint;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.Selectable, false);
        TabStop = false;
        BackColor = SystemColors.Info;
        ForeColor = SystemColors.InfoText;
        Cursor = Cursors.Hand;
        AccessibleRole = AccessibleRole.HelpBalloon;
        AccessibleName = source.AccessibleName;
        AccessibleDescription = HelpText;
    }

    int Pad => LogicalToDeviceUnits(14);
    int Gap => LogicalToDeviceUnits(10);

    /// <summary>The size for the text wrapped at 520 px (at 100 % scaling), or narrower when the window is.</summary>
    public Size Measure(int maxWidth)
    {
        int textWidth = Math.Max(LogicalToDeviceUnits(160), Math.Min(LogicalToDeviceUnits(520), maxWidth - 2 * Pad));
        Size t = TextRenderer.MeasureText(HelpText, Font, new Size(textWidth, int.MaxValue), Flags);
        Size h = TextRenderer.MeasureText(_hint, Font, new Size(textWidth, int.MaxValue), Flags);
        return new Size(Math.Max(t.Width, h.Width) + 2 * Pad, t.Height + Gap + h.Height + 2 * Pad);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.Clear(BackColor);
        using (var pen = new Pen(SystemColors.WindowFrame, Math.Max(1, LogicalToDeviceUnits(1))))
            g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        int w = Width - 2 * Pad;
        Size t = TextRenderer.MeasureText(HelpText, Font, new Size(w, int.MaxValue), Flags);
        TextRenderer.DrawText(g, HelpText, Font, new Rectangle(Pad, Pad, w, t.Height), ForeColor, Flags);
        TextRenderer.DrawText(g, _hint, Font, new Rectangle(Pad, Pad + t.Height + Gap, w, Height - Pad - t.Height - Gap), SystemColors.GrayText, Flags);
    }

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        CloseRequested?.Invoke();
    }
}

