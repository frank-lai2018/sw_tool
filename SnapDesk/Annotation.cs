using System.Drawing.Drawing2D;

namespace SnapDesk;

internal sealed class Annotation
{
    internal EditTool Kind;
    internal RectangleF Bounds;
    internal Color Color;
    internal ShapeKind Shape;
    internal Color FillColor = System.Drawing.Color.White;
    internal bool Filled;
    internal bool Outlined = true;
    internal DashStyle DashStyle = DashStyle.Solid;
    internal float Width = 4;
    internal int FontSize = 24;
    internal bool Bold = true;
    internal string Text = "";
    // Normalized points preserve direction and text orientation through resize/flip/rotate.
    internal List<PointF> Points = [];
    internal Annotation Clone() => new() { Kind = Kind, Shape = Shape, Bounds = Bounds, Color = Color, FillColor = FillColor, Filled = Filled, Outlined = Outlined, DashStyle = DashStyle, Width = Width, FontSize = FontSize, Bold = Bold, Text = Text, Points = [.. Points] };
    internal PointF World(PointF p) => new(Bounds.Left + p.X * Bounds.Width, Bounds.Top + p.Y * Bounds.Height);
    internal static SizeF Measure(string text, int fontSize, bool bold = true)
    {
        using var image = new Bitmap(1, 1); using var g = Graphics.FromImage(image);
        using var font = new Font("Microsoft JhengHei UI", fontSize, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel);
        var size = g.MeasureString(text, font, 16000, StringFormat.GenericTypographic);
        return new SizeF(Math.Max(2, size.Width + 4), Math.Max(2, size.Height + 4));
    }
    internal bool Hit(PointF point, float tolerance)
    {
        var inflated = Bounds; inflated.Inflate(tolerance, tolerance);
        if (!inflated.Contains(point)) return false;
        if (ShapeGeometry.IsClosed(Kind))
        {
            using var shape = ShapeGeometry.Path(this); using var border = new Pen(System.Drawing.Color.Black,Width+tolerance*2);
            return shape.IsVisible(point) || shape.IsOutlineVisible(point,border);
        }
        if (Kind is not (EditTool.Pen or EditTool.Line or EditTool.Arrow or EditTool.Curve or EditTool.FreeCurve)) return true;
        if (Points.Count < 2) return true;
        using var path = new GraphicsPath();
        if(Kind==EditTool.Curve && Points.Count==4) path.AddBezier(World(Points[0]),World(Points[1]),World(Points[2]),World(Points[3]));
        else if(Kind==EditTool.FreeCurve && Points.Count>2) path.AddCurve(Points.Select(World).ToArray(),.4f);
        else path.AddLines(Points.Select(World).ToArray());
        using var pen = new Pen(System.Drawing.Color.Black, Math.Max(Width, 1) + tolerance * 2) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        return path.IsOutlineVisible(point, pen);
    }
    internal void Draw(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(Color, Width) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round, DashStyle = DashStyle };
        if(ShapeGeometry.IsClosed(Kind))
        {
            using var shape=ShapeGeometry.Path(this);
            if(Filled) { using var fill=new SolidBrush(FillColor); g.FillPath(fill,shape); }
            if(Outlined) g.DrawPath(pen,shape);
            return;
        }
        switch (Kind)
        {
            case EditTool.Rectangle: g.DrawRectangle(pen, Bounds.X, Bounds.Y, Bounds.Width, Bounds.Height); break;
            case EditTool.Ellipse: g.DrawEllipse(pen, Bounds); break;
            case EditTool.Highlight: using (var b = new SolidBrush(System.Drawing.Color.FromArgb(85, Color))) g.FillRectangle(b, Bounds); break;
            case EditTool.Pen:
                if (Points.Count > 1) g.DrawLines(pen, Points.Select(World).ToArray());
                else g.FillEllipse(pen.Brush, Bounds.X, Bounds.Y, Width, Width);
                break;
            case EditTool.Line: g.DrawLine(pen, World(Points[0]), World(Points[^1])); break;
            case EditTool.Arrow: using (var cap = new AdjustableArrowCap(4, 5)) { pen.CustomEndCap = cap; g.DrawLine(pen, World(Points[0]), World(Points[^1])); } break;
            case EditTool.Curve: if(Points.Count==4) g.DrawBezier(pen,World(Points[0]),World(Points[1]),World(Points[2]),World(Points[3])); break;
            case EditTool.FreeCurve: if(Points.Count>2) g.DrawCurve(pen,Points.Select(World).ToArray(),.4f); else if(Points.Count==2) g.DrawLine(pen,World(Points[0]),World(Points[1])); break;
            case EditTool.Text:
                var size = Measure(Text, FontSize, Bold);
                var p0 = World(Points[0]); var px = World(Points[1]); var py = World(Points[2]);
                var state = g.Save();
                try
                {
                    using var matrix = new Matrix((px.X - p0.X) / size.Width, (px.Y - p0.Y) / size.Width, (py.X - p0.X) / size.Height, (py.Y - p0.Y) / size.Height, p0.X, p0.Y);
                    g.MultiplyTransform(matrix, MatrixOrder.Prepend);
                    using var font = new Font("Microsoft JhengHei UI", FontSize, Bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel);
                    using var brush = new SolidBrush(Color);
                    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                    g.DrawString(Text, font, brush, new RectangleF(0, 0, size.Width, size.Height), StringFormat.GenericTypographic);
                }
                finally { g.Restore(state); }
                break;
            case EditTool.Number:
                using (var b = new SolidBrush(Color)) g.FillEllipse(b, Bounds);
                using (var font = new Font("Segoe UI", Math.Max(8, Math.Min(Bounds.Width, Bounds.Height) * .55f), FontStyle.Bold, GraphicsUnit.Pixel))
                using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center }) g.DrawString(Text, font, Brushes.White, Bounds, format);
                break;
        }
    }
}
