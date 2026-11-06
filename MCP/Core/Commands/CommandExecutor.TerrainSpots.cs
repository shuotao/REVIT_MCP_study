#if REVIT2024_OR_GREATER
using System;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using NetTopologySuite.Geometries;
namespace RevitMCP.Core
{
 public partial class CommandExecutor
 {
  private object UpdateTerrainSpots(JObject p,JObject options)
  {
   if(p["excavation"]!=null)throw new ArgumentException("Choose one terrain operation per call.");
   var doc=_uiApp.ActiveUIDocument.Document;
   var terrain=doc.GetElement(new ElementId(options.Value<long>("toposolidId"))) as Toposolid??throw new ArgumentException("Terrain required.");
   var cad=doc.GetElement(new ElementId(p.Value<long>("cadId"))) as ImportInstance??throw new ArgumentException("CAD required.");
   var level=doc.GetElement(new ElementId(p.Value<long>("levelId"))) as Level??throw new ArgumentException("Level required.");
   if(terrain.Pinned||terrain.GroupId!=ElementId.InvalidElementId)throw new ArgumentException("Pinned/grouped terrain unsupported.");
   if(terrain.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.AsString()!="AbbyTerrain:"+cad.UniqueId)throw new ArgumentException("Terrain/CAD source mismatch.");
   string path=options.Value<string>("sourceDxfPath"),layer=options.Value<string>("layer")??"L01",tag=options.Value<string>("attributeTag")??"HE";
   var source=TerrainSpotDxf.Read(path,layer,tag);double datum=p.Value<double?>("surveyZeroMeters")??246.69;
   double unit=options.Value<double?>("sourceUnitMm")??1000;if(unit<=0||double.IsInfinity(unit)||double.IsNaN(unit)||double.IsInfinity(datum)||double.IsNaN(datum))throw new ArgumentException("Invalid units/datum.");
   var transform=cad.GetTotalTransform();
   var cadGeometry=cad.get_Geometry(new Options { IncludeNonVisibleObjects=true });
   var contourLines=new List<List<XYZ>>();TerrainLines(doc,cadGeometry,Transform.Identity,null,new HashSet<string>{"AC","IDX"},contourLines);
   var contourPoints=contourLines.SelectMany(l=>l).ToList();if(contourPoints.Count==0)throw new ArgumentException("CAD contours missing.");
   double DistanceToContours(XYZ point){double best=double.MaxValue;foreach(var line in contourLines)for(int i=1;i<line.Count;i++){var a=line[i-1];var d=line[i]-a;double length2=d.DotProduct(d);double t=length2<1e-12?0:Math.Max(0,Math.Min(1,(point-a).DotProduct(d)/length2));best=Math.Min(best,point.DistanceTo(a+d*t));}return best;}
   var anchors=source.ContourPoints.Where((v,i)=>i%Math.Max(1,source.ContourPoints.Count/20)==0).ToList();
   var transforms=new List<Tuple<string,Transform>>{Tuple.Create("TotalTransform",cad.GetTotalTransform()),Tuple.Create("InstanceTransform",cad.GetTransform())};
   transforms.AddRange(cadGeometry.Cast<GeometryObject>().OfType<GeometryInstance>().Select((g,i)=>Tuple.Create("GeometryTransform"+i,g.Transform)));
   var checks=new JArray();var matches=new List<Tuple<Transform,double,string>>();
   foreach(var candidate in transforms)
   foreach(var scale in new[]{unit/304.8,unit/304.8/candidate.Item2.Scale}.Distinct())
   {
    var errors=anchors.Select(raw=>{var world=candidate.Item2.OfPoint(new XYZ(raw[0]*scale,raw[1]*scale,raw[2]*scale));return new {Raw=raw,World=world,Distance=DistanceToContours(world)*304.8};}).ToList();
    double maxError=errors.Max(v=>v.Distance);var worst=errors.OrderByDescending(v=>v.Distance).First();
    checks.Add(new JObject{["Transform"]=candidate.Item1,["TransformScale"]=candidate.Item2.Scale,["SourceToLocalScale"]=scale,["MaxErrorMm"]=maxError,["WorstSource"]=JArray.FromObject(worst.Raw),["WorstWorldMm"]=JArray.FromObject(new[]{worst.World.X*304.8,worst.World.Y*304.8,worst.World.Z*304.8})});
    if(maxError<=5)matches.Add(Tuple.Create(candidate.Item2,scale,candidate.Item1));
   }
   if(options.Value<bool?>("alignX4Y7")==true)
   {
    if(source.GridControls.Count!=1||!source.GridLabels.Contains("X4")||!source.GridLabels.Contains("Y7"))throw new ArgumentException("Unique source grid control and X4/Y7 labels required.");
    var grids=new FilteredElementCollector(doc).OfClass(typeof(Grid)).Cast<Grid>().ToList();
    var gx=grids.Single(g=>g.Name=="X4").Curve as Line;var gy=grids.Single(g=>g.Name=="Y7").Curve as Line;
    if(gx==null||gy==null||Math.Abs(gx.Direction.Y)>1e-6||Math.Abs(gy.Direction.X)>1e-6)throw new ArgumentException("This control requires horizontal X4 and vertical Y7.");
    var control=source.GridControls[0];double angle=(90-control[2])*Math.PI/180;var mapping=Transform.CreateRotation(XYZ.BasisZ,angle);
    var target=new XYZ(gy.GetEndPoint(0).X,gx.GetEndPoint(0).Y,0);var origin=new XYZ(control[0]*unit/304.8,control[1]*unit/304.8,0);
    mapping.Origin=target-mapping.OfVector(origin);matches.Clear();matches.Add(Tuple.Create(mapping,unit/304.8,"ExplicitX4Y7GridControl"));
    checks.Add(new JObject{["Transform"]="ExplicitX4Y7GridControl",["RotationDegrees"]=angle*180/Math.PI,["TargetXmm"]=target.X*304.8,["TargetYmm"]=target.Y*304.8,["SourceControl"]=JArray.FromObject(control)});
   }
   if(matches.Count==0){if(p.Value<bool?>("apply")==true&&p.Value<bool?>("dryRun")==false)throw new InvalidOperationException("DXF/CAD alignment mismatch; no changes made.");return new JObject{["Applied"]=false,["Ready"]=false,["Reason"]="DXF/CAD alignment mismatch",["AlignmentChecks"]=checks,["CadContourCount"]=contourLines.Count,["CadContourPoints"]=contourPoints.Count};}
   var matched=matches[0];transform=matched.Item1;double coordinateScale=matched.Item2;
   foreach(var other in matches.Skip(1))foreach(var raw in anchors){var a=transform.OfPoint(new XYZ(raw[0]*coordinateScale,raw[1]*coordinateScale,raw[2]*coordinateScale));var b=other.Item1.OfPoint(new XYZ(raw[0]*other.Item2,raw[1]*other.Item2,raw[2]*other.Item2));if(a.DistanceTo(b)>1/304.8)throw new InvalidOperationException("Ambiguous CAD coordinate mapping.");}
   var editor=terrain.GetSlabShapeEditor();var original=editor.SlabShapeVertices.Cast<SlabShapeVertex>().Select(v=>v.Position).ToList();
   var gf=new GeometryFactory();var hull=gf.CreateMultiPointFromCoords(original.Select(v=>new Coordinate(v.X,v.Y)).ToArray()).ConvexHull();
   var triangles=new List<XYZ[]>();foreach(var s in ExcavationSolids(terrain))foreach(Face f in s.Faces){var mesh=f.Triangulate();for(int i=0;i<mesh.NumTriangles;i++){var t=mesh.get_Triangle(i);triangles.Add(new[]{t.get_Vertex(0),t.get_Vertex(1),t.get_Vertex(2)});}}
   double? Height(XYZ point){double? z=null;foreach(var t in triangles){var a=t[0];var b=t[1];var c=t[2];double den=(b.Y-c.Y)*(a.X-c.X)+(c.X-b.X)*(a.Y-c.Y);if(Math.Abs(den)<1e-10)continue;double u=((b.Y-c.Y)*(point.X-c.X)+(c.X-b.X)*(point.Y-c.Y))/den,v=((c.Y-a.Y)*(point.X-c.X)+(a.X-c.X)*(point.Y-c.Y))/den,w=1-u-v;if(u< -1e-8||v< -1e-8||w< -1e-8)continue;double h=u*a.Z+v*b.Z+w*c.Z;if(!z.HasValue||h>z.Value)z=h;}return z;}
   var rows=new JArray();var planned=new List<XYZ>();var seen=new Dictionary<string,double>();
   foreach(var spot in source.Points)
   {
    var xy=transform.OfPoint(new XYZ(spot.X*coordinateScale,spot.Y*coordinateScale,0));var target=new XYZ(xy.X,xy.Y,level.Elevation+(spot.Elevation-datum)*1000/304.8);
    string status="Planned",key=Math.Round(xy.X*3048)+":"+Math.Round(xy.Y*3048);
    var near=original.Where(v=>new XYZ(v.X,v.Y,0).DistanceTo(new XYZ(xy.X,xy.Y,0))<1/304.8).ToList();
    if(!hull.Covers(gf.CreatePoint(new Coordinate(xy.X,xy.Y))))status="OutsideExistingBoundary";
    else if(near.Count>0)status=near.All(v=>Math.Abs(v.Z-target.Z)<1/304.8)?"Unchanged":"ConflictsWithExistingPoint";
    else if(seen.TryGetValue(key,out double z)){if(Math.Abs(z-target.Z)>1/304.8)throw new ArgumentException("Conflicting L01 points at same location.");status="Duplicate";}
    else {seen[key]=target.Z;planned.Add(target);}
    var height=Height(target);rows.Add(new JObject{["Handle"]=spot.Handle,["Text"]=spot.Text,["Xmm"]=target.X*304.8,["Ymm"]=target.Y*304.8,["Zmm"]=target.Z*304.8,["CurrentSurfaceZmm"]=height.HasValue?(JToken)(height.Value*304.8):JValue.CreateNull(),["DeltaMm"]=height.HasValue?(JToken)((target.Z-height.Value)*304.8):JValue.CreateNull(),["Status"]=status});
   }
   var cutters=InstanceVoidCutUtils.GetCuttingVoidInstances(terrain).ToList();
   var cutPlanes=new Dictionary<long,Tuple<double,double>>();
   double AreaAt(double z)=>ExcavationSolids(terrain).SelectMany(s=>s.Faces.Cast<Face>()).OfType<PlanarFace>().Where(f=>f.FaceNormal.Z>.99&&Math.Abs(f.Origin.Z-z)<1/304.8).Sum(f=>f.Area);
   foreach(var id in cutters){var instance=doc.GetElement(id);if(instance.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.AsString()!="AbbyExcavation:"+terrain.UniqueId||!(instance.Location is LocationPoint location))throw new InvalidOperationException("Unsupported external cut; cannot guarantee preservation.");double z=location.Point.Z;cutPlanes[id.Value]=Tuple.Create(z,AreaAt(z));if(planned.Any(v=>v.Z<=z))throw new InvalidOperationException("Survey point falls below excavation bottom.");}
   var result=new JObject{["TerrainId"]=terrain.Id.Value,["SourceAttributes"]=source.Points.Count+source.Excluded.Count,["ExcludedParentheses"]=JArray.FromObject(source.Excluded),["Planned"]=planned.Count,["Rows"]=rows,["PreservedCutters"]=JArray.FromObject(cutters.Select(id=>id.Value)),["Applied"]=false};
   result["AlignmentChecks"]=checks;result["CoordinateTransform"]=matched.Item3;result["SourceToLocalScale"]=coordinateScale;
   string signature=result.ToString()+File.GetLastWriteTimeUtc(path).Ticks+terrain.GetTypeId()+string.Join(";",original.Select(v=>v.ToString()));string token;
   using(var sha=SHA256.Create())token=BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(signature))).Replace("-","");result["PreviewToken"]=token;
   if(p.Value<bool?>("apply")!=true||p.Value<bool?>("dryRun")!=false)return result;
   if(p.Value<string>("previewToken")!=token)throw new ArgumentException("Fresh preview token required.");
   if(planned.Count==0)return result;
   var offsets=terrain.Parameters.Cast<Parameter>().Where(v=>v.StorageType==StorageType.Double&&(v.Definition.Name=="距樓層的高度偏移"||v.Definition.Name=="Height Offset From Level")).ToList();
   if(offsets.Count!=1)throw new InvalidOperationException("Cannot identify terrain base offset unambiguously.");
   var failures=new TerrainFailures();double baseline=((Level)doc.GetElement(terrain.LevelId)).Elevation+offsets[0].AsDouble();
   using(var group=new TransactionGroup(doc,"Abby add surveyed terrain elevations"))
   {group.Start();try{
    using(var tx=new Transaction(doc,"Add L01 survey points and preserve excavation"))
    {tx.Start();tx.SetFailureHandlingOptions(tx.GetFailureHandlingOptions().SetFailuresPreprocessor(failures).SetClearAfterRollback(true));
     foreach(var id in cutters)InstanceVoidCutUtils.RemoveInstanceVoidCut(doc,terrain,doc.GetElement(id));doc.Regenerate();editor=terrain.GetSlabShapeEditor();
     if(!editor.IsEnabled)editor.Enable();
     var method=typeof(SlabShapeEditor).GetMethod("AddPoint",new[]{typeof(XYZ)})??typeof(SlabShapeEditor).GetMethod("DrawPoint",new[]{typeof(XYZ)});
     foreach(var point in planned){var vertex=(SlabShapeVertex)method.Invoke(editor,new object[]{new XYZ(point.X,point.Y,baseline)});if(vertex==null)throw new InvalidOperationException("Cannot add survey point.");editor.ModifySubElement(vertex,point.Z-baseline);}
     doc.Regenerate();var now=editor.SlabShapeVertices.Cast<SlabShapeVertex>().Select(v=>v.Position).ToList();
     if(original.Any(v=>!now.Any(n=>n.DistanceTo(v)<1/304.8))||planned.Any(v=>!now.Any(n=>n.DistanceTo(v)<1/304.8)))throw new InvalidOperationException("Point elevations verification failed; rollback.");
     foreach(var id in cutters)InstanceVoidCutUtils.AddInstanceVoidCut(doc,terrain,doc.GetElement(id));
     if(tx.Commit()!=TransactionStatus.Committed)throw new InvalidOperationException("Terrain update rolled back: "+string.Join(";",failures.Messages));
    }
    if(cutters.Any(id=>!InstanceVoidCutUtils.InstanceVoidCutExists(terrain,doc.GetElement(id))))throw new InvalidOperationException("Excavation relationship lost; rollback.");
    if(cutPlanes.Values.Any(v=>Math.Abs(AreaAt(v.Item1)-v.Item2)*0.09290304>0.01))throw new InvalidOperationException("Excavation bottom area changed; rollback.");
    if(group.Assimilate()!=TransactionStatus.Committed)throw new InvalidOperationException("Commit failed.");result["Applied"]=true;result["Updated"]=planned.Count;result["Warnings"]=JArray.FromObject(failures.Messages);
   }catch{if(group.GetStatus()==TransactionStatus.Started)group.RollBack();throw;}}
   return result;
  }
 }
}
#endif
