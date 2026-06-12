using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Newtonsoft.Json.Linq;

// Revit 2025+ ElementId: int → long
#if REVIT2025_OR_GREATER
using IdType = System.Int64;
#else
using IdType = System.Int32;
#endif

namespace RevitMCP.Core
{
    /// <summary>
    /// 消防偵煙探測器設置法規檢討
    /// 法源：消防法施行細則附表五（偵煙式探測器設置標準）
    /// </summary>
    public partial class CommandExecutor
    {
        #region 偵煙探測器檢討

        private object AnalyzeSmokeDetectors(JObject parameters)
        {
            var doc = _uiApp.ActiveUIDocument?.Document;
            if (doc == null) return new { Success = false, Error = "無法取得目前文件" };

            var view = _uiApp.ActiveUIDocument?.ActiveView;
            if (view == null) return new { Success = false, Error = "無法取得目前視圖" };

            // 出風口族群過濾關鍵字（使用者可傳入覆蓋）
            var outletKeywordsParam = parameters["outletKeywords"] as JArray;
            var outletKeywords = outletKeywordsParam != null
                ? outletKeywordsParam.Select(k => k.ToString().ToLower()).ToList()
                : new List<string> { "fcu", "冷風機", "ahu", "送風口", "出風口", "supply air", "air outlet", "diffuser", "散流器" };

            // 偵煙探測器族群過濾關鍵字
            var detectorKeywordsParam = parameters["detectorKeywords"] as JArray;
            var detectorKeywords = detectorKeywordsParam != null
                ? detectorKeywordsParam.Select(k => k.ToString().ToLower()).ToList()
                : new List<string> { "偵煙", "smoke detector", "smoke", "探測器", "detector", "感知器" };

            try
            {
                // ── 1. 收集探測器（在視圖範圍內）────────────────────────────────
                var allFamilyInstances = new FilteredElementCollector(doc, view.Id)
                    .OfClass(typeof(FamilyInstance))
                    .WhereElementIsNotElementType()
                    .Cast<FamilyInstance>()
                    .ToList();

                var detectors = allFamilyInstances
                    .Where(fi => IsDetector(fi, detectorKeywords))
                    .ToList();

                // ── 2. 收集出風口────────────────────────────────────────────────
                var airOutlets = allFamilyInstances
                    .Where(fi => IsAirOutlet(fi, outletKeywords))
                    .ToList();

                // ── 3. 收集牆、樑（用於距離量測）────────────────────────────────
                var walls = new FilteredElementCollector(doc, view.Id)
                    .OfClass(typeof(Wall))
                    .WhereElementIsNotElementType()
                    .Cast<Wall>()
                    .ToList();

                var beams = new FilteredElementCollector(doc, view.Id)
                    .OfCategory(BuiltInCategory.OST_StructuralFraming)
                    .WhereElementIsNotElementType()
                    .Cast<Element>()
                    .ToList();

                // ── 4. 收集房間────────────────────────────────────────────────
                var rooms = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_Rooms)
                    .WhereElementIsNotElementType()
                    .Cast<Room>()
                    .Where(r => r.Area > 0)
                    .ToList();

                // ── 5. 逐一分析探測器────────────────────────────────────────────
                var detectorResults = new List<object>();

                foreach (var det in detectors)
                {
                    XYZ detCenter = GetFamilyInstanceCenter(det);
                    double detBottomZ = GetFamilyInstanceBottomZ(det) * 304.8; // convert ft→mm

                    // 房間歸屬
                    Room containingRoom = FindContainingRoom(rooms, detCenter);
                    double roomArea    = containingRoom?.Area * 0.0929 ?? -1; // ft²→m²
                    double ceilingZ    = GetCeilingZ(doc, containingRoom, detCenter) * 304.8;
                    double ceilingHeight = containingRoom != null
                        ? GetRoomHeight(containingRoom) * 304.8 / 1000.0 // mm→m
                        : -1;

                    // 距出風口
                    double distToOutlet = GetMinDistanceToElements(detCenter, airOutlets.Cast<Element>().ToList()) * 304.8;

                    // 距牆
                    double distToWall = GetMinDistanceToWalls(detCenter, walls) * 304.8;

                    // 距樑
                    double distToBeam = GetMinDistanceToBeams(detCenter, beams) * 304.8;

                    detectorResults.Add(new
                    {
                        DetectorId    = det.Id.GetIdValue(),
                        FamilyName    = det.Symbol?.FamilyName ?? det.Name,
                        TypeName      = det.Symbol?.Name ?? "",
                        X             = detCenter.X * 304.8,
                        Y             = detCenter.Y * 304.8,
                        Z             = detCenter.Z * 304.8,
                        DetectorBottomZ        = detBottomZ,
                        CeilingZ               = ceilingZ,
                        RoomId                 = containingRoom?.Id.GetIdValue() ?? -1,
                        RoomName               = containingRoom?.Name ?? "（無房間）",
                        RoomArea               = Math.Round(roomArea, 2),
                        CeilingHeightM         = Math.Round(ceilingHeight, 2),
                        DistToNearestAirOutlet = Math.Round(distToOutlet, 0),
                        DistToNearestWall      = Math.Round(distToWall, 0),
                        DistToNearestBeam      = Math.Round(distToBeam, 0),
                    });
                }

                // ── 6. 房間統計（探測器數量、區格數）────────────────────────────
                var roomResults = new List<object>();
                foreach (var room in rooms)
                {
                    double area = room.Area * 0.0929;
                    if (area < 1) continue;

                    var detectorsInRoom = detectorResults
                        .Where(d => ((dynamic)d).RoomId == room.Id.GetIdValue())
                        .ToList();

                    int detCount  = detectorsInRoom.Count;
                    int beamZones = CountBeamZonesInRoom(doc, room, beams);
                    double ceilingH = GetRoomHeight(room) * 304.8 / 1000.0;

                    roomResults.Add(new
                    {
                        RoomId         = room.Id.GetIdValue(),
                        RoomName       = room.Name,
                        Area           = Math.Round(area, 2),
                        CeilingHeight  = Math.Round(ceilingH, 2),
                        DetectorCount  = detCount,
                        BeamZones      = beamZones,
                    });
                }

                return new
                {
                    Success        = true,
                    DetectorCount  = detectors.Count,
                    AirOutletCount = airOutlets.Count,
                    Detectors      = detectorResults,
                    Rooms          = roomResults,
                    Summary        = $"找到 {detectors.Count} 個探測器、{airOutlets.Count} 個出風口，涵蓋 {roomResults.Count} 個房間。"
                };
            }
            catch (Exception ex)
            {
                return new { Success = false, Error = "執行失敗: " + ex.Message };
            }
        }

        private object VisualizeDetectorResults(JObject parameters)
        {
            var doc = _uiApp.ActiveUIDocument?.Document;
            var view = _uiApp.ActiveUIDocument?.ActiveView;
            if (doc == null || view == null)
                return new { Success = false, Error = "無法取得文件或視圖" };

            var resultsToken = parameters["results"] as JArray;
            if (resultsToken == null)
                return new { Success = false, Error = "缺少 results 參數" };

            int colored = 0;
            using (var tx = new Transaction(doc, "偵煙探測器上色"))
            {
                tx.Start();
                foreach (var item in resultsToken)
                {
                    var idVal  = item["DetectorId"]?.Value<long>() ?? -1;
                    var isOk   = item["IsOk"]?.Value<bool>() ?? true;
                    var isWarn = item["IsWarn"]?.Value<bool>() ?? false;

                    if (idVal < 0) continue;
                    var elemId = new ElementId((IdType)idVal);
                    var elem   = doc.GetElement(elemId);
                    if (elem == null) continue;

                    var ogs = new OverrideGraphicSettings();
                    Color c;
                    if (!isOk)        c = new Color(255, 0, 0);    // 紅 = FAIL
                    else if (isWarn)  c = new Color(255, 165, 0);   // 橙 = WARN
                    else              c = new Color(0, 200, 0);     // 綠 = PASS

                    ogs.SetProjectionLineColor(c);
                    ogs.SetSurfaceForegroundPatternColor(c);
                    view.SetElementOverrides(elemId, ogs);
                    colored++;
                }
                tx.Commit();
            }

            return new { Success = true, ColoredCount = colored };
        }

        // ── 輔助方法 ─────────────────────────────────────────────────────────────

        private bool IsDetector(FamilyInstance fi, List<string> keywords)
        {
            string name = ((fi.Symbol?.FamilyName ?? "") + " " + (fi.Symbol?.Name ?? "") + " " + fi.Name).ToLower();
            return keywords.Any(k => name.Contains(k));
        }

        private bool IsAirOutlet(FamilyInstance fi, List<string> keywords)
        {
            // 先確認是 MEP 類別（機械設備、風管配件、風口）
            var catId = fi.Category?.Id.GetIdValue() ?? 0;
            bool isMepCategory =
                catId == (IdType)(int)BuiltInCategory.OST_MechanicalEquipment ||
                catId == (IdType)(int)BuiltInCategory.OST_DuctTerminal ||
                catId == (IdType)(int)BuiltInCategory.OST_DuctAccessory ||
                catId == (IdType)(int)BuiltInCategory.OST_GenericModel;

            string name = ((fi.Symbol?.FamilyName ?? "") + " " + (fi.Symbol?.Name ?? "") + " " + fi.Name).ToLower();
            return keywords.Any(k => name.Contains(k));
        }

        private XYZ GetFamilyInstanceCenter(FamilyInstance fi)
        {
            var bb = fi.get_BoundingBox(null);
            if (bb != null)
                return (bb.Min + bb.Max) / 2.0;
            var loc = fi.Location as LocationPoint;
            return loc?.Point ?? XYZ.Zero;
        }

        private double GetFamilyInstanceBottomZ(FamilyInstance fi)
        {
            var bb = fi.get_BoundingBox(null);
            return bb?.Min.Z ?? (fi.Location as LocationPoint)?.Point.Z ?? 0;
        }

        private Room FindContainingRoom(List<Room> rooms, XYZ point)
        {
            foreach (var room in rooms)
            {
                if (room.IsPointInRoom(point)) return room;
            }
            // fallback: 最近房間
            return rooms.OrderBy(r =>
            {
                var loc = r.Location as LocationPoint;
                return loc != null ? loc.Point.DistanceTo(point) : double.MaxValue;
            }).FirstOrDefault();
        }

        private double GetRoomHeight(Room room)
        {
            var heightParam = room.get_Parameter(BuiltInParameter.ROOM_HEIGHT);
            if (heightParam != null && heightParam.HasValue && heightParam.AsDouble() > 0)
                return heightParam.AsDouble();
            // fallback: 3m
            return 3.0 / 0.3048;
        }

        private double GetCeilingZ(Document doc, Room room, XYZ detCenter)
        {
            if (room == null) return detCenter.Z + (3.0 / 0.3048);

            // 嘗試從 Ceiling Element 取得
            var ceilings = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Ceilings)
                .WhereElementIsNotElementType()
                .Cast<Element>()
                .ToList();

            double bestZ = double.MinValue;
            foreach (var ceil in ceilings)
            {
                var bb = ceil.get_BoundingBox(null);
                if (bb == null) continue;
                if (bb.Min.X > detCenter.X || bb.Max.X < detCenter.X) continue;
                if (bb.Min.Y > detCenter.Y || bb.Max.Y < detCenter.Y) continue;
                if (bb.Max.Z > bestZ) bestZ = bb.Max.Z;
            }
            if (bestZ > double.MinValue) return bestZ;

            // fallback: 房間底部 + 高度
            var levelElem = doc.GetElement(room.LevelId) as Level;
            double levelZ  = levelElem?.Elevation ?? 0;
            double roomH   = GetRoomHeight(room);
            return levelZ + roomH;
        }

        private double GetMinDistanceToElements(XYZ point, List<Element> elements)
        {
            double min = double.MaxValue;
            foreach (var elem in elements)
            {
                var bb = elem.get_BoundingBox(null);
                if (bb == null) continue;
                XYZ center = (bb.Min + bb.Max) / 2.0;
                double d = new XYZ(point.X - center.X, point.Y - center.Y, 0).GetLength();
                if (d < min) min = d;
            }
            return min == double.MaxValue ? -1 / 304.8 : min;
        }

        private double GetMinDistanceToWalls(XYZ point, List<Wall> walls)
        {
            double min = double.MaxValue;
            foreach (var wall in walls)
            {
                var loc = wall.Location as LocationCurve;
                if (loc == null) continue;
                var result = loc.Curve.Project(point);
                if (result == null) continue;
                double d = new XYZ(point.X - result.XYZPoint.X, point.Y - result.XYZPoint.Y, 0).GetLength();
                if (d < min) min = d;
            }
            return min == double.MaxValue ? -1 / 304.8 : min;
        }

        private double GetMinDistanceToBeams(XYZ point, List<Element> beams)
        {
            double min = double.MaxValue;
            foreach (var beam in beams)
            {
                var loc = beam.Location as LocationCurve;
                if (loc == null) continue;
                var result = loc.Curve.Project(point);
                if (result == null) continue;
                double d = new XYZ(point.X - result.XYZPoint.X, point.Y - result.XYZPoint.Y, 0).GetLength();
                if (d < min) min = d;
            }
            return min == double.MaxValue ? -1 / 304.8 : min;
        }

        private int CountBeamZonesInRoom(Document doc, Room room, List<Element> beams)
        {
            // 簡化實作：計算在房間 BoundingBox 內且深度 ≥ 600mm 的橫向樑數量 + 1
            BoundingBoxXYZ roomBb = room.get_BoundingBox(null);
            if (roomBb == null) return 1;

            double minBeamDepthFt = 600.0 / 304.8;
            int crossingBeams = 0;
            XYZ roomCenter = (roomBb.Min + roomBb.Max) / 2.0;

            foreach (var beam in beams)
            {
                var bb = beam.get_BoundingBox(null);
                if (bb == null) continue;

                // 樑必須在房間範圍內
                if (bb.Min.X > roomBb.Max.X || bb.Max.X < roomBb.Min.X) continue;
                if (bb.Min.Y > roomBb.Max.Y || bb.Max.Y < roomBb.Min.Y) continue;

                // 樑深 ≥ 600mm
                double depth = (bb.Max.Z - bb.Min.Z) * 304.8;
                if (depth < 600) continue;

                crossingBeams++;
            }

            return crossingBeams + 1;
        }

        #endregion
    }
}
