using System.Drawing.Drawing2D;

namespace SnapDesk;

internal enum CaptureMode { Region, FullScreen, Window, Fixed, Freehand, Repeat, Scrolling }

internal sealed class CaptureOverlay : Form
{
    readonly Bitmap desktop;
    readonly CaptureMode mode;
    readonly Size fixedSize;
    readonly Rectangle screenBounds;
    readonly List<Point> path = [];
    Point start, current;
    bool dragging;
    public Rectangle Selection { get; private set; }
    public Bitmap? Result { get; private set; }

    public CaptureOverlay(Bitmap desktop, CaptureMode mode, Size fixedSize)
    {
        this.desktop = desktop; this.mode = mode; this.fixedSize = fixedSize;
        screenBounds = SystemInformation.VirtualScreen;
        AutoScaleMode = AutoScaleMode.None; FormBorderStyle = FormBorderStyle.None; Bounds = screenBounds;
        Text = "SnapDesk · 選取截圖範圍";
        StartPosition = FormStartPosition.Manual; TopMost = true; ShowInTaskbar = true; DoubleBuffered = true; KeyPreview = true; Cursor = Cursors.Cross;
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); } };
    }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Right) { DialogResult = DialogResult.Cancel; Close(); return; }
        if (e.Button != MouseButtons.Left) return;
        if (mode == CaptureMode.Window)
        {
            var screenPoint = PointToScreen(e.Location);
            Hide();
            var handle = Native.GetAncestor(Native.WindowFromPoint(screenPoint), 2);
            var bounds = Native.WindowBounds(handle);
            if (bounds.Width < 2 || bounds.Height < 2) { DialogResult = DialogResult.Cancel; Close(); return; }
            var source = new Rectangle(bounds.X - screenBounds.X, bounds.Y - screenBounds.Y, bounds.Width, bounds.Height);
            Result = desktop.Clone(source, System.Drawing.Imaging.PixelFormat.Format32bppArgb); Selection = bounds;
            DialogResult = DialogResult.OK; Close(); return;
        }
        start = current = e.Location; dragging = true; Capture = true; path.Clear(); path.Add(start);
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        current = e.Location;
        if (dragging && mode == CaptureMode.Freehand) path.Add(current);
        Selection = mode == CaptureMode.Fixed ? new Rectangle(current, fixedSize) : mode == CaptureMode.Freehand && dragging ? PathBounds(path) : dragging ? Normalize(start, current) : Rectangle.Empty;
        Selection = Rectangle.Intersect(new Rectangle(Point.Empty, desktop.Size), Selection);
        Invalidate();
    }
    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (!dragging || e.Button != MouseButtons.Left) return;
        current = e.Location; dragging = false; Capture = false;
        if (mode == CaptureMode.Freehand) path.Add(current);
        Selection = Rectangle.Intersect(new Rectangle(Point.Empty, desktop.Size), mode == CaptureMode.Fixed ? new Rectangle(current, fixedSize) : mode == CaptureMode.Freehand ? PathBounds(path) : Normalize(start, current));
        if (Selection.Width < 2 || Selection.Height < 2) { Invalidate(); return; }
        if (mode == CaptureMode.Freehand && path.Count < 3) return;
        Result = new Bitmap(Selection.Width, Selection.Height);
        using (var g = Graphics.FromImage(Result))
        {
            if (mode == CaptureMode.Freehand)
            {
                using var shape = new GraphicsPath(); shape.AddPolygon(path.Select(p => new Point(p.X - Selection.X, p.Y - Selection.Y)).ToArray()); g.SetClip(shape);
            }
            g.DrawImage(desktop, new Rectangle(Point.Empty, Result.Size), Selection, GraphicsUnit.Pixel);
        }
        Selection = new Rectangle(Selection.X + screenBounds.X, Selection.Y + screenBounds.Y, Selection.Width, Selection.Height);
        DialogResult = DialogResult.OK; Close();
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.DrawImageUnscaled(desktop, 0, 0);
        using var shade = new SolidBrush(Color.FromArgb(125, 11, 18, 32)); g.FillRectangle(shade, ClientRectangle);
        if ((dragging || mode == CaptureMode.Fixed) && Selection.Width > 0 && Selection.Height > 0)
        {
            g.DrawImage(desktop, Selection, Selection, GraphicsUnit.Pixel);
            using var pen = new Pen(Color.FromArgb(72, 222, 183), 2); g.DrawRectangle(pen, Selection);
            if (mode == CaptureMode.Freehand && path.Count > 1) g.DrawLines(pen, path.ToArray());
        }
        using var font = new Font("Microsoft JhengHei UI", 12);
        string help = $"拖曳選取範圍  ·  Esc / 右鍵取消     {Selection.Width} × {Selection.Height} px";
        if (mode == CaptureMode.Fixed) help = $"點選放置 {fixedSize.Width} × {fixedSize.Height} 範圍  ·  Esc 取消";
        if (mode == CaptureMode.Window) help = "點選要擷取的視窗  ·  Esc / 右鍵取消";
        var location = new Point(Math.Clamp(current.X + 20, 10, Math.Max(10, Width - 580)), Math.Clamp(current.Y + 30, 10, Math.Max(10, Height - 50)));
        g.FillRectangle(Brushes.Black, location.X - 8, location.Y - 5, 570, 35); g.DrawString(help, font, Brushes.White, location);
    }
    internal static Rectangle Normalize(Point a, Point b) => Rectangle.FromLTRB(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));
    internal static Rectangle PathBounds(IReadOnlyList<Point> points) => points.Count == 0 ? Rectangle.Empty : Rectangle.FromLTRB(points.Min(p => p.X), points.Min(p => p.Y), points.Max(p => p.X) + 1, points.Max(p => p.Y) + 1);
}
