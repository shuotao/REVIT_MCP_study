using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
namespace RevitMCP.Core
{
 internal static class TerrainSpotDxf
 {
  internal sealed class Spot { public string Handle,Text;public double X,Y,Elevation; }
  internal sealed class Result {public List<Spot> Points=new List<Spot>();public List<string> Excluded=new List<string>();public List<double[]> ContourPoints=new List<double[]>();public List<double[]> GridControls=new List<double[]>();public HashSet<string> GridLabels=new HashSet<string>();}
  private sealed class Record {public string Type;public List<KeyValuePair<int,string>> Values=new List<KeyValuePair<int,string>>();public string Get(int c)=>Values.FirstOrDefault(v=>v.Key==c).Value;public double Num(int c,double fallback=0)=>Get(c)==null?fallback:double.Parse(Get(c),CultureInfo.InvariantCulture);}
  internal static Result Read(string path,string layer,string tag)
  {
   if(!string.Equals(Path.GetExtension(path),".dxf",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("ASCII DXF required.");
   var records=new List<Record>();Record current=null;var lines=File.ReadAllLines(path);
   if(lines.Length<2||lines[0].StartsWith("AutoCAD Binary"))throw new ArgumentException("ASCII DXF required.");
   for(int i=0;i+1<lines.Length;i+=2){int code;if(!int.TryParse(lines[i].Trim(),out code))throw new ArgumentException("Malformed DXF.");string value=lines[i+1].Trim();if(code==0){current=new Record{Type=value};records.Add(current);}else current?.Values.Add(new KeyValuePair<int,string>(code,value));}
   var result=new Result();bool entities=false;Record insert=null;string polyLayer=null;
   foreach(var r in records){
    if(r.Type=="SECTION"){entities=r.Get(2)=="ENTITIES";continue;}if(r.Type=="ENDSEC"){entities=false;continue;}if(!entities)continue;
    if(r.Type=="POLYLINE")polyLayer=r.Get(8);
    if(r.Type=="VERTEX"&&(polyLayer=="AC"||polyLayer=="IDX"))result.ContourPoints.Add(new[]{r.Num(10),r.Num(20),r.Num(30)});
    if(r.Type=="LWPOLYLINE"&&(r.Get(8)=="AC"||r.Get(8)=="IDX")){double? x=null;foreach(var v in r.Values){if(v.Key==10)x=double.Parse(v.Value,CultureInfo.InvariantCulture);if(v.Key==20&&x.HasValue){result.ContourPoints.Add(new[]{x.Value,double.Parse(v.Value,CultureInfo.InvariantCulture),r.Num(38)});x=null;}}}
    if(r.Type=="INSERT")insert=r;
    if(r.Type=="INSERT"&&r.Get(8)=="軸線"&&r.Get(2)=="基準"){if(r.Num(41,1)!=1||r.Num(42,1)!=1)throw new ArgumentException("Scaled grid control unsupported.");result.GridControls.Add(new[]{r.Num(10),r.Num(20),r.Num(50)});}
    if(r.Type=="MTEXT"&&r.Get(8)=="軸線")result.GridLabels.Add(r.Get(1)??"");
    if(r.Type=="SEQEND"){insert=null;polyLayer=null;}
    if(r.Type!="ATTRIB"||insert==null||insert.Get(8)!=layer||r.Get(2)!=tag)continue;
    string text=r.Get(1)??"",handle=r.Get(5)??"";
    if(Regex.IsMatch(text,"[()（）]")){result.Excluded.Add(handle+":"+text);continue;}
    if(!Regex.IsMatch(text,@"^[+-]?\d+(?:\.\d+)?$"))throw new ArgumentException("Non-numeric elevation attribute "+handle+":"+text);
    if(Math.Abs(insert.Num(210))>1e-9||Math.Abs(insert.Num(220))>1e-9||Math.Abs(insert.Num(230,1)-1)>1e-9)throw new ArgumentException("Unsupported INSERT normal.");
    result.Points.Add(new Spot{Handle=handle,Text=text,X=insert.Num(10),Y=insert.Num(20),Elevation=double.Parse(text,CultureInfo.InvariantCulture)});
   }
   if(result.Points.Count==0||result.ContourPoints.Count<3)throw new ArgumentException("No supported elevation attributes or contours for alignment validation.");return result;
  }
 }
}
