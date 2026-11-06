using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;

namespace RevitMCP.Core
{
 public partial class CommandExecutor
 {
  // Limited to the generated native gate; never removes geometry or constraints.
  private static object CompactSiteGateParameters(Document d)
  {
   var fm=d.FamilyManager;
   if(!d.IsFamilyDocument || fm.get_Parameter("整體寬度")==null || fm.get_Parameter("整體高度")==null)
    throw new InvalidOperationException("Generated site gate width/height parameters required.");
   var before=ConstraintSolidBounds(d);
   if(before.Count!=163 || new FilteredElementCollector(d).OfClass(typeof(FamilyInstance)).Any())
    throw new InvalidOperationException("Expected generated gate with 163 native solids and no nested instances.");
   var parameters=fm.Parameters.Cast<FamilyParameter>().ToList();
   var dims=new FilteredElementCollector(d).OfClass(typeof(Dimension)).Cast<Dimension>().ToList();
   var forms=new FilteredElementCollector(d).OfClass(typeof(GenericForm)).Cast<GenericForm>().ToList();
   Func<FamilyParameter,bool> generated=p=>Regex.IsMatch(p.Definition.Name,@"^連動_\d+$") || new[]{"單元寬度","圍籬高度","鐵皮厚度"}.Contains(p.Definition.Name);
   var canonical=new Dictionary<string,FamilyParameter>();int merged=0;
   foreach(var p in parameters.Where(generated))
   {
    if(string.IsNullOrEmpty(p.Formula))continue;
    string key=Regex.Replace(p.Formula,@"\s+","");
    // Constant-only arithmetic has no parameter dependencies; equal numeric values can share a label.
    if(Regex.IsMatch(p.Formula,@"^[0-9.\s()+*/m-]+$") && fm.Types.Cast<FamilyType>().All(t=>t.HasValue(p)))
     key="constant:"+string.Join(";",fm.Types.Cast<FamilyType>().Select(t=>Math.Round(t.AsDouble(p).Value,10).ToString("R",System.Globalization.CultureInfo.InvariantCulture)));
    if(!canonical.TryGetValue(key,out var keep)){canonical[key]=p;continue;}
    foreach(var dim in dims){FamilyParameter label=null;try{label=dim.FamilyLabel;}catch{}if(label!=null && label.Id==p.Id)dim.FamilyLabel=keep;}
    foreach(var form in forms)foreach(Parameter ep in form.Parameters)
    {if(ConstraintAssociation(d,ep)==p.Definition.Name)fm.AssociateElementParameterToFamilyParameter(ep,keep);}
    merged++;
   }
   var used=new HashSet<string>(new[]{"整體寬度","整體高度"});
   foreach(var dim in dims){try{if(dim.FamilyLabel!=null)used.Add(dim.FamilyLabel.Definition.Name);}catch{}}
   foreach(var form in forms)foreach(Parameter ep in form.Parameters)
   {var associated=ConstraintAssociation(d,ep);if(associated!=null)used.Add(associated);}
   bool changed;
   do{changed=false;foreach(var p in parameters.Where(p=>used.Contains(p.Definition.Name)))
    foreach(var dependency in parameters)if(!string.IsNullOrEmpty(p.Formula) && Regex.IsMatch(p.Formula,@"(?<![\p{L}\p{N}_])"+Regex.Escape(dependency.Definition.Name)+@"(?![\p{L}\p{N}_])"))changed|=used.Add(dependency.Definition.Name);
   }while(changed);
   var removed=new List<string>();
   foreach(var p in parameters.Where(p=>generated(p)&&!used.Contains(p.Definition.Name)))
   {removed.Add(p.Definition.Name);fm.RemoveParameter(p);}
   d.Regenerate();var after=ConstraintSolidBounds(d);
   if(after.Count!=before.Count)throw new InvalidOperationException("Compaction changed solid count.");
   foreach(var b in before){var a=after.Single(x=>x.Id==b.Id);if(a.Min.Zip(b.Min,(x,y)=>Math.Abs(x-y)).Concat(a.Max.Zip(b.Max,(x,y)=>Math.Abs(x-y))).Max()>.1)throw new InvalidOperationException("Compaction changed geometry.");}
   return new{Kind="compactSiteGateParameters",Before=parameters.Count,After=fm.Parameters.Size,EquivalentBindingsMerged=merged,Removed=removed,GeometryUnchanged=true};
  }
 }
}
