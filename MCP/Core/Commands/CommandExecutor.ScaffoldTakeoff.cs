using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
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
        private const double FeetToMillimeters = 304.8;
        private const double MinTakeoffCurveLengthFeet = 0.00328084; // 1 mm

        private object CalculateSelectedDetailLinePerimeter(JObject parameters)
        {
            UIDocument uidoc = _uiApp.ActiveUIDocument;
            Document doc = uidoc.Document;
            View activeView = uidoc.ActiveView;

            bool includeFamilyGeometry = parameters?["includeFamilyGeometry"]?.Value<bool>() ?? true;
            bool includeFilledRegions = parameters?["includeFilledRegions"]?.Value<bool>() ?? true;
            double minCurveLengthMm = parameters?["minCurveLengthMm"]?.Value<double>() ?? 1.0;
            double minCurveLengthFeet = Math.Max(minCurveLengthMm / FeetToMillimeters, MinTakeoffCurveLengthFeet);
            double scaffoldHeightMm = parameters?["scaffoldHeightMm"]?.Value<double>() ?? 0.0;

            var selectedIds = uidoc.Selection.GetElementIds().ToList();
            if (selectedIds.Count == 0)
                throw new Exception("No selected elements. Select detail lines, detail components, or filled regions first.");

            var elementResults = new List<object>();
            double totalLengthFeet = 0.0;
            int supportedElementCount = 0;
            int totalCurveCount = 0;
            var warnings = new List<string>();

            foreach (ElementId id in selectedIds)
            {
                Element element = doc.GetElement(id);
                if (element == null)
                    continue;

                var lengths = new List<double>();
                var elementWarnings = new List<string>();
                string source = null;

                if (element is CurveElement curveElement && curveElement.GeometryCurve != null)
                {
                    AddCurveLength(curveElement.GeometryCurve, lengths, minCurveLengthFeet);
                    source = "CurveElement";
                }
                else if (includeFilledRegions && element is FilledRegion filledRegion)
                {
                    foreach (CurveLoop loop in filledRegion.GetBoundaries())
                    {
                        foreach (Curve curve in loop)
                            AddCurveLength(curve, lengths, minCurveLengthFeet);
                    }
                    source = "FilledRegion";
                }
                else if (includeFamilyGeometry && element is FamilyInstance)
                {
                    Options options = new Options
                    {
                        DetailLevel = ViewDetailLevel.Fine,
                        IncludeNonVisibleObjects = false,
                        View = activeView
                    };

                    GeometryElement geometry = element.get_Geometry(options);
                    CollectGeometryCurveLengths(geometry, lengths, minCurveLengthFeet, elementWarnings);
                    source = "FamilyInstanceGeometry";
                }

                double elementLengthFeet = lengths.Sum();
                totalLengthFeet += elementLengthFeet;
                totalCurveCount += lengths.Count;

                if (lengths.Count > 0)
                    supportedElementCount++;
                else
                    elementWarnings.Add("No measurable curve geometry found.");

                if (elementWarnings.Count > 0)
                    warnings.AddRange(elementWarnings.Select(w => $"{element.Id.GetIdValue()}: {w}"));

                elementResults.Add(new
                {
                    ElementId = element.Id.GetIdValue(),
                    Category = element.Category?.Name,
                    Name = element.Name,
                    Source = source ?? "Unsupported",
                    CurveCount = lengths.Count,
                    LengthMm = RoundMm(elementLengthFeet),
                    LengthM = RoundM(elementLengthFeet),
                    Warnings = elementWarnings
                });
            }

            double totalLengthMm = totalLengthFeet * FeetToMillimeters;
            double areaSqM = scaffoldHeightMm > 0 ? totalLengthMm * scaffoldHeightMm / 1000000.0 : 0.0;

            return new
            {
                Mode = "selected_detail_lines",
                SelectionCount = selectedIds.Count,
                SupportedElementCount = supportedElementCount,
                TotalCurveCount = totalCurveCount,
                TotalLengthMm = Math.Round(totalLengthMm, 1),
                TotalLengthM = Math.Round(totalLengthMm / 1000.0, 3),
                ScaffoldHeightMm = scaffoldHeightMm > 0 ? scaffoldHeightMm : (double?)null,
                ScaffoldAreaSqM = scaffoldHeightMm > 0 ? Math.Round(areaSqM, 3) : (double?)null,
                LengthBasis = "sum_of_selected_curve_lengths",
                Items = elementResults,
                Warnings = warnings
            };
        }

        private object CalculateExteriorWallScaffoldPerimeter(JObject parameters)
        {
            UIDocument uidoc = _uiApp.ActiveUIDocument;
            Document doc = uidoc.Document;
            View activeView = uidoc.ActiveView;

            string levelName = parameters?["levelName"]?.Value<string>();
            bool activeViewOnly = parameters?["activeViewOnly"]?.Value<bool>() ?? true;
            bool includeCurtainWalls = parameters?["includeCurtainWalls"]?.Value<bool>() ?? true;
            bool selectResult = parameters?["selectResult"]?.Value<bool>() ?? true;
            bool includeExcludedWalls = parameters?["includeExcludedWalls"]?.Value<bool>() ?? false;
            int sampleCount = Math.Max(3, Math.Min(9, parameters?["sampleCount"]?.Value<int>() ?? 3));
            double minimumExposedRatio = Math.Max(0.1, Math.Min(1.0, parameters?["minimumExposedRatio"]?.Value<double>() ?? 0.33));
            double rayAngleDegrees = Math.Max(0.0, Math.Min(45.0, parameters?["rayAngleDegrees"]?.Value<double>() ?? 25.0));
            double minWallLengthMm = parameters?["minWallLengthMm"]?.Value<double>() ?? 300.0;
            double minWallHeightMm = Math.Max(0.0, parameters?["minWallHeightMm"]?.Value<double>() ?? 2000.0);
            double scaffoldHeightMm = parameters?["scaffoldHeightMm"]?.Value<double>() ?? 0.0;
            double endpointToleranceMm = parameters?["endpointToleranceMm"]?.Value<double>() ?? 750.0;
            bool includePerimeterBridgeWalls = parameters?["includePerimeterBridgeWalls"]?.Value<bool>() ?? true;
            double bridgeEndpointToleranceMm = parameters?["bridgeEndpointToleranceMm"]?.Value<double>() ?? endpointToleranceMm;
            double maxBridgeWallLengthMm = parameters?["maxBridgeWallLengthMm"]?.Value<double>() ?? 12000.0;

            Level levelFilter = null;
            if (!string.IsNullOrWhiteSpace(levelName))
                levelFilter = FindLevel(doc, levelName, false);

            var wallCollector = activeViewOnly
                ? new FilteredElementCollector(doc, activeView.Id)
                : new FilteredElementCollector(doc);

            var walls = wallCollector
                .OfClass(typeof(Wall))
                .WhereElementIsNotElementType()
                .Cast<Wall>()
                .Where(w => IsUsableWallForScaffold(w, includeCurtainWalls, levelFilter, minWallLengthMm, minWallHeightMm))
                .ToList();

            var wallData = walls
                .Select(w => CreateWallTakeoffData(doc, w))
                .Where(w => w != null && w.Polyline.Count >= 2)
                .ToList();

            if (wallData.Count == 0)
                throw new Exception("No usable walls found for exterior scaffold perimeter takeoff.");

            var allSegments = wallData
                .SelectMany(w => ToPolylineSegments(w))
                .ToList();

            var allPoints = allSegments.SelectMany(s => new[] { s.Start, s.End }).ToList();
            double minX = allPoints.Min(p => p.X);
            double maxX = allPoints.Max(p => p.X);
            double minY = allPoints.Min(p => p.Y);
            double maxY = allPoints.Max(p => p.Y);
            double diagonal = Math.Sqrt(Math.Pow(maxX - minX, 2) + Math.Pow(maxY - minY, 2));
            double searchDepthFeet = parameters?["searchDepthMm"]?.Value<double>() > 0
                ? parameters["searchDepthMm"].Value<double>() / FeetToMillimeters
                : Math.Max(diagonal * 2.0, 10000.0 / FeetToMillimeters);

            foreach (var wall in wallData)
                EvaluateExteriorExposure(wall, allSegments, sampleCount, rayAngleDegrees, searchDepthFeet);

            var includedWalls = wallData
                .Where(w => w.ExposedRatio >= minimumExposedRatio)
                .ToList();
            foreach (var wall in includedWalls)
                wall.InclusionReason = "ray_exposure";

            var bridgeWalls = includePerimeterBridgeWalls
                ? AddPerimeterBridgeWalls(includedWalls, wallData, bridgeEndpointToleranceMm / FeetToMillimeters, maxBridgeWallLengthMm / FeetToMillimeters)
                : new List<ScaffoldWallData>();

            var selectedElementIds = includedWalls.Select(w => w.Wall.Id).ToList();
            if (selectResult && selectedElementIds.Count > 0)
                uidoc.Selection.SetElementIds(selectedElementIds);

            double totalLengthFeet = includedWalls.Sum(w => w.LengthFeet);
            double totalLengthMm = totalLengthFeet * FeetToMillimeters;
            double areaSqM = scaffoldHeightMm > 0 ? totalLengthMm * scaffoldHeightMm / 1000000.0 : 0.0;

            var includedGroups = BuildConnectedWallGroups(includedWalls, endpointToleranceMm / FeetToMillimeters);
            var excludedWalls = wallData
                .Where(w => !includedWalls.Contains(w))
                .OrderByDescending(w => w.ExposedRatio)
                .Take(includeExcludedWalls ? int.MaxValue : 20)
                .Select(w => ToWallTakeoffResult(w, false))
                .ToList();

            return new
            {
                Mode = "exterior_wall_auto",
                ActiveViewId = activeView.Id.GetIdValue(),
                ActiveViewName = activeView.Name,
                ActiveViewOnly = activeViewOnly,
                LevelName = levelFilter?.Name,
                CandidateWallCount = wallData.Count,
                IncludedWallCount = includedWalls.Count,
                ExcludedWallCount = wallData.Count - includedWalls.Count,
                TotalLengthMm = Math.Round(totalLengthMm, 1),
                TotalLengthM = Math.Round(totalLengthMm / 1000.0, 3),
                ScaffoldHeightMm = scaffoldHeightMm > 0 ? scaffoldHeightMm : (double?)null,
                ScaffoldAreaSqM = scaffoldHeightMm > 0 ? Math.Round(areaSqM, 3) : (double?)null,
                LengthBasis = "wall_location_curve_length_centerline",
                DetectionMethod = "geometric ray exposure from wall centerlines; a wall is included when one side is open for enough sample points",
                Parameters = new
                {
                    MinimumExposedRatio = minimumExposedRatio,
                    SampleCount = sampleCount,
                    RayAngleDegrees = rayAngleDegrees,
                    SearchDepthMm = Math.Round(searchDepthFeet * FeetToMillimeters, 1),
                    MinWallLengthMm = minWallLengthMm,
                    MinWallHeightMm = minWallHeightMm,
                    IncludeCurtainWalls = includeCurtainWalls,
                    EndpointToleranceMm = endpointToleranceMm,
                    IncludePerimeterBridgeWalls = includePerimeterBridgeWalls,
                    BridgeEndpointToleranceMm = bridgeEndpointToleranceMm,
                    MaxBridgeWallLengthMm = maxBridgeWallLengthMm
                },
                BridgeWallCount = bridgeWalls.Count,
                BridgeWalls = bridgeWalls
                    .OrderByDescending(w => w.LengthFeet)
                    .Select(w => ToWallTakeoffResult(w, true))
                    .ToList(),
                ConnectedGroups = includedGroups,
                IncludedWalls = includedWalls
                    .OrderByDescending(w => w.LengthFeet)
                    .Select(w => ToWallTakeoffResult(w, true))
                    .ToList(),
                ExcludedWalls = excludedWalls
            };
        }

        private object CalculateRoomScaffoldPerimeters(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;

            string levelName = parameters?["level"]?.Value<string>() ?? parameters?["levelName"]?.Value<string>();
            bool includeUnnamed = parameters?["includeUnnamed"]?.Value<bool>() ?? true;
            bool includeRoomDetails = parameters?["includeRoomDetails"]?.Value<bool>() ?? true;
            double minSegmentLengthMm = parameters?["minBoundarySegmentLengthMm"]?.Value<double>() ?? 1.0;
            double minSegmentLengthFeet = Math.Max(minSegmentLengthMm / FeetToMillimeters, MinTakeoffCurveLengthFeet);
            double scaffoldHeightMm = parameters?["scaffoldHeightMm"]?.Value<double>() ?? 0.0;

            var finishKeywords = GetStringListParameter(parameters, "finishKeywords", new List<string>
            {
                "\u5b89\u5168\u68af",
                "\u7121\u969c\u7919\u68af",
                "\u6a13\u68af",
                "\u96fb\u68af",
                "\u8ca8\u68af",
                "\u6607\u964d\u6a5f",
                "\u5347\u964d\u6a5f",
                "\u5ba2\u68af"
            });
            var excludeKeywords = GetStringListParameter(parameters, "excludeKeywords", new List<string>
            {
                "\u6236\u5916\u5e73\u53f0",
                "\u6236\u5916\u5e73\u81fa",
                "\u9732\u81fa",
                "\u9732\u53f0",
                "\u967d\u53f0",
                "\u967d\u81fa",
                "\u7ba1\u9053\u9593",
                "\u6c34\u7bb1"
            });
            var excludeLevels = GetStringListParameter(parameters, "excludeLevels", new List<string> { "FN", "TF" });

            var roomIds = (parameters?["roomIds"] as JArray)?
                .Select(token => token.Value<IdType>())
                .Where(id => id != 0)
                .Distinct()
                .ToList();

            List<RoomScaffoldRoomResult> rooms = ResolveRoomsForScaffoldPerimeter(doc, levelName, roomIds, includeUnnamed);
            if (rooms.Count == 0)
                throw new Exception("No placed rooms matched the room scaffold perimeter scope.");

            var boundaryOptions = new SpatialElementBoundaryOptions
            {
                SpatialElementBoundaryLocation = SpatialElementBoundaryLocation.Finish
            };

            foreach (RoomScaffoldRoomResult result in rooms)
            {
                Room room = doc.GetElement(result.RoomId.ToElementId()) as Room;
                FillRoomScaffoldBoundaryResult(room, result, boundaryOptions, minSegmentLengthFeet);
                FillRoomScaffoldDimensionResult(room, result, scaffoldHeightMm);
                ClassifyRoomScaffoldResult(result, finishKeywords, excludeKeywords, excludeLevels);
            }

            var validRooms = rooms.Where(room => room.BoundaryPerimeterFeet > 0).ToList();
            var skippedRooms = rooms.Where(room => room.BoundaryPerimeterFeet <= 0).ToList();

            double generalFeet = validRooms
                .Where(room => room.CategoryKey == "general_scaffold")
                .Sum(room => room.BoundaryPerimeterFeet);
            double finishFeet = validRooms
                .Where(room => room.CategoryKey == "interior_finish_scaffold")
                .Sum(room => room.BoundaryPerimeterFeet);
            double finishVolumeM3 = validRooms
                .Where(room => room.CategoryKey == "interior_finish_scaffold")
                .Sum(room => room.InteriorFinishVolumeM3);
            double excludedFeet = validRooms
                .Where(room => room.CategoryKey == "excluded")
                .Sum(room => room.BoundaryPerimeterFeet);
            string resolvedLevelName = ResolveRoomScaffoldLevelName(doc, validRooms, levelName);

            return new
            {
                Mode = "room_scaffold_perimeters",
                LengthBasis = "general rooms use room boundary segments at finish location; interior finish scaffold uses room length x width x height",
                Parameters = new
                {
                    Level = resolvedLevelName,
                    RequestedLevel = levelName,
                    RoomIds = roomIds,
                    IncludeUnnamed = includeUnnamed,
                    BoundaryLocation = "Finish",
                    FinishKeywords = finishKeywords,
                    ExcludeKeywords = excludeKeywords,
                    ExcludeLevels = excludeLevels,
                    MinBoundarySegmentLengthMm = minSegmentLengthMm,
                    ScaffoldHeightMm = scaffoldHeightMm > 0 ? scaffoldHeightMm : (double?)null
                },
                TotalRooms = rooms.Count,
                CountedRooms = validRooms.Count(room => room.CategoryKey != "excluded"),
                ExcludedRooms = validRooms.Count(room => room.CategoryKey == "excluded"),
                SkippedRooms = skippedRooms.Select(ToRoomScaffoldSkippedResult).ToList(),
                Totals = new
                {
                    GeneralScaffold = new
                    {
                        Name = "\u65bd\u5de5\u67b6(\u542b\u9632\u8b77\u8a2d\u65bd)",
                        Formula = "\u5468\u9577 x \u9ad8",
                        RoomCount = validRooms.Count(room => room.CategoryKey == "general_scaffold"),
                        QuantitySqM = scaffoldHeightMm > 0 ? Math.Round(generalFeet * FeetToMillimeters * scaffoldHeightMm / 1000000.0, 3) : (double?)null,
                        AuditPerimeterMm = RoundMm(generalFeet),
                        AuditPerimeterM = RoundM(generalFeet)
                    },
                    InteriorFinishScaffold = new
                    {
                        Name = "\u5ba4\u5167\u88dd\u4fee\u65bd\u5de5\u67b6",
                        RoomCount = validRooms.Count(room => room.CategoryKey == "interior_finish_scaffold"),
                        Formula = "\u9577 x \u5bec x \u9ad8",
                        QuantityM3 = Math.Round(finishVolumeM3, 3),
                        AuditPerimeterMm = RoundMm(finishFeet),
                        AuditPerimeterM = RoundM(finishFeet)
                    },
                    Excluded = new
                    {
                        RoomCount = validRooms.Count(room => room.CategoryKey == "excluded"),
                        AuditPerimeterMm = RoundMm(excludedFeet),
                        AuditPerimeterM = RoundM(excludedFeet)
                    }
                },
                ByLevel = validRooms
                    .GroupBy(room => room.LevelName ?? string.Empty)
                    .OrderBy(group => group.Key)
                    .Select(group => BuildRoomScaffoldLevelSummary(group, scaffoldHeightMm))
                    .ToList(),
                Rooms = includeRoomDetails
                    ? validRooms
                        .OrderBy(room => room.LevelName)
                        .ThenBy(room => room.RoomNumber)
                        .ThenBy(room => room.RoomName)
                        .Select(ToRoomScaffoldResult)
                        .ToList()
                    : null
            };
        }

        private List<RoomScaffoldRoomResult> ResolveRoomsForScaffoldPerimeter(
            Document doc,
            string levelName,
            List<IdType> explicitRoomIds,
            bool includeUnnamed)
        {
            IEnumerable<Room> query;

            if (explicitRoomIds != null && explicitRoomIds.Count > 0)
            {
                query = explicitRoomIds
                    .Select(id => doc.GetElement(id.ToElementId()) as Room)
                    .Where(room => room != null);
            }
            else
            {
                query = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_Rooms)
                    .WhereElementIsNotElementType()
                    .Cast<Room>();

                if (!string.IsNullOrWhiteSpace(levelName))
                {
                    Level targetLevel = FindLevel(doc, levelName, false);
                    query = query.Where(room => room.LevelId == targetLevel.Id);
                }
            }

            return query
                .Where(room => room.Area > 0)
                .Select(room => new RoomScaffoldRoomResult
                {
                    RoomId = room.Id.GetIdValue(),
                    RoomNumber = room.Number,
                    RoomName = GetRoomScaffoldName(room),
                    LevelName = doc.GetElement(room.LevelId)?.Name,
                    AreaSqM = Math.Round(room.Area * 0.09290304, 2),
                    Warnings = new List<string>()
                })
                .Where(room => includeUnnamed || HasRoomScaffoldName(room.RoomName))
                .ToList();
        }

        private void FillRoomScaffoldBoundaryResult(
            Room room,
            RoomScaffoldRoomResult result,
            SpatialElementBoundaryOptions boundaryOptions,
            double minSegmentLengthFeet)
        {
            if (room == null)
            {
                result.Warnings.Add("Room element was not found.");
                return;
            }

            IList<IList<BoundarySegment>> loops = room.GetBoundarySegments(boundaryOptions);
            if (loops == null || loops.Count == 0)
            {
                result.Warnings.Add("Room has no boundary segments.");
                return;
            }

            double perimeterFeet = 0.0;
            int segmentCount = 0;
            var boundaryPoints = new List<XYZ>();

            foreach (IList<BoundarySegment> loop in loops)
            {
                foreach (BoundarySegment segment in loop)
                {
                    Curve curve = segment.GetCurve();
                    if (curve == null || curve.Length < minSegmentLengthFeet)
                        continue;

                    perimeterFeet += curve.Length;
                    segmentCount++;
                    boundaryPoints.Add(curve.GetEndPoint(0));
                    boundaryPoints.Add(curve.GetEndPoint(1));
                    foreach (XYZ point in curve.Tessellate())
                        boundaryPoints.Add(point);
                }
            }

            result.BoundaryLoopCount = loops.Count;
            result.BoundarySegmentCount = segmentCount;
            result.BoundaryPerimeterFeet = perimeterFeet;
            FillRoomPlanDimensionResult(result, boundaryPoints);
            if (segmentCount == 0)
                result.Warnings.Add("Room boundary segments were shorter than the minimum length.");
        }

        private void FillRoomPlanDimensionResult(RoomScaffoldRoomResult result, List<XYZ> boundaryPoints)
        {
            if (boundaryPoints == null || boundaryPoints.Count == 0)
            {
                result.Warnings.Add("Room boundary points were unavailable for length/width calculation.");
                return;
            }

            double minX = boundaryPoints.Min(point => point.X);
            double maxX = boundaryPoints.Max(point => point.X);
            double minY = boundaryPoints.Min(point => point.Y);
            double maxY = boundaryPoints.Max(point => point.Y);
            double xSpan = Math.Max(0.0, maxX - minX);
            double ySpan = Math.Max(0.0, maxY - minY);

            result.LengthFeet = Math.Max(xSpan, ySpan);
            result.WidthFeet = Math.Min(xSpan, ySpan);
            if (result.LengthFeet <= 0.0 || result.WidthFeet <= 0.0)
                result.Warnings.Add("Room length/width could not be derived from boundary extents.");
        }

        private void FillRoomScaffoldDimensionResult(Room room, RoomScaffoldRoomResult result, double scaffoldHeightMm)
        {
            if (scaffoldHeightMm > 0)
            {
                result.HeightMm = scaffoldHeightMm;
            }
            else
            {
                BoundingBoxXYZ bbox = room?.get_BoundingBox(null);
                if (bbox != null && bbox.Max.Z > bbox.Min.Z)
                    result.HeightMm = (bbox.Max.Z - bbox.Min.Z) * FeetToMillimeters;
            }

            if (result.HeightMm <= 0.0)
            {
                result.Warnings.Add("Room height was unavailable. Pass scaffoldHeightMm to calculate interior finish scaffold quantity.");
                return;
            }

            if (result.LengthFeet <= 0.0 || result.WidthFeet <= 0.0)
                return;

            double lengthM = result.LengthFeet * FeetToMillimeters / 1000.0;
            double widthM = result.WidthFeet * FeetToMillimeters / 1000.0;
            double heightM = result.HeightMm / 1000.0;
            result.InteriorFinishVolumeM3 = lengthM * widthM * heightM;
        }

        private void ClassifyRoomScaffoldResult(
            RoomScaffoldRoomResult result,
            List<string> finishKeywords,
            List<string> excludeKeywords,
            List<string> excludeLevels)
        {
            string evidence = $"{result.RoomName} {result.RoomNumber}";
            string matchedKeyword = finishKeywords.FirstOrDefault(keyword =>
                evidence.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0);
            string excludedKeyword = excludeKeywords.FirstOrDefault(keyword =>
                evidence.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0);
            string excludedLevel = excludeLevels.FirstOrDefault(keyword =>
                IsRoomScaffoldExcludedLevel(result.LevelName, keyword));

            if (!string.IsNullOrWhiteSpace(excludedLevel))
            {
                result.CategoryKey = "excluded";
                result.CategoryName = "\u4e0d\u8a08\u5165";
                result.MatchedKeyword = excludedLevel;
            }
            else if (!string.IsNullOrWhiteSpace(matchedKeyword))
            {
                result.CategoryKey = "interior_finish_scaffold";
                result.CategoryName = "\u5ba4\u5167\u88dd\u4fee\u65bd\u5de5\u67b6";
                result.MatchedKeyword = matchedKeyword;
            }
            else if (!string.IsNullOrWhiteSpace(excludedKeyword))
            {
                result.CategoryKey = "excluded";
                result.CategoryName = "\u4e0d\u8a08\u5165";
                result.MatchedKeyword = excludedKeyword;
            }
            else
            {
                result.CategoryKey = "general_scaffold";
                result.CategoryName = "\u65bd\u5de5\u67b6(\u542b\u9632\u8b77\u8a2d\u65bd)";
            }
        }

        private bool IsRoomScaffoldExcludedLevel(string levelName, string excludedLevel)
        {
            if (string.IsNullOrWhiteSpace(levelName) || string.IsNullOrWhiteSpace(excludedLevel))
                return false;

            string level = levelName.Trim();
            string token = excludedLevel.Trim();
            return level.Equals(token, StringComparison.OrdinalIgnoreCase)
                || level.EndsWith("-" + token, StringComparison.OrdinalIgnoreCase)
                || level.EndsWith(token, StringComparison.OrdinalIgnoreCase);
        }

        private object BuildRoomScaffoldLevelSummary(IGrouping<string, RoomScaffoldRoomResult> group, double scaffoldHeightMm)
        {
            double generalFeet = group
                .Where(room => room.CategoryKey == "general_scaffold")
                .Sum(room => room.BoundaryPerimeterFeet);
            double finishFeet = group
                .Where(room => room.CategoryKey == "interior_finish_scaffold")
                .Sum(room => room.BoundaryPerimeterFeet);
            double finishVolumeM3 = group
                .Where(room => room.CategoryKey == "interior_finish_scaffold")
                .Sum(room => room.InteriorFinishVolumeM3);
            double excludedFeet = group
                .Where(room => room.CategoryKey == "excluded")
                .Sum(room => room.BoundaryPerimeterFeet);

            return new
            {
                LevelName = string.IsNullOrWhiteSpace(group.Key) ? null : group.Key,
                RoomCount = group.Count(),
                GeneralScaffoldQuantitySqM = scaffoldHeightMm > 0 ? Math.Round(generalFeet * FeetToMillimeters * scaffoldHeightMm / 1000000.0, 3) : (double?)null,
                GeneralScaffoldAuditPerimeterM = RoundM(generalFeet),
                InteriorFinishScaffoldQuantityM3 = Math.Round(finishVolumeM3, 3),
                InteriorFinishScaffoldAuditPerimeterM = RoundM(finishFeet),
                ExcludedRoomCount = group.Count(room => room.CategoryKey == "excluded"),
                ExcludedAuditPerimeterM = RoundM(excludedFeet)
            };
        }

        private object ToRoomScaffoldResult(RoomScaffoldRoomResult room)
        {
            return new
            {
                ElementId = room.RoomId,
                Number = room.RoomNumber,
                Name = room.RoomName,
                Level = room.LevelName,
                AreaM2 = room.AreaSqM,
                CategoryKey = room.CategoryKey,
                CategoryName = room.CategoryName,
                MatchedKeyword = room.MatchedKeyword,
                BoundaryLoopCount = room.BoundaryLoopCount,
                BoundarySegmentCount = room.BoundarySegmentCount,
                PerimeterMm = RoundMm(room.BoundaryPerimeterFeet),
                PerimeterM = RoundM(room.BoundaryPerimeterFeet),
                LengthM = Math.Round(room.LengthFeet * FeetToMillimeters / 1000.0, 3),
                WidthM = Math.Round(room.WidthFeet * FeetToMillimeters / 1000.0, 3),
                HeightM = room.HeightMm > 0 ? Math.Round(room.HeightMm / 1000.0, 3) : (double?)null,
                GeneralScaffoldQuantitySqM = room.CategoryKey == "general_scaffold" && room.HeightMm > 0
                    ? Math.Round(room.BoundaryPerimeterFeet * FeetToMillimeters * room.HeightMm / 1000000.0, 3)
                    : (double?)null,
                InteriorFinishQuantityM3 = room.CategoryKey == "interior_finish_scaffold" ? Math.Round(room.InteriorFinishVolumeM3, 3) : (double?)null,
                Warnings = room.Warnings
            };
        }

        private object ToRoomScaffoldSkippedResult(RoomScaffoldRoomResult room)
        {
            return new
            {
                ElementId = room.RoomId,
                Number = room.RoomNumber,
                Name = room.RoomName,
                Level = room.LevelName,
                AreaM2 = room.AreaSqM,
                Warnings = room.Warnings
            };
        }

        private string ResolveRoomScaffoldLevelName(Document doc, List<RoomScaffoldRoomResult> rooms, string requestedLevel)
        {
            if (!string.IsNullOrWhiteSpace(requestedLevel))
            {
                Level targetLevel = FindLevel(doc, requestedLevel, false);
                return targetLevel?.Name;
            }

            List<string> levelNames = rooms
                .Select(room => room.LevelName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct()
                .ToList();

            return levelNames.Count == 1 ? levelNames[0] : null;
        }

        private static string GetRoomScaffoldName(Room room)
        {
            string name = room?.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString();
            return string.IsNullOrWhiteSpace(name) ? room?.Name : name;
        }

        private static bool HasRoomScaffoldName(string roomName)
        {
            return !string.IsNullOrWhiteSpace(roomName) && roomName != "Room" && roomName != "\u623f\u9593";
        }

        private static List<string> GetStringListParameter(JObject parameters, string name, List<string> defaults)
        {
            return (parameters?[name] as JArray)?
                .Select(token => token.Value<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList()
                ?? defaults;
        }

        private bool IsUsableWallForScaffold(Wall wall, bool includeCurtainWalls, Level levelFilter, double minWallLengthMm, double minWallHeightMm)
        {
            LocationCurve locationCurve = wall.Location as LocationCurve;
            if (locationCurve?.Curve == null)
                return false;

            if (locationCurve.Curve.Length * FeetToMillimeters < minWallLengthMm)
                return false;

            if (GetWallEffectiveHeightMm(wall) < minWallHeightMm)
                return false;

            WallType wallType = wall.Document.GetElement(wall.GetTypeId()) as WallType;
            if (wallType != null)
            {
                if (!includeCurtainWalls && wallType.Kind == WallKind.Curtain)
                    return false;

                if (wallType.Kind == WallKind.Stacked)
                    return false;
            }

            if (levelFilter != null)
            {
                Parameter baseLevelParam = wall.get_Parameter(BuiltInParameter.WALL_BASE_CONSTRAINT);
                if (baseLevelParam == null || baseLevelParam.AsElementId() != levelFilter.Id)
                    return false;
            }

            return true;
        }

        private List<ScaffoldWallData> AddPerimeterBridgeWalls(
            List<ScaffoldWallData> includedWalls,
            List<ScaffoldWallData> wallData,
            double bridgeToleranceFeet,
            double maxBridgeLengthFeet)
        {
            var bridgeWalls = new List<ScaffoldWallData>();
            bool added;

            do
            {
                added = false;
                var networkSegments = includedWalls.SelectMany(w => ToPolylineSegments(w)).ToList();
                var networkEndpoints = includedWalls
                    .SelectMany(w => new[] { w.Polyline.First(), w.Polyline.Last() })
                    .ToList();

                var candidates = wallData
                    .Where(w => !includedWalls.Contains(w))
                    .Where(w => IsPerimeterBridgeCandidate(w, maxBridgeLengthFeet))
                    .Where(w => TouchesIncludedNetworkAtBothEnds(w, networkSegments, networkEndpoints, bridgeToleranceFeet))
                    .OrderBy(w => w.LengthFeet)
                    .ToList();

                foreach (var candidate in candidates)
                {
                    candidate.InclusionReason = "perimeter_bridge";
                    includedWalls.Add(candidate);
                    bridgeWalls.Add(candidate);
                    added = true;
                }
            }
            while (added);

            return bridgeWalls;
        }

        private bool IsPerimeterBridgeCandidate(ScaffoldWallData wall, double maxBridgeLengthFeet)
        {
            if (wall.LengthFeet > maxBridgeLengthFeet)
                return false;

            if (wall.WallType == null)
                return false;

            if (wall.WallType.Function == WallFunction.Interior)
                return false;

            string typeName = wall.WallType.Name ?? string.Empty;
            return wall.WallType.Function == WallFunction.Exterior
                || typeName.Contains("\u5916\u7246")
                || typeName.Contains("\u64cb\u571f\u7246")
                || typeName.Contains("\u6321\u571f\u5899");
        }
        private bool TouchesIncludedNetworkAtBothEnds(
            ScaffoldWallData wall,
            List<ScaffoldSegment2D> networkSegments,
            List<Vec2> networkEndpoints,
            double toleranceFeet)
        {
            Vec2 start = wall.Polyline.First();
            Vec2 end = wall.Polyline.Last();
            Vec2 wallDirection = GetSegmentDirection(start, end);

            var startNearbySegments = GetNearbySegments(start, networkSegments, toleranceFeet);
            var endNearbySegments = GetNearbySegments(end, networkSegments, toleranceFeet);
            bool startNearNetwork = startNearbySegments.Count > 0;
            bool endNearNetwork = endNearbySegments.Count > 0;
            bool startNearEndpoint = networkEndpoints.Any(p => p.DistanceTo(start) <= toleranceFeet);
            bool endNearEndpoint = networkEndpoints.Any(p => p.DistanceTo(end) <= toleranceFeet);
            bool startHasCornerConnection = HasNonParallelNearbySegment(startNearbySegments, wallDirection);
            bool endHasCornerConnection = HasNonParallelNearbySegment(endNearbySegments, wallDirection);

            return startNearNetwork
                && endNearNetwork
                && startHasCornerConnection
                && endHasCornerConnection
                && (startNearEndpoint || endNearEndpoint);
        }

        private bool IsPointNearSegments(Vec2 point, List<ScaffoldSegment2D> segments, double toleranceFeet)
        {
            return segments.Any(segment => DistancePointToSegment(point, segment.Start, segment.End) <= toleranceFeet);
        }

        private List<ScaffoldSegment2D> GetNearbySegments(Vec2 point, List<ScaffoldSegment2D> segments, double toleranceFeet)
        {
            return segments
                .Where(segment => DistancePointToSegment(point, segment.Start, segment.End) <= toleranceFeet)
                .ToList();
        }

        private bool HasNonParallelNearbySegment(List<ScaffoldSegment2D> segments, Vec2 wallDirection)
        {
            foreach (var segment in segments)
            {
                Vec2 segmentDirection = GetSegmentDirection(segment.Start, segment.End);
                if (Math.Abs(wallDirection.Dot(segmentDirection)) <= 0.75)
                    return true;
            }

            return false;
        }

        private Vec2 GetSegmentDirection(Vec2 start, Vec2 end)
        {
            return end.Subtract(start).Normalize();
        }

        private double DistancePointToSegment(Vec2 point, Vec2 start, Vec2 end)
        {
            Vec2 segment = end.Subtract(start);
            Vec2 toPoint = point.Subtract(start);
            double lengthSquared = segment.Dot(segment);
            if (lengthSquared < 1e-12)
                return point.DistanceTo(start);

            double t = Math.Max(0.0, Math.Min(1.0, toPoint.Dot(segment) / lengthSquared));
            Vec2 projection = new Vec2(start.X + segment.X * t, start.Y + segment.Y * t);
            return point.DistanceTo(projection);
        }

        private double GetWallEffectiveHeightMm(Wall wall)
        {
            Parameter unconnectedHeightParam = wall.get_Parameter(BuiltInParameter.WALL_USER_HEIGHT_PARAM);
            if (unconnectedHeightParam != null && unconnectedHeightParam.HasValue)
            {
                double unconnectedHeightFeet = unconnectedHeightParam.AsDouble();
                if (unconnectedHeightFeet > 0.0)
                    return unconnectedHeightFeet * FeetToMillimeters;
            }

            Document doc = wall.Document;
            Level baseLevel = doc.GetElement(wall.LevelId) as Level;
            double baseElevationFeet = baseLevel?.Elevation ?? 0.0;
            double baseOffsetFeet = wall.get_Parameter(BuiltInParameter.WALL_BASE_OFFSET)?.AsDouble() ?? 0.0;

            ElementId topConstraintId = wall.get_Parameter(BuiltInParameter.WALL_HEIGHT_TYPE)?.AsElementId() ?? ElementId.InvalidElementId;
            if (topConstraintId != ElementId.InvalidElementId)
            {
                Level topLevel = doc.GetElement(topConstraintId) as Level;
                if (topLevel != null)
                {
                    double topOffsetFeet = wall.get_Parameter(BuiltInParameter.WALL_TOP_OFFSET)?.AsDouble() ?? 0.0;
                    double heightFeet = (topLevel.Elevation + topOffsetFeet) - (baseElevationFeet + baseOffsetFeet);
                    if (heightFeet > 0.0)
                        return heightFeet * FeetToMillimeters;
                }
            }

            BoundingBoxXYZ bbox = wall.get_BoundingBox(null);
            if (bbox != null)
            {
                double bboxHeightFeet = bbox.Max.Z - bbox.Min.Z;
                if (bboxHeightFeet > 0.0)
                    return bboxHeightFeet * FeetToMillimeters;
            }

            return 0.0;
        }

        private ScaffoldWallData CreateWallTakeoffData(Document doc, Wall wall)
        {
            LocationCurve locationCurve = wall.Location as LocationCurve;
            if (locationCurve?.Curve == null)
                return null;

            WallType wallType = doc.GetElement(wall.GetTypeId()) as WallType;
            Curve curve = locationCurve.Curve;

            return new ScaffoldWallData
            {
                Wall = wall,
                WallType = wallType,
                Curve = curve,
                LengthFeet = curve.Length,
                Polyline = CurveToPoints2D(curve, 16)
            };
        }

        private void EvaluateExteriorExposure(
            ScaffoldWallData wall,
            List<ScaffoldSegment2D> allSegments,
            int sampleCount,
            double rayAngleDegrees,
            double searchDepthFeet)
        {
            int exposedSamples = 0;
            int sideAPenetrations = 0;
            int sideBPenetrations = 0;
            var sampleResults = new List<object>();

            for (int i = 0; i < sampleCount; i++)
            {
                double normalized = (i + 1.0) / (sampleCount + 1.0);
                XYZ point = wall.Curve.Evaluate(normalized, true);
                XYZ tangent = GetCurveTangent(wall.Curve, normalized);
                Vec2 normal = new Vec2(-tangent.Y, tangent.X).Normalize();
                Vec2 samplePoint = new Vec2(point.X, point.Y);

                bool sideAHit = HasRayHit(samplePoint, normal, wall.Wall.Id, allSegments, searchDepthFeet, rayAngleDegrees);
                bool sideBHit = HasRayHit(samplePoint, normal.Negate(), wall.Wall.Id, allSegments, searchDepthFeet, rayAngleDegrees);

                if (!sideAHit) sideAPenetrations++;
                if (!sideBHit) sideBPenetrations++;
                if (!sideAHit || !sideBHit) exposedSamples++;

                sampleResults.Add(new
                {
                    Station = Math.Round(normalized, 3),
                    SideAOpen = !sideAHit,
                    SideBOpen = !sideBHit
                });
            }

            wall.ExposedRatio = exposedSamples / (double)sampleCount;
            wall.OpenSideA = sideAPenetrations;
            wall.OpenSideB = sideBPenetrations;
            wall.SampleResults = sampleResults;
        }

        private bool HasRayHit(
            Vec2 origin,
            Vec2 normal,
            ElementId sourceWallId,
            List<ScaffoldSegment2D> allSegments,
            double searchDepthFeet,
            double rayAngleDegrees)
        {
            var angles = new[] { 0.0, -rayAngleDegrees, rayAngleDegrees };
            foreach (double angle in angles)
            {
                Vec2 direction = normal.RotateDegrees(angle).Normalize();
                foreach (ScaffoldSegment2D segment in allSegments)
                {
                    if (segment.WallId == sourceWallId)
                        continue;

                    if (TryRaySegmentIntersection(origin, direction, segment.Start, segment.End, out double distance)
                        && distance > 0.05
                        && distance <= searchDepthFeet)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private bool TryRaySegmentIntersection(Vec2 rayOrigin, Vec2 rayDirection, Vec2 segStart, Vec2 segEnd, out double distance)
        {
            distance = 0.0;
            Vec2 segment = segEnd.Subtract(segStart);
            double denominator = rayDirection.Cross(segment);

            if (Math.Abs(denominator) < 1e-9)
                return false;

            Vec2 delta = segStart.Subtract(rayOrigin);
            double t = delta.Cross(segment) / denominator;
            double u = delta.Cross(rayDirection) / denominator;

            if (t >= 0.0 && u >= -1e-8 && u <= 1.0 + 1e-8)
            {
                distance = t;
                return true;
            }

            return false;
        }

        private XYZ GetCurveTangent(Curve curve, double normalized)
        {
            try
            {
                Transform derivatives = curve.ComputeDerivatives(normalized, true);
                XYZ tangent = derivatives.BasisX;
                if (tangent != null && tangent.GetLength() > 1e-9)
                    return tangent.Normalize();
            }
            catch
            {
                // Fallback below.
            }

            XYZ p0 = curve.GetEndPoint(0);
            XYZ p1 = curve.GetEndPoint(1);
            XYZ fallback = p1 - p0;
            return fallback.GetLength() > 1e-9 ? fallback.Normalize() : XYZ.BasisX;
        }

        private List<ScaffoldSegment2D> ToPolylineSegments(ScaffoldWallData wall)
        {
            var result = new List<ScaffoldSegment2D>();
            for (int i = 0; i < wall.Polyline.Count - 1; i++)
            {
                if (wall.Polyline[i].DistanceTo(wall.Polyline[i + 1]) > 1e-9)
                {
                    result.Add(new ScaffoldSegment2D
                    {
                        WallId = wall.Wall.Id,
                        Start = wall.Polyline[i],
                        End = wall.Polyline[i + 1]
                    });
                }
            }
            return result;
        }

        private List<Vec2> CurveToPoints2D(Curve curve, int arcSegments)
        {
            var result = new List<Vec2>();

            if (curve is Line)
            {
                result.Add(ToVec2(curve.GetEndPoint(0)));
                result.Add(ToVec2(curve.GetEndPoint(1)));
                return result;
            }

            IList<XYZ> tessellated = curve.Tessellate();
            if (tessellated != null && tessellated.Count >= 2)
                return tessellated.Select(ToVec2).ToList();

            for (int i = 0; i <= arcSegments; i++)
            {
                double t = i / (double)arcSegments;
                result.Add(ToVec2(curve.Evaluate(t, true)));
            }

            return result;
        }

        private Vec2 ToVec2(XYZ point)
        {
            return new Vec2(point.X, point.Y);
        }

        private List<object> BuildConnectedWallGroups(List<ScaffoldWallData> walls, double endpointToleranceFeet)
        {
            var groups = new List<List<ScaffoldWallData>>();
            var unvisited = new HashSet<ScaffoldWallData>(walls);

            while (unvisited.Count > 0)
            {
                ScaffoldWallData seed = unvisited.First();
                unvisited.Remove(seed);

                var group = new List<ScaffoldWallData> { seed };
                var queue = new Queue<ScaffoldWallData>();
                queue.Enqueue(seed);

                while (queue.Count > 0)
                {
                    ScaffoldWallData current = queue.Dequeue();
                    var neighbors = unvisited
                        .Where(other => AreWallsEndpointConnected(current, other, endpointToleranceFeet))
                        .ToList();

                    foreach (ScaffoldWallData neighbor in neighbors)
                    {
                        unvisited.Remove(neighbor);
                        group.Add(neighbor);
                        queue.Enqueue(neighbor);
                    }
                }

                groups.Add(group);
            }

            return groups
                .OrderByDescending(g => g.Sum(w => w.LengthFeet))
                .Select((g, index) => new
                {
                    GroupIndex = index + 1,
                    WallCount = g.Count,
                    LengthMm = RoundMm(g.Sum(w => w.LengthFeet)),
                    LengthM = RoundM(g.Sum(w => w.LengthFeet)),
                    WallIds = g.Select(w => w.Wall.Id.GetIdValue()).ToList()
                })
                .Cast<object>()
                .ToList();
        }

        private bool AreWallsEndpointConnected(ScaffoldWallData a, ScaffoldWallData b, double toleranceFeet)
        {
            var aEnds = new[] { a.Polyline.First(), a.Polyline.Last() };
            var bEnds = new[] { b.Polyline.First(), b.Polyline.Last() };
            return aEnds.Any(pa => bEnds.Any(pb => pa.DistanceTo(pb) <= toleranceFeet));
        }

        private object ToWallTakeoffResult(ScaffoldWallData wall, bool included)
        {
            Vec2 start = wall.Polyline.First();
            Vec2 end = wall.Polyline.Last();
            string wallFunction = wall.WallType != null ? wall.WallType.Function.ToString() : null;
            string wallKind = wall.WallType != null ? wall.WallType.Kind.ToString() : null;

            return new
            {
                WallId = wall.Wall.Id.GetIdValue(),
                TypeName = wall.WallType?.Name,
                WallFunction = wallFunction,
                WallKind = wallKind,
                Included = included,
                InclusionReason = wall.InclusionReason,
                ExposedRatio = Math.Round(wall.ExposedRatio, 3),
                OpenSideASamples = wall.OpenSideA,
                OpenSideBSamples = wall.OpenSideB,
                LengthMm = RoundMm(wall.LengthFeet),
                LengthM = RoundM(wall.LengthFeet),
                Start = new { X = RoundMm(start.X), Y = RoundMm(start.Y) },
                End = new { X = RoundMm(end.X), Y = RoundMm(end.Y) },
                Samples = wall.SampleResults
            };
        }

        private void AddCurveLength(Curve curve, List<double> lengths, double minCurveLengthFeet)
        {
            if (curve == null)
                return;

            double length = curve.Length;
            if (length >= minCurveLengthFeet)
                lengths.Add(length);
        }

        private void CollectGeometryCurveLengths(
            GeometryElement geometry,
            List<double> lengths,
            double minCurveLengthFeet,
            List<string> warnings)
        {
            if (geometry == null)
                return;

            foreach (GeometryObject obj in geometry)
            {
                if (obj is Curve curve)
                {
                    AddCurveLength(curve, lengths, minCurveLengthFeet);
                }
                else if (obj is PolyLine polyLine)
                {
                    IList<XYZ> points = polyLine.GetCoordinates();
                    for (int i = 0; i < points.Count - 1; i++)
                    {
                        double length = points[i].DistanceTo(points[i + 1]);
                        if (length >= minCurveLengthFeet)
                            lengths.Add(length);
                    }
                }
                else if (obj is GeometryInstance instance)
                {
                    CollectGeometryCurveLengths(instance.GetInstanceGeometry(), lengths, minCurveLengthFeet, warnings);
                }
                else if (obj is Solid solid && solid.Edges != null)
                {
                    foreach (Edge edge in solid.Edges)
                    {
                        Curve edgeCurve = edge.AsCurve();
                        AddCurveLength(edgeCurve, lengths, minCurveLengthFeet);
                    }
                }
            }
        }

        private double RoundMm(double feet)
        {
            return Math.Round(feet * FeetToMillimeters, 1);
        }

        private double RoundM(double feet)
        {
            return Math.Round(feet * FeetToMillimeters / 1000.0, 3);
        }

        private class ScaffoldWallData
        {
            public Wall Wall { get; set; }
            public WallType WallType { get; set; }
            public Curve Curve { get; set; }
            public double LengthFeet { get; set; }
            public List<Vec2> Polyline { get; set; }
            public double ExposedRatio { get; set; }
            public int OpenSideA { get; set; }
            public int OpenSideB { get; set; }
            public string InclusionReason { get; set; }
            public List<object> SampleResults { get; set; }
        }

        private class ScaffoldSegment2D
        {
            public ElementId WallId { get; set; }
            public Vec2 Start { get; set; }
            public Vec2 End { get; set; }
        }

        private class RoomScaffoldRoomResult
        {
            public IdType RoomId { get; set; }
            public string RoomNumber { get; set; }
            public string RoomName { get; set; }
            public string LevelName { get; set; }
            public double AreaSqM { get; set; }
            public string CategoryKey { get; set; }
            public string CategoryName { get; set; }
            public string MatchedKeyword { get; set; }
            public int BoundaryLoopCount { get; set; }
            public int BoundarySegmentCount { get; set; }
            public double BoundaryPerimeterFeet { get; set; }
            public double LengthFeet { get; set; }
            public double WidthFeet { get; set; }
            public double HeightMm { get; set; }
            public double InteriorFinishVolumeM3 { get; set; }
            public List<string> Warnings { get; set; }
        }

        private struct Vec2
        {
            public double X { get; }
            public double Y { get; }

            public Vec2(double x, double y)
            {
                X = x;
                Y = y;
            }

            public Vec2 Subtract(Vec2 other)
            {
                return new Vec2(X - other.X, Y - other.Y);
            }

            public Vec2 Negate()
            {
                return new Vec2(-X, -Y);
            }

            public double Cross(Vec2 other)
            {
                return X * other.Y - Y * other.X;
            }

            public double Dot(Vec2 other)
            {
                return X * other.X + Y * other.Y;
            }

            public double DistanceTo(Vec2 other)
            {
                double dx = X - other.X;
                double dy = Y - other.Y;
                return Math.Sqrt(dx * dx + dy * dy);
            }

            public Vec2 Normalize()
            {
                double length = Math.Sqrt(X * X + Y * Y);
                return length > 1e-9 ? new Vec2(X / length, Y / length) : new Vec2(1.0, 0.0);
            }

            public Vec2 RotateDegrees(double degrees)
            {
                double radians = degrees * Math.PI / 180.0;
                double cos = Math.Cos(radians);
                double sin = Math.Sin(radians);
                return new Vec2(X * cos - Y * sin, X * sin + Y * cos);
            }
        }
    }
}
