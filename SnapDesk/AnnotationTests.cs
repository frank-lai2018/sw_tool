using System.Reflection;

namespace SnapDesk;

internal static class AnnotationTests
{
    internal static Bitmap White(int width, int height)
    {
        var image = new Bitmap(width, height); using var g = Graphics.FromImage(image); g.Clear(Color.White); return image;
    }
    static void Mouse(EditorCanvas canvas, string method, int x, int y)
        => typeof(EditorCanvas).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(canvas, [new MouseEventArgs(MouseButtons.Left, 1, x, y, 0)]);
    static void Key(EditorCanvas canvas, Keys key)
        => typeof(EditorCanvas).GetMethod("OnKeyDown", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(canvas, [new KeyEventArgs(key)]);
    static void Drag(EditorCanvas canvas, Point from, Point to)
    {
        Mouse(canvas, "OnMouseDown", from.X, from.Y); Mouse(canvas, "OnMouseMove", to.X, to.Y); Mouse(canvas, "OnMouseUp", to.X, to.Y);
    }
    internal static void Run(Action<bool, string> check, string output)
    {
        using var host = new Form { ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new Point(-30000, -30000), ClientSize = new Size(650, 500) };
        using var canvas = new EditorCanvas(White(600, 400)); host.Controls.Add(canvas); host.Show(); Application.DoEvents();
        canvas.Tool = EditTool.Rectangle; canvas.Ink = Color.Red;
        Drag(canvas, new Point(40, 40), new Point(140, 100));
        check(canvas.Objects.Count == 1 && canvas.SelectedObject?.Bounds == new RectangleF(40, 40, 100, 60), "New rectangle remains a selected editable object");
        canvas.UpdateSelectedStyle(color: Color.Blue, width: 8);
        check(canvas.Objects[0].Color == Color.Blue && canvas.Objects[0].Width == 8 && canvas.Image.GetPixel(40, 70).ToArgb() == Color.Blue.ToArgb(), "Existing rectangle color and thickness update in exported pixels");
        canvas.Undo(); check(canvas.Objects[0].Color == Color.Red && canvas.Objects[0].Width == 4, "Undo restores annotation style");
        canvas.Redo(); check(canvas.Objects[0].Color == Color.Blue && canvas.Objects[0].Width == 8, "Redo restores annotation style");
        canvas.Tool = EditTool.Select; canvas.SelectObject(canvas.Objects[0]);
        Drag(canvas, new Point(80, 70), new Point(110, 90));
        check(canvas.Objects[0].Bounds == new RectangleF(70, 60, 100, 60) && canvas.Image.GetPixel(40, 70).ToArgb() == Color.White.ToArgb(), "Dragging moves annotation and restores underlying original pixels");
        canvas.Undo(); check(canvas.Objects[0].Bounds == new RectangleF(40, 40, 100, 60), "Object movement is one undo step");
        canvas.Redo(); canvas.SelectObject(canvas.Objects[0]);
        Drag(canvas, new Point(170, 120), new Point(220, 150));
        check(canvas.Objects[0].Bounds == new RectangleF(70, 60, 150, 90), "Dragging bottom-right handle resizes existing shape");
        canvas.Undo(); check(canvas.Objects[0].Bounds.Size == new SizeF(100, 60), "Handle resize can be undone independently");
        canvas.SelectObject(canvas.Objects[0]); canvas.SetZoom(.5f);
        Drag(canvas, new Point(60, 45), new Point(70, 50));
        check(canvas.Objects[0].Bounds.Location == new PointF(90, 70), "Object movement uses image coordinates at 50 percent zoom");
        canvas.SetZoom(1); canvas.SelectObject(canvas.Objects[0]); var beforeCancel = canvas.Objects[0].Bounds;
        Mouse(canvas, "OnMouseDown", 130, 95); Mouse(canvas, "OnMouseMove", 180, 115); Key(canvas, Keys.Escape);
        check(canvas.Objects[0].Bounds == beforeCancel && canvas.SelectedObject == null, "Escape cancels in-progress object drag without losing image");
        canvas.SelectObject(canvas.Objects[0]); Key(canvas, Keys.Delete);
        check(canvas.Objects.Count == 0 && canvas.Image.GetPixel(90, 90).ToArgb() == Color.White.ToArgb(), "Delete removes selected shape and restores background");
        canvas.Undo(); check(canvas.Objects.Count == 1, "Deleted object can be restored");

        var text = new Annotation { Kind = EditTool.Text, Bounds = new RectangleF(new PointF(260, 70), Annotation.Measure("原始文字", 24)), Text = "原始文字", FontSize = 24, Color = Color.DarkGreen, Points = [new(0, 0), new(1, 0), new(0, 1)] };
        canvas.AddAnnotation(text); var oldSize = text.Bounds.Size;
        canvas.ChangeSelectedText("這是重新編輯後的文字");
        check(canvas.Objects.Count == 2 && canvas.SelectedObject!.Text == "這是重新編輯後的文字" && text.Bounds.Width > oldSize.Width, "Existing text content can change without adding a new text object");
        canvas.UpdateSelectedStyle(color: Color.Purple, fontSize: 48); var enlargedSize = text.Bounds.Size;
        check(text.FontSize == 48 && text.Color == Color.Purple && text.Bounds.Height > oldSize.Height, "Existing text color and font size are editable");
        canvas.SetSelectedBounds(new RectangleF(text.Bounds.Location, new SizeF(enlargedSize.Width * .5f, enlargedSize.Height * 1.5f)));
        check(text.Bounds.Height > enlargedSize.Height, "Text bounding box can be resized after creation");
        canvas.Undo(); check(canvas.Objects[1].Bounds.Size == enlargedSize, "Undo restores text object dimensions");
        canvas.Undo(); check(canvas.Objects[1].FontSize == 24 && canvas.Objects[1].Color == Color.DarkGreen, "Text style undo preserves editable content");
        canvas.Undo(); check(canvas.Objects[1].Text == "原始文字", "Text content undo restores original string");
        canvas.Redo(); check(canvas.Objects[1].Text == "這是重新編輯後的文字", "Text content redo restores edited string");
        canvas.SelectObject(canvas.Objects[1]);
        canvas.UpdateSelectedStyle(bold: false);
        check(!canvas.SelectedObject!.Bold && canvas.SelectedObject.Text == "這是重新編輯後的文字", "Existing text can switch from bold to regular without losing content");
        canvas.Undo(); check(canvas.Objects[1].Bold, "Text weight change can be undone"); canvas.SelectObject(canvas.Objects[1]);
        var exported = new Bitmap(canvas.Image); canvas.SelectObject(null);
        using (exported) check(PixelsEqual(exported, canvas.Image), "Exported image is identical with and without selection handles");
        canvas.SelectObject(canvas.Objects[1]);
        canvas.Rotate(RotateFlipType.Rotate90FlipNone);
        check(canvas.Image.Size == new Size(400, 600) && canvas.Objects.Count == 2 && canvas.Objects[1].Text == "這是重新編輯後的文字", "Rotation preserves editable annotations and text");
        canvas.Undo(); check(canvas.Image.Size == new Size(600, 400) && canvas.Objects.Count == 2, "Rotation undo restores geometry and objects");
        var rectBefore = canvas.Objects[0].Bounds;
        canvas.ResizeImage(new Size(300, 200));
        check(canvas.Objects[0].Bounds.X == rectBefore.X / 2 && canvas.Objects[0].Bounds.Width == rectBefore.Width / 2 && canvas.Objects.Count == 2, "Image resize transforms annotations without flattening them");
        check(canvas.Objects[1].FontSize == 12, "Image resize keeps text-size metadata consistent with scaled text");
        canvas.Undo(); canvas.Crop(new Rectangle(20, 30, 500, 300));
        check(canvas.Objects.Count == 2 && canvas.Objects[0].Bounds.X == rectBefore.X - 20 && canvas.Objects[0].Bounds.Y == rectBefore.Y - 30, "Cropping retains editable objects in cropped coordinates");
        canvas.Undo(); canvas.SelectObject(canvas.Objects[0]);
        canvas.Image.Save(Path.Combine(output, "annotation-export-preview.png"));
        using (var preview = new Bitmap(canvas.Width, canvas.Height)) { canvas.DrawToBitmap(preview, canvas.ClientRectangle); preview.Save(Path.Combine(output, "annotation-selection-preview.png")); }
        host.Hide();

        using var maskCanvas = new EditorCanvas(White(120, 100));
        maskCanvas.AddAnnotation(new Annotation { Kind = EditTool.Rectangle, Bounds = new RectangleF(20, 20, 60, 50), Width = 8, Color = Color.Red });
        maskCanvas.AddAnnotation(new Annotation { Kind = EditTool.Mosaic, Bounds = new RectangleF(10, 10, 90, 80) });
        check(maskCanvas.Objects.Count == 2 && maskCanvas.Image.GetPixel(80, 40).ToArgb() != Color.Red.ToArgb(), "Mosaic export masks preceding annotations while retaining editable objects");
        maskCanvas.DeleteSelected(); check(maskCanvas.Image.GetPixel(80, 40).ToArgb() == Color.Red.ToArgb(), "Deleting mosaic restores unchanged underlying annotation");
        maskCanvas.Undo(); check(maskCanvas.Objects.Count == 2, "Mosaic deletion can be undone");

        canvas.SelectObject(null); canvas.Tool = EditTool.Arrow;
        Drag(canvas, new Point(300, 220), new Point(450, 220));
        check(canvas.Objects[^1].Kind == EditTool.Arrow && canvas.SelectionHandles().Length == 2, "Arrow exposes two editable endpoint handles");
        canvas.Tool = EditTool.Select; Drag(canvas, new Point(450, 220), new Point(470, 280));
        check(canvas.Objects[^1].World(canvas.Objects[^1].Points[^1]) == new PointF(470, 280) && canvas.Objects[^1].World(canvas.Objects[^1].Points[0]) == new PointF(300, 220), "Dragging horizontal arrow endpoint changes angle while keeping opposite end fixed");
        canvas.Undo(); check(canvas.Objects[^1].World(canvas.Objects[^1].Points[^1]) == new PointF(450, 220), "Arrow endpoint edit can be undone");
        canvas.SelectObject(canvas.Objects[^1]);
        Drag(canvas, new Point(450, 220), new Point(280, 180));
        check(canvas.Objects[^1].World(canvas.Objects[^1].Points[^1]) == new PointF(280, 180), "Arrow endpoint can cross the other endpoint without losing its direction");
        canvas.Image.Save(Path.Combine(output, "annotation-arrow-preview.png"));
    }
    static bool PixelsEqual(Bitmap a, Bitmap b)
    {
        if (a.Size != b.Size) return false;
        for (int y = 0; y < a.Height; y++) for (int x = 0; x < a.Width; x++) if (a.GetPixel(x, y) != b.GetPixel(x, y)) return false;
        return true;
    }
}
