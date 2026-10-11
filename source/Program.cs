using System.Diagnostics;
using System.Text.Json;

namespace GameplayRecorder;
internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if(args.Contains("--render-about")) {
            using var dialog=new AboutForm();
            dialog.Shown+=(_,_)=>{using var bitmap=new Bitmap(dialog.Width,dialog.Height);dialog.DrawToBitmap(bitmap,new Rectangle(0,0,dialog.Width,dialog.Height));bitmap.Save(args[Array.IndexOf(args,"--render-about")+1]);dialog.Close();};
            Application.Run(dialog);return;
        }
        if(args.Contains("--self-test")) { SelfTest.RunAsync(args).GetAwaiter().GetResult(); return; }
        if(args.Contains("--interruption-test")) {SelfTest.InterruptAsync(args[Array.IndexOf(args,"--interruption-test")+1]).GetAwaiter().GetResult();return;}
        if(args.Contains("--recover-test")) {
            string path=Path.GetFullPath(args[Array.IndexOf(args,"--recover-test")+1]);
            var session=Directory.GetDirectories(path,".recording-*").Single();
            using var json=JsonDocument.Parse(File.ReadAllText(Path.Combine(session,"session.json")));
            Recorder.FinalizeAsync(session,json.RootElement.GetProperty("audioStart").GetDouble(),Path.Combine(path,"recovered.mp4")).GetAwaiter().GetResult();return;
        }
        if(args.Contains("--test-scene")) {Application.Run(new TestScene(args));return;}
        if(args.Contains("--ui-self-test")) {
            using var form=new MainForm(testing:true);
            form.Shown+=async(_,_)=>await form.RunUiSelfTestAsync(args[Array.IndexOf(args,"--ui-self-test")+1]);
            Application.Run(form);return;
        }
        if(args.Contains("--render-ui")) {
            using var form=new MainForm(testing:true);using var capture=new System.Windows.Forms.Timer{Interval=1000};
            capture.Tick+=(_,_)=>{capture.Stop();using var bitmap=new Bitmap(form.Width,form.Height);using var g=Graphics.FromImage(bitmap);
                g.Dispose();form.DrawToBitmap(bitmap,new Rectangle(0,0,form.Width,form.Height));
                bitmap.Save(args[Array.IndexOf(args,"--render-ui")+1]);form.Close();};
            form.Shown+=(_,_)=>capture.Start();Application.Run(form);return;
        }
        Application.ThreadException+=(_,e)=>MessageBox.Show(e.Exception.Message,"Capture & Release",MessageBoxButtons.OK,MessageBoxIcon.Error);
        Application.Run(new MainForm());
    }
}
public record Preferences(string Folder="", int Fps=30, int Height=1080, int Crf=23, int Delay=8,
    string Hotkey="Ctrl + Shift + F9", bool Cursor=false, bool Compatibility=false, string Encoder="libx264");
public class MainForm : Form
{
    readonly ComboBox games=new(),monitors=new(),mode=new(),fps=new(),quality=new(),encoder=new(),hotkey=new();
    readonly NumericUpDown delay=new(){Minimum=0,Maximum=30,Value=8,Width=65};
    readonly CheckBox compatibility=new(){Text="Compatibility capture",AutoSize=true},cursor=new(){Text="Include pointer",AutoSize=true};
    readonly TextBox folder=new(){ReadOnly=true};
    readonly Button start=new(){Text="Start recording",AutoSize=true},previewButton=new(){Text="Check preview",AutoSize=true},refresh=new(){Text="Refresh applications",AutoSize=true},browse=new(){Text="Choose folder…",AutoSize=true};
    readonly Label status=new(){Dock=DockStyle.Fill,Text="Ready",ForeColor=Color.FromArgb(42,92,78),AutoEllipsis=true},timerText=new(){AutoSize=true,Text="00:00:00"};
    readonly Label instructions=new(){Dock=DockStyle.Fill,ForeColor=Color.FromArgb(76,86,99),Padding=new Padding(0,5,0,0)};
    readonly PictureBox preview=new(){Dock=DockStyle.Fill,SizeMode=PictureBoxSizeMode.Zoom,BackColor=Color.FromArgb(233,237,241)};
    readonly Label previewHint=new(){Text="Check a preview before your first recording.",Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleCenter,ForeColor=Color.DimGray};
    readonly NotifyIcon tray=new(){Icon=SystemIcons.Application,Text="Capture & Release",Visible=true};
    readonly System.Windows.Forms.Timer timer=new(){Interval=500};
    readonly List<Control> settings=new();
    readonly string preferencesPath=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CaptureAndRelease","settings.json");
    readonly string previousPreferencesPath=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"LocalScreenRecorder","settings.json");
    readonly string legacyPreferencesPath=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"GameplayRecorder","settings.json");
    Recorder? recorder;
    CancellationTokenSource? countdown;
    bool busy,closing,hotkeyRegistered;
    DateTime began;
    string? lastOutput;
    readonly bool testing;

    public MainForm(bool testing=false)
    {
        this.testing=testing;
        Text="Capture & Release · 0.3.0"; Font=new Font("Segoe UI",10); BackColor=Color.FromArgb(247,249,251);
        Icon=System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!)??SystemIcons.Application;tray.Icon=Icon;
        ClientSize=new Size(900,780); MinimumSize=new Size(780,740); StartPosition=FormStartPosition.CenterScreen;
        var root=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=9,Padding=new Padding(26)};
        root.RowStyles.Add(new(SizeType.Absolute,55));root.RowStyles.Add(new(SizeType.Absolute,40));root.RowStyles.Add(new(SizeType.Absolute,40));
        root.RowStyles.Add(new(SizeType.Absolute,78));root.RowStyles.Add(new(SizeType.Absolute,100));root.RowStyles.Add(new(SizeType.Absolute,55));
        root.RowStyles.Add(new(SizeType.Percent,100));root.RowStyles.Add(new(SizeType.Absolute,55));root.RowStyles.Add(new(SizeType.Absolute,70));Controls.Add(root);
        var heading=new Label{Text="Capture & Release",UseMnemonic=false,Font=new Font("Segoe UI",20,FontStyle.Bold),AutoSize=true};var titleRow=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,Margin=new Padding(0)};
        titleRow.ColumnStyles.Add(new(SizeType.Percent,100));titleRow.ColumnStyles.Add(new(SizeType.Absolute,85));
        var about=new Button{Text="About…",AutoSize=true,Anchor=AnchorStyles.Right};
        about.Click+=(_,_)=>{using var dialog=new AboutForm();dialog.ShowDialog(this);};
        titleRow.Controls.Add(heading,0,0);titleRow.Controls.Add(about,1,0);root.Controls.Add(titleRow,0,0);
        SetupCombo(games);SetupCombo(monitors);SetupCombo(mode);SetupCombo(fps);SetupCombo(quality);SetupCombo(encoder);SetupCombo(hotkey);
        root.Controls.Add(Row("Application audio",games,refresh),0,1);
        mode.Items.AddRange(new object[]{"Screen · entire monitor","Window · selected application"});mode.SelectedIndex=0;
        foreach(var screen in Screen.AllScreens) monitors.Items.Add(new MonitorTarget(screen.DeviceName,screen.Bounds));
        monitors.SelectedIndex=Math.Max(0,Array.FindIndex(Screen.AllScreens,s=>s.Primary));
        root.Controls.Add(Row("Picture source",mode,monitors),0,2);
        var choices=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=true,Padding=new Padding(0,8,0,0)};
        fps.Items.AddRange(new object[]{"30 fps","60 fps"});fps.SelectedIndex=0;fps.Width=90;
        quality.Items.AddRange(new object[]{"1080p · balanced","1080p · higher quality","720p · smaller files","Original size"});quality.SelectedIndex=0;quality.Width=185;
        encoder.Items.AddRange(new object[]{"CPU · reliable default","NVIDIA GPU"});encoder.SelectedIndex=0;encoder.Width=185;
        choices.Controls.AddRange(new Control[]{fps,quality,encoder,compatibility,cursor});root.Controls.Add(choices,0,3);
        root.Controls.Add(instructions,0,4);
        folder.Text=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),"Screen Recordings");
        root.Controls.Add(Row("Save to",folder,browse),0,5);
        var box=new Panel{Dock=DockStyle.Fill,Padding=new Padding(1),BackColor=Color.FromArgb(215,222,230)};
        box.Controls.Add(preview);preview.Controls.Add(previewHint);root.Controls.Add(box,0,6);
        var bottom=new FlowLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(0,14,0,0),WrapContents=false};
        start.BackColor=Color.FromArgb(34,107,87);start.ForeColor=Color.White;start.FlatStyle=FlatStyle.Flat;start.Padding=new Padding(12,3,12,3);
        bottom.Controls.AddRange(new Control[]{start,previewButton,timerText});root.Controls.Add(bottom,0,7);
        root.Controls.Add(status,0,8);
        // A separate compact settings row stays accessible without crowding the preview.
        var shortcuts=new FlowLayoutPanel{Dock=DockStyle.Bottom,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,MinimumSize=new Size(0,43),Padding=new Padding(26,0,26,8),WrapContents=true};
        hotkey.Width=170;foreach(var mod in new[]{"Ctrl + Shift + ","Ctrl + Alt + "}) foreach(var key in new[]{"F8","F9","F10","F11","F12"}) hotkey.Items.Add(mod+key);
        hotkey.SelectedItem="Ctrl + Shift + F9";
        shortcuts.Controls.AddRange(new Control[]{new Label{Text="Start / stop",AutoSize=true,Padding=new Padding(0,5,5,0)},hotkey,new Label{Text="Countdown (seconds)",AutoSize=true,Padding=new Padding(12,5,5,0)},delay});
        var open=new Button{Text="Open recordings",AutoSize=true};shortcuts.Controls.Add(open);
        var recover=new Button{Text="Recover recording…",AutoSize=true};shortcuts.Controls.Add(recover);settings.Add(recover);
        Controls.Add(shortcuts);shortcuts.SendToBack();
        settings.AddRange(new Control[]{games,refresh,mode,monitors,fps,quality,encoder,compatibility,cursor,folder,browse,previewButton,hotkey,delay});
        LoadPreferences(); RefreshGames();UpdateSourceHint();
        refresh.Click+=(_,_)=>{RefreshGames();status.Text=$"List refreshed: {games.Items.Count} windows. Select an application from the dropdown.";};
        browse.Click+=(_,_)=>{using var picker=new FolderBrowserDialog{SelectedPath=folder.Text};if(picker.ShowDialog(this)==DialogResult.OK){folder.Text=picker.SelectedPath;SavePreferences();}};
        open.Click+=(_,_)=>{Directory.CreateDirectory(folder.Text);Process.Start(new ProcessStartInfo("explorer.exe",folder.Text){UseShellExecute=true});};
        recover.Click+=async(_,_)=>await RecoverAsync();
        start.Click+=async(_,_)=>await ToggleAsync();
        previewButton.Click+=async(_,_)=>await PreviewAsync();
        hotkey.SelectedIndexChanged+=(_,_)=>{if(IsHandleCreated) RegisterShortcut();};
        mode.SelectedIndexChanged+=(_,_)=>{monitors.Enabled=mode.SelectedIndex==0;compatibility.Enabled=mode.SelectedIndex==0;UpdateSourceHint();ClearPreview();};
        games.SelectedIndexChanged+=(_,_)=>{UpdateSourceHint();ClearPreview();};
        monitors.SelectedIndexChanged+=(_,_)=>ClearPreview();compatibility.CheckedChanged+=(_,_)=>ClearPreview();
        timer.Tick+=(_,_)=>{
            if(recorder?.Active==true) {
                var elapsed=DateTime.Now-began;timerText.Text=elapsed.ToString(@"hh\:mm\:ss");tray.Text="Recording · "+timerText.Text;
                Text="● Recording "+timerText.Text+" · Capture & Release";
                if(elapsed.TotalHours>=5.5) _=StopAsync("Recording reached its file-size safety limit.");
            }
        };timer.Start();
        var menu=new ContextMenuStrip();menu.Items.Add("Show recorder",null,(_,_)=>{Show();WindowState=FormWindowState.Normal;Activate();});
        menu.Items.Add("Start / stop",null,async(_,_)=>await ToggleAsync());
        menu.Items.Add("Exit",null,(_,_)=>Close());tray.ContextMenuStrip=menu;
        tray.DoubleClick+=(_,_)=>{Show();WindowState=FormWindowState.Normal;Activate();};
        Shown+=(_,_)=>RegisterShortcut();FormClosing+=OnClosing;
    }
    static void SetupCombo(ComboBox c) {c.DropDownStyle=ComboBoxStyle.DropDownList;c.Anchor=AnchorStyles.Left|AnchorStyles.Right;c.Width=220;}
    static Control Row(string label,Control middle,Control right)
    {
        var row=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=3,Margin=new Padding(0)};
        row.ColumnStyles.Add(new(SizeType.Absolute,145));row.ColumnStyles.Add(new(SizeType.Percent,100));row.ColumnStyles.Add(new(SizeType.Absolute,right is ComboBox?250:175));
        row.Controls.Add(new Label{Text=label,AutoSize=true,Anchor=AnchorStyles.Left},0,0);middle.Dock=DockStyle.Fill;row.Controls.Add(middle,1,0);right.Anchor=AnchorStyles.Left|AnchorStyles.Right;row.Controls.Add(right,2,0);return row;
    }
    void RefreshGames()
    {
        var previous=games.SelectedItem as GameWindow;games.Items.Clear();games.Items.AddRange(Native.Windows().ToArray());
        for(int i=0;i<games.Items.Count;i++) if(previous is not null && games.Items[i] is GameWindow w && w.Handle==previous.Handle && w.Pid==previous.Pid){games.SelectedIndex=i;return;}
        // Require explicit selection rather than capturing an arbitrary application's audio.
        games.SelectedIndex=-1;
    }
    void UpdateSourceHint()
    {
        instructions.Text=mode.SelectedIndex==0
            ? "Picture: everything visible on the selected monitor, including other windows and notifications.\nAudio: only the selected application and related processes. No microphone.\nEnter fullscreen before recording starts. Switching away may interrupt capture; the captured part is saved."
            : "Picture: only the application window selected above. Keep that window restored.\nAudio: that application and related processes; browser audio can include other tabs. No microphone.\nSome apps, including Chrome, may record black. Use Screen capture with the browser fullscreen instead.";
    }
    void ClearPreview()
    {
        var old=preview.Image;preview.Image=null;old?.Dispose();previewHint.Visible=true;
    }
    CaptureOptions Options()
    {
        if(games.SelectedItem is not GameWindow game) throw new InvalidOperationException("Open your application, click Refresh applications, then select its window from the dropdown.");
        if(monitors.SelectedItem is not MonitorTarget monitor) throw new InvalidOperationException("Select a monitor.");
        string[] codecs={"libx264","h264_nvenc"};
        return new(game,monitor,mode.SelectedIndex==1,compatibility.Checked,fps.SelectedIndex==1?60:30,
            quality.SelectedIndex==2?720:quality.SelectedIndex==3?16384:1080,quality.SelectedIndex==1?19:23,
            codecs[Math.Max(0,encoder.SelectedIndex)],cursor.Checked,folder.Text);
    }
    void SetSettings(bool enabled)
    {
        foreach(var c in settings)c.Enabled=enabled;
        if(enabled){monitors.Enabled=mode.SelectedIndex==0;compatibility.Enabled=mode.SelectedIndex==0;}
    }
    void RegisterShortcut()
    {
        if(hotkeyRegistered)Native.UnregisterHotKey(Handle,1);
        string text=hotkey.Text;var key=(Keys)Enum.Parse(typeof(Keys),text.Split(' ').Last());
        hotkeyRegistered=Native.RegisterHotKey(Handle,1,(text.Contains("Shift")?0x6u:0x3u)|0x4000u,(uint)key);
        if(!hotkeyRegistered){status.Text="Shortcut unavailable — choose another";status.ForeColor=Color.Firebrick;}
        else {status.Text="Ready · "+text;status.ForeColor=Color.FromArgb(42,92,78);}
        SavePreferences();
    }
    protected override void WndProc(ref Message m)
    {
        if(m.Msg==0x0312 && m.WParam==1){_=ToggleAsync();return;}base.WndProc(ref m);
    }
    async Task ToggleAsync()
    {
        if(countdown!=null){countdown.Cancel();return;}
        if(busy)return;
        if(recorder?.Active==true){await StopAsync();return;}
        CaptureOptions o;
        try{o=Options();}catch(Exception e){ShowError(e.Message);return;}
        busy=true;SetSettings(false);SavePreferences();countdown=new();start.Text="Cancel countdown";
        try {
            for(int left=(int)delay.Value;left>0;left--){status.Text=$"Recording in {left}s — return to your application";await Task.Delay(1000,countdown.Token);}
            countdown.Dispose();countdown=null;start.Enabled=false;status.Text="Starting capture…";
            recorder?.Dispose();recorder=new();
            recorder.Failed+=message=>{if(!IsDisposed && IsHandleCreated)BeginInvoke(async()=>await StopAsync(message));};
            await recorder.StartAsync(o);
            began=DateTime.Now;status.ForeColor=Color.FromArgb(42,92,78);status.Text=$"Recording · {(o.WindowMode?"selected window":"entire monitor")} · {o.Game.ProcessName} audio only";start.Text="Stop & save";start.Enabled=true;
            // No Activate, Focus, Restore, game keystrokes, or foreground-window calls here.
        } catch(OperationCanceledException){status.Text="Countdown cancelled";ResetIdle();}
        catch(Exception e){ShowError(e.Message+RecoveryMessage());recorder?.Dispose();recorder=null;ResetIdle();}
        finally{countdown?.Dispose();countdown=null;busy=false;}
    }
    string RecoveryMessage()=>recorder?.RecoveryPath is string p ? "\n\nSession files: "+p : "";
    async Task StopAsync(string? reason=null)
    {
        if(busy || recorder==null)return;busy=true;start.Enabled=false;status.Text="Saving MP4…";
        try{lastOutput=await recorder.StopAsync(reason);status.Text=CaptureMessages.Saved(lastOutput,reason);status.ForeColor=reason==null?Color.FromArgb(42,92,78):Color.FromArgb(146,87,0);}
        catch(Exception e){ShowError(e.Message+RecoveryMessage());status.Text="Save failed · session files kept";}
        finally{recorder.Dispose();recorder=null;busy=false;ResetIdle(preserveStatus:true);}
    }
    void ResetIdle(bool preserveStatus=false)
    {
        SetSettings(true);start.Enabled=true;start.Text="Start recording";Text="Capture & Release · 0.3.0";tray.Text="Capture & Release";
        if(!preserveStatus && status.Text.StartsWith("Starting")) status.Text="Ready";
    }
    async Task PreviewAsync()
    {
        if(busy)return;busy=true;SetSettings(false);start.Enabled=false;
        string temp=Path.Combine(Path.GetTempPath(),"LocalScreenRecorder-preview-"+Guid.NewGuid().ToString("N")+".png");
        try {
            status.Text="Checking capture…";await Recorder.PreviewAsync(Options(),temp);
            using var image=Image.FromFile(temp);var old=preview.Image;preview.Image=new Bitmap(image);old?.Dispose();previewHint.Visible=false;
            status.Text="Preview ready — check the picture";
        }catch(Exception e){ShowError(e.Message);status.Text="Preview failed";}
        finally{if(File.Exists(temp))File.Delete(temp);busy=false;SetSettings(true);start.Enabled=true;}
    }
    async Task RecoverAsync()
    {
        if(busy || recorder!=null)return;
        using var picker=new OpenFileDialog{Title="Open session.json in an interrupted .recording folder",Filter="Recording session (session.json)|session.json",InitialDirectory=folder.Text};
        if(picker.ShowDialog(this)!=DialogResult.OK)return;
        busy=true;SetSettings(false);start.Enabled=false;status.Text="Recovering recording…";
        try {
            using var data=JsonDocument.Parse(File.ReadAllText(picker.FileName));
            var sessionFolder=Path.GetDirectoryName(picker.FileName)!;
            var audioTime=data.RootElement.GetProperty("audioStart").GetDouble();
            string output=Path.Combine(Path.GetDirectoryName(sessionFolder)!,"Recovered-"+DateTime.Now.ToString("yyyy-MM-dd-HHmmss")+"-"+Guid.NewGuid().ToString("N")[..4]+".mp4");
            lastOutput=await Recorder.FinalizeAsync(sessionFolder,audioTime,output);
            status.Text="Recovered · "+Path.GetFileName(output);
        }catch(Exception e){ShowError(e.Message);status.Text="Recovery failed · original files kept";}
        finally{busy=false;SetSettings(true);start.Enabled=true;}
    }
    void ShowError(string message)
    {
        // Capture failures must not pull a fullscreen game out of focus.
        if(Native.GetForegroundWindow()==Handle)MessageBox.Show(this,message,"Capture & Release",MessageBoxButtons.OK,MessageBoxIcon.Information);
        else {status.Text=message.Split('\n')[0];tray.Text="Capture & Release · needs attention";}
    }
    async void OnClosing(object? sender,FormClosingEventArgs e)
    {
        if(closing)return;
        if(busy && countdown==null){e.Cancel=true;status.Text="Please wait for the current operation to finish";return;}
        if(countdown!=null){e.Cancel=true;countdown.Cancel();return;}
        if(recorder!=null){e.Cancel=true;await StopAsync();closing=true;Close();return;}
        closing=true;SavePreferences();timer.Stop();tray.Dispose();preview.Image?.Dispose();if(hotkeyRegistered)Native.UnregisterHotKey(Handle,1);
    }
    void LoadPreferences()
    {
        try{string path=File.Exists(preferencesPath)?preferencesPath:File.Exists(previousPreferencesPath)?previousPreferencesPath:legacyPreferencesPath;if(!File.Exists(path))return;
            var p=JsonSerializer.Deserialize<Preferences>(File.ReadAllText(path));if(p==null)return;
            if(!string.IsNullOrWhiteSpace(p.Folder))folder.Text=p.Folder;fps.SelectedIndex=p.Fps==60?1:0;
            quality.SelectedIndex=p.Height==720?2:p.Height>1080?3:p.Crf==19?1:0;delay.Value=Math.Clamp(p.Delay,0,30);
            if(hotkey.Items.Contains(p.Hotkey))hotkey.SelectedItem=p.Hotkey;cursor.Checked=p.Cursor;compatibility.Checked=p.Compatibility;
            encoder.SelectedIndex=Math.Max(0,Array.IndexOf(new[]{"libx264","h264_nvenc"},p.Encoder));
        }catch{}
    }
    void SavePreferences()
    {
        if(testing)return;
        try{Directory.CreateDirectory(Path.GetDirectoryName(preferencesPath)!);string[] codecs={"libx264","h264_nvenc"};
            var p=new Preferences(folder.Text,fps.SelectedIndex==1?60:30,quality.SelectedIndex==2?720:quality.SelectedIndex==3?16384:1080,
                quality.SelectedIndex==1?19:23,(int)delay.Value,hotkey.Text,cursor.Checked,compatibility.Checked,codecs[Math.Max(0,encoder.SelectedIndex)]);
            File.WriteAllText(preferencesPath,JsonSerializer.Serialize(p,new JsonSerializerOptions{WriteIndented=true}));
        }catch{}
    }
    internal async Task RunUiSelfTestAsync(string path)
    {
        string testFolder=Path.GetFullPath(path);Directory.CreateDirectory(testFolder);
        var report=new Dictionary<string,object>();Process? scene=null;
        async Task Until(Func<bool> condition,int seconds=25) {
            var timeout=DateTime.UtcNow.AddSeconds(seconds);
            while(!condition()){if(DateTime.UtcNow>timeout)throw new TimeoutException("UI check timed out: "+status.Text);await Task.Delay(50);}
        }
        try {
            await Task.Delay(300);
            var layoutSizes=new[]{new Size(900,780),new Size(780,740)};
            foreach(var size in layoutSizes) {
                ClientSize=size;PerformLayout();Application.DoEvents();
                foreach(var c in settings.Append(start)) {
                    if(c.Parent==null)continue;
                    var bounds=RectangleToClient(c.RectangleToScreen(c.ClientRectangle));
                    if(bounds.Right>ClientSize.Width || bounds.Bottom>ClientSize.Height || bounds.Left<0 || bounds.Top<0)
                        throw new Exception("A control is outside the window: "+c.Text);
                }
            }
            ClientSize=new Size(900,780);report["layoutAtTwoSizes"]=true;
            using(var aboutDialog=new AboutForm()) {
                if(!AboutForm.Credits.Contains("Ibrahim Samir") || !AboutForm.Credits.Contains("GPL-3.0") || !AboutForm.Credits.Contains("FFmpeg"))throw new Exception("About credits are incomplete");
                report["aboutCredits"]=true;
            }
            mode.SelectedIndex=1;UpdateSourceHint();
            if(!instructions.Text.Contains("Chrome") || monitors.Enabled)throw new Exception("Window source guidance is missing");
            mode.SelectedIndex=0;UpdateSourceHint();
            if(!instructions.Text.Contains("everything visible") || !monitors.Enabled)throw new Exception("Screen source guidance is missing");
            report["screenWindowGuidance"]=true;
            // Another recorder may still be open. Test with a free shortcut without
            // changing either app's saved preferences or sending real keyboard input.
            if(!hotkeyRegistered) foreach(var candidate in hotkey.Items.Cast<string>()) {
                hotkey.SelectedItem=candidate;if(hotkeyRegistered)break;
            }
            if(!hotkeyRegistered)throw new Exception("No test shortcut could be registered");
            scene=Process.Start(new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false,ArgumentList={"--test-scene","--tone=440"}})!;
            await Task.Delay(1500);RefreshGames();
            games.SelectedItem=games.Items.Cast<GameWindow>().First(w=>w.Pid==scene.Id);
            var selectedHandle=((GameWindow)games.SelectedItem!).Handle;RefreshGames();
            if((games.SelectedItem as GameWindow)?.Handle!=selectedHandle)throw new Exception("Refresh changed the selected window");
            encoder.SelectedIndex=0;fps.SelectedIndex=0;quality.SelectedIndex=0;compatibility.Checked=false;
            folder.Text=testFolder;var game=(GameWindow)games.SelectedItem!;
            await PreviewAsync();if(preview.Image==null)throw new Exception("UI preview did not load");
            report["previewLoaded"]=true;
            delay.Value=3;
            Native.PostMessage(Handle,0x0312,1,0);await Until(()=>countdown!=null);
            await Task.Delay(200);Native.PostMessage(Handle,0x0312,1,0);await Until(()=>!busy && countdown==null);
            if(Directory.EnumerateDirectories(testFolder,".recording-*").Any())throw new Exception("Cancelled countdown started a capture");
            report["countdownCancellation"]=true;
            Native.GetWindowRect(game.Handle,out var before);var foreground=Native.GetForegroundWindow();
            delay.Value=0;Native.PostMessage(Handle,0x0312,1,0);await Until(()=>recorder?.Active==true);
            if(start.Text!="Stop & save" || games.Enabled)throw new Exception("Incorrect recording controls");
            await Task.Delay(2500);Native.PostMessage(Handle,0x0312,1,0);await Until(()=>!busy && recorder==null);
            if(lastOutput==null || !File.Exists(lastOutput))throw new Exception("Shortcut stop did not save a video");
            Native.GetWindowRect(game.Handle,out var after);
            if(!before.Equals(after)||foreground!=Native.GetForegroundWindow())throw new Exception("UI capture changed target bounds or focus");
            if(!start.Enabled || !games.Enabled)throw new Exception("Controls were not restored after saving");
            report["shortcutRegistered"]=hotkeyRegistered;report["hotkeyMessageStartStop"]=true;
            report["windowBoundsAndFocusUnchanged"]=true;report["output"]=lastOutput;
            if(!status.Text.StartsWith("Saved:") || status.Text.Contains("interrupted"))throw new Exception("Normal stop was labelled as an interruption");
            // Terminate only this test's own capture subprocess to exercise automatic saving
            // and the observed DXGI-loss message. This is not a real fullscreen transition.
            Native.PostMessage(Handle,0x0312,1,0);await Until(()=>recorder?.Active==true);
            await Task.Delay(2000);recorder!.InterruptVideoForTest();
            await Until(()=>!busy && recorder==null);
            if(!status.Text.Contains("fullscreen or display change") || !status.Text.Contains("Saved captured part:") || !File.Exists(lastOutput))
                throw new Exception("Interrupted capture did not save with a clear reason: "+status.Text);
            report["simulatedCaptureLossSaved"]=true;report["interruptionMessage"]=status.Text;report["success"]=true;
        }catch(Exception e){report["success"]=false;report["error"]=e.ToString();Environment.ExitCode=1;}
        finally {
            if(recorder!=null)await StopAsync();try{if(scene is {HasExited:false})scene.Kill(true);}catch{}scene?.Dispose();
            await File.WriteAllTextAsync(Path.Combine(testFolder,"ui-report.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
            Close();
        }
    }
}
