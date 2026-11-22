using System.Diagnostics;
using ScreenRecorderLib;

namespace SnapDesk;

internal sealed class RecordingForm : Form
{
    readonly Panel setup=new() {Dock=DockStyle.Fill};
    readonly ComboBox mode=new() {DropDownStyle=ComboBoxStyle.DropDownList};
    readonly ComboBox display=new() {DropDownStyle=ComboBoxStyle.DropDownList};
    readonly ComboBox fps=new() {DropDownStyle=ComboBoxStyle.DropDownList};
    readonly ComboBox countdown=new() {DropDownStyle=ComboBoxStyle.DropDownList};
    readonly CheckBox systemAudio=new() {Text="錄製系統聲音",AutoSize=true};
    readonly CheckBox microphone=new() {Text="錄製麥克風",AutoSize=true};
    readonly CheckBox cursor=new() {Text="顯示滑鼠游標",Checked=true,AutoSize=true};
    readonly TextBox output=new();
    readonly Label target=new() {AutoSize=true,ForeColor=Color.FromArgb(50,90,120)};
    readonly Label state=new() {Dock=DockStyle.Top,Height=39,TextAlign=ContentAlignment.MiddleLeft,Padding=new Padding(12,0,0,0),Text="準備錄影"};
    readonly Button start=new() {Text="● 開始錄影",AutoSize=true};
    readonly Button pause=new() {Text="暫停",Enabled=false,AutoSize=true};
    readonly Button stop=new() {Text="停止並儲存",Enabled=false,AutoSize=true};
    readonly Button play=new() {Text="播放影片",Enabled=false,AutoSize=true};
    readonly Button folder=new() {Text="開啟資料夾",Enabled=false,AutoSize=true};
    readonly System.Windows.Forms.Timer timer=new() {Interval=200};
    readonly NotifyIcon tray=new();
    Rectangle area;
    RecordingSession? session;
    CancellationTokenSource? cancel;
    bool working,preparing,finishing,closingAfterStop,excludeSupported,stopHotkey;
    string savedPath="";
    internal bool IsRecording=>working;
    internal void Reveal(){if(!working||excludeSupported){Show();Activate();}}
    internal event Action? HideEditor;
    internal event Action? RestoreEditor;

    internal RecordingForm()
    {
        Text="SnapDesk · 螢幕錄影";Icon=AppIcon.Load();Font=new Font("Microsoft JhengHei UI",10);
        ClientSize=new Size(620,455);FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;StartPosition=FormStartPosition.CenterScreen;
        var commands=new FlowLayoutPanel {Dock=DockStyle.Top,Height=43,Padding=new Padding(9,3,3,3),WrapContents=false};commands.Controls.AddRange([start,pause,stop,play,folder]);
        Controls.Add(setup);Controls.Add(commands);Controls.Add(state);
        var table=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=3,RowCount=9,Padding=new Padding(12),AutoSize=false};
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,95));table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,112));
        for(int i=0;i<8;i++)table.RowStyles.Add(new RowStyle(SizeType.Absolute,35));table.RowStyles.Add(new RowStyle(SizeType.Percent,100));setup.Controls.Add(table);
        mode.Items.AddRange(["指定螢幕全螢幕","選取範圍"]);mode.SelectedIndex=0;
        foreach(var screen in Screen.AllScreens)display.Items.Add($"{screen.DeviceName} · {screen.Bounds.Width} × {screen.Bounds.Height}");display.SelectedIndex=Array.FindIndex(Screen.AllScreens,s=>s.Primary);if(display.SelectedIndex<0)display.SelectedIndex=0;
        fps.Items.AddRange(["15 FPS","30 FPS","60 FPS"]);fps.SelectedIndex=1;countdown.Items.AddRange(["不倒數","倒數 3 秒","倒數 5 秒"]);countdown.SelectedIndex=1;
        void Row(int row,string label,Control control) {table.Controls.Add(new Label {Text=label,AutoSize=true,Anchor=AnchorStyles.Left},0,row);control.Dock=DockStyle.Fill;table.Controls.Add(control,1,row);}
        Row(0,"錄製模式",mode);Row(1,"螢幕",display);Row(2,"錄製範圍",target);Row(3,"影格率",fps);Row(4,"開始前",countdown);
        var choose=new Button {Text="選取範圍…",Dock=DockStyle.Fill,Enabled=false};table.Controls.Add(choose,2,2);
        var audio=new FlowLayoutPanel {Dock=DockStyle.Fill};audio.Controls.AddRange([systemAudio,microphone]);Row(5,"聲音",audio);Row(6,"游標",cursor);Row(7,"儲存 MP4",output);
        var detectAudio=new Button {Text="偵測聲音裝置",Dock=DockStyle.Fill};table.Controls.Add(detectAudio,2,5);detectAudio.Click+=(_,_)=>RefreshAudioDevices();RefreshAudioDevices();
        var browse=new Button {Text="瀏覽…",Dock=DockStyle.Fill};table.Controls.Add(browse,2,7);
        var help=new Label {Text="Ctrl+Shift+F9 暫停／繼續 · Ctrl+Shift+F10 停止\n聲音使用 Windows 預設播放與輸入裝置。\n錄影控制列會排除於擷取之外；不支援時將隱藏至通知區。",AutoSize=false,Dock=DockStyle.Fill,ForeColor=Color.DimGray};table.Controls.Add(help,0,8);table.SetColumnSpan(help,3);
        output.Text=NewOutputPath();UpdateArea();
        mode.SelectedIndexChanged+=(_,_)=>{choose.Enabled=mode.SelectedIndex==1;area=Rectangle.Empty;UpdateArea();};
        display.SelectedIndexChanged+=(_,_)=>{area=Rectangle.Empty;UpdateArea();};
        choose.Click+=async(_,_)=>await ChooseAreaAsync();
        browse.Click+=(_,_)=>{using var dialog=new SaveFileDialog {Filter="MP4 影片|*.mp4",DefaultExt="mp4",AddExtension=true,FileName=Path.GetFileName(output.Text),InitialDirectory=Path.GetDirectoryName(output.Text)};if(dialog.ShowDialog(this)==DialogResult.OK)output.Text=dialog.FileName;};
        start.Click+=async(_,_)=>await StartRecordingAsync();pause.Click+=(_,_)=>TogglePause();stop.Click+=(_,_)=>StopRecording();
        play.Click+=(_,_)=>OpenSaved(false);folder.Click+=(_,_)=>OpenSaved(true);
        timer.Tick+=(_,_)=>{if(session!=null)state.Text=$"{(session.Status==RecorderStatus.Paused?"⏸ 已暫停":finishing?"正在完成 MP4…":"● 錄影中")}   {session.Elapsed:hh\\:mm\\:ss}";};
        var trayMenu=new ContextMenuStrip();trayMenu.Items.Add("暫停／繼續",null,(_,_)=>TogglePause());trayMenu.Items.Add("停止並儲存",null,(_,_)=>StopRecording());trayMenu.Items.Add("顯示控制列",null,(_,_)=>{if(excludeSupported||!working){Show();Activate();}});
        tray.Icon=Icon;tray.Text="SnapDesk 錄影控制";tray.ContextMenuStrip=trayMenu;tray.DoubleClick+=(_,_)=>{if(excludeSupported||!working){Show();Activate();}};
        Shown+=(_,_)=>{bool pauseHotkey=Native.RegisterHotKey(Handle,201,0x4006,(uint)Keys.F9);stopHotkey=Native.RegisterHotKey(Handle,202,0x4006,(uint)Keys.F10);if(!pauseHotkey||!stopHotkey)state.Text="部分快捷鍵被占用，請使用按鈕或通知區控制。";};
        FormClosing+=(_,e)=>
        {
            if(!working)return;
            e.Cancel=true;closingAfterStop=true;
            if(preparing){closingAfterStop=true;cancel?.Cancel();return;}
            if(finishing)return;
            StopRecording();
        };
        FormClosed+=(_,_)=>{RestoreEditor?.Invoke();};
    }
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        excludeSupported=OperatingSystem.IsWindowsVersionAtLeast(10,0,19041)&&Native.SetWindowDisplayAffinity(Handle,0x11);
    }
    Screen SelectedScreen=>Screen.AllScreens[Math.Clamp(display.SelectedIndex,0,Screen.AllScreens.Length-1)];
    void RefreshAudioDevices()
    {
        try
        {
            systemAudio.Enabled=AudioDevices.DefaultEndpoint(false)!=null;microphone.Enabled=AudioDevices.DefaultEndpoint(true)!=null;
            if(!systemAudio.Enabled)systemAudio.Checked=false;if(!microphone.Enabled)microphone.Checked=false;
            systemAudio.Text=systemAudio.Enabled?"錄製系統聲音":"系統聲音（無裝置）";microphone.Text=microphone.Enabled?"錄製麥克風":"麥克風（無裝置）";
        }
        catch {systemAudio.Enabled=microphone.Enabled=false;systemAudio.Checked=microphone.Checked=false;}
    }
    static string NewOutputPath()=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),"SnapDesk",$"SnapDesk_{DateTime.Now:yyyyMMdd_HHmmss_fff}.mp4");
    void UpdateArea(){if(mode.SelectedIndex==0)area=SelectedScreen.Bounds;target.Text=area.IsEmpty?"請先選取範圍":$"{area.Width} × {area.Height} px · ({area.X}, {area.Y})";}
    async Task<bool> ChooseAreaAsync()
    {
        HideEditor?.Invoke();Hide();
        try
        {
            await Task.Delay(250);using var desktop=Native.Capture(SystemInformation.VirtualScreen);using var overlay=new CaptureOverlay(desktop,CaptureMode.Region,Size.Empty);
            if(overlay.ShowDialog()!=DialogResult.OK)return false;overlay.Result?.Dispose();
            var screens=Screen.AllScreens;int index=Array.FindIndex(screens,s=>s.Bounds.Contains(overlay.Selection));
            if(index<0)throw new InvalidOperationException("錄影範圍請選在同一個螢幕內。");
            var chosen=RecordingSettings.ValidateArea(screens[index].Bounds,overlay.Selection);
            display.SelectedIndex=index;area=chosen;target.Text=$"{area.Width} × {area.Height} px · ({area.X}, {area.Y})";return true;
        }
        catch(Exception ex){MessageBox.Show(ex.Message,"選取錄影範圍",MessageBoxButtons.OK,MessageBoxIcon.Warning);return false;}
        finally{Show();Activate();}
    }
    async Task StartRecordingAsync()
    {
        if(working)return;
        if(mode.SelectedIndex==1&&area.IsEmpty&&!await ChooseAreaAsync())return;
        string destination="",temporary="";bool overwrite=false;
        try
        {
            var screen=SelectedScreen;area=RecordingSettings.ValidateArea(screen.Bounds,area);
            destination=Path.GetFullPath(output.Text);
            if(!string.Equals(Path.GetExtension(destination),".mp4",StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("請使用 .mp4 副檔名。");
            if(File.Exists(destination)) {if(MessageBox.Show(this,"儲存位置已有影片，錄影完成後要取代它嗎？","儲存影片",MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes)return;overwrite=true;}
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            temporary=Path.Combine(Path.GetDirectoryName(destination)!,".snapdesk-"+Guid.NewGuid().ToString("N")+".recording.mp4");
            using(var probe=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None)){}File.Delete(temporary);
            if(!excludeSupported&&!stopHotkey)throw new InvalidOperationException("此電腦無法排除控制列，而且停止快捷鍵被其他程式占用。請先釋放 Ctrl+Shift+F10 再錄影。");
            var settings=new RecordingSettings(screen.DeviceName,screen.Bounds,area,new[]{15,30,60}[fps.SelectedIndex],systemAudio.Checked,microphone.Checked,cursor.Checked);
            cancel=new CancellationTokenSource();working=preparing=true;setup.Enabled=false;start.Enabled=false;play.Enabled=folder.Enabled=false;stop.Enabled=true;stop.Text="取消倒數";
            HideEditor?.Invoke();tray.Visible=true;
            int seconds=new[]{0,3,5}[countdown.SelectedIndex];
            for(int i=seconds;i>0;i--){state.Text=$"{i} 秒後開始錄影…";await Task.Delay(1000,cancel.Token);}
            cancel.Token.ThrowIfCancellationRequested();
            setup.Visible=false;ClientSize=new Size(620,82);TopMost=true;Location=new Point(screen.WorkingArea.Right-Width-12,screen.WorkingArea.Top+12);
            if(!excludeSupported)Hide();
            state.Text="正在啟動錄影…";stop.Text="停止並儲存";stop.Enabled=false;
            session=new RecordingSession();session.StatusChanged+=_=>Post(()=>pause.Text=session?.Status==RecorderStatus.Paused?"繼續":"暫停");
            await session.StartAsync(settings,temporary);
            preparing=false;pause.Enabled=stop.Enabled=true;timer.Start();
            if(cancel.IsCancellationRequested)StopRecording();
            await session.Completion;
            timer.Stop();session.Dispose();session=null;
            File.Move(temporary,destination,overwrite);savedPath=destination;state.Text="✓ 影片已儲存："+Path.GetFileName(destination);play.Enabled=folder.Enabled=true;output.Text=NewOutputPath();
        }
        catch(OperationCanceledException){state.Text="已取消錄影倒數。";}
        catch(Exception ex)
        {
            timer.Stop();string message=ex.Message;
            if(ex is FileNotFoundException or DllNotFoundException or BadImageFormatException)message="錄影引擎無法載入。請確認已安裝 Microsoft Visual C++ 2015–2022 x64 執行階段。\n"+message;
            if(!string.IsNullOrEmpty(temporary)&&File.Exists(temporary)&&new FileInfo(temporary).Length>0)message+="\n\n未完成的影片已保留：\n"+temporary;
            state.Text="錄影未完成，請檢查錯誤訊息。";Show();MessageBox.Show(this,message,"螢幕錄影",MessageBoxButtons.OK,MessageBoxIcon.Warning);
        }
        finally
        {
            timer.Stop();session?.Dispose();session=null;cancel?.Dispose();cancel=null;preparing=finishing=false;tray.Visible=false;TopMost=false;
            setup.Enabled=setup.Visible=true;start.Enabled=true;pause.Enabled=stop.Enabled=false;stop.Text="停止並儲存";pause.Text="暫停";ClientSize=new Size(620,455);Show();RestoreEditor?.Invoke();Activate();
            working=false;
            if(closingAfterStop)Close();
        }
    }
    void Post(Action action){if(!IsDisposed&&IsHandleCreated)try{BeginInvoke(action);}catch(InvalidOperationException){}}
    void TogglePause(){if(!working||preparing||finishing)return;session?.TogglePause();}
    void StopRecording()
    {
        if(!working)return;if(preparing){cancel?.Cancel();return;}if(finishing)return;
        finishing=true;pause.Enabled=stop.Enabled=false;state.Text="正在完成 MP4，請稍候…";session?.Stop();
    }
    void OpenSaved(bool directory)
    {
        if(string.IsNullOrEmpty(savedPath))return;
        try{if(directory){var info=new ProcessStartInfo("explorer.exe"){UseShellExecute=true};info.ArgumentList.Add("/select,"+savedPath);Process.Start(info);}else Process.Start(new ProcessStartInfo(savedPath){UseShellExecute=true});}catch(Exception ex){MessageBox.Show(this,ex.Message,"開啟影片");}
    }
    protected override void WndProc(ref Message m){if(m.Msg==0x0312){if((int)m.WParam==201)TogglePause();else if((int)m.WParam==202)StopRecording();}base.WndProc(ref m);}
    protected override void Dispose(bool disposing)
    {
        if(disposing){timer.Dispose();tray.Visible=false;tray.ContextMenuStrip?.Dispose();tray.Dispose();session?.Dispose();cancel?.Dispose();if(IsHandleCreated){Native.UnregisterHotKey(Handle,201);Native.UnregisterHotKey(Handle,202);}}
        base.Dispose(disposing);
    }
}
