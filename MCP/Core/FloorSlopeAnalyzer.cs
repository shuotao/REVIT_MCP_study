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
    /// 可批次處理指定樓板，或自動收集 Function=Exterior 的樓板，並將 Min/Max 坡度回寫至指定參數。
    ///
    /// 獨立靜態類別，不影響既有 CommandExecutor 邏輯，遵循現有 case-routing 模式。
    /// 原始需求、演算法與實測來源：Issue #45 by yunchen-kt。
    /// </summary>
    internal static class FloorSlopeAnalyzer
    {
        // 朝上頂面判定門檻：法向量正規化後 Z 分量 > 此值才視為「頂面」，排除側面 / 底面。
        private const double UpwardNormalThreshold = 0.1;

        /// <summary>
        /// 命令入口。解析 parameters（elementIds / paramName），分析坡度並回寫，回傳結構化結果。
        /// </summary>
        public static object Run(Document doc, JObject parameters)
        {
            if (doc == null)
                return new { Success = false, Message = "無可用文件 (ActiveUIDocument.Document == null)。" };

            string paramName = parameters["paramName"]?.Value<string>();
            if (string.IsNullOrWhiteSpace(paramName))
                paramName = "Comments";

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

            using (Transaction trans = new Transaction(doc, "分析樓板坡度"))
            {
                trans.Start();

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
                        Parameter p = floor.LookupParameter(paramName);
                        if (p != null && !p.IsReadOnly && p.StorageType == StorageType.String)
                        {
                            written = $"Slope {minPct:F2}%~{maxPct:F2}%";
                            p.Set(written);
                        }
                        else if (p != null && !p.IsReadOnly && p.StorageType == StorageType.Double)
                        {
                            // 數值欄位以「比例值」寫入最大坡度（例如 2% → 0.02）。
                            p.Set(maxPct / 100.0);
                            written = $"{maxPct:F2}% (max, numeric)";
                        }
                        else
                        {
                            errors.Add($"樓板 {floor.Id.GetIdValue()} 的參數 '{paramName}' 不存在 / 唯讀 / 型別不符，坡度未回寫（數值仍於回傳中提供）。");
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

                trans.Commit();
            }

            return new
            {
                Success = errors.Count == 0,
                ProcessedCount = processed,
                ParameterName = paramName,
                Floors = results,
                Errors = errors
            };
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

            double min = double.MaxValue;
            double max = double.MinValue;
            int count = 0;

            foreach (GeometryObject obj in geo)
            {
                Solid solid = obj as Solid;
                if (solid == null || solid.Faces.Size == 0) continue;

                foreach (Face face in solid.Faces)
                {
                    PlanarFace pf = face as PlanarFace;
                    if (pf == null) continue;

                    XYZ n = pf.FaceNormal;
                    if (n.GetLength() < 1e-9) continue;
                    n = n.Normalize();

                    // 僅取朝上頂面；n.Z = 法向量與垂直 Z 軸夾角的餘弦。
                    if (n.Z <= UpwardNormalThreshold) continue;

                    double cos = Math.Min(1.0, Math.Max(-1.0, n.Z));
                    double angleRad = Math.Acos(cos);
                    double slopePct = Math.Tan(angleRad) * 100.0;

                    if (slopePct < min) min = slopePct;
                    if (slopePct > max) max = slopePct;
                    count++;
                }
            }

            if (count == 0) return null;
            return Tuple.Create(min, max, count);
        }
    }
}
