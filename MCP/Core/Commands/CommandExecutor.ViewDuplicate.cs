using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;

#nullable disable

#if REVIT2025_OR_GREATER
using IdType = System.Int64;
#else
using IdType = System.Int32;
#endif

namespace RevitMCP.Core
{
    public partial class CommandExecutor
    {
        private object DuplicateViewsWithDetailing(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;

            List<IdType> sourceViewIds = ReadIdList(parameters["sourceViewIds"] ?? parameters["viewIds"] ?? parameters["sourceViewId"]);
            if (sourceViewIds.Count == 0)
                throw new Exception("sourceViewIds is required.");

            List<string> targetNames = ReadStringList(parameters["targetNames"] ?? parameters["newNames"] ?? parameters["targetName"]);
            string suffix = parameters["suffix"]?.Value<string>() ?? "高於6m牆標示(W)";
            bool copyCropFromSource = parameters["copyCropFromSource"]?.Value<bool>() ?? true;
            bool setActiveLast = parameters["setActiveLast"]?.Value<bool>() ?? false;

            var existingNames = new HashSet<string>(
                new FilteredElementCollector(doc)
                    .OfClass(typeof(View))
                    .Cast<View>()
                    .Select(v => v.Name),
                StringComparer.OrdinalIgnoreCase);

            var results = new List<object>();
            View lastView = null;

            using (Transaction trans = new Transaction(doc, "Duplicate views with detailing"))
            {
                trans.Start();

                for (int i = 0; i < sourceViewIds.Count; i++)
                {
                    IdType sourceViewId = sourceViewIds[i];
                    View sourceView = doc.GetElement(sourceViewId.ToElementId()) as View;
                    if (sourceView == null)
                    {
                        results.Add(new { SourceViewId = sourceViewId, Success = false, Message = "Source view not found." });
                        continue;
                    }

                    if (!sourceView.CanViewBeDuplicated(ViewDuplicateOption.WithDetailing))
                    {
                        results.Add(new { SourceViewId = sourceViewId, SourceViewName = sourceView.Name, Success = false, Message = "View cannot be duplicated with detailing." });
                        continue;
                    }

                    string requestedName = i < targetNames.Count && !string.IsNullOrWhiteSpace(targetNames[i])
                        ? targetNames[i].Trim()
                        : $"{sourceView.Name}-{suffix}";
                    string finalName = MakeUniqueViewName(requestedName, existingNames);

                    ElementId newViewId = sourceView.Duplicate(ViewDuplicateOption.WithDetailing);
                    View newView = doc.GetElement(newViewId) as View;
                    if (newView != null)
                    {
                        try { newView.Name = finalName; }
                        catch { newView.Name = MakeUniqueViewName($"{requestedName}-{newView.Id.GetIdValue()}", existingNames); }

                        if (copyCropFromSource)
                        {
                            CopyBasicCropSettings(sourceView, newView);
                        }

                        existingNames.Add(newView.Name);
                        lastView = newView;
                    }

                    results.Add(new
                    {
                        SourceViewId = sourceViewId,
                        SourceViewName = sourceView.Name,
                        NewViewId = newView?.Id.GetIdValue(),
                        NewViewName = newView?.Name,
                        Success = newView != null,
                        DuplicateOption = "WithDetailing"
                    });
                }

                trans.Commit();
            }

            if (setActiveLast && lastView != null)
            {
                _uiApp.ActiveUIDocument.ActiveView = lastView;
            }

            return new
            {
                Success = true,
                Count = results.Count,
                Views = results
            };
        }

        private static List<IdType> ReadIdList(JToken token)
        {
            if (token == null)
                return new List<IdType>();
            if (token is JArray array)
                return array.Select(x => x.Value<IdType>()).Where(x => x > 0).Distinct().ToList();
            IdType value = token.Value<IdType>();
            return value > 0 ? new List<IdType> { value } : new List<IdType>();
        }

        private static List<string> ReadStringList(JToken token)
        {
            if (token == null)
                return new List<string>();
            if (token is JArray array)
                return array.Select(x => x.Value<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
            string value = token.Value<string>();
            return string.IsNullOrWhiteSpace(value) ? new List<string>() : new List<string> { value };
        }

        private static string MakeUniqueViewName(string requestedName, HashSet<string> existingNames)
        {
            string baseName = string.IsNullOrWhiteSpace(requestedName) ? "Duplicated View" : requestedName.Trim();
            if (!existingNames.Contains(baseName))
                return baseName;

            for (int i = 1; i < 1000; i++)
            {
                string candidate = $"{baseName} ({i})";
                if (!existingNames.Contains(candidate))
                    return candidate;
            }

            return $"{baseName} ({DateTime.Now:HHmmss})";
        }

        private static void CopyBasicCropSettings(View sourceView, View targetView)
        {
            try
            {
                targetView.CropBoxActive = sourceView.CropBoxActive;
                targetView.CropBoxVisible = sourceView.CropBoxVisible;
                if (sourceView.CropBox != null)
                    targetView.CropBox = sourceView.CropBox;
            }
            catch
            {
                // Some view types or templates can reject crop assignment; duplication itself remains valid.
            }
        }
    }
}
