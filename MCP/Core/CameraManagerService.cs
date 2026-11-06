using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ClosedXML.Excel;

namespace RevitMCP.Core
{
    /// <summary>
    /// 相機管理停靠面板的 Revit 讀寫邏輯，刻意不含任何 WPF 型別 —— 面板（UI 層）只呼叫這裡的
    /// 方法，這裡只呼叫 Revit API。拆開的理由：
    ///   1. 這批方法全部假設「已經在 Revit API 執行環境內」（ExternalEvent 的 Execute 內，
    ///      或 DocumentChanged／ViewActivated 這類 Revit 自己觸發、本來就在 API context 的
    ///      事件），呼叫端（UI 層）要自己決定何時經 ExternalEventManager 排程。
    ///   2. 第 2 階段（Key Plan／相機標記）要加新方法時，不需要碰任何 WPF 程式碼。
    /// </summary>
    internal static class CameraManagerService
    {
        internal const double LowHeightWarningCm = 120.0;
        internal const double HighHeightWarningCm = 200.0;

        // ══════════════════════════════════════════════════════════
        // 4.2 讀取相機清單 + 平面／樓層清單
        // ══════════════════════════════════════════════════════════

        internal static CameraManagerSnapshot ReadSnapshot(Document doc)
        {
            // 2026-09-26 第四輪：快照讀取計時（⏱）＋房間／樓層快取命中率。
            using (CameraPerf perf = CameraPerf.Begin("讀相機快照"))
            {
                CameraManagerSnapshot s = ReadSnapshotCore(doc, perf);
                perf.Note($"{s?.Cameras?.Count ?? 0} 台；{CameraKeyPlanService.TakeStoreyCacheStats()}");
                return s;
            }
        }

        private static CameraManagerSnapshot ReadSnapshotCore(Document doc, CameraPerf perf)
        {
            var snapshot = new CameraManagerSnapshot();
            if (doc == null)
            {
                snapshot.ErrorMessage = "目前沒有開啟中的文件。";
                return snapshot;
            }
            snapshot.DocumentTitle = doc.Title;

            try
            {
                List<Level> levels = CameraMath.GetStoreyLevelsSorted(doc);
                foreach (Level l in levels)
                {
                    snapshot.Levels.Add(new LevelOption
                    {
                        LevelId = l.Id,
                        Name = l.Name,
                        ProjectElevationFeet = l.ProjectElevation
                    });
                }

                List<View3D> cameraViews = new FilteredElementCollector(doc)
                    .OfClass(typeof(View3D))
                    .Cast<View3D>()
                    .Where(v => !v.IsTemplate && v.IsPerspective && !CameraKeyPlanService.IsSheetPerspectiveCopy(v)) // 2026-09-25：出圖用透視複本不是相機
                    .OrderBy(v => v.Name)
                    .ToList();

                var levelCameraCount = new Dictionary<ElementId, int>();
                HashSet<string> camerasWithSection;
                try { camerasWithSection = CameraKeyPlanService.GetCameraUniqueIdsWithSection(doc); }
                catch (Exception ex)
                {
                    Logger.Error("相機管理：讀取 Key Plan 剖面連結失敗（略過，不影響清單）", ex);
                    camerasWithSection = new HashSet<string>();
                }

                perf?.Lap("樓層/相機清單/剖面連結");
                foreach (View3D view in cameraViews)
                {
                    CameraRow row = BuildCameraRow(view, levels);
                    row.HasKeyPlanSection = row.UniqueId != null && camerasWithSection.Contains(row.UniqueId);
                    // 2026-09-25 Wave 3：視角版本名稱（右鍵「視角版本 ▸」用；讀取失敗＝空清單，不影響清單）
                    // 2026-09-26 第四輪：新到舊排序＋目前視角符合的版本（選單打 ✓）。
                    try
                    {
                        List<CameraKeyPlanService.CameraViewVersion> vers = CameraKeyPlanService.ReadVersionsNewestFirst(view);
                        row.VersionNames = vers.Select(v => v.Name).ToList();
                        row.CurrentVersionName = vers.Count > 0 ? CameraKeyPlanService.FindCurrentVersionName(view, vers) : null;
                    }
                    catch (Exception exV) { Logger.Error("相機管理：讀取視角版本失敗（略過）", exV); }
                    snapshot.Cameras.Add(row);

                    if (row.LevelId != null)
                    {
                        levelCameraCount[row.LevelId] = levelCameraCount.TryGetValue(row.LevelId, out int c) ? c + 1 : 1;
                    }
                }

                // 2026-09-25：室內相機的樓層＝房間的 Level，可能不是建築樓層（例：房間放在非 Building Story 的 Level）；
                // 補進樓層清單，讓清單「樓層」欄與位置圖樓層過濾找得到它（依高程排序）。
                foreach (CameraRow r in snapshot.Cameras)
                {
                    if (r.LevelId == null || snapshot.Levels.Any(l => l.LevelId == r.LevelId)) continue;
                    if (doc.GetElement(r.LevelId) is Level extra)
                        snapshot.Levels.Add(new LevelOption { LevelId = extra.Id, Name = extra.Name, ProjectElevationFeet = extra.ProjectElevation });
                }
                snapshot.Levels.Sort((a, b) => a.ProjectElevationFeet.CompareTo(b.ProjectElevationFeet));

                perf?.Lap("每台相機（房間/FOV/版本）");
                // 2026-09-25 Wave 3：圖框類型（④ ▾ 圖紙圖框 ▸）；失敗只記 log。
                try { snapshot.TitleBlocks = CameraKeyPlanService.ListTitleBlockTypes(doc); }
                catch (Exception exTb) { Logger.Error("相機管理：讀取圖框類型失敗（略過）", exTb); }

                // 4.8：底圖只提供「已啟用 CropBox」的平面（沒有的話面板改畫中性格線，見 HasCroppedPlan）。
                List<ViewPlan> croppedPlanViews = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewPlan))
                    .Cast<ViewPlan>()
                    .Where(v => !v.IsTemplate &&
                        (v.ViewType == ViewType.FloorPlan || v.ViewType == ViewType.CeilingPlan || v.ViewType == ViewType.AreaPlan) &&
                        v.CropBoxActive)
                    // 2026-09-26 第七輪：本工具產生的 Key Plan 平面／總覽（ES 標記或「Key Plan-」開頭）不當底圖候選，連「全」模式也不列。
                    .Where(v => !CameraKeyPlanService.IsToolGeneratedView(v))
                    .OrderBy(v => v.Name)
                    .ToList();

                snapshot.HasCroppedPlan = croppedPlanViews.Count > 0;
                var storeyIds = new HashSet<long>(levels.Select(l => (long)l.Id.GetIdValue()));

                foreach (ViewPlan pv in croppedPlanViews)
                {
                    var opt = new PlanViewOption
                    {
                        ViewId = pv.Id,
                        GenLevelId = pv.GenLevel != null ? pv.GenLevel.Id : null,
                        PlanName = pv.Name,
                        LevelName = pv.GenLevel?.Name,
                        DisplayName = pv.Name + "（" + pv.ViewType + "）",
                        IsFloorPlan = pv.ViewType == ViewType.FloorPlan,
                        GenLevelElevationFeet = pv.GenLevel != null ? (double?)pv.GenLevel.ProjectElevation : null,
                        GenLevelIsStorey = pv.GenLevel != null && storeyIds.Contains(pv.GenLevel.Id.GetIdValue())
                    };
                    // 2026-09-25（§4.8 底圖挑選）：攤平裁剪框供「包含相機／最大面積」判斷；失敗只記 log。
                    try
                    {
                        ElementId primaryId = pv.GetPrimaryViewId();
                        opt.IsDependent = primaryId != ElementId.InvalidElementId;
                        opt.PrimaryViewId = opt.IsDependent ? primaryId : null;
                        BoundingBoxXYZ cb = pv.CropBox;
                        if (cb != null)
                        {
                            Transform tf = cb.Transform ?? Transform.Identity;
                            opt.CropOriginX = tf.Origin.X; opt.CropOriginY = tf.Origin.Y;
                            opt.CropBasisXX = tf.BasisX.X; opt.CropBasisXY = tf.BasisX.Y;
                            opt.CropBasisYX = tf.BasisY.X; opt.CropBasisYY = tf.BasisY.Y;
                            opt.CropMinX = cb.Min.X; opt.CropMinY = cb.Min.Y;
                            opt.CropMaxX = cb.Max.X; opt.CropMaxY = cb.Max.Y;
                            opt.HasCrop = true;
                        }
                    }
                    catch (Exception exCrop)
                    {
                        Logger.Error($"相機管理：讀取平面「{pv.Name}」裁剪框失敗（不參與包含相機判斷）", exCrop);
                    }
                    snapshot.PlanViews.Add(opt);
                }

                snapshot.DefaultPlanViewId = ElementId.InvalidElementId;
                if (croppedPlanViews.Count > 0)
                {
                    ElementId busiestLevelId = levelCameraCount.Count > 0
                        ? levelCameraCount.OrderByDescending(kv => kv.Value).First().Key
                        : null;

                    // 2026-09-25（§4.8 第二版）：相機最多的樓層，用共用的 BaseMapPlanChooser.Choose（步驟 0–4）。
                    PlanViewOption chosenOpt = null;
                    string why = "";
                    if (busiestLevelId != null)
                    {
                        List<CameraRow> onLevel = snapshot.Cameras
                            .Where(r => r.LevelId != null && r.LevelId.GetIdValue() == busiestLevelId.GetIdValue()).ToList();
                        List<double[]> eyes = onLevel.Select(r => new[] { r.EyeXFeet, r.EyeYFeet, r.EyeZFeet }).ToList();
                        chosenOpt = BaseMapPlanChooser.Choose(snapshot.PlanViews, busiestLevelId, eyes, snapshot.Levels, out why, out _,
                            SourcePlanHint.MostCommon(onLevel));
                    }
                    if (chosenOpt == null)
                    {
                        chosenOpt = snapshot.PlanViews.OrderBy(p => p.IsDependent ? 1 : 0).ThenByDescending(p => p.CropAreaSqFeet).First();
                        why = (busiestLevelId == null ? "沒有相機樓層" : "相機最多的樓層沒有已裁剪平面") + " → 全部平面中最大面積";
                    }
                    snapshot.DefaultPlanViewId = chosenOpt.ViewId;
                    Logger.Info($"相機管理：預設底圖 {chosenOpt.CropLogText}（{why}）");
                }
                perf?.Lap("圖框/平面清單/預設底圖");
            }
            catch (Exception ex)
            {
                snapshot.ErrorMessage = "讀取相機清單失敗：" + ex.Message;
            }

            return snapshot;
        }

        private static readonly Dictionary<string, string> _lastIoLog = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>單一相機視圖 → CameraRow。給 ReadSnapshot 和 DocumentChanged 局部刷新共用。</summary>
        internal static CameraRow BuildCameraRow(View3D view, IList<Level> levelsSorted)
        {
            CameraMath.CameraGeometry geom = CameraMath.ComputeGeometry(view, levelsSorted);
            double? heightCm = geom.AboveLevelBelowMm.HasValue
                ? (double?)Math.Round(geom.AboveLevelBelowMm.Value / 10.0, 1)
                : null;

            string warning = "";
            if (heightCm.HasValue)
            {
                if (heightCm.Value < LowHeightWarningCm) warning = "偏低";
                else if (heightCm.Value > HighHeightWarningCm) warning = "偏高";
            }

            Parameter farClipParam = CameraMath.FindFarClipOffsetParameter(view);
            double? farClipCm = farClipParam != null
                ? (double?)Math.Round(CameraMath.FeetToMm(farClipParam.AsDouble()) / 10.0, 1)
                : null;

            CameraMath.PerspectiveFov fov = null;
            try { fov = CameraMath.ComputePerspectiveFov(view); }
            catch (Exception ex) { Logger.Error("相機管理面板：視野角計算失敗（FOV 欄留空）", ex); }

            // 2026-09-25 室內／室外判定（同一鍵 4 視圖的 FindEyeRoom）；失敗＝null（清單顯示 ?），不影響其他欄位。
            bool? isOutdoor = CameraKeyPlanService.ClassifyIndoorOutdoor(view, geom, out double outdoorFanR, out string classLog);
            // 2026-09-26 第四輪：同一台相機判定內容沒變就不重複寫 log（每次快照 21 行 log 本身也有成本）。
            string ioLine = $"相機管理：「{view.Name}」室內／室外＝{(isOutdoor.HasValue ? (isOutdoor.Value ? "室外" : "室內") : "?")}（{classLog}）" +
                            (isOutdoor == true ? $" 扇形半徑={CameraMath.FeetToMm(outdoorFanR):F0}mm" : "");
            string ioKey = view.UniqueId ?? view.Id.GetIdValue().ToString();
            if (!_lastIoLog.TryGetValue(ioKey, out string prevIo) || prevIo != ioLine)
            {
                _lastIoLog[ioKey] = ioLine;
                Logger.Info(ioLine);
            }

            // 2026-09-25（§4.8 步驟 0）：相機放置時的平面（ES 記錄；沒有＝null）。
            ViewPlan src = CameraSourcePlanStore.TryGetSourcePlan(view);
            ElementId srcPrimary = null;
            bool srcCropped = false;
            if (src != null)
            {
                try
                {
                    srcCropped = src.CropBoxActive;
                    ElementId pid = src.GetPrimaryViewId();
                    if (pid != ElementId.InvalidElementId) srcPrimary = pid;
                }
                catch (Exception exSrc) { Logger.Error("相機管理：讀取放置平面屬性失敗", exSrc); }
            }

            return new CameraRow
            {
                SourcePlanId = src?.Id,
                SourcePlanName = src?.Name,
                SourcePlanCropped = srcCropped,
                SourcePlanPrimaryId = srcPrimary,
                IsOutdoor = isOutdoor,
                OutdoorFanRadiusFeet = outdoorFanR,
                SectionRotate90 = CameraKeyPlanService.GetCameraSectionRotate90(view),
                ViewId = view.Id,
                UniqueId = view.UniqueId,
                Name = view.Name,
                LevelName = geom.LevelBelow != null ? geom.LevelBelow.Name : "(找不到樓層)",
                StoreySource = geom.StoreySource,
                StoreyLog = geom.StoreyLog,
                LevelId = geom.LevelBelow != null ? geom.LevelBelow.Id : null,
                HeightAboveLevelCm = heightCm,
                HorizontalAngleDeg = CameraMath.ComputeHorizontalAngleDeg(geom.Forward),
                PitchDeg = geom.PitchDeg,
                EyeElevationDisplay = CameraMath.GetEyeElevationDisplayString(view),
                FovEstimateDeg = fov?.HFovDeg.HasValue == true ? (double?)Math.Round(fov.HFovDeg.Value, 1) : null,
                FovLeftDeg = fov?.LeftDeg,
                FovRightDeg = fov?.RightDeg,
                FarClipCm = farClipCm,
                FarClipEditable = farClipParam != null && !farClipParam.IsReadOnly,
                EyeXFeet = geom.Eye.X,
                EyeYFeet = geom.Eye.Y,
                EyeZFeet = geom.Eye.Z,
                ForwardXFeet = geom.Forward.X,
                ForwardYFeet = geom.Forward.Y,
                ForwardZFeet = geom.Forward.Z,
                InMapRange = false, // 底圖畫完才知道，DrawMap 端回填
                HeightWarning = warning
            };
        }

        // ══════════════════════════════════════════════════════════
        // 4.8 底圖匯出
        // ══════════════════════════════════════════════════════════

        /// <summary>
        /// 匯出平面視圖成底圖，並記下這張圖對應的模型範圍（CropBox，view 自身座標系＋
        /// 轉回模型座標的 Transform）。只會被叫在 CropBoxActive==true 的平面（ReadSnapshot
        /// 已過濾），修掉「底圖用了沒裁剪的平面 → CropBox 與實際匯出範圍不一致 → 相機全部
        /// 判定在範圍外、底圖上一個標記都不畫」的根因（見 docs/camera-manager/adr.zh-TW.md
        /// D-017、spec.zh-TW.md §6.4）。
        /// </summary>
        /// <summary>
        /// 2026-09-24 對位改版（取代「推測匯出範圍」）：匯出一張「確定性」的暫存複本視圖——
        /// 樣板拿掉、CropBoxActive=true／CropBoxVisible=false、裁剪形狀還原成矩形、註記裁剪
        /// 開啟且四邊外擴設到 Revit 允許的最小值、會伸出裁剪框的註記類別（格線、樓層、剖面／
        /// 立面／詳圖索引標記、尺寸、標籤、文字、參考平面、範圍框…）用視圖層級類別隱藏。
        /// 這樣匯出的點陣圖範圍只可能是「模型裁剪框」或「模型裁剪框＋最小註記外擴」，兩者用
        /// 圖片長寬比挑較吻合者，並把所有數字寫進 log。
        ///
        /// 交易結構：TransactionGroup 內 Transaction 1 建立並設定暫存視圖 → Commit → 在沒有任何
        /// Transaction 開啟（doc 不可修改）的狀態下 ExportImage → RollBack 整個 group，暫存視圖
        /// 消失、不留復原紀錄。⚠️ 「TransactionGroup 開著、但無 Transaction 時可否 ExportImage」
        /// API 文件沒有明寫（未查證）：若 ExportImage 在 group 內丟例外，改走備援——group 仍
        /// RollBack，接著「建立暫存視圖 Commit → 匯出 → finally 用新交易刪除暫存視圖」。
        /// 暫存視圖建立本身失敗（例如文件唯讀）時，退回 <see cref="ExportBaseMapLegacy"/>。
        /// </summary>
        internal static bool IsExportingBaseMap { get; private set; }

        internal static BaseMapSnapshot ExportBaseMap(Document doc, ElementId planViewId)
        {
            using (CameraPerf perf = CameraPerf.Begin($"底圖匯出（planViewId={planViewId?.GetIdValue()}）"))
            {
                BaseMapSnapshot r = ExportBaseMapCore(doc, planViewId);
                if (r?.ErrorMessage != null) perf.Note("失敗：" + r.ErrorMessage);
                try { if (r != null) r.IsOwnView = CameraKeyPlanService.IsOwnTaggedView(doc?.GetElement(planViewId) as View); } catch { }
                return r;
            }
        }

        private static BaseMapSnapshot ExportBaseMapCore(Document doc, ElementId planViewId)
        {
            View source = doc?.GetElement(planViewId) as View;
            if (source == null || !(source is ViewPlan))
                return ExportBaseMapLegacy(doc, planViewId);

            IsExportingBaseMap = true;
            try
            {
                BaseMapSnapshot viaGroup = null;
                bool groupExportFailed = false;
                try
                {
                    using (TransactionGroup tg = new TransactionGroup(doc, "相機管理：底圖暫存視圖"))
                    {
                        tg.Start();
                        try
                        {
                            ViewPlan temp = CreateBaseMapTempView(doc, (ViewPlan)source, out string setupLog);
                            try
                            {
                                viaGroup = ExportDeterministic(doc, temp, (ViewPlan)source, setupLog + " mode=TransactionGroup");
                            }
                            catch (Exception exExport)
                            {
                                groupExportFailed = true;
                                Logger.Error("相機管理面板：TransactionGroup 內 ExportImage 失敗，改走備援（commit→export→delete）", exExport);
                            }
                        }
                        finally
                        {
                            if (tg.HasStarted() && !tg.HasEnded()) tg.RollBack();
                        }
                    }
                }
                catch (Exception exSetup)
                {
                    Logger.Error("相機管理面板：建立底圖暫存視圖失敗，退回舊版匯出（推測範圍）", exSetup);
                    CameraManagerDiagnostics.RecordCaught("ExportBaseMap.TempView", exSetup);
                    return ExportBaseMapLegacy(doc, planViewId);
                }

                if (!groupExportFailed && viaGroup != null) return viaGroup;

                // 備援：commit → export → 一定刪除
                ElementId tempId = null;
                try
                {
                    using (Transaction t = new Transaction(doc, "相機管理：建立底圖暫存視圖"))
                    {
                        t.Start();
                        ViewPlan temp = CreateBaseMapTempView(doc, (ViewPlan)source, out _, inTransaction: true);
                        TransactionHelper.CommitOrThrow(t);
                        tempId = temp.Id; // 2026-09-26 稽核：提交成功才記，未提交時 finally 不去刪已回滾的元素
                    }
                    ViewPlan committed = doc.GetElement(tempId) as ViewPlan;
                    return ExportDeterministic(doc, committed, (ViewPlan)source, "mode=CommitThenDelete");
                }
                catch (Exception exFallback)
                {
                    Logger.Error("相機管理面板：備援底圖匯出失敗，退回舊版匯出", exFallback);
                    return ExportBaseMapLegacy(doc, planViewId);
                }
                finally
                {
                    if (tempId != null && tempId != ElementId.InvalidElementId)
                    {
                        try
                        {
                            using (Transaction td = new Transaction(doc, "相機管理：刪除底圖暫存視圖"))
                            {
                                td.Start();
                                doc.Delete(tempId);
                                td.Commit();
                            }
                        }
                        catch (Exception exDel)
                        {
                            Logger.Error($"相機管理面板：刪除底圖暫存視圖 {tempId.GetIdValue()} 失敗（請手動刪除名稱以「_CameraMgr_BaseMap」開頭的視圖）", exDel);
                        }
                    }
                }
            }
            finally
            {
                IsExportingBaseMap = false;
            }
        }

        /// <summary>要隱藏的註記類別（會伸出裁剪框）；另外所有名稱以 Tags 結尾的註記類別也一併隱藏。</summary>
        private static readonly BuiltInCategory[] BaseMapHiddenAnnotationCats =
        {
            BuiltInCategory.OST_Grids, BuiltInCategory.OST_Levels,
            BuiltInCategory.OST_Sections, BuiltInCategory.OST_Elev, BuiltInCategory.OST_Callouts,
            BuiltInCategory.OST_Viewers,
            BuiltInCategory.OST_Dimensions, BuiltInCategory.OST_SpotElevations, BuiltInCategory.OST_SpotCoordinates,
            BuiltInCategory.OST_TextNotes, BuiltInCategory.OST_CLines, BuiltInCategory.OST_VolumeOfInterest,
            BuiltInCategory.OST_Matchline
        };

        /// <summary>在已開啟的 Transaction（inTransaction=true）或自己開一個 Transaction 內建立並設定暫存視圖。</summary>
        private static ViewPlan CreateBaseMapTempView(Document doc, ViewPlan source, out string setupLog, bool inTransaction = false)
        {
            Transaction own = null;
            if (!inTransaction)
            {
                own = new Transaction(doc, "相機管理：設定底圖暫存視圖");
                own.Start();
            }
            try
            {
                ElementId dupId = source.Duplicate(ViewDuplicateOption.Duplicate);
                ViewPlan temp = doc.GetElement(dupId) as ViewPlan;
                if (temp == null) throw new Exception("複製平面視圖失敗。");
                try { temp.Name = "_CameraMgr_BaseMap_" + Guid.NewGuid().ToString("N").Substring(0, 8); } catch { }
                try { if (temp.ViewTemplateId != ElementId.InvalidElementId) temp.ViewTemplateId = ElementId.InvalidElementId; } catch { }

                temp.CropBoxActive = true;
                temp.CropBoxVisible = false;
                temp.CropBox = source.CropBox; // Duplicate 已經複製裁剪框，這裡明確再設一次

                double left = double.NaN, right = double.NaN, top = double.NaN, bottom = double.NaN;
                ViewCropRegionShapeManager crsm = temp.GetCropRegionShapeManager();
                if (crsm != null)
                {
                    try { if (crsm.ShapeSet) crsm.RemoveCropRegionShape(); } catch { }
                }
                try
                {
                    Parameter annoCrop = temp.get_Parameter(BuiltInParameter.VIEWER_ANNOTATION_CROP_ACTIVE);
                    if (annoCrop != null && !annoCrop.IsReadOnly) annoCrop.Set(1);
                }
                catch { }
                if (crsm != null && crsm.CanHaveAnnotationCrop)
                {
                    SetMinOffset(v => crsm.LeftAnnotationCropOffset = v);
                    SetMinOffset(v => crsm.RightAnnotationCropOffset = v);
                    SetMinOffset(v => crsm.TopAnnotationCropOffset = v);
                    SetMinOffset(v => crsm.BottomAnnotationCropOffset = v);
                    left = crsm.LeftAnnotationCropOffset; right = crsm.RightAnnotationCropOffset;
                    top = crsm.TopAnnotationCropOffset; bottom = crsm.BottomAnnotationCropOffset;
                }

                int hidden = 0;
                var cats = new List<Category>();
                foreach (BuiltInCategory bic in BaseMapHiddenAnnotationCats)
                {
                    try { Category c = Category.GetCategory(doc, bic); if (c != null) cats.Add(c); } catch { }
                }
                foreach (Category c in doc.Settings.Categories)
                {
                    try
                    {
                        if (c.CategoryType == CategoryType.Annotation && c.Id.GetIdValue() < 0 &&
                            Enum.GetName(typeof(BuiltInCategory), (int)c.Id.GetIdValue())?.EndsWith("Tags", StringComparison.Ordinal) == true)
                            cats.Add(c);
                    }
                    catch { }
                }
                foreach (Category c in cats)
                {
                    try
                    {
                        if (temp.CanCategoryBeHidden(c.Id)) { temp.SetCategoryHidden(c.Id, true); hidden++; }
                    }
                    catch { }
                }

                if (own != null) own.Commit();

                setupLog = $"annoOffsetsFt L={left:F6} R={right:F6} T={top:F6} B={bottom:F6} hiddenCats={hidden}";
                return temp;
            }
            catch
            {
                if (own != null && own.HasStarted() && !own.HasEnded()) own.RollBack();
                throw;
            }
        }

        /// <summary>把註記裁剪外擴設到允許的最小值：先試 0，被拒就用逐步加大的值（1mm…）。
        /// 最終值一律以讀回的屬性為準（Revit 可能自行夾值）。</summary>
        private static void SetMinOffset(Action<double> setter)
        {
            double[] tries = { 0.0, CameraMath.MmToFeet(0.1), CameraMath.MmToFeet(1), CameraMath.MmToFeet(10), CameraMath.MmToFeet(100) };
            foreach (double v in tries)
            {
                try { setter(v); return; } catch { }
            }
        }

        private static BaseMapSnapshot ExportDeterministic(Document doc, ViewPlan temp, ViewPlan source, string setupLog)
        {
            var result = new BaseMapSnapshot { GenLevelId = source.GenLevel?.Id };

            BoundingBoxXYZ cropBox = temp.CropBox;
            Transform tf = cropBox.Transform;
            result.OriginX = tf.Origin.X; result.OriginY = tf.Origin.Y; result.OriginZ = tf.Origin.Z;
            result.BasisXX = tf.BasisX.X; result.BasisXY = tf.BasisX.Y; result.BasisXZ = tf.BasisX.Z;
            result.BasisYX = tf.BasisY.X; result.BasisYY = tf.BasisY.Y; result.BasisYZ = tf.BasisY.Z;
            result.BasisZX = tf.BasisZ.X; result.BasisZY = tf.BasisZ.Y; result.BasisZZ = tf.BasisZ.Z;
            result.ModelCropMinXFeet = cropBox.Min.X; result.ModelCropMinYFeet = cropBox.Min.Y;
            result.ModelCropMaxXFeet = cropBox.Max.X; result.ModelCropMaxYFeet = cropBox.Max.Y;

            double oL = 0, oR = 0, oT = 0, oB = 0;
            try
            {
                ViewCropRegionShapeManager crsm = temp.GetCropRegionShapeManager();
                if (crsm != null && crsm.CanHaveAnnotationCrop)
                {
                    oL = crsm.LeftAnnotationCropOffset; oR = crsm.RightAnnotationCropOffset;
                    oT = crsm.TopAnnotationCropOffset; oB = crsm.BottomAnnotationCropOffset;
                }
            }
            catch { }

            string tempDir = Path.Combine(Path.GetTempPath(), "RevitMCP_CameraManager", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                // 與 capture_view_image（CommandExecutor.ViewCapture.cs ExportViewToImage）相同的選項。
                var options = new ImageExportOptions
                {
                    FilePath = Path.Combine(tempDir, "basemap"),
                    ExportRange = ExportRange.SetOfViews,
                    FitDirection = FitDirectionType.Horizontal,
                    HLRandWFViewsFileType = ImageFileType.PNG,
                    ShadowViewsFileType = ImageFileType.PNG,
                    ImageResolution = ImageResolution.DPI_150,
                    ShouldCreateWebSite = false,
                    ZoomType = ZoomFitType.FitToPage,
                    PixelSize = 1600
                };
                options.SetViewsAndSheets(new List<ElementId> { temp.Id });
                doc.ExportImage(options);

                string exported = Directory.GetFiles(tempDir).OrderByDescending(f => new FileInfo(f).Length).FirstOrDefault();
                if (string.IsNullOrEmpty(exported)) throw new Exception("ExportImage 沒有產生任何檔案。");

                TryReadPngSize(exported, out int pw, out int ph);
                result.ImagePixelWidth = pw;
                result.ImagePixelHeight = ph;

                double cropW = cropBox.Max.X - cropBox.Min.X, cropH = cropBox.Max.Y - cropBox.Min.Y;
                double annoW = cropW + oL + oR, annoH = cropH + oT + oB;
                double imgAspect = ph > 0 ? (double)pw / ph : double.NaN;
                double cropAspect = cropH > 1e-9 ? cropW / cropH : double.NaN;
                double annoAspect = annoH > 1e-9 ? annoW / annoH : double.NaN;

                bool useAnno = !double.IsNaN(imgAspect) && !double.IsNaN(annoAspect) &&
                    Math.Abs(annoAspect - imgAspect) < Math.Abs(cropAspect - imgAspect);
                if (useAnno)
                {
                    result.CropMinXFeet = cropBox.Min.X - oL; result.CropMaxXFeet = cropBox.Max.X + oR;
                    result.CropMinYFeet = cropBox.Min.Y - oB; result.CropMaxYFeet = cropBox.Max.Y + oT;
                    result.ExtentsSource = "crop+minAnnotationOffsets";
                }
                else
                {
                    result.CropMinXFeet = cropBox.Min.X; result.CropMaxXFeet = cropBox.Max.X;
                    result.CropMinYFeet = cropBox.Min.Y; result.CropMaxYFeet = cropBox.Max.Y;
                    result.ExtentsSource = "modelCrop";
                }

                Logger.Info("相機管理面板：底圖對位 " + setupLog +
                    $" | image={pw}x{ph}px aspect={imgAspect:F4}" +
                    $" | cropMm={CameraMath.FeetToMm(cropW):F0}x{CameraMath.FeetToMm(cropH):F0} aspect={cropAspect:F4}" +
                    $" | crop+annoMm={CameraMath.FeetToMm(annoW):F0}x{CameraMath.FeetToMm(annoH):F0} aspect={annoAspect:F4}" +
                    $" | chosen={result.ExtentsSource}" +
                    $" | extentsLocalMm X[{CameraMath.FeetToMm(result.CropMinXFeet):F0},{CameraMath.FeetToMm(result.CropMaxXFeet):F0}] Y[{CameraMath.FeetToMm(result.CropMinYFeet):F0},{CameraMath.FeetToMm(result.CropMaxYFeet):F0}]");

                result.ImageFilePath = exported;
                result.TempDirectory = tempDir;
                return result;
            }
            catch
            {
                CleanupTempDir(tempDir);
                throw;
            }
        }

        /// <summary>讀 PNG IHDR（位元組 16–23，big-endian 寬／高）；失敗回傳 0。</summary>
        private static void TryReadPngSize(string path, out int width, out int height)
        {
            width = height = 0;
            try
            {
                using (FileStream fs = File.OpenRead(path))
                {
                    byte[] buf = new byte[24];
                    if (fs.Read(buf, 0, 24) < 24) return;
                    if (buf[0] != 0x89 || buf[1] != 0x50) return;
                    width = (buf[16] << 24) | (buf[17] << 16) | (buf[18] << 8) | buf[19];
                    height = (buf[20] << 24) | (buf[21] << 16) | (buf[22] << 8) | buf[23];
                }
            }
            catch { width = height = 0; }
        }

        internal static BaseMapSnapshot ExportBaseMapLegacy(Document doc, ElementId planViewId)
        {
            var result = new BaseMapSnapshot();
            string tempDir = null;

            try
            {
                View view = doc.GetElement(planViewId) as View;
                if (view == null)
                {
                    result.ErrorMessage = "找不到指定的平面視圖，請按重新整理。";
                    return result;
                }
                if (!view.CropBoxActive)
                {
                    result.Warning = "此平面視圖未啟用 CropBox（不應該被選到，請回報）。";
                }

                result.GenLevelId = (view as ViewPlan)?.GenLevel?.Id;

                BoundingBoxXYZ cropBox = view.CropBox;
                // 攤平成純 double（見 BaseMapSnapshot 註解），不把 Revit Transform 帶出 API context。
                Transform cropTf = cropBox.Transform;
                result.OriginX = cropTf.Origin.X; result.OriginY = cropTf.Origin.Y; result.OriginZ = cropTf.Origin.Z;
                result.BasisXX = cropTf.BasisX.X; result.BasisXY = cropTf.BasisX.Y; result.BasisXZ = cropTf.BasisX.Z;
                result.BasisYX = cropTf.BasisY.X; result.BasisYY = cropTf.BasisY.Y; result.BasisYZ = cropTf.BasisY.Z;
                result.BasisZX = cropTf.BasisZ.X; result.BasisZY = cropTf.BasisZ.Y; result.BasisZZ = cropTf.BasisZ.Z;
                double minX = cropBox.Min.X, minY = cropBox.Min.Y, maxX = cropBox.Max.X, maxY = cropBox.Max.Y;

                // 2026-09-24 實測回報：底圖疊圖位置系統性偏移，根因是平面視圖若啟用了「註記裁剪」
                // (Annotation Crop)，ExportImage 的 FitToPage 匯出範圍是「模型裁剪框 + 註記裁剪
                // 外擴量」（圖名泡、標籤等會伸出模型裁剪框外），但先前的像素↔模型換算只用了
                // view.CropBox（純模型裁剪框），造成兩者不一致、標記全部偏移。這裡用
                // ViewCropRegionShapeManager 的四個 AnnotationCropOffset 把裁剪框往外擴，讓
                // 換算用的範圍與匯出圖片實際涵蓋的範圍一致。
                try
                {
                    ViewCropRegionShapeManager crsm = view.GetCropRegionShapeManager();
                    if (crsm != null && crsm.CanHaveAnnotationCrop)
                    {
                        // ViewCropRegionShapeManager 沒有「是否啟用註記裁剪」的旗標屬性（只有
                        // CanHaveAnnotationCrop 這個「這個視圖類型是否支援」的能力旗標）；「目前
                        // 有沒有打開」是 View 上一個叫「Annotation Crop」的內建參數，但用顯示名稱
                        // LookupParameter 在非英文語系 Revit 會找不到。這裡雙重保險：先試
                        // LookupParameter，找不到就退回「四個外擴量是否有非零值」當代理判斷——
                        // 停用時外擴量通常維持 0，不會誤觸發外擴；找到的話以參數值為準。
                        bool annotationCropActive;
                        Parameter annoCropParam = view.LookupParameter("Annotation Crop");
                        if (annoCropParam != null && annoCropParam.StorageType == StorageType.Integer)
                        {
                            annotationCropActive = annoCropParam.AsInteger() != 0;
                        }
                        else
                        {
                            annotationCropActive = crsm.TopAnnotationCropOffset > 1e-9 || crsm.BottomAnnotationCropOffset > 1e-9
                                || crsm.LeftAnnotationCropOffset > 1e-9 || crsm.RightAnnotationCropOffset > 1e-9;
                        }

                        if (annotationCropActive)
                        {
                            minX -= crsm.LeftAnnotationCropOffset;
                            maxX += crsm.RightAnnotationCropOffset;
                            minY -= crsm.BottomAnnotationCropOffset;
                            maxY += crsm.TopAnnotationCropOffset;
                        }
                    }
                }
                catch
                {
                    // 某些視圖類型/版本可能不支援 ViewCropRegionShapeManager 或其中某個屬性；
                    // 忽略、退回純模型 CropBox，不讓底圖匯出因此中斷。
                }

                result.CropMinXFeet = minX;
                result.CropMinYFeet = minY;
                result.CropMaxXFeet = maxX;
                result.CropMaxYFeet = maxY;

                tempDir = Path.Combine(Path.GetTempPath(), "RevitMCP_CameraManager", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tempDir);

                var options = new ImageExportOptions
                {
                    FilePath = Path.Combine(tempDir, "basemap"),
                    ExportRange = ExportRange.SetOfViews,
                    FitDirection = FitDirectionType.Horizontal,
                    HLRandWFViewsFileType = ImageFileType.PNG,
                    ShadowViewsFileType = ImageFileType.PNG,
                    ImageResolution = ImageResolution.DPI_150,
                    ShouldCreateWebSite = false,
                    ZoomType = ZoomFitType.FitToPage,
                    PixelSize = 1600
                };
                options.SetViewsAndSheets(new List<ElementId> { view.Id });

                doc.ExportImage(options);

                string exported = Directory.GetFiles(tempDir)
                    .OrderByDescending(f => new FileInfo(f).Length)
                    .FirstOrDefault();

                if (string.IsNullOrEmpty(exported))
                {
                    result.ErrorMessage = "底圖匯出失敗：ExportImage 沒有產生任何檔案。";
                    CleanupTempDir(tempDir);
                    return result;
                }

                result.ImageFilePath = exported;
                result.TempDirectory = tempDir;
                return result;
            }
            catch (Exception ex)
            {
                result.ErrorMessage = "底圖匯出失敗：" + ex.Message;
                CleanupTempDir(tempDir);
                return result;
            }
        }

        internal static void CleanupTempDir(string directory)
        {
            try
            {
                if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
                    Directory.Delete(directory, true);
            }
            catch
            {
                // 暫存清理失敗不影響主要功能
            }
        }

        // ══════════════════════════════════════════════════════════
        // 4.5 人視高預設值套用 / 4.3 細節編輯
        // ══════════════════════════════════════════════════════════

        internal static (int okCount, int skippedCount, string error) ApplyHeightToCameras(
            Document doc, IList<ElementId> viewIds, double heightCm)
        {
            if (doc == null) return (0, 0, "目前沒有開啟中的文件。");

            int okCount = 0, skippedCount = 0;
            try
            {
                List<Level> levels = CameraMath.GetStoreyLevelsSorted(doc);
                using (Transaction t = TransactionHelper.Begin(doc, "套用相機人視高"))
                {
                    t.Start();
                    foreach (ElementId id in viewIds)
                    {
                        View3D v = doc.GetElement(id) as View3D;
                        if (v == null || v.IsTemplate || !v.IsPerspective) { skippedCount++; continue; }

                        // 2026-09-25：與清單同一個樓層規則（室內＝房間的 Level）。
                        Level levelBelow = CameraMath.ComputeGeometry(v, levels).LevelBelow;
                        if (levelBelow == null) { skippedCount++; continue; }

                        CameraKeyPlanService.EnsureOriginalVersionInTransaction(v, "人視高套用"); // 2026-09-26：第一次修改前自動存 A案
                        v.SetOrientation(CameraMath.BuildOrientationForHeightAboveLevel(v, levelBelow, heightCm));
                        okCount++;
                    }
                    TransactionHelper.CommitOrThrow(t);
                }
                return (okCount, skippedCount, null);
            }
            catch (Exception ex)
            {
                return (okCount, skippedCount, "套用高度失敗：" + ex.Message);
            }
        }

        /// <summary>清單編輯（面板）與 set_camera_pose（MCP）共用：用這一列的既有值組一份完整的
        /// <see cref="CameraEditRequest"/>，呼叫端只覆寫要改的欄位（遠裁剪預設 null＝不變）。</summary>
        internal static CameraEditRequest BuildEditRequestFromRow(CameraRow row)
        {
            return new CameraEditRequest
            {
                ViewId = row.ViewId,
                EyeXFeet = row.EyeXFeet,
                EyeYFeet = row.EyeYFeet,
                LevelId = row.LevelId,
                HeightAboveLevelCm = row.HeightAboveLevelCm ?? 0,
                HorizontalAngleDeg = row.HorizontalAngleDeg,
                PitchDeg = row.PitchDeg,
                FarClipCm = null
            };
        }

        /// <summary>4.3 細節編輯：一次寫入位置／樓層／高度／角度／遠裁剪，一個交易、一個中文復原名稱。</summary>
        internal static string ApplyDetailEdit(Document doc, CameraEditRequest req)
        {
            if (doc == null) return "目前沒有開啟中的文件。";

            try
            {
                View3D v = doc.GetElement(req.ViewId) as View3D;
                if (v == null || v.IsTemplate || !v.IsPerspective)
                    return "找不到這個相機視圖（可能已被刪除），請按重新整理。";

                Level level = doc.GetElement(req.LevelId) as Level;
                if (level == null) return "找不到指定的樓層。";

                using (Transaction t = TransactionHelper.Begin(doc, "編輯相機"))
                {
                    t.Start();

                    // 2026-09-26 第四輪：相機第一次被修改前（沒有任何視角版本）自動存原始視角「A案」，與這次編輯同一個交易（一起復原）。
                    CameraKeyPlanService.EnsureOriginalVersionInTransaction(v, "編輯相機");

                    // 先套位置／樓層／高度（眼睛 X/Y/Z），再套角度（角度重建時眼睛位置不變，
                    // 所以順序對最終結果沒有影響，但先位置後角度比較符合「使用者由上往下填欄位」的直覺）。
                    ViewOrientation3D afterPosition = CameraMath.BuildOrientationForPosition(
                        v, req.EyeXFeet, req.EyeYFeet, level, req.HeightAboveLevelCm);
                    v.SetOrientation(afterPosition);

                    ViewOrientation3D afterAngles = CameraMath.BuildOrientationForAngles(
                        v, req.HorizontalAngleDeg, req.PitchDeg);
                    v.SetOrientation(afterAngles);

                    if (req.FarClipCm.HasValue)
                    {
                        Parameter farClipParam = CameraMath.FindFarClipOffsetParameter(v);
                        if (farClipParam != null && !farClipParam.IsReadOnly)
                        {
                            double feet = CameraMath.MmToFeet(req.FarClipCm.Value * 10.0);
                            farClipParam.Set(feet);
                        }
                    }

                    TransactionHelper.CommitOrThrow(t);
                }
                return null;
            }
            catch (Exception ex)
            {
                return "編輯相機失敗：" + ex.Message;
            }
        }

        // ══════════════════════════════════════════════════════════
        // 2026-09-26 第六輪：編輯快速路徑（使用者：「改離樓板高度後，相機視圖要很快改變——這是核心功能」）
        // ══════════════════════════════════════════════════════════

        /// <summary>快速路徑交易名稱（復原清單看到的名稱，與舊版一致）。</summary>
        internal const string FastEditTransactionName = "編輯相機";

        // 面板快速路徑已就地更新清單列的相機（Id 數值 → 到期時間）。Application 的 DocumentChanged 看到「只有這些相機、
        // 交易名＝編輯相機」的變更時，不再排 live sync 整份快照重讀（清單已是最新）；標記更新照常排入去抖。
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<long, DateTime> _inPlaceEdits =
            new System.Collections.Concurrent.ConcurrentDictionary<long, DateTime>();

        /// <summary>DocumentChanged 端呼叫：這些被修改的相機是否都剛由面板快速路徑就地更新過（呼叫即消耗記號）。</summary>
        internal static bool ConsumeInPlaceEdits(ICollection<ElementId> modifiedCameraIds)
        {
            try
            {
                if (modifiedCameraIds == null || modifiedCameraIds.Count == 0) return false;
                DateTime now = DateTime.UtcNow;
                bool all = true;
                foreach (ElementId id in modifiedCameraIds)
                {
                    long k = id.GetIdValue();
                    if (!_inPlaceEdits.TryRemove(k, out DateTime until) || until < now) all = false;
                }
                return all;
            }
            catch { return false; }
        }

        /// <summary>2026-09-26 稽核：編輯數值檢查（面板與 set_camera_pose 共用）。舊版不檢查：NaN／無限大直接寫進相機、
        /// 俯仰角 ±90° 讓前方向垂直、BuildLevelUp 退化、遠裁剪 ≤ 0 寫入參數。回傳 null＝OK。</summary>
        internal static string ValidatePoseEdit(CameraPoseEdit edit)
        {
            if (edit == null) return "沒有要套用的編輯。";
            bool Bad(double? d) => d.HasValue && (double.IsNaN(d.Value) || double.IsInfinity(d.Value));
            if (Bad(edit.HeightAboveLevelCm) || Bad(edit.YawDeg) || Bad(edit.PitchDeg) || Bad(edit.EyeXFeet) || Bad(edit.EyeYFeet) || Bad(edit.FarClipCm))
                return "數值無效（NaN 或無限大）。";
            if (edit.PitchDeg.HasValue && Math.Abs(edit.PitchDeg.Value) >= MaxPitchDeg)
                return $"俯仰角必須介於 −{MaxPitchDeg:0.#}° 與 +{MaxPitchDeg:0.#}° 之間（±90° 時相機朝正上／正下，方向無法定義）。";
            if (edit.FarClipCm.HasValue && edit.FarClipCm.Value <= 0) return "遠裁剪距離必須大於 0 cm。";
            if (edit.HeightAboveLevelCm.HasValue && Math.Abs(edit.HeightAboveLevelCm.Value) > MaxAbsHeightCm)
                return $"離樓板高度超出合理範圍（±{MaxAbsHeightCm:0} cm）。";
            return null;
        }

        internal const double MaxPitchDeg = 89.5;
        internal const double MaxAbsHeightCm = 100000.0; // 1 km

        /// <summary>
        /// 編輯快速路徑：一個小交易（自動存「A案」JSON＋一次 SetOrientation＋遠裁剪），交易後立刻重繪透視圖，
        /// 再從相機實際姿態讀回新值（清單就地更新／MCP 回傳用）。不找房間、不讀其他相機、不碰 Key Plan／總覽／標記——
        /// 那些由呼叫端排入 800 ms 去抖的後續更新（CameraKeyPlanService.UpdateCamerasAfterChange）。
        /// 沒給的欄位一律沿用相機「目前實際」的值（不是清單列的值），所以清單列即使是舊資料也不會把別的欄位改回去。
        /// markInPlace＝面板呼叫（清單列會就地更新，DocumentChanged 不必再排整份快照）。
        /// source＝log 用（「面板」「MCP」）；queueWaitMs＝面板從按 Enter 到佇列開始執行的等待時間（MCP 為 0）。
        /// 須在 API context、交易外。
        /// </summary>
        internal static CameraPoseEditResult ApplyPoseEditFast(UIApplication uiApp, Document doc, CameraPoseEdit edit,
            string source, bool markInPlace, long queueWaitMs = 0)
        {
            var res = new CameraPoseEditResult { QueueWaitMs = queueWaitMs };
            var sw = System.Diagnostics.Stopwatch.StartNew();
            string what = edit?.What() ?? "相機";
            try
            {
                if (doc == null) { res.Error = "目前沒有開啟中的文件。"; return res; }
                string invalid = ValidatePoseEdit(edit);
                if (invalid != null) { res.Error = invalid; return res; }
                View3D v = edit?.ViewId != null ? doc.GetElement(edit.ViewId) as View3D : null;
                if (v == null || v.IsTemplate || !v.IsPerspective) { res.Error = "找不到這個相機視圖（可能已被刪除），請按重新整理。"; return res; }
                res.CameraName = v.Name;
                Level level = edit.LevelId != null ? doc.GetElement(edit.LevelId) as Level : null;
                if (edit.HeightAboveLevelCm.HasValue && level == null) { res.Error = "這台相機沒有可用的樓層基準，無法改離樓板高度。"; return res; }

                ViewOrientation3D o = v.GetOrientation();
                XYZ eye = o.EyePosition, forward = o.ForwardDirection, up = o.UpDirection;
                XYZ newEye = new XYZ(edit.EyeXFeet ?? eye.X, edit.EyeYFeet ?? eye.Y,
                    edit.HeightAboveLevelCm.HasValue ? level.ProjectElevation + CameraMath.MmToFeet(edit.HeightAboveLevelCm.Value * 10.0) : eye.Z);
                XYZ newForward = forward, newUp = up;
                if (edit.YawDeg.HasValue || edit.PitchDeg.HasValue)
                {
                    double curYaw = Math.Atan2(forward.X, forward.Y) * 180.0 / Math.PI;
                    double curPitch = Math.Asin(Math.Max(-1.0, Math.Min(1.0, forward.Z))) * 180.0 / Math.PI;
                    newForward = CameraMath.DirectionFromAngles(edit.YawDeg ?? curYaw, edit.PitchDeg ?? curPitch);
                    newUp = CameraMath.BuildLevelUp(newForward);
                }
                bool poseChanged = newEye.DistanceTo(eye) > CameraMath.MmToFeet(0.1) || newForward.DistanceTo(forward) > 1e-6 || newUp.DistanceTo(up) > 1e-6;

                Parameter farParam = null;
                double? farFeet = null;
                if (edit.FarClipCm.HasValue)
                {
                    farParam = CameraMath.FindFarClipOffsetParameter(v);
                    if (farParam == null || farParam.IsReadOnly) { res.Error = "這台相機的遠裁剪參數不可寫入。"; return res; }
                    double f = CameraMath.MmToFeet(edit.FarClipCm.Value * 10.0);
                    if (Math.Abs(farParam.AsDouble() - f) > CameraMath.MmToFeet(0.1)) farFeet = f;
                }
                res.Changed = poseChanged || farFeet.HasValue;

                if (res.Changed)
                {
                    using (Transaction t = TransactionHelper.Begin(doc, FastEditTransactionName))
                    {
                        t.Start();
                        try
                        {
                            // 第一次修改前自動存「A案」（只寫 ES JSON，同一交易、一起復原）。
                            res.SavedOriginalVersion = CameraKeyPlanService.EnsureOriginalVersionInTransaction(v, what + "編輯（" + source + "）");
                            if (poseChanged) v.SetOrientation(new ViewOrientation3D(newEye, newUp, newForward));
                            if (farFeet.HasValue) farParam.Set(farFeet.Value);
                            if (markInPlace) _inPlaceEdits[v.Id.GetIdValue()] = DateTime.UtcNow.AddSeconds(5);
                            TransactionHelper.CommitOrThrow(t);
                        }
                        catch
                        {
                            try { if (t.HasStarted() && !t.HasEnded()) t.RollBack(); } catch { }
                            try { _inPlaceEdits.TryRemove(v.Id.GetIdValue(), out _); } catch { }
                            throw;
                        }
                    }
                }
                res.TransactionMs = sw.ElapsedMilliseconds;

                if (res.Changed)
                {
                    long t0 = sw.ElapsedMilliseconds;
                    res.RefreshHow = RefreshCameraViewIfOpen(uiApp, v.Id);
                    res.RefreshMs = sw.ElapsedMilliseconds - t0;
                }
                else res.RefreshHow = "數值與相機目前相同，未開交易";

                // 讀回實際值（便宜：只讀這一台的 Orientation／遠裁剪參數）。
                ViewOrientation3D a = v.GetOrientation();
                res.EyeXFeet = a.EyePosition.X; res.EyeYFeet = a.EyePosition.Y; res.EyeZFeet = a.EyePosition.Z;
                res.ForwardX = a.ForwardDirection.X; res.ForwardY = a.ForwardDirection.Y; res.ForwardZ = a.ForwardDirection.Z;
                res.YawDeg = CameraMath.ComputeHorizontalAngleDeg(a.ForwardDirection);
                res.PitchDeg = Math.Round(Math.Asin(Math.Max(-1.0, Math.Min(1.0, a.ForwardDirection.Z))) * 180.0 / Math.PI, 2);
                if (level != null)
                    res.HeightAboveLevelCm = Math.Round((CameraMath.FeetToMm(a.EyePosition.Z) - CameraMath.FeetToMm(level.ProjectElevation)) / 10.0, 1);
                try
                {
                    Parameter fp = farParam ?? CameraMath.FindFarClipOffsetParameter(v);
                    if (fp != null) res.FarClipCm = Math.Round(CameraMath.FeetToMm(fp.AsDouble()) / 10.0, 1);
                }
                catch { }
            }
            catch (Exception ex)
            {
                Logger.Error($"相機編輯快速路徑失敗（{what}，{source}）", ex);
                res.Error = "編輯相機失敗：" + ex.Message;
            }
            finally
            {
                res.TotalMs = sw.ElapsedMilliseconds;
                try
                {
                    Logger.Info($"⏱ {what}編輯 → 視圖更新 total={res.TotalMs + res.QueueWaitMs}ms " +
                                $"[排隊={res.QueueWaitMs}ms, 交易={res.TransactionMs}ms, 重繪={res.RefreshMs}ms（{res.RefreshHow ?? "-"}）]" +
                                $"（{source}：「{res.CameraName ?? "?"}」{(res.SavedOriginalVersion ? "；先自動存 A案" : "")}{(res.Error != null ? "；失敗：" + res.Error : "")}）");
                }
                catch { }
            }
            return res;
        }

        // ══════════════════════════════════════════════════════════
        // Tab1 清單內編輯：重新命名（名稱欄可直接改）
        // ══════════════════════════════════════════════════════════

        /// <summary>清單「名稱」欄內編輯：重複名稱直接拒絕並回錯誤訊息，不寫入。</summary>
        internal static string RenameCameraView(Document doc, ElementId viewId, string newName)
        {
            if (doc == null) return "目前沒有開啟中的文件。";
            if (string.IsNullOrWhiteSpace(newName)) return "名稱不可為空白。";
            newName = newName.Trim();
            // 使用者輸入：含 Revit 不允許的字元就明確拒絕（不偷偷替換，避免名稱跟輸入不同）
            if (CameraKeyPlanService.HasInvalidRevitNameChars(newName))
                return "名稱不可包含下列字元：{ } [ ] : \\ | ; < > ? ` ~";

            try
            {
                View3D v = doc.GetElement(viewId) as View3D;
                if (v == null || v.IsTemplate || !v.IsPerspective)
                    return "找不到這個相機視圖（可能已被刪除），請按重新整理。";

                if (string.Equals(v.Name, newName, StringComparison.Ordinal)) return null;

                bool duplicate = new FilteredElementCollector(doc)
                    .OfClass(typeof(View))
                    .Cast<View>()
                    .Any(o => o.Id != viewId && string.Equals(o.Name, newName, StringComparison.OrdinalIgnoreCase));
                if (duplicate) return "已存在同名視圖「" + newName + "」，請換一個名稱。";

                using (Transaction t = TransactionHelper.Begin(doc, "重新命名相機視圖"))
                {
                    t.Start();
                    v.Name = newName;
                    TransactionHelper.CommitOrThrow(t);
                }
                return null;
            }
            catch (Exception ex)
            {
                return "重新命名失敗：" + ex.Message;
            }
        }

        // ══════════════════════════════════════════════════════════
        // 4.6 複製相機到其他樓層
        // ══════════════════════════════════════════════════════════

        internal static (int created, string error) CopyCameraToLevels(
            Document doc, ElementId sourceViewId, IList<ElementId> targetLevelIds)
            => CopyCameraToLevels(doc, sourceViewId, targetLevelIds, null);

        /// <summary>2026-09-25：多一個 <paramref name="createdCameras"/>（可為 null）回傳新相機 (Id, 名稱)，
        /// 供清單「樓層」欄改樓層＝複製後選取新相機。</summary>
        internal static (int created, string error) CopyCameraToLevels(
            Document doc, ElementId sourceViewId, IList<ElementId> targetLevelIds,
            List<(ElementId id, string name)> createdCameras)
        {
            if (doc == null) return (0, "目前沒有開啟中的文件。");

            try
            {
                View3D source = doc.GetElement(sourceViewId) as View3D;
                if (source == null || source.IsTemplate || !source.IsPerspective)
                    return (0, "找不到來源相機視圖（可能已被刪除），請按重新整理。");

                List<Level> levels = CameraMath.GetStoreyLevelsSorted(doc);
                ViewOrientation3D srcOrientation = source.GetOrientation();
                double eyeZFeet = srcOrientation.EyePosition.Z;
                Level srcLevelBelow = CameraMath.ComputeGeometry(source, levels).LevelBelow; // 2026-09-25：室內＝房間的 Level
                double heightAboveLevelCm = srcLevelBelow != null
                    ? CameraMath.FeetToMm(eyeZFeet - srcLevelBelow.ProjectElevation) / 10.0
                    : 0.0;

                HashSet<string> existingNames = new HashSet<string>(
                    new FilteredElementCollector(doc)
                        .OfClass(typeof(View3D))
                        .Cast<View3D>()
                        .Select(v => v.Name),
                    StringComparer.OrdinalIgnoreCase);

                int created = 0;
                using (Transaction t = TransactionHelper.Begin(doc, "複製相機到其他樓層"))
                {
                    t.Start();
                    foreach (ElementId targetLevelId in targetLevelIds)
                    {
                        Level targetLevel = doc.GetElement(targetLevelId) as Level;
                        if (targetLevel == null) continue;

                        ElementId copyId = source.Duplicate(ViewDuplicateOption.Duplicate);
                        View3D copy = doc.GetElement(copyId) as View3D;
                        if (copy == null) continue;

                        string baseName = CameraKeyPlanService.SanitizeRevitName(source.Name + " - " + targetLevel.Name);
                        string finalName = baseName;
                        int suffix = 2;
                        while (existingNames.Contains(finalName))
                        {
                            finalName = baseName + " (" + suffix + ")";
                            suffix++;
                        }
                        copy.Name = finalName;
                        existingNames.Add(finalName);

                        copy.SetOrientation(CameraMath.BuildOrientationForPosition(
                            copy, srcOrientation.EyePosition.X, srcOrientation.EyePosition.Y,
                            targetLevel, heightAboveLevelCm));

                        created++;
                        createdCameras?.Add((copyId, finalName));
                    }
                    TransactionHelper.CommitOrThrow(t);
                }
                return (created, null);
            }
            catch (Exception ex)
            {
                return (0, "複製相機失敗：" + ex.Message);
            }
        }

        // ══════════════════════════════════════════════════════════
        // 4.7 匯出 Excel
        // ══════════════════════════════════════════════════════════

        private static void SetCellOrBlank(ClosedXML.Excel.IXLCell cell, double? v)
        {
            if (v.HasValue && !double.IsNaN(v.Value) && !double.IsInfinity(v.Value)) cell.Value = v.Value;
            else cell.Value = "";
        }

        internal static string ExportExcel(IList<CameraRow> rows, string filePath)
        {
            try
            {
                using (var workbook = new XLWorkbook())
                {
                    var ws = workbook.Worksheets.Add("相機清單");
                    string[] headers =
                    {
                        "名稱", "樓層", "離樓板高度cm", "平面X(cm)", "平面Y(cm)",
                        "水平角(°)", "俯仰角(°)", "水平視野角估算(°)", "遠裁剪(cm)", "Eye Elevation 原始值"
                    };
                    for (int i = 0; i < headers.Length; i++)
                        ws.Cell(1, i + 1).Value = headers[i];
                    ws.Row(1).Style.Font.Bold = true;

                    int r = 2;
                    foreach (CameraRow row in rows)
                    {
                        ws.Cell(r, 1).Value = row.Name;
                        ws.Cell(r, 2).Value = row.LevelName;
                        // 2026-09-26 稽核：ClosedXML 0.100+ 寫入 NaN 會丟 ArgumentException → 缺值（室外／無樓層／無遠裁剪）改寫空白。
                        SetCellOrBlank(ws.Cell(r, 3), row.HeightAboveLevelCm);
                        SetCellOrBlank(ws.Cell(r, 4), row.EyeXCm);
                        SetCellOrBlank(ws.Cell(r, 5), row.EyeYCm);
                        SetCellOrBlank(ws.Cell(r, 6), row.HorizontalAngleDeg);
                        SetCellOrBlank(ws.Cell(r, 7), row.PitchDeg);
                        SetCellOrBlank(ws.Cell(r, 8), row.FovEstimateDeg);
                        SetCellOrBlank(ws.Cell(r, 9), row.FarClipCm);
                        ws.Cell(r, 10).Value = row.EyeElevationDisplay;
                        r++;
                    }
                    ws.Columns().AdjustToContents();
                    workbook.SaveAs(filePath);
                }
                return null;
            }
            catch (Exception ex)
            {
                return "匯出 Excel 失敗：" + ex.Message;
            }
        }

        // ══════════════════════════════════════════════════════════
        // 跳轉視圖
        // ══════════════════════════════════════════════════════════

        /// <summary>
        /// 2026-09-26 第四輪（使用者：清單改數字後透視圖有時沒跟著動）：相機被程式修改後，若它是作用中視圖 → RefreshActiveView；
        /// 若只是開著（非作用中）→ UpdateAllOpenViews。Revit API 沒有單一 UIView 的重繪方法。須在 API context、交易外；失敗只記 log。
        /// 2026-09-26 第七輪（使用者：面板改高度後 log 顯示 RefreshActiveView 重繪=0~1ms，但透視圖沒變；MCP 改同一台時重繪=102ms 有變）：
        /// 面板情境下 RefreshActiveView 形同空操作（焦點在面板／並排的 Key Plan 視窗），所以不再只靠它。改成：
        /// 在 GetOpenUIViews 找相機本身的 UIView → UpdateAllOpenViews ＋ 對「那個」UIView 做 zoom-corner 原地輕推
        /// （GetZoomCorners → ZoomAndCenterRectangle 同一組角點，會強迫 Revit 重繪該視窗）；若它剛好是作用中視圖再加 RefreshActiveView。
        /// 回傳字串誠實標出實際用了哪些方法、各幾 ms、重繪的是哪個視圖（作用中視圖不是相機時也寫出作用中是誰）。
        /// </summary>
        internal static string RefreshCameraViewIfOpen(UIApplication uiApp, ElementId cameraId)
        {
            try
            {
                UIDocument uiDoc = uiApp?.ActiveUIDocument;
                if (uiDoc == null || cameraId == null) return "沒有作用中文件";
                Document doc = uiDoc.Document;
                string camName = (doc?.GetElement(cameraId) as View)?.Name ?? "?";
                IList<UIView> open = uiDoc.GetOpenUIViews();
                UIView camUi = open?.FirstOrDefault(u => u.ViewId == cameraId);
                View active = uiDoc.ActiveView;
                bool isActive = active?.Id == cameraId;
                if (camUi == null)
                    return $"相機視圖「{camName}」未開啟，未重繪（作用中視圖＝「{active?.Name ?? "-"}」）";

                var parts = new List<string>();
                var sw = System.Diagnostics.Stopwatch.StartNew();
                try { uiDoc.UpdateAllOpenViews(); parts.Add($"UpdateAllOpenViews {sw.ElapsedMilliseconds}ms"); }
                catch (Exception ex) { parts.Add("UpdateAllOpenViews 失敗：" + ex.Message); }

                long t1 = sw.ElapsedMilliseconds;
                try
                {
                    IList<XYZ> corners = camUi.GetZoomCorners();
                    if (corners != null && corners.Count >= 2)
                    {
                        camUi.ZoomAndCenterRectangle(corners[0], corners[1]);
                        parts.Add($"ZoomNudge「{camName}」{sw.ElapsedMilliseconds - t1}ms");
                    }
                    else parts.Add("ZoomNudge 無角點");
                }
                catch (Exception ex) { parts.Add("ZoomNudge 失敗：" + ex.Message); }

                if (isActive)
                {
                    long t2 = sw.ElapsedMilliseconds;
                    try { uiDoc.RefreshActiveView(); parts.Add($"RefreshActiveView {sw.ElapsedMilliseconds - t2}ms"); }
                    catch (Exception ex) { parts.Add("RefreshActiveView 失敗：" + ex.Message); }
                }
                else parts.Add($"相機非作用中（作用中＝「{active?.Name ?? "-"}」）");

                return string.Join("＋", parts);
            }
            catch (Exception ex)
            {
                Logger.Error("相機管理：重繪相機視圖失敗（略過）", ex);
                return "重繪失敗：" + ex.Message;
            }
        }

        /// <summary>
        /// 2026-09-26 第四輪（使用者：「僅打開透視圖」要關掉其他視圖）：切換到相機透視圖，再關閉其他所有視圖視窗（同一鍵 4 視圖的
        /// CloseOtherViews）。須在 ExternalEvent（交易外）。回傳錯誤或 null。
        /// </summary>
        internal static string OpenPerspectiveOnly(UIApplication uiApp, ElementId viewId)
        {
            string err = JumpToView(uiApp, viewId);
            if (err != null) return err;
            try { CloseOtherViews(uiApp, new List<ElementId> { viewId }); }
            catch (Exception ex) { Logger.Error("相機管理：僅打開透視圖——關閉其他視圖失敗（透視圖已開啟）", ex); }
            return null;
        }

        /// <summary>2026-09-26 第四輪（一鍵出圖紙）：把任一視圖（圖紙）設為作用中並關閉其他所有視圖。回傳錯誤或 null。</summary>
        internal static string ShowOnlyView(UIApplication uiApp, ElementId viewId)
        {
            try
            {
                UIDocument uiDoc = uiApp?.ActiveUIDocument;
                View v = uiDoc?.Document?.GetElement(viewId) as View;
                if (v == null) return "找不到視圖";
                uiDoc.ActiveView = v;
                CloseOtherViews(uiApp, new List<ElementId> { viewId });
                return null;
            }
            catch (Exception ex)
            {
                Logger.Error("相機管理：開啟視圖並關閉其他視圖失敗", ex);
                return ex.Message;
            }
        }

        internal static string JumpToView(UIApplication uiApp, ElementId viewId)
        {
            Document doc = uiApp?.ActiveUIDocument?.Document;
            View3D v = doc?.GetElement(viewId) as View3D;
            if (v == null) return "找不到這個相機視圖（可能已被刪除），請按重新整理。";
            try
            {
                uiApp.ActiveUIDocument.ActiveView = v;
                return null;
            }
            catch (Exception ex)
            {
                return "跳轉失敗：" + ex.Message;
            }
        }

        // ══════════════════════════════════════════════════════════
        // 4.9 A：並排檢視 —— 找相機所在樓層一張「既有」平面視圖
        // ══════════════════════════════════════════════════════════

        /// <summary>
        /// 找相機 level-below 對應的既有平面視圖：優先「已經開啟中的」，其次優先已啟用 CropBox，
        /// 再其次依名稱排序取第一個。絕不新建視圖（4.9 A 只用既有視圖）。
        /// </summary>
        internal static ElementId FindPlanViewForLevel(UIApplication uiApp, Document doc, ElementId levelId)
        {
            if (doc == null || levelId == null) return null;

            List<ViewPlan> candidates = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewPlan))
                .Cast<ViewPlan>()
                .Where(v => !v.IsTemplate && v.GenLevel != null && v.GenLevel.Id == levelId)
                .OrderBy(v => v.Name)
                .ToList();
            if (candidates.Count == 0) return null;

            HashSet<ElementId> openIds = new HashSet<ElementId>(
                uiApp.ActiveUIDocument?.GetOpenUIViews()?.Select(uv => uv.ViewId) ?? Enumerable.Empty<ElementId>());

            ViewPlan chosen = candidates.FirstOrDefault(v => openIds.Contains(v.Id) && v.CropBoxActive)
                ?? candidates.FirstOrDefault(v => openIds.Contains(v.Id))
                ?? candidates.FirstOrDefault(v => v.CropBoxActive)
                ?? candidates[0];
            return chosen.Id;
        }

        /// <summary>目前所有開啟中的 UIView（並排檢視前拍照，供「還原」比對用）。</summary>
        internal static List<(ElementId viewId, UIView uiView)> SnapshotOpenViews(UIApplication uiApp)
        {
            var list = new List<(ElementId, UIView)>();
            IList<UIView> open = uiApp.ActiveUIDocument?.GetOpenUIViews();
            if (open == null) return list;
            foreach (UIView uv in open) list.Add((uv.ViewId, uv));
            return list;
        }

        // ══════════════════════════════════════════════════════════
        // 一鍵 4 視圖：開啟並並排（2026-09-25 由 CameraManagerPanelControl 搬來，面板與 MCP 共用；純 Revit API）
        // ══════════════════════════════════════════════════════════

        /// <summary>一鍵 4 視圖／並排用：關閉作用中文件裡「不是 keep 名單」的所有視圖視窗。
        /// 必須先把 keep 裡的某張設為作用視圖（Revit 不允許關閉作用中／最後一張視圖）。每張獨立 try/catch。</summary>
        internal static int CloseOtherViews(UIApplication uiApp, ICollection<ElementId> keep)
        {
            int closed = 0;
            UIDocument uidoc = uiApp?.ActiveUIDocument;
            if (uidoc == null) return 0;
            IList<UIView> open;
            try { open = uidoc.GetOpenUIViews(); }
            catch (Exception ex) { Logger.Error("相機管理面板：GetOpenUIViews 失敗，略過關閉其他視圖", ex); return 0; }
            foreach (UIView uv in open.ToList())
            {
                try
                {
                    if (keep.Contains(uv.ViewId)) continue;
                    uv.Close();
                    closed++;
                }
                catch (Exception ex)
                {
                    Logger.Error($"相機管理面板：關閉視圖 {uv.ViewId?.GetIdValue()} 失敗（略過）", ex);
                }
            }
            Logger.Info($"相機管理面板：已關閉 {closed} 張其他視圖。");
            return closed;
        }

        /// <summary>一鍵 4 視圖（含重設剖面位置）共用：依序啟動透視／平面／剖面／等角、關閉其他視圖、
        /// PostCommand 並排、排定並排後 ZoomToFit。須在 ExternalEvent（交易外）呼叫。回傳錯誤訊息或 null。</summary>
        internal static string OpenAndTileFourViews(UIApplication uiApp, Document doc, ElementId cameraViewId, CameraKeyPlanService.KeyPlanResult result)
        {
            using (CameraPerf.Begin("一鍵 4 視圖：開啟／關閉其他／並排"))
                return OpenAndTileFourViewsCore(uiApp, doc, cameraViewId, result);
        }

        private static string OpenAndTileFourViewsCore(UIApplication uiApp, Document doc, ElementId cameraViewId, CameraKeyPlanService.KeyPlanResult result)
        {
            try
            {
                View perspective = doc.GetElement(cameraViewId) as View;
                View plan = doc.GetElement(result.PlanViewId) as View;
                View section = doc.GetElement(result.SectionViewId) as View;
                View iso = doc.GetElement(result.IsoViewId) as View;

                // 2026-09-26 第四輪（使用者：重複點同一台相機每次都關閉／重開／重新並排，面板還越來越窄）：
                // 目前開著的視圖剛好就是這 4 張 → 不關、不重開、不並排，只對 4 張 ZoomToFit。
                try
                {
                    var want = new HashSet<long>(new[] { perspective, plan, section, iso }.Where(v => v != null).Select(v => (long)v.Id.GetIdValue()));
                    IList<UIView> openNow = uiApp.ActiveUIDocument.GetOpenUIViews();
                    var have = new HashSet<long>((openNow ?? new List<UIView>()).Select(u => (long)u.ViewId.GetIdValue()));
                    if (want.Count == 4 && have.SetEquals(want))
                    {
                        // 透視圖 ZoomToFit 走 CameraViewZoomService 的防護式處理（比對方位、有變就還原）。
                        int zoomed = CameraViewZoomService.ZoomAllNow(uiApp, new[] { perspective, plan, section, iso }.Where(v => v != null).Select(v => v.Id).ToList());
                        Logger.Info($"相機管理面板：這 4 張視圖已開啟並排 → 不關閉／不重新並排，只 ZoomToFit {zoomed} 張。");
                        return null;
                    }
                }
                catch (Exception exSame) { Logger.Error("相機管理面板：比對已開啟視圖失敗（照常並排）", exSame); }

                // 規格：依序啟動 4 張視圖，最後一步才 PostCommand TileViews
                // （PostCommand 排入佇列，等這次 Execute() 整段結束、控制權還給 Revit 訊息迴圈後才真的執行）。
                if (perspective != null) uiApp.ActiveUIDocument.ActiveView = perspective;
                if (plan != null) uiApp.ActiveUIDocument.ActiveView = plan;
                if (section != null) uiApp.ActiveUIDocument.ActiveView = section;
                if (iso != null) uiApp.ActiveUIDocument.ActiveView = iso;
                // 2026-09-24：先關閉其他所有視圖視窗（作用中已是 4 張之一），只留這 4 張再並排。
                var keep = new List<ElementId>();
                foreach (View v in new[] { perspective, plan, section, iso }) if (v != null) keep.Add(v.Id);
                CloseOtherViews(uiApp, keep);
                if (iso != null)
                {
                    // 2026-09-24：等角視圖啟用 Section Box 後補一次 ZoomToFit（縮放基準＝目前裁切範圍）。
                    try
                    {
                        UIView isoUiView = uiApp.ActiveUIDocument.GetOpenUIViews()?.FirstOrDefault(v => v.ViewId == iso.Id);
                        isoUiView?.ZoomToFit();
                    }
                    catch { /* 縮放失敗不應該讓整個流程中斷，忽略即可 */ }
                }

                uiApp.PostCommand(RevitCommandId.LookupPostableCommandId(PostableCommand.TileViews));
                // 並排會在本次 ExternalEvent 結束後才改變視窗大小 → 在之後的 Idling 才 ZoomToFit。
                CameraViewZoomService.ScheduleZoomToFit(uiApp, keep, expectTile: true);
                return null;
            }
            catch (Exception ex)
            {
                return "已建立/更新 Key Plan 視圖，但開啟/並排時發生錯誤：" + ex.Message;
            }
        }
    }
}
