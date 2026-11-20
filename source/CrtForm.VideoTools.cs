namespace GameplayRecorder;

public sealed partial class CrtForm
{
    readonly TabControl toolTabs=new(){Dock=DockStyle.Fill};
    readonly TabPage trimPage=new("Trim"),filtersPage=new("Filters & effects");
    readonly ComboBox filterChoice=new(){DropDownStyle=ComboBoxStyle.DropDownList,Dock=DockStyle.Fill};
    readonly Label noEffect=new(){Dock=DockStyle.Fill,Text="Original / no effects\n\nSelect CRT above to adjust its look.",TextAlign=ContentAlignment.MiddleCenter,ForeColor=Color.DimGray};
    readonly FlowLayoutPanel trimPanel=new(){Dock=DockStyle.Fill,AutoScroll=true,FlowDirection=FlowDirection.TopDown,WrapContents=false};
    readonly Button useFullVideo=new(){Text="Use full video",AutoSize=true};
    readonly NumericUpDown trimStart=new(){DecimalPlaces=3,Maximum=86400,Width=140,Increment=.1m},trimEnd=new(){DecimalPlaces=3,Maximum=86400,Width=140,Increment=.1m};
    readonly TrackBar playhead=new(){Width=340,Maximum=10000,TickStyle=TickStyle.None};
    readonly Label rangeSummary=new(){AutoSize=true,MaximumSize=new Size(340,0)};
    readonly FlowLayoutPanel rangeControls=new(){Width=350,AutoSize=true,FlowDirection=FlowDirection.TopDown,WrapContents=false};
    VideoFilterKind renderedFilter=VideoFilterKind.Original;
    bool resettingRange,seeking;
    bool HasTrim=>source?.Still==false&&((double)trimStart.Value!=0||Math.Abs((double)trimEnd.Value-source.Duration)>.000001);

    void InitializeVideoTools()
    {
        toolTabs.TabPages.AddRange(new[]{trimPage,filtersPage});trimPage.Padding=new Padding(8);filtersPage.Padding=new Padding(2);
        var filterLayout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=2};filterLayout.RowStyles.Add(new(SizeType.Absolute,78));filterLayout.RowStyles.Add(new(SizeType.Percent,100));
        var choicePanel=new TableLayoutPanel{Dock=DockStyle.Fill,RowCount=3,ColumnCount=1,Margin=new Padding(0)};
        choicePanel.RowStyles.Add(new(SizeType.Absolute,22));choicePanel.RowStyles.Add(new(SizeType.Absolute,30));choicePanel.RowStyles.Add(new(SizeType.Absolute,24));
        choicePanel.Controls.Add(new Label{Text="Choose a filter",AutoSize=true},0,0);
        filterChoice.Items.AddRange(new object[]{"Original / no effects","CRT"});filterChoice.SelectedIndex=0;choicePanel.Controls.Add(filterChoice,0,1);
        choicePanel.Controls.Add(new Label{Text="CRT settings stay separate from trimming.",AutoSize=true,ForeColor=Color.DimGray},0,2);
        var content=new Panel{Dock=DockStyle.Fill};content.Controls.Add(controls);content.Controls.Add(noEffect);controls.Visible=false;noEffect.BringToFront();
        filterLayout.Controls.Add(choicePanel,0,0);filterLayout.Controls.Add(content,0,1);filtersPage.Controls.Add(filterLayout);
        filterChoice.SelectedIndexChanged+=(_,_)=>{controls.Visible=filterChoice.SelectedIndex==1;noEffect.Visible=filterChoice.SelectedIndex==0;if(controls.Visible)controls.BringToFront();Changed();};
        trimPage.Controls.Add(trimPanel);
        void Explain(string text)=>trimPanel.Controls.Add(new Label{Text=text,MaximumSize=new Size(340,0),AutoSize=true,Margin=new Padding(3,8,3,8),ForeColor=Color.FromArgb(76,86,99)});
        Explain("Move the preview position to inspect frames. Release the slider to update the preview.");trimPanel.Controls.Add(playhead);
        Explain("The full video is selected by default. Change start or end to trim; use the reset below to keep everything again.");
        Control TimeRow(string label,NumericUpDown number){var row=new FlowLayoutPanel{Width=340,Height=35,WrapContents=false};row.Controls.Add(new Label{Text=label,Width=168,Padding=new Padding(0,5,0,0)});row.Controls.Add(number);return row;}
        rangeControls.Controls.Add(TimeRow("Start (seconds)",trimStart));rangeControls.Controls.Add(TimeRow("End (seconds, excluded)",trimEnd));
        Button markStart=new(){Text="Set start from preview",AutoSize=true},markEnd=new(){Text="Set end from preview",AutoSize=true};
        Button jumpStart=new(){Text="Preview start",AutoSize=true},jumpEnd=new(){Text="Last kept frame",AutoSize=true};
        foreach(var button in new[]{markStart,markEnd,jumpStart,jumpEnd,useFullVideo})StyleButton(button,false);
        var marks=new FlowLayoutPanel{Width=340,Height=47,WrapContents=false};marks.Controls.AddRange(new Control[]{markStart,markEnd});rangeControls.Controls.Add(marks);
        var jumps=new FlowLayoutPanel{Width=340,Height=47,WrapContents=false};jumps.Controls.AddRange(new Control[]{jumpStart,jumpEnd});rangeControls.Controls.Add(jumps);
        rangeControls.Controls.Add(useFullVideo);useFullVideo.Click+=(_,_)=>ResetRange(resetPosition:false);
        help.SetToolTip(useFullVideo,"Resets start/end to the full video without changing the preview position or filter.");
        trimPanel.Controls.Add(rangeControls);trimPanel.Controls.Add(rangeSummary);
        Explain("Start is included; end is excluded. Video cuts follow decoded frames, so boundaries are limited by the source frame times. Actual cuts also trim audio to the requested interval and encode it to AAC.");
        Explain("Export creates a separate MP4. Combine a trim and CRT in one export, or keep Original / no effects for a plain clip.");
        markStart.Click+=(_,_)=>trimStart.Value=Math.Min(seconds.Value,trimStart.Maximum);markEnd.Click+=(_,_)=>trimEnd.Value=Math.Min(seconds.Value,trimEnd.Maximum);
        jumpStart.Click+=async(_,_)=>{seconds.Value=Math.Min(trimStart.Value,seconds.Maximum);await UpdateAsync();};jumpEnd.Click+=async(_,_)=>await PreviewLastFrameAsync();
        trimStart.ValueChanged+=(_,_)=>RangeChanged();trimEnd.ValueChanged+=(_,_)=>RangeChanged();
        playhead.ValueChanged+=(_,_)=>{if(seeking||resettingRange||source==null||source.Still)return;seeking=true;seconds.Value=Math.Min(seconds.Maximum,(decimal)(source.Duration*playhead.Value/playhead.Maximum));seeking=false;};
        async Task SliderPreview(){if(job!=null||source==null)return;if(playhead.Value==playhead.Maximum)await PreviewLastFrameAsync(whole:true);else await UpdateAsync();}
        playhead.MouseUp+=async(_,_)=>await SliderPreview();playhead.KeyUp+=async(_,e)=>{if(e.KeyCode is Keys.Left or Keys.Right or Keys.Home or Keys.End or Keys.PageDown or Keys.PageUp)await SliderPreview();};
        seconds.ValueChanged+=(_,_)=>{if(seeking||resettingRange||source==null||source.Still||source.Duration<=0)return;seeking=true;playhead.Value=Math.Clamp((int)((double)seconds.Value/source.Duration*playhead.Maximum),0,playhead.Maximum);seeking=false;};
        help.SetToolTip(trimEnd,"End is the first time excluded from the clip. It must be after Start.");
        help.SetToolTip(markEnd,"Sets the exclusive end time from the current preview position.");
        RangeChanged();
    }
    TrimSelection Selection()
    {
        if(source==null||source.Still||source.Duration<=0)throw new ArgumentException("Open a completed video to select a range.");
        var value=new TrimSelection((double)trimStart.Value,(double)trimEnd.Value);value.Validate(source.Duration);return value;
    }
    bool ValidSelection(){try{Selection();return true;}catch{return false;}}
    void ResetRange(bool resetPosition=true)
    {
        resettingRange=true;trimStart.Value=0;
        decimal duration=(decimal)Math.Max(0,source?.Duration??0);trimStart.Maximum=duration;trimEnd.Maximum=duration;trimEnd.Value=duration;if(resetPosition)playhead.Value=0;
        resettingRange=false;RangeChanged();
    }
    void RangeChanged()
    {
        if(resettingRange)return;
        try{var r=Selection();rangeSummary.ForeColor=Color.FromArgb(42,92,78);rangeSummary.Text=HasTrim?"Keep: "+r.Summary:$"Entire video · {TrimSelection.Time(r.Duration)}";if(job==null){status.ForeColor=dirty?Color.FromArgb(76,86,99):Color.FromArgb(42,92,78);status.Text=dirty?"Range ready — update the preview before exporting.":"Export keeps: "+r.Summary;help.SetToolTip(status,"");}}
        catch(Exception e){rangeSummary.ForeColor=Color.Firebrick;rangeSummary.Text=source?.Still==false?e.Message:"Trimming is available for videos.";if(job==null&&source?.Still==false)Error(e.Message);}
        export.Text=HasTrim?"Export selection…":"Export video…";
        RefreshEditSummary();
        if(job==null)SetBusy(false);
    }
    async Task PreviewLastFrameAsync(bool whole=false)
    {
        if(source==null||source.Still||job!=null)return;
        job=new();SetBusy(true);status.Text="Finding the last kept frame…";double? time=null;
        try{var range=whole?new TrimSelection(0,source.Duration):Selection();time=await CrtRenderer.LastFrameTimeAsync(source,range,job.Token);}
        catch(OperationCanceledException){status.Text="Frame lookup cancelled.";}catch(Exception e){Error(e.Message);}
        finally{job.Dispose();job=null;SetBusy(false);}
        if(closeAfter){Close();return;}if(time!=null){seconds.Value=Math.Clamp((decimal)time.Value,0,seconds.Maximum);await UpdateAsync();}
    }
}
