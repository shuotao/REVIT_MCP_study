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
    /// 房間批次修改命令（v2 - warning-safe）
    /// 用途：依房間名稱/用途分組，批次設定 Room 的 Upper Limit 與 Limit Offset
    /// - 不動樓層，只改 Room 參數
    /// - Transaction 上註冊 WarningSwallower，自動吞掉「A group has been changed outside
    ///   group edit mode」與其他邊界警告，變更仍會被 Revit 正確 commit
    /// - 移除 Area > 0 過濾：Area=0 的未封閉 Room 仍需被納入（其 Upper Limit/Offset 參數有效）
    /// - summaryOnly 控制回傳 payload 大小（500+ room 時避免超過 token 上限）
    ///
    /// Note: Revit API 沒有公開 Document.EditGroup/EndEditGroup，那是 UI-only command。
    /// 對於單一 instance 的 GroupType（本案情境），修改任一成員 == 修改 Type 本身，
    /// 效果等同 EditGroup 流程，只會多一個被 WarningSwallower 吞掉的警告。
    /// 對於多 instance 的 GroupType，Revit 會自動同步到所有 instance（這是 Group 本質）。
    /// </summary>
    public partial class CommandExecutor
    {
        #region 批次修改房間高度

        private const double FEET_TO_MM = 304.8;
        private const double MIN_HEIGHT_MM = 1.0;
        private const double MAX_HEIGHT_MM = 10000.0;

        /// <summary>
        /// 批次依房間名稱/用途分組，設定 Room 的 Upper Limit (ROOM_UPPER_LEVEL) 與
        /// Limit Offset (ROOM_UPPER_OFFSET)。
        /// </summary>
        private object BatchSetRoomHeight(JObject parameters)
        {
            var groupsArray = parameters["groups"] as JArray;
            string levelName = parameters["levelName"]?.Value<string>();
            string matchField = (parameters["matchField"]?.Value<string>() ?? "name").ToLowerInvariant();
            bool summaryOnly = parameters["summaryOnly"]?.Value<bool?>() ?? true;

            if (groupsArray == null || groupsArray.Count == 0)
                throw new Exception("必須提供至少一個 group (nameMatch + heightMm)");

            if (matchField != "name" && matchField != "department")
                throw new Exception("matchField 必須為 'name' 或 'department'");

            Document doc = _uiApp.ActiveUIDocument.Document;

            // 1. 收集全部 Room（不再過濾 Area > 0；未封閉的 Room 也要能更新參數）
            var collector = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType()
                .Cast<Room>();

            List<Room> candidateRooms;
            if (!string.IsNullOrEmpty(levelName))
            {
                Level levelFilter = FindLevel(doc, levelName, false);
                candidateRooms = collector.Where(r => r.LevelId == levelFilter.Id).ToList();
            }
            else
            {
                candidateRooms = collector.ToList();
            }

            if (candidateRooms.Count == 0)
                throw new Exception(string.IsNullOrEmpty(levelName)
                    ? "專案中找不到任何 Room"
                    : $"樓層 '{levelName}' 找不到任何 Room");

            // 2. 對每個 group 規則預先比對出 target rooms
            var perGroupMatched = new List<(string name, double heightMm, Level fixedUpperLevel, List<Room> rooms)>();
            var errors = new List<string>();

            foreach (var groupToken in groupsArray)
            {
                string nameMatch = groupToken["nameMatch"]?.Value<string>();
                double? heightMm = groupToken["heightMm"]?.Value<double?>();
                string upperLevelName = groupToken["upperLevelName"]?.Value<string>();

                if (string.IsNullOrEmpty(nameMatch))
                {
                    errors.Add("group 缺少 nameMatch 欄位，已略過");
                    continue;
                }
                if (!heightMm.HasValue)
                {
                    errors.Add($"group '{nameMatch}' 缺少 heightMm 欄位，已略過");
                    continue;
                }
                if (heightMm.Value < MIN_HEIGHT_MM || heightMm.Value > MAX_HEIGHT_MM)
                {
                    errors.Add($"group '{nameMatch}' 的 heightMm={heightMm.Value} 超出合理範圍 ({MIN_HEIGHT_MM}~{MAX_HEIGHT_MM} mm)，已略過");
                    continue;
                }

                Level fixedUpperLevel = null;
                if (!string.IsNullOrEmpty(upperLevelName))
                {
                    try { fixedUpperLevel = FindLevel(doc, upperLevelName, false); }
                    catch (Exception ex)
                    {
                        errors.Add($"group '{nameMatch}' 的 upperLevelName='{upperLevelName}' 無法解析: {ex.Message}，已略過");
                        continue;
                    }
                }

                string nameMatchLower = nameMatch.ToLowerInvariant();
                var matchedRooms = candidateRooms.Where(r =>
                {
                    string candidate;
                    if (matchField == "department")
                        candidate = r.get_Parameter(BuiltInParameter.ROOM_DEPARTMENT)?.AsString();
                    else
                        candidate = r.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString();

                    return !string.IsNullOrEmpty(candidate)
                        && candidate.ToLowerInvariant().Contains(nameMatchLower);
                }).ToList();

                perGroupMatched.Add((nameMatch, heightMm.Value, fixedUpperLevel, matchedRooms));
            }

            // 3. 單一 Transaction + WarningSwallower
            int successCount = 0;
            int requestedCount = 0;
            int groupedRoomCount = 0;
            int ungroupedRoomCount = 0;
            var groupResults = new List<object>();
            var originalValues = new List<object>();
            var modifications = new List<object>();

            using (Transaction trans = new Transaction(doc, "批次修改房間上限高度"))
            {
                trans.Start();

                // 註冊 WarningSwallower：吞掉所有 Warning（含「modified outside group edit mode」）
                var opts = trans.GetFailureHandlingOptions();
                opts.SetFailuresPreprocessor(new WarningSwallower());
                trans.SetFailureHandlingOptions(opts);

                foreach (var gm in perGroupMatched)
                {
                    requestedCount += gm.rooms.Count;

                    int groupSuccess = 0;
                    int grpCount = 0;
                    int ungrpCount = 0;

                    foreach (var room in gm.rooms)
                    {
                        bool isGrouped = room.GroupId != ElementId.InvalidElementId;
                        if (isGrouped) grpCount++;
                        else ungrpCount++;

                        if (TryModifyRoom(doc, room, gm.heightMm, gm.fixedUpperLevel,
                                          errors, summaryOnly, originalValues, modifications, gm.name))
                        {
                            groupSuccess++;
                            successCount++;
                        }
                    }

                    groupedRoomCount += grpCount;
                    ungroupedRoomCount += ungrpCount;

                    groupResults.Add(new
                    {
                        NameMatch = gm.name,
                        HeightMm = gm.heightMm,
                        UpperLevel = gm.fixedUpperLevel?.Name ?? "<Room Base Level>",
                        MatchedCount = gm.rooms.Count,
                        ModifiedCount = groupSuccess,
                        GroupedCount = grpCount,
                        UngroupedCount = ungrpCount
                    });
                }

                trans.Commit();
            }

            // 4. 組 response
            string msg = $"已修改 {successCount} 個 Room 的上限高度（{groupsArray.Count} 組規則，{groupedRoomCount} 個在 Model Group 內、{ungroupedRoomCount} 個未分組，{errors.Count} 個錯誤）。Warning 已自動吞掉。按 Ctrl+Z 可還原整批。";

            if (summaryOnly)
            {
                return new
                {
                    Success = true,
                    GroupCount = groupsArray.Count,
                    CandidateRoomCount = candidateRooms.Count,
                    RequestedCount = requestedCount,
                    ModifiedCount = successCount,
                    ErrorCount = errors.Count,
                    Errors = errors,
                    Groups = groupResults,
                    GroupedRoomCount = groupedRoomCount,
                    UngroupedRoomCount = ungroupedRoomCount,
                    Message = msg
                };
            }
            else
            {
                return new
                {
                    Success = true,
                    GroupCount = groupsArray.Count,
                    CandidateRoomCount = candidateRooms.Count,
                    RequestedCount = requestedCount,
                    ModifiedCount = successCount,
                    ErrorCount = errors.Count,
                    Errors = errors,
                    Groups = groupResults,
                    GroupedRoomCount = groupedRoomCount,
                    UngroupedRoomCount = ungroupedRoomCount,
                    OriginalValues = originalValues,
                    Modifications = modifications,
                    Message = msg
                };
            }
        }

        /// <summary>
        /// 修改單一 Room 的 Upper Limit + Limit Offset，成功回 true。
        /// 失敗會把訊息加入 errors list。summaryOnly=false 時額外塞進 originalValues/modifications。
        /// </summary>
        private bool TryModifyRoom(
            Document doc, Room room, double heightMm, Level fixedUpperLevel,
            List<string> errors, bool summaryOnly,
            List<object> originalValues, List<object> modifications, string groupName)
        {
            try
            {
                Parameter upperLevelParam = room.get_Parameter(BuiltInParameter.ROOM_UPPER_LEVEL);
                Parameter upperOffsetParam = room.get_Parameter(BuiltInParameter.ROOM_UPPER_OFFSET);

                if (upperLevelParam == null || upperLevelParam.IsReadOnly
                    || upperOffsetParam == null || upperOffsetParam.IsReadOnly)
                {
                    errors.Add($"Room {room.Number} '{room.Name}' 的 Upper Limit/Offset 參數無法寫入");
                    return false;
                }

                // 舊值
                ElementId origUpperLevelId = upperLevelParam.AsElementId();
                double origUpperOffsetFt = upperOffsetParam.AsDouble();
                Level origUpperLevel = doc.GetElement(origUpperLevelId) as Level;

                if (!summaryOnly)
                {
                    originalValues.Add(new
                    {
                        ElementId = room.Id.GetIdValue(),
                        Name = room.Name,
                        Number = room.Number,
                        OriginalUpperLevel = origUpperLevel?.Name ?? "<none>",
                        OriginalUpperOffsetMm = Math.Round(origUpperOffsetFt * FEET_TO_MM, 2)
                    });
                }

                Level targetUpperLevel = fixedUpperLevel ?? (doc.GetElement(room.LevelId) as Level);
                if (targetUpperLevel == null)
                {
                    errors.Add($"Room {room.Number} '{room.Name}' 無法取得 Base Level");
                    return false;
                }

                upperLevelParam.Set(targetUpperLevel.Id);
                upperOffsetParam.Set(heightMm / FEET_TO_MM);

                if (!summaryOnly)
                {
                    modifications.Add(new
                    {
                        ElementId = room.Id.GetIdValue(),
                        Name = room.Name,
                        Number = room.Number,
                        NewUpperLevel = targetUpperLevel.Name,
                        NewUpperOffsetMm = heightMm,
                        Group = groupName
                    });
                }
                return true;
            }
            catch (Exception ex)
            {
                errors.Add($"Room {room.Number} '{room.Name}': {ex.Message}");
                return false;
            }
        }

        #endregion
    }
}
