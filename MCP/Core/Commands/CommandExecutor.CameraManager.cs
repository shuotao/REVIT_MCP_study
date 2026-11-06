using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;

// Revit 2025+ ElementId: int → long
#if REVIT2025_OR_GREATER
using IdType = System.Int64;
#else
using IdType = System.Int32;
#endif

namespace RevitMCP.Core
{
    /// <summary>
    /// 相機管理（Camera Manager）的 MCP 測試工具（2026-09-25 Wave 1）。
    ///
    /// 目的：讓 AI 能自動測試相機管理停靠面板背後的同一套服務程式碼，不必靠人工點面板。
    /// 原則：
    ///   1. 不重複邏輯——讀清單用 CameraManagerService.ReadSnapshot／BuildCameraRow，一鍵 4 視圖用
    ///      CameraKeyPlanService.CreateOrUpdateFourViews（＋CameraManagerService.OpenAndTileFourViews），
    ///      改相機用 CameraManagerService.ApplyPoseEditFast（2026-09-26 第六輪，與面板同一條編輯快速路徑），複製用 CopyCameraToLevels。
    ///   2. 走既有 MCP ExternalEvent 路徑（CommandExecutor 在 ExternalEvent.Execute 內執行），不經面板的
    ///      CameraManagerEventQueue；上述服務方法全部不含 WPF 型別，各自開自己的 Transaction。
    ///   3. 所有長度輸出為 mm（內部原點座標系），角度為度。
    /// </summary>
    public partial class CommandExecutor
    {
        private static double CmMm(double feet) => Math.Round(CameraMath.FeetToMm(feet), 1);

        private static object CmPoint(XYZ p) => p == null ? null : new { X = CmMm(p.X), Y = CmMm(p.Y), Z = CmMm(p.Z) };

        private static object CmVector(XYZ v) => v == null ? null : new { X = Math.Round(v.X, 5), Y = Math.Round(v.Y, 5), Z = Math.Round(v.Z, 5) };

        private static double? CmRound(double? v, int digits = 1) => v.HasValue ? (double?)Math.Round(v.Value, digits) : null;

        private static object CmBox(BoundingBoxXYZ box)
        {
            if (box == null) return null;
            Transform tf = box.Transform ?? Transform.Identity;
            return new
            {
                MinLocalMm = CmPoint(box.Min),
                MaxLocalMm = CmPoint(box.Max),
                SizeMm = new { X = CmMm(box.Max.X - box.Min.X), Y = CmMm(box.Max.Y - box.Min.Y), Z = CmMm(box.Max.Z - box.Min.Z) },
                Transform = new
                {
                    OriginMm = CmPoint(tf.Origin),
                    BasisX = CmVector(tf.BasisX),
                    BasisY = CmVector(tf.BasisY),
                    BasisZ = CmVector(tf.BasisZ)
                },
                // 世界座標的 Min/Max 角點（Transform 套用後；旋轉的框只代表兩個角點，不是外接框）
                MinWorldMm = CmPoint(tf.OfPoint(box.Min)),
                MaxWorldMm = CmPoint(tf.OfPoint(box.Max))
            };
        }

        /// <summary>解析透視相機：cameraId 可給相機 View3D 或平面上的相機元件；省略＝作用中視圖。</summary>
        private View3D ResolvePerspectiveCamera(Document doc, JObject parameters)
        {
            var p = new JObject();
            if (parameters?["cameraId"] != null) p["viewId"] = parameters["cameraId"];
            View3D view = ResolveCameraView(doc, p);
            if (view.IsTemplate || !view.IsPerspective)
                throw new Exception($"視圖「{view.Name}」不是透視相機（IsPerspective=false 或為樣板）。");
            return view;
        }

        private static CameraSectionOrientationMode? ParseSectionMode(JObject parameters)
        {
            string s = parameters?["sectionMode"]?.Value<string>();
            if (string.IsNullOrWhiteSpace(s)) return null;
            switch (s.Trim().ToLowerInvariant())
            {
                case "room":
                case "roomlongaxis":
                case "alongroomlongaxis":
                    return CameraSectionOrientationMode.AlongRoomLongAxis;
                case "sight":
                case "sightline":
                case "alongsightline":
                    return CameraSectionOrientationMode.AlongSightLine;
                default:
                    throw new Exception($"sectionMode「{s}」無效，請用 room 或 sight。");
            }
        }

        /// <summary>2026-09-26 第五輪：剖面位置單次覆寫（camera＝剖切面通過眼睛／center＝視野中心舊行為）。</summary>
        private static CameraSectionAnchorMode? ParseSectionAnchor(JObject parameters)
        {
            string s = parameters?["sectionAnchor"]?.Value<string>();
            if (string.IsNullOrWhiteSpace(s)) return null;
            switch (s.Trim().ToLowerInvariant())
            {
                case "camera":
                case "eye":
                    return CameraSectionAnchorMode.Camera;
                case "center":
                case "centre":
                    return CameraSectionAnchorMode.Center;
                default:
                    throw new Exception($"sectionAnchor「{s}」無效，請用 camera 或 center。");
            }
        }

        private static CameraIsoExtentMode? ParseIsoMode(JObject parameters)
        {
            string s = parameters?["isoMode"]?.Value<string>();
            if (string.IsNullOrWhiteSpace(s)) return null;
            switch (s.Trim().ToLowerInvariant())
            {
                case "full":
                case "fullwithupperbeams":
                    return CameraIsoExtentMode.FullWithUpperBeams;
                case "current":
                case "currentlevel":
                case "currentleveltoponly":
                    return CameraIsoExtentMode.CurrentLevelTopOnly;
                default:
                    throw new Exception($"isoMode「{s}」無效，請用 full 或 current。");
            }
        }

        private static CameraIsoNearWallsMode? ParseIsoNearWalls(JObject parameters)
        {
            string s = parameters?["isoNearWalls"]?.Value<string>();
            if (string.IsNullOrWhiteSpace(s)) return null;
            switch (s.Trim().ToLowerInvariant())
            {
                case "camera": return CameraIsoNearWallsMode.Camera;
                case "wall":
                case "cut": return CameraIsoNearWallsMode.Wall; // 2026-09-26 D-039：cut＝舊名，等同 wall
                case "keep": return CameraIsoNearWallsMode.Keep;
                default: throw new Exception($"isoNearWalls「{s}」無效，請用 camera、wall 或 keep。");
            }
        }

        private static CameraMarkerMode? ParseMarkerMode(JObject parameters)
        {
            string s = parameters?["markerMode"]?.Value<string>();
            if (string.IsNullOrWhiteSpace(s)) return null;
            switch (s.Trim().ToUpperInvariant().Replace("+", ""))
            {
                case "A": return CameraMarkerMode.A;
                case "B": return CameraMarkerMode.B;
                case "AB": return CameraMarkerMode.AB;
                default: throw new Exception($"markerMode「{s}」無效，請用 B、A 或 AB。");
            }
        }

        // ══════════════════════════════════════════════════════════
        // list_cameras（唯讀）
        // ══════════════════════════════════════════════════════════

        private object ListCameras(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;
            string nameContains = parameters?["nameContains"]?.Value<string>();

            CameraManagerSnapshot snap = CameraManagerService.ReadSnapshot(doc);
            if (!string.IsNullOrEmpty(snap.ErrorMessage)) throw new Exception(snap.ErrorMessage);

            List<Level> storeys = CameraMath.GetStoreyLevelsSorted(doc);
            CameraSectionOrientationMode configuredMode = CameraKeyPlanService.GetConfiguredSectionOrientationMode();

            var cameras = new List<object>();
            foreach (CameraRow row in snap.Cameras)
            {
                if (!string.IsNullOrEmpty(nameContains) &&
                    (row.Name ?? "").IndexOf(nameContains, StringComparison.OrdinalIgnoreCase) < 0) continue;

                View3D view = doc.GetElement(row.ViewId) as View3D;
                if (view == null) continue;
                CameraMath.CameraGeometry geom = CameraMath.ComputeGeometry(view, storeys);

                CameraMath.PerspectiveFov fov = null;
                try { fov = CameraMath.ComputePerspectiveFov(view); }
                catch (Exception ex) { Logger.Error("list_cameras：視野角計算失敗", ex); }

                long? roomId = null; string roomName = null, roomLog = null;
                try { CameraKeyPlanService.DescribeEyeRoom(view, geom, out roomId, out roomName, out roomLog); }
                catch (Exception ex) { roomLog = "failed: " + ex.Message; }

                // 底圖挑選：與 ③ 位置圖選取相機時同一個呼叫（單台相機＝SourcePlanHint.From(row)）。
                object baseMap = null;
                if (row.LevelId != null)
                {
                    var eyes = new List<double[]> { new[] { row.EyeXFeet, row.EyeYFeet, row.EyeZFeet } };
                    PlanViewOption chosen = BaseMapPlanChooser.Choose(snap.PlanViews, row.LevelId, eyes, snap.Levels,
                        out string why, out int step, SourcePlanHint.From(row));
                    baseMap = new
                    {
                        PlanViewId = chosen?.ViewId?.GetIdValue(),
                        PlanName = chosen?.PlanName,
                        LevelName = chosen?.LevelName,
                        Step = step,
                        Reason = why
                    };
                }

                View plan = CameraKeyPlanService.FindLinkedKeyPlanView(doc, row.UniqueId, "Plan");
                View section = CameraKeyPlanService.FindLinkedKeyPlanView(doc, row.UniqueId, "Section");
                View iso = CameraKeyPlanService.FindLinkedKeyPlanView(doc, row.UniqueId, "Iso");
                bool? sectionMoved = section is ViewSection vs ? (bool?)CameraKeyPlanService.IsSectionManuallyMoved(vs) : null;

                bool outdoor = row.IsOutdoor == true;
                cameras.Add(new
                {
                    Id = row.ViewId.GetIdValue(),
                    row.Name,
                    Level = row.LevelName,
                    LevelId = row.LevelId?.GetIdValue(),
                    // 2026-09-25：room＝室內相機取房間的 Level；elevation＝眼睛正下方的建築樓層
                    LevelSource = geom.StoreySource,
                    LevelByElevation = geom.StoreyByElevation?.Name,
                    // 2026-09-26 第四輪：樓層挑選說明（多層房間：roomSpan；室外站立面：surface）與站立面射線結果
                    StoreyLog = geom.StoreyLog,
                    StandingSurface = geom.StandingSurfaceResolved ? new
                    {
                        ZMm = geom.StandingSurfaceZ.HasValue ? (double?)CmMm(geom.StandingSurfaceZ.Value) : null,
                        Log = geom.StandingSurfaceLog
                    } : null,
                    HeightAboveLevelCm = row.HeightAboveLevelCm,
                    HeightAboveLevelMm = geom.AboveLevelBelowMm.HasValue ? (double?)Math.Round(geom.AboveLevelBelowMm.Value, 1) : null,
                    HeightWarning = row.HeightWarning,
                    EyeMm = CmPoint(geom.Eye),
                    YawDeg = Math.Round(row.HorizontalAngleDeg, 2),
                    PitchDeg = Math.Round(row.PitchDeg, 2),
                    Fov = fov == null ? null : new
                    {
                        LeftDeg = CmRound(fov.LeftDeg, 2),
                        RightDeg = CmRound(fov.RightDeg, 2),
                        DownDeg = CmRound(fov.DownDeg, 2),
                        UpDeg = CmRound(fov.UpDeg, 2),
                        HorizontalDeg = CmRound(fov.HFovDeg, 2),
                        VerticalDeg = CmRound(fov.VFovDeg, 2),
                        fov.Source
                    },
                    FarClipCm = row.FarClipCm,
                    FarClipEditable = row.FarClipEditable,
                    IndoorOutdoor = row.IndoorOutdoorText,
                    Room = new { Id = roomId, Name = roomName, Log = roomLog },
                    OutdoorFanRadiusMm = outdoor ? (double?)CmMm(row.OutdoorFanRadiusFeet) : null,
                    SourcePlan = row.SourcePlanId == null ? null : new
                    {
                        Id = row.SourcePlanId.GetIdValue(),
                        Name = row.SourcePlanName,
                        Cropped = row.SourcePlanCropped,
                        PrimaryId = row.SourcePlanPrimaryId?.GetIdValue()
                    },
                    BaseMap = baseMap,
                    KeyPlan = new
                    {
                        PlanViewId = plan?.Id.GetIdValue(),
                        // 2026-09-26 第五輪：Key Plan 平面的 GenLevel 與相機目前樓層不同＝需重跑一鍵 4 視圖（會自動重建）
                        PlanLevel = (plan as ViewPlan)?.GenLevel?.Name,
                        PlanLevelMismatch = plan is ViewPlan ? (bool?)CameraKeyPlanService.IsPlanLevelMismatch(plan, geom.LevelBelow?.Id) : null,
                        SectionViewId = section?.Id.GetIdValue(),
                        IsoViewId = iso?.Id.GetIdValue(),
                        SectionManuallyMoved = sectionMoved
                    },
                    SectionOrientation = new
                    {
                        Configured = configuredMode.ToString(),
                        // 室外一律沿視線；室內找不到房間時 ComputeSectionSpec 也會退回沿視線（以 run_camera_keyplan 的 SectionModeUsed 為準）
                        ExpectedForThisCamera = outdoor ? CameraSectionOrientationMode.AlongSightLine.ToString() : configuredMode.ToString(),
                        // 2026-09-26 第五輪：剖面位置（Camera＝剖切面通過眼睛／Center＝視野中心）
                        AnchorConfigured = CameraKeyPlanService.GetConfiguredSectionAnchorMode().ToString(),
                        // 2026-09-27：單一相機「剖面轉 90°」開關（持久化於相機元素，見 run_camera_keyplan 的 sectionRotate90 參數）
                        Rotate90 = CameraKeyPlanService.GetCameraSectionRotate90(view)
                    }
                });
            }

            return new
            {
                Success = true,
                snap.DocumentTitle,
                Count = cameras.Count,
                Note = "長度為內部原點座標系 mm；樓層（2026-09-26）：室內＝{房間 Level}∪{房間垂直範圍內有樓層平面的 Level} 中高程 ≤ 眼睛−300mm 的最高者（LevelSource=room／roomSpan）；室外＝往下射線打到的站立面 +300mm 以下、有平面的最高 Level（LevelSource=surface），沒打到＝眼睛下方建築樓層（elevation）；離樓板高度以該樓層計算；水平角 0°=+Y、順時針為正。",
                StoreyLevels = snap.Levels.Select(l => new { Id = l.LevelId.GetIdValue(), l.Name, ElevationMm = CmMm(l.ProjectElevationFeet) }).ToList(),
                Cameras = cameras
            };
        }

        // ══════════════════════════════════════════════════════════
        // run_camera_keyplan（寫入）
        // ══════════════════════════════════════════════════════════

        private object RunCameraKeyPlan(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;
            View3D camera = ResolvePerspectiveCamera(doc, parameters);
            bool tile = parameters?["tile"]?.Value<bool?>() ?? false;
            bool resetSection = parameters?["resetSection"]?.Value<bool?>() ?? false;
            var options = new CameraKeyPlanService.KeyPlanRunOptions
            {
                SectionMode = ParseSectionMode(parameters),
                IsoMode = ParseIsoMode(parameters),
                MarkerMode = ParseMarkerMode(parameters),
                IsoNearWalls = ParseIsoNearWalls(parameters),
                SectionAnchor = ParseSectionAnchor(parameters)
            };

            // 2026-09-27：sectionRotate90 有給值＝設定並持久化這台相機的「剖面轉 90°」（存在相機元素本身
            // 的 ES，不是單次覆寫），並強制這次重設剖面位置，讓效果立即可見（同面板「重設剖面位置」）。
            bool? sectionRotate90Param = parameters?["sectionRotate90"]?.Value<bool?>();
            if (sectionRotate90Param.HasValue)
            {
                string rotateErr = CameraKeyPlanService.ApplySectionRotate90(doc, camera, sectionRotate90Param.Value);
                if (rotateErr != null) throw new Exception("設定「剖面轉90°」失敗：" + rotateErr);
                resetSection = true;
            }

            CameraKeyPlanService.KeyPlanResult result =
                CameraKeyPlanService.CreateOrUpdateFourViews(_uiApp, doc, camera.Id, resetSection, options);
            if (result.Error != null) throw new Exception(result.Error);

            string tileError = null;
            if (tile) tileError = CameraManagerService.OpenAndTileFourViews(_uiApp, doc, camera.Id, result);

            ViewPlan plan = doc.GetElement(result.PlanViewId) as ViewPlan;
            ViewSection section = doc.GetElement(result.SectionViewId) as ViewSection;
            View3D iso = doc.GetElement(result.IsoViewId) as View3D;
            string cid = camera.UniqueId;
            CameraKeyPlanService.KeyPlanDecisions d = result.Decisions;

            object Describe(View v, Func<object> extents) => v == null ? null : new
            {
                Id = v.Id.GetIdValue(),
                v.Name,
                Scale = SafeScale(v),
                Extents = extents(),
                TaggedElements = CameraKeyPlanService.CountTaggedElementsByRole(doc, v, cid)
            };

            double? isoYaw = null;
            if (iso != null)
            {
                XYZ f = iso.GetOrientation().ForwardDirection;
                isoYaw = Math.Round(CameraMath.ComputeHorizontalAngleDeg(f), 2);
            }

            return new
            {
                Success = true,
                Camera = new { Id = camera.Id.GetIdValue(), camera.Name },
                SectionPreserved = result.SectionPreserved,
                // 2026-09-25 Wave 2：一鍵 4 視圖後自動更新的樓層總覽，與（⋯ 相機標記含 A 時）A 型標記同步摘要
                OverviewViewId = result.OverviewViewId?.GetIdValue(),
                AMarker = result.AMarkerLog,
                MarkerModeUsed = result.MarkerModeUsed,
                Warnings = result.Warnings,
                Tiled = tile && tileError == null,
                TileError = tileError,
                Plan = Describe(plan, () => plan == null ? null : new { plan.CropBoxActive, CropBox = CmBox(plan.CropBox), ScopeBox = ScopeBoxOf(doc, plan) }),
                Section = Describe(section, () => section == null ? null : new { CropBox = CmBox(section.CropBox) }),
                Iso = Describe(iso, () => iso == null ? null : new
                {
                    iso.IsSectionBoxActive,
                    SectionBox = CmBox(iso.GetSectionBox()),
                    ForwardDirection = CmVector(iso.GetOrientation().ForwardDirection),
                    CornerYawDeg = isoYaw
                }),
                Decisions = new
                {
                    IndoorOutdoor = d.IsOutdoor ? "室外" : "室內",
                    d.ClassLog,
                    Room = new { Id = d.RoomId, Name = d.RoomName, Log = d.RoomLog },
                    FarClipMm = CmMm(d.FarClipFeet),
                    FanRadiusMm = CmMm(d.FanRadiusFeet),
                    d.FanLog,
                    CenterYawDeg = Math.Round(d.CenterYawDeg, 2),
                    FovEdgesDeg = new { Left = Math.Round(d.LeftDeg, 2), Right = Math.Round(d.RightDeg, 2), Down = Math.Round(d.DownDeg, 2), Up = Math.Round(d.UpDeg, 2) },
                    HFovDeg = CmRound(d.HFovDeg, 2),
                    VFovDeg = CmRound(d.VFovDeg, 2),
                    StoreyTop = new
                    {
                        RoomTopMm = d.RoomTopZ.HasValue ? (double?)CmMm(d.RoomTopZ.Value) : null,
                        UpperSlabBottomMm = double.IsInfinity(d.StoreyTopSlabBottomZ) || double.IsNaN(d.StoreyTopSlabBottomZ) ? (double?)null : CmMm(d.StoreyTopSlabBottomZ),
                        UpperBeamBottomMm = d.StoreyTopBeamBottomZ.HasValue ? (double?)CmMm(d.StoreyTopBeamBottomZ.Value) : null,
                        Outdoor = d.StoreyTopOutdoor,
                        Log = d.StoreyTopLog
                    },
                    IsoXYLog = d.IsoXYLog,
                    SectionModeRequested = d.SectionModeRequested.ToString(),
                    SectionModeUsed = d.SectionModeUsed.ToString(),
                    SectionAnchor = d.SectionAnchor.ToString(),
                    SectionRotate90 = d.SectionRotate90,
                    d.SectionSpecLog,
                    d.SectionApplyLog,
                    SectionExtentDeviationMm = CmRound(d.SectionExtentDeviationMm, 1),
                    IsoExtentMode = d.IsoExtentMode.ToString(),
                    IsoNearWalls = d.IsoNearWalls.ToString(),
                    d.IsoNearWallsLog,
                    KeyPlanStyle = d.FadedStyle ? "淡化背景" : "原始"
                }
            };
        }

        /// <summary>視圖的 Scope Box（VIEWER_VOLUME_OF_INTEREST_CROP）；沒有＝null。</summary>
        private static object ScopeBoxOf(Document doc, View v)
        {
            try
            {
                Parameter p = v?.get_Parameter(BuiltInParameter.VIEWER_VOLUME_OF_INTEREST_CROP);
                if (p == null || p.StorageType != StorageType.ElementId) return null;
                ElementId id = p.AsElementId();
                if (id == null || id == ElementId.InvalidElementId) return null;
                return new { Id = id.GetIdValue(), Name = doc.GetElement(id)?.Name };
            }
            catch { return null; }
        }

        private static int? SafeScale(View v)
        {
            try { return v.Scale; } catch { return null; }
        }

        // ══════════════════════════════════════════════════════════
        // set_camera_pose（寫入）
        // ══════════════════════════════════════════════════════════

        private object SetCameraPose(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;
            View3D camera = ResolvePerspectiveCamera(doc, parameters);
            List<Level> storeys = CameraMath.GetStoreyLevelsSorted(doc);

            CameraRow before = CameraManagerService.BuildCameraRow(camera, storeys);
            if (before.LevelId == null) throw new Exception("這台相機找不到下方建築樓層，無法編輯（與面板相同限制）。");

            double? h = parameters?["heightAboveLevelCm"]?.Value<double?>();
            double? yaw = parameters?["yawDeg"]?.Value<double?>();
            double? pitch = parameters?["pitchDeg"]?.Value<double?>();
            double? x = parameters?["planXcm"]?.Value<double?>();
            double? y = parameters?["planYcm"]?.Value<double?>();
            double? far = parameters?["farClipCm"]?.Value<double?>();
            if (!h.HasValue && !yaw.HasValue && !pitch.HasValue && !x.HasValue && !y.HasValue && !far.HasValue)
                throw new Exception("至少要給一個欄位：heightAboveLevelCm、yawDeg、pitchDeg、planXcm、planYcm、farClipCm。");
            if (far.HasValue && !before.FarClipEditable)
                throw new Exception("這台相機的遠裁剪參數不可寫入（面板該欄同樣唯讀）。");

            // 2026-09-26 第六輪：與面板清單編輯同一條「編輯快速路徑」——一個小交易（自動存 A案＋一次 SetOrientation＋遠裁剪）
            // ＋立刻重繪透視圖；沒給的欄位沿用相機目前實際值。標記／剖面／Key Plan 改為 800 ms 去抖的後續更新（依變動分級），
            // 不在這個回應裡同步跑（回應的 Timing 只量快速路徑）。
            var edit = new CameraPoseEdit
            {
                ViewId = camera.Id,
                LevelId = before.LevelId,
                HeightAboveLevelCm = h,
                YawDeg = yaw,
                PitchDeg = pitch,
                EyeXFeet = x.HasValue ? (double?)CameraMath.MmToFeet(x.Value * 10.0) : null,
                EyeYFeet = y.HasValue ? (double?)CameraMath.MmToFeet(y.Value * 10.0) : null,
                FarClipCm = far
            };
            CameraPoseEditResult fast = CameraManagerService.ApplyPoseEditFast(_uiApp, doc, edit, "MCP set_camera_pose", markInPlace: false);
            if (fast.Error != null) throw new Exception(fast.Error);

            var hint = new CameraKeyPlanService.CameraPoseHint
            {
                Eye = new XYZ(before.EyeXFeet, before.EyeYFeet, before.EyeZFeet),
                YawDeg = before.HorizontalAngleDeg,
                PitchDeg = before.PitchDeg,
                StoreyId = before.LevelId != null ? (long?)before.LevelId.GetIdValue() : null,
                ShapeChanged = far.HasValue,
                Source = "set_camera_pose"
            };
            string update;
            try
            {
                update = fast.Changed ? CameraKeyPlanService.ScheduleFollowUp(doc, camera.Id, hint) : "數值與相機目前相同，未變更（不排後續更新）";
            }
            catch (Exception exUpd)
            {
                Logger.Error("set_camera_pose：排入後續更新失敗（相機已修改）", exUpd);
                update = "排入後續更新失敗：" + exUpd.Message;
            }
            string refreshed = fast.RefreshHow;

            CameraRow after = CameraManagerService.BuildCameraRow(camera, storeys);
            object Pose(CameraRow r) => new
            {
                Level = r.LevelName,
                r.HeightAboveLevelCm,
                PlanXcm = Math.Round(r.EyeXCm, 1),
                PlanYcm = Math.Round(r.EyeYCm, 1),
                YawDeg = Math.Round(r.HorizontalAngleDeg, 2),
                PitchDeg = Math.Round(r.PitchDeg, 2),
                r.FarClipCm
            };

            return new
            {
                Success = true,
                Camera = new { Id = camera.Id.GetIdValue(), camera.Name },
                Note = "planXcm/planYcm 為內部原點座標系（與面板「平面 X/Y cm」欄相同）。2026-09-26 第六輪起：編輯快速路徑（一個小交易＋立刻重繪透視圖，Timing 為其耗時）；" +
                       "標記／剖面／Key Plan 於最後一次編輯 800 ms 後依變動分級更新（高度＝只剖面標記、俯仰角＝剖面＋A 標記、水平角／視野＝平面＋剖面標記、同房間移動＝剖面對位、換樓層／房間＝完整 Key Plan），結果見 RevitMCP log 的「⏱ 相機變動後續更新」。第一次修改前若沒有任何視角版本，自動存「A案」。",
                Timing = new { transactionMs = fast.TransactionMs, refreshMs = fast.RefreshMs, totalMs = fast.TotalMs },
                Changed = fast.Changed,
                SavedOriginalVersion = fast.SavedOriginalVersion,
                ViewRefresh = refreshed,
                CameraUpdate = update,
                Before = Pose(before),
                After = Pose(after)
            };
        }

        // ══════════════════════════════════════════════════════════
        // copy_camera_to_level（寫入）
        // ══════════════════════════════════════════════════════════

        private object CopyCameraToLevel(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;
            View3D camera = ResolvePerspectiveCamera(doc, parameters);

            Level target = null;
            IdType? levelId = parameters?["levelId"]?.Value<IdType?>();
            string levelName = parameters?["levelName"]?.Value<string>();
            if (levelId.HasValue)
            {
                target = doc.GetElement(new ElementId(levelId.Value)) as Level;
                if (target == null) throw new Exception($"levelId {levelId.Value} 不是樓層。");
            }
            else if (!string.IsNullOrWhiteSpace(levelName))
            {
                List<Level> all = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().ToList();
                List<Level> matches = all.Where(l => string.Equals(l.Name, levelName.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
                if (matches.Count != 1)
                {
                    string storeys = string.Join("、", CameraMath.GetStoreyLevelsSorted(doc).Select(l => l.Name));
                    throw new Exception(matches.Count == 0
                        ? $"找不到名稱為「{levelName}」的樓層。建築樓層：{storeys}"
                        : $"名稱「{levelName}」對應到 {matches.Count} 個樓層，請改用 levelId。");
                }
                target = matches[0];
            }
            else throw new Exception("請給 levelId 或 levelName。");

            var created = new List<(ElementId id, string name)>();
            var (count, err) = CameraManagerService.CopyCameraToLevels(doc, camera.Id, new List<ElementId> { target.Id }, created);
            if (err != null) throw new Exception(err);
            if (count == 0 || created.Count == 0) throw new Exception("沒有建立任何相機。");

            return new
            {
                Success = true,
                Source = new { Id = camera.Id.GetIdValue(), camera.Name },
                TargetLevel = new { Id = target.Id.GetIdValue(), target.Name, IsBuildingStorey = CameraMath.IsBuildingStorey(target) },
                NewCamera = new { Id = created[0].id.GetIdValue(), Name = created[0].name },
                Note = "原相機與其 Key Plan 視圖不變；新相機沒有 Key Plan 視圖（需要時跑 run_camera_keyplan）。"
            };
        }

        // ══════════════════════════════════════════════════════════
        // run_camera_overview（寫入，2026-09-25 Wave 2）
        // ══════════════════════════════════════════════════════════

        private object RunCameraOverview(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;
            bool all = parameters?["all"]?.Value<bool?>() ?? false;
            List<ElementId> levelIds = null;
            if (!all)
            {
                IdType? levelId = parameters?["levelId"]?.Value<IdType?>();
                string levelName = parameters?["levelName"]?.Value<string>();
                Level target;
                if (levelId.HasValue)
                {
                    target = doc.GetElement(new ElementId(levelId.Value)) as Level;
                    if (target == null) throw new Exception($"levelId {levelId.Value} 不是樓層。");
                }
                else if (!string.IsNullOrWhiteSpace(levelName))
                {
                    List<Level> matches = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
                        .Where(l => string.Equals(l.Name, levelName.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
                    if (matches.Count != 1)
                    {
                        string storeys = string.Join("、", CameraMath.GetStoreyLevelsSorted(doc).Select(l => l.Name));
                        throw new Exception(matches.Count == 0
                            ? $"找不到名稱為「{levelName}」的樓層。建築樓層：{storeys}"
                            : $"名稱「{levelName}」對應到 {matches.Count} 個樓層，請改用 levelId。");
                    }
                    target = matches[0];
                }
                else throw new Exception("請給 levelId 或 levelName，或設 all=true。");
                levelIds = new List<ElementId> { target.Id };
            }

            List<CameraKeyPlanService.OverviewResult> results = CameraKeyPlanService.CreateOrUpdateOverviews(doc, levelIds);
            if (results.Count == 0) throw new Exception("沒有任何建築樓層有相機，未建立總覽。");

            return new
            {
                Success = results.Any(r => r.Error == null && r.ViewId != null),
                Note = "範圍為內部原點座標系 mm（模型座標外接框）；總覽一律畫 B 型標記＋名稱，與 ⋯ 相機標記設定無關。",
                Overviews = results.Select(r => new
                {
                    Level = r.LevelName,
                    LevelId = r.LevelId?.GetIdValue(),
                    ViewId = r.ViewId?.GetIdValue(),
                    r.ViewName,
                    r.Created,
                    r.CameraCount,
                    Cameras = r.CameraNames,
                    BasePlan = r.BasePlanName,
                    BasePlanReason = r.BasePlanReason,
                    r.ExtentsMode,
                    // 2026-09-25：ExtentsMm＝讀回視圖裁剪框的實際值；IntendedMm＝程式計算的預期值（兩者差 > 1 % 會出現在 Warnings）
                    ExtentsMm = r.ViewId == null ? null : new
                    {
                        MinX = CmMm(r.MinX), MinY = CmMm(r.MinY), MaxX = CmMm(r.MaxX), MaxY = CmMm(r.MaxY),
                        Width = CmMm(r.MaxX - r.MinX), Height = CmMm(r.MaxY - r.MinY)
                    },
                    IntendedMm = r.ViewId == null ? null : new
                    {
                        Width = CmMm(r.IntendedMaxX - r.IntendedMinX), Height = CmMm(r.IntendedMaxY - r.IntendedMinY)
                    },
                    ScopeBox = r.ViewId == null ? null : ScopeBoxOf(doc, doc.GetElement(r.ViewId) as View),
                    r.Warnings,
                    r.Scale,
                    TaggedElements = r.ViewId == null ? null : CountAllTaggedInView(doc, r.ViewId),
                    r.Error
                }).ToList()
            };
        }

        private static Dictionary<string, int> CountAllTaggedInView(Document doc, ElementId viewId)
        {
            View v = doc.GetElement(viewId) as View;
            var total = new Dictionary<string, int>(StringComparer.Ordinal);
            if (v == null) return total;
            foreach (View3D cam in new FilteredElementCollector(doc).OfClass(typeof(View3D)).Cast<View3D>().Where(c => !c.IsTemplate && c.IsPerspective && !CameraKeyPlanService.IsSheetPerspectiveCopy(c)))
                foreach (var kv in CameraKeyPlanService.CountTaggedElementsByRole(doc, v, cam.UniqueId))
                    total[kv.Key] = total.TryGetValue(kv.Key, out int n) ? n + kv.Value : kv.Value;
            return total;
        }

        // ══════════════════════════════════════════════════════════
        // create_camera_sheet（寫入，2026-09-25 Wave 3）
        // ══════════════════════════════════════════════════════════

        private object CreateCameraSheet(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;
            View3D camera = ResolvePerspectiveCamera(doc, parameters);
            long? tb = parameters?["titleBlockTypeId"]?.Value<long?>();

            CameraKeyPlanService.SheetResult r = CameraKeyPlanService.CreateOrUpdateCameraSheet(_uiApp, doc, camera.Id, tb);
            if (r.Error != null) throw new Exception(r.Error);

            object Mm2(double x, double y) => new { X = CmMm(x), Y = CmMm(y) };
            bool hasTb = r.TitleBlockTypeId != null && r.TitleBlockTypeId != ElementId.InvalidElementId;
            return new
            {
                Success = true,
                Camera = new { Id = camera.Id.GetIdValue(), camera.Name },
                Sheet = new { Id = r.SheetId?.GetIdValue(), Number = r.SheetNumber, Name = r.SheetName, r.Created },
                r.RanFourViews,
                // 2026-09-25：透視圖放的是原相機或出圖用透視複本
                Perspective = new { Placed = r.PerspectivePlaced, ViewId = r.PerspectiveViewId?.GetIdValue(), Note = r.PerspectiveNote },
                TitleBlock = new { TypeId = hasTb ? (long?)r.TitleBlockTypeId.GetIdValue() : null, Name = r.TitleBlockName, Source = r.TitleBlockSource },
                DrawArea = new { MinMm = Mm2(r.DrawMinX, r.DrawMinY), MaxMm = Mm2(r.DrawMaxX, r.DrawMaxY), Source = r.DrawAreaSource },
                r.ViewportTypeLog,
                Viewports = r.Viewports.Select(v => new
                {
                    v.Role,
                    ViewId = v.ViewId?.GetIdValue(),
                    v.ViewName,
                    ViewportId = v.ViewportId?.GetIdValue(),
                    v.Status,
                    v.Note,
                    CenterMm = v.ViewportId == null ? null : Mm2(v.CenterXFeet, v.CenterYFeet),
                    SizeMm = v.ViewportId == null ? null : new { W = CmMm(v.WidthFeet), H = CmMm(v.HeightFeet) },
                    CellMm = new { Min = Mm2(v.CellMinX, v.CellMinY), Max = Mm2(v.CellMaxX, v.CellMaxY) },
                    v.Overflow,
                    ViewportType = v.ViewportTypeName
                }).ToList(),
                r.Warnings,
                Note = "座標為圖紙座標 mm。可繪區扣除圖框標題欄；Key Plan 平面／剖面／等角自動挑放得進格子的最細標準比例，出圖用透視複本等比放大裁剪框（FOV 不變）；已在本相機圖紙上的視埠重新排版；透視圖已在其他圖紙上時改放出圖用透視複本；其他視圖在別張圖紙上則略過。"
            };
        }

        // ══════════════════════════════════════════════════════════
        // camera_version（list 唯讀；save／restore／delete 寫入，2026-09-25 Wave 3）
        // ══════════════════════════════════════════════════════════

        private object CameraVersion(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;
            View3D camera = ResolvePerspectiveCamera(doc, parameters);
            string action = (parameters?["action"]?.Value<string>() ?? "list").Trim().ToLowerInvariant();
            string name = parameters?["name"]?.Value<string>();
            var warnings = new List<string>();
            string message = null;

            switch (action)
            {
                case "list":
                    break;
                case "save":
                {
                    if (string.IsNullOrWhiteSpace(name)) throw new Exception("action=save 需要 name。");
                    string err = CameraKeyPlanService.SaveVersion(doc, camera.Id, name, out bool replaced);
                    if (err != null) throw new Exception(err);
                    message = replaced ? $"已覆蓋版本「{name.Trim()}」。" : $"已儲存版本「{name.Trim()}」。";
                    break;
                }
                case "restore":
                {
                    if (string.IsNullOrWhiteSpace(name)) throw new Exception("action=restore 需要 name。");
                    string err = CameraKeyPlanService.RestoreVersion(doc, camera.Id, name, warnings);
                    if (err != null) throw new Exception(err);
                    message = $"已還原版本「{name.Trim()}」（交易「{CameraKeyPlanService.VersionRestoreTransactionPrefix}{name.Trim()}」）。面板已載入時，相機標記會經去抖路徑自動更新。";
                    break;
                }
                case "delete":
                {
                    if (string.IsNullOrWhiteSpace(name)) throw new Exception("action=delete 需要 name。");
                    string err = CameraKeyPlanService.DeleteVersion(doc, camera.Id, name);
                    if (err != null) throw new Exception(err);
                    message = $"已刪除版本「{name.Trim()}」（只移除儲存的版本資料，不刪除任何模型元素）。";
                    break;
                }
                default:
                    throw new Exception($"action「{action}」無效，請用 list／save／restore／delete。");
            }

            List<Level> storeys = CameraMath.GetStoreyLevelsSorted(doc);
            CameraRow now = CameraManagerService.BuildCameraRow(camera, storeys);
            // 2026-09-26 第四輪：版本新到舊；標出目前視角符合的版本（眼睛 1 mm、方向 0.05° 內）。
            List<CameraKeyPlanService.CameraViewVersion> versions = CameraKeyPlanService.ReadVersionsNewestFirst(camera);
            string currentName = CameraKeyPlanService.FindCurrentVersionName(camera, versions);
            return new
            {
                Success = true,
                Action = action,
                CurrentMatchesVersion = currentName,
                NextSuggestedName = CameraKeyPlanService.SuggestNextVersionName(versions.Select(v => v.Name)),
                Message = message,
                Warnings = warnings,
                Camera = new
                {
                    Id = camera.Id.GetIdValue(),
                    camera.Name,
                    Current = new
                    {
                        EyeMm = CmPoint(camera.GetOrientation().EyePosition),
                        YawDeg = Math.Round(now.HorizontalAngleDeg, 2),
                        PitchDeg = Math.Round(now.PitchDeg, 2),
                        now.HeightAboveLevelCm,
                        now.FarClipCm
                    }
                },
                Versions = versions.Select(v => new
                {
                    v.Name,
                    v.SavedAt,
                    MatchesCurrent = currentName != null && v.Name == currentName,
                    EyeMm = CmPoint(v.Eye),
                    YawDeg = Math.Round(CameraMath.ComputeHorizontalAngleDeg(v.Forward), 2),
                    PitchDeg = Math.Round(Math.Asin(Math.Max(-1.0, Math.Min(1.0, v.Forward.Normalize().Z))) * 180.0 / Math.PI, 2),
                    Forward = CmVector(v.Forward),
                    Up = CmVector(v.Up),
                    CropBoxSizeMm = v.CropMin == null || v.CropMax == null ? null : new { X = CmMm(v.CropMax.X - v.CropMin.X), Y = CmMm(v.CropMax.Y - v.CropMin.Y), Z = CmMm(v.CropMax.Z - v.CropMin.Z) },
                    v.CropActive,
                    FarClipMm = v.FarClipFeet.HasValue ? (double?)CmMm(v.FarClipFeet.Value) : null,
                    v.FarClipActive
                }).ToList()
            };
        }

        // ══════════════════════════════════════════════════════════
        // get_view_extents（唯讀，通用）
        // ══════════════════════════════════════════════════════════

        private object GetViewExtents(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;
            IdType? requested = parameters?["viewId"]?.Value<IdType?>();
            View view = requested.HasValue
                ? doc.GetElement(new ElementId(requested.Value)) as View
                : _uiApp.ActiveUIDocument.ActiveView;
            if (view == null) throw new Exception(requested.HasValue ? $"元素 {requested.Value} 不是視圖。" : "沒有作用中視圖。");

            string ParamText(BuiltInParameter bip)
            {
                try
                {
                    Parameter p = view.get_Parameter(bip);
                    if (p == null) return null;
                    return p.AsValueString() ?? (p.StorageType == StorageType.Integer ? p.AsInteger().ToString() : null);
                }
                catch { return null; }
            }

            // 裁剪框
            object crop = null;
            try
            {
                if (view.CanBePrinted || view is View3D || view is ViewPlan || view is ViewSection)
                {
                    crop = new
                    {
                        Active = view.CropBoxActive,
                        Visible = view.CropBoxVisible,
                        Box = CmBox(view.CropBox)
                    };
                }
            }
            catch (Exception ex) { crop = new { Error = ex.Message }; }

            // 註解裁剪
            object annotationCrop = null;
            try
            {
                ViewCropRegionShapeManager sm = view.GetCropRegionShapeManager();
                if (sm != null && sm.CanHaveAnnotationCrop)
                {
                    annotationCrop = new
                    {
                        Active = ParamText(BuiltInParameter.VIEWER_ANNOTATION_CROP_ACTIVE),
                        OffsetsMm = new
                        {
                            Top = CmMm(sm.TopAnnotationCropOffset),
                            Bottom = CmMm(sm.BottomAnnotationCropOffset),
                            Left = CmMm(sm.LeftAnnotationCropOffset),
                            Right = CmMm(sm.RightAnnotationCropOffset)
                        },
                        ShapeEdited = sm.ShapeSet
                    };
                }
            }
            catch (Exception ex) { annotationCrop = new { Error = ex.Message }; }

            // 3D：剖面框／方向／遠裁剪
            object view3d = null;
            if (view is View3D v3)
            {
                object farClip = null;
                if (v3.IsPerspective)
                {
                    Parameter fp = CameraMath.FindFarClipOffsetParameter(v3);
                    farClip = fp == null ? null : new { Parameter = fp.Definition?.Name, Mm = CmMm(fp.AsDouble()), fp.IsReadOnly };
                }
                ViewOrientation3D o = v3.GetOrientation();
                view3d = new
                {
                    v3.IsPerspective,
                    v3.IsSectionBoxActive,
                    SectionBox = v3.IsSectionBoxActive ? CmBox(v3.GetSectionBox()) : null,
                    EyeMm = CmPoint(o.EyePosition),
                    Forward = CmVector(o.ForwardDirection),
                    Up = CmVector(o.UpDirection),
                    PerspectiveFarClip = farClip
                };
            }

            // 遠裁剪（剖面／立面等）
            object farClipping = new
            {
                Mode = ParamText(BuiltInParameter.VIEWER_BOUND_FAR_CLIPPING),
                OffsetFar = ParamText(BuiltInParameter.VIEWER_BOUND_OFFSET_FAR)
            };

            // 視圖樣板
            object template = null;
            try
            {
                if (view.ViewTemplateId != null && view.ViewTemplateId != ElementId.InvalidElementId)
                {
                    Element t = doc.GetElement(view.ViewTemplateId);
                    template = new { Id = view.ViewTemplateId.GetIdValue(), Name = t?.Name };
                }
            }
            catch { }

            // 隱藏類別（主類別＋子類別）
            var hidden = new List<string>();
            try
            {
                foreach (Category cat in doc.Settings.Categories)
                {
                    if (cat == null) continue;
                    try
                    {
                        if (view.CanCategoryBeHidden(cat.Id) && view.GetCategoryHidden(cat.Id)) hidden.Add(cat.Name);
                    }
                    catch { }
                    try
                    {
                        foreach (Category sub in cat.SubCategories)
                        {
                            try
                            {
                                if (view.CanCategoryBeHidden(sub.Id) && view.GetCategoryHidden(sub.Id)) hidden.Add(cat.Name + " › " + sub.Name);
                            }
                            catch { }
                        }
                    }
                    catch { }
                }
            }
            catch (Exception ex) { hidden.Add("(讀取失敗：" + ex.Message + ")"); }
            hidden.Sort(StringComparer.Ordinal);

            // 連結顯示設定（Revit 2024+ API）
            var links = new List<object>();
            try
            {
                foreach (RevitLinkInstance li in new FilteredElementCollector(doc).OfClass(typeof(RevitLinkInstance)).Cast<RevitLinkInstance>())
                {
#if REVIT2024_OR_GREATER
                    string vis = null, linkedView = null;
                    try
                    {
                        RevitLinkGraphicsSettings s = view.GetLinkOverrides(li.Id);
                        if (s != null)
                        {
                            vis = s.LinkVisibilityType.ToString();
                            if (s.LinkedViewId != null && s.LinkedViewId != ElementId.InvalidElementId) linkedView = s.LinkedViewId.GetIdValue().ToString();
                        }
                        else vis = "(無取代＝依主體視圖預設)";
                    }
                    catch (Exception ex) { vis = "(讀取失敗：" + ex.Message + ")"; }
                    bool? linkHidden = null;
                    try { linkHidden = li.IsHidden(view); } catch { }
                    links.Add(new { Id = li.Id.GetIdValue(), li.Name, Visibility = vis, LinkedViewId = linkedView, HiddenInView = linkHidden });
#else
                    links.Add(new { Id = li.Id.GetIdValue(), li.Name, Visibility = "(Revit 2023 以前 API 不支援讀取)" });
#endif
                }
            }
            catch (Exception ex) { links.Add(new { Error = ex.Message }); }

            // 視圖專屬元素計數
            int detailLines = 0, filledRegions = 0, textNotes = 0;
            try
            {
                detailLines = new FilteredElementCollector(doc, view.Id).OfClass(typeof(CurveElement))
                    .Cast<CurveElement>().Count(c => c.OwnerViewId == view.Id && c.CurveElementType == CurveElementType.DetailCurve);
                filledRegions = new FilteredElementCollector(doc, view.Id).OfClass(typeof(FilledRegion))
                    .Count(e => e.OwnerViewId == view.Id);
                textNotes = new FilteredElementCollector(doc, view.Id).OfClass(typeof(TextNote))
                    .Count(e => e.OwnerViewId == view.Id);
            }
            catch (Exception ex) { Logger.Error("get_view_extents：計數視圖專屬元素失敗", ex); }

            // 2026-09-25：Scope Box／方位／圖紙上的標題／可見的視圖標記數（Key Plan 自測用）
            string orientation = ParamText(BuiltInParameter.PLAN_VIEW_NORTH);
            string titleOnSheet = null;
            try { titleOnSheet = view.get_Parameter(BuiltInParameter.VIEW_DESCRIPTION)?.AsString(); } catch { }
            Dictionary<string, int> visibleViewers = null;
            try { visibleViewers = CameraKeyPlanService.CountVisibleViewers(doc, view); }
            catch (Exception ex) { Logger.Error("get_view_extents：計數可見視圖標記失敗", ex); }

            string detailLevel = null, displayStyle = null;
            try { detailLevel = view.DetailLevel.ToString(); } catch { }
            try { displayStyle = view.DisplayStyle.ToString(); } catch { }

            return new
            {
                Success = true,
                Id = view.Id.GetIdValue(),
                view.Name,
                ViewType = view.ViewType.ToString(),
                Scale = SafeScale(view),
                IsTemplate = view.IsTemplate,
                Note = "長度為內部原點座標系 mm；Box 的 Min/Max 是視圖自身座標（套 Transform 得世界座標）。",
                CropBox = crop,
                ScopeBox = ScopeBoxOf(doc, view),
                Orientation = orientation,
                TitleOnSheet = titleOnSheet,
                VisibleViewers = visibleViewers,
                AnnotationCrop = annotationCrop,
                View3D = view3d,
                FarClipping = farClipping,
                DisplayModel = ParamText(BuiltInParameter.VIEW_MODEL_DISPLAY_MODE),
                DetailLevel = detailLevel,
                DisplayStyle = displayStyle,
                ViewTemplate = template,
                HiddenCategoryCount = hidden.Count,
                HiddenCategories = hidden,
                Links = links,
                ViewOwnedElements = new { DetailLines = detailLines, FilledRegions = filledRegions, TextNotes = textNotes }
            };
        }
    }
}
