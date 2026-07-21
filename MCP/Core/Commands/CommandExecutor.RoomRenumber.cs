using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
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
    /// 房間批次重新編號命令（renumber_rooms_by_level）
    /// 依 domain/room-numbering-workflow.md SOP 實作：
    /// - 收集指定樓層已放置且 Area > 0 的 Rooms
    /// - 中心點優先 LocationPoint，退回 BoundingBox 中心；無中心點列入 SkippedRooms
    /// - CenterY 由大到小（圖面上→下），以 yToleranceMm 貪婪分列，列內 CenterX 由小到大（左→右）
    /// - 起始號碼拆前綴＋數字尾碼，保留數字位數（B134 → B134、B135…）
    /// - 候選編號與目標樓層以外既有房號衝突時停止回報，除非 allowExistingNumberConflicts
    /// - dryRun 只回預覽；正式寫入走單一 Transaction，先寫暫時編號再寫最終編號
    ///   （兩段式避免同層房號交換時的瞬時重號），任一失敗即整批 rollback
    /// - 樓層名稱解析出多個候選時擲回例外，要求使用者指定完整名稱（SOP fallback 規則）
    /// </summary>
    public partial class CommandExecutor
    {
        private const double RENUMBER_FEET_TO_MM = 304.8;

        private object RenumberRoomsByLevel(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;

            string levelName = parameters["level"]?.Value<string>();
            string startNumber = parameters["startNumber"]?.Value<string>();
            bool dryRun = parameters["dryRun"]?.Value<bool?>() ?? false;
            bool includeUnnamed = parameters["includeUnnamed"]?.Value<bool?>() ?? true;
            double yToleranceMm = parameters["yToleranceMm"]?.Value<double?>() ?? 3000.0;
            string parameterName = parameters["parameterName"]?.Value<string>();
            bool allowConflicts = parameters["allowExistingNumberConflicts"]?.Value<bool?>() ?? false;

            if (string.IsNullOrWhiteSpace(levelName))
                throw new Exception("必須提供 level（樓層名稱）");
            if (string.IsNullOrWhiteSpace(startNumber))
                throw new Exception("必須提供 startNumber（起始房號，例如 B134）");
            if (yToleranceMm <= 0)
                throw new Exception("yToleranceMm 必須大於 0");

            // 起始號碼拆解：文字前綴 + 數字尾碼（保留位數）
            Match m = Regex.Match(startNumber.Trim(), @"^(.*?)(\d+)$");
            if (!m.Success)
                throw new Exception($"startNumber 必須以數字結尾，例如 B134，收到: {startNumber}");
            string numberPrefix = m.Groups[1].Value;
            string digits = m.Groups[2].Value;
            int startValue = int.Parse(digits);
            int digitWidth = digits.Length;

            // 樓層解析：完全相符優先；否則子字串候選，多於一個即停（SOP：請使用者指定完整名稱）
            var allLevels = new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .ToList();
            Level targetLevel = allLevels.FirstOrDefault(l => l.Name == levelName);
            if (targetLevel == null)
            {
                var candidates = allLevels
                    .Where(l => l.Name.IndexOf(levelName, StringComparison.OrdinalIgnoreCase) >= 0)
                    .ToList();
                if (candidates.Count == 0)
                    throw new Exception($"找不到樓層: {levelName}（現有樓層: {string.Join(", ", allLevels.Select(l => l.Name))}）");
                if (candidates.Count > 1)
                    throw new Exception($"樓層名稱 {levelName} 解析出多個候選: {string.Join(", ", candidates.Select(l => l.Name))}，請指定完整樓層名稱");
                targetLevel = candidates[0];
            }

            // 收集目標樓層已放置且 Area > 0 的房間
            var skipped = new List<object>();
            var placedRooms = new List<RenumberRoomInfo>();
            var levelRooms = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType()
                .Cast<Room>()
                .Where(r => r.LevelId == targetLevel.Id)
                .ToList();

            foreach (Room room in levelRooms)
            {
                string roomName = room.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString();
                bool hasName = !string.IsNullOrEmpty(roomName) && roomName != "房間";

                if (room.Area <= 0)
                {
                    skipped.Add(new { ElementId = room.Id.GetIdValue(), Name = roomName ?? "未命名", Reason = "Area = 0（未封閉或未放置）" });
                    continue;
                }
                if (!includeUnnamed && !hasName)
                {
                    skipped.Add(new { ElementId = room.Id.GetIdValue(), Name = roomName ?? "未命名", Reason = "未命名房間（includeUnnamed=false）" });
                    continue;
                }

                // 中心點：LocationPoint 優先，退回 BoundingBox 中心
                XYZ center = (room.Location as LocationPoint)?.Point;
                if (center == null)
                {
                    BoundingBoxXYZ bb = room.get_BoundingBox(null);
                    if (bb != null)
                        center = (bb.Min + bb.Max) / 2.0;
                }
                if (center == null)
                {
                    skipped.Add(new { ElementId = room.Id.GetIdValue(), Name = roomName ?? "未命名", Reason = "無 LocationPoint 且無 BoundingBox，無法取得中心點" });
                    continue;
                }

                placedRooms.Add(new RenumberRoomInfo
                {
                    Room = room,
                    Name = roomName ?? "未命名",
                    OldNumber = room.Number,
                    CenterX = center.X,
                    CenterY = center.Y,
                });
            }

            if (placedRooms.Count == 0)
                throw new Exception($"樓層 {targetLevel.Name} 沒有符合條件的已放置房間（skipped: {skipped.Count}）");

            // 排序：CenterY 由大到小貪婪分列（列基準 = 該列第一間的 Y），列內 CenterX 由小到大
            double tolFeet = yToleranceMm / RENUMBER_FEET_TO_MM;
            var byYDesc = placedRooms.OrderByDescending(r => r.CenterY).ToList();
            var rows = new List<List<RenumberRoomInfo>>();
            foreach (var info in byYDesc)
            {
                if (rows.Count > 0 && rows[rows.Count - 1][0].CenterY - info.CenterY <= tolFeet)
                    rows[rows.Count - 1].Add(info);
                else
                    rows.Add(new List<RenumberRoomInfo> { info });
            }
            var ordered = new List<RenumberRoomInfo>();
            for (int rowIdx = 0; rowIdx < rows.Count; rowIdx++)
            {
                foreach (var info in rows[rowIdx].OrderBy(r => r.CenterX))
                {
                    info.Row = rowIdx + 1;
                    ordered.Add(info);
                }
            }

            // 產生候選編號（保留位數；超位自然進位）
            for (int i = 0; i < ordered.Count; i++)
                ordered[i].NewNumber = numberPrefix + (startValue + i).ToString().PadLeft(digitWidth, '0');

            // 衝突檢查：候選編號 vs 目標樓層以外的既有房號
            var proposedSet = new HashSet<string>(ordered.Select(r => r.NewNumber), StringComparer.OrdinalIgnoreCase);
            var conflicts = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType()
                .Cast<Room>()
                .Where(r => r.LevelId != targetLevel.Id && !string.IsNullOrEmpty(r.Number) && proposedSet.Contains(r.Number))
                .Select(r => new
                {
                    Number = r.Number,
                    ElementId = r.Id.GetIdValue(),
                    Level = (doc.GetElement(r.LevelId) as Level)?.Name ?? "?",
                })
                .OrderBy(c => c.Number, StringComparer.OrdinalIgnoreCase)
                .ToList();

            string endNumber = ordered[ordered.Count - 1].NewNumber;
            var roomsPayload = ordered.Select(r => new
            {
                ElementId = r.Room.Id.GetIdValue(),
                Name = r.Name,
                OldNumber = r.OldNumber,
                NewNumber = r.NewNumber,
                Row = r.Row,
                CenterX = Math.Round(r.CenterX * RENUMBER_FEET_TO_MM, 1),
                CenterY = Math.Round(r.CenterY * RENUMBER_FEET_TO_MM, 1),
            }).ToList();

            bool blockedByConflicts = conflicts.Count > 0 && !allowConflicts;

            if (dryRun || blockedByConflicts)
            {
                return new
                {
                    Success = !blockedByConflicts,
                    DryRun = dryRun,
                    Written = false,
                    Level = targetLevel.Name,
                    LevelId = targetLevel.Id.GetIdValue(),
                    Count = ordered.Count,
                    StartNumber = ordered[0].NewNumber,
                    EndNumber = endNumber,
                    RowCount = rows.Count,
                    YToleranceMm = yToleranceMm,
                    Rooms = roomsPayload,
                    SkippedRooms = skipped,
                    Conflicts = conflicts,
                    Message = blockedByConflicts
                        ? $"候選編號與其他樓層既有房號衝突 {conflicts.Count} 筆，未寫入。確認後可設 allowExistingNumberConflicts=true 重試。"
                        : $"dry-run 預覽 {ordered.Count} 間（{rows.Count} 列），未寫入。",
                };
            }

            // 正式寫入：單一 Transaction，兩段式（暫時編號 → 最終編號）避免同層瞬時重號
            using (Transaction t = TransactionHelper.Begin(doc, $"批次重新編號房間 {targetLevel.Name}"))
            {
                t.Start();
                try
                {
                    foreach (var info in ordered)
                        SetRoomNumber(info.Room, parameterName, $"RMCP_TMP_{info.Room.Id.GetIdValue()}");
                    foreach (var info in ordered)
                        SetRoomNumber(info.Room, parameterName, info.NewNumber);
                    t.Commit();
                }
                catch (Exception ex)
                {
                    t.RollBack();
                    throw new Exception($"批次寫入失敗，已 rollback: {ex.Message}");
                }
            }

            return new
            {
                Success = true,
                DryRun = false,
                Written = true,
                Level = targetLevel.Name,
                LevelId = targetLevel.Id.GetIdValue(),
                Count = ordered.Count,
                StartNumber = ordered[0].NewNumber,
                EndNumber = endNumber,
                RowCount = rows.Count,
                YToleranceMm = yToleranceMm,
                ParameterUsed = string.IsNullOrEmpty(parameterName) ? "ROOM_NUMBER (built-in)" : parameterName,
                Rooms = roomsPayload,
                SkippedRooms = skipped,
                Conflicts = conflicts,
                Message = $"已寫入 {ordered.Count} 間房號（{ordered[0].NewNumber} → {endNumber}），請以 get_rooms_by_level 驗證。",
            };
        }

        /// <summary>寫入單一房間編號：指定 parameterName 用 LookupParameter，否則用內建 ROOM_NUMBER（語系無關）。</summary>
        private static void SetRoomNumber(Room room, string parameterName, string value)
        {
            Parameter p = string.IsNullOrEmpty(parameterName)
                ? room.get_Parameter(BuiltInParameter.ROOM_NUMBER)
                : room.LookupParameter(parameterName);

            if (p == null)
                throw new Exception($"房間 {room.Id.GetIdValue()} 找不到參數 {(string.IsNullOrEmpty(parameterName) ? "ROOM_NUMBER" : parameterName)}");
            if (p.IsReadOnly)
                throw new Exception($"房間 {room.Id.GetIdValue()} 的參數 {p.Definition.Name} 唯讀，無法寫入");
            if (p.StorageType != StorageType.String)
                throw new Exception($"房間 {room.Id.GetIdValue()} 的參數 {p.Definition.Name} 不是文字型別（{p.StorageType}）");
            if (!p.Set(value))
                throw new Exception($"房間 {room.Id.GetIdValue()} 寫入 {value} 失敗");
        }

        private class RenumberRoomInfo
        {
            public Room Room { get; set; }
            public string Name { get; set; }
            public string OldNumber { get; set; }
            public double CenterX { get; set; }
            public double CenterY { get; set; }
            public int Row { get; set; }
            public string NewNumber { get; set; }
        }
    }
}
