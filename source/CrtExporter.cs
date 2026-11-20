using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace GameplayRecorder;

public sealed record CrtExportResult(string Path,bool AudioCopied,int AudioTracks,double Seconds);
public sealed record CrtExportProgress(double Fraction,string Message);

public static class CrtExporter
{
    public static async Task<CrtExportResult> ExportAsync(CrtSource source,string destination,CrtSettings settings,string encoder,IProgress<CrtExportProgress>? progress,CancellationToken token,TrimSelection? selection=null,VideoFilterKind filterKind=VideoFilterKind.Crt,VideoEditOptions? options=null,PreviewSegment? preview=null,bool autoRename=false)
    {
        // Snapshot before the first await; later UI changes cannot alter this job.
        bool applyFilter=filterKind switch{VideoFilterKind.Crt=>true,VideoFilterKind.Original=>false,_=>throw new ArgumentException("Choose a supported filter.")};
        var snapshot=applyFilter?settings.Clone():new CrtSettings();snapshot.Validate();
        var edit=options??new VideoEditOptions();var audio=edit.Audio??new AudioEdit();
        string input=Path.GetFullPath(source.Path),requested=Path.GetFullPath(destination);
        string[] protectedPaths=audio.Added==null?new[]{input}:new[]{input,Path.GetFullPath(audio.Added.Path)};
        string output=autoRename?OutputFiles.Available(requested,protectedPaths):requested;
        if(protectedPaths.Contains(output,StringComparer.OrdinalIgnoreCase)||File.Exists(output)||Directory.Exists(output))throw new IOException("Choose a separate, unused output filename. Existing files are never replaced.");
        if(Path.GetExtension(output).ToLowerInvariant()!=".mp4")throw new ArgumentException("Choose an MP4 output filename.");
        if(encoder is not ("libx264" or "h264_nvenc"))throw new ArgumentException("Choose CPU or NVIDIA encoding.");
        string parent=Path.GetDirectoryName(output)!;
        if(!Directory.Exists(parent))throw new DirectoryNotFoundException("Choose an existing output folder.");
        token.ThrowIfCancellationRequested();
        // Windows sharing semantics prevent source writes/deletes while the export reads it.
        using var sourceLock=new FileStream(input,FileMode.Open,FileAccess.Read,FileShare.Read);
        var current=await CrtRenderer.InspectAsync(input,token);
        if(current.FileLength!=source.FileLength||current.LastWriteUtc!=source.LastWriteUtc)throw new IOException("Source changed since the preview was opened. Open it again before exporting.");
        source=current;
        if(source.Still)throw new ArgumentException("Select a video to export; images can be saved as PNG.");
        if(!double.IsFinite(source.Duration)||source.Duration<=0||source.Duration>86400)throw new ArgumentException("Export requires a completed recording with a readable duration of up to 24 hours.");
        var fullRange=selection??new TrimSelection(0,source.Duration);fullRange.Validate(source.Duration);
        var range=preview?.Within(fullRange)??fullRange;range.Validate(source.Duration);
        bool trimmed=!range.IsWhole(source.Duration);
        double expectedDuration=range.Duration;
        var outputSize=edit.Dimensions(source.Width,source.Height,requireEven:true);audio.Validate(fullRange.Duration,source.AudioCodecs.Length);
        bool originalMuted=audio.MuteOriginal||audio.OriginalVolume==0;
        // Keep both inputs locked through the final rename, and protect either against overwrite.
        using var addedGuard=audio.Added==null?null:new FileStream(audio.Added.Path,FileMode.Open,FileAccess.Read,FileShare.Read);
        if(audio.Added is {} added){
            if(Path.GetFullPath(added.Path).Equals(output,StringComparison.OrdinalIgnoreCase))throw new IOException("Choose an output separate from the added audio.");
            var actual=await CrtRenderer.InspectAudioAsync(added.Path,token);
            if(actual.FileLength!=added.FileLength||actual.LastWriteUtc!=added.LastWriteUtc)throw new IOException("Added audio changed since it was selected. Choose it again.");
        }
        var drive=new DriveInfo(Path.GetPathRoot(output)!);
        if(drive.AvailableFreeSpace<1024L*1024*1024)throw new IOException("Less than 1 GB is free on the output drive. Choose another folder or free some space.");
        string work=Path.Combine(parent,".crt-export-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(work);
        var watch=Stopwatch.StartNew();bool copied=!trimmed&&!originalMuted&&audio.Added==null&&audio.OriginalVolume==1&&source.AudioCodecs.Length>0&&source.AudioCodecs.All(c=>c=="aac");
        try{
            if(applyFilter)await File.WriteAllTextAsync(Path.Combine(work,"effect.hook"),snapshot.Hook(),token);
            string pending=Path.Combine(work,"processed.mp4");
            string F(double value)=>value.ToString("0.000000",CultureInfo.InvariantCulture);
            string timing=trimmed?$"trim=start={F(range.Start)}:end={F(range.End)},setpts=PTS-({F(range.Start)})/TB,":"";
            string filter=timing+edit.VisualFilter(source.Width,source.Height,applyFilter)+",scale=in_range=pc:out_range=tv:out_color_matrix=bt709,format=yuv420p";
            var args=new List<string>{"-hide_banner","-loglevel","error","-nostats","-n"};
            if(applyFilter)args.AddRange(new[]{"-init_hw_device","vulkan=vk:0","-filter_hw_device","vk"});
            args.AddRange(new[]{"-copyts","-start_at_zero","-i",input});
            if(audio.Added!=null)args.AddRange(new[]{"-i",audio.Added.Path});
            args.AddRange(new[]{"-map","0:v:0","-map_metadata","-1","-map_chapters","-1","-vf",filter,"-c:v",encoder});
            string quality=edit.QualityValue.ToString(CultureInfo.InvariantCulture);
            if(encoder=="libx264")args.AddRange(new[]{"-preset","medium","-crf",quality});
            else args.AddRange(new[]{"-preset","p4","-rc","vbr","-cq",quality,"-b:v","0"});
            string cutAudio=trimmed?$"atrim=start={F(range.Start)}:end={F(range.End)},asetpts=PTS-({F(range.Start)})/TB,":"";
            int expectedTracks;
            if(audio.Added!=null){
                double audible=Math.Min(audio.Added.Duration,fullRange.Duration-audio.Delay);
                string addedFilter=$"[1:a:0]asetpts=PTS-STARTPTS,atrim=duration={F(audible)},aformat=sample_rates=48000:channel_layouts=stereo,volume={F(audio.AddedVolume)}";
                if(audio.FadeIn>0)addedFilter+=$",afade=t=in:st=0:d={F(audio.FadeIn)}";
                if(audio.FadeOut>0)addedFilter+=$",afade=t=out:st={F(audible-audio.FadeOut)}:d={F(audio.FadeOut)}";
                if(audio.Delay>0)addedFilter+=$",adelay=delays={F(audio.Delay*1000)}:all=1";
                if(preview!=null)addedFilter+=$",atrim=start={F(preview.Start)}:end={F(preview.Start+expectedDuration)},asetpts=PTS-({F(preview.Start)})/TB";
                string graph=addedFilter+"[added];";
                if(!originalMuted&&source.AudioCodecs.Length>0){
                    graph+=$"[0:a:{audio.OriginalTrack}]{cutAudio}aformat=sample_rates=48000:channel_layouts=stereo,volume={F(audio.OriginalVolume)}[original];[original][added]amix=inputs=2:duration=longest:dropout_transition=0:normalize=0,";
                }else graph+="[added]";
                graph+=$"alimiter=limit=0.95:level=0:latency=1,atrim=duration={F(expectedDuration)}[mix]";
                args.AddRange(new[]{"-filter_complex",graph,"-map","[mix]","-c:a","aac","-b:a","192k"});expectedTracks=1;
            }else if(originalMuted){args.Add("-an");expectedTracks=0;}
            else{
                args.AddRange(new[]{"-map","0:a?"});expectedTracks=source.AudioCodecs.Length;
                for(int i=0;i<source.AudioCodecs.Length;i++){
                    if(!trimmed&&audio.OriginalVolume==1&&source.AudioCodecs[i]=="aac")args.AddRange(new[]{"-c:a:"+i,"copy"});
                    else args.AddRange(new[]{"-c:a:"+i,"aac","-b:a:"+i,"192k"});
                }
                if(source.AudioCodecs.Length>0&&(trimmed||audio.OriginalVolume!=1)){
                    string af=cutAudio+$"volume={F(audio.OriginalVolume)}";
                    if(audio.OriginalVolume>1)af+=",alimiter=limit=0.95:level=0:latency=1";
                    args.AddRange(new[]{"-af",af});
                }
            }
            if(trimmed||audio.Added!=null)args.AddRange(new[]{"-t",F(expectedDuration)});
            args.AddRange(new[]{"-fps_mode:v","passthrough","-enc_time_base:v","demux","-colorspace","bt709","-color_primaries","bt709","-color_trc","iec61966-2-1","-color_range","tv",
                "-avoid_negative_ts","disabled","-movflags","+faststart","-stats_period","0.25","-progress","pipe:1",pending});
            var start=Recorder.Command(Recorder.Ffmpeg,args);start.WorkingDirectory=work;
            using(var process=new Process{StartInfo=start}){
                token.ThrowIfCancellationRequested();process.Start();
                CaptureJob owner;
                try{owner=new CaptureJob(process);}catch{try{if(!process.HasExited)process.Kill(true);}catch{}await process.WaitForExitAsync();throw;}
                using var processOwner=owner;
                var errors=process.StandardError.ReadToEndAsync();
                double completed=0;
                async Task ReadProgress(){string? line;while((line=await process.StandardOutput.ReadLineAsync())!=null){
                    if(line.StartsWith("out_time_us=")&&double.TryParse(line[12..],NumberStyles.Float,CultureInfo.InvariantCulture,out var us)){
                        completed=Math.Max(completed,Math.Clamp(us/1000000/expectedDuration,0,.98));
                        progress?.Report(new(completed,"Rendering and encoding…"));
                    }
                }}
                var read=ReadProgress();
                try{await process.WaitForExitAsync(token);await read;string error=await errors;if(process.ExitCode!=0)throw new IOException("Video export failed. Original unchanged. "+error);}
                catch{try{if(!process.HasExited)process.Kill(true);}catch{}await process.WaitForExitAsync();await read;await errors;throw;}
            }
            token.ThrowIfCancellationRequested();progress?.Report(new(.99,"Checking exported file…"));
            CrtSource check;
            try{check=await CrtRenderer.InspectAsync(pending,token);}catch(IOException e) when(trimmed){throw new IOException("The selected section did not produce a readable video. Choose a wider range containing video frames. Original unchanged.",e);}
            // Audio tracks with no samples inside a cut may legitimately be omitted by MP4.
            bool audioMismatch=(preview!=null||trimmed&&audio.Added==null)?check.AudioCodecs.Length>expectedTracks:check.AudioCodecs.Length!=expectedTracks;
            if(check.Width!=outputSize.Width||check.Height!=outputSize.Height||audioMismatch||Math.Abs(check.Duration-expectedDuration)>.3||new FileInfo(pending).Length<1024)
                throw new IOException("Export did not pass its stream, dimension or duration checks. Original unchanged.");
            var decode=await CrtRenderer.RunAsync(Recorder.Ffmpeg,new[]{"-v","error","-i",pending,"-map","0:v:0","-frames:v","1","-f","framemd5","-"},null,token);
            if(decode.Code!=0||!decode.Out.Split('\n').Any(line=>line.Trim().Length>0&&!line.StartsWith('#')))throw new IOException("The exported video contained no decodable frame. Choose a wider range. Original unchanged.");
            token.ThrowIfCancellationRequested();
            // Same-volume rename is the commit point. A late collision cannot replace a file.
            output=OutputFiles.Commit(pending,autoRename?requested:output,autoRename,protectedPaths);
            progress?.Report(new(1,"Saved"));return new(output,copied,check.AudioCodecs.Length,watch.Elapsed.TotalSeconds);
        }finally{
            // work was generated by this job inside the selected destination directory.
            try{Directory.Delete(work,true);}catch{}
        }
    }
}
