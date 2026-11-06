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
    /// 視圖樣版「控制參數」（View Template 對話框裡每一列的 Include 勾選框）讀寫。
    ///
    /// 動機：使用者常常看不到視圖篩選器的顏色，原因是 View Template 正在控制
    /// 「V/G Overrides Filters」，使用者在 view 上自己改沒有用，得去 View Template
    /// 對話框把該項目的 Include 勾掉。這件事目前只能手動做，本檔補上讓 AI 能列出／
    /// 切換這些控制項的工具。
    ///
    /// API：View.GetTemplateParameterIds()（模板可控的全部參數）、
    /// View.GetNonControlledTemplateParameterIds() / SetNonControlledTemplateParameterIds()
    /// （目前「未被控制」＝ Include 勾掉的子集合）。
    ///
    /// 重用既有 helper：GetParameterDisplayName(doc, paramId)（定義於
    /// CommandExecutor.ViewFilter.cs，partial class 內可直接呼叫）。
    /// </summary>
    public partial class CommandExecutor
    {
        #region set_view_template_controls

        private object SetViewTemplateControls(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;

            IdType? templateId = parameters["templateId"]?.Value<IdType>();
            string templateName = parameters["templateName"]?.Value<string>();
            bool dryRun = parameters["dryRun"]?.Value<bool>() ?? false;

            if (!templateId.HasValue && string.IsNullOrWhiteSpace(templateName))
                throw new Exception("請提供 templateId 或 templateName 其中之一，用來指定目標視圖樣版。");

            List<string> uncontrolNames = ExtractNameList(parameters["uncontrol"] as JArray);
            List<string> controlNames = ExtractNameList(parameters["control"] as JArray);

            // 同一個名稱不能同時要求「開始控制」與「停止控制」
            List<string> overlap = uncontrolNames
                .Intersect(controlNames, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (overlap.Count > 0)
                throw new Exception($"參數名稱同時出現在 control 與 uncontrol，請擇一：{string.Join(", ", overlap)}");

            View template = ResolveViewTemplate(doc, templateId, templateName);

            // 模板可控的全部參數，建立名稱索引（不分大小寫）
            ICollection<ElementId> allParamIds = template.GetTemplateParameterIds();
            var nameIndex = new Dictionary<string, List<ElementId>>(StringComparer.OrdinalIgnoreCase);
            var allDisplayNames = new List<string>();
            foreach (ElementId pid in allParamIds)
            {
                string name = GetParameterDisplayName(doc, pid);
                allDisplayNames.Add(name);
                if (!nameIndex.TryGetValue(name, out List<ElementId> ids))
                {
                    ids = new List<ElementId>();
                    nameIndex[name] = ids;
                }
                ids.Add(pid);
            }

            var warnings = new List<string>();
            List<ElementId> uncontrolIds = ResolveTemplateParamIdsByNames(
                uncontrolNames, nameIndex, allDisplayNames, warnings, template.Name);
            List<ElementId> controlIds = ResolveTemplateParamIdsByNames(
                controlNames, nameIndex, allDisplayNames, warnings, template.Name);

            HashSet<ElementId> currentNonControlled = template.GetNonControlledTemplateParameterIds().ToHashSet();
            var newNonControlled = new HashSet<ElementId>(currentNonControlled);

            var changed = new List<string>();
            foreach (ElementId pid in uncontrolIds)
            {
                if (newNonControlled.Add(pid))
                    changed.Add($"{GetParameterDisplayName(doc, pid)}（停止控制）");
            }
            foreach (ElementId pid in controlIds)
            {
                if (newNonControlled.Remove(pid))
                    changed.Add($"{GetParameterDisplayName(doc, pid)}（開始控制）");
            }

            bool hasRequestedChange = uncontrolIds.Count > 0 || controlIds.Count > 0;
            bool committed = hasRequestedChange && !dryRun;

            if (committed)
            {
                using (Transaction trans = TransactionHelper.Begin(doc, "設定視圖樣版控制參數"))
                {
                    trans.Start();
                    template.SetNonControlledTemplateParameterIds(newNonControlled.ToList());
                    trans.Commit();
                }
            }

            // 已實際提交時重讀模型確保回傳與模型一致；dryRun 或純列表模式則直接用計算結果
            HashSet<ElementId> finalNonControlled = committed
                ? template.GetNonControlledTemplateParameterIds().ToHashSet()
                : newNonControlled;

            List<string> controlledNames = allParamIds
                .Where(pid => !finalNonControlled.Contains(pid))
                .Select(pid => GetParameterDisplayName(doc, pid))
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();
            List<string> nonControlledNames = allParamIds
                .Where(pid => finalNonControlled.Contains(pid))
                .Select(pid => GetParameterDisplayName(doc, pid))
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();

            int viewsUsingTemplate = new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Count(v => !v.IsTemplate && v.ViewTemplateId != null && v.ViewTemplateId == template.Id);

            return new
            {
                TemplateId = template.Id.GetIdValue(),
                TemplateName = template.Name,
                Controlled = controlledNames,
                NonControlled = nonControlledNames,
                Changed = changed,
                DryRun = dryRun,
                ViewsUsingTemplate = viewsUsingTemplate,
                Warnings = warnings
            };
        }

        /// <summary>把 JArray 轉成去除空白／空字串的字串清單；null 回傳空清單。</summary>
        private List<string> ExtractNameList(JArray token)
        {
            if (token == null) return new List<string>();
            return token
                .Select(t => t.Value<string>()?.Trim())
                .Where(s => !string.IsNullOrEmpty(s))
                .ToList();
        }

        /// <summary>依 templateId（優先）或 templateName（精確比對）解析目標視圖樣版。</summary>
        private View ResolveViewTemplate(Document doc, IdType? templateId, string templateName)
        {
            if (templateId.HasValue)
            {
                var elem = doc.GetElement(new ElementId(templateId.Value)) as View;
                if (elem == null || !elem.IsTemplate)
                    throw new Exception($"找不到 ID 為 {templateId.Value} 的視圖樣版（templateId 必須是 IsTemplate=true 的 View）。");
                return elem;
            }

            List<View> allTemplates = new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(v => v.IsTemplate)
                .ToList();

            List<View> matches = allTemplates.Where(v => v.Name == templateName).ToList();

            if (matches.Count == 0)
            {
                List<string> close = allTemplates
                    .Select(v => v.Name)
                    .Where(n => n.IndexOf(templateName, StringComparison.OrdinalIgnoreCase) >= 0)
                    .Distinct()
                    .Take(10)
                    .ToList();
                string hint = close.Count > 0
                    ? $"你是不是要找：{string.Join(", ", close)}？"
                    : "可呼叫 get_view_templates 列出所有視圖樣版名稱。";
                throw new Exception($"找不到名稱為 '{templateName}' 的視圖樣版。{hint}");
            }

            if (matches.Count > 1)
            {
                string ids = string.Join(", ", matches.Select(v => v.Id.GetIdValue()));
                throw new Exception($"名稱 '{templateName}' 對應到 {matches.Count} 個視圖樣版，請改用 templateId 明確指定（ID 清單：{ids}）。");
            }

            return matches[0];
        }

        /// <summary>把一批使用者輸入的參數名稱解析成 ElementId；找不到時丟出包含相近名稱建議的錯誤，絕不靜默略過。</summary>
        private List<ElementId> ResolveTemplateParamIdsByNames(
            List<string> requestedNames,
            Dictionary<string, List<ElementId>> nameIndex,
            List<string> allDisplayNames,
            List<string> warnings,
            string templateDisplayName)
        {
            var result = new List<ElementId>();
            foreach (string name in requestedNames)
            {
                if (nameIndex.TryGetValue(name, out List<ElementId> matchedIds))
                {
                    if (matchedIds.Count > 1)
                        warnings.Add($"參數名稱 '{name}' 對應到 {matchedIds.Count} 個參數 ID，已全部套用。");
                    result.AddRange(matchedIds);
                    continue;
                }

                List<string> close = allDisplayNames
                    .Where(n => n.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0)
                    .Distinct()
                    .Take(10)
                    .ToList();
                string hint = close.Count > 0
                    ? $"你是不是要找：{string.Join(", ", close)}？"
                    : "呼叫本工具時不帶 uncontrol/control（純列表模式）可查出視圖樣版 '" + templateDisplayName + "' 目前所有可控參數名稱。";
                throw new Exception($"找不到參數名稱 '{name}'（視圖樣版 '{templateDisplayName}' 的可控參數清單中沒有這個名稱）。{hint}");
            }
            return result;
        }

        #endregion
    }
}
