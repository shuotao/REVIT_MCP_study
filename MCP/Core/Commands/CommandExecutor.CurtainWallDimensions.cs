using System;
using System.Collections.Generic;
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
    public partial class CommandExecutor
    {
        private static IdType? LastCurtainElevationDimensionTypeId;

        private object DiagnoseCurtainWallElevationDimensions(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;
            UIDocument uidoc = _uiApp.ActiveUIDocument;
            string testMode = parameters["testMode"]?.Value<string>()?.Trim().ToLowerInvariant() ?? "both";
            if (testMode == "level_offset")
                return DiagnoseCurtainWallElevationLevelOffsetRuntime(parameters);
            bool verbose = parameters["verbose"]?.Value<bool?>() ?? false;
            bool rollback = parameters["rollback"]?.Value<bool>() ?? true;
            double fallbackInnerOffsetFt = (parameters["dimensionOffsetMm"]?.Value<double>() ?? 300.0) / 304.8;
            double fallbackStackOffsetFt = (parameters["dimensionStackOffsetMm"]?.Value<double>() ?? 250.0) / 304.8;
            var failures = new List<string>();
            var backgroundAttempts = new List<CurtainElevationDimensionAttempt>();
            var activeProfileAttempts = new List<CurtainElevationDimensionAttempt>();
            var createdDimensionIds = new List<ElementId>();
            var verifiedDimensionIds = new List<ElementId>();
            var referencePlaneIds = new List<ElementId>();
            var dimensionWarnings = new List<string>();
            object associationMoveTest = null;

            IdType? viewId = parameters["viewId"]?.Value<IdType>();
            ViewSection view = viewId.HasValue
                ? doc.GetElement(new ElementId(viewId.Value)) as ViewSection
                : uidoc.ActiveView as ViewSection;
            if (view == null || view.IsTemplate)
                throw new Exception("Provide a valid elevation ViewSection viewId or make one active.");

            IdType? wallId = parameters["wallId"]?.Value<IdType>();
            Wall wall = wallId.HasValue
                ? doc.GetElement(new ElementId(wallId.Value)) as Wall
                : new FilteredElementCollector(doc)
                    .OfClass(typeof(Wall))
                    .WhereElementIsNotElementType()
                    .Cast<Wall>()
                    .FirstOrDefault(candidate =>
                    {
                        try { return candidate.CurtainGrid != null; }
                        catch { return false; }
                    });
            if (wall == null || wall.CurtainGrid == null)
                throw new Exception("Provide a valid curtain wall wallId (Wall with CurtainGrid).");

            CurtainElevationDimensionTypeResolution dimensionTypeResolution =
                ResolveCurtainElevationDimensionType(doc, parameters, dimensionWarnings);
            DimensionType dimensionType = dimensionTypeResolution.DimensionType;
            if (dimensionType == null)
                failures.Add("No DimensionType could be resolved.");
            CurtainElevationDimensionStackOffsetResolution stackOffsetResolution =
                ResolveCurtainElevationDimensionStackOffset(dimensionType, view.Scale, fallbackInnerOffsetFt, fallbackStackOffsetFt);
            if (!string.IsNullOrWhiteSpace(stackOffsetResolution.Warning))
                dimensionWarnings.Add(stackOffsetResolution.Warning);

            View originalActiveView = uidoc.ActiveView;
            var originallyOpenViewIds = new HashSet<IdType>(uidoc.GetOpenUIViews().Select(uiView => uiView.ViewId.GetIdValue()));
            bool wasViewOpen = originallyOpenViewIds.Contains(view.Id.GetIdValue());
            bool wasViewActive = originalActiveView?.Id == view.Id;
            bool inactiveControlEstablished = !wasViewActive;
            string inactiveControlFailure = null;
            bool activationSucceeded = false;
            string activationFailure = null;
            int referencePlaneReferenceCount = 0;
            List<CurtainElevationGeometryReference> geometryReferences = new List<CurtainElevationGeometryReference>();
            List<CurtainElevationGeometryReference> horizontalBoundaryReferences = new List<CurtainElevationGeometryReference>();
            List<CurtainElevationGeometryReference> priorityZeroGridReferences = new List<CurtainElevationGeometryReference>();
            var curtainGridLineReferenceFailures = new List<string>();
            var curtainGridLineReferenceSamples = new List<object>();
            CurtainElevationCropResult cropResult = null;
            Transform frame = null;
            double minX = 0;
            double maxX = 0;
            double minY = 0;
            double maxY = 0;
            double topGridY = 0;
            double topTotalY = 0;
            double leftGridX = 0;
            double leftTotalX = 0;
            List<double> verticalGridXs = new List<double>();
            List<double> horizontalGridYs = new List<double>();

            using (TransactionGroup group = new TransactionGroup(doc, rollback
                ? "Diagnose curtain elevation ActiveView references (Rollback)"
                : "Diagnose curtain elevation ActiveView references"))
            {
                group.Start();
                try
                {
                    using (Transaction setup = TransactionHelper.Begin(doc, "準備帷幕牆尺寸 ActiveView 對照診斷"))
                    {
                        setup.Start();
                        LocationCurve loc = wall.Location as LocationCurve;
                        XYZ wallMid = loc?.Curve?.Evaluate(0.5, true);
                        cropResult = ConfigureCurtainElevationCrop(doc, view, wall, wallMid, view.Origin, 0, 0, 1200.0 / 304.8);
                        doc.Regenerate();
                        Transform sourceFrame = GetCurtainElevationView2DFrame(view, view.CropBox?.Transform);
                        frame = GetCurtainElevationDimensionFrame(view, sourceFrame);
                        if (frame == null || sourceFrame == null || cropResult.View2DMin == null || cropResult.View2DMax == null)
                            throw new Exception("Cannot resolve dimension frame or crop 2D bounds.");

                        XYZ sourceOriginDelta = sourceFrame.Origin - frame.Origin;
                        double xShift = sourceOriginDelta.DotProduct(frame.BasisX);
                        double yShift = sourceOriginDelta.DotProduct(frame.BasisY);
                        minX = (cropResult.WallBoundaryMinXFt ?? cropResult.View2DMin.X) + xShift;
                        maxX = (cropResult.WallBoundaryMaxXFt ?? cropResult.View2DMax.X) + xShift;
                        minY = (cropResult.CurtainGeometryMinYFt ?? cropResult.View2DMin.Y) + yShift;
                        maxY = (cropResult.CurtainGeometryMaxYFt ?? cropResult.View2DMax.Y) + yShift;
                        topGridY = maxY + stackOffsetResolution.InnerOffsetFt;
                        topTotalY = topGridY + stackOffsetResolution.ResolvedOffsetFt;
                        leftGridX = minX - stackOffsetResolution.InnerOffsetFt;
                        leftTotalX = leftGridX - stackOffsetResolution.ResolvedOffsetFt;
                        geometryReferences = CollectCurtainElevationGeometryReferences(doc, wall, view, frame, minX, maxX, minY, maxY);
                        horizontalBoundaryReferences = CollectCurtainElevationGeometryReferences(doc, wall, view, frame, minX, maxX, minY, maxY, true);
                        priorityZeroGridReferences = CollectCurtainElevationGridLineReferences(
                            doc, wall, view, frame, minX, maxX, minY, maxY,
                            curtainGridLineReferenceFailures, curtainGridLineReferenceSamples, 0);
                        verticalGridXs = GetCurtainElevationGridCoordinates(doc, wall, frame, "vertical", minX, maxX, minY, maxY);
                        horizontalGridYs = GetCurtainElevationGridCoordinates(doc, wall, frame, "horizontal", minX, maxX, minY, maxY);
                        setup.Commit();
                    }

                    if (wasViewActive)
                    {
                        View alternateView = uidoc.GetOpenUIViews()
                            .Where(uiView => uiView.ViewId != view.Id)
                            .Select(uiView => doc.GetElement(uiView.ViewId) as View)
                            .FirstOrDefault(candidate => candidate != null && !candidate.IsTemplate);
                        if (alternateView == null)
                        {
                            alternateView = new FilteredElementCollector(doc)
                                .OfClass(typeof(View))
                                .Cast<View>()
                                .FirstOrDefault(candidate => !candidate.IsTemplate && candidate.Id != view.Id && candidate.CanBePrinted);
                        }

                        if (alternateView != null)
                        {
                            try
                            {
                                uidoc.ActiveView = alternateView;
                                uidoc.RefreshActiveView();
                                inactiveControlEstablished = uidoc.ActiveView?.Id != view.Id;
                            }
                            catch (Exception ex)
                            {
                                inactiveControlFailure = ex.Message;
                            }
                        }
                        else
                        {
                            inactiveControlFailure = "No alternate graphical view was available to make the target elevation inactive.";
                        }
                    }

                    if (dimensionType != null)
                    {
                        using (Transaction backgroundTransaction = TransactionHelper.Begin(doc, "建立 inactive 帷幕牆尺寸對照"))
                        {
                            backgroundTransaction.Start();
                            if (testMode == "geometry_reference" || testMode == "both")
                            {
                                List<CurtainElevationGeometryReference> widthRefs = SelectCurtainElevationBoundaryReferences(horizontalBoundaryReferences, "horizontal", minX, maxX, minY, maxY, 1.0 / 304.8);
                                List<CurtainElevationGeometryReference> heightRefs = SelectCurtainElevationBoundaryReferences(geometryReferences, "vertical", minX, maxX, minY, maxY);
                                List<CurtainElevationGeometryReference> horizontalGridRefs = SelectCurtainElevationGridDimensionReferences(horizontalBoundaryReferences, priorityZeroGridReferences, "horizontal", verticalGridXs);
                                List<CurtainElevationGeometryReference> verticalGridRefs = SelectCurtainElevationGridDimensionReferences(geometryReferences, priorityZeroGridReferences, "vertical", horizontalGridYs);
                                backgroundAttempts.Add(TryDiagnoseCurtainGeometryDimension(doc, view, frame, dimensionType, "total_width", "horizontal", new List<double> { minX, maxX }, widthRefs, topTotalY));
                                backgroundAttempts.Add(TryDiagnoseCurtainGeometryDimension(doc, view, frame, dimensionType, "total_height", "vertical", new List<double> { minY, maxY }, heightRefs, leftTotalX));
                                backgroundAttempts.Add(TryDiagnoseCurtainGeometryDimension(doc, view, frame, dimensionType, "horizontal_grid", "horizontal", verticalGridXs, horizontalGridRefs, topGridY));
                                backgroundAttempts.Add(TryDiagnoseCurtainGeometryDimension(doc, view, frame, dimensionType, "vertical_grid", "vertical", horizontalGridYs, verticalGridRefs, leftGridX));
                            }

                            if (testMode == "reference_plane_fallback" || testMode == "both")
                            {
                                backgroundAttempts.Add(TryDiagnoseCurtainReferencePlaneDimension(doc, view, frame, dimensionType, "total_width", "horizontal", new List<double> { minX, maxX }, minY, maxY, topTotalY, referencePlaneIds, out int widthRefs));
                                referencePlaneReferenceCount += widthRefs;
                                backgroundAttempts.Add(TryDiagnoseCurtainReferencePlaneDimension(doc, view, frame, dimensionType, "total_height", "vertical", new List<double> { minY, maxY }, minX, maxX, leftTotalX, referencePlaneIds, out int heightRefs));
                                referencePlaneReferenceCount += heightRefs;
                            }
                            backgroundTransaction.Commit();
                        }
                    }

                    bool backgroundViewOpen = uidoc.GetOpenUIViews().Any(uiView => uiView.ViewId == view.Id);
                    bool backgroundViewActive = uidoc.ActiveView?.Id == view.Id;
                    foreach (CurtainElevationDimensionAttempt attempt in backgroundAttempts)
                    {
                        if (attempt.DimensionId != null && attempt.DimensionId != ElementId.InvalidElementId)
                            createdDimensionIds.Add(attempt.DimensionId);
                        attempt.InactivePostCommitState = CaptureCurtainElevationDimensionReferenceState(
                            doc, view, attempt, "inactive_post_commit", backgroundViewOpen, backgroundViewActive, true);
                    }

                    try
                    {
                        uidoc.ActiveView = view;
                        uidoc.RefreshActiveView();
                        activationSucceeded = uidoc.ActiveView?.Id == view.Id;
                        if (!activationSucceeded)
                            activationFailure = "UIDocument.ActiveView did not change to the diagnostic elevation.";
                    }
                    catch (Exception ex)
                    {
                        activationFailure = ex.Message;
                    }

                    bool activeViewOpen = uidoc.GetOpenUIViews().Any(uiView => uiView.ViewId == view.Id);
                    bool activeViewActive = uidoc.ActiveView?.Id == view.Id;
                    foreach (CurtainElevationDimensionAttempt attempt in backgroundAttempts)
                    {
                        attempt.AfterViewActivationState = CaptureCurtainElevationDimensionReferenceState(
                            doc, view, attempt, "same_dimension_after_view_activation", activeViewOpen, activeViewActive, false);
                    }

                    if (dimensionType != null && (testMode == "geometry_reference" || testMode == "both"))
                    {
                        for (int referencePriority = 0; referencePriority <= 3; referencePriority++)
                        {
                            var profileFailures = new List<string>();
                            var profileSamples = new List<object>();
                            List<CurtainElevationGeometryReference> profileGridReferences = CollectCurtainElevationGridLineReferences(
                                doc, wall, view, frame, minX, maxX, minY, maxY,
                                profileFailures, profileSamples, referencePriority);
                            curtainGridLineReferenceFailures.AddRange(profileFailures);
                            curtainGridLineReferenceSamples.AddRange(profileSamples);
                            List<CurtainElevationGeometryReference> horizontalGridRefs = SelectCurtainElevationGridDimensionReferences(horizontalBoundaryReferences, profileGridReferences, "horizontal", verticalGridXs);
                            List<CurtainElevationGeometryReference> verticalGridRefs = SelectCurtainElevationGridDimensionReferences(geometryReferences, profileGridReferences, "vertical", horizontalGridYs);
                            var profileAttempts = new List<CurtainElevationDimensionAttempt>();

                            using (Transaction activeTransaction = TransactionHelper.Begin(doc, $"建立 active 帷幕牆尺寸 profile {referencePriority}"))
                            {
                                activeTransaction.Start();
                                if (referencePriority == 0)
                                {
                                    List<CurtainElevationGeometryReference> widthRefs = SelectCurtainElevationBoundaryReferences(horizontalBoundaryReferences, "horizontal", minX, maxX, minY, maxY, 1.0 / 304.8);
                                    List<CurtainElevationGeometryReference> heightRefs = SelectCurtainElevationBoundaryReferences(geometryReferences, "vertical", minX, maxX, minY, maxY);
                                    profileAttempts.Add(TryDiagnoseCurtainGeometryDimension(doc, view, frame, dimensionType, "total_width_active", "horizontal", new List<double> { minX, maxX }, widthRefs, topTotalY));
                                    profileAttempts.Add(TryDiagnoseCurtainGeometryDimension(doc, view, frame, dimensionType, "total_height_active", "vertical", new List<double> { minY, maxY }, heightRefs, leftTotalX));
                                }
                                profileAttempts.Add(TryDiagnoseCurtainGeometryDimension(doc, view, frame, dimensionType, $"horizontal_grid_active_profile_{referencePriority}", "horizontal", verticalGridXs, horizontalGridRefs, topGridY));
                                profileAttempts.Add(TryDiagnoseCurtainGeometryDimension(doc, view, frame, dimensionType, $"vertical_grid_active_profile_{referencePriority}", "vertical", horizontalGridYs, verticalGridRefs, leftGridX));
                                activeTransaction.Commit();
                            }

                            foreach (CurtainElevationDimensionAttempt attempt in profileAttempts)
                            {
                                if (attempt.DimensionId != null && attempt.DimensionId != ElementId.InvalidElementId)
                                    createdDimensionIds.Add(attempt.DimensionId);
                                attempt.ReferencePriorityProfile = referencePriority;
                                attempt.ActivePostCommitState = CaptureCurtainElevationDimensionReferenceState(
                                    doc, view, attempt, $"active_post_commit_profile_{referencePriority}", true, true, true);
                                if (attempt.ActivePostCommitState.ValidationPassed)
                                    verifiedDimensionIds.Add(attempt.DimensionId);
                            }
                            activeProfileAttempts.AddRange(profileAttempts);
                        }
                    }

                    if (parameters["verifyAssociationByMove"]?.Value<bool>() ?? true)
                    {
                        CurtainElevationDimensionAttempt moveAttempt = activeProfileAttempts.FirstOrDefault(attempt =>
                            attempt.ReferencePriorityProfile == 0 &&
                            attempt.ActivePostCommitState?.ValidationPassed == true &&
                            attempt.ExpectedCurtainGridLineIds.Count > 0 &&
                            (attempt.Name.StartsWith("horizontal_grid") || attempt.Name.StartsWith("vertical_grid")));
                        if (moveAttempt != null)
                        {
                            ElementId gridLineId = new ElementId(moveAttempt.ExpectedCurtainGridLineIds.First());
                            using (Transaction moveTransaction = TransactionHelper.Begin(doc, "驗證 CurtainGridLine 尺寸關聯 10mm"))
                            {
                                moveTransaction.Start();
                                bool wasPinned = false;
                                bool unpinnedForTest = false;
                                try
                                {
                                    // Grid lines placed by the curtain wall's layout rules are "Dependent"
                                    // association and Revit auto-pins them; ElementTransformUtils.MoveElement
                                    // throws on pinned elements, so temporarily unpin for the probe move and
                                    // explicitly restore afterward (do not rely on the transaction rollback below).
                                    Element gridLineElement = doc.GetElement(gridLineId);
                                    wasPinned = gridLineElement?.Pinned ?? false;
                                    if (wasPinned && gridLineElement != null)
                                    {
                                        gridLineElement.Pinned = false;
                                        unpinnedForTest = true;
                                    }

                                    Dimension dimension = doc.GetElement(moveAttempt.DimensionId) as Dimension;
                                    List<double> beforeValues = GetCurtainElevationDimensionValuesMm(dimension);
                                    XYZ moveVector = moveAttempt.Name.StartsWith("horizontal_grid")
                                        ? frame.BasisX.Multiply(10.0 / 304.8)
                                        : frame.BasisY.Multiply(10.0 / 304.8);
                                    ElementTransformUtils.MoveElement(doc, gridLineId, moveVector);
                                    doc.Regenerate();
                                    List<double> afterValues = GetCurtainElevationDimensionValuesMm(dimension);
                                    bool valuesChanged = beforeValues.Count == afterValues.Count &&
                                        beforeValues.Zip(afterValues, (before, after) => Math.Abs(before - after) > 0.01).Any(changed => changed);
                                    CurtainElevationDimensionReferenceState movedState = CaptureCurtainElevationDimensionReferenceState(
                                        doc, view, moveAttempt, "after_curtain_gridline_move_10mm", true, true, false);
                                    associationMoveTest = new
                                    {
                                        AttemptName = moveAttempt.Name,
                                        DimensionId = moveAttempt.DimensionId.GetIdValue(),
                                        CurtainGridLineId = gridLineId.GetIdValue(),
                                        MoveDistanceMm = 10.0,
                                        WasPinned = wasPinned,
                                        UnpinnedForTest = unpinnedForTest,
                                        BeforeSegmentValuesMm = beforeValues,
                                        AfterSegmentValuesMm = afterValues,
                                        SegmentValuesChanged = valuesChanged,
                                        ReferencesRemainAvailable = movedState.AreReferencesAvailable,
                                        StableRepresentationRoundTripPassed = movedState.StableRepresentationRoundTripPassed,
                                        Passed = valuesChanged && movedState.ValidationPassed
                                    };
                                }
                                catch (Exception ex)
                                {
                                    associationMoveTest = new
                                    {
                                        AttemptName = moveAttempt.Name,
                                        CurtainGridLineId = gridLineId.GetIdValue(),
                                        MoveDistanceMm = 10.0,
                                        WasPinned = wasPinned,
                                        UnpinnedForTest = unpinnedForTest,
                                        Passed = false,
                                        FailureReason = ex.Message
                                    };
                                }
                                finally
                                {
                                    if (unpinnedForTest)
                                    {
                                        try
                                        {
                                            Element gridLineElement = doc.GetElement(gridLineId);
                                            if (gridLineElement != null)
                                                gridLineElement.Pinned = true;
                                        }
                                        catch (Exception restoreEx)
                                        {
                                            failures.Add("Failed to restore Pinned=true on CurtainGridLine after association move test: " + restoreEx.Message);
                                        }
                                    }
                                    moveTransaction.RollBack();
                                }
                            }
                        }
                        else
                        {
                            associationMoveTest = new { Passed = false, FailureReason = "No valid priority-0 native grid dimension was available for the 10 mm association test." };
                        }
                    }

                    try
                    {
                        if (originalActiveView != null && doc.GetElement(originalActiveView.Id) != null)
                        {
                            uidoc.ActiveView = originalActiveView;
                            uidoc.RefreshActiveView();
                        }
                    }
                    catch (Exception ex)
                    {
                        failures.Add("Failed to restore original ActiveView before rollback: " + ex.Message);
                    }

                    if (rollback)
                        group.RollBack();
                    else
                        group.Assimilate();
                }
                catch (Exception ex)
                {
                    failures.Add(ex.Message);
                    try
                    {
                        if (originalActiveView != null && doc.GetElement(originalActiveView.Id) != null)
                            uidoc.ActiveView = originalActiveView;
                    }
                    catch { }
                    if (group.GetStatus() == TransactionStatus.Started)
                        group.RollBack();
                }
            }

            foreach (UIView uiView in uidoc.GetOpenUIViews().ToList())
            {
                if (originallyOpenViewIds.Contains(uiView.ViewId.GetIdValue()) || uiView.ViewId == uidoc.ActiveView?.Id)
                    continue;
                try { uiView.Close(); }
                catch (Exception ex) { failures.Add($"Failed to close diagnostic view tab {uiView.ViewId.GetIdValue()}: {ex.Message}"); }
            }

            return new
            {
                WallId = wall.Id.GetIdValue(),
                ViewId = view.Id.GetIdValue(),
                ViewName = view.Name,
                WasViewOpen = wasViewOpen,
                WasViewActive = wasViewActive,
                InactiveControlEstablished = inactiveControlEstablished,
                InactiveControlFailure = inactiveControlFailure,
                ActivationSucceeded = activationSucceeded,
                ActivationFailure = activationFailure,
                DimensionTypeId = dimensionType?.Id.GetIdValue(),
                DimensionTypeName = dimensionType?.Name,
                DimensionTypeSource = dimensionTypeResolution.Source,
                DimensionWarnings = dimensionWarnings,
                GeometryReferenceCount = geometryReferences.Count,
                CurtainGridLineCount = wall.CurtainGrid.GetUGridLineIds().Count + wall.CurtainGrid.GetVGridLineIds().Count,
                CurtainGridLineReferenceCount = priorityZeroGridReferences.Count,
                // verbose=false：巨量逐筆診斷陣列收斂為筆數，避免超過單次回傳上限；verbose=true 維持完整輸出。
                CurtainGridLineReferenceFailures = verbose ? (object)curtainGridLineReferenceFailures : curtainGridLineReferenceFailures.Count,
                CurtainGridLineReferenceSamples = verbose ? (object)curtainGridLineReferenceSamples : curtainGridLineReferenceSamples.Count,
                ReferencePlaneCreatedCount = referencePlaneIds.Count,
                ReferencePlaneReferenceCount = referencePlaneReferenceCount,
                ReferencePlaneIds = referencePlaneIds.Select(id => id.GetIdValue()).ToList(),
                BackgroundAttemptedDimensions = verbose
                    ? (object)backgroundAttempts.Select(ToCurtainElevationDimensionAttemptResult).ToList()
                    : backgroundAttempts.Count,
                ActiveViewProfileAttempts = verbose
                    ? (object)activeProfileAttempts.Select(ToCurtainElevationDimensionAttemptResult).ToList()
                    : activeProfileAttempts.Count,
                AttemptedDimensions = verbose
                    ? (object)backgroundAttempts.Concat(activeProfileAttempts).Select(ToCurtainElevationDimensionAttemptResult).ToList()
                    : backgroundAttempts.Count + activeProfileAttempts.Count,
                CreatedDimensionIds = createdDimensionIds.Select(id => id.GetIdValue()).ToList(),
                VerifiedDimensionIds = verifiedDimensionIds.Select(id => id.GetIdValue()).ToList(),
                AssociationMoveTest = associationMoveTest,
                Failures = failures,
                Rollback = rollback,
                Verbose = verbose,
                OmittedDetailNote = verbose
                    ? null
                    : "逐筆診斷已省略，需要完整輸出請帶 verbose=true（per-item diagnostics omitted; pass verbose=true for the full output）"
            };
        }

        private class CurtainElevationDimensionTypeResolution
        {
            public DimensionType DimensionType { get; set; }
            public string Source { get; set; } = "not_resolved";
        }

        private class CurtainElevationDimensionStackOffsetResolution
        {
            public double ResolvedOffsetFt { get; set; }
            public double InnerOffsetFt { get; set; }
            public double InnerOffsetExtraPaperFt { get; set; }
            public string InnerOffsetSource { get; set; } = "parameter_fallback";
            public string InnerOffsetFallbackReason { get; set; }
            public double? WitnessLineLengthPaperFt { get; set; }
            public int ViewScale { get; set; }
            public string Source { get; set; } = "parameter_fallback";
            public string FallbackReason { get; set; }
            public string Warning { get; set; }
        }

        private class CurtainElevationDimensionResult
        {
            public ElementId WallId { get; set; }
            public ElementId TotalWidthDimensionId { get; set; }
            public ElementId HorizontalGridDimensionId { get; set; }
            public ElementId TotalHeightDimensionId { get; set; }
            public ElementId LevelOffsetDimensionElementId { get; set; }
            public ElementId VerticalGridDimensionId { get; set; }
            public bool? TotalWidthDimensionAreReferencesAvailable { get; set; }
            public bool? HorizontalGridDimensionAreReferencesAvailable { get; set; }
            public bool? TotalHeightDimensionAreReferencesAvailable { get; set; }
            public bool? LevelOffsetDimensionAreReferencesAvailable { get; set; }
            public bool? VerticalGridDimensionAreReferencesAvailable { get; set; }
            public List<ElementId> ReferenceCurveIds { get; } = new List<ElementId>();
            public List<string> Warnings { get; } = new List<string>();
            public int GeometryReferenceCount { get; set; }
            public int CurtainGridLineCount { get; set; }
            public int CurtainGridLineReferenceCount { get; set; }
            public List<string> CurtainGridLineReferenceFailures { get; } = new List<string>();
            public List<object> CurtainGridLineReferenceSamples { get; } = new List<object>();
            public List<string> GeometryReferenceCategories { get; set; } = new List<string>();
            public string TotalWidthDimensionReferenceSource { get; set; }
            public string TotalHeightDimensionReferenceSource { get; set; }
            public string LevelOffsetDimensionReferenceSource { get; set; }
            public string HorizontalGridDimensionReferenceSource { get; set; }
            public string VerticalGridDimensionReferenceSource { get; set; }
            public string DimensionFallbackReason { get; set; }
            public double? CurtainBottomToLevelDistanceFt { get; set; }
            public string LevelOffsetDimensionMode { get; set; } = "not_available";
            public string LevelOffsetDimensionStatus { get; set; } = "not_available";
            public double? DimensionWitnessLineLengthPaperFt { get; set; }
            public int DimensionViewScale { get; set; }
            public double DimensionInnerOffsetExtraPaperFt { get; set; }
            public double DimensionInnerOffsetFt { get; set; }
            public string DimensionInnerOffsetSource { get; set; }
            public string DimensionInnerOffsetFallbackReason { get; set; }
            public double DimensionStackOffsetFt { get; set; }
            public string DimensionStackOffsetSource { get; set; }
            public string DimensionStackOffsetFallbackReason { get; set; }
            public int AttemptCount { get; set; }
            public int VerifiedCount { get; set; }
            public List<string> CreationErrors { get; } = new List<string>();
            public List<object> PostCommitDimensionValidation { get; } = new List<object>();
            public List<CurtainElevationPendingDimension> PendingNativeDimensions { get; } = new List<CurtainElevationPendingDimension>();
            public int CreatedCount { get; set; }
            public int FailedCount { get; set; }
            public string Status { get; set; } = "not_started";
            public bool WasViewOpenBeforeDimensioning { get; set; }
            public bool WasViewActiveBeforeDimensioning { get; set; }
            public bool ViewActivationSucceeded { get; set; }
            public string ViewActivationFailure { get; set; }
            public int? GridReferencePriorityProfile { get; set; }
            public string Warning => string.Join(" ", Warnings.Where(w => !string.IsNullOrWhiteSpace(w)));
        }

        private class CurtainElevationDimensionJob
        {
            public ViewSection View { get; set; }
            public Wall Wall { get; set; }
            public CurtainElevationCropResult CropResult { get; set; }
            public CurtainElevationDimensionResult Result { get; set; }
            public ElementId WallTagId { get; set; }
            public string WallTagStatus { get; set; } = "pending";
            public XYZ WallTagViewPosition { get; set; }
            public XYZ WallTagWorldPosition { get; set; }
        }
        private class CurtainElevationGeometryReference
        {
            public Reference Reference { get; set; }
            public ElementId ElementId { get; set; }
            public string CategoryName { get; set; }
            public XYZ Start { get; set; }
            public XYZ End { get; set; }
            public double MinX { get; set; }
            public double MaxX { get; set; }
            public double MinY { get; set; }
            public double MaxY { get; set; }
            public double CenterX => (MinX + MaxX) / 2.0;
            public double CenterY => (MinY + MaxY) / 2.0;
            public double Length { get; set; }
            public bool IsVertical { get; set; }
            public bool IsHorizontal { get; set; }
            public ElementId CurtainGridLineId { get; set; }
            public string StableRepresentation { get; set; }
            public string GeometryObjectType { get; set; }
            public string ReferenceSource { get; set; }
            public int ReferencePriority { get; set; } = int.MaxValue;
            public bool SelectedForDimension { get; set; }
            public string SelectionReason { get; set; }
        }

        private class CurtainGridLineReferenceDiagnostic
        {
            public long GridLineId { get; set; }
            public string ProjectedDirection { get; set; }
            public string ReferenceSource { get; set; }
            public string GeometryObjectType { get; set; }
            public bool ReferenceAvailable { get; set; }
            public string StableRepresentation { get; set; }
            public double ProjectedCoordinateMm { get; set; }
            public double LengthMm { get; set; }
            public int ReferencePriority { get; set; }
            public bool IsAligned { get; set; }
            public bool PositionMatches { get; set; }
            public bool CoversGridRange { get; set; }
            public bool SelectedForDimension { get; set; }
            public string SelectionReason { get; set; }
        }

        private class CurtainElevationDimensionAttempt
        {
            public string Name { get; set; }
            public string Method { get; set; }
            public int ReferenceCount { get; set; }
            public XYZ DimensionLineStart { get; set; }
            public XYZ DimensionLineEnd { get; set; }
            public bool Success { get; set; }
            public ElementId DimensionId { get; set; }
            public ElementId OwnerViewId { get; set; }
            public bool ExistsAfterCreate { get; set; }
            public bool? PreCommitAreReferencesAvailable { get; set; }
            public bool? PostCommitAreReferencesAvailable { get; set; }
            public int ExpectedReferenceCount { get; set; }
            public int? PostCommitReferenceCount { get; set; }
            public bool? PostCommitValidationPassed { get; set; }
            public string PostCommitFailureReason { get; set; }
            public bool RecoverEnhancedTotalHeightAsSeparateDimensions { get; set; }
            public List<double> RecoveryTotalHeightCoordinates { get; set; } = new List<double>();
            public List<CurtainElevationGeometryReference> RecoveryTotalHeightReferences { get; set; } = new List<CurtainElevationGeometryReference>();
            public List<double> RecoveryLevelOffsetCoordinates { get; set; } = new List<double>();
            public List<CurtainElevationGeometryReference> RecoveryLevelOffsetReferences { get; set; } = new List<CurtainElevationGeometryReference>();
            public double RecoveryLevelOffsetDimensionLineOffset { get; set; }
            public string FailureMessage { get; set; }
            public string ReferenceSource { get; set; }
            public int? ReferencePriorityProfile { get; set; }
            public List<IdType> InputReferenceElementIds { get; set; } = new List<IdType>();
            public List<string> InputStableRepresentations { get; set; } = new List<string>();
            public List<IdType> ExpectedCurtainGridLineIds { get; set; } = new List<IdType>();
            public CurtainElevationDimensionReferenceState InactivePostCommitState { get; set; }
            public CurtainElevationDimensionReferenceState AfterViewActivationState { get; set; }
            public CurtainElevationDimensionReferenceState ActivePostCommitState { get; set; }
        }

        private class CurtainElevationDimensionReferenceState
        {
            public string Phase { get; set; }
            public bool WasViewOpen { get; set; }
            public bool WasViewActive { get; set; }
            public bool DimensionExists { get; set; }
            public IdType? OwnerViewId { get; set; }
            public bool? AreReferencesAvailable { get; set; }
            public int? ReferenceCount { get; set; }
            public List<IdType> ReferenceElementIds { get; set; } = new List<IdType>();
            public List<string> StableRepresentations { get; set; } = new List<string>();
            public bool StableRepresentationRoundTripPassed { get; set; }
            public bool InputStableRepresentationRoundTripPassed { get; set; }
            public bool ReferencesMatchExpectedCurtainGridLines { get; set; }
            public bool ValidationPassed { get; set; }
            public string FailureReason { get; set; }
        }

        private class CurtainElevationPendingDimension
        {
            public string Kind { get; set; }
            public View View { get; set; }
            public Transform Frame { get; set; }
            public DimensionType DimensionType { get; set; }
            public string Axis { get; set; }
            public List<double> Coordinates { get; set; } = new List<double>();
            public double MinOther { get; set; }
            public double MaxOther { get; set; }
            public double DimensionLineOffset { get; set; }
            public bool AllowDetailCurveFallback { get; set; }
            public ElementId NativeDimensionId { get; set; }
            public int ExpectedReferenceCount { get; set; }
            public string NativeReferenceSource { get; set; }
            public bool? PreCommitAreReferencesAvailable { get; set; }
            public bool? PostCommitAreReferencesAvailable { get; set; }
            public int? PostCommitReferenceCount { get; set; }
            public bool PostCommitValidationPassed { get; set; }
            public string PostCommitFailureReason { get; set; }
            public string PostCommitValidationMode { get; set; }
            public List<double> ExpectedSegmentValuesMm { get; set; } = new List<double>();
            public List<double> ActualSegmentValuesMm { get; set; } = new List<double>();
            public bool? SegmentValuesPassed { get; set; }
            public bool RecoverEnhancedTotalHeightAsSeparateDimensions { get; set; }
            public List<double> RecoveryTotalHeightCoordinates { get; set; } = new List<double>();
            public List<CurtainElevationGeometryReference> RecoveryTotalHeightReferences { get; set; } = new List<CurtainElevationGeometryReference>();
            public List<double> RecoveryLevelOffsetCoordinates { get; set; } = new List<double>();
            public List<CurtainElevationGeometryReference> RecoveryLevelOffsetReferences { get; set; } = new List<CurtainElevationGeometryReference>();
            public double RecoveryLevelOffsetDimensionLineOffset { get; set; }
            public bool RecoverLevelOffsetWithInvisibleReference { get; set; }
            public double RecoveryLevelY { get; set; }
            public double RecoveryLevelReferenceMinX { get; set; }
            public double RecoveryLevelReferenceMaxX { get; set; }
        }


        private CurtainElevationDimensionTypeResolution ResolveCurtainElevationDimensionType(Document doc, JObject parameters, List<string> warnings)
        {
            var result = new CurtainElevationDimensionTypeResolution();
            if (doc == null)
                return result;

            IdType? explicitId = parameters?["dimensionTypeId"]?.Value<IdType?>();
            if (explicitId.HasValue && explicitId.Value != 0)
            {
                DimensionType explicitType = doc.GetElement(new ElementId(explicitId.Value)) as DimensionType;
                if (explicitType != null)
                {
                    result.DimensionType = explicitType;
                    result.Source = "explicit_id";
                    LastCurtainElevationDimensionTypeId = explicitType.Id.GetIdValue();
                    return result;
                }

                warnings?.Add($"dimensionTypeId={explicitId.Value} is not a valid DimensionType; falling back to name/last/default.");
            }

            string explicitName = parameters?["dimensionTypeName"]?.Value<string>();
            if (!string.IsNullOrWhiteSpace(explicitName))
            {
                DimensionType namedType = new FilteredElementCollector(doc)
                    .OfClass(typeof(DimensionType))
                    .Cast<DimensionType>()
                    .FirstOrDefault(t => string.Equals(t.Name, explicitName, StringComparison.OrdinalIgnoreCase));
                if (namedType != null)
                {
                    result.DimensionType = namedType;
                    result.Source = "explicit_name";
                    LastCurtainElevationDimensionTypeId = namedType.Id.GetIdValue();
                    return result;
                }

                warnings?.Add($"dimensionTypeName='{explicitName}' not found; falling back to last/default.");
            }

            if (LastCurtainElevationDimensionTypeId.HasValue)
            {
                DimensionType lastType = doc.GetElement(new ElementId(LastCurtainElevationDimensionTypeId.Value)) as DimensionType;
                if (lastType != null)
                {
                    result.DimensionType = lastType;
                    result.Source = "last_used";
                    return result;
                }
            }

            try
            {
                ElementId defaultTypeId = doc.GetDefaultElementTypeId((ElementTypeGroup)10);
                DimensionType defaultType = doc.GetElement(defaultTypeId) as DimensionType;
                if (defaultType != null)
                {
                    result.DimensionType = defaultType;
                    result.Source = "revit_default";
                    LastCurtainElevationDimensionTypeId = defaultType.Id.GetIdValue();
                    return result;
                }
            }
            catch (Exception ex)
            {
                warnings?.Add($"Revit default dimension type lookup skipped: {ex.Message}");
            }

            DimensionType firstType = new FilteredElementCollector(doc)
                .OfClass(typeof(DimensionType))
                .WhereElementIsElementType()
                .Cast<DimensionType>()
                .FirstOrDefault();
            if (firstType != null)
            {
                result.DimensionType = firstType;
                result.Source = "first_available";
                LastCurtainElevationDimensionTypeId = firstType.Id.GetIdValue();
                return result;
            }

            warnings?.Add("No DimensionType found. Elevations will be created without dimensions.");
            result.Source = "not_found";
            return result;
        }

        private CurtainElevationDimensionResult CreateCurtainElevationDimensions(
            Document doc,
            ViewSection view,
            Wall wall,
            CurtainElevationCropResult cropResult,
            DimensionType dimensionType,
            bool addDimensions,
            double fallbackInnerOffsetFt,
            double fallbackStackOffsetFt,
            CurtainElevationDimensionResult existingResult = null,
            int? preferredGridReferencePriority = null,
            bool allowGridDetailCurveFallback = true)
        {
            CurtainElevationDimensionResult result = existingResult ?? new CurtainElevationDimensionResult();
            result.GridReferencePriorityProfile = preferredGridReferencePriority;
            if (wall != null)
                result.WallId = wall.Id;
            if (!addDimensions)
            {
                result.Status = "disabled";
                return result;
            }

            if (doc == null || view == null || wall == null || cropResult == null)
            {
                result.Status = "failed";
                result.Warnings.Add("dimension skipped: missing document/view/wall/crop result.");
                result.FailedCount = 4;
                return result;
            }

            if (dimensionType == null)
            {
                result.Status = "skipped_no_dimension_type";
                result.Warnings.Add("dimension skipped: no DimensionType available.");
                result.FailedCount = 4;
                return result;
            }

            CurtainElevationDimensionStackOffsetResolution stackOffsetResolution =
                ResolveCurtainElevationDimensionStackOffset(dimensionType, view.Scale, fallbackInnerOffsetFt, fallbackStackOffsetFt);
            result.DimensionWitnessLineLengthPaperFt = stackOffsetResolution.WitnessLineLengthPaperFt;
            result.DimensionViewScale = stackOffsetResolution.ViewScale;
            result.DimensionInnerOffsetExtraPaperFt = stackOffsetResolution.InnerOffsetExtraPaperFt;
            result.DimensionInnerOffsetFt = stackOffsetResolution.InnerOffsetFt;
            result.DimensionInnerOffsetSource = stackOffsetResolution.InnerOffsetSource;
            result.DimensionInnerOffsetFallbackReason = stackOffsetResolution.InnerOffsetFallbackReason;
            result.DimensionStackOffsetFt = stackOffsetResolution.ResolvedOffsetFt;
            result.DimensionStackOffsetSource = stackOffsetResolution.Source;
            result.DimensionStackOffsetFallbackReason = stackOffsetResolution.FallbackReason;
            if (!string.IsNullOrWhiteSpace(stackOffsetResolution.Warning))
                result.Warnings.Add(stackOffsetResolution.Warning);

            Transform sourceFrame = GetCurtainElevationView2DFrame(view, view.CropBox?.Transform);
            Transform frame = GetCurtainElevationDimensionFrame(view, sourceFrame);
            if (frame == null || sourceFrame == null || cropResult.View2DMin == null || cropResult.View2DMax == null)
            {
                result.Status = "failed";
                result.Warnings.Add("dimension skipped: view 2D bounds unavailable.");
                result.FailedCount = 4;
                return result;
            }

            XYZ sourceOriginDelta = sourceFrame.Origin - frame.Origin;
            double xShift = sourceOriginDelta.DotProduct(frame.BasisX);
            double yShift = sourceOriginDelta.DotProduct(frame.BasisY);
            double minX = (cropResult.WallBoundaryMinXFt ?? cropResult.View2DMin.X) + xShift;
            double maxX = (cropResult.WallBoundaryMaxXFt ?? cropResult.View2DMax.X) + xShift;
            double minY = (cropResult.CurtainGeometryMinYFt ?? cropResult.View2DMin.Y) + yShift;
            double maxY = (cropResult.CurtainGeometryMaxYFt ?? cropResult.View2DMax.Y) + yShift;
            if (maxX - minX <= 1e-6 || maxY - minY <= 1e-6)
            {
                result.Status = "failed";
                result.Warnings.Add("dimension skipped: view 2D bounds are too small.");
                result.FailedCount = 4;
                return result;
            }

            double topGridY = maxY + stackOffsetResolution.InnerOffsetFt;
            double topTotalY = topGridY + stackOffsetResolution.ResolvedOffsetFt;
            double leftGridX = minX - stackOffsetResolution.InnerOffsetFt;
            double leftTotalX = leftGridX - stackOffsetResolution.ResolvedOffsetFt;
            List<CurtainElevationGeometryReference> geometryReferences = CollectCurtainElevationGeometryReferences(doc, wall, view, frame, minX, maxX, minY, maxY);
            List<CurtainElevationGeometryReference> horizontalBoundaryReferences = CollectCurtainElevationGeometryReferences(doc, wall, view, frame, minX, maxX, minY, maxY, true);
            List<CurtainElevationGeometryReference> gridLineReferences = CollectCurtainElevationGridLineReferences(
                doc,
                wall,
                view,
                frame,
                minX,
                maxX,
                minY,
                maxY,
                result.CurtainGridLineReferenceFailures,
                result.CurtainGridLineReferenceSamples,
                preferredGridReferencePriority);
            result.GeometryReferenceCount = horizontalBoundaryReferences.Count + gridLineReferences.Count;
            result.CurtainGridLineCount = wall.CurtainGrid.GetUGridLineIds().Count + wall.CurtainGrid.GetVGridLineIds().Count;
            result.CurtainGridLineReferenceCount = gridLineReferences.Count;
            if (result.CurtainGridLineReferenceCount < result.CurtainGridLineCount)
                result.CurtainGridLineReferenceFailures.Add($"Only {result.CurtainGridLineReferenceCount} of {result.CurtainGridLineCount} CurtainGridLine elements exposed a usable aligned geometry reference.");
            result.GeometryReferenceCategories = horizontalBoundaryReferences
                .Select(r => r.CategoryName)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct()
                .OrderBy(s => s)
                .ToList();

            List<CurtainElevationGeometryReference> totalWidthRefs = SelectCurtainElevationBoundaryReferences(horizontalBoundaryReferences, "horizontal", minX, maxX, minY, maxY, 1.0 / 304.8);
            if (TryCreateCurtainElevationDimensionChain(doc, view, frame, dimensionType, "total_width", "horizontal", new List<double> { minX, maxX }, totalWidthRefs, minY, maxY, topTotalY, result, true, out ElementId totalWidthId, out string totalWidthSource, out string totalWidthReason))
            {
                result.TotalWidthDimensionId = totalWidthId;
                result.TotalWidthDimensionAreReferencesAvailable = GetCurtainElevationDimensionReferencesAvailability(doc, totalWidthId);
                result.TotalWidthDimensionReferenceSource = totalWidthRefs.Any(reference => reference.ElementId == wall.Id)
                    ? "host_wall_geometry_reference"
                    : totalWidthSource;
                result.CreatedCount++;
            }
            else
            {
                result.FailedCount++;
                result.TotalWidthDimensionReferenceSource = "failed";
                result.Warnings.Add("total width dimension failed: " + totalWidthReason);
            }

            List<CurtainElevationGeometryReference> totalHeightRefs = SelectCurtainElevationBoundaryReferences(geometryReferences, "vertical", minX, maxX, minY, maxY);
            double? levelY = cropResult.CropBottomLevelViewYFt.HasValue
                ? cropResult.CropBottomLevelViewYFt.Value + yShift
                : (double?)null;
            CreateCurtainElevationTotalHeightAndLevelOffsetDimensions(
                doc,
                view,
                wall,
                frame,
                dimensionType,
                minX,
                maxX,
                minY,
                maxY,
                levelY,
                leftTotalX,
                stackOffsetResolution.ResolvedOffsetFt,
                totalHeightRefs,
                result);

            List<double> verticalGridXs = GetCurtainElevationGridCoordinates(doc, wall, frame, "vertical", minX, maxX, minY, maxY);
            if (verticalGridXs.Count >= 3)
            {
                List<CurtainElevationGeometryReference> verticalGridRefs = SelectCurtainElevationGridDimensionReferences(horizontalBoundaryReferences, gridLineReferences, "horizontal", verticalGridXs);
                if (TryCreateCurtainElevationDimensionChain(doc, view, frame, dimensionType, "horizontal_grid", "horizontal", verticalGridXs, verticalGridRefs, minY, maxY, topGridY, result, allowGridDetailCurveFallback, out ElementId horizontalGridId, out string horizontalGridSource, out string horizontalGridReason))
                {
                    result.HorizontalGridDimensionId = horizontalGridId;
                    result.HorizontalGridDimensionAreReferencesAvailable = GetCurtainElevationDimensionReferencesAvailability(doc, horizontalGridId);
                    result.HorizontalGridDimensionReferenceSource = horizontalGridSource;
                    result.CreatedCount++;
                }
                else
                {
                    result.FailedCount++;
                    result.HorizontalGridDimensionReferenceSource = "failed";
                    result.Warnings.Add("horizontal grid dimension failed: " + horizontalGridReason);
                }
            }
            else
            {
                result.HorizontalGridDimensionReferenceSource = "skipped";
                result.Warnings.Add("horizontal grid dimension skipped: fewer than 3 grid/boundary X coordinates.");
            }

            List<double> horizontalGridYs = GetCurtainElevationGridCoordinates(doc, wall, frame, "horizontal", minX, maxX, minY, maxY);
            if (horizontalGridYs.Count >= 3)
            {
                List<CurtainElevationGeometryReference> horizontalGridRefs = SelectCurtainElevationGridDimensionReferences(geometryReferences, gridLineReferences, "vertical", horizontalGridYs);
                if (TryCreateCurtainElevationDimensionChain(doc, view, frame, dimensionType, "vertical_grid", "vertical", horizontalGridYs, horizontalGridRefs, minX, maxX, leftGridX, result, allowGridDetailCurveFallback, out ElementId verticalGridId, out string verticalGridSource, out string verticalGridReason))
                {
                    result.VerticalGridDimensionId = verticalGridId;
                    result.VerticalGridDimensionAreReferencesAvailable = GetCurtainElevationDimensionReferencesAvailability(doc, verticalGridId);
                    result.VerticalGridDimensionReferenceSource = verticalGridSource;
                    result.CreatedCount++;
                }
                else
                {
                    result.FailedCount++;
                    result.VerticalGridDimensionReferenceSource = "failed";
                    result.Warnings.Add("vertical grid dimension failed: " + verticalGridReason);
                }
            }
            else
            {
                result.VerticalGridDimensionReferenceSource = "skipped";
                result.Warnings.Add("vertical grid dimension skipped: fewer than 3 grid/boundary Y coordinates.");
            }

            result.AttemptCount = result.CreatedCount + result.FailedCount;
            result.Status = result.CreatedCount > 0
                ? (result.FailedCount > 0 ? "partial" : "created")
                : "failed";
            return result;
        }

        private void CreateCurtainElevationTotalHeightAndLevelOffsetDimensions(
            Document doc,
            View view,
            Wall wall,
            Transform frame,
            DimensionType dimensionType,
            double minX,
            double maxX,
            double minY,
            double maxY,
            double? levelY,
            double leftTotalX,
            double stackOffsetFt,
            List<CurtainElevationGeometryReference> totalHeightReferences,
            CurtainElevationDimensionResult result)
        {
            const double zeroToleranceFt = 1.0 / 304.8;
            List<CurtainElevationGeometryReference> orderedHeightReferences = totalHeightReferences?
                .Where(reference => reference?.Reference != null)
                .OrderBy(reference => reference.CenterY)
                .ToList() ?? new List<CurtainElevationGeometryReference>();

            Level level = doc?.GetElement(wall?.LevelId) as Level;
            if (!levelY.HasValue || level == null)
            {
                result.LevelOffsetDimensionMode = "skipped_level_unavailable";
                result.LevelOffsetDimensionStatus = "skipped_level_unavailable";
                result.Warnings.Add("level offset dimension skipped: wall Level or projected Level Y was unavailable; retained the original total height dimension.");
                TryCreateCurtainElevationOriginalTotalHeightDimension(
                    doc, view, frame, dimensionType, minX, maxX, minY, maxY,
                    leftTotalX, orderedHeightReferences, result);
                return;
            }

            double signedOffsetFt = minY - levelY.Value;
            result.CurtainBottomToLevelDistanceFt = Math.Abs(signedOffsetFt);
            if (Math.Abs(signedOffsetFt) <= zeroToleranceFt)
            {
                result.LevelOffsetDimensionMode = "skipped_zero_distance";
                result.LevelOffsetDimensionStatus = "skipped_zero_distance";
                TryCreateCurtainElevationOriginalTotalHeightDimension(
                    doc, view, frame, dimensionType, minX, maxX, minY, maxY,
                    leftTotalX, orderedHeightReferences, result);
                return;
            }

            if (orderedHeightReferences.Count != 2)
            {
                result.LevelOffsetDimensionMode = "failed_missing_curtain_boundary_reference";
                result.LevelOffsetDimensionStatus = "failed";
                result.Warnings.Add("level offset dimension failed: curtain bottom/top geometry references were unavailable.");
                TryCreateCurtainElevationOriginalTotalHeightDimension(
                    doc, view, frame, dimensionType, minX, maxX, minY, maxY,
                    leftTotalX, orderedHeightReferences, result);
                return;
            }

            CurtainElevationGeometryReference levelReference;
            doc.Regenerate();
            if (!TryCreateCurtainElevationLevelPlaneReference(
                doc, level, minX, maxX, levelY.Value,
                out levelReference, out string levelPlaneReason))
            {
                if (!TryCreateCurtainElevationInvisibleLevelReference(
                    doc, view, frame, minX, maxX, levelY.Value, result,
                    out levelReference, out string fallbackReason))
                {
                    result.LevelOffsetDimensionMode = "failed_level_reference";
                    result.LevelOffsetDimensionStatus = "failed";
                    result.Warnings.Add($"level offset dimension could not resolve the Level plane Reference ({levelPlaneReason}) or invisible detail-curve fallback ({fallbackReason}); retained the original total height dimension.");
                    TryCreateCurtainElevationOriginalTotalHeightDimension(
                        doc, view, frame, dimensionType, minX, maxX, minY, maxY,
                        leftTotalX, orderedHeightReferences, result);
                    return;
                }

                result.Warnings.Add("Level plane Reference was unavailable; using an invisible detail-curve Level reference: " + levelPlaneReason);
            }

            List<double> levelOffsetCoordinates = new List<double> { minY, levelY.Value };
            List<CurtainElevationGeometryReference> levelOffsetReferences = signedOffsetFt > 0
                ? new List<CurtainElevationGeometryReference> { levelReference, orderedHeightReferences[0] }
                : new List<CurtainElevationGeometryReference> { orderedHeightReferences[0], levelReference };
            double separateDimensionX = leftTotalX - stackOffsetFt;

            if (signedOffsetFt > zeroToleranceFt)
            {
                var enhancedCoordinates = new List<double> { levelY.Value, minY, maxY };
                List<CurtainElevationGeometryReference> enhancedReferences = new List<CurtainElevationGeometryReference>
                    { levelReference, orderedHeightReferences[0], orderedHeightReferences[1] };
                bool enhancedCreated = TryCreateCurtainElevationDimensionChain(
                    doc, view, frame, dimensionType, "total_height", "vertical",
                    enhancedCoordinates, enhancedReferences, minX, maxX, leftTotalX,
                    result, false, out ElementId enhancedId, out string enhancedSource, out string enhancedReason);

                if (!enhancedCreated && levelReference.ReferenceSource == "wall_level_plane_reference")
                {
                    if (TryCreateCurtainElevationInvisibleLevelReference(
                        doc, view, frame, minX, maxX, levelY.Value, result,
                        out CurtainElevationGeometryReference invisibleLevelReference, out string invisibleReason))
                    {
                        levelReference = invisibleLevelReference;
                        enhancedReferences = new List<CurtainElevationGeometryReference>
                            { levelReference, orderedHeightReferences[0], orderedHeightReferences[1] };
                        enhancedCreated = TryCreateCurtainElevationDimensionChain(
                            doc, view, frame, dimensionType, "total_height", "vertical",
                            enhancedCoordinates, enhancedReferences, minX, maxX, leftTotalX,
                            result, false, out enhancedId, out enhancedSource, out string fallbackEnhancedReason);
                        if (!enhancedCreated)
                            enhancedReason = AppendCurtainElevationWarning(enhancedReason, "invisible Level reference: " + fallbackEnhancedReason);
                    }
                    else
                    {
                        enhancedReason = AppendCurtainElevationWarning(enhancedReason, "invisible Level reference creation failed: " + invisibleReason);
                    }
                }

                if (enhancedCreated)
                {
                    result.TotalHeightDimensionId = enhancedId;
                    result.TotalHeightDimensionAreReferencesAvailable = GetCurtainElevationDimensionReferencesAvailability(doc, enhancedId);
                    result.TotalHeightDimensionReferenceSource = enhancedSource;
                    result.LevelOffsetDimensionElementId = enhancedId;
                    result.LevelOffsetDimensionAreReferencesAvailable = result.TotalHeightDimensionAreReferencesAvailable;
                    result.LevelOffsetDimensionReferenceSource = enhancedSource;
                    result.LevelOffsetDimensionMode = "total_height_chain";
                    result.LevelOffsetDimensionStatus = "created";
                    result.CreatedCount++;

                    CurtainElevationPendingDimension pending = result.PendingNativeDimensions
                        .LastOrDefault(candidate => candidate.NativeDimensionId == enhancedId);
                    if (pending != null)
                    {
                        pending.RecoverEnhancedTotalHeightAsSeparateDimensions = true;
                        pending.RecoveryTotalHeightCoordinates = new List<double> { minY, maxY };
                        pending.RecoveryTotalHeightReferences = orderedHeightReferences.ToList();
                        levelOffsetReferences = signedOffsetFt > 0
                            ? new List<CurtainElevationGeometryReference> { levelReference, orderedHeightReferences[0] }
                            : new List<CurtainElevationGeometryReference> { orderedHeightReferences[0], levelReference };
                        pending.RecoveryLevelOffsetCoordinates = levelOffsetCoordinates;
                        pending.RecoveryLevelOffsetReferences = levelOffsetReferences;
                        pending.RecoveryLevelOffsetDimensionLineOffset = separateDimensionX;
                        ConfigureCurtainElevationLevelReferenceRecovery(pending, levelReference, levelY.Value, minX, maxX);
                    }
                    return;
                }

                result.Warnings.Add("enhanced total height dimension failed; falling back to original total height plus a separate level offset dimension: " + enhancedReason);
                result.LevelOffsetDimensionMode = "separate_outer_fallback";
            }
            else
            {
                result.LevelOffsetDimensionMode = "separate_outer_below_level";
            }

            levelOffsetReferences = signedOffsetFt > 0
                ? new List<CurtainElevationGeometryReference> { levelReference, orderedHeightReferences[0] }
                : new List<CurtainElevationGeometryReference> { orderedHeightReferences[0], levelReference };
            TryCreateCurtainElevationOriginalTotalHeightDimension(
                doc, view, frame, dimensionType, minX, maxX, minY, maxY,
                leftTotalX, orderedHeightReferences, result);
            TryCreateCurtainElevationSeparateLevelOffsetDimension(
                doc, view, frame, dimensionType, minX, maxX,
                levelOffsetCoordinates, levelOffsetReferences, separateDimensionX,
                levelY.Value, result);
        }
        private bool TryCreateCurtainElevationLevelPlaneReference(
            Document doc,
            Level level,
            double minX,
            double maxX,
            double levelY,
            out CurtainElevationGeometryReference levelReference,
            out string reason)
        {
            levelReference = null;
            reason = null;
            try
            {
                Reference reference = level?.GetPlaneReference();
                if (reference == null)
                {
                    reason = "Level.GetPlaneReference() returned null.";
                    return false;
                }

                if (!TryValidateCurtainElevationStableReference(
                    doc, reference, level.Id, out string stableRepresentation, out reason))
                {
                    return false;
                }

                levelReference = new CurtainElevationGeometryReference
                {
                    Reference = reference,
                    ElementId = level.Id,
                    CategoryName = level.Category?.Name,
                    MinX = minX,
                    MaxX = maxX,
                    MinY = levelY,
                    MaxY = levelY,
                    Length = Math.Max(maxX - minX, 0.0),
                    IsHorizontal = true,
                    StableRepresentation = stableRepresentation,
                    ReferenceSource = "wall_level_plane_reference"
                };
                return true;
            }
            catch (Exception ex)
            {
                reason = ex.Message;
                return false;
            }
        }

        private bool TryCreateCurtainElevationInvisibleLevelReference(
            Document doc,
            View view,
            Transform frame,
            double minX,
            double maxX,
            double levelY,
            CurtainElevationDimensionResult result,
            out CurtainElevationGeometryReference levelReference,
            out string reason)
        {
            levelReference = null;
            reason = null;
            DetailCurve detailCurve = null;
            try
            {
                GraphicsStyle invisibleLineStyle = TryFindCurtainElevationLevelInvisibleLineStyle(doc);
                if (invisibleLineStyle == null)
                {
                    reason = "BuiltInCategory.OST_InvisibleLines was unavailable.";
                    return false;
                }

                double lineMinX = minX;
                double lineMaxX = maxX;
                if (Math.Abs(lineMaxX - lineMinX) < 1e-6)
                    lineMaxX = lineMinX + (100.0 / 304.8);
                Line referenceLine = Line.CreateBound(
                    CurtainElevationPointAt2D(frame, lineMinX, levelY),
                    CurtainElevationPointAt2D(frame, lineMaxX, levelY));
                detailCurve = doc.Create.NewDetailCurve(view, referenceLine);
                if (detailCurve == null)
                {
                    reason = "Revit returned null DetailCurve for the Level fallback.";
                    return false;
                }

                detailCurve.LineStyle = invisibleLineStyle;
                if (detailCurve.LineStyle == null ||
                    detailCurve.LineStyle.Id.GetIdValue() != invisibleLineStyle.Id.GetIdValue())
                {
                    throw new InvalidOperationException("Invisible Level detail curve line style read-back failed.");
                }
                doc.Regenerate();
                Reference reference = detailCurve.GeometryCurve?.Reference;
                if (reference == null)
                    throw new InvalidOperationException("Invisible Level detail curve exposed no geometry Reference.");
                if (!TryValidateCurtainElevationStableReference(
                    doc, reference, detailCurve.Id, out string stableRepresentation, out reason))
                {
                    throw new InvalidOperationException(reason);
                }

                levelReference = new CurtainElevationGeometryReference
                {
                    Reference = reference,
                    ElementId = detailCurve.Id,
                    CategoryName = detailCurve.Category?.Name,
                    MinX = lineMinX,
                    MaxX = lineMaxX,
                    MinY = levelY,
                    MaxY = levelY,
                    Length = Math.Abs(lineMaxX - lineMinX),
                    IsHorizontal = true,
                    StableRepresentation = stableRepresentation,
                    ReferenceSource = "invisible_detail_curve_fallback"
                };
                if (!result.ReferenceCurveIds.Any(id => id == detailCurve.Id))
                    result.ReferenceCurveIds.Add(detailCurve.Id);
                return true;
            }
            catch (Exception ex)
            {
                reason = ex.Message;
                if (detailCurve != null)
                    DeleteCurtainElevationDetailCurves(doc, new[] { detailCurve });
                return false;
            }
        }

        private bool TryValidateCurtainElevationStableReference(
            Document doc,
            Reference reference,
            ElementId expectedElementId,
            out string stableRepresentation,
            out string reason)
        {
            stableRepresentation = null;
            reason = null;
            try
            {
                if (doc == null || reference == null || expectedElementId == null)
                {
                    reason = "Reference validation inputs were incomplete.";
                    return false;
                }

                stableRepresentation = reference.ConvertToStableRepresentation(doc);
                if (string.IsNullOrWhiteSpace(stableRepresentation))
                {
                    reason = "Reference stable representation was empty.";
                    return false;
                }

                Reference roundTripReference = Reference.ParseFromStableRepresentation(
                    doc, stableRepresentation);
                if (roundTripReference == null)
                {
                    reason = "Reference stable representation did not round-trip.";
                    return false;
                }
                if (roundTripReference.ElementId == null ||
                    roundTripReference.ElementId.GetIdValue() != expectedElementId.GetIdValue())
                {
                    reason = "Reference stable representation resolved to an unexpected element.";
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                reason = ex.Message;
                return false;
            }
        }

        private void ConfigureCurtainElevationLevelReferenceRecovery(
            CurtainElevationPendingDimension pending,
            CurtainElevationGeometryReference levelReference,
            double levelY,
            double minX,
            double maxX)
        {
            if (pending == null || levelReference?.ReferenceSource != "wall_level_plane_reference")
                return;

            pending.RecoverLevelOffsetWithInvisibleReference = true;
            pending.RecoveryLevelY = levelY;
            pending.RecoveryLevelReferenceMinX = minX;
            pending.RecoveryLevelReferenceMaxX = maxX;
        }

        private List<CurtainElevationGeometryReference> ReplaceCurtainElevationLevelReference(
            IEnumerable<CurtainElevationGeometryReference> references,
            CurtainElevationGeometryReference replacement)
        {
            return references?.Select(reference =>
                reference?.ReferenceSource == "wall_level_plane_reference"
                    ? replacement
                    : reference).ToList() ?? new List<CurtainElevationGeometryReference>();
        }

        private void DeleteCurtainElevationInvisibleLevelReference(
            Document doc,
            CurtainElevationDimensionResult result,
            CurtainElevationGeometryReference reference)
        {
            if (doc == null || reference?.ReferenceSource != "invisible_detail_curve_fallback" ||
                reference.ElementId == null || reference.ElementId == ElementId.InvalidElementId)
            {
                return;
            }

            ElementId referenceId = reference.ElementId;
            if (doc.GetElement(referenceId) != null)
                doc.Delete(referenceId);
            result?.ReferenceCurveIds.RemoveAll(id =>
                id != null && id.GetIdValue() == referenceId.GetIdValue());
        }


        private bool TryCreateCurtainElevationOriginalTotalHeightDimension(
            Document doc,
            View view,
            Transform frame,
            DimensionType dimensionType,
            double minX,
            double maxX,
            double minY,
            double maxY,
            double leftTotalX,
            List<CurtainElevationGeometryReference> totalHeightReferences,
            CurtainElevationDimensionResult result)
        {
            if (TryCreateCurtainElevationDimensionChain(
                doc, view, frame, dimensionType, "total_height", "vertical",
                new List<double> { minY, maxY }, totalHeightReferences,
                minX, maxX, leftTotalX, result, false,
                out ElementId totalHeightId, out string totalHeightSource, out string totalHeightReason))
            {
                result.TotalHeightDimensionId = totalHeightId;
                result.TotalHeightDimensionAreReferencesAvailable = GetCurtainElevationDimensionReferencesAvailability(doc, totalHeightId);
                result.TotalHeightDimensionReferenceSource = totalHeightSource;
                result.CreatedCount++;
                return true;
            }

            result.FailedCount++;
            result.TotalHeightDimensionReferenceSource = "failed";
            result.Warnings.Add("total height dimension failed: " + totalHeightReason);
            return false;
        }

        private bool TryCreateCurtainElevationSeparateLevelOffsetDimension(
            Document doc,
            View view,
            Transform frame,
            DimensionType dimensionType,
            double minX,
            double maxX,
            List<double> coordinates,
            List<CurtainElevationGeometryReference> references,
            double dimensionLineX,
            double levelY,
            CurtainElevationDimensionResult result)
        {
            List<CurtainElevationGeometryReference> workingReferences = references?.ToList();
            bool created = TryCreateCurtainElevationDimensionChain(
                doc, view, frame, dimensionType, "level_offset", "vertical",
                coordinates, workingReferences, minX, maxX, dimensionLineX,
                result, false, out ElementId levelOffsetId, out string levelOffsetSource, out string levelOffsetReason);

            if (!created && workingReferences?.Any(reference =>
                reference?.ReferenceSource == "wall_level_plane_reference") == true)
            {
                if (TryCreateCurtainElevationInvisibleLevelReference(
                    doc, view, frame, minX, maxX, levelY, result,
                    out CurtainElevationGeometryReference invisibleLevelReference, out string invisibleReason))
                {
                    workingReferences = ReplaceCurtainElevationLevelReference(
                        workingReferences, invisibleLevelReference);
                    created = TryCreateCurtainElevationDimensionChain(
                        doc, view, frame, dimensionType, "level_offset", "vertical",
                        coordinates, workingReferences, minX, maxX, dimensionLineX,
                        result, false, out levelOffsetId, out levelOffsetSource, out string fallbackReason);
                    if (!created)
                    {
                        levelOffsetReason = AppendCurtainElevationWarning(
                            levelOffsetReason, "invisible Level reference: " + fallbackReason);
                        DeleteCurtainElevationInvisibleLevelReference(
                            doc, result, invisibleLevelReference);
                    }
                }
                else
                {
                    levelOffsetReason = AppendCurtainElevationWarning(
                        levelOffsetReason, "invisible Level reference creation failed: " + invisibleReason);
                }
            }

            if (created)
            {
                result.LevelOffsetDimensionElementId = levelOffsetId;
                result.LevelOffsetDimensionAreReferencesAvailable = GetCurtainElevationDimensionReferencesAvailability(doc, levelOffsetId);
                result.LevelOffsetDimensionReferenceSource = levelOffsetSource;
                result.LevelOffsetDimensionStatus = "created";
                result.CreatedCount++;
                CurtainElevationPendingDimension pending = result.PendingNativeDimensions
                    .LastOrDefault(candidate => candidate.NativeDimensionId == levelOffsetId);
                if (pending != null)
                {
                    pending.RecoveryLevelOffsetCoordinates = coordinates.ToList();
                    pending.RecoveryLevelOffsetReferences = workingReferences.ToList();
                    pending.RecoveryLevelOffsetDimensionLineOffset = dimensionLineX;
                    ConfigureCurtainElevationLevelReferenceRecovery(
                        pending, workingReferences?.FirstOrDefault(reference =>
                            reference?.ReferenceSource == "wall_level_plane_reference"),
                        levelY, minX, maxX);
                }
                return true;
            }
            CurtainElevationGeometryReference failedInvisibleReference = workingReferences?
                .FirstOrDefault(reference => reference?.ReferenceSource == "invisible_detail_curve_fallback");
            DeleteCurtainElevationInvisibleLevelReference(doc, result, failedInvisibleReference);
            result.LevelOffsetDimensionElementId = null;
            result.LevelOffsetDimensionAreReferencesAvailable = false;
            result.LevelOffsetDimensionReferenceSource = "failed";
            result.LevelOffsetDimensionStatus = "failed";
            result.FailedCount++;
            result.Warnings.Add("level offset dimension failed: " + levelOffsetReason);
            return false;
        }


        private bool CurtainElevationGridDimensionsUseNativeReferences(CurtainElevationDimensionResult result)
        {
            if (result == null)
                return false;

            return IsCurtainElevationNativeOrSkippedGridSource(result.HorizontalGridDimensionReferenceSource) &&
                IsCurtainElevationNativeOrSkippedGridSource(result.VerticalGridDimensionReferenceSource);
        }

        private bool IsCurtainElevationNativeOrSkippedGridSource(string source)
        {
            return source == "skipped" ||
                source == "curtain_grid_internal_geometry_reference" ||
                source == "curtain_grid_curve_reference";
        }

        private void DeleteCurtainElevationDimensionArtifacts(Document doc, CurtainElevationDimensionResult result)
        {
            if (doc == null || result == null)
                return;

            var ids = new List<ElementId>
            {
                result.TotalWidthDimensionId,
                result.TotalHeightDimensionId,
                result.LevelOffsetDimensionElementId,
                result.HorizontalGridDimensionId,
                result.VerticalGridDimensionId
            };
            ids.AddRange(result.ReferenceCurveIds);

            foreach (ElementId id in ids
                .Where(id => id != null && id != ElementId.InvalidElementId)
                .Distinct())
            {
                if (doc.GetElement(id) != null)
                    doc.Delete(id);
            }
        }
        private CurtainElevationDimensionStackOffsetResolution ResolveCurtainElevationDimensionStackOffset(
            DimensionType dimensionType,
            int viewScale,
            double fallbackInnerOffsetFt,
            double fallbackStackOffsetFt)
        {
            const double defaultInnerFallbackOffsetFt = 300.0 / 304.8;
            const double defaultStackFallbackOffsetFt = 250.0 / 304.8;
            const double innerOffsetExtraPaperFt = 3.0 / 304.8;
            double safeInnerFallbackOffsetFt = fallbackInnerOffsetFt > 1e-9
                ? fallbackInnerOffsetFt
                : defaultInnerFallbackOffsetFt;
            double safeStackFallbackOffsetFt = fallbackStackOffsetFt > 1e-9
                ? fallbackStackOffsetFt
                : defaultStackFallbackOffsetFt;
            string fallbackReason = null;

            if (dimensionType == null)
            {
                fallbackReason = "dimension_type_unavailable";
            }
            else if (viewScale <= 0)
            {
                fallbackReason = "view_scale_not_positive";
            }
            else
            {
                try
                {
                    Parameter witnessLineLength =
                        dimensionType.get_Parameter(BuiltInParameter.DIM_WITNS_LINE_EXTENSION_BELOW);
                    if (witnessLineLength == null)
                    {
                        fallbackReason = "witness_line_length_parameter_unavailable";
                    }
                    else if (witnessLineLength.StorageType != StorageType.Double)
                    {
                        fallbackReason = $"witness_line_length_storage_type_{witnessLineLength.StorageType}";
                    }
                    else
                    {
                        double witnessLineLengthPaperFt = witnessLineLength.AsDouble();
                        if (witnessLineLengthPaperFt > 1e-9)
                        {
                            return new CurtainElevationDimensionStackOffsetResolution
                            {
                                ResolvedOffsetFt = witnessLineLengthPaperFt * viewScale,
                                InnerOffsetFt = (witnessLineLengthPaperFt + innerOffsetExtraPaperFt) * viewScale,
                                InnerOffsetExtraPaperFt = innerOffsetExtraPaperFt,
                                InnerOffsetSource = "dimension_type_witness_line_length_plus_3_mm",
                                WitnessLineLengthPaperFt = witnessLineLengthPaperFt,
                                ViewScale = viewScale,
                                Source = "dimension_type_witness_line_length"
                            };
                        }

                        fallbackReason = "witness_line_length_not_positive";
                    }
                }
                catch (Exception ex)
                {
                    fallbackReason = $"witness_line_length_read_failed: {ex.Message}";
                }
            }

            string innerFallbackReason = fallbackReason;
            string stackFallbackReason = fallbackReason;
            if (fallbackInnerOffsetFt <= 1e-9)
                innerFallbackReason = AppendCurtainElevationWarning(innerFallbackReason, "fallback_inner_offset_not_positive_used_default_300_mm");
            if (fallbackStackOffsetFt <= 1e-9)
                stackFallbackReason = AppendCurtainElevationWarning(stackFallbackReason, "fallback_stack_offset_not_positive_used_default_250_mm");

            return new CurtainElevationDimensionStackOffsetResolution
            {
                ResolvedOffsetFt = safeStackFallbackOffsetFt,
                InnerOffsetFt = safeInnerFallbackOffsetFt,
                InnerOffsetExtraPaperFt = innerOffsetExtraPaperFt,
                InnerOffsetSource = "parameter_fallback",
                InnerOffsetFallbackReason = innerFallbackReason,
                WitnessLineLengthPaperFt = null,
                ViewScale = viewScale,
                Source = "parameter_fallback",
                FallbackReason = stackFallbackReason,
                Warning = $"Dimension offsets used fallbacks: dimensionOffsetMm={safeInnerFallbackOffsetFt * 304.8:F3} mm ({innerFallbackReason}); dimensionStackOffsetMm={safeStackFallbackOffsetFt * 304.8:F3} mm ({stackFallbackReason})."
            };
        }

        private void VerifyCurtainElevationDimensionResult(Document doc, View view, CurtainElevationDimensionResult result)
        {
            if (doc == null || view == null || result == null)
                return;

            var ids = new[]
            {
                result.TotalWidthDimensionId,
                result.HorizontalGridDimensionId,
                result.TotalHeightDimensionId,
                result.LevelOffsetDimensionElementId,
                result.VerticalGridDimensionId
            };

            result.VerifiedCount = 0;
            foreach (ElementId id in ids.Distinct())
            {
                if (id == null || id == ElementId.InvalidElementId)
                    continue;

                Element element = doc.GetElement(id);
                Dimension dimension = element as Dimension;
                if (dimension == null)
                {
                    result.CreationErrors.Add($"Dimension id {id.GetIdValue()} was returned but cannot be read back as Dimension.");
                    continue;
                }

                if (dimension.OwnerViewId != view.Id)
                {
                    result.CreationErrors.Add($"Dimension id {id.GetIdValue()} owner view is {dimension.OwnerViewId.GetIdValue()}, expected {view.Id.GetIdValue()}.");
                    continue;
                }

                result.VerifiedCount++;
            }

            if (result.AttemptCount > 0 && result.VerifiedCount == 0)
            {
                result.Status = "failed_no_dimension_created";
                if (result.CreationErrors.Count == 0)
                result.CreationErrors.Add("No created dimension id could be verified in the target elevation view.");
            }
        }


        private bool TryRecoverEnhancedCurtainElevationTotalHeightDimension(
            Document doc,
            CurtainElevationDimensionResult result,
            CurtainElevationPendingDimension pending,
            out string reason)
        {
            reason = null;
            if (pending?.RecoveryTotalHeightCoordinates == null ||
                pending.RecoveryTotalHeightReferences == null ||
                pending.RecoveryLevelOffsetCoordinates == null ||
                pending.RecoveryLevelOffsetReferences == null)
            {
                reason = "recovery inputs were unavailable.";
                return false;
            }

            if (!TryCreateCurtainElevationGeometryReferenceDimension(
                doc, pending.View, pending.Frame, pending.DimensionType, "vertical",
                pending.RecoveryTotalHeightCoordinates, pending.RecoveryTotalHeightReferences,
                pending.DimensionLineOffset, out ElementId totalHeightId,
                out bool? totalHeightReferencesAvailable, out string totalHeightReason))
            {
                reason = "original total height recovery failed: " + totalHeightReason;
                return false;
            }

            result.TotalHeightDimensionId = totalHeightId;
            result.TotalHeightDimensionAreReferencesAvailable = totalHeightReferencesAvailable;
            result.TotalHeightDimensionReferenceSource = ResolveCurtainElevationDimensionReferenceSource(
                pending.RecoveryTotalHeightReferences);
            result.LevelOffsetDimensionElementId = null;
            result.LevelOffsetDimensionAreReferencesAvailable = null;
            result.LevelOffsetDimensionReferenceSource = null;
            result.LevelOffsetDimensionMode = "separate_outer_post_commit_fallback";

            List<CurtainElevationGeometryReference> recoveryLevelOffsetReferences =
                pending.RecoveryLevelOffsetReferences.ToList();
            CurtainElevationGeometryReference recoveryInvisibleReference = null;
            if (pending.RecoverLevelOffsetWithInvisibleReference)
            {
                if (!TryCreateCurtainElevationInvisibleLevelReference(
                    doc, pending.View, pending.Frame,
                    pending.RecoveryLevelReferenceMinX, pending.RecoveryLevelReferenceMaxX,
                    pending.RecoveryLevelY, result,
                    out recoveryInvisibleReference, out string invisibleReason))
                {
                    result.LevelOffsetDimensionStatus = "failed";
                    result.LevelOffsetDimensionAreReferencesAvailable = false;
                    result.LevelOffsetDimensionReferenceSource = "failed";
                    result.FailedCount++;
                    result.Warnings.Add("level offset post-commit fallback could not create an invisible Level reference: " + invisibleReason);
                    reason = "original total height recovered, but invisible Level reference creation failed: " + invisibleReason;
                    return true;
                }

                recoveryLevelOffsetReferences = ReplaceCurtainElevationLevelReference(
                    recoveryLevelOffsetReferences, recoveryInvisibleReference);
            }

            if (TryCreateCurtainElevationGeometryReferenceDimension(
                doc, pending.View, pending.Frame, pending.DimensionType, "vertical",
                pending.RecoveryLevelOffsetCoordinates, recoveryLevelOffsetReferences,
                pending.RecoveryLevelOffsetDimensionLineOffset, out ElementId levelOffsetId,
                out bool? levelOffsetReferencesAvailable, out string levelOffsetReason))
            {
                result.LevelOffsetDimensionElementId = levelOffsetId;
                result.LevelOffsetDimensionAreReferencesAvailable = levelOffsetReferencesAvailable;
                result.LevelOffsetDimensionReferenceSource = ResolveCurtainElevationDimensionReferenceSource(
                    recoveryLevelOffsetReferences);
                result.LevelOffsetDimensionStatus = "created_post_commit_fallback";
                result.CreatedCount++;
                return true;
            }

            DeleteCurtainElevationInvisibleLevelReference(doc, result, recoveryInvisibleReference);
            result.LevelOffsetDimensionStatus = "failed";
            result.LevelOffsetDimensionAreReferencesAvailable = false;
            result.LevelOffsetDimensionReferenceSource = "failed";
            result.FailedCount++;
            result.Warnings.Add("level offset post-commit fallback failed: " + levelOffsetReason);
            reason = "original total height recovered, but separate level offset failed: " + levelOffsetReason;
            return true;
        }
        private bool TryRecoverCurtainElevationLevelOffsetWithInvisibleReference(
            Document doc,
            CurtainElevationDimensionResult result,
            CurtainElevationPendingDimension pending,
            out string reason)
        {
            reason = null;
            if (pending?.RecoveryLevelOffsetCoordinates == null ||
                pending.RecoveryLevelOffsetReferences == null)
            {
                reason = "level offset recovery inputs were unavailable.";
                return false;
            }

            if (!TryCreateCurtainElevationInvisibleLevelReference(
                doc, pending.View, pending.Frame,
                pending.RecoveryLevelReferenceMinX, pending.RecoveryLevelReferenceMaxX,
                pending.RecoveryLevelY, result,
                out CurtainElevationGeometryReference invisibleReference,
                out string invisibleReason))
            {
                reason = "invisible Level reference creation failed: " + invisibleReason;
                return false;
            }

            List<CurtainElevationGeometryReference> recoveryReferences =
                ReplaceCurtainElevationLevelReference(
                    pending.RecoveryLevelOffsetReferences, invisibleReference);
            if (!TryCreateCurtainElevationGeometryReferenceDimension(
                doc, pending.View, pending.Frame, pending.DimensionType, "vertical",
                pending.RecoveryLevelOffsetCoordinates, recoveryReferences,
                pending.RecoveryLevelOffsetDimensionLineOffset,
                out ElementId dimensionId, out bool? referencesAvailable,
                out string dimensionReason))
            {
                DeleteCurtainElevationInvisibleLevelReference(
                    doc, result, invisibleReference);
                reason = "invisible Level reference dimension failed: " + dimensionReason;
                return false;
            }

            result.LevelOffsetDimensionElementId = dimensionId;
            result.LevelOffsetDimensionAreReferencesAvailable = referencesAvailable;
            result.LevelOffsetDimensionReferenceSource = "invisible_detail_curve_fallback";
            result.LevelOffsetDimensionStatus = "created_post_commit_fallback";
            reason = "recovered with invisible_detail_curve_fallback.";
            return true;
        }

        private void FinalizeCurtainElevationDimensionsAfterCommit(
            Document doc,
            IEnumerable<CurtainElevationDimensionResult> dimensionResults)
        {
            List<CurtainElevationDimensionResult> results = dimensionResults?
                .Where(result => result != null)
                .ToList() ?? new List<CurtainElevationDimensionResult>();
            var invalidDimensions = new List<(CurtainElevationDimensionResult Result, CurtainElevationPendingDimension Pending)>();

            foreach (CurtainElevationDimensionResult result in results)
            {
                foreach (CurtainElevationPendingDimension pending in result.PendingNativeDimensions)
                {
                    Dimension dimension = null;
                    string failureReason = null;
                    int? referenceCount = null;
                    bool? referencesAvailable = null;
                    bool isLevelOffsetPlane = IsCurtainElevationLevelOffsetPlanePending(pending);
                    string validationMode = "failed";

                    try
                    {
                        dimension = doc.GetElement(pending.NativeDimensionId) as Dimension;
                        if (dimension == null)
                        {
                            failureReason = "Dimension does not exist after commit.";
                        }
                        else if (dimension.OwnerViewId != pending.View.Id)
                        {
                            failureReason = $"OwnerViewId is {dimension.OwnerViewId.GetIdValue()}, expected {pending.View.Id.GetIdValue()}.";
                        }
                        else
                        {
                            referencesAvailable = dimension.AreReferencesAvailable;
                            referenceCount = dimension.References?.Size ?? 0;
                            if (isLevelOffsetPlane)
                            {
                                pending.ExpectedSegmentValuesMm =
                                    GetCurtainElevationExpectedSegmentValuesMm(pending.Coordinates);
                                pending.ActualSegmentValuesMm =
                                    GetCurtainElevationDimensionValuesMm(dimension);
                                pending.SegmentValuesPassed = CurtainElevationSegmentValuesMatch(
                                    pending.ExpectedSegmentValuesMm,
                                    pending.ActualSegmentValuesMm,
                                    0.5);
                            }

                            if (referencesAvailable != true)
                            {
                                if (isLevelOffsetPlane &&
                                    referenceCount == pending.ExpectedReferenceCount &&
                                    pending.SegmentValuesPassed == true)
                                    validationMode = "level_plane_segment_validation";
                                else
                                    failureReason = "AreReferencesAvailable is not true after commit in the active owner view.";
                            }
                            else if (referenceCount != pending.ExpectedReferenceCount)
                                failureReason = $"Reference count is {referenceCount ?? 0}, expected {pending.ExpectedReferenceCount}.";
                            else
                                validationMode = "strict_references_available";
                        }
                    }
                    catch (Exception ex)
                    {
                        failureReason = "Post-commit validation threw: " + ex.Message;
                    }

                    pending.PostCommitAreReferencesAvailable = referencesAvailable;
                    pending.PostCommitReferenceCount = referenceCount;
                    pending.PostCommitValidationPassed = string.IsNullOrWhiteSpace(failureReason);
                    pending.PostCommitFailureReason = failureReason;
                    pending.PostCommitValidationMode = validationMode;
                    result.PostCommitDimensionValidation.Add(new
                    {
                        WallId = result.WallId?.GetIdValue(),
                        ViewId = pending.View?.Id.GetIdValue(),
                        DimensionKind = pending.Kind,
                        DimensionId = pending.NativeDimensionId?.GetIdValue(),
                        PreCommitAreReferencesAvailable = pending.PreCommitAreReferencesAvailable,
                        PostCommitAreReferencesAvailable = pending.PostCommitAreReferencesAvailable,
                        ExpectedReferenceCount = pending.ExpectedReferenceCount,
                        PostCommitReferenceCount = pending.PostCommitReferenceCount,
                        PostCommitValidationPassed = pending.PostCommitValidationPassed,
                        PostCommitFailureReason = pending.PostCommitFailureReason,
                        PostCommitValidationMode = pending.PostCommitValidationMode,
                        ExpectedSegmentValuesMm = pending.ExpectedSegmentValuesMm,
                        ActualSegmentValuesMm = pending.ActualSegmentValuesMm,
                        SegmentValuesPassed = pending.SegmentValuesPassed,
                        NativeReferenceSource = GetCurtainElevationDimensionReferenceSource(result, pending.Kind)
                    });

                    if (pending.PostCommitValidationPassed)
                    {
                        SetCurtainElevationDimensionAvailability(result, pending.Kind, referencesAvailable);
                    }
                    else
                    {
                        invalidDimensions.Add((result, pending));
                    }
                }
            }

            if (invalidDimensions.Count > 0)
            {
                using (Transaction repair = TransactionHelper.Begin(doc, "修復無效帷幕牆立面尺寸"))
                {
                    repair.Start();
                    foreach (var item in invalidDimensions)
                    {
                        CurtainElevationDimensionResult result = item.Result;
                        CurtainElevationPendingDimension pending = item.Pending;
                        try
                        {
                            if (pending.NativeDimensionId != null &&
                                pending.NativeDimensionId != ElementId.InvalidElementId &&
                                doc.GetElement(pending.NativeDimensionId) != null)
                            {
                                doc.Delete(pending.NativeDimensionId);
                            }

                            string failurePrefix = $"{pending.Kind} native dimension failed post-commit validation: {pending.PostCommitFailureReason}";
                            if (pending.RecoverEnhancedTotalHeightAsSeparateDimensions &&
                                TryRecoverEnhancedCurtainElevationTotalHeightDimension(
                                    doc, result, pending, out string enhancedRecoveryReason))
                            {
                                result.DimensionFallbackReason = AppendCurtainElevationWarning(
                                    result.DimensionFallbackReason,
                                    failurePrefix + " Recovered as original total height plus a separate level offset dimension. " + enhancedRecoveryReason);
                                continue;
                            }
                            if (pending.RecoverLevelOffsetWithInvisibleReference)
                            {
                                if (TryRecoverCurtainElevationLevelOffsetWithInvisibleReference(
                                    doc, result, pending, out string levelRecoveryReason))
                                {
                                    result.DimensionFallbackReason = AppendCurtainElevationWarning(
                                        result.DimensionFallbackReason,
                                        failurePrefix + " " + levelRecoveryReason);
                                    continue;
                                }

                                failurePrefix = AppendCurtainElevationWarning(
                                    failurePrefix, levelRecoveryReason);
                            }

                            result.DimensionFallbackReason = AppendCurtainElevationWarning(result.DimensionFallbackReason, failurePrefix);
                            string fallbackReason = null;
                            if (pending.AllowDetailCurveFallback &&
                                TryCreateCurtainElevationDetailCurveFallbackDimension(
                                    doc,
                                    pending.View,
                                    pending.Frame,
                                    pending.DimensionType,
                                    pending.Axis,
                                    pending.Coordinates,
                                    pending.MinOther,
                                    pending.MaxOther,
                                    pending.DimensionLineOffset,
                                    result,
                                    out ElementId fallbackDimensionId,
                                    out fallbackReason))
                            {
                                string fallbackSource = pending.Kind == "total_width"
                                    ? "detail_curve_fallback_from_wall_boundary_coordinates"
                                    : "detail_curve_fallback_from_curtain_grid_coordinates";
                                SetCurtainElevationDimensionResult(result, pending.Kind, fallbackDimensionId, fallbackSource, null);
                            }
                            else
                            {
                                string fallbackFailure = pending.AllowDetailCurveFallback
                                    ? "detail curve fallback failed: " + (fallbackReason ?? "unknown reason")
                                    : "detail curve fallback is disabled for this dimension.";
                                SetCurtainElevationDimensionResult(result, pending.Kind, null, "failed", false);
                                result.CreatedCount = Math.Max(0, result.CreatedCount - 1);
                                result.FailedCount++;
                                result.Warnings.Add(failurePrefix + " " + fallbackFailure);
                            }
                        }
                        catch (Exception ex)
                        {
                            SetCurtainElevationDimensionResult(result, pending.Kind, null, "failed", false);
                            result.CreatedCount = Math.Max(0, result.CreatedCount - 1);
                            result.FailedCount++;
                            result.Warnings.Add($"{pending.Kind} post-commit repair failed: {ex.Message}");
                        }
                    }

                    repair.Commit();
                }

                foreach (var item in invalidDimensions)
                {
                    ElementId finalDimensionId = GetCurtainElevationDimensionId(item.Result, item.Pending.Kind);
                    if (finalDimensionId != null && finalDimensionId != ElementId.InvalidElementId)
                    {
                        SetCurtainElevationDimensionAvailability(
                            item.Result,
                            item.Pending.Kind,
                            GetCurtainElevationDimensionReferencesAvailability(doc, finalDimensionId));
                    }

                    ElementId levelOffsetDimensionId = item.Result.LevelOffsetDimensionElementId;
                    if (levelOffsetDimensionId != null && levelOffsetDimensionId != ElementId.InvalidElementId)
                    {
                        item.Result.LevelOffsetDimensionAreReferencesAvailable =
                            GetCurtainElevationDimensionReferencesAvailability(doc, levelOffsetDimensionId);
                    }
                }
            }

            foreach (CurtainElevationDimensionResult result in results)
            {
                result.AttemptCount = result.CreatedCount + result.FailedCount;
                result.Status = result.CreatedCount > 0
                    ? (result.FailedCount > 0 ? "partial" : "created")
                    : "failed";
            }
        }

        private ElementId GetCurtainElevationDimensionId(CurtainElevationDimensionResult result, string kind)
        {
            if (kind == "total_width") return result.TotalWidthDimensionId;
            if (kind == "total_height") return result.TotalHeightDimensionId;
            if (kind == "level_offset") return result.LevelOffsetDimensionElementId;
            if (kind == "horizontal_grid") return result.HorizontalGridDimensionId;
            if (kind == "vertical_grid") return result.VerticalGridDimensionId;
            return null;
        }

        private string GetCurtainElevationDimensionReferenceSource(CurtainElevationDimensionResult result, string kind)
        {
            if (kind == "total_width") return result.TotalWidthDimensionReferenceSource;
            if (kind == "total_height") return result.TotalHeightDimensionReferenceSource;
            if (kind == "level_offset") return result.LevelOffsetDimensionReferenceSource;
            if (kind == "horizontal_grid") return result.HorizontalGridDimensionReferenceSource;
            if (kind == "vertical_grid") return result.VerticalGridDimensionReferenceSource;
            return null;
        }

        private void SetCurtainElevationDimensionAvailability(CurtainElevationDimensionResult result, string kind, bool? available)
        {
            if (kind == "total_width") result.TotalWidthDimensionAreReferencesAvailable = available;
            else if (kind == "total_height") result.TotalHeightDimensionAreReferencesAvailable = available;
            else if (kind == "level_offset") result.LevelOffsetDimensionAreReferencesAvailable = available;
            else if (kind == "horizontal_grid") result.HorizontalGridDimensionAreReferencesAvailable = available;
            else if (kind == "vertical_grid") result.VerticalGridDimensionAreReferencesAvailable = available;
        }

        private void SetCurtainElevationDimensionResult(
            CurtainElevationDimensionResult result,
            string kind,
            ElementId dimensionId,
            string referenceSource,
            bool? available)
        {
            if (kind == "total_width")
            {
                result.TotalWidthDimensionId = dimensionId;
                result.TotalWidthDimensionReferenceSource = referenceSource;
            }
            else if (kind == "total_height")
            {
                ElementId previousTotalHeightId = result.TotalHeightDimensionId;
                result.TotalHeightDimensionId = dimensionId;
                result.TotalHeightDimensionReferenceSource = referenceSource;
                if (previousTotalHeightId != null &&
                    result.LevelOffsetDimensionElementId != null &&
                    previousTotalHeightId.GetIdValue() == result.LevelOffsetDimensionElementId.GetIdValue())
                {
                    result.LevelOffsetDimensionElementId = dimensionId;
                    result.LevelOffsetDimensionReferenceSource = referenceSource;
                    result.LevelOffsetDimensionAreReferencesAvailable = available;
                    if (dimensionId == null || dimensionId == ElementId.InvalidElementId)
                        result.LevelOffsetDimensionStatus = "failed";
                }
            }
            else if (kind == "level_offset")
            {
                result.LevelOffsetDimensionElementId = dimensionId;
                result.LevelOffsetDimensionReferenceSource = referenceSource;
            }
            else if (kind == "horizontal_grid")
            {
                result.HorizontalGridDimensionId = dimensionId;
                result.HorizontalGridDimensionReferenceSource = referenceSource;
            }
            else if (kind == "vertical_grid")
            {
                result.VerticalGridDimensionId = dimensionId;
                result.VerticalGridDimensionReferenceSource = referenceSource;
            }

            SetCurtainElevationDimensionAvailability(result, kind, available);
        }
        private CurtainElevationDimensionAttempt TryDiagnoseCurtainGeometryDimension(
            Document doc,
            View view,
            Transform frame,
            DimensionType dimensionType,
            string name,
            string axis,
            List<double> coordinates,
            List<CurtainElevationGeometryReference> geometryReferences,
            double dimensionLineOffset)
        {
            List<double> distinct = NormalizeCurtainElevationDimensionCoordinates(coordinates);
            var attempt = new CurtainElevationDimensionAttempt
            {
                Name = name,
                Method = "geometry_reference",
                ReferenceCount = geometryReferences?.Count ?? 0,
                ExpectedReferenceCount = distinct.Count,
                ReferenceSource = ResolveCurtainElevationDimensionReferenceSource(geometryReferences),
                ReferencePriorityProfile = geometryReferences?
                    .Where(reference => reference?.CurtainGridLineId != null)
                    .Select(reference => (int?)reference.ReferencePriority)
                    .FirstOrDefault(),
                InputReferenceElementIds = geometryReferences?
                    .Where(reference => reference?.ElementId != null)
                    .Select(reference => reference.ElementId.GetIdValue())
                    .ToList() ?? new List<IdType>(),
                InputStableRepresentations = geometryReferences?
                    .Where(reference => !string.IsNullOrWhiteSpace(reference?.StableRepresentation))
                    .Select(reference => reference.StableRepresentation)
                    .ToList() ?? new List<string>(),
                ExpectedCurtainGridLineIds = geometryReferences?
                    .Where(reference => reference?.CurtainGridLineId != null)
                    .Select(reference => reference.CurtainGridLineId.GetIdValue())
                    .Distinct()
                    .ToList() ?? new List<IdType>()
            };

            try
            {
                if (distinct.Count < 2)
                {
                    attempt.FailureMessage = "not enough coordinates.";
                    return attempt;
                }

                if (axis == "horizontal")
                {
                    attempt.DimensionLineStart = CurtainElevationPointAt2D(frame, distinct.First(), dimensionLineOffset);
                    attempt.DimensionLineEnd = CurtainElevationPointAt2D(frame, distinct.Last(), dimensionLineOffset);
                }
                else
                {
                    attempt.DimensionLineStart = CurtainElevationPointAt2D(frame, dimensionLineOffset, distinct.First());
                    attempt.DimensionLineEnd = CurtainElevationPointAt2D(frame, dimensionLineOffset, distinct.Last());
                }

                if (geometryReferences == null || geometryReferences.Count != distinct.Count)
                {
                    attempt.FailureMessage = $"not enough geometry references. Need {distinct.Count}, got {geometryReferences?.Count ?? 0}.";
                    return attempt;
                }

                var referenceArray = new ReferenceArray();
                foreach (CurtainElevationGeometryReference geometryReference in geometryReferences)
                {
                    if (geometryReference?.Reference == null)
                    {
                        attempt.FailureMessage = "geometry reference contains null Reference.";
                        return attempt;
                    }

                    referenceArray.Append(geometryReference.Reference);
                }

                Dimension dimension = doc.Create.NewDimension(
                    view,
                    Line.CreateBound(attempt.DimensionLineStart, attempt.DimensionLineEnd),
                    referenceArray);
                if (dimension == null)
                {
                    attempt.FailureMessage = "Revit returned null Dimension.";
                    return attempt;
                }

                ApplyDimensionType(dimension, dimensionType);
                doc.Regenerate();
                attempt.DimensionId = dimension.Id;
                attempt.OwnerViewId = dimension.OwnerViewId;
                try
                {
                    attempt.PreCommitAreReferencesAvailable = dimension.AreReferencesAvailable;
                }
                catch
                {
                    attempt.PreCommitAreReferencesAvailable = null;
                }
                attempt.Success = true;
                return attempt;
            }
            catch (Exception ex)
            {
                attempt.FailureMessage = ex.Message;
                return attempt;
            }
        }

        private CurtainElevationDimensionAttempt TryDiagnoseCurtainReferencePlaneDimension(
            Document doc,
            View view,
            Transform frame,
            DimensionType dimensionType,
            string name,
            string axis,
            List<double> coordinates,
            double minOther,
            double maxOther,
            double dimensionLineOffset,
            List<ElementId> referencePlaneIds,
            out int referenceCount)
        {
            referenceCount = 0;
            List<double> distinct = NormalizeCurtainElevationDimensionCoordinates(coordinates);
            var attempt = new CurtainElevationDimensionAttempt
            {
                Name = name,
                Method = "reference_plane_fallback",
                ExpectedReferenceCount = distinct.Count
            };

            try
            {
                if (distinct.Count < 2)
                {
                    attempt.FailureMessage = "not enough coordinates.";
                    return attempt;
                }

                double stubMin = minOther;
                double stubMax = maxOther;
                if (Math.Abs(stubMax - stubMin) < 1e-6)
                    stubMax = stubMin + 100.0 / 304.8;

                var referenceArray = new ReferenceArray();
                foreach (double coordinate in distinct)
                {
                    XYZ bubbleEnd;
                    XYZ freeEnd;
                    if (axis == "horizontal")
                    {
                        bubbleEnd = CurtainElevationPointAt2D(frame, coordinate, stubMin);
                        freeEnd = CurtainElevationPointAt2D(frame, coordinate, stubMax);
                    }
                    else
                    {
                        bubbleEnd = CurtainElevationPointAt2D(frame, stubMin, coordinate);
                        freeEnd = CurtainElevationPointAt2D(frame, stubMax, coordinate);
                    }

                    ReferencePlane referencePlane = doc.Create.NewReferencePlane(bubbleEnd, freeEnd, frame.BasisZ, view);
                    if (referencePlane == null)
                    {
                        attempt.FailureMessage = "failed to create ReferencePlane.";
                        return attempt;
                    }

                    referencePlaneIds?.Add(referencePlane.Id);
                    Reference reference = referencePlane.GetReference();
                    if (reference == null)
                    {
                        attempt.FailureMessage = "ReferencePlane.GetReference() returned null.";
                        return attempt;
                    }

                    referenceArray.Append(reference);
                    referenceCount++;
                }

                attempt.ReferenceCount = referenceCount;
                if (axis == "horizontal")
                {
                    attempt.DimensionLineStart = CurtainElevationPointAt2D(frame, distinct.First(), dimensionLineOffset);
                    attempt.DimensionLineEnd = CurtainElevationPointAt2D(frame, distinct.Last(), dimensionLineOffset);
                }
                else
                {
                    attempt.DimensionLineStart = CurtainElevationPointAt2D(frame, dimensionLineOffset, distinct.First());
                    attempt.DimensionLineEnd = CurtainElevationPointAt2D(frame, dimensionLineOffset, distinct.Last());
                }

                Dimension dimension = doc.Create.NewDimension(
                    view,
                    Line.CreateBound(attempt.DimensionLineStart, attempt.DimensionLineEnd),
                    referenceArray);
                if (dimension == null)
                {
                    attempt.FailureMessage = "Revit returned null Dimension.";
                    return attempt;
                }

                ApplyDimensionType(dimension, dimensionType);
                doc.Regenerate();
                attempt.DimensionId = dimension.Id;
                attempt.OwnerViewId = dimension.OwnerViewId;
                try
                {
                    attempt.PreCommitAreReferencesAvailable = dimension.AreReferencesAvailable;
                }
                catch
                {
                    attempt.PreCommitAreReferencesAvailable = null;
                }
                attempt.Success = true;
                return attempt;
            }
            catch (Exception ex)
            {
                attempt.ReferenceCount = referenceCount;
                attempt.FailureMessage = ex.Message;
                return attempt;
            }
        }

        private List<double> GetCurtainElevationDimensionValuesMm(Dimension dimension)
        {
            var values = new List<double>();
            if (dimension == null)
                return values;

            try
            {
                if (dimension.NumberOfSegments > 0 && dimension.Segments != null)
                {
                    foreach (DimensionSegment segment in dimension.Segments)
                        values.Add(Math.Round(Convert.ToDouble(segment.Value) * 304.8, 4));
                }
                else if (dimension.Value.HasValue)
                {
                    values.Add(Math.Round(dimension.Value.Value * 304.8, 4));
                }
            }
            catch
            {
                // Dimension values are best-effort diagnostics only.
            }
            return values;
        }
        private bool IsCurtainElevationLevelOffsetPlanePending(
            CurtainElevationPendingDimension pending)
        {
            return pending != null &&
                pending.NativeReferenceSource == "wall_level_plane_reference" &&
                (pending.Kind == "level_offset" ||
                    (pending.Kind == "total_height" &&
                        pending.RecoverEnhancedTotalHeightAsSeparateDimensions));
        }

        private List<double> GetCurtainElevationExpectedSegmentValuesMm(
            IList<double> coordinates)
        {
            if (coordinates == null || coordinates.Count < 2)
                return new List<double>();

            return coordinates.Zip(
                coordinates.Skip(1),
                (start, end) => Math.Round(Math.Abs(end - start) * 304.8, 4)).ToList();
        }

        private bool CurtainElevationSegmentValuesMatch(
            IList<double> expected,
            IList<double> actual,
            double toleranceMm = 0.5)
        {
            return expected != null && actual != null && expected.Count == actual.Count &&
                expected.Zip(actual, (left, right) => Math.Abs(left - right) <= toleranceMm)
                    .All(matches => matches);
        }
        private CurtainElevationDimensionReferenceState CaptureCurtainElevationDimensionReferenceState(
            Document doc,
            View view,
            CurtainElevationDimensionAttempt attempt,
            string phase,
            bool wasViewOpen,
            bool wasViewActive,
            bool updateCanonicalValidation)
        {
            var state = new CurtainElevationDimensionReferenceState
            {
                Phase = phase,
                WasViewOpen = wasViewOpen,
                WasViewActive = wasViewActive
            };

            try
            {
                Dimension dimension = attempt?.DimensionId != null
                    ? doc.GetElement(attempt.DimensionId) as Dimension
                    : null;
                state.DimensionExists = dimension != null;
                state.OwnerViewId = dimension?.OwnerViewId.GetIdValue();
                if (dimension == null)
                {
                    state.FailureReason = "Dimension does not exist after commit.";
                }
                else
                {
                    state.AreReferencesAvailable = dimension.AreReferencesAvailable;
                    state.ReferenceCount = dimension.References?.Size ?? 0;
                    if (dimension.References != null)
                    {
                        foreach (Reference reference in dimension.References)
                        {
                            if (reference == null)
                                continue;

                            state.ReferenceElementIds.Add(reference.ElementId.GetIdValue());
                            try
                            {
                                string stable = reference.ConvertToStableRepresentation(doc);
                                state.StableRepresentations.Add(stable);
                            }
                            catch
                            {
                                state.StableRepresentations.Add(null);
                            }
                        }
                    }

                    if (dimension.OwnerViewId != view.Id)
                        state.FailureReason = $"OwnerViewId is {dimension.OwnerViewId.GetIdValue()}, expected {view.Id.GetIdValue()}.";
                    else if (state.AreReferencesAvailable != true)
                        state.FailureReason = "AreReferencesAvailable is not true.";
                    else if (state.ReferenceCount != attempt.ExpectedReferenceCount)
                        state.FailureReason = $"Reference count is {state.ReferenceCount ?? 0}, expected {attempt.ExpectedReferenceCount}.";
                }

                state.StableRepresentationRoundTripPassed = state.StableRepresentations.Count > 0 &&
                    state.StableRepresentations.All(stable =>
                    {
                        if (string.IsNullOrWhiteSpace(stable))
                            return false;
                        try
                        {
                            Reference parsed = Reference.ParseFromStableRepresentation(doc, stable);
                            return parsed != null && state.ReferenceElementIds.Contains(parsed.ElementId.GetIdValue());
                        }
                        catch
                        {
                            return false;
                        }
                    });
                state.InputStableRepresentationRoundTripPassed = attempt?.InputStableRepresentations?.Count > 0 &&
                    attempt.InputStableRepresentations.All(stable =>
                    {
                        try { return Reference.ParseFromStableRepresentation(doc, stable) != null; }
                        catch { return false; }
                    });
                state.ReferencesMatchExpectedCurtainGridLines = attempt?.ExpectedCurtainGridLineIds == null ||
                    attempt.ExpectedCurtainGridLineIds.Count == 0 ||
                    attempt.ExpectedCurtainGridLineIds.All(id => state.ReferenceElementIds.Contains(id));
                if (string.IsNullOrWhiteSpace(state.FailureReason) && !state.StableRepresentationRoundTripPassed)
                    state.FailureReason = "Readback stable representation parse round-trip failed.";
                if (string.IsNullOrWhiteSpace(state.FailureReason) && !state.InputStableRepresentationRoundTripPassed)
                    state.FailureReason = "Input stable representation parse round-trip failed.";
                if (string.IsNullOrWhiteSpace(state.FailureReason) && !state.ReferencesMatchExpectedCurtainGridLines)
                    state.FailureReason = "Readback references do not map to the expected CurtainGridLine element ids.";
            }
            catch (Exception ex)
            {
                state.FailureReason = "Reference state readback threw: " + ex.Message;
            }

            state.ValidationPassed = string.IsNullOrWhiteSpace(state.FailureReason);
            if (updateCanonicalValidation && attempt != null)
            {
                attempt.ExistsAfterCreate = state.DimensionExists;
                attempt.OwnerViewId = state.OwnerViewId.HasValue ? new ElementId(state.OwnerViewId.Value) : null;
                attempt.PostCommitAreReferencesAvailable = state.AreReferencesAvailable;
                attempt.PostCommitReferenceCount = state.ReferenceCount;
                attempt.PostCommitValidationPassed = state.ValidationPassed;
                attempt.PostCommitFailureReason = state.FailureReason;
                if (!state.ValidationPassed)
                    attempt.FailureMessage = AppendCurtainElevationWarning(attempt.FailureMessage, state.FailureReason);
            }

            return state;
        }
        private object ToCurtainElevationDimensionAttemptResult(CurtainElevationDimensionAttempt attempt)
        {
            if (attempt == null)
                return null;

            return new
            {
                Name = attempt.Name,
                Method = attempt.Method,
                ReferenceCount = attempt.ReferenceCount,
                DimensionLineStart = ToCurtainElevationPointMm(attempt.DimensionLineStart),
                DimensionLineEnd = ToCurtainElevationPointMm(attempt.DimensionLineEnd),
                Success = attempt.Success,
                DimensionId = attempt.DimensionId?.GetIdValue(),
                OwnerViewId = attempt.OwnerViewId?.GetIdValue(),
                ExistsAfterCreate = attempt.ExistsAfterCreate,
                PreCommitAreReferencesAvailable = attempt.PreCommitAreReferencesAvailable,
                PostCommitAreReferencesAvailable = attempt.PostCommitAreReferencesAvailable,
                ExpectedReferenceCount = attempt.ExpectedReferenceCount,
                PostCommitReferenceCount = attempt.PostCommitReferenceCount,
                PostCommitValidationPassed = attempt.PostCommitValidationPassed,
                PostCommitFailureReason = attempt.PostCommitFailureReason,
                ReferenceSource = attempt.ReferenceSource,
                ReferencePriorityProfile = attempt.ReferencePriorityProfile,
                InputReferenceElementIds = attempt.InputReferenceElementIds,
                InputStableRepresentations = attempt.InputStableRepresentations,
                ExpectedCurtainGridLineIds = attempt.ExpectedCurtainGridLineIds,
                InactivePostCommitState = attempt.InactivePostCommitState,
                AfterViewActivationState = attempt.AfterViewActivationState,
                ActivePostCommitState = attempt.ActivePostCommitState,
                FailureMessage = attempt.FailureMessage
            };
        }

        private bool TryCreateCurtainElevationDimensionChain(
            Document doc,
            View view,
            Transform frame,
            DimensionType dimensionType,
            string kind,
            string axis,
            List<double> coordinates,
            List<CurtainElevationGeometryReference> geometryReferences,
            double minOther,
            double maxOther,
            double dimensionLineOffset,
            CurtainElevationDimensionResult aggregate,
            bool allowDetailCurveFallback,
            out ElementId dimensionId,
            out string referenceSource,
            out string reason)
        {
            dimensionId = null;
            referenceSource = null;
            reason = null;

            try
            {
                List<double> distinct = NormalizeCurtainElevationDimensionCoordinates(coordinates);
                if (distinct.Count < 2)
                {
                    reason = "not enough coordinates.";
                    referenceSource = "failed";
                    return false;
                }

                if (TryCreateCurtainElevationGeometryReferenceDimension(
                    doc,
                    view,
                    frame,
                    dimensionType,
                    axis,
                    distinct,
                    geometryReferences,
                    dimensionLineOffset,
                    out dimensionId,
                    out bool? preCommitAreReferencesAvailable,
                    out string geometryReason))
                {
                    referenceSource = ResolveCurtainElevationDimensionReferenceSource(geometryReferences);
                    aggregate.PendingNativeDimensions.Add(new CurtainElevationPendingDimension
                    {
                        Kind = kind,
                        View = view,
                        Frame = frame,
                        DimensionType = dimensionType,
                        Axis = axis,
                        Coordinates = distinct.ToList(),
                        MinOther = minOther,
                        MaxOther = maxOther,
                        DimensionLineOffset = dimensionLineOffset,
                        AllowDetailCurveFallback = allowDetailCurveFallback,
                        NativeDimensionId = dimensionId,
                        ExpectedReferenceCount = distinct.Count,
                        NativeReferenceSource = referenceSource,
                        PreCommitAreReferencesAvailable = preCommitAreReferencesAvailable
                    });
                    return true;
                }

                if (!allowDetailCurveFallback)
                {
                    reason = "geometry reference dimension failed; detail curve fallback is disabled for this dimension: " + geometryReason;
                    referenceSource = "failed";
                    aggregate.DimensionFallbackReason = AppendCurtainElevationWarning(
                        aggregate.DimensionFallbackReason,
                        reason);
                    return false;
                }

                bool isWallBoundaryPair = axis == "horizontal" && distinct.Count == 2;
                string fallbackTarget = isWallBoundaryPair ? "wall boundary" : "curtain grid";
                aggregate.DimensionFallbackReason = AppendCurtainElevationWarning(
                    aggregate.DimensionFallbackReason,
                    $"{axis} dimension used invisible detail curve fallback from {fallbackTarget} coordinates: {geometryReason}");

                if (TryCreateCurtainElevationDetailCurveFallbackDimension(
                    doc,
                    view,
                    frame,
                    dimensionType,
                    axis,
                    distinct,
                    minOther,
                    maxOther,
                    dimensionLineOffset,
                    aggregate,
                    out dimensionId,
                    out string fallbackReason))
                {
                    referenceSource = isWallBoundaryPair
                        ? "detail_curve_fallback_from_wall_boundary_coordinates"
                        : "detail_curve_fallback_from_curtain_grid_coordinates";
                    return true;
                }

                reason = $"geometry: {geometryReason}; detail curve fallback: {fallbackReason}";
                referenceSource = "failed";
                return false;
            }
            catch (Exception ex)
            {
                reason = ex.Message;
                referenceSource = "failed";
                return false;
            }
        }

        private string ResolveCurtainElevationDimensionReferenceSource(
            IEnumerable<CurtainElevationGeometryReference> geometryReferences)
        {
            List<CurtainElevationGeometryReference> references = geometryReferences?
                .Where(reference => reference != null)
                .ToList() ?? new List<CurtainElevationGeometryReference>();

            if (references.Any(reference => reference.ReferenceSource == "invisible_detail_curve_fallback"))
                return "invisible_detail_curve_fallback";

            if (references.Any(reference => reference.ReferenceSource == "wall_level_plane_reference"))
                return "wall_level_plane_reference";

            if (references.Any(reference =>
                reference.ReferenceSource == "curtain_grid_internal_geometry_reference"))
            {
                return "curtain_grid_internal_geometry_reference";
            }

            if (references.Any(reference =>
                reference.CurtainGridLineId != null ||
                reference.ReferenceSource == "curtain_grid_curve_reference"))
            {
                return "curtain_grid_curve_reference";
            }

            return "geometry_reference";
        }

        private bool? GetCurtainElevationDimensionReferencesAvailability(Document doc, ElementId dimensionId)
        {
            if (doc == null || dimensionId == null || dimensionId == ElementId.InvalidElementId)
                return null;

            try
            {
                return (doc.GetElement(dimensionId) as Dimension)?.AreReferencesAvailable;
            }
            catch
            {
                return null;
            }
        }


        private bool TryApplyExistingInvisibleLineStyle(Document doc, DetailCurve detailCurve)
        {
            if (doc == null || detailCurve == null)
                return false;

            try
            {
                GraphicsStyle style = TryFindExistingInvisibleLineStyle(doc);
                if (style == null)
                    return false;

                detailCurve.LineStyle = style;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private GraphicsStyle TryFindCurtainElevationLevelInvisibleLineStyle(Document doc)
        {
            GraphicsStyle existingStyle = TryFindExistingInvisibleLineStyle(doc);
            if (existingStyle != null || doc == null)
                return existingStyle;

            try
            {
                IdType invisibleCategoryId =
                    new ElementId(BuiltInCategory.OST_InvisibleLines).GetIdValue();
                return new FilteredElementCollector(doc)
                    .OfClass(typeof(GraphicsStyle))
                    .Cast<GraphicsStyle>()
                    .FirstOrDefault(style =>
                        style != null &&
                        style.GraphicsStyleType == GraphicsStyleType.Projection &&
                        style.GraphicsStyleCategory?.Id != null &&
                        style.GraphicsStyleCategory.Id.GetIdValue() == invisibleCategoryId);
            }
            catch
            {
                return null;
            }
        }



        private GraphicsStyle TryFindExistingInvisibleLineStyle(Document doc)
        {
            if (doc == null)
                return null;

            try
            {
                var candidates = new List<Category>();

                Category invisibleCategory = Category.GetCategory(doc, BuiltInCategory.OST_InvisibleLines);
                if (invisibleCategory != null)
                    candidates.Add(invisibleCategory);

                try
                {
                    Category settingsInvisibleCategory = doc.Settings.Categories.get_Item(BuiltInCategory.OST_InvisibleLines);
                    if (settingsInvisibleCategory != null && !candidates.Any(c => c.Id == settingsInvisibleCategory.Id))
                        candidates.Add(settingsInvisibleCategory);
                }
                catch
                {
                    // Some Revit builds expose invisible lines only as a Lines subcategory.
                }

                try
                {
                    Category linesCategory = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Lines);
                    ElementId invisibleCategoryId = new ElementId(BuiltInCategory.OST_InvisibleLines);
                    if (linesCategory != null)
                    {
                        foreach (Category subCategory in linesCategory.SubCategories)
                        {
                            if (subCategory != null && subCategory.Id == invisibleCategoryId)
                                candidates.Add(subCategory);
                        }
                    }
                }
                catch
                {
                    // Best effort. Do not fall back to name guessing here.
                }

                foreach (Category category in candidates)
                {
                    GraphicsStyle style = category?.GetGraphicsStyle(GraphicsStyleType.Projection);
                    if (style != null)
                        return style;
                }
            }
            catch
            {
            }

            return null;
        }

        private bool TryCreateCurtainElevationDetailCurveFallbackDimension(
            Document doc,
            View view,
            Transform frame,
            DimensionType dimensionType,
            string axis,
            List<double> distinct,
            double minOther,
            double maxOther,
            double dimensionLineOffset,
            CurtainElevationDimensionResult aggregate,
            out ElementId dimensionId,
            out string reason)
        {
            dimensionId = null;
            reason = null;
            var createdReferenceCurves = new List<DetailCurve>();
            ElementId createdDimensionId = null;

            try
            {
                GraphicsStyle invisibleLineStyle = TryFindExistingInvisibleLineStyle(doc);
                if (invisibleLineStyle == null)
                {
                    reason = "BuiltInCategory.OST_InvisibleLines was unavailable.";
                    return false;
                }

                var referenceArray = new ReferenceArray();
                double stubMin = minOther;
                double stubMax = maxOther;
                if (Math.Abs(stubMax - stubMin) < 1e-6)
                    stubMax = stubMin + (100.0 / 304.8);

                foreach (double coordinate in distinct)
                {
                    Line referenceLine;
                    if (axis == "horizontal")
                    {
                        referenceLine = Line.CreateBound(
                            CurtainElevationPointAt2D(frame, coordinate, stubMin),
                            CurtainElevationPointAt2D(frame, coordinate, stubMax));
                    }
                    else
                    {
                        referenceLine = Line.CreateBound(
                            CurtainElevationPointAt2D(frame, stubMin, coordinate),
                            CurtainElevationPointAt2D(frame, stubMax, coordinate));
                    }

                    DetailCurve detailCurve = doc.Create.NewDetailCurve(view, referenceLine);
                    if (detailCurve == null)
                    {
                        reason = "failed to create reference detail curve.";
                        DeleteCurtainElevationDetailCurves(doc, createdReferenceCurves);
                        return false;
                    }

                    createdReferenceCurves.Add(detailCurve);
                    detailCurve.LineStyle = invisibleLineStyle;
                    if (detailCurve.LineStyle == null ||
                        detailCurve.LineStyle.Id.GetIdValue() != invisibleLineStyle.Id.GetIdValue())
                    {
                        throw new InvalidOperationException("Invisible detail curve line style read-back failed.");
                    }
                    Reference reference = detailCurve.GeometryCurve?.Reference;
                    if (reference == null)
                    {
                        reason = "reference detail curve has no Reference before applying invisible line style.";
                        DeleteCurtainElevationDetailCurves(doc, createdReferenceCurves);
                        return false;
                    }

                    referenceArray.Append(reference);
                }

                Line dimensionLine;
                if (axis == "horizontal")
                {
                    dimensionLine = Line.CreateBound(
                        CurtainElevationPointAt2D(frame, distinct.First(), dimensionLineOffset),
                        CurtainElevationPointAt2D(frame, distinct.Last(), dimensionLineOffset));
                }
                else
                {
                    dimensionLine = Line.CreateBound(
                        CurtainElevationPointAt2D(frame, dimensionLineOffset, distinct.First()),
                        CurtainElevationPointAt2D(frame, dimensionLineOffset, distinct.Last()));
                }

                Dimension dimension = doc.Create.NewDimension(view, dimensionLine, referenceArray);
                if (dimension == null)
                {
                    reason = "Revit returned null Dimension.";
                    DeleteCurtainElevationDetailCurves(doc, createdReferenceCurves);
                    return false;
                }

                createdDimensionId = dimension.Id;
                ApplyDimensionType(dimension, dimensionType);

                foreach (DetailCurve detailCurve in createdReferenceCurves)
                    aggregate.ReferenceCurveIds.Add(detailCurve.Id);

                LastCurtainElevationDimensionTypeId = dimensionType.Id.GetIdValue();
                dimensionId = dimension.Id;
                return true;
            }
            catch (Exception ex)
            {
                try
                {
                    if (createdDimensionId != null && doc.GetElement(createdDimensionId) != null)
                        doc.Delete(createdDimensionId);
                }
                catch
                {
                }
                DeleteCurtainElevationDetailCurves(doc, createdReferenceCurves);
                reason = ex.Message;
                return false;
            }
        }

        private void DeleteCurtainElevationDetailCurves(Document doc, IEnumerable<DetailCurve> detailCurves)
        {
            if (doc == null || detailCurves == null)
                return;

            foreach (DetailCurve detailCurve in detailCurves)
            {
                try
                {
                    if (detailCurve != null && detailCurve.Id != ElementId.InvalidElementId && doc.GetElement(detailCurve.Id) != null)
                        doc.Delete(detailCurve.Id);
                }
                catch
                {
                    // Best effort cleanup for failed fallback references.
                }
            }
        }

        private bool TryCreateCurtainElevationGeometryReferenceDimension(
            Document doc,
            View view,
            Transform frame,
            DimensionType dimensionType,
            string axis,
            List<double> coordinates,
            List<CurtainElevationGeometryReference> geometryReferences,
            double dimensionLineOffset,
            out ElementId dimensionId,
            out bool? preCommitAreReferencesAvailable,
            out string reason)
        {
            dimensionId = null;
            preCommitAreReferencesAvailable = null;
            reason = null;
            ElementId createdDimensionId = null;

            try
            {
                if (geometryReferences == null || geometryReferences.Count != coordinates.Count)
                {
                    reason = $"geometry reference count mismatch. Expected {coordinates.Count}, got {geometryReferences?.Count ?? 0}.";
                    return false;
                }

                var referenceArray = new ReferenceArray();
                foreach (CurtainElevationGeometryReference geometryReference in geometryReferences)
                {
                    if (geometryReference?.Reference == null)
                    {
                        reason = "geometry reference contains null Reference.";
                        return false;
                    }

                    referenceArray.Append(geometryReference.Reference);
                }

                Line dimensionLine;
                if (axis == "horizontal")
                {
                    dimensionLine = Line.CreateBound(
                        CurtainElevationPointAt2D(frame, coordinates.First(), dimensionLineOffset),
                        CurtainElevationPointAt2D(frame, coordinates.Last(), dimensionLineOffset));
                }
                else
                {
                    dimensionLine = Line.CreateBound(
                        CurtainElevationPointAt2D(frame, dimensionLineOffset, coordinates.First()),
                        CurtainElevationPointAt2D(frame, dimensionLineOffset, coordinates.Last()));
                }

                Dimension dimension = doc.Create.NewDimension(view, dimensionLine, referenceArray);
                if (dimension == null)
                {
                    reason = "Revit returned null Dimension for geometry references.";
                    return false;
                }

                createdDimensionId = dimension.Id;
                ApplyDimensionType(dimension, dimensionType);
                doc.Regenerate();

                Dimension persistedDimension = doc.GetElement(createdDimensionId) as Dimension;
                if (persistedDimension == null)
                    throw new InvalidOperationException("Dimension was created but could not be read back.");
                if (persistedDimension.OwnerViewId != view.Id)
                    throw new InvalidOperationException($"Dimension owner view is {persistedDimension.OwnerViewId.GetIdValue()}, expected {view.Id.GetIdValue()}.");
                try
                {
                    preCommitAreReferencesAvailable = persistedDimension.AreReferencesAvailable;
                }
                catch
                {
                    preCommitAreReferencesAvailable = null;
                }
                if (persistedDimension.References == null || persistedDimension.References.Size != coordinates.Count)
                    throw new InvalidOperationException($"Dimension reference count is {persistedDimension.References?.Size ?? 0}, expected {coordinates.Count}.");

                LastCurtainElevationDimensionTypeId = dimensionType.Id.GetIdValue();
                dimensionId = createdDimensionId;
                return true;
            }
            catch (Exception ex)
            {
                try
                {
                    if (createdDimensionId != null &&
                        createdDimensionId != ElementId.InvalidElementId &&
                        doc.GetElement(createdDimensionId) != null)
                    {
                        doc.Delete(createdDimensionId);
                        doc.Regenerate();
                    }
                }
                catch
                {
                    // Best-effort cleanup; preserve the original dimension validation failure.
                }

                reason = ex.Message;
                return false;
            }
        }

        private List<double> GetCurtainElevationGridCoordinates(
            Document doc,
            Wall wall,
            Transform frame,
            string targetOrientation,
            double minX,
            double maxX,
            double minY,
            double maxY)
        {
            var values = new List<double>();
            if (targetOrientation == "vertical")
            {
                values.Add(minX);
                values.Add(maxX);
            }
            else
            {
                values.Add(minY);
                values.Add(maxY);
            }

            try
            {
                CurtainGrid grid = wall?.CurtainGrid;
                if (grid == null)
                    return NormalizeCurtainElevationDimensionCoordinates(values);

                var gridIds = new List<ElementId>();
                gridIds.AddRange(grid.GetUGridLineIds());
                gridIds.AddRange(grid.GetVGridLineIds());

                foreach (ElementId id in gridIds)
                {
                    CurtainGridLine gridLine = doc.GetElement(id) as CurtainGridLine;
                    Curve curve = gridLine?.FullCurve;
                    if (curve == null)
                        continue;

                    List<XYZ> points = curve.Tessellate()?.ToList() ?? new List<XYZ>();
                    if (points.Count == 0)
                    {
                        points.Add(curve.GetEndPoint(0));
                        points.Add(curve.GetEndPoint(1));
                    }

                    var local = points.Select(p => frame.Inverse.OfPoint(p)).ToList();
                    double gxMin = local.Min(p => p.X);
                    double gxMax = local.Max(p => p.X);
                    double gyMin = local.Min(p => p.Y);
                    double gyMax = local.Max(p => p.Y);
                    double dx = gxMax - gxMin;
                    double dy = gyMax - gyMin;

                    if (targetOrientation == "vertical" && dy >= dx)
                    {
                        double x = local.Average(p => p.X);
                        if (x > minX + 1e-4 && x < maxX - 1e-4)
                            values.Add(x);
                    }
                    else if (targetOrientation == "horizontal" && dx > dy)
                    {
                        double y = local.Average(p => p.Y);
                        if (y > minY + 1e-4 && y < maxY - 1e-4)
                            values.Add(y);
                    }
                }
            }
            catch
            {
                // Grid dimensions are optional; total dimensions still represent the curtain elevation.
            }

            return NormalizeCurtainElevationDimensionCoordinates(values);
        }

        private List<CurtainElevationGeometryReference> CollectCurtainElevationGeometryReferences(
            Document doc,
            Wall wall,
            View view,
            Transform frame,
            double minX,
            double maxX,
            double minY,
            double maxY,
            bool includeHostWall = false)
        {
            var references = new List<CurtainElevationGeometryReference>();
            if (doc == null || wall == null || view == null || frame == null)
                return references;

            var options = new Options
            {
                ComputeReferences = true,
                IncludeNonVisibleObjects = false
            };
            options.View = view;

            foreach (ElementId id in GetCurtainElevationElementIds(wall, includeHostWall))
            {
                Element element = doc.GetElement(id);
                if (element == null)
                    continue;

                try
                {
                    GeometryElement geometry = element.get_Geometry(options);
                    CollectCurtainElevationGeometryReferences(geometry, references, frame, Transform.Identity, element);
                }
                catch
                {
                    // Some curtain sub-elements do not expose reference-bearing geometry in elevation views.
                }
            }

            double tolerance = 5.0 / 304.8;
            return references
                .Where(r => r.Reference != null)
                .Where(r => r.Length > tolerance)
                .Where(r => r.MaxX >= minX - tolerance && r.MinX <= maxX + tolerance)
                .Where(r => r.MaxY >= minY - tolerance && r.MinY <= maxY + tolerance)
                .GroupBy(r => $"{r.ElementId.GetIdValue()}|{Math.Round(r.CenterX / tolerance)}|{Math.Round(r.CenterY / tolerance)}|{r.IsVertical}|{r.IsHorizontal}")
                .Select(g => g.OrderByDescending(r => r.Length).First())
                .ToList();
        }

        private void CollectCurtainElevationGeometryReferences(
            GeometryElement geometry,
            List<CurtainElevationGeometryReference> references,
            Transform viewFrame,
            Transform geometryTransform,
            Element sourceElement)
        {
            if (geometry == null || references == null || viewFrame == null || sourceElement == null)
                return;

            foreach (GeometryObject obj in geometry)
            {
                if (obj == null)
                    continue;

                if (obj is GeometryInstance instance)
                {
                    try
                    {
                        Transform nextTransform = geometryTransform.Multiply(instance.Transform);
                        CollectCurtainElevationGeometryReferences(instance.GetSymbolGeometry(), references, viewFrame, nextTransform, sourceElement);
                    }
                    catch
                    {
                        try
                        {
                            CollectCurtainElevationGeometryReferences(instance.GetInstanceGeometry(), references, viewFrame, geometryTransform, sourceElement);
                        }
                        catch
                        {
                            // Ignore geometry instance extraction failures.
                        }
                    }
                    continue;
                }

                if (obj is Curve curve)
                {
                    AddCurtainElevationGeometryReference(curve.Reference, curve, references, viewFrame, geometryTransform, sourceElement);
                    continue;
                }

                if (obj is Solid solid && solid.Edges != null)
                {
                    foreach (Edge edge in solid.Edges)
                    {
                        try
                        {
                            AddCurtainElevationGeometryReference(edge.Reference, edge.AsCurve(), references, viewFrame, geometryTransform, sourceElement);
                        }
                        catch
                        {
                            // Ignore malformed edge references.
                        }
                    }

                    foreach (Face face in solid.Faces)
                    {
                        if (face is PlanarFace planarFace)
                        {
                            AddCurtainElevationPlanarFaceReference(
                                planarFace, references, viewFrame, geometryTransform, sourceElement);
                        }
                    }
                }
            }
        }

        private void AddCurtainElevationPlanarFaceReference(
            PlanarFace face,
            List<CurtainElevationGeometryReference> references,
            Transform viewFrame,
            Transform geometryTransform,
            Element sourceElement)
        {
            if (face?.Reference == null || references == null || viewFrame == null || sourceElement == null)
                return;

            try
            {
                XYZ worldNormal = geometryTransform.OfVector(face.FaceNormal);
                XYZ localNormal = viewFrame.Inverse.OfVector(worldNormal).Normalize();
                if (Math.Abs(localNormal.X) < 0.999)
                    return;

                Mesh mesh = face.Triangulate();
                if (mesh == null || mesh.Vertices == null || mesh.Vertices.Count == 0)
                    return;

                List<XYZ> localVertices = mesh.Vertices
                    .Select(vertex => viewFrame.Inverse.OfPoint(geometryTransform.OfPoint(vertex)))
                    .ToList();
                double centerX = localVertices.Average(point => point.X);
                double minY = localVertices.Min(point => point.Y);
                double maxY = localVertices.Max(point => point.Y);
                double length = maxY - minY;
                if (length <= 1e-6)
                    return;

                references.Add(new CurtainElevationGeometryReference
                {
                    Reference = face.Reference,
                    ElementId = sourceElement.Id,
                    CategoryName = sourceElement.Category?.Name,
                    Start = viewFrame.OfPoint(new XYZ(centerX, minY, 0)),
                    End = viewFrame.OfPoint(new XYZ(centerX, maxY, 0)),
                    MinX = centerX,
                    MaxX = centerX,
                    MinY = minY,
                    MaxY = maxY,
                    Length = length,
                    IsVertical = true,
                    IsHorizontal = false,
                    GeometryObjectType = "planar_face"
                });
            }
            catch
            {
                // Host curtain walls do not always expose stable end-face references.
            }
        }

        private void AddCurtainElevationGeometryReference(
            Reference reference,
            Curve curve,
            List<CurtainElevationGeometryReference> references,
            Transform viewFrame,
            Transform geometryTransform,
            Element sourceElement)
        {
            if (reference == null || curve == null || references == null || viewFrame == null || sourceElement == null || !curve.IsBound)
                return;

            try
            {
                XYZ start = geometryTransform.OfPoint(curve.GetEndPoint(0));
                XYZ end = geometryTransform.OfPoint(curve.GetEndPoint(1));
                XYZ localStart = viewFrame.Inverse.OfPoint(start);
                XYZ localEnd = viewFrame.Inverse.OfPoint(end);
                double dx = Math.Abs(localEnd.X - localStart.X);
                double dy = Math.Abs(localEnd.Y - localStart.Y);
                double tolerance = 3.0 / 304.8;
                bool isVertical = dx <= tolerance && dy > tolerance;
                bool isHorizontal = dy <= tolerance && dx > tolerance;
                if (!isVertical && !isHorizontal)
                    return;

                references.Add(new CurtainElevationGeometryReference
                {
                    Reference = reference,
                    ElementId = sourceElement.Id,
                    CategoryName = sourceElement.Category?.Name,
                    Start = start,
                    End = end,
                    MinX = Math.Min(localStart.X, localEnd.X),
                    MaxX = Math.Max(localStart.X, localEnd.X),
                    MinY = Math.Min(localStart.Y, localEnd.Y),
                    MaxY = Math.Max(localStart.Y, localEnd.Y),
                    Length = Math.Sqrt(dx * dx + dy * dy),
                    IsVertical = isVertical,
                    IsHorizontal = isHorizontal
                });
            }
            catch
            {
                // Reference classification is best effort; invalid curves are ignored.
            }
        }

        private List<CurtainElevationGeometryReference> SelectCurtainElevationBoundaryReferences(
            List<CurtainElevationGeometryReference> references,
            string dimensionAxis,
            double minX,
            double maxX,
            double minY,
            double maxY,
            double toleranceFt = -1)
        {
            double tolerance = toleranceFt > 0 ? toleranceFt : 25.0 / 304.8;
            if (dimensionAxis == "horizontal")
            {
                List<CurtainElevationGeometryReference> verticals = references.Where(r => r.IsVertical).ToList();
                CurtainElevationGeometryReference left = verticals
                    .Where(r => Math.Abs(r.CenterX - minX) <= tolerance)
                    .OrderBy(r => Math.Abs(r.CenterX - minX))
                    .ThenByDescending(r => r.Length)
                    .FirstOrDefault();
                CurtainElevationGeometryReference right = verticals
                    .Where(r => Math.Abs(r.CenterX - maxX) <= tolerance)
                    .OrderBy(r => Math.Abs(r.CenterX - maxX))
                    .ThenByDescending(r => r.Length)
                    .FirstOrDefault();
                return left != null && right != null ? new List<CurtainElevationGeometryReference> { left, right } : new List<CurtainElevationGeometryReference>();
            }

            List<CurtainElevationGeometryReference> horizontals = references.Where(r => r.IsHorizontal).ToList();
            CurtainElevationGeometryReference bottom = horizontals
                .Where(r => Math.Abs(r.CenterY - minY) <= tolerance)
                .OrderBy(r => Math.Abs(r.CenterY - minY))
                .ThenByDescending(r => r.Length)
                .FirstOrDefault();
            CurtainElevationGeometryReference top = horizontals
                .Where(r => Math.Abs(r.CenterY - maxY) <= tolerance)
                .OrderBy(r => Math.Abs(r.CenterY - maxY))
                .ThenByDescending(r => r.Length)
                .FirstOrDefault();
            return bottom != null && top != null ? new List<CurtainElevationGeometryReference> { bottom, top } : new List<CurtainElevationGeometryReference>();
        }

        private List<CurtainElevationGeometryReference> SelectCurtainElevationGridDimensionReferences(
            List<CurtainElevationGeometryReference> boundaryReferences,
            List<CurtainElevationGeometryReference> gridLineReferences,
            string dimensionAxis,
            List<double> coordinates)
        {
            var result = new List<CurtainElevationGeometryReference>();
            List<double> distinct = NormalizeCurtainElevationDimensionCoordinates(coordinates);
            if (distinct.Count == 0)
                return result;

            double tolerance = 10.0 / 304.8;
            double boundaryTolerance = 1.0 / 304.8;
            double minCoordinate = distinct.First();
            double maxCoordinate = distinct.Last();

            foreach (double coordinate in distinct)
            {
                bool isBoundary = Math.Abs(coordinate - minCoordinate) <= tolerance || Math.Abs(coordinate - maxCoordinate) <= tolerance;
                List<CurtainElevationGeometryReference> candidates;
                if (isBoundary)
                {
                    candidates = dimensionAxis == "horizontal"
                        ? boundaryReferences.Where(r => r.IsVertical).ToList()
                        : boundaryReferences.Where(r => r.IsHorizontal).ToList();
                }
                else
                {
                    candidates = dimensionAxis == "horizontal"
                        ? gridLineReferences.Where(r => r.IsVertical).ToList()
                        : gridLineReferences.Where(r => r.IsHorizontal).ToList();
                }

                double matchTolerance = isBoundary && dimensionAxis == "horizontal" ? boundaryTolerance : tolerance;
                CurtainElevationGeometryReference match = candidates
                    .Where(r => Math.Abs((dimensionAxis == "horizontal" ? r.CenterX : r.CenterY) - coordinate) <= matchTolerance)
                    .OrderBy(r => Math.Abs((dimensionAxis == "horizontal" ? r.CenterX : r.CenterY) - coordinate))
                    .ThenByDescending(r => r.Length)
                    .FirstOrDefault();

                if (match == null || result.Any(r => r.Reference == match.Reference))
                    return new List<CurtainElevationGeometryReference>();

                result.Add(match);
            }

            return result;
        }

        private List<CurtainElevationGeometryReference> CollectCurtainElevationGridLineReferences(
            Document doc,
            Wall wall,
            View view,
            Transform frame,
            double minX,
            double maxX,
            double minY,
            double maxY,
            List<string> failures,
            List<object> diagnostics,
            int? preferredReferencePriority = null)
        {
            var selected = new List<CurtainElevationGeometryReference>();
            CurtainGrid grid = wall?.CurtainGrid;
            if (doc == null || grid == null || frame == null)
                return selected;

            // CurtainGridLine references must come from the element geometry without binding
            // extraction to the target elevation view's visibility/crop state.
            var options = new Options
            {
                ComputeReferences = true,
                IncludeNonVisibleObjects = true,
                DetailLevel = ViewDetailLevel.Fine
            };

            var gridIds = new List<ElementId>();
            gridIds.AddRange(grid.GetUGridLineIds());
            gridIds.AddRange(grid.GetVGridLineIds());
            double tolerance = 5.0 / 304.8;

            foreach (ElementId id in gridIds.Distinct())
            {
                CurtainGridLine gridLine = doc.GetElement(id) as CurtainGridLine;
                if (gridLine == null)
                    continue;

                try
                {
                    Curve fullCurve = gridLine.FullCurve;
                    if (fullCurve == null || !fullCurve.IsBound)
                        continue;

                    XYZ fullStart = fullCurve.GetEndPoint(0);
                    XYZ fullEnd = fullCurve.GetEndPoint(1);
                    XYZ fullLocalStart = frame.Inverse.OfPoint(fullStart);
                    XYZ fullLocalEnd = frame.Inverse.OfPoint(fullEnd);
                    XYZ fullDirection = fullLocalEnd - fullLocalStart;
                    if (fullDirection.GetLength() < tolerance)
                        continue;
                    fullDirection = fullDirection.Normalize();
                    bool gridIsVertical = Math.Abs(fullDirection.Y) >= Math.Abs(fullDirection.X);
                    double fullCoordinate = gridIsVertical
                        ? (fullLocalStart.X + fullLocalEnd.X) / 2.0
                        : (fullLocalStart.Y + fullLocalEnd.Y) / 2.0;
                    double fullRangeMin = gridIsVertical
                        ? Math.Min(fullLocalStart.Y, fullLocalEnd.Y)
                        : Math.Min(fullLocalStart.X, fullLocalEnd.X);
                    double fullRangeMax = gridIsVertical
                        ? Math.Max(fullLocalStart.Y, fullLocalEnd.Y)
                        : Math.Max(fullLocalStart.X, fullLocalEnd.X);

                    var candidates = new List<CurtainElevationGeometryReference>();
                    GeometryElement geometry = gridLine.get_Geometry(options);
                    CollectCurtainElevationGridGeometryReferences(
                        geometry,
                        candidates,
                        frame,
                        Transform.Identity,
                        gridLine);

                    try
                    {
                        CurveArray segmentCurves = gridLine.AllSegmentCurves;
                        if (segmentCurves != null)
                        {
                            foreach (Curve segment in segmentCurves)
                            {
                                int countBefore = candidates.Count;
                                Reference segmentReference = segment?.Reference;
                                AddCurtainElevationGeometryReference(segmentReference, segment, candidates, frame, Transform.Identity, gridLine);
                                if (candidates.Count <= countBefore)
                                    continue;

                                CurtainElevationGeometryReference added = candidates[candidates.Count - 1];
                                added.ReferenceSource = "curtain_grid_curve_reference";
                                added.ReferencePriority = 2;
                                added.GeometryObjectType = "segment_curve";
                            }
                        }

                        int fullCurveCountBefore = candidates.Count;
                        Reference fullCurveReference = fullCurve.Reference;
                        AddCurtainElevationGeometryReference(fullCurveReference, fullCurve, candidates, frame, Transform.Identity, gridLine);
                        if (candidates.Count > fullCurveCountBefore)
                        {
                            CurtainElevationGeometryReference fullCurveCandidate = candidates[candidates.Count - 1];
                            fullCurveCandidate.ReferenceSource = "curtain_grid_curve_reference";
                            fullCurveCandidate.ReferencePriority = 3;
                            fullCurveCandidate.GeometryObjectType = "full_curve";
                        }
                    }
                    catch
                    {
                        // Some Revit versions expose FullCurve but not its Reference.
                    }

                    foreach (CurtainElevationGeometryReference candidate in candidates)
                    {
                        candidate.CurtainGridLineId = id;
                        try
                        {
                            candidate.StableRepresentation = candidate.Reference?.ConvertToStableRepresentation(doc);
                        }
                        catch
                        {
                            candidate.StableRepresentation = null;
                        }
                    }

                    var evaluations = candidates.Select(candidate =>
                    {
                        XYZ direction = frame.Inverse.OfVector(candidate.End - candidate.Start);
                        bool isAligned = direction.GetLength() >= tolerance &&
                            Math.Abs(direction.Normalize().DotProduct(fullDirection)) >= 0.98;
                        double candidateCoordinate = gridIsVertical ? candidate.CenterX : candidate.CenterY;
                        bool positionMatches = Math.Abs(candidateCoordinate - fullCoordinate) <= tolerance;
                        double candidateRangeMin = gridIsVertical ? candidate.MinY : candidate.MinX;
                        double candidateRangeMax = gridIsVertical ? candidate.MaxY : candidate.MaxX;
                        bool coversGridRange =
                            candidateRangeMin <= fullRangeMin + tolerance &&
                            candidateRangeMax >= fullRangeMax - tolerance;
                        bool intersectsCurtainBounds =
                            candidate.MaxX >= minX - tolerance &&
                            candidate.MinX <= maxX + tolerance &&
                            candidate.MaxY >= minY - tolerance &&
                            candidate.MinY <= maxY + tolerance;
                        bool usable =
                            candidate.Reference != null &&
                            candidate.Length > tolerance &&
                            !string.IsNullOrWhiteSpace(candidate.StableRepresentation) &&
                            isAligned &&
                            positionMatches &&
                            coversGridRange &&
                            intersectsCurtainBounds;

                        return new
                        {
                            Candidate = candidate,
                            IsAligned = isAligned,
                            PositionMatches = positionMatches,
                            CoversGridRange = coversGridRange,
                            IntersectsCurtainBounds = intersectsCurtainBounds,
                            Usable = usable
                        };
                    }).ToList();

                    CurtainElevationGeometryReference best = evaluations
                        .Where(evaluation => evaluation.Usable)
                        .Where(evaluation => !preferredReferencePriority.HasValue ||
                            evaluation.Candidate.ReferencePriority == preferredReferencePriority.Value)
                        .OrderBy(evaluation => evaluation.Candidate.ReferencePriority)
                        .ThenByDescending(evaluation => evaluation.Candidate.Length)
                        .Select(evaluation => evaluation.Candidate)
                        .FirstOrDefault();
                    if (best == null)
                    {
                        string profileSuffix = preferredReferencePriority.HasValue
                            ? $" for reference priority profile {preferredReferencePriority.Value}"
                            : string.Empty;
                        failures?.Add($"CurtainGridLine {id.GetIdValue()} exposed {candidates.Count} candidate references, but none passed stable-reference, alignment, position, and range validation{profileSuffix}.");
                    }
                    else
                    {
                        best.SelectedForDimension = true;
                        best.SelectionReason = $"priority_{best.ReferencePriority}_stable_aligned_position_matched_full_range";
                        selected.Add(best);
                    }

                    foreach (var evaluation in evaluations)
                    {
                        CurtainElevationGeometryReference candidate = evaluation.Candidate;
                        bool isSelected = candidate == best;
                        diagnostics?.Add(new CurtainGridLineReferenceDiagnostic
                        {
                            GridLineId = id.GetIdValue(),
                            ProjectedDirection = candidate.IsVertical
                                ? "vertical"
                                : (candidate.IsHorizontal ? "horizontal" : "other"),
                            ReferenceSource = candidate.ReferenceSource,
                            GeometryObjectType = candidate.GeometryObjectType,
                            ReferenceAvailable = candidate.Reference != null,
                            StableRepresentation = candidate.StableRepresentation,
                            ProjectedCoordinateMm = Math.Round((gridIsVertical ? candidate.CenterX : candidate.CenterY) * 304.8, 1),
                            LengthMm = Math.Round(candidate.Length * 304.8, 1),
                            ReferencePriority = candidate.ReferencePriority,
                            IsAligned = evaluation.IsAligned,
                            PositionMatches = evaluation.PositionMatches,
                            CoversGridRange = evaluation.CoversGridRange,
                            SelectedForDimension = isSelected,
                            SelectionReason = isSelected
                                ? best.SelectionReason
                                : BuildCurtainGridReferenceRejectionReason(
                                    candidate,
                                    evaluation.IsAligned,
                                    evaluation.PositionMatches,
                                    evaluation.CoversGridRange,
                                    evaluation.IntersectsCurtainBounds)
                        });
                    }
                }
                catch (Exception ex)
                {
                    failures?.Add($"CurtainGridLine {id.GetIdValue()} geometry extraction failed: {ex.Message}");
                }
            }

            return selected
                .GroupBy(r => r.CurtainGridLineId ?? r.ElementId)
                .Select(g => g.OrderByDescending(r => r.Length).First())
                .ToList();
        }

        private void CollectCurtainElevationGridGeometryReferences(
            GeometryElement geometry,
            List<CurtainElevationGeometryReference> references,
            Transform viewFrame,
            Transform geometryTransform,
            CurtainGridLine gridLine)
        {
            if (geometry == null || references == null || viewFrame == null || gridLine == null)
                return;

            foreach (GeometryObject obj in geometry)
            {
                if (obj == null)
                    continue;

                if (obj is GeometryInstance instance)
                {
                    try
                    {
                        Transform nextTransform = geometryTransform.Multiply(instance.Transform);
                        CollectCurtainElevationGridGeometryReferences(
                            instance.GetSymbolGeometry(),
                            references,
                            viewFrame,
                            nextTransform,
                            gridLine);
                    }
                    catch
                    {
                        try
                        {
                            CollectCurtainElevationGridGeometryReferences(
                                instance.GetInstanceGeometry(),
                                references,
                                viewFrame,
                                geometryTransform,
                                gridLine);
                        }
                        catch
                        {
                            // Ignore geometry instance extraction failures.
                        }
                    }

                    continue;
                }

                if (!(obj is Curve curve))
                    continue;

                int countBefore = references.Count;
                AddCurtainElevationGeometryReference(
                    curve.Reference,
                    curve,
                    references,
                    viewFrame,
                    geometryTransform,
                    gridLine);
                if (references.Count <= countBefore)
                    continue;

                CurtainElevationGeometryReference added = references[references.Count - 1];
                bool isInternalLine = curve is Line;
                added.ReferenceSource = isInternalLine
                    ? "curtain_grid_internal_geometry_reference"
                    : "curtain_grid_curve_reference";
                added.ReferencePriority = isInternalLine ? 0 : 1;
                added.GeometryObjectType = isInternalLine
                    ? "internal_line"
                    : $"internal_{curve.GetType().Name}";
            }
        }

        private string BuildCurtainGridReferenceRejectionReason(
            CurtainElevationGeometryReference candidate,
            bool isAligned,
            bool positionMatches,
            bool coversGridRange,
            bool intersectsCurtainBounds)
        {
            var reasons = new List<string>();
            if (candidate?.Reference == null)
                reasons.Add("reference_unavailable");
            if (candidate == null || string.IsNullOrWhiteSpace(candidate.StableRepresentation))
                reasons.Add("stable_representation_unavailable");
            if (!isAligned)
                reasons.Add("not_aligned");
            if (!positionMatches)
                reasons.Add("position_mismatch");
            if (!coversGridRange)
                reasons.Add("does_not_cover_full_grid_range");
            if (!intersectsCurtainBounds)
                reasons.Add("outside_curtain_bounds");

            return reasons.Count > 0 ? string.Join(",", reasons) : "lower_priority_candidate";
        }

        private List<double> NormalizeCurtainElevationDimensionCoordinates(IEnumerable<double> coordinates)
        {
            const double tolerance = 1.0 / 304.8;
            var result = new List<double>();
            foreach (double coordinate in coordinates.Where(c => !double.IsNaN(c) && !double.IsInfinity(c)).OrderBy(c => c))
            {
                if (result.Count == 0 || Math.Abs(result.Last() - coordinate) > tolerance)
                    result.Add(coordinate);
            }

            return result;
        }

        private XYZ CurtainElevationPointAt2D(Transform frame, double x, double y)
        {
            return frame.Origin + frame.BasisX * x + frame.BasisY * y;
        }

        private Transform GetCurtainElevationDimensionFrame(ViewSection view, Transform sourceFrame)
        {
            if (view == null || sourceFrame == null)
                return sourceFrame;

            Transform frame = Transform.Identity;
            frame.Origin = view.Origin ?? sourceFrame.Origin;
            frame.BasisX = NormalizeOrFallback(view.RightDirection, sourceFrame.BasisX);
            frame.BasisY = NormalizeOrFallback(view.UpDirection, sourceFrame.BasisY);
            frame.BasisZ = NormalizeOrFallback(view.ViewDirection, sourceFrame.BasisZ);
            return frame;
        }

        private object BuildCurtainElevationDimensionTypePrompt(string selectionMode)
        {
            return new
            {
                Success = false,
                WorkflowState = "awaiting_dimension_type_selection",
                NextAction = "call_list_dimension_types",
                RequiresUserInput = true,
                NoModelChanges = true,
                ElevationsCreated = false,
                MissingFields = new[] { "dimensionTypeId" },
                DimensionTypeSelectionMode = selectionMode,
                PromptToUser = "Please call list_dimension_types and provide dimensionTypeId or dimensionTypeName.",
                Message = "Dimension type selection is required; no curtain elevation views were created."
            };
        }


        private string AppendCurtainElevationWarning(string current, string warning)
        {
            if (string.IsNullOrWhiteSpace(warning)) return current;
            return string.IsNullOrWhiteSpace(current) ? warning : current + " " + warning;
        }
        private class CurtainLevelReferenceInfo
        {
            public IdType? ElementId { get; set; }
            public string ReferenceSource { get; set; }
            public string ElementReferenceType { get; set; }
            public string StableRepresentation { get; set; }
            public bool StableRoundTripPassed { get; set; }
            public string Failure { get; set; }
        }
        private class CurtainLevelRuntimeAttempt
        {
            public string Name { get; set; }
            public string ViewState { get; set; }
            public string ReferenceStrategy { get; set; }
            public int ExpectedReferenceCount { get; set; }
            public List<CurtainLevelReferenceInfo> InputReferences { get; set; } = new List<CurtainLevelReferenceInfo>();
            public double LevelYmm { get; set; }
            public double CurtainBottomYmm { get; set; }
            public double CurtainTopYmm { get; set; }
            public double DimensionLineXmm { get; set; }
            public List<double> ExpectedSegmentValuesMm { get; set; } = new List<double>();
            public List<double> ActualSegmentValuesMm { get; set; } = new List<double>();
            public bool? SegmentValuesPassed { get; set; }
            public int? PreCommitReferenceCount { get; set; }
            public bool ReferenceAcquired { get; set; }
            public bool StableRoundTripPassed { get; set; }
            public bool DimensionCreated { get; set; }
            public bool TransactionCommitted { get; set; }
            public IdType? DimensionId { get; set; }
            public IdType? OwnerViewId { get; set; }
            public bool? PreCommitAreReferencesAvailable { get; set; }
            public bool? PostCommitAreReferencesAvailable { get; set; }
            public int? PostCommitReferenceCount { get; set; }
            public int? NumberOfSegments { get; set; }
            public IdType? HelperCurveId { get; set; }
            public IdType? HelperOwnerViewId { get; set; }
            public IdType? HelperLineStyleId { get; set; }
            public bool? HelperUsesInvisibleLines { get; set; }
            public string FailureStage { get; set; }
            public string PostCommitValidationMode { get; set; }
            public string ExceptionType { get; set; }
            public string ExceptionMessage { get; set; }
            public string ExceptionStackTrace { get; set; }
            public bool Passed { get; set; }
        }
        private class CurtainLevelProductionAttempt
        {
            public string ViewState { get; set; }
            public string FailureStage { get; set; }
            public string ExceptionType { get; set; }
            public string ExceptionMessage { get; set; }
            public string ExceptionStackTrace { get; set; }
            public string Status { get; set; }
            public string LevelOffsetDimensionMode { get; set; }
            public string LevelOffsetDimensionStatus { get; set; }
            public string LevelOffsetDimensionReferenceSource { get; set; }
            public IdType? TotalHeightDimensionId { get; set; }
            public IdType? LevelOffsetDimensionElementId { get; set; }
            public bool? TotalHeightReferencesAvailable { get; set; }
            public bool? LevelOffsetReferencesAvailable { get; set; }
            public string PostCommitValidationMode { get; set; }
            public bool? SegmentValuesPassed { get; set; }
            public List<object> PostCommitDimensionValidation { get; set; } = new List<object>();
            public List<double> ExpectedSegmentValuesMm { get; set; } = new List<double>();
            public List<double> ActualTotalHeightSegmentValuesMm { get; set; } = new List<double>();
            public List<double> ActualLevelOffsetSegmentValuesMm { get; set; } = new List<double>();
            public List<string> Warnings { get; set; } = new List<string>();
            public bool Passed { get; set; }
        }
        private object DiagnoseCurtainWallElevationLevelOffsetRuntime(JObject parameters)
        {
            Document doc=_uiApp.ActiveUIDocument.Document;
            UIDocument uidoc=_uiApp.ActiveUIDocument;
            IdType? viewId=parameters["viewId"]?.Value<IdType?>();
            ViewSection view=viewId.HasValue?doc.GetElement(new ElementId(viewId.Value)) as ViewSection:uidoc.ActiveView as ViewSection;
            if(view==null||view.IsTemplate||view.ViewType!=ViewType.Elevation) throw new Exception("level_offset runtime test requires a valid elevation viewId or active elevation.");
            IdType? wallId=parameters["wallId"]?.Value<IdType?>();
            Wall wall=wallId.HasValue?doc.GetElement(new ElementId(wallId.Value)) as Wall:ResolveSingleSelectedCurtainWall(uidoc,doc);
            if(wall==null||wall.CurtainGrid==null) throw new Exception("level_offset runtime test requires wallId or exactly one selected curtain wall; first-wall fallback is disabled.");
            var warnings=new List<string>();
            CurtainElevationDimensionTypeResolution typeResolution=ResolveCurtainElevationDimensionType(doc,parameters,warnings);
            DimensionType dimensionType=typeResolution.DimensionType;
            if(dimensionType==null) throw new Exception("level_offset runtime test could not resolve a DimensionType.");
            double inner=(parameters["dimensionOffsetMm"]?.Value<double>()??300.0)/304.8;
            double stack=(parameters["dimensionStackOffsetMm"]?.Value<double>()??250.0)/304.8;
            CurtainElevationDimensionStackOffsetResolution offsets=ResolveCurtainElevationDimensionStackOffset(dimensionType,view.Scale,inner,stack);
            if(!string.IsNullOrWhiteSpace(offsets.Warning)) warnings.Add(offsets.Warning);
            View original=uidoc.ActiveView;
            var originalTabs=new HashSet<IdType>(uidoc.GetOpenUIViews().Select(x=>x.ViewId.GetIdValue()));
            var attempts=new List<CurtainLevelRuntimeAttempt>();
            var production=new List<CurtainLevelProductionAttempt>();
            var tempIds=new List<ElementId>();
            var failures=new List<string>();
            bool inactiveEstablished=original?.Id!=view.Id,activated=false;
            string inactiveFailure=null,activationFailure=null;
            double minX=0,maxX=0,minY=0,maxY=0,levelY=0,leftX=0;
            Transform frame=null;
            List<CurtainElevationGeometryReference> heightRefs=null;
            IdType? levelId=null; string levelName=null;
            using(TransactionGroup group=new TransactionGroup(doc,"Diagnose curtain Level offset dimensions (Rollback)"))
            {
                group.Start();
                try
                {
                    using(Transaction setup=TransactionHelper.Begin(doc,"Prepare curtain Level offset runtime test"))
                    {
                        setup.Start();
                        XYZ mid=(wall.Location as LocationCurve)?.Curve?.Evaluate(0.5,true);
                        CurtainElevationCropResult crop=ConfigureCurtainElevationCrop(doc,view,wall,mid,view.Origin,0,0,1200.0/304.8);
                        doc.Regenerate();
                        Transform source=GetCurtainElevationView2DFrame(view,view.CropBox?.Transform);
                        frame=GetCurtainElevationDimensionFrame(view,source);
                        if(frame==null||source==null||crop?.View2DMin==null||crop.View2DMax==null) throw new InvalidOperationException("Production crop or dimension frame was unavailable.");
                        XYZ delta=source.Origin-frame.Origin;
                        double xs=delta.DotProduct(frame.BasisX),ys=delta.DotProduct(frame.BasisY);
                        minX=(crop.WallBoundaryMinXFt??crop.View2DMin.X)+xs;
                        maxX=(crop.WallBoundaryMaxXFt??crop.View2DMax.X)+xs;
                        minY=(crop.CurtainGeometryMinYFt??crop.View2DMin.Y)+ys;
                        maxY=(crop.CurtainGeometryMaxYFt??crop.View2DMax.Y)+ys;
                        if(!crop.CropBottomLevelViewYFt.HasValue) throw new InvalidOperationException("Projected wall Level Y was unavailable.");
                        levelY=crop.CropBottomLevelViewYFt.Value+ys;
                        Level level=doc.GetElement(wall.LevelId) as Level;
                        if(level==null) throw new InvalidOperationException("wall.LevelId did not resolve to Level.");
                        levelId=level.Id.GetIdValue(); levelName=level.Name;
                        var geometry=CollectCurtainElevationGeometryReferences(doc,wall,view,frame,minX,maxX,minY,maxY);
                        heightRefs=SelectCurtainElevationBoundaryReferences(geometry,"vertical",minX,maxX,minY,maxY);
                        if(heightRefs.Count!=2) throw new InvalidOperationException($"Expected 2 curtain bottom/top references, got {heightRefs.Count}.");
                        leftX=minX-offsets.InnerOffsetFt-offsets.ResolvedOffsetFt;
                        setup.Commit();
                    }
                    if(uidoc.ActiveView?.Id==view.Id)
                    {
                        View alternate=ResolveCurtainElevationAlternateDiagnosticView(uidoc,doc,view);
                        if(alternate==null) inactiveFailure="No alternate graphical view was available.";
                        else try{uidoc.ActiveView=alternate;uidoc.RefreshActiveView();inactiveEstablished=uidoc.ActiveView?.Id!=view.Id;}catch(Exception ex){inactiveFailure=ex.Message;}
                    }
                    if(inactiveEstablished) RunCurtainLevelRuntimeMatrix(doc,view,wall,frame,dimensionType,minX,maxX,minY,maxY,levelY,leftX,offsets.ResolvedOffsetFt,heightRefs,"inactive",attempts,production,tempIds);
                    try{uidoc.ActiveView=view;uidoc.RefreshActiveView();activated=uidoc.ActiveView?.Id==view.Id;if(!activated)activationFailure="ActiveView did not change.";}catch(Exception ex){activationFailure=ex.Message;}
                    if(activated) RunCurtainLevelRuntimeMatrix(doc,view,wall,frame,dimensionType,minX,maxX,minY,maxY,levelY,leftX,offsets.ResolvedOffsetFt,heightRefs,"active",attempts,production,tempIds);
                }
                catch(Exception ex){failures.Add(ex.ToString());}
                finally
                {
                    try{if(original!=null&&doc.GetElement(original.Id)!=null){uidoc.ActiveView=original;uidoc.RefreshActiveView();}}catch(Exception ex){failures.Add("Restore ActiveView failed: "+ex.Message);}
                    if(group.GetStatus()==TransactionStatus.Started) group.RollBack();
                }
            }
            bool cleanup=tempIds.Where(id=>id!=null&&id!=ElementId.InvalidElementId).Distinct().All(id=>doc.GetElement(id)==null);
            foreach(UIView tab in uidoc.GetOpenUIViews().ToList())
            {
                if(originalTabs.Contains(tab.ViewId.GetIdValue())||tab.ViewId==uidoc.ActiveView?.Id) continue;
                try{tab.Close();}catch(Exception ex){failures.Add("Close diagnostic tab failed: "+ex.Message);}
            }
            return new
            {
                TestMode="level_offset",WallId=wall.Id.GetIdValue(),ViewId=view.Id.GetIdValue(),ViewName=view.Name,
                LevelId=levelId,LevelName=levelName,LevelYmm=Math.Round(levelY*304.8,4),CurtainBottomYmm=Math.Round(minY*304.8,4),
                CurtainTopYmm=Math.Round(maxY*304.8,4),SignedBottomToLevelMm=Math.Round((minY-levelY)*304.8,4),
                DimensionTypeId=dimensionType.Id.GetIdValue(),DimensionTypeName=dimensionType.Name,DimensionTypeSource=typeResolution.Source,
                WasViewOpen=originalTabs.Contains(view.Id.GetIdValue()),WasViewActive=original?.Id==view.Id,
                InactiveControlEstablished=inactiveEstablished,InactiveControlFailure=inactiveFailure,ActivationSucceeded=activated,ActivationFailure=activationFailure,
                RuntimeAttempts=attempts,ProductionPathAttempts=production,FirstFailure=attempts.FirstOrDefault(x=>!x.Passed),
                FirstProductionFailure=production.FirstOrDefault(x=>!x.Passed),TemporaryElementIds=tempIds.Select(x=>x.GetIdValue()).Distinct().ToList(),
                FailureStage=failures.Count>0?"setup_or_view_control":attempts.FirstOrDefault(x=>!x.Passed)?.FailureStage??production.FirstOrDefault(x=>!x.Passed)?.FailureStage??(cleanup?null:"cleanup"),
                RollbackCleanupPassed=cleanup,CleanupFailureStage=cleanup?null:"cleanup",ForcedRollback=true,Warnings=warnings,Failures=failures
            };
        }
        private Wall ResolveSingleSelectedCurtainWall(UIDocument uidoc,Document doc)
        {
            List<Wall> walls=uidoc.Selection.GetElementIds().Select(id=>doc.GetElement(id) as Wall).Where(x=>x!=null).Where(x=>{try{return x.CurtainGrid!=null;}catch{return false;}}).ToList();
            return walls.Count==1?walls[0]:null;
        }
        private View ResolveCurtainElevationAlternateDiagnosticView(UIDocument uidoc,Document doc,View target)
        {
            View open=uidoc.GetOpenUIViews().Where(x=>x.ViewId!=target.Id).Select(x=>doc.GetElement(x.ViewId) as View).FirstOrDefault(x=>x!=null&&!x.IsTemplate);
            return open??new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>().FirstOrDefault(x=>!x.IsTemplate&&x.Id!=target.Id&&x.CanBePrinted);
        }
        private void RunCurtainLevelRuntimeMatrix(Document doc,ViewSection view,Wall wall,Transform frame,DimensionType type,double minX,double maxX,double minY,double maxY,double levelY,double lineX,double stackOffsetFt,List<CurtainElevationGeometryReference> refs,string state,List<CurtainLevelRuntimeAttempt> attempts,List<CurtainLevelProductionAttempt> production,List<ElementId> ids)
        {
            foreach(string strategy in new[]{"level_plane","invisible_detail_curve"})
            {
                attempts.Add(RunCurtainLevelRuntimeAttempt(doc,view,wall,frame,type,minX,maxX,minY,maxY,levelY,lineX,refs,state,strategy,false,ids));
                attempts.Add(RunCurtainLevelRuntimeAttempt(doc,view,wall,frame,type,minX,maxX,minY,maxY,levelY,lineX,refs,state,strategy,true,ids));
            }
            production.Add(RunCurtainLevelProductionAttempt(doc,view,wall,frame,type,minX,maxX,minY,maxY,levelY,lineX,stackOffsetFt,refs,state,ids));
        }

        private CurtainLevelRuntimeAttempt RunCurtainLevelRuntimeAttempt(Document doc,ViewSection view,Wall wall,Transform frame,DimensionType type,double minX,double maxX,double minY,double maxY,double levelY,double lineX,List<CurtainElevationGeometryReference> heightRefs,string viewState,string strategy,bool includeTop,List<ElementId> tempIds)
        {
            var attempt=new CurtainLevelRuntimeAttempt
            {
                Name=$"{strategy}_{(includeTop?"three_reference":"two_reference")}_{viewState}",ViewState=viewState,ReferenceStrategy=strategy,
                ExpectedReferenceCount=includeTop?3:2,LevelYmm=Math.Round(levelY*304.8,4),CurtainBottomYmm=Math.Round(minY*304.8,4),
                CurtainTopYmm=Math.Round(maxY*304.8,4),DimensionLineXmm=Math.Round(lineX*304.8,4),FailureStage="reference_acquisition"
            };
            Transaction tx=null;
            var aggregate=new CurtainElevationDimensionResult{WallId=wall.Id};
            try
            {
                tx=TransactionHelper.Begin(doc,"Runtime test curtain Level offset "+attempt.Name);tx.Start();
                CurtainElevationGeometryReference levelRef; string reason;
                if(strategy=="level_plane")
                {
                    if(!TryCreateCurtainElevationLevelPlaneReference(doc,doc.GetElement(wall.LevelId) as Level,minX,maxX,levelY,out levelRef,out reason)) throw new InvalidOperationException(reason);
                }
                else
                {
                    if(!TryCreateCurtainElevationInvisibleLevelReference(doc,view,frame,minX,maxX,levelY,aggregate,out levelRef,out reason)) throw new InvalidOperationException(reason);
                    attempt.HelperCurveId=levelRef.ElementId.GetIdValue();tempIds.Add(levelRef.ElementId);
                    DetailCurve helper=doc.GetElement(levelRef.ElementId) as DetailCurve;
                    attempt.HelperOwnerViewId=helper?.OwnerViewId.GetIdValue();attempt.HelperLineStyleId=helper?.LineStyle?.Id.GetIdValue();
                    GraphicsStyle invisible=TryFindCurtainElevationLevelInvisibleLineStyle(doc);
                    attempt.HelperUsesInvisibleLines=helper?.LineStyle!=null&&invisible!=null&&helper.LineStyle.Id.GetIdValue()==invisible.Id.GetIdValue();
                }
                attempt.ReferenceAcquired=true;
                var refs=new List<CurtainElevationGeometryReference>{levelRef,heightRefs[0]};if(includeTop)refs.Add(heightRefs[1]);refs=refs.OrderBy(x=>x.CenterY).ToList();
                var coordinates=refs.Select(x=>x.CenterY).ToList();
                attempt.InputReferences=refs.Select(x=>BuildCurtainLevelReferenceInfo(doc,x)).ToList();
                attempt.StableRoundTripPassed=attempt.InputReferences.All(x=>x.StableRoundTripPassed);
                if(!attempt.StableRoundTripPassed){attempt.FailureStage="stable_round_trip";throw new InvalidOperationException("Input reference stable round-trip failed.");}
                attempt.ExpectedSegmentValuesMm=coordinates.Zip(coordinates.Skip(1),(a,b)=>Math.Round(Math.Abs(b-a)*304.8,4)).ToList();
                attempt.FailureStage="creation";
                var array=new ReferenceArray();foreach(var item in refs)array.Append(item.Reference);
                Dimension dimension=doc.Create.NewDimension(view,Line.CreateBound(CurtainElevationPointAt2D(frame,lineX,coordinates.First()),CurtainElevationPointAt2D(frame,lineX,coordinates.Last())),array);
                if(dimension==null)throw new InvalidOperationException("Revit returned null Dimension.");
                ApplyDimensionType(dimension,type);doc.Regenerate();
                attempt.DimensionCreated=true;attempt.DimensionId=dimension.Id.GetIdValue();attempt.OwnerViewId=dimension.OwnerViewId.GetIdValue();tempIds.Add(dimension.Id);
                attempt.FailureStage="pre_commit";attempt.PreCommitAreReferencesAvailable=dimension.AreReferencesAvailable;attempt.PreCommitReferenceCount=dimension.References?.Size??0;
                attempt.FailureStage="commit";tx.Commit();attempt.TransactionCommitted=true;tx=null;
                attempt.FailureStage="post_commit";
                Dimension persisted=doc.GetElement(new ElementId(attempt.DimensionId.Value)) as Dimension;
                if(persisted==null)throw new InvalidOperationException("Dimension did not exist after commit.");
                attempt.PostCommitAreReferencesAvailable=persisted.AreReferencesAvailable;attempt.PostCommitReferenceCount=persisted.References?.Size??0;
                attempt.NumberOfSegments=persisted.NumberOfSegments;attempt.ActualSegmentValuesMm=GetCurtainElevationDimensionValuesMm(persisted);
                attempt.FailureStage="segment_validation";attempt.SegmentValuesPassed=CurtainLevelValuesMatch(attempt.ExpectedSegmentValuesMm,attempt.ActualSegmentValuesMm,0.5);
                bool levelPlaneFalseNegative=strategy=="level_plane"&&attempt.PostCommitAreReferencesAvailable!=true&&attempt.PostCommitReferenceCount==attempt.ExpectedReferenceCount&&attempt.SegmentValuesPassed==true;
                attempt.PostCommitValidationMode=attempt.PostCommitAreReferencesAvailable==true
                    ?"strict_references_available"
                    :(levelPlaneFalseNegative?"level_plane_segment_validation":"failed");
                bool referenceValidationPassed=attempt.PostCommitAreReferencesAvailable==true||levelPlaneFalseNegative;
                attempt.Passed=attempt.OwnerViewId==view.Id.GetIdValue()&&referenceValidationPassed&&attempt.PostCommitReferenceCount==attempt.ExpectedReferenceCount&&attempt.SegmentValuesPassed==true&&(strategy!="invisible_detail_curve"||attempt.HelperUsesInvisibleLines==true);
                attempt.FailureStage=attempt.Passed?null:"post_commit_assertion";
            }
            catch(Exception ex)
            {
                if(tx!=null&&tx.GetStatus()==TransactionStatus.Started)tx.RollBack();
                attempt.ExceptionType=ex.GetType().FullName;attempt.ExceptionMessage=ex.Message;attempt.ExceptionStackTrace=ex.StackTrace;attempt.Passed=false;
            }
            return attempt;
        }
        private CurtainLevelProductionAttempt RunCurtainLevelProductionAttempt(Document doc,ViewSection view,Wall wall,Transform frame,DimensionType type,double minX,double maxX,double minY,double maxY,double levelY,double lineX,double stackOffsetFt,List<CurtainElevationGeometryReference> heightRefs,string viewState,List<ElementId> tempIds)
        {
            var attempt=new CurtainLevelProductionAttempt{ViewState=viewState,FailureStage="production_creation"};
            var result=new CurtainElevationDimensionResult{WallId=wall.Id};
            try
            {
                using(Transaction tx=TransactionHelper.Begin(doc,"Runtime test production Level offset "+viewState))
                {
                    tx.Start();CreateCurtainElevationTotalHeightAndLevelOffsetDimensions(doc,view,wall,frame,type,minX,maxX,minY,maxY,levelY,lineX,stackOffsetFt,heightRefs,result);tx.Commit();
                }
                attempt.FailureStage="production_post_commit_repair";FinalizeCurtainElevationDimensionsAfterCommit(doc,new[]{result});
                foreach(ElementId id in new[]{result.TotalHeightDimensionId,result.LevelOffsetDimensionElementId}.Concat(result.ReferenceCurveIds).Where(x=>x!=null&&x!=ElementId.InvalidElementId).Distinct()){tempIds.Add(id);}
                attempt.Status=result.Status;attempt.LevelOffsetDimensionMode=result.LevelOffsetDimensionMode;attempt.LevelOffsetDimensionStatus=result.LevelOffsetDimensionStatus;
                attempt.LevelOffsetDimensionReferenceSource=result.LevelOffsetDimensionReferenceSource;attempt.TotalHeightDimensionId=result.TotalHeightDimensionId?.GetIdValue();
                attempt.LevelOffsetDimensionElementId=result.LevelOffsetDimensionElementId?.GetIdValue();attempt.TotalHeightReferencesAvailable=result.TotalHeightDimensionAreReferencesAvailable;
                attempt.LevelOffsetReferencesAvailable=result.LevelOffsetDimensionAreReferencesAvailable;attempt.Warnings=result.Warnings.ToList();
                CurtainElevationPendingDimension levelPending=result.PendingNativeDimensions.FirstOrDefault(IsCurtainElevationLevelOffsetPlanePending);
                attempt.PostCommitValidationMode=levelPending?.PostCommitValidationMode;
                attempt.SegmentValuesPassed=levelPending?.SegmentValuesPassed;
                attempt.PostCommitDimensionValidation=result.PostCommitDimensionValidation.ToList();
                Dimension total=result.TotalHeightDimensionId==null?null:doc.GetElement(result.TotalHeightDimensionId) as Dimension;
                Dimension offset=result.LevelOffsetDimensionElementId==null?null:doc.GetElement(result.LevelOffsetDimensionElementId) as Dimension;
                attempt.ActualTotalHeightSegmentValuesMm=GetCurtainElevationDimensionValuesMm(total);attempt.ActualLevelOffsetSegmentValuesMm=GetCurtainElevationDimensionValuesMm(offset);
                double signed=minY-levelY;
                if(Math.Abs(signed)<=1.0/304.8)
                {
                    attempt.ExpectedSegmentValuesMm=new List<double>{Math.Round((maxY-minY)*304.8,4)};
                    attempt.Passed=result.LevelOffsetDimensionStatus=="skipped_zero_distance"&&total!=null&&result.TotalHeightDimensionAreReferencesAvailable==true;
                }
                else if(signed>0)
                {
                    attempt.ExpectedSegmentValuesMm=new List<double>{Math.Round((minY-levelY)*304.8,4),Math.Round((maxY-minY)*304.8,4)};
                    attempt.Passed=total!=null&&result.LevelOffsetDimensionElementId!=null&&result.TotalHeightDimensionId.GetIdValue()==result.LevelOffsetDimensionElementId.GetIdValue()&&result.LevelOffsetDimensionReferenceSource=="wall_level_plane_reference"&&levelPending?.PostCommitValidationPassed==true&&CurtainLevelValuesMatch(attempt.ExpectedSegmentValuesMm,attempt.ActualTotalHeightSegmentValuesMm,0.5);
                }
                else
                {
                    attempt.ExpectedSegmentValuesMm=new List<double>{Math.Round(Math.Abs(minY-levelY)*304.8,4),Math.Round((maxY-minY)*304.8,4)};
                    attempt.Passed=total!=null&&offset!=null&&result.TotalHeightDimensionAreReferencesAvailable==true&&result.LevelOffsetDimensionReferenceSource=="wall_level_plane_reference"&&levelPending?.PostCommitValidationPassed==true&&CurtainLevelValuesMatch(new[]{attempt.ExpectedSegmentValuesMm[1]},attempt.ActualTotalHeightSegmentValuesMm,0.5)&&CurtainLevelValuesMatch(new[]{attempt.ExpectedSegmentValuesMm[0]},attempt.ActualLevelOffsetSegmentValuesMm,0.5);
                }
                attempt.FailureStage=attempt.Passed?null:"production_assertion";
            }
            catch(Exception ex){attempt.ExceptionType=ex.GetType().FullName;attempt.ExceptionMessage=ex.Message;attempt.ExceptionStackTrace=ex.StackTrace;attempt.Passed=false;}
            return attempt;
        }
        private CurtainLevelReferenceInfo BuildCurtainLevelReferenceInfo(Document doc,CurtainElevationGeometryReference reference)
        {
            var info=new CurtainLevelReferenceInfo{ElementId=reference.ElementId?.GetIdValue(),ReferenceSource=reference.ReferenceSource};
            try
            {
                info.ElementReferenceType=reference.Reference.ElementReferenceType.ToString();info.StableRepresentation=reference.Reference.ConvertToStableRepresentation(doc);
                Reference parsed=Reference.ParseFromStableRepresentation(doc,info.StableRepresentation);
                info.StableRoundTripPassed=parsed!=null&&parsed.ElementId!=null&&reference.ElementId!=null&&parsed.ElementId.GetIdValue()==reference.ElementId.GetIdValue();
            }
            catch(Exception ex){info.Failure=ex.Message;}
            return info;
        }
        private bool CurtainLevelValuesMatch(IList<double> expected,IList<double> actual,double toleranceMm)
        {
            return expected!=null&&actual!=null&&expected.Count==actual.Count&&expected.Zip(actual,(a,b)=>Math.Abs(a-b)<=toleranceMm).All(x=>x);
        }
    }
}
