using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RevitMCP.Core
{
    public static partial class DwgColumnExecutor
    {
        sealed class SteelOutline
        {
            public XYZ Center;
            public double Angle, Width, Depth;
        }
        static double SteelAngleDifference(double a, double b)
        {
            return Math.Abs(Math.Atan2(Math.Sin(2 * (a - b)), Math.Cos(2 * (a - b)))) / 2;
        }
        // Longest outer edges of an H profile are the two flange faces.
        static SteelOutline ReadSteelOutline(IList<XYZ> points)
        {
            var pts = points.ToList();
            if (pts.Count > 1 && pts[0].DistanceTo(pts.Last()) < 0.1 * MmFt) pts.RemoveAt(pts.Count - 1);
            if (pts.Count != 12) throw new InvalidOperationException("Steel CAD outline must have 12 H-profile vertices.");
            var edges = pts.Select((v, i) => pts[(i + 1) % pts.Count] - v).ToList();
            if (edges.Any(e => e.GetLength() < 0.1 * MmFt || Math.Abs(e.Z) > 0.1 * MmFt))
                throw new InvalidOperationException("Invalid H-profile edge.");
            var sorted = edges.OrderByDescending(e => e.GetLength()).ToList();
            double angle = Math.Atan2(sorted[0].Y, sorted[0].X);
            if (SteelAngleDifference(angle, Math.Atan2(sorted[1].Y, sorted[1].X)) > 0.001 ||
                Math.Abs(sorted[0].GetLength() - sorted[1].GetLength()) > 1 * MmFt)
                throw new InvalidOperationException("Ambiguous flange direction.");
            var u = new XYZ(Math.Cos(angle), Math.Sin(angle), 0);
            var v = new XYZ(-u.Y, u.X, 0);
            if (edges.Any(e => Math.Min(Math.Abs(e.Normalize().DotProduct(u)), Math.Abs(e.Normalize().DotProduct(v))) > 0.001))
                throw new InvalidOperationException("H-profile edges are not orthogonal.");
            double xmin = pts.Min(q => q.DotProduct(u)), xmax = pts.Max(q => q.DotProduct(u));
            double ymin = pts.Min(q => q.DotProduct(v)), ymax = pts.Max(q => q.DotProduct(v));
            double area = Math.Abs(pts.Select((q, i) => q.X * pts[(i + 1) % pts.Count].Y - q.Y * pts[(i + 1) % pts.Count].X).Sum()) / 2;
            double ratio = area / ((xmax - xmin) * (ymax - ymin));
            // Validate the thin, symmetric H section rather than treating arbitrary polygons as columns.
            if (ratio < 0.03 || ratio > 0.5 || xmax - xmin < 100 * MmFt || ymax - ymin < 100 * MmFt)
                throw new InvalidOperationException("Outline is not a plausible structural H section.");
            var center = u * ((xmin + xmax) / 2) + v * ((ymin + ymax) / 2);
            if (pts.Any(q => !pts.Any(r => (new XYZ(q.X + r.X, q.Y + r.Y, 0) - center * 2).GetLength() < 1 * MmFt)))
                throw new InvalidOperationException("H-profile is not centrally symmetric.");
            return new SteelOutline { Center = center, Angle = angle, Width = xmax - xmin, Depth = ymax - ymin };
        }
        static IEnumerable<Solid> SteelSolids(GeometryElement geometry)
        {
            if (geometry == null) yield break;
            foreach (var obj in geometry)
            {
                if (obj is Solid solid && solid.Volume > 1e-9) yield return solid;
                if (obj is GeometryInstance gi)
                    foreach (var s in SteelSolids(gi.GetInstanceGeometry())) yield return s;
            }
        }
        static List<XYZ> SteelTopVertices(FamilyInstance column, out double bottom, out double top, out double flangeAngle)
        {
            var solids = SteelSolids(column.get_Geometry(new Options { DetailLevel = ViewDetailLevel.Fine })).ToList();
            var vertices = solids.SelectMany(s => s.Edges.Cast<Edge>()).SelectMany(e => e.Tessellate()).ToList();
            if (vertices.Count == 0) throw new InvalidOperationException("Column has no solid geometry.");
            bottom = vertices.Min(v => v.Z); top = vertices.Max(v => v.Z);
            double topZ = top;
            var faces = solids.SelectMany(s => s.Faces.Cast<Face>()).OfType<PlanarFace>()
                .Where(f => f.FaceNormal.Z > 0.999 && Math.Abs(f.Origin.Z - topZ) < 0.1 * MmFt).ToList();
            var lines = faces.SelectMany(f => f.GetEdgesAsCurveLoops()).SelectMany(l => l).OfType<Line>()
                .OrderByDescending(l => l.Length).ToList();
            if (lines.Count < 2 || Math.Abs(lines[0].Length - lines[1].Length) > 1 * MmFt ||
                Math.Abs(lines[0].Direction.DotProduct(lines[1].Direction)) < 0.999)
                throw new InvalidOperationException("Cannot determine the family flange direction from its actual top face.");
            flangeAngle = Math.Atan2(lines[0].Direction.Y, lines[0].Direction.X);
            return vertices.Where(v => Math.Abs(v.Z - topZ) < 0.1 * MmFt).ToList();
        }
        sealed class SteelFailures : IFailuresPreprocessor
        {
            public FailureProcessingResult PreprocessFailures(FailuresAccessor accessor)
            {
                foreach (var failure in accessor.GetFailureMessages())
                {
                    // Warnings are not silently accepted: the complete batch rolls back.
                    return FailureProcessingResult.ProceedWithRollBack;
                }
                return FailureProcessingResult.Continue;
            }
        }
        static object CreateExactSteelColumns(Document doc, JObject p)
        {
            var view = doc.ActiveView as ViewPlan ?? throw new InvalidOperationException("A plan view is required.");
            string layer = p.Value<string>("layerName");
            if (string.IsNullOrWhiteSpace(layer)) throw new ArgumentException("layerName is required.");
            var symbol = doc.GetElement(new ElementId(p.Value<long>("exactTypeId"))) as FamilySymbol;
            if (symbol == null || symbol.Category.Id != new ElementId(BuiltInCategory.OST_StructuralColumns))
                throw new ArgumentException("exactTypeId must be a structural column type.");
            var level = doc.GetElement(new ElementId(p.Value<long>("topLevelId"))) as Level;
            if (level == null || p["topOffsetMm"] == null || p["downwardLengthMm"] == null)
                throw new ArgumentException("topLevelId, topOffsetMm and downwardLengthMm are required.");
            double offset = p.Value<double>("topOffsetMm") * MmFt;
            double length = p.Value<double>("downwardLengthMm") * MmFt;
            if (double.IsNaN(offset) || double.IsInfinity(offset) || double.IsNaN(length) || double.IsInfinity(length) || length <= 0)
                throw new ArgumentException("Invalid elevation or length.");
            double topZ = level.Elevation + offset, bottomZ = topZ - length;
            var outlines = new List<SteelOutline>();
            Action<GeometryElement> collect = null;
            collect = geometry =>
            {
                if (geometry == null) return;
                foreach (var obj in geometry)
                {
                    if (obj is GeometryInstance gi) { collect(gi.GetInstanceGeometry()); continue; }
                    var style = doc.GetElement(obj.GraphicsStyleId) as GraphicsStyle;
                    if (style?.GraphicsStyleCategory?.Name != layer) continue;
                    if (!(obj is PolyLine poly)) throw new InvalidOperationException("Exact H mode requires closed polylines on the requested layer.");
                    var points = poly.GetCoordinates();
                    if (points.Count < 2 || points[0].DistanceTo(points.Last()) > 0.1 * MmFt)
                        throw new InvalidOperationException("Open CAD outline.");
                    var outline = ReadSteelOutline(points);
                    if (outlines.Any(o => o.Center.DistanceTo(outline.Center) < 1 * MmFt))
                        throw new InvalidOperationException("Duplicate CAD H outlines.");
                    outlines.Add(outline);
                }
            };
            foreach (var cad in new FilteredElementCollector(doc, view.Id).OfClass(typeof(ImportInstance)).Cast<ImportInstance>())
                collect(cad.get_Geometry(new Options { View = view }));
            if (outlines.Count == 0 || outlines.Count != p.Value<int>("expectedCount"))
                throw new InvalidOperationException($"CAD count {outlines.Count} differs from expectedCount.");
            var existing = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_StructuralColumns)
                .WhereElementIsNotElementType().OfType<FamilyInstance>().ToList();
            var rows = new List<object>();
            bool write = p.Value<bool?>("apply") == true && p.Value<bool?>("dryRun") == false;
            int skipped = 0;
            using (var group = new TransactionGroup(doc, "CAD steel column batch"))
            using (var transaction = new Transaction(doc, "Create exact CAD steel columns"))
            {
                group.Start();
                transaction.Start();
                transaction.SetFailureHandlingOptions(transaction.GetFailureHandlingOptions()
                    .SetFailuresPreprocessor(new SteelFailures()).SetClearAfterRollback(true));
                try
                {
                    if (!symbol.IsActive) { symbol.Activate(); doc.Regenerate(); }
                    foreach (var outline in outlines)
                    {
                        var duplicate = existing.FirstOrDefault(e => e.Location is LocationPoint lp &&
                            Math.Sqrt(Math.Pow(lp.Point.X - outline.Center.X, 2) + Math.Pow(lp.Point.Y - outline.Center.Y, 2)) < 1 * MmFt &&
                            e.get_BoundingBox(null) != null && e.get_BoundingBox(null).Max.Z > bottomZ && e.get_BoundingBox(null).Min.Z < topZ);
                        if (duplicate != null) { skipped++; rows.Add(new { status = "Skipped", reason = "Existing column at CAD location", id = duplicate.Id.Value }); continue; }
                        var column = doc.Create.NewFamilyInstance(new XYZ(outline.Center.X, outline.Center.Y, bottomZ), symbol, level, StructuralType.Column);
                        column.get_Parameter(BuiltInParameter.FAMILY_BASE_LEVEL_PARAM).Set(level.Id);
                        column.get_Parameter(BuiltInParameter.FAMILY_TOP_LEVEL_PARAM).Set(level.Id);
                        column.get_Parameter(BuiltInParameter.FAMILY_BASE_LEVEL_OFFSET_PARAM).Set(offset - length);
                        column.get_Parameter(BuiltInParameter.FAMILY_TOP_LEVEL_OFFSET_PARAM).Set(offset);
                        doc.Regenerate();
                        double low, high, familyAngle;
                        SteelTopVertices(column, out low, out high, out familyAngle);
                        ElementTransformUtils.RotateElement(doc, column.Id,
                            Line.CreateBound(new XYZ(outline.Center.X, outline.Center.Y, bottomZ), new XYZ(outline.Center.X, outline.Center.Y, bottomZ + 1)), outline.Angle - familyAngle);
                        doc.Regenerate();
                        var vertices = SteelTopVertices(column, out low, out high, out familyAngle);
                        var u = new XYZ(Math.Cos(outline.Angle), Math.Sin(outline.Angle), 0);
                        var v = new XYZ(-u.Y, u.X, 0);
                        double xmin = vertices.Min(q => q.DotProduct(u)), xmax = vertices.Max(q => q.DotProduct(u));
                        double ymin = vertices.Min(q => q.DotProduct(v)), ymax = vertices.Max(q => q.DotProduct(v));
                        var center = u * ((xmin + xmax) / 2) + v * ((ymin + ymax) / 2);
                        if (Math.Abs(high - topZ) > 1 * MmFt || Math.Abs(low - bottomZ) > 1 * MmFt ||
                            center.DistanceTo(outline.Center) > 1 * MmFt || SteelAngleDifference(outline.Angle, familyAngle) > 0.001 ||
                            Math.Abs(xmax - xmin - outline.Width) > 2 * MmFt || Math.Abs(ymax - ymin - outline.Depth) > 2 * MmFt)
                            throw new InvalidOperationException("Column solid verification failed: elevation, center, flange direction or section size differs from CAD.");
                        rows.Add(new { status = write ? "Created" : "Planned", id = write ? (long?)column.Id.Value : null,
                            xMm = outline.Center.X * FtMm, yMm = outline.Center.Y * FtMm,
                            flangeDegrees = outline.Angle * 180 / Math.PI, topMm = high * FtMm, bottomMm = low * FtMm });
                    }
                    if (transaction.Commit() != TransactionStatus.Committed)
                        throw new InvalidOperationException("Batch rolled back by Revit failure handling.");
                    if (write) group.Assimilate(); else group.RollBack();
                }
                catch { if (transaction.GetStatus() == TransactionStatus.Started) transaction.RollBack(); throw; }
            }
            return new { Scanned = outlines.Count, Planned = outlines.Count - skipped, Created = write ? outlines.Count - skipped : 0,
                Skipped = skipped, Applied = write, TypeName = symbol.Name, TopLevel = level.Name, Results = rows };
        }
    }
}
