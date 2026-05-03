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

            using (Transaction trans = new Transaction(doc, "批次建立樓層平面視圖"))
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
    }
}
