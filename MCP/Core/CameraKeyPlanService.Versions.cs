using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using Newtonsoft.Json.Linq;

namespace RevitMCP.Core
{
    /// <summary>
    /// 2026-09-25 Wave 3：相機「視角版本」（spec §11.5、ADR D-030）。
    ///
    /// 每台相機可存多個具名快照（眼睛 XYZ、前方向、上方向、裁剪框 Min/Max＋Transform、裁剪框啟用、遠裁剪距離與啟用），
    /// 以 Extensible Storage 存在相機 View3D 上——同事開同一個 .rvt 也能切換。
    /// 儲存格式：新 Schema <c>RevitMCP_CameraViewVersions</c>，只有一個 string 欄位 <c>VersionsJson</c>（JSON 陣列）。
    /// 選 JSON 字串而非多個 double 欄位：欄位數固定、之後加欄位不必換 Schema GUID，也沒有 double 欄位缺單位的
    /// 問題（D-015 教訓）。長度一律 feet（Revit 內部單位），向量為無單位分量。
    /// 儲存／刪除只動 ES（不影響模型幾何）；還原是單一交易「還原相機視角：&lt;名稱&gt;」，
    /// 相機被修改後照常經 DocumentChanged → 500ms 去抖更新標記／A 型標記／樓層總覽標記。
    /// </summary>
    internal static partial class CameraKeyPlanService
    {
        // 固定 GUID，不要改——已寫入 .rvt 的 Schema 靠這個值辨識。
        private static readonly Guid VersionSchemaGuid = new Guid("A8D12364-5CAE-46E8-9548-41728EC43746");
        private const string FieldVersionsJson = "VersionsJson";
        internal const string VersionSaveTransactionName = "儲存相機視角版本";
        internal const string VersionDeleteTransactionName = "刪除相機視角版本";
        internal const string VersionRestoreTransactionPrefix = "還原相機視角：";
        private const int VersionNameMaxLength = 60;
        /// <summary>2026-09-26 第四輪（使用者規則）：相機第一次被任何路徑修改前（清單編輯、set_camera_pose、拖 A 標記、
        /// 地圖拖曳、導覽、人視高套用）若沒有任何版本 → 自動把原始視角存成這個名稱。</summary>
        internal const string OriginalVersionName = "A案";
        private const double VersionMatchMm = 1.0;
        private const double VersionMatchDeg = 0.05;

        internal sealed class CameraViewVersion
        {
            public string Name;
            public string SavedAt;
            public XYZ Eye, Forward, Up;
            public bool CropActive;
            public XYZ CropMin, CropMax, CropOrigin, CropBasisX, CropBasisY, CropBasisZ;
            public double? FarClipFeet;
            public int? FarClipActive;
        }

        private static Schema GetOrCreateVersionSchema()
        {
            Schema schema = Schema.Lookup(VersionSchemaGuid);
            if (schema != null) return schema;
            SchemaBuilder b = new SchemaBuilder(VersionSchemaGuid);
            b.SetSchemaName("RevitMCP_CameraViewVersions");
            b.SetReadAccessLevel(AccessLevel.Public);
            b.SetWriteAccessLevel(AccessLevel.Public);
            b.AddSimpleField(FieldVersionsJson, typeof(string));
            return b.Finish();
        }

        private static JArray P(XYZ p) => p == null ? null : new JArray(p.X, p.Y, p.Z);

        private static XYZ ToXyz(JToken tok)
        {
            if (!(tok is JArray a) || a.Count != 3) return null;
            return new XYZ(a[0].Value<double>(), a[1].Value<double>(), a[2].Value<double>());
        }

        /// <summary>讀出相機上所有版本（依儲存順序）；沒有或讀取失敗＝空清單（只記 log，不拋）。</summary>
        internal static List<CameraViewVersion> ReadVersions(View3D camera)
        {
            var list = new List<CameraViewVersion>();
            try
            {
                Schema schema = Schema.Lookup(VersionSchemaGuid);
                if (schema == null || camera == null) return list;
                Entity e = camera.GetEntity(schema);
                if (e == null || !e.IsValid()) return list;
                string json = e.Get<string>(schema.GetField(FieldVersionsJson));
                if (string.IsNullOrWhiteSpace(json)) return list;
                foreach (JObject o in JArray.Parse(json).OfType<JObject>())
                {
                    // 2026-09-26 稽核：單筆壞掉（例如座標欄位為 null）只略過那一筆，不讓整份清單變空。
                    CameraViewVersion v;
                    try
                    {
                    v = new CameraViewVersion
                    {
                        Name = o.Value<string>("name"),
                        SavedAt = o.Value<string>("savedAt"),
                        Eye = ToXyz(o["eye"]),
                        Forward = ToXyz(o["forward"]),
                        Up = ToXyz(o["up"]),
                        CropActive = o.Value<bool?>("cropActive") ?? true,
                        CropMin = ToXyz(o["cropMin"]),
                        CropMax = ToXyz(o["cropMax"]),
                        CropOrigin = ToXyz(o["cropOrigin"]),
                        CropBasisX = ToXyz(o["cropBasisX"]),
                        CropBasisY = ToXyz(o["cropBasisY"]),
                        CropBasisZ = ToXyz(o["cropBasisZ"]),
                        FarClipFeet = o.Value<double?>("farClipFeet"),
                        FarClipActive = o.Value<int?>("farClipActive")
                    };
                    }
                    catch (Exception exItem)
                    {
                        Logger.Error($"相機視角版本：「{camera?.Name}」有一筆版本資料無法解析（略過該筆）", exItem);
                        continue;
                    }
                    if (!string.IsNullOrEmpty(v.Name) && v.Eye != null && v.Forward != null && v.Up != null) list.Add(v);
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"相機視角版本：讀取「{camera?.Name}」的版本失敗（視為沒有版本）", ex);
            }
            return list;
        }

        internal static List<string> ReadVersionNames(View3D camera) => ReadVersionsNewestFirst(camera).Select(v => v.Name).ToList();

        /// <summary>2026-09-26 第四輪：版本依儲存時間「新到舊」排序（同時間依儲存順序新到舊）。</summary>
        internal static List<CameraViewVersion> ReadVersionsNewestFirst(View3D camera)
        {
            List<CameraViewVersion> list = ReadVersions(camera);
            return list.Select((v, i) => (v, i))
                .OrderByDescending(x => x.v.SavedAt ?? "", StringComparer.Ordinal).ThenByDescending(x => x.i)
                .Select(x => x.v).ToList();
        }

        private static double AngleDeg(XYZ a, XYZ b)
        {
            if (a == null || b == null || a.GetLength() < 1e-9 || b.GetLength() < 1e-9) return 180.0;
            double c = Math.Max(-1.0, Math.Min(1.0, a.Normalize().DotProduct(b.Normalize())));
            return Math.Acos(c) * 180.0 / Math.PI;
        }

        /// <summary>兩個視角是否相同：眼睛 ≤ 1 mm、前／上方向 ≤ 0.05°；兩邊都有裁剪框時 Min/Max ≤ 1 mm。</summary>
        internal static bool VersionMatchesPose(CameraViewVersion v, CameraViewVersion cur)
        {
            if (v?.Eye == null || cur?.Eye == null) return false;
            double tol = CameraMath.MmToFeet(VersionMatchMm);
            if (v.Eye.DistanceTo(cur.Eye) > tol) return false;
            if (AngleDeg(v.Forward, cur.Forward) > VersionMatchDeg) return false;
            if (AngleDeg(v.Up, cur.Up) > VersionMatchDeg) return false;
            if (v.CropMin != null && v.CropMax != null && cur.CropMin != null && cur.CropMax != null)
            {
                if (v.CropMin.DistanceTo(cur.CropMin) > tol || v.CropMax.DistanceTo(cur.CropMax) > tol) return false;
            }
            return true;
        }

        /// <summary>目前視角符合的版本名稱（新到舊第一個符合者）；都不符合＝null。</summary>
        internal static string FindCurrentVersionName(View3D camera, List<CameraViewVersion> newestFirst = null)
        {
            try
            {
                List<CameraViewVersion> list = newestFirst ?? ReadVersionsNewestFirst(camera);
                if (list.Count == 0) return null;
                CameraViewVersion cur = CaptureVersion(camera, "");
                return list.FirstOrDefault(v => VersionMatchesPose(v, cur))?.Name;
            }
            catch (Exception ex)
            {
                Logger.Error("相機視角版本：比對目前視角失敗", ex);
                return null;
            }
        }

        /// <summary>下一個「字母＋案」名稱：A案、B案…（「A 案」有空格的舊名也算已用）；26 個都用了 → 「版本 N」。</summary>
        internal static string SuggestNextVersionName(IEnumerable<string> existingNames)
        {
            var existing = new HashSet<string>((existingNames ?? Enumerable.Empty<string>()).Select(n => (n ?? "").Replace(" ", "")), StringComparer.Ordinal);
            for (int i = 0; i < 26; i++)
            {
                string s = ((char)('A' + i)) + "案";
                if (!existing.Contains(s)) return s;
            }
            return "版本 " + (existing.Count + 1);
        }

        /// <summary>
        /// 2026-09-26 第四輪：相機「第一次被修改前」自動存原始視角「A案」（沒有任何版本時才存）。須在呼叫端的交易內、
        /// 在修改相機之前呼叫；before＝null 時讀相機目前視角。回傳是否存了。失敗只記 log（不影響後續修改）。
        /// </summary>
        internal static bool EnsureOriginalVersionInTransaction(View3D camera, string source, CameraViewVersion before = null)
        {
            try
            {
                if (camera == null || camera.IsTemplate || !camera.IsPerspective) return false;
                if (ReadVersions(camera).Count > 0) return false;
                CameraViewVersion v = before ?? CaptureVersion(camera, OriginalVersionName);
                v.Name = OriginalVersionName;
                WriteVersions(camera, new List<CameraViewVersion> { v });
                Logger.Info($"相機視角版本：「{camera.Name}」第一次被修改（{source}）前自動存「{OriginalVersionName}」（原始視角）。");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error($"相機視角版本：自動存「{OriginalVersionName}」失敗（{source}；照常修改相機）", ex);
                return false;
            }
        }

        /// <summary>2026-09-26 稽核：相機上的版本 JSON 存在但整份無法解析（損毀）。ReadVersions 此時回空清單——
        /// 若照常寫回，會把使用者所有版本覆蓋掉，所以 WriteVersions 遇到損毀一律拒寫。</summary>
        internal static bool IsVersionDataCorrupt(View3D camera)
        {
            try
            {
                Schema schema = Schema.Lookup(VersionSchemaGuid);
                if (schema == null || camera == null) return false;
                Entity e = camera.GetEntity(schema);
                if (e == null || !e.IsValid()) return false;
                string json = e.Get<string>(schema.GetField(FieldVersionsJson));
                if (string.IsNullOrWhiteSpace(json)) return false;
                JArray.Parse(json);
                return false;
            }
            catch { return true; }
        }

        private static void WriteVersions(View3D camera, List<CameraViewVersion> versions)
        {
            if (IsVersionDataCorrupt(camera))
                throw new InvalidOperationException($"相機「{camera?.Name}」上的視角版本資料無法解析（可能損毀），為避免覆蓋既有版本，本次不寫入。");
            var arr = new JArray();
            foreach (CameraViewVersion v in versions)
            {
                arr.Add(new JObject
                {
                    ["name"] = v.Name,
                    ["savedAt"] = v.SavedAt,
                    ["eye"] = P(v.Eye),
                    ["forward"] = P(v.Forward),
                    ["up"] = P(v.Up),
                    ["cropActive"] = v.CropActive,
                    ["cropMin"] = P(v.CropMin),
                    ["cropMax"] = P(v.CropMax),
                    ["cropOrigin"] = P(v.CropOrigin),
                    ["cropBasisX"] = P(v.CropBasisX),
                    ["cropBasisY"] = P(v.CropBasisY),
                    ["cropBasisZ"] = P(v.CropBasisZ),
                    ["farClipFeet"] = v.FarClipFeet.HasValue ? (JToken)v.FarClipFeet.Value : JValue.CreateNull(),
                    ["farClipActive"] = v.FarClipActive.HasValue ? (JToken)v.FarClipActive.Value : JValue.CreateNull()
                });
            }
            Schema schema = GetOrCreateVersionSchema();
            Entity e = new Entity(schema);
            e.Set(schema.GetField(FieldVersionsJson), arr.ToString(Newtonsoft.Json.Formatting.None));
            camera.SetEntity(e);
        }

        private static Parameter FindFarClipActiveParameter(View3D camera)
        {
            try { return camera.get_Parameter(BuiltInParameter.VIEWER_BOUND_ACTIVE_FAR); } catch { return null; }
        }

        internal static CameraViewVersion CaptureVersion(View3D camera, string name)
        {
            ViewOrientation3D o = camera.GetOrientation();
            var v = new CameraViewVersion
            {
                Name = name,
                SavedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                Eye = o.EyePosition,
                Forward = o.ForwardDirection,
                Up = o.UpDirection,
                CropActive = camera.CropBoxActive
            };
            try
            {
                BoundingBoxXYZ cb = camera.CropBox;
                if (cb != null)
                {
                    Transform tf = cb.Transform ?? Transform.Identity;
                    v.CropMin = cb.Min; v.CropMax = cb.Max;
                    v.CropOrigin = tf.Origin; v.CropBasisX = tf.BasisX; v.CropBasisY = tf.BasisY; v.CropBasisZ = tf.BasisZ;
                }
            }
            catch (Exception ex) { Logger.Error("相機視角版本：讀取裁剪框失敗（版本不含裁剪框）", ex); }
            try
            {
                Parameter far = CameraMath.FindFarClipOffsetParameter(camera);
                if (far != null) v.FarClipFeet = far.AsDouble();
                Parameter act = FindFarClipActiveParameter(camera);
                if (act != null && act.StorageType == StorageType.Integer) v.FarClipActive = act.AsInteger();
            }
            catch (Exception ex) { Logger.Error("相機視角版本：讀取遠裁剪失敗（版本不含遠裁剪）", ex); }
            return v;
        }

        private static View3D ResolveCameraForVersion(Document doc, ElementId cameraId, out string error)
        {
            error = null;
            View3D cam = doc?.GetElement(cameraId) as View3D;
            if (cam == null || cam.IsTemplate || !cam.IsPerspective) { error = "找不到這個相機視圖（可能已被刪除），請按重新整理。"; return null; }
            return cam;
        }

        /// <summary>儲存目前視角為具名版本；同名＝覆蓋。回傳 null＝成功。</summary>
        internal static string SaveVersion(Document doc, ElementId cameraId, string name, out bool replaced)
        {
            replaced = false;
            name = (name ?? "").Trim();
            if (name.Length == 0) return "版本名稱不可空白。";
            if (name.Length > VersionNameMaxLength) return $"版本名稱最多 {VersionNameMaxLength} 字。";
            View3D cam = ResolveCameraForVersion(doc, cameraId, out string err);
            if (cam == null) return err;

            List<CameraViewVersion> list = ReadVersions(cam);
            CameraViewVersion snap = CaptureVersion(cam, name);
            int idx = list.FindIndex(v => string.Equals(v.Name, name, StringComparison.Ordinal));
            if (idx >= 0) { list[idx] = snap; replaced = true; } else list.Add(snap);

            using (Transaction t = TransactionHelper.Begin(doc, VersionSaveTransactionName))
            {
                t.Start();
                try
                {
                    WriteVersions(cam, list);
                    TransactionHelper.CommitOrThrow(t);
                }
                catch (Exception ex)
                {
                    try { if (t.HasStarted() && !t.HasEnded()) t.RollBack(); } catch { }
                    Logger.Error("相機視角版本：儲存失敗", ex);
                    return "儲存視角版本失敗：" + ex.Message;
                }
            }
            Logger.Info($"相機視角版本：「{cam.Name}」{(replaced ? "覆蓋" : "新增")}版本「{name}」（共 {list.Count} 個）。");
            return null;
        }

        /// <summary>刪除具名版本（只移除 ES 裡的一筆，不動任何模型元素）。回傳 null＝成功。</summary>
        internal static string DeleteVersion(Document doc, ElementId cameraId, string name)
        {
            View3D cam = ResolveCameraForVersion(doc, cameraId, out string err);
            if (cam == null) return err;
            List<CameraViewVersion> list = ReadVersions(cam);
            int removed = list.RemoveAll(v => string.Equals(v.Name, (name ?? "").Trim(), StringComparison.Ordinal));
            if (removed == 0) return $"找不到版本「{name}」。";
            using (Transaction t = TransactionHelper.Begin(doc, VersionDeleteTransactionName))
            {
                t.Start();
                try
                {
                    WriteVersions(cam, list);
                    TransactionHelper.CommitOrThrow(t);
                }
                catch (Exception ex)
                {
                    try { if (t.HasStarted() && !t.HasEnded()) t.RollBack(); } catch { }
                    Logger.Error("相機視角版本：刪除失敗", ex);
                    return "刪除視角版本失敗：" + ex.Message;
                }
            }
            Logger.Info($"相機視角版本：「{cam.Name}」刪除版本「{name}」（剩 {list.Count} 個）。");
            return null;
        }

        /// <summary>還原具名版本：單一交易「還原相機視角：&lt;名稱&gt;」。裁剪框／遠裁剪還原失敗只記在 warnings，
        /// 眼睛與方向仍套用。回傳 null＝成功。</summary>
        internal static string RestoreVersion(Document doc, ElementId cameraId, string name, List<string> warnings)
        {
            View3D cam = ResolveCameraForVersion(doc, cameraId, out string err);
            if (cam == null) return err;
            List<CameraViewVersion> all = ReadVersions(cam);
            CameraViewVersion v = all.FirstOrDefault(x => string.Equals(x.Name, (name ?? "").Trim(), StringComparison.Ordinal));
            if (v == null) return $"找不到版本「{name}」。";
            try { if (cam.IsLocked) return "這個相機視圖已鎖定，無法還原視角（請先在 Revit 解除鎖定）。"; } catch { }

            // 2026-09-26 第四輪（使用者規則）：還原前，目前視角若跟每個已存版本都不同（1 mm／0.05°）→ 先自動存「自動備份 HH:mm」。
            string backupName = null;
            try
            {
                CameraViewVersion cur = CaptureVersion(cam, "");
                if (!all.Any(x => VersionMatchesPose(x, cur)))
                {
                    backupName = "自動備份 " + DateTime.Now.ToString("HH:mm", CultureInfo.InvariantCulture);
                    if (all.Any(x => x.Name == backupName)) backupName = "自動備份 " + DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
                    cur.Name = backupName;
                    all.Add(cur);
                }
            }
            catch (Exception exB) { Logger.Error("相機視角版本：還原前自動備份判斷失敗（照常還原）", exB); backupName = null; }

            using (Transaction t = TransactionHelper.Begin(doc, VersionRestoreTransactionPrefix + v.Name))
            {
                t.Start();
                try
                {
                    if (backupName != null)
                    {
                        try { WriteVersions(cam, all); warnings?.Add($"還原前已自動備份目前視角為「{backupName}」"); }
                        catch (Exception exW) { Logger.Error("相機視角版本：還原前自動備份寫入失敗（照常還原）", exW); }
                    }
                    cam.SetOrientation(new ViewOrientation3D(v.Eye, v.Up, v.Forward));

                    // 2026-09-26 稽核：「裁剪啟用」與「裁剪框」分開 try——透視圖裁剪通常不能關、或被樣板控制，
                    // 舊版一起放在同一個 try，啟用設定一丟例外就連裁剪框也沒還原。
                    try
                    {
                        if (cam.CropBoxActive != v.CropActive) cam.CropBoxActive = v.CropActive;
                    }
                    catch (Exception exAct)
                    {
                        warnings?.Add("裁剪啟用狀態還原失敗（" + (v.CropActive ? "開" : "關") + "）：" + exAct.Message);
                        Logger.Error("相機視角版本：還原裁剪啟用狀態失敗（繼續還原裁剪框）", exAct);
                    }
                    try
                    {
                        if (v.CropMin != null && v.CropMax != null && v.CropOrigin != null && v.CropBasisX != null && v.CropBasisY != null && v.CropBasisZ != null)
                        {
                            Transform tf = Transform.CreateTranslation(XYZ.Zero);
                            tf.Origin = v.CropOrigin; tf.BasisX = v.CropBasisX; tf.BasisY = v.CropBasisY; tf.BasisZ = v.CropBasisZ;
                            cam.CropBox = new BoundingBoxXYZ { Transform = tf, Min = v.CropMin, Max = v.CropMax };
                        }
                    }
                    catch (Exception exCrop)
                    {
                        warnings?.Add("裁剪框還原失敗：" + exCrop.Message);
                        Logger.Error("相機視角版本：還原裁剪框失敗（眼睛與方向照常套用）", exCrop);
                    }

                    try
                    {
                        Parameter act = FindFarClipActiveParameter(cam);
                        if (v.FarClipActive.HasValue && act != null && !act.IsReadOnly && act.AsInteger() != v.FarClipActive.Value) act.Set(v.FarClipActive.Value);
                        Parameter far = CameraMath.FindFarClipOffsetParameter(cam);
                        if (v.FarClipFeet.HasValue && far != null && !far.IsReadOnly) far.Set(v.FarClipFeet.Value);
                        else if (v.FarClipFeet.HasValue && (far == null || far.IsReadOnly)) warnings?.Add("遠裁剪參數不可寫，未還原遠裁剪。");
                    }
                    catch (Exception exFar)
                    {
                        warnings?.Add("遠裁剪還原失敗：" + exFar.Message);
                        Logger.Error("相機視角版本：還原遠裁剪失敗", exFar);
                    }

                    TransactionHelper.CommitOrThrow(t);
                }
                catch (Exception ex)
                {
                    try { if (t.HasStarted() && !t.HasEnded()) t.RollBack(); } catch { }
                    Logger.Error("相機視角版本：還原失敗（整筆回滾）", ex);
                    return "還原視角失敗：" + ex.Message;
                }
            }
            Logger.Info($"相機視角版本：「{cam.Name}」還原版本「{v.Name}」" + (warnings != null && warnings.Count > 0 ? "；" + string.Join("；", warnings) : ""));
            return null;
        }
    }
}
