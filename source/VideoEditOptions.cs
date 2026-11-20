using System.Globalization;

namespace GameplayRecorder;

public enum VideoFilterKind { Original, Crt }

public enum ExportSize { Original, Fit1080, Fit720 }
public enum ExportQuality { High, Balanced, Smaller }

public sealed record PreviewSegment(double Start,double MaximumSeconds=10)
{
    public TrimSelection Within(TrimSelection selection)
    {
        if(!double.IsFinite(Start)||Start<0||Start>=selection.Duration||!double.IsFinite(MaximumSeconds)||MaximumSeconds<=0||MaximumSeconds>10)
            throw new ArgumentException("Choose a preview position inside the kept section, with a sample of up to 10 seconds.");
        return new(selection.Start+Start,Math.Min(selection.End,selection.Start+Start+MaximumSeconds));
    }
}

public sealed record CropSelection(int Left,int Top,int Width,int Height)
{
    public void Validate(int width,int height)
    {
        if(Left<0||Top<0||Width<2||Height<2||(long)Left+Width>width||(long)Top+Height>height)
            throw new ArgumentException("The crop must fit inside the source and keep at least 2 × 2 pixels.");
    }
    public bool IsWhole(int width,int height)=>Left==0&&Top==0&&Width==width&&Height==height;
}

public sealed record AddedAudioSource(string Path,double Duration,long FileLength,DateTime LastWriteUtc);
public sealed record AudioEdit(bool MuteOriginal=false,double OriginalVolume=1,AddedAudioSource? Added=null,
    double AddedVolume=.5,double Delay=0,double FadeIn=0,double FadeOut=0,int OriginalTrack=0)
{
    public void Validate(double duration,int tracks)
    {
        if(!double.IsFinite(OriginalVolume)||OriginalVolume<0||OriginalVolume>2||!double.IsFinite(AddedVolume)||AddedVolume<0||AddedVolume>2)
            throw new ArgumentException("Audio volume must be between 0% and 200%.");
        if(Added==null)return;
        if(tracks>0&&(OriginalTrack<0||OriginalTrack>=tracks))throw new ArgumentException("Choose an available original audio track.");
        if(!double.IsFinite(Delay)||Delay<0||Delay>=duration)throw new ArgumentException("Added audio must start before the saved clip ends.");
        double audible=Math.Min(Added.Duration,duration-Delay);
        if(!double.IsFinite(FadeIn)||!double.IsFinite(FadeOut)||FadeIn<0||FadeOut<0||FadeIn+FadeOut>audible+.000001)
            throw new ArgumentException("The added audio fades must fit within its audible length in the saved clip.");
    }
}

// Immutable job options: the UI can change later without changing an active render/export.
public sealed record VideoEditOptions(CropSelection? Crop=null,ExportSize Size=ExportSize.Original,
    ExportQuality Quality=ExportQuality.High,AudioEdit? Audio=null)
{
    public (int Width,int Height) Dimensions(int width,int height,bool requireEven=false)
    {
        Crop?.Validate(width,height);if(Crop!=null){width=Crop.Width;height=Crop.Height;}
        if(!Enum.IsDefined(Size)||!Enum.IsDefined(Quality))throw new ArgumentException("Choose a supported export preset.");
        if(Size!=ExportSize.Original){
            // Landscape and portrait use the same long/short-edge bounds. Never upscale.
            int longEdge=Size==ExportSize.Fit1080?1920:1280,shortEdge=Size==ExportSize.Fit1080?1080:720;
            double scale=Math.Min(1,Math.Min((double)longEdge/Math.Max(width,height),(double)shortEdge/Math.Min(width,height)));
            width=Math.Max(2,(int)Math.Floor(width*scale/2)*2);height=Math.Max(2,(int)Math.Floor(height*scale/2)*2);
        }
        if(requireEven&&(width%2!=0||height%2!=0))throw new ArgumentException("MP4 needs even output dimensions. Adjust the crop width/height or choose a size preset.");
        return(width,height);
    }
    public int QualityValue=>Quality switch{ExportQuality.High=>18,ExportQuality.Balanced=>23,_=>28};
    public string VisualFilter(int sourceWidth,int sourceHeight,bool crt)
    {
        var size=Dimensions(sourceWidth,sourceHeight);
        string chain="format=rgb24";
        if(Crop!=null&&!Crop.IsWhole(sourceWidth,sourceHeight))chain+=$",crop=w={Crop.Width}:h={Crop.Height}:x={Crop.Left}:y={Crop.Top}:exact=1";
        chain+=","+(crt?CrtRenderer.ShaderFilter:"format=gbrp,setparams=range=full:color_primaries=bt709:color_trc=iec61966-2-1:colorspace=gbr");
        int beforeW=Crop?.Width??sourceWidth,beforeH=Crop?.Height??sourceHeight;
        if(size.Width!=beforeW||size.Height!=beforeH)chain+=$",scale={size.Width}:{size.Height}:flags=lanczos:in_range=pc:out_range=pc,setsar=1";
        return chain;
    }
}

public sealed record TrimSelection(double Start,double End)
{
    public double Duration=>End-Start;
    public void Validate(double sourceDuration)
    {
        if(!double.IsFinite(Start)||!double.IsFinite(End)||Start<0||End<=Start||End>sourceDuration+.000001)
            throw new ArgumentException("Start must be before end, and both times must fit inside the video.");
    }
    public bool IsWhole(double duration)=>Start==0&&Math.Abs(End-duration)<.000001;
    public static string Time(double seconds)=>TimeSpan.FromSeconds(seconds).ToString(@"hh\:mm\:ss\.fff",CultureInfo.InvariantCulture);
    public string Summary=>$"{Time(Start)} → {Time(End)} · {Duration:0.###} s";
}
