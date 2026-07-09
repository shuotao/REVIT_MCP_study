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
        #region Viewport 定位（依據錨點）

        /// <summary>
        /// 批次將 viewports 移動到 sheet 上的指定位置（依 view 錨點 + sheet 參考點 + offset）
        /// </summary>
        private object PositionViewportsOnSheet(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;

            string viewAnchor = parameters["viewAnchor"]?.Value<string>();
            string sheetReference = parameters["sheetReference"]?.Value<string>();
            double offsetRightMm = parameters["offsetRightMm"]?.Value<double>() ?? 0;
            double offsetDownMm = parameters["offsetDownMm"]?.Value<double>() ?? 0;
            bool dryRun = parameters["dryRun"]?.Value<bool>() ?? false;

            if (string.IsNullOrEmpty(viewAnchor))
                throw new Exception("必須指定 viewAnchor (top-left / top-right / bottom-left / bottom-right / center)");
            if (string.IsNullOrEmpty(sheetReference))
                throw new Exception("必須指定 sheetReference (titleblock-* 或 sheet-* 角)");

            // 解析目標 viewports
            var targetViewports = ResolveTargetViewports(doc, parameters);
            if (targetViewports.Count == 0)
                throw new Exception("找不到任何符合的 viewport");

            var moved = new List<object>();
            var skipped = new List<object>();
            var failed = new List<object>();

            Transaction trans = null;
            try
            {
                if (!dryRun)
                {
                    trans = TransactionHelper.Begin(doc, "批次定位 Viewports");
                    trans.Start();
                }

                foreach (var vp in targetViewports)
                {
                    try
                    {
                        var sheet = doc.GetElement(vp.SheetId) as ViewSheet;
                        if (sheet == null)
                        {
                            skipped.Add(new { ViewportId = vp.Id.GetIdValue(), Reason = "找不到所在 sheet" });
                            continue;
                        }

                        XYZ refPoint = GetSheetReferencePoint(doc, sheet, sheetReference);
                        if (refPoint == null)
                        {
                            skipped.Add(new
                            {
                                ViewportId = vp.Id.GetIdValue(),
                                SheetNumber = sheet.SheetNumber,
                                Reason = $"無法取得 reference point '{sheetReference}'（可能該 sheet 沒有 title block）"
                            });
                            continue;
                        }

                        // 期望的錨點位置 (Revit Y+ 朝上, 所以 down = -Y)
                        XYZ desiredAnchorPos = new XYZ(
                            refPoint.X + offsetRightMm / 304.8,
                            refPoint.Y - offsetDownMm / 304.8,
                            0);

                        // 取得 viewport box outline (報告用)
                        Outline boxOutline = vp.GetBoxOutline();
                        XYZ boxMin = boxOutline.MinimumPoint;
                        XYZ boxMax = boxOutline.MaximumPoint;
                        XYZ currentBoxCenter = vp.GetBoxCenter();

                        // ─── 用「view.Outline UV ↔ boxOutline 對應」反推 view.Origin 在 sheet 上的位置 ───
                        // 然後用 cropbox 在 model 中的位置算出 cropbox 在 sheet 上的真實中心
                        var view = doc.GetElement(vp.ViewId) as View;
                        double cropSheetWidthFt;
                        double cropSheetHeightFt;
                        XYZ currentCropboxCenter;

                        try
                        {
                            BoundingBoxXYZ cb = view.CropBox;
                            BoundingBoxUV viewOutline = view.Outline;
                            double scale = view.Scale > 0 ? view.Scale : 1;

                            cropSheetWidthFt = Math.Abs(cb.Max.X - cb.Min.X) / scale;
                            cropSheetHeightFt = Math.Abs(cb.Max.Y - cb.Min.Y) / scale;

                            // Cropbox 中心在 model coords (透過 cb.Transform)
                            XYZ cropCenterLocal = new XYZ(
                                (cb.Min.X + cb.Max.X) / 2.0,
                                (cb.Min.Y + cb.Max.Y) / 2.0,
                                (cb.Min.Z + cb.Max.Z) / 2.0);
                            XYZ cropCenterModel = cb.Transform != null
                                ? cb.Transform.OfPoint(cropCenterLocal)
                                : cropCenterLocal;

                            // 推算 view.Origin 在 sheet 上的對應點 (sheet ft)
                            // 公式: view.Origin 對應 sheet 點 = boxCenter - viewOutlineCenter
                            // 用 CENTER 而非 Min 是因為 boxOutline 在每邊都有 ~0.01 ft (3mm) 對稱 padding,
                            // viewOutline 沒有. 用 Min 端會被 padding 偏移; 用 Center 對稱 padding 自動抵消.
                            // (這個 3mm 誤差是 2026-05-01 視覺實測抓到的)
                            double viewOutlineCenterU = (viewOutline.Min.U + viewOutline.Max.U) / 2.0;
                            double viewOutlineCenterV = (viewOutline.Min.V + viewOutline.Max.V) / 2.0;
                            double sheetOriginX = currentBoxCenter.X - viewOutlineCenterU;
                            double sheetOriginY = currentBoxCenter.Y - viewOutlineCenterV;

                            // Cropbox 中心在 sheet (ft)
                            XYZ viewOrigin = view.Origin;
                            double cropCenterSheetX = sheetOriginX + (cropCenterModel.X - viewOrigin.X) / scale;
                            double cropCenterSheetY = sheetOriginY + (cropCenterModel.Y - viewOrigin.Y) / scale;
                            currentCropboxCenter = new XYZ(cropCenterSheetX, cropCenterSheetY, 0);
                        }
                        catch
                        {
                            // Fallback: 退回到 hypothesis A
                            cropSheetWidthFt = boxMax.X - boxMin.X;
                            cropSheetHeightFt = boxMax.Y - boxMin.Y;
                            currentCropboxCenter = currentBoxCenter;
                        }

                        // Cropbox 邊界 (用 currentCropboxCenter)
                        XYZ vpMin = new XYZ(currentCropboxCenter.X - cropSheetWidthFt / 2.0, currentCropboxCenter.Y - cropSheetHeightFt / 2.0, 0);
                        XYZ vpMax = new XYZ(currentCropboxCenter.X + cropSheetWidthFt / 2.0, currentCropboxCenter.Y + cropSheetHeightFt / 2.0, 0);

                        XYZ anchorOffsetFromCropboxCenter = GetAnchorOffsetFromCenter(viewAnchor, vpMin, vpMax, currentCropboxCenter);

                        XYZ desiredCropboxCenter = new XYZ(
                            desiredAnchorPos.X - anchorOffsetFromCropboxCenter.X,
                            desiredAnchorPos.Y - anchorOffsetFromCropboxCenter.Y,
                            0);

                        // Cropbox 跟 viewport box 一起平移, delta 一致
                        XYZ delta = new XYZ(
                            desiredCropboxCenter.X - currentCropboxCenter.X,
                            desiredCropboxCenter.Y - currentCropboxCenter.Y,
                            0);
                        XYZ desiredCenter = new XYZ(
                            currentBoxCenter.X + delta.X,
                            currentBoxCenter.Y + delta.Y,
                            0);

                        if (!dryRun)
                        {
                            vp.SetBoxCenter(desiredCenter);
                        }

                        // 算 cropbox 在 sheet 上的左上角 (預期值, 用 desiredCropboxCenter 為基準)
                        double cropTopLeftX = desiredCropboxCenter.X - cropSheetWidthFt / 2.0;
                        double cropTopLeftY = desiredCropboxCenter.Y + cropSheetHeightFt / 2.0;
                        moved.Add(new
                        {
                            ViewportId = vp.Id.GetIdValue(),
                            ViewName = view?.Name ?? "Unknown",
                            ViewType = view?.ViewType.ToString() ?? "Unknown",
                            SheetNumber = sheet.SheetNumber,
                            SheetName = sheet.Name,
                            // Cropbox 尺寸 (扣掉 label/title bar 後的純 view 範圍)
                            CropboxWidthMm = (vpMax.X - vpMin.X) * 304.8,
                            CropboxHeightMm = (vpMax.Y - vpMin.Y) * 304.8,
                            // Box outline 尺寸 (含 label, 即 SetBoxCenter 用的那個 box)
                            BoxWidthMm = (boxMax.X - boxMin.X) * 304.8,
                            BoxHeightMm = (boxMax.Y - boxMin.Y) * 304.8,
                            // 預期 cropbox 左上角位置 (給 user 確認 anchor)
                            DesiredCropboxTopLeftMm = new { X = cropTopLeftX * 304.8, Y = cropTopLeftY * 304.8 },
                            OldBoxCenterMm = new { X = currentBoxCenter.X * 304.8, Y = currentBoxCenter.Y * 304.8 },
                            NewBoxCenterMm = new { X = desiredCenter.X * 304.8, Y = desiredCenter.Y * 304.8 },
                            DeltaMm = new
                            {
                                X = (desiredCenter.X - currentBoxCenter.X) * 304.8,
                                Y = (desiredCenter.Y - currentBoxCenter.Y) * 304.8
                            }
                        });
                    }
                    catch (Exception ex)
                    {
                        failed.Add(new { ViewportId = vp.Id.GetIdValue(), Error = ex.Message });
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
                ViewAnchor = viewAnchor,
                SheetReference = sheetReference,
                OffsetRightMm = offsetRightMm,
                OffsetDownMm = offsetDownMm,
                MovedCount = moved.Count,
                SkippedCount = skipped.Count,
                FailedCount = failed.Count,
                Moved = moved,
                Skipped = skipped,
                Failed = failed
            };
        }

        /// <summary>
        /// 解析目標 viewports：依優先序 viewportIds > viewIds > viewNames > viewNameContains
        /// 額外可用 sheetNumbers 和 viewTypeFilter 過濾
        /// </summary>
        private List<Viewport> ResolveTargetViewports(Document doc, JObject parameters)
        {
            var viewportIds = parameters["viewportIds"]?.ToObject<List<IdType>>();
            var viewIds = parameters["viewIds"]?.ToObject<List<IdType>>();
            var viewNames = parameters["viewNames"]?.ToObject<List<string>>();
            string viewNameContains = parameters["viewNameContains"]?.Value<string>();
            var sheetNumbers = parameters["sheetNumbers"]?.ToObject<List<string>>();
            var viewTypeFilter = parameters["viewTypeFilter"]?.ToObject<List<string>>();

            List<Viewport> viewports;

            if (viewportIds != null && viewportIds.Count > 0)
            {
                viewports = viewportIds
                    .Select(id => doc.GetElement(id.ToElementId()) as Viewport)
                    .Where(v => v != null).ToList();
            }
            else
            {
                var allViewports = new FilteredElementCollector(doc)
                    .OfClass(typeof(Viewport))
                    .Cast<Viewport>()
                    .ToList();

                viewports = allViewports;

                if (viewIds != null && viewIds.Count > 0)
                {
                    var viewIdSet = new HashSet<IdType>(viewIds);
                    viewports = viewports.Where(vp => viewIdSet.Contains(vp.ViewId.GetIdValue())).ToList();
                }
                else if (viewNames != null && viewNames.Count > 0)
                {
                    var nameSet = new HashSet<string>(viewNames);
                    viewports = viewports.Where(vp =>
                    {
                        var view = doc.GetElement(vp.ViewId) as View;
                        return view != null && nameSet.Contains(view.Name);
                    }).ToList();
                }
                else if (!string.IsNullOrEmpty(viewNameContains))
                {
                    viewports = viewports.Where(vp =>
                    {
                        var view = doc.GetElement(vp.ViewId) as View;
                        return view != null && view.Name.IndexOf(viewNameContains, StringComparison.OrdinalIgnoreCase) >= 0;
                    }).ToList();
                }
                else if (sheetNumbers == null || sheetNumbers.Count == 0)
                {
                    throw new Exception("必須指定 viewportIds / viewIds / viewNames / viewNameContains 或 sheetNumbers 其中一個");
                }
            }

            // Sheet number filter (always applied if specified)
            if (sheetNumbers != null && sheetNumbers.Count > 0)
            {
                var sheetNumSet = new HashSet<string>(sheetNumbers);
                viewports = viewports.Where(vp =>
                {
                    var sheet = doc.GetElement(vp.SheetId) as ViewSheet;
                    return sheet != null && sheetNumSet.Contains(sheet.SheetNumber);
                }).ToList();
            }

            // View type filter
            if (viewTypeFilter != null && viewTypeFilter.Count > 0)
            {
                var typeSet = new HashSet<string>(viewTypeFilter, StringComparer.OrdinalIgnoreCase);
                viewports = viewports.Where(vp =>
                {
                    var view = doc.GetElement(vp.ViewId) as View;
                    return view != null && typeSet.Contains(view.ViewType.ToString());
                }).ToList();
            }

            return viewports;
        }

        /// <summary>
        /// 取得 sheet 上指定 reference 的座標 (Revit feet, Y+ 朝上)
        /// </summary>
        private XYZ GetSheetReferencePoint(Document doc, ViewSheet sheet, string reference)
        {
            if (reference.StartsWith("titleblock-"))
            {
                var tb = new FilteredElementCollector(doc, sheet.Id)
                    .OfCategory(BuiltInCategory.OST_TitleBlocks)
                    .FirstOrDefault();
                if (tb == null) return null;

                BoundingBoxXYZ bbox = tb.get_BoundingBox(sheet);
                if (bbox == null) return null;

                switch (reference)
                {
                    case "titleblock-top-left": return new XYZ(bbox.Min.X, bbox.Max.Y, 0);
                    case "titleblock-top-right": return new XYZ(bbox.Max.X, bbox.Max.Y, 0);
                    case "titleblock-bottom-left": return new XYZ(bbox.Min.X, bbox.Min.Y, 0);
                    case "titleblock-bottom-right": return new XYZ(bbox.Max.X, bbox.Min.Y, 0);
                    default: return null;
                }
            }
            else if (reference.StartsWith("sheet-"))
            {
                BoundingBoxUV outline = sheet.Outline;
                switch (reference)
                {
                    case "sheet-top-left": return new XYZ(outline.Min.U, outline.Max.V, 0);
                    case "sheet-top-right": return new XYZ(outline.Max.U, outline.Max.V, 0);
                    case "sheet-bottom-left": return new XYZ(outline.Min.U, outline.Min.V, 0);
                    case "sheet-bottom-right": return new XYZ(outline.Max.U, outline.Min.V, 0);
                    default: return null;
                }
            }
            return null;
        }

        /// <summary>
        /// 給定 viewport 的 outline + center，回傳 anchor point 相對於 center 的 offset
        /// </summary>
        private XYZ GetAnchorOffsetFromCenter(string anchor, XYZ outlineMin, XYZ outlineMax, XYZ center)
        {
            XYZ anchorPos;
            switch (anchor)
            {
                case "top-left": anchorPos = new XYZ(outlineMin.X, outlineMax.Y, 0); break;
                case "top-right": anchorPos = new XYZ(outlineMax.X, outlineMax.Y, 0); break;
                case "bottom-left": anchorPos = new XYZ(outlineMin.X, outlineMin.Y, 0); break;
                case "bottom-right": anchorPos = new XYZ(outlineMax.X, outlineMin.Y, 0); break;
                case "center": anchorPos = center; break;
                default: throw new Exception($"不支援的 viewAnchor: '{anchor}' (允許: top-left/top-right/bottom-left/bottom-right/center)");
            }
            return new XYZ(anchorPos.X - center.X, anchorPos.Y - center.Y, 0);
        }

        #endregion

        #region Viewport 標題位置（LabelOffset）

        /// <summary>
        /// 批次移動 viewport 標題（label line + text）的位置。
        /// 模式:
        ///   - below-view-center: 將標題置中放在 cropbox 正下方 + gapMm 距離
        ///   - reset: LabelOffset 還原為 XYZ.Zero（Revit 預設位置）
        /// </summary>
        private object MoveViewportTitles(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;

            string mode = parameters["mode"]?.Value<string>() ?? "below-view-center";
            double gapMm = parameters["gapMm"]?.Value<double>() ?? 5.0;
            bool dryRun = parameters["dryRun"]?.Value<bool>() ?? false;

            var validModes = new HashSet<string> { "below-view-center", "reset" };
            if (!validModes.Contains(mode))
                throw new Exception($"不支援的 mode: '{mode}' (允許: below-view-center / reset)");

            var targetViewports = ResolveTargetViewports(doc, parameters);
            if (targetViewports.Count == 0)
                throw new Exception("找不到任何符合的 viewport");

            var moved = new List<object>();
            var skipped = new List<object>();
            var failed = new List<object>();

            Transaction trans = null;
            try
            {
                if (!dryRun)
                {
                    trans = TransactionHelper.Begin(doc, "批次移動 Viewport 標題");
                    trans.Start();
                }

                foreach (var vp in targetViewports)
                {
                    try
                    {
                        var view = doc.GetElement(vp.ViewId) as View;
                        var sheet = doc.GetElement(vp.SheetId) as ViewSheet;
                        if (view == null)
                        {
                            skipped.Add(new { ViewportId = vp.Id.GetIdValue(), Reason = "找不到對應 view" });
                            continue;
                        }

                        XYZ currentBoxCenter = vp.GetBoxCenter();
                        XYZ currentLabelOffset = vp.LabelOffset ?? XYZ.Zero;

                        // 取得 current label center (in sheet coords)
                        XYZ currentLabelCenter = null;
                        try
                        {
                            Outline labelOutline = vp.GetLabelOutline();
                            if (labelOutline != null)
                            {
                                currentLabelCenter = new XYZ(
                                    (labelOutline.MinimumPoint.X + labelOutline.MaximumPoint.X) / 2.0,
                                    (labelOutline.MinimumPoint.Y + labelOutline.MaximumPoint.Y) / 2.0,
                                    0);
                            }
                        }
                        catch { }

                        XYZ newLabelOffset = currentLabelOffset;
                        XYZ targetLabelCenter = null;

                        if (mode == "reset")
                        {
                            newLabelOffset = XYZ.Zero;
                        }
                        else if (mode == "below-view-center")
                        {
                            if (currentLabelCenter == null)
                            {
                                skipped.Add(new
                                {
                                    ViewportId = vp.Id.GetIdValue(),
                                    ViewName = view.Name,
                                    Reason = "無法取得 LabelOutline（標題可能不可見）"
                                });
                                continue;
                            }

                            // ─── 反推 cropbox 在 sheet 上的中心 + 底邊 ───
                            BoundingBoxXYZ cb = view.CropBox;
                            BoundingBoxUV viewOutline = null;
                            try { viewOutline = view.Outline; } catch { }

                            if (cb == null || viewOutline == null)
                            {
                                skipped.Add(new
                                {
                                    ViewportId = vp.Id.GetIdValue(),
                                    ViewName = view.Name,
                                    Reason = "無 CropBox 或 ViewOutline"
                                });
                                continue;
                            }

                            double scale = view.Scale > 0 ? view.Scale : 1;
                            double cropHeightFt = Math.Abs(cb.Max.Y - cb.Min.Y) / scale;

                            XYZ cropCenterLocal = new XYZ(
                                (cb.Min.X + cb.Max.X) / 2.0,
                                (cb.Min.Y + cb.Max.Y) / 2.0,
                                (cb.Min.Z + cb.Max.Z) / 2.0);
                            XYZ cropCenterModel = cb.Transform != null
                                ? cb.Transform.OfPoint(cropCenterLocal)
                                : cropCenterLocal;

                            // 公式同 PositionViewportsOnSheet：用 viewOutline center 抵消 boxOutline padding
                            double viewOutlineCenterU = (viewOutline.Min.U + viewOutline.Max.U) / 2.0;
                            double viewOutlineCenterV = (viewOutline.Min.V + viewOutline.Max.V) / 2.0;
                            double sheetOriginX = currentBoxCenter.X - viewOutlineCenterU;
                            double sheetOriginY = currentBoxCenter.Y - viewOutlineCenterV;

                            XYZ viewOrigin = view.Origin;
                            double cropCenterSheetX = sheetOriginX + (cropCenterModel.X - viewOrigin.X) / scale;
                            double cropCenterSheetY = sheetOriginY + (cropCenterModel.Y - viewOrigin.Y) / scale;
                            double cropBottomSheetY = cropCenterSheetY - cropHeightFt / 2.0;

                            double gapFt = gapMm / 304.8;
                            targetLabelCenter = new XYZ(cropCenterSheetX, cropBottomSheetY - gapFt, 0);

                            XYZ delta = targetLabelCenter - currentLabelCenter;
                            newLabelOffset = currentLabelOffset + delta;
                        }

                        if (!dryRun)
                        {
                            vp.LabelOffset = newLabelOffset;
                        }

                        object oldCenterMm = currentLabelCenter != null
                            ? (object)new { X = currentLabelCenter.X * 304.8, Y = currentLabelCenter.Y * 304.8 }
                            : null;
                        object newCenterMm = targetLabelCenter != null
                            ? (object)new { X = targetLabelCenter.X * 304.8, Y = targetLabelCenter.Y * 304.8 }
                            : null;

                        moved.Add(new
                        {
                            ViewportId = vp.Id.GetIdValue(),
                            ViewName = view.Name,
                            ViewType = view.ViewType.ToString(),
                            SheetNumber = sheet?.SheetNumber,
                            SheetName = sheet?.Name,
                            OldLabelOffsetMm = new { X = currentLabelOffset.X * 304.8, Y = currentLabelOffset.Y * 304.8 },
                            NewLabelOffsetMm = new { X = newLabelOffset.X * 304.8, Y = newLabelOffset.Y * 304.8 },
                            OldLabelCenterMm = oldCenterMm,
                            NewLabelCenterMm = newCenterMm
                        });
                    }
                    catch (Exception ex)
                    {
                        failed.Add(new { ViewportId = vp.Id.GetIdValue(), Error = ex.Message });
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
                Mode = mode,
                GapMm = gapMm,
                MovedCount = moved.Count,
                SkippedCount = skipped.Count,
                FailedCount = failed.Count,
                Moved = moved,
                Skipped = skipped,
                Failed = failed
            };
        }

        #endregion
    }
}
