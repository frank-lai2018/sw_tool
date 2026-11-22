using System.Drawing.Drawing2D;

namespace SnapDesk;

internal enum AccessoryMode { Color, Magnifier, Ruler, Crosshair, Protractor, Whiteboard }

internal sealed class AccessoryOverlay : Form
{
    readonly Bitmap desktop;
    readonly AccessoryMode mode;
    readonly List<List<Point>> strokes = [];
    readonly List<Point> anglePoints = [];
    Point current = new(200, 200), start;
    bool dragging;
    public Color? ChosenColor { get; private set; }
    public AccessoryOverlay(Bitmap desktop, AccessoryMode mode)
    {
        this.desktop = desktop; this.mode = mode;
        AutoScaleMode = AutoScaleMode.None; StartPosition = FormStartPosition.Manual; Bounds = SystemInformation.VirtualScreen;
        Text = "SnapDesk · 螢幕工具";
        FormBorderStyle = FormBorderStyle.None; TopMost = true; ShowInTaskbar = true; DoubleBuffered = true; KeyPreview = true; Cursor = Cursors.Cross;
        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape) Close();
            if (e.Control && e.KeyCode == Keys.Z && strokes.Count > 0) { strokes.RemoveAt(strokes.Count - 1); Invalidate(); }
            if (e.KeyCode == Keys.Delete) { strokes.Clear(); anglePoints.Clear(); Invalidate(); }
        };
        MouseDown += (_, e) =>
        {
            if (e.Button == MouseButtons.Right) { Close(); return; }
            if (e.Button != MouseButtons.Left) return;
            current = start = e.Location; dragging = true; Capture = true;
            if (mode == AccessoryMode.Color) { ChosenColor = desktop.GetPixel(Math.Clamp(e.X, 0, desktop.Width - 1), Math.Clamp(e.Y, 0, desktop.Height - 1)); Clipboard.SetText(ColorTranslator.ToHtml(ChosenColor.Value)); Close(); return; }
            if (mode == AccessoryMode.Whiteboard) strokes.Add([current]);
            if (mode == AccessoryMode.Protractor) { if (anglePoints.Count == 3) anglePoints.Clear(); anglePoints.Add(current); }
            Invalidate();
        };
        MouseMove += (_, e) => { current = e.Location; if (dragging && mode == AccessoryMode.Whiteboard && strokes.Count > 0) strokes[^1].Add(current); Invalidate(); };
        MouseUp += (_, _) => { dragging = false; Capture = false; Invalidate(); };
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.DrawImageUnscaled(desktop, Point.Empty); g.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(Color.FromArgb(244, 83, 87), 2);
        using var font = new Font("Microsoft JhengHei UI", 12, FontStyle.Bold);
        string info = "";
        switch (mode)
        {
            case AccessoryMode.Color:
            case AccessoryMode.Magnifier:
                int x = Math.Clamp(current.X, 0, desktop.Width - 1), y = Math.Clamp(current.Y, 0, desktop.Height - 1);
                var color = desktop.GetPixel(x, y);
                var src = Rectangle.Intersect(new Rectangle(x - 12, y - 12, 25, 25), new Rectangle(Point.Empty, desktop.Size));
                int mx = Math.Clamp(current.X + 28, 0, Math.Max(0, Width - 220)), my = Math.Clamp(current.Y + 28, 0, Math.Max(0, Height - 270));
                var dest = new Rectangle(mx, my, src.Width * 8, src.Height * 8);
                g.InterpolationMode = InterpolationMode.NearestNeighbor; g.PixelOffsetMode = PixelOffsetMode.Half; g.DrawImage(desktop, dest, src, GraphicsUnit.Pixel); g.DrawRectangle(pen, mx + (x - src.X) * 8, my + (y - src.Y) * 8, 8, 8);
                var readout = $"{ColorTranslator.ToHtml(color)}\nRGB {color.R}, {color.G}, {color.B}";
                g.FillRectangle(Brushes.Black, mx, dest.Bottom + 3, 220, 55); g.DrawString(readout, font, Brushes.White, mx + 5, dest.Bottom + 5);
                info = mode == AccessoryMode.Color ? "螢幕取色 · 點選複製 HEX 色碼" : "放大鏡 · 8 倍放大（凍結畫面）"; break;
            case AccessoryMode.Ruler:
                var rect = CaptureOverlay.Normalize(start, current);
                if (!start.IsEmpty)
                {
                    using var brush = new SolidBrush(Color.FromArgb(60, Color.Gold)); g.FillRectangle(brush, rect); g.DrawRectangle(pen, rect);
                    for (int tick = 0; tick < rect.Width; tick += 10) g.DrawLine(pen, rect.X + tick, rect.Top, rect.X + tick, rect.Top + (tick % 50 == 0 ? 14 : 7));
                    for (int tick = 0; tick < rect.Height; tick += 10) g.DrawLine(pen, rect.Left, rect.Y + tick, rect.Left + (tick % 50 == 0 ? 14 : 7), rect.Y + tick);
                }
                info = $"像素尺 · 拖曳量測  {rect.Width} × {rect.Height} px · 對角線 {Math.Sqrt((double)rect.Width * rect.Width + (double)rect.Height * rect.Height):F1} px"; break;
            case AccessoryMode.Crosshair:
                g.DrawLine(pen, current.X, 0, current.X, Height); g.DrawLine(pen, 0, current.Y, Width, current.Y);
                info = $"十字座標線 · X {current.X + Left}, Y {current.Y + Top} px"; break;
            case AccessoryMode.Protractor:
                foreach (var p in anglePoints) g.FillEllipse(Brushes.OrangeRed, p.X - 5, p.Y - 5, 10, 10);
                if (anglePoints.Count >= 2) g.DrawLine(pen, anglePoints[0], anglePoints[1]);
                if (anglePoints.Count == 2) g.DrawLine(pen, anglePoints[1], current);
                if (anglePoints.Count == 3) { g.DrawLine(pen, anglePoints[1], anglePoints[2]); info = $"量角器 · {Angle(anglePoints[0], anglePoints[1], anglePoints[2]):F2}° · 點選重新量測"; }
                else info = "量角器 · 依序點選起點、頂點、終點";
                break;
            case AccessoryMode.Whiteboard:
                using (var ink = new Pen(Color.FromArgb(244, 83, 87), 4) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round }) foreach (var stroke in strokes) if (stroke.Count > 1) g.DrawLines(ink, stroke.ToArray());
                info = "螢幕白板 · 拖曳繪畫 · Ctrl+Z 復原 · Delete 清除（凍結畫面）"; break;
        }
        var label = info + "    |    Esc / 右鍵離開";
        var labelSize = g.MeasureString(label, font); g.FillRectangle(Brushes.Black, 16, 16, labelSize.Width + 28, 44); g.DrawString(label, font, Brushes.White, 30, 27);
    }
    internal static double Angle(Point a, Point vertex, Point b)
    {
        double ax = a.X - vertex.X, ay = a.Y - vertex.Y, bx = b.X - vertex.X, by = b.Y - vertex.Y;
        double length = Math.Sqrt(ax * ax + ay * ay) * Math.Sqrt(bx * bx + by * by);
        return length == 0 ? 0 : Math.Acos(Math.Clamp((ax * bx + ay * by) / length, -1, 1)) * 180 / Math.PI;
    }
}
