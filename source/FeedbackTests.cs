using System.Security.Cryptography;
using System.Text.Json;

namespace GameplayRecorder;

public static class FeedbackTests
{
    public static async Task RunAsync(string[] args)
    {
        string Arg(string key)=>args[Array.IndexOf(args,key)+1];
        string folder=Path.GetFullPath(Arg("--feedback-tests")),fixtures=Path.GetFullPath(Arg("--fixtures"));Directory.CreateDirectory(folder);
        var report=new Dictionary<string,object>();var token=CancellationToken.None;var settings=new CrtSettings();
        void Check(string name,bool ok){if(!ok)throw new Exception(name);report[name]=true;}
        string Hash(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        async Task<float[]> Samples(string file){using var p=new System.Diagnostics.Process{StartInfo=Recorder.Command(Recorder.Ffmpeg,new[]{"-v","error","-i",file,"-map","0:a:0","-ac","1","-ar","48000","-f","f32le","-"})};p.Start();using var memory=new MemoryStream();var copying=p.StandardOutput.BaseStream.CopyToAsync(memory);var errors=p.StandardError.ReadToEndAsync();await p.WaitForExitAsync();await copying;if(p.ExitCode!=0)throw new IOException(await errors);await errors;byte[] bytes=memory.ToArray();float[] data=new float[bytes.Length/4];Buffer.BlockCopy(bytes,0,data,0,bytes.Length);return data;}
        double Rms(float[] samples,double start,double end)=>Math.Sqrt(samples.Skip((int)(start*48000)).Take((int)((end-start)*48000)).Average(v=>(double)v*v));
        async Task<string> Export(string name,CrtSource source,VideoEditOptions edit,TrimSelection? range=null,PreviewSegment? part=null){string output=Path.Combine(folder,name+".mp4");await CrtExporter.ExportAsync(source,output,settings,"libx264",null,token,range,VideoFilterKind.Original,edit,part);return output;}
        try{
            var source=await CrtRenderer.InspectAsync(Path.Combine(fixtures,"normal.mp4"),token);string originalHash=Hash(source.Path);
            var music=await CrtRenderer.InspectAudioAsync(Path.Combine(fixtures,"music.wav"),token);
            string existing=Path.Combine(folder,"same-name.mp4");await File.WriteAllTextAsync(existing,"keep this file");
            var first=await CrtExporter.ExportAsync(source,existing,settings,"libx264",null,token,filterKind:VideoFilterKind.Original,autoRename:true);
            var second=await CrtExporter.ExportAsync(source,existing,settings,"libx264",null,token,filterKind:VideoFilterKind.Original,autoRename:true);
            Check("existingNameNumbered",Path.GetFileName(first.Path)=="same-name (1).mp4"&&Path.GetFileName(second.Path)=="same-name (2).mp4"&&File.ReadAllText(existing)=="keep this file");
            var third=await CrtExporter.ExportAsync(source,second.Path,settings,"libx264",null,token,filterKind:VideoFilterKind.Original,autoRename:true);Check("numberedNamesContinueSequence",Path.GetFileName(third.Path)=="same-name (3).mp4");
            string late=Path.Combine(folder,"late-name.mp4");bool created=false;
            var lateResult=await CrtExporter.ExportAsync(source,late,settings,"libx264",new DirectProgress<CrtExportProgress>(_=>{if(!created){File.WriteAllText(late,"late unrelated file");created=true;}}),token,filterKind:VideoFilterKind.Original,autoRename:true);
            Check("lateCollisionNumberedWithoutOverwrite",File.ReadAllText(late)=="late unrelated file"&&Path.GetFileName(lateResult.Path)=="late-name (1).mp4");
            string copy=Path.Combine(folder,"source.mp4");File.Copy(source.Path,copy);var input=await CrtRenderer.InspectAsync(copy,token);string copyHash=Hash(copy);
            var protectedResult=await CrtExporter.ExportAsync(input,copy,settings,"libx264",null,token,filterKind:VideoFilterKind.Original,autoRename:true);
            Check("sourceNameSafelyNumbered",Hash(copy)==copyHash&&Path.GetFileName(protectedResult.Path)=="source (1).mp4");
            var range=new TrimSelection(.205,2.9);var audio=new AudioEdit(MuteOriginal:true,Added:music,AddedVolume:1,Delay:.6,FadeIn:.4,FadeOut:.7);
            var edit=new VideoEditOptions(new(17,19,320,240),Audio:audio);
            string full=await Export("full-mix",source,edit,range);
            string before=await Export("preview-delay",source,edit,range,new(.3,.6));var beforeAudio=await Samples(before);
            Check("previewRetainsClipRelativeDelay",Rms(beforeAudio,.02,.2)<.001&&Rms(beforeAudio,.45,.55)>.01);
            string middle=await Export("preview-middle",source,edit,range,new(.9,.8));var middleAudio=await Samples(middle);var fullAudio=await Samples(full);
            Check("previewMatchesFullMixLevel",Math.Abs(Rms(middleAudio,.1,.5)/Rms(fullAudio,1,1.4)-1)<.04);
            string ending=await Export("preview-fade-out",source,edit,range,new(1.8,.8));var endingAudio=await Samples(ending);
            Check("previewRetainsWholeClipFadeOut",Rms(endingAudio,.7,.77)<Rms(endingAudio,.05,.15)*.4);
            var shortMusic=await CrtRenderer.InspectAudioAsync(Path.Combine(fixtures,"short-music.wav"),token);
            string silent=await Export("preview-after-music",source,new(Audio:new(MuteOriginal:true,Added:shortMusic)),range,new(1.5,.6));
            Check("previewAfterSoundtrackCanBeSilent",(await CrtRenderer.InspectAsync(silent,token)).AudioCodecs.Length==0);
            var longSource=await CrtRenderer.InspectAsync(Path.Combine(fixtures,"cancel.mp4"),token);string capped=await Export("preview-capped",longSource,new(),part:new(12));
            Check("previewCappedAtTenSeconds",Math.Abs((await CrtRenderer.InspectAsync(capped,token)).Duration-10)<.06);
            foreach(var dims in new[]{(640,480),(1920,1080),(720,1280)}){
                var preset=CrtPresets.MatchPicture(dims.Item1,dims.Item2,true);preset.Validate();Check($"preset{dims.Item1}x{dims.Item2}Valid",preset.Enabled("curvature"));
            }
            Check("presetsUsePictureDensityAndAspect",CrtPresets.MatchPicture(640,480,false).Json()!=CrtPresets.MatchPicture(1920,1080,false).Json()&&CrtPresets.Aspect(640,480)=="4:3"&&CrtPresets.Aspect(1920,1080)=="16:9");
            Check("defaultSettingsUnchanged",settings.Json()==new CrtSettings().Json());
            foreach(var file in Directory.GetFiles(folder,"*.mp4")){
                if(file==existing||file==late)continue;
                var decode=await CrtRenderer.RunAsync(Recorder.Ffmpeg,new[]{"-v","error","-i",file,"-map","0","-fps_mode:v","passthrough","-enc_time_base:v","demux","-f","null","-"},null,token);Check(Path.GetFileName(file)+"FullDecode",decode.Code==0&&string.IsNullOrWhiteSpace(decode.Error));
            }
            Check("originalUnchanged",Hash(source.Path)==originalHash);Check("noTemporaryJobsRemain",!Directory.GetDirectories(folder,".crt-export-*").Any());report["success"]=true;
        }catch(Exception e){report["success"]=false;report["error"]=e.ToString();Environment.ExitCode=1;}
        await File.WriteAllTextAsync(Path.Combine(folder,"feedback-report.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
    }
}
