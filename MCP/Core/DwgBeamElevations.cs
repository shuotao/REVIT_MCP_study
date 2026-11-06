using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;

namespace RevitMCP.Core {
 public static partial class DwgBeamExecutor {
  private static void MatchSeparateElevations(Document doc,ImportInstance cad,JObject p,Dictionary<string,List<LabelData>> numbers,double factor,
   Dictionary<LabelData,string> suffixes,Dictionary<LabelData,string> errors,JArray diagnostics){
   if(p["elevationLayerPairs"]==null)return;
   if(!(p["elevationLayerPairs"] is JArray pairs)||pairs.Count==0)throw new ArgumentException("elevationLayerPairs must be a nonempty array.");
   var seen=new HashSet<string>();var seenElevations=new HashSet<string>();
   foreach(JObject pair in pairs){
    string n=(string)pair["numberLayer"],e=(string)pair["elevationLayer"];
    if(n==null||e==null||!numbers.ContainsKey(n)||!seen.Add(n)||!seenElevations.Add(e))throw new ArgumentException("Each elevation layer must map to one distinct selected number layer.");
    var labels=numbers[n];var elevations=ReadScheduleLabels(doc,cad,e,factor,(string)p["pythonExecutable"]??"python");
    if(elevations.Count==0)throw new ArgumentException("No elevation text found in "+e+"; refusing default elevations.");
    foreach(var elevation in elevations){
     var ranked=labels.Select(l=>new{Label=l,Distance=Math.Sqrt(Math.Pow(l.X-elevation.X,2)+Math.Pow(l.Y-elevation.Y,2))*FtMm}).OrderBy(x=>x.Distance).ToList();
     int choice=ColumnMarkMatching.Select(ranked.Select(x=>x.Distance).ToList(),2000,100);
     var row=new JObject{["Layer"]=e,["Text"]=elevation.Text,["Xmm"]=elevation.X*FtMm,["Ymm"]=elevation.Y*FtMm};diagnostics.Add(row);
     if(choice<0){
      row["Status"]="Unresolved";row["Reason"]=choice==-2?"AmbiguousElevationNumber":"DistantElevationNumber";
      foreach(var candidate in ranked.Take(2))errors[candidate.Label]=(string)row["Reason"];
      row["CandidateNumbers"]=JArray.FromObject(ranked.Take(2).Select(x=>new{Number=x.Label.Text,DistanceMm=x.Distance}));continue;
     }
     var target=ranked[choice];row["Number"]=target.Label.Text;row["DistanceMm"]=target.Distance;
     try{if(!BeamElevationLabel.Parse("B1"+elevation.Text).Offset.HasValue)throw new ArgumentException();}
     catch(ArgumentException){errors[target.Label]="InvalidSeparateElevation";row["Status"]="Invalid";continue;}
     if(suffixes.ContainsKey(target.Label)){errors[target.Label]="MultipleElevationLabels";row["Status"]="Ambiguous";continue;}
     suffixes[target.Label]=elevation.Text;row["Status"]="Matched";
    }
   }
  }
 }
}
