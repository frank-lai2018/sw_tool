using System.Drawing.Drawing2D;

namespace SnapDesk;

internal enum RibbonCommand { Paste, Copy, Resize, Rotate, FlipHorizontal, FlipVertical }

internal sealed class RibbonButton : Button
{
    readonly string glyph;
    readonly Color glyphColor;
    internal bool Selected;
    bool hot;
    internal RibbonButton(string text, string glyph, Color color)
    {
        Text = text; this.glyph = glyph; glyphColor = color;
        FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0;
        BackColor = Color.Transparent; Cursor = Cursors.Hand;
        AccessibleName = text; UseVisualStyleBackColor = false;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }
    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hot = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hot = false; Invalidate(); }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Selected ? Color.FromArgb(207, 224, 238) : hot ? Color.FromArgb(225, 235, 244) : Color.FromArgb(244, 244, 244));
        if (Selected || hot) { using var p = new Pen(Color.FromArgb(153, 180, 203)); g.DrawRectangle(p, 0, 0, Width - 1, Height - 1); }
        int iconHeight = Math.Min(39, Height - 23);
        using var iconFont = new Font(glyph == "T" ? "Georgia" : "Segoe UI Symbol", 25, glyph == "T" ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel);
        TextRenderer.DrawText(g, glyph, iconFont, new Rectangle(0, 1, Width, iconHeight), Enabled ? glyphColor : Color.Gray, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        TextRenderer.DrawText(g, Text, Font, new Rectangle(0, iconHeight + 1, Width, Height - iconHeight - 1), Enabled ? Color.FromArgb(35, 45, 57) : Color.Gray, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        if (Focused) ControlPaint.DrawFocusRectangle(g, Rectangle.Inflate(ClientRectangle, -3, -3));
    }
}

internal sealed class EditorRibbon : UserControl
{
    readonly FlowLayoutPanel groups = new() { Dock = DockStyle.Fill, WrapContents = false, AutoScroll = true, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(3, 0, 3, 0) };
    readonly Dictionary<EditTool, RibbonButton> tools = [];
    readonly List<ContextMenuStrip> menus = [];
    readonly Button inkButton;
    readonly Button fillButton;
    readonly ComboBox widthBox, sizeBox;
    readonly RibbonButton shapes;
    readonly RibbonButton boldButton;
    Color ink = Color.FromArgb(244, 83, 87);
    Color fill = Color.White;
    bool editingFill;
    ToolStripDropDown? shapePopup;
    bool syncing;
    public event Action<RibbonCommand>? Command;
    public event Action<EditTool>? ToolSelected;
    public event Action<Color>? ColorSelected;
    public event Action<int>? WidthSelected;
    public event Action<int>? TextSizeSelected;
    public event Action<bool>? BoldSelected;
    public event Action<ShapePreset>? ShapeSelected;
    public event Action? AddCustomShape;
    public event Action<Color>? FillColorSelected;
    public event Action<bool>? FillSelected;
    public event Action<bool,DashStyle>? OutlineSelected;

    public EditorRibbon()
    {
        Height = 121; Dock = DockStyle.Top; BackColor = Color.FromArgb(244, 244, 244);
        AccessibleName = "圖片編輯功能區"; Font = new Font("Microsoft JhengHei UI", 9);
        Controls.Add(groups);
        var clipboard = Group("剪貼簿", 120);
        Large(clipboard, "貼上", "▣", 3, 55, Color.FromArgb(181, 120, 50), () => Command?.Invoke(RibbonCommand.Paste));
        Large(clipboard, "複製", "▤", 61, 55, Color.FromArgb(63, 85, 110), () => Command?.Invoke(RibbonCommand.Copy));

        var image = Group("影像", 155);
        var effects = Large(image, "特效 ▾", "✧", 3, 57, Color.FromArgb(216, 134, 37), null);
        Drop(effects, [("馬賽克工具", () => ToolSelected?.Invoke(EditTool.Mosaic)), ("螢光標示工具", () => ToolSelected?.Invoke(EditTool.Highlight))]);
        Small(image, "調整大小", new Rectangle(65, 6, 84, 29), () => Command?.Invoke(RibbonCommand.Resize));
        var rotate = Small(image, "旋轉 ▾", new Rectangle(65, 39, 84, 29), null);
        Drop(rotate, [("順時針旋轉 90°", () => Command?.Invoke(RibbonCommand.Rotate)), ("水平翻轉", () => Command?.Invoke(RibbonCommand.FlipHorizontal)), ("垂直翻轉", () => Command?.Invoke(RibbonCommand.FlipVertical))]);

        var drawing = Group("工具", 509);
        Tool(drawing, "移動", "↖", EditTool.Select, 2, Color.FromArgb(65, 65, 65));
        Tool(drawing, "繪圖", "╱", EditTool.Pen, 2, Color.FromArgb(40, 88, 130));
        Tool(drawing, "箭頭", "↗", EditTool.Arrow, 52, Color.FromArgb(45, 82, 115));
        Tool(drawing, "裁切", "▱", EditTool.Crop, 102, Color.FromArgb(65, 65, 65));
        Tool(drawing, "螢光筆", "▰", EditTool.Highlight, 152, Color.FromArgb(223, 166, 16));
        Tool(drawing, "文字", "T", EditTool.Text, 202, Color.FromArgb(28, 108, 176));
        Tool(drawing, "編號", "❶", EditTool.Number, 252, Color.FromArgb(109, 104, 143));
        shapes = Large(drawing, "圖形 ▾", "▧", 302, 49, Color.FromArgb(48, 106, 155), null);
        shapes.Click+=(_,_)=>ShowShapes();
        Tool(drawing, "馬賽克", "▦", EditTool.Mosaic, 352, Color.FromArgb(80, 94, 108));
        Tool(drawing, "取色", "⌖", EditTool.Eyedropper, 402, Color.FromArgb(41, 107, 107));
        // Reserve the first slot for the selection/move tool.
        foreach (Control control in drawing.Controls) if (control is RibbonButton b && b != tools[EditTool.Select]) b.Left += 50;

        var sizes = Group("大小／文字", 145);
        boldButton = Large(sizes, "粗體", "B", 84, 53, Color.FromArgb(45, 60, 80), () => { if (!syncing) BoldSelected?.Invoke(!boldButton!.Selected); });
        widthBox = Combo(sizes, "筆畫寬度", 7, ["1 px", "2 px", "4 px", "8 px", "12 px", "16 px", "24 px", "32 px", "48 px", "64 px"]);
        sizeBox = Combo(sizes, "文字大小", 41, ["8 字", "12 字", "16 字", "24 字", "36 字", "48 字", "64 字", "96 字", "128 字"]);
        widthBox.SelectedIndexChanged += (_, _) => { if (!syncing && int.TryParse(widthBox.Text.Split(' ')[0], out int w)) WidthSelected?.Invoke(w); };
        sizeBox.SelectedIndexChanged += (_, _) => { if (!syncing && int.TryParse(sizeBox.Text.Split(' ')[0], out int size)) TextSizeSelected?.Invoke(size); };

        var colors = Group("色彩", 132);
        inkButton = new Button { Text = "", Bounds = new Rectangle(15, 7, 34, 34), FlatStyle = FlatStyle.Flat, BackColor = ink, AccessibleName = "外框色，點選後色盤修改外框" };
        inkButton.FlatAppearance.BorderColor = Color.FromArgb(150, 150, 150); inkButton.Click += (_, _) => SetColorTarget(false); colors.Controls.Add(inkButton);
        fillButton = new Button { Text = "", Bounds = new Rectangle(80, 7, 34, 34), FlatStyle = FlatStyle.Flat, BackColor = fill, AccessibleName = "填滿色，點選後色盤修改填滿" };
        fillButton.Click+=(_,_)=>SetColorTarget(true); colors.Controls.Add(fillButton);
        var outlineOptions=Small(colors,"外框 ▾",new Rectangle(1,43,63,27),null);
        Drop(outlineOptions,[("選擇外框色…",()=>{SetColorTarget(false);PickColor();}),("實線外框",()=>OutlineSelected?.Invoke(true,DashStyle.Solid)),("虛線外框",()=>OutlineSelected?.Invoke(true,DashStyle.Dash)),("無外框",()=>OutlineSelected?.Invoke(false,DashStyle.Solid))]);
        var fillOptions=Small(colors,"填滿 ▾",new Rectangle(66,43,63,27),null);
        Drop(fillOptions,[("選擇填滿色…",()=>{SetColorTarget(true);PickColor();}),("使用填滿",()=>FillSelected?.Invoke(true)),("無填滿",()=>FillSelected?.Invoke(false))]);

        var palette = Group("調色盤", 238);
        Color[] swatches = [Color.Black, Color.Gray, Color.Maroon, Color.Red, Color.Orange, Color.Gold, Color.FromArgb(34,177,76), Color.FromArgb(0,162,232), Color.RoyalBlue, Color.Purple,
            Color.White, Color.Silver, Color.FromArgb(185,122,87), Color.Pink, Color.PeachPuff, Color.Khaki, Color.LightGreen, Color.LightSkyBlue, Color.LightSteelBlue, Color.Plum,
            Color.FromArgb(245,245,245), Color.FromArgb(224,224,224), Color.FromArgb(234,217,199), Color.MistyRose, Color.FromArgb(255,239,219), Color.LightYellow, Color.Honeydew, Color.Azure, Color.Lavender, Color.Thistle];
        for (int i = 0; i < swatches.Length; i++)
        {
            Color c = swatches[i]; var button = new Button { Bounds = new Rectangle(8 + i % 10 * 22, 5 + i / 10 * 23, 20, 21), BackColor = c, FlatStyle = FlatStyle.Flat, AccessibleName = $"色碼 {ColorTranslator.ToHtml(c)}", TabStop = true };
            button.FlatAppearance.BorderColor = Color.FromArgb(190, 190, 190); button.Click += (_, _) => Pick(c); palette.Controls.Add(button);
        }
        SetState(EditTool.Arrow, ink, 4, 24);
    }

    Panel Group(string title, int width)
    {
        var panel = new Panel { Width = width, Height = 96, Margin = new Padding(0), AccessibleName = title };
        panel.Paint += (_, e) => { using var pen = new Pen(Color.FromArgb(207, 207, 207)); e.Graphics.DrawLine(pen, panel.Width - 1, 3, panel.Width - 1, panel.Height - 4); };
        panel.Controls.Add(new Label { Text = title, AutoSize = false, TextAlign = ContentAlignment.MiddleCenter, Dock = DockStyle.Bottom, Height = 21, ForeColor = Color.FromArgb(71, 78, 87) }); groups.Controls.Add(panel); return panel;
    }
    RibbonButton Large(Panel parent, string text, string glyph, int x, int width, Color color, Action? click)
    {
        var b = new RibbonButton(text, glyph, color) { Bounds = new Rectangle(x, 3, width, 70), Font = Font };
        if (click != null) b.Click += (_, _) => click(); parent.Controls.Add(b); return b;
    }
    void Tool(Panel p, string text, string glyph, EditTool tool, int x, Color color) => tools[tool] = Large(p, text, glyph, x, 49, color, () => ToolSelected?.Invoke(tool));
    Button Small(Panel parent, string text, Rectangle bounds, Action? click)
    {
        var b = new Button { Text = text, Bounds = bounds, FlatStyle = FlatStyle.Flat, Font = Font, AccessibleName = text }; b.FlatAppearance.BorderSize = 0;
        if (click != null) b.Click += (_, _) => click(); parent.Controls.Add(b); return b;
    }
    ComboBox Combo(Panel parent, string name, int y, string[] values)
    {
        var box = new ComboBox { Bounds = new Rectangle(7, y, 70, 25), DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = name, Font = Font }; box.Items.AddRange(values); parent.Controls.Add(box); return box;
    }
    void Drop(Button button, (string label, Action click)[] entries)
    {
        var menu = new ContextMenuStrip { Font = Font }; menus.Add(menu);
        foreach (var (label, click) in entries) menu.Items.Add(label, null, (_, _) => click());
        button.Click += (_, _) => menu.Show(button, new Point(0, button.Height));
    }
    internal void ShowShapes()
    {
        shapePopup?.Dispose(); shapePopup=new ToolStripDropDown { Padding=Padding.Empty };
        var gallery=new ShapeGallery(); var host=new ToolStripControlHost(gallery) { AutoSize=false,Size=gallery.Size,Margin=Padding.Empty,Padding=Padding.Empty };
        shapePopup.Items.Add(host);
        gallery.Picked+=preset=>{shapePopup.Close();ShapeSelected?.Invoke(preset);};
        gallery.AddCustom+=()=>{shapePopup.Close();AddCustomShape?.Invoke();};
        shapePopup.Show(shapes,new Point(0,shapes.Height));
    }
    void SetColorTarget(bool isFill)
    {
        editingFill=isFill;
        inkButton.FlatAppearance.BorderSize=isFill?1:3; fillButton.FlatAppearance.BorderSize=isFill?3:1;
        inkButton.FlatAppearance.BorderColor=isFill?Color.Gray:Color.DodgerBlue; fillButton.FlatAppearance.BorderColor=isFill?Color.DodgerBlue:Color.Gray;
    }
    void Pick(Color color) { if(editingFill) FillColorSelected?.Invoke(color); else ColorSelected?.Invoke(color); }
    void PickColor() { using var d = new ColorDialog { Color = editingFill?fill:ink, FullOpen = true }; if (d.ShowDialog(FindForm()) == DialogResult.OK) Pick(d.Color); }
    internal void SetState(EditTool tool, Color color, int strokeWidth, int textSize, bool bold = true, Color? fillColor = null,bool filled=false,bool outlined=true,DashStyle dash=DashStyle.Solid)
    {
        syncing = true;
        try
        {
            if (boldButton.Selected != bold) { boldButton.Selected = bold; boldButton.Invalidate(); }
            foreach (var (key, button) in tools) { bool selected = key == tool; if (button.Selected != selected) { button.Selected = selected; button.Invalidate(); } }
            bool shapeSelected = tool is EditTool.Rectangle or EditTool.Ellipse or EditTool.Line or EditTool.Shape or EditTool.Curve or EditTool.FreeCurve;
            if (shapes.Selected != shapeSelected) { shapes.Selected = shapeSelected; shapes.Invalidate(); }
            ink = color; inkButton.BackColor = color;
            fill=fillColor??Color.White;fillButton.BackColor=fill;fillButton.Text=filled?"":"∅";
            inkButton.Text=outlined?"":"∅"; SetColorTarget(editingFill);
            string width = $"{strokeWidth} px", size = $"{textSize} 字";
            if (!widthBox.Items.Contains(width)) widthBox.Items.Add(width);
            if (!sizeBox.Items.Contains(size)) sizeBox.Items.Add(size);
            widthBox.SelectedItem = width; sizeBox.SelectedItem = size;
        }
        finally { syncing = false; }
    }
    protected override void Dispose(bool disposing) { if (disposing) { shapePopup?.Dispose(); foreach (var menu in menus) menu.Dispose(); } base.Dispose(disposing); }
}
