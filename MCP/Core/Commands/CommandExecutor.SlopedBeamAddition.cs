using System;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Newtonsoft.Json.Linq;
#if REVIT2025_OR_GREATER
using SlopeId = System.Int64;
#else
using SlopeId = System.Int32;
#endif
namespace RevitMCP.Core {
 public partial class CommandExecutor {
  private static double SlopePlaneZ(PlanarFace f,XYZ p){return f.Origin.Z-(f.FaceNormal.X*(p.X-f.Origin.X)+f.FaceNormal.Y*(p.Y-f.Origin.Y))/f.FaceNormal.Z;}
  private static double SlopeEdgeDistance(XYZ p,XYZ a,XYZ b){var d=new XYZ(b.X-a.X,b.Y-a.Y,0);var v=new XYZ(p.X-a.X,p.Y-a.Y,0);double t=d.DotProduct(d)<1e-12?0:Math.Max(0,Math.Min(1,v.DotProduct(d)/d.DotProduct(d)));return (v-d*t).GetLength();}
  private object CreateSlopedBeamAddition(JObject p){
   var doc=_uiApp.ActiveUIDocument.Document;var ids=p["sourceBeamIds"] as JArray;
   if(ids==null||ids.Count!=1||p["floorId"]==null)throw new ArgumentException("slopedSolid requires exactly one source beam and floorId.");
   var beam=doc.GetElement(new ElementId((SlopeId)ids[0].Value<long>())) as FamilyInstance;
   var floor=doc.GetElement(new ElementId((SlopeId)p["floorId"].Value<long>())) as Floor;
   if(beam==null||beam.StructuralType!=StructuralType.Beam||floor==null)throw new ArgumentException("Invalid source beam/floor.");
   var line=(beam.Location as LocationCurve)?.Curve as Line;if(line==null)throw new ArgumentException("Straight source required.");
   var a=line.GetEndPoint(0);var b=line.GetEndPoint(1);if(Math.Abs(a.Z-b.Z)>1e-6)throw new ArgumentException("Horizontal source required.");
   var d=new XYZ(b.X-a.X,b.Y-a.Y,0).Normalize();var side=new XYZ(-d.Y,d.X,0);
   var solids=FramingFloorSolids(beam.get_Geometry(new Options{DetailLevel=ViewDetailLevel.Fine})).Where(s=>s.Volume>1e-9).ToList();
   var vertices=solids.SelectMany(s=>s.Faces.Cast<Face>().SelectMany(f=>f.Triangulate().Vertices)).ToList();if(vertices.Count==0)throw new ArgumentException("Source solid unavailable.");
   double u0=vertices.Min(v=>(v-a).DotProduct(d)),u1=vertices.Max(v=>(v-a).DotProduct(d)),v0=vertices.Min(v=>(v-a).DotProduct(side)),v1=vertices.Max(v=>(v-a).DotProduct(side)),bottom=vertices.Max(v=>v.Z);
   Func<double,double,XYZ> point=(u,v)=>new XYZ(a.X+d.X*u+side.X*v,a.Y+d.Y*u+side.Y*v,bottom);
   var corners=new[]{point(u0,v0),point(u1,v0),point(u1,v1),point(u0,v1)};
   // Require a rectangular, genuinely horizontal source top, not a bounding-box approximation.
   for(int i=0;i<=20;i++)for(int j=0;j<=4;j++){var q=point(u0+(u1-u0)*(0.001+0.998*i/20.0),v0+(v1-v0)*(0.001+0.998*j/4.0));var z=FitTrySurfaceZ(beam,q.X,q.Y,false);if(!z.HasValue||Math.Abs(z.Value-bottom)*304.8>1)throw new ArgumentException("Source top is not a complete horizontal rectangle.");}
   var center=point((u0+u1)/2,(v0+v1)/2);var candidates=new List<Tuple<double,PlanarFace>>();
   foreach(var s in FramingFloorSolids(floor.get_Geometry(new Options{DetailLevel=ViewDetailLevel.Fine})))foreach(Face f in s.Faces){
    if(!(f is PlanarFace pf)||pf.FaceNormal.Z<0.1)continue;
    double distance=double.MaxValue;foreach(EdgeArray loop in f.EdgeLoops)foreach(Edge e in loop){var vs=e.Tessellate();for(int i=1;i<vs.Count;i++)distance=Math.Min(distance,SlopeEdgeDistance(center,vs[i-1],vs[i]));}
    var projection=pf.Project(new XYZ(center.X,center.Y,SlopePlaneZ(pf,center)));if(projection!=null&&pf.IsInside(projection.UVPoint))distance=0;
    candidates.Add(Tuple.Create(distance,pf));
   }
   if(candidates.Count==0)throw new ArgumentException("No planar floor top.");double nearest=candidates.Min(c=>c.Item1);
   if(nearest*304.8>600)throw new ArgumentException("Floor plane farther than 600 mm.");
   var near=candidates.Where(c=>c.Item1<=nearest+1/304.8).ToList();var face=near[0].Item2;
   if(near.Any(c=>corners.Any(q=>Math.Abs(SlopePlaneZ(c.Item2,q)-SlopePlaneZ(face,q))*304.8>1)))throw new ArgumentException("Ambiguous nearby floor planes.");
   bool extend=p["extendFloorPlane"]?.Value<bool>()??false;int covered=0,extended=0;
   for(int i=0;i<=20;i++)for(int j=0;j<=8;j++){var q=point(u0+(u1-u0)*i/20.0,v0+(v1-v0)*j/8.0);var z=FitTrySurfaceZ(floor,q.X,q.Y,false);if(z.HasValue){covered++;if(Math.Abs(z.Value-SlopePlaneZ(face,q))*304.8>1)throw new ArgumentException("Floor top is not one consistent plane.");}else{extended++;if(!extend)throw new ArgumentException("Missing floor coverage; explicit extendFloorPlane required.");}}
   var top=corners.Select(q=>new XYZ(q.X,q.Y,SlopePlaneZ(face,q))).ToArray();var depths=top.Select(q=>q.Z-bottom).ToArray();if(depths.Min()*304.8<1)throw new ArgumentException("Addition thickness must be positive at every corner.");
   var category=new ElementId(BuiltInCategory.OST_StructuralFraming);if(!DirectShape.IsValidCategoryId(category,doc))throw new ArgumentException("Structural framing DirectShape unsupported.");
   string number=(string)p["numberValue"]??"增築";string key="sloped-addition:"+beam.UniqueId+":"+floor.UniqueId;
   if(new FilteredElementCollector(doc).OfClass(typeof(DirectShape)).Cast<DirectShape>().Any(x=>x.ApplicationId=="Abby.SlopedBeamAddition"&&x.ApplicationDataId==key))throw new ArgumentException("Addition already exists for this beam/floor.");
   var material=beam.GetMaterialIds(false).OrderBy(x=>x.GetIdValue()).FirstOrDefault()??ElementId.InvalidElementId;
   var signature=doc.ProjectInformation.UniqueId+key+number+extend+material.GetIdValue()+string.Join("|",corners.Concat(top).Select(q=>q.X.ToString("R",CultureInfo.InvariantCulture)+","+q.Y.ToString("R",CultureInfo.InvariantCulture)+","+q.Z.ToString("R",CultureInfo.InvariantCulture)));
   string token;using(var hash=SHA256.Create())token=Convert.ToBase64String(hash.ComputeHash(Encoding.UTF8.GetBytes(signature)));
   bool dry=p["dryRun"]?.Value<bool>()??true,apply=p["apply"]?.Value<bool>()??false;
   if(apply&&!dry&&(string)p["previewToken"]!=token)throw new ArgumentException("Fresh preview token required.");
   Func<XYZ[],CurveLoop> loopFor=ps=>{var loop=new CurveLoop();for(int i=0;i<4;i++)loop.Append(Line.CreateBound(ps[i],ps[(i+1)%4]));return loop;};
   var shape=GeometryCreationUtilities.CreateLoftGeometry(new List<CurveLoop>{loopFor(corners),loopFor(top)},new SolidOptions(material,ElementId.InvalidElementId));
   double expected=(u1-u0)*(v1-v0)*depths.Average();if(Math.Abs(shape.Volume-expected)>expected*1e-6)throw new InvalidOperationException("Loft volume mismatch.");
   bool committed=false;long? created=null;double error=0;
   using(var tx=new Transaction(doc,"Create sloped beam addition")){try{tx.Start();tx.SetFailureHandlingOptions(tx.GetFailureHandlingOptions().SetFailuresPreprocessor(new FramingFloorFailures()).SetClearAfterRollback(true));var ds=DirectShape.CreateElement(doc,category);ds.ApplicationId="Abby.SlopedBeamAddition";ds.ApplicationDataId=key;ds.Name="斜面梁增築";ds.SetShape(new List<GeometryObject>{shape});AdditionText(ds,"編號").Set(number);doc.Regenerate();
    var actual=FramingFloorSolids(ds.get_Geometry(new Options{DetailLevel=ViewDetailLevel.Fine})).ToList();if(Math.Abs(actual.Sum(s=>s.Volume)-expected)>expected*1e-6)throw new InvalidOperationException("Regenerated volume mismatch.");
    for(int i=0;i<=20;i++)for(int j=0;j<=8;j++){var q=point(u0+(u1-u0)*(0.001+0.998*i/20.0),v0+(v1-v0)*(0.001+0.998*j/8.0));double zt=FitSurfaceZ(ds,q.X,q.Y,false),zb=FitSurfaceZ(ds,q.X,q.Y,true);error=Math.Max(error,Math.Max(Math.Abs(zt-SlopePlaneZ(face,q)),Math.Abs(zb-bottom))*304.8);}if(error>1)throw new InvalidOperationException("Regenerated surface mismatch.");
    foreach(var s in actual)foreach(var original in solids)if(BooleanOperationsUtils.ExecuteBooleanOperation(s,original,BooleanOperationsType.Intersect).Volume>1e-6)throw new InvalidOperationException("Addition intersects original beam.");
    if(apply&&!dry){created=ds.Id.GetIdValue();if(tx.Commit()!=TransactionStatus.Committed)throw new InvalidOperationException("Commit failed.");committed=true;}else tx.RollBack();
   }catch{if(tx.GetStatus()==TransactionStatus.Started)tx.RollBack();throw;}}
   return new{Success=true,Committed=committed,CreatedElementId=committed?created:null,DryRun=dry,PreviewToken=token,Representation="StructuralFraming DirectShape",SourceBeamId=beam.Id.GetIdValue(),FloorId=floor.Id.GetIdValue(),MinDepthMm=depths.Min()*304.8,MaxDepthMm=depths.Max()*304.8,WidthMm=(v1-v0)*304.8,LengthMm=(u1-u0)*304.8,BottomElevationMm=bottom*304.8,VolumeM3=expected*Math.Pow(0.3048,3),MaxErrorMm=error,CoveredSamples=covered,ExtendedSamples=extended,NumberValue=number};
  }
 }
}
