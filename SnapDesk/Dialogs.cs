namespace SnapDesk;

internal static class Dialogs
{
    internal static string? Text(IWin32Window owner, string title, string initial = "")
    {
        using var form = new Form { Text = title, ClientSize = new Size(410, 155), StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MinimizeBox = false, MaximizeBox = false, Font = new Font("Microsoft JhengHei UI", 10) };
        var box = new TextBox { Text = initial, Multiline = true, Dock = DockStyle.Top, Height = 105, ScrollBars = ScrollBars.Vertical };
        var ok = new Button { Text = "確定", DialogResult = DialogResult.OK, Dock = DockStyle.Right, Width = 90 };
        var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Dock = DockStyle.Right, Width = 90 };
        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 42, Padding = new Padding(4) };
        bottom.Controls.Add(cancel); bottom.Controls.Add(ok); form.Controls.Add(box); form.Controls.Add(bottom); form.AcceptButton = ok; form.CancelButton = cancel;
        form.Shown += (_, _) => box.SelectAll();
        return form.ShowDialog(owner) == DialogResult.OK ? box.Text : null;
    }

    internal static Size? Dimensions(IWin32Window owner, string title, Size initial)
    {
        using var form = new Form { Text = title, ClientSize = new Size(330, 145), StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MinimizeBox = false, MaximizeBox = false };
        var w = new NumericUpDown { Minimum = 1, Maximum = 16000, Value = Math.Clamp(initial.Width, 1, 16000), Location = new Point(90, 15), Width = 210 };
        var h = new NumericUpDown { Minimum = 1, Maximum = 16000, Value = Math.Clamp(initial.Height, 1, 16000), Location = new Point(90, 52), Width = 210 };
        form.Controls.AddRange([new Label { Text = "寬度 px", AutoSize = true, Location = new Point(15, 19) }, new Label { Text = "高度 px", AutoSize = true, Location = new Point(15, 56) }, w, h]);
        var ok = new Button { Text = "確定", Location = new Point(130, 100), DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "取消", Location = new Point(220, 100), DialogResult = DialogResult.Cancel };
        form.Controls.AddRange([ok, cancel]); form.AcceptButton = ok; form.CancelButton = cancel;
        return form.ShowDialog(owner) == DialogResult.OK ? new Size((int)w.Value, (int)h.Value) : null;
    }
}
