using System.Text.Json;
namespace GameplayRecorder;

public static class SetupTests
{
    public static async Task RunAsync(string output)
    {
        Directory.CreateDirectory(output);var report=new Dictionary<string,object>();
        try{
            _=new CrtSettings();report["shaderResourcesAvailable"]=true;
            if(File.Exists(Recorder.Ffprobe)||File.Exists(Recorder.Ffmpeg))throw new Exception("Run this check before dependency setup.");
            try{await CrtRenderer.RunAsync(Recorder.Ffprobe,new[]{"-version"},null,CancellationToken.None);throw new Exception("Missing tool was not detected");}
            catch(FileNotFoundException e){if(!e.Message.Contains("Setup Recording Tools.cmd"))throw;report["missingToolsHaveSetupGuidance"]=true;}
            report["runtimeMajor"]=Environment.Version.Major;report["success"]=Environment.Version.Major==10;
        }catch(Exception e){report["success"]=false;report["error"]=e.ToString();Environment.ExitCode=1;}
        await File.WriteAllTextAsync(Path.Combine(output,"setup-report.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
    }
}
