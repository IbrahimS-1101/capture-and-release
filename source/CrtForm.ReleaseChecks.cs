using System.Text.Json;
namespace GameplayRecorder;

public sealed partial class CrtForm
{
    internal async Task RunReleaseChecksAsync(string[] args)
    {
        string Arg(string key)=>args[Array.IndexOf(args,key)+1];string output=Path.GetFullPath(Arg("--release-checks"));Directory.CreateDirectory(output);var report=new Dictionary<string,object>();
        void Check(string name,bool ok){if(!ok)throw new Exception(name+": "+status.Text);report[name]=true;}
        IEnumerable<Control> Children(Control parent){foreach(Control child in parent.Controls){yield return child;foreach(var nested in Children(child))yield return nested;}}
        void Mouse(string name,Point p)=>typeof(Control).GetMethod(name,System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.Invoke(picture,new object[]{new MouseEventArgs(MouseButtons.Left,1,p.X,p.Y,0)});
        Point At(int x,int y){var rect=PictureBounds();return new(rect.Left+(int)Math.Round(x*(double)rect.Width/raw!.Width),rect.Top+(int)Math.Round(y*(double)rect.Height/raw.Height));}
        void Drag(Point start,Point finish){Mouse("OnMouseDown",start);Mouse("OnMouseMove",finish);Mouse("OnMouseUp",finish);}
        try{
            Check("sourceOpened",await OpenAsync(Arg("--video")));toolTabs.SelectedTab=cropPage;PerformLayout();Application.DoEvents();
            var portrait=Children(cropPage).OfType<Button>().Single(b=>b.Text=="9:16");portrait.PerformClick();var selected=ReadEditOptions().Crop!;
            int cx=selected.Left+selected.Width/2,cy=selected.Top+selected.Height/2;
            Drag(At(cx,cy),At(cx+90,cy));var moved=ReadEditOptions().Crop!;
            Check("portraitMovesWithoutResizing",Math.Abs(moved.Left-selected.Left-90)<=1&&moved.Top==selected.Top&&moved.Width==selected.Width&&moved.Height==selected.Height&&!cropPicking);
            Drag(At(moved.Left+moved.Width/2,cy),At(source!.Width+500,cy));var right=ReadEditOptions().Crop!;
            Check("moveClampsToRightEdge",right.Left+right.Width==source.Width&&right.Width==selected.Width&&right.Height==selected.Height);
            Drag(At(right.Left+right.Width/2,cy),At(-500,cy));var left=ReadEditOptions().Crop!;Check("moveClampsToLeftEdge",left.Left==0&&left.Width==selected.Width);
            var before=ReadEditOptions().Crop;Drag(At(source.Width-2,cy),At(source.Width-80,cy));Check("outsideClickDoesNotRedraw",ReadEditOptions().Crop==before);
            Children(cropPage).OfType<Button>().Single(b=>b.Text=="16:9").PerformClick();var wide=ReadEditOptions().Crop!;
            Drag(At(wide.Left+wide.Width/2,wide.Top+wide.Height/2),At(wide.Left+wide.Width/2,source.Height+500));var bottom=ReadEditOptions().Crop!;Check("landscapeMovesAndClampsBottom",bottom.Top+bottom.Height==source.Height&&bottom.Width==wide.Width&&bottom.Height==wide.Height);
            actual.Checked=true;SetCrop(new(80,60,200,160));Drag(At(180,140),At(230,170));var actualMove=ReadEditOptions().Crop!;Check("moveInActualSizeView",actualMove.Left==130&&actualMove.Top==90&&actualMove.Width==200&&actualMove.Height==160);actual.Checked=false;
            pickCrop.PerformClick();Drag(At(70,80),At(290,250));var drawn=ReadEditOptions().Crop!;Check("explicitDrawingStillWorks",Math.Abs(drawn.Left-70)<=1&&Math.Abs(drawn.Top-80)<=1&&Math.Abs(drawn.Width-220)<=2&&Math.Abs(drawn.Height-170)<=2);
            Check("movedCropPreview",await UpdateAsync());string video=Path.Combine(output,"moved-crop.mp4");Check("movedCropExport",await ExportToAsync(video));var exported=await CrtRenderer.InspectAsync(video,CancellationToken.None);Check("movedCropExportDimensions",exported.Width==drawn.Width&&exported.Height==drawn.Height);
            using(var bitmap=new Bitmap(Width,Height)){DrawToBitmap(bitmap,new Rectangle(0,0,Width,Height));bitmap.Save(Path.Combine(output,"crop-move.png"));}
            using(var about=new AboutForm()){
                about.Show(this);Application.DoEvents();
                foreach(var size in new[]{new Size(640,720),new Size(580,580)}){
                    about.ClientSize=size;about.PerformLayout();Application.DoEvents();
                    foreach(var link in Children(about).OfType<LinkLabel>()){
                        var rect=about.RectangleToClient(link.RectangleToScreen(link.ClientRectangle));Check("aboutLinkFits"+size.Width+link.Name,rect.Left>=0&&rect.Right<=about.ClientSize.Width&&rect.Top>=0&&rect.Bottom<=about.ClientSize.Height);
                    }
                }
                Check("coffeeDestination",(string)Children(about).OfType<LinkLabel>().Single(l=>l.Name=="coffeeLink").Links[0].LinkData! == "https://buymeacoffee.com/isamir");
                Check("xDestination",(string)Children(about).OfType<LinkLabel>().Single(l=>l.Name=="xLink").Links[0].LinkData! == "https://x.com/isamirDM");
                Check("supportOptionalAndFree",AboutForm.SupportText.Contains("free to use")&&AboutForm.SupportText.Contains("anywhere")&&AboutForm.SupportText.Contains("entirely optional")&&AboutForm.Credits.Contains("GPL-3.0-only"));
                about.ClientSize=new Size(640,720);about.PerformLayout();Application.DoEvents();using(var bitmap=new Bitmap(about.Width,about.Height)){about.DrawToBitmap(bitmap,new Rectangle(0,0,bitmap.Width,bitmap.Height));bitmap.Save(Path.Combine(output,"about-support.png"));}about.Close();
            }
            report["success"]=true;
        }catch(Exception e){report["success"]=false;report["error"]=e.ToString();Environment.ExitCode=1;}
        finally{await File.WriteAllTextAsync(Path.Combine(output,"release-checks.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));Close();}
    }
}
