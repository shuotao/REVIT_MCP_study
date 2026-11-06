using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Newtonsoft.Json.Linq;
using ClosedXML.Excel;

// Revit 2025+ ElementId: int → long
#if REVIT2025_OR_GREATER
using IdType = System.Int64;
#else
using IdType = System.Int32;
#endif

namespace RevitMCP.Core
{
    /// <summary>
    /// 房間內部表面積計算命令
    /// 使用 SpatialElementGeometryCalculator 精確計算牆面、地板、天花板面積
    /// Fallback：邊界線段 × 高度（簡化計算）
    /// </summary>
    public partial class CommandExecutor
    {
        private const double SQ_FT_TO_SQ_M = 0.092903;
        private const double FT_TO_MM = 304.8;

        // 快取：第一次分析的結果，供第二次帶預設值時直接使用
        private List<Room> _cachedTargetRooms;
        private List<object> _cachedRoomResults;

        private object GetRoomSurfaceAreas(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;
            IdType? roomId = parameters["roomId"]?.Value<IdType>();
            string roomName = parameters["roomName"]?.Value<string>();
            string levelName = parameters["level"]?.Value<string>();
            bool includeBreakdown = parameters["includeBreakdown"]?.Value<bool>() ?? true;
            bool subtractOpenings = parameters["subtractOpenings"]?.Value<bool>() ?? true;
            // 強化參數解析：支援 boolean true 和 string "true"
            bool includeFinishLayers = false;
            var finishToken = parameters["includeFinishLayers"];
            if (finishToken != null)
            {
                if (finishToken.Type == JTokenType.Boolean)
                    includeFinishLayers = finishToken.Value<bool>();
                else if (finishToken.Type == JTokenType.String)
                    includeFinishLayers = string.Equals(finishToken.Value<string>(), "true", StringComparison.OrdinalIgnoreCase);
            }

            // 預設粉刷層類型標記（選填）
            string defaultFloorFinish = parameters["defaultFloorFinish"]?.Value<string>();
            string defaultWallFinish = parameters["defaultWallFinish"]?.Value<string>();
            string defaultCeilingFinish = parameters["defaultCeilingFinish"]?.Value<string>();

            _finishTypeCache.Clear(); // 每次呼叫清空快取

            Logger.Info($"[RoomSurface] includeFinishLayers={includeFinishLayers}, raw={finishToken?.ToString() ?? "NULL"}, tokenType={finishToken?.Type.ToString() ?? "N/A"}");

            // ═══ 快取快速路徑：第二次呼叫帶預設值時，跳過所有幾何分析 ═══
            bool hasDefaults = !string.IsNullOrEmpty(defaultFloorFinish) ||
                               !string.IsNullOrEmpty(defaultWallFinish) ||
                               !string.IsNullOrEmpty(defaultCeilingFinish);

            if (hasDefaults && _cachedRoomResults != null && _cachedRoomResults.Count > 0 && _cachedTargetRooms != null)
            {
                Logger.Info($"[RoomSurface] Fast path: using cached results ({_cachedRoomResults.Count} rooms), applying defaults");
                var cachedResults = ApplyDefaultsToResults(doc, _cachedRoomResults,
                    defaultFloorFinish, defaultWallFinish, defaultCeilingFinish);
                var cachedRooms = _cachedTargetRooms;

                // 彙總
                double tFloor = 0, tCeiling = 0, tWallGross = 0, tOpening = 0;
                foreach (dynamic r in cachedResults)
                {
                    tFloor += (double)r.FloorArea_m2;
                    tCeiling += (double)r.CeilingArea_m2;
                    tWallGross += (double)r.WallGrossArea_m2;
                    tOpening += (double)r.OpeningArea_m2;
                }

                // 後處理：寫入參數、建立明細表、匯出 Excel
                string cachedExcelPath = null;
                string cachedScheduleName = null;
                var cachedWarnings = new List<string>();

                try
                {
                    using (var tx = new Transaction(doc, "寫入粉刷層資訊（預設值）"))
                    {
                        tx.Start();
                        WriteFinishToRoomParameters(doc, cachedRooms, cachedResults);
                        cachedScheduleName = CreateFinishSchedule(doc);
                        tx.Commit();
                    }
                }
                catch (Exception ex)
                {
                    cachedWarnings.Add($"粉刷層參數寫入/明細表建立失敗：{ex.Message}");
                }

                try
                {
                    cachedExcelPath = ExportFinishAreaExcel(doc, cachedResults, parameters);
                }
                catch (Exception ex)
                {
                    cachedWarnings.Add($"Excel 匯出失敗：{ex.Message}");
                }

                return new
                {
                    TotalRooms = cachedResults.Count,
                    Summary = new
                    {
                        TotalFloorArea_m2 = Math.Round(tFloor, 2),
                        TotalCeilingArea_m2 = Math.Round(tCeiling, 2),
                        TotalWallGrossArea_m2 = Math.Round(tWallGross, 2),
                        TotalOpeningArea_m2 = Math.Round(tOpening, 2),
                        TotalWallNetArea_m2 = Math.Round(tWallGross - tOpening, 2),
                        TotalNetSurfaceArea_m2 = Math.Round(tFloor + tCeiling + tWallGross - tOpening, 2)
                    },
                    Rooms = cachedResults,
                    FinishSchedule = cachedScheduleName,
                    ExcelPath = cachedExcelPath,
                    Warnings = cachedWarnings.Count > 0 ? cachedWarnings : null
                };
            }

            // 收集目標房間
            List<Room> targetRooms = new List<Room>();

            if (roomId.HasValue)
            {
                var room = doc.GetElement(new ElementId(roomId.Value)) as Room;
                if (room == null)
                    throw new Exception($"找不到房間 ID: {roomId}");
                targetRooms.Add(room);
            }
            else if (!string.IsNullOrEmpty(roomName))
            {
                var rooms = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_Rooms)
                    .WhereElementIsNotElementType()
                    .Cast<Room>()
                    .Where(r => r.Area > 0 &&
                        (r.Name.Contains(roomName) ||
                         r.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString()?.Contains(roomName) == true))
                    .ToList();
                if (rooms.Count == 0)
                    throw new Exception($"找不到房間名稱包含: {roomName}");
                targetRooms.AddRange(rooms);
            }
            else if (!string.IsNullOrEmpty(levelName))
            {
                Level targetLevel = FindLevel(doc, levelName, false);
                targetRooms = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_Rooms)
                    .WhereElementIsNotElementType()
                    .Cast<Room>()
                    .Where(r => r.LevelId == targetLevel.Id && r.Area > 0)
                    .ToList();
                if (targetRooms.Count == 0)
                    throw new Exception($"樓層 {levelName} 沒有有效的房間");
            }
            else
            {
                // 未指定篩選條件：取得所有房間
                targetRooms = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_Rooms)
                    .WhereElementIsNotElementType()
                    .Cast<Room>()
                    .Where(r => r.Area > 0)
                    .ToList();
            }

            // 計算每個房間的表面積
            var roomResults = new List<object>();
            var warnings = new List<string>();

            using (var calculator = new SpatialElementGeometryCalculator(doc))
            {
                foreach (var room in targetRooms)
                {
                    try
                    {
                        var roomResult = CalculateRoomSurfaces(doc, room, calculator, includeBreakdown, subtractOpenings, includeFinishLayers, defaultFloorFinish, defaultWallFinish, defaultCeilingFinish);
                        roomResults.Add(roomResult);
                    }
                    catch (Exception ex)
                    {
                        // Calculator 失敗，嘗試 Fallback
                        try
                        {
                            var fallbackResult = CalculateRoomSurfacesFallback(doc, room, includeBreakdown, subtractOpenings, includeFinishLayers, defaultFloorFinish, defaultWallFinish, defaultCeilingFinish);
                            roomResults.Add(fallbackResult);
                            warnings.Add($"房間 {room.Name} (ID:{room.Id.GetIdValue()}) 使用簡化計算：{ex.Message}");
                        }
                        catch (Exception ex2)
                        {
                            warnings.Add($"房間 {room.Name} (ID:{room.Id.GetIdValue()}) 計算失敗：{ex2.Message}");
                        }
                    }
                }
            }

            // 儲存快取供第二次呼叫使用
            _cachedTargetRooms = targetRooms;
            _cachedRoomResults = roomResults;

            // 彙總
            double totalFloor = 0, totalCeiling = 0, totalWallGross = 0, totalOpening = 0;
            foreach (dynamic r in roomResults)
            {
                totalFloor += (double)r.FloorArea_m2;
                totalCeiling += (double)r.CeilingArea_m2;
                totalWallGross += (double)r.WallGrossArea_m2;
                totalOpening += (double)r.OpeningArea_m2;
            }

            // 粉刷層後處理：寫入參數、建立明細表、匯出 Excel
            string excelPath = null;
            string scheduleName = null;
            if (includeFinishLayers && roomResults.Count > 0)
            {
                try
                {
                    using (var tx = new Transaction(doc, "寫入粉刷層資訊"))
                    {
                        tx.Start();
                        WriteFinishToRoomParameters(doc, targetRooms, roomResults);
                        scheduleName = CreateFinishSchedule(doc);
                        tx.Commit();
                    }
                }
                catch (Exception ex)
                {
                    warnings.Add($"粉刷層參數寫入/明細表建立失敗：{ex.Message}");
                }

                try
                {
                    excelPath = ExportFinishAreaExcel(doc, roomResults, parameters);
                }
                catch (Exception ex)
                {
                    warnings.Add($"Excel 匯出失敗：{ex.Message}");
                }
            }

            return new
            {
                TotalRooms = roomResults.Count,
                Summary = new
                {
                    TotalFloorArea_m2 = Math.Round(totalFloor, 2),
                    TotalCeilingArea_m2 = Math.Round(totalCeiling, 2),
                    TotalWallGrossArea_m2 = Math.Round(totalWallGross, 2),
                    TotalOpeningArea_m2 = Math.Round(totalOpening, 2),
                    TotalWallNetArea_m2 = Math.Round(totalWallGross - totalOpening, 2),
                    TotalNetSurfaceArea_m2 = Math.Round(totalFloor + totalCeiling + totalWallGross - totalOpening, 2)
                },
                Rooms = roomResults,
                FinishSchedule = scheduleName,
                ExcelPath = excelPath,
                Warnings = warnings.Count > 0 ? warnings : null
            };
        }

        /// <summary>
        /// 使用 SpatialElementGeometryCalculator 精確計算房間表面積
        /// </summary>
        private object CalculateRoomSurfaces(Document doc, Room room,
            SpatialElementGeometryCalculator calculator,
            bool includeBreakdown, bool subtractOpenings, bool includeFinishLayers,
            string defaultFloorFinish = null, string defaultWallFinish = null, string defaultCeilingFinish = null)
        {
            var results = calculator.CalculateSpatialElementGeometry(room);
            Solid roomSolid = results.GetGeometry();

            double floorArea = 0;
            double ceilingArea = 0;
            double wallGrossArea = 0;
            double openingArea = 0;
            var breakdownList = new List<object>();

            // 用於按宿主元素彙總面積
            var hostAreas = new Dictionary<ElementId, HostFaceData>();
            // 追蹤已處理開口的牆 ID，避免同一面牆的門窗被重複計算
            var processedWallOpenings = new HashSet<ElementId>();

            foreach (Face face in roomSolid.Faces)
            {
                double faceArea = face.Area * SQ_FT_TO_SQ_M;
                XYZ normal = face.ComputeNormal(new UV(0.5, 0.5));

                // ── 水平面：法線方向直接決定 Floor / Ceiling ──
                // 不查詢 boundaryFaceInfo，避免被牆壁 host 搶走分類
                if (Math.Abs(normal.Z) > 0.8)
                {
                    if (normal.Z < 0)
                        floorArea += faceArea;
                    else
                        ceilingArea += faceArea;
                    continue;
                }

                // ── 非水平面：視為牆面 ──
                wallGrossArea += faceArea;

                var boundaryFaceInfos = results.GetBoundaryFaceInfo(face);
                if (boundaryFaceInfos == null || boundaryFaceInfos.Count == 0) continue;

                foreach (var faceInfo in boundaryFaceInfos)
                {
                    ElementId hostId = GetHostElementIdFromFaceInfo(doc, faceInfo);
                    Element hostElement = hostId != ElementId.InvalidElementId
                        ? doc.GetElement(hostId) : null;
                    if (hostElement == null || !(hostElement is Wall)) continue;

                    double subfaceArea = faceInfo.GetSubface().Area * SQ_FT_TO_SQ_M;

                    // 追蹤每面牆的面積，用於 breakdown
                    if (includeBreakdown)
                    {
                        if (!hostAreas.ContainsKey(hostId))
                        {
                            hostAreas[hostId] = new HostFaceData
                            {
                                ElementId = hostId,
                                Category = "Wall",
                                TypeName = hostElement is Wall wall
                                    ? wall.WallType?.Name ?? "Unknown"
                                    : hostElement.Name,
                                GrossArea = 0,
                                OpeningArea = 0
                            };
                        }
                        hostAreas[hostId].GrossArea += subfaceArea;
                    }

                    // 計算門窗開口面積（每面牆只計算一次，避免重複累加）
                    if (subtractOpenings && hostElement is Wall hostWall
                        && !processedWallOpenings.Contains(hostId))
                    {
                        processedWallOpenings.Add(hostId);
                        double wallOpeningArea = CalculateWallOpeningArea(doc, hostWall, room, face);
                        openingArea += wallOpeningArea;
                        if (includeBreakdown && hostAreas.ContainsKey(hostId))
                        {
                            hostAreas[hostId].OpeningArea += wallOpeningArea;
                        }
                    }
                }
            }

            // 補償：若無偵測到天花板/地板（模型中無實體元素），以房間平面面積估算
            double roomPlanArea = room.Area * SQ_FT_TO_SQ_M;
            bool ceilingEstimated = false;
            bool floorEstimated = false;

            if (ceilingArea < 0.001 && roomPlanArea > 0.001)
            {
                ceilingArea = roomPlanArea;
                ceilingEstimated = true;
            }
            if (floorArea < 0.001 && roomPlanArea > 0.001)
            {
                floorArea = roomPlanArea;
                floorEstimated = true;
            }

            // 偵測粉刷層
            List<FinishLayerData> wallFinishLayers = null;
            List<FinishLayerData> floorFinishLayers = null;
            List<FinishLayerData> ceilingFinishLayers = null;
            List<FinishLayerData> unassociatedFinishLayers = null;

            if (includeFinishLayers)
            {
                var allFinish = DetectFinishLayers(doc, room, roomSolid, hostAreas);
                wallFinishLayers = allFinish.Where(f => f.Category == "Wall" && f.AssociatedBoundaryId != ElementId.InvalidElementId).ToList();
                floorFinishLayers = allFinish.Where(f => f.Category == "Floor").ToList();
                ceilingFinishLayers = allFinish.Where(f => f.Category == "Ceiling").ToList();
                unassociatedFinishLayers = allFinish.Where(f => f.Category == "Wall" && f.AssociatedBoundaryId == ElementId.InvalidElementId).ToList();

                // 計算覆蓋面積
                CalculateFinishCoverageAreas(doc, wallFinishLayers, hostAreas, roomSolid, floorArea, ceilingArea);
                CalculateFinishCoverageAreas(doc, floorFinishLayers, hostAreas, roomSolid, floorArea, ceilingArea);
                CalculateFinishCoverageAreas(doc, ceilingFinishLayers, hostAreas, roomSolid, floorArea, ceilingArea);
                CalculateFinishCoverageAreas(doc, unassociatedFinishLayers, hostAreas, roomSolid, floorArea, ceilingArea);

                // 對缺少粉刷的表面填入預設值（僅在無快取直接帶預設值時觸發）
                ApplyDefaultFinishLayers(doc,
                    ref floorFinishLayers, ref ceilingFinishLayers,
                    ref wallFinishLayers, ref unassociatedFinishLayers,
                    hostAreas, floorArea, ceilingArea,
                    defaultFloorFinish, defaultWallFinish, defaultCeilingFinish);
            }

            // 建立 breakdown
            if (includeBreakdown)
            {
                foreach (var kvp in hostAreas)
                {
                    var data = kvp.Value;
                    // 找出此牆的粉刷層
                    var wallFinish = wallFinishLayers?.Where(f => f.AssociatedBoundaryId == data.ElementId).ToList();
                    breakdownList.Add(new
                    {
                        HostElementId = data.ElementId.GetIdValue(),
                        HostCategory = data.Category,
                        HostTypeName = data.TypeName,
                        GrossArea_m2 = Math.Round(data.GrossArea, 2),
                        OpeningArea_m2 = Math.Round(data.OpeningArea, 2),
                        NetArea_m2 = Math.Round(data.GrossArea - data.OpeningArea, 2),
                        FinishLayers = (includeFinishLayers && wallFinish != null && wallFinish.Count > 0)
                            ? wallFinish.Select(f => FormatFinishLayer(f)).ToList()
                            : (object)null
                    });
                }
            }

            string roomNameStr = room.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? room.Name;
            double wallNet = wallGrossArea - openingArea;

            var result = new
            {
                ElementId = room.Id.GetIdValue(),
                Name = roomNameStr,
                Number = room.Number,
                Level = doc.GetElement(room.LevelId)?.Name,
                Method = "SpatialElementGeometryCalculator",
                FloorArea_m2 = Math.Round(floorArea, 2),
                CeilingArea_m2 = Math.Round(ceilingArea, 2),
                WallGrossArea_m2 = Math.Round(wallGrossArea, 2),
                OpeningArea_m2 = Math.Round(openingArea, 2),
                WallNetArea_m2 = Math.Round(wallNet, 2),
                TotalNetSurfaceArea_m2 = Math.Round(floorArea + ceilingArea + wallNet, 2),
                Breakdown = includeBreakdown ? breakdownList : null,
                FloorFinishLayers = (includeFinishLayers && floorFinishLayers != null && floorFinishLayers.Count > 0)
                    ? floorFinishLayers.Select(f => FormatFinishLayer(f)).ToList() : (object)null,
                CeilingFinishLayers = (includeFinishLayers && ceilingFinishLayers != null && ceilingFinishLayers.Count > 0)
                    ? ceilingFinishLayers.Select(f => FormatFinishLayer(f)).ToList() : (object)null,
                UnassociatedFinishLayers = (includeFinishLayers && unassociatedFinishLayers != null && unassociatedFinishLayers.Count > 0)
                    ? unassociatedFinishLayers.Select(f => FormatFinishLayer(f)).ToList() : (object)null,
                EstimatedSurfaces = (ceilingEstimated || floorEstimated)
                    ? new
                    {
                        CeilingEstimated = ceilingEstimated,
                        FloorEstimated = floorEstimated,
                        Note = "無實體元素，以房間平面面積估算（假設水平面）"
                    }
                    : (object)null
            };

            return result;
        }

        /// <summary>
        /// Fallback：邊界線段 × 高度 簡化計算
        /// </summary>
        private object CalculateRoomSurfacesFallback(Document doc, Room room,
            bool includeBreakdown, bool subtractOpenings, bool includeFinishLayers = false,
            string defaultFloorFinish = null, string defaultWallFinish = null, string defaultCeilingFinish = null)
        {
            double floorArea = room.Area * SQ_FT_TO_SQ_M;
            double ceilingArea = floorArea; // 假設平天花板

            // 取得房間高度
            double roomHeightFt = GetRoomHeightInFeet(room);

            // 計算牆面面積：邊界線段長度 × 高度
            var options = new SpatialElementBoundaryOptions();
            var segments = room.GetBoundarySegments(options);
            double wallGrossArea = 0;
            var breakdownList = new List<object>();

            if (segments != null)
            {
                foreach (var loop in segments)
                {
                    foreach (var seg in loop)
                    {
                        double segLengthM = seg.GetCurve().Length * 0.3048; // ft → m
                        double segHeightM = roomHeightFt * 0.3048;
                        double segArea = segLengthM * segHeightM;
                        wallGrossArea += segArea;

                        if (includeBreakdown)
                        {
                            ElementId hostId = seg.ElementId;
                            Element hostElement = hostId != ElementId.InvalidElementId ? doc.GetElement(hostId) : null;
                            string typeName = "Unknown";
                            if (hostElement is Wall wall)
                                typeName = wall.WallType?.Name ?? "Unknown";
                            else if (hostElement != null)
                                typeName = hostElement.Name;

                            breakdownList.Add(new
                            {
                                HostElementId = hostId.GetIdValue(),
                                HostCategory = "Wall",
                                HostTypeName = typeName,
                                GrossArea_m2 = Math.Round(segArea, 2),
                                OpeningArea_m2 = 0.0,
                                NetArea_m2 = Math.Round(segArea, 2)
                            });
                        }
                    }
                }
            }

            // 簡易開口計算：從牆上的 inserts 取得
            double openingArea = 0;
            if (subtractOpenings && segments != null)
            {
                var processedWalls = new HashSet<ElementId>();
                foreach (var loop in segments)
                {
                    foreach (var seg in loop)
                    {
                        ElementId hostId = seg.ElementId;
                        if (hostId == ElementId.InvalidElementId || processedWalls.Contains(hostId))
                            continue;

                        Element hostElement = doc.GetElement(hostId);
                        if (hostElement is Wall wall)
                        {
                            processedWalls.Add(hostId);
                            var inserts = wall.FindInserts(true, true, true, true);
                            foreach (var insertId in inserts)
                            {
                                var insert = doc.GetElement(insertId);
                                if (insert == null) continue;
                                var insertArea = GetInsertArea(insert);
                                openingArea += insertArea;
                            }
                        }
                    }
                }
            }

            // Fallback 粉刷層偵測（僅 BoundingBox，無 Solid 精篩）
            List<object> fbFloorFinish = null, fbCeilingFinish = null, fbUnassocFinish = null;
            if (includeFinishLayers)
            {
                try
                {
                    var boundaryIds = new HashSet<ElementId>();
                    if (segments != null)
                        foreach (var loop in segments)
                            foreach (var seg in loop)
                                if (seg.ElementId != ElementId.InvalidElementId)
                                    boundaryIds.Add(seg.ElementId);

                    var bbCandidates = DetectFinishLayersByBoundingBox(doc, room, boundaryIds);
                    var finishElements = bbCandidates.Where(e => IsElementInsideRoom(room, e)).ToList();
                    Logger.Info($"[RoomSurface] Fallback: BB candidates={bbCandidates.Count}, after IsPointInRoom filter={finishElements.Count}");
                    var finishData = finishElements.Select(e => BuildFinishLayerData(doc, e, boundaryIds)).ToList();

                    var wallFinish = finishData.Where(f => f.Category == "Wall" && f.AssociatedBoundaryId != ElementId.InvalidElementId).ToList();
                    var floorFinish = finishData.Where(f => f.Category == "Floor").ToList();
                    var ceilingFinish = finishData.Where(f => f.Category == "Ceiling").ToList();
                    var unassocFinish = finishData.Where(f => f.Category == "Wall" && f.AssociatedBoundaryId == ElementId.InvalidElementId).ToList();

                    AssignFallbackCoverageAreas(wallFinish, floorArea, ceilingArea, "Wall");
                    AssignFallbackCoverageAreas(floorFinish, floorArea, ceilingArea, "Floor");
                    AssignFallbackCoverageAreas(ceilingFinish, floorArea, ceilingArea, "Ceiling");

                    // 對缺少粉刷的表面填入預設值（Fallback 路徑無 hostAreas，牆面以 breakdown 為基礎）
                    // 地板預設
                    if (floorFinish.Count == 0 && !string.IsNullOrEmpty(defaultFloorFinish))
                    {
                        var (typeName, typeMark, layers) = LookupFinishType(doc, defaultFloorFinish, "Floor");
                        floorFinish.Add(new FinishLayerData
                        {
                            ElementId = ElementId.InvalidElementId, Category = "Floor",
                            TypeName = typeName, TypeMark = typeMark, Thickness_mm = 0,
                            CoverageArea = Math.Round(floorArea, 2), AreaMethod = "DefaultFill",
                            AssociatedBoundaryId = ElementId.InvalidElementId, CompoundLayers = layers
                        });
                    }
                    // 天花預設
                    if (ceilingFinish.Count == 0 && !string.IsNullOrEmpty(defaultCeilingFinish))
                    {
                        var (typeName, typeMark, layers) = LookupFinishType(doc, defaultCeilingFinish, "Ceiling");
                        ceilingFinish.Add(new FinishLayerData
                        {
                            ElementId = ElementId.InvalidElementId, Category = "Ceiling",
                            TypeName = typeName, TypeMark = typeMark, Thickness_mm = 0,
                            CoverageArea = Math.Round(ceilingArea, 2), AreaMethod = "DefaultFill",
                            AssociatedBoundaryId = ElementId.InvalidElementId, CompoundLayers = layers
                        });
                    }
                    // 牆面預設：對每面沒有粉刷的邊界牆填入預設值
                    if (!string.IsNullOrEmpty(defaultWallFinish))
                    {
                        var coveredIds = new HashSet<ElementId>(wallFinish.Select(f => f.AssociatedBoundaryId));
                        var (typeName, typeMark, layers) = LookupFinishType(doc, defaultWallFinish, "Wall");
                        foreach (dynamic bd in breakdownList)
                        {
                            var hostId = new ElementId((IdType)bd.HostElementId);
                            if (coveredIds.Contains(hostId)) continue;
                            double netArea = (double)bd.NetArea_m2;
                            if (netArea <= 0) continue;
                            wallFinish.Add(new FinishLayerData
                            {
                                ElementId = ElementId.InvalidElementId, Category = "Wall",
                                TypeName = typeName, TypeMark = typeMark, Thickness_mm = 0,
                                CoverageArea = netArea, AreaMethod = "DefaultFill",
                                AssociatedBoundaryId = hostId, CompoundLayers = layers
                            });
                        }
                    }

                    // 更新 breakdown 加入 FinishLayers
                    if (includeBreakdown && wallFinish.Count > 0)
                    {
                        var newBreakdown = new List<object>();
                        foreach (dynamic bd in breakdownList)
                        {
                            var hostId = new ElementId((IdType)bd.HostElementId);
                            var wf = wallFinish.Where(f => f.AssociatedBoundaryId == hostId).ToList();
                            newBreakdown.Add(new
                            {
                                HostElementId = bd.HostElementId,
                                HostCategory = bd.HostCategory,
                                HostTypeName = bd.HostTypeName,
                                GrossArea_m2 = bd.GrossArea_m2,
                                OpeningArea_m2 = bd.OpeningArea_m2,
                                NetArea_m2 = bd.NetArea_m2,
                                FinishLayers = wf.Count > 0 ? wf.Select(f => FormatFinishLayer(f)).ToList() : (object)null
                            });
                        }
                        breakdownList = newBreakdown;
                    }

                    fbFloorFinish = floorFinish.Count > 0 ? floorFinish.Select(f => FormatFinishLayer(f)).ToList() : null;
                    fbCeilingFinish = ceilingFinish.Count > 0 ? ceilingFinish.Select(f => FormatFinishLayer(f)).ToList() : null;
                    fbUnassocFinish = unassocFinish.Count > 0 ? unassocFinish.Select(f => FormatFinishLayer(f)).ToList() : null;
                }
                catch { /* 粉刷層偵測失敗不影響主結果 */ }
            }

            string roomNameStr = room.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? room.Name;
            double wallNet = wallGrossArea - openingArea;

            return new
            {
                ElementId = room.Id.GetIdValue(),
                Name = roomNameStr,
                Number = room.Number,
                Level = doc.GetElement(room.LevelId)?.Name,
                Method = "Fallback_BoundarySegments",
                FloorArea_m2 = Math.Round(floorArea, 2),
                CeilingArea_m2 = Math.Round(ceilingArea, 2),
                WallGrossArea_m2 = Math.Round(wallGrossArea, 2),
                OpeningArea_m2 = Math.Round(openingArea, 2),
                WallNetArea_m2 = Math.Round(wallNet, 2),
                TotalNetSurfaceArea_m2 = Math.Round(floorArea + ceilingArea + wallNet, 2),
                Breakdown = includeBreakdown ? breakdownList : null,
                FloorFinishLayers = fbFloorFinish,
                CeilingFinishLayers = fbCeilingFinish,
                UnassociatedFinishLayers = fbUnassocFinish,
                Warning = "使用簡化計算（邊界線段×高度），斜天花板或不規則幾何可能不準確"
            };
        }

        #region Room Surface Helpers

        /// <summary>
        /// 依 face normal 分類表面
        /// </summary>
        private void ClassifyByNormal(Face face, double faceArea,
            ref double floorArea, ref double ceilingArea, ref double wallGrossArea)
        {
            XYZ normal = face.ComputeNormal(new UV(0.5, 0.5));
            double zComponent = Math.Abs(normal.Z);

            if (zComponent > 0.8)
            {
                if (normal.Z < 0)
                    floorArea += faceArea;
                else
                    ceilingArea += faceArea;
            }
            else
            {
                wallGrossArea += faceArea;
            }
        }

        /// <summary>
        /// 從 SpatialElementBoundarySubface 取得宿主 ElementId
        /// </summary>
        private ElementId GetHostElementIdFromFaceInfo(Document doc, SpatialElementBoundarySubface subface)
        {
            try
            {
                var linkElementId = subface.SpatialBoundaryElement;
                return linkElementId.HostElementId;
            }
            catch
            {
                // 忽略錯誤
            }
            return ElementId.InvalidElementId;
        }

        /// <summary>
        /// 判斷宿主元素的表面類別
        /// </summary>
        private string GetSurfaceCategory(Element hostElement, Face face)
        {
            if (hostElement == null) return "Unknown";

            var cat = hostElement.Category;
            if (cat == null) return "Unknown";

            var catId = cat.Id.GetIdValue();

            // 用 BuiltInCategory 判斷
            if (catId == (IdType)(int)BuiltInCategory.OST_Floors)
                return "Floor";
            if (catId == (IdType)(int)BuiltInCategory.OST_Ceilings)
                return "Ceiling";
            if (catId == (IdType)(int)BuiltInCategory.OST_Roofs)
                return "Ceiling";
            if (catId == (IdType)(int)BuiltInCategory.OST_Walls)
                return "Wall";

            // 其他類別，依法線判斷
            XYZ normal = face.ComputeNormal(new UV(0.5, 0.5));
            if (Math.Abs(normal.Z) > 0.8)
                return normal.Z < 0 ? "Floor" : "Ceiling";

            return "Wall";
        }

        /// <summary>
        /// 計算牆上所有嵌入物的開口面積（門、窗、Opening 族群等）
        /// </summary>
        private double CalculateWallOpeningArea(Document doc, Wall wall, Room room, Face wallFace)
        {
            double totalOpeningArea = 0;
            var inserts = wall.FindInserts(true, true, true, true);

            foreach (var insertId in inserts)
            {
                var insert = doc.GetElement(insertId);
                if (insert == null || insert.Category == null) continue;

                // 排除明確非開口的類別
                var insertCat = insert.Category.Id.GetIdValue();
                if (insertCat == (IdType)(int)BuiltInCategory.OST_Rooms ||
                    insertCat == (IdType)(int)BuiltInCategory.OST_Areas)
                    continue;

                totalOpeningArea += GetInsertArea(insert);
            }

            return totalOpeningArea;
        }

        /// <summary>
        /// 取得門窗開口面積（m²）
        /// </summary>
        private double GetInsertArea(Element insert)
        {
            if (insert == null) return 0;

            double width = 0, height = 0;
            var catId = insert.Category?.Id.GetIdValue();

            // 1. Instance 內建參數
            if (catId == (IdType)(int)BuiltInCategory.OST_Windows)
            {
                width = insert.get_Parameter(BuiltInParameter.WINDOW_WIDTH)?.AsDouble() ?? 0;
                height = insert.get_Parameter(BuiltInParameter.WINDOW_HEIGHT)?.AsDouble() ?? 0;
            }
            else if (catId == (IdType)(int)BuiltInCategory.OST_Doors)
            {
                width = insert.get_Parameter(BuiltInParameter.DOOR_WIDTH)?.AsDouble() ?? 0;
                height = insert.get_Parameter(BuiltInParameter.DOOR_HEIGHT)?.AsDouble() ?? 0;
            }

            // 2. 粗開口尺寸（Rough Width/Height）— 自訂族群常用
            if (width <= 0 || height <= 0)
            {
                if (width <= 0)
                    width = insert.get_Parameter(BuiltInParameter.FAMILY_ROUGH_WIDTH_PARAM)?.AsDouble() ?? 0;
                if (height <= 0)
                    height = insert.get_Parameter(BuiltInParameter.FAMILY_ROUGH_HEIGHT_PARAM)?.AsDouble() ?? 0;
            }

            // 3. FamilySymbol（Type）參數 — instance 沒有時從 type 讀取
            if ((width <= 0 || height <= 0) && insert is FamilyInstance fi && fi.Symbol != null)
            {
                var symbol = fi.Symbol;
                if (width <= 0)
                {
                    width = symbol.get_Parameter(BuiltInParameter.WINDOW_WIDTH)?.AsDouble() ?? 0;
                    if (width <= 0) width = symbol.get_Parameter(BuiltInParameter.DOOR_WIDTH)?.AsDouble() ?? 0;
                    if (width <= 0) width = symbol.get_Parameter(BuiltInParameter.FAMILY_ROUGH_WIDTH_PARAM)?.AsDouble() ?? 0;
                }
                if (height <= 0)
                {
                    height = symbol.get_Parameter(BuiltInParameter.WINDOW_HEIGHT)?.AsDouble() ?? 0;
                    if (height <= 0) height = symbol.get_Parameter(BuiltInParameter.DOOR_HEIGHT)?.AsDouble() ?? 0;
                    if (height <= 0) height = symbol.get_Parameter(BuiltInParameter.FAMILY_ROUGH_HEIGHT_PARAM)?.AsDouble() ?? 0;
                }
            }

            if (width > 0 && height > 0)
            {
                // ft² → m²
                return width * height * SQ_FT_TO_SQ_M;
            }

            // 4. BoundingBox fallback
            var bbox = insert.get_BoundingBox(null);
            if (bbox != null)
            {
                double w = Math.Abs(bbox.Max.X - bbox.Min.X);
                double h = Math.Abs(bbox.Max.Z - bbox.Min.Z);
                double d = Math.Abs(bbox.Max.Y - bbox.Min.Y);
                // 取較小的兩個維度（排除深度方向）
                var dims = new[] { w, h, d }.OrderBy(x => x).ToArray();
                return dims[1] * dims[2] * SQ_FT_TO_SQ_M;
            }

            return 0;
        }

        /// <summary>
        /// 取得房間高度（英尺）
        /// </summary>
        private double GetRoomHeightInFeet(Room room)
        {
            // 嘗試從 Unbounded Height 參數取得
            var heightParam = room.get_Parameter(BuiltInParameter.ROOM_HEIGHT);
            if (heightParam != null && heightParam.AsDouble() > 0)
                return heightParam.AsDouble();

            // 嘗試從 Upper Limit + Offset 計算
            var upperLimitParam = room.get_Parameter(BuiltInParameter.ROOM_UPPER_LEVEL);
            var upperOffsetParam = room.get_Parameter(BuiltInParameter.ROOM_UPPER_OFFSET);
            var lowerOffsetParam = room.get_Parameter(BuiltInParameter.ROOM_LOWER_OFFSET);
            if (upperOffsetParam != null)
            {
                double upperOffset = upperOffsetParam.AsDouble();
                double lowerOffset = lowerOffsetParam?.AsDouble() ?? 0;
                if (upperOffset > 0)
                    return upperOffset - lowerOffset;
            }

            // Fallback：從 BoundingBox
            var bbox = room.get_BoundingBox(null);
            if (bbox != null)
                return bbox.Max.Z - bbox.Min.Z;

            return 3.0 / 0.3048; // 預設 3m
        }

        /// <summary>
        /// 宿主面資料暫存
        /// </summary>
        private class HostFaceData
        {
            public ElementId ElementId { get; set; }
            public string Category { get; set; }
            public string TypeName { get; set; }
            public double GrossArea { get; set; }
            public double OpeningArea { get; set; }
        }

        #endregion

        #region Finish Layer Detection

        /// <summary>
        /// 粉刷層資料
        /// </summary>
        private class FinishLayerData
        {
            public ElementId ElementId { get; set; }
            public string Category { get; set; } // Wall, Floor, Ceiling
            public string TypeName { get; set; }
            public string TypeMark { get; set; }
            public double Thickness_mm { get; set; }
            public double CoverageArea { get; set; }
            public string AreaMethod { get; set; } // SolidIntersection, LocationCurve, SurfaceArea, ElementArea
            public ElementId AssociatedBoundaryId { get; set; }
            public List<CompoundLayerInfo> CompoundLayers { get; set; }
            public Element Element { get; set; } // 暫存用，不序列化
        }

        private class CompoundLayerInfo
        {
            public string Function { get; set; }
            public string MaterialName { get; set; }
            public double Thickness_mm { get; set; }
        }

        /// <summary>
        /// 偵測房間內的粉刷層（混合式：BB 預篩 + 排除邊界 + Solid 精篩）
        /// </summary>
        private List<FinishLayerData> DetectFinishLayers(Document doc, Room room, Solid roomSolid,
            Dictionary<ElementId, HostFaceData> hostAreas)
        {
            // Step 1: 收集邊界元素 ID
            var boundaryIds = new HashSet<ElementId>();
            try
            {
                var boundarySegments = room.GetBoundarySegments(new SpatialElementBoundaryOptions());
                if (boundarySegments != null)
                    foreach (var loop in boundarySegments)
                        foreach (var seg in loop)
                            if (seg.ElementId != ElementId.InvalidElementId)
                                boundaryIds.Add(seg.ElementId);
            }
            catch { }

            if (hostAreas != null)
                foreach (var hostId in hostAreas.Keys)
                    boundaryIds.Add(hostId);

            Logger.Info($"[RoomSurface] DetectFinishLayers: room={room.Name}, boundaryIds={boundaryIds.Count}");

            // Step 2: BoundingBox 預篩 + 排除邊界
            var roomBBox = room.get_BoundingBox(null);
            if (roomBBox == null)
            {
                Logger.Info($"[RoomSurface] Room BoundingBox is null, skip finish detection");
                return new List<FinishLayerData>();
            }

            var outline = new Outline(roomBBox.Min, roomBBox.Max);

            var candidates = new List<Element>();

            // 牆壁候選
            var wallCandidates = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Walls)
                .WhereElementIsNotElementType()
                .WherePasses(new BoundingBoxIntersectsFilter(outline))
                .Where(e => !boundaryIds.Contains(e.Id)).ToList();
            candidates.AddRange(wallCandidates);

            // 樓板候選
            var floorCandidates = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Floors)
                .WhereElementIsNotElementType()
                .WherePasses(new BoundingBoxIntersectsFilter(outline))
                .Where(e => !boundaryIds.Contains(e.Id)).ToList();
            candidates.AddRange(floorCandidates);

            // 天花板候選
            var ceilingCandidates = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Ceilings)
                .WhereElementIsNotElementType()
                .WherePasses(new BoundingBoxIntersectsFilter(outline))
                .Where(e => !boundaryIds.Contains(e.Id)).ToList();
            candidates.AddRange(ceilingCandidates);

            Logger.Info($"[RoomSurface] BoundingBox candidates: walls={wallCandidates.Count}, floors={floorCandidates.Count}, ceilings={ceilingCandidates.Count}");

            // Step 3: Solid 交集精確驗證 + IsPointInRoom 備援
            var finishElements = new List<Element>();
            int solidPassCount = 0, pointPassCount = 0;
            foreach (var candidate in candidates)
            {
                if (IsElementInsideRoomSolid(candidate, roomSolid))
                {
                    finishElements.Add(candidate);
                    solidPassCount++;
                }
                else if (IsElementInsideRoom(room, candidate))
                {
                    finishElements.Add(candidate);
                    pointPassCount++;
                    Logger.Info($"[RoomSurface] Element {candidate.Id.GetIdValue()} passed IsPointInRoom fallback");
                }
            }

            Logger.Info($"[RoomSurface] Final finish elements: {finishElements.Count} (Solid: {solidPassCount}, IsPointInRoom: {pointPassCount})");

            // Step 4: 建立粉刷層資料
            var result = new List<FinishLayerData>();
            foreach (var element in finishElements)
            {
                var data = BuildFinishLayerData(doc, element, boundaryIds);
                result.Add(data);
            }

            return result;
        }

        /// <summary>
        /// 判斷元素是否在 Room Solid 內（Solid 交集驗證）
        /// </summary>
        private bool IsElementInsideRoomSolid(Element candidate, Solid roomSolid)
        {
            try
            {
                var geomOptions = new Options();
                var geom = candidate.get_Geometry(geomOptions);
                if (geom == null) return false;

                foreach (GeometryObject gObj in geom)
                {
                    Solid candidateSolid = gObj as Solid;
                    if (candidateSolid == null || candidateSolid.Volume < 1e-9)
                    {
                        // 可能是 GeometryInstance（族群）
                        if (gObj is GeometryInstance gi)
                        {
                            foreach (GeometryObject subObj in gi.GetInstanceGeometry())
                            {
                                candidateSolid = subObj as Solid;
                                if (candidateSolid != null && candidateSolid.Volume > 1e-9)
                                    break;
                            }
                        }
                        if (candidateSolid == null || candidateSolid.Volume < 1e-9)
                            continue;
                    }

                    try
                    {
                        Solid intersection = BooleanOperationsUtils.ExecuteBooleanOperation(
                            roomSolid, candidateSolid, BooleanOperationsType.Intersect);
                        if (intersection != null && intersection.Volume > 1e-9)
                            return true;
                    }
                    catch
                    {
                        // BooleanOp 失敗，Fallback 到 ElementIntersectsSolidFilter
                        try
                        {
                            var solidFilter = new ElementIntersectsSolidFilter(roomSolid);
                            return solidFilter.PassesFilter(candidate);
                        }
                        catch { return false; }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Info($"[RoomSurface] IsElementInsideRoomSolid geometry error for {candidate.Id.GetIdValue()}: {ex.Message}");
            }
            return false;
        }

        /// <summary>
        /// 使用 Room.IsPointInRoom 判斷元素是否在房間內
        /// 作為 Solid 交集失敗時的備援判斷
        /// </summary>
        private bool IsElementInsideRoom(Room room, Element candidate)
        {
            try
            {
                var points = new List<XYZ>();

                if (candidate.Location is LocationCurve locCurve)
                {
                    var curve = locCurve.Curve;
                    // 測試 25%, 50%, 75% 三個點，處理 L/T 形房間邊角的粉刷牆
                    points.Add(curve.Evaluate(0.25, true));
                    points.Add(curve.Evaluate(0.50, true));
                    points.Add(curve.Evaluate(0.75, true));
                }
                else if (candidate.Location is LocationPoint locPoint)
                {
                    points.Add(locPoint.Point);
                }
                else
                {
                    // 無位置資訊，使用 BoundingBox 中心
                    var bbox = candidate.get_BoundingBox(null);
                    if (bbox != null)
                    {
                        points.Add(new XYZ(
                            (bbox.Min.X + bbox.Max.X) / 2,
                            (bbox.Min.Y + bbox.Max.Y) / 2,
                            (bbox.Min.Z + bbox.Max.Z) / 2));
                    }
                }

                foreach (var pt in points)
                {
                    if (room.IsPointInRoom(pt))
                        return true;
                }
            }
            catch (Exception ex)
            {
                Logger.Info($"[RoomSurface] IsElementInsideRoom failed for {candidate.Id.GetIdValue()}: {ex.Message}");
            }
            return false;
        }

        /// <summary>
        /// Fallback: 僅 BoundingBox 偵測（無 Solid 精篩）
        /// </summary>
        private List<Element> DetectFinishLayersByBoundingBox(Document doc, Room room, HashSet<ElementId> boundaryIds)
        {
            var roomBBox = room.get_BoundingBox(null);
            if (roomBBox == null) return new List<Element>();

            var outline = new Outline(roomBBox.Min, roomBBox.Max);
            var candidates = new List<Element>();

            candidates.AddRange(new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Walls).WhereElementIsNotElementType()
                .WherePasses(new BoundingBoxIntersectsFilter(outline))
                .Where(e => !boundaryIds.Contains(e.Id)));

            candidates.AddRange(new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Floors).WhereElementIsNotElementType()
                .WherePasses(new BoundingBoxIntersectsFilter(outline))
                .Where(e => !boundaryIds.Contains(e.Id)));

            candidates.AddRange(new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Ceilings).WhereElementIsNotElementType()
                .WherePasses(new BoundingBoxIntersectsFilter(outline))
                .Where(e => !boundaryIds.Contains(e.Id)));

            return candidates;
        }

        /// <summary>
        /// 從元素建立 FinishLayerData
        /// </summary>
        private FinishLayerData BuildFinishLayerData(Document doc, Element element, HashSet<ElementId> boundaryIds)
        {
            string category = "Unknown";
            string typeName = "Unknown";
            string typeMark = null;
            double thickness = 0;
            CompoundStructure cs = null;

            if (element is Wall wall)
            {
                category = "Wall";
                typeName = wall.WallType?.Name ?? "Unknown";
                thickness = wall.Width * FT_TO_MM;
                cs = wall.WallType?.GetCompoundStructure();
                typeMark = wall.WallType?.get_Parameter(BuiltInParameter.ALL_MODEL_TYPE_MARK)?.AsString();
            }
            else if (element is Floor floor)
            {
                category = "Floor";
                typeName = floor.FloorType?.Name ?? "Unknown";
                thickness = floor.get_Parameter(BuiltInParameter.FLOOR_ATTR_THICKNESS_PARAM)?.AsDouble() * FT_TO_MM ?? 0;
                cs = floor.FloorType?.GetCompoundStructure();
                typeMark = floor.FloorType?.get_Parameter(BuiltInParameter.ALL_MODEL_TYPE_MARK)?.AsString();
            }
            else if (element is Ceiling ceiling)
            {
                category = "Ceiling";
                var ceilingTypeId = ceiling.GetTypeId();
                var ceilingType = doc.GetElement(ceilingTypeId) as CeilingType;
                typeName = ceilingType?.Name ?? "Unknown";
                cs = ceilingType?.GetCompoundStructure();
                typeMark = ceilingType?.get_Parameter(BuiltInParameter.ALL_MODEL_TYPE_MARK)?.AsString();
                if (cs != null)
                    thickness = cs.GetLayers().Sum(l => l.Width) * FT_TO_MM;
            }

            // Fallback type mark → type name
            if (string.IsNullOrEmpty(typeMark))
                typeMark = typeName;

            // 複合結構層
            var compoundLayers = GetCompoundLayerInfo(doc, cs);

            // 關聯母牆（僅牆面）
            ElementId associatedId = ElementId.InvalidElementId;
            if (category == "Wall" && element is Wall finishWall)
                associatedId = FindAssociatedBoundaryWall(doc, finishWall, boundaryIds);

            return new FinishLayerData
            {
                ElementId = element.Id,
                Category = category,
                TypeName = typeName,
                TypeMark = typeMark,
                Thickness_mm = Math.Round(thickness, 1),
                CoverageArea = 0,
                AreaMethod = "Pending",
                AssociatedBoundaryId = associatedId,
                CompoundLayers = compoundLayers,
                Element = element
            };
        }

        /// <summary>
        /// 擷取複合結構層材料資訊
        /// </summary>
        private List<CompoundLayerInfo> GetCompoundLayerInfo(Document doc, CompoundStructure cs)
        {
            if (cs == null) return new List<CompoundLayerInfo>();

            var result = new List<CompoundLayerInfo>();
            foreach (var layer in cs.GetLayers())
            {
                Material mat = layer.MaterialId != ElementId.InvalidElementId
                    ? doc.GetElement(layer.MaterialId) as Material : null;
                result.Add(new CompoundLayerInfo
                {
                    Function = layer.Function.ToString(),
                    MaterialName = mat?.Name ?? "未指定",
                    Thickness_mm = Math.Round(layer.Width * FT_TO_MM, 1)
                });
            }
            return result;
        }

        /// <summary>
        /// 關聯母牆（平行度 > 0.95 + 距離 < 1000mm）
        /// </summary>
        private ElementId FindAssociatedBoundaryWall(Document doc, Wall finishWall, HashSet<ElementId> boundaryIds)
        {
            LocationCurve finishLoc = finishWall.Location as LocationCurve;
            if (finishLoc == null) return ElementId.InvalidElementId;

            Line finishLine = finishLoc.Curve as Line;
            if (finishLine == null) return ElementId.InvalidElementId; // 弧形牆暫不處理

            XYZ finishDir = finishLine.Direction;
            ElementId bestMatch = ElementId.InvalidElementId;
            double bestDistance = double.MaxValue;

            foreach (var bId in boundaryIds)
            {
                Wall bWall = doc.GetElement(bId) as Wall;
                if (bWall == null) continue;

                LocationCurve bLoc = bWall.Location as LocationCurve;
                if (bLoc == null) continue;
                Line bLine = bLoc.Curve as Line;
                if (bLine == null) continue;

                // 平行度 > 0.95
                double dot = Math.Abs(finishDir.DotProduct(bLine.Direction));
                if (dot < 0.95) continue;

                // 距離 < 1000mm
                XYZ midPoint = finishLine.Evaluate(0.5, true);
                IntersectionResult proj = bLine.Project(midPoint);
                if (proj == null) continue;

                double distance = midPoint.DistanceTo(proj.XYZPoint) * FT_TO_MM;
                if (distance < 1000 && distance < bestDistance)
                {
                    bestDistance = distance;
                    bestMatch = bId;
                }
            }

            return bestMatch;
        }

        /// <summary>
        /// 計算粉刷層覆蓋面積（三層邏輯）
        /// </summary>
        private void CalculateFinishCoverageAreas(Document doc, List<FinishLayerData> finishLayers,
            Dictionary<ElementId, HostFaceData> hostAreas, Solid roomSolid,
            double floorArea, double ceilingArea)
        {
            if (finishLayers == null || finishLayers.Count == 0) return;

            // 按類別分組處理
            // 牆面粉刷：按母牆分組
            var wallGroups = finishLayers.Where(f => f.Category == "Wall" && f.AssociatedBoundaryId != ElementId.InvalidElementId)
                .GroupBy(f => f.AssociatedBoundaryId);

            foreach (var group in wallGroups)
            {
                var layers = group.ToList();
                double hostNetArea = 0;
                if (hostAreas != null && hostAreas.ContainsKey(group.Key))
                {
                    var hostData = hostAreas[group.Key];
                    hostNetArea = hostData.GrossArea - hostData.OpeningArea;
                }

                if (layers.Count == 1)
                {
                    // 單一粉刷 → 面積 = 母牆 NetArea
                    layers[0].CoverageArea = hostNetArea;
                    layers[0].AreaMethod = "SurfaceArea";
                }
                else
                {
                    // 多種粉刷 → LocationCurve 投影
                    CalculateWallFinishByProjection(doc, layers, group.Key, hostNetArea);
                }
            }

            // 地板粉刷
            var floorLayers = finishLayers.Where(f => f.Category == "Floor").ToList();
            CalculateHorizontalFinishAreas(floorLayers, roomSolid, floorArea, isFloor: true);

            // 天花板粉刷
            var ceilingLayers = finishLayers.Where(f => f.Category == "Ceiling").ToList();
            CalculateHorizontalFinishAreas(ceilingLayers, roomSolid, ceilingArea, isFloor: false);

            // 未關聯牆面粉刷：使用元素自身面積
            foreach (var layer in finishLayers.Where(f => f.Category == "Wall" && f.AssociatedBoundaryId == ElementId.InvalidElementId))
            {
                layer.CoverageArea = layer.Element?.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED)?.AsDouble() * SQ_FT_TO_SQ_M ?? 0;
                layer.AreaMethod = "ElementArea";
            }
        }

        /// <summary>
        /// 牆面多粉刷層：LocationCurve 投影計算各自面積
        /// </summary>
        private void CalculateWallFinishByProjection(Document doc, List<FinishLayerData> layers,
            ElementId boundaryWallId, double hostNetArea)
        {
            Wall boundaryWall = doc.GetElement(boundaryWallId) as Wall;
            if (boundaryWall == null)
            {
                // 無法取得母牆，用元素自身面積
                foreach (var layer in layers)
                {
                    layer.CoverageArea = layer.Element?.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED)?.AsDouble() * SQ_FT_TO_SQ_M ?? 0;
                    layer.AreaMethod = "ElementArea";
                }
                return;
            }

            LocationCurve boundaryLoc = boundaryWall.Location as LocationCurve;
            if (boundaryLoc == null)
            {
                foreach (var layer in layers)
                {
                    layer.CoverageArea = layer.Element?.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED)?.AsDouble() * SQ_FT_TO_SQ_M ?? 0;
                    layer.AreaMethod = "ElementArea";
                }
                return;
            }

            Curve boundaryCurve = boundaryLoc.Curve;
            double boundaryHeightFt = boundaryWall.get_Parameter(BuiltInParameter.WALL_USER_HEIGHT_PARAM)?.AsDouble() ?? 0;
            double hostGrossArea = 0;
            if (boundaryHeightFt > 0)
                hostGrossArea = boundaryCurve.Length * boundaryHeightFt * SQ_FT_TO_SQ_M;
            double netRatio = hostGrossArea > 0 ? hostNetArea / hostGrossArea : 1.0;

            foreach (var layer in layers)
            {
                try
                {
                    if (layer.Element is Wall finishWall)
                    {
                        LocationCurve finishLoc = finishWall.Location as LocationCurve;
                        if (finishLoc == null) throw new Exception("No LocationCurve");

                        Curve finishCurve = finishLoc.Curve;
                        IntersectionResult projStart = boundaryCurve.Project(finishCurve.GetEndPoint(0));
                        IntersectionResult projEnd = boundaryCurve.Project(finishCurve.GetEndPoint(1));

                        if (projStart == null || projEnd == null) throw new Exception("Projection failed");

                        double coverageLength = finishCurve.Length; // 使用粉刷牆自身長度更準確
                        double finishHeight = finishWall.get_Parameter(BuiltInParameter.WALL_USER_HEIGHT_PARAM)?.AsDouble() ?? boundaryHeightFt;

                        layer.CoverageArea = coverageLength * finishHeight * SQ_FT_TO_SQ_M * netRatio;
                        layer.AreaMethod = "LocationCurve";
                    }
                    else throw new Exception("Not a wall");
                }
                catch
                {
                    layer.CoverageArea = layer.Element?.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED)?.AsDouble() * SQ_FT_TO_SQ_M ?? 0;
                    layer.AreaMethod = "ElementArea";
                }
            }
        }

        /// <summary>
        /// 地板/天花板粉刷層面積計算
        /// </summary>
        private void CalculateHorizontalFinishAreas(List<FinishLayerData> layers, Solid roomSolid,
            double surfaceArea, bool isFloor)
        {
            if (layers == null || layers.Count == 0) return;

            if (layers.Count == 1)
            {
                layers[0].CoverageArea = surfaceArea;
                layers[0].AreaMethod = "SurfaceArea";
                return;
            }

            // 多種粉刷 → Solid 布林交集
            foreach (var layer in layers)
            {
                try
                {
                    var geomOptions = new Options();
                    var geom = layer.Element.get_Geometry(geomOptions);
                    Solid elementSolid = null;
                    foreach (GeometryObject gObj in geom)
                    {
                        elementSolid = gObj as Solid;
                        if (elementSolid != null && elementSolid.Volume > 1e-9) break;
                        if (gObj is GeometryInstance gi)
                            foreach (GeometryObject sub in gi.GetInstanceGeometry())
                            {
                                elementSolid = sub as Solid;
                                if (elementSolid != null && elementSolid.Volume > 1e-9) break;
                            }
                        if (elementSolid != null && elementSolid.Volume > 1e-9) break;
                    }

                    if (elementSolid == null || elementSolid.Volume < 1e-9)
                        throw new Exception("No solid geometry");

                    Solid intersection = BooleanOperationsUtils.ExecuteBooleanOperation(
                        roomSolid, elementSolid, BooleanOperationsType.Intersect);

                    if (intersection == null || intersection.Volume < 1e-9)
                        throw new Exception("No intersection");

                    // 取水平面面積
                    double bestArea = 0;
                    foreach (Face face in intersection.Faces)
                    {
                        XYZ normal = face.ComputeNormal(new UV(0.5, 0.5));
                        if (Math.Abs(normal.Z) > 0.8)
                        {
                            double faceArea = face.Area * SQ_FT_TO_SQ_M;
                            if (faceArea > bestArea)
                                bestArea = faceArea;
                        }
                    }

                    if (bestArea > 0)
                    {
                        layer.CoverageArea = bestArea;
                        layer.AreaMethod = "SolidIntersection";
                    }
                    else throw new Exception("No horizontal face");
                }
                catch
                {
                    // Fallback: 元素自身面積
                    layer.CoverageArea = layer.Element?.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED)?.AsDouble() * SQ_FT_TO_SQ_M ?? 0;
                    layer.AreaMethod = "ElementArea";
                }
            }
        }

        /// <summary>
        /// Fallback 模式的覆蓋面積分配
        /// </summary>
        private void AssignFallbackCoverageAreas(List<FinishLayerData> layers, double floorArea, double ceilingArea, string category)
        {
            if (layers == null || layers.Count == 0) return;

            if (layers.Count == 1)
            {
                double area = category == "Floor" ? floorArea : category == "Ceiling" ? ceilingArea : 0;
                layers[0].CoverageArea = area;
                layers[0].AreaMethod = "SurfaceArea";
            }
            else
            {
                // Fallback 多種：使用元素自身面積
                foreach (var layer in layers)
                {
                    layer.CoverageArea = layer.Element?.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED)?.AsDouble() * SQ_FT_TO_SQ_M ?? 0;
                    layer.AreaMethod = "ElementArea";
                }
            }
        }

        /// <summary>
        /// 格式化粉刷層資料為輸出物件
        /// </summary>
        private object FormatFinishLayer(FinishLayerData f)
        {
            return new
            {
                ElementId = f.ElementId.GetIdValue(),
                TypeName = f.TypeName,
                TypeMark = f.TypeMark,
                Thickness_mm = f.Thickness_mm,
                CoverageArea_m2 = Math.Round(f.CoverageArea, 2),
                AreaMethod = f.AreaMethod,
                CompoundLayers = f.CompoundLayers.Count > 0
                    ? f.CompoundLayers.Select(c => new
                    {
                        c.Function,
                        c.MaterialName,
                        c.Thickness_mm
                    }).ToList()
                    : (object)null
            };
        }

        #endregion

        #region Finish Layer Parameters & Schedule & Excel

        /// <summary>
        /// 在已快取的 roomResults 上填入預設粉刷層（不做任何幾何分析）
        /// </summary>
        private List<object> ApplyDefaultsToResults(Document doc, List<object> cachedResults,
            string defaultFloorFinish, string defaultWallFinish, string defaultCeilingFinish)
        {
            // 預先查找類型（有快取，只查一次）
            (string typeName, string typeMark, List<CompoundLayerInfo> layers)? floorType = null;
            (string typeName, string typeMark, List<CompoundLayerInfo> layers)? wallType = null;
            (string typeName, string typeMark, List<CompoundLayerInfo> layers)? ceilingType = null;

            if (!string.IsNullOrEmpty(defaultFloorFinish))
                floorType = LookupFinishType(doc, defaultFloorFinish, "Floor");
            if (!string.IsNullOrEmpty(defaultWallFinish))
                wallType = LookupFinishType(doc, defaultWallFinish, "Wall");
            if (!string.IsNullOrEmpty(defaultCeilingFinish))
                ceilingType = LookupFinishType(doc, defaultCeilingFinish, "Ceiling");

            var updatedResults = new List<object>();
            foreach (dynamic r in cachedResults)
            {
                // 地板：若無粉刷層且有預設
                object newFloorFinish = r.FloorFinishLayers;
                if (r.FloorFinishLayers == null && floorType.HasValue)
                {
                    var ft = floorType.Value;
                    newFloorFinish = new List<object> { FormatDefaultFinishEntry(ft.typeName, ft.typeMark, (double)r.FloorArea_m2, ft.layers) };
                }

                // 天花板：若無粉刷層且有預設
                object newCeilingFinish = r.CeilingFinishLayers;
                if (r.CeilingFinishLayers == null && ceilingType.HasValue)
                {
                    var ct = ceilingType.Value;
                    newCeilingFinish = new List<object> { FormatDefaultFinishEntry(ct.typeName, ct.typeMark, (double)r.CeilingArea_m2, ct.layers) };
                }

                // 牆面 Breakdown：對每面沒有粉刷的牆填入預設
                object newBreakdown = r.Breakdown;
                if (r.Breakdown != null && wallType.HasValue)
                {
                    var wt = wallType.Value;
                    var updatedBreakdown = new List<object>();
                    foreach (dynamic bd in r.Breakdown)
                    {
                        if (bd.FinishLayers == null)
                        {
                            double netArea = (double)bd.NetArea_m2;
                            updatedBreakdown.Add(new
                            {
                                HostElementId = bd.HostElementId,
                                HostCategory = bd.HostCategory,
                                HostTypeName = bd.HostTypeName,
                                GrossArea_m2 = bd.GrossArea_m2,
                                OpeningArea_m2 = bd.OpeningArea_m2,
                                NetArea_m2 = bd.NetArea_m2,
                                FinishLayers = (object)new List<object> { FormatDefaultFinishEntry(wt.typeName, wt.typeMark, netArea, wt.layers) }
                            });
                        }
                        else
                        {
                            updatedBreakdown.Add(bd);
                        }
                    }
                    newBreakdown = updatedBreakdown;
                }

                // 重建 roomResult
                updatedResults.Add(new
                {
                    ElementId = (IdType)r.ElementId,
                    Name = (string)r.Name,
                    Number = (string)r.Number,
                    Level = (string)r.Level,
                    Method = (string)r.Method,
                    FloorArea_m2 = (double)r.FloorArea_m2,
                    CeilingArea_m2 = (double)r.CeilingArea_m2,
                    WallGrossArea_m2 = (double)r.WallGrossArea_m2,
                    OpeningArea_m2 = (double)r.OpeningArea_m2,
                    WallNetArea_m2 = (double)r.WallNetArea_m2,
                    TotalNetSurfaceArea_m2 = (double)r.TotalNetSurfaceArea_m2,
                    Breakdown = newBreakdown,
                    FloorFinishLayers = newFloorFinish,
                    CeilingFinishLayers = newCeilingFinish,
                    UnassociatedFinishLayers = r.UnassociatedFinishLayers,
                    EstimatedSurfaces = r.EstimatedSurfaces
                });
            }

            return updatedResults;
        }

        /// <summary>
        /// 格式化預設粉刷層條目（與 FormatFinishLayer 輸出格式相同）
        /// </summary>
        private object FormatDefaultFinishEntry(string typeName, string typeMark, double area, List<CompoundLayerInfo> layers)
        {
            return new
            {
                ElementId = (IdType)(-1),
                TypeName = typeName,
                TypeMark = typeMark,
                Thickness_mm = 0.0,
                CoverageArea_m2 = Math.Round(area, 2),
                AreaMethod = "DefaultFill",
                CompoundLayers = layers != null && layers.Count > 0
                    ? layers.Select(c => new { c.Function, c.MaterialName, c.Thickness_mm }).ToList()
                    : (object)null
            };
        }

        /// <summary>
        /// 根據類型標記查找專案中的實際粉刷類型
        /// </summary>
        // 快取 LookupFinishType 結果，避免每個房間重複查詢
        private Dictionary<string, (string typeName, string typeMark, List<CompoundLayerInfo> layers)> _finishTypeCache
            = new Dictionary<string, (string, string, List<CompoundLayerInfo>)>();

        private (string typeName, string typeMark, List<CompoundLayerInfo> layers) LookupFinishType(
            Document doc, string typeMark, string category)
        {
            string cacheKey = $"{category}:{typeMark}";
            if (_finishTypeCache.ContainsKey(cacheKey))
                return _finishTypeCache[cacheKey];

            try
            {
                IEnumerable<Element> types = null;
                if (category == "Wall")
                    types = new FilteredElementCollector(doc).OfClass(typeof(WallType)).ToElements();
                else if (category == "Floor")
                    types = new FilteredElementCollector(doc).OfClass(typeof(FloorType)).ToElements();
                else if (category == "Ceiling")
                    types = new FilteredElementCollector(doc).OfClass(typeof(CeilingType)).ToElements();

                if (types != null)
                {
                    foreach (var t in types)
                    {
                        string mark = t.get_Parameter(BuiltInParameter.ALL_MODEL_TYPE_MARK)?.AsString();
                        if (string.Equals(mark, typeMark, StringComparison.OrdinalIgnoreCase))
                        {
                            var layers = new List<CompoundLayerInfo>();
                            CompoundStructure cs = null;
                            if (t is WallType wt) cs = wt.GetCompoundStructure();
                            else if (t is FloorType ft) cs = ft.GetCompoundStructure();
                            else if (t is CeilingType ct) cs = ct.GetCompoundStructure();

                            if (cs != null)
                                layers = GetCompoundLayerInfo(doc, cs);

                            Logger.Info($"[RoomSurface] LookupFinishType: found {category} type '{t.Name}' for mark '{typeMark}'");
                            var result = (t.Name, mark, layers);
                            _finishTypeCache[cacheKey] = result;
                            return result;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Info($"[RoomSurface] LookupFinishType error for mark '{typeMark}': {ex.Message}");
            }

            Logger.Info($"[RoomSurface] LookupFinishType: no {category} type found for mark '{typeMark}', using as-is");
            var fallbackResult = (typeMark, typeMark, new List<CompoundLayerInfo>());
            _finishTypeCache[cacheKey] = fallbackResult;
            return fallbackResult;
        }

        /// <summary>
        /// 對缺少粉刷層的表面填入預設粉刷類型
        /// </summary>
        private void ApplyDefaultFinishLayers(
            Document doc,
            ref List<FinishLayerData> floorFinishLayers,
            ref List<FinishLayerData> ceilingFinishLayers,
            ref List<FinishLayerData> wallFinishLayers,
            ref List<FinishLayerData> unassociatedFinishLayers,
            Dictionary<ElementId, HostFaceData> hostAreas,
            double floorArea, double ceilingArea,
            string defaultFloorFinish, string defaultWallFinish, string defaultCeilingFinish)
        {
            // 地板：若無偵測到粉刷層且有預設值
            if ((floorFinishLayers == null || floorFinishLayers.Count == 0) && !string.IsNullOrEmpty(defaultFloorFinish))
            {
                var (typeName, typeMark, layers) = LookupFinishType(doc, defaultFloorFinish, "Floor");
                floorFinishLayers = floorFinishLayers ?? new List<FinishLayerData>();
                floorFinishLayers.Add(new FinishLayerData
                {
                    ElementId = ElementId.InvalidElementId,
                    Category = "Floor",
                    TypeName = typeName,
                    TypeMark = typeMark,
                    Thickness_mm = 0,
                    CoverageArea = Math.Round(floorArea, 2),
                    AreaMethod = "DefaultFill",
                    AssociatedBoundaryId = ElementId.InvalidElementId,
                    CompoundLayers = layers,
                    Element = null
                });
                Logger.Info($"[RoomSurface] DefaultFill: floor → '{typeMark}', area={floorArea:F2}m²");
            }

            // 天花板：若無偵測到粉刷層且有預設值
            if ((ceilingFinishLayers == null || ceilingFinishLayers.Count == 0) && !string.IsNullOrEmpty(defaultCeilingFinish))
            {
                var (typeName, typeMark, layers) = LookupFinishType(doc, defaultCeilingFinish, "Ceiling");
                ceilingFinishLayers = ceilingFinishLayers ?? new List<FinishLayerData>();
                ceilingFinishLayers.Add(new FinishLayerData
                {
                    ElementId = ElementId.InvalidElementId,
                    Category = "Ceiling",
                    TypeName = typeName,
                    TypeMark = typeMark,
                    Thickness_mm = 0,
                    CoverageArea = Math.Round(ceilingArea, 2),
                    AreaMethod = "DefaultFill",
                    AssociatedBoundaryId = ElementId.InvalidElementId,
                    CompoundLayers = layers,
                    Element = null
                });
                Logger.Info($"[RoomSurface] DefaultFill: ceiling → '{typeMark}', area={ceilingArea:F2}m²");
            }

            // 牆面：檢查每面邊界牆是否已有粉刷層
            if (!string.IsNullOrEmpty(defaultWallFinish) && hostAreas != null)
            {
                // 已有粉刷層的邊界牆 ID
                var coveredWallIds = new HashSet<ElementId>();
                if (wallFinishLayers != null)
                    foreach (var wf in wallFinishLayers)
                        if (wf.AssociatedBoundaryId != ElementId.InvalidElementId)
                            coveredWallIds.Add(wf.AssociatedBoundaryId);

                var (typeName, typeMark, layers) = LookupFinishType(doc, defaultWallFinish, "Wall");

                foreach (var kvp in hostAreas)
                {
                    if (coveredWallIds.Contains(kvp.Key)) continue; // 已有粉刷，跳過

                    var hostData = kvp.Value;
                    double netArea = Math.Round(hostData.GrossArea - hostData.OpeningArea, 2);
                    if (netArea <= 0) continue;

                    wallFinishLayers = wallFinishLayers ?? new List<FinishLayerData>();
                    wallFinishLayers.Add(new FinishLayerData
                    {
                        ElementId = ElementId.InvalidElementId,
                        Category = "Wall",
                        TypeName = typeName,
                        TypeMark = typeMark,
                        Thickness_mm = 0,
                        CoverageArea = netArea,
                        AreaMethod = "DefaultFill",
                        AssociatedBoundaryId = kvp.Key,
                        CompoundLayers = layers,
                        Element = null
                    });
                    Logger.Info($"[RoomSurface] DefaultFill: wall {kvp.Key.GetIdValue()} → '{typeMark}', area={netArea}m²");
                }
            }
        }

        /// <summary>
        /// 將粉刷層類型標記寫入房間飾面參數
        /// </summary>
        private void WriteFinishToRoomParameters(Document doc, List<Room> rooms, List<object> roomResults)
        {
            foreach (var room in rooms)
            {
                // 找到對應的 roomResult
                dynamic roomResult = roomResults.FirstOrDefault(r =>
                {
                    dynamic dr = r;
                    return (IdType)dr.ElementId == room.Id.GetIdValue();
                });
                if (roomResult == null) continue;

                // 收集各表面的粉刷層類型標記
                var wallMarks = new List<string>();
                var floorMarks = new List<string>();
                var ceilingMarks = new List<string>();

                // 從 Breakdown 收集牆面粉刷
                if (roomResult.Breakdown != null)
                {
                    foreach (dynamic bd in roomResult.Breakdown)
                    {
                        if (bd.FinishLayers != null)
                        {
                            foreach (dynamic fl in bd.FinishLayers)
                            {
                                string mark = (string)fl.TypeMark;
                                if (!string.IsNullOrEmpty(mark) && !wallMarks.Contains(mark))
                                    wallMarks.Add(mark);
                            }
                        }
                    }
                }

                // 未關聯的牆面粉刷也加入
                if (roomResult.UnassociatedFinishLayers != null)
                {
                    foreach (dynamic fl in roomResult.UnassociatedFinishLayers)
                    {
                        string mark = (string)fl.TypeMark;
                        if (!string.IsNullOrEmpty(mark) && !wallMarks.Contains(mark))
                            wallMarks.Add(mark);
                    }
                }

                // 地板粉刷
                if (roomResult.FloorFinishLayers != null)
                {
                    foreach (dynamic fl in roomResult.FloorFinishLayers)
                    {
                        string mark = (string)fl.TypeMark;
                        if (!string.IsNullOrEmpty(mark) && !floorMarks.Contains(mark))
                            floorMarks.Add(mark);
                    }
                }

                // 天花板粉刷
                if (roomResult.CeilingFinishLayers != null)
                {
                    foreach (dynamic fl in roomResult.CeilingFinishLayers)
                    {
                        string mark = (string)fl.TypeMark;
                        if (!string.IsNullOrEmpty(mark) && !ceilingMarks.Contains(mark))
                            ceilingMarks.Add(mark);
                    }
                }

                // 寫入參數
                if (wallMarks.Count > 0)
                    room.get_Parameter(BuiltInParameter.ROOM_FINISH_WALL)?.Set(string.Join(", ", wallMarks));
                if (floorMarks.Count > 0)
                    room.get_Parameter(BuiltInParameter.ROOM_FINISH_FLOOR)?.Set(string.Join(", ", floorMarks));
                if (ceilingMarks.Count > 0)
                    room.get_Parameter(BuiltInParameter.ROOM_FINISH_CEILING)?.Set(string.Join(", ", ceilingMarks));
            }
        }

        /// <summary>
        /// 建立「各空間粉刷表」明細表
        /// </summary>
        private string CreateFinishSchedule(Document doc)
        {
            const string scheduleName = "各空間粉刷表";

            // 刪除同名明細表
            var existing = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSchedule))
                .Cast<ViewSchedule>()
                .FirstOrDefault(v => v.Name == scheduleName);
            if (existing != null)
                doc.Delete(existing.Id);

            // 建立房間明細表
            var schedule = ViewSchedule.CreateSchedule(doc, new ElementId(BuiltInCategory.OST_Rooms));
            schedule.Name = scheduleName;

            var definition = schedule.Definition;

            // 可用欄位中找到需要的 BuiltInParameter
            var schedulableFields = definition.GetSchedulableFields();

            ScheduleFieldId levelFieldId = null;
            ScheduleFieldId numberFieldId = null;

            // 新增欄位
            var fieldsToAdd = new[]
            {
                BuiltInParameter.ROOM_LEVEL_ID,
                BuiltInParameter.ROOM_NUMBER,
                BuiltInParameter.ROOM_NAME,
                BuiltInParameter.ROOM_FINISH_CEILING,
                BuiltInParameter.ROOM_FINISH_FLOOR,
                BuiltInParameter.ROOM_FINISH_WALL
            };

            foreach (var bip in fieldsToAdd)
            {
                var field = schedulableFields.FirstOrDefault(f =>
                    f.ParameterId != null && f.ParameterId == new ElementId(bip));
                if (field != null)
                {
                    var addedField = definition.AddField(field);
                    if (bip == BuiltInParameter.ROOM_LEVEL_ID)
                        levelFieldId = addedField.FieldId;
                    else if (bip == BuiltInParameter.ROOM_NUMBER)
                        numberFieldId = addedField.FieldId;
                }
            }

            // 排序：樓層遞增 → 房間編號遞增
            if (levelFieldId != null)
            {
                var sortLevel = new ScheduleSortGroupField(levelFieldId, ScheduleSortOrder.Ascending);
                definition.AddSortGroupField(sortLevel);
            }
            if (numberFieldId != null)
            {
                var sortNumber = new ScheduleSortGroupField(numberFieldId, ScheduleSortOrder.Ascending);
                definition.AddSortGroupField(sortNumber);
            }

            return scheduleName;
        }

        /// <summary>
        /// 匯出粉刷面積明細 Excel
        /// </summary>
        private string ExportFinishAreaExcel(Document doc, List<object> roomResults, JObject parameters)
        {
            string outputPath = parameters?["outputPath"]?.Value<string>();
            if (string.IsNullOrEmpty(outputPath))
            {
                string projectDir = string.IsNullOrEmpty(doc.PathName)
                    ? Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
                    : System.IO.Path.GetDirectoryName(doc.PathName);
                outputPath = System.IO.Path.Combine(projectDir,
                    $"粉刷面積明細_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");
            }

            using (var wb = new XLWorkbook())
            {
                var ws = wb.Worksheets.Add("粉刷面積明細");

                // 色彩定義
                var headerBg = XLColor.FromHtml("#4472C4");
                var headerFg = XLColor.White;
                var altRowBg = XLColor.FromHtml("#F2F2F2");

                // 標題行
                string[] headers = {
                    "房間編號", "房間名稱",
                    "地板類型標記", "地板粉刷層", "地板粉刷面積(m²)", "地板總表面積(m²)",
                    "牆面類型標記", "牆面粉刷層", "牆面粉刷面積(m²)", "牆面總表面積(m²)",
                    "天花類型標記", "天花粉刷層", "天花粉刷面積(m²)", "天花總表面積(m²)"
                };

                for (int c = 0; c < headers.Length; c++)
                {
                    var cell = ws.Cell(1, c + 1);
                    cell.Value = headers[c];
                    cell.Style.Font.SetBold().Font.SetFontColor(headerFg);
                    cell.Style.Fill.SetBackgroundColor(headerBg);
                    cell.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
                }

                int row = 2;
                int roomIndex = 0;
                foreach (dynamic roomResult in roomResults)
                {
                    // 收集各類別粉刷層
                    var floorLayers = CollectFinishLayersFromResult(roomResult, "Floor");
                    var wallLayers = CollectFinishLayersFromResult(roomResult, "Wall");
                    var ceilingLayers = CollectFinishLayersFromResult(roomResult, "Ceiling");

                    // 計算此房間需要的行數（取三類中最大的行數）
                    int maxRows = Math.Max(1, Math.Max(floorLayers.Count, Math.Max(wallLayers.Count, ceilingLayers.Count)));

                    int startRow = row;
                    for (int i = 0; i < maxRows; i++)
                    {
                        int currentRow = row + i;

                        // 地板
                        if (i < floorLayers.Count)
                        {
                            ws.Cell(currentRow, 3).Value = floorLayers[i].Item1; // TypeMark
                            ws.Cell(currentRow, 4).Value = floorLayers[i].Item2; // TypeName
                            ws.Cell(currentRow, 5).Value = floorLayers[i].Item3; // 面積
                        }

                        // 牆面
                        if (i < wallLayers.Count)
                        {
                            ws.Cell(currentRow, 7).Value = wallLayers[i].Item1;  // TypeMark
                            ws.Cell(currentRow, 8).Value = wallLayers[i].Item2;  // TypeName
                            ws.Cell(currentRow, 9).Value = wallLayers[i].Item3;  // 面積
                        }

                        // 天花
                        if (i < ceilingLayers.Count)
                        {
                            ws.Cell(currentRow, 11).Value = ceilingLayers[i].Item1; // TypeMark
                            ws.Cell(currentRow, 12).Value = ceilingLayers[i].Item2; // TypeName
                            ws.Cell(currentRow, 13).Value = ceilingLayers[i].Item3; // 面積
                        }
                    }

                    // 合併儲存格：房間編號、名稱、總面積
                    int endRow = startRow + maxRows - 1;

                    ws.Cell(startRow, 1).Value = (string)roomResult.Number;
                    ws.Cell(startRow, 2).Value = (string)roomResult.Name;
                    ws.Cell(startRow, 6).Value = (double)roomResult.FloorArea_m2;
                    ws.Cell(startRow, 10).Value = (double)roomResult.WallNetArea_m2;
                    ws.Cell(startRow, 14).Value = (double)roomResult.CeilingArea_m2;

                    if (maxRows > 1)
                    {
                        ws.Range(startRow, 1, endRow, 1).Merge().Style.Alignment.SetVertical(XLAlignmentVerticalValues.Center);
                        ws.Range(startRow, 2, endRow, 2).Merge().Style.Alignment.SetVertical(XLAlignmentVerticalValues.Center);
                        ws.Range(startRow, 6, endRow, 6).Merge().Style.Alignment.SetVertical(XLAlignmentVerticalValues.Center);
                        ws.Range(startRow, 10, endRow, 10).Merge().Style.Alignment.SetVertical(XLAlignmentVerticalValues.Center);
                        ws.Range(startRow, 14, endRow, 14).Merge().Style.Alignment.SetVertical(XLAlignmentVerticalValues.Center);
                    }

                    // 交替行底色
                    if (roomIndex % 2 == 1)
                    {
                        ws.Range(startRow, 1, endRow, headers.Length).Style.Fill.SetBackgroundColor(altRowBg);
                    }

                    row += maxRows;
                    roomIndex++;
                }

                // 自動欄寬
                ws.Columns().AdjustToContents();

                // 框線
                var usedRange = ws.RangeUsed();
                if (usedRange != null)
                {
                    usedRange.Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin);
                    usedRange.Style.Border.SetInsideBorder(XLBorderStyleValues.Thin);
                }

                wb.SaveAs(outputPath);
            }

            return outputPath;
        }

        /// <summary>
        /// 從 roomResult 收集指定類別的粉刷層列表（名稱, 面積）
        /// </summary>
        private List<Tuple<string, string, double>> CollectFinishLayersFromResult(dynamic roomResult, string category)
        {
            var result = new List<Tuple<string, string, double>>();

            if (category == "Wall")
            {
                // 從 Breakdown 收集
                if (roomResult.Breakdown != null)
                {
                    foreach (dynamic bd in roomResult.Breakdown)
                    {
                        if (bd.FinishLayers != null)
                        {
                            foreach (dynamic fl in bd.FinishLayers)
                            {
                                result.Add(Tuple.Create((string)fl.TypeMark, (string)fl.TypeName, (double)fl.CoverageArea_m2));
                            }
                        }
                    }
                }
                // 加入未關聯粉刷層
                if (roomResult.UnassociatedFinishLayers != null)
                {
                    foreach (dynamic fl in roomResult.UnassociatedFinishLayers)
                    {
                        result.Add(Tuple.Create((string)fl.TypeMark, (string)fl.TypeName, (double)fl.CoverageArea_m2));
                    }
                }
            }
            else if (category == "Floor" && roomResult.FloorFinishLayers != null)
            {
                foreach (dynamic fl in roomResult.FloorFinishLayers)
                {
                    result.Add(Tuple.Create((string)fl.TypeMark, (string)fl.TypeName, (double)fl.CoverageArea_m2));
                }
            }
            else if (category == "Ceiling" && roomResult.CeilingFinishLayers != null)
            {
                foreach (dynamic fl in roomResult.CeilingFinishLayers)
                {
                    result.Add(Tuple.Create((string)fl.TypeMark, (string)fl.TypeName, (double)fl.CoverageArea_m2));
                }
            }

            return result
                .GroupBy(t => new { Mark = t.Item1, Name = t.Item2 })
                .Select(g => Tuple.Create(g.Key.Mark, g.Key.Name, Math.Round(g.Sum(t => t.Item3), 2)))
                .ToList();
        }

        #endregion
    }
}
