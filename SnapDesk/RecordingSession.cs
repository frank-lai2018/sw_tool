using System.Diagnostics;
using ScreenRecorderLib;

namespace SnapDesk;

internal sealed record RecordingSettings(string DisplayName,Rectangle MonitorBounds,Rectangle Area,int Framerate,bool SystemAudio,bool Microphone,bool Cursor,bool HardwareEncoding=false)
{
    internal Rectangle LocalArea => new(Area.X-MonitorBounds.X,Area.Y-MonitorBounds.Y,Area.Width,Area.Height);
    internal static Rectangle ValidateArea(Rectangle monitor,Rectangle requested)
    {
        if(!monitor.Contains(requested)||requested.Width<16||requested.Height<16) throw new ArgumentException("錄影範圍必須完整位於同一個螢幕內，寬高至少 16 像素。");
        // H.264 4:2:0 requires even dimensions; trim at most one pixel at right/bottom.
        return new Rectangle(requested.X,requested.Y,requested.Width&~1,requested.Height&~1);
    }
    internal RecorderOptions Options()
    {
        var area=ValidateArea(MonitorBounds,Area);
        if(Framerate is not (15 or 30 or 60)) throw new ArgumentException("請選擇 15、30 或 60 FPS。");
        var source=new DisplayRecordingSource(DisplayName) {RecorderApi=RecorderApi.DesktopDuplication,SourceRect=new ScreenRect(area.X-MonitorBounds.X,area.Y-MonitorBounds.Y,area.Width,area.Height),OutputSize=new ScreenSize(area.Width,area.Height),AnchorPoint=Anchor.TopLeft,Stretch=StretchMode.None};
        var audio=new List<AudioSourceBase>();
        if(SystemAudio) audio.Add(new LoopbackAudioSource(AudioDevices.DefaultEndpoint(false)??throw new InvalidOperationException("找不到預設播放裝置，請接上喇叭／耳機，或取消勾選「錄製系統聲音」。")));
        if(Microphone) audio.Add(new CaptureAudioSource(AudioDevices.DefaultEndpoint(true)??throw new InvalidOperationException("找不到預設麥克風，請接上麥克風，或取消勾選「錄製麥克風」。")));
        return new RecorderOptions
        {
            SourceOptions=new SourceOptions {RecordingSources=[source]},
            OutputOptions=new OutputOptions {RecorderMode=RecorderMode.Video,OutputFrameSize=new ScreenSize(area.Width,area.Height),Stretch=StretchMode.None},
            VideoEncoderOptions=new VideoEncoderOptions {Framerate=Framerate,IsFixedFramerate=true,Bitrate=(int)Math.Clamp((long)area.Width*area.Height*Framerate/10,2_000_000,24_000_000),Encoder=new H264VideoEncoder {BitrateMode=H264BitrateControlMode.CBR,EncoderProfile=H264Profile.Main},IsHardwareEncodingEnabled=HardwareEncoding,IsThrottlingDisabled=false,IsFragmentedMp4Enabled=false,IsMp4FastStartEnabled=true},
            AudioOptions=new AudioOptions {IsAudioEnabled=audio.Count>0,AudioSources=audio,Bitrate=AudioBitrate.bitrate_128kbps,Channels=AudioChannels.Stereo},
            MouseOptions=new MouseOptions {IsMousePointerEnabled=Cursor,IsMouseClicksDetected=false},
            SnapshotOptions=new SnapshotOptions {SnapshotsWithVideo=false},
            LogOptions=new LogOptions {IsLogEnabled=false}
        };
    }
}

internal sealed class RecordingSession : IDisposable
{
    Recorder? recorder;
    readonly TaskCompletionSource started=new(TaskCreationOptions.RunContinuationsAsynchronously);
    readonly TaskCompletionSource<string> completed=new(TaskCreationOptions.RunContinuationsAsynchronously);
    readonly Stopwatch elapsed=new();
    internal event Action<RecorderStatus>? StatusChanged;
    internal RecorderStatus Status {get;private set;}=RecorderStatus.Idle;
    internal TimeSpan Elapsed=>elapsed.Elapsed;
    internal Task<string> Completion=>completed.Task;
    internal string OutputPath {get;private set;}="";
    internal async Task StartAsync(RecordingSettings settings,string path)=>await StartAsync(settings.Options(),path);
    internal async Task StartAsync(RecorderOptions options,string path)
    {
        if(recorder!=null) throw new InvalidOperationException("此錄影工作已啟動。");
        OutputPath=path;
        recorder=Recorder.CreateRecorder(options);
        recorder.OnStatusChanged+=(_,e)=>
        {
            Status=e.Status;
            if(e.Status==RecorderStatus.Recording) {elapsed.Start();started.TrySetResult();}
            else elapsed.Stop();
            StatusChanged?.Invoke(e.Status);
        };
        recorder.OnRecordingComplete+=(_,e)=>{elapsed.Stop();completed.TrySetResult(e.FilePath);};
        recorder.OnRecordingFailed+=(_,e)=>
        {
            elapsed.Stop();var error=new InvalidOperationException("錄影失敗："+e.Error);
            started.TrySetException(error);completed.TrySetException(error);
        };
        recorder.Record(path);
        try {await started.Task.WaitAsync(TimeSpan.FromSeconds(15));}
        catch {Stop();throw;}
    }
    internal void TogglePause()
    {
        if(Status==RecorderStatus.Recording) recorder?.Pause();
        else if(Status==RecorderStatus.Paused) recorder?.Resume();
    }
    internal void Stop() {if(recorder!=null && !completed.Task.IsCompleted) recorder.Stop();}
    public void Dispose() {recorder?.Dispose();recorder=null;elapsed.Stop();}
}
