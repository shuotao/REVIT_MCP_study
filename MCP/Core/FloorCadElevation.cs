using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
namespace RevitMCP.Core {
 internal static class FloorCadElevation {
  internal sealed class Text {public string Value;public double X,Y;}
  internal sealed class Match {public string Code,Reason,Annotation;public long LevelId;public double OffsetMm;}
  internal static List<Match> Resolve(IList<Text> numbers,IList<Text> elevations,long defaultLevel,long numericLevel,double factor,double radius,double gap,ISet<string> skip,bool foldedFlat) {
   var result=numbers.Select(n=>new Match{Code=n.Value.Trim(),LevelId=defaultLevel}).ToList();
   for(int i=0;i<numbers.Count;i++) {
    string raw=result[i].Code;
    if(skip.Contains(raw)){result[i].Reason="ExcludedCadLabel";continue;}
    var code=Regex.Match(raw,@"^([A-Za-z]+[0-9]+)(?:[（(]([^()（）]+)[）)])?$");
    if(!code.Success){result[i].Reason="UnsupportedSlabLabel";continue;}
    result[i].Code=code.Groups[1].Value;
    if(code.Groups[2].Success) Parse(result[i],code.Groups[2].Value,numericLevel,factor,foldedFlat);
   }
   var assigned=new Dictionary<int,List<string>>();
   foreach(var elevation in elevations){
    var candidates=numbers.Select((n,i)=>new{Index=i,Distance=Math.Sqrt(Math.Pow(n.X-elevation.X,2)+Math.Pow(n.Y-elevation.Y,2))}).OrderBy(v=>v.Distance).ToList();
    if(candidates.Count==0)continue;
    if(candidates[0].Distance>radius){result[candidates[0].Index].Reason="ElevationOutsidePairingRange";continue;}
    if(candidates.Count>1&&candidates[1].Distance-candidates[0].Distance<gap){foreach(var v in candidates.Where(v=>v.Distance-candidates[0].Distance<gap))result[v.Index].Reason="AmbiguousElevationPair";continue;}
    int index=candidates[0].Index;if(!assigned.ContainsKey(index))assigned[index]=new List<string>();assigned[index].Add(elevation.Value.Trim());
   }
   foreach(var entry in assigned){
    var m=result[entry.Key];if(m.Reason!=null)continue;
    var values=entry.Value.Distinct().ToList();
    if(values.Count!=1||m.Annotation!=null){m.Reason="ConflictingElevationAnnotations";continue;}
    var value=Regex.Match(values[0],@"^[（(]([^()（）]+)[）)]$");
    if(!value.Success){m.Reason="UnsupportedElevationAnnotation";continue;}
    Parse(m,value.Groups[1].Value,numericLevel,factor,foldedFlat);
   }
   return result;
  }
  private static void Parse(Match m,string text,long numericLevel,double factor,bool foldedFlat){
   m.Annotation=text.Trim();
   if(m.Annotation=="折板"&&foldedFlat)return;
   if(!Regex.IsMatch(m.Annotation,@"^[+-]?(?:\d+(?:\.\d*)?|\.\d+)$")||!double.TryParse(m.Annotation,NumberStyles.Float,CultureInfo.InvariantCulture,out double n)||Math.Abs(n*factor)>10000){m.Reason="UnsupportedElevationAnnotation";return;}
   m.LevelId=numericLevel;m.OffsetMm=n*factor;
  }
 }
}
