using System.Security.Cryptography;
using System.Text.Json;

namespace GameplayRecorder;

public sealed partial class CrtForm
{
    internal async Task RunAudioListenSelfTestAsync(string[] args)
    {
        string Arg(string key)=>args[Array.IndexOf(args,key)+1];
        string output=Path.GetFullPath(Arg("--listen-self-test")),fixtures=Path.GetFullPath(Arg("--audio-folder"));Directory.CreateDirectory(output);
        var report=new Dictionary<string,object>();
        void Check(string name,bool ok){if(!ok)throw new Exception(name+": "+status.Text);report[name]=true;}
        string Hash(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        var originals=Directory.GetFiles(fixtures).ToDictionary(p=>p,Hash);
        try{
            Check("listenDisabledWithoutFile",!listenAudio.Enabled);
            Check("sourceOpened",await OpenAsync(Arg("--video")));toolTabs.SelectedTab=audioPage;
            foreach(var name in new[]{"1.mp3","2.wav","3.m4a","4-long.wav"}){
                Check(name+"Selected",await OpenAddedAudioAsync(Path.Combine(fixtures,name)));Check(name+"ListenEnabled",listenAudio.Enabled);
                muteOriginal.Checked=true;addedVolume.Value=0;audioDelay.Value=.3m;fadeIn.Value=.1m;fadeOut.Value=.1m;
                var edits=ReadEditOptions();var frame=processed;bool pending=dirty;
                Check(name+"StandalonePlayback",await ListenToAddedAsync(testPlayback:true,testCapture:name=="1.mp3"?Path.Combine(output,"listen-player.png"):null));
                Check(name+"EditsUntouched",ReadEditOptions()==edits&&ReferenceEquals(frame,processed)&&dirty==pending);
            }
            // Identification is independent of an invalid export range or muted mix.
            trimStart.Value=1;trimEnd.Value=.5m;var invalidEdits=ReadEditOptions();
            Check("listenIndependentOfInvalidTrim",await ListenToAddedAsync(testPlayback:true)&&ReadEditOptions()==invalidEdits);ResetRange();
            Size=MinimumSize;PerformLayout();Application.DoEvents();
            var row=listenAudio.Parent!;var rect=row.RectangleToClient(listenAudio.RectangleToScreen(listenAudio.ClientRectangle));Check("listenButtonFitsAudioRow",rect.Left>=0&&rect.Top>=0&&rect.Right<=row.ClientSize.Width&&rect.Bottom<=row.ClientSize.Height);
            ClientSize=new Size(1220,850);PerformLayout();Application.DoEvents();
            using(var screenshot=new Bitmap(Width,Height)){DrawToBitmap(screenshot,new Rectangle(0,0,Width,Height));screenshot.Save(Path.Combine(output,"audio-tab.png"));}
            string changed=Path.Combine(output,"changed.mp3");File.Copy(Path.Combine(fixtures,"1.mp3"),changed);await OpenAddedAudioAsync(changed);File.SetLastWriteTimeUtc(changed,DateTime.UtcNow.AddSeconds(-20));Check("changedAudioRejected",!await ListenToAddedAsync(testPlayback:true));
            File.Delete(changed);Check("missingAudioRejected",!await ListenToAddedAsync(testPlayback:true));removeAudio.PerformClick();Check("removeDisablesListen",addedAudio==null&&!listenAudio.Enabled);
            filterChoice.SelectedIndex=1;toolTabs.SelectedTab=filtersPage;
            var dos=controls.Controls.Cast<Control>().SelectMany(c=>c is FlowLayoutPanel panel?panel.Controls.Cast<Control>():new[]{c}).OfType<Button>().Single(b=>b.Text=="DOS 320×200");dos.PerformClick();
            Check("dosLabelAndSettings",settings.Number(CrtSettings.Fields.Single(f=>f.Key=="sourceSize"&&f.Index==0))==320&&settings.Number(CrtSettings.Fields.Single(f=>f.Key=="sourceSize"&&f.Index==1))==200);
            using(var screenshot=new Bitmap(Width,Height)){DrawToBitmap(screenshot,new Rectangle(0,0,Width,Height));screenshot.Save(Path.Combine(output,"filter-tab.png"));}
            // The existing video sample player must still use its original layout/mode.
            using(var player=new SamplePlayer(Arg("--video"),silentTest:true)){
                player.Show(this);Check("videoPlayerStillOpens",await player.Ready.WaitAsync(TimeSpan.FromSeconds(12)));await Task.Delay(220);Check("videoPlayerModeUnchanged",!player.IsAudioOnly&&player.HasPicture&&player.Position>0);player.Close();
            }
            Check("filesUnchanged",originals.All(p=>Hash(p.Key)==p.Value));report["success"]=true;
        }catch(Exception e){report["success"]=false;report["error"]=e.ToString();Environment.ExitCode=1;}
        finally{await File.WriteAllTextAsync(Path.Combine(output,"listen-report.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));Close();}
    }
}
