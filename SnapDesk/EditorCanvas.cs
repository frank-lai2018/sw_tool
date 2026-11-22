using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace SnapDesk;

internal enum EditTool { Pen, Arrow, Line, Rectangle, Ellipse, Highlight, Text, Number, Mosaic, Crop, Eyedropper, Select, Shape, Curve, FreeCurve }

internal sealed class EditorCanvas : Control
{
    sealed class State : IDisposable
    {
        internal Bitmap Background;
        internal List<Annotation> Objects;
        internal int Sequence;
        internal State(Bitmap image, List<Annotation> objects, int sequence) { Background = new Bitmap(image); Objects = objects.Select(a => a.Clone()).ToList(); Sequence = sequence; }
        public void Dispose() => Background.Dispose();
    }
    enum Gesture { None, Draw, Move, Resize }
    Bitmap bitmap;
    Bitmap? composite;
    List<Annotation> objects = [];
    readonly List<State> undo = [], redo = [];
    readonly List<PointF> stroke = [];
    Annotation? selected, original;
    State? gestureBefore;
    Gesture gesture;
    PointF start, end;
    int handle = -1;
    float zoom = 1;
    int sequence = 1;
    public event EventHandler? Changed;
    public event EventHandler? SelectionChanged;
    public event Action<Color>? ColorPicked;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal EditTool Tool { get; set; } = EditTool.Arrow;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal Color Ink { get; set; } = Color.FromArgb(244, 83, 87);
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal float StrokeWidth { get; set; } = 4;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal int TextSize { get; set; } = 24;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal bool TextBold { get; set; } = true;
    internal ShapeKind ShapeKind = ShapeKind.Rectangle;
    internal PointF[]? CustomPoints;
    internal Color FillColor = Color.White;
    internal bool Filled;
    internal bool Outlined = true;
    internal DashStyle StrokeDash = DashStyle.Solid;
    // Flattened export cache intentionally excludes selection handles.
    public Bitmap Image => composite ??= Render();
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal bool Dirty { get; set; }
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal string? FilePath { get; set; }
    internal Annotation? SelectedObject => selected;
    internal IReadOnlyList<Annotation> Objects => objects;
    public bool CanUndo => undo.Count > 0;
    public bool CanRedo => redo.Count > 0;
    public float Zoom => zoom;

    public EditorCanvas(Bitmap image)
    {
        bitmap = image; DoubleBuffered = true; BackColor = Color.White; Cursor = Cursors.Cross; TabStop = true; SetZoom(1);
    }
    public void SetZoom(float value) { zoom = Math.Clamp(value, .1f, 4); Size = new Size((int)Math.Ceiling(bitmap.Width * zoom), (int)Math.Ceiling(bitmap.Height * zoom)); Invalidate(); }
    PointF ImagePoint(Point p) => new(Math.Clamp(p.X / zoom, 0, bitmap.Width - 1), Math.Clamp(p.Y / zoom, 0, bitmap.Height - 1));
    State CaptureState() => new(bitmap, objects, sequence);
    void Snapshot() => PushUndo(CaptureState());
    void PushUndo(State state)
    {
        undo.Add(state); foreach (var s in redo) s.Dispose(); redo.Clear();
        long bytes = undo.Sum(s => (long)s.Background.Width * s.Background.Height * 4);
        while (undo.Count > 1 && (undo.Count > 24 || bytes > 192L * 1024 * 1024)) { bytes -= (long)undo[0].Background.Width * undo[0].Background.Height * 4; undo[0].Dispose(); undo.RemoveAt(0); }
    }
    void InvalidateImage() { composite?.Dispose(); composite = null; Invalidate(); }
    void Notify() { Dirty = true; InvalidateImage(); SetZoom(zoom); Changed?.Invoke(this, EventArgs.Empty); }
    void Restore(State state)
    {
        bitmap.Dispose(); bitmap = state.Background; objects = state.Objects; sequence = state.Sequence;
        SelectObject(null); InvalidateImage(); SetZoom(zoom);
    }
    public void Undo() { CancelGesture(); if (undo.Count == 0) return; redo.Add(CaptureState()); var state = undo[^1]; undo.RemoveAt(undo.Count - 1); Restore(state); Notify(); }
    public void Redo() { CancelGesture(); if (redo.Count == 0) return; undo.Add(CaptureState()); var state = redo[^1]; redo.RemoveAt(redo.Count - 1); Restore(state); Notify(); }
    Bitmap Render()
    {
        var result = new Bitmap(bitmap);
        try
        {
            foreach (var a in objects)
            {
                if (a.Kind == EditTool.Mosaic)
                {
                    var area = Rectangle.Intersect(Rectangle.Ceiling(a.Bounds), new Rectangle(Point.Empty, result.Size));
                    if (area.Width > 0 && area.Height > 0) Pixelate(result, area);
                }
                else { using var g = Graphics.FromImage(result); a.Draw(g); }
            }
            return result;
        }
        catch { result.Dispose(); throw; }
    }
    internal void SelectObject(Annotation? value) { selected = value; Invalidate(); SelectionChanged?.Invoke(this, EventArgs.Empty); }
    internal Annotation? HitTest(PointF point) => objects.LastOrDefault(a => a.Hit(point, 5 / zoom));
    internal void AddAnnotation(Annotation annotation) { Snapshot(); objects.Add(annotation); SelectObject(annotation); Notify(); }
    internal void UpdateSelectedStyle(Color? color = null, float? width = null, int? fontSize = null, bool? bold = null, Color? fillColor = null, bool? filled = null, bool? outlined = null, DashStyle? dash = null)
    {
        if (selected == null) return;
        if ((!color.HasValue || selected.Color == color) && (!width.HasValue || selected.Width == width) && (!fontSize.HasValue || selected.FontSize == fontSize) && (!bold.HasValue || selected.Bold == bold) && (!fillColor.HasValue || selected.FillColor == fillColor) && (!filled.HasValue || selected.Filled == filled) && (!outlined.HasValue || selected.Outlined == outlined) && (!dash.HasValue || selected.DashStyle == dash)) return;
        Snapshot();
        if (color.HasValue) selected.Color = color.Value;
        if (width.HasValue) selected.Width = Math.Clamp(width.Value, 1, 64);
        if (fillColor.HasValue) selected.FillColor = fillColor.Value;
        if (filled.HasValue) selected.Filled = filled.Value;
        if (outlined.HasValue) selected.Outlined = outlined.Value;
        if (dash.HasValue) selected.DashStyle = dash.Value;
        if(!selected.Filled && !selected.Outlined) selected.Outlined=true;
        if (fontSize.HasValue)
        {
            int size = Math.Clamp(fontSize.Value, 6, 300);
            if (selected.Kind is EditTool.Text or EditTool.Number)
            {
                float scale = (float)size / selected.FontSize;
                selected.Bounds = new RectangleF(selected.Bounds.Location, new SizeF(selected.Bounds.Width * scale, selected.Bounds.Height * scale));
            }
            selected.FontSize = size;
        }
        if (bold.HasValue && selected.Bold != bold.Value)
        {
            if (selected.Kind == EditTool.Text)
            {
                var before = Annotation.Measure(selected.Text, selected.FontSize, selected.Bold);
                var after = Annotation.Measure(selected.Text, selected.FontSize, bold.Value);
                ResizeTextMetrics(before, after);
            }
            selected.Bold = bold.Value;
        }
        Notify(); SelectionChanged?.Invoke(this, EventArgs.Empty);
    }
    internal void ChangeSelectedText(string text)
    {
        if (selected?.Kind != EditTool.Text || string.IsNullOrWhiteSpace(text) || text == selected.Text) return;
        Snapshot(); var before = Annotation.Measure(selected.Text, selected.FontSize, selected.Bold); var after = Annotation.Measure(text, selected.FontSize, selected.Bold);
        ResizeTextMetrics(before, after);
        selected.Text = text; Notify();
    }
    void ResizeTextMetrics(SizeF before, SizeF after)
    {
        if (selected == null) return;
        bool turned = Math.Abs(selected.Points[1].Y - selected.Points[0].Y) > .5;
        selected.Bounds = new RectangleF(selected.Bounds.Location, new SizeF(selected.Bounds.Width * (turned ? after.Height / before.Height : after.Width / before.Width), selected.Bounds.Height * (turned ? after.Width / before.Width : after.Height / before.Height)));
    }
    internal void EditSelectedText() { if (selected?.Kind == EditTool.Text && Dialogs.Text(this, "編輯標註文字", selected.Text) is { } text) ChangeSelectedText(text); }
    internal void DeleteSelected() { if (selected == null) return; Snapshot(); objects.Remove(selected); SelectObject(null); Notify(); }
    internal void SetSelectedBounds(RectangleF bounds)
    {
        if (selected == null || bounds.Width < 2 || bounds.Height < 2 || selected.Bounds == bounds) return;
        Snapshot(); ApplyBounds(selected, selected.Clone(), bounds); Notify(); SelectionChanged?.Invoke(this, EventArgs.Empty);
    }
    static void ApplyBounds(Annotation target, Annotation before, RectangleF bounds)
    {
        if (before.Kind is EditTool.Text or EditTool.Number)
        {
            bool turned = before.Kind == EditTool.Text && Math.Abs(before.Points[2].X - before.Points[0].X) > .5;
            float scale = turned ? bounds.Width / before.Bounds.Width : bounds.Height / before.Bounds.Height;
            target.FontSize = Math.Clamp((int)Math.Round(before.FontSize * scale), 6, 300);
        }
        target.Bounds = bounds;
    }
    public void Rotate(RotateFlipType type)
    {
        CancelGesture(); Snapshot(); var size = bitmap.Size; bitmap.RotateFlip(type);
        foreach (var a in objects)
        {
            var r = a.Bounds;
            if (type == RotateFlipType.Rotate90FlipNone)
            { a.Bounds = new RectangleF(size.Height - r.Bottom, r.X, r.Height, r.Width); a.Points = a.Points.Select(p => new PointF(1 - p.Y, p.X)).ToList(); }
            else if (type == RotateFlipType.RotateNoneFlipX)
            { a.Bounds = new RectangleF(size.Width - r.Right, r.Y, r.Width, r.Height); a.Points = a.Points.Select(p => new PointF(1 - p.X, p.Y)).ToList(); }
            else if (type == RotateFlipType.RotateNoneFlipY)
            { a.Bounds = new RectangleF(r.X, size.Height - r.Bottom, r.Width, r.Height); a.Points = a.Points.Select(p => new PointF(p.X, 1 - p.Y)).ToList(); }
        }
        SelectObject(null); Notify();
    }
    public void ResizeImage(Size size)
    {
        if (size.Width < 1 || size.Height < 1 || (long)size.Width * size.Height > 80_000_000) throw new ArgumentException("圖片最多支援 8,000 萬像素。");
        CancelGesture(); var n = new Bitmap(size.Width, size.Height);
        using (var g = Graphics.FromImage(n)) { g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.DrawImage(bitmap, new Rectangle(Point.Empty, size)); }
        Snapshot(); float sx = (float)size.Width / bitmap.Width, sy = (float)size.Height / bitmap.Height;
        foreach (var a in objects) { ApplyBounds(a, a.Clone(), new RectangleF(a.Bounds.X * sx, a.Bounds.Y * sy, a.Bounds.Width * sx, a.Bounds.Height * sy)); a.Width = Math.Clamp(a.Width * MathF.Sqrt(sx * sy), 1, 64); }
        bitmap.Dispose(); bitmap = n; SelectObject(null); Notify();
    }
    internal void Crop(Rectangle area)
    {
        area = Rectangle.Intersect(area, new Rectangle(Point.Empty, bitmap.Size)); if (area.Width < 2 || area.Height < 2) return;
        Snapshot(); var cropped = bitmap.Clone(area, PixelFormat.Format32bppArgb); bitmap.Dispose(); bitmap = cropped;
        foreach (var a in objects) a.Bounds = new RectangleF(a.Bounds.X - area.X, a.Bounds.Y - area.Y, a.Bounds.Width, a.Bounds.Height);
        SelectObject(null); Notify();
    }
    internal PointF[] SelectionHandles()
    {
        if (selected == null) return [];
        if (selected.Kind is EditTool.Line or EditTool.Arrow) return [selected.World(selected.Points[0]), selected.World(selected.Points[^1])];
        if (selected.Kind == EditTool.Curve) return selected.Points.Select(selected.World).ToArray();
        var r = selected.Bounds; float mx = r.Left + r.Width / 2, my = r.Top + r.Height / 2;
        return [new(r.Left, r.Top), new(mx, r.Top), new(r.Right, r.Top), new(r.Right, my), new(r.Right, r.Bottom), new(mx, r.Bottom), new(r.Left, r.Bottom), new(r.Left, my)];
    }
    int HandleAt(PointF point)
    {
        var handles = SelectionHandles(); float half = 6 / zoom;
        for (int i = 0; i < handles.Length; i++) if (Math.Abs(point.X - handles[i].X) <= half && Math.Abs(point.Y - handles[i].Y) <= half) return i;
        return -1;
    }
    Annotation Draft()
    {
        var bounds = RectangleF.FromLTRB(Math.Min(start.X, end.X), Math.Min(start.Y, end.Y), Math.Max(start.X, end.X), Math.Max(start.Y, end.Y));
        if (Tool is EditTool.Pen or EditTool.FreeCurve && stroke.Count > 0) bounds = RectangleF.FromLTRB(stroke.Min(p => p.X), stroke.Min(p => p.Y), stroke.Max(p => p.X), stroke.Max(p => p.Y));
        bounds.Width = Math.Max(1, bounds.Width); bounds.Height = Math.Max(1, bounds.Height);
        if(Tool==EditTool.Shape && ShapeKind is ShapeKind.Square or ShapeKind.Circle) { float side=Math.Min(bounds.Width,bounds.Height); bounds.Size=new SizeF(side,side); }
        var points = Tool is EditTool.Pen or EditTool.FreeCurve ? stroke : new List<PointF> { start, end };
        var a=new Annotation { Kind = Tool, Shape=ShapeKind, Bounds = bounds, Color = Ink, FillColor=FillColor,Filled=Filled,Outlined=Outlined,DashStyle=StrokeDash, Width = StrokeWidth, FontSize = TextSize, Points = points.Select(p => new PointF((p.X - bounds.X) / bounds.Width, (p.Y - bounds.Y) / bounds.Height)).ToList() };
        if(Tool==EditTool.Shape) a.Points=(CustomPoints??ShapeGeometry.Vertices(ShapeKind)).ToList();
        if(Tool==EditTool.Curve) a.Points=[new(0,1),new(.1f,0),new(.9f,1),new(1,0)];
        return a;
    }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Right) { if (HitTest(ImagePoint(e.Location)) is { } rightHit) SelectObject(rightHit); return; }
        if (e.Button != MouseButtons.Left) return;
        Focus(); start = end = ImagePoint(e.Location);
        if (Tool == EditTool.Eyedropper) { ColorPicked?.Invoke(Image.GetPixel((int)start.X, (int)start.Y)); return; }
        var hit = HitTest(start); handle = HandleAt(start);
        if (e.Clicks >= 2 && hit?.Kind == EditTool.Text) { SelectObject(hit); EditSelectedText(); return; }
        if (handle >= 0 && selected != null) { original = selected.Clone(); gesture = Gesture.Resize; Capture = true; return; }
        if (hit != null && Tool is not (EditTool.Crop or EditTool.Mosaic)) { SelectObject(hit); original = hit.Clone(); gesture = Gesture.Move; Capture = true; return; }
        SelectObject(null);
        if (Tool == EditTool.Select) return;
        if (Tool == EditTool.Text)
        {
            string? text = Dialogs.Text(this, "輸入標註文字"); if (string.IsNullOrWhiteSpace(text)) return;
            AddAnnotation(new Annotation { Kind = Tool, Bounds = new RectangleF(start, Annotation.Measure(text, TextSize, TextBold)), Text = text, Color = Ink, FontSize = TextSize, Bold = TextBold, Width = StrokeWidth, Points = [new(0, 0), new(1, 0), new(0, 1)] }); return;
        }
        if (Tool == EditTool.Number)
        {
            int size = Math.Max(30, TextSize + 12);
            var a = new Annotation { Kind = Tool, Bounds = new RectangleF(start.X - size / 2f, start.Y - size / 2f, size, size), Text = sequence.ToString(), Color = Ink, FontSize = TextSize, Width = StrokeWidth };
            AddAnnotation(a); sequence++; return;
        }
        gesture = Gesture.Draw; Capture = true; stroke.Clear(); stroke.Add(start);
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e); var point = ImagePoint(e.Location);
        if (gesture == Gesture.None)
        {
            int h = HandleAt(point); Cursor = h >= 0 ? selected?.Kind is EditTool.Line or EditTool.Arrow or EditTool.Curve ? Cursors.Cross : h % 4 == 0 ? Cursors.SizeNWSE : h % 4 == 2 ? Cursors.SizeNESW : h % 4 == 1 ? Cursors.SizeNS : Cursors.SizeWE : HitTest(point) != null ? Cursors.SizeAll : Tool == EditTool.Select ? Cursors.Default : Cursors.Cross;
            return;
        }
        end = point;
        if (gesture == Gesture.Draw) { stroke.Add(end); Invalidate(); return; }
        if (selected == null || original == null) return;
        if (start == end && gestureBefore == null) return;
        gestureBefore ??= CaptureState();
        var r = original.Bounds; float dx = end.X - start.X, dy = end.Y - start.Y;
        if (gesture == Gesture.Move) selected.Bounds = new RectangleF(r.X + dx, r.Y + dy, r.Width, r.Height);
        else if (original.Kind==EditTool.Curve)
        {
            var points=original.Points.Select(original.World).ToArray(); points[handle]=new PointF(points[handle].X+dx,points[handle].Y+dy);
            var bounds=RectangleF.FromLTRB(points.Min(p=>p.X),points.Min(p=>p.Y),points.Max(p=>p.X),points.Max(p=>p.Y)); bounds.Width=Math.Max(1,bounds.Width); bounds.Height=Math.Max(1,bounds.Height);
            selected.Bounds=bounds; selected.Points=points.Select(p=>new PointF((p.X-bounds.X)/bounds.Width,(p.Y-bounds.Y)/bounds.Height)).ToList();
        }
        else if (original.Kind is EditTool.Line or EditTool.Arrow)
        {
            var p0 = original.World(original.Points[0]); var p1 = original.World(original.Points[^1]);
            if (handle == 0) p0 = new PointF(p0.X + dx, p0.Y + dy); else p1 = new PointF(p1.X + dx, p1.Y + dy);
            var bounds = new RectangleF(Math.Min(p0.X, p1.X), Math.Min(p0.Y, p1.Y), Math.Max(1, Math.Abs(p0.X - p1.X)), Math.Max(1, Math.Abs(p0.Y - p1.Y)));
            selected.Bounds = bounds;
            selected.Points = new[] { p0, p1 }.Select(p => new PointF((p.X - bounds.X) / bounds.Width, (p.Y - bounds.Y) / bounds.Height)).ToList();
        }
        else
        {
            float l = r.Left, t = r.Top, right = r.Right, b = r.Bottom;
            if (handle is 0 or 6 or 7) l = Math.Min(right - 2, l + dx);
            if (handle is 2 or 3 or 4) right = Math.Max(l + 2, right + dx);
            if (handle is 0 or 1 or 2) t = Math.Min(b - 2, t + dy);
            if (handle is 4 or 5 or 6) b = Math.Max(t + 2, b + dy);
            ApplyBounds(selected, original, RectangleF.FromLTRB(l, t, right, b));
        }
        InvalidateImage();
    }
    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e); if (gesture == Gesture.None || e.Button != MouseButtons.Left) return;
        OnMouseMove(e); var finished = gesture; gesture = Gesture.None; Capture = false;
        if (finished == Gesture.Draw)
        {
            var draft = Draft();
            if (Tool == EditTool.Crop) Crop(Rectangle.Round(draft.Bounds));
            else if (Tool is EditTool.Pen or EditTool.Line or EditTool.Arrow or EditTool.Curve or EditTool.FreeCurve || draft.Bounds.Width >= 2 && draft.Bounds.Height >= 2) AddAnnotation(draft);
        }
        else if (gestureBefore != null)
        {
            if (selected != null && original != null && selected.Bounds == original.Bounds && selected.FontSize == original.FontSize && selected.Points.SequenceEqual(original.Points)) gestureBefore.Dispose();
            else { PushUndo(gestureBefore); Notify(); SelectionChanged?.Invoke(this, EventArgs.Empty); }
            gestureBefore = null;
        }
        original = null; Invalidate();
    }
    void CancelGesture()
    {
        gesture = Gesture.None; Capture = false; original = null;
        if (gestureBefore != null) { var before = gestureBefore; gestureBefore = null; Restore(before); }
        Invalidate();
    }
    protected override void OnMouseCaptureChanged(EventArgs e) { base.OnMouseCaptureChanged(e); if (!Capture && gesture != Gesture.None) CancelGesture(); }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape) { CancelGesture(); SelectObject(null); e.Handled = true; }
        else if (e.KeyCode == Keys.Delete) { DeleteSelected(); e.Handled = true; }
        else if (e.KeyCode == Keys.F2) { EditSelectedText(); e.Handled = true; }
        base.OnKeyDown(e);
    }
    protected override bool IsInputKey(Keys keyData) => (keyData & Keys.KeyCode) is Keys.Delete or Keys.F2 || base.IsInputKey(keyData);
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); var g = e.Graphics; g.ScaleTransform(zoom, zoom);
        g.InterpolationMode = zoom >= 1 ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBicubic;
        using var tile = new Bitmap(16, 16); using (var tg = Graphics.FromImage(tile)) { tg.Clear(Color.White); tg.FillRectangle(Brushes.Gainsboro, 0, 0, 8, 8); tg.FillRectangle(Brushes.Gainsboro, 8, 8, 8, 8); }
        using var checker = new TextureBrush(tile); g.FillRectangle(checker, new Rectangle(Point.Empty, bitmap.Size)); g.DrawImage(Image, new Rectangle(Point.Empty, bitmap.Size));
        if (gesture == Gesture.Draw)
        {
            var a = Draft();
            if (Tool is EditTool.Mosaic or EditTool.Crop) { using var p = new Pen(Color.DodgerBlue, 1 / zoom) { DashStyle = DashStyle.Dash }; g.DrawRectangle(p, a.Bounds.X, a.Bounds.Y, a.Bounds.Width, a.Bounds.Height); }
            else a.Draw(g);
        }
        if (selected != null)
        {
            using var pen = new Pen(Color.FromArgb(30, 125, 225), 1 / zoom) { DashStyle = DashStyle.Dash };
            var r = selected.Bounds; g.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height);
            if(selected.Kind==EditTool.Curve && selected.Points.Count==4) { g.DrawLine(pen,selected.World(selected.Points[0]),selected.World(selected.Points[1])); g.DrawLine(pen,selected.World(selected.Points[2]),selected.World(selected.Points[3])); }
            foreach (var p in SelectionHandles()) { var hr = new RectangleF(p.X - 4 / zoom, p.Y - 4 / zoom, 8 / zoom, 8 / zoom); g.FillRectangle(Brushes.White, hr); g.DrawRectangle(pen, hr.X, hr.Y, hr.Width, hr.Height); }
        }
    }
    internal static void Pixelate(Bitmap image, Rectangle rect)
    {
        const int block = 14;
        using var sampled = new Bitmap(image); using var g = Graphics.FromImage(image); g.CompositingMode = CompositingMode.SourceCopy;
        for (int y = rect.Top; y < rect.Bottom; y += block)
            for (int x = rect.Left; x < rect.Right; x += block)
            {
                var r = Rectangle.FromLTRB(x, y, Math.Min(x + block, rect.Right), Math.Min(y + block, rect.Bottom));
                using var brush = new SolidBrush(sampled.GetPixel(x + r.Width / 2, y + r.Height / 2)); g.FillRectangle(brush, r);
            }
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { bitmap.Dispose(); composite?.Dispose(); gestureBefore?.Dispose(); foreach (var s in undo) s.Dispose(); foreach (var s in redo) s.Dispose(); }
        base.Dispose(disposing);
    }
}
