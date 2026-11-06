using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using RevitMCP.Configuration;

// Revit 2025+ ElementId: int → long
#if REVIT2025_OR_GREATER
using IdType = System.Int64;
#else
using IdType = System.Int32;
#endif

namespace RevitMCP.Core
{
    /// <summary>
    /// 2026-09-25 Wave 2：每層總覽平面 <c>Key Plan-總覽-&lt;樓層名&gt;</c>（spec §10.10）。
    ///
    /// 規則：
    ///   1. 一個建築樓層一張（只為「有相機的樓層」建立）；以 ES（CameraKeyPlanLink：CameraUniqueId 欄位存 Level UniqueId、
    ///      Role＝Overview）連結樓層，重跑更新同一張、不重複；找不到 ES 才退回精確名稱比對。
    ///   2. 來源＝該層的底圖挑選器（<see cref="BaseMapPlanChooser.Choose"/>，與 ③ 位置圖同一個挑法；排除本工具自建的 Key Plan 視圖）
    ///      選出的平面，<c>Duplicate(ViewDuplicateOption.Duplicate)</c>（不含詳圖）；挑不到 → 該層既有平面 → 新建樓層平面。
    ///   3. 裁剪：該層全部相機皆室內＝各相機扇形（室內扇形半徑）外接框聯集 +3 m；有室外相機＝底圖裁剪框（建築範圍）∪
    ///      室內扇形 +3 m ∪ 眼睛 +3 m。只改範圍、不旋轉（沿用來源平面座標系）。
    ///   4. 表現法同 Key Plan 平面（§10.7）；文字類別不整類隱藏，改逐元素隱藏「不是我們的」文字，保留相機名稱。
    ///   5. 每台相機畫同一套 B 型標記（眼睛圓圈、視線、扇形邊線＋填滿、眼睛楔形）＋名稱 TextNote（文字類型「相機標記名稱字」，背景透明）。
    ///      標記 Role＝MarkerPlan／MarkerName（cid＝相機 UniqueId）；每次更新先刪除這張視圖內所有相機標記再重畫（離開本層或被刪除的相機不殘留）。
    /// 單一交易「更新樓層總覽」；呼叫端須在 API context、交易外。
    /// </summary>
    internal static partial class CameraKeyPlanService
    {
        internal const string OverviewRole = "Overview";
        internal const string OverviewTransactionName = "更新樓層總覽";
        private const string MarkerNameRole = "MarkerName";
        private const double OverviewFanMarginMm = 3000.0;
        private const string MarkerNameTextTypeName = "相機標記名稱字";
        private const double MarkerNameTextSizeMm = 2.5;

        internal sealed class OverviewResult
        {
            public ElementId LevelId;
            public string LevelName;
            public ElementId ViewId;
            public string ViewName;
            public bool Created;
            public int CameraCount;
            public List<string> CameraNames = new List<string>();
            public string BasePlanName;
            public string BasePlanReason;
            public string ExtentsMode;
            /// <summary>裁剪範圍（模型座標外接框，feet）。</summary>
            public double MinX, MinY, MaxX, MaxY;
            public int? Scale;
            /// <summary>2026-09-25：計算出的預期範圍（模型座標 feet）；MinX…MaxY 改為讀回視圖裁剪框的實際值。</summary>
            public double IntendedMinX, IntendedMinY, IntendedMaxX, IntendedMaxY;
            public List<string> Warnings = new List<string>();
            public string Error;
        }

        /// <summary>這張視圖是不是本工具建立的「樓層總覽」。</summary>
        internal static bool IsOverviewView(View v) =>
            v != null && TryReadLink(v, out _, out string role) && role == OverviewRole;

        /// <summary>找某樓層的總覽平面（ES 優先，再退回精確名稱）；沒有回 null。</summary>
        internal static ViewPlan FindOverviewView(Document doc, Level level)
        {
            if (doc == null || level == null) return null;
            if (Schema.Lookup(SchemaGuid) != null && FindLinkedView(doc, level.UniqueId, OverviewRole) is ViewPlan vp) return vp;
            string name = OverviewNameFor(level);
            return new FilteredElementCollector(doc).OfClass(typeof(ViewPlan)).Cast<ViewPlan>()
                .FirstOrDefault(v => !v.IsTemplate && v.Name == name);
        }

        private static string OverviewNameFor(Level level) => SanitizeRevitName("Key Plan-總覽-" + (level?.Name ?? "?")); // 2026-09-26 稽核：樓層名可能含非法字元

        /// <summary>
        /// 建立或更新多個樓層的總覽平面。levelIds＝null → 所有「有相機」的建築樓層。
        /// 回傳每層一筆結果（沒有相機的樓層回 Error，不建立視圖）。
        /// </summary>
        internal static List<OverviewResult> CreateOrUpdateOverviews(Document doc, ICollection<ElementId> levelIds)
        {
            var results = new List<OverviewResult>();
            if (doc == null) return results;

            List<Level> levels = CameraMath.GetStoreyLevelsSorted(doc);
            // 1) 交易外：每台相機的參數（與一鍵 4 視圖同一個 ComputeCameraParams），依下方樓層分組。
            var byLevel = new Dictionary<long, List<(View3D cam, CameraParams cp)>>();
            foreach (View3D cam in new FilteredElementCollector(doc).OfClass(typeof(View3D)).Cast<View3D>()
                         .Where(v => !v.IsTemplate && v.IsPerspective && !IsSheetPerspectiveCopy(v)).OrderBy(v => v.Name, StringComparer.Ordinal))
            {
                CameraParams cp;
                try { cp = ComputeCameraParams(cam, levels); }
                catch (Exception ex) { Logger.Error($"樓層總覽：相機「{cam.Name}」參數計算失敗（略過）", ex); continue; }
                if (cp.Geom?.LevelBelow == null) continue;
                long lv = cp.Geom.LevelBelow.Id.GetIdValue();
                if (!byLevel.TryGetValue(lv, out var list)) byLevel[lv] = list = new List<(View3D, CameraParams)>();
                list.Add((cam, cp));
            }

            // 2026-09-25：室內相機的樓層＝房間的 Level（可能不是建築樓層），all 時也要列入。
            List<ElementId> targets = levelIds != null && levelIds.Count > 0
                ? levelIds.Where(i => i != null).ToList()
                : byLevel.Keys
                    .Select(k => doc.GetElement(new ElementId((IdType)k)) as Level)
                    .Where(l => l != null)
                    .OrderBy(l => l.ProjectElevation)
                    .Select(l => l.Id).ToList();
            if (targets.Count == 0) return results;

            // 2) 交易外：底圖候選（已裁剪平面，排除本工具自建的 Key Plan／總覽視圖）＋樓層清單。
            List<PlanViewOption> planOptions = BuildOverviewPlanOptions(doc, levels, out List<LevelOption> levelOptions);

            var plans = new List<(Level level, List<(View3D cam, CameraParams cp)> cams, PlanViewOption basePlan, string why, OverviewResult res)>();
            foreach (ElementId lid in targets)
            {
                var res = new OverviewResult { LevelId = lid };
                results.Add(res);
                Level level = doc.GetElement(lid) as Level;
                if (level == null) { res.Error = $"元素 {lid.GetIdValue()} 不是樓層。"; continue; }
                res.LevelName = level.Name;
                if (!byLevel.TryGetValue(lid.GetIdValue(), out var cams) || cams.Count == 0)
                {
                    res.Error = $"樓層「{level.Name}」沒有相機（室內相機以房間所在樓層、室外以下方建築樓層判定），不建立總覽。";
                    continue;
                }
                var eyes = cams.Select(c => new[] { c.cp.Geom.Eye.X, c.cp.Geom.Eye.Y, c.cp.Geom.Eye.Z }).ToList();
                SourcePlanHint hint = SourcePlanHint.MostCommon(cams.Select(c => BuildSourceHintRow(c.cam)).Where(r => r != null));
                PlanViewOption chosen = BaseMapPlanChooser.Choose(planOptions, lid, eyes, levelOptions, out string why, out int step, hint);
                plans.Add((level, cams, chosen, why, res));
            }
            if (plans.Count == 0) return results;

            bool faded = IsKeyPlanFadedStyle();
            using (Transaction t = TransactionHelper.Begin(doc, OverviewTransactionName))
            {
                t.Start();
                try
                {
                    foreach (var p in plans)
                    {
                        SubTransaction st = new SubTransaction(doc);
                        try
                        {
                            st.Start();
                            BuildOneOverview(doc, p.level, p.cams, p.basePlan, p.why, faded, p.res);
                            st.Commit();
                        }
                        catch (Exception ex)
                        {
                            try { if (st.HasStarted() && !st.HasEnded()) st.RollBack(); } catch { }
                            p.res.Error = "建立總覽失敗：" + ex.Message;
                            p.res.ViewId = null;
                            Logger.Error($"樓層總覽：「{p.level.Name}」失敗（該層已回滾）", ex);
                            CameraManagerDiagnostics.RecordCaught("CreateOrUpdateOverview", ex);
                        }
                        finally { st.Dispose(); }
                    }
                    TransactionHelper.CommitOrThrow(t);
                    BumpOwnViewsEpoch();
                }
                catch (Exception ex)
                {
                    try { if (t.HasStarted() && !t.HasEnded()) t.RollBack(); } catch { }
                    Logger.Error("樓層總覽：交易失敗，已回滾", ex);
                    foreach (var p in plans) { p.res.Error = p.res.Error ?? ("建立總覽失敗（已復原）：" + ex.Message); p.res.ViewId = null; }
                }
            }
            return results;
        }

        /// <summary>只為樓層清單讀 CameraRow 的放置平面欄位（BaseMapPlanChooser 步驟 0 提示），不做室內外判定等重工作。</summary>
        private static CameraRow BuildSourceHintRow(View3D cam)
        {
            try
            {
                ViewPlan src = CameraSourcePlanStore.TryGetSourcePlan(cam);
                if (src == null) return null;
                ElementId pid = null;
                bool cropped = false;
                try { cropped = src.CropBoxActive; ElementId p = src.GetPrimaryViewId(); if (p != ElementId.InvalidElementId) pid = p; } catch { }
                return new CameraRow { ViewId = cam.Id, SourcePlanId = src.Id, SourcePlanName = src.Name, SourcePlanCropped = cropped, SourcePlanPrimaryId = pid };
            }
            catch { return null; }
        }

        /// <summary>與 CameraManagerService.ReadSnapshot 同一組欄位的已裁剪平面清單，但排除本工具自建（有 ES 連結）的視圖。</summary>
        private static List<PlanViewOption> BuildOverviewPlanOptions(Document doc, List<Level> levels, out List<LevelOption> levelOptions)
        {
            levelOptions = levels.Select(l => new LevelOption { LevelId = l.Id, Name = l.Name, ProjectElevationFeet = l.ProjectElevation }).ToList();
            var storeyIds = new HashSet<long>(levels.Select(l => (long)l.Id.GetIdValue()));
            var list = new List<PlanViewOption>();
            foreach (ViewPlan pv in new FilteredElementCollector(doc).OfClass(typeof(ViewPlan)).Cast<ViewPlan>()
                         .Where(v => !v.IsTemplate && (v.ViewType == ViewType.FloorPlan || v.ViewType == ViewType.CeilingPlan || v.ViewType == ViewType.AreaPlan))
                         .OrderBy(v => v.Name, StringComparer.Ordinal))
            {
                try
                {
                    if (!pv.CropBoxActive) continue;
                    if (IsToolGeneratedView(pv)) continue; // 本工具自建的 Key Plan 平面／總覽（ES 或「Key Plan-」名稱）不當底圖
                    var opt = new PlanViewOption
                    {
                        ViewId = pv.Id,
                        GenLevelId = pv.GenLevel?.Id,
                        PlanName = pv.Name,
                        LevelName = pv.GenLevel?.Name,
                        DisplayName = pv.Name + "（" + pv.ViewType + "）",
                        IsFloorPlan = pv.ViewType == ViewType.FloorPlan,
                        GenLevelElevationFeet = pv.GenLevel != null ? (double?)pv.GenLevel.ProjectElevation : null,
                        GenLevelIsStorey = pv.GenLevel != null && storeyIds.Contains(pv.GenLevel.Id.GetIdValue())
                    };
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
                    list.Add(opt);
                }
                catch (Exception ex) { Logger.Error($"樓層總覽：讀取平面「{pv.Name}」失敗（不列入底圖候選）", ex); }
            }
            return list;
        }

        private static void BuildOneOverview(Document doc, Level level, List<(View3D cam, CameraParams cp)> cams,
            PlanViewOption basePlan, string why, bool faded, OverviewResult res)
        {
            string name = OverviewNameFor(level);
            ViewPlan view = FindOverviewView(doc, level);
            ViewPlan source = basePlan?.ViewId != null ? doc.GetElement(basePlan.ViewId) as ViewPlan : null;
            res.BasePlanName = source?.Name;
            res.BasePlanReason = why;

            // 2026-09-26 第五輪：總覽以樓層快取，但 GenLevel 不可改——既有總覽的 GenLevel ≠ 本次底圖（挑選器結果）的樓層
            // （沒有挑選結果＝本樓層）→ 退役舊總覽（沒上圖紙＝刪除；上了圖紙＝改名「-舊樓層」並解除連結），下方重建。
            if (view != null)
            {
                ElementId expectedLevelId = source?.GenLevel?.Id ?? level.Id;
                if (IsPlanLevelMismatch(view, expectedLevelId))
                {
                    string oldLevel = view.GenLevel?.Name ?? "?";
                    string newLevel = (doc.GetElement(expectedLevelId) as Level)?.Name ?? "?";
                    string retired = RetireMismatchedKeyPlanView(doc, view, null, null);
                    Logger.Info($"樓層總覽：「{level.Name}」總覽平面樓層不符（舊 {oldLevel} → 新 {newLevel}）→ 重建；{retired}");
                    view = null;
                }
            }

            if (view == null)
            {
                ViewPlan dupFrom = source ?? FindSourcePlanForDuplication(doc, null, level.Id);
                if (dupFrom != null)
                {
                    try { view = doc.GetElement(dupFrom.Duplicate(ViewDuplicateOption.Duplicate)) as ViewPlan; }
                    catch (Exception ex) { Logger.Error($"樓層總覽：複製「{dupFrom.Name}」失敗，改新建樓層平面", ex); view = null; }
                    if (view != null && source == null) res.BasePlanName = dupFrom.Name + "（底圖挑選器無結果，改用該層既有平面）";
                }
                if (view == null)
                {
                    ViewFamilyType vft = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>()
                        .FirstOrDefault(v => v.ViewFamily == ViewFamily.FloorPlan);
                    if (vft == null) throw new Exception("找不到任何樓層平面視圖類型 (ViewFamily.FloorPlan)。");
                    view = ViewPlan.Create(doc, vft.Id, level.Id);
                    res.BasePlanName = "(新建樓層平面)";
                }
                res.Created = true;
            }
            if (view.Name != name) view.Name = MakeUniqueName(doc, name, view.Id);
            TagLink(view, level.UniqueId, OverviewRole);
            res.ViewId = view.Id;
            res.ViewName = view.Name;

            try { if (view.ViewTemplateId != ElementId.InvalidElementId) view.ViewTemplateId = ElementId.InvalidElementId; } catch { }
            // 2026-09-25：底圖常鎖在 Scope Box（CropBox 設定會被忽略）→ 先解除、專案北、圖紙標題改成自己的。
            NormalizeDuplicatedPlan(view, "樓層總覽 " + level.Name);

            // 裁剪範圍（模型 XY）
            double m = CameraMath.MmToFeet(OverviewFanMarginMm);
            double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
            void Grow(double ax0, double ay0, double ax1, double ay1)
            {
                x0 = Math.Min(x0, ax0); y0 = Math.Min(y0, ay0); x1 = Math.Max(x1, ax1); y1 = Math.Max(y1, ay1);
            }
            bool anyOutdoor = cams.Any(c => c.cp.IsOutdoor);
            var fanRadiusLog = new List<string>();
            foreach (var c in cams)
            {
                XYZ eye = c.cp.Geom.Eye;
                Grow(eye.X - m, eye.Y - m, eye.X + m, eye.Y + m);
                if (c.cp.IsOutdoor) { fanRadiusLog.Add($"{c.cam.Name}=室外(不撐大)"); continue; } // 室外扇形（可達 60 m）不撐大總覽，改以建築範圍＋眼睛表示
                // 2026-09-26 第三輪：裁剪與畫扇形用同一個半徑函式（OverviewFanRadiusFeet）。
                double r = OverviewFanRadiusFeet(c.cp);
                fanRadiusLog.Add($"{c.cam.Name}=R{CameraMath.FeetToMm(r):F0}");
                ComputeSampledFanBox(eye, c.cp.ForwardHoriz, c.cp.LeftRad, c.cp.RightRad, r, m,
                    out double fx0, out double fy0, out double fx1, out double fy1);
                Grow(fx0, fy0, fx1, fy1);
            }
            if (anyOutdoor)
            {
                if (basePlan != null && basePlan.HasCrop)
                {
                    foreach (var (lx, ly) in new[] { (basePlan.CropMinX, basePlan.CropMinY), (basePlan.CropMaxX, basePlan.CropMinY),
                                                     (basePlan.CropMinX, basePlan.CropMaxY), (basePlan.CropMaxX, basePlan.CropMaxY) })
                    {
                        double wx = basePlan.CropOriginX + lx * basePlan.CropBasisXX + ly * basePlan.CropBasisYX;
                        double wy = basePlan.CropOriginY + lx * basePlan.CropBasisXY + ly * basePlan.CropBasisYY;
                        Grow(wx, wy, wx, wy);
                    }
                    res.ExtentsMode = "建築範圍（底圖裁剪框）∪ 室內扇形 +3 m ∪ 眼睛 +3 m（有室外相機）";
                }
                else res.ExtentsMode = "室內扇形 +3 m ∪ 眼睛 +3 m（有室外相機、但底圖沒有裁剪框可當建築範圍）";
            }
            else res.ExtentsMode = "室內扇形聯集 +3 m";
            res.ExtentsMode += "；扇形半徑 " + string.Join("、", fanRadiusLog);

            try { view.GetCropRegionShapeManager()?.RemoveCropRegionShape(); } catch { /* 沒有非矩形裁剪時會丟例外，忽略 */ }
            view.CropBoxActive = true;
            view.CropBoxVisible = true;
            double z = level.ProjectElevation;
            void ApplyCrop()
            {
                res.IntendedMinX = x0; res.IntendedMinY = y0; res.IntendedMaxX = x1; res.IntendedMaxY = y1;
                BoundingBoxXYZ box = view.CropBox;
                Transform inv = box.Transform.Inverse;
                double lx0 = double.MaxValue, ly0 = double.MaxValue, lx1 = double.MinValue, ly1 = double.MinValue;
                foreach (XYZ p in new[] { new XYZ(x0, y0, z), new XYZ(x1, y0, z), new XYZ(x0, y1, z), new XYZ(x1, y1, z) })
                {
                    XYZ l = inv.OfPoint(p);
                    lx0 = Math.Min(lx0, l.X); lx1 = Math.Max(lx1, l.X); ly0 = Math.Min(ly0, l.Y); ly1 = Math.Max(ly1, l.Y);
                }
                box.Min = new XYZ(lx0, ly0, box.Min.Z);
                box.Max = new XYZ(lx1, ly1, box.Max.Z);
                ResetCropWarnings();
                SetCropAndVerify(view, box, "樓層總覽");
                res.Warnings.AddRange(TakeCropWarnings());
            }
            ApplyCrop();
            EnsureSectionsVisible(view);

            // 表現法（同 Key Plan 平面）；總覽保留文字類別，改逐元素隱藏他人文字。
            DrawMarkerSafely(doc, "ApplyKeyPlanStyleOverview", () =>
            {
                ApplyKeyPlanStyle(view, faded);
                if (faded) HideCategoriesByName(view, new[] { "OST_TextNotes" }, false, false, hide: false);
            });

            // 先清掉這張視圖內所有相機標記（相機可能已刪除或移到別層），再逐台重畫。
            DrawMarkerSafely(doc, "HideForeignViewersOverview", () => HideForeignViewers(doc, view, null, "樓層總覽"));
            int removed = DeleteMarkersInView(doc, view, null, "MarkerPlan", MarkerNameRole);
            double markerZ = view.GenLevel?.ProjectElevation ?? z;
            foreach (var c in cams)
            {
                string cid = c.cam.UniqueId;
                string camName = c.cam.Name;
                DrawMarkerSafely(doc, "DrawOverviewMarker", () => DrawOverviewCameraMarker(doc, view, c.cp, cid, camName, markerZ));
                res.CameraNames.Add(camName);
            }
            res.CameraCount = cams.Count;

            // 2026-09-26 第三輪（MCP 自測：總覽裁剪切到扇形）：畫完標記後讀回「室內相機」標記元素（B 扇形／邊線／名稱）
            // 在本視圖的外接框；超出裁剪（含 3 m 餘裕）就外擴並記 log——裁剪永遠包得住實際畫出的扇形。
            try
            {
                doc.Regenerate();
                var indoorCids = new HashSet<string>(cams.Where(c => !c.cp.IsOutdoor).Select(c => c.cam.UniqueId), StringComparer.Ordinal);
                var roles = new HashSet<string>(new[] { "MarkerPlan", MarkerNameRole }, StringComparer.Ordinal);
                double bx0 = double.MaxValue, by0 = double.MaxValue, bx1 = double.MinValue, by1 = double.MinValue;
                int nMarkers = 0;
                if (indoorCids.Count > 0)
                {
                    // OwnedByView（不經視圖可見性／裁剪過濾）：被裁掉一部分的標記也要讀到
                    foreach (Element e in new FilteredElementCollector(doc).OwnedByView(view.Id).WhereElementIsNotElementType())
                    {
                        if (!TryReadLink(e, out string ecid, out string erole) || !roles.Contains(erole) || !indoorCids.Contains(ecid ?? "")) continue;
                        BoundingBoxXYZ eb = null;
                        try { eb = e.get_BoundingBox(view); } catch { eb = null; }
                        if (eb == null) continue;
                        foreach (XYZ w in WorldCorners(eb))
                        {
                            bx0 = Math.Min(bx0, w.X); by0 = Math.Min(by0, w.Y); bx1 = Math.Max(bx1, w.X); by1 = Math.Max(by1, w.Y);
                        }
                        nMarkers++;
                    }
                }
                if (nMarkers > 0)
                {
                    double tol = CameraMath.MmToFeet(1.0);
                    bool grow = bx0 - m < x0 - tol || by0 - m < y0 - tol || bx1 + m > x1 + tol || by1 + m > y1 + tol;
                    string rb = $"標記讀回外接框 X[{CameraMath.FeetToMm(bx0):F0},{CameraMath.FeetToMm(bx1):F0}] Y[{CameraMath.FeetToMm(by0):F0},{CameraMath.FeetToMm(by1):F0}]mm（{nMarkers} 個）";
                    if (grow)
                    {
                        Grow(bx0 - m, by0 - m, bx1 + m, by1 + m);
                        ApplyCrop();
                        res.ExtentsMode += "；" + rb + " 超出裁剪 → 已外擴（+3 m）";
                        res.Warnings.Add("總覽：實際畫出的相機標記超出原裁剪，已外擴包含（" + rb + "）");
                        Logger.Info("樓層總覽：" + rb + " 超出原裁剪，已外擴");
                    }
                    else res.ExtentsMode += "；" + rb + " 皆在裁剪內";
                }
            }
            catch (Exception ex) { Logger.Error("樓層總覽：標記讀回外擴失敗（保留原裁剪）", ex); }

            // 回報值一律讀回視圖（不回報預期值）。
            if (TryGetCropWorldXY(view, out double rx0, out double ry0, out double rx1, out double ry1))
            { res.MinX = rx0; res.MinY = ry0; res.MaxX = rx1; res.MaxY = ry1; }
            else { res.MinX = x0; res.MinY = y0; res.MaxX = x1; res.MaxY = y1; res.Warnings.Add("讀不到總覽裁剪框，範圍改報預期值。"); }
            if (faded) DrawMarkerSafely(doc, "HideForeignTextOverview", () => HideForeignTextNotes(view));
            try { res.Scale = view.Scale; } catch { }
            Logger.Info($"樓層總覽：「{view.Name}」(id={view.Id.GetIdValue()}) {(res.Created ? "新建" : "更新")}；底圖＝{res.BasePlanName ?? "(無)"}（{why}）；" +
                        $"相機 {cams.Count} 台；範圍 預期 {CameraMath.FeetToMm(x1 - x0):F0}×{CameraMath.FeetToMm(y1 - y0):F0}mm／實際 {CameraMath.FeetToMm(res.MaxX - res.MinX):F0}×{CameraMath.FeetToMm(res.MaxY - res.MinY):F0}mm（{res.ExtentsMode}）；刪除舊標記 {removed} 個；比例 1:{res.Scale}");
        }

        /// <summary>2026-09-26 第三輪：總覽扇形半徑——裁剪（ComputeSampledFanBox）與畫扇形（DrawPlanMarker）共用這一個函式，
        /// 兩邊不會各算各的。目前＝相機參數的扇形半徑（與 Key Plan 平面／A 扇形同源）。</summary>
        private static double OverviewFanRadiusFeet(CameraParams cp) => cp?.FanRadiusFeet ?? 0.0;

        /// <summary>總覽上一台相機的標記：B 型平面標記（不論 ⋯ 相機標記設定，總覽一律畫 B）＋名稱 TextNote。須在交易內。</summary>
        private static void DrawOverviewCameraMarker(Document doc, ViewPlan view, CameraParams cp, string cid, string camName, double markerZ)
        {
            DrawPlanMarker(doc, view, cp.Geom, cp.ForwardHoriz, cp.LeftRad, cp.RightRad, OverviewFanRadiusFeet(cp), cid, markerZ, forceB: true);
            DeleteMarkersInView(doc, view, cid, MarkerNameRole);
            ElementId textType = GetOrCreateMarkerNameTextType(doc);
            if (textType == ElementId.InvalidElementId) return;
            int scale = view.Scale > 0 ? view.Scale : 100;
            // 名稱放在眼睛「後方」（避開扇形），距離＝紙上 3 mm。
            XYZ back = cp.ForwardHoriz.Negate();
            XYZ p = new XYZ(cp.Geom.Eye.X, cp.Geom.Eye.Y, markerZ) + back * CameraMath.MmToFeet(3.0 * scale);
            TextNote tn = TextNote.Create(doc, view.Id, p, camName ?? "?", textType);
            TagLink(tn, cid, MarkerNameRole);
        }

        /// <summary>刪除視圖內指定角色（任一）的相機標記；cid＝null＝不分相機。回傳刪除數。須在交易內。</summary>
        private static int DeleteMarkersInView(Document doc, View view, string cid, params string[] roles)
        {
            if (Schema.Lookup(SchemaGuid) == null) return 0;
            var set = new HashSet<string>(roles ?? new string[0], StringComparer.Ordinal);
            // 2026-09-26 第四輪：只掃「這張視圖擁有的、帶 ES 的」元素（OwnedByView＋ExtensibleStorageFilter），
            // 不再用「視圖中所有可見元素」collector（大型模型每張總覽要數秒）。
            List<ElementId> ids = new FilteredElementCollector(doc).OwnedByView(view.Id).WherePasses(new ExtensibleStorageFilter(SchemaGuid))
                .Where(e => TryReadLink(e, out string c, out string r) && set.Contains(r) && (cid == null || c == cid))
                .Select(e => e.Id).ToList();
            if (ids.Count > 0) doc.Delete(ids);
            return ids.Count;
        }

        /// <summary>文字類型「相機標記名稱字」：複製第一個文字類型，2.5 mm、背景透明、黑；只在第一次建立時設定。</summary>
        private static ElementId GetOrCreateMarkerNameTextType(Document doc)
        {
            List<TextNoteType> all = new FilteredElementCollector(doc).OfClass(typeof(TextNoteType)).Cast<TextNoteType>().ToList();
            TextNoteType named = all.FirstOrDefault(t => t.Name == MarkerNameTextTypeName);
            if (named != null) return named.Id;
            TextNoteType src = all.OrderBy(t => t.Name, StringComparer.Ordinal).FirstOrDefault();
            if (src == null) return ElementId.InvalidElementId;
            try
            {
                TextNoteType t = src.Duplicate(MarkerNameTextTypeName) as TextNoteType;
                if (t == null) return src.Id;
                try { t.get_Parameter(BuiltInParameter.TEXT_SIZE)?.Set(CameraMath.MmToFeet(MarkerNameTextSizeMm)); } catch (Exception ex) { Logger.Error("樓層總覽：名稱字大小設定失敗", ex); }
                try { t.get_Parameter(BuiltInParameter.TEXT_BACKGROUND)?.Set(1); } catch (Exception ex) { Logger.Error("樓層總覽：名稱字背景透明設定失敗", ex); }
                try { t.get_Parameter(BuiltInParameter.LINE_COLOR)?.Set(0); } catch (Exception ex) { Logger.Error("樓層總覽：名稱字顏色設定失敗", ex); }
                Logger.Info($"樓層總覽：建立文字類型「{MarkerNameTextTypeName}」（複製自「{src.Name}」，{MarkerNameTextSizeMm}mm、背景透明、黑）");
                return t.Id;
            }
            catch (Exception ex)
            {
                Logger.Error($"樓層總覽：建立文字類型「{MarkerNameTextTypeName}」失敗，改用「{src.Name}」", ex);
                return src.Id;
            }
        }

        /// <summary>
        /// 「更新相機標記」（去抖）用：這些相機在所有總覽上的標記重畫——先從每張總覽刪除這台相機的標記，
        /// 再畫到「目前下方樓層」的總覽（該層沒有總覽就不建）。不動總覽裁剪框。須在交易內。回傳重畫的總覽標記數。
        /// </summary>
        private static int RedrawCamerasInOverviews(Document doc, IList<(View3D cam, CameraParams cp)> cams,
            IDictionary<string, long?> previousStorey = null)
        {
            if (cams == null || cams.Count == 0) return 0;
            List<ViewPlan> overviews = new FilteredElementCollector(doc).OfClass(typeof(ViewPlan)).WherePasses(new ExtensibleStorageFilter(SchemaGuid)).Cast<ViewPlan>()
                .Where(v => !v.IsTemplate && IsOverviewView(v)).ToList();
            if (overviews.Count == 0) return 0;
            // 樓層 UniqueId → 總覽
            var byLevel = new Dictionary<string, ViewPlan>(StringComparer.Ordinal);
            foreach (ViewPlan v in overviews)
                if (TryReadLink(v, out string luid, out _) && !string.IsNullOrEmpty(luid) && !byLevel.ContainsKey(luid)) byLevel[luid] = v;
            int n = 0;
            foreach (var c in cams)
            {
                string cid = c.cam.UniqueId;
                Level lb = c.cp.Geom?.LevelBelow;
                // 2026-09-26 第四輪（D-041）：知道上次畫在哪一層 → 只清「舊樓層＋新樓層」兩張總覽上這台相機的標記；不知道才全部清。
                List<ViewPlan> clear = overviews;
                if (previousStorey != null && previousStorey.TryGetValue(cid, out long? prevId) && prevId.HasValue)
                {
                    clear = new List<ViewPlan>();
                    Level prevLevel = null;
                    try { prevLevel = doc.GetElement(new ElementId((IdType)prevId.Value)) as Level; } catch { prevLevel = null; }
                    if (prevLevel != null && byLevel.TryGetValue(prevLevel.UniqueId, out ViewPlan pv)) clear.Add(pv);
                    if (lb != null && byLevel.TryGetValue(lb.UniqueId, out ViewPlan nv) && !clear.Contains(nv)) clear.Add(nv);
                }
                foreach (ViewPlan ov in clear) DeleteMarkersInView(doc, ov, cid, "MarkerPlan", MarkerNameRole);
                if (lb == null) continue;
                if (!byLevel.TryGetValue(lb.UniqueId, out ViewPlan target)) continue;
                double markerZ = target.GenLevel?.ProjectElevation ?? lb.ProjectElevation;
                string camName = c.cam.Name;
                DrawMarkerSafely(doc, "UpdateOverviewMarker", () => DrawOverviewCameraMarker(doc, target, c.cp, cid, camName, markerZ));
                n++;
            }
            return n;
        }

        /// <summary>2026-09-26 第四輪：上次自動更新總覽時，該樓層相機的雜湊（相機 UniqueId＋姿態，排序後串起來＋模型 epoch）。</summary>
        private static readonly Dictionary<string, string> _overviewStoreyHash = new Dictionary<string, string>(StringComparer.Ordinal);

        private static string OverviewStoreyHash(Document doc, Level level)
        {
            List<Level> levels = CameraMath.GetStoreyLevelsSorted(doc);
            var parts = new List<string>();
            foreach (View3D v in new FilteredElementCollector(doc).OfClass(typeof(View3D)).Cast<View3D>())
            {
                if (v.IsTemplate || !v.IsPerspective || IsSheetPerspectiveCopy(v)) continue;
                CameraMath.CameraGeometry g;
                try { g = CameraMath.ComputeGeometry(v, levels); } catch { continue; }
                if (g.LevelBelow == null || g.LevelBelow.Id != level.Id) continue;
                parts.Add(v.UniqueId + "=" + ComputePoseHash(v) + "|" + v.Name);
            }
            parts.Sort(StringComparer.Ordinal);
            return ModelEpoch + "#" + string.Join(";", parts);
        }

        /// <summary>一鍵 4 視圖後自動更新該相機所在樓層的總覽（獨立交易；失敗只記 log，不影響 4 視圖）。
        /// 2026-09-26 第四輪：該樓層的相機（位置／方向／裁剪／名稱）與模型 epoch 自上次自動更新後都沒變、總覽也還在 → 略過。</summary>
        private static ElementId TryUpdateOverviewForCamera(Document doc, Level levelBelow)
        {
            if (doc == null || levelBelow == null) return null;
            try
            {
                string key = StoreyDocKey(doc) + "|" + levelBelow.UniqueId;
                string hash = null;
                try { hash = OverviewStoreyHash(doc, levelBelow); } catch (Exception exH) { Logger.Error("樓層總覽：計算略過判斷失敗（照常更新）", exH); }
                ViewPlan existing = FindOverviewView(doc, levelBelow);
                if (hash != null && existing != null && _overviewStoreyHash.TryGetValue(key, out string prev) && prev == hash)
                {
                    Logger.Info($"樓層總覽：「{existing.Name}」該樓層相機自上次更新後都沒變 → 略過（一鍵 4 視圖不重建總覽）");
                    return existing.Id;
                }
                List<OverviewResult> r = CreateOrUpdateOverviews(doc, new List<ElementId> { levelBelow.Id });
                OverviewResult one = r.FirstOrDefault();
                if (one?.Error != null) Logger.Error("一鍵 4 視圖：自動更新樓層總覽失敗：" + one.Error);
                else if (hash != null) _overviewStoreyHash[key] = hash;
                return one?.ViewId;
            }
            catch (Exception ex)
            {
                Logger.Error("一鍵 4 視圖：自動更新樓層總覽失敗（4 視圖不受影響）", ex);
                CameraManagerDiagnostics.RecordCaught("AutoOverview", ex);
                return null;
            }
        }
    }
}
