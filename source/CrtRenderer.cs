using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace GameplayRecorder;

public record CrtSource(string Path,double Duration,bool Still,int Width,int Height,string[] AudioCodecs,long FileLength,DateTime LastWriteUtc,double StartTime);

public static class CrtRenderer
{
    public const string ShaderFilter="format=gbrp,setparams=range=full:color_primaries=bt709:color_trc=iec61966-2-1:colorspace=gbr,hwupload,libplacebo=format=gbrp:custom_shader_path=effect.hook:dithering=none:deband=false:peak_detect=false:apply_filmgrain=false:apply_dolbyvision=false:upscaler=bilinear:downscaler=bilinear,hwdownload,format=gbrp";
    public static async Task<(int Code,string Out,string Error)> RunAsync(string exe,IEnumerable<string> args,string? directory,CancellationToken token)
    {
        if(!File.Exists(exe))throw new FileNotFoundException("A media tool is missing. Run Setup Recording Tools.cmd from the extracted release folder, then try again. See README.md for manual setup.",exe);
        token.ThrowIfCancellationRequested();var info=Recorder.Command(exe,args);if(directory!=null)info.WorkingDirectory=directory;
        using var p=new Process{StartInfo=info};p.Start();var stdout=p.StandardOutput.ReadToEndAsync();var stderr=p.StandardError.ReadToEndAsync();
        try{await p.WaitForExitAsync(token);}catch{try{if(!p.HasExited)p.Kill(true);}catch{}await p.WaitForExitAsync();await stdout;await stderr;throw;}
        return(p.ExitCode,await stdout,await stderr);
    }
    public static async Task<CrtSource> InspectAsync(string path,CancellationToken token)
    {
        string full=System.IO.Path.GetFullPath(path);if(!File.Exists(full))throw new FileNotFoundException("Source file was not found.");
        var original=new FileInfo(full);long length=original.Length;DateTime written=original.LastWriteTimeUtc;
        var result=await RunAsync(Recorder.Ffprobe,new[]{"-v","error","-show_entries","stream=codec_type,codec_name,width,height,sample_aspect_ratio,color_transfer:stream_side_data=rotation:format=duration,format_name,start_time","-of","json",full},null,token);
        if(result.Code!=0)throw new IOException("Could not read the selected file. "+result.Error);
        using var doc=JsonDocument.Parse(result.Out);var stream=doc.RootElement.GetProperty("streams").EnumerateArray().FirstOrDefault(s=>s.GetProperty("codec_type").GetString()=="video");
        if(stream.ValueKind==JsonValueKind.Undefined)throw new IOException("Select an image or a video containing a picture stream.");
        if(stream.TryGetProperty("color_transfer",out var trc)&&trc.GetString() is "smpte2084" or "arib-std-b67")throw new IOException("Video tools supports SDR footage. HDR is not supported yet.");
        if(stream.TryGetProperty("sample_aspect_ratio",out var sar)&&sar.GetString() is string ar&& ar is not ("1:1" or "0:1" or "N/A"))throw new IOException("This preview currently requires square-pixel footage to preserve picture proportions.");
        int width=stream.GetProperty("width").GetInt32(),height=stream.GetProperty("height").GetInt32();
        if(stream.TryGetProperty("side_data_list",out var side))foreach(var entry in side.EnumerateArray())if(entry.TryGetProperty("rotation",out var r)){
            double angle=r.GetDouble();if(Math.Abs(angle/90-Math.Round(angle/90))>.00001)throw new IOException("This CRT tool supports only quarter-turn rotation metadata.");
            if(Math.Abs((int)Math.Round(angle/90))%2==1)(width,height)=(height,width);
        }
        if(width<=0||height<=0||(long)width*height>33554432)throw new IOException("Preview is limited to 32-megapixel frames.");
        var format=doc.RootElement.GetProperty("format");double duration=0;
        if(format.TryGetProperty("duration",out var d))double.TryParse(d.GetString(),NumberStyles.Float,CultureInfo.InvariantCulture,out duration);
        string type=format.GetProperty("format_name").GetString()??"";
        bool still=type.Contains("image2")||type.EndsWith("_pipe")||Path.GetExtension(full).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".webp";
        var audio=doc.RootElement.GetProperty("streams").EnumerateArray().Where(s=>s.GetProperty("codec_type").GetString()=="audio").Select(s=>s.GetProperty("codec_name").GetString()??"").ToArray();
        original.Refresh();if(original.Length!=length||original.LastWriteTimeUtc!=written)throw new IOException("Source changed while being inspected. Open it again.");
        double startTime=0;if(format.TryGetProperty("start_time",out var start))double.TryParse(start.GetString(),NumberStyles.Float,CultureInfo.InvariantCulture,out startTime);
        return new(full,still?0:duration,still,width,height,audio,length,written,startTime);
    }
    public static async Task<double> LastFrameTimeAsync(CrtSource source,TrimSelection range,CancellationToken token)
    {
        range.Validate(source.Duration);
        string F(double t)=>t.ToString("0.000000",CultureInfo.InvariantCulture);
        double end=source.StartTime+range.End;
        // Read past the boundary to include reference packets for reordered B-frames.
        string interval=F(Math.Max(source.StartTime,end-2))+"%"+F(end+2);
        var result=await RunAsync(Recorder.Ffprobe,new[]{"-v","error","-read_intervals",interval,"-select_streams","v:0","-show_frames","-show_entries","frame=best_effort_timestamp_time","-of","json",source.Path},null,token);
        if(result.Code!=0)throw new IOException("Could not locate the last frame in the selected range.");
        using var doc=JsonDocument.Parse(result.Out);double latest=double.NegativeInfinity;
        foreach(var frame in doc.RootElement.GetProperty("frames").EnumerateArray()){
            if(frame.TryGetProperty("best_effort_timestamp_time",out var p)&&double.TryParse(p.GetString(),NumberStyles.Float,CultureInfo.InvariantCulture,out var pts)){
                double time=pts-source.StartTime;if(time>=range.Start-.000001&&time<range.End-.000001)latest=Math.Max(latest,time);
            }
        }
        if(!double.IsFinite(latest))throw new IOException("The selected range contains no decoded video frame. Choose a wider range.");
        // Seek just before the frame: decimal rounding must not skip it.
        return Math.Max(0,latest-.000002);
    }
    public static async Task ExtractAsync(CrtSource source,double seconds,string output,CancellationToken token)
    {
        var args=new List<string>{"-hide_banner","-loglevel","error","-n","-i",source.Path,"-map","0:v:0"};
        if(!source.Still){if(!double.IsFinite(seconds)||seconds<0||source.Duration>0&&seconds>=source.Duration)throw new ArgumentException("Select a frame time within the video.");args.AddRange(new[]{"-ss",seconds.ToString("0.000000",CultureInfo.InvariantCulture)});}
        args.AddRange(new[]{"-frames:v","1","-an","-vf","format=rgb24","-update","1",output});
        var result=await RunAsync(Recorder.Ffmpeg,args,null,token);
        if(result.Code!=0||!File.Exists(output))throw new IOException("Could not decode the selected frame. Try an earlier time. "+result.Error);
    }
    public static async Task RenderAsync(string input,string output,CrtSettings settings,string work,CancellationToken token)
    {
        var snapshot=settings.Clone();string hook=Path.Combine(work,"effect.hook");await File.WriteAllTextAsync(hook,snapshot.Hook(),token);
        string vf=ShaderFilter;
        var result=await RunAsync(Recorder.Ffmpeg,new[]{"-hide_banner","-loglevel","error","-n","-init_hw_device","vulkan=vk:0","-filter_hw_device","vk","-i",input,"-vf",vf,"-frames:v","1","-update","1",output},work,token);
        if(result.Code!=0||!File.Exists(output))throw new IOException("CRT rendering failed. A Vulkan-capable GPU/driver and FFmpeg libplacebo are required. "+result.Error);
    }
    public static async Task<AddedAudioSource> InspectAudioAsync(string path,CancellationToken token)
    {
        string full=Path.GetFullPath(path);var file=new FileInfo(full);long length=file.Length;DateTime written=file.LastWriteTimeUtc;
        var result=await RunAsync(Recorder.Ffprobe,new[]{"-v","error","-show_entries","stream=codec_type,duration:format=duration","-of","json",full},null,token);
        if(result.Code!=0)throw new IOException("Could not read this audio file. "+result.Error);
        using var doc=JsonDocument.Parse(result.Out);
        var audio=doc.RootElement.GetProperty("streams").EnumerateArray().FirstOrDefault(s=>s.GetProperty("codec_type").GetString()=="audio");
        if(audio.ValueKind==JsonValueKind.Undefined)throw new IOException("Choose a file containing an audio track.");
        double duration=0;
        if(audio.TryGetProperty("duration",out var a))double.TryParse(a.GetString(),NumberStyles.Float,CultureInfo.InvariantCulture,out duration);
        if(duration<=0&&doc.RootElement.TryGetProperty("format",out var f)&&f.TryGetProperty("duration",out var d))double.TryParse(d.GetString(),NumberStyles.Float,CultureInfo.InvariantCulture,out duration);
        if(!double.IsFinite(duration)||duration<=0||duration>86400)throw new IOException("Choose completed audio with a readable duration of up to 24 hours.");
        file.Refresh();if(file.Length!=length||file.LastWriteTimeUtc!=written)throw new IOException("Audio changed while being read. Choose it again.");
        return new(full,duration,length,written);
    }
    public static async Task RenderEditedAsync(string input,string output,CrtSource source,CrtSettings settings,VideoFilterKind filter,VideoEditOptions edit,string work,CancellationToken token)
    {
        bool crt=filter==VideoFilterKind.Crt;
        if(crt)await File.WriteAllTextAsync(Path.Combine(work,"effect.hook"),settings.Clone().Hook(),token);
        var args=new List<string>{"-hide_banner","-loglevel","error","-n"};
        if(crt)args.AddRange(new[]{"-init_hw_device","vulkan=vk:0","-filter_hw_device","vk"});
        args.AddRange(new[]{"-i",input,"-vf",edit.VisualFilter(source.Width,source.Height,crt),"-frames:v","1","-update","1",output});
        var result=await RunAsync(Recorder.Ffmpeg,args,work,token);
        if(result.Code!=0||!File.Exists(output))throw new IOException("Preview failed. "+(crt?"CRT requires a Vulkan-capable GPU/driver. ":"")+result.Error);
    }
    public static Bitmap ReadBitmap(string file){using var image=Image.FromFile(file);return new Bitmap(image);}
}
