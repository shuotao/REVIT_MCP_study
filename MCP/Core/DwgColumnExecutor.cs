using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;

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

            var cols = Extract(geoms);

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

            return new
            {
                layerName = layerName,
                count = cols.Count,
                sizeSummary = sizeGroups,
                columns = colList,
                message = cols.Count == 0 ? $"圖層「{layerName}」中沒有識別到封閉矩形" : null
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

            var bLv = vp.GenLevel;
            if (bLv == null) throw new Exception("無法取得基準樓層");

            var tLv = new FilteredElementCollector(doc)
                .OfClass(typeof(Level)).Cast<Level>()
                .Where(l => l.Elevation > bLv.Elevation + 0.001)
                .OrderBy(l => l.Elevation).FirstOrDefault();
            if (tLv == null) throw new Exception($"找不到高於「{bLv.Name}」的樓層");

            var geoms = CollectLayerGeometry(doc, vp, layerName);
            if (geoms.Count == 0) throw new Exception($"圖層「{layerName}」中找不到幾何物件");

            var cols = Extract(geoms);
            if (cols.Count == 0) throw new Exception($"圖層「{layerName}」中無封閉矩形");

            string wp = null, dp = null;
            var baseSym = FindFamily(doc, bic, ref wp, ref dp);
            if (baseSym == null)
                throw new Exception($"找不到{columnTypeName}族群，請先在專案中載入矩形柱族群");

            int ok = 0, fail = 0;
            var errors = new List<string>();

            using (var tr = new Transaction(doc, $"從DWG建立{columnTypeName}"))
            {
                tr.Start();
                if (!baseSym.IsActive) { baseSym.Activate(); doc.Regenerate(); }

                foreach (var c in cols)
                {
                    try
                    {
                        var sym = GetOrCreate(doc, baseSym, c.W, c.D, wp, dp);
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
                widthParam = wp,
                depthParam = dp,
                baseLevel = bLv.Name,
                topLevel = tLv.Name,
                totalDetected = cols.Count,
                created = ok,
                failed = fail,
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
        static List<ColData> Extract(List<GeometryObject> geoms)
        {
            var res = new List<ColData>();
            foreach (var obj in geoms)
            {
                if (obj is PolyLine pl)
                {
                    var pts = pl.GetCoordinates();
                    if (pts.Count >= 4) { var c = MakeRect(pts.ToList()); if (c != null) res.Add(c); }
                }
            }
            var lns = geoms.OfType<Line>().ToList();
            if (lns.Count >= 4) res.AddRange(RectsFromLines(lns));

            double t = 50.0 * MmFt;
            var uni = new List<ColData>();
            foreach (var c in res)
                if (!uni.Any(u => Math.Sqrt(Math.Pow(c.X - u.X, 2) + Math.Pow(c.Y - u.Y, 2)) < t))
                    uni.Add(c);
            return uni;
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
