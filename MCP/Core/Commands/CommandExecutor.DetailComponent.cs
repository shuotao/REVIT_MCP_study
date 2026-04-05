using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;

#if REVIT2025_OR_GREATER
using IdType = System.Int64;
#else
using IdType = System.Int32;
#endif

namespace RevitMCP.Core
{
    public partial class CommandExecutor
    {
        #region 詳圖元件管理

        /// <summary>
        /// 取得詳圖元件列表
        /// </summary>
        private object GetDetailComponents(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;
            string familyFilter = parameters?["familyName"]?.Value<string>() ?? "";

            var detailItems = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_DetailComponents)
                .WhereElementIsNotElementType()
                .Where(e =>
                {
                    var fi = e as FamilyInstance;
                    if (fi == null) return false;
                    return string.IsNullOrEmpty(familyFilter) || fi.Symbol.FamilyName.Contains(familyFilter);
                })
                .Select(e =>
                {
                    var fi = e as FamilyInstance;
                    return new
                    {
                        ElementId = fi.Id.GetIdValue(),
                        FamilyName = fi.Symbol.FamilyName,
                        TypeName = fi.Symbol.Name,
                        OwnerViewId = fi.OwnerViewId.GetIdValue(),
                        Parameters = GetAllDetailParameters(fi)
                    };
                })
                .ToList();

            return new
            {
                Count = detailItems.Count,
                Items = detailItems
            };
        }

        private Dictionary<string, string> GetAllDetailParameters(Element elem)
        {
            var result = new Dictionary<string, string>();
            foreach (Parameter p in elem.Parameters)
            {
                string val = p.AsValueString() ?? p.AsString() ?? "";
                if (!string.IsNullOrEmpty(val))
                {
                    result[p.Definition.Name] = val;
                }
            }
            return result;
        }

        /// <summary>
        /// 同步詳圖元件編號與圖紙編號 (v3.5: Safeguard Mode)
        /// </summary>
        private object SyncDetailComponentNumbers()
        {
            Document doc = _uiApp.ActiveUIDocument.Document;
            int updatedInstances = 0;
            int typesCreated = 0;
            var processedTypes = new HashSet<string>();

            // 1. 建立 ViewId → Sheet 對應表
            var viewToSheetMap = new Dictionary<IdType, ViewSheet>();
            var sheets = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .ToList();

            foreach (var sheet in sheets)
            {
                viewToSheetMap[sheet.Id.GetIdValue()] = sheet;
                foreach (var vpId in sheet.GetAllViewports())
                {
                    var vp = doc.GetElement(vpId) as Viewport;
                    if (vp != null)
                        viewToSheetMap[vp.ViewId.GetIdValue()] = sheet;
                }
            }

            // 2. 找出所有 AE-圖號 元件
            var categories = new List<BuiltInCategory>
            {
                BuiltInCategory.OST_DetailComponents,
                BuiltInCategory.OST_GenericAnnotation
            };

            var allInstances = new List<FamilyInstance>();
            foreach (var cat in categories)
            {
                var instances = new FilteredElementCollector(doc)
                    .OfCategory(cat)
                    .WhereElementIsNotElementType()
                    .Where(e =>
                    {
                        var fi = e as FamilyInstance;
                        return fi != null && fi.Symbol.FamilyName.Contains("AE-圖號");
                    })
                    .Cast<FamilyInstance>();
                allInstances.AddRange(instances);
            }

            using (TransactionGroup tg = new TransactionGroup(doc, "同步詳圖元件編號"))
            {
                tg.Start();

                foreach (var instance in allInstances)
                {
                    IdType ownerViewId = instance.OwnerViewId.GetIdValue();
                    ViewSheet sheet = null;

                    if (!viewToSheetMap.TryGetValue(ownerViewId, out sheet))
                    {
                        Element ownerView = doc.GetElement(ownerViewId.ToElementId());
                        sheet = ownerView as ViewSheet;
                        if (sheet == null) continue;
                    }

                    string sheetNumber = sheet.SheetNumber;
                    string sheetName = sheet.Name;
                    FamilySymbol currentSymbol = instance.Symbol;

                    // v3.5: 只有類型名稱已匹配圖紙編號才進行同步
                    if (!currentSymbol.Name.StartsWith(sheetNumber + "-"))
                        continue;

                    string typeKey = $"{currentSymbol.FamilyName}:{currentSymbol.Name}";
                    if (processedTypes.Contains(typeKey)) continue;

                    using (Transaction t = new Transaction(doc, "同步元件類型"))
                    {
                        t.Start();

                        Parameter pTypeSheetNum = currentSymbol.LookupParameter("詳圖圖號");
                        if (pTypeSheetNum != null && !pTypeSheetNum.IsReadOnly)
                            pTypeSheetNum.Set(sheetNumber);

                        Parameter pTypeSheetName = currentSymbol.LookupParameter("圖說名稱");
                        if (pTypeSheetName != null && !pTypeSheetName.IsReadOnly)
                            pTypeSheetName.Set(sheetName);

                        t.Commit();
                        updatedInstances++;
                        processedTypes.Add(typeKey);
                    }
                }
                tg.Assimilate();
            }

            return new
            {
                Success = true,
                UpdatedInstances = updatedInstances,
                TypesCreated = typesCreated,
                Message = $"同步完成：更新了 {updatedInstances} 個標頭元件，建立了 {typesCreated} 個新類型。"
            };
        }

        /// <summary>
        /// 建立詳圖元件類型（依據圖紙編號）
        /// </summary>
        private object CreateDetailComponentType(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;

            string sheetNumber = parameters?["sheetNumber"]?.Value<string>();
            string detailName = parameters?["detailName"]?.Value<string>();
            string familyName = parameters?["familyName"]?.Value<string>();
            string detailNumber = parameters?["detailNumber"]?.Value<string>() ?? "1";

            if (string.IsNullOrEmpty(sheetNumber))
                throw new Exception("必須提供 sheetNumber 參數");
            if (string.IsNullOrEmpty(detailName))
                throw new Exception("必須提供 detailName 參數");

            var sheet = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .FirstOrDefault(s => s.SheetNumber == sheetNumber);

            if (sheet == null)
                throw new Exception($"找不到圖紙編號: {sheetNumber}");

            string sheetName = sheet.Name;

            // 尋找基礎 FamilySymbol
            FamilySymbol baseSymbol = null;
            if (!string.IsNullOrEmpty(familyName))
            {
                baseSymbol = new FilteredElementCollector(doc)
                    .OfClass(typeof(FamilySymbol))
                    .Cast<FamilySymbol>()
                    .FirstOrDefault(s => s.FamilyName != null &&
                        (s.FamilyName.Equals(familyName, StringComparison.OrdinalIgnoreCase) || s.FamilyName.Contains(familyName)));
            }
            else
            {
                baseSymbol = new FilteredElementCollector(doc)
                    .OfClass(typeof(FamilySymbol))
                    .Cast<FamilySymbol>()
                    .FirstOrDefault(s => s.FamilyName != null && s.FamilyName.Contains("AE-圖號"));
            }

            if (baseSymbol == null)
                throw new Exception($"找不到基礎詳圖項目族群: {familyName ?? "AE-圖號"}");

            string targetTypeName = $"{sheetNumber}-{sheetName}-{detailName}";

            // 檢查是否已存在
            var existingSymbol = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .FirstOrDefault(s => s.FamilyName == baseSymbol.FamilyName && s.Name == targetTypeName);

            if (existingSymbol != null)
            {
                return new
                {
                    Success = false,
                    Error = $"類型已存在: {targetTypeName}",
                    ExistingTypeId = existingSymbol.Id.GetIdValue()
                };
            }

            using (Transaction t = new Transaction(doc, "建立詳圖元件類型"))
            {
                t.Start();

                var newSymbol = baseSymbol.Duplicate(targetTypeName) as FamilySymbol;
                if (newSymbol == null)
                    throw new Exception($"複製類型失敗: {targetTypeName}");

                Parameter pTypeSheetNum = newSymbol.LookupParameter("詳圖圖號");
                if (pTypeSheetNum != null && !pTypeSheetNum.IsReadOnly)
                    pTypeSheetNum.Set(sheetNumber);

                Parameter pTypeSheetName = newSymbol.LookupParameter("圖說名稱");
                if (pTypeSheetName != null && !pTypeSheetName.IsReadOnly)
                    pTypeSheetName.Set(sheetName ?? "");

                Parameter pTypeDetailName = newSymbol.LookupParameter("詳圖名稱");
                if (pTypeDetailName != null && !pTypeDetailName.IsReadOnly)
                    pTypeDetailName.Set(detailName ?? "");

                Parameter pTypeDetailNumber = newSymbol.LookupParameter("詳圖編號");
                if (pTypeDetailNumber != null && !pTypeDetailNumber.IsReadOnly)
                    pTypeDetailNumber.Set(detailNumber);

                t.Commit();

                return new
                {
                    Success = true,
                    TypeId = newSymbol.Id.GetIdValue(),
                    TypeName = newSymbol.Name,
                    SheetNumber = sheetNumber,
                    SheetName = sheetName,
                    DetailName = detailName,
                    DetailNumber = detailNumber
                };
            }
        }

        /// <summary>
        /// 列出族群符號
        /// </summary>
        private object ListFamilySymbols(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;
            string filterName = parameters?["filter"]?.Value<string>();

            var symbols = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .Where(s => string.IsNullOrEmpty(filterName) ||
                           (s.FamilyName != null && s.FamilyName.Contains(filterName)) ||
                           (s.Name != null && s.Name.Contains(filterName)))
                .Select(s => new
                {
                    Id = s.Id.GetIdValue(),
                    Name = s.Name,
                    FamilyName = s.FamilyName ?? "<NULL>",
                    Category = s.Category?.Name ?? "<NO_CAT>"
                })
                .Take(100)
                .ToList();

            return new { Success = true, Symbols = symbols };
        }

        #endregion
    }
}
