using System.Diagnostics;
using ScreenRecorderLib;

namespace SnapDesk;

internal static class RecordingTests
{
    internal static void CheckOptions(Action<bool,string> check)
    {
        var monitor=new Rectangle(-1920,-100,1920,1080);
        var normalized=RecordingSettings.ValidateArea(monitor,new Rectangle(-1901,-79,321,181));
        check(normalized==new Rectangle(-1901,-79,320,180),"Recording dimensions normalize odd size without shifting requested origin");
        bool rejected=false;try{RecordingSettings.ValidateArea(monitor,new Rectangle(-100,0,500,300));}catch(ArgumentException){rejected=true;}
        check(rejected,"Recording rejects ranges that cross monitor boundaries");
        rejected=false;try{RecordingSettings.ValidateArea(monitor,new Rectangle(-1000,0,2,10));}catch(ArgumentException){rejected=true;}
        check(rejected,"Recording rejects unusably small regions");
        var settings=new RecordingSettings("TEST",monitor,normalized,30,false,false,true);
        check(settings.LocalArea==new Rectangle(19,21,320,180),"Recording crop coordinates are relative to chosen monitor including negative desktop origins");
        var options=settings.Options();var source=(DisplayRecordingSource)options.SourceOptions.RecordingSources.Single();
        check(source.SourceRect.Left==19&&source.SourceRect.Top==21&&source.SourceRect.Width==320&&source.SourceRect.Height==180,"Capture options preserve exact monitor-local crop dimensions");
        check(options.OutputOptions.OutputFrameSize.Width==320&&options.OutputOptions.OutputFrameSize.Height==180,"Encoded output dimensions match selected capture region");
        check(source.RecorderApi==RecorderApi.DesktopDuplication,"Recording uses Desktop Duplication capture backend");
        check(options.AudioOptions.IsAudioEnabled==false&&options.AudioOptions.AudioSources.Count==0,"Silent recording does not enable microphone or system capture");
        check(options.VideoEncoderOptions.Encoder is H264VideoEncoder&&options.VideoEncoderOptions.Framerate==30,"Recording encodes H.264 at selected frame rate");
        bool hasMic=AudioDevices.DefaultEndpoint(true)!=null,hasOutput=AudioDevices.DefaultEndpoint(false)!=null;
        if(hasMic&&hasOutput){var withAudio=(settings with {SystemAudio=true,Microphone=true}).Options();check(withAudio.AudioOptions.IsAudioEnabled==true&&withAudio.AudioOptions.AudioSources.Count==2,"Recording options can mix default system audio and microphone");}
        else {bool missingHandled=false;try{(settings with {SystemAudio=!hasOutput,Microphone=!hasMic}).Options();}catch(InvalidOperationException){missingHandled=true;}check(missingHandled,"Unavailable audio device produces an actionable error instead of a native null-reference failure");}
    }
    internal static void Smoke()
    {
        var log=new List<string>();string path=Path.Combine(TestArtifacts.Directory,"recording-smoke-"+Guid.NewGuid().ToString("N")+".mp4");
        using var test=new Form {Text="SnapDesk 錄影測試畫面",FormBorderStyle=FormBorderStyle.None,ShowInTaskbar=false,StartPosition=FormStartPosition.Manual,TopMost=true,BackColor=Color.FromArgb(20,100,180),ClientSize=new Size(400,240)};
        var screen=Screen.PrimaryScreen!;test.Location=new Point(screen.WorkingArea.Left+35,screen.WorkingArea.Top+35);
        test.Paint+=(_,e)=>{using var font=new Font("Segoe UI",22,FontStyle.Bold);e.Graphics.DrawString("SnapDesk Recording Test",font,Brushes.White,12,18);e.Graphics.FillRectangle(Brushes.LimeGreen,20,130,80,70);};
        using var hiddenControl=new Form {FormBorderStyle=FormBorderStyle.None,ShowInTaskbar=false,StartPosition=FormStartPosition.Manual,TopMost=true,BackColor=Color.Red,ClientSize=new Size(80,60)};
        hiddenControl.Location=new Point(test.Left+210,test.Top+130);
        using var session=new RecordingSession();
        void Check(bool pass,string message){if(!pass)throw new Exception(message);log.Add("PASS: "+message);}
        void Pump(TimeSpan duration){var watch=Stopwatch.StartNew();while(watch.Elapsed<duration){Application.DoEvents();Thread.Sleep(10);}}
        void Wait(Task task,int seconds){var watch=Stopwatch.StartNew();while(!task.IsCompleted&&watch.Elapsed.TotalSeconds<seconds){Application.DoEvents();Thread.Sleep(10);}if(!task.IsCompleted)throw new TimeoutException("錄影測試逾時。");task.GetAwaiter().GetResult();}
        try
        {
            test.Show();hiddenControl.Show();bool excluded=Native.SetWindowDisplayAffinity(hiddenControl.Handle,0x11);Pump(TimeSpan.FromMilliseconds(300));
            var settings=new RecordingSettings(screen.DeviceName,screen.Bounds,new Rectangle(test.Location,test.ClientSize),15,false,false,false);
            Wait(session.StartAsync(settings,path),20);Check(session.Status==RecorderStatus.Recording,"Real desktop capture starts using packaged recording engine");
            Pump(TimeSpan.FromMilliseconds(1100));session.TogglePause();Pump(TimeSpan.FromMilliseconds(150));Check(session.Status==RecorderStatus.Paused,"Recording pauses");
            var elapsed=session.Elapsed;Pump(TimeSpan.FromMilliseconds(350));Check((session.Elapsed-elapsed).TotalMilliseconds<80,"Paused time is excluded from displayed elapsed time");
            session.TogglePause();Pump(TimeSpan.FromMilliseconds(1100));Check(session.Status==RecorderStatus.Recording,"Recording resumes");
            session.Stop();Wait(session.Completion,30);Check(File.Exists(path)&&new FileInfo(path).Length>1024,"Stopping finalizes a nonempty MP4 file");
            session.Dispose();
            using(var controller=new RecordingForm())
            {
                T Field<T>(string name)=>(T)typeof(RecordingForm).GetField(name,System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(controller)!;
                void Until(Func<bool> predicate,int seconds){var watch=Stopwatch.StartNew();while(!predicate()&&watch.Elapsed.TotalSeconds<seconds){Application.DoEvents();Thread.Sleep(10);}if(!predicate())throw new TimeoutException("錄影介面測試逾時。");}
                controller.ShowInTaskbar=false;controller.StartPosition=FormStartPosition.Manual;controller.Location=new Point(-30000,-30000);controller.Show();Application.DoEvents();
                Field<ComboBox>("mode").SelectedIndex=1;
                typeof(RecordingForm).GetField("area",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.SetValue(controller,new Rectangle(test.Location,test.ClientSize));
                Field<ComboBox>("fps").SelectedIndex=0;Field<ComboBox>("countdown").SelectedIndex=0;
                string uiPath=Path.Combine(TestArtifacts.Directory,"recording-ui-smoke-"+Guid.NewGuid().ToString("N")+".mp4");Field<TextBox>("output").Text=uiPath;
                Field<Button>("start").PerformClick();Until(()=>Field<Button>("pause").Enabled,20);Pump(TimeSpan.FromMilliseconds(900));
                Field<Button>("pause").PerformClick();Until(()=>Field<Button>("pause").Text=="繼續",5);
                Field<Button>("pause").PerformClick();Until(()=>Field<Button>("pause").Text=="暫停",5);Pump(TimeSpan.FromMilliseconds(900));
                Field<Button>("stop").PerformClick();Until(()=>!controller.IsRecording,30);
                Pump(TimeSpan.FromMilliseconds(200));
                Check(File.Exists(uiPath)&&new FileInfo(uiPath).Length>1024,"Record/pause/resume/stop buttons finalize video at user-selected destination");
                Check(Field<Button>("play").Enabled&&Field<Button>("folder").Enabled&&Field<Button>("start").Enabled,$"Recording UI restores controls and enables playback after saving (play={Field<Button>("play").Enabled}, folder={Field<Button>("folder").Enabled}, start={Field<Button>("start").Enabled}, state={Field<Label>("state").Text})");
                log.Add("UI_VIDEO="+uiPath);controller.Hide();
            }
            log.Add("CONTROL_EXCLUSION_API="+excluded);log.Add("VIDEO="+path);Environment.ExitCode=0;
        }
        catch(Exception ex){log.Add(ex.ToString());Environment.ExitCode=1;}
        finally{test.Hide();hiddenControl.Hide();File.WriteAllLines(Path.Combine(TestArtifacts.Directory,"recording-test-results.txt"),log);}
    }
}
