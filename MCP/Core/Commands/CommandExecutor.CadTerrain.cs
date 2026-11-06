#if REVIT2024_OR_GREATER
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;

namespace RevitMCP.Core
{
    public partial class CommandExecutor
    {
        private sealed class TerrainFailures : IFailuresPreprocessor
        {
            public readonly List<string> Messages = new List<string>();
            public FailureProcessingResult PreprocessFailures(FailuresAccessor a)
            {
                bool error = false;
                foreach (var f in a.GetFailureMessages())
                {
                    Messages.Add(f.GetDescriptionText());
                    if (f.GetSeverity() == FailureSeverity.Warning) a.DeleteWarning(f);
                    else error = true;
                }
                return error ? FailureProcessingResult.ProceedWithRollBack : FailureProcessingResult.Continue;
            }
        }

        // Symbol geometry is local: compose each instance transform exactly once.
        private static void TerrainLines(Document doc, GeometryElement geometry, Transform transform,
            string inheritedLayer, HashSet<string> layers, List<List<XYZ>> lines)
        {
            foreach (GeometryObject g in geometry)
            {
                var style = doc.GetElement(g.GraphicsStyleId) as GraphicsStyle;
                var layer = style?.GraphicsStyleCategory?.Name ?? inheritedLayer;
                if (g is GeometryInstance instance)
                {
                    var symbol = instance.GetSymbolGeometry();
                    if (symbol != null) TerrainLines(doc, symbol, transform.Multiply(instance.Transform), layer, layers, lines);
                }
                else if (layers.Contains(layer ?? ""))
                {
                    IList<XYZ> points = null;
                    if (g is PolyLine poly) points = poly.GetCoordinates();
                    else if (g is Curve curve) points = curve.Tessellate();
                    if (points != null && points.Count > 1) lines.Add(points.Select(transform.OfPoint).ToList());
                }
            }
        }

        // Iterative RDP retains contour endpoints; tolerance is maximum 3D chord deviation.
        private static List<XYZ> TerrainSimplify(List<XYZ> points, double tolerance)
        {
            if (points.Count < 3 || tolerance <= 0) return points;
            var keep = new SortedSet<int> { 0, points.Count - 1 };
            var stack = new Stack<Tuple<int, int>>();
            stack.Push(Tuple.Create(0, points.Count - 1));
            while (stack.Count > 0)
            {
                var range = stack.Pop(); int a = range.Item1, b = range.Item2, index = -1;
                var delta = points[b] - points[a]; double length2 = delta.DotProduct(delta), worst = tolerance;
                for (int i = a + 1; i < b; i++)
                {
                    double t = length2 < 1e-20 ? 0 : Math.Max(0, Math.Min(1, (points[i] - points[a]).DotProduct(delta) / length2));
                    double distance = points[i].DistanceTo(points[a] + delta * t);
                    if (distance > worst) { worst = distance; index = i; }
                }
                if (index >= 0) { keep.Add(index); stack.Push(Tuple.Create(a, index)); stack.Push(Tuple.Create(index, b)); }
            }
            return keep.Select(i => points[i]).ToList();
        }

        private object CreateToposolidFromCad(JObject p)
        {
            if (p["spotElevationUpdate"] is JObject spots) return UpdateTerrainSpots(p, spots);
            if (p["excavation"] is JObject excavation) return ExcavateCadTerrain(p, excavation);
            var doc = _uiApp.ActiveUIDocument.Document;
            var cad = doc.GetElement(new ElementId(p.Value<long>("cadId"))) as ImportInstance;
            var level = doc.GetElement(new ElementId(p.Value<long>("levelId"))) as Level;
            if (cad == null || level == null) throw new ArgumentException("Valid cadId and levelId are required.");
            var layers = new HashSet<string>((p["layers"] as JArray)?.Values<string>() ?? new[] { "AC", "IDX" }, StringComparer.Ordinal);
            double datum = p.Value<double?>("surveyZeroMeters") ?? 246.69;
            double toleranceMm = p.Value<double?>("simplifyToleranceMm") ?? 20;
            if (double.IsNaN(datum) || double.IsInfinity(datum) || toleranceMm < 0 || toleranceMm > 100 || double.IsNaN(toleranceMm) || layers.Count == 0)
                throw new ArgumentException("Invalid datum or simplification tolerance (0–100 mm).");
            var types = new FilteredElementCollector(doc).OfClass(typeof(ToposolidType)).Cast<ToposolidType>().OrderBy(t => t.Name).ToList();
            long requestedType = p.Value<long?>("typeId") ?? 0;
            var type = requestedType == 0 ? types.FirstOrDefault() : types.FirstOrDefault(t => t.Id.Value == requestedType);
            if (type == null) throw new ArgumentException("No valid Toposolid type; specify an existing typeId.");
            var lines = new List<List<XYZ>>();
            var geometry = cad.get_Geometry(new Options { IncludeNonVisibleObjects = true });
            if (geometry == null) throw new InvalidOperationException("CAD geometry unavailable.");
            TerrainLines(doc, geometry, Transform.Identity, null, layers, lines);
            if (lines.Count == 0) throw new InvalidOperationException("No geometry in requested contour layers.");
            if (lines.Any(l => l.Max(v => v.Z) - l.Min(v => v.Z) > 1 / 304.8))
                throw new InvalidOperationException("A contour is not horizontal within 1 mm; inspect source geometry.");
            int rawCount = lines.Sum(l => l.Count);
            var unique = new Dictionary<string, XYZ>();
            foreach (var v in lines.SelectMany(l => TerrainSimplify(l, toleranceMm / 304.8)))
            {
                var q = new XYZ(v.X, v.Y, v.Z - datum * 1000 / 304.8 + level.Elevation);
                if (!new[] { q.X, q.Y, q.Z }.All(d => !double.IsNaN(d) && !double.IsInfinity(d))) throw new InvalidOperationException("Nonfinite CAD point.");
                string key = Math.Round(q.X * 3048).ToString(CultureInfo.InvariantCulture) + ":" + Math.Round(q.Y * 3048).ToString(CultureInfo.InvariantCulture);
                if (unique.TryGetValue(key, out var existing) && Math.Abs(existing.Z - q.Z) > 1 / 304.8)
                    throw new InvalidOperationException("Coincident XY contours have conflicting elevations.");
                unique[key] = q;
            }
            var points = unique.Values.OrderBy(v => v.X).ThenBy(v => v.Y).ThenBy(v => v.Z).ToList();
            if (points.Count < 3 || points.Count > 10000) throw new InvalidOperationException($"Prepared {points.Count} points; require 3–10000. Adjust simplifyToleranceMm after review.");
            var seed = cad.UniqueId + "|" + level.UniqueId + "|" + type.UniqueId + "|" + string.Join(";", points.Select(v => v.X.ToString("R", CultureInfo.InvariantCulture) + "," + v.Y.ToString("R", CultureInfo.InvariantCulture) + "," + v.Z.ToString("R", CultureInfo.InvariantCulture)));
            string token;
            using (var sha = SHA256.Create()) token = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(seed))).Replace("-", "");
            string marker = "AbbyTerrain:" + cad.UniqueId;
            var duplicates = new FilteredElementCollector(doc).OfClass(typeof(Toposolid)).Cast<Toposolid>()
                .Where(t => t.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.AsString() == marker).Select(t => t.Id.Value).ToArray();
            bool apply = p.Value<bool?>("apply") == true && p.Value<bool?>("dryRun") == false;
            var result = new JObject
            {
                ["DryRun"] = !apply, ["CadId"] = cad.Id.Value, ["LevelId"] = level.Id.Value,
                ["TypeId"] = type.Id.Value, ["TypeName"] = type.Name, ["ContourCount"] = lines.Count,
                ["RawPoints"] = rawCount, ["PreparedPoints"] = points.Count, ["SimplifyToleranceMm"] = toleranceMm,
                ["SurveyZeroMeters"] = datum, ["MinSurfaceZMm"] = points.Min(v => v.Z) * 304.8,
                ["SourceMinZMm"] = lines.SelectMany(l => l).Min(v => v.Z) * 304.8,
                ["SourceMaxZMm"] = lines.SelectMany(l => l).Max(v => v.Z) * 304.8,
                ["MinXMm"] = points.Min(v => v.X) * 304.8, ["MaxXMm"] = points.Max(v => v.X) * 304.8,
                ["MinYMm"] = points.Min(v => v.Y) * 304.8, ["MaxYMm"] = points.Max(v => v.Y) * 304.8,
                ["MaxSurfaceZMm"] = points.Max(v => v.Z) * 304.8, ["PreviewToken"] = token,
                ["ExistingTerrainIds"] = JArray.FromObject(duplicates), ["Created"] = false,
                ["Boundary"] = "Revit point-cloud envelope; not a surveyed property boundary",
                ["Layers"] = JArray.FromObject(layers.OrderBy(x => x)),
                ["SamplePointsMm"] = JArray.FromObject(points.Take(5).Select(v => new { X = v.X * 304.8, Y = v.Y * 304.8, Z = v.Z * 304.8 }))
            };
            if (!apply) return result;
            if (p.Value<string>("previewToken") != token) throw new InvalidOperationException("Preview token missing/stale; preview current geometry first.");
            if (duplicates.Length > 0) throw new InvalidOperationException("Terrain already created for this CAD; duplicate creation blocked.");
            var failures = new TerrainFailures();
            using (var group = new TransactionGroup(doc, "Abby CAD terrain"))
            {
                group.Start();
                try
                {
                    Toposolid terrain;
                    using (var tx = new Transaction(doc, "Create terrain from contour points"))
                    {
                        tx.Start();
                        tx.SetFailureHandlingOptions(tx.GetFailureHandlingOptions().SetFailuresPreprocessor(failures).SetClearAfterRollback(true));
                        terrain = Toposolid.Create(doc, points, type.Id, level.Id);
                        terrain.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS).Set(marker);
                        if (tx.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("Terrain transaction rolled back: " + string.Join("; ", failures.Messages));
                    }
                    var vertices = terrain.GetSlabShapeEditor().SlabShapeVertices.Cast<SlabShapeVertex>().Select(v => v.Position).ToList();
                    if (vertices.Count < 3 || Math.Abs(vertices.Min(v => v.Z) - points.Min(v => v.Z)) > 1 / 304.8 || Math.Abs(vertices.Max(v => v.Z) - points.Max(v => v.Z)) > 1 / 304.8)
                        throw new InvalidOperationException("Post-commit surface elevation verification failed; rolled back.");
                    if (Math.Abs(vertices.Min(v => v.X) - points.Min(v => v.X)) > 1 / 304.8 || Math.Abs(vertices.Max(v => v.X) - points.Max(v => v.X)) > 1 / 304.8 ||
                        Math.Abs(vertices.Min(v => v.Y) - points.Min(v => v.Y)) > 1 / 304.8 || Math.Abs(vertices.Max(v => v.Y) - points.Max(v => v.Y)) > 1 / 304.8)
                        throw new InvalidOperationException("Post-commit plan alignment verification failed; rolled back.");
                    if (group.Assimilate() != TransactionStatus.Committed) throw new InvalidOperationException("Terrain group commit failed.");
                    result["Created"] = true; result["ElementId"] = terrain.Id.Value;
                    result["VerifiedVertices"] = vertices.Count;
                    result["Warnings"] = JArray.FromObject(failures.Messages);
                }
                catch { if (group.GetStatus() == TransactionStatus.Started) group.RollBack(); throw; }
            }
            return result;
        }
    }
}
#endif
