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
    /// 視圖篩選器（ParameterFilterElement）建立／套用／移除。
    ///
    /// 動機：逐個元素 override 不是規則、不會自動涵蓋新元素、別人接手看不出意圖。
    /// 檢討用視圖應該用篩選器表達「規則 → 表現法」，改資料就改顏色。
    ///
    ///   - get_filterable_parameters：列出指定類別「可作為篩選條件」的參數（唯讀）
    ///   - create_view_filter：建立／更新篩選器並套用到視圖（含圖形覆寫）
    ///   - remove_view_filter：從視圖移除，可選一併刪除篩選器本身
    ///
    /// 重用既有 helper：ResolveCategoryId(doc, name)、GetSolidFillPatternId(doc)。
    /// 跨版本：字串規則工廠在 2023 起移除 caseSensitive 參數，以 #if 分支處理。
    /// </summary>
    public partial class CommandExecutor
    {
        #region 類別與參數解析

        /// <summary>把 categories 參數（字串陣列或單一字串）解析成 category ElementId 集合。</summary>
        private ICollection<ElementId> ResolveFilterCategories(Document doc, JToken token)
        {
            var names = new List<string>();
            if (token is JArray arr)
                names.AddRange(arr.Select(t => t.Value<string>()).Where(s => !string.IsNullOrWhiteSpace(s)));
            else if (token != null && token.Type != JTokenType.Null)
            {
                string single = token.Value<string>();
                if (!string.IsNullOrWhiteSpace(single)) names.Add(single);
            }

            if (names.Count == 0)
                throw new Exception("請以 categories 提供至少一個類別名稱（例如 [\"Walls\", \"Structural Columns\"]）");

            var ids = new List<ElementId>();
            var failed = new List<string>();
            foreach (var n in names)
            {
                ElementId id = null;
                try { id = ResolveCategoryId(doc, n); } catch { id = null; }

                if (id == null || id == ElementId.InvalidElementId)
                {
                    failed.Add(n);
                    continue;
                }
                if (!ids.Any(x => x.GetIdValue() == id.GetIdValue()))
                    ids.Add(id);
            }

            if (failed.Count > 0)
                throw new Exception($"無法解析類別名稱：{string.Join(", ", failed)}。可用 list_categories 查詢正確名稱。");

            // Revit 不允許把不可篩選的類別放進 ParameterFilterElement
            var allowed = ParameterFilterUtilities.GetAllFilterableCategories();
            var rejected = ids.Where(i => !allowed.Any(a => a.GetIdValue() == i.GetIdValue()))
                              .Select(i => Category.GetCategory(doc, i)?.Name ?? i.GetIdValue().ToString())
                              .ToList();
            if (rejected.Count > 0)
                throw new Exception($"下列類別不支援篩選器：{string.Join(", ", rejected)}");

            return ids;
        }

        /// <summary>取得參數 ElementId 的顯示名稱（內建參數走 LabelUtils，其餘走 ParameterElement）。</summary>
        private string GetParameterDisplayName(Document doc, ElementId paramId)
        {
            IdType raw = paramId.GetIdValue();
            if (raw < 0)
            {
                try { return LabelUtils.GetLabelFor((BuiltInParameter)(int)raw); }
                catch { return ((BuiltInParameter)(int)raw).ToString(); }
            }
            var pe = doc.GetElement(paramId) as ParameterElement;
            return pe?.Name ?? raw.ToString();
        }

        /// <summary>在指定類別中找一個實例樣本，用來判定參數的 StorageType。</summary>
        private Element FindSampleElement(Document doc, ICollection<ElementId> catIds)
        {
            try
            {
                var multi = new ElementMulticategoryFilter(catIds);
                return new FilteredElementCollector(doc)
                    .WherePasses(multi)
                    .WhereElementIsNotElementType()
                    .FirstElement();
            }
            catch { return null; }
        }

        /// <summary>讀出參數的 StorageType；讀不到回 StorageType.None。</summary>
        private StorageType GetParameterStorageType(Document doc, ICollection<ElementId> catIds, ElementId paramId)
        {
            Element sample = FindSampleElement(doc, catIds);
            if (sample != null)
            {
                foreach (Parameter p in sample.Parameters)
                {
                    if (p?.Id != null && p.Id.GetIdValue() == paramId.GetIdValue())
                        return p.StorageType;
                }
                // 型別參數（例如 Type Name / 類型註解）掛在 ElementType 上
                ElementId typeId = sample.GetTypeId();
                if (typeId != null && typeId != ElementId.InvalidElementId)
                {
                    var et = doc.GetElement(typeId);
                    if (et != null)
                    {
                        foreach (Parameter p in et.Parameters)
                        {
                            if (p?.Id != null && p.Id.GetIdValue() == paramId.GetIdValue())
                                return p.StorageType;
                        }
                    }
                }
            }
            var pe = doc.GetElement(paramId) as ParameterElement;
            if (pe != null)
            {
                try { return pe.GetDefinition().GetDataType() == SpecTypeId.String.Text ? StorageType.String : StorageType.None; }
                catch { }
            }
            return StorageType.None;
        }

        /// <summary>把使用者給的參數名稱對到可篩選參數的 ElementId。</summary>
        private ElementId ResolveFilterableParameter(Document doc, ICollection<ElementId> catIds, string paramName, out string matchedName)
        {
            matchedName = null;
            if (string.IsNullOrWhiteSpace(paramName))
                throw new Exception("規則缺少 parameter（參數名稱）");

            var candidates = ParameterFilterUtilities.GetFilterableParametersInCommon(doc, catIds);

            // 先精確（忽略大小寫），再子字串
            ElementId exact = null;
            ElementId loose = null;
            string exactName = null, looseName = null;

            foreach (var pid in candidates)
            {
                string name = GetParameterDisplayName(doc, pid);
                if (string.IsNullOrEmpty(name)) continue;

                if (string.Equals(name, paramName, StringComparison.OrdinalIgnoreCase))
                {
                    exact = pid; exactName = name; break;
                }
                if (loose == null && name.IndexOf(paramName, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    loose = pid; looseName = name;
                }
            }

            if (exact != null) { matchedName = exactName; return exact; }
            if (loose != null) { matchedName = looseName; return loose; }

            throw new Exception(
                $"參數 '{paramName}' 不在這些類別的可篩選參數清單中。" +
                $"請先用 get_filterable_parameters 查可用名稱（該清單就是 Revit 篩選器對話框下拉選單的內容）。");
        }

        /// <summary>ElementId 型參數的值：可給數字 ID，或給名稱（先找樓層，再找任意具名元素）。</summary>
        private ElementId ResolveElementIdValue(Document doc, string value)
        {
            if (long.TryParse(value, out long numeric))
                return new ElementId((IdType)numeric);

            var level = new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .FirstOrDefault(l => string.Equals(l.Name, value, StringComparison.OrdinalIgnoreCase));
            if (level != null) return level.Id;

            var any = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .FirstOrDefault(e => string.Equals(e.Name, value, StringComparison.OrdinalIgnoreCase));
            if (any != null) return any.Id;

            var anyType = new FilteredElementCollector(doc)
                .WhereElementIsElementType()
                .FirstOrDefault(e => string.Equals(e.Name, value, StringComparison.OrdinalIgnoreCase));
            if (anyType != null) return anyType.Id;

            throw new Exception($"找不到名稱為 '{value}' 的元素或樓層，無法作為 ElementId 型參數的比較值");
        }

        private FilterRule BuildFilterRule(Document doc, ICollection<ElementId> catIds, JObject ruleObj, out string describe)
        {
            string paramName = ruleObj["parameter"]?.Value<string>();
            string op = (ruleObj["operator"]?.Value<string>() ?? "equals").Trim().ToLowerInvariant();
            string value = ruleObj["value"]?.Type == JTokenType.Null ? null : ruleObj["value"]?.ToString();

            ElementId paramId = ResolveFilterableParameter(doc, catIds, paramName, out string matchedName);
            StorageType storage = GetParameterStorageType(doc, catIds, paramId);

            describe = $"{matchedName} {op} {value} [{storage}]";

            if (value == null)
                throw new Exception($"規則 '{matchedName}' 缺少 value");

            switch (storage)
            {
                case StorageType.ElementId:
                {
                    ElementId target = ResolveElementIdValue(doc, value);
                    switch (op)
                    {
                        case "equals": return ParameterFilterRuleFactory.CreateEqualsRule(paramId, target);
                        case "not_equals": return ParameterFilterRuleFactory.CreateNotEqualsRule(paramId, target);
                        default:
                            throw new Exception($"ElementId 型參數 '{matchedName}' 只支援 equals / not_equals，收到 '{op}'");
                    }
                }

                case StorageType.Integer:
                {
                    if (!int.TryParse(value, out int iv))
                    {
                        // 是/否參數常以 true/false 傳入
                        if (bool.TryParse(value, out bool bv)) iv = bv ? 1 : 0;
                        else throw new Exception($"參數 '{matchedName}' 為整數型，無法解析值 '{value}'");
                    }
                    switch (op)
                    {
                        case "equals": return ParameterFilterRuleFactory.CreateEqualsRule(paramId, iv);
                        case "not_equals": return ParameterFilterRuleFactory.CreateNotEqualsRule(paramId, iv);
                        case "greater_than": return ParameterFilterRuleFactory.CreateGreaterRule(paramId, iv);
                        case "greater_or_equal": return ParameterFilterRuleFactory.CreateGreaterOrEqualRule(paramId, iv);
                        case "less_than": return ParameterFilterRuleFactory.CreateLessRule(paramId, iv);
                        case "less_or_equal": return ParameterFilterRuleFactory.CreateLessOrEqualRule(paramId, iv);
                        default: throw new Exception($"整數型參數不支援運算子 '{op}'");
                    }
                }

                case StorageType.Double:
                {
                    if (!double.TryParse(value, out double dv))
                        throw new Exception($"參數 '{matchedName}' 為數值型，無法解析值 '{value}'");
                    const double eps = 1e-6;
                    switch (op)
                    {
                        case "equals": return ParameterFilterRuleFactory.CreateEqualsRule(paramId, dv, eps);
                        case "not_equals": return ParameterFilterRuleFactory.CreateNotEqualsRule(paramId, dv, eps);
                        case "greater_than": return ParameterFilterRuleFactory.CreateGreaterRule(paramId, dv, eps);
                        case "greater_or_equal": return ParameterFilterRuleFactory.CreateGreaterOrEqualRule(paramId, dv, eps);
                        case "less_than": return ParameterFilterRuleFactory.CreateLessRule(paramId, dv, eps);
                        case "less_or_equal": return ParameterFilterRuleFactory.CreateLessOrEqualRule(paramId, dv, eps);
                        default: throw new Exception($"數值型參數不支援運算子 '{op}'");
                    }
                }

                default:
                {
                    // 字串（含 StorageType.None 的保底路徑）
#if REVIT2023_OR_GREATER
                    switch (op)
                    {
                        case "equals": return ParameterFilterRuleFactory.CreateEqualsRule(paramId, value);
                        case "not_equals": return ParameterFilterRuleFactory.CreateNotEqualsRule(paramId, value);
                        case "contains": return ParameterFilterRuleFactory.CreateContainsRule(paramId, value);
                        case "not_contains": return ParameterFilterRuleFactory.CreateNotContainsRule(paramId, value);
                        case "begins_with": return ParameterFilterRuleFactory.CreateBeginsWithRule(paramId, value);
                        case "ends_with": return ParameterFilterRuleFactory.CreateEndsWithRule(paramId, value);
                        default: throw new Exception($"字串型參數不支援運算子 '{op}'");
                    }
#else
                    switch (op)
                    {
                        case "equals": return ParameterFilterRuleFactory.CreateEqualsRule(paramId, value, false);
                        case "not_equals": return ParameterFilterRuleFactory.CreateNotEqualsRule(paramId, value, false);
                        case "contains": return ParameterFilterRuleFactory.CreateContainsRule(paramId, value, false);
                        case "not_contains": return ParameterFilterRuleFactory.CreateNotContainsRule(paramId, value, false);
                        case "begins_with": return ParameterFilterRuleFactory.CreateBeginsWithRule(paramId, value, false);
                        case "ends_with": return ParameterFilterRuleFactory.CreateEndsWithRule(paramId, value, false);
                        default: throw new Exception($"字串型參數不支援運算子 '{op}'");
                    }
#endif
                }
            }
        }

        #endregion

        #region get_filterable_parameters

        private object GetFilterableParameters(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;
            ICollection<ElementId> catIds = ResolveFilterCategories(doc, parameters["categories"]);
            string contains = parameters["contains"]?.Value<string>();

            var paramIds = ParameterFilterUtilities.GetFilterableParametersInCommon(doc, catIds);
            Element sample = FindSampleElement(doc, catIds);

            var rows = new List<object>();
            foreach (var pid in paramIds)
            {
                string name = GetParameterDisplayName(doc, pid);
                if (string.IsNullOrEmpty(name)) continue;
                if (!string.IsNullOrWhiteSpace(contains) &&
                    name.IndexOf(contains, StringComparison.OrdinalIgnoreCase) < 0) continue;

                StorageType st = StorageType.None;
                if (sample != null)
                {
                    foreach (Parameter p in sample.Parameters)
                    {
                        if (p?.Id != null && p.Id.GetIdValue() == pid.GetIdValue()) { st = p.StorageType; break; }
                    }
                }

                IdType raw = pid.GetIdValue();
                rows.Add(new
                {
                    Name = name,
                    ParameterId = raw,
                    IsBuiltIn = raw < 0,
                    StorageType = st.ToString(),
                    SupportedOperators = st == StorageType.ElementId
                        ? new[] { "equals", "not_equals" }
                        : (st == StorageType.Integer || st == StorageType.Double)
                            ? new[] { "equals", "not_equals", "greater_than", "greater_or_equal", "less_than", "less_or_equal" }
                            : new[] { "equals", "not_equals", "contains", "not_contains", "begins_with", "ends_with" }
                });
            }

            return new
            {
                Success = true,
                Categories = catIds.Select(i => Category.GetCategory(doc, i)?.Name).ToList(),
                SampleElementId = sample?.Id.GetIdValue(),
                Count = rows.Count,
                Note = sample == null
                    ? "這些類別在專案中沒有任何實例，StorageType 無法判定（會顯示 None）；規則仍可建立，但建議先放一個元素再查。"
                    : null,
                Parameters = rows.OrderBy(r => ((dynamic)r).Name).ToList()
            };
        }

        #endregion

        #region create_view_filter

        private object CreateViewFilter(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;

            string name = parameters["name"]?.Value<string>();
            if (string.IsNullOrWhiteSpace(name))
                throw new Exception("請提供 name（篩選器名稱）");

            ICollection<ElementId> catIds = ResolveFilterCategories(doc, parameters["categories"]);

            var rulesToken = parameters["rules"] as JArray;
            if (rulesToken == null || rulesToken.Count == 0)
                throw new Exception("請提供 rules（至少一條規則）");

            IdType? viewId = parameters["viewId"]?.Value<IdType>();
            View view;
            if (viewId.HasValue)
            {
                view = doc.GetElement(new ElementId(viewId.Value)) as View;
                if (view == null) throw new Exception($"找不到視圖 ID: {viewId}");
            }
            else view = _uiApp.ActiveUIDocument.ActiveView;

            bool applyToView = parameters["applyToView"]?.Value<bool>() ?? true;
            bool visible = parameters["visible"]?.Value<bool>() ?? true;
            bool overwrite = parameters["overwriteExisting"]?.Value<bool>() ?? false;

            if (applyToView && !view.AreGraphicsOverridesAllowed())
                throw new Exception($"視圖 '{view.Name}' 不允許圖形覆寫（例如某些視圖類型或被鎖定），無法套用篩選器。");

            var warnings = new List<string>();
            if (applyToView && view.ViewTemplateId != null && view.ViewTemplateId != ElementId.InvalidElementId)
            {
                var tpl = doc.GetElement(view.ViewTemplateId) as View;
                if (tpl != null)
                {
                    var controlled = tpl.GetNonControlledTemplateParameterIds()
                        .Select(i => i.GetIdValue()).ToHashSet();
                    // V/G 篩選器是否被樣板控制：不在「非控制」清單中即為受控
                    IdType vgFilters = (IdType)(int)BuiltInParameter.VIS_GRAPHICS_FILTERS;
                    if (!controlled.Contains(vgFilters))
                        warnings.Add($"視圖套用了樣板 '{tpl.Name}' 且 V/G 篩選器受樣板控制，篩選器可能不會生效；請改在樣板上設定，或把該項目從樣板控制中排除。");
                }
            }

            // 建立規則
            var rules = new List<FilterRule>();
            var ruleDescriptions = new List<string>();
            foreach (var rt in rulesToken)
            {
                if (!(rt is JObject ro)) throw new Exception("rules 的每一筆必須是物件，含 parameter / operator / value");
                rules.Add(BuildFilterRule(doc, catIds, ro, out string desc));
                ruleDescriptions.Add(desc);
            }

            ElementFilter elementFilter = new ElementParameterFilter(rules);

            ParameterFilterElement pfe = null;
            bool reused = false;

            using (Transaction trans = TransactionHelper.Begin(doc, "Create View Filter"))
            {
                trans.Start();

                var existing = new FilteredElementCollector(doc)
                    .OfClass(typeof(ParameterFilterElement))
                    .Cast<ParameterFilterElement>()
                    .FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));

                if (existing != null)
                {
                    if (!overwrite)
                    {
                        trans.RollBack();
                        throw new Exception($"已存在同名篩選器 '{name}'（ID {existing.Id.GetIdValue()}）。要沿用並更新它請帶 overwriteExisting=true，或換一個名稱。");
                    }
                    existing.SetCategories(catIds);
                    existing.SetElementFilter(elementFilter);
                    pfe = existing;
                    reused = true;
                }
                else
                {
                    pfe = ParameterFilterElement.Create(doc, name, catIds, elementFilter);
                }

                if (applyToView)
                {
                    if (!view.IsFilterApplied(pfe.Id))
                        view.AddFilter(pfe.Id);

                    view.SetFilterVisibility(pfe.Id, visible);

                    OverrideGraphicSettings ogs = BuildFilterOverrides(doc, view, parameters);
                    view.SetFilterOverrides(pfe.Id, ogs);
                }

                trans.Commit();
            }

            // 命中數（在該視圖範圍內實際被規則抓到幾個元素）
            int matched = -1;
            try
            {
                matched = new FilteredElementCollector(doc, view.Id)
                    .WherePasses(new ElementMulticategoryFilter(catIds))
                    .WherePasses(new ElementParameterFilter(rules))
                    .WhereElementIsNotElementType()
                    .ToElementIds().Count;
            }
            catch { matched = -1; }

            return new
            {
                Success = true,
                FilterId = pfe.Id.GetIdValue(),
                FilterName = pfe.Name,
                Reused = reused,
                Categories = catIds.Select(i => Category.GetCategory(doc, i)?.Name).ToList(),
                Rules = ruleDescriptions,
                AppliedToView = applyToView,
                ViewId = view.Id.GetIdValue(),
                ViewName = view.Name,
                FilterVisible = visible,
                MatchedElementCount = matched,
                MatchedCountNote = matched < 0
                    ? "命中數計算失敗（不影響篩選器本身）"
                    : (matched == 0 ? "規則目前在這張視圖內命中 0 個元素——檢查參數值是否正確、或元素是否在視圖範圍內。" : null),
                Warnings = warnings,
                Message = $"{(reused ? "已更新" : "已建立")}篩選器 '{pfe.Name}'" +
                          (applyToView ? $" 並套用到視圖 '{view.Name}'" : "（未套用到視圖）")
            };
        }

        /// <summary>從參數建出篩選器要用的 OverrideGraphicSettings。</summary>
        private OverrideGraphicSettings BuildFilterOverrides(Document doc, View view, JObject parameters)
        {
            var ogs = new OverrideGraphicSettings();
            ElementId solid = GetSolidFillPatternId(doc);

            string patternMode = (parameters["patternMode"]?.Value<string>() ?? "auto").Trim().ToLowerInvariant();
            bool isPlanView = view.ViewType == ViewType.FloorPlan ||
                              view.ViewType == ViewType.CeilingPlan ||
                              view.ViewType == ViewType.AreaPlan ||
                              view.ViewType == ViewType.EngineeringPlan;

            bool useCut;
            if (patternMode == "cut") useCut = true;
            else if (patternMode == "surface") useCut = false;
            else useCut = isPlanView;

            // 前景填充層。patternName 省略時沿用舊行為（實心填滿），所以既有呼叫端不受影響；
            // 給了名稱就改用該填充樣式，讓篩選器能畫 hatch 而不只是色塊。
            string patternName = parameters["patternName"]?.Value<string>();
            bool hasPatternName = !string.IsNullOrWhiteSpace(patternName);
            ElementId foregroundPatternId = hasPatternName
                ? ResolveFillPatternIdOrThrow(doc, patternName)
                : solid;

            bool hasFillColor = parameters["fillColor"] != null && parameters["fillColor"].Type != JTokenType.Null;

            if (hasFillColor || hasPatternName)
            {
                Color foreColor = null;
                if (hasFillColor)
                {
                    var c = parameters["fillColor"];
                    foreColor = new Color((byte)c["r"].Value<int>(), (byte)c["g"].Value<int>(), (byte)c["b"].Value<int>());
                }

                bool patternUsable = foregroundPatternId != null && foregroundPatternId != ElementId.InvalidElementId;

                if (useCut)
                {
                    if (foreColor != null) ogs.SetCutForegroundPatternColor(foreColor);
                    if (patternUsable)
                    {
                        ogs.SetCutForegroundPatternId(foregroundPatternId);
                        ogs.SetCutForegroundPatternVisible(true);
                    }
                }
                else
                {
                    if (foreColor != null) ogs.SetSurfaceForegroundPatternColor(foreColor);
                    if (patternUsable)
                    {
                        ogs.SetSurfaceForegroundPatternId(foregroundPatternId);
                        ogs.SetSurfaceForegroundPatternVisible(true);
                    }
                }
            }

            // 背景填充層。典型用法：前景放 hatch 線、背景放實心底色（例如淺綠底 + 草地紋）。
            string backgroundPatternName = parameters["backgroundPatternName"]?.Value<string>();
            bool hasBackgroundPattern = !string.IsNullOrWhiteSpace(backgroundPatternName);
            bool hasBackgroundColor = parameters["backgroundColor"] != null &&
                                      parameters["backgroundColor"].Type != JTokenType.Null;

            if (hasBackgroundPattern || hasBackgroundColor)
            {
                ElementId backgroundPatternId = hasBackgroundPattern
                    ? ResolveFillPatternIdOrThrow(doc, backgroundPatternName)
                    : solid;

                Color backColor = null;
                if (hasBackgroundColor)
                {
                    var c = parameters["backgroundColor"];
                    backColor = new Color((byte)c["r"].Value<int>(), (byte)c["g"].Value<int>(), (byte)c["b"].Value<int>());
                }

                bool backPatternUsable = backgroundPatternId != null && backgroundPatternId != ElementId.InvalidElementId;

                if (useCut)
                {
                    if (backColor != null) ogs.SetCutBackgroundPatternColor(backColor);
                    if (backPatternUsable)
                    {
                        ogs.SetCutBackgroundPatternId(backgroundPatternId);
                        ogs.SetCutBackgroundPatternVisible(true);
                    }
                }
                else
                {
                    if (backColor != null) ogs.SetSurfaceBackgroundPatternColor(backColor);
                    if (backPatternUsable)
                    {
                        ogs.SetSurfaceBackgroundPatternId(backgroundPatternId);
                        ogs.SetSurfaceBackgroundPatternVisible(true);
                    }
                }
            }

            if (parameters["lineColor"] != null && parameters["lineColor"].Type != JTokenType.Null)
            {
                var c = parameters["lineColor"];
                var color = new Color((byte)c["r"].Value<int>(), (byte)c["g"].Value<int>(), (byte)c["b"].Value<int>());
                ogs.SetProjectionLineColor(color);
                ogs.SetCutLineColor(color);
            }

            int? lineWeight = parameters["lineWeight"]?.Value<int>();
            if (lineWeight.HasValue && lineWeight.Value >= 1 && lineWeight.Value <= 16)
            {
                ogs.SetProjectionLineWeight(lineWeight.Value);
                ogs.SetCutLineWeight(lineWeight.Value);
            }

            int? transparency = parameters["transparency"]?.Value<int>();
            if (transparency.HasValue)
                ogs.SetSurfaceTransparency(Math.Max(0, Math.Min(100, transparency.Value)));

            bool? halftone = parameters["halftone"]?.Value<bool>();
            if (halftone.HasValue) ogs.SetHalftone(halftone.Value);

            return ogs;
        }

        #endregion

        #region remove_view_filter

        private object RemoveViewFilter(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;

            IdType? viewId = parameters["viewId"]?.Value<IdType>();
            View view;
            if (viewId.HasValue)
            {
                view = doc.GetElement(new ElementId(viewId.Value)) as View;
                if (view == null) throw new Exception($"找不到視圖 ID: {viewId}");
            }
            else view = _uiApp.ActiveUIDocument.ActiveView;

            string name = parameters["name"]?.Value<string>();
            IdType? filterId = parameters["filterId"]?.Value<IdType>();
            bool deleteFilter = parameters["deleteFilter"]?.Value<bool>() ?? false;

            ParameterFilterElement pfe = null;
            if (filterId.HasValue)
                pfe = doc.GetElement(new ElementId(filterId.Value)) as ParameterFilterElement;
            else if (!string.IsNullOrWhiteSpace(name))
                pfe = new FilteredElementCollector(doc)
                    .OfClass(typeof(ParameterFilterElement))
                    .Cast<ParameterFilterElement>()
                    .FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
            else
                throw new Exception("請提供 name 或 filterId");

            if (pfe == null)
                throw new Exception($"找不到篩選器（name='{name}', filterId={filterId}）");

            bool wasApplied = view.IsFilterApplied(pfe.Id);
            IdType removedId = pfe.Id.GetIdValue();
            string removedName = pfe.Name;

            using (Transaction trans = TransactionHelper.Begin(doc, "Remove View Filter"))
            {
                trans.Start();
                if (wasApplied) view.RemoveFilter(pfe.Id);
                if (deleteFilter) doc.Delete(pfe.Id);
                trans.Commit();
            }

            return new
            {
                Success = true,
                FilterId = removedId,
                FilterName = removedName,
                WasAppliedToView = wasApplied,
                RemovedFromView = wasApplied,
                FilterElementDeleted = deleteFilter,
                ViewId = view.Id.GetIdValue(),
                ViewName = view.Name,
                Message = deleteFilter
                    ? $"已從視圖 '{view.Name}' 移除並刪除篩選器 '{removedName}'"
                    : $"已從視圖 '{view.Name}' 移除篩選器 '{removedName}'（篩選器本身保留在專案中）"
            };
        }

        #endregion
    }
}
