using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace GameplayRecorder;

public static class EditingTests
{
    public static async Task RunAsync(string[] args)
    {
        string Arg(string key)=>args[Array.IndexOf(args,key)+1];
        string output=Path.GetFullPath(Arg("--editing-tests")),fixtures=Path.GetFullPath(Arg("--fixtures"));Directory.CreateDirectory(output);
        var report=new Dictionary<string,object>();var token=CancellationToken.None;var settings=new CrtSettings();settings.Enable("curvature",true);
        void Check(string key,bool ok){if(!ok)throw new Exception(key);report[key]=true;}
        string Hash(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        var originals=Directory.GetFiles(fixtures).ToDictionary(p=>p,Hash);
        async Task<CrtSource> Source(string name)=>await CrtRenderer.InspectAsync(Path.Combine(fixtures,name),token);
        async Task<string> Export(string name,CrtSource source,VideoEditOptions edit,TrimSelection? trim=null,VideoFilterKind filter=VideoFilterKind.Original,string encoder="libx264"){
            string file=Path.Combine(output,name+".mp4");await CrtExporter.ExportAsync(source,file,settings,encoder,null,token,trim,filter,edit);
            var decoded=await CrtRenderer.RunAsync(Recorder.Ffmpeg,new[]{"-v","error","-i",file,"-map","0","-fps_mode:v","passthrough","-enc_time_base:v","demux","-f","null","-"},null,token);
            Check(name+"_fullDecode",decoded.Code==0&&string.IsNullOrWhiteSpace(decoded.Error));return file;
        }
        async Task<float[]> Samples(string file,int channels=1){
            using var p=new System.Diagnostics.Process{StartInfo=Recorder.Command(Recorder.Ffmpeg,new[]{"-v","error","-i",file,"-map","0:a:0","-ac",channels.ToString(CultureInfo.InvariantCulture),"-ar","48000","-f","f32le","-"})};p.Start();using var memory=new MemoryStream();var copy=p.StandardOutput.BaseStream.CopyToAsync(memory);var errors=p.StandardError.ReadToEndAsync();await p.WaitForExitAsync();await copy;if(p.ExitCode!=0)throw new IOException(await errors);await errors;byte[] bytes=memory.ToArray();float[] data=new float[bytes.Length/4];Buffer.BlockCopy(bytes,0,data,0,bytes.Length);return data;
        }
        double Rms(float[] data,double start,double end){var values=data.Skip((int)(start*48000)).Take((int)((end-start)*48000)).ToArray();return Math.Sqrt(values.Average(v=>(double)v*v));}
        double Tone(float[] data,double start,double end,int frequency){int offset=(int)(start*48000),count=Math.Min((int)((end-start)*48000),data.Length-offset);double x=0,y=0;for(int i=0;i<count;i++){double a=2*Math.PI*frequency*i/48000;x+=data[offset+i]*Math.Cos(a);y+=data[offset+i]*Math.Sin(a);}return Math.Sqrt(x*x+y*y)/count;}
        try{
            var normal=await Source("normal.mp4");var silent=await Source("silent.mp4");var added=await CrtRenderer.InspectAudioAsync(Path.Combine(fixtures,"music.wav"),token);
            var crop=new CropSelection(17,19,320,240);var cropEdit=new VideoEditOptions(crop);
            string cropped=await Export("crop-plain",normal,cropEdit,new(.205,2.41));var croppedInfo=await CrtRenderer.InspectAsync(cropped,token);Check("cropDimensionsExact",croppedInfo.Width==320&&croppedInfo.Height==240);
            string png=Path.Combine(output,"raw.png"),preview=Path.Combine(output,"crop-preview.png");await CrtRenderer.ExtractAsync(normal,.5,png,token);await CrtRenderer.RenderEditedAsync(png,preview,normal,settings,VideoFilterKind.Original,cropEdit,output,token);
            using(var raw=CrtRenderer.ReadBitmap(png))using(var image=CrtRenderer.ReadBitmap(preview)){
                bool same=image.Width==320&&image.Height==240;for(int y=0;y<240&&same;y++)for(int x=0;x<320;x++)if(image.GetPixel(x,y)!=raw.GetPixel(x+17,y+19)){same=false;break;}Check("cropPreviewExactPixels",same);
            }
            string half=await Export("half-volume",normal,new(Audio:new(OriginalVolume:.5)));var baseline=await Samples(normal.Path);var lowered=await Samples(half);double ratio=Rms(lowered,.2,2.5)/Rms(baseline,.2,2.5);report["halfVolumeRmsRatio"]=ratio;Check("halfVolumeAmplitude",Math.Abs(ratio-.5)<.03);
            string muted=await Export("muted",normal,new(Audio:new(MuteOriginal:true)));Check("muteRemovesSoundtrack",(await CrtRenderer.InspectAsync(muted,token)).AudioCodecs.Length==0);
            string zero=await Export("zero-volume",normal,new(Audio:new(OriginalVolume:0)));Check("zeroVolumeRemovesSoundtrack",(await CrtRenderer.InspectAsync(zero,token)).AudioCodecs.Length==0);
            string multi=await Export("multi-volume",await Source("mixed-audio.mkv"),new(Audio:new(OriginalVolume:.5)));Check("volumePreservesSeparateTracks",(await CrtRenderer.InspectAsync(multi,token)).AudioCodecs.Length==2);
            var overlay=new AudioEdit(Added:added,AddedVolume:.5,Delay:.4,FadeIn:.2,FadeOut:.3);
            string mix=await Export("trim-crop-crt-mix",normal,new(crop,ExportSize.Fit720,ExportQuality.Balanced,overlay),new(.515,2.41),VideoFilterKind.Crt);
            var mixed=await Samples(mix);Check("originalAndAddedTonesPresent",Tone(mixed,.8,1.2,440)>.015&&Tone(mixed,.8,1.2,660)>.005);
            Check("addedToneStartsAfterDelay",Tone(mixed,.05,.3,660)<.001);Check("mixIsOneStereoTrack",(await CrtRenderer.InspectAsync(mix,token)).AudioCodecs.Length==1);
            string only=await Export("added-to-silent",silent,new(Audio:overlay));var music=await Samples(only);Check("addedAudioDelayIsRelativeToOutput",Rms(music,.02,.3)<.001&&Rms(music,.8,1.2)>.01);
            Check("addedAudioFades",Rms(music,.42,.46)<Rms(music,.8,1.2)*.4&&Rms(music,1.94,1.98)<Rms(music,.8,1.2)*.3);
            string replace=await Export("replace-original",normal,new(Audio:overlay with{MuteOriginal=true}));var replaced=await Samples(replace);Check("mutedOriginalAbsentFromMix",Tone(replaced,.8,1.2,440)<.001&&Tone(replaced,.8,1.2,660)>.005);
            string selected=await Export("select-second-track",await Source("multiple-audio.mp4"),new(Audio:overlay with{OriginalTrack=1}));var selectedSamples=await Samples(selected);Check("mixUsesSelectedOriginalTrack",Tone(selectedSamples,.8,1.2,880)>.015&&Tone(selectedSamples,.8,1.2,440)<.001);
            string empty=await Export("empty-original-section-mix",await Source("short-audio.mp4"),new(Audio:overlay with{Delay=0,FadeIn=0,FadeOut=0}),new(1.505,2.41));Check("addedAudioSurvivesEmptyOriginalSection",(await CrtRenderer.InspectAsync(empty,token)).AudioCodecs.Length==1);
            string vfr=await Export("vfr-added",await Source("vfr-delayed.mp4"),new(crop,Audio:overlay),new(.515,2.41));var vfrSamples=await Samples(vfr);Check("vfrAudioMixTiming",Tone(vfrSamples,.8,1.2,660)>.005&&Tone(vfrSamples,.02,.3,660)<.001);
            var shortMusic=await CrtRenderer.InspectAudioAsync(Path.Combine(fixtures,"short-music.wav"),token);
            string shortMix=await Export("short-added",normal,new(Audio:new(Added:shortMusic)));Check("shortAddedDoesNotShortenVideo",Math.Abs((await CrtRenderer.InspectAsync(shortMix,token)).Duration-normal.Duration)<.06);
            foreach(var extension in new[]{"mp3","m4a"}){
                string encodedAudio=Path.Combine(output,"audio source "+extension+"."+extension);
                var encode=await CrtRenderer.RunAsync(Recorder.Ffmpeg,new[]{"-v","error","-n","-i",added.Path,"-t","1.2",encodedAudio},null,token);Check(extension+"FixtureEncoded",encode.Code==0);
                var audioSource=await CrtRenderer.InspectAudioAsync(encodedAudio,token);await Export(extension+"-added",silent,new(Audio:new(Added:audioSource,Delay:.2,FadeIn:.1,FadeOut:.1)));
            }
            string unicodeAudio=Path.Combine(output,"ملف صوت للتجربة.wav");File.Copy(added.Path,unicodeAudio);
            var unicodeSource=await CrtRenderer.InspectAudioAsync(unicodeAudio,token);await Export("فيديو مع صوت",silent,new(Audio:new(Added:unicodeSource)));
            foreach(var name in new[]{"landscape.mp4","portrait.mp4"})foreach(var preset in new[]{ExportSize.Fit1080,ExportSize.Fit720}){
                var source=await Source(name);var edit=new VideoEditOptions(Size:preset,Quality:ExportQuality.Smaller);var dims=edit.Dimensions(source.Width,source.Height,true);
                string file=await Export(Path.GetFileNameWithoutExtension(name)+preset,source,edit);var info=await CrtRenderer.InspectAsync(file,token);Check(name+preset+"_sizeAndNoUpscale",info.Width==dims.Width&&info.Height==dims.Height&&info.Width<=source.Width&&info.Height<=source.Height);
            }
            Check("portraitPresetAspect",new VideoEditOptions(Size:ExportSize.Fit720).Dimensions(800,1400)==(720,1260));
            Check("smallSourceNeverUpscaled",new VideoEditOptions(Size:ExportSize.Fit1080).Dimensions(640,400)==(640,400));
            var landscape=await Source("landscape.mp4");var resizedEdit=new VideoEditOptions(Size:ExportSize.Fit720);
            string resized=await Export("resized-crt",landscape,resizedEdit,filter:VideoFilterKind.Crt);
            string originalFrame=Path.Combine(output,"large-source.png"),resizedPreview=Path.Combine(output,"resized-preview.png"),resizedFrame=Path.Combine(output,"resized-frame.png");
            await CrtRenderer.ExtractAsync(landscape,0,originalFrame,token);await CrtRenderer.RenderEditedAsync(originalFrame,resizedPreview,landscape,settings,VideoFilterKind.Crt,resizedEdit,output,token);await CrtRenderer.ExtractAsync(await CrtRenderer.InspectAsync(resized,token),0,resizedFrame,token);
            using(var expected=CrtRenderer.ReadBitmap(resizedPreview))using(var actual=CrtRenderer.ReadBitmap(resizedFrame)){
                double difference=0;Check("resizedPreviewDimensionsMatchExport",expected.Size==actual.Size&&actual.Width==1152&&actual.Height==720);
                for(int y=0;y<actual.Height;y++)for(int x=0;x<actual.Width;x++){var a=actual.GetPixel(x,y);var b=expected.GetPixel(x,y);difference+=Math.Abs(a.R-b.R)+Math.Abs(a.G-b.G)+Math.Abs(a.B-b.B);}
                report["resizedCrtPreviewMeanDifference"]=difference/(actual.Width*actual.Height*3);Check("resizedCrtPreviewMatchesEncodedLook",difference/(actual.Width*actual.Height*3)<8);
            }
            var loud=await Source("loud.mp4");string limited=await Export("loud-limited",loud,new(Audio:new(OriginalVolume:2,Added:added,AddedVolume:2)));var peaks=await Samples(limited,2);report["limitedStereoPeak"]=peaks.Max(v=>Math.Abs(v));Check("mixLimiterKeepsAudioFinite",peaks.All(float.IsFinite)&&peaks.Max(v=>Math.Abs(v))<=1.03);
            string smaller=await Export("smaller-quality",normal,new(Quality:ExportQuality.Smaller));string high=await Export("high-quality",normal,new());Check("smallerPresetReducesFixtureSize",new FileInfo(smaller).Length<new FileInfo(high).Length);
            bool audioLocked=false;
            await CrtExporter.ExportAsync(normal,Path.Combine(output,"locked-inputs.mp4"),settings,"libx264",new DirectProgress<CrtExportProgress>(_=>{try{using var write=new FileStream(added.Path,FileMode.Open,FileAccess.Write,FileShare.ReadWrite);}catch(IOException){audioLocked=true;}}),token,null,VideoFilterKind.Original,new(Audio:new(Added:added)));
            Check("addedAudioLockedDuringExport",audioLocked);
            foreach(var invalid in new[]{new VideoEditOptions(new(-1,0,320,240)),new VideoEditOptions(new(0,0,321,241)),new VideoEditOptions(Audio:new(Added:added,Delay:4)),new VideoEditOptions(Audio:new(Added:added,FadeIn:2,FadeOut:2))}){
                try{await Export("invalid",normal,invalid);throw new Exception("Invalid edit was accepted");}catch(ArgumentException){report["invalidEditsRejected"]=true;}
            }
            string changed=Path.Combine(output,"changed.wav");File.Copy(added.Path,changed);var old=await CrtRenderer.InspectAudioAsync(changed,token);File.SetLastWriteTimeUtc(changed,DateTime.UtcNow.AddSeconds(-20));
            try{await Export("changed-audio",normal,new(Audio:new(Added:old)));throw new Exception("Changed audio accepted");}catch(IOException){report["changedAddedAudioRejected"]=true;}
            using(var cancellation=new CancellationTokenSource()){
                var longSource=await Source("cancel.mp4");string dest=Path.Combine(output,"cancelled.mp4");
                try{await CrtExporter.ExportAsync(longSource,dest,settings,"libx264",new DirectProgress<CrtExportProgress>(_=>cancellation.Cancel()),cancellation.Token,null,VideoFilterKind.Crt,new(crop,Audio:new(Added:added)));throw new Exception("Cancel accepted");}
                catch(OperationCanceledException){Check("combinedCancellationCleansUp",!File.Exists(dest)&&!Directory.GetDirectories(output,".crt-export-*").Any());}
            }
            if(args.Contains("--include-nvidia"))await Export("nvidia-combined",normal,new(crop,ExportSize.Fit720,ExportQuality.Smaller,overlay),new(.515,2.41),VideoFilterKind.Crt,"h264_nvenc");
            Check("inputsUnchanged",originals.All(p=>Hash(p.Key)==p.Value));Check("noTemporaryJobsRemain",!Directory.GetDirectories(output,".crt-export-*").Any());report["success"]=true;
        }catch(Exception e){report["success"]=false;report["error"]=e.ToString();Environment.ExitCode=1;}
        await File.WriteAllTextAsync(Path.Combine(output,"editing-report.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
    }
}
