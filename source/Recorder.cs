using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace GameplayRecorder;

public record CaptureOptions(GameWindow Game, MonitorTarget Monitor, bool WindowMode, bool Compatibility,
    int Fps, int MaxHeight, int Crf, string Encoder, bool Cursor, string Folder);
public record VideoInfo(double Start, double Duration, int Width, int Height);

public sealed class Recorder : IDisposable
{
    public static string Tools => Path.Combine(AppContext.BaseDirectory,"tools");
    public static string Ffmpeg => Path.Combine(Tools,"ffmpeg.exe");
    public static string Ffprobe => Path.Combine(Tools,"ffprobe.exe");
    private Process? video, audio;
    private CaptureJob? videoJob;
    private EventWaitHandle? stopAudio;
    private string? session, videoPath, wavePath, logPath;
    private StreamWriter? log;
    private readonly object logLock=new();
    private double audioStart;
    private CaptureOptions? options;
    public event Action<string>? Failed;
    public string? OutputPath { get; private set; }
    public string? RecoveryPath => session;
    public bool Active { get; private set; }
    private bool intentionalStop;
    private bool displayAccessLost;

    internal static ProcessStartInfo Command(string exe,IEnumerable<string> args)
    {
        var p=new ProcessStartInfo(exe) { UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true,RedirectStandardOutput=true,RedirectStandardInput=true };
        foreach(var arg in args) p.ArgumentList.Add(arg);
        return p;
    }
    internal static async Task<(int Code,string Out,string Error)> Run(string exe,IEnumerable<string> args,CancellationToken cancel=default)
    {
        using var p=new Process {StartInfo=Command(exe,args)};
        p.Start(); var stdout=p.StandardOutput.ReadToEndAsync(); var stderr=p.StandardError.ReadToEndAsync();
        try { await p.WaitForExitAsync(cancel); }
        catch { try { p.Kill(true); } catch {} throw; }
        return (p.ExitCode,await stdout,await stderr);
    }
    private void Log(string message) { lock(logLock) { log?.WriteLine($"{DateTimeOffset.Now:O} {message}"); log?.Flush(); } }
    public static IEnumerable<string> CaptureInput(CaptureOptions o)
    {
        if (o.WindowMode) return new[]{"-f","gdigrab","-framerate",o.Fps.ToString(),"-draw_mouse",o.Cursor?"1":"0","-i",$"hwnd={o.Game.Handle}"};
        var b=o.Monitor.Bounds;
        if (o.Compatibility) return new[]{"-f","gdigrab","-framerate",o.Fps.ToString(),"-draw_mouse",o.Cursor?"1":"0","-offset_x",b.X.ToString(),"-offset_y",b.Y.ToString(),"-video_size",$"{b.Width}x{b.Height}","-i","desktop"};
        // Enumerate DXGI outputs in the same order as DXGI; Screen order is not guaranteed.
        int index=DisplayIndex.Find(o.Monitor.Device);
        return new[]{"-f","lavfi","-i",$"ddagrab=output_idx={index}:framerate={o.Fps}:draw_mouse={(o.Cursor?1:0)},hwdownload,format=bgra"};
    }
    public async Task StartAsync(CaptureOptions o)
    {
        if(Active || session!=null) throw new InvalidOperationException("This recorder already has a session.");
        if (!Native.SameProcess(o.Game)) throw new InvalidOperationException("The selected application window has closed. Click Refresh applications and select it again.");
        if(o.WindowMode && Native.IsIconic(o.Game.Handle)) throw new InvalidOperationException("Restore the selected window before capturing it, or use Screen capture.");
        if(Environment.OSVersion.Version.Build<20348) throw new InvalidOperationException("Application-only audio needs Windows build 20348 or later (Windows 11 recommended).");
        if(o.Encoder is not ("libx264" or "h264_nvenc")) throw new InvalidOperationException("Choose CPU or NVIDIA GPU encoding.");
        foreach(var file in new[]{Ffmpeg,Ffprobe,Path.Combine(Tools,"ProcessAudio.exe")})
            if(!File.Exists(file)) throw new FileNotFoundException("A recording tool is missing. Run Setup Recording Tools.cmd from the extracted release folder, then try again. See README.md for manual setup.",file);
        Directory.CreateDirectory(o.Folder);
        if(new DriveInfo(Path.GetPathRoot(Path.GetFullPath(o.Folder))!).AvailableFreeSpace<1024L*1024*1024)
            throw new IOException("Less than 1 GB is free on the recording drive. Choose another folder or free some space.");
        options=o;
        var name=$"Recording-{DateTime.Now:yyyy-MM-dd-HHmmss}-{Guid.NewGuid().ToString("N")[..4]}";
        OutputPath=Path.Combine(o.Folder,name+".mp4");
        session=Path.Combine(o.Folder,".recording-"+name); Directory.CreateDirectory(session);
        videoPath=Path.Combine(session,"video.mkv"); wavePath=Path.Combine(session,"game-audio.wav"); logPath=Path.Combine(session,"capture.log");
        log=new StreamWriter(logPath,true); Log("Starting application-only audio capture");
        Log($"Picture source: {(o.WindowMode?"Window":"Screen")}; application PID: {o.Game.Pid}; encoder: {o.Encoder}");
        var eventName=@"Local\GameplayRecorder-"+Guid.NewGuid().ToString("N");
        stopAudio=new EventWaitHandle(false,EventResetMode.ManualReset,eventName);
        try {
            audio=new Process {StartInfo=Command(Path.Combine(Tools,"ProcessAudio.exe"),new[]{o.Game.Pid.ToString(),wavePath,eventName,Environment.ProcessId.ToString()})};
            audio.Start();
            audio.ErrorDataReceived+=(_,e)=>{if(e.Data!=null) Log("audio: "+e.Data);}; audio.BeginErrorReadLine();
            string? ready=await audio.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(15));
            if(ready==null || !ready.StartsWith("READY ") || !double.TryParse(ready[6..],CultureInfo.InvariantCulture,out audioStart))
                throw new InvalidOperationException("Windows could not start application audio. See the session's capture.log.");
            await File.WriteAllTextAsync(Path.Combine(session,"session.json"),JsonSerializer.Serialize(new {audioStart,output=OutputPath},new JsonSerializerOptions{WriteIndented=true}));
            // Drain the helper's final output without blocking its pipe.
            _=audio.StandardOutput.ReadToEndAsync();
            var args=new List<string>{"-hide_banner","-loglevel","info","-nostats","-y","-use_wallclock_as_timestamps","1"};
            args.AddRange(CaptureInput(o)); args.AddRange(new[]{"-copyts","-an","-vf",$"scale=w=-2:h='max(2,trunc(min({o.MaxHeight},ih)/2)*2)':flags=lanczos,format=yuv420p","-c:v",o.Encoder});
            if(o.Encoder=="libx264") args.AddRange(new[]{"-preset","veryfast","-tune","zerolatency","-crf",o.Crf.ToString()});
            else if(o.Encoder=="h264_nvenc") args.AddRange(new[]{"-preset","p4","-rc","vbr","-cq",o.Crf.ToString(),"-b:v","0"});
            args.AddRange(new[]{"-enc_time_base","1:1000","-fps_mode","vfr","-flush_packets","1","-cluster_time_limit","1000","-cluster_size_limit","1048576","-stats_period","0.25","-progress","pipe:1",videoPath});
            video=new Process {StartInfo=Command(Ffmpeg,args),EnableRaisingEvents=true};
            var firstFrame=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            video.OutputDataReceived+=(_,e)=>{
                if(e.Data?.StartsWith("frame=")==true && int.TryParse(e.Data[6..],out var frames) && frames>0) firstFrame.TrySetResult();
            };
            video.ErrorDataReceived+=(_,e)=>{if(e.Data!=null) RecordVideoDiagnostic(e.Data);};
            var captureProcess=video;
            video.Exited+=async(_,_)=>{
                // Drain stderr before classifying the failure: the final error can arrive
                // after Process.Exited, even when FFmpeg returns a zero exit code.
                try {await captureProcess.WaitForExitAsync();Log("Video process exited with code "+captureProcess.ExitCode);}
                catch(ObjectDisposedException){return;}
                if(!intentionalStop) {
                    string reason=CaptureMessages.VideoStopped(displayAccessLost);
                    firstFrame.TrySetException(new IOException(reason));
                    if(Active) Failed?.Invoke(reason);
                }
            };
            video.Start(); videoJob=new CaptureJob(video); video.BeginOutputReadLine(); video.BeginErrorReadLine();
            await firstFrame.Task.WaitAsync(TimeSpan.FromSeconds(15));
            if(video.HasExited) throw new IOException(CaptureMessages.VideoStopped(displayAccessLost));
            Active=true;
            audio.EnableRaisingEvents=true;
            audio.Exited+=(_,_)=>{ if(Active && !intentionalStop) Failed?.Invoke("Application audio capture stopped unexpectedly. The session log contains details."); };
            if(audio.HasExited) throw new IOException("Application audio capture stopped before recording started.");
        } catch {
            await AbortStartAsync(); throw;
        }
    }
    private void RecordVideoDiagnostic(string message)
    {
        if(message.Contains("887a0026",StringComparison.OrdinalIgnoreCase) || message.Contains("DXGI_ERROR_ACCESS_LOST",StringComparison.OrdinalIgnoreCase)) displayAccessLost=true;
        Log("video: "+message);
    }
    internal void InterruptVideoForTest()
    {
        RecordVideoDiagnostic("Controlled test of the observed error: AcquireNextFrame failed: 887a0026");
        if(video is {HasExited:false}) video.Kill(true);
    }
    private async Task AbortStartAsync()
    {
        intentionalStop=true; Active=false; stopAudio?.Set();
        if(video is {HasExited:false}) { try {await video.StandardInput.WriteLineAsync("q"); await video.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));} catch {try{video.Kill(true);}catch{}} }
        if(audio is {HasExited:false}) {try{await audio.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));}catch{try{audio.Kill(true);}catch{}}}
        Log("Start failed. Session files kept for diagnostics.");
    }
    public async Task<string> StopAsync(string? interruption=null)
    {
        if(session==null || options==null || videoPath==null || wavePath==null || OutputPath==null) throw new InvalidOperationException("No recording is active.");
        intentionalStop=true; Active=false;
        Log(interruption==null?"Stop requested by user.":"Capture interrupted: "+interruption);
        if(video is {HasExited:false}) {
            try {await video.StandardInput.WriteLineAsync("q");}
            catch(IOException){Log("Video input closed before stop command; saving surviving frames.");}
            catch(InvalidOperationException){Log("Video already stopped; saving surviving frames.");}
            try { await video.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30)); }
            catch(TimeoutException) {video.Kill(true); await video.WaitForExitAsync(); Log("Video process needed forced shutdown; attempting recovery.");}
        }
        stopAudio?.Set();
        if(audio is {HasExited:false}) {
            try { await audio.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)); }
            catch(TimeoutException) {audio.Kill(true); throw new IOException("Application audio could not be finalized. Session files were kept.");}
        }
        if(audio?.ExitCode!=0) throw new IOException("Application audio capture failed. Session files were kept; see capture.log.");
        return await FinalizeAsync(session,audioStart,OutputPath,Log);
    }
    public static async Task<VideoInfo> ProbeAsync(string path)
    {
        var p=await Run(Ffprobe,new[]{"-v","error","-show_entries","format=start_time,duration:stream=codec_type,width,height","-of","json",path});
        if(p.Code!=0) throw new IOException("Could not read the captured video: "+p.Error);
        using var j=JsonDocument.Parse(p.Out); var f=j.RootElement.GetProperty("format");
        double start=f.TryGetProperty("start_time",out var startElement) && double.TryParse(startElement.GetString(),CultureInfo.InvariantCulture,out var startValue)?startValue:0;
        double end=f.TryGetProperty("duration",out var endElement) && double.TryParse(endElement.GetString(),CultureInfo.InvariantCulture,out var endValue)?endValue:0;
        var stream=j.RootElement.GetProperty("streams").EnumerateArray().First(s=>s.GetProperty("codec_type").GetString()=="video");
        // Epoch-timestamped Matroska stores its end timestamp in format.duration.
        double duration=end-start;
        if(duration<=0 || start<1000000000) {
            // An interrupted Matroska file may have no final duration/cues. Read surviving
            // packet times rather than relying on the unfinished container header.
            var packets=await Run(Ffprobe,new[]{"-v","error","-select_streams","v:0","-show_packets","-show_entries","packet=pts_time,duration_time","-of","compact=p=0:nk=0",path});
            double first=double.MaxValue,last=0;
            foreach(var line in packets.Out.Split('\n')) {
                double stamp=double.NaN,length=1.0/30;
                foreach(var field in line.Split('|')) {
                    var pair=field.Trim().Split('=',2);
                    if(pair.Length!=2)continue;
                    if(pair[0]=="pts_time" && double.TryParse(pair[1],CultureInfo.InvariantCulture,out var value))stamp=value;
                    if(pair[0]=="duration_time" && double.TryParse(pair[1],CultureInfo.InvariantCulture,out value))length=value;
                }
                if(!double.IsNaN(stamp)){first=Math.Min(first,stamp);last=Math.Max(last,stamp+length);}
            }
            start=first;duration=last-first;
        }
        if(start<1000000000 || duration<=0 || duration>86400) throw new IOException("Unexpected video timestamps; captured files were kept.");
        return new(start,duration,stream.GetProperty("width").GetInt32(),stream.GetProperty("height").GetInt32());
    }
    public static async Task<string> FinalizeAsync(string folder,double audioStart,string output,Action<string>? logger=null)
    {
        string video=Path.Combine(folder,"video.mkv"),wave=Path.Combine(folder,"game-audio.wav");
        var info=await ProbeAsync(video);
        // Shift both sources onto the video's first wall-clock frame. Audio that predates
        // video is trimmed; late audio receives matching leading silence.
        var offset=audioStart-info.Start;
        string f(double value)=>value.ToString("0.000000",CultureInfo.InvariantCulture);
        string audioFilter=offset<0 ? $"atrim=start={f(-offset)},asetpts=PTS-STARTPTS,apad" : $"adelay={Math.Round(offset*1000)}:all=1,apad";
        string part=output+".partial.mp4";
        if(File.Exists(output)||File.Exists(part)) throw new IOException("The output already exists. Choose a different filename.");
        var result=await Run(Ffmpeg,new[]{"-hide_banner","-loglevel","warning","-n","-copyts","-itsoffset",f(-info.Start),"-i",video,"-i",wave,"-map","0:v:0","-map","1:a:0","-c:v","copy","-c:a","aac","-b:a","192k","-af",audioFilter,"-t",f(info.Duration),"-avoid_negative_ts","disabled","-movflags","+faststart",part});
        logger?.Invoke("mux: "+result.Error);
        if(result.Code!=0) throw new IOException("Could not save the MP4. Original session files were kept. "+result.Error);
        var verify=await Run(Ffprobe,new[]{"-v","error","-show_entries","stream=codec_type:format=duration","-of","json",part});
        using var doc=JsonDocument.Parse(verify.Out);
        var types=doc.RootElement.GetProperty("streams").EnumerateArray().Select(s=>s.GetProperty("codec_type").GetString()).ToArray();
        if(verify.Code!=0 || !types.Contains("video") || !types.Contains("audio") || new FileInfo(part).Length<1024)
            throw new IOException("The saved MP4 did not pass verification. Session files were kept.");
        File.Move(part,output);
        logger?.Invoke("Saved "+output);
        // Keep logs/timing metadata, remove large intermediates only after verifying the final file.
        File.Delete(video); File.Delete(wave);
        return output;
    }
    public static async Task PreviewAsync(CaptureOptions o,string file)
    {
        var args=new List<string>{"-hide_banner","-loglevel","error","-y"}; args.AddRange(CaptureInput(o));
        args.AddRange(new[]{"-frames:v","1","-vf","scale=640:-1","-update","1",file});
        using var cts=new CancellationTokenSource(TimeSpan.FromSeconds(12));
        var p=await Run(Ffmpeg,args,cts.Token);
        if(p.Code!=0) throw new IOException("Preview failed. Try Compatibility capture or Screen capture.\n"+p.Error);
    }
    public void Dispose()
    {
        stopAudio?.Set();
        // Normal paths await finalization before disposal. This is emergency cleanup only.
        foreach(var p in new[]{video,audio}) {try{if(p is {HasExited:false}) p.Kill(true);}catch{} p?.Dispose();}
        videoJob?.Dispose();stopAudio?.Dispose(); lock(logLock) {log?.Dispose();log=null;}
    }
}
