using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;

namespace RevitMCP.Core
{
    /// <summary>
    /// CAD 文字標注回填工具集：讀取 CAD 圖面上的編號文字（C1、B2、S1…），
    /// 依空間位置配對到「使用者已建好」的柱/梁/樓板，批次寫入「備註」等實例參數。
    /// 不建立任何幾何，只做「文字 ↔ 既有元件」配對與參數寫入。
    ///   - preview_comments_from_cad  : 乾跑，回傳元件↔編號對照表供使用者確認
    ///   - backfill_comments_from_cad : 單一 Transaction 批次寫入參數
    /// CAD 文字讀取沿用 dwg-column-import 模式 C 的 ezdxf_worker.py 管線
    /// （需連結 CAD + 系統 Python + ezdxf；DWG 另需 ODA File Converter）。
    /// </summary>
    public static class CadAnnotationExecutor
    {
        const double FtMm = 304.8;
        const double MmFt = 1.0 / 304.8;

        // ────────────────────────────────────────────────
        // 入口
        // ────────────────────────────────────────────────

        public static object PreviewCommentsFromCad(Document doc, JObject p)
            => Run(doc, p, apply: false);

        public static object BackfillCommentsFromCad(Document doc, JObject p)
            => Run(doc, p, apply: true);

        // ────────────────────────────────────────────────
        // 主流程：讀文字 → 撈元件 → 單位偵測 → 配對 →（apply 時）寫入
        // ────────────────────────────────────────────────

        static object Run(Document doc, JObject p, bool apply)
        {
            var vp = doc.ActiveView as ViewPlan;
            if (vp == null) throw new Exception("請在平面視圖中執行");

            string textLayerName = p["textLayerName"]?.Value<string>();
            if (string.IsNullOrEmpty(textLayerName)) throw new Exception("必須提供 textLayerName 參數");

            string category = (p["category"]?.Value<string>() ?? "").ToLowerInvariant();
            if (category != "column" && category != "beam" && category != "floor")
                throw new Exception("category 必須為 column、beam 或 floor");

            double maxDistMm = p["maxDistanceMm"]?.Value<double>() ?? 1500.0;
            string parameterName = p["parameterName"]?.Value<string>();
            bool overwrite = p["overwrite"]?.Value<bool>() ?? false;

            var labels = ReadLabelsRaw(doc, vp, textLayerName);
            if (labels.Count == 0)
                throw new Exception($"文字圖層「{textLayerName}」中找不到任何 TEXT/MTEXT 文字");

            var targets = CollectTargets(doc, vp, category);
            if (targets.Count == 0)
                throw new Exception($"目前視圖中找不到任何 {CategoryLabel(category)} 元件，請先建模或切換到正確的平面視圖");

            string detectedUnit = TransformLabelsToRevit(labels, targets);

            var (matches, unmatchedLabels) = MatchLabels(labels, targets, maxDistMm * MmFt);

            var unlabeled = targets.Where(t => !matches.Any(m => m.Target == t)).ToList();

            var warnings = new List<string>();
            if (unmatchedLabels.Count > 0)
                warnings.Add($"{unmatchedLabels.Count} 個文字找不到 {maxDistMm}mm 內的{CategoryLabel(category)}，可能是容差太小、單位判斷錯誤，或該構件尚未建模");
            if (unlabeled.Count > 0)
                warnings.Add($"{unlabeled.Count} 個{CategoryLabel(category)}沒有配對到任何文字");

            int written = 0, skippedExisting = 0, skippedReadOnly = 0;
            if (apply)
            {
                using (var tx = new Transaction(doc, "CAD 文字回填備註"))
                {
                    tx.Start();
                    foreach (var m in matches)
                    {
                        var param = GetTargetParameter(m.Target.Element, parameterName);
                        if (param == null || param.IsReadOnly) { skippedReadOnly++; m.Skipped = "read_only_or_missing"; continue; }
                        string current = param.AsString();
                        if (!overwrite && !string.IsNullOrEmpty(current)) { skippedExisting++; m.Skipped = "has_existing_value"; continue; }
                        param.Set(m.Label.Text);
                        written++;
                    }
                    tx.Commit();
                }
                if (skippedExisting > 0)
                    warnings.Add($"{skippedExisting} 個元件已有備註值而略過（要覆蓋請帶 overwrite=true）");
                if (skippedReadOnly > 0)
                    warnings.Add($"{skippedReadOnly} 個元件的目標參數不存在或唯讀");
            }

            return new
            {
                mode = apply ? "apply" : "preview",
                viewName = vp.Name,
                category,
                parameterName = parameterName ?? "備註 (Comments)",
                detectedUnit,
                labelCount = labels.Count,
                elementCount = targets.Count,
                matchedCount = matches.Count,
                writtenCount = apply ? (int?)written : null,
                matches = matches.Select(m => new
                {
                    elementId = m.Target.Element.Id.GetIdValue(),
                    typeName = m.Target.TypeName,
                    label = m.Label.Text,
                    distance_mm = Math.Round(m.Distance * FtMm, 0),
                    currentValue = GetTargetParameter(m.Target.Element, parameterName)?.AsString(),
                    skipped = m.Skipped
                }).ToList(),
                unmatchedLabels = unmatchedLabels.Select(l => new
                {
                    text = l.Text,
                    x_mm = Math.Round(l.X * FtMm, 1),
                    y_mm = Math.Round(l.Y * FtMm, 1)
                }).ToList(),
                unlabeledElementIds = unlabeled.Select(t => t.Element.Id.GetIdValue()).ToList(),
                warnings
            };
        }

        // ────────────────────────────────────────────────
        // 目標元件收集
        // ────────────────────────────────────────────────

        class TargetData
        {
            public Element Element;
            public string TypeName;
            public XYZ Point;          // column: LocationPoint；floor: bbox 中心
            public Curve Curve;        // beam: LocationCurve
            public BoundingBoxXYZ Box; // floor: 模型 bbox
        }

        static string CategoryLabel(string category)
            => category == "column" ? "柱" : category == "beam" ? "梁" : "樓板";

        static List<TargetData> CollectTargets(Document doc, ViewPlan vp, string category)
        {
            var result = new List<TargetData>();
            var bics = category == "column"
                ? new[] { BuiltInCategory.OST_StructuralColumns, BuiltInCategory.OST_Columns }
                : category == "beam"
                    ? new[] { BuiltInCategory.OST_StructuralFraming }
                    : new[] { BuiltInCategory.OST_Floors };

            foreach (var bic in bics)
            {
                var elems = new FilteredElementCollector(doc, vp.Id)
                    .OfCategory(bic)
                    .WhereElementIsNotElementType()
                    .ToList();

                foreach (var e in elems)
                {
                    var t = new TargetData { Element = e, TypeName = doc.GetElement(e.GetTypeId())?.Name ?? e.Name };
                    if (category == "beam")
                    {
                        t.Curve = (e.Location as LocationCurve)?.Curve;
                        if (t.Curve == null) continue;
                    }
                    else if (category == "column")
                    {
                        var lp = e.Location as LocationPoint;
                        if (lp == null) continue;
                        t.Point = lp.Point;
                    }
                    else // floor
                    {
                        t.Box = e.get_BoundingBox(null);
                        if (t.Box == null) continue;
                        t.Point = (t.Box.Min + t.Box.Max) / 2;
                    }
                    result.Add(t);
                }
            }
            return result;
        }

        // 文字點到目標元件的平面距離（feet）。樓板：點在 bbox 內視為 0。
        static double DistanceTo(TargetData t, double x, double y)
        {
            if (t.Curve != null)
            {
                var res = t.Curve.Project(new XYZ(x, y, t.Curve.GetEndPoint(0).Z));
                if (res == null) return double.MaxValue;
                var q = res.XYZPoint;
                return Math.Sqrt((q.X - x) * (q.X - x) + (q.Y - y) * (q.Y - y));
            }
            if (t.Box != null)
            {
                if (x >= t.Box.Min.X && x <= t.Box.Max.X && y >= t.Box.Min.Y && y <= t.Box.Max.Y)
                    return 0;
            }
            return Math.Sqrt((t.Point.X - x) * (t.Point.X - x) + (t.Point.Y - y) * (t.Point.Y - y));
        }

        // ────────────────────────────────────────────────
        // 配對：距離排序貪婪法，一文字配一元件
        // ────────────────────────────────────────────────

        class MatchData
        {
            public LabelData Label;
            public TargetData Target;
            public double Distance;
            public string Skipped;
        }

        static (List<MatchData>, List<LabelData>) MatchLabels(
            List<LabelData> labels, List<TargetData> targets, double maxDistFt)
        {
            var pairs = new List<MatchData>();
            foreach (var l in labels)
                foreach (var t in targets)
                {
                    double d = DistanceTo(t, l.X, l.Y);
                    if (d <= maxDistFt) pairs.Add(new MatchData { Label = l, Target = t, Distance = d });
                }

            var matches = new List<MatchData>();
            var usedLabels = new HashSet<LabelData>();
            var usedTargets = new HashSet<TargetData>();
            foreach (var pair in pairs.OrderBy(x => x.Distance))
            {
                if (usedLabels.Contains(pair.Label) || usedTargets.Contains(pair.Target)) continue;
                usedLabels.Add(pair.Label);
                usedTargets.Add(pair.Target);
                matches.Add(pair);
            }

            var unmatched = labels.Where(l => !usedLabels.Contains(l)).ToList();
            return (matches, unmatched);
        }

        // ────────────────────────────────────────────────
        // 參數存取：未指定 parameterName 時用內建「備註」(Comments)，語系無關
        // ────────────────────────────────────────────────

        static Parameter GetTargetParameter(Element e, string parameterName)
            => string.IsNullOrEmpty(parameterName)
                ? e.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)
                : e.LookupParameter(parameterName);

        // ────────────────────────────────────────────────
        // CAD 文字讀取（沿用 DwgColumnExecutor 的 ezdxf 管線）
        // ────────────────────────────────────────────────

        class LabelData { public string Text; public double X; public double Y; public double RawX; public double RawY; public Transform Transform; }

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

        // 讀出原始 DXF 座標的文字清單（尚未換算單位；Transform 先存起來，
        // 單位係數由 TransformLabelsToRevit 以目標元件位置試算決定）
        static List<LabelData> ReadLabelsRaw(Document doc, ViewPlan vp, string textLayerName)
        {
            string workerPath = FindWorkerScript();
            if (workerPath == null)
                throw new Exception("找不到 ezdxf_worker.py（請重跑 install-addon.ps1 部署，或確認開發目錄結構）");

            var cads = new FilteredElementCollector(doc, vp.Id)
                .OfClass(typeof(ImportInstance)).Cast<ImportInstance>().ToList();
            if (cads.Count == 0) throw new Exception("目前視圖中找不到任何 CAD 連結或匯入");

            string filePath = null;
            ImportInstance targetCad = null;
            foreach (var c in cads)
            {
                string path = GetCadFilePath(doc, c);
                if (path != null) { filePath = path; targetCad = c; break; }
            }

            if (filePath == null)
            {
                bool hasImportOnly = cads.All(c => !c.IsLinked);
                if (hasImportOnly)
                    throw new Exception(
                        "視圖內的 CAD 均以「匯入(Import)」方式加入，無法讀取原始檔案路徑。" +
                        "請刪除匯入的 CAD，改用「插入 → 連結 CAD (Link CAD)」，才能讀取編號文字。");
                throw new Exception("無法取得 CAD 檔案路徑");
            }

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

            var transform = targetCad.GetTotalTransform();
            var labels = new List<LabelData>();
            foreach (JObject item in json["data"] as JArray ?? new JArray())
            {
                string text = (item["text"]?.Value<string>() ?? "").Trim();
                if (string.IsNullOrEmpty(text)) continue;
                labels.Add(new LabelData
                {
                    Text = text,
                    RawX = item["x"]?.Value<double>() ?? 0,
                    RawY = item["y"]?.Value<double>() ?? 0,
                    Transform = transform
                });
            }
            return labels;
        }

        // 自動試算最佳 DXF 單位：以「目標元件位置」為比對基準（而非 CAD 幾何層），
        // 選讓文字整體最靠近元件的換算係數，並就地把 X/Y 換算成 Revit feet。
        static string TransformLabelsToRevit(List<LabelData> labels, List<TargetData> targets)
        {
            double[] trialScales = { 1.0 / 304.8, 1.0 / 30.48, 1.0 / 0.3048, 1.0 / 25.4, 1.0 };
            string[] scaleNames = { "mm", "cm", "m", "inch", "ft" };

            double bestScale = trialScales[0];
            string bestName = scaleNames[0];
            double bestErr = double.MaxValue;
            for (int i = 0; i < trialScales.Length; i++)
            {
                double sc = trialScales[i];
                double sumMinDist = 0;
                foreach (var l in labels)
                {
                    var pt = l.Transform.OfPoint(new XYZ(l.RawX * sc, l.RawY * sc, 0));
                    sumMinDist += targets.Min(t => DistanceTo(t, pt.X, pt.Y));
                }
                if (sumMinDist < bestErr) { bestErr = sumMinDist; bestScale = sc; bestName = scaleNames[i]; }
            }

            foreach (var l in labels)
            {
                var pt = l.Transform.OfPoint(new XYZ(l.RawX * bestScale, l.RawY * bestScale, 0));
                l.X = pt.X;
                l.Y = pt.Y;
            }
            return bestName;
        }
    }
}
