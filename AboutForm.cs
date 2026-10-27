using System.Drawing;

namespace ChinPad;

/// <summary>A small, keyboard-accessible About window; text follows the selected app language.</summary>
internal sealed class AboutForm : Form
{
    readonly Label _version, _licence, _folderLabel, _updates;
    readonly TextBox _folder;
    readonly Button _close;
    readonly Icon _icon = TrayIcon.MakeAppIcon();

    public AboutForm(SettingsPaths paths)
    {
        SuspendLayout();
        Name = "AboutChinPad";
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 11);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Icon = _icon;
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1, Padding = new Padding(20),
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        Label Prose() => new()
        {
            AutoSize = true, MaximumSize = new Size(548, 0), UseMnemonic = false,
            Margin = new Padding(0, 0, 0, 12),
        };
        Label name = Prose();
        name.Text = AppInfo.Name;
        name.Font = new Font(Font.FontFamily, 18, FontStyle.Bold);
        _version = Prose();
        _licence = Prose();
        _folderLabel = Prose();
        _folderLabel.Margin = new Padding(0, 4, 0, 4);
        _folder = new TextBox
        {
            Text = paths.Folder, ReadOnly = true, Multiline = true, WordWrap = true,
            ScrollBars = ScrollBars.Vertical, Size = new Size(548, 66),
            BackColor = SystemColors.Window, TabIndex = 0, Margin = new Padding(0, 0, 0, 16),
        };
        _updates = Prose();
        _close = new Button
        {
            AutoSize = true, MinimumSize = new Size(120, 46), Anchor = AnchorStyles.Right,
            DialogResult = DialogResult.OK, TabIndex = 1, Margin = Padding.Empty,
        };
        _close.Click += (_, _) => Close();
        AcceptButton = CancelButton = _close;
        foreach (Control control in new Control[] { name, _version, _licence, _folderLabel, _folder, _updates, _close })
            layout.Controls.Add(control);
        Controls.Add(layout);
        L10n.Changed += Relabel;
        Relabel();
        ResumeLayout(true);
    }

    void Relabel()
    {
        Text = L10n.T("About.Title");
        _version.Text = L10n.F("About.Version", AppInfo.Version);
        _licence.Text = L10n.T("About.Licence");
        _folderLabel.Text = _folder.AccessibleName = L10n.T("About.SettingsFolder");
        _updates.Text = L10n.F("About.Updates", AppInfo.ProjectPage);
        _close.Text = L10n.T("Dialog.OK");
    }

    protected override void OnShown(EventArgs e) { base.OnShown(e); _close.Focus(); }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { L10n.Changed -= Relabel; _icon.Dispose(); }
        base.Dispose(disposing);
    }
}
