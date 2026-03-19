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
            var typeMappingDict = new Dictionary<string, int>();
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
                        int targetTypeId = 0;
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
