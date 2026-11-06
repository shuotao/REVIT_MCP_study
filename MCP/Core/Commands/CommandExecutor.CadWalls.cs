using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
namespace RevitMCP.Core {
 public partial class CommandExecutor {
  private object CreateWallsFromDwg(JObject p) {
   var doc=_uiApp.ActiveUIDocument.Document;
   if(doc.IsFamilyDocument)throw new ArgumentException("Project document required.");
   var view=doc.ActiveView as ViewPlan??throw new ArgumentException("Plan view required.");
   var bottom=doc.GetElement(new ElementId(FramingFloorId(p,"baseLevelId"))) as Level??throw new ArgumentException("Base level missing.");
   var top=doc.GetElement(new ElementId(FramingFloorId(p,"topLevelId"))) as Level??throw new ArgumentException("Top level missing.");
   if(view.GenLevel?.Id!=bottom.Id)throw new ArgumentException("Active view must be on base level.");
   double bo=FramingFloorNumber(p,"baseOffsetMm",0,-10000,10000),to=FramingFloorNumber(p,"topOffsetMm",0,-10000,10000),tol=FramingFloorNumber(p,"toleranceMm",2,0.1,5),gap=FramingFloorNumber(p,"maxOpeningGapMm",3000,0,6000);
   double z=bottom.Elevation+bo/304.8,zt=top.Elevation+to/304.8;if(zt<=z)throw new ArgumentException("Top must be above base.");
   bool dry=FramingFloorFlag(p,"dryRun",true),apply=FramingFloorFlag(p,"apply",false);
   string layer=(string)p["layerName"]??"WALL";
   if(string.IsNullOrWhiteSpace(layer))throw new ArgumentException("Layer is required.");
   var cads=new FilteredElementCollector(doc,view.Id).OfClass(typeof(ImportInstance)).Cast<ImportInstance>().ToList();
   if(p["importInstanceUniqueId"]!=null)cads=cads.Where(c=>c.UniqueId==(string)p["importInstanceUniqueId"]).ToList();
   if(cads.Count!=1)throw new ArgumentException("Select exactly one visible CAD using importInstanceUniqueId.");
   var cad=cads[0];var openingLayers=p["openingLayerNames"]==null?new HashSet<string>(new[]{"DOOR","DOOR-GLAZ","DOOR-HDLN","DOOR-OPNG","WINDOW"}):new HashSet<string>(((JArray)p["openingLayerNames"]).Values<string>());
   if(openingLayers.Contains(layer))throw new ArgumentException("Wall layer cannot also be an opening layer.");
   var lines=new List<CadWallGeometry.Segment>();var openings=new List<CadWallGeometry.Segment>();var unsupported=new List<string>();var excludedCurves=new List<List<XYZ>>();
   Action<GeometryElement,Transform> walk=null;
   walk=(ge,tr)=>{if(ge==null)return;foreach(var obj in ge){if(obj is GeometryInstance gi){walk(gi.GetSymbolGeometry(),tr.Multiply(gi.Transform));continue;}
    string name=(doc.GetElement(obj.GraphicsStyleId) as GraphicsStyle)?.GraphicsStyleCategory?.Name;if(name!=layer&&!openingLayers.Contains(name??""))continue;
    var dest=name==layer?lines:openings;
    Action<XYZ,XYZ> add=(a,b)=>{a=tr.OfPoint(a);b=tr.OfPoint(b);if(Math.Abs(a.Z-b.Z)>tol/304.8){throw new ArgumentException(name+": nonhorizontal geometry; cannot safely pair this drawing.");}var s=new CadWallGeometry.Segment(a.X*304.8,a.Y*304.8,b.X*304.8,b.Y*304.8);if(s.Length>0.1)dest.Add(s);};
    if(obj is Line line)add(line.GetEndPoint(0),line.GetEndPoint(1));
    else if(obj is PolyLine poly){var points=poly.GetCoordinates();for(int i=1;i<points.Count;i++)add(points[i-1],points[i]);}
    else if(obj is Curve curve&&name!=layer){var points=curve.Tessellate();for(int i=1;i<points.Count;i++)add(points[i-1],points[i]);}
    else {unsupported.Add(name+": "+obj.GetType().Name);if(name==layer && obj is Curve unsupportedCurve)excludedCurves.Add(unsupportedCurve.Tessellate().Select(tr.OfPoint).ToList());else if(name==layer)throw new ArgumentException("Unsupported wall geometry cannot be safely localized: "+obj.GetType().Name);}
   }};
   walk(cad.get_Geometry(new Options{View=view,IncludeNonVisibleObjects=false}),Transform.Identity);
   if(lines.Count==0)throw new ArgumentException("No straight wall lines on selected layer.");
   if(lines.Count>15000)throw new ArgumentException("Too many wall lines; limit CAD scope.");
   var lining=p["lining"] as JObject;
   WallType liningType=null;var referenceNames=new HashSet<string>();double cadWidth=0,referenceDistance=0;
   if(lining!=null){
    if(p["wallTypeOverrides"]!=null)throw new ArgumentException("lining and wallTypeOverrides are mutually exclusive.");
    liningType=doc.GetElement(new ElementId(FramingFloorId(lining,"wallTypeId"))) as WallType;
    if(liningType==null||liningType.Kind!=WallKind.Basic||liningType.GetCompoundStructure()==null||liningType.GetCompoundStructure().VariableLayerIndex>=0)throw new ArgumentException("Fixed-width basic lining wall type required.");
    cadWidth=FramingFloorNumber(lining,"cadWidthMm",0,50,1000);
    double expected=FramingFloorNumber(lining,"expectedWidthMm",0,50,1000);
    if(Math.Abs(liningType.Width*304.8-expected)>0.1)throw new ArgumentException("Lining actual width does not match expectedWidthMm; confirm type width before writing.");
    referenceDistance=FramingFloorNumber(lining,"maxReferenceGapMm",1000,1,3000);
    referenceNames=new HashSet<string>((lining["referenceWallTypeNames"] as JArray??throw new ArgumentException("Reference type names required.")).Values<string>());
    if(referenceNames.Count==0||referenceNames.Any(string.IsNullOrWhiteSpace))throw new ArgumentException("Reference names must be nonempty.");
   }
   var types=new FilteredElementCollector(doc).OfClass(typeof(WallType)).Cast<WallType>().Where(t=>t.Kind==WallKind.Basic&&t.Name.StartsWith("RC-",StringComparison.OrdinalIgnoreCase)&&t.GetCompoundStructure()!=null&&t.GetCompoundStructure().VariableLayerIndex<0).OrderBy(t=>t.Id.GetIdValue()).ToList();
   double? NamedWidth(WallType t){var m=System.Text.RegularExpressions.Regex.Match(t.Name,@"^RC-\s*(?<width>[0-9]+(?:\.[0-9]+)?)\s*cm$",System.Text.RegularExpressions.RegexOptions.IgnoreCase);return m.Success?(double?)(double.Parse(m.Groups["width"].Value,CultureInfo.InvariantCulture)*10):null;}
   var inconsistent=types.Where(t=>NamedWidth(t).HasValue&&Math.Abs(NamedWidth(t).Value-t.Width*304.8)>0.1).ToList();
   var mismatchIds=new HashSet<ElementId>(inconsistent.Select(t=>t.Id));
   var affectedCounts=new FilteredElementCollector(doc).OfClass(typeof(Wall)).Cast<Wall>().Where(w=>mismatchIds.Contains(w.GetTypeId())).GroupBy(w=>w.GetTypeId()).ToDictionary(g=>g.Key,g=>g.Count());
   var typeWarnings=inconsistent.Select(t=>new {TypeId=t.Id.GetIdValue(),TypeName=t.Name,NamedWidthMm=NamedWidth(t),ActualWidthMm=t.Width*304.8,AffectedWallCount=affectedCounts.TryGetValue(t.Id,out int count)?count:0,Reason="TypeNameWidthMismatch",RequiresUserConfirmation=true,Message="Ask whether to correct actual type width to the named width; never modify automatically."}).ToList();
   var overrides=new List<Tuple<double,WallType>>();
   if(p["wallTypeOverrides"]!=null){
    if(!(p["wallTypeOverrides"] is JArray overrideArray)||overrideArray.Count==0)throw new ArgumentException("wallTypeOverrides must be nonempty.");
    foreach(JObject item in overrideArray){double width=FramingFloorNumber(item,"widthMm",0,50,1000);long id=FramingFloorId(item,"wallTypeId");var type=types.SingleOrDefault(t=>t.Id.GetIdValue()==id);
     if(type==null||Math.Abs(type.Width*304.8-width)>tol)throw new ArgumentException("Override must select an eligible RC type with matching actual width.");
     if(overrides.Any(o=>Math.Abs(o.Item1-width)<=2*tol))throw new ArgumentException("Overlapping override widths.");overrides.Add(Tuple.Create(width,type));
    }
   }
   var runs=CadWallGeometry.Find(lines,openings,tol,gap,50,1000);
   var existing=new FilteredElementCollector(doc).OfClass(typeof(Wall)).Cast<Wall>().Where(w=>{var bb=w.get_BoundingBox(null);return bb!=null&&bb.Max.Z>z+0.001&&bb.Min.Z<zt-0.001;}).ToList();
   var referenceWalls=existing.Where(w=>referenceNames.Contains(w.WallType.Name)&&w.CrossSection==WallCrossSection.Vertical&&(w.Location as LocationCurve)?.Curve is Line).ToList();
   var references=referenceWalls.Select(w=>{var l=(Line)((LocationCurve)w.Location).Curve;var a=l.GetEndPoint(0);var b=l.GetEndPoint(1);return new CadLiningPlacement.Reference{Id=w.Id.GetIdValue(),Axis=new CadWallGeometry.Segment(a.X*304.8,a.Y*304.8,b.X*304.8,b.Y*304.8),Width=w.Width*304.8};}).ToList();
   var originalAxes=lining==null?new Dictionary<ElementId,Line>():existing.Where(w=>(w.Location as LocationCurve)?.Curve is Line).ToDictionary(w=>w.Id,w=>(Line)((LocationCurve)w.Location).Curve.Clone());
   var rows=new List<JObject>();var plans=new List<Tuple<CadWallGeometry.Run,WallType,JObject>>();
   foreach(var r in runs){var s=r.Axis;var row=new JObject{["Status"]="Skipped",["WidthMm"]=r.Width,["LengthMm"]=s.Length,["BridgedOpenings"]=r.Bridges,["Start"]=new JObject{["x"]=s.X1,["y"]=s.Y1},["End"]=new JObject{["x"]=s.X2,["y"]=s.Y2}};rows.Add(row);
    if(r.Reason!=null){row["Reason"]=r.Reason;continue;}
    if(excludedCurves.Any(points=>points.Count>0 && Math.Max(s.X1,s.X2)+r.Width/2>=points.Min(a=>a.X)*304.8 && Math.Min(s.X1,s.X2)-r.Width/2<=points.Max(a=>a.X)*304.8 && Math.Max(s.Y1,s.Y2)+r.Width/2>=points.Min(a=>a.Y)*304.8 && Math.Min(s.Y1,s.Y2)-r.Width/2<=points.Max(a=>a.Y)*304.8)){row["Reason"]="NearUnsupportedWallGeometry";continue;}
    var placement=r;
    if(lining!=null){
     if(Math.Abs(r.Width-cadWidth)>tol){row["Reason"]="CadWidthMismatch";continue;}
     var resolved=CadLiningPlacement.Resolve(r,liningType.Width*304.8,references,referenceDistance,tol);
     if(resolved.Reason!=null){row["Reason"]=resolved.Reason;continue;}
     placement=resolved.Run;s=placement.Axis;
     row["ReferenceWallIds"]=JArray.FromObject(resolved.ReferenceIds);row["OutwardSign"]=resolved.Side;row["InteriorPlaneMm"]=resolved.InteriorPlane;
     row["ActualWidthMm"]=placement.Width;row["CenterShiftMm"]=placement.N-r.N;
     row["PlacedStart"]=new JObject{["x"]=s.X1,["y"]=s.Y1};row["PlacedEnd"]=new JObject{["x"]=s.X2,["y"]=s.Y2};
    }
    var selectedOverride=overrides.Where(o=>Math.Abs(o.Item1-r.Width)<=tol).ToList();
    var matching=liningType!=null?new List<WallType>{liningType}:selectedOverride.Count==1?new List<WallType>{selectedOverride[0].Item2}:types.Where(t=>!mismatchIds.Contains(t.Id)&&Math.Abs(t.Width*304.8-r.Width)<=tol).ToList();if(matching.Count!=1){row["Reason"]=matching.Count==0?"NoMatchingRcType":"AmbiguousRcTypes";continue;}
    row["TypeId"]=matching[0].Id.GetIdValue();row["TypeName"]=matching[0].Name;
    var overlaps=existing.Where(w=>{var lc=w.Location as LocationCurve;if(lc?.Curve is Line l){var a=l.GetEndPoint(0);var b=l.GetEndPoint(1);return CadWallGeometry.Overlaps(placement,new CadWallGeometry.Segment(a.X*304.8,a.Y*304.8,b.X*304.8,b.Y*304.8),w.Width*304.8,tol);}var bb=w.get_BoundingBox(null);return bb!=null&&Math.Max(s.X1,s.X2)+r.Width/2>bb.Min.X*304.8&&Math.Min(s.X1,s.X2)-r.Width/2<bb.Max.X*304.8&&Math.Max(s.Y1,s.Y2)+r.Width/2>bb.Min.Y*304.8&&Math.Min(s.Y1,s.Y2)-r.Width/2<bb.Max.Y*304.8;}).Select(w=>w.Id.GetIdValue()).ToList();
    if(overlaps.Count>0){row["Reason"]="ExistingWallOverlap";row["ExistingWallIds"]=JArray.FromObject(overlaps);continue;}
    row["Status"]="Planned";plans.Add(Tuple.Create(placement,matching[0],row));
   }
   var sig=new JObject{["Lining"]=lining?.DeepClone(),["References"]=JArray.FromObject(references),["Overrides"]=p["wallTypeOverrides"]?.DeepClone(),["Document"]=doc.ProjectInformation.UniqueId,["View"]=view.UniqueId,["Cad"]=cad.UniqueId,["Base"]=bottom.UniqueId,["Top"]=top.UniqueId,["Z"]=z,["ZTop"]=zt,["Layer"]=layer,["Tolerance"]=tol,["Gap"]=gap,["Openings"]=JArray.FromObject(openings),["Lines"]=JArray.FromObject(lines),["Unsupported"]=JArray.FromObject(unsupported),["Rows"]=JArray.FromObject(rows),["Types"]=JArray.FromObject(types.Select(t=>new{Id=t.UniqueId,Width=t.Width})),["Phase"]=view.get_Parameter(BuiltInParameter.VIEW_PHASE)?.AsElementId()?.GetIdValue()};
   string token=PlanarFloorRegions.Hash(sig.ToString(Newtonsoft.Json.Formatting.None));bool committed=false,rolledBack=false;string error=null;
   if(apply&&!dry){if((string)p["previewToken"]!=token)throw new ArgumentException("Fresh matching previewToken required.");
    if(plans.Count>0)using(var group=new TransactionGroup(doc,"Create continuous CAD walls")){try{group.Start();foreach(var plan in plans){using(var tx=new Transaction(doc,"Create CAD wall")){try{tx.Start();tx.SetFailureHandlingOptions(tx.GetFailureHandlingOptions().SetFailuresPreprocessor(new FramingFloorFailures()).SetClearAfterRollback(true).SetForcedModalHandling(true));var s=plan.Item1.Axis;
      var wall=Wall.Create(doc,Line.CreateBound(new XYZ(s.X1/304.8,s.Y1/304.8,bottom.Elevation),new XYZ(s.X2/304.8,s.Y2/304.8,bottom.Elevation)),plan.Item2.Id,bottom.Id,zt-z,bo/304.8,false,lining==null);
      if(lining!=null){WallUtils.DisallowWallJoinAtEnd(wall,0);WallUtils.DisallowWallJoinAtEnd(wall,1);}
      if(!wall.get_Parameter(BuiltInParameter.WALL_HEIGHT_TYPE).Set(top.Id)||!wall.get_Parameter(BuiltInParameter.WALL_TOP_OFFSET).Set(to/304.8))throw new InvalidOperationException("Cannot set wall top constraint.");
      var phase=view.get_Parameter(BuiltInParameter.VIEW_PHASE)?.AsElementId();if(phase!=null&&phase!=ElementId.InvalidElementId)wall.get_Parameter(BuiltInParameter.PHASE_CREATED).Set(phase);
      doc.Regenerate();if(wall.GetTypeId()!=plan.Item2.Id||wall.get_Parameter(BuiltInParameter.WALL_BASE_CONSTRAINT).AsElementId()!=bottom.Id||wall.get_Parameter(BuiltInParameter.WALL_HEIGHT_TYPE).AsElementId()!=top.Id)throw new InvalidOperationException("Wall readback mismatch.");
      if(lining!=null)VerifyCadLiningWall(wall,plan.Item1,plan.Item3);
      if(tx.Commit()!=TransactionStatus.Committed)throw new InvalidOperationException("Wall transaction failed.");plan.Item3["Status"]="Created";plan.Item3["ElementId"]=wall.Id.GetIdValue();
     }catch(Exception ex){if(tx.GetStatus()==TransactionStatus.Started)tx.RollBack();if(!FramingFloorRecoverable(ex)||tx.GetStatus()!=TransactionStatus.RolledBack)throw;plan.Item3["Status"]="Failed";plan.Item3["Reason"]="CreationFailed";plan.Item3["Detail"]=ex.Message;}}}
     if(lining!=null){
      foreach(var plan in plans.Where(a=>(string)a.Item3["Status"]=="Created"))VerifyCadLiningWall((Wall)doc.GetElement(new ElementId(FramingFloorId(plan.Item3,"ElementId"))),plan.Item1,plan.Item3);
      foreach(var before in originalAxes){var after=(doc.GetElement(before.Key)?.Location as LocationCurve)?.Curve as Line;if(after==null||Enumerable.Range(0,2).Any(i=>after.GetEndPoint(i).DistanceTo(before.Value.GetEndPoint(i))*304.8>0.1))throw new InvalidOperationException("Existing wall moved; batch rolled back.");}
     }
     if(group.Assimilate()!=TransactionStatus.Committed)throw new InvalidOperationException("Group commit failed.");committed=true;
    }catch(Exception ex){error=ex.Message;if(group.GetStatus()==TransactionStatus.Started)group.RollBack();rolledBack=true;foreach(var plan in plans){plan.Item3.Remove("ElementId");plan.Item3["Status"]="Failed";plan.Item3["Reason"]="BatchRolledBack";}}}
   }
   return new{Success=error==null&&!rows.Any(r=>(string)r["Status"]=="Failed"),Error=error,DryRun=dry,Apply=apply,Committed=committed,RolledBack=rolledBack,PreviewToken=token,ViewId=view.Id.GetIdValue(),ImportInstanceUniqueId=cad.UniqueId,BaseLevel=bottom.Name,TopLevel=top.Name,BaseOffsetMm=bo,TopOffsetMm=to,Scanned=lines.Count,OpeningSegments=openings.Count,Detected=runs.Count,Planned=plans.Count,Created=rows.Count(r=>(string)r["Status"]=="Created"),Skipped=rows.Count(r=>(string)r["Status"]=="Skipped"),Failed=rows.Count(r=>(string)r["Status"]=="Failed"),TypeWarnings=typeWarnings,UnsupportedGeometry=unsupported,Results=rows,ByReason=rows.Where(r=>r["Reason"]!=null).GroupBy(r=>(string)r["Reason"]).Select(g=>new{Reason=g.Key,Count=g.Count()}),Notes=new[]{"Straight planar double-line walls only. Opening evidence required for gap bridging; no doors/windows created.","Existing wall overlaps skip the whole candidate. Ambiguous thickness/type matches skip. Preview is required before apply.",lining==null?"RC basic types matched by actual width; structural=true.":"Explicit lining type, structural=false; CAD interior face fixed, excess thickness toward named reference walls. Wall end joins disabled. Unmatched or ambiguous reference side skipped."}};
  }
  private void VerifyCadLiningWall(Wall wall,CadWallGeometry.Run run,JObject row){
   int sign=row["OutwardSign"].Value<int>();var inward=new XYZ(run.V*sign,-run.U*sign,0);
   double expected=-sign*row["InteriorPlaneMm"].Value<double>();
   if(Math.Abs(ResizeWallPlane(wall,inward)*304.8-expected)>0.1||Math.Abs(wall.Width*304.8-run.Width)>0.1||wall.get_Parameter(BuiltInParameter.WALL_STRUCTURAL_SIGNIFICANT).AsInteger()!=0)throw new InvalidOperationException("Lining interior face, width or structural flag verification failed.");
  }

 }
}
