using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
#if REVIT2025_OR_GREATER
using WBId=System.Int64;
#else
using WBId=System.Int32;
#endif

namespace RevitMCP.Core
{
    public partial class CommandExecutor
    {
        private object FitWallTopToBeamExtremes(JObject args)
        {
            var doc=_uiApp.ActiveUIDocument.Document;
            var p=args["wallTopToBeams"];
            string targetSurface=(string)p["targetSurface"]??"top";
            if(targetSurface!="top"&&targetSurface!="bottom")throw new ArgumentException("targetSurface must be top or bottom.");
            bool targetBottom=targetSurface=="bottom";
            var wall=doc.GetElement(new ElementId((WBId)p["wallId"].Value<long>())) as Wall;
            var ids=p["beamIds"]?.Values<long>().Distinct().OrderBy(id=>id).ToArray();
            if(wall==null||ids==null||ids.Length==0||ids.Length>50)throw new ArgumentException("One wall and 1-50 beam IDs required.");
            var beams=ids.Select(id=>doc.GetElement(new ElementId((WBId)id)) as FamilyInstance).ToList();
            if(beams.Any(b=>b==null||b.StructuralType!=Autodesk.Revit.DB.Structure.StructuralType.Beam))throw new ArgumentException("Every reference must be a structural beam.");
            if(wall.Pinned||wall.GroupId!=ElementId.InvalidElementId||wall.WallType.Kind!=WallKind.Basic||wall.CrossSection!=WallCrossSection.Vertical)
                throw new ArgumentException("Only unpinned, ungrouped vertical basic walls supported.");
            if(wall.get_Parameter(BuiltInParameter.WALL_TOP_IS_ATTACHED).AsInteger()!=0)throw new ArgumentException("Wall top is attached; detach top before editing its profile.");
            if(wall.SketchId!=ElementId.InvalidElementId||!wall.CanHaveProfileSketch()||wall.FindInserts(true,true,true,true).Count>0)
                throw new ArgumentException("Existing profile or inserts unsupported.");
            var line=(wall.Location as LocationCurve)?.Curve as Line;
            if(line==null)throw new ArgumentException("Straight wall required.");
            var a=line.GetEndPoint(0);var b=line.GetEndPoint(1);var d=new XYZ(b.X-a.X,b.Y-a.Y,0).Normalize();var side=new XYZ(-d.Y,d.X,0);
            double length=new XYZ(b.X-a.X,b.Y-a.Y,0).GetLength(),width=wall.Width;
            var vertices=FramingFloorSolids(wall.get_Geometry(new Options{DetailLevel=ViewDetailLevel.Fine})).SelectMany(s=>s.Faces.Cast<Face>().SelectMany(f=>f.Triangulate().Vertices)).ToList();
            if(vertices.Count==0)throw new ArgumentException("Wall solid missing.");
            double lo=vertices.Min(v=>(v-a).DotProduct(side)),hi=vertices.Max(v=>(v-a).DotProduct(side));
            var origin=a+side*((lo+hi)/2);
            var segments=new List<WallTopExtremes.Segment>();
            var curvedStations=new List<double>();
            foreach(var beam in beams)
            {
                int before=segments.Count;
                foreach(var solid in FramingFloorSolids(beam.get_Geometry(new Options{DetailLevel=ViewDetailLevel.Fine})))
                foreach(Face face in solid.Faces)
                {
                    if(!(face is PlanarFace pf))
                    {
                        // Rolled steel fillets are usually internal to the planar flange envelope.
                        // Keep their projected stations for independent actual-face ray validation.
                        foreach(var v in face.Triangulate().Vertices)
                        {
                            double u=(v-origin).DotProduct(d)*304.8;
                            if(u>=0&&u<=length*304.8)curvedStations.Add(u);
                        }
                        continue;
                    }
                    if(targetBottom?pf.FaceNormal.Z>=-.1:pf.FaceNormal.Z<=.1)continue;
                    var mesh=face.Triangulate();
                    for(int ti=0;ti<mesh.NumTriangles;ti++)
                    {
                        var tri=mesh.get_Triangle(ti);var hits=new List<XYZ>();
                        for(int k=0;k<3;k++)
                        {
                            var v=tri.get_Vertex(k);var w=tri.get_Vertex((k+1)%3);
                            double sv=(v-origin).DotProduct(side),sw=(w-origin).DotProduct(side);
                            if(Math.Abs(sv)<1e-8)hits.Add(v);
                            if(sv*sw<0)hits.Add(v+(w-v)*(sv/(sv-sw)));
                        }
                        if(hits.Count<2)continue;
                        var sorted=hits.OrderBy(v=>(v-origin).DotProduct(d)).ToList();var q0=sorted.First();var q1=sorted.Last();
                        double u0=(q0-origin).DotProduct(d),u1=(q1-origin).DotProduct(d);
                        if(u1-u0<1e-7)continue;
                        double start=Math.Max(0,u0),end=Math.Min(length,u1);if(end-start<1e-7)continue;
                        segments.Add(new WallTopExtremes.Segment{U0=start*304.8,U1=end*304.8,Z0=(q0.Z+(q1.Z-q0.Z)*(start-u0)/(u1-u0))*304.8,Z1=(q0.Z+(q1.Z-q0.Z)*(end-u0)/(u1-u0))*304.8,Source=beam.Id.GetIdValue()});
                    }
                }
                if(segments.Count==before)throw new ArgumentException("Beam does not intersect wall center plane within wall length: "+beam.Id.GetIdValue());
            }
            if(segments.Count>2000)throw new ArgumentException("Beam geometry too complex for bounded extrema analysis.");
            // Do not silently ignore a curved surface that forms the actual outer surface.
            var checks=segments.SelectMany(s=>Enumerable.Range(0,21).Select(i=>s.U0+(s.U1-s.U0)*i/20.0))
                .Concat(curvedStations).Distinct().OrderBy(u=>u).ToList();
            if(checks.Count>10000)throw new ArgumentException("Too many surface verification stations.");
            double maxSurfaceError=0;int surfaceChecks=0;
            foreach(double u in checks)
            {
                var covering=segments.Where(s=>u>=s.U0-1e-7&&u<=s.U1+1e-7).ToList();
                if(covering.Count==0)continue;
                double expected=targetBottom?covering.Min(s=>s.Z(u)):covering.Max(s=>s.Z(u));
                var q=origin+d*(u/304.8);
                var hits=beams.Select(beam=>FitTrySurfaceZ(beam,q.X,q.Y,targetBottom)).Where(z=>z.HasValue).Select(z=>z.Value*304.8).ToList();
                if(hits.Count==0)continue; // Exact tessellation boundaries can have no ray hit.
                double actual=targetBottom?hits.Min():hits.Max();
                maxSurfaceError=Math.Max(maxSurfaceError,Math.Abs(actual-expected));surfaceChecks++;
            }
            if(surfaceChecks<3||maxSurfaceError>1)throw new ArgumentException("Curved/actual beam surface differs from planar envelope by over 1 mm.");
            var fit=WallTopExtremes.Solve(segments,targetBottom);
            double z0=fit.At(0)/304.8,z1=fit.At(length*304.8)/304.8;
            // Snapshot the actual bottom, including attached or sloping bottoms. Never detach it.
            var samples=new List<Tuple<XYZ,double?>>();
            for(int i=0;i<=100;i++)for(int j=0;j<=4;j++)
            {
                double t=.0001+.9998*i/100.0;
                var q=a+d*(length*t)+side*(lo+(hi-lo)*(.01+.98*j/4.0));
                var bottom=FitTrySurfaceZ(wall,q.X,q.Y,true);
                if(bottom.HasValue&&z0+(z1-z0)*t<=bottom.Value+1/304.8)throw new ArgumentException("Proposed top intersects existing wall bottom; nothing changed.");
                samples.Add(Tuple.Create(q,bottom));
            }
            if(samples.Count(s=>s.Item2.HasValue)<100)throw new ArgumentException("Insufficient wall bottom coverage.");
            int bottomAttached=wall.get_Parameter(BuiltInParameter.WALL_BOTTOM_IS_ATTACHED).AsInteger();
            var wallTypeId=wall.GetTypeId();bool flipped=wall.Flipped;
            string sig=targetSurface+doc.ProjectInformation.UniqueId+wall.UniqueId+wallTypeId.GetIdValue()+bottomAttached+flipped+
                string.Join("|",beams.Select(x=>x.UniqueId))+string.Join("|",new[]{a.X,a.Y,a.Z,b.X,b.Y,b.Z,lo,hi,width,z0,z1}.Concat(samples.Select(s=>s.Item2??double.NaN)).Select(x=>x.ToString("R",CultureInfo.InvariantCulture)))+
                string.Join("|",vertices.Select(v=>string.Join(",",new[]{v.X,v.Y,v.Z}.Select(x=>x.ToString("R",CultureInfo.InvariantCulture)))))+
                string.Join("|",segments.Select(s=>string.Join(",",new[]{s.U0,s.U1,s.Z0,s.Z1}.Select(x=>x.ToString("R",CultureInfo.InvariantCulture)))));
            string token;using(var hash=SHA256.Create())token=Convert.ToBase64String(hash.ComputeHash(Encoding.UTF8.GetBytes(sig)));
            bool dry=args["dryRun"]?.Value<bool>()??true,apply=args["apply"]?.Value<bool>()??false;
            if(apply&&!dry&&(string)args["previewToken"]!=token)throw new ArgumentException("Fresh preview token required.");
            var failures=new WallBottomFailures();double maxTopError=0,maxBottomError=0;bool committed=false;
            using(var group=new TransactionGroup(doc,"Wall top through beam extrema"))
            {
                group.Start();
                try
                {
                    using(var tx=new Transaction(doc,"Prepare wall top profile"))
                    {
                        tx.Start();tx.SetFailureHandlingOptions(tx.GetFailureHandlingOptions().SetFailuresPreprocessor(failures).SetClearAfterRollback(true));
                        wall.CreateProfileSketch();doc.Regenerate();
                        if(tx.Commit()!=TransactionStatus.Committed)throw new InvalidOperationException("Cannot create wall profile: "+string.Join(" | ",failures.Messages));
                    }
                    var sketch=doc.GetElement(wall.SketchId) as Sketch;
                    var curves=sketch.GetAllElements().Select(id=>doc.GetElement(id)).OfType<ModelCurve>().ToList();
                    if(curves.Count!=4||curves.Any(c=>!(c.GeometryCurve is Line)))throw new ArgumentException("Only initial four-edge wall sketch supported.");
                    var endpoints=curves.SelectMany(c=>new[]{c.GeometryCurve.GetEndPoint(0),c.GeometryCurve.GetEndPoint(1)}).ToList();
                    double sketchTop=endpoints.Max(q=>q.Z),sketchBottom=endpoints.Min(q=>q.Z);
                    if(endpoints.Any(q=>Math.Abs(q.Z-sketchTop)>1e-6&&Math.Abs(q.Z-sketchBottom)>1e-6))throw new ArgumentException("Initial sketch must be rectangular.");
                    if(Math.Min(z0,z1)<=sketchBottom+1/304.8)throw new ArgumentException("Top below profile base.");
                    using(var scope=new SketchEditScope(doc,"Single straight sloping wall top"))
                    {
                        scope.Start(sketch.Id);
                        using(var tx=new Transaction(doc,"Replace wall top edge"))
                        {
                            tx.Start();tx.SetFailureHandlingOptions(tx.GetFailureHandlingOptions().SetFailuresPreprocessor(failures).SetClearAfterRollback(true));
                            var replacement=new List<Curve>();
                            foreach(var curve in curves)
                            {
                                var ends=new[]{curve.GeometryCurve.GetEndPoint(0),curve.GeometryCurve.GetEndPoint(1)};
                                for(int k=0;k<2;k++)if(Math.Abs(ends[k].Z-sketchTop)<1e-6)
                                {
                                    double u=(ends[k]-a).DotProduct(d);ends[k]=new XYZ(ends[k].X,ends[k].Y,fit.At(u*304.8)/304.8);
                                }
                                replacement.Add(Line.CreateBound(ends[0],ends[1]));
                            }
                            doc.Delete(curves.Select(c=>c.Id).ToList());
                            foreach(var curve in replacement)doc.Create.NewModelCurve(curve,sketch.SketchPlane);
                            if(tx.Commit()!=TransactionStatus.Committed)throw new InvalidOperationException("Wall profile update failed.");
                        }
                        scope.Commit(failures);
                    }
                    // Verify after the edit commits, while the outer transaction group can still roll back.
                    var now=(wall.Location as LocationCurve).Curve;
                    for(int k=0;k<2;k++)if(now.GetEndPoint(k).DistanceTo(line.GetEndPoint(k))>1e-7)throw new InvalidOperationException("Wall axis changed.");
                    if(wall.GetTypeId()!=wallTypeId||wall.Flipped!=flipped||Math.Abs(wall.Width-width)>1e-7||wall.get_Parameter(BuiltInParameter.WALL_BOTTOM_IS_ATTACHED).AsInteger()!=bottomAttached)
                        throw new InvalidOperationException("Wall type, thickness, orientation or bottom attachment changed.");
                    foreach(var sample in samples)
                    {
                        var q=sample.Item1;var bottom=FitTrySurfaceZ(wall,q.X,q.Y,true);
                        if(bottom.HasValue!=sample.Item2.HasValue)throw new InvalidOperationException("Wall footprint changed.");
                        if(!bottom.HasValue)continue;
                        maxBottomError=Math.Max(maxBottomError,Math.Abs(bottom.Value-sample.Item2.Value)*304.8);
                        maxTopError=Math.Max(maxTopError,Math.Abs(FitSurfaceZ(wall,q.X,q.Y,false)*304.8-fit.At((q-a).DotProduct(d)*304.8)));
                    }
                    if(maxTopError>1||maxBottomError>1)throw new InvalidOperationException("Wall top/bottom verification exceeded 1 mm.");
                    if(apply&&!dry){if(group.Assimilate()!=TransactionStatus.Committed)throw new InvalidOperationException("Group commit failed.");committed=true;}
                    else group.RollBack();
                }
                catch{if(group.GetStatus()==TransactionStatus.Started)group.RollBack();throw;}
            }
            return new{Success=true,Committed=committed,DryRun=dry,TargetSurface=targetSurface,WallId=wall.Id.GetIdValue(),BeamIds=ids,
                HighPoint=new{BeamId=fit.High.Source,AlongWallMm=fit.High.U,ElevationMm=fit.High.Z},
                LowPoint=new{BeamId=fit.Low.Source,AlongWallMm=fit.Low.U,ElevationMm=fit.Low.Z},StartTopMm=z0*304.8,EndTopMm=z1*304.8,
                MaxTopErrorMm=maxTopError,MaxBottomErrorMm=maxBottomError,BeamSurfaceChecks=surfaceChecks,MaxBeamSurfaceErrorMm=maxSurfaceError,BottomAttachmentPreserved=true,VerifiedSamples=samples.Count(s=>s.Item2.HasValue),PreviewToken=token,DynamicAttachment=false};
        }
    }
}
