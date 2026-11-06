using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Newtonsoft.Json.Linq;

#if REVIT2025_OR_GREATER
using IdType = System.Int64;
#else
using IdType = System.Int32;
#endif

namespace RevitMCP.Core
{
    /// <summary>
    /// 粉刷層材料圖例自動建立命令
    /// 掃描全專案粉刷層 → 為每種粉刷類型建立 FilledRegionType → 在 Legend 視圖繪製三張表
    /// </summary>
    public partial class CommandExecutor
    {
        // 1 cm = 0.0328084 ft
        private const double CM_TO_FT = 0.0328084;

        // 表格固定尺寸（cm，模型空間，1:100 視圖比例）
        private const double LEGEND_COL_MARK_CM = 130;
        private const double LEGEND_COL_SWATCH_CM = 120;
        private const double LEGEND_COL_NAME_CM = 650;
        private const double LEGEND_ROW_HEIGHT_CM = 50;
        private const double LEGEND_TABLE_GAP_CM = 100;       // 表與表之間的垂直空白
        private const double LEGEND_TEXT_HEIGHT_HALF_CM = 20;    // 類型標記（短碼）bounding box 約 40cm，取一半
        private const double LEGEND_TEXT_HEIGHT_HALF_CM_ZH = 28; // 中文多字元文字 bounding box 略高

        private const double LEGEND_TEXT_HEIGHT_MM = 3.0;
        private const double LEGEND_TEXT_WIDTH_FACTOR = 0.7;
        private const string LEGEND_TEXT_FONT = "Microsoft JhengHei UI";
        private const string LEGEND_TEXT_TYPE_NAME = "粉刷圖例 3mm";

        private class DistinctFinishType
        {
            public string Category;          // "Wall" / "Floor" / "Ceiling"
            public string TypeMark;
            public string TypeName;
            public ElementId TypeId;
            public Material FinishMaterial;  // 從 CompoundStructure Function=Finish 層挑出
            public ElementId FilledRegionTypeId;
            public bool ColorOnlyFallback;   // 表面樣式空，用 Solid Fill + 材料色
        }

        private class DistinctPaintMaterial
        {
            public string Category;          // "Wall" / "Floor" / "Ceiling"
            public ElementId MaterialId;
            public Material Material;
            public string MarkText;          // Material.Mark 或 "(未填)"
            public string DescriptionText;   // Material.Description 或 "(未填)"
            public ElementId FilledRegionTypeId;
            public bool ColorOnlyFallback;
        }

        private class LegendRow
        {
            public string MarkText;
            public string NameText;          // 粉刷: TypeName(+" (僅顏色)"); 油漆: DescriptionText
            public ElementId FilledRegionTypeId;
            public bool IsPaintRow;          // true=油漆；false=粉刷
        }

        private object CreateFinishLegend(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;
            string requestedLegendName = parameters["legendName"]?.Value<string>();
            string requestedTemplateName = parameters["legendTemplateName"]?.Value<string>();

            if (string.IsNullOrEmpty(requestedLegendName))
                requestedLegendName = $"粉刷圖例_{DateTime.Now:yyyyMMdd}";

            var warnings = new List<string>();

            // 前置檢查：Legend template 與 FilledRegionType template 必須存在（在 Transaction 之外）
            View templateLegend = FindLegendTemplate(doc, requestedTemplateName);
            if (templateLegend == null)
                throw new InvalidOperationException(
                    "未在專案中找到任何 Legend 視圖。請先在 Revit 中建立一個空白 Legend（檢視標籤 → 圖例 → 圖例），命名隨意，再重新呼叫此功能。");

            FilledRegionType frtTemplate = new FilteredElementCollector(doc)
                .OfClass(typeof(FilledRegionType))
                .Cast<FilledRegionType>()
                .FirstOrDefault();
            if (frtTemplate == null)
                throw new InvalidOperationException(
                    "專案中沒有任何 FilledRegionType 可作為模板，請先在 Revit 中建立任一填滿區域類型。");

            // 蒐集全專案粉刷類型（在 Transaction 之外，純讀取）
            Logger.Info("[FinishLegend] 開始蒐集全專案粉刷層...");
            var distinctTypes = CollectDistinctFinishTypes(doc, warnings);
            Logger.Info($"[FinishLegend] 粉刷層蒐集完成：共 {distinctTypes.Count} 種粉刷類型");

            // 蒐集全專案油漆材料（Paint 工具塗色，在 Transaction 之外）
            Logger.Info("[FinishLegend] 開始蒐集全專案油漆材料...");
            var distinctPaints = CollectDistinctPaintMaterials(doc, warnings);
            Logger.Info($"[FinishLegend] 油漆材料蒐集完成：共 {distinctPaints.Count} 筆");

            int frtCreated = 0, frtReused = 0;
            int paintFrtCreated = 0, paintFrtReused = 0;
            ElementId newLegendId;
            string finalLegendName;

            FillPatternElement solidFill = new FilteredElementCollector(doc)
                .OfClass(typeof(FillPatternElement))
                .Cast<FillPatternElement>()
                .FirstOrDefault(x => x.GetFillPattern().IsSolidFill);

            using (var tx = new Transaction(doc, "建立粉刷層材料圖例"))
            {
                tx.Start();

                ElementId textTypeId = EnsureLegendTextNoteType(doc);

                foreach (var dft in distinctTypes)
                {
                    bool reused;
                    dft.FilledRegionTypeId = EnsureFilledRegionTypeForFinish(
                        doc, dft, frtTemplate, solidFill, warnings, out reused);
                    if (reused) frtReused++;
                    else if (dft.FilledRegionTypeId != ElementId.InvalidElementId) frtCreated++;
                }

                foreach (var dpm in distinctPaints)
                {
                    bool reused;
                    dpm.FilledRegionTypeId = EnsureFilledRegionTypeForPaint(
                        doc, dpm, frtTemplate, solidFill, warnings, out reused);
                    if (reused) paintFrtReused++;
                    else if (dpm.FilledRegionTypeId != ElementId.InvalidElementId) paintFrtCreated++;
                }

                // 複製 Legend
                newLegendId = templateLegend.Duplicate(ViewDuplicateOption.Duplicate);
                View newLegend = doc.GetElement(newLegendId) as View;
                if (newLegend == null)
                    throw new InvalidOperationException("Legend 視圖複製失敗。");

                // 設定比例 1:100
                var scaleParam = newLegend.get_Parameter(BuiltInParameter.VIEW_SCALE);
                if (scaleParam != null && !scaleParam.IsReadOnly)
                    scaleParam.Set(100);

                finalLegendName = EnsureUniqueLegendName(doc, requestedLegendName);
                newLegend.Name = finalLegendName;

                // 把粉刷類型與油漆材料各自依 Category 分組並轉成 LegendRow
                var floorRows = BuildLegendRowsForCategory(distinctTypes, distinctPaints, "Floor");
                var wallRows = BuildLegendRowsForCategory(distinctTypes, distinctPaints, "Wall");
                var ceilingRows = BuildLegendRowsForCategory(distinctTypes, distinctPaints, "Ceiling");

                double currentTopY_cm = 0;
                currentTopY_cm = DrawCategoryTable(doc, newLegend, "地坪粉刷圖例", floorRows, currentTopY_cm, textTypeId);
                if (floorRows.Count > 0) currentTopY_cm -= LEGEND_TABLE_GAP_CM;
                currentTopY_cm = DrawCategoryTable(doc, newLegend, "牆面粉刷圖例", wallRows, currentTopY_cm, textTypeId);
                if (wallRows.Count > 0) currentTopY_cm -= LEGEND_TABLE_GAP_CM;
                currentTopY_cm = DrawCategoryTable(doc, newLegend, "天花粉刷圖例", ceilingRows, currentTopY_cm, textTypeId);

                tx.Commit();
            }

            if (distinctTypes.Count == 0 && distinctPaints.Count == 0)
                warnings.Add("專案中無偵測到任何粉刷層或油漆材料，圖例為空。");

            return new
            {
                success = true,
                legendViewId = newLegendId.GetIdValue(),
                legendViewName = finalLegendName,
                isNewLegend = true,
                filledRegionTypes = new
                {
                    created = frtCreated,
                    reused = frtReused,
                    paintCreated = paintFrtCreated,
                    paintReused = paintFrtReused
                },
                rows = new
                {
                    floors = distinctTypes.Count(d => d.Category == "Floor"),
                    walls = distinctTypes.Count(d => d.Category == "Wall"),
                    ceilings = distinctTypes.Count(d => d.Category == "Ceiling"),
                    paintFloors = distinctPaints.Count(d => d.Category == "Floor"),
                    paintWalls = distinctPaints.Count(d => d.Category == "Wall"),
                    paintCeilings = distinctPaints.Count(d => d.Category == "Ceiling")
                },
                warnings = warnings.Count > 0 ? warnings.ToArray() : null
            };
        }

        /// <summary>
        /// 合併單一 Category 的粉刷列與油漆列為 LegendRow 列表（粉刷在前、油漆在後）。
        /// </summary>
        private List<LegendRow> BuildLegendRowsForCategory(
            List<DistinctFinishType> finishes, List<DistinctPaintMaterial> paints, string category)
        {
            var result = new List<LegendRow>();

            foreach (var dft in finishes
                .Where(d => d.Category == category)
                .OrderBy(d => d.TypeMark, StringComparer.OrdinalIgnoreCase))
            {
                result.Add(new LegendRow
                {
                    MarkText = dft.TypeMark,
                    NameText = dft.ColorOnlyFallback ? $"{dft.TypeName} (僅顏色)" : dft.TypeName,
                    FilledRegionTypeId = dft.FilledRegionTypeId,
                    IsPaintRow = false
                });
            }

            foreach (var dpm in paints
                .Where(d => d.Category == category)
                .OrderBy(d => d.MarkText, StringComparer.OrdinalIgnoreCase))
            {
                string name = dpm.ColorOnlyFallback ? $"{dpm.DescriptionText} (僅顏色)" : dpm.DescriptionText;
                result.Add(new LegendRow
                {
                    MarkText = dpm.MarkText,
                    NameText = name,
                    FilledRegionTypeId = dpm.FilledRegionTypeId,
                    IsPaintRow = true
                });
            }

            return result;
        }

        // ═══════════════════════════════════════════════════════════
        // 蒐集全專案粉刷類型
        // ═══════════════════════════════════════════════════════════

        private List<DistinctFinishType> CollectDistinctFinishTypes(Document doc, List<string> warnings)
        {
            var allRooms = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType()
                .Cast<Room>()
                .Where(r => r.Area > 0)
                .ToList();

            var dict = new Dictionary<string, DistinctFinishType>();

            using (var calc = new SpatialElementGeometryCalculator(doc))
            {
                foreach (var room in allRooms)
                {
                    try
                    {
                        var finishes = DetectFinishLayersForLegend(doc, room, calc);
                        foreach (var finish in finishes)
                        {
                            string key = $"{finish.Category}|{finish.TypeMark}|{finish.TypeName}";
                            if (dict.ContainsKey(key)) continue;

                            // 從元素的型別取得 CompoundStructure → Function=Finish 層的 Material
                            ElementId typeId = ElementId.InvalidElementId;
                            CompoundStructure cs = null;

                            if (finish.Element is Wall w)
                            {
                                typeId = w.GetTypeId();
                                cs = w.WallType?.GetCompoundStructure();
                            }
                            else if (finish.Element is Floor f)
                            {
                                typeId = f.GetTypeId();
                                cs = f.FloorType?.GetCompoundStructure();
                            }
                            else if (finish.Element is Ceiling c)
                            {
                                typeId = c.GetTypeId();
                                cs = (doc.GetElement(typeId) as CeilingType)?.GetCompoundStructure();
                            }

                            Material mat = PickFinishMaterial(doc, cs, finish.TypeName, warnings);

                            dict[key] = new DistinctFinishType
                            {
                                Category = finish.Category,
                                TypeMark = finish.TypeMark ?? finish.TypeName,
                                TypeName = finish.TypeName,
                                TypeId = typeId,
                                FinishMaterial = mat
                            };
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Info($"[FinishLegend] Room {room.Name} 偵測失敗，略過：{ex.Message}");
                    }
                }
            }

            return dict.Values.ToList();
        }

        /// <summary>
        /// 為單一 Room 跑 Spatial geometry → 收集 hostAreas → 呼叫既有的 DetectFinishLayers
        /// 比 CalculateRoomSurfaces 精簡，只回傳 FinishLayerData 列表
        /// </summary>
        private List<FinishLayerData> DetectFinishLayersForLegend(
            Document doc, Room room, SpatialElementGeometryCalculator calc)
        {
            var results = calc.CalculateSpatialElementGeometry(room);
            Solid roomSolid = results.GetGeometry();

            var hostAreas = new Dictionary<ElementId, HostFaceData>();
            foreach (Face face in roomSolid.Faces)
            {
                XYZ normal = face.ComputeNormal(new UV(0.5, 0.5));
                if (Math.Abs(normal.Z) > 0.8) continue;

                var boundaryFaceInfos = results.GetBoundaryFaceInfo(face);
                if (boundaryFaceInfos == null || boundaryFaceInfos.Count == 0) continue;

                foreach (var faceInfo in boundaryFaceInfos)
                {
                    ElementId hostId = GetHostElementIdFromFaceInfo(doc, faceInfo);
                    if (hostId == ElementId.InvalidElementId) continue;
                    if (!hostAreas.ContainsKey(hostId))
                        hostAreas[hostId] = new HostFaceData { ElementId = hostId, Category = "Wall" };
                }
            }

            return DetectFinishLayers(doc, room, roomSolid, hostAreas);
        }

        /// <summary>
        /// 從 CompoundStructure 挑 Function=Finish1 的層；無則 Finish2；再無則第一個非結構層
        /// </summary>
        private Material PickFinishMaterial(Document doc, CompoundStructure cs, string typeName, List<string> warnings)
        {
            if (cs == null) return null;
            var layers = cs.GetLayers();
            if (layers == null || layers.Count == 0) return null;

            CompoundStructureLayer chosen =
                layers.FirstOrDefault(l => l.Function == MaterialFunctionAssignment.Finish1)
                ?? layers.FirstOrDefault(l => l.Function == MaterialFunctionAssignment.Finish2);

            if (chosen == null)
            {
                // Fallback：第一個非結構層
                chosen = layers.FirstOrDefault(l => l.Function != MaterialFunctionAssignment.Structure);
                if (chosen != null)
                    warnings.Add($"粉刷類型 '{typeName}' 的 CompoundStructure 找不到 Function=Finish 的層，已用第一個非結構層的材料。");
            }

            if (chosen == null || chosen.MaterialId == ElementId.InvalidElementId) return null;
            return doc.GetElement(chosen.MaterialId) as Material;
        }

        // ═══════════════════════════════════════════════════════════
        // 蒐集全專案「油漆工具」塗色材料
        // ═══════════════════════════════════════════════════════════

        private List<DistinctPaintMaterial> CollectDistinctPaintMaterials(Document doc, List<string> warnings)
        {
            var dict = new Dictionary<string, DistinctPaintMaterial>();

            var targetCategories = new[]
            {
                BuiltInCategory.OST_Walls,
                BuiltInCategory.OST_Floors,
                BuiltInCategory.OST_Ceilings
            };

            var geoOptions = new Options
            {
                ComputeReferences = false,
                IncludeNonVisibleObjects = false,
                DetailLevel = ViewDetailLevel.Fine
            };

            foreach (var cat in targetCategories)
            {
                var elements = new FilteredElementCollector(doc)
                    .OfCategory(cat)
                    .WhereElementIsNotElementType()
                    .ToList();

                foreach (var elem in elements)
                {
                    // 預篩：沒有 Paint 塗層的元素直接略過
                    ICollection<ElementId> paintedIds = null;
                    try
                    {
                        paintedIds = elem.GetMaterialIds(true);
                    }
                    catch
                    {
                        continue;
                    }
                    if (paintedIds == null || paintedIds.Count == 0) continue;

                    GeometryElement geo;
                    try
                    {
                        geo = elem.get_Geometry(geoOptions);
                    }
                    catch (Exception ex)
                    {
                        warnings.Add($"元素 Id={elem.Id.GetIdValue()} 的 Geometry 讀取失敗，已略過：{ex.Message}");
                        continue;
                    }
                    if (geo == null) continue;

                    foreach (var obj in geo)
                    {
                        Solid solid = obj as Solid;
                        if (solid == null || solid.Faces.Size == 0) continue;

                        foreach (Face face in solid.Faces)
                        {
                            ElementId matId = face.MaterialElementId;
                            if (matId == null || matId == ElementId.InvalidElementId) continue;

                            // 只保留真正由 Paint 工具塗上的材質；結構層材質不在 paintedIds 裡
                            if (!paintedIds.Contains(matId)) continue;

                            XYZ normal;
                            try
                            {
                                normal = face.ComputeNormal(new UV(0.5, 0.5));
                            }
                            catch
                            {
                                continue;
                            }

                            string category = ClassifyFaceNormal(normal);
                            string key = $"{category}|{matId.GetIdValue()}";
                            if (dict.ContainsKey(key)) continue;

                            Material mat = doc.GetElement(matId) as Material;
                            if (mat == null) continue;

                            dict[key] = new DistinctPaintMaterial
                            {
                                Category = category,
                                MaterialId = matId,
                                Material = mat,
                                MarkText = ReadMaterialParamOrPlaceholder(mat, BuiltInParameter.ALL_MODEL_MARK),
                                DescriptionText = ReadMaterialParamOrPlaceholder(mat, BuiltInParameter.ALL_MODEL_DESCRIPTION)
                            };
                        }
                    }
                }
            }

            return dict.Values.ToList();
        }

        /// <summary>依面法向量 Z 分量分類：>0.2=Floor、<-0.2=Ceiling、其他=Wall</summary>
        private string ClassifyFaceNormal(XYZ normal)
        {
            if (normal == null) return "Wall";
            if (normal.Z > 0.2) return "Floor";
            if (normal.Z < -0.2) return "Ceiling";
            return "Wall";
        }

        private string ReadMaterialParamOrPlaceholder(Material mat, BuiltInParameter bip)
        {
            try
            {
                var p = mat.get_Parameter(bip);
                if (p == null) return "(未填)";
                string v = p.AsString();
                if (string.IsNullOrWhiteSpace(v)) return "(未填)";
                return v;
            }
            catch
            {
                return "(未填)";
            }
        }

        // ═══════════════════════════════════════════════════════════
        // FilledRegionType 建立 / 複用
        // ═══════════════════════════════════════════════════════════

        private ElementId EnsureFilledRegionTypeForFinish(
            Document doc, DistinctFinishType dft, FilledRegionType template,
            FillPatternElement solidFill, List<string> warnings, out bool reused)
        {
            reused = false;
            string targetName = $"{dft.TypeMark} {dft.TypeName}".Trim();
            if (string.IsNullOrEmpty(targetName)) targetName = "(未命名粉刷)";

            // 已存在則複用
            FilledRegionType existing = new FilteredElementCollector(doc)
                .OfClass(typeof(FilledRegionType))
                .Cast<FilledRegionType>()
                .FirstOrDefault(x => x.Name == targetName);
            if (existing != null)
            {
                reused = true;
                return existing.Id;
            }

            FilledRegionType newType;
            try
            {
                newType = template.Duplicate(targetName) as FilledRegionType;
            }
            catch (Exception ex)
            {
                warnings.Add($"無法建立 FilledRegionType '{targetName}'：{ex.Message}");
                return ElementId.InvalidElementId;
            }
            if (newType == null) return ElementId.InvalidElementId;

            bool colorOnly;
            ApplyMaterialPatternToFilledRegionType(newType, dft.FinishMaterial, solidFill, warnings, out colorOnly);
            dft.ColorOnlyFallback = colorOnly;
            return newType.Id;
        }

        private ElementId EnsureFilledRegionTypeForPaint(
            Document doc, DistinctPaintMaterial dpm, FilledRegionType template,
            FillPatternElement solidFill, List<string> warnings, out bool reused)
        {
            reused = false;
            string materialName = dpm.Material?.Name ?? "(未命名材料)";
            string targetName = $"Paint {materialName}".Trim();

            FilledRegionType existing = new FilteredElementCollector(doc)
                .OfClass(typeof(FilledRegionType))
                .Cast<FilledRegionType>()
                .FirstOrDefault(x => x.Name == targetName);
            if (existing != null)
            {
                reused = true;
                return existing.Id;
            }

            FilledRegionType newType;
            try
            {
                newType = template.Duplicate(targetName) as FilledRegionType;
            }
            catch (Exception ex)
            {
                warnings.Add($"無法建立 FilledRegionType '{targetName}'：{ex.Message}");
                return ElementId.InvalidElementId;
            }
            if (newType == null) return ElementId.InvalidElementId;

            bool colorOnly;
            ApplyMaterialPatternToFilledRegionType(newType, dpm.Material, solidFill, warnings, out colorOnly);
            dpm.ColorOnlyFallback = colorOnly;
            return newType.Id;
        }

        private void ApplyMaterialPatternToFilledRegionType(
            FilledRegionType frt, Material mat, FillPatternElement solidFill,
            List<string> warnings, out bool colorOnlyFallback)
        {
            colorOnlyFallback = false;

            ElementId fgPatternId = ElementId.InvalidElementId;
            ElementId bgPatternId = ElementId.InvalidElementId;
            Color fgColor = new Color(0, 0, 0);
            Color bgColor = new Color(255, 255, 255);

            if (mat != null)
            {
                fgPatternId = mat.SurfaceForegroundPatternId;
                bgPatternId = mat.SurfaceBackgroundPatternId;
                if (mat.SurfaceForegroundPatternColor != null)
                    fgColor = mat.SurfaceForegroundPatternColor;
                if (mat.SurfaceBackgroundPatternColor != null)
                    bgColor = mat.SurfaceBackgroundPatternColor;
            }

            // 前景樣式為空：以 Solid Fill + 白色替代
            if (fgPatternId == ElementId.InvalidElementId)
            {
                colorOnlyFallback = true;
                if (solidFill != null) fgPatternId = solidFill.Id;
                fgColor = new Color(255, 255, 255); // 統一用白色
                warnings.Add($"材料 '{mat?.Name ?? "(無材料)"}' 沒有 SurfaceForegroundPattern，已用 Solid Fill + 白色。");
            }

            try
            {
                if (fgPatternId != ElementId.InvalidElementId)
                    frt.ForegroundPatternId = fgPatternId;
                frt.ForegroundPatternColor = fgColor;
                if (bgPatternId != ElementId.InvalidElementId)
                {
                    frt.BackgroundPatternId = bgPatternId;
                    frt.BackgroundPatternColor = bgColor;
                }
            }
            catch (Exception ex)
            {
                warnings.Add($"FilledRegionType '{frt.Name}' 設定樣式失敗：{ex.Message}");
            }
        }

        // ═══════════════════════════════════════════════════════════
        // Legend 視圖 / TextNoteType 處理
        // ═══════════════════════════════════════════════════════════

        private View FindLegendTemplate(Document doc, string preferredName)
        {
            var legends = new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(v => !v.IsTemplate && v.ViewType == ViewType.Legend)
                .ToList();

            if (legends.Count == 0) return null;

            if (!string.IsNullOrEmpty(preferredName))
            {
                var named = legends.FirstOrDefault(v => v.Name == preferredName);
                if (named != null) return named;
            }
            return legends.First();
        }

        private string EnsureUniqueLegendName(Document doc, string baseName)
        {
            var existing = new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Select(v => v.Name)
                .ToHashSet();

            if (!existing.Contains(baseName)) return baseName;
            return $"{baseName}_{DateTime.Now:HHmmss}";
        }

        private ElementId EnsureLegendTextNoteType(Document doc)
        {
            TextNoteType type = new FilteredElementCollector(doc)
                .OfClass(typeof(TextNoteType))
                .Cast<TextNoteType>()
                .FirstOrDefault(t => t.Name == LEGEND_TEXT_TYPE_NAME);

            if (type == null)
            {
                TextNoteType template = new FilteredElementCollector(doc)
                    .OfClass(typeof(TextNoteType))
                    .Cast<TextNoteType>()
                    .FirstOrDefault();
                if (template == null)
                    throw new InvalidOperationException("專案中找不到任何 TextNoteType 可作為模板。");

                type = template.Duplicate(LEGEND_TEXT_TYPE_NAME) as TextNoteType;
                if (type == null) return template.Id;
            }

            // [Debug] 列出所有 Double 型別的 TextNoteType 參數名稱與數值，方便確認「引線/圖框偏移」的正確名稱
            var _dbgParams = new System.Text.StringBuilder("[FinishLegend] TextNoteType Double params: ");
            foreach (Parameter p in type.Parameters)
            {
                if (p?.Definition?.Name != null && p.StorageType == StorageType.Double)
                    _dbgParams.Append($"[{p.Definition.Name}={p.AsDouble():F5}] ");
            }
            Logger.Info(_dbgParams.ToString());

            try
            {
                type.get_Parameter(BuiltInParameter.TEXT_FONT)?.Set(LEGEND_TEXT_FONT);
                type.get_Parameter(BuiltInParameter.TEXT_SIZE)?.Set(LEGEND_TEXT_HEIGHT_MM / 304.8); // mm → ft
                type.get_Parameter(BuiltInParameter.TEXT_WIDTH_SCALE)?.Set(LEGEND_TEXT_WIDTH_FACTOR);
                type.get_Parameter(BuiltInParameter.TEXT_BOX_VISIBILITY)?.Set(0); // 不顯示邊框
                type.get_Parameter(BuiltInParameter.LINE_COLOR)?.Set(0); // 黑色
                type.get_Parameter(BuiltInParameter.LINE_PEN)?.Set(1);   // 線粗 1

                // 引線/圖框偏移 → 0：LEADER_OFFSET_SHEET BIP 對 TextNoteType 回傳 null，
                // 與 Background 同樣需要用 Definition.Name 多語言 fallback
                FindParameterByAnyName(type, "Leader/Border Offset", "Leader Offset", "引線/圖框偏移", "Border Offset")?.Set(0.0);

                // 背景 → 1 (Transparent)：Revit API 對 TextNoteType 沒有 Background 的 BuiltInParameter，
                // 只能用 Definition.Name 多語言 fallback；值語意是 1=透明 / 0=不透明（與直覺相反）
                FindParameterByAnyName(type, "Background", "背景")?.Set(1);
            }
            catch (Exception ex)
            {
                Logger.Info($"[FinishLegend] TextNoteType '{LEGEND_TEXT_TYPE_NAME}' 部分參數設定失敗：{ex.Message}");
            }

            return type.Id;
        }

        private static Parameter FindParameterByAnyName(Element elem, params string[] names)
        {
            foreach (Parameter p in elem.Parameters)
            {
                var defName = p.Definition?.Name;
                if (string.IsNullOrEmpty(defName)) continue;
                foreach (var n in names)
                {
                    if (defName == n) return p;
                }
            }
            return null;
        }

        // ═══════════════════════════════════════════════════════════
        // 表格繪製
        // ═══════════════════════════════════════════════════════════

        /// <summary>
        /// 在 Legend 中繪製單一類別的表格，回傳該表格底部 Y 座標（cm）。
        /// 若 rows 為空則不繪製，回傳原 topY。
        /// 混合粉刷 + 油漆時，會在第一個油漆列之前插入「── 油漆材料 ──」分隔列。
        /// </summary>
        private double DrawCategoryTable(Document doc, View view, string title,
            List<LegendRow> rows, double topY_cm, ElementId textTypeId)
        {
            if (rows.Count == 0) return topY_cm;

            // 判定是否需要分隔列（同表內同時有粉刷與油漆）
            bool hasFinish = rows.Any(r => !r.IsPaintRow);
            bool hasPaint = rows.Any(r => r.IsPaintRow);
            int dividerIndex = -1;   // 在視覺行索引 (0-based) 中，哪一格是分隔列
            if (hasFinish && hasPaint)
            {
                for (int i = 0; i < rows.Count; i++)
                {
                    if (rows[i].IsPaintRow) { dividerIndex = i; break; }
                }
            }

            int visualDataRows = rows.Count + (dividerIndex >= 0 ? 1 : 0);
            int totalRows = 2 + visualDataRows;  // title + header + 視覺資料列
            double tableHeight = totalRows * LEGEND_ROW_HEIGHT_CM;
            double tableWidth = LEGEND_COL_MARK_CM + LEGEND_COL_SWATCH_CM + LEGEND_COL_NAME_CM;

            double leftX = 0;
            double rightX = tableWidth;
            double col1RightX = LEGEND_COL_MARK_CM;
            double col2RightX = LEGEND_COL_MARK_CM + LEGEND_COL_SWATCH_CM;

            double titleTop = topY_cm;
            double titleBottom = topY_cm - LEGEND_ROW_HEIGHT_CM;
            double headerBottom = titleBottom - LEGEND_ROW_HEIGHT_CM;
            double tableBottom = topY_cm - tableHeight;

            // 外框
            DrawDetailLine(doc, view, leftX, titleTop, rightX, titleTop);
            DrawDetailLine(doc, view, leftX, tableBottom, rightX, tableBottom);
            DrawDetailLine(doc, view, leftX, titleTop, leftX, tableBottom);
            DrawDetailLine(doc, view, rightX, titleTop, rightX, tableBottom);

            // 標題列底邊
            DrawDetailLine(doc, view, leftX, titleBottom, rightX, titleBottom);

            // 表頭與各資料列的水平分隔線
            for (int i = 0; i < visualDataRows + 1; i++)
            {
                double y = headerBottom - i * LEGEND_ROW_HEIGHT_CM;
                DrawDetailLine(doc, view, leftX, y, rightX, y);
            }

            // 垂直欄位分隔線（在分隔列位置要斷開）
            if (dividerIndex < 0)
            {
                DrawDetailLine(doc, view, col1RightX, titleBottom, col1RightX, tableBottom);
                DrawDetailLine(doc, view, col2RightX, titleBottom, col2RightX, tableBottom);
            }
            else
            {
                double divTop = headerBottom - dividerIndex * LEGEND_ROW_HEIGHT_CM;
                double divBottom = divTop - LEGEND_ROW_HEIGHT_CM;
                DrawDetailLine(doc, view, col1RightX, titleBottom, col1RightX, divTop);
                DrawDetailLine(doc, view, col2RightX, titleBottom, col2RightX, divTop);
                DrawDetailLine(doc, view, col1RightX, divBottom, col1RightX, tableBottom);
                DrawDetailLine(doc, view, col2RightX, divBottom, col2RightX, tableBottom);
            }

            // 標題文字（跨 3 欄置中）
            DrawCenteredText(doc, view, textTypeId,
                (leftX + rightX) / 2, (titleTop + titleBottom) / 2, title, LEGEND_TEXT_HEIGHT_HALF_CM_ZH);

            // 表頭文字
            DrawCenteredText(doc, view, textTypeId,
                (leftX + col1RightX) / 2, (titleBottom + headerBottom) / 2, "編號", LEGEND_TEXT_HEIGHT_HALF_CM_ZH);
            DrawCenteredText(doc, view, textTypeId,
                (col1RightX + col2RightX) / 2, (titleBottom + headerBottom) / 2, "圖例", LEGEND_TEXT_HEIGHT_HALF_CM_ZH);
            DrawCenteredText(doc, view, textTypeId,
                (col2RightX + rightX) / 2, (titleBottom + headerBottom) / 2, "說明", LEGEND_TEXT_HEIGHT_HALF_CM_ZH);

            // 資料列（含分隔列）
            int dataCursor = 0;
            for (int vi = 0; vi < visualDataRows; vi++)
            {
                double rowTop = headerBottom - vi * LEGEND_ROW_HEIGHT_CM;
                double rowBottom = rowTop - LEGEND_ROW_HEIGHT_CM;
                double rowMidY = (rowTop + rowBottom) / 2;

                if (vi == dividerIndex)
                {
                    // 分隔列：跨 3 欄置中文字
                    DrawCenteredText(doc, view, textTypeId,
                        (leftX + rightX) / 2, rowMidY, "── 油漆材料 ──", LEGEND_TEXT_HEIGHT_HALF_CM_ZH);
                    continue;
                }

                var row = rows[dataCursor];
                dataCursor++;

                // 第 1 欄：編號（類型標記），置中，保持 halfY=20
                DrawCenteredText(doc, view, textTypeId,
                    (leftX + col1RightX) / 2, rowMidY, row.MarkText);

                // 第 2 欄：FilledRegion 填滿整格
                if (row.FilledRegionTypeId != ElementId.InvalidElementId)
                {
                    try
                    {
                        CreateLegendFilledRegion(doc, view, row.FilledRegionTypeId,
                            col1RightX, rowBottom, col2RightX, rowTop);
                    }
                    catch (Exception ex)
                    {
                        Logger.Info($"[FinishLegend] FilledRegion 建立失敗 ({row.MarkText} {row.NameText})：{ex.Message}");
                    }
                }

                // 第 3 欄：說明，靠左對齊，左側留 10cm padding
                DrawLeftAlignedText(doc, view, textTypeId, col2RightX + 10, rowMidY, row.NameText, LEGEND_TEXT_HEIGHT_HALF_CM_ZH);
            }

            return tableBottom;
        }

        private void DrawDetailLine(Document doc, View view,
            double x1_cm, double y1_cm, double x2_cm, double y2_cm)
        {
            XYZ p1 = new XYZ(x1_cm * CM_TO_FT, y1_cm * CM_TO_FT, 0);
            XYZ p2 = new XYZ(x2_cm * CM_TO_FT, y2_cm * CM_TO_FT, 0);
            if (p1.DistanceTo(p2) < 1e-6) return;
            doc.Create.NewDetailCurve(view, Line.CreateBound(p1, p2));
        }

        private void DrawCenteredText(Document doc, View view, ElementId textTypeId,
            double centerX_cm, double centerY_cm, string text,
            double halfY = LEGEND_TEXT_HEIGHT_HALF_CM)
        {
            if (string.IsNullOrEmpty(text)) return;
            double x = centerX_cm * CM_TO_FT;
            double y = (centerY_cm + halfY) * CM_TO_FT;
            var options = new TextNoteOptions
            {
                TypeId = textTypeId,
                HorizontalAlignment = HorizontalTextAlignment.Center
            };
            TextNote.Create(doc, view.Id, new XYZ(x, y, 0), text, options);
        }

        private void DrawLeftAlignedText(Document doc, View view, ElementId textTypeId,
            double leftX_cm, double centerY_cm, string text,
            double halfY = LEGEND_TEXT_HEIGHT_HALF_CM)
        {
            if (string.IsNullOrEmpty(text)) return;
            double x = leftX_cm * CM_TO_FT;
            double y = (centerY_cm + halfY) * CM_TO_FT;
            var options = new TextNoteOptions
            {
                TypeId = textTypeId,
                HorizontalAlignment = HorizontalTextAlignment.Left
            };
            TextNote.Create(doc, view.Id, new XYZ(x, y, 0), text, options);
        }

        private void CreateLegendFilledRegion(Document doc, View view, ElementId frtId,
            double leftX_cm, double bottomY_cm, double rightX_cm, double topY_cm)
        {
            XYZ p1 = new XYZ(leftX_cm * CM_TO_FT, bottomY_cm * CM_TO_FT, 0);
            XYZ p2 = new XYZ(rightX_cm * CM_TO_FT, bottomY_cm * CM_TO_FT, 0);
            XYZ p3 = new XYZ(rightX_cm * CM_TO_FT, topY_cm * CM_TO_FT, 0);
            XYZ p4 = new XYZ(leftX_cm * CM_TO_FT, topY_cm * CM_TO_FT, 0);

            var loop = new CurveLoop();
            loop.Append(Line.CreateBound(p1, p2));
            loop.Append(Line.CreateBound(p2, p3));
            loop.Append(Line.CreateBound(p3, p4));
            loop.Append(Line.CreateBound(p4, p1));

            FilledRegion.Create(doc, frtId, view.Id, new List<CurveLoop> { loop });
        }
    }
}
