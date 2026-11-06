using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using Autodesk.Revit.UI;
using RevitMCP.Configuration;

namespace RevitMCP.Core
{
    /// <summary>
    /// 「一鍵 4 視圖」（規格書 §5.1 平面／剖面／等角 ＋ §5.3 B 型註解標記，第一版）。
    ///
    /// 設計取捨（第一次交付，未經 Revit 實機驗證，僅保證可編譯、邏輯自洽）：
    ///   1. 三個 Key Plan 視圖與相機的對應關係，用 Extensible Storage 記在「視圖元素本身」
    ///      上（欄位：CameraUniqueId + Role），不是用名稱猜——這樣使用者事後改相機名稱、
    ///      Key Plan 視圖不會變成孤兒，重新整理／改名都抓得到同一個視圖，不會生出重複視圖。
    ///      找不到 ES 連結時退回用「精確名稱比對」找既有視圖（同樣視為既有、更新而非新建），
    ///      兩者都找不到才新建。
    ///   2. 標記（B 型註解，只在平面／剖面）用同一個 Schema、不同 Role（MarkerPlan／
    ///      MarkerSection）貼在畫出來的 DetailCurve 上；每次執行先刪除同一台相機、同一
    ///      Role 的舊標記元素再重畫，不會愈跑愈多。
    ///   3. 版面尺寸（margin、剖面深度、遠裁剪預設值…）是有註記依據的假設值，不是查證過
    ///      的規格——都寫在下面對應的常數旁的註解。
    /// </summary>
    internal static partial class CameraKeyPlanService
    {
        // ── 假設值（未經使用者驗收，之後可視需要調整） ──
        private const double PlanMarginCm = 200.0;           // 平面裁剪框在相機/視野範圍外再留的邊界
        private const double SectionBackMarginCm = 100.0;    // 剖面：相機背後留一點，不要正好切在相機位置
        private const double SectionForwardMarginCm = 100.0; // 剖面：遠裁剪之外再留一點
        private const double SectionDepthHalfCm = 150.0;     // 剖面「厚度」：沿視線垂直方向前後各留 1.5m
        private const double SectionBelowLevelCm = 100.0;    // 剖面垂直下限：樓層下方 1m（規格 5.1：−1m）
        private const double SectionAboveEyeCm = 300.0;      // 剖面垂直上限：眼睛上方 3m（規格 5.1：+3m）
        private const double DefaultFarClipCm = 2000.0;      // 相機沒有可用遠裁剪值時的預設（20m，未查證，僅供第一版有值可用）
        private const double DefaultFovDeg = 60.0;           // 沒有 FOV 估算值時的預設半視角基準
        // 等角 Key Plan 視圖：方向＝相機水平前方向往下俯 35.26°（2026-09-25 起，取代固定 TOP-BACK-LEFT），
        // 範圍＝眼睛所在房間 +0.6m ＋依樓層/結構延伸的 SectionBox，見下方 BuildIsoOrientation／BuildIsoSectionBox。
        private const double IsoFarClipCapMm = 30000.0;        // XY 遠裁剪扇形的上限（30m），避免超大遠裁剪把等角範圍拉到整個基地
        private const double IsoSupportingBeamToleranceMm = 300.0; // 沿用 set_3d_section_box 的支撐樑容差預設值
        private const double IsoUpperSlabClearanceMm = 10.0;   // 規格：上層樓板底再退 10mm
        private const double IsoMinBackoffCm = 500.0;          // 等角視角眼睛位置至少退後 5m，確保落在 SectionBox 外
        private const double DefaultSlabThicknessMm = 300.0;   // 找不到上層樓板、且上一樓層沒有任何樓板可取厚度時的假設樓板厚
        private const double SectionAboveSlabCm = 50.0;        // 剖面垂直上限：上層樓板底 +0.5m（規格 5.1 修訂）
        // 2026-09-24 第四輪（等角「看得到的房間」＋樓板下限，協調者轉述使用者回報）：
        private const double IsoFloorBelowClearanceMm = 50.0;  // 等角 Z 下限：本層樓板底再往下 50mm
        private const double IsoRoomMarginMm = 500.0;
        private const double IsoNearWallInsetMm = 100.0;       // 2026-09-26 等角近側牆：房間邊界往內 100mm（切掉牆）          // 房間聯集外擴 0.5m
        private const double IsoRoomRayStepMm = 500.0;         // 視線取樣間距 0.5m
        private const double IsoRoomRayMaxMm = 15000.0;        // 視線取樣最遠 15m（再與遠裁剪取小）
        private const double IsoRoomRayGapStopMm = 1000.0;     // 連續離開所有房間超過 1m 就停這條射線
        private const double IsoNoRoomFanCapMm = 10000.0;      // （舊）找不到房間時扇形框遠端夾到 10m——2026-09-25 起改用 NoRoomFanCapMm
        // 2026-09-25 第五輪（使用者：「以我們這間房間為主，往外留一些 buffer」）：只用「眼睛所在房間」。
        private const double RoomBufferMm = 600.0;             // 房間外擴 0.6m（等角 XY、平面裁剪、剖面水平範圍、扇形半徑）
        private const double NoRoomFanCapMm = 8000.0;          // 室內但算不出視線離開房間距離：扇形距離夾到 8m
        private const double OutdoorPlanMarginMm = 2000.0;     // 室外：平面裁剪／等角 XY＝扇形外接框 +2m
        private const double OutdoorBehindEyeMm = 2000.0;      // 室外：剖面在眼睛後方留 2m
        // 2026-09-26 第七輪（Snowdon Towers「Exterior Street View」Key Plan 平面看不到剖面線：室外剖面沿線 −2m…+60m，
        // 遠端標頭超出平面裁剪框、近端標頭被眼睛楔形蓋住）：剖面沿線範圍兩端各截到平面裁剪框內縮 1m，確保標頭落在框內。
        private const double SectionLineCropInsetMm = 1000.0;
        private const double SightCenterlineSkipAngleDeg = 3.0; // 剖面方向與畫面中心線夾角 < 此值 → 平面上不重畫紅色視線中心線（剖面線已代表它）
        // 2026-09-27（MCP 實測，D-044 修正②）：剖面「看向」（−BasisZ）與相機水平視線夾角 < 此值 → 剖面側視垂直
        // 扇形（DrawSectionMarker）會誤導（等於在剖面裡沿著視線看下去，不是真的側視），只畫眼睛圓圈。判斷用幾何
        // （夾角），不看是不是剖面轉 90°，任何組合只要剖面恰好看向視線都適用。
        private const double SectionFanSuppressAlongSightDeg = 45.0;
        // 2026-09-28（使用者決策，D-046：相機站在門邊，沿視線看的剖面卡在牆上）：跳過眼前牆的射線參數。
        private const double WallSkipTightRangeMm = 1500.0;          // 只認「眼睛前方 1.5m 內」的命中；更遠的牆是場景一部分，不移剖切面
        private const double WallSkipThicknessSanityCapMm = 1500.0;  // 找同一元素「背面」命中時的合理厚度上限（近端 + 這個值），避免抓到後面另一片牆
        private const double WallSkipClearanceMm = 100.0;            // 剖切面落在元素背面再往前 100mm
        private const int SectionViewerLineWeight = 6;         // 本相機剖面 viewer 在 Key Plan 平面上的投影線寬（深灰、虛線，不跟淡化走）
        private const double OutdoorBelowLevelMm = 1000.0;     // 室外：Z 下限＝min(下方樓層, 最低樓層) −1m
        private const double OutdoorAboveModelMm = 2000.0;     // 室外：Z 上限＝XY 範圍內模型最高點 +2m
        private const double OutdoorModelScanZFeet = 10000.0;  // 室外：找模型最高點時 Outline 的 Z 範圍（±10000 ft，等同不限）
        private const double SectionLevelMinGapMm = 1000.0;    // 剖面樓層標頭：上一層須與下方樓層高差 ≥ 1.0m（近重複樓層不算）
        private const double FanMinRadiusMm = 1500.0;          // 扇形半徑下限 1.5m
        private const double RoomTopIsoBufferMm = 300.0;       // 等角頂＝房間頂 +0.3m（再與上層樓板底 −10mm 取小）
        private const double RoomTopSectionBufferMm = 500.0;   // 剖面頂＝房間頂 +0.5m
        private const double RoomTopSlabToleranceMm = 100.0;   // 上層樓板判定：底面（或頂面）≥ 房間頂 −0.1m
        private const double IsoElevationDeg = 35.26;          // 等角俯角（標準等角 atan(1/√2)）

        /// <summary>「更新相機標記」交易名稱：Application.cs 的 DocumentChanged 用它（加上
        /// <see cref="IsUpdatingMarkers"/>）辨識「我們自己的標記重畫」，避免回饋迴圈。</summary>
        internal const string MarkerUpdateTransactionName = "更新相機標記";
        internal const string KeyPlanTransactionName = "建立相機 Key Plan";
        internal const string MarkerColorTransactionName = "變更相機標記顏色";
        /// <summary>2026-09-27：單一相機「剖面轉 90°」開關的交易名稱。</summary>
        internal const string SectionRotate90TransactionName = "剖面轉90°";
        /// <summary>標記重畫交易進行中（含 Commit 當下觸發的 DocumentChanged）。</summary>
        internal static bool IsUpdatingMarkers { get; private set; }
        // MarkerCrossRadiusCm／MarkerArrowLenCm（模型空間固定 cm）已被下面的「列印比例固定」
        // 常數取代，見 DrawPlanMarker／DrawSectionMarker 上方的 MarkerCirclePrintRadiusMm 等。

        private static double Cm(double cm) => CameraMath.MmToFeet(cm * 10.0);

        // ══════════════════════════════════════════════════════════
        // Extensible Storage：相機 ↔ Key Plan 視圖／標記 的連結
        // ══════════════════════════════════════════════════════════

        // 固定 GUID，不要改——已經寫進使用者 .rvt 檔的 Schema 綁定靠這個值辨識。
        private static readonly Guid SchemaGuid = new Guid("6C9E2F0A-6E2C-4E7C-9C8B-2F6E9B0F1A2D");
        private const string FieldCameraUniqueId = "CameraUniqueId";
        private const string FieldRole = "Role";

        private static Schema GetOrCreateSchema()
        {
            Schema schema = Schema.Lookup(SchemaGuid);
            if (schema != null) return schema;

            SchemaBuilder builder = new SchemaBuilder(SchemaGuid);
            builder.SetSchemaName("RevitMCP_CameraKeyPlanLink");
            builder.SetReadAccessLevel(AccessLevel.Public);
            builder.SetWriteAccessLevel(AccessLevel.Public);
            builder.AddSimpleField(FieldCameraUniqueId, typeof(string));
            builder.AddSimpleField(FieldRole, typeof(string));
            return builder.Finish();
        }

        private static void TagLink(Element el, string cameraUniqueId, string role)
        {
            Schema schema = GetOrCreateSchema();
            Entity entity = new Entity(schema);
            entity.Set(schema.GetField(FieldCameraUniqueId), cameraUniqueId ?? "");
            entity.Set(schema.GetField(FieldRole), role ?? "");
            el.SetEntity(entity);
        }

        private static bool TryReadLink(Element el, out string cameraUniqueId, out string role)
        {
            cameraUniqueId = null;
            role = null;
            Schema schema = Schema.Lookup(SchemaGuid);
            if (schema == null) return false;
            Entity entity = el.GetEntity(schema);
            if (entity == null || !entity.IsValid()) return false;
            cameraUniqueId = entity.Get<string>(schema.GetField(FieldCameraUniqueId));
            role = entity.Get<string>(schema.GetField(FieldRole));
            return true;
        }

        private static View FindLinkedView(Document doc, string cameraUniqueId, string role)
        {
            // 2026-09-26 第四輪：只看帶本 Schema 的視圖（ExtensibleStorageFilter），不逐一讀全部視圖的 ES。
            if (Schema.Lookup(SchemaGuid) == null) return null;
            foreach (View v in new FilteredElementCollector(doc).OfClass(typeof(View)).WherePasses(new ExtensibleStorageFilter(SchemaGuid)).Cast<View>())
            {
                if (v.IsTemplate) continue;
                if (TryReadLink(v, out string cid, out string r) && cid == cameraUniqueId && r == role)
                    return v;
            }
            return null;
        }

        // ══════════════════════════════════════════════════════════
        // 2026-09-24 追加：剖面「使用者是否手動調整過」狀態（第二個 Schema，不動上面固定 GUID
        // 的 CameraKeyPlanLink——ES Schema 一旦建立即不可再增減欄位，同一個 GUID 不能改欄位
        // 定義，否則會弄壞已經寫進使用者 .rvt 的既有連結）。記的是「上次程式自動產生剖面時」
        // 的視線原點/水平方向；下次執行時比對剖面目前實際的 CropBox.Transform，若偏差在容差
        // 內＝使用者沒動過（或這正是我們剛寫入的），照常更新；偏差超出容差＝使用者手動調整過，
        // 這次只補畫相機標記，CropBox 完全不動。使用者可用「重設剖面位置」強制覆蓋回去。
        // ══════════════════════════════════════════════════════════

        // 2026-09-24 第三輪修正：第一版（GUID 1A7E4C3B-…）的 double 欄位沒有設定 Spec，Revit 2021+
        // 的 SchemaBuilder.Finish() 對「沒有單位的 double 欄位」一律丟 InvalidOperationException
        // 「Units are required for field OriginX」（2026-09-24 21:33 CameraManagerCrash log 實證，
        // 緊跟在前的 SEHException 只是同一個例外在原生層 ESSchemaBuilder.finish 的第一機會紀錄，
        // 不是另一個錯誤）。Finish 失敗＝Schema 從未註冊進任何文件，舊 GUID 理論上是乾淨的；
        // 但仍換一個新 GUID，杜絕「某個測試版本曾用錯誤欄位定義註冊過」的可能——ES Schema 一旦
        // 註冊，同一 GUID 的欄位定義永遠不能改。舊 GUID 不讀：它從未成功寫入過任何資料。
        // 座標欄位（OriginX/Y/Z）用 SpecTypeId.Length＋UnitTypeId.Feet（內部單位）；方向向量分量
        // （ForwardX/Y）是無單位數值，用 SpecTypeId.Number＋UnitTypeId.General。
        private static readonly Guid SectionStateSchemaGuid = new Guid("5B2D8E61-3C4A-4F7B-A1D9-7E0C6B3F2A58");
        private const string FieldOriginX = "OriginX";
        private const string FieldOriginY = "OriginY";
        private const string FieldOriginZ = "OriginZ";
        private const string FieldForwardX = "ForwardX";
        private const string FieldForwardY = "ForwardY";
        private const double SectionMovedOriginToleranceMm = 5.0;
        private const double SectionMovedAngleToleranceDeg = 0.5; // 2026-09-25 第二輪：2°→0.5°（狀態改存「套用後讀回」的實際值，不再有系統性誤差）

        private static Schema GetOrCreateSectionStateSchema()
        {
            Schema schema = Schema.Lookup(SectionStateSchemaGuid);
            if (schema != null) return schema;

            SchemaBuilder builder = new SchemaBuilder(SectionStateSchemaGuid);
            builder.SetSchemaName("RevitMCP_CameraSectionStateV2");
            builder.SetReadAccessLevel(AccessLevel.Public);
            builder.SetWriteAccessLevel(AccessLevel.Public);
            builder.AddSimpleField(FieldOriginX, typeof(double)).SetSpec(SpecTypeId.Length);
            builder.AddSimpleField(FieldOriginY, typeof(double)).SetSpec(SpecTypeId.Length);
            builder.AddSimpleField(FieldOriginZ, typeof(double)).SetSpec(SpecTypeId.Length);
            builder.AddSimpleField(FieldForwardX, typeof(double)).SetSpec(SpecTypeId.Number);
            builder.AddSimpleField(FieldForwardY, typeof(double)).SetSpec(SpecTypeId.Number);
            return builder.Finish();
        }

        /// <summary>面板載入時呼叫的自我檢查（不需要文件、不需要交易；Schema 註冊是應用程式層級）：
        /// 確認兩個 ES Schema 都能建出來、且剖面狀態 Schema 的 double 欄位都帶有 Spec。
        /// 回傳 null＝OK，否則為失敗原因。必須在 Revit API context（ExternalEvent／Idling）呼叫。</summary>
        internal static string SelfCheckSchemas()
        {
            try
            {
                Schema link = GetOrCreateSchema();
                if (link == null) return "CameraKeyPlanLink Schema 建立回傳 null";
                Schema state = GetOrCreateSectionStateSchema();
                if (state == null) return "CameraSectionState Schema 建立回傳 null";
                foreach (string f in new[] { FieldOriginX, FieldOriginY, FieldOriginZ, FieldForwardX, FieldForwardY })
                {
                    Field field = state.GetField(f);
                    if (field == null) return "CameraSectionState 缺欄位 " + f;
                    ForgeTypeId spec = field.GetSpecTypeId();
                    if (spec == null || spec.Empty()) return "CameraSectionState 欄位 " + f + " 沒有 Spec";
                }
                Schema options = GetOrCreateCameraSectionOptionsSchema();
                if (options == null) return "CameraSectionOptions Schema 建立回傳 null";
                return null;
            }
            catch (Exception ex)
            {
                return ex.GetType().Name + ": " + ex.Message;
            }
        }

        // ══════════════════════════════════════════════════════════
        // 2026-09-27 追加：單一相機「剖面轉 90°」開關（第三個 Schema，同樣不動前兩個既有 GUID）。
        // 這個開關記在「相機元素本身」（View3D 透視相機），不是記在剖面視圖上——這樣即使剖面
        // 視圖還沒建立（第一次跑一鍵 4 視圖前）也能先設定，且一鍵 4 視圖／剖面對位都能在算
        // ComputeSectionSpec 之前讀到它。預設 false（沒有 ES 資料＝false，不影響舊檔案）。
        // ══════════════════════════════════════════════════════════
        private static readonly Guid CameraSectionOptionsSchemaGuid = new Guid("A50006E7-15DF-4114-9032-904C391CD569");
        private const string FieldSectionRotate90 = "SectionRotate90";

        private static Schema GetOrCreateCameraSectionOptionsSchema()
        {
            Schema schema = Schema.Lookup(CameraSectionOptionsSchemaGuid);
            if (schema != null) return schema;

            SchemaBuilder builder = new SchemaBuilder(CameraSectionOptionsSchemaGuid);
            builder.SetSchemaName("RevitMCP_CameraSectionOptions");
            builder.SetReadAccessLevel(AccessLevel.Public);
            builder.SetWriteAccessLevel(AccessLevel.Public);
            builder.AddSimpleField(FieldSectionRotate90, typeof(bool));
            return builder.Finish();
        }

        /// <summary>這台相機是否設定「剖面轉 90°」（存在相機元素本身的 ES，跨 session／同事共用同一份 .rvt
        /// 都看得到；預設 false）。唯讀，任何情境都可呼叫（不需要交易）；任何失敗一律視為 false。</summary>
        internal static bool GetCameraSectionRotate90(Element camera)
        {
            try
            {
                if (camera == null) return false;
                Schema schema = Schema.Lookup(CameraSectionOptionsSchemaGuid);
                if (schema == null) return false;
                Entity e = camera.GetEntity(schema);
                if (e == null || !e.IsValid()) return false;
                return e.Get<bool>(schema.GetField(FieldSectionRotate90));
            }
            catch (Exception ex)
            {
                Logger.Error("相機 Key Plan：讀取「剖面轉90°」設定失敗，視為未啟用", ex);
                return false;
            }
        }

        /// <summary>寫入這台相機的「剖面轉 90°」設定；呼叫端須已在交易內。</summary>
        private static void SetCameraSectionRotate90(Element camera, bool value)
        {
            Schema schema = GetOrCreateCameraSectionOptionsSchema();
            Entity e = new Entity(schema);
            e.Set(schema.GetField(FieldSectionRotate90), value);
            camera.SetEntity(e);
        }

        /// <summary>設定並提交（自己開單一交易「剖面轉90°」）：面板切換選單、MCP 工具 run_camera_keyplan
        /// 的 sectionRotate90 參數共用。回傳 null＝成功，否則錯誤訊息（呼叫端須在 ExternalEvent 內）。</summary>
        internal static string ApplySectionRotate90(Document doc, Element camera, bool value)
        {
            if (doc == null) return "目前沒有開啟中的文件。";
            if (camera == null) return "找不到這台相機（可能已被刪除）。";
            using (Transaction t = TransactionHelper.Begin(doc, SectionRotate90TransactionName))
            {
                try
                {
                    t.Start();
                    SetCameraSectionRotate90(camera, value);
                    TransactionHelper.CommitOrThrow(t);
                    return null;
                }
                catch (Exception ex)
                {
                    try { if (t.HasStarted() && !t.HasEnded()) t.RollBack(); } catch { }
                    Logger.Error("相機 Key Plan：設定「剖面轉90°」失敗", ex);
                    return ex.Message;
                }
            }
        }

        /// <summary>剖面狀態是「保留手動調整」用的附屬簿記——任何失敗（Schema、寫入）都只記 log、
        /// 不往外拋，絕不能讓一鍵 4 視圖整筆交易因此回滾。</summary>
        private static void SaveSectionState(Element sectionView, XYZ origin, XYZ forwardHoriz)
        {
            try
            {
                Schema schema = GetOrCreateSectionStateSchema();
                Entity e = new Entity(schema);
                e.Set(schema.GetField(FieldOriginX), origin.X, UnitTypeId.Feet);
                e.Set(schema.GetField(FieldOriginY), origin.Y, UnitTypeId.Feet);
                e.Set(schema.GetField(FieldOriginZ), origin.Z, UnitTypeId.Feet);
                e.Set(schema.GetField(FieldForwardX), forwardHoriz.X, UnitTypeId.General);
                e.Set(schema.GetField(FieldForwardY), forwardHoriz.Y, UnitTypeId.General);
                sectionView.SetEntity(e);
            }
            catch (Exception ex)
            {
                Logger.Error("相機 Key Plan：寫入剖面狀態（ES）失敗，略過（不影響視圖建立）", ex);
                CameraManagerDiagnostics.RecordCaught("SaveSectionState", ex);
            }
        }

        /// <summary>2026-09-25 第二輪（誤判「剖面被手動移過」）：CropBox 套用後 Regenerate，讀回 Revit 實際的
        /// CropBox.Transform（Revit 會正規化剖切面位置；深度範圍外擴也會平移原點）再存 ES——之後的比對兩邊都是讀回值。</summary>
        private static void SaveSectionStateActual(Document doc, ViewSection section, BoundingBoxXYZ requested, XYZ requestedAxis)
        {
            XYZ origin = requested.Transform.Origin, axis = requestedAxis;
            try
            {
                doc.Regenerate();
                Transform tf = section.CropBox.Transform;
                XYZ bx = new XYZ(tf.BasisX.X, tf.BasisX.Y, 0);
                if (bx.GetLength() > 1e-6)
                {
                    double dMm = CameraMath.FeetToMm(tf.Origin.DistanceTo(origin));
                    if (dMm > 0.5) Logger.Info($"相機 Key Plan：剖面「{section.Name}」套用後原點被 Revit 調整 {dMm:F1}mm（存讀回值）");
                    origin = tf.Origin; axis = bx.Normalize();
                }
            }
            catch (Exception ex) { Logger.Error("相機 Key Plan：讀回剖面 CropBox 失敗，改存要求值", ex); }
            SaveSectionState(section, origin, axis);
        }

        private static bool TryReadSectionState(Element sectionView, out XYZ origin, out XYZ forwardHoriz)
        {
            origin = null;
            forwardHoriz = null;
            try
            {
                Schema schema = Schema.Lookup(SectionStateSchemaGuid);
                if (schema == null) return false;
                Entity e = sectionView.GetEntity(schema);
                if (e == null || !e.IsValid()) return false;

                double ox = e.Get<double>(schema.GetField(FieldOriginX), UnitTypeId.Feet);
                double oy = e.Get<double>(schema.GetField(FieldOriginY), UnitTypeId.Feet);
                double oz = e.Get<double>(schema.GetField(FieldOriginZ), UnitTypeId.Feet);
                double fx = e.Get<double>(schema.GetField(FieldForwardX), UnitTypeId.General);
                double fy = e.Get<double>(schema.GetField(FieldForwardY), UnitTypeId.General);
                origin = new XYZ(ox, oy, oz);
                forwardHoriz = new XYZ(fx, fy, 0);
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error("相機 Key Plan：讀取剖面狀態（ES）失敗，視為未手動調整", ex);
                origin = null;
                forwardHoriz = null;
                return false;
            }
        }

        /// <summary>比對剖面目前實際的 CropBox.Transform 與上次程式產生時記下的視線原點/方向；
        /// 沒有存過狀態（例如舊版本產生、還沒補存過）一律視為「沒動過」，照常更新一次並補存狀態。</summary>
        private static bool HasUserMovedSection(ViewSection section, XYZ expectedOrigin, XYZ expectedForwardHoriz)
        {
            if (!TryReadSectionState(section, out XYZ savedOrigin, out XYZ savedForward)) return false;
            try
            {
                Transform tf = section.CropBox.Transform;
                XYZ curOrigin = tf.Origin;
                XYZ curForwardRaw = new XYZ(tf.BasisX.X, tf.BasisX.Y, 0);
                if (curForwardRaw.GetLength() < 1e-6) return false; // 退化情形，無法判斷，當作沒動過
                XYZ curForward = curForwardRaw.Normalize();

                // 2026-09-25 第二輪：同一座標框架（模型座標，皆為 CropBox.Transform 讀回值）比對；
                // 狀態在「CropBox 套用＋Regenerate 之後」讀回再存（Revit 會把剖切面正規化、深度外擴會平移原點），
                // 所以沒人動過時兩者應完全一致。不一致時兩組值都記 log。
                double dMm = CameraMath.FeetToMm(curOrigin.DistanceTo(savedOrigin));
                double angDeg = savedForward.GetLength() > 1e-6 ? curForward.AngleTo(savedForward.Normalize()) * 180.0 / Math.PI : 0.0;
                bool originMoved = dMm > SectionMovedOriginToleranceMm;
                bool directionMoved = angDeg > SectionMovedAngleToleranceDeg;
                if (originMoved || directionMoved)
                {
                    XYZ n = tf.BasisZ;
                    double normalMm = n != null && n.GetLength() > 1e-6 ? CameraMath.FeetToMm((curOrigin - savedOrigin).DotProduct(n.Normalize())) : double.NaN;
                    Logger.Info($"相機 Key Plan：剖面「{section.Name}」判定為手動移過 — 存檔原點({CameraMath.FeetToMm(savedOrigin.X):F1},{CameraMath.FeetToMm(savedOrigin.Y):F1},{CameraMath.FeetToMm(savedOrigin.Z):F1})mm " +
                                $"方向({savedForward.X:F4},{savedForward.Y:F4}) vs 目前原點({CameraMath.FeetToMm(curOrigin.X):F1},{CameraMath.FeetToMm(curOrigin.Y):F1},{CameraMath.FeetToMm(curOrigin.Z):F1})mm " +
                                $"方向({curForward.X:F4},{curForward.Y:F4})；Δ原點={dMm:F1}mm（法向 {normalMm:F1}mm，容許 {SectionMovedOriginToleranceMm}mm）Δ角={angDeg:F2}°（容許 {SectionMovedAngleToleranceDeg}°）");
                }
                return originMoved || directionMoved;
            }
            catch
            {
                return false;
            }
        }

        private static void DeleteOldMarkers(Document doc, View view, string cameraUniqueId, string role)
        {
            if (Schema.Lookup(SchemaGuid) == null) return;
            // 2026-09-26 第四輪：標記都是視圖專屬元素 → OwnedByView＋ExtensibleStorageFilter（不再掃視圖中所有可見元素）。
            List<ElementId> toDelete = new FilteredElementCollector(doc).OwnedByView(view.Id)
                .WherePasses(new ExtensibleStorageFilter(SchemaGuid))
                .Where(e => TryReadLink(e, out string cid, out string r) && cid == cameraUniqueId && r == role)
                .Select(e => e.Id)
                .ToList();
            if (toDelete.Count > 0) doc.Delete(toDelete);
        }

        // ══════════════════════════════════════════════════════════
        // 進入點
        // ══════════════════════════════════════════════════════════

        internal class KeyPlanResult
        {
            public ElementId PlanViewId;
            public ElementId SectionViewId;
            public ElementId IsoViewId;
            public string Error;
            /// <summary>true＝剖面位置/方向與上次程式產生時不同（使用者在 Revit 內手動調整過），
            /// 這次只補畫相機標記，CropBox 完全沒有被覆寫。</summary>
            public bool SectionPreserved;
            /// <summary>2026-09-25（MCP 測試工具 run_camera_keyplan）：這次執行的關鍵判定，純資料（交易外算好的值）。
            /// 面板不使用；只是把原本只寫進 log 的判定也放進回傳值，邏輯不變。</summary>
            public KeyPlanDecisions Decisions = new KeyPlanDecisions();
            /// <summary>2026-09-25 Wave 2：一鍵 4 視圖後自動更新的樓層總覽（失敗或未建立＝null）。</summary>
            public ElementId OverviewViewId;
            /// <summary>2026-09-25 Wave 2：A 型標記同步結果（null＝未啟用 A）。</summary>
            public string AMarkerLog;
            /// <summary>2026-09-25：這次實際生效的相機標記型式（含 MCP 單次覆寫）。</summary>
            public string MarkerModeUsed;
            /// <summary>2026-09-25：裁剪框讀回與預期不符（&gt; 1 %）等警告。</summary>
            public List<string> Warnings = new List<string>();
        }

        /// <summary>一鍵 4 視圖的關鍵判定（feet／度），供 MCP 自動測試讀取；與 log 內容同源。</summary>
        internal sealed class KeyPlanDecisions
        {
            public bool IsOutdoor;
            public string ClassLog;
            public long? RoomId;
            public string RoomName;
            public string RoomLog;
            public double FarClipFeet;
            public double FanRadiusFeet;
            public string FanLog;
            public double CenterYawDeg;
            public double LeftDeg, RightDeg, DownDeg, UpDeg;
            public double? HFovDeg, VFovDeg;
            public double? RoomTopZ;
            public double StoreyTopSlabBottomZ;
            public double? StoreyTopBeamBottomZ;
            public bool StoreyTopOutdoor;
            public string StoreyTopLog;
            public string IsoXYLog;
            public CameraSectionOrientationMode SectionModeRequested;
            public CameraSectionOrientationMode SectionModeUsed;
            /// <summary>2026-09-26 第五輪：剖面位置（Camera＝剖切面通過眼睛／Center＝舊行為）。</summary>
            public CameraSectionAnchorMode SectionAnchor;
            /// <summary>2026-09-27：這台相機是否啟用「剖面轉 90°」（單一相機、持久化開關）。</summary>
            public bool SectionRotate90;
            public string SectionSpecLog;
            /// <summary>2026-09-26：剖面套用紀錄（新建／移動旋轉／刪除重建＋世界範圍後檢）。</summary>
            public string SectionApplyLog;
            /// <summary>2026-09-26：剖面意圖 vs 讀回的世界範圍最大偏差（mm）；null＝未檢（例如保留手動調整）。</summary>
            public double? SectionExtentDeviationMm;
            public CameraIsoExtentMode IsoExtentMode;
            /// <summary>2026-09-26：等角近側牆模式（Cut／Keep）與實際切了哪兩面、數值。</summary>
            public CameraIsoNearWallsMode IsoNearWalls;
            public string IsoNearWallsLog;
            public bool FadedStyle;
        }

        /// <summary>
        /// 2026-09-25（MCP 測試工具）：單次執行覆寫面板設定（剖面方向／等角範圍）。null＝沿用面板設定。
        /// 只在 <see cref="CreateOrUpdateFourViews(UIApplication, Document, ElementId, bool, KeyPlanRunOptions)"/>
        /// 執行期間生效（try/finally 還原），不寫回設定檔。
        /// </summary>
        internal sealed class KeyPlanRunOptions
        {
            public CameraSectionOrientationMode? SectionMode;
            public CameraIsoExtentMode? IsoMode;
            /// <summary>2026-09-25：相機標記型式單次覆寫（B／A／AB），不寫回面板設定。</summary>
            public CameraMarkerMode? MarkerMode;
            /// <summary>2026-09-26：等角近側牆單次覆寫（Cut／Keep），不寫回面板設定。</summary>
            public CameraIsoNearWallsMode? IsoNearWalls;
            /// <summary>2026-09-26 第五輪：剖面位置單次覆寫（Camera／Center），不寫回面板設定。</summary>
            public CameraSectionAnchorMode? SectionAnchor;
        }

        // Revit API 只在主執行緒執行，一次只會有一個一鍵 4 視圖在跑，靜態欄位即可。
        private static KeyPlanRunOptions _runOptions;

        /// <summary>帶單次覆寫選項的版本（MCP 工具用）；其餘邏輯與面板呼叫的版本完全相同。</summary>
        internal static KeyPlanResult CreateOrUpdateFourViews(UIApplication uiApp, Document doc, ElementId cameraViewId,
            bool forceResetSection, KeyPlanRunOptions options)
        {
            KeyPlanRunOptions previous = _runOptions;
            CameraMarkerMode? previousMarker = _markerModeOverride;
            _runOptions = options;
            if (options?.MarkerMode != null) _markerModeOverride = options.MarkerMode;
            try { return CreateOrUpdateFourViews(uiApp, doc, cameraViewId, forceResetSection); }
            finally { _runOptions = previous; _markerModeOverride = previousMarker; }
        }

        /// <summary>
        /// 建立或更新一台相機的 Key Plan 三視圖＋B 型標記。呼叫端須自行包在 ExternalEvent
        /// 內執行；本方法內部自己開單一交易（中文復原名稱「建立相機 Key Plan」）。
        /// </summary>
        internal static KeyPlanResult CreateOrUpdateFourViews(UIApplication uiApp, Document doc, ElementId cameraViewId, bool forceResetSection = false)
        {
            // 2026-09-26 第四輪：各步驟計時（⏱ log），實機量測時間花在哪裡。
            using (CameraPerf perf = CameraPerf.Begin("一鍵 4 視圖（Key Plan 建立/更新）"))
            {
                KeyPlanResult r = CreateOrUpdateFourViewsCore(uiApp, doc, cameraViewId, forceResetSection, perf);
                if (r?.Error != null) perf.Note("失敗：" + r.Error);
                return r;
            }
        }

        private static KeyPlanResult CreateOrUpdateFourViewsCore(UIApplication uiApp, Document doc, ElementId cameraViewId, bool forceResetSection, CameraPerf perf)
        {
            var result = new KeyPlanResult();
            if (doc == null) { result.Error = "目前沒有開啟中的文件。"; return result; }

            View3D camera = doc.GetElement(cameraViewId) as View3D;
            if (camera == null || camera.IsTemplate || !camera.IsPerspective)
            {
                result.Error = "找不到這個相機視圖（可能已被刪除），請按重新整理。";
                return result;
            }

            List<Level> levels = CameraMath.GetStoreyLevelsSorted(doc);
            CameraParams cp = ComputeCameraParams(camera, levels);
            CameraMath.CameraGeometry geom = cp.Geom;
            perf?.Lap("相機參數/房間");
            if (geom.LevelBelow == null)
            {
                result.Error = "找不到這台相機正下方的樓層，無法產生 Key Plan（一鍵 4 視圖需要樓層基準）。";
                return result;
            }

            string cid = camera.UniqueId;
            string camName = camera.Name;
            // 2026-09-26 稽核：先清掉 Revit 不允許的字元再比對／改名（否則「名稱不同 → 改名」每次都成立、依名稱找回也永遠找不到）。
            string planName = SanitizeRevitName("Key Plan-" + camName + "-平面");
            string sectionName = SanitizeRevitName("Key Plan-" + camName + "-剖面");
            string isoName = SanitizeRevitName("Key Plan-" + camName + "-等角");

            double farClipFeet = cp.FarClipFeet;
            double leftRad = cp.LeftRad, rightRad = cp.RightRad;
            XYZ forwardHoriz = cp.ForwardHoriz;
            double fanRadiusFeet = cp.FanRadiusFeet;
            Logger.Info($"相機 Key Plan：「{camName}」FOV h={(cp.FovDeg.HasValue ? cp.FovDeg.Value.ToString("F1") : "?")}° v={(cp.VFovDeg.HasValue ? cp.VFovDeg.Value.ToString("F1") : "?")}° edges[{cp.EdgesLog}] [{cp.FovLog}]");
            Logger.Info("相機 Key Plan：眼睛所在房間 " + (cp.Room?.Log ?? "(none)") + $"；扇形半徑={CameraMath.FeetToMm(fanRadiusFeet):F0}mm（{cp.FanLog}）");
            Logger.Info($"相機 Key Plan：「{camName}」室內／室外判定＝{cp.ClassLog} → 套用{(cp.IsOutdoor ? "室外規則（扇形半徑 min(遠裁剪,室外半徑)、無樓層切、剖面沿視線）" : "室內規則（房間為主）")}");

            // 2026-09-25 第五輪（使用者：「以我們這間房間為主，往外留一些 buffer」）：等角 XY／平面裁剪／剖面
            // 水平範圍一律只用「眼睛所在房間」外擴 0.6m；眼睛不在房間時退回扇形（距離夾到 8m）。純查詢，交易外算好。
            IsoXY isoXY = ComputeIsoXY(geom, forwardHoriz, leftRad, rightRad, cp);
            Logger.Info("相機 Key Plan：等角 XY " + isoXY.Log);

            // 本層「頂」：以房間頂為主（挑高房間），上層樓板只認房間頂以上者（夾層不算）。純查詢。
            StoreyTop storeyTop = ComputeStoreyTop(doc, geom, isoXY, levels, cp.Room, cp.IsOutdoor);
            Logger.Info("相機 Key Plan：本層頂判定 " + storeyTop.Log);
            Logger.Info($"相機 Key Plan：「{camName}」樓層＝「{geom.LevelBelow.Name}」（來源 {geom.StoreySource}；{geom.StoreyLog ?? "-"}）" +
                        (geom.StandingSurfaceResolved ? "；站立面 " + (geom.StandingSurfaceLog ?? "-") : ""));
            perf?.Lap("範圍計算");
            CameraSectionOrientationMode sectionMode = GetSectionOrientationMode();

            // 2026-09-25：把判定也放進回傳值（MCP 測試工具用），數值與上面 log 同源。
            KeyPlanDecisions dec = result.Decisions;
            dec.IsOutdoor = cp.IsOutdoor;
            dec.ClassLog = cp.ClassLog;
            dec.RoomId = cp.Room?.Room != null ? (long?)cp.Room.Room.Id.GetIdValue() : null;
            dec.RoomName = cp.Room?.Room?.Name;
            dec.RoomLog = cp.Room?.Log;
            dec.FarClipFeet = farClipFeet;
            dec.FanRadiusFeet = fanRadiusFeet;
            dec.FanLog = cp.FanLog;
            dec.CenterYawDeg = cp.CenterYawDeg;
            dec.LeftDeg = leftRad * 180.0 / Math.PI;
            dec.RightDeg = rightRad * 180.0 / Math.PI;
            dec.DownDeg = cp.DownRad * 180.0 / Math.PI;
            dec.UpDeg = cp.UpRad * 180.0 / Math.PI;
            dec.HFovDeg = cp.FovDeg;
            dec.VFovDeg = cp.VFovDeg;
            dec.RoomTopZ = storeyTop.RoomTopZ;
            dec.StoreyTopSlabBottomZ = storeyTop.SlabBottomZ;
            dec.StoreyTopBeamBottomZ = storeyTop.BeamBottomZ;
            dec.StoreyTopOutdoor = storeyTop.Outdoor;
            dec.StoreyTopLog = storeyTop.Log;
            dec.IsoXYLog = isoXY.Log;
            dec.SectionModeRequested = sectionMode;
            bool sectionRotate90 = GetCameraSectionRotate90(camera);
            dec.SectionRotate90 = sectionRotate90;
            dec.IsoExtentMode = GetIsoExtentMode();

            // ES Schema 註冊不需要交易；先在交易外建好，Schema 問題不會在交易中途才爆出來。
            // 剖面狀態只是附屬簿記：失敗只記 log，視圖照建（SaveSectionState 內部也會吞掉例外）。
            ResetCropWarnings();
            result.MarkerModeUsed = GetMarkerMode().ToString();
            string schemaErr = SelfCheckSchemas();
            if (schemaErr != null) Logger.Error("相機 Key Plan：ES Schema 自我檢查失敗（剖面狀態簿記將略過）：" + schemaErr);

            using (Transaction t = TransactionHelper.Begin(doc, KeyPlanTransactionName))
            {
                t.Start();
                try
                {
                    ViewPlan planView = GetOrCreatePlanView(doc, uiApp, geom.LevelBelow, planName, cid);
                    // 2026-09-25：複製來的平面可能鎖在 Scope Box（CropBox 設定會被忽略）→ 每次先解除，再設裁剪框。
                    NormalizeDuplicatedPlan(planView, "Key Plan 平面");
                    ConfigurePlanCropBox(planView, geom, forwardHoriz, leftRad, rightRad, fanRadiusFeet, isoXY, cp.IsOutdoor);
                    // 2026-09-26 第四輪（使用者：屋頂花園相機的 Key Plan 拿到下一層的室內平面）：室外相機有站立面 →
                    // 平面剖切面＝站立面 +1200 mm、底／視圖深度＝站立面 −1000 mm（屋面與女兒牆看得到）。
                    if (cp.IsOutdoor && geom.StandingSurfaceZ.HasValue)
                        DrawMarkerSafely(doc, "OutdoorPlanViewRange", () => ApplyOutdoorPlanViewRange(doc, planView, geom.LevelBelow, geom.StandingSurfaceZ.Value));
                    bool faded = IsKeyPlanFadedStyle();
                    dec.FadedStyle = faded;
                    Logger.Info("相機 Key Plan：表現法＝" + (faded ? "淡化背景" : "原始"));
                    DrawMarkerSafely(doc, "ApplyKeyPlanStylePlan", () => ApplyKeyPlanStyle(planView, faded));

                    double? cutPlaneZ = GetPlanCutPlaneZ(doc, planView);
                    XYZ planBasisX = null;
                    try { planBasisX = planView.CropBox?.Transform?.BasisX; } catch { planBasisX = null; }
                    CameraSectionAnchorMode sectionAnchor = GetSectionAnchorMode();
                    dec.SectionAnchor = sectionAnchor;
                    SectionSpec spec = ComputeSectionSpec(doc, geom, cp, isoXY, storeyTop, sectionMode, planBasisX, cutPlaneZ, sectionAnchor, sectionRotate90);
                    // 2026-09-26 第七輪：剖面沿線範圍截到 Key Plan 平面裁剪框內（避免遠端標頭超框、近端被眼睛楔形蓋住）；
                    // 一定要在 DrawPlanMarker（讀 spec/section 軸判斷是否重畫紅色視線中心線）與建剖面之前做。
                    ClampSectionSpecToPlanCrop(planView, spec);
                    Logger.Info("相機 Key Plan：剖面規格 " + spec.Log);
                    dec.SectionModeUsed = spec.ModeUsed;
                    dec.SectionSpecLog = spec.Log;
                    _lastSectionApplyLog = null; _lastSectionDeviationMm = double.NaN;
                    ViewSection sectionView = GetOrCreateSectionView(doc, sectionName, cid, geom, spec, forceResetSection, out bool preserved);
                    dec.SectionApplyLog = preserved ? "保留使用者手動調整（範圍未動）" : _lastSectionApplyLog;
                    dec.SectionExtentDeviationMm = double.IsNaN(_lastSectionDeviationMm) ? (double?)null : _lastSectionDeviationMm;
                    // 2026-09-26 第七輪：移到剖面建好之後——平面上的紅色視線中心線要跟「剖面實際軸向」比較夾角
                    // （讀既有剖面 CropBox.Transform.BasisX，preserved／新建都適用），角度 < 3° 就不重複畫。
                    XYZ sectionAxisXY = GetSectionAxisWorldXY(sectionView);
                    DrawMarkerSafely(doc, "DrawPlanMarker",
                        () => DrawPlanMarker(doc, planView, geom, forwardHoriz, leftRad, rightRad, fanRadiusFeet, cid, sectionAxisXY: sectionAxisXY));
                    result.PlanViewId = planView.Id;
                    DrawMarkerSafely(doc, "ApplyKeyPlanStyleSection",
                        () => ApplyKeyPlanStyle(sectionView, faded, isSection: true, levelBelow: geom.LevelBelow, outdoor: cp.IsOutdoor, eye: geom.Eye, cid: cid));
                    DrawMarkerSafely(doc, "DrawSectionMarker",
                        () => DrawSectionMarker(doc, sectionView, geom, cp.CenterHoriz ?? forwardHoriz, fanRadiusFeet, cp.DownRad, cp.UpRad, cid));
                    result.SectionViewId = sectionView.Id;
                    result.SectionPreserved = preserved;
                    DrawMarkerSafely(doc, "HideOtherSectionMarkers",
                        () => HideOtherSectionMarkers(doc, planView, sectionView));
                    // 2026-09-25：剖面裡也會出現其他剖面／立面／詳圖索引標記 → 逐元素隱藏（剖面沒有要保留的 viewer）。
                    DrawMarkerSafely(doc, "HideForeignViewersSection",
                        () => HideForeignViewers(doc, sectionView, null, "剖面"));

                    CameraIsoNearWallsMode nearWalls = GetIsoNearWallsMode();
                    View3D isoView = GetOrCreateIsoView(doc, isoName, cid, geom, isoXY, storeyTop, forwardHoriz,
                        cp.IsOutdoor ? null : cp.Room, nearWalls, out string nearWallsLog);
                    dec.IsoNearWalls = nearWalls;
                    dec.IsoNearWallsLog = nearWallsLog;
                    result.IsoViewId = isoView.Id;

                    TransactionHelper.CommitOrThrow(t);
                    BumpOwnViewsEpoch();
                    perf?.Lap("Key Plan 交易");
                }
                catch (Exception ex)
                {
                    try { if (t.HasStarted() && !t.HasEnded()) t.RollBack(); }
                    catch (Exception rbEx) { Logger.Error("相機 Key Plan：交易回滾失敗", rbEx); }
                    result.Error = "建立 Key Plan 失敗（已復原，模型未變更）：" + ex.Message;
                    Logger.Error("相機 Key Plan：CreateOrUpdateFourViews 失敗，交易已回滾", ex);
                    CameraManagerDiagnostics.RecordCaught("CreateOrUpdateFourViews", ex);
                }
            }

            result.Warnings.AddRange(TakeCropWarnings());
            if (result.Error == null)
            {
                RecordKeyPlanPose(doc, camera, cp);
                RecordDrawn(doc, camera, cp);
                // 2026-09-25 Wave 2：自動更新本層總覽＋（A 啟用時）A 型標記；各自獨立交易，失敗只記 log，不影響 4 視圖。
                // 2026-09-26 第四輪：該樓層相機（姿態＋模型 epoch）自上次自動更新後都沒變 → 略過總覽。
                result.OverviewViewId = TryUpdateOverviewForCamera(doc, geom.LevelBelow);
                perf?.Lap("樓層總覽");
                if (MarkerModeIncludesA())
                {
                    try { result.AMarkerLog = SyncAMarkers(doc, new List<ElementId> { camera.Id }, placeMissing: true); }
                    catch (Exception ex)
                    {
                        result.AMarkerLog = "A 型標記同步失敗：" + ex.Message;
                        Logger.Error("一鍵 4 視圖：A 型標記同步失敗（4 視圖不受影響）", ex);
                    }
                }
                else if (!_markerModeOverride.HasValue)
                {
                    // 2026-09-26 第四輪：面板設定＝B → 不碰 A 標記（切換型式時 ApplyMarkerModeChange 已統一刪除全部 A 標記）。
                    result.AMarkerLog = "標記型式＝B：略過 A 標記";
                }
                else
                {
                    // 2026-09-25 第二輪：標記型式＝B → 這台相機既有的 A 實例＋扇形要刪掉（B 標記在 A 模式時已由 DeleteOldMarkers 刪除）。
                    try
                    {
                        int removed = RemoveAMarkersForCamera(doc, cid);
                        result.AMarkerLog = removed > 0 ? $"標記型式＝B：已刪除本相機 A 實例／扇形 {removed} 個" : "標記型式＝B：本相機沒有 A 標記";
                    }
                    catch (Exception ex)
                    {
                        result.AMarkerLog = "刪除 A 型標記失敗：" + ex.Message;
                        Logger.Error("一鍵 4 視圖：刪除 A 型標記失敗（4 視圖不受影響）", ex);
                    }
                }
                perf?.Lap("A 標記");
            }

            return result;
        }

        /// <summary>
        /// 2026-09-26 第四輪：室外相機的 Key Plan 平面視圖範圍——剖切面＝站立面 +1200 mm、底與視圖深度＝站立面 −1000 mm、
        /// 頂＝剖切面 +1000 mm，全部以相機樓層為基準的偏移。須在交易內；失敗只記 log。
        /// </summary>
        private static void ApplyOutdoorPlanViewRange(Document doc, ViewPlan plan, Level level, double surfaceZ)
        {
            if (plan == null || level == null) return;
            PlanViewRange vr = plan.GetViewRange();
            double lz = level.ProjectElevation;
            double cut = surfaceZ + CameraMath.MmToFeet(1200.0) - lz;
            double bottom = surfaceZ - CameraMath.MmToFeet(1000.0) - lz;
            double top = cut + CameraMath.MmToFeet(1000.0);
            string before = "";
            try
            {
                before = $"剖切 {CameraMath.FeetToMm(vr.GetOffset(PlanViewPlane.CutPlane)):F0}、底 {CameraMath.FeetToMm(vr.GetOffset(PlanViewPlane.BottomClipPlane)):F0}、深度 {CameraMath.FeetToMm(vr.GetOffset(PlanViewPlane.ViewDepthPlane)):F0}";
            }
            catch { }
            foreach (PlanViewPlane pl in new[] { PlanViewPlane.TopClipPlane, PlanViewPlane.CutPlane, PlanViewPlane.BottomClipPlane, PlanViewPlane.ViewDepthPlane })
                vr.SetLevelId(pl, level.Id);
            vr.SetOffset(PlanViewPlane.TopClipPlane, top);
            vr.SetOffset(PlanViewPlane.CutPlane, cut);
            vr.SetOffset(PlanViewPlane.BottomClipPlane, bottom);
            vr.SetOffset(PlanViewPlane.ViewDepthPlane, bottom);
            plan.SetViewRange(vr);
            Logger.Info($"相機 Key Plan：室外平面「{plan.Name}」視圖範圍（相對「{level.Name}」mm）：{before} → 頂 {CameraMath.FeetToMm(top):F0}、剖切 {CameraMath.FeetToMm(cut):F0}、" +
                        $"底／深度 {CameraMath.FeetToMm(bottom):F0}（站立面 {CameraMath.FeetToMm(surfaceZ):F0}mm）");
        }

        // ══════════════════════════════════════════════════════════
        // 平面
        // ══════════════════════════════════════════════════════════

        private static ViewPlan GetOrCreatePlanView(Document doc, UIApplication uiApp, Level levelBelow, string planName, string cid)
        {
            View linked = FindLinkedView(doc, cid, "Plan");
            // 2026-09-26 第五輪（屋頂花園相機：新規則已判到正確樓層，但 ES 連結的平面是舊版在下一層建的）：
            // ViewPlan 的 GenLevel 不可改 → 樓層不符就退役舊平面（沒上圖紙＝刪除；上了圖紙＝改名「-舊樓層」並解除連結），
            // 再走下方同一條「從該層底圖複製」路徑重建。呼叫端後續步驟一律用回傳的新視圖。
            if (linked is ViewPlan mismatched && IsPlanLevelMismatch(mismatched, levelBelow.Id))
            {
                string oldLevel = mismatched.GenLevel?.Name ?? "?";
                string retired = RetireMismatchedKeyPlanView(doc, mismatched, cid, "MarkerPlan");
                Logger.Info($"相機 Key Plan：Key Plan 平面樓層不符（舊 {oldLevel} → 新 {levelBelow.Name}）→ 重建；{retired}");
                linked = null;
            }
            if (linked is ViewPlan linkedPlan)
            {
                // 2026-09-25（使用者決策）：相機樓層不就地修改（清單改樓層＝複製新相機）；樓層不符由上方重建處理。
                if (linkedPlan.Name != planName) linkedPlan.Name = MakeUniqueName(doc, planName, linkedPlan.Id);
                EnsureSectionsVisible(linkedPlan);
                return linkedPlan;
            }

            ViewPlan byName = new FilteredElementCollector(doc).OfClass(typeof(ViewPlan)).Cast<ViewPlan>()
                .FirstOrDefault(v => !v.IsTemplate && v.Name == planName &&
                                     (v.GenLevel == null || v.GenLevel.Id.GetIdValue() == levelBelow.Id.GetIdValue()));
            if (byName != null)
            {
                TagLink(byName, cid, "Plan");
                EnsureSectionsVisible(byName);
                return byName;
            }

            ViewPlan source = FindSourcePlanForDuplication(doc, uiApp, levelBelow.Id);
            ViewPlan created;
            if (source != null)
            {
                ElementId dupId = source.Duplicate(ViewDuplicateOption.Duplicate);
                created = doc.GetElement(dupId) as ViewPlan;
            }
            else
            {
                // 規格 §5.1：這個樓層沒有既有平面可複製 → 用預設樓層平面視圖類型新建一個。
                ViewFamilyType floorPlanVft = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType))
                    .Cast<ViewFamilyType>().FirstOrDefault(v => v.ViewFamily == ViewFamily.FloorPlan);
                if (floorPlanVft == null) throw new Exception("找不到任何樓層平面視圖類型 (ViewFamily.FloorPlan)。");
                ElementId newId = ViewPlan.Create(doc, floorPlanVft.Id, levelBelow.Id).Id;
                created = doc.GetElement(newId) as ViewPlan;
            }

            if (created == null) throw new Exception("建立/複製平面視圖失敗。");
            created.Name = MakeUniqueName(doc, planName, created.Id);
            TagLink(created, cid, "Plan");
            EnsureSectionsVisible(created);
            return created;
        }

        /// <summary>2026-09-26 第五輪：平面視圖的 GenLevel 與預期樓層不同（GenLevel 為 null 或無預期值＝不判不符）。</summary>
        internal static bool IsPlanLevelMismatch(View v, ElementId expectedLevelId)
        {
            if (!(v is ViewPlan vp) || expectedLevelId == null || expectedLevelId == ElementId.InvalidElementId) return false;
            Level gl;
            try { gl = vp.GenLevel; } catch { return false; }
            return gl != null && gl.Id.GetIdValue() != expectedLevelId.GetIdValue();
        }

        /// <summary>
        /// 2026-09-26 第五輪：退役樓層不符的 Key Plan 平面／樓層總覽（須在交易內）。
        /// 沒放在任何圖紙上（含其從屬視圖）且不是作用中視圖 → 刪除；否則刪掉本相機在其上的標記（markerRole 非 null 時）、
        /// 解除 ES 連結、改名「&lt;原名&gt;-舊樓層」（重名自動加序號）。回傳一句說明（log 用）。呼叫後不可再使用 old。
        /// </summary>
        private static string RetireMismatchedKeyPlanView(Document doc, ViewPlan old, string markerCid, string markerRole)
        {
            ElementId oldId = old.Id;
            string oldName = old.Name;
            var ids = new HashSet<long> { oldId.GetIdValue() };
            try { foreach (ElementId d in old.GetDependentViewIds()) ids.Add(d.GetIdValue()); } catch { }
            bool onSheet = new FilteredElementCollector(doc).OfClass(typeof(Viewport)).Cast<Viewport>()
                .Any(vp => vp.ViewId != null && ids.Contains(vp.ViewId.GetIdValue()));
            bool isActive = false;
            try { isActive = doc.ActiveView != null && doc.ActiveView.Id == oldId; } catch { }
            if (!onSheet && !isActive)
            {
                try
                {
                    doc.Delete(oldId);
                    return $"已刪除舊視圖「{oldName}」（未放在圖紙上）";
                }
                catch (Exception ex) { Logger.Error($"相機 Key Plan：刪除舊樓層視圖「{oldName}」失敗，改為改名保留", ex); }
            }
            if (markerCid != null && markerRole != null)
            {
                try { DeleteOldMarkers(doc, old, markerCid, markerRole); }
                catch (Exception ex) { Logger.Error($"相機 Key Plan：清除舊樓層視圖「{oldName}」上的相機標記失敗", ex); }
            }
            try
            {
                Schema s = Schema.Lookup(SchemaGuid);
                if (s != null) old.DeleteEntity(s);
            }
            catch (Exception ex) { Logger.Error($"相機 Key Plan：解除舊樓層視圖「{oldName}」的連結失敗", ex); }
            string newName = MakeUniqueName(doc, oldName + "-舊樓層", oldId);
            old.Name = newName;
            return $"舊視圖{(onSheet ? "已放在圖紙上" : "為作用中視圖")} → 改名「{newName}」並解除連結";
        }

        /// <summary>
        /// 2026-09-24 實測回報：「一鍵4視圖」建好剖面後，Key Plan 平面上完全看不到 Revit 原生的
        /// 剖面線標記（可拖曳調整剖切位置的那條線）。根因：平面視圖是從既有來源平面
        /// <c>Duplicate</c> 出來的，若來源套用了 ViewTemplate，複製出來的 Key Plan 平面會直接
        /// 繼承同一個樣板；樣板若控制了 V/G 的「Sections」類別可見性（很多專案樣板會關掉剖面
        /// 符號以免出圖雜亂），複製出來的 Key Plan 平面就跟著看不到剖面線，且面板自己
        /// SetCategoryHidden 也會被樣板蓋掉。Key Plan 平面是本工具自建的輔助/診斷視圖，不是
        /// 專案出圖用的平面，拿掉樣板不影響使用者原本的樣板本身（只影響這張新視圖）；因此這裡
        /// 明確拆掉 ViewTemplateId，再確保 Sections 類別可見。兩步都包 try/catch，任何版本差異
        /// 或視圖類型不支援都不讓整個「一鍵4視圖」流程中斷。
        /// </summary>
        private static void EnsureSectionsVisible(ViewPlan plan)
        {
            try
            {
                if (plan.ViewTemplateId != ElementId.InvalidElementId)
                    plan.ViewTemplateId = ElementId.InvalidElementId;
            }
            catch { /* 忽略：某些視圖狀態下可能不允許清除樣板，不讓建立流程中斷 */ }

            try
            {
                Category sectionsCat = Category.GetCategory(plan.Document, BuiltInCategory.OST_Sections);
                if (sectionsCat != null && plan.CanCategoryBeHidden(sectionsCat.Id))
                    plan.SetCategoryHidden(sectionsCat.Id, false);
            }
            catch { /* 忽略：找不到類別或該視圖類型不支援時，不讓建立流程中斷 */ }
        }

        /// <summary>
        /// 2026-09-25（使用者要求：Key Plan 平面只顯示「我們的」剖面線）：在這張 Key Plan 平面裡，
        /// 把其他「剖面」的 viewer 標記（OST_Viewers）逐元素隱藏，只留 Key Plan-&lt;相機&gt;-剖面。
        /// 只動剖面（viewer 對應的視圖 ViewType == Section）；立面、詳圖索引（callout）、其他視圖一律不碰。
        /// viewer ↔ 剖面視圖的對應判定（依序，記 log 說明比對依據，不猜）：
        ///   ① ourSection.GetDependentElements(OST_Viewers) 回傳的元素（刪除剖面時會連帶刪除的 viewer）；
        ///   ② viewer.Name 與剖面視圖名稱完全相同（Revit 的剖面 viewer 名稱＝視圖名稱，視圖名稱在文件內唯一）；
        ///   ③ viewer 的 VIEW_NAME 參數值（「視圖名稱」）與剖面視圖名稱相同。
        /// 其他 viewer 同樣以名稱／VIEW_NAME 對應到 ViewType==Section 的視圖才算「剖面」，才會被隱藏。
        /// 每次一鍵 4 視圖與「更新相機標記」都重跑（冪等：已隱藏的 viewer 不會出現在視圖收集器裡）。
        /// 必須在交易內呼叫；任何失敗只記 log，不丟例外。
        /// </summary>
        private static void HideOtherSectionMarkers(Document doc, ViewPlan plan, ViewSection ourSection)
        {
            if (doc == null || plan == null || ourSection == null) return;
            try
            {
                var sectionViewsByName = new Dictionary<string, ViewSection>(StringComparer.Ordinal);
                foreach (ViewSection vs in new FilteredElementCollector(doc).OfClass(typeof(ViewSection)).Cast<ViewSection>())
                {
                    if (vs.IsTemplate || vs.ViewType != ViewType.Section) continue;
                    sectionViewsByName[vs.Name] = vs;
                }

                string ourName = ourSection.Name;
                var ourViewerIds = new HashSet<ElementId>();
                string matchedBy = null;
                try
                {
                    IList<ElementId> deps = ourSection.GetDependentElements(new ElementCategoryFilter(BuiltInCategory.OST_Viewers));
                    if (deps != null)
                        foreach (ElementId d in deps) if (d != ourSection.Id) ourViewerIds.Add(d);
                    if (ourViewerIds.Count > 0) matchedBy = "GetDependentElements";
                }
                catch (Exception ex) { Logger.Error("相機 Key Plan：GetDependentElements(OST_Viewers) 失敗，改用名稱比對", ex); }

                // 以文件層級收集 viewer（含已被隱藏者），用來找「我們的」並確保它沒被藏起來。
                List<Element> allViewers = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Viewers)
                    .WhereElementIsNotElementType().ToList();
                string ViewerViewName(Element e)
                {
                    try
                    {
                        string p = e.get_Parameter(BuiltInParameter.VIEW_NAME)?.AsString();
                        if (!string.IsNullOrEmpty(p)) return p;
                    }
                    catch { }
                    return e.Name;
                }
                if (ourViewerIds.Count == 0)
                {
                    foreach (Element e in allViewers)
                    {
                        if (e.Name == ourName) { ourViewerIds.Add(e.Id); matchedBy = "Name"; }
                        else if (ViewerViewName(e) == ourName) { ourViewerIds.Add(e.Id); matchedBy = matchedBy ?? "VIEW_NAME"; }
                    }
                }
                if (ourViewerIds.Count == 0)
                {
                    Logger.Info($"相機 Key Plan：找不到剖面「{ourName}」對應的 viewer，為避免誤藏，本次不隱藏其他剖面。");
                    return;
                }

                // 我們的 viewer 若曾被藏起來，解除隱藏。
                try
                {
                    var toUnhide = ourViewerIds.Where(id => doc.GetElement(id) is Element e && e.IsHidden(plan)).ToList();
                    if (toUnhide.Count > 0) plan.UnhideElements(toUnhide);
                }
                catch (Exception ex) { Logger.Error("相機 Key Plan：解除隱藏本相機剖面 viewer 失敗", ex); }

                EnsureOurSectionMarkerVisible(doc, plan, ourSection, ourViewerIds);

                // 2026-09-25：不只其他剖面，立面／詳圖索引標記也逐元素隱藏（只留本相機剖面），並隱藏立面／詳圖索引類別。
                int hiddenCount = HideForeignViewers(doc, plan, ourViewerIds, "平面");
                Logger.Info($"相機 Key Plan：平面「{plan.Name}」只留本相機剖面「{ourName}」（viewer 比對依據={matchedBy}，" +
                    $"ids={string.Join(",", ourViewerIds.Select(i => i.GetIdValue().ToString()))}），隱藏其他視圖標記 {hiddenCount} 個");
            }
            catch (Exception ex)
            {
                Logger.Error("相機 Key Plan：隱藏其他剖面標記失敗（視圖照常建立）", ex);
                CameraManagerDiagnostics.RecordCaught("HideOtherSectionMarkers", ex);
            }
        }

        /// <summary>專案內第一個名稱含「dash」（不分大小寫）或「虛」的 <see cref="LinePatternElement"/>（依名稱排序取第一個）；
        /// 找不到回傳 null（呼叫端維持實線，不強塞一個不存在的樣式）。</summary>
        private static ElementId FindDashLinePatternId(Document doc)
        {
            try
            {
                LinePatternElement dash = new FilteredElementCollector(doc).OfClass(typeof(LinePatternElement)).Cast<LinePatternElement>()
                    .Where(x => x.Name != null && (x.Name.IndexOf("dash", StringComparison.OrdinalIgnoreCase) >= 0 || x.Name.Contains("虛")))
                    .OrderBy(x => x.Name, StringComparer.Ordinal).FirstOrDefault();
                return dash?.Id;
            }
            catch (Exception ex) { Logger.Error("相機 Key Plan：搜尋虛線圖案失敗（改用實線）", ex); return null; }
        }

        /// <summary>
        /// 2026-09-25（使用者回報：1:100 的 Key Plan 平面上看不到本相機剖面線，log 卻說已只留本相機剖面）。
        /// 逐項檢查並修正，每項記 log 說明「是否是原因、做了什麼」：
        ///   ① 剖面「在比例粗於…時隱藏」（SECTION_COARSER_SCALE_PULLDOWN_METRIC／_IMPERIAL，執行期以名稱解析）
        ///      若比平面比例細 → Revit 不畫剖面標記；每次都設成可接受的最粗值（5000→…→200 依序嘗試）。
        ///   ② 平面 Sections 類別被隱藏 → 取消隱藏。
        ///   ③ viewer 元素逐元素隱藏 → 已由呼叫端 UnhideElements；這裡再記一次最終 IsHidden。
        ///   ④ 平面 Display Model＝半色調會讓標記變灰 → 對 viewer 設元素取代：halftone=false、投影線色＝深灰 64、線寬 6、
        ///      虛線（專案有 Dash 圖案才套用；不跟淡化走，2026-09-26 第七輪修正——原本套標記色、線寬 5，容易被誤認成標記）。
        /// 剖面垂直範圍是否包含平面剖切面（原因②於協調者清單）在 ComputeSectionSpec 已確保並記 log。
        /// </summary>
        private static void EnsureOurSectionMarkerVisible(Document doc, ViewPlan plan, ViewSection section, ICollection<ElementId> viewerIds)
        {
            int planScale = 0;
            try { planScale = plan.Scale; } catch { }
            // ① 比例門檻
            foreach (string name in new[] { "SECTION_COARSER_SCALE_PULLDOWN_METRIC", "SECTION_COARSER_SCALE_PULLDOWN_IMPERIAL" })
            {
                try
                {
                    if (!Enum.TryParse(name, out BuiltInParameter bip)) { Logger.Info($"相機 Key Plan：此版本沒有參數 {name}，略過"); continue; }
                    var owners = new List<Element> { section };
                    foreach (ElementId vid in viewerIds) if (doc.GetElement(vid) is Element ve) owners.Add(ve);
                    foreach (Element owner in owners)
                    {
                        Parameter p = owner.get_Parameter(bip);
                        if (p == null) continue;
                        string who = owner.Id == section.Id ? "section" : "viewer";
                        if (p.IsReadOnly || p.StorageType != StorageType.Integer)
                        {
                            Logger.Info($"相機 Key Plan：{name}（{who}）唯讀或型別={p.StorageType}，值={p.AsValueString()}，略過");
                            continue;
                        }
                        int before = p.AsInteger();
                        bool wasCause = planScale > 0 && before > 0 && before < planScale;
                        int applied = before;
                        // 2026-09-27 修正：section 與 viewer（及 METRIC／IMPERIAL）共用同一個值；不在下拉選項內的值
                        // Set 會回 true 但讀回變 -1（無效＝剖面線全被藏）。原本沒還原，最後一輪把 2400 蓋成 -1。
                        // 現在：已經 ≥ 平面比例就不動；每次嘗試讀回不符就立刻還原成 before。
                        if (before >= planScale && planScale > 0) { applied = before; }
                        else
                        {
                            foreach (int cand in new[] { 5000, 2500, 2400, 2000, 1200, 1000, 500, 200 })
                            {
                                if (cand <= before) break;
                                try
                                {
                                    if (p.Set(cand) && p.AsInteger() == cand) { applied = cand; break; }
                                }
                                catch { /* 此值不在下拉選項內，試下一個 */ }
                                try { if (p.AsInteger() != before) p.Set(before); } catch { }
                            }
                        }
                        Logger.Info($"相機 Key Plan：剖面比例門檻 {name}（{who}）{before}→{p.AsInteger()}（{p.AsValueString()}），平面 1:{planScale}" +
                                    (wasCause ? "——原因①成立：門檻比平面比例細，剖面線原本被隱藏" : "——原因①不成立"));
                    }
                }
                catch (Exception ex) { Logger.Error($"相機 Key Plan：設定剖面比例門檻 {name} 失敗", ex); }
            }
            // ② 類別
            try
            {
                Category cat = Category.GetCategory(doc, BuiltInCategory.OST_Sections);
                if (cat != null && plan.CanCategoryBeHidden(cat.Id))
                {
                    bool hidden = plan.GetCategoryHidden(cat.Id);
                    if (hidden) plan.SetCategoryHidden(cat.Id, false);
                    Logger.Info($"相機 Key Plan：平面 Sections 類別 hidden={hidden}" + (hidden ? "——原因成立，已取消隱藏" : "——正常"));
                }
            }
            catch (Exception ex) { Logger.Error("相機 Key Plan：檢查平面 Sections 類別失敗", ex); }
            // ⑤ 視圖篩選器（2026-09-27 使用者回報：平面仍看不到剖面線；MCP 診斷＝複製來源平面帶進的篩選器
            //    「WV_Sections」Visible=false 把所有剖面標記藏掉）。其他剖面標記已由 HideOtherViewers 逐元素隱藏，
            //    所以把「類別含 Sections／Viewers 且會抓到本剖面 viewer」的隱藏篩選器改回可見是安全的。
            try
            {
                foreach (ElementId fid in plan.GetFilters())
                {
                    if (plan.GetFilterVisibility(fid)) continue;
                    if (!(doc.GetElement(fid) is ParameterFilterElement pfe)) continue;
                    ICollection<ElementId> cats = pfe.GetCategories();
                    bool coversSections = cats.Any(c => c.GetIdValue() == (long)BuiltInCategory.OST_Sections
                                                     || c.GetIdValue() == (long)BuiltInCategory.OST_Viewers);
                    if (!coversSections) continue;
                    ElementFilter ef = null;
                    try { ef = pfe.GetElementFilter(); } catch { }
                    bool catchesOurs = ef == null || viewerIds.Count == 0
                        || viewerIds.Any(vid => { try { return ef.PassesFilter(doc, vid); } catch { return true; } });
                    if (!catchesOurs) { Logger.Info($"相機 Key Plan：篩選器「{pfe.Name}」隱藏剖面但規則不命中本剖面，保留"); continue; }
                    plan.SetFilterVisibility(fid, true);
                    Logger.Info($"相機 Key Plan：篩選器「{pfe.Name}」在平面把剖面設為不可見——原因⑤成立，已改為可見（其他剖面已逐元素隱藏）");
                }
            }
            catch (Exception ex) { Logger.Error("相機 Key Plan：檢查平面視圖篩選器失敗", ex); }
            // ③＋④
            // 2026-09-26 第七輪（使用者：剖面線要看得像一般剖面線，不要跟著淡化、也不要用標記色）：
            // 投影線色改深灰 64（KpDarkGrey）、線寬 6、有虛線圖案就用虛線；halftone 仍固定 false（不跟淡化走）。
            ElementId dashPatternId = FindDashLinePatternId(doc);
            foreach (ElementId vid in viewerIds)
            {
                try
                {
                    Element ve = doc.GetElement(vid);
                    bool hiddenNow = ve != null && ve.IsHidden(plan);
                    var ogs = new OverrideGraphicSettings();
                    ogs.SetHalftone(false);
                    ogs.SetProjectionLineColor(KpDarkGrey);
                    ogs.SetProjectionLineWeight(SectionViewerLineWeight);
                    if (dashPatternId != null) ogs.SetProjectionLinePatternId(dashPatternId);
                    plan.SetElementOverrides(vid, ogs);
                    Logger.Info($"相機 Key Plan：本相機剖面 viewer {vid.GetIdValue()} 最終 IsHidden={hiddenNow}；已設元素取代" +
                                $"（非半色調、深灰 64、線寬 {SectionViewerLineWeight}、{(dashPatternId != null ? "虛線" : "實線（專案內找不到 Dash 圖案）")}）");
                }
                catch (Exception ex) { Logger.Error("相機 Key Plan：本相機剖面 viewer 元素取代失敗", ex); }
            }
        }

        /// <summary>比照「並排檢視」(4.9 A) 的既有視圖挑選順序：已開啟優先、其次已裁剪、其次名稱排序；
        /// 這裡再優先挑 FloorPlan 類型（Key Plan 平面本來就該是一般樓層平面）。</summary>
        private static ViewPlan FindSourcePlanForDuplication(Document doc, UIApplication uiApp, ElementId levelId)
        {
            List<ViewPlan> candidates = new FilteredElementCollector(doc).OfClass(typeof(ViewPlan)).Cast<ViewPlan>()
                .Where(v => !v.IsTemplate && v.GenLevel != null && v.GenLevel.Id == levelId)
                .Where(v => !IsToolGeneratedView(v)) // 2026-09-26 第七輪：不從本工具的 Key Plan 平面／總覽複製
                .OrderBy(v => v.Name)
                .ToList();
            if (candidates.Count == 0) return null;

            HashSet<ElementId> openIds = new HashSet<ElementId>(
                uiApp?.ActiveUIDocument?.GetOpenUIViews()?.Select(uv => uv.ViewId) ?? Enumerable.Empty<ElementId>());

            List<ViewPlan> floorPlans = candidates.Where(v => v.ViewType == ViewType.FloorPlan).ToList();
            List<ViewPlan> pool = floorPlans.Count > 0 ? floorPlans : candidates;

            return pool.FirstOrDefault(v => openIds.Contains(v.Id) && v.CropBoxActive)
                ?? pool.FirstOrDefault(v => openIds.Contains(v.Id))
                ?? pool.FirstOrDefault(v => v.CropBoxActive)
                ?? pool[0];
        }

        /// <summary>
        /// 裁剪框只調整範圍（Min/Max），刻意不動 Transform——這樣裁剪框跟原平面視圖的座標系
        /// 保持一致，符合規格「keep it axis-aligned to the source view's orientation」。
        /// 框住相機位置 + 視野扇形的三個特徵點（左邊界、中心、右邊界，各在遠裁剪距離處），
        /// 外加固定 margin。
        /// </summary>
        private static void ConfigurePlanCropBox(ViewPlan planView, CameraMath.CameraGeometry geom, XYZ forwardHoriz, double leftRad, double rightRad, double farClipFeet, IsoXY isoXY = null, bool outdoor = false)
        {
            planView.CropBoxActive = true;
            planView.CropBoxVisible = true;

            BoundingBoxXYZ box = planView.CropBox;
            Transform tf = box.Transform;
            Transform inv = tf.Inverse;

            // 2026-09-25 室外規則：裁剪＝非對稱扇形（眼睛＋兩緣＋弧取樣，半徑＝farClipFeet 參數＝室外扇形半徑）外接框 +2m，
            // 在平面視圖自身座標系計算（Transform 不動）。
            if (outdoor)
            {
                try
                {
                    double m = CameraMath.MmToFeet(OutdoorPlanMarginMm);
                    XYZ le = inv.OfPoint(geom.Eye);
                    double ox0 = le.X, ox1 = le.X, oy0 = le.Y, oy1 = le.Y;
                    const int samplesO = 16;
                    for (int i = 0; i <= samplesO; i++)
                    {
                        double a = leftRad + (rightRad - leftRad) * i / samplesO;
                        XYZ l = inv.OfPoint(geom.Eye + PlanEdgeDir(forwardHoriz, a) * farClipFeet);
                        ox0 = Math.Min(ox0, l.X); ox1 = Math.Max(ox1, l.X); oy0 = Math.Min(oy0, l.Y); oy1 = Math.Max(oy1, l.Y);
                    }
                    box.Min = new XYZ(ox0 - m, oy0 - m, box.Min.Z);
                    box.Max = new XYZ(ox1 + m, oy1 + m, box.Max.Z);
                    SetCropAndVerify(planView, box, "Key Plan 平面");
                    Logger.Info($"相機 Key Plan：平面裁剪（室外）＝扇形 R={CameraMath.FeetToMm(farClipFeet):F0}mm 外接框 +2m → {CameraMath.FeetToMm(ox1 - ox0 + 2 * m):F0}×{CameraMath.FeetToMm(oy1 - oy0 + 2 * m):F0}mm");
                    return;
                }
                catch (Exception ex)
                {
                    Logger.Error("相機 Key Plan：平面裁剪（室外規則）失敗，退回扇形規則", ex);
                    box = planView.CropBox; tf = box.Transform; inv = tf.Inverse;
                }
            }

            XYZ dirCenter = forwardHoriz;
            XYZ dirLeft = PlanEdgeDir(forwardHoriz, leftRad);
            XYZ dirRight = PlanEdgeDir(forwardHoriz, rightRad);

            List<XYZ> pts = new List<XYZ>
            {
                geom.Eye,
                geom.Eye + dirCenter * farClipFeet,
                geom.Eye + dirLeft * farClipFeet,
                geom.Eye + dirRight * farClipFeet
            };

            // 2026-09-25（Key Plan 表現法 §5.1）：有「看得到的房間聯集」（已含 +0.5m）時，裁剪＝房間聯集，
            // 必要時外擴包住完整扇形（扇形取弧上取樣點、外留 0.5m）；沒有房間聯集才退回舊規則（扇形 + 2m）。
            if (isoXY != null && isoXY.HasRoomUnion)
            {
                try
                {
                    double fanM = Cm(50);
                    double rx0 = isoXY.RoomMinX, ry0 = isoXY.RoomMinY, rx1 = isoXY.RoomMaxX, ry1 = isoXY.RoomMaxY;
                    var fan = new List<XYZ> { geom.Eye };
                    const int samples = 16;
                    for (int i = 0; i <= samples; i++)
                    {
                        double a = leftRad + (rightRad - leftRad) * i / samples;
                        fan.Add(geom.Eye + PlanEdgeDir(forwardHoriz, a) * farClipFeet);
                    }
                    double z = geom.Eye.Z;
                    var all = new List<XYZ>
                    {
                        new XYZ(rx0, ry0, z), new XYZ(rx1, ry0, z), new XYZ(rx0, ry1, z), new XYZ(rx1, ry1, z)
                    };
                    double ax0 = double.MaxValue, ax1 = double.MinValue, ay0 = double.MaxValue, ay1 = double.MinValue;
                    foreach (XYZ p in all)
                    {
                        XYZ l = inv.OfPoint(p);
                        ax0 = Math.Min(ax0, l.X); ax1 = Math.Max(ax1, l.X); ay0 = Math.Min(ay0, l.Y); ay1 = Math.Max(ay1, l.Y);
                    }
                    foreach (XYZ p in fan)
                    {
                        XYZ l = inv.OfPoint(p);
                        ax0 = Math.Min(ax0, l.X - fanM); ax1 = Math.Max(ax1, l.X + fanM);
                        ay0 = Math.Min(ay0, l.Y - fanM); ay1 = Math.Max(ay1, l.Y + fanM);
                    }
                    box.Min = new XYZ(ax0, ay0, box.Min.Z);
                    box.Max = new XYZ(ax1, ay1, box.Max.Z);
                    SetCropAndVerify(planView, box, "Key Plan 平面");
                    Logger.Info($"相機 Key Plan：平面裁剪＝房間聯集∪完整扇形(+0.5m) → {CameraMath.FeetToMm(ax1 - ax0):F0}×{CameraMath.FeetToMm(ay1 - ay0):F0}mm");
                    return;
                }
                catch (Exception ex)
                {
                    Logger.Error("相機 Key Plan：平面裁剪（房間聯集規則）失敗，退回扇形規則", ex);
                    box = planView.CropBox; tf = box.Transform; inv = tf.Inverse;
                }
            }

            double marginFeet = Cm(PlanMarginCm);
            double minX = double.MaxValue, maxX = double.MinValue, minY = double.MaxValue, maxY = double.MinValue;
            foreach (XYZ p in pts)
            {
                XYZ local = inv.OfPoint(p);
                minX = Math.Min(minX, local.X); maxX = Math.Max(maxX, local.X);
                minY = Math.Min(minY, local.Y); maxY = Math.Max(maxY, local.Y);
            }

            box.Min = new XYZ(minX - marginFeet, minY - marginFeet, box.Min.Z);
            box.Max = new XYZ(maxX + marginFeet, maxY + marginFeet, box.Max.Z);
            SetCropAndVerify(planView, box, "Key Plan 平面");
        }

        /// <summary>2026-09-25：平面上的視錐邊界方向。edgeRad＝相對正前方的帶號角（相機右為正）；RotateHorizontal
        /// 以逆時針為正，而相機右＝forward×up 在平面上是「順時針」側（forward=+Y → right=+X），所以旋轉 −edgeRad。
        /// 例：左緣 −44° → 逆時針 44°（左側），右緣 +25° → 順時針 25°（右側）。</summary>
        private static XYZ PlanEdgeDir(XYZ forwardHoriz, double edgeRad) => RotateHorizontal(forwardHoriz, -edgeRad);

        private static XYZ RotateHorizontal(XYZ v, double angleRad)
        {
            double cos = Math.Cos(angleRad), sin = Math.Sin(angleRad);
            return new XYZ(v.X * cos - v.Y * sin, v.X * sin + v.Y * cos, v.Z);
        }

        // ══════════════════════════════════════════════════════════
        // 剖面
        // ══════════════════════════════════════════════════════════

        /// <summary>
        /// forceReset＝true 時無條件覆寫（「重設剖面位置」用）；否則先比對使用者是否手動調整過
        /// （<see cref="HasUserMovedSection"/>），調整過就只補 ES 連結／改名、CropBox 完全不動，
        /// preserved 回傳 true 讓呼叫端在狀態列告知使用者。
        /// </summary>
        private static ViewSection GetOrCreateSectionView(Document doc, string sectionName, string cid,
            CameraMath.CameraGeometry geom, SectionSpec spec, bool forceReset, out bool preserved)
        {
            preserved = false;
            BoundingBoxXYZ box = BuildSectionBox(geom, spec);
            XYZ forwardHoriz = spec.Axis; // ES 狀態記的是「剖面 X 軸」（沿視線模式＝水平視線；對齊房間模式＝房間長軸）

            View linked = FindLinkedView(doc, cid, "Section");
            ViewSection existing = linked as ViewSection;
            if (existing == null)
            {
                existing = new FilteredElementCollector(doc).OfClass(typeof(ViewSection)).Cast<ViewSection>()
                    .FirstOrDefault(v => !v.IsTemplate && v.Name == sectionName);
            }

            if (existing != null)
            {
                if (existing.Name != sectionName) existing.Name = MakeUniqueName(doc, sectionName, existing.Id);

                if (!forceReset && HasUserMovedSection(existing, geom.Eye, forwardHoriz))
                {
                    preserved = true;
                }
                else
                {
                    // 2026-09-26 第三輪（回歸）：既有剖面設 CropBox 時 Revit 不採用新 Transform（只把 Min/Max 套在舊座標系）
                    // → 先移動／旋轉剖面到新原點與方向，再在「讀回的實際座標系」算區域範圍；做不到且不在圖紙上才刪除重建。
                    // 2026-09-26 第四輪：UpdateExistingSectionFrame 可能刪除重建——之後 existing 是已刪除物件，
                    // 碰它任何屬性（含 .Id）都會 InvalidObjectException。先記下舊 Id，重建後一律只用回傳的新視圖。
                    ElementId existingId = existing.Id;
                    ViewSection moved = UpdateExistingSectionFrame(doc, existing, box, sectionName, cid, out string applyLog);
                    _lastSectionApplyLog = applyLog;
                    if (moved == null)
                    {
                        preserved = false;
                        if (!existing.IsValidObject)
                            throw new InvalidOperationException("剖面已被刪除且重建失敗：" + applyLog);
                        TagLink(existing, cid, "Section");
                        return existing;
                    }
                    if (moved.Id != existingId)
                    {
                        // 新視圖：CreateSectionFromBox 已重掛 ES 連結（role Section）；舊視圖的標記隨視圖一起刪除，
                        // 呼叫端後續（樣式、標記、隱藏他剖面、ES 狀態、Result.SectionViewId）全用這個新視圖。
                        TagLink(moved, cid, "Section");
                        SaveSectionStateActual(doc, moved, box, forwardHoriz);
                        return moved;
                    }
                    SaveSectionStateActual(doc, existing, box, forwardHoriz); // 2026-09-25 第二輪：存套用後讀回的實際狀態
                }
                TagLink(existing, cid, "Section");
                return existing;
            }

            ViewSection created = CreateSectionFromBox(doc, box, sectionName, cid);
            _lastSectionApplyLog = "新建剖面；" + DescribeSectionExtentCheck(doc, created, box, out _);
            SaveSectionStateActual(doc, created, box, forwardHoriz);
            return created;
        }

        private static ViewSection CreateSectionFromBox(Document doc, BoundingBoxXYZ box, string sectionName, string cid)
        {
            ViewFamilyType sectionVft = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>().FirstOrDefault(v => v.ViewFamily == ViewFamily.Section);
            if (sectionVft == null) throw new Exception("找不到任何剖面視圖類型 (ViewFamily.Section)。");

            ViewSection created = ViewSection.CreateSection(doc, sectionVft.Id, box);
            // 2026-09-27（使用者：平面 Key Plan 看不到剖面線）：MCP 實測 CreateSection 產生的剖面與框方向相反——
            // 讀回 BasisX＝−意圖軸、剖切面落在意圖框的遠裁剪面（眼睛 60 m 外），範圍相同所以範圍後檢仍報 0 mm。
            // 讀回方向相反 → 刪掉，改用「同一個實體框、座標系繞鉛直軸轉 180°」重建（X、Z 軸與範圍都取反），
            // 再檢一次方向與「意圖剖切面（Max.Z）通過原點」。
            doc.Regenerate();
            string orientLog = DescribeSectionOrientation(created, box, out bool flipped);
            if (flipped)
            {
                doc.Delete(created.Id);
                created = ViewSection.CreateSection(doc, sectionVft.Id, RotateSectionRequest180(box));
                doc.Regenerate();
                string again = DescribeSectionOrientation(created, box, out bool stillFlipped);
                orientLog += "；已改用反向描述重建 → " + again;
                if (stillFlipped) AddCropWarning($"Key Plan 剖面「{sectionName}」方向仍與意圖相反（剖切面未通過相機位置）");
            }
            Logger.Info($"相機 Key Plan：新建剖面方向檢查 {orientLog}");
            created.Name = MakeUniqueName(doc, sectionName, created.Id);
            TagLink(created, cid, "Section");
            return created;
        }

        /// <summary>同一個實體框，座標系繞鉛直軸轉 180°：BasisX、BasisZ 取反，X、Z 範圍鏡射（BasisY＝上不變）。</summary>
        private static BoundingBoxXYZ RotateSectionRequest180(BoundingBoxXYZ box)
        {
            Transform t = Transform.Identity;
            t.Origin = box.Transform.Origin;
            t.BasisX = box.Transform.BasisX.Negate();
            t.BasisY = box.Transform.BasisY;
            t.BasisZ = box.Transform.BasisZ.Negate();
            return new BoundingBoxXYZ
            {
                Transform = t,
                Min = new XYZ(-box.Max.X, box.Min.Y, -box.Max.Z),
                Max = new XYZ(-box.Min.X, box.Max.Y, -box.Min.Z)
            };
        }

        /// <summary>讀回剖面方向：實際 BasisX 與意圖 BasisX 同向？意圖剖切面（意圖 Max.Z 面）上的點在實際座標系是否落在實際剖切面（Max.Z）。</summary>
        private static string DescribeSectionOrientation(ViewSection section, BoundingBoxXYZ intended, out bool flipped)
        {
            flipped = false;
            try
            {
                BoundingBoxXYZ act = section.CropBox;
                double dot = act.Transform.BasisX.DotProduct(intended.Transform.BasisX);
                XYZ cutPt = intended.Transform.OfPoint(new XYZ(0, 0, intended.Max.Z));
                double cutZ = act.Transform.Inverse.OfPoint(cutPt).Z;
                double offMm = CameraMath.FeetToMm(cutZ - act.Max.Z);
                flipped = dot < 0 || Math.Abs(offMm) > SectionExtentToleranceMm;
                return $"BasisX·意圖={dot:F3}、意圖剖切面距實際剖切面={offMm:F0}mm" + (flipped ? "（方向相反）" : "（正確）");
            }
            catch (Exception ex) { return "方向檢查失敗：" + ex.Message; }
        }

        /// <summary>本次一鍵 4 視圖的剖面套用紀錄（移動/旋轉/重建＋世界範圍後檢）；交易內寫、呼叫端讀出放進 Decisions。</summary>
        private static string _lastSectionApplyLog;
        /// <summary>剖面世界範圍後檢：意圖 vs 讀回的最大偏差（mm）；NaN＝未檢。</summary>
        private static double _lastSectionDeviationMm = double.NaN;
        private const double SectionExtentToleranceMm = 5.0;

        /// <summary>
        /// 2026-09-26 第三輪：既有剖面改到新的原點／方向。
        /// ① 找剖面的 viewer 元素（GetDependentElements(OST_Viewers) 中 OwnerViewId 不是剖面自己者＝其他視圖裡的剖面標記；
        ///    自己的裁剪框元素 OwnerViewId＝剖面，不能拿來移）；
        /// ② 繞「目前原點」的鉛直軸 RotateElement 到新方向 → Regenerate → 讀回 → MoveElement 水平位移到新原點 → Regenerate；
        /// ③ 讀回實際 CropBox.Transform，確認 BasisX 與目標一致（&lt; 0.05°），在「實際座標系」內把意圖的 8 個世界角點換成
        ///    區域 Min/Max 再設 CropBox（Transform 用讀回值），Regenerate 後比對世界範圍（容許 5mm）；
        /// ④ 任一步做不到 → 剖面不在任何圖紙上才刪除重建（同名、重掛 ES 連結）；在圖紙上 → 不動範圍、回傳 null 並記原因。
        /// 回傳：生效的剖面（可能是重建後的新視圖）；null＝無法更新。
        /// </summary>
        private static ViewSection UpdateExistingSectionFrame(Document doc, ViewSection section, BoundingBoxXYZ intended,
            string sectionName, string cid, out string log)
        {
            var sb = new System.Text.StringBuilder();
            _lastSectionDeviationMm = double.NaN;
            Transform target = intended.Transform;
            XYZ targetX = new XYZ(target.BasisX.X, target.BasisX.Y, 0).Normalize();
            string failReason = null;
            // 2026-09-26 稽核：移動路徑包在 SubTransaction——中途失敗（方向不符／偏差 > 5 mm）時先回滾「轉了一半、移了一半」的 viewer，
            // 再走退路。舊版在圖紙上的剖面會帶著半移動狀態一起提交，下次又被 HasUserMovedSection 誤判為「使用者手動移過」而永久保留錯位。
            SubTransaction moveSt = null;
            try
            {
                moveSt = new SubTransaction(doc);
                moveSt.Start();
                Transform cur = section.CropBox.Transform;
                XYZ curX = new XYZ(cur.BasisX.X, cur.BasisX.Y, 0);
                if (curX.GetLength() < 1e-6) throw new InvalidOperationException("目前剖面 BasisX 退化（非鉛直剖面？）");
                curX = curX.Normalize();
                double ang = Math.Atan2(curX.CrossProduct(targetX).Z, curX.DotProduct(targetX));
                double moveMm = CameraMath.FeetToMm(Horizontal(target.Origin - cur.Origin).GetLength());
                sb.Append($"舊原點({Mm3(cur.Origin)}) 舊方向({curX.X:F3},{curX.Y:F3}) → 新原點({Mm3(target.Origin)}) 新方向({targetX.X:F3},{targetX.Y:F3})；Δ角={ang * 180 / Math.PI:F2}° 水平位移={moveMm:F0}mm；");

                if (Math.Abs(ang) > 1e-5 || moveMm > 0.5)
                {
                    ElementId viewerId = FindSectionViewerId(doc, section, out string how);
                    if (viewerId == null) throw new InvalidOperationException("找不到剖面的 viewer 元素");
                    sb.Append($"viewer={viewerId.GetIdValue()}（{how}）；");
                    if (Math.Abs(ang) > 1e-5)
                    {
                        Line axisLine = Line.CreateBound(cur.Origin, cur.Origin + XYZ.BasisZ);
                        ElementTransformUtils.RotateElement(doc, viewerId, axisLine, ang);
                        doc.Regenerate();
                        cur = section.CropBox.Transform;
                        sb.Append($"已旋轉；");
                    }
                    XYZ delta = Horizontal(target.Origin - cur.Origin);
                    if (delta.GetLength() > CameraMath.MmToFeet(0.5))
                    {
                        ElementTransformUtils.MoveElement(doc, viewerId, delta);
                        doc.Regenerate();
                        cur = section.CropBox.Transform;
                        sb.Append($"已移動；");
                    }
                }

                // 讀回實際座標系，確認方向（BasisX 水平分量與 BasisY 為上）一致
                XYZ actX = new XYZ(cur.BasisX.X, cur.BasisX.Y, 0);
                double angErr = actX.GetLength() < 1e-6 ? 180 : actX.Normalize().AngleTo(targetX) * 180 / Math.PI;
                double upErr = cur.BasisY.AngleTo(target.BasisY) * 180 / Math.PI;
                // 剖切平面位置：實際原點與目標原點沿法向（BasisZ）的距離——Min/Max 會換算，但記下來供查
                double normalOffMm = CameraMath.FeetToMm((cur.Origin - target.Origin).DotProduct(target.BasisZ));
                sb.Append($"讀回 原點({Mm3(cur.Origin)}) 方向誤差={angErr:F3}° 上方向誤差={upErr:F3}° 法向偏移={normalOffMm:F0}mm；");
                if (angErr > 0.05 || upErr > 0.05)
                    throw new InvalidOperationException($"移動/旋轉後方向仍不符（{angErr:F2}°/{upErr:F2}°）");

                // 2026-09-26 第四輪：深度。剖切面＝CropBox 區域 Max.Z（Revit 把剖切面正規化在 viewer 原點，
                // 無法像新建時那樣把 Max.Z 設在原點前方）→ 意圖深度 [dMin,dMax] 的 dMax≠0 時，先把 viewer 沿視圖
                // 深度軸再移 dMax，讓剖切面落在意圖前緣；之後在讀回座標系設 Min.Z＝dMin−dMax、Max.Z≈0。
                // 位移量與正負號一律由「讀回的實際座標系」算（意圖角點經 cur.Inverse 的 Z 上界 − 目前剖切面 Max.Z），不假設方向。
                double[] ext = LocalExtents(intended, cur);
                double curCutZ = section.CropBox.Max.Z;
                double depthShift = ext[5] - curCutZ; // 沿 cur.BasisZ（feet）
                sb.Append($"深度：意圖區域Z[{CameraMath.FeetToMm(ext[4]):F0},{CameraMath.FeetToMm(ext[5]):F0}]mm 目前剖切面Z={CameraMath.FeetToMm(curCutZ):F0}mm → 沿深度軸移 {CameraMath.FeetToMm(depthShift):F0}mm；");
                if (Math.Abs(depthShift) > CameraMath.MmToFeet(0.5))
                {
                    ElementId viewerId = FindSectionViewerId(doc, section, out string how2);
                    if (viewerId == null) throw new InvalidOperationException("找不到剖面的 viewer 元素（深度位移）");
                    ElementTransformUtils.MoveElement(doc, viewerId, Horizontal(cur.BasisZ) * depthShift);
                    doc.Regenerate();
                    cur = section.CropBox.Transform;
                    ext = LocalExtents(intended, cur);
                    sb.Append($"已沿深度移動（{how2}）讀回 原點({Mm3(cur.Origin)}) 意圖區域Z[{CameraMath.FeetToMm(ext[4]):F0},{CameraMath.FeetToMm(ext[5]):F0}]mm 剖切面Z={CameraMath.FeetToMm(section.CropBox.Max.Z):F0}mm；");
                }

                // 在實際座標系算區域範圍：意圖 8 角點（世界）→ 實際.Inverse
                section.CropBox = new BoundingBoxXYZ { Transform = cur, Min = new XYZ(ext[0], ext[2], ext[4]), Max = new XYZ(ext[1], ext[3], ext[5]) };
                SetSectionFarClip(section, ext[5] - ext[4], sb);
                sb.Append(DescribeSectionExtentCheck(doc, section, intended, out double dev));
                if (dev > SectionExtentToleranceMm)
                {
                    // 讀回校正一次：比較意圖與實際在「意圖座標系」的深度上界，差多少就沿意圖深度軸再移多少（正負號來自讀回值）
                    double[] a = LocalExtents(intended, intended.Transform), r = LocalExtents(section.CropBox, intended.Transform);
                    double fix = a[5] - r[5];
                    sb.Append($"深度校正：意圖Z上界−實際={CameraMath.FeetToMm(fix):F1}mm；");
                    if (Math.Abs(fix) > CameraMath.MmToFeet(0.5))
                    {
                        ElementId viewerId = FindSectionViewerId(doc, section, out _);
                        if (viewerId == null) throw new InvalidOperationException("找不到剖面的 viewer 元素（深度校正）");
                        ElementTransformUtils.MoveElement(doc, viewerId, Horizontal(intended.Transform.BasisZ) * fix);
                        doc.Regenerate();
                        cur = section.CropBox.Transform;
                        ext = LocalExtents(intended, cur);
                        section.CropBox = new BoundingBoxXYZ { Transform = cur, Min = new XYZ(ext[0], ext[2], ext[4]), Max = new XYZ(ext[1], ext[3], ext[5]) };
                        SetSectionFarClip(section, ext[5] - ext[4], sb);
                        sb.Append(DescribeSectionExtentCheck(doc, section, intended, out dev));
                    }
                }
                if (dev > SectionExtentToleranceMm)
                    throw new InvalidOperationException($"世界範圍偏差 {dev:F1}mm > {SectionExtentToleranceMm}mm");
                moveSt.Commit();
                log = sb.ToString();
                Logger.Info($"相機 Key Plan：更新既有剖面「{section.Name}」 {log}");
                return section;
            }
            catch (Exception ex)
            {
                failReason = ex.Message;
                try
                {
                    if (moveSt != null && moveSt.HasStarted() && !moveSt.HasEnded()) { moveSt.RollBack(); sb.Append("已回滾部分移動；"); }
                }
                catch (Exception exRb) { Logger.Error("相機 Key Plan：回滾剖面部分移動失敗", exRb); }
                sb.Append($"移動路徑失敗：{ex.Message}；");
                Logger.Error($"相機 Key Plan：更新既有剖面「{section.Name}」的位置失敗", ex);
            }
            finally
            {
                moveSt?.Dispose();
            }

            // ④ 退路：不在圖紙上 → 刪除重建；在圖紙上 → 回報
            List<string> sheets = SheetsHostingView(doc, section.Id);
            if (sheets.Count > 0)
            {
                sb.Append($"剖面已放在圖紙（{string.Join("、", sheets)}）上，不刪除重建；範圍未更新，請按「重設剖面位置」或手動調整。");
                log = sb.ToString();
                AddCropWarning($"Key Plan 剖面「{section.Name}」無法移到新位置（{failReason}），且已放在圖紙上，未刪除重建——範圍未更新。");
                return null;
            }
            try
            {
                ElementId oldId = section.Id;
                doc.Delete(oldId);
                ViewSection created = CreateSectionFromBox(doc, intended, sectionName, cid);
                sb.Append($"已刪除舊剖面 {oldId.GetIdValue()} 並重建為 {created.Id.GetIdValue()}；");
                sb.Append(DescribeSectionExtentCheck(doc, created, intended, out _));
                log = sb.ToString();
                Logger.Info($"相機 Key Plan：剖面「{sectionName}」刪除重建 {log}");
                return created;
            }
            catch (Exception ex2)
            {
                sb.Append($"刪除重建也失敗：{ex2.Message}（範圍未更新）");
                log = sb.ToString();
                AddCropWarning($"Key Plan 剖面「{sectionName}」無法更新到新位置：{ex2.Message}");
                Logger.Error("相機 Key Plan：剖面刪除重建失敗", ex2);
                return null;
            }
        }

        /// <summary>剖面的 viewer（其他視圖裡的剖面標記；移動/旋轉它＝移動剖面）。自己的裁剪框元素（OwnerViewId＝剖面）排除。</summary>
        private static ElementId FindSectionViewerId(Document doc, ViewSection section, out string how)
        {
            how = null;
            try
            {
                IList<ElementId> deps = section.GetDependentElements(new ElementCategoryFilter(BuiltInCategory.OST_Viewers));
                if (deps != null)
                {
                    foreach (ElementId d in deps)
                    {
                        if (d == section.Id) continue;
                        Element e = doc.GetElement(d);
                        if (e == null || e.OwnerViewId == section.Id) continue;
                        how = "GetDependentElements";
                        return d;
                    }
                }
            }
            catch (Exception ex) { Logger.Error("相機 Key Plan：找剖面 viewer（GetDependentElements）失敗，改用名稱比對", ex); }
            foreach (Element e in new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Viewers).WhereElementIsNotElementType())
            {
                if (e.Id == section.Id || e.OwnerViewId == section.Id) continue;
                if (e.Name == section.Name) { how = "Name"; return e.Id; }
            }
            return null;
        }

        private static List<string> SheetsHostingView(Document doc, ElementId viewId)
        {
            var names = new List<string>();
            foreach (Viewport vp in new FilteredElementCollector(doc).OfClass(typeof(Viewport)).Cast<Viewport>())
            {
                if (vp.ViewId != viewId) continue;
                if (doc.GetElement(vp.SheetId) is ViewSheet s) names.Add(s.SheetNumber + " " + s.Name);
            }
            return names;
        }

        private static XYZ Horizontal(XYZ v) => new XYZ(v.X, v.Y, 0);

        /// <summary>框 b 的 8 個世界角點投到座標系 frame 的區域範圍：[minX,maxX,minY,maxY,minZ,maxZ]（feet）。</summary>
        private static double[] LocalExtents(BoundingBoxXYZ b, Transform frame)
        {
            Transform inv = frame.Inverse;
            double[] r = { double.MaxValue, double.MinValue, double.MaxValue, double.MinValue, double.MaxValue, double.MinValue };
            foreach (XYZ w in WorldCorners(b))
            {
                XYZ l = inv.OfPoint(w);
                r[0] = Math.Min(r[0], l.X); r[1] = Math.Max(r[1], l.X);
                r[2] = Math.Min(r[2], l.Y); r[3] = Math.Max(r[3], l.Y);
                r[4] = Math.Min(r[4], l.Z); r[5] = Math.Max(r[5], l.Z);
            }
            return r;
        }

        /// <summary>2026-09-26 第四輪：移動路徑的遠裁剪偏移＝意圖深度（dMax−dMin）；參數唯讀／被樣板鎖住只記 log。</summary>
        private static void SetSectionFarClip(ViewSection section, double depthFeet, System.Text.StringBuilder sb)
        {
            try
            {
                if (!(depthFeet > 1e-6)) return;
                Parameter p = section.get_Parameter(BuiltInParameter.VIEWER_BOUND_OFFSET_FAR);
                if (p == null || p.IsReadOnly) { sb.Append("遠裁剪偏移不可寫；"); return; }
                if (Math.Abs(p.AsDouble() - depthFeet) > CameraMath.MmToFeet(0.5)) p.Set(depthFeet);
                sb.Append($"遠裁剪偏移={CameraMath.FeetToMm(p.AsDouble()):F0}mm；");
            }
            catch (Exception ex) { sb.Append($"遠裁剪偏移設定失敗：{ex.Message}；"); }
        }

        private static string Mm3(XYZ p) => $"{CameraMath.FeetToMm(p.X):F0},{CameraMath.FeetToMm(p.Y):F0},{CameraMath.FeetToMm(p.Z):F0}";

        private static IEnumerable<XYZ> WorldCorners(BoundingBoxXYZ b)
        {
            Transform tf = b.Transform ?? Transform.Identity;
            foreach (double x in new[] { b.Min.X, b.Max.X })
                foreach (double y in new[] { b.Min.Y, b.Max.Y })
                    foreach (double z in new[] { b.Min.Z, b.Max.Z })
                        yield return tf.OfPoint(new XYZ(x, y, z));
        }

        /// <summary>剖面世界範圍後檢：把意圖框與讀回框的 8 個世界角點都投到「意圖座標系」，比較 X（沿剖面）、
        /// Y（高程）、Z（深度）三軸的 min/max，取最大偏差（mm）。Regenerate 後讀回。</summary>
        private static string DescribeSectionExtentCheck(Document doc, ViewSection section, BoundingBoxXYZ intended, out double devMm)
        {
            devMm = double.NaN;
            try
            {
                doc.Regenerate();
                BoundingBoxXYZ act = section.CropBox;
                Transform inv = intended.Transform.Inverse;
                double[] Ext(BoundingBoxXYZ b)
                {
                    double[] r = { double.MaxValue, double.MinValue, double.MaxValue, double.MinValue, double.MaxValue, double.MinValue };
                    foreach (XYZ w in WorldCorners(b))
                    {
                        XYZ l = inv.OfPoint(w);
                        r[0] = Math.Min(r[0], l.X); r[1] = Math.Max(r[1], l.X);
                        r[2] = Math.Min(r[2], l.Y); r[3] = Math.Max(r[3], l.Y);
                        r[4] = Math.Min(r[4], l.Z); r[5] = Math.Max(r[5], l.Z);
                    }
                    return r;
                }
                double[] a = Ext(intended), b2 = Ext(act);
                double dXY = 0, dZ = 0;
                for (int i = 0; i < 4; i++) dXY = Math.Max(dXY, Math.Abs(CameraMath.FeetToMm(a[i] - b2[i])));
                for (int i = 4; i < 6; i++) dZ = Math.Max(dZ, Math.Abs(CameraMath.FeetToMm(a[i] - b2[i])));
                // 世界高程（剖面 Y 軸＝世界上）
                double wz0 = double.MaxValue, wz1 = double.MinValue;
                foreach (XYZ w in WorldCorners(act)) { wz0 = Math.Min(wz0, w.Z); wz1 = Math.Max(wz1, w.Z); }
                double iz0 = double.MaxValue, iz1 = double.MinValue;
                foreach (XYZ w in WorldCorners(intended)) { iz0 = Math.Min(iz0, w.Z); iz1 = Math.Max(iz1, w.Z); }
                devMm = Math.Max(dXY, dZ);
                _lastSectionDeviationMm = devMm;
                string s = $"世界範圍後檢：沿剖面/高程最大偏差={dXY:F1}mm、深度偏差={dZ:F1}mm；世界Z 意圖[{CameraMath.FeetToMm(iz0):F0},{CameraMath.FeetToMm(iz1):F0}] 實際[{CameraMath.FeetToMm(wz0):F0},{CameraMath.FeetToMm(wz1):F0}]mm";
                if (devMm > SectionExtentToleranceMm)
                {
                    s += $"（> {SectionExtentToleranceMm}mm）";
                    AddCropWarning($"Key Plan 剖面「{section.Name}」世界範圍與意圖差 {devMm:F1}mm（沿剖面/高程 {dXY:F1}、深度 {dZ:F1}）");
                }
                return s;
            }
            catch (Exception ex)
            {
                return "世界範圍後檢失敗：" + ex.Message;
            }
        }

        /// <summary>
        /// 「重設剖面位置」：無條件把既有剖面（若存在）的 CropBox 強制覆寫回目前相機的視線
        /// 位置，並補畫標記。呼叫端須自行包在 ExternalEvent + Transaction 內（沿用既有
        /// <see cref="CreateOrUpdateFourViews"/> 的呼叫慣例，這裡直接重用整套流程、只是把
        /// forceResetSection 設為 true）。
        /// </summary>
        internal static KeyPlanResult ResetSectionToSightLine(UIApplication uiApp, Document doc, ElementId cameraViewId)
            => CreateOrUpdateFourViews(uiApp, doc, cameraViewId, forceResetSection: true);

        /// <summary>
        /// 供迷你 key plan（KeyPlanSection）疊畫「目前剖面線」用：讀取這台相機目前連結的剖面
        /// 視圖，回傳其 CropBox 沿剖面本身 X 軸（＝視線水平方向）的兩個端點（模型座標，Z 分量
        /// 取自 CropBox.Transform，供 2D 平面投影時忽略即可）。找不到連結剖面時回傳 false——
        /// 這是唯讀查詢，不開 Transaction，呼叫端仍需經 ExternalEventManager 排程（因為要讀
        /// Document）。回傳的線段反映使用者手動調整後的實際剖面位置（不是程式原本算出來的
        /// 視線），符合「迷你地圖上的剖面線要跟著使用者調整」的需求。
        /// </summary>
        /// <summary>回傳目前文件中「已經有 Key Plan 剖面」的相機 UniqueId 集合（唯讀，只掃
        /// ViewSection）。供快照標記 CameraRow.HasKeyPlanSection，面板據此對沒有剖面的相機完全
        /// 不排 RefreshSelectedSectionLine。</summary>
        internal static HashSet<string> GetCameraUniqueIdsWithSection(Document doc)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            if (doc == null || Schema.Lookup(SchemaGuid) == null) return set;
            foreach (ViewSection v in new FilteredElementCollector(doc).OfClass(typeof(ViewSection)).Cast<ViewSection>())
            {
                if (v.IsTemplate) continue;
                if (TryReadLink(v, out string cid, out string role) && role == "Section" && !string.IsNullOrEmpty(cid))
                    set.Add(cid);
            }
            return set;
        }

        /// <summary>「④ 全部符合畫面」用：這台相機（UniqueId）以 ES 連結的 Key Plan 平面／剖面／等角視圖 Id（唯讀）。</summary>
        internal static List<ElementId> GetLinkedKeyPlanViewIds(Document doc, string cameraUniqueId)
        {
            var ids = new List<ElementId>();
            if (doc == null || string.IsNullOrEmpty(cameraUniqueId)) return ids;
            foreach (string role in new[] { "Plan", "Section", "Iso" })
            {
                View v = FindLinkedView(doc, cameraUniqueId, role);
                if (v != null) ids.Add(v.Id);
            }
            return ids;
        }

        internal static bool TryGetSectionLineForCamera(Document doc, string cameraUniqueId, out XYZ p1, out XYZ p2)
        {
            p1 = null;
            p2 = null;
            if (doc == null || string.IsNullOrEmpty(cameraUniqueId)) return false;

            View linked = FindLinkedView(doc, cameraUniqueId, "Section");
            if (!(linked is ViewSection section)) return false;

            try
            {
                BoundingBoxXYZ box = section.CropBox;
                Transform tf = box.Transform;
                p1 = tf.OfPoint(new XYZ(box.Min.X, 0, 0));
                p2 = tf.OfPoint(new XYZ(box.Max.X, 0, 0));
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 剖面座標系：X＝沿視線水平方向（forwardHoriz）、Y＝世界 Z（上）、
        /// Z＝BasisX×BasisY（看向的方向，即「從側面看」的深度軸）。Origin 放在相機眼睛，
        /// 所以剖面平面剛好包含視線水平投影（規格「cut plane contains camera's horizontal
        /// sight line」）。範圍：X 從相機背後一點到遠裁剪之外一點；Y 從樓層下方 1m 到眼睛上方
        /// 3m（規格 §5.1）；Z（厚度）前後各 1.5m（未查證的假設值，見檔案頂端常數註解）。
        /// </summary>
        private static BoundingBoxXYZ BuildSectionBox(CameraMath.CameraGeometry geom, SectionSpec spec)
        {
            XYZ up = XYZ.BasisZ;
            XYZ axis = spec.Axis;
            XYZ viewDir = axis.CrossProduct(up);
            if (viewDir.GetLength() < 1e-6) viewDir = XYZ.BasisX;
            viewDir = viewDir.Normalize();

            Transform tf = Transform.Identity;
            tf.Origin = spec.Origin ?? geom.Eye;
            tf.BasisX = axis;
            tf.BasisY = up;
            tf.BasisZ = viewDir;

            double halfDepth = Cm(SectionDepthHalfCm);
            double minZ = double.IsNaN(spec.MinZ) ? -halfDepth : spec.MinZ;
            double maxZ = double.IsNaN(spec.MaxZ) ? halfDepth : spec.MaxZ;
            return new BoundingBoxXYZ
            {
                Transform = tf,
                Min = new XYZ(spec.MinX, spec.MinY, minZ),
                Max = new XYZ(spec.MaxX, spec.MaxY, maxZ)
            };
        }

        /// <summary>剖面規格（交易外算好）：X 軸方向（水平）、以眼睛為原點的 X/Y 範圍（feet）。</summary>
        internal sealed class SectionSpec
        {
            public XYZ Axis;
            /// <summary>2026-09-25：剖面原點（剖切平面通過的點）。沿視線＝眼睛；對齊房間長邊＝畫面中心線上
            /// 半個扇形半徑處（Z＝眼睛高）。null＝眼睛。MinX/MaxX/MinY/MaxY 都是相對此原點。</summary>
            public XYZ Origin;
            /// <summary>深度（剖面 Z）範圍；預設 ±1.5m，眼睛不在範圍內時外擴讓眼睛仍在深度內。</summary>
            public double MinZ = double.NaN, MaxZ = double.NaN;
            public double MinX, MaxX, MinY, MaxY;
            public CameraSectionOrientationMode ModeUsed;
            public string Log;
        }

        /// <summary>2026-09-26 第五輪：剖面位置——單次覆寫（MCP 工具）優先，否則面板設定（預設相機位置）。</summary>
        private static CameraSectionAnchorMode GetSectionAnchorMode()
        {
            if (_runOptions?.SectionAnchor != null) return _runOptions.SectionAnchor.Value;
            try { return ConfigManager.Instance.Settings.CameraManager.SectionAnchor; }
            catch { return CameraSectionAnchorMode.Camera; }
        }

        /// <summary>面板目前設定的剖面位置（唯讀，list_cameras 用）。</summary>
        internal static CameraSectionAnchorMode GetConfiguredSectionAnchorMode() => GetSectionAnchorMode();

        private static CameraSectionOrientationMode GetSectionOrientationMode()
        {
            if (_runOptions?.SectionMode != null) return _runOptions.SectionMode.Value;
            try { return ConfigManager.Instance.Settings.CameraManager.SectionOrientationMode; }
            catch { return CameraSectionOrientationMode.AlongRoomLongAxis; }
        }

        /// <summary>等角範圍模式：單次覆寫（MCP 工具）優先，否則面板設定。</summary>
        private static CameraIsoNearWallsMode GetIsoNearWallsMode()
        {
            if (_runOptions?.IsoNearWalls != null) return _runOptions.IsoNearWalls.Value;
            try { return ConfigManager.Instance.Settings.CameraManager.IsoNearWalls; }
            catch { return CameraIsoNearWallsMode.Camera; }
        }

        /// <summary>
        /// 2026-09-26（使用者：等角從角落看，最靠近觀看者的兩道牆擋住室內，例如電梯側那道牆；D-038）：
        /// 室內相機的等角 forward＝(sx, sy, −1)/√3，觀看者在 −sx／−sy 側 → 近側＝X：sx&gt;0 ? MinX : MaxX、
        /// Y：sy&gt;0 ? MinY : MaxY。這兩面改成「眼睛所在房間邊界線外接框（平面、軸向）往內 100 mm」——房間邊界＝
        /// 牆內面，所以牆被切掉；遠側兩面維持房間 +0.6 m（牆保留），Z 不動。室外相機／找不到房間／Keep＝不動。
        /// 直接改 box（Transform＝Identity 的世界座標框），回傳 log 字串。
        /// </summary>
        private static string ApplyIsoNearWallCut(BoundingBoxXYZ box, RoomCtx room, XYZ forwardHoriz, CameraIsoNearWallsMode mode, XYZ eye = null)
        {
            if (mode == CameraIsoNearWallsMode.Keep) return "mode=Keep（保留，不切）";
            if (mode == CameraIsoNearWallsMode.Camera) return ApplyIsoNearCutAtCamera(box, room, forwardHoriz, eye);
            if (room?.Room == null || room.Pts == null || room.Pts.Count < 3) return "mode=Wall 但無眼睛所在房間／室外 → 不切";
            // 房間邊界線外接框（只用邊界點；不含最近房間 fallback 時為了含眼睛而外擴的 rc.MinX 等）
            double rx0 = room.Pts.Min(q => q.X), rx1 = room.Pts.Max(q => q.X);
            double ry0 = room.Pts.Min(q => q.Y), ry1 = room.Pts.Max(q => q.Y);
            XYZ fh = forwardHoriz != null ? new XYZ(forwardHoriz.X, forwardHoriz.Y, 0) : XYZ.BasisY;
            fh = fh.GetLength() > 1e-6 ? fh.Normalize() : XYZ.BasisY;
            XYZ f = CameraMath.SnapToIsoCorner(fh, out double cornerYaw);
            bool sxPos = f.X > 0, syPos = f.Y > 0;
            double inset = CameraMath.MmToFeet(IsoNearWallInsetMm);
            XYZ min = box.Min, max = box.Max;
            double nMinX = min.X, nMaxX = max.X, nMinY = min.Y, nMaxY = max.Y;
            string xSide, ySide;
            if (sxPos) { nMinX = rx0 + inset; xSide = "MinX"; } else { nMaxX = rx1 - inset; xSide = "MaxX"; }
            if (syPos) { nMinY = ry0 + inset; ySide = "MinY"; } else { nMaxY = ry1 - inset; ySide = "MaxY"; }
            if (!(nMinX < nMaxX) || !(nMinY < nMaxY))
                return $"mode=Wall 但房間太小（內縮後 min≥max）→ 不切 room X[{CameraMath.FeetToMm(rx0):F0},{CameraMath.FeetToMm(rx1):F0}] Y[{CameraMath.FeetToMm(ry0):F0},{CameraMath.FeetToMm(ry1):F0}]mm";
            box.Min = new XYZ(nMinX, nMinY, min.Z);
            box.Max = new XYZ(nMaxX, nMaxY, max.Z);
            return $"mode=Wall corner yaw={cornerYaw:F0}° forward=({f.X:F3},{f.Y:F3},{f.Z:F3}) 切 {xSide}＋{ySide}（房間邊界內縮 {IsoNearWallInsetMm:F0}mm）" +
                   $" room X[{CameraMath.FeetToMm(rx0):F0},{CameraMath.FeetToMm(rx1):F0}] Y[{CameraMath.FeetToMm(ry0):F0},{CameraMath.FeetToMm(ry1):F0}]mm" +
                   $" → box X[{CameraMath.FeetToMm(nMinX):F0},{CameraMath.FeetToMm(nMaxX):F0}] Y[{CameraMath.FeetToMm(nMinY):F0},{CameraMath.FeetToMm(nMaxY):F0}]mm" +
                   $"（原 X[{CameraMath.FeetToMm(min.X):F0},{CameraMath.FeetToMm(max.X):F0}] Y[{CameraMath.FeetToMm(min.Y):F0},{CameraMath.FeetToMm(max.Y):F0}]mm）";
        }

        /// <summary>
        /// 2026-09-26 第四輪（使用者決策，D-039，取代 D-038 的預設）：等角近側「相機位置」——觀看者側兩面剛好放在
        /// 眼睛的 X／Y（不留後退距離，避免在牆後面誤切）。同 D-038：forward＝(sx, sy, −1)/√3，近側＝X：sx&gt;0 ? MinX : MaxX、
        /// Y：sy&gt;0 ? MinY : MaxY。夾限：新面不得越過對面（留 ≥ 100 mm），眼睛在框外（觀看側之外）時該軸不動（不會把框撐大）。
        /// 只限室內相機（room!=null）；室外的等角本來就是扇形框，不切。
        /// </summary>
        private static string ApplyIsoNearCutAtCamera(BoundingBoxXYZ box, RoomCtx room, XYZ forwardHoriz, XYZ eye)
        {
            if (room?.Room == null) return "mode=Camera 但室外／無眼睛所在房間 → 不切";
            if (eye == null) return "mode=Camera 但沒有眼睛座標 → 不切";
            XYZ fh = forwardHoriz != null ? new XYZ(forwardHoriz.X, forwardHoriz.Y, 0) : XYZ.BasisY;
            fh = fh.GetLength() > 1e-6 ? fh.Normalize() : XYZ.BasisY;
            XYZ f = CameraMath.SnapToIsoCorner(fh, out double cornerYaw);
            bool sxPos = f.X > 0, syPos = f.Y > 0;
            double gap = CameraMath.MmToFeet(100.0);
            XYZ min = box.Min, max = box.Max;
            double nMinX = min.X, nMaxX = max.X, nMinY = min.Y, nMaxY = max.Y;
            string xs, ys;
            if (sxPos)
            {
                if (eye.X > min.X) { nMinX = Math.Min(eye.X, max.X - gap); xs = "MinX→眼睛X"; } else xs = "MinX 不動（眼睛在框外）";
            }
            else
            {
                if (eye.X < max.X) { nMaxX = Math.Max(eye.X, min.X + gap); xs = "MaxX→眼睛X"; } else xs = "MaxX 不動（眼睛在框外）";
            }
            if (syPos)
            {
                if (eye.Y > min.Y) { nMinY = Math.Min(eye.Y, max.Y - gap); ys = "MinY→眼睛Y"; } else ys = "MinY 不動（眼睛在框外）";
            }
            else
            {
                if (eye.Y < max.Y) { nMaxY = Math.Max(eye.Y, min.Y + gap); ys = "MaxY→眼睛Y"; } else ys = "MaxY 不動（眼睛在框外）";
            }
            if (!(nMinX < nMaxX) || !(nMinY < nMaxY)) return "mode=Camera 但夾限後 min≥max → 不切";
            box.Min = new XYZ(nMinX, nMinY, min.Z);
            box.Max = new XYZ(nMaxX, nMaxY, max.Z);
            return $"mode=Camera corner yaw={cornerYaw:F0}° eye=({CameraMath.FeetToMm(eye.X):F0},{CameraMath.FeetToMm(eye.Y):F0})mm {xs}＋{ys}" +
                   $" → box X[{CameraMath.FeetToMm(nMinX):F0},{CameraMath.FeetToMm(nMaxX):F0}] Y[{CameraMath.FeetToMm(nMinY):F0},{CameraMath.FeetToMm(nMaxY):F0}]mm" +
                   $"（原 X[{CameraMath.FeetToMm(min.X):F0},{CameraMath.FeetToMm(max.X):F0}] Y[{CameraMath.FeetToMm(min.Y):F0},{CameraMath.FeetToMm(max.Y):F0}]mm）";
        }

        private static CameraIsoExtentMode GetIsoExtentMode()
        {
            if (_runOptions?.IsoMode != null) return _runOptions.IsoMode.Value;
            try { return ConfigManager.Instance.Settings.CameraManager.IsoExtentMode; }
            catch { return CameraIsoExtentMode.FullWithUpperBeams; }
        }

        /// <summary>面板目前設定的剖面方向（唯讀，list_cameras 用）。</summary>
        internal static CameraSectionOrientationMode GetConfiguredSectionOrientationMode() => GetSectionOrientationMode();

        /// <summary>這台相機以 ES 連結的 Key Plan 視圖（role＝Plan／Section／Iso）；沒有＝null。唯讀。</summary>
        internal static View FindLinkedKeyPlanView(Document doc, string cameraUniqueId, string role)
        {
            if (doc == null || string.IsNullOrEmpty(cameraUniqueId) || Schema.Lookup(SchemaGuid) == null) return null;
            return FindLinkedView(doc, cameraUniqueId, role);
        }

        /// <summary>剖面是否被使用者手動移過（與一鍵 4 視圖保留判定同一個比對；沒有 ES 狀態＝false）。唯讀。</summary>
        internal static bool IsSectionManuallyMoved(ViewSection section)
            => section != null && HasUserMovedSection(section, null, null);

        /// <summary>視圖內貼了本相機 ES 標記的元素數，依 Role 分組（MarkerPlan／MarkerSection／LevelMark…）。唯讀。</summary>
        internal static Dictionary<string, int> CountTaggedElementsByRole(Document doc, View view, string cameraUniqueId)
        {
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            if (doc == null || view == null || Schema.Lookup(SchemaGuid) == null) return counts;
            foreach (Element e in new FilteredElementCollector(doc, view.Id).WhereElementIsNotElementType())
            {
                if (!TryReadLink(e, out string cid, out string role) || cid != cameraUniqueId || string.IsNullOrEmpty(role)) continue;
                counts[role] = counts.TryGetValue(role, out int n) ? n + 1 : 1;
            }
            return counts;
        }

        /// <summary>眼睛所在房間（與一鍵 4 視圖同一個 FindEyeRoom）；找不到＝roomId null。唯讀，須在 API context。</summary>
        internal static void DescribeEyeRoom(View3D camera, CameraMath.CameraGeometry geom, out long? roomId, out string roomName, out string log)
        {
            roomId = null; roomName = null; log = null;
            if (camera == null || geom == null) { log = "no camera"; return; }
            RoomCtx rc = FindEyeRoom(camera.Document, camera, geom);
            log = rc?.Log;
            if (rc?.Room != null) { roomId = rc.Room.Id.GetIdValue(); roomName = rc.Room.Name; }
        }

        /// <summary>
        /// 2026-09-25 第五輪（規格 §5.1）：
        ///   方向——對齊房間長邊（預設）：房間長軸、通過眼睛；方向號誌取「與相機水平前方向同側」（剖面上視線箭頭朝右）。
        ///           沿視線：水平視線方向（舊規則）。找不到房間 → 一律沿視線。
        ///   水平範圍——對齊房間：房間頂點沿長軸投影範圍 ±0.6m；沿視線：視線穿越本房間 BoundingBox 的區段 ±0.6m
        ///           （眼睛後方至少 1m）；沒有房間：[−1m, 扇形半徑 +0.6m]。
        ///   垂直範圍——下限＝樓層 −1m；上限＝房間頂 +0.5m（無房間：上層樓板底 +0.5m，算不出來＝眼睛 +3m）；
        ///           並確保包含 Key Plan 平面的剖切面高程（否則剖面線在平面上不顯示），不含就外擴並記 log。
        /// 任何一步失敗只記 log、退回較簡單的規則。
        /// </summary>
        private static SectionSpec ComputeSectionSpec(Document doc, CameraMath.CameraGeometry geom, CameraParams cp, IsoXY isoXY, StoreyTop storeyTop,
            CameraSectionOrientationMode mode, XYZ planBasisX, double? cutPlaneZ,
            CameraSectionAnchorMode anchor = CameraSectionAnchorMode.Center, bool rotate90 = false)
        {
            // 2026-09-25：「沿視線」一律沿畫面中心線（水平視錐左右緣角平分線），不是相機正前方。
            XYZ center = cp.CenterHoriz ?? cp.ForwardHoriz;
            var s = new SectionSpec { Axis = center, ModeUsed = CameraSectionOrientationMode.AlongSightLine, Origin = geom.Eye };
            double buf = CameraMath.MmToFeet(RoomBufferMm);
            var log = new System.Text.StringBuilder();
            log.Append($"中心線 yaw={cp.CenterYawDeg:F1}° ");
            bool done = false;

            if (cp.IsOutdoor)
            {
                // 2026-09-25 室外規則：一律沿視線（忽略「對齊房間長邊」設定）；水平＝眼睛後 2m … 扇形半徑。
                s.Axis = center; s.ModeUsed = CameraSectionOrientationMode.AlongSightLine;
                s.MinX = -CameraMath.MmToFeet(OutdoorBehindEyeMm);
                s.MaxX = Math.Max(cp.FanRadiusFeet, Cm(100));
                log.Append($"OUTDOOR mode=沿視線（室外強制，設定={mode}）X[{CameraMath.FeetToMm(s.MinX):F0},{CameraMath.FeetToMm(s.MaxX):F0}]mm");
                done = true;
            }

            if (!done && mode == CameraSectionOrientationMode.AlongRoomLongAxis)
            {
                try
                {
                    if (cp.Room?.Room != null && cp.Room.Pts.Count >= 3 &&
                        TryRoomLongAxis(cp.Room, geom.Eye, planBasisX, center, out XYZ axis, out double t0, out double t1, out string axLog))
                    {
                        s.Axis = axis; s.ModeUsed = CameraSectionOrientationMode.AlongRoomLongAxis;
                        s.MinX = t0 - buf; s.MaxX = t1 + buf;
                        log.Append($"mode=對齊房間長邊 {axLog} X[{CameraMath.FeetToMm(s.MinX):F0},{CameraMath.FeetToMm(s.MaxX):F0}]mm(相對眼睛)");
                        // 2026-09-25：剖切線改通過「可見範圍中心」＝中心線上半個扇形半徑處（半徑已夾在房間 +0.6m 內），
                        // 方向不變；X 範圍改成相對新原點；眼睛若跑出剖面深度範圍就外擴深度，讓眼睛標記仍在範圍內。
                        try
                        {
                            double half = Math.Max(0.0, cp.FanRadiusFeet) / 2.0;
                            XYZ c = geom.Eye + center * half;
                            XYZ origin = new XYZ(c.X, c.Y, geom.Eye.Z);
                            double shift = (origin - geom.Eye).DotProduct(axis);
                            s.Origin = origin;
                            s.MinX -= shift; s.MaxX -= shift;
                            XYZ viewDir = axis.CrossProduct(XYZ.BasisZ);
                            viewDir = viewDir.GetLength() > 1e-6 ? viewDir.Normalize() : XYZ.BasisX;
                            double d = (geom.Eye - origin).DotProduct(viewDir);
                            double hd = Cm(SectionDepthHalfCm), dm = Cm(50);
                            s.MinZ = Math.Min(-hd, d - dm);
                            s.MaxZ = Math.Max(hd, d + dm);
                            log.Append($" → 原點＝中心線 {CameraMath.FeetToMm(half):F0}mm 處 ({CameraMath.FeetToMm(origin.X):F0},{CameraMath.FeetToMm(origin.Y):F0})mm " +
                                       $"X[{CameraMath.FeetToMm(s.MinX):F0},{CameraMath.FeetToMm(s.MaxX):F0}]mm 眼睛深度={CameraMath.FeetToMm(d):F0}mm " +
                                       $"深度[{CameraMath.FeetToMm(s.MinZ):F0},{CameraMath.FeetToMm(s.MaxZ):F0}]mm" +
                                       (Math.Abs(d) > hd - dm ? "（已外擴含眼睛）" : ""));
                        }
                        catch (Exception exC)
                        {
                            Logger.Error("相機 Key Plan：剖面原點移到可見範圍中心失敗，維持通過眼睛", exC);
                            s.Origin = geom.Eye; s.MinX = t0 - buf; s.MaxX = t1 + buf; s.MinZ = s.MaxZ = double.NaN;
                        }
                        done = true;
                    }
                    else log.Append("mode=對齊房間長邊 → 無房間/長軸算不出 → 退回沿視線; ");
                }
                catch (Exception ex)
                {
                    Logger.Error("相機 Key Plan：房間長軸計算失敗，剖面退回沿視線", ex);
                    log.Append("mode=對齊房間長邊 失敗 → 退回沿視線; ");
                }
            }

            if (!done)
            {
                s.Axis = center; s.ModeUsed = CameraSectionOrientationMode.AlongSightLine; s.Origin = geom.Eye;
                if (isoXY != null && isoXY.HasSightRange)
                {
                    s.MinX = Math.Min(isoXY.SightMinT - buf, -Cm(SectionBackMarginCm));
                    s.MaxX = Math.Max(isoXY.SightMaxT + buf, Cm(100));
                    log.Append($"mode=沿視線 sight[{CameraMath.FeetToMm(isoXY.SightMinT):F0},{CameraMath.FeetToMm(isoXY.SightMaxT):F0}]mm ±0.6m → X[{CameraMath.FeetToMm(s.MinX):F0},{CameraMath.FeetToMm(s.MaxX):F0}]mm");
                }
                else
                {
                    s.MinX = -Cm(SectionBackMarginCm);
                    s.MaxX = cp.FanRadiusFeet + buf;
                    log.Append($"mode=沿視線 (no sight range) X[{CameraMath.FeetToMm(s.MinX):F0},{CameraMath.FeetToMm(s.MaxX):F0}]mm");
                }
            }

            // 2026-09-27（使用者：「有時候會想看另一向的剖面」）：單一相機的「剖面轉 90°」開關——把目前模式
            // 決定的軸（含房間長軸模式已經把原點移到中心線那一步）繞垂直軸轉 90°，水平範圍沿新軸重新投影。
            // 一定要在 anchor=Camera 區塊（原點改回眼睛、看向側翻轉、深度）之前做，這樣兩者才能疊加生效；
            // anchor=Center 則直接沿用這裡轉好的軸與範圍（維持舊行為，只是方向換了）。
            if (rotate90) ApplyRotate90(s, cp, geom, isoXY);

            // 2026-09-26 第五輪（使用者：剖面預設要剛好通過相機位置）：剖面位置＝相機位置 → 原點＝眼睛（方向沿用上面的模式），
            // 水平範圍改成相對眼睛；深度：剖切面（區域 Max.Z）＝0（通過眼睛），往看向側（−BasisZ）延伸扇形半徑（至少 1 m）。
            // 對齊房間長邊時若扇形中心線落在剖切面背後（看向側的反側），把軸反向（範圍對調），讓相機前方的扇形在剖面裡看得到。
            if (anchor == CameraSectionAnchorMode.Camera)
            {
                try
                {
                    XYZ o0 = s.Origin ?? geom.Eye;
                    XYZ ax = s.Axis;
                    double sh = (geom.Eye - o0).DotProduct(ax);
                    double x0 = s.MinX - sh, x1 = s.MaxX - sh;
                    XYZ look = XYZ.BasisZ.CrossProduct(ax); // 看向＝−BasisZ＝−(axis×up)＝up×axis
                    bool flipped = false;
                    if (look.GetLength() > 1e-6 && center != null && center.DotProduct(look.Normalize()) < -1e-6)
                    {
                        ax = ax.Negate();
                        double a = x0; x0 = -x1; x1 = -a;
                        flipped = true;
                    }
                    // 2026-09-27（MCP 實測，D-044 修正①：Kitchen 01 轉 90° 後只看到牆／衛浴，看不到房間內部）：
                    // 深度要用「翻轉之後」的最終看向（BuildSectionBox 用最終 ax 重算 viewDir＝ax×up，深度往
                    // −viewDir＝up×ax 延伸）；flip 前的 look 方向可能已經反過來，不能直接拿來投影。
                    XYZ lookFinal = XYZ.BasisZ.CrossProduct(ax);
                    lookFinal = lookFinal.GetLength() > 1e-6 ? lookFinal.Normalize() : (center ?? XYZ.BasisX);
                    double depth = Math.Max(cp.FanRadiusFeet, Cm(100));
                    string depthSrc = $"扇形半徑 {CameraMath.FeetToMm(depth):F0}mm";
                    // 剖面轉 90°＋室內：扇形半徑（原本沿視線的可見距離）跟新軸向的房間深度未必相關，容易太薄
                    // （只看到近側牆／固定物），改成不小於「房間沿最終看向側、從剖切面（眼睛）起算的投影範圍
                    // ＋0.6m buffer」——與 ApplyRotate90 的水平範圍用同一個 buffer 常數。室外／找不到房間維持原規則。
                    if (rotate90 && cp.Room?.Room != null && cp.Room.Pts != null && cp.Room.Pts.Count >= 3)
                    {
                        double depthBuf = CameraMath.MmToFeet(RoomBufferMm);
                        double maxT = double.MinValue;
                        foreach (XYZ p in cp.Room.Pts)
                        {
                            double t = (p.X - geom.Eye.X) * lookFinal.X + (p.Y - geom.Eye.Y) * lookFinal.Y;
                            if (t > maxT) maxT = t;
                        }
                        if (maxT > double.MinValue)
                        {
                            double roomDepth = maxT + depthBuf;
                            if (roomDepth > depth)
                            {
                                depthSrc = $"剖面轉90°：房間沿看向側投影 {CameraMath.FeetToMm(roomDepth):F0}mm（含 {RoomBufferMm:F0}mm buffer）取代{depthSrc}";
                                depth = roomDepth;
                            }
                        }
                    }
                    // 2026-09-28（使用者決策，D-046，MCP 實測：Kitchen 01 站在門邊，剖切面卡在牆上只看到牆／
                    // 衛浴固定物，看不到房間）：只在「沿視線看」（與剖面 fan 抑制同一個幾何判斷：lookFinal 與
                    // 相機視線 center 夾角 < SectionFanSuppressAlongSightDeg）才自動跳過眼前的牆——側視剖面
                    // （預設模式）不受影響、剖切面仍通過眼睛。往 lookFinal 打水平射線，1.5m 內打到牆／門／窗
                    // 就把剖切面挪到該元素背面 +100mm；遠裁剪維持在原世界位置（深度＝原深度－位移量）。
                    XYZ sectionOrigin = geom.Eye;
                    double sightAlignDot2 = Math.Max(-1.0, Math.Min(1.0, lookFinal.DotProduct(center ?? lookFinal)));
                    double sightAlignDeg2 = Math.Acos(sightAlignDot2) * 180.0 / Math.PI;
                    string wallSkipLog;
                    if (sightAlignDeg2 < SectionFanSuppressAlongSightDeg)
                    {
                        double wallOffset = ComputeWallSkipOffset(doc, geom.Eye, lookFinal, out string hitLog);
                        wallSkipLog = hitLog;
                        if (wallOffset > 1e-6)
                        {
                            double minDepth = Cm(50);
                            depth = Math.Max(depth - wallOffset, minDepth);
                            sectionOrigin = geom.Eye + lookFinal * wallOffset;
                        }
                    }
                    else
                    {
                        wallSkipLog = $"剖面非沿視線看（與視線夾角 {sightAlignDeg2:F1}° ≥ {SectionFanSuppressAlongSightDeg:F0}°），不套用跳過眼前牆";
                    }
                    s.Axis = ax; s.Origin = sectionOrigin; s.MinX = x0; s.MaxX = x1;
                    s.MinZ = -depth; s.MaxZ = 0.0;
                    log.Append($" | 剖面位置＝相機位置：原點＝{(sectionOrigin.IsAlmostEqualTo(geom.Eye) ? "眼睛" : $"眼睛沿看向前移{CameraMath.FeetToMm((sectionOrigin - geom.Eye).GetLength()):F0}mm")} X[{CameraMath.FeetToMm(s.MinX):F0},{CameraMath.FeetToMm(s.MaxX):F0}]mm 深度[{CameraMath.FeetToMm(s.MinZ):F0},0]mm（剖切面{(sectionOrigin.IsAlmostEqualTo(geom.Eye) ? "通過眼睛" : "已跳過眼前牆")}；深度來源＝{depthSrc}；跳牆判斷＝{wallSkipLog}）" +
                               (flipped ? "；扇形在剖切面背後 → 軸反向" : ""));
                }
                catch (Exception exA)
                {
                    Logger.Error("相機 Key Plan：剖面位置＝相機位置 計算失敗，沿用視野中心規則", exA);
                    log.Append(" | 剖面位置＝相機位置 失敗 → 沿用視野中心");
                }
            }
            else log.Append(" | 剖面位置＝視野中心（舊行為）");

            double levelZ = geom.LevelBelow.ProjectElevation;
            double bottomZ = levelZ - Cm(SectionBelowLevelCm);
            double topZ;
            string topSrc;
            if (storeyTop.Outdoor)
            {
                bottomZ = storeyTop.OutdoorBottomZ;
                topZ = storeyTop.OutdoorTopZ;
                topSrc = "outdoor (same Z as iso)";
            }
            else if (storeyTop.RoomTopZ.HasValue)
            {
                topZ = storeyTop.RoomTopZ.Value + CameraMath.MmToFeet(RoomTopSectionBufferMm);
                topSrc = "roomTop+0.5m";
            }
            else
            {
                topZ = storeyTop.SlabBottomZ + Cm(SectionAboveSlabCm);
                topSrc = "slabBottom+0.5m";
            }
            if (!storeyTop.Outdoor && (double.IsNaN(topZ) || double.IsInfinity(topZ) || topZ - geom.Eye.Z < Cm(30)))
            {
                topZ = geom.Eye.Z + Cm(SectionAboveEyeCm);
                topSrc = "fallback eye+3m";
            }
            string cutSrc = "cutPlane=?";
            if (cutPlaneZ.HasValue)
            {
                double c = cutPlaneZ.Value, eps = Cm(10);
                bool inside = c > bottomZ + eps && c < topZ - eps;
                cutSrc = $"cutPlane={CameraMath.FeetToMm(c):F0}mm inside={inside}";
                if (!inside)
                {
                    if (c <= bottomZ + eps) bottomZ = c - Cm(50);
                    if (c >= topZ - eps) topZ = c + Cm(50);
                    cutSrc += " → 外擴垂直範圍以包含平面剖切面";
                }
            }
            s.MinY = bottomZ - geom.Eye.Z;
            s.MaxY = topZ - geom.Eye.Z;
            log.Append($" | Z[{CameraMath.FeetToMm(bottomZ):F0},{CameraMath.FeetToMm(topZ):F0}]mm (bottom={(storeyTop.Outdoor ? "outdoor" : "level−1m")}, top={topSrc}) {cutSrc}");
            XYZ o = s.Origin ?? geom.Eye;
            log.Append($" | 剖面原點=({CameraMath.FeetToMm(o.X):F0},{CameraMath.FeetToMm(o.Y):F0},{CameraMath.FeetToMm(o.Z):F0})mm{(o.IsAlmostEqualTo(geom.Eye) ? "＝眼睛" : "")} axis=({s.Axis.X:F3},{s.Axis.Y:F3})");
            s.Log = log.ToString();
            return s;
        }

        /// <summary>
        /// 2026-09-28（使用者決策，D-046）：剖面「沿視線看」時，相機常常站在門邊／貼牆（例如 Snowdon Towers
        /// 「Camera at Kitchen 01」），剖切面通過眼睛就卡在眼前那道牆上，畫面只看到牆面／衛浴固定物，看不到
        /// 房間。往 <paramref name="lookFinal"/>（section 的最終看向，已經過 anchor=Camera 的軸翻轉）從
        /// <paramref name="eye"/> 打水平射線（沿用 <see cref="FindRaycastView"/> 同一張非透視／非樣板 3D 視圖、
        /// 含連結模型，與站立面射線同一套 ReferenceIntersector 用法），目標類別牆／門／窗／帷幕嵌板（帷幕牆
        /// 本身就是 OST_Walls）。只認眼睛前方 <see cref="WallSkipTightRangeMm"/>（1.5m）內的最近命中——更遠的
        /// 牆是場景的一部分（例如房間對面那道牆），不能移動剖切面。找到最近命中的元素後，在同一條射線的所有
        /// 命中裡找「同一個元素」（比對 Reference 的 ElementId＋LinkedElementId，含連結模型時仍正確分組）中
        /// Proximity 最大者當作「元素背面」（夾一個合理厚度上限 <see cref="WallSkipThicknessSanityCapMm"/>，
        /// 避免抓到牆後面另一片牆當成同一片的背面），剖切面落在背面再 +<see cref="WallSkipClearanceMm"/>（100mm）。
        /// 回傳位移量（feet，&gt;0＝有效）；沒有可用 3D 視圖、前方 1.5m 內沒有命中、或射線例外都回傳 0，
        /// <paramref name="log"/> 說明原因，呼叫端維持剖切面在相機位置，不拋例外。
        /// </summary>
        private static double ComputeWallSkipOffset(Document doc, XYZ eye, XYZ lookFinal, out string log)
        {
            log = "剖切面維持在相機位置";
            try
            {
                if (doc == null || eye == null || lookFinal == null || lookFinal.GetLength() < 1e-6)
                {
                    log = "剖切面維持在相機位置（缺少射線起點／方向）";
                    return 0.0;
                }
                View3D v3 = FindRaycastView(doc);
                if (v3 == null)
                {
                    log = "剖切面維持在相機位置（沒有可用的 3D 視圖做射線）";
                    return 0.0;
                }
                var cats = new List<BuiltInCategory>
                {
                    BuiltInCategory.OST_Walls, BuiltInCategory.OST_Doors, BuiltInCategory.OST_Windows, BuiltInCategory.OST_CurtainWallPanels
                };
                var ri = new ReferenceIntersector(new ElementMulticategoryFilter(cats), FindReferenceTarget.Face, v3) { FindReferencesInRevitLinks = true };
                IList<ReferenceWithContext> hits = ri.Find(eye, lookFinal.Normalize());
                List<ReferenceWithContext> ordered = (hits ?? new List<ReferenceWithContext>())
                    .Where(h => h != null).OrderBy(h => h.Proximity).ToList();
                if (ordered.Count == 0)
                {
                    log = "前方 1.5m 內無牆，剖切面維持在相機位置";
                    return 0.0;
                }
                double tightFeet = CameraMath.MmToFeet(WallSkipTightRangeMm);
                ReferenceWithContext near = ordered[0];
                if (near.Proximity > tightFeet)
                {
                    log = "前方 1.5m 內無牆，剖切面維持在相機位置";
                    return 0.0;
                }
                Reference nearRef = near.GetReference();
                ElementId nearElId = nearRef.ElementId;
                ElementId nearLinkId = nearRef.LinkedElementId;
                bool SameElement(Reference r) => r != null && r.ElementId == nearElId && r.LinkedElementId == nearLinkId;
                double capFeet = near.Proximity + CameraMath.MmToFeet(WallSkipThicknessSanityCapMm);
                double farProximity = near.Proximity;
                foreach (ReferenceWithContext h in ordered)
                {
                    Reference r;
                    try { r = h.GetReference(); } catch { continue; }
                    if (!SameElement(r) || h.Proximity > capFeet) continue;
                    if (h.Proximity > farProximity) farProximity = h.Proximity;
                }
                double clearanceFeet = CameraMath.MmToFeet(WallSkipClearanceMm);
                double offset = farProximity + clearanceFeet;
                string catName = "?", elLabel = nearElId.GetIdValue().ToString();
                try
                {
                    bool linked = nearLinkId != null && nearLinkId != ElementId.InvalidElementId;
                    if (linked)
                    {
                        Element linkInst = doc.GetElement(nearElId);
                        catName = "連結元素";
                        elLabel = $"{linkInst?.Name}內 {nearLinkId.GetIdValue()}";
                    }
                    else
                    {
                        Element el = doc.GetElement(nearElId);
                        catName = el?.Category?.Name ?? "?";
                        elLabel = $"{nearElId.GetIdValue()}「{el?.Name}」";
                    }
                }
                catch { }
                log = $"前方偵測到「{catName}」{elLabel}（近端 {CameraMath.FeetToMm(near.Proximity):F0}mm、背面 {CameraMath.FeetToMm(farProximity):F0}mm）→ 剖切面往前移 {CameraMath.FeetToMm(offset):F0}mm（背面 +{WallSkipClearanceMm:F0}mm）";
                return offset;
            }
            catch (Exception ex)
            {
                Logger.Error("相機 Key Plan：剖面「跳過眼前牆」射線失敗，剖切面維持原位", ex);
                log = "剖切面維持在相機位置（射線例外：" + ex.Message + "）";
                return 0.0;
            }
        }

        /// <summary>
        /// 2026-09-27（使用者：「剖面有沒有辦法提供一個選項，讓它可以旋轉 90 度」）：單一相機「剖面轉 90°」——
        /// 把目前模式決定的軸（<paramref name="s"/>.Axis，此時已包含各模式自己的原點移動，例如對齊房間長邊
        /// 已把原點移到中心線上）繞垂直軸轉 90°，沿新軸重新算水平範圍（相對目前的 <paramref name="s"/>.Origin）：
        ///   室內（找得到眼睛所在房間）：房間頂點沿新軸投影 ±0.6m buffer（與其他規則同一個 buffer 常數）；
        ///   室外／無房間：等角 XY 範圍（房間外接框+buffer，或找不到房間時的扇形外接框）沿新軸投影。
        /// 之後 anchor=Camera 區塊（原點改回眼睛、看向側翻轉、深度）與 ClampSectionSpecToPlanCrop 原樣對
        /// 「已轉向」的 spec 運作，不需要另外改。深度（MinZ/MaxZ）重置為 NaN，讓 BuildSectionBox 用預設
        /// ±1.5m（anchor=Camera 之後會整個覆蓋掉，只影響 anchor=Center＋房間長軸這個組合）。
        /// 任何一步算不出來（基準軸與垂直軸重合、投影範圍為零、例外）都記 log 並維持原方向，不拋例外。
        /// </summary>
        private static void ApplyRotate90(SectionSpec s, CameraParams cp, CameraMath.CameraGeometry geom, IsoXY isoXY)
        {
            try
            {
                XYZ baseAxis = s.Axis != null && s.Axis.GetLength() > 1e-6 ? s.Axis.Normalize() : XYZ.BasisX;
                XYZ axis90 = XYZ.BasisZ.CrossProduct(baseAxis);
                if (axis90.GetLength() < 1e-6)
                {
                    s.Log += " | 剖面轉90°：基準軸與垂直軸重合，無法轉向，略過";
                    return;
                }
                axis90 = axis90.Normalize();
                XYZ origin = s.Origin ?? geom.Eye;
                double buf = CameraMath.MmToFeet(RoomBufferMm);
                double mn = double.MaxValue, mx = double.MinValue;
                string src;

                if (cp.Room?.Room != null && cp.Room.Pts != null && cp.Room.Pts.Count >= 3)
                {
                    foreach (XYZ p in cp.Room.Pts)
                    {
                        double t = (p.X - origin.X) * axis90.X + (p.Y - origin.Y) * axis90.Y;
                        if (t < mn) mn = t;
                        if (t > mx) mx = t;
                    }
                    mn -= buf; mx += buf;
                    src = $"室內：眼睛所在房間頂點投影 ±{RoomBufferMm:F0}mm buffer";
                }
                else if (isoXY != null)
                {
                    double[] xs = { isoXY.MinX, isoXY.MaxX };
                    double[] ys = { isoXY.MinY, isoXY.MaxY };
                    foreach (double x in xs)
                        foreach (double y in ys)
                        {
                            double t = (x - origin.X) * axis90.X + (y - origin.Y) * axis90.Y;
                            if (t < mn) mn = t;
                            if (t > mx) mx = t;
                        }
                    src = "室外／無房間：等角 XY 範圍投影";
                }
                else
                {
                    double r = Math.Max(cp.FanRadiusFeet, Cm(100));
                    mn = -r; mx = r;
                    src = "無房間／無等角範圍資料：扇形半徑 fallback";
                }

                if (!(mx > mn + 1e-6))
                {
                    s.Log += " | 剖面轉90°：投影範圍算不出來（min≥max），略過，維持原方向";
                    return;
                }

                string beforeAxis = $"({s.Axis.X:F3},{s.Axis.Y:F3})";
                string beforeRange = $"X[{CameraMath.FeetToMm(s.MinX):F0},{CameraMath.FeetToMm(s.MaxX):F0}]mm";
                s.Axis = axis90;
                s.MinX = mn; s.MaxX = mx;
                s.MinZ = s.MaxZ = double.NaN; // 深度隨新軸重算（anchor=Camera 會整個覆蓋；anchor=Center 退回預設 ±1.5m）
                s.Log += $" | 剖面轉90°：基準軸 {beforeAxis} → ({axis90.X:F3},{axis90.Y:F3})，{src}，{beforeRange} → X[{CameraMath.FeetToMm(mn):F0},{CameraMath.FeetToMm(mx):F0}]mm";
            }
            catch (Exception ex)
            {
                Logger.Error("相機 Key Plan：剖面轉90°計算失敗，維持原方向", ex);
                s.Log += " | 剖面轉90°：計算失敗（例外），維持原方向";
            }
        }

        /// <summary>
        /// 2026-09-26 第七輪（Snowdon Towers「Exterior Street View」：室外剖面沿線 −2m…+60m，遠端標頭超出
        /// Key Plan 平面的裁剪框、近端標頭被眼睛楔形蓋住，平面上看不出剖面線）：把 <paramref name="spec"/> 的
        /// 沿線範圍（MinX/MaxX，相對 spec.Origin）截到 <paramref name="planView"/> 目前裁剪框內、兩端各再內縮
        /// <see cref="SectionLineCropInsetMm"/>，讓兩端標頭符號都落在平面可見範圍內。只縮短，原本就比裁剪框
        /// 短的範圍不動；讀不到裁剪框或算不出交點（原點在框外）就不截，維持原範圍。只改 spec，不動已建立的視圖。
        /// </summary>
        private static void ClampSectionSpecToPlanCrop(ViewPlan planView, SectionSpec spec)
        {
            try
            {
                BoundingBoxXYZ crop = planView?.CropBox;
                if (crop == null || spec?.Axis == null) return;
                XYZ origin = spec.Origin;
                if (origin == null) return;
                double inset = CameraMath.MmToFeet(SectionLineCropInsetMm);
                double posLimit = RayToCropEdge(crop, origin, spec.Axis);
                double negLimit = RayToCropEdge(crop, origin, spec.Axis.Negate());
                var log = new System.Text.StringBuilder();
                if (!double.IsInfinity(posLimit))
                {
                    double cap = Math.Max(posLimit - inset, 0.0);
                    if (spec.MaxX > cap + 1e-6)
                    {
                        log.Append($"正向截到平面裁剪框（{CameraMath.FeetToMm(posLimit):F0}mm 內縮 {SectionLineCropInsetMm:F0}mm）：MaxX {CameraMath.FeetToMm(spec.MaxX):F0}→{CameraMath.FeetToMm(cap):F0}mm；");
                        spec.MaxX = cap;
                    }
                }
                if (!double.IsInfinity(negLimit))
                {
                    double cap = Math.Max(negLimit - inset, 0.0);
                    if (spec.MinX < -cap - 1e-6)
                    {
                        log.Append($"負向截到平面裁剪框（{CameraMath.FeetToMm(negLimit):F0}mm 內縮 {SectionLineCropInsetMm:F0}mm）：MinX {CameraMath.FeetToMm(spec.MinX):F0}→{CameraMath.FeetToMm(-cap):F0}mm；");
                        spec.MinX = -cap;
                    }
                }
                if (log.Length > 0)
                {
                    spec.Log += " | 剖面沿線範圍截到平面裁剪框：" + log;
                    Logger.Info("相機 Key Plan：剖面沿線範圍截到平面裁剪框：" + log);
                }
            }
            catch (Exception ex) { Logger.Error("相機 Key Plan：剖面沿線範圍截到平面裁剪框失敗（維持原範圍）", ex); }
        }

        /// <summary>既有剖面的世界座標軸方向（水平，Z 忽略），供平面上判斷「剖面方向與視線中心線是否幾乎重疊」。
        /// 讀不到（剖面不存在或例外）回傳 null；呼叫端應視為「無法判斷」而照原規則畫。</summary>
        private static XYZ GetSectionAxisWorldXY(ViewSection section)
        {
            try
            {
                XYZ bx = section?.CropBox?.Transform?.BasisX;
                if (bx == null) return null;
                XYZ flat = new XYZ(bx.X, bx.Y, 0);
                return flat.GetLength() > 1e-6 ? flat.Normalize() : null;
            }
            catch { return null; }
        }

        /// <summary>
        /// 房間長軸：候選方向＝Key Plan 平面的 X 軸（軸對齊）＋每條邊界線段方向；對每個方向算「沿該方向／
        /// 其垂直方向」的投影範圍面積，取最小者（有一邊與某邊界共線的最小外接矩形；矩形房間精確）。
        /// 最小面積若沒有比軸對齊框小 10% 以上，就用軸對齊框（剖面線在平面上保持水平/垂直）。
        /// 長短邊差 &lt;15%（近正方形）→ 兩軸中取與相機水平前方向較接近者。號誌：與相機前方向同側。
        /// t0/t1＝房間頂點沿軸、相對眼睛的投影範圍（feet）。
        /// </summary>
        private static bool TryRoomLongAxis(RoomCtx rc, XYZ eye, XYZ planBasisX, XYZ forwardHoriz,
            out XYZ axis, out double t0, out double t1, out string log)
        {
            axis = null; t0 = t1 = 0; log = "";
            XYZ px = planBasisX != null ? new XYZ(planBasisX.X, planBasisX.Y, 0) : XYZ.BasisX;
            px = px.GetLength() > 1e-6 ? px.Normalize() : XYZ.BasisX;

            void Extent(XYZ u, out double lu, out double lv)
            {
                XYZ v = new XYZ(-u.Y, u.X, 0);
                double a0 = double.MaxValue, a1 = double.MinValue, b0 = double.MaxValue, b1 = double.MinValue;
                foreach (XYZ p in rc.Pts)
                {
                    double a = p.X * u.X + p.Y * u.Y, b = p.X * v.X + p.Y * v.Y;
                    a0 = Math.Min(a0, a); a1 = Math.Max(a1, a); b0 = Math.Min(b0, b); b1 = Math.Max(b1, b);
                }
                lu = a1 - a0; lv = b1 - b0;
            }

            Extent(px, out double aabbU, out double aabbV);
            double aabbArea = aabbU * aabbV;
            XYZ bestU = px; double bestArea = aabbArea, bestLu = aabbU, bestLv = aabbV;
            int tested = 0;
            foreach (var seg in rc.Segs)
            {
                XYZ d = new XYZ(seg.Value.X - seg.Key.X, seg.Value.Y - seg.Key.Y, 0);
                if (d.GetLength() < 0.1) continue;
                d = d.Normalize();
                Extent(d, out double lu, out double lv);
                tested++;
                if (lu * lv < bestArea - 1e-9) { bestArea = lu * lv; bestU = d; bestLu = lu; bestLv = lv; }
                if (tested > 400) break;
            }
            bool useObb = bestArea < 0.9 * aabbArea;
            XYZ u0 = useObb ? bestU : px;
            double Lu = useObb ? bestLu : aabbU, Lv = useObb ? bestLv : aabbV;
            XYZ v0 = new XYZ(-u0.Y, u0.X, 0);
            if (!(Lu > 1e-6) || !(Lv > 1e-6)) return false;

            XYZ chosen;
            string why;
            if (Math.Abs(Lu - Lv) / Math.Max(Lu, Lv) < 0.15)
            {
                chosen = Math.Abs(u0.DotProduct(forwardHoriz)) >= Math.Abs(v0.DotProduct(forwardHoriz)) ? u0 : v0;
                why = "近正方形→取較接近視線的軸";
            }
            else
            {
                chosen = Lu >= Lv ? u0 : v0;
                why = "長軸";
            }
            if (chosen.DotProduct(forwardHoriz) < 0) chosen = chosen.Negate();
            axis = chosen.Normalize();

            double mn = double.MaxValue, mx = double.MinValue;
            foreach (XYZ p in rc.Pts)
            {
                double t = (p.X - eye.X) * axis.X + (p.Y - eye.Y) * axis.Y;
                mn = Math.Min(mn, t); mx = Math.Max(mx, t);
            }
            t0 = Math.Min(mn, 0); t1 = Math.Max(mx, 0);
            double sightAngle = Math.Acos(Math.Max(-1, Math.Min(1, Math.Abs(axis.DotProduct(forwardHoriz))))) * 180 / Math.PI;
            log = $"{(useObb ? "OBB" : "AABB(平面方向)")} L={CameraMath.FeetToMm(Math.Max(Lu, Lv)):F0}×{CameraMath.FeetToMm(Math.Min(Lu, Lv)):F0}mm {why} " +
                  $"axis=({axis.X:F3},{axis.Y:F3}) 與視線夾角={sightAngle:F1}° 房間沿軸[{CameraMath.FeetToMm(mn):F0},{CameraMath.FeetToMm(mx):F0}]mm";
            return true;
        }

        /// <summary>Key Plan 平面的剖切面絕對高程（feet）；讀不到回傳 null。</summary>
        private static double? GetPlanCutPlaneZ(Document doc, ViewPlan plan)
        {
            try
            {
                PlanViewRange vr = plan.GetViewRange();
                Level l = doc.GetElement(vr.GetLevelId(PlanViewPlane.CutPlane)) as Level ?? plan.GenLevel;
                if (l == null) return null;
                return l.ProjectElevation + vr.GetOffset(PlanViewPlane.CutPlane);
            }
            catch (Exception ex)
            {
                Logger.Error("相機 Key Plan：讀取平面剖切面高程失敗", ex);
                return null;
            }
        }

        // ══════════════════════════════════════════════════════════
        // 等角
        // ══════════════════════════════════════════════════════════

        private static View3D GetOrCreateIsoView(Document doc, string isoName, string cid,
            CameraMath.CameraGeometry geom, IsoXY isoXY, StoreyTop storeyTop, XYZ forwardHoriz,
            RoomCtx nearWallRoom, CameraIsoNearWallsMode nearWalls, out string nearWallsLog)
        {
            // 2026-09-24 第四輪：三個分支（ES 連結／同名既有／新建）一律「設定 SectionBox → 確保剖面框
            // 類別可見」，不改視圖的 DisplayStyle（使用者現況 FlatColors 維持不動）。
            BoundingBoxXYZ box = BuildIsoSectionBox(doc, geom, isoXY, storeyTop);
            // 2026-09-26（D-038）：切掉等角觀看者側的兩道牆（dollhouse cutaway），只限室內相機。
            nearWallsLog = ApplyIsoNearWallCut(box, nearWallRoom, forwardHoriz, nearWalls, geom?.Eye);
            Logger.Info("相機 Key Plan：等角近側牆 " + nearWallsLog);
            ViewOrientation3D orientation = BuildIsoOrientation(box, forwardHoriz);

            View linked = FindLinkedView(doc, cid, "Iso");
            if (linked is View3D linkedIso)
            {
                if (linkedIso.Name != isoName) linkedIso.Name = MakeUniqueName(doc, isoName, linkedIso.Id);
                linkedIso.SetOrientation(orientation);
                if (!linkedIso.IsSectionBoxActive) linkedIso.IsSectionBoxActive = true;
                linkedIso.SetSectionBox(box);
                EnsureSectionBoxVisible(linkedIso);
                return linkedIso;
            }

            View3D byName = new FilteredElementCollector(doc).OfClass(typeof(View3D)).Cast<View3D>()
                .FirstOrDefault(v => !v.IsTemplate && !v.IsPerspective && v.Name == isoName);
            if (byName != null)
            {
                byName.SetOrientation(orientation);
                byName.IsSectionBoxActive = true;
                byName.SetSectionBox(box);
                EnsureSectionBoxVisible(byName);
                TagLink(byName, cid, "Iso");
                return byName;
            }

            ViewFamilyType threeDVft = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>().FirstOrDefault(v => v.ViewFamily == ViewFamily.ThreeDimensional);
            if (threeDVft == null) throw new Exception("找不到任何 3D 視圖類型 (ViewFamily.ThreeDimensional)。");

            View3D created = View3D.CreateIsometric(doc, threeDVft.Id);
            created.Name = MakeUniqueName(doc, isoName, created.Id);
            created.SetOrientation(orientation);
            created.IsSectionBoxActive = true;
            created.SetSectionBox(box);
            EnsureSectionBoxVisible(created);
            TagLink(created, cid, "Iso");
            return created;
        }

        /// <summary>
        /// 讓等角圖看得到剖面框外框，使用者才能直接拖曳調整範圍。新建 3D 視圖可能帶到
        /// ViewFamilyType 的預設樣板，或「剖面框」類別被隱藏——兩者都會讓框線消失。
        /// 只動 Key Plan 等角圖本身，不改任何使用者的視圖樣板；失敗只記 log。
        /// </summary>
        private static void EnsureSectionBoxVisible(View3D iso)
        {
            try
            {
                if (iso.ViewTemplateId != ElementId.InvalidElementId)
                    iso.ViewTemplateId = ElementId.InvalidElementId;
            }
            catch (Exception ex) { Logger.Error($"Key Plan 等角圖移除視圖樣板失敗: {ex.Message}"); }

            try
            {
                ElementId sectionBoxCat = new ElementId(BuiltInCategory.OST_SectionBox);
                bool canHide = iso.CanCategoryBeHidden(sectionBoxCat);
                bool hiddenBefore = canHide && iso.GetCategoryHidden(sectionBoxCat);
                if (hiddenBefore) iso.SetCategoryHidden(sectionBoxCat, false);
                bool hiddenAfter = canHide && iso.GetCategoryHidden(sectionBoxCat);
                string style = "?";
                try { style = iso.DisplayStyle.ToString(); } catch { }
                Logger.Info($"相機 Key Plan：等角 id={iso.Id.GetIdValue()} 剖面框類別 canHide={canHide} hiddenBefore={hiddenBefore} hiddenAfter={hiddenAfter} " +
                    $"sectionBoxActive={iso.IsSectionBoxActive} template={iso.ViewTemplateId?.GetIdValue()} displayStyle={style}（不變更）");
            }
            catch (Exception ex) { Logger.Error($"Key Plan 等角圖顯示剖面框類別失敗: {ex.Message}"); }

            // 2026-09-25：剖面框仍看不到（OST_SectionBox 未隱藏、Section Box=是、無樣板）——主要假設：
            // 視圖的「註解類別」總開關被關（AreAnnotationCategoriesHidden==true；剖面框屬註解類別）。
            // 打開總開關，再逐類別隱藏等角不需要的註解，讓畫面只剩剖面框外框。
            try
            {
                bool annBefore = iso.AreAnnotationCategoriesHidden;
                if (annBefore) iso.AreAnnotationCategoriesHidden = false;
                bool annAfter = iso.AreAnnotationCategoriesHidden;
                bool analytical = false;
                try { analytical = iso.AreAnalyticalModelCategoriesHidden; } catch { }
                Logger.Info($"相機 Key Plan：等角 id={iso.Id.GetIdValue()} AreAnnotationCategoriesHidden before={annBefore} after={annAfter}" +
                    $"（AreAnalyticalModelCategoriesHidden={analytical}，與剖面框無關，不變更）");
            }
            catch (Exception ex) { Logger.Error("相機 Key Plan：等角圖打開註解類別總開關失敗", ex); }

            try
            {
                var names = new List<string>(IsoDeclutterCategoryNames);
                int hidden = HideCategoriesByName(iso, names, includeAllTags: true, keepRoomTags: false, hide: true);
                Logger.Info($"相機 Key Plan：等角 id={iso.Id.GetIdValue()} 隱藏雜項註解類別 {hidden} 個（剖面框保留）");
            }
            catch (Exception ex) { Logger.Error("相機 Key Plan：等角圖隱藏雜項註解類別失敗", ex); }
        }

        // ══════════════════════════════════════════════════════════
        // Key Plan 表現法（2026-09-25，規格 §5.1「Key Plan 表現法」）
        // ══════════════════════════════════════════════════════════

        /// <summary>平面／剖面 Key Plan「淡化背景」要隱藏的註解類別（另外 *Tags 全部，房間標籤除外）。
        /// 不含 OST_Sections（會連本相機剖面一起藏掉；其他剖面改逐元素隱藏，見 HideOtherSectionMarkers）。</summary>
        private static readonly string[] KeyPlanDeclutterCategoryNames =
        {
            "OST_Grids", "OST_Levels", "OST_Dimensions", "OST_TextNotes", "OST_CLines", "OST_VolumeOfInterest",
            "OST_Elev", "OST_ElevationMarks", "OST_Callouts", "OST_CalloutHeads", "OST_CalloutBoundary",
            "OST_SpotElevations", "OST_SpotCoordinates", "OST_SpotSlopes", "OST_Matchline", "OST_RevisionClouds"
        };

        /// <summary>等角圖要隱藏的註解類別（剖面框 OST_SectionBox 保留）；等角圖本來就不需要剖面標記。</summary>
        private static readonly string[] IsoDeclutterCategoryNames =
        {
            "OST_Grids", "OST_Levels", "OST_Dimensions", "OST_TextNotes", "OST_CLines", "OST_VolumeOfInterest",
            "OST_Elev", "OST_ElevationMarks", "OST_Callouts", "OST_CalloutHeads", "OST_CalloutBoundary",
            "OST_Sections", "OST_SpotElevations", "OST_SpotCoordinates", "OST_SpotSlopes", "OST_Matchline", "OST_RevisionClouds"
        };

        /// <summary>依 BuiltInCategory 名稱（字串，執行期解析，避免各版 SDK 缺列舉值時編譯失敗）隱藏／
        /// 取消隱藏類別；includeAllTags＝另外處理所有「*Tags」註解類別（keepRoomTags＝跳過 OST_RoomTags）。
        /// 只動 CanCategoryBeHidden 的類別；回傳實際變更數。個別失敗只記 log。</summary>
        private static int HideCategoriesByName(View view, IEnumerable<string> names, bool includeAllTags, bool keepRoomTags, bool hide)
        {
            Document doc = view.Document;
            var ids = new Dictionary<long, KeyValuePair<ElementId, string>>();
            foreach (string n in names)
            {
                if (!Enum.TryParse(n, out BuiltInCategory bic)) { Logger.Info($"相機 Key Plan：此版本沒有類別 {n}，略過"); continue; }
                Category c = null;
                try { c = Category.GetCategory(doc, bic); } catch { }
                if (c != null) ids[c.Id.GetIdValue()] = new KeyValuePair<ElementId, string>(c.Id, n);
            }
            if (includeAllTags)
            {
                foreach (Category c in doc.Settings.Categories)
                {
                    try
                    {
                        if (c == null || c.CategoryType != CategoryType.Annotation) continue;
                        long v = c.Id.GetIdValue();
                        if (v >= 0) continue;
                        string bn = Enum.GetName(typeof(BuiltInCategory), (int)v);
                        if (bn == null || !bn.EndsWith("Tags", StringComparison.Ordinal)) continue;
                        if (keepRoomTags && bn == "OST_RoomTags") continue;
                        ids[v] = new KeyValuePair<ElementId, string>(c.Id, bn);
                    }
                    catch { }
                }
            }
            int changed = 0;
            foreach (var kv in ids)
            {
                try
                {
                    ElementId cid = kv.Value.Key;
                    if (cid == null || !view.CanCategoryBeHidden(cid)) continue;
                    if (view.GetCategoryHidden(cid) == hide) continue;
                    view.SetCategoryHidden(cid, hide);
                    changed++;
                }
                catch (Exception ex) { Logger.Error($"相機 Key Plan：{(hide ? "隱藏" : "取消隱藏")}類別 {kv.Value.Value} 失敗（略過）", ex); }
            }
            return changed;
        }

        /// <summary>
        /// 套用（或還原）Key Plan 平面／剖面的「淡化背景」表現法。只作用在傳入的 Key Plan 視圖本身，
        /// 必須在裁剪框設定之後、標記繪製之前呼叫（比例會影響標記的紙上尺寸）。每一步獨立 try/catch＋log。
        ///   faded：① Display Model＝一般（2026-09-25 第七輪起不再用半色調，外輪廓要清楚）② 隱藏雜項註解類別
        ///          （房間標籤保留但類別取代半色調）④ 模型類別／牆元素取代（見 ApplyModelCategoryOverrides）
        ///          ③ 比例＝1:50/100/200 中最細且裁剪範圍放得進 A3 橫式 400×270mm 者（都放不下用 1:200）。
        ///   原始：Display Model＝一般（0）、取消隱藏同一組類別、清除房間標籤與 ④ 的類別／牆元素取代；比例不動。
        /// </summary>
        private static void ApplyKeyPlanStyle(View view, bool faded, bool isSection = false, Level levelBelow = null, bool outdoor = false, XYZ eye = null, string cid = null)
        {
            if (view == null) return;
            string label = $"「{view.Name}」(id={view.Id.GetIdValue()})";
            Document doc = view.Document;

            try
            {
                if (view.ViewTemplateId != ElementId.InvalidElementId)
                {
                    view.ViewTemplateId = ElementId.InvalidElementId;
                    Logger.Info($"相機 Key Plan：{label} 移除視圖樣板（讓表現法設定生效）");
                }
            }
            catch (Exception ex) { Logger.Error($"相機 Key Plan：{label} 移除視圖樣板失敗", ex); }

            // ① Display Model
            try
            {
                Parameter p = view.get_Parameter(BuiltInParameter.VIEW_MODEL_DISPLAY_MODE);
                if (p != null && !p.IsReadOnly && p.StorageType == StorageType.Integer)
                {
                    int before = p.AsInteger();
                    string beforeStr = p.AsValueString();
                    // 2026-09-25 第七輪（使用者：「平面淡化可以，但建築外輪廓要清楚」）：Display Model＝半色調會連切到的
                    // 牆一起變灰、外輪廓消失；兩種樣式一律改回「一般」，淡化改由 ④ 類別／元素取代處理。
                    int target = 0; // 0＝一般、1＝半色調、2＝不顯示（Revit UI 下拉順序；以 AsValueString 記 log 供實機核對）
                    if (before != target) p.Set(target);
                    // 2026-09-25（剖面實測 Display Model 疑似未生效）：以 AsValueString 驗證；若不是預期字樣，
                    // 逐一試 0..2 找出顯示字樣符合者（半色調：Halftone／半色調；一般：Normal／一般），找不到就回到 target。
                    string after = p.AsValueString() ?? "";
                    bool Matches(string v) =>
                        v.IndexOf("Normal", StringComparison.OrdinalIgnoreCase) >= 0 || v.Contains("一般") || v.Contains("正常");
                    string probe = "";
                    if (!Matches(after))
                    {
                        int found = -1;
                        for (int k = 0; k <= 2 && found < 0; k++)
                        {
                            try { p.Set(k); string vs = p.AsValueString() ?? ""; probe += $"{k}={vs};"; if (Matches(vs)) found = k; }
                            catch (Exception pex) { probe += $"{k}=ERR({pex.Message});"; }
                        }
                        p.Set(found >= 0 ? found : target);
                        probe = " probe[" + probe + "]" + (found >= 0 ? $" → 用 {found}" : " → 找不到符合字樣，維持 " + target);
                    }
                    Logger.Info($"相機 Key Plan：{label} Display Model {before}（{beforeStr}）→{p.AsInteger()}（{p.AsValueString()}）{probe}");
                }
                else Logger.Info($"相機 Key Plan：{label} Display Model 參數不可用（null/唯讀/型別={p?.StorageType}），略過");
            }
            catch (Exception ex) { Logger.Error($"相機 Key Plan：{label} 設定 Display Model 失敗", ex); }

            // ② 類別隱藏＋房間標籤半色調
            try
            {
                // 2026-09-25（§5.1 剖面樓層）：剖面的樓層整類隱藏（主體＋連結的樓層基準都藏），改畫自己的簡化樓層線＋
                // 樓層字（見 DrawSectionLevelMarks）；那些字是 TextNote，所以剖面的文字類別不整類隱藏，
                // 改成逐元素隱藏「不是我們的」文字（HideForeignTextNotes）。
                IEnumerable<string> names = isSection
                    ? KeyPlanDeclutterCategoryNames.Where(x => x != "OST_TextNotes")
                    : KeyPlanDeclutterCategoryNames;
                int n = HideCategoriesByName(view, names, includeAllTags: true, keepRoomTags: true, hide: faded);
                if (isSection) HideCategoriesByName(view, new[] { "OST_TextNotes" }, false, false, hide: false);
                Logger.Info($"相機 Key Plan：{label} {(faded ? "隱藏" : "取消隱藏")}雜項註解類別 {n} 個");
            }
            catch (Exception ex) { Logger.Error($"相機 Key Plan：{label} 類別隱藏/還原失敗", ex); }
            try
            {
                Category roomTags = Category.GetCategory(doc, BuiltInCategory.OST_RoomTags);
                if (roomTags != null && view.IsCategoryOverridable(roomTags.Id))
                {
                    var ogs = new OverrideGraphicSettings();
                    if (faded) ogs.SetHalftone(true);
                    view.SetCategoryOverrides(roomTags.Id, ogs);
                    Logger.Info($"相機 Key Plan：{label} 房間標籤類別取代 halftone={faded}");
                }
            }
            catch (Exception ex) { Logger.Error($"相機 Key Plan：{label} 房間標籤半色調設定失敗", ex); }

            // ④ 模型類別／牆元素取代（取代舊的 Display Model＝半色調；外輪廓最粗最黑）
            try { ApplyModelCategoryOverrides(view, faded, isSection); }
            catch (Exception ex) { Logger.Error($"相機 Key Plan：{label} 模型類別取代失敗", ex); }

            // ⑤ 連結模型顯示＝依主體視圖（2026-09-25：剖面裡堆疊的「L5 TOS」「R2 TOS」「Parking」等是連結模型的樓層，
            // 主體逐元素隱藏管不到；依主體視圖時 Revit 不顯示連結自己的樓層／軸線／註解，只顯示模型幾何）。
            if (faded)
            {
                try { SetLinksByHostView(view); }
                catch (Exception ex) { Logger.Error($"相機 Key Plan：{label} 連結模型顯示設定失敗（略過）", ex); }
            }

            if (isSection)
            {
                try { ApplySectionLevelDatums(view, faded, levelBelow, outdoor, cid); }
                catch (Exception ex) { Logger.Error($"相機 Key Plan：{label} 剖面樓層線處理失敗", ex); }
            }
            else if (faded)
            {
                try { HidePlanDatumBubbles(view); }
                catch (Exception ex) { Logger.Error($"相機 Key Plan：{label} 平面樓層／軸線標頭隱藏失敗", ex); }
            }

            // ③ 比例（剖面多一檔 1:20）
            if (!faded) return;
            try
            {
                BoundingBoxXYZ crop = view.CropBox;
                if (crop == null) return;
                double wMm = CameraMath.FeetToMm(Math.Abs(crop.Max.X - crop.Min.X));
                double hMm = CameraMath.FeetToMm(Math.Abs(crop.Max.Y - crop.Min.Y));
                int chosen = 200;
                foreach (int s in (isSection ? new[] { 20, 50, 100, 200 } : new[] { 50, 100, 200 }))
                {
                    if (wMm / s <= 400.0 && hMm / s <= 270.0) { chosen = s; break; }
                }
                int before = view.Scale;
                if (before != chosen) view.Scale = chosen;
                Logger.Info($"相機 Key Plan：{label} 裁剪 {wMm:F0}×{hMm:F0}mm → 比例 1:{before}→1:{view.Scale}（A3 橫式 400×270mm）");
            }
            catch (Exception ex) { Logger.Error($"相機 Key Plan：{label} 設定比例失敗", ex); }
        }

        /// <summary>
        /// 剖面 Key Plan 的樓層（2026-09-25 第二版，使用者拍板：連結模型的樓層在「依主體視圖」下仍會顯示，且無法逐元素
        /// 隱藏連結內的元素）：
        ///   faded＝整類隱藏 OST_Levels（主體＋連結的樓層基準都不顯示，見 ApplyKeyPlanStyle ②），改畫「簡化樓層記號」：
        ///          保留樓層（室內：下方樓層＋高差 ≥1 m 的下一個建築樓層；室外：只有下方樓層）各畫一條橫跨裁剪寬度的
        ///          灰色細詳圖線＋左端一個 TextNote（樓層名＋依專案單位格式的高程）。每個元素以 ES（相機 UniqueId＋
        ///          Role＝LevelMark＋Level UniqueId）連結真的 Level，重跑時整批替換、樓層改高程／改名時自動重畫
        ///          （<see cref="UpdateLevelMarksFromLevels"/>）。另外把「不是我們的」文字逐元素隱藏（文字類別不整類藏）。
        ///   原始＝刪除我們的樓層記號、取消隱藏文字；並清掉舊版（逐元素隱藏／半色調／視圖專屬 2D 範圍）留下的樓層設定。
        /// 舊版逐元素樓層隱藏／半色調／2D 範圍／標頭擺放邏輯已移除（2026-09-25）。
        /// </summary>
        private static void ApplySectionLevelDatums(View view, bool faded, Level levelBelow, bool outdoor, string cid)
        {
            Document doc = view.Document;
            if (!faded)
            {
                int removed = DeleteLevelMarks(doc, view, null);
                int unhidText = UnhideTextNotes(view);
                // 舊版淡化樣式留下的逐元素樓層設定（隱藏／取代／視圖專屬 2D 範圍）一併還原。
                List<Level> allLevels = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().ToList();
                var toUnhide = allLevels.Where(l => { try { return l.IsHidden(view); } catch { return false; } }).Select(l => l.Id).ToList();
                if (toUnhide.Count > 0) { try { view.UnhideElements(toUnhide); } catch (Exception ex) { Logger.Error("相機 Key Plan：取消隱藏舊版樓層失敗", ex); } }
                int extReset = 0;
                foreach (Level l in allLevels)
                {
                    try { view.SetElementOverrides(l.Id, new OverrideGraphicSettings()); } catch { }
                    foreach (DatumEnds end in new[] { DatumEnds.End0, DatumEnds.End1 })
                    {
                        try
                        {
                            if (l.GetDatumExtentTypeInView(end, view) == DatumExtentType.ViewSpecific)
                            {
                                l.SetDatumExtentType(end, view, DatumExtentType.Model);
                                extReset++;
                            }
                        }
                        catch { }
                    }
                }
                Logger.Info($"相機 Key Plan：剖面「{view.Name}」原始樣式：樓層類別取消隱藏、刪除簡化樓層記號 {removed} 個、取消隱藏文字 {unhidText} 個、" +
                            $"還原舊版逐元素隱藏樓層 {toUnhide.Count} 個／2D 範圍 {extReset} 端");
                return;
            }

            int hiddenText = HideForeignTextNotes(view);
            if (levelBelow == null || string.IsNullOrEmpty(cid))
            {
                int removed = DeleteLevelMarks(doc, view, cid);
                Logger.Info($"相機 Key Plan：剖面「{view.Name}」找不到相機樓層（或相機 Id），不畫樓層記號（刪除舊記號 {removed} 個；隱藏他人文字 {hiddenText} 個）");
                return;
            }
            List<Level> keep = ComputeSectionKeepLevels(doc, levelBelow, outdoor, view);
            int drawn = DrawSectionLevelMarks(doc, view, keep, cid);
            Logger.Info($"相機 Key Plan：剖面「{view.Name}」樓層類別整類隱藏（主體＋連結）；簡化樓層記號（{(outdoor ? "室外：剖面高度範圍內的所有建築樓層" : "室內：樓層下一層＋樓層＋高差≥1m 上一個建築樓層")}）" +
                        $" [{string.Join(", ", keep.Select(l => $"{l.Name}@{CameraMath.FeetToMm(l.ProjectElevation):F0}mm"))}] 畫了 {drawn} 個元素；隱藏他人文字 {hiddenText} 個");
        }

        /// <summary>
        /// 剖面要標的主體樓層（2026-09-26 第四輪，使用者規則）：
        ///   室內＝相機樓層的「下一個建築樓層」（高差 ≥ 1 m）＋相機樓層＋高差 ≥ 1 m 的上一個建築樓層；
        ///   室外＝剖面裁剪框高度範圍內的所有建築樓層（讀不到範圍＝只有相機樓層）。
        /// 記號仍連結真的 Level（改高程／改名自動重畫）。記號落在剖面裁剪框外時會被裁掉（範圍不因此外擴）。
        /// </summary>
        private static List<Level> ComputeSectionKeepLevels(Document doc, Level levelBelow, bool outdoor, View section = null)
        {
            var keep = new List<Level>();
            if (levelBelow == null) return keep;
            List<Level> storeys = CameraMath.GetStoreyLevelsSorted(doc);
            double gap = CameraMath.MmToFeet(SectionLevelMinGapMm);
            if (outdoor)
            {
                if (section != null && TryGetViewWorldZRange(section, out double z0, out double z1))
                    keep.AddRange(storeys.Where(l => l.ProjectElevation >= z0 - 1e-6 && l.ProjectElevation <= z1 + 1e-6));
                if (!keep.Any(l => l.Id == levelBelow.Id) && keep.Count == 0) keep.Add(levelBelow);
                return keep.OrderBy(l => l.ProjectElevation).ToList();
            }
            Level below = storeys.LastOrDefault(l => l.Id != levelBelow.Id && l.ProjectElevation <= levelBelow.ProjectElevation - gap);
            if (below != null) keep.Add(below);
            keep.Add(levelBelow);
            Level above = storeys.FirstOrDefault(l => l.Id != levelBelow.Id && l.ProjectElevation >= levelBelow.ProjectElevation + gap);
            if (above != null) keep.Add(above);
            return keep;
        }

        /// <summary>視圖裁剪框（含 Transform）八個角的世界 Z 範圍。</summary>
        private static bool TryGetViewWorldZRange(View view, out double z0, out double z1)
        {
            z0 = double.MaxValue; z1 = double.MinValue;
            try
            {
                BoundingBoxXYZ cb = view.CropBox;
                if (cb == null) return false;
                foreach (XYZ p in WorldCorners(cb)) { z0 = Math.Min(z0, p.Z); z1 = Math.Max(z1, p.Z); }
                return z1 > z0;
            }
            catch { return false; }
        }

        // ── 簡化樓層記號（ES 連結真的 Level）──
        // 固定 GUID，不要改——寫進使用者 .rvt 後靠它辨識。三個 string 欄位，不需要 Spec。
        private static readonly Guid LevelMarkSchemaGuid = new Guid("AB09D4E8-CA14-4211-B54B-4F483BCF2C5B");
        private const string LevelMarkRole = "LevelMark";
        private const string FieldLevelUniqueId = "LevelUniqueId";
        private const string LevelMarkLineStyleName = "相機Key Plan樓層線";
        private const string LevelMarkTextTypeName = "相機Key Plan樓層字";
        internal const string LevelMarkUpdateTransactionName = "更新 Key Plan 樓層線";
        private const double LevelMarkTextSizeMm = 2.0;

        private static Schema GetOrCreateLevelMarkSchema()
        {
            Schema schema = Schema.Lookup(LevelMarkSchemaGuid);
            if (schema != null) return schema;
            SchemaBuilder b = new SchemaBuilder(LevelMarkSchemaGuid);
            b.SetSchemaName("RevitMCP_CameraKeyPlanLevelMark");
            b.SetReadAccessLevel(AccessLevel.Public);
            b.SetWriteAccessLevel(AccessLevel.Public);
            b.AddSimpleField(FieldCameraUniqueId, typeof(string));
            b.AddSimpleField(FieldRole, typeof(string));
            b.AddSimpleField(FieldLevelUniqueId, typeof(string));
            return b.Finish();
        }

        private static void TagLevelMark(Element el, string cid, string levelUid)
        {
            Schema schema = GetOrCreateLevelMarkSchema();
            Entity e = new Entity(schema);
            e.Set(schema.GetField(FieldCameraUniqueId), cid ?? "");
            e.Set(schema.GetField(FieldRole), LevelMarkRole);
            e.Set(schema.GetField(FieldLevelUniqueId), levelUid ?? "");
            el.SetEntity(e);
        }

        private static bool TryReadLevelMark(Element el, out string cid, out string levelUid)
        {
            cid = null; levelUid = null;
            try
            {
                Schema schema = Schema.Lookup(LevelMarkSchemaGuid);
                if (schema == null || el == null) return false;
                Entity e = el.GetEntity(schema);
                if (e == null || !e.IsValid()) return false;
                if (e.Get<string>(schema.GetField(FieldRole)) != LevelMarkRole) return false;
                cid = e.Get<string>(schema.GetField(FieldCameraUniqueId));
                levelUid = e.Get<string>(schema.GetField(FieldLevelUniqueId));
                return true;
            }
            catch { return false; }
        }

        /// <summary>刪除這張視圖內（cid＝null 時不分相機）的簡化樓層記號，回傳刪除數。須在交易內。</summary>
        private static int DeleteLevelMarks(Document doc, View view, string cid)
        {
            if (Schema.Lookup(LevelMarkSchemaGuid) == null) return 0;
            List<ElementId> ids = new FilteredElementCollector(doc).OwnedByView(view.Id).WherePasses(new ExtensibleStorageFilter(LevelMarkSchemaGuid))
                .Where(e => TryReadLevelMark(e, out string c, out _) && (cid == null || c == cid))
                .Select(e => e.Id).ToList();
            if (ids.Count > 0) doc.Delete(ids);
            return ids.Count;
        }

        /// <summary>逐元素隱藏這張剖面內「不是我們的」TextNote（我們的樓層字除外）。回傳本次隱藏數。</summary>
        private static int HideForeignTextNotes(View view)
        {
            try
            {
                Document doc = view.Document;
                List<ElementId> ids = new FilteredElementCollector(doc, view.Id).OfClass(typeof(TextNote))
                    .Where(e => !TryReadLevelMark(e, out _, out _))
                    .Where(e => !(TryReadLink(e, out _, out string r) && r == MarkerNameRole)) // 2026-09-25 Wave 2：樓層總覽的相機名稱
                    .Where(e => { try { return !e.IsHidden(view) && e.CanBeHidden(view); } catch { return false; } })
                    .Select(e => e.Id).ToList();
                if (ids.Count > 0) view.HideElements(ids);
                return ids.Count;
            }
            catch (Exception ex) { Logger.Error("相機 Key Plan：隱藏剖面內其他文字失敗（略過）", ex); return 0; }
        }

        private static int UnhideTextNotes(View view)
        {
            try
            {
                Document doc = view.Document;
                // 被隱藏的元素不在一般視圖 collector 內 → 用全文件 TextNote 過濾 OwnerViewId。
                List<ElementId> ids = new FilteredElementCollector(doc).OfClass(typeof(TextNote))
                    .Where(e => e.OwnerViewId == view.Id)
                    .Where(e => { try { return e.IsHidden(view); } catch { return false; } })
                    .Select(e => e.Id).ToList();
                if (ids.Count > 0) view.UnhideElements(ids);
                return ids.Count;
            }
            catch (Exception ex) { Logger.Error("相機 Key Plan：取消隱藏剖面內文字失敗（略過）", ex); return 0; }
        }

        /// <summary>線型「相機Key Plan樓層線」：灰 160、線寬 1、有虛線樣式就用虛線；只在第一次建立時設定。</summary>
        private static GraphicsStyle GetOrCreateLevelMarkLineStyle(Document doc)
        {
            try
            {
                Category lines = Category.GetCategory(doc, BuiltInCategory.OST_Lines);
                if (lines == null) return null;
                Category sub = null;
                foreach (Category c in lines.SubCategories) if (c.Name == LevelMarkLineStyleName) { sub = c; break; }
                if (sub == null)
                {
                    sub = doc.Settings.Categories.NewSubcategory(lines, LevelMarkLineStyleName);
                    sub.LineColor = new Color(160, 160, 160);
                    try { sub.SetLineWeight(1, GraphicsStyleType.Projection); } catch { }
                    string pat = "實線";
                    try
                    {
                        LinePatternElement dash = new FilteredElementCollector(doc).OfClass(typeof(LinePatternElement)).Cast<LinePatternElement>()
                            .Where(x => x.Name != null && (x.Name.IndexOf("dash", StringComparison.OrdinalIgnoreCase) >= 0 || x.Name.Contains("虛")))
                            .OrderBy(x => x.Name, StringComparer.Ordinal).FirstOrDefault();
                        if (dash != null) { sub.SetLinePatternId(dash.Id, GraphicsStyleType.Projection); pat = dash.Name; }
                        else sub.SetLinePatternId(LinePatternElement.GetSolidPatternId(), GraphicsStyleType.Projection);
                    }
                    catch (Exception ex) { Logger.Error("相機 Key Plan：樓層線設定虛線樣式失敗（改實線）", ex); }
                    Logger.Info($"相機 Key Plan：建立線型「{LevelMarkLineStyleName}」灰 160、線寬 1、樣式={pat}");
                }
                return sub.GetGraphicsStyle(GraphicsStyleType.Projection);
            }
            catch (Exception ex)
            {
                Logger.Error($"相機 Key Plan：建立/取得線型「{LevelMarkLineStyleName}」失敗，樓層線改用預設線型", ex);
                return null;
            }
        }

        /// <summary>文字類型「相機Key Plan樓層字」：複製第一個文字類型，約 2 mm、背景透明、灰 128；只在第一次建立時設定。</summary>
        private static ElementId GetOrCreateLevelMarkTextType(Document doc)
        {
            List<TextNoteType> all = new FilteredElementCollector(doc).OfClass(typeof(TextNoteType)).Cast<TextNoteType>().ToList();
            TextNoteType named = all.FirstOrDefault(t => t.Name == LevelMarkTextTypeName);
            if (named != null) return named.Id;
            TextNoteType src = all.OrderBy(t => t.Name, StringComparer.Ordinal).FirstOrDefault();
            if (src == null) return ElementId.InvalidElementId;
            try
            {
                TextNoteType t = src.Duplicate(LevelMarkTextTypeName) as TextNoteType;
                if (t == null) return src.Id;
                try { t.get_Parameter(BuiltInParameter.TEXT_SIZE)?.Set(CameraMath.MmToFeet(LevelMarkTextSizeMm)); } catch (Exception ex) { Logger.Error("相機 Key Plan：樓層字大小設定失敗", ex); }
                try { t.get_Parameter(BuiltInParameter.TEXT_BACKGROUND)?.Set(1); } catch (Exception ex) { Logger.Error("相機 Key Plan：樓層字背景透明設定失敗", ex); }
                try { t.get_Parameter(BuiltInParameter.LINE_COLOR)?.Set(128 + 128 * 256 + 128 * 65536); } catch (Exception ex) { Logger.Error("相機 Key Plan：樓層字顏色設定失敗", ex); }
                Logger.Info($"相機 Key Plan：建立文字類型「{LevelMarkTextTypeName}」（複製自「{src.Name}」，{LevelMarkTextSizeMm}mm、背景透明、灰 128）");
                return t.Id;
            }
            catch (Exception ex)
            {
                Logger.Error($"相機 Key Plan：建立文字類型「{LevelMarkTextTypeName}」失敗，改用「{src.Name}」", ex);
                return src.Id;
            }
        }

        /// <summary>樓層字：「樓層名  +高程」，高程用 Level.Elevation（同 Revit 樓層標頭，相對其高程基準）依專案單位格式化。</summary>
        private static string FormatLevelMarkText(Document doc, Level l)
        {
            string elev;
            try
            {
                double v = l.Elevation;
                string f = UnitFormatUtils.Format(doc.GetUnits(), SpecTypeId.Length, Math.Abs(v), false);
                elev = (v < -1e-9 ? "-" : "+") + f;
            }
            catch { elev = $"{(l.Elevation < 0 ? "-" : "+")}{Math.Abs(CameraMath.FeetToMm(l.Elevation)):F0} mm"; }
            return $"{l.Name}  {elev}";
        }

        /// <summary>
        /// 刪除這台相機在這張剖面的舊樓層記號，再依 keep 畫：每層一條橫跨裁剪寬度的詳圖線（剖面平面上、Z＝樓層高程）
        /// ＋左端上方一個 TextNote。全部以 ES 連結 Level。回傳建立的元素數。須在交易內。
        /// </summary>
        private static int DrawSectionLevelMarks(Document doc, View view, IList<Level> keep, string cid)
        {
            int removed = DeleteLevelMarks(doc, view, cid);
            if (keep == null || keep.Count == 0) return 0;
            BoundingBoxXYZ crop = view.CropBox;
            if (crop == null) return 0;
            Transform tf = crop.Transform;
            XYZ bx = new XYZ(tf.BasisX.X, tf.BasisX.Y, 0);
            if (bx.GetLength() < 1e-6) return 0;
            bx = bx.Normalize();
            XYZ n = view.ViewDirection;
            n = (n != null && n.GetLength() > 1e-6) ? n.Normalize() : tf.BasisZ;
            double minX = Math.Min(crop.Min.X, crop.Max.X), maxX = Math.Max(crop.Min.X, crop.Max.X);
            XYZ OnPlane(double x, double z)
            {
                XYZ b = tf.Origin + tf.BasisX * x;
                b = b - n * (b - view.Origin).DotProduct(n);
                return new XYZ(b.X, b.Y, z);
            }
            GraphicsStyle style = GetOrCreateLevelMarkLineStyle(doc);
            ElementId textType = GetOrCreateLevelMarkTextType(doc);
            int scale = view.Scale > 0 ? view.Scale : 100;
            double textLift = CameraMath.MmToFeet((LevelMarkTextSizeMm + 1.0) * scale);
            double textInset = CameraMath.MmToFeet(1.0 * scale);
            int created = 0;
            var names = new List<string>();
            foreach (Level l in keep)
            {
                try
                {
                    double z = l.ProjectElevation;
                    XYZ a = OnPlane(minX, z), b = OnPlane(maxX, z);
                    if (a.DistanceTo(b) < 1e-6) continue;
                    DetailCurve dc = doc.Create.NewDetailCurve(view, Line.CreateBound(a, b));
                    if (style != null) { try { dc.LineStyle = style; } catch { } }
                    TagLevelMark(dc, cid, l.UniqueId);
                    created++;
                    if (textType != ElementId.InvalidElementId)
                    {
                        XYZ tp = OnPlane(minX, z + textLift) + bx * textInset;
                        TextNote tn = TextNote.Create(doc, view.Id, tp, FormatLevelMarkText(doc, l), textType);
                        TagLevelMark(tn, cid, l.UniqueId);
                        created++;
                    }
                    names.Add(FormatLevelMarkText(doc, l));
                }
                catch (Exception ex) { Logger.Error($"相機 Key Plan：剖面「{view.Name}」畫樓層記號 {l.Name} 失敗（略過）", ex); }
            }
            Logger.Info($"相機 Key Plan：剖面「{view.Name}」簡化樓層記號：刪除舊 {removed} 個、建立 {created} 個 [{string.Join("；", names)}]");
            return created;
        }

        /// <summary>
        /// 2026-09-25（§5.1 簡化樓層記號自動同步）：主體 Level 改高程／改名後，由面板去抖排入。掃描所有帶 LevelMark 的
        /// 元素，依「剖面視圖＋相機」分組，用目前的 Level 值整組重畫（同一組保留原本的樓層集合）。單一交易
        /// 「更新 Key Plan 樓層線」，期間 IsUpdatingMarkers＝true（Application 的 DocumentChanged 據此忽略）。
        /// 回傳重畫的組數。須在 API context（ExternalEvent）內。
        /// </summary>
        internal static int UpdateLevelMarksFromLevels(Document doc)
        {
            if (doc == null || Schema.Lookup(LevelMarkSchemaGuid) == null) return 0;
            var groups = new Dictionary<string, Tuple<View, string, HashSet<string>>>(StringComparer.Ordinal);
            foreach (Element e in new FilteredElementCollector(doc).WhereElementIsNotElementType().OfClass(typeof(CurveElement)).Cast<Element>()
                         .Concat(new FilteredElementCollector(doc).OfClass(typeof(TextNote))))
            {
                if (!TryReadLevelMark(e, out string cid, out string luid)) continue;
                if (!(doc.GetElement(e.OwnerViewId) is View v)) continue;
                string key = v.Id.GetIdValue() + "|" + cid;
                if (!groups.TryGetValue(key, out var g)) groups[key] = g = Tuple.Create(v, cid, new HashSet<string>(StringComparer.Ordinal));
                if (!string.IsNullOrEmpty(luid)) g.Item3.Add(luid);
            }
            if (groups.Count == 0) return 0;
            int done = 0;
            IsUpdatingMarkers = true;
            try
            {
                using (Transaction t = TransactionHelper.Begin(doc, LevelMarkUpdateTransactionName))
                {
                    t.Start();
                    try
                    {
                        List<Level> storeys = null;
                        foreach (var g in groups.Values)
                        {
                            List<Level> levels = null;
                            // 2026-09-26 稽核：舊版只重畫「已經畫過的那幾條樓層」→ 新插入的樓層、或移進／移出保留範圍的樓層不會更新。
                            // 剖面且相機仍在 → 依相機目前樓層重算保留集合（與一鍵 4 視圖同一規則）；算不出來才沿用既有集合。
                            try
                            {
                                if (g.Item1 is ViewSection && doc.GetElement(g.Item2) is View3D cam && cam.IsPerspective && !cam.IsTemplate)
                                {
                                    if (storeys == null) storeys = CameraMath.GetStoreyLevelsSorted(doc);
                                    CameraParams cpL = ComputeCameraParams(cam, storeys);
                                    if (cpL?.Geom?.LevelBelow != null)
                                        levels = ComputeSectionKeepLevels(doc, cpL.Geom.LevelBelow, cpL.IsOutdoor, g.Item1);
                                }
                            }
                            catch (Exception exKeep) { Logger.Error("相機 Key Plan：重算剖面樓層記號保留集合失敗（沿用既有樓層）", exKeep); levels = null; }
                            if (levels == null || levels.Count == 0)
                                levels = g.Item3.Select(u => doc.GetElement(u) as Level).Where(l => l != null)
                                    .OrderBy(l => l.ProjectElevation).ToList();
                            DrawMarkerSafely(doc, "UpdateLevelMarks", () => DrawSectionLevelMarks(doc, g.Item1, levels, g.Item2));
                            done++;
                        }
                        TransactionHelper.CommitOrThrow(t);
                        BumpOwnViewsEpoch();
                    }
                    catch
                    {
                        try { if (t.HasStarted() && !t.HasEnded()) t.RollBack(); } catch { }
                        throw;
                    }
                }
            }
            finally { IsUpdatingMarkers = false; }
            Logger.Info($"相機 Key Plan：樓層變更 → 重畫簡化樓層記號 {done} 組（剖面×相機）");
            return done;
        }

        /// <summary>
        /// 2026-09-25：把這張 Key Plan 視圖中每個 RevitLinkInstance 的顯示設定改成「依主體視圖」（ByHostView），
        /// 讓連結模型自己的樓層／軸線／註解不顯示（使用者截圖：剖面裡堆疊的連結樓層標頭），模型幾何則照主體
        /// 視圖的類別取代（半色調）顯示。只動傳入的 Key Plan 視圖。View.Get/SetLinkOverrides 為 Revit 2024+ API；
        /// 更舊版本只記 log。之後以 FilteredElementCollector(doc, viewId, linkId) 數連結中「在這張視圖可見」的
        /// Level 數量作驗證（此計數是否完全反映顯示模式未經實機驗證，僅供參考）。
        /// </summary>
        private static void SetLinksByHostView(View view)
        {
            Document doc = view.Document;
            List<RevitLinkInstance> links;
            try { links = new FilteredElementCollector(doc).OfClass(typeof(RevitLinkInstance)).Cast<RevitLinkInstance>().ToList(); }
            catch (Exception ex) { Logger.Error($"相機 Key Plan：「{view.Name}」收集連結模型失敗", ex); return; }
            if (links.Count == 0) { Logger.Info($"相機 Key Plan：「{view.Name}」沒有連結模型"); return; }
#if REVIT2024_OR_GREATER
            var rows = new List<string>();
            var changedIds = new List<RevitLinkInstance>();
            int changed = 0, failed = 0;
            foreach (RevitLinkInstance li in links)
            {
                string name = li.Name;
                try
                {
                    bool hidden = false;
                    try { hidden = li.IsHidden(view); } catch { }
                    RevitLinkGraphicsSettings cur = null;
                    try { cur = view.GetLinkOverrides(li.Id); } catch { }
                    string before = cur == null ? "ByHostView(無取代)" : cur.LinkVisibilityType.ToString();
                    if (cur == null || cur.LinkVisibilityType == LinkVisibility.ByHostView)
                    {
                        rows.Add($"{name}: {before} 不變{(hidden ? "（此視圖隱藏）" : "")}");
                        changedIds.Add(li);
                        continue;
                    }
                    var s = new RevitLinkGraphicsSettings { LinkVisibilityType = LinkVisibility.ByHostView };
                    view.SetLinkOverrides(li.Id, s);
                    changed++;
                    changedIds.Add(li);
                    RevitLinkGraphicsSettings after = null;
                    try { after = view.GetLinkOverrides(li.Id); } catch { }
                    rows.Add($"{name}: {before}→{(after == null ? "ByHostView(無取代)" : after.LinkVisibilityType.ToString())}{(hidden ? "（此視圖隱藏）" : "")}");
                }
                catch (Exception ex)
                {
                    failed++;
                    rows.Add($"{name}: 設定失敗 {ex.Message}");
                }
            }

            // 驗證：連結中在這張視圖可見的 Level 數（需要先 Regenerate 讓設定生效）。
            string verify;
            try
            {
                doc.Regenerate();
                var counts = new List<string>();
                int total = 0;
                foreach (RevitLinkInstance li in changedIds)
                {
                    try
                    {
                        int n = new FilteredElementCollector(doc, view.Id, li.Id).OfClass(typeof(Level)).GetElementCount();
                        total += n;
                        if (n > 0) counts.Add($"{li.Name}={n}");
                    }
                    catch (Exception exC) { counts.Add($"{li.Name}=?({exC.Message})"); }
                }
                verify = $"連結中此視圖可見樓層（collector）合計 {total}" + (counts.Count > 0 ? " [" + string.Join(", ", counts) + "]" : "");
            }
            catch (Exception ex) { verify = "驗證計數失敗：" + ex.Message; }

            Logger.Info($"相機 Key Plan：「{view.Name}」連結模型顯示→依主體視圖：連結 {links.Count} 個、改設 {changed}、失敗 {failed}；" +
                        $"{verify}；[{string.Join("; ", rows)}]");
#else
            Logger.Info($"相機 Key Plan：「{view.Name}」連結模型 {links.Count} 個；此 Revit 版本沒有 View.SetLinkOverrides（2024+ API），" +
                        "連結顯示設定未變更，連結模型的樓層可能仍會顯示");
#endif
        }

        /// <summary>
        /// 2026-09-25：在指定視圖內隱藏基準面（樓層／軸線）兩端的標頭（End0、End1）。樓層標頭族群有不透明
        /// 白底，會蓋住 Key Plan 內容；這裡只動「這張視圖」的標頭可見性（HideBubbleInView 是視圖專屬設定），
        /// 不改族群／類型。每一次呼叫各自 try/catch，失敗只記錄不中斷。回傳實際隱藏的標頭數。
        /// </summary>
        private static int HideDatumBubblesInView(View view, IEnumerable<DatumPlane> datums, string what)
        {
            if (view == null || datums == null) return 0;
            int datumCount = 0, hidden = 0, alreadyHidden = 0, failed = 0;
            var failNames = new List<string>();
            foreach (DatumPlane d in datums)
            {
                if (d == null) continue;
                datumCount++;
                foreach (DatumEnds end in new[] { DatumEnds.End0, DatumEnds.End1 })
                {
                    try
                    {
                        if (!d.IsBubbleVisibleInView(end, view)) { alreadyHidden++; continue; }
                        d.HideBubbleInView(end, view);
                        hidden++;
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        if (failNames.Count < 8) failNames.Add($"{d.Name}/{end}({ex.Message})");
                    }
                }
            }
            Logger.Info($"相機 Key Plan：「{view.Name}」{what}標頭隱藏：基準面 {datumCount} 個、本次隱藏 {hidden} 個標頭、" +
                        $"原已隱藏 {alreadyHidden}、失敗 {failed}" + (failNames.Count > 0 ? " [" + string.Join("; ", failNames) + "]" : ""));
            return hidden;
        }

        /// <summary>平面 Key Plan：視圖內仍可見的樓層／軸線（正常已被 declutter 整類隱藏，這裡只是保險）兩端標頭隱藏。</summary>
        private static void HidePlanDatumBubbles(View view)
        {
            Document doc = view.Document;
            var visible = new List<DatumPlane>();
            try
            {
                foreach (Element e in new FilteredElementCollector(doc, view.Id).OfClass(typeof(Autodesk.Revit.DB.Grid)))
                {
                    try { if (e is DatumPlane dp && !e.IsHidden(view)) visible.Add(dp); } catch { }
                }
                foreach (Element e in new FilteredElementCollector(doc, view.Id).OfClass(typeof(Level)))
                {
                    try { if (e is DatumPlane dp && !e.IsHidden(view)) visible.Add(dp); } catch { }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"相機 Key Plan：平面「{view.Name}」收集可見樓層／軸線失敗", ex);
                return;
            }
            HideDatumBubblesInView(view, visible, "平面可見樓層／軸線");
        }

        // ── ④ 模型類別取代（2026-09-25 第七輪：外輪廓清楚）─────────────────────────

        /// <summary>淡化背景時「切到要清楚」的結構／圍護類別：剖切線黑、剖切填充圖案色中灰 128（不換圖案）、
        /// 投影線中灰 128（API 的半色調是整個元素一起淡，無法只淡投影線，改以投影線色模擬）。</summary>
        private static readonly string[] KeyPlanCutEmphasisCategoryNames =
        {
            "OST_Walls", "OST_StructuralColumns", "OST_Columns", "OST_Floors", "OST_Roofs", "OST_StructuralFraming",
            "OST_CurtainWallPanels", "OST_CurtainWallMullions", "OST_CurtaSystem", "OST_Stairs"
        };

        /// <summary>迭代模型類別時絕不淡化的類別：相機標記（OST_Lines 子類別「相機標記線」、FilledRegion 屬
        /// OST_DetailComponents）、剖面 viewer、剖面框／相機；OST_RvtLinks 另外處理（半色調）。</summary>
        private static readonly string[] KeyPlanNeverHalftoneCategoryNames =
        {
            "OST_Lines", "OST_DetailComponents", "OST_Viewers", "OST_SectionBox", "OST_Cameras"
        };

        private const int KeyPlanWallCutWeight = 5;
        private const int KeyPlanColumnCutWeight = 4;
        private const int KeyPlanSlabCutWeightSection = 5;
        private const int KeyPlanExteriorWallCutWeight = 7;

        private static Autodesk.Revit.DB.Color KpBlack => new Autodesk.Revit.DB.Color(0, 0, 0);
        private static Autodesk.Revit.DB.Color KpMidGrey => new Autodesk.Revit.DB.Color(128, 128, 128);
        private static Autodesk.Revit.DB.Color KpDarkGrey => new Autodesk.Revit.DB.Color(64, 64, 64);

        /// <summary>切到類別的取代設定；cutWeight=null＝不改模型線寬（只改顏色）。</summary>
        private static OverrideGraphicSettings BuildCutEmphasisOgs(int? cutWeight, Autodesk.Revit.DB.Color cutLineColor)
        {
            var ogs = new OverrideGraphicSettings();
            ogs.SetHalftone(false);
            ogs.SetCutLineColor(cutLineColor);
            if (cutWeight.HasValue) ogs.SetCutLineWeight(cutWeight.Value);
            ogs.SetCutForegroundPatternColor(KpMidGrey);
            ogs.SetCutBackgroundPatternColor(KpMidGrey);
            ogs.SetProjectionLineColor(KpMidGrey);
            return ogs;
        }

        private static int? CutWeightFor(string bicName, bool isSection)
        {
            switch (bicName)
            {
                case "OST_Walls": return KeyPlanWallCutWeight;
                case "OST_StructuralColumns":
                case "OST_Columns": return KeyPlanColumnCutWeight;
                case "OST_Floors":
                case "OST_Roofs": return isSection ? (int?)KeyPlanSlabCutWeightSection : null;
                default: return null;
            }
        }

        /// <summary>
        /// 淡化背景：① 結構／圍護類別剖切線黑（牆 5、柱 4、剖面樓板／屋頂 5，其餘不改線寬）、剖切圖案色中灰、
        /// 投影線中灰；② 其他所有可取代的模型類別（迭代 doc.Settings.Categories，CategoryType.Model）半色調，
        /// 相機標記相關類別除外；③ 連結模型 OST_RvtLinks 類別半色調（RevitLinkGraphicsSettings 無半色調屬性）；
        /// ④ 視圖內的牆逐元素：外牆（帷幕牆 WallKind.Curtain，或 WallType.Function＝Exterior）剖切線黑、線寬 7；
        /// 其他牆剖切線深灰 64、線寬 5。原始：清掉上述所有類別取代與視圖內牆的元素取代。
        /// 每類別／每面牆獨立 try/catch；冪等（每次整組覆寫）。
        /// </summary>
        private static void ApplyModelCategoryOverrides(View view, bool faded, bool isSection)
        {
            Document doc = view.Document;
            string label = $"「{view.Name}」(id={view.Id.GetIdValue()})";
            var emphasis = new HashSet<string>(KeyPlanCutEmphasisCategoryNames);
            var never = new HashSet<string>(KeyPlanNeverHalftoneCategoryNames);
            int emphasized = 0, halftoned = 0, cleared = 0, catFail = 0, skippedNotOverridable = 0;
            bool linkDone = false;

            foreach (Category c in doc.Settings.Categories)
            {
                string bn = null;
                try
                {
                    if (c == null) continue;
                    long v = c.Id.GetIdValue();
                    if (v >= 0) continue;
                    bn = Enum.GetName(typeof(BuiltInCategory), (int)v);
                    if (bn == null) continue;
                    bool isEmphasis = emphasis.Contains(bn);
                    bool isLink = bn == "OST_RvtLinks";
                    if (!isEmphasis && !isLink && c.CategoryType != CategoryType.Model) continue;
                    if (never.Contains(bn)) continue;
                    // 2026-09-25 Wave 2：A 型相機標記是一般模型族群；A 啟用時一般模型不半色調，否則標記會被淡化。
                    if (faded && bn == "OST_GenericModel" && MarkerModeIncludesA())
                    {
                        try { view.SetCategoryOverrides(c.Id, new OverrideGraphicSettings()); } catch { }
                        continue;
                    }
                    if (!view.IsCategoryOverridable(c.Id)) { skippedNotOverridable++; continue; }

                    if (!faded)
                    {
                        view.SetCategoryOverrides(c.Id, new OverrideGraphicSettings());
                        cleared++;
                    }
                    else if (isEmphasis)
                    {
                        view.SetCategoryOverrides(c.Id, BuildCutEmphasisOgs(CutWeightFor(bn, isSection), KpBlack));
                        emphasized++;
                    }
                    else
                    {
                        var ogs = new OverrideGraphicSettings();
                        ogs.SetHalftone(true);
                        view.SetCategoryOverrides(c.Id, ogs);
                        halftoned++;
                        if (isLink) linkDone = true;
                    }
                }
                catch (Exception ex)
                {
                    catFail++;
                    Logger.Error($"相機 Key Plan：{label} 類別取代 {bn ?? "?"} 失敗（略過）", ex);
                }
            }

            int ext = 0, inner = 0, wallCleared = 0, wallFail = 0;
            try
            {
                var walls = new FilteredElementCollector(doc, view.Id).OfClass(typeof(Wall)).Cast<Wall>().ToList();
                OverrideGraphicSettings extOgs = faded ? BuildCutEmphasisOgs(KeyPlanExteriorWallCutWeight, KpBlack) : null;
                OverrideGraphicSettings intOgs = faded ? BuildCutEmphasisOgs(KeyPlanWallCutWeight, KpDarkGrey) : null;
                foreach (Wall w in walls)
                {
                    try
                    {
                        if (!faded)
                        {
                            view.SetElementOverrides(w.Id, new OverrideGraphicSettings());
                            wallCleared++;
                            continue;
                        }
                        bool isExterior = false;
                        WallType wt = w.WallType;
                        if (wt != null)
                        {
                            try { isExterior = wt.Kind == WallKind.Curtain; } catch { }
                            if (!isExterior)
                            {
                                try { isExterior = wt.Function == WallFunction.Exterior; } catch { }
                            }
                        }
                        view.SetElementOverrides(w.Id, isExterior ? extOgs : intOgs);
                        if (isExterior) ext++; else inner++;
                    }
                    catch (Exception ex)
                    {
                        wallFail++;
                        if (wallFail <= 3) Logger.Error($"相機 Key Plan：{label} 牆 {w.Id.GetIdValue()} 元素取代失敗（略過）", ex);
                    }
                }
            }
            catch (Exception ex) { Logger.Error($"相機 Key Plan：{label} 收集視圖內牆失敗", ex); }

            if (faded)
                Logger.Info($"相機 Key Plan：{label} 淡化背景類別取代：強調（切到黑）{emphasized} 類、半色調 {halftoned} 類" +
                            $"（連結模型 {(linkDone ? "已半色調" : "未處理")}）、不可取代略過 {skippedNotOverridable}、失敗 {catFail}；" +
                            $"牆元素取代：外牆（黑、線寬 {KeyPlanExteriorWallCutWeight}）{ext} 面、內牆（深灰 64、線寬 {KeyPlanWallCutWeight}）{inner} 面、失敗 {wallFail}");
            else
                Logger.Info($"相機 Key Plan：{label} 原始樣式：清除類別取代 {cleared} 類、清除牆元素取代 {wallCleared} 面；" +
                            $"不可取代略過 {skippedNotOverridable}、失敗 {catFail + wallFail}");
        }

        private static bool IsKeyPlanFadedStyle()
        {
            try { return ConfigManager.Instance.Settings.CameraManager.KeyPlanFadedStyle; }
            catch { return true; }
        }


        /// <summary>本層「頂」的判定結果（純 double，交易外算好）。</summary>
        internal class StoreyTop
        {
            /// <summary>上層樓板／屋頂底面（找不到時為備援值：上一樓層高程 − 代表樓板厚（無則 300mm）；
            /// 連上一樓層都沒有時＝眼睛 +3m）。</summary>
            public double SlabBottomZ;
            /// <summary>上層樑底面最低值；沒有上層樑為 null。</summary>
            public double? BeamBottomZ;
            /// <summary>2026-09-25：眼睛所在房間的頂（房間 BoundingBox Max.Z，反映上限＋上限偏移）；無房間為 null。</summary>
            public double? RoomTopZ;
            /// <summary>2026-09-25 室外規則：不做樓層切／上層樓板判定，改用 OutdoorBottomZ／OutdoorTopZ（等角與剖面共用）。</summary>
            public bool Outdoor;
            public double OutdoorBottomZ, OutdoorTopZ;
            public string Log;
        }

        /// <summary>等角／本層頂共用的 XY 目標範圍：相機眼睛＋視野扇形（遠裁剪先夾到 30m）＋平面 margin。</summary>
        private static void ComputeFanXY(CameraMath.CameraGeometry geom, XYZ forwardHoriz, double leftRad, double rightRad, double farClipFeet,
            out double minX, out double minY, out double maxX, out double maxY, double capMm = IsoFarClipCapMm)
        {
            double reachFeet = Math.Min(farClipFeet, CameraMath.MmToFeet(capMm));
            double marginFeet = Cm(PlanMarginCm);
            XYZ dirLeft = PlanEdgeDir(forwardHoriz, leftRad);
            XYZ dirRight = PlanEdgeDir(forwardHoriz, rightRad);
            XYZ[] pts =
            {
                geom.Eye,
                geom.Eye + forwardHoriz * reachFeet,
                geom.Eye + dirLeft * reachFeet,
                geom.Eye + dirRight * reachFeet
            };
            minX = pts.Min(p => p.X) - marginFeet;
            maxX = pts.Max(p => p.X) + marginFeet;
            minY = pts.Min(p => p.Y) - marginFeet;
            maxY = pts.Max(p => p.Y) + marginFeet;
        }

        /// <summary>
        /// 2026-09-24 修訂（實測回報：「只到當層頂」＝下一樓層高程，上層樓板從該高程往下垂，整片
        /// 樓板（甚至屋頂）落在 SectionBox 內把本層蓋住）。規則：
        ///   上層樓板底＝XY 重疊、底面高於本層樓層 +300mm、頂面高於眼睛的 Floors／Roofs 中底面最低者
        ///   （天花板不算）；找不到 → 上一樓層高程 − 該樓層樓板代表厚度（中位數，無樓板則 300mm）；
        ///   連上一樓層都沒有 → 眼睛 +3m。
        ///   上層樑底＝XY 重疊、底面高於本層 +300mm、頂面高於眼睛的結構構架中底面最低者。
        /// 使用 SectionBoxLevelHelper 的「新增」方法，set_3d_section_box 既有方法與行為不動。
        /// </summary>
        private static StoreyTop ComputeStoreyTop(Document doc, CameraMath.CameraGeometry geom, IsoXY xy, List<Level> levelsSorted, RoomCtx room = null, bool outdoor = false)
        {
            var r = new StoreyTop();
            double levelZ = geom.LevelBelow.ProjectElevation;
            double tol = CameraMath.MmToFeet(IsoSupportingBeamToleranceMm);
            double minX = xy.MinX, minY = xy.MinY, maxX = xy.MaxX, maxY = xy.MaxY;

            if (outdoor)
            {
                // 2026-09-25 室外規則：Z 下限＝min(下方樓層, 模型最低樓層) −1m；Z 上限＝XY 範圍內主要模型類別
                // BoundingBox 最高點（與眼睛取大）+2m；找不到任何模型 → 眼睛 +3m。不做樓層切、不找上層樓板。
                r.Outdoor = true;
                double lowest = levelZ;
                try { if (levelsSorted != null && levelsSorted.Count > 0) lowest = Math.Min(levelZ, levelsSorted.Min(l => l.ProjectElevation)); }
                catch (Exception ex) { Logger.Error("相機 Key Plan：室外讀最低樓層失敗，用下方樓層", ex); }
                r.OutdoorBottomZ = lowest - CameraMath.MmToFeet(OutdoorBelowLevelMm);
                string topSrc;
                try
                {
                    if (SectionBoxLevelHelper.FindMaxModelTopInXY(doc, minX, minY, maxX, maxY, OutdoorModelScanZFeet, out double mt, out int n, out string topEl))
                    {
                        r.OutdoorTopZ = Math.Max(mt, geom.Eye.Z) + CameraMath.MmToFeet(OutdoorAboveModelMm);
                        topSrc = $"modelTop={CameraMath.FeetToMm(mt):F0}mm by {topEl} among {n} elements; max(modelTop, eye)+2m";
                    }
                    else
                    {
                        r.OutdoorTopZ = geom.Eye.Z + Cm(SectionAboveEyeCm);
                        topSrc = "no model elements in XY → eye+3m";
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error("相機 Key Plan：室外找模型最高點失敗，改用眼睛 +3m", ex);
                    r.OutdoorTopZ = geom.Eye.Z + Cm(SectionAboveEyeCm);
                    topSrc = "model scan failed → eye+3m";
                }
                if (!(r.OutdoorTopZ > r.OutdoorBottomZ + Cm(100))) r.OutdoorTopZ = r.OutdoorBottomZ + Cm(300);
                r.RoomTopZ = null;
                r.SlabBottomZ = r.OutdoorTopZ;
                r.Log = $"OUTDOOR levelZ={CameraMath.FeetToMm(levelZ):F0}mm lowestLevel={CameraMath.FeetToMm(lowest):F0}mm eyeZ={CameraMath.FeetToMm(geom.Eye.Z):F0}mm " +
                        $"Z[{CameraMath.FeetToMm(r.OutdoorBottomZ):F0},{CameraMath.FeetToMm(r.OutdoorTopZ):F0}]mm (bottom=min(levelBelow,lowestLevel)−1m; top: {topSrc})";
                return r;
            }

            // 2026-09-25 第五輪（挑高房間＋夾層，Loft Unit 實測：舊規則把房間內夾層當成上層樓板，只高於眼睛 22cm）：
            // 有房間頂 → 上層樓板只認「底面或頂面 ≥ 房間頂 −0.1m」者（房間內、整片低於房間頂的夾層不算）；
            // 上層樑只認頂面 ≥ 房間頂 −0.1m 者（吊在房間頂的樑）。
            if (room?.TopZ != null)
            {
                double roomTop = room.TopZ.Value;
                r.RoomTopZ = roomTop;
                double topTol = CameraMath.MmToFeet(RoomTopSlabToleranceMm);
                string src;
                try
                {
                    Element slab2 = SectionBoxLevelHelper.FindUpperSlabOrRoofAboveRoomTop(doc, minX, minY, maxX, maxY, roomTop, topTol, out double sb, out int skipped);
                    if (slab2 != null)
                    {
                        r.SlabBottomZ = sb;
                        src = $"slab/roof id={slab2.Id.GetIdValue()} ({slab2.Category?.Name}) above roomTop; skipped below-roomTop floors={skipped}";
                    }
                    else
                    {
                        r.SlabBottomZ = double.PositiveInfinity;
                        src = $"no slab above roomTop (skipped below-roomTop floors={skipped})";
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error("相機 Key Plan：找房間頂以上的樓板失敗，只用房間頂", ex);
                    r.SlabBottomZ = double.PositiveInfinity;
                    src = "slab search failed";
                }
                try
                {
                    if (SectionBoxLevelHelper.FindLowestUpperBeamBottom(doc, minX, minY, maxX, maxY, levelZ, roomTop - topTol, tol, out double bb, out int nb))
                        r.BeamBottomZ = bb;
                }
                catch (Exception ex) { Logger.Error("相機 Key Plan：找上層樑失敗，略過", ex); }
                r.Log = $"levelZ={CameraMath.FeetToMm(levelZ):F0}mm eyeZ={CameraMath.FeetToMm(geom.Eye.Z):F0}mm roomTop={CameraMath.FeetToMm(roomTop):F0}mm " +
                        $"slabBottom={(double.IsInfinity(r.SlabBottomZ) ? "none" : CameraMath.FeetToMm(r.SlabBottomZ).ToString("F0") + "mm")} [{src}] " +
                        $"beamBottom={(r.BeamBottomZ.HasValue ? CameraMath.FeetToMm(r.BeamBottomZ.Value).ToString("F0") + "mm" : "none")}";
                return r;
            }

            string slabSrc;
            Element slab = null;
            double slabBottom = double.NaN;
            try
            {
                slab = SectionBoxLevelHelper.FindNearestUpperSlabOrRoof(doc, minX, minY, maxX, maxY, levelZ, geom.Eye.Z, tol, out slabBottom);
            }
            catch (Exception ex) { Logger.Error("相機 Key Plan：找上層樓板失敗，改用備援", ex); slab = null; }

            if (slab != null)
            {
                r.SlabBottomZ = slabBottom;
                slabSrc = $"slab/roof id={slab.Id.GetIdValue()} ({slab.Category?.Name})";
            }
            else
            {
                Level next = levelsSorted?.FirstOrDefault(l => l.ProjectElevation > levelZ + 1e-6);
                if (next != null)
                {
                    double? t = null;
                    try { t = SectionBoxLevelHelper.EstimateTypicalSlabThickness(doc, next.Id); } catch { t = null; }
                    double thick = t ?? CameraMath.MmToFeet(DefaultSlabThicknessMm);
                    r.SlabBottomZ = next.ProjectElevation - thick;
                    slabSrc = $"fallback nextLevel '{next.Name}' - thickness {CameraMath.FeetToMm(thick):F0}mm ({(t.HasValue ? "median of floors at that level" : "default 300mm")})";
                }
                else
                {
                    r.SlabBottomZ = geom.Eye.Z + Cm(300);
                    slabSrc = "fallback no upper level: eye+3m";
                }
            }

            try
            {
                if (SectionBoxLevelHelper.FindLowestUpperBeamBottom(doc, minX, minY, maxX, maxY, levelZ, geom.Eye.Z, tol, out double beamBottom, out int n))
                    r.BeamBottomZ = beamBottom;
            }
            catch (Exception ex) { Logger.Error("相機 Key Plan：找上層樑失敗，略過", ex); }

            r.Log = $"levelZ={CameraMath.FeetToMm(levelZ):F0}mm eyeZ={CameraMath.FeetToMm(geom.Eye.Z):F0}mm " +
                    $"slabBottom={CameraMath.FeetToMm(r.SlabBottomZ):F0}mm [{slabSrc}] " +
                    $"beamBottom={(r.BeamBottomZ.HasValue ? CameraMath.FeetToMm(r.BeamBottomZ.Value).ToString("F0") + "mm" : "none")}";
            return r;
        }

        /// <summary>
        /// 等角視角範圍：
        ///   XY：<see cref="ComputeFanXY"/>（相機眼睛＋視野扇形，遠裁剪夾到 30m，加平面 margin）。
        ///   Z 下限：相機所在樓層高程，往下用「支撐樑」延伸（規則同 set_3d_section_box）。
        ///   Z 上限（2026-09-24 修訂，兩種模式都要「從上方看得到本層」）：
        ///     - FullWithUpperBeams「等角：整層（見上層樑）」：上層樓板底 −10mm（上層樑看得到、樓板切掉）。
        ///     - CurrentLevelTopOnly「等角：只到當層（不含上層）」：min(上層樓板底, 上層樑底) −10mm。
        ///   上層樓板底的判定與備援見 <see cref="ComputeStoreyTop"/>。
        /// </summary>
        private static BoundingBoxXYZ BuildIsoSectionBox(Document doc, CameraMath.CameraGeometry geom, IsoXY xy, StoreyTop storeyTop)
        {
            double targetMinX = xy.MinX, targetMinY = xy.MinY, targetMaxX = xy.MaxX, targetMaxY = xy.MaxY;
            double levelZ = geom.LevelBelow.ProjectElevation;
            double toleranceFeet = CameraMath.MmToFeet(IsoSupportingBeamToleranceMm);

            if (storeyTop != null && storeyTop.Outdoor)
            {
                // 2026-09-25 室外規則：XY＝扇形外接框 +2m（IsoXY），Z＝StoreyTop 室外範圍；不找支撐樑／本層樓板／上層樓板。
                Logger.Info($"相機 Key Plan：等角範圍（室外）X[{CameraMath.FeetToMm(targetMinX):F0},{CameraMath.FeetToMm(targetMaxX):F0}] " +
                            $"Y[{CameraMath.FeetToMm(targetMinY):F0},{CameraMath.FeetToMm(targetMaxY):F0}] Z[{CameraMath.FeetToMm(storeyTop.OutdoorBottomZ):F0},{CameraMath.FeetToMm(storeyTop.OutdoorTopZ):F0}]mm");
                return new BoundingBoxXYZ
                {
                    Transform = Transform.Identity,
                    Min = new XYZ(targetMinX, targetMinY, storeyTop.OutdoorBottomZ),
                    Max = new XYZ(targetMaxX, targetMaxY, storeyTop.OutdoorTopZ)
                };
            }

            // 2026-09-24 第四輪：XY 只由 IsoXY（看得到的房間）決定——支撐樑只用來往下延伸 Z，
            // 不再把整根樑的 XY 聯集進來（長樑會把範圍拉到整排單元）。
            double minX = targetMinX, minY = targetMinY, maxX = targetMaxX, maxY = targetMaxY;
            double bx0 = targetMinX, by0 = targetMinY, bx1 = targetMaxX, by1 = targetMaxY, beamMinZ = levelZ;
            int beamCount = 0;
            try
            {
                beamCount = SectionBoxLevelHelper.IncludeSupportingBeams(doc,
                    targetMinX, targetMinY, targetMaxX, targetMaxY, levelZ, toleranceFeet,
                    ref bx0, ref by0, ref beamMinZ, ref bx1, ref by1);
            }
            catch (Exception ex) { Logger.Error("相機 Key Plan：找支撐樑失敗，略過", ex); beamMinZ = levelZ; }

            // Z 下限（2026-09-24 第四輪，使用者回報「沒看到樓板」）：舊規則下限＝樓層高程，樓板頂剛好在
            // 樓層高程、板身在下方 → 整片被切掉。新規則：min(本層樓板底 −50mm, 支撐樑底)；本層樓板＝
            // XY 與範圍重疊、頂面在樓層高程 ±300mm 內的 Floors（取 BoundingBox 最低 Z）；找不到 → 樓層 −300mm。
            string bottomSrc;
            double floorBased;
            if (TryFindFloorBottomAtLevel(doc, targetMinX, targetMinY, targetMaxX, targetMaxY, levelZ, toleranceFeet,
                    out double floorBottom, out int floorCount, out string floorIds))
            {
                floorBased = floorBottom - CameraMath.MmToFeet(IsoFloorBelowClearanceMm);
                bottomSrc = $"floors[{floorCount}] ({floorIds}) bottom={CameraMath.FeetToMm(floorBottom):F0}mm −{IsoFloorBelowClearanceMm:F0}mm";
            }
            else
            {
                floorBased = levelZ - CameraMath.MmToFeet(DefaultSlabThicknessMm);
                bottomSrc = $"no floor at level → level −{DefaultSlabThicknessMm:F0}mm";
            }
            double minZ = floorBased;
            if (beamCount > 0 && beamMinZ < minZ)
            {
                minZ = beamMinZ;
                bottomSrc += $"; supporting beams[{beamCount}] bottom={CameraMath.FeetToMm(beamMinZ):F0}mm (chosen)";
            }
            else if (beamCount > 0)
            {
                bottomSrc += $"; supporting beams[{beamCount}] bottom={CameraMath.FeetToMm(beamMinZ):F0}mm (higher, not chosen)";
            }
            Logger.Info($"相機 Key Plan：等角 Z 下限={CameraMath.FeetToMm(minZ):F0}mm levelZ={CameraMath.FeetToMm(levelZ):F0}mm 來源：{bottomSrc}");

            CameraIsoExtentMode mode = GetIsoExtentMode();
            double clearanceFeet = CameraMath.MmToFeet(IsoUpperSlabClearanceMm);
            double maxZ;
            if (storeyTop.RoomTopZ.HasValue)
            {
                // 2026-09-25：房間頂 +0.3m 為主，再與（房間頂以上的）上層樓板底 −10mm 取小；「只到當層」再與上層樑底 −10mm 取小。
                maxZ = storeyTop.RoomTopZ.Value + CameraMath.MmToFeet(RoomTopIsoBufferMm);
                if (!double.IsInfinity(storeyTop.SlabBottomZ) && !double.IsNaN(storeyTop.SlabBottomZ))
                    maxZ = Math.Min(maxZ, storeyTop.SlabBottomZ - clearanceFeet);
                if (mode == CameraIsoExtentMode.CurrentLevelTopOnly && storeyTop.BeamBottomZ.HasValue)
                    maxZ = Math.Min(maxZ, storeyTop.BeamBottomZ.Value - clearanceFeet);
            }
            else
            {
                double top = storeyTop.SlabBottomZ;
                if (mode == CameraIsoExtentMode.CurrentLevelTopOnly && storeyTop.BeamBottomZ.HasValue)
                    top = Math.Min(top, storeyTop.BeamBottomZ.Value);
                maxZ = top - clearanceFeet;
            }

            if (maxZ <= minZ) maxZ = minZ + Cm(300); // 保底：極端情形（找到的頂低於底）絕不做出負高度的 SectionBox
            Logger.Info($"相機 Key Plan：等角範圍 mode={mode} topZ={CameraMath.FeetToMm(maxZ):F0}mm minZ={CameraMath.FeetToMm(minZ):F0}mm");

            return new BoundingBoxXYZ
            {
                Transform = Transform.Identity,
                Min = new XYZ(minX, minY, minZ),
                Max = new XYZ(maxX, maxY, maxZ)
            };
        }

        /// <summary>找出 levelsSorted 中，高程略高於 aboveZ 的下一個樓層高程；levelsSorted 必須已依高程排序。</summary>
        private static double? FindNextLevelElevation(List<Level> levelsSorted, double aboveZ)
        {
            Level next = levelsSorted?.FirstOrDefault(l => l.ProjectElevation > aboveZ + 1e-6);
            return next?.ProjectElevation;
        }

        // ══════════════════════════════════════════════════════════
        // 2026-09-24 第四輪：相機參數共用、等角「看得到的房間」XY、本層樓板底、標記即時重畫
        // ══════════════════════════════════════════════════════════

        /// <summary>一鍵 4 視圖與「更新相機標記」共用的相機衍生參數（遠裁剪、水平/垂直半視角、水平前方向、
        /// 眼睛所在房間、扇形半徑）。</summary>
        internal sealed class CameraParams
        {
            public CameraMath.CameraGeometry Geom;
            public XYZ ForwardHoriz;
            /// <summary>2026-09-25「畫面中心線」：水平視錐左右緣的角平分線（非對稱裁剪時≠相機正前方），
            /// ＝PlanEdgeDir(ForwardHoriz, (LeftRad+RightRad)/2)。平面視線、剖面位置/方向、扇形半徑都用它。</summary>
            public XYZ CenterHoriz;
            public double CenterYawDeg;
            public double FarClipFeet;
            public double HalfFovRad;
            public double HalfVFovRad;
            /// <summary>2026-09-25 非對稱視錐：水平左/右緣（相機右為正）、垂直下/上緣（上為正）相對正前方的帶號角度（rad）。
            /// 平面扇形＝RotateHorizontal(forwardHoriz, −edge)（RotateHorizontal 是逆時針為正、相機右在順時針側）；
            /// 剖面扇形＝pitch + DownRad … pitch + UpRad。算不出 FOV 時退回對稱 ±Half*。</summary>
            public double LeftRad, RightRad, DownRad, UpRad;
            public string EdgesLog;
            public double? FovDeg;
            public double? VFovDeg;
            public string FovLog;
            public RoomCtx Room;
            /// <summary>平面／剖面扇形半徑（feet）：視線離開房間的距離 +0.6m，夾在 [1.5m, 遠裁剪]；無房間＝min(遠裁剪, 8m)。</summary>
            public double FanRadiusFeet;
            public string FanLog;
            /// <summary>2026-09-25 室內／室外判定：眼睛所在房間（FindEyeRoom）找得到＝室內，否則＝室外。</summary>
            public bool IsOutdoor;
            public string ClassLog;
        }

        private static CameraParams ComputeCameraParams(View3D camera, List<Level> levels)
        {
            var cp = new CameraParams();
            cp.Geom = CameraMath.ComputeGeometry(camera, levels);

            Parameter farClipParam = CameraMath.FindFarClipOffsetParameter(camera);
            double farClipFeet = farClipParam != null ? farClipParam.AsDouble() : Cm(DefaultFarClipCm);
            if (!(farClipFeet > 1e-6)) farClipFeet = Cm(DefaultFarClipCm);
            cp.FarClipFeet = farClipFeet;

            // 2026-09-25：FOV 改由 CameraMath.ComputePerspectiveFov 統一計算（清單欄位、迷你地圖、平面/剖面扇形共用）。
            CameraMath.PerspectiveFov fov = CameraMath.ComputePerspectiveFov(camera);
            cp.FovDeg = fov.HFovDeg;
            cp.VFovDeg = fov.VFovDeg;
            cp.FovLog = fov.Log;
            cp.HalfFovRad = (cp.FovDeg ?? DefaultFovDeg) / 2.0 * Math.PI / 180.0;
            cp.HalfVFovRad = (cp.VFovDeg ?? (cp.FovDeg ?? DefaultFovDeg) * 0.75) / 2.0 * Math.PI / 180.0;
            const double D2R = Math.PI / 180.0;
            bool asym = fov.LeftDeg.HasValue && fov.RightDeg.HasValue && fov.DownDeg.HasValue && fov.UpDeg.HasValue
                        && fov.RightDeg.Value > fov.LeftDeg.Value && fov.UpDeg.Value > fov.DownDeg.Value;
            cp.LeftRad = asym ? fov.LeftDeg.Value * D2R : -cp.HalfFovRad;
            cp.RightRad = asym ? fov.RightDeg.Value * D2R : cp.HalfFovRad;
            cp.DownRad = asym ? fov.DownDeg.Value * D2R : -cp.HalfVFovRad;
            cp.UpRad = asym ? fov.UpDeg.Value * D2R : cp.HalfVFovRad;
            cp.EdgesLog = $"leftDeg={cp.LeftRad / D2R:F1} rightDeg={cp.RightRad / D2R:F1} downDeg={cp.DownRad / D2R:F1} upDeg={cp.UpRad / D2R:F1}" +
                          (asym ? $" (source={fov.Source})" : " (symmetric fallback)");

            XYZ fh = new XYZ(cp.Geom.Forward.X, cp.Geom.Forward.Y, 0);
            cp.ForwardHoriz = fh.GetLength() > 1e-6 ? fh.Normalize() : XYZ.BasisY;
            try
            {
                XYZ ch = PlanEdgeDir(cp.ForwardHoriz, (cp.LeftRad + cp.RightRad) / 2.0);
                cp.CenterHoriz = ch.GetLength() > 1e-6 ? new XYZ(ch.X, ch.Y, 0).Normalize() : cp.ForwardHoriz;
            }
            catch { cp.CenterHoriz = cp.ForwardHoriz; }
            cp.CenterYawDeg = CameraMath.ComputeHorizontalAngleDeg(cp.CenterHoriz);
            cp.EdgesLog += $" | 中心線 yaw={cp.CenterYawDeg:F1}°（相機 yaw={CameraMath.ComputeHorizontalAngleDeg(cp.ForwardHoriz):F1}°，偏 {(cp.LeftRad + cp.RightRad) / 2.0 / D2R:F1}°）";

            // 眼睛所在房間＋扇形半徑（任何失敗 → 無房間規則）
            double minR = CameraMath.MmToFeet(FanMinRadiusMm);
            try
            {
                cp.Room = FindEyeRoom(camera.Document, camera, cp.Geom); // 2026-09-25：不再需要下方樓層（房間搜尋以眼睛 XY＋垂直範圍）
            }
            catch (Exception ex)
            {
                Logger.Error("相機 Key Plan：找眼睛所在房間失敗，改用無房間規則", ex);
                cp.Room = null;
            }
            cp.IsOutdoor = cp.Room?.Room == null;
            cp.ClassLog = cp.IsOutdoor
                ? "室外（" + (cp.Geom.LevelBelow == null ? "找不到下方樓層" : "眼睛不在任何房間：" + (cp.Room?.Log ?? "room lookup failed")) + "）"
                : "室內（房間 id=" + cp.Room.Room.Id.GetIdValue() + "）";
            if (cp.IsOutdoor)
            {
                double outdoorR = GetOutdoorRadiusFeet(out double outdoorM);
                double ro = Math.Max(minR, Math.Min(farClipFeet, outdoorR));
                cp.FanRadiusFeet = ro;
                cp.FanLog = $"outdoor → min(farClip {CameraMath.FeetToMm(farClipFeet):F0}mm, 室外半徑 {outdoorM:F0}m) = {CameraMath.FeetToMm(ro):F0}mm (≥1.5m)";
                return cp;
            }
            double? exit = null;
            if (cp.Room?.Room != null)
            {
                try { exit = RayExitDistance(cp.Room, cp.Geom.Eye, cp.CenterHoriz); } // 2026-09-25：沿畫面中心線
                catch (Exception ex) { Logger.Error("相機 Key Plan：視線離開房間距離計算失敗", ex); exit = null; }
            }
            double r;
            if (exit.HasValue)
            {
                r = exit.Value + CameraMath.MmToFeet(RoomBufferMm);
                cp.FanLog = $"exit={CameraMath.FeetToMm(exit.Value):F0}mm +0.6m";
            }
            else
            {
                r = Math.Min(farClipFeet, CameraMath.MmToFeet(NoRoomFanCapMm));
                cp.FanLog = cp.Room?.Room != null ? "room but no exit hit → min(farClip,8m)" : "no room → min(farClip,8m)";
            }
            r = Math.Min(farClipFeet, Math.Max(minR, r));
            cp.FanRadiusFeet = r;
            cp.FanLog += $" → clamp[1.5m, farClip {CameraMath.FeetToMm(farClipFeet):F0}mm]";
            return cp;
        }

        /// <summary>室外範圍半徑（設定，m → feet）；讀設定失敗用預設 60m。</summary>
        private static double GetOutdoorRadiusFeet(out double meters)
        {
            meters = CameraManagerSettings.DefaultOutdoorRadiusM;
            try { meters = ConfigManager.Instance.Settings.CameraManager.GetEffectiveOutdoorRadiusM(); }
            catch (Exception ex) { Logger.Error("相機 Key Plan：讀取室外範圍半徑設定失敗，用預設 60m", ex); }
            return CameraMath.MmToFeet(meters * 1000.0);
        }

        /// <summary>
        /// 清單列用的室內／室外判定（與一鍵 4 視圖同一套 FindEyeRoom）：true＝室外、false＝室內、null＝無法判定
        /// （找不到下方樓層或查詢例外）。outdoorFanRadiusFeet＝室外時的扇形半徑 min(遠裁剪, 室外半徑)，室內為 0。
        /// 呼叫端須在 API context。
        /// </summary>
        internal static bool? ClassifyIndoorOutdoor(View3D camera, CameraMath.CameraGeometry geom, out double outdoorFanRadiusFeet, out string log)
        {
            outdoorFanRadiusFeet = 0;
            log = null;
            if (camera == null || geom == null) { log = "no camera"; return null; }
            try
            {
                RoomCtx rc = FindEyeRoom(camera.Document, camera, geom);
                if (rc?.Room != null) { log = "室內 " + rc.Log; return false; }
                Parameter farClipParam = CameraMath.FindFarClipOffsetParameter(camera);
                double farClipFeet = farClipParam != null ? farClipParam.AsDouble() : Cm(DefaultFarClipCm);
                if (!(farClipFeet > 1e-6)) farClipFeet = Cm(DefaultFarClipCm);
                double r = GetOutdoorRadiusFeet(out _);
                outdoorFanRadiusFeet = Math.Max(CameraMath.MmToFeet(FanMinRadiusMm), Math.Min(farClipFeet, r));
                log = "室外 " + (rc?.Log ?? "");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error("相機管理：室內／室外判定失敗（清單顯示 ?）", ex);
                log = "failed: " + ex.Message;
                return null;
            }
        }

        /// <summary>室外扇形外接框（XY，模型座標）：眼睛＋左緣到右緣弧上 16 等分取樣點（含兩緣端點），半徑 radiusFeet，再外擴 marginFeet。</summary>
        private static void ComputeSampledFanBox(XYZ eye, XYZ forwardHoriz, double leftRad, double rightRad, double radiusFeet, double marginFeet,
            out double minX, out double minY, out double maxX, out double maxY)
        {
            minX = maxX = eye.X; minY = maxY = eye.Y;
            const int samples = 16;
            for (int i = 0; i <= samples; i++)
            {
                double a = leftRad + (rightRad - leftRad) * i / samples;
                XYZ p = eye + PlanEdgeDir(forwardHoriz, a) * radiusFeet;
                minX = Math.Min(minX, p.X); maxX = Math.Max(maxX, p.X);
                minY = Math.Min(minY, p.Y); maxY = Math.Max(maxY, p.Y);
            }
            minX -= marginFeet; minY -= marginFeet; maxX += marginFeet; maxY += marginFeet;
        }

        /// <summary>眼睛所在房間（2026-09-25）：房間元素、平面輪廓點/線段（XY）、輪廓外接框（不含 buffer）、房間頂。</summary>
        internal sealed class RoomCtx
        {
            public Element Room;
            public List<XYZ> Pts = new List<XYZ>();
            public List<KeyValuePair<XYZ, XYZ>> Segs = new List<KeyValuePair<XYZ, XYZ>>();
            public double MinX, MinY, MaxX, MaxY;
            public double? TopZ;
            /// <summary>2026-09-26 第四輪：房間垂直範圍 [底, 頂]（底＝Level＋底部偏移；頂＝底＋UnboundedHeight），多層房間樓層規則用。</summary>
            public double? SpanBaseZ, SpanTopZ;
            /// <summary>2026-09-26 第五輪：眼睛 XY 落在房間內、但眼睛高於房間頂（被排除）的房間中，最高的房間頂 Z——
            /// 室外相機站在該房間上方的屋頂／露台時，射線若漏掉屋頂面，以此推定站立面。</summary>
            public double? RoofBelowZ;
            public string RoofBelowLabel;
            public string Log;
        }

        // 眼睛所在房間 FindEyeRoom：見 CameraKeyPlanService.EyeRoom.cs（2026-09-25 改為房間輪廓＋垂直範圍搜尋）。

        /// <summary>水平視線（自眼睛 XY 沿 dir）第一次碰到房間邊界線段的距離（feet）；沒碰到回傳 null。</summary>
        private static double? RayExitDistance(RoomCtx rc, XYZ eye, XYZ dir)
        {
            double best = double.PositiveInfinity;
            double Cross(double ux, double uy, double vx, double vy) => ux * vy - uy * vx;
            foreach (var seg in rc.Segs)
            {
                double ex = seg.Value.X - seg.Key.X, ey = seg.Value.Y - seg.Key.Y;
                double denom = Cross(dir.X, dir.Y, ex, ey);
                if (Math.Abs(denom) < 1e-12) continue;
                double wx = seg.Key.X - eye.X, wy = seg.Key.Y - eye.Y;
                double t = Cross(wx, wy, ex, ey) / denom;
                double u = Cross(wx, wy, dir.X, dir.Y) / denom;
                if (t > 1e-4 && u >= -1e-9 && u <= 1 + 1e-9 && t < best) best = t;
            }
            return double.IsInfinity(best) ? (double?)null : best;
        }

        /// <summary>等角 SectionBox 的 XY 範圍（模型座標，feet）＋房間範圍（平面裁剪外擴用）。</summary>
        internal sealed class IsoXY
        {
            public double MinX, MinY, MaxX, MaxY;
            public bool HasRoomUnion;
            public double RoomMinX, RoomMinY, RoomMaxX, RoomMaxY;
            /// <summary>水平視線穿過「眼睛所在房間」外接框的參數範圍（feet，自眼睛起算，可為負＝眼睛後方），
            /// 不含 buffer；剖面（沿視線模式）水平範圍用。</summary>
            public bool HasSightRange;
            public double SightMinT, SightMaxT;
            public string Log;
        }

        /// <summary>
        /// 等角 XY 規則（2026-09-25 第五輪，取代「視線取樣房間聯集」——實測取樣會穿牆進到鄰戶）：
        ///   有眼睛所在房間 → 房間輪廓外接框 +0.6m（不再與扇形框取交集）。
        ///   沒有 → 扇形框（距離夾到 8m）。
        /// </summary>
        private static IsoXY ComputeIsoXY(CameraMath.CameraGeometry geom, XYZ forwardHoriz, double leftRad, double rightRad, CameraParams cp)
        {
            var r = new IsoXY();
            RoomCtx rc = cp.Room;
            if (cp.IsOutdoor)
            {
                // 2026-09-25 室外規則：扇形（半徑 min(遠裁剪, 室外半徑)，含弧取樣）外接框 +2m；剖面視線範圍 0…半徑。
                ComputeSampledFanBox(geom.Eye, forwardHoriz, leftRad, rightRad, cp.FanRadiusFeet, CameraMath.MmToFeet(OutdoorPlanMarginMm),
                    out r.MinX, out r.MinY, out r.MaxX, out r.MaxY);
                r.HasSightRange = true; r.SightMinT = 0; r.SightMaxT = cp.FanRadiusFeet;
                r.Log = $"source=outdoorFan R={CameraMath.FeetToMm(cp.FanRadiusFeet):F0}mm +2m → X[{CameraMath.FeetToMm(r.MinX):F0},{CameraMath.FeetToMm(r.MaxX):F0}] Y[{CameraMath.FeetToMm(r.MinY):F0},{CameraMath.FeetToMm(r.MaxY):F0}]mm " +
                        $"({CameraMath.FeetToMm(r.MaxX - r.MinX):F0}×{CameraMath.FeetToMm(r.MaxY - r.MinY):F0}mm)";
                return r;
            }
            try
            {
                if (rc?.Room != null && rc.MaxX > rc.MinX && rc.MaxY > rc.MinY)
                {
                    double m = CameraMath.MmToFeet(RoomBufferMm);
                    r.HasRoomUnion = true;
                    r.RoomMinX = rc.MinX - m; r.RoomMinY = rc.MinY - m; r.RoomMaxX = rc.MaxX + m; r.RoomMaxY = rc.MaxY + m;
                    r.MinX = r.RoomMinX; r.MinY = r.RoomMinY; r.MaxX = r.RoomMaxX; r.MaxY = r.RoomMaxY;
                    if (TryRayBoxT(geom.Eye, cp.CenterHoriz ?? forwardHoriz, rc.MinX, rc.MinY, rc.MaxX, rc.MaxY, out double t0, out double t1))
                    {
                        r.HasSightRange = true; r.SightMinT = t0; r.SightMaxT = t1;
                    }
                    r.Log = $"source=eyeRoom id={rc.Room.Id.GetIdValue()}「{SafeRoomLabel(rc.Room)}」 +0.6m → X[{CameraMath.FeetToMm(r.MinX):F0},{CameraMath.FeetToMm(r.MaxX):F0}] Y[{CameraMath.FeetToMm(r.MinY):F0},{CameraMath.FeetToMm(r.MaxY):F0}]mm" +
                            (r.HasSightRange ? $" sight[{CameraMath.FeetToMm(r.SightMinT):F0},{CameraMath.FeetToMm(r.SightMaxT):F0}]mm" : " sight=none");
                    return r;
                }
            }
            catch (Exception ex)
            {
                Logger.Error("相機 Key Plan：等角房間範圍計算失敗，退回扇形框（8m）", ex);
            }

            ComputeFanXY(geom, forwardHoriz, leftRad, rightRad, cp.FarClipFeet, out r.MinX, out r.MinY, out r.MaxX, out r.MaxY, NoRoomFanCapMm);
            r.HasSightRange = true; r.SightMinT = 0; r.SightMaxT = cp.FanRadiusFeet;
            r.Log = $"source=fanFallback(no eye room, cap {NoRoomFanCapMm / 1000:F0}m) X[{CameraMath.FeetToMm(r.MinX):F0},{CameraMath.FeetToMm(r.MaxX):F0}] Y[{CameraMath.FeetToMm(r.MinY):F0},{CameraMath.FeetToMm(r.MaxY):F0}]mm";
            return r;
        }

        /// <summary>水平射線（origin + t·dir）與 XY 軸對齊矩形的交段 [t0,t1]（slab 法，t 可為負）。</summary>
        private static bool TryRayBoxT(XYZ origin, XYZ dir, double x0, double y0, double x1, double y1, out double t0, out double t1)
        {
            t0 = double.NegativeInfinity; t1 = double.PositiveInfinity;
            double[] o = { origin.X, origin.Y }, d = { dir.X, dir.Y }, lo = { x0, y0 }, hi = { x1, y1 };
            for (int i = 0; i < 2; i++)
            {
                if (Math.Abs(d[i]) < 1e-9)
                {
                    if (o[i] < lo[i] || o[i] > hi[i]) return false;
                    continue;
                }
                double a = (lo[i] - o[i]) / d[i], b = (hi[i] - o[i]) / d[i];
                if (a > b) { double tmp = a; a = b; b = tmp; }
                t0 = Math.Max(t0, a); t1 = Math.Min(t1, b);
            }
            return t1 > t0;
        }

        private static string SafeRoomLabel(Element room)
        {
            try
            {
                string num = room.get_Parameter(BuiltInParameter.ROOM_NUMBER)?.AsString();
                string name = room.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString();
                return ((num ?? "") + " " + (name ?? "")).Trim();
            }
            catch { return "?"; }
        }

        /// <summary>本層樓板底：XY 與範圍重疊、BoundingBox 頂面在樓層高程 ±tol 內的 Floors 中，最低的 BoundingBox Z。</summary>
        private static bool TryFindFloorBottomAtLevel(Document doc, double x0, double y0, double x1, double y1, double levelZ, double tol,
            out double bottomZ, out int count, out string ids)
        {
            bottomZ = double.PositiveInfinity;
            count = 0;
            var idList = new List<string>();
            try
            {
                var floors = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Floors).WhereElementIsNotElementType();
                foreach (Element f in floors)
                {
                    if (!SectionBoxLevelHelper.TryGetWorldBounds(f, out double a0, out double b0, out double z0, out double a1, out double b1, out double z1)) continue;
                    bool overlaps = a1 > x0 && a0 < x1 && b1 > y0 && b0 < y1;
                    if (!overlaps || Math.Abs(z1 - levelZ) > tol) continue;
                    count++;
                    if (idList.Count < 6) idList.Add(f.Id.GetIdValue().ToString());
                    if (z0 < bottomZ) bottomZ = z0;
                }
            }
            catch (Exception ex)
            {
                Logger.Error("相機 Key Plan：找本層樓板失敗", ex);
                count = 0;
            }
            ids = string.Join(",", idList) + (count > idList.Count ? ",…" : "");
            return count > 0 && !double.IsInfinity(bottomZ);
        }

        /// <summary>
        /// 規格 §4.4（2026-09-24 第四輪）：透視圖裁剪框（FOV）或相機位置/方向改變後，只重畫這些相機在
        /// Key Plan 平面／剖面上的標記（含水平／垂直視野扇形），單一交易「更新相機標記」。不動任何
        /// 裁剪框、剖面範圍、等角 SectionBox，也不並排——剖面自己的裁剪若已包不住扇形就維持原樣
        /// （扇形半徑本來就會截到剖面裁剪邊）。呼叫端須在 API context（CameraManagerEventQueue）。
        /// 回傳重畫的相機數；沒有 Key Plan 的相機直接略過。
        /// </summary>
        internal static int UpdateMarkersForCameras(Document doc, ICollection<ElementId> cameraIds)
            => UpdateMarkersForCameras(doc, cameraIds, false, out _);

        /// <summary>
        /// 2026-09-26 第四輪（D-041，使用者：A 標記模式每次變動都重畫全部相機、每台約 3 秒、當機）：增量版。
        ///   · 姿態雜湊（眼睛／方向／裁剪框／遠裁剪＋模型 epoch＋標記型式）與上次畫標記時相同的相機整台略過（force＝true 除外）。
        ///   · 只掃帶 ES 的視圖（ExtensibleStorageFilter）找 Key Plan 平面／剖面。
        ///   · 簡化樓層記號只在相機樓層變了（或沒有紀錄、或 force）才重畫；「只留本相機剖面／隱藏其他 viewer」只在 force 時重做。
        ///   · 樓層總覽只清「舊樓層＋新樓層」兩張；A 標記只同步變了的相機（輕量：不重驗扇形樣式、沒有新放置就不重跑透視圖隱藏）。
        /// force＝切換相機標記型式等需要全部重畫的情況。summary＝略過／重畫數量（狀態列、log）。
        /// </summary>
        internal static int UpdateMarkersForCameras(Document doc, ICollection<ElementId> cameraIds, bool force, out string summary)
            => UpdateMarkersForCameras(doc, cameraIds, force, MarkerParts.All, out summary);

        /// <summary>2026-09-26 第六輪（編輯後續更新分級）：只重畫指定的標記群組。高度／俯仰角變動只影響剖面上的標記
        /// （垂直扇形）——平面扇形、平面裁剪、等角框、樓層總覽都不必動。出圖用透視複本一律同步（它就是相機的複本）。</summary>
        [Flags]
        internal enum MarkerParts
        {
            None = 0,
            Plan = 1,
            Section = 2,
            Overview = 4,
            AMarker = 8,
            All = Plan | Section | Overview | AMarker
        }

        internal static int UpdateMarkersForCameras(Document doc, ICollection<ElementId> cameraIds, bool force, MarkerParts parts, out string summary)
        {
            summary = null;
            if (doc == null || cameraIds == null || cameraIds.Count == 0) return 0;
            if (Schema.Lookup(SchemaGuid) == null) return 0; // 從未建立過 Key Plan

            using (CameraPerf perf = CameraPerf.Begin($"更新相機標記（要求 {cameraIds.Count} 台{(force ? "，強制" : "")}{(parts != MarkerParts.All ? "，只畫 " + parts : "")}）"))
            {
                var targets = new Dictionary<string, View3D>(StringComparer.Ordinal);
                int skipped = 0;
                foreach (ElementId id in cameraIds)
                {
                    if (!(doc.GetElement(id) is View3D v) || v.IsTemplate || !v.IsPerspective || IsSheetPerspectiveCopy(v)) continue;
                    if (!force && _lastDrawn.TryGetValue(PoseKey(doc, v.UniqueId), out PoseRecord rec) && rec.Hash == MarkerHash(v)) { skipped++; continue; }
                    targets[v.UniqueId] = v;
                }
                perf.Lap("比對姿態");
                if (targets.Count == 0)
                {
                    summary = skipped > 0 ? $"相機標記：{skipped} 台姿態未變，略過" : null;
                    perf.Note(summary ?? "沒有相機");
                    return 0;
                }

                var plans = new Dictionary<string, ViewPlan>(StringComparer.Ordinal);
                var sections = new Dictionary<string, ViewSection>(StringComparer.Ordinal);
                var sheetCopies = new Dictionary<string, View3D>(StringComparer.Ordinal); // 2026-09-25：出圖用透視複本
                bool anyOverview = false;
                foreach (View v in new FilteredElementCollector(doc).OfClass(typeof(View)).WherePasses(new ExtensibleStorageFilter(SchemaGuid)).Cast<View>())
                {
                    if (v.IsTemplate) continue;
                    if (!TryReadLink(v, out string cid, out string role) || cid == null) continue;
                    if (role == OverviewRole) { anyOverview = true; continue; }
                    if (!targets.ContainsKey(cid)) continue;
                    if (role == "Plan" && v is ViewPlan vp) plans[cid] = vp;
                    else if (role == "Section" && v is ViewSection vs) sections[cid] = vs;
                    else if (role == SheetPerspectiveRole && v is View3D pc) sheetCopies[cid] = pc;
                }
                // 2026-09-25 Wave 2：有樓層總覽或 A 型標記時，沒有 Key Plan 的相機也要重畫（總覽標記／A 標記）。
                bool syncA = (parts & MarkerParts.AMarker) != 0 && MarkerModeIncludesA() && FindAMarkerSymbol(doc) != null;
                if ((parts & MarkerParts.Overview) == 0) anyOverview = false;
                List<string> work = targets.Keys.Where(k => plans.ContainsKey(k) || sections.ContainsKey(k) || sheetCopies.ContainsKey(k) || anyOverview || syncA).ToList();
                perf.Lap("找 Key Plan 視圖");
                if (work.Count == 0) { perf.Note("沒有 Key Plan／總覽／A 標記"); return 0; }

                List<Level> levels = CameraMath.GetStoreyLevelsSorted(doc);
                var overviewCams = new List<(View3D cam, CameraParams cp)>();
                var prevStorey = new Dictionary<string, long?>(StringComparer.Ordinal);
                int done = 0, levelMarkRedraw = 0;
                var drawFailed = new HashSet<string>(StringComparer.Ordinal); // 2026-09-26 稽核：畫失敗的相機不記「已畫」
                bool sharedDrawFailed = false;
                IsUpdatingMarkers = true;
                try
                {
                    using (Transaction t = TransactionHelper.Begin(doc, MarkerUpdateTransactionName))
                    {
                        t.Start();
                        try
                        {
                            foreach (string cid in work)
                            {
                                View3D cam = targets[cid];
                                if (sheetCopies.TryGetValue(cid, out View3D sheetCopy))
                                {
                                    string synced = null;
                                    DrawMarkerSafely(doc, "SyncSheetPerspectiveCopy", () => synced = SyncSheetPerspectiveCopy(cam, sheetCopy));
                                    Logger.Info($"相機 Key Plan：出圖用透視複本「{sheetCopy.Name}」已同步（{synced}）");
                                }
                                CameraParams cp = ComputeCameraParams(cam, levels);
                                if (cp.Geom.LevelBelow == null)
                                {
                                    Logger.Info($"相機 Key Plan：更新標記略過「{cam.Name}」（找不到下方樓層）。");
                                    continue;
                                }
                                bool hadRecord = HasLastDrawnRecord(doc, cid);
                                long? prev = LastDrawnStorey(doc, cid);
                                if (hadRecord) prevStorey[cid] = prev;
                                bool storeyChanged = !hadRecord || prev != cp.Geom.LevelBelow.Id.GetIdValue();
                                overviewCams.Add((cam, cp));
                                plans.TryGetValue(cid, out ViewPlan plan);
                                sections.TryGetValue(cid, out ViewSection sec);
                                // 2026-09-26 第五輪：標記更新交易不重建視圖；樓層不符只提示（一鍵 4 視圖時會重建 Key Plan 平面）。
                                if (plan != null && (force || storeyChanged) && IsPlanLevelMismatch(plan, cp.Geom.LevelBelow.Id))
                                    Logger.Info($"相機 Key Plan：「{cam.Name}」Key Plan 平面樓層不符（平面 {plan.GenLevel?.Name ?? "?"}、相機 {cp.Geom.LevelBelow.Name}）→ 請重跑一鍵 4 視圖重建");
                                if (plan != null && (parts & MarkerParts.Plan) != 0)
                                {
                                    // 2026-09-26 第七輪：既有剖面的實際軸向 → 平面上紅色視線中心線與剖面夾角 < 3° 就不重畫。
                                    XYZ sectionAxisXY = sec != null ? GetSectionAxisWorldXY(sec) : null;
                                    if (!DrawMarkerSafely(doc, "UpdatePlanMarker",
                                        () => DrawPlanMarker(doc, plan, cp.Geom, cp.ForwardHoriz, cp.LeftRad, cp.RightRad, cp.FanRadiusFeet, cid, sectionAxisXY: sectionAxisXY)))
                                        drawFailed.Add(cid);
                                }
                                if (sec != null && (parts & MarkerParts.Section) != 0)
                                {
                                    if (!DrawMarkerSafely(doc, "UpdateSectionMarker",
                                        () => DrawSectionMarker(doc, sec, cp.Geom, cp.CenterHoriz ?? cp.ForwardHoriz, cp.FanRadiusFeet, cp.DownRad, cp.UpRad, cid)))
                                        drawFailed.Add(cid);
                                    // 2026-09-25（§5.1）：淡化樣式下，簡化樓層記號跟著相機目前樓層重畫（2026-09-26：只在樓層變了才重畫）。
                                    if (IsKeyPlanFadedStyle() && (force || storeyChanged))
                                    {
                                        DrawMarkerSafely(doc, "UpdateSectionLevelMarks",
                                            () => DrawSectionLevelMarks(doc, sec, ComputeSectionKeepLevels(doc, cp.Geom.LevelBelow, cp.IsOutdoor, sec), cid));
                                        levelMarkRedraw++;
                                    }
                                }
                                if (force)
                                {
                                    if (plan != null && sec != null)
                                        DrawMarkerSafely(doc, "UpdateHideOtherSections", () => HideOtherSectionMarkers(doc, plan, sec));
                                    if (sec != null)
                                        DrawMarkerSafely(doc, "UpdateHideForeignViewersSection", () => HideForeignViewers(doc, sec, null, "剖面"));
                                }
                                done++;
                                Logger.Info($"相機 Key Plan：更新標記「{cam.Name}」hFOV={(cp.FovDeg.HasValue ? cp.FovDeg.Value.ToString("F1") : "?")}° " +
                                    $"vFOV={(cp.VFovDeg.HasValue ? cp.VFovDeg.Value.ToString("F1") : "?")}° edges[{cp.EdgesLog}] farClip={CameraMath.FeetToMm(cp.FarClipFeet):F0}mm " +
                                    $"fanR={CameraMath.FeetToMm(cp.FanRadiusFeet):F0}mm ({cp.FanLog}) room={(cp.Room?.Room != null ? cp.Room.Room.Id.GetIdValue().ToString() : "none")} " +
                                    $"storey={cp.Geom.LevelBelow.Name}{(storeyChanged ? "（變更／首次）" : "")} " +
                                    $"plan={(plan != null ? plan.Id.GetIdValue().ToString() : "-")} section={(sec != null ? sec.Id.GetIdValue().ToString() : "-")} | FOV[{cp.FovLog}]");
                            }
                            perf.Lap("Key Plan 標記");
                            if (anyOverview)
                            {
                                int ov = 0;
                                if (!DrawMarkerSafely(doc, "UpdateOverviewMarkers", () => ov = RedrawCamerasInOverviews(doc, overviewCams, force ? null : prevStorey)))
                                    sharedDrawFailed = true;
                                if (ov > 0) Logger.Info($"相機 Key Plan：更新樓層總覽上 {ov} 台相機的標記");
                            }
                            perf.Lap("總覽標記");
                            if (syncA)
                            {
                                string aLog = null;
                                if (!DrawMarkerSafely(doc, "UpdateAMarkers", () => aLog = SyncAMarkersInTransaction(doc, overviewCams, placeMissing: true, light: !force)))
                                    sharedDrawFailed = true;
                                if (aLog != null) Logger.Info("相機 Key Plan：更新 A 型標記 " + aLog);
                            }
                            perf.Lap("A 標記");
                            TransactionHelper.CommitOrThrow(t);
                            BumpOwnViewsEpoch();
                            perf.Lap("Commit");
                        }
                        catch
                        {
                            try { if (t.HasStarted() && !t.HasEnded()) t.RollBack(); } catch { }
                            throw;
                        }
                    }
                    // 2026-09-26 稽核：只記錄真的畫成功的相機；總覽／A 標記整批失敗 → 全部不記（下次整批重試）。
                    if (!sharedDrawFailed)
                        foreach (var (cam, cp) in overviewCams)
                            if (!drawFailed.Contains(cam.UniqueId)) RecordDrawn(doc, cam, cp);
                    if (drawFailed.Count > 0 || sharedDrawFailed)
                        Logger.Info($"相機 Key Plan：更新標記有失敗（個別 {drawFailed.Count} 台{(sharedDrawFailed ? "、總覽／A 標記整批" : "")}）→ 不記「已畫」，下次變動時重試。");
                }
                finally
                {
                    IsUpdatingMarkers = false;
                }
                summary = $"相機標記：重畫 {done} 台" + (skipped > 0 ? $"、姿態未變略過 {skipped} 台" : "") + (levelMarkRedraw > 0 ? $"、樓層記號 {levelMarkRedraw} 台" : "");
                perf.Note(summary);
                return done;
            }
        }

        /// <summary>
        /// 2026-09-25 第五輪（使用者：等角要「從相機起點的方向看」，舊版固定 TOP-BACK-LEFT 常從反方向看）：
        /// 前方向＝相機水平前方向（yaw）往下俯 35.26°（標準等角俯角）；眼睛放在 SectionBox 中心沿 −forward
        /// 退到 box 外（至少 5m 或對角線長）＝相機後上方；up＝與 forward 垂直、不滾動（BuildLevelUp）。
        /// 平行投影下眼睛精確位置不影響畫面，只要在 box 外即可。
        /// </summary>
        private static ViewOrientation3D BuildIsoOrientation(BoundingBoxXYZ box, XYZ forwardHoriz)
        {
            XYZ fh = forwardHoriz != null ? new XYZ(forwardHoriz.X, forwardHoriz.Y, 0) : XYZ.BasisY;
            fh = fh.GetLength() > 1e-6 ? fh.Normalize() : XYZ.BasisY;
            // 2026-09-25 第六輪（使用者：等角必須是真正的標準等角）：從 ViewCube 上方四角
            // （水平 NE/NW/SE/SW＝yaw 45°/135°/225°/315°，專案座標 X/Y 軸）挑水平方向與相機水平前方向
            // 最接近者，forward＝(±1, ±1, −1)/√3（俯角 atan(1/√2)=35.264°），不再用相機精確 yaw。
            XYZ forward = CameraMath.SnapToIsoCorner(fh, out double cornerYaw);
            XYZ center = (box.Min + box.Max) / 2.0;
            double diagonal = (box.Max - box.Min).GetLength();
            double backoff = Math.Max(diagonal, Cm(IsoMinBackoffCm));
            XYZ eye = center - forward * backoff;
            XYZ up = CameraMath.BuildLevelUp(forward);
            Logger.Info($"相機 Key Plan：等角方向 相機yaw={CameraMath.ComputeHorizontalAngleDeg(fh):F1}° → ViewCube 角 yaw={cornerYaw:F0}° 俯角=35.264° forward=({forward.X:F4},{forward.Y:F4},{forward.Z:F4})");
            return new ViewOrientation3D(eye, up, forward);
        }

        // ══════════════════════════════════════════════════════════
        // B 型標記（規格 §5.3，第一版：只有平面／剖面，3D 不畫）
        // ══════════════════════════════════════════════════════════

        // 2026-09-24：標記改版——方向指示不再是「開口線段箭頭」，改成「圓圈（眼睛位置）＋
        // 方向桿＋實心三角形箭頭」，且大小改成「列印比例固定」（依 view.Scale 換算，印出來
        // 恆定約幾 mm），不再是模型空間固定的 cm 常數——這樣不同比例的 Key Plan 視圖，標記
        // 印在紙上看起來都一樣大。視野扇形維持原有的物理範圍（跟著 farClipFeet／halfFovRad），
        // 不受這次改版影響。
        private const double MarkerCirclePrintRadiusMm = 1.0;
        // 2026-09-25（使用者回報：看大範圍時三角形太小）：箭頭總長（眼睛→箭尖）改成
        //   arrowLen = max(8mm 紙上長度 × view.Scale, 15% × 畫出來的扇形半徑)
        // 頭寬 ≈ 0.6 × arrowLen（半寬 0.3），頭長沿用舊比例 1.4/3 ≈ 0.467 × arrowLen；桿仍是線。
        // 這是模型/詳圖幾何，使用者縮放時自然跟著縮放。
        private const double MarkerArrowMinPrintLenMm = 8.0;
        private const double MarkerArrowFanRatio = 0.15;
        private const double MarkerArrowHeadLenRatio = 1.4 / 3.0;
        private const double MarkerArrowHeadHalfWidthRatio = 0.3;
        /// <summary>箭頭 FilledRegion 在該視圖的「依元素取代」表面透明度（0–100）。</summary>
        private const int MarkerArrowTransparency = 30; // 2026-09-25：眼睛楔形 30%（比扇形 90% 深）

        /// <summary>箭頭總長（feet）：max(8mm 紙上 × 比例, 15% × 扇形半徑)。</summary>
        private static double ComputeArrowLenFeet(View view, double fanRadiusFeet)
        {
            double byPaper = PrintSizeFeet(view, MarkerArrowMinPrintLenMm);
            double byFan = (fanRadiusFeet > 0 && !double.IsInfinity(fanRadiusFeet) && !double.IsNaN(fanRadiusFeet))
                ? fanRadiusFeet * MarkerArrowFanRatio : 0.0;
            return Math.Max(byPaper, byFan);
        }

        /// <summary>
        /// 箭頭半透明：在箭頭所屬視圖對這個 FilledRegion 設「依元素取代」——表面前景＝實心填滿、
        /// 顏色＝標記色、表面透明度 50。依據：Autodesk 社群已解答帖「How to change transparency of a
        /// filled region」＝ Override Graphics in View › By Element › Surface Transparency 對填充區域有效；
        /// API 端為 OverrideGraphicSettings.SetSurfaceTransparency（未在本機 Revit 實機驗證）。
        /// 失敗退回 Halftone＋淡化色，並記 log。回傳採用的方式字串供 log。
        /// </summary>
        private static string ApplyArrowOverride(Document doc, View view, ElementId regionId, Color color, int transparency = MarkerArrowTransparency)
        {
            if (doc == null || view == null || regionId == null || regionId == ElementId.InvalidElementId) return "skip";
            try
            {
                var ogs = new OverrideGraphicSettings();
                ElementId solidId = FindSolidFillPatternId(doc);
                if (solidId != ElementId.InvalidElementId)
                {
                    ogs.SetSurfaceForegroundPatternId(solidId);
                    ogs.SetSurfaceForegroundPatternVisible(true);
                }
                ogs.SetSurfaceForegroundPatternColor(color);
                ogs.SetSurfaceTransparency(transparency);
                view.SetElementOverrides(regionId, ogs);
                return "transparency" + transparency;
            }
            catch (Exception ex)
            {
                Logger.Error("相機 Key Plan：箭頭表面透明度取代失敗，退回 Halftone＋淡化色", ex);
                try
                {
                    var ogs = new OverrideGraphicSettings();
                    ogs.SetHalftone(true);
                    ogs.SetSurfaceForegroundPatternColor(LightenColor(color, 0.5));
                    view.SetElementOverrides(regionId, ogs);
                    return "halftone-fallback";
                }
                catch (Exception ex2)
                {
                    Logger.Error("相機 Key Plan：箭頭 Halftone 退回也失敗，箭頭維持不透明", ex2);
                    return "none";
                }
            }
        }

        private static Color LightenColor(Color c, double t)
        {
            byte L(byte v) => (byte)Math.Round(v + (255 - v) * t);
            return new Color(L(c.Red), L(c.Green), L(c.Blue));
        }

        private static ElementId FindSolidFillPatternId(Document doc)
        {
            List<FillPatternElement> all = new FilteredElementCollector(doc).OfClass(typeof(FillPatternElement))
                .Cast<FillPatternElement>().ToList();
            FillPatternElement p =
                all.FirstOrDefault(x => x.GetFillPattern()?.IsSolidFill == true && x.GetFillPattern().Target == FillPatternTarget.Drafting)
                ?? all.FirstOrDefault(x => x.GetFillPattern()?.IsSolidFill == true);
            return p?.Id ?? ElementId.InvalidElementId;
        }

        /// <summary>「④ 標記顏色」變更時，把所有既有相機箭頭（Role＝MarkerPlan／MarkerSection 的
        /// FilledRegion）在其所屬視圖的取代顏色一併更新。回傳更新數。須在交易內呼叫。</summary>
        private static int RefreshArrowOverrides(Document doc, Color color)
        {
            int n = 0;
            foreach (FilledRegion fr in new FilteredElementCollector(doc).OfClass(typeof(FilledRegion)).Cast<FilledRegion>())
            {
                try
                {
                    if (!TryReadLink(fr, out string _, out string role)) continue;
                    if (role != "MarkerPlan" && role != "MarkerSection") continue;
                    if (!(doc.GetElement(fr.OwnerViewId) is View v)) continue;
                    // 預設楔形 30%、扇形 MarkerFanTransparency（90%）；沿用該元素目前的透明度（使用者可能調過），只更新顏色。
                    int tr = MarkerArrowTransparency;
                    try { int cur = v.GetElementOverrides(fr.Id).Transparency; if (cur > 0 && cur < 100) tr = cur; } catch { }
                    ApplyArrowOverride(doc, v, fr.Id, color, tr);
                    n++;
                }
                catch (Exception ex) { Logger.Error("相機 Key Plan：更新既有箭頭取代顏色失敗（略過該箭頭）", ex); }
            }
            return n;
        }

        /// <summary>對應 UI 面板的 accent 色 (#3B82C4)，讓 Revit 標記與面板配色一致。</summary>
        private static readonly Color MarkerFillColor = new Color(59, 130, 196);

        /// <summary>把「列印後想要的毫米」換算成這個 view 的模型空間長度（feet）：
        /// modelLength = printLength_mm * view.Scale，再轉 feet。找不到有效比例（&lt;=0，
        /// 例如某些特殊視圖）時退回 1:100，不讓標記大小算出負值或除以零。</summary>
        private static double PrintSizeFeet(View view, double printMm)
        {
            int scale = view.Scale > 0 ? view.Scale : 100;
            return CameraMath.MmToFeet(printMm * scale);
        }

        /// <summary>用兩個半圓弧拼出一個完整圓圈（Arc.Create 對「剛好一整圈」的角度是退化情形，
        /// 兩個 180° 半圓弧是常見且穩健的替代做法）。</summary>
        private static void AddCircle(List<Curve> curves, XYZ center, double radius, XYZ xAxis, XYZ yAxis)
        {
            curves.Add(Arc.Create(center, radius, 0, Math.PI, xAxis, yAxis));
            curves.Add(Arc.Create(center, radius, Math.PI, 2 * Math.PI, xAxis, yAxis));
        }

        /// <summary>
        /// 找（或建立）一個「前景填滿樣式＝實心」的 <see cref="FilledRegionType"/>，供箭頭三角形
        /// 使用。優先重用文件內已有的實心類型，不要每次都新建一個。找不到任何實心
        /// <see cref="FillPattern"/>（極少數情形，例如樣板被清得很乾淨）或文件內連一個
        /// FilledRegionType 都沒有時丟例外，呼叫端會 catch 並退回細線畫法，不讓整個標記失敗。
        /// ⚠️ 未經 Revit 實機驗證：FilledRegionType.Duplicate/ForegroundPatternId 賦值的行為
        /// 依 API 文件推斷，尚未在真實 .rvt 開檔測試。
        /// </summary>
        private const string MarkerRegionTypeName = "相機標記-實心";
        private const string MarkerLineStyleName = "相機標記線";
        private const int MarkerLineWeight = 5;
        /// <summary>視野扇形半透明填滿的表面透明度（0–100）。平面與剖面共用這一個值，要調淡／調深只改這裡。
        /// 2026-09-25：70 → 90（使用者回報扇形蓋住其他圖面；詳圖元件在 Revit 一律畫在模型之上，只能調淡＋排到詳圖最底層）。</summary>
        private const int MarkerFanTransparency = 90;

        // 2026-09-25（§5.3 繪製順序）：一次標記重畫期間建立的元素，畫完後扇形填滿送到詳圖最底層、
        // 楔形與標記線拉到最上層（DetailElementOrderUtils）。只在交易內、單執行緒使用。
        private static readonly List<ElementId> _orderFanIds = new List<ElementId>();
        private static readonly List<ElementId> _orderFrontIds = new List<ElementId>();

        private static void BeginMarkerDrawOrder() { _orderFanIds.Clear(); _orderFrontIds.Clear(); }

        /// <summary>扇形填滿 SendToBack、眼睛楔形＋標記線 BringToFront；任何失敗只記 log。</summary>
        private static void ApplyMarkerDrawOrder(Document doc, View view, string role)
        {
            try
            {
                if (_orderFanIds.Count > 0)
                    DetailElementOrderUtils.SendToBack(doc, view, new List<ElementId>(_orderFanIds));
                if (_orderFrontIds.Count > 0)
                    DetailElementOrderUtils.BringToFront(doc, view, new List<ElementId>(_orderFrontIds));
                Logger.Info($"相機 Key Plan：{role} 繪製順序 扇形填滿 {_orderFanIds.Count} 個送到最底、楔形／標記線 {_orderFrontIds.Count} 個拉到最上");
            }
            catch (Exception ex)
            {
                Logger.Error($"相機 Key Plan：{role} 調整詳圖繪製順序失敗（標記照常存在）", ex);
            }
            finally { _orderFanIds.Clear(); _orderFrontIds.Clear(); }
        }

        /// <summary>設定裡的標記顏色（#RRGGBB）；解析失敗退回預設藍 #3B82C4。</summary>
        internal static Color GetConfiguredMarkerColor()
        {
            try
            {
                string hex = ConfigManager.Instance.Settings.CameraManager.MarkerColorHex;
                if (TryParseHexColor(hex, out byte r, out byte g, out byte b)) return new Color(r, g, b);
            }
            catch { }
            return new Color(59, 130, 196);
        }

        internal static bool TryParseHexColor(string hex, out byte r, out byte g, out byte b)
        {
            r = g = b = 0;
            if (string.IsNullOrWhiteSpace(hex)) return false;
            string h = hex.Trim().TrimStart('#');
            if (h.Length != 6) return false;
            try
            {
                r = Convert.ToByte(h.Substring(0, 2), 16);
                g = Convert.ToByte(h.Substring(2, 2), 16);
                b = Convert.ToByte(h.Substring(4, 2), 16);
                return true;
            }
            catch { return false; }
        }

        /// <summary>
        /// 2026-09-24：相機標記線的線型＝OST_Lines 的子類別「相機標記線」（投影線寬 3、實線、
        /// 顏色取設定值），不存在才建立一次；已存在就原樣沿用（使用者可能在「物件型式」改過顏色，
        /// 不覆寫）。回傳其投影 GraphicsStyle；任何失敗回傳 null（呼叫端就用預設線型，不中斷）。
        /// 必須在交易內呼叫（建立子類別會修改文件）。
        /// </summary>
        private static GraphicsStyle GetOrCreateMarkerLineStyle(Document doc)
        {
            try
            {
                Category lines = Category.GetCategory(doc, BuiltInCategory.OST_Lines);
                if (lines == null) return null;
                Category sub = null;
                foreach (Category c in lines.SubCategories)
                {
                    if (c.Name == MarkerLineStyleName) { sub = c; break; }
                }
                if (sub == null)
                {
                    sub = doc.Settings.Categories.NewSubcategory(lines, MarkerLineStyleName);
                    sub.LineColor = GetConfiguredMarkerColor();
                    try { sub.SetLinePatternId(LinePatternElement.GetSolidPatternId(), GraphicsStyleType.Projection); } catch { }
                }
                // 2026-09-25：投影線寬 3→5（Key Plan 表現法：標記加粗），既有線型也一併改。
                try
                {
                    int? w = sub.GetLineWeight(GraphicsStyleType.Projection);
                    if (w != MarkerLineWeight)
                    {
                        sub.SetLineWeight(MarkerLineWeight, GraphicsStyleType.Projection);
                        Logger.Info($"相機 Key Plan：線型「{MarkerLineStyleName}」投影線寬 {w}→{MarkerLineWeight}");
                    }
                }
                catch (Exception ex) { Logger.Error("相機 Key Plan：設定相機標記線線寬失敗", ex); }
                return sub.GetGraphicsStyle(GraphicsStyleType.Projection);
            }
            catch (Exception ex)
            {
                Logger.Error("相機 Key Plan：建立/取得線型「相機標記線」失敗，標記改用預設線型", ex);
                return null;
            }
        }

        /// <summary>
        /// 「④ 標記顏色」：在單一交易「變更相機標記顏色」內，把線型「相機標記線」的 LineColor 與
        /// FilledRegionType「相機標記-實心」的前景顏色一起改掉（兩者不存在就先建立），既有標記因為
        /// 共用同一個線型/類型，全部一次變色。回傳 null＝成功，否則錯誤訊息。呼叫端須在 ExternalEvent 內。
        /// </summary>
        internal static string ApplyMarkerColor(Document doc, byte r, byte g, byte b)
        {
            if (doc == null) return "目前沒有開啟中的文件。";
            using (Transaction t = TransactionHelper.Begin(doc, MarkerColorTransactionName))
            {
                try
                {
                    t.Start();
                    Color color = new Color(r, g, b);
                    GraphicsStyle gs = GetOrCreateMarkerLineStyle(doc);
                    if (gs?.GraphicsStyleCategory != null) gs.GraphicsStyleCategory.LineColor = color;
                    try
                    {
                        ElementId typeId = GetOrCreateSolidFilledRegionType(doc);
                        if (doc.GetElement(typeId) is FilledRegionType frt && frt.Name == MarkerRegionTypeName)
                            frt.ForegroundPatternColor = color;
                    }
                    catch (Exception ex) { Logger.Error("相機 Key Plan：變更箭頭填滿顏色失敗（線色仍會更新）", ex); }
                    try
                    {
                        int n = RefreshArrowOverrides(doc, color);
                        Logger.Info($"相機 Key Plan：標記顏色變更，已更新 {n} 個既有箭頭的視圖取代顏色");
                    }
                    catch (Exception ex) { Logger.Error("相機 Key Plan：更新既有箭頭取代顏色失敗（線色仍會更新）", ex); }
                    TransactionHelper.CommitOrThrow(t);
                    return null;
                }
                catch (Exception ex)
                {
                    try { if (t.HasStarted() && !t.HasEnded()) t.RollBack(); } catch { }
                    Logger.Error("相機 Key Plan：變更相機標記顏色失敗", ex);
                    return "變更標記顏色失敗：" + ex.Message;
                }
            }
        }

        /// <summary>標記繪製一律包在 SubTransaction＋try/catch：任何失敗只回滾標記本身並記 log，
        /// 絕不讓一鍵 4 視圖整筆交易回滾（視圖照樣建立）。</summary>
        /// <returns>2026-09-26 稽核：true＝已畫；false＝失敗已回滾（呼叫端據此不記「已畫」，下次不會被姿態雜湊略過）。</returns>
        private static bool DrawMarkerSafely(Document doc, string what, Action draw)
        {
            SubTransaction st = null;
            try
            {
                st = new SubTransaction(doc);
                st.Start();
                draw();
                st.Commit();
                return true;
            }
            catch (Exception ex)
            {
                try { if (st != null && st.HasStarted() && !st.HasEnded()) st.RollBack(); } catch { }
                Logger.Error($"相機 Key Plan：{what} 失敗，略過標記（視圖照常建立）", ex);
                CameraManagerDiagnostics.RecordCaught(what, ex);
                return false;
            }
            finally
            {
                st?.Dispose();
            }
        }

        private static void CreateMarkerCurves(Document doc, View view, IEnumerable<Curve> curves, string cid, string role, GraphicsStyle style)
        {
            foreach (Curve c in curves)
            {
                if (c == null || c.Length < 1e-6) continue;
                DetailCurve dc = doc.Create.NewDetailCurve(view, c);
                if (style != null)
                {
                    try { dc.LineStyle = style; } catch { }
                }
                TagLink(dc, cid, role);
                _orderFrontIds.Add(dc.Id);
            }
        }

        private static ElementId GetOrCreateSolidFilledRegionType(Document doc)
        {
            // 2026-09-24：優先用專屬名稱「相機標記-實心」（顏色可由「④ 標記顏色」統一改），沒有才建立；
            // 建立失敗才退回任何既有實心類型。
            FilledRegionType named = new FilteredElementCollector(doc).OfClass(typeof(FilledRegionType))
                .Cast<FilledRegionType>().FirstOrDefault(t => t.Name == MarkerRegionTypeName);
            if (named != null) return named.Id;
            try
            {
                return CreateMarkerRegionType(doc);
            }
            catch (Exception ex)
            {
                Logger.Error("相機 Key Plan：建立「相機標記-實心」失敗，改用既有實心填滿類型", ex);
            }
            FilledRegionType existing = new FilteredElementCollector(doc).OfClass(typeof(FilledRegionType))
                .Cast<FilledRegionType>().FirstOrDefault(t => IsSolidForeground(doc, t));
            if (existing != null) return existing.Id;
            throw new Exception("找不到任何實心填滿區域類型。");
        }

        private static ElementId CreateMarkerRegionType(Document doc)
        {

            // FilledRegion 的前景樣式要用 Drafting 目標的填滿樣式（Model 目標套用在 FilledRegionType
            // 上不保證有效）；優先找 Drafting＋實心，退一步才接受任何 IsSolidFill（不同 Revit
            // 版本/樣板內建樣式命名不一定一致，這裡用旗標比對而非名稱字串，較穩健）。
            List<FillPatternElement> allPatterns = new FilteredElementCollector(doc).OfClass(typeof(FillPatternElement))
                .Cast<FillPatternElement>().ToList();
            FillPatternElement solidPattern =
                allPatterns.FirstOrDefault(p => p.GetFillPattern()?.IsSolidFill == true && p.GetFillPattern().Target == FillPatternTarget.Drafting)
                ?? allPatterns.FirstOrDefault(p => p.GetFillPattern()?.IsSolidFill == true);
            if (solidPattern == null)
                throw new Exception("找不到任何實心填滿樣式 (FillPattern.IsSolidFill)。");

            FilledRegionType baseType = new FilteredElementCollector(doc).OfClass(typeof(FilledRegionType))
                .Cast<FilledRegionType>().FirstOrDefault();
            if (baseType == null)
                throw new Exception("文件內找不到任何 FilledRegionType 可供複製。");

            string uniqueName = MakeUniqueTypeName(doc, typeof(FilledRegionType), MarkerRegionTypeName);
            FilledRegionType created = baseType.Duplicate(uniqueName) as FilledRegionType;
            if (created == null)
                throw new Exception("複製 FilledRegionType 失敗。");

            created.ForegroundPatternId = solidPattern.Id;
            created.ForegroundPatternColor = GetConfiguredMarkerColor();
            return created.Id;
        }

        private static bool IsSolidForeground(Document doc, FilledRegionType type)
        {
            ElementId fgId = type.ForegroundPatternId;
            if (fgId == null || fgId == ElementId.InvalidElementId) return false;
            return (doc.GetElement(fgId) as FillPatternElement)?.GetFillPattern()?.IsSolidFill == true;
        }

        /// <summary>
        /// 2026-09-25（箭頭反轉）：取代舊的「朝前三角形箭頭」——在眼睛處畫一個實心小楔形（FilledRegion），
        /// 頂點＝眼睛、沿「與視野扇形相同的兩條（非對稱）邊界」向外張開，長度＝原箭頭長度規則
        /// （max(8mm 紙上, 15% 扇形半徑)）。同標記色、透明度 30%（比扇形填滿 90% 深），在扇形根部讀成較深的一塊。
        /// edgeEndA／edgeEndB＝兩條邊界在楔形長度處的端點，arcMid＝兩端點之間的弧中點（三點弧）；全部須與 eye 共面於視圖平面。
        /// </summary>
        private static void CreateEyeWedgeRegion(Document doc, View view, XYZ eye, XYZ edgeEndA, XYZ arcMid, XYZ edgeEndB,
            string cid, string role, GraphicsStyle style)
        {
            ElementId typeId = GetOrCreateSolidFilledRegionType(doc);
            CurveLoop loop = new CurveLoop();
            loop.Append(Line.CreateBound(eye, edgeEndA));
            loop.Append(Arc.Create(edgeEndA, edgeEndB, arcMid));
            loop.Append(Line.CreateBound(edgeEndB, eye));

            FilledRegion region = FilledRegion.Create(doc, typeId, view.Id, new List<CurveLoop> { loop });
            if (style != null)
            {
                try { region.SetLineStyleId(style.Id); } catch { /* 外框線型設定失敗不影響填滿 */ }
            }
            TagLink(region, cid, role);
            _orderFrontIds.Add(region.Id);
            string mode = ApplyArrowOverride(doc, view, region.Id, GetConfiguredMarkerColor(), MarkerArrowTransparency);
            Logger.Info($"相機 Key Plan：眼睛楔形 {role} 長={CameraMath.FeetToMm(eye.DistanceTo(edgeEndA)):F0}/{CameraMath.FeetToMm(eye.DistanceTo(edgeEndB)):F0}mm 取代={mode}");
        }

        /// <summary>視野扇形半透明填滿（FilledRegion，同標記色、透明度 <see cref="MarkerFanTransparency"/>，取代方式同箭頭）。</summary>
        private static void CreateFanWedgeRegion(Document doc, View view, CurveLoop loop, string cid, string role, GraphicsStyle style)
        {
            ElementId typeId = GetOrCreateSolidFilledRegionType(doc);
            FilledRegion region = FilledRegion.Create(doc, typeId, view.Id, new List<CurveLoop> { loop });
            if (style != null)
            {
                try { region.SetLineStyleId(style.Id); } catch { }
            }
            TagLink(region, cid, role);
            _orderFanIds.Add(region.Id);
            string mode = ApplyArrowOverride(doc, view, region.Id, GetConfiguredMarkerColor(), MarkerFanTransparency);
            Logger.Info($"相機 Key Plan：扇形填滿 {role} 取代={mode}");
        }

        private static void DrawPlanMarker(Document doc, ViewPlan planView, CameraMath.CameraGeometry geom,
            XYZ forwardHoriz, double leftRad, double rightRad, double farClipFeet, string cid, double? zOverride = null, bool forceB = false,
            XYZ sectionAxisXY = null)
        {
            // 2026-09-25：farClipFeet 參數現在傳入的是「扇形半徑」（視線離開房間 +0.6m，夾 [1.5m, 遠裁剪]），名稱沿用。
            // 2026-09-25（非對稱視錐）：扇形從相機左緣 leftRad 到右緣 rightRad（相對正前方帶號角，相機右為正），
            // 不再是 ±h/2；中央視線仍沿相機正前方。原本的朝前三角形箭頭改成「眼睛處的實心楔形」。
            DeleteOldMarkers(doc, planView, cid, "MarkerPlan");
            // 2026-09-25 Wave 2：⋯ 相機標記＝「A 模型」時 Key Plan 不畫 B 型詳圖標記（舊的照刪）；樓層總覽一律畫（forceB）。
            if (!forceB && !MarkerModeIncludesB())
            {
                Logger.Info("相機 Key Plan：相機標記＝A 模型，平面不畫 B 型標記（已刪除舊標記）");
                return;
            }
            BeginMarkerDrawOrder();
            GraphicsStyle style = GetOrCreateMarkerLineStyle(doc);

            double levelZ = zOverride ?? geom.LevelBelow.ProjectElevation;
            XYZ eyeFlat = new XYZ(geom.Eye.X, geom.Eye.Y, levelZ);
            // NewDetailCurve(view, curve) 不需要顯式 SketchPlane 參數，Revit 依 view 本身的
            // 平面（平面視圖＝任意水平面）自動判定；這裡只要保證每條 curve 的 Z 一致落在
            // levelZ 這個水平面即可（FilledRegion 同理）。
            XYZ side = forwardHoriz.CrossProduct(XYZ.BasisZ);
            side = side.GetLength() > 1e-6 ? side.Normalize() : XYZ.BasisX;

            double circleR = PrintSizeFeet(planView, MarkerCirclePrintRadiusMm);
            double arrowLen = Math.Min(ComputeArrowLenFeet(planView, farClipFeet), farClipFeet); // 平面扇形半徑＝farClipFeet

            List<Curve> curves = new List<Curve>();
            AddCircle(curves, eyeFlat, circleR, forwardHoriz, side);

            // 中央視線：2026-09-25 起沿「畫面中心線」（左右緣角平分線；對稱視錐時＝相機正前方），從眼睛圓圈邊到扇形半徑。
            // 2026-09-26 第七輪：剖面方向與這條中心線幾乎重疊（< 3°）時，剖面線本身已經代表視線方向 → 不重畫紅色中心線
            // （否則兩條線完全疊在一起，剖面線反而被畫在下面看不到）；扇形邊線／弧／楔形照畫，不受影響。
            XYZ centerDir = PlanEdgeDir(forwardHoriz, (leftRad + rightRad) / 2.0);
            centerDir = centerDir.GetLength() > 1e-6 ? new XYZ(centerDir.X, centerDir.Y, 0).Normalize() : forwardHoriz;
            bool skipCenterline = false;
            if (sectionAxisXY != null)
            {
                double dot = Math.Max(-1.0, Math.Min(1.0, Math.Abs(centerDir.DotProduct(sectionAxisXY))));
                double angleDeg = Math.Acos(dot) * 180.0 / Math.PI;
                if (angleDeg < SightCenterlineSkipAngleDeg)
                {
                    skipCenterline = true;
                    Logger.Info($"相機 Key Plan：剖面方向與視線中心線夾角={angleDeg:F1}°（< {SightCenterlineSkipAngleDeg:F0}°）→ 平面上不畫紅色視線中心線（由剖面線代表）");
                }
            }
            XYZ shaftStart = eyeFlat + centerDir * circleR;
            XYZ sightEnd = eyeFlat + centerDir * farClipFeet;
            if (!skipCenterline && farClipFeet > circleR && (sightEnd - shaftStart).GetLength() > 1e-6)
                curves.Add(Line.CreateBound(shaftStart, sightEnd));

            // 視野扇形：相機左緣／右緣邊界線 + 弧（物理範圍，不受「列印比例固定」影響）。
            // 弧從右緣（平面上順時針側）逆時針掃到左緣，掃角＝rightRad − leftRad＝水平總視角。
            XYZ dirLeft = PlanEdgeDir(forwardHoriz, leftRad);
            XYZ dirRight = PlanEdgeDir(forwardHoriz, rightRad);
            double sweep = rightRad - leftRad;
            XYZ arcY = XYZ.BasisZ.CrossProduct(dirRight).Normalize();
            curves.Add(Line.CreateBound(eyeFlat, eyeFlat + dirLeft * farClipFeet));
            curves.Add(Line.CreateBound(eyeFlat, eyeFlat + dirRight * farClipFeet));
            try
            {
                curves.Add(Arc.Create(eyeFlat, farClipFeet, 0, sweep, dirRight, arcY));
            }
            catch
            {
                // 視角接近/超過 180 度等退化情形：跳過弧線，保留左右邊界線即可。
            }

            // 扇形半透明填滿（先建，在邊線與楔形之下）
            try
            {
                XYZ leftEnd = eyeFlat + dirLeft * farClipFeet, rightEnd = eyeFlat + dirRight * farClipFeet;
                CurveLoop wedge = new CurveLoop();
                wedge.Append(Line.CreateBound(eyeFlat, rightEnd));
                wedge.Append(Arc.Create(eyeFlat, farClipFeet, 0, sweep, dirRight, arcY));
                wedge.Append(Line.CreateBound(leftEnd, eyeFlat));
                CreateFanWedgeRegion(doc, planView, wedge, cid, "MarkerPlan", style);
            }
            catch (Exception ex) { Logger.Error("相機 Key Plan：平面扇形填滿失敗（邊線照畫）", ex); }

            CreateMarkerCurves(doc, planView, curves, cid, "MarkerPlan", style);

            // 眼睛楔形（取代舊的朝前三角形箭頭）：頂點在眼睛、沿相同兩條邊界張開，長度＝箭頭長度規則。
            try
            {
                XYZ dirMid = PlanEdgeDir(forwardHoriz, (leftRad + rightRad) / 2.0);
                CreateEyeWedgeRegion(doc, planView, eyeFlat,
                    eyeFlat + dirRight * arrowLen, eyeFlat + dirMid * arrowLen, eyeFlat + dirLeft * arrowLen,
                    cid, "MarkerPlan", style);
            }
            catch (Exception ex)
            {
                Logger.Error("相機 Key Plan：平面眼睛楔形填滿失敗（扇形與視線照畫）", ex);
            }
            Logger.Info($"相機 Key Plan：平面扇形 leftDeg={leftRad * 180 / Math.PI:F1} rightDeg={rightRad * 180 / Math.PI:F1} " +
                        $"radius={CameraMath.FeetToMm(farClipFeet):F0}mm wedgeLen={CameraMath.FeetToMm(arrowLen):F0}mm");
            ApplyMarkerDrawOrder(doc, planView, "MarkerPlan");
        }

        /// <summary>
        /// 剖面標記：眼睛圓圈＋水平視線＋箭頭＋垂直視野扇形。2026-09-25 第五輪：
        ///   半徑＝fanRadiusFeet（與平面扇形相同：視線離開房間 +0.6m），不再截到剖面裁剪邊；
        ///   剖面可能「對齊房間長邊」而非沿視線 → 視線與扇形邊線一律「投影到剖面平面」：水平分量用
        ///   alongRaw＝forwardHoriz 去掉剖面法向分量（不正規化，所以斜交時自然縮短＝透視縮減），垂直分量不變。
        ///   視線與剖面平面夾角 &gt; 60° 記 log（扇形明顯縮短）；接近 90°（|alongRaw|&lt;0.05）只畫眼睛圓圈。
        /// 若使用者手動移過剖面，眼睛先投影到剖面目前的實際平面上，避免 NewDetailCurve 因不共面失敗。
        /// 2026-09-27（D-044 修正②）：剖面「看向」（−BasisZ）與相機水平視線夾角 &lt; <see cref="SectionFanSuppressAlongSightDeg"/>
        /// 也只畫眼睛圓圈（不管是不是剖面轉 90° 造成的，純看幾何）——這種情況等於順著視線看進剖面，垂直扇形
        /// （假設視線躺在畫面裡的側視）會誤導；平面視圖的扇形不受影響。
        /// 2026-09-28（D-046）：剖面轉 90°＋跳過眼前牆後，眼睛不再落在剖切面（剖切面往前挪了），眼睛落在裁剪框
        /// 深度範圍外側（視野中「相機背後」那一側）——投影點（畫在畫面上的圓圈位置）落在裁剪框 X/Y 範圍外時
        /// 直接略過整個標記（不畫圓圈、不畫扇形），不拋例外。
        /// </summary>
        private static void DrawSectionMarker(Document doc, ViewSection sectionView, CameraMath.CameraGeometry geom,
            XYZ forwardHoriz, double fanRadiusFeet, double downRad, double upRad, string cid)
        {
            DeleteOldMarkers(doc, sectionView, cid, "MarkerSection");
            if (!MarkerModeIncludesB())
            {
                Logger.Info("相機 Key Plan：相機標記＝A 模型，剖面不畫 B 型標記（已刪除舊標記）");
                return;
            }
            BeginMarkerDrawOrder();
            GraphicsStyle style = GetOrCreateMarkerLineStyle(doc);

            XYZ up = XYZ.BasisZ;
            XYZ n = sectionView.ViewDirection;
            if (n == null || n.GetLength() < 1e-6) return;
            n = n.Normalize();
            XYZ eye = geom.Eye - n * (geom.Eye - sectionView.Origin).DotProduct(n);
            try
            {
                Transform cropTf = sectionView.CropBox?.Transform;
                BoundingBoxXYZ cb = sectionView.CropBox;
                if (cropTf != null && cb != null)
                {
                    XYZ local = cropTf.Inverse.OfPoint(eye);
                    double eps = CameraMath.MmToFeet(1.0);
                    if (local.X < cb.Min.X - eps || local.X > cb.Max.X + eps || local.Y < cb.Min.Y - eps || local.Y > cb.Max.Y + eps)
                    {
                        Logger.Info($"相機 Key Plan：剖面標記 眼睛投影點落在裁剪框外（本機 X={CameraMath.FeetToMm(local.X):F0}mm Y={CameraMath.FeetToMm(local.Y):F0}mm，框 X[{CameraMath.FeetToMm(cb.Min.X):F0},{CameraMath.FeetToMm(cb.Max.X):F0}]mm Y[{CameraMath.FeetToMm(cb.Min.Y):F0},{CameraMath.FeetToMm(cb.Max.Y):F0}]mm），略過整個標記");
                        return;
                    }
                }
            }
            catch (Exception exCrop)
            {
                Logger.Error("相機 Key Plan：剖面標記 檢查眼睛是否在裁剪框內失敗，照常繪製", exCrop);
            }
            XYZ alongRaw = forwardHoriz - n * forwardHoriz.DotProduct(n);
            alongRaw = new XYZ(alongRaw.X, alongRaw.Y, 0);
            double lenA = alongRaw.GetLength();
            double planeAngleDeg = Math.Acos(Math.Max(0.0, Math.Min(1.0, lenA))) * 180.0 / Math.PI;

            // 2026-09-27（MCP 實測，D-044 修正②，Kitchen 01 轉 90° 後剖面看向與相機視線幾乎同向：側視垂直
            // 扇形誤導使用者）：剖面「看向」＝−n（BuildSectionBox 的 viewDir 是 n＝BasisZ，實際看進去的方向是 −n）；
            // 與水平視線 forwardHoriz 夾角 < SectionFanSuppressAlongSightDeg → 這其實是「順著視線看下去」，不是
            // 側視，垂直扇形（假設視線躺在剖面畫面裡）會誤導，只畫眼睛圓圈（平面上的扇形不受影響）。
            double sightAlignDot = Math.Max(-1.0, Math.Min(1.0, -(n.DotProduct(forwardHoriz))));
            double sightAlignDeg = Math.Acos(sightAlignDot) * 180.0 / Math.PI;
            bool looksAlongSight = sightAlignDeg < SectionFanSuppressAlongSightDeg;

            double circleR = PrintSizeFeet(sectionView, MarkerCirclePrintRadiusMm);
            List<Curve> curves = new List<Curve>();
            XYZ sideAxis = lenA > 1e-6 ? alongRaw / lenA : up.CrossProduct(n).Normalize();
            AddCircle(curves, eye, circleR, sideAxis, up);

            if (lenA < 0.05 || looksAlongSight)
            {
                CreateMarkerCurves(doc, sectionView, curves, cid, "MarkerSection", style);
                string why = lenA < 0.05
                    ? $"視線幾乎垂直剖面平面（夾角 {planeAngleDeg:F1}°）"
                    : $"剖面看向（−BasisZ）與相機視線夾角 {sightAlignDeg:F1}° < {SectionFanSuppressAlongSightDeg:F0}°（順著視線看，側視垂直扇形會誤導）";
                Logger.Info($"相機 Key Plan：剖面標記 {why}，只畫眼睛圓圈（平面扇形不受影響）");
                return;
            }
            XYZ alongN = alongRaw / lenA;
            double radius = fanRadiusFeet;
            if (!(radius > 0) || double.IsInfinity(radius) || double.IsNaN(radius)) radius = CameraMath.MmToFeet(FanMinRadiusMm);

            curves.Add(Line.CreateBound(eye, eye + alongRaw * radius)); // 水平視線（投影後長度）

            double pitch = 0, aLow = 0, aHigh = 0;
            XYZ dLow = alongRaw, dHigh = alongRaw, dMid = alongRaw;
            bool fanOk = false;
            try
            {
                // 2026-09-25（非對稱視錐）：垂直扇形從 pitch+downRad 到 pitch+upRad（相對正前方帶號角，上為正），
                // 不再是 pitch ± v/2。中央視線（水平視線）不變。
                pitch = Math.Asin(Math.Max(-1.0, Math.Min(1.0, geom.Forward.Z)));
                aLow = pitch + downRad; aHigh = pitch + upRad;
                double aMid = (aLow + aHigh) / 2.0;
                dLow = alongRaw * Math.Cos(aLow) + up * Math.Sin(aLow);
                dHigh = alongRaw * Math.Cos(aHigh) + up * Math.Sin(aHigh);
                dMid = alongRaw * Math.Cos(aMid) + up * Math.Sin(aMid);
                fanOk = true;
            }
            catch (Exception ex)
            {
                Logger.Error("相機 Key Plan：剖面垂直扇形方向計算失敗，只畫視線", ex);
            }

            double arrowLen = ComputeArrowLenFeet(sectionView, radius * lenA);
            // 楔形沿扇形邊界取「與扇形同比例」的長度：k＝arrowLen／(投影後扇形半徑)，夾 ≤ 1。
            double wedgeK = Math.Min(1.0, arrowLen / Math.Max(1e-9, radius * lenA));

            if (fanOk && radius > circleR * 2)
            {
                try
                {
                    XYZ lowEnd = eye + dLow * radius, highEnd = eye + dHigh * radius, midEnd = eye + dMid * radius;
                    curves.Add(Line.CreateBound(eye, lowEnd));
                    curves.Add(Line.CreateBound(eye, highEnd));
                    curves.Add(Arc.Create(lowEnd, highEnd, midEnd)); // 三點弧（斜交時為橢圓弧的近似）
                    try
                    {
                        CurveLoop wedge = new CurveLoop();
                        wedge.Append(Line.CreateBound(eye, lowEnd));
                        wedge.Append(Arc.Create(lowEnd, highEnd, midEnd));
                        wedge.Append(Line.CreateBound(highEnd, eye));
                        CreateFanWedgeRegion(doc, sectionView, wedge, cid, "MarkerSection", style);
                    }
                    catch (Exception ex) { Logger.Error("相機 Key Plan：剖面扇形填滿失敗（邊線照畫）", ex); }
                }
                catch (Exception ex) { Logger.Error("相機 Key Plan：剖面垂直視野扇形失敗，只畫視線", ex); }
            }
            Logger.Info($"相機 Key Plan：剖面垂直視野扇形 pitch={pitch * 180 / Math.PI:F1}° downDeg={downRad * 180 / Math.PI:F1} upDeg={upRad * 180 / Math.PI:F1} " +
                        $"(vFOV={(upRad - downRad) * 180 / Math.PI:F1}°) radius={CameraMath.FeetToMm(radius):F0}mm " +
                        $"視線與剖面平面夾角={planeAngleDeg:F1}°" + (planeAngleDeg > 60 ? "（>60°：扇形投影後明顯縮短）" : ""));

            CreateMarkerCurves(doc, sectionView, curves, cid, "MarkerSection", style);

            // 眼睛楔形（取代舊的朝前三角形箭頭）：頂點在眼睛、沿相同的上下兩條邊界張開。
            if (fanOk)
            {
                try
                {
                    double wl = radius * wedgeK;
                    CreateEyeWedgeRegion(doc, sectionView, eye, eye + dLow * wl, eye + dMid * wl, eye + dHigh * wl,
                        cid, "MarkerSection", style);
                }
                catch (Exception ex)
                {
                    Logger.Error("相機 Key Plan：剖面眼睛楔形填滿失敗（扇形與視線照畫）", ex);
                }
            }
            ApplyMarkerDrawOrder(doc, sectionView, "MarkerSection");
        }

        /// <summary>射線（起點在裁剪框內）沿 dir 走到裁剪框 X/Y 邊界的距離（只看剖面的 X＝水平、Y＝垂直，
        /// 深度 Z 忽略）；起點不在框內或算不出來時回傳 +∞（不截短）。</summary>
        private static double RayToCropEdge(BoundingBoxXYZ crop, XYZ origin, XYZ dir)
        {
            try
            {
                Transform inv = crop.Transform.Inverse;
                XYZ o = inv.OfPoint(origin);
                XYZ d = inv.OfVector(dir);
                if (o.X < crop.Min.X - 1e-6 || o.X > crop.Max.X + 1e-6 || o.Y < crop.Min.Y - 1e-6 || o.Y > crop.Max.Y + 1e-6)
                    return double.PositiveInfinity;
                double t = double.PositiveInfinity;
                if (d.X > 1e-9) t = Math.Min(t, (crop.Max.X - o.X) / d.X);
                else if (d.X < -1e-9) t = Math.Min(t, (crop.Min.X - o.X) / d.X);
                if (d.Y > 1e-9) t = Math.Min(t, (crop.Max.Y - o.Y) / d.Y);
                else if (d.Y < -1e-9) t = Math.Min(t, (crop.Min.Y - o.Y) / d.Y);
                return t > 0 ? t : double.PositiveInfinity;
            }
            catch
            {
                return double.PositiveInfinity;
            }
        }

        // ══════════════════════════════════════════════════════════
        // 小工具
        // ══════════════════════════════════════════════════════════

        /// <summary>Revit 視圖／圖紙／類型名稱不允許的字元（Revit UI 會擋：{}[]:\|;&lt;&gt;?`~）。</summary>
        private static readonly char[] InvalidRevitNameChars = { '{', '}', '[', ']', ':', '\\', '|', ';', '<', '>', '?', '`', '~' };

        /// <summary>把 Revit 名稱不允許的字元換成「_」並去掉控制字元／前後空白；空字串回傳 fallback。
        /// 相機名稱雖然已是合法視圖名，但樓層名、使用者輸入、複製名稱組合後仍可能出現非法字元
        /// （設 Name 會丟 ArgumentException、整筆交易回滾），所以所有建立／改名路徑一律先過這裡。</summary>
        internal static string SanitizeRevitName(string name, string fallback = "未命名")
        {
            if (string.IsNullOrWhiteSpace(name)) return fallback;
            var sb = new System.Text.StringBuilder(name.Length);
            foreach (char c in name)
            {
                if (char.IsControl(c)) continue;
                sb.Append(Array.IndexOf(InvalidRevitNameChars, c) >= 0 ? '_' : c);
            }
            string r = sb.ToString().Trim();
            return r.Length == 0 ? fallback : r;
        }

        /// <summary>名稱是否含 Revit 不允許的字元（給使用者輸入的改名做明確拒絕）。</summary>
        internal static bool HasInvalidRevitNameChars(string name)
            => !string.IsNullOrEmpty(name) && (name.IndexOfAny(InvalidRevitNameChars) >= 0 || name.Any(char.IsControl));

        private static string MakeUniqueName(Document doc, string baseName, ElementId excludeId)
        {
            baseName = SanitizeRevitName(baseName);
            HashSet<string> existing = new HashSet<string>(
                new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>()
                    .Where(v => v.Id != excludeId)
                    .Select(v => v.Name),
                StringComparer.OrdinalIgnoreCase);

            if (!existing.Contains(baseName)) return baseName;
            int suffix = 2;
            string candidate = baseName + " (" + suffix + ")";
            while (existing.Contains(candidate))
            {
                suffix++;
                candidate = baseName + " (" + suffix + ")";
            }
            return candidate;
        }

        /// <summary>同 <see cref="MakeUniqueName"/>，但用於任意 <see cref="ElementType"/> 子類別
        /// （例如 <see cref="FilledRegionType"/>）的名稱去重，不限於 View。</summary>
        private static string MakeUniqueTypeName(Document doc, Type elementType, string baseName)
        {
            baseName = SanitizeRevitName(baseName);
            HashSet<string> existing = new HashSet<string>(
                new FilteredElementCollector(doc).OfClass(elementType).Cast<ElementType>().Select(t => t.Name),
                StringComparer.OrdinalIgnoreCase);

            if (!existing.Contains(baseName)) return baseName;
            int suffix = 2;
            string candidate = baseName + " " + suffix;
            while (existing.Contains(candidate))
            {
                suffix++;
                candidate = baseName + " " + suffix;
            }
            return candidate;
        }
    }
}
