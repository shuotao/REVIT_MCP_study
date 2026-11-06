using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;

// Revit 2025+ ElementId: int → long
#if REVIT2025_OR_GREATER
using IdType = System.Int64;
#else
using IdType = System.Int32;
#endif

namespace RevitMCP.Core
{
    /// <summary>
    /// 平面視圖的視圖範圍（View Range）讀寫。
    ///
    /// 存在理由：「元素明明在模型裡、類別也開著、卻在平面圖看不到」最常見的成因就是
    /// View Depth 沒涵蓋到該元素（典型案例：地形／基礎在樓板下方）。
    /// 先前沒有任何工具能讀或改這組設定，只能請使用者手動開對話框。
    /// </summary>
    public partial class CommandExecutor
    {
        /// <summary>View Range 的四個面，對應 Revit 對話框由上而下的順序。</summary>
        private static readonly Dictionary<string, PlanViewPlane> PlanViewPlaneMap =
            new Dictionary<string, PlanViewPlane>(StringComparer.OrdinalIgnoreCase)
            {
                { "topClip", PlanViewPlane.TopClipPlane },
                { "cutPlane", PlanViewPlane.CutPlane },
                { "bottomClip", PlanViewPlane.BottomClipPlane },
                { "viewDepth", PlanViewPlane.ViewDepthPlane },
            };

        private object GetViewRange(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;
            ViewPlan viewPlan = ResolveViewPlan(doc, parameters);
            PlanViewRange range = viewPlan.GetViewRange();

            var planes = PlanViewPlaneMap.Select(kv =>
            {
                ElementId levelId = range.GetLevelId(kv.Value);
                double offsetFeet = range.GetOffset(kv.Value);
                return (object)new
                {
                    Plane = kv.Key,
                    Level = DescribePlanViewLevel(doc, levelId),
                    LevelId = levelId.GetIdValue(),
                    OffsetMm = Math.Round(UnitUtils.ConvertFromInternalUnits(offsetFeet, UnitTypeId.Millimeters), 1)
                };
            }).ToList();

            return new
            {
                Success = true,
                ViewId = viewPlan.Id.GetIdValue(),
                ViewName = viewPlan.Name,
                ViewType = viewPlan.ViewType.ToString(),
                AssociatedLevel = viewPlan.GenLevel != null ? viewPlan.GenLevel.Name : null,
                ViewTemplateId = viewPlan.ViewTemplateId.GetIdValue(),
                ViewTemplateControlsViewRange = ViewTemplateLocksViewRange(doc, viewPlan),
                Note = "OffsetMm 是相對於該面所指樓層的偏移；Unlimited 代表無限延伸（View Depth 常用）。",
                Planes = planes
            };
        }

        private object SetViewRange(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;
            ViewPlan viewPlan = ResolveViewPlan(doc, parameters);

            if (ViewTemplateLocksViewRange(doc, viewPlan))
            {
                var tpl = doc.GetElement(viewPlan.ViewTemplateId) as View;
                throw new Exception(
                    $"視圖 '{viewPlan.Name}' 的 View Range 由視圖樣板 '{(tpl != null ? tpl.Name : "?")}' 控制，" +
                    "在視圖上改不動。請改樣板本身，或先解除樣板指派。");
            }

            // 先收集要改的面，全部解析成功才開 Transaction，避免改到一半失敗留下半套設定
            var updates = new List<Tuple<PlanViewPlane, string, ElementId, double?>>();
            foreach (var kv in PlanViewPlaneMap)
            {
                var token = parameters[kv.Key] as JObject;
                if (token == null) continue;

                string levelToken = token["level"]?.Value<string>();
                ElementId levelId = string.IsNullOrWhiteSpace(levelToken)
                    ? null
                    : ParsePlanViewLevel(doc, levelToken);

                double? offsetFeet = null;
                if (token["offsetMm"] != null && token["offsetMm"].Type != JTokenType.Null)
                {
                    offsetFeet = UnitUtils.ConvertToInternalUnits(
                        token["offsetMm"].Value<double>(), UnitTypeId.Millimeters);
                }

                if (levelId == null && !offsetFeet.HasValue) continue;
                updates.Add(Tuple.Create(kv.Value, kv.Key, levelId, offsetFeet));
            }

            if (updates.Count == 0)
            {
                throw new Exception(
                    "沒有指定任何要修改的面。可用的面：topClip / cutPlane / bottomClip / viewDepth，" +
                    "每個面給 { level, offsetMm }，例如 viewDepth: { level: \"Unlimited\" }。");
            }

            PlanViewRange range = viewPlan.GetViewRange();
            var applied = new List<object>();

            using (var tx = new Transaction(doc, "設定視圖範圍"))
            {
                tx.Start();

                foreach (var u in updates)
                {
                    PlanViewPlane plane = u.Item1;
                    if (u.Item3 != null) range.SetLevelId(plane, u.Item3);
                    if (u.Item4.HasValue) range.SetOffset(plane, u.Item4.Value);
                }

                viewPlan.SetViewRange(range);
                tx.Commit();
            }

            // 回讀實際落地的值當作驗收證據，而不是回報「我送出的值」
            PlanViewRange after = viewPlan.GetViewRange();
            foreach (var u in updates)
            {
                ElementId levelId = after.GetLevelId(u.Item1);
                applied.Add(new
                {
                    Plane = u.Item2,
                    Level = DescribePlanViewLevel(doc, levelId),
                    OffsetMm = Math.Round(
                        UnitUtils.ConvertFromInternalUnits(after.GetOffset(u.Item1), UnitTypeId.Millimeters), 1)
                });
            }

            return new
            {
                Success = true,
                ViewId = viewPlan.Id.GetIdValue(),
                ViewName = viewPlan.Name,
                UpdatedPlaneCount = applied.Count,
                Applied = applied,
                Message = $"已更新視圖 '{viewPlan.Name}' 的 {applied.Count} 個視圖範圍平面"
            };
        }

        // ── helpers ────────────────────────────────────────────────────────

        private ViewPlan ResolveViewPlan(Document doc, JObject parameters)
        {
            View view;
            IdType viewId = parameters["viewId"]?.Value<IdType>() ?? 0;

            if (viewId > 0)
            {
                view = doc.GetElement(new ElementId(viewId)) as View;
                if (view == null) throw new Exception($"找不到視圖 ID: {viewId}");
            }
            else
            {
                view = _uiApp.ActiveUIDocument.ActiveView;
            }

            var viewPlan = view as ViewPlan;
            if (viewPlan == null)
            {
                throw new Exception(
                    $"'{view.Name}' 是 {view.ViewType}，不是平面類視圖。" +
                    "只有 FloorPlan / CeilingPlan / AreaPlan / EngineeringPlan 有 View Range。");
            }

            return viewPlan;
        }

        /// <summary>視圖樣板是否把 View Range 鎖住（鎖住時在視圖上改了也不會生效）。</summary>
        private bool ViewTemplateLocksViewRange(Document doc, View view)
        {
            if (view.ViewTemplateId == null || view.ViewTemplateId == ElementId.InvalidElementId) return false;

            var template = doc.GetElement(view.ViewTemplateId) as View;
            if (template == null) return false;

            var nonControlled = template.GetNonControlledTemplateParameterIds();
            var viewRangeParam = new ElementId(BuiltInParameter.PLAN_VIEW_RANGE);
            return !nonControlled.Any(id => id == viewRangeParam);
        }

        private string DescribePlanViewLevel(Document doc, ElementId levelId)
        {
            if (levelId == PlanViewRange.Unlimited) return "Unlimited";
            if (levelId == PlanViewRange.LevelAbove) return "Level Above";
            if (levelId == PlanViewRange.LevelBelow) return "Level Below";
            if (levelId == PlanViewRange.Current) return "Associated Level";

            var level = doc.GetElement(levelId) as Level;
            return level != null ? level.Name : $"(ElementId {levelId.GetIdValue()})";
        }

        private ElementId ParsePlanViewLevel(Document doc, string token)
        {
            string key = token.Trim().Replace(" ", "").Replace("_", "").ToLowerInvariant();
            switch (key)
            {
                case "unlimited":
                    return PlanViewRange.Unlimited;
                case "levelabove":
                case "above":
                    return PlanViewRange.LevelAbove;
                case "levelbelow":
                case "below":
                    return PlanViewRange.LevelBelow;
                case "current":
                case "associatedlevel":
                case "associated":
                    return PlanViewRange.Current;
            }

            // 其餘一律當成樓層名稱（找不到會丟出含樓層清單的例外）
            return FindLevel(doc, token, false).Id;
        }
    }
}
