using System;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
#if REVIT2025_OR_GREATER
using WTopId = System.Int64;
#else
using WTopId = System.Int32;
#endif
namespace RevitMCP.Core {
 public partial class CommandExecutor {
  private object FitWallTopToFloor(JObject args){
   var doc=_uiApp.ActiveUIDocument.Document;var p=args["wallTopToFloor"];
   var wall=doc.GetElement(new ElementId((WTopId)p["wallId"].Value<long>())) as Wall;var floor=doc.GetElement(new ElementId((WTopId)p["floorId"].Value<long>())) as Floor;
   if(wall==null||floor==null)throw new ArgumentException("Wall and floor required.");
   if(wall.Pinned||wall.GroupId!=ElementId.InvalidElementId||wall.WallType.Kind!=WallKind.Basic||wall.CrossSection!=WallCrossSection.Vertical)throw new ArgumentException("Only unpinned, ungrouped vertical basic walls supported.");
   if(wall.get_Parameter(BuiltInParameter.WALL_BOTTOM_IS_ATTACHED).AsInteger()!=0||wall.get_Parameter(BuiltInParameter.WALL_TOP_IS_ATTACHED).AsInteger()!=0)throw new ArgumentException("Detach wall top/base first.");
   if(wall.SketchId!=ElementId.InvalidElementId||!wall.CanHaveProfileSketch()||wall.FindInserts(true,true,true,true).Count>0)throw new ArgumentException("Existing edited profile or inserts unsupported.");
   var line=(wall.Location as LocationCurve)?.Curve as Line;if(line==null)throw new ArgumentException("Straight wall required.");
   var a=line.GetEndPoint(0);var b=line.GetEndPoint(1);var d=new XYZ(b.X-a.X,b.Y-a.Y,0).Normalize();var side=new XYZ(-d.Y,d.X,0);double length=new XYZ(b.X-a.X,b.Y-a.Y,0).GetLength();
   var vertices=FramingFloorSolids(wall.get_Geometry(new Options{DetailLevel=ViewDetailLevel.Fine})).SelectMany(s=>s.Faces.Cast<Face>().SelectMany(f=>f.Triangulate().Vertices)).ToList();
   double lo=vertices.Min(v=>(v-a).DotProduct(side)),hi=vertices.Max(v=>(v-a).DotProduct(side)),top=vertices.Max(v=>v.Z),oldBottom=vertices.Min(v=>v.Z);
   Func<double,double,XYZ> point=(t,v)=>a+d*(length*t)+side*v;
   var center=point(.5,(lo+hi)/2);var candidates=new List<Tuple<double,PlanarFace>>();
   foreach(var solid in FramingFloorSolids(floor.get_Geometry(new Options{DetailLevel=ViewDetailLevel.Fine})))foreach(Face f in solid.Faces){if(!(f is PlanarFace pf)||pf.FaceNormal.Z<.1)continue;double distance=double.MaxValue;foreach(EdgeArray loop in f.EdgeLoops)foreach(Edge e in loop){var vs=e.Tessellate();for(int i=1;i<vs.Count;i++)distance=Math.Min(distance,SlopeEdgeDistance(center,vs[i-1],vs[i]));}var projection=pf.Project(new XYZ(center.X,center.Y,SlopePlaneZ(pf,center)));if(projection!=null&&pf.IsInside(projection.UVPoint))distance=0;candidates.Add(Tuple.Create(distance,pf));}
   if(candidates.Count==0)throw new ArgumentException("No planar floor top.");double nearest=candidates.Min(c=>c.Item1);if(nearest*304.8>600)throw new ArgumentException("Floor too far from wall.");var near=candidates.Where(c=>c.Item1<=nearest+1/304.8).ToList();var face=near[0].Item2;
   var corners=new[]{point(0,lo),point(0,hi),point(1,lo),point(1,hi)};if(near.Any(c=>corners.Any(q=>Math.Abs(SlopePlaneZ(c.Item2,q)-SlopePlaneZ(face,q))*304.8>1)))throw new ArgumentException("Ambiguous nearby floor planes.");
   double z0=SlopePlaneZ(face,point(0,(lo+hi)/2)),z1=SlopePlaneZ(face,point(1,(lo+hi)/2));
   bool extend=p["extendFloorPlane"]?.Value<bool>()??false;int covered=0,extended=0;double maxError=0;var samples=new List<XYZ>();
   for(int i=0;i<=40;i++)for(int j=0;j<=4;j++){double t=.00001+.99998*i/40.0;var q=point(t,lo+(hi-lo)*(.001+.998*j/4.0));var originalBottom=FitTrySurfaceZ(wall,q.X,q.Y,true);var originalTop=FitTrySurfaceZ(wall,q.X,q.Y,false);if(!originalBottom.HasValue||!originalTop.HasValue)continue;double z=SlopePlaneZ(face,q);if(Math.Abs(z-(z0+(z1-z0)*t))*304.8>1)throw new ArgumentException("Transverse slope across wall thickness unsupported.");var hit=FitTrySurfaceZ(floor,q.X,q.Y,false);if(hit.HasValue){covered++;if(Math.Abs(hit.Value-z)*304.8>1)throw new ArgumentException("Floor top has multiple slopes.");}else{extended++;if(!extend)throw new ArgumentException("Explicit extendFloorPlane required for uncovered wall points.");double distance=double.MaxValue;foreach(EdgeArray loop in face.EdgeLoops)foreach(Edge edge in loop){var vs=edge.Tessellate();for(int k=1;k<vs.Count;k++)distance=Math.Min(distance,SlopeEdgeDistance(q,vs[k-1],vs[k]));}if(distance*304.8>600)throw new ArgumentException("Extrapolation exceeds 600 mm.");}if(Math.Abs(originalBottom.Value-oldBottom)*304.8>1||Math.Abs(originalTop.Value-top)*304.8>1)throw new ArgumentException("Existing wall must have flat top and bottom.");samples.Add(new XYZ(q.X,q.Y,z));}
   if(samples.Count<100||samples.Min(q=>(q-a).DotProduct(d)/length)>.05||samples.Max(q=>(q-a).DotProduct(d)/length)<.95)throw new ArgumentException("Insufficient actual wall footprint coverage for verification.");
   if(Math.Min(z0,z1)<=oldBottom+1/304.8||Math.Max(z0,z1)>top+1/304.8)throw new ArgumentException("Requested top must trim within existing wall height.");
   string sig=doc.ProjectInformation.UniqueId+wall.UniqueId+floor.UniqueId+extend+wall.GetTypeId().GetIdValue()+string.Join("|",new[]{a.X,a.Y,a.Z,b.X,b.Y,b.Z,lo,hi,top,oldBottom,z0,z1}.Select(x=>x.ToString("R",CultureInfo.InvariantCulture)));
   string token;using(var hash=SHA256.Create())token=Convert.ToBase64String(hash.ComputeHash(Encoding.UTF8.GetBytes(sig)));
   bool dry=args["dryRun"]?.Value<bool>()??true,apply=args["apply"]?.Value<bool>()??false;if(apply&&!dry&&(string)args["previewToken"]!=token)throw new ArgumentException("Fresh preview token required.");
   var failures=new WallBottomFailures();bool committed=false;using(var group=new TransactionGroup(doc,"Fit wall top to floor")){group.Start();try{
    using(var tx=new Transaction(doc,"Prepare wall profile")){tx.Start();tx.SetFailureHandlingOptions(tx.GetFailureHandlingOptions().SetFailuresPreprocessor(failures).SetClearAfterRollback(true));wall.CreateProfileSketch();doc.Regenerate();if(tx.Commit()!=TransactionStatus.Committed)throw new InvalidOperationException("Profile creation failed: "+string.Join(" | ",failures.Messages));}
    var sketch=doc.GetElement(wall.SketchId) as Sketch;
    var curves=sketch.GetAllElements().Select(id=>doc.GetElement(id)).OfType<ModelCurve>().ToList();
    if(curves.Count!=4||curves.Any(c=>!(c.GeometryCurve is Line)))throw new ArgumentException("Only simple four-edge wall profile supported.");
    var endpoints=curves.SelectMany(c=>new[]{c.GeometryCurve.GetEndPoint(0),c.GeometryCurve.GetEndPoint(1)}).ToList();double baseZ=endpoints.Min(q=>q.Z),topZ=endpoints.Max(q=>q.Z);
    if(Math.Abs(baseZ-oldBottom)*304.8>1||Math.Abs(topZ-top)*304.8>1)throw new ArgumentException("Sketch differs from existing wall shape.");
    using(var scope=new SketchEditScope(doc,"Slope wall upper edge")){scope.Start(sketch.Id);using(var tx=new Transaction(doc,"Update profile edges")){tx.Start();tx.SetFailureHandlingOptions(tx.GetFailureHandlingOptions().SetFailuresPreprocessor(failures).SetClearAfterRollback(true));var replacement=new List<Curve>();foreach(var curve in curves){var ps=new[]{curve.GeometryCurve.GetEndPoint(0),curve.GeometryCurve.GetEndPoint(1)};for(int k=0;k<2;k++)if(Math.Abs(ps[k].Z-topZ)<1e-6){double t=(ps[k]-a).DotProduct(d)/length;ps[k]=new XYZ(ps[k].X,ps[k].Y,z0+(z1-z0)*t);}replacement.Add(Line.CreateBound(ps[0],ps[1]));}var plane=sketch.SketchPlane;doc.Delete(curves.Select(c=>c.Id).ToList());foreach(var edge in replacement)doc.Create.NewModelCurve(edge,plane);if(tx.Commit()!=TransactionStatus.Committed)throw new InvalidOperationException("Profile edit failed: "+string.Join(" | ",failures.Messages));}scope.Commit(failures);}
    using(var tx=new Transaction(doc,"Verify wall profile")){tx.Start();tx.SetFailureHandlingOptions(tx.GetFailureHandlingOptions().SetFailuresPreprocessor(failures).SetClearAfterRollback(true));doc.Regenerate();var now=(wall.Location as LocationCurve).Curve;for(int i=0;i<2;i++)if(now.GetEndPoint(i).DistanceTo(line.GetEndPoint(i))>1e-6)throw new InvalidOperationException("Wall axis moved.");foreach(var q in samples){maxError=Math.Max(maxError,Math.Abs(FitSurfaceZ(wall,q.X,q.Y,false)-q.Z)*304.8);if(Math.Abs(FitSurfaceZ(wall,q.X,q.Y,true)-oldBottom)*304.8>1)throw new InvalidOperationException("Wall bottom changed.");}if(maxError>1)throw new InvalidOperationException("Wall top fit failed.");if(tx.Commit()!=TransactionStatus.Committed)throw new InvalidOperationException("Verification failed.");}
    if(apply&&!dry){if(group.Assimilate()!=TransactionStatus.Committed)throw new InvalidOperationException("Group commit failed.");committed=true;}else group.RollBack();
   }catch{if(group.GetStatus()==TransactionStatus.Started)group.RollBack();throw;}}
   return new{Success=true,Committed=committed,DryRun=dry,WallId=wall.Id.GetIdValue(),FloorId=floor.Id.GetIdValue(),StartTopMm=z0*304.8,EndTopMm=z1*304.8,BottomMm=oldBottom*304.8,CoveredSamples=covered,ExtendedSamples=extended,MaxErrorMm=maxError,VerifiedSamples=samples.Count,PreviewToken=token,DynamicAttachment=false};
  }
 }
}

