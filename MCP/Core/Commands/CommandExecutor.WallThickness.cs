using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
#if REVIT2025_OR_GREATER
using WallResizeId = System.Int64;
#else
using WallResizeId = System.Int32;
#endif
namespace RevitMCP.Core {
 public partial class CommandExecutor {
  // Explicit physical outward normals are supplied by the caller, not inferred from Wall.Function.
  private object ResizeWallsKeepingFace(JObject p) {
   var doc=_uiApp.ActiveUIDocument.Document;
   double mm=p["targetThicknessMm"]?.Value<double>()??0;
   string name=p["newTypeName"]?.Value<string>();
   var items=p["walls"] as JArray;
   if(double.IsNaN(mm)||double.IsInfinity(mm)||mm<=0||string.IsNullOrWhiteSpace(name)||items==null||items.Count==0)throw new ArgumentException("Thickness, newTypeName and walls required.");
   if(p["typeId"]!=null||p["elementId"]!=null||p["elementIds"]!=null)throw new ArgumentException("Do not mix resize and type-change modes.");
   bool dry=p["dryRun"]?.Value<bool>()??true,apply=p["apply"]?.Value<bool>()??false;
   var plans=new List<(Wall W,XYZ Normal,double Plane,Line Axis,double Width,JObject Row)>();
   var seen=new HashSet<long>();
   foreach(JObject item in items) {
    var id=item["elementId"].Value<WallResizeId>();
    if(!seen.Add(id))throw new ArgumentException("Duplicate wall ID.");
    var w=doc.GetElement(new ElementId(id)) as Wall;
    if(w==null||w.Pinned||w.GroupId!=ElementId.InvalidElementId||w.WallType.Kind!=WallKind.Basic)throw new ArgumentException("Wall unavailable, pinned, grouped or non-basic.");
    var axis=(w.Location as LocationCurve)?.Curve as Line;
    var cs=w.WallType.GetCompoundStructure();
    if(axis==null||cs==null||cs.LayerCount!=1||!cs.IsVerticallyHomogeneous()||cs.GetLayerFunction(0)!=MaterialFunctionAssignment.Structure)throw new ArgumentException("Only straight, single structural-layer walls supported.");
    if(w.CrossSection!=WallCrossSection.Vertical)throw new ArgumentException("Only vertical walls supported.");
    double x=item["outwardNormal"]?["x"]?.Value<double>()??0,y=item["outwardNormal"]?["y"]?.Value<double>()??0;
    if(double.IsNaN(x)||double.IsNaN(y)||double.IsInfinity(x)||double.IsInfinity(y)||Math.Sqrt(x*x+y*y)<0.9)throw new ArgumentException("Explicit outward normal required.");
    var n=new XYZ(x,y,0).Normalize();
    if(Math.Abs(n.DotProduct(axis.Direction))>1e-6||Math.Abs(Math.Abs(n.DotProduct(w.Orientation))-1)>1e-6)throw new ArgumentException("Normal must be perpendicular to wall.");
    double plane=ResizeWallPlane(w,n);
    var row=new JObject{["ElementId"]=id,["OldThicknessMm"]=w.Width*304.8,["NewThicknessMm"]=mm,["FixedPlaneMm"]=plane*304.8,["Status"]="Planned"};
    plans.Add((w,n,plane,axis,w.Width,row));
   }
   var source=plans[0].W.WallType;
   if(plans.Any(v=>v.W.GetTypeId()!=source.Id))throw new ArgumentException("One source wall type per batch required.");
   if(plans.Any(v=>mm<=v.Width*304.8+0.01))throw new ArgumentException("Target must be thicker than source.");
   if(new FilteredElementCollector(doc).OfClass(typeof(WallType)).Cast<WallType>().Any(t=>t.Name==name))throw new ArgumentException("Target name exists; choose a new dedicated name.");
   // Guard every other native wall, including independent finishes that Revit may move
   // through joins or proximity constraints. Capture before opening any transaction.
   var protectedWalls=new FilteredElementCollector(doc).OfClass(typeof(Wall)).Cast<Wall>()
    .Where(w=>!seen.Contains(w.Id.GetIdValue())).Select(w=>new WallResizeSnapshot(w)).ToList();
   foreach(var a in plans) {
    var key=a.W.get_Parameter(BuiltInParameter.WALL_KEY_REF_PARAM);
    if(key==null||key.IsReadOnly)throw new InvalidOperationException("Wall location line unavailable: "+a.W.Id.GetIdValue());
    a.Row["AnchorLocationLine"]=a.Normal.DotProduct(a.W.Orientation)>0?"FinishFaceExterior":"FinishFaceInterior";
   }
   bool committed=false;string error=null;long? newId=null;
   if(!dry&&apply)using(var group=new TransactionGroup(doc,"Thicken walls keeping physical outer face")) {
    group.Start();
    try {
     using(var tx=new Transaction(doc,"Create dedicated wall type and resize")) {
      tx.Start();tx.SetFailureHandlingOptions(tx.GetFailureHandlingOptions().SetFailuresPreprocessor(new WallResizeFailures()).SetClearAfterRollback(true));var type=(WallType)source.Duplicate(name);newId=type.Id.GetIdValue();var cs=type.GetCompoundStructure();cs.SetLayerWidth(0,mm/304.8);type.SetCompoundStructure(cs);
      // Setting the location-line reference alone must leave geometry unchanged.
      // Anchor every wall first, then change type. Never translate the wall, because
      // MoveElement can drag a separate finish even when the target plane is correct.
      foreach(var a in plans) {
       var anchor=a.Normal.DotProduct(a.W.Orientation)>0?WallLocationLine.FinishFaceExterior:WallLocationLine.FinishFaceInterior;
       a.W.get_Parameter(BuiltInParameter.WALL_KEY_REF_PARAM).Set((int)anchor);
      }
      doc.Regenerate();
      foreach(var a in plans) {
       var axis=(a.W.Location as LocationCurve)?.Curve as Line;
       if(axis==null||axis.GetEndPoint(0).DistanceTo(a.Axis.GetEndPoint(0))*304.8>0.1||axis.GetEndPoint(1).DistanceTo(a.Axis.GetEndPoint(1))*304.8>0.1||Math.Abs(ResizeWallPlane(a.W,a.Normal)-a.Plane)*304.8>0.1)
        throw new InvalidOperationException("Anchoring moved wall before type change: "+a.W.Id.GetIdValue());
      }
      VerifyProtectedResizeWalls(protectedWalls);
      foreach(var a in plans)a.W.ChangeTypeId(type.Id);
      doc.Regenerate();VerifyResizedWalls(plans,mm);VerifyProtectedResizeWalls(protectedWalls);
      if(tx.Commit()!=TransactionStatus.Committed)throw new InvalidOperationException("Transaction did not commit.");
     }
     VerifyResizedWalls(plans,mm);VerifyProtectedResizeWalls(protectedWalls);
     if(Math.Abs(source.Width-plans[0].Width)>1e-8)throw new InvalidOperationException("Source type changed.");
     if(group.Assimilate()!=TransactionStatus.Committed)throw new InvalidOperationException("Group did not commit.");
     committed=true;foreach(var a in plans)a.Row["Status"]="Updated";
    }catch(Exception ex){error=ex.Message;if(group.GetStatus()==TransactionStatus.Started)group.RollBack();newId=null;foreach(var a in plans)a.Row["Status"]="Failed";}
   }
   return new{Success=error==null,DryRun=dry,Apply=apply,Committed=committed,Error=error,NewTypeId=newId,Scanned=plans.Count,Updated=committed?plans.Count:0,Failed=error==null?0:plans.Count,Method="LocationLineFaceAnchorV2",ProtectedWallCount=protectedWalls.Count,OtherWallsVerified=committed,Results=plans.Select(a=>a.Row).ToList()};
  }
  private sealed class WallResizeSnapshot {
   public readonly Wall Wall;
   public readonly ElementId TypeId;
   public readonly double Width;
   public readonly Curve Axis;
   public readonly XYZ Min,Max;
   public WallResizeSnapshot(Wall wall) {
    Wall=wall;TypeId=wall.GetTypeId();Width=wall.Width;
    Axis=((wall.Location as LocationCurve)?.Curve)?.Clone();
    var box=wall.get_BoundingBox(null);Min=box?.Min;Max=box?.Max;
   }
  }
  private static void VerifyProtectedResizeWalls(List<WallResizeSnapshot> snapshots) {
   const double tol=0.1/304.8;
   foreach(var s in snapshots) {
    if(!s.Wall.IsValidObject||s.Wall.GetTypeId()!=s.TypeId||Math.Abs(s.Wall.Width-s.Width)>tol)
     throw new InvalidOperationException("Protected wall type changed: "+s.TypeId.GetIdValue());
    var axis=(s.Wall.Location as LocationCurve)?.Curve;
    if((s.Axis==null)!=(axis==null))throw new InvalidOperationException("Protected wall axis unavailable.");
    if(axis!=null)for(int i=0;i<=4;i++)if(axis.Evaluate(i/4.0,true).DistanceTo(s.Axis.Evaluate(i/4.0,true))>tol)
     throw new InvalidOperationException("Protected wall moved: "+s.Wall.Id.GetIdValue());
    var box=s.Wall.get_BoundingBox(null);
    if((box==null)!=(s.Min==null)||(box!=null&&(box.Min.DistanceTo(s.Min)>tol||box.Max.DistanceTo(s.Max)>tol)))
     throw new InvalidOperationException("Protected wall bounds changed: "+s.Wall.Id.GetIdValue());
   }
  }
  private class WallResizeFailures : IFailuresPreprocessor {
   public FailureProcessingResult PreprocessFailures(FailuresAccessor accessor) {
    foreach(var message in accessor.GetFailureMessages()) {
     if(message.GetSeverity()==FailureSeverity.Error||message.GetSeverity()==FailureSeverity.Warning)return FailureProcessingResult.ProceedWithRollBack;
    }
    return FailureProcessingResult.Continue;
   }
  }
  private double ResizeWallPlane(Wall w,XYZ n) {
   var doc=w.Document;var side=n.DotProduct(w.Orientation)>0?ShellLayerType.Exterior:ShellLayerType.Interior;
   var faces=HostObjectUtils.GetSideFaces(w,side).Select(r=>doc.GetElement(r).GetGeometryObjectFromReference(r) as PlanarFace).Where(f=>f!=null&&f.FaceNormal.DotProduct(n)>0.99999).ToList();
   if(faces.Count==0)throw new InvalidOperationException("No physical planar side face.");
   double plane=faces[0].Origin.DotProduct(n);
   if(faces.Any(f=>Math.Abs(f.Origin.DotProduct(n)-plane)*304.8>0.1))throw new InvalidOperationException("Side faces are not coplanar.");
   return plane;
  }
  private void VerifyResizedWalls(List<(Wall W,XYZ Normal,double Plane,Line Axis,double Width,JObject Row)> plans,double mm) {
   foreach(var a in plans){var line=(a.W.Location as LocationCurve)?.Curve as Line;
    if(line==null||Math.Abs(a.W.Width*304.8-mm)>0.01||Math.Abs(ResizeWallPlane(a.W,a.Normal)-a.Plane)*304.8>0.1)throw new InvalidOperationException("Thickness or fixed-face verification failed.");
    for(int i=0;i<2;i++){var delta=line.GetEndPoint(i)-a.Axis.GetEndPoint(i);if(Math.Abs(delta.Z)*304.8>0.1||Math.Abs(delta.DotProduct(a.Axis.Direction))*304.8>0.1)throw new InvalidOperationException("Wall endpoint or height changed.");}
   }
  }
 }
}


