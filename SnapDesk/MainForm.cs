using System.Drawing.Imaging;
using System.Drawing.Drawing2D;
using System.Drawing.Printing;
using System.Text.Json;

namespace SnapDesk;

internal sealed class Preferences
{
    public int Delay { get; set; }
    public bool AutoSave { get; set; }
    public string SaveFolder { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "SnapDesk");
    public bool MinimizeToTray { get; set; } = true;
    public int FixedWidth { get; set; } = 800;
    public int FixedHeight { get; set; } = 600;
    public int ScrollMaxSteps { get; set; } = ScrollCapture.DefaultMaxSteps;
    public static readonly string SettingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SnapDesk", "settings.json");
    public static Preferences Load() { try { return JsonSerializer.Deserialize<Preferences>(File.ReadAllText(SettingsPath)) ?? new(); } catch { return new(); } }
    public void Save() { Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!); File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true })); }
}

internal sealed class MainForm : Form
{
    readonly DocumentTabs tabs = new() { Dock = DockStyle.Fill };
    readonly ToolStripStatusLabel status = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
    readonly ToolStripStatusLabel dimensions = new();
    EditorRibbon? ribbon;
    readonly Preferences prefs = Preferences.Load();
    readonly NotifyIcon tray = new();

    EditTool activeTool = EditTool.Arrow;
    Color ink = Color.FromArgb(244, 83, 87);
    int strokeWidth = 4, fontSize = 24;
    bool fontBold = true;
    ShapePreset activeShape=new("矩形",EditTool.Rectangle);
    Color fillColor=Color.White;
    bool filled,outlined=true;
    DashStyle dash=DashStyle.Solid;
    bool busy, exitRequested;
    Rectangle lastCapture;
    int docIndex;
    RecordingForm? recording;
    EditorCanvas? Editor => tabs.SelectedTab?.Tag as EditorCanvas;

    public MainForm(bool preview = false)
    {
        Text = "SnapDesk · 截圖與圖片工具"; MinimumSize = new Size(1050, 720); Size = new Size(1380, 900); StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Microsoft JhengHei UI", 10); BackColor = Color.FromArgb(241, 245, 249); Icon = AppIcon.Load(); KeyPreview = true; AllowDrop = true;
        BuildInterface();
        tabs.CloseRequested += pages => Safe(() => tabs.TryCloseDocuments(pages, _ => ConfirmClose()));
        tabs.SelectedIndexChanged += (_, _) => { ReadSelectionStyle(); UpdateStatus(); };
        DragEnter += (_, e) => e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true ? DragDropEffects.Copy : DragDropEffects.None;
        DragDrop += (_, e) => { if (e.Data?.GetData(DataFormats.FileDrop) is string[] files) foreach (var f in files) OpenFile(f); };
        FormClosing += ClosingApp;
        Resize += (_, _) => { if (WindowState == FormWindowState.Minimized && prefs.MinimizeToTray) { Hide(); tray.ShowBalloonTip(1500, "SnapDesk 已縮到通知區", "Ctrl + Shift + 2 可隨時選取截圖範圍。", ToolTipIcon.Info); } };
        if (preview) return;
        var menu = new ContextMenuStrip(); menu.Items.Add("開啟 SnapDesk", null, (_, _) => Restore());
        menu.Items.Add("範圍截圖", null, async (_, _) => await CaptureAsync(CaptureMode.Region));
        menu.Items.Add("全螢幕截圖", null, async (_, _) => await CaptureAsync(CaptureMode.FullScreen));
        menu.Items.Add("螢幕錄影…",null,(_,_)=>OpenRecorder());
        menu.Items.Add("離開", null, (_, _) => { exitRequested = true; Close(); });
        tray.Icon = Icon; tray.Text = "SnapDesk · Ctrl+Shift+2 範圍截圖"; tray.ContextMenuStrip = menu; tray.Visible = true; tray.DoubleClick += (_, _) => Restore();
        Shown += (_, _) => { var conflicts = new List<string>(); for (int i = 1; i <= 3; i++) if (!Native.RegisterHotKey(Handle, i, 0x4006, (uint)(Keys.D0 + i))) conflicts.Add($"Ctrl+Shift+{i}"); status.Text = conflicts.Count > 0 ? $"快捷鍵被占用：{string.Join("、", conflicts)}。仍可使用工具列截圖。" : "就緒  ·  Ctrl+Shift+1 全螢幕  /  Ctrl+Shift+2 範圍  /  Ctrl+Shift+3 視窗"; };
    }

    ToolStripMenuItem MenuItem(string text, Action action, Keys shortcut = Keys.None) { var item = new ToolStripMenuItem(text, null, (_, _) => Safe(action)); item.ShortcutKeys = shortcut; return item; }
    void Safe(Action action) { try { action(); } catch (Exception ex) { MessageBox.Show(this, ex.Message, "SnapDesk", MessageBoxButtons.OK, MessageBoxIcon.Warning); } }
    void BuildInterface()
    {
        var menu = new MenuStrip { AccessibleName = "主選單", Font = Font, BackColor = Color.White, Padding = new Padding(10, 5, 10, 5) };
        var file = new ToolStripMenuItem("檔案");
        file.DropDownItems.AddRange([MenuItem("新增白色畫布", NewBlank, Keys.Control | Keys.N), MenuItem("開啟圖片…", Open, Keys.Control | Keys.O), MenuItem("貼上圖片", Paste, Keys.Control | Keys.V), new ToolStripSeparator(), MenuItem("儲存", () => Save(false), Keys.Control | Keys.S), MenuItem("另存新檔…", () => Save(true), Keys.Control | Keys.Shift | Keys.S), MenuItem("複製圖片", Copy, Keys.Control | Keys.C), MenuItem("列印…", Print, Keys.Control | Keys.P), new ToolStripSeparator(), MenuItem("關閉目前圖片", CloseDocument, Keys.Control | Keys.W), MenuItem("離開", () => { exitRequested = true; Close(); })]);
        var edit = new ToolStripMenuItem("編輯"); edit.DropDownItems.AddRange([MenuItem("復原", () => Editor?.Undo(), Keys.Control | Keys.Z), MenuItem("重做", () => Editor?.Redo(), Keys.Control | Keys.Y), MenuItem("調整圖片大小…", ResizeImage), MenuItem("順時針旋轉 90°", () => Editor?.Rotate(RotateFlipType.Rotate90FlipNone)), MenuItem("水平翻轉", () => Editor?.Rotate(RotateFlipType.RotateNoneFlipX)), MenuItem("垂直翻轉", () => Editor?.Rotate(RotateFlipType.RotateNoneFlipY))]);
        var capture = new ToolStripMenuItem("擷取");
        foreach (var pair in CaptureLabels()) { var mode = pair.Item2; var item = new ToolStripMenuItem(pair.Item1); item.Click += async (_, _) => await CaptureAsync(mode); capture.DropDownItems.Add(item); }
        var accessories = new ToolStripMenuItem("螢幕工具");
        foreach (var pair in new[] { ("螢幕取色器", AccessoryMode.Color), ("放大鏡", AccessoryMode.Magnifier), ("像素尺", AccessoryMode.Ruler), ("十字座標線", AccessoryMode.Crosshair), ("量角器", AccessoryMode.Protractor), ("螢幕白板", AccessoryMode.Whiteboard) })
        { var mode = pair.Item2; accessories.DropDownItems.Add(MenuItem(pair.Item1, () => LaunchAccessory(mode))); }
        menu.Items.AddRange([file, edit, capture, accessories, MenuItem("錄影",OpenRecorder), MenuItem("設定", Settings), MenuItem("關於", () => MessageBox.Show(this, "SnapDesk 0.1.9\nWindows 截圖、圖片編輯與螢幕錄影\n\nCtrl+Shift+1：全螢幕截圖\nCtrl+Shift+2：範圍截圖\nCtrl+Shift+3：選取視窗截圖\n錄影時 Ctrl+Shift+F9：暫停／繼續\nCtrl+Shift+F10：停止並儲存 MP4\n\n目前未提供雲端上傳與 OCR。", "關於 SnapDesk"))]);
        var top = new ToolStrip { AccessibleName = "截圖與檔案", Font = Font, GripStyle = ToolStripGripStyle.Hidden, BackColor = Color.FromArgb(232, 241, 247), Padding = new Padding(12, 9, 12, 9) };

        var newCapture = new ToolStripDropDownButton("＋  新增截圖"); foreach (var pair in CaptureLabels()) { var mode = pair.Item2; newCapture.DropDownItems.Add(pair.Item1, null, async (_, _) => await CaptureAsync(mode)); }
        top.Items.Add(newCapture); AddButton(top, "範圍截圖", async () => await CaptureAsync(CaptureMode.Region)); AddButton(top, "全螢幕", async () => await CaptureAsync(CaptureMode.FullScreen));
        AddButton(top,"● 錄影",OpenRecorder);
        top.Items.Add(new ToolStripSeparator()); AddButton(top, "開啟", Open); AddButton(top, "貼上", Paste); AddButton(top, "儲存", () => Save(false)); AddButton(top, "複製", Copy); top.Items.Add(new ToolStripSeparator()); AddButton(top, "復原", () => Editor?.Undo()); AddButton(top, "重做", () => Editor?.Redo());
        var delay = new ToolStripComboBox { AccessibleName = "截圖延遲", DropDownStyle = ComboBoxStyle.DropDownList, Width = 105 }; delay.Items.AddRange(["不延遲", "延遲 3 秒", "延遲 5 秒", "延遲 10 秒"]); delay.SelectedIndex = Array.IndexOf(new[] { 0, 3, 5, 10 }, prefs.Delay); if (delay.SelectedIndex < 0) delay.SelectedIndex = 0;
        delay.SelectedIndexChanged += (_, _) => { prefs.Delay = new[] { 0, 3, 5, 10 }[delay.SelectedIndex]; Safe(prefs.Save); }; top.Items.Add(new ToolStripSeparator()); top.Items.Add(delay);
        var ribbonControl = BuildRibbon();
        var footer = new StatusStrip { AccessibleName = "圖片狀態", BackColor = Color.White, Padding = new Padding(10, 4, 10, 4) }; footer.Items.Add(status); footer.Items.Add(dimensions);
        var zoom = new ToolStripDropDownButton("縮放"); foreach (int percent in new[] { 25, 50, 75, 100, 150, 200, 400 }) { int p = percent; zoom.DropDownItems.Add($"{p}%", null, (_, _) => { Editor?.SetZoom(p / 100f); UpdateStatus(); }); } zoom.DropDownItems.Add("符合視窗", null, (_, _) => Fit()); footer.Items.Add(zoom);
        var home = new TabPage("開始") { BackColor = BackColor, Padding = new Padding(35) };
        var welcome = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
        welcome.Controls.Add(new Label { Text = "SnapDesk", Font = new Font("Segoe UI", 34, FontStyle.Bold), ForeColor = Color.FromArgb(19, 47, 72), AutoSize = true, Margin = new Padding(0, 25, 0, 0) });
        welcome.Controls.Add(new Label { Text = "擷取畫面，讓重點一目了然。", Font = new Font(Font.FontFamily, 18), AutoSize = true, Margin = new Padding(0, 8, 0, 30) });
        foreach (var pair in new[] { ("選取範圍    Ctrl + Shift + 2", CaptureMode.Region), ("擷取全螢幕    Ctrl + Shift + 1", CaptureMode.FullScreen), ("選取視窗    Ctrl + Shift + 3", CaptureMode.Window), ("捲動長截圖（實驗性）", CaptureMode.Scrolling) })
        { var mode = pair.Item2; var b = new Button { Text = pair.Item1, Size = new Size(400, 52), FlatStyle = FlatStyle.Flat, BackColor = Color.White, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(14, 0, 0, 0), Margin = new Padding(0, 0, 0, 12) }; b.FlatAppearance.BorderColor = Color.FromArgb(207, 218, 230); b.Click += async (_, _) => await CaptureAsync(mode); welcome.Controls.Add(b); }
        welcome.Controls.Add(new Label { Text = "也可以拖入圖片、按 Ctrl+V 貼上，或建立白色畫布。\n截圖後可加箭頭、文字、編號、馬賽克，並儲存 PNG / JPEG / BMP。\n\n縮小視窗後可從右下角通知區開啟。", AutoSize = true, ForeColor = Color.FromArgb(80, 96, 115), Margin = new Padding(0, 18, 0, 0) });
        home.Controls.Add(welcome); tabs.TabPages.Add(home);
        Controls.Add(tabs); Controls.Add(ribbonControl); Controls.Add(top); Controls.Add(menu); Controls.Add(footer); tabs.BringToFront(); MainMenuStrip = menu;
        ribbonControl.Dock = top.Dock = menu.Dock = DockStyle.Top;
    }
    static (string, CaptureMode)[] CaptureLabels() => [("範圍截圖", CaptureMode.Region), ("全螢幕（所有螢幕）", CaptureMode.FullScreen), ("選取視窗", CaptureMode.Window), ("固定大小範圍", CaptureMode.Fixed), ("任意形狀範圍", CaptureMode.Freehand), ("重複上次範圍", CaptureMode.Repeat), ("捲動長截圖（實驗性）", CaptureMode.Scrolling)];
    void AddButton(ToolStrip strip, string text, Action action) { var b = new ToolStripButton(text); b.Click += (_, _) => Safe(action); strip.Items.Add(b); }
    void Restore() { Show(); WindowState = FormWindowState.Normal; Activate(); }
    void OpenRecorder()
    {
        if(busy)return;
        if(recording is {IsDisposed:false}){recording.Reveal();return;}
        recording=new RecordingForm();recording.HideEditor+=Hide;recording.RestoreEditor+=Restore;
        recording.FormClosed+=(_,_)=>recording=null;recording.Show();
    }
    void SelectTool(EditTool tool) { activeTool = tool; if (tool != EditTool.Select) Editor?.SelectObject(null); SyncTools(); status.Text = tool == EditTool.Select ? "點選物件後可拖曳移動、拉控制點縮放，並修改色彩與大小。文字可雙擊或按 F2 編輯。" : tool == EditTool.Crop ? "拖曳框選要保留的區域，放開滑鼠完成裁切；Esc 取消。" : tool == EditTool.Text ? "點選空白處新增文字；雙擊既有文字可編輯內容。" : "拖曳繪製；畫完仍可選取、移動、縮放與修改樣式。"; }
    void SyncTools() { ribbon?.SetState(activeTool, ink, strokeWidth, fontSize, fontBold,fillColor,filled,outlined,dash); foreach (TabPage page in tabs.TabPages) if (page.Tag is EditorCanvas e) { e.Tool = activeTool; e.Ink = ink; e.StrokeWidth = strokeWidth; e.TextSize = fontSize; e.TextBold = fontBold; e.ShapeKind=activeShape.Shape; e.CustomPoints=activeShape.Points; e.FillColor=fillColor; e.Filled=filled; e.Outlined=outlined; e.StrokeDash=dash; } }
    EditorRibbon BuildRibbon()
    {
        ribbon = new EditorRibbon();
        ribbon.ToolSelected += SelectTool;
        ribbon.ColorSelected += c => Safe(() => { ink = c; outlined=true; Editor?.UpdateSelectedStyle(color: c,outlined:true); SyncTools(); });
        ribbon.FillColorSelected+=c=>Safe(()=>{fillColor=c;filled=true;Editor?.UpdateSelectedStyle(fillColor:c,filled:true);SyncTools();});
        ribbon.FillSelected+=value=>Safe(()=>{filled=value;if(!filled&&!outlined)outlined=true;Editor?.UpdateSelectedStyle(filled:filled,outlined:outlined);SyncTools();});
        ribbon.OutlineSelected+=(value,style)=>Safe(()=>{outlined=value;dash=style;if(!outlined&&!filled)filled=true;Editor?.UpdateSelectedStyle(outlined:outlined,filled:filled,dash:dash);SyncTools();});
        ribbon.ShapeSelected+=preset=>{activeShape=preset;SelectTool(preset.Tool);};
        ribbon.AddCustomShape+=()=>Safe(()=>
        {
            if(Editor?.SelectedObject is not { } a || !ShapeGeometry.IsClosed(a.Kind)) { MessageBox.Show(this,"請先畫出並選取一個封閉圖形，再加入自訂。","自訂形狀");return; }
            string? name=Dialogs.Text(this,"自訂形狀名稱","我的圖形"); if(!string.IsNullOrWhiteSpace(name)) {CustomShapes.Save(a,name.Trim());status.Text="已加入自訂形狀；下次開啟圖形選單即可使用。";}
        });
        ribbon.WidthSelected += w => Safe(() => { strokeWidth = w; Editor?.UpdateSelectedStyle(width: w); SyncTools(); });
        ribbon.TextSizeSelected += size => Safe(() => { fontSize = size; Editor?.UpdateSelectedStyle(fontSize: size); SyncTools(); });
        ribbon.BoldSelected += bold => Safe(() => { fontBold = bold; Editor?.UpdateSelectedStyle(bold: bold); SyncTools(); });
        ribbon.Command += command => Safe(() =>
        {
            switch (command)
            {
                case RibbonCommand.Paste: Paste(); break;
                case RibbonCommand.Copy: Copy(); break;
                case RibbonCommand.Resize: ResizeImage(); break;
                case RibbonCommand.Rotate: Editor?.Rotate(RotateFlipType.Rotate90FlipNone); break;
                case RibbonCommand.FlipHorizontal: Editor?.Rotate(RotateFlipType.RotateNoneFlipX); break;
                case RibbonCommand.FlipVertical: Editor?.Rotate(RotateFlipType.RotateNoneFlipY); break;
            }
        });
        SyncTools(); return ribbon;
    }
    void ReadSelectionStyle()
    {
        if (Editor?.SelectedObject is not { } a) return;
        ink = a.Color; strokeWidth = (int)Math.Round(a.Width); fontSize = a.FontSize; fontBold = a.Bold;
        fillColor=a.FillColor;filled=a.Filled;outlined=a.Outlined;dash=a.DashStyle;
        SyncTools();
    }
    internal void AddDocument(Bitmap image, string name, string? filePath = null, bool dirty = true)
    {
        var canvas = new EditorCanvas(image) { AccessibleName = "圖片編輯畫布", Dirty = dirty, FilePath = filePath, Location = new Point(24, 24) };
        var panel = new Panel { AccessibleName = "圖片檢視區", Dock = DockStyle.Fill, AutoScroll = true, BackColor = Color.FromArgb(218, 225, 234) }; panel.Controls.Add(canvas);
        var page = new TabPage(name + (dirty ? " *" : "")) { Tag = canvas }; page.Controls.Add(panel); tabs.TabPages.Add(page); tabs.SelectedTab = page;
        canvas.Changed += (_, _) => { if (!page.Text.EndsWith(" *")) page.Text += " *"; UpdateStatus(); };
        canvas.SelectionChanged += (_, _) => { if (tabs.SelectedTab == page) ReadSelectionStyle(); };
        canvas.ColorPicked += c => { ink = c; SyncTools(); status.Text = $"已選取 {ColorTranslator.ToHtml(c)} · RGB {c.R}, {c.G}, {c.B}"; };
        var imageMenu = new ImageContextMenu(() => Safe(() => CopyImage(canvas))) { Font = Font };
        imageMenu.Items.Add(new ToolStripSeparator());
        imageMenu.Items.Add("另存圖片…", null, (_, _) => Safe(() => Save(true)));
        var editText = imageMenu.Items.Add("編輯文字…（F2）", null, (_, _) => Safe(canvas.EditSelectedText));
        var deleteObject = imageMenu.Items.Add("刪除選取物件（Delete）", null, (_, _) => Safe(canvas.DeleteSelected));
        var undoItem = imageMenu.Items.Add("復原", null, (_, _) => canvas.Undo());
        imageMenu.Opening += (_, _) => { tabs.SelectedTab = page; undoItem.Enabled = canvas.CanUndo; editText.Enabled = canvas.SelectedObject?.Kind == EditTool.Text; deleteObject.Enabled = canvas.SelectedObject != null; };
        canvas.ContextMenuStrip = panel.ContextMenuStrip = imageMenu;
        page.Disposed += (_, _) => imageMenu.Dispose();
        SyncTools(); Fit(); UpdateStatus();
    }
    void UpdateStatus() { var e = Editor; dimensions.Text = e == null ? "" : $"{e.Image.Width} × {e.Image.Height} px  ·  {e.Zoom:P0}"; }
    void Fit() { if (Editor is not { } e || e.Parent is not { } panel) return; e.SetZoom(Math.Min(1, Math.Min((panel.ClientSize.Width - 55f) / e.Image.Width, (panel.ClientSize.Height - 55f) / e.Image.Height))); UpdateStatus(); }
    void Open() { using var d = new OpenFileDialog { Filter = "圖片|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff", Multiselect = true }; if (d.ShowDialog(this) == DialogResult.OK) foreach (var f in d.FileNames) OpenFile(f); }
    void OpenFile(string path) => Safe(() => { using var source = Image.FromFile(path); if ((long)source.Width * source.Height > 80_000_000) throw new InvalidOperationException("圖片超過 8,000 萬像素上限。"); AddDocument(new Bitmap(source), Path.GetFileName(path), path, false); });
    void NewBlank() { if (Dialogs.Dimensions(this, "新增白色畫布", new Size(1280, 720)) is { } size) { if ((long)size.Width * size.Height > 80_000_000) throw new InvalidOperationException("畫布最多支援 8,000 萬像素。"); var b = new Bitmap(size.Width, size.Height); using (var g = Graphics.FromImage(b)) g.Clear(Color.White); AddDocument(b, $"畫布 {++docIndex}"); } }
    void Paste() { using var image = Clipboard.GetImage(); if (image != null) AddDocument(new Bitmap(image), $"貼上 {++docIndex}"); else status.Text = "剪貼簿內沒有圖片。"; }
    void Copy() { if (Editor is { } e) CopyImage(e); }
    void CopyImage(EditorCanvas canvas)
    {
        Clipboard.SetImage(canvas.Image);
        status.Text = "整張圖片已複製，可切換到其他程式按 Ctrl+V 貼上。";
    }
    bool Save(bool saveAs)
    {
        if (Editor is not { } e) return true;
        string? path = e.FilePath;
        if (saveAs || path == null || !new[] { ".png", ".jpg", ".jpeg", ".bmp" }.Contains(Path.GetExtension(path).ToLowerInvariant()))
        { using var d = new SaveFileDialog { Filter = "PNG 圖片|*.png|JPEG 圖片|*.jpg|BMP 圖片|*.bmp", FileName = path == null ? $"SnapDesk_{DateTime.Now:yyyyMMdd_HHmmss}.png" : Path.GetFileNameWithoutExtension(path) + ".png", DefaultExt = "png", AddExtension = true }; if (d.ShowDialog(this) != DialogResult.OK) return false; path = d.FileName; }
        SaveBitmap(e.Image, path); e.FilePath = path; e.Dirty = false; tabs.SelectedTab!.Text = Path.GetFileName(path); status.Text = $"已儲存：{path}"; return true;
    }
    internal static void SaveBitmap(Bitmap image, string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant(); var format = ext is ".jpg" or ".jpeg" ? ImageFormat.Jpeg : ext == ".bmp" ? ImageFormat.Bmp : ImageFormat.Png;
        // Encode before replacing a destination, so failed encoding cannot truncate it.
        using var stream = new MemoryStream();
        if (format.Equals(ImageFormat.Jpeg) || format.Equals(ImageFormat.Bmp)) { using var flat = new Bitmap(image.Width, image.Height); using (var g = Graphics.FromImage(flat)) { g.Clear(Color.White); g.DrawImageUnscaled(image, 0, 0); } flat.Save(stream, format); } else image.Save(stream, format);
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllBytes(temp, stream.ToArray()); File.Move(temp, path, true); } finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    void ResizeImage() { if (Editor is { } e && Dialogs.Dimensions(this, "調整圖片大小（寬高獨立）", e.Image.Size) is { } size) e.ResizeImage(size); }
    void Print()
    {
        if (Editor is not { } e) return;
        using var doc = new PrintDocument(); doc.DocumentName = "SnapDesk";
        doc.PrintPage += (_, args) => { float scale = Math.Min((float)args.MarginBounds.Width / e.Image.Width, (float)args.MarginBounds.Height / e.Image.Height); args.Graphics?.DrawImage(e.Image, args.MarginBounds.X, args.MarginBounds.Y, e.Image.Width * scale, e.Image.Height * scale); };
        using var dialog = new PrintDialog { Document = doc, UseEXDialog = true }; if (dialog.ShowDialog(this) == DialogResult.OK) doc.Print();
    }
    bool ConfirmClose() { if (Editor is not { Dirty: true }) return true; var result = MessageBox.Show(this, "這張圖片尚未儲存，要先儲存嗎？", "關閉圖片", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question); return result == DialogResult.No || result == DialogResult.Yes && Save(false); }
    void CloseDocument() { if (tabs.SelectedTab is { Tag: EditorCanvas } page) tabs.TryCloseDocuments([page], _ => ConfirmClose()); }
    void ClosingApp(object? sender, FormClosingEventArgs args)
    {
        if(recording is {IsDisposed:false})
        {
            if(recording.IsRecording){args.Cancel=true;recording.Reveal();MessageBox.Show(recording,"請先停止並儲存錄影，再離開 SnapDesk。","螢幕錄影");return;}
            recording.Close();
        }
        if (busy) { args.Cancel = true; return; }
        foreach (TabPage page in tabs.TabPages.Cast<TabPage>().ToArray()) if (page.Tag is EditorCanvas { Dirty: true }) { tabs.SelectedTab = page; try { if (!ConfirmClose()) { args.Cancel = true; exitRequested = false; return; } } catch (Exception ex) { MessageBox.Show(this, ex.Message); args.Cancel = true; return; } }
        tray.Visible = false; tray.Dispose(); for (int i = 1; i <= 3; i++) Native.UnregisterHotKey(Handle, i); Safe(prefs.Save);
    }
    protected override void WndProc(ref Message m) { if (m.Msg == 0x0312 && !busy) { var mode = (int)m.WParam == 1 ? CaptureMode.FullScreen : (int)m.WParam == 2 ? CaptureMode.Region : CaptureMode.Window; _ = CaptureAsync(mode); } base.WndProc(ref m); }

    async Task CaptureAsync(CaptureMode mode)
    {
        if (busy || recording?.IsRecording==true) return;
        if (mode == CaptureMode.Repeat && lastCapture.IsEmpty) { status.Text = "請先擷取一次範圍。"; Restore(); return; }
        if (mode == CaptureMode.Fixed) { if (Dialogs.Dimensions(this, "固定截圖範圍", new Size(prefs.FixedWidth, prefs.FixedHeight)) is not { } size) return; prefs.FixedWidth = size.Width; prefs.FixedHeight = size.Height; Safe(prefs.Save); }
        if(mode==CaptureMode.Scrolling)
        {
            if(ScrollCaptureSetup.Choose(this,prefs.ScrollMaxSteps) is not { } steps)return;
            prefs.ScrollMaxSteps=steps;Safe(prefs.Save);
        }
        busy = true; Bitmap? result = null; string? scrollNotice = null;
        try
        {
            Hide(); await Task.Delay(300 + prefs.Delay * 1000);
            var bounds = SystemInformation.VirtualScreen;
            if (mode == CaptureMode.FullScreen) result = Native.Capture(bounds);
            else if (mode == CaptureMode.Repeat) { var rect = Rectangle.Intersect(bounds, lastCapture); result = Native.Capture(rect); }
            else
            {
                using var desktop = Native.Capture(bounds);
                using var overlay = new CaptureOverlay(desktop, mode, new Size(prefs.FixedWidth, prefs.FixedHeight));
                if (overlay.ShowDialog() == DialogResult.OK) { result = overlay.Result; lastCapture = overlay.Selection; }
            }
            if (result != null && mode == CaptureMode.Scrolling)
            {
                result.Dispose(); result = null; await Task.Delay(250);
                using var progress=new ScrollCaptureProgress(lastCapture);progress.ShowForCapture();
                result=await ScrollCapture.CaptureAsync(lastCapture,text=>scrollNotice=text,prefs.ScrollMaxSteps,progress.Report,progress.Token);
            }
            if (result != null)
            {
                var captured = result; result = null; Restore(); AddDocument(captured, $"截圖 {++docIndex}");
                if (prefs.AutoSave) { Directory.CreateDirectory(prefs.SaveFolder); string path = Path.Combine(prefs.SaveFolder, $"SnapDesk_{DateTime.Now:yyyyMMdd_HHmmss_fff}_{Guid.NewGuid().ToString("N")[..4]}.png"); SaveBitmap(captured, path); Editor!.FilePath = path; Editor.Dirty = false; status.Text = $"已自動儲存：{path}"; }
                else status.Text = "截圖完成。在圖片上按右鍵，選擇「複製圖片」，再到其他程式按 Ctrl+V 貼上。";
                if (scrollNotice != null) status.Text = scrollNotice;
            }
        }
        catch (Exception ex) { Restore(); MessageBox.Show(this, ex.Message, "截圖失敗", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        finally { result?.Dispose(); busy = false; if (!exitRequested) Restore(); }
    }
    void LaunchAccessory(AccessoryMode mode)
    {
        if (busy || recording?.IsRecording==true) return; Hide();
        try { Thread.Sleep(180); using var desktop = Native.Capture(SystemInformation.VirtualScreen); using var tool = new AccessoryOverlay(desktop, mode); tool.ShowDialog(); if (tool.ChosenColor is { } color) { ink = color; SyncTools(); status.Text = $"已複製色碼 {ColorTranslator.ToHtml(color)}"; } }
        finally { Restore(); }
    }
    void Settings()
    {
        using var f = new Form { Text = "SnapDesk 設定", ClientSize = new Size(570, 245), StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, Font = Font };
        var auto = new CheckBox { Text = "截圖後自動儲存 PNG", Checked = prefs.AutoSave, Location = new Point(20, 20), AutoSize = true };
        var folder = new TextBox { Text = prefs.SaveFolder, Location = new Point(20, 60), Width = 425 };
        var browse = new Button { Text = "瀏覽…", Location = new Point(460, 58), Width = 85 }; browse.Click += (_, _) => { using var d = new FolderBrowserDialog { SelectedPath = folder.Text }; if (d.ShowDialog(f) == DialogResult.OK) folder.Text = d.SelectedPath; };
        var minimize = new CheckBox { Text = "最小化時縮到通知區", Checked = prefs.MinimizeToTray, Location = new Point(20, 105), AutoSize = true };
        f.Controls.AddRange([auto, folder, browse, minimize, new Label { Text = "全域快捷鍵：Ctrl+Shift+1 / 2 / 3（全螢幕 / 範圍 / 視窗）", AutoSize = true, Location = new Point(20, 145) }]);
        var ok = new Button { Text = "儲存", Location = new Point(370, 195), DialogResult = DialogResult.OK }; var cancel = new Button { Text = "取消", Location = new Point(465, 195), DialogResult = DialogResult.Cancel }; f.Controls.AddRange([ok, cancel]); f.AcceptButton = ok; f.CancelButton = cancel;
        if (f.ShowDialog(this) == DialogResult.OK) { if (!Path.IsPathFullyQualified(folder.Text)) throw new InvalidOperationException("請選擇完整的儲存資料夾路徑。"); prefs.AutoSave = auto.Checked; prefs.SaveFolder = folder.Text; prefs.MinimizeToTray = minimize.Checked; prefs.Save(); }
    }
}
