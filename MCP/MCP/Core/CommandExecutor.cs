using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCP.Models;

namespace RevitMCP.Core
{
    /// <summary>
    /// 命令執行器 - 執行各種 Revit 操作
    /// </summary>
    public class CommandExecutor
    {
        private readonly UIApplication _uiApp;

        public CommandExecutor(UIApplication uiApp)
        {
            _uiApp = uiApp ?? throw new ArgumentNullException(nameof(uiApp));
        }

        /// <summary>
        /// 共用方法：查找樓層
        /// </summary>
        private Level FindLevel(Document doc, string levelName, bool useFirstIfNotFound = true)
        {
            var level = new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .FirstOrDefault(l => l.Name == levelName || l.Name.Contains(levelName) || levelName.Contains(l.Name));

            if (level == null && useFirstIfNotFound)
            {
                level = new FilteredElementCollector(doc)
                    .OfClass(typeof(Level))
                    .Cast<Level>()
                    .OrderBy(l => l.Elevation)
                    .FirstOrDefault();
            }

            if (level == null)
            {
                throw new Exception($"找不到樓層: {levelName}");
            }

            return level;
        }

        /// <summary>
        /// 執行命令
        /// </summary>
        public RevitCommandResponse ExecuteCommand(RevitCommandRequest request)
        {
            try
            {
                var parameters = request.Parameters as JObject ?? new JObject();
                object result = null;

                switch (request.CommandName.ToLower())
                {
                    case "create_wall":
                        result = CreateWall(parameters);
                        break;
                    
                    case "get_project_info":
                        result = GetProjectInfo();
                        break;

                    
                    case "create_floor":
                        result = CreateFloor(parameters);
                        break;
                    
                    case "get_all_levels":
                        result = GetAllLevels();
                        break;
                    
                    case "get_element_info":
                        result = GetElementInfo(parameters);
                        break;
                    
                    case "delete_element":
                        result = DeleteElement(parameters);
                        break;
                    
                    case "modify_element_parameter":
                        result = ModifyElementParameter(parameters);
                        break;
                    
                    case "create_door":
                        result = CreateDoor(parameters);
                        break;
                    
                    case "create_window":
                        result = CreateWindow(parameters);
                        break;
                    
                    case "get_all_grids":
                        result = GetAllGrids();
                        break;
                    
                    case "get_column_types":
                        result = GetColumnTypes(parameters);
                        break;
                    
                    case "create_column":
                        result = CreateColumn(parameters);
                        break;
                    
                    case "get_furniture_types":
                        result = GetFurnitureTypes(parameters);
                        break;
                    
                    case "place_furniture":
                        result = PlaceFurniture(parameters);
                        break;
                    
                    case "get_room_info":
                        result = GetRoomInfo(parameters);
                        break;
                    
                    case "get_rooms_by_level":
                        result = GetRoomsByLevel(parameters);
                        break;
                    
                    case "get_all_views":
                        result = GetAllViews(parameters);
                        break;
                    
                    case "get_active_view":
                        result = GetActiveView();
                        break;
                    
                    case "set_active_view":
                        result = SetActiveView(parameters);
                        break;
                    
                    case "select_element":
                        result = SelectElement(parameters);
                        break;
                    
                    case "zoom_to_element":
                        result = ZoomToElement(parameters);
                        break;
                    
                    case "measure_distance":
                        result = MeasureDistance(parameters);
                        break;
                    
                    case "get_wall_info":
                        result = GetWallInfo(parameters);
                        break;
                    
                    case "create_dimension":
                        result = CreateDimension(parameters);
                        break;
                    
                    case "query_walls_by_location":
                        result = QueryWallsByLocation(parameters);
                        break;
                    
                    case "query_elements":
                        result = QueryElements(parameters);
                        break;
                    
                    default:
                        throw new NotImplementedException($"未實作的命令: {request.CommandName}");
                }

                return new RevitCommandResponse
                {
                    Success = true,
                    Data = result,
                    RequestId = request.RequestId
                };
            }
            catch (Exception ex)
            {
                return new RevitCommandResponse
                {
                    Success = false,
                    Error = ex.Message,
                    RequestId = request.RequestId
                };
            }
        }

        #region 命令實作

        /// <summary>
        /// 建立牆
        /// </summary>
        private object CreateWall(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;

            double startX = parameters["startX"]?.Value<double>() ?? 0;
            double startY = parameters["startY"]?.Value<double>() ?? 0;
            double endX = parameters["endX"]?.Value<double>() ?? 0;
            double endY = parameters["endY"]?.Value<double>() ?? 0;
            double height = parameters["height"]?.Value<double>() ?? 3000;

            // 轉換為英尺 (Revit 內部單位)
            XYZ start = new XYZ(startX / 304.8, startY / 304.8, 0);
            XYZ end = new XYZ(endX / 304.8, endY / 304.8, 0);

            using (Transaction trans = new Transaction(doc, "建立牆"))
            {
                trans.Start();

                // 建立線
                Line line = Line.CreateBound(start, end);

                // 取得預設樓層
                Level level = new FilteredElementCollector(doc)
                    .OfClass(typeof(Level))
                    .Cast<Level>()
                    .FirstOrDefault();

                if (level == null)
                {
                    throw new Exception("找不到樓層");
                }

                // 建立牆
                Wall wall = Wall.Create(doc, line, level.Id, false);
                
                // 設定高度
                Parameter heightParam = wall.get_Parameter(BuiltInParameter.WALL_USER_HEIGHT_PARAM);
                if (heightParam != null && !heightParam.IsReadOnly)
                {
                    heightParam.Set(height / 304.8);
                }

                trans.Commit();

                return new
                {
                    ElementId = wall.Id.IntegerValue,
                    Message = $"成功建立牆，ID: {wall.Id.IntegerValue}"
                };
            }
        }

        /// <summary>
        /// 取得專案資訊
        /// </summary>
        private object GetProjectInfo()
        {
            Document doc = _uiApp.ActiveUIDocument.Document;
            ProjectInfo projInfo = doc.ProjectInformation;

            return new
            {
                ProjectName = doc.Title,
                BuildingName = projInfo.BuildingName,
                OrganizationName = projInfo.OrganizationName,
                Author = projInfo.Author,
                Address = projInfo.Address,
                ClientName = projInfo.ClientName,
                ProjectNumber = projInfo.Number,
                ProjectStatus = projInfo.Status
            };
        }

        /// <summary>
        /// 取得所有樓層
        /// </summary>
        private object GetAllLevels()
        {
            Document doc = _uiApp.ActiveUIDocument.Document;

            var levels = new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .OrderBy(l => l.Elevation)
                .Select(l => new
                {
                    ElementId = l.Id.IntegerValue,
                    Name = l.Name,
                    Elevation = Math.Round(l.Elevation * 304.8, 2) // 轉換為公釐
                })
                .ToList();

            return new
            {
                Count = levels.Count,
                Levels = levels
            };
        }

        /// <summary>
        /// 取得元素資訊
        /// </summary>
        private object GetElementInfo(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;
            int elementId = parameters["elementId"]?.Value<int>() ?? 0;

            Element element = doc.GetElement(new ElementId(elementId));
            if (element == null)
            {
                throw new Exception($"找不到元素 ID: {elementId}");
            }

            var parameterList = new List<object>();
            foreach (Parameter param in element.Parameters)
            {
                if (param.HasValue)
                {
                    parameterList.Add(new
                    {
                        Name = param.Definition.Name,
                        Value = param.AsValueString() ?? param.AsString(),
                        Type = param.StorageType.ToString()
                    });
                }
            }

            return new
            {
                ElementId = element.Id.IntegerValue,
                Name = element.Name,
                Category = element.Category?.Name,
                Type = doc.GetElement(element.GetTypeId())?.Name,
                Level = doc.GetElement(element.LevelId)?.Name,
                Parameters = parameterList
            };
        }

        /// <summary>
        /// 刪除元素
        /// </summary>
        private object DeleteElement(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;
            int elementId = parameters["elementId"]?.Value<int>() ?? 0;

            using (Transaction trans = new Transaction(doc, "刪除元素"))
            {
                trans.Start();

                Element element = doc.GetElement(new ElementId(elementId));
                if (element == null)
                {
                    throw new Exception($"找不到元素 ID: {elementId}");
                }

                doc.Delete(new ElementId(elementId));
                trans.Commit();

                return new
                {
                    Message = $"成功刪除元素 ID: {elementId}"
                };
            }
        }

        /// <summary>
        /// 建立樓板
        /// </summary>
        private object CreateFloor(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;
            
            var pointsArray = parameters["points"] as JArray;
            string levelName = parameters["levelName"]?.Value<string>() ?? "Level 1";
            
            if (pointsArray == null || pointsArray.Count < 3)
            {
                throw new Exception("需要至少 3 個點來建立樓板");
            }

            using (Transaction trans = new Transaction(doc, "建立樓板"))
            {
                trans.Start();

                // 取得樓層
                Level level = FindLevel(doc, levelName, true);

                // 建立邊界曲線
                var points = pointsArray.Select(p => new XYZ(
                    p["x"]?.Value<double>() / 304.8 ?? 0,
                    p["y"]?.Value<double>() / 304.8 ?? 0,
                    0
                )).ToList();

                // 取得預設樓板類型
                FloorType floorType = new FilteredElementCollector(doc)
                    .OfClass(typeof(FloorType))
                    .Cast<FloorType>()
                    .FirstOrDefault();

                if (floorType == null)
                {
                    throw new Exception("找不到樓板類型");
                }

                // 建立 CurveLoop (Revit 2022+ 使用)
                CurveLoop curveLoop = new CurveLoop();
                for (int i = 0; i < points.Count; i++)
                {
                    XYZ start = points[i];
                    XYZ end = points[(i + 1) % points.Count];
                    curveLoop.Append(Line.CreateBound(start, end));
                }

                // 使用 Floor.Create (適用於 Revit 2022+)
                Floor floor = Floor.Create(doc, new List<CurveLoop> { curveLoop }, floorType.Id, level.Id);

                trans.Commit();

                return new
                {
                    ElementId = floor.Id.IntegerValue,
                    Level = level.Name,
                    Message = $"成功建立樓板，ID: {floor.Id.IntegerValue}"
                };
            }
        }


        /// <summary>
        /// 修改元素參數
        /// </summary>
        private object ModifyElementParameter(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;
            int elementId = parameters["elementId"]?.Value<int>() ?? 0;
            string parameterName = parameters["parameterName"]?.Value<string>();
            string value = parameters["value"]?.Value<string>();

            if (string.IsNullOrEmpty(parameterName))
            {
                throw new Exception("請指定參數名稱");
            }

            Element element = doc.GetElement(new ElementId(elementId));
            if (element == null)
            {
                throw new Exception($"找不到元素 ID: {elementId}");
            }

            using (Transaction trans = new Transaction(doc, "修改參數"))
            {
                trans.Start();

                Parameter param = element.LookupParameter(parameterName);
                if (param == null)
                {
                    throw new Exception($"找不到參數: {parameterName}");
                }

                if (param.IsReadOnly)
                {
                    throw new Exception($"參數 {parameterName} 是唯讀的");
                }

                bool success = false;
                switch (param.StorageType)
                {
                    case StorageType.String:
                        success = param.Set(value);
                        break;
                    case StorageType.Double:
                        if (double.TryParse(value, out double dVal))
                            success = param.Set(dVal);
                        break;
                    case StorageType.Integer:
                        if (int.TryParse(value, out int iVal))
                            success = param.Set(iVal);
                        break;
                    default:
                        throw new Exception($"不支援的參數類型: {param.StorageType}");
                }

                if (!success)
                {
                    throw new Exception($"設定參數失敗");
                }

                trans.Commit();

                return new
                {
                    ElementId = elementId,
                    ParameterName = parameterName,
                    NewValue = value,
                    Message = $"成功修改參數 {parameterName}"
                };
            }
        }

        /// <summary>
        /// 建立門
        /// </summary>
        private object CreateDoor(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;
            int wallId = parameters["wallId"]?.Value<int>() ?? 0;
            double locationX = parameters["locationX"]?.Value<double>() ?? 0;
            double locationY = parameters["locationY"]?.Value<double>() ?? 0;

            Wall wall = doc.GetElement(new ElementId(wallId)) as Wall;
            if (wall == null)
            {
                throw new Exception($"找不到牆 ID: {wallId}");
            }

            using (Transaction trans = new Transaction(doc, "建立門"))
            {
                trans.Start();

                // 取得門類型
                FamilySymbol doorSymbol = new FilteredElementCollector(doc)
                    .OfClass(typeof(FamilySymbol))
                    .OfCategory(BuiltInCategory.OST_Doors)
                    .Cast<FamilySymbol>()
                    .FirstOrDefault();

                if (doorSymbol == null)
                {
                    throw new Exception("找不到門類型");
                }

                if (!doorSymbol.IsActive)
                {
                    doorSymbol.Activate();
                    doc.Regenerate();
                }

                // 取得牆的樓層
                Level level = doc.GetElement(wall.LevelId) as Level;
                XYZ location = new XYZ(locationX / 304.8, locationY / 304.8, level?.Elevation ?? 0);

                FamilyInstance door = doc.Create.NewFamilyInstance(
                    location, doorSymbol, wall, level, 
                    Autodesk.Revit.DB.Structure.StructuralType.NonStructural);

                trans.Commit();

                return new
                {
                    ElementId = door.Id.IntegerValue,
                    DoorType = doorSymbol.Name,
                    WallId = wallId,
                    Message = $"成功建立門，ID: {door.Id.IntegerValue}"
                };
            }
        }

        /// <summary>
        /// 建立窗
        /// </summary>
        private object CreateWindow(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;
            int wallId = parameters["wallId"]?.Value<int>() ?? 0;
            double locationX = parameters["locationX"]?.Value<double>() ?? 0;
            double locationY = parameters["locationY"]?.Value<double>() ?? 0;

            Wall wall = doc.GetElement(new ElementId(wallId)) as Wall;
            if (wall == null)
            {
                throw new Exception($"找不到牆 ID: {wallId}");
            }

            using (Transaction trans = new Transaction(doc, "建立窗"))
            {
                trans.Start();

                // 取得窗類型
                FamilySymbol windowSymbol = new FilteredElementCollector(doc)
                    .OfClass(typeof(FamilySymbol))
                    .OfCategory(BuiltInCategory.OST_Windows)
                    .Cast<FamilySymbol>()
                    .FirstOrDefault();

                if (windowSymbol == null)
                {
                    throw new Exception("找不到窗類型");
                }

                if (!windowSymbol.IsActive)
                {
                    windowSymbol.Activate();
                    doc.Regenerate();
                }

                // 取得牆的樓層
                Level level = doc.GetElement(wall.LevelId) as Level;
                XYZ location = new XYZ(locationX / 304.8, locationY / 304.8, (level?.Elevation ?? 0) + 3); // 窗戶高度 3 英尺

                FamilyInstance window = doc.Create.NewFamilyInstance(
                    location, windowSymbol, wall, level,
                    Autodesk.Revit.DB.Structure.StructuralType.NonStructural);

                trans.Commit();

                return new
                {
                    ElementId = window.Id.IntegerValue,
                    WindowType = windowSymbol.Name,
                    WallId = wallId,
                    Message = $"成功建立窗，ID: {window.Id.IntegerValue}"
                };
            }
        }

        /// <summary>
        /// 取得所有網格線
        /// </summary>
        private object GetAllGrids()
        {
            Document doc = _uiApp.ActiveUIDocument.Document;

            var grids = new FilteredElementCollector(doc)
                .OfClass(typeof(Grid))
                .Cast<Grid>()
                .Select(g =>
                {
                    // 取得 Grid 的曲線（通常是直線）
                    Curve curve = g.Curve;
                    XYZ startPoint = curve.GetEndPoint(0);
                    XYZ endPoint = curve.GetEndPoint(1);

                    // 判斷方向（水平或垂直）
                    double dx = Math.Abs(endPoint.X - startPoint.X);
                    double dy = Math.Abs(endPoint.Y - startPoint.Y);
                    string direction = dx > dy ? "水平" : "垂直";

                    return new
                    {
                        ElementId = g.Id.IntegerValue,
                        Name = g.Name,
                        Direction = direction,
                        StartX = Math.Round(startPoint.X * 304.8, 2),  // 英尺 → 公釐
                        StartY = Math.Round(startPoint.Y * 304.8, 2),
                        EndX = Math.Round(endPoint.X * 304.8, 2),
                        EndY = Math.Round(endPoint.Y * 304.8, 2)
                    };
                })
                .OrderBy(g => g.Name)
                .ToList();

            return new
            {
                Count = grids.Count,
                Grids = grids
            };
        }

        /// <summary>
        /// 取得柱類型
        /// </summary>
        private object GetColumnTypes(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;
            string materialFilter = parameters["material"]?.Value<string>();

            // 查詢結構柱和建築柱的 FamilySymbol
            var columnTypes = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .Where(fs => fs.Category != null && 
                    (fs.Category.Id.IntegerValue == (int)BuiltInCategory.OST_Columns ||
                     fs.Category.Id.IntegerValue == (int)BuiltInCategory.OST_StructuralColumns))
                .Select(fs =>
                {
                    // 嘗試取得尺寸參數
                    double width = 0, depth = 0;
                    
                    // 常見的柱尺寸參數名稱
                    Parameter widthParam = fs.LookupParameter("寬度") ?? 
                                          fs.LookupParameter("Width") ?? 
                                          fs.LookupParameter("b");
                    Parameter depthParam = fs.LookupParameter("深度") ?? 
                                          fs.LookupParameter("Depth") ?? 
                                          fs.LookupParameter("h");
                    
                    if (widthParam != null && widthParam.HasValue)
                        width = Math.Round(widthParam.AsDouble() * 304.8, 0);  // 轉公釐
                    if (depthParam != null && depthParam.HasValue)
                        depth = Math.Round(depthParam.AsDouble() * 304.8, 0);

                    return new
                    {
                        ElementId = fs.Id.IntegerValue,
                        TypeName = fs.Name,
                        FamilyName = fs.FamilyName,
                        Category = fs.Category?.Name,
                        Width = width,
                        Depth = depth,
                        SizeDescription = width > 0 && depth > 0 ? $"{width}x{depth}" : "未知尺寸"
                    };
                })
                .Where(ct => string.IsNullOrEmpty(materialFilter) || 
                             ct.FamilyName.Contains(materialFilter) || 
                             ct.TypeName.Contains(materialFilter))
                .OrderBy(ct => ct.FamilyName)
                .ThenBy(ct => ct.TypeName)
                .ToList();

            return new
            {
                Count = columnTypes.Count,
                ColumnTypes = columnTypes
            };
        }

        /// <summary>
        /// 建立柱子
        /// </summary>
        private object CreateColumn(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;

            // 解析參數
            double x = parameters["x"]?.Value<double>() ?? 0;
            double y = parameters["y"]?.Value<double>() ?? 0;
            string bottomLevelName = parameters["bottomLevel"]?.Value<string>() ?? "Level 1";
            string topLevelName = parameters["topLevel"]?.Value<string>();
            string columnTypeName = parameters["columnType"]?.Value<string>();

            // 轉換座標（公釐 → 英尺）
            XYZ location = new XYZ(x / 304.8, y / 304.8, 0);

            using (Transaction trans = new Transaction(doc, "建立柱子"))
            {
                trans.Start();

                // 取得底部樓層
                Level bottomLevel = FindLevel(doc, bottomLevelName, true);

                // 取得柱類型（FamilySymbol）
                FamilySymbol columnSymbol = new FilteredElementCollector(doc)
                    .OfClass(typeof(FamilySymbol))
                    .Cast<FamilySymbol>()
                    .Where(fs => fs.Category != null &&
                        (fs.Category.Id.IntegerValue == (int)BuiltInCategory.OST_Columns ||
                         fs.Category.Id.IntegerValue == (int)BuiltInCategory.OST_StructuralColumns))
                    .FirstOrDefault(fs => string.IsNullOrEmpty(columnTypeName) || 
                                          fs.Name == columnTypeName ||
                                          fs.FamilyName.Contains(columnTypeName));

                if (columnSymbol == null)
                {
                    throw new Exception(string.IsNullOrEmpty(columnTypeName) 
                        ? "專案中沒有可用的柱類型" 
                        : $"找不到柱類型: {columnTypeName}");
                }

                // 確保 FamilySymbol 已啟用
                if (!columnSymbol.IsActive)
                {
                    columnSymbol.Activate();
                    doc.Regenerate();
                }

                // 建立柱子
                FamilyInstance column = doc.Create.NewFamilyInstance(
                    location,
                    columnSymbol,
                    bottomLevel,
                    Autodesk.Revit.DB.Structure.StructuralType.Column
                );

                // 設定頂部樓層（如果有指定）
                if (!string.IsNullOrEmpty(topLevelName))
                {
                    Level topLevel = new FilteredElementCollector(doc)
                        .OfClass(typeof(Level))
                        .Cast<Level>()
                        .FirstOrDefault(l => l.Name == topLevelName);

                    if (topLevel != null)
                    {
                        Parameter topLevelParam = column.get_Parameter(BuiltInParameter.FAMILY_TOP_LEVEL_PARAM);
                        if (topLevelParam != null && !topLevelParam.IsReadOnly)
                        {
                            topLevelParam.Set(topLevel.Id);
                        }
                    }
                }

                trans.Commit();

                return new
                {
                    ElementId = column.Id.IntegerValue,
                    ColumnType = columnSymbol.Name,
                    FamilyName = columnSymbol.FamilyName,
                    Level = bottomLevel.Name,
                    LocationX = x,
                    LocationY = y,
                    Message = $"成功建立柱子，ID: {column.Id.IntegerValue}"
                };
            }
        }

        /// <summary>
        /// 取得家具類型
        /// </summary>
        private object GetFurnitureTypes(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;
            string categoryFilter = parameters["category"]?.Value<string>();

            var furnitureTypes = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .OfCategory(BuiltInCategory.OST_Furniture)
                .Cast<FamilySymbol>()
                .Select(fs => new
                {
                    ElementId = fs.Id.IntegerValue,
                    TypeName = fs.Name,
                    FamilyName = fs.FamilyName,
                    IsActive = fs.IsActive
                })
                .Where(ft => string.IsNullOrEmpty(categoryFilter) ||
                             ft.FamilyName.Contains(categoryFilter) ||
                             ft.TypeName.Contains(categoryFilter))
                .OrderBy(ft => ft.FamilyName)
                .ThenBy(ft => ft.TypeName)
                .ToList();

            return new
            {
                Count = furnitureTypes.Count,
                FurnitureTypes = furnitureTypes
            };
        }

        /// <summary>
        /// 放置家具
        /// </summary>
        private object PlaceFurniture(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;

            double x = parameters["x"]?.Value<double>() ?? 0;
            double y = parameters["y"]?.Value<double>() ?? 0;
            string furnitureTypeName = parameters["furnitureType"]?.Value<string>();
            string levelName = parameters["level"]?.Value<string>() ?? "Level 1";
            double rotation = parameters["rotation"]?.Value<double>() ?? 0;

            // 轉換座標（公釐 → 英尺）
            XYZ location = new XYZ(x / 304.8, y / 304.8, 0);

            using (Transaction trans = new Transaction(doc, "放置家具"))
            {
                trans.Start();

                // 取得樓層
                Level level = FindLevel(doc, levelName, true);

                // 取得家具類型
                FamilySymbol furnitureSymbol = new FilteredElementCollector(doc)
                    .OfClass(typeof(FamilySymbol))
                    .OfCategory(BuiltInCategory.OST_Furniture)
                    .Cast<FamilySymbol>()
                    .FirstOrDefault(fs => fs.Name == furnitureTypeName ||
                                          fs.FamilyName.Contains(furnitureTypeName));

                if (furnitureSymbol == null)
                {
                    throw new Exception($"找不到家具類型: {furnitureTypeName}");
                }

                // 確保 FamilySymbol 已啟用
                if (!furnitureSymbol.IsActive)
                {
                    furnitureSymbol.Activate();
                    doc.Regenerate();
                }

                // 放置家具
                FamilyInstance furniture = doc.Create.NewFamilyInstance(
                    location,
                    furnitureSymbol,
                    level,
                    Autodesk.Revit.DB.Structure.StructuralType.NonStructural
                );

                // 旋轉
                if (Math.Abs(rotation) > 0.001)
                {
                    Line axis = Line.CreateBound(location, location + XYZ.BasisZ);
                    ElementTransformUtils.RotateElement(doc, furniture.Id, axis, rotation * Math.PI / 180);
                }

                trans.Commit();

                return new
                {
                    ElementId = furniture.Id.IntegerValue,
                    FurnitureType = furnitureSymbol.Name,
                    FamilyName = furnitureSymbol.FamilyName,
                    Level = level.Name,
                    LocationX = x,
                    LocationY = y,
                    Rotation = rotation,
                    Message = $"成功放置家具，ID: {furniture.Id.IntegerValue}"
                };
            }
        }

        /// <summary>
        /// 取得房間資訊
        /// </summary>
        private object GetRoomInfo(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;
            int? roomId = parameters["roomId"]?.Value<int>();
            string roomName = parameters["roomName"]?.Value<string>();

            Room room = null;

            if (roomId.HasValue)
            {
                room = doc.GetElement(new ElementId(roomId.Value)) as Room;
            }
            else if (!string.IsNullOrEmpty(roomName))
            {
                room = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_Rooms)
                    .WhereElementIsNotElementType()
                    .Cast<Room>()
                    .FirstOrDefault(r => r.Name.Contains(roomName) || 
                                         r.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString()?.Contains(roomName) == true);
            }

            if (room == null)
            {
                throw new Exception(roomId.HasValue 
                    ? $"找不到房間 ID: {roomId}" 
                    : $"找不到房間名稱包含: {roomName}");
            }

            // 取得房間位置點
            LocationPoint locPoint = room.Location as LocationPoint;
            XYZ center = locPoint?.Point ?? XYZ.Zero;

            // 取得 BoundingBox
            BoundingBoxXYZ bbox = room.get_BoundingBox(null);
            
            // 取得面積
            double area = room.Area * 0.092903; // 平方英尺 → 平方公尺

            return new
            {
                ElementId = room.Id.IntegerValue,
                Name = room.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString(),
                Number = room.Number,
                Level = doc.GetElement(room.LevelId)?.Name,
                Area = Math.Round(area, 2),
                CenterX = Math.Round(center.X * 304.8, 2),
                CenterY = Math.Round(center.Y * 304.8, 2),
                BoundingBox = bbox != null ? new
                {
                    MinX = Math.Round(bbox.Min.X * 304.8, 2),
                    MinY = Math.Round(bbox.Min.Y * 304.8, 2),
                    MaxX = Math.Round(bbox.Max.X * 304.8, 2),
                    MaxY = Math.Round(bbox.Max.Y * 304.8, 2)
                } : null
            };
        }

        /// <summary>
        /// 取得樓層房間清單
        /// </summary>
        private object GetRoomsByLevel(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;
            string levelName = parameters["level"]?.Value<string>();
            bool includeUnnamed = parameters["includeUnnamed"]?.Value<bool>() ?? true;

            if (string.IsNullOrEmpty(levelName))
            {
                throw new Exception("請指定樓層名稱");
            }

            // 取得指定樓層
            Level targetLevel = FindLevel(doc, levelName, false);

            // 取得該樓層的所有房間
            var rooms = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType()
                .Cast<Room>()
                .Where(r => r.LevelId == targetLevel.Id)
                .Where(r => r.Area > 0) // 排除面積為 0 的房間（未封閉）
                .Select(r => 
                {
                    string roomName = r.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString();
                    bool hasName = !string.IsNullOrEmpty(roomName) && roomName != "房間";
                    
                    // 取得房間中心點
                    LocationPoint locPoint = r.Location as LocationPoint;
                    XYZ center = locPoint?.Point ?? XYZ.Zero;
                    
                    // 取得面積（平方英尺 → 平方公尺）
                    double areaM2 = r.Area * 0.092903;
                    
                    return new
                    {
                        ElementId = r.Id.IntegerValue,
                        Name = roomName ?? "未命名",
                        Number = r.Number,
                        Area = Math.Round(areaM2, 2),
                        HasName = hasName,
                        CenterX = Math.Round(center.X * 304.8, 2),
                        CenterY = Math.Round(center.Y * 304.8, 2)
                    };
                })
                .Where(r => includeUnnamed || r.HasName)
                .OrderBy(r => r.Number)
                .ToList();

            // 計算統計
            double totalArea = rooms.Sum(r => r.Area);
            int roomsWithName = rooms.Count(r => r.HasName);
            int roomsWithoutName = rooms.Count(r => !r.HasName);

            return new
            {
                Level = targetLevel.Name,
                LevelId = targetLevel.Id.IntegerValue,
                TotalRooms = rooms.Count,
                TotalArea = Math.Round(totalArea, 2),
                RoomsWithName = roomsWithName,
                RoomsWithoutName = roomsWithoutName,
                DataCompleteness = rooms.Count > 0 
                    ? $"{Math.Round((double)roomsWithName / rooms.Count * 100, 1)}%" 
                    : "N/A",
                Rooms = rooms
            };
        }

        /// <summary>
        /// 取得所有視圖
        /// </summary>
        private object GetAllViews(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;
            string viewTypeFilter = parameters["viewType"]?.Value<string>();
            string levelNameFilter = parameters["levelName"]?.Value<string>();

            var views = new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(v => !v.IsTemplate && v.CanBePrinted)
                .Select(v =>
                {
                    string levelName = "";
                    if (v.GenLevel != null)
                    {
                        levelName = v.GenLevel.Name;
                    }

                    return new
                    {
                        ElementId = v.Id.IntegerValue,
                        Name = v.Name,
                        ViewType = v.ViewType.ToString(),
                        LevelName = levelName,
                        Scale = v.Scale
                    };
                })
                .Where(v => string.IsNullOrEmpty(viewTypeFilter) || 
                            v.ViewType.ToLower().Contains(viewTypeFilter.ToLower()))
                .Where(v => string.IsNullOrEmpty(levelNameFilter) || 
                            v.LevelName.Contains(levelNameFilter))
                .OrderBy(v => v.ViewType)
                .ThenBy(v => v.Name)
                .ToList();

            return new
            {
                Count = views.Count,
                Views = views
            };
        }

        /// <summary>
        /// 取得目前視圖
        /// </summary>
        private object GetActiveView()
        {
            View activeView = _uiApp.ActiveUIDocument.ActiveView;
            Document doc = _uiApp.ActiveUIDocument.Document;

            string levelName = "";
            if (activeView.GenLevel != null)
            {
                levelName = activeView.GenLevel.Name;
            }

            return new
            {
                ElementId = activeView.Id.IntegerValue,
                Name = activeView.Name,
                ViewType = activeView.ViewType.ToString(),
                LevelName = levelName,
                Scale = activeView.Scale
            };
        }

        /// <summary>
        /// 切換視圖
        /// </summary>
        private object SetActiveView(JObject parameters)
        {
            int viewId = parameters["viewId"]?.Value<int>() ?? 0;
            Document doc = _uiApp.ActiveUIDocument.Document;

            View view = doc.GetElement(new ElementId(viewId)) as View;
            if (view == null)
            {
                throw new Exception($"找不到視圖 ID: {viewId}");
            }

            _uiApp.ActiveUIDocument.ActiveView = view;

            return new
            {
                Success = true,
                ViewId = viewId,
                ViewName = view.Name,
                Message = $"已切換至視圖: {view.Name}"
            };
        }

        /// <summary>
        /// 選取元素
        /// </summary>
        private object SelectElement(JObject parameters)
        {
            var elementIds = new List<ElementId>();
            
            // 支援單一 ID
            if (parameters.ContainsKey("elementId"))
            {
                int id = parameters["elementId"].Value<int>();
                if (id > 0) elementIds.Add(new ElementId(id));
            }

            // 支援多個 ID
            if (parameters.ContainsKey("elementIds"))
            {
                var ids = parameters["elementIds"].Values<int>();
                foreach (var id in ids)
                {
                    if (id > 0) elementIds.Add(new ElementId(id));
                }
            }

            if (elementIds.Count == 0)
            {
                throw new Exception("未提供有效的 elementId 或 elementIds");
            }

            Document doc = _uiApp.ActiveUIDocument.Document;
            
            // 選取元素
            _uiApp.ActiveUIDocument.Selection.SetElementIds(elementIds);

            return new
            {
                Success = true,
                Count = elementIds.Count,
                Message = $"已選取 {elementIds.Count} 個元素"
            };
        }

        /// <summary>
        /// 縮放至元素
        /// </summary>
        private object ZoomToElement(JObject parameters)
        {
            int elementId = parameters["elementId"]?.Value<int>() ?? 0;
            Document doc = _uiApp.ActiveUIDocument.Document;

            Element element = doc.GetElement(new ElementId(elementId));
            if (element == null)
            {
                throw new Exception($"找不到元素 ID: {elementId}");
            }

            // 顯示元素（會自動縮放）
            var elementIds = new List<ElementId> { new ElementId(elementId) };
            _uiApp.ActiveUIDocument.ShowElements(elementIds);

            return new
            {
                Success = true,
                ElementId = elementId,
                ElementName = element.Name,
                Message = $"已縮放至元素: {element.Name}"
            };
        }

        /// <summary>
        /// 測量距離
        /// </summary>
        private object MeasureDistance(JObject parameters)
        {
            double p1x = parameters["point1X"]?.Value<double>() ?? 0;
            double p1y = parameters["point1Y"]?.Value<double>() ?? 0;
            double p1z = parameters["point1Z"]?.Value<double>() ?? 0;
            double p2x = parameters["point2X"]?.Value<double>() ?? 0;
            double p2y = parameters["point2Y"]?.Value<double>() ?? 0;
            double p2z = parameters["point2Z"]?.Value<double>() ?? 0;

            // 轉換為英尺
            XYZ point1 = new XYZ(p1x / 304.8, p1y / 304.8, p1z / 304.8);
            XYZ point2 = new XYZ(p2x / 304.8, p2y / 304.8, p2z / 304.8);

            double distanceFeet = point1.DistanceTo(point2);
            double distanceMm = distanceFeet * 304.8;

            return new
            {
                Distance = Math.Round(distanceMm, 2),
                Unit = "mm",
                Point1 = new { X = p1x, Y = p1y, Z = p1z },
                Point2 = new { X = p2x, Y = p2y, Z = p2z }
            };
        }

        /// <summary>
        /// 取得牆資訊
        /// </summary>
        private object GetWallInfo(JObject parameters)
        {
            int wallId = parameters["wallId"]?.Value<int>() ?? 0;
            Document doc = _uiApp.ActiveUIDocument.Document;

            Wall wall = doc.GetElement(new ElementId(wallId)) as Wall;
            if (wall == null)
            {
                throw new Exception($"找不到牆 ID: {wallId}");
            }

            // 取得牆的位置曲線
            LocationCurve locCurve = wall.Location as LocationCurve;
            Curve curve = locCurve?.Curve;

            XYZ startPoint = curve?.GetEndPoint(0) ?? XYZ.Zero;
            XYZ endPoint = curve?.GetEndPoint(1) ?? XYZ.Zero;

            // 取得牆厚度
            double thickness = wall.Width * 304.8; // 英尺 → 公釐

            // 取得牆長度
            double length = curve != null ? curve.Length * 304.8 : 0;

            // 取得牆高度
            Parameter heightParam = wall.get_Parameter(BuiltInParameter.WALL_USER_HEIGHT_PARAM);
            double height = heightParam != null ? heightParam.AsDouble() * 304.8 : 0;

            return new
            {
                ElementId = wallId,
                Name = wall.Name,
                WallType = wall.WallType.Name,
                Thickness = Math.Round(thickness, 2),
                Length = Math.Round(length, 2),
                Height = Math.Round(height, 2),
                StartX = Math.Round(startPoint.X * 304.8, 2),
                StartY = Math.Round(startPoint.Y * 304.8, 2),
                EndX = Math.Round(endPoint.X * 304.8, 2),
                EndY = Math.Round(endPoint.Y * 304.8, 2),
                Level = doc.GetElement(wall.LevelId)?.Name
            };
        }

        /// <summary>
        /// 建立尺寸標註
        /// </summary>
        private object CreateDimension(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;
            
            int viewId = parameters["viewId"]?.Value<int>() ?? 0;
            double startX = parameters["startX"]?.Value<double>() ?? 0;
            double startY = parameters["startY"]?.Value<double>() ?? 0;
            double endX = parameters["endX"]?.Value<double>() ?? 0;
            double endY = parameters["endY"]?.Value<double>() ?? 0;
            double offset = parameters["offset"]?.Value<double>() ?? 500;

            View view = doc.GetElement(new ElementId(viewId)) as View;
            if (view == null)
            {
                throw new Exception($"找不到視圖 ID: {viewId}");
            }

            using (Transaction trans = new Transaction(doc, "建立尺寸標註"))
            {
                trans.Start();

                // 轉換座標
                XYZ start = new XYZ(startX / 304.8, startY / 304.8, 0);
                XYZ end = new XYZ(endX / 304.8, endY / 304.8, 0);

                // 建立參考線
                Line line = Line.CreateBound(start, end);

                // 建立尺寸標註用的參考陣列
                ReferenceArray refArray = new ReferenceArray();

                // 使用 DetailCurve 作為參考
                // 先建立兩個詳圖線作為參考點
                XYZ perpDir = new XYZ(-(end.Y - start.Y), end.X - start.X, 0).Normalize();
                double offsetFeet = offset / 304.8;

                // 偏移後的標註線位置
                XYZ dimLinePoint = start.Add(perpDir.Multiply(offsetFeet));
                Line dimLine = Line.CreateBound(
                    start.Add(perpDir.Multiply(offsetFeet)),
                    end.Add(perpDir.Multiply(offsetFeet))
                );

                // 使用 NewDetailCurve 建立參考（建立足夠長的線段）
                // 詳圖線應垂直於標註方向，作為標註的參考點
                double lineLength = 1.0; // 1 英尺 = 約 305mm

                // 使用 perpDir（垂直方向）來建立詳圖線
                DetailCurve dc1 = doc.Create.NewDetailCurve(view, Line.CreateBound(
                    start.Subtract(perpDir.Multiply(lineLength / 2)), 
                    start.Add(perpDir.Multiply(lineLength / 2))));
                DetailCurve dc2 = doc.Create.NewDetailCurve(view, Line.CreateBound(
                    end.Subtract(perpDir.Multiply(lineLength / 2)), 
                    end.Add(perpDir.Multiply(lineLength / 2))));

                refArray.Append(dc1.GeometryCurve.Reference);
                refArray.Append(dc2.GeometryCurve.Reference);

                // 建立尺寸標註
                Dimension dim = doc.Create.NewDimension(view, dimLine, refArray);

                // 注意：保留詳圖線作為標註參考點（如需刪除請手動處理）

                trans.Commit();

                double dimValue = dim.Value.HasValue ? dim.Value.Value * 304.8 : 0;

                return new
                {
                    DimensionId = dim.Id.IntegerValue,
                    Value = Math.Round(dimValue, 2),
                    Unit = "mm",
                    ViewId = viewId,
                    ViewName = view.Name,
                    Message = $"成功建立尺寸標註: {Math.Round(dimValue, 0)} mm"
                };
            }
        }

        /// <summary>
        /// 查詢指定位置附近的牆體
        /// </summary>
        private object QueryWallsByLocation(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;
            
            double centerX = parameters["x"]?.Value<double>() ?? 0;
            double centerY = parameters["y"]?.Value<double>() ?? 0;
            double searchRadius = parameters["searchRadius"]?.Value<double>() ?? 5000;
            string levelName = parameters["level"]?.Value<string>();

            // 轉換為英尺
            XYZ center = new XYZ(centerX / 304.8, centerY / 304.8, 0);
            double radiusFeet = searchRadius / 304.8;

            // 取得所有牆
            var wallCollector = new FilteredElementCollector(doc)
                .OfClass(typeof(Wall))
                .WhereElementIsNotElementType()
                .Cast<Wall>();

            // 如果指定樓層，過濾樓層
            if (!string.IsNullOrEmpty(levelName))
            {
                var level = new FilteredElementCollector(doc)
                    .OfClass(typeof(Level))
                    .Cast<Level>()
                    .FirstOrDefault(l => l.Name.Contains(levelName));

                if (level != null)
                {
                    wallCollector = wallCollector.Where(w => w.LevelId == level.Id);
                }
            }

            var nearbyWalls = new List<object>();

            foreach (var wall in wallCollector)
            {
                LocationCurve locCurve = wall.Location as LocationCurve;
                if (locCurve == null) continue;

                Curve curve = locCurve.Curve;
                XYZ startPoint = curve.GetEndPoint(0);
                XYZ endPoint = curve.GetEndPoint(1);
                
                // 計算點到線段的最近距離
                XYZ wallDir = (endPoint - startPoint).Normalize();
                XYZ toCenter = center - startPoint;
                double proj = toCenter.DotProduct(wallDir);
                double wallLength = curve.Length;
                
                XYZ closestPoint;
                if (proj < 0)
                    closestPoint = startPoint;
                else if (proj > wallLength)
                    closestPoint = endPoint;
                else
                    closestPoint = startPoint + wallDir * proj;
                
                double distToWall = center.DistanceTo(closestPoint) * 304.8;

                if (distToWall <= searchRadius)
                {
                    // 取得牆厚度
                    double thickness = wall.Width * 304.8;
                    
                    // 計算牆的方向向量（垂直於位置線）
                    XYZ perpendicular = new XYZ(-wallDir.Y, wallDir.X, 0);
                    double halfThickness = wall.Width / 2;
                    
                    // 牆的兩個面
                    XYZ face1Point = closestPoint + perpendicular * halfThickness;
                    XYZ face2Point = closestPoint - perpendicular * halfThickness;

                    nearbyWalls.Add(new
                    {
                        ElementId = wall.Id.IntegerValue,
                        Name = wall.Name,
                        WallType = wall.WallType.Name,
                        Thickness = Math.Round(thickness, 2),
                        Length = Math.Round(curve.Length * 304.8, 2),
                        DistanceToCenter = Math.Round(distToWall, 2),
                        // 位置線座標
                        LocationLine = new
                        {
                            StartX = Math.Round(startPoint.X * 304.8, 2),
                            StartY = Math.Round(startPoint.Y * 304.8, 2),
                            EndX = Math.Round(endPoint.X * 304.8, 2),
                            EndY = Math.Round(endPoint.Y * 304.8, 2)
                        },
                        // 最近點位置
                        ClosestPoint = new
                        {
                            X = Math.Round(closestPoint.X * 304.8, 2),
                            Y = Math.Round(closestPoint.Y * 304.8, 2)
                        },
                        // 兩側面座標（在最近點處）
                        Face1 = new
                        {
                            X = Math.Round(face1Point.X * 304.8, 2),
                            Y = Math.Round(face1Point.Y * 304.8, 2)
                        },
                        Face2 = new
                        {
                            X = Math.Round(face2Point.X * 304.8, 2),
                            Y = Math.Round(face2Point.Y * 304.8, 2)
                        },
                        // 判斷牆是水平還是垂直
                        Orientation = Math.Abs(wallDir.X) > Math.Abs(wallDir.Y) ? "Horizontal" : "Vertical"
                    });
                }
            }

            // 直接返回列表（已在搜尋時過濾距離）

            return new
            {
                Count = nearbyWalls.Count,
                SearchCenter = new { X = centerX, Y = centerY },
                SearchRadius = searchRadius,
                Walls = nearbyWalls
            };
        }


        /// <summary>
        /// 查詢視圖中的元素
        /// </summary>
        private object QueryElements(JObject parameters)
        {
            try
            {
                string categoryName = parameters["category"]?.Value<string>();
                int? viewId = parameters["viewId"]?.Value<int>();
                int maxCount = parameters["maxCount"]?.Value<int>() ?? 100;
                
                Document doc = _uiApp.ActiveUIDocument.Document;
                
                if (string.IsNullOrEmpty(categoryName))
                {
                    throw new Exception("必須提供 category 參數");
                }
                
                // 決定查詢範圍: 指定視圖 或 目前視圖
                ElementId targetViewId = viewId.HasValue ? new ElementId(viewId.Value) : doc.ActiveView.Id;
                
                FilteredElementCollector collector = new FilteredElementCollector(doc, targetViewId);
                
                // 嘗試解析 BuiltInCategory
                BuiltInCategory category = BuiltInCategory.INVALID;
                bool isBuiltIn = Enum.TryParse("OST_" + categoryName, true, out category) || 
                                 Enum.TryParse(categoryName, true, out category);
                
                List<Element> elements = new List<Element>();
                
                if (isBuiltIn && category != BuiltInCategory.INVALID)
                {
                    elements = collector.OfCategory(category).ToElements().ToList();
                }
                else
                {
                    // 嘗試用 Class 查詢
                    if (categoryName.Equals("Dimensions", StringComparison.OrdinalIgnoreCase))
                    {
                        elements = collector.OfClass(typeof(Dimension)).ToElements().ToList();
                    }
                    else if (categoryName.Equals("Walls", StringComparison.OrdinalIgnoreCase))
                    {
                        elements = collector.OfClass(typeof(Wall)).ToElements().ToList();
                    }
                    else if (categoryName.Equals("Rooms", StringComparison.OrdinalIgnoreCase))
                    {
                        elements = collector.OfCategory(BuiltInCategory.OST_Rooms).ToElements().ToList();
                    }
                    else
                    {
                        throw new Exception($"不支援的類別: {categoryName}");
                    }
                }
                
                // 提取基本資訊
                var resultList = elements.Take(maxCount).Select(elem =>
                {
                    var item = new Dictionary<string, object>
                    {
                        { "ElementId", elem.Id.IntegerValue },
                        { "Name", elem.Name ?? "" },
                        { "Category", elem.Category?.Name ?? "" }
                    };
                    
                    // 特殊處理 Dimension
                    if (elem is Dimension dim)
                    {
                        if (dim.Value.HasValue)
                            item.Add("Value", Math.Round(dim.Value.Value * 304.8, 2)); // 轉 mm
                        if (dim.DimensionType != null)
                            item.Add("DimensionType", dim.DimensionType.Name);
                    }
                    
                    return item;
                }).ToList();
                
                return new
                {
                    Success = true,
                    Count = resultList.Count,
                    TotalFound = elements.Count,
                    ViewId = targetViewId.IntegerValue,
                    Category = categoryName,
                    Elements = resultList
                };
            }
            catch (Exception ex)
            {
                 throw new Exception($"QueryElements 錯誤: {ex.Message}");
            }
        }

        #endregion
    }
}



