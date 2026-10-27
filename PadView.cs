using System.Drawing.Drawing2D;
using System.Globalization;

namespace ChinPad;

/// <summary>
/// The touchpad drawn to scale (aspect kept): its click zones as translucent rectangles with their action,
/// the move area, the touches as dots (green: moves the cursor; light green: in the move area; yellow: in a
/// click zone; grey: just landed, not decided yet; blue: a finger Windows handles) with their contact ID, and
/// a thin ring for 300 ms where the pad was pressed. The top edge is the one next to the keyboard.
/// </summary>
internal sealed class PadView : Control
{
    const double PressMarkMs = 300;
    PadSnapshot? _pad;
    IReadOnlyList<ZoneSettings> _zones = [];
    bool _paused;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal bool ShowLegend { get; set; } = true;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal IReadOnlyList<WizardPoint> Landings { get; set; } = [];

    public PadView()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        TabStop = false;
        AccessibleName = SettingsText.DrawingName;
        AccessibleRole = AccessibleRole.Graphic;
        BackColor = SystemColors.Window;
    }

    public void Show(PadSnapshot? pad, IReadOnlyList<ZoneSettings> zones, bool paused)
    {
        _pad = pad;
        _zones = zones;
        _paused = paused;
        Invalidate();
    }

    public static Color DotColor(DotState s) => s switch
    {
        DotState.Moving => Color.FromArgb(0x22, 0xB0, 0x45),
        DotState.Move => Color.FromArgb(0x9C, 0xDB, 0xAA),
        DotState.Zone => Color.FromArgb(0xF2, 0xC2, 0x00),
        DotState.Undecided => Color.FromArgb(0x9A, 0x9A, 0x9A),
        _ => Color.FromArgb(0x4A, 0x90, 0xD9),
    };

    static Color ZoneColor(ZoneAction a) => a switch
    {
        ZoneAction.Left => Color.FromArgb(0x3D, 0x7E, 0xE8),
        ZoneAction.Right => Color.FromArgb(0xF0, 0x8C, 0x1A),
        ZoneAction.Middle => Color.FromArgb(0x9B, 0x4D, 0xCA),
        _ => Color.FromArgb(0x70, 0x70, 0x70),
    };

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.Clear(BackColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float s = DeviceDpi / 96f;
        Font small = Font; // captions, legend and IDs: the window's 11 pt too
        using var bold = new Font(Font, FontStyle.Bold);
        int line = TextRenderer.MeasureText(g, "Ag", Font).Height;
        Rectangle area = Rectangle.Inflate(ClientRectangle, -(int)(8 * s), -(int)(6 * s));

        if (_pad is null)
        {
            TextRenderer.DrawText(g, SettingsText.DrawingNone, Font, area, SystemColors.GrayText,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
            return;
        }

        // Legend first (it decides how much height the pad gets).
        var legend = new (DotState State, string Text)[]
        {
            (DotState.Moving, SettingsText.LegendMoving), (DotState.Move, SettingsText.LegendMove), (DotState.Zone, SettingsText.LegendZone),
            (DotState.Undecided, SettingsText.LegendUndecided), (DotState.Windows, SettingsText.LegendWindows),
        };
        int dot = (int)(12 * s), gap = (int)(14 * s);
        int smallLine = TextRenderer.MeasureText(g, "Ag", small).Height;
        var legendRows = new List<List<(DotState, string, int)>> { new() };
        int x = 0;
        foreach ((DotState st, string text) in ShowLegend ? legend : [])
        {
            int w = dot + (int)(4 * s) + TextRenderer.MeasureText(g, text, small).Width + gap;
            if (x > 0 && x + w > area.Width)
            {
                legendRows.Add(new());
                x = 0;
            }
            legendRows[^1].Add((st, text, w));
            x += w;
        }
        int legendH = (ShowLegend ? legendRows.Count : 0) * (smallLine + (int)(2 * s));

        // The pad rectangle, aspect kept, between the caption line and the legend.
        double wMm = _pad.Geometry.WidthMm > 0 ? _pad.Geometry.WidthMm : 100, hMm = _pad.Geometry.HeightMm > 0 ? _pad.Geometry.HeightMm : 60;
        int top = area.Top + smallLine + (int)(2 * s), bottom = area.Bottom - legendH - (int)(8 * s);
        double scale = Math.Max(0.1, Math.Min(area.Width / wMm, Math.Max(10, bottom - top) / hMm));
        var pr = new RectangleF((float)(area.Left + (area.Width - wMm * scale) / 2), (float)(top + (bottom - top - hMm * scale) / 2),
            (float)(wMm * scale), (float)(hMm * scale));
        TextRenderer.DrawText(g, SettingsText.KeyboardSide, small, new Point((int)pr.Left, (int)pr.Top - smallLine - (int)(2 * s)), SystemColors.GrayText);

        using (var padBrush = new SolidBrush(_paused ? Color.FromArgb(0xEE, 0xEE, 0xEE) : Color.FromArgb(0xFA, 0xFA, 0xFA)))
        using (var padPen = new Pen(Color.FromArgb(0x55, 0x55, 0x55), 2 * s))
        {
            g.FillRectangle(padBrush, pr);
            g.DrawRectangle(padPen, pr.X, pr.Y, pr.Width, pr.Height);
        }

        // Zones (later zones under earlier ones, as the first listed zone wins where they overlap).
        for (int i = _zones.Count - 1; i >= 0; i--)
        {
            ZoneSettings z = _zones[i];
            var zr = new RectangleF(pr.X + (float)z.XMin * pr.Width, pr.Y + (float)z.YMin * pr.Height,
                (float)(z.XMax - z.XMin) * pr.Width, (float)(z.YMax - z.YMin) * pr.Height);
            Color c = ZoneColor(z.Action);
            using var fill = new SolidBrush(Color.FromArgb(70, c));
            using var edge = new Pen(Color.FromArgb(190, c), 1.5f * s);
            g.FillRectangle(fill, zr);
            g.DrawRectangle(edge, zr.X, zr.Y, zr.Width, zr.Height);
            TextRenderer.DrawText(g, SettingsText.ZoneName(z.Action), bold, Rectangle.Round(zr), Color.FromArgb(0x20, 0x20, 0x20),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
        }

        // The move area's label, at a point inside no zone.
        if (MoveLabelPoint() is (double fx, double fy))
        {
            string text = _zones.Count == 0 ? SettingsText.MoveAreaNoZones : SettingsText.MoveArea;
            int width = Math.Max(20, (int)pr.Width - (int)(16 * s));
            Size ts = TextRenderer.MeasureText(g, text, Font, new Size(width, int.MaxValue), TextFormatFlags.WordBreak);
            var caption = new Rectangle((int)(pr.X + fx * pr.Width - width / 2.0), (int)(pr.Y + fy * pr.Height - ts.Height / 2.0), width, ts.Height);
            TextRenderer.DrawText(g, text, Font, caption, Color.FromArgb(0x60, 0x60, 0x60), TextFormatFlags.WordBreak | TextFormatFlags.HorizontalCenter);
        }

        // The press marker: a thin ring for 300 ms.
        if (_pad.Press is { HasPosition: true } press && Clock.NowMs - press.AtMs < PressMarkMs)
        {
            float cx = pr.X + (float)(press.XMm * scale), cy = pr.Y + (float)(press.YMm * scale);
            float r = Math.Max(20 * s, (float)(9 * scale));
            using var ring = new Pen(press.Outcome is "left" or "right" or "middle" ? Color.FromArgb(0xC0, 0x1E, 0x1E) : Color.FromArgb(0x60, 0x60, 0x60), 2 * s);
            g.DrawEllipse(ring, cx - r, cy - r, 2 * r, 2 * r);
            TextRenderer.DrawText(g, SettingsText.PressPrefix + SettingsText.PressOutcome(press.Outcome), bold, new Point((int)(cx - r), (int)(cy - r - smallLine - 2 * s)), ring.Color);
        }

        // The wizard keeps completed landings visible as numbered rings.
        for (int i = 0; i < Landings.Count; i++)
        {
            WizardPoint point = Landings[i];
            float cx = pr.X + (float)(point.X * scale), cy = pr.Y + (float)(point.Y * scale);
            float radius = Math.Max(13 * s, (float)(4 * scale));
            using var pen = new Pen(i < 3 ? ZoneColor(ZoneAction.Left) : ZoneColor(ZoneAction.Right), 3 * s);
            g.DrawEllipse(pen, cx - radius, cy - radius, 2 * radius, 2 * radius);
            TextRenderer.DrawText(g, (i % 3 + 1).ToString(CultureInfo.InvariantCulture), bold,
                Rectangle.Round(new RectangleF(cx - radius, cy - radius, radius * 2, radius * 2)),
                pen.Color, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        // Touches.
        foreach (ContactDot d in _pad.Contacts)
        {
            float cx = pr.X + (float)(d.XMm * scale), cy = pr.Y + (float)(d.YMm * scale);
            float r = Math.Max(11 * s, (float)(3.5 * scale));
            using var fill = new SolidBrush(DotColor(d.State));
            using var rim = new Pen(Color.FromArgb(0x30, 0x30, 0x30), 1.5f * s);
            g.FillEllipse(fill, cx - r, cy - r, 2 * r, 2 * r);
            g.DrawEllipse(rim, cx - r, cy - r, 2 * r, 2 * r);
            TextRenderer.DrawText(g, d.Id.ToString(CultureInfo.InvariantCulture), small, Rectangle.Round(new RectangleF(cx - r, cy - r, 2 * r, 2 * r)),
                Color.Black, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        // Legend.
        int ly = area.Bottom - legendH;
        foreach (var row in legendRows)
        {
            int lx = area.Left;
            foreach ((DotState st, string text, int w) in row)
            {
                using var b = new SolidBrush(DotColor(st));
                g.FillEllipse(b, lx, ly + (smallLine - dot) / 2f, dot, dot);
                TextRenderer.DrawText(g, text, small, new Point(lx + dot + (int)(4 * s), ly), SystemColors.ControlText);
                lx += w;
            }
            ly += smallLine + (int)(2 * s);
        }
    }

    /// <summary>A point (pad fractions) for the "Move area" label that lies in no zone; null if the zones cover every candidate.</summary>
    (double, double)? MoveLabelPoint()
    {
        if (_zones.Count == 0) return (0.5, 0.5);
        if (StripLayout.IsStandard(_zones, out double top)) return (0.5, top / 2);
        foreach ((double fx, double fy) in new[] { (0.5, 0.4), (0.5, 0.15), (0.5, 0.6), (0.3, 0.4), (0.7, 0.4), (0.5, 0.85) })
            if (ZoneSettings.Find(_zones, fx, fy) is null) return (fx, fy);
        return null;
    }
}
