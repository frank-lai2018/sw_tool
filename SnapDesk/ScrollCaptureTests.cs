using System.Drawing.Imaging;

namespace SnapDesk;

internal static class ScrollCaptureTests
{
    internal static void Run(Action<bool,string> check)
    {
        const int width=64,viewHeight=120,shift=40,steps=200;
        using var content=new Bitmap(width,viewHeight+steps*shift);
        var random=new Random(913);
        for(int y=0;y<content.Height;y++)for(int x=0;x<width;x++)content.SetPixel(x,y,Color.FromArgb(random.Next(256),random.Next(256),random.Next(256)));
        Bitmap Frame(int offset)=>content.Clone(new Rectangle(0,offset,width,viewHeight),PixelFormat.Format32bppArgb);
        int scrolls=0;string notice="";ScrollProgress progress=default;
        using(var result=ScrollCapture.CaptureCoreAsync(steps,()=>Frame(scrolls*shift),()=>scrolls++,(_,_)=>Task.CompletedTask,()=>false,text=>notice=text,p=>progress=p).GetAwaiter().GetResult())
        {
            check(scrolls==200&&progress.Frames==201,"Long capture continues for 200 scroll steps rather than stopping after 20");
            check(result.Size==content.Size&&result.GetPixel(35,7900)==content.GetPixel(35,7900),"Long capture preserves far-bottom content without duplicated overlap");
            check(notice.Contains("200 次")&&progress.Height==content.Height,"Long capture reports configured step limit and final height");
        }
        using(var first=Frame(0))
        using(var builder=new ScrollImageBuilder(first))
        {
            for(int i=1;i<=20;i++){using var frame=Frame(i*shift);builder.Append(frame,shift);}
            check(builder.StoredPixels==(long)width*(viewHeight+20*shift),"Long capture stores only novel rows instead of retaining every full viewport");
            using var result=builder.Build();check(result.GetPixel(30,800)==content.GetPixel(30,800),"Strip accumulator stitches novel rows at correct vertical offsets");
        }
        scrolls=0;int captures=0;
        Bitmap DelayedFrame(){captures++;return Frame(scrolls==0||scrolls==1&&captures==2?0:shift);}
        using(var result=ScrollCapture.CaptureCoreAsync(10,DelayedFrame,()=>scrolls++,(_,_)=>Task.CompletedTask,()=>false,text=>notice=text).GetAwaiter().GetResult())
        {
            check(result.Height==viewHeight+shift&&captures>=7,"Lazy page update gets retried before deciding scrolling stopped");
            check(scrolls==2&&notice.Contains("可能已到頁尾"),"Stable end-of-page stops early instead of scrolling to the configured maximum");
        }
        using(var cancellation=new CancellationTokenSource())
        {
            scrolls=0;
            using var result=ScrollCapture.CaptureCoreAsync(200,()=>Frame(scrolls*shift),()=>{scrolls++;if(scrolls==5)cancellation.Cancel();},(_,_)=>Task.CompletedTask,()=>false,text=>notice=text,null,cancellation.Token).GetAwaiter().GetResult();
            check(result.Height==viewHeight+4*shift&&notice.Contains("手動停止"),"Stop cancels further scrolling while preserving every completed segment");
        }
        int captureNumber=0;
        using(var result=ScrollCapture.CaptureCoreAsync(10,()=>{if(captureNumber++==0)return Frame(0);var blank=new Bitmap(width,viewHeight);using(var g=Graphics.FromImage(blank))g.Clear(Color.White);return blank;},()=>{},(_,_)=>Task.CompletedTask,()=>false,text=>notice=text).GetAwaiter().GetResult())
            check(result.Height==viewHeight&&notice.Contains("無法可靠拼接"),"Unreliable matching preserves partial result and explains why it stopped");
        check(ScrollImageBuilder.WithinLimits(2000,40000)&&!ScrollImageBuilder.WithinLimits(2000,40001),"Long capture enforces 80-million-pixel limit using 64-bit arithmetic");
        check(ScrollImageBuilder.WithinLimits(64,100000)&&!ScrollImageBuilder.WithinLimits(64,100001),"Long capture enforces tall-image height limit");
        check(ScrollCapture.DefaultMaxSteps==200&&ScrollCapture.LargestMaxSteps==1000,"Long capture exposes 200-step default and 1000-step configurable maximum");
        using(var tall=new EditorCanvas(AnnotationTests.White(64,40001)))
        {
            tall.SetZoom(4);check(tall.Height<=30000&&tall.Image.Height==40001,"Very tall screenshots remain fully addressable without exceeding native control height");
        }
    }
}
