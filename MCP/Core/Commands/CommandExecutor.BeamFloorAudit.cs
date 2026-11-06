using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
#if REVIT2025_OR_GREATER
using IdType = System.Int64;
#else
using IdType = System.Int32;
#endif
namespace RevitMCP.Core {
 public partial class CommandExecutor {
  private object ApplyBeamFloorJoins(JObject parameters) {
   var query=(JObject)parameters.DeepClone();query["apply"]=false;query["dryRun"]=true;
   var before=JObject.FromObject(AuditBeamFloorJoins(query));
   bool dry=parameters["dryRun"]?.Value<bool>()??true,apply=parameters["apply"]?.Value<bool>()??false;
   if(dry||!apply)return before;
   var doc=_uiApp.ActiveUIDocument.Document;var results=new JArray();bool committed=false;string error=null;
   using(var tx=new Transaction(doc,"Beams cut floors")) {
    try {
     if(tx.Start()!=TransactionStatus.Started)throw new InvalidOperationException("Transaction start failed.");
     tx.SetFailureHandlingOptions(tx.GetFailureHandlingOptions().SetFailuresPreprocessor(new FramingFloorFailures()).SetClearAfterRollback(true).SetForcedModalHandling(true));
     foreach(var item in (JArray)before["Results"]) {
      var row=(JObject)item.DeepClone();results.Add(row);
      if((string)row["Status"]=="Correct")continue;
      var beam=doc.GetElement(new ElementId((IdType)(long)row["BeamId"]));
      var floor=doc.GetElement(new ElementId((IdType)(long)row["FloorId"]));
      using(var sub=new SubTransaction(doc)) {
       sub.Start();
       try {
        if(beam==null||floor==null)throw new InvalidOperationException("Element missing.");
        if(!JoinGeometryUtils.AreElementsJoined(doc,beam,floor))JoinGeometryUtils.JoinGeometry(doc,beam,floor);
        if(!JoinGeometryUtils.IsCuttingElementInJoin(doc,beam,floor))JoinGeometryUtils.SwitchJoinOrder(doc,beam,floor);
        doc.Regenerate();
        if(!JoinGeometryUtils.AreElementsJoined(doc,beam,floor)||!JoinGeometryUtils.IsCuttingElementInJoin(doc,beam,floor))throw new InvalidOperationException("Beam cutting verification failed.");
        if(sub.Commit()!=TransactionStatus.Committed)throw new InvalidOperationException("Pair commit failed.");
        row["Status"]="Updated";
       }catch(Exception ex) when(FramingFloorRecoverable(ex)) {
        if(sub.GetStatus()==TransactionStatus.Started)sub.RollBack();row["Status"]="Failed";row["Reason"]=ex.Message;
       }
      }
     }
     if(tx.Commit()!=TransactionStatus.Committed)throw new InvalidOperationException("Batch commit failed.");committed=true;
    }catch(Exception ex) {
     if(tx.GetStatus()==TransactionStatus.Started)tx.RollBack();error=ex.Message;
     foreach(var row in results.OfType<JObject>().Where(r=>(string)r["Status"]=="Updated")){row["Status"]="Failed";row["Reason"]="BatchNotCommitted";}
    }
   }
   var after=JObject.FromObject(AuditBeamFloorJoins(query));
   // Final committed state is authoritative: regeneration can discard a join.
   foreach(var row in results.OfType<JObject>().Where(r=>(string)r["Status"]=="Updated"))
    if(!((JArray)after["Results"]).Any(r=>(long)r["BeamId"]==(long)row["BeamId"]&&(long)r["FloorId"]==(long)row["FloorId"]&&(string)r["Status"]=="Correct")) {row["Status"]="Failed";row["Reason"]="PostCommitVerificationFailed";}
   return new {Success=error==null&&results.All(r=>(string)r["Status"]!="Failed"),Committed=committed,Error=error,
    Updated=results.Count(r=>(string)r["Status"]=="Updated"),Failed=results.Count(r=>(string)r["Status"]=="Failed"),Results=results,Verification=after};
  }
  private object AuditBeamFloorJoins(JObject parameters) {
   if(parameters["apply"]?.Value<bool>()==true || parameters["dryRun"]?.Value<bool>()==false)
    throw new ArgumentException("auditBeamFloor is read-only; use dryRun=true and apply=false.");
   var doc=_uiApp.ActiveUIDocument.Document;
   var names=(parameters["levels"] as JArray)?.Values<string>().ToList();
   var levels=new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
    .Where(l=>names==null||names.Count==0||names.Contains(l.Name)).ToList();
   if(names!=null&&names.Any(n=>!levels.Any(l=>l.Name==n)))throw new ArgumentException("Unknown exact level name.");
   var ids=new HashSet<ElementId>(levels.Select(l=>l.Id));
   var floors=new FilteredElementCollector(doc).OfClass(typeof(Floor)).Cast<Floor>()
    .Where(f=>names==null||names.Count==0||ids.Contains(f.LevelId)).ToList();
   var rows=new JArray();var excluded=new JArray();
   foreach(var floor in floors) {
    try {
     var candidates=new HashSet<ElementId>(JoinGeometryUtils.GetJoinedElements(doc,floor));
     var box=floor.get_BoundingBox(null);
     if(box!=null) {
      var outline=new Outline(box.Min-new XYZ(0.001,0.001,0.001),box.Max+new XYZ(0.001,0.001,0.001));
      foreach(var id in new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_StructuralFraming)
       .WhereElementIsNotElementType().WherePasses(new BoundingBoxIntersectsFilter(outline)).ToElementIds())candidates.Add(id);
     }else excluded.Add(new JObject{["FloorId"]=floor.Id.GetIdValue(),["Reason"]="MissingBoundingBox"});
     foreach(var id in candidates) {
      var beam=doc.GetElement(id) as FamilyInstance;
      if(beam==null||beam.StructuralType!=Autodesk.Revit.DB.Structure.StructuralType.Beam)continue;
      try {
       bool joined=JoinGeometryUtils.AreElementsJoined(doc,beam,floor);
       if(!joined&&!new ElementIntersectsElementFilter(floor).PassesFilter(doc,id))continue;
       string status=!joined?"NotJoined":JoinGeometryUtils.IsCuttingElementInJoin(doc,beam,floor)?"Correct":"Reversed";
       rows.Add(new JObject{["BeamId"]=id.GetIdValue(),["FloorId"]=floor.Id.GetIdValue(),["Status"]=status});
      }catch(Exception ex) when(FramingFloorRecoverable(ex)) {
       excluded.Add(new JObject{["BeamId"]=id.GetIdValue(),["FloorId"]=floor.Id.GetIdValue(),["Reason"]=ex.Message});
      }
     }
    }catch(Exception ex) when(FramingFloorRecoverable(ex)) {excluded.Add(new JObject{["FloorId"]=floor.Id.GetIdValue(),["Reason"]=ex.Message});}
   }
   return new {Success=excluded.Count==0,DryRun=true,Apply=false,Rule="Beam > Floor",ProcessedFloors=floors.Count,Scanned=rows.Count,
    Correct=rows.Count(r=>(string)r["Status"]=="Correct"),Reversed=rows.Count(r=>(string)r["Status"]=="Reversed"),
    NotJoined=rows.Count(r=>(string)r["Status"]=="NotJoined"),Failed=excluded.Count,Results=rows,ExcludedItems=excluded};
  }
 }
}

