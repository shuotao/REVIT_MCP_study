using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ClosedXML.Excel;
using Newtonsoft.Json.Linq;

#if REVIT2025_OR_GREATER
using IdType = System.Int64;
#else
using IdType = System.Int32;
#endif

namespace RevitMCP.Core
{
    /// <summary>
    /// Legend 視圖批次建立 + Excel table 讀取
    /// 設計：seed legend 由樣板人工放置（建議 _SEED_BLANK），API 僅做 Duplicate
    /// 依賴：ClosedXML
    /// </summary>
    public partial class CommandExecutor
    {
        #region Legend 批次建立

        /// <summary>
        /// 從 seed legend 批次複製出多個 legend 視圖
        /// </summary>
        private object CreateLegends(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;

            var namesArray = parameters["names"] as JArray;
            if (namesArray == null || namesArray.Count == 0)
                throw new Exception("請提供要建立的 legend 名稱清單 (names)");

            string seedName = parameters["seedName"]?.Value<string>();
            string duplicateMode = parameters["duplicateMode"]?.Value<string>() ?? "withDetailing";

            // 1. 找 seed legend
            var legends = new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(v => !v.IsTemplate && v.ViewType == ViewType.Legend)
                .ToList();

            if (legends.Count == 0)
                throw new Exception("專案中找不到任何 Legend 視圖。請先在樣板/專案中手動建立一個 legend（建議命名為 _SEED_BLANK）作為種子。");

            View seed = null;
            if (!string.IsNullOrEmpty(seedName))
            {
                seed = legends.FirstOrDefault(l => l.Name == seedName);
                if (seed == null)
                    throw new Exception($"找不到指定的 seed legend：{seedName}");
            }
            else
            {
                seed = legends.FirstOrDefault(l => l.Name.StartsWith("_SEED_"))
                       ?? legends.First();
            }

            ViewDuplicateOption dupOption = duplicateMode == "empty"
                ? ViewDuplicateOption.Duplicate
                : ViewDuplicateOption.WithDetailing;

            var results = new List<object>();

            using (Transaction trans = new Transaction(doc, "批次建立 Legend"))
            {
                trans.Start();

                foreach (var n in namesArray)
                {
                    string targetName = n?.Value<string>();
                    if (string.IsNullOrEmpty(targetName))
                    {
                        results.Add(new { Name = (string)null, Success = false, Error = "name 為空" });
                        continue;
                    }

                    try
                    {
                        ElementId newId = seed.Duplicate(dupOption);
                        View newView = doc.GetElement(newId) as View;
                        if (newView == null)
                            throw new Exception("Duplicate 後取得 View 失敗");

                        try { newView.Name = targetName; }
                        catch (Exception nameEx)
                        {
                            results.Add(new
                            {
                                ElementId = newId.GetIdValue(),
                                Name = targetName,
                                SeedUsed = seed.Name,
                                Success = false,
                                Error = $"命名失敗（可能已存在）：{nameEx.Message}"
                            });
                            continue;
                        }

                        results.Add(new
                        {
                            ElementId = newId.GetIdValue(),
                            Name = newView.Name,
                            SeedUsed = seed.Name,
                            Success = true
                        });
                    }
                    catch (Exception ex)
                    {
                        results.Add(new
                        {
                            Name = targetName,
                            SeedUsed = seed.Name,
                            Success = false,
                            Error = ex.Message
                        });
                    }
                }

                trans.Commit();
            }

            return new
            {
                Total = namesArray.Count,
                SeedUsed = seed.Name,
                DuplicateMode = duplicateMode,
                Results = results
            };
        }

        #endregion

        #region Excel Table 讀取（僅列印範圍）

        /// <summary>
        /// 讀取 Excel 中的命名 table（或 worksheet 全表），僅回傳落在列印範圍內的儲存格
        /// </summary>
        private object ReadExcelTables(JObject parameters)
        {
            string filePath = parameters["filePath"]?.Value<string>();
            if (string.IsNullOrEmpty(filePath))
                throw new Exception("請提供 filePath");
            if (!File.Exists(filePath))
                throw new Exception($"找不到 Excel 檔案：{filePath}");

            string mode = parameters["mode"]?.Value<string>() ?? "named_table";

            HashSet<string> filter = null;
            var filterArray = parameters["tableNames"] as JArray;
            if (filterArray != null && filterArray.Count > 0)
            {
                filter = new HashSet<string>(
                    filterArray.Select(t => t?.Value<string>()).Where(s => !string.IsNullOrEmpty(s)),
                    StringComparer.OrdinalIgnoreCase);
            }

            var resultTables = new List<object>();
            var warnings = new List<string>();

            using (var wb = new XLWorkbook(filePath))
            {
                foreach (var ws in wb.Worksheets)
                {
                    var printAreas = ws.PageSetup.PrintAreas?.ToList();
                    if (printAreas == null || printAreas.Count == 0)
                    {
                        warnings.Add($"Worksheet '{ws.Name}' 沒有設定列印範圍，已略過");
                        continue;
                    }

                    if (mode == "worksheet" || mode == "worksheets")
                    {
                        // 整個 worksheet 視為一張表，名稱 = worksheet name
                        if (filter != null && !filter.Contains(ws.Name)) continue;

                        // worksheets 模式：直接用 print area 當作 source，
                        // 避免 RangeUsed() 跳過僅含邊框的空白外框欄
                        var merged = ExtractCellsInPrintAreas(ws, printAreas[0], printAreas);
                        if (merged == null) continue;
                        resultTables.Add(new
                        {
                            name = ws.Name,
                            worksheet = ws.Name,
                            rows = merged.Rows,
                            colCount = merged.ColCount,
                            rowCount = merged.RowCount,
                            clippedByPrintArea = merged.Clipped,
                            merges = merged.Merges,
                            borders = merged.Borders
                        });
                    }
                    else
                    {
                        // named_table 模式：讀 worksheet.Tables
                        foreach (var tbl in ws.Tables)
                        {
                            if (filter != null && !filter.Contains(tbl.Name)) continue;

                            var extracted = ExtractCellsInPrintAreas(ws, tbl.AsRange(), printAreas);
                            if (extracted == null) continue; // 完全在列印範圍外

                            resultTables.Add(new
                            {
                                name = tbl.Name,
                                worksheet = ws.Name,
                                rows = extracted.Rows,
                                colCount = extracted.ColCount,
                                rowCount = extracted.RowCount,
                                clippedByPrintArea = extracted.Clipped,
                                merges = extracted.Merges,
                                borders = extracted.Borders
                            });
                        }
                    }
                }
            }

            return new
            {
                Mode = mode,
                FilePath = filePath,
                TableCount = resultTables.Count,
                Tables = resultTables,
                Warnings = warnings
            };
        }

        private class ExtractedRange
        {
            public List<List<string>> Rows;
            public int RowCount;
            public int ColCount;
            public bool Clipped;
            public List<int[]> Merges;
            // borders[r][c] = [top, right, bottom, left]，每個值為 XLBorderStyleValues 的字串
            public List<List<string[]>> Borders;
        }

        private static string BorderToStr(XLBorderStyleValues v)
        {
            return v.ToString(); // None / Thin / Medium / Thick / Double / Hair / Dashed / Dotted ...
        }

        private static int BorderSeverity(XLBorderStyleValues v)
        {
            switch (v)
            {
                case XLBorderStyleValues.None: return 0;
                case XLBorderStyleValues.Hair:
                case XLBorderStyleValues.Dotted:
                case XLBorderStyleValues.Dashed:
                case XLBorderStyleValues.DashDot:
                case XLBorderStyleValues.DashDotDot:
                case XLBorderStyleValues.Thin: return 1;
                case XLBorderStyleValues.Double:
                case XLBorderStyleValues.MediumDashed:
                case XLBorderStyleValues.MediumDashDot:
                case XLBorderStyleValues.MediumDashDotDot:
                case XLBorderStyleValues.SlantDashDot:
                case XLBorderStyleValues.Medium: return 2;
                case XLBorderStyleValues.Thick: return 3;
                default: return 1;
            }
        }

        private static XLBorderStyleValues MaxBorder(XLBorderStyleValues a, XLBorderStyleValues b)
        {
            return BorderSeverity(a) >= BorderSeverity(b) ? a : b;
        }

        /// <summary>
        /// 取 sourceRange 與所有 printAreas 的聯集交集；
        /// 若交集為空回 null；若小於 sourceRange 則 Clipped=true。
        /// 為簡單起見，若有多個 printAreas，取「第一個有交集」的列印範圍。
        /// </summary>
        private ExtractedRange ExtractCellsInPrintAreas(IXLWorksheet ws, IXLRange sourceRange, List<IXLRange> printAreas)
        {
            if (sourceRange == null) return null;

            int srcFirstRow = sourceRange.RangeAddress.FirstAddress.RowNumber;
            int srcLastRow = sourceRange.RangeAddress.LastAddress.RowNumber;
            int srcFirstCol = sourceRange.RangeAddress.FirstAddress.ColumnNumber;
            int srcLastCol = sourceRange.RangeAddress.LastAddress.ColumnNumber;

            foreach (var pa in printAreas)
            {
                int paFirstRow = pa.RangeAddress.FirstAddress.RowNumber;
                int paLastRow = pa.RangeAddress.LastAddress.RowNumber;
                int paFirstCol = pa.RangeAddress.FirstAddress.ColumnNumber;
                int paLastCol = pa.RangeAddress.LastAddress.ColumnNumber;

                int firstRow = Math.Max(srcFirstRow, paFirstRow);
                int lastRow = Math.Min(srcLastRow, paLastRow);
                int firstCol = Math.Max(srcFirstCol, paFirstCol);
                int lastCol = Math.Min(srcLastCol, paLastCol);

                if (firstRow > lastRow || firstCol > lastCol) continue; // 此 printArea 無交集

                bool clipped = firstRow != srcFirstRow || lastRow != srcLastRow
                            || firstCol != srcFirstCol || lastCol != srcLastCol;

                // 收集落在 extracted 區內的合併儲存格（轉成 0-based 相對座標）
                var mergesLocal = new List<int[]>();
                var coveredByMerge = new HashSet<long>(); // key = r*100000+c
                foreach (var mr in ws.MergedRanges)
                {
                    int mFirstRow = mr.RangeAddress.FirstAddress.RowNumber;
                    int mLastRow = mr.RangeAddress.LastAddress.RowNumber;
                    int mFirstCol = mr.RangeAddress.FirstAddress.ColumnNumber;
                    int mLastCol = mr.RangeAddress.LastAddress.ColumnNumber;

                    int mr1 = Math.Max(mFirstRow, firstRow);
                    int mr2 = Math.Min(mLastRow, lastRow);
                    int mc1 = Math.Max(mFirstCol, firstCol);
                    int mc2 = Math.Min(mLastCol, lastCol);
                    if (mr1 > mr2 || mc1 > mc2) continue;

                    mergesLocal.Add(new[]
                    {
                        mr1 - firstRow,
                        mc1 - firstCol,
                        mr2 - firstRow,
                        mc2 - firstCol
                    });
                    // 標記 merge 內非左上角的 cell（避免重複輸出文字）
                    for (int rr = mr1; rr <= mr2; rr++)
                        for (int cc = mc1; cc <= mc2; cc++)
                            if (!(rr == mFirstRow && cc == mFirstCol))
                                coveredByMerge.Add((long)rr * 100000 + cc);
                }

                var rows = new List<List<string>>();
                var borders = new List<List<string[]>>();
                for (int r = firstRow; r <= lastRow; r++)
                {
                    var rowVals = new List<string>();
                    var rowBorders = new List<string[]>();
                    for (int c = firstCol; c <= lastCol; c++)
                    {
                        var cell = ws.Cell(r, c);
                        if (coveredByMerge.Contains((long)r * 100000 + c))
                            rowVals.Add(string.Empty);
                        else
                            rowVals.Add(cell?.GetString() ?? string.Empty);

                        var bd = cell?.Style?.Border;
                        var top = bd != null ? bd.TopBorder : XLBorderStyleValues.None;
                        var right = bd != null ? bd.RightBorder : XLBorderStyleValues.None;
                        var bottom = bd != null ? bd.BottomBorder : XLBorderStyleValues.None;
                        var left = bd != null ? bd.LeftBorder : XLBorderStyleValues.None;

                        // 邊界格：與列印範圍外鄰格的反向邊框取最大嚴重度
                        if (r == firstRow && r > 1)
                        {
                            var nb = ws.Cell(r - 1, c)?.Style?.Border;
                            if (nb != null) top = MaxBorder(top, nb.BottomBorder);
                        }
                        if (r == lastRow)
                        {
                            var nb = ws.Cell(r + 1, c)?.Style?.Border;
                            if (nb != null) bottom = MaxBorder(bottom, nb.TopBorder);
                        }
                        if (c == firstCol && c > 1)
                        {
                            var nb = ws.Cell(r, c - 1)?.Style?.Border;
                            if (nb != null) left = MaxBorder(left, nb.RightBorder);
                        }
                        if (c == lastCol)
                        {
                            var nb = ws.Cell(r, c + 1)?.Style?.Border;
                            if (nb != null) right = MaxBorder(right, nb.LeftBorder);
                        }

                        rowBorders.Add(new[]
                        {
                            BorderToStr(top),
                            BorderToStr(right),
                            BorderToStr(bottom),
                            BorderToStr(left)
                        });
                    }
                    rows.Add(rowVals);
                    borders.Add(rowBorders);
                }

                return new ExtractedRange
                {
                    Rows = rows,
                    RowCount = lastRow - firstRow + 1,
                    ColCount = lastCol - firstCol + 1,
                    Clipped = clipped,
                    Merges = mergesLocal,
                    Borders = borders
                };
            }

            return null; // 所有 printArea 都沒交集
        }

        #endregion
    }
}
