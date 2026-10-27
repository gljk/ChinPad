using System.Drawing.Drawing2D;

namespace ChinPad;

/// <summary>Renders WizardLogic, forwards frames and writes its values through the normal settings host.</summary>
internal sealed class WizardForm : Form
{
    readonly ISettingsHost _host;
    readonly TableLayoutPanel _root, _content;
    readonly Label _step, _title, _instructions, _details, _pads, _measureText, _holdText, _error, _languageLabel, _speed, _strip;
    readonly PadView _pad = new() { ShowLegend = false };
    readonly ComboBox _language = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    readonly CheckBox _login = new WizardCheckBox { Checked = true, MinimumSize = new Size(0, 56) };
    readonly Button _back, _skip, _continue, _admin, _setup, _speedLess, _speedMore, _stripLess, _stripMore;
    readonly ProgressBar _measure = new() { Dock = DockStyle.Fill, Maximum = 1000 };
    readonly ProgressBar _hold = new() { Dock = DockStyle.Fill, Maximum = 1000 };
    readonly Panel _adminPanel = new() { Dock = DockStyle.Top, AutoSize = true };
    readonly TableLayoutPanel _tune;
    readonly WizardTarget _target;
    readonly System.Windows.Forms.Timer _timer = new() { Interval = 33 };
    AppSettings? _unsaved;
    WizardStep _lastStep;
    bool _rendering, _languageLoading, _trying, _finishing, _adminBusy, _adminExpanded;
    string _failure = "", _adminStatus = "";
    string? _adminStatusKey;

    public WizardLogic Logic { get; }
    public event Action<bool>? TryOutChanged;
    public event Action? Finished;

    public WizardForm(ISettingsHost host, WizardLogic logic)
    {
        _host = host;
        Logic = logic;
        SuspendLayout();
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 14);
        Text = L10n.T("Wizard.Title");
        Name = "FirstTimeSetup";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1360, 900);
        MinimumSize = new Size(780, 560);
        WindowState = FormWindowState.Maximized;
        DoubleBuffered = true;
        _root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 7, Padding = new Padding(20) };
        _root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < 7; i++) _root.RowStyles.Add(new RowStyle(i == 3 ? SizeType.Percent : SizeType.AutoSize, i == 3 ? 100 : 0));
        _step = Prose();
        _title = Prose();
        _title.Font = new Font(Font.FontFamily, 24, FontStyle.Bold);
        _instructions = Prose();
        _instructions.Margin = new Padding(0, 6, 0, 12);
        var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 44));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 56));
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _pad.Dock = DockStyle.Fill;
        _pad.Margin = new Padding(0, 0, 20, 0);
        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Margin = Padding.Empty };
        _content = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1 };
        _content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _details = Prose();
        _pads = Prose();
        _languageLabel = Prose();
        _language.Margin = new Padding(0, 6, 0, 12);
        _details.Margin = new Padding(0, 0, 0, 14);
        _login.Margin = new Padding(0, 8, 0, 12);
        _admin = BigButton();
        _setup = BigButton();
        var adminText = Prose();
        adminText.Name = "AdministratorOptions";
        var adminLayout = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Top, ColumnCount = 1 };
        adminLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        adminLayout.Controls.Add(adminText);
        adminLayout.Controls.Add(_setup);
        _adminPanel.Controls.Add(adminLayout);
        _admin.Click += (_, _) => { _adminExpanded = !_adminExpanded; RenderState(); };
        _setup.Click += async (_, _) => await SetupAdministrator();
        _tune = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Top, ColumnCount = 3, Margin = new Padding(0, 8, 0, 8) };
        _tune.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _tune.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 76));
        _tune.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 76));
        _speed = Prose(); _strip = Prose();
        _speedLess = BigButton(); _speedMore = BigButton(); _stripLess = BigButton(); _stripMore = BigButton();
        _speedLess.Click += (_, _) => Logic.AdjustSpeed(-1); _speedMore.Click += (_, _) => Logic.AdjustSpeed(1);
        _stripLess.Click += (_, _) => Logic.AdjustStrip(-1); _stripMore.Click += (_, _) => Logic.AdjustStrip(1);
        _tune.Controls.Add(_speed, 0, 0); _tune.Controls.Add(_speedLess, 1, 0); _tune.Controls.Add(_speedMore, 2, 0);
        _tune.Controls.Add(_strip, 0, 1); _tune.Controls.Add(_stripLess, 1, 1); _tune.Controls.Add(_stripMore, 2, 1);
        _target = new WizardTarget(logic) { Dock = DockStyle.Top, Height = 260, MinimumSize = new Size(0, 220) };
        foreach (Control c in new Control[] { _languageLabel, _language, _pads, _details, _target, _tune, _login, _admin, _adminPanel })
        {
            _content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _content.Controls.Add(c, 0, _content.RowCount++);
        }
        scroll.Controls.Add(_content);
        body.Controls.Add(_pad, 0, 0);
        body.Controls.Add(scroll, 1, 0);
        var progress = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, AutoSize = true, Margin = new Padding(0, 10, 0, 4) };
        progress.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _measureText = Prose(); _holdText = Prose();
        _measure.Height = _hold.Height = 18;
        progress.Controls.Add(_measureText);
        progress.Controls.Add(_measure);
        progress.Controls.Add(_holdText);
        progress.Controls.Add(_hold);
        _error = Prose(); _error.ForeColor = Color.Firebrick;
        var buttons = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 3, Margin = new Padding(0, 10, 0, 0) };
        for (int i = 0; i < 3; i++) buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 3));
        _back = BigButton(); _skip = BigButton(); _continue = BigButton();
        _continue.Font = new Font(Font, FontStyle.Bold);
        _back.Click += (_, _) => Command(WizardKey.Back);
        _skip.Click += (_, _) => Command(WizardKey.Skip);
        _continue.Click += (_, _) => Command(WizardKey.Continue);
        buttons.Controls.Add(_back, 0, 0); buttons.Controls.Add(_skip, 1, 0); buttons.Controls.Add(_continue, 2, 0);
        _root.Controls.Add(_step, 0, 0); _root.Controls.Add(_title, 0, 1); _root.Controls.Add(_instructions, 0, 2);
        _root.Controls.Add(body, 0, 3); _root.Controls.Add(progress, 0, 4); _root.Controls.Add(_error, 0, 5); _root.Controls.Add(buttons, 0, 6);
        Controls.Add(_root);
        _language.Items.AddRange([L10n.T("Language.Auto"), L10n.T("Language.English"), L10n.T("Language.Serbian")]);
        _language.SelectedIndexChanged += (_, _) =>
        {
            if (!_languageLoading && _language.SelectedIndex >= 0)
                Logic.ChangeLanguage(new[] { "auto", "en", "sr-Latn" }[_language.SelectedIndex]);
        };
        _root.Layout += (_, _) =>
        {
            int width = Math.Max(100, _root.ClientSize.Width - _root.Padding.Horizontal);
            foreach (Label label in new[] { _step, _title, _instructions, _measureText, _holdText, _error }) label.MaximumSize = new Size(width, 0);
        };
        _content.Layout += (_, _) =>
        {
            int width = Math.Max(100, _content.ClientSize.Width - _content.Padding.Horizontal - LogicalToDeviceUnits(12));
            foreach (Label label in new[] { _details, _pads, _languageLabel, adminText }) label.MaximumSize = new Size(width, 0);
            _setup.MaximumSize = new Size(width, 0);
            _login.MaximumSize = new Size(width, 0);
            _speed.MaximumSize = _strip.MaximumSize = new Size(Math.Max(80, width - LogicalToDeviceUnits(160)), 0);
        };
        Logic.SettingsProduced += SaveSettings;
        Logic.Changed += RenderState;
        L10n.Changed += RenderState;
        _timer.Tick += (_, _) => { if (_unsaved is null && !_adminBusy) Logic.Tick(Clock.NowMs); else RenderState(); };
        ResumeLayout(true);
        RenderState();
    }

    Label Prose() => new() { AutoSize = true, Anchor = AnchorStyles.Left | AnchorStyles.Right, UseMnemonic = false, Margin = new Padding(0, 2, 0, 2) };
    Button BigButton() => new()
    {
        AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(64, 56),
        Dock = DockStyle.Top, Padding = new Padding(10, 6, 10, 6), Margin = new Padding(0, 4, 8, 4),
        UseVisualStyleBackColor = true,
    };
    protected override void OnShown(EventArgs e) { base.OnShown(e); _timer.Start(); _continue.Focus(); }
    public void Feed(WizardFrame frame) { if (_unsaved is null && !_adminBusy && !IsDisposed) Logic.OnFrame(frame); }
    public void Command(WizardKey key)
    {
        if (_adminBusy) return;
        if (_unsaved is not null) { SaveSettings(_unsaved); RenderState(); return; }
        Logic.Command(key, Clock.NowMs);
    }
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape) { Command(WizardKey.Back); return true; }
        // Space still activates a focused checkbox, dropdown or button; on the page itself it continues.
        if (keyData == Keys.Enter && !_language.DroppedDown) { Command(WizardKey.Continue); return true; }
        if (keyData == Keys.Space && ActiveControl is not (ButtonBase or ComboBox)) { Command(WizardKey.Continue); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }
    void SaveSettings(AppSettings settings)
    {
        try
        {
            _host.WriteSettings(settings, "first-time setup");
            _unsaved = null; _failure = "";
            L10n.Apply(settings.Language);
        }
        catch (Exception ex) { _unsaved = settings; _failure = L10n.F("Settings.SaveFailed", L10n.Error(ex)); }
    }

    void RenderState()
    {
        if (_rendering || IsDisposed) return;
        _rendering = true;
        try
        {
            if (Logic.Step == WizardStep.Closed)
            {
                if (_lastStep == WizardStep.Finish && !_finishing)
                {
                    _host.SetStartAtLogin(_login.Checked);
                    if (_host.StartAtLoginEnabled != _login.Checked)
                    {
                        _failure = L10n.T("Settings.StartAtLoginFailed");
                        Logic.InjectState(WizardStep.Finish, Clock.NowMs);
                        return;
                    }
                }
                _finishing = true;
                Close();
                return;
            }
            _lastStep = Logic.Step;
            Text = L10n.T("Wizard.Title");
            _step.Text = L10n.F("Wizard.Step", Logic.StepNumber, 10);
            _title.Text = L10n.T("Wizard." + Logic.Step + ".Title");
            _instructions.Text = L10n.T("Wizard." + Logic.Step + ".Text");
            _back.Text = L10n.T("Wizard.Back");
            _skip.Text = L10n.T(Logic.Step == WizardStep.ChinVerdict && Logic.Verdict == ChinVerdict.NotReported ? "Wizard.FinishButton" : "Wizard.Skip");
            _continue.Text = L10n.T(_unsaved is not null ? "Wizard.Retry" : Logic.Step is WizardStep.Finish or WizardStep.NoPad ? "Wizard.FinishButton" : "Wizard.Continue");
            _back.Visible = Logic.CanBack;
            _skip.Visible = Logic.CanSkip;
            _continue.Enabled = !_adminBusy && (_unsaved is not null || Logic.CanContinue);
            _back.Enabled = _skip.Enabled = !_adminBusy;
            bool welcome = Logic.Step == WizardStep.Welcome, selection = Logic.Step == WizardStep.SelectPad, finish = Logic.Step == WizardStep.Finish;
            _languageLabel.Visible = _language.Visible = welcome;
            _languageLabel.Text = _language.AccessibleName = L10n.T("Language.Label");
            _languageLoading = true;
            _language.SelectedIndex = Logic.Settings.Language switch { "en" => 1, "sr-Latn" => 2, _ => 0 };
            _languageLoading = false;
            _pad.AccessibleName = L10n.T("Settings.DrawingName");
            _pads.Visible = selection;
            _pads.Text = string.Join("\r\n\r\n", Logic.Pads.Select(p => p.Label + (Logic.Reporting(p.Index) ? PadChoice.Text.ReportingNow : "")));
            _details.Text = DetailsText();
            _details.Visible = _details.Text.Length > 0;
            _login.Visible = _admin.Visible = finish;
            _login.Text = L10n.T("Wizard.Login");
            _admin.Text = L10n.T(_adminExpanded ? "Wizard.AdminHide" : "Wizard.Admin");
            _adminPanel.Visible = finish && _adminExpanded;
            _adminPanel.Controls[0].Controls[0].Text = L10n.T("Wizard.AdminOptions");
            _setup.Text = L10n.T("Wizard.SetupAdmin");
            _setup.Enabled = !_adminBusy;
            _target.Visible = _tune.Visible = Logic.TryOut;
            _target.Invalidate();
            _speed.Text = L10n.F("Wizard.Speed", Logic.Settings.SpeedPxPerMm);
            double stripMm = Logic.Selected is { } sel && Logic.Settings.Zones.Count > 0
                ? (Logic.Settings.Zones[0].YMax - Logic.Settings.Zones[0].YMin) * sel.Geometry.HeightMm : 0;
            _strip.Text = L10n.F("Wizard.Strip", stripMm);
            _speedLess.Text = _stripLess.Text = L10n.T("Wizard.Minus");
            _speedMore.Text = _stripMore.Text = L10n.T("Wizard.Plus");
            _speedLess.AccessibleName = L10n.T("Wizard.SpeedLess"); _speedMore.AccessibleName = L10n.T("Wizard.SpeedMore");
            _stripLess.AccessibleName = L10n.T("Wizard.StripLess"); _stripMore.AccessibleName = L10n.T("Wizard.StripMore");
            _measure.Value = (int)(Logic.MeasurementProgress * 1000);
            _measureText.Text = MeasurementText();
            _hold.Value = (int)(Logic.HoldProgress * 1000);
            _holdText.Text = L10n.T(Logic.Waiting ? "Wizard.Hold" : "Wizard.Automatic");
            _hold.Visible = Logic.Waiting;
            _error.Text = _failure.Length > 0 ? _failure : _adminStatusKey is { } key ? L10n.T(key) : _adminStatus;
            _error.Visible = _error.Text.Length > 0;
            DrawPad();
            bool trying = Logic.TryOut && _unsaved is null;
            if (_trying != trying) { _trying = trying; TryOutChanged?.Invoke(trying); }
        }
        finally { _rendering = false; }
    }

    string DetailsText() => Logic.Step switch
    {
        WizardStep.Welcome => L10n.T("Wizard.WelcomeLines"),
        WizardStep.SelectPad => Logic.Selected is { } pad ? L10n.F("Wizard.Selected", pad.Label) : "",
        WizardStep.ChinVerdict => Logic.Verdict switch
        {
            ChinVerdict.Good => L10n.T("Wizard.ChinGood"),
            ChinVerdict.JoinTouches => L10n.T("Wizard.ChinJoined"),
            ChinVerdict.NotReported => L10n.F("Wizard.ChinMissing", Logic.ContactsSeen == 0
                ? L10n.T("Wizard.Nothing") : L10n.F("Wizard.ShortTouches", Logic.ContactsSeen)),
            _ => L10n.T("Wizard.Skipped"),
        },
        WizardStep.LeftButton => L10n.F("Wizard.Landings", Logic.LeftLandings.Count),
        WizardStep.RightButton => L10n.F("Wizard.Landings", Logic.RightLandings.Count),
        WizardStep.Zones => L10n.T(Logic.ZonesKept ? "Wizard.ZonesKept" : Logic.SplitFallback ? "Wizard.SplitFallback" : "Wizard.ZonesReady"),
        WizardStep.Sweep => L10n.F("Wizard.Sweeps", Logic.SweepsCompleted),
        WizardStep.SweepResult => L10n.T(Logic.SweepTimedOut ? "Wizard.SweepMissing" : "Wizard.SweepReady"),
        WizardStep.ClickLeft or WizardStep.ClickRight => Logic.ClickTimedOut ? L10n.T("Wizard.ClickMissing") : "",
        WizardStep.TryIt => L10n.F("Wizard.Targets", string.Join("  ", Enumerable.Range(0, 3).Select(i => i < Logic.TargetsHit ? L10n.T("Wizard.Tick") : L10n.T("Wizard.TargetPending")))),
        WizardStep.Finish => L10n.F("Wizard.Summary", Logic.Selected?.Label ?? L10n.T("Settings.PadNone"),
            Logic.Settings.Zones.Count > 0 && Logic.Settings.Zones[0].YMin == 0 ? L10n.T("Wizard.TopButtons") : L10n.T("Wizard.BottomButtons"),
            Logic.Settings.SpeedPxPerMm, L10n.T(Logic.Settings.ClickLock.Enabled ? "Wizard.LockOn" : "Wizard.LockOff")),
        _ => "",
    };

    string MeasurementText() => Logic.Step switch
    {
        WizardStep.ChinTest or WizardStep.Still or WizardStep.Sweep or WizardStep.ClickLeft or WizardStep.ClickRight =>
            L10n.F("Wizard.Countdown", Math.Ceiling(Logic.SecondsRemaining)),
        WizardStep.LeftButton => L10n.F("Wizard.Landings", Logic.LeftLandings.Count),
        WizardStep.RightButton => L10n.F("Wizard.Landings", Logic.RightLandings.Count),
        _ => L10n.F("Wizard.Progress", Math.Round(Logic.MeasurementProgress * 100)),
    };

    void DrawPad()
    {
        WizardPad? selected = Logic.Selected ?? Logic.Pads
            .OrderByDescending(p => Logic.LiveFrame(p.Index)?.Contacts.Any(c => c.Tip) == true)
            .ThenByDescending(p => Logic.Reporting(p.Index)).FirstOrDefault();
        if (selected is null) { _pad.Show(null, [], true); return; }
        WizardFrame? frame = Logic.LiveFrame(selected.Index);
        var dots = new List<ContactDot>();
        if (frame is not null)
            foreach (WizardContact c in frame.Contacts.Where(c => c.Tip))
            {
                ZoneSettings? zone = ClickEngine.ClassifyMm(Logic.Settings.Zones, selected.Geometry, c.X, c.Y);
                dots.Add(new(c.Id, c.X, c.Y, zone is null ? DotState.Move : DotState.Zone, zone?.Action));
            }
        _pad.Landings = Logic.Step is WizardStep.LeftButton or WizardStep.RightButton or WizardStep.Zones
            ? Logic.LeftLandings.Concat(Logic.RightLandings).ToArray() : [];
        _pad.Show(new PadSnapshot(selected.Index, selected.Label, selected.Geometry, 0, frame?.AtMs ?? 0, dots, null),
            Logic.Settings.Zones, !Logic.TryOut);
    }

    async Task SetupAdministrator()
    {
        if (_adminBusy || Logic.Step != WizardStep.Finish) return;
        _adminBusy = true;
        _failure = "";
        try
        {
            _login.Checked = false;
            _host.SetStartAtLogin(false);
            if (_host.StartAtLoginEnabled) { _failure = L10n.T("Settings.StartAtLoginFailed"); return; }
            _adminStatusKey = "Wizard.AdminWorking";
            RenderState();
            string exe = Environment.ProcessPath ?? Path.Combine(_host.Paths.ExeDir, "ChinPad.exe");
            bool success = await WizardAdministrator.SetupAsync(exe);
            if (!IsDisposed) _adminStatusKey = success ? "Wizard.AdminSuccess" : "Wizard.AdminFailed";
        }
        catch (Exception ex) { if (!IsDisposed) _adminStatus = L10n.Error(ex); _adminStatusKey = null; }
        finally { _adminBusy = false; if (!IsDisposed) RenderState(); }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _timer.Stop();
        TryOutChanged?.Invoke(false);
        Finished?.Invoke();
        base.OnFormClosed(e);
        Dispose();
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
            L10n.Changed -= RenderState;
            Logic.Changed -= RenderState;
            Logic.SettingsProduced -= SaveSettings;
        }
        base.Dispose(disposing);
    }

    /// <summary>DrawToBitmap tests use the production rendering path, without waiting or changing the system.</summary>
    internal void InjectState(WizardStep step, double atMs)
    {
        _timer.Stop();
        Logic.InjectState(step, atMs);
        RenderState();
        PerformLayout();
    }
}

/// <summary>A pointer hit must be inside the circle. Each of the three targets occupies a different place.</summary>
internal sealed class WizardTarget(WizardLogic logic) : Control
{
    public Rectangle CircleBounds
    {
        get
        {
            float s = DeviceDpi / 96f;
            int d = Math.Max(80, (int)(100 * s));
            double fx = logic.TargetsHit switch { 0 => 0.15, 1 => 0.85, _ => 0.5 };
            double fy = logic.TargetsHit switch { 0 => 0.2, 1 => 0.8, _ => 0.5 };
            return new Rectangle((int)((Width - d) * fx), (int)((Height - d) * fy), d, d);
        }
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        Rectangle r = CircleBounds;
        using var brush = new SolidBrush(logic.TargetsHit >= 3 ? Color.SeaGreen : Color.RoyalBlue);
        e.Graphics.FillEllipse(brush, r);
        TextRenderer.DrawText(e.Graphics, L10n.T(logic.TargetsHit >= 3 ? "Wizard.Tick" : "Wizard.Target"), Font, r,
            Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        Rectangle r = CircleBounds;
        double dx = (e.X - (r.X + r.Width / 2.0)) / (r.Width / 2.0), dy = (e.Y - (r.Y + r.Height / 2.0)) / (r.Height / 2.0);
        if (dx * dx + dy * dy <= 1) logic.HitTarget();
    }
}


/// <summary>Wrap at the available width; the check and its caption remain a large keyboard target.</summary>
internal sealed class WizardCheckBox : CheckBox
{
    public WizardCheckBox() { AutoSize = false; Anchor = AnchorStyles.Left | AnchorStyles.Right; Width = 100; }
    protected override void OnSizeChanged(EventArgs e) { base.OnSizeChanged(e); Fit(); }
    protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Fit(); }
    protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); Fit(); }
    void Fit()
    {
        int width = Math.Max(20, Width - LogicalToDeviceUnits(28) - Padding.Horizontal);
        int height = Math.Max(MinimumSize.Height, TextRenderer.MeasureText(Text, Font, new Size(width, int.MaxValue),
            TextFormatFlags.WordBreak).Height + LogicalToDeviceUnits(10));
        if (Height != height) Height = height;
    }
}
