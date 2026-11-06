using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
namespace RevitMCP.Core {
 public partial class CommandExecutor {
  private object ShapeFloorRamp(JObject args) {
   var doc=_uiApp.ActiveUIDocument.Document;var view=doc.ActiveView;
   if(!(view is ViewPlan))throw new ArgumentException("Use the plan view defining the right-hand low end.");
   var floor=doc.GetElement(new ElementId(FramingFloorId(args,"floorId"))) as Floor??throw new ArgumentException("Floor not found.");
   if(floor.Pinned||floor.GroupId!=ElementId.InvalidElementId||floor.DesignOption!=null)throw new ArgumentException("Pinned, grouped or design-option floors are unsupported.");
   var ids=args["pathElementIds"] as JArray;
   if(ids==null||ids.Count<1||ids.Count>20)throw new ArgumentException("Provide 1 to 20 path model-line IDs.");
   var curves=new List<Curve>();var pathElements=new List<Element>();
   foreach(var id in ids){var e=doc.GetElement(new ElementId((long)id)) as ModelCurve??throw new ArgumentException("Path must use model curves.");var curve=e.GeometryCurve;if(!(curve is Line)&&!(curve is Arc))throw new ArgumentException("Only line/arc paths supported.");if(curve.Tessellate().Any(p=>Math.Abs(p.Z-curve.GetEndPoint(0).Z)>1e-6))throw new ArgumentException("Horizontal path required.");curves.Add(curve);pathElements.Add(e);}
   if(pathElements.Select(e=>e.Id).Distinct().Count()!=curves.Count)throw new ArgumentException("Duplicate path IDs.");
   double low=FramingFloorNumber(args,"lowElevationMm",-7350,-1000000,1000000)/304.8;
   double high=FramingFloorNumber(args,"highElevationMm",-3850,-1000000,1000000)/304.8;
   double step=FramingFloorNumber(args,"spacingMm",1000,100,10000)/304.8;
   if(high<=low)throw new ArgumentException("High elevation must exceed low elevation.");
   var ends=curves.SelectMany(c=>new[]{c.GetEndPoint(0),c.GetEndPoint(1)}).ToList();
   var free=ends.Where(p=>ends.Count(q=>q.DistanceTo(p)<0.001/304.8)==1).ToList();
   if(free.Count!=2)throw new ArgumentException("Path must be a single connected open chain.");
   if(Math.Abs((free[0]-free[1]).DotProduct(view.RightDirection))<1/304.8)throw new ArgumentException("Right-hand endpoint is ambiguous.");
   var start=free.OrderByDescending(p=>p.DotProduct(view.RightDirection)).First();var ordered=new List<Curve>();var todo=curves.ToList();var next=start;
   while(todo.Count>0){var matches=todo.Where(c=>c.GetEndPoint(0).DistanceTo(next)<0.001/304.8||c.GetEndPoint(1).DistanceTo(next)<0.001/304.8).ToList();if(matches.Count!=1)throw new ArgumentException("Disconnected/branched path.");var c=matches[0];todo.Remove(c);if(c.GetEndPoint(0).DistanceTo(next)>0.001/304.8)c=c.CreateReversed();ordered.Add(c);next=c.GetEndPoint(1);}
   for(int i=1;i<ordered.Count;i++)if(ordered[i-1].ComputeDerivatives(1,true).BasisX.Normalize().DotProduct(ordered[i].ComputeDerivatives(0,true).BasisX.Normalize())<0.999)throw new ArgumentException("Path has a sharp corner; explicit corner cross-sections required.");
   double length=ordered.Sum(c=>c.Length);if(length/step>200)throw new ArgumentException("Too many stations.");
   var faces=FramingFloorSolids(floor.get_Geometry(new Options{DetailLevel=ViewDetailLevel.Fine})).SelectMany(s=>s.Faces.Cast<Face>()).OfType<PlanarFace>().Where(f=>f.FaceNormal.Z>0.999999).ToList();
   if(faces.Count!=1)throw new ArgumentException("Requires one flat unmodified top face.");var face=faces[0];double baseline=face.Origin.Z;
   if(Math.Abs(baseline-low)>0.1/304.8)throw new ArgumentException("Current floor top must match low elevation.");
   if(ordered.Any(c=>Math.Abs(c.GetEndPoint(0).Z-baseline)>0.1/304.8))throw new ArgumentException("Path must lie on initial floor top plane.");
   double originalLength=length,trimmedStart=0;Curve entryEdge=null;
   if(FramingFloorFlag(args,"trimStartToBoundary",false)) {
    var boundaryEdges=face.EdgeLoops.Cast<EdgeArray>().SelectMany(loop=>loop.Cast<Edge>()).Select(e=>e.AsCurve()).ToList();
    bool Inside(XYZ p){var hit=face.Project(p);return hit!=null && face.IsInside(hit.UVPoint);}
    var kept=new List<Curve>();bool entered=false;
    foreach(var c in ordered) {
     var cuts=new List<double>{0,1};
     foreach(var edge in boundaryEdges){IntersectionResultArray hits;c.Intersect(edge,out hits);if(hits!=null)foreach(IntersectionResult hit in hits){var pr=c.Project(hit.XYZPoint);double n=c.ComputeNormalizedParameter(pr.Parameter);if(n>1e-9&&n<1-1e-9)cuts.Add(n);}}
     cuts=cuts.OrderBy(x=>x).Aggregate(new List<double>(),(a,n)=>{if(a.Count==0||Math.Abs(n-a.Last())>1e-9)a.Add(n);return a;});
     for(int i=1;i<cuts.Count;i++) {
      double a=cuts[i-1],b=cuts[i];bool inside=Inside(c.Evaluate((a+b)/2,true));
      if(!inside){if(entered)throw new ArgumentException("Path exits the floor after entry; review path before shaping.");trimmedStart+=(b-a)*c.Length;continue;}
      if(!entered){entered=true;var entry=c.Evaluate(a,true);if(trimmedStart>0.001/304.8){var edges=boundaryEdges.Where(e=>e.Distance(entry)<0.01/304.8).ToList();if(edges.Count!=1||!(edges[0] is Line))throw new ArgumentException("Entry requires a unique straight floor end edge.");entryEdge=edges[0];}}
      var part=c.Clone();part.MakeBound(c.ComputeRawParameter(a),c.ComputeRawParameter(b));kept.Add(part);
     }
    }
    if(!entered||kept.Count==0)throw new ArgumentException("Path never enters floor.");
    ordered=kept;length=ordered.Sum(c=>c.Length);
    if(length<step)throw new ArgumentException("Effective ramp path is shorter than one station spacing.");
   }
   var editor=floor.GetSlabShapeEditor();if(editor==null)throw new ArgumentException("Floor does not support shape editing.");
   if(editor.IsEnabled && editor.SlabShapeVertices.Cast<SlabShapeVertex>().Any(v=>Math.Abs(v.Position.Z-baseline)>0.1/304.8))throw new ArgumentException("Existing non-flat shape editing detected; do not overwrite it.");
   var stations=new List<double>{0};for(double d=step;d<length-0.001/304.8;d+=step)stations.Add(d);stations.Add(length);
   var sections=new List<Tuple<double,XYZ,XYZ>>();
   foreach(double distance in stations){
    if(distance==0 && entryEdge!=null){sections.Add(Tuple.Create(0.0,entryEdge.GetEndPoint(0),entryEdge.GetEndPoint(1)));continue;}
    double rem=distance;Curve segment=ordered.Last();foreach(var c in ordered){segment=c;if(rem<=c.Length)break;rem-=c.Length;}double ratio=Math.Min(1,rem/segment.Length);var centerRaw=segment.Evaluate(ratio,true);var center=new XYZ(centerRaw.X,centerRaw.Y,baseline);var tangent=segment.ComputeDerivatives(ratio,true).BasisX.Normalize();var normal=new XYZ(-tangent.Y,tangent.X,0);var line=Line.CreateBound(center-normal*10000,center+normal*10000);var points=new List<XYZ>();
    foreach(EdgeArray loop in face.EdgeLoops)foreach(Edge edge in loop){var boundary=edge.AsCurve();
     // Curve.Intersect does not enumerate endpoints of collinear overlapping curves.
     foreach(var endpoint in new[]{boundary.GetEndPoint(0),boundary.GetEndPoint(1)})if(line.Distance(endpoint)<0.001/304.8 && !points.Any(q=>q.DistanceTo(endpoint)<0.001/304.8))points.Add(endpoint);
     IntersectionResultArray hits;boundary.Intersect(line,out hits);if(hits!=null)foreach(IntersectionResult hit in hits){var p=hit.XYZPoint;if(!points.Any(q=>q.DistanceTo(p)<0.001/304.8))points.Add(p);}}
    points=points.OrderBy(p=>(p-center).DotProduct(normal)).ToList();var intervals=new List<Tuple<XYZ,XYZ>>();for(int i=1;i<points.Count;i++){var mid=(points[i-1]+points[i])/2;var projection=face.Project(mid);if(projection!=null&&(face.IsInside(projection.UVPoint)||face.EdgeLoops.Cast<EdgeArray>().SelectMany(loop=>loop.Cast<Edge>()).Any(edge=>edge.AsCurve().Distance(mid)<0.001/304.8))&&((points[i-1]-center).DotProduct(normal)<=0.001/304.8)&&((points[i]-center).DotProduct(normal)>=-0.001/304.8))intervals.Add(Tuple.Create(points[i-1],points[i]));}
    if(intervals.Count!=1)return new {Success=false,Committed=false,Error="No unique floor cross-section",StationMm=distance*304.8,Center=RampPoint(center),Tangent=RampPoint(tangent),Intersections=new JArray(points.Select(RampPoint)),IntervalCount=intervals.Count,
     Boundary=new JArray(face.EdgeLoops.Cast<EdgeArray>().Select(loop=>new JArray(loop.Cast<Edge>().Select(edge=>new JObject{["CurveType"]=edge.AsCurve().GetType().Name,["Points"]=new JArray(edge.AsCurve().Tessellate().Select(RampPoint))}))))};
    sections.Add(Tuple.Create(distance,intervals[0].Item1,intervals[0].Item2));
   }
   for(int i=0;i<sections.Count;i++)for(int j=i+1;j<sections.Count;j++){IntersectionResultArray hits;var a=sections[i];var b=sections[j];if(Line.CreateBound(a.Item2,a.Item3).Intersect(Line.CreateBound(b.Item2,b.Item3),out hits)!=SetComparisonResult.Disjoint)throw new ArgumentException("Cross-sections intersect; cannot assign distinct elevations safely.");}
   double Station(XYZ p){double best=double.MaxValue,result=0,offset=0;foreach(var c in ordered){var candidates=new List<Tuple<XYZ,double>>{Tuple.Create(c.GetEndPoint(0),0.0),Tuple.Create(c.GetEndPoint(1),c.Length)};var projection=c.Project(new XYZ(p.X,p.Y,baseline));if(projection!=null){double n=c.ComputeNormalizedParameter(projection.Parameter);if(n>=0&&n<=1)candidates.Add(Tuple.Create(projection.XYZPoint,n*c.Length));}foreach(var q in candidates){double dist=new XYZ(p.X,p.Y,baseline).DistanceTo(q.Item1);if(dist<best){best=dist;result=offset+q.Item2;}}offset+=c.Length;}return Math.Max(0,Math.Min(length,result));}
   var lines=new JArray(sections.Select(s=>new JObject{["StationMm"]=s.Item1*304.8,["ElevationMm"]=(low+(high-low)*s.Item1/length)*304.8,["Start"]=RampPoint(s.Item2),["End"]=RampPoint(s.Item3)}));
   var boundarySignature=new JArray(face.EdgeLoops.Cast<EdgeArray>().SelectMany(loop=>loop.Cast<Edge>()).Select(e=>new JArray(e.AsCurve().Tessellate().Select(RampPoint)))).ToString(Newtonsoft.Json.Formatting.None);
   var shapeSignature=new JArray(editor.SlabShapeVertices.Cast<SlabShapeVertex>().Select(v=>RampPoint(v.Position))).ToString(Newtonsoft.Json.Formatting.None);
   string token=PlanarFloorRegions.Hash(shapeSignature+boundarySignature+doc.ProjectInformation.UniqueId+floor.UniqueId+view.UniqueId+floor.GetTypeId()+face.Area.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+lines.ToString(Newtonsoft.Json.Formatting.None));
   bool dryRun=FramingFloorFlag(args,"dryRun",true),apply=FramingFloorFlag(args,"apply",false),committed=false;int changed=0;string error=null,stage="Preview",errorType=null;bool rolledBack=false;
   if(apply&&!dryRun){if((string)args["previewToken"]!=token)throw new ArgumentException("Matching previewToken required.");using(var tx=new Transaction(doc,"Shape ramp floor by path stations")){try{stage="StartTransaction";tx.Start();tx.SetFailureHandlingOptions(tx.GetFailureHandlingOptions().SetFailuresPreprocessor(new FramingFloorFailures()).SetClearAfterRollback(true).SetForcedModalHandling(true));stage="EnableEditor";if(!editor.IsEnabled)editor.Enable();doc.Regenerate();
     SlabShapeVertex Vertex(XYZ point){foreach(SlabShapeVertex v in editor.SlabShapeVertices)if(v.Position.DistanceTo(point)<0.01/304.8)return v;var method=typeof(SlabShapeEditor).GetMethod("AddPoint",new[]{typeof(XYZ)})??typeof(SlabShapeEditor).GetMethod("DrawPoint",new[]{typeof(XYZ)});return (SlabShapeVertex)method.Invoke(editor,new object[]{point})??throw new InvalidOperationException("Could not add boundary vertex.");}
     var splitMethod=typeof(SlabShapeEditor).GetMethod("AddSplitLine",new[]{typeof(SlabShapeVertex),typeof(SlabShapeVertex)})??typeof(SlabShapeEditor).GetMethod("DrawSplitLine",new[]{typeof(SlabShapeVertex),typeof(SlabShapeVertex)});
     foreach(var s in sections){
      stage="InspectExistingCreases at "+(s.Item1*304.8).ToString("F3",System.Globalization.CultureInfo.InvariantCulture)+" mm";
      bool present=editor.SlabShapeCreases.Cast<SlabShapeCrease>().Any(cr=>RampHasExactCrease(cr,s.Item2,s.Item3));
      if(!present){stage="AddStartVertex at "+s.Item1*304.8;var a=Vertex(s.Item2);stage="AddEndVertex at "+s.Item1*304.8;var b=Vertex(s.Item3);stage="AddSplitLine at "+s.Item1*304.8;splitMethod.Invoke(editor,new object[]{a,b});}
     }doc.Regenerate();
     stage="CalculateVertexElevations";var expected=new List<Tuple<SlabShapeVertex,double>>();foreach(SlabShapeVertex v in editor.SlabShapeVertices){double station=Station(v.Position);foreach(var s in sections){var l=Line.CreateBound(s.Item2,s.Item3);if(l.Distance(new XYZ(v.Position.X,v.Position.Y,baseline))<0.01/304.8){station=s.Item1;break;}}double z=low+(high-low)*station/length;expected.Add(Tuple.Create(v,z));}
     foreach(var v in expected){stage="ModifyVertex "+changed;editor.ModifySubElement(v.Item1,v.Item2-baseline);changed++;}doc.Regenerate();foreach(var v in expected)if(Math.Abs(v.Item1.Position.Z-v.Item2)>0.1/304.8)throw new InvalidOperationException("Vertex elevation readback failed.");
     stage="Commit";if(tx.Commit()!=TransactionStatus.Committed)throw new InvalidOperationException("Ramp transaction failed.");committed=true;
    }catch(Exception ex){if(tx.GetStatus()==TransactionStatus.Started)tx.RollBack();changed=0;rolledBack=tx.GetStatus()==TransactionStatus.RolledBack;var cause=ex.InnerException??ex;errorType=cause.GetType().FullName;error=stage+": "+errorType+" "+cause.Message+" | "+cause.StackTrace;}}}
   return new{Success=error==null,Error=error,DryRun=dryRun,Apply=apply,Committed=committed,RolledBack=rolledBack,Stage=stage,ErrorType=errorType,FloorId=floor.Id.GetIdValue(),OriginalPathLengthMm=originalLength*304.8,TrimmedStartMm=trimmedStart*304.8,PathLengthMm=length*304.8,LowElevationMm=low*304.8,HighElevationMm=high*304.8,RisePerMetreMm=(high-low)/length*1000,SplitLineCount=sections.Count,ModifiedVertices=changed,PreviewToken=token,Sections=lines};
  }
  private static bool RampHasExactCrease(SlabShapeCrease crease,XYZ start,XYZ end) {
   // Revit-generated internal creases can throw from Curve after DrawSplitLine.
   // They are not usable evidence of an existing explicit straight section.
   Curve curve;
   try { curve=crease.Curve; }
   catch(Autodesk.Revit.Exceptions.InvalidOperationException) { return false; }
   if(!(curve is Line))return false;
   var a=curve.GetEndPoint(0);var b=curve.GetEndPoint(1);double tolerance=0.01/304.8;
   return (a.DistanceTo(start)<tolerance&&b.DistanceTo(end)<tolerance)||(b.DistanceTo(start)<tolerance&&a.DistanceTo(end)<tolerance);
  }
  private static JObject RampPoint(XYZ p)=>new JObject{["x"]=p.X*304.8,["y"]=p.Y*304.8,["z"]=p.Z*304.8};
 }
}
