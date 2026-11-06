using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;

namespace RevitMCP.Core
{
    public partial class CommandExecutor
    {
        #region CAD 圖層建網格線

        /// <summary>預覽：不給 layerName 時列出 CAD 圖層（附直線數與建議圖層）；給了則回報將建立的網格線。</summary>
        private object PreviewDwgGrids(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;
            var vp = doc.ActiveView as ViewPlan;
            if (vp == null) throw new Exception("請在平面視圖中執行（CAD 需在該視圖可見）");

            string layerName = parameters["layerName"]?.Value<string>();
            if (string.IsNullOrEmpty(layerName))
                return ListCadLineLayers(doc, vp);

            return BuildDwgGrids(doc, vp, parameters, true);
        }

        private object CreateGridsFromDwg(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;
            var vp = doc.ActiveView as ViewPlan;
            if (vp == null) throw new Exception("請在平面視圖中執行（CAD 需在該視圖可見）");

            string layerName = parameters["layerName"]?.Value<string>();
            if (string.IsNullOrEmpty(layerName)) throw new Exception("必須提供 layerName，可先呼叫 preview_dwg_grids 查圖層");

            return BuildDwgGrids(doc, vp, parameters, false);
        }

        private object BuildDwgGrids(Document doc, ViewPlan vp, JObject p, bool dryRun)
        {
            string layerName = p["layerName"].Value<string>();
            double minLenMm = p["minLengthMm"]?.Value<double>() ?? 3000;
            double extendMm = p["extendMm"]?.Value<double>() ?? 0;
            bool verticalNumbers = !string.Equals(p["verticalLabels"]?.Value<string>(), "letters", StringComparison.OrdinalIgnoreCase);
            bool autoName = p["autoName"]?.Value<bool>() ?? true;

            var segs = CollectCadLines(doc, vp, layerName);
            if (segs.Count == 0)
                throw new Exception($"圖層「{layerName}」在目前視圖中找不到任何直線");

            var merged = MergeCollinear(segs);
            var kept = merged.Where(m => m.a.DistanceTo(m.b) * GridMmPerFt >= minLenMm).ToList();
            int tooShort = merged.Count - kept.Count;

            // 垂直向（|dy|>|dx|）由下往上、水平向由左往右，軸號圓圈落在終點端（上方／右方）
            var vertical = new List<(XYZ a, XYZ b)>();
            var horizontal = new List<(XYZ a, XYZ b)>();
            double ext = extendMm / GridMmPerFt;
            foreach (var (a0, b0) in kept)
            {
                var d = (b0 - a0).Normalize();
                bool isVertical = Math.Abs(d.Y) > Math.Abs(d.X);
                var a = a0; var b = b0;
                if ((isVertical && b.Y < a.Y) || (!isVertical && b.X < a.X)) { var t = a; a = b; b = t; d = d.Negate(); }
                a = a - d * ext; b = b + d * ext;
                (isVertical ? vertical : horizontal).Add((a, b));
            }
            vertical = vertical.OrderBy(l => (l.a.X + l.b.X) / 2).ToList();
            horizontal = horizontal.OrderBy(l => (l.a.Y + l.b.Y) / 2).ToList();

            var plans = new List<GridPlan>();
            for (int i = 0; i < vertical.Count; i++)
                plans.Add(new GridPlan { Start = vertical[i].a, End = vertical[i].b,
                    Name = autoName ? (verticalNumbers ? (i + 1).ToString() : GridLetter(i)) : null });
            for (int i = 0; i < horizontal.Count; i++)
                plans.Add(new GridPlan { Start = horizontal[i].a, End = horizontal[i].b,
                    Name = autoName ? (verticalNumbers ? GridLetter(i) : (i + 1).ToString()) : null });

            var results = ApplyGridPlans(doc, plans, dryRun, $"從CAD圖層建立網格線：{layerName}");

            return new
            {
                DryRun = dryRun,
                ViewName = vp.Name,
                LayerName = layerName,
                RawLineCount = segs.Count,
                MergedLineCount = merged.Count,
                FilteredTooShort = tooShort,
                MinLengthMm = minLenMm,
                VerticalCount = vertical.Count,
                HorizontalCount = horizontal.Count,
                Summary = results.GroupBy(r => r.Status).ToDictionary(g => g.Key, g => g.Count()),
                Grids = results,
                Message = dryRun
                    ? "預覽完成，未修改模型。確認無誤後用相同參數呼叫 create_grids_from_dwg。"
                    : $"已建立 {results.Count(r => r.Status == "created")} 條網格線"
            };
        }

        /// <summary>A..Z（跳過 I、O，避免與 1、0 混淆），超過 24 個後接 AA、AB…</summary>
        private static string GridLetter(int index)
        {
            const string letters = "ABCDEFGHJKLMNPQRSTUVWXYZ";
            string s = "";
            index++;
            while (index > 0)
            {
                index--;
                s = letters[index % letters.Length] + s;
                index /= letters.Length;
            }
            return s;
        }

        private static object ListCadLineLayers(Document doc, ViewPlan vp)
        {
            var cads = new FilteredElementCollector(doc, vp.Id).OfClass(typeof(ImportInstance)).Cast<ImportInstance>().ToList();
            if (cads.Count == 0) throw new Exception("目前視圖中找不到任何 CAD 連結或匯入");

            var counts = new Dictionary<string, int>();
            foreach (var (layer, _, _) in EnumerateCadLines(doc, vp, cads))
                counts[layer] = counts.TryGetValue(layer, out int n) ? n + 1 : 1;

            string[] keywords = { "grid", "axis", "軸", "網格", "柱心", "中心線" };
            var layers = counts.OrderBy(kv => kv.Key).Select(kv => new { Layer = kv.Key, LineCount = kv.Value }).ToList();
            var suggested = layers.Select(l => l.Layer)
                .FirstOrDefault(l => keywords.Any(k => l.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0));

            return new
            {
                ViewName = vp.Name,
                CadCount = cads.Count,
                CadNames = cads.Select(c => doc.GetElement(c.GetTypeId())?.Name).ToList(),
                LayerCount = layers.Count,
                Layers = layers,
                SuggestedLayer = suggested,
                Message = "只列出含直線的圖層。選定圖層後再以 layerName 呼叫 preview_dwg_grids 預覽。"
            };
        }

        private static List<(XYZ a, XYZ b)> CollectCadLines(Document doc, ViewPlan vp, string layerName)
        {
            var cads = new FilteredElementCollector(doc, vp.Id).OfClass(typeof(ImportInstance)).Cast<ImportInstance>().ToList();
            if (cads.Count == 0) throw new Exception("目前視圖中找不到任何 CAD 連結或匯入");
            return EnumerateCadLines(doc, vp, cads)
                .Where(x => x.layer == layerName)
                .Select(x => (x.a, x.b)).ToList();
        }

        /// <summary>列舉 CAD 中所有直線段（含 PolyLine 拆段、圖塊內一層），座標已是專案座標、Z 壓平為 0。</summary>
        private static IEnumerable<(string layer, XYZ a, XYZ b)> EnumerateCadLines(Document doc, ViewPlan vp, List<ImportInstance> cads)
        {
            var opts = new Options { View = vp };
            foreach (var cad in cads)
            {
                var ge = cad.get_Geometry(opts);
                if (ge == null) continue;
                foreach (var go in ge)
                {
                    if (!(go is GeometryInstance gi)) continue;
                    var ig = gi.GetInstanceGeometry();
                    if (ig == null) continue;
                    foreach (var obj in ig)
                    {
                        if (obj is GeometryInstance block)
                        {
                            var bg = block.GetInstanceGeometry();
                            if (bg == null) continue;
                            foreach (var inner in bg)
                                foreach (var x in LinesOf(doc, inner)) yield return x;
                            continue;
                        }
                        foreach (var x in LinesOf(doc, obj)) yield return x;
                    }
                }
            }
        }

        private static IEnumerable<(string layer, XYZ a, XYZ b)> LinesOf(Document doc, GeometryObject obj)
        {
            if (obj.GraphicsStyleId == ElementId.InvalidElementId) yield break;
            var layer = (doc.GetElement(obj.GraphicsStyleId) as GraphicsStyle)?.GraphicsStyleCategory?.Name;
            if (layer == null) yield break;

            if (obj is Line ln && ln.IsBound)
            {
                yield return (layer, Flat(ln.GetEndPoint(0)), Flat(ln.GetEndPoint(1)));
            }
            else if (obj is PolyLine pl)
            {
                var pts = pl.GetCoordinates();
                for (int i = 0; i + 1 < pts.Count; i++)
                    if (pts[i].DistanceTo(pts[i + 1]) > 1e-6)
                        yield return (layer, Flat(pts[i]), Flat(pts[i + 1]));
            }
        }

        private static XYZ Flat(XYZ p) => new XYZ(p.X, p.Y, 0);

        /// <summary>同一無限直線上的線段（平行且垂距 &lt; 10mm）合併為一條，取投影最遠兩端；處理被軸號圓圈打斷的軸線。</summary>
        private static List<(XYZ a, XYZ b)> MergeCollinear(List<(XYZ a, XYZ b)> segs)
        {
            var groups = new List<(XYZ origin, XYZ dir, double min, double max)>();
            foreach (var (a, b) in segs.OrderByDescending(s => s.a.DistanceTo(s.b)))
            {
                if (a.DistanceTo(b) < 1e-6) continue;
                var d = (b - a).Normalize();
                int hit = -1;
                for (int i = 0; i < groups.Count; i++)
                {
                    var g = groups[i];
                    if (Math.Abs(g.dir.CrossProduct(d).Z) > 0.001) continue;
                    var perp = new XYZ(-g.dir.Y, g.dir.X, 0);
                    if (Math.Abs((a - g.origin).DotProduct(perp)) > GridDupTolFt) continue;
                    if (Math.Abs((b - g.origin).DotProduct(perp)) > GridDupTolFt) continue;
                    hit = i; break;
                }
                if (hit < 0)
                {
                    groups.Add((a, d, 0, (b - a).DotProduct(d)));
                }
                else
                {
                    var g = groups[hit];
                    double s1 = (a - g.origin).DotProduct(g.dir), s2 = (b - g.origin).DotProduct(g.dir);
                    groups[hit] = (g.origin, g.dir, Math.Min(g.min, Math.Min(s1, s2)), Math.Max(g.max, Math.Max(s1, s2)));
                }
            }
            return groups.Select(g => (g.origin + g.dir * g.min, g.origin + g.dir * g.max)).ToList();
        }

        #endregion
    }
}
