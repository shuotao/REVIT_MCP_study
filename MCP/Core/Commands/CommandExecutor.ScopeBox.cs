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
    public partial class CommandExecutor
    {
        #region ScopeBox 操作

        /// <summary>
        /// 批次將 ScopeBox 套用到一組 views（透過設定 view 的 VIEWER_VOLUME_OF_INTEREST_CROP 參數）
        /// </summary>
        private object SetScopeBoxForViews(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;

            string scopeBoxName = parameters["scopeBoxName"]?.Value<string>();
            if (string.IsNullOrEmpty(scopeBoxName))
                throw new Exception("必須指定 scopeBoxName");

            // 1. 找 ScopeBox
            var allScopeBoxes = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_VolumeOfInterest)
                .WhereElementIsNotElementType()
                .ToList();

            var matchingScopeBoxes = allScopeBoxes
                .Where(e => e.Name == scopeBoxName)
                .ToList();

            if (matchingScopeBoxes.Count == 0)
            {
                var available = allScopeBoxes.Select(e => e.Name).ToList();
                throw new Exception($"找不到 ScopeBox: '{scopeBoxName}'. 目標檔可用的 ScopeBox: [{string.Join(", ", available)}]");
            }

            var warnings = new List<string>();
            if (matchingScopeBoxes.Count > 1)
                warnings.Add($"找到 {matchingScopeBoxes.Count} 個同名 ScopeBox '{scopeBoxName}', 使用第一個 (Id={matchingScopeBoxes[0].Id.GetIdValue()})");

            var scopeBox = matchingScopeBoxes[0];

            // 2. 解析目標 views
            var targetViews = ResolveTargetViewsForScopeBox(doc, parameters);
            if (targetViews.Count == 0)
                throw new Exception("找不到任何符合的 view");

            var applied = new List<object>();
            var skipped = new List<object>();
            var failed = new List<object>();

            // 3. 批次套用（單一 transaction）
            using (Transaction trans = TransactionHelper.Begin(doc, $"批次設定 ScopeBox: {scopeBoxName}"))
            {
                trans.Start();
                foreach (var view in targetViews)
                {
                    try
                    {
                        if (view.IsTemplate)
                        {
                            skipped.Add(new { ViewName = view.Name, Reason = "view template, 跳過" });
                            continue;
                        }

                        var param = view.get_Parameter(BuiltInParameter.VIEWER_VOLUME_OF_INTEREST_CROP);
                        if (param == null)
                        {
                            skipped.Add(new { ViewName = view.Name, ViewType = view.ViewType.ToString(), Reason = "view 不支援 ScopeBox 參數" });
                            continue;
                        }
                        if (param.IsReadOnly)
                        {
                            skipped.Add(new { ViewName = view.Name, ViewType = view.ViewType.ToString(), Reason = "ScopeBox 參數為 read-only" });
                            continue;
                        }

                        param.Set(scopeBox.Id);
                        applied.Add(new
                        {
                            ViewId = view.Id.GetIdValue(),
                            ViewName = view.Name,
                            ViewType = view.ViewType.ToString()
                        });
                    }
                    catch (Exception ex)
                    {
                        failed.Add(new { ViewName = view.Name, Error = ex.Message });
                    }
                }
                trans.Commit();
            }

            return new
            {
                ScopeBoxName = scopeBoxName,
                ScopeBoxId = scopeBox.Id.GetIdValue(),
                AppliedCount = applied.Count,
                SkippedCount = skipped.Count,
                FailedCount = failed.Count,
                Applied = applied,
                Skipped = skipped,
                Failed = failed,
                Warnings = warnings
            };
        }

        /// <summary>
        /// Helper: 三選一解析目標 views (viewIds > viewNames > viewNameContains)
        /// </summary>
        private List<View> ResolveTargetViewsForScopeBox(Document doc, JObject parameters)
        {
            var viewIds = parameters["viewIds"]?.ToObject<List<IdType>>();
            var viewNames = parameters["viewNames"]?.ToObject<List<string>>();
            string viewNameContains = parameters["viewNameContains"]?.Value<string>();
            var viewTypeFilter = parameters["viewTypeFilter"]?.ToObject<List<string>>();

            List<View> views;

            if (viewIds != null && viewIds.Count > 0)
            {
                views = viewIds
                    .Select(id => doc.GetElement(id.ToElementId()) as View)
                    .Where(v => v != null)
                    .ToList();
            }
            else if (viewNames != null && viewNames.Count > 0)
            {
                var nameSet = new HashSet<string>(viewNames);
                views = new FilteredElementCollector(doc)
                    .OfClass(typeof(View))
                    .Cast<View>()
                    .Where(v => !v.IsTemplate && nameSet.Contains(v.Name))
                    .ToList();
            }
            else if (!string.IsNullOrEmpty(viewNameContains))
            {
                views = new FilteredElementCollector(doc)
                    .OfClass(typeof(View))
                    .Cast<View>()
                    .Where(v => !v.IsTemplate && v.Name.IndexOf(viewNameContains, StringComparison.OrdinalIgnoreCase) >= 0)
                    .ToList();
            }
            else
            {
                throw new Exception("必須指定 viewIds / viewNames / viewNameContains 其中一個");
            }

            // Optional viewType filter
            if (viewTypeFilter != null && viewTypeFilter.Count > 0)
            {
                var typeSet = new HashSet<string>(viewTypeFilter, StringComparer.OrdinalIgnoreCase);
                views = views.Where(v => typeSet.Contains(v.ViewType.ToString())).ToList();
            }

            return views;
        }

        #endregion
    }
}
