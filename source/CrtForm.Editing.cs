namespace GameplayRecorder;

public sealed partial class CrtForm
{
    readonly TabPage cropPage=new("Crop"),audioPage=new("Audio"),exportPage=new("Export");
    readonly NumericUpDown cropLeft=Pixels(),cropTop=Pixels(),cropWidth=Pixels(),cropHeight=Pixels();
    readonly Button resetCrop=new(){Text="Use full picture",AutoSize=true},pickCrop=new(){Text="Select on preview",AutoSize=true};
    readonly Label cropSummary=new(){AutoSize=true,MaximumSize=new Size(340,0)};
    readonly NumericUpDown originalVolume=Percent(100),addedVolume=Percent(50),audioDelay=AudioSeconds(),fadeIn=AudioSeconds(),fadeOut=AudioSeconds();
    readonly CheckBox muteOriginal=new(){Text="Mute original audio",AutoSize=true};
    readonly Button chooseAudio=new(){Text="Add audio file…",AutoSize=true},listenAudio=new(){Text="Listen to file…",AutoSize=true,Enabled=false},removeAudio=new(){Text="Remove",AutoSize=true};
    readonly Label audioFile=new(){Text="No added audio",AutoSize=true,MaximumSize=new Size(335,0)},audioSummary=new(){AutoSize=true,MaximumSize=new Size(335,0)};
    readonly ComboBox mixTrack=new(){DropDownStyle=ComboBoxStyle.DropDownList,Width=132},sizePreset=new(){DropDownStyle=ComboBoxStyle.DropDownList,Width=335},qualityPreset=new(){DropDownStyle=ComboBoxStyle.DropDownList,Width=335};
    readonly Label outputSummary=new(){AutoSize=true,MaximumSize=new Size(335,0)};
    readonly Label sourcePresetHint=new(){Text="Optional preset · choose after opening a source",AutoSize=true,MaximumSize=new Size(325,0),Margin=new Padding(8,0,8,8),ForeColor=Color.DimGray};
    bool CropOverview=>toolTabs.SelectedTab==cropPage;
    AddedAudioSource? addedAudio;
    bool editingReset,cropPicking;
    Point? cropAnchor;Rectangle? dragCrop;
    CropSelection? movingCrop;
    static NumericUpDown Pixels()=>new(){Maximum=33554432,Width=132,Increment=2};
    static NumericUpDown Percent(int value)=>new(){Maximum=200,Value=value,Width=132,Increment=5};
    static NumericUpDown AudioSeconds()=>new(){Maximum=86400,DecimalPlaces=2,Increment=.1m,Width=132};

    void InitializeEditing()
    {
        toolTabs.TabPages.Insert(1,cropPage);toolTabs.TabPages.Add(audioPage);toolTabs.TabPages.Add(exportPage);
        FlowLayoutPanel Page(TabPage tab){tab.Padding=new Padding(8);var p=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,AutoScroll=true};tab.Controls.Add(p);return p;}
        void Note(FlowLayoutPanel p,string text)=>p.Controls.Add(new Label{Text=text,AutoSize=true,MaximumSize=new Size(335,0),Margin=new Padding(3,8,3,12),ForeColor=Color.FromArgb(76,86,99)});
        void Field(FlowLayoutPanel p,string label,Control field){field.AccessibleName=label;var row=new FlowLayoutPanel{Width=345,Height=37,WrapContents=false};row.Controls.Add(new Label{Text=label,Width=185,Padding=new Padding(0,5,0,0)});row.Controls.Add(field);p.Controls.Add(row);}
        var crop=Page(cropPage);
        Note(crop,"Choose a crop, then drag inside its blue rectangle to move it without changing its size. Use Select on preview to draw a new area.");
        crop.Controls.Add(pickCrop);Field(crop,"Left",cropLeft);Field(crop,"Top",cropTop);Field(crop,"Width",cropWidth);Field(crop,"Height",cropHeight);
        var shapes=new FlowLayoutPanel{Width=345,Height=44,WrapContents=false};
        foreach(var shape in new[]{("Square",1d),("16:9",16d/9),("9:16",9d/16)}){
            var b=new Button{Text=shape.Item1,AutoSize=true};StyleButton(b,false);shapes.Controls.Add(b);
            b.Click+=(_,_)=>{if(source==null)return;int w=source.Width,h=source.Height;if((double)w/h>shape.Item2)w=(int)(h*shape.Item2);else h=(int)(w/shape.Item2);w=w/2*2;h=h/2*2;SetCrop(new((source.Width-w)/2,(source.Height-h)/2,w,h));};
        }
        crop.Controls.Add(shapes);crop.Controls.Add(resetCrop);crop.Controls.Add(cropSummary);
        Note(crop,"This tab keeps the full source and selection outline visible. Update preview, then switch to another tab to see the cropped result. Crop comes before filters. MP4 needs an even width and height.");
        foreach(var field in new[]{cropLeft,cropTop,cropWidth,cropHeight})field.ValueChanged+=(_,_)=>{if(editingReset)return;Changed();RefreshEditSummary();picture.Invalidate();};
        toolTabs.SelectedIndexChanged+=(_,_)=>{if(cropPicking)StopCropSelection();Display();original.Enabled=!CropOverview;picture.Invalidate();};
        resetCrop.Click+=(_,_)=>{if(source!=null)SetCrop(new(0,0,source.Width,source.Height));};
        pickCrop.Click+=(_,_)=>{
            if(source==null||raw==null||job!=null)return;
            if(cropPicking){StopCropSelection();return;}
            cropPicking=true;cropAnchor=null;dragCrop=null;pickCrop.Text="Cancel selection";
            actual.Checked=false;original.Enabled=false;actual.Enabled=false;status.Text="Drag over the picture to choose the area to keep.";
            picture.Cursor=Cursors.Cross;picture.Invalidate();
        };
        picture.MouseDown+=(_,e)=>{
            if(job!=null||!CropOverview||e.Button!=MouseButtons.Left||PreviewPoint(e.Location) is not Point p)return;
            if(cropPicking){cropAnchor=p;picture.Capture=true;return;}
            if(source==null)return;
            var crop=ReadEditOptions().Crop!;
            try{crop.Validate(source.Width,source.Height);}catch{return;}
            if(new Rectangle(crop.Left,crop.Top,crop.Width,crop.Height).Contains(p)){
                movingCrop=crop;cropAnchor=p;picture.Capture=true;picture.Cursor=Cursors.SizeAll;
            }
        };
        picture.MouseMove+=(_,e)=>{
            if(job!=null)return;
            if(cropAnchor is Point a&&PreviewPoint(e.Location,clamp:true) is Point p){
                if(cropPicking)dragCrop=Rectangle.FromLTRB(Math.Min(a.X,p.X),Math.Min(a.Y,p.Y),Math.Max(a.X,p.X),Math.Max(a.Y,p.Y));
                else if(movingCrop is {} crop&&source!=null)dragCrop=new Rectangle(Math.Clamp(crop.Left+p.X-a.X,0,source.Width-crop.Width),Math.Clamp(crop.Top+p.Y-a.Y,0,source.Height-crop.Height),crop.Width,crop.Height);
                picture.Invalidate();return;
            }
            if(!cropPicking&&CropOverview&&PreviewPoint(e.Location) is Point hover){var crop=ReadEditOptions().Crop!;picture.Cursor=new Rectangle(crop.Left,crop.Top,crop.Width,crop.Height).Contains(hover)?Cursors.SizeAll:Cursors.Default;}
        };
        picture.MouseUp+=(_,e)=>{
            if(e.Button!=MouseButtons.Left||cropAnchor==null)return;
            picture.Capture=false;
            if(dragCrop is Rectangle r&&r.Width>=2&&r.Height>=2){SetCrop(new(r.X,r.Y,cropPicking?r.Width/2*2:r.Width,cropPicking?r.Height/2*2:r.Height));}
            StopCropSelection();
        };
        picture.MouseLeave+=(_,_)=>{if(cropAnchor==null&&!cropPicking)picture.Cursor=Cursors.Default;};
        picture.Paint+=(_,e)=>{
            if((!cropPicking&&!CropOverview)||raw==null)return;Rectangle target=PictureBounds();var r=dragCrop??new Rectangle((int)cropLeft.Value,(int)cropTop.Value,(int)cropWidth.Value,(int)cropHeight.Value);
            var shown=new RectangleF(target.Left+(float)r.X/raw.Width*target.Width,target.Top+(float)r.Y/raw.Height*target.Height,(float)r.Width/raw.Width*target.Width,(float)r.Height/raw.Height*target.Height);
            using var shade=new SolidBrush(Color.FromArgb(25,48,154,245));using var pen=new Pen(Color.FromArgb(48,154,245),2);e.Graphics.FillRectangle(shade,shown);e.Graphics.DrawRectangle(pen,shown.X,shown.Y,shown.Width,shown.Height);
        };

        var audio=Page(audioPage);Note(audio,"Change the saved clip’s sound. Use Play 10 s sample below to hear the mix before exporting.");
        audio.Controls.Add(muteOriginal);Field(audio,"Original volume (%)",originalVolume);
        Note(audio,"Adding audio mixes one selected original track and the added file into a single stereo soundtrack. With no added file, original tracks stay separate.");
        Field(audio,"Original track for mix",mixTrack);
        var audioButtons=new FlowLayoutPanel{Width=345,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,MaximumSize=new Size(345,0),WrapContents=true,Margin=new Padding(3,3,3,8)};audioButtons.Controls.AddRange(new Control[]{chooseAudio,listenAudio,removeAudio});audio.Controls.Add(audioButtons);audio.Controls.Add(audioFile);
        help.SetToolTip(listenAudio,"Listen to the selected audio file by itself, from its beginning. Video, mix volumes, start delay and fades are not applied.");
        listenAudio.Click+=async(_,_)=>await ListenToAddedAsync();
        Field(audio,"Added volume (%)",addedVolume);Field(audio,"Start in saved clip (s)",audioDelay);Field(audio,"Fade in (s)",fadeIn);Field(audio,"Fade out (s)",fadeOut);audio.Controls.Add(audioSummary);
        Note(audio,"The added file plays once from its beginning, starting at the chosen time in the exported section. It is cut at the clip’s end. Fades affect only added audio. Loud boosts/mixes use a limiter.");
        chooseAudio.Click+=async(_,_)=>{using var dialog=new OpenFileDialog{Title="Choose audio to add",Filter="Audio and video|*.wav;*.mp3;*.m4a;*.aac;*.flac;*.ogg;*.opus;*.mp4;*.mkv;*.webm|All files|*.*"};if(dialog.ShowDialog(this)==DialogResult.OK)await OpenAddedAudioAsync(dialog.FileName);};
        removeAudio.Click+=(_,_)=>{addedAudio=null;RefreshEditSummary();SetBusy(false);};
        foreach(var n in new[]{originalVolume,addedVolume,audioDelay,fadeIn,fadeOut})n.ValueChanged+=(_,_)=>AudioChanged();muteOriginal.CheckedChanged+=(_,_)=>AudioChanged();mixTrack.SelectedIndexChanged+=(_,_)=>AudioChanged();

        var presets=Page(exportPage);Note(presets,"Save a separate H.264 MP4. These presets keep the picture’s proportions and never enlarge it.");
        Note(presets,"Picture size");sizePreset.Items.AddRange(new object[]{"Original / cropped size","Fit within 1080p · landscape or portrait","Fit within 720p · landscape or portrait"});sizePreset.SelectedIndex=0;presets.Controls.Add(sizePreset);
        Note(presets,"Quality");qualityPreset.Items.AddRange(new object[]{"High · larger files","Balanced","Smaller file · more compression"});qualityPreset.SelectedIndex=0;presets.Controls.Add(qualityPreset);
        Note(presets,"Quality controls compression, not a guaranteed file size. CPU and NVIDIA results can differ. Frame timing and the saved section stay unchanged.");presets.Controls.Add(outputSummary);
        sizePreset.SelectedIndexChanged+=(_,_)=>{if(editingReset)return;Changed();RefreshEditSummary();};qualityPreset.SelectedIndexChanged+=(_,_)=>AudioChanged();
        foreach(var b in new[]{resetCrop,pickCrop,chooseAudio,listenAudio,removeAudio})StyleButton(b,false);
        help.SetToolTip(audioDelay,"Seconds from the beginning of the saved selection, not the source video.");
        help.SetToolTip(sizePreset,"Fits the long and short edges within 1920 × 1080 or 1280 × 720, also for portrait video. No upscaling.");
        RefreshEditSummary();
    }
    Rectangle PictureBounds()
    {
        if(raw==null||picture.ClientSize.Width==0||picture.ClientSize.Height==0)return Rectangle.Empty;
        if(actual.Checked)return new(0,0,raw.Width,raw.Height);
        double scale=Math.Min((double)picture.ClientSize.Width/raw.Width,(double)picture.ClientSize.Height/raw.Height);
        int w=(int)Math.Round(raw.Width*scale),h=(int)Math.Round(raw.Height*scale);return new((picture.ClientSize.Width-w)/2,(picture.ClientSize.Height-h)/2,w,h);
    }
    void StopCropSelection()
    {
        cropPicking=false;cropAnchor=null;dragCrop=null;movingCrop=null;picture.Capture=false;pickCrop.Text="Select on preview";picture.Cursor=Cursors.Default;original.Enabled=!CropOverview;actual.Enabled=true;picture.Invalidate();
    }
    Point? PreviewPoint(Point location,bool clamp=false)
    {
        var b=PictureBounds();if(raw==null||b.Width<=0||b.Height<=0||(!clamp&&!b.Contains(location)))return null;
        return new Point(Math.Clamp((int)Math.Round((location.X-b.Left)*(double)raw.Width/b.Width),0,raw.Width),Math.Clamp((int)Math.Round((location.Y-b.Top)*(double)raw.Height/b.Height),0,raw.Height));
    }
    void SetCrop(CropSelection value)
    {
        editingReset=true;cropLeft.Value=value.Left;cropTop.Value=value.Top;cropWidth.Value=value.Width;cropHeight.Value=value.Height;editingReset=false;Changed();RefreshEditSummary();Display();picture.Invalidate();
    }
    VideoEditOptions ReadEditOptions()=>new(source==null?null:new CropSelection((int)cropLeft.Value,(int)cropTop.Value,(int)cropWidth.Value,(int)cropHeight.Value),
        (ExportSize)Math.Max(0,sizePreset.SelectedIndex),(ExportQuality)Math.Max(0,qualityPreset.SelectedIndex),new AudioEdit(muteOriginal.Checked,(double)originalVolume.Value/100,addedAudio,(double)addedVolume.Value/100,(double)audioDelay.Value,(double)fadeIn.Value,(double)fadeOut.Value,Math.Max(0,mixTrack.SelectedIndex)));
    bool ValidEditOptions()
    {
        if(source==null||source.Still)return false;
        try{var edit=ReadEditOptions();edit.Dimensions(source.Width,source.Height,true);edit.Audio!.Validate(Selection().Duration,source.AudioCodecs.Length);return true;}catch{return false;}
    }
    void ResetEditingSource()
    {
        if(source==null)return;editingReset=true;
        cropLeft.Value=0;cropTop.Value=0;cropWidth.Value=source.Width;cropHeight.Value=source.Height;
        addedAudio=null;muteOriginal.Checked=false;originalVolume.Value=100;addedVolume.Value=50;audioDelay.Value=0;fadeIn.Value=0;fadeOut.Value=0;
        mixTrack.Items.Clear();for(int i=0;i<source.AudioCodecs.Length;i++)mixTrack.Items.Add($"Track {i+1} · {source.AudioCodecs[i]}");if(mixTrack.Items.Count>0)mixTrack.SelectedIndex=0;
        sizePreset.SelectedIndex=0;qualityPreset.SelectedIndex=0;StopCropSelection();editingReset=false;RefreshEditSummary();
    }
    void AudioChanged()
    {
        if(editingReset)return;RefreshEditSummary();if(job!=null)return;SetBusy(false);
        if(source?.Still==false){try{ReadEditOptions().Audio!.Validate(Selection().Duration,source.AudioCodecs.Length);status.ForeColor=Color.FromArgb(42,92,78);status.Text=dirty?"Picture changed — update the preview before exporting.":"Export settings ready. Audio changes apply to the saved clip.";}catch(Exception e){Error(e.Message);}}
    }
    void RefreshEditSummary()
    {
        if(source==null){cropSummary.Text="Open a source to crop its picture.";outputSummary.Text="Open a video to see output details.";return;}
        try{var edit=ReadEditOptions();var size=edit.Dimensions(source.Width,source.Height,!source.Still);cropSummary.Text=$"Source: {source.Width} × {source.Height}\nKeep: {edit.Crop!.Width} × {edit.Crop.Height} at {edit.Crop.Left}, {edit.Crop.Top}";cropSummary.ForeColor=Color.FromArgb(42,92,78);outputSummary.Text=$"Output: {size.Width} × {size.Height}\nH.264 MP4 · {qualityPreset.Text}";sourcePresetHint.Text=$"{size.Width} × {size.Height} · {CrtPresets.Aspect(size.Width,size.Height)}\nChoose Match this picture to apply a mild preset.";}
        catch(Exception e){cropSummary.Text=e.Message;cropSummary.ForeColor=Color.Firebrick;outputSummary.Text=e.Message;}
        audioFile.Text=addedAudio==null?"No added audio":Path.GetFileName(addedAudio.Path)+$" · {addedAudio.Duration:0.##} s";
        help.SetToolTip(audioFile,addedAudio?.Path??"");
        try{if(!source.Still)ReadEditOptions().Audio!.Validate(Selection().Duration,source.AudioCodecs.Length);audioSummary.ForeColor=Color.FromArgb(42,92,78);audioSummary.Text=addedAudio!=null?"Export: one stereo soundtrack" : muteOriginal.Checked||originalVolume.Value==0?"Export: no audio":"Export: original tracks · "+source.AudioCodecs.Length;}
        catch(Exception e){audioSummary.ForeColor=Color.Firebrick;audioSummary.Text=e.Message;}
    }
    void SetEditingBusy(bool busy)
    {
        if(busy)StopCropSelection();
        samplePreview.Enabled=!busy&&source?.Still==false;
        cropPage.Enabled=!busy&&source!=null;audioPage.Enabled=!busy&&source?.Still==false;exportPage.Enabled=!busy&&source?.Still==false;
        foreach(var field in new Control[]{addedVolume,audioDelay,fadeIn,fadeOut,removeAudio})field.Enabled=addedAudio!=null;
        listenAudio.Enabled=!busy&&addedAudio!=null;
        mixTrack.Enabled=addedAudio!=null&&source?.AudioCodecs.Length>0&&!muteOriginal.Checked;originalVolume.Enabled=!muteOriginal.Checked&&source?.AudioCodecs.Length>0;
    }
    async Task<bool> OpenAddedAudioAsync(string path)
    {
        if(job!=null||source==null||source.Still)return false;job=new();SetBusy(true);status.Text="Reading added audio…";
        try{addedAudio=await CrtRenderer.InspectAudioAsync(path,job.Token);audioDelay.Value=0;fadeIn.Value=0;fadeOut.Value=0;RefreshEditSummary();status.Text="Audio ready. Changes will be applied on export.";return true;}
        catch(OperationCanceledException){status.Text="Audio selection cancelled.";return false;}catch(Exception e){Error(e.Message);return false;}
        finally{job.Dispose();job=null;SetBusy(false);if(closeAfter)BeginInvoke((Action)Close);}
    }
}
