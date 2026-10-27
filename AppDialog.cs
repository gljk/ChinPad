using System.Drawing;

namespace ChinPad;

/// <summary>App-owned dialogs use our button text, even when Windows uses another language.</summary>
internal static class AppDialog
{
    public static DialogResult Show(IWin32Window? owner, string message, string title, bool confirm = false)
    {
        using var form = new Form
        {
            Text = title, Font = new Font("Segoe UI", 11F), AutoScaleMode = AutoScaleMode.Dpi,
            AutoScaleDimensions = new SizeF(96, 96), StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog, MinimizeBox = false, MaximizeBox = false,
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(16),
        };
        var layout = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, Dock = DockStyle.Fill };
        var text = new Label { Text = message, AutoSize = true, MaximumSize = new Size(560, 0), UseMnemonic = false, Margin = new Padding(3, 3, 3, 16) };
        var buttons = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.RightToLeft, Anchor = AnchorStyles.Right };
        Button Add(string key, DialogResult result)
        {
            var button = new Button { Text = L10n.T(key), AutoSize = true, MinimumSize = new Size(120, 46), DialogResult = result, Margin = new Padding(8, 0, 0, 0) };
            buttons.Controls.Add(button);
            return button;
        }
        Button cancel = Add(confirm ? "Dialog.No" : "Dialog.OK", confirm ? DialogResult.No : DialogResult.OK);
        form.CancelButton = cancel;
        form.AcceptButton = cancel; // restoring defaults always starts on No
        if (confirm) Add("Dialog.Yes", DialogResult.Yes);
        layout.Controls.Add(text);
        layout.Controls.Add(buttons);
        form.Controls.Add(layout);
        return form.ShowDialog(owner);
    }
}
