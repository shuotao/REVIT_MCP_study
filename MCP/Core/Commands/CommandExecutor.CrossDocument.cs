using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;

#if REVIT2025_OR_GREATER
using IdType = System.Int64;
#else
using IdType = System.Int32;
#endif

namespace RevitMCP.Core
{
    // NOTE: ConflictTrackingHandler 已於 Wave4（bundle②）抽出為獨立檔
    //   MCP/Core/Commands/ConflictTrackingHandler.cs，供 DetailCopy 與本檔共用。
    //   snapshot 原本在此內嵌的定義與抽出版逐字相同（建構子簽章、UseDestinationTypes
    //   行為、{ Action = "used_destination" } 記錄皆一致），故此處移除重複定義。

    public partial class CommandExecutor
    {
        #region Cross-Document Sheet Copy

        private object ReadSourceFileSheets(JObject parameters)
        {
            string sourceFilePath = parameters["sourceFilePath"]?.Value<string>();
            if (string.IsNullOrEmpty(sourceFilePath))
                throw new Exception("必須指定 sourceFilePath");

            var sheetNumbers = parameters["sheetNumbers"]?.ToObject<List<string>>();
            bool keepOpen = parameters["keepOpen"]?.Value<bool>() ?? true;

            var (sourceDoc, needToClose) = OpenSourceDocument(sourceFilePath);

            try
            {
                Document targetDoc = _uiApp.ActiveUIDocument.Document;

                var sourceSheets = CollectSheetInfo(sourceDoc, sheetNumbers);
                var conflicts = DetectConflicts(sourceDoc, targetDoc, sheetNumbers);
                var targetPreview = CollectTargetPreview(targetDoc);

                return new
                {
                    SourceFile = sourceFilePath,
                    IsWorkshared = sourceDoc.IsWorkshared,
                    SheetCount = sourceSheets.Count,
                    Sheets = sourceSheets,
                    Conflicts = conflicts,
                    TargetMatchPreview = targetPreview
                };
            }
            finally
            {
                if (!keepOpen && needToClose)
                    sourceDoc.Close(false);
            }
        }

        private object CopySheetsFromFile(JObject parameters)
        {
            string sourceFilePath = parameters["sourceFilePath"]?.Value<string>();
            if (string.IsNullOrEmpty(sourceFilePath))
                throw new Exception("必須指定 sourceFilePath");

            var sheetNumbers = parameters["sheetNumbers"]?.ToObject<List<string>>();
            string viewStrategy = parameters["viewMatchStrategy"]?.Value<string>() ?? "match_or_create";
            bool copyDraftingContents = parameters["copyDraftingContents"]?.Value<bool>() ?? true;
            bool copySheetCustomParameters = parameters["copySheetCustomParameters"]?.Value<bool>() ?? true;
            bool closeAfterCopy = parameters["closeAfterCopy"]?.Value<bool>() ?? true;
            var conflictResolution = parameters["conflictResolution"] as JObject ?? new JObject();
            var syncOptions = parameters["syncProperties"] as JObject;

            var sheetConflicts = conflictResolution["sheets"]?.ToObject<Dictionary<string, string>>()
                ?? new Dictionary<string, string>();
            var viewConflicts = conflictResolution["views"]?.ToObject<Dictionary<string, string>>()
                ?? new Dictionary<string, string>();
            var scheduleConflicts = conflictResolution["schedules"]?.ToObject<Dictionary<string, string>>()
                ?? new Dictionary<string, string>();
            string typeConflictAction = conflictResolution["typeConflicts"]?.Value<string>() ?? "use_destination";

            var (sourceDoc, needToClose) = OpenSourceDocument(sourceFilePath);

            try
            {
                Document targetDoc = _uiApp.ActiveUIDocument.Document;

                var sourceSheets = GetSourceSheetElements(sourceDoc, sheetNumbers);

                var sheetsCreated = new List<object>();
                var sheetsSkipped = new List<object>();
                var viewsMatched = new List<object>();
                var viewsCreated = new List<object>();
                var viewportsPlaced = new List<object>();
                var manualActionRequired = new List<object>();
                var warnings = new List<string>();
                var sheetErrors = new List<object>();
                var viewTemplatesCopied = new List<object>();
                var copiedTemplateCache = new Dictionary<string, ElementId>();

                // Build target lookup tables
                var targetViewsByName = new FilteredElementCollector(targetDoc)
                    .OfClass(typeof(View)).Cast<View>()
                    .Where(v => !v.IsTemplate)
                    .GroupBy(v => v.Name)
                    .ToDictionary(g => g.Key, g => g.First());

                var targetSheetsByNumber = new FilteredElementCollector(targetDoc)
                    .OfClass(typeof(ViewSheet)).Cast<ViewSheet>()
                    .ToDictionary(s => s.SheetNumber, s => s);

                var targetSchedulesByName = new FilteredElementCollector(targetDoc)
                    .OfClass(typeof(ViewSchedule)).Cast<ViewSchedule>()
                    .Where(vs => !vs.IsTitleblockRevisionSchedule)
                    .GroupBy(vs => vs.Name)
                    .ToDictionary(g => g.Key, g => g.First());

                using (TransactionGroup tg = new TransactionGroup(targetDoc, "從來源檔複製圖紙"))
                {
                    tg.Start();

                    foreach (ViewSheet sourceSheet in sourceSheets)
                    {
                        string sheetNum = sourceSheet.SheetNumber;
                        string sheetName = sourceSheet.Name;

                        try
                        {
                            // ── Sheet conflict check ──
                            if (targetSheetsByNumber.ContainsKey(sheetNum))
                            {
                                string action = sheetConflicts.ContainsKey(sheetNum) ? sheetConflicts[sheetNum] : "skip";
                                if (action == "skip")
                                {
                                    sheetsSkipped.Add(new { Number = sheetNum, Name = sheetName, Reason = "目標檔已有同編號圖紙，使用者選擇 skip" });
                                    continue;
                                }
                                if (action == "rename")
                                {
                                    int suffix = 1;
                                    while (targetSheetsByNumber.ContainsKey($"{sheetNum}-{suffix}")) suffix++;
                                    sheetNum = $"{sheetNum}-{suffix}";
                                }
                            }

                            // ── Find title block ──
                            ElementId tbId = FindMatchingTitleBlock(sourceDoc, targetDoc, sourceSheet);

                            using (Transaction t = TransactionHelper.Begin(targetDoc, $"複製圖紙 {sheetNum}"))
                            {
                                t.Start();

                                // ── Create sheet ──
                                ViewSheet newSheet = ViewSheet.Create(targetDoc, tbId);
                                newSheet.SheetNumber = sheetNum;
                                newSheet.Name = sheetName;

                                targetSheetsByNumber[sheetNum] = newSheet;

                                // ── Copy custom parameters (sheet folder, discipline, etc.) ──
                                if (copySheetCustomParameters)
                                {
                                    CopySheetCustomParameters(sourceSheet, newSheet, warnings);
                                }

                                // ── Process viewports ──
                                var vpIds = sourceSheet.GetAllViewports();
                                foreach (ElementId vpId in vpIds)
                                {
                                    var vp = sourceDoc.GetElement(vpId) as Viewport;
                                    if (vp == null) continue;

                                    View sourceView = sourceDoc.GetElement(vp.ViewId) as View;
                                    if (sourceView == null) continue;

                                    XYZ vpCenter = vp.GetBoxCenter();
                                    string viewName = sourceView.Name;
                                    ViewType viewType = sourceView.ViewType;

                                    // Determine tier and action
                                    string tier = ClassifyViewTier(viewType, sourceView);

                                    if (tier == "tier4")
                                    {
                                        manualActionRequired.Add(BuildManualAction(sourceView, sourceDoc, sheetNum));
                                        continue;
                                    }

                                    // ── Match or create view ──
                                    View targetView = null;
                                    string viewStatus = "skipped";

                                    if (targetViewsByName.ContainsKey(viewName))
                                    {
                                        string vAction = viewConflicts.ContainsKey(viewName) ? viewConflicts[viewName] : "use_existing";
                                        if (vAction == "use_existing")
                                        {
                                            targetView = targetViewsByName[viewName];
                                            viewStatus = "matched_existing";
                                            viewsMatched.Add(new { ViewName = viewName, ViewType = viewType.ToString(), Status = viewStatus });
                                        }
                                        else if (vAction == "overwrite" && (tier == "tier1" || tier == "tier2"))
                                        {
                                            var oldView = targetViewsByName[viewName];
                                            targetDoc.Delete(oldView.Id);
                                            targetViewsByName.Remove(viewName);
                                            targetView = CreateViewByTier(sourceDoc, sourceView, targetDoc, viewName, tier, copyDraftingContents, typeConflictAction, warnings);
                                            if (targetView != null)
                                            {
                                                targetViewsByName[targetView.Name] = targetView;
                                                viewStatus = "overwritten";
                                                viewsCreated.Add(new { ViewName = targetView.Name, ViewType = viewType.ToString(), Status = viewStatus });
                                            }
                                        }
                                        else if (vAction == "rename" && (tier == "tier1" || tier == "tier2"))
                                        {
                                            string newName = GenerateUniqueName(viewName, targetViewsByName);
                                            targetView = CreateViewByTier(sourceDoc, sourceView, targetDoc, newName, tier, copyDraftingContents, typeConflictAction, warnings);
                                            if (targetView != null)
                                            {
                                                targetViewsByName[targetView.Name] = targetView;
                                                viewStatus = "created_renamed";
                                                viewsCreated.Add(new { ViewName = targetView.Name, OriginalName = viewName, ViewType = viewType.ToString(), Status = viewStatus });
                                            }
                                        }
                                    }
                                    else if (viewStrategy == "match_or_create" && (tier == "tier1" || tier == "tier2"))
                                    {
                                        targetView = CreateViewByTier(sourceDoc, sourceView, targetDoc, viewName, tier, copyDraftingContents, typeConflictAction, warnings);
                                        if (targetView != null)
                                        {
                                            targetViewsByName[targetView.Name] = targetView;
                                            viewStatus = "created_new";
                                            viewsCreated.Add(new { ViewName = targetView.Name, ViewType = viewType.ToString(), Status = viewStatus });
                                        }
                                    }
                                    else if (tier == "tier3")
                                    {
                                        manualActionRequired.Add(new
                                        {
                                            SheetNumber = sheetNum,
                                            Type = $"{viewType}_not_found",
                                            ViewName = viewName,
                                            Reason = $"目標檔中找不到同名 {viewType} view，Tier 3 不自動建立",
                                            SuggestedAction = "手動建立後重新執行 copy_sheets_from_file"
                                        });
                                        continue;
                                    }
                                    else
                                    {
                                        warnings.Add($"View '{viewName}' ({viewType}) 在目標檔找不到，strategy=match_only 跳過");
                                        continue;
                                    }

                                    if (targetView == null)
                                    {
                                        warnings.Add($"View '{viewName}' 建立失敗，跳過 viewport 放置");
                                        continue;
                                    }

                                    // ── Sync properties ──
                                    SyncViewPropertiesFromSource(targetDoc, targetView, sourceView, sourceDoc, syncOptions,
                                        copiedTemplateCache, viewTemplatesCopied, warnings);

                                    // ── Place viewport ──
                                    try
                                    {
                                        if (viewType == ViewType.Legend || Viewport.CanAddViewToSheet(targetDoc, newSheet.Id, targetView.Id))
                                        {
                                            Viewport.Create(targetDoc, newSheet.Id, targetView.Id, vpCenter);
                                            viewportsPlaced.Add(new
                                            {
                                                SheetNumber = sheetNum,
                                                ViewName = targetView.Name,
                                                Center = new { X = vpCenter.X * 304.8, Y = vpCenter.Y * 304.8 },
                                                Status = viewStatus
                                            });
                                        }
                                        else
                                        {
                                            manualActionRequired.Add(new
                                            {
                                                SheetNumber = sheetNum,
                                                Type = "view_already_placed",
                                                ViewName = targetView.Name,
                                                Reason = "View 已在另一張圖紙上，非 Legend view 不可重複放置",
                                                SuggestedAction = "使用 View.Duplicate() 建立副本，或先從另一張圖紙移除"
                                            });
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        warnings.Add($"Viewport 放置失敗: view='{targetView.Name}', sheet='{sheetNum}': {ex.Message}");
                                    }
                                }

                                // ── Process ScheduleSheetInstances ──
                                var scheduleInstances = new FilteredElementCollector(sourceDoc, sourceSheet.Id)
                                    .OfClass(typeof(ScheduleSheetInstance))
                                    .Cast<ScheduleSheetInstance>()
                                    .ToList();

                                foreach (var ssi in scheduleInstances)
                                {
                                    var sourceSchedule = sourceDoc.GetElement(ssi.ScheduleId) as ViewSchedule;
                                    if (sourceSchedule == null) continue;

                                    string schName = sourceSchedule.Name;
                                    XYZ position = ssi.Point;

                                    if (targetSchedulesByName.ContainsKey(schName))
                                    {
                                        string sAction = scheduleConflicts.ContainsKey(schName) ? scheduleConflicts[schName] : "use_existing";
                                        if (sAction == "skip")
                                        {
                                            warnings.Add($"Schedule '{schName}' 使用者選擇 skip，不放置到 sheet {sheetNum}");
                                            continue;
                                        }

                                        try
                                        {
                                            ScheduleSheetInstance.Create(targetDoc, newSheet.Id, targetSchedulesByName[schName].Id, position);
                                            viewportsPlaced.Add(new
                                            {
                                                SheetNumber = sheetNum,
                                                ViewName = schName,
                                                Type = "ScheduleSheetInstance",
                                                Position = new { X = position.X * 304.8, Y = position.Y * 304.8 },
                                                Status = "placed"
                                            });
                                        }
                                        catch (Exception ex)
                                        {
                                            warnings.Add($"ScheduleSheetInstance 放置失敗: '{schName}' on sheet {sheetNum}: {ex.Message}");
                                        }
                                    }
                                    else
                                    {
                                        manualActionRequired.Add(new
                                        {
                                            SheetNumber = sheetNum,
                                            Type = "schedule_not_found",
                                            ScheduleName = schName,
                                            SourceFields = GetScheduleFieldNames(sourceSchedule),
                                            Position = new { X = position.X * 304.8, Y = position.Y * 304.8 },
                                            Reason = "目標檔中找不到同名 Schedule",
                                            SuggestedAction = "使用 create_view_schedule 工具建立，或手動建立後重新執行"
                                        });
                                    }
                                }

                                t.Commit();

                                sheetsCreated.Add(new
                                {
                                    Number = sheetNum,
                                    Name = sheetName,
                                    ElementId = newSheet.Id.GetIdValue()
                                });
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.Error($"複製圖紙 {sheetNum} 失敗: {ex.Message}");
                            sheetErrors.Add(new { SheetNumber = sheetNum, Error = ex.Message });
                        }
                    }

                    tg.Assimilate();
                }

                return new
                {
                    Success = true,
                    Summary = new
                    {
                        SheetsCreated = sheetsCreated.Count,
                        SheetsSkipped = sheetsSkipped.Count,
                        SheetErrors = sheetErrors.Count,
                        ViewsMatched = viewsMatched.Count,
                        ViewsCreated = viewsCreated.Count,
                        ViewportsPlaced = viewportsPlaced.Count,
                        ViewTemplatesCopied = viewTemplatesCopied.Count,
                        ManualActionsRequired = manualActionRequired.Count,
                        Warnings = warnings.Count
                    },
                    SheetsCreated = sheetsCreated,
                    SheetsSkipped = sheetsSkipped,
                    SheetErrors = sheetErrors,
                    ViewsMatched = viewsMatched,
                    ViewsCreated = viewsCreated,
                    ViewportsPlaced = viewportsPlaced,
                    ViewTemplatesCopied = viewTemplatesCopied,
                    ManualActionRequired = manualActionRequired,
                    Warnings = warnings
                };
            }
            finally
            {
                if (closeAfterCopy && needToClose)
                    sourceDoc.Close(false);
            }
        }

        #endregion

        #region Cross-Document Helpers

        private (Document doc, bool needToClose) OpenSourceDocument(string filePath)
        {
            if (!File.Exists(filePath))
                throw new Exception($"來源檔案不存在: {filePath}");

            var fileInfo = new FileInfo(filePath);
            if (fileInfo.Length > 500L * 1024 * 1024)
                throw new Exception($"來源檔案過大 ({fileInfo.Length / 1024 / 1024}MB)，建議低於 500MB");

            Autodesk.Revit.ApplicationServices.Application app = _uiApp.Application;
            Document targetDoc = _uiApp.ActiveUIDocument.Document;

            if (!string.IsNullOrEmpty(targetDoc.PathName) &&
                targetDoc.PathName.Equals(filePath, StringComparison.OrdinalIgnoreCase))
                throw new Exception("來源檔與目標檔相同，無法自我複製");

            foreach (Document doc in app.Documents)
            {
                if (doc.PathName.Equals(filePath, StringComparison.OrdinalIgnoreCase))
                    return (doc, false);
            }

            BasicFileInfo bfi = BasicFileInfo.Extract(filePath);
            ModelPath modelPath = ModelPathUtils.ConvertUserVisiblePathToModelPath(filePath);
            OpenOptions openOpts = new OpenOptions();
            if (bfi.IsWorkshared)
                openOpts.DetachFromCentralOption = DetachFromCentralOption.DetachAndPreserveWorksets;

            Document sourceDoc = app.OpenDocumentFile(modelPath, openOpts);
            return (sourceDoc, true);
        }

        private List<object> CollectSheetInfo(Document sourceDoc, List<string> sheetNumbers)
        {
            var sheets = new FilteredElementCollector(sourceDoc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .Where(s => !s.IsPlaceholder)
                .Where(s => sheetNumbers == null || sheetNumbers.Count == 0 || sheetNumbers.Contains(s.SheetNumber))
                .OrderBy(s => s.SheetNumber)
                .ToList();

            var result = new List<object>();

            foreach (var sheet in sheets)
            {
                var titleBlock = new FilteredElementCollector(sourceDoc, sheet.Id)
                    .OfCategory(BuiltInCategory.OST_TitleBlocks)
                    .FirstOrDefault();

                string tbFamily = null, tbType = null;
                if (titleBlock != null)
                {
                    tbFamily = titleBlock.get_Parameter(BuiltInParameter.ELEM_FAMILY_PARAM)?.AsValueString();
                    tbType = titleBlock.get_Parameter(BuiltInParameter.ELEM_TYPE_PARAM)?.AsValueString();
                }

                // Viewports
                var viewports = new List<object>();
                foreach (ElementId vpId in sheet.GetAllViewports())
                {
                    var vp = sourceDoc.GetElement(vpId) as Viewport;
                    if (vp == null) continue;

                    var view = sourceDoc.GetElement(vp.ViewId) as View;
                    if (view == null) continue;

                    XYZ center = vp.GetBoxCenter();
                    string levelName = null;
                    if (view is ViewPlan vp2)
                        levelName = vp2.GenLevel?.Name;

                    string viewTemplateName = null;
                    if (view.ViewTemplateId != ElementId.InvalidElementId)
                    {
                        var template = sourceDoc.GetElement(view.ViewTemplateId) as View;
                        viewTemplateName = template?.Name;
                    }

                    string scopeBoxName = null;
                    var scopeBoxParam = view.get_Parameter(BuiltInParameter.VIEWER_VOLUME_OF_INTEREST_CROP);
                    if (scopeBoxParam != null && scopeBoxParam.AsElementId() != ElementId.InvalidElementId)
                    {
                        var scopeBox = sourceDoc.GetElement(scopeBoxParam.AsElementId());
                        scopeBoxName = scopeBox?.Name;
                    }

                    string tier = ClassifyViewTier(view.ViewType, view);

                    var cropBoxInfo = (object)null;
                    try
                    {
                        if (view.CropBoxActive)
                        {
                            var cb = view.CropBox;
                            cropBoxInfo = new
                            {
                                Min = new { X = cb.Min.X * 304.8, Y = cb.Min.Y * 304.8, Z = cb.Min.Z * 304.8 },
                                Max = new { X = cb.Max.X * 304.8, Y = cb.Max.Y * 304.8, Z = cb.Max.Z * 304.8 }
                            };
                        }
                    }
                    catch { /* some views don't support CropBox */ }

                    int elementCount = 0;
                    if (view.ViewType == ViewType.DraftingView)
                    {
                        elementCount = new FilteredElementCollector(sourceDoc, view.Id)
                            .WhereElementIsNotElementType()
                            .GetElementCount();
                    }

                    viewports.Add(new
                    {
                        ViewName = view.Name,
                        ViewType = view.ViewType.ToString(),
                        Tier = tier,
                        LevelName = levelName,
                        Scale = view.Scale,
                        Center = new { X = center.X * 304.8, Y = center.Y * 304.8 },
                        CropBoxActive = view.CropBoxActive,
                        CropBox = cropBoxInfo,
                        ViewTemplateName = viewTemplateName,
                        ScopeBoxName = scopeBoxName,
                        DetailLevel = view.DetailLevel.ToString(),
                        DisplayStyle = view.DisplayStyle.ToString(),
                        ElementCount = view.ViewType == ViewType.DraftingView ? (int?)elementCount : null
                    });
                }

                // Schedule sheet instances
                var scheduleInstances = CollectScheduleInstances(sourceDoc, sheet.Id);

                result.Add(new
                {
                    Number = sheet.SheetNumber,
                    Name = sheet.Name,
                    TitleBlockFamily = tbFamily,
                    TitleBlockType = tbType,
                    ViewportCount = viewports.Count,
                    Viewports = viewports,
                    ScheduleInstanceCount = scheduleInstances.Count,
                    ScheduleInstances = scheduleInstances
                });
            }

            return result;
        }

        private List<object> CollectScheduleInstances(Document doc, ElementId sheetId)
        {
            return new FilteredElementCollector(doc, sheetId)
                .OfClass(typeof(ScheduleSheetInstance))
                .Cast<ScheduleSheetInstance>()
                .Select(ssi =>
                {
                    var schedule = doc.GetElement(ssi.ScheduleId) as ViewSchedule;
                    var fields = GetScheduleFieldNames(schedule);
                    string catName = null;
                    try
                    {
                        if (schedule?.Definition?.CategoryId != null && schedule.Definition.CategoryId != ElementId.InvalidElementId)
                            catName = Category.GetCategory(doc, schedule.Definition.CategoryId)?.Name;
                    }
                    catch { }

                    return (object)new
                    {
                        ScheduleName = schedule?.Name ?? "Unknown",
                        CategoryName = catName,
                        FieldCount = fields.Count,
                        FieldNames = fields,
                        Position = new { X = ssi.Point.X * 304.8, Y = ssi.Point.Y * 304.8 }
                    };
                })
                .ToList();
        }

        private List<string> GetScheduleFieldNames(ViewSchedule schedule)
        {
            if (schedule?.Definition == null) return new List<string>();
            int count = schedule.Definition.GetFieldCount();
            return Enumerable.Range(0, count)
                .Select(i => schedule.Definition.GetField(i).GetName())
                .ToList();
        }

        private object DetectConflicts(Document sourceDoc, Document targetDoc, List<string> sheetNumbersFilter)
        {
            var targetSheetNumbers = new HashSet<string>(
                new FilteredElementCollector(targetDoc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>()
                .Select(s => s.SheetNumber));

            var targetViewNames = new HashSet<string>(
                new FilteredElementCollector(targetDoc).OfClass(typeof(View)).Cast<View>()
                .Where(v => !v.IsTemplate).Select(v => v.Name));

            var targetScheduleNames = new HashSet<string>(
                new FilteredElementCollector(targetDoc).OfClass(typeof(ViewSchedule)).Cast<ViewSchedule>()
                .Where(vs => !vs.IsTitleblockRevisionSchedule).Select(vs => vs.Name));

            var sheetConflicts = new List<object>();
            var viewConflicts = new List<object>();
            var scheduleConflicts = new List<object>();
            var seenViews = new HashSet<string>();
            var seenSchedules = new HashSet<string>();

            // FIX: 只掃 sheetNumbersFilter 指定的 sheets, 不掃全部 (避免在大型來源檔上慢到 timeout)
            var sourceSheets = new FilteredElementCollector(sourceDoc)
                .OfClass(typeof(ViewSheet)).Cast<ViewSheet>()
                .Where(s => !s.IsPlaceholder)
                .Where(s => sheetNumbersFilter == null || sheetNumbersFilter.Count == 0 || sheetNumbersFilter.Contains(s.SheetNumber))
                .ToList();

            foreach (var sheet in sourceSheets)
            {
                if (targetSheetNumbers.Contains(sheet.SheetNumber))
                    sheetConflicts.Add(new { SourceNumber = sheet.SheetNumber, SourceName = sheet.Name, ConflictType = "sheet_number_exists" });

                foreach (ElementId vpId in sheet.GetAllViewports())
                {
                    var vp = sourceDoc.GetElement(vpId) as Viewport;
                    if (vp == null) continue;
                    var view = sourceDoc.GetElement(vp.ViewId) as View;
                    if (view == null || seenViews.Contains(view.Name)) continue;
                    seenViews.Add(view.Name);

                    if (targetViewNames.Contains(view.Name))
                    {
                        string tier = ClassifyViewTier(view.ViewType, view);
                        viewConflicts.Add(new { ViewName = view.Name, ViewType = view.ViewType.ToString(), Tier = tier, ConflictType = "view_name_exists" });
                    }
                }

                var scheduleInstances = new FilteredElementCollector(sourceDoc, sheet.Id)
                    .OfClass(typeof(ScheduleSheetInstance)).Cast<ScheduleSheetInstance>().ToList();

                foreach (var ssi in scheduleInstances)
                {
                    var schedule = sourceDoc.GetElement(ssi.ScheduleId) as ViewSchedule;
                    if (schedule == null || seenSchedules.Contains(schedule.Name)) continue;
                    seenSchedules.Add(schedule.Name);

                    if (targetScheduleNames.Contains(schedule.Name))
                        scheduleConflicts.Add(new { ScheduleName = schedule.Name, ConflictType = "schedule_exists_in_target" });
                    else
                        scheduleConflicts.Add(new { ScheduleName = schedule.Name, ConflictType = "schedule_not_found_in_target" });
                }
            }

            var targetTemplateNames = new HashSet<string>(
                new FilteredElementCollector(targetDoc).OfClass(typeof(View)).Cast<View>()
                .Where(v => v.IsTemplate).Select(v => v.Name));

            var seenTemplates = new HashSet<string>();
            var templateGaps = new List<object>();

            foreach (var sheet in sourceSheets)
            {
                foreach (ElementId vpId in sheet.GetAllViewports())
                {
                    var vp = sourceDoc.GetElement(vpId) as Viewport;
                    if (vp == null) continue;
                    var view = sourceDoc.GetElement(vp.ViewId) as View;
                    if (view == null || view.ViewTemplateId == ElementId.InvalidElementId) continue;
                    var templateName = (sourceDoc.GetElement(view.ViewTemplateId) as View)?.Name;
                    if (string.IsNullOrEmpty(templateName) || !seenTemplates.Add(templateName)) continue;

                    if (!targetTemplateNames.Contains(templateName))
                    {
                        templateGaps.Add(new
                        {
                            TemplateName = templateName,
                            Status = "will_be_auto_copied"
                        });
                    }
                }
            }

            return new
            {
                Sheets = sheetConflicts,
                Views = viewConflicts,
                Schedules = scheduleConflicts,
                ViewTemplates = templateGaps
            };
        }

        private object CollectTargetPreview(Document targetDoc)
        {
            var existingSheets = new FilteredElementCollector(targetDoc)
                .OfClass(typeof(ViewSheet)).Cast<ViewSheet>()
                .Select(s => s.SheetNumber).OrderBy(n => n).ToList();

            var existingViews = new FilteredElementCollector(targetDoc)
                .OfClass(typeof(View)).Cast<View>()
                .Where(v => !v.IsTemplate)
                .Select(v => v.Name).Distinct().OrderBy(n => n).ToList();

            var existingLevels = new FilteredElementCollector(targetDoc)
                .OfClass(typeof(Level)).Cast<Level>()
                .Select(l => l.Name).OrderBy(n => n).ToList();

            var existingTemplates = new FilteredElementCollector(targetDoc)
                .OfClass(typeof(View)).Cast<View>()
                .Where(v => v.IsTemplate)
                .Select(v => v.Name).OrderBy(n => n).ToList();

            return new
            {
                ExistingSheets = existingSheets,
                ExistingViews = existingViews,
                ExistingLevels = existingLevels,
                ExistingViewTemplates = existingTemplates
            };
        }

        private List<ViewSheet> GetSourceSheetElements(Document sourceDoc, List<string> sheetNumbers)
        {
            return new FilteredElementCollector(sourceDoc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .Where(s => !s.IsPlaceholder)
                .Where(s => sheetNumbers == null || sheetNumbers.Count == 0 || sheetNumbers.Contains(s.SheetNumber))
                .OrderBy(s => s.SheetNumber)
                .ToList();
        }

        private string ClassifyViewTier(ViewType viewType, View view)
        {
            switch (viewType)
            {
                case ViewType.FloorPlan:
                case ViewType.CeilingPlan:
                    // Check if it's a callout (has parent)
                    try
                    {
                        var calloutOwner = view.get_Parameter(BuiltInParameter.SECTION_PARENT_VIEW_NAME);
                        if (calloutOwner != null && !string.IsNullOrEmpty(calloutOwner.AsString()))
                            return "tier4"; // Callout
                    }
                    catch { }
                    return "tier1";

                case ViewType.Section:
                case ViewType.Detail:
                case ViewType.ThreeD:
                    return "tier2";

                case ViewType.DraftingView:
                    return "tier2";

                case ViewType.Legend:
                    return "tier3";

                case ViewType.Schedule:
                    return "tier3";

                case ViewType.AreaPlan:
                case ViewType.Elevation:
                case ViewType.Rendering:
                    return "tier4";

                default:
                    return "tier4";
            }
        }

        private View CreateViewByTier(
            Document sourceDoc, View sourceView, Document targetDoc,
            string viewName, string tier, bool copyDraftingContents,
            string typeConflictAction, List<string> warnings)
        {
            try
            {
                switch (sourceView.ViewType)
                {
                    case ViewType.FloorPlan:
                    case ViewType.CeilingPlan:
                        return CreateFloorOrCeilingPlan(sourceDoc, sourceView, targetDoc, viewName);

                    case ViewType.Section:
                    case ViewType.Detail:
                        return CreateSectionFromSource(sourceView, targetDoc, viewName);

                    case ViewType.ThreeD:
                        return CreateIsometric3DView(targetDoc, viewName);

                    case ViewType.DraftingView:
                        if (copyDraftingContents)
                            return CreateDraftingViewWithContent(sourceDoc, sourceView, targetDoc, viewName, typeConflictAction, warnings);
                        else
                            return CreateEmptyDraftingView(targetDoc, viewName, sourceView.Scale);

                    default:
                        warnings.Add($"不支援建立 ViewType={sourceView.ViewType} 的 view: {viewName}");
                        return null;
                }
            }
            catch (Exception ex)
            {
                warnings.Add($"建立 view '{viewName}' 失敗: {ex.Message}");
                return null;
            }
        }

        private ViewPlan CreateFloorOrCeilingPlan(Document sourceDoc, View sourceView, Document targetDoc, string viewName)
        {
            var sourceViewPlan = sourceView as ViewPlan;
            if (sourceViewPlan == null)
                throw new Exception($"來源 view 不是 ViewPlan: {sourceView.Name}");

            string levelName = sourceViewPlan.GenLevel?.Name;
            if (string.IsNullOrEmpty(levelName))
                throw new Exception($"來源 view '{sourceView.Name}' 沒有關聯的 Level");

            Level targetLevel = new FilteredElementCollector(targetDoc)
                .OfClass(typeof(Level)).Cast<Level>()
                .FirstOrDefault(l => l.Name == levelName);

            if (targetLevel == null)
                throw new Exception($"目標檔找不到 Level: {levelName}");

            ViewFamily family = sourceView.ViewType == ViewType.CeilingPlan
                ? ViewFamily.CeilingPlan : ViewFamily.FloorPlan;
            ElementId vftId = FindViewFamilyTypeId(targetDoc, family);

            ViewPlan newView = ViewPlan.Create(targetDoc, vftId, targetLevel.Id);
            newView.Name = viewName;

            return newView;
        }

        private ViewSection CreateSectionFromSource(View sourceView, Document targetDoc, string viewName)
        {
            BoundingBoxXYZ sourceCropBox = sourceView.CropBox;
            if (sourceCropBox == null)
                throw new Exception($"來源 Section view '{sourceView.Name}' 沒有 CropBox");

            ElementId vftId = FindViewFamilyTypeId(targetDoc, ViewFamily.Section);

            BoundingBoxXYZ sectionBox = new BoundingBoxXYZ();
            sectionBox.Min = sourceCropBox.Min;
            sectionBox.Max = sourceCropBox.Max;
            sectionBox.Transform = sourceCropBox.Transform;

            ViewSection newView = ViewSection.CreateSection(targetDoc, vftId, sectionBox);
            newView.Name = viewName;

            return newView;
        }

        private View3D CreateIsometric3DView(Document targetDoc, string viewName)
        {
            ElementId vftId = FindViewFamilyTypeId(targetDoc, ViewFamily.ThreeDimensional);
            View3D newView = View3D.CreateIsometric(targetDoc, vftId);
            newView.Name = viewName;
            return newView;
        }

        private ViewDrafting CreateDraftingViewWithContent(
            Document sourceDoc, View sourceDraftingView,
            Document targetDoc, string viewName,
            string typeConflictAction, List<string> warnings)
        {
            ElementId vftId = FindViewFamilyTypeId(targetDoc, ViewFamily.Drafting);
            ViewDrafting newView = ViewDrafting.Create(targetDoc, vftId);
            newView.Name = viewName;
            newView.Scale = sourceDraftingView.Scale;

            var sourceElements = new FilteredElementCollector(sourceDoc, sourceDraftingView.Id)
                .WhereElementIsNotElementType()
                .ToElementIds();

            if (sourceElements.Count > 0)
            {
                var typeConflicts = new List<object>();
                var handler = new ConflictTrackingHandler(typeConflictAction, typeConflicts);
                CopyPasteOptions opts = new CopyPasteOptions();
                opts.SetDuplicateTypeNamesHandler(handler);

                try
                {
                    ElementTransformUtils.CopyElements(
                        sourceDraftingView, sourceElements,
                        newView, Transform.Identity, opts);
                }
                catch (Exception ex)
                {
                    warnings.Add($"DraftingView '{viewName}' 內容複製部分失敗: {ex.Message}");
                }

                if (typeConflicts.Count > 0)
                    warnings.Add($"DraftingView '{viewName}' 有 {typeConflicts.Count} 個 type 衝突，已使用 {typeConflictAction} 策略");
            }

            return newView;
        }

        private ViewDrafting CreateEmptyDraftingView(Document targetDoc, string viewName, int scale)
        {
            ElementId vftId = FindViewFamilyTypeId(targetDoc, ViewFamily.Drafting);
            ViewDrafting newView = ViewDrafting.Create(targetDoc, vftId);
            newView.Name = viewName;
            newView.Scale = scale;
            return newView;
        }

        private void SyncViewPropertiesFromSource(Document targetDoc, View targetView, View sourceView, Document sourceDoc, JObject syncOptions,
            Dictionary<string, ElementId> copiedTemplateCache, List<object> viewTemplatesCopied, List<string> warnings)
        {
            bool syncScale = syncOptions?["scale"]?.Value<bool>() ?? true;
            bool syncCropBox = syncOptions?["cropBox"]?.Value<bool>() ?? true;
            bool syncTemplate = syncOptions?["viewTemplate"]?.Value<bool>() ?? true;
            bool syncDetailLevel = syncOptions?["detailLevel"]?.Value<bool>() ?? true;
            bool syncDisplayStyle = syncOptions?["displayStyle"]?.Value<bool>() ?? true;

            try
            {
                if (syncScale && targetView.ViewType != ViewType.ThreeD)
                {
                    try { targetView.Scale = sourceView.Scale; } catch { }
                }

                if (syncCropBox && sourceView.CropBoxActive)
                {
                    try
                    {
                        targetView.CropBoxActive = true;
                        targetView.CropBoxVisible = sourceView.CropBoxVisible;
                        // Only copy CropBox bounds for non-section views (sections already have the box from creation)
                        if (targetView.ViewType != ViewType.Section && targetView.ViewType != ViewType.Detail)
                        {
                            BoundingBoxXYZ newCrop = new BoundingBoxXYZ();
                            newCrop.Min = sourceView.CropBox.Min;
                            newCrop.Max = sourceView.CropBox.Max;
                            newCrop.Transform = sourceView.CropBox.Transform;
                            targetView.CropBox = newCrop;
                        }
                    }
                    catch { }
                }

                if (syncTemplate && sourceView.ViewTemplateId != ElementId.InvalidElementId)
                {
                    var sourceTemplateName = (sourceDoc.GetElement(sourceView.ViewTemplateId) as View)?.Name;
                    if (!string.IsNullOrEmpty(sourceTemplateName))
                    {
                        ElementId targetTemplateId = ElementId.InvalidElementId;

                        if (copiedTemplateCache.TryGetValue(sourceTemplateName, out ElementId cachedId))
                        {
                            targetTemplateId = cachedId;
                        }
                        else
                        {
                            var existingTemplate = new FilteredElementCollector(targetDoc)
                                .OfClass(typeof(View)).Cast<View>()
                                .FirstOrDefault(v => v.IsTemplate && v.Name == sourceTemplateName);

                            if (existingTemplate != null)
                            {
                                targetTemplateId = existingTemplate.Id;
                                copiedTemplateCache[sourceTemplateName] = targetTemplateId;
                            }
                        }

                        if (targetTemplateId == ElementId.InvalidElementId)
                        {
                            try
                            {
                                var copyOpts = new CopyPasteOptions();
                                copyOpts.SetDuplicateTypeNamesHandler(
                                    new ConflictTrackingHandler("use_destination", new List<object>()));

                                var copiedIds = ElementTransformUtils.CopyElements(
                                    sourceDoc,
                                    new List<ElementId> { sourceView.ViewTemplateId },
                                    targetDoc, Transform.Identity, copyOpts);

                                if (copiedIds != null && copiedIds.Count > 0)
                                {
                                    var copiedTemplate = targetDoc.GetElement(copiedIds.First()) as View;
                                    if (copiedTemplate != null && copiedTemplate.IsTemplate)
                                    {
                                        targetTemplateId = copiedTemplate.Id;
                                        copiedTemplateCache[sourceTemplateName] = targetTemplateId;
                                        viewTemplatesCopied.Add(new
                                        {
                                            TemplateName = sourceTemplateName,
                                            TargetElementId = targetTemplateId.GetIdValue()
                                        });
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                warnings.Add($"ViewTemplate '{sourceTemplateName}' 從來源複製失敗: {ex.Message}");
                            }
                        }

                        if (targetTemplateId != ElementId.InvalidElementId)
                        {
                            try { targetView.ViewTemplateId = targetTemplateId; } catch { }
                        }
                    }
                }

                if (syncDetailLevel)
                {
                    try { targetView.DetailLevel = sourceView.DetailLevel; } catch { }
                }

                if (syncDisplayStyle)
                {
                    try { targetView.DisplayStyle = sourceView.DisplayStyle; } catch { }
                }
            }
            catch { /* property sync is best-effort */ }
        }

        private ElementId FindViewFamilyTypeId(Document doc, ViewFamily family)
        {
            var vft = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>()
                .FirstOrDefault(x => x.ViewFamily == family);

            if (vft == null)
                throw new Exception($"目標檔找不到 ViewFamilyType: {family}");

            return vft.Id;
        }

        private ElementId FindMatchingTitleBlock(Document sourceDoc, Document targetDoc, ViewSheet sourceSheet)
        {
            var sourceTb = new FilteredElementCollector(sourceDoc, sourceSheet.Id)
                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                .FirstOrDefault();

            string targetFamilyName = null;
            if (sourceTb != null)
                targetFamilyName = sourceTb.get_Parameter(BuiltInParameter.ELEM_FAMILY_PARAM)?.AsValueString();

            // Try to find matching title block in target
            var targetTbTypes = new FilteredElementCollector(targetDoc)
                .OfClass(typeof(FamilySymbol))
                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                .Cast<FamilySymbol>()
                .ToList();

            if (!string.IsNullOrEmpty(targetFamilyName))
            {
                var match = targetTbTypes.FirstOrDefault(t => t.FamilyName == targetFamilyName);
                if (match != null) return match.Id;
            }

            // Fallback: use first available
            if (targetTbTypes.Any())
                return targetTbTypes.First().Id;

            throw new Exception("目標檔中找不到任何 Title Block 類型");
        }

        private object BuildManualAction(View sourceView, Document sourceDoc, string sheetNumber)
        {
            string levelName = null;
            if (sourceView is ViewPlan vp)
                levelName = vp.GenLevel?.Name;

            string viewTemplateName = null;
            if (sourceView.ViewTemplateId != ElementId.InvalidElementId)
                viewTemplateName = (sourceDoc.GetElement(sourceView.ViewTemplateId) as View)?.Name;

            string reason;
            string suggestedAction;
            switch (sourceView.ViewType)
            {
                case ViewType.Elevation:
                    reason = "Elevation 需要手動建立 ElevationMarker 並指定方向";
                    suggestedAction = "在目標檔中手動建立同名 Elevation view，然後重新執行 copy_sheets_from_file 以 match_only 策略放置";
                    break;
                case ViewType.AreaPlan:
                    reason = "AreaPlan 需要對應的 AreaScheme 才能建立";
                    suggestedAction = "確認目標檔有對應的 AreaScheme 後手動建立";
                    break;
                case ViewType.Rendering:
                    reason = "Rendering 需要手動設定相機與渲染參數";
                    suggestedAction = "在目標檔中手動建立 3D view 並設定渲染";
                    break;
                default:
                    reason = $"{sourceView.ViewType} 需要人工處理";
                    suggestedAction = "在目標檔中手動建立同名 view";
                    break;
            }

            // Check for callout
            try
            {
                var parentParam = sourceView.get_Parameter(BuiltInParameter.SECTION_PARENT_VIEW_NAME);
                if (parentParam != null && !string.IsNullOrEmpty(parentParam.AsString()))
                {
                    reason = "Callout 需要先建立 parent view 再手動建立";
                    suggestedAction = $"先確認 parent view '{parentParam.AsString()}' 存在，再手動建立 Callout";
                }
            }
            catch { }

            return new
            {
                SheetNumber = sheetNumber,
                Type = sourceView.ViewType.ToString(),
                ViewName = sourceView.Name,
                Reason = reason,
                SuggestedAction = suggestedAction,
                SourceProperties = new
                {
                    Scale = sourceView.Scale,
                    LevelName = levelName,
                    ViewTemplateName = viewTemplateName,
                    DetailLevel = sourceView.DetailLevel.ToString(),
                    DisplayStyle = sourceView.DisplayStyle.ToString()
                }
            };
        }

        private string GenerateUniqueName(string baseName, Dictionary<string, View> existingNames)
        {
            int suffix = 2;
            string candidate = $"{baseName} ({suffix})";
            while (existingNames.ContainsKey(candidate))
            {
                suffix++;
                candidate = $"{baseName} ({suffix})";
                if (suffix > 100) break;
            }
            return candidate;
        }

        /// <summary>
        /// 複製 source sheet 的所有可寫 custom parameter 到 target sheet（同名匹配）。
        /// 跳過 SHEET_NUMBER, SHEET_NAME (已另外處理) 及 ElementId 類型 (跨文件 ID 不可移植)。
        /// </summary>
        private void CopySheetCustomParameters(ViewSheet sourceSheet, ViewSheet targetSheet, List<string> warnings)
        {
            var skipBuiltIns = new HashSet<BuiltInParameter>
            {
                BuiltInParameter.SHEET_NUMBER,
                BuiltInParameter.SHEET_NAME,
                // VIEWPORT_SHEET_NUMBER and similar appear with same display name "Sheet Number"
                // but are different BuiltInParameters — caught by name skip below
            };

            // 也用 NAME 跳過 (處理 source 含同名 duplicate parameter 的情況, 例如 sheet 有兩個叫 "Sheet Number" 的 parameter,
            // 一個是 SHEET_NUMBER, 一個是 VIEWPORT_SHEET_NUMBER 但顯示名稱都是 "Sheet Number".
            // 若不用 name skip, LookupParameter("Sheet Number") 會回傳真正的 SHEET_NUMBER 然後被空字串覆蓋, 導致 Revit 報錯)
            var skipNames = new HashSet<string>(StringComparer.Ordinal)
            {
                "Sheet Number",
                "Sheet Name",
            };

            int copiedCount = 0;

            foreach (Parameter sourceParam in sourceSheet.Parameters)
            {
                try
                {
                    // Skip BuiltInParameter we already handled
                    if (sourceParam.Definition is InternalDefinition intDef &&
                        skipBuiltIns.Contains(intDef.BuiltInParameter))
                        continue;

                    // Skip by name (catches duplicate-name parameters)
                    if (skipNames.Contains(sourceParam.Definition.Name))
                        continue;

                    // Skip ElementId (cross-doc IDs not portable)
                    if (sourceParam.StorageType == StorageType.ElementId)
                        continue;

                    // Skip None type
                    if (sourceParam.StorageType == StorageType.None)
                        continue;

                    string paramName = sourceParam.Definition.Name;
                    Parameter targetParam = targetSheet.LookupParameter(paramName);
                    if (targetParam == null)
                        continue;

                    if (targetParam.IsReadOnly)
                        continue;

                    if (targetParam.StorageType != sourceParam.StorageType)
                    {
                        warnings.Add($"Sheet '{sourceSheet.SheetNumber}': parameter '{paramName}' StorageType 不一致 (source={sourceParam.StorageType}, target={targetParam.StorageType}), 跳過");
                        continue;
                    }

                    switch (sourceParam.StorageType)
                    {
                        case StorageType.String:
                            string strVal = sourceParam.AsString();
                            if (strVal != null)
                            {
                                targetParam.Set(strVal);
                                copiedCount++;
                            }
                            break;
                        case StorageType.Double:
                            // 只有 source 有設定值才複製 (避免覆蓋 target 預設成 0.0)
                            if (sourceParam.HasValue)
                            {
                                targetParam.Set(sourceParam.AsDouble());
                                copiedCount++;
                            }
                            break;
                        case StorageType.Integer:
                            if (sourceParam.HasValue)
                            {
                                targetParam.Set(sourceParam.AsInteger());
                                copiedCount++;
                            }
                            break;
                    }
                }
                catch (Exception ex)
                {
                    warnings.Add($"Sheet '{sourceSheet.SheetNumber}': 複製 parameter '{sourceParam.Definition.Name}' 失敗: {ex.Message}");
                }
            }

            if (copiedCount > 0)
                Logger.Info($"Sheet '{sourceSheet.SheetNumber}': 複製了 {copiedCount} 個 custom parameter 到 target");
        }

        /// <summary>
        /// 補丁工具：為 target 中已存在的 sheets 從 source 同號 sheet 補齊 custom parameters
        /// （例如修補先前複製時遺漏的 sheet folder 資訊）
        /// </summary>
        private object SyncSheetParametersFromSource(JObject parameters)
        {
            string sourceFilePath = parameters["sourceFilePath"]?.Value<string>();
            if (string.IsNullOrEmpty(sourceFilePath))
                throw new Exception("必須指定 sourceFilePath");

            var sheetNumbersFilter = parameters["sheetNumbers"]?.ToObject<List<string>>();
            bool closeAfterSync = parameters["closeAfterSync"]?.Value<bool>() ?? true;

            var (sourceDoc, needToClose) = OpenSourceDocument(sourceFilePath);

            try
            {
                Document targetDoc = _uiApp.ActiveUIDocument.Document;

                // Build target lookup: SheetNumber → ViewSheet
                var targetSheetsByNumber = new FilteredElementCollector(targetDoc)
                    .OfClass(typeof(ViewSheet)).Cast<ViewSheet>()
                    .Where(s => !s.IsPlaceholder)
                    .ToDictionary(s => s.SheetNumber, s => s);

                // Get source sheets (filter by sheetNumbers if provided)
                var sourceSheets = new FilteredElementCollector(sourceDoc)
                    .OfClass(typeof(ViewSheet)).Cast<ViewSheet>()
                    .Where(s => !s.IsPlaceholder)
                    .Where(s => sheetNumbersFilter == null || sheetNumbersFilter.Count == 0 || sheetNumbersFilter.Contains(s.SheetNumber))
                    .ToList();

                var matched = new List<object>();
                var notMatched = new List<object>();
                var warnings = new List<string>();

                using (Transaction trans = TransactionHelper.Begin(targetDoc, "從 source 同步 sheet custom parameters"))
                {
                    trans.Start();

                    foreach (ViewSheet sourceSheet in sourceSheets)
                    {
                        if (!targetSheetsByNumber.TryGetValue(sourceSheet.SheetNumber, out ViewSheet targetSheet))
                        {
                            notMatched.Add(new
                            {
                                SheetNumber = sourceSheet.SheetNumber,
                                SheetName = sourceSheet.Name,
                                Reason = "target 中找不到同編號 sheet"
                            });
                            continue;
                        }

                        int beforeWarnings = warnings.Count;
                        CopySheetCustomParameters(sourceSheet, targetSheet, warnings);
                        int newWarningsThisSheet = warnings.Count - beforeWarnings;

                        matched.Add(new
                        {
                            SheetNumber = sourceSheet.SheetNumber,
                            TargetSheetId = targetSheet.Id.GetIdValue(),
                            TargetSheetName = targetSheet.Name,
                            WarningsForThisSheet = newWarningsThisSheet
                        });
                    }

                    trans.Commit();
                }

                return new
                {
                    Success = true,
                    SourceFile = sourceFilePath,
                    Summary = new
                    {
                        SourceSheetsScanned = sourceSheets.Count,
                        MatchedCount = matched.Count,
                        NotMatchedCount = notMatched.Count,
                        WarningsTotal = warnings.Count
                    },
                    Matched = matched,
                    NotMatched = notMatched,
                    Warnings = warnings
                };
            }
            finally
            {
                if (closeAfterSync && needToClose)
                    sourceDoc.Close(false);
            }
        }

        #endregion
    }
}
