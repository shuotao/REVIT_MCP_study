using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace RevitMCP.Core
{
    // 相機管理停靠面板的資料模型，只是純資料，不含任何 Revit API 呼叫。
    //
    // 注意：這裡全部用 C# property（{ get; set; }），不是 public field —— WPF 的
    // Binding 只認得屬性，綁 public field 會在畫面上悄悄留白（不會拋例外，非常難查）。
    // DataGridTextColumn 的 Binding 就是綁在這些屬性上。

    /// <summary>DataGrid 一列 = 一個透視相機視圖。</summary>
    internal class CameraRow
    {
        public ElementId ViewId { get; set; }
        /// <summary>相機視圖的 Element.UniqueId（跨 session 穩定，不受 ElementId 重新編號影響）；
        /// 用於 Key Plan 剖面等以 Extensible Storage 連結相機的查詢（見 CameraKeyPlanService）。</summary>
        public string UniqueId { get; set; }
        public string Name { get; set; }
        public string LevelName { get; set; }
        /// <summary>Eye 正下方那個樓層的 ElementId；找不到基準樓層時為 null。用於挑選預設底圖平面、換樓層編輯。</summary>
        public ElementId LevelId { get; set; }
        /// <summary>離樓板高度 (cm)，找不到樓層下方基準時為 null。</summary>
        public double? HeightAboveLevelCm { get; set; }
        /// <summary>水平角 °：0 = 專案北 (+Y)，順時針為正。</summary>
        public double HorizontalAngleDeg { get; set; }
        public double PitchDeg { get; set; }
        /// <summary>Eye Elevation 參數的原始顯示字串（性質面板看到的字串，含專案顯示單位）。</summary>
        public string EyeElevationDisplay { get; set; }
        /// <summary>水平視野角估算值（度）；未經實機驗證，唯讀顯示，絕不寫回模型。null = 算不出來。</summary>
        public double? FovEstimateDeg { get; set; }
        /// <summary>2026-09-25：水平視錐左/右緣相對正前方的帶號角度（度，相機右為正；見 CameraMath.PerspectiveFov）。
        /// 使用者只拖裁剪框一邊時兩者不對稱；迷你地圖扇形用這兩個值。null＝算不出來（退回對稱）。</summary>
        public double? FovLeftDeg { get; set; }
        public double? FovRightDeg { get; set; }
        /// <summary>遠裁剪距離 (cm)；找不到對應參數時為 null，面板應改唯讀並說明。</summary>
        public double? FarClipCm { get; set; }
        /// <summary>遠裁剪參數是否可寫入（IsReadOnly==false）。找不到參數時一律 false。</summary>
        public bool FarClipEditable { get; set; }
        /// <summary>清單「遠裁剪 cm」常駐 TextBox 的 IsReadOnly 繫結用（2026-09-25）。</summary>
        public bool FarClipReadOnly => !FarClipEditable;

        // 平面座標（內部單位 feet），用於底圖疊圖與 Apply 高度時的比對。
        public double EyeXFeet { get; set; }
        public double EyeYFeet { get; set; }
        public double EyeZFeet { get; set; }
        public double ForwardXFeet { get; set; }
        public double ForwardYFeet { get; set; }
        public double ForwardZFeet { get; set; }

        /// <summary>是否落在目前底圖的裁剪範圍內；false 時只列在清單、不畫在底圖上。</summary>
        public bool InMapRange { get; set; }

        /// <summary>"" = 正常，"偏低"／"偏高" = 高度落在建議範圍外。</summary>
        public string HeightWarning { get; set; }

        /// <summary>這台相機是否已經有「一鍵 4 視圖」建立的 Key Plan 剖面（ES 連結）。false 時面板
        /// 不排讀剖面線的 ExternalEvent（2026-09-24 第三輪）。</summary>
        public bool HasKeyPlanSection { get; set; }

        /// <summary>2026-09-25 Wave 3：這台相機已儲存的「視角版本」名稱（ES，依儲存順序）；右鍵「視角版本 ▸」用。</summary>
        public List<string> VersionNames { get; set; } = new List<string>();
        /// <summary>2026-09-26 第四輪：目前視角符合的版本名稱（眼睛 1 mm、方向 0.05° 內；null＝都不符合）。右鍵選單打 ✓。</summary>
        public string CurrentVersionName { get; set; }
        /// <summary>2026-09-26 第四輪：樓層來源（room／roomSpan／surface／elevation）與挑選說明（ToolTip、list_cameras）。</summary>
        public string StoreySource { get; set; }
        public string StoreyLog { get; set; }

        /// <summary>2026-09-25 室內／室外判定（與一鍵 4 視圖同一套 FindEyeRoom）：true＝室外（眼睛不在任何房間），
        /// false＝室內，null＝判定失敗／找不到樓層。</summary>
        public bool? IsOutdoor { get; set; }

        // 2026-09-25（§4.8 步驟 0）：相機放置時的平面（CameraSourcePlanStore 以 ES 記錄；沒有記錄＝null）。
        public ElementId SourcePlanId { get; set; }
        public string SourcePlanName { get; set; }
        /// <summary>放置平面目前是否啟用裁剪框（未裁剪的平面不能當底圖，改找它的父／從屬視圖）。</summary>
        public bool SourcePlanCropped { get; set; }
        /// <summary>放置平面若是從屬視圖，其主視圖 Id；否則 null。</summary>
        public ElementId SourcePlanPrimaryId { get; set; }
        /// <summary>室外相機的扇形半徑（feet）＝min(遠裁剪, 室外範圍半徑)；迷你地圖以模型比例畫出。室內為 0。</summary>
        public double OutdoorFanRadiusFeet { get; set; }
        /// <summary>DataGrid 列 ToolTip 前綴用：「室內」「室外」或「?」。</summary>
        public string IndoorOutdoorText => IsOutdoor.HasValue ? (IsOutdoor.Value ? "室外" : "室內") : "?";

        /// <summary>2026-09-27：這台相機是否啟用「剖面轉 90°」（單一相機、持久化於相機元素的 ES，
        /// 見 CameraKeyPlanService.GetCameraSectionRotate90）。右鍵選單／④ 下拉選單打勾狀態用。</summary>
        public bool SectionRotate90 { get; set; }

        public string ViewIdText => ViewId.GetIdValue().ToString();

        public string HeightAboveLevelCmText => HeightAboveLevelCm.HasValue
            ? HeightAboveLevelCm.Value.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) // 2026-09-26 稽核：與其他欄（WPF StringFormat＝en-US）一致用小數點
            : "—";

        /// <summary>DataGrid「遠裁剪 cm」選用欄的顯示字串（找不到參數時顯示 —）。</summary>
        public string FarClipCmText => FarClipCm.HasValue
            ? FarClipCm.Value.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)
            : "—";

        /// <summary>DataGrid「狀態」欄：高度示警優先，其次是不在底圖範圍時的提示，都沒有就空白。</summary>
        public string StatusText
        {
            get
            {
                if (!string.IsNullOrEmpty(HeightWarning)) return "高度" + HeightWarning;
                if (!InMapRange) return "不在底圖範圍";
                return "";
            }
        }

        /// <summary>平面座標 X（cm），提供給細節編輯欄使用（以內部單位換算，供顯示與回填）。</summary>
        public double EyeXCm => CameraMath.FeetToMm(EyeXFeet) / 10.0;
        public double EyeYCm => CameraMath.FeetToMm(EyeYFeet) / 10.0;
    }

    /// <summary>ComboBox 裡的一個平面視圖選項。</summary>
    internal class PlanViewOption
    {
        public ElementId ViewId { get; set; }
        public string DisplayName { get; set; }
        public ElementId GenLevelId { get; set; }
        /// <summary>平面視圖本身的名稱（不含類型後綴）；底圖清單精簡模式標籤用。</summary>
        public string PlanName { get; set; }
        /// <summary>GenLevel 名稱（沒有則 null）。</summary>
        public string LevelName { get; set; }

        // 2026-09-25（③ 底圖挑選）：裁剪框（CropBox）攤平成純 double——原點、X/Y 基底（只取 XY 分量）、
        // 視圖自身座標的 Min/Max（feet）。HasCrop＝false 表示讀不到（不參與「包含相機」判斷）。
        public bool HasCrop { get; set; }
        public double CropOriginX { get; set; }
        public double CropOriginY { get; set; }
        public double CropBasisXX { get; set; } = 1;
        public double CropBasisXY { get; set; }
        public double CropBasisYX { get; set; }
        public double CropBasisYY { get; set; } = 1;
        public double CropMinX { get; set; }
        public double CropMinY { get; set; }
        public double CropMaxX { get; set; }
        public double CropMaxY { get; set; }
        /// <summary>ViewType == FloorPlan（天花／面積平面為 false）。</summary>
        public bool IsFloorPlan { get; set; }
        /// <summary>從屬視圖（GetPrimaryViewId 有效）。挑底圖時次要。</summary>
        public bool IsDependent { get; set; }
        /// <summary>2026-09-25（§4.8 第二版）：GenLevel 的 ProjectElevation（feet，與相機眼睛 Z 同座標）；沒有 GenLevel＝null。</summary>
        public double? GenLevelElevationFeet { get; set; }
        /// <summary>GenLevel 是否屬於相機管理的「建築樓層」集合（CameraMath.GetStoreyLevelsSorted）。</summary>
        public bool GenLevelIsStorey { get; set; }
        /// <summary>從屬視圖的主視圖 Id（非從屬＝null）；步驟 0 找放置平面的父／從屬視圖用。</summary>
        public ElementId PrimaryViewId { get; set; }

        public double CropWidthFeet => HasCrop ? System.Math.Abs(CropMaxX - CropMinX) : 0;
        public double CropHeightFeet => HasCrop ? System.Math.Abs(CropMaxY - CropMinY) : 0;
        public double CropAreaSqFeet => CropWidthFeet * CropHeightFeet;

        /// <summary>模型座標 (x,y) 是否落在裁剪框內（矩形框；非矩形裁剪形狀以外接框近似）。純算術。</summary>
        public bool CropContains(double x, double y)
        {
            if (!HasCrop) return false;
            double dx = x - CropOriginX, dy = y - CropOriginY;
            double lx = dx * CropBasisXX + dy * CropBasisXY;
            double ly = dx * CropBasisYX + dy * CropBasisYY;
            return lx >= System.Math.Min(CropMinX, CropMaxX) && lx <= System.Math.Max(CropMinX, CropMaxX) &&
                   ly >= System.Math.Min(CropMinY, CropMaxY) && ly <= System.Math.Max(CropMinY, CropMaxY);
        }

        /// <summary>log 用：「名稱 id=… 裁剪 W×H m」。</summary>
        public string CropLogText =>
            $"「{PlanName ?? DisplayName}」id={ViewId?.GetIdValue()} 裁剪 {CameraMath.FeetToMm(CropWidthFeet) / 1000.0:F1}×{CameraMath.FeetToMm(CropHeightFeet) / 1000.0:F1} m" +
            (IsDependent ? "（從屬）" : "") + (IsFloorPlan ? "" : "（非樓層平面）");

        public override string ToString() => DisplayName;
    }

    /// <summary>
    /// 2026-09-25（規格 §4.8 底圖挑選）：自動挑底圖（預設、跟隨、選取自動切換、精簡清單）一律呼叫
    /// <see cref="Choose"/>，純資料運算，不碰 Revit API。第二版（Snowdon Towers 實測：一樓由多個高程相近的
    /// 「L1 - Block xx」樓層組成，相機所在樓層的平面都不含相機、Site Plan 掛在高 1 m 的另一個 L1 樓層）：
    ///   步驟 0：相機「放置時的平面」（CameraSourcePlanStore 以 ES 記錄）——已裁剪就直接用；未裁剪改用其父／從屬／
    ///           同主視圖的已裁剪平面（須包含相機）；都沒有才往下；
    ///   步驟 1：相機樓層上、裁剪框包含相機眼睛 XY 的樓層平面 → 非從屬優先、面積最大；
    ///   步驟 2：其他樓層、高程在相機樓層 ±1.5 m 內、包含眼睛的樓層平面 → 高程最接近優先、面積最大；
    ///   步驟 3：任何建築樓層（Building Story）、高程 ≤ 眼睛高程、包含眼睛的樓層平面 → 最高樓層優先、面積最大；
    ///   步驟 4：相機樓層面積最大的已裁剪平面（舊規則：樓層平面優先、非從屬優先），
    ///           該層沒有 → 最近下方樓層、再最近上方樓層（各層同舊規則）。
    /// 多台相機（預設底圖）時，「包含」以包含台數多者優先，再套各步驟的排序。
    /// </summary>
    internal static class BaseMapPlanChooser
    {
        /// <summary>步驟 2 的高程容差：±1.5 m。</summary>
        internal const double NearElevationToleranceFeet = 1500.0 / 304.8;

        /// <param name="levelId">相機所在（下方）樓層。</param>
        /// <param name="eyes">相機眼睛模型座標 {x, y[, z]}（feet）；null／空＝直接走步驟 4。</param>
        /// <param name="levelsByElevation">建築樓層（由低到高）；用於相機樓層高程與步驟 4 上下樓層退回，可為 null。</param>
        /// <param name="step">選中的步驟 0–4（0＝放置平面，4 含上下樓層退回）；-1＝找不到。</param>
        /// <param name="src">步驟 0：相機放置時的平面（<see cref="SourcePlanHint.From"/>）；null＝沒有記錄。</param>
        internal static PlanViewOption Choose(IEnumerable<PlanViewOption> all, ElementId levelId, IList<double[]> eyes,
            IList<LevelOption> levelsByElevation, out string why, out int step, SourcePlanHint src = null)
        {
            why = ""; step = -1;
            if (all == null) { why = "no plans"; return null; }
            List<PlanViewOption> plans = all.Where(p => p != null && p.ViewId != null).ToList();
            List<double[]> eyeList = (eyes ?? new List<double[]>()).Where(e => e != null && e.Length >= 2).ToList();

            int Contained(PlanViewOption p)
            {
                int n = 0;
                foreach (double[] e in eyeList) if (p.CropContains(e[0], e[1])) n++;
                return n;
            }
            bool OnLevel(PlanViewOption p, long id) => p.GenLevelId != null && p.GenLevelId.GetIdValue() == id;
            string Tag(PlanViewOption p, int c) => $"包含相機 {c}/{eyeList.Count} 台";

            // 步驟 0：相機放置時的平面。已裁剪 → 直接用；未裁剪 → 改找它的父／從屬／同主視圖的已裁剪平面
            // （有相機座標時須包含相機），都沒有就往下走步驟 1–4。
            string step0Note = "";
            if (src?.ViewId != null)
            {
                long sid = src.ViewId.GetIdValue();
                PlanViewOption exact = plans.FirstOrDefault(p => p.ViewId.GetIdValue() == sid);
                if (exact != null)
                {
                    step = 0; why = $"步驟0 底圖＝相機放置時的平面「{src.Name ?? exact.PlanName}」";
                    return exact;
                }
                long pid = src.PrimaryId != null ? (long)src.PrimaryId.GetIdValue() : -1;
                List<PlanViewOption> related = plans.Where(p =>
                        (pid > 0 && p.ViewId.GetIdValue() == pid) ||
                        (p.PrimaryViewId != null && (p.PrimaryViewId.GetIdValue() == sid || (pid > 0 && p.PrimaryViewId.GetIdValue() == pid))))
                    .Where(p => eyeList.Count == 0 || Contained(p) > 0).ToList();
                if (related.Count > 0)
                {
                    PlanViewOption b = related.OrderByDescending(Contained).ThenBy(p => p.IsDependent ? 1 : 0)
                        .ThenByDescending(p => p.CropAreaSqFeet).ThenBy(p => p.PlanName ?? p.DisplayName, System.StringComparer.Ordinal).First();
                    step = 0; why = $"步驟0 底圖＝相機放置時的平面「{src.Name}」未啟用裁剪 → 改用其父／從屬視圖（候選 {related.Count} 張）";
                    return b;
                }
                step0Note = $"（放置平面「{src.Name}」{(src.Cropped ? "不在清單" : "未啟用裁剪且無可用父／從屬視圖")}）";
            }
            if (levelId == null) { why = "no level" + step0Note; return null; }
            long lv = levelId.GetIdValue();

            // 相機樓層高程：先查樓層清單，再查該層平面帶的 GenLevel 高程。
            double? camLevelElev = levelsByElevation?.FirstOrDefault(l => l?.LevelId != null && l.LevelId.GetIdValue() == lv)?.ProjectElevationFeet;
            if (!camLevelElev.HasValue)
                camLevelElev = plans.FirstOrDefault(p => OnLevel(p, lv) && p.GenLevelElevationFeet.HasValue)?.GenLevelElevationFeet;

            if (eyeList.Count > 0)
            {
                // 步驟 1
                List<PlanViewOption> s1 = plans.Where(p => p.IsFloorPlan && OnLevel(p, lv) && Contained(p) > 0).ToList();
                if (s1.Count > 0)
                {
                    PlanViewOption b = s1.OrderByDescending(Contained).ThenBy(p => p.IsDependent ? 1 : 0)
                        .ThenByDescending(p => p.CropAreaSqFeet).ThenBy(p => p.PlanName ?? p.DisplayName, System.StringComparer.Ordinal).First();
                    step = 1; why = $"步驟1 同層包含相機：{Tag(b, Contained(b))}、非從屬優先、面積最大（候選 {s1.Count} 張）" + step0Note;
                    return b;
                }
                // 步驟 2
                if (camLevelElev.HasValue)
                {
                    double ce = camLevelElev.Value;
                    List<PlanViewOption> s2 = plans.Where(p => p.IsFloorPlan && !OnLevel(p, lv) && p.GenLevelElevationFeet.HasValue &&
                        System.Math.Abs(p.GenLevelElevationFeet.Value - ce) <= NearElevationToleranceFeet + 1e-9 && Contained(p) > 0).ToList();
                    if (s2.Count > 0)
                    {
                        PlanViewOption b = s2.OrderByDescending(Contained).ThenBy(p => System.Math.Abs(p.GenLevelElevationFeet.Value - ce))
                            .ThenByDescending(p => p.CropAreaSqFeet).ThenBy(p => p.PlanName ?? p.DisplayName, System.StringComparer.Ordinal).First();
                        double dMm = CameraMath.FeetToMm(b.GenLevelElevationFeet.Value - ce);
                        step = 2; why = $"步驟2 同層無平面包含相機 → 高程相近樓層「{b.LevelName ?? "?"}」（{dMm:+0;-0;0} mm）：{Tag(b, Contained(b))}、面積最大（候選 {s2.Count} 張）" + step0Note;
                        return b;
                    }
                }
                // 步驟 3（眼睛高程取多台相機中最低者，保守）
                List<double> zs = eyeList.Where(e => e.Length >= 3).Select(e => e[2]).ToList();
                if (zs.Count > 0)
                {
                    double eyeZ = zs.Min();
                    List<PlanViewOption> s3 = plans.Where(p => p.IsFloorPlan && p.GenLevelIsStorey && p.GenLevelElevationFeet.HasValue &&
                        p.GenLevelElevationFeet.Value <= eyeZ + 1e-9 && Contained(p) > 0).ToList();
                    if (s3.Count > 0)
                    {
                        PlanViewOption b = s3.OrderByDescending(Contained).ThenByDescending(p => p.GenLevelElevationFeet.Value)
                            .ThenByDescending(p => p.CropAreaSqFeet).ThenBy(p => p.PlanName ?? p.DisplayName, System.StringComparer.Ordinal).First();
                        step = 3; why = $"步驟3 ±1.5 m 內無平面包含相機 → 眼睛下方最高樓層「{b.LevelName ?? "?"}」：{Tag(b, Contained(b))}、面積最大（候選 {s3.Count} 張）" + step0Note;
                        return b;
                    }
                }
            }

            // 步驟 4：舊規則（同層最大面積），再上下樓層退回。
            PlanViewOption Largest(long id, out int poolCount)
            {
                List<PlanViewOption> ofLevel = plans.Where(p => OnLevel(p, id)).ToList();
                List<PlanViewOption> pool = ofLevel.Where(p => p.IsFloorPlan).ToList();
                if (pool.Count == 0) pool = ofLevel;
                poolCount = pool.Count;
                return pool.OrderByDescending(Contained).ThenBy(p => p.IsDependent ? 1 : 0).ThenByDescending(p => p.CropAreaSqFeet)
                    .ThenBy(p => p.PlanName ?? p.DisplayName, System.StringComparer.Ordinal).FirstOrDefault();
            }
            string noEye = eyeList.Count == 0 ? "沒有相機座標" : "沒有任何平面包含相機";
            PlanViewOption own = Largest(lv, out int n4);
            if (own != null)
            {
                step = 4; why = $"步驟4 {noEye} → 同層最大面積（同層候選 {n4} 張）" + step0Note;
                return own;
            }
            if (levelsByElevation != null)
            {
                int idx = -1;
                for (int i = 0; i < levelsByElevation.Count; i++)
                    if (levelsByElevation[i]?.LevelId != null && levelsByElevation[i].LevelId.GetIdValue() == lv) { idx = i; break; }
                if (idx >= 0)
                {
                    for (int i = idx - 1; i >= 0; i--)
                    {
                        LevelOption o = levelsByElevation[i];
                        PlanViewOption p = o?.LevelId != null ? Largest(o.LevelId.GetIdValue(), out int n) : null;
                        if (p != null) { step = 4; why = $"步驟4 {noEye}、同層沒有已裁剪平面 → 最近下方樓層「{o.Name}」最大面積" + step0Note; return p; }
                    }
                    for (int i = idx + 1; i < levelsByElevation.Count; i++)
                    {
                        LevelOption o = levelsByElevation[i];
                        PlanViewOption p = o?.LevelId != null ? Largest(o.LevelId.GetIdValue(), out int n) : null;
                        if (p != null) { step = 4; why = $"步驟4 {noEye}、同層與下方都沒有已裁剪平面 → 最近上方樓層「{o.Name}」最大面積" + step0Note; return p; }
                    }
                }
            }
            why = $"{noEye}，且該樓層（與上下樓層）沒有已裁剪平面" + step0Note;
            return null;
        }
    }

    /// <summary>§4.8 步驟 0 的輸入：相機放置時的平面（由 CameraRow 的 SourcePlan* 欄位組成）。純資料。</summary>
    internal class SourcePlanHint
    {
        public ElementId ViewId { get; set; }
        public string Name { get; set; }
        public bool Cropped { get; set; }
        public ElementId PrimaryId { get; set; }

        public static SourcePlanHint From(CameraRow r) => r?.SourcePlanId == null ? null
            : new SourcePlanHint { ViewId = r.SourcePlanId, Name = r.SourcePlanName, Cropped = r.SourcePlanCropped, PrimaryId = r.SourcePlanPrimaryId };

        /// <summary>多台相機（預設底圖、底圖清單每層一張）：取最多台相機共用的放置平面；都沒有記錄＝null。</summary>
        public static SourcePlanHint MostCommon(IEnumerable<CameraRow> rows)
        {
            CameraRow r = rows?.Where(x => x?.SourcePlanId != null)
                .GroupBy(x => (long)x.SourcePlanId.GetIdValue())
                .OrderByDescending(g => g.Count()).ThenBy(g => g.Key)
                .Select(g => g.First()).FirstOrDefault();
            return From(r);
        }
    }

    /// <summary>ComboBox 裡的一個樓層選項（細節編輯「所在樓層」、複製相機到其他樓層共用）。</summary>
    internal class LevelOption
    {
        public ElementId LevelId { get; set; }
        public string Name { get; set; }
        public double ProjectElevationFeet { get; set; }

        public override string ToString() => Name;
    }

    /// <summary>「重新讀取相機清單＋平面視圖清單＋樓層清單」這一步驟（在 ExternalEvent 內執行）的結果。</summary>
    /// <summary>2026-09-25 Wave 3：圖框類型（攤平成純資料給 WPF）。</summary>
    internal class TitleBlockOption
    {
        public long Id { get; set; }
        public string Name { get; set; }
        public int UseCount { get; set; }
    }

    internal class CameraManagerSnapshot
    {
        public List<CameraRow> Cameras { get; set; } = new List<CameraRow>();
        public List<PlanViewOption> PlanViews { get; set; } = new List<PlanViewOption>();
        public List<LevelOption> Levels { get; set; } = new List<LevelOption>();
        public ElementId DefaultPlanViewId { get; set; }
        /// <summary>目前作用中視圖的 Id（由呼叫端在 ExternalEvent 內另外補上，ReadSnapshot 本身不填）；
        /// 供面板「預設選取：作用中視圖是透視圖就選它，否則選第一列」使用。</summary>
        public ElementId ActiveViewId { get; set; }
        /// <summary>目前文件內是否存在任何「已啟用 CropBox」的平面視圖；false 時底圖改用中性格線（4.8）。</summary>
        public bool HasCroppedPlan { get; set; }
        /// <summary>2026-09-25 Wave 3：專案內的圖框類型（④ ▾ 圖紙圖框 ▸ 下拉用；UseCount＝已放在圖紙上的次數）。</summary>
        public List<TitleBlockOption> TitleBlocks { get; set; } = new List<TitleBlockOption>();
        public string ErrorMessage { get; set; }
        public string DocumentTitle { get; set; }
    }

    /// <summary>「匯出底圖 + 量出裁剪範圍」這一步驟（在 ExternalEvent 內執行）的結果。</summary>
    internal class BaseMapSnapshot
    {
        public string ImageFilePath { get; set; }
        public string TempDirectory { get; set; }

        /// <summary>CropBox 在視圖自身座標系（feet）的範圍；配合下方攤平的原點／基底可還原成模型座標。</summary>
        public double CropMinXFeet { get; set; }
        public double CropMinYFeet { get; set; }
        public double CropMaxXFeet { get; set; }
        public double CropMaxYFeet { get; set; }

        /// <summary>2026-09-24 對位改版：匯出圖片實際像素（從 PNG 檔頭讀出，0＝未知）、純模型裁剪框
        /// 範圍（不含註記裁剪外擴，供「顯示對位框」除錯疊圖）、以及換算範圍的來源說明。</summary>
        public int ImagePixelWidth { get; set; }
        public int ImagePixelHeight { get; set; }
        public double ModelCropMinXFeet { get; set; }
        public double ModelCropMinYFeet { get; set; }
        public double ModelCropMaxXFeet { get; set; }
        public double ModelCropMaxYFeet { get; set; }
        public string ExtentsSource { get; set; }
        // 2026-09-24 第二輪崩潰調查：原本這裡直接存 Revit 的 Transform（APIObject，IDisposable、
        // 背後是原生物件），再拿到 WPF 執行緒上呼叫 .Inverse／.OfPoint——等於在 API context 以外
        // 操作 Revit 原生物件，而且物件的生命週期交給 GC。改成在 ExportBaseMap（API context 內）
        // 就攤平成純 double：原點＋三個基底向量。CropBox.Transform 是剛體轉換（正交基底），反轉
        // 換算＝轉置，見 ModelToLocal／ModelVectorToLocal。XYZ／ElementId 經反射確認是純受控
        // 類別（非 APIObject、非 IDisposable、無 finalizer），可安全留在快照裡。
        public double OriginX { get; set; }
        public double OriginY { get; set; }
        public double OriginZ { get; set; }
        public double BasisXX { get; set; } = 1;
        public double BasisXY { get; set; }
        public double BasisXZ { get; set; }
        public double BasisYX { get; set; }
        public double BasisYY { get; set; } = 1;
        public double BasisYZ { get; set; }
        public double BasisZX { get; set; }
        public double BasisZY { get; set; }
        public double BasisZZ { get; set; } = 1;

        /// <summary>模型座標點 → 視圖自身座標（只取 X/Y，平面用）。純算術，任何執行緒皆可呼叫。</summary>
        public void ModelToLocal(double x, double y, double z, out double localX, out double localY)
        {
            double dx = x - OriginX, dy = y - OriginY, dz = z - OriginZ;
            localX = dx * BasisXX + dy * BasisXY + dz * BasisXZ;
            localY = dx * BasisYX + dy * BasisYY + dz * BasisYZ;
        }

        /// <summary>2026-09-26 第四輪：視圖自身座標 (X,Y) → 模型座標 XY（ModelToLocal 的反轉；剛體轉換，基底正交）。地圖拖曳用。</summary>
        public void LocalToModel(double localX, double localY, out double x, out double y)
        {
            x = OriginX + localX * BasisXX + localY * BasisYX;
            y = OriginY + localX * BasisXY + localY * BasisYY;
        }

        /// <summary>2026-09-26 第四輪：視圖自身座標向量 → 模型座標向量（XY）。</summary>
        public void LocalVectorToModel(double localX, double localY, out double x, out double y)
        {
            x = localX * BasisXX + localY * BasisYX;
            y = localX * BasisXY + localY * BasisYY;
        }

        /// <summary>模型座標向量 → 視圖自身座標（只取 X/Y）。</summary>
        public void ModelVectorToLocal(double x, double y, double z, out double localX, out double localY)
        {
            localX = x * BasisXX + y * BasisXY + z * BasisXZ;
            localY = x * BasisYX + y * BasisYY + z * BasisYZ;
        }
        /// <summary>底圖平面視圖所在的樓層 Id（找不到時為 null）；供迷你 key plan 篩選「只畫同樓層相機」。</summary>
        public ElementId GenLevelId { get; set; }

        public string ErrorMessage { get; set; }
        /// <summary>2026-09-26 第四輪：底圖是本工具自己的視圖（Key Plan／樓層總覽）——點陣快取另外比對 OwnViewsEpoch。</summary>
        public bool IsOwnView { get; set; }
        /// <summary>非致命提醒（例如底圖平面未啟用 CropBox，疊圖可能不準），匯出仍會繼續。</summary>
        public string Warning { get; set; }
    }

    /// <summary>細節編輯欄一次送出的內容（4.3）。哪個欄位有變更由呼叫端自行比對再決定要不要送。</summary>
    internal class CameraEditRequest
    {
        public ElementId ViewId { get; set; }
        public double EyeXFeet { get; set; }
        public double EyeYFeet { get; set; }
        public ElementId LevelId { get; set; }
        public double HeightAboveLevelCm { get; set; }
        public double HorizontalAngleDeg { get; set; }
        public double PitchDeg { get; set; }
        /// <summary>null = 不變更遠裁剪。</summary>
        public double? FarClipCm { get; set; }
    }

    /// <summary>
    /// 2026-09-26 第六輪：編輯快速路徑的請求——只帶「使用者這次改的欄位」（null＝不動，保留相機目前實際值）。
    /// 與 <see cref="CameraEditRequest"/> 不同：不從清單列補齊其他欄位，所以清單列即使是舊資料也不會把別的欄位改回去。
    /// </summary>
    internal class CameraPoseEdit
    {
        public ElementId ViewId { get; set; }
        /// <summary>離樓板高度的基準樓層（清單列目前樓層）；只有 HeightAboveLevelCm 有值時需要。</summary>
        public ElementId LevelId { get; set; }
        public double? HeightAboveLevelCm { get; set; }
        public double? YawDeg { get; set; }
        public double? PitchDeg { get; set; }
        public double? EyeXFeet { get; set; }
        public double? EyeYFeet { get; set; }
        public double? FarClipCm { get; set; }

        /// <summary>log 用的欄位名稱（「高度」「水平角」…）；多欄時以「＋」連接。</summary>
        public string What()
        {
            var parts = new List<string>();
            if (HeightAboveLevelCm.HasValue) parts.Add("高度");
            if (YawDeg.HasValue) parts.Add("水平角");
            if (PitchDeg.HasValue) parts.Add("俯仰角");
            if (EyeXFeet.HasValue || EyeYFeet.HasValue) parts.Add("平面位置");
            if (FarClipCm.HasValue) parts.Add("遠裁剪");
            return parts.Count > 0 ? string.Join("＋", parts) : "相機";
        }
    }

    /// <summary>2026-09-26 第六輪：編輯快速路徑的結果（計時＋套用後的實際值，供清單就地更新／MCP 回傳）。</summary>
    internal class CameraPoseEditResult
    {
        public string Error { get; set; }
        public string CameraName { get; set; }
        public long QueueWaitMs { get; set; }
        public long TransactionMs { get; set; }
        public long RefreshMs { get; set; }
        public long TotalMs { get; set; }
        public string RefreshHow { get; set; }
        public bool SavedOriginalVersion { get; set; }
        /// <summary>姿態真的有變（眼睛 0.1 mm／方向 1e-6／遠裁剪 0.1 mm 以上）。</summary>
        public bool Changed { get; set; }
        public double EyeXFeet { get; set; }
        public double EyeYFeet { get; set; }
        public double EyeZFeet { get; set; }
        public double ForwardX { get; set; }
        public double ForwardY { get; set; }
        public double ForwardZ { get; set; }
        public double? HeightAboveLevelCm { get; set; }
        public double YawDeg { get; set; }
        public double PitchDeg { get; set; }
        public double? FarClipCm { get; set; }
    }

    /// <summary>
    /// 等角 Key Plan 視圖的範圍模式（規格 Change 2「工具」區的小 ComboBox）：
    ///   FullWithUpperBeams（預設）：頂面延伸到最近上層樓板底再退 10mm（含上層樑，找不到樓板時
    ///     退回下一樓層高程、再沒有就 eye 高度+3m）。
    ///   CurrentLevelTopOnly：頂面直接停在下一樓層高程（沒有下一樓層時同上退回 eye+3m），
    ///     不特別找上層樓板/樑。
    /// 兩種模式的 Z 下限（往下延伸支撐樑）規則相同，差異只在頂面怎麼決定。
    /// </summary>
    public enum CameraIsoExtentMode
    {
        FullWithUpperBeams = 0,
        CurrentLevelTopOnly = 1
    }

    /// <summary>
    /// 2026-09-26（使用者要求，D-038；同日實測後改預設，D-039）：等角 Key Plan 視圖「觀看者側」兩面（室內相機）。
    ///   Camera（預設，數值 0）＝近側兩面剛好放在眼睛的 X／Y（不留後退距離，不會誤切到牆後面的東西）；
    ///   Keep（1）＝不切（四面都是房間 +0.6 m，牆保留）；
    ///   Wall（2）＝D-038 舊規則：近側兩面切到房間邊界往內 100 mm。
    /// 設定檔存整數：舊版預設 Cut＝0 讀進來會變成 Camera（即新預設），Keep 不變。室外相機不受影響。
    /// </summary>
    public enum CameraIsoNearWallsMode
    {
        Camera = 0,
        Keep = 1,
        Wall = 2
    }

    /// <summary>
    /// 2026-09-25（使用者拍板）：Key Plan 剖面的方向。
    ///   AlongRoomLongAxis（預設，選項 C「對齊房間長邊」）：剖面沿眼睛所在房間的長軸、通過眼睛，
    ///     水平範圍＝房間沿該軸 +0.6m；找不到房間時退回 AlongSightLine。
    ///   AlongSightLine（選項 A「沿視線（側視）」）：舊規則，剖面平面包含水平視線。
    /// </summary>
    public enum CameraSectionOrientationMode
    {
        AlongRoomLongAxis = 0,
        AlongSightLine = 1
    }

    /// <summary>
    /// 2026-09-26 第五輪（使用者）：Key Plan 剖面「位置」（剖切面通過哪一點；方向仍由 <see cref="CameraSectionOrientationMode"/> 決定）。
    ///   Camera（預設）＝剖切面通過相機眼睛；深度＝眼睛（深度 0，剖切面）往視圖看向側延伸扇形半徑。
    ///   Center＝舊行為（對齊房間長邊：畫面中心線上半個扇形半徑處；沿視線：眼睛、深度前後各 1.5 m）。
    /// </summary>
    public enum CameraSectionAnchorMode
    {
        Camera = 0,
        Center = 1
    }

    /// <summary>
    /// 2026-09-25（Wave 2）：相機標記型式（⋯ 更多 → 相機標記）。
    ///   B（預設）＝Key Plan 平面／剖面／樓層總覽上的詳圖標記（只在該視圖、可列印、不進算圖）；
    ///   A＝模型族群「RevitMCP 相機標記」（平面／剖面／3D 都看得到，透視圖一律隱藏子類別「相機標記」）＋視野扇形（DirectShape）；
    ///   AB＝兩者都畫。
    /// </summary>
    public enum CameraMarkerMode
    {
        B = 0,
        A = 1,
        AB = 2
    }

    /// <summary>人視高預設值等，存在外掛設定檔（本機）。public：<see cref="RevitMCP.Configuration.ServiceSettings.CameraManager"/>
    /// 是 public 屬性，屬性型別的存取範圍不能比屬性窄（CS0053）。</summary>
    public class CameraManagerSettings
    {
        public double DefaultHeightAboveLevelCm { get; set; } = 160.0;

        /// <summary>相機分頁「清單／地圖」GridSplitter 位置：清單（DataGrid）佔可用高度的比例
        /// （0～1），地圖佔剩下的比例。拖曳 GridSplitter 後即時存檔。2026-09-24 拿掉「選取中
        /// 相機」卡片後，預設把騰出來的空間讓給地圖，預設值由 0.45 下修為 0.35（只影響新安裝，
        /// 已存過設定的使用者維持自己拖過的值）。</summary>
        public double CameraTabListFraction { get; set; } = 0.35;

        /// <summary>2026-09-24 第四輪版面（位置圖佔下半、⑤ 收進選單）：② 相機清單佔「清單＋位置圖」可用高度的
        /// 比例，預設 1/3。換新鍵名是因為版面整個重排，舊的 <see cref="CameraTabListFraction"/>（舊版面拖出的值）
        /// 不再適用；舊鍵保留只為相容既有設定檔。</summary>
        public double CameraListFractionV3 { get; set; } = 1.0 / 3.0;

        /// <summary>等角 Key Plan 視圖的範圍模式，見 <see cref="CameraIsoExtentMode"/>。</summary>
        public CameraIsoExtentMode IsoExtentMode { get; set; } = CameraIsoExtentMode.FullWithUpperBeams;

        /// <summary>2026-09-26：等角近側，見 <see cref="CameraIsoNearWallsMode"/>（預設＝相機位置，D-039）。</summary>
        public CameraIsoNearWallsMode IsoNearWalls { get; set; } = CameraIsoNearWallsMode.Camera;

        /// <summary>Key Plan 剖面方向，見 <see cref="CameraSectionOrientationMode"/>（預設對齊房間長邊）。</summary>
        public CameraSectionOrientationMode SectionOrientationMode { get; set; } = CameraSectionOrientationMode.AlongRoomLongAxis;

        /// <summary>2026-09-26 第五輪：Key Plan 剖面位置，見 <see cref="CameraSectionAnchorMode"/>（預設＝相機位置）。</summary>
        public CameraSectionAnchorMode SectionAnchor { get; set; } = CameraSectionAnchorMode.Camera;

        /// <summary>2026-09-24：Key Plan 平面／剖面相機標記線（線型「相機標記線」）與箭頭填滿顏色，
        /// 十六進位 #RRGGBB；預設藍 #3B82C4（面板 accent 色）。</summary>
        public string MarkerColorHex { get; set; } = "#3B82C4";

        /// <summary>2026-09-25：Key Plan 平面／剖面的表現法。true＝「淡化背景」（預設：模型半色調、
        /// 隱藏雜項註解類別、房間標籤半色調、自動挑 1:50/100/200 比例）；false＝「原始」（下次一鍵
        /// 4 視圖時還原 Display Model 為一般、取消隱藏這些類別、清掉房間標籤半色調）。</summary>
        public bool KeyPlanFadedStyle { get; set; } = true;

        /// <summary>2026-09-25（Wave 2）：相機標記型式，見 <see cref="CameraMarkerMode"/>；預設 B（詳圖）。</summary>
        public CameraMarkerMode MarkerMode { get; set; } = CameraMarkerMode.B;

        /// <summary>2026-09-25：室外相機（眼睛不在任何房間內）的扇形／Key Plan 範圍半徑（m），實際半徑＝
        /// min(遠裁剪, 本值)。「一鍵 4 視圖 ▾ → 室外範圍半徑…」可改；有效範圍 5～1000 m（超出以預設 60 m 處理）。</summary>
        public double OutdoorRadiusM { get; set; } = DefaultOutdoorRadiusM;

        public const double DefaultOutdoorRadiusM = 60.0;
        public const double MinOutdoorRadiusM = 5.0;
        public const double MaxOutdoorRadiusM = 1000.0;

        /// <summary>讀取時防呆：非有限值或超出 [5, 1000] m → 預設 60 m。</summary>
        public double GetEffectiveOutdoorRadiusM()
        {
            double v = OutdoorRadiusM;
            return (double.IsNaN(v) || double.IsInfinity(v) || v < MinOutdoorRadiusM || v > MaxOutdoorRadiusM) ? DefaultOutdoorRadiusM : v;
        }

        // ── DataGrid 選用欄位（右鍵標題列勾選，本機記憶，預設全部關閉） ──
        public bool ShowColumnPlaneX { get; set; } = false;
        public bool ShowColumnPlaneY { get; set; } = false;
        public bool ShowColumnFarClip { get; set; } = false;
        public bool ShowColumnFov { get; set; } = false;
        public bool ShowColumnEyeElevation { get; set; } = false;

        /// <summary>迷你 key plan 是否顯示其他樓層的相機（灰色淡化＋「(樓層)」字尾）。預設關閉，
        /// 只畫底圖所在樓層的相機（見 2026-09-24 使用者實測回報：不同樓層相機混在一張底圖上
        /// 位置容易誤判）。</summary>
        public bool ShowOtherLevelCamerasOnMap { get; set; } = false;
        /// <summary>2026-09-25：③ 底圖清單「顯示全部平面」。false（預設）＝只列有相機的樓層、每層一張偏好平面。</summary>
        public bool ShowAllPlansInBaseMap { get; set; } = false;

        /// <summary>2026-09-25 Wave 3：一鍵出圖紙使用的圖框類型 ElementId（0＝自動：專案內最常用的圖框）。</summary>
        public long SheetTitleBlockTypeId { get; set; } = 0;
        /// <summary>2026-09-25 Wave 3：一鍵出圖紙時，圖框外接框向內留的邊界（mm）。</summary>
        public double SheetMarginMm { get; set; } = 15.0;
        /// <summary>2026-09-25 第二輪：一鍵出圖紙偵測不到圖框標題欄線時，橫式圖紙右側保留給標題欄的寬度比例（0–0.5，預設 0.20）。</summary>
        public double SheetReservedStripFraction { get; set; } = 0.20;
    }
}
