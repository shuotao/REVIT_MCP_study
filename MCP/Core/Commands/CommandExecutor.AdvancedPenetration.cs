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
    public partial class CommandExecutor
    {
        private object AdvancedAnalyzeAndTag(JObject parameters)
        {
            Document mainDoc = _uiApp.ActiveUIDocument.Document;
            View activeView = mainDoc.ActiveView;

            try
            {
                var sleeves = new FilteredElementCollector(mainDoc, activeView.Id)
                    .WhereElementIsNotElementType()
                    .WherePasses(new ElementMulticategoryFilter(new List<BuiltInCategory> { 
                        BuiltInCategory.OST_PipeAccessory, 
                        BuiltInCategory.OST_GenericModel 
                    }))
                    .ToList();

                var linkInstances = new FilteredElementCollector(mainDoc, activeView.Id)
                    .OfClass(typeof(RevitLinkInstance))
                    .Cast<RevitLinkInstance>()
                    .ToList();

                int successCount = 0;
                int failCount = 0;
                var checkResults = new List<object>();

                foreach (var sleeve in sleeves)
                {
                    BoundingBoxXYZ sleeveBBox = sleeve.get_BoundingBox(null);
                    if (sleeveBBox == null) continue;

                    // 幾何碰撞第一層篩選已移至內部：不在此處直接 continue 排除碰牆/碰板的套管，
                    // 以免誤判「梁側貼牆/貼板」的合法穿梁套管。改由後續長度匹配度判定。

                    string comments = sleeve.LookupParameter("備註")?.AsString() ?? "";
                    string familyName = (sleeve as FamilyInstance)?.Symbol?.FamilyName ?? "";
                    string typeName = sleeve.Name;

                    // 動態讀取過濾關鍵字 (包含檢查類型名稱)
                    string keywordParam = parameters["Keyword"]?.Value<string>();
                    bool matchSleeve = false;
                    
                    if (!string.IsNullOrEmpty(keywordParam)) {
                        matchSleeve = comments.Contains(keywordParam) || 
                                     familyName.IndexOf(keywordParam, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                     typeName.IndexOf(keywordParam, StringComparison.OrdinalIgnoreCase) >= 0;
                    } else {
                        bool hasSleeveKeyword = familyName.IndexOf("Sleeve", StringComparison.OrdinalIgnoreCase) >= 0 || 
                                               typeName.IndexOf("Sleeve", StringComparison.OrdinalIgnoreCase) >= 0;
                        matchSleeve = comments.Contains("穿梁") || familyName.Contains("穿梁") || familyName.Contains("套管") || hasSleeveKeyword;
                    }

                    if (!matchSleeve)
                    {
                        continue; 
                    }

                    string[] diamParams = parameters["diameterParamNames"]?.ToObject<string[]>();
                    XYZ sleeveCenter = (sleeveBBox.Min + sleeveBBox.Max) * 0.5;
                    double sleeveD = GetSleeveDiameter(sleeve, sleeveBBox, diamParams);
                    string sleeveLevel = GetReferenceLevelName(sleeve);

                    foreach (var li in linkInstances)
                    {
                        Document linkDoc = li.GetLinkDocument();
                        if (linkDoc == null) continue;

                        Transform tr = li.GetTotalTransform();
                        // 建立正確的 Outline (考慮坐標轉換)
                        XYZ min = tr.Inverse.OfPoint(sleeveBBox.Min);
                        XYZ max = tr.Inverse.OfPoint(sleeveBBox.Max);
                        Outline sleeveOutline = new Outline(
                            new XYZ(Math.Min(min.X, max.X), Math.Min(min.Y, max.Y), Math.Min(min.Z, max.Z)),
                            new XYZ(Math.Max(min.X, max.X), Math.Max(min.Y, max.Y), Math.Max(min.Z, max.Z))
                        );

                        var linkBeams = new FilteredElementCollector(linkDoc)
                            .OfCategory(BuiltInCategory.OST_StructuralFraming)
                            .WherePasses(new BoundingBoxIntersectsFilter(sleeveOutline))
                            .WhereElementIsNotElementType();

                        foreach (var b in linkBeams)
                        {
                            FamilyInstance beamInstance = b as FamilyInstance;
                            double beamDepth = GetBeamDepth(b);
                            double beamWidth = GetBeamWidth(b);
                            string beamLevel = GetReferenceLevelName(b);
                            
                            double distToStart = -1;
                            double distToEnd = -1;
                            double minDist = -1;
                            double perpDistXY = 0;
                            double connDepthStart = -1;
                            double connDepthEnd = -1;

                            LocationCurve beamLoc = b.Location as LocationCurve;
                            if (beamLoc != null)
                            {
                                Curve bc = beamLoc.Curve;
                                XYZ localSleeveCenter = tr.Inverse.OfPoint(sleeveCenter);
                                IntersectionResult ir = bc.Project(localSleeveCenter);
                                if (ir != null)
                                {
                                    distToStart = ir.XYZPoint.DistanceTo(bc.GetEndPoint(0));
                                    distToEnd = ir.XYZPoint.DistanceTo(bc.GetEndPoint(1));
                                    minDist = Math.Min(distToStart, distToEnd);
                                    perpDistXY = new XYZ(localSleeveCenter.X - ir.XYZPoint.X, localSleeveCenter.Y - ir.XYZPoint.Y, 0).GetLength() * 304.8;
                                }

                                // 偵測起點(0)相連梁梁深
                                var conn0 = beamLoc.get_ElementsAtJoin(0);
                                if (conn0 != null)
                                {
                                    foreach (Element elem in conn0)
                                    {
                                        if (elem == null || elem.Id == b.Id) continue;
                                        if (elem.Category != null && elem.Category.Id.IntegerValue == (int)BuiltInCategory.OST_StructuralFraming)
                                        {
                                            connDepthStart = GetBeamDepth(elem) * 304.8;
                                            break;
                                        }
                                    }
                                }

                                // 偵測終點(1)相連梁梁深
                                var conn1 = beamLoc.get_ElementsAtJoin(1);
                                if (conn1 != null)
                                {
                                    foreach (Element elem in conn1)
                                    {
                                        if (elem == null || elem.Id == b.Id) continue;
                                        if (elem.Category != null && elem.Category.Id.IntegerValue == (int)BuiltInCategory.OST_StructuralFraming)
                                        {
                                            connDepthEnd = GetBeamDepth(elem) * 304.8;
                                            break;
                                        }
                                    }
                                }
                            }

                            BoundingBoxXYZ beamBox = b.get_BoundingBox(null);
                            double beamBottomZ = beamBox != null ? beamBox.Min.Z : 0;
                            double beamTopZ = beamBottomZ + beamDepth; // 扣除梁頂增築：以梁底標高加上型號梁深作為結構梁原本頂標高

                            checkResults.Add(new {
                                SleeveId = sleeve.Id.GetIdValue(),
                                SleeveLevel = sleeveLevel,
                                BeamId = b.Id.GetIdValue(),
                                BeamName = b.Name,
                                BeamLevel = beamLevel,
                                BeamUsage = beamInstance != null ? DetermineBeamUsage(beamInstance) : "Major",
                                IsNearWall = CheckIsIntersectsWithWall(mainDoc, sleeve),
                                BeamDepth = beamDepth * 304.8, // convert to mm
                                BeamWidth = beamWidth * 304.8, // convert to mm
                                SleeveDiameter = sleeveD * 304.8, // convert to mm
                                SleeveLength = GetSleeveLength(sleeve) * 304.8, // convert to mm
                                IsExcluded = GetSleeveLength(sleeve) * 304.8 < beamWidth * 304.8 - 10.0 || perpDistXY > (beamWidth * 304.8 / 2.0) + 100.0,
                                ExclusionReason = GetSleeveLength(sleeve) * 304.8 < beamWidth * 304.8 - 10.0 ? "套管長度小於梁寬，無法穿梁" : (perpDistXY > (beamWidth * 304.8 / 2.0) + 100.0 ? "套管偏離梁中心線過遠，疑似平行穿牆套管" : ""),
                                DistanceToStart = distToStart * 304.8,
                                DistanceToEnd = distToEnd * 304.8,
                                MinDistance = minDist * 304.8,
                                PerpDistXY = perpDistXY,
                                ConnectedBeamDepthStart = connDepthStart,
                                ConnectedBeamDepthEnd = connDepthEnd,
                                SleeveZ = sleeveCenter.Z * 304.8,
                                BeamTopZ = beamTopZ * 304.8,
                                BeamBottomZ = beamBottomZ * 304.8
                            });
                        }
                    }
                }

                return new { 
                    Success = true, 
                    Summary = $"幾何萃取完成。共提取 {checkResults.Count} 處關聯資料。",
                    Results = checkResults
                };
            }
            catch (Exception ex)
            {
                return new { Success = false, Error = "執行失敗: " + ex.Message };
            }
        }

        private double GetSleeveDiameter(Element sleeve, BoundingBoxXYZ bb, string[] paramNames = null) {
            if (paramNames == null || paramNames.Length == 0) {
                paramNames = new string[] { "開孔直徑", "直徑", "管徑", "Diameter", "Size" };
            }

            // 1. 使用雙層參數讀取器 (Instance -> Type)
            double? paramVal = GetParameterValueWithFallback(sleeve, paramNames);
            if (paramVal.HasValue) return paramVal.Value;

            // 2. 嘗試從類型名稱解析 (例如 "圓形開口 200mm") 作為備用
            Element type = sleeve.Document.GetElement(sleeve.GetTypeId());
            if (type != null) {
                var match = System.Text.RegularExpressions.Regex.Match(type.Name, @"(\d+)");
                if (match.Success) {
                    double nameVal = double.Parse(match.Groups[1].Value) / 304.8; // mm to feet
                    foreach (Parameter tp in type.Parameters) {
                        if (tp.StorageType == StorageType.Double && Math.Abs(tp.AsDouble() - nameVal) < 0.01) return tp.AsDouble();
                    }
                }
            }

            // 3. 最終備案：使用幾何包絡盒寬度
            if (bb != null) {
                return Math.Max(bb.Max.X - bb.Min.X, bb.Max.Y - bb.Min.Y);
            }
            return 0.1 / 0.3048; // 預設 10cm
        }

        private double GetBeamDepth(Element beam) {
            Document doc = beam.Document;
            Element type = doc.GetElement(beam.GetTypeId());
            
            if (type != null) {
                // 智慧解析：從名稱如 "35 x 70 cm" 提取最大數字作為深度基準
                var matches = System.Text.RegularExpressions.Regex.Matches(type.Name, @"(\d+)");
                double maxNumInName = 0;
                foreach (System.Text.RegularExpressions.Match m in matches) {
                    maxNumInName = Math.Max(maxNumInName, double.Parse(m.Value));
                }

                if (maxNumInName > 0) {
                    double targetFeet = (type.Name.Contains("cm")) ? maxNumInName / 30.48 : maxNumInName / 304.8;
                    foreach (Parameter tp in type.Parameters) {
                        if (tp.StorageType == StorageType.Double && Math.Abs(tp.AsDouble() - targetFeet) < 0.05) {
                            return tp.AsDouble();
                        }
                    }
                }

                // 備案：尋找名為 h 或 Depth 的類型參數
                Parameter hp = type.LookupParameter("h") ?? type.LookupParameter("深度") ?? type.LookupParameter("Depth") ?? type.LookupParameter("Height");
                if (hp != null && hp.HasValue && hp.StorageType == StorageType.Double) return hp.AsDouble();
            }

            // 最後才用 BoundingBox (且限制合理範圍 30cm~150cm)
            BoundingBoxXYZ bb = beam.get_BoundingBox(null);
            if (bb != null) {
                double h = bb.Max.Z - bb.Min.Z;
                if (h > 0.8 / 0.3048 && h < 5.0 / 0.3048) return h; // 排除樓層高度雜訊
            }
            return 700.0 / 304.8; // 預設 70cm
        }
    }
}
