using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;

namespace RevitMCP.Core
{
    public partial class CommandExecutor
    {
        private sealed class ParameterCopyRow
        {
            public long ElementId { get; set; }
            public string Category { get; set; } = "";
            public string Status { get; set; } = "Skipped";
            public string? Reason { get; set; }
            public string? StorageType { get; set; }
            public string? SourceText { get; set; }
            public object? OldValue { get; set; }
            public object? NewValue { get; set; }
            public bool WasPlanned { get; set; }
        }

        private sealed class ParameterCopyFailures : IFailuresPreprocessor
        {
            public FailureProcessingResult PreprocessFailures(FailuresAccessor accessor)
            {
                return accessor.GetFailureMessages().Any(f => f.GetSeverity() == FailureSeverity.Error)
                    ? FailureProcessingResult.ProceedWithRollBack : FailureProcessingResult.Continue;
            }
        }

        private object CopyParameterByGroup(JObject parameters)
        {
            // Validate on the Revit side too: JSON schema defaults are annotations, not execution.
            string sourceName = CopyParameterString(parameters, "sourceParameterName", "頂部約束");
            string targetName = CopyParameterString(parameters, "targetParameterName", "牆頂至");
            ForgeTypeId sourceGroup = CopyParameterGroup(CopyParameterString(parameters, "sourceGroup", "約束"));
            ForgeTypeId targetGroup = CopyParameterGroup(CopyParameterString(parameters, "targetGroup", "文字"));
            if (CopyParameterString(parameters, "scope", "project") != "project")
                throw new ArgumentException("Only scope=project is supported.");
            bool dryRun = CopyParameterBoolean(parameters, "dryRun", true);
            bool apply = CopyParameterBoolean(parameters, "apply", false);
            bool overwrite = CopyParameterBoolean(parameters, "overwrite", true);
            bool write = apply && !dryRun;
            string transform = CopyParameterString(parameters, "valueTransform", "afterColon");
            if (transform != "raw" && transform != "afterColon")
                throw new ArgumentException("valueTransform must be raw or afterColon.");
            var categoryMap = new Dictionary<string, BuiltInCategory>(StringComparer.Ordinal)
            {
                ["Walls"] = BuiltInCategory.OST_Walls,
                ["StructuralColumns"] = BuiltInCategory.OST_StructuralColumns,
                ["StructuralFraming"] = BuiltInCategory.OST_StructuralFraming,
                ["Floors"] = BuiltInCategory.OST_Floors,
                ["Doors"] = BuiltInCategory.OST_Doors,
                ["Windows"] = BuiltInCategory.OST_Windows
            };
            string[] categories = new[] { "Walls" };
            if (parameters.TryGetValue("categories", out JToken? categoryToken))
            {
                if (!(categoryToken is JArray array) || array.Count == 0 ||
                    array.Any(t => t.Type != JTokenType.String || !categoryMap.ContainsKey(t.Value<string>()!)))
                    throw new ArgumentException("categories must be a non-empty array of Walls, StructuralColumns, StructuralFraming, Floors, Doors, Windows.");
                categories = array.Values<string>().Select(s => s!).Distinct(StringComparer.Ordinal).ToArray();
            }
            Document doc = _uiApp.ActiveUIDocument?.Document
                ?? throw new InvalidOperationException("No active document.");
            if (doc.IsFamilyDocument)
                throw new InvalidOperationException("A project document is required.");

            var rows = new List<ParameterCopyRow>();
            var plans = new List<(Parameter Target, object Value, ParameterCopyRow Row)>();
            string? error = null;
            bool committed = false;
            bool rolledBack = false;
            // Complete the read-only plan before opening the single write transaction.
            foreach (string category in categories)
            {
                foreach (Element element in new FilteredElementCollector(doc)
                    .OfCategory(categoryMap[category]).WhereElementIsNotElementType().OrderBy(e => e.Id.GetIdValue()))
                {
                    var row = new ParameterCopyRow { ElementId = element.Id.GetIdValue(), Category = category };
                    rows.Add(row);
                    try
                    {
                        var sources = element.GetParameters(sourceName)
                            .Where(p => p.Definition.GetGroupTypeId().Equals(sourceGroup)).ToList();
                        var targets = element.GetParameters(targetName)
                            .Where(p => p.Definition.GetGroupTypeId().Equals(targetGroup)).ToList();
                        if (sources.Count != 1 || targets.Count != 1)
                        {
                            row.Reason = sources.Count == 0 ? "MissingSourceParameter"
                                : sources.Count > 1 ? "AmbiguousSourceParameter"
                                : targets.Count == 0 ? "MissingTargetParameter" : "AmbiguousTargetParameter";
                            continue;
                        }
                        Parameter source = sources[0], target = targets[0];
                        row.StorageType = source.StorageType.ToString();
                        row.OldValue = CopyParameterValue(target);
                        row.NewValue = CopyParameterValue(source);
                        if (CopyParameterEmpty(source)) { row.Reason = "EmptySource"; continue; }
                        if (target.IsReadOnly) { row.Reason = "ReadOnlyTarget"; continue; }
                        if (transform == "afterColon")
                        {
                            if (target.StorageType != StorageType.String || source.StorageType == StorageType.None)
                            { row.Reason = "IncompatibleStorageType"; continue; }
                            row.SourceText = source.StorageType == StorageType.String
                                ? source.AsString() : source.AsValueString();
                            row.NewValue = null;
                            if (string.IsNullOrWhiteSpace(row.SourceText))
                            { row.Reason = "EmptySource"; continue; }
                            int colon = row.SourceText.IndexOfAny(new[] { ':', '：' });
                            if (colon < 0) { row.Reason = "MissingDelimiter"; continue; }
                            row.NewValue = row.SourceText.Substring(colon + 1).Trim();
                            if (string.IsNullOrWhiteSpace((string)row.NewValue))
                            { row.Reason = "EmptyTransformedSource"; continue; }
                            row.StorageType = StorageType.String.ToString();
                        }
                        else
                        {
                            if (source.StorageType == StorageType.None || source.StorageType != target.StorageType)
                            { row.Reason = "IncompatibleStorageType"; continue; }
                            // Same storage alone is insufficient (e.g. length vs area, integer vs Yes/No).
                            if (!source.Definition.GetDataType().Equals(target.Definition.GetDataType()))
                            { row.Reason = "IncompatibleDataType"; continue; }
                        }
                        if (target.HasValue && Equals(row.OldValue, row.NewValue))
                        { row.Status = "Unchanged"; continue; }
                        if (!overwrite && !CopyParameterEmpty(target))
                        { row.Reason = "TargetHasValue"; continue; }
                        row.WasPlanned = true;
                        row.Status = "Planned";
                        // Preserve ElementId objects for Parameter.Set; reports use numeric IDs.
                        object value = transform == "raw" && source.StorageType == StorageType.ElementId
                            ? (object)source.AsElementId() : row.NewValue!;
                        plans.Add((target, value, row));
                    }
                    catch (Exception ex)
                    {
                        row.Status = "Failed";
                        row.Reason = "ReadFailed: " + ex.Message;
                    }
                }
            }
            if (write && plans.Count > 0)
            {
                using (var transaction = new Transaction(doc, "Copy parameter by group"))
                {
                    try
                    {
                        if (transaction.Start() != TransactionStatus.Started)
                            throw new InvalidOperationException("Could not start transaction.");
                        transaction.SetFailureHandlingOptions(transaction.GetFailureHandlingOptions()
                            .SetFailuresPreprocessor(new ParameterCopyFailures())
                            .SetClearAfterRollback(true).SetForcedModalHandling(true));
                        foreach (var plan in plans)
                        {
                            bool set;
                            switch (plan.Target.StorageType)
                            {
                                case StorageType.String: set = plan.Target.Set((string)plan.Value); break;
                                case StorageType.Integer: set = plan.Target.Set((int)plan.Value); break;
                                case StorageType.Double: set = plan.Target.Set((double)plan.Value); break;
                                case StorageType.ElementId: set = plan.Target.Set((ElementId)plan.Value); break;
                                default: throw new InvalidOperationException("Unsupported storage type.");
                            }
                            if (!set)
                                throw new InvalidOperationException("Parameter.Set returned false for element " + plan.Row.ElementId);
                        }
                        doc.Regenerate();
                        // Do not report success if constraints/formulas changed the requested values.
                        foreach (var plan in plans)
                            if (!Equals(CopyParameterValue(plan.Target), plan.Row.NewValue))
                                throw new InvalidOperationException("Value verification failed for element " + plan.Row.ElementId);
                        if (transaction.Commit() != TransactionStatus.Committed)
                            throw new InvalidOperationException("Transaction did not commit.");
                        committed = true;
                        foreach (var plan in plans) plan.Row.Status = "Updated";
                    }
                    catch (Exception ex)
                    {
                        error = ex.Message;
                        if (transaction.GetStatus() == TransactionStatus.Started)
                            transaction.RollBack();
                        rolledBack = transaction.GetStatus() == TransactionStatus.RolledBack;
                        foreach (var plan in plans)
                        {
                            plan.Row.Status = "Failed";
                            plan.Row.Reason = "BatchNotCommitted: " + error;
                        }
                    }
                }
            }
            return new
            {
                Success = error == null && rows.All(r => r.Status != "Failed"),
                ValueTransform = transform, DryRun = dryRun, Apply = apply, WriteEnabled = write, Overwrite = overwrite,
                Committed = committed, RolledBack = rolledBack, Error = error, Scope = "project",
                SourceParameterName = sourceName, SourceGroupTypeId = sourceGroup.TypeId,
                TargetParameterName = targetName, TargetGroupTypeId = targetGroup.TypeId,
                Scanned = rows.Count, Planned = rows.Count(r => r.WasPlanned),
                Updated = rows.Count(r => r.Status == "Updated"),
                Unchanged = rows.Count(r => r.Status == "Unchanged"),
                Skipped = rows.Count(r => r.Status == "Skipped"),
                Failed = rows.Count(r => r.Status == "Failed"),
                ByCategory = categories.Select(c => new
                {
                    Category = c, Counts = CopyParameterCounts(rows.Where(r => r.Category == c).ToList())
                }).ToList(),
                ByReason = rows.Where(r => r.Reason != null).GroupBy(r => r.Reason)
                    .Select(g => new { Reason = g.Key, Count = g.Count() }).ToList(),
                ValueChanges = rows.Where(r => r.WasPlanned || r.Status == "Unchanged")
                    .GroupBy(r => new { r.Category, r.StorageType, r.OldValue, r.NewValue })
                    .Select(g => new
                    {
                        g.Key.Category, g.Key.StorageType, g.Key.OldValue, g.Key.NewValue,
                        Count = g.Count(), Planned = g.Count(r => r.WasPlanned),
                        Updated = g.Count(r => r.Status == "Updated"),
                        Unchanged = g.Count(r => r.Status == "Unchanged"),
                        Failed = g.Count(r => r.Status == "Failed")
                    }).ToList(),
                Results = rows
            };
        }

        private static object CopyParameterCounts(List<ParameterCopyRow> rows) => new
        {
            Scanned = rows.Count, Planned = rows.Count(r => r.WasPlanned),
            Updated = rows.Count(r => r.Status == "Updated"),
            Unchanged = rows.Count(r => r.Status == "Unchanged"),
            Skipped = rows.Count(r => r.Status == "Skipped"),
            Failed = rows.Count(r => r.Status == "Failed")
        };

        private static string CopyParameterString(JObject args, string name, string fallback)
        {
            if (!args.TryGetValue(name, out JToken? token)) return fallback;
            if (token.Type != JTokenType.String || string.IsNullOrWhiteSpace(token.Value<string>()))
                throw new ArgumentException(name + " must be a non-empty string.");
            return token.Value<string>()!;
        }

        private static bool CopyParameterBoolean(JObject args, string name, bool fallback)
        {
            if (!args.TryGetValue(name, out JToken? token)) return fallback;
            if (token.Type != JTokenType.Boolean) throw new ArgumentException(name + " must be a boolean.");
            return token.Value<bool>();
        }

        private static ForgeTypeId CopyParameterGroup(string name)
        {
            switch (name.ToLowerInvariant())
            {
                case "約束": case "constraints": case "pg_constraints": return GroupTypeId.Constraints;
                case "文字": case "text": case "pg_text": return GroupTypeId.Text;
            }
            // Resolve only explicit built-in group IDs; never infer groups from localized labels.
            foreach (var property in typeof(GroupTypeId).GetProperties(
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
            {
                if (property.PropertyType == typeof(ForgeTypeId) &&
                    property.GetValue(null) is ForgeTypeId id && id.TypeId == name)
                    return id;
            }
            throw new ArgumentException("Unknown parameter group: " + name + ". Use Constraints, Text or a full built-in group TypeId.");
        }

        private static bool CopyParameterEmpty(Parameter parameter)
        {
            return !parameter.HasValue ||
                (parameter.StorageType == StorageType.String && string.IsNullOrWhiteSpace(parameter.AsString())) ||
                (parameter.StorageType == StorageType.ElementId && parameter.AsElementId() == ElementId.InvalidElementId);
        }

        private static object? CopyParameterValue(Parameter parameter)
        {
            if (!parameter.HasValue) return null;
            switch (parameter.StorageType)
            {
                case StorageType.String: return parameter.AsString();
                case StorageType.Integer: return parameter.AsInteger();
                case StorageType.Double: return parameter.AsDouble();
                case StorageType.ElementId: return parameter.AsElementId().GetIdValue();
                default: return null;
            }
        }
    }
}
