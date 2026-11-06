using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;

namespace RevitMCP.Core
{
    public partial class CommandExecutor
    {
        // Declarative, family-document-only operations. Never executes supplied code or edits a project.
        private object ExportConstrainedFamily(JObject parameters, JObject spec)
        {
            var active = _uiApp.ActiveUIDocument.Document;
            string mode = spec.Value<string>("mode") ?? "inspect";
            if (mode != "inspect" && mode != "repair") throw new ArgumentException("Unknown familyConstraints mode.");
            string source = spec.Value<string>("sourcePath");
            if (mode == "inspect" && string.IsNullOrWhiteSpace(source))
            {
                if (!active.IsFamilyDocument) throw new InvalidOperationException("Open a family document or provide sourcePath.");
                return new { Success = true, ReadOnly = true, Family = VisitConstraintFamily(active, Strings(spec["familyPath"]), 0,
                    d => InspectConstraintFamily(d), false) };
            }
            if (string.IsNullOrWhiteSpace(source) || !Path.IsPathRooted(source) || !File.Exists(source) ||
                !string.Equals(Path.GetExtension(source), ".rfa", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("An existing absolute sourcePath .rfa is required. The source is never overwritten.");
            string outputFolder = parameters.Value<string>("outputFolder");
            string fileName = spec.Value<string>("fileName");
            bool apply = mode == "repair" && parameters.Value<bool?>("apply") == true && parameters.Value<bool?>("dryRun") == false;
            string output = null;
            if (mode == "repair")
            {
                if (string.IsNullOrWhiteSpace(outputFolder) || !Path.IsPathRooted(outputFolder) ||
                    string.IsNullOrWhiteSpace(fileName) || Path.GetFileName(fileName) != fileName ||
                    fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                    !string.Equals(Path.GetExtension(fileName), ".rfa", StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("Provide absolute outputFolder and a plain .rfa fileName.");
                output = Path.Combine(Path.GetFullPath(outputFolder), fileName);
                if (File.Exists(output)) throw new IOException("Output already exists. Choose a new fileName; overwrite is prohibited.");
                if (!(spec["steps"] is JArray steps) || steps.Count == 0) throw new ArgumentException("repair requires steps.");
                RequireWidthFlexCases(spec["flexCases"] as JArray);
            }
            // Open a physical copy, so an already-open source or nested family cannot be changed by this tool.
            string staging = Path.Combine(Path.GetTempPath(), "AbbyFamilyConstraints-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(staging);
            string candidate = Path.Combine(staging, mode == "repair" ? fileName : "inspection.rfa");
            File.Copy(source, candidate, false);
            Document working = null;
            var reports = new List<object>();
            try
            {
                working = _uiApp.Application.OpenDocumentFile(candidate);
                if (!working.IsFamilyDocument) throw new InvalidOperationException("Source must be an RFA family document.");
                if (mode == "inspect")
                {
                    object inspection = VisitConstraintFamily(working, Strings(spec["familyPath"]), 0,
                        d => InspectConstraintFamily(d), false);
                    return new { Success = true, ReadOnly = true, SourcePath = source, Family = inspection };
                }
                foreach (JObject step in (JArray)spec["steps"])
                {
                    var path = Strings(step["familyPath"]);
                    reports.Add(VisitConstraintFamily(working, path, 0, d => ApplyConstraintStep(d, step), true));
                }
                var flexReports = RunConstraintFlexCases(working, (JArray)spec["flexCases"]);
                object finalState = InspectConstraintFamily(working, false);
                if (apply)
                {
                    working.SaveAs(candidate, new SaveAsOptions { OverwriteExistingFile = true, MaximumBackups = 1 });
                    working.Close(false); working = null;
                    working = _uiApp.Application.OpenDocumentFile(candidate);
                    var reopenReports = RunConstraintFlexCases(working, (JArray)spec["flexCases"]);
                    working.Close(false); working = null;
                    Directory.CreateDirectory(outputFolder);
                    // Copy(false) provides a final no-overwrite check, including a concurrently created destination.
                    File.Copy(candidate, output, false);
                    return new { Success = true, DryRun = false, SourcePath = source, OutputPath = output,
                        SourceUnchanged = true, HostProjectModified = false, Steps = reports,
                        FlexTests = flexReports, ReopenedFlexTests = reopenReports, Final = finalState };
                }
                return new { Success = true, DryRun = true, SourcePath = source, OutputPath = output,
                    SourceUnchanged = true, HostProjectModified = false, Steps = reports, FlexTests = flexReports, Final = finalState };
            }
            catch (Exception ex)
            {
                return new { Success = false, DryRun = !apply, SourceUnchanged = true, HostProjectModified = false,
                    OutputWritten = false, Error = ex.Message, Steps = reports };
            }
            finally
            {
                if (working != null && working.IsValidObject) working.Close(false);
                // Only our newly created staging directory. Never enumerate/delete source or output directories.
                foreach (string file in Directory.EnumerateFiles(staging)) File.Delete(file);
                if (!Directory.EnumerateFileSystemEntries(staging).Any()) Directory.Delete(staging);
            }
        }

        private static string[] Strings(JToken token) => (token as JArray)?.Values<string>().ToArray() ?? new string[0];
        private static double Mm(double feet) => Math.Round(feet * 304.8, 6);
        private static double[] PointMm(XYZ p) => new[] { Mm(p.X), Mm(p.Y), Mm(p.Z) };
        private static XYZ ConstraintPoint(JToken p)
        {
            var a = (p as JArray)?.Values<double>().ToArray();
            if (a == null || a.Length != 3 || a.Any(x => double.IsNaN(x) || double.IsInfinity(x)))
                throw new ArgumentException("Coordinates must contain three finite mm values.");
            return new XYZ(a[0], a[1], a[2]) / 304.8;
        }
        private static Element ConstraintElement(Document d, JToken id)
        {
            long value = id?.Value<long>() ?? -1;
            if (value <= 0) throw new ArgumentException("Positive element ID required.");
#if REVIT2025_OR_GREATER
            var element = d.GetElement(new ElementId(value));
#else
            var element = d.GetElement(new ElementId(checked((int)value)));
#endif
            return element ?? throw new ArgumentException("Element not found: " + value);
        }
        private static string Stable(Document d, Reference r)
        {
            if (r == null) return null;
            try { return r.ConvertToStableRepresentation(d); } catch { return null; }
        }
        private static Reference ConstraintReference(Document d, JToken token)
        {
            if (token == null) throw new ArgumentException("Reference is missing.");
            if (token.Type == JTokenType.String) return Reference.ParseFromStableRepresentation(d, token.Value<string>());
            var e = ConstraintElement(d, token["elementId"]);
            string kind = token.Value<string>("kind");
            if (e is ReferencePlane plane) return plane.GetReference();
            if (e is CurveElement curve)
                return kind == "end" ? curve.GeometryCurve.GetEndPointReference(token.Value<int?>("end") ?? 0) : curve.GeometryCurve.Reference;
            if (e is FamilyInstance instance)
            {
                if (!Enum.TryParse(kind, out FamilyInstanceReferenceType referenceType)) throw new ArgumentException("Invalid instance reference kind.");
                var refs = instance.GetReferences(referenceType);
                if (refs.Count != 1) throw new ArgumentException("Instance reference is missing or ambiguous.");
                return refs[0];
            }
            throw new ArgumentException("Unsupported reference element.");
        }
        private sealed class ConstraintFamilyLoad : IFamilyLoadOptions
        {
            public bool OnFamilyFound(bool inUse, out bool overwrite) { overwrite = true; return true; }
            public bool OnSharedFamilyFound(Family family, bool inUse, out FamilySource source, out bool overwrite)
            { source = FamilySource.Family; overwrite = true; return true; }
        }
        private sealed class ConstraintFailures : IFailuresPreprocessor
        {
            private readonly HashSet<string> _existing;
            public ConstraintFailures(Document d) { _existing = new HashSet<string>(d.GetWarnings().Select(w => w.GetDescriptionText())); }
            public FailureProcessingResult PreprocessFailures(FailuresAccessor accessor)
            {
                foreach (var failure in accessor.GetFailureMessages())
                {
                    // Do not silently accept newly broken constraints or remove constraints to obtain a passing flex.
                    if (failure.GetSeverity() != FailureSeverity.Warning || !_existing.Contains(failure.GetDescriptionText()))
                        return FailureProcessingResult.ProceedWithRollBack;
                    accessor.DeleteWarning(failure);
                }
                return FailureProcessingResult.Continue;
            }
        }
        private static object VisitConstraintFamily(Document d, string[] path, int index, Func<Document, object> action, bool load)
        {
            if (!d.IsFamilyDocument) throw new InvalidOperationException("Family document required.");
            if (index == path.Length) return action(d);
            var families = new FilteredElementCollector(d).OfClass(typeof(Family)).Cast<Family>().Where(f => f.Name == path[index]).ToList();
            if (families.Count != 1) throw new ArgumentException("Nested family missing or ambiguous: " + path[index]);
            var child = d.EditFamily(families[0]);
            try
            {
                object result = VisitConstraintFamily(child, path, index + 1, action, load);
                if (load && child.LoadFamily(d, new ConstraintFamilyLoad()) == null)
                    throw new InvalidOperationException("Nested family reload failed: " + path[index]);
                return result;
            }
            finally { child.Close(false); }
        }
        private static FamilyParameter ConstraintParameter(Document d, JObject op)
        {
            string name = op.Value<string>("parameter");
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("parameter name required.");
            var fm = d.FamilyManager;
            var p = fm.get_Parameter(name);
            if (p == null)
            {
                if (op.Value<bool?>("create") != true) throw new ArgumentException("Family parameter missing: " + name);
                p = fm.AddParameter(name, GroupTypeId.Geometry,
                    op.Value<string>("dataType") == "yesNo" ? SpecTypeId.Boolean.YesNo : SpecTypeId.Length,
                    op.Value<bool?>("instance") ?? false);
            }
            return p;
        }
        private static void SetConstraintParameter(Document d, JObject op)
        {
            var fm = d.FamilyManager;
            var p = ConstraintParameter(d, op);
            if (op.ContainsKey("formula")) fm.SetFormula(p, op.Value<string>("formula"));
            if (op["valueMm"] != null) fm.Set(p, op.Value<double>("valueMm") / 304.8);
            if (op["valueBool"] != null) fm.Set(p, op.Value<bool>("valueBool") ? 1 : 0);
        }
        private static object ApplyConstraintStep(Document d, JObject step)
        {
            if (!(step["actions"] is JArray actions) || actions.Count == 0) throw new ArgumentException("Step actions required.");
            int count = 0;
            var created = new List<object>();
            using (var tx = new Transaction(d, "Repair family width constraints"))
            {
                tx.Start();
                tx.SetFailureHandlingOptions(tx.GetFailureHandlingOptions().SetFailuresPreprocessor(new ConstraintFailures(d)).SetClearAfterRollback(true));
                string typeName = step.Value<string>("typeName");
                if (typeName != null)
                {
                    var types = d.FamilyManager.Types.Cast<FamilyType>().Where(t => t.Name == typeName).ToList();
                    if (types.Count != 1) throw new ArgumentException("Family type missing: " + typeName);
                    d.FamilyManager.CurrentType = types[0];
                }
                foreach (JObject op in actions)
                {
                    string kind = op.Value<string>("kind");
                    switch (kind)
                    {
                        case "parameter": SetConstraintParameter(d, op); break;
                        case "compactSiteGateParameters": created.Add(CompactSiteGateParameters(d)); break;
                        case "associate":
                            var element = ConstraintElement(d, op["elementId"]);
                            var matches = element.Parameters.Cast<Parameter>().Where(p => p.Definition.Name == op.Value<string>("elementParameter")).ToList();
                            if (matches.Count != 1) throw new ArgumentException("Element parameter missing or ambiguous.");
                            d.FamilyManager.AssociateElementParameterToFamilyParameter(matches[0], ConstraintParameter(d, op));
                            break;
                        case "dimension":
                            var view = ConstraintElement(d, op["viewId"]) as View ?? throw new ArgumentException("View required.");
                            var refs = new ReferenceArray();
                            foreach (var r in (JArray)op["references"]) refs.Append(ConstraintReference(d, r));
                            if (refs.Size != 2) throw new ArgumentException("Exactly two references required.");
                            var dimension = d.FamilyCreate.NewLinearDimension(view,
                                Line.CreateBound(ConstraintPoint(op["startMm"]), ConstraintPoint(op["endMm"])), refs);
                            if (op["parameter"] != null)
                            {
                                dimension.FamilyLabel = ConstraintParameter(d, op);
                                SetConstraintParameter(d, op);
                            }
                            else dimension.IsLocked = true;
                            created.Add(new { Kind = kind, Id = dimension.Id.GetIdValue() });
                            break;
                        case "align":
                            var alignView = ConstraintElement(d, op["viewId"]) as View ?? throw new ArgumentException("View required.");
                            var alignment = d.FamilyCreate.NewAlignment(alignView, ConstraintReference(d, op["first"]), ConstraintReference(d, op["second"]));
                            created.Add(new { Kind = kind, Id = alignment.Id.GetIdValue() });
                            break;
                        case "plane":
                            var planeView = ConstraintElement(d, op["viewId"]) as View ?? throw new ArgumentException("View required.");
                            var plane = d.FamilyCreate.NewReferencePlane(ConstraintPoint(op["startMm"]), ConstraintPoint(op["endMm"]), ConstraintPoint(op["normalMm"]), planeView);
                            plane.Name = op.Value<string>("name") ?? "WidthRepair";
                            created.Add(new { Kind = kind, Id = plane.Id.GetIdValue(), Reference = Stable(d, plane.GetReference()) });
                            break;
                        case "move":
                            var move = ConstraintElement(d, op["elementId"]);
                            if (!(move is FamilyInstance) && !(move is GenericForm) && !(move is ReferencePlane) && !(move is CurveElement))
                                throw new ArgumentException("Only family geometry/reference elements may move.");
                            ElementTransformUtils.MoveElement(d, move.Id, ConstraintPoint(op["offsetMm"]));
                            break;
                        case "curve":
                            var ce = ConstraintElement(d, op["elementId"]) as ModelCurve ?? throw new ArgumentException("ModelCurve required.");
                            if (!(ce.GeometryCurve is Line)) throw new ArgumentException("Only straight sketch/reference lines supported.");
                            ce.GeometryCurve = Line.CreateBound(ConstraintPoint(op["startMm"]), ConstraintPoint(op["endMm"]));
                            break;
                        case "removeDimension":
                            var remove = ConstraintElement(d, op["elementId"]) as Dimension ?? throw new ArgumentException("Only an explicit dimension may be removed.");
                            d.Delete(remove.Id);
                            break;
                        default: throw new ArgumentException("Unsupported declarative action: " + kind);
                    }
                    d.Regenerate();
                    count++;
                }
                if (tx.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("Constraint repair rolled back.");
            }
            var tests = step["flexCases"] is JArray cases ? RunConstraintFlexCases(d, cases) : new List<object>();
            return new { Family = d.Title, Actions = count, Created = created, FlexTests = tests };
        }
        private static void RequireWidthFlexCases(JArray cases)
        {
            if (cases == null || cases.Count < 2 || cases.Select(c => c.Value<double?>("expectedWidthMm")).Where(v => v.HasValue).Distinct().Count() < 2)
                throw new ArgumentException("At least two distinct expectedWidthMm flex cases are required before exporting.");
        }
        private static List<object> RunConstraintFlexCases(Document d, JArray cases)
        {
            var results = new List<object>();
            foreach (JObject test in cases)
            {
                using (var group = new TransactionGroup(d, "Temporary flex verification"))
                {
                group.Start();
                using (var tx = new Transaction(d, "Verify family geometry flex"))
                {
                    tx.Start();
                    tx.SetFailureHandlingOptions(tx.GetFailureHandlingOptions().SetFailuresPreprocessor(new ConstraintFailures(d)).SetClearAfterRollback(true));
                    foreach (JObject setting in (JArray)test["parameters"]) SetConstraintParameter(d, setting);
                    d.Regenerate();
                    if (tx.Commit() != TransactionStatus.Committed)
                        throw new InvalidOperationException("Flex failed Revit constraint validation and rolled back.");
                    var bounds = ConstraintSolidBounds(d);
                    if (bounds.Count == 0) throw new InvalidOperationException("Flex produced no solid geometry.");
                    double minX = bounds.Min(b => b.Min[0]), maxX = bounds.Max(b => b.Max[0]);
                    double minZ = bounds.Min(b => b.Min[2]), maxZ = bounds.Max(b => b.Max[2]);
                    double tolerance = test.Value<double?>("toleranceMm") ?? 0.5;
                    if (tolerance <= 0 || tolerance > 2) throw new ArgumentException("Flex tolerance must be >0 and <=2mm.");
                    double? width = test.Value<double?>("expectedWidthMm"), height = test.Value<double?>("expectedHeightMm");
                    if (width.HasValue && Math.Abs(maxX - minX - width.Value) > tolerance)
                        throw new InvalidOperationException("Actual solid width " + (maxX - minX) + "mm differs from expected " + width + "mm.");
                    if (height.HasValue && Math.Abs(maxZ - minZ - height.Value) > tolerance)
                        throw new InvalidOperationException("Actual solid height differs from expected height.");
                    foreach (JObject check in (test["elementChecks"] as JArray ?? new JArray()))
                    {
                        long id = check.Value<long>("elementId");
                        var bound = bounds.SingleOrDefault(b => b.Id == id) ?? throw new InvalidOperationException("Solid check element missing: " + id);
                        foreach (string side in new[] { "minMm", "maxMm" })
                        {
                            if (!(check[side] is JArray expected) || expected.Count != 3) continue;
                            var actual = side == "minMm" ? bound.Min : bound.Max;
                            for (int i = 0; i < 3; i++)
                                if (expected[i].Type != JTokenType.Null && Math.Abs(actual[i] - expected[i].Value<double>()) > tolerance)
                                    throw new InvalidOperationException("Solid component bounds failed: " + id + "/" + side + "/" + i);
                        }
                    }
                    var warnings = d.GetWarnings().Select(w => w.GetDescriptionText()).ToArray();
                    results.Add(new { Name = test.Value<string>("name"), WidthMm = maxX - minX, HeightMm = maxZ - minZ,
                        SolidElements = bounds.Count, Bounds = bounds, Warnings = warnings });
                }
                group.RollBack();
                }
            }
            return results;
        }
        private sealed class ConstraintBounds
        {
            public long Id { get; set; }
            public string Name { get; set; }
            public double[] Min { get; set; }
            public double[] Max { get; set; }
        }
        private static List<ConstraintBounds> ConstraintSolidBounds(Document d)
        {
            var result = new List<ConstraintBounds>();
            foreach (var e in new FilteredElementCollector(d).WhereElementIsNotElementType().Where(e => e is GenericForm || e is FamilyInstance))
            {
                var points = new List<XYZ>();
                GatherConstraintVertices(e.get_Geometry(new Options { DetailLevel = ViewDetailLevel.Fine, IncludeNonVisibleObjects = false }), points);
                if (points.Count == 0) continue;
                result.Add(new ConstraintBounds { Id = e.Id.GetIdValue(), Name = e.Name,
                    Min = new[] { Mm(points.Min(p => p.X)), Mm(points.Min(p => p.Y)), Mm(points.Min(p => p.Z)) },
                    Max = new[] { Mm(points.Max(p => p.X)), Mm(points.Max(p => p.Y)), Mm(points.Max(p => p.Z)) } });
            }
            return result;
        }
        private static void GatherConstraintVertices(GeometryElement geometry, List<XYZ> points)
        {
            if (geometry == null) return;
            foreach (var g in geometry)
            {
                if (g is GeometryInstance instance) GatherConstraintVertices(instance.GetInstanceGeometry(), points);
                else if (g is Solid solid && solid.Volume > 1e-12)
                    foreach (Face f in solid.Faces) points.AddRange(f.Triangulate().Vertices);
            }
        }
        private static object InspectConstraintFamily(Document d, bool details = true)
        {
            var fm = d.FamilyManager;
            var parameters = fm.Parameters.Cast<FamilyParameter>().Select(p => new {
                Name = p.Definition.Name, p.IsInstance, p.Formula, StorageType = p.StorageType.ToString(),
                Value = ConstraintValue(fm.CurrentType, p) }).ToArray();
            var elements = new List<object>();
            if (details)
            {
                foreach (var e in new FilteredElementCollector(d).WhereElementIsNotElementType())
                {
                    long id = e.Id.GetIdValue();
                    if (e is ReferencePlane rp) elements.Add(new { Id = id, Class = "ReferencePlane", rp.Name,
                        BubbleMm = PointMm(rp.BubbleEnd), FreeMm = PointMm(rp.FreeEnd), Normal = PointMm(rp.Normal), Reference = Stable(d, rp.GetReference()) });
                    else if (e is CurveElement ce && ce.GeometryCurve.IsBound) elements.Add(new { Id = id, Class = e.GetType().Name, OwnerViewId = e.OwnerViewId.GetIdValue(),
                        StartMm = PointMm(ce.GeometryCurve.GetEndPoint(0)), EndMm = PointMm(ce.GeometryCurve.GetEndPoint(1)),
                        Reference = Stable(d, ce.GeometryCurve.Reference), StartReference = Stable(d, ce.GeometryCurve.GetEndPointReference(0)), EndReference = Stable(d, ce.GeometryCurve.GetEndPointReference(1)) });
                    else if (e is Dimension dim)
                    {
                        string label = null; try { label = dim.FamilyLabel?.Definition.Name; } catch { }
                        elements.Add(new { Id = id, Class = "Dimension", OwnerViewId = dim.OwnerViewId.GetIdValue(), Label = label,
                            References = dim.References.Cast<Reference>().Select(r => Stable(d, r)).ToArray() });
                    }
                    else if (e is FamilyInstance fi) elements.Add(new { Id = id, Class = "FamilyInstance", Family = fi.Symbol.Family.Name,
                        Type = fi.Symbol.Name, TypeId = fi.Symbol.Id.GetIdValue(), OriginMm = PointMm(fi.GetTransform().Origin),
                        InstanceParameters = ConstraintElementParameters(d, fi), TypeParameters = ConstraintElementParameters(d, fi.Symbol),
                        References = Enum.GetValues(typeof(FamilyInstanceReferenceType)).Cast<FamilyInstanceReferenceType>().Where(k => k != FamilyInstanceReferenceType.NotAReference)
                            .SelectMany(k => fi.GetReferences(k).Select(r => new { Kind = k.ToString(), Stable = Stable(d, r) })).ToArray() });
                    else if (e is GenericForm form) elements.Add(new { Id = id, Class = e.GetType().Name, form.IsSolid,
                        SketchId = form is Extrusion extrusion ? extrusion.Sketch.Id.GetIdValue() : form is Sweep sweep ? sweep.PathSketch?.Id.GetIdValue() : null });
                    else if (e is Sketch sketch) elements.Add(new { Id = id, Class = "Sketch", OwnerId = sketch.OwnerId.GetIdValue(),
                        Curves = sketch.GetAllElements().Select(x => x.GetIdValue()).ToArray() });
                }
            }
            return new { DocumentTitle = d.Title, SourcePath = d.PathName, CurrentType = fm.CurrentType?.Name,
                Types = fm.Types.Cast<FamilyType>().Select(t => t.Name).ToArray(), Parameters = parameters,
                Views = new FilteredElementCollector(d).OfClass(typeof(View)).Cast<View>().Where(v => !v.IsTemplate)
                    .Select(v => new { Id = v.Id.GetIdValue(), v.Name, Type = v.ViewType.ToString() }).ToArray(),
                SolidBounds = ConstraintSolidBounds(d), Elements = elements,
                Warnings = d.GetWarnings().Select(w => w.GetDescriptionText()).ToArray() };
        }
        private static object ConstraintValue(FamilyType type, FamilyParameter p)
        {
            if (type == null || !type.HasValue(p)) return null;
            switch (p.StorageType)
            {
                case StorageType.Double: return type.AsDouble(p);
                case StorageType.Integer: return type.AsInteger(p);
                case StorageType.String: return type.AsString(p);
                case StorageType.ElementId: return type.AsElementId(p)?.GetIdValue();
                default: return null;
            }
        }
        private static object[] ConstraintElementParameters(Document d, Element e)
        {
            return e.Parameters.Cast<Parameter>().Select(p => new { Name = p.Definition.Name,
                StorageType = p.StorageType.ToString(), Value = p.AsValueString() ?? (p.StorageType == StorageType.String ? p.AsString() : null),
                AssociatedTo = ConstraintAssociation(d, p) }).Cast<object>().ToArray();
        }
        private static string ConstraintAssociation(Document d, Parameter p)
        {
            try { return d.FamilyManager.GetAssociatedFamilyParameter(p)?.Definition.Name; }
            catch (Autodesk.Revit.Exceptions.ArgumentException) { return null; }
        }
    }
}
