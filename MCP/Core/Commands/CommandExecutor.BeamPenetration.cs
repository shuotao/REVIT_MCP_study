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
                                    if (elem.Category.Id.GetIdValue() == (IdType)(int)BuiltInCategory.OST_StructuralFraming) {
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
                                    if (elem.Category.Id.GetIdValue() == (IdType)(int)BuiltInCategory.OST_StructuralFraming) {
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
                    if (elem.Category != null && elem.Category.Id.GetIdValue() == (IdType)(int)BuiltInCategory.OST_StructuralColumns)
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

    }
}
