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
    /// 材質「圖形」頁籤（Graphics）的讀寫：著色顏色、透明度、表面/切割的前景與背景填充。
    ///
    /// 存在理由：視圖篩選器與 V/G 覆寫只作用在單一視圖；要讓「所有視圖」一起變，
    /// 必須改材質本身。既有的 set_material_surface_pattern 只服務綠建材命名、
    /// 只能產生網格/木紋，也不能改著色顏色，所以另開這個通用工具。
    /// </summary>
    public partial class CommandExecutor
    {
        private object GetMaterialGraphics(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;
            Material material = ResolveMaterialForGraphics(doc, parameters);
            return new
            {
                Success = true,
                DocumentTitle = doc.Title,
                Graphics = DescribeMaterialGraphics(doc, material)
            };
        }

        private object SetMaterialGraphics(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;
            Material material = ResolveMaterialForGraphics(doc, parameters);
            bool dryRun = parameters["dryRun"]?.Value<bool>() ?? false;

            object before = DescribeMaterialGraphics(doc, material);

            // 先把所有輸入解析完，填充樣式名稱錯了就在開 Transaction 前報錯
            Color shadingColor = ParseRgb(parameters["shadingColor"]);
            bool? useRenderAppearance = parameters["useRenderAppearance"]?.Value<bool?>();
            int? transparency = parameters["transparency"]?.Value<int?>();

            var layers = new[]
            {
                new { Key = "surfaceForeground", Pattern = parameters["surfaceForegroundPattern"]?.Value<string>(), Color = ParseRgb(parameters["surfaceForegroundColor"]) },
                new { Key = "surfaceBackground", Pattern = parameters["surfaceBackgroundPattern"]?.Value<string>(), Color = ParseRgb(parameters["surfaceBackgroundColor"]) },
                new { Key = "cutForeground", Pattern = parameters["cutForegroundPattern"]?.Value<string>(), Color = ParseRgb(parameters["cutForegroundColor"]) },
                new { Key = "cutBackground", Pattern = parameters["cutBackgroundPattern"]?.Value<string>(), Color = ParseRgb(parameters["cutBackgroundColor"]) },
            };

            var resolvedPatterns = new Dictionary<string, ElementId>();
            foreach (var layer in layers)
            {
                if (string.IsNullOrWhiteSpace(layer.Pattern)) continue;
                // "none" = 清除該層圖案
                resolvedPatterns[layer.Key] = layer.Pattern.Trim().Equals("none", StringComparison.OrdinalIgnoreCase)
                    ? ElementId.InvalidElementId
                    : ResolveMaterialGraphicsPattern(doc, layer.Pattern);
            }

            bool hasAnyChange = shadingColor != null || useRenderAppearance.HasValue || transparency.HasValue
                                || layers.Any(l => l.Color != null) || resolvedPatterns.Count > 0;
            if (!hasAnyChange)
                throw new Exception("沒有指定任何要修改的項目（shadingColor / useRenderAppearance / transparency / 各層 pattern 或 color）。");

            if (dryRun)
            {
                return new
                {
                    Success = true,
                    DryRun = true,
                    DocumentTitle = doc.Title,
                    MaterialId = material.Id.GetIdValue(),
                    MaterialName = material.Name,
                    Before = before,
                    Message = "dryRun：輸入已驗證（填充樣式名稱都找得到），未修改材質。"
                };
            }

            using (Transaction trans = new Transaction(doc, "設定材質圖形"))
            {
                trans.Start();

                if (useRenderAppearance.HasValue)
                    material.UseRenderAppearanceForShading = useRenderAppearance.Value;
                if (shadingColor != null)
                {
                    // 著色顏色要生效，必須關閉「使用彩現外觀」，否則 Revit 會改用外觀顏色著色
                    if (!useRenderAppearance.HasValue) material.UseRenderAppearanceForShading = false;
                    material.Color = shadingColor;
                }
                if (transparency.HasValue)
                    material.Transparency = Math.Max(0, Math.Min(100, transparency.Value));

                foreach (var layer in layers)
                {
                    bool hasPattern = resolvedPatterns.TryGetValue(layer.Key, out ElementId patternId);
                    switch (layer.Key)
                    {
                        case "surfaceForeground":
                            if (hasPattern) material.SurfaceForegroundPatternId = patternId;
                            if (layer.Color != null) material.SurfaceForegroundPatternColor = layer.Color;
                            break;
                        case "surfaceBackground":
                            if (hasPattern) material.SurfaceBackgroundPatternId = patternId;
                            if (layer.Color != null) material.SurfaceBackgroundPatternColor = layer.Color;
                            break;
                        case "cutForeground":
                            if (hasPattern) material.CutForegroundPatternId = patternId;
                            if (layer.Color != null) material.CutForegroundPatternColor = layer.Color;
                            break;
                        case "cutBackground":
                            if (hasPattern) material.CutBackgroundPatternId = patternId;
                            if (layer.Color != null) material.CutBackgroundPatternColor = layer.Color;
                            break;
                    }
                }

                trans.Commit();
            }

            return new
            {
                Success = true,
                DryRun = false,
                DocumentTitle = doc.Title,
                MaterialId = material.Id.GetIdValue(),
                MaterialName = material.Name,
                Before = before,
                After = DescribeMaterialGraphics(doc, material),
                Message = $"已更新材質 '{material.Name}' 的圖形設定（After 為寫入後從文件回讀的值）。"
            };
        }

        private Material ResolveMaterialForGraphics(Document doc, JObject parameters)
        {
            IdType? materialId = parameters["materialId"]?.Value<IdType?>();
            string materialName = parameters["materialName"]?.Value<string>();

            if (materialId.HasValue)
            {
                if (doc.GetElement(new ElementId(materialId.Value)) is Material byId) return byId;
                throw new Exception($"在文件 '{doc.Title}' 找不到材質 ID: {materialId.Value}");
            }

            if (!string.IsNullOrWhiteSpace(materialName))
            {
                var matches = new FilteredElementCollector(doc)
                    .OfClass(typeof(Material))
                    .Cast<Material>()
                    .Where(m => m.Name.Equals(materialName, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (matches.Count == 1) return matches[0];
                if (matches.Count > 1) throw new Exception($"有 {matches.Count} 個材質叫 '{materialName}'，請改用 materialId。");
                throw new Exception($"在文件 '{doc.Title}' 找不到材質 '{materialName}'。");
            }

            throw new Exception("請提供 materialId 或 materialName。");
        }

        /// <summary>
        /// 解析填充樣式名稱。&lt;Solid fill&gt; 另外處理，因為它不論語系都用 IsSolidFill 判斷最可靠。
        /// 其餘沿用 ResolveFillPatternIdOrThrow（同名時優先 Model 樣式）。
        /// </summary>
        private ElementId ResolveMaterialGraphicsPattern(Document doc, string name)
        {
            string n = name.Trim();
            if (n.Equals("solid", StringComparison.OrdinalIgnoreCase) ||
                n.Equals("<Solid fill>", StringComparison.OrdinalIgnoreCase) ||
                n.Equals("Solid fill", StringComparison.OrdinalIgnoreCase) ||
                n == "實心填滿" || n == "<實心填滿>")
            {
                return GetSolidFillPatternId(doc);
            }
            return ResolveFillPatternIdOrThrow(doc, n);
        }

        private object DescribeMaterialGraphics(Document doc, Material m)
        {
            return new
            {
                MaterialId = m.Id.GetIdValue(),
                MaterialName = m.Name,
                UseRenderAppearanceForShading = m.UseRenderAppearanceForShading,
                ShadingColor = DescribeColor(m.Color),
                Transparency = m.Transparency,
                SurfaceForeground = new { Pattern = DescribePatternName(doc, m.SurfaceForegroundPatternId), Color = DescribeColor(m.SurfaceForegroundPatternColor) },
                SurfaceBackground = new { Pattern = DescribePatternName(doc, m.SurfaceBackgroundPatternId), Color = DescribeColor(m.SurfaceBackgroundPatternColor) },
                CutForeground = new { Pattern = DescribePatternName(doc, m.CutForegroundPatternId), Color = DescribeColor(m.CutForegroundPatternColor) },
                CutBackground = new { Pattern = DescribePatternName(doc, m.CutBackgroundPatternId), Color = DescribeColor(m.CutBackgroundPatternColor) }
            };
        }

        private static string DescribePatternName(Document doc, ElementId id)
        {
            if (id == null || id == ElementId.InvalidElementId) return null;
            return (doc.GetElement(id) as FillPatternElement)?.Name;
        }

        private static string DescribeColor(Color c)
        {
            if (c == null || !c.IsValid) return null;
            return $"RGB({c.Red},{c.Green},{c.Blue})";
        }

        private static Color ParseRgb(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null) return null;
            int r = token["r"]?.Value<int>() ?? throw new Exception("顏色缺少 r");
            int g = token["g"]?.Value<int>() ?? throw new Exception("顏色缺少 g");
            int b = token["b"]?.Value<int>() ?? throw new Exception("顏色缺少 b");
            return new Color((byte)Math.Max(0, Math.Min(255, r)), (byte)Math.Max(0, Math.Min(255, g)), (byte)Math.Max(0, Math.Min(255, b)));
        }
    }
}
