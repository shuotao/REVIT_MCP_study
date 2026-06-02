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
                            if (Math.Abs(sleeveLen - beamWidthMM) > 10.0)
                            {
                                continue; // 長度不匹配且碰了牆/板，跳過
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
                        }
                    }

                    double beamTopZ = worldMax.Z;
                    double beamBottomZ = worldMin.Z;

                    XYZ worldP0 = XYZ.Zero;
                    XYZ worldP1 = XYZ.Zero;
                    if (beamLoc != null)
                    {
                        worldP0 = tr.OfPoint(beamLoc.Curve.GetEndPoint(0));
                        worldP1 = tr.OfPoint(beamLoc.Curve.GetEndPoint(1));
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
                        IsExcluded = Math.Abs(GetSleeveLength(sleeve, lenParams) * 304.8 - beamWidth * 304.8) > 10.0,
                        ExclusionReason = Math.Abs(GetSleeveLength(sleeve, lenParams) * 304.8 - beamWidth * 304.8) > 10.0 ? "套管長度與梁寬不匹配" : "",
                        DistanceToStart = distToStart * 304.8,
                        DistanceToEnd = distToEnd * 304.8,
                        MinDistance = minDist * 304.8,
                        SleeveZ = sleeveCenter.Z * 304.8,
                        BeamTopZ = beamTopZ * 304.8,
                        BeamBottomZ = beamBottomZ * 304.8,
                        BeamStartX = worldP0.X * 304.8,
                        BeamStartY = worldP0.Y * 304.8,
                        BeamEndX = worldP1.X * 304.8,
                        BeamEndY = worldP1.Y * 304.8,
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
                    TextNote tn = TextNote.Create(doc, doc.ActiveView.Id, pos, isOk ? "● PASS" : "● FAIL", defaultTextTypeId);
                    tn.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.Set("BeamPenetration_Helper");
                    
                    OverrideGraphicSettings textOgs = new OverrideGraphicSettings();
                    textOgs.SetProjectionLineColor(color);
                    doc.ActiveView.SetElementOverrides(tn.Id, textOgs);

                    // 2. 獲取梁 ID 並建立套管到梁端點的水平尺寸標註 (Dimension)
                    if (res["BeamId"] != null)
                    {
                        IdType beamId = res["BeamId"].Value<IdType>();
                        Transform beamTr;
                        Element beam = FindElementInMainOrLinks(doc, beamId, out beamTr);
                        if (beam != null)
                        {
                            CreatePenetrationDimension(doc, doc.ActiveView, sleeve, beam, beamTr);
                        }
                    }
                }
                trans.Commit();
            }
            return new { Success = true };
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

        private void CreatePenetrationDimension(Document doc, View view, Element sleeve, Element beam, Transform tr)
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

                XYZ localSleeveCenter = tr.Inverse.OfPoint(sleeveCenter);
                IntersectionResult ir = beamLoc.Curve.Project(localSleeveCenter);
                if (ir == null) return;
                XYZ projPoint = tr.OfPoint(ir.XYZPoint);

                double d0 = projPoint.DistanceTo(p0);
                double d1 = projPoint.DistanceTo(p1);
                XYZ targetEnd = (d0 < d1) ? p0 : p1;

                XYZ beamDir = (p1 - p0).Normalize();
                XYZ perpDir = new XYZ(-beamDir.Y, beamDir.X, 0).Normalize();

                XYZ sleeveToBeam = (sleeveCenter - projPoint).Normalize();
                XYZ offsetDir = perpDir;
                if (sleeveToBeam.DotProduct(perpDir) < 0)
                {
                    offsetDir = perpDir.Negate();
                }

                // 偏移距離：500mm
                double offsetFeet = 500.0 / 304.8;
                // 詳圖線延伸長度：650mm (從梁內 150mm 到梁外 500mm)
                double startOffsetFeet = -150.0 / 304.8;
                double endOffsetFeet = 500.0 / 304.8;

                XYZ pt1_start = targetEnd.Add(offsetDir.Multiply(startOffsetFeet));
                XYZ pt1_end = targetEnd.Add(offsetDir.Multiply(endOffsetFeet));

                XYZ pt2_start = projPoint.Add(offsetDir.Multiply(startOffsetFeet));
                XYZ pt2_end = projPoint.Add(offsetDir.Multiply(endOffsetFeet));

                // 尺寸線在偏移 400mm 處
                double dimOffsetFeet = 400.0 / 304.8;
                XYZ dimStart = targetEnd.Add(offsetDir.Multiply(dimOffsetFeet));
                XYZ dimEnd = projPoint.Add(offsetDir.Multiply(dimOffsetFeet));

                Line dimLine = Line.CreateBound(dimStart, dimEnd);

                DetailCurve dc1 = doc.Create.NewDetailCurve(view, Line.CreateBound(pt1_start, pt1_end));
                DetailCurve dc2 = doc.Create.NewDetailCurve(view, Line.CreateBound(pt2_start, pt2_end));

                ReferenceArray refArray = new ReferenceArray();
                refArray.Append(dc1.GeometryCurve.Reference);
                refArray.Append(dc2.GeometryCurve.Reference);

                Dimension dim = doc.Create.NewDimension(view, dimLine, refArray);
                
                dc1.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.Set("BeamPenetration_Helper");
                dc2.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.Set("BeamPenetration_Helper");
                dim.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.Set("BeamPenetration_Helper");
            }
            catch (Exception)
            {
                // 靜默失敗以避免干擾主要上色與標籤流程
            }
        }
    }
}
