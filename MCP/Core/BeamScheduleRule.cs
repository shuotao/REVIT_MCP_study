using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
namespace RevitMCP.Core {
 public class BeamScheduleRule {
  public string[] Patterns {get;set;}
  public string TypeName {get;set;}
  public double WidthMm {get;set;}
  public double HeightMm {get;set;}
  public string BeamRole {get;set;}
  public void Validate(){if(Patterns==null||Patterns.Length==0||string.IsNullOrWhiteSpace(TypeName)||double.IsNaN(WidthMm)||double.IsInfinity(WidthMm)||WidthMm<=0||double.IsNaN(HeightMm)||double.IsInfinity(HeightMm)||HeightMm<=0)throw new ArgumentException("Invalid beam schedule rule.");if(BeamRole!="大樑"&&BeamRole!="次樑")throw new ArgumentException("Rule beamRole must be 大樑 or 次樑.");foreach(var p in Patterns)Score(p,"");}
  public static int Score(string pattern,string label){
   if(pattern==null)throw new ArgumentException("Missing pattern.");
   var wildcard=Regex.Match(pattern,@"\A([A-Za-z]+)\*\z");if(wildcard.Success)return Regex.IsMatch(label??"",@"\A"+wildcard.Groups[1].Value+@"[0-9]+\z")?1:0;
   var range=Regex.Match(pattern,@"\A([A-Za-z]+)([0-9]+)[~～]([A-Za-z]*)([0-9]+)\z");
   if(range.Success){int lo,hi;if(!int.TryParse(range.Groups[2].Value,out lo)||!int.TryParse(range.Groups[4].Value,out hi)||lo>hi||(range.Groups[3].Length>0&&range.Groups[3].Value!=range.Groups[1].Value))throw new ArgumentException("Invalid numeric range: "+pattern);var m=Regex.Match(label??"",@"\A"+range.Groups[1].Value+@"([0-9]+)\z");int n;return m.Success&&int.TryParse(m.Groups[1].Value,out n)&&n>=lo&&n<=hi?2:0;}
   if(!Regex.IsMatch(pattern,@"\A[A-Za-z]+[0-9]+\z"))throw new ArgumentException("Unsupported beam pattern: "+pattern);
   return pattern==label?3:0;
  }
  public static BeamScheduleRule Resolve(IEnumerable<BeamScheduleRule> rules,string label){var ranked=rules.Select(r=>new{Rule=r,Score=r.Patterns.Max(p=>Score(p,label?.Trim()))}).Where(x=>x.Score>0).ToList();if(ranked.Count==0)return null;var best=ranked.Where(x=>x.Score==ranked.Max(y=>y.Score)).ToList();if(best.Count!=1)throw new ArgumentException("Ambiguous schedule rules for "+label);return best[0].Rule;}
 }
}
