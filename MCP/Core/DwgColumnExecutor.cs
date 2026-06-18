using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

namespace RevitMCP.Core
{
    /// <summary>
    /// DWG 柱建立工具集
    ///   - get_dwg_column_layers : 取得 CAD 圖層清單
    ///   - preview_dwg_columns   : 預覽指定圖層解析出的柱資訊
    ///   - create_columns_from_dwg : 從 CAD 圖層建立結構柱或建築柱
    /// </summary>
    public static class DwgColumnExecutor
    {
        const double FtMm = 304.8;
        const double MmFt = 1.0 / 304.8;
        const double Tol = 5.0;

        // ────────────────────────────────────────────────
        // 入口：取得圖層清單
        // ────────────────────────────────────────────────
        public static object GetDwgColumnLayers(Document doc)
        {
            var vp = doc.ActiveView as ViewPlan;
            if (vp == null) throw new Exception("請在平面視圖中執行");

            var cads = new FilteredElementCollector(doc, vp.Id)
                .OfClass(typeof(ImportInstance))
                .Cast<ImportInstance>()
                .ToList();
            if (cads.Count == 0) throw new Exception("目前視圖中找不到任何 CAD 連結或匯入");

            var layerNames = new HashSet<string>();
            var opts = new Options { ComputeReferences = true, View = vp };

            foreach (var cad in cads)
            {
                var ge = cad.get_Geometry(opts);
                if (ge == null) continue;
                foreach (var go in ge)
                {
                    var gi = go as GeometryInstance;
                    if (gi == null) continue;
                    var ig = gi.GetInstanceGeometry();
                    if (ig == null) continue;
                    foreach (var obj in ig)
                    {
                        if (obj.GraphicsStyleId == ElementId.InvalidElementId) continue;
                        var gs = doc.GetElement(obj.GraphicsStyleId) as GraphicsStyle;
                        if (gs?.GraphicsStyleCategory == null) continue;
                        layerNames.Add(gs.GraphicsStyleCategory.Name);
                    }
                }
            }

            if (layerNames.Count == 0) throw new Exception("無法從 CAD 讀取任何圖層");

            var sortedLayers = layerNames.OrderBy(n => n).ToList();

            string[] columnKeywords = { "柱", "column", "col", "pillar" };
            var suggested = sortedLayers.FirstOrDefault(l =>
                columnKeywords.Any(k => l.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0));

            return new
            {
                viewName = vp.Name,
                cadCount = cads.Count,
                layerCount = sortedLayers.Count,
                layers = sortedLayers,
                suggestedLayer = suggested
            };
        }

        // ────────────────────────────────────────────────
        // 入口：預覽
        // ────────────────────────────────────────────────
        public static object PreviewDwgColumns(Document doc, JObject p)
        {
            var vp = doc.ActiveView as ViewPlan;
            if (vp == null) throw new Exception("請在平面視圖中執行");

            string layerName = p["layerName"]?.Value<string>();
            if (string.IsNullOrEmpty(layerName)) throw new Exception("必須提供 layerName 參數");

            var geoms = CollectLayerGeometry(doc, vp, layerName);
            if (geoms.Count == 0) throw new Exception($"圖層「{layerName}」中找不到幾何物件");

            var diag = new List<string>();
            var cols = Extract(geoms, diag);

            var colList = cols.Select(c => new
            {
                x_mm = Math.Round(c.X * FtMm, 1),
                y_mm = Math.Round(c.Y * FtMm, 1),
                width_mm = Math.Round(c.W, 0),
                depth_mm = Math.Round(c.D, 0),
                rotation_deg = Math.Round(c.A * 180.0 / Math.PI, 2)
            }).ToList();

            var sizeGroups = cols
                .GroupBy(c => $"{(int)Math.Round(c.W)}x{(int)Math.Round(c.D)}")
                .Select(g => new { size = g.Key, count = g.Count() })
                .ToList();

            // ── 前端單位/放樣健檢（純回報、不改模型）─────────────────────────
            // 定位放樣、尺寸、單位是「參考 DWG/DXF 建模」的第一要務：在建柱前的斷點
            // 讓使用者先確認這些數字合理，避免單位連結錯誤導致整批 10x 偏差的錯誤建模。
            object preflight = null;
            if (cols.Count > 0)
            {
                var warnings = new List<string>();
                int tooSmall = cols.Count(c => c.W < 100 || c.D < 100);
                int tooLarge = cols.Count(c => c.W > 3000 || c.D > 3000);
                if (tooSmall > 0)
                    warnings.Add($"{tooSmall} 根斷面 < 100mm：連結單位可能偏小（DXF 實為 cm/m 卻以 mm 連結？）請於連結對話框改正單位後重做。");
                if (tooLarge > 0)
                    warnings.Add($"{tooLarge} 根斷面 > 3000mm：連結單位可能偏大（DXF 實為 mm 卻以 cm/m 連結？）請於連結對話框改正單位後重做。");
                double xMin = cols.Min(c => c.X) * FtMm, xMax = cols.Max(c => c.X) * FtMm;
                double yMin = cols.Min(c => c.Y) * FtMm, yMax = cols.Max(c => c.Y) * FtMm;
                preflight = new
                {
                    unitSanity = warnings.Count == 0 ? "ok" : "check",
                    sizeRangeMm = new
                    {
                        minSide = (int)Math.Round(cols.Min(c => Math.Min(c.W, c.D))),
                        maxSide = (int)Math.Round(cols.Max(c => Math.Max(c.W, c.D)))
                    },
                    extentMm = new
                    {
                        width = (int)Math.Round(xMax - xMin),
                        height = (int)Math.Round(yMax - yMin),
                        xMin = (int)Math.Round(xMin),
                        yMin = (int)Math.Round(yMin)
                    },
                    warnings = warnings
                };
            }

            return new
            {
                layerName = layerName,
                count = cols.Count,
                sizeSummary = sizeGroups,
                preflight = preflight,
                columns = colList,
                debug = diag,
                message = cols.Count == 0 ? $"圖層「{layerName}」中沒有識別到封閉矩形（debug 欄位有收集到的型別與原始尺寸）" : null
            };
        }

        // ────────────────────────────────────────────────
        // 入口：建立柱
        // ────────────────────────────────────────────────
        public static object CreateColumnsFromDwg(Document doc, JObject p)
        {
            var vp = doc.ActiveView as ViewPlan;
            if (vp == null) throw new Exception("請在平面視圖中執行");

            string layerName = p["layerName"]?.Value<string>();
            if (string.IsNullOrEmpty(layerName)) throw new Exception("必須提供 layerName 參數");

            string columnTypeStr = p["columnType"]?.Value<string>() ?? "structural";
            bool isStructural = !columnTypeStr.Equals("architectural", StringComparison.OrdinalIgnoreCase);
            string columnTypeName = isStructural ? "結構柱" : "建築柱";

            BuiltInCategory bic = isStructural
                ? BuiltInCategory.OST_StructuralColumns
                : BuiltInCategory.OST_Columns;
            StructuralType stype = isStructural ? StructuralType.Column : StructuralType.NonStructural;

            string requestedFamilyName = p["familyName"]?.Value<string>();
            string textLayerName = p["textLayerName"]?.Value<string>();

            var bLv = vp.GenLevel;
            if (bLv == null) throw new Exception("無法取得基準樓層");

            var tLv = new FilteredElementCollector(doc)
                .OfClass(typeof(Level)).Cast<Level>()
                .Where(l => l.Elevation > bLv.Elevation + 0.001)
                .OrderBy(l => l.Elevation).FirstOrDefault();
            if (tLv == null) throw new Exception($"找不到高於「{bLv.Name}」的樓層");

            var geoms = CollectLayerGeometry(doc, vp, layerName);
            if (geoms.Count == 0) throw new Exception($"圖層「{layerName}」中找不到幾何物件");

            var cols = Extract(geoms, null);
            if (cols.Count == 0) throw new Exception($"圖層「{layerName}」中無封閉矩形");

            string wp = null, dp = null;
            FamilySymbol baseSym;
            if (!string.IsNullOrEmpty(requestedFamilyName))
            {
                baseSym = FindFamilyByName(doc, bic, requestedFamilyName, ref wp, ref dp);
                if (baseSym == null)
                    throw new Exception($"找不到名稱為「{requestedFamilyName}」的{columnTypeName}族群，請確認族群已載入且名稱正確");
            }
            else
            {
                baseSym = FindFamily(doc, bic, ref wp, ref dp);
                if (baseSym == null)
                    throw new Exception($"找不到{columnTypeName}族群，請先在專案中載入矩形柱族群");
            }

            // 讀取 CAD 文字標注（如 C1、C2）
            List<LabelData> labels = null;
            string labelStatus = "skipped"; // skipped | ok | no_oda | error | no_worker
            string labelWarning = null;

            if (!string.IsNullOrEmpty(textLayerName))
            {
                try
                {
                    labels = ReadLabelsFromCad(doc, vp, textLayerName, layerName);
                    labelStatus = labels != null ? "ok" : "no_worker";
                    if (labels == null)
                        labelWarning = "找不到 ezdxf_worker.py 或無法取得 CAD 檔案路徑，柱已建立但未對應柱名稱";
                }
                catch (Exception ex) when (ex.Message.StartsWith("NO_ODA:"))
                {
                    labelStatus = "no_oda";
                    labelWarning = "DWG 格式需要 ODA File Converter 才能讀取文字標注。"
                        + "請安裝後重試，或將 CAD 連結改為 DXF 格式。"
                        + "柱已建立但未對應柱名稱。";
                }
                catch (Exception ex)
                {
                    labelStatus = "error";
                    labelWarning = "讀取 CAD 文字標注時發生錯誤：" + ex.Message + "。柱已建立但未對應柱名稱。";
                }
            }

            int ok = 0, fail = 0;
            var errors = new List<string>();
            var typeMapping = new List<string>();
            var unmatchedLabels = new List<string>();
            var matchDebug = new List<string>(); // label→type→dist，最多10筆

            using (var tr = new Transaction(doc, $"從DWG建立{columnTypeName}"))
            {
                tr.Start();
                if (!baseSym.IsActive) { baseSym.Activate(); doc.Regenerate(); }

                foreach (var c in cols)
                {
                    try
                    {
                        FamilySymbol sym;
                        string matchedLabel = null;
                        if (labels != null && labels.Count > 0)
                        {
                            // 找距離最近的文字標注（容許範圍 2000mm）
                            var nearest = labels
                                .OrderBy(l => (c.X - l.X) * (c.X - l.X) + (c.Y - l.Y) * (c.Y - l.Y))
                                .First();
                            double distMm = Math.Sqrt(
                                Math.Pow(c.X - nearest.X, 2) + Math.Pow(c.Y - nearest.Y, 2)) * FtMm;
                            if (matchDebug.Count < 6)
                                matchDebug.Add($"col=({c.X * FtMm:F0},{c.Y * FtMm:F0})mm lbl={nearest.Text} raw=({nearest.RawX:F1},{nearest.RawY:F1}) lblPos=({nearest.X * FtMm:F0},{nearest.Y * FtMm:F0})mm dist={distMm:F0}mm");
                            if (distMm < 2000)
                            {
                                matchedLabel = nearest.Text;
                                sym = MatchSymbolByLabel(doc, baseSym, nearest.Text, wp, dp, c.W, c.D);
                                if (sym == null)
                                {
                                    // 找不到對應類型：記錄 unmatched，跳過此柱（不自動建立）
                                    string unmatchedKey = nearest.Text;
                                    if (!unmatchedLabels.Contains(unmatchedKey))
                                        unmatchedLabels.Add(unmatchedKey);
                                    fail++;
                                    continue;
                                }
                            }
                            else
                            {
                                sym = GetOrCreate(doc, baseSym, c.W, c.D, wp, dp);
                            }
                        }
                        else
                        {
                            sym = GetOrCreate(doc, baseSym, c.W, c.D, wp, dp);
                        }

                        if (sym == null)
                        {
                            fail++;
                            errors.Add($"無法建立族群類型 {(int)Math.Round(c.W / 10.0)}x{(int)Math.Round(c.D / 10.0)}cm");
                            continue;
                        }
                        if (!sym.IsActive) { sym.Activate(); doc.Regenerate(); }

                        var loc = new XYZ(c.X, c.Y, bLv.Elevation);
                        var inst = doc.Create.NewFamilyInstance(loc, sym, bLv, stype);
                        if (inst != null)
                        {
                            SetParam(inst, BuiltInParameter.FAMILY_TOP_LEVEL_PARAM, tLv.Id);
                            SetParam(inst, BuiltInParameter.FAMILY_TOP_LEVEL_OFFSET_PARAM, 0.0);
                            SetParam(inst, BuiltInParameter.FAMILY_BASE_LEVEL_OFFSET_PARAM, 0.0);

                            if (Math.Abs(c.A) > 0.001)
                            {
                                var ax = Line.CreateBound(loc, loc + XYZ.BasisZ);
                                ElementTransformUtils.RotateElement(doc, inst.Id, ax, c.A);
                            }
                            ok++;
                            if (typeMapping.Count < 10 && !typeMapping.Contains(sym.Name))
                                typeMapping.Add(sym.Name);
                        }
                    }
                    catch (Exception ex)
                    {
                        fail++;
                        errors.Add(ex.Message);
                    }
                }
                tr.Commit();
            }

            return new
            {
                columnType = columnTypeName,
                familyName = baseSym.Family.Name,
                familyNameMatched = !string.IsNullOrEmpty(requestedFamilyName),
                labelReadStatus = labelStatus,
                labelCount = labels?.Count,
                labelWarning = labelWarning,
                labelsPreview = labels?.Take(6).Select(l => l.Text).ToList(),
                widthParam = wp,
                depthParam = dp,
                baseLevel = bLv.Name,
                topLevel = tLv.Name,
                totalDetected = cols.Count,
                created = ok,
                failed = fail,
                typesUsed = typeMapping,
                unmatchedLabels = unmatchedLabels,
                matchDebug = matchDebug,
                errors = errors.Take(10).ToList()
            };
        }

        // ────────────────────────────────────────────────
        // 蒐集指定圖層的幾何物件
        // ────────────────────────────────────────────────
        static List<GeometryObject> CollectLayerGeometry(Document doc, ViewPlan vp, string layerName)
        {
            var result = new List<GeometryObject>();
            var cads = new FilteredElementCollector(doc, vp.Id)
                .OfClass(typeof(ImportInstance)).Cast<ImportInstance>().ToList();
            var opts = new Options { ComputeReferences = true, View = vp };

            foreach (var cad in cads)
            {
                var ge = cad.get_Geometry(opts);
                if (ge == null) continue;
                foreach (var go in ge)
                {
                    var gi = go as GeometryInstance;
                    if (gi == null) continue;
                    var ig = gi.GetInstanceGeometry();
                    if (ig == null) continue;
                    foreach (var obj in ig)
                    {
                        // 圖塊(INSERT)一律收集，交由 FromBlockInstance 以點群判斷
                        //（圖塊本身 GraphicsStyleId 常為 Invalid，無法靠 style 過濾）
                        if (obj is GeometryInstance) { result.Add(obj); continue; }
                        if (obj.GraphicsStyleId == ElementId.InvalidElementId) continue;
                        var gs = doc.GetElement(obj.GraphicsStyleId) as GraphicsStyle;
                        if (gs?.GraphicsStyleCategory?.Name == layerName)
                            result.Add(obj);
                    }
                }
            }
            return result;
        }

        // ────────────────────────────────────────────────
        // 矩形解析
        // ────────────────────────────────────────────────
        static List<ColData> Extract(List<GeometryObject> geoms, List<string> diag)
        {
            var res = new List<ColData>();
            foreach (var obj in geoms)
            {
                if (obj is PolyLine pl)
                {
                    var pts = pl.GetCoordinates().ToList();
                    var c = MakeRect(pts);                          // 嚴格矩形優先（向後相容）
                    if (c == null && pts.Count >= 3)                // 退而求其次：通用外接矩形
                        c = BuildColFromPoints(pts, LongestEdgeAngle(pts), diag, "PolyLine(" + pts.Count + "pt)");
                    if (c != null) res.Add(c);
                }
                else if (obj is GeometryInstance gi)
                {
                    var c = FromBlockInstance(gi, diag);
                    if (c != null) res.Add(c);
                }
            }
            var lns = geoms.OfType<Line>().ToList();
            if (lns.Count >= 4) res.AddRange(RectsFromLines(lns));
            if (diag != null)
                diag.Add("geoms=" + geoms.Count + " polyline=" + geoms.OfType<PolyLine>().Count() +
                         " line=" + lns.Count + " inst=" + geoms.OfType<GeometryInstance>().Count() +
                         " -> candidates=" + res.Count);

            double t = 50.0 * MmFt;
            var uni = new List<ColData>();
            foreach (var c in res)
                if (!uni.Any(u => Math.Sqrt(Math.Pow(c.X - u.X, 2) + Math.Pow(c.Y - u.Y, 2)) < t))
                    uni.Add(c);
            return uni;
        }

        // ────────────────────────────────────────────────
        // CAD 圖塊 (INSERT) → 柱：重心 + transform 旋轉角 + 去旋轉 bounding box 尺寸
        // ────────────────────────────────────────────────
        static ColData FromBlockInstance(GeometryInstance gi, List<string> diag)
        {
            var pts = new List<XYZ>();
            CollectInstancePoints(gi.GetInstanceGeometry(), pts, 0);
            if (pts.Count < 3) return null;
            // 圖塊：旋轉角取自 instance transform 的 X 基底向量
            double rot = Math.Atan2(gi.Transform.BasisX.Y, gi.Transform.BasisX.X);
            return BuildColFromPoints(pts, rot, diag, "block(" + pts.Count + "pt)");
        }

        // 點群最長邊的方向角（封閉圖形的主軸估計，給 PolyLine/線段用）
        static double LongestEdgeAngle(List<XYZ> pts)
        {
            double best = -1, ang = 0;
            for (int i = 0; i < pts.Count; i++)
            {
                var a = pts[i]; var b = pts[(i + 1) % pts.Count];
                double dx = b.X - a.X, dy = b.Y - a.Y;
                double len = dx * dx + dy * dy;
                if (len > best) { best = len; ang = Math.Atan2(dy, dx); }
            }
            return ang;
        }

        // 通用：點群 + 主方向 → 柱資料（去旋轉外接矩形 + 形心 + 5mm 量化 + 尺寸過濾）。
        // 封閉 PolyLine / 四線段 / 圖塊共用此法，不挑畫法。
        static ColData BuildColFromPoints(List<XYZ> pts, double rot, List<string> diag, string src)
        {
            if (pts.Count < 3) return null;
            double cx0 = pts.Average(p => p.X);
            double cy0 = pts.Average(p => p.Y);

            double cosN = Math.Cos(-rot), sinN = Math.Sin(-rot);
            double minX = 1e9, minY = 1e9, maxX = -1e9, maxY = -1e9;
            foreach (var p in pts)
            {
                double dx = p.X - cx0, dy = p.Y - cy0;
                double rx = dx * cosN - dy * sinN;
                double ry = dx * sinN + dy * cosN;
                if (rx < minX) minX = rx;
                if (rx > maxX) maxX = rx;
                if (ry < minY) minY = ry;
                if (ry > maxY) maxY = ry;
            }

            double wRaw = (maxX - minX) * FtMm, dRaw = (maxY - minY) * FtMm;
            double wMm = Math.Round(wRaw / 5.0) * 5;
            double dMm = Math.Round(dRaw / 5.0) * 5;
            if (diag != null) diag.Add(src + " raw " + ((int)wRaw) + "x" + ((int)dRaw) + "mm");
            if (wMm < 100 || wMm > 3000 || dMm < 100 || dMm > 3000) return null;

            double bcx = (minX + maxX) / 2.0, bcy = (minY + maxY) / 2.0;
            double cosR = Math.Cos(rot), sinR = Math.Sin(rot);
            double mx = cx0 + (bcx * cosR - bcy * sinR);
            double my = cy0 + (bcx * sinR + bcy * cosR);

            double a = rot;
            while (a <= -Math.PI / 2.0) a += Math.PI;
            while (a > Math.PI / 2.0) a -= Math.PI;
            if (Math.Abs(wMm - dMm) < Tol) a = 0;

            return new ColData { X = mx, Y = my, W = wMm, D = dMm, A = a };
        }

        // 遞迴收集圖塊內所有曲線端點（模型座標）；含嵌套圖塊，限制深度避免病態檔案
        static void CollectInstancePoints(GeometryElement ge, List<XYZ> pts, int depth)
        {
            if (ge == null || depth > 5) return;
            foreach (var obj in ge)
            {
                if (obj is Line ln)
                {
                    pts.Add(ln.GetEndPoint(0));
                    pts.Add(ln.GetEndPoint(1));
                }
                else if (obj is PolyLine pl)
                {
                    pts.AddRange(pl.GetCoordinates());
                }
                else if (obj is Arc ar)
                {
                    foreach (var q in ar.Tessellate()) pts.Add(q);
                }
                else if (obj is Curve cv)
                {
                    foreach (var q in cv.Tessellate()) pts.Add(q);
                }
                else if (obj is GeometryInstance ngi)
                {
                    CollectInstancePoints(ngi.GetInstanceGeometry(), pts, depth + 1);
                }
            }
        }

        static ColData MakeRect(List<XYZ> points)
        {
            var pts = new List<XYZ>();
            foreach (var p in points)
            {
                bool dup = false;
                foreach (var q in pts) { if (p.DistanceTo(q) < 0.001) { dup = true; break; } }
                if (!dup) pts.Add(p);
            }
            if (pts.Count != 4) return null;

            for (int i = 0; i < 4; i++)
            {
                var ab = pts[(i + 1) % 4] - pts[i];
                var bc = pts[(i + 2) % 4] - pts[(i + 1) % 4];
                double la = Math.Sqrt(ab.X * ab.X + ab.Y * ab.Y);
                double lb = Math.Sqrt(bc.X * bc.X + bc.Y * bc.Y);
                if (la < 0.001 || lb < 0.001) return null;
                if (Math.Abs((ab.X * bc.X + ab.Y * bc.Y) / (la * lb)) > 0.05) return null;
            }

            XYZ e1 = pts[1] - pts[0];
            XYZ e2 = pts[2] - pts[1];
            double l1 = Math.Sqrt(e1.X * e1.X + e1.Y * e1.Y) * FtMm;
            double l2 = Math.Sqrt(e2.X * e2.X + e2.Y * e2.Y) * FtMm;
            double angle1 = Math.Atan2(e1.Y, e1.X);
            if (angle1 < 0) angle1 += Math.PI;
            if (angle1 >= Math.PI) angle1 -= Math.PI;

            double wMm, dMm, rot;
            if (angle1 <= Math.PI / 4.0 || angle1 > 3.0 * Math.PI / 4.0)
            {
                wMm = Math.Round(l1 / 5.0) * 5;
                dMm = Math.Round(l2 / 5.0) * 5;
                rot = (angle1 <= Math.PI / 4.0) ? angle1 : angle1 - Math.PI;
            }
            else
            {
                wMm = Math.Round(l2 / 5.0) * 5;
                dMm = Math.Round(l1 / 5.0) * 5;
                rot = angle1 - Math.PI / 2.0;
            }

            if (wMm < 100 || wMm > 3000 || dMm < 100 || dMm > 3000) return null;
            if (Math.Abs(wMm - dMm) < Tol) rot = 0;

            double cx = 0, cy = 0;
            foreach (var p in pts) { cx += p.X; cy += p.Y; }
            return new ColData { X = cx / 4.0, Y = cy / 4.0, W = wMm, D = dMm, A = rot };
        }

        static List<ColData> RectsFromLines(List<Line> lines)
        {
            var res = new List<ColData>();
            var used = new bool[lines.Count];
            for (int i = 0; i < lines.Count; i++)
            {
                if (used[i]) continue;
                var ch = new List<int> { i };
                XYZ st = lines[i].GetEndPoint(0), cur = lines[i].GetEndPoint(1);
                for (int step = 0; step < 3; step++)
                {
                    bool found = false;
                    for (int j = 0; j < lines.Count; j++)
                    {
                        if (ch.Contains(j)) continue;
                        XYZ p0 = lines[j].GetEndPoint(0), p1 = lines[j].GetEndPoint(1);
                        if (cur.DistanceTo(p0) < 0.01) { ch.Add(j); cur = p1; found = true; break; }
                        if (cur.DistanceTo(p1) < 0.01) { ch.Add(j); cur = p0; found = true; break; }
                    }
                    if (!found) break;
                }
                if (ch.Count != 4 || cur.DistanceTo(st) >= 0.01) continue;
                var vts = new List<XYZ>();
                XYZ pt = lines[ch[0]].GetEndPoint(0);
                vts.Add(pt);
                for (int k = 0; k < ch.Count; k++)
                {
                    XYZ a = lines[ch[k]].GetEndPoint(0), b = lines[ch[k]].GetEndPoint(1);
                    pt = pt.DistanceTo(a) < 0.01 ? b : a;
                    if (k < 3) vts.Add(pt);
                }
                var c = MakeRect(vts);
                if (c != null) { res.Add(c); foreach (int x in ch) used[x] = true; }
            }
            return res;
        }

        // ────────────────────────────────────────────────
        // 族群搜尋與參數偵測
        // ────────────────────────────────────────────────
        static FamilySymbol FindFamily(Document doc, BuiltInCategory bic, ref string wp, ref string dp)
        {
            var syms = new FilteredElementCollector(doc)
                .OfCategory(bic)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>().ToList();
            if (syms.Count == 0) return null;

            var best = syms.OrderByDescending(s => FamilyScore(s)).First();
            DetectParams(best, ref wp, ref dp);
            return best;
        }

        // 依族群名稱精確找族群，取其第一個 Symbol 做參數偵測
        static FamilySymbol FindFamilyByName(Document doc, BuiltInCategory bic, string familyName, ref string wp, ref string dp)
        {
            var sym = new FilteredElementCollector(doc)
                .OfCategory(bic)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .FirstOrDefault(s => s.Family.Name.Equals(familyName, StringComparison.OrdinalIgnoreCase));
            if (sym == null) return null;
            DetectParams(sym, ref wp, ref dp);
            return sym;
        }

        // ────────────────────────────────────────────────
        // CAD 文字標注讀取
        // ────────────────────────────────────────────────

        class LabelData { public string Text; public double X; public double Y; public double RawX; public double RawY; }

        // 從上到下依序找 ezdxf_worker.py：dll 同層 → 往上六層（開發環境）→ %APPDATA%\RevitMCP
        static string FindWorkerScript()
        {
            string asmDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

            string candidate = Path.Combine(asmDir, "ezdxf_worker.py");
            if (File.Exists(candidate)) return candidate;

            DirectoryInfo dir = new DirectoryInfo(asmDir);
            for (int i = 0; i < 6 && dir != null; i++, dir = dir.Parent)
            {
                candidate = Path.Combine(dir.FullName, "bridge", "python", "skills", "ezdxf_worker.py");
                if (File.Exists(candidate)) return candidate;
            }

            string appData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "RevitMCP", "ezdxf_worker.py");
            return File.Exists(appData) ? appData : null;
        }

        // 從 ImportInstance 反查磁碟上的 CAD 真實路徑。
        // 只有「連結(Link CAD)」才有外部檔案參考；「匯入(Import)」無法取得路徑，回傳 null。
        static string GetCadFilePath(Document doc, ImportInstance cad)
        {
            if (!cad.IsLinked) return null;
            try
            {
                var linkType = doc.GetElement(cad.GetTypeId()) as CADLinkType;
                if (linkType == null) return null;
                var extRef = linkType.GetExternalFileReference();
                if (extRef == null) return null;
                string pathStr = ModelPathUtils.ConvertModelPathToUserVisiblePath(extRef.GetPath());
                if (string.IsNullOrEmpty(pathStr)) return null;
                if (!Path.IsPathRooted(pathStr) && !string.IsNullOrEmpty(doc.PathName))
                    pathStr = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(doc.PathName), pathStr));
                return File.Exists(pathStr) ? pathStr : null;
            }
            catch { return null; }
        }

        // 呼叫 ezdxf_worker.py，讀取指定圖層的文字，座標已轉成 Revit 模型座標（feet）
        // 若 ezdxf_worker 回傳 error_type=no_oda，拋出 "NO_ODA:" 前綴的 Exception
        static List<LabelData> ReadLabelsFromCad(Document doc, ViewPlan vp, string textLayerName, string colLayerName = null)
        {
            string workerPath = FindWorkerScript();
            if (workerPath == null) return null;

            var cads = new FilteredElementCollector(doc, vp.Id)
                .OfClass(typeof(ImportInstance)).Cast<ImportInstance>().ToList();

            // 優先找連結(IsLinked)的 CAD，只有連結才能反查原始檔案路徑
            string filePath = null;
            ImportInstance targetCad = null;
            foreach (var c in cads)
            {
                string p = GetCadFilePath(doc, c);
                if (p != null) { filePath = p; targetCad = c; break; }
            }

            if (filePath == null)
            {
                bool hasImportOnly = cads.Any() && cads.All(c => !c.IsLinked);
                if (hasImportOnly)
                    throw new Exception(
                        "視圖內的 CAD 均以「匯入(Import)」方式加入，無法讀取原始檔案路徑。" +
                        "請刪除匯入的 CAD，改用「插入 → 連結 CAD (Link CAD)」，才能讀取柱號標注文字。");
                return null;
            }

            var cad = targetCad;

            var psi = new ProcessStartInfo
            {
                FileName = "python",
                Arguments = $"\"{workerPath}\" \"{filePath}\" \"{textLayerName}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            string output;
            using (var proc = Process.Start(psi))
            {
                output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit(20000);
            }

            if (string.IsNullOrWhiteSpace(output))
                throw new Exception("ezdxf_worker.py 無回應（python 未安裝或路徑不正確）");

            var json = JObject.Parse(output);
            string errorType = json["error_type"]?.Value<string>();
            if (errorType == "no_oda")
                throw new Exception("NO_ODA:" + json["error"]?.Value<string>());
            if (json["error"] != null)
                throw new Exception(json["error"].Value<string>());

            var transform = cad.GetTotalTransform();

            // 先收集所有原始 DXF 座標
            var rawItems = new List<(string text, double rx, double ry, double rz)>();
            foreach (JObject item in json["data"] as JArray ?? new JArray())
            {
                string text = (item["text"]?.Value<string>() ?? "").Trim();
                if (string.IsNullOrEmpty(text)) continue;
                rawItems.Add((text,
                    item["x"]?.Value<double>() ?? 0,
                    item["y"]?.Value<double>() ?? 0,
                    item["z"]?.Value<double>() ?? 0));
            }
            if (rawItems.Count == 0) return new List<LabelData>();

            // 自動試算最佳 DXF 單位（$INSUNITS=0 無法判斷時用此法）
            // 嘗試 mm / cm / m / inch → feet 換算係數，選讓標注最靠近幾何柱的那個
            double[] trialScales = { 1.0 / 304.8, 1.0 / 30.48, 1.0 / 0.3048, 1.0 / 25.4, 1.0 };
            string[] scaleNames = { "mm", "cm", "m", "inch", "ft" };

            // 取幾何柱的 Revit 座標（從目前視圖掃描，做為比對基準）
            var geomCols = string.IsNullOrEmpty(colLayerName)
                ? new List<GeometryObject>()
                : CollectLayerGeometry(doc, vp, colLayerName);
            var refCols = Extract(geomCols, null); // ColData list in feet

            double bestScale = trialScales[0];
            double bestErr = double.MaxValue;
            foreach (var sc in trialScales)
            {
                double sumMinDist = 0;
                foreach (var (_, rx, ry, _) in rawItems)
                {
                    var pt = transform.OfPoint(new XYZ(rx * sc, ry * sc, 0));
                    double minD = refCols.Count > 0
                        ? refCols.Min(c => (c.X - pt.X) * (c.X - pt.X) + (c.Y - pt.Y) * (c.Y - pt.Y))
                        : 1e18;
                    sumMinDist += minD;
                }
                if (sumMinDist < bestErr) { bestErr = sumMinDist; bestScale = sc; }
            }

            var labels = new List<LabelData>();
            foreach (var (text, rx, ry, rz) in rawItems)
            {
                var revitPt = transform.OfPoint(new XYZ(rx * bestScale, ry * bestScale, rz * bestScale));
                labels.Add(new LabelData { Text = text, X = revitPt.X, Y = revitPt.Y, RawX = rx, RawY = ry });
            }
            return labels;
        }

        // 依標注文字（如 "C1" 或 "C1(100×60)"）在指定族群中找最匹配的類型。
        // 找不到時回傳 null（呼叫端負責記錄 unmatched，不自動建立新類型）。
        // 比對優先順序同 BIMAssistant find_or_create_type：
        //   1. 完整名稱完全符合
        //   2. 代號前綴符合（"C3a_" 或 "C3a-"）
        //   3. 代號前綴 + 尺寸數值雙重符合（防止代號重複時誤判）
        static FamilySymbol MatchSymbolByLabel(
            Document doc, FamilySymbol baseSym, string labelText, string wp, string dp, double wMm, double dMm)
        {
            var syms = baseSym.Family.GetFamilySymbolIds()
                .Select(id => doc.GetElement(id) as FamilySymbol)
                .Where(s => s != null).ToList();

            // Step 1: 完整名稱完全符合
            var match = syms.FirstOrDefault(s =>
                s.Name.Equals(labelText, StringComparison.OrdinalIgnoreCase));
            if (match != null) return match;

            // 萃取代號：去掉括號與尺寸後綴，如 "C3a(100×60)" → "C3a"
            // 同時解析 CAD 標注中的尺寸（若有），用於第三段驗證
            string code = labelText;
            int labelW = 0, labelD = 0;
            var codeM = Regex.Match(labelText,
                @"^([A-Za-z]+\d+[a-z]?)[\s\(_\-]?\s*(\d+(?:\.\d+)?)\s*[xX×\*]\s*(\d+(?:\.\d+)?)");
            if (codeM.Success)
            {
                code = codeM.Groups[1].Value;
                labelW = (int)Math.Round(double.Parse(codeM.Groups[2].Value));
                labelD = (int)Math.Round(double.Parse(codeM.Groups[3].Value));
            }
            else
            {
                var codeOnly = Regex.Match(labelText, @"^[A-Za-z]+\d+[a-z]?");
                if (codeOnly.Success) code = codeOnly.Value;
            }

            // Step 2: 代號前綴符合（"C3a_..." 或 "C3a-..." 或完全等於 "C3a"）
            match = syms.FirstOrDefault(s =>
                s.Name.StartsWith(code + "_", StringComparison.OrdinalIgnoreCase) ||
                s.Name.StartsWith(code + "-", StringComparison.OrdinalIgnoreCase) ||
                s.Name.Equals(code, StringComparison.OrdinalIgnoreCase));
            if (match != null) return match;

            // Step 3: 代號前綴 + 尺寸雙重符合（代號相同但尺寸不同時區分）
            if (labelW > 0 && labelD > 0)
            {
                double wF = wMm * MmFt, dF = dMm * MmFt, t = Tol * MmFt;
                match = syms.FirstOrDefault(s =>
                {
                    if (!s.Name.StartsWith(code, StringComparison.OrdinalIgnoreCase)) return false;
                    var pw = s.LookupParameter(wp); var pd = s.LookupParameter(dp);
                    if (pw == null || pd == null) return false;
                    double sw = pw.AsDouble() * FtMm, sd = pd.AsDouble() * FtMm;
                    return (Math.Abs(sw - wMm) < Tol * 2 && Math.Abs(sd - dMm) < Tol * 2) ||
                           (Math.Abs(sw - dMm) < Tol * 2 && Math.Abs(sd - wMm) < Tol * 2);
                });
                if (match != null) return match;
            }

            // 找不到：回傳 null，由呼叫端記錄 unmatched
            return null;
        }

        static int FamilyScore(FamilySymbol s)
        {
            string fn = s.Family.Name;
            bool c = fn.Contains("混凝土") || fn.IndexOf("Concrete", StringComparison.OrdinalIgnoreCase) >= 0 || fn.Contains("RC");
            bool r = fn.Contains("矩形") || fn.IndexOf("Rect", StringComparison.OrdinalIgnoreCase) >= 0;
            string _wp = null, _dp = null;
            if (c && r) return 3;
            if (c) return 2;
            if (DetectParams(s, ref _wp, ref _dp)) return 1;
            return 0;
        }

        static bool DetectParams(FamilySymbol s, ref string wp, ref string dp)
        {
            wp = dp = null;
            string[] wn = { "b", "B", "寬度", "寬", "柱寬", "斷面寬", "Width", "width", "w", "W", "Bf", "bf", "B1" };
            string[] dn = { "h", "H", "深度", "深", "柱深", "斷面深", "Depth", "depth", "d", "D", "Height", "height", "H1" };
            wp = FindParam(s, wn, null);
            dp = FindParam(s, dn, wp);
            if (wp != null && dp != null) return true;

            var cands = s.Parameters.Cast<Parameter>()
                .Where(p => p.StorageType == StorageType.Double && !p.IsReadOnly && p.HasValue)
                .Where(p => { double v = p.AsDouble() * FtMm; return v >= 50 && v <= 5000; })
                .OrderBy(p => p.AsDouble()).ToList();
            if (cands.Count >= 2 && wp == null && dp == null)
            { wp = cands[0].Definition.Name; dp = cands[1].Definition.Name; return true; }
            if (cands.Count >= 1)
            {
                if (wp == null) wp = cands[0].Definition.Name;
                if (dp == null) dp = cands[0].Definition.Name;
                return true;
            }
            return false;
        }

        static string FindParam(FamilySymbol s, string[] names, string ex)
        {
            foreach (var n in names)
            {
                if (n == ex) continue;
                var p = s.LookupParameter(n);
                if (p != null && !p.IsReadOnly && p.StorageType == StorageType.Double) return n;
            }
            return null;
        }

        static FamilySymbol GetOrCreate(Document doc, FamilySymbol bs, double wMm, double dMm, string wp, string dp)
        {
            double wF = wMm * MmFt, dF = dMm * MmFt, t = Tol * MmFt;
            var ids = bs.Family.GetFamilySymbolIds();

            foreach (ElementId id in ids)
            {
                var s = doc.GetElement(id) as FamilySymbol;
                if (s == null) continue;
                var pw = s.LookupParameter(wp);
                var pd = s.LookupParameter(dp);
                if (pw != null && pd != null &&
                    Math.Abs(pw.AsDouble() - wF) < t &&
                    Math.Abs(pd.AsDouble() - dF) < t) return s;
            }

            string nm = $"{(int)Math.Round(wMm / 10.0)}x{(int)Math.Round(dMm / 10.0)}";
            int sf = 1;
            string fn = nm;
            while (ids.Select(id => doc.GetElement(id) as FamilySymbol)
                       .Any(s => s != null && s.Name == fn))
            { sf++; fn = $"{nm}_{sf}"; }

            var ns = bs.Duplicate(fn) as FamilySymbol;
            if (ns != null)
            {
                ns.LookupParameter(wp)?.Set(wF);
                ns.LookupParameter(dp)?.Set(dF);
            }
            return ns;
        }

        static void SetParam(FamilyInstance inst, BuiltInParameter bip, ElementId val)
        {
            var p = inst.get_Parameter(bip);
            if (p != null && !p.IsReadOnly) p.Set(val);
        }

        static void SetParam(FamilyInstance inst, BuiltInParameter bip, double val)
        {
            var p = inst.get_Parameter(bip);
            if (p != null && !p.IsReadOnly) p.Set(val);
        }

        class ColData
        {
            public double X;
            public double Y;
            public double W;
            public double D;
            public double A;
        }
    }
}
