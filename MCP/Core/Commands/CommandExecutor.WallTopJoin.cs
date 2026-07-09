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
        #region 牆頂接合

        /// <summary>
        /// 將指定樓層的牆頂與上方的樓板/天花板/結構構件做幾何接合。
        /// 由 JoinWallTop 獨立外掛移植為無對話框版本：原本由 WPF 勾選的樓層，改由 levels 參數帶入。
        /// </summary>
        private object JoinWallTops(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;

            List<Level> allLevels = new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .OrderBy(lv => lv.Elevation)
                .ToList();

            if (allLevels.Count == 0)
                throw new Exception("專案中沒有任何樓層。");

            // levels 省略或空陣列 = 處理全部樓層
            var levelsArray = parameters["levels"] as JArray;
            List<Level> selectedLevels;
            if (levelsArray == null || levelsArray.Count == 0)
            {
                selectedLevels = allLevels;
            }
            else
            {
                var names = levelsArray
                    .Select(t => t.Value<string>())
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .ToList();

                selectedLevels = allLevels
                    .Where(lv => names.Any(n => lv.Name == n || lv.Name.Contains(n) || n.Contains(lv.Name)))
                    .ToList();

                if (selectedLevels.Count == 0)
                    throw new Exception($"找不到符合的樓層：{string.Join(", ", names)}");
            }

            HashSet<ElementId> selectedLevelIds =
                new HashSet<ElementId>(selectedLevels.Select(lv => lv.Id));

            List<Wall> walls = new FilteredElementCollector(doc)
                .OfClass(typeof(Wall))
                .Cast<Wall>()
                .Where(w =>
                {
                    Parameter p = w.get_Parameter(BuiltInParameter.WALL_BASE_CONSTRAINT);
                    return p != null && selectedLevelIds.Contains(p.AsElementId());
                })
                .ToList();

            if (walls.Count == 0)
            {
                return new
                {
                    Success = true,
                    ProcessedLevels = selectedLevels.Count,
                    ProcessedWalls = 0,
                    Joined = 0,
                    Skipped = 0,
                    Failed = 0,
                    Message = "所選樓層中沒有牆體。"
                };
            }

            ElementMulticategoryFilter categoryFilter = new ElementMulticategoryFilter(
                new List<BuiltInCategory>
                {
                    BuiltInCategory.OST_Floors,
                    BuiltInCategory.OST_Ceilings,
                    BuiltInCategory.OST_StructuralFraming
                });

            int joinedCount = 0, skippedCount = 0, failedCount = 0;
            List<string> failedInfos = new List<string>();
            Dictionary<string, int> levelStats = selectedLevels.ToDictionary(lv => lv.Name, lv => 0);

            using (Transaction trans = new Transaction(doc, "接合牆體頂部與上方構件"))
            {
                trans.Start();

                foreach (Wall wall in walls)
                {
                    BoundingBoxXYZ wallBB = wall.get_BoundingBox(null);
                    if (wallBB == null) continue;

                    Parameter baseLevelParam = wall.get_Parameter(BuiltInParameter.WALL_BASE_CONSTRAINT);
                    Level baseLevel = doc.GetElement(baseLevelParam.AsElementId()) as Level;
                    string levelName = baseLevel?.Name ?? "未知";

                    double searchDown = 1.0, searchUp = 2.0, hTol = 0.1;

                    // 先用 BoundingBox 粗篩，再用幾何相交精確過濾，避免接合實際未相交的元素
                    Outline searchOutline = new Outline(
                        new XYZ(wallBB.Min.X - hTol, wallBB.Min.Y - hTol, wallBB.Max.Z - searchDown),
                        new XYZ(wallBB.Max.X + hTol, wallBB.Max.Y + hTol, wallBB.Max.Z + searchUp));

                    List<Element> targets = new FilteredElementCollector(doc)
                        .WherePasses(categoryFilter)
                        .WherePasses(new BoundingBoxIntersectsFilter(searchOutline))
                        .WherePasses(new ElementIntersectsElementFilter(wall))
                        .WhereElementIsNotElementType()
                        .ToList();

                    foreach (Element target in targets)
                    {
                        try
                        {
                            if (JoinGeometryUtils.AreElementsJoined(doc, wall, target))
                            {
                                skippedCount++;
                                continue;
                            }

                            JoinGeometryUtils.JoinGeometry(doc, wall, target);
                            joinedCount++;
                            if (levelStats.ContainsKey(levelName))
                                levelStats[levelName]++;
                        }
                        catch (Exception)
                        {
                            failedCount++;
                            string categoryName = target.Category?.Name ?? "未知類別";
                            failedInfos.Add(
                                $"[{levelName}] 牆 ID:{wall.Id.GetIdValue()} ↔ {categoryName} ID:{target.Id.GetIdValue()}");
                        }
                    }
                }

                trans.Commit();
            }

            return new
            {
                Success = true,
                ProcessedLevels = selectedLevels.Count,
                ProcessedWalls = walls.Count,
                Joined = joinedCount,
                Skipped = skippedCount,
                Failed = failedCount,
                LevelStats = levelStats.Select(kv => new { Level = kv.Key, Joined = kv.Value }).ToList(),
                Failures = failedInfos,
                Message = $"成功接合 {joinedCount} 組，已接合跳過 {skippedCount} 組，失敗 {failedCount} 組。"
            };
        }

        #endregion
    }
}
