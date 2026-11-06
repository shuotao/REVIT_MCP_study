using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;

namespace RevitMCP.Core
{
    public static partial class DwgColumnExecutor
    {
        public static object FillColumnMarks(Document doc, JObject args) => FillMarks(doc, args, false);
        public static object FillBeamMarks(Document doc, JObject args) => FillMarks(doc, args, true);
        private static object FillMarks(Document doc, JObject args, bool beams)
        {
            var view = doc.ActiveView as ViewPlan ?? throw new ArgumentException("A plan view is required.");
            string Required(string key) {
                if (args[key]?.Type != JTokenType.String || string.IsNullOrWhiteSpace((string)args[key]))
                    throw new ArgumentException(key + " is required.");
                return (string)args[key];
            }
            bool Flag(string key, bool fallback) {
                if (args[key] == null) return fallback;
                if (args[key].Type != JTokenType.Boolean) throw new ArgumentException(key + " must be boolean.");
                return (bool)args[key];
            }
            double Number(string key, double fallback) {
                var t = args[key];
                if (t == null) return fallback;
                if (t.Type != JTokenType.Float && t.Type != JTokenType.Integer) throw new ArgumentException(key + " must be numeric.");
                double v = (double)t;
                if (double.IsNaN(v) || double.IsInfinity(v) || v <= 0) throw new ArgumentException(key + " must be positive.");
                return v;
            }
            string uid = Required("importInstanceUniqueId"), layer = Required("textLayerName");
            string units = Required("cadUnits");
            var factors = new Dictionary<string,double> { ["mm"]=1, ["cm"]=10, ["m"]=1000, ["inch"]=25.4, ["ft"]=304.8 };
            if (!factors.ContainsKey(units)) throw new ArgumentException("Unsupported cadUnits.");
            string idKey = beams ? "beamIds" : "columnIds";
            var ids = args[idKey] as JArray;
            if (ids == null || ids.Count == 0 || ids.Any(t => t.Type != JTokenType.Integer || (long)t <= 0))
                throw new ArgumentException("Explicit positive " + idKey + " are required.");
            bool dryRun = Flag("dryRun", true), apply = Flag("apply", false), overwrite = Flag("overwrite", true);
            double maxDistance = Number("maxDistanceMm", 2000), gap = Number("ambiguityGapMm", 100);
            var cad = doc.GetElement(uid) as ImportInstance;
            if (cad == null || !cad.IsLinked) throw new ArgumentException("Specified CAD must be a linked ImportInstance.");
            if (!new FilteredElementCollector(doc, view.Id).OfClass(typeof(ImportInstance)).Any(e => e.Id == cad.Id))
                throw new ArgumentException("CAD is not in the active view.");
            var elements = ids.Values<long>().Distinct().Select(id => doc.GetElement(new ElementId(id))).ToList();
            var category = beams ? BuiltInCategory.OST_StructuralFraming : BuiltInCategory.OST_StructuralColumns;
            if (elements.Any(e => !(e is FamilyInstance) || e.Category.Id.GetIdValue() != (long)category ||
                (beams && ((FamilyInstance)e).StructuralType != Autodesk.Revit.DB.Structure.StructuralType.Beam)))
                throw new ArgumentException("Every ID must be an existing " + (beams ? "structural beam" : "structural column") + " in the current document.");
            string file = GetCadFilePath(doc, cad) ?? throw new InvalidOperationException("Linked DWG path unavailable.");
            string worker = FindWorkerScript() ?? throw new InvalidOperationException("ezdxf_worker.py unavailable.");
            string python = args["pythonExecutable"] == null ? "python" : Required("pythonExecutable");
            // Pass quoted arguments to the process directly; no shell execution.
            if (new[] {file,worker,layer}.Any(s => s.Contains("\"") || s.EndsWith("\\")))
                throw new ArgumentException("Unsupported quote or trailing slash in CAD arguments.");
            var psi = new ProcessStartInfo(python, "\"" + worker + "\" \"" + file + "\" \"" + layer + "\"")
                { UseShellExecute=false, CreateNoWindow=true, RedirectStandardOutput=true, RedirectStandardError=true };
            string odaRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "ODA");
            if (Directory.Exists(odaRoot))
            {
                var odaDirectories = Directory.GetDirectories(odaRoot).Where(d=>File.Exists(Path.Combine(d,"ODAFileConverter.exe")));
                psi.EnvironmentVariables["PATH"] = string.Join(";",odaDirectories) + ";" + Environment.GetEnvironmentVariable("PATH");
            }
            string output;
            using (var process = Process.Start(psi))
            {
                var stdout = process.StandardOutput.ReadToEndAsync();
                var stderr = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(60000)) { process.Kill(); throw new TimeoutException("CAD text reader timed out."); }
                output = stdout.GetAwaiter().GetResult();
                string errors = stderr.GetAwaiter().GetResult();
                if (process.ExitCode != 0) throw new InvalidOperationException("CAD reader failed: " + output + errors);
            }
            var data = JObject.Parse(output);
            if (data["error"] != null) throw new InvalidOperationException((string)data["error"]);
            var labels = new List<(string Text, XYZ Point)>();
            var transform = cad.GetTotalTransform();
            foreach (JObject item in data["data"] as JArray ?? new JArray())
            {
                string label = ((string)item["text"] ?? "").Trim();
                // Accept column codes and an optional dimension suffix; reject notes/formatting.
                var match = Regex.Match(label, @"^([A-Za-z]+\d+[A-Za-z0-9_-]*)(?:\s*[\(（][^()（）]*[\)）])?$");
                if (!match.Success) continue;
                var raw = new XYZ((double)item["x"], (double)item["y"], (double)item["z"]);
                labels.Add((match.Groups[1].Value, transform.OfPoint(raw.Multiply(factors[units]/304.8))));
            }
            if (labels.Count == 0) throw new InvalidOperationException("No supported member codes on the text layer; no writes performed.");
            var rows = new List<JObject>();
            var plans = new List<(Parameter Parameter, string Value, JObject Row, int Label)>();
            foreach (var element in elements)
            {
                var row = new JObject { ["ElementId"]=element.Id.GetIdValue(), ["Status"]="Skipped" }; rows.Add(row);
                // Columns use their insertion point; beams use the location-curve midpoint.
                XYZ point = beams ? (element.Location as LocationCurve)?.Curve.Evaluate(0.5, true)
                    : (element.Location as LocationPoint)?.Point;
                var target = element.get_Parameter(BuiltInParameter.ALL_MODEL_MARK);
                if (point == null) { row["Reason"]="MissingLocation"; continue; }
                if (target == null) { row["Reason"]="MissingMarkParameter"; continue; }
                if (target.StorageType != StorageType.String) { row["Reason"]="IncompatibleStorageType"; continue; }
                row["OldValue"] = target.AsString();
                row["CenterXmm"] = point.X * 304.8; row["CenterYmm"] = point.Y * 304.8;
                row["CenterZmm"] = point.Z * 304.8;
                var ranked = labels.Select((l,i) => new {Index=i, l.Text, Distance=Math.Sqrt(
                    Math.Pow(l.Point.X-point.X,2)+Math.Pow(l.Point.Y-point.Y,2))*304.8})
                    .OrderBy(l => l.Distance).ToList();
                int choice = ColumnMarkMatching.Select(ranked.Select(r=>r.Distance).ToList(), maxDistance, gap);
                var nearest = ranked[0];
                row["DistanceMm"]=nearest.Distance; row["NewValue"]=nearest.Text;
                if (choice == -1) { row["Reason"]="NoNearbyLabel"; continue; }
                if (choice == -2) { row["Reason"]="AmbiguousLabels"; continue; }
                // Reserve labels even for unchanged/read-only-by-overwrite rows to prevent reuse.
                plans.Add((target,nearest.Text,row,nearest.Index));
            }
            var reused = ColumnMarkMatching.ReusedLabels(plans.Select(p=>p.Label));
            foreach (var group in plans.GroupBy(p => p.Label).Where(g => reused.Contains(g.Key)))
                foreach (var p in group) p.Row["Reason"]=beams ? "LabelAssignedToMultipleBeams" : "LabelAssignedToMultipleColumns";
            plans = plans.Where(p => p.Row["Reason"] == null).ToList();
            foreach (var p in plans)
            {
                if (p.Parameter.IsReadOnly) p.Row["Reason"]="ReadOnlyTarget";
                else if (p.Parameter.AsString() == p.Value) p.Row["Status"]="Unchanged";
                else if (!overwrite && !string.IsNullOrWhiteSpace(p.Parameter.AsString())) p.Row["Reason"]="TargetHasValue";
                else p.Row["Status"]="Planned";
            }
            plans = plans.Where(p => (string)p.Row["Status"]=="Planned").ToList();
            bool committed=false, rolledBack=false; string error=null;
            if (apply && !dryRun && plans.Count>0)
            {
                using (var tx = new Transaction(doc,beams ? "Fill beam marks from CAD" : "Fill column marks from CAD"))
                {
                    try {
                        if (tx.Start()!=TransactionStatus.Started) throw new InvalidOperationException("Transaction start failed.");
                        tx.SetFailureHandlingOptions(tx.GetFailureHandlingOptions().SetFailuresPreprocessor(new MarkFailures()).SetClearAfterRollback(true).SetForcedModalHandling(true));
                        foreach (var p in plans) if (!p.Parameter.Set(p.Value)) throw new InvalidOperationException("Parameter.Set failed.");
                        doc.Regenerate();
                        if (plans.Any(p => p.Parameter.AsString()!=p.Value)) throw new InvalidOperationException("Value verification failed.");
                        if (tx.Commit()!=TransactionStatus.Committed) throw new InvalidOperationException("Commit failed.");
                        committed=true; foreach (var p in plans) p.Row["Status"]="Updated";
                    } catch(Exception ex) {
                        error=ex.Message;
                        if(tx.GetStatus()==TransactionStatus.Started) tx.RollBack();
                        rolledBack=tx.GetStatus()==TransactionStatus.RolledBack;
                        foreach(var p in plans) { p.Row["Status"]="Failed"; p.Row["Reason"]="BatchNotCommitted"; }
                    }
                }
            }
            return new { Success=error==null, DryRun=dryRun, Apply=apply, Committed=committed, RolledBack=rolledBack, Error=error,
                ImportInstanceUniqueId=uid, SourceFile=file, TextLayerName=layer, CadUnits=units, LabelCount=labels.Count,
                SourceTextCount=(data["data"] as JArray)?.Count ?? 0,
                TargetParameter="ALL_MODEL_MARK", TargetCategory=beams ? "StructuralFraming" : "StructuralColumns", Scanned=rows.Count, Planned=plans.Count,
                Updated=rows.Count(r=>(string)r["Status"]=="Updated"), Unchanged=rows.Count(r=>(string)r["Status"]=="Unchanged"),
                Skipped=rows.Count(r=>(string)r["Status"]=="Skipped"), Failed=rows.Count(r=>(string)r["Status"]=="Failed"),
                ByReason=rows.Where(r=>r["Reason"]!=null).GroupBy(r=>(string)r["Reason"]).Select(g=>new {Reason=g.Key,Count=g.Count()}).ToList(),
                Results=rows };
        }
        private sealed class MarkFailures : IFailuresPreprocessor {
            public FailureProcessingResult PreprocessFailures(FailuresAccessor a) =>
                a.GetFailureMessages().Any(m=>m.GetSeverity()==FailureSeverity.Error) ? FailureProcessingResult.ProceedWithRollBack : FailureProcessingResult.Continue;
        }
    }
}
