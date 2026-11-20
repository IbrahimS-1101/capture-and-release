using System.Security.Cryptography;
using System.Text.Json;

namespace GameplayRecorder;

public sealed partial class CrtForm
{
    internal async Task RunSelfTestAsync(string[] args)
    {
        string Arg(string key)=>args[Array.IndexOf(args,key)+1];
        string output=Path.GetFullPath(Arg("--crt-self-test")),input=Path.GetFullPath(Arg("--input")),reference=Path.GetFullPath(Arg("--reference"));
        Directory.CreateDirectory(output);var report=new Dictionary<string,object>();
        string Hash(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        var hash=Hash(input);
        void Check(string key,bool ok){if(!ok)throw new Exception(key+": "+status.Text);report[key]=true;}
        bool Same(Bitmap a,Bitmap b){if(a.Size!=b.Size)return false;for(int y=0;y<a.Height;y++)for(int x=0;x<a.Width;x++)if(a.GetPixel(x,y).ToArgb()!=b.GetPixel(x,y).ToArgb())return false;return true;}
        try{
            Check("defaultsFlat",!effects["curvature"].Checked&&effects["scanlines"].Checked);
            Check("defaultsOriginalNoEffects",filterChoice.SelectedIndex==0&&!controls.Visible);
            string? vkDriver=Environment.GetEnvironmentVariable("VK_DRIVER_FILES"),vkIcd=Environment.GetEnvironmentVariable("VK_ICD_FILENAMES");
            try{Environment.SetEnvironmentVariable("VK_DRIVER_FILES",Path.Combine(output,"no-vulkan.json"));Environment.SetEnvironmentVariable("VK_ICD_FILENAMES",Path.Combine(output,"no-vulkan.json"));Check("originalImagePreviewWithoutVulkan",await OpenAsync(input));}
            finally{Environment.SetEnvironmentVariable("VK_DRIVER_FILES",vkDriver);Environment.SetEnvironmentVariable("VK_ICD_FILENAMES",vkIcd);}
            toolTabs.SelectedTab=filtersPage;filterChoice.SelectedIndex=1;PerformLayout();Application.DoEvents();
            foreach(var size in new[]{new Size(1220,850),new Size(1000,720)}){
                ClientSize=size;PerformLayout();Application.DoEvents();
                foreach(var c in new Control[]{open,update,cancel,seconds,save,export,exportEncoder,viewport,controls}){
                    if(!c.Visible)continue;
                    var bounds=RectangleToClient(c.RectangleToScreen(c.ClientRectangle));Check("layout"+size.Width+c.Text.Replace(' ','_'),bounds.Left>=0&&bounds.Top>=0&&bounds.Right<=ClientSize.Width&&bounds.Bottom<=ClientSize.Height);
                }
                Rectangle Bounds(Control c)=>RectangleToClient(c.RectangleToScreen(c.ClientRectangle));
                Check("updateBesideEditingTools"+size.Width,Bounds(update).Top>=Bounds(toolTabs).Bottom&&Bounds(update).Left>=Bounds(toolTabs).Left);
                Check("cancelBelowExportControls"+size.Width,Bounds(cancel).Top>=Bounds(export).Bottom);
            }
            ClientSize=new Size(1220,850);
            Check("sidebarHasNoHorizontalScroll",!controls.HorizontalScroll.Visible);
            var advanced=(CheckBox)controls.Controls.Find("advancedToggle",true).Single();advanced.Checked=true;PerformLayout();Application.DoEvents();
            Check("advancedHelpFitsSidebar",!controls.HorizontalScroll.Visible&&controls.Controls.Find("advancedDetails",true).Single().Visible);advanced.Checked=false;
            Check("imageLoadedAndRendered",await OpenAsync(input));
            Check("imageDisablesVideoExport",!export.Enabled);
            var referenceSettings=new CrtSettings();referenceSettings.Enable("curvature",true);ApplySettings(referenceSettings);
            Check("dirtyDisablesSave",dirty&&!save.Enabled);
            Check("curvedRendered",await UpdateAsync());
            using(var expected=CrtRenderer.ReadBitmap(reference))Check("matchesApprovedPythonPortExactly",Same(processed!,expected));
            Check("saveEnabledForCompletedPreview",save.Enabled&&!dirty);
            SaveCompletedPreview(Path.Combine(output,"saved-preview.png"));
            string numberedPng=SaveCompletedPreview(Path.Combine(output,"saved-preview.png"),autoRename:true);Check("pngNameConflictNumbered",Path.GetFileName(numberedPng)=="saved-preview (1).png"&&File.Exists(Path.Combine(output,"saved-preview.png")));
            try{SaveCompletedPreview(Path.Combine(output,"saved-preview.png"));throw new Exception("Overwrite accepted");}catch(IOException){report["existingOutputProtected"]=true;}
            try{SaveCompletedPreview(input);throw new Exception("Source overwrite accepted");}catch(IOException){report["sourceOverwriteProtected"]=true;}
            effects["mask"].Checked=false;
            Check("effectToggleDisablesItsControls",effectRows["mask"].All(c=>!c.Enabled)&&!settings.Enabled("mask"));
            effects["mask"].Checked=true;
            var strength=fields.First(f=>f.Field.Key=="scanlineStrength");strength.Number.Value=.6m;
            Check("numberAndSliderReachSettings",Math.Abs(settings.Number(strength.Field)-.6)<.00001&&strength.Slider.Value==600);
            strength.Slider.Value=250;
            Check("sliderReachesNumberAndSettings",strength.Number.Value==.25m&&Math.Abs(settings.Number(strength.Field)-.25)<.00001);
            var off=new CrtSettings();foreach(var effect in CrtSettings.Effects)off.Enable(effect,false);ApplySettings(off);
            Check("allOffRendered",await UpdateAsync());Check("allOffExactOriginalPixels",Same(raw!,processed!));
            actual.Checked=true;Check("actualSizeUsesOriginalPixels",picture.Size==processed!.Size&&picture.Dock==DockStyle.None);
            original.Checked=true;Check("originalViewUsesDecodedFrame",ReferenceEquals(picture.Image,raw));original.Checked=false;actual.Checked=false;
            ApplySettings(referenceSettings);Check("restoredSettingsRendered",await UpdateAsync());
            // Cancel the next owned decode/render job, keeping the completed preview.
            var before=processed;var task=UpdateAsync();job!.Cancel();bool completed=await task;
            Check("cancelRetainsPreviousPreview",!completed&&ReferenceEquals(processed,before)&&job==null);
            var invalid=referenceSettings.Json().Replace("\"scanlineFadeEnd\": 0.5","\"scanlineFadeEnd\": 0.1");
            try{CrtSettings.Parse(invalid);throw new Exception("Invalid fade order accepted");}catch(ArgumentException){report["invalidSettingsRejected"]=true;}
            File.WriteAllText(Path.Combine(output,"settings-roundtrip.json"),settings.Json());
            Check("settingsRoundTrip",CrtSettings.Parse(File.ReadAllText(Path.Combine(output,"settings-roundtrip.json"))).Hook()==settings.Hook());
            if(args.Contains("--video")){
                Check("videoLoaded",await OpenAsync(Arg("--video")));Check("videoTimeAvailable",seconds.Enabled&&seconds.Maximum>1);
                using var firstFrame=new Bitmap(raw!);
                seconds.Value=1.25m;Check("seekMarksPreviewDirty",dirty&&!save.Enabled);Check("selectedVideoFrameRendered",await UpdateAsync());
                Check("seekChangesDecodedFrame",!Same(firstFrame,raw!));
                SaveCompletedPreview(Path.Combine(output,"video-frame.png"));
                if(args.Contains("--test-export")){
                    Check("videoExportAvailableAfterPreview",export.Enabled);
                    string destination=Path.Combine(output,"ui-export.mp4");
                    var taskExport=ExportToAsync(destination);Check("exportLocksEditing",job!=null&&!controls.Enabled&&!exportEncoder.Enabled&&cancel.Enabled);
                    Check("cancelAlongsideProgress",exportProgress.Visible&&cancel.Text=="Cancel export"&&Math.Abs(cancel.PointToScreen(new Point(0,cancel.Height/2)).Y-exportProgress.PointToScreen(new Point(0,exportProgress.Height/2)).Y)<=3);
                    Check("videoExportCompletedFromUi",await taskExport);
                    var exported=await CrtRenderer.InspectAsync(destination,CancellationToken.None);
                    Check("exportsWholeVideoNotSeekPosition",Math.Abs(exported.Duration-source!.Duration)<.1&&exported.Duration>2.5);
                    Check("exportCompletionState",exportProgress.Value==1000&&status.Text.StartsWith("Saved:")&&controls.Enabled);
                    toolTabs.SelectedTab=trimPage;PerformLayout();Application.DoEvents();Check("trimTabFitsSidebar",!trimPanel.HorizontalScroll.Visible);Check("trimFieldsAvailableWithoutEnableSwitch",trimStart.Enabled&&trimEnd.Enabled);trimStart.Value=.515m;trimEnd.Value=2.41m;
                    Check("trimChangeKeepsCompletedPreview",!dirty&&export.Enabled);
                    string trimmed=Path.Combine(output,"ui-trimmed.mp4");Check("trimAndFilterExported",await ExportToAsync(trimmed));
                    var clipped=await CrtRenderer.InspectAsync(trimmed,CancellationToken.None);Check("trimmedDurationFromUi",Math.Abs(clipped.Duration-1.895)<.06);
                    trimEnd.Value=.1m;Check("invalidTrimDisablesExport",!export.Enabled);trimEnd.Value=2.41m;
                    await PreviewLastFrameAsync();Check("lastKeptFramePreview",(double)seconds.Value<2.41&&(double)seconds.Value>2.3);
                    decimal currentPreviewTime=seconds.Value;useFullVideo.PerformClick();Check("fullVideoResetPreservesPreviewTime",Selection().IsWhole(source!.Duration)&&seconds.Value==currentPreviewTime&&export.Text=="Export video…");filterChoice.SelectedIndex=0;Check("switchingToOriginalRequiresPreview",dirty&&!export.Enabled);
                    Check("originalPreviewRendered",await UpdateAsync());Check("originalPreviewExactPixels",Same(raw!,processed!));
                    filterChoice.SelectedIndex=1;await UpdateAsync();toolTabs.SelectedTab=filtersPage;
                    var field=fields.First(f=>f.Field.Key=="bloomStrength");field.Number.Value=.4m;
                    Check("dirtySettingsDisableVideoExport",!export.Enabled);field.Number.Value=.35m;await UpdateAsync();
                }
                Check("imageReloadAfterVideo",await OpenAsync(input));
            }
            if(args.Contains("--hdr")){
                try{await CrtRenderer.InspectAsync(Arg("--hdr"),CancellationToken.None);throw new Exception("HDR was accepted");}catch(IOException){report["hdrRejectedExplicitly"]=true;}
            }
            var retained=processed;string invalidFile=Path.Combine(output,"invalid.mp4");await File.WriteAllTextAsync(invalidFile,"not a media file");
            Check("invalidSourceKeepsCompletedPreview",!await OpenAsync(invalidFile)&&ReferenceEquals(processed,retained));
            if(args.Contains("--test-export")&&args.Contains("--video")){
                await OpenAsync(Arg("--video"));
            }
            if(args.Contains("--test-export")&&args.Contains("--cancel-video")){
                Check("cancelVideoOpened",await OpenAsync(Arg("--cancel-video")));
                string cancelOutput=Path.Combine(output,"ui-cancelled.mp4");var cancelTask=ExportToAsync(cancelOutput);await Task.Delay(200);job?.Cancel();
                Check("uiExportCancellationRestoresControls",!await cancelTask&&job==null&&controls.Enabled&&!File.Exists(cancelOutput));
            }
            if(args.Contains("--display-video")){
                string retainedLook=settings.Json();
                Check("displayClipOpened",await OpenAsync(Arg("--display-video")));
                Check("newSourceDoesNotAutoChangeCrt",settings.Json()==retainedLook);
                using(var expected=CrtRenderer.ReadBitmap(reference))Check("losslessClipPreviewMatchesApprovedFrame",Same(processed!,expected));
                string finalVideo=Path.Combine(output,"doom-CRT.mp4");Check("doomClipExported",await ExportToAsync(finalVideo));
                string frame=Path.Combine(output,"doom-export-frame.png");await CrtRenderer.ExtractAsync(await CrtRenderer.InspectAsync(finalVideo,CancellationToken.None),0,frame,CancellationToken.None);
                using var exported=CrtRenderer.ReadBitmap(frame);double total=0;
                for(int y=0;y<exported.Height;y++)for(int x=0;x<exported.Width;x++){var a=exported.GetPixel(x,y);var b=processed!.GetPixel(x,y);total+=Math.Abs(a.R-b.R)+Math.Abs(a.G-b.G)+Math.Abs(a.B-b.B);}
                double mean=total/(exported.Width*exported.Height*3);report["encodedFrameMeanDifferenceFromPreview"]=mean;
                Check("encodedFrameVisuallyTracksPreview",mean<8);
            }
            if(args.Contains("--added-audio")&&source?.Still==false){
                toolTabs.SelectedTab=cropPage;PerformLayout();Application.DoEvents();pickCrop.PerformClick();
                Check("cropSelectionUsesFullOriginal",cropPicking&&ReferenceEquals(picture.Image,raw)&&!actual.Checked&&!original.Enabled&&!actual.Enabled);
                var bounds=PictureBounds();Point a=new(bounds.Left+bounds.Width/4,bounds.Top+bounds.Height/4),b=new(bounds.Left+bounds.Width*3/4,bounds.Top+bounds.Height*3/4);
                void Mouse(string method,Point p)=>typeof(Control).GetMethod(method,System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.Invoke(picture,new object[]{new MouseEventArgs(MouseButtons.Left,1,p.X,p.Y,0)});
                Mouse("OnMouseDown",a);Mouse("OnMouseMove",b);Mouse("OnMouseUp",b);
                Check("dragCropUpdatesFields",!cropPicking&&!original.Checked&&!original.Enabled&&actual.Enabled&&cropWidth.Value>=source.Width/2-2&&cropWidth.Value<=source.Width/2+2&&dirty&&!export.Enabled);
                Check("cropOutlineRemainsOnSource",CropOverview&&ReferenceEquals(picture.Image,raw));
                bool comparison=original.Checked;pickCrop.PerformClick();pickCrop.PerformClick();Check("cancelCropKeepsComparisonChoice",!cropPicking&&original.Checked==comparison&&ReferenceEquals(picture.Image,raw));
                SetCrop(new(13,15,400,300));Check("croppedPreviewRendered",await UpdateAsync());Check("previewShowsCropAndRawRetained",processed!.Width==400&&processed.Height==300&&raw!.Width==source.Width);
                Check("updatedCropKeepsOutlineView",ReferenceEquals(picture.Image,raw));toolTabs.SelectedTab=filtersPage;Check("otherTabsShowProcessedResult",ReferenceEquals(picture.Image,processed));
                var match=(Button)controls.Controls.Find("matchPicturePreset",true).Single();match.PerformClick();Check("picturePresetIsExplicitAndDirty",dirty&&settings.Json()==CrtPresets.MatchPicture(400,300,true).Json());ApplySettings(referenceSettings);await UpdateAsync();
                toolTabs.SelectedTab=audioPage;Check("addedAudioOpened",await OpenAddedAudioAsync(Arg("--added-audio")));originalVolume.Value=50;audioDelay.Value=.2m;fadeIn.Value=.1m;fadeOut.Value=.1m;
                Check("audioChangesDoNotDirtyPicture",!dirty&&export.Enabled&&addedAudio!=null);
                fadeIn.Value=60;Check("invalidAudioDisablesExport",!export.Enabled);fadeIn.Value=.1m;
                muteOriginal.Checked=true;Check("muteDisablesOriginalLevel",!originalVolume.Enabled);muteOriginal.Checked=false;
                toolTabs.SelectedTab=exportPage;qualityPreset.SelectedIndex=1;sizePreset.SelectedIndex=2;Check("sizeChangeRequiresUpdatedPreview",dirty&&!export.Enabled);Check("sizePreviewUpdated",await UpdateAsync());
                string combined=Path.Combine(output,"ui-combined.mp4");Check("combinedUiExport",await ExportToAsync(combined));var info=await CrtRenderer.InspectAsync(combined,CancellationToken.None);Check("combinedUiDimensionsAndAudio",info.Width==400&&info.Height==300&&info.AudioCodecs.Length==1);
                foreach(var tab in new[]{cropPage,audioPage,exportPage}){
                    toolTabs.SelectedTab=tab;PerformLayout();Application.DoEvents();
                    Check(tab.Text+"NoHorizontalScroll",!((FlowLayoutPanel)tab.Controls[0]).HorizontalScroll.Visible);
                    using var screenshot=new Bitmap(Width,Height);DrawToBitmap(screenshot,new Rectangle(0,0,Width,Height));screenshot.Save(Path.Combine(output,tab.Text.ToLowerInvariant()+"-window.png"));
                }
                Size=MinimumSize;PerformLayout();Application.DoEvents();
                foreach(var tab in new[]{trimPage,cropPage,filtersPage,audioPage,exportPage}){
                    toolTabs.SelectedTab=tab;PerformLayout();Application.DoEvents();
                    foreach(var button in new[]{update,cancel,export,samplePreview}){var rect=RectangleToClient(button.RectangleToScreen(button.ClientRectangle));Check("actualMinimum"+tab.Text+button.Text,rect.Left>=0&&rect.Top>=0&&rect.Right<=ClientSize.Width&&rect.Bottom<=ClientSize.Height);}
                }
                ClientSize=new Size(1220,850);
                if(args.Contains("--test-playback")){
                    toolTabs.SelectedTab=audioPage;seconds.Value=0;Check("inAppSamplePlayback",await PlaySampleAsync(testPlayback:true,testCapture:Path.Combine(output,"sample-player.png")));Check("sampleRestoresEditing",job==null&&samplePreview.Enabled&&controls.Enabled);
                    var cancelledSample=PlaySampleAsync();job?.Cancel();Check("sampleCancellationRestoresEditing",!await cancelledSample&&job==null&&samplePreview.Enabled&&controls.Enabled);
                }
                toolTabs.SelectedTab=audioPage;removeAudio.PerformClick();Check("removeAddedAudio",addedAudio==null&&export.Enabled);
                toolTabs.SelectedTab=cropPage;resetCrop.PerformClick();Check("fullPictureReset",ReadEditOptions().Crop!.IsWhole(source.Width,source.Height));
                ResetEditingSource();Check("sourceEditReset",addedAudio==null&&originalVolume.Value==100&&sizePreset.SelectedIndex==0&&qualityPreset.SelectedIndex==0);await UpdateAsync();
            }
            status.ForeColor=Color.FromArgb(42,92,78);status.Text="Preview ready. Trim selects the range; Filters & effects selects its look.";
            Check("sourceFileUnchanged",Hash(input)==hash);
            using(var bitmap=new Bitmap(Width,Height)){DrawToBitmap(bitmap,new Rectangle(0,0,Width,Height));bitmap.Save(Path.Combine(output,"crt-window.png"));}
            if(source?.Still==false){toolTabs.SelectedTab=trimPage;trimStart.Value=.5m;trimEnd.Value=Math.Min(2.4m,trimEnd.Maximum);PerformLayout();Application.DoEvents();using var bitmap=new Bitmap(Width,Height);DrawToBitmap(bitmap,new Rectangle(0,0,Width,Height));bitmap.Save(Path.Combine(output,"trim-window.png"));}
            report["success"]=true;
        }catch(Exception e){report["success"]=false;report["error"]=e.ToString();Environment.ExitCode=1;}
        finally{await File.WriteAllTextAsync(Path.Combine(output,"crt-ui-report.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));Close();}
    }
}
