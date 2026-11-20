namespace GameplayRecorder;

public static class CrtPresets
{
    public static string Aspect(int width,int height)
    {
        double a=(double)width/height;
        if(Math.Abs(a-1)<.015)return "square";
        if(Math.Abs(a-4d/3)<.025)return "4:3";
        if(Math.Abs(a-16d/9)<.025)return "16:9";
        if(Math.Abs(a-9d/16)<.025)return "9:16";
        if(Math.Abs(a-3d/4)<.025)return "3:4";
        return width>height?"wide":"portrait";
    }
    // A mild starting look, not recovery of the original game's logical resolution.
    public static CrtSettings MatchPicture(int width,int height,bool curvature)
    {
        if(width<2||height<2)throw new ArgumentException("Open a picture at least 2 × 2 pixels.");
        var settings=new CrtSettings();settings.Enable("curvature",curvature);
        foreach(var field in CrtSettings.Fields){
            double? value=field.Key switch{
                "scanlineCount"=>Math.Clamp(height/3d,1,1200),
                "maskColumns"=>Math.Clamp(width/2d,3,2400),
                "maskRows"=>Math.Clamp(height/3d,1,1200),
                "scanlineStrength"=>.18,"maskStrength"=>.45,"bloomStrength"=>.2,
                "softeningStrength"=>.15,
                "curvatureAmount"=>.014*Math.Min(1,field.Index==0?(double)height/width:(double)width/height),
                "curvatureScale"=>1.012,_=>null};
            if(value!=null)settings.Set(field,Math.Round(value.Value,field.Decimals));
        }
        settings.Validate();return settings;
    }
}
