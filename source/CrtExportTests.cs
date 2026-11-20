using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace GameplayRecorder;

internal sealed class DirectProgress<T>(Action<T> action):IProgress<T>{public void Report(T value)=>action(value);}

public static class CrtExportTests
{
    public static async Task RunAsync(string[] args)
    {
        string Arg(string key)=>args[Array.IndexOf(args,key)+1];
        string folder=Path.GetFullPath(Arg("--crt-export-tests")),fixtures=Path.GetFullPath(Arg("--fixtures"));Directory.CreateDirectory(folder);
        var report=new Dictionary<string,object>();
        void Check(string name,bool good){if(!good)throw new Exception(name);report[name]=true;}
        string Hash(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        var originals=Directory.GetFiles(fixtures).ToDictionary(p=>p,Hash);
        var settings=new CrtSettings();settings.Enable("curvature",true);
        async Task<double[]> Frames(string file){var r=await CrtRenderer.RunAsync(Recorder.Ffprobe,new[]{"-v","error","-select_streams","v:0","-show_frames","-show_entries","frame=best_effort_timestamp_time","-of","json",file},null,CancellationToken.None);using var doc=JsonDocument.Parse(r.Out);return doc.RootElement.GetProperty("frames").EnumerateArray().Select(f=>double.Parse(f.GetProperty("best_effort_timestamp_time").GetString()!,CultureInfo.InvariantCulture)).ToArray();}
        async Task<string[]> AudioHashes(string file,string selector="a"){var r=await CrtRenderer.RunAsync(Recorder.Ffprobe,new[]{"-v","error","-select_streams",selector,"-show_packets","-show_data_hash","sha256","-show_entries","packet=stream_index,data_hash","-of","json",file},null,CancellationToken.None);using var doc=JsonDocument.Parse(r.Out);return doc.RootElement.GetProperty("packets").EnumerateArray().Select(p=>p.GetProperty("stream_index").GetInt32()+":"+p.GetProperty("data_hash").GetString()).ToArray();}
        async Task<double> Offset(string file){var r=await CrtRenderer.RunAsync(Recorder.Ffprobe,new[]{"-v","error","-show_entries","stream=codec_type,start_time","-of","json",file},null,CancellationToken.None);using var doc=JsonDocument.Parse(r.Out);var streams=doc.RootElement.GetProperty("streams").EnumerateArray().ToArray();double Start(JsonElement e)=>double.Parse(e.GetProperty("start_time").GetString()!,CultureInfo.InvariantCulture);return Start(streams.First(s=>s.GetProperty("codec_type").GetString()=="audio"))-Start(streams.First(s=>s.GetProperty("codec_type").GetString()=="video"));}
        try{
            foreach(var name in new[]{"normal.mp4","vfr-delayed.mp4","silent.mp4","pcm.mkv","multiple-audio.mp4","mixed-audio.mkv"}){
                string input=Path.Combine(fixtures,name),output=Path.Combine(folder,Path.GetFileNameWithoutExtension(name)+"-CRT.mp4");
                var source=await CrtRenderer.InspectAsync(input,CancellationToken.None);var progress=new List<double>();
                var result=await CrtExporter.ExportAsync(source,output,settings,"libx264",new DirectProgress<CrtExportProgress>(p=>progress.Add(p.Fraction)),CancellationToken.None);
                Check(name+"_completed",File.Exists(result.Path)&&progress.Count>0&&progress.Last()==1&&progress.Zip(progress.Skip(1)).All(p=>p.First<=p.Second));
                var times=await Frames(input);var rendered=await Frames(output);
                Check(name+"_frameTimesPreserved",times.Length==rendered.Length&&times.Zip(rendered).All(p=>Math.Abs((p.First-times[0])-(p.Second-rendered[0]))<.001));
                var decoded=await CrtRenderer.RunAsync(Recorder.Ffmpeg,new[]{"-v","error","-i",output,"-f","null","-"},null,CancellationToken.None);
                Check(name+"_fullDecode",decoded.Code==0&&string.IsNullOrWhiteSpace(decoded.Error));
                if(source.AudioCodecs.Length>0){
                    Check(name+"_audioOffsetPreserved",Math.Abs(await Offset(input)-await Offset(output))<.025);
                    if(source.AudioCodecs.All(c=>c=="aac")){var originalPackets=await AudioHashes(input);var outputPackets=await AudioHashes(output);Check(name+"_audioPacketsCopiedExactly",originalPackets.SequenceEqual(outputPackets));}
                    else{
                        Check(name+"_audioConvertedToAac",(await CrtRenderer.InspectAsync(output,CancellationToken.None)).AudioCodecs.All(c=>c=="aac")&&!result.AudioCopied);
                        if(name=="mixed-audio.mkv"){var originalPackets=await AudioHashes(input,"a:0");var outputPackets=await AudioHashes(output,"a:0");Check("mixedAudioKeepsAacPackets",originalPackets.SequenceEqual(outputPackets));}
                    }
                }else Check("videoOnlyRemainsVideoOnly",(await CrtRenderer.InspectAsync(output,CancellationToken.None)).AudioCodecs.Length==0);
                report[name+"_elapsedSeconds"]=result.Seconds;
            }
            var normal=await CrtRenderer.InspectAsync(Path.Combine(fixtures,"normal.mp4"),CancellationToken.None);
            var mutableSettings=settings.Clone();string snapshotOutput=Path.Combine(folder,"snapshot.mp4");
            var snapshotJob=CrtExporter.ExportAsync(normal,snapshotOutput,mutableSettings,"libx264",null,CancellationToken.None);mutableSettings.Enable("bloom",false);await snapshotJob;
            async Task<string> VideoHash(string file){var result=await CrtRenderer.RunAsync(Recorder.Ffmpeg,new[]{"-v","error","-i",file,"-map","0:v:0","-c:v","copy","-f","hash","-hash","sha256","-"},null,CancellationToken.None);if(result.Code!=0)throw new IOException(result.Error);return result.Out;}
            Check("exportUsesFrozenSettings",await VideoHash(snapshotOutput)==await VideoHash(Path.Combine(folder,"normal-CRT.mp4")));
            string existing=Path.Combine(folder,"normal-CRT.mp4"),existingHash=Hash(existing);
            try{await CrtExporter.ExportAsync(normal,existing,settings,"libx264",null,CancellationToken.None);throw new Exception("Overwrite accepted");}catch(IOException){Check("existingOutputUnchanged",Hash(existing)==existingHash);}
            try{await CrtExporter.ExportAsync(normal,normal.Path,settings,"libx264",null,CancellationToken.None);throw new Exception("Source overwrite accepted");}catch(IOException){report["sourceOverwriteRejected"]=true;}
            string cancellation=Path.Combine(folder,"cancelled.mp4");using(var stop=new CancellationTokenSource()){
                var source=await CrtRenderer.InspectAsync(Path.Combine(fixtures,"cancel.mp4"),CancellationToken.None);
                var progress=new DirectProgress<CrtExportProgress>(_=>stop.Cancel());
                try{await CrtExporter.ExportAsync(source,cancellation,settings,"libx264",progress,stop.Token);throw new Exception("Cancellation did not cancel");}catch(OperationCanceledException){Check("cancelLeavesNoPartialOrFinal",!File.Exists(cancellation)&&!Directory.GetDirectories(folder,".crt-export-*").Any());}
            }
            string collision=Path.Combine(folder,"late-collision.mp4");bool collided=false;
            try{await CrtExporter.ExportAsync(normal,collision,settings,"libx264",new DirectProgress<CrtExportProgress>(_=>{if(collided)return;File.WriteAllText(collision,"unrelated file");collided=true;}),CancellationToken.None);throw new Exception("Late collision overwrote a file");}
            catch(IOException){Check("lateCollisionProtected",File.ReadAllText(collision)=="unrelated file"&&!Directory.GetDirectories(folder,".crt-export-*").Any());}
            string mutable=Path.Combine(folder,"source-changed.mp4");File.Copy(normal.Path,mutable);var old=await CrtRenderer.InspectAsync(mutable,CancellationToken.None);using(var stream=new FileStream(mutable,FileMode.Append)){stream.WriteByte(0);}
            try{await CrtExporter.ExportAsync(old,Path.Combine(folder,"changed-output.mp4"),settings,"libx264",null,CancellationToken.None);throw new Exception("Changed source accepted");}catch(IOException){report["changedSourceRejected"]=true;}
            if(args.Contains("--include-nvidia")){
                string path=Path.Combine(folder,"nvidia-CRT.mp4");await CrtExporter.ExportAsync(normal,path,settings,"h264_nvenc",null,CancellationToken.None);
                var decoded=await CrtRenderer.RunAsync(Recorder.Ffmpeg,new[]{"-v","error","-i",path,"-f","null","-"},null,CancellationToken.None);Check("nvidiaExportDecodes",decoded.Code==0&&string.IsNullOrWhiteSpace(decoded.Error));
            }
            Check("allFixtureSourcesUnchanged",originals.All(p=>Hash(p.Key)==p.Value));Check("temporaryJobsCleaned",!Directory.GetDirectories(folder,".crt-export-*").Any());
            report["success"]=true;
        }catch(Exception e){report["success"]=false;report["error"]=e.ToString();Environment.ExitCode=1;}
        await File.WriteAllTextAsync(Path.Combine(folder,"export-report.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
    }
}
