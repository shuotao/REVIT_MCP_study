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
        #region 樓層平面視圖建立

        /// <summary>
        /// 以範本 FloorPlan 視圖為基礎，在多個 Level 上批次建立新的樓層平面視圖
        /// </summary>
        private object CreateFloorPlansFromTemplate(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;

            IdType templateViewId = parameters["templateViewId"]?.Value<IdType>() ?? 0;
            if (templateViewId == 0)
                throw new Exception("必須指定 templateViewId");

            var creationsArray = parameters["creations"] as JArray;
            if (creationsArray == null || creationsArray.Count == 0)
                throw new Exception("creations 不可為空");

            bool applyViewTemplate = parameters["applyViewTemplate"]?.Value<bool>() ?? true;

            ViewPlan templateView = doc.GetElement(templateViewId.ToElementId()) as ViewPlan;
            if (templateView == null)
                throw new Exception($"ElementId {templateViewId} 不是 ViewPlan 或不存在");
            if (templateView.ViewType != ViewType.FloorPlan)
                throw new Exception($"範本視圖必須是 FloorPlan，目前是 {templateView.ViewType}");

            ElementId viewFamilyTypeId = templateView.GetTypeId();
            ElementId sourceTemplateId = templateView.ViewTemplateId;

            ElementId sourcePhaseId = templateView.get_Parameter(BuiltInParameter.VIEW_PHASE)?.AsElementId();
            ElementId sourcePhaseFilterId = templateView.get_Parameter(BuiltInParameter.VIEW_PHASE_FILTER)?.AsElementId();

            var allLevels = new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .GroupBy(l => l.Name)
                .ToDictionary(g => g.Key, g => g.First());

            var existingViewNames = new HashSet<string>(
                new FilteredElementCollector(doc)
                    .OfClass(typeof(View))
                    .Cast<View>()
                    .Where(v => !v.IsTemplate)
                    .Select(v => v.Name));

            var created = new List<object>();
            var skipped = new List<object>();

            using (Transaction trans = TransactionHelper.Begin(doc, "批次建立樓層平面視圖"))
            {
                trans.Start();

                foreach (JObject item in creationsArray.OfType<JObject>())
                {
                    string levelName = item["levelName"]?.Value<string>();
                    string newName = item["newName"]?.Value<string>();

                    if (string.IsNullOrEmpty(levelName) || string.IsNullOrEmpty(newName))
                    {
                        skipped.Add(new { LevelName = levelName, NewName = newName, Reason = "levelName 或 newName 為空" });
                        continue;
                    }

                    if (!allLevels.TryGetValue(levelName, out Level level))
                    {
                        skipped.Add(new { LevelName = levelName, NewName = newName, Reason = $"找不到 Level: {levelName}" });
                        continue;
                    }

                    if (existingViewNames.Contains(newName))
                    {
                        skipped.Add(new { LevelName = levelName, NewName = newName, Reason = $"view 名稱已存在: {newName}" });
                        continue;
                    }

                    try
                    {
                        ViewPlan newView = ViewPlan.Create(doc, viewFamilyTypeId, level.Id);
                        newView.Name = newName;

                        if (applyViewTemplate && sourceTemplateId != ElementId.InvalidElementId)
                        {
                            newView.ViewTemplateId = sourceTemplateId;
                        }

                        if (sourcePhaseId != null && sourcePhaseId != ElementId.InvalidElementId)
                        {
                            var p = newView.get_Parameter(BuiltInParameter.VIEW_PHASE);
                            if (p != null && !p.IsReadOnly) p.Set(sourcePhaseId);
                        }
                        if (sourcePhaseFilterId != null && sourcePhaseFilterId != ElementId.InvalidElementId)
                        {
                            var p = newView.get_Parameter(BuiltInParameter.VIEW_PHASE_FILTER);
                            if (p != null && !p.IsReadOnly) p.Set(sourcePhaseFilterId);
                        }

                        existingViewNames.Add(newName);

                        created.Add(new
                        {
                            ElementId = newView.Id.GetIdValue(),
                            Name = newView.Name,
                            LevelName = level.Name,
                            LevelId = level.Id.GetIdValue(),
                            AppliedViewTemplate = applyViewTemplate && sourceTemplateId != ElementId.InvalidElementId
                        });
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"建立 FloorPlan 失敗 [{levelName}/{newName}]: {ex.Message}");
                        skipped.Add(new { LevelName = levelName, NewName = newName, Reason = ex.Message });
                    }
                }

                trans.Commit();
            }

            return new
            {
                TemplateViewId = templateView.Id.GetIdValue(),
                TemplateViewName = templateView.Name,
                CreatedCount = created.Count,
                SkippedCount = skipped.Count,
                Created = created,
                Skipped = skipped
            };
        }

        #endregion

        #region 批次套用 ViewTemplate

        private object BatchApplyViewTemplate(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;

            bool dryRun = parameters["dryRun"]?.Value<bool>() ?? false;
            bool skipIfSameTemplate = parameters["skipIfSameTemplate"]?.Value<bool>() ?? true;

            View targetTemplate = ResolveViewTemplate(doc, parameters);
            List<View> candidateViews = ResolveTargetViewsForTemplate(doc, parameters);

            if (candidateViews.Count == 0)
            {
                return new
                {
                    TemplateName = targetTemplate.Name,
                    TemplateId = targetTemplate.Id.GetIdValue(),
                    DryRun = dryRun,
                    ModifiedCount = 0,
                    SkippedCount = 0,
                    FailedCount = 0,
                    Message = "沒有找到任何符合條件的 view",
                    Modified = new List<object>(),
                    Skipped = new List<object>(),
                    Failed = new List<object>()
                };
            }

            var modified = new List<object>();
            var skipped = new List<object>();
            var failed = new List<object>();

            Action<View> processView = (view) =>
            {
                if (view.IsTemplate)
                {
                    skipped.Add(new { ViewName = view.Name, ViewId = view.Id.GetIdValue(), Reason = "是 ViewTemplate，不可套用" });
                    return;
                }

                string previousTemplateName = "<None>";
                if (view.ViewTemplateId != ElementId.InvalidElementId)
                {
                    var prevTemplate = doc.GetElement(view.ViewTemplateId) as View;
                    previousTemplateName = prevTemplate?.Name ?? "Unknown";
                }

                if (skipIfSameTemplate && view.ViewTemplateId == targetTemplate.Id)
                {
                    skipped.Add(new
                    {
                        ViewName = view.Name,
                        ViewId = view.Id.GetIdValue(),
                        ViewType = view.ViewType.ToString(),
                        PreviousTemplate = previousTemplateName,
                        Reason = "已使用相同 template"
                    });
                    return;
                }

                if (!dryRun)
                    view.ViewTemplateId = targetTemplate.Id;

                modified.Add(new
                {
                    ViewName = view.Name,
                    ViewId = view.Id.GetIdValue(),
                    ViewType = view.ViewType.ToString(),
                    PreviousTemplate = previousTemplateName,
                    NewTemplate = targetTemplate.Name
                });
            };

            if (dryRun)
            {
                foreach (var view in candidateViews)
                {
                    try { processView(view); }
                    catch (Exception ex) { failed.Add(new { ViewName = view.Name, ViewId = view.Id.GetIdValue(), Error = ex.Message }); }
                }
            }
            else
            {
                using (Transaction trans = TransactionHelper.Begin(doc, "批次套用 ViewTemplate"))
                {
                    trans.Start();
                    foreach (var view in candidateViews)
                    {
                        try { processView(view); }
                        catch (Exception ex) { failed.Add(new { ViewName = view.Name, ViewId = view.Id.GetIdValue(), Error = ex.Message }); }
                    }
                    trans.Commit();
                }
            }

            return new
            {
                TemplateName = targetTemplate.Name,
                TemplateId = targetTemplate.Id.GetIdValue(),
                DryRun = dryRun,
                ModifiedCount = modified.Count,
                SkippedCount = skipped.Count,
                FailedCount = failed.Count,
                Modified = modified,
                Skipped = skipped,
                Failed = failed
            };
        }

        private View ResolveViewTemplate(Document doc, JObject parameters)
        {
            IdType templateId = parameters["viewTemplateId"]?.Value<IdType>() ?? 0;
            string templateName = parameters["viewTemplateName"]?.Value<string>();

            if (templateId != 0)
            {
                var element = doc.GetElement(templateId.ToElementId()) as View;
                if (element == null || !element.IsTemplate)
                    throw new Exception($"ElementId {templateId} 不是 ViewTemplate 或不存在");
                return element;
            }

            if (!string.IsNullOrEmpty(templateName))
            {
                var allTemplates = new FilteredElementCollector(doc)
                    .OfClass(typeof(View))
                    .Cast<View>()
                    .Where(v => v.IsTemplate)
                    .ToList();

                var match = allTemplates.FirstOrDefault(t => t.Name == templateName);
                if (match == null)
                {
                    var available = allTemplates.Select(t => $"{t.Name} (Id={t.Id.GetIdValue()})").ToList();
                    throw new Exception($"找不到 ViewTemplate: '{templateName}'. 可用的 ViewTemplate: [{string.Join(", ", available.Take(20))}]");
                }
                return match;
            }

            throw new Exception("必須指定 viewTemplateId 或 viewTemplateName");
        }

        private List<View> ResolveTargetViewsForTemplate(Document doc, JObject parameters)
        {
            var viewIds = parameters["viewIds"]?.ToObject<List<IdType>>();
            var viewNames = parameters["viewNames"]?.ToObject<List<string>>();
            string viewNameContains = parameters["viewNameContains"]?.Value<string>();
            var sheetIds = parameters["sheetIds"]?.ToObject<List<IdType>>();
            var sheetNumbers = parameters["sheetNumbers"]?.ToObject<List<string>>();
            var viewTypeFilter = parameters["viewTypeFilter"]?.ToObject<List<string>>();

            var viewSet = new HashSet<ElementId>();
            var views = new List<View>();
            bool anySelector = false;

            if (sheetIds != null && sheetIds.Count > 0)
            {
                anySelector = true;
                foreach (var sid in sheetIds)
                {
                    var sheet = doc.GetElement(sid.ToElementId()) as ViewSheet;
                    if (sheet == null) continue;
                    foreach (var vpId in sheet.GetAllViewports())
                    {
                        var vp = doc.GetElement(vpId) as Viewport;
                        if (vp == null) continue;
                        var view = doc.GetElement(vp.ViewId) as View;
                        if (view != null && viewSet.Add(view.Id))
                            views.Add(view);
                    }
                }
            }

            if (sheetNumbers != null && sheetNumbers.Count > 0)
            {
                anySelector = true;
                var numberSet = new HashSet<string>(sheetNumbers, StringComparer.OrdinalIgnoreCase);
                var sheets = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSheet))
                    .Cast<ViewSheet>()
                    .Where(s => numberSet.Contains(s.SheetNumber))
                    .ToList();
                foreach (var sheet in sheets)
                {
                    foreach (var vpId in sheet.GetAllViewports())
                    {
                        var vp = doc.GetElement(vpId) as Viewport;
                        if (vp == null) continue;
                        var view = doc.GetElement(vp.ViewId) as View;
                        if (view != null && viewSet.Add(view.Id))
                            views.Add(view);
                    }
                }
            }

            if (viewIds != null && viewIds.Count > 0)
            {
                anySelector = true;
                foreach (var vid in viewIds)
                {
                    var view = doc.GetElement(vid.ToElementId()) as View;
                    if (view != null && viewSet.Add(view.Id))
                        views.Add(view);
                }
            }

            if (viewNames != null && viewNames.Count > 0)
            {
                anySelector = true;
                var nameSet = new HashSet<string>(viewNames);
                var matched = new FilteredElementCollector(doc)
                    .OfClass(typeof(View))
                    .Cast<View>()
                    .Where(v => !v.IsTemplate && nameSet.Contains(v.Name))
                    .ToList();
                foreach (var v in matched)
                    if (viewSet.Add(v.Id)) views.Add(v);
            }

            if (!string.IsNullOrEmpty(viewNameContains))
            {
                anySelector = true;
                var matched = new FilteredElementCollector(doc)
                    .OfClass(typeof(View))
                    .Cast<View>()
                    .Where(v => !v.IsTemplate
                        && v.Name.IndexOf(viewNameContains, StringComparison.OrdinalIgnoreCase) >= 0)
                    .ToList();
                foreach (var v in matched)
                    if (viewSet.Add(v.Id)) views.Add(v);
            }

            if (!anySelector)
                throw new Exception("必須指定 sheetIds / sheetNumbers / viewIds / viewNames / viewNameContains 其中至少一個");

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
