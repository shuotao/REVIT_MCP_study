using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
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
    /// 碰撞偵測核心邏輯
    /// 執行 MEP 管線中心線 vs CSA 結構體的 Curve-to-Solid 碰撞運算
    /// </summary>
    public class ClashDetector
    {
        private readonly UIApplication _uiApp;
        private readonly LinkedModelHelper _linkHelper;

        public ClashDetector(UIApplication uiApp, LinkedModelHelper linkHelper)
        {
            _uiApp = uiApp ?? throw new ArgumentNullException(nameof(uiApp));
            _linkHelper = linkHelper ?? throw new ArgumentNullException(nameof(linkHelper));
        }

        #region Public Methods

        /// <summary>
        /// 執行碰撞偵測
        /// </summary>
        public object DetectClashes(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            // 解析 MEP 來源
            JObject mepSource = parameters["mepSource"] as JObject;
            JObject csaSource = parameters["csaSource"] as JObject;
            JObject options = parameters["options"] as JObject;

            if (mepSource == null)
                throw new Exception("必須提供 mepSource 參數");

            // 選項
            bool useCoarseFilter = options?["useCoarseFilter"]?.Value<bool>() ?? true;
            double toleranceMm = options?["tolerance"]?.Value<double>() ?? 0;
            string levelFilter = options?["levelFilter"]?.Value<string>();
            int maxResults = options?["maxResults"]?.Value<int>() ?? 1000;
            double toleranceFt = toleranceMm / 304.8;

            // ==== Phase 1: 收集 MEP 管線 ====
            var mepElements = CollectMepElements(doc, mepSource);
            if (mepElements.Count == 0)
                return new { TotalClashes = 0, Message = "MEP 來源中沒有找到符合條件的管線元素", Clashes = new List<object>() };

            // ==== Phase 2: 收集 CSA 結構體 ====
            var csaElements = CollectCsaElements(doc, csaSource);
            if (csaElements.Count == 0)
                return new { TotalClashes = 0, Message = "CSA 來源中沒有找到符合條件的結構元素", Clashes = new List<object>() };

            // ==== Phase 3: 碰撞偵測 ====
            var clashes = new List<object>();
            int clashId = 0;

            // 統計用
            var bySystem = new Dictionary<string, int>();
            var byCSACategory = new Dictionary<string, int>();

            foreach (var mep in mepElements)
            {
                if (clashId >= maxResults) break;

                // 取得 MEP 中心線
                Curve mepCurve = GetElementCurve(mep.Element, mep.Transform);
                if (mepCurve == null) continue;

                // 取得 MEP 屬性
                string systemType = GetParamValue(mep.Element, mep.Doc, "System Type", "系統類型", "System Classification");
                string mepSize = GetParamValue(mep.Element, mep.Doc, "Size", "大小", "Outside Diameter", "外徑");
                double outerDiameter = GetNumericParamFeet(mep.Element, mep.Doc, "Outside Diameter", "外徑", "Size", "大小", "Width", "寬度", "Height", "高度");

                // MEP BoundingBox（粗篩用）
                BoundingBoxXYZ mepBBox = null;
                if (useCoarseFilter)
                {
                    mepBBox = GetTransformedBBox(mep.Element, mep.Transform);
                    if (mepBBox == null) continue;
                    // 擴大 BBox 以容納公差
                    if (toleranceFt > 0)
                    {
                        mepBBox.Min -= new XYZ(toleranceFt, toleranceFt, toleranceFt);
                        mepBBox.Max += new XYZ(toleranceFt, toleranceFt, toleranceFt);
                    }
                }

                foreach (var csa in csaElements)
                {
                    if (clashId >= maxResults) break;

                    // 粗篩: BoundingBox 交集測試
                    if (useCoarseFilter && mepBBox != null)
                    {
                        BoundingBoxXYZ csaBBox = GetTransformedBBox(csa.Element, csa.Transform);
                        if (csaBBox == null) continue;
                        if (!BBoxIntersects(mepBBox, csaBBox))
                            continue;
                    }

                    // 精確碰撞: Curve vs Solid
                    var solids = GetElementSolids(csa.Element, csa.Transform);
                    foreach (var solid in solids)
                    {
                        if (clashId >= maxResults) break;

                        try
                        {
                            // 使用 Solid.IntersectWithCurve 取得穿透線段
                            var solidCurveIntersection = solid.IntersectWithCurve(mepCurve, new SolidCurveIntersectionOptions());
                            if (solidCurveIntersection == null || solidCurveIntersection.SegmentCount == 0)
                                continue;

                            // 處理每一段穿透線段
                            for (int i = 0; i < solidCurveIntersection.SegmentCount; i++)
                            {
                                Curve segment = solidCurveIntersection.GetCurveSegment(i);
                                XYZ entryPoint = segment.GetEndPoint(0);
                                XYZ exitPoint = segment.GetEndPoint(1);
                                double penetrationLengthMm = segment.Length * 304.8;
                                XYZ direction = (exitPoint - entryPoint).Normalize();

                                // 計算截面積與體積
                                double crossSectionMm2 = 0;
                                double occupiedVolumeMm3 = 0;
                                if (outerDiameter > 0)
                                {
                                    double radiusMm = (outerDiameter * 304.8) / 2.0;
                                    crossSectionMm2 = Math.PI * radiusMm * radiusMm;
                                    occupiedVolumeMm3 = crossSectionMm2 * penetrationLengthMm;
                                }

                                // CSA 資訊
                                string csaCategoryName = csa.Element.Category?.Name ?? "Unknown";
                                string csaTypeName = csa.Doc.GetElement(csa.Element.GetTypeId())?.Name ?? "";
                                string csaThickness = GetParamValue(csa.Element, csa.Doc, "Width", "寬度", "Thickness", "厚度", "Depth", "深度");

                                clashId++;
                                clashes.Add(new
                                {
                                    ClashId = clashId,
                                    MepElement = new
                                    {
                                        Id = mep.Element.Id.GetIdValue(),
                                        SystemType = systemType ?? "N/A",
                                        Size = mepSize ?? "N/A",
                                        TypeName = mep.Doc.GetElement(mep.Element.GetTypeId())?.Name ?? "",
                                        Category = mep.Element.Category?.Name ?? ""
                                    },
                                    CsaElement = new
                                    {
                                        Id = csa.Element.Id.GetIdValue(),
                                        Category = csaCategoryName,
                                        TypeName = csaTypeName,
                                        Thickness = csaThickness ?? "N/A"
                                    },
                                    Intersection = new
                                    {
                                        EntryPoint = new
                                        {
                                            X = Math.Round(entryPoint.X * 304.8, 2),
                                            Y = Math.Round(entryPoint.Y * 304.8, 2),
                                            Z = Math.Round(entryPoint.Z * 304.8, 2)
                                        },
                                        ExitPoint = new
                                        {
                                            X = Math.Round(exitPoint.X * 304.8, 2),
                                            Y = Math.Round(exitPoint.Y * 304.8, 2),
                                            Z = Math.Round(exitPoint.Z * 304.8, 2)
                                        },
                                        PenetrationLength = Math.Round(penetrationLengthMm, 2),
                                        PipeDirection = new
                                        {
                                            X = Math.Round(direction.X, 6),
                                            Y = Math.Round(direction.Y, 6),
                                            Z = Math.Round(direction.Z, 6)
                                        },
                                        PipeCrossSection = Math.Round(crossSectionMm2, 2),
                                        OccupiedVolume = Math.Round(occupiedVolumeMm3, 2)
                                    }
                                });

                                // 統計
                                string sysKey = systemType ?? "未知系統";
                                if (!bySystem.ContainsKey(sysKey)) bySystem[sysKey] = 0;
                                bySystem[sysKey]++;

                                string csaKey = csaCategoryName;
                                if (!byCSACategory.ContainsKey(csaKey)) byCSACategory[csaKey] = 0;
                                byCSACategory[csaKey]++;
                            }
                        }
                        catch (Autodesk.Revit.Exceptions.InvalidOperationException)
                        {
                            // Solid.IntersectWithCurve 可能因幾何不合法而失敗，跳過
                            continue;
                        }
                    }
                }
            }

            stopwatch.Stop();

            return new
            {
                TotalClashes = clashes.Count,
                ExecutionTime = $"{stopwatch.Elapsed.TotalSeconds:F1}s",
                MepElementCount = mepElements.Count,
                CsaElementCount = csaElements.Count,
                Summary = new
                {
                    BySystem = bySystem.Select(kv => new { System = kv.Key, Count = kv.Value }).ToList(),
                    ByCSACategory = byCSACategory.Select(kv => new { Category = kv.Key, Count = kv.Value }).ToList()
                },
                Clashes = clashes
            };
        }

        /// <summary>
        /// 匯出碰撞報告
        /// </summary>
        public object ExportClashReport(JObject parameters)
        {
            JObject clashData = parameters["clashData"] as JObject;
            string format = parameters["format"]?.Value<string>() ?? "csv";
            string outputPath = parameters["outputPath"]?.Value<string>();
            string reportTitle = parameters["reportTitle"]?.Value<string>() ?? "MEP-CSA 碰撞偵測報告";

            if (clashData == null)
                throw new Exception("必須提供 clashData 參數（來自 detect_clashes 的回傳結果）");

            if (string.IsNullOrEmpty(outputPath))
            {
                string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                outputPath = Path.Combine(desktop, $"clash_report_{timestamp}");
            }

            JArray clashes = clashData["Clashes"] as JArray;
            if (clashes == null || clashes.Count == 0)
                return new { Success = false, Message = "碰撞資料為空，無需匯出" };

            var outputPaths = new List<string>();

            if (format == "csv" || format == "both")
            {
                string csvPath = outputPath.EndsWith(".csv") ? outputPath : outputPath + ".csv";
                ExportToCsv(clashes, csvPath, reportTitle);
                outputPaths.Add(csvPath);
            }

            if (format == "json" || format == "both")
            {
                string jsonPath = outputPath.EndsWith(".json") ? outputPath : outputPath + ".json";
                File.WriteAllText(jsonPath, JsonConvert.SerializeObject(new
                {
                    ReportTitle = reportTitle,
                    GeneratedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    TotalClashes = clashData["TotalClashes"],
                    Summary = clashData["Summary"],
                    Clashes = clashes
                }, Formatting.Indented), Encoding.UTF8);
                outputPaths.Add(jsonPath);
            }

            return new
            {
                Success = true,
                RowCount = clashes.Count,
                OutputPaths = outputPaths,
                Message = $"成功匯出 {clashes.Count} 筆碰撞資料至 {string.Join(", ", outputPaths)}"
            };
        }

        /// <summary>
        /// 碰撞結果視覺化上色
        /// </summary>
        public object ColorizeClashes(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;
            JObject clashData = parameters["clashData"] as JObject;
            string colorScheme = parameters["colorScheme"]?.Value<string>() ?? "by_csa_category";
            IdType? viewIdParam = parameters["viewId"]?.Value<IdType>();

            if (clashData == null)
                throw new Exception("必須提供 clashData 參數");

            View view;
            if (viewIdParam.HasValue)
            {
                view = doc.GetElement(new ElementId(viewIdParam.Value)) as View;
                if (view == null)
                    throw new Exception($"找不到視圖 ID: {viewIdParam}");
            }
            else
            {
                view = doc.ActiveView;
            }

            JArray clashes = clashData["Clashes"] as JArray;
            if (clashes == null || clashes.Count == 0)
                return new { Success = false, Message = "碰撞資料為空" };

            // 取得 Solid Fill Pattern
            ElementId solidPatternId = GetSolidFillPatternId(doc);

            int coloredCount = 0;
            var colorMap = new Dictionary<string, ColorEntry>();

            using (Transaction trans = new Transaction(doc, "碰撞結果上色"))
            {
                trans.Start();

                foreach (var clash in clashes)
                {
                    // 取得 CSA 元素 ID（主模型元素）
                    IdType csaId = clash["CsaElement"]?["Id"]?.Value<IdType>() ?? 0;
                    if (csaId == 0) continue;

                    Element csaElement = doc.GetElement(new ElementId(csaId));
                    if (csaElement == null) continue;

                    // 決定顏色
                    string colorKey;
                    Color color;

                    switch (colorScheme)
                    {
                        case "by_csa_category":
                            colorKey = clash["CsaElement"]?["Category"]?.Value<string>() ?? "Unknown";
                            color = GetCsaCategoryColor(colorKey);
                            break;
                        case "by_system":
                            colorKey = clash["MepElement"]?["SystemType"]?.Value<string>() ?? "Unknown";
                            color = GetSystemColor(colorKey);
                            break;
                        case "by_severity":
                            double penetration = clash["Intersection"]?["PenetrationLength"]?.Value<double>() ?? 0;
                            colorKey = GetSeverityLevel(penetration, clash["CsaElement"]?["Category"]?.Value<string>());
                            color = GetSeverityColor(colorKey);
                            break;
                        default:
                            colorKey = "default";
                            color = new Color(255, 80, 80);
                            break;
                    }

                    // 上色
                    var overrideSettings = new OverrideGraphicSettings();
                    overrideSettings.SetSurfaceForegroundPatternColor(color);
                    if (solidPatternId != ElementId.InvalidElementId)
                    {
                        overrideSettings.SetSurfaceForegroundPatternId(solidPatternId);
                        overrideSettings.SetSurfaceForegroundPatternVisible(true);
                    }
                    overrideSettings.SetCutForegroundPatternColor(color);
                    if (solidPatternId != ElementId.InvalidElementId)
                    {
                        overrideSettings.SetCutForegroundPatternId(solidPatternId);
                        overrideSettings.SetCutForegroundPatternVisible(true);
                    }
                    overrideSettings.SetProjectionLineColor(color);
                    overrideSettings.SetCutLineColor(color);

                    view.SetElementOverrides(new ElementId(csaId), overrideSettings);
                    coloredCount++;

                    // 記錄 colorMap
                    if (!colorMap.ContainsKey(colorKey))
                        colorMap[colorKey] = new ColorEntry { R = color.Red, G = color.Green, B = color.Blue, Count = 0 };
                    colorMap[colorKey].Count++;
                }

                trans.Commit();
            }

            return new
            {
                Success = true,
                ColoredCount = coloredCount,
                ColorScheme = colorScheme,
                ViewName = view.Name,
                ColorMap = colorMap.Select(kv => new
                {
                    Key = kv.Key,
                    R = kv.Value.R,
                    G = kv.Value.G,
                    B = kv.Value.B,
                    Count = kv.Value.Count
                }).ToList(),
                Message = $"成功對 {coloredCount} 個 CSA 元素上色 (配色: {colorScheme})"
            };
        }

        #endregion

        #region Collect Elements

        private class ElementWithContext
        {
            public Element Element { get; set; }
            public Document Doc { get; set; }
            public Transform Transform { get; set; }
        }

        /// <summary>
        /// 收集 MEP 元素（優先從連結模型）
        /// </summary>
        private List<ElementWithContext> CollectMepElements(Document doc, JObject mepSource)
        {
            IdType? linkInstanceId = mepSource["linkInstanceId"]?.Value<IdType>();
            string category = mepSource["category"]?.Value<string>();
            JArray categories = mepSource["categories"] as JArray;
            JArray filters = mepSource["filters"] as JArray;

            // 決定品類列表
            var categoryList = new List<string>();
            if (categories != null)
                categoryList = categories.Select(c => c.Value<string>()).Where(c => !string.IsNullOrEmpty(c)).ToList();
            else if (!string.IsNullOrEmpty(category))
                categoryList.Add(category);
            else
                categoryList.AddRange(new[] { "Pipes", "Ducts", "CableTrays" }); // 預設包含三大類

            var result = new List<ElementWithContext>();

            if (linkInstanceId.HasValue && linkInstanceId.Value != 0)
            {
                // 從連結模型取
                var (instance, linkDoc, transform) = _linkHelper.GetLinkData(linkInstanceId.Value);
                foreach (string catName in categoryList)
                {
                    BuiltInCategory bic = LinkedModelHelper.ResolveBuiltInCategory(catName);
                    var elements = new FilteredElementCollector(linkDoc)
                        .OfCategory(bic)
                        .WhereElementIsNotElementType()
                        .ToElements();

                    result.AddRange(elements.Select(e => new ElementWithContext
                    {
                        Element = e,
                        Doc = linkDoc,
                        Transform = transform
                    }));
                }

                // 套用過濾
                if (filters != null && filters.Count > 0)
                {
                    result = result.Where(ctx =>
                    {
                        foreach (var f in filters)
                        {
                            string field = f["field"]?.Value<string>();
                            string op = f["operator"]?.Value<string>();
                            string val = f["value"]?.Value<string>();
                            string actual = GetParamValue(ctx.Element, ctx.Doc, field);
                            if (!MatchFilterValue(actual, op, val)) return false;
                        }
                        return true;
                    }).ToList();
                }
            }
            else
            {
                // 從當前模型取
                foreach (string catName in categoryList)
                {
                    BuiltInCategory bic = LinkedModelHelper.ResolveBuiltInCategory(catName);
                    var elements = new FilteredElementCollector(doc)
                        .OfCategory(bic)
                        .WhereElementIsNotElementType()
                        .ToElements();

                    result.AddRange(elements.Select(e => new ElementWithContext
                    {
                        Element = e,
                        Doc = doc,
                        Transform = Transform.Identity
                    }));
                }
            }

            return result;
        }

        /// <summary>
        /// 收集 CSA 結構元素
        /// </summary>
        private List<ElementWithContext> CollectCsaElements(Document doc, JObject csaSource)
        {
            var result = new List<ElementWithContext>();

            // 預設 CSA 品類
            var defaultCategories = new[] { "Walls", "Floors", "StructuralFraming", "StructuralColumns" };

            if (csaSource == null)
            {
                // 未指定來源時，從當前模型查詢
                foreach (string catName in defaultCategories)
                {
                    BuiltInCategory bic = LinkedModelHelper.ResolveBuiltInCategory(catName);
                    var elements = new FilteredElementCollector(doc)
                        .OfCategory(bic)
                        .WhereElementIsNotElementType()
                        .ToElements();

                    result.AddRange(elements.Select(e => new ElementWithContext
                    {
                        Element = e,
                        Doc = doc,
                        Transform = Transform.Identity
                    }));
                }
                return result;
            }

            IdType? linkInstanceId = csaSource["linkInstanceId"]?.Value<IdType>();
            JArray categories = csaSource["categories"] as JArray;
            var categoryList = categories?.Select(c => c.Value<string>()).Where(c => !string.IsNullOrEmpty(c)).ToList()
                               ?? defaultCategories.ToList();

            if (linkInstanceId.HasValue && linkInstanceId.Value != 0)
            {
                var (instance, linkDoc, transform) = _linkHelper.GetLinkData(linkInstanceId.Value);
                foreach (string catName in categoryList)
                {
                    BuiltInCategory bic = LinkedModelHelper.ResolveBuiltInCategory(catName);
                    var elements = new FilteredElementCollector(linkDoc)
                        .OfCategory(bic)
                        .WhereElementIsNotElementType()
                        .ToElements();

                    result.AddRange(elements.Select(e => new ElementWithContext
                    {
                        Element = e,
                        Doc = linkDoc,
                        Transform = transform
                    }));
                }
            }
            else
            {
                foreach (string catName in categoryList)
                {
                    BuiltInCategory bic = LinkedModelHelper.ResolveBuiltInCategory(catName);
                    var elements = new FilteredElementCollector(doc)
                        .OfCategory(bic)
                        .WhereElementIsNotElementType()
                        .ToElements();

                    result.AddRange(elements.Select(e => new ElementWithContext
                    {
                        Element = e,
                        Doc = doc,
                        Transform = Transform.Identity
                    }));
                }
            }

            return result;
        }

        #endregion

        #region Geometry Helpers

        /// <summary>
        /// 取得元素中心線（經 Transform 後）
        /// </summary>
        private Curve GetElementCurve(Element element, Transform transform)
        {
            var location = element.Location;
            if (location is LocationCurve locCurve)
            {
                return locCurve.Curve.CreateTransformed(transform);
            }
            return null;
        }

        /// <summary>
        /// 取得元素的轉換後 BoundingBox
        /// </summary>
        private BoundingBoxXYZ GetTransformedBBox(Element element, Transform transform)
        {
            var bbox = element.get_BoundingBox(null);
            if (bbox == null) return null;

            // 轉換 8 個角點，取最大最小值
            var corners = new XYZ[]
            {
                new XYZ(bbox.Min.X, bbox.Min.Y, bbox.Min.Z),
                new XYZ(bbox.Max.X, bbox.Min.Y, bbox.Min.Z),
                new XYZ(bbox.Min.X, bbox.Max.Y, bbox.Min.Z),
                new XYZ(bbox.Max.X, bbox.Max.Y, bbox.Min.Z),
                new XYZ(bbox.Min.X, bbox.Min.Y, bbox.Max.Z),
                new XYZ(bbox.Max.X, bbox.Min.Y, bbox.Max.Z),
                new XYZ(bbox.Min.X, bbox.Max.Y, bbox.Max.Z),
                new XYZ(bbox.Max.X, bbox.Max.Y, bbox.Max.Z),
            };

            var transformed = corners.Select(c => transform.OfPoint(c)).ToArray();
            var result = new BoundingBoxXYZ
            {
                Min = new XYZ(
                    transformed.Min(p => p.X),
                    transformed.Min(p => p.Y),
                    transformed.Min(p => p.Z)),
                Max = new XYZ(
                    transformed.Max(p => p.X),
                    transformed.Max(p => p.Y),
                    transformed.Max(p => p.Z))
            };
            return result;
        }

        /// <summary>
        /// BoundingBox 相交測試
        /// </summary>
        private bool BBoxIntersects(BoundingBoxXYZ a, BoundingBoxXYZ b)
        {
            return a.Min.X <= b.Max.X && a.Max.X >= b.Min.X
                && a.Min.Y <= b.Max.Y && a.Max.Y >= b.Min.Y
                && a.Min.Z <= b.Max.Z && a.Max.Z >= b.Min.Z;
        }

        /// <summary>
        /// 取得元素的所有 Solid（經 Transform）
        /// </summary>
        private List<Solid> GetElementSolids(Element element, Transform transform)
        {
            var solids = new List<Solid>();
            try
            {
                var options = new Options { DetailLevel = ViewDetailLevel.Fine };
                var geoElement = element.get_Geometry(options);
                if (geoElement == null) return solids;

                foreach (var geoObj in geoElement)
                {
                    if (geoObj is Solid solid && solid.Volume > 0)
                    {
                        solids.Add(SolidUtils.CreateTransformed(solid, transform));
                    }
                    else if (geoObj is GeometryInstance gi)
                    {
                        foreach (var innerObj in gi.GetInstanceGeometry(transform))
                        {
                            if (innerObj is Solid innerSolid && innerSolid.Volume > 0)
                            {
                                solids.Add(innerSolid);
                            }
                        }
                    }
                }
            }
            catch { /* 幾何取得失敗時跳過 */ }
            return solids;
        }

        #endregion

        #region Parameter Helpers

        /// <summary>
        /// 多名稱嘗試取得參數值（中英文相容）
        /// </summary>
        private string GetParamValue(Element elem, Document doc, params string[] paramNames)
        {
            foreach (string name in paramNames)
            {
                if (string.IsNullOrEmpty(name)) continue;

                // 實例參數
                foreach (Parameter p in elem.Parameters)
                {
                    if (p.Definition.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    {
                        string val = p.AsValueString() ?? p.AsString();
                        if (!string.IsNullOrEmpty(val)) return val;
                    }
                }

                // 類型參數
                Element typeElem = doc.GetElement(elem.GetTypeId());
                if (typeElem != null)
                {
                    foreach (Parameter p in typeElem.Parameters)
                    {
                        if (p.Definition.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                        {
                            string val = p.AsValueString() ?? p.AsString();
                            if (!string.IsNullOrEmpty(val)) return val;
                        }
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// 取得數值參數（英尺）
        /// </summary>
        private double GetNumericParamFeet(Element elem, Document doc, params string[] paramNames)
        {
            foreach (string name in paramNames)
            {
                if (string.IsNullOrEmpty(name)) continue;

                foreach (Parameter p in elem.Parameters)
                {
                    if (p.Definition.Name.Equals(name, StringComparison.OrdinalIgnoreCase)
                        && p.StorageType == StorageType.Double
                        && p.HasValue)
                    {
                        double v = p.AsDouble();
                        if (v > 0) return v;
                    }
                }

                Element typeElem = doc.GetElement(elem.GetTypeId());
                if (typeElem != null)
                {
                    foreach (Parameter p in typeElem.Parameters)
                    {
                        if (p.Definition.Name.Equals(name, StringComparison.OrdinalIgnoreCase)
                            && p.StorageType == StorageType.Double
                            && p.HasValue)
                        {
                            double v = p.AsDouble();
                            if (v > 0) return v;
                        }
                    }
                }
            }
            return 0;
        }

        private bool MatchFilterValue(string actual, string op, string target)
        {
            if (string.IsNullOrEmpty(actual)) return false;
            switch (op?.ToLowerInvariant())
            {
                case "equals": return actual.Equals(target, StringComparison.OrdinalIgnoreCase);
                case "contains": return actual.IndexOf(target, StringComparison.OrdinalIgnoreCase) >= 0;
                case "not_equals": return !actual.Equals(target, StringComparison.OrdinalIgnoreCase);
                case "greater_than":
                case "less_than":
                    string cleanVal = System.Text.RegularExpressions.Regex.Replace(actual, @"[^\d.-]", "");
                    if (double.TryParse(cleanVal, out double v1) && double.TryParse(target, out double v2))
                        return op == "less_than" ? v1 < v2 : v1 > v2;
                    return false;
                default: return true;
            }
        }

        #endregion

        #region Color Helpers

        private class ColorEntry
        {
            public byte R { get; set; }
            public byte G { get; set; }
            public byte B { get; set; }
            public int Count { get; set; }
        }

        private Color GetCsaCategoryColor(string category)
        {
            string lower = category?.ToLowerInvariant() ?? "";
            if (lower.Contains("column") || lower.Contains("柱"))
                return new Color(255, 80, 80);     // 紅色 — 柱體穿越
            if (lower.Contains("framing") || lower.Contains("樑") || lower.Contains("beam"))
                return new Color(255, 165, 0);     // 橘色 — 樑穿越
            if (lower.Contains("floor") || lower.Contains("板") || lower.Contains("ceiling"))
                return new Color(255, 220, 0);     // 黃色 — 板穿越
            if (lower.Contains("wall") || lower.Contains("牆"))
                return new Color(70, 130, 255);    // 藍色 — 牆穿越
            return new Color(180, 180, 180);        // 灰色 — 其他
        }

        private Color GetSystemColor(string systemType)
        {
            string lower = systemType?.ToLowerInvariant() ?? "";
            if (lower.Contains("消防") || lower.Contains("fire") || lower.Contains("sprinkler"))
                return new Color(255, 80, 80);     // 紅色
            if (lower.Contains("冰水") || lower.Contains("chilled") || lower.Contains("冷") || lower.Contains("hydronic"))
                return new Color(70, 130, 255);    // 藍色
            if (lower.Contains("排水") || lower.Contains("drain") || lower.Contains("waste") || lower.Contains("sanitary"))
                return new Color(139, 90, 43);     // 棕色
            if (lower.Contains("給水") || lower.Contains("supply") || lower.Contains("domestic"))
                return new Color(0, 200, 150);     // 青綠色
            if (lower.Contains("空調") || lower.Contains("hvac") || lower.Contains("air") || lower.Contains("ventilation"))
                return new Color(255, 165, 0);     // 橘色
            if (lower.Contains("cable") || lower.Contains("電") || lower.Contains("power"))
                return new Color(255, 220, 0);     // 黃色
            return new Color(180, 180, 180);        // 灰色 — 其他
        }

        private string GetSeverityLevel(double penetrationMm, string csaCategory)
        {
            string lower = csaCategory?.ToLowerInvariant() ?? "";
            if (lower.Contains("column") || lower.Contains("柱"))
                return "critical";  // 柱穿越一律嚴重
            if (penetrationMm > 500)
                return "critical";
            if (penetrationMm > 200)
                return "warning";
            return "normal";
        }

        private Color GetSeverityColor(string severity)
        {
            switch (severity)
            {
                case "critical": return new Color(255, 80, 80);    // 紅
                case "warning": return new Color(255, 165, 0);     // 橘
                case "normal": return new Color(70, 130, 255);     // 藍
                default: return new Color(180, 180, 180);           // 灰
            }
        }

        private ElementId GetSolidFillPatternId(Document doc)
        {
            try
            {
                var fillPattern = new FilteredElementCollector(doc)
                    .OfClass(typeof(FillPatternElement))
                    .Cast<FillPatternElement>()
                    .FirstOrDefault(fp => fp.GetFillPattern().IsSolidFill);

                return fillPattern?.Id ?? ElementId.InvalidElementId;
            }
            catch
            {
                return ElementId.InvalidElementId;
            }
        }

        #endregion

        #region CSV Export

        private void ExportToCsv(JArray clashes, string csvPath, string reportTitle)
        {
            var sb = new StringBuilder();

            // BOM for Excel UTF-8
            sb.AppendLine($"# {reportTitle}");
            sb.AppendLine($"# 產生時間: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"# 總碰撞數: {clashes.Count}");
            sb.AppendLine();

            // 表頭
            sb.AppendLine(string.Join(",",
                "序號",
                "系統名稱",
                "管徑/尺寸",
                "管截面積(mm²)",
                "結構類別",
                "結構類型",
                "結構厚度",
                "貫穿長度(mm)",
                "佔用體積(mm³)",
                "管線向量X", "管線向量Y", "管線向量Z",
                "入口X", "入口Y", "入口Z",
                "出口X", "出口Y", "出口Z",
                "MEP_ElementId",
                "CSA_ElementId"
            ));

            foreach (var clash in clashes)
            {
                var mep = clash["MepElement"];
                var csa = clash["CsaElement"];
                var intersection = clash["Intersection"];
                var entry = intersection?["EntryPoint"];
                var exit = intersection?["ExitPoint"];
                var dir = intersection?["PipeDirection"];

                sb.AppendLine(string.Join(",",
                    CsvEscape(clash["ClashId"]?.ToString()),
                    CsvEscape(mep?["SystemType"]?.ToString()),
                    CsvEscape(mep?["Size"]?.ToString()),
                    CsvEscape(intersection?["PipeCrossSection"]?.ToString()),
                    CsvEscape(csa?["Category"]?.ToString()),
                    CsvEscape(csa?["TypeName"]?.ToString()),
                    CsvEscape(csa?["Thickness"]?.ToString()),
                    CsvEscape(intersection?["PenetrationLength"]?.ToString()),
                    CsvEscape(intersection?["OccupiedVolume"]?.ToString()),
                    dir?["X"], dir?["Y"], dir?["Z"],
                    entry?["X"], entry?["Y"], entry?["Z"],
                    exit?["X"], exit?["Y"], exit?["Z"],
                    mep?["Id"],
                    csa?["Id"]
                ));
            }

            File.WriteAllText(csvPath, sb.ToString(), new UTF8Encoding(true));
        }

        private string CsvEscape(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            if (value.Contains(",") || value.Contains("\"") || value.Contains("\n"))
                return "\"" + value.Replace("\"", "\"\"") + "\"";
            return value;
        }

        #endregion
    }
}
