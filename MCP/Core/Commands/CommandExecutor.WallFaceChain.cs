using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
namespace RevitMCP.Core
{
    public partial class CommandExecutor
    {
        private object CreateWallFaceChain(JObject args)
        {
            var doc = _uiApp.ActiveUIDocument.Document;
            double Num(JToken t) {
                if(t==null || (t.Type!=JTokenType.Integer && t.Type!=JTokenType.Float))
                    throw new ArgumentException("Numeric coordinates are required.");
                var v=t.Value<double>();
                if(double.IsNaN(v)||double.IsInfinity(v))throw new ArgumentException("Non-finite number.");
                return v;
            }
            bool Flag(string key,bool fallback) {
                if(args[key]==null)return fallback;
                if(args[key].Type!=JTokenType.Boolean)throw new ArgumentException(key+" must be boolean.");
                return args[key].Value<bool>();
            }
            if(args["wallIds"]!=null && args["elementIds"]!=null)throw new ArgumentException("Use wallIds or elementIds, not both.");
            bool mixed=args["elementIds"]!=null;
            var ids=(args["elementIds"]??args["wallIds"]) as JArray;
            if(ids==null||ids.Count<2||ids.Any(t=>t.Type!=JTokenType.Integer||t.Value<long>()<=0)||
                ids.Values<long>().Distinct().Count()!=ids.Count)throw new ArgumentException("Two or more distinct wallIds are required.");
            var view=doc.GetElement(new ElementId(args["viewId"].Value<long>())) as ViewPlan;
            if(view==null||view.IsTemplate||view.Id!=doc.ActiveView.Id||Math.Abs(view.ViewDirection.Z)<0.999999)
                throw new ArgumentException("The active horizontal plan view is required.");
            var visible=new HashSet<long>(new FilteredElementCollector(doc,view.Id).WhereElementIsNotElementType()
                .Select(e=>(long)e.Id.GetIdValue()));
            var walls=ids.Values<long>().Select(id=>doc.GetElement(new ElementId(id))).ToList();
            bool Allowed(Element e)=>e is Wall w?w.WallType.Kind==WallKind.Basic:
                mixed && e is FamilyInstance && e.Category!=null &&
                (e.Category.Id.GetIdValue()==(long)BuiltInCategory.OST_StructuralColumns || e.Category.Id.GetIdValue()==(long)BuiltInCategory.OST_Columns);
            if(walls.Any(w=>w==null||!visible.Contains((long)w.Id.GetIdValue())||w.IsHidden(view)||!Allowed(w)))
                throw new ArgumentException("All targets must be visible local basic walls or columns.");
            var lines=walls.OfType<Wall>().Select(w=>(w.Location as LocationCurve)?.Curve as Line).ToList();
            if(lines.Any(l=>l==null||Math.Abs(l.Direction.Z)>1e-8))
                throw new ArgumentException("Only straight vertical walls with horizontal axes are supported.");
            var dir=new XYZ(Num(args["direction"]?["x"]),Num(args["direction"]?["y"]),
                args["direction"]?["z"]==null?0:Num(args["direction"]["z"]));
            if(dir.GetLength()<1e-8||Math.Abs(dir.Z)>1e-8)throw new ArgumentException("A horizontal dimension direction is required.");
            dir=dir.Normalize();
            if(lines.Any(l=>Math.Abs(l.Direction.DotProduct(dir))>1e-6))
                throw new ArgumentException("All wall axes must be perpendicular to the dimension direction.");
            if(args["counterDirection"]!=null) {
                var token=args["counterDirection"];var back=new XYZ(Num(token["x"]),Num(token["y"]),token["z"]==null?0:Num(token["z"]));
                if(back.GetLength()<1e-8||back.Normalize().DotProduct(dir)>-0.999999)
                    throw new ArgumentException("counterDirection must oppose direction.");
            }
            var origin=new XYZ(Num(args["origin"]?["x"]),Num(args["origin"]?["y"]),Num(args["origin"]?["z"]))/304.8;
            var samples=new Dictionary<long,XYZ>();
            if(args["samplePoints"]!=null) {
                if(!(args["samplePoints"] is JArray sampleArray))throw new ArgumentException("samplePoints must be an array.");
                foreach(var sample in sampleArray) {
                    if(sample["elementId"]?.Type!=JTokenType.Integer)throw new ArgumentException("samplePoints requires elementId.");
                    long id=sample["elementId"].Value<long>();
                    if(!ids.Values<long>().Contains(id)||samples.ContainsKey(id))throw new ArgumentException("samplePoints must uniquely identify selected elements.");
                    samples[id]=new XYZ(Num(sample["x"]),Num(sample["y"]),Num(sample["z"]))/304.8;
                }
            }
            var tangent=dir.CrossProduct(XYZ.BasisZ);
            var offset=(args["offsetMm"]==null?500:Num(args["offsetMm"]))/304.8;
            bool dry=Flag("dryRun",true),apply=Flag("apply",false);
            var type=doc.GetElement(args["dimensionTypeId"]==null?doc.GetDefaultElementTypeId(ElementTypeGroup.LinearDimensionType):
                new ElementId(args["dimensionTypeId"].Value<long>())) as DimensionType;
            if(type==null||type.StyleType!=DimensionStyleType.Linear)throw new ArgumentException("A linear DimensionType is required.");
            var output=new JObject {["Success"]=true,["Mode"]=mixed?"explicit_element_chain":"explicit_wall_chain",["DryRun"]=dry,["Apply"]=apply,
                ["Committed"]=false,["ViewId"]=view.Id.GetIdValue(),["DimensionTypeId"]=type.Id.GetIdValue(),
                ["Scanned"]=walls.Count,["Updated"]=0};
            var diagnostics=new JArray(); output[mixed?"Elements":"Walls"]=diagnostics;
            var all=new List<Tuple<double,Reference,XYZ,long>>();
            foreach(var wall in walls) {
                var row=new JObject {[mixed?"ElementId":"WallId"]=wall.Id.GetIdValue(),["Category"]=wall.Category.Name};diagnostics.Add(row);
                var hits=new List<Tuple<double,Reference,XYZ,long>>();
                var sampleOrigin=samples.TryGetValue(wall.Id.GetIdValue(),out var specific)?specific:origin;
                var normals=new Dictionary<string,XYZ>();
                void AddFace(PlanarFace face,Reference r,Transform transform) {
                    if(face==null||r==null||r.ElementId!=wall.Id)return;
                    var normal=transform.OfVector(face.FaceNormal).Normalize();
                    if(Math.Abs(normal.DotProduct(dir))<0.999999)return;
                    var hit=face.Project(transform.Inverse.OfPoint(sampleOrigin));
                    if(hit==null||!face.IsInside(hit.UVPoint))return;
                    var key=r.ConvertToStableRepresentation(doc);
                    if(normals.ContainsKey(key))return;
                    var point=transform.OfPoint(hit.XYZPoint);normals[key]=normal;
                    hits.Add(Tuple.Create(point.DotProduct(dir),r,point,(long)wall.Id.GetIdValue()));
                }
                if(wall is Wall basicWall) {
                    foreach(var shell in new[]{ShellLayerType.Interior,ShellLayerType.Exterior})
                    foreach(var r in HostObjectUtils.GetSideFaces(basicWall,shell))
                        AddFace(basicWall.GetGeometryObjectFromReference(r) as PlanarFace,r,Transform.Identity);
                }else {
                    // Original symbol geometry preserves references; transformed copies do not.
                    void Walk(GeometryElement geometry,Transform transform) {
                        if(geometry==null)return;
                        foreach(var obj in geometry) {
                            if(obj is Solid solid && solid.Volume>1e-9)
                                foreach(Face f in solid.Faces)if(f is PlanarFace p)AddFace(p,p.Reference,transform);
                            if(obj is GeometryInstance instance)
                                Walk(instance.GetSymbolGeometry(),transform.Multiply(instance.Transform));
                        }
                    }
                    Walk(wall.get_Geometry(new Options {ComputeReferences=true,IncludeNonVisibleObjects=false,DetailLevel=ViewDetailLevel.Fine}),Transform.Identity);
                }
                hits=hits.OrderBy(h=>h.Item1).ToList();
                if(hits.Count!=2||hits[1].Item1-hits[0].Item1<1/304.8) {
                    row["Status"]="Skipped";row["Reason"]="MissingOrAmbiguousSideFacesAtSamplePoint";continue;
                }
                if(normals[hits[0].Item2.ConvertToStableRepresentation(doc)].DotProduct(dir)>-0.999999||
                    normals[hits[1].Item2.ConvertToStableRepresentation(doc)].DotProduct(dir)<0.999999) {
                    row["Status"]="Skipped";row["Reason"]="InvalidSideFaceOrientation";continue;
                }
                row["Status"]="Ready";row["Faces"]=new JArray(hits.Select(h=>new JObject {
                    ["StableReference"]=h.Item2.ConvertToStableRepresentation(doc),
                    ["PointMm"]=new JArray(h.Item3.X*304.8,h.Item3.Y*304.8,h.Item3.Z*304.8)}));
                all.AddRange(hits);
            }
            int skipped=diagnostics.Count(r=>(string)r["Status"]=="Skipped");
            output["Skipped"]=skipped;
            // Do not silently bridge a missing/ambiguous selected wall.
            if(skipped>0){output["Status"]="Skipped";output["Reason"]="IncompleteChain";return output;}
            all=all.OrderBy(h=>h.Item1).ToList();
            for(int i=0;i<all.Count;i+=2)
                if(all[i].Item4!=all[i+1].Item4|| (i>0 && (all[i].Item1-all[i-1].Item1)*304.8<1)) {
                    output["Status"]="Skipped";output["Reason"]="OverlappingOrTouchingWalls";return output;
                }
            var segments=new JArray();
            for(int i=1;i<all.Count;i++)segments.Add(new JObject {
                ["Kind"]=all[i-1].Item4==all[i].Item4?(doc.GetElement(new ElementId(all[i].Item4)) is Wall?"WallThickness":"ColumnWidth"):"Clearance",
                [mixed?"FromElementId":"FromWallId"]=all[i-1].Item4,[mixed?"ToElementId":"ToWallId"]=all[i].Item4,
                ["ValueMm"]=(all[i].Item1-all[i-1].Item1)*304.8});
            output["Segments"]=segments;output["Planned"]=1;output["Status"]="Planned";
            var keys=new HashSet<string>(all.Select(h=>h.Item2.ConvertToStableRepresentation(doc)));
            foreach(Dimension existing in new FilteredElementCollector(doc,view.Id).OfClass(typeof(Dimension))) {
                if(existing.OwnerViewId!=view.Id||existing.References==null||existing.References.Size!=all.Count)continue;
                if(keys.SetEquals(existing.References.Cast<Reference>().Select(r=>r.ConvertToStableRepresentation(doc)))) {
                    output["Status"]="Unchanged";output["Planned"]=0;output["DimensionId"]=existing.Id.GetIdValue();
                    output["ExistingDimensionTypeId"]=existing.GetTypeId().GetIdValue();return output;
                }
            }
            if(dry||!apply)return output;
            XYZ InPlane(XYZ point) {
                // All references share one dimension line, even when face samples use different heights/stations.
                var p=origin+dir*((point-origin).DotProduct(dir))+tangent*offset;
                return p-view.ViewDirection*((p-view.Origin).DotProduct(view.ViewDirection));
            }
            void Verify(Dimension dim) {
                if(dim==null||dim.GetTypeId()!=type.Id||dim.Segments==null||dim.Segments.Size!=segments.Count)
                    throw new InvalidOperationException("Dimension type or segment count mismatch.");
                var actual=dim.Segments.Cast<DimensionSegment>().Select(s=>s.Value.HasValue?s.Value.Value*304.8:double.NaN).ToArray();
                var expected=segments.Select(s=>s["ValueMm"].Value<double>()).ToArray();
                bool Matches(double[] values)=>values.Zip(expected,(a,b)=>!double.IsNaN(a)&&Math.Abs(a-b)<0.1).All(v=>v);
                if(!Matches(actual)&&!Matches(Enumerable.Reverse(actual).ToArray()))throw new InvalidOperationException("Segment values mismatch.");
                if(!keys.SetEquals(dim.References.Cast<Reference>().Select(r=>r.ConvertToStableRepresentation(doc))))
                    throw new InvalidOperationException("Dimension reference mismatch.");
            }
            using(var group=new TransactionGroup(doc,"Selected wall face chain")) {
                group.Start();
                try {
                    long id;
                    using(var tx=new Transaction(doc,"Selected wall face chain")) {
                        tx.Start();var refs=new ReferenceArray();all.ForEach(h=>refs.Append(h.Item2));
                        var dim=doc.Create.NewDimension(view,Line.CreateBound(InPlane(all.First().Item3),InPlane(all.Last().Item3)),refs,type);
                        doc.Regenerate();Verify(dim);id=dim.Id.GetIdValue();
                        if(tx.Commit()!=TransactionStatus.Committed)throw new InvalidOperationException("Transaction not committed.");
                    }
                    Verify(doc.GetElement(new ElementId(id)) as Dimension);
                    if(group.Assimilate()!=TransactionStatus.Committed)throw new InvalidOperationException("Group not committed.");
                    output["Status"]="Updated";output["Updated"]=1;output["Committed"]=true;output["DimensionId"]=id;
                }catch(Exception ex) {
                    if(group.GetStatus()==TransactionStatus.Started)group.RollBack();
                    output["Success"]=false;output["Status"]="Failed";output["Failed"]=1;output["Error"]=ex.Message;
                    output["RolledBack"]=group.GetStatus()==TransactionStatus.RolledBack;
                }
            }
            return output;
        }
    }
}

