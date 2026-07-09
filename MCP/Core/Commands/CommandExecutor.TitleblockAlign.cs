using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
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
        #region Titleblock 對齊（依參考 sheet 或絕對座標）

        /// <summary>
        /// 批次將多個 sheet 上的 titleblock 移動到同一個 anchor 座標。
        /// 解決 change_element_type 後 titleblock 沒跟著 instance origin 移動的問題。
        /// </summary>
        private object AlignTitleblocksOnSheets(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;

            string referenceSheetNumber = parameters["referenceSheetNumber"]?.Value<string>();
            JObject refPosObj = parameters["referencePositionMm"] as JObject;
            string anchor = parameters["anchor"]?.Value<string>() ?? "top-left";
            var targetSheetNumbers = parameters["targetSheetNumbers"]?.ToObject<List<string>>();
            bool dryRun = parameters["dryRun"]?.Value<bool>() ?? false;
            double toleranceMm = parameters["toleranceMm"]?.Value<double>() ?? 0.1;

            if (string.IsNullOrEmpty(referenceSheetNumber) && refPosObj == null)
                throw new Exception("必須指定 referenceSheetNumber 或 referencePositionMm 其中一個");
            if (!string.IsNullOrEmpty(referenceSheetNumber) && refPosObj != null)
                throw new Exception("referenceSheetNumber 和 referencePositionMm 不可同時指定，請擇一");

            var validAnchors = new HashSet<string> { "top-left", "top-right", "bottom-left", "bottom-right", "center" };
            if (!validAnchors.Contains(anchor))
                throw new Exception($"不支援的 anchor: '{anchor}' (允許: top-left/top-right/bottom-left/bottom-right/center)");

            XYZ refPoint;
            string referenceDescription;
            if (!string.IsNullOrEmpty(referenceSheetNumber))
            {
                var refSheet = FindSheetByNumber(doc, referenceSheetNumber);
                if (refSheet == null)
                    throw new Exception($"找不到 sheet number = '{referenceSheetNumber}'");
                var refTb = GetFirstTitleblock(doc, refSheet);
                if (refTb == null)
                    throw new Exception($"sheet '{referenceSheetNumber}' 上沒有 titleblock");
                refPoint = GetTitleblockAnchorPoint(refTb, refSheet, anchor);
                if (refPoint == null)
                    throw new Exception($"無法取得 sheet '{referenceSheetNumber}' titleblock 的 {anchor} 座標");
                referenceDescription = $"sheet {referenceSheetNumber} {anchor}";
            }
            else
            {
                double x = refPosObj["x"]?.Value<double>() ?? 0;
                double y = refPosObj["y"]?.Value<double>() ?? 0;
                refPoint = new XYZ(x / 304.8, y / 304.8, 0);
                referenceDescription = $"absolute ({x:F2}, {y:F2})mm {anchor}";
            }

            List<ViewSheet> targetSheets;
            if (targetSheetNumbers != null && targetSheetNumbers.Count > 0)
            {
                targetSheets = targetSheetNumbers
                    .Select(num => new { Num = num, Sheet = FindSheetByNumber(doc, num) })
                    .Where(x => x.Sheet != null)
                    .Select(x => x.Sheet)
                    .ToList();
            }
            else
            {
                targetSheets = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_Sheets)
                    .Cast<ViewSheet>()
                    .Where(s => !s.IsTemplate && !s.IsPlaceholder)
                    .ToList();
            }
            if (targetSheets.Count == 0)
                throw new Exception("找不到任何符合的 target sheet");

            var moved = new List<object>();
            var skipped = new List<object>();
            var failed = new List<object>();

            double toleranceFt = toleranceMm / 304.8;

            Transaction trans = null;
            try
            {
                if (!dryRun)
                {
                    trans = TransactionHelper.Begin(doc, "批次對齊 Titleblocks");
                    trans.Start();
                }

                foreach (var sheet in targetSheets)
                {
                    try
                    {
                        var tb = GetFirstTitleblock(doc, sheet);
                        if (tb == null)
                        {
                            skipped.Add(new
                            {
                                SheetNumber = sheet.SheetNumber,
                                SheetName = sheet.Name,
                                Reason = "sheet 上沒有 titleblock"
                            });
                            continue;
                        }

                        XYZ currentAnchor = GetTitleblockAnchorPoint(tb, sheet, anchor);
                        if (currentAnchor == null)
                        {
                            skipped.Add(new
                            {
                                SheetNumber = sheet.SheetNumber,
                                SheetName = sheet.Name,
                                Reason = $"無法取得 titleblock 的 {anchor} 座標"
                            });
                            continue;
                        }

                        XYZ delta = new XYZ(refPoint.X - currentAnchor.X, refPoint.Y - currentAnchor.Y, 0);
                        double deltaMagFt = Math.Sqrt(delta.X * delta.X + delta.Y * delta.Y);

                        bool isReferenceSheet = !string.IsNullOrEmpty(referenceSheetNumber)
                            && sheet.SheetNumber == referenceSheetNumber;

                        bool alreadyAligned = deltaMagFt < toleranceFt;

                        if (!dryRun && !alreadyAligned)
                        {
                            ElementTransformUtils.MoveElement(doc, tb.Id, delta);
                        }

                        var tbType = doc.GetElement(tb.GetTypeId()) as ElementType;
                        moved.Add(new
                        {
                            SheetNumber = sheet.SheetNumber,
                            SheetName = sheet.Name,
                            TitleblockId = tb.Id.GetIdValue(),
                            TitleblockTypeName = tbType?.Name ?? "Unknown",
                            IsReferenceSheet = isReferenceSheet,
                            AlreadyAligned = alreadyAligned,
                            OldAnchorMm = new { X = currentAnchor.X * 304.8, Y = currentAnchor.Y * 304.8 },
                            NewAnchorMm = new { X = refPoint.X * 304.8, Y = refPoint.Y * 304.8 },
                            DeltaMm = new
                            {
                                X = delta.X * 304.8,
                                Y = delta.Y * 304.8
                            }
                        });
                    }
                    catch (Exception ex)
                    {
                        failed.Add(new
                        {
                            SheetNumber = sheet.SheetNumber,
                            SheetName = sheet.Name,
                            Error = ex.Message
                        });
                    }
                }

                if (!dryRun && trans != null)
                {
                    trans.Commit();
                }
            }
            finally
            {
                if (trans != null && trans.HasStarted() && !trans.HasEnded())
                    trans.RollBack();
                trans?.Dispose();
            }

            return new
            {
                DryRun = dryRun,
                Reference = referenceDescription,
                Anchor = anchor,
                ReferencePointMm = new { X = refPoint.X * 304.8, Y = refPoint.Y * 304.8 },
                ToleranceMm = toleranceMm,
                MovedCount = moved.Count,
                SkippedCount = skipped.Count,
                FailedCount = failed.Count,
                Moved = moved,
                Skipped = skipped,
                Failed = failed
            };
        }

        private ViewSheet FindSheetByNumber(Document doc, string sheetNumber)
        {
            return new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Sheets)
                .Cast<ViewSheet>()
                .FirstOrDefault(s => s.SheetNumber == sheetNumber);
        }

        private Element GetFirstTitleblock(Document doc, ViewSheet sheet)
        {
            return new FilteredElementCollector(doc, sheet.Id)
                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                .FirstOrDefault();
        }

        private XYZ GetTitleblockAnchorPoint(Element titleblock, ViewSheet sheet, string anchor)
        {
            BoundingBoxXYZ bbox = titleblock.get_BoundingBox(sheet);
            if (bbox == null) return null;

            switch (anchor)
            {
                case "top-left": return new XYZ(bbox.Min.X, bbox.Max.Y, 0);
                case "top-right": return new XYZ(bbox.Max.X, bbox.Max.Y, 0);
                case "bottom-left": return new XYZ(bbox.Min.X, bbox.Min.Y, 0);
                case "bottom-right": return new XYZ(bbox.Max.X, bbox.Min.Y, 0);
                case "center":
                    return new XYZ(
                        (bbox.Min.X + bbox.Max.X) / 2.0,
                        (bbox.Min.Y + bbox.Max.Y) / 2.0,
                        0);
                default: return null;
            }
        }

        #endregion
    }
}
