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
        private object GetRoomDoorCounts(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;

            string levelName = parameters["level"]?.Value<string>();
            bool includeUnnamed = parameters["includeUnnamed"]?.Value<bool>() ?? true;
            bool includeDoorDetails = parameters["includeDoorDetails"]?.Value<bool>() ?? true;
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

            List<RoomDoorCountRoom> rooms = ResolveRoomsForDoorCounts(doc, levelName, explicitRoomIds, includeUnnamed);
            if (rooms.Count == 0)
            {
                throw new Exception("No placed rooms matched the door count scope.");
            }

            Dictionary<IdType, RoomDoorCountRoom> roomById = rooms.ToDictionary(room => room.RoomId);
            List<FamilyInstance> doors = ResolveDoorsForDoorCounts(doc, rooms, levelName);
            List<object> unassignedDoors = new List<object>();
            int countedDoorCount = 0;

            foreach (FamilyInstance door in doors)
            {
                RoomDoorAssignment assignment = ResolvePrimaryDoorRoom(doc, door, roomById, primaryRoomSource);
                if (assignment.Room == null)
                {
                    unassignedDoors.Add(BuildDoorSummary(doc, door, null, "No matching primary room in scope"));
                    continue;
                }

                countedDoorCount++;
                assignment.Room.DoorCount++;
                if (includeDoorDetails)
                {
                    assignment.Room.Doors.Add(BuildDoorSummary(doc, door, assignment.PrimarySource, null));
                }
            }

            string resolvedLevelName = ResolveDoorCountLevelName(doc, rooms, levelName);

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
                    Rule = "Each door is counted once against its primary room. Default primary room is ToRoom, with FromRoom fallback."
                },
                TotalRooms = rooms.Count,
                TotalDoorsInScope = doors.Count,
                CountedDoors = countedDoorCount,
                UnassignedDoors = unassignedDoors,
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
                        room.DoorCount,
                        Doors = includeDoorDetails ? room.Doors : null
                    })
                    .ToList()
            };
        }

        private List<RoomDoorCountRoom> ResolveRoomsForDoorCounts(
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
                .Select(room => new RoomDoorCountRoom
                {
                    RoomId = room.Id.GetIdValue(),
                    Number = room.Number,
                    Name = GetDoorCountRoomName(room),
                    Level = doc.GetElement(room.LevelId)?.Name,
                    AreaM2 = Math.Round(room.Area * 0.09290304, 2),
                    HasName = HasDoorCountRoomName(room)
                })
                .Where(room => includeUnnamed || room.HasName)
                .ToList();
        }

        private List<FamilyInstance> ResolveDoorsForDoorCounts(Document doc, List<RoomDoorCountRoom> rooms, string levelName)
        {
            IEnumerable<FamilyInstance> query = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Doors)
                .WhereElementIsNotElementType()
                .OfType<FamilyInstance>();

            if (!string.IsNullOrWhiteSpace(levelName))
            {
                HashSet<string> levelNames = new HashSet<string>(
                    rooms
                        .Select(room => room.Level)
                        .Where(name => !string.IsNullOrWhiteSpace(name)),
                    StringComparer.OrdinalIgnoreCase);

                query = query.Where(door =>
                {
                    string doorLevel = GetDoorLevelName(doc, door);
                    return !string.IsNullOrWhiteSpace(doorLevel) && levelNames.Contains(doorLevel);
                });
            }

            return query.ToList();
        }

        private RoomDoorAssignment ResolvePrimaryDoorRoom(
            Document doc,
            FamilyInstance door,
            Dictionary<IdType, RoomDoorCountRoom> roomById,
            string primaryRoomSource)
        {
            Room toRoom = SafeGetDoorToRoom(doc, door);
            Room fromRoom = SafeGetDoorFromRoom(doc, door);

            if (primaryRoomSource == "fromroom")
            {
                RoomDoorAssignment fromAssignment = TryCreateDoorAssignment(fromRoom, roomById, "FromRoom");
                if (fromAssignment.Room != null) return fromAssignment;
                return TryCreateDoorAssignment(toRoom, roomById, "ToRoomFallback");
            }

            if (primaryRoomSource == "auto")
            {
                RoomDoorAssignment toAssignment = TryCreateDoorAssignment(toRoom, roomById, "ToRoom");
                if (toAssignment.Room != null) return toAssignment;
                return TryCreateDoorAssignment(fromRoom, roomById, "FromRoomFallback");
            }

            RoomDoorAssignment primary = TryCreateDoorAssignment(toRoom, roomById, "ToRoom");
            if (primary.Room != null) return primary;
            return TryCreateDoorAssignment(fromRoom, roomById, "FromRoomFallback");
        }

        private RoomDoorAssignment TryCreateDoorAssignment(
            Room room,
            Dictionary<IdType, RoomDoorCountRoom> roomById,
            string source)
        {
            if (room == null)
            {
                return new RoomDoorAssignment();
            }

            IdType roomId = room.Id.GetIdValue();
            return roomById.TryGetValue(roomId, out RoomDoorCountRoom result)
                ? new RoomDoorAssignment { Room = result, PrimarySource = source }
                : new RoomDoorAssignment();
        }

        private object BuildDoorSummary(Document doc, FamilyInstance door, string primarySource, string reason)
        {
            Room toRoom = SafeGetDoorToRoom(doc, door);
            Room fromRoom = SafeGetDoorFromRoom(doc, door);
            Element type = doc.GetElement(door.GetTypeId());

            return new
            {
                ElementId = door.Id.GetIdValue(),
                Family = door.Symbol?.FamilyName,
                Type = door.Symbol?.Name,
                TypeMark = GetParameterString(type, BuiltInParameter.ALL_MODEL_TYPE_MARK),
                Mark = GetParameterString(door, BuiltInParameter.ALL_MODEL_MARK),
                Level = GetDoorLevelName(doc, door),
                PrimarySource = primarySource,
                FromRoomId = fromRoom?.Id.GetIdValue(),
                FromRoomNumber = fromRoom?.Number,
                FromRoomName = fromRoom == null ? null : GetDoorCountRoomName(fromRoom),
                ToRoomId = toRoom?.Id.GetIdValue(),
                ToRoomNumber = toRoom?.Number,
                ToRoomName = toRoom == null ? null : GetDoorCountRoomName(toRoom),
                Reason = reason
            };
        }

        private static Room SafeGetDoorFromRoom(Document doc, FamilyInstance door)
        {
            try
            {
                Room room = door.FromRoom;
                if (room != null) return room;
            }
            catch
            {
            }

            return SafeGetDoorRoomByPhase(doc, door, false);
        }

        private static Room SafeGetDoorToRoom(Document doc, FamilyInstance door)
        {
            try
            {
                Room room = door.ToRoom;
                if (room != null) return room;
            }
            catch
            {
            }

            return SafeGetDoorRoomByPhase(doc, door, true);
        }

        private static Room SafeGetDoorRoomByPhase(Document doc, FamilyInstance door, bool useToRoom)
        {
            List<Phase> phases = new List<Phase>();

            Phase createdPhase = doc.GetElement(door.CreatedPhaseId) as Phase;
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
                    Room room = useToRoom ? door.get_ToRoom(phase) : door.get_FromRoom(phase);
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

        private static string GetDoorCountRoomName(Room room)
        {
            return room.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? room.Name ?? "";
        }

        private static bool HasDoorCountRoomName(Room room)
        {
            string roomName = GetDoorCountRoomName(room);
            return !string.IsNullOrWhiteSpace(roomName) && roomName != "Room" && roomName != "房間";
        }

        private static string GetParameterString(Element element, BuiltInParameter builtInParameter)
        {
            return element?.get_Parameter(builtInParameter)?.AsString();
        }

        private static string GetDoorLevelName(Document doc, FamilyInstance door)
        {
            Element level = doc.GetElement(door.LevelId);
            if (level != null)
            {
                return level.Name;
            }

            Parameter scheduleLevel = door.get_Parameter(BuiltInParameter.INSTANCE_SCHEDULE_ONLY_LEVEL_PARAM);
            ElementId scheduleLevelId = scheduleLevel?.AsElementId();
            if (scheduleLevelId != null && scheduleLevelId != ElementId.InvalidElementId)
            {
                return doc.GetElement(scheduleLevelId)?.Name;
            }

            return null;
        }

        private static string ResolveDoorCountLevelName(Document doc, List<RoomDoorCountRoom> rooms, string requestedLevel)
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

        private class RoomDoorCountRoom
        {
            public IdType RoomId { get; set; }
            public string Number { get; set; }
            public string Name { get; set; }
            public string Level { get; set; }
            public double AreaM2 { get; set; }
            public bool HasName { get; set; }
            public int DoorCount { get; set; }
            public List<object> Doors { get; } = new List<object>();
        }

        private class RoomDoorAssignment
        {
            public RoomDoorCountRoom Room { get; set; }
            public string PrimarySource { get; set; }
        }
    }
}
