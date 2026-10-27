using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using ChinPad.Native;

namespace ChinPad;

/// <summary>
/// Notification-area icon of the background mode. The icon is a dot drawn at run time: green = active,
/// grey = paused, red = something failed (no touchpad, raw input or the mouse hook). Menu: Settings... (the
/// settings window, also on a double-click), Save diagnostic bundle..., Pause, Reload settings, Open settings
/// file, Open log, Open README, Start at login, About ChinPad..., Exit. The tooltip shows the state, the last click ChinPad
/// sent and, while ClickLock keeps a button down, "left button locked" (0.5.0). Dispose removes the icon at
/// once, so no ghost icon stays in the tray.
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    static readonly Color Green = Color.FromArgb(0x22, 0xB0, 0x45);
    readonly NotifyIcon _notify;
    readonly ContextMenuStrip _menu;
    readonly ToolStripMenuItem _pauseItem, _readmeItem, _startItem;
    readonly Icon _active, _paused, _failed;
    readonly List<(ToolStripItem Item, string Key)> _labels = [];
    bool _isPaused;
    int _strategy;
    string? _problem;
    MouseButton? _lastButton, _lockedButton;
    DateTime _lastAt;
    bool _disposed;

    public event Action? ShowSettingsWindow, SaveBundle, TogglePause, ReloadSettings, OpenSettingsFile, OpenLog, OpenReadme, ToggleStartAtLogin, ShowAboutWindow, ExitRequested;
    /// <summary>Raised just before the menu shows, so the owner can refresh SetMenuState.</summary>
    public event Action? MenuOpening;

    public TrayIcon()
    {
        _active = MakeDot(Green);
        _paused = MakeDot(Color.FromArgb(0x9A, 0x9A, 0x9A));
        _failed = MakeDot(Color.FromArgb(0xD8, 0x2C, 0x2C));

        _pauseItem = Item("Tray.Pause", () => TogglePause?.Invoke());
        _readmeItem = Item("Tray.OpenReadme", () => OpenReadme?.Invoke());
        _startItem = Item("Tray.Login", () => ToggleStartAtLogin?.Invoke());
        var settingsItem = Item("Tray.Settings", () => ShowSettingsWindow?.Invoke());
        settingsItem.Font = new Font(settingsItem.Font, FontStyle.Bold); // the double-click action
        _menu = new ContextMenuStrip();
        _menu.Items.Add(settingsItem);
        _menu.Items.Add(Item("Tray.Bundle", () => SaveBundle?.Invoke()));
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(_pauseItem);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(Item("Tray.Reload", () => ReloadSettings?.Invoke()));
        _menu.Items.Add(Item("Tray.OpenSettings", () => OpenSettingsFile?.Invoke()));
        _menu.Items.Add(Item("Tray.OpenLog", () => OpenLog?.Invoke()));
        _menu.Items.Add(_readmeItem);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(_startItem);
        _menu.Items.Add(Item("Tray.About", () => ShowAboutWindow?.Invoke()));
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(Item("Tray.Exit", () => ExitRequested?.Invoke()));
        _menu.Opening += (_, _) => MenuOpening?.Invoke();

        _notify = new NotifyIcon { Icon = _active, Text = "ChinPad", ContextMenuStrip = _menu };
        L10n.Changed += Relabel;
        _notify.MouseDoubleClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) ShowSettingsWindow?.Invoke();
        };
    }

    ToolStripMenuItem Item(string key, Action action)
    {
        var item = new ToolStripMenuItem(L10n.T(key), null, (_, _) => action());
        _labels.Add((item, key));
        return item;
    }

    void Relabel()
    {
        if (_disposed) return;
        foreach (var (item, key) in _labels) item.Text = L10n.T(key);
        ShowText();
    }

    public void Show() => _notify.Visible = true;

    /// <param name="problem">Short text of a failed component, or null; shown in the tooltip and as a red icon.</param>
    /// <param name="hotkey">Shown next to "Pause" in the menu; empty when no hotkey is registered.</param>
    public void Update(bool paused, int strategy, string? problem, string hotkey)
    {
        if (_disposed) return;
        _pauseItem.Checked = paused;
        _pauseItem.ShortcutKeyDisplayString = hotkey.Length > 0 ? hotkey : null;
        _notify.Icon = problem is not null ? _failed : paused ? _paused : _active;
        _isPaused = paused;
        _strategy = strategy;
        _problem = problem;
        ShowText();
    }

    /// <summary>"Start at login" check mark, and "Open README" enabled only when the file exists.</summary>
    public void SetMenuState(bool startAtLogin, bool readmeAvailable)
    {
        if (_disposed) return;
        _startItem.Checked = startAtLogin;
        _readmeItem.Enabled = readmeAvailable;
    }

    /// <summary>Second tooltip line, e.g. "last click: left 14:03:22".</summary>
    public void SetLastClick(MouseButton button, DateTime at)
    {
        if (_disposed) return;
        _lastButton = button;
        _lastAt = at;
        ShowText();
    }

    public void SetLocked(MouseButton? button)
    {
        if (_disposed || button == _lockedButton) return;
        _lockedButton = button;
        ShowText();
    }

    void ShowText()
    {
        string state = L10n.F("Tray.State", L10n.T(_isPaused ? "Tray.Paused" : "Tray.Active"), _strategy);
        if (_problem is not null) state += " - " + _problem;
        string last = _lastButton is MouseButton b ? L10n.F("Tray.Last" + b, _lastAt) : "";
        string locked = _lockedButton is MouseButton l ? L10n.T("Tray.Locked" + l) : "";
        string text = string.Join("\n", new[] { state, last, locked }.Where(t => t.Length > 0));
        _notify.Text = text.Length <= 127 ? text : text[..127]; // NotifyIcon allows at most 127 characters
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        L10n.Changed -= Relabel;
        _notify.Visible = false; // removes the icon from the tray now
        _notify.Dispose();
        _menu.Dispose();
        _active.Dispose();
        _paused.Dispose();
        _failed.Dispose();
    }

    /// <summary>The green dot at window-icon size, for the settings window (the caller disposes it).</summary>
    public static Icon MakeAppIcon() => MakeDot(Green, Math.Max(32, SystemInformation.IconSize.Width));

    /// <summary>A filled circle with a thin dark rim on a transparent small-icon bitmap (16x16 at 100 % scaling).</summary>
    static Icon MakeDot(Color fill, int size = 0)
    {
        if (size <= 0) size = Math.Max(16, SystemInformation.SmallIconSize.Width);
        using var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            float inset = size / 8f;
            var r = new RectangleF(inset, inset, size - 2 * inset, size - 2 * inset);
            using var brush = new SolidBrush(fill);
            using var rim = new Pen(Color.FromArgb(170, 0, 0, 0), Math.Max(1f, size / 16f));
            g.FillEllipse(brush, r);
            g.DrawEllipse(rim, r);
        }
        // GetHicon creates an icon handle that Icon.FromHandle does not own: copy it into an Icon that
        // owns its own handle, then free the original.
        IntPtr handle = bmp.GetHicon();
        try
        {
            using Icon borrowed = Icon.FromHandle(handle);
            return (Icon)borrowed.Clone();
        }
        finally
        {
            InputNative.DestroyIcon(handle);
        }
    }
}
