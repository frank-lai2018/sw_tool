namespace SnapDesk;

internal static class ScrollCaptureSetup
{
    internal static int? Choose(IWin32Window owner,int current)
    {
        using var dialog=new Form {Text="長截圖設定",ClientSize=new Size(520,225),FormBorderStyle=FormBorderStyle.FixedDialog,StartPosition=FormStartPosition.CenterParent,MaximizeBox=false,MinimizeBox=false,Font=new Font("Microsoft JhengHei UI",10)};
        dialog.Controls.Add(new Label {Text="選取可捲動內容的內部區域，避開固定頁首、工具列與捲軸。\n可設定最多 1,000 次捲動，頁面不再移動時會提前停止。\n過程可按「停止」或按住 Esc，保留已擷取內容。",AutoSize=false,Bounds=new Rectangle(18,15,490,78)});
        dialog.Controls.Add(new Label {Text="最多捲動次數",AutoSize=true,Location=new Point(18,110)});
        var steps=new NumericUpDown {Minimum=20,Maximum=ScrollCapture.LargestMaxSteps,Value=Math.Clamp(current,20,ScrollCapture.LargestMaxSteps),Increment=20,Bounds=new Rectangle(150,106,130,30)};dialog.Controls.Add(steps);
        dialog.Controls.Add(new Label {Text="圖片上限：8,000 萬像素／100,000 像素高。",AutoSize=true,ForeColor=Color.DimGray,Location=new Point(18,150)});
        var ok=new Button {Text="選取範圍並開始",Bounds=new Rectangle(270,180,145,32),DialogResult=DialogResult.OK};var cancel=new Button {Text="取消",Bounds=new Rectangle(425,180,75,32),DialogResult=DialogResult.Cancel};
        dialog.Controls.AddRange([ok,cancel]);dialog.AcceptButton=ok;dialog.CancelButton=cancel;
        return dialog.ShowDialog(owner)==DialogResult.OK?(int)steps.Value:null;
    }
}
internal sealed class ScrollCaptureProgress : Form
{
    readonly Label label=new() {AutoSize=false,Bounds=new Rectangle(12,10,270,58)};
    readonly Button stop=new() {Text="停止並保留",Bounds=new Rectangle(285,17,112,35)};
    readonly CancellationTokenSource cancellation=new();
    internal CancellationToken Token=>cancellation.Token;
    protected override bool ShowWithoutActivation=>true;
    internal ScrollCaptureProgress(Rectangle captureArea)
    {
        Text="SnapDesk · 長截圖進度";ClientSize=new Size(410,78);Font=new Font("Microsoft JhengHei UI",10);FormBorderStyle=FormBorderStyle.FixedToolWindow;TopMost=true;ShowInTaskbar=false;StartPosition=FormStartPosition.Manual;
        var candidates=Screen.AllScreens.SelectMany(s=>new[]{new Point(s.WorkingArea.Right-Width-8,s.WorkingArea.Top+8),new Point(s.WorkingArea.Left+8,s.WorkingArea.Top+8),new Point(s.WorkingArea.Right-Width-8,s.WorkingArea.Bottom-Height-8),new Point(s.WorkingArea.Left+8,s.WorkingArea.Bottom-Height-8)}).ToArray();
        Location=candidates.FirstOrDefault(p=>!new Rectangle(p,Size).IntersectsWith(captureArea),candidates[0]);
        label.Text="準備擷取長截圖…\n按住 Esc 也可停止。";Controls.AddRange([label,stop]);
        stop.Click+=(_,_)=>Stop();FormClosing+=(_,e)=>{e.Cancel=true;Stop();Hide();};
    }
    internal void Report(ScrollProgress progress){if(!IsDisposed)label.Text=$"已拼接 {progress.Frames} 段 · 高 {progress.Height:N0} px\n捲動 {progress.Step} / {progress.MaxSteps} 次 · Esc 停止";}
    void Stop(){cancellation.Cancel();stop.Enabled=false;label.Text="正在完成目前已擷取的內容…";}
    internal void ShowForCapture()
    {
        _=Handle;
        if(OperatingSystem.IsWindowsVersionAtLeast(10,0,19041)&&Native.SetWindowDisplayAffinity(Handle,0x11))Show();
    }
    protected override void Dispose(bool disposing){if(disposing)cancellation.Dispose();base.Dispose(disposing);}
}
