using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
#if REVIT2025_OR_GREATER
using BatchId=System.Int64;
#else
using BatchId=System.Int32;
#endif
namespace RevitMCP.Core {
 public partial class CommandExecutor {
  private object SetRcWallsStructural(JObject args) {
   if((string)args["parameterName"]!="結構"||(string)args["value"]!="1")throw new ArgumentException("RC batch supports only 結構=1.");
   var values=args["elementIds"] as JArray;
   if(values==null||values.Count==0||values.Count>10000)throw new ArgumentException("Explicit elementIds required (1..10000).");
   var doc=_uiApp.ActiveUIDocument.Document;var rows=new JArray();var planned=new List<Wall>();
   foreach(var id in values.Values<long>().Distinct()) {
    var wall=doc.GetElement(new ElementId((BatchId)id)) as Wall;var row=new JObject{["ElementId"]=id};rows.Add(row);
    if(wall==null||wall.WallType.Name.IndexOf("RC",StringComparison.OrdinalIgnoreCase)<0){row["Status"]="Skipped";row["Reason"]="NotRcWall";continue;}
    var p=wall.get_Parameter(BuiltInParameter.WALL_STRUCTURAL_SIGNIFICANT);
    row["TypeName"]=wall.WallType.Name;
    if(p==null||p.StorageType!=StorageType.Integer){row["Status"]="Skipped";row["Reason"]="MissingStructuralParameter";continue;}
    if(p.AsInteger()==1){row["Status"]="Unchanged";continue;}
    if(p.IsReadOnly||wall.GroupId!=ElementId.InvalidElementId){row["Status"]="Skipped";row["Reason"]="ReadOnlyOrGrouped";continue;}
    row["Status"]="Planned";planned.Add(wall);
   }
   bool dry=args["dryRun"]?.Value<bool>()??true,apply=args["apply"]?.Value<bool>()??false,committed=false;string error=null;
   if(!dry&&apply&&planned.Count>0)using(var group=new TransactionGroup(doc,"Set selected RC walls structural")) {
    group.Start();
    try {
     using(var tx=new Transaction(doc,"Enable structural walls")) {
      tx.Start();tx.SetFailureHandlingOptions(tx.GetFailureHandlingOptions().SetFailuresPreprocessor(new FramingFloorFailures()).SetClearAfterRollback(true));
      foreach(var wall in planned)if(!wall.get_Parameter(BuiltInParameter.WALL_STRUCTURAL_SIGNIFICANT).Set(1))throw new InvalidOperationException("Set failed: "+wall.Id.GetIdValue());
      if(tx.Commit()!=TransactionStatus.Committed)throw new InvalidOperationException("Transaction not committed.");
     }
     foreach(var wall in planned)if(wall.get_Parameter(BuiltInParameter.WALL_STRUCTURAL_SIGNIFICANT).AsInteger()!=1)throw new InvalidOperationException("Readback failed: "+wall.Id.GetIdValue());
     if(group.Assimilate()!=TransactionStatus.Committed)throw new InvalidOperationException("Group commit failed.");committed=true;
     foreach(var row in rows.Where(r=>(string)r["Status"]=="Planned"))row["Status"]="Updated";
    }catch(Exception ex){if(group.GetStatus()==TransactionStatus.Started)group.RollBack();error=ex.Message;foreach(var row in rows.Where(r=>(string)r["Status"]=="Planned")){row["Status"]="Failed";row["Reason"]="BatchRolledBack";}}
   }
   return new{Success=error==null,DryRun=dry,Apply=apply,Committed=committed,Error=error,Scanned=rows.Count,Planned=planned.Count,Updated=rows.Count(r=>(string)r["Status"]=="Updated"),Unchanged=rows.Count(r=>(string)r["Status"]=="Unchanged"),Skipped=rows.Count(r=>(string)r["Status"]=="Skipped"),Failed=rows.Count(r=>(string)r["Status"]=="Failed"),Results=rows};
  }
 }
}
