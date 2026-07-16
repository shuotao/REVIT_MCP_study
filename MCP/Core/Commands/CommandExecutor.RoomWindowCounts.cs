using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
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
        private object GetRoomWindowCounts(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;

            string levelName = parameters["level"]?.Value<string>();
            bool includeUnnamed = parameters["includeUnnamed"]?.Value<bool>() ?? true;
            bool includeWindowDetails = parameters["includeWindowDetails"]?.Value<bool>() ?? true;
            string primaryRoomSource = (parameters["primaryRoomSource"]?.Value<string>() ?? "toRoom").Trim().ToLowerInvariant();
            List<IdType> explicitRoomIds = (parameters["roomIds"] as JArray)?
                .Select(value => value.Value<IdType>())
                .Where(id => id != 0)
                .Distinct()
                .ToList();

            if (primaryRoomSource != "toroom" && primaryRoomSource != "fromroom" && primaryRoomSource != "auto")
            {
                throw new Exception("primaryRoomSource must be one of: toRoom, fromRoom, auto.");
            }

            List<RoomWindowCountRoom> rooms = ResolveRoomsForWindowCounts(doc, levelName, explicitRoomIds, includeUnnamed);
            if (rooms.Count == 0)
            {
                throw new Exception("No placed rooms matched the window count scope.");
            }

            Dictionary<IdType, RoomWindowCountRoom> roomById = rooms.ToDictionary(room => room.RoomId);
            List<FamilyInstance> windows = ResolveWindowsForRoomCounts(doc, rooms, levelName);
            List<object> unassignedWindows = new List<object>();
            int countedWindowCount = 0;

            foreach (FamilyInstance window in windows)
            {
                RoomWindowAssignment assignment = ResolvePrimaryWindowRoom(doc, window, roomById, primaryRoomSource);
                if (assignment.Room == null)
                {
                    unassignedWindows.Add(BuildWindowSummary(doc, window, null, "No matching primary room in scope"));
                    continue;
                }

                countedWindowCount++;
                assignment.Room.WindowCount++;
                if (includeWindowDetails)
                {
                    assignment.Room.Windows.Add(BuildWindowSummary(doc, window, assignment.PrimarySource, null));
                }
            }

            string resolvedLevelName = ResolveWindowCountLevelName(rooms, levelName);

            return new
            {
                Success = true,
                Scope = new
                {
                    Level = resolvedLevelName,
                    RequestedLevel = levelName,
                    RoomIds = explicitRoomIds,
                    IncludeUnnamed = includeUnnamed,
                    PrimaryRoomSource = primaryRoomSource,
                    Rule = "Each window is counted once against its primary room. Default primary room is ToRoom, with FromRoom fallback."
                },
                TotalRooms = rooms.Count,
                TotalWindowsInScope = windows.Count,
                CountedWindows = countedWindowCount,
                UnassignedWindows = unassignedWindows,
                Rooms = rooms
                    .OrderBy(room => room.Level)
                    .ThenBy(room => room.Number)
                    .ThenBy(room => room.Name)
                    .Select(room => new
                    {
                        ElementId = room.RoomId,
                        room.Number,
                        room.Name,
                        room.Level,
                        room.AreaM2,
                        room.WindowCount,
                        Windows = includeWindowDetails ? room.Windows : null
                    })
                    .ToList()
            };
        }

        private List<RoomWindowCountRoom> ResolveRoomsForWindowCounts(
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
                .Select(room => new RoomWindowCountRoom
                {
                    RoomId = room.Id.GetIdValue(),
                    Number = room.Number,
                    Name = GetWindowCountRoomName(room),
                    Level = doc.GetElement(room.LevelId)?.Name,
                    AreaM2 = Math.Round(room.Area * 0.09290304, 2),
                    HasName = HasWindowCountRoomName(room)
                })
                .Where(room => includeUnnamed || room.HasName)
                .ToList();
        }

        private List<FamilyInstance> ResolveWindowsForRoomCounts(Document doc, List<RoomWindowCountRoom> rooms, string levelName)
        {
            IEnumerable<FamilyInstance> query = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Windows)
                .WhereElementIsNotElementType()
                .OfType<FamilyInstance>();

            if (!string.IsNullOrWhiteSpace(levelName))
            {
                HashSet<string> levelNames = new HashSet<string>(
                    rooms
                        .Select(room => room.Level)
                        .Where(name => !string.IsNullOrWhiteSpace(name)),
                    StringComparer.OrdinalIgnoreCase);

                query = query.Where(window =>
                {
                    string windowLevel = GetWindowLevelName(doc, window);
                    return !string.IsNullOrWhiteSpace(windowLevel) && levelNames.Contains(windowLevel);
                });
            }

            return query.ToList();
        }

        private RoomWindowAssignment ResolvePrimaryWindowRoom(
            Document doc,
            FamilyInstance window,
            Dictionary<IdType, RoomWindowCountRoom> roomById,
            string primaryRoomSource)
        {
            Room toRoom = SafeGetWindowToRoom(doc, window);
            Room fromRoom = SafeGetWindowFromRoom(doc, window);

            if (primaryRoomSource == "fromroom")
            {
                RoomWindowAssignment fromAssignment = TryCreateWindowAssignment(fromRoom, roomById, "FromRoom");
                if (fromAssignment.Room != null) return fromAssignment;
                return TryCreateWindowAssignment(toRoom, roomById, "ToRoomFallback");
            }

            if (primaryRoomSource == "auto")
            {
                RoomWindowAssignment toAssignment = TryCreateWindowAssignment(toRoom, roomById, "ToRoom");
                if (toAssignment.Room != null) return toAssignment;
                return TryCreateWindowAssignment(fromRoom, roomById, "FromRoomFallback");
            }

            RoomWindowAssignment primary = TryCreateWindowAssignment(toRoom, roomById, "ToRoom");
            if (primary.Room != null) return primary;
            return TryCreateWindowAssignment(fromRoom, roomById, "FromRoomFallback");
        }

        private RoomWindowAssignment TryCreateWindowAssignment(
            Room room,
            Dictionary<IdType, RoomWindowCountRoom> roomById,
            string source)
        {
            if (room == null)
            {
                return new RoomWindowAssignment();
            }

            IdType roomId = room.Id.GetIdValue();
            return roomById.TryGetValue(roomId, out RoomWindowCountRoom result)
                ? new RoomWindowAssignment { Room = result, PrimarySource = source }
                : new RoomWindowAssignment();
        }

        private object BuildWindowSummary(Document doc, FamilyInstance window, string primarySource, string reason)
        {
            Room toRoom = SafeGetWindowToRoom(doc, window);
            Room fromRoom = SafeGetWindowFromRoom(doc, window);
            Element type = doc.GetElement(window.GetTypeId());

            return new
            {
                ElementId = window.Id.GetIdValue(),
                Family = window.Symbol?.FamilyName,
                Type = window.Symbol?.Name,
                TypeMark = GetWindowParameterString(type, BuiltInParameter.ALL_MODEL_TYPE_MARK),
                Mark = GetWindowParameterString(window, BuiltInParameter.ALL_MODEL_MARK),
                Level = GetWindowLevelName(doc, window),
                PrimarySource = primarySource,
                FromRoomId = fromRoom?.Id.GetIdValue(),
                FromRoomNumber = fromRoom?.Number,
                FromRoomName = fromRoom == null ? null : GetWindowCountRoomName(fromRoom),
                ToRoomId = toRoom?.Id.GetIdValue(),
                ToRoomNumber = toRoom?.Number,
                ToRoomName = toRoom == null ? null : GetWindowCountRoomName(toRoom),
                Reason = reason
            };
        }

        private static Room SafeGetWindowFromRoom(Document doc, FamilyInstance window)
        {
            try
            {
                Room room = window.FromRoom;
                if (room != null) return room;
            }
            catch
            {
                // FromRoom/ToRoom 於某些相位或未放置窗時會擲例外；交由相位回退法處理
            }

            return SafeGetWindowRoomByPhase(doc, window, false);
        }

        private static Room SafeGetWindowToRoom(Document doc, FamilyInstance window)
        {
            try
            {
                Room room = window.ToRoom;
                if (room != null) return room;
            }
            catch
            {
                // FromRoom/ToRoom 於某些相位或未放置窗時會擲例外；交由相位回退法處理
            }

            return SafeGetWindowRoomByPhase(doc, window, true);
        }

        private static Room SafeGetWindowRoomByPhase(Document doc, FamilyInstance window, bool useToRoom)
        {
            List<Phase> phases = new List<Phase>();

            Phase createdPhase = doc.GetElement(window.CreatedPhaseId) as Phase;
            if (createdPhase != null)
            {
                phases.Add(createdPhase);
            }

            phases.AddRange(new FilteredElementCollector(doc)
                .OfClass(typeof(Phase))
                .Cast<Phase>()
                .Where(phase => phases.All(existing => existing.Id != phase.Id)));

            foreach (Phase phase in phases)
            {
                try
                {
                    Room room = useToRoom ? window.get_ToRoom(phase) : window.get_FromRoom(phase);
                    if (room != null)
                    {
                        return room;
                    }
                }
                catch
                {
                    // Some family instances cannot resolve rooms for every phase.
                }
            }

            return null;
        }

        private static string GetWindowCountRoomName(Room room)
        {
            return room.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? room.Name ?? "";
        }

        private static bool HasWindowCountRoomName(Room room)
        {
            string roomName = GetWindowCountRoomName(room);
            return !string.IsNullOrWhiteSpace(roomName) && roomName != "Room" && roomName != "?輸?";
        }

        private static string GetWindowParameterString(Element element, BuiltInParameter builtInParameter)
        {
            return element?.get_Parameter(builtInParameter)?.AsString();
        }

        private static string GetWindowLevelName(Document doc, FamilyInstance window)
        {
            Element level = doc.GetElement(window.LevelId);
            if (level != null)
            {
                return level.Name;
            }

            Parameter scheduleLevel = window.get_Parameter(BuiltInParameter.INSTANCE_SCHEDULE_ONLY_LEVEL_PARAM);
            ElementId scheduleLevelId = scheduleLevel?.AsElementId();
            if (scheduleLevelId != null && scheduleLevelId != ElementId.InvalidElementId)
            {
                return doc.GetElement(scheduleLevelId)?.Name;
            }

            return null;
        }

        private static string ResolveWindowCountLevelName(List<RoomWindowCountRoom> rooms, string requestedLevel)
        {
            if (!string.IsNullOrWhiteSpace(requestedLevel))
            {
                List<string> levels = rooms
                    .Select(room => room.Level)
                    .Where(level => !string.IsNullOrWhiteSpace(level))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                return levels.Count == 1 ? levels[0] : requestedLevel;
            }

            return null;
        }

        private class RoomWindowCountRoom
        {
            public IdType RoomId { get; set; }
            public string Number { get; set; }
            public string Name { get; set; }
            public string Level { get; set; }
            public double AreaM2 { get; set; }
            public bool HasName { get; set; }
            public int WindowCount { get; set; }
            public List<object> Windows { get; } = new List<object>();
        }

        private class RoomWindowAssignment
        {
            public RoomWindowCountRoom Room { get; set; }
            public string PrimarySource { get; set; }
        }
    }
}
