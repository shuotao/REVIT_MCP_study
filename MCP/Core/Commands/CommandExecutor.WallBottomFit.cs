using System;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
#if REVIT2025_OR_GREATER
using WFitId = System.Int64;
#else
using WFitId = System.Int32;
#endif
namespace RevitMCP.Core {
 public partial class CommandExecutor {
  private sealed class WallBottomFailures:IFailuresPreprocessor {
   public readonly List<string> Messages=new List<string>();
   public FailureProcessingResult PreprocessFailures(FailuresAccessor accessor){bool error=false;foreach(var failure in accessor.GetFailureMessages()){Messages.Add(failure.GetDescriptionText());if(failure.GetSeverity()==FailureSeverity.Warning)accessor.DeleteWarning(failure);else error=true;}return error?FailureProcessingResult.ProceedWithRollBack:FailureProcessingResult.Continue;}
  }
  private object FitWallBottomToBeam(JObject args){
   var doc=_uiApp.ActiveUIDocument.Document;var p=args["wallBottomToBeam"];
   var wall=doc.GetElement(new ElementId((WFitId)p["wallId"].Value<long>())) as Wall;var beam=doc.GetElement(new ElementId((WFitId)p["beamId"].Value<long>())) as FamilyInstance;
   if(wall==null||beam==null||beam.StructuralType!=Autodesk.Revit.DB.Structure.StructuralType.Beam)throw new ArgumentException("Wall and structural beam required.");
   if(wall.Pinned||wall.GroupId!=ElementId.InvalidElementId||wall.WallType.Kind!=WallKind.Basic||wall.CrossSection!=WallCrossSection.Vertical)throw new ArgumentException("Only unpinned, ungrouped vertical basic walls supported.");
   if(wall.get_Parameter(BuiltInParameter.WALL_BOTTOM_IS_ATTACHED).AsInteger()!=0||wall.get_Parameter(BuiltInParameter.WALL_TOP_IS_ATTACHED).AsInteger()!=0)throw new ArgumentException("Detach wall top/base first.");
   if(wall.SketchId!=ElementId.InvalidElementId||!wall.CanHaveProfileSketch()||wall.FindInserts(true,true,true,true).Count>0)throw new ArgumentException("Existing edited profile or inserts unsupported.");
   var line=(wall.Location as LocationCurve)?.Curve as Line;if(line==null)throw new ArgumentException("Straight wall required.");
   var a=line.GetEndPoint(0);var b=line.GetEndPoint(1);var d=new XYZ(b.X-a.X,b.Y-a.Y,0).Normalize();var side=new XYZ(-d.Y,d.X,0);double length=new XYZ(b.X-a.X,b.Y-a.Y,0).GetLength();
   var vertices=FramingFloorSolids(wall.get_Geometry(new Options{DetailLevel=ViewDetailLevel.Fine})).SelectMany(s=>s.Faces.Cast<Face>().SelectMany(f=>f.Triangulate().Vertices)).ToList();
   double lo=vertices.Min(v=>(v-a).DotProduct(side)),hi=vertices.Max(v=>(v-a).DotProduct(side)),top=vertices.Max(v=>v.Z),oldBottom=vertices.Min(v=>v.Z);
   Func<double,double,XYZ> point=(t,v)=>a+d*(length*t)+side*v;
   var mid0=point(.00001,(lo+hi)/2);var mid1=point(.99999,(lo+hi)/2);
   double h0=FitSurfaceZ(beam,mid0.X,mid0.Y,false),h1=FitSurfaceZ(beam,mid1.X,mid1.Y,false),slope=(h1-h0)/(.99998*length),z0=h0-slope*.00001*length,z1=z0+slope*length;
   double maxError=0;var samples=new List<XYZ>();
   for(int i=0;i<=40;i++)for(int j=0;j<=4;j++){double t=.00001+.99998*i/40.0;var q=point(t,lo+(hi-lo)*(.001+.998*j/4.0));var z=FitSurfaceZ(beam,q.X,q.Y,false);if(Math.Abs(z-(z0+(z1-z0)*t))*304.8>1)throw new ArgumentException("Beam top is not a single longitudinal plane across wall width.");if(Math.Abs(FitSurfaceZ(wall,q.X,q.Y,false)-top)*304.8>1)throw new ArgumentException("Wall top is not flat.");samples.Add(new XYZ(q.X,q.Y,z));}
   if(Math.Max(z0,z1)>=top-1/304.8||Math.Min(z0,z1)<oldBottom-1/304.8)throw new ArgumentException("Requested bottom must trim within existing wall height.");
   string sig=doc.ProjectInformation.UniqueId+wall.UniqueId+beam.UniqueId+wall.GetTypeId().GetIdValue()+string.Join("|",new[]{a.X,a.Y,a.Z,b.X,b.Y,b.Z,lo,hi,top,oldBottom,z0,z1}.Select(x=>x.ToString("R",CultureInfo.InvariantCulture)));
   string token;using(var hash=SHA256.Create())token=Convert.ToBase64String(hash.ComputeHash(Encoding.UTF8.GetBytes(sig)));
   bool dry=args["dryRun"]?.Value<bool>()??true,apply=args["apply"]?.Value<bool>()??false;if(apply&&!dry&&(string)args["previewToken"]!=token)throw new ArgumentException("Fresh preview token required.");
   var failures=new WallBottomFailures();bool committed=false;using(var group=new TransactionGroup(doc,"Fit wall bottom to beam")){group.Start();try{
    using(var tx=new Transaction(doc,"Prepare wall profile")){tx.Start();tx.SetFailureHandlingOptions(tx.GetFailureHandlingOptions().SetFailuresPreprocessor(failures).SetClearAfterRollback(true));wall.CreateProfileSketch();doc.Regenerate();if(tx.Commit()!=TransactionStatus.Committed)throw new InvalidOperationException("Profile creation failed: "+string.Join(" | ",failures.Messages));}
    var sketch=doc.GetElement(wall.SketchId) as Sketch;
    var curves=sketch.GetAllElements().Select(id=>doc.GetElement(id)).OfType<ModelCurve>().ToList();
    if(curves.Count!=4||curves.Any(c=>!(c.GeometryCurve is Line)))throw new ArgumentException("Only simple four-edge wall profile supported.");
    var endpoints=curves.SelectMany(c=>new[]{c.GeometryCurve.GetEndPoint(0),c.GeometryCurve.GetEndPoint(1)}).ToList();double baseZ=endpoints.Min(q=>q.Z),topZ=endpoints.Max(q=>q.Z);
    if(Math.Abs(baseZ-oldBottom)*304.8>1||Math.Abs(topZ-top)*304.8>1)throw new ArgumentException("Sketch differs from existing wall shape.");
    using(var scope=new SketchEditScope(doc,"Slope wall lower edge")){scope.Start(sketch.Id);using(var tx=new Transaction(doc,"Update profile edges")){tx.Start();tx.SetFailureHandlingOptions(tx.GetFailureHandlingOptions().SetFailuresPreprocessor(failures).SetClearAfterRollback(true));var replacement=new List<Curve>();foreach(var curve in curves){var ps=new[]{curve.GeometryCurve.GetEndPoint(0),curve.GeometryCurve.GetEndPoint(1)};for(int k=0;k<2;k++)if(Math.Abs(ps[k].Z-baseZ)<1e-6){double t=(ps[k]-a).DotProduct(d)/length;ps[k]=new XYZ(ps[k].X,ps[k].Y,z0+(z1-z0)*t);}replacement.Add(Line.CreateBound(ps[0],ps[1]));}var plane=sketch.SketchPlane;doc.Delete(curves.Select(c=>c.Id).ToList());foreach(var edge in replacement)doc.Create.NewModelCurve(edge,plane);if(tx.Commit()!=TransactionStatus.Committed)throw new InvalidOperationException("Profile edit failed: "+string.Join(" | ",failures.Messages));}scope.Commit(failures);}
    using(var tx=new Transaction(doc,"Verify wall profile")){tx.Start();tx.SetFailureHandlingOptions(tx.GetFailureHandlingOptions().SetFailuresPreprocessor(failures).SetClearAfterRollback(true));doc.Regenerate();var now=(wall.Location as LocationCurve).Curve;for(int i=0;i<2;i++)if(now.GetEndPoint(i).DistanceTo(line.GetEndPoint(i))>1e-6)throw new InvalidOperationException("Wall axis moved.");foreach(var q in samples){maxError=Math.Max(maxError,Math.Abs(FitSurfaceZ(wall,q.X,q.Y,true)-q.Z)*304.8);if(Math.Abs(FitSurfaceZ(wall,q.X,q.Y,false)-top)*304.8>1)throw new InvalidOperationException("Wall top changed.");}if(maxError>1)throw new InvalidOperationException("Wall bottom fit failed.");if(tx.Commit()!=TransactionStatus.Committed)throw new InvalidOperationException("Verification failed.");}
    if(apply&&!dry){if(group.Assimilate()!=TransactionStatus.Committed)throw new InvalidOperationException("Group commit failed.");committed=true;}else group.RollBack();
   }catch{if(group.GetStatus()==TransactionStatus.Started)group.RollBack();throw;}}
   return new{Success=true,Committed=committed,DryRun=dry,WallId=wall.Id.GetIdValue(),BeamId=beam.Id.GetIdValue(),StartBottomMm=z0*304.8,EndBottomMm=z1*304.8,TopMm=top*304.8,MaxErrorMm=maxError,VerifiedSamples=samples.Count,PreviewToken=token,DynamicAttachment=false};
  }
 }
}

