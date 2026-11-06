using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
#if REVIT2025_OR_GREATER
using TypeNameId = System.Int64;
#else
using TypeNameId = System.Int32;
#endif

namespace RevitMCP.Core
{
    public partial class CommandExecutor
    {
        private object CopyTypeNameBatch(JObject args)
        {
            if (args["rcWallsStructuralBatch"]?.Value<bool>() == true || args["elementId"] != null)
                throw new ArgumentException("Batch modes and single-element mode cannot be combined.");
            string name = args["parameterName"]?.Value<string>();
            if (string.IsNullOrWhiteSpace(name) || args["value"]?.Value<string>() != "")
                throw new ArgumentException("Specify parameterName and value as an empty string for type-name copying.");
            var ids = args["elementIds"] as JArray;
            if (ids == null || ids.Count == 0 || ids.Count > 10000 ||
                ids.Any(x => x.Type != JTokenType.Integer || x.Value<long>() <= 0 || x.Value<long>() > TypeNameId.MaxValue))
                throw new ArgumentException("Explicit positive elementIds required (1..10000).");
            var doc = _uiApp.ActiveUIDocument.Document;
            if (doc.IsFamilyDocument) throw new InvalidOperationException("Project document required.");
            bool dry = args["dryRun"]?.Value<bool>() ?? true;
            bool apply = args["apply"]?.Value<bool>() ?? false;
            var rows = new JArray();
            var plans = new List<(Element Element, Parameter Target, string Value, JObject Row)>();
            foreach (long id in ids.Values<long>().Distinct())
            {
                var row = new JObject { ["ElementId"] = id, ["Status"] = "Skipped" };
                rows.Add(row);
                var element = doc.GetElement(new ElementId((TypeNameId)id));
                if (element == null || element is ElementType) { row["Reason"] = "NotInstance"; continue; }
                var type = doc.GetElement(element.GetTypeId()) as ElementType;
                if (type == null || string.IsNullOrWhiteSpace(type.Name)) { row["Reason"] = "MissingTypeName"; continue; }
                row["TypeName"] = type.Name;
                var matches = element.GetParameters(name);
                if (matches.Count != 1) { row["Reason"] = matches.Count == 0 ? "MissingParameter" : "AmbiguousParameter"; continue; }
                var target = matches[0];
                if (target.StorageType != StorageType.String) { row["Reason"] = "NotStringParameter"; continue; }
                row["OldValue"] = target.AsString();
                row["NewValue"] = type.Name;
                if (target.AsString() == type.Name) { row["Status"] = "Unchanged"; continue; }
                if (target.IsReadOnly || element.GroupId != ElementId.InvalidElementId)
                { row["Reason"] = "ReadOnlyOrGrouped"; continue; }
                row["Status"] = "Planned";
                plans.Add((element, target, type.Name, row));
            }
            bool committed = false;
            string error = null;
            if (apply && !dry && plans.Count > 0)
            {
                using (var group = new TransactionGroup(doc, "Batch copy type names"))
                {
                    group.Start();
                    try
                    {
                        using (var tx = new Transaction(doc, "Fill instance text parameters"))
                        {
                            tx.Start();
                            tx.SetFailureHandlingOptions(tx.GetFailureHandlingOptions()
                                .SetFailuresPreprocessor(new ParameterCopyFailures()).SetClearAfterRollback(true));
                            foreach (var plan in plans)
                                if (!plan.Target.Set(plan.Value)) throw new InvalidOperationException("Set failed: " + plan.Element.Id.GetIdValue());
                            if (tx.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("Transaction not committed.");
                        }
                        foreach (var plan in plans)
                        {
                            var current = doc.GetElement(plan.Element.Id).GetParameters(name);
                            if (current.Count != 1 || current[0].AsString() != plan.Value)
                                throw new InvalidOperationException("Post-commit verification failed: " + plan.Element.Id.GetIdValue());
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
            return new {
                Success = error == null, DryRun = dry, Apply = apply, Committed = committed, Error = error,
                ParameterName = name, Scanned = rows.Count, Planned = plans.Count,
                Updated = rows.Count(r => (string)r["Status"] == "Updated"),
                Unchanged = rows.Count(r => (string)r["Status"] == "Unchanged"),
                Skipped = rows.Count(r => (string)r["Status"] == "Skipped"),
                Failed = rows.Count(r => (string)r["Status"] == "Failed"), Results = rows
            };
        }
    }
}
