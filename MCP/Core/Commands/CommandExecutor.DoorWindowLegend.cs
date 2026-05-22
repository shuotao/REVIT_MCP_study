using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
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
    public partial class CommandExecutor
    {
        private const string AwaitingSeedSelectionState = "awaiting_seed_selection";
        private const string AwaitingUserChoiceState = "awaiting_user_choice";
        private const string AwaitingLayoutPreferencesState = "awaiting_layout_preferences";
        private const string AwaitingValidLayoutPreferencesState = "awaiting_valid_layout_preferences";
        private const string MissingCreateLayoutError = "create_mode_requires_layout_direction_and_max_per_line";
        private const string InvalidSeedTypeError = "invalid_seed_type";
        private const string LegendSeedViewNotFoundError = "legend_seed_view_not_found";
        private const string LegendSeedComponentNotFoundError = "legend_seed_component_not_found";
        private const string LegendSeedComponentTypeMismatchError = "legend_seed_component_type_mismatch";
        private const string LegendComponentTypeSwapFailedError = "legend_component_type_swap_failed";

        private const double HorizontalSpacingCm = 500.0;
        private const double VerticalSpacingCm = 450.0;
        private const double LabelOffsetCm = 35.0;
        private const double CmToFeet = 0.0328083989501312;

        private object DoorWindowLegendTools(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;

            string targetType = parameters?["targetType"]?.Value<string>()?.Trim().ToLowerInvariant();
            string mode = parameters?["mode"]?.Value<string>()?.Trim().ToLowerInvariant();

            if (targetType != "door" && targetType != "window")
                throw new Exception("targetType 必須是 door 或 window");

            if (mode != "list" && mode != "create")
                throw new Exception("mode 必須是 list 或 create");

            List<DoorWindowLegendTypeInfo> usedTypes = SortDoorWindowTypesByTypeMark(
                CollectUsedDoorWindowTypes(doc, targetType));

            if (mode == "list")
                return BuildDoorWindowLegendListResult(targetType, usedTypes);

            string layoutDirection = parameters?["layoutDirection"]?.Value<string>()?.Trim().ToLowerInvariant();
            int? maxPerLine = parameters?["maxPerLine"]?.Value<int?>();
            IdType? seedLegendViewId = parameters?["seedLegendViewId"]?.Value<IdType?>();

            if (!seedLegendViewId.HasValue)
            {
                return new
                {
                    TargetType = targetType,
                    DisplayName = GetDoorWindowDisplayName(targetType),
                    WorkflowState = AwaitingSeedSelectionState,
                    NextAction = "call_list_seeds",
                    SeedTypeRequired = "legend",
                    RequiresUserInput = true,
                    DoNotAutoSelectSeed = true,
                    DoNotRetryWithOtherSeeds = true,
                    PromptToUser = "請先從 list_seeds 的結果中選擇一個 ViewName 作為 seed。",
                    Message = "建立門表或窗表前，需要先選擇 seed Legend 視圖。",
                };
            }

            List<string> missingFields = new List<string>();
            if (string.IsNullOrWhiteSpace(layoutDirection))
                missingFields.Add("layoutDirection");
            if (!maxPerLine.HasValue)
                missingFields.Add("maxPerLine");

            if (missingFields.Count > 0)
            {
                return new
                {
                    TargetType = targetType,
                    DisplayName = GetDoorWindowDisplayName(targetType),
                    WorkflowState = AwaitingLayoutPreferencesState,
                    NextAction = "ask_layout_preferences",
                    RequiresUserInput = true,
                    DoNotAutoAssignLayout = true,
                    DoNotRetryCreateWithoutLayout = true,
                    MissingFields = missingFields,
                    PromptToUser = "請選擇排版方向（horizontal 或 vertical），並提供每排/欄數量（maxPerLine）。",
                    Message = "建立門表或窗表前，需要先提供排版方向與每排/欄數量。",
                };
            }

            List<string> invalidFields = new List<string>();
            if (layoutDirection != "horizontal" && layoutDirection != "vertical")
                invalidFields.Add("layoutDirection");
            if (maxPerLine.Value < 1)
                invalidFields.Add("maxPerLine");

            if (invalidFields.Count > 0)
            {
                return new
                {
                    TargetType = targetType,
                    DisplayName = GetDoorWindowDisplayName(targetType),
                    WorkflowState = AwaitingValidLayoutPreferencesState,
                    NextAction = "ask_layout_preferences",
                    RequiresUserInput = true,
                    DoNotAutoAssignLayout = true,
                    DoNotRetryCreateWithoutLayout = true,
                    InvalidFields = invalidFields,
                    PromptToUser = "請提供有效的排版方向（horizontal 或 vertical），以及大於等於 1 的 maxPerLine。",
                    Message = "排版參數無效，請重新提供 layoutDirection 與 maxPerLine。",
                };
            }

            return CreateDoorWindowLegend(
                doc,
                targetType,
                layoutDirection,
                maxPerLine.Value,
                usedTypes,
                seedLegendViewId.Value);
        }

        private object ListSeeds(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;
            string seedType = parameters?["seedType"]?.Value<string>()?.Trim().ToLowerInvariant();

            if (seedType != "legend")
                throw new Exception(InvalidSeedTypeError);

            List<object> seeds = ListLegendSeedCandidates(doc);
            return new
            {
                SeedType = "legend",
                Count = seeds.Count,
                WorkflowState = AwaitingUserChoiceState,
                SelectionMode = "user_must_choose",
                SelectionField = "ViewName",
                RequiresUserInput = true,
                DoNotAutoSelect = true,
                DoNotAutoRetryCreate = true,
                PromptToUser = "請從以下 Legend 視圖中選一個 ViewName 作為 seed。",
                Seeds = seeds,
            };
        }

        private object BuildDoorWindowLegendListResult(string targetType, List<DoorWindowLegendTypeInfo> usedTypes)
        {
            return new
            {
                TargetType = targetType,
                DisplayName = GetDoorWindowDisplayName(targetType),
                Count = usedTypes.Count,
                Types = usedTypes.Select(t => new
                {
                    TypeId = t.TypeId.GetIdValue(),
                    TypeMarkRaw = t.TypeMarkRaw,
                    TypeMarkDisplay = t.TypeMarkDisplay,
                    TypeName = t.TypeName,
                }).ToList(),
                SuggestedAction = "create_legend",
            };
        }

        private object CreateDoorWindowLegend(
            Document doc,
            string targetType,
            string layoutDirection,
            int maxPerLine,
            List<DoorWindowLegendTypeInfo> usedTypes,
            IdType seedLegendViewId)
        {
            View seedView = GetLegendViewById(doc, seedLegendViewId);
            if (seedView == null)
            {
                return new
                {
                    TargetType = targetType,
                    DisplayName = GetDoorWindowDisplayName(targetType),
                    ErrorCode = LegendSeedViewNotFoundError,
                    Message = $"找不到 viewId={seedLegendViewId} 的有效 seed Legend，請重新選擇 seed。",
                };
            }

            List<ElementId> seedComponentIds = CollectLegendComponentIds(doc, seedView);
            if (seedComponentIds.Count == 0)
            {
                return new
                {
                    TargetType = targetType,
                    DisplayName = GetDoorWindowDisplayName(targetType),
                    ErrorCode = LegendSeedComponentNotFoundError,
                    SeedLegendViewId = SafeGetElementIdValue(seedView),
                    SeedLegendViewName = SafeGetViewName(seedView),
                    Message = $"Seed Legend「{SafeGetViewName(seedView)}」內沒有可用的 Legend Component。",
                    SeedViewDebug = BuildLegendViewDebug(doc, seedView),
                };
            }

            try
            {
                return CreateDoorWindowLegendFromSeed(
                    doc,
                    targetType,
                    layoutDirection,
                    maxPerLine,
                    usedTypes,
                    seedView);
            }
            catch (Exception ex)
            {
                Logger.Error($"door-window-legend-tools create failed before entering seed flow. targetType={targetType}, seedLegendViewId={seedLegendViewId}", ex);
                return new
                {
                    TargetType = targetType,
                    DisplayName = GetDoorWindowDisplayName(targetType),
                    ErrorCode = LegendSeedComponentTypeMismatchError,
                    SeedLegendViewId = SafeGetElementIdValue(seedView),
                    SeedLegendViewName = SafeGetViewName(seedView),
                    Message = ex.Message,
                    SeedViewDebug = BuildLegendViewDebug(doc, seedView),
                };
            }
        }

        private object CreateDoorWindowLegendFromSeed(
            Document doc,
            string targetType,
            string layoutDirection,
            int maxPerLine,
            List<DoorWindowLegendTypeInfo> usedTypes,
            View seedView)
        {
            string defaultLegendName = targetType == "door" ? "門表" : "窗表";
            object seedViewIdValue = SafeGetElementIdValue(seedView);
            string seedViewName = SafeGetViewName(seedView);

            using (TransactionGroup group = new TransactionGroup(doc, $"建立{defaultLegendName}"))
            {
                group.Start();

                ElementId newLegendId = ElementId.InvalidElementId;
                string newLegendName = string.Empty;
                int placedCount = 0;
                List<DoorWindowLegendFailedType> failedTypes = new List<DoorWindowLegendFailedType>();
                List<object> attemptDebug = new List<object>();
                object duplicatedViewDebug = null;
                DoorWindowLegendCleanupResult cleanupResult = DoorWindowLegendCleanupResult.Skipped();
                int seedOriginalElementCount = 0;
                int generatedElementCount = 0;
                int finalViewElementCountBeforeCleanup = 0;
                int finalViewElementCountAfterCleanup = 0;

                using (Transaction trans = new Transaction(doc, $"建立{defaultLegendName} Legend"))
                {
                    trans.Start();

                    View legendView = DuplicateLegendView(doc, seedView, defaultLegendName);
                    legendView.Scale = 50;
                    doc.Regenerate();

                    newLegendId = legendView.Id;
                    newLegendName = legendView.Name;
                    List<ElementId> seedOriginalElementIds = CollectViewElementIds(doc, legendView);
                    seedOriginalElementCount = seedOriginalElementIds.Count;
                    List<ElementId> generatedElementIds = new List<ElementId>();

                    if (usedTypes.Count == 0)
                    {
                        cleanupResult = DeleteSeedOriginalIntersection(doc, legendView, seedOriginalElementIds);
                        finalViewElementCountBeforeCleanup = cleanupResult.FinalViewElementCountBeforeCleanup;
                        finalViewElementCountAfterCleanup = cleanupResult.FinalViewElementCountAfterCleanup;
                        Logger.Info($"door-window-legend-tools cleanup completed. mode=delete_seed_original_ids_one_by_one, seedOriginalElementCount={seedOriginalElementCount}, protectedCount={cleanupResult.ProtectedElementCount}, finalBefore={finalViewElementCountBeforeCleanup}, finalAfter={finalViewElementCountAfterCleanup}, deleted={cleanupResult.DeletedCount}, skipped={cleanupResult.SkippedCount}, reason={cleanupResult.Reason}");
                        duplicatedViewDebug = BuildLegendViewDebug(doc, legendView);
                    }
                    else
                    {
                        HashSet<IdType> seedOriginalIdValues = seedOriginalElementIds
                            .Where(IsValidElementId)
                            .Select(id => id.GetIdValue())
                            .ToHashSet();
                        List<ElementId> duplicatedSeedComponentIds = CollectLegendComponentIds(doc, legendView)
                            .Where(id => seedOriginalIdValues.Contains(id.GetIdValue()))
                            .ToList();
                        ElementId sourceSeedComponentId = duplicatedSeedComponentIds.FirstOrDefault(id => IsValidElementId(id) && doc.GetElement(id) != null);
                        if (!IsValidElementId(sourceSeedComponentId))
                        {
                            duplicatedViewDebug = BuildLegendViewDebug(doc, legendView);
                            Logger.Error($"door-window-legend-tools duplicated seed view has no usable source component. targetType={targetType}, seedView={seedViewName}({seedViewIdValue})");
                            trans.RollBack();
                            group.RollBack();
                            return new
                            {
                                TargetType = targetType,
                                DisplayName = GetDoorWindowDisplayName(targetType),
                                ErrorCode = LegendSeedComponentTypeMismatchError,
                                SeedLegendViewId = seedViewIdValue,
                                SeedLegendViewName = seedViewName,
                                Message = "duplicated Legend 視圖內找不到可讀取的 source Legend Component。",
                                SeedViewDebug = BuildLegendViewDebug(doc, seedView),
                                DuplicatedViewDebug = duplicatedViewDebug,
                                AttemptDebug = attemptDebug,
                            };
                        }

                        try
                        {
                            Logger.Info($"door-window-legend-tools create start. targetType={targetType}, seedView={seedViewName}({seedViewIdValue}), duplicatedView={newLegendName}({newLegendId.GetIdValue()}), usedTypeCount={usedTypes.Count}, sourceSeedComponentId={sourceSeedComponentId.GetIdValue()}");

                            placedCount = PlaceLegendItemsFromOriginalSeedSource(
                                doc,
                                legendView,
                                sourceSeedComponentId,
                                layoutDirection,
                                maxPerLine,
                                usedTypes,
                                failedTypes,
                                attemptDebug,
                                out generatedElementIds);
                            generatedElementCount = generatedElementIds.Count;

                            cleanupResult = DeleteSeedOriginalIntersection(doc, legendView, seedOriginalElementIds);
                            finalViewElementCountBeforeCleanup = cleanupResult.FinalViewElementCountBeforeCleanup;
                            finalViewElementCountAfterCleanup = cleanupResult.FinalViewElementCountAfterCleanup;
                            Logger.Info($"door-window-legend-tools cleanup completed. mode=delete_seed_original_ids_one_by_one, seedOriginalElementCount={seedOriginalElementCount}, protectedCount={cleanupResult.ProtectedElementCount}, finalBefore={finalViewElementCountBeforeCleanup}, finalAfter={finalViewElementCountAfterCleanup}, deleted={cleanupResult.DeletedCount}, skipped={cleanupResult.SkippedCount}, reason={cleanupResult.Reason}");
                            duplicatedViewDebug = BuildLegendViewDebug(doc, legendView);
                        }
                        catch (Exception ex)
                        {
                            duplicatedViewDebug = BuildLegendViewDebug(doc, legendView);
                            Logger.Error($"door-window-legend-tools placement failed. targetType={targetType}, seedView={seedViewName}({seedViewIdValue})", ex);
                            trans.RollBack();
                            group.RollBack();
                            return new
                            {
                                TargetType = targetType,
                                DisplayName = GetDoorWindowDisplayName(targetType),
                                ErrorCode = LegendSeedComponentTypeMismatchError,
                                SeedLegendViewId = seedViewIdValue,
                                SeedLegendViewName = seedViewName,
                                Message = ex.Message,
                                SeedViewDebug = BuildLegendViewDebug(doc, seedView),
                                DuplicatedViewDebug = duplicatedViewDebug,
                                AttemptDebug = attemptDebug,
                            };
                        }
                    }

                    trans.Commit();
                }

                group.Assimilate();

                View createdView = doc.GetElement(newLegendId) as View;
                if (createdView != null)
                    _uiApp.ActiveUIDocument.ActiveView = createdView;

                bool isEmptyLegend = usedTypes.Count == 0 || placedCount == 0;
                string message;
                if (usedTypes.Count == 0)
                    message = $"已建立空的{defaultLegendName}，seed 原始內容暫時保留。";
                else if (placedCount == 0)
                    message = $"已建立{defaultLegendName}，但所有 type 都無法轉成 Legend Component；seed 原始內容暫時保留。";
                else if (failedTypes.Count > 0)
                    message = $"已建立{defaultLegendName}，部分 type 建立失敗；seed 原始內容暫時保留。";
                else
                    message = $"已成功建立{defaultLegendName}，seed 原始內容暫時保留。";

                return new
                {
                    TargetType = targetType,
                    DisplayName = GetDoorWindowDisplayName(targetType),
                    ErrorCode = usedTypes.Count > 0 && placedCount == 0
                        ? LegendComponentTypeSwapFailedError
                        : null,
                    LegendViewId = newLegendId.GetIdValue(),
                    LegendViewName = newLegendName,
                    SeedLegendViewId = seedViewIdValue,
                    SeedLegendViewName = seedViewName,
                    UsedTypeCount = usedTypes.Count,
                    PlacedCount = placedCount,
                    FailedTypes = failedTypes.Select(f => new
                    {
                        TypeId = f.TypeId.GetIdValue(),
                        TypeMarkDisplay = f.TypeMarkDisplay,
                        TypeName = f.TypeName,
                        Reason = f.Reason,
                    }).ToList(),
                    IsEmptyLegend = isEmptyLegend,
                    CleanupSkipped = false,
                    CleanupMode = "delete_seed_original_ids_one_by_one",
                    CleanupDeletedCount = cleanupResult.DeletedCount,
                    CleanupSkippedCount = cleanupResult.SkippedCount,
                    CleanupSkippedOriginalIds = cleanupResult.SkippedOriginalIds,
                    CleanupProtectedElementCount = cleanupResult.ProtectedElementCount,
                    CleanupDeletedElementIds = cleanupResult.DeletedElementIds,
                    CleanupReason = cleanupResult.Reason,
                    SeedOriginalElementCount = seedOriginalElementCount,
                    GeneratedElementCount = generatedElementCount,
                    FinalViewElementCountBeforeCleanup = finalViewElementCountBeforeCleanup,
                    FinalViewElementCountAfterCleanup = finalViewElementCountAfterCleanup,
                    Message = message,
                    AttemptDebug = attemptDebug,
                    DuplicatedViewDebug = duplicatedViewDebug,
                };
            }
        }

        private List<object> ListLegendSeedCandidates(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(v => v.ViewType == ViewType.Legend && !v.IsTemplate)
                .OrderBy(v => v.Name, StringComparer.OrdinalIgnoreCase)
                .Select(v =>
                {
                    List<ElementId> componentIds = CollectLegendComponentIds(doc, v);
                    return (object)new
                    {
                        ViewId = v.Id.GetIdValue(),
                        ViewName = v.Name,
                        LegendComponentCount = componentIds.Count,
                        IsUsableSeed = componentIds.Count > 0,
                    };
                })
                .ToList();
        }

        private List<DoorWindowLegendTypeInfo> CollectUsedDoorWindowTypes(Document doc, string targetType)
        {
            BuiltInCategory category = targetType == "door" ? BuiltInCategory.OST_Doors : BuiltInCategory.OST_Windows;
            HashSet<ElementId> typeIds = new HashSet<ElementId>(new ElementIdValueComparer());

            IList<Element> instances = new FilteredElementCollector(doc)
                .OfCategory(category)
                .WhereElementIsNotElementType()
                .WhereElementIsViewIndependent()
                .ToElements();

            foreach (Element instance in instances)
            {
                ElementId typeId = instance.GetTypeId();
                if (typeId != null && typeId != ElementId.InvalidElementId)
                    typeIds.Add(typeId);
            }

            List<DoorWindowLegendTypeInfo> results = new List<DoorWindowLegendTypeInfo>();
            foreach (ElementId typeId in typeIds)
            {
                FamilySymbol symbol = doc.GetElement(typeId) as FamilySymbol;
                if (symbol == null)
                    continue;

                string typeMarkRaw = GetTypeMark(symbol);
                results.Add(new DoorWindowLegendTypeInfo
                {
                    TypeId = typeId,
                    TypeMarkRaw = typeMarkRaw,
                    TypeMarkDisplay = string.IsNullOrWhiteSpace(typeMarkRaw) ? "(未填)" : typeMarkRaw.Trim(),
                    TypeName = symbol.Name ?? string.Empty,
                });
            }

            return results;
        }

        private List<DoorWindowLegendTypeInfo> SortDoorWindowTypesByTypeMark(List<DoorWindowLegendTypeInfo> usedTypes)
        {
            return usedTypes
                .OrderBy(t => string.IsNullOrWhiteSpace(t.TypeMarkRaw) ? 1 : 0)
                .ThenBy(t => NormalizeTypeMarkForSort(t.TypeMarkRaw), new NaturalStringComparer())
                .ThenBy(t => t.TypeName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private View GetLegendViewById(Document doc, IdType viewId)
        {
            View view = doc.GetElement(new ElementId(viewId)) as View;
            if (view == null || view.ViewType != ViewType.Legend || view.IsTemplate)
                return null;

            return view;
        }

        private View DuplicateLegendView(Document doc, View sourceLegend, string defaultName)
        {
            if (sourceLegend == null)
                throw new Exception(LegendSeedViewNotFoundError);

            ElementId duplicatedId = sourceLegend.Duplicate(ViewDuplicateOption.WithDetailing);
            View duplicated = doc.GetElement(duplicatedId) as View;
            if (duplicated == null)
                throw new Exception("legend_duplicate_failed");

            duplicated.Name = BuildUniqueLegendName(doc, defaultName);
            return duplicated;
        }

        private string BuildUniqueLegendName(Document doc, string baseName)
        {
            HashSet<string> existingNames = new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Select(v => v.Name)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (!existingNames.Contains(baseName))
                return baseName;

            return $"{baseName}_{DateTime.Now:yyyyMMdd_HHmmss}";
        }

        private void ClearLegendViewContents(Document doc, View legendView)
        {
            ICollection<ElementId> toDelete = new FilteredElementCollector(doc, legendView.Id)
                .WhereElementIsNotElementType()
                .ToElementIds();

            if (toDelete.Count > 0)
                doc.Delete(toDelete);
        }

        private void ClearLegendViewContentsExcept(Document doc, View legendView, IEnumerable<ElementId> keepElementIds)
        {
            HashSet<IdType> keepIds = new HashSet<IdType>(
                (keepElementIds ?? Enumerable.Empty<ElementId>())
                    .Where(IsValidElementId)
                    .Select(id => id.GetIdValue()));

            ICollection<ElementId> toDelete = new FilteredElementCollector(doc, legendView.Id)
                .WhereElementIsNotElementType()
                .ToElementIds()
                .Where(IsValidElementId)
                .Where(id => !keepIds.Contains(id.GetIdValue()))
                .ToList();

            if (toDelete.Count > 0)
                doc.Delete(toDelete);
        }

        private List<ElementId> CollectViewElementIds(Document doc, View view)
        {
            if (doc == null || view == null)
                return new List<ElementId>();

            try
            {
                return new FilteredElementCollector(doc, view.Id)
                    .WhereElementIsNotElementType()
                    .ToElementIds()
                    .Where(IsValidElementId)
                    .Distinct(new ElementIdValueComparer())
                    .ToList();
            }
            catch
            {
                return new List<ElementId>();
            }
        }

        private DoorWindowLegendCleanupResult DeleteSeedOriginalIntersection(
            Document doc,
            View legendView,
            List<ElementId> seedOriginalElementIds)
        {
            DoorWindowLegendCleanupResult result = new DoorWindowLegendCleanupResult
            {
                DeletedCount = 0,
                Reason = "completed",
                SeedOriginalElementCount = seedOriginalElementIds?.Count ?? 0,
                FinalViewElementCountBeforeCleanup = 0,
                FinalViewElementCountAfterCleanup = 0,
            };

            if (doc == null || legendView == null)
            {
                result.Reason = "invalid_document_or_view";
                return result;
            }

            List<ElementId> finalViewElementIds = CollectViewElementIds(doc, legendView);
            result.FinalViewElementCountBeforeCleanup = finalViewElementIds.Count;

            HashSet<IdType> seedOriginalIdValues = new HashSet<IdType>(
                (seedOriginalElementIds ?? new List<ElementId>())
                    .Where(IsValidElementId)
                    .Select(id => id.GetIdValue()));

            HashSet<IdType> protectedIdValues = new HashSet<IdType>(
                finalViewElementIds
                    .Where(IsValidElementId)
                    .Select(id => id.GetIdValue())
                    .Where(id => !seedOriginalIdValues.Contains(id)));
            result.ProtectedElementCount = protectedIdValues.Count;

            List<ElementId> candidateOriginalIds = (seedOriginalElementIds ?? new List<ElementId>())
                .Where(IsValidElementId)
                .Where(id => doc.GetElement(id) != null)
                .Distinct(new ElementIdValueComparer())
                .ToList();

            if (candidateOriginalIds.Count == 0)
            {
                result.Reason = "nothing_to_delete";
                result.FinalViewElementCountAfterCleanup = result.FinalViewElementCountBeforeCleanup;
                return result;
            }

            foreach (ElementId originalId in candidateOriginalIds)
            {
                SubTransaction cleanupTransaction = new SubTransaction(doc);
                try
                {
                    cleanupTransaction.Start();
                    ICollection<ElementId> deletedIds = doc.Delete(originalId);
                    List<IdType> deletedIdValues = deletedIds
                        .Where(IsValidElementId)
                        .Select(id => id.GetIdValue())
                        .ToList();
                    List<IdType> wouldDeleteProtectedIds = deletedIdValues
                        .Where(id => protectedIdValues.Contains(id))
                        .ToList();

                    if (wouldDeleteProtectedIds.Count > 0)
                    {
                        cleanupTransaction.RollBack();
                        result.SkippedCount++;
                        result.SkippedOriginalIds.Add(new
                        {
                            OriginalElementId = originalId.GetIdValue(),
                            WouldDeleteProtectedIds = wouldDeleteProtectedIds,
                            Reason = "delete_would_remove_generated_elements",
                        });
                        Logger.Info($"door-window-legend-tools cleanup skipped original element. originalElementId={originalId.GetIdValue()}, wouldDeleteProtectedIds={string.Join(",", wouldDeleteProtectedIds)}");
                        continue;
                    }

                    cleanupTransaction.Commit();
                    result.DeletedCount++;
                    result.DeletedElementIds.AddRange(deletedIdValues);
                }
                catch (Exception ex)
                {
                    try
                    {
                        if (cleanupTransaction.HasStarted())
                            cleanupTransaction.RollBack();
                    }
                    catch
                    {
                        // Ignore rollback errors in per-element cleanup.
                    }

                    result.SkippedCount++;
                    result.SkippedOriginalIds.Add(new
                    {
                        OriginalElementId = originalId.GetIdValue(),
                        WouldDeleteProtectedIds = new List<IdType>(),
                        Reason = ex.Message,
                    });
                    Logger.Error($"door-window-legend-tools cleanup skipped original element due to exception. originalElementId={originalId.GetIdValue()}", ex);
                }
            }

            doc.Regenerate();
            result.FinalViewElementCountAfterCleanup = CollectViewElementIds(doc, legendView).Count;
            result.Reason = result.SkippedCount > 0 ? "completed_with_skips" : "completed";
            return result;
        }

        private List<ElementId> CollectLegendComponentIds(Document doc, View legendView)
        {
            if (doc == null || legendView == null)
                return new List<ElementId>();

            try
            {
                List<ElementId> categoryMatches = new FilteredElementCollector(doc, legendView.Id)
                    .OfCategory(BuiltInCategory.OST_LegendComponents)
                    .WhereElementIsNotElementType()
                    .ToElementIds()
                    .Where(IsValidElementId)
                    .ToList();

                if (categoryMatches.Count > 0)
                    return categoryMatches;
            }
            catch
            {
                // Ignore and fall back to parameter-based detection.
            }

            try
            {
                return new FilteredElementCollector(doc, legendView.Id)
                    .WhereElementIsNotElementType()
                    .ToElementIds()
                    .Where(IsValidElementId)
                    .Where(id => IsLegendComponentElement(doc.GetElement(id)))
                    .ToList();
            }
            catch
            {
                return new List<ElementId>();
            }
        }

        private bool IsLegendComponentElement(Element element)
        {
            if (element == null)
                return false;

            try
            {
                Parameter componentParam = element.get_Parameter(BuiltInParameter.LEGEND_COMPONENT);
                return componentParam != null && componentParam.StorageType == StorageType.ElementId;
            }
            catch
            {
                return false;
            }
        }

        private object BuildLegendViewDebug(Document doc, View legendView)
        {
            if (doc == null || legendView == null)
                return null;

            object viewId = SafeGetElementIdValue(legendView);
            string viewName = SafeGetViewName(legendView);

            try
            {
                List<ElementId> elementIds = new FilteredElementCollector(doc, legendView.Id)
                    .WhereElementIsNotElementType()
                    .ToElementIds()
                    .Where(IsValidElementId)
                    .ToList();

                List<object> elements = elementIds
                    .Select(id => BuildElementDebugInfo(doc, id))
                    .Where(info => info != null)
                    .ToList();

                return new
                {
                    ViewId = viewId,
                    ViewName = viewName,
                    TotalElementCount = elementIds.Count,
                    LegendComponentCount = CollectLegendComponentIds(doc, legendView).Count,
                    Elements = elements,
                };
            }
            catch (Exception ex)
            {
                return new
                {
                    ViewId = viewId,
                    ViewName = viewName,
                    DebugError = ex.Message,
                    Elements = new object[0],
                };
            }
        }

        private object BuildElementDebugInfo(Document doc, ElementId elementId)
        {
            if (!IsValidElementId(elementId))
                return null;

            Element element = null;
            try
            {
                element = doc.GetElement(elementId);
                if (element == null)
                {
                    return new
                    {
                        ElementId = SafeGetElementIdValue(elementId),
                        Exists = false,
                    };
                }

                Parameter componentParam = element.get_Parameter(BuiltInParameter.LEGEND_COMPONENT);
                ElementId legendTypeId = componentParam?.AsElementId();

                return new
                {
                    ElementId = SafeGetElementIdValue(elementId),
                    Exists = true,
                    Category = element.Category?.Name ?? string.Empty,
                    ClassName = element.GetType().Name,
                    Name = element.Name ?? string.Empty,
                    HasLegendComponentParameter = componentParam != null,
                    LegendComponentTypeId = legendTypeId != null && legendTypeId != ElementId.InvalidElementId
                        ? (object)legendTypeId.GetIdValue()
                        : null,
                };
            }
            catch (Exception ex)
            {
                return new
                {
                    ElementId = SafeGetElementIdValue(elementId),
                    DebugError = ex.Message,
                };
            }
        }

        private object SafeGetElementIdValue(ElementId elementId)
        {
            try
            {
                return elementId != null && elementId != ElementId.InvalidElementId
                    ? (object)elementId.GetIdValue()
                    : null;
            }
            catch
            {
                return null;
            }
        }

        private bool IsValidElementId(ElementId elementId)
        {
            return elementId != null && elementId != ElementId.InvalidElementId;
        }

        private void SafeDeleteElement(Document doc, ElementId elementId)
        {
            if (!IsValidElementId(elementId))
                return;

            try
            {
                if (doc.GetElement(elementId) != null)
                    doc.Delete(elementId);
            }
            catch
            {
                // Cleanup best-effort only. The original failure reason is more important.
            }
        }

        private object SafeGetElementIdValue(Element element)
        {
            try
            {
                return element != null ? SafeGetElementIdValue(element.Id) : null;
            }
            catch
            {
                return null;
            }
        }

        private string SafeGetViewName(View view)
        {
            try
            {
                return view?.Name ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private int PlaceLegendItemsFromOriginalSeedSource(
            Document doc,
            View legendView,
            ElementId sourceSeedComponentId,
            string layoutDirection,
            int maxPerLine,
            List<DoorWindowLegendTypeInfo> usedTypes,
            List<DoorWindowLegendFailedType> failedTypes,
            List<object> attemptDebug,
            out List<ElementId> keepElementIds)
        {
            keepElementIds = new List<ElementId>();

            Element sourceComponent = doc.GetElement(sourceSeedComponentId);
            if (sourceComponent == null)
                throw new InvalidOperationException("source Legend Component 不存在，無法建立門窗圖例。");

            BoundingBoxXYZ seedBounds = sourceComponent.get_BoundingBox(legendView);
            XYZ seedOrigin = GetSeedAnchor(seedBounds);
            ElementId textTypeId = doc.GetDefaultElementTypeId(ElementTypeGroup.TextNoteType);

            int placedCount = 0;

            for (int index = 0; index < usedTypes.Count; index++)
            {
                DoorWindowLegendTypeInfo type = usedTypes[index];
                XYZ targetOrigin = GetTargetAnchor(seedOrigin, layoutDirection, maxPerLine, index);
                XYZ translation = targetOrigin - seedOrigin;
                ElementId copiedComponentId = ElementId.InvalidElementId;

                try
                {
                    Logger.Info($"door-window-legend-tools type attempt start. view={SafeGetViewName(legendView)}({SafeGetElementIdValue(legendView)}), sourceSeedComponentId={sourceSeedComponentId.GetIdValue()}, targetTypeId={type.TypeId.GetIdValue()}, typeMark={type.TypeMarkDisplay}, typeName={type.TypeName}");

                    copiedComponentId = CopyLegendComponentInView(doc, legendView, sourceSeedComponentId, translation);
                    if (copiedComponentId == ElementId.InvalidElementId)
                    {
                        string reason = "無法從原始 seed Legend Component 複製新元件。";
                        failedTypes.Add(new DoorWindowLegendFailedType
                        {
                            TypeId = type.TypeId,
                            TypeMarkDisplay = type.TypeMarkDisplay,
                            TypeName = type.TypeName,
                            Reason = reason,
                        });
                        attemptDebug.Add(new
                        {
                            Step = "copy",
                            Success = false,
                            TargetTypeId = type.TypeId.GetIdValue(),
                            TypeMarkDisplay = type.TypeMarkDisplay,
                            TypeName = type.TypeName,
                            SourceSeedComponentId = sourceSeedComponentId.GetIdValue(),
                            Message = reason,
                        });
                        Logger.Error($"door-window-legend-tools copy failed. targetTypeId={type.TypeId.GetIdValue()}, typeMark={type.TypeMarkDisplay}, typeName={type.TypeName}");
                        continue;
                    }

                    string swapReason;
                    ElementId appliedComponentId;
                    if (!TryApplyLegendComponentTypeInView(
                            doc,
                            legendView.Id,
                            copiedComponentId,
                            type.TypeId,
                            sourceSeedComponentId,
                            out appliedComponentId,
                            out swapReason))
                    {
                        doc.Delete(copiedComponentId);
                        failedTypes.Add(new DoorWindowLegendFailedType
                        {
                            TypeId = type.TypeId,
                            TypeMarkDisplay = type.TypeMarkDisplay,
                            TypeName = type.TypeName,
                            Reason = swapReason,
                        });
                        attemptDebug.Add(new
                        {
                            Step = "set_legend_component",
                            Success = false,
                            TargetTypeId = type.TypeId.GetIdValue(),
                            TypeMarkDisplay = type.TypeMarkDisplay,
                            TypeName = type.TypeName,
                            SourceSeedComponentId = sourceSeedComponentId.GetIdValue(),
                            CopiedComponentId = SafeGetElementIdValue(copiedComponentId),
                            Message = swapReason,
                            ViewDebugAfter = BuildLegendViewDebug(doc, legendView),
                        });
                        Logger.Error($"door-window-legend-tools set LEGEND_COMPONENT failed. copiedComponentId={SafeGetElementIdValue(copiedComponentId)}, targetTypeId={type.TypeId.GetIdValue()}, reason={swapReason}");
                        continue;
                    }

                    Element appliedComponent = doc.GetElement(appliedComponentId) ?? FindLegendComponentByTargetType(doc, legendView.Id, type.TypeId);
                    ElementId labelId = CreateLegendTextNote(doc, legendView, appliedComponent, textTypeId, type.TypeMarkDisplay);
                    keepElementIds.Add(appliedComponentId);
                    if (IsValidElementId(labelId))
                        keepElementIds.Add(labelId);

                    attemptDebug.Add(new
                    {
                        Step = "created",
                        Success = true,
                        TargetTypeId = type.TypeId.GetIdValue(),
                        TypeMarkDisplay = type.TypeMarkDisplay,
                        TypeName = type.TypeName,
                        SourceSeedComponentId = sourceSeedComponentId.GetIdValue(),
                        CopiedComponentId = SafeGetElementIdValue(copiedComponentId),
                        AppliedComponentId = SafeGetElementIdValue(appliedComponentId),
                        LabelId = SafeGetElementIdValue(labelId),
                    });
                    Logger.Info($"door-window-legend-tools type attempt success. appliedComponentId={SafeGetElementIdValue(appliedComponentId)}, targetTypeId={type.TypeId.GetIdValue()}, typeMark={type.TypeMarkDisplay}");
                    placedCount++;
                }
                catch (Exception ex)
                {
                    SafeDeleteElement(doc, copiedComponentId);
                    failedTypes.Add(new DoorWindowLegendFailedType
                    {
                        TypeId = type.TypeId,
                        TypeMarkDisplay = type.TypeMarkDisplay,
                        TypeName = type.TypeName,
                        Reason = ex.Message,
                    });
                    attemptDebug.Add(new
                    {
                        Step = "exception",
                        Success = false,
                        TargetTypeId = type.TypeId.GetIdValue(),
                        TypeMarkDisplay = type.TypeMarkDisplay,
                        TypeName = type.TypeName,
                        SourceSeedComponentId = sourceSeedComponentId.GetIdValue(),
                        CopiedComponentId = SafeGetElementIdValue(copiedComponentId),
                        Message = ex.Message,
                        ViewDebugAfter = BuildLegendViewDebug(doc, legendView),
                    });
                    Logger.Error($"door-window-legend-tools type attempt exception. copiedComponentId={SafeGetElementIdValue(copiedComponentId)}, targetTypeId={type.TypeId.GetIdValue()}, typeMark={type.TypeMarkDisplay}", ex);
                }
            }

            return placedCount;
        }

        private ElementId CopyLegendComponentInView(
            Document doc,
            View legendView,
            ElementId sourceComponentId,
            XYZ translation)
        {
            if (legendView == null || sourceComponentId == null || sourceComponentId == ElementId.InvalidElementId)
                return ElementId.InvalidElementId;

            HashSet<IdType> existingLegendComponentIds = CollectLegendComponentIds(doc, legendView)
                .Select(id => id.GetIdValue())
                .ToHashSet();

            ICollection<ElementId> copiedIds = ElementTransformUtils.CopyElement(doc, sourceComponentId, translation);

            doc.Regenerate();

            ElementId copiedLegendComponentId = CollectLegendComponentIds(doc, legendView)
                .FirstOrDefault(id => !existingLegendComponentIds.Contains(id.GetIdValue()));

            if (copiedLegendComponentId != null && copiedLegendComponentId != ElementId.InvalidElementId)
                return copiedLegendComponentId;

            foreach (ElementId copiedId in copiedIds)
            {
                if (copiedId == null || copiedId == ElementId.InvalidElementId)
                    continue;

                Element copiedElement = doc.GetElement(copiedId);
                if (IsLegendComponentElement(copiedElement))
                    return copiedId;
            }

            return ElementId.InvalidElementId;
        }

        private bool TryApplyLegendComponentTypeInView(
            Document doc,
            ElementId legendViewId,
            ElementId legendComponentId,
            ElementId targetTypeId,
            ElementId sourceSeedComponentId,
            out ElementId appliedComponentId,
            out string reason)
        {
            appliedComponentId = ElementId.InvalidElementId;
            reason = null;

            Element legendComponent = doc.GetElement(legendComponentId);
            Parameter componentParam = legendComponent?.get_Parameter(BuiltInParameter.LEGEND_COMPONENT);
            if (componentParam == null)
            {
                reason = "找不到 LEGEND_COMPONENT 參數。";
                return false;
            }

            if (componentParam.IsReadOnly)
            {
                reason = "LEGEND_COMPONENT 參數不可寫入。";
                return false;
            }

            string currentStep = "set_parameter";
            try
            {
                bool setOk = componentParam.Set(targetTypeId);
                if (!setOk)
                {
                    reason = "Revit 拒絕設定 LEGEND_COMPONENT。";
                    return false;
                }

                currentStep = "regenerate";
                doc.Regenerate();

                currentStep = "find_target_component";

                if (LegendComponentMatchesTargetType(doc.GetElement(legendComponentId), targetTypeId))
                {
                    appliedComponentId = legendComponentId;
                    return true;
                }

                ElementId matchedComponentId = FindLegendComponentIdByTargetType(
                    doc,
                    legendViewId,
                    targetTypeId,
                    new[] { sourceSeedComponentId });
                if (!IsValidElementId(matchedComponentId))
                {
                    reason = "find_target_component: 重新掃描 Legend 視圖後，找不到本次 copy 出來且已轉成目標 type 的 Legend Component。";
                    return false;
                }

                appliedComponentId = matchedComponentId;
                return true;
            }
            catch (Exception ex)
            {
                reason = $"{currentStep}: {ex.Message}";
                return false;
            }
        }

        private Element FindLegendComponentByTargetType(Document doc, ElementId legendViewId, ElementId targetTypeId)
        {
            ElementId id = FindLegendComponentIdByTargetType(doc, legendViewId, targetTypeId, Enumerable.Empty<ElementId>());
            return IsValidElementId(id) ? doc.GetElement(id) : null;
        }

        private ElementId FindLegendComponentIdByTargetType(
            Document doc,
            ElementId legendViewId,
            ElementId targetTypeId,
            IEnumerable<ElementId> excludedElementIds)
        {
            if (!IsValidElementId(legendViewId) || !IsValidElementId(targetTypeId))
                return ElementId.InvalidElementId;

            HashSet<IdType> excludedIds = new HashSet<IdType>(
                (excludedElementIds ?? Enumerable.Empty<ElementId>())
                    .Where(IsValidElementId)
                    .Select(id => id.GetIdValue()));

            try
            {
                ElementId categoryMatch = new FilteredElementCollector(doc, legendViewId)
                    .OfCategory(BuiltInCategory.OST_LegendComponents)
                    .WhereElementIsNotElementType()
                    .ToElementIds()
                    .Where(IsValidElementId)
                    .Where(id => !excludedIds.Contains(id.GetIdValue()))
                    .FirstOrDefault(id => LegendComponentMatchesTargetType(doc.GetElement(id), targetTypeId));

                if (IsValidElementId(categoryMatch))
                    return categoryMatch;
            }
            catch
            {
                // Fall back to parameter-based detection.
            }

            try
            {
                ElementId parameterMatch = new FilteredElementCollector(doc, legendViewId)
                    .WhereElementIsNotElementType()
                    .ToElementIds()
                    .Where(IsValidElementId)
                    .Where(id => !excludedIds.Contains(id.GetIdValue()))
                    .FirstOrDefault(id => LegendComponentMatchesTargetType(doc.GetElement(id), targetTypeId));

                return IsValidElementId(parameterMatch) ? parameterMatch : ElementId.InvalidElementId;
            }
            catch
            {
                return ElementId.InvalidElementId;
            }
        }

        private bool LegendComponentMatchesTargetType(Element element, ElementId targetTypeId)
        {
            if (element == null || !IsValidElementId(targetTypeId))
                return false;

            try
            {
                Parameter param = element.get_Parameter(BuiltInParameter.LEGEND_COMPONENT);
                ElementId value = param?.AsElementId();
                return value != null
                    && value != ElementId.InvalidElementId
                    && value.GetIdValue() == targetTypeId.GetIdValue();
            }
            catch
            {
                return false;
            }
        }

        private ElementId CreateLegendTextNote(Document doc, View legendView, Element legendComponent, ElementId textTypeId, string text)
        {
            if (legendComponent == null || string.IsNullOrWhiteSpace(text))
                return ElementId.InvalidElementId;

            BoundingBoxXYZ bounds = legendComponent.get_BoundingBox(legendView);
            if (bounds == null)
                return ElementId.InvalidElementId;

            double centerX = (bounds.Min.X + bounds.Max.X) / 2.0;
            double topY = bounds.Max.Y;
            XYZ textPoint = new XYZ(centerX, topY + (LabelOffsetCm * CmToFeet), 0);

            TextNoteOptions options = new TextNoteOptions(textTypeId)
            {
                HorizontalAlignment = HorizontalTextAlignment.Center,
            };

            TextNote note = TextNote.Create(doc, legendView.Id, textPoint, text, options);
            return note?.Id ?? ElementId.InvalidElementId;
        }

        private XYZ GetSeedAnchor(BoundingBoxXYZ bounds)
        {
            if (bounds == null)
                return XYZ.Zero;

            double centerY = (bounds.Min.Y + bounds.Max.Y) / 2.0;
            return new XYZ(bounds.Min.X, centerY, 0);
        }

        private XYZ GetTargetAnchor(XYZ seedOrigin, string layoutDirection, int maxPerLine, int index)
        {
            int primaryIndex = index % maxPerLine;
            int secondaryIndex = index / maxPerLine;

            double horizontalSpacing = HorizontalSpacingCm * CmToFeet;
            double verticalSpacing = VerticalSpacingCm * CmToFeet;

            if (layoutDirection == "horizontal")
            {
                return new XYZ(
                    seedOrigin.X + (primaryIndex * horizontalSpacing),
                    seedOrigin.Y - (secondaryIndex * verticalSpacing),
                    0);
            }

            return new XYZ(
                seedOrigin.X + (secondaryIndex * horizontalSpacing),
                seedOrigin.Y - (primaryIndex * verticalSpacing),
                0);
        }

        private string GetDoorWindowDisplayName(string targetType)
        {
            return targetType == "door" ? "門" : "窗";
        }

        private string GetDoorWindowDisplayNameFromType(Document doc, DoorWindowLegendTypeInfo type)
        {
            FamilySymbol symbol = doc.GetElement(type?.TypeId) as FamilySymbol;
            if (symbol?.Category == null)
                return "門窗";

            if (symbol.Category.Id.GetIdValue() == new ElementId(BuiltInCategory.OST_Doors).GetIdValue())
                return "門";

            if (symbol.Category.Id.GetIdValue() == new ElementId(BuiltInCategory.OST_Windows).GetIdValue())
                return "窗";

            return "門窗";
        }

        private string GetTypeMark(FamilySymbol symbol)
        {
            Parameter typeMark = symbol.get_Parameter(BuiltInParameter.ALL_MODEL_TYPE_MARK);
            return typeMark?.AsString() ?? string.Empty;
        }

        private string NormalizeTypeMarkForSort(string typeMark)
        {
            return string.IsNullOrWhiteSpace(typeMark) ? string.Empty : typeMark.Trim();
        }

        private class DoorWindowLegendTypeInfo
        {
            public ElementId TypeId { get; set; }
            public string TypeMarkRaw { get; set; }
            public string TypeMarkDisplay { get; set; }
            public string TypeName { get; set; }
        }

        private class DoorWindowLegendFailedType
        {
            public ElementId TypeId { get; set; }
            public string TypeMarkDisplay { get; set; }
            public string TypeName { get; set; }
            public string Reason { get; set; }
        }

        private class DoorWindowLegendCleanupResult
        {
            public int DeletedCount { get; set; }
            public int SkippedCount { get; set; }
            public string Reason { get; set; }
            public int SeedOriginalElementCount { get; set; }
            public int ProtectedElementCount { get; set; }
            public int FinalViewElementCountBeforeCleanup { get; set; }
            public int FinalViewElementCountAfterCleanup { get; set; }
            public List<object> SkippedOriginalIds { get; set; } = new List<object>();
            public List<IdType> DeletedElementIds { get; set; } = new List<IdType>();

            public static DoorWindowLegendCleanupResult Skipped()
            {
                return new DoorWindowLegendCleanupResult
                {
                    DeletedCount = 0,
                    SkippedCount = 0,
                    Reason = "not_started",
                    SeedOriginalElementCount = 0,
                    ProtectedElementCount = 0,
                    FinalViewElementCountBeforeCleanup = 0,
                    FinalViewElementCountAfterCleanup = 0,
                    SkippedOriginalIds = new List<object>(),
                    DeletedElementIds = new List<IdType>(),
                };
            }
        }

        private class ElementIdValueComparer : IEqualityComparer<ElementId>
        {
            public bool Equals(ElementId x, ElementId y)
            {
                if (ReferenceEquals(x, y))
                    return true;

                if (x == null || y == null)
                    return false;

                return x.GetIdValue() == y.GetIdValue();
            }

            public int GetHashCode(ElementId obj)
            {
                return obj == null ? 0 : obj.GetIdValue().GetHashCode();
            }
        }

        private class NaturalStringComparer : IComparer<string>
        {
            private static readonly Regex TokenRegex = new Regex(@"\d+|\D+", RegexOptions.Compiled);

            public int Compare(string x, string y)
            {
                x = x ?? string.Empty;
                y = y ?? string.Empty;

                MatchCollection xMatches = TokenRegex.Matches(x);
                MatchCollection yMatches = TokenRegex.Matches(y);
                int count = Math.Min(xMatches.Count, yMatches.Count);

                for (int i = 0; i < count; i++)
                {
                    string xToken = xMatches[i].Value;
                    string yToken = yMatches[i].Value;

                    bool xIsNumber = long.TryParse(xToken, NumberStyles.None, CultureInfo.InvariantCulture, out long xNumber);
                    bool yIsNumber = long.TryParse(yToken, NumberStyles.None, CultureInfo.InvariantCulture, out long yNumber);

                    int result = xIsNumber && yIsNumber
                        ? xNumber.CompareTo(yNumber)
                        : string.Compare(xToken, yToken, StringComparison.OrdinalIgnoreCase);

                    if (result != 0)
                        return result;
                }

                return xMatches.Count.CompareTo(yMatches.Count);
            }
        }
    }
}
