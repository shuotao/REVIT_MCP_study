using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using Autodesk.Revit.UI;

namespace RevitMCP.Core
{
    /// <summary>
    /// 2026-09-26 第四輪（使用者實測：A 標記模式下每次相機變動都「運算量很大、當機」；Navigation Wheel 繞行後標記不跟）：
    /// 相機變動後的「增量」更新（D-041）。
    ///   ① 只處理真的變了的相機：姿態雜湊＝眼睛／前方向／上方向／裁剪框／遠裁剪＋模型 epoch；與上次畫標記時相同 → 整台略過。
    ///   ② 樓層總覽只動「舊樓層＋新樓層」那兩張的這台相機標記；A 標記只同步變了的相機；簡化樓層記號只在樓層變了才重畫。
    ///   ③ 眼睛移動 &gt; 300 mm 或水平角變 &gt; 5°、且相機已有 Key Plan → 走一鍵 4 視圖的更新路徑（不並排）重算範圍／剖面／等角。
    ///   ④ 導覽（Navigation Wheel 等不觸發 DocumentChanged 的變動）偵測到的相機，若沒有任何視角版本 → 先把「移動前」的姿態存成「A案」。
    /// 全部在呼叫端的 API context（面板佇列 ExternalEvent 或 MCP ExternalEvent）內執行；每一步 try/catch＋log。
    /// </summary>
    internal static partial class CameraKeyPlanService
    {
        /// <summary>變動前的姿態提示（面板清單列舊值、導覽監看的基準）。Before＝移動前的完整視角（自動存「A案」用）。</summary>
        internal sealed class CameraPoseHint
        {
            public XYZ Eye;
            public double YawDeg;
            public CameraViewVersion Before;
            public string Source;
            /// <summary>2026-09-26 第六輪：變動前的俯仰角（°）與樓層 Id（清單列值）；後續更新分級用。null＝不知道。</summary>
            public double? PitchDeg;
            public long? StoreyId;
            /// <summary>這次變動是否改了裁剪框／遠裁剪（視野形狀）。null＝不知道（導覽監看），視為可能有改。</summary>
            public bool? ShapeChanged;
        }

        private sealed class PoseRecord
        {
            public string Hash;
            public XYZ Eye;
            public double YawDeg;
            public long? StoreyId;
            // 2026-09-26 第六輪：後續更新分級用。
            public double? PitchDeg;
            public string ShapeHash;
            public long? RoomId;
            /// <summary>眼睛所在房間的外框最大邊長（mm）；室外／沒有房間＝null。</summary>
            public double? RoomSizeMm;
        }

        /// <summary>上次畫標記時的姿態（鍵＝文件＋相機 UniqueId）。</summary>
        private static readonly Dictionary<string, PoseRecord> _lastDrawn = new Dictionary<string, PoseRecord>(StringComparer.Ordinal);
        /// <summary>上次 Key Plan 範圍（一鍵 4 視圖）計算時的姿態。</summary>
        private static readonly Dictionary<string, PoseRecord> _lastKeyPlan = new Dictionary<string, PoseRecord>(StringComparer.Ordinal);

        // 本工具自己改了 Key Plan／總覽視圖內容（標記、樓層記號、裁剪）的次數；③ 位置圖若用這些視圖當底圖，底圖點陣快取以此判斷過期。
        private static int _ownViewsEpoch;
        internal static int OwnViewsEpoch => System.Threading.Volatile.Read(ref _ownViewsEpoch);
        internal static void BumpOwnViewsEpoch() => System.Threading.Interlocked.Increment(ref _ownViewsEpoch);

        /// <summary>這張視圖是不是本工具建立／連結的視圖（Key Plan 平面／剖面／等角、樓層總覽…）。</summary>
        internal static bool IsOwnTaggedView(View v)
        {
            try { return v != null && Schema.Lookup(SchemaGuid) != null && TryReadLink(v, out _, out _); } catch { return false; }
        }

        /// <summary>2026-09-26 第七輪（list_cameras 回報底圖＝「Key Plan-總覽-L4」）：本工具產生的視圖一律不當底圖／複製來源——
        /// ES 標記的 Key Plan 平面／總覽／剖面／等角／圖紙透視複本，或名稱以「Key Plan-」開頭（含使用者手動複製、ES 遺失者）。</summary>
        internal static bool IsToolGeneratedView(View v)
        {
            if (v == null) return false;
            try { if ((v.Name ?? "").StartsWith("Key Plan-", StringComparison.OrdinalIgnoreCase)) return true; } catch { }
            return IsOwnTaggedView(v);
        }

        internal const double RealignMoveMm = 300.0;
        internal const double RealignYawDeg = 5.0;

        private static string PoseKey(Document doc, string cid) => StoreyDocKey(doc) + "|" + cid;

        private static string R(double v, int digits) => Math.Round(v, digits).ToString(CultureInfo.InvariantCulture);

        /// <summary>
        /// 相機姿態雜湊：眼睛（0.1 mm）、前方向／上方向（1e-5）、裁剪框啟用與 Min/Max（0.1 mm）、遠裁剪（0.1 mm）。
        /// 只讀，便宜（面板導覽監看每 300 ms 呼叫一次）。讀取失敗的欄位記成「?」。
        /// </summary>
        internal static string ComputePoseHash(View3D cam)
        {
            if (cam == null) return "";
            var sb = new System.Text.StringBuilder(160);
            try
            {
                ViewOrientation3D o = cam.GetOrientation();
                XYZ e = o.EyePosition, f = o.ForwardDirection, u = o.UpDirection;
                sb.Append(R(CameraMath.FeetToMm(e.X), 1)).Append(',').Append(R(CameraMath.FeetToMm(e.Y), 1)).Append(',').Append(R(CameraMath.FeetToMm(e.Z), 1));
                sb.Append('|').Append(R(f.X, 5)).Append(',').Append(R(f.Y, 5)).Append(',').Append(R(f.Z, 5));
                sb.Append('|').Append(R(u.X, 5)).Append(',').Append(R(u.Y, 5)).Append(',').Append(R(u.Z, 5));
            }
            catch { sb.Append("?"); }
            try
            {
                sb.Append('|').Append(cam.CropBoxActive ? 1 : 0);
                BoundingBoxXYZ cb = cam.CropBox;
                if (cb != null)
                    sb.Append('|').Append(R(CameraMath.FeetToMm(cb.Min.X), 1)).Append(',').Append(R(CameraMath.FeetToMm(cb.Min.Y), 1)).Append(',').Append(R(CameraMath.FeetToMm(cb.Min.Z), 1))
                      .Append(',').Append(R(CameraMath.FeetToMm(cb.Max.X), 1)).Append(',').Append(R(CameraMath.FeetToMm(cb.Max.Y), 1)).Append(',').Append(R(CameraMath.FeetToMm(cb.Max.Z), 1));
            }
            catch { sb.Append("|?"); }
            try
            {
                Parameter far = CameraMath.FindFarClipOffsetParameter(cam);
                sb.Append('|').Append(far != null ? R(CameraMath.FeetToMm(far.AsDouble()), 1) : "-");
            }
            catch { sb.Append("|?"); }
            // 2026-09-26 稽核：遠裁剪開／關也列入（舊版只看距離，切換開關不會觸發標記更新）。
            try
            {
                Parameter act = cam.get_Parameter(BuiltInParameter.VIEWER_BOUND_ACTIVE_FAR);
                sb.Append("|fa").Append(act != null && act.StorageType == StorageType.Integer ? act.AsInteger().ToString(CultureInfo.InvariantCulture) : "-");
            }
            catch { sb.Append("|fa?"); }
            return sb.ToString();
        }

        private static double YawOf(XYZ forward)
        {
            try { return CameraMath.ComputeHorizontalAngleDeg(forward); } catch { return 0; }
        }

        /// <summary>標記用雜湊＝姿態＋模型 epoch（房間變了扇形半徑也會變）＋標記型式（切換型式一律強制重畫，此處只是保險）。</summary>
        private static string MarkerHash(View3D cam) => ComputePoseHash(cam) + "|e" + ModelEpoch + "|m" + GetMarkerMode();

        private static PoseRecord BuildPoseRecord(View3D cam, CameraParams cp, string hash)
        {
            var r = new PoseRecord
            {
                Hash = hash,
                Eye = cp?.Geom?.Eye,
                YawDeg = cp?.Geom?.Forward != null ? YawOf(cp.Geom.Forward) : 0,
                PitchDeg = cp?.Geom?.PitchDeg,
                StoreyId = cp?.Geom?.LevelBelow != null ? (long?)cp.Geom.LevelBelow.Id.GetIdValue() : null,
                ShapeHash = ComputeShapeHash(cam)
            };
            try
            {
                RoomCtx rc = cp?.Room;
                if (rc?.Room != null && !cp.IsOutdoor)
                {
                    r.RoomId = rc.Room.Id.GetIdValue();
                    double size = Math.Max(rc.MaxX - rc.MinX, rc.MaxY - rc.MinY);
                    if (size > 0 && !double.IsInfinity(size) && !double.IsNaN(size)) r.RoomSizeMm = CameraMath.FeetToMm(size);
                }
            }
            catch { }
            return r;
        }

        private static void RecordDrawn(Document doc, View3D cam, CameraParams cp)
        {
            try { _lastDrawn[PoseKey(doc, cam.UniqueId)] = BuildPoseRecord(cam, cp, MarkerHash(cam)); }
            catch { }
        }

        private static void RecordKeyPlanPose(Document doc, View3D cam, CameraParams cp)
        {
            try { _lastKeyPlan[PoseKey(doc, cam.UniqueId)] = BuildPoseRecord(cam, cp, ComputePoseHash(cam)); }
            catch { }
        }

        /// <summary>2026-09-26 稽核：文件關閉／開啟、或 Undo／Redo 後，舊的「上次已畫」紀錄不再可信 → 全清，下次一律重畫。</summary>
        internal static void ClearPoseRecords()
        {
            _lastDrawn.Clear();
            _lastKeyPlan.Clear();
        }

        /// <summary>2026-09-26 稽核：某台相機的標記／剖面重畫失敗 → 移除「已畫」紀錄，避免之後被姿態雜湊判定「沒變」而永遠略過。</summary>
        private static void ForgetDrawn(Document doc, string cid)
        {
            try { if (doc != null && !string.IsNullOrEmpty(cid)) _lastDrawn.Remove(PoseKey(doc, cid)); } catch { }
        }

        /// <summary>視野形狀雜湊：裁剪框啟用與 Min/Max（0.1 mm）＋遠裁剪（0.1 mm）。與眼睛／方向無關。</summary>
        private static string ComputeShapeHash(View3D cam)
        {
            string h = ComputePoseHash(cam);
            int i = h.IndexOf('|');                   // 眼睛之後
            i = i >= 0 ? h.IndexOf('|', i + 1) : -1;  // 前方向之後
            i = i >= 0 ? h.IndexOf('|', i + 1) : -1;  // 上方向之後
            return i >= 0 ? h.Substring(i + 1) : h;
        }

        private static long? LastDrawnStorey(Document doc, string cid)
            => _lastDrawn.TryGetValue(PoseKey(doc, cid), out PoseRecord r) ? r.StoreyId : null;

        private static bool HasLastDrawnRecord(Document doc, string cid) => _lastDrawn.ContainsKey(PoseKey(doc, cid));

        /// <summary>這台相機是否已有 Key Plan 平面／剖面／等角任一張（ES 連結）。</summary>
        internal static bool HasKeyPlanViews(Document doc, string cid)
        {
            if (doc == null || string.IsNullOrEmpty(cid) || Schema.Lookup(SchemaGuid) == null) return false;
            foreach (View v in new FilteredElementCollector(doc).OfClass(typeof(View)).WherePasses(new ExtensibleStorageFilter(SchemaGuid)).Cast<View>())
            {
                if (v.IsTemplate) continue;
                if (TryReadLink(v, out string c, out string r) && c == cid && (r == "Plan" || r == "Section" || r == "Iso")) return true;
            }
            return false;
        }

        /// <summary>2026-09-26 第六輪：相機變動後續更新的分級（由輕到重）。</summary>
        internal enum FollowUpKind
        {
            /// <summary>只有高度變 → 只重畫剖面上的標記群組（樓層沒變，平面扇形／平面裁剪／等角框都不受影響）。</summary>
            SectionOnly,
            /// <summary>俯仰角變 → 剖面標記＋A 標記（A 標記族群有俯仰角參數）。</summary>
            SectionPitch,
            /// <summary>水平角／視野（裁剪框、遠裁剪）變，或無法分類 → 平面＋剖面標記（＋樓層總覽／A 標記）。</summary>
            Markers,
            /// <summary>平面移動、仍在同一房間且不超過房間大小 → 標記＋剖面重新對位（剖面框跟著眼睛移，不動平面／等角）。</summary>
            SectionRealign,
            /// <summary>換樓層、換房間或大幅移動 → 一鍵 4 視圖的更新路徑（不並排）。</summary>
            Full
        }

        private static string KindText(FollowUpKind k)
        {
            switch (k)
            {
                case FollowUpKind.SectionOnly: return "只重畫剖面標記";
                case FollowUpKind.SectionPitch: return "剖面標記＋A 標記";
                case FollowUpKind.Markers: return "平面＋剖面標記";
                case FollowUpKind.SectionRealign: return "標記＋剖面重新對位";
                default: return "完整 Key Plan 更新";
            }
        }

        /// <summary>
        /// 相機變動後的更新入口（面板去抖佇列、set_camera_pose 後續更新共用）。回傳摘要（狀態列／log）。
        /// 2026-09-26 第六輪（使用者：改離樓板高度要快；舊版眼睛移動 &gt; 300 mm 就跑整個一鍵 4 視圖、好幾秒）：
        /// 先依「與上次畫標記時的姿態」比對分級（見 <see cref="FollowUpKind"/>）——高度／俯仰角的編輯永遠不走完整 Key Plan。
        /// 比較基準：上次畫標記的紀錄（本 session）→ 沒有時用呼叫端給的變動前姿態（清單列舊值）→ 都沒有＝重畫平面＋剖面標記。
        /// uiApp 可為 null（此時不做 Key Plan 範圍／剖面重算，只更新標記）。
        /// </summary>
        internal static string UpdateCamerasAfterChange(UIApplication uiApp, Document doc, ICollection<ElementId> cameraIds,
            IDictionary<ElementId, CameraPoseHint> hints, out int updated)
            => UpdateCamerasAfterChange(uiApp, doc, cameraIds, hints, out updated, out _);

        /// <param name="storeyOrRoomChanged">有相機換了樓層／房間或大幅移動（走了完整 Key Plan 更新）——面板據此補一次清單重讀
        /// （樓層欄、室內外）；其他分級時清單列已由編輯快速路徑就地更新，不必重讀。</param>
        internal static string UpdateCamerasAfterChange(UIApplication uiApp, Document doc, ICollection<ElementId> cameraIds,
            IDictionary<ElementId, CameraPoseHint> hints, out int updated, out bool storeyOrRoomChanged)
        {
            updated = 0;
            storeyOrRoomChanged = false;
            if (doc == null || cameraIds == null || cameraIds.Count == 0) return "沒有相機需要更新。";
            var notes = new List<string>();
            var redrawn = new List<string>();
            using (CameraPerf perf = CameraPerf.Begin($"相機變動後續更新（去抖；{cameraIds.Count} 台）"))
            {
                // ④ 導覽前的姿態 → 自動「A案」（只在沒有任何版本時）
                try
                {
                    var toSave = new List<(View3D cam, CameraViewVersion v)>();
                    if (hints != null)
                    {
                        foreach (var kv in hints)
                        {
                            if (kv.Value?.Before == null || !cameraIds.Contains(kv.Key)) continue;
                            if (!(doc.GetElement(kv.Key) is View3D cam) || cam.IsTemplate || !cam.IsPerspective) continue;
                            if (ReadVersions(cam).Count > 0) continue;
                            // 2026-09-26 稽核：版本資料損毀的相機寫入會被拒 → 不放進同一交易（否則整批回滾、其他相機也存不到 A案）。
                            if (IsVersionDataCorrupt(cam)) { Logger.Info($"相機視角版本：「{cam.Name}」版本資料損毀，略過自動存 A案。"); continue; }
                            toSave.Add((cam, kv.Value.Before));
                        }
                    }
                    if (toSave.Count > 0)
                    {
                        using (Transaction t = TransactionHelper.Begin(doc, VersionSaveTransactionName))
                        {
                            t.Start();
                            try
                            {
                                foreach (var (cam, v) in toSave)
                                {
                                    v.Name = OriginalVersionName;
                                    WriteVersions(cam, new List<CameraViewVersion> { v });
                                    Logger.Info($"相機視角版本：「{cam.Name}」第一次被導覽修改前自動存「{OriginalVersionName}」（原始視角）。");
                                }
                                TransactionHelper.CommitOrThrow(t);
                                notes.Add($"自動存「{OriginalVersionName}」{toSave.Count} 台");
                            }
                            catch (Exception ex)
                            {
                                try { if (t.HasStarted() && !t.HasEnded()) t.RollBack(); } catch { }
                                Logger.Error("相機視角版本：導覽前自動存 A案 失敗（略過）", ex);
                            }
                        }
                    }
                }
                catch (Exception ex) { Logger.Error("相機變動後更新：自動存原始版本失敗（略過）", ex); }
                perf.Lap("自動存A案");

                // ③ 分級
                var groups = new Dictionary<FollowUpKind, List<ElementId>>();
                var why = new Dictionary<ElementId, string>();
                List<Level> levels = null;
                int unchanged = 0;
                foreach (ElementId id in cameraIds)
                {
                    FollowUpKind kind;
                    string reason;
                    try
                    {
                        if (!(doc.GetElement(id) is View3D cam) || cam.IsTemplate || !cam.IsPerspective || IsSheetPerspectiveCopy(cam)) continue;
                        if (levels == null) levels = CameraMath.GetStoreyLevelsSorted(doc);
                        kind = ClassifyChange(uiApp, doc, cam, hints != null && hints.TryGetValue(id, out CameraPoseHint h) ? h : null, levels, out reason, out bool same, out bool storeyChanged);
                        if (same) { unchanged++; continue; }
                        if (storeyChanged) storeyOrRoomChanged = true;
                    }
                    catch (Exception ex)
                    {
                        Logger.Error("相機變動後更新：分級失敗（改為重畫平面＋剖面標記）", ex);
                        kind = FollowUpKind.Markers;
                        reason = "分級失敗：" + ex.Message;
                    }
                    if (!groups.TryGetValue(kind, out List<ElementId> list)) groups[kind] = list = new List<ElementId>();
                    list.Add(id);
                    why[id] = reason;
                    Logger.Info($"相機變動後續更新：相機 id={id.GetIdValue()} → {KindText(kind)}（{reason}）");
                }
                perf.Lap("分級");

                List<ElementId> G(FollowUpKind k)
                {
                    if (!groups.TryGetValue(k, out List<ElementId> l)) groups[k] = l = new List<ElementId>();
                    return l;
                }

                // 完整 Key Plan（換樓層／換房間／大幅移動）
                if (G(FollowUpKind.Full).Count > 0) storeyOrRoomChanged = true;
                foreach (ElementId id in G(FollowUpKind.Full).ToList())
                {
                    try
                    {
                        Logger.Info($"相機變動後更新：相機 id={id.GetIdValue()} {why[id]} → 重算 Key Plan 範圍（一鍵 4 視圖更新路徑，不並排）");
                        KeyPlanResult r = CreateOrUpdateFourViews(uiApp, doc, id);
                        if (r.Error != null) { notes.Add("Key Plan 重算失敗：" + r.Error); G(FollowUpKind.Markers).Add(id); }
                        else { updated++; notes.Add("已重算 Key Plan 範圍（" + why[id] + "）"); redrawn.Add("完整 Key Plan×1"); }
                    }
                    catch (Exception ex)
                    {
                        Logger.Error("相機變動後更新：重算 Key Plan 失敗（改為只更新標記）", ex);
                        CameraManagerDiagnostics.RecordCaught("RealignKeyPlan", ex);
                        G(FollowUpKind.Markers).Add(id);
                    }
                }
                perf.Lap("完整KeyPlan");

                // 標記群組（各一個交易；姿態雜湊相同者在 UpdateMarkersForCameras 內自動略過）
                int drawnCount = 0;
                void Draw(FollowUpKind k, MarkerParts parts, string label)
                {
                    List<ElementId> ids = G(k);
                    if (ids.Count == 0) return;
                    // 2026-09-26 稽核：一組失敗（交易丟例外）不得中斷其他組、剖面對位與面板後續的樓層記號更新。
                    try
                    {
                        int n = UpdateMarkersForCameras(doc, ids, false, parts, out string mSummary);
                        drawnCount += n;
                        if (n > 0) redrawn.Add($"{label}×{n}");
                        if (!string.IsNullOrEmpty(mSummary)) notes.Add(mSummary);
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"相機變動後更新：「{label}」重畫失敗（略過此組）", ex);
                        CameraManagerDiagnostics.RecordCaught("FollowUpDraw", ex);
                        notes.Add($"{label}重畫失敗：{ex.Message}");
                        foreach (ElementId fid in ids) { try { ForgetDrawn(doc, doc.GetElement(fid)?.UniqueId); } catch { } }
                    }
                }
                Draw(FollowUpKind.Markers, MarkerParts.All, "平面＋剖面標記");
                Draw(FollowUpKind.SectionPitch, MarkerParts.Section | MarkerParts.AMarker, "剖面標記＋A");
                // 2026-09-26 稽核：A 標記族群實例與扇形放在眼睛高度 → 只改高度也要移 A 標記（舊版只畫剖面，A 標記停在舊高度且被記為已畫）。
                Draw(FollowUpKind.SectionOnly, MarkerParts.Section | MarkerParts.AMarker, "剖面標記＋A");
                // 剖面重新對位：平面／總覽／A 標記照常重畫，剖面標記留給對位後再畫（避免畫兩次）。
                Draw(FollowUpKind.SectionRealign, MarkerParts.Plan | MarkerParts.Overview | MarkerParts.AMarker, "平面標記");
                updated += drawnCount;
                perf.Lap("標記");
                foreach (ElementId id in G(FollowUpKind.SectionRealign))
                {
                    try
                    {
                        string r = RealignSectionForCamera(doc, id, out bool recreated);
                        notes.Add(r);
                        redrawn.Add("剖面對位×1");
                        // 2026-09-26 稽核：剖面在對位時被刪除重建 → 新剖面沒有 Key Plan 樣式與樓層記號、平面也沒隱藏其他剖面標頭
                        // → 補跑一次完整 Key Plan 更新（同一鍵 4 視圖，不並排）。
                        if (recreated && uiApp != null)
                        {
                            KeyPlanResult kr = CreateOrUpdateFourViews(uiApp, doc, id);
                            notes.Add(kr.Error == null ? "剖面已重建 → 已補套 Key Plan 樣式與樓層記號" : "剖面已重建，補套 Key Plan 樣式失敗：" + kr.Error);
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Error("相機變動後更新：剖面重新對位失敗（略過）", ex);
                        CameraManagerDiagnostics.RecordCaught("RealignSection", ex);
                        // 2026-09-26 稽核：平面標記已在上面記為「已畫」，但剖面沒對位 → 移除紀錄，下次不會被判「姿態沒變」而略過。
                        try { ForgetDrawn(doc, doc.GetElement(id)?.UniqueId); } catch { }
                    }
                }
                perf.Lap("剖面對位");
                if (unchanged > 0) notes.Add($"{unchanged} 台姿態未變，略過");
                perf.Note("重畫：" + (redrawn.Count > 0 ? string.Join("、", redrawn) : "無") + (notes.Count > 0 ? "；" + string.Join("；", notes) : ""));
            }
            return notes.Count > 0 ? string.Join("；", notes) : "相機標記已是最新。";
        }

        /// <summary>
        /// 分級一台相機的變動。same＝與上次畫標記時完全相同（整台略過）。只讀；房間查詢只在「平面有移動」時才做。
        /// </summary>
        private static FollowUpKind ClassifyChange(UIApplication uiApp, Document doc, View3D cam, CameraPoseHint hint, List<Level> levels,
            out string reason, out bool same, out bool storeyChanged)
        {
            same = false;
            storeyChanged = false;
            string key = PoseKey(doc, cam.UniqueId);
            _lastDrawn.TryGetValue(key, out PoseRecord rec);
            if (rec != null && rec.Hash == MarkerHash(cam)) { same = true; reason = "姿態與上次標記相同"; return FollowUpKind.Markers; }

            PoseRecord basis = rec?.Eye != null ? rec : null;
            string basisName = "上次標記";
            bool? shapeChanged = basis != null ? (bool?)(basis.ShapeHash != ComputeShapeHash(cam)) : null;
            if (basis == null && hint?.Eye != null)
            {
                basis = new PoseRecord { Eye = hint.Eye, YawDeg = hint.YawDeg, PitchDeg = hint.PitchDeg, StoreyId = hint.StoreyId };
                basisName = "變動前（" + (hint.Source ?? "?") + "）";
                shapeChanged = hint.ShapeChanged;
            }
            if (basis == null) { reason = "沒有前次姿態可比對"; return FollowUpKind.Markers; }

            ViewOrientation3D o = cam.GetOrientation();
            XYZ e = o.EyePosition;
            double dxy = CameraMath.FeetToMm(Math.Sqrt(Math.Pow(e.X - basis.Eye.X, 2) + Math.Pow(e.Y - basis.Eye.Y, 2)));
            double dz = CameraMath.FeetToMm(Math.Abs(e.Z - basis.Eye.Z));
            double dyaw = Math.Abs(NormalizeAngle((YawOf(o.ForwardDirection) - basis.YawDeg) * Math.PI / 180.0)) * 180.0 / Math.PI;
            double pitch = Math.Asin(Math.Max(-1.0, Math.Min(1.0, o.ForwardDirection.Z))) * 180.0 / Math.PI;
            double? dpitch = basis.PitchDeg.HasValue ? (double?)Math.Abs(pitch - basis.PitchDeg.Value) : null;
            string delta = $"相對{basisName}：平面 {dxy:F0}mm、高度 {dz:F0}mm、水平角 {dyaw:F2}°、俯仰角 {(dpitch.HasValue ? dpitch.Value.ToString("F2") + "°" : "?")}" +
                           $"{(shapeChanged == true ? "、視野形狀有變" : shapeChanged == null ? "、視野形狀未知" : "")}";

            // 樓層（室內＝眼睛所在房間的 Level；有快取）
            CameraMath.CameraGeometry geom = CameraMath.ComputeGeometry(cam, levels);
            long? storey = geom.LevelBelow != null ? (long?)geom.LevelBelow.Id.GetIdValue() : null;
            bool hasKp = uiApp != null && HasKeyPlanViews(doc, cam.UniqueId);
            if (basis.StoreyId.HasValue && storey.HasValue && basis.StoreyId != storey)
            {
                storeyChanged = true;
                reason = delta + $"；樓層 {basis.StoreyId}→{storey}";
                return hasKp ? FollowUpKind.Full : FollowUpKind.Markers;
            }
            if (dxy > 1.0)
            {
                if (!hasKp) { reason = delta; return FollowUpKind.Markers; }
                if (basis.RoomSizeMm.HasValue && basis.RoomId.HasValue)
                {
                    CameraParams cp = ComputeCameraParams(cam, levels);
                    long? room = cp?.Room?.Room != null && !cp.IsOutdoor ? (long?)cp.Room.Room.Id.GetIdValue() : null;
                    if (room != basis.RoomId) { reason = delta + $"；房間 {basis.RoomId}→{(room.HasValue ? room.ToString() : "室外/無")}"; return FollowUpKind.Full; }
                    if (dxy > basis.RoomSizeMm.Value) { reason = delta + $"；超過房間大小 {basis.RoomSizeMm.Value:F0}mm"; return FollowUpKind.Full; }
                    reason = delta + $"；同一房間（大小 {basis.RoomSizeMm.Value:F0}mm）";
                    return dyaw > 0.05 || shapeChanged != false ? FollowUpKind.Full : FollowUpKind.SectionRealign;
                }
                // 沒有房間紀錄（本 session 尚未畫過、或室外）：沿用舊門檻
                if (dxy > RealignMoveMm) { reason = delta + $"；無房間紀錄、移動 > {RealignMoveMm:F0}mm"; return FollowUpKind.Full; }
                reason = delta + "；無房間紀錄、小幅移動";
                return FollowUpKind.SectionRealign;
            }
            if (dyaw > 0.05 || shapeChanged != false) { reason = delta; return FollowUpKind.Markers; }
            if (dpitch.HasValue && dpitch.Value > 0.01) { reason = delta; return FollowUpKind.SectionPitch; }
            if (!dpitch.HasValue) { reason = delta + "（俯仰角未知，一併更新 A 標記）"; return FollowUpKind.SectionPitch; }
            if (dz > 0.05) { reason = delta; return FollowUpKind.SectionOnly; }
            reason = delta + "（其他變動，例如模型或標記型式）";
            return FollowUpKind.Markers;
        }

        /// <summary>
        /// 2026-09-26 第六輪：平面小幅移動（同一房間內）→ 只把這台相機的 Key Plan 剖面框重新對到新的眼睛位置，並重畫剖面標記。
        /// 與一鍵 4 視圖同一套剖面規格（ComputeSectionSpec＋GetOrCreateSectionView；使用者手動移過的剖面照樣保留），
        /// 但不動平面裁剪、等角框、樣式、並排。單一交易「更新相機標記」（DocumentChanged 端視為本工具自己的變更）。
        /// </summary>
        internal static string RealignSectionForCamera(Document doc, ElementId cameraId, out bool recreated)
        {
            recreated = false;
            if (!(doc?.GetElement(cameraId) is View3D camera) || camera.IsTemplate || !camera.IsPerspective) return "剖面對位：找不到相機";
            string cid = camera.UniqueId;
            ViewSection section = FindLinkedView(doc, cid, "Section") as ViewSection;
            if (section == null) return $"剖面對位：「{camera.Name}」沒有 Key Plan 剖面";
            using (CameraPerf perf = CameraPerf.Begin($"剖面重新對位「{camera.Name}」"))
            {
                List<Level> levels = CameraMath.GetStoreyLevelsSorted(doc);
                CameraParams cp = ComputeCameraParams(camera, levels);
                CameraMath.CameraGeometry geom = cp.Geom;
                if (geom.LevelBelow == null) return "剖面對位：找不到樓層";
                ViewPlan plan = FindLinkedView(doc, cid, "Plan") as ViewPlan;
                IsoXY isoXY = ComputeIsoXY(geom, cp.ForwardHoriz, cp.LeftRad, cp.RightRad, cp);
                StoreyTop storeyTop = ComputeStoreyTop(doc, geom, isoXY, levels, cp.Room, cp.IsOutdoor);
                double? cutPlaneZ = plan != null ? GetPlanCutPlaneZ(doc, plan) : null;
                XYZ planBasisX = null;
                try { planBasisX = plan?.CropBox?.Transform?.BasisX; } catch { planBasisX = null; }
                SectionSpec spec = ComputeSectionSpec(doc, geom, cp, isoXY, storeyTop, GetSectionOrientationMode(), planBasisX, cutPlaneZ, GetSectionAnchorMode(), GetCameraSectionRotate90(camera));
                perf.Lap("剖面規格");
                string sectionName = SanitizeRevitName("Key Plan-" + camera.Name + "-剖面");
                bool preserved = false;
                bool recreatedLocal = false;
                IsUpdatingMarkers = true;
                try
                {
                    using (Transaction t = TransactionHelper.Begin(doc, MarkerUpdateTransactionName))
                    {
                        t.Start();
                        try
                        {
                            ElementId oldSectionId = section.Id;
                            ViewSection sec = GetOrCreateSectionView(doc, sectionName, cid, geom, spec, false, out preserved);
                            recreatedLocal = sec != null && sec.Id != oldSectionId;
                            DrawMarkerSafely(doc, "RealignSectionMarker",
                                () => DrawSectionMarker(doc, sec, geom, cp.CenterHoriz ?? cp.ForwardHoriz, cp.FanRadiusFeet, cp.DownRad, cp.UpRad, cid));
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
                perf.Lap("交易");
                recreated = recreatedLocal;
                RecordDrawn(doc, camera, cp);
                string msg = preserved ? $"剖面對位：「{camera.Name}」剖面曾手動移過，範圍保留，只重畫標記"
                                       : $"剖面對位：「{camera.Name}」剖面已跟著相機移動";
                perf.Note(msg);
                return msg;
            }
        }

        // ══════════════════════════════════════════════════════════
        // 2026-09-26 第六輪：去抖後續更新排程（set_camera_pose 用；面板啟用時交給面板的同一個計時器）
        // ══════════════════════════════════════════════════════════

        internal const int FollowUpDebounceMs = 800;

        /// <summary>面板註冊：(相機, 變動前姿態) → 面板已啟用就排進面板的去抖計時器並回傳 true。</summary>
        /// 2026-09-26 稽核：多帶 Document——面板只接受「面板資料來源文件」的相機（否則 id 會套到另一份文件的同號元素）。
        internal static Func<Document, ElementId, CameraPoseHint, bool> PanelFollowUpSink;

        private static readonly object _followLock = new object();
        private static readonly Dictionary<ElementId, CameraPoseHint> _followPending = new Dictionary<ElementId, CameraPoseHint>();
        /// <summary>2026-09-26 稽核：待執行項目的相機 UniqueId 與來源文件鍵——計時器觸發時作用中文件可能已換，只在同一份文件、同一台相機時執行。</summary>
        private static readonly Dictionary<ElementId, string> _followPendingUid = new Dictionary<ElementId, string>();
        private static string _followDocKey;
        private static System.Threading.Timer _followTimer;

        /// <summary>
        /// 排入一台相機的後續更新（標記／剖面／Key Plan 依分級），最後一次排入後 800 ms 才執行，同一台合併（保留最早的變動前姿態）。
        /// 面板已啟用 → 交給面板（它還會在完成後更新狀態列）；否則用這裡的計時器，到時經相機管理佇列（ExternalEvent）執行。
        /// 呼叫端須在 Revit 主執行緒。
        /// </summary>
        internal static string ScheduleFollowUp(Document doc, ElementId cameraId, CameraPoseHint hint)
        {
            if (cameraId == null || doc == null) return "沒有相機";
            string docKey = StoreyDocKey(doc);
            string uid = null;
            try { uid = doc.GetElement(cameraId)?.UniqueId; } catch { uid = null; }
            try
            {
                if (PanelFollowUpSink != null && PanelFollowUpSink(doc, cameraId, hint))
                    return $"已排入面板的去抖後續更新（{FollowUpDebounceMs} ms 後，依變動分級重畫）";
            }
            catch (Exception ex) { Logger.Error("相機後續更新：交給面板失敗（改用內建排程）", ex); }
            lock (_followLock)
            {
                if (_followDocKey != null && _followDocKey != docKey && _followPending.Count > 0)
                {
                    Logger.Info($"相機後續更新（內建排程）：文件已換 → 捨棄前一份文件待執行的 {_followPending.Count} 台（下次編輯會重新判斷）");
                    _followPending.Clear();
                    _followPendingUid.Clear();
                }
                _followDocKey = docKey;
                if (!_followPending.ContainsKey(cameraId)) { _followPending[cameraId] = hint; _followPendingUid[cameraId] = uid; }
                if (_followTimer == null) _followTimer = new System.Threading.Timer(_ => OnFollowTimer(), null, System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
                _followTimer.Change(FollowUpDebounceMs, System.Threading.Timeout.Infinite);
            }
            return $"已排入去抖後續更新（{FollowUpDebounceMs} ms 後，依變動分級重畫）";
        }

        private static void OnFollowTimer()
        {
            Dictionary<ElementId, CameraPoseHint> batch;
            Dictionary<ElementId, string> uids;
            string batchDocKey;
            lock (_followLock)
            {
                if (_followPending.Count == 0) return;
                batch = new Dictionary<ElementId, CameraPoseHint>(_followPending);
                uids = new Dictionary<ElementId, string>(_followPendingUid);
                batchDocKey = _followDocKey;
                _followPending.Clear();
                _followPendingUid.Clear();
            }
            // 計時器執行緒 → 相機管理佇列（ExternalEvent，thread-safe Raise）
            CameraManagerEventQueue.Enqueue("CameraFollowUp", uiApp =>
            {
                Document doc = uiApp?.ActiveUIDocument?.Document;
                if (doc == null) return;
                if (StoreyDocKey(doc) != batchDocKey)
                {
                    Logger.Info($"相機後續更新（內建排程）：作用中文件已不是排入時的文件 → 略過 {batch.Count} 台");
                    return;
                }
                var ids = batch.Keys.Where(id => doc.GetElement(id) is View3D v &&
                                                 (!uids.TryGetValue(id, out string u) || u == null || v.UniqueId == u)).ToList();
                if (ids.Count == 0) return;
                string summary = UpdateCamerasAfterChange(uiApp, doc, ids, batch, out int n);
                Logger.Info($"相機後續更新（內建排程）：{ids.Count} 台 → {summary}");
            });
        }
    }
}
