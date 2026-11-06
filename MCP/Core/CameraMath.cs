using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace RevitMCP.Core
{
    /// <summary>
    /// 相機（透視 3D 視圖）幾何計算的共用工具。
    ///
    /// 抽出理由：get_camera_info（<see cref="CommandExecutor"/> 的
    /// <c>CommandExecutor.CameraInfo.cs</c>）與相機管理視窗
    /// （<see cref="CameraManagerWindow"/>）都需要同一套「眼睛座標 → 樓層 → 離樓板高度／
    /// 俯仰角」換算，兩邊各寫一份容易漂移，所以集中在這裡。
    /// 這裡只做純數學計算，不觸碰 Document／Transaction，方便兩邊呼叫端各自決定
    /// 是否要在 ExternalEvent 裡執行。
    /// </summary>
    internal static class CameraMath
    {
        internal static double FeetToMm(double feet)
        {
            return UnitUtils.ConvertFromInternalUnits(feet, UnitTypeId.Millimeters);
        }

        internal static double MmToFeet(double mm)
        {
            return UnitUtils.ConvertToInternalUnits(mm, UnitTypeId.Millimeters);
        }

        /// <summary>取得文件內所有樓層，依 ProjectElevation 由低到高排序。</summary>
        internal static List<Level> GetLevelsSorted(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .OrderBy(l => l.ProjectElevation)
                .ToList();
        }

        /// <summary>Level 的「Building Story／建築樓層」（BuiltInParameter.LEVEL_IS_BUILDING_STORY）是否為是；
        /// 讀不到參數時視為是（保守不排除）。</summary>
        internal static bool IsBuildingStorey(Level level)
        {
            try
            {
                Parameter p = level?.get_Parameter(BuiltInParameter.LEVEL_IS_BUILDING_STORY);
                if (p == null || p.StorageType != StorageType.Integer) return true;
                return p.AsInteger() != 0;
            }
            catch
            {
                return true;
            }
        }

        // 最近一次 GetStoreyLevelsSorted 排除掉的非樓層 Level（只存數值，不存 Level 參照），給 ComputeGeometry 記 log 用。
        private static string _nonStoreyDocKey;
        private static List<Tuple<double, string, long>> _nonStoreyLevels = new List<Tuple<double, string, long>>();
        private static readonly HashSet<string> _loggedNonStoreySkips = new HashSet<string>(StringComparer.Ordinal);

        private static string DocKey(Document doc)
        {
            try { return (doc?.PathName ?? "") + "|" + (doc?.Title ?? ""); } catch { return "?"; }
        }

        /// <summary>
        /// 2026-09-25：相機管理用的「樓層」集合——只取 Building Story＝是 的 Level（依 ProjectElevation 由低到高）；
        /// 一個都沒有勾時退回全部 Level。女兒牆、屋突頂等非樓層 Level 不再被當成相機的「下方樓層」。
        /// </summary>
        internal static List<Level> GetStoreyLevelsSorted(Document doc)
        {
            List<Level> all = GetLevelsSorted(doc);
            List<Level> storeys = all.Where(IsBuildingStorey).ToList();
            var skipped = new List<Tuple<double, string, long>>();
            if (storeys.Count == 0)
            {
                Logger.Info($"相機管理：沒有任何 Level 勾選 Building Story，樓層判定退回全部 {all.Count} 個 Level。");
                storeys = all;
            }
            else
            {
                foreach (Level l in all)
                    if (!IsBuildingStorey(l)) skipped.Add(Tuple.Create(l.ProjectElevation, l.Name, (long)l.Id.GetIdValue()));
            }
            _nonStoreyDocKey = DocKey(doc);
            _nonStoreyLevels = skipped;
            return storeys;
        }

        /// <summary>若眼睛與所選樓層之間夾著被排除的非樓層 Level，記一次 log（同一相機＋同一 Level 只記一次）。</summary>
        private static void LogSkippedNonStorey(View3D view, Level storeyBelow, double eyeZFeet)
        {
            try
            {
                if (view == null || _nonStoreyLevels.Count == 0 || DocKey(view.Document) != _nonStoreyDocKey) return;
                double floor = storeyBelow != null ? storeyBelow.ProjectElevation : double.NegativeInfinity;
                Tuple<double, string, long> nearest = _nonStoreyLevels
                    .Where(t => t.Item1 > floor + 1e-9 && t.Item1 <= eyeZFeet + 1e-9)
                    .OrderByDescending(t => t.Item1).FirstOrDefault();
                if (nearest == null) return;
                string key = view.Id.GetIdValue() + ":" + nearest.Item3 + ":" + (storeyBelow?.Id.GetIdValue().ToString() ?? "null");
                if (!_loggedNonStoreySkips.Add(key)) return;
                Logger.Info($"相機管理：「{view.Name}」下方最近的 Level「{nearest.Item2}」不是建築樓層（Building Story=否），略過，改用樓層「{storeyBelow?.Name ?? "(無)"}」。");
            }
            catch { }
        }

        /// <summary>
        /// 找出眼睛所在高度「正下方」最近的那個樓層（樓層高程 ≤ 眼睛高程的最高一層）。
        /// levelsSorted 必須已依 ProjectElevation 由低到高排序（見 <see cref="GetLevelsSorted"/>）。
        /// </summary>
        internal static Level FindLevelBelow(IList<Level> levelsSorted, double eyeZFeet)
        {
            return levelsSorted.LastOrDefault(l => l.ProjectElevation <= eyeZFeet + 1e-9);
        }

        /// <summary>一個相機（透視 View3D）的幾何摘要，內部座標系（feet）＋常用衍生值（mm/度）。</summary>
        internal class CameraGeometry
        {
            public XYZ Eye;
            public XYZ Forward;
            public XYZ Up;
            public double EyeZMm;
            public double PitchDeg;
            public Level LevelBelow;
            /// <summary>眼睛高於 LevelBelow 的高度（mm），LevelBelow 為 null 時為 null。</summary>
            public double? AboveLevelBelowMm;
            /// <summary>2026-09-25：眼睛所在房間（CameraKeyPlanService 房間搜尋結果；透視相機才搜尋）。
            /// EyeRoomResolved＝true 表示已搜尋過（EyeRoom 可能是 Room==null 的 RoomCtx），同一份 geom 不再重搜。</summary>
            public CameraKeyPlanService.RoomCtx EyeRoom;
            public bool EyeRoomResolved;
            /// <summary>樓層來源：room＝室內相機取房間的 Level；elevation＝眼睛高程正下方的建築樓層（室外或找不到房間）。</summary>
            public string StoreySource = "elevation";
            /// <summary>依高程判定的建築樓層（改用房間樓層前的值，僅供 log／比對）。</summary>
            public Level StoreyByElevation;
            /// <summary>2026-09-26 第四輪：眼睛正下方的「站立面」高程（feet；ReferenceIntersector 往下打樓板／屋頂／地形），
            /// 只在需要時（室外相機、房間頂以上的候選）才算；Resolved＝已算過（Z 可能是 null＝沒打到）。</summary>
            public double? StandingSurfaceZ;
            public bool StandingSurfaceResolved;
            public string StandingSurfaceLog;
            /// <summary>2026-09-26 第四輪：樓層挑選說明（多層房間／室外站立面規則），供 log 與 list_cameras。</summary>
            public string StoreyLog;
        }

        /// <summary>
        /// 計算 view 的眼睛座標、視角俯仰、所在樓層與離樓板高度。
        /// levelsSorted 由呼叫端傳入（避免同一批相機重複收集樓層集合）。
        /// </summary>
        internal static CameraGeometry ComputeGeometry(View3D view, IList<Level> levelsSorted)
        {
            ViewOrientation3D orientation = view.GetOrientation();
            XYZ eye = orientation.EyePosition;
            XYZ forward = orientation.ForwardDirection;
            XYZ up = orientation.UpDirection;

            double eyeZMm = FeetToMm(eye.Z);
            double pitchDeg = Math.Round(Math.Asin(Math.Max(-1.0, Math.Min(1.0, forward.Z))) * 180.0 / Math.PI, 2);

            Level levelBelow = FindLevelBelow(levelsSorted, eye.Z);
            LogSkippedNonStorey(view, levelBelow, eye.Z);
            double? above = levelBelow != null
                ? (double?)Math.Round(eyeZMm - FeetToMm(levelBelow.ProjectElevation), 1)
                : null;

            var geom = new CameraGeometry
            {
                Eye = eye,
                Forward = forward,
                Up = up,
                EyeZMm = eyeZMm,
                PitchDeg = pitchDeg,
                LevelBelow = levelBelow,
                AboveLevelBelowMm = above,
                StoreyByElevation = levelBelow
            };

            // 2026-09-25（MCP 自測：室內相機的樓層被判成女兒牆等 Building Story）：室內相機的樓層＝眼睛所在房間的 Level，
            // 離樓板高度也改以該樓層計算。清單、位置圖、Key Plan、總覽分組、A 標記都經由這裡取得同一個樓層。
            if (view != null && view.IsPerspective && !view.IsTemplate)
            {
                try { CameraKeyPlanService.ApplyIndoorStorey(view, geom); }
                catch (Exception ex) { Logger.Error("相機管理：室內樓層判定失敗（沿用高程判定的樓層）", ex); }
            }
            return geom;
        }

        /// <summary>
        /// 依「離指定樓層的高度（cm）」建立新的 ViewOrientation3D：
        /// 只移動眼睛的 Z（樓層高程 + 高度），XY、Forward、Up 完全不變 —— 這樣俯仰角與
        /// 平面上的位置／朝向都不會被這個操作改動，符合「只調高度」的使用者預期。
        /// </summary>
        /// <summary>
        /// 取得性質面板上「Eye Elevation」參數的顯示字串（保留原字串與單位，不重新換算）。
        /// 找不到對應內建參數時回傳 "—"。判斷方式沿用 get_camera_info 原本的
        /// 「BuiltInParameter 名稱含 EYE、不含 TARGET」規則。
        /// </summary>
        internal static string GetEyeElevationDisplayString(View3D view)
        {
            foreach (Parameter p in view.Parameters)
            {
                if (p == null || p.StorageType != StorageType.Double) continue;
                if (!(p.Definition is InternalDefinition def)) continue;

                string bipName = def.BuiltInParameter.ToString();
                bool isEye = bipName.IndexOf("EYE", StringComparison.OrdinalIgnoreCase) >= 0;
                bool isTarget = bipName.IndexOf("TARGET", StringComparison.OrdinalIgnoreCase) >= 0;
                if (isEye && !isTarget)
                    return p.AsValueString();
            }
            return "—";
        }

        internal static ViewOrientation3D BuildOrientationForHeightAboveLevel(
            View3D view, Level level, double heightAboveLevelCm)
        {
            if (level == null) throw new ArgumentNullException(nameof(level));

            ViewOrientation3D orientation = view.GetOrientation();
            XYZ eye = orientation.EyePosition;
            XYZ forward = orientation.ForwardDirection;
            XYZ up = orientation.UpDirection;

            double heightFeet = MmToFeet(heightAboveLevelCm * 10.0); // cm -> mm -> feet
            double newZFeet = level.ProjectElevation + heightFeet;

            XYZ newEye = new XYZ(eye.X, eye.Y, newZFeet);
            return new ViewOrientation3D(newEye, up, forward);
        }

        // ══════════════════════════════════════════════════════════
        // 4.3 細節編輯：位置 / 樓層 / 角度 —— 新增於相機管理停靠面板（2026-09-24）
        // ══════════════════════════════════════════════════════════

        /// <summary>
        /// 由 forward 方向算「水平角」：0 = 專案北 (+Y)，順時針為正（對應羅盤習慣，也是
        /// 面板 4.2/4.3 要求的定義）。atan2(x, y) 在 +Y 時為 0，x 增加（往 +X／順時針）時
        /// 角度變大，恰好符合「順時針為正」。
        /// </summary>
        internal static double ComputeHorizontalAngleDeg(XYZ forward)
        {
            double angle = Math.Atan2(forward.X, forward.Y) * 180.0 / Math.PI;
            if (angle < 0) angle += 360.0;
            return Math.Round(angle, 2);
        }

        /// <summary>依水平角（0=+Y，順時針）與俯仰角（0=水平，正值往上看）反算單位方向向量。</summary>
        internal static XYZ DirectionFromAngles(double horizontalAngleDeg, double pitchDeg)
        {
            double hRad = horizontalAngleDeg * Math.PI / 180.0;
            double pRad = pitchDeg * Math.PI / 180.0;
            double horizLen = Math.Cos(pRad);
            double x = Math.Sin(hRad) * horizLen;
            double y = Math.Cos(hRad) * horizLen;
            double z = Math.Sin(pRad);
            XYZ v = new XYZ(x, y, z);
            return v.GetLength() > 1e-9 ? v.Normalize() : XYZ.BasisY;
        }

        /// <summary>
        /// 給定新的 forward，反算一個「不滾動」(roll = 0) 的 up 向量：永遠盡量讓相機保持水平，
        /// 這是一般建築相機的預期行為（面板只提供水平角／俯仰角兩個角度欄位，沒有滾動欄位）。
        /// forward 幾乎垂直（正對地板/天花）時，世界 Z 軸退化，改用 BasisX 當參考避免除以零。
        /// </summary>
        internal static XYZ BuildLevelUp(XYZ forward)
        {
            XYZ worldUp = XYZ.BasisZ;
            XYZ right = forward.CrossProduct(worldUp);
            if (right.GetLength() < 1e-6)
            {
                right = forward.CrossProduct(XYZ.BasisX);
                if (right.GetLength() < 1e-6) right = XYZ.BasisY;
            }
            right = right.Normalize();
            return right.CrossProduct(forward).Normalize();
        }

        /// <summary>
        /// 標準等角方向（2026-09-25）：在 ViewCube 上方四角（水平 yaw 45°/135°/225°/315°，
        /// 0=+Y 順時針）中挑水平方向與 <paramref name="forwardHoriz"/> 最接近者，
        /// 回傳 forward＝(±1, ±1, −1)/√3（俯角 atan(1/√2)≈35.264°）。
        /// </summary>
        internal static XYZ SnapToIsoCorner(XYZ forwardHoriz, out double cornerYawDeg)
        {
            double fx = forwardHoriz != null ? forwardHoriz.X : 0.0;
            double fy = forwardHoriz != null ? forwardHoriz.Y : 1.0;
            if (Math.Abs(fx) < 1e-9 && Math.Abs(fy) < 1e-9) fy = 1.0;
            // 與 (sx, sy)/√2 的內積最大 ⇔ sx=sign(fx), sy=sign(fy)（0 視為 +）
            double sx = fx >= 0 ? 1.0 : -1.0;
            double sy = fy >= 0 ? 1.0 : -1.0;
            cornerYawDeg = ComputeHorizontalAngleDeg(new XYZ(sx, sy, 0));
            double inv = 1.0 / Math.Sqrt(3.0);
            return new XYZ(sx * inv, sy * inv, -inv);
        }

        /// <summary>
        /// 依「新水平角／新俯仰角」重建方向，眼睛位置完全不變（4.3「改角度時保持眼睛位置不動」）。
        /// </summary>
        internal static ViewOrientation3D BuildOrientationForAngles(
            View3D view, double horizontalAngleDeg, double pitchDeg)
        {
            ViewOrientation3D orientation = view.GetOrientation();
            XYZ eye = orientation.EyePosition;
            XYZ forward = DirectionFromAngles(horizontalAngleDeg, pitchDeg);
            XYZ up = BuildLevelUp(forward);
            return new ViewOrientation3D(eye, up, forward);
        }

        /// <summary>
        /// 依「新平面座標（feet）＋新樓層＋離樓板高度 (cm)」重建眼睛位置，方向／上方向不變
        /// （4.3「換樓層時保持離樓板高度」，平面 X/Y 編輯同理只動位置）。
        /// </summary>
        internal static ViewOrientation3D BuildOrientationForPosition(
            View3D view, double eyeXFeet, double eyeYFeet, Level level, double heightAboveLevelCm)
        {
            if (level == null) throw new ArgumentNullException(nameof(level));

            ViewOrientation3D orientation = view.GetOrientation();
            XYZ forward = orientation.ForwardDirection;
            XYZ up = orientation.UpDirection;

            double heightFeet = MmToFeet(heightAboveLevelCm * 10.0);
            double newZFeet = level.ProjectElevation + heightFeet;

            XYZ newEye = new XYZ(eyeXFeet, eyeYFeet, newZFeet);
            return new ViewOrientation3D(newEye, up, forward);
        }

        /// <summary>
        /// 水平視野角（度），2026-09-25 起委派 <see cref="ComputePerspectiveFov"/>（下段為舊版說明，舊演算法已移除）。⚠️ 未經實機數值驗證（無法在無 Revit 環境下確認），僅供參考、
        /// 絕不寫回模型 —— 面板上一律唯讀顯示。
        ///
        /// 推算方式：透視 View3D 的 CropBox 是「視圖自身座標系」下的方盒，X/Y 為裁剪範圍的
        /// 寬高、Z 為沿視線方向的深度（近／遠裁剪）；取 Max.Z 當作裁剪矩形所在的深度，
        /// 用 half-width / depth 反算半視角。這是社群常見的估算法（無官方 API 直接回傳 FOV），
        /// 是否等於 Revit 內部真正用來渲染畫面的視野角，需要使用者在 Revit 內用已知 FOV 的
        /// 相機實測比對後才能確認；因此本欄位標示「估算」而非精確值。
        /// </summary>
        internal static double? EstimateHorizontalFovDeg(View3D view)
        {
            // 2026-09-25：改由 ComputePerspectiveFov 統一計算（清單 FOV 欄、迷你地圖、平面/剖面扇形共用）。
            double? h = ComputePerspectiveFov(view).HFovDeg;
            return h.HasValue ? (double?)Math.Round(h.Value, 1) : null;
        }

        /// <summary>垂直視野角（度）：改由 <see cref="ComputePerspectiveFov"/> 計算；算不出來才退回
        /// vFOV = 2·atan(tan(hFOV/2)·cropH/cropW)。</summary>
        internal static double? EstimateVerticalFovDeg(View3D view, double? horizontalFovDeg)
        {
            try
            {
                PerspectiveFov f = ComputePerspectiveFov(view);
                if (f.VFovDeg.HasValue) return f.VFovDeg;
                if (!horizontalFovDeg.HasValue) return null;
                BoundingBoxXYZ box = view.CropBox;
                if (box == null) return null;
                double w = box.Max.X - box.Min.X, h = box.Max.Y - box.Min.Y;
                if (w <= 1e-9 || h <= 1e-9) return null;
                double halfH = horizontalFovDeg.Value / 2.0 * Math.PI / 180.0;
                double v = 2.0 * Math.Atan(Math.Tan(halfH) * (h / w));
                return v * 180.0 / Math.PI;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>透視圖視野角計算結果（度）＋供 log 的所有候選值。</summary>
        internal sealed class PerspectiveFov
        {
            public double? HFovDeg;
            public double? VFovDeg;
            /// <summary>2026-09-25（非對稱視錐）：四個邊界相對「相機正前方」的帶號角度（度）。
            /// 水平沿相機 right 向量（right＝forward×up）為正：左緣通常為負、右緣為正；垂直沿相機 up 為正：
            /// 下緣通常為負、上緣為正。使用者只拖裁剪框一邊時 Revit 視錐會不對稱（實機 log：xMm[-29,14]
            /// dMm[30,30] → 左 −44.0°／右 +25.0°），扇形必須用這四個角度而不是 ±h/2。
            /// 候選 C（裁剪形狀）可用時＝形狀頂點的 atan2 極值；退回 A/B（CropBox）時＝對稱 ±h/2、±v/2。</summary>
            public double? LeftDeg;
            public double? RightDeg;
            public double? DownDeg;
            public double? UpDeg;
            public string Source;
            public string Log;
        }

        /// <summary>
        /// 2026-09-25：透視 View3D 的水平／垂直視野角。
        ///
        /// 舊版錯誤（實機 log：hFOV 一律 '?'）：用 <c>CropBox.Max.Z</c> 當深度且要求 &gt;0。依 Revit API
        /// 開發者指南〈View3D〉：視圖座標系「X 向右、Y 向上、Z 朝向使用者」，透視裁剪框是「以觀察者為頂點
        /// 的角錐、介於前後兩個平行裁剪面之間的部分」，<c>CropBox.Max</c> 在前裁剪面上、<c>CropBox.Min</c>
        /// 是前裁剪面矩形投影到後裁剪面的點——所以 Max.Z 在眼睛前方時是「負值」，舊版必定回傳 null。
        ///
        /// 三個候選（全部記 log，供實機拖曳裁剪框時比對哪個值會變）：
        ///   C＝裁剪形狀（GetCropRegionShapeManager().GetCropShape()）：每個頂點相對眼睛分解成
        ///      (right, up, forward) 分量，角度＝atan2(x, d)；不需要知道裁剪矩形在哪個深度。
        ///   A＝CropBox 寬/高 在「前裁剪面深度」dFront＝(Transform.OfPoint(Max) − eye)·forward。
        ///   B＝CropBox 寬/高 在「後裁剪面深度」dBack＝(Transform.OfPoint(Min) − eye)·forward。
        /// 採用順序：C（有效且 1°–179°）→ A → B。A 與 C 若不一致（&gt;2°）一併記 log。
        /// ⚠️ 未經實機驗證哪個候選等於 Revit 內部真正的視野角；以 log 對照「新放相機（預設 50° 鏡頭／1.5:1）」確認。
        /// </summary>
        internal static PerspectiveFov ComputePerspectiveFov(View3D view)
        {
            var r = new PerspectiveFov();
            var log = new System.Text.StringBuilder();
            try
            {
                if (view == null || !view.IsPerspective) { r.Log = "not perspective"; return r; }
                ViewOrientation3D o = view.GetOrientation();
                XYZ eye = o.EyePosition;
                XYZ fwd = o.ForwardDirection.Normalize();
                XYZ up = o.UpDirection.Normalize();
                // 視圖座標 X＝Y×Z＝up×(−forward)＝forward×up＝相機右（例：forward=+Y、up=+Z → right=+X，面向北時東為右）。
                XYZ right = fwd.CrossProduct(up).Normalize();

                double? cH = null, cV = null;
                double cL = 0, cR = 0, cD = 0, cU = 0;
                try
                {
                    ViewCropRegionShapeManager mgr = view.GetCropRegionShapeManager();
                    IList<CurveLoop> loops = mgr?.GetCropShape();
                    if (loops != null && loops.Count > 0)
                    {
                        double axMin = double.MaxValue, axMax = double.MinValue, ayMin = double.MaxValue, ayMax = double.MinValue;
                        double xMin = double.MaxValue, xMax = double.MinValue, yMin = double.MaxValue, yMax = double.MinValue;
                        double dMin = double.MaxValue, dMax = double.MinValue;
                        int n = 0; bool bad = false;
                        foreach (Curve c in loops[0])
                        {
                            foreach (XYZ p in c.Tessellate())
                            {
                                XYZ v = p - eye;
                                double d = v.DotProduct(fwd), x = v.DotProduct(right), y = v.DotProduct(up);
                                xMin = Math.Min(xMin, x); xMax = Math.Max(xMax, x); yMin = Math.Min(yMin, y); yMax = Math.Max(yMax, y);
                                dMin = Math.Min(dMin, d); dMax = Math.Max(dMax, d);
                                n++;
                                if (d <= 1e-6) { bad = true; continue; }
                                double ax = Math.Atan2(x, d), ay = Math.Atan2(y, d);
                                axMin = Math.Min(axMin, ax); axMax = Math.Max(axMax, ax);
                                ayMin = Math.Min(ayMin, ay); ayMax = Math.Max(ayMax, ay);
                            }
                        }
                        log.Append($"shape[{loops.Count} loops,{n} pts] xMm[{FeetToMm(xMin):F0},{FeetToMm(xMax):F0}] yMm[{FeetToMm(yMin):F0},{FeetToMm(yMax):F0}] dMm[{FeetToMm(dMin):F0},{FeetToMm(dMax):F0}]");
                        if (!bad && n > 0)
                        {
                            cH = (axMax - axMin) * 180.0 / Math.PI;
                            cV = (ayMax - ayMin) * 180.0 / Math.PI;
                            // 帶號邊界角：x＝沿 right（相機右）分量、y＝沿 up 分量 → 最小值＝左/下緣，最大值＝右/上緣。
                            cL = axMin * 180.0 / Math.PI; cR = axMax * 180.0 / Math.PI;
                            cD = ayMin * 180.0 / Math.PI; cU = ayMax * 180.0 / Math.PI;
                            log.Append($" → C h={cH:F1}° v={cV:F1}° edges L={cL:F1}° R={cR:F1}° D={cD:F1}° U={cU:F1}°");
                        }
                        else log.Append(" → C invalid (point not in front of eye)");
                    }
                    else log.Append("shape=none");
                }
                catch (Exception ex) { log.Append("shape err " + ex.GetType().Name); }

                double? aH = null, aV = null, bH = null, bV = null;
                try
                {
                    BoundingBoxXYZ box = view.CropBox;
                    if (box != null)
                    {
                        Transform tf = box.Transform ?? Transform.Identity;
                        double w = box.Max.X - box.Min.X, h = box.Max.Y - box.Min.Y;
                        double dFront = (tf.OfPoint(box.Max) - eye).DotProduct(fwd);
                        double dBack = (tf.OfPoint(box.Min) - eye).DotProduct(fwd);
                        log.Append($" | cropBox MinMm=({FeetToMm(box.Min.X):F0},{FeetToMm(box.Min.Y):F0},{FeetToMm(box.Min.Z):F0}) MaxMm=({FeetToMm(box.Max.X):F0},{FeetToMm(box.Max.Y):F0},{FeetToMm(box.Max.Z):F0}) " +
                                   $"w={FeetToMm(w):F0} h={FeetToMm(h):F0} aspect={(h > 1e-9 ? w / h : 0):F3} tfOrigin−eye={FeetToMm(tf.Origin.DistanceTo(eye)):F0}mm basisZ·fwd={tf.BasisZ.DotProduct(fwd):F3} dFront={FeetToMm(dFront):F0} dBack={FeetToMm(dBack):F0}");
                        if (dFront > 1e-6 && w > 1e-9 && h > 1e-9)
                        {
                            aH = 2 * Math.Atan(w / 2 / dFront) * 180 / Math.PI; aV = 2 * Math.Atan(h / 2 / dFront) * 180 / Math.PI;
                            log.Append($" → A h={aH:F1}° v={aV:F1}°");
                        }
                        if (dBack > 1e-6 && w > 1e-9 && h > 1e-9)
                        {
                            bH = 2 * Math.Atan(w / 2 / dBack) * 180 / Math.PI; bV = 2 * Math.Atan(h / 2 / dBack) * 180 / Math.PI;
                            log.Append($" → B h={bH:F1}° v={bV:F1}°");
                        }
                    }
                }
                catch (Exception ex) { log.Append(" | cropBox err " + ex.GetType().Name); }

                bool Ok(double? x) => x.HasValue && x.Value > 1.0 && x.Value < 179.0;
                if (Ok(cH) && Ok(cV))
                {
                    r.HFovDeg = cH; r.VFovDeg = cV; r.Source = "C(cropShape)";
                    r.LeftDeg = cL; r.RightDeg = cR; r.DownDeg = cD; r.UpDeg = cU;
                }
                else if (Ok(aH) && Ok(aV)) { r.HFovDeg = aH; r.VFovDeg = aV; r.Source = "A(cropBox@front)"; }
                else if (Ok(bH) && Ok(bV)) { r.HFovDeg = bH; r.VFovDeg = bV; r.Source = "B(cropBox@back)"; }
                else r.Source = "none";
                if (r.HFovDeg.HasValue && !r.LeftDeg.HasValue)
                {
                    // A/B 沒有邊界位置資訊 → 對稱。
                    r.LeftDeg = -r.HFovDeg.Value / 2.0; r.RightDeg = r.HFovDeg.Value / 2.0;
                    r.DownDeg = -r.VFovDeg.Value / 2.0; r.UpDeg = r.VFovDeg.Value / 2.0;
                }
                if (Ok(cH) && Ok(aH) && Math.Abs(cH.Value - aH.Value) > 2.0) log.Append($" | NOTE C≠A (Δh={cH.Value - aH.Value:F1}°)");
                log.Append(" | chosen=" + r.Source);
            }
            catch (Exception ex)
            {
                log.Append(" | err " + ex.GetType().Name + ": " + ex.Message);
            }
            r.Log = log.ToString();
            return r;
        }

        /// <summary>
        /// 找出「遠裁剪」相關的 Double 參數（比照 <see cref="GetEyeElevationDisplayString"/>
        /// 用 BuiltInParameter 列舉名稱比對，不吃顯示字串／語系）。回不到就回傳 null，
        /// 呼叫端應改唯讀並說明查無此參數。
        /// </summary>
        internal static Parameter FindFarClipOffsetParameter(View3D view)
        {
            foreach (Parameter p in view.Parameters)
            {
                if (p == null || p.StorageType != StorageType.Double) continue;
                if (!(p.Definition is InternalDefinition def)) continue;
                string bipName = def.BuiltInParameter.ToString();
                if (bipName.IndexOf("FAR", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    bipName.IndexOf("OFFSET", StringComparison.OrdinalIgnoreCase) >= 0)
                    return p;
            }
            return null;
        }
    }
}
