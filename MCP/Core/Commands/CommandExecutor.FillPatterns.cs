#if REVIT2025_OR_GREATER
using IdType = System.Int64;
#else
using IdType = System.Int32;
#endif
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;

namespace RevitMCP.Core
{
    public partial class CommandExecutor
    {
        /// <summary>
        /// 將選定的填滿範圍從製圖樣式轉換為模型樣式
        /// </summary>
        private object ConvertDraftingToModelPattern()
        {
            Document doc = _uiApp.ActiveUIDocument.Document;
            UIDocument uidoc = _uiApp.ActiveUIDocument;
            var selectedIds = uidoc.Selection.GetElementIds();
            
            if (selectedIds.Count == 0)
            {
                throw new Exception("請先選取要轉換的填滿範圍 (Filled Region)。");
            }

            int viewScale = doc.ActiveView.Scale;
            int convertedCount = 0;
            var convertedIds = new List<long>();

            using (Transaction t = new Transaction(doc, "轉換為模型樣式"))
            {
                t.Start();
                try
                {
                    foreach (ElementId id in selectedIds)
                    {
                        Element elem = doc.GetElement(id);
                        if (!(elem is FilledRegion filledRegion)) 
                        {
                            Logger.Info($"跳過非填滿範圍元素: {id}");
                            continue;
                        }
                        
                        Logger.Info($"正在處理填滿範圍: {id}");
                        FilledRegionType oldType = doc.GetElement(filledRegion.GetTypeId()) as FilledRegionType;
                        
                        if (oldType == null)
                        {
                            Logger.Error($"找不到填滿範圍類型: {filledRegion.GetTypeId()}");
                            continue;
                        }

                        // Revit 2018+ 優先使用 ForegroundPatternId
                        ElementId patId = oldType.ForegroundPatternId;
                        if (patId == ElementId.InvalidElementId)
                        {
                            Logger.Info($"類型 {oldType.Name} 沒有前景樣式，跳過。");
                            continue;
                        }

                        FillPatternElement oldPatElem = doc.GetElement(patId) as FillPatternElement;
                        if (oldPatElem == null)
                        {
                            Logger.Error($"找不到樣式元素: {patId}");
                            continue;
                        }

                        FillPattern oldFp = oldPatElem.GetFillPattern();
                        if (oldFp == null)
                        {
                            Logger.Error($"樣式元素 {patId} 沒有有效的 FillPattern");
                            continue;
                        }

                        if (oldFp.Target == FillPatternTarget.Model)
                        {
                            Logger.Info($"樣式 {oldFp.Name} 已經是模型樣式，跳過。");
                            continue;
                        }

                        // 1. 處理樣式放大
                        FillPatternElement newPatElem = GetOrCreateScaledModelPattern(doc, oldPatElem, viewScale);
                        if (newPatElem == null) continue;

                        // 2. 處理類型複製
                        string newTypeName = $"{oldType.Name} - 模型 (1_{viewScale})";
                        FilledRegionType newType = new FilteredElementCollector(doc)
                            .OfClass(typeof(FilledRegionType))
                            .Cast<FilledRegionType>()
                            .FirstOrDefault(x => x.Name == newTypeName);

                        if (newType == null)
                        {
                            newType = oldType.Duplicate(newTypeName) as FilledRegionType;
                            newType.ForegroundPatternId = newPatElem.Id;
                        }

                        // 3. 原地替換
                        filledRegion.ChangeTypeId(newType.Id);
                        convertedCount++;
                        convertedIds.Add(id.GetIdValue());
                        Logger.Info($"成功轉換: {id} -> {newType.Name}");
                    }
                    t.Commit();
                }
                catch (Exception ex)
                {
                    Logger.Error("轉換過程中發生未預期錯誤", ex);
                    if (t.HasStarted()) t.RollBack();
                    throw;
                }
            }

            return new
            {
                Message = $"已成功將 {convertedCount} 個填滿範圍轉換為符合當前比例縮放的模型樣式。",
                ConvertedCount = convertedCount,
                ConvertedIds = convertedIds
            };
        }

        /// <summary>
        /// 自動掃描並將所有被旋轉（在圖紙上）的剖面圖內的製圖樣式轉換為模型樣式
        /// </summary>
        private object AutoConvertRotatedViewportPatterns()
        {
            Document doc = _uiApp.ActiveUIDocument.Document;
            int totalConvertedCount = 0;
            int skippedGroupCount = 0;
            var processedViewIds = new HashSet<ElementId>();
            var convertedIds = new List<long>();

            // 1. 取得所有視埠 (Viewports)
            var viewports = new FilteredElementCollector(doc)
                .OfClass(typeof(Viewport))
                .Cast<Viewport>()
                .ToList();

            using (Transaction t = new Transaction(doc, "自動轉換旋轉剖面圖填滿樣式"))
            {
                t.Start();
                try
                {
                    foreach (var vp in viewports)
                    {
                        Parameter rotParam = vp.get_Parameter(BuiltInParameter.VIEWPORT_ATTR_ORIENTATION_ON_SHEET);
                        bool isRotated = vp.Rotation != ViewportRotation.None;
                        if (!isRotated && rotParam != null && rotParam.AsInteger() != 0)
                        {
                            isRotated = true;
                        }

                        // 檢查是否在圖紙上有旋轉
                        if (!isRotated) 
                            continue;

                        ElementId viewId = vp.ViewId;
                        
                        // 避免重複處理同一個視圖
                        if (processedViewIds.Contains(viewId)) 
                            continue;
                            
                        processedViewIds.Add(viewId);

                        View view = doc.GetElement(viewId) as View;
                        if (view == null) 
                            continue;

                        // 依需求：僅針對「剖面圖 (Section)」或「詳圖 (Detail)」
                        if (view.ViewType != ViewType.Section && view.ViewType != ViewType.Detail)
                            continue;

                        int viewScale = view.Scale;

                        // 找出該視圖中所有的 FilledRegion
                        var filledRegions = new FilteredElementCollector(doc, viewId)
                            .OfClass(typeof(FilledRegion))
                            .Cast<FilledRegion>()
                            .ToList();

                        foreach (var fr in filledRegions)
                        {
                            // 根據使用者需求：略過群組內的物件，避免強制解除群組
                            if (fr.GroupId != ElementId.InvalidElementId)
                            {
                                skippedGroupCount++;
                                Logger.Info($"[Auto] 略過群組中的填滿範圍: {fr.Id} (視圖: {view.Name})");
                                continue;
                            }

                            FilledRegionType oldType = doc.GetElement(fr.GetTypeId()) as FilledRegionType;
                            if (oldType == null) continue;

                            // Revit 2018+ 優先使用 ForegroundPatternId
                            ElementId patId = oldType.ForegroundPatternId;
                            if (patId == ElementId.InvalidElementId) continue;

                            FillPatternElement oldPatElem = doc.GetElement(patId) as FillPatternElement;
                            if (oldPatElem == null) continue;

                            FillPattern oldFp = oldPatElem.GetFillPattern();
                            // 如果已經是模型樣式，就跳過
                            if (oldFp == null || oldFp.Target == FillPatternTarget.Model) continue;

                            // 自動將製圖樣式轉為模型樣式
                            FillPatternElement newPatElem = GetOrCreateScaledModelPattern(doc, oldPatElem, viewScale);
                            if (newPatElem == null) continue;

                            string newTypeName = $"{oldType.Name} - 模型 (1_{viewScale})";
                            FilledRegionType newType = new FilteredElementCollector(doc)
                                .OfClass(typeof(FilledRegionType))
                                .Cast<FilledRegionType>()
                                .FirstOrDefault(x => x.Name == newTypeName);

                            if (newType == null)
                            {
                                newType = oldType.Duplicate(newTypeName) as FilledRegionType;
                                newType.ForegroundPatternId = newPatElem.Id;
                            }

                            // 執行選取替換
                            fr.ChangeTypeId(newType.Id);
                            totalConvertedCount++;
                            convertedIds.Add(fr.Id.GetIdValue());
                        }
                    }
                    t.Commit();
                }
                catch (Exception ex)
                {
                    Logger.Error("自動轉換旋轉視埠填滿樣式時發生錯誤", ex);
                    if (t.HasStarted()) t.RollBack();
                    throw;
                }
            }

            return new
            {
                Message = $"自動掃描完成。共檢查了 {processedViewIds.Count} 個旋轉的剖面圖，成功轉換了 {totalConvertedCount} 個填滿範圍，並因群組限制而略過了 {skippedGroupCount} 個物件。",
                ProcessedViewsCount = processedViewIds.Count,
                ConvertedCount = totalConvertedCount,
                SkippedGroupCount = skippedGroupCount,
                ConvertedIds = convertedIds
            };
        }

        /// <summary>
        /// 診斷工具：回傳所有視埠的旋轉狀態與屬性，協助排除遺漏問題
        /// </summary>
        private object CheckViewportsRotation()
        {
            Document doc = _uiApp.ActiveUIDocument.Document;
            var viewports = new FilteredElementCollector(doc)
                .OfClass(typeof(Viewport))
                .Cast<Viewport>()
                .ToList();

            var results = new List<object>();

            foreach (var vp in viewports)
            {
                View view = doc.GetElement(vp.ViewId) as View;
                Parameter rotParam = vp.get_Parameter(BuiltInParameter.VIEWPORT_ATTR_ORIENTATION_ON_SHEET);
                
                results.Add(new
                {
                    ViewportId = vp.Id.GetIdValue(),
                    ViewId = vp.ViewId.GetIdValue(),
                    ViewName = view?.Name,
                    ViewType = view?.ViewType.ToString(),
                    RawRotationEnum = vp.Rotation.ToString(),
                    ParamName = rotParam?.Definition?.Name,
                    ParamValue = rotParam?.AsValueString(),
                    ParamInt = rotParam?.AsInteger()
                });
            }

            return results;
        }

        // 供上方呼叫的輔助方法：計算轉換與建立 FillPattern
        private FillPatternElement GetOrCreateScaledModelPattern(Document doc, FillPatternElement oldPatElem, int scale)
        {
            FillPattern oldFp = oldPatElem.GetFillPattern();
            string newPatName = $"{oldFp.Name} - 模型 (1_{scale})";

            FillPatternElement existing = new FilteredElementCollector(doc)
                .OfClass(typeof(FillPatternElement))
                .Cast<FillPatternElement>()
                .FirstOrDefault(x => x.Name == newPatName);

            if (existing != null) return existing;

            // 0 = ToModel / ToModelFace, 1 = ToView / ToViewFace
            FillPattern newFp = new FillPattern(newPatName, FillPatternTarget.Model, (FillPatternHostOrientation)0);
            var oldGrids = oldFp.GetFillGrids();
            var newGrids = new List<FillGrid>();

            foreach (var grid in oldGrids)
            {
                FillGrid newGrid = new FillGrid()
                {
                    Angle = grid.Angle,
                    Origin = grid.Origin,
                    // 核心：將間距與位移乘上視圖比例尺
                    Offset = grid.Offset * scale,
                    Shift = grid.Shift * scale
                };

                var segs = grid.GetSegments();
                if (segs != null && segs.Count > 0)
                {
                    newGrid.SetSegments(segs.Select(s => s * scale).ToList());
                }
                newGrids.Add(newGrid);
            }

            newFp.SetFillGrids(newGrids);
            return FillPatternElement.Create(doc, newFp);
        }

                private object CreateRCFilledRegion(JObject parameters)
        {
            Logger.Info("[RC\u8cbc\u7d19] \u9032\u5165 CreateRCFilledRegion...");
            try
            {
                Document doc = _uiApp.ActiveUIDocument?.Document;
                View activeView = doc?.ActiveView;
                if (activeView == null) throw new Exception("\u7121\u6cd5\u53d6\u5f97\u73fe\u7528\u8996\u5716\u3002");

                string filledRegionTypeName = parameters["filledRegionTypeName"]?.ToString() ?? "\u6df1\u7070\u8272";

                FilledRegionType regionType = new FilteredElementCollector(doc)
                    .OfClass(typeof(FilledRegionType)).Cast<FilledRegionType>()
                    .FirstOrDefault(x => x.Name != null && x.Name.Equals(filledRegionTypeName, StringComparison.OrdinalIgnoreCase));

                if (regionType == null) throw new Exception($"\u627e\u4e0d\u5230\u586b\u6eff\u6a23\u5f0f: {filledRegionTypeName}");

                ElementId invisibleLineStyleId = ElementId.InvalidElementId;
                foreach (var gs in new FilteredElementCollector(doc).OfClass(typeof(GraphicsStyle)).Cast<GraphicsStyle>())
                {
                    if (gs.GraphicsStyleCategory == null || string.IsNullOrEmpty(gs.Name)) continue;
                    if (gs.Name.Contains("\u4e0d\u53ef\u898b") || gs.Name.ToLower().Contains("invisible"))
                    {
                        invisibleLineStyleId = gs.Id;
                        break;
                    }
                }

                int createdCount = 0;
                List<IdType> generatedIds;
                using (Transaction t = new Transaction(doc, "\u7522\u751f RC \u586b\u6eff\u5340\u57df"))
                {
                    t.Start();
                    createdCount = Internal_CreateRCFilledRegionInView(doc, activeView, regionType, invisibleLineStyleId, out generatedIds);
                    t.Commit();
                }

                return new { Message = $"\u57f7\u884c\u5b8c\u6210\u3002\u6210\u529f\u7522\u751f {createdCount} \u500b\u586b\u6eff\u8cbc\u7d19\u3002", CreatedCount = createdCount, CreatedFilledRegionIds = generatedIds };
            }
            catch (Exception ex)
            {
                Logger.Error("[RC\u8cbc\u7d19] \u5168\u57df\u932f\u8aa4", ex);
                throw;
            }
        }

        // ==================== RC 智慧更新輔助方法 ====================

        /// <summary>
        /// 計算 CurveLoop 集合的幾何指紋 (重心X, 重心Y, 總面積)，用於變更偵測
        /// </summary>
        private string RC_ComputeFingerprint(IList<CurveLoop> loops)
        {
            double totalArea = 0;
            double cx = 0, cy = 0;
            int ptCount = 0;

            foreach (var loop in loops)
            {
                foreach (Curve c in loop)
                {
                    XYZ p = c.GetEndPoint(0);
                    cx += p.X; cy += p.Y;
                    ptCount++;
                }
            }

            if (ptCount > 0) { cx /= ptCount; cy /= ptCount; }

            // 面積用外圍迴圈近似（Shoelace）
            if (loops.Count > 0)
            {
                var outerLoop = loops[0];
                var pts = new List<XYZ>();
                foreach (Curve c in outerLoop) pts.Add(c.GetEndPoint(0));
                double area = 0;
                for (int i = 0; i < pts.Count; i++)
                {
                    int j = (i + 1) % pts.Count;
                    area += pts[i].X * pts[j].Y;
                    area -= pts[j].X * pts[i].Y;
                }
                totalArea = Math.Abs(area) / 2.0;
            }

            // 精度到小數 3 位 (約 0.3mm)
            return $"{Math.Round(cx, 3)},{Math.Round(cy, 3)},{Math.Round(totalArea, 3)}";
        }

        /// <summary>
        /// 計算已有 FilledRegion 的幾何指紋
        /// </summary>
        private string RC_ComputeFilledRegionFingerprint(FilledRegion fr)
        {
            try
            {
                var loops = fr.GetBoundaries();
                if (loops == null || loops.Count == 0) return "";
                return RC_ComputeFingerprint(loops);
            }
            catch { return ""; }
        }

        private const string RC_AUTO_TAG = "RC_AUTO";

        /// <summary>
        /// 取得視圖中所有帶有 RC_AUTO 標記且屬於指定類型的 FilledRegion
        /// </summary>
        private List<FilledRegion> RC_GetExistingAutoRegions(Document doc, View view, ElementId regionTypeId)
        {
            return new FilteredElementCollector(doc, view.Id)
                .OfClass(typeof(FilledRegion))
                .Cast<FilledRegion>()
                .Where(fr =>
                {
                    if (fr.GetTypeId() != regionTypeId) return false;
                    var commentParam = fr.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                    return commentParam != null && commentParam.AsString() == RC_AUTO_TAG;
                })
                .ToList();
        }

        // ==================== 核心邏輯 ====================

        /// <summary>
        /// 計算視圖中所有 RC 元素的剖切面 CurveLoop，傳回 (loops, fingerprint) 列表
        /// </summary>
        private List<(IList<CurveLoop> Loops, string Fingerprint)> RC_CollectCutFaces(Document doc, View view)
        {
            var result = new List<(IList<CurveLoop>, string)>();

            var catList = new List<BuiltInCategory> {
                BuiltInCategory.OST_Walls,
                BuiltInCategory.OST_Floors,
                BuiltInCategory.OST_StructuralColumns,
                BuiltInCategory.OST_StructuralFraming
            };

            var rcElements = new FilteredElementCollector(doc, view.Id)
                .WherePasses(new ElementMulticategoryFilter(catList))
                .WhereElementIsNotElementType()
                .ToList();

            var rcKeywords = new[] { "rc", "混凝土", "concrete" };
            var filteredElements = rcElements.Where(elem =>
            {
                if (elem == null) return false;
                ElementId typeId = elem.GetTypeId();
                if (typeId == ElementId.InvalidElementId) return false;
                Element typeElem = doc.GetElement(typeId);
                if (typeElem == null) return false;
                string typeName = (typeElem.Name ?? "").ToLower();
                return rcKeywords.Any(kw => typeName.Contains(kw));
            }).ToList();

            XYZ viewDir = view.ViewDirection;
            Options geomOptions = new Options { View = view, ComputeReferences = true };

            foreach (Element elem in filteredElements)
            {
                if (elem == null) continue;
                try
                {
                    var solids = RC_ExtractSolids(elem.get_Geometry(geomOptions));
                    bool gotOne = false;

                    foreach (Solid solid in solids)
                    {
                        if (gotOne) break;
                        if (solid == null || solid.Faces == null || solid.Faces.Size == 0) continue;

                        foreach (Face face in solid.Faces)
                        {
                            if (gotOne) break;
                            PlanarFace pf = face as PlanarFace;
                            if (pf == null) continue;

                            double dot = Math.Abs(pf.FaceNormal.DotProduct(viewDir));
                            if (dot < 0.98) continue;

                            double distToFacePlane = Math.Abs((view.Origin - pf.Origin).DotProduct(pf.FaceNormal));
                            const double CUT_PLANE_TOLERANCE = 0.1;
                            if (distToFacePlane > CUT_PLANE_TOLERANCE) continue;

                            IList<CurveLoop> loops = pf.GetEdgesAsCurveLoops();
                            if (loops == null || loops.Count == 0) continue;

                            var validLoops = loops.Where(l => l != null && !l.IsOpen()).ToList();
                            if (validLoops.Count == 0) continue;

                            string fp = RC_ComputeFingerprint(validLoops);
                            result.Add((validLoops, fp));
                            gotOne = true;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error($"處理視圖 {view.Name} 中的元素 {elem.Id} 失敗", ex);
                }
            }
            return result;
        }

        /// <summary>
        /// 核心邏輯：在指定視圖中智慧產生/更新 RC 填滿區域
        /// 回傳 (created, deleted, skipped) 數量
        /// </summary>
        private (int Created, int Deleted, bool Skipped) Internal_SmartCreateRCFilledRegionInView(
            Document doc, View view, FilledRegionType regionType, ElementId invisibleLineStyleId, out List<IdType> generatedIds)
        {
            generatedIds = new List<IdType>();

            // 1. 收集目前模型的 RC 剖切面
            var newFaces = RC_CollectCutFaces(doc, view);
            var newFingerprints = new HashSet<string>(newFaces.Select(f => f.Fingerprint));

            // 2. 收集現有的 RC_AUTO FilledRegion
            var existingRegions = RC_GetExistingAutoRegions(doc, view, regionType.Id);
            var existingFingerprints = new HashSet<string>(
                existingRegions.Select(fr => RC_ComputeFilledRegionFingerprint(fr)));

            // 3. 比較指紋集合
            bool identical = newFingerprints.Count == existingFingerprints.Count
                          && newFingerprints.SetEquals(existingFingerprints);

            if (identical)
            {
                // 完全吻合 → 跳過
                Logger.Info($"[RC貼紙] 視圖 '{view.Name}' 無變更，跳過。");
                return (0, 0, true);
            }

            // 4. 有變更 → 刪除舊的
            int deletedCount = existingRegions.Count;
            foreach (var fr in existingRegions)
            {
                doc.Delete(fr.Id);
            }

            // 5. 建立新的
            int createdCount = 0;
            foreach (var (loops, fp) in newFaces)
            {
                try
                {
                    FilledRegion fr = FilledRegion.Create(doc, regionType.Id, view.Id, loops);
                    if (fr != null)
                    {
                        if (invisibleLineStyleId != ElementId.InvalidElementId)
                            fr.SetLineStyleId(invisibleLineStyleId);

                        // 標記為自動產生
                        var commentParam = fr.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                        if (commentParam != null && !commentParam.IsReadOnly)
                            commentParam.Set(RC_AUTO_TAG);

                        generatedIds.Add(fr.Id.GetIdValue());
                        createdCount++;
                    }
                }
                catch { }
            }

            Logger.Info($"[RC貼紙] 視圖 '{view.Name}': 刪除 {deletedCount} 舊 → 建立 {createdCount} 新");
            return (createdCount, deletedCount, false);
        }

        /// <summary>
        /// 保留舊版介面 (不含智慧更新)
        /// </summary>
        private int Internal_CreateRCFilledRegionInView(Document doc, View view, FilledRegionType regionType, ElementId invisibleLineStyleId, out List<IdType> generatedIds)
        {
            List<IdType> ids;
            var (created, _, _) = Internal_SmartCreateRCFilledRegionInView(doc, view, regionType, invisibleLineStyleId, out ids);
            generatedIds = ids;
            return created;
        }

        private object BatchCreateRCFilledRegions(JObject parameters)
        {
            Logger.Info("[RC貼紙] 進入 BatchCreateRCFilledRegions (智慧更新模式)...");
            try
            {
                Document doc = _uiApp.ActiveUIDocument?.Document;
                if (doc == null) throw new Exception("無法取得現用文件。");

                var viewIdList = parameters["viewIds"]?.ToObject<List<IdType>>() ?? new List<IdType>();
                var sheetNumbers = parameters["sheetNumbers"]?.ToObject<List<string>>();
                string filledRegionTypeName = parameters["filledRegionTypeName"]?.ToString() ?? "深灰色";

                // 如果有提供 sheetNumbers，則加入這些圖紙上的視圖
                if (sheetNumbers != null && sheetNumbers.Count > 0)
                {
                    var sheets = new FilteredElementCollector(doc)
                        .OfClass(typeof(ViewSheet))
                        .Cast<ViewSheet>()
                        .Where(s => sheetNumbers.Contains(s.SheetNumber))
                        .ToList();

                    foreach (var sheet in sheets)
                    {
                        foreach (ElementId vpId in sheet.GetAllViewports())
                        {
                            Viewport vp = doc.GetElement(vpId) as Viewport;
                            if (vp != null)
                            {
                                IdType vid = vp.ViewId.GetIdValue();
                                if (!viewIdList.Contains(vid))
                                    viewIdList.Add(vid);
                            }
                        }
                    }
                }

                if (viewIdList.Count == 0) throw new Exception("未指定視圖 ID 且找不到指定圖紙上的視圖。");

                FilledRegionType regionType = new FilteredElementCollector(doc)
                    .OfClass(typeof(FilledRegionType)).Cast<FilledRegionType>()
                    .FirstOrDefault(x => x.Name != null && x.Name.Equals(filledRegionTypeName, StringComparison.OrdinalIgnoreCase));

                if (regionType == null) throw new Exception($"找不到填滿樣式: {filledRegionTypeName}");

                ElementId invisibleLineStyleId = ElementId.InvalidElementId;
                foreach (var gs in new FilteredElementCollector(doc).OfClass(typeof(GraphicsStyle)).Cast<GraphicsStyle>())
                {
                    if (gs.GraphicsStyleCategory == null || string.IsNullOrEmpty(gs.Name)) continue;
                    if (gs.Name.Contains("不可見") || gs.Name.ToLower().Contains("invisible"))
                    {
                        invisibleLineStyleId = gs.Id;
                        break;
                    }
                }

                int totalCreated = 0;
                int totalDeleted = 0;
                int totalSkipped = 0;
                var results = new List<object>();

                foreach (IdType vid in viewIdList)
                {
                    ElementId vId = new ElementId(vid);
                    View view = doc.GetElement(vId) as View;
                    if (view == null) continue;

                    using (Transaction t = new Transaction(doc, $"Batch RC 貼紙 - {view.Name}"))
                    {
                        t.Start();
                        List<IdType> ids;
                        var (created, deleted, skipped) = Internal_SmartCreateRCFilledRegionInView(
                            doc, view, regionType, invisibleLineStyleId, out ids);

                        totalCreated += created;
                        totalDeleted += deleted;
                        if (skipped) totalSkipped++;

                        string status = skipped ? "unchanged" : (deleted > 0 ? "updated" : "new");
                        results.Add(new {
                            ViewName = view.Name, ViewId = vid,
                            Status = status,
                            DeletedCount = deleted, CreatedCount = created,
                            Ids = ids
                        });
                        t.Commit();
                    }
                }

                string msg = $"批量執行完成。建立 {totalCreated} / 刪除 {totalDeleted} / 跳過 {totalSkipped} 個視圖。";
                return new { Message = msg, TotalCreated = totalCreated, TotalDeleted = totalDeleted, TotalSkipped = totalSkipped, Results = results };
            }
            catch (Exception ex)
            {
                Logger.Error("批量 RC 貼紙全域錯誤", ex);
                throw;
            }
        }

        private List<Solid> RC_ExtractSolids(GeometryElement geoElem)
        {
            var result = new List<Solid>();
            if (geoElem == null) return result;
            foreach (GeometryObject obj in geoElem)
            {
                if (obj is Solid s && s.Faces != null && s.Faces.Size > 0)
                    result.Add(s);
                else if (obj is GeometryInstance gi)
                {
                    GeometryElement instGeom = gi.GetInstanceGeometry();
                    if (instGeom != null)
                        foreach (GeometryObject io in instGeom)
                            if (io is Solid iSolid && iSolid.Faces != null && iSolid.Faces.Size > 0)
                                result.Add(iSolid);
                }
            }
            return result;
        }
    }
}