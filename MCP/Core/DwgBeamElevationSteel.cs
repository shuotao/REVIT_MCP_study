using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace RevitMCP.Core
{
    public static partial class DwgBeamExecutor
    {
        private sealed class ElevationSteelPlan
        {
            public XYZ A, B;
            public JObject Row, Rule;
            public FamilySymbol Symbol, Template;
            public Level Level;
            public FamilyInstance Instance;
            public int Label;
        }

        // A section CAD is a vertical drawing: model XY comes from the named grid,
        // and model Z comes from the transformed CAD, never the sheet's raw Y.
        private static object CreateElevationSteel(Document doc, JObject input)
        {
            var view = doc.ActiveView as ViewSection ?? throw new ArgumentException("ElevationSteel requires a section/elevation view.");
            string Required(string key) => !string.IsNullOrWhiteSpace((string)input[key]) ? (string)input[key] : throw new ArgumentException(key + " required.");
            string layer = Required("layerName"), textLayer = Required("textLayerNameX"), gridName = Required("centerGridName");
            bool apply = input.Value<bool?>("apply") == true && input.Value<bool?>("dryRun") == false;
            var grids = new FilteredElementCollector(doc).OfClass(typeof(Grid)).Cast<Grid>().Where(g => g.Name == gridName).ToList();
            if (grids.Count != 1 || !(grids[0].Curve is Line)) throw new ArgumentException("Requires one straight center grid.");
            var grid = grids[0]; var origin = grid.Curve.GetEndPoint(0);
            var delta = grid.Curve.GetEndPoint(1) - origin;
            var axis = new XYZ(delta.X, delta.Y, 0).Normalize();
            if (Math.Abs(view.ViewDirection.Z) > 1e-6 || Math.Abs(axis.DotProduct(view.RightDirection)) < 0.999999)
                throw new ArgumentException("Section must be vertical and parallel to the center grid.");
            XYZ Project(XYZ q) { double u = (q - origin).DotProduct(axis); return new XYZ(origin.X + u * axis.X, origin.Y + u * axis.Y, q.Z); }
            var cads = new FilteredElementCollector(doc, view.Id).OfClass(typeof(ImportInstance)).Cast<ImportInstance>()
                .Where(c => input["importInstanceUniqueId"] == null || c.UniqueId == (string)input["importInstanceUniqueId"]).ToList();
            if (cads.Count != 1 || !cads[0].IsLinked) throw new ArgumentException("Specify exactly one visible linked CAD with importInstanceUniqueId.");
            var cad = cads[0]; var segments = new List<Line>();
            using (var options = new Options { View = view })
            {
                void Walk(GeometryElement ge, Transform tr)
                {
                    if (ge == null) return;
                    foreach (var obj in ge)
                    {
                        if (obj is GeometryInstance instance) { Walk(instance.GetSymbolGeometry(), tr.Multiply(instance.Transform)); continue; }
                        var style = doc.GetElement(obj.GraphicsStyleId) as GraphicsStyle;
                        if (style?.GraphicsStyleCategory?.Name != layer) continue;
                        if (obj is Line line && line.IsBound)
                            segments.Add(Line.CreateBound(tr.OfPoint(line.GetEndPoint(0)), tr.OfPoint(line.GetEndPoint(1))));
                        else if (obj is PolyLine poly)
                        {
                            var pts = poly.GetCoordinates();
                            for (int i = 1; i < pts.Count; i++)
                                if (pts[i].DistanceTo(pts[i - 1]) > doc.Application.ShortCurveTolerance)
                                    segments.Add(Line.CreateBound(tr.OfPoint(pts[i - 1]), tr.OfPoint(pts[i])));
                        }
                    }
                }
                Walk(cad.get_Geometry(options), Transform.Identity);
            }
            var factors = new Dictionary<string, double> { ["mm"] = 1, ["cm"] = 10, ["m"] = 1000 };
            string units = (string)input["cadUnits"];
            if (units != null && !factors.ContainsKey(units)) throw new ArgumentException("Invalid cadUnits.");
            var labels = ReadScheduleLabels(doc, cad, textLayer, units == null ? 0 : factors[units], (string)input["pythonExecutable"] ?? "python", view, segments, layer);
            // Only exact steel marks; sizes or unrelated text cannot become beam marks.
            var marks = labels.Select((l, i) => new { Label = l, Index = i, Number = l.Text.Trim(), Point = Project(new XYZ(l.X, l.Y, l.Z)) })
                .Where(l => Regex.IsMatch(l.Number, @"^(?:CSB|SB|SD)\d+$", RegexOptions.CultureInvariant)).ToList();
            var rules = (input["steelRules"] as JArray ?? new JArray()).Cast<JObject>().ToList();
            foreach (var rule in rules)
            {
                string kind = (string)rule["sectionKind"];
                var dims = rule["dimensionsMm"] as JObject;
                if (kind != "BH" && kind != "RH" && kind != "BOX") throw new ArgumentException("sectionKind must be BH/RH/BOX.");
                if (dims == null || dims.Count < (kind == "BOX" ? 3 : 4)) throw new ArgumentException("Complete section dimensions required.");
                if (kind == "BOX" && (dims["牆標稱厚度"] == null || dims["牆設計厚度"] != null))
                    throw new ArgumentException("BOX thickness must use 牆標稱厚度, not 牆設計厚度.");
                if (!(rule["numbers"] is JArray numbers) || numbers.Count == 0 || numbers.Any(n => !Regex.IsMatch((string)n ?? "", @"^(?:CSB|SB|SD)\d+$")))
                    throw new ArgumentException("Exact steel numbers required.");
            }
            if (rules.SelectMany(r => ((JArray)r["numbers"]).Values<string>()).GroupBy(n => n).Any(g => g.Count() > 1))
                throw new ArgumentException("A steel number occurs in multiple schedule rules.");
            if (rules.GroupBy(r => r.Value<long>("baseTypeId") + "|" + (string)r["typeName"]).Any(g =>
                g.Select(r => ((JObject)r["dimensionsMm"]).ToString(Formatting.None)).Distinct().Count() > 1))
                throw new ArgumentException("Conflicting dimensions for the same requested type.");
            var levels = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().OrderBy(l => l.ProjectElevation).ToList();
            if (levels.Count == 0) throw new ArgumentException("No levels.");
            var existing = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_StructuralFraming)
                .WhereElementIsNotElementType().OfType<FamilyInstance>().ToList();
            var rows = new List<JObject>(); var plans = new List<ElevationSteelPlan>(); var seen = new HashSet<string>();
            string Vec(XYZ q) => string.Join(",", new[] { q.X, q.Y, q.Z }.Select(v => v.ToString("R", CultureInfo.InvariantCulture)));
            double limit = input.Value<double?>("labelDistanceMm") ?? 1500;
            if (limit <= 0 || limit > 5000 || double.IsNaN(limit)) throw new ArgumentException("labelDistanceMm must be in (0,5000].");
            foreach (var line in segments)
            {
                var a = Project(line.GetEndPoint(0)); var b = Project(line.GetEndPoint(1));
                if ((b - a).DotProduct(axis) < 0) { var swap = a; a = b; b = swap; }
                string key = Vec(a) + "|" + Vec(b);
                if (!seen.Add(key)) continue;
                var row = new JObject { ["Status"] = "Skipped", ["StartMm"] = new JArray(a.X * FtMm, a.Y * FtMm, a.Z * FtMm),
                    ["EndMm"] = new JArray(b.X * FtMm, b.Y * FtMm, b.Z * FtMm) };
                rows.Add(row);
                if (Math.Abs(a.Z - b.Z) * FtMm > 0.1) { row["Reason"] = "NonHorizontalLine"; continue; }
                double length = a.DistanceTo(b);
                if (length * FtMm < 300) { row["Reason"] = "ShortLine"; continue; }
                int choice = ElevationSteelMatching.Select(0, length * FtMm, a.Z * FtMm,
                    marks.Select(l => ((l.Point - a).DotProduct(axis) * FtMm, l.Point.Z * FtMm)).ToList(), limit);
                if (choice < 0) { row["Reason"] = choice == -2 ? "AmbiguousMarks" : "NoNearbyMark"; continue; }
                var label = marks[choice]; row["Number"] = label.Number; row["LabelDistanceMm"] = Math.Abs(label.Point.Z - a.Z) * FtMm;
                var matches = rules.Where(r => ((JArray)r["numbers"]).Values<string>().Contains(label.Number)).ToList();
                if (matches.Count != 1) { row["Reason"] = matches.Count == 0 ? "MissingScheduleRule" : "AmbiguousScheduleRule"; continue; }
                var rule = matches[0];
                var template = doc.GetElement(new ElementId(rule.Value<long>("baseTypeId"))) as FamilySymbol;
                if (template?.Category?.Id.GetIdValue() != (long)BuiltInCategory.OST_StructuralFraming) throw new ArgumentException("baseTypeId must be structural framing.");
                var dimensions = rule["dimensionsMm"] as JObject ?? throw new ArgumentException("dimensionsMm required.");
                if (dimensions.Count < 3 || dimensions.Properties().Any(p => p.Value.Type != JTokenType.Float && p.Value.Type != JTokenType.Integer))
                    throw new ArgumentException("Provide all section dimensions (height/width and thicknesses).");
                foreach (var dimension in dimensions.Properties()) ElevationDimension(template, dimension.Name, (double)dimension.Value, false);
                string typeName = (string)rule["typeName"];
                if (string.IsNullOrWhiteSpace(typeName)) throw new ArgumentException("typeName required.");
                var symbols = template.Family.GetFamilySymbolIds().Select(id => (FamilySymbol)doc.GetElement(id)).Where(s => s.Name == typeName).ToList();
                if (symbols.Count > 1) throw new ArgumentException("Ambiguous type name.");
                var symbol = symbols.SingleOrDefault();
                if (symbol != null && dimensions.Properties().Any(d => Math.Abs(ElevationDimension(symbol, d.Name, (double)d.Value, false).AsDouble() * FtMm - (double)d.Value) > 0.1))
                { row["Reason"] = "ExistingTypeDimensionMismatch"; row["TypeId"] = symbol.Id.GetIdValue(); continue; }
                // Do not duplicate an already occupied axis, regardless of its existing type.
                if (existing.Any(e => ElevationSteelOverlap(e, a, b))) { row["Reason"] = "ExistingBeamOverlap"; continue; }
                var level = levels.LastOrDefault(l => l.ProjectElevation <= a.Z + 1e-8) ?? levels[0];
                row["Status"] = "Planned"; row["TypeName"] = typeName; row["FamilyName"] = template.FamilyName; row["CreateType"] = symbol == null;
                row["DimensionsMm"] = dimensions.DeepClone(); row["ReferenceLevel"] = level.Name; row["ReferenceLevelId"] = level.Id.GetIdValue();
                row["TopOffsetMm"] = (a.Z - level.ProjectElevation) * FtMm;
                plans.Add(new ElevationSteelPlan { A = a, B = b, Row = row, Rule = rule, Symbol = symbol, Template = template, Level = level, Label = label.Index });
            }
            foreach (var group in plans.GroupBy(p => p.Label).Where(g => g.Count() > 1))
                foreach (var plan in group) { plan.Row["Status"] = "Skipped"; plan.Row["Reason"] = "MarkAssignedToMultipleLines"; }
            plans = plans.Where(p => (string)p.Row["Status"] == "Planned").ToList();
            if (plans.GroupBy(p => p.Template.Family.Id + "|" + (string)p.Rule["typeName"]).Any(g =>
                g.Select(p => ((JObject)p.Rule["dimensionsMm"]).ToString(Formatting.None)).Distinct().Count() > 1))
                throw new ArgumentException("Conflicting dimensions within the same family/type.");
            var settings = (JObject)input.DeepClone(); settings.Remove("apply"); settings.Remove("dryRun"); settings.Remove("previewToken");
            string fingerprint = doc.ProjectInformation.UniqueId + view.UniqueId + cad.UniqueId + Vec(origin) + Vec(axis) +
                settings.ToString(Formatting.None) + JArray.FromObject(rows).ToString(Formatting.None);
            string token;
            using (var hash = SHA256.Create()) token = BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(fingerprint))).Replace("-", "");
            if (apply && (string)input["previewToken"] != token) throw new ArgumentException("Run preview again; missing/stale previewToken.");
            var createdTypes = new List<long>();
            if (apply && plans.Count > 0)
            {
                using (var group = new TransactionGroup(doc, "Abby elevation steel beams"))
                {
                    group.Start();
                    try
                    {
                        using (var tx = new Transaction(doc, "Create elevation steel types, beams and both marks"))
                        {
                            tx.Start(); tx.SetFailureHandlingOptions(tx.GetFailureHandlingOptions().SetFailuresPreprocessor(new ExactBeamFailures()).SetClearAfterRollback(true));
                            var cache = new Dictionary<string, FamilySymbol>();
                            foreach (var plan in plans)
                            {
                                string cacheKey = plan.Template.Family.Id + "|" + (string)plan.Rule["typeName"];
                                if (!cache.TryGetValue(cacheKey, out var symbol))
                                {
                                    symbol = plan.Symbol;
                                    if (symbol == null)
                                    {
                                        symbol = (FamilySymbol)plan.Template.Duplicate((string)plan.Rule["typeName"]);
                                        foreach (var d in ((JObject)plan.Rule["dimensionsMm"]).Properties()) ElevationDimension(symbol, d.Name, (double)d.Value, true);
                                        createdTypes.Add(symbol.Id.GetIdValue());
                                    }
                                    cache.Add(cacheKey, symbol);
                                }
                                plan.Symbol = symbol;
                                if (!symbol.IsActive) symbol.Activate(); doc.Regenerate();
                                var a = new XYZ(plan.A.X, plan.A.Y, plan.Level.ProjectElevation);
                                var b = new XYZ(plan.B.X, plan.B.Y, plan.Level.ProjectElevation);
                                var beam = doc.Create.NewFamilyInstance(Line.CreateBound(a, b), symbol, plan.Level, StructuralType.Beam);
                                plan.Instance = beam;
                                StructuralFramingUtils.DisallowJoinAtEnd(beam, 0); StructuralFramingUtils.DisallowJoinAtEnd(beam, 1);
                                ExactBeamSet(beam, BuiltInParameter.Y_JUSTIFICATION, 1);
                                ExactBeamSet(beam, BuiltInParameter.Y_OFFSET_VALUE, 0.0);
                                ExactBeamSet(beam, BuiltInParameter.Z_JUSTIFICATION, 0);
                                ExactBeamSet(beam, BuiltInParameter.Z_OFFSET_VALUE, plan.A.Z - plan.Level.ProjectElevation);
                                ExactBeamSet(beam, BuiltInParameter.STRUCTURAL_BEAM_END0_ELEVATION, 0.0);
                                ExactBeamSet(beam, BuiltInParameter.STRUCTURAL_BEAM_END1_ELEVATION, 0.0);
                                foreach (var groupId in new[] { GroupTypeId.Constraints, GroupTypeId.Text })
                                {
                                    var mark = ElevationMark(beam, groupId);
                                    if (!mark.Set((string)plan.Row["Number"])) throw new InvalidOperationException("Cannot write beam number.");
                                }
                            }
                            doc.Regenerate();
                            if (tx.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("Steel batch commit rejected.");
                        }
                        foreach (var plan in plans)
                        {
                            var beam = plan.Instance;
                            foreach (var d in ((JObject)plan.Rule["dimensionsMm"]).Properties())
                                if (Math.Abs(ElevationDimension(plan.Symbol, d.Name, (double)d.Value, false).AsDouble() * FtMm - (double)d.Value) > 0.1)
                                    throw new InvalidOperationException("Section dimensions changed after regeneration.");
                            foreach (var groupId in new[] { GroupTypeId.Constraints, GroupTypeId.Text })
                                if (ElevationMark(beam, groupId).AsString() != (string)plan.Row["Number"]) throw new InvalidOperationException("Mark verification failed.");
                            var curve = ((LocationCurve)beam.Location).Curve;
                            var a = curve.GetEndPoint(0); var b = curve.GetEndPoint(1);
                            bool xy = new XYZ(a.X, a.Y, plan.A.Z).DistanceTo(plan.A) < 0.1 / FtMm && new XYZ(b.X, b.Y, plan.B.Z).DistanceTo(plan.B) < 0.1 / FtMm;
                            if (!xy || beam.get_Parameter(BuiltInParameter.Z_JUSTIFICATION).AsInteger() != 0 ||
                                Math.Abs(beam.get_Parameter(BuiltInParameter.Z_OFFSET_VALUE).AsDouble() - (plan.A.Z - plan.Level.ProjectElevation)) * FtMm > 0.1)
                                throw new InvalidOperationException("Beam top or plan axis verification failed.");
                            var box = beam.get_BoundingBox(null);
                            if (box == null || Math.Abs(box.Max.Z - plan.A.Z) * FtMm > 1)
                                throw new InvalidOperationException("Physical beam top differs from CAD top; batch rolled back.");
                            plan.Row["ElementId"] = beam.Id.GetIdValue(); plan.Row["Status"] = "Created";
                        }
                        if (group.Assimilate() != TransactionStatus.Committed) throw new InvalidOperationException("Batch group rejected.");
                    }
                    catch { if (group.GetStatus() == TransactionStatus.Started) group.RollBack(); throw; }
                }
            }
            return new { Mode = "elevationSteel", Applied = apply, PreviewToken = token, ViewId = view.Id.GetIdValue(),
                CadId = cad.Id.GetIdValue(), ImportInstanceUniqueId = cad.UniqueId, SourceFile = GetCadFilePath(doc, cad), Grid = gridName,
                Scanned = rows.Count, Planned = plans.Count, Created = rows.Count(r => (string)r["Status"] == "Created"),
                Skipped = rows.Count(r => (string)r["Status"] == "Skipped"), CreatedTypeIds = createdTypes,
                Labels = marks.Select(m => new { m.Number, Xmm = m.Point.X * FtMm, Ymm = m.Point.Y * FtMm, Zmm = m.Point.Z * FtMm,
                    SourceModelMm = new[] { m.Label.X * FtMm, m.Label.Y * FtMm, m.Label.Z * FtMm } }), Results = rows,
                SectionTemplates = rules.Count == 0 ? new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol))
                    .OfCategory(BuiltInCategory.OST_StructuralFraming).Cast<FamilySymbol>()
                    .Where(s => s.FamilyName.Contains("型鋼")).Select(s => new { Id = s.Id.GetIdValue(), s.FamilyName, s.Name,
                        Lengths = s.Parameters.Cast<Parameter>().Where(ElevationLengthParameter)
                            .Select(p => new { Name = p.Definition.Name, Millimeters = p.AsDouble() * FtMm, p.IsReadOnly }).ToList() }).ToList() : null };
        }

        private static Parameter ElevationDimension(FamilySymbol symbol, string name, double mm, bool write)
        {
            if (double.IsNaN(mm) || double.IsInfinity(mm) || mm <= 0) throw new ArgumentException("Invalid section dimension.");
            var parameters = symbol.GetParameters(name).Where(ElevationLengthParameter).ToList();
            if (parameters.Count != 1) throw new ArgumentException(symbol.Name + ": missing/ambiguous length parameter " + name);
            var parameter = parameters[0];
            if (write && (parameter.IsReadOnly || !parameter.Set(mm / FtMm))) throw new InvalidOperationException("Cannot set " + name);
            return parameter;
        }

        private static Parameter ElevationMark(FamilyInstance beam, ForgeTypeId group)
        {
            var matches = beam.Parameters.Cast<Parameter>().Where(p => p.Definition.Name == "編號" && p.Definition.GetGroupTypeId() == group).ToList();
            if (matches.Count != 1 || matches[0].IsReadOnly || matches[0].StorageType != StorageType.String)
                throw new InvalidOperationException("Missing/wrong/read-only 編號 in group " + group.TypeId + "; entire batch rolled back.");
            return matches[0];
        }

        private static bool ElevationLengthParameter(Parameter parameter) => parameter.StorageType == StorageType.Double &&
            UnitUtils.IsMeasurableSpec(parameter.Definition.GetDataType()) &&
            UnitUtils.GetValidUnits(parameter.Definition.GetDataType()).Contains(UnitTypeId.Millimeters);

        private static Transform ResolveElevationTextTransform(Transform original, View view, List<Line> model, JArray controls, double factor)
        {
            if (controls == null || controls.Count < 3 || model == null || model.Count < 3)
                throw new InvalidOperationException("Elevation text needs at least three CAD control lines to verify placement.");
            var raw = controls.Select(c => c.Select(p => new XYZ((double)p[0] * factor, (double)p[1] * factor, (double)p[2] * factor)).ToArray()).ToList();
            const double tolerance = 1.0 / FtMm;
            // Match a multiset of complete segments, not a bounding box or one anchor.
            bool Matches(Transform transform)
            {
                if (raw.Count != model.Count) return false;
                var unused = model.ToList();
                foreach (var pair in raw)
                {
                    var a = transform.OfPoint(pair[0]); var b = transform.OfPoint(pair[1]);
                    int i = unused.FindIndex(l =>
                        (a.DistanceTo(l.GetEndPoint(0)) <= tolerance && b.DistanceTo(l.GetEndPoint(1)) <= tolerance) ||
                        (b.DistanceTo(l.GetEndPoint(0)) <= tolerance && a.DistanceTo(l.GetEndPoint(1)) <= tolerance));
                    if (i < 0) return false;
                    unused.RemoveAt(i);
                }
                return true;
            }
            if (Matches(original)) return original;
            var points = model.SelectMany(l => new[] { l.GetEndPoint(0), l.GetEndPoint(1) }).ToList();
            var rawPoints = raw.SelectMany(p => p).ToList(); var candidates = new List<Transform>();
            // View-specific 2D CAD may retain an XY GetTotalTransform despite its geometry
            // being displayed in the section plane. Accept a view-frame mapping ONLY if
            // every actual CAD segment independently agrees with Revit's cached geometry.
            foreach (int sx in new[] { 1, -1 }) foreach (int sy in new[] { 1, -1 })
            {
                var x = view.RightDirection.Normalize() * sx; var y = XYZ.BasisZ * sy; var n = x.CrossProduct(y);
                var tr = Transform.Identity; tr.BasisX = x; tr.BasisY = y; tr.BasisZ = n;
                tr.Origin = x * (points.Min(p => p.DotProduct(x)) - rawPoints.Min(p => p.X)) +
                    y * (points.Min(p => p.DotProduct(y)) - rawPoints.Min(p => p.Y)) +
                    n * (points.Min(p => p.DotProduct(n)) - rawPoints.Min(p => p.Z));
                if (Matches(tr)) candidates.Add(tr);
            }
            if (candidates.Count != 1) throw new InvalidOperationException("CAD/Revit control geometry alignment is missing or ambiguous; no text placement guessed.");
            return candidates[0];
        }

        private static bool ElevationSteelOverlap(FamilyInstance beam, XYZ a, XYZ b)
        {
            if (!(beam.Location is LocationCurve lc) || !(lc.Curve is Line)) return false;
            var box = beam.get_BoundingBox(null);
            if (box == null || Math.Abs(box.Max.Z - a.Z) * FtMm > 2) return false;
            var p = lc.Curve.GetEndPoint(0); var q = lc.Curve.GetEndPoint(1);
            return ScheduleSegmentOverlap(a, b, new XYZ(p.X, p.Y, a.Z), new XYZ(q.X, q.Y, a.Z));
        }
    }
}
