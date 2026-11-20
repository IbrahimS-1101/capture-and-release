namespace GameplayRecorder;

public sealed partial class CrtForm
{
    readonly Button samplePreview=new(){Text="Play 10 s sample…",AutoSize=true,Enabled=false};
    async Task<bool> ListenToAddedAsync(bool testPlayback=false,string? testCapture=null)
    {
        if(job!=null||addedAudio==null)return false;
        try{
            var file=new FileInfo(addedAudio.Path);
            if(!file.Exists)throw new IOException("The selected audio file is no longer available. Choose it again.");
            if(file.Length!=addedAudio.FileLength||file.LastWriteTimeUtc!=addedAudio.LastWriteUtc)throw new IOException("The selected audio file changed. Choose it again before listening or exporting.");
            using var player=new SamplePlayer(addedAudio.Path,silentTest:testPlayback,audioOnly:true,knownAudioDuration:addedAudio.Duration);
            if(testPlayback){
                player.Show(this);if(!await player.Ready.WaitAsync(TimeSpan.FromSeconds(12)))throw new IOException("Could not open this audio file: "+player.Failure);
                await Task.Delay(220);if(!player.IsAudioOnly||!player.HasAudio||player.Position<=0)throw new IOException("Audio-only playback did not start.");
                if(Math.Abs(player.DurationSeconds-addedAudio.Duration)>.15)throw new IOException($"Audio preview duration: Windows {player.DurationSeconds:0.###} s; selected file {addedAudio.Duration:0.###} s.");
                player.Pause();double position=player.Position;await Task.Delay(120);if(Math.Abs(player.Position-position)>.15)throw new IOException("Audio did not pause.");
                double target=addedAudio.Duration>10?addedAudio.Duration-1:.4;player.Seek(target);player.Play();await Task.Delay(180);if(!player.IsPlaying||player.Position<target-.1)throw new IOException("Audio did not seek and resume.");
                if(testCapture!=null){player.BringToFront();player.Activate();await Task.Delay(100);using var image=new Bitmap(player.Width,player.Height);using(var graphics=Graphics.FromImage(image))graphics.CopyFromScreen(player.Location,Point.Empty,player.Size);image.Save(testCapture);}
                player.Close();
            }else player.ShowDialog(this);
            status.Text="Audio file preview closed. Your mix settings are unchanged.";return true;
        }catch(Exception e){Error("Audio file preview: "+e.Message);return false;}
    }
    void InitializePlayback(FlowLayoutPanel row)
    {
        row.WrapContents=true;row.Controls.Add(samplePreview);StyleButton(samplePreview,false);
        row.SetFlowBreak(update,true);
        row.Controls.Add(new Label{Text="From preview time, within the kept section",AutoSize=true,MaximumSize=new Size(215,0),Margin=new Padding(5,8,0,0),ForeColor=Color.DimGray});
        help.SetToolTip(samplePreview,"Renders up to 10 seconds with the current crop, filter, audio and export settings, then plays it inside the app. If the preview time is outside the kept section, starts at its beginning. This is a temporary sample, not a full export.");
        samplePreview.Click+=async(_,_)=>await PlaySampleAsync();
    }
    async Task<bool> PlaySampleAsync(bool testPlayback=false,string? testCapture=null)
    {
        if(job!=null||source==null||source.Still)return false;
        var selected=source;CrtSettings snapshot;VideoEditOptions edit;TrimSelection range;PreviewSegment part;VideoFilterKind filter;
        try{
            snapshot=settings.Clone();filter=(VideoFilterKind)filterChoice.SelectedIndex;edit=ReadEditOptions();range=Selection();edit.Dimensions(source.Width,source.Height,true);edit.Audio!.Validate(range.Duration,source.AudioCodecs.Length);
            double time=(double)seconds.Value;part=new(time>=range.Start&&time<range.End?time-range.Start:0);
        }catch(Exception e){Error(e.Message);return false;}
        string folder=Path.Combine(Path.GetTempPath(),"CaptureAndRelease-Sample-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        job=new();SetBusy(true);cancel.Text="Cancel sample";exportProgress.Value=0;exportProgress.Visible=true;status.Text="Preparing a temporary playback sample…";
        var active=job;var progress=new Progress<CrtExportProgress>(p=>{if(ReferenceEquals(job,active)){exportProgress.Value=Math.Clamp((int)(p.Fraction*1000),0,1000);status.Text=$"Preparing sample: {p.Fraction:P0}";}});
        try{
            var result=await CrtExporter.ExportAsync(selected,Path.Combine(folder,"sample.mp4"),snapshot,exportEncoder.SelectedIndex==1?"h264_nvenc":"libx264",progress,job.Token,range,filter,edit,part);
            job.Token.ThrowIfCancellationRequested();if(closeAfter)return false;
            exportProgress.Visible=false;cancel.Enabled=false;status.Text="Playing sample. Close the player to adjust your edits.";
            using var player=new SamplePlayer(result.Path,silentTest:testPlayback);
            if(testPlayback){
                player.Show(this);if(!await player.Ready.WaitAsync(TimeSpan.FromSeconds(12)))throw new IOException("Sample player could not open the clip: "+player.Failure);
                await Task.Delay(350);if(!player.HasPicture||!player.HasAudio||player.Position<=0)throw new IOException("Sample did not begin picture/audio playback.");
                if(testCapture!=null){player.BringToFront();player.Activate();await Task.Delay(150);using var image=new Bitmap(player.Width,player.Height);using(var graphics=Graphics.FromImage(image))graphics.CopyFromScreen(player.Location,Point.Empty,player.Size);image.Save(testCapture);}
                player.Pause();double paused=player.Position;await Task.Delay(200);if(Math.Abs(player.Position-paused)>.15)throw new IOException("Sample did not pause.");
                player.Seek(.5);player.Play();await Task.Delay(300);if(!player.IsPlaying)throw new IOException("Sample did not resume.");player.Close();
            }else player.ShowDialog(this);
            status.Text="Sample closed. Adjust settings and play another sample, or export the full kept section.";return true;
        }catch(OperationCanceledException){status.Text="Sample cancelled. Your source and saved exports are unchanged.";return false;}
        catch(Exception e){Error("Sample preview: "+e.Message);return false;}
        finally{
            job.Dispose();job=null;exportProgress.Visible=false;SetBusy(false);
            try{Directory.Delete(folder,true);}catch{}
            if(closeAfter)BeginInvoke((Action)Close);
        }
    }
}
