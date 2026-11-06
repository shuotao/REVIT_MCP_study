#if REVIT2024_OR_GREATER
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Newtonsoft.Json.Linq;
using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Polygonize;

namespace RevitMCP.Core
{
    public partial class CommandExecutor
    {
        private static List<Solid> ExcavationSolids(Element element)
        {
            var solids = new List<Solid>();
            Action<GeometryElement> walk = null;
            walk = ge => { foreach (GeometryObject g in ge) { if (g is Solid s && s.Volume > 1e-8) solids.Add(s); else if (g is GeometryInstance gi) walk(gi.GetInstanceGeometry()); } };
            walk(element.get_Geometry(new Options())); return solids;
        }
        private object ExcavateCadTerrain(JObject p, JObject e)
        {
            var doc = _uiApp.ActiveUIDocument.Document;
            var terrain = doc.GetElement(new ElementId(e.Value<long>("toposolidId"))) as Toposolid;
            var level = doc.GetElement(new ElementId(p.Value<long>("levelId"))) as Level;
            if (terrain == null || level == null) throw new ArgumentException("Valid terrain and level required.");
            if (!InstanceVoidCutUtils.CanBeCutWithVoid(terrain)) throw new InvalidOperationException("This terrain cannot be cut with a family void.");
            string layer = e.Value<string>("boundaryLayer") ?? "area";
            string boundaryMode=e.Value<string>("boundaryMode")??"single";
            if(boundaryMode!="single" && boundaryMode!="largest_rectangle")throw new ArgumentException("Unknown boundaryMode.");
            double bottom = level.Elevation + e.Value<double>("bottomOffsetMm") / 304.8;
            if (double.IsNaN(bottom) || double.IsInfinity(bottom)) throw new ArgumentException("Invalid excavation elevation.");
            var candidates = new List<Tuple<ImportInstance,List<List<XYZ>>>>();
            var modelIds=e["boundaryElementIds"] as JArray;
            double gapLimitMm=0;
            Polygon modelBoundary=modelIds!=null?ExcavationModelBoundary(doc,modelIds,out gapLimitMm):null;
            if(modelBoundary!=null)candidates.Add(Tuple.Create((ImportInstance)null,new List<List<XYZ>>{modelBoundary.ExteriorRing.Coordinates.Select(c=>new XYZ(c.X,c.Y,0)).ToList()}));
            foreach (var cad in new FilteredElementCollector(doc, doc.ActiveView.Id).OfClass(typeof(ImportInstance)).Cast<ImportInstance>().Where(c=>modelBoundary==null))
            {
                var lines = new List<List<XYZ>>(); var geometry = cad.get_Geometry(new Options { IncludeNonVisibleObjects = true });
                if (geometry != null) TerrainLines(doc, geometry, Transform.Identity, null, new HashSet<string> { layer }, lines);
                if (lines.Count > 0) candidates.Add(Tuple.Create(cad,lines));
            }
            if (candidates.Count != 1) throw new InvalidOperationException($"Expected one visible CAD containing '{layer}', found {candidates.Count}.");
            var source = candidates[0];
            Polygon selectedRectangle = boundaryMode=="largest_rectangle"
                ? TerrainRectangleBoundary.Select(source.Item2.Select(l=>l.Select(v=>new Coordinate(v.X,v.Y)).ToList()).ToList()) : null;
            // CAD hatches may also expose line geometry on the boundary layer. Prefer an
            // explicit closed polyline over hatch strokes; never pick the largest of multiple loops.
            var closedPaths = source.Item2.Where(l => l.Count >= 4 &&
                new XYZ(l[0].X,l[0].Y,0).DistanceTo(new XYZ(l[l.Count-1].X,l[l.Count-1].Y,0)) <= 0.2/304.8).ToList();
            if (selectedRectangle==null && closedPaths.Count > 1)
            {
                if(p.Value<bool?>("apply")==true && p.Value<bool?>("dryRun")==false)
                    throw new InvalidOperationException($"Found {closedPaths.Count} closed CAD paths; boundary is ambiguous.");
                return new JObject { ["Applied"]=false,["Ready"]=false,["Reason"]="MultipleClosedPaths",
                    ["BoundaryCadId"]=source.Item1.Id.Value,["BoundaryCadName"]=doc.GetElement(source.Item1.GetTypeId()).Name,
                    ["Candidates"]=JArray.FromObject(closedPaths.Select((l,i)=>new { Index=i,PointsMm=l.Select(v=>new[]{v.X*304.8,v.Y*304.8}).ToArray() })) };
            }
            var boundaryLines = selectedRectangle!=null
                ? new List<List<XYZ>> { selectedRectangle.ExteriorRing.Coordinates.Select(c=>new XYZ(c.X,c.Y,0)).ToList() }
                : closedPaths.Count == 1 ? closedPaths : source.Item2;
            var gf = new GeometryFactory(); var polygonizer = new Polygonizer();
            foreach (var line in boundaryLines)
                for (int i = 1; i < line.Count; i++)
                {
                    var a = new Coordinate(Math.Round(line[i-1].X*304.8,1)/304.8,Math.Round(line[i-1].Y*304.8,1)/304.8);
                    var b = new Coordinate(Math.Round(line[i].X*304.8,1)/304.8,Math.Round(line[i].Y*304.8,1)/304.8);
                    if (a.Distance(b) > doc.Application.ShortCurveTolerance) polygonizer.Add(gf.CreateLineString(new[] { a,b }));
                }
            var polygons = polygonizer.GetPolygons().Cast<Polygon>().ToList();
            if (polygons.Count != 1 || polygonizer.GetDangles().Count > 0 || polygonizer.GetCutEdges().Count > 0 || polygonizer.GetInvalidRingLines().Count > 0 || !polygons[0].IsValid || polygons[0].NumInteriorRings != 0)
                throw new InvalidOperationException("Boundary must be one valid closed polygon without holes, open branches or crossing segments.");
            var polygon = polygons[0]; var coords = polygon.ExteriorRing.Coordinates;
            var vertices = terrain.GetSlabShapeEditor().SlabShapeVertices.Cast<SlabShapeVertex>().Select(v=>v.Position).ToList();
            if(vertices.Count<3) throw new InvalidOperationException("Terrain shape vertices unavailable.");
            double top=vertices.Max(v=>v.Z)+10;
            if(bottom>=vertices.Min(v=>v.Z)) throw new InvalidOperationException("Excavation bottom must be below the lowest terrain surface in this mode.");
            var loop = new CurveLoop();
            for(int i=1;i<coords.Length;i++) loop.Append(Line.CreateBound(new XYZ(coords[i-1].X,coords[i-1].Y,bottom),new XYZ(coords[i].X,coords[i].Y,bottom)));
            var cutter = GeometryCreationUtilities.CreateExtrusionGeometry(new[]{loop},XYZ.BasisZ,top-bottom);
            double intersection = ExcavationSolids(terrain).Sum(s=>BooleanOperationsUtils.ExecuteBooleanOperation(s,cutter,BooleanOperationsType.Intersect).Volume);
            if(intersection<1e-6 && e.Value<bool?>("updateExisting")!=true) throw new InvalidOperationException("Boundary does not intersect terrain.");
            string marker="AbbyExcavation:"+terrain.UniqueId;
            var previous=new FilteredElementCollector(doc).OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>().Where(f=>f.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.AsString()==marker).ToList();
            if(previous.Count>1)throw new InvalidOperationException("Multiple existing excavation cutters.");
            if(previous.Count>0 && e.Value<bool?>("updateExisting")!=true)
                throw new InvalidOperationException("Excavation already exists for this terrain.");
            var bbox=terrain.get_BoundingBox(null); double extend=Math.Max(0,bbox.Min.Z-(bottom-300/304.8));
            var plan=new JObject { ["TerrainId"]=terrain.Id.Value,["BoundaryCadId"]=source.Item1?.Id.Value,["BoundaryLayer"]=layer,
                ["BottomZMm"]=bottom*304.8,["ExistingSolidBottomMm"]=bbox.Min.Z*304.8,["ExtendDownMm"]=extend*304.8,
                ["BoundaryAreaM2"]=polygon.Area*0.09290304,["BoundaryPointsMm"]=JArray.FromObject(coords.Select(c=>new[]{c.X*304.8,c.Y*304.8})),
                ["TerrainTypeId"]=terrain.GetTypeId().Value,["SurfaceMinZMm"]=vertices.Min(v=>v.Z)*304.8,["SurfaceMaxZMm"]=vertices.Max(v=>v.Z)*304.8,
                ["TemplatePath"]=e.Value<string>("templatePath"),["ViewId"]=doc.ActiveView.Id.Value };
            plan["BoundarySource"] = closedPaths.Count == 1 ? "ExplicitClosedPolyline" : "StitchedSegments";
            plan["BoundaryMode"] = boundaryMode;
            plan["BoundaryElementIds"]=modelIds;plan["ReplaceCutters"]=JArray.FromObject(previous.Select(v=>v.Id.Value));plan["GapClosureLimitMm"]=gapLimitMm;
            plan["IgnoredOtherPaths"] = source.Item2.Count - boundaryLines.Count;
            string fingerprint=plan.ToString(Newtonsoft.Json.Formatting.None)+string.Join(";",vertices.Select(v=>v.ToString()));
            string token;using(var sha=SHA256.Create()) token=BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(fingerprint))).Replace("-","");
            plan["PreviewToken"]=token;plan["Applied"]=false;
            if(p.Value<bool?>("apply")!=true || p.Value<bool?>("dryRun")!=false)return plan;
            if(p.Value<string>("previewToken")!=token)throw new InvalidOperationException("Preview token missing/stale.");
            string template=e.Value<string>("templatePath");
            if(!File.Exists(template) || !string.Equals(Path.GetExtension(template),".rft",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Valid compatible generic model template required.");
            var failures=new TerrainFailures(); Document familyDoc=null;
            string familyPath=Path.Combine(Path.GetTempPath(),"AbbyExcavation_"+Guid.NewGuid().ToString("N")+".rfa");
            var origin=new XYZ(coords[0].X,coords[0].Y,bottom);
            try
            {
                familyDoc=_uiApp.Application.NewFamilyDocument(template);
                using(var tx=new Transaction(familyDoc,"Create excavation void"))
                {
                    tx.Start();
                    familyDoc.OwnerFamily.FamilyCategory=Category.GetCategory(familyDoc,BuiltInCategory.OST_GenericModel);
                    familyDoc.OwnerFamily.get_Parameter(BuiltInParameter.FAMILY_ALLOW_CUT_WITH_VOIDS).Set(1);
                    familyDoc.FamilyManager.NewType("Excavation");
                    var profile=new CurveArray();
                    for(int i=1;i<coords.Length;i++)profile.Append(Line.CreateBound(new XYZ(coords[i-1].X-origin.X,coords[i-1].Y-origin.Y,0),new XYZ(coords[i].X-origin.X,coords[i].Y-origin.Y,0)));
                    var profiles=new CurveArrArray();profiles.Append(profile);
                    var plane=SketchPlane.Create(familyDoc,Plane.CreateByNormalAndOrigin(XYZ.BasisZ,XYZ.Zero));
                    familyDoc.FamilyCreate.NewExtrusion(false,profiles,plane,top-bottom);
                    if(tx.Commit()!=TransactionStatus.Committed)throw new InvalidOperationException("Void family creation failed.");
                }
                familyDoc.SaveAs(familyPath,new SaveAsOptions { OverwriteExistingFile=false });
                familyDoc.Close(false);familyDoc=null;
                using(var group=new TransactionGroup(doc,"Abby terrain excavation"))
                {
                    group.Start();
                    try
                    {
                        long instanceId;
                        using(var tx=new Transaction(doc,"Extend terrain and cut excavation"))
                        {
                            tx.Start();tx.SetFailureHandlingOptions(tx.GetFailureHandlingOptions().SetFailuresPreprocessor(failures).SetClearAfterRollback(true));
                            foreach(var old in previous){var oldId=old.Id;InstanceVoidCutUtils.RemoveInstanceVoidCut(doc,terrain,old);var deleted=doc.Delete(oldId);if(deleted.Any(id=>id!=oldId))throw new InvalidOperationException("Unexpected cutter deletion dependency; rollback.");}doc.Regenerate();
                            if(extend>1/304.8)
                            {
                                var original=(ToposolidType)doc.GetElement(terrain.GetTypeId());
                                var copy=(ToposolidType)original.Duplicate("Abby_開挖土體_"+terrain.Id.Value+"_"+Guid.NewGuid().ToString("N").Substring(0,6));
                                var cs=copy.GetCompoundStructure();if(cs==null || cs.LayerCount==0)throw new InvalidOperationException("Terrain type has no compound structure.");
                                int index=cs.VariableLayerIndex>=0?cs.VariableLayerIndex:cs.LayerCount-1;
                                cs.SetLayerWidth(index,cs.GetLayerWidth(index)+extend);copy.SetCompoundStructure(cs);terrain.ChangeTypeId(copy.Id);doc.Regenerate();
                                var now=terrain.GetSlabShapeEditor().SlabShapeVertices.Cast<SlabShapeVertex>().Select(v=>v.Position).ToList();
                                if(now.Count!=vertices.Count || vertices.Any(v=>!now.Any(n=>n.DistanceTo(v)<1/304.8)))throw new InvalidOperationException("Deepening moved the ground surface; rollback.");
                            }
                            if(!doc.LoadFamily(familyPath,out Family loaded))throw new InvalidOperationException("Cannot load excavation void.");
                            var symbol=(FamilySymbol)doc.GetElement(loaded.GetFamilySymbolIds().First());symbol.Activate();doc.Regenerate();
                            var instance=doc.Create.NewFamilyInstance(origin,symbol,StructuralType.NonStructural);doc.Regenerate();
                            instance.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS).Set(marker);
                            InstanceVoidCutUtils.AddInstanceVoidCut(doc,terrain,instance);instanceId=instance.Id.Value;
                            if(tx.Commit()!=TransactionStatus.Committed)throw new InvalidOperationException("Excavation rolled back: "+string.Join(";",failures.Messages.Distinct()));
                        }
                        var cutInstance=doc.GetElement(new ElementId(instanceId));
                        if(!InstanceVoidCutUtils.InstanceVoidCutExists(terrain,cutInstance))throw new InvalidOperationException("Cut relation missing after commit.");
                        var solids=ExcavationSolids(terrain);
                        double remaining=solids.Sum(s=>BooleanOperationsUtils.ExecuteBooleanOperation(s,cutter,BooleanOperationsType.Intersect).Volume)*0.028316846592;
                        double bottomArea=solids.SelectMany(s=>s.Faces.Cast<Face>()).OfType<PlanarFace>().Where(f=>f.FaceNormal.Z>0.99 && Math.Abs(f.Origin.Z-bottom)<1/304.8).Sum(f=>f.Area)*0.09290304;
                        if(remaining>0.001 || bottomArea<0.001)throw new InvalidOperationException("Excavation geometry verification failed; rollback.");
                        var finalVertices=terrain.GetSlabShapeEditor().SlabShapeVertices.Cast<SlabShapeVertex>().Select(v=>v.Position).ToList();
                        if(finalVertices.Count!=vertices.Count||vertices.Any(v=>!finalVertices.Any(n=>n.DistanceTo(v)<1/304.8)))throw new InvalidOperationException("Terrain survey vertices changed; rollback.");
                        group.Assimilate();plan["Applied"]=true;plan["VoidInstanceId"]=instanceId;plan["RemainingSoilInCutM3"]=remaining;plan["ExcavationBottomAreaM2"]=bottomArea;plan["Warnings"]=JArray.FromObject(failures.Messages.Distinct());
                    }
                    catch {if(group.GetStatus()==TransactionStatus.Started)group.RollBack();throw;}
                }
            }
            finally {if(familyDoc!=null)familyDoc.Close(false);}
            return plan;
        }
    }
}
#endif
