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
            // 預設改為 empty：避免誤用非空 seed 時把內容複製進新 legend
            string duplicateMode = parameters["duplicateMode"]?.Value<string>() ?? "empty";

            // 1. 找 seed legend
            var legends = new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(v => !v.IsTemplate && v.ViewType == ViewType.Legend)
                .ToList();

            if (legends.Count == 0)
                throw new Exception("專案中找不到任何 Legend 視圖。請先在樣板/專案中手動建立一個空白 legend（命名為 _SEED_BLANK）作為種子。");

            View seed = null;
            if (!string.IsNullOrEmpty(seedName))
            {
                seed = legends.FirstOrDefault(l => l.Name == seedName);
                if (seed == null)
                    throw new Exception($"找不到指定的 seed legend：{seedName}");
            }
            else
            {
                // 嚴格模式：只接受 _SEED_ 前綴的 legend，避免誤用有內容的 legend 作 seed
                seed = legends.FirstOrDefault(l => l.Name.StartsWith("_SEED_"));
                if (seed == null)
                {
                    var available = string.Join(", ", legends.Select(l => l.Name));
                    throw new Exception(
                        $"找不到 _SEED_ 前綴的空白 seed legend。" +
                        $"請手動建立一個空白 legend 並命名為 _SEED_BLANK，或在參數中指定 seedName。" +
                        $"\n目前專案中的 legend：{available}");
                }
            }

            ViewDuplicateOption dupOption = duplicateMode == "withDetailing"
                ? ViewDuplicateOption.WithDetailing
                : ViewDuplicateOption.Duplicate;

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
        /// 讀取 Excel 中的命名 table（或 worksheet 全表），僅回傳落在列印範圍內的儲存格。
        /// 預設不回傳 borders 陣列（節省 token）。需要時傳 includeBorders=true。
        /// summary=true 模式只回 {name, rowCount, colCount, mergeCount, hasNonThinBorders}。
        /// </summary>
        private object ReadExcelTables(JObject parameters)
        {
            string filePath = parameters["filePath"]?.Value<string>();
            if (string.IsNullOrEmpty(filePath))
                throw new Exception("請提供 filePath");
            if (!File.Exists(filePath))
                throw new Exception($"找不到 Excel 檔案：{filePath}");

            string mode = parameters["mode"]?.Value<string>() ?? "named_table";
            bool includeBorders = parameters["includeBorders"]?.Value<bool>() ?? false;
            bool summary = parameters["summary"]?.Value<bool>() ?? false;

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

            // 在 summary 模式下，需要 borders 來偵測 hasNonThinBorders
            bool needBorders = includeBorders || summary;

            object BuildTableObject(string name, string wsName, ExtractedRange ext)
            {
                if (summary)
                {
                    bool hasNonThin = false;
                    if (ext.Borders != null)
                    {
                        foreach (var row in ext.Borders)
                        {
                            foreach (var b in row)
                            {
                                if (b == null) continue;
                                for (int i = 0; i < 4; i++)
                                {
                                    var s = b[i];
                                    if (string.IsNullOrEmpty(s) || s == "None" || s == "Thin"
                                        || s == "Hair" || s == "Dotted" || s == "Dashed"
                                        || s == "DashDot" || s == "DashDotDot") continue;
                                    hasNonThin = true; break;
                                }
                                if (hasNonThin) break;
                            }
                            if (hasNonThin) break;
                        }
                    }
                    return new
                    {
                        name = name,
                        worksheet = wsName,
                        rowCount = ext.RowCount,
                        colCount = ext.ColCount,
                        mergeCount = ext.Merges?.Count ?? 0,
                        clippedByPrintArea = ext.Clipped,
                        hasNonThinBorders = hasNonThin
                    };
                }

                if (includeBorders)
                {
                    return new
                    {
                        name = name,
                        worksheet = wsName,
                        rows = ext.Rows,
                        colCount = ext.ColCount,
                        rowCount = ext.RowCount,
                        clippedByPrintArea = ext.Clipped,
                        merges = ext.Merges,
                        borders = ext.Borders
                    };
                }

                return new
                {
                    name = name,
                    worksheet = wsName,
                    rows = ext.Rows,
                    colCount = ext.ColCount,
                    rowCount = ext.RowCount,
                    clippedByPrintArea = ext.Clipped,
                    merges = ext.Merges
                };
            }

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
                        if (filter != null && !filter.Contains(ws.Name)) continue;

                        var merged = ExtractCellsInPrintAreas(ws, printAreas[0], printAreas, needBorders);
                        if (merged == null) continue;
                        resultTables.Add(BuildTableObject(ws.Name, ws.Name, merged));
                    }
                    else
                    {
                        foreach (var tbl in ws.Tables)
                        {
                            if (filter != null && !filter.Contains(tbl.Name)) continue;

                            var extracted = ExtractCellsInPrintAreas(ws, tbl.AsRange(), printAreas, needBorders);
                            if (extracted == null) continue;

                            resultTables.Add(BuildTableObject(tbl.Name, ws.Name, extracted));
                        }
                    }
                }
            }

            return new
            {
                Mode = mode,
                FilePath = filePath,
                Summary = summary,
                IncludeBorders = includeBorders,
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
            // Excel 原始尺寸（mm，未經 textSize 縮放）
            public List<double> ColWidthsMm;
            public List<double> RowHeightsMm;
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
        private ExtractedRange ExtractCellsInPrintAreas(IXLWorksheet ws, IXLRange sourceRange, List<IXLRange> printAreas, bool includeBorders = true)
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
                var borders = includeBorders ? new List<List<string[]>>() : null;
                for (int r = firstRow; r <= lastRow; r++)
                {
                    var rowVals = new List<string>();
                    var rowBorders = includeBorders ? new List<string[]>() : null;
                    for (int c = firstCol; c <= lastCol; c++)
                    {
                        var cell = ws.Cell(r, c);
                        if (coveredByMerge.Contains((long)r * 100000 + c))
                            rowVals.Add(string.Empty);
                        else
                            rowVals.Add(cell?.GetString() ?? string.Empty);

                        if (!includeBorders) continue;

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
                    if (includeBorders) borders.Add(rowBorders);
                }

                // 收集 Excel 原始 column 寬與 row 高（mm）
                // Excel column width 單位 = 默認字型 0 字元寬。Calibri 11pt 約 7px/char @ 96 DPI
                // pixels = chars * 7 + 5 (padding)
                // mm = pixels * 25.4 / 96
                var colWidthsMm = new List<double>();
                for (int c = firstCol; c <= lastCol; c++)
                {
                    double chars;
                    try { chars = ws.Column(c).Width; } catch { chars = 8.43; } // Excel 預設
                    if (chars <= 0) chars = 8.43;
                    double pixels = chars * 7.0 + 5.0;
                    colWidthsMm.Add(pixels * 25.4 / 96.0);
                }
                // Row height 單位 = points (1 point = 1/72 inch)
                var rowHeightsMm = new List<double>();
                for (int r = firstRow; r <= lastRow; r++)
                {
                    double points;
                    try { points = ws.Row(r).Height; } catch { points = 15.0; } // Excel 預設
                    if (points <= 0) points = 15.0;
                    rowHeightsMm.Add(points * 25.4 / 72.0);
                }

                return new ExtractedRange
                {
                    Rows = rows,
                    RowCount = lastRow - firstRow + 1,
                    ColCount = lastCol - firstCol + 1,
                    Clipped = clipped,
                    Merges = mergesLocal,
                    Borders = borders,
                    ColWidthsMm = colWidthsMm,
                    RowHeightsMm = rowHeightsMm
                };
            }

            return null; // 所有 printArea 都沒交集
        }

        #endregion

        #region Excel → Drafting Views（原子化命令）

        /// <summary>
        /// 一站式：讀 Excel → 對每張 sheet 建立 Drafting View → 畫線 → 寫字
        /// 固定常數：viewScale=1（1:1）、textSize=3mm、rowH=4.5mm、colW=30mm
        /// </summary>
        private object ImportExcelToDraftingViews(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;

            string filePath = parameters["filePath"]?.Value<string>();
            if (string.IsNullOrEmpty(filePath)) throw new Exception("請提供 filePath");
            if (!File.Exists(filePath)) throw new Exception($"找不到 Excel 檔案：{filePath}");

            string namingPattern = parameters["namingPattern"]?.Value<string>() ?? "{name}";
            bool overwrite = parameters["overwrite"]?.Value<bool>() ?? true;

            HashSet<string> filter = null;
            var sheetsArr = parameters["sheets"] as JArray;
            if (sheetsArr != null && sheetsArr.Count > 0)
            {
                filter = new HashSet<string>(
                    sheetsArr.Select(t => t?.Value<string>()).Where(s => !string.IsNullOrEmpty(s)),
                    StringComparer.OrdinalIgnoreCase);
            }

            // 固定常數（Plan 規範）
            const int VIEW_SCALE = 1;
            const double TEXT_SIZE_MM = 3.0;
            const double LINE_H_MM    = TEXT_SIZE_MM * 1.4;   // 4.2mm/行
            // 在 Excel 原始 mm 基礎上放大 20%（使表格在 view 中視覺較大）
            const double EXCEL_SCALE = 1.2;
            const double MIN_ROW_H_MM = 5.0;                  // 最小列高
            const double MIN_COL_W_MM = 5.0;                  // 最小欄寬
            const double MM_TO_FEET = 1.0 / 304.8;
            double textSizeFeet = TEXT_SIZE_MM * MM_TO_FEET;

            // 找 Drafting ViewFamilyType
            ViewFamilyType draftingVFT = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>()
                .FirstOrDefault(x => x.ViewFamily == ViewFamily.Drafting);
            if (draftingVFT == null) throw new Exception("專案中找不到 Drafting ViewFamilyType");

            // 解析或建立 textSize=3mm 的 TextNoteType
            var allTextTypes = new FilteredElementCollector(doc)
                .OfClass(typeof(TextNoteType))
                .Cast<TextNoteType>()
                .ToList();
            if (allTextTypes.Count == 0) throw new Exception("找不到任何 TextNoteType");

            // 預先收集所有 sheet 的資料（讀檔在 Transaction 外）
            var sheetData = new List<(string viewName, string wsName, ExtractedRange ext)>();
            var warnings = new List<string>();

            using (var wb = new XLWorkbook(filePath))
            {
                foreach (var ws in wb.Worksheets)
                {
                    if (filter != null && !filter.Contains(ws.Name)) continue;

                    var printAreas = ws.PageSetup.PrintAreas?.ToList();
                    if (printAreas == null || printAreas.Count == 0)
                    {
                        warnings.Add($"Worksheet '{ws.Name}' 沒有設定列印範圍，已略過");
                        continue;
                    }

                    var ext = ExtractCellsInPrintAreas(ws, printAreas[0], printAreas, false);
                    if (ext == null)
                    {
                        warnings.Add($"Worksheet '{ws.Name}' 列印範圍無資料，已略過");
                        continue;
                    }

                    string viewName = namingPattern.Replace("{name}", ws.Name);
                    sheetData.Add((viewName, ws.Name, ext));
                }
            }

            var created = new List<object>();
            var skipped = new List<object>();
            var errors = new List<object>();

            // 預先尋找已存在的同名 view（為 overwrite 用）
            var allViews = new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(v => !v.IsTemplate)
                .ToList();

            using (Transaction trans = new Transaction(doc, "匯入 Excel 至 Drafting Views"))
            {
                trans.Start();

                // 解析或建立目標 TextNoteType（在 Transaction 內以便建立新 type）
                TextNoteType textType = allTextTypes.FirstOrDefault(t =>
                {
                    var p = t.get_Parameter(BuiltInParameter.TEXT_SIZE);
                    return p != null && Math.Abs(p.AsDouble() - textSizeFeet) < 1e-6;
                });
                if (textType == null)
                {
                    string newName = $"MCP_TextSize_{TEXT_SIZE_MM:0.##}mm";
                    textType = allTextTypes.FirstOrDefault(t => t.Name == newName);
                    if (textType == null)
                    {
                        textType = allTextTypes.First().Duplicate(newName) as TextNoteType;
                        var sp = textType.get_Parameter(BuiltInParameter.TEXT_SIZE);
                        if (sp != null) sp.Set(textSizeFeet);
                    }
                }

                var textOptions = new TextNoteOptions
                {
                    TypeId = textType.Id,
                    HorizontalAlignment = HorizontalTextAlignment.Center
                };

                // ===== Phase A：擷取即將被覆蓋之 view 在 sheets 上的 viewport 位置 =====
                // key = viewName，value = list of (sheetId, sheetName, boxCenter)
                // 之後 doc.Delete(view) 會連帶刪除 viewport，所以必須先記下中心點
                var capturedViewports = new Dictionary<string, List<Tuple<IdType, string, XYZ>>>();
                var newViewByName = new Dictionary<string, ElementId>();
                if (overwrite)
                {
                    var targetNames = new HashSet<string>(sheetData.Select(s => s.viewName));
                    var allSheets = new FilteredElementCollector(doc)
                        .OfClass(typeof(ViewSheet))
                        .Cast<ViewSheet>()
                        .ToList();
                    foreach (var vs in allSheets)
                    {
                        foreach (var vpId in vs.GetAllViewports())
                        {
                            var vp = doc.GetElement(vpId) as Viewport;
                            if (vp == null) continue;
                            var v = doc.GetElement(vp.ViewId) as View;
                            if (v == null || !targetNames.Contains(v.Name)) continue;
                            if (!capturedViewports.TryGetValue(v.Name, out var list))
                            {
                                list = new List<Tuple<IdType, string, XYZ>>();
                                capturedViewports[v.Name] = list;
                            }
                            list.Add(Tuple.Create(vs.Id.GetIdValue(), vs.Name, vp.GetBoxCenter()));
                        }
                    }
                }

                foreach (var sheet in sheetData)
                {
                    try
                    {
                        // 處理 overwrite
                        var existing = allViews.FirstOrDefault(v => v.Name == sheet.viewName);
                        if (existing != null)
                        {
                            if (!overwrite)
                            {
                                skipped.Add(new { name = sheet.viewName, reason = "view 已存在 (overwrite=false)" });
                                continue;
                            }
                            doc.Delete(existing.Id);
                            allViews.Remove(existing);
                        }

                        // 建 Drafting View
                        ViewDrafting view = ViewDrafting.Create(doc, draftingVFT.Id);
                        view.Name = sheet.viewName;
                        try { view.Scale = VIEW_SCALE; } catch { /* 某些 view 不支援 1:1 仍可繼續 */ }
                        newViewByName[sheet.viewName] = view.Id;

                        // ===== Layout：套用 Excel 原始 column/row 尺寸 ×EXCEL_SCALE，再依 wrap 補強 row 高 =====
                        int R = sheet.ext.RowCount, C = sheet.ext.ColCount;
                        var merges = sheet.ext.Merges ?? new List<int[]>();

                        // (r,c) -> merge 對應，及 covered set
                        var mergeAt = new Dictionary<long, int[]>();
                        var covered = new HashSet<long>();
                        foreach (var m in merges)
                        {
                            int mr1 = m[0], mc1 = m[1], mr2 = m[2], mc2 = m[3];
                            mergeAt[(long)mr1 * 100000 + mc1] = m;
                            for (int rr = mr1; rr <= mr2; rr++)
                                for (int cc = mc1; cc <= mc2; cc++)
                                    if (!(rr == mr1 && cc == mc1))
                                        covered.Add((long)rr * 100000 + cc);
                        }

                        double[] colW = new double[C];
                        for (int c = 0; c < C; c++)
                        {
                            double w = (sheet.ext.ColWidthsMm != null && c < sheet.ext.ColWidthsMm.Count)
                                       ? sheet.ext.ColWidthsMm[c] * EXCEL_SCALE : 0;
                            colW[c] = Math.Max(MIN_COL_W_MM, w);
                        }
                        double[] rowH = new double[R];
                        for (int r = 0; r < R; r++)
                        {
                            double h = (sheet.ext.RowHeightsMm != null && r < sheet.ext.RowHeightsMm.Count)
                                       ? sheet.ext.RowHeightsMm[r] * EXCEL_SCALE : 0;
                            rowH[r] = Math.Max(MIN_ROW_H_MM, h);
                        }

                        // 補強 row 高：對每個 cell 計算 wrap 行數，若 Excel 原始 row 高不夠則加大
                        for (int r = 0; r < R; r++)
                        {
                            var rowList = sheet.ext.Rows[r];
                            for (int c = 0; c < C; c++)
                            {
                                if (covered.Contains((long)r * 100000 + c)) continue;
                                string text = c < rowList.Count ? rowList[c] : null;
                                if (string.IsNullOrEmpty(text)) continue;
                                text = text.Replace("\r\n", "\n").Replace("\r", "\n");
                                if (string.IsNullOrEmpty(text)) continue;

                                int mr1, mc1, mr2, mc2;
                                long key = (long)r * 100000 + c;
                                if (mergeAt.TryGetValue(key, out var m))
                                {
                                    mr1 = m[0]; mc1 = m[1]; mr2 = m[2]; mc2 = m[3];
                                }
                                else
                                {
                                    mr1 = r; mc1 = c; mr2 = r; mc2 = c;
                                }
                                double mergedW = 0;
                                for (int cc = mc1; cc <= mc2; cc++) mergedW += colW[cc];
                                int wrapLines = CountWrappedLines(text, mergedW, TEXT_SIZE_MM);
                                if (wrapLines <= 1) continue;
                                double need = wrapLines * LINE_H_MM + 1.5;
                                int rowsSpan = mr2 - mr1 + 1;
                                double perRow = need / rowsSpan;
                                for (int rr = mr1; rr <= mr2; rr++)
                                    if (rowH[rr] < perRow) rowH[rr] = perRow;
                            }
                        }

                        // 累積座標
                        double[] colX = new double[C + 1];
                        for (int c = 0; c < C; c++) colX[c + 1] = colX[c] + colW[c];
                        double[] rowY = new double[R + 1];
                        for (int r = 0; r < R; r++) rowY[r + 1] = rowY[r] - rowH[r]; // y 向下為負

                        int linesCount = BuildAndDrawLines(doc, view, sheet.ext, colX, rowY, MM_TO_FEET);
                        int textsCount = BuildAndDrawTexts(doc, view, sheet.ext, colX, rowY, MM_TO_FEET,
                            textOptions, LINE_H_MM, TEXT_SIZE_MM);

                        created.Add(new
                        {
                            name = view.Name,
                            viewId = view.Id.GetIdValue(),
                            lines = linesCount,
                            texts = textsCount
                        });
                    }
                    catch (Exception ex)
                    {
                        errors.Add(new { name = sheet.viewName, error = ex.Message });
                    }
                }

                // ===== Phase C：把擷取到的 viewport 放回對應 sheet 的原中心點 =====
                int viewportsRestored = 0;
                var viewportFailures = new List<object>();
                foreach (var kvp in capturedViewports)
                {
                    string viewName = kvp.Key;
                    if (!newViewByName.TryGetValue(viewName, out var newViewId)) continue;
                    foreach (var vpInfo in kvp.Value)
                    {
                        try
                        {
                            var sheetEl = doc.GetElement(vpInfo.Item1.ToElementId()) as ViewSheet;
                            if (sheetEl == null)
                            {
                                viewportFailures.Add(new { view = viewName, sheet = vpInfo.Item2, error = "sheet not found" });
                                continue;
                            }
                            Viewport.Create(doc, sheetEl.Id, newViewId, vpInfo.Item3);
                            viewportsRestored++;
                        }
                        catch (Exception ex)
                        {
                            viewportFailures.Add(new { view = viewName, sheet = vpInfo.Item2, error = ex.Message });
                        }
                    }
                }

                trans.Commit();

                return new
                {
                    Total = sheetData.Count,
                    Created = created,
                    Skipped = skipped,
                    Errors = errors,
                    Warnings = warnings,
                    ViewportsRestored = viewportsRestored,
                    ViewportFailures = viewportFailures
                };
            }
        }

        /// <summary>
        /// 估算字串顯示寬度（mm）。CJK/全形 = 1.0×textSize；ASCII/半形 = 0.55×textSize。
        /// 支援多行：取最寬行。
        /// </summary>
        private static double EstimateTextWidth(string text, double textSize)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            double maxW = 0;
            foreach (var seg in text.Split('\n'))
            {
                double w = 0;
                foreach (char ch in seg)
                {
                    bool isCJK = (ch >= 0x3000 && ch <= 0x9FFF)
                              || (ch >= 0xAC00 && ch <= 0xD7AF)
                              || (ch >= 0xFF00 && ch <= 0xFFEF);
                    w += isCJK ? textSize : textSize * 0.55;
                }
                if (w > maxW) maxW = w;
            }
            return maxW;
        }

        /// <summary>
        /// 計算文字按 cellWidth 換行後的總行數（含原有 \n 切分）。
        /// </summary>
        private static int CountWrappedLines(string text, double cellWidth, double textSize)
        {
            if (string.IsNullOrEmpty(text)) return 1;
            int lines = 0;
            double safeCellW = Math.Max(cellWidth, textSize);
            foreach (var seg in text.Split('\n'))
            {
                double w = 0;
                foreach (char ch in seg)
                {
                    bool isCJK = (ch >= 0x3000 && ch <= 0x9FFF)
                              || (ch >= 0xAC00 && ch <= 0xD7AF)
                              || (ch >= 0xFF00 && ch <= 0xFFEF);
                    w += isCJK ? textSize : textSize * 0.55;
                }
                int n = (int)Math.Ceiling(w / safeCellW);
                lines += Math.Max(1, n);
            }
            return Math.Max(1, lines);
        }

        /// <summary>
        /// 依 ext.Rows + ext.Merges 計算 merge-aware 邊界線並畫到 view 上。
        /// 同方向連續實線段合併成一條 detail line。座標用 colX/rowY 累積值。
        /// </summary>
        private int BuildAndDrawLines(Document doc, View view, ExtractedRange ext,
            double[] colX, double[] rowY, double mmToFeet)
        {
            int R = ext.RowCount, C = ext.ColCount;
            var merges = ext.Merges ?? new List<int[]>();

            bool InMergeH(int r, int c)
            {
                // 內部水平邊：r-1 與 r 之間，col c
                foreach (var m in merges)
                {
                    int mr1 = m[0], mc1 = m[1], mr2 = m[2], mc2 = m[3];
                    if (mr1 <= r - 1 && mr2 >= r && mc1 <= c && c <= mc2) return true;
                }
                return false;
            }
            bool InMergeV(int r, int c)
            {
                foreach (var m in merges)
                {
                    int mr1 = m[0], mc1 = m[1], mr2 = m[2], mc2 = m[3];
                    if (mc1 <= c - 1 && mc2 >= c && mr1 <= r && r <= mr2) return true;
                }
                return false;
            }

            var lineSpecs = new List<(double sx, double sy, double ex, double ey)>();

            // 水平線：r ∈ [0..R]，c ∈ [0..C-1]
            for (int r = 0; r <= R; r++)
            {
                int c = 0;
                while (c < C)
                {
                    if (r > 0 && r < R && InMergeH(r, c)) { c++; continue; }
                    int c0 = c;
                    while (c < C && !(r > 0 && r < R && InMergeH(r, c))) c++;
                    lineSpecs.Add((colX[c0], rowY[r], colX[c], rowY[r]));
                }
            }
            // 垂直線：c ∈ [0..C]，r ∈ [0..R-1]
            for (int c = 0; c <= C; c++)
            {
                int r = 0;
                while (r < R)
                {
                    if (c > 0 && c < C && InMergeV(r, c)) { r++; continue; }
                    int r0 = r;
                    while (r < R && !(c > 0 && c < C && InMergeV(r, c))) r++;
                    lineSpecs.Add((colX[c], rowY[r0], colX[c], rowY[r]));
                }
            }

            // 找 <Thin Lines> style（選用，找不到就用預設）
            int created = 0;
            foreach (var ls in lineSpecs)
            {
                XYZ p1 = new XYZ(ls.sx * mmToFeet, ls.sy * mmToFeet, 0);
                XYZ p2 = new XYZ(ls.ex * mmToFeet, ls.ey * mmToFeet, 0);
                if (p1.DistanceTo(p2) < 0.001) continue;
                try
                {
                    var line = Line.CreateBound(p1, p2);
                    var dc = doc.Create.NewDetailCurve(view, line);
                    // 套 <Thin Lines> 樣式
                    foreach (ElementId sid in dc.GetLineStyleIds())
                    {
                        var styleEl = doc.GetElement(sid);
                        if (styleEl != null && styleEl.Name.Contains("Thin"))
                        {
                            dc.LineStyle = styleEl;
                            break;
                        }
                    }
                    created++;
                }
                catch { /* skip 失敗線 */ }
            }
            return created;
        }

        /// <summary>
        /// 依 ext.Rows + ext.Merges 將每個非空 cell（合併格用合併區中心）寫成 TextNote。
        /// 設定 Width = 合併 cell 寬度，使文字超長時自動 wrap 到下一行（同 Excel 行為）。
        /// 垂直方向以 wrap 後行數推算 textHeight 後做置中。
        /// </summary>
        private int BuildAndDrawTexts(Document doc, View view, ExtractedRange ext,
            double[] colX, double[] rowY, double mmToFeet,
            TextNoteOptions textOptions, double lineHmm, double textSizeMm)
        {
            int R = ext.RowCount, C = ext.ColCount;
            var merges = ext.Merges ?? new List<int[]>();

            // 建立 (r,c) -> merge 對應，及 covered set
            var mergeAt = new Dictionary<long, int[]>();
            var covered = new HashSet<long>();
            foreach (var m in merges)
            {
                int mr1 = m[0], mc1 = m[1], mr2 = m[2], mc2 = m[3];
                mergeAt[(long)mr1 * 100000 + mc1] = m;
                for (int rr = mr1; rr <= mr2; rr++)
                    for (int cc = mc1; cc <= mc2; cc++)
                        if (!(rr == mr1 && cc == mc1))
                            covered.Add((long)rr * 100000 + cc);
            }

            const double MIN_WIDTH_FEET = 0.0091;

            int created = 0;
            for (int r = 0; r < R; r++)
            {
                var row = ext.Rows[r];
                for (int c = 0; c < C; c++)
                {
                    if (covered.Contains((long)r * 100000 + c)) continue;
                    string text = c < row.Count ? row[c] : null;
                    if (string.IsNullOrEmpty(text)) continue;
                    // \r\n → \n
                    text = text.Replace("\r\n", "\n").Replace("\r", "\n");
                    if (string.IsNullOrEmpty(text)) continue;

                    int mr1, mc1, mr2, mc2;
                    long key = (long)r * 100000 + c;
                    if (mergeAt.TryGetValue(key, out var m))
                    {
                        mr1 = m[0]; mc1 = m[1]; mr2 = m[2]; mc2 = m[3];
                    }
                    else
                    {
                        mr1 = r; mc1 = c; mr2 = r; mc2 = c;
                    }

                    double left   = colX[mc1];
                    double right  = colX[mc2 + 1];
                    double top    = rowY[mr1];      // Revit Y+ = up，top 較大
                    double bottom = rowY[mr2 + 1];  // bottom 較小
                    double cx = (left + right) / 2.0;
                    double cy = (top + bottom) / 2.0;
                    double widthMm = right - left;

                    // 計算 wrap 後行數，做垂直置中
                    int wrapLines = CountWrappedLines(text, widthMm, textSizeMm);
                    double textH = wrapLines * lineHmm;
                    // TextNote.Create 的 position 是文字框「上邊緣」(Revit Y+ = up)，
                    // 垂直置中：上邊緣 = cell 中線 + 文字高度的一半
                    double yInsert = cy + textH / 2.0;

                    try
                    {
                        TextNote note = TextNote.Create(doc, view.Id,
                            new XYZ(cx * mmToFeet, yInsert * mmToFeet, 0),
                            text, textOptions);
                        // 設 Width = 合併 cell 寬度，使長文字自動 wrap
                        try
                        {
                            double widthFeet = widthMm * mmToFeet;
                            if (widthFeet < MIN_WIDTH_FEET) widthFeet = MIN_WIDTH_FEET;
                            note.Width = widthFeet;
                        }
                        catch { /* Width 不允許時退回自動寬度 */ }
                        created++;
                    }
                    catch { /* skip 失敗 cell */ }
                }
            }
            return created;
        }

        #endregion
    }
}
