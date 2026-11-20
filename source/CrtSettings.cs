using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace GameplayRecorder;

public record CrtField(string Key,string Label,double Minimum,double Maximum,int Decimals,int Index=-1);

public sealed class CrtSettings
{
    public static readonly CrtField[] Fields={
        new("sourceSize","Game width (0 = actual)",0,8192,0,0),new("sourceSize","Game height (0 = actual)",0,8192,0,1),
        new("contentRect","Picture left (%)",0,1,4,0),new("contentRect","Picture top (%)",0,1,4,1),
        new("contentRect","Picture width (%)",0,1,4,2),new("contentRect","Picture height (%)",0,1,4,3),
        new("curvatureAmount","Horizontal bend",0,.2,4,0),new("curvatureAmount","Vertical bend",0,.2,4,1),new("curvatureScale","Picture shrink",1,1.2,4),
        new("softeningRadius","Radius (game pixels)",0,2,3),new("softeningStrength","Strength",0,1,3),
        new("scanlineCount","Raster lines",1,1200,0),new("scanlineStrength","Strength",0,1,3),
        new("scanlineFadeStart","Fade starts at",0,.499,4),new("scanlineFadeEnd","Full strength at",0,.5,4),
        new("maskColumns","RGB stripes across",3,2400,0),new("maskRows","Staggered rows",1,1200,0),new("maskStrength","Strength",0,1,3),
        new("bloomRadius","Horizontal radius",0,8,3,0),new("bloomRadius","Vertical radius",0,8,3,1),new("bloomStrength","Strength",0,1,3)
    };
    public static readonly string[] Effects={"curvature","softening","scanlines","mask","bloom"};
    readonly JsonObject values;
    public CrtSettings():this(JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"shaders","defaults.json")))!.AsObject()){}
    CrtSettings(JsonObject values){this.values=values;Validate();}
    public static CrtSettings Parse(string json)=>new(JsonNode.Parse(json)!.AsObject());
    public CrtSettings Clone()=>new((JsonObject)values.DeepClone());
    public string Json()=>values.ToJsonString(new System.Text.Json.JsonSerializerOptions{WriteIndented=true});
    public bool Enabled(string effect)=>values[effect+"Enabled"]!.GetValue<bool>();
    public void Enable(string effect,bool value)=>values[effect+"Enabled"]=value;
    public double Number(CrtField f)=>f.Index<0?values[f.Key]!.GetValue<double>():values[f.Key]![f.Index]!.GetValue<double>();
    public void Set(CrtField f,double value){if(f.Index<0)values[f.Key]=value;else values[f.Key]![f.Index]=value;}
    public void Validate()
    {
        var expected=Fields.Select(f=>f.Key).Concat(Effects.Select(e=>e+"Enabled")).ToHashSet();
        if(!expected.SetEquals(values.Select(v=>v.Key)))throw new ArgumentException("Settings contain missing or unknown fields.");
        foreach(var e in Effects)if(values[e+"Enabled"] is not JsonValue v || !v.TryGetValue<bool>(out _))throw new ArgumentException("Effect switches must be true or false.");
        foreach(var key in new[]{"sourceSize","contentRect","curvatureAmount","bloomRadius"})
            if(values[key] is not JsonArray a || a.Count!=(key=="contentRect"?4:2))throw new ArgumentException("Invalid vector: "+key);
        foreach(var f in Fields){double n=Number(f);if(!double.IsFinite(n)||n<f.Minimum||n>f.Maximum)throw new ArgumentException("Out-of-range value: "+f.Label);}
        double R(int i)=>values["contentRect"]![i]!.GetValue<double>();
        if(R(2)<=0||R(3)<=0||R(0)+R(2)>1.00000001||R(1)+R(3)>1.00000001)throw new ArgumentException("Picture area must have positive size and fit inside the frame.");
        if(values["scanlineFadeEnd"]!.GetValue<double>()<=values["scanlineFadeStart"]!.GetValue<double>())throw new ArgumentException("Scanline fade end must be greater than fade start.");
    }
    static string Float(double v){string s=v.ToString("R",CultureInfo.InvariantCulture);return s.Contains('.')||s.Contains('E')?s:s+".0";}
    public string Hook()
    {
        Validate();var lines=new List<string>{"//!HOOK MAIN","//!BIND HOOKED","//!DESC Capture and Release CRT","#define inputSize HOOKED_size"};
        foreach(var pair in values){
            if(pair.Value is JsonArray a)lines.Add($"const vec{a.Count} {pair.Key} = vec{a.Count}({string.Join(",",a.Select(v=>Float(v!.GetValue<double>()))) });");
            else if(pair.Value is JsonValue v && v.TryGetValue<bool>(out var b))lines.Add($"const bool {pair.Key} = {(b?"true":"false")};");
            else lines.Add($"const float {pair.Key} = {Float(pair.Value!.GetValue<double>())};");
        }
        string body=File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"shaders","crt.frag"));body=body[body.IndexOf("bool outside",StringComparison.Ordinal)..];
        body=body.Replace("void main() {","vec4 hook() {\n    vec2 uv = HOOKED_pos;").Replace("texture2D(frame,","HOOKED_tex(");
        body=Regex.Replace(body,@"gl_FragColor = ([^;]+);\s*return;","return $1;").Replace("gl_FragColor =","return");
        return string.Join('\n',lines)+"\n"+body;
    }
}
