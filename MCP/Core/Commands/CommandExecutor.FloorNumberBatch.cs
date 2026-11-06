using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
#if REVIT2025_OR_GREATER
using FloorNumberId = System.Int64;
#else
using FloorNumberId = System.Int32;
#endif

namespace RevitMCP.Core
{
    public partial class CommandExecutor
    {
        private object SetFloorNumbersByThickness(JObject args)
        {
            if (args["elementId"] != null || args["copyTypeNameBatch"]?.Value<bool>() == true || args["rcWallsStructuralBatch"]?.Value<bool>() == true
                || args["parameterName"]?.Value<string>() != "編號" || args["value"]?.Value<string>() != "")
                throw new ArgumentException("Use floorNumberRules alone with parameterName=編號 and value empty.");
            var doc = _uiApp.ActiveUIDocument.Document;
            if (doc.IsFamilyDocument || args["viewId"]?.Value<long>() != doc.ActiveView.Id.GetIdValue())
                throw new ArgumentException("Current project viewId required.");
            var ids = args["elementIds"] as JArray;
            if (ids == null || ids.Count == 0 || ids.Count > 10000 || ids.Any(x => x.Type != JTokenType.Integer || x.Value<long>() <= 0 || x.Value<long>() > FloorNumberId.MaxValue))
                throw new ArgumentException("Explicit positive elementIds required.");
            var ruleArray = args["floorNumberRules"] as JArray;
            if (ruleArray == null || ruleArray.Count == 0 || ruleArray.Count > 100) throw new ArgumentException("Rules required.");
            var rules = ruleArray.Select(r => (Thickness: r["thicknessMm"]?.Value<double>() ?? double.NaN, Number: r["number"]?.Value<string>())).ToList();
            if (rules.Any(r => double.IsNaN(r.Thickness) || double.IsInfinity(r.Thickness) || r.Thickness <= 0 || string.IsNullOrWhiteSpace(r.Number))
                || rules.Any(r => rules.Count(s => Math.Abs(s.Thickness-r.Thickness) <= 0.2) > 1)) throw new ArgumentException("Invalid or overlapping thickness rules.");
            var visible = new HashSet<long>(new FilteredElementCollector(doc, doc.ActiveView.Id).OfClass(typeof(Floor)).WhereElementIsNotElementType().ToElementIds().Select(id => (long)id.GetIdValue()));
            var rows = new JArray();
            var plans = new List<(ElementId Id, Parameter Target, string Value, JObject Row)>();
            foreach (long id in ids.Values<long>().Distinct())
            {
                var row = new JObject { ["ElementId"] = id, ["Status"] = "Skipped" }; rows.Add(row);
                if (!visible.Contains(id) || !(doc.GetElement(new ElementId((FloorNumberId)id)) is Floor floor)) { row["Reason"] = "NotFloorInCurrentView"; continue; }
                var thickness = floor.get_Parameter(BuiltInParameter.FLOOR_ATTR_THICKNESS_PARAM);
                if (thickness == null || thickness.StorageType != StorageType.Double || !thickness.HasValue) { row["Reason"] = "MissingThickness"; continue; }
                double mm = UnitUtils.ConvertFromInternalUnits(thickness.AsDouble(), UnitTypeId.Millimeters);
                row["ThicknessMm"] = mm;
                var matches = rules.Where(r => Math.Abs(r.Thickness-mm) <= 0.1).ToList();
                if (matches.Count != 1) { row["Reason"] = "NoMatchingThicknessRule"; continue; }
                var targets = floor.GetParameters("編號").Where(p => p.Definition.GetGroupTypeId().Equals(GroupTypeId.Text)).ToList();
                if (targets.Count != 1) { row["Reason"] = "MissingOrAmbiguousTextNumber"; continue; }
                var target = targets[0];
                if (target.StorageType != StorageType.String) { row["Reason"] = "NotString"; continue; }
                string value = matches[0].Number;
                row["OldValue"] = target.AsString(); row["NewValue"] = value;
                if (target.AsString() == value) { row["Status"] = "Unchanged"; continue; }
                if (target.IsReadOnly || floor.GroupId != ElementId.InvalidElementId) { row["Reason"] = "ReadOnlyOrGrouped"; continue; }
                row["Status"] = "Planned"; plans.Add((floor.Id, target, value, row));
            }
            bool dry = args["dryRun"]?.Value<bool>() ?? true, apply = args["apply"]?.Value<bool>() ?? false, committed = false;
            string error = null;
            if (!dry && apply && plans.Count > 0)
            {
                using (var group = new TransactionGroup(doc, "Batch floor numbers by thickness"))
                {
                    group.Start();
                    try
                    {
                        using (var tx = new Transaction(doc, "Update floor text numbers"))
                        {
                            tx.Start();
                            tx.SetFailureHandlingOptions(tx.GetFailureHandlingOptions().SetFailuresPreprocessor(new ParameterCopyFailures()).SetClearAfterRollback(true));
                            foreach (var plan in plans) if (!plan.Target.Set(plan.Value)) throw new InvalidOperationException("Set failed: " + plan.Id);
                            if (tx.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("Transaction not committed.");
                        }
                        foreach (var plan in plans)
                        {
                            var current = doc.GetElement(plan.Id).GetParameters("編號").Where(p => p.Definition.GetGroupTypeId().Equals(GroupTypeId.Text)).ToList();
                            if (current.Count != 1 || current[0].AsString() != plan.Value) throw new InvalidOperationException("Verification failed: " + plan.Id);
                        }
                        if (group.Assimilate() != TransactionStatus.Committed) throw new InvalidOperationException("Group commit failed.");
                        committed = true;
                        foreach (var plan in plans) plan.Row["Status"] = "Updated";
                    }
                    catch (Exception ex)
                    {
                        if (group.GetStatus() == TransactionStatus.Started) group.RollBack();
                        error = ex.Message;
                        foreach (var plan in plans) { plan.Row["Status"] = "Failed"; plan.Row["Reason"] = "BatchNotCommitted"; }
                    }
                }
            }
            return new { Success = error == null, DryRun = dry, Apply = apply, Committed = committed, Error = error,
                ViewId = doc.ActiveView.Id.GetIdValue(), Scanned = rows.Count, Planned = plans.Count,
                Updated = rows.Count(r => (string)r["Status"] == "Updated"), Unchanged = rows.Count(r => (string)r["Status"] == "Unchanged"),
                Skipped = rows.Count(r => (string)r["Status"] == "Skipped"), Failed = rows.Count(r => (string)r["Status"] == "Failed"), Results = rows };
        }
    }
}
