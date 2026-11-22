using System.Drawing.Imaging;

namespace SnapDesk;

internal static class SelfTest
{
    internal static void Run()
    {
        var log = new List<string>();
        var temp = Path.Combine(Path.GetTempPath(), "SnapDesk-test-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temp);
        void Check(bool condition, string name) { if (!condition) throw new Exception("FAIL: " + name); log.Add("PASS: " + name); }
        try
        {
            Check(CaptureOverlay.Normalize(new Point(80, 90), new Point(10, 20)) == new Rectangle(10, 20, 70, 70), "Reverse drag normalizes region");
            Check(CaptureOverlay.PathBounds([new Point(10, 10), new Point(90, 10), new Point(90, 70), new Point(10, 70), new Point(10, 11)]) == new Rectangle(10, 10, 81, 61), "Closed freehand lasso retains entire path bounds");
            using (var e = new EditorCanvas(new Bitmap(120, 80)))
            {
                e.Rotate(RotateFlipType.Rotate90FlipNone); Check(e.Image.Size == new Size(80, 120) && e.CanUndo, "Rotate changes dimensions and creates undo history");
                e.Undo(); Check(e.Image.Size == new Size(120, 80) && e.CanRedo, "Undo restores original dimensions");
                e.Redo(); Check(e.Image.Size == new Size(80, 120), "Redo restores rotated image");
                e.Undo(); e.ResizeImage(new Size(60, 40)); Check(e.Image.Size == new Size(60, 40) && !e.CanRedo, "New edit clears redo history");
                e.Undo(); Check(e.Image.Size == new Size(120, 80), "Resize can be undone");
            }
            using (var image = new Bitmap(40, 40))
            {
                for (int y = 0; y < image.Height; y++) for (int x = 0; x < image.Width; x++) image.SetPixel(x, y, Color.FromArgb(x * 6, y * 6, 50));
                var outside = image.GetPixel(1, 1); EditorCanvas.Pixelate(image, new Rectangle(5, 5, 19, 21));
                Check(image.GetPixel(1, 1) == outside, "Mosaic does not alter pixels outside selection");
                Check(image.GetPixel(5, 5) == image.GetPixel(18, 18) && image.GetPixel(23, 25) != Color.FromArgb(138, 150, 50), "Mosaic replaces full and partial edge blocks");
            }
            using (var image = new Bitmap(15, 12))
            {
                image.SetPixel(0, 0, Color.Transparent); image.SetPixel(5, 5, Color.Red);
                foreach (string ext in new[] { ".png", ".jpg", ".bmp" })
                {
                    string path = Path.Combine(temp, "export" + ext); MainForm.SaveBitmap(image, path);
                    using var decoded = new Bitmap(path); Check(decoded.Size == image.Size, "Export decodes with correct dimensions: " + ext);
                    if (ext == ".png") Check(decoded.GetPixel(0, 0).A == 0, "PNG preserves transparency");
                    if (ext == ".bmp") Check(decoded.GetPixel(0, 0).ToArgb() == Color.White.ToArgb(), "BMP flattens transparency onto white");
                }
                string overwrite = Path.Combine(temp, "export.png"); MainForm.SaveBitmap(image, overwrite); Check(new FileInfo(overwrite).Length > 0, "Existing destination can be replaced");
            }
            using (var content = new Bitmap(180, 650))
            {
                var random = new Random(42);
                for (int y = 0; y < content.Height; y++) for (int x = 0; x < content.Width; x++) content.SetPixel(x, y, Color.FromArgb(random.Next(256), random.Next(256), random.Next(256)));
                using var first = content.Clone(new Rectangle(0, 0, 180, 360), PixelFormat.Format32bppArgb);
                using var second = content.Clone(new Rectangle(0, 137, 180, 360), PixelFormat.Format32bppArgb);
                Check(ScrollCapture.FindShift(first, second) == 137, "Scroll overlap finds actual 137 pixel displacement");
                Check(ScrollCapture.FindShift(first, first) == 0, "Unchanged screen stops scrolling");
                using var stitched = ScrollCapture.Stitch([(first, 0), (second, 137)]);
                Check(stitched.Size == new Size(180, 497) && stitched.GetPixel(90, 400) == content.GetPixel(90, 400), "Stitch preserves content and combined dimensions");
                using var blank = new Bitmap(180, 360); using (var g = Graphics.FromImage(blank)) g.Clear(Color.White);
                Check(ScrollCapture.FindShift(first, blank) == -1, "Unmatched content refuses unsafe stitch");
            }
            Check(Math.Abs(AccessoryOverlay.Angle(new Point(10, 0), Point.Empty, new Point(0, 10)) - 90) < .001, "Protractor returns right angle");
            Check(AccessoryOverlay.Angle(Point.Empty, Point.Empty, Point.Empty) == 0, "Protractor handles coincident points");
            using (var host = new Form { ClientSize = new Size(1000, 220), Font = new Font("Microsoft JhengHei UI", 10) })
            using (var tabs = new DocumentTabs { Dock = DockStyle.Fill })
            {
                host.Controls.Add(tabs); var home = new TabPage("開始"); tabs.TabPages.Add(home);
                TabPage AddPage(string name)
                {
                    var editor = new EditorCanvas(new Bitmap(20, 20));
                    var page = new TabPage(name) { Tag = editor }; page.Controls.Add(editor); tabs.TabPages.Add(page); return page;
                }
                var first = AddPage("截圖 1 *"); var middle = AddPage("截圖 2 *"); var last = AddPage("截圖 3 *");
                _ = host.Handle; _ = tabs.Handle; host.PerformLayout(); tabs.SelectedTab = middle;
                Check(tabs.GetCloseTargets(middle, CloseScope.Current).SequenceEqual([middle]), "Close-current targets clicked tab");
                Check(tabs.GetCloseTargets(middle, CloseScope.Left).SequenceEqual([first]), "Close-left excludes home and clicked tab");
                Check(tabs.GetCloseTargets(middle, CloseScope.Right).SequenceEqual([last]), "Close-right excludes clicked tab");
                Check(tabs.GetCloseTargets(first, CloseScope.Left).Length == 0 && tabs.GetCloseTargets(last, CloseScope.Right).Length == 0, "Edge tabs have no invalid left or right targets");
                Check(tabs.GetCloseTargets(middle, CloseScope.All).SequenceEqual([first, middle, last]) && tabs.GetCloseTargets(home, CloseScope.All).Length == 0, "Close-all includes images and protects home");
                Check(tabs.CloseBounds(0).IsEmpty && !tabs.CloseBounds(1).IsEmpty, "Close buttons appear on image tabs only");
                // Drive this control's own managed mouse handlers without desktop input or screen capture.
                TabPage[]? requested = null; tabs.CloseRequested += pages => requested = pages;
                var close = tabs.CloseBounds(1); var point = new Point(close.Left + close.Width / 2, close.Top + close.Height / 2);
                void Mouse(string method, Point p)
                {
                    typeof(DocumentTabs).GetMethod(method, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(tabs, [new MouseEventArgs(MouseButtons.Left, 1, p.X, p.Y, 0)]);
                }
                var repaints = new List<Rectangle>();
                InvalidateEventHandler trackRepaint = (_, e) => repaints.Add(e.InvalidRect);
                tabs.Invalidated += trackRepaint;
                var header = tabs.GetTabRect(1);
                for (int i = 0; i < 100; i++) Mouse("OnMouseMove", new Point(header.Left + 10 + i % 20, header.Top + 10));
                Check(repaints.Count == 0, "Moving across tab text does not invalidate the tab control");
                Mouse("OnMouseMove", point);
                Check(repaints.Count == 1 && repaints[0] == close, "Entering X repaints only its small button rectangle");
                for (int i = 0; i < 100; i++) Mouse("OnMouseMove", new Point(point.X + i % 2, point.Y));
                Check(repaints.Count == 1, "Movement within the same X does not cause repeated repaints");
                var nextClose = tabs.CloseBounds(2); var nextPoint = new Point(nextClose.Left + nextClose.Width / 2, nextClose.Top + nextClose.Height / 2);
                Mouse("OnMouseMove", nextPoint);
                Check(repaints.Count == 3 && repaints[1] == close && repaints[2] == nextClose, "Moving between X buttons repaints only the two affected buttons");
                typeof(DocumentTabs).GetMethod("OnMouseLeave", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(tabs, [EventArgs.Empty]);
                Check(repaints.Count == 4 && repaints[^1] == nextClose, "Leaving tab strip repaints only the last hovered X");
                tabs.Invalidated -= trackRepaint;
                Mouse("OnMouseDown", point); Mouse("OnMouseUp", point);
                Check(requested?.SequenceEqual([first]) == true, "X click requests closure of the clicked tab");
                requested = null; Mouse("OnMouseDown", point); Mouse("OnMouseUp", new Point(close.Left - 12, close.Top + 4));
                Check(requested == null, "Releasing outside X does not close a tab");
                tabs.SelectedTab = first;
                using (var menu = tabs.CreateCloseMenu(middle))
                {
                    Check(menu.Items.Count == 4 && menu.Items.Cast<ToolStripItem>().All(i => i.Enabled), "Middle tab context menu offers all four close actions");
                    menu.Items[3].PerformClick();
                    Check(requested?.SequenceEqual([last]) == true, "Context close-right uses right-clicked tab instead of selected tab");
                }
                using (var menu = tabs.CreateCloseMenu(first)) Check(!menu.Items[2].Enabled, "No-left-tabs context action is disabled");
                tabs.SelectedTab = middle;
                using (var preview = new Bitmap(tabs.Width, tabs.Height)) { tabs.DrawToBitmap(preview, tabs.ClientRectangle); preview.Save(Path.Combine(TestArtifacts.Directory, "test-tabs-preview.png")); }
                int prompts = 0;
                Check(!tabs.TryCloseDocuments(tabs.GetCloseTargets(middle, CloseScope.All), p => { Check(tabs.SelectedTab == p, "Save confirmation uses correct image tab"); return ++prompts < 2; }), "Cancel aborts batch close");
                Check(tabs.TabCount == 4 && !first.IsDisposed && !middle.IsDisposed && !last.IsDisposed && tabs.SelectedTab == middle, "Cancel preserves every tab and original selection");
                try { tabs.TryCloseDocuments([first, middle], _ => throw new IOException("Simulated save failure")); } catch (IOException) { }
                Check(tabs.TabCount == 4 && tabs.SelectedTab == middle, "Save failure leaves documents open");
                Check(tabs.TryCloseDocuments([first, first], _ => true) && first.IsDisposed && ((EditorCanvas)first.Tag!).IsDisposed && tabs.SelectedTab == middle, "Close disposes image resources once and preserves surviving selection");
                Check(tabs.TryCloseDocuments(tabs.GetCloseTargets(middle, CloseScope.Right), _ => true) && last.IsDisposed && !middle.IsDisposed, "Close-right removes only right-side images");
                Check(tabs.TryCloseDocuments([home, middle], _ => true) && tabs.TabCount == 1 && tabs.SelectedTab == home && !home.IsDisposed, "Closing all images returns to protected home tab");
            }
            AnnotationTests.Run(Check, TestArtifacts.Directory);
            ScrollCaptureTests.Run(Check);
            ShapeTests.Run(Check, TestArtifacts.Directory);
            RecordingTests.CheckOptions(Check);
            int copyInvocations = 0;
            using (var imageMenu = new ImageContextMenu(() => copyInvocations++))
            {
                Check(copyInvocations == 0 && imageMenu.Items[0].Text == "複製圖片", "Image menu offers copy without copying on creation");
                imageMenu.Items[0].PerformClick();
                Check(copyInvocations == 1, "Image copies only after choosing copy menu item");
            }
            using (var ribbon = new EditorRibbon())
            {
                int changes = 0; ribbon.ToolSelected += _ => changes++; ribbon.WidthSelected += _ => changes++; ribbon.TextSizeSelected += _ => changes++; ribbon.ColorSelected += _ => changes++;
                ribbon.SetState(EditTool.Ellipse, Color.Blue, 8, 36);
                Check(changes == 0, "Ribbon state synchronization does not trigger editing commands");
            }
            using (var previewForm = new MainForm(preview: true))
            {
                previewForm.ClientSize = new Size(1280, 720);
                void Prepare(Control control) { _ = control.Handle; foreach (Control child in control.Controls) Prepare(child); control.PerformLayout(); }
                Prepare(previewForm);
                previewForm.ShowInTaskbar = false; previewForm.StartPosition = FormStartPosition.Manual;
                previewForm.Location = new Point(-30000, -30000); previewForm.Show(); Application.DoEvents();
                IEnumerable<Control> Descendants(Control control) => control.Controls.Cast<Control>().SelectMany(c => new[] { c }.Concat(Descendants(c)));
                var sizeControl = Descendants(previewForm).OfType<ComboBox>().Single(c => c.AccessibleName == "文字大小");
                Check(sizeControl.SelectedItem?.ToString() == "24 字", "Ribbon text-size control retains default after window creation");
                previewForm.AddDocument(AnnotationTests.White(960, 430), "可編輯標註示範", dirty: false);
                var tabControl = previewForm.Controls.OfType<DocumentTabs>().Single();
                var firstPage = tabControl.SelectedTab!; var scene = (EditorCanvas)firstPage.Tag!;
                var rectangle = new Annotation { Kind = EditTool.Rectangle, Bounds = new RectangleF(70, 70, 240, 125), Color = Color.FromArgb(27, 111, 175), Width = 4 };
                var label = new Annotation { Kind = EditTool.Text, Bounds = new RectangleF(new PointF(365, 100), Annotation.Measure("雙擊文字，繼續編輯", 24)), Text = "雙擊文字，繼續編輯", Color = Color.DarkGreen, FontSize = 24, Points = [new(0, 0), new(1, 0), new(0, 1)] };
                scene.AddAnnotation(rectangle); scene.AddAnnotation(label); scene.SelectObject(rectangle);
                var widthControl = Descendants(previewForm).OfType<ComboBox>().Single(c => c.AccessibleName == "筆畫寬度");
                Check(widthControl.SelectedItem?.ToString() == "4 px", "Selecting existing shape loads its thickness into ribbon");
                widthControl.SelectedItem = "12 px";
                Check(rectangle.Width == 12 && label.Width == 4, "Ribbon thickness changes only selected annotation");
                scene.SelectObject(label); sizeControl.SelectedItem = "36 字";
                Check(label.FontSize == 36 && rectangle.FontSize == 24, "Ribbon font size updates selected text without changing other objects");
                var boldControl = Descendants(previewForm).OfType<RibbonButton>().Single(c => c.Text == "粗體"); boldControl.PerformClick();
                Check(!label.Bold && rectangle.Bold, "Ribbon bold button updates only selected text");
                var redControl = Descendants(previewForm).OfType<Button>().Single(c => c.AccessibleName?.StartsWith("色碼 ") == true && c.BackColor == Color.Red); redControl.PerformClick();
                Check(label.Color == Color.Red && rectangle.Color != Color.Red, "Palette updates existing selected object color");
                previewForm.AddDocument(AnnotationTests.White(320, 200), "另一張截圖", dirty: false);
                var secondScene = (EditorCanvas)tabControl.SelectedTab!.Tag!;
                var ellipse = new Annotation { Kind = EditTool.Ellipse, Bounds = new RectangleF(20, 20, 90, 60), Color = Color.Blue, Width = 8 };
                secondScene.AddAnnotation(ellipse); widthControl.SelectedItem = "2 px";
                Check(ellipse.Width == 2 && rectangle.Width == 12 && label.Color == Color.Red, "Editing another screenshot leaves previous screenshot annotations unchanged");
                tabControl.SelectedTab = firstPage; scene.SelectObject(rectangle);
                var moveControl = Descendants(previewForm).OfType<RibbonButton>().Single(c => c.Text == "移動"); moveControl.PerformClick();
                Check(scene.Tool == EditTool.Select && scene.SelectedObject == rectangle, "Move tool preserves selected object for resizing");
                var fillTarget=Descendants(previewForm).OfType<Button>().Single(c=>c.AccessibleName?.StartsWith("填滿色，")==true);
                var outlineTarget=Descendants(previewForm).OfType<Button>().Single(c=>c.AccessibleName?.StartsWith("外框色，")==true);
                var goldControl=Descendants(previewForm).OfType<Button>().Single(c=>c.AccessibleName?.StartsWith("色碼 ")==true&&c.BackColor==Color.Gold);
                var outlineBefore=rectangle.Color;fillTarget.PerformClick();goldControl.PerformClick();
                Check(rectangle.Filled&&rectangle.FillColor==Color.Gold&&rectangle.Color==outlineBefore,"Fill-color target routes palette to fill without replacing outline color");
                outlineTarget.PerformClick();redControl.PerformClick();
                Check(rectangle.Color==Color.Red&&rectangle.FillColor==Color.Gold&&label.Color==Color.Red,"Outline-color target keeps fill and other annotations unchanged");
                previewForm.PerformLayout(); Application.DoEvents();
                using var preview = new Bitmap(previewForm.Width, previewForm.Height);
                previewForm.DrawToBitmap(preview, new Rectangle(Point.Empty, preview.Size));
                preview.Save(Path.Combine(TestArtifacts.Directory, "interface-preview.png"));
                previewForm.Hide();
            }
            using(var recorderPreview=new RecordingForm())
            {
                recorderPreview.ShowInTaskbar=false;recorderPreview.StartPosition=FormStartPosition.Manual;recorderPreview.Location=new Point(-30000,-30000);recorderPreview.Show();Application.DoEvents();
                using var preview=new Bitmap(recorderPreview.Width,recorderPreview.Height);recorderPreview.DrawToBitmap(preview,new Rectangle(Point.Empty,preview.Size));preview.Save(Path.Combine(TestArtifacts.Directory,"recording-controls-preview.png"));recorderPreview.Hide();
            }
            log.Add($"All {log.Count} checks passed."); Environment.ExitCode = 0;
        }
        catch (Exception ex) { log.Add(ex.ToString()); Environment.ExitCode = 1; }
        finally { File.WriteAllLines(Path.Combine(TestArtifacts.Directory, "test-results.txt"), log); Directory.Delete(temp, true); }
    }
}
