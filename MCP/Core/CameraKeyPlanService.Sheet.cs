using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using Autodesk.Revit.UI;
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
    /// 2026-09-25 Wave 3：一鍵出圖紙（spec §10.13、ADR D-029）。
    ///
    /// 一台相機一張圖紙「Key Plan-&lt;相機名&gt;」：左側約 60% 放透視圖，右欄由上而下放 Key Plan 平面／剖面／等角。
    /// 規則：
    ///   1. 圖框類型：呼叫端指定 → 面板設定（<see cref="CameraManagerSettings.SheetTitleBlockTypeId"/>）→ 專案內最常用 → 第一個圖框類型 → 無圖框。
    ///   2. 可繪區＝圖框實例在圖紙上的外接框向內縮 <see cref="CameraManagerSettings.SheetMarginMm"/>；沒有圖框＝A3 橫式 420×297 mm。
    ///   3. 2026-09-25 第二輪：可繪區再扣掉圖框標題欄（分析圖框幾何的長直／長橫線；偵測不到＝橫式保留右側 20 %）。
    ///      透視圖左 55 %；右欄三列高度依內容長寬比分配。本工具的 Key Plan 平面／剖面／等角改為「放得進格子的最細標準比例」
    ///      （1:20/25/50/75/100/150/200，都放不進再往 1:250… 延伸）；出圖用透視複本等比放大裁剪框填滿格子約 90 %（FOV 不變，讀回驗證）。
    ///   4. 視埠類型：沿用專案既有、會顯示標題的視埠類型（最常用者），不新建。
    ///   5. 一張視圖只能在一張圖紙上：已在「本相機的圖紙」（ES Role＝Sheet）上的視埠重新排版；在別張圖紙上的回報並略過。
    ///      例外（2026-09-25）：透視圖已在別張圖紙上 → 改放出圖用透視複本（見 CameraKeyPlanService.ViewHygiene.cs）。
    ///   6. 相機還沒有 Key Plan 視圖時，先跑一鍵 4 視圖（不並排）。
    /// 單一交易「建立相機圖紙」；呼叫端須在 API context、交易外。
    /// </summary>
    internal static partial class CameraKeyPlanService
    {
        internal const string SheetRole = "Sheet";
        internal const string SheetTransactionName = "建立相機圖紙";
        private const double SheetDefaultWidthMm = 420.0;   // 沒有圖框時的預設可繪區（A3 橫式）
        private const double SheetDefaultHeightMm = 297.0;
        private const double SheetPerspectiveFraction = 0.55; // 透視圖佔可繪區寬度的比例（2026-09-25 第二輪 0.6→0.55）
        private const double SheetCellGapMm = 6.0;           // 格子之間的間距（留給視埠標題）

        internal sealed class SheetViewportInfo
        {
            public string Role;            // Perspective／Plan／Section／Iso
            public ElementId ViewId;
            public string ViewName;
            public ElementId ViewportId;
            public string Status;          // 新放置／重新排版／略過
            public string Note;
            public double CenterXFeet, CenterYFeet, WidthFeet, HeightFeet;
            public double CellMinX, CellMinY, CellMaxX, CellMaxY;
            public bool Overflow;
            public string ViewportTypeName;
        }

        internal sealed class SheetResult
        {
            public ElementId SheetId;
            public string SheetNumber;
            public string SheetName;
            public bool Created;
            public bool RanFourViews;
            public ElementId TitleBlockTypeId;
            public string TitleBlockName;
            public string TitleBlockSource;
            public string ViewportTypeLog;
            /// <summary>2026-09-25：圖紙上放的透視圖是「原相機」還是「透視複本（新建／沿用並同步）」。</summary>
            public string PerspectivePlaced;
            public string PerspectiveNote;
            public ElementId PerspectiveViewId;
            public double DrawMinX, DrawMinY, DrawMaxX, DrawMaxY; // 可繪區（圖紙座標 feet）
            public string DrawAreaSource;
            public List<SheetViewportInfo> Viewports = new List<SheetViewportInfo>();
            public List<string> Warnings = new List<string>();
            public string Error;
        }

        // ══════════════════════════════════════════════════════════
        // 圖框類型
        // ══════════════════════════════════════════════════════════

        /// <summary>專案內所有圖框類型＋已放置次數（依使用次數多→少、名稱排序）。</summary>
        internal static List<TitleBlockOption> ListTitleBlockTypes(Document doc)
        {
            var list = new List<TitleBlockOption>();
            if (doc == null) return list;
            var usage = new Dictionary<long, int>();
            foreach (Element tb in new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_TitleBlocks).WhereElementIsNotElementType())
            {
                long tid = tb.GetTypeId().GetIdValue();
                usage[tid] = usage.TryGetValue(tid, out int n) ? n + 1 : 1;
            }
            foreach (FamilySymbol fs in new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_TitleBlocks).WhereElementIsElementType().OfType<FamilySymbol>())
            {
                long id = fs.Id.GetIdValue();
                list.Add(new TitleBlockOption
                {
                    Id = id,
                    Name = (fs.FamilyName ?? "") + "：" + fs.Name,
                    UseCount = usage.TryGetValue(id, out int n) ? n : 0
                });
            }
            return list.OrderByDescending(o => o.UseCount).ThenBy(o => o.Name, StringComparer.Ordinal).ToList();
        }

        /// <summary>挑圖框類型：requested（MCP 參數）→ 面板設定 → 最常用 → 第一個；都沒有＝InvalidElementId。</summary>
        internal static ElementId ChooseTitleBlockType(Document doc, long? requested, out string name, out string source)
        {
            name = null;
            source = null;
            List<TitleBlockOption> all = ListTitleBlockTypes(doc);
            TitleBlockOption Find(long id) => all.FirstOrDefault(o => o.Id == id);

            if (requested.HasValue && requested.Value > 0)
            {
                TitleBlockOption o = Find(requested.Value);
                if (o == null) throw new Exception($"titleBlockTypeId {requested.Value} 不是圖框類型。");
                name = o.Name; source = "指定";
                return new ElementId((IdType)o.Id);
            }
            long setting = 0;
            try { setting = ConfigManager.Instance.Settings.CameraManager.SheetTitleBlockTypeId; } catch { }
            if (setting > 0)
            {
                TitleBlockOption o = Find(setting);
                if (o != null) { name = o.Name; source = "面板設定"; return new ElementId((IdType)o.Id); }
                Logger.Info($"相機圖紙：面板設定的圖框類型 id={setting} 已不存在，改用最常用的圖框。");
            }
            if (all.Count == 0) { source = "專案內沒有圖框類型（無圖框）"; return ElementId.InvalidElementId; }
            TitleBlockOption first = all[0];
            name = first.Name;
            source = first.UseCount > 0 ? $"最常用（{first.UseCount} 張圖紙）" : "沒有圖紙使用圖框，取第一個類型";
            return new ElementId((IdType)first.Id);
        }

        // ══════════════════════════════════════════════════════════
        // 視埠類型：沿用會顯示標題的既有類型（最常用者），不新建
        // ══════════════════════════════════════════════════════════

        private static readonly string[] NoTitleTexts = { "No", "否", "無", "不", "不顯示", "Never" };

        /// <summary>視埠類型是否顯示標題（VIEWPORT_ATTR_SHOW_LABEL）；raw＝原始值（log 用）。</summary>
        private static bool ViewportTypeShowsTitle(ElementType t, out string raw)
        {
            bool title = true;
            raw = "(無參數)";
            try
            {
                Parameter p = t.get_Parameter(BuiltInParameter.VIEWPORT_ATTR_SHOW_LABEL);
                if (p != null)
                {
                    string vs = p.AsValueString();
                    int iv = p.StorageType == StorageType.Integer ? p.AsInteger() : -1;
                    raw = $"{vs ?? "?"}/{iv}";
                    // 未查證：整數值與「是／否／多個視埠時」的對應。先看顯示字串，取不到才以 0＝否判斷。
                    title = !string.IsNullOrWhiteSpace(vs)
                        ? !NoTitleTexts.Any(x => string.Equals(vs.Trim(), x, StringComparison.OrdinalIgnoreCase))
                        : iv != 0;
                }
            }
            catch { }
            return title;
        }

        /// <summary>
        /// 2026-09-25（MCP 自測：log 說「沒有顯示標題的視埠類型」，實際卻套到 Title w Line）：候選不再只靠
        /// ElementType.Category（視埠類型的 Category 在部分版本為 null，候選清單就是空的），改為聯集：
        /// Category＝Viewports、FamilyName＝Viewport／視埠／視口、既有視埠用到的類型、呼叫端給的 extraIds
        /// （新視埠的 GetValidTypes）。
        /// </summary>
        private static ElementId ChooseViewportType(Document doc, IEnumerable<ElementId> extraIds, out string typeName, out string log)
        {
            typeName = null;
            var usage = new Dictionary<long, int>();
            foreach (Viewport vp in new FilteredElementCollector(doc).OfClass(typeof(Viewport)).Cast<Viewport>())
            {
                long tid = vp.GetTypeId().GetIdValue();
                usage[tid] = usage.TryGetValue(tid, out int n) ? n + 1 : 1;
            }

            long vpCat = (long)(int)BuiltInCategory.OST_Viewports;
            var familyNames = new HashSet<string>(new[] { "Viewport", "視埠", "視口" }, StringComparer.OrdinalIgnoreCase);
            var extra = new HashSet<long>((extraIds ?? Enumerable.Empty<ElementId>()).Select(i => (long)i.GetIdValue()));
            var seen = new HashSet<long>();
            var candidates = new List<(ElementType t, bool title, int used, string raw)>();
            foreach (ElementType t in new FilteredElementCollector(doc).OfClass(typeof(ElementType)).Cast<ElementType>())
            {
                long id = t.Id.GetIdValue();
                bool isVp = (t.Category != null && t.Category.Id.GetIdValue() == vpCat)
                            || familyNames.Contains(t.FamilyName ?? "")
                            || usage.ContainsKey(id) || extra.Contains(id);
                if (!isVp || !seen.Add(id)) continue;
                bool title = ViewportTypeShowsTitle(t, out string raw);
                candidates.Add((t, title, usage.TryGetValue(id, out int u) ? u : 0, raw));
            }

            log = "視埠類型候選：" + (candidates.Count == 0 ? "(無)" : string.Join("；", candidates.Select(c => $"{c.t.Name}[標題={c.raw}，用 {c.used} 次]")));
            var pick = candidates.Where(c => c.title).OrderByDescending(c => c.used).ThenBy(c => c.t.Name, StringComparer.Ordinal).FirstOrDefault();
            if (pick.t != null)
            {
                typeName = pick.t.Name;
                log += $" → 採用「{pick.t.Name}」";
                return pick.t.Id;
            }
            log += candidates.Count == 0
                ? " → 找不到任何視埠類型候選，放置時由 Revit 套預設類型（見「實際使用」）"
                : " → 候選都不顯示標題，放置時由 Revit 套預設類型（見「實際使用」）";
            return ElementId.InvalidElementId;
        }

        // ══════════════════════════════════════════════════════════
        // 進入點
        // ══════════════════════════════════════════════════════════

        internal static ViewSheet FindCameraSheet(Document doc, string cameraUniqueId)
        {
            if (doc == null || string.IsNullOrEmpty(cameraUniqueId) || Schema.Lookup(SchemaGuid) == null) return null;
            return FindLinkedView(doc, cameraUniqueId, SheetRole) as ViewSheet;
        }

        /// <summary>建立或更新一台相機的圖紙。titleBlockTypeId＝null 走面板設定／最常用。</summary>
        internal static SheetResult CreateOrUpdateCameraSheet(UIApplication uiApp, Document doc, ElementId cameraViewId, long? titleBlockTypeId)
        {
            var res = new SheetResult();
            if (doc == null) { res.Error = "目前沒有開啟中的文件。"; return res; }
            View3D camera = doc.GetElement(cameraViewId) as View3D;
            if (camera == null || camera.IsTemplate || !camera.IsPerspective) { res.Error = "找不到這個相機視圖（可能已被刪除），請按重新整理。"; return res; }
            string cid = camera.UniqueId;
            string camName = camera.Name;

            // 1) 沒有 Key Plan 視圖 → 先跑一鍵 4 視圖（不並排）
            View plan = FindLinkedKeyPlanView(doc, cid, "Plan");
            View section = FindLinkedKeyPlanView(doc, cid, "Section");
            View iso = FindLinkedKeyPlanView(doc, cid, "Iso");
            if (plan == null || section == null || iso == null)
            {
                Logger.Info($"相機圖紙：「{camName}」缺少 Key Plan 視圖（平面={plan != null} 剖面={section != null} 等角={iso != null}），先執行一鍵 4 視圖。");
                KeyPlanResult kp = CreateOrUpdateFourViews(uiApp, doc, cameraViewId, false);
                if (kp.Error != null) { res.Error = "先建立 Key Plan 失敗：" + kp.Error; return res; }
                res.RanFourViews = true;
                // 2026-09-26 稽核：Id 可能為 null（或交易回滾後元素已不存在）→ GetElement(null) 會丟 ArgumentNullException。
                plan = kp.PlanViewId != null ? doc.GetElement(kp.PlanViewId) as View : null;
                section = kp.SectionViewId != null ? doc.GetElement(kp.SectionViewId) as View : null;
                iso = kp.IsoViewId != null ? doc.GetElement(kp.IsoViewId) as View : null;
                if (plan == null || section == null || iso == null)
                {
                    res.Error = $"一鍵 4 視圖完成但讀不回 Key Plan 視圖（平面={plan != null} 剖面={section != null} 等角={iso != null}），未建立圖紙。";
                    return res;
                }
            }

            // 2) 交易外：圖框、視埠類型、各視圖目前在哪張圖紙
            ElementId tbType;
            try { tbType = ChooseTitleBlockType(doc, titleBlockTypeId, out res.TitleBlockName, out res.TitleBlockSource); }
            catch (Exception ex) { res.Error = ex.Message; return res; }
            res.TitleBlockTypeId = tbType;
            ElementId vpType = ChooseViewportType(doc, null, out string vpTypeName, out string vpLog);
            res.ViewportTypeLog = vpLog;
            Logger.Info("相機圖紙：" + vpLog);

            ViewSheet sheet = FindCameraSheet(doc, cid);
            var viewOnSheet = new Dictionary<long, Viewport>();
            foreach (Viewport vp in new FilteredElementCollector(doc).OfClass(typeof(Viewport)).Cast<Viewport>())
                viewOnSheet[vp.ViewId.GetIdValue()] = vp;

            // 2026-09-25（MCP 自測：透視圖已在別張圖紙上就被略過）：一張視圖只能放一張圖紙 → 原相機已在「別張」圖紙上時，
            // 改放出圖用透視複本「<相機名>-Key Plan 圖紙」（ES Role＝PerspectiveForSheet，沿用既有複本）；複本跟著原相機同步。
            // 選擇順序：原相機已在本相機圖紙上 → 原相機；已有複本 → 複本；原相機在別張圖紙上 → 建立複本；否則 → 原相機。
            bool camOnOurSheet = false, camOnOtherSheet = false;
            string camOtherSheetText = null;
            if (viewOnSheet.TryGetValue(camera.Id.GetIdValue(), out Viewport camVp) && doc.GetElement(camVp.Id) != null)
            {
                if (sheet != null && camVp.SheetId == sheet.Id) camOnOurSheet = true;
                else
                {
                    camOnOtherSheet = true;
                    ViewSheet os = doc.GetElement(camVp.SheetId) as ViewSheet;
                    camOtherSheetText = $"{os?.SheetNumber} - {os?.Name}";
                }
            }
            View3D existingCopy = FindSheetPerspectiveCopy(doc, cid);
            bool useCopy = !camOnOurSheet && (existingCopy != null || camOnOtherSheet);

            var items = new List<(string role, View view)>
            {
                ("Perspective", useCopy ? null : camera), ("Plan", plan), ("Section", section), ("Iso", iso)
            };

            double marginFeet = CameraMath.MmToFeet(GetSheetMarginMm());
            using (Transaction t = TransactionHelper.Begin(doc, SheetTransactionName))
            {
                t.Start();
                try
                {
                    string sheetName = SanitizeRevitName("Key Plan-" + camName);
                    if (sheet == null)
                    {
                        sheet = ViewSheet.Create(doc, tbType);
                        TagLink(sheet, cid, SheetRole);
                        res.Created = true;
                    }
                    else if (tbType != ElementId.InvalidElementId)
                    {
                        // 既有圖紙：圖框類型與目前選擇不同 → 換類型（不動位置）
                        Element tbInst = FirstTitleBlockOn(doc, sheet);
                        if (tbInst != null && tbInst.GetTypeId() != tbType)
                        {
                            try { tbInst.ChangeTypeId(tbType); res.Warnings.Add("已把既有圖紙的圖框換成「" + res.TitleBlockName + "」。"); }
                            catch (Exception ex) { res.Warnings.Add("更換圖框類型失敗：" + ex.Message); }
                        }
                    }
                    if (sheet.Name != sheetName)
                    {
                        try { sheet.Name = sheetName; }
                        catch (Exception ex) { res.Warnings.Add("圖紙改名失敗：" + ex.Message); }
                    }
                    doc.Regenerate();

                    // 3) 可繪區：圖框外接框內縮邊界，再扣掉圖框的標題欄（2026-09-25 第二輪：右欄壓到標題欄）
                    Element tb = FirstTitleBlockOn(doc, sheet);
                    BoundingBoxXYZ bb = null;
                    try { bb = tb?.get_BoundingBox(sheet); } catch { bb = null; }
                    double x0, y0, x1, y1;
                    if (bb != null && bb.Max.X - bb.Min.X > 2 * marginFeet && bb.Max.Y - bb.Min.Y > 2 * marginFeet)
                    {
                        x0 = bb.Min.X + marginFeet; y0 = bb.Min.Y + marginFeet;
                        x1 = bb.Max.X - marginFeet; y1 = bb.Max.Y - marginFeet;
                        res.DrawAreaSource = $"圖框外接框 {CameraMath.FeetToMm(bb.Max.X - bb.Min.X):F0}×{CameraMath.FeetToMm(bb.Max.Y - bb.Min.Y):F0} mm 內縮 {GetSheetMarginMm():0.#} mm";
                        DetectTitleStrip(tb, sheet, bb, out double? stripLeftX, out double? stripTopY, out string stripLog, out bool stripDetected);
                        double stripMargin = marginFeet;
                        if (!stripDetected)
                        {
                            // 2026-09-26 第四輪（MCP 自測：E1 30 x 42 圖框幾何讀不到線，15 mm 邊界讓透視圖壓到左側內框
                            // ——內框離紙邊約 38 mm）：偵測不到圖框線時，四邊改用 max(設定邊界, 4 % 圖紙寬) 內縮，
                            // 另外仍保留右側標題欄帶。偵測得到線時維持原本的設定邊界。
                            double fb = Math.Max(marginFeet, UndetectedFrameMarginFraction * (bb.Max.X - bb.Min.X));
                            if (fb > marginFeet && bb.Max.X - bb.Min.X > 2 * fb && bb.Max.Y - bb.Min.Y > 2 * fb)
                            {
                                x0 = bb.Min.X + fb; y0 = bb.Min.Y + fb;
                                x1 = bb.Max.X - fb; y1 = bb.Max.Y - fb;
                                stripMargin = fb;
                                res.DrawAreaSource += $"；未偵測到圖框線 → 四邊內縮改為 max({GetSheetMarginMm():0.#} mm, {UndetectedFrameMarginFraction * 100:0.#}% 圖紙寬)＝{CameraMath.FeetToMm(fb):F0} mm";
                            }
                        }
                        if (stripLeftX.HasValue) x1 = Math.Min(x1, stripLeftX.Value - stripMargin);
                        if (stripTopY.HasValue) y0 = Math.Max(y0, stripTopY.Value + stripMargin);
                        res.DrawAreaSource += "；" + stripLog;
                        Logger.Info("相機圖紙：標題欄判定 " + stripLog);
                    }
                    else
                    {
                        x0 = marginFeet; y0 = marginFeet;
                        x1 = CameraMath.MmToFeet(SheetDefaultWidthMm) - marginFeet;
                        y1 = CameraMath.MmToFeet(SheetDefaultHeightMm) - marginFeet;
                        res.DrawAreaSource = "沒有圖框（或讀不到外接框）→ A3 橫式 420×297 mm 內縮 " + GetSheetMarginMm().ToString("0.#") + " mm";
                    }
                    res.DrawMinX = x0; res.DrawMinY = y0; res.DrawMaxX = x1; res.DrawMaxY = y1;

                    if (useCopy)
                    {
                        View3D copy = GetOrCreateSheetPerspectiveCopy(doc, camera, out bool copyCreated);
                        items[0] = ("Perspective", copy);
                        res.PerspectivePlaced = copyCreated ? "透視複本（新建）" : "透視複本（沿用並同步）";
                        res.PerspectiveNote = camOnOtherSheet
                            ? $"原相機已在圖紙「{camOtherSheetText}」上，改放「{copy.Name}」（方向／裁剪／遠裁剪與原相機同步）"
                            : $"沿用既有出圖複本「{copy.Name}」（已與原相機同步）";
                        res.Warnings.Add(res.PerspectiveNote);
                        // 2026-09-25 第二輪：複本也要隱藏 A 型標記子類別（樣板控制 V/G 時改在樣板隱藏）。
                        Category markerSub = FindProjectMarkerSubcategory(doc);
                        if (markerSub != null)
                        {
                            HideAMarkersInPerspectiveViews(doc, markerSub, out int hf, out string hl);
                            if (hf > 0) res.Warnings.Add("透視圖隱藏相機標記子類別失敗 " + hf + " 張" + hl);
                        }
                    }
                    else res.PerspectivePlaced = "原相機";
                    res.PerspectiveViewId = items[0].view?.Id;

                    // 4) 先確保四個視埠都在圖紙上（新放置者暫放可繪區中心），再依內容大小排版。
                    bool retriedVpType = false;
                    var placedVps = new Dictionary<string, Viewport>();
                    XYZ tmpCenter = new XYZ((x0 + x1) / 2, (y0 + y1) / 2, 0);
                    foreach (var (role, view) in items)
                    {
                        var info = new SheetViewportInfo { Role = role, ViewId = view?.Id, ViewName = view?.Name };
                        res.Viewports.Add(info);
                        if (view == null) { info.Status = "略過"; info.Note = "找不到視圖"; continue; }

                        if (viewOnSheet.TryGetValue(view.Id.GetIdValue(), out Viewport existing) && doc.GetElement(existing.Id) != null)
                        {
                            if (existing.SheetId == sheet.Id)
                            {
                                info.Status = "重新排版";
                                info.ViewportId = existing.Id;
                                placedVps[role] = existing;
                                continue;
                            }
                            ViewSheet other = doc.GetElement(existing.SheetId) as ViewSheet;
                            info.Status = "略過";
                            info.Note = $"已在其他圖紙「{other?.SheetNumber} - {other?.Name}」上（一張視圖只能放一張圖紙）";
                            res.Warnings.Add($"{view.Name}：{info.Note}");
                            continue;
                        }
                        if (!Viewport.CanAddViewToSheet(doc, sheet.Id, view.Id))
                        {
                            info.Status = "略過";
                            info.Note = "Revit 不允許把這張視圖放到圖紙（CanAddViewToSheet=false）";
                            res.Warnings.Add($"{view.Name}：{info.Note}");
                            continue;
                        }

                        Viewport vp = Viewport.Create(doc, sheet.Id, view.Id, tmpCenter);
                        if (vpType == ElementId.InvalidElementId && !retriedVpType)
                        {
                            // 事前找不到顯示標題的類型 → 用這個新視埠的 GetValidTypes 再挑一次（只試一次）。
                            retriedVpType = true;
                            try
                            {
                                vpType = ChooseViewportType(doc, vp.GetValidTypes(), out vpTypeName, out string retryLog);
                                res.ViewportTypeLog += "｜以新視埠 GetValidTypes 重挑：" + retryLog;
                            }
                            catch (Exception ex) { Logger.Error("相機圖紙：以 GetValidTypes 重挑視埠類型失敗", ex); }
                        }
                        if (vpType != ElementId.InvalidElementId)
                        {
                            try
                            {
                                if (vp.GetValidTypes().Contains(vpType)) vp.ChangeTypeId(vpType);
                            }
                            catch (Exception ex) { Logger.Error("相機圖紙：套用視埠類型失敗", ex); }
                        }
                        info.Status = "新放置";
                        info.ViewportId = vp.Id;
                        placedVps[role] = vp;
                    }
                    doc.Regenerate();

                    // 5) 排版（2026-09-25 第二輪）：透視圖左 55%；右欄三列高度依內容長寬比分配；
                    //    Key Plan 平面／剖面／等角挑「放得進格子的最細標準比例」；透視複本等比放大裁剪框填滿格子約 90%。
                    LayoutSheetViewports(doc, items, placedVps, res, x0, y0, x1, y1);

                    foreach (SheetViewportInfo info in res.Viewports)
                        if (info.ViewportId != null && doc.GetElement(info.ViewportId) is Viewport vpx) FillViewportGeometry(doc, vpx, info);

                    TransactionHelper.CommitOrThrow(t);
                }
                catch (Exception ex)
                {
                    try { if (t.HasStarted() && !t.HasEnded()) t.RollBack(); } catch { }
                    Logger.Error("相機圖紙：建立失敗（整筆回滾）", ex);
                    res.Error = "建立圖紙失敗：" + ex.Message;
                    res.SheetId = null;
                    return res;
                }
            }

            List<string> usedTypes = res.Viewports.Where(v => v.ViewportTypeName != null).Select(v => v.ViewportTypeName).Distinct().ToList();
            res.ViewportTypeLog += "｜實際使用：" + (usedTypes.Count == 0 ? "(無視埠)" : string.Join("、", usedTypes));
            res.SheetId = sheet.Id;
            res.SheetNumber = sheet.SheetNumber;
            res.SheetName = sheet.Name;
            foreach (SheetViewportInfo v in res.Viewports.Where(v => v.Overflow))
                res.Warnings.Add($"{v.ViewName}：視埠 {CameraMath.FeetToMm(v.WidthFeet):F0}×{CameraMath.FeetToMm(v.HeightFeet):F0} mm 超出格子 {CameraMath.FeetToMm(v.CellMaxX - v.CellMinX):F0}×{CameraMath.FeetToMm(v.CellMaxY - v.CellMinY):F0} mm（已自動挑比例／縮放後仍超出，請手動調整）");
            Logger.Info($"相機圖紙：「{camName}」→ {res.SheetNumber} {res.SheetName}（{(res.Created ? "新建" : "更新")}；圖框 {res.TitleBlockName ?? "無"}［{res.TitleBlockSource}］；{res.DrawAreaSource}）；" +
                        string.Join("；", res.Viewports.Select(v => $"{v.Role}={v.Status}{(v.Overflow ? "(超出)" : "")}{(v.Note != null ? "：" + v.Note : "")}")));
            return res;
        }

        // ══════════════════════════════════════════════════════════
        // 2026-09-25 第二輪（MCP 自測圖紙）：標題欄判定＋依內容排版
        // ══════════════════════════════════════════════════════════

        private const double TitleStripFrameGapMm = 25.0;       // 離外框線 ≤ 25 mm 的長線視為圖框（內框），不是標題欄線
        private const double UndetectedFrameMarginFraction = 0.04; // 讀不到圖框線時四邊內縮＝max(設定邊界, 4 % 圖紙寬)
        private const double SheetFillRatio = 0.90;            // 透視圖填滿格子的比例
        private const double SheetFitRatio = 0.95;              // Key Plan 視圖挑比例時的格子可用比例（留標題空間）
        private static readonly int[] KeyPlanStandardScales = { 20, 25, 50, 75, 100, 150, 200 };
        private static readonly int[] KeyPlanExtendedScales = { 250, 300, 400, 500, 750, 1000 };

        private static double GetSheetReservedStripFraction()
        {
            double f = 0.20;
            try { f = ConfigManager.Instance.Settings.CameraManager.SheetReservedStripFraction; } catch { }
            return (double.IsNaN(f) || f < 0 || f > 0.5) ? 0.20 : f;
        }

        /// <summary>
        /// 分析圖框實例在圖紙視圖中的幾何找標題欄：
        ///   右側標題欄＝長度 ≥ 60% 圖框高的垂直線，位在圖框右側 40% 內、且不是最右側的外框／內框線（離最右長直線 &gt; 25 mm）→ 取最左者；
        ///   下方標題欄＝長度 ≥ 60% 圖框寬的水平線，位在圖框下方 35% 內、且離最下長橫線 &gt; 25 mm → 取最高者。
        /// 兩者都找不到、圖紙為橫式 → 保留右側 <see cref="CameraManagerSettings.SheetReservedStripFraction"/>（預設 20 %）。
        /// </summary>
        private static void DetectTitleStrip(Element tb, ViewSheet sheet, BoundingBoxXYZ bb, out double? stripLeftX, out double? stripTopY, out string log, out bool detected)
        {
            stripLeftX = null; stripTopY = null; detected = false;
            double W = bb.Max.X - bb.Min.X, H = bb.Max.Y - bb.Min.Y;
            var lines = new List<(XYZ a, XYZ b)>();
            // 2026-09-26 第三輪（MCP 自測：E1 30 x 42 讀到 0 條線）：依序試幾組 Options，取線最多的一組；
            // 每組都逐層展開 GeometryInstance（GetInstanceGeometry，空的話改 GetSymbolGeometry(gi.Transform)）、
            // 收 Line／PolyLine／實體邊，並統計各類幾何物件數寫進 log，下次讀不到時看得出原因。
            string optUsed = null, typeStats = null;
            var optionSets = new List<(string name, Func<Options> make)>
            {
                ("View=sheet", () => new Options { View = sheet, IncludeNonVisibleObjects = false }),
                ("View=sheet+NonVisible", () => new Options { View = sheet, IncludeNonVisibleObjects = true }),
                ("無 View/Fine", () => new Options { DetailLevel = ViewDetailLevel.Fine, IncludeNonVisibleObjects = true })
            };
            var tried = new List<string>();
            foreach (var (name, make) in optionSets)
            {
                try
                {
                    var tmp = new List<(XYZ a, XYZ b)>();
                    var stats = new Dictionary<string, int>(StringComparer.Ordinal);
                    GeometryElement ge = tb.get_Geometry(make());
                    CollectSheetLines(ge, tmp, 0, stats);
                    string st = stats.Count == 0 ? "(空)" : string.Join(",", stats.Select(kv => kv.Key + "×" + kv.Value));
                    tried.Add($"{name}:{tmp.Count}線[{st}]");
                    if (tmp.Count > lines.Count) { lines = tmp; optUsed = name; typeStats = st; }
                }
                catch (Exception ex) { tried.Add($"{name}:失敗 {ex.Message}"); }
            }
            if (lines.Count == 0) Logger.Info("相機圖紙：圖框幾何讀不到線，各 Options 結果＝" + string.Join("；", tried));

            double tolDir = 1e-3, frameGap = CameraMath.MmToFeet(TitleStripFrameGapMm);
            var vert = lines.Where(l => Math.Abs(l.a.X - l.b.X) < tolDir && Math.Abs(l.a.Y - l.b.Y) >= 0.6 * H).Select(l => (l.a.X + l.b.X) / 2).ToList();
            var horz = lines.Where(l => Math.Abs(l.a.Y - l.b.Y) < tolDir && Math.Abs(l.a.X - l.b.X) >= 0.6 * W).Select(l => (l.a.Y + l.b.Y) / 2).ToList();
            string found = $"圖框幾何線 {lines.Count} 條（長直 {vert.Count}、長橫 {horz.Count}；{(optUsed != null ? optUsed + " " + typeStats : "試過 " + string.Join("；", tried))}）";
            if (vert.Count > 0)
            {
                double rightFrame = vert.Max();
                var cand = vert.Where(x => x < rightFrame - frameGap && x >= bb.Min.X + 0.6 * W).ToList();
                if (cand.Count > 0) stripLeftX = cand.Min();
            }
            if (horz.Count > 0)
            {
                double bottomFrame = horz.Min();
                var cand = horz.Where(y => y > bottomFrame + frameGap && y <= bb.Min.Y + 0.35 * H).ToList();
                if (cand.Count > 0) stripTopY = cand.Max();
            }
            if (stripLeftX.HasValue || stripTopY.HasValue)
            {
                detected = true;
                log = found + "；標題欄＝" +
                      (stripLeftX.HasValue ? $"右側（x≥{CameraMath.FeetToMm(stripLeftX.Value - bb.Min.X):F0}mm，寬 {CameraMath.FeetToMm(bb.Max.X - stripLeftX.Value):F0}mm）" : "") +
                      (stripTopY.HasValue ? $"下方（y≤{CameraMath.FeetToMm(stripTopY.Value - bb.Min.Y):F0}mm）" : "");
                return;
            }
            if (W > H)
            {
                double f = GetSheetReservedStripFraction();
                stripLeftX = bb.Max.X - f * W;
                log = found + $"；未偵測到標題欄線 → 橫式圖紙保留右側 {f * 100:F0}%（{CameraMath.FeetToMm(f * W):F0}mm，可在設定 SheetReservedStripFraction 調整）";
                return;
            }
            log = found + "；未偵測到標題欄線（直式圖紙不保留）";
        }

        private static void CollectSheetLines(GeometryElement ge, List<(XYZ a, XYZ b)> lines, int depth, Dictionary<string, int> stats = null)
        {
            if (ge == null || depth > 6) return;
            void Count(string k) { if (stats != null) stats[k] = stats.TryGetValue(k, out int n) ? n + 1 : 1; }
            foreach (GeometryObject go in ge)
            {
                if (go == null) continue;
                Count(go.GetType().Name);
                if (go is Line ln && ln.IsBound) lines.Add((ln.GetEndPoint(0), ln.GetEndPoint(1)));
                else if (go is PolyLine pl)
                {
                    IList<XYZ> pts = pl.GetCoordinates();
                    for (int i = 0; i + 1 < pts.Count; i++) lines.Add((pts[i], pts[i + 1]));
                }
                else if (go is Solid so && so.Edges != null && so.Edges.Size > 0)
                {
                    // 填滿區域／遮罩在部分版本以薄實體回傳：收它的直線邊
                    foreach (Edge e in so.Edges)
                    {
                        try { if (e.AsCurve() is Line el && el.IsBound) lines.Add((el.GetEndPoint(0), el.GetEndPoint(1))); } catch { }
                    }
                }
                else if (go is GeometryInstance gi)
                {
                    GeometryElement inst = null;
                    try { inst = gi.GetInstanceGeometry(); } catch { inst = null; }
                    bool any = false;
                    if (inst != null) foreach (GeometryObject _ in inst) { any = true; break; }
                    if (!any)
                    {
                        try { inst = gi.GetSymbolGeometry(gi.Transform); Count("SymbolGeometryFallback"); } catch { inst = null; }
                    }
                    CollectSheetLines(inst, lines, depth + 1, stats);
                }
            }
        }

        private static (double w, double h) ViewportSize(Viewport vp)
        {
            Outline o = vp.GetBoxOutline();
            return (o.MaximumPoint.X - o.MinimumPoint.X, o.MaximumPoint.Y - o.MinimumPoint.Y);
        }

        /// <summary>依內容排版（須在交易內，viewport 已建立且 Regenerate 過）。</summary>
        private static void LayoutSheetViewports(Document doc, List<(string role, View view)> items, Dictionary<string, Viewport> vps,
            SheetResult res, double x0, double y0, double x1, double y1)
        {
            // 2026-09-26 第三輪（MCP 自測：透視圖無法以裁剪框放大 → 圖紙約 40 % 空白、Key Plan 被迫 1:75）：
            //   ① 先處理透視圖（出圖複本仍試放大裁剪框；FOV 會變就還原），讀回「實際視埠大小」，
            //      透視格＝實際寬 + 左右間距（夾在可繪寬 20–70 %），其餘寬度全給右側；
            //   ② 右側試幾種版面（單欄三列／上二下一／2×2），各自挑「放得進的最細比例」，取最粗比例最小、再比例和最小者；
            //   ③ 圖紙上透視圖大小只能在 Revit 內手動調（裁剪區域大小 → 縮放〔鎖定比例〕），見 spec §10.13。
            double gap = CameraMath.MmToFeet(SheetCellGapMm);
            var rightRoles = new[] { "Plan", "Section", "Iso" }.Where(r => vps.ContainsKey(r)).ToList();
            bool hasPersp = vps.ContainsKey("Perspective");
            double W = x1 - x0;
            var cells = new Dictionary<string, double[]>();
            string layoutLog = "";

            double splitX = hasPersp && rightRoles.Count > 0 ? x0 + W * SheetPerspectiveFraction
                          : hasPersp ? x1 + gap / 2 : x0 - gap / 2;
            if (hasPersp)
            {
                var (role, view) = items.First(i => i.role == "Perspective");
                Viewport vp = vps["Perspective"];
                SheetViewportInfo pinfo = res.Viewports.FirstOrDefault(i => i.Role == "Perspective");
                double cw = splitX - gap / 2 - x0, ch = y1 - y0;
                try
                {
                    if (view is View3D p3 && IsSheetPerspectiveCopy(p3))
                    {
                        var (w, h) = ViewportSize(vp);
                        if (w > 1e-9 && h > 1e-9)
                        {
                            double k = SheetFillRatio * Math.Min(cw / w, ch / h);
                            if (pinfo != null) pinfo.Note = EnlargePerspectiveCrop(doc, p3, k);
                        }
                    }
                    else if (pinfo != null) pinfo.Note = "原相機（不是出圖複本）：不改裁剪框大小";
                }
                catch (Exception ex) { Logger.Error("相機圖紙：透視圖尺寸調整失敗（照原尺寸放置）", ex); }
                doc.Regenerate();
                if (rightRoles.Count > 0)
                {
                    var (aw, ah) = ViewportSize(vp);
                    double want = aw + 2 * gap;
                    double pw = Math.Max(0.20 * W, Math.Min(0.70 * W, want));
                    splitX = x0 + pw;
                    layoutLog = $"透視視埠實際 {CameraMath.FeetToMm(aw):F0}×{CameraMath.FeetToMm(ah):F0}mm → 透視格寬 {CameraMath.FeetToMm(pw):F0}mm（可繪寬 {CameraMath.FeetToMm(W):F0}mm 的 {pw / W * 100:F0}%）";
                    if (aw > pw + 1e-9 || ah > ch + 1e-9) layoutLog += "（透視視埠比格子大：請在 Revit 以「裁剪區域大小→縮放（鎖定比例）」縮小）";
                    if (pinfo != null) pinfo.Note = (pinfo.Note != null ? pinfo.Note + "；" : "") + "圖紙上透視圖大小請在 Revit 內用「裁剪區域大小 → 縮放（鎖定比例）」手動調整";
                }
                cells["Perspective"] = new[] { x0, y0, splitX - gap / 2, y1 };
            }

            // 右側：各視圖「模型內容」大小（比例無關：外框尺寸 × 目前比例）。
            double rx0 = splitX + gap / 2;
            var model = new Dictionary<string, (double w, double h, int scale)>();
            foreach (string r in rightRoles)
            {
                View v = doc.GetElement(vps[r].ViewId) as View;
                int sc = 1;
                try { sc = Math.Max(1, v.Scale); } catch { }
                var (w, h) = ViewportSize(vps[r]);
                model[r] = (w * sc, h * sc, sc);
            }
            int PickScale(string r, double cw, double ch)
            {
                var m = model[r];
                foreach (int s in KeyPlanStandardScales.Concat(KeyPlanExtendedScales))
                    if (m.w / s <= cw * SheetFitRatio && m.h / s <= ch * SheetFitRatio) return s;
                return KeyPlanExtendedScales.Last();
            }
            var picks = new Dictionary<string, int>();
            if (rightRoles.Count > 0)
            {
                var candidates = BuildRightLayouts(rightRoles, model.ToDictionary(kv => kv.Key, kv => (kv.Value.w, kv.Value.h)), rx0, y0, x1, y1, gap);
                (string name, Dictionary<string, double[]> cells, Dictionary<string, int> picks, int max, int sum) best = default;
                var tried = new List<string>();
                foreach (var (name, lc) in candidates)
                {
                    var p = rightRoles.ToDictionary(r => r, r => PickScale(r, lc[r][2] - lc[r][0], lc[r][3] - lc[r][1]));
                    int mx = p.Values.Max(), sm = p.Values.Sum();
                    tried.Add($"{name}(" + string.Join("/", rightRoles.Select(r => "1:" + p[r])) + ")");
                    if (best.name == null || mx < best.max || (mx == best.max && sm < best.sum)) best = (name, lc, p, mx, sm);
                }
                foreach (var kv in best.cells) cells[kv.Key] = kv.Value;
                picks = best.picks;
                layoutLog += (layoutLog.Length > 0 ? "；" : "") + $"右側版面＝{best.name}（試過 {string.Join("、", tried)}）";
            }
            if (layoutLog.Length > 0)
            {
                res.DrawAreaSource = (res.DrawAreaSource ?? "") + "；" + layoutLog;
                Logger.Info("相機圖紙：" + layoutLog);
            }

            foreach (var (role, view) in items)
            {
                SheetViewportInfo info = res.Viewports.FirstOrDefault(i => i.Role == role);
                if (info == null || !vps.ContainsKey(role) || !cells.TryGetValue(role, out double[] c)) continue;
                info.CellMinX = c[0]; info.CellMinY = c[1]; info.CellMaxX = c[2]; info.CellMaxY = c[3];
                if (role == "Perspective") continue;
                try
                {
                    var m = model[role];
                    int pick = picks.TryGetValue(role, out int pp) ? pp : PickScale(role, c[2] - c[0], c[3] - c[1]);
                    if (pick != m.scale)
                    {
                        try { view.Scale = pick; info.Note = $"比例 1:{m.scale} → 1:{pick}"; }
                        catch (Exception ex) { info.Note = $"改比例 1:{pick} 失敗（{ex.Message}；可能是視圖樣板控制比例）"; res.Warnings.Add($"{view.Name}：{info.Note}"); }
                    }
                    else info.Note = $"比例 1:{pick}（不變）";
                    if (pick > KeyPlanStandardScales.Last()) res.Warnings.Add($"{view.Name}：標準比例 1:20–1:200 都放不進格子，改用 1:{pick}");
                }
                catch (Exception ex) { Logger.Error($"相機圖紙：{role} 尺寸調整失敗（照原尺寸放置）", ex); }
            }
            doc.Regenerate();
            foreach (var kv in vps)
            {
                if (!cells.TryGetValue(kv.Key, out double[] c)) continue;
                try { kv.Value.SetBoxCenter(new XYZ((c[0] + c[2]) / 2, (c[1] + c[3]) / 2, 0)); }
                catch (Exception ex) { Logger.Error("相機圖紙：視埠置中失敗", ex); }
            }
            doc.Regenerate();
        }

        /// <summary>
        /// 2026-09-26 第三輪：右側 Key Plan 版面候選（純計算，不碰 Revit）。
        ///   單欄：由上而下，列高依內容長寬比（夾 0.25–4）分配（沿用舊規則）；
        ///   上二下一（3 張時）：上列兩格＋下列一格橫跨全寬；下列放「最扁」（寬/高最大）者，上下列高試 40/50/60 %；
        ///   2×2（3 張時）：四等分，依序放左上／右上／左下（右下留白）；
        ///   並排（2 張時）：左右兩格。
        /// </summary>
        private static List<(string name, Dictionary<string, double[]> cells)> BuildRightLayouts(List<string> roles,
            Dictionary<string, (double w, double h)> model, double rx0, double y0, double x1, double y1, double gap)
        {
            var list = new List<(string, Dictionary<string, double[]>)>();
            double rw = x1 - rx0, rh = y1 - y0;
            if (rw <= 0 || rh <= 0) return list;

            // 單欄
            {
                var c = new Dictionary<string, double[]>();
                double avail = rh - gap * (roles.Count - 1);
                var weight = roles.ToDictionary(r => r, r =>
                {
                    var m = model[r];
                    double a = m.w > 1e-9 ? m.h / m.w : 1.0;
                    return Math.Max(0.25, Math.Min(4.0, a));
                });
                double sum = weight.Values.Sum(), top = y1;
                foreach (string r in roles)
                {
                    double hRow = avail * weight[r] / sum;
                    c[r] = new[] { rx0, top - hRow, x1, top };
                    top -= hRow + gap;
                }
                list.Add(("單欄", c));
            }
            double midX = rx0 + rw / 2;
            if (roles.Count == 3)
            {
                string flat = roles.OrderByDescending(r => model[r].h > 1e-9 ? model[r].w / model[r].h : 1.0).First();
                var upper = roles.Where(r => r != flat).ToList();
                foreach (double f in new[] { 0.4, 0.5, 0.6 })
                {
                    double splitY = y1 - rh * f;
                    var c = new Dictionary<string, double[]>
                    {
                        [upper[0]] = new[] { rx0, splitY + gap / 2, midX - gap / 2, y1 },
                        [upper[1]] = new[] { midX + gap / 2, splitY + gap / 2, x1, y1 },
                        [flat] = new[] { rx0, y0, x1, splitY - gap / 2 }
                    };
                    list.Add(($"上二下一({f * 100:F0}%)", c));
                }
                double midY = y0 + rh / 2;
                list.Add(("2×2", new Dictionary<string, double[]>
                {
                    [roles[0]] = new[] { rx0, midY + gap / 2, midX - gap / 2, y1 },
                    [roles[1]] = new[] { midX + gap / 2, midY + gap / 2, x1, y1 },
                    [roles[2]] = new[] { rx0, y0, midX - gap / 2, midY - gap / 2 }
                }));
            }
            else if (roles.Count == 2)
            {
                list.Add(("並排", new Dictionary<string, double[]>
                {
                    [roles[0]] = new[] { rx0, y0, midX - gap / 2, y1 },
                    [roles[1]] = new[] { midX + gap / 2, y0, x1, y1 }
                }));
            }
            return list;
        }

        /// <summary>
        /// 出圖用透視複本：裁剪框等比放大 k 倍讓視埠變大，視野角不變。依序嘗試：
        ///   ① X/Y/Z 一起乘 k（以裁剪框座標原點＝眼睛為中心；角錐相似 → 角度不變）；② 只乘 X/Y。
        /// 每次套用後 Regenerate、以 ComputePerspectiveFov 讀回比對水平／垂直 FOV 與四個邊界角（容許 0.2°），
        /// 都不符 → 還原原裁剪框並回報。回傳說明文字。須在交易內。
        /// </summary>
        private static string EnlargePerspectiveCrop(Document doc, View3D view, double k)
        {
            if (!(k > 0) || Math.Abs(k - 1.0) < 0.01) return $"透視複本裁剪框不需縮放（k={k:F3}）";
            CameraMath.PerspectiveFov f0 = CameraMath.ComputePerspectiveFov(view);
            BoundingBoxXYZ orig = view.CropBox;
            string Fov(CameraMath.PerspectiveFov f) => $"h={f.HFovDeg:F2}° v={f.VFovDeg:F2}° L={f.LeftDeg:F2} R={f.RightDeg:F2} D={f.DownDeg:F2} U={f.UpDeg:F2}";
            bool Same(CameraMath.PerspectiveFov a, CameraMath.PerspectiveFov b)
            {
                bool Eq(double? x, double? y) => x.HasValue && y.HasValue && Math.Abs(x.Value - y.Value) <= 0.2;
                return Eq(a.HFovDeg, b.HFovDeg) && Eq(a.VFovDeg, b.VFovDeg) && Eq(a.LeftDeg, b.LeftDeg) && Eq(a.RightDeg, b.RightDeg)
                       && Eq(a.DownDeg, b.DownDeg) && Eq(a.UpDeg, b.UpDeg);
            }
            if (!f0.HFovDeg.HasValue) return "讀不到透視複本 FOV，不縮放裁剪框";
            var variants = new List<(string name, BoundingBoxXYZ box)>
            {
                ("XYZ×k", new BoundingBoxXYZ { Transform = orig.Transform, Min = orig.Min * k, Max = orig.Max * k }),
                ("XY×k", new BoundingBoxXYZ { Transform = orig.Transform, Min = new XYZ(orig.Min.X * k, orig.Min.Y * k, orig.Min.Z), Max = new XYZ(orig.Max.X * k, orig.Max.Y * k, orig.Max.Z) })
            };
            var tried = new List<string>();
            foreach (var (name, box) in variants)
            {
                try
                {
                    view.CropBox = box;
                    doc.Regenerate();
                    CameraMath.PerspectiveFov f1 = CameraMath.ComputePerspectiveFov(view);
                    if (Same(f0, f1))
                    {
                        string msg = $"透視複本裁剪框 {name} k={k:F3}，FOV 不變（{Fov(f1)}）";
                        Logger.Info("相機圖紙：" + msg);
                        return msg;
                    }
                    tried.Add($"{name} → {Fov(f1)}");
                }
                catch (Exception ex) { tried.Add($"{name} 失敗：{ex.Message}"); }
            }
            try { view.CropBox = orig; doc.Regenerate(); } catch (Exception ex) { Logger.Error("相機圖紙：還原透視複本裁剪框失敗", ex); }
            string fail = $"透視複本放大裁剪框後 FOV 會改變，已還原（原 {Fov(f0)}；試過 {string.Join("；", tried)}）";
            Logger.Error("相機圖紙：" + fail);
            return fail;
        }

        private static double GetSheetMarginMm()
        {
            double m = 15.0;
            try { m = ConfigManager.Instance.Settings.CameraManager.SheetMarginMm; } catch { }
            return (double.IsNaN(m) || double.IsInfinity(m) || m < 0 || m > 100) ? 15.0 : m;
        }

        private static Element FirstTitleBlockOn(Document doc, ViewSheet sheet) =>
            new FilteredElementCollector(doc, sheet.Id).OfCategory(BuiltInCategory.OST_TitleBlocks).WhereElementIsNotElementType().FirstElement();

        private static void FillViewportGeometry(Document doc, Viewport vp, SheetViewportInfo info)
        {
            try
            {
                XYZ ctr = vp.GetBoxCenter();
                Outline o = vp.GetBoxOutline();
                info.CenterXFeet = ctr.X; info.CenterYFeet = ctr.Y;
                info.WidthFeet = o.MaximumPoint.X - o.MinimumPoint.X;
                info.HeightFeet = o.MaximumPoint.Y - o.MinimumPoint.Y;
                double tol = CameraMath.MmToFeet(0.5);
                info.Overflow = info.WidthFeet > (info.CellMaxX - info.CellMinX) + tol || info.HeightFeet > (info.CellMaxY - info.CellMinY) + tol;
                info.ViewportTypeName = doc.GetElement(vp.GetTypeId())?.Name;
            }
            catch (Exception ex) { Logger.Error("相機圖紙：讀取視埠範圍失敗", ex); }
        }
    }
}
