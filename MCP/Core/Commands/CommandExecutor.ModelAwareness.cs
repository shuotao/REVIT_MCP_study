using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCP.Core.Review;

#if REVIT2025_OR_GREATER
using IdType = System.Int64;
#else
using IdType = System.Int32;
#endif

namespace RevitMCP.Core
{
    /// <summary>
    /// 模型感知：讓 AI 不靠螢幕截圖就掌握 Revit 目前狀態。
    ///   get_model_snapshot  — 文件 / 作用視圖 / 選取 / 樓層 / 連結 / 主要類別數量 / 警告數
    ///   get_model_warnings  — 依訊息分組的警告清單（可只看特定元素）
    ///   get_recent_changes  — DocumentChanged 紀錄（使用者手動 + AI）
    ///   export_view_image   — 把指定視圖（或聚焦元素的 3D 剖面框）輸出成 PNG 給 AI 看
    /// </summary>
    public partial class CommandExecutor
    {
        private static readonly (string key, BuiltInCategory bic)[] SnapshotCategories =
        {
            ("walls", BuiltInCategory.OST_Walls), ("floors", BuiltInCategory.OST_Floors),
            ("ceilings", BuiltInCategory.OST_Ceilings), ("roofs", BuiltInCategory.OST_Roofs),
            ("doors", BuiltInCategory.OST_Doors), ("windows", BuiltInCategory.OST_Windows),
            ("rooms", BuiltInCategory.OST_Rooms), ("stairs", BuiltInCategory.OST_Stairs),
            ("structural_columns", BuiltInCategory.OST_StructuralColumns), ("columns", BuiltInCategory.OST_Columns),
            ("beams", BuiltInCategory.OST_StructuralFraming), ("curtain_panels", BuiltInCategory.OST_CurtainWallPanels),
            ("generic_models", BuiltInCategory.OST_GenericModel), ("furniture", BuiltInCategory.OST_Furniture),
            ("pipes", BuiltInCategory.OST_PipeCurves), ("ducts", BuiltInCategory.OST_DuctCurves),
            ("cable_trays", BuiltInCategory.OST_CableTray), ("conduits", BuiltInCategory.OST_Conduit),
        };

        private object GetModelSnapshot(JObject parameters)
        {
            ChangeTracker.EnsureSubscribed(_uiApp.Application);
            UIDocument uidoc = _uiApp.ActiveUIDocument ?? throw new Exception("目前沒有開啟的專案");
            Document doc = uidoc.Document;
            View av = doc.ActiveView;

            object viewRange = null;
            if (av is ViewPlan vp)
            {
                try
                {
                    PlanViewRange r = vp.GetViewRange();
                    object Plane(PlanViewPlane p)
                    {
                        var lid = r.GetLevelId(p);
                        return new { level = lid == ElementId.InvalidElementId ? null : doc.GetElement(lid)?.Name, offsetMm = ReviewUtil.Mm(r.GetOffset(p)) };
                    }
                    viewRange = new
                    {
                        top = Plane(PlanViewPlane.TopClipPlane),
                        cut = Plane(PlanViewPlane.CutPlane),
                        bottom = Plane(PlanViewPlane.BottomClipPlane),
                        viewDepth = Plane(PlanViewPlane.ViewDepthPlane)
                    };
                }
                catch { }
            }

            var selIds = uidoc.Selection.GetElementIds();
            var selection = selIds.Take(20).Select(id => doc.GetElement(id)).Where(e => e != null).Select(e => new
            {
                id = e.Id.GetIdValue(),
                category = e.Category?.Name,
                type = ReviewUtil.TypeName(e),
                level = ReviewUtil.LevelName(e)
            }).ToList();

            var levels = ReviewUtil.SortedLevels(doc).Select(l => new
            {
                id = l.Id.GetIdValue(),
                name = l.Name,
                elevationMm = ReviewUtil.Mm(l.ProjectElevation)
            }).ToList();

            var grids = new FilteredElementCollector(doc).OfClass(typeof(Grid)).Cast<Grid>().Select(g => g.Name).OrderBy(n => n).ToList();

            var links = new FilteredElementCollector(doc).OfClass(typeof(RevitLinkInstance)).Cast<RevitLinkInstance>().Select(li =>
            {
                Document ld = null;
                try { ld = li.GetLinkDocument(); } catch { }
                return new { instanceId = li.Id.GetIdValue(), name = li.Name, loaded = ld != null };
            }).ToList();

            var counts = new Dictionary<string, int>();
            foreach (var (key, bic) in SnapshotCategories)
            {
                int n = new FilteredElementCollector(doc).OfCategory(bic).WhereElementIsNotElementType().GetElementCount();
                if (n > 0) counts[key] = n;
            }

            string lengthUnit = null;
            try { lengthUnit = doc.GetUnits().GetFormatOptions(SpecTypeId.Length).GetUnitTypeId().TypeId; } catch { }

            int warningCount = 0;
            try { warningCount = doc.GetWarnings().Count; } catch { }

            return new
            {
                document = new { title = doc.Title, path = doc.PathName, isWorkshared = doc.IsWorkshared, isModified = doc.IsModified },
                lengthUnit,
                activeView = new
                {
                    id = av.Id.GetIdValue(),
                    name = av.Name,
                    viewType = av.ViewType.ToString(),
                    level = av.GenLevel?.Name,
                    scale = av.Scale,
                    detailLevel = av.DetailLevel.ToString(),
                    viewRange
                },
                selection = new { count = selIds.Count, items = selection },
                levels,
                grids = new { count = grids.Count, names = grids.Take(100).ToList() },
                links,
                elementCounts = counts,
                warningCount,
                changeSeq = ChangeTracker.CurrentSeq,
                hint = "changeSeq 可傳給 get_recent_changes(sinceSeq) 取得之後的變更；長度值單位為 mm"
            };
        }

        private object GetModelWarnings(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument?.Document ?? throw new Exception("目前沒有開啟的專案");
            string contains = parameters["contains"]?.Value<string>();
            int maxGroups = parameters["maxGroups"]?.Value<int>() ?? 50;
            var idFilter = new HashSet<long>((parameters["elementIds"] as JArray)?.Select(t => t.Value<long>()) ?? Enumerable.Empty<long>());

            var all = doc.GetWarnings();
            var rows = new List<(string text, string severity, List<long> ids)>();
            foreach (var w in all)
            {
                string text;
                try { text = w.GetDescriptionText(); } catch { continue; }
                if (!string.IsNullOrEmpty(contains) && text.IndexOf(contains, StringComparison.OrdinalIgnoreCase) < 0) continue;
                var ids = w.GetFailingElements().Concat(w.GetAdditionalElements()).Select(i => (long)i.GetIdValue()).Distinct().ToList();
                if (idFilter.Count > 0 && !ids.Any(idFilter.Contains)) continue;
                rows.Add((text, w.GetSeverity().ToString(), ids));
            }

            var groups = rows.GroupBy(r => r.text).OrderByDescending(g => g.Count()).Take(maxGroups).Select(g => new
            {
                message = g.Key,
                severity = g.First().severity,
                count = g.Count(),
                sampleElementIds = g.SelectMany(r => r.ids).Distinct().Take(12).ToList()
            }).ToList();

            return new
            {
                totalWarningsInModel = all.Count,
                matched = rows.Count,
                groupCount = rows.Select(r => r.text).Distinct().Count(),
                groups
            };
        }

        private object GetRecentChanges(JObject parameters)
        {
            ChangeTracker.EnsureSubscribed(_uiApp.Application);
            Document doc = _uiApp.ActiveUIDocument?.Document;
            long since = parameters["sinceSeq"]?.Value<long>() ?? 0;
            int limit = Math.Max(1, Math.Min(200, parameters["limit"]?.Value<int>() ?? 50));
            bool currentOnly = parameters["currentDocumentOnly"]?.Value<bool>() ?? true;

            var list = ChangeTracker.Since(since, limit, currentOnly ? doc?.Title : null);
            return new
            {
                currentSeq = ChangeTracker.CurrentSeq,
                count = list.Count,
                note = "紀錄從本工具（或 get_model_snapshot）首次被呼叫時開始訂閱，之前的變更不會有紀錄。byAI=true 僅代表交易名稱以 AI:/MCP 開頭（本組檢討/安全編輯工具都會加），其他舊工具的修改可能顯示 false。operation=TransactionGroupRolledBack 是試跑（dryRun）回滾，模型最終未變更",
                changes = list.Select(c => new
                {
                    seq = c.Seq,
                    time = c.Time.ToString("HH:mm:ss"),
                    document = c.Document,
                    operation = c.Operation,
                    transactions = c.Transactions,
                    byAI = c.ByAI,
                    added = c.Added,
                    modified = c.Modified,
                    deleted = c.Deleted,
                    categories = ResolveCategoryCounts(doc, c),
                    sampleAddedIds = c.AddedIds,
                    sampleModifiedIds = c.ModifiedIds,
                    sampleDeletedIds = c.DeletedIds
                }).ToList()
            };
        }

        /// <summary>惰性解析類別統計：僅在 get_recent_changes 被呼叫時、且紀錄屬於目前文件時才查元素；已不存在的 ID 略過。</summary>
        private static Dictionary<string, int> ResolveCategoryCounts(Document doc, ChangeTracker.ChangeEntry c)
        {
            var cats = new Dictionary<string, int>();
            if (doc == null || c.Document != doc.Title || c.CategoryProbeIds == null) return cats;
            foreach (long idv in c.CategoryProbeIds)
            {
                string name;
                try { name = doc.GetElement(new ElementId((IdType)idv))?.Category?.Name; } catch { name = null; }
                if (string.IsNullOrEmpty(name)) continue;
                cats[name] = cats.TryGetValue(name, out int n) ? n + 1 : 1;
            }
            return cats;
        }

        /// <summary>清除 check_beam_clear_height / check_mep_spacing 放的檢討標記（只動 ApplicationId = RevitMCP.Review 的 DirectShape）。</summary>
        private object ClearReviewMarkers(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument?.Document ?? throw new Exception("目前沒有開啟的專案");
            string check = parameters["checkName"]?.Value<string>();
            var ids = new FilteredElementCollector(doc).OfClass(typeof(DirectShape)).Cast<DirectShape>()
                .Where(d => d.ApplicationId == ReviewUtil.MarkerAppId && (string.IsNullOrEmpty(check) || d.ApplicationDataId == check))
                .Select(d => d.Id).ToList();
            if (ids.Count == 0) return new { removed = 0, message = "沒有檢討標記可清除" };
            using (var t = new Transaction(doc, "AI: 清除檢討標記"))
            {
                t.Start();
                doc.Delete(ids);
                t.Commit();
            }
            return new { removed = ids.Count, checkName = check ?? "(全部)" };
        }

        private object ExportViewImage(JObject parameters)
        {
            UIDocument uidoc = _uiApp.ActiveUIDocument ?? throw new Exception("目前沒有開啟的專案");
            Document doc = uidoc.Document;
            int px = Math.Max(400, Math.Min(4000, parameters["pixelSize"]?.Value<int>() ?? 1600));
            string viewName = parameters["viewName"]?.Value<string>();
            IdType viewIdVal = parameters["viewId"]?.Value<IdType>() ?? 0;
            var focusIds = (parameters["focusElementIds"] as JArray)?.Select(t => new ElementId(t.Value<IdType>())).ToList();
            double marginMm = parameters["marginMm"]?.Value<double>() ?? 1500;

            View view;
            if (focusIds != null && focusIds.Count > 0)
            {
                // 聚焦模式：在檢討用 3D 視圖用剖面框框住指定元素
                var boxes = focusIds.Select(id => doc.GetElement(id)).Where(e => e != null)
                    .Select(e => ReviewUtil.HostBox(e, Transform.Identity)).Where(b => b != null).Select(b => b.Value).ToList();
                if (boxes.Count == 0) throw new Exception("focusElementIds 中沒有可用的主模型元素");
                double m = ReviewUtil.Ft(marginMm);
                var min = new XYZ(boxes.Min(b => b.min.X) - m, boxes.Min(b => b.min.Y) - m, boxes.Min(b => b.min.Z) - m);
                var max = new XYZ(boxes.Max(b => b.max.X) + m, boxes.Max(b => b.max.Y) + m, boxes.Max(b => b.max.Z) + m);
                var v3 = ReviewUtil.EnsureReviewView(doc, ReviewUtil.ResultViewName, new[] { BuiltInCategory.OST_GenericModel });
                using (var t = new Transaction(doc, "AI: 設定檢討視圖剖面框"))
                {
                    t.Start();
                    v3.SetSectionBox(new BoundingBoxXYZ { Min = min, Max = max });
                    v3.IsSectionBoxActive = true;
                    t.Commit();
                }
                view = v3;
            }
            else if (viewIdVal != 0)
            {
                view = doc.GetElement(new ElementId(viewIdVal)) as View ?? throw new Exception($"找不到視圖 ID: {viewIdVal}");
            }
            else if (!string.IsNullOrWhiteSpace(viewName))
            {
                view = new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>()
                    .FirstOrDefault(v => !v.IsTemplate && v.Name == viewName) ?? throw new Exception($"找不到視圖: {viewName}");
            }
            else
            {
                view = doc.ActiveView;
            }

            var unexportable = new[] { ViewType.Schedule, ViewType.ProjectBrowser, ViewType.SystemBrowser, ViewType.Internal, ViewType.Undefined, ViewType.PanelSchedule, ViewType.ColumnSchedule, ViewType.Report };
            if (view.IsTemplate || unexportable.Contains(view.ViewType))
                throw new Exception($"此視圖類型無法輸出影像: {view.ViewType}");

            string dir = Path.Combine(Path.GetTempPath(), "RevitMCP_ViewImages");
            Directory.CreateDirectory(dir);
            string prefix = "view_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
            var opts = new ImageExportOptions
            {
                ExportRange = ExportRange.SetOfViews,
                FilePath = Path.Combine(dir, prefix),
                ZoomType = ZoomFitType.FitToPage,
                PixelSize = px,
                FitDirection = FitDirectionType.Horizontal,
                HLRandWFViewsFileType = ImageFileType.PNG,
                ShadowViewsFileType = ImageFileType.PNG,
                ImageResolution = ImageResolution.DPI_150
            };
            opts.SetViewsAndSheets(new List<ElementId> { view.Id });
            doc.ExportImage(opts);

            var file = new DirectoryInfo(dir).GetFiles(prefix + "*.png").OrderByDescending(f => f.LastWriteTime).FirstOrDefault()
                       ?? throw new Exception("Revit 未產生影像檔（此視圖可能不支援輸出）");
            byte[] bytes = File.ReadAllBytes(file.FullName);
            try { file.Delete(); } catch { }

            return new
            {
                viewId = view.Id.GetIdValue(),
                viewName = view.Name,
                viewType = view.ViewType.ToString(),
                pixelSize = px,
                MimeType = "image/png",
                ImageBase64 = Convert.ToBase64String(bytes)
            };
        }
    }
}
