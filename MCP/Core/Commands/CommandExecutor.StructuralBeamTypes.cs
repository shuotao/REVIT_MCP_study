using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;

namespace RevitMCP.Core
{
    public partial class CommandExecutor
    {
        // Creates types only; never activates symbols or places instances.
        private object CreateStructuralBeamTypes(JObject parameters)
        {
            var created = new List<object>();
            var skipped = new List<object>();
            var failed = new List<object>();
            var pending = new List<JObject>();
            string? error = null;
            string familyName = "混凝土樑-矩形";
            try
            {
                familyName = parameters["familyName"]?.Value<string>() ?? familyName;
                string? baseTypeName = parameters["baseTypeName"]?.Value<string>();
                var items = parameters["types"] as JArray;
                if (items == null || items.Count == 0)
                    throw new ArgumentException("types must be a non-empty array.");
                Document doc = _uiApp.ActiveUIDocument?.Document
                    ?? throw new InvalidOperationException("No active document.");
                var symbols = new FilteredElementCollector(doc)
                    .OfClass(typeof(FamilySymbol)).OfCategory(BuiltInCategory.OST_StructuralFraming)
                    .Cast<FamilySymbol>().Where(s => s.FamilyName == familyName)
                    .OrderBy(s => s.Name, StringComparer.Ordinal).ToList();
                var existing = symbols.ToDictionary(s => s.Name, StringComparer.OrdinalIgnoreCase);
                var requested = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (JToken item in items)
                {
                    var entry = item as JObject;
                    string? name = entry?["typeName"]?.Type == JTokenType.String
                        ? entry["typeName"]!.Value<string>() : null;
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        failed.Add(new { TypeName = name, Reason = "typeName must be a non-empty string." });
                        continue;
                    }
                    if (existing.TryGetValue(name, out FamilySymbol? symbol))
                    {
                        skipped.Add(new { TypeName = name, TypeId = symbol.Id.GetIdValue(), Reason = "Already exists in the selected family." });
                        continue;
                    }
                    if (!requested.Add(name))
                    {
                        skipped.Add(new { TypeName = name, Reason = "Duplicate request name; first entry takes precedence." });
                        continue;
                    }
                    pending.Add(entry!);
                }
                FamilySymbol? baseSymbol = baseTypeName == null
                    ? symbols.FirstOrDefault() : symbols.FirstOrDefault(s => s.Name == baseTypeName);
                if (baseSymbol == null)
                    throw new ArgumentException($"Structural Framing base type not found: {familyName} / {baseTypeName ?? "(first type)"}.");
                foreach (JObject item in pending)
                {
                    ReadBeamDimensionMm(item, "widthMm");
                    ReadBeamDimensionMm(item, "heightMm");
                }
                if (failed.Count > 0)
                    throw new ArgumentException("Invalid batch entry.");
                if (pending.Count > 0)
                {
                    using (var transaction = new Transaction(doc, "Create structural beam types"))
                    {
                        try
                        {
                            if (transaction.Start() != TransactionStatus.Started)
                                throw new InvalidOperationException("Could not start transaction.");
                            transaction.SetFailureHandlingOptions(transaction.GetFailureHandlingOptions()
                                .SetFailuresPreprocessor(new BeamTypeFailuresPreprocessor())
                                .SetClearAfterRollback(true).SetForcedModalHandling(true));
                            foreach (JObject item in pending)
                            {
                                string name = item["typeName"]!.Value<string>()!;
                                var symbol = baseSymbol.Duplicate(name) as FamilySymbol
                                    ?? throw new InvalidOperationException($"Failed to duplicate {name}.");
                                double width = ReadBeamDimensionMm(item, "widthMm");
                                double height = ReadBeamDimensionMm(item, "heightMm");
                                SetBeamTypeDimension(symbol, width, new[] { "b", "B", "寬度", "Width" });
                                SetBeamTypeDimension(symbol, height, new[] { "h", "H", "高度", "Height" });
                                created.Add(new { TypeName = name, TypeId = symbol.Id.GetIdValue(), WidthMm = width, HeightMm = height });
                            }
                            doc.Regenerate();
                            if (transaction.Commit() != TransactionStatus.Committed)
                                throw new InvalidOperationException("Transaction was not committed; batch rolled back.");
                        }
                        catch
                        {
                            if (transaction.GetStatus() == TransactionStatus.Started)
                                transaction.RollBack();
                            throw;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
                created.Clear();
                foreach (JObject item in pending)
                    failed.Add(new { TypeName = item["typeName"]?.Value<string>(), Reason = "Batch not committed: " + error });
                if (pending.Count == 0 && failed.Count == 0)
                    failed.Add(new { TypeName = (string?)null, Reason = error });
            }
            return new { Success = error == null, FamilyName = familyName, Created = created, Skipped = skipped, Failed = failed, Error = error };
        }

        private static double ReadBeamDimensionMm(JObject item, string key)
        {
            JToken? token = item[key];
            if (token == null || (token.Type != JTokenType.Integer && token.Type != JTokenType.Float))
                throw new ArgumentException($"{item["typeName"]}: {key} must be a number.");
            double value = token.Value<double>();
            if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0)
                throw new ArgumentException($"{item["typeName"]}: {key} must be finite and greater than zero.");
            return value;
        }

        private static void SetBeamTypeDimension(FamilySymbol symbol, double mm, string[] names)
        {
            Parameter? parameter = names.SelectMany(name => symbol.GetParameters(name))
                .FirstOrDefault(p => !p.IsReadOnly && p.StorageType == StorageType.Double
                    && p.Definition.GetDataType() == SpecTypeId.Length);
            if (parameter == null)
                throw new InvalidOperationException($"{symbol.Name}: no writable length parameter ({string.Join("/", names)}).");
            double value = UnitUtils.ConvertToInternalUnits(mm, UnitTypeId.Millimeters);
            if (!parameter.Set(value) || Math.Abs(parameter.AsDouble() - value) > 1e-9)
                throw new InvalidOperationException($"{symbol.Name}: failed to set {parameter.Definition.Name}.");
        }

        private sealed class BeamTypeFailuresPreprocessor : IFailuresPreprocessor
        {
            public FailureProcessingResult PreprocessFailures(FailuresAccessor accessor)
            {
                foreach (FailureMessageAccessor failure in accessor.GetFailureMessages())
                {
                    if (failure.GetSeverity() != FailureSeverity.Warning)
                        return FailureProcessingResult.ProceedWithRollBack;
                    accessor.DeleteWarning(failure);
                }
                return FailureProcessingResult.Continue;
            }
        }
    }
}
