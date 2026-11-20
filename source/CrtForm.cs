using System.Text.Json;

namespace GameplayRecorder;

public sealed partial class CrtForm : Form
{
    CrtSettings settings=new();CrtSource? source;
    readonly Button open=new(){Text="Choose source…",AutoSize=true},update=new(){Text="Update preview",AutoSize=true},cancel=new(){Text="Cancel",AutoSize=true,Enabled=false},save=new(){Text="Save frame PNG…",AutoSize=true,Enabled=false};
    readonly Button export=new(){Text="Export video…",AutoSize=true,Enabled=false};
    readonly ComboBox exportEncoder=new(){DropDownStyle=ComboBoxStyle.DropDownList,Width=135};
    readonly ProgressBar exportProgress=new(){Dock=DockStyle.Fill,Maximum=1000,Visible=false};
    readonly ToolTip help=new(){AutoPopDelay=15000,InitialDelay=450,ReshowDelay=100};
    readonly NumericUpDown seconds=new(){DecimalPlaces=3,Increment=.1m,Maximum=86400,Width=100,Enabled=false};
    readonly Label file=new(){AutoSize=false,Width=680,Height=36,TextAlign=ContentAlignment.MiddleLeft,Text="Choose an image or a saved recording",AutoEllipsis=true},status=new(){Dock=DockStyle.Fill,Text="Choose a source to begin. Your original file stays unchanged.",AutoEllipsis=true};
    readonly Label dimensions=new(){AutoSize=true,Text="Preview renders at original frame dimensions. Fit view can change fine patterns."};
    readonly CheckBox original=new(){Text="Show original",AutoSize=true},actual=new(){Text="100% view",AutoSize=true};
    readonly Panel viewport=new(){Dock=DockStyle.Fill,AutoScroll=true,BackColor=Color.FromArgb(24,28,34)};
    readonly PictureBox picture=new(){Dock=DockStyle.Fill,SizeMode=PictureBoxSizeMode.Zoom};
    readonly FlowLayoutPanel controls=new(){Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,AutoScroll=true};
    readonly Dictionary<string,CheckBox> effects=new();
    readonly Dictionary<string,List<Control>> effectRows=new();
    readonly List<(CrtField Field,NumericUpDown Number,TrackBar Slider)> fields=new();
    readonly bool testing;
    CancellationTokenSource? job;
    Bitmap? raw,processed;
    bool refreshing,dirty=true,closeAfter;
    CrtSettings? renderedSettings;
    public CrtForm(bool testing=false)
    {
        this.testing=testing;Text="Capture & Release · Video tools · 0.4.0 beta 1";Font=new Font("Segoe UI",9.5f);BackColor=Color.FromArgb(247,249,251);
        Icon=System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!)??SystemIcons.Application;
        StartPosition=FormStartPosition.CenterParent;ClientSize=new Size(1220,850);MinimumSize=new Size(1000,720);
        var root=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(16),ColumnCount=1,RowCount=4};
        root.RowStyles.Add(new(SizeType.Absolute,94));root.RowStyles.Add(new(SizeType.Percent,100));root.RowStyles.Add(new(SizeType.Absolute,58));root.RowStyles.Add(new(SizeType.Absolute,98));Controls.Add(root);
        var top=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=2,Margin=new Padding(0)};
        top.RowStyles.Add(new(SizeType.Absolute,48));top.RowStyles.Add(new(SizeType.Absolute,42));
        var heading=new FlowLayoutPanel{Dock=DockStyle.Fill,Margin=new Padding(0),WrapContents=false};
        heading.Controls.Add(new Label{Text="Video tools",Font=new Font("Segoe UI",20,FontStyle.Bold),ForeColor=Color.FromArgb(34,107,87),AutoSize=true,Margin=new Padding(0,0,18,0)});
        heading.Controls.Add(new Label{Text="Edit your video. Shape its look and sound. Save a separate copy.",AutoSize=true,ForeColor=Color.FromArgb(90,100,112),Margin=new Padding(0,15,0,0)});
        var sourceRow=new FlowLayoutPanel{Dock=DockStyle.Fill,Margin=new Padding(0),WrapContents=false};sourceRow.Controls.AddRange(new Control[]{open,file});
        var timeLabel=new Label{Text="Preview time (s)",AutoSize=true,Padding=new Padding(0,9,2,0)};
        var frameRow=new FlowLayoutPanel{Dock=DockStyle.Fill,Margin=new Padding(0),WrapContents=false};seconds.Margin=new Padding(3,8,8,0);frameRow.Controls.AddRange(new Control[]{timeLabel,seconds,update});
        help.SetToolTip(seconds,"Choose a preview position. Use the Trim tab to set the section to keep.");help.SetToolTip(timeLabel,"Preview time alone does not set a trim boundary.");
        top.Controls.Add(heading,0,0);top.Controls.Add(sourceRow,0,1);root.Controls.Add(top,0,0);
        var body=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,Margin=new Padding(0)};
        body.ColumnStyles.Add(new(SizeType.Percent,100));body.ColumnStyles.Add(new(SizeType.Absolute,410));
        viewport.BorderStyle=BorderStyle.FixedSingle;controls.BackColor=Color.White;controls.BorderStyle=BorderStyle.FixedSingle;
        var editor=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=2,Margin=new Padding(0)};editor.RowStyles.Add(new(SizeType.Percent,100));editor.RowStyles.Add(new(SizeType.Absolute,94));editor.Controls.Add(toolTabs,0,0);editor.Controls.Add(frameRow,0,1);InitializePlayback(frameRow);
        viewport.Controls.Add(picture);body.Controls.Add(viewport,0,0);body.Controls.Add(editor,1,0);root.Controls.Add(body,0,1);
        controls.Controls.Add(new Label{Text="CRT controls",Font=new Font(Font,FontStyle.Bold),AutoSize=true,Margin=new Padding(8)});
        var presets=new FlowLayoutPanel{Width=345,Height=46,WrapContents=false};
        var reset=new Button{Text="Defaults (flat)",AutoSize=true};var dos=new Button{Text="DOS 320×200",AutoSize=true};
        presets.Controls.AddRange(new Control[]{reset,dos});controls.Controls.Add(presets);
        var match=new Button{Name="matchPicturePreset",Text="Match this picture",AutoSize=true};StyleButton(match,false);controls.Controls.Add(match);controls.Controls.Add(sourcePresetHint);
        help.SetToolTip(match,"Optional mild CRT preset based on the current crop/output size and aspect ratio. Keeps curvature on/off, resets the other CRT controls. Nothing is applied automatically.");
        match.Click+=(_,_)=>{try{if(source==null)throw new ArgumentException("Open a source first.");var size=ReadEditOptions().Dimensions(source.Width,source.Height);ApplySettings(CrtPresets.MatchPicture(size.Width,size.Height,settings.Enabled("curvature")));status.Text=$"Matched {size.Width} × {size.Height} · {CrtPresets.Aspect(size.Width,size.Height)}. Update or play a sample to judge the look.";}catch(Exception e){Error(e.Message);}};
        reset.Click+=(_,_)=>ApplySettings(new CrtSettings());
        dos.Click+=(_,_)=>{var s=new CrtSettings();foreach(var f in CrtSettings.Fields.Where(f=>f.Key=="sourceSize"))s.Set(f,f.Index==0?320:200);ApplySettings(s);};
        AddGroup("curvature","Curvature",new[]{"curvatureAmount","curvatureScale"});
        AddGroup("softening","Softening",new[]{"softeningRadius","softeningStrength"});
        AddGroup("scanlines","Scanlines",new[]{"scanlineCount","scanlineStrength"});
        AddGroup("mask","Phosphor mask",new[]{"maskColumns","maskRows","maskStrength"});
        AddGroup("bloom","Bloom",new[]{"bloomRadius","bloomStrength"});
        var advanced=new CheckBox{Name="advancedToggle",Text="Advanced settings (optional)",AutoSize=true,Margin=new Padding(8)};controls.Controls.Add(advanced);
        var advancedBox=new FlowLayoutPanel{Name="advancedDetails",Width=345,AutoSize=true,FlowDirection=FlowDirection.TopDown,WrapContents=false,Visible=false};
        void AdvancedSection(string title,string text,Func<CrtField,bool> matches){
            advancedBox.Controls.Add(new Label{Text=title,Font=new Font(Font,FontStyle.Bold),AutoSize=true,Margin=new Padding(4,12,4,4)});
            advancedBox.Controls.Add(new Label{Text=text,MaximumSize=new Size(325,0),AutoSize=true,Margin=new Padding(4,0,4,8)});
            foreach(var f in CrtSettings.Fields.Where(matches)){var row=CreateRow(f);advancedBox.Controls.Add(row);if(f.Key.StartsWith("scanlineFade"))effectRows["scanlines"].Add(row);}
        }
        AdvancedSection("Original game size","Sets the pixel scale for softening and bloom. 0 uses the actual picture size. This does not resize the video or set the scanline count.",f=>f.Key=="sourceSize");
        AdvancedSection("Picture area inside the frame","Use this for borders around the game. Values are percentages of the whole frame. The surrounding area stays unchanged; this does not crop. Full image: left 0, top 0, width 100, height 100.",f=>f.Key=="contentRect");
        AdvancedSection("Scanline edge fade","Shapes the dark edges of each scanline, not an animation. Positions run from 0 at the line centre to 0.5 at its edge. A lower start widens the darkened band. Defaults: 0.30 / 0.50; usually adjust Strength instead.",f=>f.Key.StartsWith("scanlineFade"));
        controls.Controls.Add(advancedBox);advanced.CheckedChanged+=(_,_)=>advancedBox.Visible=advanced.Checked;
        var files=new FlowLayoutPanel{Width=345,Height=46,WrapContents=false};var loadSettings=new Button{Text="Load CRT settings…",AutoSize=true};var saveSettings=new Button{Text="Save CRT settings…",AutoSize=true};files.Controls.AddRange(new Control[]{loadSettings,saveSettings});controls.Controls.Add(files);
        exportEncoder.Items.AddRange(new object[]{"CPU encoding","NVIDIA encoding"});exportEncoder.SelectedIndex=0;
        help.SetToolTip(exportEncoder,"Selects the H.264 encoder. Original + CPU needs no Vulkan. CRT requires Vulkan; NVIDIA encoding requires supported NVIDIA hardware. Quality and size are under Export.");
        help.SetToolTip(export,"Exports the range chosen under Trim, with the completed preview's filter. Whole-video AAC can be copied; trimming encodes audio to AAC for accurate boundaries.");
        original.Margin=new Padding(3,14,10,0);actual.Margin=new Padding(3,14,14,0);exportEncoder.Margin=new Padding(16,8,6,0);
        var bottom=new FlowLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(0,8,0,0),WrapContents=false};bottom.Controls.AddRange(new Control[]{original,actual,save,exportEncoder,export});root.Controls.Add(bottom,0,2);
        var footer=new TableLayoutPanel{Dock=DockStyle.Fill,RowCount=3,ColumnCount=1};footer.RowStyles.Add(new(SizeType.Absolute,22));footer.RowStyles.Add(new(SizeType.Absolute,42));footer.RowStyles.Add(new(SizeType.Percent,100));
        var operation=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Margin=new Padding(0)};operation.ColumnStyles.Add(new(SizeType.Percent,100));operation.ColumnStyles.Add(new(SizeType.Absolute,140));
        exportProgress.Dock=DockStyle.None;exportProgress.Anchor=AnchorStyles.Left|AnchorStyles.Right;exportProgress.Height=12;cancel.Anchor=AnchorStyles.Right;operation.Controls.Add(exportProgress,0,0);operation.Controls.Add(cancel,1,0);
        footer.Controls.Add(dimensions,0,0);footer.Controls.Add(operation,0,1);footer.Controls.Add(status,0,2);root.Controls.Add(footer,0,3);
        dimensions.ForeColor=Color.FromArgb(90,100,112);dimensions.Font=new Font("Segoe UI",9);
        foreach(var button in new[]{open,update,cancel,save,export,reset,dos,loadSettings,saveSettings})StyleButton(button,button==export);
        export.EnabledChanged+=(_,_)=>StyleButton(export,true);
        open.Click+=async(_,_)=>{using var picker=new OpenFileDialog{Title="Open an SDR image or recording",Filter="Images and recordings|*.png;*.jpg;*.jpeg;*.bmp;*.webp;*.mp4;*.mkv;*.mov;*.avi;*.webm|All files|*.*"};if(picker.ShowDialog(this)==DialogResult.OK)await OpenAsync(picker.FileName);};
        update.Click+=async(_,_)=>await UpdateAsync();cancel.Click+=(_,_)=>job?.Cancel();
        seconds.ValueChanged+=(_,_)=>Changed();original.CheckedChanged+=(_,_)=>Display();actual.CheckedChanged+=(_,_)=>Display();
        save.Click+=(_,_)=>SavePng();
        export.Click+=async(_,_)=>{if(source==null||source.Still)return;string suffix=(HasTrim?"-clip":"")+(renderedFilter==VideoFilterKind.Crt?"-CRT":"");if(suffix.Length==0)suffix="-copy";using var picker=new SaveFileDialog{Title="Export a separate copy",Filter="MP4 video|*.mp4",InitialDirectory=Path.GetDirectoryName(source.Path),FileName=Path.GetFileNameWithoutExtension(source.Path)+suffix+".mp4",OverwritePrompt=false};if(picker.ShowDialog(this)==DialogResult.OK)await ExportToAsync(picker.FileName);};
        loadSettings.Click+=(_,_)=>{using var picker=new OpenFileDialog{Filter="CRT settings|*.json"};if(picker.ShowDialog(this)==DialogResult.OK)try{ApplySettings(CrtSettings.Parse(File.ReadAllText(picker.FileName)));}catch(Exception e){Error(e.Message);}};
        saveSettings.Click+=(_,_)=>{try{settings.Validate();using var picker=new SaveFileDialog{Filter="CRT settings|*.json",FileName="crt-settings.json",OverwritePrompt=false};if(picker.ShowDialog(this)==DialogResult.OK){using var stream=new FileStream(picker.FileName,FileMode.CreateNew);using var writer=new StreamWriter(stream);writer.Write(settings.Json());status.Text="Settings saved.";}}catch(Exception e){Error(e.Message);}};
        FormClosing+=(_,e)=>{if(job!=null){e.Cancel=true;closeAfter=true;job.Cancel();}else{picture.Image=null;raw?.Dispose();processed?.Dispose();help.Dispose();}};
        Sync();InitializeVideoTools();InitializeEditing();SetBusy(false);
    }
    static void StyleButton(Button button,bool primary)
    {
        button.FlatStyle=FlatStyle.Flat;button.FlatAppearance.BorderSize=1;button.FlatAppearance.BorderColor=primary?Color.FromArgb(34,107,87):Color.FromArgb(210,218,225);
        button.BackColor=primary?Color.FromArgb(34,107,87):Color.White;button.ForeColor=primary?Color.White:Color.FromArgb(40,54,65);button.Padding=new Padding(8,4,8,4);
        if(primary&&!button.Enabled){button.BackColor=Color.FromArgb(229,234,237);button.ForeColor=Color.FromArgb(120,130,140);button.FlatAppearance.BorderColor=Color.FromArgb(210,218,225);}
    }
    void AddGroup(string effect,string title,string[] keys)
    {
        var group=new FlowLayoutPanel{Width=345,AutoSize=true,FlowDirection=FlowDirection.TopDown,WrapContents=false,Margin=new Padding(4,4,4,8)};
        var toggle=new CheckBox{Text=title,Font=new Font(Font,FontStyle.Bold),AutoSize=true};effects[effect]=toggle;group.Controls.Add(toggle);effectRows[effect]=new();
        foreach(var f in CrtSettings.Fields.Where(f=>keys.Contains(f.Key))){var row=CreateRow(f);group.Controls.Add(row);effectRows[effect].Add(row);}
        toggle.CheckedChanged+=(_,_)=>{if(refreshing)return;settings.Enable(effect,toggle.Checked);foreach(var row in effectRows[effect])row.Enabled=toggle.Checked;Changed();};controls.Controls.Add(group);
    }
    Control CreateRow(CrtField field)
    {
        bool percent=field.Key=="contentRect";decimal scale=percent?100:1;
        var row=new TableLayoutPanel{Width=330,Height=47,ColumnCount=2,RowCount=2,Margin=new Padding(0)};
        row.ColumnStyles.Add(new(SizeType.Percent,100));row.ColumnStyles.Add(new(SizeType.Absolute,95));row.RowStyles.Add(new(SizeType.Absolute,22));row.RowStyles.Add(new(SizeType.Absolute,25));
        var label=new Label{Text=field.Label,AutoSize=true};row.Controls.Add(label,0,0);
        var number=new NumericUpDown{Minimum=(decimal)field.Minimum*scale,Maximum=(decimal)field.Maximum*scale,DecimalPlaces=percent?2:field.Decimals,Increment=field.Decimals==0?1:(percent ? .1m : .01m),Dock=DockStyle.Fill};
        var slider=new TrackBar{Minimum=0,Maximum=1000,TickStyle=TickStyle.None,AutoSize=false,Dock=DockStyle.Fill,Height=25};row.Controls.Add(slider,0,1);row.Controls.Add(number,1,0);row.SetRowSpan(number,2);
        string hint=field.Key switch{
            "sourceSize"=>"Original game pixel count used by softening/bloom. 0 uses decoded picture pixels. Does not change output resolution.",
            "contentRect"=>"Percentage of the complete frame. Left/top are offsets; width/height are the selected picture's size. Surrounding pixels are preserved.",
            "scanlineFadeStart"=>"Distance from each scanline's centre where darkening begins (0 to 0.499). Lower values make the dark band wider.",
            "scanlineFadeEnd"=>"Distance where darkening reaches the chosen scanline strength. Must exceed fade start; 0.5 is the line edge.",
            "maskColumns" or "maskRows"=>"Density of the fine phosphor pattern. Judge at 100% view; fine patterns may alias when resized.",
            "curvatureScale"=>"Additional picture shrink, used only when curvature is enabled with a nonzero bend.",
            _=>field.Label+" — applied when its effect is enabled. Update the preview after changing it."
        };help.SetToolTip(label,hint);help.SetToolTip(number,hint);help.SetToolTip(slider,hint);
        bool linking=false;
        number.ValueChanged+=(_,_)=>{if(refreshing||linking)return;linking=true;double value=(double)(number.Value/scale);settings.Set(field,value);slider.Value=Math.Clamp((int)Math.Round((value-field.Minimum)/(field.Maximum-field.Minimum)*1000),0,1000);linking=false;Changed();};
        slider.ValueChanged+=(_,_)=>{if(refreshing||linking)return;double value=field.Minimum+(field.Maximum-field.Minimum)*slider.Value/1000;number.Value=Math.Clamp((decimal)Math.Round(value*(double)scale,number.DecimalPlaces),number.Minimum,number.Maximum);};
        fields.Add((field,number,slider));return row;
    }
    void Sync()
    {
        refreshing=true;
        foreach(var pair in effects){pair.Value.Checked=settings.Enabled(pair.Key);foreach(var row in effectRows[pair.Key])row.Enabled=pair.Value.Checked;}
        foreach(var (field,number,slider) in fields){double value=settings.Number(field);number.Value=(decimal)value*(field.Key=="contentRect"?100:1);slider.Value=Math.Clamp((int)Math.Round((value-field.Minimum)/(field.Maximum-field.Minimum)*1000),0,1000);}
        refreshing=false;
    }
    void ApplySettings(CrtSettings value){settings=value.Clone();Sync();filterChoice.SelectedIndex=1;toolTabs.SelectedTab=filtersPage;Changed();}
    void Changed(){if(refreshing)return;dirty=true;save.Enabled=false;export.Enabled=false;status.ForeColor=Color.FromArgb(76,86,99);status.Text=source==null?"Choose a source to preview this filter.":"Settings changed — press Update preview to apply.";}
    void Error(string text){status.ForeColor=Color.Firebrick;status.Text=text;help.SetToolTip(status,text);}
    void SetBusy(bool value){open.Enabled=!value;update.Enabled=!value&&source!=null;seconds.Enabled=!value&&source?.Still==false;controls.Enabled=!value;filterChoice.Enabled=!value;trimPanel.Enabled=!value&&source?.Still==false&&source.Duration>0;cancel.Enabled=value;if(!value)cancel.Text="Cancel";save.Enabled=!value&&!dirty&&processed!=null;export.Enabled=!value&&!dirty&&renderedSettings!=null&&source?.Still==false&&ValidSelection()&&ValidEditOptions();exportEncoder.Enabled=!value;SetEditingBusy(value);}
    public async Task<bool> OpenAsync(string path)
    {
        if(job!=null)return false;job=new();SetBusy(true);exportProgress.Visible=false;status.Text="Reading source…";
        bool ok=false;
        try{source=await CrtRenderer.InspectAsync(path,job.Token);file.Text=Path.GetFileName(source.Path);help.SetToolTip(file,source.Path);seconds.Value=0;seconds.Maximum=(decimal)(source.Duration>0?Math.Max(0,source.Duration-.001):86400);ResetRange();ResetEditingSource();if(source.Still)toolTabs.SelectedTab=filtersPage;picture.Image=null;raw?.Dispose();processed?.Dispose();raw=null;processed=null;dirty=true;ok=true;}
        catch(OperationCanceledException){status.Text="Source load cancelled.";}
        catch(Exception e){Error(e.Message);}
        finally{job.Dispose();job=null;SetBusy(false);}
        if(closeAfter){Close();return false;}
        return ok&&await UpdateAsync();
    }
    public async Task<bool> UpdateAsync()
    {
        if(job!=null||source==null)return false;
        CrtSettings snapshot;var selectedFilter=(VideoFilterKind)filterChoice.SelectedIndex;VideoEditOptions edit;
        try{snapshot=selectedFilter==VideoFilterKind.Crt?settings.Clone():new CrtSettings();snapshot.Validate();edit=ReadEditOptions();edit.Dimensions(source.Width,source.Height);}catch(Exception e){Error(e.Message);return false;}
        StopCropSelection();
        var selected=source;double time=(double)seconds.Value;
        job=new();SetBusy(true);exportProgress.Visible=false;status.ForeColor=Color.FromArgb(76,86,99);status.Text="Decoding selected frame…";
        string work=Path.Combine(Path.GetTempPath(),"CaptureAndRelease-CRT-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(work);
        Bitmap? nextRaw=null,nextProcessed=null;
        try{
            string input=Path.Combine(work,"source.png"),output=Path.Combine(work,"processed.png");
            await CrtRenderer.ExtractAsync(selected,time,input,job.Token);
            status.Text=selectedFilter==VideoFilterKind.Crt?"Rendering CRT with Vulkan…":"Applying picture edits…";await CrtRenderer.RenderEditedAsync(input,output,selected,snapshot,selectedFilter,edit,work,job.Token);
            job.Token.ThrowIfCancellationRequested();
            nextRaw=CrtRenderer.ReadBitmap(input);nextProcessed=CrtRenderer.ReadBitmap(output);
            picture.Image=null;raw?.Dispose();processed?.Dispose();raw=nextRaw;processed=nextProcessed;nextRaw=null;nextProcessed=null;
            renderedSettings=snapshot;renderedFilter=selectedFilter;dirty=false;Display();dimensions.Text=$"{processed.Width} × {processed.Height} output · {(selectedFilter==VideoFilterKind.Crt?"CRT":"Original / no effects")} · source {selected.Width} × {selected.Height}";
            status.Text=selected.Still?"Preview ready. Save a separate frame PNG.":"Preview ready. Picture edits are shown; audio changes apply on export.";status.ForeColor=Color.FromArgb(42,92,78);help.SetToolTip(status,"");if(!selected.Still){try{var range=Selection();edit.Dimensions(selected.Width,selected.Height,true);ReadEditOptions().Audio!.Validate(range.Duration,selected.AudioCodecs.Length);}catch(Exception e){Error(e.Message);}}return true;
        }catch(OperationCanceledException){status.Text="Preview cancelled. The previous completed preview is retained.";return false;}
        catch(Exception e){Error(e.Message);return false;}
        finally{nextRaw?.Dispose();nextProcessed?.Dispose();job.Dispose();job=null;SetBusy(false);try{Directory.Delete(work,true);}catch{}if(closeAfter)BeginInvoke((Action)Close);}
    }
    internal async Task<bool> ExportToAsync(string path)
    {
        if(job!=null||source==null||source.Still||dirty||renderedSettings==null)return false;
        var selected=source;var snapshot=renderedSettings.Clone();string encoder=exportEncoder.SelectedIndex==1?"h264_nvenc":"libx264";
        TrimSelection range;try{range=Selection();}catch(Exception e){Error(e.Message);return false;}var selectedFilter=renderedFilter;
        job=new();SetBusy(true);cancel.Text="Cancel export";exportProgress.Value=0;exportProgress.Visible=true;status.ForeColor=Color.FromArgb(76,86,99);status.Text="Exporting: "+range.Summary;
        var activeJob=job;
        var progress=new Progress<CrtExportProgress>(p=>{if(!ReferenceEquals(job,activeJob))return;exportProgress.Value=Math.Clamp((int)(p.Fraction*1000),0,1000);status.Text=$"{p.Message} {p.Fraction:P0}";});
        try{
            var result=await CrtExporter.ExportAsync(selected,path,snapshot,encoder,progress,job.Token,range,selectedFilter,ReadEditOptions(),autoRename:true);
            exportProgress.Value=1000;status.ForeColor=Color.FromArgb(42,92,78);
            string audio=result.AudioTracks==0?"no audio in selected section":result.AudioCopied?"audio copied unchanged":"audio converted to AAC";
            string renamed=Path.GetFullPath(path).Equals(result.Path,StringComparison.OrdinalIgnoreCase)?"":" · name numbered to avoid replacing a file";
            status.Text=$"Saved: {Path.GetFileName(result.Path)}{renamed} · {audio}";help.SetToolTip(status,result.Path);return true;
        }catch(OperationCanceledException){status.Text="Export cancelled. Original unchanged; unfinished export removed.";return false;}
        catch(Exception e){Error(e.Message);return false;}
        finally{job.Dispose();job=null;exportProgress.Visible=false;SetBusy(false);if(closeAfter)BeginInvoke((Action)Close);}
    }
    void Display()
    {
        var image=original.Checked||CropOverview?raw:processed;picture.Image=image;
        if(actual.Checked){var offset=viewport.AutoScrollPosition;picture.Dock=DockStyle.None;picture.SizeMode=PictureBoxSizeMode.Normal;picture.Size=image?.Size??Size.Empty;viewport.AutoScrollMinSize=image?.Size??Size.Empty;picture.Location=offset;}
        else{viewport.AutoScrollMinSize=Size.Empty;viewport.AutoScrollPosition=Point.Empty;picture.Dock=DockStyle.Fill;picture.SizeMode=PictureBoxSizeMode.Zoom;}
    }
    void SavePng()
    {
        if(processed==null||dirty||job!=null)return;
        using var picker=new SaveFileDialog{Filter="PNG image|*.png",InitialDirectory=Path.GetDirectoryName(source!.Path),FileName=Path.GetFileNameWithoutExtension(source.Path)+(renderedFilter==VideoFilterKind.Crt?"-CRT":"-frame")+".png",OverwritePrompt=false};
        if(picker.ShowDialog(this)!=DialogResult.OK)return;
        try{string path=SaveCompletedPreview(picker.FileName,autoRename:true);status.Text="Saved: "+Path.GetFileName(path)+(Path.GetFullPath(picker.FileName).Equals(path,StringComparison.OrdinalIgnoreCase)?"":" · name numbered to avoid replacing a file");help.SetToolTip(status,path);}catch(Exception e){Error(e.Message);}
    }
    internal string SaveCompletedPreview(string path,bool autoRename=false)
    {
        if(processed==null||dirty||job!=null)throw new InvalidOperationException("Update the preview before saving.");
        if(Path.GetExtension(path).ToLowerInvariant()!=".png")throw new IOException("Choose a PNG filename.");
        if(!autoRename&&source!=null&&Path.GetFullPath(path).Equals(source.Path,StringComparison.OrdinalIgnoreCase))throw new IOException("Choose a separate output file.");
        string requested=Path.GetFullPath(path);
        while(true){
            path=autoRename?OutputFiles.Available(requested,source==null?Array.Empty<string>():new[]{source.Path}):requested;bool created=false;
            try{using var stream=new FileStream(path,FileMode.CreateNew);created=true;processed.Save(stream,System.Drawing.Imaging.ImageFormat.Png);return path;}
            catch(IOException) when(autoRename&&!created&&(File.Exists(path)||Directory.Exists(path))){continue;}
            catch{if(created)try{File.Delete(path);}catch{}throw;}
        }
    }
}
