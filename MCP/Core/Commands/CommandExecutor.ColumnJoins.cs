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
        /// 相容 shim（#138 C3）：unjoin_wall_joins 已自 MCP registry 移除，命令名保留給舊版 MCP-Server
        /// 與既有腳本。等價於 unjoin_element_joins(sourceCategory: Walls, targetCategories: [Columns,
        /// StructuralColumns])。保留舊工具的兩個契約：未給 wallIds 也未給 viewId 時拋錯（不擴及全專案），
        /// 以及開始前清空 _unjoinedPairs（rejoin 只還原這一次）。
        /// </summary>
        private object UnjoinWallJoinsCompat(JObject parameters)
        {
            var wallIds = parameters["wallIds"] as JArray;
            var viewId = parameters["viewId"];
            if ((wallIds == null || wallIds.Count == 0) && viewId == null)
                throw new Exception("請提供 wallIds 或 viewId 參數");

            _unjoinedPairs.Clear();
            var translated = new JObject
            {
                ["sourceCategory"] = "Walls",
                ["targetCategories"] = new JArray("Columns", "StructuralColumns"),
            };
            if (wallIds != null) translated["elementIds"] = wallIds;
            if (viewId != null) translated["viewId"] = viewId;
            return UnjoinElementJoins(translated);
        }

        /// <summary>
        /// 相容 shim（#138 C3）：unjoin_column_joins 已自 MCP registry 移除。等價於
        /// unjoin_element_joins(sourceCategories: [Columns, StructuralColumns],
        /// targetCategories: [Walls, Floors, StructuralFraming])。
        /// </summary>
        private object UnjoinColumnJoinsCompat(JObject parameters)
        {
            var translated = new JObject
            {
                ["sourceCategories"] = new JArray("Columns", "StructuralColumns"),
                ["targetCategories"] = new JArray("Walls", "Floors", "StructuralFraming"),
            };
            if (parameters["columnIds"] is JArray columnIds) translated["elementIds"] = columnIds;
            if (parameters["viewId"] != null) translated["viewId"] = parameters["viewId"];
            return UnjoinElementJoins(translated);
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
        /// 來源可用 sourceCategory（單一）或 sourceCategories（陣列）指定。
        /// </summary>
        private object UnjoinElementJoins(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;

            // sourceCategories（陣列）與 sourceCategory（單一）擇一；陣列讓一次呼叫涵蓋建築柱＋結構柱這類成對類別
            var sourceNames = new List<string>();
            if (parameters["sourceCategories"] is JArray sourceCatsArray)
                sourceNames.AddRange(sourceCatsArray.Select(t => t.Value<string>()).Where(n => !string.IsNullOrEmpty(n)));
            string singleSource = parameters["sourceCategory"]?.Value<string>();
            if (!string.IsNullOrEmpty(singleSource) && !sourceNames.Contains(singleSource))
                sourceNames.Add(singleSource);
            if (sourceNames.Count == 0)
                throw new Exception("必須提供 sourceCategory 或 sourceCategories 參數 (例如 StructuralFraming、Walls、Floors)");

            var sourceCats = new List<BuiltInCategory>();
            foreach (string name in sourceNames)
            {
                if (!Enum.TryParse($"OST_{name}", out BuiltInCategory cat))
                    throw new Exception($"無法解析 sourceCategory: {name}（應為 Walls、Floors、Columns、StructuralColumns、StructuralFraming、StructuralFoundation、Roofs、Ceilings 等）");
                sourceCats.Add(cat);
            }
            string sourceCategoryStr = string.Join("+", sourceNames);

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
                foreach (BuiltInCategory sourceCat in sourceCats)
                {
                    var collector = viewId.HasValue
                        ? new FilteredElementCollector(doc, new ElementId(viewId.Value))
                        : new FilteredElementCollector(doc);
                    elementIds.AddRange(collector.OfCategory(sourceCat)
                                                 .WhereElementIsNotElementType()
                                                 .ToElements()
                                                 .Select(e => e.Id.GetIdValue()));
                }
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
