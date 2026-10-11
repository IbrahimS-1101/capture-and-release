using System.Diagnostics;

namespace GameplayRecorder;
internal static class DisplayIndex
{
    internal static int Find(string device)
    {
        using var p=new Process{StartInfo=Recorder.Command(Path.Combine(Recorder.Tools,"ProcessAudio.exe"),new[]{"--displays"})};
        p.Start(); var read=p.StandardOutput.ReadToEndAsync();
        if(!p.WaitForExit(5000)) {p.Kill(true);throw new IOException("Display detection timed out. Try Compatibility capture.");}
        foreach(var line in read.GetAwaiter().GetResult().Split('\n')) {
            var pair=line.Trim().Split('\t');
            if(pair.Length==2 && pair[1].Equals(device,StringComparison.OrdinalIgnoreCase)) return int.Parse(pair[0]);
        }
        throw new IOException("This monitor is attached to another graphics adapter. Enable Compatibility capture for this monitor.");
    }
}
