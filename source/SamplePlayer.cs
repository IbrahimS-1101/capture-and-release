namespace GameplayRecorder;

// Uses the Windows desktop runtime already required by this app; no browser/player download.
public sealed class SamplePlayer : Form
{
    readonly System.Windows.Controls.MediaElement media=new(){LoadedBehavior=System.Windows.Controls.MediaState.Manual,UnloadedBehavior=System.Windows.Controls.MediaState.Manual,Stretch=System.Windows.Media.Stretch.Uniform};
    readonly System.Windows.Forms.Integration.ElementHost host=new(){Dock=DockStyle.Fill};
    readonly Button play=new(){Text="Pause",AutoSize=true,Enabled=false},stop=new(){Text="Restart",AutoSize=true,Enabled=false};
    readonly TrackBar seek=new(){Dock=DockStyle.Fill,Minimum=0,Maximum=1000,TickStyle=TickStyle.None,Enabled=false};
    readonly Label clock=new(){AutoSize=true,Text="Opening sample…",Padding=new Padding(8,8,0,0)};
    readonly System.Windows.Forms.Timer timer=new(){Interval=100};
    readonly TaskCompletionSource<bool> ready=new();
    readonly string path;
    readonly double? audioDuration;
    bool playing,seeking,ended,disposed;
    public Task<bool> Ready=>ready.Task;
    public bool HasPicture=>media.NaturalVideoWidth>0&&media.NaturalVideoHeight>0;
    public bool HasAudio=>media.HasAudio;
    public double Position=>media.Position.TotalSeconds;
    public double DurationSeconds=>Duration;
    public bool IsPlaying=>playing;
    public bool IsAudioOnly {get;}
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public string? Failure {get;private set;}
    public SamplePlayer(string file,bool silentTest=false,bool audioOnly=false,double? knownAudioDuration=null)
    {
        // Windows can round M4A duration to whole seconds; retain the probed audio length.
        audioDuration=audioOnly&&knownAudioDuration is double length&&double.IsFinite(length)&&length>0?length:null;
        IsAudioOnly=audioOnly;path=Path.GetFullPath(file);Text=(audioOnly?"Listen to audio":"Preview sample")+" · Capture & Release";Font=new Font("Segoe UI",10);BackColor=Color.FromArgb(247,249,251);
        StartPosition=FormStartPosition.CenterParent;ClientSize=audioOnly?new Size(740,220):new Size(900,620);MinimumSize=audioOnly?new Size(640,250):new Size(640,440);ShowInTaskbar=false;
        Icon=System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!)??SystemIcons.Application;
        var root=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(12),ColumnCount=1,RowCount=4};
        root.RowStyles.Add(new(SizeType.Absolute,30));root.RowStyles.Add(new(SizeType.Percent,100));root.RowStyles.Add(new(SizeType.Absolute,40));root.RowStyles.Add(new(SizeType.Absolute,48));Controls.Add(root);
        root.Controls.Add(new Label{Text=audioOnly?"Selected audio file only · mix levels, delay and fades are not applied.":"A temporary sample of your current picture and sound edits.",AutoSize=true},0,0);
        var screen=new System.Windows.Controls.Grid{Background=audioOnly?System.Windows.Media.Brushes.WhiteSmoke:System.Windows.Media.Brushes.Black};screen.Children.Add(media);
        if(audioOnly){media.Visibility=System.Windows.Visibility.Hidden;screen.Children.Add(new System.Windows.Controls.TextBlock{Text=Path.GetFileName(path),FontSize=18,Foreground=System.Windows.Media.Brushes.DarkSlateGray,VerticalAlignment=System.Windows.VerticalAlignment.Center,HorizontalAlignment=System.Windows.HorizontalAlignment.Center,TextWrapping=System.Windows.TextWrapping.Wrap,ToolTip=path,Margin=new System.Windows.Thickness(12)});}
        host.Child=screen;root.Controls.Add(host,0,1);root.Controls.Add(seek,0,2);
        var buttons=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false};var close=new Button{Text="Close",AutoSize=true,DialogResult=DialogResult.OK};
        var external=new Button{Text="Open in player",AutoSize=true};external.Click+=(_,_)=>{Pause();try{System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path){UseShellExecute=true});}catch(Exception e){clock.Text="Could not open player: "+e.Message;}};
        buttons.Controls.AddRange(new Control[]{play,stop,external,close,clock});root.Controls.Add(buttons,0,3);CancelButton=close;
        media.Volume=silentTest?0:1;
        media.MediaOpened+=(_,_)=>{if(disposed)return;play.Enabled=true;stop.Enabled=true;seek.Enabled=Duration>0;playing=true;ready.TrySetResult(true);timer.Start();UpdateClock();};
        media.MediaEnded+=(_,_)=>{if(disposed)return;playing=false;ended=true;play.Text="Replay";UpdateClock();};
        media.MediaFailed+=(_,e)=>{if(disposed)return;Failure=e.ErrorException.Message;playing=false;play.Enabled=false;stop.Enabled=false;seek.Enabled=false;clock.Text="Windows playback unavailable. Try Open in player.";ready.TrySetResult(false);};
        play.Click+=(_,_)=>{if(playing)Pause();else Play();};stop.Click+=(_,_)=>{Seek(0);Play();};
        seek.MouseDown+=(_,_)=>seeking=true;seek.MouseUp+=(_,_)=>{seeking=false;Seek(seek.Value/1000d*Duration);};
        seek.KeyDown+=(_,_)=>seeking=true;seek.KeyUp+=(_,_)=>{seeking=false;Seek(seek.Value/1000d*Duration);};
        timer.Tick+=(_,_)=>{if(!seeking&&Duration>0)seek.Value=Math.Clamp((int)(Position/Duration*1000),0,1000);UpdateClock();};
        Shown+=(_,_)=>{media.Source=new Uri(path,UriKind.Absolute);media.Play();};
        FormClosed+=(_,_)=>ReleaseMedia();
    }
    double Duration=>audioDuration??(media.NaturalDuration.HasTimeSpan?media.NaturalDuration.TimeSpan.TotalSeconds:0);
    void UpdateClock(){if(!disposed)clock.Text=$"{Position:0.0} / {Duration:0.0} s";}
    public void Play(){if(disposed||Failure!=null)return;if(ended){media.Position=TimeSpan.Zero;ended=false;}media.Play();playing=true;play.Text="Pause";}
    public void Pause(){if(disposed)return;media.Pause();playing=false;play.Text="Play";}
    public void Seek(double seconds){if(disposed||Duration<=0)return;ended=false;media.Position=TimeSpan.FromSeconds(Math.Clamp(seconds,0,Math.Max(0,Duration-.01)));UpdateClock();}
    void ReleaseMedia(){if(disposed)return;disposed=true;timer.Stop();media.Stop();media.Close();host.Child=null;timer.Dispose();ready.TrySetResult(false);}
    protected override void Dispose(bool disposing){if(disposing)ReleaseMedia();base.Dispose(disposing);}
}
