using System.Drawing.Drawing2D;
using System.Reflection;
using System.Text.Json;

namespace SnapDesk;

internal static class ShapeTests
{
    static void Mouse(EditorCanvas canvas,string method,int x,int y) => typeof(EditorCanvas).GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(canvas,[new MouseEventArgs(MouseButtons.Left,1,x,y,0)]);
    internal static void Run(Action<bool,string> check,string output)
    {
        using var sheet=AnnotationTests.White(800,480); using var graphics=Graphics.FromImage(sheet);
        int count=0;
        foreach(var preset in ShapeGeometry.Basic)
        {
            int x=15+count%8*100,y=12+count/8*140;
            var a=new Annotation {Kind=preset.Tool,Shape=preset.Shape,Bounds=new RectangleF(x+10,y+5,70,75),Color=Color.FromArgb(22,92,155),FillColor=Color.FromArgb(216,239,255),Filled=true,Width=2,Points=ShapeGeometry.Vertices(preset.Shape).ToList()};
            using var path=ShapeGeometry.Path(a);
            check(path.PointCount>=3 && path.GetBounds().Width>0 && path.GetBounds().Height>0,"Shape geometry renders: "+preset.Name);
            a.Draw(graphics); using var font=new Font("Microsoft JhengHei UI",9); graphics.DrawString(preset.Name,font,Brushes.Black,x,y+88); count++;
        }
        sheet.Save(Path.Combine(output,"shapes-preview.png"));
        using var host=new Form {ShowInTaskbar=false,StartPosition=FormStartPosition.Manual,Location=new Point(-30000,-30000),ClientSize=new Size(700,500)};
        using var canvas=new EditorCanvas(AnnotationTests.White(600,400)); host.Controls.Add(canvas);host.Show();Application.DoEvents();
        canvas.Tool=EditTool.Shape;canvas.ShapeKind=ShapeKind.Star5;canvas.Ink=Color.Blue;canvas.Filled=true;canvas.FillColor=Color.Gold;
        Mouse(canvas,"OnMouseDown",30,30);Mouse(canvas,"OnMouseMove",160,150);Mouse(canvas,"OnMouseUp",160,150);
        check(canvas.Objects.Count==1&&canvas.Objects[0].Shape==ShapeKind.Star5&&canvas.Objects[0].Points.Count==10,"Gallery shape draws as editable polygon annotation");
        check(canvas.Image.GetPixel(95,90).ToArgb()==Color.Gold.ToArgb(),"New shape uses independent fill color");
        canvas.UpdateSelectedStyle(fillColor:Color.Lime,color:Color.Red,width:8,dash:DashStyle.Dash);
        check(canvas.Objects[0].FillColor==Color.Lime&&canvas.Objects[0].Color==Color.Red&&canvas.Objects[0].DashStyle==DashStyle.Dash&&canvas.Image.GetPixel(95,90).ToArgb()==Color.Lime.ToArgb(),"Existing shape fill outline thickness and dash can change together");
        canvas.Undo();check(canvas.Objects[0].FillColor==Color.Gold&&canvas.Objects[0].Color==Color.Blue,"Undo restores outline and fill independently");
        canvas.SelectObject(canvas.Objects[0]);canvas.UpdateSelectedStyle(outlined:false);
        check(!canvas.Objects[0].Outlined&&canvas.Objects[0].Filled,"Fill-only shape remains visible without an outline");
        canvas.UpdateSelectedStyle(filled:false);check(canvas.Objects[0].Outlined,"Cannot accidentally make a shape both unfilled and unoutlined");
        var original=canvas.Objects[0].Points.ToArray();canvas.Rotate(RotateFlipType.Rotate90FlipNone);
        check(canvas.Objects[0].Points[0]==new PointF(1-original[0].Y,original[0].X),"Polygon vertices rotate with the image");canvas.Undo();
        canvas.SelectObject(null);canvas.Tool=EditTool.Shape;canvas.ShapeKind=ShapeKind.Circle;
        Mouse(canvas,"OnMouseDown",220,30);Mouse(canvas,"OnMouseMove",350,110);Mouse(canvas,"OnMouseUp",350,110);
        check(canvas.Objects[^1].Bounds.Width==canvas.Objects[^1].Bounds.Height,"Circle preset constrains initial drawing to equal dimensions");
        canvas.SelectObject(null);canvas.Tool=EditTool.Curve;
        Mouse(canvas,"OnMouseDown",250,180);Mouse(canvas,"OnMouseMove",430,300);Mouse(canvas,"OnMouseUp",430,300);
        check(canvas.Objects[^1].Kind==EditTool.Curve&&canvas.SelectionHandles().Length==4,"Bezier curve exposes four editable control points");
        var control=canvas.SelectionHandles()[1];canvas.Tool=EditTool.Select;
        Mouse(canvas,"OnMouseDown",(int)control.X,(int)control.Y);Mouse(canvas,"OnMouseMove",(int)control.X-30,(int)control.Y+35);Mouse(canvas,"OnMouseUp",(int)control.X-30,(int)control.Y+35);
        var changed=canvas.Objects[^1].World(canvas.Objects[^1].Points[1]);
        check(Math.Abs(changed.X-(control.X-30))<.01&&Math.Abs(changed.Y-(control.Y+35))<.01,"Curve control point drag changes curve shape");canvas.Undo();
        check(Math.Abs(canvas.Objects[^1].World(canvas.Objects[^1].Points[1]).X-control.X)<.01,"Curve control point edit supports undo");
        canvas.SelectObject(null);canvas.Tool=EditTool.FreeCurve;
        Mouse(canvas,"OnMouseDown",40,220);Mouse(canvas,"OnMouseMove",90,190);Mouse(canvas,"OnMouseMove",120,250);Mouse(canvas,"OnMouseMove",160,230);Mouse(canvas,"OnMouseUp",200,270);
        check(canvas.Objects[^1].Kind==EditTool.FreeCurve&&canvas.Objects[^1].Points.Count>=4,"Free curve retains dragged points for later scaling");
        canvas.Image.Save(Path.Combine(output,"shapes-canvas-preview.png"));
        var record=new CustomShapeRecord {Name="我的星形",Tool=EditTool.Shape,Shape=ShapeKind.Star5,Coordinates=original.SelectMany(p=>new[]{p.X,p.Y}).ToArray()};
        var restored=JsonSerializer.Deserialize<CustomShapeRecord>(JsonSerializer.Serialize(record))!;
        check(restored.Preset.Points!.SequenceEqual(original)&&restored.Preset.Name=="我的星形","Custom shape round-trip preserves reusable geometry and name");
        using var gallery=new ShapeGallery();var tiles=Children(gallery).OfType<ShapeTile>().ToArray();
        ShapePreset? picked=null;int addRequests=0;gallery.Picked+=p=>picked=p;gallery.AddCustom+=()=>addRequests++;
        tiles.Single(t=>t.Preset.Name=="六角星").PerformClick();
        check(picked?.Shape==ShapeKind.Star6,"Gallery icon invokes selected shape preset");
        Children(gallery).OfType<Button>().Single(b=>b.AccessibleName=="將選取圖形加入自訂").PerformClick();
        check(addRequests==1,"Gallery custom plus button requests saving selected shape");
        using var galleryHost=new Form {ShowInTaskbar=false,StartPosition=FormStartPosition.Manual,Location=new Point(-30000,-30000),ClientSize=gallery.Size};
        galleryHost.Controls.Add(gallery);galleryHost.Show();Application.DoEvents();
        check(tiles.All(t=>t.Bottom<=t.Parent!.ClientSize.Height),"All preset icons fit within gallery rows without clipping");
        using var preview=new Bitmap(gallery.Width,gallery.Height);gallery.DrawToBitmap(preview,gallery.ClientRectangle);preview.Save(Path.Combine(output,"shape-gallery-preview.png"));
        galleryHost.Hide();host.Hide();
    }
    static IEnumerable<Control> Children(Control parent)=>parent.Controls.Cast<Control>().SelectMany(c=>new[]{c}.Concat(Children(c)));
}
