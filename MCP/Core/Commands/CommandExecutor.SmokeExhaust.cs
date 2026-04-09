using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
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
    /// 排煙窗法規檢討命令
    /// 來源：PR#12 (@7alexhuang-ux)，經跨版本修正後整合
    /// 法規：建技規§101① + 消防§188
    /// 依賴：ClosedXML (Excel 匯出)
    /// </summary>
    public partial class CommandExecutor
    {
        #region 排煙窗檢討


        /// <summary>
        /// 從族群名稱推斷窗戶開啟方式與有效面積折減係數
        /// </summary>
        private (string operationType, double openingRatio, bool needsConfirm, string note) GetWindowOperationType(string familyName, string typeName)
        {
            string name = (familyName + " " + typeName).ToLower();

            // 固定窗 → 排煙無效
            if (ContainsAny(name, new[] { "fixed", "固定", "picture", "景觀", "fix" }))
                return ("fixed", 0, false, "固定窗：排煙有效面積為 0");

            // 全開型 → 1.0
            if (ContainsAny(name, new[] { "casement", "平開", "側開", "pivot", "樞軸", "中懸", "tilt", "內倒內開", "tiltturn" }))
                return ("casement", 1.0, false, null);

            // 半開型 → 0.5
            if (ContainsAny(name, new[] { "sliding", "橫拉", "推拉", "hung", "上下拉", "單拉", "double hung", "single hung", "doublehung", "singlehung" }))
                return ("sliding", 0.5, false, "橫拉/拉窗：有效面積折減 50%");

            // 外推型 → 0.5（保守）
            if (ContainsAny(name, new[] { "awning", "上懸", "外推", "hopper", "下懸", "projected" }))
                return ("projected", 0.5, false, "外推/懸窗：有效面積折減 50%（保守估計）");

            // 百葉 → 0.5
            if (ContainsAny(name, new[] { "louver", "百葉" }))
                return ("louver", 0.5, false, "百葉窗：有效面積折減 50%");

            // 無法判定
            return ("unknown", 0, true, "無法從族群名稱判定開啟方式，需人工確認");
        }

        private bool ContainsAny(string source, string[] keywords)
        {
            foreach (var kw in keywords)
            {
                if (source.Contains(kw)) return true;
            }
            return false;
        }

        /// <summary>
        /// 取得房間天花板高度（兩種方式）
        /// </summary>
        private double GetCeilingHeight(Document doc, Room room, string source)
        {
            const double FEET_TO_MM = 304.8;

            if (source == "ceiling_element")
            {
                // 方式 B：搜尋 Ceiling 元素
                var ceilings = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_Ceilings)
                    .WhereElementIsNotElementType()
                    .ToList();

                Level roomLevel = doc.GetElement(room.LevelId) as Level;
                if (roomLevel == null) goto fallback;

                XYZ roomCenter = null;
                try
                {
                    // 取得房間中心點
                    BoundingBoxXYZ bb = room.get_BoundingBox(null);
                    if (bb != null)
                    {
                        roomCenter = new XYZ((bb.Min.X + bb.Max.X) / 2, (bb.Min.Y + bb.Max.Y) / 2, bb.Min.Z);
                    }
                }
                catch (Exception) { /* 忽略個別元素處理失敗 */ }

                foreach (var ceilingElem in ceilings)
                {
                    // 檢查天花板是否在同一樓層
                    Parameter levelParam = ceilingElem.get_Parameter(BuiltInParameter.LEVEL_PARAM);
                    if (levelParam == null) continue;
                    ElementId ceilingLevelId = levelParam.AsElementId();
                    if (ceilingLevelId != room.LevelId) continue;

                    // 取得天花板高度偏移
                    Parameter offsetParam = ceilingElem.get_Parameter(BuiltInParameter.CEILING_HEIGHTABOVELEVEL_PARAM);
                    if (offsetParam != null)
                    {
                        double offsetFeet = offsetParam.AsDouble();
                        // 如果有房間中心點，檢查天花板是否在房間範圍內
                        if (roomCenter != null)
                        {
                            BoundingBoxXYZ ceilingBB = ceilingElem.get_BoundingBox(null);
                            if (ceilingBB != null)
                            {
                                if (roomCenter.X >= ceilingBB.Min.X && roomCenter.X <= ceilingBB.Max.X &&
                                    roomCenter.Y >= ceilingBB.Min.Y && roomCenter.Y <= ceilingBB.Max.Y)
                                {
                                    return offsetFeet * FEET_TO_MM;
                                }
                            }
                        }
                        else
                        {
                            return offsetFeet * FEET_TO_MM;
                        }
                    }
                }
            }

            fallback:
            // 方式 A（預設）：讀 Room 的 Upper Limit + Limit Offset
            {
                Parameter upperLimitParam = room.get_Parameter(BuiltInParameter.ROOM_UPPER_LEVEL);
                Parameter upperOffsetParam = room.get_Parameter(BuiltInParameter.ROOM_UPPER_OFFSET);

                Level roomLevel = doc.GetElement(room.LevelId) as Level;
                double roomLevelElevation = roomLevel?.Elevation ?? 0;

                if (upperLimitParam != null && upperOffsetParam != null)
                {
                    ElementId upperLevelId = upperLimitParam.AsElementId();
                    double upperOffset = upperOffsetParam.AsDouble();

                    if (upperLevelId != ElementId.InvalidElementId)
                    {
                        Level upperLevel = doc.GetElement(upperLevelId) as Level;
                        if (upperLevel != null)
                        {
                            double height = (upperLevel.Elevation - roomLevelElevation + upperOffset) * FEET_TO_MM;
                            return height;
                        }
                    }

                    // 如果 Upper Level 就是自身樓層
                    return upperOffset * FEET_TO_MM;
                }

                // 最終 fallback：用 BoundingBox
                BoundingBoxXYZ bb = room.get_BoundingBox(null);
                if (bb != null)
                {
                    return (bb.Max.Z - bb.Min.Z) * FEET_TO_MM;
                }

                return 3000; // 預設 3m
            }
        }

        /// <summary>
        /// 排煙窗檢討（Step 2+5 合併）
        /// 檢查天花板下 80cm 內可開啟窗面積是否 ≥ 區劃面積 2%
        /// 法源：建技規§101① + 消防§188③⑦
        /// </summary>
        private object CheckSmokeExhaustWindows(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;
            string levelName = parameters["levelName"]?.Value<string>();
            string ceilingHeightSource = parameters["ceilingHeightSource"]?.Value<string>() ?? "room_parameter";
            bool colorize = parameters["colorize"]?.Value<bool>() ?? true;
            double smokeZoneHeight = parameters["smokeZoneHeight"]?.Value<double>() ?? 800; // 預設 80cm

            // 非居室排除關鍵字（走廊、樓梯等非居室空間不需檢討排煙）
            string[] defaultExcludeKeywords = { "走廊", "corridor", "hall", "樓梯", "stair", "電梯", "elevator", "lift", "管道", "shaft", "機房", "mechanical", "廁所", "toilet", "restroom", "浴室", "bath", "玄關", "vestibule", "lobby", "陽台", "balcony" };
            var excludeParam = parameters["excludeKeywords"] as JArray;
            string[] excludeKeywords = excludeParam != null
                ? excludeParam.Select(t => t.Value<string>()).ToArray()
                : defaultExcludeKeywords;

            const double FEET_TO_MM = 304.8;
            const double SQ_FEET_TO_SQ_M = 0.092903;

            // 取得樓層
            Level level = FindLevel(doc, levelName, false);
            double levelElevation = level.Elevation;

            // 判定是否為地下室
            bool isBasement = levelElevation < 0;

            // 取得該樓層所有有面積的房間
            var rooms = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType()
                .Cast<Room>()
                .Where(r => r.LevelId == level.Id && r.Area > 0)
                .ToList();

            var roomResults = new List<object>();
            int totalRooms = 0;
            int roomsChecked = 0;
            int roomsCompliant = 0;
            int roomsFailed = 0;
            int roomsSkipped = 0;
            int roomsSkippedNonResidential = 0;
            int roomsNeedConfirm = 0;

            // 收集所有需要上色的窗戶
            var colorizeList = new List<(IdType elementId, string type)>();
            var roomCeilingHeights = new List<double>(); // 收集天花板高度用於畫線
            var allWindowDetails = new List<(IdType id, double areaInZone, bool inZone, double width, double heightInZone)>(); // 所有窗戶的標註資料

            SpatialElementBoundaryOptions boundaryOptions = new SpatialElementBoundaryOptions();

            foreach (Room room in rooms)
            {
                totalRooms++;
                double roomAreaSqM = room.Area * SQ_FEET_TO_SQ_M;

                // 非居室排除
                string roomName = room.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? "";
                string roomNameLower = roomName.ToLower();
                bool isNonResidential = excludeKeywords.Any(kw => roomNameLower.Contains(kw.ToLower()));
                if (isNonResidential)
                {
                    roomsSkippedNonResidential++;
                    continue;
                }

                // 面積 ≤ 50m² 的房間不需檢討（建技規§1第35款第三目）
                if (roomAreaSqM <= 50)
                {
                    roomsSkipped++;
                    continue;
                }

                roomsChecked++;

                // 取得天花板高度
                double ceilingHeight = GetCeilingHeight(doc, room, ceilingHeightSource);
                roomCeilingHeights.Add(ceilingHeight);

                // 計算有效帶範圍（相對於樓層地板）
                double smokeZoneTop = ceilingHeight;
                double smokeZoneBottom = ceilingHeight - smokeZoneHeight;

                // 找出房間邊界牆上的所有窗戶
                var windowResults = new List<object>();
                var processedWindowIds = new HashSet<IdType>();
                bool hasConfirmNeeded = false;

                // 平行追蹤變數（避免 Cast<dynamic>()）
                double sumEffectiveArea = 0;
                int countInSmokeZone = 0;
                int countEffective = 0;
                int countNeedsConfirm = 0;
                // 追蹤固定窗與接近有效帶的窗戶（用於改善建議）
                int fixedWindowsInZoneCount = 0;
                double fixedWindowsInZonePotentialGain = 0;
                int nearZoneWindowsCount = 0;
                // 用於建議的詳細窗戶資訊
                var windowDetailsList = new List<(IdType id, string familyName, string typeName, string opType, double areaInZone, double headHeight, bool inZone, double width, double heightInZone)>();

                IList<IList<BoundarySegment>> segments = room.GetBoundarySegments(boundaryOptions);
                if (segments != null)
                {
                    foreach (IList<BoundarySegment> segmentList in segments)
                    {
                        foreach (BoundarySegment segment in segmentList)
                        {
                            Element element = doc.GetElement(segment.ElementId);
                            if (element is Wall wall)
                            {
                                IList<ElementId> insertIds = wall.FindInserts(true, false, false, false);
                                foreach (ElementId insertId in insertIds)
                                {
                                    if (processedWindowIds.Contains(insertId.GetIdValue())) continue;

                                    Element insert = doc.GetElement(insertId);
                                    if (insert is FamilyInstance fi &&
                                        fi.Category.Id.GetIdValue() == (IdType)(int)BuiltInCategory.OST_Windows)
                                    {
                                        // 確認窗戶屬於此房間（使用與 GetRoomDaylightInfo 相同的邏輯）
                                        bool belongsToRoom = false;
                                        if (wall.Location is LocationCurve wallLocCurve && insert.Location is LocationPoint insertLoc)
                                        {
                                            Curve wallCurve = wallLocCurve.Curve;
                                            Curve segmentCurve = segment.GetCurve();
                                            IntersectionResult resStart = wallCurve.Project(segmentCurve.GetEndPoint(0));
                                            IntersectionResult resEnd = wallCurve.Project(segmentCurve.GetEndPoint(1));
                                            if (resStart != null && resEnd != null)
                                            {
                                                double tMin = Math.Min(resStart.Parameter, resEnd.Parameter);
                                                double tMax = Math.Max(resStart.Parameter, resEnd.Parameter);
                                                IntersectionResult resWindow = wallCurve.Project(insertLoc.Point);
                                                if (resWindow != null)
                                                {
                                                    double tWindow = resWindow.Parameter;
                                                    double tol = 500.0 / 304.8;
                                                    if (tWindow >= tMin - tol && tWindow <= tMax + tol)
                                                        belongsToRoom = true;
                                                }
                                            }
                                            else
                                            {
                                                if (fi.FromRoom != null && fi.FromRoom.Id == room.Id) belongsToRoom = true;
                                                else if (fi.ToRoom != null && fi.ToRoom.Id == room.Id) belongsToRoom = true;
                                            }
                                        }
                                        else
                                        {
                                            if (fi.FromRoom != null && fi.FromRoom.Id == room.Id) belongsToRoom = true;
                                            else if (fi.ToRoom != null && fi.ToRoom.Id == room.Id) belongsToRoom = true;
                                        }

                                        if (!belongsToRoom) continue;
                                        processedWindowIds.Add(insertId.GetIdValue());

                                        // 取得窗戶尺寸
                                        BuiltInParameter[] widthBips = { BuiltInParameter.FAMILY_WIDTH_PARAM, BuiltInParameter.WINDOW_WIDTH };
                                        string[] widthNames = { "粗略寬度", "寬度", "Width", "寬" };
                                        BuiltInParameter[] heightBips = { BuiltInParameter.FAMILY_HEIGHT_PARAM, BuiltInParameter.WINDOW_HEIGHT };
                                        string[] heightNames = { "粗略高度", "高度", "Height", "高" };
                                        BuiltInParameter[] sillBips = { BuiltInParameter.INSTANCE_SILL_HEIGHT_PARAM };
                                        string[] sillNames = { "窗台高度", "Sill Height", "底高度", "窗臺高度" };
                                        BuiltInParameter[] headBips = { BuiltInParameter.INSTANCE_HEAD_HEIGHT_PARAM };
                                        string[] headNames = { "窗頂高度", "Head Height", "頂高度" };

                                        Element symbol = fi.Symbol;
                                        double? wValRaw = GetParamValue(fi, widthBips, widthNames);
                                        if (wValRaw == null || wValRaw == 0)
                                            wValRaw = GetParamValue(symbol, widthBips, widthNames);
                                        double wVal = wValRaw ?? 0;
                                        double width = wVal * FEET_TO_MM;

                                        double? hValRaw = GetParamValue(fi, heightBips, heightNames);
                                        if (hValRaw == null || hValRaw == 0)
                                            hValRaw = GetParamValue(symbol, heightBips, heightNames);
                                        double hVal = hValRaw ?? 0;
                                        double height = hVal * FEET_TO_MM;

                                        double sillHeightRaw = GetParamValue(fi, sillBips, sillNames) ?? 0;
                                        double sillHeight = sillHeightRaw * FEET_TO_MM;

                                        double headHeightRaw = GetParamValue(fi, headBips, headNames) ?? (sillHeightRaw + hVal);
                                        double headHeight = headHeightRaw * FEET_TO_MM;

                                        // 計算窗頂是否進入有效帶
                                        // 窗頂若超過天花板則截斷
                                        double headHeightCapped = Math.Min(headHeight, smokeZoneTop);
                                        bool isInSmokeZone = headHeightCapped > smokeZoneBottom;

                                        double heightInZone = 0;
                                        double areaInZone = 0;
                                        if (isInSmokeZone)
                                        {
                                            double effectiveBottom = Math.Max(sillHeight, smokeZoneBottom);
                                            heightInZone = headHeightCapped - effectiveBottom;
                                            if (heightInZone < 0) heightInZone = 0;
                                            areaInZone = (width / 1000.0) * (heightInZone / 1000.0); // 轉 m²
                                        }

                                        // 從族群名稱判定開啟方式
                                        var (operationType, openingRatio, needsConfirm, note) =
                                            GetWindowOperationType(fi.Symbol.FamilyName, fi.Symbol.Name);

                                        if (needsConfirm) hasConfirmNeeded = true;

                                        double effectiveArea = areaInZone * openingRatio;

                                        // 收集上色資訊
                                        if (colorize)
                                        {
                                            colorizeList.Add((insertId.GetIdValue(), operationType));
                                        }

                                        windowResults.Add(new
                                        {
                                            WindowId = insertId.GetIdValue(),
                                            FamilyName = fi.Symbol.FamilyName,
                                            TypeName = fi.Symbol.Name,
                                            Width = Math.Round(width, 1),
                                            Height = Math.Round(height, 1),
                                            SillHeight = Math.Round(sillHeight, 1),
                                            HeadHeight = Math.Round(headHeight, 1),
                                            HeadHeightCapped = Math.Round(headHeightCapped, 1),
                                            IsInSmokeZone = isInSmokeZone,
                                            HeightInZone = Math.Round(heightInZone, 1),
                                            AreaInZone = Math.Round(areaInZone, 4),
                                            OperationType = operationType,
                                            OperationSource = "familyName",
                                            OpeningRatio = openingRatio,
                                            EffectiveArea = Math.Round(effectiveArea, 4),
                                            NeedsManualConfirm = needsConfirm,
                                            Note = note,
                                            HostWallId = wall.Id.GetIdValue()
                                        });

                                        // 更新平行追蹤變數
                                        sumEffectiveArea += Math.Round(effectiveArea, 4);
                                        if (isInSmokeZone) countInSmokeZone++;
                                        if (Math.Round(effectiveArea, 4) > 0) countEffective++;
                                        if (needsConfirm) countNeedsConfirm++;
                                        // 固定窗（用於改善建議 A）
                                        if (operationType == "fixed" && Math.Round(areaInZone, 4) > 0)
                                        {
                                            fixedWindowsInZoneCount++;
                                            fixedWindowsInZonePotentialGain += Math.Round(areaInZone, 4);
                                        }
                                        // 接近有效帶的窗（用於改善建議 B）
                                        if (!isInSmokeZone && headHeight > smokeZoneBottom - 300)
                                        {
                                            nearZoneWindowsCount++;
                                        }
                                        // 記錄詳細資訊供建議使用
                                        windowDetailsList.Add((insertId.GetIdValue(), fi.Symbol.FamilyName, fi.Symbol.Name, operationType, Math.Round(areaInZone, 4), headHeight, isInSmokeZone, width, heightInZone));
                                        allWindowDetails.Add((insertId.GetIdValue(), Math.Round(areaInZone, 4), isInSmokeZone, width, heightInZone));
                                    }
                                }
                            }
                        }
                    }
                }

                // 計算合規性（使用平行追蹤變數，避免 Cast<dynamic>()）
                double totalEffectiveArea = sumEffectiveArea;
                double requiredArea = roomAreaSqM * 0.02;
                double ratio = roomAreaSqM > 0 ? totalEffectiveArea / roomAreaSqM : 0;
                bool isCompliant = totalEffectiveArea >= requiredArea;
                double deficit = isCompliant ? 0 : requiredArea - totalEffectiveArea;

                // 無窗居室判定（建技規§1第35款第三目）
                bool isWindowlessRoom = totalEffectiveArea < requiredArea;

                // 防煙區劃警告（§101① / §188①）
                bool exceedsCompartment = roomAreaSqM > 500;

                // 改善建議（具體到每扇窗）
                var recommendations = new List<object>();
                if (!isCompliant)
                {
                    double remainingDeficit = deficit;

                    // 建議 A：固定窗改為可開啟窗
                    if (fixedWindowsInZoneCount > 0)
                    {
                        // 查詢專案中可用的 Casement 窗型
                        var availableCasementTypes = new FilteredElementCollector(doc)
                            .OfCategory(BuiltInCategory.OST_Windows)
                            .WhereElementIsElementType()
                            .Cast<FamilySymbol>()
                            .Where(fs => ContainsAny((fs.FamilyName + " " + fs.Name).ToLower(),
                                new[] { "casement", "平開", "側開", "pivot", "樞軸" }))
                            .Select(fs => new { TypeName = fs.FamilyName + ": " + fs.Name, TypeId = fs.Id.GetIdValue() })
                            .Take(5)
                            .ToList();

                        // 列出每扇固定窗的具體資訊
                        var fixedWindowDetails = windowDetailsList
                            .Where(w => w.opType == "fixed" && w.areaInZone > 0)
                            .Select(w => new
                            {
                                WindowId = w.id,
                                CurrentType = w.familyName + ": " + w.typeName,
                                AreaInZone = Math.Round(w.areaInZone, 4),
                                PotentialGain = Math.Round(w.areaInZone, 4) // 從 0 變 1.0
                            })
                            .ToList();

                        double totalPotentialGain = fixedWindowsInZonePotentialGain;
                        remainingDeficit -= totalPotentialGain;

                        recommendations.Add(new
                        {
                            Type = "A",
                            Action = "將固定窗改為可開啟窗",
                            PotentialGain = Math.Round(totalPotentialGain, 2),
                            CanSolve = totalPotentialGain >= deficit,
                            FixedWindows = fixedWindowDetails,
                            AvailableCasementTypes = availableCasementTypes,
                            Note = $"將 {fixedWindowsInZoneCount} 扇固定窗改為 Casement，可補足 +{Math.Round(totalPotentialGain, 2)} m²"
                        });
                    }

                    // 建議 B：接近有效帶的窗戶上移/加高
                    if (nearZoneWindowsCount > 0)
                    {
                        var nearWindowDetails = windowDetailsList
                            .Where(w => !w.inZone && w.headHeight > smokeZoneBottom - 300)
                            .Select(w => new
                            {
                                WindowId = w.id,
                                CurrentType = w.familyName + ": " + w.typeName,
                                HeadHeight = Math.Round(w.headHeight, 1),
                                SmokeZoneBottom = Math.Round(smokeZoneBottom, 1),
                                GapToZone = Math.Round(smokeZoneBottom - w.headHeight, 1),
                                SuggestAction = $"上移 {Math.Round(smokeZoneBottom - w.headHeight, 0)}mm 即可進入有效帶"
                            })
                            .ToList();

                        recommendations.Add(new
                        {
                            Type = "B",
                            Action = "窗戶上移或加高進入有效帶",
                            WindowCount = nearZoneWindowsCount,
                            NearWindows = nearWindowDetails,
                            Note = $"有 {nearZoneWindowsCount} 扇窗接近有效帶（差 30cm 內），上移即可計入排煙面積"
                        });
                    }

                    // 建議 C：新增窗戶
                    if (remainingDeficit > 0 && remainingDeficit <= deficit)
                    {
                        // 查詢專案中所有可開啟窗型及其尺寸
                        var availableTypes = new FilteredElementCollector(doc)
                            .OfCategory(BuiltInCategory.OST_Windows)
                            .WhereElementIsElementType()
                            .Cast<FamilySymbol>()
                            .Where(fs =>
                            {
                                var (opType2, ratio2, _, _) = GetWindowOperationType(fs.FamilyName, fs.Name);
                                return ratio2 > 0;
                            })
                            .Select(fs =>
                            {
                                double? tw = GetParamValue(fs, new[] { BuiltInParameter.FAMILY_WIDTH_PARAM, BuiltInParameter.WINDOW_WIDTH }, new[] { "Width", "寬度" });
                                double? th = GetParamValue(fs, new[] { BuiltInParameter.FAMILY_HEIGHT_PARAM, BuiltInParameter.WINDOW_HEIGHT }, new[] { "Height", "高度" });
                                double wMm = (tw ?? 0) * FEET_TO_MM;
                                double hMm = (th ?? 0) * FEET_TO_MM;
                                var (_, ratioC, _, _) = GetWindowOperationType(fs.FamilyName, fs.Name);
                                return new
                                {
                                    TypeName = fs.FamilyName + ": " + fs.Name,
                                    TypeId = fs.Id.GetIdValue(),
                                    Width = Math.Round(wMm, 0),
                                    Height = Math.Round(hMm, 0),
                                    OpeningRatio = ratioC,
                                    EffectiveAreaPerWindow = Math.Round((wMm / 1000.0) * Math.Min(hMm, smokeZoneHeight) / 1000.0 * ratioC, 4)
                                };
                            })
                            .Where(t => t.EffectiveAreaPerWindow > 0)
                            .OrderByDescending(t => t.EffectiveAreaPerWindow)
                            .Take(5)
                            .ToList();

                        recommendations.Add(new
                        {
                            Type = "C",
                            Action = "新增可開啟窗",
                            RemainingDeficit = Math.Round(Math.Max(remainingDeficit, 0), 2),
                            AvailableWindowTypes = availableTypes,
                            Note = $"於天花板下 80cm 範圍內的外牆空白段新增可開啟窗，需補足 {Math.Round(Math.Max(remainingDeficit, 0), 2)} m²"
                        });
                    }

                    // 建議 D：改採機械排煙
                    if (deficit > totalEffectiveArea * 2 || totalEffectiveArea == 0)
                    {
                        recommendations.Add(new
                        {
                            Type = "D",
                            Action = "改採機械排煙",
                            Deficit = Math.Round(deficit, 2),
                            Note = "缺口過大或完全無排煙窗，建議改採機械排煙（排煙風機 + 風管）"
                        });
                    }
                }

                if (hasConfirmNeeded) roomsNeedConfirm++;
                if (isCompliant) roomsCompliant++;
                else roomsFailed++;

                // roomName 已在迴圈開頭取得

                roomResults.Add(new
                {
                    RoomId = room.Id.GetIdValue(),
                    RoomName = roomName,
                    RoomNumber = room.Number,
                    RoomArea = Math.Round(roomAreaSqM, 2),
                    IsBasement = isBasement,
                    CeilingHeight = Math.Round(ceilingHeight, 1),
                    CeilingHeightSource = ceilingHeightSource,
                    SmokeZoneTop = Math.Round(smokeZoneTop, 1),
                    SmokeZoneBottom = Math.Round(smokeZoneBottom, 1),
                    ExceedsCompartmentThreshold = exceedsCompartment,
                    CompartmentNote = exceedsCompartment ? "房間面積 > 500 m²，須以防煙壁分割為多個區劃" : null,
                    Windows = windowResults,
                    Summary = new
                    {
                        TotalWindowsChecked = windowResults.Count,
                        WindowsInSmokeZone = countInSmokeZone,
                        WindowsEffective = countEffective,
                        NeedsConfirmCount = countNeedsConfirm,
                        TotalEffectiveArea = Math.Round(totalEffectiveArea, 4),
                        RequiredArea = Math.Round(requiredArea, 4),
                        Ratio = Math.Round(ratio, 4),
                        RequiredRatio = 0.02,
                        Deficit = Math.Round(deficit, 4),
                        IsWindowlessRoom = isWindowlessRoom,
                        IsCompliant = isCompliant,
                        Result = isCompliant ? "PASS" : "FAIL",
                        Recommendations = recommendations
                    }
                });
            }

            // 收集所有房間的天花板高度（用於畫線）
            var uniqueCeilingHeights = new HashSet<double>();
            foreach (var rd in roomCeilingHeights)
            {
                uniqueCeilingHeights.Add(rd);
            }

            // 執行視覺化：四向立面複製 + 上色 + 標註
            var createdViewIds = new List<object>();
            IdType? firstCreatedViewId = null;
            if (colorize)
            {
                ElementId solidPatternId = GetSolidFillPatternId(doc);

                // 找出所有立面視圖
                var elevationViews = new FilteredElementCollector(doc)
                    .OfClass(typeof(View))
                    .Cast<View>()
                    .Where(v => v.ViewType == ViewType.Elevation && !v.IsTemplate && v.CanBePrinted)
                    .ToList();

                if (elevationViews.Count == 0)
                {
                    // fallback：找剖面或 3D
                    elevationViews = new FilteredElementCollector(doc)
                        .OfClass(typeof(View))
                        .Cast<View>()
                        .Where(v => (v.ViewType == ViewType.Section || v.ViewType == ViewType.ThreeD) &&
                                    !v.IsTemplate && v.CanBePrinted)
                        .Take(1)
                        .ToList();
                }

                // 取得文字類型
                TextNoteType textType = new FilteredElementCollector(doc)
                    .OfClass(typeof(TextNoteType))
                    .Cast<TextNoteType>()
                    .FirstOrDefault();

                using (Transaction trans = new Transaction(doc, "排煙檢討四向立面"))
                {
                    trans.Start();

                    foreach (View sourceView in elevationViews)
                    {
                        // 複製視圖
                        ElementId newViewId;
                        try
                        {
                            newViewId = sourceView.Duplicate(ViewDuplicateOption.Duplicate);
                        }
                        catch { continue; }

                        View newView = doc.GetElement(newViewId) as View;
                        string timestamp = DateTime.Now.ToString("MMdd_HHmm");
                        try { newView.Name = $"排煙檢討_{sourceView.Name}_{level.Name}_{timestamp}"; }
                        catch { newView.Name = $"排煙檢討_{newViewId.GetIdValue()}_{timestamp}"; }

                        // 1. 上色窗戶
                        foreach (var (elemId, opType) in colorizeList)
                        {
                            OverrideGraphicSettings ogs = new OverrideGraphicSettings();
                            Color color;
                            switch (opType)
                            {
                                case "casement": case "pivot": color = new Color(0, 180, 0); break;
                                case "sliding": case "projected": case "louver": color = new Color(255, 200, 0); break;
                                case "fixed": color = new Color(255, 50, 50); break;
                                default: color = new Color(180, 180, 180); break;
                            }
                            ogs.SetSurfaceForegroundPatternColor(color);
                            if (solidPatternId != ElementId.InvalidElementId)
                            {
                                ogs.SetSurfaceForegroundPatternId(solidPatternId);
                                ogs.SetSurfaceForegroundPatternVisible(true);
                            }
                            ogs.SetProjectionLineColor(color);
                            newView.SetElementOverrides(new ElementId(elemId), ogs);
                        }

                        // 2. 繪製天花板線和有效帶線
                        BoundingBoxXYZ cb = newView.CropBox;
                        if (cb != null)
                        {
                            Transform t = cb.Transform;
                            double leftX = cb.Min.X - 1.0;  // 左右多延伸 1ft
                            double rightX = cb.Max.X + 1.0;

                            foreach (double ceilingMM in uniqueCeilingHeights)
                            {
                                double ceilingFeet = ceilingMM / FEET_TO_MM;
                                double smokeBottomFeet = (ceilingMM - smokeZoneHeight) / FEET_TO_MM;
                                double modelZ_ceiling = levelElevation + ceilingFeet;
                                double modelZ_smokeBottom = levelElevation + smokeBottomFeet;

                                // 將模型 Z 座標轉為視圖局部 Y 座標
                                double localY_ceiling, localY_smokeBottom;
                                if (Math.Abs(t.BasisY.Z) > 0.001)
                                {
                                    localY_ceiling = (modelZ_ceiling - t.Origin.Z) / t.BasisY.Z;
                                    localY_smokeBottom = (modelZ_smokeBottom - t.Origin.Z) / t.BasisY.Z;
                                }
                                else continue;

                                // 天花板線（紅色）
                                try
                                {
                                    XYZ ceilStart = t.OfPoint(new XYZ(leftX, localY_ceiling, 0));
                                    XYZ ceilEnd = t.OfPoint(new XYZ(rightX, localY_ceiling, 0));
                                    if (ceilStart.DistanceTo(ceilEnd) > 0.01)
                                    {
                                        DetailCurve ceilDC = doc.Create.NewDetailCurve(newView,
                                            Line.CreateBound(ceilStart, ceilEnd));
                                        OverrideGraphicSettings lineOgs = new OverrideGraphicSettings();
                                        lineOgs.SetProjectionLineColor(new Color(255, 0, 0));
                                        newView.SetElementOverrides(ceilDC.Id, lineOgs);
                                    }
                                }
                                catch (Exception) { /* 忽略個別元素處理失敗 */ }

                                // 有效帶下緣線（綠色）
                                try
                                {
                                    XYZ smokeStart = t.OfPoint(new XYZ(leftX, localY_smokeBottom, 0));
                                    XYZ smokeEnd = t.OfPoint(new XYZ(rightX, localY_smokeBottom, 0));
                                    if (smokeStart.DistanceTo(smokeEnd) > 0.01)
                                    {
                                        DetailCurve smokeDC = doc.Create.NewDetailCurve(newView,
                                            Line.CreateBound(smokeStart, smokeEnd));
                                        OverrideGraphicSettings lineOgs = new OverrideGraphicSettings();
                                        lineOgs.SetProjectionLineColor(new Color(0, 180, 0));
                                        newView.SetElementOverrides(smokeDC.Id, lineOgs);
                                    }
                                }
                                catch (Exception) { /* 忽略個別元素處理失敗 */ }

                                // 3. 標註文字：右側標示天花板高度和有效帶
                                if (textType != null)
                                {
                                    try
                                    {
                                        double textLocalY = (localY_ceiling + localY_smokeBottom) / 2.0;
                                        XYZ textPos = t.OfPoint(new XYZ(rightX + 0.5, textLocalY, 0));
                                        TextNoteOptions opts = new TextNoteOptions { TypeId = textType.Id };
                                        string annotText = $"天花板 H={ceilingMM}mm\n" +
                                                          $"↕ 有效帶 {smokeZoneHeight}mm\n" +
                                                          $"下緣 H-{smokeZoneHeight}={ceilingMM - smokeZoneHeight}mm";
                                        TextNote.Create(doc, newView.Id, textPos, annotText, opts);
                                    }
                                    catch (Exception) { /* 忽略個別元素處理失敗 */ }
                                }
                            }

                            // 4. 窗戶標註：帶內寬×高
                            XYZ viewDir = t.BasisZ; // 視圖看向的方向
                            foreach (var wd in allWindowDetails)
                            {
                                if (!wd.inZone || wd.areaInZone <= 0) continue;

                                Element winElem = doc.GetElement(new ElementId(wd.id));
                                if (winElem == null) continue;

                                FamilyInstance winFI = winElem as FamilyInstance;
                                if (winFI == null) continue;

                                // 檢查窗戶的宿主牆是否面向此立面視圖
                                Wall hostWall = winFI.Host as Wall;
                                if (hostWall == null) continue;

                                LocationCurve wallLoc = hostWall.Location as LocationCurve;
                                if (wallLoc == null) continue;

                                XYZ wallDirection = (wallLoc.Curve.GetEndPoint(1) - wallLoc.Curve.GetEndPoint(0)).Normalize();
                                XYZ wallNormal = wallDirection.CrossProduct(XYZ.BasisZ).Normalize();

                                // 牆法線與視圖方向的點積 > 0.7 → 窗戶面對此視圖
                                double dot = Math.Abs(wallNormal.DotProduct(viewDir));
                                if (dot < 0.5) continue;

                                // 取得窗戶位置
                                LocationPoint winLoc = winElem.Location as LocationPoint;
                                if (winLoc == null) continue;

                                // 投影到視圖平面
                                XYZ winModelPos = winLoc.Point;
                                double depth = (winModelPos - t.Origin).DotProduct(t.BasisZ);
                                XYZ projected = winModelPos - depth * t.BasisZ;

                                // 標註在窗戶下方
                                XYZ textOffset = new XYZ(0, 0, -0.5); // 下方 0.5ft
                                XYZ textPos = projected + textOffset;

                                if (textType != null)
                                {
                                    try
                                    {
                                        TextNoteOptions opts = new TextNoteOptions
                                        {
                                            TypeId = textType.Id,
                                            HorizontalAlignment = HorizontalTextAlignment.Center
                                        };
                                        // 窗寬 × 帶內高（有效面積）
                                        double winWidthMM = wd.width;
                                        double heightInZoneMM = wd.heightInZone;
                                        string winText = $"帶內 {winWidthMM:F0}×{heightInZoneMM:F0}mm\n" +
                                                        $"={wd.areaInZone:F3}m²";
                                        TextNote.Create(doc, newView.Id, textPos, winText, opts);
                                    }
                                    catch (Exception) { /* 忽略個別元素處理失敗 */ }
                                }
                            }
                        }

                        if (firstCreatedViewId == null) firstCreatedViewId = newViewId.GetIdValue();

                        createdViewIds.Add(new
                        {
                            ViewId = newViewId.GetIdValue(),
                            ViewName = newView.Name,
                            SourceView = sourceView.Name
                        });
                    }

                    trans.Commit();
                }

                // 切換到第一個新建的立面
                if (firstCreatedViewId != null)
                {
                    _uiApp.ActiveUIDocument.ActiveView = doc.GetElement(new ElementId(firstCreatedViewId.Value)) as View;
                }
            }

            return new
            {
                LevelName = level.Name,
                LevelElevation = Math.Round(levelElevation * FEET_TO_MM, 1),
                IsBasement = isBasement,
                CeilingHeightSource = ceilingHeightSource,
                SmokeZoneHeight = smokeZoneHeight,
                AnnotatedViews = createdViewIds,
                LegalBasis = new
                {
                    WindowlessRoom = "建技規§1第35款第三目 + §100②：>50m²居室，天花板下80cm通風面積<2%",
                    SmokeExhaust = "建技規§101① + 消防§188③⑦：排煙口面積≥防煙區劃2%，設於天花板下80cm內",
                    Compartment = "建技規§101① + 消防§188①：每500m²以防煙壁區劃",
                    HorizontalDistance = "消防§188③：任一位置至排煙口水平距離≤30m"
                },
                Rooms = roomResults,
                LevelSummary = new
                {
                    TotalRooms = totalRooms,
                    RoomsSkippedNonResidential = roomsSkippedNonResidential,
                    NonResidentialReason = "非居室空間（走廊、樓梯等）免檢討",
                    RoomsSkippedSmall = roomsSkipped,
                    SmallRoomReason = "面積 ≤ 50 m² 免檢討",
                    RoomsChecked = roomsChecked,
                    RoomsCompliant = roomsCompliant,
                    RoomsFailed = roomsFailed,
                    RoomsNeedConfirm = roomsNeedConfirm
                }
            };
        }

        /// <summary>
        /// 無開口樓層判定（Step 1）
        /// 法源：消防設置標準§4 + §28③
        /// </summary>
        private object CheckFloorEffectiveOpenings(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;
            string levelName = parameters["levelName"]?.Value<string>();
            bool colorize = parameters["colorize"]?.Value<bool>() ?? true;

            const double FEET_TO_MM = 304.8;
            const double SQ_FEET_TO_SQ_M = 0.092903;

            Level level = FindLevel(doc, levelName, false);
            double levelElevation = level.Elevation;

            // 判定樓層數（十層以上或以下有不同標準）
            var allLevels = new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .OrderBy(l => l.Elevation)
                .ToList();
            int floorNumber = allLevels.IndexOf(allLevels.First(l => l.Id == level.Id)) + 1;
            bool isAbove10F = floorNumber > 10;

            // 取得該樓層總地板面積
            var rooms = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType()
                .Cast<Room>()
                .Where(r => r.LevelId == level.Id && r.Area > 0)
                .ToList();
            double totalFloorArea = rooms.Sum(r => r.Area * SQ_FEET_TO_SQ_M);

            // 找出該樓層所有外牆
            var exteriorWalls = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Walls)
                .WhereElementIsNotElementType()
                .Cast<Wall>()
                .Where(w => w.WallType.Function == WallFunction.Exterior)
                .Where(w =>
                {
                    Parameter levelParam = w.get_Parameter(BuiltInParameter.WALL_BASE_CONSTRAINT);
                    return levelParam != null && levelParam.AsElementId() == level.Id;
                })
                .ToList();

            var openingResults = new List<object>();
            var colorizeList = new List<(IdType elementId, bool isEffective)>();
            int largeOpeningCount = 0; // 十層以下：≥1m 圓 或 75cm×120cm 的開口數

            // 平行追蹤變數（避免 Cast<dynamic>()）
            double sumOpeningEffectiveArea = 0;
            int countOpeningEffective = 0;
            int countOpeningNeedsConfirm = 0;

            foreach (Wall wall in exteriorWalls)
            {
                IList<ElementId> insertIds = wall.FindInserts(true, false, false, false);
                foreach (ElementId insertId in insertIds)
                {
                    Element insert = doc.GetElement(insertId);
                    if (insert is FamilyInstance fi &&
                        (fi.Category.Id.GetIdValue() == (IdType)(int)BuiltInCategory.OST_Windows ||
                         fi.Category.Id.GetIdValue() == (IdType)(int)BuiltInCategory.OST_Doors))
                    {
                        Element symbol = fi.Symbol;

                        BuiltInParameter[] widthBips = { BuiltInParameter.FAMILY_WIDTH_PARAM, BuiltInParameter.WINDOW_WIDTH, BuiltInParameter.DOOR_WIDTH };
                        string[] widthNames = { "粗略寬度", "寬度", "Width", "寬" };
                        BuiltInParameter[] heightBips = { BuiltInParameter.FAMILY_HEIGHT_PARAM, BuiltInParameter.WINDOW_HEIGHT, BuiltInParameter.DOOR_HEIGHT };
                        string[] heightNames = { "粗略高度", "高度", "Height", "高" };
                        BuiltInParameter[] sillBips = { BuiltInParameter.INSTANCE_SILL_HEIGHT_PARAM };
                        string[] sillNames = { "窗台高度", "Sill Height", "底高度", "窗臺高度" };

                        double? wValRaw2 = GetParamValue(fi, widthBips, widthNames);
                        if (wValRaw2 == null || wValRaw2 == 0)
                            wValRaw2 = GetParamValue(symbol, widthBips, widthNames);
                        double wVal = wValRaw2 ?? 0;
                        double width = wVal * FEET_TO_MM;

                        double? hValRaw2 = GetParamValue(fi, heightBips, heightNames);
                        if (hValRaw2 == null || hValRaw2 == 0)
                            hValRaw2 = GetParamValue(symbol, heightBips, heightNames);
                        double hVal = hValRaw2 ?? 0;
                        double height = hVal * FEET_TO_MM;

                        double sillHeightRaw = GetParamValue(fi, sillBips, sillNames) ?? 0;
                        double sillHeight = sillHeightRaw * FEET_TO_MM;

                        // 可內接圓直徑 = min(寬, 高)
                        double minDimension = Math.Min(width, height);

                        // 判定條件
                        bool isValidSize = minDimension >= 500; // 可容納直徑 50cm 圓
                        bool isValidHeight = sillHeight <= 1200; // 下緣 ≤ 1.2m

                        // 開啟方式判定
                        var (operationType, _, needsConfirm, note) =
                            GetWindowOperationType(fi.Symbol.FamilyName, fi.Symbol.Name);

                        // Step 1 的判定：固定窗也算有效（可破壞），但加註
                        bool isOpenable = operationType != "unknown";
                        string confirmNote = null;
                        bool needsManualConfirm = false;

                        if (operationType == "fixed")
                        {
                            isOpenable = true; // 固定窗可破壞進入
                            needsManualConfirm = true;
                            confirmNote = "固定窗：需確認為普通玻璃（厚度≤6mm，非強化/膠合），且無鐵窗";
                        }
                        else if (operationType == "unknown")
                        {
                            isOpenable = false;
                            needsManualConfirm = true;
                            confirmNote = "無法判定開啟方式，需人工確認";
                        }

                        bool isEffective = isValidSize && isValidHeight && isOpenable;
                        double openingArea = isEffective ? (width / 1000.0) * (height / 1000.0) : 0;

                        // 十層以下加嚴：檢查是否為大開口
                        bool isLargeOpening = false;
                        if (!isAbove10F && isEffective)
                        {
                            // 直徑 ≥ 1m 或 寬 ≥ 75cm × 高 ≥ 120cm
                            if (minDimension >= 1000 || (width >= 750 && height >= 1200))
                            {
                                isLargeOpening = true;
                                largeOpeningCount++;
                            }
                        }

                        if (colorize)
                        {
                            colorizeList.Add((insertId.GetIdValue(), isEffective));
                        }

                        openingResults.Add(new
                        {
                            ElementId = insertId.GetIdValue(),
                            Category = fi.Category.Name,
                            FamilyName = fi.Symbol.FamilyName,
                            TypeName = fi.Symbol.Name,
                            Width = Math.Round(width, 1),
                            Height = Math.Round(height, 1),
                            SillHeight = Math.Round(sillHeight, 1),
                            MinInscribedCircleDiameter = Math.Round(minDimension, 1),
                            IsValidSize = isValidSize,
                            IsValidHeight = isValidHeight,
                            OperationType = operationType,
                            IsOpenable = isOpenable,
                            IsEffective = isEffective,
                            IsLargeOpening = isLargeOpening,
                            EffectiveArea = Math.Round(openingArea, 4),
                            NeedsManualConfirm = needsManualConfirm,
                            ConfirmNote = confirmNote,
                            HostWallId = wall.Id.GetIdValue()
                        });

                        // 更新平行追蹤變數
                        sumOpeningEffectiveArea += Math.Round(openingArea, 4);
                        if (isEffective) countOpeningEffective++;
                        if (needsManualConfirm) countOpeningNeedsConfirm++;
                    }
                }
            }

            // 計算總有效開口面積（使用平行追蹤變數，避免 Cast<dynamic>()）
            double totalEffectiveArea = sumOpeningEffectiveArea;
            double threshold_1_30 = totalFloorArea / 30.0;
            double ratio = totalFloorArea > 0 ? totalEffectiveArea / totalFloorArea : 0;
            bool isNoOpeningFloor = totalEffectiveArea < threshold_1_30;

            // 十層以下加嚴檢查
            bool meetsLargeOpeningReq = isAbove10F || largeOpeningCount >= 2;

            // 最終判定
            bool finalIsNoOpening = isNoOpeningFloor || (!isAbove10F && !meetsLargeOpeningReq);

            // 後果判定
            var consequences = new List<string>();
            if (finalIsNoOpening)
            {
                consequences.Add("判定為「無開口樓層」");
                if (totalFloorArea >= 1000)
                {
                    consequences.Add("依消防設置標準§28③：樓地板面積 ≥ 1000m² 之無開口樓層，須設排煙設備");
                }
                consequences.Add("依消防設置標準§17：須設自動灑水設備（不受面積限制）");
            }

            // 執行上色
            if (colorize && colorizeList.Count > 0)
            {
                View activeView = _uiApp.ActiveUIDocument.ActiveView;
                ElementId solidPatternId = GetSolidFillPatternId(doc);

                using (Transaction trans = new Transaction(doc, "無開口樓層檢討上色"))
                {
                    trans.Start();
                    foreach (var (elementId, isEffective) in colorizeList)
                    {
                        OverrideGraphicSettings ogs = new OverrideGraphicSettings();
                        Color color = isEffective ? new Color(0, 180, 0) : new Color(255, 50, 50);

                        bool useCut = (activeView.ViewType == ViewType.FloorPlan ||
                                       activeView.ViewType == ViewType.CeilingPlan);
                        if (useCut)
                        {
                            ogs.SetCutForegroundPatternColor(color);
                            if (solidPatternId != ElementId.InvalidElementId)
                            {
                                ogs.SetCutForegroundPatternId(solidPatternId);
                                ogs.SetCutForegroundPatternVisible(true);
                            }
                        }
                        else
                        {
                            ogs.SetSurfaceForegroundPatternColor(color);
                            if (solidPatternId != ElementId.InvalidElementId)
                            {
                                ogs.SetSurfaceForegroundPatternId(solidPatternId);
                                ogs.SetSurfaceForegroundPatternVisible(true);
                            }
                        }
                        ogs.SetProjectionLineColor(color);
                        activeView.SetElementOverrides(new ElementId(elementId), ogs);
                    }
                    trans.Commit();
                }
            }

            return new
            {
                LevelName = level.Name,
                FloorNumber = floorNumber,
                IsAbove10F = isAbove10F,
                TotalFloorArea = Math.Round(totalFloorArea, 2),
                Threshold_1_30 = Math.Round(threshold_1_30, 4),
                LegalBasis = new
                {
                    Definition = "消防設置標準§4：有效開口面積 < 樓地板面積 1/30 → 無開口樓層",
                    SmokeExhaust = "消防設置標準§28③：≥ 1000m² 之無開口樓層須設排煙設備",
                    Sprinkler = "消防設置標準§17：無開口樓層須設自動灑水設備"
                },
                ExteriorOpenings = openingResults,
                Summary = new
                {
                    TotalOpenings = openingResults.Count,
                    EffectiveOpenings = countOpeningEffective,
                    NeedsConfirmCount = countOpeningNeedsConfirm,
                    LargeOpeningCount = largeOpeningCount,
                    LargeOpeningRequired = isAbove10F ? 0 : 2,
                    MeetsLargeOpeningReq = meetsLargeOpeningReq,
                    TotalEffectiveArea = Math.Round(totalEffectiveArea, 4),
                    Ratio = Math.Round(ratio, 6),
                    Threshold = Math.Round(1.0 / 30.0, 6),
                    IsNoOpeningFloor = finalIsNoOpening,
                    Result = finalIsNoOpening ? "FAIL" : "PASS"
                },
                Consequences = consequences
            };
        }

        #endregion

        #region 視覺化工具

        /// <summary>
        /// 建立剖面視圖（用於排煙窗檢討的立面檢視）
        /// </summary>
        private object CreateSectionView(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;
            IdType wallId = parameters["wallId"].Value<IdType>();
            string viewName = parameters["viewName"]?.Value<string>() ?? "排煙檢討剖面";
            double offset = parameters["offset"]?.Value<double>() ?? 1000; // 剖面偏移距離（mm）
            int scale = parameters["scale"]?.Value<int>() ?? 50;

            const double MM_TO_FEET = 1.0 / 304.8;

            Wall wall = doc.GetElement(new ElementId(wallId)) as Wall;
            if (wall == null) throw new Exception($"找不到牆 ID: {wallId}");

            LocationCurve locCurve = wall.Location as LocationCurve;
            if (locCurve == null) throw new Exception("牆沒有位置曲線");

            Curve wallCurve = locCurve.Curve;
            XYZ start = wallCurve.GetEndPoint(0);
            XYZ end = wallCurve.GetEndPoint(1);

            // 牆的方向向量
            XYZ wallDir = (end - start).Normalize();
            // 垂直於牆的方向（朝外）
            XYZ viewDir = wallDir.CrossProduct(XYZ.BasisZ).Normalize();

            // 牆的中點
            XYZ midPoint = (start + end) / 2.0;

            // 剖面框的尺寸
            double wallLength = wallCurve.Length;
            double wallHeight = wall.get_Parameter(BuiltInParameter.WALL_USER_HEIGHT_PARAM)?.AsDouble() ?? (4000 * MM_TO_FEET);

            // 建立 BoundingBoxXYZ 作為剖面範圍
            BoundingBoxXYZ sectionBox = new BoundingBoxXYZ();

            // 建立右手座標系 Transform（Revit API 要求 BasisX × BasisY = BasisZ）
            double offsetFeet = offset * MM_TO_FEET;
            Transform transform = Transform.Identity;
            transform.Origin = midPoint;
            transform.BasisX = wallDir;
            transform.BasisY = XYZ.BasisZ;
            transform.BasisZ = wallDir.CrossProduct(XYZ.BasisZ); // 右手系：垂直於牆面

            sectionBox.Transform = transform;

            // 設定剖面框範圍（局部座標）
            double halfLength = wallLength / 2.0 + 2.0; // 左右多 2 英尺
            double topMargin = 2.0; // 頂部多 2 英尺
            double bottomMargin = 1.0; // 底部多 1 英尺

            sectionBox.Min = new XYZ(-halfLength, -bottomMargin, -offsetFeet);
            sectionBox.Max = new XYZ(halfLength, wallHeight + topMargin, wall.Width + 2.0);

            IdType viewIdResult;
            using (Transaction trans = new Transaction(doc, "建立排煙檢討剖面"))
            {
                trans.Start();

                // 取得剖面視圖的 ViewFamilyType
                ViewFamilyType sectionType = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewFamilyType))
                    .Cast<ViewFamilyType>()
                    .FirstOrDefault(vft => vft.ViewFamily == ViewFamily.Section);

                if (sectionType == null)
                    throw new Exception("找不到剖面視圖類型");

                ViewSection sectionView = ViewSection.CreateSection(doc, sectionType.Id, sectionBox);
                sectionView.Name = viewName;
                sectionView.Scale = scale;

                viewIdResult = sectionView.Id.GetIdValue();

                trans.Commit();
            }

            return new
            {
                ViewId = viewIdResult,
                ViewName = viewName,
                WallId = wallId,
                Scale = scale,
                Message = $"已建立排煙檢討剖面視圖：{viewName}"
            };
        }

        /// <summary>
        /// 在視圖上繪製詳圖線（天花板線、有效帶線等）
        /// </summary>
        private object CreateDetailLines(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;
            IdType viewId = parameters["viewId"].Value<IdType>();
            var linesArray = parameters["lines"] as JArray;
            if (linesArray == null || linesArray.Count == 0)
                throw new Exception("需要提供 lines 陣列");

            const double MM_TO_FEET = 1.0 / 304.8;

            View view = doc.GetElement(new ElementId(viewId)) as View;
            if (view == null) throw new Exception($"找不到視圖 ID: {viewId}");

            var createdLines = new List<object>();

            using (Transaction trans = new Transaction(doc, "繪製詳圖線"))
            {
                trans.Start();

                foreach (JObject lineObj in linesArray)
                {
                    double startX = lineObj["startX"].Value<double>() * MM_TO_FEET;
                    double startY = lineObj["startY"].Value<double>() * MM_TO_FEET;
                    double endX = lineObj["endX"].Value<double>() * MM_TO_FEET;
                    double endY = lineObj["endY"].Value<double>() * MM_TO_FEET;

                    XYZ startPt = new XYZ(startX, startY, 0);
                    XYZ endPt = new XYZ(endX, endY, 0);

                    if (startPt.DistanceTo(endPt) < 0.001) continue;

                    Line line = Line.CreateBound(startPt, endPt);
                    DetailCurve detailLine = doc.Create.NewDetailCurve(view, line);

                    // 設定線條樣式
                    string lineStyle = lineObj["lineStyle"]?.Value<string>();
                    if (!string.IsNullOrEmpty(lineStyle))
                    {
                        var lineStyles = detailLine.GetLineStyleIds();
                        foreach (ElementId styleId in lineStyles)
                        {
                            Element style = doc.GetElement(styleId);
                            if (style != null && style.Name.Contains(lineStyle))
                            {
                                detailLine.LineStyle = style;
                                break;
                            }
                        }
                    }

                    // 設定顏色覆寫
                    if (lineObj["color"] != null)
                    {
                        var colorObj = lineObj["color"];
                        byte r = (byte)colorObj["r"].Value<int>();
                        byte g = (byte)colorObj["g"].Value<int>();
                        byte b = (byte)colorObj["b"].Value<int>();
                        Color color = new Color(r, g, b);

                        OverrideGraphicSettings ogs = new OverrideGraphicSettings();
                        ogs.SetProjectionLineColor(color);
                        view.SetElementOverrides(detailLine.Id, ogs);
                    }

                    createdLines.Add(new
                    {
                        ElementId = detailLine.Id.GetIdValue(),
                        Label = lineObj["label"]?.Value<string>()
                    });
                }

                trans.Commit();
            }

            return new
            {
                ViewId = viewId,
                LinesCreated = createdLines.Count,
                Lines = createdLines
            };
        }

        /// <summary>
        /// 建立填充區域（有效帶範圍的半透明色塊）
        /// </summary>
        private object CreateFilledRegion(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;
            IdType viewId = parameters["viewId"].Value<IdType>();
            var pointsArray = parameters["points"] as JArray;
            if (pointsArray == null || pointsArray.Count < 3)
                throw new Exception("需要至少 3 個點來定義填充區域");

            const double MM_TO_FEET = 1.0 / 304.8;

            View view = doc.GetElement(new ElementId(viewId)) as View;
            if (view == null) throw new Exception($"找不到視圖 ID: {viewId}");

            // 找到填充區域類型
            string regionTypeName = parameters["regionType"]?.Value<string>();
            FilledRegionType regionType = new FilteredElementCollector(doc)
                .OfClass(typeof(FilledRegionType))
                .Cast<FilledRegionType>()
                .FirstOrDefault(frt => !string.IsNullOrEmpty(regionTypeName) ?
                    frt.Name.Contains(regionTypeName) : true);

            if (regionType == null)
                throw new Exception("找不到填充區域類型");

            // 建立邊界曲線
            var curveLoop = new CurveLoop();
            var points = new List<XYZ>();

            foreach (JObject ptObj in pointsArray)
            {
                double x = ptObj["x"].Value<double>() * MM_TO_FEET;
                double y = ptObj["y"].Value<double>() * MM_TO_FEET;
                points.Add(new XYZ(x, y, 0));
            }

            for (int i = 0; i < points.Count; i++)
            {
                int nextIdx = (i + 1) % points.Count;
                if (points[i].DistanceTo(points[nextIdx]) > 0.001)
                {
                    curveLoop.Append(Line.CreateBound(points[i], points[nextIdx]));
                }
            }

            IdType regionId;
            using (Transaction trans = new Transaction(doc, "建立填充區域"))
            {
                trans.Start();

                FilledRegion filledRegion = FilledRegion.Create(
                    doc, regionType.Id, view.Id,
                    new List<CurveLoop> { curveLoop });

                regionId = filledRegion.Id.GetIdValue();

                // 設定顏色覆寫
                if (parameters["color"] != null)
                {
                    var colorObj = parameters["color"];
                    byte r = (byte)colorObj["r"].Value<int>();
                    byte g = (byte)colorObj["g"].Value<int>();
                    byte b = (byte)colorObj["b"].Value<int>();
                    Color color = new Color(r, g, b);

                    OverrideGraphicSettings ogs = new OverrideGraphicSettings();
                    ogs.SetSurfaceForegroundPatternColor(color);
                    ogs.SetSurfaceTransparency(parameters["transparency"]?.Value<int>() ?? 50);
                    view.SetElementOverrides(filledRegion.Id, ogs);
                }

                trans.Commit();
            }

            return new
            {
                ElementId = regionId,
                ViewId = viewId,
                Message = "填充區域已建立"
            };
        }

        /// <summary>
        /// 在視圖上建立文字標註
        /// </summary>
        private object CreateTextNote(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;
            IdType viewId = parameters["viewId"].Value<IdType>();
            double x = parameters["x"].Value<double>() / 304.8; // mm to feet
            double y = parameters["y"].Value<double>() / 304.8;
            string text = parameters["text"].Value<string>();
            double textSize = parameters["textSize"]?.Value<double>() ?? 2.5; // mm

            View view = doc.GetElement(new ElementId(viewId)) as View;
            if (view == null) throw new Exception($"找不到視圖 ID: {viewId}");

            // 取得或建立文字類型
            TextNoteType textType = new FilteredElementCollector(doc)
                .OfClass(typeof(TextNoteType))
                .Cast<TextNoteType>()
                .FirstOrDefault();

            if (textType == null)
                throw new Exception("找不到文字標註類型");

            IdType textNoteId;
            using (Transaction trans = new Transaction(doc, "建立文字標註"))
            {
                trans.Start();

                TextNoteOptions options = new TextNoteOptions
                {
                    TypeId = textType.Id,
                    HorizontalAlignment = HorizontalTextAlignment.Left
                };

                TextNote textNote = TextNote.Create(doc, view.Id, new XYZ(x, y, 0), text, options);
                textNoteId = textNote.Id.GetIdValue();

                trans.Commit();
            }

            return new
            {
                ElementId = textNoteId,
                ViewId = viewId,
                Text = text
            };
        }

        #endregion

        #region §101 補充法規檢討

        /// <summary>
        /// §101 補充法規檢討：排風量提醒 + 中央管理室偵測
        /// 排風量：靜態提醒（MEP 設備無法自動偵測）
        /// 中央管理室：半自動偵測（建築高度 > 30m 或地下面積 > 1000m²）
        /// </summary>
        private object CheckSupplementaryRegulations(Document doc)
        {
            const double FEET_TO_M = 0.3048;
            const double SQ_FEET_TO_SQ_M = 0.092903;

            // 取得所有樓層
            var allLevels = new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .OrderBy(l => l.Elevation)
                .ToList();

            if (allLevels.Count == 0)
            {
                return new
                {
                    BuildingHeight = 0.0,
                    BasementTotalArea = 0.0,
                    RequiresCentralControl = false,
                    TriggerReason = (string)null,
                    CentralControlRoom = (object)null,
                    ExhaustFanReminder = "§101：排風機排風量 ≥ 120 m³/min，須隨排煙口自動啟動（需人工確認）",
                    Items = new List<object>()
                };
            }

            // 1. 計算建築物高度
            double minElevation = allLevels.First().Elevation * FEET_TO_M;
            double maxElevation = allLevels.Last().Elevation * FEET_TO_M;
            double buildingHeight = maxElevation - minElevation;

            // 2. 計算地下層總面積（Elevation < 0 的樓層）
            var basementLevels = allLevels.Where(l => l.Elevation < 0).ToList();
            double basementTotalArea = 0;
            var basementDetails = new List<object>();

            foreach (var bLevel in basementLevels)
            {
                var bRooms = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_Rooms)
                    .WhereElementIsNotElementType()
                    .Cast<Room>()
                    .Where(r => r.LevelId == bLevel.Id && r.Area > 0)
                    .ToList();
                double levelArea = bRooms.Sum(r => r.Area * SQ_FEET_TO_SQ_M);
                basementTotalArea += levelArea;
                basementDetails.Add(new
                {
                    LevelName = bLevel.Name,
                    Elevation = Math.Round(bLevel.Elevation * FEET_TO_M, 2),
                    Area = Math.Round(levelArea, 2),
                    RoomCount = bRooms.Count
                });
            }

            // 3. 判斷是否觸發中央監控要求
            bool requiresCentralControl = buildingHeight > 30.0 || basementTotalArea > 1000.0;
            string triggerReason = null;
            if (buildingHeight > 30.0 && basementTotalArea > 1000.0)
                triggerReason = $"建築高度 {buildingHeight:F1}m > 30m 且地下面積 {basementTotalArea:F1}m² > 1000m²";
            else if (buildingHeight > 30.0)
                triggerReason = $"建築高度 {buildingHeight:F1}m > 30m";
            else if (basementTotalArea > 1000.0)
                triggerReason = $"地下層面積 {basementTotalArea:F1}m² > 1000m²";

            // 4. 搜尋中央管理室
            object centralControlRoom = null;
            string centralControlResult = "N/A";
            if (requiresCentralControl)
            {
                string[] keywords = { "中央管理", "防災中心", "中控", "監控室", "中央監控", "管理室" };
                var allRooms = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_Rooms)
                    .WhereElementIsNotElementType()
                    .Cast<Room>()
                    .Where(r => r.Area > 0)
                    .ToList();

                Room foundRoom = allRooms.FirstOrDefault(r =>
                {
                    string roomName = r.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? "";
                    return keywords.Any(kw => roomName.Contains(kw));
                });

                if (foundRoom != null)
                {
                    Level roomLevel = doc.GetElement(foundRoom.LevelId) as Level;
                    centralControlRoom = new
                    {
                        RoomId = foundRoom.Id.GetIdValue(),
                        RoomName = foundRoom.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString(),
                        RoomNumber = foundRoom.Number,
                        LevelName = roomLevel?.Name ?? "未知",
                        Area = Math.Round(foundRoom.Area * SQ_FEET_TO_SQ_M, 2)
                    };
                    centralControlResult = "PASS";
                }
                else
                {
                    centralControlResult = "WARNING";
                }
            }

            // 5. 組合檢討項目
            var items = new List<object>
            {
                new
                {
                    Item = "排風量 ≥ 120 m³/min",
                    LegalBasis = "建技規§101：排風機應隨排煙口之開啟而自動操作，排風量不得小於每分鐘 120 m³",
                    TriggerCondition = "設有排煙設備時",
                    Result = "MANUAL",
                    Note = "MEP 設備需人工確認：(1) 排風機是否連動排煙口自動啟動 (2) 排風量是否 ≥ 120 m³/min"
                },
                new
                {
                    Item = "中央管理室",
                    LegalBasis = "建技規§101：建築物高度 > 30m 或地下層面積 > 1000m²，排煙設備控制應設於中央管理室",
                    TriggerCondition = requiresCentralControl ? triggerReason : "未達門檻（高度 ≤ 30m 且地下面積 ≤ 1000m²）",
                    Result = centralControlResult,
                    Note = centralControlResult == "PASS"
                        ? "已偵測到中央管理室"
                        : centralControlResult == "WARNING"
                            ? "⚠ 未偵測到中央管理室，依§101 應設置排煙控制中央管理室"
                            : "未達設置門檻"
                }
            };

            return new
            {
                BuildingHeight = Math.Round(buildingHeight, 1),
                MaxElevation = Math.Round(maxElevation, 1),
                MinElevation = Math.Round(minElevation, 1),
                TotalLevels = allLevels.Count,
                BasementLevels = basementDetails,
                BasementTotalArea = Math.Round(basementTotalArea, 2),
                RequiresCentralControl = requiresCentralControl,
                TriggerReason = triggerReason,
                CentralControlRoom = centralControlRoom,
                CentralControlResult = centralControlResult,
                ExhaustFanReminder = "§101：排風機排風量 ≥ 120 m³/min，須隨排煙口自動啟動（需人工確認）",
                Items = items
            };
        }

        #endregion

        #region Excel 匯出

        /// <summary>
        /// 匯出排煙窗檢討結果為 Excel (.xlsx)
        /// 使用 ClosedXML，多工作表 + 淺底色 + 改善建議
        /// </summary>
        private object ExportSmokeReviewExcel(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;
            string levelName = parameters["levelName"]?.Value<string>();
            string ceilingHeightSource = parameters["ceilingHeightSource"]?.Value<string>() ?? "room_parameter";
            string outputPath = parameters["outputPath"]?.Value<string>();

            if (string.IsNullOrEmpty(outputPath))
            {
                string projectPath = doc.PathName;
                string projectDir = string.IsNullOrEmpty(projectPath) ?
                    Environment.GetFolderPath(Environment.SpecialFolder.Desktop) :
                    System.IO.Path.GetDirectoryName(projectPath);
                outputPath = System.IO.Path.Combine(projectDir,
                    $"排煙窗檢討_{levelName ?? "全部"}_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");
            }

            // 執行檢討
            var checkParams = new JObject { ["levelName"] = levelName, ["ceilingHeightSource"] = ceilingHeightSource, ["colorize"] = false };
            var checkResult = JObject.FromObject(CheckSmokeExhaustWindows(checkParams));

            var floorParams = new JObject { ["levelName"] = levelName, ["colorize"] = false };
            var floorResult = JObject.FromObject(CheckFloorEffectiveOpenings(floorParams));

            using (var wb = new ClosedXML.Excel.XLWorkbook())
            {
                // 色彩定義
                var headerBg = ClosedXML.Excel.XLColor.FromHtml("#4472C4");     // 深藍
                var headerFg = ClosedXML.Excel.XLColor.White;
                var passBg = ClosedXML.Excel.XLColor.FromHtml("#E2EFDA");       // 淺綠
                var failBg = ClosedXML.Excel.XLColor.FromHtml("#FCE4EC");       // 淺紅
                var altRowBg = ClosedXML.Excel.XLColor.FromHtml("#F2F2F2");     // 淺灰交替行
                var warnBg = ClosedXML.Excel.XLColor.FromHtml("#FFF3E0");       // 淺橘

                // ===== Sheet 1：樓層總覽 =====
                var ws1 = wb.Worksheets.Add("樓層總覽");
                ws1.Cell(1, 1).Value = $"排煙窗檢討報告 — {levelName}";
                ws1.Range(1, 1, 1, 6).Merge().Style.Font.SetBold().Font.SetFontSize(14).Alignment.SetHorizontal(ClosedXML.Excel.XLAlignmentHorizontalValues.Center);
                ws1.Cell(2, 1).Value = $"產出時間：{DateTime.Now:yyyy/MM/dd HH:mm}";
                ws1.Range(2, 1, 2, 6).Merge().Style.Font.SetFontColor(ClosedXML.Excel.XLColor.Gray);

                int r = 4;
                string[] h1 = { "樓層", "樓地板面積(m²)", "有效開口面積(m²)", "開口比值", "1/30門檻(m²)", "無開口樓層判定" };
                for (int c = 0; c < h1.Length; c++)
                {
                    ws1.Cell(r, c + 1).Value = h1[c];
                    ws1.Cell(r, c + 1).Style.Fill.SetBackgroundColor(headerBg).Font.SetFontColor(headerFg).Font.SetBold();
                }
                r++;
                ws1.Cell(r, 1).Value = floorResult["LevelName"]?.ToString();
                ws1.Cell(r, 2).Value = (double)floorResult["TotalFloorArea"];
                ws1.Cell(r, 3).Value = (double)floorResult["Summary"]["TotalEffectiveArea"];
                ws1.Cell(r, 4).Value = (double)floorResult["Summary"]["Ratio"];
                ws1.Cell(r, 4).Style.NumberFormat.Format = "0.0000%";
                ws1.Cell(r, 5).Value = (double)floorResult["Threshold_1_30"];
                string floorResultStr = floorResult["Summary"]["Result"]?.ToString();
                ws1.Cell(r, 6).Value = floorResultStr;
                ws1.Cell(r, 6).Style.Fill.SetBackgroundColor(floorResultStr == "PASS" ? passBg : failBg);

                // 法規依據
                r += 2;
                ws1.Cell(r, 1).Value = "法規依據";
                ws1.Cell(r, 1).Style.Font.SetBold();
                r++;
                ws1.Cell(r, 1).Value = "無開口樓層：消防設置標準§4（有效開口 < 1/30）";
                r++;
                ws1.Cell(r, 1).Value = "排煙觸發：消防設置標準§28③（≥1000m² 無開口樓層須設排煙）";
                r++;
                ws1.Cell(r, 1).Value = "排煙窗：建技規§101① + 消防§188（天花板下80cm，≥區劃面積2%）";
                r++;
                ws1.Cell(r, 1).Value = "無窗居室：建技規§1第35款第三目（>50m²居室，通風面積<2%）";

                ws1.Columns().AdjustToContents();

                // ===== Sheet 2：房間排煙檢討 =====
                var ws2 = wb.Worksheets.Add("房間檢討明細");
                string[] h2 = { "房間名稱", "編號", "面積(m²)", "天花板高(mm)", "有效帶頂(mm)", "有效帶底(mm)",
                                "有效排煙面積(m²)", "需求面積(m²)", "比值", "判定", "無窗居室", "防煙區劃警告", "改善建議" };
                for (int c = 0; c < h2.Length; c++)
                {
                    ws2.Cell(1, c + 1).Value = h2[c];
                    ws2.Cell(1, c + 1).Style.Fill.SetBackgroundColor(headerBg).Font.SetFontColor(headerFg).Font.SetBold();
                }

                r = 2;
                foreach (JToken room in (JArray)checkResult["Rooms"])
                {
                    bool isAltRow = (r % 2 == 0);
                    string result = room["Summary"]["Result"]?.ToString();
                    bool isFail = result == "FAIL";

                    ws2.Cell(r, 1).Value = room["RoomName"]?.ToString();
                    ws2.Cell(r, 2).Value = room["RoomNumber"]?.ToString();
                    ws2.Cell(r, 3).Value = (double)room["RoomArea"];
                    ws2.Cell(r, 4).Value = (double)room["CeilingHeight"];
                    ws2.Cell(r, 5).Value = (double)room["SmokeZoneTop"];
                    ws2.Cell(r, 6).Value = (double)room["SmokeZoneBottom"];
                    ws2.Cell(r, 7).Value = (double)room["Summary"]["TotalEffectiveArea"];
                    ws2.Cell(r, 8).Value = (double)room["Summary"]["RequiredArea"];
                    ws2.Cell(r, 9).Value = (double)room["Summary"]["Ratio"];
                    ws2.Cell(r, 9).Style.NumberFormat.Format = "0.00%";
                    ws2.Cell(r, 10).Value = result;
                    ws2.Cell(r, 11).Value = (bool)room["Summary"]["IsWindowlessRoom"] ? "是" : "否";
                    ws2.Cell(r, 12).Value = (bool)room["ExceedsCompartmentThreshold"] ? "⚠ >500m²" : "";

                    // 改善建議：直接寫入，每個建議換行
                    var recs = (JArray)room["Summary"]["Recommendations"];
                    if (recs != null && recs.Count > 0)
                    {
                        var recTexts = new List<string>();
                        foreach (JToken rec in recs)
                        {
                            string note = rec["Note"]?.ToString();
                            if (!string.IsNullOrEmpty(note)) recTexts.Add(note);
                            // 列出可用窗型
                            var availTypes = rec["AvailableWindowTypes"] as JArray ?? rec["AvailableCasementTypes"] as JArray;
                            if (availTypes != null)
                            {
                                foreach (JToken at in availTypes)
                                {
                                    string typeLine = $"  → {at["TypeName"]} (每扇可補 {at["EffectiveAreaPerWindow"] ?? at["PotentialGain"]} m²)";
                                    recTexts.Add(typeLine);
                                }
                            }
                            // 列出固定窗
                            var fixedWins = rec["FixedWindows"] as JArray;
                            if (fixedWins != null)
                            {
                                foreach (JToken fw in fixedWins)
                                {
                                    recTexts.Add($"  → 窗ID {fw["WindowId"]}：{fw["CurrentType"]}，帶內 {fw["AreaInZone"]} m²");
                                }
                            }
                        }
                        ws2.Cell(r, 13).Value = string.Join("\n", recTexts);
                        ws2.Cell(r, 13).Style.Alignment.SetWrapText(true);
                    }

                    // 行底色
                    var rowBg = isFail ? failBg : (isAltRow ? altRowBg : ClosedXML.Excel.XLColor.NoColor);
                    if (rowBg != ClosedXML.Excel.XLColor.NoColor)
                    {
                        ws2.Range(r, 1, r, h2.Length).Style.Fill.SetBackgroundColor(rowBg);
                    }
                    // 判定欄加強底色
                    ws2.Cell(r, 10).Style.Fill.SetBackgroundColor(isFail ? failBg : passBg);
                    if ((bool)room["ExceedsCompartmentThreshold"])
                    {
                        ws2.Cell(r, 12).Style.Fill.SetBackgroundColor(warnBg);
                    }

                    r++;
                }
                ws2.Columns().AdjustToContents();
                ws2.Column(13).Width = 50; // 建議欄加寬

                // ===== Sheet 3：窗戶明細 =====
                var ws3 = wb.Worksheets.Add("窗戶明細");
                string[] h3 = { "房間", "窗戶ID", "族群名稱", "類型名稱", "寬(mm)", "高(mm)",
                                "窗台高(mm)", "窗頂高(mm)", "在有效帶內", "帶內高度(mm)",
                                "帶內面積(m²)", "開啟方式", "折減係數", "有效面積(m²)", "需人工確認", "備註" };
                for (int c = 0; c < h3.Length; c++)
                {
                    ws3.Cell(1, c + 1).Value = h3[c];
                    ws3.Cell(1, c + 1).Style.Fill.SetBackgroundColor(headerBg).Font.SetFontColor(headerFg).Font.SetBold();
                }

                r = 2;
                foreach (JToken room in (JArray)checkResult["Rooms"])
                {
                    foreach (JToken w in (JArray)room["Windows"])
                    {
                        bool isAltRow = (r % 2 == 0);
                        string opType = w["OperationType"]?.ToString();

                        ws3.Cell(r, 1).Value = room["RoomName"]?.ToString();
                        ws3.Cell(r, 2).Value = (int)w["WindowId"];
                        ws3.Cell(r, 3).Value = w["FamilyName"]?.ToString();
                        ws3.Cell(r, 4).Value = w["TypeName"]?.ToString();
                        ws3.Cell(r, 5).Value = (double)w["Width"];
                        ws3.Cell(r, 6).Value = (double)w["Height"];
                        ws3.Cell(r, 7).Value = (double)w["SillHeight"];
                        ws3.Cell(r, 8).Value = (double)w["HeadHeight"];
                        ws3.Cell(r, 9).Value = (bool)w["IsInSmokeZone"] ? "是" : "否";
                        ws3.Cell(r, 10).Value = (double)w["HeightInZone"];
                        ws3.Cell(r, 11).Value = (double)w["AreaInZone"];
                        ws3.Cell(r, 12).Value = opType;
                        ws3.Cell(r, 13).Value = (double)w["OpeningRatio"];
                        ws3.Cell(r, 14).Value = (double)w["EffectiveArea"];
                        ws3.Cell(r, 15).Value = (bool)w["NeedsManualConfirm"] ? "⚠ 需確認" : "";
                        string noteStr = w["Note"]?.Type == JTokenType.Null ? "" : (w["Note"]?.ToString() ?? "");
                        ws3.Cell(r, 16).Value = noteStr;

                        // 開啟方式底色
                        ClosedXML.Excel.XLColor opBg;
                        switch (opType)
                        {
                            case "casement": case "pivot": opBg = passBg; break;
                            case "sliding": case "projected": case "louver": opBg = warnBg; break;
                            case "fixed": opBg = failBg; break;
                            default: opBg = ClosedXML.Excel.XLColor.FromHtml("#E0E0E0"); break;
                        }
                        ws3.Cell(r, 12).Style.Fill.SetBackgroundColor(opBg);
                        ws3.Cell(r, 14).Style.Fill.SetBackgroundColor((double)w["EffectiveArea"] > 0 ? passBg : failBg);

                        // 交替行底色
                        if (isAltRow)
                        {
                            ws3.Range(r, 1, r, h3.Length).Style.Fill.SetBackgroundColor(altRowBg);
                            // 重新套用特殊欄底色
                            ws3.Cell(r, 12).Style.Fill.SetBackgroundColor(opBg);
                            ws3.Cell(r, 14).Style.Fill.SetBackgroundColor((double)w["EffectiveArea"] > 0 ? passBg : failBg);
                        }

                        r++;
                    }
                }
                ws3.Columns().AdjustToContents();

                // ===== Sheet 4：改善建議 =====
                var ws4 = wb.Worksheets.Add("改善建議");
                string[] h4 = { "房間", "面積(m²)", "缺口(m²)", "建議類型", "建議說明", "具體標的", "可補面積(m²)" };
                for (int c = 0; c < h4.Length; c++)
                {
                    ws4.Cell(1, c + 1).Value = h4[c];
                    ws4.Cell(1, c + 1).Style.Fill.SetBackgroundColor(headerBg).Font.SetFontColor(headerFg).Font.SetBold();
                }

                r = 2;
                foreach (JToken room in (JArray)checkResult["Rooms"])
                {
                    if (room["Summary"]["Result"]?.ToString() != "FAIL") continue;

                    string roomName = room["RoomName"]?.ToString();
                    double roomArea = (double)room["RoomArea"];
                    double deficitVal = (double)room["Summary"]["Deficit"];

                    foreach (JToken rec in (JArray)room["Summary"]["Recommendations"])
                    {
                        string recType = rec["Type"]?.ToString() ?? "";
                        string recNote = rec["Note"]?.ToString() ?? rec["Action"]?.ToString() ?? "";

                        // 固定窗明細
                        var fixedWins = rec["FixedWindows"] as JArray;
                        var availTypes = rec["AvailableWindowTypes"] as JArray ?? rec["AvailableCasementTypes"] as JArray;
                        var nearWins = rec["NearWindows"] as JArray;

                        if (fixedWins != null && fixedWins.Count > 0)
                        {
                            foreach (JToken fw in fixedWins)
                            {
                                ws4.Cell(r, 1).Value = roomName;
                                ws4.Cell(r, 2).Value = roomArea;
                                ws4.Cell(r, 3).Value = deficitVal;
                                ws4.Cell(r, 4).Value = recType;
                                ws4.Cell(r, 5).Value = "固定窗改為可開啟窗";
                                ws4.Cell(r, 6).Value = $"窗ID {fw["WindowId"]}：{fw["CurrentType"]}";
                                ws4.Cell(r, 7).Value = (double)fw["PotentialGain"];
                                ws4.Range(r, 1, r, 7).Style.Fill.SetBackgroundColor(warnBg);
                                r++;
                            }
                        }
                        else if (availTypes != null && availTypes.Count > 0)
                        {
                            foreach (JToken at in availTypes)
                            {
                                ws4.Cell(r, 1).Value = roomName;
                                ws4.Cell(r, 2).Value = roomArea;
                                ws4.Cell(r, 3).Value = deficitVal;
                                ws4.Cell(r, 4).Value = recType;
                                ws4.Cell(r, 5).Value = recNote;
                                ws4.Cell(r, 6).Value = at["TypeName"]?.ToString();
                                double epa = 0;
                                if (at["EffectiveAreaPerWindow"] != null) epa = (double)at["EffectiveAreaPerWindow"];
                                ws4.Cell(r, 7).Value = epa;
                                if (r % 2 == 0) ws4.Range(r, 1, r, 7).Style.Fill.SetBackgroundColor(altRowBg);
                                r++;
                            }
                        }
                        else if (nearWins != null && nearWins.Count > 0)
                        {
                            foreach (JToken nw in nearWins)
                            {
                                ws4.Cell(r, 1).Value = roomName;
                                ws4.Cell(r, 2).Value = roomArea;
                                ws4.Cell(r, 3).Value = deficitVal;
                                ws4.Cell(r, 4).Value = recType;
                                ws4.Cell(r, 5).Value = nw["SuggestAction"]?.ToString();
                                ws4.Cell(r, 6).Value = $"窗ID {nw["WindowId"]}：距有效帶 {nw["GapToZone"]}mm";
                                ws4.Cell(r, 7).Value = "";
                                ws4.Range(r, 1, r, 7).Style.Fill.SetBackgroundColor(warnBg);
                                r++;
                            }
                        }
                        else
                        {
                            // 一般建議（如機械排煙）
                            ws4.Cell(r, 1).Value = roomName;
                            ws4.Cell(r, 2).Value = roomArea;
                            ws4.Cell(r, 3).Value = deficitVal;
                            ws4.Cell(r, 4).Value = recType;
                            ws4.Cell(r, 5).Value = recNote;
                            ws4.Cell(r, 6).Value = "";
                            ws4.Cell(r, 7).Value = "";
                            ws4.Range(r, 1, r, 7).Style.Fill.SetBackgroundColor(failBg);
                            r++;
                        }
                    }
                }
                ws4.Columns().AdjustToContents();
                ws4.Column(5).Width = 45;
                ws4.Column(6).Width = 35;

                // ===== Sheet 5：§101 補充法規檢討 =====
                var ws5 = wb.Worksheets.Add("§101補充檢討");
                var supplementary = JObject.FromObject(CheckSupplementaryRegulations(doc));

                ws5.Cell(1, 1).Value = "§101 補充法規檢討 — 排風量・中央管理室";
                ws5.Range(1, 1, 1, 5).Merge().Style.Font.SetBold().Font.SetFontSize(14)
                    .Alignment.SetHorizontal(ClosedXML.Excel.XLAlignmentHorizontalValues.Center);
                ws5.Cell(2, 1).Value = $"產出時間：{DateTime.Now:yyyy/MM/dd HH:mm}";
                ws5.Range(2, 1, 2, 5).Merge().Style.Font.SetFontColor(ClosedXML.Excel.XLColor.Gray);

                // 建築物資訊
                r = 4;
                ws5.Cell(r, 1).Value = "建築物資訊";
                ws5.Cell(r, 1).Style.Font.SetBold().Font.SetFontSize(12);
                r++;
                ws5.Cell(r, 1).Value = "建築物高度";
                ws5.Cell(r, 2).Value = $"{supplementary["BuildingHeight"]} m";
                ws5.Cell(r, 3).Value = (double)supplementary["BuildingHeight"] > 30 ? "⚠ 超過 30m" : "≤ 30m";
                r++;
                ws5.Cell(r, 1).Value = "地下層總面積";
                ws5.Cell(r, 2).Value = $"{supplementary["BasementTotalArea"]} m²";
                ws5.Cell(r, 3).Value = (double)supplementary["BasementTotalArea"] > 1000 ? "⚠ 超過 1000m²" : "≤ 1000m²";

                // 地下層明細
                var basementLevels = supplementary["BasementLevels"] as JArray;
                if (basementLevels != null && basementLevels.Count > 0)
                {
                    r += 2;
                    ws5.Cell(r, 1).Value = "地下層明細";
                    ws5.Cell(r, 1).Style.Font.SetBold();
                    r++;
                    string[] hBL = { "樓層", "標高(m)", "面積(m²)", "房間數" };
                    for (int c = 0; c < hBL.Length; c++)
                    {
                        ws5.Cell(r, c + 1).Value = hBL[c];
                        ws5.Cell(r, c + 1).Style.Fill.SetBackgroundColor(headerBg).Font.SetFontColor(headerFg).Font.SetBold();
                    }
                    r++;
                    foreach (JToken bl in basementLevels)
                    {
                        ws5.Cell(r, 1).Value = bl["LevelName"]?.ToString();
                        ws5.Cell(r, 2).Value = (double)bl["Elevation"];
                        ws5.Cell(r, 3).Value = (double)bl["Area"];
                        ws5.Cell(r, 4).Value = bl["RoomCount"]?.Value<int>() ?? 0;
                        r++;
                    }
                }

                // 檢討項目表
                r += 2;
                ws5.Cell(r, 1).Value = "檢討項目";
                ws5.Cell(r, 1).Style.Font.SetBold().Font.SetFontSize(12);
                r++;
                string[] h5 = { "檢討項目", "法規依據", "觸發條件", "判定結果", "說明" };
                for (int c = 0; c < h5.Length; c++)
                {
                    ws5.Cell(r, c + 1).Value = h5[c];
                    ws5.Cell(r, c + 1).Style.Fill.SetBackgroundColor(headerBg).Font.SetFontColor(headerFg).Font.SetBold();
                }
                r++;

                var items = supplementary["Items"] as JArray;
                if (items != null)
                {
                    foreach (JToken item in items)
                    {
                        ws5.Cell(r, 1).Value = item["Item"]?.ToString();
                        ws5.Cell(r, 2).Value = item["LegalBasis"]?.ToString();
                        ws5.Cell(r, 3).Value = item["TriggerCondition"]?.ToString();
                        string itemResult = item["Result"]?.ToString() ?? "";
                        ws5.Cell(r, 4).Value = itemResult;
                        ws5.Cell(r, 5).Value = item["Note"]?.ToString();

                        // 結果底色
                        var resultBg = itemResult == "PASS" ? passBg
                            : itemResult == "WARNING" ? warnBg
                            : itemResult == "MANUAL" ? ClosedXML.Excel.XLColor.FromHtml("#E3F2FD") // 淺藍
                            : altRowBg;
                        ws5.Cell(r, 4).Style.Fill.SetBackgroundColor(resultBg);
                        ws5.Cell(r, 5).Style.Alignment.SetWrapText(true);
                        r++;
                    }
                }

                // 中央管理室詳細資訊
                var centralRoom = supplementary["CentralControlRoom"];
                if (centralRoom != null && centralRoom.Type != JTokenType.Null)
                {
                    r += 1;
                    ws5.Cell(r, 1).Value = "偵測到中央管理室";
                    ws5.Cell(r, 1).Style.Font.SetBold();
                    r++;
                    ws5.Cell(r, 1).Value = "房間名稱";
                    ws5.Cell(r, 2).Value = centralRoom["RoomName"]?.ToString();
                    r++;
                    ws5.Cell(r, 1).Value = "房間編號";
                    ws5.Cell(r, 2).Value = centralRoom["RoomNumber"]?.ToString();
                    r++;
                    ws5.Cell(r, 1).Value = "所在樓層";
                    ws5.Cell(r, 2).Value = centralRoom["LevelName"]?.ToString();
                    r++;
                    ws5.Cell(r, 1).Value = "面積";
                    ws5.Cell(r, 2).Value = $"{centralRoom["Area"]} m²";
                }

                ws5.Columns().AdjustToContents();
                ws5.Column(2).Width = 55;
                ws5.Column(5).Width = 50;

                // 全域框線
                foreach (var ws in wb.Worksheets)
                {
                    var usedRange = ws.RangeUsed();
                    if (usedRange != null)
                    {
                        usedRange.Style.Border.SetOutsideBorder(ClosedXML.Excel.XLBorderStyleValues.Thin);
                        usedRange.Style.Border.SetInsideBorder(ClosedXML.Excel.XLBorderStyleValues.Thin);
                        usedRange.Style.Font.SetFontName("Microsoft JhengHei");
                    }
                }

                wb.SaveAs(outputPath);
            }

            return new
            {
                OutputPath = outputPath,
                LevelName = levelName ?? "全部",
                RoomsChecked = checkResult["LevelSummary"]?["RoomsChecked"]?.Value<int>() ?? 0,
                RoomsFailed = checkResult["LevelSummary"]?["RoomsFailed"]?.Value<int>() ?? 0,
                Message = $"排煙窗檢討報告已匯出至：{outputPath}"
            };
        }


        #endregion
    }
}
