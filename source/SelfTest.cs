using System.Diagnostics;
using System.Media;
using System.Text.Json;

namespace GameplayRecorder;

// Controlled test source: animated, borderless window and a known output tone.
// A second process emits a different tone to check application-audio isolation.
internal sealed class TestScene : Form
{
    readonly System.Windows.Forms.Timer timer=new(){Interval=33};
    readonly SoundPlayer player;
    readonly MemoryStream wave;
    readonly bool background;
    int frame;
    public TestScene(string[] args)
    {
        int frequency=int.Parse(args.FirstOrDefault(a=>a.StartsWith("--tone="))?[7..]??"440");
        bool hidden=args.Contains("--background-tone");
        background=hidden;
        int owner=int.Parse(args.FirstOrDefault(a=>a.StartsWith("--owner="))?[8..]??"0");
        Text="Gameplay Recorder test scene "+frequency;
        BackColor=Color.FromArgb(14,31,48);FormBorderStyle=FormBorderStyle.None;StartPosition=FormStartPosition.Manual;
        Bounds=Screen.PrimaryScreen!.Bounds;DoubleBuffered=true;
        if(hidden){Opacity=0;ShowInTaskbar=false;Bounds=new Rectangle(-20000,-20000,64,64);}
        wave=new MemoryStream();using(var w=new BinaryWriter(wave,System.Text.Encoding.UTF8,true)) {
            const int count=48000*2;w.Write("RIFF"u8.ToArray());w.Write(36+count*4);w.Write("WAVEfmt "u8.ToArray());w.Write(16);
            w.Write((short)1);w.Write((short)2);w.Write(48000);w.Write(192000);w.Write((short)4);w.Write((short)16);w.Write("data"u8.ToArray());w.Write(count*4);
            for(int i=0;i<count;i++){short v=(short)(5000*Math.Sin(2*Math.PI*frequency*i/48000));w.Write(v);w.Write(v);}
        }
        wave.Position=0;player=new SoundPlayer(wave);
        Shown+=(_,_)=>{player.PlayLooping();timer.Start();if(!hidden)Activate();};
        timer.Tick+=(_,_)=>{frame++;Invalidate();if(frame>3000)Close();
            if(owner>0 && frame%15==0){try{using var p=Process.GetProcessById(owner);if(p.HasExited)Close();}catch(ArgumentException){Close();}}};
        Paint+=(_,e)=>{
            using var title=new Font("Segoe UI",30,FontStyle.Bold);using var text=new Font("Segoe UI",16);
            e.Graphics.DrawString("GAMEPLAY RECORDER · CAPTURE TEST",title,Brushes.White,50,70);
            e.Graphics.DrawString($"Animated picture + {frequency} Hz application audio\nFrame {frame}\nThis is a controlled test, not a game.",text,Brushes.LightSteelBlue,55,145);
            int x=50+(frame*9)%Math.Max(100,ClientSize.Width-220);e.Graphics.FillEllipse(Brushes.MediumAquamarine,x,ClientSize.Height/2,120,120);
        };
        FormClosed+=(_,_)=>{timer.Dispose();player.Stop();player.Dispose();wave.Dispose();};
    }
    protected override bool ShowWithoutActivation=>background;
    protected override CreateParams CreateParams {
        get {var p=base.CreateParams;if(background)p.ExStyle|=0x08000000;return p;}
    }
}
internal static class SelfTest
{
    internal static async Task InterruptAsync(string path)
    {
        string folder=Path.GetFullPath(path);Directory.CreateDirectory(folder);
        using var scene=Process.Start(new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false,ArgumentList={"--test-scene","--tone=440","--owner="+Environment.ProcessId}})!;
        await Task.Delay(1500);var game=Native.Windows().First(w=>w.Pid==scene.Id);
        var screen=Screen.FromHandle(game.Handle);
        var recorder=new Recorder();
        await recorder.StartAsync(new(game,new MonitorTarget(screen.DeviceName,screen.Bounds),false,false,30,720,23,"libx264",false,folder));
        await Task.Delay(3000);
        // Deliberately skip Stop/Dispose to exercise OS cleanup and recovery.
        Environment.Exit(73);
    }
    internal static async Task RunAsync(string[] args)
    {
        string folder=Path.GetFullPath(args.ElementAtOrDefault(Array.IndexOf(args,"--self-test")+1)??"self-test");Directory.CreateDirectory(folder);
        var report=new Dictionary<string,object>();Process? scene=null,other=null;
        try {
            string exe=Environment.ProcessPath!;
            scene=Process.Start(new ProcessStartInfo(exe){UseShellExecute=false,ArgumentList={"--test-scene","--tone=440"}})!;
            other=Process.Start(new ProcessStartInfo(exe){UseShellExecute=false,ArgumentList={"--test-scene","--tone=880","--background-tone"}})!;
            GameWindow? game=null;
            for(int i=0;i<60 && game==null;i++){await Task.Delay(100);game=Native.Windows().FirstOrDefault(w=>w.Pid==scene.Id);}
            if(game==null)throw new Exception("Test window did not appear");
            await Task.Delay(1500);
            Native.GetWindowRect(game.Handle,out var before);var focusBefore=Native.GetForegroundWindow();
            var screen=Screen.FromHandle(game.Handle);var monitor=new MonitorTarget(screen.DeviceName,screen.Bounds);
            foreach(bool compatibility in new[]{false,true}) {
                using var recorder=new Recorder();string? captureError=null;recorder.Failed+=m=>captureError=m;
                await recorder.StartAsync(new(game,monitor,false,compatibility,30,1080,23,"libx264",false,folder));
                await Task.Delay(4000);
                Native.GetWindowRect(game.Handle,out var during);
                string output=await recorder.StopAsync();
                Native.GetWindowRect(game.Handle,out var after);
                if(captureError!=null)throw new Exception(captureError);
                bool unchanged=before.Equals(during)&&before.Equals(after);
                bool focusKept=focusBefore==Native.GetForegroundWindow();
                if(!unchanged || !focusKept)throw new Exception($"Test window geometry unchanged: {unchanged}; foreground before: {focusBefore}; after: {Native.GetForegroundWindow()}; target: {game.Handle}");
                report[compatibility?"compatibility":"desktop_duplication"]=new {output,windowBoundsUnchanged=unchanged,foregroundUnchanged=focusKept};
            }
            // Window mode exercises the alternate input path without changing target geometry.
            using(var recorder=new Recorder()) {
                await recorder.StartAsync(new(game,monitor,true,false,30,720,23,"libx264",false,folder));await Task.Delay(2500);
                report["window_capture"]=await recorder.StopAsync();
            }
            if(args.Contains("--include-nvidia")) {
                using var recorder=new Recorder();
                await recorder.StartAsync(new(game,monitor,false,false,30,1080,23,"h264_nvenc",false,folder));await Task.Delay(2500);
                report["nvidia_screen_capture"]=await recorder.StopAsync();
            }
            report["success"]=true;
        }catch(Exception e){report["success"]=false;report["error"]=e.ToString();Environment.ExitCode=1;}
        finally{
            foreach(var p in new[]{scene,other}){try{if(p is {HasExited:false})p.Kill(true);}catch{}p?.Dispose();}
            await File.WriteAllTextAsync(Path.Combine(folder,"report.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
        }
    }
}
