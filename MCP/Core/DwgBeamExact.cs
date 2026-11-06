using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace RevitMCP.Core
{
    public static partial class DwgBeamExecutor
    {
        sealed class ExactBeamFailures : IFailuresPreprocessor
        {
            public string Error;
            public readonly List<string> Warnings = new List<string>();
            public FailureProcessingResult PreprocessFailures(FailuresAccessor accessor)
            {
                var messages = accessor.GetFailureMessages();
                var blocking = new List<string>();
                foreach (var message in messages)
                {
                    if (message.GetSeverity() == FailureSeverity.Warning &&
                        message.GetFailureDefinitionId() == BuiltInFailures.InaccurateFailures.InaccurateBeamOrBrace)
                    {
                        Warnings.Add(message.GetDescriptionText());
                        accessor.DeleteWarning(message);
                    }
                    else blocking.Add(message.GetDescriptionText());
                }
                if (blocking.Count == 0) return FailureProcessingResult.Continue;
                Error = string.Join("; ", blocking.Distinct());
                return FailureProcessingResult.ProceedWithRollBack;
            }
        }
        static IEnumerable<Solid> ExactBeamSolids(GeometryElement geometry)
        {
            if (geometry == null) yield break;
            foreach (var obj in geometry)
            {
                if (obj is Solid solid && solid.Volume > 1e-9) yield return solid;
                if (obj is GeometryInstance gi)
                    foreach (var item in ExactBeamSolids(gi.GetInstanceGeometry())) yield return item;
            }
        }
        static void ExactBeamSet(FamilyInstance beam, BuiltInParameter key, double value)
        {
            var parameter = beam.get_Parameter(key);
            if (parameter == null || parameter.IsReadOnly) throw new InvalidOperationException("Cannot set " + key);
            if (parameter.StorageType == StorageType.Integer) parameter.Set((int)value); else parameter.Set(value);
        }
        static bool ExactBeamOverlap(Curve curve, BeamData b)
        {
            if (!(curve is Line)) return false;
            var a = curve.GetEndPoint(0); var z = curve.GetEndPoint(1);
            var dir = (b.EndPoint - b.StartPoint).Normalize();
            var delta = z - a;
            if (Math.Abs(delta.Normalize().DotProduct(dir)) < 0.999999) return false;
            var side = new XYZ(-dir.Y, dir.X, 0);
            if (Math.Abs((a - b.StartPoint).DotProduct(side)) > 5 * MmFt) return false;
            double t0 = (a - b.StartPoint).DotProduct(dir), t1 = (z - b.StartPoint).DotProduct(dir);
            return Math.Min(b.LengthMm * MmFt, Math.Max(t0, t1)) - Math.Max(0, Math.Min(t0, t1)) > 5 * MmFt;
        }
        static double ExactPhysicalTop(FamilyInstance beam)
        {
            var points = ExactBeamSolids(beam.get_Geometry(new Options { DetailLevel = ViewDetailLevel.Fine }))
                .SelectMany(s => s.Edges.Cast<Edge>()).SelectMany(e => e.Tessellate()).ToList();
            return points.Count == 0 ? double.NaN : points.Max(v => v.Z);
        }
        static List<BeamData> MergeExactPaths(List<BeamData> source, double tolerance)
        {
            var remaining = source.OrderByDescending(b => b.LengthMm).ToList();
            var result = new List<BeamData>();
            while (remaining.Count > 0)
            {
                var seed = remaining[0]; var dir = (seed.EndPoint - seed.StartPoint).Normalize();
                var side = new XYZ(-dir.Y, dir.X, 0);
                var group = remaining.Where(b => Math.Abs((b.EndPoint - b.StartPoint).Normalize().DotProduct(dir)) > 0.99995 &&
                    Math.Abs((b.StartPoint - seed.StartPoint).DotProduct(side)) <= tolerance &&
                    Math.Abs((b.EndPoint - seed.StartPoint).DotProduct(side)) <= tolerance).ToList();
                foreach (var b in group) remaining.Remove(b);
                var points = group.SelectMany(b => new[] { b.StartPoint, b.EndPoint }).OrderBy(v => v.DotProduct(dir)).ToList();
                var a = points.First(); var z = points.Last(); var mergedDir = (z - a).Normalize();
                var mergedSide = new XYZ(-mergedDir.Y, mergedDir.X, 0);
                if (points.Any(v => Math.Abs((v - a).DotProduct(mergedSide)) > tolerance))
                    throw new InvalidOperationException("Merged path exceeds lateral tolerance.");
                result.Add(new BeamData { StartPoint = a, EndPoint = z, Center = (a + z) / 2,
                    LengthMm = a.DistanceTo(z) * FtMm, WidthMm = seed.WidthMm });
            }
            return result;
        }
        static object CreateExactDwgBeams(Document doc, JObject p)
        {
            var view = doc.ActiveView as ViewPlan ?? throw new ArgumentException("A plan view is required.");
            string layer = p.Value<string>("layerName");
            var symbol = doc.GetElement(new ElementId(p.Value<long>("exactTypeId"))) as FamilySymbol;
            var level = doc.GetElement(new ElementId(p.Value<long>("referenceLevelId"))) as Level;
            if (string.IsNullOrWhiteSpace(layer) || symbol == null || level == null || p["zOffsetMm"] == null ||
                symbol.Category.Id != new ElementId(BuiltInCategory.OST_StructuralFraming))
                throw new ArgumentException("Requires CAD layer, structural framing type, referenceLevelId and zOffsetMm.");
            if (p.Value<string>("familyName") != symbol.Family.Name) throw new ArgumentException("familyName differs from exactTypeId.");
            double offset = p.Value<double>("zOffsetMm") * MmFt, top = level.Elevation + offset;
            if (double.IsNaN(offset) || double.IsInfinity(offset)) throw new ArgumentException("Invalid offset.");
            var sourceLines = CollectLayerLines(doc, view, layer);
            var beams = ExtractBeamCenterLines(sourceLines, ReadCadBeamWidth(p), p.Value<double?>("cadWidthToleranceMm") ?? 5);
            if (beams.Count == 0 || beams.Count > 500 || beams.Count != p.Value<int>("expectedCount"))
                throw new ArgumentException($"CAD candidate count {beams.Count} differs from expectedCount.");
            int sourceCount = beams.Count;
            var originalCandidates = beams.ToList();
            bool merge = p.Value<bool?>("mergeContinuous") == true;
            double mergeTolerance = (p.Value<double?>("mergeToleranceMm") ?? 50) * MmFt;
            if (mergeTolerance < 0 || mergeTolerance > 100 * MmFt || double.IsNaN(mergeTolerance)) throw new ArgumentException("mergeToleranceMm must be 0..100.");
            if (merge) beams = MergeExactPaths(beams, mergeTolerance);
            else beams = beams.OrderByDescending(b => b.LengthMm).ToList();
            var replacementIds = (p["replaceElementIds"] as JArray ?? new JArray()).Values<long>().Distinct().Select(id => new ElementId(id)).ToList();
            bool replaceMerged = p.Value<bool?>("replaceMergedPaths") == true;
            bool replaceSegments = p.Value<bool?>("replaceOriginalSegments") == true;
            if (replaceSegments && (merge || replaceMerged || replacementIds.Count == 0 || !ReadCadBeamWidth(p).HasValue))
                throw new ArgumentException("replaceOriginalSegments requires explicit IDs, CAD width and nonmerged mode.");
            if (replaceMerged && (merge || replacementIds.Count == 0))
                throw new ArgumentException("replaceMergedPaths requires explicit replacement IDs and mergeContinuous=false.");
            if (replacementIds.Count > 0 && !merge && !replaceMerged && !replaceSegments)
                throw new ArgumentException("Segmented replacement requires replaceMergedPaths=true.");
            var replacementCandidates = replaceMerged ? MergeExactPaths(originalCandidates, mergeTolerance) : originalCandidates;
            if (replaceSegments) replacementCandidates = ExtractBeamCenterLines(sourceLines, ReadCadBeamWidth(p), p.Value<double?>("cadWidthToleranceMm") ?? 5, false);
            var replacementState = new JArray();
            foreach (var id in replacementIds)
            {
                var old = doc.GetElement(id) as FamilyInstance;
                if (old == null || old.Pinned || old.GroupId != ElementId.InvalidElementId || old.Symbol.Id != symbol.Id ||
                    old.get_Parameter(BuiltInParameter.INSTANCE_REFERENCE_LEVEL_PARAM)?.AsElementId() != level.Id ||
                    Math.Abs(ExactPhysicalTop(old) - top) > MmFt || !(old.Location is LocationCurve oldLocation))
                    throw new ArgumentException("Replacement beam changed or incompatible: " + id);
                var a = oldLocation.Curve.GetEndPoint(0); var z = oldLocation.Curve.GetEndPoint(1);
                Func<XYZ, XYZ, double> xy = (v, w) => new XYZ(v.X-w.X, v.Y-w.Y, 0).GetLength();
                if (!replacementCandidates.Any(b => (xy(a,b.StartPoint)<MmFt && xy(z,b.EndPoint)<MmFt) || (xy(z,b.StartPoint)<MmFt && xy(a,b.EndPoint)<MmFt)))
                    throw new ArgumentException("Replacement beam does not match a CAD candidate: " + id);
                replacementState.Add(new JObject { ["Id"] = id.Value, ["UniqueId"] = old.UniqueId,
                    ["Start"] = new JArray(a.X,a.Y,a.Z), ["End"] = new JArray(z.X,z.Y,z.Z) });
            }
            var existing = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_StructuralFraming)
                .WhereElementIsNotElementType().OfType<FamilyInstance>().Where(e => !replacementIds.Contains(e.Id)).ToList();
            var rows = new JArray();
            var plans = new List<BeamData>();
            foreach (var b in beams)
            {
                var overlap = existing.FirstOrDefault(e => e.Location is LocationCurve lc && ExactBeamOverlap(lc.Curve, b) &&
                    Math.Abs(ExactPhysicalTop(e) - top) < 5 * MmFt);
                bool sameBatch = plans.Any(q => ExactBeamOverlap(Line.CreateBound(q.StartPoint, q.EndPoint), b));
                var row = new JObject { ["Status"] = overlap != null || sameBatch ? "Skipped" : "Planned",
                    ["StartMm"] = new JArray(b.StartPoint.X * FtMm, b.StartPoint.Y * FtMm),
                    ["EndMm"] = new JArray(b.EndPoint.X * FtMm, b.EndPoint.Y * FtMm),
                    ["CadWidthMm"] = b.WidthMm, ["LengthMm"] = b.LengthMm };
                if (overlap != null) { row["Reason"] = "Existing collinear beam at target top"; row["ExistingId"] = overlap.Id.Value; }
                else if (sameBatch) row["Reason"] = "Overlapping CAD candidate in same batch";
                else plans.Add(b);
                rows.Add(row);
            }
            if (replacementIds.Count > 0 && plans.Count != beams.Count) throw new InvalidOperationException("Replacement overlaps another existing beam; no deletion performed.");
            string payload = new JObject { ["Document"] = doc.Title, ["View"] = view.Id.Value,
                ["Type"] = symbol.UniqueId, ["Level"] = level.UniqueId, ["Top"] = top,
                ["ReplacementState"] = replacementState, ["Rows"] = rows.DeepClone() }.ToString(Newtonsoft.Json.Formatting.None);
            string token;
            using (var sha = SHA256.Create()) token = Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(payload)));
            bool write = p.Value<bool?>("apply") == true && p.Value<bool?>("dryRun") == false;
            if (write && p.Value<string>("previewToken") != token) throw new ArgumentException("Fresh matching previewToken required.");
            var plannedRows = rows.OfType<JObject>().Where(r => r.Value<string>("Status") == "Planned").ToList();
            var failures = new ExactBeamFailures();
            using (var group = new TransactionGroup(doc, "Exact CAD beam batch"))
            using (var tx = new Transaction(doc, "Create CAD beams at specified top"))
            {
                group.Start(); tx.Start();
                tx.SetFailureHandlingOptions(tx.GetFailureHandlingOptions().SetFailuresPreprocessor(failures).SetClearAfterRollback(true));
                try
                {
                    if (replacementIds.Count > 0)
                    {
                        var dependencies = replacementIds.SelectMany(id => doc.GetElement(id).GetDependentElements(null)).Distinct()
                            .Select(id => doc.GetElement(id)).Where(e => e != null).ToDictionary(e => e.Id, e => e.GetType().Name);
                        // Revit creates a private sketch plane with a beam. Its automatic cleanup
                        // is allowed; tags, dimensions and every other dependent remain protected.
                        var ownedPlanes = new HashSet<ElementId>(dependencies.Keys.Where(id =>
                        {
                            var dependent = doc.GetElement(id);
                            if (dependent is SketchPlane) return true;
                            // Native bookkeeping can be exposed only as the base Element class.
                            // Exclude every named, typed, categorized, geometric or owning element.
                            if (dependent.GetType() != typeof(Element) || dependent.Category != null ||
                                !string.IsNullOrEmpty(dependent.Name) || dependent.GetTypeId() != ElementId.InvalidElementId ||
                                dependent.GetDependentElements(null).Any(child => child != id)) return false;
                            var geometry = dependent.get_Geometry(new Options());
                            return geometry == null || !geometry.Cast<GeometryObject>().Any();
                        }));
                        var removed = doc.Delete(replacementIds);
                        var unexpected = removed.Where(id => !replacementIds.Contains(id) && !ownedPlanes.Contains(id)).ToList();
                        if (unexpected.Count > 0) throw new InvalidOperationException("Replacement would delete protected dependents: " +
                            string.Join(", ", unexpected.Select(id => id + ":" + (dependencies.ContainsKey(id) ? dependencies[id] : "unclassified"))) + "; rolled back.");
                    }
                    if (!symbol.IsActive) { symbol.Activate(); doc.Regenerate(); }
                    for (int i = 0; i < plans.Count; i++)
                    {
                        var b = plans[i];
                        var a = new XYZ(b.StartPoint.X, b.StartPoint.Y, level.Elevation);
                        var z = new XYZ(b.EndPoint.X, b.EndPoint.Y, level.Elevation);
                        var beam = doc.Create.NewFamilyInstance(Line.CreateBound(a, z), symbol, level, StructuralType.Beam);
                        StructuralFramingUtils.DisallowJoinAtEnd(beam, 0); StructuralFramingUtils.DisallowJoinAtEnd(beam, 1);
                        ExactBeamSet(beam, BuiltInParameter.Y_JUSTIFICATION, 1);
                        ExactBeamSet(beam, BuiltInParameter.Y_OFFSET_VALUE, 0);
                        ExactBeamSet(beam, BuiltInParameter.Z_JUSTIFICATION, 0);
                        ExactBeamSet(beam, BuiltInParameter.Z_OFFSET_VALUE, offset);
                        ExactBeamSet(beam, BuiltInParameter.STRUCTURAL_BEAM_END0_ELEVATION, 0);
                        ExactBeamSet(beam, BuiltInParameter.STRUCTURAL_BEAM_END1_ELEVATION, 0);
                        plannedRows[i]["ElementId"] = beam.Id.Value;
                    }
                    doc.Regenerate();
                    for (int i = 0; i < plans.Count; i++)
                    {
                        var beam = (FamilyInstance)doc.GetElement(new ElementId(plannedRows[i].Value<long>("ElementId")));
                        var b = plans[i];
                        var solids = ExactBeamSolids(beam.get_Geometry(new Options { DetailLevel = ViewDetailLevel.Fine })).ToList();
                        var vertices = solids.SelectMany(s => s.Edges.Cast<Edge>()).SelectMany(e => e.Tessellate()).ToList();
                        if (vertices.Count == 0) throw new InvalidOperationException("Beam has no solid.");
                        var dir = (b.EndPoint - b.StartPoint).Normalize(); var side = new XYZ(-dir.Y, dir.X, 0);
                        double crossCenter = (vertices.Min(v => v.DotProduct(side)) + vertices.Max(v => v.DotProduct(side))) / 2;
                        var curve = ((LocationCurve)beam.Location).Curve;
                        if (Math.Abs(vertices.Max(v => v.Z) - top) > 1 * MmFt ||
                            Math.Abs(crossCenter - b.Center.DotProduct(side)) > 1 * MmFt ||
                            Math.Abs(curve.Length * FtMm - b.LengthMm) > 1 ||
                            beam.get_Parameter(BuiltInParameter.INSTANCE_REFERENCE_LEVEL_PARAM).AsElementId() != level.Id)
                            throw new InvalidOperationException("Actual beam top, center, length or reference level verification failed.");
                        plannedRows[i]["TopMm"] = vertices.Max(v => v.Z) * FtMm;
                        plannedRows[i]["BottomMm"] = vertices.Min(v => v.Z) * FtMm;
                        plannedRows[i]["Status"] = write ? "Created" : "Planned";
                    }
                    if (tx.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("Revit rolled back: " + failures.Error);
                    if (write) group.Assimilate();
                    else { group.RollBack(); foreach (var row in plannedRows) row.Remove("ElementId"); }
                }
                catch { if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack(); throw; }
            }
            return new { Scanned = sourceCount, ContinuousPaths = beams.Count, Replaced = write ? replacementIds.Count : 0, Planned = plans.Count, Created = write ? plans.Count : 0,
                Skipped = beams.Count - plans.Count, Applied = write, PreviewToken = token,
                WarningCount = failures.Warnings.Count, Warnings = failures.Warnings.Distinct().ToList(),
                TypeName = symbol.Name, ReferenceLevel = level.Name, ZOffsetMm = offset * FtMm, Results = rows };
        }
    }
}
