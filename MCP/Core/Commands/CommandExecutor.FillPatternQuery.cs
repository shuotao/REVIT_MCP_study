using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;

namespace RevitMCP.Core
{
    /// <summary>
    /// 填充樣式（FillPatternElement）查詢與名稱解析。
    ///
    /// 存在理由：視圖覆寫、篩選器覆寫、材質表面樣式都要用 FillPatternElement 的 ElementId，
    /// 但先前沒有任何工具能把專案裡可用的樣式名稱列出來，AI 端只能猜 ID 或退回實心填滿。
    /// </summary>
    public partial class CommandExecutor
    {
        /// <summary>
        /// 列出專案中所有填充樣式，供後續指定 pattern 名稱時對照。唯讀。
        /// </summary>
        private object ListFillPatterns(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;

            string contains = parameters["contains"]?.Value<string>();
            string targetFilter = (parameters["target"]?.Value<string>() ?? "any").Trim().ToLowerInvariant();

            var all = new FilteredElementCollector(doc)
                .OfClass(typeof(FillPatternElement))
                .Cast<FillPatternElement>()
                .ToList();

            var rows = new List<object>();
            int modelCount = 0;
            int draftingCount = 0;

            foreach (var fpe in all)
            {
                FillPattern fp;
                try
                {
                    fp = fpe.GetFillPattern();
                }
                catch
                {
                    continue;
                }
                if (fp == null) continue;

                bool isModel = fp.Target == FillPatternTarget.Model;
                if (isModel) modelCount++; else draftingCount++;

                if (targetFilter == "model" && !isModel) continue;
                if (targetFilter == "drafting" && isModel) continue;

                if (!string.IsNullOrWhiteSpace(contains) &&
                    fpe.Name.IndexOf(contains, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                rows.Add(new
                {
                    Id = fpe.Id.GetIdValue(),
                    Name = fpe.Name,
                    Target = isModel ? "Model" : "Drafting",
                    IsSolidFill = fp.IsSolidFill,
                    GridCount = fp.GridCount
                });
            }

            return new
            {
                Success = true,
                TotalInProject = all.Count,
                ModelPatternCount = modelCount,
                DraftingPatternCount = draftingCount,
                ReturnedCount = rows.Count,
                TargetFilter = targetFilter,
                Contains = contains,
                Note = "Model 樣式隨視圖比例縮放（實際尺寸），Drafting 樣式在圖紙上固定大小。地形／材質紋理通常用 Model。",
                Patterns = rows
            };
        }

        /// <summary>
        /// 依名稱解析 FillPatternElement。比對順序：
        /// 完全相符 → 不分大小寫相符 → 包含（子字串）。
        /// preferModel=true 時，同分的候選優先取 Model target（隨比例縮放，較符合建模語意）。
        /// 找不到回傳 ElementId.InvalidElementId，由呼叫端決定要報錯還是退回預設。
        /// </summary>
        private ElementId ResolveFillPatternId(Document doc, string name, bool preferModel = true)
        {
            if (string.IsNullOrWhiteSpace(name)) return ElementId.InvalidElementId;

            var candidates = new FilteredElementCollector(doc)
                .OfClass(typeof(FillPatternElement))
                .Cast<FillPatternElement>()
                .ToList();

            Func<IEnumerable<FillPatternElement>, FillPatternElement> pick = list =>
            {
                var items = list.ToList();
                if (items.Count == 0) return null;
                if (!preferModel) return items[0];

                var model = items.FirstOrDefault(fpe =>
                {
                    try { return fpe.GetFillPattern()?.Target == FillPatternTarget.Model; }
                    catch { return false; }
                });
                return model ?? items[0];
            };

            var hit = pick(candidates.Where(f => f.Name == name))
                   ?? pick(candidates.Where(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase)))
                   ?? pick(candidates.Where(f => f.Name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0));

            return hit?.Id ?? ElementId.InvalidElementId;
        }

        /// <summary>
        /// 解析 pattern 名稱，找不到時丟出帶「可用名稱範例」的例外，
        /// 避免呼叫端拿到 InvalidElementId 後靜默退回實心填滿（那會讓使用者以為設定成功了）。
        /// </summary>
        private ElementId ResolveFillPatternIdOrThrow(Document doc, string name, bool preferModel = true)
        {
            ElementId id = ResolveFillPatternId(doc, name, preferModel);
            if (id != null && id != ElementId.InvalidElementId) return id;

            var samples = new FilteredElementCollector(doc)
                .OfClass(typeof(FillPatternElement))
                .Cast<FillPatternElement>()
                .Select(f => f.Name)
                .OrderBy(n => n)
                .Take(15)
                .ToList();

            throw new Exception(
                $"找不到填充樣式 '{name}'。請先用 list_fill_patterns 查可用名稱。" +
                $"（專案中前幾個樣式：{string.Join(", ", samples)}）");
        }
    }
}
