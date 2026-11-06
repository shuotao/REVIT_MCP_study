using System;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
namespace RevitMCP.Core {
 public partial class CommandExecutor {
  // Called inside the new floor's transaction, after its final elevation is set.
  private static JArray JoinNewFloorWalls(Document doc, Floor floor) {
   var rows=new JArray();
   var box=floor.get_BoundingBox(null);
   if(box==null)throw new InvalidOperationException("New floor has no bounds for wall joining.");
   var outline=new Outline(box.Min-new XYZ(0.001,0.001,0.001),box.Max+new XYZ(0.001,0.001,0.001));
   var existing=JoinGeometryUtils.GetJoinedElements(doc,floor);
   var walls=new FilteredElementCollector(doc).OfClass(typeof(Wall)).Cast<Wall>()
    .Where(w=>existing.Contains(w.Id)||new BoundingBoxIntersectsFilter(outline).PassesFilter(doc,w.Id)&&new ElementIntersectsElementFilter(floor).PassesFilter(doc,w.Id)).ToList();
   foreach(var wall in walls) {
    var row=new JObject{["WallId"]=wall.Id.GetIdValue(),["FloorId"]=floor.Id.GetIdValue()};rows.Add(row);
    if(JoinGeometryUtils.AreElementsJoined(doc,floor,wall)&&JoinGeometryUtils.IsCuttingElementInJoin(doc,floor,wall)){row["Status"]="Unchanged";continue;}
    using(var sub=new SubTransaction(doc)) {
     sub.Start();
     try {
      if(!JoinGeometryUtils.AreElementsJoined(doc,floor,wall))JoinGeometryUtils.JoinGeometry(doc,floor,wall);
      if(!JoinGeometryUtils.IsCuttingElementInJoin(doc,floor,wall))JoinGeometryUtils.SwitchJoinOrder(doc,floor,wall);
      doc.Regenerate();
      if(!JoinGeometryUtils.AreElementsJoined(doc,floor,wall)||!JoinGeometryUtils.IsCuttingElementInJoin(doc,floor,wall))throw new InvalidOperationException("Floor cutting order verification failed.");
      if(sub.Commit()!=TransactionStatus.Committed)throw new InvalidOperationException("Wall join commit failed.");
      row["Status"]="Updated";
     }catch(Exception ex) when(FramingFloorRecoverable(ex)) {
      if(sub.GetStatus()==TransactionStatus.Started)sub.RollBack();
      row["Status"]="Failed";row["Reason"]=ex.Message;
     }
    }
   }
   return rows;
  }
 }
}
