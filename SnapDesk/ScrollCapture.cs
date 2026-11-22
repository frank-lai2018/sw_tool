using System.Buffers;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace SnapDesk;

internal readonly record struct ScrollProgress(int Step,int MaxSteps,int Frames,int Height);

internal sealed class ScrollImageBuilder : IDisposable
{
    readonly List<Bitmap> strips=[];
    internal const long MaxPixels=80_000_000;
    internal const int MaxHeight=100_000;
    internal int Width {get;}
    internal int Height {get;private set;}
    internal int Frames=>strips.Count;
    internal long StoredPixels=>strips.Sum(s=>(long)s.Width*s.Height);
    internal ScrollImageBuilder(Bitmap first)
    {
        Width=first.Width;Height=first.Height;
        if(!WithinLimits(Width,Height))throw new InvalidOperationException("長截圖超過尺寸上限。");
        strips.Add(new Bitmap(first));
    }
    internal static bool WithinLimits(int width,int height)=>width>0&&height>0&&height<=MaxHeight&&(long)width*height<=MaxPixels;
    internal bool CanAppend(int rows)=>rows>0&&WithinLimits(Width,Height+rows);
    internal void Append(Bitmap frame,int rows)
    {
        if(frame.Width!=Width||rows>frame.Height||!CanAppend(rows))throw new ArgumentException("長截圖新增內容超過範圍或尺寸上限。");
        strips.Add(frame.Clone(new Rectangle(0,frame.Height-rows,Width,rows),PixelFormat.Format32bppArgb));Height+=rows;
    }
    internal Bitmap Build()
    {
        var result=new Bitmap(Width,Height);
        try {using var g=Graphics.FromImage(result);int y=0;foreach(var strip in strips){g.DrawImageUnscaled(strip,0,y);y+=strip.Height;}return result;}
        catch{result.Dispose();throw;}
    }
    public void Dispose(){foreach(var strip in strips)strip.Dispose();strips.Clear();}
}

internal static class ScrollCapture
{
    internal const int DefaultMaxSteps=200;
    internal const int LargestMaxSteps=1000;
    const int ScrollDelay=650;
    sealed class Pixels : IDisposable
    {
        internal readonly byte[] Data;
        internal readonly int Stride;
        internal Pixels(Bitmap image)
        {
            var locked=image.LockBits(new Rectangle(Point.Empty,image.Size),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
            Stride=Math.Abs(locked.Stride);byte[]? rented=null;
            try
            {
                Data=rented=ArrayPool<byte>.Shared.Rent(Stride*image.Height);
                if(locked.Stride>0)Marshal.Copy(locked.Scan0,Data,0,Stride*image.Height);
                else for(int y=0;y<image.Height;y++)Marshal.Copy(locked.Scan0+y*locked.Stride,Data,y*Stride,Stride);
            }
            catch{if(rented!=null)ArrayPool<byte>.Shared.Return(rented);throw;}
            finally{image.UnlockBits(locked);}
        }
        public void Dispose()=>ArrayPool<byte>.Shared.Return(Data);
    }
    // Sample locked pixels instead of making hundreds of thousands of GDI GetPixel calls per frame.
    internal static int FindShift(Bitmap previous,Bitmap next)
    {
        int height=Math.Min(previous.Height,next.Height),width=Math.Min(previous.Width,next.Width);
        if(height<60||width<30)return -1;
        using var a=new Pixels(previous);using var b=new Pixels(next);
        var scores=new List<(int shift,double error)>();
        for(int shift=0;shift<=height*3/4;shift++)
        {
            double error=0;int count=0;
            for(int y=8;y<height-shift-8;y+=Math.Max(7,(height-shift)/22))
                for(int x=width/10;x<width*9/10;x+=Math.Max(7,width/28))
                {
                    int p=(y+shift)*a.Stride+x*4,q=y*b.Stride+x*4;
                    error+=Math.Abs(a.Data[p]-b.Data[q])+Math.Abs(a.Data[p+1]-b.Data[q+1])+Math.Abs(a.Data[p+2]-b.Data[q+2]);count++;
                }
            if(count>0)scores.Add((shift,error/count));
        }
        var best=scores.MinBy(s=>s.error);
        if(best.error>18)return -1;if(best.shift==0)return 0;
        if(scores.Any(s=>Math.Abs(s.shift-best.shift)>4&&s.error<=best.error+1.5))return -1;
        return best.shift;
    }
    internal static Bitmap Stitch(IReadOnlyList<(Bitmap image,int offset)> frames)
    {
        int height=frames.Max(f=>f.offset+f.image.Height),width=frames[0].image.Width;
        if(!ScrollImageBuilder.WithinLimits(width,height))throw new InvalidOperationException("長截圖超過 8,000 萬像素或 100,000 像素高度上限。");
        var result=new Bitmap(width,height);using var g=Graphics.FromImage(result);
        foreach(var frame in frames)g.DrawImageUnscaled(frame.image,0,frame.offset);return result;
    }
    internal static async Task<Bitmap> CaptureAsync(Rectangle area,Action<string>? report=null,int maxSteps=DefaultMaxSteps,Action<ScrollProgress>? progress=null,CancellationToken cancellationToken=default)
    {
        if(area.Height<100||area.Width<60)throw new InvalidOperationException("捲動截圖請選取至少 60 像素寬、100 像素高的區域。");
        var oldPosition=Cursor.Position;
        try
        {
            Cursor.Position=new Point(area.Left+area.Width/2,area.Top+area.Height/2);
            Native.SetForegroundWindow(Native.GetAncestor(Native.WindowFromPoint(Cursor.Position),2));await Task.Delay(200);
            return await CaptureCoreAsync(maxSteps,()=>Native.Capture(area),()=>Native.mouse_event(0x0800,0,0,-120,0),Task.Delay,()=>Native.GetAsyncKeyState((int)Keys.Escape)<0,report,progress,cancellationToken);
        }
        finally{Cursor.Position=oldPosition;}
    }
    internal static async Task<Bitmap> CaptureCoreAsync(int maxSteps,Func<Bitmap> capture,Action scroll,Func<int,CancellationToken,Task> wait,Func<bool> escape,Action<string>? report=null,Action<ScrollProgress>? progress=null,CancellationToken cancellationToken=default)
    {
        if(maxSteps<1||maxSteps>LargestMaxSteps)throw new ArgumentOutOfRangeException(nameof(maxSteps));
        Bitmap previous=capture();using var builder=StartBuilder(previous);
        string reason=$"已達設定的 {maxSteps} 次捲動上限";
        try
        {
            progress?.Invoke(new ScrollProgress(0,maxSteps,builder.Frames,builder.Height));
            for(int step=1;step<=maxSteps;step++)
            {
                if(escape()||cancellationToken.IsCancellationRequested){reason="已手動停止";break;}
                scroll();
                // Retry the same scroll position before concluding the page ended or failed to match.
                int shift=-1;Bitmap? next=null;
                try
                {
                    for(int retry=0;retry<4;retry++)
                    {
                        await wait(retry==0?ScrollDelay:350,cancellationToken);
                        if(escape()||cancellationToken.IsCancellationRequested)throw new OperationCanceledException();
                        next?.Dispose();next=capture();shift=FindShift(previous,next);
                        if(shift>0)break;
                    }
                    if(shift<=0){reason=shift==0?"內容已停止移動（可能已到頁尾）":"後續畫面無法可靠拼接";break;}
                    if(!builder.CanAppend(shift)){reason="已達 8,000 萬像素或 100,000 像素高度上限";break;}
                    builder.Append(next!,shift);previous.Dispose();previous=next!;next=null;
                    progress?.Invoke(new ScrollProgress(step,maxSteps,builder.Frames,builder.Height));
                }
                finally{next?.Dispose();}
            }
        }
        catch(OperationCanceledException){reason="已手動停止";}
        finally{previous.Dispose();}
        report?.Invoke($"已拼接 {builder.Frames} 段，{builder.Width} × {builder.Height} px。{reason}。請檢查接縫與完整性。");
        return builder.Build();
    }
    static ScrollImageBuilder StartBuilder(Bitmap first)
    {
        try{return new ScrollImageBuilder(first);}catch{first.Dispose();throw;}
    }
}
