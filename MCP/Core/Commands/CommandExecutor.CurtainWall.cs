using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
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
    /// 帷幕牆 + 立面面板命令
    /// 來源：PR#11 (@7alexhuang-ux)，經跨版本修正後整合
    /// </summary>
    public partial class CommandExecutor
    {
        private const double CurtainElevationDirectionDotThreshold = 0.98;

        #region 帷幕牆工具

        private object GetCurtainWallInfo(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;
            UIDocument uidoc = _uiApp.ActiveUIDocument;

            IdType? elementId = parameters["elementId"]?.Value<IdType>();
            Wall wall = null;

            // 如果沒有指定 elementId，使用目前選取的元素
            if (elementId.HasValue)
            {
                Element elem = doc.GetElement(new ElementId(elementId.Value));
                wall = elem as Wall;
            }
            else
            {
                var selection = uidoc.Selection.GetElementIds();
                if (selection.Count == 0)
                    throw new Exception("請先選取一個帷幕牆，或指定 elementId");

                Element elem = doc.GetElement(selection.First());
                wall = elem as Wall;
            }

            if (wall == null)
                throw new Exception("選取的元素不是牆");

            // 檢查是否為帷幕牆
            CurtainGrid grid = wall.CurtainGrid;
            if (grid == null)
                throw new Exception("此牆不是帷幕牆（沒有 CurtainGrid）");

            // 取得 Grid 資訊
            var uGridIds = grid.GetUGridLineIds();
            var vGridIds = grid.GetVGridLineIds();
            var panelIds = grid.GetPanelIds();

            // 計算 rows 和 columns
            int rows = uGridIds.Count + 1;    // U Grid = 水平線 = 定義 Row
            int columns = vGridIds.Count + 1; // V Grid = 垂直線 = 定義 Column

            // 收集面板資訊
            var panelTypeDict = new Dictionary<IdType, (string TypeName, string MaterialName, string MaterialColor, int Count)>();
            var panelMatrix = new List<List<int>>(); // [row][col] = typeId

            // 取得牆的位置線來計算方向
            LocationCurve locCurve = wall.Location as LocationCurve;
            Curve curve = locCurve?.Curve;

            // 收集面板並分析
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

                    // 嘗試取得材料資訊
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
                    catch (Exception) { /* 忽略個別元素處理失敗 */ }

                    panelTypeDict[typeIdInt] = (typeName, materialName, materialColor, 0);
                }

                var current = panelTypeDict[typeIdInt];
                panelTypeDict[typeIdInt] = (current.TypeName, current.MaterialName, current.MaterialColor, current.Count + 1);
            }

            // 取得面板尺寸（從第一個面板估算）
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

            // 組織回傳資料
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
        /// 取得專案中所有可用的帷幕面板類型
        /// </summary>
        private object GetCurtainPanelTypes(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;

            // 取得所有 Curtain Panel 類型
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
                    catch (Exception) { /* 忽略個別元素處理失敗 */ }

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
        /// 建立每一道帷幕牆的外立面視圖，並套用「帷幕立面」視圖樣板。
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
            bool applyViewTemplate = parameters["applyViewTemplate"]?.Value<bool>() ?? true;
            string nameSeparator = parameters["nameSeparator"]?.Value<string>() ?? "";
            bool dryRun = parameters["dryRun"]?.Value<bool>() ?? false;

            ViewFamilyType elevationType = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>()
                .FirstOrDefault(vft => vft.ViewFamily == ViewFamily.Elevation);

            if (elevationType == null)
                throw new Exception("找不到 Elevation 的 ViewFamilyType");

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
            var createdViews = new List<(ViewSection View, Wall Wall, XYZ WallMidPoint, XYZ MarkerPoint)>();
            var templateWarnings = new List<string>();
            View viewTemplate = null;
            bool templateCreated = false;
            bool templateUpdated = false;

            if (dryRun)
            {
                foreach (Wall wall in curtainWalls)
                {
                    Level level = doc.GetElement(wall.LevelId) as Level;
                    string levelName = level?.Name ?? "未指定樓層";
                    string mark = GetCurtainWallMark(wall);
                    string viewName = MakeUniqueCurtainElevationViewName(existingNames, $"{levelName}{nameSeparator}{mark}");
                    CurtainElevationExteriorResolution exterior = ResolveCurtainElevationExteriorSide(wall);

                    created.Add(new
                    {
                        WallId = wall.Id.GetIdValue(),
                        ViewId = (IdType)0,
                        ViewName = viewName,
                        LevelName = levelName,
                        Mark = mark,
                        MarkerId = (IdType)0,
                        WallFlipped = wall.Flipped,
                        ResolvedExteriorSide = exterior?.SideName,
                        ResolvedExteriorSource = exterior?.Source,
                        ResolvedExteriorDirection = ToCurtainElevationXyz(exterior?.ExteriorDirection),
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
                    Created = created,
                    Skipped = skipped,
                    TemplateWarnings = templateWarnings
                };
            }

            using (Transaction trans = TransactionHelper.Begin(doc, "建立帷幕牆外立面視圖"))
            {
                trans.Start();

                foreach (Wall wall in curtainWalls)
                {
                    Level level = doc.GetElement(wall.LevelId) as Level;
                    string levelName = level?.Name ?? "未指定樓層";
                    string mark = GetCurtainWallMark(wall);

                    try
                    {
                        LocationCurve loc = wall.Location as LocationCurve;
                        if (loc == null)
                        {
                            skipped.Add(new { WallId = wall.Id.GetIdValue(), LevelName = levelName, Mark = mark, Reason = "牆沒有 LocationCurve" });
                            continue;
                        }

                        BoundingBoxXYZ wallBox = wall.get_BoundingBox(null);
                        if (wallBox == null)
                        {
                            skipped.Add(new { WallId = wall.Id.GetIdValue(), LevelName = levelName, Mark = mark, Reason = "無法取得牆 BoundingBox" });
                            continue;
                        }

                        ViewPlan placementView = explicitPlacementView
                            ?? ResolveCurtainElevationPlanForWall(wall, activePlan, floorPlansByLevel);
                        if (placementView == null)
                        {
                            skipped.Add(new { WallId = wall.Id.GetIdValue(), LevelName = levelName, Mark = mark, Reason = "找不到可放置 ElevationMarker 的平面視圖" });
                            continue;
                        }

                        XYZ wallMid = loc.Curve.Evaluate(0.5, true);
                        CurtainElevationExteriorResolution exterior = ResolveCurtainElevationExteriorSide(wall);
                        XYZ outward = exterior?.ExteriorDirection;
                        if (outward == null)
                        {
                            skipped.Add(new { WallId = wall.Id.GetIdValue(), LevelName = levelName, Mark = mark, Reason = "無法判斷 wall.Orientation" });
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
                                Reason = $"立面方向驗證失敗，DirectionDot={directionResult.DirectionDot:F4}",
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
                        CurtainElevationCropResult cropResult = ConfigureCurtainElevationCrop(doc, elevationView, wall, wallMid, markerPoint, horizontalMarginFt, verticalMarginFt, fallbackDepthFt);
                        ConfigureCurtainElevationFarClip(elevationView, cropResult.FarClipDepthFt);

                        createdViews.Add((elevationView, wall, wallMid, markerPoint));
                        created.Add(new
                        {
                            WallId = wall.Id.GetIdValue(),
                            ViewId = elevationView.Id.GetIdValue(),
                            ViewName = elevationView.Name,
                            LevelName = levelName,
                            Mark = mark,
                            IsPersistentOutput = true,
                            MarkerId = marker.Id.GetIdValue(),
                            FarClipDepthMm = Math.Round(cropResult.FarClipDepthFt * 304.8, 1),
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

                if (applyViewTemplate && createdViews.Count > 0)
                {
                    viewTemplate = FindCurtainElevationViewTemplate(doc, viewTemplateName);
                    if (viewTemplate == null)
                    {
                        viewTemplate = createdViews[0].View.CreateViewTemplate();
                        viewTemplate.Name = viewTemplateName;
                        templateCreated = true;
                    }

                    ConfigureCurtainElevationViewTemplate(doc, viewTemplate, templateWarnings);
                    templateUpdated = true;

                    foreach (var item in createdViews)
                    {
                        item.View.ViewTemplateId = viewTemplate.Id;
                        CurtainElevationCropResult cropResult = ConfigureCurtainElevationCrop(doc, item.View, item.Wall, item.WallMidPoint, item.MarkerPoint, horizontalMarginFt, verticalMarginFt, fallbackDepthFt);
                        ConfigureCurtainElevationFarClip(item.View, cropResult.FarClipDepthFt);
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
                Created = created,
                Skipped = skipped,
                TemplateWarnings = templateWarnings
            };
        }

        private object DiagnoseCurtainWallElevationDirection(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;
            UIDocument uidoc = _uiApp.ActiveUIDocument;

            IdType wallId = parameters["wallId"]?.Value<IdType>() ?? 0;
            if (wallId == 0)
                throw new Exception("必須指定 wallId");

            int scale = parameters["scale"]?.Value<int>() ?? 50;
            double offsetFt = (parameters["offsetMm"]?.Value<double>() ?? 1500.0) / 304.8;
            bool includeCropDiagnostics = parameters["includeCropDiagnostics"]?.Value<bool>() ?? false;

            Wall wall = doc.GetElement(new ElementId(wallId)) as Wall;
            if (wall == null)
                throw new Exception($"找不到 Wall ID: {wallId}");
            if (wall.CurtainGrid == null)
                throw new Exception($"Wall ID {wallId} 不是 CurtainGrid != null 的帷幕牆");

            LocationCurve loc = wall.Location as LocationCurve;
            if (loc == null || loc.Curve == null)
                throw new Exception($"Wall ID {wallId} 沒有可用 LocationCurve");

            ViewFamilyType elevationType = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>()
                .FirstOrDefault(vft => vft.ViewFamily == ViewFamily.Elevation);
            if (elevationType == null)
                throw new Exception("找不到 Elevation 的 ViewFamilyType");

            ViewPlan explicitPlacementView = ResolveCurtainElevationPlacementView(doc, parameters);
            Dictionary<ElementId, ViewPlan> floorPlansByLevel = GetCurtainElevationFloorPlansByLevel(doc);
            ViewPlan activePlan = uidoc.ActiveView as ViewPlan;
            if (activePlan != null && activePlan.IsTemplate)
                activePlan = null;

            ViewPlan placementView = explicitPlacementView
                ?? ResolveCurtainElevationPlanForWall(wall, activePlan, floorPlansByLevel);
            if (placementView == null)
                throw new Exception("找不到可用來放置 ElevationMarker 的 ViewPlan");

            Level level = doc.GetElement(wall.LevelId) as Level;
            Curve curve = loc.Curve;
            XYZ start = curve.GetEndPoint(0);
            XYZ end = curve.GetEndPoint(1);
            XYZ wallMid = curve.Evaluate(0.5, true);
            XYZ wallDirection = FlattenAndNormalize(end - start);
            XYZ wallOrientation = FlattenAndNormalize(wall.Orientation);
            if (wallOrientation == null)
                throw new Exception("無法判斷 wall.Orientation");

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

            using (Transaction trans = new Transaction(doc, "診斷帷幕立面方向（Rollback）"))
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
                    throw new Exception($"placementViewId {placementViewId} 不是可用的 ViewPlan");
                return view;
            }

            if (!string.IsNullOrWhiteSpace(placementViewName))
            {
                ViewPlan view = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewPlan))
                    .Cast<ViewPlan>()
                    .FirstOrDefault(v => !v.IsTemplate && v.Name == placementViewName);
                if (view == null)
                    throw new Exception($"找不到 placementViewName 指定的 ViewPlan: {placementViewName}");
                return view;
            }

            return null;
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
            CurtainElevationGeometryPointResult pointResult = GetCurtainElevationGeometryPoints(doc, wall, view);
            CurtainElevationGeometryPointResult view2DPointResult = GetCurtainElevationView2DPoints(doc, wall, view);
            Transform viewCropFrame = viewCrop?.Transform;
            Transform view2DFrame = GetCurtainElevationView2DFrame(view, viewCropFrame);
            Transform wallAlignedFrame = GetCurtainElevationWallAlignedCropFrame(wall, wallMidPoint, markerPoint);
            CurtainElevationLocalExtents viewExtents = GetCurtainElevationLocalExtents(pointResult.Records, viewCropFrame);
            CurtainElevationLocalExtents view2DExtents = GetCurtainElevationLocalExtents(view2DPointResult.Records, view2DFrame);
            CurtainElevationLocalExtents view2DCropFrameExtents = ConvertCurtainElevationView2DExtentsToCropFrameExtents(view2DFrame, view2DExtents, viewCropFrame, 0, 0);
            CurtainElevationLocalExtents wallExtents = GetCurtainElevationLocalExtents(pointResult.Records, wallAlignedFrame);

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

            double depthFt = Math.Max(cropFrameExtents.Max.Z - cropFrameExtents.Min.Z, 1.0 / 304.8);
            result.FarClipDepthFt = depthFt;
            result.LocalMin = cropFrameExtents.Min;
            result.LocalMax = cropFrameExtents.Max;
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
                    result.RegionShapeFallbackReason = "無法判斷 crop region plane normal";
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

        private void ConfigureCurtainElevationFarClip(ViewSection view, double depthFt)
        {
            SetViewParameterByBuiltInName(view, "VIEWER_BOUND_ACTIVE", 1);
            SetViewParameterByBuiltInName(view, "VIEWER_BOUND_FAR_CLIPPING", 2);
            SetViewParameterByBuiltInName(view, "VIEWER_BOUND_OFFSET", depthFt);
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
                new ElementId((IdType)(int)BuiltInCategory.OST_WallTags)
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
        /// 建立新的帷幕面板類型（含材料）
        /// </summary>
        private object CreateCurtainPanelType(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;

            string typeName = parameters["typeName"]?.Value<string>();
            string colorHex = parameters["color"]?.Value<string>() ?? "#808080";
            int transparency = parameters["transparency"]?.Value<int>() ?? 0;
            string basePanelTypeName = parameters["basePanelType"]?.Value<string>();

            if (string.IsNullOrEmpty(typeName))
                throw new Exception("請指定新類型名稱 (typeName)");

            // 解析顏色
            colorHex = colorHex.TrimStart('#');
            byte r = Convert.ToByte(colorHex.Substring(0, 2), 16);
            byte g = Convert.ToByte(colorHex.Substring(2, 2), 16);
            byte b = Convert.ToByte(colorHex.Substring(4, 2), 16);
            Color revitColor = new Color(r, g, b);

            using (Transaction trans = new Transaction(doc, "建立帷幕面板類型"))
            {
                trans.Start();

                // 1. 找到基礎面板類型來複製
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
                    // 使用預設的 System Panel
                    basePanelType = new FilteredElementCollector(doc)
                        .OfCategory(BuiltInCategory.OST_CurtainWallPanels)
                        .WhereElementIsElementType()
                        .Cast<ElementType>()
                        .FirstOrDefault();
                }

                if (basePanelType == null)
                    throw new Exception("找不到可用的帷幕面板類型作為基礎");

                // 2. 檢查是否已存在同名類型
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
                    // 3. 複製類型
                    newPanelType = basePanelType.Duplicate(typeName) as ElementType;
                    isNewType = true;
                }

                // 4. 建立或更新材料
                string materialName = $"CW_PNL_{typeName}";
                Material material = new FilteredElementCollector(doc)
                    .OfClass(typeof(Material))
                    .Cast<Material>()
                    .FirstOrDefault(m => m.Name == materialName);

                if (material == null)
                {
                    // 建立新材料
                    ElementId newMatId = Material.Create(doc, materialName);
                    material = doc.GetElement(newMatId) as Material;
                }

                // 設定材料屬性
                material.Color = revitColor;
                material.Transparency = transparency;

                // 5. 將材料指派給面板類型
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
                        ? $"成功建立新面板類型: {typeName}"
                        : $"已更新既有面板類型: {typeName}"
                };
            }
        }

        /// <summary>
        /// 批次套用面板排列模式
        /// 支援兩種模式：
        /// 1. typeMapping + matrix: 使用字母矩陣配合類型映射
        /// 2. pattern: 直接使用 TypeId 矩陣
        /// </summary>
        private object ApplyPanelPattern(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;
            UIDocument uidoc = _uiApp.ActiveUIDocument;

            IdType? wallElementId = parameters["elementId"]?.Value<IdType>() ?? parameters["wallId"]?.Value<IdType>();
            JObject typeMapping = parameters["typeMapping"] as JObject;
            JArray matrix = parameters["matrix"] as JArray;
            JArray patternArray = parameters["pattern"] as JArray;

            // 取得帷幕牆
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
                throw new Exception("找不到帷幕牆，請指定 elementId 或選取帷幕牆");

            CurtainGrid grid = wall.CurtainGrid;
            if (grid == null)
                throw new Exception("此牆不是帷幕牆");

            // 建立類型映射字典
            var typeMappingDict = new Dictionary<string, IdType>();
            if (typeMapping != null)
            {
                foreach (var prop in typeMapping.Properties())
                {
                    typeMappingDict[prop.Name] = prop.Value.Value<IdType>();
                }
            }

            // 決定使用哪種模式
            JArray sourceMatrix = matrix ?? patternArray;
            if (sourceMatrix == null)
                throw new Exception("請提供 matrix（字母矩陣 + typeMapping）或 pattern（TypeId 矩陣）");

            // 取得所有面板
            var panelIds = grid.GetPanelIds().ToList();

            // 建立面板位置映射 (依據幾何位置排序)
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

            // 依照位置排序並分配 Row/Col
            // 先依 Z (高度) 分組（由上到下），再依 X 或 Y 排序（由左到右）
            var sortedByZ = panelPositions.OrderByDescending(p => p.Center.Z).ToList();

            // 分組 by Z
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

            // 建立 Row/Col 到 PanelId 的映射
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

            // 套用模式
            int successCount = 0;
            int failCount = 0;
            var failedPanels = new List<object>();

            using (Transaction trans = new Transaction(doc, "套用帷幕面板排列"))
            {
                trans.Start();

                for (int r = 0; r < sourceMatrix.Count && r < rowGroups.Count; r++)
                {
                    JArray rowData = sourceMatrix[r] as JArray;
                    if (rowData == null) continue;

                    for (int c = 0; c < rowData.Count; c++)
                    {
                        if (!panelGrid.ContainsKey((r, c))) continue;

                        // 取得目標類型 ID
                        IdType targetTypeId = 0;
                        var cellValue = rowData[c];

                        if (cellValue.Type == JTokenType.String)
                        {
                            // 字母模式，從 typeMapping 查找
                            string key = cellValue.Value<string>();
                            if (string.IsNullOrEmpty(key)) continue;
                            if (!typeMappingDict.TryGetValue(key, out targetTypeId))
                            {
                                failedPanels.Add(new { Row = r, Col = c, Reason = $"找不到映射: {key}" });
                                failCount++;
                                continue;
                            }
                        }
                        else if (cellValue.Type == JTokenType.Integer)
                        {
                            // 直接 TypeId 模式
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
                            // 取得目標類型
                            ElementType targetType = doc.GetElement(new ElementId(targetTypeId)) as ElementType;
                            if (targetType == null)
                            {
                                failedPanels.Add(new { PanelId = panelId.GetIdValue(), Row = r, Col = c, Reason = $"找不到 TypeId: {targetTypeId}" });
                                failCount++;
                                continue;
                            }

                            // 變更面板類型
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
                Message = $"成功套用 {successCount} 個面板，失敗 {failCount} 個"
            };
        }

        // ============================
        // 立面面板 (Facade Panel) 相關
        // ============================

        /// <summary>
        /// 建立單片立面面板 (DirectShape)
        /// 支援多種幾何類型：curved_panel（弧形面板）、beveled_opening（斜切凹窗框）、flat_panel（平面面板）
        /// </summary>
        private object CreateFacadePanel(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;
            UIDocument uidoc = _uiApp.ActiveUIDocument;

            // 解析共用參數
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

            // 取得牆體
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
                throw new Exception("找不到牆體，請指定 wallId 或選取牆體");

            LocationCurve wallLoc = wall.Location as LocationCurve;
            if (wallLoc == null)
                throw new Exception("無法取得牆體位置線");

            Line wallLine = wallLoc.Curve as Line;
            if (wallLine == null)
                throw new Exception("目前僅支援直線牆");

            XYZ wallDir = wallLine.Direction.Normalize();
            // 使用 wall.Orientation 取得外牆面法線（永遠指向室外）
            XYZ wallNormal = wall.Orientation.Normalize();
            // 將起始點從牆中心線偏移到外牆面（半個牆厚度）
            double halfWallThickness = wall.Width / 2.0; // 已經是 feet
            XYZ wallExteriorStart = wallLine.GetEndPoint(0) + wallNormal * halfWallThickness;

            using (Transaction trans = new Transaction(doc, $"建立立面面板: {panelName}"))
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

                    // 建立 DirectShape
                    DirectShape ds = DirectShape.CreateElement(
                        doc, new ElementId((IdType)(int)BuiltInCategory.OST_GenericModel));
                    ds.ApplicationId = "RevitMCP_FacadePanel";
                    ds.ApplicationDataId = panelName;
                    ds.SetShape(new GeometryObject[] { solid });

                    // 材料覆寫
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
                        Message = $"成功建立立面面板: {panelName} ({geometryType}), ID: {ds.Id.GetIdValue()}"
                    };
                }
                catch (Exception ex)
                {
                    if (trans.GetStatus() == TransactionStatus.Started)
                        trans.RollBack();
                    throw new Exception($"建立立面面板失敗: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 建立弧形面板 Solid（弧形截面沿 Z 軸擠出）
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
        /// 建立斜切凹窗框 Solid（外框 + 斜切面，中心開口）
        /// bevelDirection: "center"(均勻), "up"(上深), "down"(下深), "left"(左深), "right"(右深)
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

            // 外框位置
            XYZ center = wallStart + wallDir * posA + wallNormal * off;

            // 外框四角（牆面上，Z = posZ）
            XYZ oA = center - wallDir * (fw / 2) + new XYZ(0, 0, posZ);           // 左下
            XYZ oB = center + wallDir * (fw / 2) + new XYZ(0, 0, posZ);           // 右下
            XYZ oC = center + wallDir * (fw / 2) + new XYZ(0, 0, posZ + fh);      // 右上
            XYZ oD = center - wallDir * (fw / 2) + new XYZ(0, 0, posZ + fh);      // 左上

            // 內開口四角（深入 bevelDepth 的位置）
            // 根據 bevelDirection 調整各邊的深度
            double dTop = bd, dBottom = bd, dLeft = bd, dRight = bd;

            switch (bevelDirection)
            {
                case "up":    dTop = bd * 0.3; dBottom = bd * 1.5; break;
                case "down":  dTop = bd * 1.5; dBottom = bd * 0.3; break;
                case "left":  dLeft = bd * 0.3; dRight = bd * 1.5; break;
                case "right": dLeft = bd * 1.5; dRight = bd * 0.3; break;
                // center: 均勻深度
            }

            double innerCenterX_offset = 0;
            double innerCenterZ_offset = 0;

            XYZ innerCenter = center + wallNormal * bd;

            XYZ iA = innerCenter - wallDir * (ow / 2) + new XYZ(0, 0, posZ + (fh - oh) / 2);
            XYZ iB = innerCenter + wallDir * (ow / 2) + new XYZ(0, 0, posZ + (fh - oh) / 2);
            XYZ iC = innerCenter + wallDir * (ow / 2) + new XYZ(0, 0, posZ + (fh + oh) / 2);
            XYZ iD = innerCenter - wallDir * (ow / 2) + new XYZ(0, 0, posZ + (fh + oh) / 2);

            // 對斜切方向做微調：偏移內開口位置
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

            // 建立幾何：用 4 個梯形面 + 外框背面組成的實體
            // 使用 BooleanOperationsUtils：外框實體 - 內開口金字塔形空間
            // 方法：建立外框 box，建立內部的金字塔形 void，做布林減法

            // 外框 solid：矩形截面沿法線擠出
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

            // 內部金字塔形切割：用 CreateBlendGeometry 或逐面構建
            // 簡化：用較小的矩形在 bevelDepth 位置建立，做布林減法
            CurveLoop innerProfile = new CurveLoop();
            innerProfile.Append(Line.CreateBound(iA, iB));
            innerProfile.Append(Line.CreateBound(iB, iC));
            innerProfile.Append(Line.CreateBound(iC, iD));
            innerProfile.Append(Line.CreateBound(iD, iA));

            Solid innerVoid = GeometryCreationUtilities.CreateExtrusionGeometry(
                new List<CurveLoop> { innerProfile },
                wallNormal,
                ft + 0.01 // 穿透整個厚度
            );

            // 布林減法：外框 - 內開口
            Solid result = BooleanOperationsUtils.ExecuteBooleanOperation(
                outerBox, innerVoid, BooleanOperationsType.Difference);

            return result;
        }

        /// <summary>
        /// 建立平面面板 Solid（簡單矩形截面沿法線擠出）
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
        /// 建立傾斜平板 Solid（平面面板繞軸旋轉一定角度）
        /// tiltAxis: "horizontal"（繞水平軸前後傾斜）, "vertical"（繞垂直軸左右傾斜）
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

            // 面板四角（未傾斜時）
            XYZ p1 = new XYZ(-w / 2, 0, 0);       // 左下
            XYZ p2 = new XYZ(w / 2, 0, 0);         // 右下
            XYZ p3 = new XYZ(w / 2, 0, h);          // 右上
            XYZ p4 = new XYZ(-w / 2, 0, h);         // 左上

            // 套用傾斜
            if (tiltAxis == "horizontal")
            {
                // 繞水平軸（wallDir）旋轉：上邊前傾或後傾
                double dz = Math.Sin(angleRad) * h / 2;
                double dy = (1 - Math.Cos(angleRad)) * h / 2;
                p1 = new XYZ(p1.X, p1.Y + dy - Math.Sin(angleRad) * 0, p1.Z - dz);
                p2 = new XYZ(p2.X, p2.Y + dy - Math.Sin(angleRad) * 0, p2.Z - dz);
                p3 = new XYZ(p3.X, p3.Y - dy + Math.Sin(angleRad) * h, p3.Z + dz - h + h * Math.Cos(angleRad));
                p4 = new XYZ(p4.X, p4.Y - dy + Math.Sin(angleRad) * h, p4.Z + dz - h + h * Math.Cos(angleRad));

                // 簡化：直接偏移上下邊的 normal 方向
                double topOffset = Math.Tan(angleRad) * h / 2;
                p1 = new XYZ(-w / 2, -topOffset, 0);
                p2 = new XYZ(w / 2, -topOffset, 0);
                p3 = new XYZ(w / 2, topOffset, h);
                p4 = new XYZ(-w / 2, topOffset, h);
            }
            else // vertical
            {
                // 繞垂直軸旋轉：左右邊前後偏移
                double sideOffset = Math.Tan(angleRad) * w / 2;
                p1 = new XYZ(-w / 2, -sideOffset, 0);
                p2 = new XYZ(w / 2, sideOffset, 0);
                p3 = new XYZ(w / 2, sideOffset, h);
                p4 = new XYZ(-w / 2, -sideOffset, h);
            }

            // 轉換到世界座標
            Transform localToWorld = Transform.Identity;
            localToWorld.BasisX = wallDir;
            localToWorld.BasisY = wallNormal;
            localToWorld.BasisZ = XYZ.BasisZ;
            localToWorld.Origin = center + new XYZ(0, 0, posZ);

            XYZ wp1 = localToWorld.OfPoint(p1);
            XYZ wp2 = localToWorld.OfPoint(p2);
            XYZ wp3 = localToWorld.OfPoint(p3);
            XYZ wp4 = localToWorld.OfPoint(p4);

            // 建立前面
            CurveLoop frontProfile = new CurveLoop();
            frontProfile.Append(Line.CreateBound(wp1, wp2));
            frontProfile.Append(Line.CreateBound(wp2, wp3));
            frontProfile.Append(Line.CreateBound(wp3, wp4));
            frontProfile.Append(Line.CreateBound(wp4, wp1));

            return GeometryCreationUtilities.CreateExtrusionGeometry(
                new List<CurveLoop> { frontProfile }, wallNormal, t);
        }

        /// <summary>
        /// 建立圓角開口 Solid（厚牆上的圓角矩形開口）
        /// openingShape: "rounded_rect"（圓角矩形）, "arch"（上方圓拱）, "stadium"（上下半圓）
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

            // 確保圓角半徑不超過開口尺寸的一半
            cr = Math.Min(cr, Math.Min(ow / 2, oh / 2));

            XYZ center = wallStart + wallDir * posA + wallNormal * off;

            // 外框 solid（矩形截面，沿法線擠出）
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

            // 內開口 solid（圓角矩形，用於布林減法）
            XYZ iCenter = center + wallNormal * ft; // 從表面厚度之後開始
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
        /// 建立圓角矩形 CurveLoop 輪廓
        /// </summary>
        private CurveLoop CreateRoundedRectProfile(
            XYZ center, XYZ wallDir,
            double left, double right, double bottom, double top,
            double radius, string shape)
        {
            CurveLoop loop = new CurveLoop();

            XYZ pBL = center + wallDir * left + new XYZ(0, 0, bottom);   // 左下
            XYZ pBR = center + wallDir * right + new XYZ(0, 0, bottom);  // 右下
            XYZ pTR = center + wallDir * right + new XYZ(0, 0, top);     // 右上
            XYZ pTL = center + wallDir * left + new XYZ(0, 0, top);      // 左上

            if (radius <= 0.001 || shape == "rect")
            {
                // 無圓角
                loop.Append(Line.CreateBound(pBL, pBR));
                loop.Append(Line.CreateBound(pBR, pTR));
                loop.Append(Line.CreateBound(pTR, pTL));
                loop.Append(Line.CreateBound(pTL, pBL));
                return loop;
            }

            double r = radius;

            if (shape == "arch")
            {
                // 上方圓拱：下方直角，上方半圓弧
                double archRadius = (right - left) / 2;
                double archCenterZ = top - archRadius;

                // 下左 → 下右
                loop.Append(Line.CreateBound(pBL, pBR));
                // 下右 → 右側拱起點
                XYZ archStartR = center + wallDir * right + new XYZ(0, 0, archCenterZ);
                loop.Append(Line.CreateBound(pBR, archStartR));
                // 右側 → 圓拱頂 → 左側
                XYZ archTop = center + new XYZ(0, 0, top);
                XYZ archStartL = center + wallDir * left + new XYZ(0, 0, archCenterZ);
                Arc arch = Arc.Create(archStartR, archStartL, archTop);
                loop.Append(arch);
                // 左側拱終點 → 下左
                loop.Append(Line.CreateBound(archStartL, pBL));
                return loop;
            }

            // rounded_rect / stadium：四角帶圓弧
            // 各角的圓弧中心
            XYZ cBL = center + wallDir * (left + r) + new XYZ(0, 0, bottom + r);
            XYZ cBR = center + wallDir * (right - r) + new XYZ(0, 0, bottom + r);
            XYZ cTR = center + wallDir * (right - r) + new XYZ(0, 0, top - r);
            XYZ cTL = center + wallDir * (left + r) + new XYZ(0, 0, top - r);

            // 底邊（左下角結束 → 右下角開始）
            XYZ bl_end = center + wallDir * (left + r) + new XYZ(0, 0, bottom);
            XYZ br_start = center + wallDir * (right - r) + new XYZ(0, 0, bottom);
            if (bl_end.DistanceTo(br_start) > 0.001)
                loop.Append(Line.CreateBound(bl_end, br_start));

            // 右下角圓弧
            XYZ br_end = center + wallDir * right + new XYZ(0, 0, bottom + r);
            XYZ br_mid = cBR + (wallDir * r + new XYZ(0, 0, -r)).Normalize() * r;
            Arc arcBR = Arc.Create(br_start, br_end, br_mid);
            loop.Append(arcBR);

            // 右邊
            XYZ tr_start = center + wallDir * right + new XYZ(0, 0, top - r);
            if (br_end.DistanceTo(tr_start) > 0.001)
                loop.Append(Line.CreateBound(br_end, tr_start));

            // 右上角圓弧
            XYZ tr_end = center + wallDir * (right - r) + new XYZ(0, 0, top);
            XYZ tr_mid = cTR + (wallDir * r + new XYZ(0, 0, r)).Normalize() * r;
            Arc arcTR = Arc.Create(tr_start, tr_end, tr_mid);
            loop.Append(arcTR);

            // 頂邊
            XYZ tl_start = center + wallDir * (left + r) + new XYZ(0, 0, top);
            if (tr_end.DistanceTo(tl_start) > 0.001)
                loop.Append(Line.CreateBound(tr_end, tl_start));

            // 左上角圓弧
            XYZ tl_end = center + wallDir * left + new XYZ(0, 0, top - r);
            XYZ tl_mid = cTL + (wallDir * (-r) + new XYZ(0, 0, r)).Normalize() * r;
            Arc arcTL = Arc.Create(tl_start, tl_end, tl_mid);
            loop.Append(arcTL);

            // 左邊
            XYZ bl_start = center + wallDir * left + new XYZ(0, 0, bottom + r);
            if (tl_end.DistanceTo(bl_start) > 0.001)
                loop.Append(Line.CreateBound(tl_end, bl_start));

            // 左下角圓弧
            XYZ bl_mid = cBL + (wallDir * (-r) + new XYZ(0, 0, -r)).Normalize() * r;
            Arc arcBL = Arc.Create(bl_start, bl_end, bl_mid);
            loop.Append(arcBL);

            return loop;
        }

        /// <summary>
        /// 為 DirectShape 套用材料覆寫
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
        /// 批次建立整面立面（根據 AI 分析結果）
        /// </summary>
        private object CreateFacadeFromAnalysis(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;
            UIDocument uidoc = _uiApp.ActiveUIDocument;

            // 解析參數
            IdType? wallId = parameters["wallId"]?.Value<IdType>();

            JObject facadeLayers = parameters["facadeLayers"] as JObject;
            if (facadeLayers == null)
                throw new Exception("請提供 facadeLayers 參數");

            JObject outerLayer = facadeLayers["outer"] as JObject;
            if (outerLayer == null)
                throw new Exception("請提供 facadeLayers.outer 參數");

            double globalOffset = outerLayer["offset"]?.Value<double>() ?? 200;
            double gap = outerLayer["gap"]?.Value<double>() ?? 20;
            double bandHeight = outerLayer["horizontalBandHeight"]?.Value<double>() ?? 0;
            double floorHeight = outerLayer["floorHeight"]?.Value<double>() ?? 3600;

            JArray panelTypesArray = outerLayer["panelTypes"] as JArray;
            JArray patternArray = outerLayer["pattern"] as JArray;

            if (panelTypesArray == null || panelTypesArray.Count == 0)
                throw new Exception("請提供至少一個 panelTypes");
            if (patternArray == null || patternArray.Count == 0)
                throw new Exception("請提供 pattern 排列矩陣");

            // 取得牆體
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
                throw new Exception("找不到牆體，請指定 wallId 或選取牆體");

            // 取得牆的位置和方向
            LocationCurve wallLoc = wall.Location as LocationCurve;
            if (wallLoc == null)
                throw new Exception("無法取得牆體位置線");

            Line wallLine = wallLoc.Curve as Line;
            if (wallLine == null)
                throw new Exception("目前僅支援直線牆");

            XYZ wallDir = wallLine.Direction.Normalize();
            // 使用 wall.Orientation 取得外牆面法線（永遠指向室外）
            XYZ wallNormal = wall.Orientation.Normalize();
            // 將起始點從牆中心線偏移到外牆面（半個牆厚度）
            double halfWallThickness = wall.Width / 2.0; // 已經是 feet
            XYZ wallStart = wallLine.GetEndPoint(0) + wallNormal * halfWallThickness;
            double wallLength = wallLine.Length * 304.8; // ft → mm

            // 取得牆的基準高程（Level 高程 + Base Offset）
            Level baseLevel = doc.GetElement(wall.LevelId) as Level;
            double wallBaseZ = baseLevel != null ? baseLevel.Elevation : 0; // feet
            Parameter baseOffsetParam = wall.get_Parameter(BuiltInParameter.WALL_BASE_OFFSET);
            double baseOffset = baseOffsetParam != null ? baseOffsetParam.AsDouble() : 0; // feet
            double wallBaseElevationMm = (wallBaseZ + baseOffset) * 304.8; // mm

            // 解析面板類型
            var typeDict = new Dictionary<string, JObject>();
            foreach (JObject ptObj in panelTypesArray)
            {
                string id = ptObj["id"]?.Value<string>();
                if (!string.IsNullOrEmpty(id))
                    typeDict[id] = ptObj;
            }

            // 開始建立
            int successCount = 0;
            int failCount = 0;
            var createdPanels = new List<object>();
            var failedPanels = new List<object>();

            using (Transaction trans = new Transaction(doc, "建立立面面板組"))
            {
                trans.Start();

                // 預先建立所有材料和 DirectShapeType
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

                    // 建立 DirectShapeType，命名規則: FP_{TypeId}_{名稱}
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

                // 取得實心填滿圖案
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

                // 遍歷每一層
                for (int floor = 0; floor < patternArray.Count; floor++)
                {
                    string rowPattern = patternArray[floor]?.Value<string>() ?? "";
                    if (string.IsNullOrEmpty(rowPattern)) continue;

                    double panelH = floorHeight - bandHeight; // 面板高度
                    double zBase = wallBaseElevationMm + floor * floorHeight; // 此層底部 Z (mm)，加上牆基準高程

                    // 計算此列所有面板的總寬度（用於對齊）
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

                    // 起始 X 位置（置中對齊）
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

                        // 各幾何類型專用參數
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

                            // 根據 geometryType 呼叫對應方法
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

                            // DirectShape — 命名規則: FP_{TypeId}_F{樓層}_C{欄位}
                            string dsName = $"FP_{typeId}_F{floor + 1}_C{col + 1}";
                            DirectShape ds = DirectShape.CreateElement(
                                doc,
                                new ElementId((IdType)(int)BuiltInCategory.OST_GenericModel)
                            );
                            ds.ApplicationId = "RevitMCP_FacadePanel";
                            ds.ApplicationDataId = dsName;
                            ds.SetShape(new GeometryObject[] { solid });

                            // 指定 DirectShapeType
                            if (dsTypeCache.ContainsKey(typeId))
                            {
                                ds.SetTypeId(dsTypeCache[typeId].Id);
                            }

                            // 材料覆寫
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

                // 建立水平分隔帶（如果有）
                if (bandHeight > 0)
                {
                    // 建立分隔帶 DirectShapeType
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
                        double bandThick = 50 / 304.8; // 分隔帶厚度 50mm

                        try
                        {
                            // 分隔帶為簡單矩形擠出
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
                            // 分隔帶建立失敗不影響主流程
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
                Message = $"成功建立 {successCount} 片立面面板，失敗 {failCount} 片"
            };
        }

        /// <summary>
        /// 建立或取得立面面板材料
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
