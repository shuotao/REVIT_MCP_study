using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCP.Models;

#if REVIT2025_OR_GREATER
using IdType = System.Int64;
#else
using IdType = System.Int32;
#endif

namespace RevitMCP.Core
{
    public partial class CommandExecutor
    {
        /// <summary>
        /// 1. 掃描目前視圖中所有被套管穿過的梁 (V3.0)
        /// </summary>
        private object ScanPenetratedBeamsInView(JObject parameters)
        {
            Document mainDoc = _uiApp.ActiveUIDocument.Document;
            View activeView = mainDoc.ActiveView;
            
            // 獲取視圖中所有套管
            var sleeves = new FilteredElementCollector(mainDoc, activeView.Id)
                .WhereElementIsNotElementType()
                .WherePasses(new ElementMulticategoryFilter(new List<BuiltInCategory> { 
                    BuiltInCategory.OST_PipeAccessory, 
                    BuiltInCategory.OST_GenericModel 
                })).ToList();

            var results = new Dictionary<string, dynamic>();

            // 獲取視圖中所有連結模型
            var linkInstances = new FilteredElementCollector(mainDoc, activeView.Id)
                .OfClass(typeof(RevitLinkInstance))
                .Cast<RevitLinkInstance>()
                .ToList();

            foreach (var sleeve in sleeves)
            {
                BoundingBoxXYZ sleeveBBox = sleeve.get_BoundingBox(null);
                if (sleeveBBox == null) continue;

                // 幾何碰撞第一層篩選已移至內部 linkBeams 迴圈：
                // 不在此處直接 continue 排除碰牆/碰板的套管，以免誤判梁側貼牆的合法套管。

                foreach (var li in linkInstances)
                {
                    Document linkDoc = li.GetLinkDocument();
                    if (linkDoc == null) continue;

                    Transform tr = li.GetTotalTransform();
                    Outline sleeveOutline = new Outline(tr.Inverse.OfPoint(sleeveBBox.Min), tr.Inverse.OfPoint(sleeveBBox.Max));

                    // 只針對視圖中「看得到」的梁進行幾何碰撞
                    var linkBeams = new FilteredElementCollector(linkDoc)
                        .OfCategory(BuiltInCategory.OST_StructuralFraming)
                        .WherePasses(new BoundingBoxIntersectsFilter(sleeveOutline))
                        .WhereElementIsNotElementType();

                    foreach (var b in linkBeams)
                    {
                        // 判定貼梁牆/貼梁板之過濾排除：
                        // 如果套管同時與牆或板相交，且套管長度與該相交梁的寬度不匹配 (誤差 > 10mm)，則排除。
                        bool intersectsWall = CheckIsIntersectsWithWall(mainDoc, sleeve);
                        bool intersectsFloor = CheckIsIntersectsWithFloor(mainDoc, sleeve);
                        if (intersectsWall || intersectsFloor)
                        {
                            double sleeveLen = GetSleeveLength(sleeve) * 304.8;
                            double beamWidthMM = GetBeamWidth(b) * 304.8;
                            
                            double perpDistXY = 0;
                            LocationCurve beamLoc = b.Location as LocationCurve;
                            if (beamLoc != null)
                            {
                                XYZ sleeveCenter = (sleeveBBox.Min + sleeveBBox.Max) * 0.5;
                                XYZ localSleeveCenter = tr.Inverse.OfPoint(sleeveCenter);
                                IntersectionResult ir = beamLoc.Curve.Project(localSleeveCenter);
                                if (ir != null)
                                {
                                    perpDistXY = new XYZ(localSleeveCenter.X - ir.XYZPoint.X, localSleeveCenter.Y - ir.XYZPoint.Y, 0).GetLength() * 304.8;
                                }
                            }

                            if (sleeveLen < beamWidthMM - 10.0 || perpDistXY > (beamWidthMM / 2.0) + 100.0)
                            {
                                continue; // 長度小於梁寬或偏離過遠，跳過
                            }
                        }

                        string key = $"{li.Id.GetIdValue()}_{b.Id.GetIdValue()}";
                        if (!results.ContainsKey(key))
                        {
                            results[key] = new { 
                                BeamId = b.Id.GetIdValue(), 
                                LinkId = li.Id.GetIdValue(), 
                                Name = b.Name, 
                                Level = GetReferenceLevelName(b), 
                                SleeveIds = new List<IdType>() 
                            };
                        }
                        results[key].SleeveIds.Add(sleeve.Id.GetIdValue());
                    }
                }
            }
            return new { Count = results.Count, Beams = results.Values.ToList() };
        }

        /// <summary>
        /// 2. 深度分析穿梁幾何
        /// </summary>
        private object AnalyzeBeamPenetration(JObject parameters)
        {
            try {
                IdType beamId = parameters["beamId"]?.Value<IdType>() ?? 0;
                IdType linkId = parameters["linkInstanceId"]?.Value<IdType>() ?? 0;

                // 接收外部傳遞的動態參數名稱 (若無則由方法內部給定預設值)
                string[] diamParams = parameters["diameterParamNames"]?.ToObject<string[]>();
                string[] lenParams = parameters["lengthParamNames"]?.ToObject<string[]>();
                string[] widthParams = parameters["widthParamNames"]?.ToObject<string[]>();

                Document mainDoc = _uiApp.ActiveUIDocument.Document;
                Document doc = mainDoc;
                Transform tr = Transform.Identity;
                
                if (linkId != 0) {
                    var link = mainDoc.GetElement(new ElementId(linkId)) as RevitLinkInstance;
                    if (link == null) throw new Exception($"找不到連結模型實體 (ID: {linkId})");
                    doc = link.GetLinkDocument();
                    if (doc == null) throw new Exception("無法取得連結模型的文件 (Document 為 null)");
                    tr = link.GetTotalTransform();
                }

                Element beamElem = doc.GetElement(new ElementId(beamId));
                if (beamElem == null) throw new Exception($"在目標文件中找不到梁 (ID: {beamId})");
                
                FamilyInstance beam = beamElem as FamilyInstance;
                if (beam == null) throw new Exception($"元素 (ID: {beamId}) 不是 FamilyInstance (目前為 {beamElem.GetType().Name})");

                string beamLevel = GetReferenceLevelName(beam);
                double beamDepth = GetBeamDepth(beam);
                double beamWidth = GetBeamWidth(beam, widthParams);

                BoundingBoxXYZ beamBox = beam.get_BoundingBox(null);
                if (beamBox == null) throw new Exception($"無法取得梁 (ID: {beamId}) 的 BoundingBox");

                // 將梁的 BoundingBox 轉換為世界座標中的 Outline
                XYZ worldMin = tr.OfPoint(beamBox.Min);
                XYZ worldMax = tr.OfPoint(beamBox.Max);
                Outline beamOutline = new Outline(
                    new XYZ(Math.Min(worldMin.X, worldMax.X), Math.Min(worldMin.Y, worldMax.Y), Math.Min(worldMin.Z, worldMax.Z)),
                    new XYZ(Math.Max(worldMin.X, worldMax.X), Math.Max(worldMin.Y, worldMax.Y), Math.Max(worldMin.Z, worldMax.Z))
                );

                List<Element> sleeves = new List<Element>();
                IdType[] targetSleeveIds = parameters["sleeveIds"]?.ToObject<IdType[]>();

                if (targetSleeveIds != null && targetSleeveIds.Length > 0) {
                    // 如果外部已經明確指定了套管清單 (精確名單模式)，直接讀取實體
                    foreach (IdType sId in targetSleeveIds) {
                        Element sl = mainDoc.GetElement(new ElementId(sId));
                        if (sl != null) sleeves.Add(sl);
                    }
                } else {
                    // 退回舊有邏輯：在主模型的當前視圖中，尋找與梁相交的套管
                    View activeView = mainDoc.ActiveView;
                    sleeves = new FilteredElementCollector(mainDoc, activeView.Id)
                        .WhereElementIsNotElementType()
                        .WherePasses(new BoundingBoxIntersectsFilter(beamOutline))
                        .WherePasses(new ElementMulticategoryFilter(new List<BuiltInCategory> { 
                            BuiltInCategory.OST_PipeAccessory, 
                            BuiltInCategory.OST_GenericModel 
                        })).ToList();
                }

                Outline localBeamOutline = new Outline(beamBox.Min, beamBox.Max);
                List<FamilyInstance> orthogonalBeams = new List<FamilyInstance>();
                LocationCurve beamLocForOrthogonal = beam.Location as LocationCurve;
                if (beamLocForOrthogonal != null) {
                    XYZ beamDir = (beamLocForOrthogonal.Curve.GetEndPoint(1) - beamLocForOrthogonal.Curve.GetEndPoint(0)).Normalize();
                    var intersectingBeams = new FilteredElementCollector(doc)
                        .OfCategory(BuiltInCategory.OST_StructuralFraming)
                        .WherePasses(new BoundingBoxIntersectsFilter(localBeamOutline))
                        .WhereElementIsNotElementType()
                        .Cast<FamilyInstance>()
                        .Where(b => b.Id != beam.Id)
                        .ToList();

                    foreach (var ib in intersectingBeams) {
                        LocationCurve ibLoc = ib.Location as LocationCurve;
                        if (ibLoc != null) {
                            XYZ ibDir = (ibLoc.Curve.GetEndPoint(1) - ibLoc.Curve.GetEndPoint(0)).Normalize();
                            if (Math.Abs(beamDir.DotProduct(ibDir)) < 0.5) { // 正交或大於 60 度
                                orthogonalBeams.Add(ib);
                            }
                        }
                    }
                }

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

                    bool hasSleeveKeyword = familyName.IndexOf("Sleeve", StringComparison.OrdinalIgnoreCase) >= 0 || 
                                           typeName.IndexOf("Sleeve", StringComparison.OrdinalIgnoreCase) >= 0;
                    bool isSleeve = comments.Contains("穿梁") || familyName.Contains("穿梁") || familyName.Contains("套管") || hasSleeveKeyword;

                    if (!isSleeve) continue;

                    XYZ sleeveCenter = (sleeveBBox.Min + sleeveBBox.Max) * 0.5;
                    double sleeveD = GetSleeveDiameter(sleeve, sleeveBBox, diamParams);
                    string sleeveLevel = GetReferenceLevelName(sleeve);

                    double distToStart = -1;
                    double distToEnd = -1;
                    double minDist = -1;
                    double perpDistXY = 0;
                    double connDepthStart = -1;
                    double connDepthEnd = -1;
                    double distToStartFace = -1;
                    double distToEndFace = -1;

                    LocationCurve beamLoc = beam.Location as LocationCurve;
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
                        
                        distToStartFace = distToStart;
                        distToEndFace = distToEnd;

                        XYZ beamDirTemp = (bc.GetEndPoint(1) - bc.GetEndPoint(0)).Normalize();
                        XYZ worldP0Temp = tr.OfPoint(bc.GetEndPoint(0));
                        XYZ worldP1Temp = tr.OfPoint(bc.GetEndPoint(1));
                        XYZ worldBeamDirTemp = tr.OfVector(beamDirTemp).Normalize();

                        double offsetStart = GetCollisionWidthAtPoint(mainDoc, worldP0Temp, worldBeamDirTemp, beam.Id);
                        double offsetEnd = GetCollisionWidthAtPoint(mainDoc, worldP1Temp, worldBeamDirTemp, beam.Id);

                        distToStartFace = distToStart - offsetStart / 2.0;
                        distToEndFace = distToEnd - offsetEnd / 2.0;

                        // 偵測起點(0)相連結構取得大梁深度
                        var conn0 = beamLoc.get_ElementsAtJoin(0);
                        if (conn0 != null)
                        {
                            foreach (ElementId id in conn0)
                            {
                                Element elem = doc.GetElement(id);
                                if (elem == null || elem.Id == beam.Id) continue;
                                if (elem.Category != null)
                                {
                                    int catId = elem.Category.Id.IntegerValue;
                                    if (catId == (int)BuiltInCategory.OST_StructuralFraming) {
                                        connDepthStart = GetBeamDepth(elem) * 304.8;
                                        break;
                                    }
                                }
                            }
                        }

                        // 偵測終點(1)相連結構取得大梁深度
                        var conn1 = beamLoc.get_ElementsAtJoin(1);
                        if (conn1 != null)
                        {
                            foreach (ElementId id in conn1)
                            {
                                Element elem = doc.GetElement(id);
                                if (elem == null || elem.Id == beam.Id) continue;
                                if (elem.Category != null)
                                {
                                    int catId = elem.Category.Id.IntegerValue;
                                    if (catId == (int)BuiltInCategory.OST_StructuralFraming) {
                                        connDepthEnd = GetBeamDepth(elem) * 304.8;
                                        break;
                                    }
                                }
                            }
                        }
                    }

                    double beamBottomZ = worldMin.Z;
                    double beamTopZ = beamBottomZ + beamDepth; // 扣除梁頂增築：以梁底標高加上型號梁深作為結構梁原本頂標高

                    XYZ worldP0 = XYZ.Zero;
                    XYZ worldP1 = XYZ.Zero;
                    if (beamLoc != null)
                    {
                        worldP0 = tr.OfPoint(beamLoc.Curve.GetEndPoint(0));
                        worldP1 = tr.OfPoint(beamLoc.Curve.GetEndPoint(1));
                    }

                    FamilyInstance nearestSideBeam = null;
                    double minSideBeamDist = double.MaxValue;
                    double nearestSideBeamDepth = 0;
                    double nearestSideBeamWidth = 0;

                    if (beamLoc != null)
                    {
                        XYZ sleeveLocalProj = tr.Inverse.OfPoint(sleeveCenter);
                        IntersectionResult irSleeve = beamLoc.Curve.Project(sleeveLocalProj);
                        if (irSleeve != null)
                        {
                            foreach (var ob in orthogonalBeams) {
                                LocationCurve obLoc = ob.Location as LocationCurve;
                                if (obLoc == null) continue;
                                IntersectionResult p0 = beamLoc.Curve.Project(obLoc.Curve.GetEndPoint(0));
                                IntersectionResult p1 = beamLoc.Curve.Project(obLoc.Curve.GetEndPoint(1));
                                
                                double d0 = p0 != null ? p0.XYZPoint.DistanceTo(irSleeve.XYZPoint) : double.MaxValue;
                                double d1 = p1 != null ? p1.XYZPoint.DistanceTo(irSleeve.XYZPoint) : double.MaxValue;
                                
                                double distToSideBeamCenter = Math.Min(d0, d1);
                                
                                if (distToSideBeamCenter < minSideBeamDist && distToSideBeamCenter < (3000.0 / 304.8)) {
                                    minSideBeamDist = distToSideBeamCenter;
                                    nearestSideBeam = ob;
                                    nearestSideBeamDepth = GetBeamDepth(ob) * 304.8;
                                    nearestSideBeamWidth = GetBeamWidth(ob) * 304.8;
                                }
                            }
                        }
                    }

                    checkResults.Add(new {
                        SleeveId = sleeve.Id.GetIdValue(),
                        SleeveLevel = sleeveLevel,
                        BeamId = beamId,
                        BeamName = beam.Name,
                        BeamLevel = beamLevel,
                        BeamUsage = DetermineBeamUsage(beam),
                        IsNearWall = CheckIsIntersectsWithWall(mainDoc, sleeve),
                        BeamDepth = beamDepth * 304.8, // mm
                        BeamWidth = beamWidth * 304.8, // mm
                        SleeveDiameter = sleeveD * 304.8, // mm
                        SleeveLength = GetSleeveLength(sleeve, lenParams) * 304.8, // mm
                        IsExcluded = GetSleeveLength(sleeve, lenParams) * 304.8 < beamWidth * 304.8 - 10.0 || perpDistXY > (beamWidth * 304.8 / 2.0) + 100.0,
                        ExclusionReason = GetSleeveLength(sleeve, lenParams) * 304.8 < beamWidth * 304.8 - 10.0 ? "套管長度小於梁寬，無法穿梁" : (perpDistXY > (beamWidth * 304.8 / 2.0) + 100.0 ? "套管偏離梁中心線過遠，疑似平行穿牆套管" : ""),
                        DistanceToStart = distToStart * 304.8,
                        DistanceToEnd = distToEnd * 304.8,
                        MinDistance = minDist * 304.8,
                        MinDistanceFace = Math.Min(distToStartFace, distToEndFace) * 304.8,
                        PerpDistXY = perpDistXY,
                        ConnectedBeamDepthStart = connDepthStart,
                        ConnectedBeamDepthEnd = connDepthEnd,
                        SleeveZ = sleeveCenter.Z * 304.8,
                        BeamTopZ = beamTopZ * 304.8,
                        BeamBottomZ = beamBottomZ * 304.8,
                        BeamStartX = worldP0.X * 304.8,
                        BeamStartY = worldP0.Y * 304.8,
                        BeamEndX = worldP1.X * 304.8,
                        BeamEndY = worldP1.Y * 304.8,
                        NearestSideBeamId = nearestSideBeam?.Id.GetIdValue() ?? 0,
                        NearestSideBeamName = nearestSideBeam?.Name ?? "",
                        NearestSideBeamDepth = nearestSideBeamDepth,
                        NearestSideBeamWidth = nearestSideBeamWidth,
                        DistToNearestSideBeamCenter = minSideBeamDist == double.MaxValue ? -1 : minSideBeamDist * 304.8,
                        SleeveX = sleeveCenter.X * 304.8,
                        SleeveY = sleeveCenter.Y * 304.8
                    });
                }

                return new { 
                    Success = true, 
                    Results = checkResults 
                };
            } catch (Exception ex) {
                return new { Success = false, Error = "分析失敗: " + ex.Message + "\n" + ex.StackTrace };
            }
        }

        private bool IsPointNearColumn(Document doc, XYZ point)
        {
            // 建立一個約 15cm (0.5 英尺) 大小的 Bounding Box 包圍該端點，作為拓撲未連接時的備用幾何防線
            double size = 0.5; 
            Outline outline = new Outline(
                new XYZ(point.X - size / 2, point.Y - size / 2, point.Z - size / 2),
                new XYZ(point.X + size / 2, point.Y + size / 2, point.Z + size / 2)
            );

            // 1. 搜尋主模型中的結構柱
            var columns = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_StructuralColumns)
                .WherePasses(new BoundingBoxIntersectsFilter(outline))
                .WhereElementIsNotElementType()
                .ToList();

            if (columns.Count > 0) return true;

            // 2. 搜尋連結模型中的結構柱
            var linkInstances = new FilteredElementCollector(doc)
                .OfClass(typeof(RevitLinkInstance))
                .Cast<RevitLinkInstance>()
                .ToList();

            foreach (var li in linkInstances)
            {
                Document linkDoc = li.GetLinkDocument();
                if (linkDoc == null) continue;

                Transform tr = li.GetTotalTransform();
                XYZ localPoint = tr.Inverse.OfPoint(point);
                Outline localOutline = new Outline(
                    new XYZ(localPoint.X - size / 2, localPoint.Y - size / 2, localPoint.Z - size / 2),
                    new XYZ(localPoint.X + size / 2, localPoint.Y + size / 2, localPoint.Z + size / 2)
                );

                var linkColumns = new FilteredElementCollector(linkDoc)
                    .OfCategory(BuiltInCategory.OST_StructuralColumns)
                    .WherePasses(new BoundingBoxIntersectsFilter(localOutline))
                    .WhereElementIsNotElementType()
                    .ToList();

                if (linkColumns.Count > 0) return true;
            }

            return false;
        }

        private bool IsBeamEndConnectedToColumn(FamilyInstance beam, int end)
        {
            try
            {
                LocationCurve beamLoc = beam.Location as LocationCurve;
                if (beamLoc == null) return false;

                Document doc = beam.Document;
                var connectedIds = beamLoc.get_ElementsAtJoin(end);
                if (connectedIds == null) return false;

                foreach (ElementId id in connectedIds)
                {
                    Element elem = doc.GetElement(id);
                    if (elem == null) continue;

                    // 檢查相連元素的品類是否為結構柱
                    if (elem.Category != null && elem.Category.Id.IntegerValue == (int)BuiltInCategory.OST_StructuralColumns)
                    {
                        return true;
                    }
                }
            }
            catch
            {
                // 忽略異常，退回幾何判定
            }
            return false;
        }

        private string DetermineBeamUsage(FamilyInstance beam)
        {
            // 1. 先使用內建參數判斷，若是 Joist 則直接判定為 Minor
            var usage = beam.StructuralUsage;
            if (usage == StructuralInstanceUsage.Joist) return "Minor";

            try
            {
                LocationCurve beamLoc = beam.Location as LocationCurve;
                if (beamLoc != null)
                {
                    // 2. 優先使用方案 A：拓撲相連檢查 (get_ElementsAtJoin)
                    bool p0Connected = IsBeamEndConnectedToColumn(beam, 0);
                    bool p1Connected = IsBeamEndConnectedToColumn(beam, 1);
                    if (p0Connected || p1Connected) return "Major";

                    // 3. 備用防禦方案 B：幾何微距碰撞檢查 (15cm 穩健公差範圍)
                    XYZ p0 = beamLoc.Curve.GetEndPoint(0);
                    XYZ p1 = beamLoc.Curve.GetEndPoint(1);

                    bool p0NearCol = IsPointNearColumn(beam.Document, p0);
                    bool p1NearCol = IsPointNearColumn(beam.Document, p1);

                    if (p0NearCol || p1NearCol)
                    {
                        return "Major";
                    }
                    else
                    {
                        return "Minor";
                    }
                }
            }
            catch
            {
                // 發生異常時退回 Major
            }

            return "Major";
        }

        private bool CheckIsIntersectsWithWall(Document mainDoc, Element sleeve)
        {
            BoundingBoxXYZ bbox = sleeve.get_BoundingBox(null);
            if (bbox == null) return false;
            
            Outline outline = new Outline(bbox.Min, bbox.Max);
            
            // 1. 檢查主模型中的牆
            var walls = new FilteredElementCollector(mainDoc)
                .OfCategory(BuiltInCategory.OST_Walls)
                .WherePasses(new BoundingBoxIntersectsFilter(outline))
                .WhereElementIsNotElementType()
                .ToList();
                
            if (walls.Count > 0) return true;
            
            // 2. 檢查連結模型中的牆
            var linkInstances = new FilteredElementCollector(mainDoc)
                .OfClass(typeof(RevitLinkInstance))
                .Cast<RevitLinkInstance>()
                .ToList();
                
            foreach (var li in linkInstances)
            {
                Document linkDoc = li.GetLinkDocument();
                if (linkDoc == null) continue;
                
                Transform tr = li.GetTotalTransform();
                
                // 將套管的 BoundingBox 轉換至連結模型坐標系
                XYZ localMin = tr.Inverse.OfPoint(bbox.Min);
                XYZ localMax = tr.Inverse.OfPoint(bbox.Max);
                Outline localOutline = new Outline(
                    new XYZ(Math.Min(localMin.X, localMax.X), Math.Min(localMin.Y, localMax.Y), Math.Min(localMin.Z, localMax.Z)),
                    new XYZ(Math.Max(localMin.X, localMax.X), Math.Max(localMin.Y, localMax.Y), Math.Max(localMin.Z, localMax.Z))
                );
                
                var linkWalls = new FilteredElementCollector(linkDoc)
                    .OfCategory(BuiltInCategory.OST_Walls)
                    .WherePasses(new BoundingBoxIntersectsFilter(localOutline))
                    .WhereElementIsNotElementType()
                    .ToList();
                    
                if (linkWalls.Count > 0) return true;
            }
            
            return false;
        }

        private bool CheckIsIntersectsWithFloor(Document mainDoc, Element sleeve)
        {
            BoundingBoxXYZ bbox = sleeve.get_BoundingBox(null);
            if (bbox == null) return false;
            
            Outline outline = new Outline(bbox.Min, bbox.Max);
            
            // 1. 檢查主模型中的樓板
            var floors = new FilteredElementCollector(mainDoc)
                .OfCategory(BuiltInCategory.OST_Floors)
                .WherePasses(new BoundingBoxIntersectsFilter(outline))
                .WhereElementIsNotElementType()
                .ToList();
                
            if (floors.Count > 0) return true;
            
            // 2. 檢查連結模型中的樓板
            var linkInstances = new FilteredElementCollector(mainDoc)
                .OfClass(typeof(RevitLinkInstance))
                .Cast<RevitLinkInstance>()
                .ToList();
                
            foreach (var li in linkInstances)
            {
                Document linkDoc = li.GetLinkDocument();
                if (linkDoc == null) continue;
                
                Transform tr = li.GetTotalTransform();
                
                XYZ localMin = tr.Inverse.OfPoint(bbox.Min);
                XYZ localMax = tr.Inverse.OfPoint(bbox.Max);
                Outline localOutline = new Outline(
                    new XYZ(Math.Min(localMin.X, localMax.X), Math.Min(localMin.Y, localMax.Y), Math.Min(localMin.Z, localMax.Z)),
                    new XYZ(Math.Max(localMin.X, localMax.X), Math.Max(localMin.Y, localMax.Y), Math.Max(localMin.Z, localMax.Z))
                );
                
                var linkFloors = new FilteredElementCollector(linkDoc)
                    .OfCategory(BuiltInCategory.OST_Floors)
                    .WherePasses(new BoundingBoxIntersectsFilter(localOutline))
                    .WhereElementIsNotElementType()
                    .ToList();
                    
                if (linkFloors.Count > 0) return true;
            }
            
            return false;
        }

        private object GetSrcBeamMapping(JObject parameters) { return new { Success = true }; }

        /// <summary>
        /// 3. 自動化標註與視覺化
        /// </summary>
        private object VisualizePenetration(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;
            JArray results = parameters["results"] as JArray;

            using (Transaction trans = new Transaction(doc, "自動化穿梁標註"))
            {
                trans.Start();

                // 1. 先清除該視圖中舊有的穿梁標註與詳圖線
                var oldElements = new FilteredElementCollector(doc, doc.ActiveView.Id)
                    .WhereElementIsNotElementType()
                    .Where(x => {
                        Parameter p = x.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                        if (p != null && p.HasValue && p.AsString() == "BeamPenetration_Helper") return true;
                        if (x is TextNote tn && (tn.Text.StartsWith("● PASS") || tn.Text.StartsWith("● FAIL"))) return true;
                        return false;
                    })
                    .Select(x => x.Id)
                    .ToList();
                
                if (oldElements.Count > 0) {
                    try {
                        doc.Delete(oldElements);
                    } catch {
                        // 忽略某些無法刪除的元素
                    }
                }

                // 為了讓標註錯開不重疊，紀錄每根梁目前已經標註的數量
                Dictionary<IdType, int> beamDimCounts = new Dictionary<IdType, int>();

                foreach (var res in results)
                {
                    IdType id = res["SleeveId"].Value<IdType>();
                    bool isOk = res["IsOk"].Value<bool>();
                    string msg = res["Message"].Value<string>();

                    Element sleeve = doc.GetElement(new ElementId(id));
                    if (sleeve == null) continue;

                    Color color = isOk ? new Color(0, 200, 0) : new Color(255, 0, 0);
                    OverrideGraphicSettings ogs = new OverrideGraphicSettings();
                    
                    // 同時設定線條與填充顏色，確保清晰可見
                    ogs.SetProjectionLineColor(color);
                    ogs.SetSurfaceForegroundPatternColor(color);
                    // 嘗試抓取固體填充樣式
                    FillPatternElement solidFill = new FilteredElementCollector(doc).OfClass(typeof(FillPatternElement)).Cast<FillPatternElement>().FirstOrDefault(x => x.GetFillPattern().IsSolidFill);
                    if (solidFill != null) ogs.SetSurfaceForegroundPatternId(solidFill.Id);

                    doc.ActiveView.SetElementOverrides(sleeve.Id, ogs);

                    // 文字標註強制放在視圖平面高度
                    XYZ pos = XYZ.Zero;
                    BoundingBoxXYZ bb = sleeve.get_BoundingBox(null);
                    if (bb != null) pos = (bb.Min + bb.Max) * 0.5;
                    
                    // 取得視圖切割平面高度
                    PlanViewRange vr = (doc.ActiveView as ViewPlan)?.GetViewRange();
                    if (vr != null) {
                        double cutPlaneH = (doc.ActiveView as ViewPlan).GenLevel.Elevation + vr.GetOffset(PlanViewPlane.CutPlane);
                        pos = new XYZ(pos.X, pos.Y, cutPlaneH);
                    }

                    // 獲取預設的文字類型 ID (Revit 2017+ 需要有效的類型)
                    ElementId defaultTextTypeId = doc.GetDefaultElementTypeId(ElementTypeGroup.TextNoteType);
                    if (defaultTextTypeId == ElementId.InvalidElementId) {
                        defaultTextTypeId = new FilteredElementCollector(doc).OfClass(typeof(TextNoteType)).FirstElementId();
                    }

                    // 確保文字標註沒有外框
                    TextNoteType baseType = doc.GetElement(defaultTextTypeId) as TextNoteType;
                    TextNoteType noBorderType = new FilteredElementCollector(doc)
                        .OfClass(typeof(TextNoteType))
                        .Cast<TextNoteType>()
                        .FirstOrDefault(t => t.get_Parameter(BuiltInParameter.TEXT_BOX_VISIBILITY) != null && t.get_Parameter(BuiltInParameter.TEXT_BOX_VISIBILITY).AsInteger() == 0);

                    if (noBorderType == null && baseType != null)
                    {
                        noBorderType = baseType.Duplicate(baseType.Name + "_NoBorder") as TextNoteType;
                        noBorderType.get_Parameter(BuiltInParameter.TEXT_BOX_VISIBILITY)?.Set(0);
                    }
                    
                    ElementId finalTypeId = noBorderType != null ? noBorderType.Id : defaultTextTypeId;

                    // 將文字方塊往右上角偏移 (2ft)，避免擋住套管
                    XYZ textPos = new XYZ(pos.X + 2.0, pos.Y + 2.0, pos.Z);
                    TextNote tn = TextNote.Create(doc, doc.ActiveView.Id, textPos, isOk ? "● PASS" : "● FAIL", finalTypeId);
                    
                    // 加入指引箭頭指向套管中心
                    Leader leader = tn.AddLeader(TextNoteLeaderTypes.TNLT_STRAIGHT_L);
                    leader.End = pos;
                    
                    tn.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.Set("BeamPenetration_Helper");
                    
                    OverrideGraphicSettings textOgs = new OverrideGraphicSettings();
                    textOgs.SetProjectionLineColor(color);
                    doc.ActiveView.SetElementOverrides(tn.Id, textOgs);

                }

                // 3. 處理同一根大梁上所有節點的排序與淨距標註
                Dictionary<IdType, List<Element>> beamSleeves = new Dictionary<IdType, List<Element>>();
                foreach (var res in results)
                {
                    IdType id = res["SleeveId"].Value<IdType>();
                    IdType bId = (res["BeamId"] != null && res["BeamId"].Type != Newtonsoft.Json.Linq.JTokenType.Null) ? res["BeamId"].Value<IdType>() : 0;
                    if (bId == 0) continue;
                    Element slv = doc.GetElement(new ElementId(id));
                    if (slv == null) continue;
                    if (!beamSleeves.ContainsKey(bId)) beamSleeves[bId] = new List<Element>();
                    beamSleeves[bId].Add(slv);
                }

                foreach (var kvp in beamSleeves)
                {
                    IdType bId = kvp.Key;
                    List<Element> slvs = kvp.Value;

                    Transform bTr;
                    Element bElem = FindElementInMainOrLinks(doc, bId, out bTr);
                    if (bElem == null) continue;
                    LocationCurve bLoc = bElem.Location as LocationCurve;
                    if (bLoc == null) continue;

                    CreateSequentialDimensions(doc, doc.ActiveView, bElem, slvs, bTr);
                }

                trans.Commit();
            }
            return new { Success = true };
        }

        private class DimNode
        {
            public XYZ LeftEdge;
            public XYZ RightEdge;
            public double CenterDist;
            public string NodeType; // "Boundary", "Sleeve", "OrthogonalBeam"
        }

        private void GetBeamEndFaces(Element beam, Transform tr, XYZ p0, XYZ p1, out XYZ faceEnd0, out XYZ faceEnd1)
        {
            faceEnd0 = p0;
            faceEnd1 = p1;
            
            LocationCurve beamLoc = beam.Location as LocationCurve;
            if (beamLoc == null) return;
            Curve bc = beamLoc.Curve;
            XYZ beamDir = (p1 - p0).Normalize();

            Options opt = new Options() { ComputeReferences = true, DetailLevel = ViewDetailLevel.Fine };
            GeometryElement geomElem = beam.get_Geometry(opt);
            if (geomElem != null)
            {
                double minDot0 = double.MaxValue;
                double minDot1 = double.MaxValue;

                Stack<GeometryObject> geoms = new Stack<GeometryObject>();
                foreach (GeometryObject go in geomElem) geoms.Push(go);

                while (geoms.Count > 0)
                {
                    GeometryObject go = geoms.Pop();
                    if (go is GeometryInstance geomInst)
                    {
                        foreach (GeometryObject instGo in geomInst.GetInstanceGeometry()) geoms.Push(instGo);
                    }
                    else if (go is Solid solid && solid.Faces.Size > 0 && solid.Volume > 0)
                    {
                        foreach (Face face in solid.Faces)
                        {
                            if (face is PlanarFace pf)
                            {
                                double dot = Math.Abs(pf.FaceNormal.DotProduct(tr.Inverse.OfVector(beamDir)));
                                if (dot > 0.8)
                                {
                                    XYZ faceCenter = pf.Evaluate(new UV(0.5, 0.5));
                                    XYZ globalFaceCenter = tr.OfPoint(faceCenter);
                                    
                                    IntersectionResult ir = bc.Project(tr.Inverse.OfPoint(globalFaceCenter));
                                    if (ir != null)
                                    {
                                        double distToP0 = globalFaceCenter.DistanceTo(p0);
                                        double distToP1 = globalFaceCenter.DistanceTo(p1);
                                        
                                        // 確保我們找到的截斷面「真的是在端點附近」，而不是因為穿牆而在梁中段產生的內部面
                                        double beamLength = p0.DistanceTo(p1);
                                        // 截斷面距離不可超過梁長的 1/3，且絕對值不超過 2 公尺 (2000mm)
                                        double maxAllowedCutback = Math.Min(beamLength / 3.0, 2000.0 / 304.8);
                                        
                                        if (distToP0 < distToP1)
                                        {
                                            if (distToP0 < minDot0 && distToP0 <= maxAllowedCutback)
                                            {
                                                minDot0 = distToP0;
                                                faceEnd0 = tr.OfPoint(ir.XYZPoint);
                                            }
                                        }
                                        else
                                        {
                                            if (distToP1 < minDot1 && distToP1 <= maxAllowedCutback)
                                            {
                                                minDot1 = distToP1;
                                                faceEnd1 = tr.OfPoint(ir.XYZPoint);
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        private void CreateSequentialDimensions(Document doc, View view, Element bElem, List<Element> slvs, Transform bTr)
        {
            try
            {
                LocationCurve bLoc = bElem.Location as LocationCurve;
                if (bLoc == null) return;
                Curve bc = bLoc.Curve;

                XYZ p0 = bTr.OfPoint(bc.GetEndPoint(0));
                XYZ p1 = bTr.OfPoint(bc.GetEndPoint(1));
                XYZ beamDir = (p1 - p0).Normalize();

                GetBeamEndFaces(bElem, bTr, p0, p1, out XYZ faceEnd0, out XYZ faceEnd1);

                List<DimNode> nodes = new List<DimNode>();

                // 1. Boundary Nodes (表示梁本身的端點/相連柱/相交大梁等邊界)
                nodes.Add(new DimNode { LeftEdge = faceEnd0, RightEdge = faceEnd0, CenterDist = p0.DistanceTo(faceEnd0), NodeType = "Boundary" });
                nodes.Add(new DimNode { LeftEdge = faceEnd1, RightEdge = faceEnd1, CenterDist = p0.DistanceTo(faceEnd1), NodeType = "Boundary" });

                // 2. Sleeve Nodes (套管)
                foreach (var s in slvs)
                {
                    BoundingBoxXYZ sbb = s.get_BoundingBox(view);
                    if (sbb == null) continue;
                    XYZ sCenter = (sbb.Min + sbb.Max) * 0.5;
                    IntersectionResult ir = bc.Project(bTr.Inverse.OfPoint(sCenter));
                    if (ir == null) continue;
                    XYZ projPoint = bTr.OfPoint(ir.XYZPoint);
                    double d = p0.DistanceTo(projPoint);
                    
                    double sleeveD = GetSleeveDiameter(s, sbb);
                    double radius = sleeveD / 2.0;

                    nodes.Add(new DimNode {
                        LeftEdge = projPoint - beamDir * radius,
                        RightEdge = projPoint + beamDir * radius,
                        CenterDist = d,
                        NodeType = "Sleeve"
                    });
                }

                // 3. Orthogonal Beam Nodes (正交小梁)
                Outline localBeamOutline = new Outline(bElem.get_BoundingBox(null).Min, bElem.get_BoundingBox(null).Max);
                var intersectingBeams = new FilteredElementCollector(bElem.Document)
                    .OfCategory(BuiltInCategory.OST_StructuralFraming)
                    .WherePasses(new BoundingBoxIntersectsFilter(localBeamOutline))
                    .WhereElementIsNotElementType()
                    .Cast<FamilyInstance>()
                    .Where(b => b.Id != bElem.Id)
                    .ToList();

                foreach (var ob in intersectingBeams)
                {
                    LocationCurve obLoc = ob.Location as LocationCurve;
                    if (obLoc == null) continue;
                    XYZ obDir = bTr.OfVector((obLoc.Curve.GetEndPoint(1) - obLoc.Curve.GetEndPoint(0)).Normalize()).Normalize();
                    
                    if (Math.Abs(beamDir.DotProduct(obDir)) < 0.7) 
                    {
                        // 實體極值投影法：直接剖析正交梁的實體幾何
                        Options opt = new Options() { ComputeReferences = true, DetailLevel = ViewDetailLevel.Fine };
                        GeometryElement geomElem = ob.get_Geometry(opt);
                        if (geomElem == null) continue;

                        double minParam = double.MaxValue;
                        double maxParam = double.MinValue;
                        bool foundAny = false;

                        Stack<GeometryObject> geoms = new Stack<GeometryObject>();
                        foreach (GeometryObject go in geomElem) geoms.Push(go);

                        while (geoms.Count > 0)
                        {
                            GeometryObject go = geoms.Pop();
                            if (go is GeometryInstance geomInst)
                            {
                                foreach (GeometryObject instGo in geomInst.GetInstanceGeometry()) geoms.Push(instGo);
                            }
                            else if (go is Solid solid && solid.Faces.Size > 0 && solid.Volume > 0)
                            {
                                foreach (Edge edge in solid.Edges)
                                {
                                    foreach (XYZ pt in edge.Tessellate())
                                    {
                                        IntersectionResult ir = bc.Project(pt);
                                        if (ir != null)
                                        {
                                            if (ir.Parameter < minParam) minParam = ir.Parameter;
                                            if (ir.Parameter > maxParam) maxParam = ir.Parameter;
                                            foundAny = true;
                                        }
                                    }
                                }
                            }
                        }

                        if (foundAny)
                        {
                            // 取得主梁端點在曲線上的參數位置
                            double p0Param = bc.Project(bTr.Inverse.OfPoint(p0))?.Parameter ?? 0;
                            double p1Param = bc.Project(bTr.Inverse.OfPoint(p1))?.Parameter ?? 1;

                            double startParam = Math.Min(p0Param, p1Param);
                            double endParam = Math.Max(p0Param, p1Param);

                            // 如果投影範圍至少有一部分落在主梁上
                            if (maxParam > startParam && minParam < endParam)
                            {
                                // 裁切超出大梁起迄範圍的部分
                                if (minParam < startParam) minParam = startParam;
                                if (maxParam > endParam) maxParam = endParam;

                                // 只有當跨度在合理範圍內 (例如 < 2m)，才認為它是正交交接，避免抓到平行貼合的超長梁
                                if (maxParam - minParam < (2000.0 / 304.8) && maxParam - minParam > 0.01)
                                {
                                    XYZ leftP = bTr.OfPoint(bc.Evaluate(minParam, false));
                                    XYZ rightP = bTr.OfPoint(bc.Evaluate(maxParam, false));
                                    
                                    // 確保 LeftEdge 永遠是靠近 p0 的那一端
                                    double dLeft = p0.DistanceTo(leftP);
                                    double dRight = p0.DistanceTo(rightP);

                                    nodes.Add(new DimNode {
                                        LeftEdge = dLeft < dRight ? leftP : rightP,
                                        RightEdge = dLeft < dRight ? rightP : leftP,
                                        CenterDist = (dLeft + dRight) / 2.0,
                                        NodeType = "OrthogonalBeam"
                                    });
                                }
                            }
                        }
                    }
                }

                // 按照距離排序 (由起點到終點)
                nodes = nodes.OrderBy(n => n.CenterDist).ToList();

                // Generate non-overlapping Dimensions
                for (int i = 0; i < nodes.Count - 1; i++)
                {
                    var A = nodes[i];
                    var B = nodes[i + 1];

                    // 只有當兩者之一包含套管時，才畫標註 (避免只畫出柱子到正交梁的無意義標註)
                    if (A.NodeType == "Sleeve" || B.NodeType == "Sleeve")
                    {
                        // 確保間距大於 0 才標註，避免交錯
                        double gap = B.CenterDist - A.CenterDist;
                        if (gap > 0.1) // 至少大於約 3cm (0.1ft) 避免重疊
                        {
                            // 使用交錯的錯開距離避免文字擠在一起
                            int staggerIndex = i % 2;
                            double offsetVal = 500.0 + (staggerIndex * 250.0);
                            
                            // 統一呼叫尺寸標註方法 (從 A 的右邊緣 到 B 的左邊緣)
                            CreateDimensionInternal(doc, view, A.RightEdge.X * 304.8, A.RightEdge.Y * 304.8, B.LeftEdge.X * 304.8, B.LeftEdge.Y * 304.8, offsetVal);
                        }
                    }
                }
            }
            catch (Exception) { }
        }

        /// <summary>
        /// 泛用的雙層參數讀取器 (Instance -> Type)
        /// </summary>
        private double? GetParameterValueWithFallback(Element elem, string[] paramNames)
        {
            if (elem == null || paramNames == null || paramNames.Length == 0) return null;

            // 1. 搜尋實體參數 (Instance)
            foreach (string paramName in paramNames)
            {
                Parameter p = elem.LookupParameter(paramName);
                if (p != null && p.HasValue && p.StorageType == StorageType.Double)
                {
                    double val = p.AsDouble();
                    if (val > 0.001) return val;
                }
            }

            // 2. 搜尋類型參數 (Type)
            ElementId typeId = elem.GetTypeId();
            if (typeId != ElementId.InvalidElementId)
            {
                Element typeElem = elem.Document.GetElement(typeId);
                if (typeElem != null)
                {
                    foreach (string paramName in paramNames)
                    {
                        Parameter p = typeElem.LookupParameter(paramName);
                        if (p != null && p.HasValue && p.StorageType == StorageType.Double)
                        {
                            double val = p.AsDouble();
                            if (val > 0.001) return val;
                        }
                    }
                }
            }

            return null;
        }

        private double GetBeamWidth(Element beam, string[] paramNames = null) {
            if (paramNames == null || paramNames.Length == 0) {
                paramNames = new string[] { "樑寬", "b", "梁寬", "Width" };
            }
            double? val = GetParameterValueWithFallback(beam, paramNames);
            return val ?? (0.4 / 0.3048); // 預設 40cm
        }

        private double GetSleeveLength(Element sleeve, string[] paramNames = null) {
            if (paramNames == null || paramNames.Length == 0) {
                paramNames = new string[] { "開口長度", "長度", "Length" };
            }
            double? val = GetParameterValueWithFallback(sleeve, paramNames);
            return val ?? (0.6 / 0.3048); // 預設 60cm
        }
        private string GetReferenceLevelName(Element elem) {
            // 嘗試多種可能的樓層參數名稱
            Parameter p = elem.get_Parameter(BuiltInParameter.INSTANCE_REFERENCE_LEVEL_PARAM) 
                       ?? elem.get_Parameter(BuiltInParameter.LEVEL_PARAM)
                       ?? elem.LookupParameter("Reference Level")
                       ?? elem.LookupParameter("樓層")
                       ?? elem.LookupParameter("參考樓層");

            if (p != null && p.HasValue) {
                ElementId id = p.AsElementId();
                if (id != ElementId.InvalidElementId) return elem.Document.GetElement(id)?.Name;
            }
            return "Unknown";
        }
        private Outline TransformOutline(Outline outline, Transform tr) {
            XYZ min = tr.OfPoint(outline.MinimumPoint);
            XYZ max = tr.OfPoint(outline.MaximumPoint);
            return new Outline(new XYZ(Math.Min(min.X, max.X), Math.Min(min.Y, max.Y), Math.Min(min.Z, max.Z)),
                               new XYZ(Math.Max(min.X, max.X), Math.Max(min.Y, max.Y), Math.Max(min.Z, max.Z)));
        }

        private Element FindElementInMainOrLinks(Document mainDoc, IdType elementId, out Transform totalTransform)
        {
            totalTransform = Transform.Identity;
            Element el = mainDoc.GetElement(new ElementId(elementId));
            if (el != null) return el;

            var linkInstances = new FilteredElementCollector(mainDoc)
                .OfClass(typeof(RevitLinkInstance))
                .Cast<RevitLinkInstance>()
                .ToList();

            foreach (var li in linkInstances)
            {
                Document linkDoc = li.GetLinkDocument();
                if (linkDoc == null) continue;

                Element linkEl = linkDoc.GetElement(new ElementId(elementId));
                if (linkEl != null)
                {
                    totalTransform = li.GetTotalTransform();
                    return linkEl;
                }
            }
            return null;
        }

        private void CreatePenetrationDimension(Document doc, View view, Element sleeve, Element beam, Transform tr, int staggerIndex)
        {
            try
            {
                BoundingBoxXYZ sleeveBBox = sleeve.get_BoundingBox(view);
                if (sleeveBBox == null) return;
                XYZ sleeveCenter = (sleeveBBox.Min + sleeveBBox.Max) * 0.5;

                LocationCurve beamLoc = beam.Location as LocationCurve;
                if (beamLoc == null) return;

                XYZ p0 = tr.OfPoint(beamLoc.Curve.GetEndPoint(0));
                XYZ p1 = tr.OfPoint(beamLoc.Curve.GetEndPoint(1));
                XYZ beamDir = (p1 - p0).Normalize();

                XYZ localSleeveCenter = tr.Inverse.OfPoint(sleeveCenter);
                IntersectionResult ir = beamLoc.Curve.Project(localSleeveCenter);
                if (ir == null) return;
                XYZ projPoint = tr.OfPoint(ir.XYZPoint);

                // Use the physical solid of the beam to find the exact end faces (which account for cutbacks/joins to columns)
                XYZ faceEnd0 = p0;
                XYZ faceEnd1 = p1;
                Options opt = new Options() { ComputeReferences = true, DetailLevel = ViewDetailLevel.Fine };
                GeometryElement geomElem = beam.get_Geometry(opt);
                if (geomElem != null)
                {
                    double minDot0 = double.MaxValue;
                    double minDot1 = double.MaxValue;
                    
                    foreach (GeometryObject geomObj in geomElem)
                    {
                        Solid solid = geomObj as Solid;
                        if (solid == null && geomObj is GeometryInstance geomInst)
                        {
                            solid = geomInst.GetInstanceGeometry().OfType<Solid>().FirstOrDefault(s => s.Faces.Size > 0);
                        }
                        if (solid != null && solid.Faces.Size > 0)
                        {
                            foreach (Face face in solid.Faces)
                            {
                                if (face is PlanarFace pf)
                                {
                                    // 判斷面是否垂直於梁方向 (法向量與梁方向平行)
                                    double dot = Math.Abs(pf.FaceNormal.DotProduct(tr.Inverse.OfVector(beamDir)));
                                    if (dot > 0.99)
                                    {
                                        XYZ faceCenter = pf.Evaluate(new UV(0.5, 0.5));
                                        XYZ globalFaceCenter = tr.OfPoint(faceCenter);
                                        // 判斷是屬於哪一端
                                        if (globalFaceCenter.DistanceTo(p0) < globalFaceCenter.DistanceTo(p1))
                                        {
                                            double distTo0 = globalFaceCenter.DistanceTo(projPoint); // 修改：尋找距離套管最近的面
                                            if (distTo0 < minDot0)
                                            {
                                                minDot0 = distTo0;
                                                // 投影到梁中心線上
                                                IntersectionResult fr = beamLoc.Curve.Project(tr.Inverse.OfPoint(globalFaceCenter));
                                                if (fr != null) faceEnd0 = tr.OfPoint(fr.XYZPoint);
                                                else faceEnd0 = globalFaceCenter;
                                            }
                                        }
                                        else
                                        {
                                            double distTo1 = globalFaceCenter.DistanceTo(projPoint); // 修改：尋找距離套管最近的面
                                            if (distTo1 < minDot1)
                                            {
                                                minDot1 = distTo1;
                                                IntersectionResult fr = beamLoc.Curve.Project(tr.Inverse.OfPoint(globalFaceCenter));
                                                if (fr != null) faceEnd1 = tr.OfPoint(fr.XYZPoint);
                                                else faceEnd1 = globalFaceCenter;
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }

                double d0 = projPoint.DistanceTo(faceEnd0);
                double d1 = projPoint.DistanceTo(faceEnd1);
                int endIdx = (d0 < d1) ? 0 : 1;
                XYZ correctedTargetEnd = (endIdx == 0) ? faceEnd0 : faceEnd1;

                XYZ perpDir = new XYZ(-beamDir.Y, beamDir.X, 0).Normalize();

                XYZ sleeveToBeam = (sleeveCenter - projPoint).Normalize();
                double offsetVal = 500.0 + (staggerIndex * 250.0); // 增加隨索引變化的錯開距離
                if (sleeveToBeam.DotProduct(perpDir) < 0)
                {
                    offsetVal = -500.0 - (staggerIndex * 250.0);
                }

                // 計算套管外邊緣：投影點朝梁端點移動半徑 (D/2) 距離
                double sleeveD = GetSleeveDiameter(sleeve, sleeveBBox);
                double radiusFeet = sleeveD / 2.0;
                XYZ toEndDir = (correctedTargetEnd - projPoint).Normalize();
                XYZ sleeveEdgePoint = projPoint + toEndDir * radiusFeet;

                // 呼叫現有工具庫的標註建立核心方法，不重複製作輪子
                CreateDimensionInternal(doc, view, correctedTargetEnd.X * 304.8, correctedTargetEnd.Y * 304.8, sleeveEdgePoint.X * 304.8, sleeveEdgePoint.Y * 304.8, offsetVal);
            }
            catch (Exception)
            {
                // 靜默失敗以避免干擾主要上色與標籤流程
            }
        }

        private double GetCollisionWidthAtPoint(Document mainDoc, XYZ worldPoint, XYZ beamDir, ElementId excludeBeamId)
        {
            try
            {
                double size = 0.5; // 約 15 cm 的偵測範圍
                Outline outline = new Outline(
                    new XYZ(worldPoint.X - size / 2, worldPoint.Y - size / 2, worldPoint.Z - size / 2),
                    new XYZ(worldPoint.X + size / 2, worldPoint.Y + size / 2, worldPoint.Z + size / 2)
                );

                // 1. 在主模型中搜尋柱
                var mainCols = new FilteredElementCollector(mainDoc)
                    .OfCategory(BuiltInCategory.OST_StructuralColumns)
                    .WherePasses(new BoundingBoxIntersectsFilter(outline))
                    .WhereElementIsNotElementType()
                    .ToList();

                foreach (var col in mainCols)
                {
                    BoundingBoxXYZ bbox = col.get_BoundingBox(null);
                    if (bbox != null)
                    {
                        double w = Math.Abs(beamDir.X) > Math.Abs(beamDir.Y) ? (bbox.Max.X - bbox.Min.X) : (bbox.Max.Y - bbox.Min.Y);
                        return w;
                    }
                }

                // 2. 搜尋連結模型中的柱
                var linkInstances = new FilteredElementCollector(mainDoc)
                    .OfClass(typeof(RevitLinkInstance))
                    .Cast<RevitLinkInstance>()
                    .ToList();

                foreach (var li in linkInstances)
                {
                    Document linkDoc = li.GetLinkDocument();
                    if (linkDoc == null) continue;

                    Transform tr = li.GetTotalTransform();
                    XYZ localPoint = tr.Inverse.OfPoint(worldPoint);
                    Outline localOutline = new Outline(
                        new XYZ(localPoint.X - size / 2, localPoint.Y - size / 2, localPoint.Z - size / 2),
                        new XYZ(localPoint.X + size / 2, localPoint.Y + size / 2, localPoint.Z + size / 2)
                    );

                    var linkCols = new FilteredElementCollector(linkDoc)
                        .OfCategory(BuiltInCategory.OST_StructuralColumns)
                        .WherePasses(new BoundingBoxIntersectsFilter(localOutline))
                        .WhereElementIsNotElementType()
                        .ToList();

                    foreach (var col in linkCols)
                    {
                        BoundingBoxXYZ bbox = col.get_BoundingBox(null);
                        if (bbox != null)
                        {
                            XYZ localBeamDir = tr.Inverse.OfVector(beamDir).Normalize();
                            double w = Math.Abs(localBeamDir.X) > Math.Abs(localBeamDir.Y) ? (bbox.Max.X - bbox.Min.X) : (bbox.Max.Y - bbox.Min.Y);
                            return w;
                        }
                    }
                }

                // 3. 在主模型中搜尋牆
                var mainWalls = new FilteredElementCollector(mainDoc)
                    .OfCategory(BuiltInCategory.OST_Walls)
                    .WherePasses(new BoundingBoxIntersectsFilter(outline))
                    .WhereElementIsNotElementType()
                    .Cast<Wall>()
                    .ToList();

                foreach (var wall in mainWalls)
                {
                    return wall.WallType.Width;
                }

                // 4. 在連結模型中搜尋牆
                foreach (var li in linkInstances)
                {
                    Document linkDoc = li.GetLinkDocument();
                    if (linkDoc == null) continue;

                    Transform tr = li.GetTotalTransform();
                    XYZ localPoint = tr.Inverse.OfPoint(worldPoint);
                    Outline localOutline = new Outline(
                        new XYZ(localPoint.X - size / 2, localPoint.Y - size / 2, localPoint.Z - size / 2),
                        new XYZ(localPoint.X + size / 2, localPoint.Y + size / 2, localPoint.Z + size / 2)
                    );

                    var linkWalls = new FilteredElementCollector(linkDoc)
                        .OfCategory(BuiltInCategory.OST_Walls)
                        .WherePasses(new BoundingBoxIntersectsFilter(localOutline))
                        .WhereElementIsNotElementType()
                        .Cast<Wall>()
                        .ToList();

                    foreach (var wall in linkWalls)
                    {
                        return wall.WallType.Width;
                    }
                }

                // 5. 在主模型中搜尋大梁
                var mainBeams = new FilteredElementCollector(mainDoc)
                    .OfCategory(BuiltInCategory.OST_StructuralFraming)
                    .WherePasses(new BoundingBoxIntersectsFilter(outline))
                    .WhereElementIsNotElementType()
                    .ToList();

                foreach (var b in mainBeams)
                {
                    if (b.Id != excludeBeamId)
                    {
                        return GetBeamWidth(b);
                    }
                }

                // 6. 在連結模型中搜尋大梁
                foreach (var li in linkInstances)
                {
                    Document linkDoc = li.GetLinkDocument();
                    if (linkDoc == null) continue;

                    Transform tr = li.GetTotalTransform();
                    XYZ localPoint = tr.Inverse.OfPoint(worldPoint);
                    Outline localOutline = new Outline(
                        new XYZ(localPoint.X - size / 2, localPoint.Y - size / 2, localPoint.Z - size / 2),
                        new XYZ(localPoint.X + size / 2, localPoint.Y + size / 2, localPoint.Z + size / 2)
                    );

                    var linkBeams = new FilteredElementCollector(linkDoc)
                        .OfCategory(BuiltInCategory.OST_StructuralFraming)
                        .WherePasses(new BoundingBoxIntersectsFilter(localOutline))
                        .WhereElementIsNotElementType()
                        .ToList();

                    foreach (var b in linkBeams)
                    {
                        if (b.Id != excludeBeamId)
                        {
                            return GetBeamWidth(b);
                        }
                    }
                }
            }
            catch
            {
                // 發生任何異常則不補償
            }

            return 0;
        }

        private double GetColumnWidth(Element col)
        {
            Element type = col.Document.GetElement(col.GetTypeId());
            if (type != null)
            {
                string[] paramNames = new string[] { "寬度", "b", "Width", "B", "D", "深度", "Depth" };
                foreach (string paramName in paramNames)
                {
                    Parameter p = type.LookupParameter(paramName);
                    if (p != null && p.HasValue && p.StorageType == StorageType.Double)
                    {
                        return p.AsDouble();
                    }
                }
            }
            return 600.0 / 304.8; // 預設 600mm
        }

        private void CreateSleeveToSideBeamDimension(Document doc, View view, Element sleeve, Element sideBeam, Transform tr, int staggerIndex)
        {
            try
            {
                BoundingBoxXYZ sleeveBBox = sleeve.get_BoundingBox(view);
                if (sleeveBBox == null) return;
                XYZ sleeveCenter = (sleeveBBox.Min + sleeveBBox.Max) * 0.5;

                LocationCurve sideBeamLoc = sideBeam.Location as LocationCurve;
                if (sideBeamLoc == null) return;

                XYZ p0 = tr.OfPoint(sideBeamLoc.Curve.GetEndPoint(0));
                XYZ p1 = tr.OfPoint(sideBeamLoc.Curve.GetEndPoint(1));

                XYZ localSleeveCenter = tr.Inverse.OfPoint(sleeveCenter);
                IntersectionResult ir = sideBeamLoc.Curve.Project(localSleeveCenter);
                if (ir == null) return;
                XYZ projPoint = tr.OfPoint(ir.XYZPoint);

                XYZ dirToSleeve = (sleeveCenter - projPoint).Normalize();

                double sideBeamWidth = GetBeamWidth(sideBeam);
                XYZ sideBeamSidePoint = projPoint + dirToSleeve * (sideBeamWidth / 2.0);

                double sleeveD = GetSleeveDiameter(sleeve, sleeveBBox);
                double radius = sleeveD / 2.0;
                XYZ sleeveEdgePoint = sleeveCenter - dirToSleeve * radius;

                // 標註的 offset：往垂直於 dirToSleeve 的方向偏移
                XYZ perpDir = new XYZ(-dirToSleeve.Y, dirToSleeve.X, 0).Normalize();
                double offsetVal = 500.0 + (staggerIndex * 250.0); // 錯開距離

                CreateDimensionInternal(doc, view, sideBeamSidePoint.X * 304.8, sideBeamSidePoint.Y * 304.8, sleeveEdgePoint.X * 304.8, sleeveEdgePoint.Y * 304.8, offsetVal);
            }
            catch (Exception)
            {
                // 靜默失敗以避免干擾主要上色與標籤流程
            }
        }

        private void CreateSleeveSpacingDimension(Document doc, View view, Element s1, Element s2, Element beam, Transform tr, int staggerIndex)
        {
            try
            {
                BoundingBoxXYZ bb1 = s1.get_BoundingBox(null);
                BoundingBoxXYZ bb2 = s2.get_BoundingBox(null);
                if (bb1 == null || bb2 == null) return;
                
                XYZ c1 = (bb1.Min + bb1.Max) * 0.5;
                XYZ c2 = (bb2.Min + bb2.Max) * 0.5;
                
                LocationCurve bLoc = beam.Location as LocationCurve;
                if (bLoc == null) return;
                
                IntersectionResult ir1 = bLoc.Curve.Project(tr.Inverse.OfPoint(c1));
                IntersectionResult ir2 = bLoc.Curve.Project(tr.Inverse.OfPoint(c2));
                if (ir1 == null || ir2 == null) return;
                
                XYZ p1 = tr.OfPoint(ir1.XYZPoint);
                XYZ p2 = tr.OfPoint(ir2.XYZPoint);
                
                XYZ dir = (p2 - p1).Normalize();
                double r1 = GetSleeveDiameter(s1, bb1) / 2.0;
                double r2 = GetSleeveDiameter(s2, bb2) / 2.0;
                
                // 套管的邊緣點
                XYZ edge1 = p1 + dir * r1;
                XYZ edge2 = p2 - dir * r2;

                XYZ p0 = tr.OfPoint(bLoc.Curve.GetEndPoint(0));
                XYZ p1_end = tr.OfPoint(bLoc.Curve.GetEndPoint(1));
                XYZ beamDir = (p1_end - p0).Normalize();
                XYZ perpDir = new XYZ(-beamDir.Y, beamDir.X, 0).Normalize();

                XYZ sleeveToBeam = (c1 - p1).Normalize();
                double offsetVal = 500.0 + (staggerIndex * 250.0);
                if (sleeveToBeam.DotProduct(perpDir) < 0)
                {
                    offsetVal = -500.0 - (staggerIndex * 250.0);
                }
                
                CreateDimensionInternal(doc, view, edge1.X * 304.8, edge1.Y * 304.8, edge2.X * 304.8, edge2.Y * 304.8, offsetVal);
            }
            catch (Exception)
            {
                // 靜默失敗
            }
        }
    }
}
