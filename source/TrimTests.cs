using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace GameplayRecorder;

public static class TrimTests
{
    public static async Task RunAsync(string[] args)
    {
        string Arg(string key)=>args[Array.IndexOf(args,key)+1];
        string folder=Path.GetFullPath(Arg("--trim-tests")),fixtures=Path.GetFullPath(Arg("--fixtures"));Directory.CreateDirectory(folder);
        var report=new Dictionary<string,object>();var settings=new CrtSettings();settings.Enable("curvature",true);
        void Check(string name,bool value){if(!value)throw new Exception(name);report[name]=true;}
        string Hash(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        var originals=Directory.GetFiles(fixtures).ToDictionary(p=>p,Hash);
        async Task<double[]> Frames(string path){var r=await CrtRenderer.RunAsync(Recorder.Ffprobe,new[]{"-v","error","-select_streams","v:0","-show_frames","-show_entries","frame=best_effort_timestamp_time","-of","json",path},null,CancellationToken.None);using var doc=JsonDocument.Parse(r.Out);return doc.RootElement.GetProperty("frames").EnumerateArray().Select(f=>double.Parse(f.GetProperty("best_effort_timestamp_time").GetString()!,CultureInfo.InvariantCulture)).ToArray();}
        try{
            foreach(var item in new[]{("normal.mp4",1.015,2.76,VideoFilterKind.Original),("normal.mp4",.205,2.41,VideoFilterKind.Crt),("vfr-delayed.mp4",.205,2.41,VideoFilterKind.Crt),("silent.mp4",.205,1.61,VideoFilterKind.Original),("mixed-audio.mkv",.205,1.61,VideoFilterKind.Original),("short-audio.mp4",1.505,2.41,VideoFilterKind.Original)}){
                var source=await CrtRenderer.InspectAsync(Path.Combine(fixtures,item.Item1),CancellationToken.None);var range=new TrimSelection(item.Item2,item.Item3);
                string name=Path.GetFileNameWithoutExtension(item.Item1)+"-"+item.Item4;string output=Path.Combine(folder,name+".mp4");
                var result=await CrtExporter.ExportAsync(source,output,settings,"libx264",null,CancellationToken.None,range,item.Item4);
                Check(name+"_audioReencodedWhenCut",!result.AudioCopied);
                var expected=(await Frames(source.Path)).Select(t=>t-source.StartTime).Where(t=>t>=range.Start-.000001&&t<range.End-.000001).Select(t=>t-range.Start).ToArray();
                var actual=await Frames(output);
                Check(name+"_preciseDecodedFrameRange",expected.Length>0&&actual.Length==expected.Length&&actual.Zip(expected).All(p=>Math.Abs(p.First-p.Second)<.001));
                var probe=await CrtRenderer.InspectAsync(output,CancellationToken.None);
                Check(name+"_duration",Math.Abs(probe.Duration-range.Duration)<.06);
                Check(name+"_dimensionsAndTracks",probe.Width==source.Width&&probe.Height==source.Height&&probe.AudioCodecs.Length==(item.Item1=="short-audio.mp4"?0:source.AudioCodecs.Length));
                var decode=await CrtRenderer.RunAsync(Recorder.Ffmpeg,new[]{"-v","error","-i",output,"-f","null","-"},null,CancellationToken.None);
                Check(name+"_decode",decode.Code==0&&string.IsNullOrWhiteSpace(decode.Error));
                double last=await CrtRenderer.LastFrameTimeAsync(source,range,CancellationToken.None);
                Check(name+"_lastKeptFrameLookup",Math.Abs(last-(expected.Last()+range.Start))<.00001);
            }
            var normal=await CrtRenderer.InspectAsync(Path.Combine(fixtures,"normal.mp4"),CancellationToken.None);
            foreach(var range in new[]{new TrimSelection(2,1),new TrimSelection(-1,2),new TrimSelection(0,9),new TrimSelection(double.NaN,2)}){
                try{await CrtExporter.ExportAsync(normal,Path.Combine(folder,"invalid.mp4"),settings,"libx264",null,CancellationToken.None,range);throw new Exception("Invalid range accepted");}catch(ArgumentException){report["invalidRangesRejected"]=true;}
            }
            string? driver=Environment.GetEnvironmentVariable("VK_DRIVER_FILES"),icd=Environment.GetEnvironmentVariable("VK_ICD_FILENAMES");
            try{
                Environment.SetEnvironmentVariable("VK_DRIVER_FILES",Path.Combine(folder,"no-vulkan-driver.json"));Environment.SetEnvironmentVariable("VK_ICD_FILENAMES",Path.Combine(folder,"no-vulkan-driver.json"));
                await CrtExporter.ExportAsync(normal,Path.Combine(folder,"original-no-vulkan.mp4"),settings,"libx264",null,CancellationToken.None,new TrimSelection(.205,2.41),VideoFilterKind.Original);
                report["originalTrimDoesNotRequireVulkan"]=true;
            }finally{Environment.SetEnvironmentVariable("VK_DRIVER_FILES",driver);Environment.SetEnvironmentVariable("VK_ICD_FILENAMES",icd);}
            if(File.Exists(Path.Combine(fixtures,"marker.mkv"))){
                var marker=await CrtRenderer.InspectAsync(Path.Combine(fixtures,"marker.mkv"),CancellationToken.None);string output=Path.Combine(folder,"marker-trim.mp4");
                var range=new TrimSelection(1.05,2.75);await CrtExporter.ExportAsync(marker,output,settings,"libx264",null,CancellationToken.None,range,VideoFilterKind.Original);
                var info=Recorder.Command(Recorder.Ffmpeg,new[]{"-v","error","-i",output,"-map","0:a:0","-ac","1","-ar","48000","-f","s16le","-"});using var p=new System.Diagnostics.Process{StartInfo=info};p.Start();using var bytes=new MemoryStream();var copy=p.StandardOutput.BaseStream.CopyToAsync(bytes);var errors=p.StandardError.ReadToEndAsync();await p.WaitForExitAsync();await copy;await errors;
                byte[] data=bytes.ToArray();int onset=-1;for(int i=0;i+960<data.Length;i+=480){double sum=0;for(int k=i;k<i+480;k+=2){double value=BitConverter.ToInt16(data,k);sum+=value*value;}if(Math.Sqrt(sum/240)>2000){onset=i/2;break;}}
                double audioTime=(double)onset/48000;report["trimmedMarkerAudioOnsetSeconds"]=audioTime;
                Check("trimmedAudioMarkerRemainsSynchronized",onset>=0&&Math.Abs(audioTime-.45)<.015);
            }
            Check("originalFilesUnchanged",originals.All(p=>Hash(p.Key)==p.Value));Check("temporaryJobsRemoved",!Directory.GetDirectories(folder,".crt-export-*").Any());report["success"]=true;
        }catch(Exception e){report["success"]=false;report["error"]=e.ToString();Environment.ExitCode=1;}
        await File.WriteAllTextAsync(Path.Combine(folder,"trim-report.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
    }
}
