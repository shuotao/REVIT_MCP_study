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
            if(parameters["wallTopToBeams"]!=null) {
                if(parameters["wallTopToFloor"]!=null||parameters["wallBottomToBeam"]!=null||new[]{"floorCutsWall","beamCutsWall","auditBeamFloor","beamCutsFloor"}.Any(k=>parameters[k]?.Value<bool>()==true))throw new ArgumentException("Choose one mode.");
                return FitWallTopToBeamExtremes(parameters);
            }
            if(parameters["wallTopToFloor"]!=null) {
                if(parameters["wallBottomToBeam"]!=null||new[]{"floorCutsWall","beamCutsWall","auditBeamFloor","beamCutsFloor"}.Any(k=>parameters[k]?.Value<bool>()==true))throw new ArgumentException("Choose one mode.");
                return FitWallTopToFloor(parameters);
            }
            if(parameters["wallBottomToBeam"]!=null) {
                if(new[]{"floorCutsWall","beamCutsWall","auditBeamFloor","beamCutsFloor"}.Any(k=>parameters[k]?.Value<bool>()==true))throw new ArgumentException("Choose one mode.");
                return FitWallBottomToBeam(parameters);
            }
            if (new[]{"floorCutsWall","beamCutsWall","auditBeamFloor","beamCutsFloor"}.Count(k=>parameters[k]?.Value<bool>() == true)>1) throw new ArgumentException("Choose only one cutting mode.");
            if (parameters["beamCutsFloor"]?.Value<bool>() == true) return ApplyBeamFloorJoins(parameters);
            if (parameters["auditBeamFloor"]?.Value<bool>() == true) return AuditBeamFloorJoins(parameters);
            if (parameters["floorCutsWall"]?.Value<bool>() == true) return JoinWallsFloors(parameters);
            if (parameters["beamCutsWall"]?.Value<bool>() == true) return JoinWallTopBeams(parameters);
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

        // Opt-in beam-only mode; legacy floor/ceiling behavior remains unchanged.
        private object JoinWallTopBeams(JObject parameters)
        {
            var doc = _uiApp.ActiveUIDocument.Document;
            bool dryRun = parameters["dryRun"]?.Value<bool>() ?? true;
            bool apply = parameters["apply"]?.Value<bool>() ?? false;
            var names = (parameters["levels"] as JArray)?.Values<string>().ToList();
            var levels = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
                .Where(l => names == null || names.Count == 0 || names.Contains(l.Name)).ToList();
            if (levels.Count == 0) throw new ArgumentException("No exact level names matched.");
            var levelIds = new HashSet<ElementId>(levels.Select(l => l.Id));
            var walls = new FilteredElementCollector(doc).OfClass(typeof(Wall)).Cast<Wall>()
                .Where(w => levelIds.Contains(w.get_Parameter(BuiltInParameter.WALL_BASE_CONSTRAINT).AsElementId())).ToList();
            var pairs = new List<Tuple<Wall, Element, bool, bool>>();
            foreach (var wall in walls)
            {
                var bb = wall.get_BoundingBox(null); if (bb == null) continue;
                // Include existing joins explicitly: joined solids need not intersect anymore.
                var existing = new HashSet<ElementId>(JoinGeometryUtils.GetJoinedElements(doc, wall));
                var outline = new Outline(new XYZ(bb.Min.X - 0.01, bb.Min.Y - 0.01, bb.Max.Z - 0.01),
                    new XYZ(bb.Max.X + 0.01, bb.Max.Y + 0.01, bb.Max.Z + 0.01));
                var candidates = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_StructuralFraming)
                    .WhereElementIsNotElementType().WherePasses(new BoundingBoxIntersectsFilter(outline)).ToList();
                foreach (var id in existing)
                {
                    var e = doc.GetElement(id);
                    if (e?.Category?.Id.GetIdValue() == (long)BuiltInCategory.OST_StructuralFraming && !candidates.Any(c => c.Id == id)) candidates.Add(e);
                }
                foreach (var beam in candidates)
                {
                    if (!(beam is FamilyInstance fi) || fi.StructuralType != Autodesk.Revit.DB.Structure.StructuralType.Beam) continue;
                    var beamBB = beam.get_BoundingBox(null); if (beamBB == null) continue;
                    // A top beam spans the wall's top elevation; floor-level beams are excluded.
                    if (beamBB.Max.Z < bb.Max.Z - 0.01 || beamBB.Min.Z > bb.Max.Z + 0.01 || beamBB.Min.Z <= bb.Min.Z + 0.01) continue;
                    bool joined = JoinGeometryUtils.AreElementsJoined(doc, wall, beam);
                    if (!joined && !new ElementIntersectsElementFilter(wall).PassesFilter(doc, beam.Id)) continue;
                    bool correct = joined && JoinGeometryUtils.IsCuttingElementInJoin(doc, beam, wall);
                    pairs.Add(Tuple.Create(wall, beam, joined, correct));
                }
            }
            var rows = pairs.Select(p => new JObject { ["WallId"] = p.Item1.Id.GetIdValue(), ["BeamId"] = p.Item2.Id.GetIdValue(),
                ["Status"] = p.Item4 ? "Unchanged" : "Planned", ["Action"] = p.Item4 ? "None" : p.Item3 ? "SwitchOrder" : "Join" }).ToList();
            bool committed = false; string error = null;
            if (!dryRun && apply && pairs.Any(p => !p.Item4))
            {
                using (var tx = new Transaction(doc, "Join wall tops - beams cut walls"))
                {
                    try
                    {
                        if (tx.Start() != TransactionStatus.Started) throw new InvalidOperationException("Transaction start failed.");
                        for (int i = 0; i < pairs.Count; i++)
                        {
                            var p = pairs[i]; if (p.Item4) continue;
                            using (var sub = new SubTransaction(doc))
                            {
                                sub.Start();
                                try
                                {
                                    if (!p.Item3) JoinGeometryUtils.JoinGeometry(doc, p.Item2, p.Item1);
                                    if (!JoinGeometryUtils.IsCuttingElementInJoin(doc, p.Item2, p.Item1)) JoinGeometryUtils.SwitchJoinOrder(doc, p.Item2, p.Item1);
                                    doc.Regenerate();
                                    if (!JoinGeometryUtils.AreElementsJoined(doc, p.Item2, p.Item1) || !JoinGeometryUtils.IsCuttingElementInJoin(doc, p.Item2, p.Item1)) throw new InvalidOperationException("Beam cutting order verification failed.");
                                    if (sub.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("Pair commit failed.");
                                    rows[i]["Status"] = "Updated";
                                }
                                catch (Exception ex)
                                {
                                    if (sub.GetStatus() == TransactionStatus.Started) sub.RollBack();
                                    rows[i]["Status"] = "Failed"; rows[i]["Reason"] = ex.Message;
                                }
                            }
                        }
                        if (tx.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("Batch commit failed.");
                        committed = true;
                    }
                    catch (Exception ex)
                    {
                        if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
                        error = ex.Message;
                        foreach (var r in rows.Where(r => (string)r["Status"] == "Updated")) { r["Status"] = "Failed"; r["Reason"] = "BatchNotCommitted"; }
                    }
                }
            }
            return new { Success = error == null, DryRun = dryRun, Apply = apply, Committed = committed, Error = error,
                ProcessedWalls = walls.Count, Scanned = pairs.Count, Planned = pairs.Count(p => !p.Item4),
                Updated = rows.Count(r => (string)r["Status"] == "Updated"), Unchanged = rows.Count(r => (string)r["Status"] == "Unchanged"),
                Failed = rows.Count(r => (string)r["Status"] == "Failed"), Results = rows };
        }

        private object JoinWallsFloors(JObject parameters)
        {
            var doc = _uiApp.ActiveUIDocument.Document;
            bool dryRun = parameters["dryRun"]?.Value<bool>() ?? true;
            bool apply = parameters["apply"]?.Value<bool>() ?? false;
            var names = (parameters["levels"] as JArray)?.Values<string>().ToList();
            var levels = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
                .Where(l => names == null || names.Count == 0 || names.Contains(l.Name)).ToList();
            if (levels.Count == 0) throw new ArgumentException("No exact level names matched.");
            var levelIds = new HashSet<ElementId>(levels.Select(l => l.Id));
            var walls = new FilteredElementCollector(doc).OfClass(typeof(Wall)).Cast<Wall>()
                .Where(w => levelIds.Contains(w.get_Parameter(BuiltInParameter.WALL_BASE_CONSTRAINT).AsElementId())).ToList();
            var pairs = new List<Tuple<Wall, Element, bool, bool>>();
            foreach (var wall in walls)
            {
                var bb = wall.get_BoundingBox(null); if (bb == null) continue;
                // Include existing joins explicitly: joined solids need not intersect anymore.
                var existing = new HashSet<ElementId>(JoinGeometryUtils.GetJoinedElements(doc, wall));
                var outline = new Outline(new XYZ(bb.Min.X - 0.01, bb.Min.Y - 0.01, bb.Min.Z - 0.01),
                    new XYZ(bb.Max.X + 0.01, bb.Max.Y + 0.01, bb.Max.Z + 0.01));
                var candidates = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Floors)
                    .WhereElementIsNotElementType().WherePasses(new BoundingBoxIntersectsFilter(outline)).ToList();
                foreach (var id in existing)
                {
                    var e = doc.GetElement(id);
                    if (e?.Category?.Id.GetIdValue() == (long)BuiltInCategory.OST_Floors && !candidates.Any(c => c.Id == id)) candidates.Add(e);
                }
                foreach (var beam in candidates)
                {
                    if (!(beam is Floor)) continue;
                    var beamBB = beam.get_BoundingBox(null); if (beamBB == null) continue;
                    bool joined = JoinGeometryUtils.AreElementsJoined(doc, wall, beam);
                    if (!joined && !new ElementIntersectsElementFilter(wall).PassesFilter(doc, beam.Id)) continue;
                    bool correct = joined && JoinGeometryUtils.IsCuttingElementInJoin(doc, beam, wall);
                    pairs.Add(Tuple.Create(wall, beam, joined, correct));
                }
            }
            var rows = pairs.Select(p => new JObject { ["WallId"] = p.Item1.Id.GetIdValue(), ["FloorId"] = p.Item2.Id.GetIdValue(),
                ["Status"] = p.Item4 ? "Unchanged" : "Planned", ["Action"] = p.Item4 ? "None" : p.Item3 ? "SwitchOrder" : "Join" }).ToList();
            bool committed = false; string error = null;
            if (!dryRun && apply && pairs.Any(p => !p.Item4))
            {
                using (var tx = new Transaction(doc, "Join walls - floors cut walls"))
                {
                    try
                    {
                        if (tx.Start() != TransactionStatus.Started) throw new InvalidOperationException("Transaction start failed.");
                        for (int i = 0; i < pairs.Count; i++)
                        {
                            var p = pairs[i]; if (p.Item4) continue;
                            using (var sub = new SubTransaction(doc))
                            {
                                sub.Start();
                                try
                                {
                                    if (!p.Item3) JoinGeometryUtils.JoinGeometry(doc, p.Item2, p.Item1);
                                    if (!JoinGeometryUtils.IsCuttingElementInJoin(doc, p.Item2, p.Item1)) JoinGeometryUtils.SwitchJoinOrder(doc, p.Item2, p.Item1);
                                    doc.Regenerate();
                                    if (!JoinGeometryUtils.AreElementsJoined(doc, p.Item2, p.Item1) || !JoinGeometryUtils.IsCuttingElementInJoin(doc, p.Item2, p.Item1)) throw new InvalidOperationException("Floor cutting order verification failed.");
                                    if (sub.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("Pair commit failed.");
                                    rows[i]["Status"] = "Updated";
                                }
                                catch (Exception ex)
                                {
                                    if (sub.GetStatus() == TransactionStatus.Started) sub.RollBack();
                                    rows[i]["Status"] = "Failed"; rows[i]["Reason"] = ex.Message;
                                }
                            }
                        }
                        if (tx.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("Batch commit failed.");
                        committed = true;
                    }
                    catch (Exception ex)
                    {
                        if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
                        error = ex.Message;
                        foreach (var r in rows.Where(r => (string)r["Status"] == "Updated")) { r["Status"] = "Failed"; r["Reason"] = "BatchNotCommitted"; }
                    }
                }
            }
            return new { Success = error == null, DryRun = dryRun, Apply = apply, Committed = committed, Error = error,
                ProcessedWalls = walls.Count, Scanned = pairs.Count, Planned = pairs.Count(p => !p.Item4),
                Updated = rows.Count(r => (string)r["Status"] == "Updated"), Unchanged = rows.Count(r => (string)r["Status"] == "Unchanged"),
                Failed = rows.Count(r => (string)r["Status"] == "Failed"), Results = rows };
        }

        #endregion
    }
}



