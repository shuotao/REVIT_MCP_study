using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;

namespace RevitMCP.Core
{
    public static class DwgBeamExecutor
    {
        const double FtMm = 304.8;
        const double MmFt = 1.0 / 304.8;
        const double Tol = 5.0;

        public class BeamData
        {
            public XYZ StartPoint { get; set; }
            public XYZ EndPoint { get; set; }
            public XYZ Center { get; set; }
            public double LengthMm { get; set; }
            public double WidthMm { get; set; }
            public bool IsXAligned { get; set; }
            public bool IsYAligned { get; set; }
            public double Angle { get; set; }
        }

        class LabelData { public string Text; public double X; public double Y; public double RawX; public double RawY; }

        public static object GetDwgBeamLayers(Document doc)
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

            string[] beamKeywords = { "樑", "梁", "beam", "bm" };
            var suggested = sortedLayers.FirstOrDefault(l =>
                beamKeywords.Any(k => l.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0));

            return new
            {
                viewName = vp.Name,
                cadCount = cads.Count,
                layerCount = sortedLayers.Count,
                layers = sortedLayers,
                suggestedLayer = suggested
            };
        }

        public static object PreviewDwgBeams(Document doc, JObject p)
        {
            var vp = doc.ActiveView as ViewPlan;
            if (vp == null) throw new Exception("請在平面視圖中執行");

            string layerName = p["layerName"]?.Value<string>();
            if (string.IsNullOrEmpty(layerName)) throw new Exception("必須提供 layerName 參數");

            var lines = CollectLayerLines(doc, vp, layerName);
            var beams = ExtractBeamCenterLines(lines);

            var xBeams = beams.Where(b => b.IsXAligned).ToList();
            var yBeams = beams.Where(b => b.IsYAligned).ToList();

            return new
            {
                layerName = layerName,
                totalBeams = beams.Count,
                xAlignedCount = xBeams.Count,
                yAlignedCount = yBeams.Count,
                otherCount = beams.Count - xBeams.Count - yBeams.Count,
                sampleBeams = beams.Take(5).Select(b => new {
                    width_mm = Math.Round(b.WidthMm, 1),
                    length_mm = Math.Round(b.LengthMm, 1),
                    isX = b.IsXAligned,
                    isY = b.IsYAligned
                })
            };
        }

        public static object CreateBeamsFromDwg(Document doc, JObject p)
        {
            var vp = doc.ActiveView as ViewPlan;
            if (vp == null) throw new Exception("請在平面視圖中執行");

            string layerName = p["layerName"]?.Value<string>();
            if (string.IsNullOrEmpty(layerName)) throw new Exception("必須提供 layerName (樑線段) 參數");

            string requestedFamilyName = p["familyName"]?.Value<string>();
            if (string.IsNullOrEmpty(requestedFamilyName)) throw new Exception("必須提供 familyName (族群名稱) 參數");

            string typeName = p["typeName"]?.Value<string>();
            string textLayerNameX = p["textLayerNameX"]?.Value<string>();
            string textLayerNameY = p["textLayerNameY"]?.Value<string>();
            string beamRole = p["beamRole"]?.Value<string>();

            bool isQuickMode = !string.IsNullOrEmpty(typeName);
            bool isLabelMode = !string.IsNullOrEmpty(textLayerNameX) || !string.IsNullOrEmpty(textLayerNameY);

            if (!isQuickMode && !isLabelMode)
                throw new Exception("必須提供 typeName (快速模式) 或至少一個文字圖層 (名稱對應模式)");

            var bLv = vp.GenLevel;
            if (bLv == null) throw new Exception("無法取得基準樓層");

            var lines = CollectLayerLines(doc, vp, layerName);
            if (lines.Count == 0) throw new Exception($"圖層「{layerName}」中找不到直線幾何");

            var beams = ExtractBeamCenterLines(lines);
            if (beams.Count == 0) throw new Exception($"圖層「{layerName}」中無法配對出雙線樑中心線");

            FamilySymbol baseSym = FindFramingFamily(doc, requestedFamilyName);
            if (baseSym == null)
                throw new Exception($"找不到名稱為「{requestedFamilyName}」的結構樑族群");

            FamilySymbol quickModeSym = null;
            if (isQuickMode)
            {
                quickModeSym = baseSym.Family.GetFamilySymbolIds()
                    .Select(id => doc.GetElement(id) as FamilySymbol)
                    .FirstOrDefault(s => NormalizeX(s.Name).Equals(NormalizeX(typeName), StringComparison.OrdinalIgnoreCase));
                
                if (quickModeSym == null)
                    throw new Exception($"在族群「{requestedFamilyName}」中找不到類型「{typeName}」");
            }

            string wp = null, dp = null;
            DetectParams(baseSym, ref wp, ref dp); // 樑通常 b, h 或 寬度, 深度

            List<LabelData> labelsX = null;
            List<LabelData> labelsY = null;
            string labelStatus = "skipped";
            string labelWarning = null;

            if (isLabelMode && !isQuickMode)
            {
                try
                {
                    labelsX = string.IsNullOrEmpty(textLayerNameX) ? null : ReadLabelsFromCad(doc, vp, textLayerNameX, beams.Select(b => b.Center).ToList());
                    labelsY = string.IsNullOrEmpty(textLayerNameY) ? null : ReadLabelsFromCad(doc, vp, textLayerNameY, beams.Select(b => b.Center).ToList());
                    labelStatus = (labelsX != null || labelsY != null) ? "ok" : "no_worker";
                    if (labelsX == null && labelsY == null)
                        labelWarning = "找不到 ezdxf_worker.py 或無法取得 CAD 檔案路徑";
                    
                    if (labelsX != null) labelsX = FilterLabels(labelsX, beams.Where(b => b.IsXAligned).Select(b => b.Center).ToList());
                    if (labelsY != null) labelsY = FilterLabels(labelsY, beams.Where(b => b.IsYAligned).Select(b => b.Center).ToList());
                }
                catch (Exception ex) when (ex.Message.StartsWith("NO_ODA:"))
                {
                    labelStatus = "no_oda";
                    labelWarning = "DWG 格式需要 ODA File Converter 才能讀取文字標注。";
                }
                catch (Exception ex)
                {
                    labelStatus = "error";
                    labelWarning = "讀取 CAD 文字標注時發生錯誤：" + ex.Message;
                }
            }

            int ok = 0, fail = 0;
            var errors = new List<string>();
            var unmatchedLabels = new List<string>();
            var typeMapping = new List<string>();

            var orthogonalBeams = beams.Where(b => b.IsXAligned || b.IsYAligned).ToList();
            var diagonalBeams = beams.Where(b => !b.IsXAligned && !b.IsYAligned).ToList();
            var usedLabelKeys = new HashSet<string>(); // C# 端維護：已配對的標籤座標 key

            using (var tr = new Transaction(doc, "從DWG建立結構樑"))
            {
                tr.Start();
                if (!baseSym.IsActive) { baseSym.Activate(); doc.Regenerate(); }
                if (quickModeSym != null && !quickModeSym.IsActive) { quickModeSym.Activate(); doc.Regenerate(); }

                // 第一波：正交樑（優先消耗文字標籤，避免干擾第二波）
                foreach (var b in orthogonalBeams)
                {
                    try
                    {
                        FamilySymbol sym = null;
                        if (isQuickMode)
                        {
                            sym = quickModeSym;
                        }
                        else if (isLabelMode)
                        {
                            var targetLabels = b.IsXAligned ? labelsX : labelsY;
                            if (targetLabels != null)
                            {
                                var available = targetLabels.Where(l => !usedLabelKeys.Contains(LabelKey(l))).ToList();
                                var nearest = available.OrderBy(l => b.Center.DistanceTo(new XYZ(l.X, l.Y, b.Center.Z))).FirstOrDefault();
                                if (nearest != null)
                                {
                                    double distMm = b.Center.DistanceTo(new XYZ(nearest.X, nearest.Y, b.Center.Z)) * FtMm;
                                    if (distMm < 2500)
                                    {
                                        sym = MatchSymbolByLabel(doc, baseSym, nearest.Text, wp, dp, b.WidthMm, 500);
                                        if (sym != null)
                                            usedLabelKeys.Add(LabelKey(nearest));
                                        else if (!unmatchedLabels.Contains(nearest.Text))
                                            unmatchedLabels.Add(nearest.Text);
                                    }
                                }
                            }
                        }
                        if (sym == null) { fail++; continue; }
                        PlaceBeamInstance(doc, b, bLv, sym, typeMapping, ref ok);
                    }
                    catch (Exception ex) { fail++; errors.Add(ex.Message); }
                }

                // 第二波：非正交（斜）樑，使用剩餘標籤，點到線段距離配對
                foreach (var b in diagonalBeams)
                {
                    try
                    {
                        FamilySymbol sym = null;
                        if (isQuickMode)
                        {
                            sym = quickModeSym;
                        }
                        else if (isLabelMode)
                        {
                            var remaining = new List<LabelData>();
                            if (labelsX != null) remaining.AddRange(labelsX.Where(l => !usedLabelKeys.Contains(LabelKey(l))));
                            if (labelsY != null) remaining.AddRange(labelsY.Where(l => !usedLabelKeys.Contains(LabelKey(l))));

                            var nearest = remaining
                                .OrderBy(l => PointToLineDistanceMm(new XYZ(l.X, l.Y, b.Center.Z), b.StartPoint, b.EndPoint))
                                .FirstOrDefault();
                            if (nearest != null)
                            {
                                double distMm = PointToLineDistanceMm(new XYZ(nearest.X, nearest.Y, b.Center.Z), b.StartPoint, b.EndPoint);
                                double threshold = b.WidthMm / 2.0 + 500.0; // 樑寬一半 + 500mm 緩衝
                                if (distMm < threshold)
                                {
                                    sym = MatchSymbolByLabel(doc, baseSym, nearest.Text, wp, dp, b.WidthMm, 500);
                                    if (sym != null)
                                        usedLabelKeys.Add(LabelKey(nearest));
                                    else if (!unmatchedLabels.Contains(nearest.Text))
                                        unmatchedLabels.Add(nearest.Text);
                                }
                            }
                        }
                        if (sym == null) { fail++; continue; }
                        PlaceBeamInstance(doc, b, bLv, sym, typeMapping, ref ok);
                    }
                    catch (Exception ex) { fail++; errors.Add(ex.Message); }
                }

                tr.Commit();
            }

            return new
            {
                mode = isQuickMode ? "快速模式" : "名稱對應模式",
                beamRole = beamRole,
                familyName = baseSym.Family.Name,
                labelReadStatus = labelStatus,
                labelWarning = labelWarning,
                labelCountX = labelsX?.Count ?? 0,
                labelCountY = labelsY?.Count ?? 0,
                totalDetected = beams.Count,
                created = ok,
                failed = fail,
                typesUsed = typeMapping,
                unmatchedLabels = unmatchedLabels,
                errors = errors.Take(10).ToList()
            };
        }

        static List<Line> CollectLayerLines(Document doc, ViewPlan vp, string layerName)
        {
            var result = new List<Line>();
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
                        {
                            if (obj is Line ln) result.Add(ln);
                            else if (obj is PolyLine pl)
                            {
                                var pts = pl.GetCoordinates();
                                for (int i = 0; i < pts.Count - 1; i++) result.Add(Line.CreateBound(pts[i], pts[i+1]));
                            }
                        }
                    }
                }
            }
            return result;
        }

        static List<BeamData> ExtractBeamCenterLines(List<Line> lines)
        {
            var results = new List<BeamData>();
            var used = new HashSet<int>();

            for (int i = 0; i < lines.Count; i++)
            {
                if (used.Contains(i)) continue;
                Line L1 = lines[i];
                XYZ d1 = (L1.GetEndPoint(1) - L1.GetEndPoint(0)).Normalize();
                
                double angle = Math.Atan2(d1.Y, d1.X) * 180 / Math.PI;
                if (angle < 0) angle += 180;
                if (angle >= 180) angle -= 180;

                bool isX = (angle <= 15 || angle >= 165);
                bool isY = (angle >= 75 && angle <= 105);

                int bestPair = -1;
                double bestDist = 2000 * MmFt; 
                
                for (int j = i + 1; j < lines.Count; j++)
                {
                    if (used.Contains(j)) continue;
                    Line L2 = lines[j];
                    XYZ d2 = (L2.GetEndPoint(1) - L2.GetEndPoint(0)).Normalize();
                    
                    if (Math.Abs(d1.DotProduct(d2)) < 0.95) continue; 
                    
                    XYZ p1 = L1.GetEndPoint(0);
                    XYZ p2 = L2.GetEndPoint(0);
                    XYZ v = p2 - p1;
                    double dist = (v - v.DotProduct(d1) * d1).GetLength();
                    
                    if (dist < 100 * MmFt || dist > 2500 * MmFt) continue;

                    double p0 = L1.GetEndPoint(0).DotProduct(d1);
                    double p1_proj = L1.GetEndPoint(1).DotProduct(d1);
                    double min1 = Math.Min(p0, p1_proj);
                    double max1 = Math.Max(p0, p1_proj);

                    double p2_proj = L2.GetEndPoint(0).DotProduct(d1);
                    double p3_proj = L2.GetEndPoint(1).DotProduct(d1);
                    double min2 = Math.Min(p2_proj, p3_proj);
                    double max2 = Math.Max(p2_proj, p3_proj);

                    double overlap = Math.Max(0, Math.Min(max1, max2) - Math.Max(min1, min2));
                    if (overlap > 300 * MmFt)
                    {
                        if (dist < bestDist)
                        {
                            bestDist = dist;
                            bestPair = j;
                        }
                    }
                }

                if (bestPair != -1)
                {
                    used.Add(i);
                    used.Add(bestPair);
                    Line L2 = lines[bestPair];
                    
                    XYZ dir = (L1.GetEndPoint(1) - L1.GetEndPoint(0)).Normalize();
                    double p0 = L1.GetEndPoint(0).DotProduct(dir);
                    double p1_proj = L1.GetEndPoint(1).DotProduct(dir);
                    double min1 = Math.Min(p0, p1_proj);
                    double max1 = Math.Max(p0, p1_proj);

                    double p2 = L2.GetEndPoint(0).DotProduct(dir);
                    double p3 = L2.GetEndPoint(1).DotProduct(dir);
                    double min2 = Math.Min(p2, p3);
                    double max2 = Math.Max(p2, p3);

                    double overlapMin = Math.Max(min1, min2);
                    double overlapMax = Math.Min(max1, max2);

                    XYZ basePt = L1.GetEndPoint(0) - p0 * dir; 
                    XYZ centerOffset = (L2.GetEndPoint(0) - (L2.GetEndPoint(0).DotProduct(dir) * dir + basePt)); 
                    XYZ midOffset = centerOffset / 2.0;

                    XYZ finalStart = basePt + dir * overlapMin + midOffset;
                    XYZ finalEnd = basePt + dir * overlapMax + midOffset;

                    results.Add(new BeamData {
                        StartPoint = finalStart,
                        EndPoint = finalEnd,
                        Center = (finalStart + finalEnd) / 2.0,
                        LengthMm = (finalEnd - finalStart).GetLength() * FtMm,
                        WidthMm = bestDist * FtMm,
                        IsXAligned = isX,
                        IsYAligned = isY,
                        Angle = angle
                    });
                }
            }
            return results;
        }

        static FamilySymbol FindFramingFamily(Document doc, string name)
        {
            var syms = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_StructuralFraming)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>().ToList();
            if (syms.Count == 0) return null;
            if (!string.IsNullOrEmpty(name))
                return syms.FirstOrDefault(s => s.Family.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            return syms.FirstOrDefault();
        }

        static void DetectParams(FamilySymbol sym, ref string wp, ref string dp)
        {
            string[] wNames = { "b", "Width", "寬", "寬度" };
            string[] dNames = { "h", "Depth", "深", "深度", "高", "高度" };
            foreach (Parameter p in sym.Parameters)
            {
                string n = p.Definition.Name;
                if (wNames.Contains(n) && wp == null) wp = n;
                if (dNames.Contains(n) && dp == null) dp = n;
            }
        }

        static List<LabelData> FilterLabels(List<LabelData> labels, List<XYZ> refPts)
        {
            return labels;
        }

        static string LabelKey(LabelData l) => $"{l.X:F6},{l.Y:F6}";

        static double PointToLineDistanceMm(XYZ pt, XYZ lineStart, XYZ lineEnd)
        {
            XYZ dir = lineEnd - lineStart;
            double len = dir.GetLength();
            if (len < 1e-9) return pt.DistanceTo(lineStart) * FtMm;
            dir = dir.Normalize();
            double proj = (pt - lineStart).DotProduct(dir);
            proj = Math.Max(0, Math.Min(len, proj));
            XYZ closest = lineStart + proj * dir;
            return pt.DistanceTo(closest) * FtMm;
        }

        static void PlaceBeamInstance(Document doc, BeamData b, Level bLv, FamilySymbol sym, List<string> typeMapping, ref int ok)
        {
            if (!sym.IsActive) { sym.Activate(); doc.Regenerate(); }
            if (!typeMapping.Contains(sym.Name) && typeMapping.Count < 10)
                typeMapping.Add(sym.Name);

            XYZ st = new XYZ(b.StartPoint.X, b.StartPoint.Y, bLv.Elevation);
            XYZ en = new XYZ(b.EndPoint.X, b.EndPoint.Y, bLv.Elevation);
            var inst = doc.Create.NewFamilyInstance(Line.CreateBound(st, en), sym, bLv, StructuralType.Beam);
            if (inst == null) return;
            try
            {
                var yJust = inst.get_Parameter(BuiltInParameter.Y_JUSTIFICATION);
                if (yJust != null && !yJust.IsReadOnly) yJust.Set(1); // Center

                var zOff = inst.get_Parameter(BuiltInParameter.Z_OFFSET_VALUE);
                if (zOff != null && !zOff.IsReadOnly) zOff.Set(0.0);

                var zJust = inst.get_Parameter(BuiltInParameter.Z_JUSTIFICATION);
                if (zJust != null && !zJust.IsReadOnly) zJust.Set(0); // Top
            }
            catch { }
            ok++;
        }

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
            string appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RevitMCP", "ezdxf_worker.py");
            return File.Exists(appData) ? appData : null;
        }

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
                if (!Path.IsPathRooted(pathStr) && !string.IsNullOrEmpty(doc.PathName))
                    pathStr = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(doc.PathName), pathStr));
                return File.Exists(pathStr) ? pathStr : null;
            }
            catch { return null; }
        }

        static List<LabelData> ReadLabelsFromCad(Document doc, ViewPlan vp, string textLayerName, List<XYZ> refPts)
        {
            string workerPath = FindWorkerScript();
            if (workerPath == null) return null;

            var cads = new FilteredElementCollector(doc, vp.Id)
                .OfClass(typeof(ImportInstance)).Cast<ImportInstance>().ToList();

            double[] trialScales = { 1.0 / 304.8, 1.0 / 30.48, 1.0 / 0.3048, 1.0 / 25.4, 1.0 };
            var allLabels = new List<LabelData>();

            foreach (var cad in cads)
            {
                string filePath = GetCadFilePath(doc, cad);
                if (filePath == null) continue;

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

                if (string.IsNullOrWhiteSpace(output)) continue;

                var json = JObject.Parse(output);
                string errorType = json["error_type"]?.Value<string>();
                if (errorType == "no_oda")
                    throw new Exception("NO_ODA:" + json["error"]?.Value<string>());
                if (json["error"] != null) continue; // 此 CAD 無此圖層，跳過

                var rawItems = new List<(string text, double rx, double ry)>();
                foreach (JObject item in json["data"] as JArray ?? new JArray())
                {
                    string text = (item["text"]?.Value<string>() ?? "").Trim();
                    if (!string.IsNullOrEmpty(text))
                        rawItems.Add((text, item["x"]?.Value<double>() ?? 0, item["y"]?.Value<double>() ?? 0));
                }
                if (rawItems.Count == 0) continue;

                var transform = cad.GetTotalTransform();
                double bestScale = trialScales[0];
                double bestErr = double.MaxValue;
                foreach (var sc in trialScales)
                {
                    double sumMinDist = 0;
                    foreach (var (_, rx, ry) in rawItems)
                    {
                        var pt = transform.OfPoint(new XYZ(rx * sc, ry * sc, 0));
                        double minD = refPts.Count > 0 ? refPts.Min(c => c.DistanceTo(pt)) : 1e18;
                        sumMinDist += minD;
                    }
                    if (sumMinDist < bestErr) { bestErr = sumMinDist; bestScale = sc; }
                }

                foreach (var (text, rx, ry) in rawItems)
                {
                    var pt = transform.OfPoint(new XYZ(rx * bestScale, ry * bestScale, 0));
                    allLabels.Add(new LabelData { Text = text, X = pt.X, Y = pt.Y, RawX = rx, RawY = ry });
                }
            }

            return allLabels.Count > 0 ? allLabels : null;
        }

        static string NormalizeX(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return s.Replace("X", "x").Replace("×", "x");
        }

        static FamilySymbol MatchSymbolByLabel(Document doc, FamilySymbol baseSym, string labelText, string wp, string dp, double wMm, double dMm)
        {
            var syms = baseSym.Family.GetFamilySymbolIds()
                .Select(id => doc.GetElement(id) as FamilySymbol)
                .Where(s => s != null).ToList();

            string normLabel = NormalizeX(labelText);

            // Step 1: 完全符合
            var exact = syms.FirstOrDefault(s => NormalizeX(s.Name).Equals(normLabel, StringComparison.Ordinal));
            if (exact != null) return exact;

            // Step 2: 完整標註文字直接當作前綴比對
            var prefixMatch = syms.FirstOrDefault(s => 
                NormalizeX(s.Name).StartsWith(normLabel + "_", StringComparison.Ordinal) || 
                NormalizeX(s.Name).StartsWith(normLabel + "-", StringComparison.Ordinal));
            
            return prefixMatch;
        }


    }
}
