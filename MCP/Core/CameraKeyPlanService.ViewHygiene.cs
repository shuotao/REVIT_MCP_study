using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using RevitMCP.Configuration;

namespace RevitMCP.Core
{
    /// <summary>
    /// 2026-09-25（MCP 自測第一輪修正）：Key Plan 視圖的「衛生」處理，集中在這裡。
    ///   1. 複製來的平面（Key Plan 平面、樓層總覽）解除 Scope Box 鎖定、方位＝專案北、清除沿用的「圖紙上的標題」，
    ///      之後才設裁剪框；裁剪框設完一律讀回比對（差 &gt; 1 % 記 WARNING 與可能原因）。
    ///   2. Key Plan 平面／剖面／總覽逐元素隱藏「其他」視圖標記（剖面／立面／詳圖索引 viewer 與立面標記本體），
    ///      平面保留本相機剖面；並隱藏立面／詳圖索引類別（Key Plan 不需要）。
    ///   3. 出圖用透視複本（Role＝PerspectiveForSheet）：原相機已在別張圖紙上時，圖紙改放這張複本；
    ///      複本的方向／裁剪／遠裁剪跟著原相機同步。複本不是相機（清單、總覽、A 標記都排除它）。
    ///   4. run_camera_keyplan 的 markerMode 單次覆寫（不寫回設定）。
    /// </summary>
    internal static partial class CameraKeyPlanService
    {
        internal const string SheetPerspectiveRole = "PerspectiveForSheet";
        private const double CropMismatchTolerance = 0.01; // 1 %

        // ══════════════════════════════════════════════════════════
        // markerMode 單次覆寫（MCP run_camera_keyplan）
        // ══════════════════════════════════════════════════════════

        /// <summary>單次覆寫（MCP）；有值時 <see cref="GetMarkerMode"/> 回傳它，不讀面板設定。</summary>
        private static CameraMarkerMode? _markerModeOverride;

        // ══════════════════════════════════════════════════════════
        // 裁剪框讀回比對
        // ══════════════════════════════════════════════════════════

        // 單一執行緒（Revit API 主執行緒）：一鍵 4 視圖／總覽開始時清空，結束時收進回傳值。
        private static readonly List<string> _cropWarnings = new List<string>();

        internal static void ResetCropWarnings() => _cropWarnings.Clear();

        private static void AddCropWarning(string msg)
        {
            Logger.Info("相機 Key Plan WARNING：" + msg);
            _cropWarnings.Add(msg);
        }

        internal static List<string> TakeCropWarnings()
        {
            var copy = new List<string>(_cropWarnings);
            _cropWarnings.Clear();
            return copy;
        }

        /// <summary>設定裁剪框後立刻讀回，寬／高或角點位移差 &gt; 1 % 記 WARNING（附可能原因）。回傳讀回的框。須在交易內。</summary>
        private static BoundingBoxXYZ SetCropAndVerify(View view, BoundingBoxXYZ intended, string what)
        {
            view.CropBox = intended;
            BoundingBoxXYZ actual = null;
            try { actual = view.CropBox; } catch { actual = null; }
            if (actual == null) return null;
            try
            {
                double iw = intended.Max.X - intended.Min.X, ih = intended.Max.Y - intended.Min.Y;
                double aw = actual.Max.X - actual.Min.X, ah = actual.Max.Y - actual.Min.Y;
                Transform it = intended.Transform ?? Transform.Identity, at = actual.Transform ?? Transform.Identity;
                XYZ iMin = it.OfPoint(intended.Min), aMin = at.OfPoint(actual.Min);
                double shift = Math.Sqrt((iMin.X - aMin.X) * (iMin.X - aMin.X) + (iMin.Y - aMin.Y) * (iMin.Y - aMin.Y));
                double scale = Math.Max(Math.Max(Math.Abs(iw), Math.Abs(ih)), 1e-6);
                bool bad = Math.Abs(aw - iw) > CropMismatchTolerance * Math.Max(Math.Abs(iw), 1e-6)
                           || Math.Abs(ah - ih) > CropMismatchTolerance * Math.Max(Math.Abs(ih), 1e-6)
                           || shift > CropMismatchTolerance * scale;
                if (!bad) return actual;

                var causes = new List<string>();
                try
                {
                    Parameter sb = view.get_Parameter(BuiltInParameter.VIEWER_VOLUME_OF_INTEREST_CROP);
                    if (sb != null && sb.AsElementId() != ElementId.InvalidElementId)
                        causes.Add($"Scope Box「{view.Document.GetElement(sb.AsElementId())?.Name}」仍鎖定裁剪框");
                }
                catch { }
                try { if (view.GetCropRegionShapeManager()?.ShapeSet == true) causes.Add("裁剪框是非矩形形狀"); } catch { }
                try { if (view.ViewTemplateId != ElementId.InvalidElementId) causes.Add("視圖樣板仍套用中"); } catch { }
                try { if (view.GetPrimaryViewId() != ElementId.InvalidElementId) causes.Add("這是從屬視圖（裁剪受主視圖／Scope Box 約束）"); } catch { }
                if (causes.Count == 0) causes.Add("原因不明（Revit 調整了裁剪框）");

                string msg = $"{what}「{view.Name}」裁剪框未照預期套用：預期 {CameraMath.FeetToMm(iw):F0}×{CameraMath.FeetToMm(ih):F0} mm，" +
                             $"實際 {CameraMath.FeetToMm(aw):F0}×{CameraMath.FeetToMm(ah):F0} mm（角點位移 {CameraMath.FeetToMm(shift):F0} mm）；可能原因：{string.Join("、", causes)}";
                _cropWarnings.Add(msg);
                Logger.Info("WARNING 相機 Key Plan：" + msg);
            }
            catch (Exception ex) { Logger.Error("相機 Key Plan：裁剪框讀回比對失敗", ex); }
            return actual;
        }

        /// <summary>視圖裁剪框在模型座標的 XY 外接框（讀回值，四角套 Transform）。</summary>
        internal static bool TryGetCropWorldXY(View view, out double minX, out double minY, out double maxX, out double maxY)
        {
            minX = minY = double.MaxValue; maxX = maxY = double.MinValue;
            try
            {
                BoundingBoxXYZ b = view?.CropBox;
                if (b == null) return false;
                Transform tf = b.Transform ?? Transform.Identity;
                foreach (XYZ p in new[] { new XYZ(b.Min.X, b.Min.Y, b.Min.Z), new XYZ(b.Max.X, b.Min.Y, b.Min.Z), new XYZ(b.Min.X, b.Max.Y, b.Min.Z), new XYZ(b.Max.X, b.Max.Y, b.Min.Z) })
                {
                    XYZ w = tf.OfPoint(p);
                    minX = Math.Min(minX, w.X); maxX = Math.Max(maxX, w.X); minY = Math.Min(minY, w.Y); maxY = Math.Max(maxY, w.Y);
                }
                return true;
            }
            catch { return false; }
        }

        // ══════════════════════════════════════════════════════════
        // 複製來的平面：解除 Scope Box／專案北／圖紙標題
        // ══════════════════════════════════════════════════════════

        /// <summary>
        /// 每次建立／更新 Key Plan 平面或樓層總覽、在設定裁剪框「之前」呼叫（須在交易內）：
        ///   ① 移除視圖樣板（樣板可能鎖 Scope Box／方位）；
        ///   ② VIEWER_VOLUME_OF_INTEREST_CROP＝InvalidElementId（解除 Scope Box；否則 CropBox 設定被忽略、維持 Scope Box 範圍）；
        ///   ③ 方位（PLAN_VIEW_NORTH）＝專案北；
        ///   ④ 移除非矩形裁剪形狀；裁剪框若仍是旋轉的（來源 Scope Box 旋轉過）→ 換成軸向對齊（專案北）；
        ///   ⑤ 圖紙上的標題（VIEW_DESCRIPTION）＝titleOnSheet（不沿用來源平面的標題）。
        /// 每一步獨立 try/catch＋log，不中斷流程。
        /// </summary>
        private static void NormalizeDuplicatedPlan(ViewPlan plan, string titleOnSheet)
        {
            if (plan == null) return;
            Document doc = plan.Document;
            string label = $"「{plan.Name}」(id={plan.Id.GetIdValue()})";
            var notes = new List<string>();

            try
            {
                if (plan.ViewTemplateId != ElementId.InvalidElementId) { plan.ViewTemplateId = ElementId.InvalidElementId; notes.Add("移除視圖樣板"); }
            }
            catch (Exception ex) { Logger.Error($"相機 Key Plan：{label} 移除視圖樣板失敗", ex); }

            try
            {
                Parameter sb = plan.get_Parameter(BuiltInParameter.VIEWER_VOLUME_OF_INTEREST_CROP);
                if (sb != null && sb.AsElementId() != ElementId.InvalidElementId)
                {
                    string sbName = doc.GetElement(sb.AsElementId())?.Name;
                    if (sb.IsReadOnly) notes.Add($"Scope Box「{sbName}」參數唯讀，無法解除");
                    else { sb.Set(ElementId.InvalidElementId); notes.Add($"解除 Scope Box「{sbName}」"); }
                }
            }
            catch (Exception ex) { Logger.Error($"相機 Key Plan：{label} 解除 Scope Box 失敗", ex); }

            try
            {
                Parameter north = plan.get_Parameter(BuiltInParameter.PLAN_VIEW_NORTH);
                if (north != null && !north.IsReadOnly && north.StorageType == StorageType.Integer && north.AsInteger() != 0)
                {
                    string before = north.AsValueString();
                    north.Set(0);
                    notes.Add($"方位 {before}→{north.AsValueString()}");
                }
            }
            catch (Exception ex) { Logger.Error($"相機 Key Plan：{label} 設定專案北失敗", ex); }

            try { plan.GetCropRegionShapeManager()?.RemoveCropRegionShape(); } catch { /* 沒有非矩形裁剪時會丟例外，忽略 */ }

            try
            {
                doc.Regenerate();
                BoundingBoxXYZ box = plan.CropBox;
                Transform tf = box?.Transform;
                if (tf != null && (Math.Abs(tf.BasisX.Y) > 1e-6 || tf.BasisX.X < 0))
                {
                    // 旋轉的裁剪框 → 軸向對齊：以原框四角的世界外接框為新範圍，原點保留、旋轉歸零。
                    double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
                    foreach (XYZ p in new[] { box.Min, new XYZ(box.Max.X, box.Min.Y, box.Min.Z), new XYZ(box.Min.X, box.Max.Y, box.Min.Z), box.Max })
                    {
                        XYZ w = tf.OfPoint(p);
                        x0 = Math.Min(x0, w.X); x1 = Math.Max(x1, w.X); y0 = Math.Min(y0, w.Y); y1 = Math.Max(y1, w.Y);
                    }
                    Transform id = Transform.Identity;
                    id.Origin = new XYZ(0, 0, tf.Origin.Z);
                    var nb = new BoundingBoxXYZ { Transform = id, Min = new XYZ(x0, y0, box.Min.Z), Max = new XYZ(x1, y1, box.Max.Z) };
                    double angle = Math.Atan2(tf.BasisX.Y, tf.BasisX.X) * 180.0 / Math.PI;
                    try
                    {
                        plan.CropBox = nb;
                        BoundingBoxXYZ rb = plan.CropBox;
                        bool ok = rb?.Transform != null && Math.Abs(rb.Transform.BasisX.Y) < 1e-6;
                        notes.Add($"裁剪框旋轉 {angle:F1}°→{(ok ? "已轉正" : "Revit 未接受轉正（維持旋轉，裁剪仍依視圖座標設定）")}");
                    }
                    catch (Exception ex) { notes.Add($"裁剪框旋轉 {angle:F1}° 轉正失敗：{ex.Message}"); }
                }
            }
            catch (Exception ex) { Logger.Error($"相機 Key Plan：{label} 裁剪框轉正失敗", ex); }

            try
            {
                Parameter title = plan.get_Parameter(BuiltInParameter.VIEW_DESCRIPTION);
                if (title != null && !title.IsReadOnly && title.StorageType == StorageType.String && title.AsString() != titleOnSheet)
                {
                    string before = title.AsString();
                    title.Set(titleOnSheet ?? "");
                    notes.Add($"圖紙上的標題「{before}」→「{titleOnSheet}」");
                }
            }
            catch (Exception ex) { Logger.Error($"相機 Key Plan：{label} 設定圖紙上的標題失敗", ex); }

            Logger.Info($"相機 Key Plan：{label} 視圖整理：" + (notes.Count > 0 ? string.Join("；", notes) : "無需變更"));
        }

        // ══════════════════════════════════════════════════════════
        // 其他視圖標記（剖面／立面／詳圖索引）逐元素隱藏
        // ══════════════════════════════════════════════════════════

        private static readonly string[] ForeignViewerCategoryNames =
        {
            "OST_Elev", "OST_ElevationMarks", "OST_Callouts", "OST_CalloutHeads", "OST_CalloutBoundary"
        };

        /// <summary>
        /// 視圖內「不是 keepIds」的 viewer（OST_Viewers：剖面／立面／詳圖索引標記）與立面標記本體（OST_Elev）逐元素隱藏，
        /// 並隱藏立面／詳圖索引類別（Key Plan 不需要）。keepIds 先解除隱藏。回傳隱藏數。須在交易內；失敗只記 log。
        /// </summary>
        private static int HideForeignViewers(Document doc, View view, ICollection<ElementId> keepIds, string what)
        {
            if (doc == null || view == null) return 0;
            var keep = new HashSet<long>((keepIds ?? new List<ElementId>()).Select(i => (long)i.GetIdValue()));
            int hidden = 0;
            try
            {
                var toHide = new List<ElementId>();
                var byKind = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (BuiltInCategory bic in new[] { BuiltInCategory.OST_Viewers, BuiltInCategory.OST_Elev })
                {
                    foreach (Element e in new FilteredElementCollector(doc, view.Id).OfCategory(bic).WhereElementIsNotElementType())
                    {
                        if (keep.Contains(e.Id.GetIdValue())) continue;
                        if (e.Id == view.Id || IsOwnCropElement(e, view)) continue;
                        bool can = false;
                        try { can = e.CanBeHidden(view); } catch { }
                        if (!can) continue;
                        toHide.Add(e.Id);
                        string kind = bic == BuiltInCategory.OST_Elev ? "立面標記" : ViewerKind(doc, e);
                        byKind[kind] = byKind.TryGetValue(kind, out int n) ? n + 1 : 1;
                    }
                }
                if (toHide.Count > 0) view.HideElements(toHide);
                hidden = toHide.Count;
                int cats = HideCategoriesByName(view, ForeignViewerCategoryNames, includeAllTags: false, keepRoomTags: true, hide: true);
                Logger.Info($"相機 Key Plan：{what}「{view.Name}」隱藏其他視圖標記 {hidden} 個（{string.Join("、", byKind.Select(kv => kv.Key + " " + kv.Value))}），" +
                            $"保留 {keep.Count} 個；隱藏立面／詳圖索引類別 {cats} 個");
            }
            catch (Exception ex)
            {
                Logger.Error($"相機 Key Plan：{what}「{view.Name}」隱藏其他視圖標記失敗（視圖照常建立）", ex);
                CameraManagerDiagnostics.RecordCaught("HideForeignViewers", ex);
            }
            return hidden;
        }

        /// <summary>視圖自己的裁剪框元素也在 OST_Viewers、名稱與視圖相同（常見做法：以此找裁剪框元素旋轉）→ 不可當成「其他標記」隱藏。</summary>
        private static bool IsOwnCropElement(Element e, View view)
        {
            try
            {
                if (string.Equals(e.Name, view.Name, StringComparison.Ordinal)) return true;
                string vn = e.get_Parameter(BuiltInParameter.VIEW_NAME)?.AsString();
                return !string.IsNullOrEmpty(vn) && string.Equals(vn, view.Name, StringComparison.Ordinal);
            }
            catch { return false; }
        }

        private static string ViewerKind(Document doc, Element viewer)
        {
            try
            {
                string vn = viewer.get_Parameter(BuiltInParameter.VIEW_NAME)?.AsString() ?? viewer.Name;
                View v = new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>().FirstOrDefault(x => !x.IsTemplate && x.Name == vn);
                if (v == null) return "viewer";
                switch (v.ViewType)
                {
                    case ViewType.Section: return "剖面";
                    case ViewType.Elevation: return "立面";
                    case ViewType.Detail: return "詳圖";
                    default: return v.ViewType.ToString();
                }
            }
            catch { return "viewer"; }
        }

        /// <summary>視圖內目前「可見」的 viewer 數（剖面／立面／詳圖索引）與立面標記數（get_view_extents 用，唯讀）。</summary>
        internal static Dictionary<string, int> CountVisibleViewers(Document doc, View view)
        {
            var counts = new Dictionary<string, int>(StringComparer.Ordinal) { ["Sections"] = 0, ["Elevations"] = 0, ["Callouts"] = 0, ["Other"] = 0, ["ElevationMarks"] = 0 };
            if (doc == null || view == null) return counts;
            var viewsByName = new Dictionary<string, View>(StringComparer.Ordinal);
            foreach (View v in new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>())
                if (!v.IsTemplate && !viewsByName.ContainsKey(v.Name)) viewsByName[v.Name] = v;
            bool CatHidden(BuiltInCategory bic)
            {
                try { Category c = Category.GetCategory(doc, bic); return c != null && view.CanCategoryBeHidden(c.Id) && view.GetCategoryHidden(c.Id); }
                catch { return false; }
            }
            foreach (Element e in new FilteredElementCollector(doc, view.Id).OfCategory(BuiltInCategory.OST_Viewers).WhereElementIsNotElementType())
            {
                if (e.Id == view.Id || IsOwnCropElement(e, view)) continue;
                try { if (e.IsHidden(view)) continue; } catch { }
                string vn = null;
                try { vn = e.get_Parameter(BuiltInParameter.VIEW_NAME)?.AsString(); } catch { }
                vn = string.IsNullOrEmpty(vn) ? e.Name : vn;
                View target = vn != null && viewsByName.TryGetValue(vn, out View t) ? t : null;
                string key = target == null ? "Other"
                    : target.ViewType == ViewType.Section ? "Sections"
                    : target.ViewType == ViewType.Elevation ? "Elevations"
                    : target.ViewType == ViewType.Detail || target.ViewType == ViewType.FloorPlan || target.ViewType == ViewType.CeilingPlan ? "Callouts" : "Other";
                if (key == "Sections" && CatHidden(BuiltInCategory.OST_Sections)) continue;
                counts[key]++;
            }
            if (!CatHidden(BuiltInCategory.OST_Elev))
            {
                foreach (Element e in new FilteredElementCollector(doc, view.Id).OfCategory(BuiltInCategory.OST_Elev).WhereElementIsNotElementType())
                {
                    try { if (e.IsHidden(view)) continue; } catch { }
                    counts["ElevationMarks"]++;
                }
            }
            return counts;
        }

        // ══════════════════════════════════════════════════════════
        // 出圖用透視複本
        // ══════════════════════════════════════════════════════════

        /// <summary>這張視圖是不是出圖用的透視複本（不是相機）。</summary>
        internal static bool IsSheetPerspectiveCopy(View v)
        {
            try { return v != null && TryReadLink(v, out _, out string role) && role == SheetPerspectiveRole; }
            catch { return false; }
        }

        internal static View3D FindSheetPerspectiveCopy(Document doc, string cameraUniqueId)
        {
            if (doc == null || string.IsNullOrEmpty(cameraUniqueId) || Autodesk.Revit.DB.ExtensibleStorage.Schema.Lookup(SchemaGuid) == null) return null;
            return FindLinkedView(doc, cameraUniqueId, SheetPerspectiveRole) as View3D;
        }

        /// <summary>找或建立 <c>&lt;相機名&gt;-Key Plan 圖紙</c> 透視複本，並同步到原相機目前的狀態。須在交易內。</summary>
        private static View3D GetOrCreateSheetPerspectiveCopy(Document doc, View3D camera, out bool created)
        {
            created = false;
            View3D copy = FindSheetPerspectiveCopy(doc, camera.UniqueId);
            string name = camera.Name + "-Key Plan 圖紙";
            if (copy == null)
            {
                ElementId id = camera.Duplicate(ViewDuplicateOption.Duplicate);
                copy = doc.GetElement(id) as View3D;
                if (copy == null) throw new Exception("複製透視圖失敗。");
                TagLink(copy, camera.UniqueId, SheetPerspectiveRole);
                created = true;
            }
            if (copy.Name != name)
            {
                try { copy.Name = MakeUniqueName(doc, name, copy.Id); } catch (Exception ex) { Logger.Error("相機圖紙：透視複本改名失敗", ex); }
            }
            SyncSheetPerspectiveCopy(camera, copy);
            return copy;
        }

        /// <summary>把原相機的方向、裁剪框（含開關）、遠裁剪（值＋開關）套到複本。須在交易內；個別失敗只記 log。回傳摘要。</summary>
        private static string SyncSheetPerspectiveCopy(View3D camera, View3D copy)
        {
            if (camera == null || copy == null) return null;
            var notes = new List<string>();
            // 2026-09-25 第二輪：複本在圖紙上時，裁剪框曾被等比放大填滿格子 → 同步前記下視埠大小，同步後等比縮放回相同外框（FOV 不變）。
            Viewport copyVp = null;
            double vw0 = 0, vh0 = 0;
            try
            {
                copyVp = new FilteredElementCollector(copy.Document).OfClass(typeof(Viewport)).Cast<Viewport>().FirstOrDefault(v => v.ViewId == copy.Id);
                if (copyVp != null) (vw0, vh0) = ViewportSize(copyVp);
            }
            catch { copyVp = null; }
            try { copy.SetOrientation(camera.GetOrientation()); notes.Add("方向"); }
            catch (Exception ex) { Logger.Error($"相機圖紙：透視複本「{copy.Name}」同步方向失敗", ex); }
            try
            {
                if (copy.CropBoxActive != camera.CropBoxActive) copy.CropBoxActive = camera.CropBoxActive;
                copy.CropBox = camera.CropBox;
                notes.Add("裁剪框");
            }
            catch (Exception ex) { Logger.Error($"相機圖紙：透視複本「{copy.Name}」同步裁剪框失敗", ex); }
            try
            {
                Parameter srcActive = camera.get_Parameter(BuiltInParameter.VIEWER_BOUND_ACTIVE_FAR);
                Parameter dstActive = copy.get_Parameter(BuiltInParameter.VIEWER_BOUND_ACTIVE_FAR);
                if (srcActive != null && dstActive != null && !dstActive.IsReadOnly && dstActive.AsInteger() != srcActive.AsInteger())
                    dstActive.Set(srcActive.AsInteger());
                Parameter srcFar = CameraMath.FindFarClipOffsetParameter(camera);
                Parameter dstFar = CameraMath.FindFarClipOffsetParameter(copy);
                if (srcFar != null && dstFar != null && !dstFar.IsReadOnly && Math.Abs(dstFar.AsDouble() - srcFar.AsDouble()) > 1e-9)
                    dstFar.Set(srcFar.AsDouble());
                notes.Add("遠裁剪");
            }
            catch (Exception ex) { Logger.Error($"相機圖紙：透視複本「{copy.Name}」同步遠裁剪失敗", ex); }
            if (copyVp != null && vw0 > 1e-9 && vh0 > 1e-9)
            {
                try
                {
                    copy.Document.Regenerate();
                    var (vw1, vh1) = ViewportSize(copyVp);
                    if (vw1 > 1e-9 && vh1 > 1e-9)
                    {
                        double k = Math.Min(vw0 / vw1, vh0 / vh1);
                        if (Math.Abs(k - 1.0) > 0.01) notes.Add("圖紙大小（" + EnlargePerspectiveCrop(copy.Document, copy, k) + "）");
                    }
                }
                catch (Exception ex) { Logger.Error($"相機圖紙：透視複本「{copy.Name}」維持圖紙上大小失敗", ex); }
            }
            return string.Join("／", notes);
        }
    }
}
