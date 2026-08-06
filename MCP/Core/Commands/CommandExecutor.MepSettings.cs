using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using Newtonsoft.Json.Linq;

namespace RevitMCP.Core
{
    /// <summary>
    /// get_mep_segments_and_sizes — 一次盤點整個專案的 MEP Segment 與 Size 目錄。
    /// 對應 Manage → MEP Settings → Mechanical/Pipe Settings 裡的 Segments and Sizes 對話框：
    /// 每個 PipeSegment（材質 × Schedule）各帶一份尺寸表，Schedule 撈不到、System Browser 也看不到，
    /// 唯一路徑是 API。純唯讀、不開 Transaction。
    /// 尺寸全部由內部單位（feet）轉為 mm 輸出，供台灣 CNS 對帳使用。
    /// </summary>
    public partial class CommandExecutor
    {
        #region get_mep_segments_and_sizes

        private object GetMepSegmentsAndSizes(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;

            bool usedOnly = parameters["usedOnly"]?.Value<bool?>() ?? false;
            bool summaryOnly = parameters["summaryOnly"]?.Value<bool?>() ?? false;
            string segmentName = parameters["segmentName"]?.Value<string>()?.Trim();
            bool hasNameFilter = !string.IsNullOrWhiteSpace(segmentName);
            // 指定 segmentName 時預設不帶風管（是在鑽某個管段，風管只會是雜訊）；明確給 includeDuct 則以它為準。
            bool includeDuct = parameters["includeDuct"]?.Value<bool?>() ?? !hasNameFilter;

            // 1) 管段（PipeSegment 是 Segment 唯一的衍生類別）
            var segments = new List<object>();
            int totalPipeSizes = 0;
            int reportedPipeSizes = 0;

            var allPipeSegments = new FilteredElementCollector(doc)
                .OfClass(typeof(PipeSegment))
                .Cast<PipeSegment>()
                .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var pipeSegments = hasNameFilter
                ? allPipeSegments.Where(s => s.Name != null &&
                    s.Name.IndexOf(segmentName, StringComparison.OrdinalIgnoreCase) >= 0).ToList()
                : allPipeSegments;

            foreach (PipeSegment seg in pipeSegments)
            {
                var sizes = new List<object>();
                int inSizeLists = 0;
                int inSizing = 0;

                foreach (MEPSize size in seg.GetSizes().OrderBy(s => s.NominalDiameter))
                {
                    totalPipeSizes++;
                    if (size.UsedInSizeLists) inSizeLists++;
                    if (size.UsedInSizing) inSizing++;
                    if (usedOnly && !size.UsedInSizeLists && !size.UsedInSizing) continue;
                    reportedPipeSizes++;
                    if (!summaryOnly) sizes.Add(DescribeSize(size));
                }

                segments.Add(new
                {
                    id = seg.Id.GetIdValue(),
                    kind = "pipe",
                    name = seg.Name,
                    material = GetElementNameOrNull(doc, seg.MaterialId),
                    schedule = GetElementNameOrNull(doc, seg.ScheduleTypeId),
                    description = string.IsNullOrWhiteSpace(seg.Description) ? null : seg.Description,
                    roughness_mm = ToMm(seg.Roughness, 6),
                    sizeCount = seg.SizeCount,
                    usedInSizeListsCount = inSizeLists,
                    usedInSizingCount = inSizing,
                    sizes = summaryOnly ? null : sizes,
                });
            }

            // 2) 風管尺寸（DuctSizeSettings 是全案一份，依 Round / Rectangular / Oval 分表）
            var ductShapes = new List<object>();
            var ductRoundNominalMm = new List<double>();
            string ductNote = null;

            if (includeDuct)
            {
                try
                {
                    DuctSizeSettings ductSettings = DuctSizeSettings.GetDuctSizeSettings(doc);
                    if (ductSettings == null)
                    {
                        ductNote = "此文件沒有 DuctSizeSettings（可能不是 MEP 樣板）。";
                    }
                    else
                    {
                        foreach (DuctShape shape in new[] { DuctShape.Round, DuctShape.Rectangular, DuctShape.Oval })
                        {
                            DuctSizes ductSizes = ductSettings[shape];
                            if (ductSizes == null) continue;

                            var shapeSizes = new List<object>();
                            int inSizeLists = 0;
                            int inSizing = 0;

                            foreach (MEPSize size in EnumerateDuctSizes(ductSizes).OrderBy(s => s.NominalDiameter))
                            {
                                if (size.UsedInSizeLists) inSizeLists++;
                                if (size.UsedInSizing) inSizing++;
                                if (usedOnly && !size.UsedInSizeLists && !size.UsedInSizing) continue;
                                if (!summaryOnly) shapeSizes.Add(DescribeDuctSize(size));
                                if (shape == DuctShape.Round)
                                    ductRoundNominalMm.Add(ToMm(size.NominalDiameter));
                            }

                            ductShapes.Add(new
                            {
                                shape = shape.ToString(),
                                sizeCount = ductSizes.Count,
                                usedInSizeListsCount = inSizeLists,
                                usedInSizingCount = inSizing,
                                sizes = summaryOnly ? null : shapeSizes,
                            });
                        }
                    }
                }
                catch (Exception ex)
                {
                    // 風管設定讀取失敗不應讓整支工具失敗——管段目錄仍有價值
                    ductNote = $"讀取風管尺寸失敗：{ex.Message}";
                }
            }

            return new
            {
                Success = true,
                UsedOnly = usedOnly,
                SummaryOnly = summaryOnly,
                IncludeDuct = includeDuct,
                SegmentNameFilter = hasNameFilter ? segmentName : null,
                SegmentCount = segments.Count,
                SegmentTotalCount = allPipeSegments.Count,
                PipeSizeCount = new { total = totalPipeSizes, reported = reportedPipeSizes },
                Segments = segments,
                DuctSizes = includeDuct ? ductShapes : null,
                DuctRoundSizes_mm = includeDuct ? ductRoundNominalMm : null,
                // Revit 的風管尺寸表只有 nominal 一個維度；MEPSize 的 inner/outer 對 duct 一律回固定佔位值
                // （實測 Round/Rectangular/Oval 三表全為 12 ft），輸出會誤導對帳，故只給 nominal。
                DuctSizeNote = includeDuct && ductShapes.Count > 0
                    ? "風管尺寸只有 nominal；Revit 對 duct 的 inner/outer 回傳固定佔位值，故不輸出。"
                    : null,
                DuctNote = ductNote,
                Message = BuildMepInventoryMessage(segments.Count, allPipeSegments.Count, totalPipeSizes, reportedPipeSizes, usedOnly, summaryOnly, hasNameFilter)
            };
        }

        /// <summary>組回傳訊息，把「篩掉了什麼」講清楚，避免把局部結果誤讀成全案盤點</summary>
        private static string BuildMepInventoryMessage(
            int shown, int total, int totalSizes, int reportedSizes,
            bool usedOnly, bool summaryOnly, bool hasNameFilter)
        {
            string scope = hasNameFilter
                ? $"符合名稱篩選的 {shown} 個管段（全案共 {total} 個）"
                : $"共 {shown} 個管段";

            string sizePart = summaryOnly
                ? $"，僅回傳統計（{totalSizes} 筆尺寸未逐筆列出）"
                : usedOnly
                    ? $"；僅列出勾選 Used in Size Lists / Used in Sizing 的尺寸（{reportedSizes}/{totalSizes}）"
                    : $"、{totalSizes} 筆管尺寸";

            return scope + sizePart + "。所有尺寸單位為 mm。";
        }

        /// <summary>管尺寸 → 輸出物件（內部單位 feet 轉 mm；pipe 的 inner/outer 是真實值）</summary>
        private static object DescribeSize(MEPSize size)
        {
            return new
            {
                nominal_mm = ToMm(size.NominalDiameter),
                inner_mm = ToMm(size.InnerDiameter),
                outer_mm = ToMm(size.OuterDiameter),
                usedInSizeLists = size.UsedInSizeLists,
                usedInSizing = size.UsedInSizing,
            };
        }

        /// <summary>
        /// 風管尺寸 → 輸出物件。刻意不輸出 inner/outer：Revit 的風管尺寸表只有 nominal 一個維度，
        /// MEPSize 的 inner/outer 對 duct 是固定佔位值（實測三種形狀全為 12 ft），輸出只會誤導。
        /// </summary>
        private static object DescribeDuctSize(MEPSize size)
        {
            return new
            {
                nominal_mm = ToMm(size.NominalDiameter),
                usedInSizeLists = size.UsedInSizeLists,
                usedInSizing = size.UsedInSizing,
            };
        }

        /// <summary>用 Revit 原生 iterator 走訪 DuctSizes（不依賴其泛型 IEnumerable 介面）</summary>
        private static IEnumerable<MEPSize> EnumerateDuctSizes(DuctSizes ductSizes)
        {
            DuctSizeIterator iterator = ductSizes.GetDuctSizeIterator();
            iterator.Reset();
            while (iterator.MoveNext())
            {
                MEPSize current = iterator.Current;
                if (current != null) yield return current;
            }
        }

        /// <summary>內部單位（feet）轉 mm</summary>
        private static double ToMm(double internalValue, int digits = 4)
        {
            return Math.Round(UnitUtils.ConvertFromInternalUnits(internalValue, UnitTypeId.Millimeters), digits);
        }

        /// <summary>取得 ElementId 對應元素的名稱；無效 id 或查無元素時回傳 null</summary>
        private static string GetElementNameOrNull(Document doc, ElementId id)
        {
            if (id == null || id == ElementId.InvalidElementId) return null;
            Element element = doc.GetElement(id);
            return element?.Name;
        }

        #endregion
    }
}
