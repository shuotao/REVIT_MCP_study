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
    /// 連結模型操作輔助類別
    /// 負責讀取連結模型資訊、查詢連結模型元素、抽取元素幾何
    /// </summary>
    public class LinkedModelHelper
    {
        private readonly UIApplication _uiApp;

        public LinkedModelHelper(UIApplication uiApp)
        {
            _uiApp = uiApp ?? throw new ArgumentNullException(nameof(uiApp));
        }

        #region Public Methods

        /// <summary>
        /// 取得所有連結模型清單
        /// </summary>
        public object GetLinkedModels()
        {
            Document doc = _uiApp.ActiveUIDocument.Document;

            var linkInstances = new FilteredElementCollector(doc)
                .OfClass(typeof(RevitLinkInstance))
                .Cast<RevitLinkInstance>()
                .ToList();

            var results = new List<object>();
            foreach (var link in linkInstances)
            {
                var linkDoc = link.GetLinkDocument();
                var transform = link.GetTotalTransform();

                // 嘗試取得連結類型名稱
                var linkType = doc.GetElement(link.GetTypeId());
                string linkTypeName = linkType?.Name ?? "Unknown";

                results.Add(new
                {
                    LinkInstanceId = link.Id.GetIdValue(),
                    LinkTypeName = linkTypeName,
                    FileName = linkDoc?.Title ?? "(未載入)",
                    FilePath = linkDoc?.PathName ?? "(無法取得路徑)",
                    IsLoaded = linkDoc != null,
                    Transform = new
                    {
                        OriginX = Math.Round(transform.Origin.X * 304.8, 2),
                        OriginY = Math.Round(transform.Origin.Y * 304.8, 2),
                        OriginZ = Math.Round(transform.Origin.Z * 304.8, 2),
                        IsIdentity = transform.IsIdentity
                    }
                });
            }

            return new
            {
                Count = results.Count,
                LinkedModels = results
            };
        }

        /// <summary>
        /// 查詢連結模型中的元素
        /// </summary>
        public object QueryLinkedElements(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;
            IdType linkInstanceId = parameters["linkInstanceId"]?.Value<IdType>() ?? 0;
            string categoryName = parameters["category"]?.Value<string>();
            JArray filters = parameters["filters"] as JArray;
            JArray returnFields = parameters["returnFields"] as JArray;
            int maxCount = parameters["maxCount"]?.Value<int>() ?? 500;

            if (string.IsNullOrEmpty(categoryName))
                throw new Exception("必須提供 category 參數");

            // 取得連結實體
            var linkInstance = doc.GetElement(new ElementId(linkInstanceId)) as RevitLinkInstance;
            if (linkInstance == null)
                throw new Exception($"找不到連結模型實體 ID: {linkInstanceId}");

            var linkDoc = linkInstance.GetLinkDocument();
            if (linkDoc == null)
                throw new Exception("連結模型未載入或無法讀取，請確認已在 Revit 中載入此連結");

            var transform = linkInstance.GetTotalTransform();

            // 解析品類
            BuiltInCategory bic = ResolveBuiltInCategory(categoryName);

            // 收集元素
            var collector = new FilteredElementCollector(linkDoc)
                .OfCategory(bic)
                .WhereElementIsNotElementType();

            var elements = collector.ToElements().ToList();

            // 套用參數過濾
            if (filters != null && filters.Count > 0)
            {
                elements = ApplyParameterFilters(elements, filters, linkDoc);
            }

            // 擷取返回欄位
            var returnFieldList = returnFields?.Select(f => f.Value<string>()).ToList();

            var results = elements
                .Take(maxCount)
                .Select(e => BuildLinkedElementData(e, linkDoc, transform, returnFieldList))
                .ToList();

            return new
            {
                TotalCount = elements.Count,
                ReturnedCount = results.Count,
                LinkFileName = linkDoc.Title,
                LinkInstanceId = linkInstanceId,
                Elements = results
            };
        }

        /// <summary>
        /// 取得元素幾何資訊（支援連結模型元素）
        /// </summary>
        public object GetElementGeometry(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;
            IdType elementId = parameters["elementId"]?.Value<IdType>() ?? 0;
            IdType? linkInstanceId = parameters["linkInstanceId"]?.Value<IdType>();
            string geometryType = parameters["geometryType"]?.Value<string>() ?? "centerline";
            bool applyTransform = parameters["applyTransform"]?.Value<bool>() ?? true;

            Document targetDoc;
            Transform linkTransform = Transform.Identity;

            if (linkInstanceId.HasValue && linkInstanceId.Value != 0)
            {
                var linkInstance = doc.GetElement(new ElementId(linkInstanceId.Value)) as RevitLinkInstance;
                if (linkInstance == null)
                    throw new Exception($"找不到連結模型實體 ID: {linkInstanceId}");

                targetDoc = linkInstance.GetLinkDocument();
                if (targetDoc == null)
                    throw new Exception("連結模型未載入");

                if (applyTransform)
                    linkTransform = linkInstance.GetTotalTransform();
            }
            else
            {
                targetDoc = doc;
            }

            Element element = targetDoc.GetElement(new ElementId(elementId));
            if (element == null)
                throw new Exception($"找不到元素 ID: {elementId}");

            var result = new Dictionary<string, object>
            {
                { "ElementId", elementId },
                { "Category", element.Category?.Name ?? "Unknown" },
                { "TypeName", targetDoc.GetElement(element.GetTypeId())?.Name ?? "" }
            };

            switch (geometryType.ToLowerInvariant())
            {
                case "centerline":
                    result["Centerline"] = ExtractCenterline(element, linkTransform);
                    break;
                case "boundingbox":
                    result["BoundingBox"] = ExtractBoundingBox(element, linkTransform);
                    break;
                case "solid":
                    result["Solid"] = ExtractSolidInfo(element, linkTransform);
                    break;
                case "all":
                    result["Centerline"] = ExtractCenterline(element, linkTransform);
                    result["BoundingBox"] = ExtractBoundingBox(element, linkTransform);
                    result["Solid"] = ExtractSolidInfo(element, linkTransform);
                    break;
                default:
                    throw new Exception($"不支援的幾何類型: {geometryType}，可使用: centerline, boundingbox, solid, all");
            }

            return result;
        }

        #endregion

        #region Internal Helpers (供 ClashDetector 使用)

        /// <summary>
        /// 取得連結模型實體與文件
        /// </summary>
        internal (RevitLinkInstance instance, Document linkDoc, Transform transform) GetLinkData(IdType linkInstanceId)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;
            var linkInstance = doc.GetElement(new ElementId(linkInstanceId)) as RevitLinkInstance;
            if (linkInstance == null)
                throw new Exception($"找不到連結模型實體 ID: {linkInstanceId}");

            var linkDoc = linkInstance.GetLinkDocument();
            if (linkDoc == null)
                throw new Exception("連結模型未載入");

            return (linkInstance, linkDoc, linkInstance.GetTotalTransform());
        }

        /// <summary>
        /// 在指定文件中收集指定品類的元素
        /// </summary>
        internal List<Element> CollectElements(Document targetDoc, string[] categoryNames, JArray filters = null)
        {
            var allElements = new List<Element>();
            foreach (string catName in categoryNames)
            {
                BuiltInCategory bic = ResolveBuiltInCategory(catName);
                var elements = new FilteredElementCollector(targetDoc)
                    .OfCategory(bic)
                    .WhereElementIsNotElementType()
                    .ToElements()
                    .ToList();
                allElements.AddRange(elements);
            }

            if (filters != null && filters.Count > 0)
            {
                allElements = ApplyParameterFilters(allElements, filters, allElements.FirstOrDefault()?.Document);
            }

            return allElements;
        }

        /// <summary>
        /// 解析品類名稱為 BuiltInCategory
        /// </summary>
        internal static BuiltInCategory ResolveBuiltInCategory(string categoryName)
        {
            if (string.IsNullOrEmpty(categoryName))
                throw new Exception("品類名稱不可為空");

            string lower = categoryName.ToLowerInvariant().Trim();

            // MEP 品類
            if (lower == "pipes" || lower == "管" || lower == "管線")
                return BuiltInCategory.OST_PipeCurves;
            if (lower == "ducts" || lower == "風管")
                return BuiltInCategory.OST_DuctCurves;
            if (lower == "cabletrays" || lower == "cable trays" || lower == "電纜架" || lower == "電纜線架")
                return BuiltInCategory.OST_CableTray;
            if (lower == "conduits" || lower == "導管" || lower == "電管")
                return BuiltInCategory.OST_Conduit;
            if (lower == "pipefittings" || lower == "pipe fittings" || lower == "管件")
                return BuiltInCategory.OST_PipeFitting;
            if (lower == "ductfittings" || lower == "duct fittings" || lower == "風管管件")
                return BuiltInCategory.OST_DuctFitting;
            if (lower == "pipeaccessories" || lower == "pipe accessories" || lower == "管配件")
                return BuiltInCategory.OST_PipeAccessory;

            // CSA 品類
            if (lower == "walls" || lower == "牆" || lower == "牆壁")
                return BuiltInCategory.OST_Walls;
            if (lower == "floors" || lower == "樓板" || lower == "板")
                return BuiltInCategory.OST_Floors;
            if (lower == "structuralframing" || lower == "structural framing" || lower == "結構構架" || lower == "樑")
                return BuiltInCategory.OST_StructuralFraming;
            if (lower == "structuralcolumns" || lower == "structural columns" || lower == "結構柱" || lower == "柱")
                return BuiltInCategory.OST_StructuralColumns;
            if (lower == "columns" || lower == "建築柱")
                return BuiltInCategory.OST_Columns;
            if (lower == "ceilings" || lower == "天花板")
                return BuiltInCategory.OST_Ceilings;

            // 其他常用
            if (lower == "doors" || lower == "門")
                return BuiltInCategory.OST_Doors;
            if (lower == "windows" || lower == "窗")
                return BuiltInCategory.OST_Windows;
            if (lower == "rooms" || lower == "房間")
                return BuiltInCategory.OST_Rooms;
            if (lower == "genericmodels" || lower == "generic models" || lower == "一般模型")
                return BuiltInCategory.OST_GenericModel;

            throw new Exception($"無法辨識品類: {categoryName}。" +
                "MEP: Pipes, Ducts, CableTrays, Conduits, PipeFittings, DuctFittings | " +
                "CSA: Walls, Floors, StructuralFraming, StructuralColumns, Columns");
        }

        #endregion

        #region Private Helpers

        /// <summary>
        /// 套用參數過濾
        /// </summary>
        private List<Element> ApplyParameterFilters(List<Element> elements, JArray filters, Document targetDoc)
        {
            if (filters == null || filters.Count == 0) return elements;

            return elements.Where(e =>
            {
                foreach (var filter in filters)
                {
                    string field = filter["field"]?.Value<string>();
                    string op = filter["operator"]?.Value<string>();
                    string targetValue = filter["value"]?.Value<string>();

                    if (string.IsNullOrEmpty(field)) continue;

                    string actualValue = GetParameterValue(e, field, targetDoc);
                    if (!MatchFilter(actualValue, op, targetValue))
                        return false;
                }
                return true;
            }).ToList();
        }

        /// <summary>
        /// 取得元素的參數值（包含類型參數）
        /// </summary>
        private string GetParameterValue(Element elem, string paramName, Document doc)
        {
            // 實例參數
            foreach (Parameter p in elem.Parameters)
            {
                if (p.Definition.Name.Equals(paramName, StringComparison.OrdinalIgnoreCase))
                    return p.AsValueString() ?? p.AsString() ?? "";
            }

            // 類型參數
            Element typeElem = doc.GetElement(elem.GetTypeId());
            if (typeElem != null)
            {
                foreach (Parameter p in typeElem.Parameters)
                {
                    if (p.Definition.Name.Equals(paramName, StringComparison.OrdinalIgnoreCase))
                        return p.AsValueString() ?? p.AsString() ?? "";
                }
            }

            return "";
        }

        /// <summary>
        /// 過濾條件比對
        /// </summary>
        private bool MatchFilter(string actualValue, string op, string targetValue)
        {
            if (string.IsNullOrEmpty(actualValue)) return false;

            switch (op?.ToLowerInvariant())
            {
                case "equals":
                    return actualValue.Equals(targetValue, StringComparison.OrdinalIgnoreCase);
                case "contains":
                    return actualValue.IndexOf(targetValue, StringComparison.OrdinalIgnoreCase) >= 0;
                case "not_equals":
                    return !actualValue.Equals(targetValue, StringComparison.OrdinalIgnoreCase);
                case "less_than":
                case "greater_than":
                    string cleanVal = System.Text.RegularExpressions.Regex.Replace(actualValue, @"[^\d.-]", "");
                    if (double.TryParse(cleanVal, out double v1) && double.TryParse(targetValue, out double v2))
                    {
                        return op == "less_than" ? v1 < v2 : v1 > v2;
                    }
                    return false;
                default:
                    return true;
            }
        }

        /// <summary>
        /// 建立連結元素資料
        /// </summary>
        private Dictionary<string, object> BuildLinkedElementData(
            Element elem, Document linkDoc, Transform transform, List<string> returnFields)
        {
            var data = new Dictionary<string, object>
            {
                { "ElementId", elem.Id.GetIdValue() },
                { "Name", elem.Name ?? "" },
                { "Category", elem.Category?.Name ?? "" },
                { "TypeName", linkDoc.GetElement(elem.GetTypeId())?.Name ?? "" }
            };

            // 加入位置資訊
            var location = elem.Location;
            if (location is LocationCurve locCurve)
            {
                var curve = locCurve.Curve;
                var start = transform.OfPoint(curve.GetEndPoint(0));
                var end = transform.OfPoint(curve.GetEndPoint(1));

                data["Location"] = new
                {
                    Type = "Curve",
                    StartX = Math.Round(start.X * 304.8, 2),
                    StartY = Math.Round(start.Y * 304.8, 2),
                    StartZ = Math.Round(start.Z * 304.8, 2),
                    EndX = Math.Round(end.X * 304.8, 2),
                    EndY = Math.Round(end.Y * 304.8, 2),
                    EndZ = Math.Round(end.Z * 304.8, 2),
                    Length = Math.Round(curve.Length * 304.8, 2)
                };
            }
            else if (location is LocationPoint locPoint)
            {
                var pt = transform.OfPoint(locPoint.Point);
                data["Location"] = new
                {
                    Type = "Point",
                    X = Math.Round(pt.X * 304.8, 2),
                    Y = Math.Round(pt.Y * 304.8, 2),
                    Z = Math.Round(pt.Z * 304.8, 2)
                };
            }

            // 額外要求的欄位
            if (returnFields != null)
            {
                foreach (string fieldName in returnFields)
                {
                    if (data.ContainsKey(fieldName)) continue;
                    string val = GetParameterValue(elem, fieldName, linkDoc);
                    data[fieldName] = string.IsNullOrEmpty(val) ? "N/A" : val;
                }
            }

            return data;
        }

        /// <summary>
        /// 抽取中心線資訊
        /// </summary>
        private object ExtractCenterline(Element element, Transform transform)
        {
            var location = element.Location;
            if (location is LocationCurve locCurve)
            {
                var curve = locCurve.Curve;
                var transformedCurve = curve.CreateTransformed(transform);
                var start = transformedCurve.GetEndPoint(0);
                var end = transformedCurve.GetEndPoint(1);
                var direction = (end - start).Normalize();

                return new
                {
                    HasCenterline = true,
                    StartX = Math.Round(start.X * 304.8, 2),
                    StartY = Math.Round(start.Y * 304.8, 2),
                    StartZ = Math.Round(start.Z * 304.8, 2),
                    EndX = Math.Round(end.X * 304.8, 2),
                    EndY = Math.Round(end.Y * 304.8, 2),
                    EndZ = Math.Round(end.Z * 304.8, 2),
                    DirectionX = Math.Round(direction.X, 6),
                    DirectionY = Math.Round(direction.Y, 6),
                    DirectionZ = Math.Round(direction.Z, 6),
                    Length = Math.Round(transformedCurve.Length * 304.8, 2)
                };
            }

            return new { HasCenterline = false, Message = "此元素沒有中心線（非線性元素）" };
        }

        /// <summary>
        /// 抽取 Bounding Box 資訊
        /// </summary>
        private object ExtractBoundingBox(Element element, Transform transform)
        {
            var bbox = element.get_BoundingBox(null);
            if (bbox == null)
                return new { HasBoundingBox = false, Message = "無法取得 Bounding Box" };

            var min = transform.OfPoint(bbox.Min);
            var max = transform.OfPoint(bbox.Max);

            return new
            {
                HasBoundingBox = true,
                MinX = Math.Round(min.X * 304.8, 2),
                MinY = Math.Round(min.Y * 304.8, 2),
                MinZ = Math.Round(min.Z * 304.8, 2),
                MaxX = Math.Round(max.X * 304.8, 2),
                MaxY = Math.Round(max.Y * 304.8, 2),
                MaxZ = Math.Round(max.Z * 304.8, 2)
            };
        }

        /// <summary>
        /// 抽取 Solid 統計資訊（不傳完整 mesh）
        /// </summary>
        private object ExtractSolidInfo(Element element, Transform transform)
        {
            try
            {
                var options = new Options { DetailLevel = ViewDetailLevel.Fine };
                var geoElement = element.get_Geometry(options);
                if (geoElement == null)
                    return new { HasSolid = false, Message = "無法取得幾何" };

                double totalVolume = 0;
                double totalSurfaceArea = 0;
                int solidCount = 0;

                foreach (var geoObj in geoElement)
                {
                    Solid solid = geoObj as Solid;
                    if (solid == null && geoObj is GeometryInstance gi)
                    {
                        foreach (var innerObj in gi.GetInstanceGeometry(transform))
                        {
                            solid = innerObj as Solid;
                            if (solid != null && solid.Volume > 0)
                            {
                                totalVolume += solid.Volume;
                                totalSurfaceArea += solid.SurfaceArea;
                                solidCount++;
                            }
                        }
                        continue;
                    }

                    if (solid != null && solid.Volume > 0)
                    {
                        totalVolume += solid.Volume;
                        totalSurfaceArea += solid.SurfaceArea;
                        solidCount++;
                    }
                }

                // 轉換單位: 立方英尺 → 立方公釐, 平方英尺 → 平方公釐
                double volumeMm3 = totalVolume * Math.Pow(304.8, 3);
                double surfaceAreaMm2 = totalSurfaceArea * Math.Pow(304.8, 2);

                return new
                {
                    HasSolid = solidCount > 0,
                    SolidCount = solidCount,
                    Volume = Math.Round(volumeMm3, 2),
                    VolumeUnit = "mm³",
                    SurfaceArea = Math.Round(surfaceAreaMm2, 2),
                    SurfaceAreaUnit = "mm²"
                };
            }
            catch (Exception ex)
            {
                return new { HasSolid = false, Message = $"幾何抽取失敗: {ex.Message}" };
            }
        }

        #endregion
    }
}
