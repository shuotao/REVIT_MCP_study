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
    /// 樓板表面坡度分析。
    /// 以 Solid → PlanarFace 的法向量與 Z 軸夾角，計算每片「朝上頂面」的坡度百分比，
    /// 可批次處理指定樓板，或自動收集 Function=Exterior 的樓板。
    ///
    /// 寫入政策（issue #137）：**預設完全不寫入模型**，只回傳分析結果。
    /// 只有呼叫端明確給了 paramName 才會開 Transaction 回寫。
    /// 修正前 paramName 預設為 "Comments" 且無條件 p.Set(...)，等於一支名為 analyze_ 的工具
    /// 會默默覆寫全模型 Function=Exterior 樓板的註解欄（很多團隊把人工備註放在那裡）。
    ///
    /// 獨立靜態類別，不影響既有 CommandExecutor 邏輯，遵循現有 case-routing 模式。
    /// 原始需求、演算法與實測來源：Issue #45 by yunchen-kt。
    /// </summary>
    internal static class FloorSlopeAnalyzer
    {
        // 朝上頂面判定門檻：法向量正規化後 Z 分量 > 此值才視為「排水頂面」。
        // 0.7 ≈ 與水平夾角 45° 內，可納入緩坡與斜坡排水面，同時排除近垂直的板側 / 倒角面
        // （那些 n.Z 接近 0，會吐出數百 % 的假坡度）。排水檢討屬緩坡（1~2%），此門檻不會誤殺真頂面。
        private const double UpwardNormalThreshold = 0.7;

        /// <summary>
        /// 命令入口。解析 parameters（elementIds / paramName），分析坡度，
        /// 並**僅在 paramName 有明確指定時**回寫，回傳結構化結果。
        /// </summary>
        public static object Run(Document doc, JObject parameters)
        {
            if (doc == null)
                return new { Success = false, Message = "無可用文件 (ActiveUIDocument.Document == null)。" };

            // paramName 不再有預設值。未指定 = 純分析，不開 Transaction、不動模型。
            string paramName = parameters["paramName"]?.Value<string>();
            bool writeRequested = !string.IsNullOrWhiteSpace(paramName);

            List<Floor> floors = CollectFloors(doc, parameters);
            if (floors.Count == 0)
            {
                return new
                {
                    Success = false,
                    Message = "找不到符合條件的樓板。未指定 elementIds 時，會自動收集 Function=Exterior 的樓板。"
                };
            }

            var results = new List<object>();
            var errors = new List<string>();
            int processed = 0;
            int writtenCount = 0;

            if (writeRequested)
            {
                using (Transaction trans = new Transaction(doc, "分析樓板坡度並回寫參數"))
                {
                    trans.Start();
                    Analyze(floors, paramName, results, errors, ref processed, ref writtenCount);
                    trans.Commit();
                }
            }
            else
            {
                // 純分析路徑：不開 Transaction，模型零異動。
                Analyze(floors, null, results, errors, ref processed, ref writtenCount);
            }

            return new
            {
                Success = errors.Count == 0,
                ProcessedCount = processed,
                ParameterName = writeRequested ? paramName : (string?)null,
                WrittenCount = writtenCount,
                WriteSkipped = !writeRequested,
                Message = writeRequested
                    ? (string?)null
                    : "未指定 paramName：本次僅回傳分析結果，未寫入任何 Revit 參數。若要回寫，請明確指定 paramName（例如 \"Comments\" 會寫入內建註解參數，或填自訂參數名稱）。",
                Floors = results,
                Errors = errors
            };
        }

        /// <summary>
        /// 逐片樓板計算坡度；paramName 為 null 時只蒐集結果不回寫。
        /// 呼叫端負責 Transaction（只有要回寫時才需要）。
        /// </summary>
        private static void Analyze(
            List<Floor> floors,
            string? paramName,
            List<object> results,
            List<string> errors,
            ref int processed,
            ref int writtenCount)
        {
            foreach (Floor floor in floors)
            {
                try
                {
                    var slope = ComputeSlope(floor);
                    if (slope == null)
                    {
                        errors.Add($"樓板 {floor.Id.GetIdValue()} 無可分析的朝上平面（可能為曲面或無 PlanarFace）。");
                        continue;
                    }

                    double minPct = slope.Item1;
                    double maxPct = slope.Item2;
                    int faceCount = slope.Item3;

                    string written = null;
                    if (paramName != null)
                    {
                        Parameter p = ResolveWriteParameter(floor, paramName);
                        if (p != null && !p.IsReadOnly && p.StorageType == StorageType.String)
                        {
                            written = $"Slope {minPct:F2}%~{maxPct:F2}%";
                            p.Set(written);
                            writtenCount++;
                        }
                        else if (p != null && !p.IsReadOnly && p.StorageType == StorageType.Double)
                        {
                            // 數值欄位以「比例值」寫入最大坡度（例如 2% → 0.02）。
                            p.Set(maxPct / 100.0);
                            written = $"{maxPct:F2}% (max, numeric)";
                            writtenCount++;
                        }
                        else
                        {
                            string why = p == null
                                ? "找不到該參數（內建 Comments 以 BuiltInParameter 解析、其餘名稱以 LookupParameter 解析本地化顯示名）"
                                : (p.IsReadOnly ? "參數為唯讀" : $"型別不支援（StorageType={p.StorageType}，僅支援 String / Double）");
                            errors.Add($"樓板 {floor.Id.GetIdValue()} 的參數 '{paramName}' 無法寫入：{why}，坡度未回寫（數值仍於回傳中提供）。");
                        }
                    }

                    results.Add(new
                    {
                        ElementId = floor.Id.GetIdValue(),
                        MinSlopePercent = Math.Round(minPct, 2),
                        MaxSlopePercent = Math.Round(maxPct, 2),
                        UpwardFaceCount = faceCount,
                        Written = written
                    });
                    processed++;
                }
                catch (Exception ex)
                {
                    errors.Add($"樓板 {floor.Id.GetIdValue()} 分析失敗: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 解析回寫目標參數。
        ///
        /// "Comments" 走 BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS（Floor 是模型元件 instance，
        /// 其「註解」欄位即此內建參數；本專案 StairCompliance / FillPatterns / RoomFilledRegions
        /// 皆用同一枚舉）。原本只用 LookupParameter("Comments") 是比對**本地化顯示名**：
        /// 英文 Revit 會命中並覆寫使用者備註，zh-TW Revit（顯示名為「註解」）則完全命中不到、
        /// 靜默不寫入 —— 同一組參數在不同語言介面行為不同（issue #137 回報者 test A / test B）。
        /// 其他名稱仍走 LookupParameter，以支援自訂／共用參數與本地化顯示名（例如直接填「註解」）。
        /// </summary>
        private static Parameter? ResolveWriteParameter(Floor floor, string paramName)
        {
            if (string.Equals(paramName, "Comments", StringComparison.OrdinalIgnoreCase))
            {
                Parameter builtIn = floor.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                if (builtIn != null)
                    return builtIn;
            }

            return floor.LookupParameter(paramName);
        }

        /// <summary>
        /// 收集目標樓板：優先用傳入的 elementIds；否則自動收集 Function=Exterior 的樓板。
        /// </summary>
        private static List<Floor> CollectFloors(Document doc, JObject parameters)
        {
            var ids = parameters["elementIds"] as JArray;
            if (ids != null && ids.Count > 0)
            {
                var list = new List<Floor>();
                foreach (var token in ids)
                {
                    try
                    {
                        IdType v = token.Value<IdType>();
                        if (doc.GetElement(new ElementId(v)) is Floor f)
                            list.Add(f);
                    }
                    catch
                    {
                        // 忽略單一無效 ID，不中斷整批
                    }
                }
                return list;
            }

            return new FilteredElementCollector(doc)
                .OfClass(typeof(Floor))
                .Cast<Floor>()
                .Where(f => IsExterior(doc, f))
                .ToList();
        }

        /// <summary>
        /// 判定樓板的型別 Function 是否為 Exterior（FUNCTION_PARAM == 1）。
        /// </summary>
        private static bool IsExterior(Document doc, Floor floor)
        {
            Element type = doc.GetElement(floor.GetTypeId());
            Parameter fp = type?.get_Parameter(BuiltInParameter.FUNCTION_PARAM);
            return fp != null && fp.AsInteger() == 1; // 0 = Interior, 1 = Exterior
        }

        /// <summary>
        /// 計算單片樓板所有朝上 PlanarFace 的坡度，回傳 (minPercent, maxPercent, upwardFaceCount)；
        /// 無朝上平面時回傳 null。
        /// </summary>
        private static Tuple<double, double, int> ComputeSlope(Floor floor)
        {
            var opt = new Options
            {
                ComputeReferences = false,
                IncludeNonVisibleObjects = false,
                DetailLevel = ViewDetailLevel.Fine
            };

            GeometryElement geo = floor.get_Geometry(opt);
            if (geo == null) return null;

            var slopes = new List<double>();
            CollectUpwardSlopes(geo, slopes);

            if (slopes.Count == 0) return null;
            return Tuple.Create(slopes.Min(), slopes.Max(), slopes.Count);
        }

        /// <summary>
        /// 走訪幾何，蒐集所有朝上 PlanarFace 的坡度%。會遞迴進入 GeometryInstance，
        /// 避免巢狀幾何（被 instance 包住的 Solid）被漏算而誤判為「無頂面」。
        /// </summary>
        private static void CollectUpwardSlopes(IEnumerable<GeometryObject> geometry, List<double> slopes)
        {
            foreach (GeometryObject obj in geometry)
            {
                if (obj is Solid solid)
                {
                    if (solid.Faces.Size == 0) continue;
                    foreach (Face face in solid.Faces)
                    {
                        if (!(face is PlanarFace pf)) continue;

                        XYZ n = pf.FaceNormal;
                        if (n.GetLength() < 1e-9) continue;
                        n = n.Normalize();

                        // 僅取朝上頂面；n.Z = 法向量與垂直 Z 軸夾角的餘弦。
                        if (n.Z <= UpwardNormalThreshold) continue;

                        double cos = Math.Min(1.0, Math.Max(-1.0, n.Z));
                        double slopePct = Math.Tan(Math.Acos(cos)) * 100.0;
                        slopes.Add(slopePct);
                    }
                }
                else if (obj is GeometryInstance gi)
                {
                    CollectUpwardSlopes(gi.GetInstanceGeometry(), slopes);
                }
            }
        }
    }
}
