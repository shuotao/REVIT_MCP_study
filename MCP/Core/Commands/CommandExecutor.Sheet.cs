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

        /// <summary>
        /// 取得指定圖紙上所有視埠的詳細資訊（中心點、邊界框、尺寸）
        /// </summary>
        private object GetSheetViewportDetails(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;
            IdType sheetId = parameters?["sheetId"]?.Value<IdType>() ?? 0;

            ViewSheet sheet;
            if (sheetId != 0)
            {
                sheet = doc.GetElement(sheetId.ToElementId()) as ViewSheet;
                if (sheet == null)
                    throw new Exception($"找不到圖紙 ID: {sheetId}");
            }
            else
            {
                View activeView = _uiApp.ActiveUIDocument.ActiveView;
                sheet = activeView as ViewSheet;
                if (sheet == null)
                    throw new Exception($"目前的作用視圖不是圖紙 (ViewSheet)，而是 {activeView.ViewType}。請指定 sheetId 或切換至圖紙視圖。");
            }

            var vpIds = sheet.GetAllViewports();
            var viewports = new List<object>();

            foreach (var vpId in vpIds)
            {
                var vp = doc.GetElement(vpId) as Viewport;
                if (vp == null) continue;

                var view = doc.GetElement(vp.ViewId) as View;
                XYZ center = vp.GetBoxCenter();
                Outline outline = vp.GetBoxOutline();
                XYZ min = outline.MinimumPoint;
                XYZ max = outline.MaximumPoint;

                double widthFt = max.X - min.X;
                double heightFt = max.Y - min.Y;

                viewports.Add(new
                {
                    ViewportId = vp.Id.GetIdValue(),
                    ViewId = vp.ViewId.GetIdValue(),
                    ViewName = view?.Name ?? "Unknown",
                    ViewType = view?.ViewType.ToString() ?? "Unknown",
                    Center = new
                    {
                        X = Math.Round(center.X * 304.8, 2),
                        Y = Math.Round(center.Y * 304.8, 2)
                    },
                    Outline = new
                    {
                        MinX = Math.Round(min.X * 304.8, 2),
                        MinY = Math.Round(min.Y * 304.8, 2),
                        MaxX = Math.Round(max.X * 304.8, 2),
                        MaxY = Math.Round(max.Y * 304.8, 2)
                    },
                    WidthMm = Math.Round(widthFt * 304.8, 2),
                    HeightMm = Math.Round(heightFt * 304.8, 2)
                });
            }

            return new
            {
                SheetId = sheet.Id.GetIdValue(),
                SheetNumber = sheet.SheetNumber,
                SheetName = sheet.Name,
                ViewportCount = viewports.Count,
                Viewports = viewports
            };
        }

        /// <summary>
        /// 依指定順序排列圖紙上的視埠（支援 viewportIds 或 viewNames）
        /// </summary>
        private object ArrangeViewportsOnSheet(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;
            var viewportIdsArray = parameters?["viewportIds"] as JArray;
            var viewNamesArray = parameters?["viewNames"] as JArray;
            string direction = parameters?["direction"]?.Value<string>() ?? "horizontal";
            double gapMm = parameters?["gapMm"]?.Value<double>() ?? 0;
            string alignY = parameters?["alignY"]?.Value<string>() ?? "center";
            IdType sheetId = parameters?["sheetId"]?.Value<IdType>() ?? 0;

            if ((viewportIdsArray == null || viewportIdsArray.Count == 0) &&
                (viewNamesArray == null || viewNamesArray.Count == 0))
                throw new Exception("請提供 viewportIds 或 viewNames（至少一項）");

            direction = direction.ToLower();
            if (direction != "horizontal" && direction != "vertical")
                throw new Exception($"direction 必須是 'horizontal' 或 'vertical'，收到: '{direction}'");

            alignY = alignY.ToLower();

            // Resolve sheet
            ViewSheet sheet;
            if (sheetId != 0)
            {
                sheet = doc.GetElement(sheetId.ToElementId()) as ViewSheet;
                if (sheet == null)
                    throw new Exception($"找不到圖紙 ID: {sheetId}");
            }
            else
            {
                View activeView = _uiApp.ActiveUIDocument.ActiveView;
                sheet = activeView as ViewSheet;
                if (sheet == null)
                    throw new Exception($"目前的作用視圖不是圖紙 (ViewSheet)，而是 {activeView.ViewType}。請指定 sheetId 或切換至圖紙視圖。");
            }

            double gapFt = gapMm / 304.8;
            var warnings = new List<string>();
            int placedCount = 0;

            // Build ordered viewport list
            var orderedViewports = new List<Viewport>();

            if (viewNamesArray != null && viewNamesArray.Count > 0)
            {
                // Resolve by view name: build name->viewport map from sheet
                var vpIds = sheet.GetAllViewports();
                var nameMap = new Dictionary<string, Viewport>(StringComparer.OrdinalIgnoreCase);
                foreach (var vpId in vpIds)
                {
                    var vp = doc.GetElement(vpId) as Viewport;
                    if (vp == null) continue;
                    var view = doc.GetElement(vp.ViewId) as View;
                    if (view == null) continue;

                    if (view.ViewType != ViewType.DraftingView)
                        continue;

                    if (!nameMap.ContainsKey(view.Name))
                        nameMap[view.Name] = vp;
                }

                // Build project-wide DraftingView name->View map for auto-place
                var allDraftingViews = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewDrafting))
                    .Cast<ViewDrafting>()
                    .ToDictionary(v => v.Name, v => v, StringComparer.OrdinalIgnoreCase);

                // Phase 1: Auto-place views not yet on this sheet (requires transaction)
                var namesToPlace = new List<string>();
                foreach (var nameToken in viewNamesArray)
                {
                    string name = nameToken.Value<string>();
                    if (!nameMap.ContainsKey(name) && allDraftingViews.ContainsKey(name))
                        namesToPlace.Add(name);
                }

                if (namesToPlace.Count > 0)
                {
                    using (Transaction tPlace = new Transaction(doc, "放置視圖到圖紙"))
                    {
                        tPlace.Start();
                        XYZ tempCenter = new XYZ(0, 0, 0);
                        foreach (var name in namesToPlace)
                        {
                            var draftView = allDraftingViews[name];
                            try
                            {
                                Viewport newVp = Viewport.Create(doc, sheet.Id, draftView.Id, tempCenter);
                                nameMap[name] = newVp;
                                placedCount++;
                            }
                            catch (Exception ex)
                            {
                                warnings.Add($"無法將 '{name}' 放到圖紙上: {ex.Message}");
                            }
                        }
                        tPlace.Commit();
                    }
                }

                // Phase 2: Build ordered list
                foreach (var nameToken in viewNamesArray)
                {
                    string name = nameToken.Value<string>();
                    if (nameMap.TryGetValue(name, out Viewport matchedVp))
                    {
                        orderedViewports.Add(matchedVp);
                    }
                    else if (!allDraftingViews.ContainsKey(name))
                    {
                        warnings.Add($"專案中找不到名為 '{name}' 的 DraftingView");
                    }
                }
            }
            else
            {
                // Resolve by viewport IDs
                foreach (var idToken in viewportIdsArray)
                {
                    IdType vpIdVal = idToken.Value<IdType>();
                    var vp = doc.GetElement(vpIdVal.ToElementId()) as Viewport;
                    if (vp == null)
                        throw new Exception($"找不到視埠 ID: {vpIdVal}");

                    var vpSheet = doc.GetElement(vp.SheetId) as ViewSheet;
                    if (vpSheet == null || vpSheet.Id.GetIdValue() != sheet.Id.GetIdValue())
                        throw new Exception($"視埠 {vpIdVal} 不在圖紙 {sheet.SheetNumber} 上");

                    orderedViewports.Add(vp);
                }
            }

            if (orderedViewports.Count < 2)
                throw new Exception($"有效的視埠數量不足（需至少 2 個，實際 {orderedViewports.Count} 個）");

            // Collect outline data
            var vpDataList = orderedViewports.Select(vp =>
            {
                Outline outline = vp.GetBoxOutline();
                double w = outline.MaximumPoint.X - outline.MinimumPoint.X;
                double h = outline.MaximumPoint.Y - outline.MinimumPoint.Y;
                return new { Vp = vp, Outline = outline, Width = w, Height = h };
            }).ToList();

            // Anchor: first viewport's current left edge
            XYZ firstCenter = vpDataList[0].Vp.GetBoxCenter();
            Outline firstOutline = vpDataList[0].Outline;

            var results = new List<object>();

            using (Transaction trans = new Transaction(doc, "排列視埠"))
            {
                trans.Start();

                if (direction == "horizontal")
                {
                    double currentLeftX = firstOutline.MinimumPoint.X;

                    double refY;
                    if (alignY == "top")
                        refY = firstOutline.MaximumPoint.Y;
                    else if (alignY == "bottom")
                        refY = firstOutline.MinimumPoint.Y;
                    else
                        refY = firstCenter.Y;

                    for (int i = 0; i < vpDataList.Count; i++)
                    {
                        var d = vpDataList[i];
                        double newCenterX = currentLeftX + d.Width / 2.0;

                        double newCenterY;
                        if (alignY == "top")
                            newCenterY = refY - d.Height / 2.0;
                        else if (alignY == "bottom")
                            newCenterY = refY + d.Height / 2.0;
                        else
                            newCenterY = refY;

                        d.Vp.SetBoxCenter(new XYZ(newCenterX, newCenterY, 0));

                        var view = doc.GetElement(d.Vp.ViewId) as View;
                        results.Add(new
                        {
                            ViewportId = d.Vp.Id.GetIdValue(),
                            ViewName = view?.Name ?? "Unknown",
                            NewCenter = new
                            {
                                X = Math.Round(newCenterX * 304.8, 2),
                                Y = Math.Round(newCenterY * 304.8, 2)
                            },
                            WidthMm = Math.Round(d.Width * 304.8, 2),
                            HeightMm = Math.Round(d.Height * 304.8, 2)
                        });

                        currentLeftX += d.Width + gapFt;
                    }
                }
                else // vertical
                {
                    double currentTopY = firstOutline.MaximumPoint.Y;
                    double refX = firstCenter.X;

                    for (int i = 0; i < vpDataList.Count; i++)
                    {
                        var d = vpDataList[i];
                        double newCenterY = currentTopY - d.Height / 2.0;
                        double newCenterX = refX;

                        d.Vp.SetBoxCenter(new XYZ(newCenterX, newCenterY, 0));

                        var view = doc.GetElement(d.Vp.ViewId) as View;
                        results.Add(new
                        {
                            ViewportId = d.Vp.Id.GetIdValue(),
                            ViewName = view?.Name ?? "Unknown",
                            NewCenter = new
                            {
                                X = Math.Round(newCenterX * 304.8, 2),
                                Y = Math.Round(newCenterY * 304.8, 2)
                            },
                            WidthMm = Math.Round(d.Width * 304.8, 2),
                            HeightMm = Math.Round(d.Height * 304.8, 2)
                        });

                        currentTopY -= d.Height + gapFt;
                    }
                }

                trans.Commit();
            }

            double totalWidth, totalHeight;
            if (direction == "horizontal")
            {
                totalWidth = vpDataList.Sum(d => d.Width) + gapFt * (vpDataList.Count - 1);
                totalHeight = vpDataList.Max(d => d.Height);
            }
            else
            {
                totalWidth = vpDataList.Max(d => d.Width);
                totalHeight = vpDataList.Sum(d => d.Height) + gapFt * (vpDataList.Count - 1);
            }

            return new
            {
                Success = true,
                Direction = direction,
                GapMm = gapMm,
                Alignment = alignY,
                TotalWidthMm = Math.Round(totalWidth * 304.8, 2),
                TotalHeightMm = Math.Round(totalHeight * 304.8, 2),
                PlacedCount = placedCount,
                ArrangedCount = results.Count,
                ArrangedViewports = results,
                Warnings = warnings
            };
        }

        /// <summary>
        /// 縮放 DraftingView 中表格的寬度（僅 X 軸），高度不變
        /// </summary>
        private object ScaleDraftingViewWidth(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;
            double scaleFactor = parameters?["scaleFactor"]?.Value<double>() ?? 0.9;
            IdType sheetId = parameters?["sheetId"]?.Value<IdType>() ?? 0;
            var viewNamesArray = parameters?["viewNames"] as JArray;

            if (scaleFactor <= 0 || scaleFactor > 5)
                throw new Exception($"scaleFactor 必須在 0~5 之間，收到: {scaleFactor}");

            // Resolve sheet
            ViewSheet sheet;
            if (sheetId != 0)
            {
                sheet = doc.GetElement(sheetId.ToElementId()) as ViewSheet;
                if (sheet == null)
                    throw new Exception($"找不到圖紙 ID: {sheetId}");
            }
            else
            {
                View activeView = _uiApp.ActiveUIDocument.ActiveView;
                sheet = activeView as ViewSheet;
                if (sheet == null)
                    throw new Exception("目前的作用視圖不是圖紙 (ViewSheet)，請指定 sheetId 或切換至圖紙視圖。");
            }

            // Get target DraftingViews from sheet's viewports
            var vpIds = sheet.GetAllViewports();
            var targetViews = new List<ViewDrafting>();
            var nameFilter = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (viewNamesArray != null)
                foreach (var n in viewNamesArray)
                    nameFilter.Add(n.Value<string>());

            foreach (var vpId in vpIds)
            {
                var vp = doc.GetElement(vpId) as Viewport;
                if (vp == null) continue;
                var view = doc.GetElement(vp.ViewId) as ViewDrafting;
                if (view == null) continue;

                if (nameFilter.Count > 0 && !nameFilter.Contains(view.Name))
                    continue;

                targetViews.Add(view);
            }

            if (targetViews.Count == 0)
                throw new Exception("目標圖紙上沒有找到符合條件的 DraftingView");

            var results = new List<object>();
            Logger.Info($"[ScaleWidth] 開始縮放 {targetViews.Count} 個 DraftingView，scaleFactor={scaleFactor}");

            for (int i = 0; i < targetViews.Count; i++)
            {
                var view = targetViews[i];
                try
                {
                    // Collect elements before transaction
                    var curveElements = new FilteredElementCollector(doc, view.Id)
                        .OfCategory(BuiltInCategory.OST_Lines)
                        .WhereElementIsNotElementType()
                        .ToList();

                    var textNotes = new FilteredElementCollector(doc, view.Id)
                        .OfClass(typeof(TextNote))
                        .Cast<TextNote>()
                        .ToList();

                    if (curveElements.Count == 0 && textNotes.Count == 0)
                    {
                        results.Add(new { ViewName = view.Name, Scaled = false, Reason = "無可縮放的元素" });
                        continue;
                    }

                    // Find anchor (min X from detail curves = table left edge)
                    double anchorX = double.MaxValue;
                    foreach (var el in curveElements)
                    {
                        var ce = el as CurveElement;
                        if (ce == null) continue;
                        Curve curve = ce.GeometryCurve;
                        if (curve == null) continue;
                        XYZ p0 = curve.GetEndPoint(0);
                        XYZ p1 = curve.GetEndPoint(1);
                        anchorX = Math.Min(anchorX, Math.Min(p0.X, p1.X));
                    }
                    if (anchorX == double.MaxValue)
                    {
                        foreach (var tn in textNotes)
                            anchorX = Math.Min(anchorX, tn.Coord.X);
                    }
                    if (anchorX == double.MaxValue) anchorX = 0;

                    int curveCount = 0, textCount = 0;
                    var viewWarnings = new List<string>();

                    // Per-view transaction to avoid giant commit
                    using (Transaction trans = new Transaction(doc, $"縮放 {view.Name} 寬度"))
                    {
                        trans.Start();

                        // Scale DetailCurves (table grid lines)
                        foreach (var el in curveElements)
                        {
                            var ce = el as CurveElement;
                            if (ce == null) continue;

                            try
                            {
                                Line line = ce.GeometryCurve as Line;
                                if (line == null) continue;

                                XYZ p0 = line.GetEndPoint(0);
                                XYZ p1 = line.GetEndPoint(1);

                                double newX0 = anchorX + (p0.X - anchorX) * scaleFactor;
                                double newX1 = anchorX + (p1.X - anchorX) * scaleFactor;

                                XYZ newP0 = new XYZ(newX0, p0.Y, p0.Z);
                                XYZ newP1 = new XYZ(newX1, p1.Y, p1.Z);

                                if (newP0.DistanceTo(newP1) > 1e-9)
                                {
                                    ce.GeometryCurve = Line.CreateBound(newP0, newP1);
                                    curveCount++;
                                }
                            }
                            catch (Exception ex)
                            {
                                viewWarnings.Add($"Curve {el.Id.GetIdValue()}: {ex.Message}");
                            }
                        }

                        // Scale TextNotes (cell content)
                        foreach (var tn in textNotes)
                        {
                            try
                            {
                                XYZ coord = tn.Coord;
                                double newX = anchorX + (coord.X - anchorX) * scaleFactor;
                                double deltaX = newX - coord.X;

                                if (Math.Abs(deltaX) > 1e-9)
                                    ElementTransformUtils.MoveElement(doc, tn.Id, new XYZ(deltaX, 0, 0));

                                // Scale text wrapping width
                                try { tn.Width = tn.Width * scaleFactor; }
                                catch { /* Width may be read-only for some notes */ }

                                textCount++;
                            }
                            catch (Exception ex)
                            {
                                viewWarnings.Add($"TextNote {tn.Id.GetIdValue()}: {ex.Message}");
                            }
                        }

                        trans.Commit();
                    }

                    Logger.Info($"[ScaleWidth] ({i + 1}/{targetViews.Count}) {view.Name}: {curveCount} curves, {textCount} texts");

                    results.Add(new
                    {
                        ViewName = view.Name,
                        Scaled = true,
                        DetailCurvesModified = curveCount,
                        TextNotesModified = textCount,
                        Warnings = viewWarnings
                    });
                }
                catch (Exception ex)
                {
                    Logger.Error($"[ScaleWidth] {view.Name} 失敗: {ex.Message}");
                    results.Add(new { ViewName = view.Name, Scaled = false, Reason = ex.Message });
                }
            }

            Logger.Info($"[ScaleWidth] 完成，共處理 {results.Count} 個 view");

            return new
            {
                Success = true,
                ScaleFactor = scaleFactor,
                ViewsProcessed = results.Count,
                Results = results
            };
        }

        /// <summary>
        /// 縮放 DraftingView 表格的行高（僅 Y 軸），寬度不變
        /// </summary>
        private object ScaleDraftingViewHeight(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;
            double scaleFactor = parameters?["scaleFactor"]?.Value<double>() ?? 1.1;
            IdType sheetId = parameters?["sheetId"]?.Value<IdType>() ?? 0;
            var viewNamesArray = parameters?["viewNames"] as JArray;

            if (scaleFactor <= 0 || scaleFactor > 5)
                throw new Exception($"scaleFactor 必須在 0~5 之間，收到: {scaleFactor}");

            // Resolve target views: by sheetId, active sheet, or all DraftingViews
            var targetViews = new List<ViewDrafting>();
            var nameFilter = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (viewNamesArray != null)
                foreach (var n in viewNamesArray)
                    nameFilter.Add(n.Value<string>());

            if (sheetId != 0)
            {
                var sheet = doc.GetElement(sheetId.ToElementId()) as ViewSheet;
                if (sheet == null)
                    throw new Exception($"找不到圖紙 ID: {sheetId}");

                foreach (var vpId in sheet.GetAllViewports())
                {
                    var vp = doc.GetElement(vpId) as Viewport;
                    if (vp == null) continue;
                    var view = doc.GetElement(vp.ViewId) as ViewDrafting;
                    if (view == null) continue;
                    if (nameFilter.Count > 0 && !nameFilter.Contains(view.Name)) continue;
                    targetViews.Add(view);
                }
            }
            else if (viewNamesArray != null && viewNamesArray.Count > 0)
            {
                // No sheetId but viewNames given: search all DraftingViews by name
                var allDrafting = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewDrafting))
                    .Cast<ViewDrafting>()
                    .Where(v => nameFilter.Contains(v.Name))
                    .ToList();
                targetViews.AddRange(allDrafting);
            }
            else
            {
                // No sheetId, no viewNames: use active sheet
                View activeView = _uiApp.ActiveUIDocument.ActiveView;
                var sheet = activeView as ViewSheet;
                if (sheet == null)
                    throw new Exception("目前的作用視圖不是圖紙 (ViewSheet)，請指定 sheetId 或 viewNames 或切換至圖紙視圖。");

                foreach (var vpId in sheet.GetAllViewports())
                {
                    var vp = doc.GetElement(vpId) as Viewport;
                    if (vp == null) continue;
                    var view = doc.GetElement(vp.ViewId) as ViewDrafting;
                    if (view == null) continue;
                    targetViews.Add(view);
                }
            }

            if (targetViews.Count == 0)
                throw new Exception("沒有找到符合條件的 DraftingView");

            var results = new List<object>();
            Logger.Info($"[ScaleHeight] 開始縮放 {targetViews.Count} 個 DraftingView，scaleFactor={scaleFactor}");

            for (int i = 0; i < targetViews.Count; i++)
            {
                var view = targetViews[i];
                try
                {
                    var curveElements = new FilteredElementCollector(doc, view.Id)
                        .OfCategory(BuiltInCategory.OST_Lines)
                        .WhereElementIsNotElementType()
                        .ToList();

                    var textNotes = new FilteredElementCollector(doc, view.Id)
                        .OfClass(typeof(TextNote))
                        .Cast<TextNote>()
                        .ToList();

                    if (curveElements.Count == 0 && textNotes.Count == 0)
                    {
                        results.Add(new { ViewName = view.Name, Scaled = false, Reason = "無可縮放的元素" });
                        continue;
                    }

                    // Find anchor (max Y from detail curves = table top edge)
                    double anchorY = double.MinValue;
                    foreach (var el in curveElements)
                    {
                        var ce = el as CurveElement;
                        if (ce == null) continue;
                        Curve curve = ce.GeometryCurve;
                        if (curve == null) continue;
                        XYZ p0 = curve.GetEndPoint(0);
                        XYZ p1 = curve.GetEndPoint(1);
                        anchorY = Math.Max(anchorY, Math.Max(p0.Y, p1.Y));
                    }
                    if (anchorY == double.MinValue)
                    {
                        foreach (var tn in textNotes)
                            anchorY = Math.Max(anchorY, tn.Coord.Y);
                    }
                    if (anchorY == double.MinValue) anchorY = 0;

                    int curveCount = 0, textCount = 0;
                    var viewWarnings = new List<string>();

                    using (Transaction trans = new Transaction(doc, $"縮放 {view.Name} 行高"))
                    {
                        trans.Start();

                        // Scale DetailCurves Y coordinates
                        foreach (var el in curveElements)
                        {
                            var ce = el as CurveElement;
                            if (ce == null) continue;

                            try
                            {
                                Line line = ce.GeometryCurve as Line;
                                if (line == null) continue;

                                XYZ p0 = line.GetEndPoint(0);
                                XYZ p1 = line.GetEndPoint(1);

                                double newY0 = anchorY + (p0.Y - anchorY) * scaleFactor;
                                double newY1 = anchorY + (p1.Y - anchorY) * scaleFactor;

                                XYZ newP0 = new XYZ(p0.X, newY0, p0.Z);
                                XYZ newP1 = new XYZ(p1.X, newY1, p1.Z);

                                if (newP0.DistanceTo(newP1) > 1e-9)
                                {
                                    ce.GeometryCurve = Line.CreateBound(newP0, newP1);
                                    curveCount++;
                                }
                            }
                            catch (Exception ex)
                            {
                                viewWarnings.Add($"Curve {el.Id.GetIdValue()}: {ex.Message}");
                            }
                        }

                        // Scale TextNotes Y coordinates
                        foreach (var tn in textNotes)
                        {
                            try
                            {
                                XYZ coord = tn.Coord;
                                double newY = anchorY + (coord.Y - anchorY) * scaleFactor;
                                double deltaY = newY - coord.Y;

                                if (Math.Abs(deltaY) > 1e-9)
                                    ElementTransformUtils.MoveElement(doc, tn.Id, new XYZ(0, deltaY, 0));

                                textCount++;
                            }
                            catch (Exception ex)
                            {
                                viewWarnings.Add($"TextNote {tn.Id.GetIdValue()}: {ex.Message}");
                            }
                        }

                        trans.Commit();
                    }

                    Logger.Info($"[ScaleHeight] ({i + 1}/{targetViews.Count}) {view.Name}: {curveCount} curves, {textCount} texts");

                    results.Add(new
                    {
                        ViewName = view.Name,
                        Scaled = true,
                        DetailCurvesModified = curveCount,
                        TextNotesModified = textCount,
                        Warnings = viewWarnings
                    });
                }
                catch (Exception ex)
                {
                    Logger.Error($"[ScaleHeight] {view.Name} 失敗: {ex.Message}");
                    results.Add(new { ViewName = view.Name, Scaled = false, Reason = ex.Message });
                }
            }

            Logger.Info($"[ScaleHeight] 完成，共處理 {results.Count} 個 view");

            return new
            {
                Success = true,
                ScaleFactor = scaleFactor,
                ViewsProcessed = results.Count,
                Results = results
            };
        }

        #endregion
    }
}
