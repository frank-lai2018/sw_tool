using System.Drawing.Drawing2D;

namespace SnapDesk;

internal enum ShapeKind { Rectangle, Square, RoundedRectangle, Ellipse, Circle, Triangle, Diamond, Pentagon, Hexagon, Heptagon, Octagon, Star5, Star6, Star8, Star12, Heart, SpeechBubble, RightArrow, LeftArrow, UpArrow, DownArrow, DoubleArrow, Custom }
internal sealed record ShapePreset(string Name, EditTool Tool, ShapeKind Shape = ShapeKind.Rectangle, PointF[]? Points = null);

internal static class ShapeGeometry
{
    internal static readonly ShapePreset[] Basic = [
        new("矩形", EditTool.Rectangle), new("正方形", EditTool.Shape, ShapeKind.Square), new("圓角矩形", EditTool.Shape, ShapeKind.RoundedRectangle),
        new("橢圓", EditTool.Ellipse), new("圓形", EditTool.Shape, ShapeKind.Circle), new("三角形", EditTool.Shape, ShapeKind.Triangle), new("菱形", EditTool.Shape, ShapeKind.Diamond),
        new("五邊形", EditTool.Shape, ShapeKind.Pentagon), new("六邊形", EditTool.Shape, ShapeKind.Hexagon), new("七邊形", EditTool.Shape, ShapeKind.Heptagon), new("八邊形", EditTool.Shape, ShapeKind.Octagon),
        new("五角星", EditTool.Shape, ShapeKind.Star5), new("六角星", EditTool.Shape, ShapeKind.Star6), new("八角星", EditTool.Shape, ShapeKind.Star8), new("十二角星", EditTool.Shape, ShapeKind.Star12),
        new("愛心", EditTool.Shape, ShapeKind.Heart), new("對話框", EditTool.Shape, ShapeKind.SpeechBubble),
        new("向右箭頭", EditTool.Shape, ShapeKind.RightArrow), new("向左箭頭", EditTool.Shape, ShapeKind.LeftArrow), new("向上箭頭", EditTool.Shape, ShapeKind.UpArrow), new("向下箭頭", EditTool.Shape, ShapeKind.DownArrow), new("雙向箭頭", EditTool.Shape, ShapeKind.DoubleArrow)];
    internal static readonly ShapePreset[] Lines = [new("直線", EditTool.Line), new("箭頭直線", EditTool.Arrow), new("曲線（控制點）", EditTool.Curve), new("自由曲線", EditTool.FreeCurve)];
    internal static bool IsClosed(EditTool tool) => tool is EditTool.Rectangle or EditTool.Ellipse or EditTool.Shape;
    internal static PointF[] Vertices(ShapeKind kind)
    {
        int sides = kind switch { ShapeKind.Pentagon => 5, ShapeKind.Hexagon => 6, ShapeKind.Heptagon => 7, ShapeKind.Octagon => 8, _ => 0 };
        if (sides > 0) return Radial(sides, 1);
        int rays = kind switch { ShapeKind.Star5 => 5, ShapeKind.Star6 => 6, ShapeKind.Star8 => 8, ShapeKind.Star12 => 12, _ => 0 };
        if (rays > 0) return Radial(rays * 2, .43f);
        PointF[] right = [new(0,.25f),new(.6f,.25f),new(.6f,0),new(1,.5f),new(.6f,1),new(.6f,.75f),new(0,.75f)];
        return kind switch
        {
            ShapeKind.Triangle => [new(.5f,0),new(1,1),new(0,1)],
            ShapeKind.Diamond => [new(.5f,0),new(1,.5f),new(.5f,1),new(0,.5f)],
            ShapeKind.SpeechBubble => [new(0,0),new(1,0),new(1,.75f),new(.45f,.75f),new(.2f,1),new(.25f,.75f),new(0,.75f)],
            ShapeKind.RightArrow => right,
            ShapeKind.LeftArrow => right.Select(p => new PointF(1-p.X,p.Y)).ToArray(),
            ShapeKind.UpArrow => right.Select(p => new PointF(p.Y,1-p.X)).ToArray(),
            ShapeKind.DownArrow => right.Select(p => new PointF(1-p.Y,p.X)).ToArray(),
            ShapeKind.DoubleArrow => [new(0,.5f),new(.25f,0),new(.25f,.3f),new(.75f,.3f),new(.75f,0),new(1,.5f),new(.75f,1),new(.75f,.7f),new(.25f,.7f),new(.25f,1)],
            ShapeKind.Heart => Heart(),
            _ => [new(0,0),new(1,0),new(1,1),new(0,1)]
        };
    }
    static PointF[] Radial(int count, float inner)
    {
        var points = Enumerable.Range(0,count).Select(i => { double angle = -Math.PI/2 + i * 2*Math.PI/count; float radius = inner < 1 && i%2==1 ? inner : 1; return new PointF(.5f + .5f*radius*(float)Math.Cos(angle),.5f+.5f*radius*(float)Math.Sin(angle)); }).ToArray();
        float l=points.Min(p=>p.X),t=points.Min(p=>p.Y),w=points.Max(p=>p.X)-l,h=points.Max(p=>p.Y)-t;
        return points.Select(p=>new PointF((p.X-l)/w,(p.Y-t)/h)).ToArray();
    }
    static PointF[] Heart() => Enumerable.Range(0, 72).Select(i =>
    {
        double t=i*2*Math.PI/72;
        double x=16*Math.Pow(Math.Sin(t),3), y=13*Math.Cos(t)-5*Math.Cos(2*t)-2*Math.Cos(3*t)-Math.Cos(4*t);
        return new PointF((float)(.5+x/32),(float)((12-y)/29));
    }).ToArray();
    internal static GraphicsPath Path(Annotation a)
    {
        var path = new GraphicsPath(); var r=a.Bounds;
        if (a.Kind==EditTool.Ellipse || a.Kind==EditTool.Shape && a.Shape is ShapeKind.Circle or ShapeKind.Ellipse) path.AddEllipse(r);
        else if (a.Kind==EditTool.Shape && a.Shape==ShapeKind.RoundedRectangle)
        {
            float d=Math.Min(r.Width,r.Height)*.3f;
            path.AddArc(r.Left,r.Top,d,d,180,90); path.AddArc(r.Right-d,r.Top,d,d,270,90); path.AddArc(r.Right-d,r.Bottom-d,d,d,0,90); path.AddArc(r.Left,r.Bottom-d,d,d,90,90); path.CloseFigure();
        }
        else if (a.Kind==EditTool.Rectangle || a.Points.Count<3) path.AddRectangle(r);
        else path.AddPolygon(a.Points.Select(a.World).ToArray());
        return path;
    }
}
