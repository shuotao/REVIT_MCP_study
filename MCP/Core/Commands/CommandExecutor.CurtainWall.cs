using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;

// Revit 2025+ ElementId: int ??long
#if REVIT2025_OR_GREATER
using IdType = System.Int64;
#else
using IdType = System.Int32;
#endif

namespace RevitMCP.Core
{
    /// <summary>
    /// 帷�???+ 立面?�板?�令
    /// 來�?：PR#11 (@7alexhuang-ux)，�?跨�??�修�???��?
    /// </summary>
    public partial class CommandExecutor
    {
        private const double CurtainElevationDirectionDotThreshold = 0.98;
        private static IdType? LastCurtainElevationDimensionTypeId;

        #region 帷�??�工??
        private object GetCurtainWallInfo(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;
            UIDocument uidoc = _uiApp.ActiveUIDocument;

            IdType? elementId = parameters["elementId"]?.Value<IdType>();
            Wall wall = null;

            // 如�?沒�??��? elementId，使?�目?�選?��??��?
            if (elementId.HasValue)
            {
                Element elem = doc.GetElement(new ElementId(elementId.Value));
                wall = elem as Wall;
            }
            else
            {
                var selection = uidoc.Selection.GetElementIds();
                if (selection.Count == 0)
                    throw new Exception("請�??��?一?�帷幕�?，�??��? elementId");

                Element elem = doc.GetElement(selection.First());
                wall = elem as Wall;
            }

            if (wall == null)
                throw new Exception("?��??��?素�??��?");

            // 檢查?�否?�帷幕�?
            CurtainGrid grid = wall.CurtainGrid;
            if (grid == null)
                throw new Exception("Selected wall is not a curtain wall (CurtainGrid is null).");

            // ?��? Grid 資�?
            var uGridIds = grid.GetUGridLineIds();
            var vGridIds = grid.GetVGridLineIds();
            var panelIds = grid.GetPanelIds();

            // 計�? rows ??columns
            int rows = uGridIds.Count + 1;    // U Grid = 水平�?= 定義 Row
            int columns = vGridIds.Count + 1; // V Grid = ?�直�?= 定義 Column

            // ?��??�板資�?
            var panelTypeDict = new Dictionary<IdType, (string TypeName, string MaterialName, string MaterialColor, int Count)>();
            var panelMatrix = new List<List<int>>(); // [row][col] = typeId

            // ?��??��?位置線�?計�??��?
            LocationCurve locCurve = wall.Location as LocationCurve;
            Curve curve = locCurve?.Curve;

            // ?��??�板並�???
            foreach (ElementId panelId in panelIds)
            {
                Element panel = doc.GetElement(panelId);
                if (panel == null) continue;

                ElementId typeId = panel.GetTypeId();
                IdType typeIdInt = typeId.GetIdValue();

                if (!panelTypeDict.ContainsKey(typeIdInt))
                {
                    ElementType panelType = doc.GetElement(typeId) as ElementType;
                    string typeName = panelType?.Name ?? "Unknown";

                    // ?�試?��??��?資�?
                    string materialName = "";
                    string materialColor = "#808080";

                    try
                    {
                        Parameter matParam = panelType?.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM);
                        if (matParam != null && matParam.HasValue)
                        {
                            ElementId matId = matParam.AsElementId();
                            Material mat = doc.GetElement(matId) as Material;
                            if (mat != null)
                            {
                                materialName = mat.Name;
                                Color color = mat.Color;
                                if (color != null && color.IsValid)
                                {
                                    materialColor = $"#{color.Red:X2}{color.Green:X2}{color.Blue:X2}";
                                }
                            }
                        }
                    }
                    catch (Exception) { /* 忽略?�別?��??��?失�? */ }

                    panelTypeDict[typeIdInt] = (typeName, materialName, materialColor, 0);
                }

                var current = panelTypeDict[typeIdInt];
                panelTypeDict[typeIdInt] = (current.TypeName, current.MaterialName, current.MaterialColor, current.Count + 1);
            }

            // ?��??�板尺寸（�?第�??�面?�估算�?
            double panelWidth = 0;
            double panelHeight = 0;

            if (panelIds.Count > 0)
            {
                Element firstPanel = doc.GetElement(panelIds.First());
                BoundingBoxXYZ bb = firstPanel?.get_BoundingBox(null);
                if (bb != null)
                {
                    panelWidth = Math.Round((bb.Max.X - bb.Min.X) * 304.8, 2);
                    panelHeight = Math.Round((bb.Max.Z - bb.Min.Z) * 304.8, 2);
                }
            }

            // 組�??�傳資�?
            var panelTypes = panelTypeDict.Select(kvp => new
            {
                TypeId = kvp.Key,
                TypeName = kvp.Value.TypeName,
                MaterialName = kvp.Value.MaterialName,
                MaterialColor = kvp.Value.MaterialColor,
                Count = kvp.Value.Count
            }).ToList();

            return new
            {
                ElementId = wall.Id.GetIdValue(),
                WallType = wall.WallType.Name,
                IsCurtainWall = true,
                Rows = rows,
                Columns = columns,
                TotalPanels = panelIds.Count,
                PanelWidth = panelWidth,
                PanelHeight = panelHeight,
                UGridCount = uGridIds.Count,
                VGridCount = vGridIds.Count,
                PanelTypes = panelTypes,
                PanelIds = panelIds.Select(id => id.GetIdValue()).ToList()
            };
        }

        /// <summary>
        /// ?��?專�?中�??�可?��?帷�??�板類�?
        /// </summary>
        private object GetCurtainPanelTypes(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;

            // ?��??�??Curtain Panel 類�?
            var panelTypes = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_CurtainWallPanels)
                .WhereElementIsElementType()
                .Cast<ElementType>()
                .Select(pt =>
                {
                    string materialName = "";
                    string materialColor = "#808080";
                    int transparency = 0;

                    try
                    {
                        Parameter matParam = pt.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM);
                        if (matParam != null && matParam.HasValue)
                        {
                            ElementId matId = matParam.AsElementId();
                            Material mat = doc.GetElement(matId) as Material;
                            if (mat != null)
                            {
                                materialName = mat.Name;
                                Color color = mat.Color;
                                if (color != null && color.IsValid)
                                {
                                    materialColor = $"#{color.Red:X2}{color.Green:X2}{color.Blue:X2}";
                                }
                                transparency = mat.Transparency;
                            }
                        }
                    }
                    catch (Exception) { /* 忽略?�別?��??��?失�? */ }

                    return new
                    {
                        TypeId = pt.Id.GetIdValue(),
                        TypeName = pt.Name,
                        Family = (pt as FamilySymbol)?.FamilyName ?? "System Panel",
                        MaterialName = materialName,
                        MaterialColor = materialColor,
                        Transparency = transparency
                    };
                })
                .OrderBy(pt => pt.Family)
                .ThenBy(pt => pt.TypeName)
                .ToList();

            return new
            {
                Count = panelTypes.Count,
                PanelTypes = panelTypes
            };
        }

        /// <summary>
        /// 建�?每�??�帷幕�??��?立面視�?，並套用?�帷幕�??�」�??�樣?��?
        /// </summary>
        private object CreateCurtainWallElevations(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;
            UIDocument uidoc = _uiApp.ActiveUIDocument;

            int scale = parameters["scale"]?.Value<int>() ?? 50;
            double offsetFt = (parameters["offsetMm"]?.Value<double>() ?? 1500.0) / 304.8;
            double horizontalMarginFt = (parameters["horizontalMarginMm"]?.Value<double>() ?? 0.0) / 304.8;
            double verticalMarginFt = (parameters["verticalMarginMm"]?.Value<double>() ?? 0.0) / 304.8;
            double fallbackDepthFt = (parameters["depthMm"]?.Value<double>() ?? 1200.0) / 304.8;
            string viewTemplateName = parameters["viewTemplateName"]?.Value<string>() ?? "帷幕立面";
            string elevationViewTypeName = parameters["elevationViewTypeName"]?.Value<string>() ?? "帷幕立面";
            bool applyViewTemplate = parameters["applyViewTemplate"]?.Value<bool>() ?? true;
            string nameSeparator = parameters["nameSeparator"]?.Value<string>() ?? "-";
            bool dryRun = parameters["dryRun"]?.Value<bool>() ?? false;
            bool addDimensions = parameters["addDimensions"]?.Value<bool>() ?? true;
            string dimensionTypeSelectionMode = parameters["dimensionTypeSelectionMode"]?.Value<string>()?.Trim().ToLowerInvariant() ?? "auto";
            double dimensionOffsetFt = (parameters["dimensionOffsetMm"]?.Value<double>() ?? 300.0) / 304.8;
            double dimensionStackOffsetFt = (parameters["dimensionStackOffsetMm"]?.Value<double>() ?? 250.0) / 304.8;

            ViewFamilyType sourceElevationType = GetFirstCurtainElevationViewFamilyType(doc);
            ViewFamilyType elevationType = FindCurtainElevationViewFamilyType(doc, elevationViewTypeName) ?? sourceElevationType;

            if (sourceElevationType == null)
                throw new Exception("?��???Elevation ??ViewFamilyType");

            ViewPlan explicitPlacementView = ResolveCurtainElevationPlacementView(doc, parameters);
            Dictionary<ElementId, ViewPlan> floorPlansByLevel = GetCurtainElevationFloorPlansByLevel(doc);
            ViewPlan activePlan = uidoc.ActiveView as ViewPlan;
            if (activePlan != null && activePlan.IsTemplate)
                activePlan = null;

            var existingNames = new HashSet<string>(
                new FilteredElementCollector(doc)
                    .OfClass(typeof(View))
                    .Cast<View>()
                    .Select(v => v.Name));

            List<Wall> curtainWalls = new FilteredElementCollector(doc)
                .OfClass(typeof(Wall))
                .WhereElementIsNotElementType()
                .Cast<Wall>()
                .Where(w =>
                {
                    try { return w.CurtainGrid != null; }
                    catch { return false; }
                })
                .OrderBy(w => w.LevelId.GetIdValue())
                .ThenBy(w => w.Id.GetIdValue())
                .ToList();

            var created = new List<object>();
            var skipped = new List<object>();
            var templateWarnings = new List<string>();
            var dimensionWarnings = new List<string>();
            View viewTemplate = null;
            bool templateCreated = false;
            bool templateUpdated = false;
            bool elevationViewTypeCreated = false;
            CurtainElevationDimensionTypeResolution dimensionTypeResolution =
                ResolveCurtainElevationDimensionType(doc, parameters, dimensionWarnings);
            DimensionType dimensionType = addDimensions ? dimensionTypeResolution.DimensionType : null;
            int dimensionsCreatedCount = 0;
            int dimensionsFailedCount = 0;
            bool hasExplicitDimensionType =
                parameters["dimensionTypeId"] != null ||
                !string.IsNullOrWhiteSpace(parameters["dimensionTypeName"]?.Value<string>());

            if (addDimensions && dimensionTypeSelectionMode == "prompt" && !hasExplicitDimensionType)
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
                    DimensionTypeSelectionMode = dimensionTypeSelectionMode,
                    PromptToUser = "Call list_dimension_types and pass dimensionTypeId or dimensionTypeName, or set dimensionTypeSelectionMode to auto.",
                    Message = "Dimension type selection is required; no curtain elevation views were created."
                };
            }

            if (dryRun)
            {
                foreach (Wall wall in curtainWalls)
                {
                    Level level = doc.GetElement(wall.LevelId) as Level;
                    string levelName = level?.Name ?? "Unknown Level";
                    string mark = GetCurtainWallMark(wall);
                    string viewName = MakeUniqueCurtainElevationViewName(existingNames, $"{levelName}{nameSeparator}{mark}");
                    CurtainElevationExteriorResolution exterior = ResolveCurtainElevationExteriorSide(wall);

                    created.Add(new
                    {
                        WallId = wall.Id.GetIdValue(),
                        ViewId = (IdType)0,
                        ViewName = viewName,
                        ElevationViewTypeId = elevationType?.Id.GetIdValue() ?? 0,
                        ElevationViewTypeName = elevationType?.Name ?? elevationViewTypeName,
                        LevelName = levelName,
                        Mark = mark,
                        MarkerId = (IdType)0,
                        WallFlipped = wall.Flipped,
                        ResolvedExteriorSide = exterior?.SideName,
                        ResolvedExteriorSource = exterior?.Source,
                        ResolvedExteriorDirection = ToCurtainElevationXyz(exterior?.ExteriorDirection),
                        AddDimensions = addDimensions,
                        DimensionTypeSelectionMode = dimensionTypeSelectionMode,
                        DimensionTypeId = dimensionType?.Id.GetIdValue(),
                        DimensionTypeName = dimensionType?.Name,
                        DimensionTypeSource = dimensionTypeResolution.Source,
                        DimensionStatus = addDimensions ? "dry_run" : "disabled",
                        IsPersistentOutput = true,
                        DryRun = true
                    });
                }

                return new
                {
                    Success = true,
                    DryRun = true,
                    PersistentViewsCreated = true,
                    CleanupRequired = false,
                    DeleteGeneratedViews = false,
                    TotalCurtainWalls = curtainWalls.Count,
                    CreatedCount = created.Count,
                    SkippedCount = skipped.Count,
                    ViewTemplateId = (IdType)0,
                    ViewTemplateName = viewTemplateName,
                    TemplateCreated = false,
                    TemplateUpdated = false,
                    ElevationViewTypeId = elevationType?.Id.GetIdValue() ?? 0,
                    ElevationViewTypeName = elevationType?.Name ?? elevationViewTypeName,
                    ElevationViewTypeCreated = false,
                    ElevationViewTypeWillBeCreated = FindCurtainElevationViewFamilyType(doc, elevationViewTypeName) == null,
                    AddDimensions = addDimensions,
                    DimensionTypeSelectionMode = dimensionTypeSelectionMode,
                    DimensionTypeId = dimensionType?.Id.GetIdValue(),
                    DimensionTypeName = dimensionType?.Name,
                    DimensionTypeSource = dimensionTypeResolution.Source,
                    DimensionsCreatedCount = 0,
                    DimensionsFailedCount = 0,
                    DimensionWarnings = dimensionWarnings,
                    Created = created,
                    Skipped = skipped,
                    TemplateWarnings = templateWarnings
                };
            }

            using (Transaction trans = TransactionHelper.Begin(doc, "Create curtain wall elevations"))
            {
                trans.Start();
                elevationType = GetOrCreateCurtainElevationViewFamilyType(
                    doc,
                    sourceElevationType,
                    elevationViewTypeName,
                    out elevationViewTypeCreated,
                    templateWarnings);

                
                if (applyViewTemplate)
                {
                    viewTemplate = FindCurtainElevationViewTemplate(doc, viewTemplateName);
                    if (viewTemplate != null)
                    {
                        ConfigureCurtainElevationViewTemplate(doc, viewTemplate, templateWarnings);
                        templateUpdated = true;
                    }
                }

                foreach (Wall wall in curtainWalls)
                {
                    Level level = doc.GetElement(wall.LevelId) as Level;
                    string levelName = level?.Name ?? "Unknown Level";
                    string mark = GetCurtainWallMark(wall);

                    try
                    {
                        LocationCurve loc = wall.Location as LocationCurve;
                        if (loc == null)
                        {
                            skipped.Add(new { WallId = wall.Id.GetIdValue(), LevelName = levelName, Mark = mark, Reason = "?��???LocationCurve" });
                            continue;
                        }

                        BoundingBoxXYZ wallBox = wall.get_BoundingBox(null);
                        if (wallBox == null)
                        {
                            skipped.Add(new { WallId = wall.Id.GetIdValue(), LevelName = levelName, Mark = mark, Reason = "?��??��???BoundingBox" });
                            continue;
                        }

                        ViewPlan placementView = explicitPlacementView
                            ?? ResolveCurtainElevationPlanForWall(wall, activePlan, floorPlansByLevel);
                        if (placementView == null)
                        {
                            skipped.Add(new { WallId = wall.Id.GetIdValue(), LevelName = levelName, Mark = mark, Reason = "No placement ViewPlan available for ElevationMarker" });
                            continue;
                        }

                        XYZ wallMid = loc.Curve.Evaluate(0.5, true);
                        CurtainElevationExteriorResolution exterior = ResolveCurtainElevationExteriorSide(wall);
                        XYZ outward = exterior?.ExteriorDirection;
                        if (outward == null)
                        {
                            skipped.Add(new { WallId = wall.Id.GetIdValue(), LevelName = levelName, Mark = mark, Reason = "?��??�斷 wall.Orientation" });
                            continue;
                        }

                        XYZ markerPoint = wallMid + outward * (wall.Width / 2.0 + offsetFt);
                        string viewName = MakeUniqueCurtainElevationViewName(existingNames, $"{levelName}{nameSeparator}{mark}");

                        ElevationMarker marker = ElevationMarker.CreateElevationMarker(doc, elevationType.Id, markerPoint, scale);
                        ViewSection elevationView = marker.CreateElevation(doc, placementView.Id, 0);
                        doc.Regenerate();

                        XYZ desiredLookDirection = GetCurtainElevationDesiredLookDirection(wallMid, markerPoint);
                        CurtainElevationDirectionResult directionResult = AlignCurtainElevationMarkerByVisualLook(
                            doc, marker, markerPoint, elevationView, desiredLookDirection);
                        if (directionResult.DirectionDot < CurtainElevationDirectionDotThreshold)
                        {
                            DeleteCurtainElevationMarkerAndView(doc, marker, elevationView);
                            skipped.Add(new
                            {
                                WallId = wall.Id.GetIdValue(),
                                LevelName = levelName,
                                Mark = mark,
                                Reason = $"立面?��?驗�?失�?，DirectionDot={directionResult.DirectionDot:F4}",
                                DirectionDot = Math.Round(directionResult.DirectionDot, 4),
                                DirectionFixApplied = directionResult.DirectionFixApplied,
                                DesiredLookDirection = ToCurtainElevationXyz(directionResult.DesiredLookDirection),
                                FinalVisualLookDirection = ToCurtainElevationXyz(directionResult.FinalVisualLookDirection),
                                WallOrientation = ToCurtainElevationXyz(FlattenAndNormalize(wall.Orientation)),
                                WallFlipped = wall.Flipped,
                                ResolvedExteriorSide = exterior.SideName,
                                ResolvedExteriorSource = exterior.Source,
                                ResolvedExteriorDirection = ToCurtainElevationXyz(outward),
                                MarkerPoint = ToCurtainElevationXyz(markerPoint),
                                WallMidPoint = ToCurtainElevationXyz(wallMid)
                            });
                            continue;
                        }

                        elevationView.Name = viewName;
                        elevationView.Scale = scale;
                        if (applyViewTemplate)
                        {
                            if (viewTemplate == null)
                            {
                                viewTemplate = elevationView.CreateViewTemplate();
                                viewTemplate.Name = viewTemplateName;
                                templateCreated = true;
                                ConfigureCurtainElevationViewTemplate(doc, viewTemplate, templateWarnings);
                                templateUpdated = true;
                            }

                            elevationView.ViewTemplateId = viewTemplate.Id;
                        }

                        CurtainElevationCropResult cropResult = ConfigureCurtainElevationCrop(doc, elevationView, wall, wallMid, markerPoint, horizontalMarginFt, verticalMarginFt, fallbackDepthFt);
                        ConfigureCurtainElevationFarClip(elevationView, cropResult, templateWarnings);
                        CurtainElevationDimensionResult dimensionResult = CreateCurtainElevationDimensions(
                            doc,
                            elevationView,
                            wall,
                            cropResult,
                            dimensionType,
                            addDimensions,
                            dimensionOffsetFt,
                            dimensionStackOffsetFt);
                        doc.Regenerate();
                        VerifyCurtainElevationDimensionResult(doc, elevationView, dimensionResult);
                        dimensionsCreatedCount += dimensionResult.CreatedCount;
                        dimensionsFailedCount += dimensionResult.FailedCount;
                        if (!string.IsNullOrWhiteSpace(dimensionResult.Warning))
                            dimensionWarnings.Add($"Wall {wall.Id.GetIdValue()}: {dimensionResult.Warning}");
                        created.Add(new
                        {
                            WallId = wall.Id.GetIdValue(),
                            ViewId = elevationView.Id.GetIdValue(),
                            ViewName = elevationView.Name,
                            ElevationViewTypeId = elevationType.Id.GetIdValue(),
                            ElevationViewTypeName = elevationType.Name,
                            LevelName = levelName,
                            Mark = mark,
                            IsPersistentOutput = true,
                            MarkerId = marker.Id.GetIdValue(),
                            FarClipDepthMm = Math.Round(cropResult.FarClipDepthFt * 304.8, 1),
                            FarClipMethod = cropResult.FarClipMethod,
                            FarClipRequestedDepthMm = Math.Round(cropResult.FarClipRequestedDepthFt * 304.8, 1),
                            FarClipActualOffsetMm = cropResult.FarClipActualOffsetFt.HasValue ? Math.Round(cropResult.FarClipActualOffsetFt.Value * 304.8, 1) : (double?)null,
                            FarClipActualActive = cropResult.FarClipActualActive,
                            FarClipActualMode = cropResult.FarClipActualMode,
                            FarClipDepthOrigin = ToCurtainElevationPointMm(cropResult.FarClipDepthOrigin),
                            FarClipLookDirection = ToCurtainElevationXyz(cropResult.FarClipLookDirection),
                            FarClipMinCandidateDepthMm = Math.Round(cropResult.FarClipMinCandidateDepthFt * 304.8, 1),
                            FarClipMaxCandidateDepthMm = Math.Round(cropResult.FarClipMaxCandidateDepthFt * 304.8, 1),
                            FarClipPositivePointCount = cropResult.FarClipPositivePointCount,
                            FarClipWarning = cropResult.FarClipWarning,
                            FarClipMarginMm = Math.Round(cropResult.FarClipMarginFt * 304.8, 1),
                            FarClipNearestTargetMm = Math.Round(cropResult.FarClipNearestTargetFt * 304.8, 1),
                            FarClipFarthestTargetMm = Math.Round(cropResult.FarClipFarthestTargetFt * 304.8, 1),
                            FarClipPointSource = cropResult.FarClipPointSource,
                            FarClipExtremeContributor = cropResult.FarClipExtremeContributor,
                            FarClipCropBoxDepthApplied = cropResult.FarClipCropBoxDepthApplied,
                            FarClipCropBoxDepthMethod = cropResult.FarClipCropBoxDepthMethod,
                            FarClipViewOriginLocalZMm = Math.Round(cropResult.FarClipViewOriginLocalZFt * 304.8, 1),
                            FarClipLookDirectionLocalZ = Math.Round(cropResult.FarClipLookDirectionLocalZ, 6),
                            FarClipCropBoxMinZBeforeMm = Math.Round(cropResult.FarClipCropBoxMinZBeforeFt * 304.8, 1),
                            FarClipCropBoxMaxZBeforeMm = Math.Round(cropResult.FarClipCropBoxMaxZBeforeFt * 304.8, 1),
                            FarClipCropBoxMinZAfterMm = Math.Round(cropResult.FarClipCropBoxMinZAfterFt * 304.8, 1),
                            FarClipCropBoxMaxZAfterMm = Math.Round(cropResult.FarClipCropBoxMaxZAfterFt * 304.8, 1),
                            FarClipCropBoxDepthAfterMm = Math.Round(cropResult.FarClipCropBoxDepthAfterFt * 304.8, 1),
                            FarClipDepthDeltaMm = Math.Round(cropResult.FarClipDepthDeltaFt * 304.8, 1),
                            FarClipPass = cropResult.FarClipPass,
                            CropMethod = cropResult.Method,
                            CropPointSource = cropResult.PointSource,
                            CropPointCount = cropResult.PointCount,
                            CropFallbackElementCount = cropResult.FallbackElementCount,
                            CropFrameSource = cropResult.FrameSource,
                            CropRightDirection = ToCurtainElevationXyz(cropResult.RightDirection),
                            CropUpDirection = ToCurtainElevationXyz(cropResult.UpDirection),
                            CropDepthDirection = ToCurtainElevationXyz(cropResult.DepthDirection),
                            CropLocalMin = ToCurtainElevationXyz(cropResult.LocalMin),
                            CropLocalMax = ToCurtainElevationXyz(cropResult.LocalMax),
                            CropUsedRevitTransformFallback = cropResult.UsedRevitTransformFallback,
                            CropUsedHostWallFallback = cropResult.UsedHostWallFallback,
                            CropContributingElementIds = cropResult.ContributingElementIds,
                            CropFallbackElementIds = cropResult.FallbackElementIds,
                            CropExtremeContributors = cropResult.ExtremeContributors,
                            Crop2DOrigin = ToCurtainElevationPointMm(cropResult.View2DOrigin),
                            Crop2DRightDirection = ToCurtainElevationXyz(cropResult.View2DRightDirection),
                            Crop2DUpDirection = ToCurtainElevationXyz(cropResult.View2DUpDirection),
                            Crop2DMin = ToCurtainElevationXyz(cropResult.View2DMin),
                            Crop2DMax = ToCurtainElevationXyz(cropResult.View2DMax),
                            Crop2DPointCount = cropResult.View2DPointCount,
                            Crop2DSource = cropResult.View2DSource,
                            Crop2DExtremeContributors = cropResult.View2DExtremeContributors,
                            CropRegionShapeApplied = cropResult.RegionShapeApplied,
                            CropRegionShapeFallbackReason = cropResult.RegionShapeFallbackReason,
                            DimensionAttemptCount = dimensionResult.AttemptCount,
                            DimensionVerifiedCount = dimensionResult.VerifiedCount,
                            DimensionCreationErrors = dimensionResult.CreationErrors,
                            DimensionsCreatedCount = dimensionResult.CreatedCount,
                            DimensionsFailedCount = dimensionResult.FailedCount,
                            DimensionTypeSelectionMode = dimensionTypeSelectionMode,
                            DimensionTypeId = dimensionType?.Id.GetIdValue(),
                            DimensionTypeName = dimensionType?.Name,
                            DimensionTypeSource = dimensionTypeResolution.Source,
                            TotalWidthDimensionId = dimensionResult.TotalWidthDimensionId?.GetIdValue(),
                            HorizontalGridDimensionId = dimensionResult.HorizontalGridDimensionId?.GetIdValue(),
                            TotalHeightDimensionId = dimensionResult.TotalHeightDimensionId?.GetIdValue(),
                            VerticalGridDimensionId = dimensionResult.VerticalGridDimensionId?.GetIdValue(),
                            ReferenceCurveIds = dimensionResult.ReferenceCurveIds.Select(id => id.GetIdValue()).ToList(),
                            TotalWidthDimensionReferenceSource = dimensionResult.TotalWidthDimensionReferenceSource,
                            TotalHeightDimensionReferenceSource = dimensionResult.TotalHeightDimensionReferenceSource,
                            HorizontalGridDimensionReferenceSource = dimensionResult.HorizontalGridDimensionReferenceSource,
                            VerticalGridDimensionReferenceSource = dimensionResult.VerticalGridDimensionReferenceSource,
                            GeometryReferenceCount = dimensionResult.GeometryReferenceCount,
                            CurtainGridLineCount = dimensionResult.CurtainGridLineCount,
                            CurtainGridLineReferenceCount = dimensionResult.CurtainGridLineReferenceCount,
                            CurtainGridLineReferenceSamples = dimensionResult.CurtainGridLineReferenceSamples,
                            CurtainGridLineReferenceFailures = dimensionResult.CurtainGridLineReferenceFailures,
                            GeometryReferenceCategories = dimensionResult.GeometryReferenceCategories,
                            DimensionFallbackReason = dimensionResult.DimensionFallbackReason,
                            DimensionStatus = dimensionResult.Status,
                            DimensionWarnings = dimensionResult.Warnings,
                            DirectionDot = Math.Round(directionResult.DirectionDot, 4),
                            DirectionFixApplied = directionResult.DirectionFixApplied,
                            DesiredLookDirection = ToCurtainElevationXyz(directionResult.DesiredLookDirection),
                            FinalVisualLookDirection = ToCurtainElevationXyz(directionResult.FinalVisualLookDirection),
                            WallOrientation = ToCurtainElevationXyz(FlattenAndNormalize(wall.Orientation)),
                            WallFlipped = wall.Flipped,
                            ResolvedExteriorSide = exterior.SideName,
                            ResolvedExteriorSource = exterior.Source,
                            ResolvedExteriorDirection = ToCurtainElevationXyz(outward),
                            MarkerPoint = ToCurtainElevationXyz(markerPoint),
                            WallMidPoint = ToCurtainElevationXyz(wallMid)
                        });
                    }
                    catch (Exception ex)
                    {
                        skipped.Add(new { WallId = wall.Id.GetIdValue(), LevelName = levelName, Mark = mark, Reason = ex.Message });
                    }
                }

                trans.Commit();
            }

            return new
            {
                Success = true,
                DryRun = false,
                PersistentViewsCreated = true,
                CleanupRequired = false,
                DeleteGeneratedViews = false,
                TotalCurtainWalls = curtainWalls.Count,
                CreatedCount = created.Count,
                SkippedCount = skipped.Count,
                ViewTemplateId = viewTemplate?.Id.GetIdValue() ?? 0,
                ViewTemplateName = viewTemplate?.Name ?? viewTemplateName,
                TemplateCreated = templateCreated,
                TemplateUpdated = templateUpdated,
                ElevationViewTypeId = elevationType?.Id.GetIdValue() ?? 0,
                ElevationViewTypeName = elevationType?.Name ?? elevationViewTypeName,
                ElevationViewTypeCreated = elevationViewTypeCreated,
                AddDimensions = addDimensions,
                DimensionTypeSelectionMode = dimensionTypeSelectionMode,
                DimensionTypeId = dimensionType?.Id.GetIdValue(),
                DimensionTypeName = dimensionType?.Name,
                DimensionTypeSource = dimensionTypeResolution.Source,
                DimensionsCreatedCount = dimensionsCreatedCount,
                DimensionsFailedCount = dimensionsFailedCount,
                DimensionWarnings = dimensionWarnings,
                Created = created,
                Skipped = skipped,
                TemplateWarnings = templateWarnings
            };
        }

        private object DiagnoseCurtainWallElevationDimensions(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;
            UIDocument uidoc = _uiApp.ActiveUIDocument;

            string testMode = parameters["testMode"]?.Value<string>()?.Trim().ToLowerInvariant() ?? "both";
            bool rollback = parameters["rollback"]?.Value<bool>() ?? true;
            var failures = new List<string>();
            var attempts = new List<CurtainElevationDimensionAttempt>();
            var createdDimensionIds = new List<ElementId>();
            var verifiedDimensionIds = new List<ElementId>();
            var referencePlaneIds = new List<ElementId>();
            var dimensionWarnings = new List<string>();

            ViewSection view = null;
            IdType? viewId = parameters["viewId"]?.Value<IdType>();
            if (viewId.HasValue)
                view = doc.GetElement(new ElementId(viewId.Value)) as ViewSection;
            else
                view = uidoc.ActiveView as ViewSection;

            if (view == null || view.IsTemplate)
                throw new Exception("Provide a valid elevation ViewSection viewId or make one active.");

            Wall wall = null;
            IdType? wallId = parameters["wallId"]?.Value<IdType>();
            if (wallId.HasValue)
                wall = doc.GetElement(new ElementId(wallId.Value)) as Wall;
            else
            {
                wall = new FilteredElementCollector(doc)
                    .OfClass(typeof(Wall))
                    .WhereElementIsNotElementType()
                    .Cast<Wall>()
                    .FirstOrDefault(w =>
                    {
                        try { return w.CurtainGrid != null; }
                        catch { return false; }
                    });
            }

            if (wall == null || wall.CurtainGrid == null)
                throw new Exception("Provide a valid curtain wall wallId (Wall with CurtainGrid).");

            CurtainElevationDimensionTypeResolution dimensionTypeResolution =
                ResolveCurtainElevationDimensionType(doc, parameters, dimensionWarnings);
            DimensionType dimensionType = dimensionTypeResolution.DimensionType;
            if (dimensionType == null)
                failures.Add("No DimensionType could be resolved.");

            int referencePlaneCreatedCount = 0;
            int referencePlaneReferenceCount = 0;
            List<CurtainElevationGeometryReference> geometryReferences = new List<CurtainElevationGeometryReference>();
            List<CurtainElevationGeometryReference> gridLineReferences = new List<CurtainElevationGeometryReference>();
            CurtainElevationCropResult cropResult = null;

            using (Transaction trans = new Transaction(doc, rollback ? "Diagnose curtain elevation dimensions (Rollback)" : "Diagnose curtain elevation dimensions"))
            {
                trans.Start();

                try
                {
                    LocationCurve loc = wall.Location as LocationCurve;
                    XYZ wallMid = loc?.Curve?.Evaluate(0.5, true);
                    cropResult = ConfigureCurtainElevationCrop(doc, view, wall, wallMid, view.Origin, 0, 0, 1200.0 / 304.8);
                    doc.Regenerate();

                    Transform sourceFrame = GetCurtainElevationView2DFrame(view, view.CropBox?.Transform);
                    Transform frame = GetCurtainElevationDimensionFrame(view, sourceFrame);
                    if (frame == null || sourceFrame == null || cropResult.View2DMin == null || cropResult.View2DMax == null)
                    {
                        failures.Add("Cannot resolve dimension frame or crop 2D bounds.");
                    }
                    else if (dimensionType != null)
                    {
                        XYZ sourceOriginDelta = sourceFrame.Origin - frame.Origin;
                        double xShift = sourceOriginDelta.DotProduct(frame.BasisX);
                        double yShift = sourceOriginDelta.DotProduct(frame.BasisY);
                        double minX = cropResult.View2DMin.X + xShift;
                        double maxX = cropResult.View2DMax.X + xShift;
                        double minY = cropResult.View2DMin.Y + yShift;
                        double maxY = cropResult.View2DMax.Y + yShift;
                        double offsetFt = 300.0 / 304.8;

                        geometryReferences = CollectCurtainElevationGeometryReferences(doc, wall, view, frame, minX, maxX, minY, maxY);
                        gridLineReferences = CollectCurtainElevationGridLineReferences(doc, wall, view, frame, minX, maxX, minY, maxY);
                        if (testMode == "geometry_reference" || testMode == "both")
                        {
                            List<CurtainElevationGeometryReference> totalWidthRefs = SelectCurtainElevationBoundaryReferences(geometryReferences, "horizontal", minX, maxX, minY, maxY);
                            attempts.Add(TryDiagnoseCurtainGeometryDimension(doc, view, frame, dimensionType, "total_width", "horizontal", new List<double> { minX, maxX }, totalWidthRefs, maxY + offsetFt));

                            List<CurtainElevationGeometryReference> totalHeightRefs = SelectCurtainElevationBoundaryReferences(geometryReferences, "vertical", minX, maxX, minY, maxY);
                            attempts.Add(TryDiagnoseCurtainGeometryDimension(doc, view, frame, dimensionType, "total_height", "vertical", new List<double> { minY, maxY }, totalHeightRefs, maxX + offsetFt));

                            List<double> verticalGridXs = GetCurtainElevationGridCoordinates(doc, wall, frame, "vertical", minX, maxX, minY, maxY);
                            List<CurtainElevationGeometryReference> verticalGridRefs = SelectCurtainElevationGridDimensionReferences(geometryReferences, gridLineReferences, "horizontal", verticalGridXs);
                            attempts.Add(TryDiagnoseCurtainGeometryDimension(doc, view, frame, dimensionType, "horizontal_grid", "horizontal", verticalGridXs, verticalGridRefs, maxY + offsetFt * 2));

                            List<double> horizontalGridYs = GetCurtainElevationGridCoordinates(doc, wall, frame, "horizontal", minX, maxX, minY, maxY);
                            List<CurtainElevationGeometryReference> horizontalGridRefs = SelectCurtainElevationGridDimensionReferences(geometryReferences, gridLineReferences, "vertical", horizontalGridYs);
                            attempts.Add(TryDiagnoseCurtainGeometryDimension(doc, view, frame, dimensionType, "vertical_grid", "vertical", horizontalGridYs, horizontalGridRefs, maxX + offsetFt * 2));
                        }

                        if (testMode == "reference_plane_fallback" || testMode == "both")
                        {
                            attempts.Add(TryDiagnoseCurtainReferencePlaneDimension(doc, view, frame, dimensionType, "total_width", "horizontal", new List<double> { minX, maxX }, minY, maxY, maxY + offsetFt, referencePlaneIds, out int widthRefs));
                            referencePlaneReferenceCount += widthRefs;

                            attempts.Add(TryDiagnoseCurtainReferencePlaneDimension(doc, view, frame, dimensionType, "total_height", "vertical", new List<double> { minY, maxY }, minX, maxX, maxX + offsetFt, referencePlaneIds, out int heightRefs));
                            referencePlaneReferenceCount += heightRefs;
                        }
                    }

                    doc.Regenerate();

                    foreach (CurtainElevationDimensionAttempt attempt in attempts)
                    {
                        if (attempt?.DimensionId == null || attempt.DimensionId == ElementId.InvalidElementId)
                            continue;

                        createdDimensionIds.Add(attempt.DimensionId);
                        Dimension dimension = doc.GetElement(attempt.DimensionId) as Dimension;
                        attempt.ExistsAfterCreate = dimension != null;
                        attempt.OwnerViewId = dimension?.OwnerViewId;
                        if (dimension != null && dimension.OwnerViewId == view.Id)
                            verifiedDimensionIds.Add(attempt.DimensionId);
                        else if (dimension != null)
                            attempt.FailureMessage = AppendCurtainElevationWarning(attempt.FailureMessage, $"OwnerViewId readback mismatch: {dimension.OwnerViewId.GetIdValue()}.");
                    }

                    referencePlaneCreatedCount = referencePlaneIds.Count;

                    if (rollback)
                        trans.RollBack();
                    else
                        trans.Commit();
                }
                catch (Exception ex)
                {
                    failures.Add(ex.Message);
                    if (trans.GetStatus() == TransactionStatus.Started)
                        trans.RollBack();
                }
            }

            return new
            {
                WallId = wall.Id.GetIdValue(),
                ViewId = view.Id.GetIdValue(),
                ViewName = view.Name,
                DimensionTypeId = dimensionType?.Id.GetIdValue(),
                DimensionTypeName = dimensionType?.Name,
                DimensionTypeSource = dimensionTypeResolution.Source,
                DimensionWarnings = dimensionWarnings,
                GeometryReferenceCount = geometryReferences.Count,
                GeometryReferenceSamples = geometryReferences
                    .Take(20)
                    .Select(r => new
                    {
                        ElementId = r.ElementId?.GetIdValue(),
                        Category = r.CategoryName,
                        IsVertical = r.IsVertical,
                        IsHorizontal = r.IsHorizontal,
                        CenterXmm = Math.Round(r.CenterX * 304.8, 1),
                        CenterYmm = Math.Round(r.CenterY * 304.8, 1),
                        LengthMm = Math.Round(r.Length * 304.8, 1)
                    })
                    .ToList(),
                ReferencePlaneCreatedCount = referencePlaneCreatedCount,
                ReferencePlaneReferenceCount = referencePlaneReferenceCount,
                ReferencePlaneIds = referencePlaneIds.Select(id => id.GetIdValue()).ToList(),
                AttemptedDimensions = attempts.Select(ToCurtainElevationDimensionAttemptResult).ToList(),
                CreatedDimensionIds = createdDimensionIds.Select(id => id.GetIdValue()).ToList(),
                VerifiedDimensionIds = verifiedDimensionIds.Select(id => id.GetIdValue()).ToList(),
                Failures = failures,
                Rollback = rollback
            };
        }

        private object DiagnoseCurtainWallElevationDirection(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;
            UIDocument uidoc = _uiApp.ActiveUIDocument;

            IdType wallId = parameters["wallId"]?.Value<IdType>() ?? 0;
            if (wallId == 0)
                throw new Exception("必�??��? wallId");

            int scale = parameters["scale"]?.Value<int>() ?? 50;
            double offsetFt = (parameters["offsetMm"]?.Value<double>() ?? 1500.0) / 304.8;
            bool includeCropDiagnostics = parameters["includeCropDiagnostics"]?.Value<bool>() ?? false;

            Wall wall = doc.GetElement(new ElementId(wallId)) as Wall;
            if (wall == null)
                throw new Exception($"?��???Wall ID: {wallId}");
            if (wall.CurtainGrid == null)
                throw new Exception($"Wall ID {wallId} 不是 CurtainGrid != null ?�帷幕�?");

            LocationCurve loc = wall.Location as LocationCurve;
            if (loc == null || loc.Curve == null)
                throw new Exception($"Wall ID {wallId} 沒�??�用 LocationCurve");

            ViewFamilyType elevationType = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>()
                .FirstOrDefault(vft => vft.ViewFamily == ViewFamily.Elevation);
            if (elevationType == null)
                throw new Exception("?��???Elevation ??ViewFamilyType");

            ViewPlan explicitPlacementView = ResolveCurtainElevationPlacementView(doc, parameters);
            Dictionary<ElementId, ViewPlan> floorPlansByLevel = GetCurtainElevationFloorPlansByLevel(doc);
            ViewPlan activePlan = uidoc.ActiveView as ViewPlan;
            if (activePlan != null && activePlan.IsTemplate)
                activePlan = null;

            ViewPlan placementView = explicitPlacementView
                ?? ResolveCurtainElevationPlanForWall(wall, activePlan, floorPlansByLevel);
            if (placementView == null)
                throw new Exception("?��??�可?��??�置 ElevationMarker ??ViewPlan");

            Level level = doc.GetElement(wall.LevelId) as Level;
            Curve curve = loc.Curve;
            XYZ start = curve.GetEndPoint(0);
            XYZ end = curve.GetEndPoint(1);
            XYZ wallMid = curve.Evaluate(0.5, true);
            XYZ wallDirection = FlattenAndNormalize(end - start);
            XYZ wallOrientation = FlattenAndNormalize(wall.Orientation);
            if (wallOrientation == null)
                throw new Exception("?��??�斷 wall.Orientation");

            XYZ apiExteriorMarkerPoint = wallMid + wallOrientation * (wall.Width / 2.0 + offsetFt);
            XYZ apiExteriorLookDirection = GetCurtainElevationDesiredLookDirection(wallMid, apiExteriorMarkerPoint);
            XYZ uiArrowSideCandidate = wallOrientation.Negate();
            XYZ uiArrowMarkerPoint = wallMid + uiArrowSideCandidate * (wall.Width / 2.0 + offsetFt);
            var notes = new List<string>
            {
                "Points are millimeters in model coordinates; vectors are unitless XYZ directions.",
                "UiArrowSideCandidate is a diagnostic hypothesis only: opposite of wall.Orientation. It is not used by create_curtain_wall_elevations.",
                "The temporary ElevationMarker/ViewSection are created inside a rollback transaction and should not remain in the model."
            };

            XYZ initialViewDirection = null;
            XYZ initialVisualLookDirection = null;
            XYZ temporaryViewDirection = null;
            XYZ temporaryVisualLookDirection = null;
            double directionDot = -1.0;
            bool directionFixApplied = false;
            bool wouldPassDirectionCheck = false;
            object cropDiagnostics = null;

            using (Transaction trans = new Transaction(doc, "Diagnose curtain wall elevation direction (Rollback)"))
            {
                trans.Start();

                ElevationMarker marker = ElevationMarker.CreateElevationMarker(doc, elevationType.Id, apiExteriorMarkerPoint, scale);
                ViewSection view = marker.CreateElevation(doc, placementView.Id, 0);
                doc.Regenerate();

                initialViewDirection = view.ViewDirection;
                initialVisualLookDirection = GetCurtainElevationVisualLookDirection(view);

                CurtainElevationDirectionResult directionResult = AlignCurtainElevationMarkerByVisualLook(
                    doc, marker, apiExteriorMarkerPoint, view, apiExteriorLookDirection);

                temporaryViewDirection = view.ViewDirection;
                temporaryVisualLookDirection = directionResult.FinalVisualLookDirection;
                directionDot = directionResult.DirectionDot;
                directionFixApplied = directionResult.DirectionFixApplied;
                wouldPassDirectionCheck = directionDot >= CurtainElevationDirectionDotThreshold;

                if (includeCropDiagnostics)
                    cropDiagnostics = BuildCurtainElevationCropDiagnostics(doc, wall, view, wallMid, apiExteriorMarkerPoint, directionResult);

                trans.RollBack();
            }

            return new
            {
                WallId = wall.Id.GetIdValue(),
                WallType = wall.WallType?.Name,
                LevelName = level?.Name,
                Flipped = wall.Flipped,
                Units = "points=mm, vectors=unitless",
                StartPoint = ToCurtainElevationPointMm(start),
                EndPoint = ToCurtainElevationPointMm(end),
                WallMidPoint = ToCurtainElevationPointMm(wallMid),
                WallDirection = ToCurtainElevationXyz(wallDirection),
                WallOrientation = ToCurtainElevationXyz(wallOrientation),
                ApiExteriorMarkerPoint = ToCurtainElevationPointMm(apiExteriorMarkerPoint),
                ApiExteriorLookDirection = ToCurtainElevationXyz(apiExteriorLookDirection),
                UiArrowSideCandidate = ToCurtainElevationXyz(uiArrowSideCandidate),
                UiArrowMarkerPoint = ToCurtainElevationPointMm(uiArrowMarkerPoint),
                InitialViewDirection = ToCurtainElevationXyz(initialViewDirection),
                InitialVisualLookDirection = ToCurtainElevationXyz(initialVisualLookDirection),
                TemporaryViewDirection = ToCurtainElevationXyz(temporaryViewDirection),
                TemporaryVisualLookDirection = ToCurtainElevationXyz(temporaryVisualLookDirection),
                DirectionDot = Math.Round(directionDot, 4),
                DirectionFixApplied = directionFixApplied,
                WouldPassDirectionCheck = wouldPassDirectionCheck,
                DirectionDotThreshold = CurtainElevationDirectionDotThreshold,
                IncludeCropDiagnostics = includeCropDiagnostics,
                CropDiagnostics = cropDiagnostics,
                PlacementViewId = placementView.Id.GetIdValue(),
                PlacementViewName = placementView.Name,
                Notes = notes
            };
        }

        private object DiagnoseCurtainWallElevationDirections(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;
            UIDocument uidoc = _uiApp.ActiveUIDocument;

            int scale = parameters["scale"]?.Value<int>() ?? 50;
            double offsetFt = (parameters["offsetMm"]?.Value<double>() ?? 1500.0) / 304.8;
            bool includeTemporaryMarker = parameters["includeTemporaryMarker"]?.Value<bool>() ?? true;
            bool includeCropDiagnostics = parameters["includeCropDiagnostics"]?.Value<bool>() ?? false;
            if (includeCropDiagnostics)
                includeTemporaryMarker = true;
            JObject knownExteriorSideByWallId = parameters["knownExteriorSideByWallId"] as JObject;

            ViewFamilyType elevationType = null;
            if (includeTemporaryMarker)
            {
                elevationType = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewFamilyType))
                    .Cast<ViewFamilyType>()
                    .FirstOrDefault(vft => vft.ViewFamily == ViewFamily.Elevation);
                if (elevationType == null)
                    throw new Exception("No Elevation ViewFamilyType is available.");
            }

            ViewPlan explicitPlacementView = ResolveCurtainElevationPlacementView(doc, parameters);
            Dictionary<ElementId, ViewPlan> floorPlansByLevel = GetCurtainElevationFloorPlansByLevel(doc);
            ViewPlan activePlan = uidoc.ActiveView as ViewPlan;
            if (activePlan != null && activePlan.IsTemplate)
                activePlan = null;

            JArray requestedWallIds = parameters["wallIds"] as JArray;
            var walls = new List<Wall>();
            var skipped = new List<object>();

            if (requestedWallIds != null && requestedWallIds.Count > 0)
            {
                foreach (JToken token in requestedWallIds)
                {
                    IdType id = token.Value<IdType>();
                    Wall wall = doc.GetElement(new ElementId(id)) as Wall;
                    if (wall == null)
                    {
                        skipped.Add(new { WallId = id, Reason = "Element is not a Wall." });
                        continue;
                    }

                    bool isCurtainWall = false;
                    try { isCurtainWall = wall.CurtainGrid != null; }
                    catch { isCurtainWall = false; }

                    if (!isCurtainWall)
                    {
                        skipped.Add(new { WallId = id, Reason = "Wall.CurtainGrid is null." });
                        continue;
                    }

                    walls.Add(wall);
                }
            }
            else
            {
                walls = new FilteredElementCollector(doc)
                    .OfClass(typeof(Wall))
                    .WhereElementIsNotElementType()
                    .Cast<Wall>()
                    .Where(w =>
                    {
                        try { return w.CurtainGrid != null; }
                        catch { return false; }
                    })
                    .OrderBy(w => w.LevelId.GetIdValue())
                    .ThenBy(w => w.Id.GetIdValue())
                    .ToList();
            }

            var results = new List<object>();

            foreach (Wall wall in walls)
            {
                Level level = doc.GetElement(wall.LevelId) as Level;
                string mark = GetCurtainWallMark(wall);

                try
                {
                    LocationCurve loc = wall.Location as LocationCurve;
                    if (loc == null || loc.Curve == null)
                    {
                        skipped.Add(new { WallId = wall.Id.GetIdValue(), LevelName = level?.Name, Mark = mark, Reason = "Wall has no usable LocationCurve." });
                        continue;
                    }

                    XYZ wallOrientation = FlattenAndNormalize(wall.Orientation);
                    if (wallOrientation == null)
                    {
                        skipped.Add(new { WallId = wall.Id.GetIdValue(), LevelName = level?.Name, Mark = mark, Reason = "Cannot resolve wall.Orientation." });
                        continue;
                    }

                    ViewPlan placementView = null;
                    if (includeTemporaryMarker)
                    {
                        placementView = explicitPlacementView
                            ?? ResolveCurtainElevationPlanForWall(wall, activePlan, floorPlansByLevel);
                        if (placementView == null)
                        {
                            skipped.Add(new { WallId = wall.Id.GetIdValue(), LevelName = level?.Name, Mark = mark, Reason = "No usable ViewPlan for temporary ElevationMarker placement." });
                            continue;
                        }
                    }

                    Curve curve = loc.Curve;
                    XYZ start = curve.GetEndPoint(0);
                    XYZ end = curve.GetEndPoint(1);
                    XYZ wallMid = curve.Evaluate(0.5, true);
                    XYZ wallDirection = FlattenAndNormalize(end - start);
                    CurtainElevationExteriorResolution autoResolved = ResolveCurtainElevationExteriorSide(wall);

                    CurtainElevationSideCandidate apiCandidate;
                    CurtainElevationSideCandidate oppositeCandidate;
                    object cropDiagnostics = null;

                    if (includeTemporaryMarker)
                    {
                        using (Transaction trans = new Transaction(doc, "Diagnose curtain wall elevation sides (Rollback)"))
                        {
                            trans.Start();
                            apiCandidate = BuildCurtainElevationSideCandidate(
                                doc, elevationType, placementView, wall, wallMid, wallOrientation, offsetFt, scale, "api_orientation", true);
                            oppositeCandidate = BuildCurtainElevationSideCandidate(
                                doc, elevationType, placementView, wall, wallMid, wallOrientation.Negate(), offsetFt, scale, "opposite_orientation", true);
                            if (includeCropDiagnostics)
                            {
                                cropDiagnostics = BuildCurtainElevationCropDiagnosticsForSide(
                                    doc, elevationType, placementView, wall, wallMid, autoResolved?.ExteriorDirection, offsetFt, scale);
                            }
                            trans.RollBack();
                        }
                    }
                    else
                    {
                        apiCandidate = BuildCurtainElevationSideCandidate(
                            doc, elevationType, placementView, wall, wallMid, wallOrientation, offsetFt, scale, "api_orientation", false);
                        oppositeCandidate = BuildCurtainElevationSideCandidate(
                            doc, elevationType, placementView, wall, wallMid, wallOrientation.Negate(), offsetFt, scale, "opposite_orientation", false);
                    }

                    string wallIdKey = wall.Id.GetIdValue().ToString();
                    string knownExteriorSide = knownExteriorSideByWallId?[wallIdKey]?.Value<string>();
                    bool knownSideValid = knownExteriorSide == "api_orientation" || knownExteriorSide == "opposite_orientation";
                    string matchedCandidate = knownSideValid ? knownExteriorSide : null;
                    string autoResolvedCandidate = autoResolved?.SideName;
                    bool? autoMatchesKnownExteriorSide = knownSideValid && autoResolvedCandidate != null
                        ? autoResolvedCandidate == knownExteriorSide
                        : (bool?)null;
                    string recommendation = knownSideValid
                        ? knownExteriorSide
                        : "ambiguous_requires_user_label";

                    results.Add(new
                    {
                        WallId = wall.Id.GetIdValue(),
                        WallType = wall.WallType?.Name,
                        LevelName = level?.Name,
                        Mark = mark,
                        Flipped = wall.Flipped,
                        Units = "points=mm, vectors=unitless",
                        StartPoint = ToCurtainElevationPointMm(start),
                        EndPoint = ToCurtainElevationPointMm(end),
                        WallMidPoint = ToCurtainElevationPointMm(wallMid),
                        WallDirection = ToCurtainElevationXyz(wallDirection),
                        WallOrientation = ToCurtainElevationXyz(wallOrientation),
                        ApiCandidate = ToCurtainElevationSideCandidateResult(apiCandidate),
                        OppositeCandidate = ToCurtainElevationSideCandidateResult(oppositeCandidate),
                        KnownExteriorSide = knownExteriorSide,
                        KnownExteriorSideValid = knownSideValid,
                        MatchedCandidate = matchedCandidate,
                        AutoResolvedCandidate = autoResolvedCandidate,
                        AutoResolvedSource = autoResolved?.Source,
                        AutoResolvedExteriorDirection = ToCurtainElevationXyz(autoResolved?.ExteriorDirection),
                        AutoMatchesKnownExteriorSide = autoMatchesKnownExteriorSide,
                        Recommendation = recommendation,
                        IncludeCropDiagnostics = includeCropDiagnostics,
                        CropDiagnostics = cropDiagnostics,
                        PlacementViewId = placementView?.Id.GetIdValue() ?? 0,
                        PlacementViewName = placementView?.Name,
                        Notes = new[]
                        {
                            "Revit API does not expose the wall flip-control/double-arrow UI position as queryable model geometry.",
                            "api_orientation uses wall.Orientation. opposite_orientation uses -wall.Orientation.",
                            "Recommendation remains ambiguous unless knownExteriorSideByWallId supplies a user label."
                        }
                    });
                }
                catch (Exception ex)
                {
                    skipped.Add(new { WallId = wall.Id.GetIdValue(), LevelName = level?.Name, Mark = mark, Reason = ex.Message });
                }
            }

            return new
            {
                Success = true,
                TotalCurtainWalls = requestedWallIds != null && requestedWallIds.Count > 0 ? requestedWallIds.Count : walls.Count,
                DiagnosedCount = results.Count,
                SkippedCount = skipped.Count,
                IncludeTemporaryMarker = includeTemporaryMarker,
                DirectionDotThreshold = CurtainElevationDirectionDotThreshold,
                Results = results,
                Skipped = skipped
            };
        }

        private string GetCurtainWallMark(Wall wall)
        {
            string mark = wall.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString();
            return string.IsNullOrWhiteSpace(mark) ? $"CW-{wall.Id.GetIdValue()}" : mark.Trim();
        }

        private ViewPlan ResolveCurtainElevationPlacementView(Document doc, JObject parameters)
        {
            IdType placementViewId = parameters["placementViewId"]?.Value<IdType>() ?? 0;
            string placementViewName = parameters["placementViewName"]?.Value<string>();

            if (placementViewId != 0)
            {
                ViewPlan view = doc.GetElement(new ElementId(placementViewId)) as ViewPlan;
                if (view == null || view.IsTemplate)
                    throw new Exception($"placementViewId {placementViewId} 不是?�用??ViewPlan");
                return view;
            }

            if (!string.IsNullOrWhiteSpace(placementViewName))
            {
                ViewPlan view = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewPlan))
                    .Cast<ViewPlan>()
                    .FirstOrDefault(v => !v.IsTemplate && v.Name == placementViewName);
                if (view == null)
                    throw new Exception($"?��???placementViewName ?��???ViewPlan: {placementViewName}");
                return view;
            }

            return null;
        }

        private ViewFamilyType GetFirstCurtainElevationViewFamilyType(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>()
                .FirstOrDefault(vft => vft.ViewFamily == ViewFamily.Elevation);
        }

        private ViewFamilyType FindCurtainElevationViewFamilyType(Document doc, string typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName))
                return null;

            return new FilteredElementCollector(doc)
                .OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>()
                .FirstOrDefault(vft => vft.ViewFamily == ViewFamily.Elevation && vft.Name == typeName);
        }

        private ViewFamilyType GetOrCreateCurtainElevationViewFamilyType(
            Document doc,
            ViewFamilyType sourceElevationType,
            string typeName,
            out bool created,
            List<string> warnings)
        {
            created = false;
            string resolvedName = string.IsNullOrWhiteSpace(typeName) ? "帷幕立面" : typeName.Trim();
            ViewFamilyType existing = FindCurtainElevationViewFamilyType(doc, resolvedName);
            if (existing != null)
                return existing;

            if (sourceElevationType == null)
                throw new Exception("?��???Elevation ??ViewFamilyType");

            try
            {
                ElementType duplicated = sourceElevationType.Duplicate(resolvedName);
                ViewFamilyType createdType = duplicated as ViewFamilyType;
                if (createdType == null)
                    throw new Exception("Duplicate did not return a ViewFamilyType.");

                created = true;
                return createdType;
            }
            catch (Exception ex)
            {
                warnings?.Add($"?��?建�?立面?��??�「{resolvedName}?��??�用?��?類�??�{sourceElevationType.Name}?? {ex.Message}");
                return sourceElevationType;
            }
        }

        private Dictionary<ElementId, ViewPlan> GetCurtainElevationFloorPlansByLevel(Document doc)
        {
            var result = new Dictionary<ElementId, ViewPlan>();
            foreach (ViewPlan view in new FilteredElementCollector(doc).OfClass(typeof(ViewPlan)).Cast<ViewPlan>())
            {
                if (view.IsTemplate || view.ViewType != ViewType.FloorPlan || view.GenLevel == null)
                    continue;
                if (!result.ContainsKey(view.GenLevel.Id))
                    result[view.GenLevel.Id] = view;
            }
            return result;
        }

        private ViewPlan ResolveCurtainElevationPlanForWall(Wall wall, ViewPlan activePlan, Dictionary<ElementId, ViewPlan> floorPlansByLevel)
        {
            if (activePlan != null && activePlan.ViewType == ViewType.FloorPlan)
                return activePlan;
            if (floorPlansByLevel.TryGetValue(wall.LevelId, out ViewPlan plan))
                return plan;
            return null;
        }

        private string MakeUniqueCurtainElevationViewName(HashSet<string> existingNames, string baseName)
        {
            baseName = string.IsNullOrWhiteSpace(baseName) ? "帷幕立面" : baseName.Trim();
            if (existingNames.Add(baseName))
                return baseName;

            for (int i = 2; i < 10000; i++)
            {
                string candidate = $"{baseName}_{i}";
                if (existingNames.Add(candidate))
                    return candidate;
            }

            string fallback = $"{baseName}_{DateTime.Now:HHmmss}";
            existingNames.Add(fallback);
            return fallback;
        }

        private XYZ FlattenAndNormalize(XYZ vector)
        {
            if (vector == null)
                return null;
            XYZ flat = new XYZ(vector.X, vector.Y, 0);
            return flat.GetLength() < 1e-9 ? null : flat.Normalize();
        }

        private class CurtainElevationDirectionResult
        {
            public double DirectionDot { get; set; } = -1.0;
            public bool DirectionFixApplied { get; set; }
            public XYZ DesiredLookDirection { get; set; }
            public XYZ FinalVisualLookDirection { get; set; }
        }

        private class CurtainElevationExteriorResolution
        {
            public string SideName { get; set; }
            public XYZ ExteriorDirection { get; set; }
            public string Source { get; set; }
        }

        private CurtainElevationExteriorResolution ResolveCurtainElevationExteriorSide(Wall wall)
        {
            XYZ orientation = FlattenAndNormalize(wall?.Orientation);
            if (orientation == null)
            {
                return new CurtainElevationExteriorResolution
                {
                    SideName = null,
                    ExteriorDirection = null,
                    Source = "wall.Flipped"
                };
            }

            bool useApiOrientation = wall.Flipped;
            return new CurtainElevationExteriorResolution
            {
                SideName = useApiOrientation ? "api_orientation" : "opposite_orientation",
                ExteriorDirection = useApiOrientation ? orientation : orientation.Negate(),
                Source = "wall.Flipped"
            };
        }

        private class CurtainElevationSideCandidate
        {
            public string CandidateName { get; set; }
            public XYZ ExteriorDirection { get; set; }
            public XYZ MarkerPoint { get; set; }
            public XYZ DesiredLookDirection { get; set; }
            public XYZ InitialViewDirection { get; set; }
            public XYZ InitialVisualLookDirection { get; set; }
            public XYZ TemporaryViewDirection { get; set; }
            public XYZ TemporaryVisualLookDirection { get; set; }
            public double DirectionDot { get; set; } = -1.0;
            public bool DirectionFixApplied { get; set; }
            public bool WouldPassDirectionCheck { get; set; }
            public string Error { get; set; }
        }

        private CurtainElevationSideCandidate BuildCurtainElevationSideCandidate(
            Document doc,
            ViewFamilyType elevationType,
            ViewPlan placementView,
            Wall wall,
            XYZ wallMid,
            XYZ exteriorDirection,
            double offsetFt,
            int scale,
            string candidateName,
            bool includeTemporaryMarker)
        {
            XYZ exterior = FlattenAndNormalize(exteriorDirection);
            XYZ markerPoint = wallMid != null && exterior != null
                ? wallMid + exterior * (wall.Width / 2.0 + offsetFt)
                : null;
            XYZ desiredLookDirection = GetCurtainElevationDesiredLookDirection(wallMid, markerPoint);

            var candidate = new CurtainElevationSideCandidate
            {
                CandidateName = candidateName,
                ExteriorDirection = exterior,
                MarkerPoint = markerPoint,
                DesiredLookDirection = desiredLookDirection
            };

            if (!includeTemporaryMarker)
                return candidate;

            if (doc == null || elevationType == null || placementView == null || markerPoint == null || desiredLookDirection == null)
            {
                candidate.Error = "Missing data required to create a temporary ElevationMarker.";
                return candidate;
            }

            try
            {
                ElevationMarker marker = ElevationMarker.CreateElevationMarker(doc, elevationType.Id, markerPoint, scale);
                ViewSection view = marker.CreateElevation(doc, placementView.Id, 0);
                doc.Regenerate();

                candidate.InitialViewDirection = view.ViewDirection;
                candidate.InitialVisualLookDirection = GetCurtainElevationVisualLookDirection(view);

                CurtainElevationDirectionResult directionResult = AlignCurtainElevationMarkerByVisualLook(
                    doc, marker, markerPoint, view, desiredLookDirection);

                candidate.TemporaryViewDirection = view.ViewDirection;
                candidate.TemporaryVisualLookDirection = directionResult.FinalVisualLookDirection;
                candidate.DirectionDot = directionResult.DirectionDot;
                candidate.DirectionFixApplied = directionResult.DirectionFixApplied;
                candidate.WouldPassDirectionCheck = directionResult.DirectionDot >= CurtainElevationDirectionDotThreshold;
            }
            catch (Exception ex)
            {
                candidate.Error = ex.Message;
            }

            return candidate;
        }

        private object ToCurtainElevationSideCandidateResult(CurtainElevationSideCandidate candidate)
        {
            if (candidate == null)
                return null;

            return new
            {
                CandidateName = candidate.CandidateName,
                ExteriorDirection = ToCurtainElevationXyz(candidate.ExteriorDirection),
                MarkerPoint = ToCurtainElevationPointMm(candidate.MarkerPoint),
                DesiredLookDirection = ToCurtainElevationXyz(candidate.DesiredLookDirection),
                InitialViewDirection = ToCurtainElevationXyz(candidate.InitialViewDirection),
                InitialVisualLookDirection = ToCurtainElevationXyz(candidate.InitialVisualLookDirection),
                TemporaryViewDirection = ToCurtainElevationXyz(candidate.TemporaryViewDirection),
                TemporaryVisualLookDirection = ToCurtainElevationXyz(candidate.TemporaryVisualLookDirection),
                DirectionDot = Math.Round(candidate.DirectionDot, 4),
                DirectionFixApplied = candidate.DirectionFixApplied,
                WouldPassDirectionCheck = candidate.WouldPassDirectionCheck,
                Error = candidate.Error
            };
        }

        private XYZ GetCurtainElevationDesiredLookDirection(XYZ wallMid, XYZ markerPoint)
        {
            if (wallMid == null || markerPoint == null)
                return null;

            return FlattenAndNormalize(wallMid - markerPoint);
        }

        private XYZ GetCurtainElevationVisualLookDirection(ViewSection view)
        {
            if (view == null)
                return null;

            // Elevation API ViewDirection is opposite to the visible marker look direction.
            return FlattenAndNormalize(view.ViewDirection.Negate());
        }

        private CurtainElevationDirectionResult AlignCurtainElevationMarkerByVisualLook(
            Document doc,
            ElevationMarker marker,
            XYZ origin,
            ViewSection view,
            XYZ desiredLookDirection)
        {
            XYZ desired = FlattenAndNormalize(desiredLookDirection);
            var result = new CurtainElevationDirectionResult
            {
                DesiredLookDirection = desired,
                FinalVisualLookDirection = GetCurtainElevationVisualLookDirection(view)
            };

            if (doc == null || marker == null || origin == null || view == null || desired == null)
                return result;

            RotateCurtainElevationMarkerByVisualLook(doc, marker, origin, view, desired);
            doc.Regenerate();

            result.FinalVisualLookDirection = GetCurtainElevationVisualLookDirection(view);
            result.DirectionDot = GetCurtainElevationDirectionDot(result.FinalVisualLookDirection, desired);

            if (result.DirectionDot < 0.0)
            {
                Line axis = Line.CreateBound(origin, origin + XYZ.BasisZ);
                ElementTransformUtils.RotateElement(doc, marker.Id, axis, Math.PI);
                result.DirectionFixApplied = true;
                doc.Regenerate();

                result.FinalVisualLookDirection = GetCurtainElevationVisualLookDirection(view);
                result.DirectionDot = GetCurtainElevationDirectionDot(result.FinalVisualLookDirection, desired);
            }

            return result;
        }

        private void RotateCurtainElevationMarkerByVisualLook(
            Document doc,
            ElevationMarker marker,
            XYZ origin,
            ViewSection view,
            XYZ desiredLookDirection)
        {
            XYZ current = GetCurtainElevationVisualLookDirection(view);
            XYZ desired = FlattenAndNormalize(desiredLookDirection);
            if (current == null || desired == null)
                return;

            double dot = Math.Max(-1.0, Math.Min(1.0, current.DotProduct(desired)));
            double crossZ = current.CrossProduct(desired).Z;
            double angle = Math.Atan2(crossZ, dot);
            if (Math.Abs(angle) < 1e-9)
                return;

            Line axis = Line.CreateBound(origin, origin + XYZ.BasisZ);
            ElementTransformUtils.RotateElement(doc, marker.Id, axis, angle);
        }

        private double GetCurtainElevationDirectionDot(XYZ visualLookDirection, XYZ desiredLookDirection)
        {
            XYZ visual = FlattenAndNormalize(visualLookDirection);
            XYZ desired = FlattenAndNormalize(desiredLookDirection);
            if (visual == null || desired == null)
                return -1.0;

            return Math.Max(-1.0, Math.Min(1.0, visual.DotProduct(desired)));
        }

        private void DeleteCurtainElevationMarkerAndView(Document doc, ElevationMarker marker, ViewSection view)
        {
            var ids = new List<ElementId>();
            if (view != null && view.Id != ElementId.InvalidElementId)
                ids.Add(view.Id);
            if (marker != null && marker.Id != ElementId.InvalidElementId)
                ids.Add(marker.Id);

            foreach (ElementId id in ids.Distinct())
            {
                try
                {
                    if (doc.GetElement(id) != null)
                        doc.Delete(id);
                }
                catch
                {
                    // Best effort cleanup; the skipped result still reports the direction failure.
                }
            }
        }

        private object ToCurtainElevationXyz(XYZ value)
        {
            if (value == null)
                return null;

            return new
            {
                X = Math.Round(value.X, 6),
                Y = Math.Round(value.Y, 6),
                Z = Math.Round(value.Z, 6)
            };
        }

        private object ToCurtainElevationPointMm(XYZ value)
        {
            if (value == null)
                return null;

            return new
            {
                X = Math.Round(value.X * 304.8, 2),
                Y = Math.Round(value.Y * 304.8, 2),
                Z = Math.Round(value.Z * 304.8, 2)
            };
        }

        private class CurtainElevationCropResult
        {
            public double FarClipDepthFt { get; set; }
            public string FarClipMethod { get; set; } = "fallback_depth";
            public double FarClipRequestedDepthFt { get; set; }
            public double? FarClipActualOffsetFt { get; set; }
            public int? FarClipActualActive { get; set; }
            public int? FarClipActualMode { get; set; }
            public XYZ FarClipDepthOrigin { get; set; }
            public XYZ FarClipLookDirection { get; set; }
            public double FarClipMinCandidateDepthFt { get; set; }
            public double FarClipMaxCandidateDepthFt { get; set; }
            public int FarClipPositivePointCount { get; set; }
            public string FarClipWarning { get; set; }
            public double FarClipMarginFt { get; set; }
            public double FarClipNearestTargetFt { get; set; }
            public double FarClipFarthestTargetFt { get; set; }
            public string FarClipPointSource { get; set; }
            public object FarClipExtremeContributor { get; set; }
            public bool FarClipCropBoxDepthApplied { get; set; }
            public string FarClipCropBoxDepthMethod { get; set; }
            public double FarClipViewOriginLocalZFt { get; set; }
            public double FarClipLookDirectionLocalZ { get; set; }
            public double FarClipCropBoxMinZBeforeFt { get; set; }
            public double FarClipCropBoxMaxZBeforeFt { get; set; }
            public double FarClipCropBoxMinZAfterFt { get; set; }
            public double FarClipCropBoxMaxZAfterFt { get; set; }
            public double FarClipCropBoxDepthAfterFt { get; set; }
            public double FarClipDepthDeltaFt { get; set; }
            public bool FarClipPass { get; set; }
            public string Method { get; set; } = "view_2d_visible_bounds";
            public string PointSource { get; set; } = "bbox_fallback";
            public int PointCount { get; set; }
            public int FallbackElementCount { get; set; }
            public string FrameSource { get; set; } = "unresolved";
            public XYZ RightDirection { get; set; }
            public XYZ UpDirection { get; set; }
            public XYZ DepthDirection { get; set; }
            public XYZ LocalMin { get; set; }
            public XYZ LocalMax { get; set; }
            public bool UsedRevitTransformFallback { get; set; }
            public bool UsedHostWallFallback { get; set; }
            public List<IdType> ContributingElementIds { get; set; } = new List<IdType>();
            public List<IdType> FallbackElementIds { get; set; } = new List<IdType>();
            public object ExtremeContributors { get; set; }
            public XYZ View2DOrigin { get; set; }
            public XYZ View2DRightDirection { get; set; }
            public XYZ View2DUpDirection { get; set; }
            public XYZ View2DMin { get; set; }
            public XYZ View2DMax { get; set; }
            public int View2DPointCount { get; set; }
            public string View2DSource { get; set; }
            public object View2DExtremeContributors { get; set; }
            public bool RegionShapeApplied { get; set; }
            public string RegionShapeFallbackReason { get; set; } = "disabled_for_diagnostics";
        }

        private class CurtainElevationDimensionTypeResolution
        {
            public DimensionType DimensionType { get; set; }
            public string Source { get; set; } = "not_resolved";
        }

        private class CurtainElevationDimensionResult
        {
            public ElementId TotalWidthDimensionId { get; set; }
            public ElementId HorizontalGridDimensionId { get; set; }
            public ElementId TotalHeightDimensionId { get; set; }
            public ElementId VerticalGridDimensionId { get; set; }
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
            public string HorizontalGridDimensionReferenceSource { get; set; }
            public string VerticalGridDimensionReferenceSource { get; set; }
            public string DimensionFallbackReason { get; set; }
            public int AttemptCount { get; set; }
            public int VerifiedCount { get; set; }
            public List<string> CreationErrors { get; } = new List<string>();
            public int CreatedCount { get; set; }
            public int FailedCount { get; set; }
            public string Status { get; set; } = "not_started";
            public string Warning => string.Join(" ", Warnings.Where(w => !string.IsNullOrWhiteSpace(w)));
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
            public string FailureMessage { get; set; }
        }

        private class CurtainElevationPointRecord
        {
            public XYZ Point { get; set; }
            public ElementId ElementId { get; set; }
            public string CategoryName { get; set; }
            public string Source { get; set; }
        }

        private class CurtainElevationGeometryPointResult
        {
            public List<XYZ> Points { get; } = new List<XYZ>();
            public List<CurtainElevationPointRecord> Records { get; } = new List<CurtainElevationPointRecord>();
            public List<ElementId> FallbackElementIds { get; } = new List<ElementId>();
            public HashSet<ElementId> ContributingElementIds { get; } = new HashSet<ElementId>();
            public bool UsedHostWallFallback { get; set; }
            public int GeometryPointCount { get; set; }
            public int ViewSpecificBoundingBoxPointCount { get; set; }
            public int FallbackElementCount { get; set; }
            public int ElementCount { get; set; }

            public string PointSource
            {
                get
                {
                    if (ViewSpecificBoundingBoxPointCount > 0 && (GeometryPointCount > 0 || FallbackElementCount > 0))
                        return "view_bbox_with_fallback";
                    if (ViewSpecificBoundingBoxPointCount > 0)
                        return "view_bbox";
                    if (GeometryPointCount > 0 && FallbackElementCount > 0)
                        return "geometry_with_bbox_fallback";
                    if (GeometryPointCount > 0)
                        return "geometry";
                    return "bbox_fallback";
                }
            }
        }

        private class CurtainElevationLocalExtents
        {
            public XYZ Min { get; set; }
            public XYZ Max { get; set; }
            public CurtainElevationPointRecord MinXRecord { get; set; }
            public CurtainElevationPointRecord MaxXRecord { get; set; }
            public CurtainElevationPointRecord MinYRecord { get; set; }
            public CurtainElevationPointRecord MaxYRecord { get; set; }
            public CurtainElevationPointRecord MinZRecord { get; set; }
            public CurtainElevationPointRecord MaxZRecord { get; set; }
        }

        private class CurtainElevationFarClipResult
        {
            public double DepthFt { get; set; }
            public double MarginFt { get; set; }
            public double NearestTargetFt { get; set; }
            public double FarthestTargetFt { get; set; }
            public string Method { get; set; }
            public string PointSource { get; set; }
            public CurtainElevationPointRecord ExtremeContributor { get; set; }
            public XYZ DepthOrigin { get; set; }
            public XYZ LookDirection { get; set; }
            public double MinCandidateDepthFt { get; set; }
            public double MaxCandidateDepthFt { get; set; }
            public int PositivePointCount { get; set; }
            public string Warning { get; set; }
            public bool CropBoxDepthApplied { get; set; }
            public string CropBoxDepthMethod { get; set; }
            public double ViewOriginLocalZFt { get; set; }
            public double LookDirectionLocalZ { get; set; }
            public double CropBoxMinZAfterFt { get; set; }
            public double CropBoxMaxZAfterFt { get; set; }
            public double CropBoxDepthAfterFt { get; set; }
        }

        private object BuildCurtainElevationCropDiagnosticsForSide(
            Document doc,
            ViewFamilyType elevationType,
            ViewPlan placementView,
            Wall wall,
            XYZ wallMidPoint,
            XYZ exteriorDirection,
            double offsetFt,
            int scale)
        {
            XYZ exterior = FlattenAndNormalize(exteriorDirection);
            if (doc == null || elevationType == null || placementView == null || wall == null || wallMidPoint == null || exterior == null)
                return new { Error = "Missing data required to create temporary crop diagnostic elevation." };

            XYZ markerPoint = wallMidPoint + exterior * (wall.Width / 2.0 + offsetFt);
            XYZ desiredLookDirection = GetCurtainElevationDesiredLookDirection(wallMidPoint, markerPoint);
            if (desiredLookDirection == null)
                return new { Error = "Cannot resolve desired look direction for crop diagnostics." };

            try
            {
                ElevationMarker marker = ElevationMarker.CreateElevationMarker(doc, elevationType.Id, markerPoint, scale);
                ViewSection view = marker.CreateElevation(doc, placementView.Id, 0);
                doc.Regenerate();

                CurtainElevationDirectionResult directionResult = AlignCurtainElevationMarkerByVisualLook(
                    doc, marker, markerPoint, view, desiredLookDirection);
                doc.Regenerate();

                return BuildCurtainElevationCropDiagnostics(doc, wall, view, wallMidPoint, markerPoint, directionResult);
            }
            catch (Exception ex)
            {
                return new { Error = ex.Message };
            }
        }

        private object BuildCurtainElevationCropDiagnostics(
            Document doc,
            Wall wall,
            ViewSection view,
            XYZ wallMidPoint,
            XYZ markerPoint,
            CurtainElevationDirectionResult directionResult)
        {
            if (doc == null || wall == null || view == null)
                return new { Error = "Missing document, wall, or temporary view." };

            BoundingBoxXYZ viewCrop = view.CropBox;
            double? viewerBoundOffsetBefore = GetViewDoubleParameterByBuiltInName(view, "VIEWER_BOUND_OFFSET_FAR");
            CurtainElevationGeometryPointResult pointResult = GetCurtainElevationGeometryPoints(doc, wall, view);
            CurtainElevationGeometryPointResult view2DPointResult = GetCurtainElevationView2DPoints(doc, wall, view);
            Transform viewCropFrame = viewCrop?.Transform;
            Transform view2DFrame = GetCurtainElevationView2DFrame(view, viewCropFrame);
            Transform wallAlignedFrame = GetCurtainElevationWallAlignedCropFrame(wall, wallMidPoint, markerPoint);
            CurtainElevationLocalExtents viewExtents = GetCurtainElevationLocalExtents(pointResult.Records, viewCropFrame);
            CurtainElevationLocalExtents view2DExtents = GetCurtainElevationLocalExtents(view2DPointResult.Records, view2DFrame);
            CurtainElevationLocalExtents view2DCropFrameExtents = ConvertCurtainElevationView2DExtentsToCropFrameExtents(view2DFrame, view2DExtents, viewCropFrame, 0, 0);
            CurtainElevationLocalExtents wallExtents = GetCurtainElevationLocalExtents(pointResult.Records, wallAlignedFrame);
            CurtainElevationFarClipResult farClipResult = CalculateCurtainElevationFarClipDepth(view, viewCropFrame, view2DPointResult.Records, 0);

            bool managerAvailable = false;
            bool? loopValid = null;
            string loopError = null;
            List<XYZ> loopPoints = new List<XYZ>();

            try
            {
                ViewCropRegionShapeManager manager = view.GetCropRegionShapeManager();
                managerAvailable = manager != null;

                if (manager != null && wallAlignedFrame != null && wallExtents?.Min != null && wallExtents?.Max != null)
                {
                    CurveLoop loop = BuildCurtainElevationCandidateCropLoop(view, wallAlignedFrame, wallExtents.Min, wallExtents.Max, loopPoints);
                    loopValid = loop != null && manager.IsCropRegionShapeValid(loop);
                }
            }
            catch (Exception ex)
            {
                loopError = ex.Message;
            }

            var diagnosticFarClipWarnings = new List<string>();
            CurtainElevationCropResult appliedCropResult = ConfigureCurtainElevationCrop(doc, view, wall, wallMidPoint, markerPoint, 0, 0, 0);
            ConfigureCurtainElevationFarClip(view, appliedCropResult, diagnosticFarClipWarnings);
            BoundingBoxXYZ viewCropAfter = view.CropBox;
            double? viewerBoundOffsetAfter = GetViewDoubleParameterByBuiltInName(view, "VIEWER_BOUND_OFFSET_FAR");

            return new
            {
                DirectionDot = Math.Round(directionResult?.DirectionDot ?? -1.0, 4),
                ViewRightDirection = ToCurtainElevationXyz(view.RightDirection),
                ViewUpDirection = ToCurtainElevationXyz(view.UpDirection),
                ViewDirection = ToCurtainElevationXyz(view.ViewDirection),
                ViewCropTransform = ToCurtainElevationTransform(viewCropFrame),
                CropBoxTransform = ToCurtainElevationTransform(viewCropFrame),
                ViewCropMin = ToCurtainElevationXyz(viewCrop?.Min),
                ViewCropMax = ToCurtainElevationXyz(viewCrop?.Max),
                CropBoxMinZBeforeMm = viewCrop?.Min == null ? (double?)null : Math.Round(viewCrop.Min.Z * 304.8, 1),
                CropBoxMaxZBeforeMm = viewCrop?.Max == null ? (double?)null : Math.Round(viewCrop.Max.Z * 304.8, 1),
                CropBoxDepthBeforeMm = viewCrop?.Min == null || viewCrop.Max == null ? (double?)null : Math.Round(Math.Abs(viewCrop.Max.Z - viewCrop.Min.Z) * 304.8, 1),
                CropBoxMinZAfterMm = viewCropAfter?.Min == null ? (double?)null : Math.Round(viewCropAfter.Min.Z * 304.8, 1),
                CropBoxMaxZAfterMm = viewCropAfter?.Max == null ? (double?)null : Math.Round(viewCropAfter.Max.Z * 304.8, 1),
                CropBoxDepthAfterMm = viewCropAfter?.Min == null || viewCropAfter.Max == null ? (double?)null : Math.Round(Math.Abs(viewCropAfter.Max.Z - viewCropAfter.Min.Z) * 304.8, 1),
                ViewerBoundOffsetBeforeMm = viewerBoundOffsetBefore.HasValue ? Math.Round(viewerBoundOffsetBefore.Value * 304.8, 1) : (double?)null,
                ViewerBoundOffsetAfterMm = viewerBoundOffsetAfter.HasValue ? Math.Round(viewerBoundOffsetAfter.Value * 304.8, 1) : (double?)null,
                ExpectedDepthMm = Math.Round(appliedCropResult.FarClipRequestedDepthFt * 304.8, 1),
                DepthDeltaMm = Math.Round(appliedCropResult.FarClipDepthDeltaFt * 304.8, 1),
                FarClipPass = appliedCropResult.FarClipPass,
                FarClipDiagnosticsWarnings = diagnosticFarClipWarnings,
                WallAlignedFrame = ToCurtainElevationTransform(wallAlignedFrame),
                GeometryPointSource = pointResult.PointSource,
                GeometryPointCount = pointResult.Points.Count,
                GeometryFallbackElementCount = pointResult.FallbackElementCount,
                UsedHostWallFallback = pointResult.UsedHostWallFallback,
                PointSourceCountsByCategory = GetCurtainElevationPointSourceCountsByCategory(pointResult.Records),
                FallbackElementIds = pointResult.FallbackElementIds.Select(id => id.GetIdValue()).ToList(),
                GeometryLocalExtentsInActualCropFrame = ToCurtainElevationLocalExtents(viewExtents),
                GeometryLocalExtentsInViewCropFrame = ToCurtainElevationLocalExtents(viewExtents),
                GeometryLocalExtentsInWallAlignedFrame = ToCurtainElevationLocalExtents(wallExtents),
                ExtremeContributors = ToCurtainElevationExtremeContributors(viewExtents),
                View2DCropMethod = "view_2d_visible_bounds",
                View2DOrigin = ToCurtainElevationPointMm(view2DFrame?.Origin),
                View2DRightDirection = ToCurtainElevationXyz(view2DFrame?.BasisX),
                View2DUpDirection = ToCurtainElevationXyz(view2DFrame?.BasisY),
                View2DPointSource = view2DPointResult.PointSource,
                View2DPointCount = view2DPointResult.Points.Count,
                View2DPointSourceCountsByCategory = GetCurtainElevationPointSourceCountsByCategory(view2DPointResult.Records),
                View2DFallbackElementIds = view2DPointResult.FallbackElementIds.Select(id => id.GetIdValue()).ToList(),
                View2DLocalExtents = ToCurtainElevationLocalExtents(view2DExtents),
                View2DConvertedCropFrameExtents = ToCurtainElevationLocalExtents(view2DCropFrameExtents),
                View2DExtremeContributors = ToCurtainElevationExtremeContributors(view2DExtents),
                FarClipMethod = farClipResult?.Method,
                FarClipDepthMm = farClipResult == null ? (double?)null : Math.Round(farClipResult.DepthFt * 304.8, 1),
                FarClipRequestedDepthMm = farClipResult == null ? (double?)null : Math.Round(farClipResult.DepthFt * 304.8, 1),
                FarClipDepthOrigin = ToCurtainElevationPointMm(farClipResult?.DepthOrigin),
                FarClipLookDirection = ToCurtainElevationXyz(farClipResult?.LookDirection),
                FarClipMinCandidateDepthMm = farClipResult == null ? (double?)null : Math.Round(farClipResult.MinCandidateDepthFt * 304.8, 1),
                FarClipMaxCandidateDepthMm = farClipResult == null ? (double?)null : Math.Round(farClipResult.MaxCandidateDepthFt * 304.8, 1),
                FarClipPositivePointCount = farClipResult?.PositivePointCount,
                FarClipWarning = farClipResult?.Warning,
                FarClipCropBoxDepthApplied = farClipResult?.CropBoxDepthApplied,
                FarClipCropBoxDepthMethod = farClipResult?.CropBoxDepthMethod,
                FarClipViewOriginLocalZMm = farClipResult == null ? (double?)null : Math.Round(farClipResult.ViewOriginLocalZFt * 304.8, 1),
                FarClipLookDirectionLocalZ = farClipResult == null ? (double?)null : Math.Round(farClipResult.LookDirectionLocalZ, 6),
                FarClipCropBoxMinZAfterMm = farClipResult == null ? (double?)null : Math.Round(farClipResult.CropBoxMinZAfterFt * 304.8, 1),
                FarClipCropBoxMaxZAfterMm = farClipResult == null ? (double?)null : Math.Round(farClipResult.CropBoxMaxZAfterFt * 304.8, 1),
                FarClipCropBoxDepthAfterMm = farClipResult == null ? (double?)null : Math.Round(farClipResult.CropBoxDepthAfterFt * 304.8, 1),
                FarClipMarginMm = farClipResult == null ? (double?)null : Math.Round(farClipResult.MarginFt * 304.8, 1),
                FarClipNearestTargetMm = farClipResult == null ? (double?)null : Math.Round(farClipResult.NearestTargetFt * 304.8, 1),
                FarClipFarthestTargetMm = farClipResult == null ? (double?)null : Math.Round(farClipResult.FarthestTargetFt * 304.8, 1),
                FarClipPointSource = farClipResult?.PointSource,
                FarClipExtremeContributor = ToCurtainElevationPointContributor(farClipResult?.ExtremeContributor),
                CandidateCropLoopPoints = loopPoints.Select(ToCurtainElevationPointMm).ToList(),
                CandidateCropLoopIsValid = loopValid,
                CandidateCropLoopError = loopError,
                CropRegionShapeManagerAvailable = managerAvailable,
                Note = "Diagnostic only; crop shape is not applied."
            };
        }

        private CurtainElevationLocalExtents GetCurtainElevationLocalExtents(List<CurtainElevationPointRecord> records, Transform frame)
        {
            if (records == null || records.Count == 0 || frame == null)
                return null;

            Transform inverse = frame.Inverse;
            double minX = double.MaxValue;
            double minY = double.MaxValue;
            double minZ = double.MaxValue;
            double maxX = double.MinValue;
            double maxY = double.MinValue;
            double maxZ = double.MinValue;

            CurtainElevationPointRecord minXRecord = null;
            CurtainElevationPointRecord minYRecord = null;
            CurtainElevationPointRecord minZRecord = null;
            CurtainElevationPointRecord maxXRecord = null;
            CurtainElevationPointRecord maxYRecord = null;
            CurtainElevationPointRecord maxZRecord = null;

            foreach (CurtainElevationPointRecord record in records)
            {
                XYZ point = record?.Point;
                if (point == null)
                    continue;

                XYZ local = inverse.OfPoint(point);
                if (local.X < minX) { minX = local.X; minXRecord = record; }
                if (local.Y < minY) { minY = local.Y; minYRecord = record; }
                if (local.Z < minZ) { minZ = local.Z; minZRecord = record; }
                if (local.X > maxX) { maxX = local.X; maxXRecord = record; }
                if (local.Y > maxY) { maxY = local.Y; maxYRecord = record; }
                if (local.Z > maxZ) { maxZ = local.Z; maxZRecord = record; }
            }

            if (minX == double.MaxValue)
                return null;

            return new CurtainElevationLocalExtents
            {
                Min = new XYZ(minX, minY, minZ),
                Max = new XYZ(maxX, maxY, maxZ),
                MinXRecord = minXRecord,
                MaxXRecord = maxXRecord,
                MinYRecord = minYRecord,
                MaxYRecord = maxYRecord,
                MinZRecord = minZRecord,
                MaxZRecord = maxZRecord
            };
        }

        private object ToCurtainElevationLocalExtents(CurtainElevationLocalExtents extents)
        {
            if (extents == null)
                return null;

            return new
            {
                MinFt = ToCurtainElevationXyz(extents.Min),
                MaxFt = ToCurtainElevationXyz(extents.Max),
                MinMm = ToCurtainElevationPointMm(extents.Min),
                MaxMm = ToCurtainElevationPointMm(extents.Max)
            };
        }

        private object ToCurtainElevationExtremeContributors(CurtainElevationLocalExtents extents)
        {
            if (extents == null)
                return null;

            return new
            {
                MinX = ToCurtainElevationPointContributor(extents.MinXRecord),
                MaxX = ToCurtainElevationPointContributor(extents.MaxXRecord),
                MinY = ToCurtainElevationPointContributor(extents.MinYRecord),
                MaxY = ToCurtainElevationPointContributor(extents.MaxYRecord),
                MinZ = ToCurtainElevationPointContributor(extents.MinZRecord),
                MaxZ = ToCurtainElevationPointContributor(extents.MaxZRecord)
            };
        }

        private object ToCurtainElevationPointContributor(CurtainElevationPointRecord record)
        {
            if (record == null)
                return null;

            return new
            {
                ElementId = record.ElementId?.GetIdValue() ?? 0,
                Category = record.CategoryName,
                Source = record.Source,
                Point = ToCurtainElevationPointMm(record.Point)
            };
        }

        private object GetCurtainElevationPointSourceCountsByCategory(List<CurtainElevationPointRecord> records)
        {
            if (records == null)
                return new List<object>();

            return records
                .GroupBy(r => new { Category = r.CategoryName ?? "(none)", Source = r.Source ?? "(unknown)" })
                .Select(g => new
                {
                    g.Key.Category,
                    g.Key.Source,
                    Count = g.Count(),
                    ElementCount = g.Select(r => r.ElementId).Where(id => id != null).Distinct().Count()
                })
                .OrderBy(x => x.Category)
                .ThenBy(x => x.Source)
                .ToList();
        }

        private object ToCurtainElevationTransform(Transform transform)
        {
            if (transform == null)
                return null;

            return new
            {
                Origin = ToCurtainElevationPointMm(transform.Origin),
                BasisX = ToCurtainElevationXyz(transform.BasisX),
                BasisY = ToCurtainElevationXyz(transform.BasisY),
                BasisZ = ToCurtainElevationXyz(transform.BasisZ)
            };
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
            double offsetFt,
            double stackOffsetFt)
        {
            var result = new CurtainElevationDimensionResult();
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
            double minX = cropResult.View2DMin.X + xShift;
            double maxX = cropResult.View2DMax.X + xShift;
            double minY = cropResult.View2DMin.Y + yShift;
            double maxY = cropResult.View2DMax.Y + yShift;
            if (maxX - minX <= 1e-6 || maxY - minY <= 1e-6)
            {
                result.Status = "failed";
                result.Warnings.Add("dimension skipped: view 2D bounds are too small.");
                result.FailedCount = 4;
                return result;
            }

            double topTotalY = maxY + offsetFt;
            double topGridY = topTotalY + stackOffsetFt;
            double rightTotalX = maxX + offsetFt;
            double rightGridX = rightTotalX + stackOffsetFt;
            List<CurtainElevationGeometryReference> geometryReferences = CollectCurtainElevationGeometryReferences(doc, wall, view, frame, minX, maxX, minY, maxY);
            List<CurtainElevationGeometryReference> gridLineReferences = CollectCurtainElevationGridLineReferences(doc, wall, view, frame, minX, maxX, minY, maxY);
            result.GeometryReferenceCount = geometryReferences.Count + gridLineReferences.Count;
            result.CurtainGridLineCount = wall.CurtainGrid.GetUGridLineIds().Count + wall.CurtainGrid.GetVGridLineIds().Count;
            result.CurtainGridLineReferenceCount = gridLineReferences.Count;
            if (result.CurtainGridLineReferenceCount < result.CurtainGridLineCount)
                result.CurtainGridLineReferenceFailures.Add($"Only {result.CurtainGridLineReferenceCount} of {result.CurtainGridLineCount} CurtainGridLine elements exposed a usable aligned geometry reference.");
            result.CurtainGridLineReferenceSamples.AddRange(gridLineReferences.Select(r => (object)new
            {
                GridLineId = r.CurtainGridLineId?.GetIdValue(),
                GridLineOrientation = r.IsVertical ? "vertical" : (r.IsHorizontal ? "horizontal" : "other"),
                GeometryObjectType = r.GeometryObjectType,
                ReferenceAvailable = r.Reference != null,
                StableRepresentation = r.StableRepresentation,
                ProjectedCoordinate = Math.Round((r.IsVertical ? r.CenterX : r.CenterY) * 304.8, 1),
                LengthMm = Math.Round(r.Length * 304.8, 1),
                SelectedForDimension = r.SelectedForDimension,
                SelectionReason = r.SelectionReason
            }));
            result.GeometryReferenceCategories = geometryReferences
                .Select(r => r.CategoryName)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct()
                .OrderBy(s => s)
                .ToList();

            List<CurtainElevationGeometryReference> totalWidthRefs = SelectCurtainElevationBoundaryReferences(geometryReferences, "horizontal", minX, maxX, minY, maxY);
            if (TryCreateCurtainElevationDimensionChain(doc, view, frame, dimensionType, "horizontal", new List<double> { minX, maxX }, totalWidthRefs, minY, maxY, topTotalY, result, false, out ElementId totalWidthId, out string totalWidthSource, out string totalWidthReason))
            {
                result.TotalWidthDimensionId = totalWidthId;
                result.TotalWidthDimensionReferenceSource = totalWidthSource;
                result.CreatedCount++;
            }
            else
            {
                result.FailedCount++;
                result.TotalWidthDimensionReferenceSource = "failed";
                result.Warnings.Add("total width dimension failed: " + totalWidthReason);
            }

            List<CurtainElevationGeometryReference> totalHeightRefs = SelectCurtainElevationBoundaryReferences(geometryReferences, "vertical", minX, maxX, minY, maxY);
            if (TryCreateCurtainElevationDimensionChain(doc, view, frame, dimensionType, "vertical", new List<double> { minY, maxY }, totalHeightRefs, minX, maxX, rightTotalX, result, false, out ElementId totalHeightId, out string totalHeightSource, out string totalHeightReason))
            {
                result.TotalHeightDimensionId = totalHeightId;
                result.TotalHeightDimensionReferenceSource = totalHeightSource;
                result.CreatedCount++;
            }
            else
            {
                result.FailedCount++;
                result.TotalHeightDimensionReferenceSource = "failed";
                result.Warnings.Add("total height dimension failed: " + totalHeightReason);
            }

            List<double> verticalGridXs = GetCurtainElevationGridCoordinates(doc, wall, frame, "vertical", minX, maxX, minY, maxY);
            if (verticalGridXs.Count >= 3)
            {
                List<CurtainElevationGeometryReference> verticalGridRefs = SelectCurtainElevationGridDimensionReferences(geometryReferences, gridLineReferences, "horizontal", verticalGridXs);
                if (TryCreateCurtainElevationDimensionChain(doc, view, frame, dimensionType, "horizontal", verticalGridXs, verticalGridRefs, minY, maxY, topGridY, result, true, out ElementId horizontalGridId, out string horizontalGridSource, out string horizontalGridReason))
                {
                    result.HorizontalGridDimensionId = horizontalGridId;
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
                if (TryCreateCurtainElevationDimensionChain(doc, view, frame, dimensionType, "vertical", horizontalGridYs, horizontalGridRefs, minX, maxX, rightGridX, result, true, out ElementId verticalGridId, out string verticalGridSource, out string verticalGridReason))
                {
                    result.VerticalGridDimensionId = verticalGridId;
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

        private void VerifyCurtainElevationDimensionResult(Document doc, View view, CurtainElevationDimensionResult result)
        {
            if (doc == null || view == null || result == null)
                return;

            var ids = new[]
            {
                result.TotalWidthDimensionId,
                result.HorizontalGridDimensionId,
                result.TotalHeightDimensionId,
                result.VerticalGridDimensionId
            };

            result.VerifiedCount = 0;
            foreach (ElementId id in ids)
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
                ReferenceCount = geometryReferences?.Count ?? 0
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

                if (geometryReferences == null || geometryReferences.Count < distinct.Count)
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
                attempt.DimensionId = dimension.Id;
                attempt.OwnerViewId = dimension.OwnerViewId;
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
                Method = "reference_plane_fallback"
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
                attempt.DimensionId = dimension.Id;
                attempt.OwnerViewId = dimension.OwnerViewId;
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
                FailureMessage = attempt.FailureMessage
            };
        }

        private bool TryCreateCurtainElevationDimensionChain(
            Document doc,
            View view,
            Transform frame,
            DimensionType dimensionType,
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
                    out string geometryReason))
                {
                    referenceSource = "geometry_reference";
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

                aggregate.DimensionFallbackReason = AppendCurtainElevationWarning(
                    aggregate.DimensionFallbackReason,
                    $"{axis} grid dimension used invisible detail curve fallback from curtain grid coordinates: {geometryReason}");

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
                    referenceSource = "detail_curve_fallback_from_curtain_grid_coordinates";
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

            try
            {
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

                ApplyDimensionType(dimension, dimensionType);

                GraphicsStyle invisibleLineStyle = TryFindExistingInvisibleLineStyle(doc);
                bool invisibleLineStyleApplied = invisibleLineStyle != null;
                foreach (DetailCurve detailCurve in createdReferenceCurves)
                {
                    aggregate.ReferenceCurveIds.Add(detailCurve.Id);

                    if (invisibleLineStyle == null)
                    {
                        invisibleLineStyleApplied = false;
                        continue;
                    }

                    try
                    {
                        detailCurve.LineStyle = invisibleLineStyle;
                    }
                    catch
                    {
                        invisibleLineStyleApplied = false;
                    }
                }

                if (!invisibleLineStyleApplied)
                {
                    aggregate.Warnings.Add("Grid dimension detail-curve fallback succeeded, but Revit did not expose/apply BuiltInCategory.OST_InvisibleLines to the helper curves.");
                    aggregate.DimensionFallbackReason = AppendCurtainElevationWarning(
                        aggregate.DimensionFallbackReason,
                        "detail curve fallback dimension succeeded, invisible line style was not applied.");
                }

                LastCurtainElevationDimensionTypeId = dimensionType.Id.GetIdValue();
                dimensionId = dimension.Id;
                return true;
            }
            catch (Exception ex)
            {
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
            out string reason)
        {
            dimensionId = null;
            reason = null;

            try
            {
                if (geometryReferences == null || geometryReferences.Count < coordinates.Count)
                {
                    reason = $"not enough geometry references. Need {coordinates.Count}, got {geometryReferences?.Count ?? 0}.";
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

                ApplyDimensionType(dimension, dimensionType);
                LastCurtainElevationDimensionTypeId = dimensionType.Id.GetIdValue();
                dimensionId = dimension.Id;
                return true;
            }
            catch (Exception ex)
            {
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
            double maxY)
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

            foreach (ElementId id in GetCurtainElevationElementIds(wall, includeHostWall: false))
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
                }
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
            double maxY)
        {
            double tolerance = 25.0 / 304.8;
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

                CurtainElevationGeometryReference match = candidates
                    .Where(r => Math.Abs((dimensionAxis == "horizontal" ? r.CenterX : r.CenterY) - coordinate) <= tolerance)
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
            double maxY)
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
                IncludeNonVisibleObjects = false,
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

                    var candidates = new List<CurtainElevationGeometryReference>();
                    // Prefer native CurtainGridLine curve references before solid geometry.
                    try
                    {
                        AddCurtainElevationGeometryReference(fullCurve.Reference, fullCurve, candidates, frame, Transform.Identity, gridLine);
                        foreach (Curve segment in gridLine.AllSegmentCurves ?? new List<Curve>())
                            AddCurtainElevationGeometryReference(segment?.Reference, segment, candidates, frame, Transform.Identity, gridLine);
                    }
                    catch
                    {
                        // Some Revit versions expose FullCurve but not its Reference.
                    }
                    GeometryElement geometry = gridLine.get_Geometry(options);
                    CollectCurtainElevationGeometryReferences(geometry, candidates, frame, Transform.Identity, gridLine);

                    candidates = candidates
                        .Where(r => r.Reference != null && r.Length > tolerance)
                        .Where(r => r.MaxX >= minX - tolerance && r.MinX <= maxX + tolerance)
                        .Where(r => r.MaxY >= minY - tolerance && r.MinY <= maxY + tolerance)
                        .Where(r =>
                        {
                            XYZ direction = frame.Inverse.OfVector(r.End - r.Start);
                            if (direction.GetLength() < tolerance)
                                return false;
                            double alignment = Math.Abs(direction.Normalize().DotProduct(fullDirection));
                            return alignment >= 0.98;
                        })
                        .OrderByDescending(r => r.Length)
                        .ToList();

                    CurtainElevationGeometryReference best = candidates.FirstOrDefault();
                    if (best == null)
                        continue;

                    best.CurtainGridLineId = id;
                    best.GeometryObjectType = best.GeometryObjectType ?? "Curve";
                    best.SelectedForDimension = true;
                    best.SelectionReason = "longest_reference_aligned_with_full_curve";
                    try
                    {
                        best.StableRepresentation = best.Reference.ConvertToStableRepresentation(doc);
                    }
                    catch
                    {
                        best.StableRepresentation = null;
                    }
                    selected.Add(best);
                }
                catch
                {
                    // A grid line can exist without reference-bearing project geometry.
                }
            }

            return selected
                .GroupBy(r => r.CurtainGridLineId ?? r.ElementId)
                .Select(g => g.OrderByDescending(r => r.Length).First())
                .ToList();
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

        private CurtainElevationCropResult ConfigureCurtainElevationCrop(Document doc, ViewSection view, Wall wall, XYZ wallMidPoint, XYZ markerPoint, double horizontalMarginFt, double verticalMarginFt, double fallbackDepthFt)
        {
            var result = new CurtainElevationCropResult
            {
                FarClipDepthFt = fallbackDepthFt
            };

            if (view == null || wall == null)
                return result;

            BoundingBoxXYZ crop = view.CropBox;
            if (crop == null)
                return result;

            CurtainElevationGeometryPointResult pointResult = GetCurtainElevationView2DPoints(doc, wall, view);
            List<XYZ> points = pointResult.Points;
            result.PointSource = pointResult.PointSource;
            result.PointCount = points.Count;
            result.FallbackElementCount = pointResult.FallbackElementCount;
            result.UsedHostWallFallback = pointResult.UsedHostWallFallback;
            result.ContributingElementIds = pointResult.ContributingElementIds.Select(id => id.GetIdValue()).ToList();
            result.FallbackElementIds = pointResult.FallbackElementIds.Select(id => id.GetIdValue()).ToList();

            if (pointResult.Records.Count == 0)
                return result;

            Transform cropFrame = crop.Transform;
            Transform view2DFrame = GetCurtainElevationView2DFrame(view, cropFrame);
            result.FrameSource = "view_2d_visible_bounds";
            result.RightDirection = view2DFrame.BasisX;
            result.UpDirection = view2DFrame.BasisY;
            result.DepthDirection = cropFrame.BasisZ;
            result.View2DOrigin = view2DFrame.Origin;
            result.View2DRightDirection = view2DFrame.BasisX;
            result.View2DUpDirection = view2DFrame.BasisY;
            result.View2DPointCount = pointResult.Records.Count;
            result.View2DSource = pointResult.PointSource;

            CurtainElevationLocalExtents view2DExtents = GetCurtainElevationLocalExtents(pointResult.Records, view2DFrame);
            if (view2DExtents == null)
                return result;

            CurtainElevationLocalExtents cropFrameExtents = ConvertCurtainElevationView2DExtentsToCropFrameExtents(
                view2DFrame,
                view2DExtents,
                cropFrame,
                horizontalMarginFt,
                verticalMarginFt);
            if (cropFrameExtents == null)
                return result;

            CurtainElevationFarClipResult farClipResult = CalculateCurtainElevationFarClipDepth(
                view,
                cropFrame,
                pointResult.Records,
                fallbackDepthFt);
            if (farClipResult != null)
            {
                result.FarClipDepthFt = farClipResult.DepthFt;
                result.FarClipMethod = farClipResult.Method;
                result.FarClipRequestedDepthFt = farClipResult.DepthFt;
                result.FarClipDepthOrigin = farClipResult.DepthOrigin;
                result.FarClipLookDirection = farClipResult.LookDirection;
                result.FarClipMinCandidateDepthFt = farClipResult.MinCandidateDepthFt;
                result.FarClipMaxCandidateDepthFt = farClipResult.MaxCandidateDepthFt;
                result.FarClipPositivePointCount = farClipResult.PositivePointCount;
                result.FarClipWarning = farClipResult.Warning;
                result.FarClipMarginFt = farClipResult.MarginFt;
                result.FarClipNearestTargetFt = farClipResult.NearestTargetFt;
                result.FarClipFarthestTargetFt = farClipResult.FarthestTargetFt;
                result.FarClipPointSource = farClipResult.PointSource;
                result.FarClipExtremeContributor = ToCurtainElevationPointContributor(farClipResult.ExtremeContributor);
                result.FarClipCropBoxDepthApplied = farClipResult.CropBoxDepthApplied;
                result.FarClipCropBoxDepthMethod = farClipResult.CropBoxDepthMethod;
                result.FarClipViewOriginLocalZFt = farClipResult.ViewOriginLocalZFt;
                result.FarClipLookDirectionLocalZ = farClipResult.LookDirectionLocalZ;
                result.FarClipCropBoxMinZAfterFt = farClipResult.CropBoxMinZAfterFt;
                result.FarClipCropBoxMaxZAfterFt = farClipResult.CropBoxMaxZAfterFt;
                result.FarClipCropBoxDepthAfterFt = farClipResult.CropBoxDepthAfterFt;
            }
            result.LocalMin = cropFrameExtents.Min;
            result.LocalMax = cropFrameExtents.Max;
            result.FarClipCropBoxMinZBeforeFt = result.LocalMin.Z;
            result.FarClipCropBoxMaxZBeforeFt = result.LocalMax.Z;
            if (farClipResult?.CropBoxDepthApplied == true)
            {
                result.LocalMin = new XYZ(result.LocalMin.X, result.LocalMin.Y, farClipResult.CropBoxMinZAfterFt);
                result.LocalMax = new XYZ(result.LocalMax.X, result.LocalMax.Y, farClipResult.CropBoxMaxZAfterFt);
            }
            result.ExtremeContributors = ToCurtainElevationExtremeContributors(view2DExtents);
            result.View2DMin = new XYZ(view2DExtents.Min.X - horizontalMarginFt, view2DExtents.Min.Y - verticalMarginFt, view2DExtents.Min.Z);
            result.View2DMax = new XYZ(view2DExtents.Max.X + horizontalMarginFt, view2DExtents.Max.Y + verticalMarginFt, view2DExtents.Max.Z);
            result.View2DExtremeContributors = ToCurtainElevationExtremeContributors(view2DExtents);

            view.CropBoxActive = true;
            view.CropBoxVisible = false;
            view.CropBox = new BoundingBoxXYZ
            {
                Transform = cropFrame,
                Min = result.LocalMin,
                Max = result.LocalMax
            };

            return result;
        }

        private CurtainElevationFarClipResult CalculateCurtainElevationFarClipDepth(
            ViewSection view,
            Transform cropFrame,
            List<CurtainElevationPointRecord> records,
            double fallbackDepthFt)
        {
            const double farClipMarginFt = 50.0 / 304.8;
            const double minimumDepthFt = 50.0 / 304.8;

            if (records == null || records.Count == 0)
            {
                return new CurtainElevationFarClipResult
                {
                    DepthFt = Math.Max(fallbackDepthFt, minimumDepthFt),
                    MarginFt = farClipMarginFt,
                    Method = "fallback_depth_no_target_points",
                    PointSource = "none"
                };
            }

            XYZ origin = view?.Origin;
            XYZ lookDirection = GetCurtainElevationVisualLookDirection(view);
            if (origin == null || lookDirection == null)
            {
                return new CurtainElevationFarClipResult
                {
                    DepthFt = Math.Max(fallbackDepthFt, minimumDepthFt),
                    MarginFt = farClipMarginFt,
                    Method = "fallback_depth_no_view_origin_or_look_direction",
                    PointSource = "none",
                    DepthOrigin = origin,
                    LookDirection = lookDirection,
                    Warning = "Cannot resolve view origin or visual look direction; used depthMm fallback."
                };
            }

            bool canApplyCropBoxDepth = false;
            double viewOriginLocalZ = 0;
            double lookDirectionLocalZ = 0;
            string cropBoxDepthWarning = null;
            if (cropFrame != null)
            {
                try
                {
                    Transform inverse = cropFrame.Inverse;
                    viewOriginLocalZ = inverse.OfPoint(origin).Z;
                    lookDirectionLocalZ = inverse.OfVector(lookDirection).Z;
                    canApplyCropBoxDepth = Math.Abs(lookDirectionLocalZ) > 1e-9;
                }
                catch (Exception ex)
                {
                    cropBoxDepthWarning = $"Cannot project far clip depth into crop box local Z: {ex.Message}";
                }
            }
            else
            {
                cropBoxDepthWarning = "Cannot apply crop box depth because crop frame is null.";
            }

            double minDepth = double.MaxValue;
            double maxDepth = double.MinValue;
            double maxPositiveDepth = double.MinValue;
            double maxAbsDepth = double.MinValue;
            int positiveCount = 0;
            CurtainElevationPointRecord maxPositiveRecord = null;
            CurtainElevationPointRecord maxAbsRecord = null;

            foreach (CurtainElevationPointRecord record in records)
            {
                XYZ point = record?.Point;
                if (point == null)
                    continue;

                double depth = (point - origin).DotProduct(lookDirection);
                minDepth = Math.Min(minDepth, depth);
                maxDepth = Math.Max(maxDepth, depth);

                if (depth > 0)
                {
                    positiveCount++;
                    if (depth > maxPositiveDepth)
                    {
                        maxPositiveDepth = depth;
                        maxPositiveRecord = record;
                    }
                }

                double absDepth = Math.Abs(depth);
                if (absDepth > maxAbsDepth)
                {
                    maxAbsDepth = absDepth;
                    maxAbsRecord = record;
                }
            }

            if (maxDepth == double.MinValue)
            {
                return new CurtainElevationFarClipResult
                {
                    DepthFt = Math.Max(fallbackDepthFt, minimumDepthFt),
                    MarginFt = farClipMarginFt,
                    Method = "fallback_depth_no_target_points",
                    PointSource = "none",
                    DepthOrigin = origin,
                    LookDirection = lookDirection,
                    Warning = "No valid target points after filtering; used depthMm fallback."
                };
            }

            bool hasPositiveDepth = positiveCount > 0;
            double targetDepthFt = hasPositiveDepth ? maxPositiveDepth : maxAbsDepth;
            CurtainElevationPointRecord extremeRecord = hasPositiveDepth ? maxPositiveRecord : maxAbsRecord;
            string warning = hasPositiveDepth
                ? null
                : "All target point depths were non-positive from view.Origin along visual look direction; used absolute max depth fallback.";
            warning = AppendCurtainElevationWarning(warning, cropBoxDepthWarning);
            double depthFt = Math.Max(targetDepthFt + farClipMarginFt, minimumDepthFt);
            double cropBoxMinZAfter = 0;
            double cropBoxMaxZAfter = 0;
            if (canApplyCropBoxDepth)
            {
                if (lookDirectionLocalZ >= 0)
                {
                    cropBoxMinZAfter = viewOriginLocalZ;
                    cropBoxMaxZAfter = viewOriginLocalZ + depthFt;
                }
                else
                {
                    cropBoxMinZAfter = viewOriginLocalZ - depthFt;
                    cropBoxMaxZAfter = viewOriginLocalZ;
                }

                if (cropBoxMinZAfter > cropBoxMaxZAfter)
                {
                    double temp = cropBoxMinZAfter;
                    cropBoxMinZAfter = cropBoxMaxZAfter;
                    cropBoxMaxZAfter = temp;
                }
            }
            else
            {
                warning = AppendCurtainElevationWarning(warning, "Crop box Z projection is unavailable; VIEWER_BOUND_OFFSET_FAR was still set.");
            }

            return new CurtainElevationFarClipResult
            {
                DepthFt = depthFt,
                MarginFt = farClipMarginFt,
                NearestTargetFt = minDepth,
                FarthestTargetFt = maxDepth,
                Method = hasPositiveDepth ? "view_origin_to_target_max_depth" : "view_origin_to_target_abs_depth_fallback",
                PointSource = extremeRecord?.Source,
                ExtremeContributor = extremeRecord,
                DepthOrigin = origin,
                LookDirection = lookDirection,
                MinCandidateDepthFt = minDepth,
                MaxCandidateDepthFt = maxDepth,
                PositivePointCount = positiveCount,
                Warning = warning,
                CropBoxDepthApplied = false,
                CropBoxDepthMethod = "diagnostic_only_viewer_bound_offset_controls_far_clip",
                ViewOriginLocalZFt = viewOriginLocalZ,
                LookDirectionLocalZ = lookDirectionLocalZ,
                CropBoxMinZAfterFt = cropBoxMinZAfter,
                CropBoxMaxZAfterFt = cropBoxMaxZAfter,
                CropBoxDepthAfterFt = canApplyCropBoxDepth ? Math.Abs(cropBoxMaxZAfter - cropBoxMinZAfter) : 0
            };
        }

        private Transform GetCurtainElevationView2DFrame(ViewSection view, Transform fallbackCropFrame)
        {
            Transform frame = Transform.Identity;
            frame.Origin = fallbackCropFrame?.Origin ?? view?.Origin ?? XYZ.Zero;
            frame.BasisX = NormalizeOrFallback(view?.RightDirection, fallbackCropFrame?.BasisX ?? XYZ.BasisX);
            frame.BasisY = NormalizeOrFallback(view?.UpDirection, fallbackCropFrame?.BasisY ?? XYZ.BasisZ);
            frame.BasisZ = NormalizeOrFallback(fallbackCropFrame?.BasisZ, view?.ViewDirection ?? XYZ.BasisY);
            return frame;
        }

        private XYZ NormalizeOrFallback(XYZ value, XYZ fallback)
        {
            try
            {
                if (value != null && value.GetLength() > 1e-9)
                    return value.Normalize();
            }
            catch
            {
                // Fall through to fallback.
            }

            try
            {
                if (fallback != null && fallback.GetLength() > 1e-9)
                    return fallback.Normalize();
            }
            catch
            {
                // Fall through to basis X.
            }

            return XYZ.BasisX;
        }

        private CurtainElevationLocalExtents ConvertCurtainElevationView2DExtentsToCropFrameExtents(
            Transform view2DFrame,
            CurtainElevationLocalExtents view2DExtents,
            Transform cropFrame,
            double horizontalMarginFt,
            double verticalMarginFt)
        {
            if (view2DFrame == null || view2DExtents?.Min == null || view2DExtents.Max == null || cropFrame == null)
                return null;

            double minX = view2DExtents.Min.X - horizontalMarginFt;
            double maxX = view2DExtents.Max.X + horizontalMarginFt;
            double minY = view2DExtents.Min.Y - verticalMarginFt;
            double maxY = view2DExtents.Max.Y + verticalMarginFt;
            double minZ = view2DExtents.Min.Z;
            double maxZ = view2DExtents.Max.Z;

            var cornerRecords = new List<CurtainElevationPointRecord>();
            foreach (double x in new[] { minX, maxX })
            {
                foreach (double y in new[] { minY, maxY })
                {
                    foreach (double z in new[] { minZ, maxZ })
                    {
                        cornerRecords.Add(new CurtainElevationPointRecord
                        {
                            Point = view2DFrame.OfPoint(new XYZ(x, y, z)),
                            Source = "view_2d_crop_corner"
                        });
                    }
                }
            }

            return GetCurtainElevationLocalExtents(cornerRecords, cropFrame);
        }

        private void ApplyCurtainElevationCropRegionShape(
            ViewSection view,
            Transform cropFrame,
            XYZ localMin,
            XYZ localMax,
            CurtainElevationCropResult result)
        {
            if (view == null || cropFrame == null || localMin == null || localMax == null || result == null)
                return;

            try
            {
                BoundingBoxXYZ currentCrop = view.CropBox;
                Transform viewFrame = currentCrop?.Transform;
                XYZ planeOrigin = viewFrame?.Origin ?? cropFrame.Origin;
                XYZ planeNormal = FlattenAndNormalize(viewFrame?.BasisZ ?? cropFrame.BasisZ);
                if (planeNormal == null)
                {
                    result.RegionShapeFallbackReason = "?��??�斷 crop region plane normal";
                    return;
                }

                XYZ p0 = ProjectCurtainElevationPointToPlane(cropFrame.OfPoint(new XYZ(localMin.X, localMin.Y, 0)), planeOrigin, planeNormal);
                XYZ p1 = ProjectCurtainElevationPointToPlane(cropFrame.OfPoint(new XYZ(localMax.X, localMin.Y, 0)), planeOrigin, planeNormal);
                XYZ p2 = ProjectCurtainElevationPointToPlane(cropFrame.OfPoint(new XYZ(localMax.X, localMax.Y, 0)), planeOrigin, planeNormal);
                XYZ p3 = ProjectCurtainElevationPointToPlane(cropFrame.OfPoint(new XYZ(localMin.X, localMax.Y, 0)), planeOrigin, planeNormal);

                var loop = new CurveLoop();
                loop.Append(Line.CreateBound(p0, p1));
                loop.Append(Line.CreateBound(p1, p2));
                loop.Append(Line.CreateBound(p2, p3));
                loop.Append(Line.CreateBound(p3, p0));

                ViewCropRegionShapeManager manager = view.GetCropRegionShapeManager();
                if (manager == null)
                {
                    result.RegionShapeFallbackReason = "ViewCropRegionShapeManager unavailable";
                    return;
                }

                if (!manager.IsCropRegionShapeValid(loop))
                {
                    result.RegionShapeFallbackReason = "wall-aligned crop loop is not valid for this view";
                    return;
                }

                result.RegionShapeApplied = false;
                result.RegionShapeFallbackReason = "disabled_for_diagnostics";
            }
            catch (Exception ex)
            {
                result.RegionShapeFallbackReason = ex.Message;
            }
        }

        private CurveLoop BuildCurtainElevationCandidateCropLoop(
            ViewSection view,
            Transform cropFrame,
            XYZ localMin,
            XYZ localMax,
            List<XYZ> projectedLoopPoints)
        {
            if (view == null || cropFrame == null || localMin == null || localMax == null)
                return null;

            BoundingBoxXYZ currentCrop = view.CropBox;
            Transform viewFrame = currentCrop?.Transform;
            XYZ planeOrigin = viewFrame?.Origin ?? cropFrame.Origin;
            XYZ planeNormal = FlattenAndNormalize(viewFrame?.BasisZ ?? cropFrame.BasisZ);
            if (planeNormal == null)
                return null;

            XYZ p0 = ProjectCurtainElevationPointToPlane(cropFrame.OfPoint(new XYZ(localMin.X, localMin.Y, 0)), planeOrigin, planeNormal);
            XYZ p1 = ProjectCurtainElevationPointToPlane(cropFrame.OfPoint(new XYZ(localMax.X, localMin.Y, 0)), planeOrigin, planeNormal);
            XYZ p2 = ProjectCurtainElevationPointToPlane(cropFrame.OfPoint(new XYZ(localMax.X, localMax.Y, 0)), planeOrigin, planeNormal);
            XYZ p3 = ProjectCurtainElevationPointToPlane(cropFrame.OfPoint(new XYZ(localMin.X, localMax.Y, 0)), planeOrigin, planeNormal);

            projectedLoopPoints?.AddRange(new[] { p0, p1, p2, p3 });

            var loop = new CurveLoop();
            loop.Append(Line.CreateBound(p0, p1));
            loop.Append(Line.CreateBound(p1, p2));
            loop.Append(Line.CreateBound(p2, p3));
            loop.Append(Line.CreateBound(p3, p0));
            return loop;
        }

        private XYZ ProjectCurtainElevationPointToPlane(XYZ point, XYZ planeOrigin, XYZ planeNormal)
        {
            if (point == null || planeOrigin == null || planeNormal == null)
                return point;

            XYZ normal = FlattenAndNormalize(planeNormal);
            if (normal == null)
                return point;

            double distance = (point - planeOrigin).DotProduct(normal);
            return point - normal * distance;
        }

        private Transform GetCurtainElevationWallAlignedCropFrame(Wall wall, XYZ wallMidPoint, XYZ markerPoint)
        {
            if (wall == null || wallMidPoint == null || markerPoint == null)
                return null;

            LocationCurve loc = wall.Location as LocationCurve;
            Curve curve = loc?.Curve;
            if (curve == null)
                return null;

            XYZ start;
            XYZ end;
            try
            {
                start = curve.GetEndPoint(0);
                end = curve.GetEndPoint(1);
            }
            catch
            {
                return null;
            }

            XYZ right = FlattenAndNormalize(end - start);
            XYZ up = XYZ.BasisZ;
            XYZ depth = FlattenAndNormalize(markerPoint - wallMidPoint);
            if (right == null || depth == null)
                return null;

            XYZ handedDepth = FlattenAndNormalize(right.CrossProduct(up));
            if (handedDepth == null)
                return null;

            if (handedDepth.DotProduct(depth) < 0)
            {
                right = right.Negate();
                handedDepth = handedDepth.Negate();
            }

            Transform transform = Transform.Identity;
            transform.Origin = wallMidPoint;
            transform.BasisX = right;
            transform.BasisY = up;
            transform.BasisZ = handedDepth;
            return transform;
        }

        private bool IsCurtainElevationCropFrameEquivalent(Transform actual, Transform expected)
        {
            if (actual == null || expected == null)
                return false;

            const double directionTolerance = 0.999;
            return Math.Abs(FlattenAndNormalize(actual.BasisX)?.DotProduct(FlattenAndNormalize(expected.BasisX)) ?? 0) >= directionTolerance
                && Math.Abs(FlattenAndNormalize(actual.BasisY)?.DotProduct(FlattenAndNormalize(expected.BasisY)) ?? 0) >= directionTolerance
                && Math.Abs(FlattenAndNormalize(actual.BasisZ)?.DotProduct(FlattenAndNormalize(expected.BasisZ)) ?? 0) >= directionTolerance;
        }

        private void ApplyCurtainElevationCropWithRevitTransformFallback(ViewSection view, Transform sourceFrame, XYZ localMin, XYZ localMax)
        {
            BoundingBoxXYZ currentCrop = view?.CropBox;
            Transform targetFrame = currentCrop?.Transform;
            if (view == null || sourceFrame == null || localMin == null || localMax == null || targetFrame == null)
                return;

            List<XYZ> worldCorners = GetCurtainElevationCropWorldCorners(sourceFrame, localMin, localMax);
            Transform inverse = targetFrame.Inverse;
            double minX = double.MaxValue;
            double minY = double.MaxValue;
            double minZ = double.MaxValue;
            double maxX = double.MinValue;
            double maxY = double.MinValue;
            double maxZ = double.MinValue;

            foreach (XYZ point in worldCorners)
            {
                XYZ local = inverse.OfPoint(point);
                minX = Math.Min(minX, local.X);
                minY = Math.Min(minY, local.Y);
                minZ = Math.Min(minZ, local.Z);
                maxX = Math.Max(maxX, local.X);
                maxY = Math.Max(maxY, local.Y);
                maxZ = Math.Max(maxZ, local.Z);
            }

            if (minX == double.MaxValue)
                return;

            view.CropBox = new BoundingBoxXYZ
            {
                Transform = targetFrame,
                Min = new XYZ(minX, minY, minZ),
                Max = new XYZ(maxX, maxY, maxZ)
            };
        }

        private List<XYZ> GetCurtainElevationCropWorldCorners(Transform frame, XYZ min, XYZ max)
        {
            return new List<XYZ>
            {
                frame.OfPoint(new XYZ(min.X, min.Y, min.Z)),
                frame.OfPoint(new XYZ(min.X, min.Y, max.Z)),
                frame.OfPoint(new XYZ(min.X, max.Y, min.Z)),
                frame.OfPoint(new XYZ(min.X, max.Y, max.Z)),
                frame.OfPoint(new XYZ(max.X, min.Y, min.Z)),
                frame.OfPoint(new XYZ(max.X, min.Y, max.Z)),
                frame.OfPoint(new XYZ(max.X, max.Y, min.Z)),
                frame.OfPoint(new XYZ(max.X, max.Y, max.Z))
            };
        }

        private List<BoundingBoxXYZ> GetCurtainElevationElementBoundingBoxes(Document doc, Wall wall)
        {
            var boxes = new List<BoundingBoxXYZ>();
            var ids = new HashSet<ElementId>();

            AddCurtainElevationElementBoundingBox(doc, wall?.Id, ids, boxes);

            try
            {
                CurtainGrid grid = wall?.CurtainGrid;
                if (grid != null)
                {
                    foreach (ElementId id in grid.GetPanelIds())
                        AddCurtainElevationElementBoundingBox(doc, id, ids, boxes);
                    foreach (ElementId id in grid.GetMullionIds())
                        AddCurtainElevationElementBoundingBox(doc, id, ids, boxes);
                }
            }
            catch
            {
                // Fallback to host wall bbox if curtain sub-elements are not available.
            }

            try
            {
                foreach (ElementId id in wall.FindInserts(true, true, true, true))
                    AddCurtainElevationElementBoundingBox(doc, id, ids, boxes);
            }
            catch
            {
                // Some wall/system states do not support inserts lookup.
            }

            return boxes;
        }

        private CurtainElevationGeometryPointResult GetCurtainElevationGeometryPoints(Document doc, Wall wall, ViewSection view)
        {
            var result = new CurtainElevationGeometryPointResult();
            if (doc == null || wall == null)
                return result;

            var ids = GetCurtainElevationElementIds(wall, includeHostWall: false);
            var options = new Options
            {
                IncludeNonVisibleObjects = false
            };
            options.View = view;

            foreach (ElementId id in ids)
                AddCurtainElevationElementGeometryPointRecords(doc, id, options, result, "geometry", false);

            if (result.Points.Count == 0)
            {
                result.UsedHostWallFallback = true;
                AddCurtainElevationElementGeometryPointRecords(doc, wall.Id, options, result, "host_wall_geometry", true);
            }

            return result;
        }

        private CurtainElevationGeometryPointResult GetCurtainElevationView2DPoints(Document doc, Wall wall, ViewSection view)
        {
            var result = new CurtainElevationGeometryPointResult();
            if (doc == null || wall == null)
                return result;

            var ids = GetCurtainElevationElementIds(wall, includeHostWall: false);
            var options = new Options
            {
                IncludeNonVisibleObjects = false
            };
            options.View = view;

            foreach (ElementId id in ids)
                AddCurtainElevationElementView2DPointRecords(doc, id, view, options, result, false);

            if (result.Points.Count == 0)
            {
                result.UsedHostWallFallback = true;
                AddCurtainElevationElementView2DPointRecords(doc, wall.Id, view, options, result, true);
            }

            return result;
        }

        private void AddCurtainElevationElementView2DPointRecords(
            Document doc,
            ElementId id,
            View view,
            Options options,
            CurtainElevationGeometryPointResult result,
            bool isHostWallFallback)
        {
            Element element = doc?.GetElement(id);
            if (element == null || result == null)
                return;

            result.ElementCount++;

            var viewBoxPoints = new List<XYZ>();
            if (AddCurtainElevationBoundingBoxPoints(element, view, viewBoxPoints))
            {
                result.ViewSpecificBoundingBoxPointCount += viewBoxPoints.Count;
                AddCurtainElevationPointRecords(viewBoxPoints, element, isHostWallFallback ? "host_wall_view_bbox" : "view_bbox", result);
                return;
            }

            var elementPoints = new List<XYZ>();
            try
            {
                GeometryElement geometry = element.get_Geometry(options);
                AddCurtainElevationGeometryPoints(geometry, elementPoints);
            }
            catch
            {
                // Some system/family states cannot provide view-specific geometry.
            }

            if (elementPoints.Count > 0)
            {
                result.GeometryPointCount += elementPoints.Count;
                AddCurtainElevationPointRecords(elementPoints, element, isHostWallFallback ? "host_wall_geometry" : "geometry", result);
                return;
            }

            var bboxPoints = new List<XYZ>();
            if (AddCurtainElevationBoundingBoxPoints(element, bboxPoints))
            {
                result.FallbackElementCount++;
                result.FallbackElementIds.Add(id);
                AddCurtainElevationPointRecords(bboxPoints, element, isHostWallFallback ? "host_wall_bbox_fallback" : "bbox_fallback", result);
            }
        }

        private void AddCurtainElevationElementGeometryPointRecords(
            Document doc,
            ElementId id,
            Options options,
            CurtainElevationGeometryPointResult result,
            string geometrySource,
            bool isHostWallFallback)
        {
            Element element = doc?.GetElement(id);
            if (element == null || result == null)
                return;

            result.ElementCount++;
            var elementPoints = new List<XYZ>();

            try
            {
                GeometryElement geometry = element.get_Geometry(options);
                AddCurtainElevationGeometryPoints(geometry, elementPoints);
            }
            catch
            {
                // Some system/family states cannot provide view-specific geometry.
            }

            if (elementPoints.Count > 0)
            {
                result.GeometryPointCount += elementPoints.Count;
                AddCurtainElevationPointRecords(elementPoints, element, geometrySource, result);
                return;
            }

            var bboxPoints = new List<XYZ>();
            if (AddCurtainElevationBoundingBoxPoints(element, bboxPoints))
            {
                result.FallbackElementCount++;
                result.FallbackElementIds.Add(id);
                AddCurtainElevationPointRecords(bboxPoints, element, isHostWallFallback ? "host_wall_bbox_fallback" : "bbox_fallback", result);
            }
        }

        private void AddCurtainElevationPointRecords(List<XYZ> points, Element element, string source, CurtainElevationGeometryPointResult result)
        {
            if (points == null || element == null || result == null)
                return;

            foreach (XYZ point in points)
            {
                result.Points.Add(point);
                result.Records.Add(new CurtainElevationPointRecord
                {
                    Point = point,
                    ElementId = element.Id,
                    CategoryName = element.Category?.Name,
                    Source = source
                });
            }

            result.ContributingElementIds.Add(element.Id);
        }

        private List<ElementId> GetCurtainElevationElementIds(Wall wall, bool includeHostWall)
        {
            var ids = new List<ElementId>();
            var seen = new HashSet<ElementId>();

            if (includeHostWall)
                AddCurtainElevationElementId(wall?.Id, seen, ids);

            try
            {
                CurtainGrid grid = wall?.CurtainGrid;
                if (grid != null)
                {
                    foreach (ElementId id in grid.GetPanelIds())
                        AddCurtainElevationElementId(id, seen, ids);
                    foreach (ElementId id in grid.GetMullionIds())
                        AddCurtainElevationElementId(id, seen, ids);
                }
            }
            catch
            {
                // Fallback to host wall if curtain sub-elements are not available.
            }

            try
            {
                foreach (ElementId id in wall.FindInserts(true, true, true, true))
                    AddCurtainElevationElementId(id, seen, ids);
            }
            catch
            {
                // Some wall/system states do not support inserts lookup.
            }

            return ids;
        }

        private void AddCurtainElevationElementId(ElementId id, HashSet<ElementId> seen, List<ElementId> ids)
        {
            if (id == null || id == ElementId.InvalidElementId || seen.Contains(id))
                return;

            seen.Add(id);
            ids.Add(id);
        }

        private void AddCurtainElevationGeometryPoints(GeometryElement geometry, List<XYZ> points)
        {
            if (geometry == null || points == null)
                return;

            foreach (GeometryObject obj in geometry)
                AddCurtainElevationGeometryObjectPoints(obj, points);
        }

        private void AddCurtainElevationGeometryObjectPoints(GeometryObject obj, List<XYZ> points)
        {
            if (obj == null || points == null)
                return;

            if (obj is Solid solid)
            {
                AddCurtainElevationSolidPoints(solid, points);
                return;
            }

            if (obj is Mesh mesh)
            {
                foreach (XYZ point in mesh.Vertices)
                    points.Add(point);
                return;
            }

            if (obj is Curve curve)
            {
                AddCurtainElevationCurvePoints(curve, points);
                return;
            }

            if (obj is GeometryInstance instance)
            {
                try
                {
                    AddCurtainElevationGeometryPoints(instance.GetInstanceGeometry(), points);
                }
                catch
                {
                    // Ignore geometry instance extraction failures; caller can bbox fallback per element.
                }
            }
        }

        private void AddCurtainElevationSolidPoints(Solid solid, List<XYZ> points)
        {
            if (solid == null || points == null || solid.Edges == null || solid.Edges.Size == 0)
                return;

            foreach (Edge edge in solid.Edges)
            {
                try
                {
                    IList<XYZ> tessellated = edge.Tessellate();
                    foreach (XYZ point in tessellated)
                        points.Add(point);
                }
                catch
                {
                    try
                    {
                        AddCurtainElevationCurvePoints(edge.AsCurve(), points);
                    }
                    catch
                    {
                        // Ignore bad edge geometry.
                    }
                }
            }
        }

        private void AddCurtainElevationCurvePoints(Curve curve, List<XYZ> points)
        {
            if (curve == null || points == null)
                return;

            try
            {
                IList<XYZ> tessellated = curve.Tessellate();
                if (tessellated != null && tessellated.Count > 0)
                {
                    foreach (XYZ point in tessellated)
                        points.Add(point);
                    return;
                }
            }
            catch
            {
                // Fallback to endpoints below.
            }

            try
            {
                points.Add(curve.GetEndPoint(0));
                points.Add(curve.GetEndPoint(1));
            }
            catch
            {
                // Unbound curves do not expose endpoints.
            }
        }

        private bool AddCurtainElevationBoundingBoxPoints(Element element, List<XYZ> points)
        {
            if (element == null || points == null)
                return false;

            BoundingBoxXYZ box = element.get_BoundingBox(null);
            if (box == null)
                return false;

            int before = points.Count;
            AddCurtainElevationBoundingBoxPoints(box, points);
            return points.Count > before;
        }

        private bool AddCurtainElevationBoundingBoxPoints(Element element, View view, List<XYZ> points)
        {
            if (element == null || view == null || points == null)
                return false;

            BoundingBoxXYZ box = element.get_BoundingBox(view);
            if (box == null)
                return false;

            int before = points.Count;
            AddCurtainElevationBoundingBoxPoints(box, points);
            return points.Count > before;
        }

        private void AddCurtainElevationBoundingBoxPoints(BoundingBoxXYZ box, List<XYZ> points)
        {
            if (box == null || points == null)
                return;

            Transform transform = box.Transform ?? Transform.Identity;
            points.Add(transform.OfPoint(new XYZ(box.Min.X, box.Min.Y, box.Min.Z)));
            points.Add(transform.OfPoint(new XYZ(box.Min.X, box.Min.Y, box.Max.Z)));
            points.Add(transform.OfPoint(new XYZ(box.Min.X, box.Max.Y, box.Min.Z)));
            points.Add(transform.OfPoint(new XYZ(box.Min.X, box.Max.Y, box.Max.Z)));
            points.Add(transform.OfPoint(new XYZ(box.Max.X, box.Min.Y, box.Min.Z)));
            points.Add(transform.OfPoint(new XYZ(box.Max.X, box.Min.Y, box.Max.Z)));
            points.Add(transform.OfPoint(new XYZ(box.Max.X, box.Max.Y, box.Min.Z)));
            points.Add(transform.OfPoint(new XYZ(box.Max.X, box.Max.Y, box.Max.Z)));
        }

        private void AddCurtainElevationElementBoundingBox(Document doc, ElementId id, HashSet<ElementId> ids, List<BoundingBoxXYZ> boxes)
        {
            if (doc == null || id == null || id == ElementId.InvalidElementId || ids.Contains(id))
                return;

            ids.Add(id);
            Element element = doc.GetElement(id);
            BoundingBoxXYZ box = element?.get_BoundingBox(null);
            if (box != null)
                boxes.Add(box);
        }

        private List<XYZ> GetCurtainElevationBoundingPoints(List<BoundingBoxXYZ> boxes)
        {
            var points = new List<XYZ>();
            foreach (BoundingBoxXYZ box in boxes)
            {
                points.Add(new XYZ(box.Min.X, box.Min.Y, box.Min.Z));
                points.Add(new XYZ(box.Min.X, box.Min.Y, box.Max.Z));
                points.Add(new XYZ(box.Min.X, box.Max.Y, box.Min.Z));
                points.Add(new XYZ(box.Min.X, box.Max.Y, box.Max.Z));
                points.Add(new XYZ(box.Max.X, box.Min.Y, box.Min.Z));
                points.Add(new XYZ(box.Max.X, box.Min.Y, box.Max.Z));
                points.Add(new XYZ(box.Max.X, box.Max.Y, box.Min.Z));
                points.Add(new XYZ(box.Max.X, box.Max.Y, box.Max.Z));
            }

            return points;
        }

        private void ConfigureCurtainElevationFarClip(ViewSection view, CurtainElevationCropResult result, List<string> warnings)
        {
            double depthFt = result?.FarClipDepthFt ?? 0;
            SetViewParameterByBuiltInName(view, "VIEWER_BOUND_ACTIVE_FAR", 1);
            SetViewParameterByBuiltInName(view, "VIEWER_BOUND_FAR_CLIPPING", 2);
            SetViewParameterByBuiltInName(view, "VIEWER_BOUND_OFFSET_FAR", depthFt);

            if (result == null)
                return;

            result.FarClipRequestedDepthFt = depthFt;
            try
            {
                view?.Document?.Regenerate();
            }
            catch (Exception ex)
            {
                result.FarClipWarning = AppendCurtainElevationWarning(result.FarClipWarning, $"Document regenerate after far clip write failed: {ex.Message}");
            }

            result.FarClipActualActive = GetViewIntegerParameterByBuiltInName(view, "VIEWER_BOUND_ACTIVE_FAR");
            result.FarClipActualMode = GetViewIntegerParameterByBuiltInName(view, "VIEWER_BOUND_FAR_CLIPPING");
            result.FarClipActualOffsetFt = GetViewDoubleParameterByBuiltInName(view, "VIEWER_BOUND_OFFSET_FAR");
            BoundingBoxXYZ readbackCrop = view?.CropBox;
            if (readbackCrop?.Min != null && readbackCrop.Max != null)
            {
                result.FarClipCropBoxMinZAfterFt = readbackCrop.Min.Z;
                result.FarClipCropBoxMaxZAfterFt = readbackCrop.Max.Z;
                result.FarClipCropBoxDepthAfterFt = Math.Abs(readbackCrop.Max.Z - readbackCrop.Min.Z);
            }

            if (!result.FarClipActualOffsetFt.HasValue)
            {
                result.FarClipPass = false;
                result.FarClipDepthDeltaFt = depthFt;
                result.FarClipWarning = AppendCurtainElevationWarning(result.FarClipWarning, "Cannot read VIEWER_BOUND_OFFSET_FAR after setting far clip.");
            }
            else if (Math.Abs(result.FarClipActualOffsetFt.Value - depthFt) > 1.0 / 304.8)
            {
                result.FarClipPass = false;
                result.FarClipDepthDeltaFt = Math.Abs(result.FarClipActualOffsetFt.Value - depthFt);
                result.FarClipWarning = AppendCurtainElevationWarning(
                    result.FarClipWarning,
                    $"VIEWER_BOUND_OFFSET_FAR readback differs from requested depth. Requested={Math.Round(depthFt * 304.8, 1)}mm, Actual={Math.Round(result.FarClipActualOffsetFt.Value * 304.8, 1)}mm.");
            }
            else
            {
                result.FarClipDepthDeltaFt = Math.Abs(result.FarClipActualOffsetFt.Value - depthFt);
                result.FarClipPass = true;
            }

            if (result.FarClipActualActive.HasValue && result.FarClipActualActive.Value != 1)
            {
                result.FarClipPass = false;
                result.FarClipWarning = AppendCurtainElevationWarning(result.FarClipWarning, $"VIEWER_BOUND_ACTIVE_FAR readback is {result.FarClipActualActive.Value}, expected 1.");
            }

            if (result.FarClipActualMode.HasValue && result.FarClipActualMode.Value != 2)
            {
                result.FarClipPass = false;
                result.FarClipWarning = AppendCurtainElevationWarning(result.FarClipWarning, $"VIEWER_BOUND_FAR_CLIPPING readback is {result.FarClipActualMode.Value}, expected 2.");
            }

            if (!string.IsNullOrWhiteSpace(result.FarClipWarning))
                warnings?.Add(result.FarClipWarning);
        }

        private void SetViewParameterByBuiltInName(View view, string builtInParameterName, double value)
        {
            if (!Enum.TryParse(builtInParameterName, out BuiltInParameter bip))
                return;

            Parameter parameter = view.get_Parameter(bip);
            if (parameter == null || parameter.IsReadOnly)
                return;

            if (parameter.StorageType == StorageType.Double)
                parameter.Set(value);
            else if (parameter.StorageType == StorageType.Integer)
                parameter.Set((int)Math.Round(value));
        }

        private double? GetViewDoubleParameterByBuiltInName(View view, string builtInParameterName)
        {
            if (view == null || !Enum.TryParse(builtInParameterName, out BuiltInParameter bip))
                return null;

            Parameter parameter = view.get_Parameter(bip);
            if (parameter == null || parameter.StorageType != StorageType.Double)
                return null;

            return parameter.AsDouble();
        }

        private int? GetViewIntegerParameterByBuiltInName(View view, string builtInParameterName)
        {
            if (view == null || !Enum.TryParse(builtInParameterName, out BuiltInParameter bip))
                return null;

            Parameter parameter = view.get_Parameter(bip);
            if (parameter == null || parameter.StorageType != StorageType.Integer)
                return null;

            return parameter.AsInteger();
        }

        private string AppendCurtainElevationWarning(string current, string warning)
        {
            if (string.IsNullOrWhiteSpace(warning))
                return current;

            if (string.IsNullOrWhiteSpace(current))
                return warning;

            return $"{current} {warning}";
        }

        private View FindCurtainElevationViewTemplate(Document doc, string templateName)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .FirstOrDefault(v => v.IsTemplate && v.Name == templateName);
        }

        private void ConfigureCurtainElevationViewTemplate(Document doc, View template, List<string> warnings)
        {
            var keepCategories = new HashSet<ElementId>
            {
                new ElementId((IdType)(int)BuiltInCategory.OST_Walls),
                new ElementId((IdType)(int)BuiltInCategory.OST_CurtainWallPanels),
                new ElementId((IdType)(int)BuiltInCategory.OST_CurtainWallMullions),
                new ElementId((IdType)(int)BuiltInCategory.OST_Doors),
                new ElementId((IdType)(int)BuiltInCategory.OST_Windows),
                new ElementId((IdType)(int)BuiltInCategory.OST_Levels),
                new ElementId((IdType)(int)BuiltInCategory.OST_WallTags),
                new ElementId((IdType)(int)BuiltInCategory.OST_Dimensions),
                new ElementId((IdType)(int)BuiltInCategory.OST_Lines)
            };

            foreach (Category category in doc.Settings.Categories)
            {
                if (category == null)
                    continue;
                if (category.CategoryType != CategoryType.Model && category.CategoryType != CategoryType.Annotation)
                    continue;

                TrySetCurtainTemplateCategoryHidden(template, category.Id, true, warnings);
            }

            foreach (ElementId id in keepCategories)
            {
                TrySetCurtainTemplateCategoryHidden(template, id, false, warnings);
            }

            ExcludeCurtainElevationCropAndFarClipFromTemplate(template, warnings);
        }

        private void TrySetCurtainTemplateCategoryHidden(View template, ElementId categoryId, bool hidden, List<string> warnings)
        {
            try
            {
                if (template.CanCategoryBeHidden(categoryId))
                    template.SetCategoryHidden(categoryId, hidden);
            }
            catch (Exception ex)
            {
                warnings.Add($"Category {categoryId.GetIdValue()} visibility skipped: {ex.Message}");
            }
        }

        private void ExcludeCurtainElevationCropAndFarClipFromTemplate(View template, List<string> warnings)
        {
            try
            {
                HashSet<ElementId> allTemplateParams = template.GetTemplateParameterIds().ToHashSet();
                HashSet<ElementId> nonControlled = template.GetNonControlledTemplateParameterIds().ToHashSet();

                foreach (BuiltInParameter bip in Enum.GetValues(typeof(BuiltInParameter)))
                {
                    string name = bip.ToString();
                    bool isCropOrFarClip =
                        name.IndexOf("CROP", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        name.IndexOf("VIEWER_BOUND", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        name.IndexOf("FAR_CLIP", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        name.IndexOf("CLIPPING", StringComparison.OrdinalIgnoreCase) >= 0;

                    if (!isCropOrFarClip)
                        continue;

                    ElementId paramId = new ElementId((IdType)(int)bip);
                    if (allTemplateParams.Contains(paramId))
                        nonControlled.Add(paramId);
                }

                template.SetNonControlledTemplateParameterIds(nonControlled);
            }
            catch (Exception ex)
            {
                warnings.Add($"View template non-controlled crop/far clip parameters skipped: {ex.Message}");
            }
        }

        /// <summary>
        /// 建�??��?帷�??�板類�?（含?��?�?
        /// </summary>
        private object CreateCurtainPanelType(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;

            string typeName = parameters["typeName"]?.Value<string>();
            string colorHex = parameters["color"]?.Value<string>() ?? "#808080";
            int transparency = parameters["transparency"]?.Value<int>() ?? 0;
            string basePanelTypeName = parameters["basePanelType"]?.Value<string>();

            if (string.IsNullOrEmpty(typeName))
                throw new Exception("請�?定新類�??�稱 (typeName)");

            // �??顏色
            colorHex = colorHex.TrimStart('#');
            byte r = Convert.ToByte(colorHex.Substring(0, 2), 16);
            byte g = Convert.ToByte(colorHex.Substring(2, 2), 16);
            byte b = Convert.ToByte(colorHex.Substring(4, 2), 16);
            Color revitColor = new Color(r, g, b);

            using (Transaction trans = new Transaction(doc, "建�?帷�??�板類�?"))
            {
                trans.Start();

                // 1. ?�到?��??�板類�?來�?�?
                ElementType basePanelType = null;

                if (!string.IsNullOrEmpty(basePanelTypeName))
                {
                    basePanelType = new FilteredElementCollector(doc)
                        .OfCategory(BuiltInCategory.OST_CurtainWallPanels)
                        .WhereElementIsElementType()
                        .Cast<ElementType>()
                        .FirstOrDefault(pt => pt.Name == basePanelTypeName);
                }

                if (basePanelType == null)
                {
                    // 使用?�設??System Panel
                    basePanelType = new FilteredElementCollector(doc)
                        .OfCategory(BuiltInCategory.OST_CurtainWallPanels)
                        .WhereElementIsElementType()
                        .Cast<ElementType>()
                        .FirstOrDefault();
                }

                if (basePanelType == null)
                    throw new Exception("?��??�可?��?帷�??�板類�?作為?��?");

                // 2. 檢查?�否已�??��??��???
                ElementType existingType = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_CurtainWallPanels)
                    .WhereElementIsElementType()
                    .Cast<ElementType>()
                    .FirstOrDefault(pt => pt.Name == typeName);

                ElementType newPanelType;
                bool isNewType = false;

                if (existingType != null)
                {
                    newPanelType = existingType;
                }
                else
                {
                    // 3. 複製類�?
                    newPanelType = basePanelType.Duplicate(typeName) as ElementType;
                    isNewType = true;
                }

                // 4. 建�??�更?��???
                string materialName = $"CW_PNL_{typeName}";
                Material material = new FilteredElementCollector(doc)
                    .OfClass(typeof(Material))
                    .Cast<Material>()
                    .FirstOrDefault(m => m.Name == materialName);

                if (material == null)
                {
                    // 建�??��???
                    ElementId newMatId = Material.Create(doc, materialName);
                    material = doc.GetElement(newMatId) as Material;
                }

                // 設�??��?屬�?
                material.Color = revitColor;
                material.Transparency = transparency;

                // 5. 將�??��?派給?�板類�?
                Parameter matParam = newPanelType.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM);
                if (matParam != null && !matParam.IsReadOnly)
                {
                    matParam.Set(material.Id);
                }

                trans.Commit();

                return new
                {
                    Success = true,
                    TypeId = newPanelType.Id.GetIdValue(),
                    TypeName = typeName,
                    IsNewType = isNewType,
                    MaterialId = material.Id.GetIdValue(),
                    MaterialName = materialName,
                    Color = $"#{r:X2}{g:X2}{b:X2}",
                    Transparency = transparency,
                    Message = isNewType
                        ? $"?��?建�??�面?��??? {typeName}"
                        : $"已更?�既?�面?��??? {typeName}"
                };
            }
        }

        /// <summary>
        /// ?�次套用?�板?��?模�?
        /// ?�援?�種模�?�?
        /// 1. typeMapping + matrix: 使用字�??�陣?��?類�??��?
        /// 2. pattern: ?�接使用 TypeId ?�陣
        /// </summary>
        private object ApplyPanelPattern(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;
            UIDocument uidoc = _uiApp.ActiveUIDocument;

            IdType? wallElementId = parameters["elementId"]?.Value<IdType>() ?? parameters["wallId"]?.Value<IdType>();
            JObject typeMapping = parameters["typeMapping"] as JObject;
            JArray matrix = parameters["matrix"] as JArray;
            JArray patternArray = parameters["pattern"] as JArray;

            // ?��?帷�???
            Wall wall = null;
            if (wallElementId.HasValue)
            {
                wall = doc.GetElement(new ElementId(wallElementId.Value)) as Wall;
            }
            else
            {
                var selection = uidoc.Selection.GetElementIds();
                if (selection.Count > 0)
                {
                    wall = doc.GetElement(selection.First()) as Wall;
                }
            }

            if (wall == null)
                throw new Exception("?��??�帷幕�?，�??��? elementId ?�選?�帷幕�?");

            CurtainGrid grid = wall.CurtainGrid;
            if (grid == null)
                throw new Exception("Selected wall is not a curtain wall.");

            // 建�?類�??��?字典
            var typeMappingDict = new Dictionary<string, IdType>();
            if (typeMapping != null)
            {
                foreach (var prop in typeMapping.Properties())
                {
                    typeMappingDict[prop.Name] = prop.Value.Value<IdType>();
                }
            }

            // 決�?使用?�種模�?
            JArray sourceMatrix = matrix ?? patternArray;
            if (sourceMatrix == null)
                throw new Exception("Provide matrix with typeMapping, or pattern with typeId values.");

            // ?��??�?�面??
            var panelIds = grid.GetPanelIds().ToList();

            // 建�??�板位置?��? (依�?幾�?位置?��?)
            var panelPositions = new List<(ElementId Id, XYZ Center)>();

            foreach (ElementId panelId in panelIds)
            {
                Element panel = doc.GetElement(panelId);
                if (panel == null) continue;

                BoundingBoxXYZ bb = panel.get_BoundingBox(null);
                if (bb == null) continue;

                XYZ center = (bb.Min + bb.Max) / 2;
                panelPositions.Add((panelId, center));
            }

            // 依照位置?��?並�???Row/Col
            // ?��? Z (高度) ?��?（由上到下�?，�?�?X ??Y ?��?（由左到?��?
            var sortedByZ = panelPositions.OrderByDescending(p => p.Center.Z).ToList();

            // ?��? by Z
            var rowGroups = new List<List<(ElementId Id, XYZ Center)>>();
            double zTolerance = 0.5; // 0.5 feet

            foreach (var panel in sortedByZ)
            {
                bool added = false;
                foreach (var group in rowGroups)
                {
                    if (Math.Abs(group[0].Center.Z - panel.Center.Z) < zTolerance)
                    {
                        group.Add((panel.Id, panel.Center));
                        added = true;
                        break;
                    }
                }
                if (!added)
                {
                    rowGroups.Add(new List<(ElementId, XYZ)> { (panel.Id, panel.Center) });
                }
            }

            // 建�? Row/Col ??PanelId ?��?�?
            var panelGrid = new Dictionary<(int row, int col), ElementId>();
            int rowIndex = 0;
            foreach (var rowGroup in rowGroups)
            {
                var sortedRow = rowGroup.OrderBy(p => p.Center.X).ThenBy(p => p.Center.Y).ToList();
                int colIndex = 0;
                foreach (var panel in sortedRow)
                {
                    panelGrid[(rowIndex, colIndex)] = panel.Id;
                    colIndex++;
                }
                rowIndex++;
            }

            // 套用模�?
            int successCount = 0;
            int failCount = 0;
            var failedPanels = new List<object>();

            using (Transaction trans = new Transaction(doc, "套用帷�??�板?��?"))
            {
                trans.Start();

                for (int r = 0; r < sourceMatrix.Count && r < rowGroups.Count; r++)
                {
                    JArray rowData = sourceMatrix[r] as JArray;
                    if (rowData == null) continue;

                    for (int c = 0; c < rowData.Count; c++)
                    {
                        if (!panelGrid.ContainsKey((r, c))) continue;

                        // ?��??��?類�? ID
                        IdType targetTypeId = 0;
                        var cellValue = rowData[c];

                        if (cellValue.Type == JTokenType.String)
                        {
                            // 字�?模�?，�? typeMapping ?�找
                            string key = cellValue.Value<string>();
                            if (string.IsNullOrEmpty(key)) continue;
                            if (!typeMappingDict.TryGetValue(key, out targetTypeId))
                            {
                                failedPanels.Add(new { Row = r, Col = c, Reason = $"?��??��?�? {key}" });
                                failCount++;
                                continue;
                            }
                        }
                        else if (cellValue.Type == JTokenType.Integer)
                        {
                            // ?�接 TypeId 模�?
                            targetTypeId = cellValue.Value<IdType>();
                        }

                        if (targetTypeId == 0) continue;

                        ElementId panelId = panelGrid[(r, c)];
                        Element panel = doc.GetElement(panelId);

                        if (panel == null)
                        {
                            failCount++;
                            continue;
                        }

                        try
                        {
                            // ?��??��?類�?
                            ElementType targetType = doc.GetElement(new ElementId(targetTypeId)) as ElementType;
                            if (targetType == null)
                            {
                                failedPanels.Add(new { PanelId = panelId.GetIdValue(), Row = r, Col = c, Reason = $"?��???TypeId: {targetTypeId}" });
                                failCount++;
                                continue;
                            }

                            // 變更?�板類�?
                            panel.ChangeTypeId(new ElementId(targetTypeId));
                            successCount++;
                        }
                        catch (Exception ex)
                        {
                            failedPanels.Add(new { PanelId = panelId.GetIdValue(), Row = r, Col = c, Reason = ex.Message });
                            failCount++;
                        }
                    }
                }

                trans.Commit();
            }

            return new
            {
                Success = true,
                WallId = wall.Id.GetIdValue(),
                TotalPanels = panelIds.Count,
                SuccessCount = successCount,
                FailCount = failCount,
                FailedPanels = failedPanels,
                GridSize = new { Rows = rowGroups.Count, Columns = rowGroups.FirstOrDefault()?.Count ?? 0 },
                Message = $"Applied curtain panel pattern: success={successCount}, failed={failCount}."
            };
        }

        // ============================
        // 立面?�板 (Facade Panel) ?��?
        // ============================

        /// <summary>
        /// 建�??��?立面?�板 (DirectShape)
        /// ?�援多種幾�?類�?：curved_panel（弧形面?��??�beveled_opening（�??�凹窗�?）、flat_panel（平?�面?��?
        /// </summary>
        private object CreateFacadePanel(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;
            UIDocument uidoc = _uiApp.ActiveUIDocument;

            // �???�用?�數
            IdType? wallId = parameters["wallId"]?.Value<IdType>();
            double positionAlongWall = parameters["positionAlongWall"]?.Value<double>() ?? 0;
            double positionZ = parameters["positionZ"]?.Value<double>() ?? 0;
            double width = parameters["width"]?.Value<double>() ?? 800;
            double height = parameters["height"]?.Value<double>() ?? 3400;
            double depth = parameters["depth"]?.Value<double>() ?? 150;
            double thickness = parameters["thickness"]?.Value<double>() ?? 30;
            double offset = parameters["offset"]?.Value<double>() ?? 200;
            string colorHex = parameters["color"]?.Value<string>() ?? "#B85C3A";
            string panelName = parameters["name"]?.Value<string>() ?? "FacadePanel";
            string geometryType = parameters["geometryType"]?.Value<string>() ?? "curved_panel";

            // curved_panel 專用
            string curveType = parameters["curveType"]?.Value<string>() ?? "concave";

            // beveled_opening 專用
            string bevelDirection = parameters["bevelDirection"]?.Value<string>() ?? "center";
            double openingWidth = parameters["openingWidth"]?.Value<double>() ?? 600;
            double openingHeight = parameters["openingHeight"]?.Value<double>() ?? 800;
            double bevelDepth = parameters["bevelDepth"]?.Value<double>() ?? 300;

            // angled_panel 專用
            double tiltAngle = parameters["tiltAngle"]?.Value<double>() ?? 15;
            string tiltAxis = parameters["tiltAxis"]?.Value<string>() ?? "horizontal";

            // rounded_opening 專用
            double cornerRadius = parameters["cornerRadius"]?.Value<double>() ?? 100;
            string openingShape = parameters["openingShape"]?.Value<string>() ?? "rounded_rect";

            // ?��??��?
            Wall wall = null;
            if (wallId.HasValue)
            {
                wall = doc.GetElement(new ElementId(wallId.Value)) as Wall;
            }
            else
            {
                var selection = uidoc.Selection.GetElementIds();
                if (selection.Count > 0)
                    wall = doc.GetElement(selection.First()) as Wall;
            }

            if (wall == null)
                throw new Exception("Provide wallId or select a wall.");

            LocationCurve wallLoc = wall.Location as LocationCurve;
            if (wallLoc == null)
                throw new Exception("Wall has no location curve.");

            Line wallLine = wallLoc.Curve as Line;
            if (wallLine == null)
                throw new Exception("?��??�支?�直線�?");

            XYZ wallDir = wallLine.Direction.Normalize();
            // 使用 wall.Orientation ?��?外�??��?線�?永�??��?室�?�?
            XYZ wallNormal = wall.Orientation.Normalize();
            // 將起始�?從�?中�?線�?移到外�??��??�個�??�度�?
            double halfWallThickness = wall.Width / 2.0; // 已�???feet
            XYZ wallExteriorStart = wallLine.GetEndPoint(0) + wallNormal * halfWallThickness;

            using (Transaction trans = new Transaction(doc, $"建�?立面?�板: {panelName}"))
            {
                trans.Start();

                try
                {
                    Solid solid;

                    switch (geometryType)
                    {
                        case "beveled_opening":
                            solid = CreateBeveledOpeningSolid(
                                wallExteriorStart, wallDir, wallNormal,
                                positionAlongWall, positionZ, width, height,
                                openingWidth, openingHeight, bevelDepth, thickness,
                                bevelDirection, offset);
                            break;

                        case "angled_panel":
                            solid = CreateAngledPanelSolid(
                                wallExteriorStart, wallDir, wallNormal,
                                positionAlongWall, positionZ, width, height,
                                thickness, offset, tiltAngle, tiltAxis);
                            break;

                        case "rounded_opening":
                            solid = CreateRoundedOpeningSolid(
                                wallExteriorStart, wallDir, wallNormal,
                                positionAlongWall, positionZ, width, height,
                                openingWidth, openingHeight, depth, thickness,
                                cornerRadius, openingShape, offset);
                            break;

                        case "flat_panel":
                            solid = CreateFlatPanelSolid(
                                wallExteriorStart, wallDir, wallNormal,
                                positionAlongWall, positionZ, width, height,
                                thickness, offset);
                            break;

                        case "curved_panel":
                        default:
                            solid = CreateCurvedPanelSolid(
                                wallExteriorStart, wallDir, wallNormal,
                                positionAlongWall, positionZ, width, height,
                                depth, thickness, curveType, offset);
                            break;
                    }

                    // 建�? DirectShape
                    DirectShape ds = DirectShape.CreateElement(
                        doc, new ElementId((IdType)(int)BuiltInCategory.OST_GenericModel));
                    ds.ApplicationId = "RevitMCP_FacadePanel";
                    ds.ApplicationDataId = panelName;
                    ds.SetShape(new GeometryObject[] { solid });

                    // ?��?覆寫
                    Material mat = FindOrCreateFacadeMaterial(doc, colorHex, panelName);
                    ApplyMaterialOverride(doc, ds.Id, mat);

                    trans.Commit();

                    return new
                    {
                        Success = true,
                        ElementId = ds.Id.GetIdValue(),
                        Name = panelName,
                        GeometryType = geometryType,
                        Width = width,
                        Height = height,
                        Depth = depth,
                        Color = colorHex,
                        Message = $"?��?建�?立面?�板: {panelName} ({geometryType}), ID: {ds.Id.GetIdValue()}"
                    };
                }
                catch (Exception ex)
                {
                    if (trans.GetStatus() == TransactionStatus.Started)
                        trans.RollBack();
                    throw new Exception($"建�?立面?�板失�?: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 建�?弧形?�板 Solid（弧形截?�沿 Z 軸�??��?
        /// </summary>
        private Solid CreateCurvedPanelSolid(
            XYZ wallStart, XYZ wallDir, XYZ wallNormal,
            double posAlongMm, double posZMm, double widthMm, double heightMm,
            double depthMm, double thicknessMm, string curveType, double offsetMm)
        {
            double w = widthMm / 304.8;
            double h = heightMm / 304.8;
            double d = depthMm / 304.8;
            double t = thicknessMm / 304.8;
            double off = offsetMm / 304.8;
            double posA = posAlongMm / 304.8;
            double posZ = posZMm / 304.8;

            XYZ center = wallStart + wallDir * posA + wallNormal * off;
            XYZ p1 = center - wallDir * (w / 2);
            XYZ p2 = center + wallDir * (w / 2);

            double arcSign = curveType == "concave" ? 1.0 : -1.0;
            XYZ midPt = center + wallNormal * (d * arcSign);

            p1 = new XYZ(p1.X, p1.Y, posZ);
            p2 = new XYZ(p2.X, p2.Y, posZ);
            midPt = new XYZ(midPt.X, midPt.Y, posZ);

            Arc innerArc = Arc.Create(p1, p2, midPt);
            XYZ p1o = p1 + wallNormal * (t * arcSign);
            XYZ p2o = p2 + wallNormal * (t * arcSign);
            XYZ midO = midPt + wallNormal * (t * arcSign);
            Arc outerArc = Arc.Create(p1o, p2o, midO);

            CurveLoop profile = new CurveLoop();
            profile.Append(innerArc);
            profile.Append(Line.CreateBound(p2, p2o));
            profile.Append(outerArc.CreateReversed());
            profile.Append(Line.CreateBound(p1o, p1));

            return GeometryCreationUtilities.CreateExtrusionGeometry(
                new List<CurveLoop> { profile }, XYZ.BasisZ, h);
        }

        /// <summary>
        /// 建�??��??��?�?Solid（�?�?+ ?��??��?中�??�口�?
        /// bevelDirection: "center"(?�勻), "up"(上深), "down"(下深), "left"(左深), "right"(?�深)
        /// </summary>
        private Solid CreateBeveledOpeningSolid(
            XYZ wallStart, XYZ wallDir, XYZ wallNormal,
            double posAlongMm, double posZMm, double framWidthMm, double frameHeightMm,
            double openWidthMm, double openHeightMm, double bevelDepthMm, double frameThickMm,
            string bevelDirection, double offsetMm)
        {
            double fw = framWidthMm / 304.8;
            double fh = frameHeightMm / 304.8;
            double ow = openWidthMm / 304.8;
            double oh = openHeightMm / 304.8;
            double bd = bevelDepthMm / 304.8;
            double ft = frameThickMm / 304.8;
            double off = offsetMm / 304.8;
            double posA = posAlongMm / 304.8;
            double posZ = posZMm / 304.8;

            // 外�?位置
            XYZ center = wallStart + wallDir * posA + wallNormal * off;

            // 外�??��?（�??��?，Z = posZ�?
            XYZ oA = center - wallDir * (fw / 2) + new XYZ(0, 0, posZ);           // 左�?
            XYZ oB = center + wallDir * (fw / 2) + new XYZ(0, 0, posZ);           // ?��?
            XYZ oC = center + wallDir * (fw / 2) + new XYZ(0, 0, posZ + fh);      // ?��?
            XYZ oD = center - wallDir * (fw / 2) + new XYZ(0, 0, posZ + fh);      // 左�?

            // ?��????角�?深入 bevelDepth ?��?置�?
            // ?��? bevelDirection 調整?��??�深�?
            double dTop = bd, dBottom = bd, dLeft = bd, dRight = bd;

            switch (bevelDirection)
            {
                case "up":    dTop = bd * 0.3; dBottom = bd * 1.5; break;
                case "down":  dTop = bd * 1.5; dBottom = bd * 0.3; break;
                case "left":  dLeft = bd * 0.3; dRight = bd * 1.5; break;
                case "right": dLeft = bd * 1.5; dRight = bd * 0.3; break;
                // center: ?�勻深度
            }

            double innerCenterX_offset = 0;
            double innerCenterZ_offset = 0;

            XYZ innerCenter = center + wallNormal * bd;

            XYZ iA = innerCenter - wallDir * (ow / 2) + new XYZ(0, 0, posZ + (fh - oh) / 2);
            XYZ iB = innerCenter + wallDir * (ow / 2) + new XYZ(0, 0, posZ + (fh - oh) / 2);
            XYZ iC = innerCenter + wallDir * (ow / 2) + new XYZ(0, 0, posZ + (fh + oh) / 2);
            XYZ iD = innerCenter - wallDir * (ow / 2) + new XYZ(0, 0, posZ + (fh + oh) / 2);

            // 對�??�方?��?微調：�?移內?�口位置
            XYZ dirShift = XYZ.Zero;
            switch (bevelDirection)
            {
                case "up":    dirShift = new XYZ(0, 0, (fh - oh) * 0.15); break;
                case "down":  dirShift = new XYZ(0, 0, -(fh - oh) * 0.15); break;
                case "left":  dirShift = -wallDir * (fw - ow) * 0.15; break;
                case "right": dirShift = wallDir * (fw - ow) * 0.15; break;
            }
            iA = iA + dirShift;
            iB = iB + dirShift;
            iC = iC + dirShift;
            iD = iD + dirShift;

            // 建�?幾�?：用 4 ?�梯形面 + 外�??�面組�??�實�?
            // 使用 BooleanOperationsUtils：�?框實�?- ?��????字�?形空??
            // ?��?：建立�?�?box，建立內?��??��?塔形 void，�?布�?減�?

            // 外�? solid：矩形截?�沿法�??�出
            CurveLoop outerProfile = new CurveLoop();
            XYZ oA2 = new XYZ(oA.X, oA.Y, oA.Z);
            XYZ oB2 = new XYZ(oB.X, oB.Y, oB.Z);
            XYZ oC2 = new XYZ(oC.X, oC.Y, oC.Z);
            XYZ oD2 = new XYZ(oD.X, oD.Y, oD.Z);

            outerProfile.Append(Line.CreateBound(oA2, oB2));
            outerProfile.Append(Line.CreateBound(oB2, oC2));
            outerProfile.Append(Line.CreateBound(oC2, oD2));
            outerProfile.Append(Line.CreateBound(oD2, oA2));

            Solid outerBox = GeometryCreationUtilities.CreateExtrusionGeometry(
                new List<CurveLoop> { outerProfile },
                wallNormal,
                bd + ft
            );

            // ?�部?��?塔形?�割：用 CreateBlendGeometry ?�逐面構建
            // 簡�?：用較�??�矩形在 bevelDepth 位置建�?，�?布�?減�?
            CurveLoop innerProfile = new CurveLoop();
            innerProfile.Append(Line.CreateBound(iA, iB));
            innerProfile.Append(Line.CreateBound(iB, iC));
            innerProfile.Append(Line.CreateBound(iC, iD));
            innerProfile.Append(Line.CreateBound(iD, iA));

            Solid innerVoid = GeometryCreationUtilities.CreateExtrusionGeometry(
                new List<CurveLoop> { innerProfile },
                wallNormal,
                ft + 0.01 // 穿透整?��?�?
            );

            // 布�?減�?：�?�?- ?��???
            Solid result = BooleanOperationsUtils.ExecuteBooleanOperation(
                outerBox, innerVoid, BooleanOperationsType.Difference);

            return result;
        }

        /// <summary>
        /// 建�?平面?�板 Solid（簡?�矩形截?�沿法�??�出�?
        /// </summary>
        private Solid CreateFlatPanelSolid(
            XYZ wallStart, XYZ wallDir, XYZ wallNormal,
            double posAlongMm, double posZMm, double widthMm, double heightMm,
            double thicknessMm, double offsetMm)
        {
            double w = widthMm / 304.8;
            double h = heightMm / 304.8;
            double t = thicknessMm / 304.8;
            double off = offsetMm / 304.8;
            double posA = posAlongMm / 304.8;
            double posZ = posZMm / 304.8;

            XYZ center = wallStart + wallDir * posA + wallNormal * off;
            XYZ p1 = center - wallDir * (w / 2) + new XYZ(0, 0, posZ);
            XYZ p2 = center + wallDir * (w / 2) + new XYZ(0, 0, posZ);
            XYZ p3 = center + wallDir * (w / 2) + new XYZ(0, 0, posZ + h);
            XYZ p4 = center - wallDir * (w / 2) + new XYZ(0, 0, posZ + h);

            CurveLoop profile = new CurveLoop();
            profile.Append(Line.CreateBound(p1, p2));
            profile.Append(Line.CreateBound(p2, p3));
            profile.Append(Line.CreateBound(p3, p4));
            profile.Append(Line.CreateBound(p4, p1));

            return GeometryCreationUtilities.CreateExtrusionGeometry(
                new List<CurveLoop> { profile }, wallNormal, t);
        }

        /// <summary>
        /// 建�??��?平板 Solid（平?�面?��?軸�?轉�?定�?度�?
        /// tiltAxis: "horizontal"（�?水平軸�?後傾?��?, "vertical"（�??�直軸左?�傾?��?
        /// </summary>
        private Solid CreateAngledPanelSolid(
            XYZ wallStart, XYZ wallDir, XYZ wallNormal,
            double posAlongMm, double posZMm, double widthMm, double heightMm,
            double thicknessMm, double offsetMm, double tiltAngleDeg, string tiltAxis)
        {
            double w = widthMm / 304.8;
            double h = heightMm / 304.8;
            double t = thicknessMm / 304.8;
            double off = offsetMm / 304.8;
            double posA = posAlongMm / 304.8;
            double posZ = posZMm / 304.8;
            double angleRad = tiltAngleDeg * Math.PI / 180.0;

            XYZ center = wallStart + wallDir * posA + wallNormal * off;

            // ?�板?��?（未?��??��?
            XYZ p1 = new XYZ(-w / 2, 0, 0);       // 左�?
            XYZ p2 = new XYZ(w / 2, 0, 0);         // ?��?
            XYZ p3 = new XYZ(w / 2, 0, h);          // ?��?
            XYZ p4 = new XYZ(-w / 2, 0, h);         // 左�?

            // 套用?��?
            if (tiltAxis == "horizontal")
            {
                // 繞水平軸（wallDir）�?轉�?上�??�傾?��???
                double dz = Math.Sin(angleRad) * h / 2;
                double dy = (1 - Math.Cos(angleRad)) * h / 2;
                p1 = new XYZ(p1.X, p1.Y + dy - Math.Sin(angleRad) * 0, p1.Z - dz);
                p2 = new XYZ(p2.X, p2.Y + dy - Math.Sin(angleRad) * 0, p2.Z - dz);
                p3 = new XYZ(p3.X, p3.Y - dy + Math.Sin(angleRad) * h, p3.Z + dz - h + h * Math.Cos(angleRad));
                p4 = new XYZ(p4.X, p4.Y - dy + Math.Sin(angleRad) * h, p4.Z + dz - h + h * Math.Cos(angleRad));

                // 簡�?：直?��?移�?下�???normal ?��?
                double topOffset = Math.Tan(angleRad) * h / 2;
                p1 = new XYZ(-w / 2, -topOffset, 0);
                p2 = new XYZ(w / 2, -topOffset, 0);
                p3 = new XYZ(w / 2, topOffset, h);
                p4 = new XYZ(-w / 2, topOffset, h);
            }
            else // vertical
            {
                // 繞�??�軸?��?：左?��??��??�移
                double sideOffset = Math.Tan(angleRad) * w / 2;
                p1 = new XYZ(-w / 2, -sideOffset, 0);
                p2 = new XYZ(w / 2, sideOffset, 0);
                p3 = new XYZ(w / 2, sideOffset, h);
                p4 = new XYZ(-w / 2, -sideOffset, h);
            }

            // 轉�??��??�座�?
            Transform localToWorld = Transform.Identity;
            localToWorld.BasisX = wallDir;
            localToWorld.BasisY = wallNormal;
            localToWorld.BasisZ = XYZ.BasisZ;
            localToWorld.Origin = center + new XYZ(0, 0, posZ);

            XYZ wp1 = localToWorld.OfPoint(p1);
            XYZ wp2 = localToWorld.OfPoint(p2);
            XYZ wp3 = localToWorld.OfPoint(p3);
            XYZ wp4 = localToWorld.OfPoint(p4);

            // 建�??�面
            CurveLoop frontProfile = new CurveLoop();
            frontProfile.Append(Line.CreateBound(wp1, wp2));
            frontProfile.Append(Line.CreateBound(wp2, wp3));
            frontProfile.Append(Line.CreateBound(wp3, wp4));
            frontProfile.Append(Line.CreateBound(wp4, wp1));

            return GeometryCreationUtilities.CreateExtrusionGeometry(
                new List<CurveLoop> { frontProfile }, wallNormal, t);
        }

        /// <summary>
        /// 建�??��??�口 Solid（�??��??��?角矩形�????
        /// openingShape: "rounded_rect"（�?角矩形�?, "arch"（�??��??��?, "stadium"（�?下�??��?
        /// </summary>
        private Solid CreateRoundedOpeningSolid(
            XYZ wallStart, XYZ wallDir, XYZ wallNormal,
            double posAlongMm, double posZMm, double frameWidthMm, double frameHeightMm,
            double openWidthMm, double openHeightMm, double depthMm, double thicknessMm,
            double cornerRadiusMm, string openingShape, double offsetMm)
        {
            double fw = frameWidthMm / 304.8;
            double fh = frameHeightMm / 304.8;
            double ow = openWidthMm / 304.8;
            double oh = openHeightMm / 304.8;
            double dep = depthMm / 304.8;
            double ft = thicknessMm / 304.8;
            double cr = cornerRadiusMm / 304.8;
            double off = offsetMm / 304.8;
            double posA = posAlongMm / 304.8;
            double posZ = posZMm / 304.8;

            // 確�??��??��?不�??��???��寸�?一??
            cr = Math.Min(cr, Math.Min(ow / 2, oh / 2));

            XYZ center = wallStart + wallDir * posA + wallNormal * off;

            // 外�? solid（矩形截?��?沿�?線�??��?
            XYZ oA = center - wallDir * (fw / 2) + new XYZ(0, 0, posZ);
            XYZ oB = center + wallDir * (fw / 2) + new XYZ(0, 0, posZ);
            XYZ oC = center + wallDir * (fw / 2) + new XYZ(0, 0, posZ + fh);
            XYZ oD = center - wallDir * (fw / 2) + new XYZ(0, 0, posZ + fh);

            CurveLoop outerProfile = new CurveLoop();
            outerProfile.Append(Line.CreateBound(oA, oB));
            outerProfile.Append(Line.CreateBound(oB, oC));
            outerProfile.Append(Line.CreateBound(oC, oD));
            outerProfile.Append(Line.CreateBound(oD, oA));

            Solid outerBox = GeometryCreationUtilities.CreateExtrusionGeometry(
                new List<CurveLoop> { outerProfile }, wallNormal, dep + ft);

            // ?��???solid（�?角矩形�??�於布�?減�?�?
            XYZ iCenter = center + wallNormal * ft; // 從表?��?度�?後�?�?
            double iLeft = -ow / 2;
            double iRight = ow / 2;
            double iBottom = posZ + (fh - oh) / 2;
            double iTop = posZ + (fh + oh) / 2;

            CurveLoop innerProfile = CreateRoundedRectProfile(
                iCenter, wallDir, iLeft, iRight, iBottom, iTop, cr, openingShape);

            Solid innerVoid = GeometryCreationUtilities.CreateExtrusionGeometry(
                new List<CurveLoop> { innerProfile }, wallNormal, dep + 0.01);

            return BooleanOperationsUtils.ExecuteBooleanOperation(
                outerBox, innerVoid, BooleanOperationsType.Difference);
        }

        /// <summary>
        /// 建�??��??�形 CurveLoop 輪�?
        /// </summary>
        private CurveLoop CreateRoundedRectProfile(
            XYZ center, XYZ wallDir,
            double left, double right, double bottom, double top,
            double radius, string shape)
        {
            CurveLoop loop = new CurveLoop();

            XYZ pBL = center + wallDir * left + new XYZ(0, 0, bottom);   // 左�?
            XYZ pBR = center + wallDir * right + new XYZ(0, 0, bottom);  // ?��?
            XYZ pTR = center + wallDir * right + new XYZ(0, 0, top);     // ?��?
            XYZ pTL = center + wallDir * left + new XYZ(0, 0, top);      // 左�?

            if (radius <= 0.001 || shape == "rect")
            {
                // ?��?�?
                loop.Append(Line.CreateBound(pBL, pBR));
                loop.Append(Line.CreateBound(pBR, pTR));
                loop.Append(Line.CreateBound(pTR, pTL));
                loop.Append(Line.CreateBound(pTL, pBL));
                return loop;
            }

            double r = radius;

            if (shape == "arch")
            {
                // 上方?�拱：�??�直角�?上方?��?�?
                double archRadius = (right - left) / 2;
                double archCenterZ = top - archRadius;

                // 下左 ??下右
                loop.Append(Line.CreateBound(pBL, pBR));
                // 下右 ???�側?�起�?
                XYZ archStartR = center + wallDir * right + new XYZ(0, 0, archCenterZ);
                loop.Append(Line.CreateBound(pBR, archStartR));
                // ?�側 ???�拱????左側
                XYZ archTop = center + new XYZ(0, 0, top);
                XYZ archStartL = center + wallDir * left + new XYZ(0, 0, archCenterZ);
                Arc arch = Arc.Create(archStartR, archStartL, archTop);
                loop.Append(arch);
                // 左側?��?�???下左
                loop.Append(Line.CreateBound(archStartL, pBL));
                return loop;
            }

            // rounded_rect / stadium：�?角帶?�弧
            // ?��??��?弧中�?
            XYZ cBL = center + wallDir * (left + r) + new XYZ(0, 0, bottom + r);
            XYZ cBR = center + wallDir * (right - r) + new XYZ(0, 0, bottom + r);
            XYZ cTR = center + wallDir * (right - r) + new XYZ(0, 0, top - r);
            XYZ cTL = center + wallDir * (left + r) + new XYZ(0, 0, top - r);

            // 底�?（左下�?結�? ???��?角�?始�?
            XYZ bl_end = center + wallDir * (left + r) + new XYZ(0, 0, bottom);
            XYZ br_start = center + wallDir * (right - r) + new XYZ(0, 0, bottom);
            if (bl_end.DistanceTo(br_start) > 0.001)
                loop.Append(Line.CreateBound(bl_end, br_start));

            // ?��?角�?�?
            XYZ br_end = center + wallDir * right + new XYZ(0, 0, bottom + r);
            XYZ br_mid = cBR + (wallDir * r + new XYZ(0, 0, -r)).Normalize() * r;
            Arc arcBR = Arc.Create(br_start, br_end, br_mid);
            loop.Append(arcBR);

            // ?��?
            XYZ tr_start = center + wallDir * right + new XYZ(0, 0, top - r);
            if (br_end.DistanceTo(tr_start) > 0.001)
                loop.Append(Line.CreateBound(br_end, tr_start));

            // ?��?角�?�?
            XYZ tr_end = center + wallDir * (right - r) + new XYZ(0, 0, top);
            XYZ tr_mid = cTR + (wallDir * r + new XYZ(0, 0, r)).Normalize() * r;
            Arc arcTR = Arc.Create(tr_start, tr_end, tr_mid);
            loop.Append(arcTR);

            // ?��?
            XYZ tl_start = center + wallDir * (left + r) + new XYZ(0, 0, top);
            if (tr_end.DistanceTo(tl_start) > 0.001)
                loop.Append(Line.CreateBound(tr_end, tl_start));

            // 左�?角�?�?
            XYZ tl_end = center + wallDir * left + new XYZ(0, 0, top - r);
            XYZ tl_mid = cTL + (wallDir * (-r) + new XYZ(0, 0, r)).Normalize() * r;
            Arc arcTL = Arc.Create(tl_start, tl_end, tl_mid);
            loop.Append(arcTL);

            // 左�?
            XYZ bl_start = center + wallDir * left + new XYZ(0, 0, bottom + r);
            if (tl_end.DistanceTo(bl_start) > 0.001)
                loop.Append(Line.CreateBound(tl_end, bl_start));

            // 左�?角�?�?
            XYZ bl_mid = cBL + (wallDir * (-r) + new XYZ(0, 0, -r)).Normalize() * r;
            Arc arcBL = Arc.Create(bl_start, bl_end, bl_mid);
            loop.Append(arcBL);

            return loop;
        }

        /// <summary>
        /// ??DirectShape 套用?��?覆寫
        /// </summary>
        private void ApplyMaterialOverride(Document doc, ElementId elementId, Material mat)
        {
            View activeView = doc.ActiveView;
            if (activeView == null) return;

            OverrideGraphicSettings ogs = new OverrideGraphicSettings();
            ogs.SetSurfaceForegroundPatternColor(mat.Color);

            FillPatternElement solidFill = FillPatternElement.GetFillPatternElementByName(
                doc, FillPatternTarget.Drafting, "<Solid fill>");
            if (solidFill == null)
            {
                solidFill = new FilteredElementCollector(doc)
                    .OfClass(typeof(FillPatternElement))
                    .Cast<FillPatternElement>()
                    .FirstOrDefault(fp => fp.GetFillPattern().IsSolidFill);
            }
            if (solidFill != null)
                ogs.SetSurfaceForegroundPatternId(solidFill.Id);

            activeView.SetElementOverrides(elementId, ogs);
        }

        /// <summary>
        /// ?�次建�??�面立面（根??AI ?��?結�?�?
        /// </summary>
        private object CreateFacadeFromAnalysis(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;
            UIDocument uidoc = _uiApp.ActiveUIDocument;

            // �???�數
            IdType? wallId = parameters["wallId"]?.Value<IdType>();

            JObject facadeLayers = parameters["facadeLayers"] as JObject;
            if (facadeLayers == null)
                throw new Exception("請�?�?facadeLayers ?�數");

            JObject outerLayer = facadeLayers["outer"] as JObject;
            if (outerLayer == null)
                throw new Exception("請�?�?facadeLayers.outer ?�數");

            double globalOffset = outerLayer["offset"]?.Value<double>() ?? 200;
            double gap = outerLayer["gap"]?.Value<double>() ?? 20;
            double bandHeight = outerLayer["horizontalBandHeight"]?.Value<double>() ?? 0;
            double floorHeight = outerLayer["floorHeight"]?.Value<double>() ?? 3600;

            JArray panelTypesArray = outerLayer["panelTypes"] as JArray;
            JArray patternArray = outerLayer["pattern"] as JArray;

            if (panelTypesArray == null || panelTypesArray.Count == 0)
                throw new Exception("請�?供至少�???panelTypes");
            if (patternArray == null || patternArray.Count == 0)
                throw new Exception("請�?�?pattern ?��??�陣");

            // ?��??��?
            Wall wall = null;
            if (wallId.HasValue)
            {
                wall = doc.GetElement(new ElementId(wallId.Value)) as Wall;
            }
            else
            {
                var selection = uidoc.Selection.GetElementIds();
                if (selection.Count > 0)
                    wall = doc.GetElement(selection.First()) as Wall;
            }

            if (wall == null)
                throw new Exception("Provide wallId or select a wall.");

            // ?��??��?位置?�方??
            LocationCurve wallLoc = wall.Location as LocationCurve;
            if (wallLoc == null)
                throw new Exception("Wall has no location curve.");

            Line wallLine = wallLoc.Curve as Line;
            if (wallLine == null)
                throw new Exception("?��??�支?�直線�?");

            XYZ wallDir = wallLine.Direction.Normalize();
            // 使用 wall.Orientation ?��?外�??��?線�?永�??��?室�?�?
            XYZ wallNormal = wall.Orientation.Normalize();
            // 將起始�?從�?中�?線�?移到外�??��??�個�??�度�?
            double halfWallThickness = wall.Width / 2.0; // 已�???feet
            XYZ wallStart = wallLine.GetEndPoint(0) + wallNormal * halfWallThickness;
            double wallLength = wallLine.Length * 304.8; // ft ??mm

            // ?��??��??��?高�?（Level 高�? + Base Offset�?
            Level baseLevel = doc.GetElement(wall.LevelId) as Level;
            double wallBaseZ = baseLevel != null ? baseLevel.Elevation : 0; // feet
            Parameter baseOffsetParam = wall.get_Parameter(BuiltInParameter.WALL_BASE_OFFSET);
            double baseOffset = baseOffsetParam != null ? baseOffsetParam.AsDouble() : 0; // feet
            double wallBaseElevationMm = (wallBaseZ + baseOffset) * 304.8; // mm

            // �???�板類�?
            var typeDict = new Dictionary<string, JObject>();
            foreach (JObject ptObj in panelTypesArray)
            {
                string id = ptObj["id"]?.Value<string>();
                if (!string.IsNullOrEmpty(id))
                    typeDict[id] = ptObj;
            }

            // ?��?建�?
            int successCount = 0;
            int failCount = 0;
            var createdPanels = new List<object>();
            var failedPanels = new List<object>();

            using (Transaction trans = new Transaction(doc, "Create curtain facade panels"))
            {
                trans.Start();

                // ?��?建�??�?��??��? DirectShapeType
                var materialCache = new Dictionary<string, Material>();
                var dsTypeCache = new Dictionary<string, DirectShapeType>();
                foreach (var kvp in typeDict)
                {
                    string colorHex = kvp.Value["color"]?.Value<string>() ?? "#808080";
                    string userName = kvp.Value["name"]?.Value<string>() ?? $"FP_{kvp.Key}";
                    if (!materialCache.ContainsKey(kvp.Key))
                    {
                        materialCache[kvp.Key] = FindOrCreateFacadeMaterial(doc, colorHex, userName);
                    }

                    // 建�? DirectShapeType，命?��??? FP_{TypeId}_{?�稱}
                    string dsTypeName = $"FP_{kvp.Key}_{userName}";
                    DirectShapeType existingType = new FilteredElementCollector(doc)
                        .OfClass(typeof(DirectShapeType))
                        .Cast<DirectShapeType>()
                        .FirstOrDefault(t => t.Name == dsTypeName);

                    if (existingType != null)
                    {
                        dsTypeCache[kvp.Key] = existingType;
                    }
                    else
                    {
                        DirectShapeType dsType = DirectShapeType.Create(
                            doc, dsTypeName,
                            new ElementId((IdType)(int)BuiltInCategory.OST_GenericModel));
                        dsTypeCache[kvp.Key] = dsType;
                    }
                }

                // ?��?實�?填滿?��?
                FillPatternElement solidFill = FillPatternElement.GetFillPatternElementByName(
                    doc, FillPatternTarget.Drafting, "<Solid fill>");
                if (solidFill == null)
                {
                    solidFill = new FilteredElementCollector(doc)
                        .OfClass(typeof(FillPatternElement))
                        .Cast<FillPatternElement>()
                        .FirstOrDefault(fp => fp.GetFillPattern().IsSolidFill);
                }

                View activeView = doc.ActiveView;

                // ?�歷每�?�?
                for (int floor = 0; floor < patternArray.Count; floor++)
                {
                    string rowPattern = patternArray[floor]?.Value<string>() ?? "";
                    if (string.IsNullOrEmpty(rowPattern)) continue;

                    double panelH = floorHeight - bandHeight; // ?�板高度
                    double zBase = wallBaseElevationMm + floor * floorHeight; // 此層底部 Z (mm)，�?上�??��?高�?

                    // 計�?此�??�?�面?��?總寬度�??�於對�?�?
                    double totalRowWidth = 0;
                    for (int c = 0; c < rowPattern.Length; c++)
                    {
                        string typeId = rowPattern[c].ToString();
                        if (typeDict.ContainsKey(typeId))
                        {
                            totalRowWidth += typeDict[typeId]["width"]?.Value<double>() ?? 800;
                            if (c < rowPattern.Length - 1) totalRowWidth += gap;
                        }
                    }

                    // 起�? X 位置（置中�?齊�?
                    double startX = (wallLength - totalRowWidth) / 2;
                    double x = startX;

                    for (int col = 0; col < rowPattern.Length; col++)
                    {
                        string typeId = rowPattern[col].ToString();
                        if (!typeDict.ContainsKey(typeId)) continue;

                        JObject pt = typeDict[typeId];
                        double pw = pt["width"]?.Value<double>() ?? 800;
                        double pd = pt["depth"]?.Value<double>() ?? 150;
                        double pThick = pt["thickness"]?.Value<double>() ?? 30;
                        string pCurve = pt["curveType"]?.Value<string>() ?? "concave";
                        string pColor = pt["color"]?.Value<string>() ?? "#808080";
                        string pName = pt["name"]?.Value<string>() ?? $"FP_{typeId}";
                        string pGeomType = pt["geometryType"]?.Value<string>() ?? "curved_panel";

                        // ?�幾何�??��??��???
                        double pTiltAngle = pt["tiltAngle"]?.Value<double>() ?? 15;
                        string pTiltAxis = pt["tiltAxis"]?.Value<string>() ?? "horizontal";
                        double pCornerRadius = pt["cornerRadius"]?.Value<double>() ?? 100;
                        string pOpeningShape = pt["openingShape"]?.Value<string>() ?? "rounded_rect";
                        string pBevelDir = pt["bevelDirection"]?.Value<string>() ?? "center";
                        double pOpenW = pt["openingWidth"]?.Value<double>() ?? (pw * 0.7);
                        double pOpenH = pt["openingHeight"]?.Value<double>() ?? (panelH * 0.7);

                        try
                        {
                            double posAlongMm = x + pw / 2;

                            // ?��? geometryType ?�叫對�??��?
                            Solid solid;
                            switch (pGeomType)
                            {
                                case "beveled_opening":
                                    solid = CreateBeveledOpeningSolid(
                                        wallStart, wallDir, wallNormal,
                                        posAlongMm, zBase, pw, panelH,
                                        pOpenW, pOpenH, pd, pThick,
                                        pBevelDir, globalOffset);
                                    break;

                                case "angled_panel":
                                    solid = CreateAngledPanelSolid(
                                        wallStart, wallDir, wallNormal,
                                        posAlongMm, zBase, pw, panelH,
                                        pThick, globalOffset, pTiltAngle, pTiltAxis);
                                    break;

                                case "rounded_opening":
                                    solid = CreateRoundedOpeningSolid(
                                        wallStart, wallDir, wallNormal,
                                        posAlongMm, zBase, pw, panelH,
                                        pOpenW, pOpenH, pd, pThick,
                                        pCornerRadius, pOpeningShape, globalOffset);
                                    break;

                                case "flat_panel":
                                    solid = CreateFlatPanelSolid(
                                        wallStart, wallDir, wallNormal,
                                        posAlongMm, zBase, pw, panelH,
                                        pThick, globalOffset);
                                    break;

                                case "curved_panel":
                                default:
                                    solid = CreateCurvedPanelSolid(
                                        wallStart, wallDir, wallNormal,
                                        posAlongMm, zBase, pw, panelH,
                                        pd, pThick, pCurve, globalOffset);
                                    break;
                            }

                            // DirectShape ???��?規�?: FP_{TypeId}_F{樓層}_C{欄�?}
                            string dsName = $"FP_{typeId}_F{floor + 1}_C{col + 1}";
                            DirectShape ds = DirectShape.CreateElement(
                                doc,
                                new ElementId((IdType)(int)BuiltInCategory.OST_GenericModel)
                            );
                            ds.ApplicationId = "RevitMCP_FacadePanel";
                            ds.ApplicationDataId = dsName;
                            ds.SetShape(new GeometryObject[] { solid });

                            // ?��? DirectShapeType
                            if (dsTypeCache.ContainsKey(typeId))
                            {
                                ds.SetTypeId(dsTypeCache[typeId].Id);
                            }

                            // ?��?覆寫
                            if (materialCache.ContainsKey(typeId))
                            {
                                ApplyMaterialOverride(doc, ds.Id, materialCache[typeId]);
                            }

                            createdPanels.Add(new
                            {
                                ElementId = ds.Id.GetIdValue(),
                                Name = dsName,
                                Floor = floor + 1,
                                Column = col + 1,
                                TypeId = typeId
                            });

                            successCount++;
                        }
                        catch (Exception ex)
                        {
                            failedPanels.Add(new
                            {
                                Floor = floor + 1,
                                Column = col + 1,
                                TypeId = typeId,
                                Reason = ex.Message
                            });
                            failCount++;
                        }

                        x += pw + gap;
                    }
                }

                // 建�?水平?��?帶�?如�??��?
                if (bandHeight > 0)
                {
                    // 建�??��?�?DirectShapeType
                    string bandTypeName = $"FP_Band_H{bandHeight}";
                    DirectShapeType bandType = new FilteredElementCollector(doc)
                        .OfClass(typeof(DirectShapeType))
                        .Cast<DirectShapeType>()
                        .FirstOrDefault(t => t.Name == bandTypeName);
                    if (bandType == null)
                    {
                        bandType = DirectShapeType.Create(
                            doc, bandTypeName,
                            new ElementId((IdType)(int)BuiltInCategory.OST_GenericModel));
                    }

                    for (int floor = 0; floor < patternArray.Count; floor++)
                    {
                        double panelH = floorHeight - bandHeight;
                        double bandZ = (wallBaseElevationMm + floor * floorHeight + panelH) / 304.8;
                        double bh_ft = bandHeight / 304.8;
                        double bandThick = 50 / 304.8; // ?��?帶�?�?50mm

                        try
                        {
                            // ?��?帶為簡單?�形?�出
                            XYZ b1 = wallStart + wallNormal * (globalOffset / 304.8);
                            XYZ b2 = b1 + wallDir * (wallLength / 304.8);
                            XYZ b3 = b2 + wallNormal * bandThick;
                            XYZ b4 = b1 + wallNormal * bandThick;

                            b1 = new XYZ(b1.X, b1.Y, bandZ);
                            b2 = new XYZ(b2.X, b2.Y, bandZ);
                            b3 = new XYZ(b3.X, b3.Y, bandZ);
                            b4 = new XYZ(b4.X, b4.Y, bandZ);

                            CurveLoop bandProfile = new CurveLoop();
                            bandProfile.Append(Line.CreateBound(b1, b2));
                            bandProfile.Append(Line.CreateBound(b2, b3));
                            bandProfile.Append(Line.CreateBound(b3, b4));
                            bandProfile.Append(Line.CreateBound(b4, b1));

                            Solid bandSolid = GeometryCreationUtilities.CreateExtrusionGeometry(
                                new List<CurveLoop> { bandProfile },
                                XYZ.BasisZ,
                                bh_ft
                            );

                            DirectShape bandDs = DirectShape.CreateElement(
                                doc,
                                new ElementId((IdType)(int)BuiltInCategory.OST_GenericModel)
                            );
                            bandDs.ApplicationId = "RevitMCP_FacadeBand";
                            bandDs.ApplicationDataId = $"FP_Band_F{floor + 1}";
                            bandDs.SetShape(new GeometryObject[] { bandSolid });
                            bandDs.SetTypeId(bandType.Id);
                        }
                        catch
                        {
                            // ?��?帶建立失?��?影響主�?�?
                        }
                    }
                }

                trans.Commit();
            }

            return new
            {
                Success = true,
                WallId = wall.Id.GetIdValue(),
                TotalPanels = successCount + failCount,
                SuccessCount = successCount,
                FailCount = failCount,
                CreatedPanels = createdPanels,
                FailedPanels = failedPanels,
                Message = $"Created curtain facade panels: success={successCount}, failed={failCount}."
            };
        }

        /// <summary>
        /// 建�??��?得�??�面?��???
        /// </summary>
        private Material FindOrCreateFacadeMaterial(Document doc, string colorHex, string baseName)
        {
            colorHex = colorHex.TrimStart('#');
            byte r = Convert.ToByte(colorHex.Substring(0, 2), 16);
            byte g = Convert.ToByte(colorHex.Substring(2, 2), 16);
            byte b = Convert.ToByte(colorHex.Substring(4, 2), 16);

            string materialName = $"FP_MAT_{baseName}";

            Material material = new FilteredElementCollector(doc)
                .OfClass(typeof(Material))
                .Cast<Material>()
                .FirstOrDefault(m => m.Name == materialName);

            if (material == null)
            {
                ElementId newMatId = Material.Create(doc, materialName);
                material = doc.GetElement(newMatId) as Material;
            }

            material.Color = new Color(r, g, b);
            material.Transparency = 0;

            return material;
        }

        #endregion
    }
}
