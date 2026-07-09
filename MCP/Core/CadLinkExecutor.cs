using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

// Revit 2025+ ElementId: int → long
#if REVIT2025_OR_GREATER
using IdType = System.Int64;
#else
using IdType = System.Int32;
#endif

namespace RevitMCP.Core
{
    /// <summary>
    /// 將 CAD（DWG/DXF）檔案連結到指定 Revit 視圖。
    ///
    /// 對應 Revit API 2026：
    ///   bool Document.Link(string file, DWGImportOptions options, View pDBView, out ElementId elementId)
    ///
    /// View 行為：
    ///   - thisViewOnly=true → 連結僅在該視圖可見（3D 視圖不支援）
    ///   - thisViewOnly=false→ View 仍用於提供 Level 參考點
    /// </summary>
    public static class CadLinkExecutor
    {
        public static object LinkCadToView(Document doc, JObject p)
        {
            // ── 1. 解析檔案路徑 ─────────────────────────
            string filePath = p["filePath"]?.Value<string>();
            if (string.IsNullOrWhiteSpace(filePath))
                throw new Exception("必須提供 filePath（CAD 檔案絕對路徑）");
            if (!File.Exists(filePath))
                throw new Exception($"找不到 CAD 檔案：{filePath}");

            string ext = Path.GetExtension(filePath).ToLowerInvariant();
            if (ext != ".dwg" && ext != ".dxf")
                throw new Exception($"檔案副檔名必須是 .dwg 或 .dxf，目前為 {ext}");

            // ── 2. 解析目標視圖（viewId 優先，viewName 次之）──
            View targetView = ResolveTargetView(doc, p);

            // ── 3. 建立 DWGImportOptions ────────────────
            bool thisViewOnly = p["thisViewOnly"]?.Value<bool>() ?? true;

            // 3D 視圖不支援 ThisViewOnly
            if (thisViewOnly && targetView is View3D)
                throw new Exception("3D 視圖不支援 thisViewOnly=true，請改用 2D 視圖或將 thisViewOnly 設為 false");

            var opts = new DWGImportOptions
            {
                ThisViewOnly = thisViewOnly,
                Placement    = ParsePlacement(p["placement"]?.Value<string>()),
                Unit         = ParseUnit(p["unit"]?.Value<string>()),
                ColorMode    = ParseColorMode(p["colorMode"]?.Value<string>()),
                VisibleLayersOnly         = p["visibleLayersOnly"]?.Value<bool>() ?? false,
                OrientToView              = p["orientToView"]?.Value<bool>() ?? false,
                AutoCorrectAlmostVHLines  = p["autoCorrectAlmostVHLines"]?.Value<bool>() ?? true,
            };

            // 互斥規則：OrientToView 需要 ThisViewOnly=false
            if (opts.OrientToView && opts.ThisViewOnly)
                throw new Exception("orientToView=true 需要 thisViewOnly=false");

            // 自訂縮放（>0 才生效，否則使用 Unit）
            double? customScale = p["customScale"]?.Value<double?>();
            if (customScale.HasValue && customScale.Value > 0)
                opts.CustomScale = customScale.Value;

            // 參考點（mm → ft）
            var rp = p["referencePoint"] as JObject;
            if (rp != null)
            {
                double rx = rp["x"]?.Value<double>() ?? 0;
                double ry = rp["y"]?.Value<double>() ?? 0;
                double rz = rp["z"]?.Value<double>() ?? 0;
                opts.ReferencePoint = new XYZ(rx / 304.8, ry / 304.8, rz / 304.8);
            }

            // ── 4. 連結 ─────────────────────────────
            ElementId linkedId;
            bool ok;
            using (var t = new Transaction(doc, "Link CAD to View"))
            {
                t.Start();
                ok = doc.Link(filePath, opts, targetView, out linkedId);
                t.Commit();
            }

            if (!ok || linkedId == null || linkedId == ElementId.InvalidElementId)
                throw new Exception("Document.Link 回傳失敗，請檢查檔案、視圖與 options 設定");

            // ── 5. 回傳結果 ─────────────────────────
            var linked = doc.GetElement(linkedId);
            return new
            {
                linkedInstanceId = linkedId.GetIdValue(),
                fileName         = Path.GetFileName(filePath),
                filePath         = filePath,
                viewId           = targetView.Id.GetIdValue(),
                viewName         = targetView.Name,
                viewType         = targetView.ViewType.ToString(),
                thisViewOnly     = opts.ThisViewOnly,
                placement        = opts.Placement.ToString(),
                unit             = opts.Unit.ToString(),
                colorMode        = opts.ColorMode.ToString(),
                visibleLayersOnly         = opts.VisibleLayersOnly,
                orientToView              = opts.OrientToView,
                autoCorrectAlmostVHLines  = opts.AutoCorrectAlmostVHLines,
                customScale               = opts.CustomScale,
                message = $"成功連結 {Path.GetFileName(filePath)} 到視圖「{targetView.Name}」（ID: {linkedId.GetIdValue()}）"
            };
        }

        // ────────────────────────────────────────────
        // Helpers
        // ────────────────────────────────────────────

        private static View ResolveTargetView(Document doc, JObject p)
        {
            IdType? viewId = p["viewId"]?.Value<IdType?>();
            string viewName = p["viewName"]?.Value<string>();

            // 優先用 viewId
            if (viewId.HasValue && viewId.Value > 0)
            {
                var v = doc.GetElement(new ElementId(viewId.Value)) as View;
                if (v == null)
                    throw new Exception($"找不到視圖 ID: {viewId.Value}");
                if (v.IsTemplate)
                    throw new Exception($"視圖 ID {viewId.Value} 是視圖樣板，不可作為 Link 目標");
                return v;
            }

            // 次之用 viewName（精確 → 包含 → 候選清單）
            if (!string.IsNullOrWhiteSpace(viewName))
            {
                var allViews = new FilteredElementCollector(doc)
                    .OfClass(typeof(View))
                    .Cast<View>()
                    .Where(v => !v.IsTemplate)
                    .ToList();

                var exact = allViews.FirstOrDefault(v => v.Name == viewName);
                if (exact != null) return exact;

                var contains = allViews.Where(v => v.Name.IndexOf(viewName, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
                if (contains.Count == 1) return contains[0];

                if (contains.Count > 1)
                {
                    var candidates = string.Join(", ", contains.Select(v => $"'{v.Name}' (ID={v.Id.GetIdValue()})"));
                    throw new Exception($"視圖名稱「{viewName}」對應多個視圖，請改用 viewId。候選：{candidates}");
                }

                throw new Exception($"找不到視圖名稱：{viewName}");
            }

            throw new Exception("必須提供 viewId 或 viewName 其中一個");
        }

        private static ImportPlacement ParsePlacement(string s)
        {
            if (string.IsNullOrEmpty(s)) return ImportPlacement.Origin;
            if (Enum.TryParse<ImportPlacement>(s, ignoreCase: true, out var v)) return v;
            throw new Exception($"不支援的 placement：{s}（可用：Origin / Centered / Site / Shared / DefaultLocation）");
        }

        private static ImportUnit ParseUnit(string s)
        {
            if (string.IsNullOrEmpty(s)) return ImportUnit.Default;
            if (Enum.TryParse<ImportUnit>(s, ignoreCase: true, out var v)) return v;
            throw new Exception($"不支援的 unit：{s}（可用：Default / Foot / Inch / Meter / Decimeter / Centimeter / Millimeter / Custom）");
        }

        private static ImportColorMode ParseColorMode(string s)
        {
            if (string.IsNullOrEmpty(s)) return ImportColorMode.BlackAndWhite;
            if (Enum.TryParse<ImportColorMode>(s, ignoreCase: true, out var v)) return v;
            throw new Exception($"不支援的 colorMode：{s}（可用：BlackAndWhite / Preserved / InvertColors）");
        }

        // ════════════════════════════════════════════════════════════════════
        // 批次連結：多個 CAD 依檔名樓層代碼匹配視圖，並對齊 Grid 交點
        // ════════════════════════════════════════════════════════════════════

        public static object LinkCadsByFloor(Document doc, JObject p)
        {
            // ── 1. 解析必填參數 ─────────────────────────
            var items = p["items"] as JArray;
            if (items == null || items.Count == 0)
                throw new Exception("必須提供 items 陣列（每筆含 filePath 與 cadAnchorPoint）");

            string gridLabelX = p["gridLabelX"]?.Value<string>();
            string gridLabelY = p["gridLabelY"]?.Value<string>();
            if (string.IsNullOrWhiteSpace(gridLabelX) || string.IsNullOrWhiteSpace(gridLabelY))
                throw new Exception("必須提供 gridLabelX 與 gridLabelY（Revit Grid 標籤）");

            // ── 2. 求 Revit 端錨點（兩條 Grid 的交點） ─────
            XYZ revitAnchorFt = ComputeGridIntersection(doc, gridLabelX, gridLabelY);

            // ── 3. 預讀所有 FloorPlan ─────────────────
            var floorPlans = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewPlan))
                .Cast<ViewPlan>()
                .Where(v => !v.IsTemplate && v.ViewType == ViewType.FloorPlan)
                .ToList();

            if (floorPlans.Count == 0)
                throw new Exception("專案中找不到任何平面視圖（FloorPlan）");

            // ── 4. 共用 DWGImportOptions（強制 Origin + 不旋轉，由 MoveElement 對齊） ──
            bool thisViewOnly = p["thisViewOnly"]?.Value<bool>() ?? true;
            var opts = new DWGImportOptions
            {
                ThisViewOnly             = thisViewOnly,
                Placement                = ImportPlacement.Origin,
                Unit                     = ParseUnit(p["unit"]?.Value<string>()),
                ColorMode                = ParseColorMode(p["colorMode"]?.Value<string>()),
                VisibleLayersOnly        = p["visibleLayersOnly"]?.Value<bool>() ?? false,
                OrientToView             = false,
                AutoCorrectAlmostVHLines = p["autoCorrectAlmostVHLines"]?.Value<bool>() ?? true,
            };

            // ── 5. 逐筆處理（共用單一 Transaction） ──────
            var results = new List<object>();
            using (var trans = new Transaction(doc, "Batch Link CADs by Floor"))
            {
                trans.Start();

                for (int i = 0; i < items.Count; i++)
                {
                    var item = items[i] as JObject;
                    string filePath = item?["filePath"]?.Value<string>();

                    try
                    {
                        if (item == null)
                            throw new Exception("item 不是物件");
                        if (string.IsNullOrWhiteSpace(filePath))
                            throw new Exception("缺 filePath");
                        if (!File.Exists(filePath))
                            throw new Exception("檔案不存在");

                        string ext = Path.GetExtension(filePath).ToLowerInvariant();
                        if (ext != ".dwg" && ext != ".dxf")
                            throw new Exception($"副檔名必須是 .dwg 或 .dxf，目前為 {ext}");

                        // 5a. 抽樓層代碼
                        string baseName = Path.GetFileNameWithoutExtension(filePath);
                        string floorCode = ExtractFloorCode(baseName);
                        if (floorCode == null)
                        {
                            results.Add(new
                            {
                                index = i,
                                filePath,
                                status = "skipped",
                                reason = $"從檔名「{baseName}」抽不到樓層代碼"
                            });
                            continue;
                        }

                        // 5b. 找 FloorPlan：先精確、再 contains
                        var view = floorPlans.FirstOrDefault(v =>
                            string.Equals(v.Name, floorCode, StringComparison.OrdinalIgnoreCase));
                        if (view == null)
                        {
                            var candidates = floorPlans
                                .Where(v => v.Name.IndexOf(floorCode, StringComparison.OrdinalIgnoreCase) >= 0)
                                .ToList();
                            if (candidates.Count == 1) view = candidates[0];
                            else if (candidates.Count > 1)
                            {
                                results.Add(new
                                {
                                    index = i, filePath, floorCode, status = "skipped",
                                    reason = $"視圖名稱含「{floorCode}」有多筆：{string.Join(", ", candidates.Select(v => v.Name))}"
                                });
                                continue;
                            }
                        }
                        if (view == null)
                        {
                            results.Add(new
                            {
                                index = i, filePath, floorCode, status = "skipped",
                                reason = $"找不到名稱含「{floorCode}」的 FloorPlan"
                            });
                            continue;
                        }

                        // 5c. CAD 錨點（mm → ft）
                        var anchor = item["cadAnchorPoint"] as JObject;
                        if (anchor == null)
                            throw new Exception("缺 cadAnchorPoint");
                        double cax = anchor["x"]?.Value<double>() ?? 0;
                        double cay = anchor["y"]?.Value<double>() ?? 0;
                        XYZ cadAnchorFt = new XYZ(cax / 304.8, cay / 304.8, 0);

                        // 5d. Link
                        ElementId linkedId;
                        bool ok = doc.Link(filePath, opts, view, out linkedId);
                        if (!ok || linkedId == null || linkedId == ElementId.InvalidElementId)
                            throw new Exception("Document.Link 回傳 false");

                        // 5e. 平移對齊（translation = revit錨點 - cad錨點）
                        XYZ translation = revitAnchorFt - cadAnchorFt;
                        if (translation.GetLength() > 1e-9)
                            ElementTransformUtils.MoveElement(doc, linkedId, translation);

                        results.Add(new
                        {
                            index = i,
                            filePath,
                            fileName = Path.GetFileName(filePath),
                            floorCode,
                            viewId = view.Id.GetIdValue(),
                            viewName = view.Name,
                            linkedInstanceId = linkedId.GetIdValue(),
                            translationMm = new
                            {
                                x = Math.Round(translation.X * 304.8, 2),
                                y = Math.Round(translation.Y * 304.8, 2)
                            },
                            status = "linked"
                        });
                    }
                    catch (Exception ex)
                    {
                        results.Add(new { index = i, filePath, status = "failed", reason = ex.Message });
                    }
                }

                trans.Commit();
            }

            int linked  = results.Count(r => GetStatus(r) == "linked");
            int skipped = results.Count(r => GetStatus(r) == "skipped");
            int failed  = results.Count(r => GetStatus(r) == "failed");

            return new
            {
                gridLabelX,
                gridLabelY,
                revitAnchorMm = new
                {
                    x = Math.Round(revitAnchorFt.X * 304.8, 2),
                    y = Math.Round(revitAnchorFt.Y * 304.8, 2)
                },
                total   = items.Count,
                linked,
                skipped,
                failed,
                results,
                message = $"批次連結完成：成功 {linked} / 略過 {skipped} / 失敗 {failed}（共 {items.Count}）"
            };
        }

        private static string GetStatus(object r)
        {
            var prop = r.GetType().GetProperty("status");
            return prop?.GetValue(r) as string;
        }

        /// <summary>
        /// 求兩條直線 Grid 的 XY 平面交點（Z=0）。不支援弧形 Grid。
        /// </summary>
        private static XYZ ComputeGridIntersection(Document doc, string labelX, string labelY)
        {
            var grids = new FilteredElementCollector(doc)
                .OfClass(typeof(Grid))
                .Cast<Grid>()
                .ToList();

            var g1 = grids.FirstOrDefault(g => string.Equals(g.Name, labelX, StringComparison.OrdinalIgnoreCase));
            var g2 = grids.FirstOrDefault(g => string.Equals(g.Name, labelY, StringComparison.OrdinalIgnoreCase));
            if (g1 == null) throw new Exception($"找不到 Grid「{labelX}」");
            if (g2 == null) throw new Exception($"找不到 Grid「{labelY}」");

            var l1 = g1.Curve as Line;
            var l2 = g2.Curve as Line;
            if (l1 == null || l2 == null)
                throw new Exception("僅支援直線 Grid（不支援弧形 Grid）");

            XYZ p1 = l1.Origin, d1 = l1.Direction;
            XYZ p2 = l2.Origin, d2 = l2.Direction;

            // d1 × d2 的 Z 分量；若為 0 則平行
            double cross = d1.X * d2.Y - d1.Y * d2.X;
            if (Math.Abs(cross) < 1e-9)
                throw new Exception($"Grid「{labelX}」與「{labelY}」平行，無交點");

            XYZ dp = new XYZ(p2.X - p1.X, p2.Y - p1.Y, 0);
            double t = (dp.X * d2.Y - dp.Y * d2.X) / cross;
            return new XYZ(p1.X + t * d1.X, p1.Y + t * d1.Y, 0);
        }

        /// <summary>
        /// 從 CAD 檔名抽樓層代碼，正規化為 FL{n} / B{n} / RF / {n}F 形式。
        /// 抽不到時回 null。
        /// </summary>
        private static string ExtractFloorCode(string fileName)
        {
            // 優先順序：FL → B+數字 → RF → 地下n → n樓 → n F（避免「11F辦公室」被誤抓成「1F」要靠 \b 邊界）
            Match m;

            m = Regex.Match(fileName, @"FL(\d+)", RegexOptions.IgnoreCase);
            if (m.Success) return "FL" + m.Groups[1].Value;

            m = Regex.Match(fileName, @"B(\d+)F?", RegexOptions.IgnoreCase);
            if (m.Success) return "B" + m.Groups[1].Value;

            m = Regex.Match(fileName, @"\bRF\b", RegexOptions.IgnoreCase);
            if (m.Success) return "RF";

            m = Regex.Match(fileName, @"地下(\d+)樓?");
            if (m.Success) return "B" + m.Groups[1].Value;

            m = Regex.Match(fileName, @"(\d+)樓");
            if (m.Success) return m.Groups[1].Value + "F";

            // \d+F 放最後（最寬鬆）
            m = Regex.Match(fileName, @"(\d+)F", RegexOptions.IgnoreCase);
            if (m.Success) return m.Groups[1].Value + "F";

            return null;
        }
    }
}
