using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;

#if REVIT2025_OR_GREATER
using IdType = System.Int64;
#else
using IdType = System.Int32;
#endif

namespace RevitMCP.Core
{
    /// <summary>
    /// 視圖元素隱藏/顯示與類別可見性控制（劉可 PR#30 bundle⑥，view-category-visibility skill）。
    ///
    /// 從 snapshot 的 CommandExecutor.cs inline 段落抽出為獨立 partial：
    ///   - hide_elements / unhide_elements：依 ElementId 精確控制主模型元素
    ///   - set_category_visibility：整類別控制（含連結模型）
    ///
    /// 依賴 main 既有的 ResolveCategoryId(doc, name) 與本檔內附的 ParseIdArray helper。
    /// 跨版本調整：snapshot 原用 cat.Id.IntegerValue（R26 不存在），改為 RevitCompatibility.GetIdValue()。
    /// </summary>
    public partial class CommandExecutor
    {
        private object HideElements(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;
            IdType? singleElementId = parameters["elementId"]?.Value<IdType>();
            IdType? viewId = parameters["viewId"]?.Value<IdType>();

            View view;
            if (viewId.HasValue)
            {
                view = doc.GetElement(new ElementId(viewId.Value)) as View;
                if (view == null)
                    throw new Exception($"找不到視圖 ID: {viewId}");
            }
            else
            {
                view = _uiApp.ActiveUIDocument.ActiveView;
            }

            List<IdType> elementIds = new List<IdType>();
            if (singleElementId.HasValue)
                elementIds.Add(singleElementId.Value);
            elementIds.AddRange(ParseIdArray(parameters["elementIds"]));

            if (elementIds.Count == 0)
                throw new Exception("請提供至少一個元素 ID");

            var validIds = new List<ElementId>();
            foreach (var elemId in elementIds)
            {
                Element element = doc.GetElement(new ElementId(elemId));
                if (element != null && element.CanBeHidden(view))
                    validIds.Add(new ElementId(elemId));
            }

            if (validIds.Count == 0)
                throw new Exception("沒有可隱藏的元素");

            using (Transaction trans = TransactionHelper.Begin(doc, "Hide Elements"))
            {
                trans.Start();
                view.HideElements(validIds);
                trans.Commit();

                return new
                {
                    Success = true,
                    HiddenCount = validIds.Count,
                    RequestedCount = elementIds.Count,
                    ViewId = view.Id.GetIdValue(),
                    ViewName = view.Name,
                    Message = $"已在視圖 '{view.Name}' 中隱藏 {validIds.Count} 個元素"
                };
            }
        }

        private object UnhideElements(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;
            IdType? singleElementId = parameters["elementId"]?.Value<IdType>();
            IdType? viewId = parameters["viewId"]?.Value<IdType>();

            View view;
            if (viewId.HasValue)
            {
                view = doc.GetElement(new ElementId(viewId.Value)) as View;
                if (view == null)
                    throw new Exception($"找不到視圖 ID: {viewId}");
            }
            else
            {
                view = _uiApp.ActiveUIDocument.ActiveView;
            }

            List<IdType> elementIds = new List<IdType>();
            if (singleElementId.HasValue)
                elementIds.Add(singleElementId.Value);
            elementIds.AddRange(ParseIdArray(parameters["elementIds"]));

            if (elementIds.Count == 0)
                throw new Exception("請提供至少一個元素 ID");

            var validIds = new List<ElementId>();
            foreach (var elemId in elementIds)
            {
                Element element = doc.GetElement(new ElementId(elemId));
                if (element != null)
                    validIds.Add(new ElementId(elemId));
            }

            if (validIds.Count == 0)
                throw new Exception("找不到任何有效元素");

            using (Transaction trans = TransactionHelper.Begin(doc, "Unhide Elements"))
            {
                trans.Start();
                view.UnhideElements(validIds);
                trans.Commit();

                return new
                {
                    Success = true,
                    UnhiddenCount = validIds.Count,
                    RequestedCount = elementIds.Count,
                    ViewId = view.Id.GetIdValue(),
                    ViewName = view.Name,
                    Message = $"已在視圖 '{view.Name}' 中取消隱藏 {validIds.Count} 個元素"
                };
            }
        }

        private object SetCategoryVisibility(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;
            string categoryName = parameters["category"]?.Value<string>();
            bool hidden = parameters["hidden"]?.Value<bool>() ?? true;
            IdType? viewId = parameters["viewId"]?.Value<IdType>();

            if (string.IsNullOrEmpty(categoryName))
                throw new Exception("請提供類別名稱 (category)");

            View view;
            if (viewId.HasValue)
            {
                view = doc.GetElement(new ElementId(viewId.Value)) as View;
                if (view == null)
                    throw new Exception($"找不到視圖 ID: {viewId}");
            }
            else
            {
                view = _uiApp.ActiveUIDocument.ActiveView;
            }

            // 使用現有的 ResolveCategoryId 方法解析類別
            ElementId categoryId = ResolveCategoryId(doc, categoryName);
            if (categoryId == ElementId.InvalidElementId)
                throw new Exception($"找不到類別: {categoryName}");

            Category category = null;
            foreach (Category cat in doc.Settings.Categories)
            {
                // 跨版本：用 GetIdValue() 取代 R26 已移除的 .IntegerValue
                if (cat.Id.GetIdValue() == categoryId.GetIdValue())
                {
                    category = cat;
                    break;
                }
            }
            string resolvedName = category?.Name ?? categoryName;

            using (Transaction trans = TransactionHelper.Begin(doc, hidden ? "Hide Category" : "Show Category"))
            {
                trans.Start();
                view.SetCategoryHidden(categoryId, hidden);
                trans.Commit();

                return new
                {
                    Success = true,
                    Category = resolvedName,
                    Hidden = hidden,
                    ViewId = view.Id.GetIdValue(),
                    ViewName = view.Name,
                    Message = $"已在視圖 '{view.Name}' 中{(hidden ? "隱藏" : "顯示")}類別 '{resolvedName}'"
                };
            }
        }

        /// <summary>
        /// 健壯解析 ID 陣列，支援 JArray、JSON 字串、逗號分隔字串
        /// </summary>
        private List<IdType> ParseIdArray(JToken token)
        {
            var result = new List<IdType>();
            if (token == null) return result;

            // 已經是 JArray
            if (token is JArray arr)
            {
                result.AddRange(arr.Select(id => id.Value<IdType>()));
                return result;
            }

            // 可能是 JSON 字串 "[1,2,3]"
            var str = token.ToString().Trim();
            if (str.StartsWith("["))
            {
                try
                {
                    var parsed = JArray.Parse(str);
                    result.AddRange(parsed.Select(id => id.Value<IdType>()));
                    return result;
                }
                catch { }
            }

            // 嘗試逗號分隔
            foreach (var part in str.Split(','))
            {
                if (IdType.TryParse(part.Trim(), out IdType id))
                    result.Add(id);
            }
            return result;
        }
    }
}
