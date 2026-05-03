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
    public partial class CommandExecutor
    {
        /// <summary>
        /// 解除柱子與鄰近牆、樓板、結構樑的幾何接合。
        /// 配對存入 _unjoinedPairs，可用 rejoin_wall_joins 還原（限同一 Revit session）。
        /// </summary>
        private object UnjoinColumnJoins(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;

            var columnIdsArray = parameters["columnIds"] as JArray;
            IdType? viewId = parameters["viewId"]?.Value<IdType>();

            List<IdType> columnIds = new List<IdType>();
            if (columnIdsArray != null)
            {
                columnIds.AddRange(columnIdsArray.Select(id => id.Value<IdType>()));
            }

            // 若未指定 columnIds，則收集所有柱子（視圖內或整個專案）
            if (columnIds.Count == 0)
            {
                FilteredElementCollector archCols = viewId.HasValue
                    ? new FilteredElementCollector(doc, new ElementId(viewId.Value))
                    : new FilteredElementCollector(doc);
                FilteredElementCollector structCols = viewId.HasValue
                    ? new FilteredElementCollector(doc, new ElementId(viewId.Value))
                    : new FilteredElementCollector(doc);

                var arch = archCols.OfCategory(BuiltInCategory.OST_Columns)
                                   .WhereElementIsNotElementType()
                                   .ToElements();
                var stru = structCols.OfCategory(BuiltInCategory.OST_StructuralColumns)
                                     .WhereElementIsNotElementType()
                                     .ToElements();

                columnIds = arch.Concat(stru)
                                .Select(c => c.Id.GetIdValue())
                                .ToList();
            }

            if (columnIds.Count == 0)
            {
                return new
                {
                    Success = true,
                    UnjoinedCount = 0,
                    ColumnCount = 0,
                    StoredPairs = _unjoinedPairs.Count,
                    Message = "未找到任何柱子"
                };
            }

            int unjoinedCount = 0;
            int wallPairs = 0;
            int floorPairs = 0;
            int framingPairs = 0;

            // 重要：沿用 _unjoinedPairs 給 rejoin_wall_joins 使用，不能 Clear
            // 但若同一 session 內重複呼叫，為避免重複 pair，先去重
            var existingPairs = new HashSet<string>(
                _unjoinedPairs.Select(p => PairKey(p.Item1, p.Item2)));

            using (Transaction trans = TransactionHelper.Begin(doc, "Unjoin Column Geometry"))
            {
                trans.Start();

                foreach (IdType columnId in columnIds)
                {
                    Element column = doc.GetElement(new ElementId(columnId));
                    if (column == null) continue;

                    BoundingBoxXYZ bbox = column.get_BoundingBox(null);
                    if (bbox == null) continue;

                    XYZ min = bbox.Min - new XYZ(1, 1, 1);
                    XYZ max = bbox.Max + new XYZ(1, 1, 1);
                    Outline outline = new Outline(min, max);
                    var bboxFilter = new BoundingBoxIntersectsFilter(outline);

                    // 依序處理：牆、樓板、結構樑
                    var wallNeighbors = new FilteredElementCollector(doc)
                        .OfCategory(BuiltInCategory.OST_Walls)
                        .WhereElementIsNotElementType()
                        .WherePasses(bboxFilter)
                        .ToElements();

                    var floorNeighbors = new FilteredElementCollector(doc)
                        .OfCategory(BuiltInCategory.OST_Floors)
                        .WhereElementIsNotElementType()
                        .WherePasses(bboxFilter)
                        .ToElements();

                    var framingNeighbors = new FilteredElementCollector(doc)
                        .OfCategory(BuiltInCategory.OST_StructuralFraming)
                        .WhereElementIsNotElementType()
                        .WherePasses(bboxFilter)
                        .ToElements();

                    unjoinedCount += TryUnjoinBatch(doc, column, wallNeighbors, existingPairs, ref wallPairs);
                    unjoinedCount += TryUnjoinBatch(doc, column, floorNeighbors, existingPairs, ref floorPairs);
                    unjoinedCount += TryUnjoinBatch(doc, column, framingNeighbors, existingPairs, ref framingPairs);
                }

                trans.Commit();
            }

            return new
            {
                Success = true,
                UnjoinedCount = unjoinedCount,
                ColumnCount = columnIds.Count,
                WallPairs = wallPairs,
                FloorPairs = floorPairs,
                FramingPairs = framingPairs,
                StoredPairs = _unjoinedPairs.Count,
                Message = $"已取消 {unjoinedCount} 個柱-鄰接元素接合（牆 {wallPairs}, 樓板 {floorPairs}, 樑 {framingPairs}）"
            };
        }

        private int TryUnjoinBatch(Document doc, Element column, IList<Element> neighbors,
                                   HashSet<string> existingPairs, ref int categoryCount)
        {
            int count = 0;
            foreach (Element neighbor in neighbors)
            {
                if (neighbor.Id == column.Id) continue;
                string key = PairKey(column.Id, neighbor.Id);
                if (existingPairs.Contains(key)) continue;

                try
                {
                    if (JoinGeometryUtils.AreElementsJoined(doc, column, neighbor))
                    {
                        JoinGeometryUtils.UnjoinGeometry(doc, column, neighbor);
                        _unjoinedPairs.Add(new Tuple<ElementId, ElementId>(column.Id, neighbor.Id));
                        existingPairs.Add(key);
                        count++;
                        categoryCount++;
                    }
                }
                catch
                {
                    // 忽略無法取消接合的配對
                }
            }
            return count;
        }

        private static string PairKey(ElementId a, ElementId b)
        {
            IdType av = a.GetIdValue();
            IdType bv = b.GetIdValue();
            return av < bv ? $"{av}-{bv}" : $"{bv}-{av}";
        }

        /// <summary>
        /// 通用版：以任一類別為中心解除其與指定 target 類別的幾何接合。
        /// 預設 target 為 8 類（Walls/Floors/Columns/StructuralColumns/
        /// StructuralFraming/StructuralFoundation/Roofs/Ceilings）。
        /// 共用 _unjoinedPairs 與 rejoin_wall_joins 還原路徑。
        /// </summary>
        private object UnjoinElementJoins(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;

            string sourceCategoryStr = parameters["sourceCategory"]?.Value<string>();
            if (string.IsNullOrEmpty(sourceCategoryStr))
                throw new Exception("必須提供 sourceCategory 參數 (例如 StructuralFraming、Walls、Floors)");

            BuiltInCategory sourceCat;
            if (!Enum.TryParse($"OST_{sourceCategoryStr}", out sourceCat))
                throw new Exception($"無法解析 sourceCategory: {sourceCategoryStr}（應為 Walls、Floors、Columns、StructuralColumns、StructuralFraming、StructuralFoundation、Roofs、Ceilings 等）");

            var targetCatsArray = parameters["targetCategories"] as JArray;
            List<BuiltInCategory> targetCats = new List<BuiltInCategory>();
            List<string> targetNames = new List<string>();
            if (targetCatsArray != null && targetCatsArray.Count > 0)
            {
                foreach (var tc in targetCatsArray)
                {
                    string name = tc.Value<string>();
                    if (Enum.TryParse($"OST_{name}", out BuiltInCategory cat))
                    {
                        targetCats.Add(cat);
                        targetNames.Add(name);
                    }
                }
            }
            else
            {
                var defaults = new[]
                {
                    ("Walls", BuiltInCategory.OST_Walls),
                    ("Floors", BuiltInCategory.OST_Floors),
                    ("Columns", BuiltInCategory.OST_Columns),
                    ("StructuralColumns", BuiltInCategory.OST_StructuralColumns),
                    ("StructuralFraming", BuiltInCategory.OST_StructuralFraming),
                    ("StructuralFoundation", BuiltInCategory.OST_StructuralFoundation),
                    ("Roofs", BuiltInCategory.OST_Roofs),
                    ("Ceilings", BuiltInCategory.OST_Ceilings),
                };
                foreach (var (name, cat) in defaults)
                {
                    targetCats.Add(cat);
                    targetNames.Add(name);
                }
            }

            var elementIdsArray = parameters["elementIds"] as JArray;
            IdType? viewId = parameters["viewId"]?.Value<IdType>();

            List<IdType> elementIds = new List<IdType>();
            if (elementIdsArray != null)
                elementIds.AddRange(elementIdsArray.Select(id => id.Value<IdType>()));

            if (elementIds.Count == 0)
            {
                var collector = viewId.HasValue
                    ? new FilteredElementCollector(doc, new ElementId(viewId.Value))
                    : new FilteredElementCollector(doc);
                elementIds = collector.OfCategory(sourceCat)
                                      .WhereElementIsNotElementType()
                                      .ToElements()
                                      .Select(e => e.Id.GetIdValue())
                                      .ToList();
            }

            if (elementIds.Count == 0)
            {
                return new
                {
                    Success = true,
                    UnjoinedCount = 0,
                    SourceCategory = sourceCategoryStr,
                    SourceCount = 0,
                    StoredPairs = _unjoinedPairs.Count,
                    Message = $"未找到類別 {sourceCategoryStr} 的元素"
                };
            }

            int unjoinedCount = 0;
            var pairsByCategory = new Dictionary<string, int>();
            for (int i = 0; i < targetNames.Count; i++)
                pairsByCategory[targetNames[i]] = 0;

            var existingPairs = new HashSet<string>(
                _unjoinedPairs.Select(p => PairKey(p.Item1, p.Item2)));

            using (Transaction trans = TransactionHelper.Begin(doc, $"Unjoin {sourceCategoryStr} Geometry"))
            {
                trans.Start();

                foreach (IdType sourceId in elementIds)
                {
                    Element source = doc.GetElement(new ElementId(sourceId));
                    if (source == null) continue;

                    BoundingBoxXYZ bbox = source.get_BoundingBox(null);
                    if (bbox == null) continue;

                    XYZ min = bbox.Min - new XYZ(1, 1, 1);
                    XYZ max = bbox.Max + new XYZ(1, 1, 1);
                    Outline outline = new Outline(min, max);
                    var bboxFilter = new BoundingBoxIntersectsFilter(outline);

                    for (int i = 0; i < targetCats.Count; i++)
                    {
                        var neighbors = new FilteredElementCollector(doc)
                            .OfCategory(targetCats[i])
                            .WhereElementIsNotElementType()
                            .WherePasses(bboxFilter)
                            .ToElements();

                        int categoryCount = 0;
                        unjoinedCount += TryUnjoinBatch(doc, source, neighbors, existingPairs, ref categoryCount);
                        pairsByCategory[targetNames[i]] += categoryCount;
                    }
                }

                trans.Commit();
            }

            string breakdown = string.Join(", ",
                pairsByCategory.Where(kv => kv.Value > 0).Select(kv => $"{kv.Key} {kv.Value}"));
            if (string.IsNullOrEmpty(breakdown)) breakdown = "無新增接合解除";

            return new
            {
                Success = true,
                UnjoinedCount = unjoinedCount,
                SourceCategory = sourceCategoryStr,
                SourceCount = elementIds.Count,
                PairsByCategory = pairsByCategory,
                StoredPairs = _unjoinedPairs.Count,
                Message = $"已取消 {unjoinedCount} 個 {sourceCategoryStr}-鄰接元素接合（{breakdown}）"
            };
        }
    }
}
