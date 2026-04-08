using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
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
        #region 圖紙管理

        /// <summary>
        /// 取得所有圖紙
        /// </summary>
        private object GetAllSheets()
        {
            Document doc = _uiApp.ActiveUIDocument.Document;

            var sheets = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .Select(s => new
                {
                    ElementId = s.Id.GetIdValue(),
                    SheetNumber = s.SheetNumber,
                    SheetName = s.Name
                })
                .OrderBy(s => s.SheetNumber)
                .ToList();

            return new
            {
                Count = sheets.Count,
                Sheets = sheets
            };
        }

        /// <summary>
        /// 取得圖框類型
        /// </summary>
        private object GetTitleBlocks()
        {
            Document doc = _uiApp.ActiveUIDocument.Document;

            var titleBlocks = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                .Cast<FamilySymbol>()
                .Select(fs => new
                {
                    ElementId = fs.Id.GetIdValue(),
                    Name = fs.Name,
                    FamilyName = fs.FamilyName
                })
                .OrderBy(t => t.FamilyName)
                .ThenBy(t => t.Name)
                .ToList();

            return new
            {
                Count = titleBlocks.Count,
                TitleBlocks = titleBlocks
            };
        }

        /// <summary>
        /// 批次建立圖紙
        /// </summary>
        private object CreateSheets(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;
            IdType titleBlockId = parameters["titleBlockId"]?.Value<IdType>() ?? 0;
            var sheetsArray = parameters["sheets"] as JArray;

            if (titleBlockId == 0)
                throw new Exception("請提供圖框類型 ID (titleBlockId)");

            if (sheetsArray == null || sheetsArray.Count == 0)
                throw new Exception("請提供要建立的圖紙清單 (sheets)");

            ElementId tbId = titleBlockId.ToElementId();
            Element tbType = doc.GetElement(tbId);
            if (tbType == null)
                throw new Exception($"找不到圖框類型 ID: {titleBlockId}");

            List<object> results = new List<object>();

            using (Transaction trans = new Transaction(doc, "批次建立圖紙"))
            {
                trans.Start();

                foreach (var s in sheetsArray)
                {
                    string number = s["number"]?.Value<string>();
                    string name = s["name"]?.Value<string>();

                    try
                    {
                        ViewSheet sheet = ViewSheet.Create(doc, tbId);
                        if (!string.IsNullOrEmpty(number))
                            sheet.SheetNumber = number;
                        if (!string.IsNullOrEmpty(name))
                            sheet.Name = name;

                        results.Add(new
                        {
                            ElementId = sheet.Id.GetIdValue(),
                            SheetNumber = sheet.SheetNumber,
                            SheetName = sheet.Name,
                            Success = true
                        });
                    }
                    catch (Exception ex)
                    {
                        results.Add(new
                        {
                            SheetNumber = number,
                            SheetName = name,
                            Success = false,
                            Error = ex.Message
                        });
                    }
                }

                trans.Commit();
            }

            return new
            {
                Total = sheetsArray.Count,
                Results = results
            };
        }

        /// <summary>
        /// 取得視埠與圖紙的對應表
        /// </summary>
        private object GetViewportMap()
        {
            Document doc = _uiApp.ActiveUIDocument.Document;
            var result = new List<object>();

            var sheets = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .ToList();

            foreach (var sheet in sheets)
            {
                var vportIds = sheet.GetAllViewports();
                foreach (var vpId in vportIds)
                {
                    var vp = doc.GetElement(vpId) as Viewport;
                    if (vp != null)
                    {
                        var view = doc.GetElement(vp.ViewId) as View;
                        result.Add(new
                        {
                            SheetId = sheet.Id.GetIdValue(),
                            SheetNumber = sheet.SheetNumber,
                            SheetName = sheet.Name,
                            ViewportId = vp.Id.GetIdValue(),
                            ViewId = vp.ViewId.GetIdValue(),
                            ViewName = view?.Name ?? "Unknown",
                            ViewType = view?.ViewType.ToString() ?? "Unknown"
                        });
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// 自動修正圖紙編號 (掃描 -1 後綴並合併)
        /// </summary>
        private object AutoRenumberSheets(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;

            // Phase 0: Emergency Recovery (Fix _MCPFIX)
            var fixSheets = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .Where(s => s.SheetNumber.EndsWith("_MCPFIX"))
                .ToList();

            if (fixSheets.Count > 0)
            {
                using (Transaction tFix = new Transaction(doc, "還原_MCPFIX"))
                {
                    tFix.Start();
                    foreach (var s in fixSheets)
                    {
                        string original = s.SheetNumber.Replace("_MCPFIX", "");
                        try { s.SheetNumber = original; }
                        catch (Exception ex) { Logger.Error($"還原 _MCPFIX 失敗: {ex.Message}"); }
                    }
                    tFix.Commit();
                }
            }

            // 1. 重新掃描所有圖紙
            var allSheets = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .ToList();

            var insertSheets = allSheets
                .Where(s => s.SheetNumber.EndsWith("-1"))
                .OrderBy(s => s.SheetNumber)
                .ToList();

            if (insertSheets.Count == 0)
                return new { Success = true, Message = "專案中沒有發現帶有 '-1' 後綴的圖紙" };

            var sheetMap = allSheets.ToDictionary(s => s.SheetNumber, s => s.Id.GetIdValue());
            Dictionary<IdType, string> finalMoves = new Dictionary<IdType, string>();
            Dictionary<string, IdType> reservationMap = new Dictionary<string, IdType>(sheetMap.Count);
            foreach (var kvp in sheetMap) reservationMap[kvp.Key] = kvp.Value;

            int processedInsertions = 0;

            // 2. 計算變更 (模擬過程)
            foreach (var sourceSheet in insertSheets)
            {
                IdType sourceId = sourceSheet.Id.GetIdValue();
                string sourceNumber = sourceSheet.SheetNumber;
                string baseNumber = sourceNumber.Substring(0, sourceNumber.Length - 2);
                string targetNumber = IncrementString(baseNumber);

                string currentMoverNumber = targetNumber;
                IdType currentMoverId = sourceId;

                while (true)
                {
                    if (reservationMap.ContainsKey(currentMoverNumber))
                    {
                        IdType occupierId = reservationMap[currentMoverNumber];
                        if (occupierId == currentMoverId) break;

                        finalMoves[currentMoverId] = currentMoverNumber;
                        reservationMap[currentMoverNumber] = currentMoverId;
                        currentMoverId = occupierId;
                        currentMoverNumber = IncrementString(currentMoverNumber);
                    }
                    else
                    {
                        finalMoves[currentMoverId] = currentMoverNumber;
                        reservationMap[currentMoverNumber] = currentMoverId;
                        break;
                    }

                    if (finalMoves.Count > 2000) break;
                }

                processedInsertions++;
            }

            // 3. 執行變更
            finalMoves = OptimizeSheetOrder(doc, finalMoves);

            int changedCount = 0;
            if (finalMoves.Count > 0)
            {
                using (TransactionGroup tg = new TransactionGroup(doc, "自動圖紙編號修正"))
                {
                    tg.Start();

                    using (Transaction t1 = new Transaction(doc, "Step1:暫存"))
                    {
                        t1.Start();
                        foreach (var id in finalMoves.Keys)
                        {
                            Element elem = doc.GetElement(id.ToElementId());
                            if (elem != null)
                            {
                                Parameter p = elem.get_Parameter(BuiltInParameter.SHEET_NUMBER);
                                if (p != null) p.Set(p.AsString() + "_TEMP_" + Guid.NewGuid().ToString().Substring(0, 5));
                            }
                        }
                        t1.Commit();
                    }

                    using (Transaction t2 = new Transaction(doc, "Step2:最終"))
                    {
                        t2.Start();
                        foreach (var kvp in finalMoves)
                        {
                            Element elem = doc.GetElement(kvp.Key.ToElementId());
                            if (elem != null)
                            {
                                Parameter p = elem.get_Parameter(BuiltInParameter.SHEET_NUMBER);
                                if (p != null) p.Set(kvp.Value);
                                changedCount++;
                            }
                        }
                        t2.Commit();
                    }

                    tg.Assimilate();
                }
            }

            return new
            {
                Success = true,
                ChangedCount = changedCount,
                InsertionsResolved = processedInsertions,
                Message = $"修復並更新完成：處理了 {processedInsertions} 張插入圖紙，共更新 {changedCount} 個編號"
            };
        }

        private List<string> GenerateSequence(string start, int count)
        {
            List<string> result = new List<string> { start };
            string current = start;
            for (int i = 1; i < count; i++)
            {
                current = IncrementString(current);
                result.Add(current);
            }
            return result;
        }

        private string IncrementString(string input)
        {
            var match = System.Text.RegularExpressions.Regex.Match(input, @"(.*?)([0-9]+)$");
            if (match.Success)
            {
                string prefix = match.Groups[1].Value;
                string numberStr = match.Groups[2].Value;
                long number = long.Parse(numberStr) + 1;
                return prefix + number.ToString().PadLeft(numberStr.Length, '0');
            }
            return input + "-1";
        }

        private Dictionary<IdType, string> OptimizeSheetOrder(Document doc, Dictionary<IdType, string> moves)
        {
            var participants = new List<SheetSortInfo>();
            foreach (var kvp in moves)
            {
                Element elem = doc.GetElement(kvp.Key.ToElementId());
                if (elem != null && elem is ViewSheet sheet)
                {
                    participants.Add(new SheetSortInfo { ID = kvp.Key, Name = sheet.Name, TargetNumber = kvp.Value });
                }
            }

            var regex = new System.Text.RegularExpressions.Regex(@"^(.*?)[\(\（]([\d一二三四五六七八九十]+)(?:/[\d]+)?[\)\）]$");
            var matched = participants
                .Select(p => { var m = regex.Match(p.Name); return new { Data = p, Match = m }; })
                .Where(x => x.Match.Success)
                .Select(x => new SheetMatchItem
                {
                    Data = x.Data,
                    BaseName = x.Match.Groups[1].Value.Trim(),
                    MatchIndex = GetSheetNameIndex(x.Match.Groups[2].Value)
                })
                .ToList();

            var groups = matched.GroupBy(x => x.BaseName).ToList();
            var newMoves = new Dictionary<IdType, string>(moves);

            foreach (var grp in groups)
            {
                var items = grp.ToList();
                if (items.Count < 2) continue;

                items.Sort((a, b) => string.Compare(a.Data.TargetNumber, b.Data.TargetNumber, StringComparison.Ordinal));

                var subGroups = new List<List<SheetMatchItem>>();
                var currentSubGroup = new List<SheetMatchItem> { items[0] };

                for (int i = 1; i < items.Count; i++)
                {
                    long prevNum = ExtractTrailingNumber(items[i - 1].Data.TargetNumber);
                    long currNum = ExtractTrailingNumber(items[i].Data.TargetNumber);

                    if (currNum - prevNum <= 3)
                        currentSubGroup.Add(items[i]);
                    else
                    {
                        subGroups.Add(currentSubGroup);
                        currentSubGroup = new List<SheetMatchItem> { items[i] };
                    }
                }
                subGroups.Add(currentSubGroup);

                foreach (var subGrp in subGroups)
                {
                    if (subGrp.Count < 2) continue;
                    var targetNumbers = subGrp.Select(x => x.Data.TargetNumber).OrderBy(n => n).ToList();
                    var sortedSheets = subGrp.OrderBy(x => x.MatchIndex).ToList();
                    for (int i = 0; i < sortedSheets.Count; i++)
                    {
                        if (i < targetNumbers.Count)
                            newMoves[sortedSheets[i].Data.ID] = targetNumbers[i];
                    }
                }
            }

            return newMoves;
        }

        private long ExtractTrailingNumber(string input)
        {
            var match = System.Text.RegularExpressions.Regex.Match(input, @"(\d+)$");
            if (match.Success) return long.Parse(match.Groups[1].Value);
            return 0;
        }

        private int GetSheetNameIndex(string val)
        {
            if (int.TryParse(val, out int n)) return n;
            switch (val)
            {
                case "一": return 1; case "二": return 2; case "三": return 3;
                case "四": return 4; case "五": return 5; case "六": return 6;
                case "七": return 7; case "八": return 8; case "九": return 9;
                case "十": return 10; default: return 999;
            }
        }

        private class SheetSortInfo
        {
            public IdType ID { get; set; }
            public string Name { get; set; }
            public string TargetNumber { get; set; }
        }

        private class SheetMatchItem
        {
            public SheetSortInfo Data { get; set; }
            public string BaseName { get; set; }
            public int MatchIndex { get; set; }
        }

        #endregion
    }
}
