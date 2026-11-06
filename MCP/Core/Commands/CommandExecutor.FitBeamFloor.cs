using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
#if REVIT2025_OR_GREATER
using FitId = System.Int64;
#else
using FitId = System.Int32;
#endif
namespace RevitMCP.Core {
 public partial class CommandExecutor {
  private static double FitSurfaceZ(Element e,double x,double y,bool bottom) {
   return FitTrySurfaceZ(e,x,y,bottom)??throw new InvalidOperationException($"Surface coverage missing: element {e.Id.GetIdValue()}, X={x*304.8:F3} mm, Y={y*304.8:F3} mm, bottom={bottom}.");
  }
  private static double? FitTrySurfaceZ(Element e,double x,double y,bool bottom) {
   var bb=e.get_BoundingBox(null);var line=Line.CreateBound(new XYZ(x,y,bb.Min.Z-10),new XYZ(x,y,bb.Max.Z+10));
   var hits=new List<double>();
   foreach(var solid in FramingFloorSolids(e.get_Geometry(new Options{DetailLevel=ViewDetailLevel.Fine})))
    foreach(Face face in solid.Faces) {
     IntersectionResultArray intersections;
     if(face.Intersect(line,out intersections)!=SetComparisonResult.Overlap||intersections==null)continue;
     foreach(IntersectionResult hit in intersections) {
      var normal=face.ComputeNormal(hit.UVPoint);
      if(bottom?normal.Z < -0.1:normal.Z > 0.1)hits.Add(hit.XYZPoint.Z);
     }
    }
   if(hits.Count==0)return null;
   return bottom?hits.Min():hits.Max();
  }
  private object FitBeamToFloor(JObject p) {
   var doc=_uiApp.ActiveUIDocument.Document;
   var beam=doc.GetElement(new ElementId((FitId)p["beamId"].Value<long>())) as FamilyInstance;
   var floor=doc.GetElement(new ElementId((FitId)p["floorId"].Value<long>())) as Floor;
   if(beam==null||floor==null||beam.StructuralType!=Autodesk.Revit.DB.Structure.StructuralType.Beam)throw new ArgumentException("Select one structural beam and one floor.");
   if(beam.Pinned||beam.GroupId!=ElementId.InvalidElementId)throw new ArgumentException("Pinned/grouped beam is unsupported.");
   var location=beam.Location as LocationCurve;var line=location?.Curve as Line;
   if(line==null)throw new ArgumentException("Only straight beams are supported.");
   var a=line.GetEndPoint(0);var b=line.GetEndPoint(1);var d=new XYZ(b.X-a.X,b.Y-a.Y,0).Normalize();var side=new XYZ(-d.Y,d.X,0);
   var level=doc.GetElement(beam.get_Parameter(BuiltInParameter.INSTANCE_REFERENCE_LEVEL_PARAM).AsElementId()) as Level;
   if(level==null)throw new ArgumentException("Beam reference level unavailable.");
   if(beam.get_Parameter(BuiltInParameter.Z_JUSTIFICATION)?.AsInteger()!=0)throw new ArgumentException("Beam must use top justification.");
   var solids=FramingFloorSolids(beam.get_Geometry(new Options{DetailLevel=ViewDetailLevel.Fine})).ToList();
   var vertices=new List<XYZ>();foreach(var s in solids)foreach(Face f in s.Faces)vertices.AddRange(f.Triangulate().Vertices);
   if(vertices.Count==0)throw new InvalidOperationException("Beam solid missing.");
   double left=vertices.Min(v=>(v-a).DotProduct(side)),right=vertices.Max(v=>(v-a).DotProduct(side));
   double mid=(left+right)/2;
   string targetSurface=(string)p["targetSurface"]??"bottom";
   if(targetSurface!="bottom"&&targetSurface!="top")throw new ArgumentException("targetSurface must be bottom or top.");
   bool targetBottom=targetSurface=="bottom";
   bool edgeMode=p["extendAlongEdge"]?.Value<bool>()??false;
   bool extend=edgeMode||(p["extendOverhang"]?.Value<bool>()??false);
   JObject edgeReport=null;
   double z0=0,z1=0;int missingSamples=0;
   var samples=new List<XYZ>();var observations=new List<Tuple<double,double>>();
   if(!extend){z0=FitSurfaceZ(floor,a.X+side.X*mid,a.Y+side.Y*mid,targetBottom);z1=FitSurfaceZ(floor,b.X+side.X*mid,b.Y+side.Y*mid,targetBottom);}
   if(edgeMode){
    var edge=FitRampEdge(floor,a,b,d,side,left,right,targetBottom);
    z0=edge.Item1;z1=edge.Item2;edgeReport=edge.Item3;
   }
   for(int i=0;i<=40;i++) {
    int hits=0;
    var laterals=extend?Enumerable.Range(0,9).Select(j=>left+0.001+(right-left-0.002)*j/8.0):new[]{left+0.001,mid,right-0.001}.AsEnumerable();
    foreach(double lateral in laterals){
     double t=i/40.0;var xy=a+(b-a)*t+side*lateral;
     double? z=FitTrySurfaceZ(floor,xy.X,xy.Y,targetBottom);
     if(!z.HasValue){if(!extend)throw new InvalidOperationException("Floor does not cover full beam width.");missingSamples++;continue;}
     hits++;observations.Add(Tuple.Create(t,z.Value));samples.Add(new XYZ(xy.X,xy.Y,z.Value));
    }
    if(extend&&!edgeMode&&hits<3)throw new InvalidOperationException("Insufficient overlap at a longitudinal station; at least three lateral samples required. No longitudinal gap extrapolation.");
   }
   if(extend&&!edgeMode){
    var fit=RampOverlapFit.Fit(observations,1.0/304.8);z0=fit.Item1;z1=fit.Item2;
   }
   foreach(var item in observations)if(Math.Abs(item.Item2-(z0+(z1-z0)*item.Item1))*304.8>1)throw new InvalidOperationException("Overlap underside is not a single longitudinal plane within 1 mm; straight beam cannot fit.");
   var token=beam.UniqueId+"|"+floor.UniqueId+"|"+line.ToString()+"|"+edgeReport?.ToString(Newtonsoft.Json.Formatting.None)+"|"+targetSurface+"|"+extend+"|"+left.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+"|"+right.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+"|"+string.Join(",",samples.Select(v=>v.ToString()+v.Z.ToString("R",System.Globalization.CultureInfo.InvariantCulture)));
   bool apply=p["apply"]?.Value<bool>()??false,dry=p["dryRun"]?.Value<bool>()??true;
   if(apply&&!dry&&(string)p["previewToken"]!=token)throw new ArgumentException("Fresh preview token required.");
   bool committed=false;string error=null;double maxError=0;int verifiedSamples=0,overlapVerified=0,extendedVerified=0;
   using(var tx=new Transaction(doc,"Fit beam top to ramp "+targetSurface)) {
    try {
     tx.Start();tx.SetFailureHandlingOptions(tx.GetFailureHandlingOptions().SetFailuresPreprocessor(new FramingFloorFailures()).SetClearAfterRollback(true));
     foreach(var id in JoinGeometryUtils.GetJoinedElements(doc,beam).ToList())JoinGeometryUtils.UnjoinGeometry(doc,beam,doc.GetElement(id));
     Autodesk.Revit.DB.Structure.StructuralFramingUtils.DisallowJoinAtEnd(beam,0);
     Autodesk.Revit.DB.Structure.StructuralFramingUtils.DisallowJoinAtEnd(beam,1);
     AdditionSet(beam,BuiltInParameter.Z_OFFSET_VALUE,0);
     AdditionSet(beam,BuiltInParameter.STRUCTURAL_BEAM_END0_ELEVATION,z0-level.Elevation);
     AdditionSet(beam,BuiltInParameter.STRUCTURAL_BEAM_END1_ELEVATION,z1-level.Elevation);
     doc.Regenerate();
     var now=(beam.Location as LocationCurve).Curve;
     for(int end=0;end<2;end++){var expected=end==0?a:b;var actual=now.GetEndPoint(end);if(Math.Abs(actual.X-expected.X)>1e-6||Math.Abs(actual.Y-expected.Y)>1e-6)throw new InvalidOperationException("Beam plan position changed.");}
     // Read the regenerated beam's actual top faces. Never cast onto its old XY envelope.
     var slope=(z1-z0)/new XYZ(b.X-a.X,b.Y-a.Y,0).GetLength();
     var expectedNormal=new XYZ(-d.X*slope,-d.Y*slope,1).Normalize();
     foreach(var solid in FramingFloorSolids(beam.get_Geometry(new Options{DetailLevel=ViewDetailLevel.Fine}))) {
      foreach(Face face in solid.Faces) {
       if(!(face is PlanarFace planar)||planar.FaceNormal.Normalize().DotProduct(expectedNormal)<0.999999)continue;
       var mesh=face.Triangulate();
       for(int ti=0;ti<mesh.NumTriangles;ti++) {
        var triangle=mesh.get_Triangle(ti);var v0=triangle.get_Vertex(0);var v1=triangle.get_Vertex(1);var v2=triangle.get_Vertex(2);
        double edge=Math.Max(v0.DistanceTo(v1),Math.Max(v1.DistanceTo(v2),v2.DistanceTo(v0)));
        if((v1-v0).CrossProduct(v2-v0).GetLength()<1e-12)continue;
        int divisions=Math.Max(2,(int)Math.Ceiling(edge*304.8/250));
        if(divisions>500)throw new InvalidOperationException("Top-face sampling complexity limit exceeded.");
        for(int u=0;u<=divisions;u++)for(int v=0;v<=divisions-u;v++) {
         // Positive barycentric weights stay inside each actual triangle, including near edges.
         double denominator=divisions+0.003;
         double w0=(u+0.001)/denominator,w1=(v+0.001)/denominator,w2=(divisions-u-v+0.001)/denominator;
         var q=v0*w0+v1*w1+v2*w2;
         double? actualUnderside=FitTrySurfaceZ(floor,q.X,q.Y,targetBottom);
         double underside;
         if(actualUnderside.HasValue){underside=actualUnderside.Value;overlapVerified++;}
         else if(extend){double t=(q-a).DotProduct(d)/new XYZ(b.X-a.X,b.Y-a.Y,0).GetLength();underside=z0+(z1-z0)*t;extendedVerified++;}
         else throw new InvalidOperationException("Regenerated beam top extends beyond floor coverage.");
         maxError=Math.Max(maxError,Math.Abs(q.Z-underside)*304.8);verifiedSamples++;
        }
       }
      }
     }
     if(verifiedSamples<3||(!edgeMode&&overlapVerified<3))throw new InvalidOperationException("No usable regenerated beam top face matching the requested slope.");
     // Independent solid intersection check prevents a falsely accepted penetration.
     if(targetBottom)foreach(var beamSolid in FramingFloorSolids(beam.get_Geometry(new Options{DetailLevel=ViewDetailLevel.Fine})))
      foreach(var floorSolid in FramingFloorSolids(floor.get_Geometry(new Options{DetailLevel=ViewDetailLevel.Fine}))) {
       var overlap=BooleanOperationsUtils.ExecuteBooleanOperation(beamSolid,floorSolid,BooleanOperationsType.Intersect);
       if(overlap!=null&&overlap.Volume>1e-8)throw new InvalidOperationException("Beam penetrates the floor solid.");
      }
     if(maxError>1)throw new InvalidOperationException("Beam top does not match selected slab surface within 1 mm.");
     if(apply&&!dry){if(tx.Commit()!=TransactionStatus.Committed)throw new InvalidOperationException("Commit failed.");committed=true;}
     else tx.RollBack();
    }catch(Exception ex){if(tx.GetStatus()==TransactionStatus.Started)tx.RollBack();error=ex.Message;}
   }
   return new{Success=error==null,Error=error,DryRun=dry,Apply=apply,Committed=committed,BeamId=beam.Id.GetIdValue(),FloorId=floor.Id.GetIdValue(),StartTopMm=z0*304.8,EndTopMm=z1*304.8,MaxErrorMm=maxError,VerifiedSamples=verifiedSamples,OverlapVerifiedSamples=overlapVerified,ExtendedVerifiedSamples=extendedVerified,ExtendAlongEdge=edgeMode,ReferenceEdge=edgeReport,TargetSurface=targetSurface,ExpectedFloorOverlap=!targetBottom,ExtendOverhang=extend,MissingInputSamples=missingSamples,PreviewToken=token,Note="Existing beam joins are removed and end joins disabled when applied; beam dimensions and plan axis retained."};
  }
  private Tuple<double,double,JObject> FitRampEdge(Floor floor,XYZ a,XYZ b,XYZ d,XYZ side,double left,double right,bool bottom){
   double length=new XYZ(b.X-a.X,b.Y-a.Y,0).GetLength();
   var candidates=new List<Tuple<double,double,double,XYZ,XYZ>>();
   foreach(var solid in FramingFloorSolids(floor.get_Geometry(new Options{DetailLevel=ViewDetailLevel.Fine})))foreach(Face face in solid.Faces){
    if(!(face is PlanarFace pf)||(bottom?pf.FaceNormal.Z>=-0.1:pf.FaceNormal.Z<=0.1))continue;
    // A beam without cross-section rotation cannot follow a transverse slope.
    if(Math.Abs(pf.FaceNormal.DotProduct(side)/pf.FaceNormal.Z)*(right-left)*304.8>1)continue;
    foreach(EdgeArray loop in face.EdgeLoops)foreach(Edge edge in loop){
     if(!(edge.AsCurve() is Line el))continue;
     var p=el.GetEndPoint(0);var q=el.GetEndPoint(1);var delta=new XYZ(q.X-p.X,q.Y-p.Y,0);
     if(delta.GetLength()<0.001||Math.Abs(delta.Normalize().DotProduct(d))<0.999999)continue;
     double t0=(p-a).DotProduct(d),t1=(q-a).DotProduct(d),span=Math.Abs(t1-t0);
     if(Math.Min(length,Math.Max(t0,t1))-Math.Max(0,Math.Min(t0,t1))<Math.Max(500/304.8,length*0.2))continue;
     double lateral=(p-a).DotProduct(side),distance=Math.Max(0,Math.Max(left-lateral,lateral-right));
     if(distance*304.8>600)continue;
     double slope=(q.Z-p.Z)/(t1-t0),z0=p.Z-slope*t0,z1=z0+slope*length;
     candidates.Add(Tuple.Create(distance,z0,z1,p,q));
    }
   }
   if(candidates.Count==0)throw new InvalidOperationException("No nearby straight parallel slab edge with sufficient projected overlap.");
   double nearest=candidates.Min(c=>c.Item1);
   var near=candidates.Where(c=>c.Item1<=nearest+1/304.8).ToList();var best=near[0];
   if(near.Any(c=>Math.Abs(c.Item2-best.Item2)*304.8>1||Math.Abs(c.Item3-best.Item3)*304.8>1))throw new InvalidOperationException("Nearby collinear slab edges have conflicting elevations/slopes.");
   return Tuple.Create(best.Item2,best.Item3,new JObject{["DistanceFromBeamMm"]=nearest*304.8,["Start"]=new JArray(best.Item4.X*304.8,best.Item4.Y*304.8,best.Item4.Z*304.8),["End"]=new JArray(best.Item5.X*304.8,best.Item5.Y*304.8,best.Item5.Z*304.8),["CandidateCount"]=near.Count});
  }

 }
}

