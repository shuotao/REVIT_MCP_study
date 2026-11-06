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
        #region 視圖圖形診斷（唯讀）

        /// <summary>
        /// 唯讀診斷：回答「這張圖現在為什麼長這樣」。
        /// 依 Revit 的圖形覆寫優先序，一次收集所有可能來源：
        ///   視覺型式 > 視圖樣板 > 元素個別覆寫 > 篩選器 > 類別覆寫 > 材料本身
        /// 不開 Transaction、不修改任何東西。
        /// </summary>
        private object GetViewGraphicsDiagnostics(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;

            // --- 目標視圖 ---
            View view;
            IdType? viewIdParam = parameters["viewId"]?.Value<IdType>();
            if (viewIdParam.HasValue && viewIdParam.Value != 0)
            {
                view = doc.GetElement(viewIdParam.Value.ToElementId()) as View;
                if (view == null)
                    throw new Exception($"找不到視圖 ID {viewIdParam.Value}");
            }
            else
            {
                view = _uiApp.ActiveUIDocument.ActiveView;
            }

            // --- 目標元素（選填）---
            Element target = null;
            IdType? elementIdParam = parameters["elementId"]?.Value<IdType>();
            if (elementIdParam.HasValue && elementIdParam.Value != 0)
            {
                target = doc.GetElement(elementIdParam.Value.ToElementId());
                if (target == null)
                    throw new Exception($"找不到元素 ID {elementIdParam.Value}");
            }

            var result = new Dictionary<string, object>();

            result["View"] = DescribeDiagView(doc, view);
            result["Filters"] = DescribeDiagFilters(doc, view, target);

            if (target != null)
            {
                result["Element"] = new Dictionary<string, object>
                {
                    ["ElementId"] = target.Id.GetIdValue(),
                    ["Name"] = target.Name,
                    ["Category"] = target.Category?.Name ?? "N/A",
                    ["TypeId"] = target.GetTypeId().GetIdValue()
                };
                result["ElementOverride"] = DescribeDiagOverride(doc, SafeGetElementOverrides(view, target.Id));
                result["CategoryOverride"] = DescribeDiagCategoryOverride(doc, view, target.Category);
                result["Materials"] = DescribeDiagMaterials(doc, target);
            }

            result["Verdict"] = BuildDiagVerdict(view, target, result);
            return result;
        }

        // ---------- 視圖本身 ----------

        private Dictionary<string, object> DescribeDiagView(Document doc, View view)
        {
            var info = new Dictionary<string, object>
            {
                ["ElementId"] = view.Id.GetIdValue(),
                ["Name"] = view.Name,
                ["ViewType"] = view.ViewType.ToString(),
                ["Scale"] = view.Scale
            };

            try { info["DisplayStyle"] = view.DisplayStyle.ToString(); }
            catch { info["DisplayStyle"] = "N/A"; }

            try { info["DetailLevel"] = view.DetailLevel.ToString(); }
            catch { info["DetailLevel"] = "N/A"; }

            // 視覺型式是否會把所有表面上色（Wireframe / HLR 之外都會）
            try
            {
                var ds = view.DisplayStyle;
                info["DisplayStyleShadesSurfaces"] =
                    ds != DisplayStyle.Wireframe && ds != DisplayStyle.HLR;
            }
            catch { info["DisplayStyleShadesSurfaces"] = false; }

            // 視圖樣板
            try
            {
                if (view.ViewTemplateId != ElementId.InvalidElementId)
                {
                    var template = doc.GetElement(view.ViewTemplateId) as View;
                    info["ViewTemplateId"] = view.ViewTemplateId.GetIdValue();
                    info["ViewTemplateName"] = template?.Name ?? "Unknown";
                    info["ControlledByTemplate"] = DescribeDiagTemplateControl(template);
                }
                else
                {
                    info["ViewTemplateId"] = 0;
                    info["ViewTemplateName"] = null;
                    info["ControlledByTemplate"] = new List<string>();
                }
            }
            catch
            {
                info["ViewTemplateName"] = "N/A";
                info["ControlledByTemplate"] = new List<string>();
            }

            return info;
        }

        /// <summary>
        /// 列出被視圖樣板鎖住的圖形相關參數（使用者在視圖上改不動的那些）
        /// </summary>
        private List<string> DescribeDiagTemplateControl(View template)
        {
            var controlled = new List<string>();
            if (template == null) return controlled;

            try
            {
                var notControlled = new HashSet<IdType>(
                    template.GetNonControlledTemplateParameterIds().Select(id => id.GetIdValue()));

                var interesting = new Dictionary<BuiltInParameter, string>
                {
                    [BuiltInParameter.MODEL_GRAPHICS_STYLE] = "視覺型式 (Display Style)",
                    [BuiltInParameter.VIS_GRAPHICS_MODEL] = "模型類別 V/G 覆寫",
                    [BuiltInParameter.VIS_GRAPHICS_FILTERS] = "篩選器 (Filters)",
                    [BuiltInParameter.VIEW_DETAIL_LEVEL] = "詳細程度"
                };

                foreach (var kv in interesting)
                {
                    try
                    {
                        var pid = new ElementId(kv.Key).GetIdValue();
                        if (!notControlled.Contains(pid))
                            controlled.Add(kv.Value);
                    }
                    catch { }
                }
            }
            catch { }

            return controlled;
        }

        // ---------- 篩選器 ----------

        private List<Dictionary<string, object>> DescribeDiagFilters(Document doc, View view, Element target)
        {
            var list = new List<Dictionary<string, object>>();
            ICollection<ElementId> filterIds;
            try { filterIds = view.GetFilters(); }
            catch { return list; }

            foreach (var fid in filterIds)
            {
                var entry = new Dictionary<string, object>();
                var filterElem = doc.GetElement(fid);
                entry["FilterId"] = fid.GetIdValue();
                entry["Name"] = filterElem?.Name ?? "Unknown";

                try { entry["Enabled"] = view.GetIsFilterEnabled(fid); }
                catch { entry["Enabled"] = false; }

                try { entry["Visible"] = view.GetFilterVisibility(fid); }
                catch { entry["Visible"] = true; }

                try { entry["Overrides"] = DescribeDiagOverride(doc, view.GetFilterOverrides(fid)); }
                catch { entry["Overrides"] = null; }

                // 這個篩選器有沒有抓到目標元素
                entry["MatchesTargetElement"] = DiagFilterMatches(filterElem as ParameterFilterElement, target);

                list.Add(entry);
            }

            return list;
        }

        private object DiagFilterMatches(ParameterFilterElement filterElem, Element target)
        {
            if (filterElem == null || target == null) return "N/A";
            try
            {
                // 先看類別是否包含，再跑規則
                if (target.Category == null) return false;
                var cats = filterElem.GetCategories();
                if (!cats.Any(c => c.GetIdValue() == target.Category.Id.GetIdValue())) return false;

                var ef = filterElem.GetElementFilter();
                if (ef == null) return true; // 只有類別、沒有規則 = 全中
                return ef.PassesFilter(target);
            }
            catch { return "N/A"; }
        }

        // ---------- 覆寫設定 ----------

        private OverrideGraphicSettings SafeGetElementOverrides(View view, ElementId id)
        {
            try { return view.GetElementOverrides(id); }
            catch { return null; }
        }

        private Dictionary<string, object> DescribeDiagCategoryOverride(Document doc, View view, Category category)
        {
            if (category == null) return null;
            OverrideGraphicSettings ogs = null;
            try { ogs = view.GetCategoryOverrides(category.Id); }
            catch { }

            var d = DescribeDiagOverride(doc, ogs) ?? new Dictionary<string, object>();
            d["CategoryName"] = category.Name;
            try { d["CategoryHidden"] = view.GetCategoryHidden(category.Id); }
            catch { d["CategoryHidden"] = false; }
            return d;
        }

        /// <summary>
        /// 把 OverrideGraphicSettings 攤平成可讀欄位，並判斷到底有沒有覆寫
        /// </summary>
        private Dictionary<string, object> DescribeDiagOverride(Document doc, OverrideGraphicSettings ogs)
        {
            if (ogs == null) return null;

            var d = new Dictionary<string, object>();
            bool hasAny = false;

            Action<string, Color> addColor = (key, color) =>
            {
                if (color != null && color.IsValid)
                {
                    d[key] = $"RGB({color.Red},{color.Green},{color.Blue})";
                    hasAny = true;
                }
                else
                {
                    d[key] = null;
                }
            };

            Action<string, ElementId> addPattern = (key, id) =>
            {
                if (id != null && id != ElementId.InvalidElementId)
                {
                    var fp = doc.GetElement(id) as FillPatternElement;
                    string name = fp?.Name ?? id.GetIdValue().ToString();
                    bool isSolid = false;
                    try { isSolid = fp?.GetFillPattern()?.IsSolidFill ?? false; } catch { }
                    d[key] = name + (isSolid ? " (實心填滿)" : "");
                    hasAny = true;
                }
                else
                {
                    d[key] = null;
                }
            };

            try { addColor("SurfaceForegroundPatternColor", ogs.SurfaceForegroundPatternColor); } catch { }
            try { addPattern("SurfaceForegroundPattern", ogs.SurfaceForegroundPatternId); } catch { }
            try { addColor("SurfaceBackgroundPatternColor", ogs.SurfaceBackgroundPatternColor); } catch { }
            try { addPattern("SurfaceBackgroundPattern", ogs.SurfaceBackgroundPatternId); } catch { }
            try { addColor("CutForegroundPatternColor", ogs.CutForegroundPatternColor); } catch { }
            try { addPattern("CutForegroundPattern", ogs.CutForegroundPatternId); } catch { }
            try { addColor("CutBackgroundPatternColor", ogs.CutBackgroundPatternColor); } catch { }
            try { addPattern("CutBackgroundPattern", ogs.CutBackgroundPatternId); } catch { }
            try { addColor("ProjectionLineColor", ogs.ProjectionLineColor); } catch { }
            try { addColor("CutLineColor", ogs.CutLineColor); } catch { }

            try
            {
                d["Halftone"] = ogs.Halftone;
                if (ogs.Halftone) hasAny = true;
            }
            catch { }

            try
            {
                int t = ogs.Transparency;
                d["SurfaceTransparency"] = t;
                if (t != 0) hasAny = true;
            }
            catch { }

            try
            {
                int w = ogs.ProjectionLineWeight;
                d["ProjectionLineWeight"] = w == OverrideGraphicSettings.InvalidPenNumber ? (object)null : w;
                if (w != OverrideGraphicSettings.InvalidPenNumber) hasAny = true;
            }
            catch { }

            d["HasAnyOverride"] = hasAny;
            return d;
        }

        // ---------- 材料 ----------

        private List<Dictionary<string, object>> DescribeDiagMaterials(Document doc, Element target)
        {
            var list = new List<Dictionary<string, object>>();
            var seen = new HashSet<IdType>();
            var ids = new List<ElementId>();

            // 複合結構（牆／樓板／屋頂／天花）逐層取材料，順序即層序
            try
            {
                var hostAttr = doc.GetElement(target.GetTypeId()) as HostObjAttributes;
                var cs = hostAttr?.GetCompoundStructure();
                if (cs != null)
                {
                    foreach (var layer in cs.GetLayers())
                        ids.Add(layer.MaterialId);
                }
            }
            catch { }

            // 其餘元素走通用取法
            if (ids.Count == 0)
            {
                try { ids.AddRange(target.GetMaterialIds(false)); }
                catch { }
            }

            int index = 0;
            foreach (var mid in ids)
            {
                index++;
                if (mid == null || mid == ElementId.InvalidElementId) continue;
                if (!seen.Add(mid.GetIdValue())) continue;

                var mat = doc.GetElement(mid) as Material;
                if (mat == null) continue;

                var m = new Dictionary<string, object>
                {
                    ["LayerIndex"] = index,
                    ["MaterialId"] = mid.GetIdValue(),
                    ["MaterialName"] = mat.Name
                };

                try
                {
                    var c = mat.Color;
                    m["ShadingColor"] = (c != null && c.IsValid) ? $"RGB({c.Red},{c.Green},{c.Blue})" : null;
                }
                catch { m["ShadingColor"] = null; }

                try { m["UseRenderAppearanceForShading"] = mat.UseRenderAppearanceForShading; }
                catch { }

                AddDiagMaterialPattern(doc, m, "SurfaceForeground", mat.SurfaceForegroundPatternId, mat.SurfaceForegroundPatternColor);
                AddDiagMaterialPattern(doc, m, "SurfaceBackground", mat.SurfaceBackgroundPatternId, mat.SurfaceBackgroundPatternColor);
                AddDiagMaterialPattern(doc, m, "CutForeground", mat.CutForegroundPatternId, mat.CutForegroundPatternColor);
                AddDiagMaterialPattern(doc, m, "CutBackground", mat.CutBackgroundPatternId, mat.CutBackgroundPatternColor);

                list.Add(m);
            }

            return list;
        }

        private void AddDiagMaterialPattern(Document doc, Dictionary<string, object> bag, string prefix, ElementId patternId, Color color)
        {
            try
            {
                if (patternId != null && patternId != ElementId.InvalidElementId)
                {
                    var fp = doc.GetElement(patternId) as FillPatternElement;
                    bag[prefix + "Pattern"] = fp?.Name ?? patternId.GetIdValue().ToString();
                    bool isSolid = false;
                    try { isSolid = fp?.GetFillPattern()?.IsSolidFill ?? false; } catch { }
                    bag[prefix + "PatternIsSolid"] = isSolid;
                }
                else
                {
                    bag[prefix + "Pattern"] = null;
                    bag[prefix + "PatternIsSolid"] = false;
                }
            }
            catch { }

            try
            {
                bag[prefix + "PatternColor"] =
                    (color != null && color.IsValid) ? $"RGB({color.Red},{color.Green},{color.Blue})" : null;
            }
            catch { }
        }

        // ---------- 推定主因 ----------

        /// <summary>
        /// 依 Revit 覆寫優先序列出「所有」命中的成因，而不是只回第一個。
        /// 一張圖可以同時被視覺型式、篩選器與材料影響；只回一個會讓後續判讀被誤導。
        /// 啟發式判斷，每筆都標明是推定。
        /// </summary>
        private Dictionary<string, object> BuildDiagVerdict(View view, Element target, Dictionary<string, object> result)
        {
            var causes = new List<Dictionary<string, object>>();
            var viewInfo = result["View"] as Dictionary<string, object>;

            // --- 優先序 1：視覺型式（影響整張圖的所有表面）---
            if (viewInfo != null
                && viewInfo.TryGetValue("DisplayStyleShadesSurfaces", out var shadesObj)
                && shadesObj is bool shades && shades)
            {
                string tmpl = viewInfo.TryGetValue("ViewTemplateName", out var tmplObj) ? tmplObj as string : null;
                string via = string.IsNullOrEmpty(tmpl) ? "" : $"，且由視圖樣板 '{tmpl}' 控制（在視圖上改不動）";
                causes.Add(MakeDiagCause(
                    1,
                    "視覺型式 (Display Style)",
                    "整個視圖",
                    $"視覺型式 = {viewInfo["DisplayStyle"]}{via}。此模式下所有表面都會著色，與材料或覆寫無關。",
                    string.IsNullOrEmpty(tmpl)
                        ? "把視覺型式改回 Hidden Line。"
                        : $"先解除或修改視圖樣板 '{tmpl}'，再改視覺型式。"));
            }

            if (target == null)
            {
                return new Dictionary<string, object>
                {
                    ["PrimaryCause"] = causes.Count > 0 ? causes[0]["Source"] : null,
                    ["CauseCount"] = causes.Count,
                    ["Causes"] = causes,
                    ["Summary"] = causes.Count > 0
                        ? $"未指定 elementId，僅回報視圖層級成因（{causes.Count} 項）。要追某個元素的表現法來源請帶 elementId。"
                        : "未指定 elementId，視圖層級沒有發現全域著色設定。要追某個元素的表現法來源請帶 elementId。"
                };
            }

            IdType targetId = target.Id.GetIdValue();

            // --- 優先序 2：元素個別覆寫 ---
            var elemEffects = DescribeDiagEffects(result["ElementOverride"] as Dictionary<string, object>);
            if (elemEffects.Count > 0)
            {
                causes.Add(MakeDiagCause(
                    2,
                    "元素個別覆寫",
                    "此視圖的這個元素",
                    $"元素 {targetId} 在本視圖被直接覆寫（在視圖中覆寫圖形 > 依元素）：{string.Join("；", elemEffects)}。",
                    "用 clear_element_override 還原，或在 Revit 中右鍵 > 在視圖中覆寫圖形 > 依元素 > 重設。"));
            }

            // --- 優先序 3：篩選器（可能有多個同時命中，全部列出）---
            var filters = result["Filters"] as List<Dictionary<string, object>>;
            if (filters != null)
            {
                foreach (var f in filters)
                {
                    bool enabled = f.TryGetValue("Enabled", out var enabledObj) && enabledObj is bool e && e;
                    bool matches = f.TryGetValue("MatchesTargetElement", out var matchObj) && matchObj is bool m && m;
                    if (!enabled || !matches) continue;

                    bool visible = !f.TryGetValue("Visible", out var visObj) || !(visObj is bool v) || v;
                    if (!visible)
                    {
                        causes.Add(MakeDiagCause(
                            3,
                            $"篩選器 '{f["Name"]}'（隱藏）",
                            "此視圖的這個元素",
                            $"篩選器 '{f["Name"]}' 抓到這個元素並把它設為不可見——元素存在但畫不出來。",
                            "到 VG > 篩選器 頁籤，把該列的『可見性』勾回來。"));
                        continue;
                    }

                    var filterEffects = DescribeDiagEffects(f["Overrides"] as Dictionary<string, object>);
                    if (filterEffects.Count > 0)
                    {
                        causes.Add(MakeDiagCause(
                            3,
                            $"篩選器 '{f["Name"]}'",
                            "此視圖的這個元素",
                            $"篩選器 '{f["Name"]}' 抓到這個元素並套了：{string.Join("；", filterEffects)}。",
                            "到 VG > 篩選器 頁籤關掉該覆寫，或修改篩選器規則讓它不要抓到這個元素。"));
                    }
                }
            }

            // --- 優先序 4：類別覆寫 ---
            var catOgs = result["CategoryOverride"] as Dictionary<string, object>;
            if (catOgs != null)
            {
                if (catOgs.TryGetValue("CategoryHidden", out var hiddenObj) && hiddenObj is bool hidden && hidden)
                {
                    causes.Add(MakeDiagCause(
                        4,
                        "類別被關閉",
                        "此視圖的整個品類",
                        $"品類 '{catOgs["CategoryName"]}' 在本視圖被關閉可見性——這個元素根本不會顯示。",
                        "到 VG > 模型類別 頁籤把該品類勾回來。"));
                }

                var catEffects = DescribeDiagEffects(catOgs);
                if (catEffects.Count > 0)
                {
                    causes.Add(MakeDiagCause(
                        4,
                        "類別覆寫",
                        "此視圖的整個品類",
                        $"品類 '{catOgs["CategoryName"]}' 在本視圖被套了：{string.Join("；", catEffects)}——同品類的元素都會受影響。",
                        "到 VG > 模型類別 頁籤清掉該品類的覆寫。"));
                }
            }

            // --- 優先序 5：材料本身（影響全專案）---
            var mats = result["Materials"] as List<Dictionary<string, object>>;
            if (mats != null)
            {
                foreach (var mat in mats)
                {
                    var matEffects = DescribeDiagMaterialEffects(mat);
                    if (matEffects.Count == 0) continue;

                    causes.Add(MakeDiagCause(
                        5,
                        $"材料 '{mat["MaterialName"]}'",
                        "全專案所有視圖",
                        $"材料 '{mat["MaterialName"]}'（第 {mat["LayerIndex"]} 層）本身的設定：{string.Join("；", matEffects)}。",
                        "改材料的圖形設定會影響全專案；若只想改這張圖，改用視圖覆寫或篩選器。"));
                }
            }

            string summary;
            if (causes.Count == 0)
            {
                summary = "在視覺型式、元素覆寫、篩選器、類別覆寫、材料五個來源都沒找到會改變表現法的設定。"
                        + "請再檢查連結模型覆寫、設計選項、階段化圖形覆寫，或該位置是否疊了填滿區域／詳圖項目。";
            }
            else
            {
                var lines = causes.Select((c, i) => $"{i + 1}. [{c["Source"]}／{c["Scope"]}] {c["Detail"]}");
                summary = $"推定：找到 {causes.Count} 個會影響表現法的成因，依 Revit 覆寫優先序排列（越前面越優先勝出）：\n"
                        + string.Join("\n", lines);
            }

            return new Dictionary<string, object>
            {
                ["PrimaryCause"] = causes.Count > 0 ? causes[0]["Source"] : null,
                ["CauseCount"] = causes.Count,
                ["Causes"] = causes,
                ["Summary"] = summary
            };
        }

        private Dictionary<string, object> MakeDiagCause(int priority, string source, string scope, string detail, string fix)
        {
            return new Dictionary<string, object>
            {
                ["Priority"] = priority,
                ["Source"] = source,
                ["Scope"] = scope,
                ["Detail"] = detail,
                ["Fix"] = fix
            };
        }

        /// <summary>
        /// 把一組已攤平的覆寫設定翻成「使用者看得到的效果」清單。
        /// 涵蓋表面／剖面填滿、線色、線寬、半色調、透明度——不是只看表面顏色。
        /// </summary>
        private List<string> DescribeDiagEffects(Dictionary<string, object> ogs)
        {
            var effects = new List<string>();
            if (ogs == null) return effects;

            Action<string, string> add = (key, label) =>
            {
                if (ogs.TryGetValue(key, out var value) && value != null)
                    effects.Add($"{label} = {value}");
            };

            add("SurfaceForegroundPatternColor", "表面前景填滿顏色");
            add("SurfaceForegroundPattern", "表面前景填充樣式");
            add("SurfaceBackgroundPatternColor", "表面背景填滿顏色");
            add("SurfaceBackgroundPattern", "表面背景填充樣式");
            add("CutForegroundPatternColor", "剖面前景填滿顏色");
            add("CutForegroundPattern", "剖面前景填充樣式");
            add("CutBackgroundPatternColor", "剖面背景填滿顏色");
            add("CutBackgroundPattern", "剖面背景填充樣式");
            add("ProjectionLineColor", "投影線顏色");
            add("CutLineColor", "剖面線顏色");
            add("ProjectionLineWeight", "投影線寬");

            if (ogs.TryGetValue("Halftone", out var halftoneObj) && halftoneObj is bool halftone && halftone)
                effects.Add("半色調 (Halftone)");

            if (ogs.TryGetValue("SurfaceTransparency", out var transObj) && transObj is int transparency && transparency != 0)
                effects.Add($"表面透明度 = {transparency}%");

            return effects;
        }

        /// <summary>
        /// 材料本身的圖形設定裡，哪些會實際改變畫面。
        /// </summary>
        private List<string> DescribeDiagMaterialEffects(Dictionary<string, object> mat)
        {
            var effects = new List<string>();
            if (mat == null) return effects;

            Action<string, string> addPattern = (prefix, label) =>
            {
                bool solid = mat.TryGetValue(prefix + "PatternIsSolid", out var solidObj) && solidObj is bool isSolid && isSolid;
                object color = mat.TryGetValue(prefix + "PatternColor", out var colorObj) ? colorObj : null;
                object pattern = mat.TryGetValue(prefix + "Pattern", out var patternObj) ? patternObj : null;

                if (solid && color != null)
                    effects.Add($"{label}為實心填滿 + {color}");
                else if (pattern != null && color != null)
                    effects.Add($"{label} = {pattern} + {color}");
            };

            addPattern("SurfaceForeground", "表面前景填充樣式");
            addPattern("SurfaceBackground", "表面背景填充樣式");
            addPattern("CutForeground", "剖面前景填充樣式");
            addPattern("CutBackground", "剖面背景填充樣式");

            return effects;
        }

        #endregion
    }
}
