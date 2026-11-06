using System;
using System.Collections.Generic;
using System.Globalization;
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
    /// 房間重新排序編號命令（issue #111 / #125）
    ///
    /// 方法定義於 domain/room-numbering-workflow.md，本檔案是其唯一 C# 落地：
    /// - 收集目標樓層已放置（Area &gt; 0）的 Rooms
    /// - 房間中心點：優先 LocationPoint，其次 BoundingBox 中心；兩者皆無則列入 SkippedRooms
    /// - 依 CenterY 由大到小排序（圖面上方到下方），以 yToleranceMm 分列
    /// - 同列依 CenterX 由小到大排序（由左到右）
    /// - 從 startNumber 的文字前綴＋數字尾碼開始連續遞增，保留補零寬度
    /// - 單一 Transaction 寫入；任一房間寫入失敗則整批回滾
    ///
    /// 純邏輯（種子解析／格式化／排序分列）抽成 static 方法，接受純值而非 Revit 物件，
    /// 供 net48 harness 以 reflection 直接驗證（Stage S4 acceptance C2）。
    /// </summary>
    public partial class CommandExecutor
    {
        #region 房間重新編號

        /// <summary>
        /// 種子（startNumber）解析結果：文字前綴 + 數字尾碼的值與寬度。
        /// </summary>
        internal struct RoomNumberSeed
        {
            public string Prefix;
            public long Value;
            public int Width;
        }

        /// <summary>
        /// 排序演算法輸入/輸出用的純值三元組：Id（字串，避免依賴 IdType）、CenterX（mm）、CenterY（mm）。
        /// RowIndex 由 OrderRoomsTopDownLeftRight 填入，輸入時忽略。
        /// </summary>
        internal struct RoomOrderPoint
        {
            public string Id;
            public double X;
            public double Y;
            public int RowIndex;
        }

        /// <summary>
        /// 解析 startNumber：拆出文字前綴與數字尾碼（含寬度）。沒有數字尾碼一律拒絕。
        /// 純函式，不依賴任何 Revit 型別 — 可被 harness 用 reflection 直接呼叫驗證。
        /// </summary>
        private static RoomNumberSeed ParseRoomNumberSeed(string seed)
        {
            if (string.IsNullOrEmpty(seed))
                throw new ArgumentException("startNumber 不可為空，且必須以數字結尾。");

            int splitIndex = seed.Length;
            while (splitIndex > 0 && char.IsDigit(seed[splitIndex - 1]))
                splitIndex--;

            if (splitIndex == seed.Length)
                throw new ArgumentException($"startNumber '{seed}' 沒有數字尾碼，無法遞增編號。");

            string prefix = seed.Substring(0, splitIndex);
            string digits = seed.Substring(splitIndex);

            return new RoomNumberSeed
            {
                Prefix = prefix,
                Value = long.Parse(digits, CultureInfo.InvariantCulture),
                Width = digits.Length
            };
        }

        /// <summary>
        /// 依前綴＋數值＋補零寬度組出房間編號字串。value 若需要的位數超過 width，
        /// 不截斷、自然長出（例如 width=3 的 R999 下一號是 R1000，不是 R000）。
        /// 純函式，可被 harness 直接呼叫驗證。
        /// </summary>
        private static string FormatRoomNumber(string prefix, long value, int width)
        {
            string digits = value.ToString(CultureInfo.InvariantCulture);
            if (digits.Length < width)
                digits = digits.PadLeft(width, '0');
            return prefix + digits;
        }

        /// <summary>
        /// 依 domain/room-numbering-workflow.md 的排序規則排序：
        /// 1) 依 Y 由大到小排序（圖面上方到下方）
        /// 2) 用 yToleranceMm 把相近 Y 分成同一列：新列的錨點 Y 取該列第一個房間的 Y，
        ///    後續房間只要與「該列錨點」的差在容差內就併入同列（不是跟前一筆比較，
        ///    避免容差鏈式漂移把同列以外的房間拉進來）
        /// 3) 同列內依 X 由小到大排序（由左到右）
        /// 回傳依最終編號順序排列的清單，RowIndex 標明第幾列（從 0 起算）。
        /// 純函式，操作 plain id/x/y 三元組，不依賴任何 Revit 型別。
        /// </summary>
        private static List<RoomOrderPoint> OrderRoomsTopDownLeftRight(List<RoomOrderPoint> rooms, double yToleranceMm)
        {
            var result = new List<RoomOrderPoint>();
            if (rooms == null || rooms.Count == 0)
                return result;

            var sortedByYDesc = rooms.OrderByDescending(r => r.Y).ToList();

            var rows = new List<List<RoomOrderPoint>>();
            double rowAnchorY = 0;

            foreach (var room in sortedByYDesc)
            {
                if (rows.Count == 0 || Math.Abs(room.Y - rowAnchorY) > yToleranceMm)
                {
                    rows.Add(new List<RoomOrderPoint>());
                    rowAnchorY = room.Y;
                }
                rows[rows.Count - 1].Add(room);
            }

            for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
            {
                foreach (var room in rows[rowIndex].OrderBy(r => r.X))
                {
                    var placed = room;
                    placed.RowIndex = rowIndex;
                    result.Add(placed);
                }
            }

            return result;
        }

        /// <summary>
        /// 取得房間中心點（mm）：優先 LocationPoint，其次 BoundingBox 中心。
        /// 兩者皆無則回傳 false（呼叫端應將該房間列入 SkippedRooms，不參與編號）。
        /// </summary>
        private static bool TryGetRoomCenterMm(Room room, out double xMm, out double yMm)
        {
            const double feetToMm = 304.8;

            LocationPoint locationPoint = room.Location as LocationPoint;
            if (locationPoint != null)
            {
                xMm = locationPoint.Point.X * feetToMm;
                yMm = locationPoint.Point.Y * feetToMm;
                return true;
            }

            BoundingBoxXYZ bbox = room.get_BoundingBox(null);
            if (bbox != null)
            {
                xMm = (bbox.Min.X + bbox.Max.X) / 2.0 * feetToMm;
                yMm = (bbox.Min.Y + bbox.Max.Y) / 2.0 * feetToMm;
                return true;
            }

            xMm = 0;
            yMm = 0;
            return false;
        }

        /// <summary>
        /// 解析目標房號要寫入的 Parameter：指定 parameterName 就用 LookupParameter，
        /// 否則用內建的 ROOM_NUMBER。
        /// </summary>
        private static Parameter ResolveRoomNumberParameter(Room room, string parameterName)
        {
            if (!string.IsNullOrWhiteSpace(parameterName))
                return room.LookupParameter(parameterName);

            return room.get_Parameter(BuiltInParameter.ROOM_NUMBER);
        }

        /// <summary>
        /// 依樓層名稱找出唯一目標樓層。與共用的 FindLevel 不同之處：
        /// 找到多個候選一律視為錯誤並停止（domain 規則：「若樓層名稱解析出多個候選，
        /// 停止並請使用者指定完整樓層名稱」），不會像 FindLevel 靜默取第一筆。
        /// </summary>
        private static Level ResolveUnambiguousLevel(Document doc, string levelName)
        {
            if (string.IsNullOrWhiteSpace(levelName))
                throw new Exception("請指定樓層名稱 (level)。");

            var allLevels = new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .ToList();

            var exactMatches = allLevels.Where(l => l.Name == levelName).ToList();
            if (exactMatches.Count == 1)
                return exactMatches[0];

            var partialMatches = allLevels
                .Where(l => l.Name.Contains(levelName) || levelName.Contains(l.Name))
                .ToList();

            if (partialMatches.Count == 1)
                return partialMatches[0];

            if (partialMatches.Count == 0)
                throw new Exception($"找不到樓層: {levelName}");

            string candidateNames = string.Join(", ", partialMatches.Select(l => l.Name));
            throw new Exception($"樓層名稱 '{levelName}' 解析出多個候選（{candidateNames}），請指定完整樓層名稱。");
        }

        /// <summary>
        /// 批次重新排序編號指定樓層的已放置房間。單一 Transaction 內完成寫入；
        /// 任一房間寫入失敗則整批回滾。
        /// </summary>
        private object RenumberRoomsByLevel(JObject parameters)
        {
            string levelName = parameters["level"]?.Value<string>();
            string startNumber = parameters["startNumber"]?.Value<string>();
            bool dryRun = parameters["dryRun"]?.Value<bool?>() ?? false;
            bool includeUnnamed = parameters["includeUnnamed"]?.Value<bool?>() ?? true;
            double yToleranceMm = parameters["yToleranceMm"]?.Value<double?>() ?? 3000;
            string parameterName = parameters["parameterName"]?.Value<string>();
            bool allowExistingNumberConflicts = parameters["allowExistingNumberConflicts"]?.Value<bool?>() ?? false;

            if (string.IsNullOrWhiteSpace(levelName))
                throw new Exception("請指定樓層名稱 (level)。");

            RoomNumberSeed seed = ParseRoomNumberSeed(startNumber);

            Document doc = _uiApp.ActiveUIDocument.Document;
            Level targetLevel = ResolveUnambiguousLevel(doc, levelName);

            // 1. 收集目標樓層所有已放置（Area > 0）的 Rooms
            var levelRooms = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType()
                .Cast<Room>()
                .Where(r => r.LevelId == targetLevel.Id && r.Area > 0)
                .ToList();

            var skippedRooms = new List<object>();
            var orderInput = new List<RoomOrderPoint>();
            var roomById = new Dictionary<string, Room>();

            foreach (Room room in levelRooms)
            {
                string roomName = room.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString();
                bool hasName = !string.IsNullOrEmpty(roomName) && roomName != "房間";
                string idString = room.Id.GetIdValue().ToString(CultureInfo.InvariantCulture);

                if (!includeUnnamed && !hasName)
                {
                    skippedRooms.Add(new
                    {
                        ElementId = room.Id.GetIdValue(),
                        OldNumber = ResolveRoomNumberParameter(room, parameterName)?.AsString(),
                        Name = roomName ?? "未命名",
                        Reason = "未命名房間且 includeUnnamed=false，已排除"
                    });
                    continue;
                }

                double xMm, yMm;
                if (!TryGetRoomCenterMm(room, out xMm, out yMm))
                {
                    skippedRooms.Add(new
                    {
                        ElementId = room.Id.GetIdValue(),
                        OldNumber = ResolveRoomNumberParameter(room, parameterName)?.AsString(),
                        Name = roomName ?? "未命名",
                        Reason = "無法取得中心點（無 LocationPoint 亦無 BoundingBox），未放置或幾何異常"
                    });
                    continue;
                }

                roomById[idString] = room;
                orderInput.Add(new RoomOrderPoint { Id = idString, X = xMm, Y = yMm });
            }

            List<RoomOrderPoint> ordered = OrderRoomsTopDownLeftRight(orderInput, yToleranceMm);

            // 2. 依排序結果組出提案編號
            var proposedRooms = new List<(Room Room, string OldNumber, string NewNumber, double X, double Y, int RowIndex)>();
            for (int i = 0; i < ordered.Count; i++)
            {
                RoomOrderPoint point = ordered[i];
                Room room = roomById[point.Id];
                string newNumber = FormatRoomNumber(seed.Prefix, seed.Value + i, seed.Width);
                string oldNumber = ResolveRoomNumberParameter(room, parameterName)?.AsString();
                proposedRooms.Add((room, oldNumber, newNumber, point.X, point.Y, point.RowIndex));
            }

            // 3. 衝突檢查：提案編號是否已存在於「不會被本次重新編號改到」的房間上。
            //    範圍刻意是「排除實際進入 proposedRooms 的房間」，而不是「排除整個目標樓層」：
            //    後者會漏掉目標樓層上因 includeUnnamed=false 或取不到中心點而落入 SkippedRooms、
            //    但仍保留原編號的房間——那些房間的舊編號若剛好撞上本次提案編號，會在同一樓層
            //    產生重複房號卻不會被回報（inspector-ops 發現的 major 缺陷）。
            //    以 proposedRooms 排除後，涵蓋範圍自然變成：(a) 其他樓層的房間（domain 規則明文
            //    要求）、(b) 目標樓層上 Area=0 的房間、(c) 目標樓層上被跳過但保留原編號的房間。
            var proposedRoomIds = new HashSet<IdType>(proposedRooms.Select(p => p.Room.Id.GetIdValue()));
            var otherRooms = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType()
                .Cast<Room>()
                .Where(r => !proposedRoomIds.Contains(r.Id.GetIdValue()))
                .ToList();

            var existingNumberLookup = new Dictionary<string, List<(IdType Id, string LevelName, bool SameLevel)>>();
            foreach (Room room in otherRooms)
            {
                Parameter param = ResolveRoomNumberParameter(room, parameterName);
                string value = param?.AsString();
                if (string.IsNullOrEmpty(value))
                    continue;

                string roomLevelName = (doc.GetElement(room.LevelId) as Level)?.Name ?? "<unknown>";
                bool sameLevel = room.LevelId == targetLevel.Id;
                if (!existingNumberLookup.TryGetValue(value, out var list))
                {
                    list = new List<(IdType, string, bool)>();
                    existingNumberLookup[value] = list;
                }
                list.Add((room.Id.GetIdValue(), roomLevelName, sameLevel));
            }

            var conflicts = new List<object>();
            foreach (var proposal in proposedRooms)
            {
                if (existingNumberLookup.TryGetValue(proposal.NewNumber, out var clashes))
                {
                    foreach (var clash in clashes)
                    {
                        conflicts.Add(new
                        {
                            ProposedNumber = proposal.NewNumber,
                            RoomId = proposal.Room.Id.GetIdValue(),
                            ConflictingRoomId = clash.Id,
                            ConflictingLevel = clash.LevelName,
                            SameLevel = clash.SameLevel
                        });
                    }
                }
            }

            bool hasBlockingConflicts = conflicts.Count > 0 && !allowExistingNumberConflicts;
            bool willWrite = !dryRun && !hasBlockingConflicts;

            if (willWrite)
            {
                using (Transaction trans = new Transaction(doc, "重新排序編號房間"))
                {
                    trans.Start();
                    try
                    {
                        foreach (var proposal in proposedRooms)
                        {
                            Parameter param = ResolveRoomNumberParameter(proposal.Room, parameterName);
                            if (param == null || param.IsReadOnly)
                            {
                                throw new Exception(
                                    $"Room {proposal.Room.Id.GetIdValue()}（原編號 '{proposal.OldNumber}'）的房號參數不可寫入" +
                                    (string.IsNullOrWhiteSpace(parameterName) ? "" : $" (parameterName='{parameterName}')"));
                            }
                            // Parameter.Set 在值被拒絕時回傳 false 而不丟例外（例如受公式或群組約束）；
                            // 若不檢查回傳值，transaction 仍會 commit、該房間靜默沒被改號，
                            // 違反 domain「任一房間無法寫入則整批回滾」的保證（inspector-ops 發現）。
                            bool applied = param.Set(proposal.NewNumber);
                            if (!applied)
                            {
                                throw new Exception(
                                    $"Room {proposal.Room.Id.GetIdValue()}（原編號 '{proposal.OldNumber}'）寫入新編號 " +
                                    $"'{proposal.NewNumber}' 被 Revit 拒絕（可能受公式或群組約束）");
                            }
                        }
                        trans.Commit();
                    }
                    catch (Exception ex)
                    {
                        if (trans.GetStatus() == TransactionStatus.Started)
                            trans.RollBack();
                        throw new Exception($"寫入房間編號失敗，已整批回滾: {ex.Message}", ex);
                    }
                }
            }

            var roomsOutput = proposedRooms.Select(p => new
            {
                ElementId = p.Room.Id.GetIdValue(),
                OldNumber = p.OldNumber,
                NewNumber = p.NewNumber,
                Name = p.Room.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? "未命名",
                CenterXMm = Math.Round(p.X, 2),
                CenterYMm = Math.Round(p.Y, 2),
                RowIndex = p.RowIndex
            }).ToList();

            string message;
            if (dryRun)
                message = $"Dry-run：{proposedRooms.Count} 個房間預計重新編號，{skippedRooms.Count} 個略過，{conflicts.Count} 個編號衝突。尚未寫入。";
            else if (hasBlockingConflicts)
                message = $"發現 {conflicts.Count} 個編號衝突且 allowExistingNumberConflicts=false，已停止並回報衝突，未寫入任何變更。";
            else
                message = $"已寫入 {proposedRooms.Count} 個房間的新編號（{skippedRooms.Count} 個略過，{conflicts.Count} 個已允許的衝突）。";

            return new
            {
                Level = targetLevel.Name,
                LevelId = targetLevel.Id.GetIdValue(),
                Count = proposedRooms.Count,
                StartNumber = proposedRooms.Count > 0 ? proposedRooms[0].NewNumber : FormatRoomNumber(seed.Prefix, seed.Value, seed.Width),
                EndNumber = proposedRooms.Count > 0 ? proposedRooms[proposedRooms.Count - 1].NewNumber : null,
                DryRun = dryRun,
                Written = willWrite,
                Rooms = roomsOutput,
                SkippedRooms = skippedRooms,
                Conflicts = conflicts,
                Message = message
            };
        }

        #endregion
    }
}
