using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Newtonsoft.Json.Linq;

#nullable disable

#if REVIT2025_OR_GREATER
using IdType = System.Int64;
#else
using IdType = System.Int32;
#endif

namespace RevitMCP.Core
{
    public partial class CommandExecutor
    {
        private object AnalyzeTallPartitionRooms(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;

            bool autoDetectLevels = parameters["autoDetectLevels"]?.Value<bool>() ?? false;
            List<string> levelNames = ReadPartitionStringList(
                parameters["levels"] ?? parameters["levelNames"] ?? parameters["level"],
                autoDetectLevels ? Enumerable.Empty<string>() : new[] { "B-1F", "B-3F" });
            List<string> excludeTokens = ReadPartitionStringList(parameters["excludeRoomNameContains"], new[] { "管道", "樓梯", "安全梯", "電梯", "客梯", "貨梯" });
            string wallTypeContains = parameters["wallTypeContains"]?.Value<string>() ?? "TYPE";
            double minWallHeightMm = parameters["minWallHeightMm"]?.Value<double>() ?? parameters["minHeightMm"]?.Value<double>() ?? 6000.0;
            bool includeSingleRoomBoundaryWalls = parameters["includeSingleRoomBoundaryWalls"]?.Value<bool>() ?? true;
            bool includeRoomRayHeight = parameters["includeRoomRayHeight"]?.Value<bool>() ?? true;
            bool includeDetails = parameters["includeDetails"]?.Value<bool>() ?? true;
            int sampleGrid = Math.Max(1, Math.Min(7, parameters["sampleGrid"]?.Value<int>() ?? 3));
            double maxSearchDistanceMm = parameters["maxSearchDistanceMm"]?.Value<double>() ?? 40000.0;

            var warnings = new List<string>();
            var resolvedLevels = new List<object>();
            var targetLevelIds = new HashSet<IdType>();
            if (autoDetectLevels && levelNames.Count == 0)
            {
                var levelsWithRooms = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_Rooms)
                    .WhereElementIsNotElementType()
                    .Cast<Room>()
                    .Where(room => room.Area > 0 && room.LevelId != ElementId.InvalidElementId)
                    .Select(room => room.LevelId)
                    .Distinct(new ElementIdValueComparer())
                    .ToList();

                foreach (ElementId levelId in levelsWithRooms)
                {
                    Level level = doc.GetElement(levelId) as Level;
                    if (level == null) continue;

                    targetLevelIds.Add(level.Id.GetIdValue());
                    resolvedLevels.Add(new
                    {
                        Requested = "auto",
                        LevelId = level.Id.GetIdValue(),
                        Name = level.Name,
                        ElevationMm = Math.Round(level.Elevation * 304.8, 2)
                    });
                }
            }

            foreach (string levelName in levelNames.Where(v => !string.IsNullOrWhiteSpace(v)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    Level level = FindLevel(doc, levelName, false);
                    targetLevelIds.Add(level.Id.GetIdValue());
                    resolvedLevels.Add(new
                    {
                        Requested = levelName,
                        LevelId = level.Id.GetIdValue(),
                        Name = level.Name,
                        ElevationMm = Math.Round(level.Elevation * 304.8, 2)
                    });
                }
                catch (Exception ex)
                {
                    warnings.Add($"找不到樓層 {levelName}: {ex.Message}");
                }
            }

            if (targetLevelIds.Count == 0)
                throw new Exception("沒有可分析的樓層。請提供 levels，例如 [\"B-1F\", \"B-3F\"]。");

            List<Room> targetRooms = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType()
                .Cast<Room>()
                .Where(room => room.Area > 0 && targetLevelIds.Contains(room.LevelId.GetIdValue()))
                .ToList();

            var excludedRooms = new List<object>();
            var wallRecords = new Dictionary<IdType, TallPartitionWallRecord>();
            var boundaryOptions = new SpatialElementBoundaryOptions
            {
                SpatialElementBoundaryLocation = SpatialElementBoundaryLocation.Finish
            };

            foreach (Room room in targetRooms)
            {
                string roomName = GetPartitionRoomName(room);
                if (PartitionContainsAny(roomName, excludeTokens))
                {
                    excludedRooms.Add(BuildPartitionRoomBrief(doc, room, "excluded-by-name"));
                    continue;
                }

                IList<IList<BoundarySegment>> loops = null;
                try
                {
                    loops = room.GetBoundarySegments(boundaryOptions);
                }
                catch (Exception ex)
                {
                    warnings.Add($"房間 {room.Number} {roomName} 讀取邊界失敗: {ex.Message}");
                    continue;
                }

                if (loops == null || loops.Count == 0)
                    continue;

                foreach (IList<BoundarySegment> loop in loops)
                {
                    foreach (BoundarySegment segment in loop)
                    {
                        ElementId boundaryId = segment.ElementId;
                        if (boundaryId == null || boundaryId == ElementId.InvalidElementId)
                            continue;

                        Wall wall = doc.GetElement(boundaryId) as Wall;
                        if (wall == null)
                            continue;

                        string typeName = wall.WallType?.Name ?? wall.Name ?? "";
                        if (!PartitionContains(typeName, wallTypeContains))
                            continue;

                        WallHeightEvidence heightEvidence = GetPartitionWallHeightEvidence(wall);
                        if (!heightEvidence.HeightFeet.HasValue)
                            continue;

                        double heightMm = heightEvidence.HeightFeet.Value * 304.8;
                        if (heightMm <= minWallHeightMm)
                            continue;

                        IdType wallId = wall.Id.GetIdValue();
                        TallPartitionWallRecord record;
                        if (!wallRecords.TryGetValue(wallId, out record))
                        {
                            record = new TallPartitionWallRecord
                            {
                                WallId = wallId,
                                TypeName = typeName,
                                LevelName = GetLevelName(doc, wall.LevelId),
                                WallLengthFeet = GetPartitionWallCurveLengthFeet(wall),
                                WallHeightFeet = heightEvidence.HeightFeet.Value,
                                WallHeightBasis = heightEvidence.Basis,
                                ParameterHeightFeet = heightEvidence.ParameterHeightFeet,
                                BoundingBoxHeightFeet = heightEvidence.BoundingBoxHeightFeet
                            };
                            wallRecords[wallId] = record;
                        }

                        Curve curve = segment.GetCurve();
                        double segmentLengthFeet = curve != null ? curve.Length : 0;
                        record.AddRoom(room, doc, segmentLengthFeet);
                    }
                }
            }

            if (!includeSingleRoomBoundaryWalls)
            {
                wallRecords = wallRecords
                    .Where(pair => pair.Value.RoomSides.Count >= 2)
                    .ToDictionary(pair => pair.Key, pair => pair.Value);
            }

            View3D detectorView = null;
            var roomClearHeightCache = new Dictionary<IdType, RoomClearHeightEvidence>();
            if (includeRoomRayHeight && wallRecords.Count > 0)
            {
                try
                {
                    detectorView = GetDetector3DView(doc);
                }
                catch (Exception ex)
                {
                    warnings.Add($"無法執行房間至樓板底射線檢查: {ex.Message}");
                }
            }

            var roomRecords = new Dictionary<IdType, TallPartitionRoomRecord>();
            foreach (TallPartitionWallRecord wallRecord in wallRecords.Values)
            {
                foreach (TallPartitionRoomSide side in wallRecord.RoomSides.Values)
                {
                    IdType roomId = side.Room.Id.GetIdValue();
                    TallPartitionRoomRecord roomRecord;
                    if (!roomRecords.TryGetValue(roomId, out roomRecord))
                    {
                        RoomClearHeightEvidence clearHeight = null;
                        if (includeRoomRayHeight && detectorView != null)
                        {
                            if (!roomClearHeightCache.TryGetValue(roomId, out clearHeight))
                            {
                                clearHeight = GetPartitionRoomClearHeightByRay(doc, detectorView, side.Room, sampleGrid, maxSearchDistanceMm);
                                roomClearHeightCache[roomId] = clearHeight;
                            }
                        }

                        roomRecord = new TallPartitionRoomRecord(side.Room, doc, clearHeight);
                        roomRecords[roomId] = roomRecord;
                    }

                    roomRecord.AddWall(wallRecord, side);
                }
            }

            List<TallPartitionWallRecord> sortedWalls = wallRecords.Values
                .OrderBy(w => w.LevelName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(w => w.TypeName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(w => w.WallId)
                .ToList();

            List<TallPartitionRoomRecord> sortedRooms = roomRecords.Values
                .OrderBy(r => r.LevelName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(r => r.Number, StringComparer.OrdinalIgnoreCase)
                .ThenBy(r => r.RoomId)
                .ToList();

            double uniqueWallLengthM = sortedWalls.Sum(w => w.WallLengthFeet) * 0.3048;
            double uniqueWallAreaM2 = sortedWalls.Sum(w => w.WallLengthFeet * w.WallHeightFeet) * 0.09290304;
            double roomSideLengthM = sortedRooms.Sum(r => r.TotalBoundaryLengthFeet) * 0.3048;
            double roomSideAreaM2 = sortedRooms.Sum(r => r.TotalRoomSideAreaFeet2) * 0.09290304;

            return new
            {
                Success = true,
                Criteria = new
                {
                    Levels = autoDetectLevels && levelNames.Count == 0 ? new List<string> { "auto" } : levelNames,
                AutoDetectLevels = autoDetectLevels,
                    WallTypeContains = wallTypeContains,
                    MinWallHeightMm = minWallHeightMm,
                    ExcludeRoomNameContains = excludeTokens,
                    IncludeSingleRoomBoundaryWalls = includeSingleRoomBoundaryWalls,
                    IncludeRoomRayHeight = includeRoomRayHeight,
                    RoomRaySampleGrid = includeRoomRayHeight ? (object)sampleGrid : null,
                    MaxSearchDistanceMm = includeRoomRayHeight ? (object)maxSearchDistanceMm : null
                },
                ResolvedLevels = resolvedLevels,
                Summary = new
                {
                    RoomsScanned = targetRooms.Count,
                    RoomsExcluded = excludedRooms.Count,
                    TallPartitionRooms = sortedRooms.Count,
                    TallPartitionWalls = sortedWalls.Count,
                    SharedTallPartitionWalls = sortedWalls.Count(w => w.RoomSides.Count >= 2),
                    UniqueWallLengthM = Math.Round(uniqueWallLengthM, 2),
                    UniqueWallAreaM2 = Math.Round(uniqueWallAreaM2, 2),
                    RoomSideLengthM = Math.Round(roomSideLengthM, 2),
                    RoomSideAreaM2 = Math.Round(roomSideAreaM2, 2)
                },
                Rooms = sortedRooms.Select(r => r.ToResult(includeDetails)).ToList(),
                Walls = includeDetails ? sortedWalls.Select(w => w.ToResult()).ToList() : null,
                ExcludedRooms = includeDetails ? excludedRooms : null,
                Warnings = warnings.Count > 0 ? warnings : null
            };
        }

        private List<string> ReadPartitionStringList(JToken token, IEnumerable<string> defaults)
        {
            var result = new List<string>();
            if (token is JArray array)
            {
                foreach (JToken item in array)
                {
                    string value = item?.Value<string>();
                    if (!string.IsNullOrWhiteSpace(value))
                        result.Add(value.Trim());
                }
            }
            else if (token != null)
            {
                string value = token.Value<string>();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    result.AddRange(value
                        .Split(new[] { ',', ';', '、' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(v => v.Trim())
                        .Where(v => !string.IsNullOrWhiteSpace(v)));
                }
            }

            if (result.Count == 0 && defaults != null)
                result.AddRange(defaults);

            return result;
        }

        private bool PartitionContains(string text, string token)
        {
            if (string.IsNullOrWhiteSpace(token))
                return true;

            return (text ?? "").IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool PartitionContainsAny(string text, IEnumerable<string> tokens)
        {
            string source = text ?? "";
            foreach (string token in tokens ?? Enumerable.Empty<string>())
            {
                if (!string.IsNullOrWhiteSpace(token) && source.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            return false;
        }

        private string GetPartitionRoomName(Room room)
        {
            return room?.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? room?.Name ?? "";
        }

        private object BuildPartitionRoomBrief(Document doc, Room room, string reason)
        {
            return new
            {
                RoomId = room.Id.GetIdValue(),
                Number = room.Number,
                Name = GetPartitionRoomName(room),
                Level = GetLevelName(doc, room.LevelId),
                Reason = reason
            };
        }

        private double GetPartitionWallCurveLengthFeet(Wall wall)
        {
            LocationCurve location = wall.Location as LocationCurve;
            return location?.Curve?.Length ?? 0;
        }

        private WallHeightEvidence GetPartitionWallHeightEvidence(Wall wall)
        {
            double? parameterHeight = null;
            Parameter heightParam = wall.get_Parameter(BuiltInParameter.WALL_USER_HEIGHT_PARAM);
            if (heightParam != null && heightParam.StorageType == StorageType.Double)
            {
                double value = heightParam.AsDouble();
                if (value > 1e-6)
                    parameterHeight = value;
            }

            double? bboxHeight = null;
            BoundingBoxXYZ bbox = wall.get_BoundingBox(null);
            if (bbox != null)
            {
                double value = bbox.Max.Z - bbox.Min.Z;
                if (value > 1e-6)
                    bboxHeight = value;
            }

            double? selected = bboxHeight ?? parameterHeight;
            string basis = bboxHeight.HasValue ? "bounding_box" : parameterHeight.HasValue ? "unconnected_height" : "none";

            return new WallHeightEvidence
            {
                HeightFeet = selected,
                Basis = basis,
                ParameterHeightFeet = parameterHeight,
                BoundingBoxHeightFeet = bboxHeight
            };
        }

        private RoomClearHeightEvidence GetPartitionRoomClearHeightByRay(
            Document doc,
            View3D detectorView,
            Room room,
            int sampleGrid,
            double maxSearchDistanceMm)
        {
            BoundingBoxXYZ bbox = room.get_BoundingBox(null);
            Level level = doc.GetElement(room.LevelId) as Level;
            double baseZFeet = bbox != null ? bbox.Min.Z : (level?.Elevation ?? 0);
            List<XYZ> samples = BuildPartitionRoomSamplePoints(room, bbox, baseZFeet, sampleGrid);

            var sampleResults = new List<RoomClearHeightSample>();
            double minAboveFeet = 50.0 / 304.8;

            foreach (XYZ sample in samples)
            {
                List<FloorHitInfo> hits = CollectFloorBottomHitsAtPoint(doc, detectorView, sample, baseZFeet, maxSearchDistanceMm, null)
                    .Where(hit => hit.HasHit && hit.BottomZFeet > baseZFeet + minAboveFeet)
                    .OrderBy(hit => hit.BottomZFeet)
                    .ToList();

                FloorHitInfo nearest = hits.FirstOrDefault();
                if (nearest == null)
                {
                    sampleResults.Add(new RoomClearHeightSample
                    {
                        XFeet = sample.X,
                        YFeet = sample.Y,
                        HasHit = false,
                        Message = "no-floor-above-room-bottom"
                    });
                    continue;
                }

                sampleResults.Add(new RoomClearHeightSample
                {
                    XFeet = sample.X,
                    YFeet = sample.Y,
                    HasHit = true,
                    HeightFeet = nearest.BottomZFeet - baseZFeet,
                    FloorId = nearest.FloorId,
                    FloorName = nearest.FloorName,
                    FloorLevelName = nearest.LevelName,
                    FloorBottomZFeet = nearest.BottomZFeet,
                    Message = nearest.Message
                });
            }

            var hitsOnly = sampleResults.Where(s => s.HasHit).ToList();
            return new RoomClearHeightEvidence
            {
                BaseZFeet = baseZFeet,
                SampleCount = samples.Count,
                HitCount = hitsOnly.Count,
                MinHeightFeet = hitsOnly.Count > 0 ? (double?)hitsOnly.Min(s => s.HeightFeet) : null,
                MaxHeightFeet = hitsOnly.Count > 0 ? (double?)hitsOnly.Max(s => s.HeightFeet) : null,
                AverageHeightFeet = hitsOnly.Count > 0 ? (double?)hitsOnly.Average(s => s.HeightFeet) : null,
                Samples = sampleResults
            };
        }

        private List<XYZ> BuildPartitionRoomSamplePoints(Room room, BoundingBoxXYZ bbox, double baseZFeet, int sampleGrid)
        {
            var samples = new List<XYZ>();
            double sampleZ = baseZFeet + 150.0 / 304.8;

            LocationPoint location = room.Location as LocationPoint;
            if (location != null)
                AddPartitionRoomSample(room, samples, new XYZ(location.Point.X, location.Point.Y, sampleZ));

            if (bbox != null)
            {
                List<double> ratios = sampleGrid == 1
                    ? new List<double> { 0.5 }
                    : Enumerable.Range(0, sampleGrid).Select(i => (i + 1.0) / (sampleGrid + 1.0)).ToList();

                foreach (double xRatio in ratios)
                {
                    foreach (double yRatio in ratios)
                    {
                        double x = bbox.Min.X + (bbox.Max.X - bbox.Min.X) * xRatio;
                        double y = bbox.Min.Y + (bbox.Max.Y - bbox.Min.Y) * yRatio;
                        AddPartitionRoomSample(room, samples, new XYZ(x, y, sampleZ));
                    }
                }
            }

            return samples;
        }

        private void AddPartitionRoomSample(Room room, List<XYZ> samples, XYZ point)
        {
            foreach (XYZ existing in samples)
            {
                if (existing.DistanceTo(point) < 10.0 / 304.8)
                    return;
            }

            try
            {
                if (!room.IsPointInRoom(point))
                    return;
            }
            catch
            {
                // If Revit cannot evaluate the point, keep the sample rather than losing all ray evidence.
            }

            samples.Add(point);
        }

        private class WallHeightEvidence
        {
            public double? HeightFeet { get; set; }
            public string Basis { get; set; }
            public double? ParameterHeightFeet { get; set; }
            public double? BoundingBoxHeightFeet { get; set; }
        }

        private class TallPartitionWallRecord
        {
            public IdType WallId { get; set; }
            public string TypeName { get; set; }
            public string LevelName { get; set; }
            public double WallLengthFeet { get; set; }
            public double WallHeightFeet { get; set; }
            public string WallHeightBasis { get; set; }
            public double? ParameterHeightFeet { get; set; }
            public double? BoundingBoxHeightFeet { get; set; }
            public Dictionary<IdType, TallPartitionRoomSide> RoomSides { get; } = new Dictionary<IdType, TallPartitionRoomSide>();

            public void AddRoom(Room room, Document doc, double segmentLengthFeet)
            {
                IdType roomId = room.Id.GetIdValue();
                TallPartitionRoomSide side;
                if (!RoomSides.TryGetValue(roomId, out side))
                {
                    side = new TallPartitionRoomSide
                    {
                        Room = room,
                        RoomId = roomId,
                        RoomNumber = room.Number,
                        RoomName = room.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? room.Name ?? "",
                        LevelName = (doc.GetElement(room.LevelId) as Level)?.Name ?? ""
                    };
                    RoomSides[roomId] = side;
                }

                side.BoundaryLengthFeet += Math.Max(0, segmentLengthFeet);
            }

            public object ToResult()
            {
                return new
                {
                    WallId,
                    TypeName,
                    Level = LevelName,
                    HeightMm = Math.Round(WallHeightFeet * 304.8, 2),
                    HeightBasis = WallHeightBasis,
                    ParameterHeightMm = ParameterHeightFeet.HasValue ? (object)Math.Round(ParameterHeightFeet.Value * 304.8, 2) : null,
                    BoundingBoxHeightMm = BoundingBoxHeightFeet.HasValue ? (object)Math.Round(BoundingBoxHeightFeet.Value * 304.8, 2) : null,
                    WallLengthM = Math.Round(WallLengthFeet * 0.3048, 2),
                    UniqueWallAreaM2 = Math.Round(WallLengthFeet * WallHeightFeet * 0.09290304, 2),
                    AdjacentRoomCount = RoomSides.Count,
                    IsSharedBetweenTargetRooms = RoomSides.Count >= 2,
                    Rooms = RoomSides.Values
                        .OrderBy(r => r.LevelName, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(r => r.RoomNumber, StringComparer.OrdinalIgnoreCase)
                        .Select(r => new
                        {
                            RoomId = r.RoomId,
                            Number = r.RoomNumber,
                            Name = r.RoomName,
                            Level = r.LevelName,
                            BoundaryLengthM = Math.Round(r.BoundaryLengthFeet * 0.3048, 2),
                            RoomSideAreaM2 = Math.Round(r.BoundaryLengthFeet * WallHeightFeet * 0.09290304, 2)
                        })
                        .ToList()
                };
            }
        }

        private class TallPartitionRoomSide
        {
            public Room Room { get; set; }
            public IdType RoomId { get; set; }
            public string RoomNumber { get; set; }
            public string RoomName { get; set; }
            public string LevelName { get; set; }
            public double BoundaryLengthFeet { get; set; }
        }

        private class TallPartitionRoomRecord
        {
            public IdType RoomId { get; }
            public string Number { get; }
            public string Name { get; }
            public string LevelName { get; }
            public double AreaM2 { get; }
            public RoomClearHeightEvidence ClearHeight { get; }
            public double TotalBoundaryLengthFeet { get; private set; }
            public double TotalRoomSideAreaFeet2 { get; private set; }
            private readonly List<TallPartitionRoomWallRow> _wallRows = new List<TallPartitionRoomWallRow>();
            private readonly Dictionary<string, TypeSummary> _typeSummaries = new Dictionary<string, TypeSummary>(StringComparer.OrdinalIgnoreCase);

            public TallPartitionRoomRecord(Room room, Document doc, RoomClearHeightEvidence clearHeight)
            {
                RoomId = room.Id.GetIdValue();
                Number = room.Number;
                Name = room.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? room.Name ?? "";
                LevelName = (doc.GetElement(room.LevelId) as Level)?.Name ?? "";
                AreaM2 = room.Area * 0.09290304;
                ClearHeight = clearHeight;
            }

            public void AddWall(TallPartitionWallRecord wall, TallPartitionRoomSide side)
            {
                double areaFeet2 = side.BoundaryLengthFeet * wall.WallHeightFeet;
                TotalBoundaryLengthFeet += side.BoundaryLengthFeet;
                TotalRoomSideAreaFeet2 += areaFeet2;

                TypeSummary summary;
                if (!_typeSummaries.TryGetValue(wall.TypeName, out summary))
                {
                    summary = new TypeSummary { TypeName = wall.TypeName };
                    _typeSummaries[wall.TypeName] = summary;
                }
                summary.WallCount++;
                summary.LengthFeet += side.BoundaryLengthFeet;
                summary.AreaFeet2 += areaFeet2;

                _wallRows.Add(new TallPartitionRoomWallRow
                {
                    WallId = wall.WallId,
                    TypeName = wall.TypeName,
                    HeightMm = Math.Round(wall.WallHeightFeet * 304.8, 2),
                    HeightBasis = wall.WallHeightBasis,
                    BoundaryLengthM = Math.Round(side.BoundaryLengthFeet * 0.3048, 2),
                    RoomSideAreaM2 = Math.Round(areaFeet2 * 0.09290304, 2),
                    IsSharedBetweenTargetRooms = wall.RoomSides.Count >= 2,
                    AdjacentRoomCount = wall.RoomSides.Count
                });
            }

            public object ToResult(bool includeDetails)
            {
                return new
                {
                    RoomId,
                    Number,
                    Name,
                    Level = LevelName,
                    AreaM2 = Math.Round(AreaM2, 2),
                    TallTypeWallCount = _wallRows.Count,
                    SharedTallTypeWallCount = _wallRows.Count(row => row.IsSharedBetweenTargetRooms),
                    BoundaryLengthM = Math.Round(TotalBoundaryLengthFeet * 0.3048, 2),
                    RoomSideAreaM2 = Math.Round(TotalRoomSideAreaFeet2 * 0.09290304, 2),
                    RoomClearHeightByRay = ClearHeight?.ToResult(),
                    TypeSummary = _typeSummaries.Values
                        .OrderBy(s => s.TypeName, StringComparer.OrdinalIgnoreCase)
                        .Select(s => s.ToResult())
                        .ToList(),
                    Walls = includeDetails ? _wallRows : null
                };
            }
        }

        private class TypeSummary
        {
            public string TypeName { get; set; }
            public int WallCount { get; set; }
            public double LengthFeet { get; set; }
            public double AreaFeet2 { get; set; }

            public object ToResult()
            {
                return new
                {
                    TypeName,
                    WallCount,
                    LengthM = Math.Round(LengthFeet * 0.3048, 2),
                    AreaM2 = Math.Round(AreaFeet2 * 0.09290304, 2)
                };
            }
        }

        private class TallPartitionRoomWallRow
        {
            public IdType WallId { get; set; }
            public string TypeName { get; set; }
            public double HeightMm { get; set; }
            public string HeightBasis { get; set; }
            public double BoundaryLengthM { get; set; }
            public double RoomSideAreaM2 { get; set; }
            public bool IsSharedBetweenTargetRooms { get; set; }
            public int AdjacentRoomCount { get; set; }
        }

        private class RoomClearHeightEvidence
        {
            public double BaseZFeet { get; set; }
            public int SampleCount { get; set; }
            public int HitCount { get; set; }
            public double? MinHeightFeet { get; set; }
            public double? MaxHeightFeet { get; set; }
            public double? AverageHeightFeet { get; set; }
            public List<RoomClearHeightSample> Samples { get; set; }

            public object ToResult()
            {
                return new
                {
                    BaseZMm = Math.Round(BaseZFeet * 304.8, 2),
                    SampleCount,
                    HitCount,
                    MinHeightMm = MinHeightFeet.HasValue ? (object)Math.Round(MinHeightFeet.Value * 304.8, 2) : null,
                    MaxHeightMm = MaxHeightFeet.HasValue ? (object)Math.Round(MaxHeightFeet.Value * 304.8, 2) : null,
                    AverageHeightMm = AverageHeightFeet.HasValue ? (object)Math.Round(AverageHeightFeet.Value * 304.8, 2) : null,
                    Samples = Samples?.Select(s => s.ToResult()).ToList()
                };
            }
        }

        private class RoomClearHeightSample
        {
            public double XFeet { get; set; }
            public double YFeet { get; set; }
            public bool HasHit { get; set; }
            public double HeightFeet { get; set; }
            public IdType FloorId { get; set; }
            public string FloorName { get; set; }
            public string FloorLevelName { get; set; }
            public double FloorBottomZFeet { get; set; }
            public string Message { get; set; }

            public object ToResult()
            {
                return new
                {
                    X = Math.Round(XFeet * 304.8, 2),
                    Y = Math.Round(YFeet * 304.8, 2),
                    HasHit,
                    HeightMm = HasHit ? (object)Math.Round(HeightFeet * 304.8, 2) : null,
                    FloorId = HasHit ? (object)FloorId : null,
                    FloorName,
                    FloorLevel = FloorLevelName,
                    FloorBottomZMm = HasHit ? (object)Math.Round(FloorBottomZFeet * 304.8, 2) : null,
                    Message
                };
            }
        }
    }
}
