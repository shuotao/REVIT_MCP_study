using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;

#if REVIT2025_OR_GREATER
using IdType = System.Int64;
#else
using IdType = System.Int32;
#endif

namespace RevitMCP.Core
{
    /// <summary>
    /// 抑制 Revit 在 Transaction 中跳出的 warning 對話框，
    /// 把 warning 訊息收集起來並 delete 掉，避免阻塞 UI thread。
    /// 用於 dimension 跨 view 複製時的 "some dimensions don't have the host" 等可忽略警告。
    /// </summary>
    internal class SuppressWarningsPreprocessor : IFailuresPreprocessor
    {
        private readonly List<string> _suppressedMessages;

        public SuppressWarningsPreprocessor(List<string> suppressedMessages)
        {
            _suppressedMessages = suppressedMessages;
        }

        public FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor)
        {
            var failures = failuresAccessor.GetFailureMessages();
            foreach (var failure in failures)
            {
                if (failure.GetSeverity() == FailureSeverity.Warning)
                {
                    _suppressedMessages.Add(failure.GetDescriptionText());
                    failuresAccessor.DeleteWarning(failure);
                }
            }
            return FailureProcessingResult.Continue;
        }
    }

    public partial class CommandExecutor
    {
        #region Detail Items Copy

        private object CopyDetailItemsToViews(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;

            var sourceViewId = parameters["sourceViewId"]?.Value<IdType>()
                ?? throw new Exception("必須指定 sourceViewId");
            var targetViewIdValues = parameters["targetViewIds"]?.ToObject<List<IdType>>()
                ?? throw new Exception("必須指定 targetViewIds");

            if (targetViewIdValues.Count == 0)
                throw new Exception("targetViewIds 不可為空");

            var sourceView = doc.GetElement(RevitCompatibility.ToElementId(sourceViewId)) as View;
            if (sourceView == null)
                throw new Exception($"找不到來源視圖 (ID: {sourceViewId})");
            if (sourceView.IsTemplate)
                throw new Exception($"來源視圖 '{sourceView.Name}' 是 ViewTemplate，不包含詳圖項目");

            var categories = parameters["elementCategories"]?.ToObject<List<string>>()
                ?? new List<string> { "All" };
            bool copyAll = categories.Contains("All");

            bool dryRun = parameters["dryRun"]?.Value<bool>() ?? false;
            bool preserveGroups = parameters["preserveGroups"]?.Value<bool>() ?? true;
            bool fallbackToIndividual = parameters["fallbackToIndividual"]?.Value<bool>() ?? true;

            double offsetXmm = parameters["offset"]?["x"]?.Value<double>() ?? 0;
            double offsetYmm = parameters["offset"]?["y"]?.Value<double>() ?? 0;

            // Collect source element IDs
            ICollection<ElementId> elementIds;
            var specifiedIds = parameters["sourceElementIds"]?.ToObject<List<IdType>>();

            int detailCurveCount = 0, textNoteCount = 0, filledRegionCount = 0, detailComponentCount = 0, dimensionCount = 0, groupCount = 0;

            if (specifiedIds != null && specifiedIds.Count > 0)
            {
                elementIds = specifiedIds
                    .Select(id => RevitCompatibility.ToElementId(id))
                    .Where(id => doc.GetElement(id) != null)
                    .ToList();

                foreach (var eid in elementIds)
                {
                    var el = doc.GetElement(eid);
                    if (el is DetailCurve) detailCurveCount++;
                    else if (el is TextNote) textNoteCount++;
                    else if (el is FilledRegion) filledRegionCount++;
                    else if (el is Dimension) dimensionCount++;
                    else if (el.Category != null &&
                             el.Category.Id == new ElementId(BuiltInCategory.OST_DetailComponents))
                        detailComponentCount++;
                }
            }
            else
            {
                var collected = new HashSet<ElementId>();

                if (copyAll || categories.Contains("DetailCurves"))
                {
                    var items = new FilteredElementCollector(doc, sourceView.Id)
                        .OfClass(typeof(CurveElement))
                        .Where(e => e is DetailCurve)
                        .Select(e => e.Id)
                        .ToList();
                    detailCurveCount = items.Count;
                    foreach (var id in items) collected.Add(id);
                }

                if (copyAll || categories.Contains("TextNotes"))
                {
                    var items = new FilteredElementCollector(doc, sourceView.Id)
                        .OfClass(typeof(TextNote))
                        .Select(e => e.Id)
                        .ToList();
                    textNoteCount = items.Count;
                    foreach (var id in items) collected.Add(id);
                }

                if (copyAll || categories.Contains("FilledRegions"))
                {
                    var items = new FilteredElementCollector(doc, sourceView.Id)
                        .OfClass(typeof(FilledRegion))
                        .Select(e => e.Id)
                        .ToList();
                    filledRegionCount = items.Count;
                    foreach (var id in items) collected.Add(id);
                }

                if (copyAll || categories.Contains("DetailComponents"))
                {
                    var items = new FilteredElementCollector(doc, sourceView.Id)
                        .OfCategory(BuiltInCategory.OST_DetailComponents)
                        .WhereElementIsNotElementType()
                        .Select(e => e.Id)
                        .ToList();
                    detailComponentCount = items.Count;
                    foreach (var id in items) collected.Add(id);
                }

                if (copyAll || categories.Contains("Dimensions"))
                {
                    var items = new FilteredElementCollector(doc, sourceView.Id)
                        .OfClass(typeof(Dimension))
                        .WhereElementIsNotElementType()
                        .Select(e => e.Id)
                        .ToList();
                    dimensionCount = items.Count;
                    foreach (var id in items) collected.Add(id);
                }

                elementIds = collected.ToList();
            }

            // === Group 保留處理 ===
            // 預設把 detail group / model group 的成員 id 替換成 group instance id，
            // 確保 ElementTransformUtils.CopyElements 把整個 group 當原子單位複製，
            // 而不是攤平成個別元素丟失 group 結構。
            if (preserveGroups && elementIds.Count > 0)
            {
                var memberToGroup = new Dictionary<ElementId, ElementId>();
                var groupsInView = new FilteredElementCollector(doc, sourceView.Id)
                    .OfClass(typeof(Group))
                    .Cast<Group>()
                    .ToList();
                foreach (var g in groupsInView)
                {
                    foreach (var mid in g.GetMemberIds())
                    {
                        memberToGroup[mid] = g.Id;
                    }
                }

                if (memberToGroup.Count > 0)
                {
                    var collapsed = new HashSet<ElementId>();
                    var addedGroupIds = new HashSet<ElementId>();
                    foreach (var id in elementIds)
                    {
                        if (memberToGroup.TryGetValue(id, out var gid))
                        {
                            collapsed.Add(gid);
                            addedGroupIds.Add(gid);
                        }
                        else
                        {
                            collapsed.Add(id);
                        }
                    }
                    elementIds = collapsed.ToList();
                    groupCount = addedGroupIds.Count;
                }
            }

            int totalSource = elementIds.Count;
            var summary = new
            {
                DetailCurves = detailCurveCount,
                TextNotes = textNoteCount,
                FilledRegions = filledRegionCount,
                DetailComponents = detailComponentCount,
                Dimensions = dimensionCount,
                Groups = groupCount,
                Total = totalSource
            };

            if (dryRun)
            {
                return new
                {
                    Success = true,
                    DryRun = true,
                    SourceViewId = sourceViewId,
                    SourceViewName = sourceView.Name,
                    SourceElementSummary = summary,
                    TargetViewCount = targetViewIdValues.Count
                };
            }

            if (totalSource == 0)
            {
                return new
                {
                    Success = true,
                    SourceViewId = sourceViewId,
                    SourceViewName = sourceView.Name,
                    SourceElementSummary = summary,
                    Message = "來源視圖中沒有符合條件的詳圖項目",
                    TotalElementsCopied = 0
                };
            }

            // Build transform
            Transform transform = Transform.Identity;
            if (Math.Abs(offsetXmm) > 0.001 || Math.Abs(offsetYmm) > 0.001)
            {
                double xFeet = offsetXmm / 304.8;
                double yFeet = offsetYmm / 304.8;
                transform = Transform.CreateTranslation(new XYZ(xFeet, yFeet, 0));
            }

            var results = new List<object>();
            int totalCopied = 0;

            foreach (var targetIdVal in targetViewIdValues)
            {
                var targetView = doc.GetElement(RevitCompatibility.ToElementId(targetIdVal)) as View;
                if (targetView == null)
                {
                    results.Add(new
                    {
                        TargetViewId = targetIdVal,
                        TargetViewName = (string)null,
                        Status = "Skipped",
                        Reason = $"找不到視圖 (ID: {targetIdVal})"
                    });
                    continue;
                }

                if (RevitCompatibility.GetIdValue(targetView.Id) == sourceViewId)
                {
                    results.Add(new
                    {
                        TargetViewId = targetIdVal,
                        TargetViewName = targetView.Name,
                        Status = "Skipped",
                        Reason = "目標視圖與來源視圖相同"
                    });
                    continue;
                }

                if (targetView.IsTemplate)
                {
                    results.Add(new
                    {
                        TargetViewId = targetIdVal,
                        TargetViewName = targetView.Name,
                        Status = "Skipped",
                        Reason = "目標視圖是 ViewTemplate"
                    });
                    continue;
                }

                var suppressedWarnings = new List<string>();
                var typeConflicts = new List<object>();
                var handler = new ConflictTrackingHandler("use_destination", typeConflicts);
                int copied = 0;
                bool batchSucceeded = false;
                List<object> failedItems = null;
                string batchErrorReason = null;

                // === 階段 1: 嘗試批次複製 ===
                try
                {
                    using (Transaction trans = new Transaction(doc,
                        $"Batch copy detail items to '{targetView.Name}'"))
                    {
                        var failOpts = trans.GetFailureHandlingOptions();
                        failOpts.SetFailuresPreprocessor(new SuppressWarningsPreprocessor(suppressedWarnings));
                        failOpts.SetForcedModalHandling(false);
                        failOpts.SetClearAfterRollback(true);
                        trans.SetFailureHandlingOptions(failOpts);

                        trans.Start();

                        var opts = new CopyPasteOptions();
                        opts.SetDuplicateTypeNamesHandler(handler);

                        var newIds = ElementTransformUtils.CopyElements(
                            sourceView, elementIds, targetView, transform, opts);

                        trans.Commit();

                        copied = newIds?.Count ?? 0;
                        batchSucceeded = true;
                    }
                }
                catch (Exception batchEx)
                {
                    batchErrorReason = batchEx.Message;
                }

                // === 階段 2: Individual fallback（批次失敗 + fallbackToIndividual=true） ===
                if (!batchSucceeded && fallbackToIndividual)
                {
                    failedItems = new List<object>();
                    foreach (var id in elementIds)
                    {
                        try
                        {
                            using (Transaction tInner = new Transaction(doc,
                                $"Copy single element to '{targetView.Name}'"))
                            {
                                var foInner = tInner.GetFailureHandlingOptions();
                                foInner.SetFailuresPreprocessor(new SuppressWarningsPreprocessor(suppressedWarnings));
                                foInner.SetForcedModalHandling(false);
                                foInner.SetClearAfterRollback(true);
                                tInner.SetFailureHandlingOptions(foInner);

                                tInner.Start();

                                var optsInner = new CopyPasteOptions();
                                optsInner.SetDuplicateTypeNamesHandler(handler);

                                var newIds = ElementTransformUtils.CopyElements(
                                    sourceView, new ElementId[] { id }, targetView, transform, optsInner);

                                tInner.Commit();
                                copied += newIds?.Count ?? 0;
                            }
                        }
                        catch (Exception itemEx)
                        {
                            failedItems.Add(new
                            {
                                ElementId = RevitCompatibility.GetIdValue(id),
                                Reason = itemEx.Message
                            });
                        }
                    }
                }

                totalCopied += copied;

                // 統計 suppressed warning 訊息（去重 + 取前 5 種）
                var warningSummary = suppressedWarnings
                    .GroupBy(m => m)
                    .Select(g => new { Message = g.Key, Count = g.Count() })
                    .OrderByDescending(x => x.Count)
                    .Take(5)
                    .ToList<object>();

                // 決定 status
                string status;
                string mode;
                if (batchSucceeded)
                {
                    status = "Success";
                    mode = "Batch";
                }
                else if (failedItems == null)
                {
                    // batch 失敗且未 fallback
                    status = "Failed";
                    mode = "Batch";
                }
                else if (failedItems.Count == 0)
                {
                    status = "Success";
                    mode = "Individual";
                }
                else if (failedItems.Count < elementIds.Count)
                {
                    status = "PartialSuccess";
                    mode = "Individual";
                }
                else
                {
                    status = "Failed";
                    mode = "Individual";
                }

                results.Add(new
                {
                    TargetViewId = targetIdVal,
                    TargetViewName = targetView.Name,
                    Status = status,
                    Mode = mode,
                    ElementsCopied = copied,
                    FailedCount = failedItems?.Count ?? 0,
                    FailedElements = failedItems ?? new List<object>(),
                    BatchErrorReason = batchSucceeded ? null : batchErrorReason,
                    TypeConflicts = typeConflicts.Count,
                    SuppressedWarnings = suppressedWarnings.Count,
                    WarningSummary = warningSummary
                });
            }

            return new
            {
                Success = true,
                SourceViewId = sourceViewId,
                SourceViewName = sourceView.Name,
                SourceElementSummary = summary,
                Results = results,
                TotalElementsCopied = totalCopied
            };
        }

        #endregion
    }
}
