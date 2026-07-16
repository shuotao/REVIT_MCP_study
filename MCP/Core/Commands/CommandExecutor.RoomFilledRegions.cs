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
        private object CreateRoomFilledRegions(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;
            IdType viewId = parameters["viewId"]?.Value<IdType>() ?? 0;
            View view = doc.GetElement(viewId.ToElementId()) as View;
            if (view == null)
                throw new Exception($"View not found: {viewId}");

            JArray roomIdsArray = parameters["roomIds"] as JArray;
            List<IdType> roomIds = roomIdsArray?.Select(x => x.Value<IdType>()).Distinct().ToList() ?? new List<IdType>();
            if (roomIds.Count == 0)
                throw new Exception("roomIds is required.");

            bool clearExisting = parameters["clearExisting"]?.Value<bool>() ?? true;
            string marker = parameters["marker"]?.Value<string>() ?? "MCP_TALL_PARTITION_ROOM_FILL";
            string typeName = parameters["filledRegionTypeName"]?.Value<string>() ?? "MCP_高於6m輕隔間房間_藍底";
            int transparency = Math.Max(0, Math.Min(100, parameters["transparency"]?.Value<int>() ?? 65));
            Color color = ReadRgb(parameters["color"] as JObject, new Color(0, 92, 255));

            var boundaryOptions = new SpatialElementBoundaryOptions
            {
                SpatialElementBoundaryLocation = SpatialElementBoundaryLocation.Finish
            };

            var created = new List<object>();
            var skipped = new List<object>();
            int deletedCount = 0;
            FilledRegionType regionType = null;

            using (Transaction trans = new Transaction(doc, "Create room filled regions"))
            {
                trans.Start();

                regionType = EnsureRoomFillRegionType(doc, typeName, color);

                if (clearExisting)
                {
                    var oldRegions = new FilteredElementCollector(doc, view.Id)
                        .OfClass(typeof(FilledRegion))
                        .Cast<FilledRegion>()
                        .Where(fr => HasMarker(fr, marker))
                        .Select(fr => fr.Id)
                        .ToList();

                    if (oldRegions.Count > 0)
                    {
                        doc.Delete(oldRegions);
                        deletedCount = oldRegions.Count;
                    }
                }

                foreach (IdType roomId in roomIds)
                {
                    Room room = doc.GetElement(roomId.ToElementId()) as Room;
                    if (room == null)
                    {
                        skipped.Add(new { RoomId = roomId, Reason = "Room not found" });
                        continue;
                    }

                    IList<IList<BoundarySegment>> segments = room.GetBoundarySegments(boundaryOptions);
                    if (segments == null || segments.Count == 0)
                    {
                        skipped.Add(new { RoomId = roomId, Reason = "No room boundary" });
                        continue;
                    }

                    List<CurveLoop> loops = BuildRoomCurveLoops(segments);
                    if (loops.Count == 0)
                    {
                        skipped.Add(new { RoomId = roomId, Reason = "No valid boundary loops" });
                        continue;
                    }

                    FilledRegion filledRegion;
                    try
                    {
                        filledRegion = FilledRegion.Create(doc, regionType.Id, view.Id, loops);
                    }
                    catch (Exception ex)
                    {
                        skipped.Add(new { RoomId = roomId, Reason = ex.Message });
                        continue;
                    }

                    SetMarker(filledRegion, $"{marker}; RoomId={roomId}; Room={GetRoomNumberName(room)}");

                    OverrideGraphicSettings ogs = new OverrideGraphicSettings();
                    ElementId solidPatternId = GetSolidFillPatternId(doc);
                    ogs.SetSurfaceForegroundPatternColor(color);
                    if (solidPatternId != null && solidPatternId != ElementId.InvalidElementId)
                    {
                        ogs.SetSurfaceForegroundPatternId(solidPatternId);
                        ogs.SetSurfaceForegroundPatternVisible(true);
                    }
                    if (transparency > 0)
                    {
                        ogs.SetSurfaceTransparency(transparency);
                    }
                    view.SetElementOverrides(filledRegion.Id, ogs);

                    created.Add(new
                    {
                        RoomId = roomId,
                        Room = GetRoomNumberName(room),
                        FilledRegionId = filledRegion.Id.GetIdValue(),
                        LoopCount = loops.Count
                    });
                }

                trans.Commit();
            }

            return new
            {
                Success = true,
                ViewId = view.Id.GetIdValue(),
                ViewName = view.Name,
                FilledRegionTypeId = regionType.Id.GetIdValue(),
                FilledRegionTypeName = regionType.Name,
                DeletedExisting = deletedCount,
                CreatedCount = created.Count,
                SkippedCount = skipped.Count,
                Created = created,
                Skipped = skipped
            };
        }

        private FilledRegionType EnsureRoomFillRegionType(Document doc, string typeName, Color color)
        {
            FilledRegionType existing = new FilteredElementCollector(doc)
                .OfClass(typeof(FilledRegionType))
                .Cast<FilledRegionType>()
                .FirstOrDefault(t => t.Name == typeName);
            if (existing != null)
                return existing;

            FilledRegionType template = new FilteredElementCollector(doc)
                .OfClass(typeof(FilledRegionType))
                .Cast<FilledRegionType>()
                .FirstOrDefault();
            if (template == null)
                throw new Exception("No FilledRegionType exists in this document.");

            FilledRegionType created = template.Duplicate(typeName) as FilledRegionType;
            ElementId solidPatternId = GetSolidFillPatternId(doc);
            if (solidPatternId != null && solidPatternId != ElementId.InvalidElementId)
            {
                created.ForegroundPatternId = solidPatternId;
            }
            created.ForegroundPatternColor = color;
            try { created.IsMasking = false; } catch { /* IsMasking 非所有型別/版本可設，失敗維持預設 */ }
            return created;
        }

        private List<CurveLoop> BuildRoomCurveLoops(IList<IList<BoundarySegment>> segments)
        {
            var loops = new List<CurveLoop>();
            foreach (IList<BoundarySegment> segmentList in segments)
            {
                var loop = new CurveLoop();
                foreach (BoundarySegment segment in segmentList)
                {
                    Curve curve = segment.GetCurve();
                    if (curve == null || curve.ApproximateLength < 0.001)
                        continue;
                    loop.Append(curve.Clone());
                }

                if (!loop.IsOpen())
                    loops.Add(loop);
            }
            return loops;
        }

        private static Color ReadRgb(JObject obj, Color fallback)
        {
            if (obj == null)
                return fallback;
            byte r = (byte)Math.Max(0, Math.Min(255, obj["r"]?.Value<int>() ?? fallback.Red));
            byte g = (byte)Math.Max(0, Math.Min(255, obj["g"]?.Value<int>() ?? fallback.Green));
            byte b = (byte)Math.Max(0, Math.Min(255, obj["b"]?.Value<int>() ?? fallback.Blue));
            return new Color(r, g, b);
        }

        private bool HasMarker(Element element, string marker)
        {
            string comments = element.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.AsString()
                ?? element.LookupParameter("Comments")?.AsString()
                ?? element.LookupParameter("備註")?.AsString()
                ?? "";
            return comments.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void SetMarker(Element element, string value)
        {
            Parameter parameter = element.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)
                ?? element.LookupParameter("Comments")
                ?? element.LookupParameter("備註");
            if (parameter != null && !parameter.IsReadOnly)
            {
                parameter.Set(value);
            }
        }

        private string GetRoomNumberName(Room room)
        {
            string number = room.get_Parameter(BuiltInParameter.ROOM_NUMBER)?.AsString() ?? "";
            string name = room.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? room.Name ?? "";
            return string.IsNullOrWhiteSpace(number) ? name : $"{number} {name}".Trim();
        }
    }
}
