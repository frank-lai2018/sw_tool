using System.Drawing.Drawing2D;
using System.Text.Json;

namespace SnapDesk;

internal sealed class CustomShapeRecord
{
    public string Name { get; set; } = "自訂圖形";
    public EditTool Tool { get; set; }
    public ShapeKind Shape { get; set; }
    public float[] Coordinates { get; set; } = [];
    internal ShapePreset Preset => new(Name,Tool,Shape,Enumerable.Range(0,Coordinates.Length/2).Select(i=>new PointF(Coordinates[i*2],Coordinates[i*2+1])).ToArray());
}
internal static class CustomShapes
{
    static readonly string PathName=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"SnapDesk","custom-shapes.json");
    internal static CustomShapeRecord[] Load()
    {
        try { return (JsonSerializer.Deserialize<CustomShapeRecord[]>(File.ReadAllText(PathName)) ?? []).Where(r=>ShapeGeometry.IsClosed(r.Tool)&&Enum.IsDefined(r.Shape)&&r.Coordinates.Length%2==0&&r.Coordinates.Length<=2048&&r.Coordinates.All(v=>float.IsFinite(v)&&v>=0&&v<=1)).Take(40).ToArray(); } catch { return []; }
    }
    internal static void Save(Annotation a,string name)
    {
        if(!ShapeGeometry.IsClosed(a.Kind)) throw new InvalidOperationException("請先選取一個封閉圖形。");
        var items=Load().ToList(); items.Add(new CustomShapeRecord { Name=name,Tool=a.Kind,Shape=a.Shape,Coordinates=a.Points.SelectMany(p=>new[]{p.X,p.Y}).ToArray() });
        Directory.CreateDirectory(Path.GetDirectoryName(PathName)!);
        string temp=PathName+".tmp"; File.WriteAllText(temp,JsonSerializer.Serialize(items.TakeLast(40))); File.Move(temp,PathName,true);
    }
}

internal sealed class ShapeTile : Button
{
    internal readonly ShapePreset Preset;
    internal ShapeTile(ShapePreset preset)
    {
        Preset=preset; AccessibleName=preset.Name; Size=new Size(32,32); Margin=new Padding(1); FlatStyle=FlatStyle.Flat; FlatAppearance.BorderSize=0; BackColor=Color.White;
        SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); var g=e.Graphics; g.SmoothingMode=SmoothingMode.AntiAlias;
        var a=new Annotation { Kind=Preset.Tool,Shape=Preset.Shape,Bounds=new RectangleF(6,6,Width-12,Height-12),Color=Color.FromArgb(45,58,70),Width=1.3f,Points=(Preset.Points??ShapeGeometry.Vertices(Preset.Shape)).ToList() };
        if(a.Kind is EditTool.Line or EditTool.Arrow) a.Points=[new(0,0),new(1,1)];
        if(a.Kind==EditTool.Curve) a.Points=[new(0,1),new(.1f,0),new(.9f,1),new(1,0)];
        if(a.Kind==EditTool.FreeCurve) a.Points=[new(0,.2f),new(.6f,0),new(.4f,.5f),new(.9f,.7f),new(.5f,1)];
        a.Draw(g);
    }
}
internal sealed class ShapeGallery : UserControl
{
    readonly ToolTip tips=new();
    public event Action<ShapePreset>? Picked;
    public event Action? AddCustom;
    internal ShapeGallery()
    {
        Width=266; Height=350; BackColor=Color.White; Font=new Font("Microsoft JhengHei UI",9);
        var content=new FlowLayoutPanel { Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,AutoScroll=true,Padding=new Padding(4) }; Controls.Add(content);
        Section(content,"圖形",ShapeGeometry.Basic); Section(content,"直線／曲線",ShapeGeometry.Lines);
        Section(content,"自訂形狀",CustomShapes.Load().Select(c=>c.Preset).ToArray());
        var add=new Button { Text="＋ 將選取圖形加入自訂",AutoSize=false,Size=new Size(238,30),FlatStyle=FlatStyle.Flat,AccessibleName="將選取圖形加入自訂" }; add.Click+=(_,_)=>AddCustom?.Invoke(); content.Controls.Add(add);
    }
    void Section(FlowLayoutPanel parent,string title,ShapePreset[] presets)
    {
        parent.Controls.Add(new Label { Text=title,Width=238,Height=24,BackColor=Color.FromArgb(237,240,244),TextAlign=ContentAlignment.MiddleLeft,Margin=new Padding(0,3,0,2) });
        if(presets.Length==0) return;
        var grid=new FlowLayoutPanel { Width=238,Height=((presets.Length+6)/7)*34,WrapContents=true,Margin=new Padding(0) };
        foreach(var preset in presets) { var tile=new ShapeTile(preset); tips.SetToolTip(tile,preset.Name); tile.Click+=(_,_)=>Picked?.Invoke(preset); grid.Controls.Add(tile); }
        parent.Controls.Add(grid);
    }
    protected override void Dispose(bool disposing) { if(disposing) tips.Dispose(); base.Dispose(disposing); }
}
