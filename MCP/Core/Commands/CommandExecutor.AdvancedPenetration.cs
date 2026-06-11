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
                string keywordParam = parameters["Keyword"]?.Value<string>();
                string[] diamParams = parameters["diameterParamNames"]?.ToObject<string[]>();

                var linkInstances = new FilteredElementCollector(mainDoc, activeView.Id)
                    .OfClass(typeof(RevitLinkInstance))
                    .Cast<RevitLinkInstance>()
                    .ToList();

                // 建立套管清單：(元素, 所屬連結ID(0=主模型), 世界座標轉換)
                var sleeveEntries = new List<(Element Sleeve, IdType SleeveLinkId, Transform SleeveTransform)>();

                // 1a. 主模型套管
                var mainSleeves = new FilteredElementCollector(mainDoc, activeView.Id)
                    .WhereElementIsNotElementType()
                    .WherePasses(new ElementMulticategoryFilter(new List<BuiltInCategory> {
                        BuiltInCategory.OST_PipeAccessory,
                        BuiltInCategory.OST_GenericModel
                    }))
                    .ToList();

                foreach (var s in mainSleeves)
                    sleeveEntries.Add((s, 0, Transform.Identity));

                // 1b. 連結模型套管
                foreach (var li in linkInstances)
                {
                    Document linkDoc = li.GetLinkDocument();
                    if (linkDoc == null) continue;

                    Transform lTr = li.GetTotalTransform();
                    IdType liId = li.Id.GetIdValue();

                    var linkedSleeves = new FilteredElementCollector(linkDoc)
                        .WhereElementIsNotElementType()
                        .WherePasses(new ElementMulticategoryFilter(new List<BuiltInCategory> {
                            BuiltInCategory.OST_PipeAccessory,
                            BuiltInCategory.OST_GenericModel
                        }))
                        .ToList();

                    foreach (var s in linkedSleeves)
                        sleeveEntries.Add((s, liId, lTr));
                }

                var checkResults = new List<object>();

                foreach (var (sleeve, sleeveLinkId, sleeveTr) in sleeveEntries)
                {
                    BoundingBoxXYZ sleeveBBoxLocal = sleeve.get_BoundingBox(null);
                    if (sleeveBBoxLocal == null) continue;

                    // 轉換至世界座標
                    XYZ wMin = sleeveTr.OfPoint(sleeveBBoxLocal.Min);
                    XYZ wMax = sleeveTr.OfPoint(sleeveBBoxLocal.Max);
                    XYZ sleeveCenter = (wMin + wMax) * 0.5;

                    // 關鍵字篩選
                    string comments = sleeve.LookupParameter("備註")?.AsString() ?? "";
                    string familyName = (sleeve as FamilyInstance)?.Symbol?.FamilyName ?? "";
                    string typeName = sleeve.Name;

                    bool matchSleeve;
                    if (!string.IsNullOrEmpty(keywordParam))
                    {
                        matchSleeve = comments.Contains(keywordParam) ||
                                     familyName.IndexOf(keywordParam, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                     typeName.IndexOf(keywordParam, StringComparison.OrdinalIgnoreCase) >= 0;
                    }
                    else
                    {
                        bool hasSleeveKeyword = familyName.IndexOf("Sleeve", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                               typeName.IndexOf("Sleeve", StringComparison.OrdinalIgnoreCase) >= 0;
                        matchSleeve = comments.Contains("穿梁") || familyName.Contains("穿梁") ||
                                     familyName.Contains("套管") || hasSleeveKeyword;
                    }
                    if (!matchSleeve) continue;

                    double sleeveD = GetSleeveDiameter(sleeve, sleeveBBoxLocal, diamParams);
                    string sleeveLevel = GetReferenceLevelName(sleeve);

                    // 搜尋所有連結模型中的梁
                    foreach (var li in linkInstances)
                    {
                        Document linkDoc = li.GetLinkDocument();
                        if (linkDoc == null) continue;

                        Transform bTr = li.GetTotalTransform();
                        IdType beamLinkId = li.Id.GetIdValue();

                        // 將套管世界座標 bbox 轉換至梁的連結模型本地座標
                        XYZ localMin = bTr.Inverse.OfPoint(wMin);
                        XYZ localMax = bTr.Inverse.OfPoint(wMax);
                        Outline sleeveOutline = new Outline(
                            new XYZ(Math.Min(localMin.X, localMax.X), Math.Min(localMin.Y, localMax.Y), Math.Min(localMin.Z, localMax.Z)),
                            new XYZ(Math.Max(localMin.X, localMax.X), Math.Max(localMin.Y, localMax.Y), Math.Max(localMin.Z, localMax.Z))
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

                            double distToStart = -1, distToEnd = -1, minDist = -1, perpDistXY = 0;
                            double distToStartFace = -1, distToEndFace = -1;
                            double connDepthStart = -1, connDepthEnd = -1;
                            XYZ worldP0 = XYZ.Zero, worldP1 = XYZ.Zero;

                            LocationCurve beamLoc = b.Location as LocationCurve;
                            if (beamLoc != null)
                            {
                                Curve bc = beamLoc.Curve;
                                XYZ localSleeveCenter = bTr.Inverse.OfPoint(sleeveCenter);
                                IntersectionResult ir = bc.Project(localSleeveCenter);
                                if (ir != null)
                                {
                                    distToStart = ir.XYZPoint.DistanceTo(bc.GetEndPoint(0));
                                    distToEnd = ir.XYZPoint.DistanceTo(bc.GetEndPoint(1));
                                    minDist = Math.Min(distToStart, distToEnd);
                                    perpDistXY = new XYZ(localSleeveCenter.X - ir.XYZPoint.X, localSleeveCenter.Y - ir.XYZPoint.Y, 0).GetLength() * 304.8;
                                }

                                worldP0 = bTr.OfPoint(bc.GetEndPoint(0));
                                worldP1 = bTr.OfPoint(bc.GetEndPoint(1));
                                XYZ worldBeamDir = (worldP1 - worldP0).Normalize();

                                // MinDistanceFace：扣除柱/牆/大梁的寬度補償
                                double offsetStart = GetCollisionWidthAtPoint(mainDoc, worldP0, worldBeamDir, b.Id);
                                double offsetEnd   = GetCollisionWidthAtPoint(mainDoc, worldP1, worldBeamDir, b.Id);
                                distToStartFace = distToStart - offsetStart / 2.0;
                                distToEndFace   = distToEnd   - offsetEnd   / 2.0;

                                // 起點相連梁深
                                var conn0 = beamLoc.get_ElementsAtJoin(0);
                                if (conn0 != null) foreach (Element elem in conn0)
                                {
                                    if (elem == null || elem.Id == b.Id) continue;
                                    if (elem.Category?.Id.IntegerValue == (int)BuiltInCategory.OST_StructuralFraming)
                                    { connDepthStart = GetBeamDepth(elem) * 304.8; break; }
                                }

                                // 終點相連梁深
                                var conn1 = beamLoc.get_ElementsAtJoin(1);
                                if (conn1 != null) foreach (Element elem in conn1)
                                {
                                    if (elem == null || elem.Id == b.Id) continue;
                                    if (elem.Category?.Id.IntegerValue == (int)BuiltInCategory.OST_StructuralFraming)
                                    { connDepthEnd = GetBeamDepth(elem) * 304.8; break; }
                                }
                            }

                            // 最近正交小梁偵測
                            FamilyInstance nearestSideBeam = null;
                            double minSideBeamDist = double.MaxValue;
                            double nearestSideBeamDepth = 0, nearestSideBeamWidth = 0;

                            if (beamLoc != null && beamInstance != null)
                            {
                                XYZ beamDir = (worldP1 - worldP0).Normalize();
                                BoundingBoxXYZ bBox = b.get_BoundingBox(null);
                                if (bBox != null)
                                {
                                    Outline localBeamOutline = new Outline(bBox.Min, bBox.Max);
                                    var candidates = new FilteredElementCollector(linkDoc)
                                        .OfCategory(BuiltInCategory.OST_StructuralFraming)
                                        .WherePasses(new BoundingBoxIntersectsFilter(localBeamOutline))
                                        .WhereElementIsNotElementType()
                                        .Cast<FamilyInstance>()
                                        .Where(ob => ob.Id != b.Id)
                                        .ToList();

                                    XYZ localSleeveCenter = bTr.Inverse.OfPoint(sleeveCenter);
                                    IntersectionResult irSleeve = beamLoc.Curve.Project(localSleeveCenter);

                                    if (irSleeve != null)
                                    {
                                        foreach (var ob in candidates)
                                        {
                                            LocationCurve obLoc = ob.Location as LocationCurve;
                                            if (obLoc == null) continue;
                                            XYZ obDir = bTr.OfVector((obLoc.Curve.GetEndPoint(1) - obLoc.Curve.GetEndPoint(0)).Normalize()).Normalize();
                                            if (Math.Abs(beamDir.DotProduct(obDir)) >= 0.5) continue;

                                            IntersectionResult p0r = beamLoc.Curve.Project(obLoc.Curve.GetEndPoint(0));
                                            IntersectionResult p1r = beamLoc.Curve.Project(obLoc.Curve.GetEndPoint(1));
                                            double d0 = p0r?.XYZPoint.DistanceTo(irSleeve.XYZPoint) ?? double.MaxValue;
                                            double d1 = p1r?.XYZPoint.DistanceTo(irSleeve.XYZPoint) ?? double.MaxValue;
                                            double dist = Math.Min(d0, d1);

                                            if (dist < minSideBeamDist && dist < (3000.0 / 304.8))
                                            {
                                                minSideBeamDist = dist;
                                                nearestSideBeam = ob;
                                                nearestSideBeamDepth = GetBeamDepth(ob) * 304.8;
                                                nearestSideBeamWidth = GetBeamWidth(ob) * 304.8;
                                            }
                                        }
                                    }
                                }
                            }

                            BoundingBoxXYZ beamBox = b.get_BoundingBox(null);
                            double beamBottomZ = beamBox != null ? bTr.OfPoint(beamBox.Min).Z : 0;
                            double beamTopZ = beamBottomZ + beamDepth;

                            bool isExcluded = GetSleeveLength(sleeve) * 304.8 < beamWidth * 304.8 - 10.0
                                          || perpDistXY > (beamWidth * 304.8 / 2.0) + 100.0;
                            string exclusionReason = GetSleeveLength(sleeve) * 304.8 < beamWidth * 304.8 - 10.0
                                ? "套管長度小於梁寬，無法穿梁"
                                : (perpDistXY > (beamWidth * 304.8 / 2.0) + 100.0
                                    ? "套管偏離梁中心線過遠，疑似平行穿牆套管" : "");

                            checkResults.Add(new {
                                SleeveId      = sleeve.Id.GetIdValue(),
                                SleeveLinkId  = sleeveLinkId,
                                SleeveLevel   = sleeveLevel,
                                BeamId        = b.Id.GetIdValue(),
                                BeamLinkId    = beamLinkId,
                                BeamName      = b.Name,
                                BeamLevel     = beamLevel,
                                BeamUsage     = beamInstance != null ? DetermineBeamUsage(beamInstance) : "Major",
                                IsNearWall    = sleeveLinkId == 0 ? CheckIsIntersectsWithWall(mainDoc, sleeve) : false,
                                BeamDepth     = beamDepth  * 304.8,
                                BeamWidth     = beamWidth  * 304.8,
                                SleeveDiameter = sleeveD   * 304.8,
                                SleeveLength  = GetSleeveLength(sleeve) * 304.8,
                                IsExcluded    = isExcluded,
                                ExclusionReason = exclusionReason,
                                DistanceToStart = distToStart * 304.8,
                                DistanceToEnd   = distToEnd   * 304.8,
                                MinDistance     = minDist     * 304.8,
                                MinDistanceFace = Math.Min(distToStartFace, distToEndFace) * 304.8,
                                PerpDistXY      = perpDistXY,
                                ConnectedBeamDepthStart = connDepthStart,
                                ConnectedBeamDepthEnd   = connDepthEnd,
                                SleeveZ   = sleeveCenter.Z * 304.8,
                                BeamTopZ  = beamTopZ  * 304.8,
                                BeamBottomZ = beamBottomZ * 304.8,
                                BeamStartX = worldP0.X * 304.8,
                                BeamStartY = worldP0.Y * 304.8,
                                BeamEndX   = worldP1.X * 304.8,
                                BeamEndY   = worldP1.Y * 304.8,
                                NearestSideBeamId    = nearestSideBeam?.Id.GetIdValue() ?? 0,
                                NearestSideBeamName  = nearestSideBeam?.Name ?? "",
                                NearestSideBeamDepth = nearestSideBeamDepth,
                                NearestSideBeamWidth = nearestSideBeamWidth,
                                DistToNearestSideBeamCenter = minSideBeamDist == double.MaxValue ? -1 : minSideBeamDist * 304.8,
                                SleeveX = sleeveCenter.X * 304.8,
                                SleeveY = sleeveCenter.Y * 304.8
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

            double? paramVal = GetParameterValueWithFallback(sleeve, paramNames);
            if (paramVal.HasValue) return paramVal.Value;

            Element type = sleeve.Document.GetElement(sleeve.GetTypeId());
            if (type != null) {
                var match = System.Text.RegularExpressions.Regex.Match(type.Name, @"(\d+)");
                if (match.Success) {
                    double nameVal = double.Parse(match.Groups[1].Value) / 304.8;
                    foreach (Parameter tp in type.Parameters) {
                        if (tp.StorageType == StorageType.Double && Math.Abs(tp.AsDouble() - nameVal) < 0.01) return tp.AsDouble();
                    }
                }
            }

            if (bb != null) {
                return Math.Max(bb.Max.X - bb.Min.X, bb.Max.Y - bb.Min.Y);
            }
            return 0.1 / 0.3048;
        }

        private double GetBeamDepth(Element beam) {
            Document doc = beam.Document;
            Element type = doc.GetElement(beam.GetTypeId());

            if (type != null) {
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

                Parameter hp = type.LookupParameter("h") ?? type.LookupParameter("深度") ?? type.LookupParameter("Depth") ?? type.LookupParameter("Height");
                if (hp != null && hp.HasValue && hp.StorageType == StorageType.Double) return hp.AsDouble();
            }

            BoundingBoxXYZ bb = beam.get_BoundingBox(null);
            if (bb != null) {
                double h = bb.Max.Z - bb.Min.Z;
                if (h > 0.8 / 0.3048 && h < 5.0 / 0.3048) return h;
            }
            return 700.0 / 304.8;
        }
    }
}
