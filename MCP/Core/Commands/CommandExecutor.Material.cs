using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Visual;
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
        #region 材質批次修改

        /// <summary>
        /// 查詢指定類別中所有元素類型及其目前材質資訊
        /// </summary>
        private object GetTypesByCategory(JObject parameters)
        {
            string categoryName = parameters["category"]?.Value<string>();
            bool excludeCurtainWalls = parameters["excludeCurtainWalls"]?.Value<bool>() ?? true;

            if (string.IsNullOrEmpty(categoryName))
                throw new Exception("必須提供 category 參數");

            Document doc = _uiApp.ActiveUIDocument.Document;
            string catLower = categoryName.ToLowerInvariant();

            var typeInfos = new List<object>();

            if (catLower == "walls" || catLower == "牆")
            {
                var wallTypes = new FilteredElementCollector(doc)
                    .OfClass(typeof(WallType))
                    .Cast<WallType>();

                // 計算每個 type 的 instance 數量
                var allWalls = new FilteredElementCollector(doc)
                    .OfClass(typeof(Wall))
                    .WhereElementIsNotElementType()
                    .Cast<Wall>()
                    .ToList();

                foreach (var wt in wallTypes)
                {
                    if (excludeCurtainWalls && wt.Kind == WallKind.Curtain)
                        continue;

                    var materials = GetCompoundStructureMaterials(doc, wt);
                    int instanceCount = allWalls.Count(w => w.WallType.Id == wt.Id);

                    typeInfos.Add(new
                    {
                        TypeId = wt.Id.GetIdValue(),
                        TypeName = wt.Name,
                        FamilyName = wt.FamilyName,
                        InstanceCount = instanceCount,
                        WallKind = wt.Kind.ToString(),
                        Materials = materials
                    });
                }
            }
            else if (catLower == "floors" || catLower == "樓板")
            {
                var floorTypes = new FilteredElementCollector(doc)
                    .OfClass(typeof(FloorType))
                    .Cast<FloorType>();

                var allFloors = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_Floors)
                    .WhereElementIsNotElementType()
                    .ToList();

                foreach (var ft in floorTypes)
                {
                    var materials = GetCompoundStructureMaterials(doc, ft);
                    int instanceCount = allFloors.Count(f => f.GetTypeId() == ft.Id);

                    typeInfos.Add(new
                    {
                        TypeId = ft.Id.GetIdValue(),
                        TypeName = ft.Name,
                        FamilyName = ft.FamilyName,
                        InstanceCount = instanceCount,
                        Materials = materials
                    });
                }
            }
            else if (catLower == "ceilings" || catLower == "天花板")
            {
                var ceilingTypes = new FilteredElementCollector(doc)
                    .OfClass(typeof(CeilingType))
                    .Cast<CeilingType>();

                var allCeilings = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_Ceilings)
                    .WhereElementIsNotElementType()
                    .ToList();

                foreach (var ct in ceilingTypes)
                {
                    var materials = GetCompoundStructureMaterials(doc, ct);
                    int instanceCount = allCeilings.Count(c => c.GetTypeId() == ct.Id);

                    typeInfos.Add(new
                    {
                        TypeId = ct.Id.GetIdValue(),
                        TypeName = ct.Name,
                        FamilyName = ct.FamilyName,
                        InstanceCount = instanceCount,
                        // 注意：天花板可見面是 Bottom（Layer N-1），會被 batch_set_material 修改
                        VisibleLayer = "Bottom (Layer N-1)",
                        Materials = materials
                    });
                }
            }
            else if (catLower == "columns" || catLower == "柱")
            {
                CollectFamilySymbolTypes(doc,
                    new[] { BuiltInCategory.OST_Columns, BuiltInCategory.OST_StructuralColumns },
                    typeInfos);
            }
            else if (catLower == "structuralframing" || catLower == "梁"
                     || catLower == "structural framing")
            {
                CollectFamilySymbolTypes(doc,
                    new[] { BuiltInCategory.OST_StructuralFraming },
                    typeInfos);
            }
            else if (catLower == "mullions" || catLower == "豎框"
                     || catLower == "curtainwallmullions"
                     || catLower == "curtain wall mullions")
            {
                // 帷幕牆豎框：MullionType，材質在 MATERIAL_ID_PARAM
                var mullionTypes = new FilteredElementCollector(doc)
                    .OfClass(typeof(MullionType))
                    .Cast<MullionType>();

                var allMullions = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_CurtainWallMullions)
                    .WhereElementIsNotElementType()
                    .ToList();

                foreach (var mt in mullionTypes)
                {
                    // 讀取目前材質
                    string matName = "<none>";
                    Parameter matParam = mt.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM);
                    if (matParam != null && matParam.HasValue)
                    {
                        Material mat = doc.GetElement(matParam.AsElementId()) as Material;
                        if (mat != null) matName = mat.Name;
                    }

                    int instanceCount = allMullions.Count(m => m.GetTypeId() == mt.Id);

                    typeInfos.Add(new
                    {
                        TypeId = mt.Id.GetIdValue(),
                        TypeName = mt.Name,
                        FamilyName = mt.FamilyName,
                        InstanceCount = instanceCount,
                        Materials = new List<string> { matName }
                    });
                }
            }
            else
            {
                throw new Exception($"不支援的類別: {categoryName}。支援: Walls, Floors, Ceilings, Columns, StructuralFraming, Mullions");
            }

            return new
            {
                Success = true,
                Category = categoryName,
                TypeCount = typeInfos.Count,
                Types = typeInfos
            };
        }

        /// <summary>
        /// 批次修改指定類型的材質（「複製原材質」模式）。
        /// 為每個 type 的原材質建立複本（命名 "{原名}_{suffix}"），
        /// 只修改複本的 Appearance Asset，保留 Graphics 顏色與原材質其他屬性。
        /// 牆/樓板只修改 CompoundStructure 最外層（Layer 0）。
        /// </summary>
        private object BatchSetMaterial(JObject parameters)
        {
            var typeIdsArray = parameters["typeIds"] as JArray;
            var colorObj = parameters["color"];
            string suffix = parameters["materialName"]?.Value<string>() ?? "White_MCP";

            // roughness 參數：0-1（選填）。值 > 1 會當作 0-100 百分比處理（除以 100）
            double? roughness = null;
            var roughnessToken = parameters["roughness"];
            if (roughnessToken != null && roughnessToken.Type != JTokenType.Null)
            {
                double val = roughnessToken.Value<double>();
                if (val > 1.0) val = val / 100.0;
                if (val < 0) val = 0;
                if (val > 1) val = 1;
                roughness = val;
            }

            if (typeIdsArray == null || typeIdsArray.Count == 0)
                throw new Exception("必須提供至少一個 typeId");

            if (colorObj == null)
                throw new Exception("必須提供 color 參數 (RGB)");

            byte r = (byte)colorObj["r"].Value<int>();
            byte g = (byte)colorObj["g"].Value<int>();
            byte b = (byte)colorObj["b"].Value<int>();
            Color targetColor = new Color(r, g, b);

            Document doc = _uiApp.ActiveUIDocument.Document;
            string suffixTag = "_" + suffix;

            // === Phase 1：掃描各 type 的所有可見面 slot（無 transaction）===
            // 牆會產生兩個 slot（Layer 0 外側 + Layer N-1 內側），其他類型一個 slot。
            // 分類：
            //   - planPerOrigMat: 目前材質不是複本，需要複製並重新指派
            //   - existingDuplicates: 目前材質已是複本（結尾含 suffix），直接更新該材質的 appearance
            var planPerOrigMat = new Dictionary<long, List<(IdType TypeId, Element TypeElem, int LayerIndex, string SlotKind)>>();
            var existingDuplicates = new Dictionary<long, List<IdType>>(); // dupMatId → typeIds
            var errors = new List<string>();

            foreach (var idToken in typeIdsArray)
            {
                IdType typeId = idToken.Value<IdType>();
                Element typeElem = doc.GetElement(new ElementId(typeId));

                if (typeElem == null)
                {
                    errors.Add($"找不到 Type ID: {typeId}");
                    continue;
                }

                // 取得該 type 所有可見面 slot
                var slots = GetMaterialSlots(typeElem);
                if (slots.Count == 0)
                {
                    errors.Add($"Type ID {typeId} ({typeElem.Name}) 不支援的類型或無可見面 slot");
                    continue;
                }

                foreach (var (layerIndex, slotKind) in slots)
                {
                    ElementId curMatId = GetSlotMaterialId(typeElem, layerIndex);

                    if (curMatId == ElementId.InvalidElementId)
                    {
                        errors.Add($"Type ID {typeId} ({typeElem.Name}) slot {slotKind} 無材質");
                        continue;
                    }

                    Material curMat = doc.GetElement(curMatId) as Material;
                    if (curMat == null)
                    {
                        errors.Add($"Type ID {typeId} ({typeElem.Name}) slot {slotKind} 材質 ID {curMatId.GetIdValue()} 找不到");
                        continue;
                    }

                    long key = curMatId.GetIdValue();

                    // 若目前材質名已含 suffix → 直接更新該複本材質（不重複複製、不重新指派）
                    if (curMat.Name.EndsWith(suffixTag, StringComparison.Ordinal))
                    {
                        if (!existingDuplicates.ContainsKey(key))
                            existingDuplicates[key] = new List<IdType>();
                        if (!existingDuplicates[key].Contains(typeId))
                            existingDuplicates[key].Add(typeId);
                        continue;
                    }

                    // 否則需要複製原材質並指派到該 slot
                    if (!planPerOrigMat.ContainsKey(key))
                        planPerOrigMat[key] = new List<(IdType, Element, int, string)>();
                    planPerOrigMat[key].Add((typeId, typeElem, layerIndex, slotKind));
                }
            }

            // === Phase 2a：建立/找到複本材質（Transaction）===
            // origMatId → duplicateMatId
            var duplicateMap = new Dictionary<long, ElementId>();
            using (Transaction t2a = TransactionHelper.Begin(doc, "複製原材質"))
            {
                t2a.Start();
                foreach (var kvp in planPerOrigMat)
                {
                    Material origMat = doc.GetElement(new ElementId((IdType)kvp.Key)) as Material;
                    if (origMat == null) continue;

                    Material dup = FindOrCreateDuplicateMaterial(doc, origMat, suffix);
                    duplicateMap[kvp.Key] = dup.Id;
                }
                t2a.Commit();
            }

            // === Phase 2b：為所有目標材質（新複本 + 既有複本）更新 Appearance Asset ===
            var appearanceWarnings = new List<string>();
            var allTargetMatIds = new HashSet<long>();
            foreach (var id in duplicateMap.Values) allTargetMatIds.Add(id.GetIdValue());
            foreach (var id in existingDuplicates.Keys) allTargetMatIds.Add(id);

            foreach (long matIdValue in allTargetMatIds)
            {
                Material dup = doc.GetElement(new ElementId((IdType)matIdValue)) as Material;
                if (dup == null) continue;
                try
                {
                    UpdateAppearanceAsset(doc, dup, targetColor, roughness);
                }
                catch (Exception ex)
                {
                    string msg = $"'{dup.Name}' 的 Appearance 更新失敗：{ex.Message}";
                    appearanceWarnings.Add(msg);
                    Logger.Error($"Appearance Asset 更新失敗 material={dup.Name}", ex);
                }
            }

            // === Phase 3：把新複本套用回各 type（Transaction）===
            var duplicatedMaterialsReport = new List<object>();
            int successCount = 0;

            using (Transaction t3 = TransactionHelper.Begin(doc, "套用複本材質"))
            {
                t3.Start();
                foreach (var kvp in planPerOrigMat)
                {
                    long origMatIdValue = kvp.Key;
                    if (!duplicateMap.TryGetValue(origMatIdValue, out ElementId dupMatId))
                        continue;

                    Material origMat = doc.GetElement(new ElementId((IdType)origMatIdValue)) as Material;
                    Material dupMat = doc.GetElement(dupMatId) as Material;

                    var typesUsingIt = new List<long>();
                    foreach (var (typeId, typeElem, layerIndex, slotKind) in kvp.Value)
                    {
                        try
                        {
                            SetSlotMaterialId(typeElem, layerIndex, dupMatId);
                            if (!typesUsingIt.Contains(typeId))
                                typesUsingIt.Add(typeId);
                            successCount++;
                        }
                        catch (Exception ex)
                        {
                            errors.Add($"Type ID {typeId} ({typeElem.Name}) slot {slotKind} 套用複本失敗：{ex.Message}");
                        }
                    }

                    duplicatedMaterialsReport.Add(new
                    {
                        Action = "Duplicated",
                        OriginalName = origMat?.Name,
                        OriginalId = origMatIdValue,
                        DuplicateName = dupMat?.Name,
                        DuplicateId = dupMatId.GetIdValue(),
                        TypesUsingIt = typesUsingIt
                    });
                }
                t3.Commit();
            }

            // 既有複本：不需重新指派，直接記入報告
            foreach (var kvp in existingDuplicates)
            {
                Material dupMat = doc.GetElement(new ElementId((IdType)kvp.Key)) as Material;
                duplicatedMaterialsReport.Add(new
                {
                    Action = "UpdatedExisting",
                    OriginalName = (string)null,
                    OriginalId = 0L,
                    DuplicateName = dupMat?.Name,
                    DuplicateId = kvp.Key,
                    TypesUsingIt = kvp.Value.Select(t => (long)t).ToList()
                });
                successCount += kvp.Value.Count;
            }

            int newDuplicatesCount = duplicateMap.Count;
            int existingUpdatedCount = existingDuplicates.Count;

            return new
            {
                Success = true,
                Suffix = suffix,
                Color = $"#{r:X2}{g:X2}{b:X2}",
                Roughness = roughness,
                ModifiedCount = successCount,
                RequestedCount = typeIdsArray.Count,
                NewDuplicatesCreated = newDuplicatesCount,
                ExistingDuplicatesUpdated = existingUpdatedCount,
                ErrorCount = errors.Count,
                Errors = errors,
                AppearanceWarnings = appearanceWarnings,
                DuplicatedMaterials = duplicatedMaterialsReport,
                Message = $"已處理 {successCount} 個類型"
                    + (newDuplicatesCount > 0 ? $"；建立 {newDuplicatesCount} 個新複本" : "")
                    + (existingUpdatedCount > 0 ? $"；更新 {existingUpdatedCount} 個既有複本" : "")
                    + $" (suffix: {suffix}, #{r:X2}{g:X2}{b:X2}"
                    + (roughness.HasValue ? $", roughness={roughness.Value:F2}" : "")
                    + ")"
                    + (appearanceWarnings.Count > 0 ? $"；{appearanceWarnings.Count} 個 Appearance 警告" : "")
            };
        }

        /// <summary>
        /// 取得 type 的可見面 slot 清單。每個 slot 用 (LayerIndex, SlotKind) 表示。
        /// LayerIndex -1 表示不是 CompoundStructure layer（用 parameter 代替）。
        ///   - Wall：Layer 0（外側）+ Layer N-1（內側）兩個 slot（若 LayerCount > 1）
        ///   - Floor：Layer 0（頂面）
        ///   - Ceiling：Layer N-1（底面，從房間抬頭看到）
        ///   - FamilySymbol（柱/梁）：STRUCTURAL_MATERIAL_PARAM
        ///   - MullionType：MATERIAL_ID_PARAM
        /// </summary>
        private List<(int LayerIndex, string SlotKind)> GetMaterialSlots(Element typeElem)
        {
            var slots = new List<(int, string)>();

            if (typeElem is WallType wt)
            {
                CompoundStructure cs = wt.GetCompoundStructure();
                if (cs != null && cs.LayerCount > 0)
                {
                    slots.Add((0, "Wall.Exterior(Layer0)"));
                    if (cs.LayerCount > 1)
                        slots.Add((cs.LayerCount - 1, "Wall.Interior(LayerLast)"));
                }
                else
                {
                    slots.Add((-1, "Wall.ParamFallback"));
                }
            }
            else if (typeElem is FloorType ft)
            {
                CompoundStructure cs = ft.GetCompoundStructure();
                if (cs != null && cs.LayerCount > 0)
                    slots.Add((0, "Floor.Top(Layer0)"));
                else
                    slots.Add((-1, "Floor.ParamFallback"));
            }
            else if (typeElem is CeilingType ct)
            {
                CompoundStructure cs = ct.GetCompoundStructure();
                if (cs != null && cs.LayerCount > 0)
                    slots.Add((cs.LayerCount - 1, "Ceiling.Bottom(LayerLast)"));
                else
                    slots.Add((-1, "Ceiling.ParamFallback"));
            }
            else if (typeElem is MullionType)
            {
                slots.Add((-1, "Mullion.Material"));
            }
            else if (typeElem is FamilySymbol)
            {
                slots.Add((-1, "StructuralMaterial"));
            }

            return slots;
        }

        /// <summary>
        /// 讀取指定 slot 的當前材質 ID。LayerIndex -1 表示走 parameter 路徑。
        /// </summary>
        private ElementId GetSlotMaterialId(Element typeElem, int layerIndex)
        {
            if (layerIndex >= 0)
            {
                CompoundStructure cs = GetCompoundStructureForElement(typeElem);
                if (cs != null && layerIndex < cs.LayerCount)
                    return cs.GetMaterialId(layerIndex);
                return ElementId.InvalidElementId;
            }

            // Parameter 路徑
            if (typeElem is FamilySymbol fs)
            {
                Parameter mp = fs.get_Parameter(BuiltInParameter.STRUCTURAL_MATERIAL_PARAM);
                if (mp == null || !mp.HasValue)
                    mp = fs.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM);
                return mp?.AsElementId() ?? ElementId.InvalidElementId;
            }
            if (typeElem is ElementType et)
            {
                Parameter mp = et.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM);
                return mp?.AsElementId() ?? ElementId.InvalidElementId;
            }
            return ElementId.InvalidElementId;
        }

        /// <summary>
        /// 設定指定 slot 的材質 ID。LayerIndex -1 表示走 parameter 路徑。
        /// </summary>
        private void SetSlotMaterialId(Element typeElem, int layerIndex, ElementId newMatId)
        {
            if (layerIndex >= 0)
            {
                CompoundStructure cs = GetCompoundStructureForElement(typeElem);
                if (cs == null || layerIndex >= cs.LayerCount)
                    throw new Exception($"Layer {layerIndex} 超出 CompoundStructure 範圍");
                cs.SetMaterialId(layerIndex, newMatId);
                SetCompoundStructureForElement(typeElem, cs);
                return;
            }

            // Parameter 路徑
            if (typeElem is FamilySymbol fs)
            {
                Parameter mp = fs.get_Parameter(BuiltInParameter.STRUCTURAL_MATERIAL_PARAM);
                if (mp == null || mp.IsReadOnly)
                    mp = fs.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM);
                if (mp != null && !mp.IsReadOnly) { mp.Set(newMatId); return; }
                throw new Exception("FamilySymbol 材質參數唯讀或不存在");
            }
            if (typeElem is MullionType)
            {
                Parameter mp = (typeElem as ElementType).get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM);
                if (mp != null && !mp.IsReadOnly) { mp.Set(newMatId); return; }
                throw new Exception("Mullion 材質參數唯讀或不存在");
            }
            if (typeElem is ElementType et)
            {
                Parameter mp = et.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM);
                if (mp != null && !mp.IsReadOnly) { mp.Set(newMatId); return; }
                throw new Exception("ElementType 材質參數唯讀或不存在");
            }
            throw new Exception($"不支援的 type 類型: {typeElem.GetType().Name}");
        }

        private CompoundStructure GetCompoundStructureForElement(Element typeElem)
        {
            if (typeElem is WallType wt) return wt.GetCompoundStructure();
            if (typeElem is FloorType ft) return ft.GetCompoundStructure();
            if (typeElem is CeilingType ct) return ct.GetCompoundStructure();
            return null;
        }

        private void SetCompoundStructureForElement(Element typeElem, CompoundStructure cs)
        {
            if (typeElem is WallType wt) { wt.SetCompoundStructure(cs); return; }
            if (typeElem is FloorType ft) { ft.SetCompoundStructure(cs); return; }
            if (typeElem is CeilingType ct) { ct.SetCompoundStructure(cs); return; }
        }

        // (舊的 SetOuterMaterialId 已刪除，被 slot-based 的 SetSlotMaterialId 取代)

        /// <summary>
        /// 找到或建立原材質的複本（命名 "{原名}_{suffix}"）。
        /// Material.Duplicate 會複製 Graphics/Appearance/所有參數。
        /// </summary>
        private Material FindOrCreateDuplicateMaterial(Document doc, Material original, string suffix)
        {
            string newName = $"{original.Name}_{suffix}";

            Material existing = new FilteredElementCollector(doc)
                .OfClass(typeof(Material)).Cast<Material>()
                .FirstOrDefault(m => m.Name == newName);
            if (existing != null) return existing;

            // Material.Duplicate 直接回傳 Material（非 ElementId）
            return original.Duplicate(newName);
        }

        /// <summary>
        /// 將既有材質（透過名稱查找）套用到指定的 Type。
        /// 不建立新材質。用於復原或批次指派既有材質。
        /// </summary>
        private object AssignExistingMaterial(JObject parameters)
        {
            var typeIdsArray = parameters["typeIds"] as JArray;
            string matName = parameters["materialName"]?.Value<string>();

            if (typeIdsArray == null || typeIdsArray.Count == 0)
                throw new Exception("必須提供至少一個 typeId");

            if (string.IsNullOrEmpty(matName))
                throw new Exception("必須提供 materialName");

            Document doc = _uiApp.ActiveUIDocument.Document;

            // 找既有材質（不建立新的）
            Material mat = new FilteredElementCollector(doc)
                .OfClass(typeof(Material))
                .Cast<Material>()
                .FirstOrDefault(m => m.Name == matName);

            if (mat == null)
                throw new Exception($"找不到名為 '{matName}' 的材質");

            using (Transaction trans = TransactionHelper.Begin(doc, "套用既有材質"))
            {
                trans.Start();

                int successCount = 0;
                var errors = new List<string>();
                var assignedTypes = new List<object>();

                foreach (var idToken in typeIdsArray)
                {
                    IdType typeId = idToken.Value<IdType>();
                    Element typeElem = doc.GetElement(new ElementId(typeId));

                    if (typeElem == null)
                    {
                        errors.Add($"找不到 Type ID: {typeId}");
                        continue;
                    }

                    try
                    {
                        if (typeElem is WallType wt)
                        {
                            // 復原情境：所有 layer 都回到同一材質
                            SetCompoundStructureAllLayers(wt, mat.Id);
                            assignedTypes.Add(new
                            {
                                TypeId = typeId,
                                TypeName = wt.Name,
                                Category = "Walls"
                            });
                            successCount++;
                        }
                        else if (typeElem is FloorType ft)
                        {
                            SetCompoundStructureAllLayers(ft, mat.Id);
                            assignedTypes.Add(new
                            {
                                TypeId = typeId,
                                TypeName = ft.Name,
                                Category = "Floors"
                            });
                            successCount++;
                        }
                        else if (typeElem is CeilingType ct)
                        {
                            SetCompoundStructureAllLayers(ct, mat.Id);
                            assignedTypes.Add(new
                            {
                                TypeId = typeId,
                                TypeName = ct.Name,
                                Category = "Ceilings"
                            });
                            successCount++;
                        }
                        else if (typeElem is MullionType mullionType)
                        {
                            Parameter mullionMatParam = mullionType.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM);
                            if (mullionMatParam != null && !mullionMatParam.IsReadOnly)
                                mullionMatParam.Set(mat.Id);
                            assignedTypes.Add(new
                            {
                                TypeId = typeId,
                                TypeName = mullionType.Name,
                                FamilyName = mullionType.FamilyName,
                                Category = "Mullions"
                            });
                            successCount++;
                            continue;
                        }
                        else if (typeElem is FamilySymbol fs)
                        {
                            SetStructuralMaterial(fs, mat.Id);
                            assignedTypes.Add(new
                            {
                                TypeId = typeId,
                                TypeName = fs.Name,
                                FamilyName = fs.FamilyName,
                                Category = fs.Category?.Name ?? "Unknown"
                            });
                            successCount++;
                        }
                        else
                        {
                            errors.Add($"Type ID {typeId} ({typeElem.Name}) 不是支援的類型");
                        }
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"Type ID {typeId} ({typeElem.Name}): {ex.Message}");
                    }
                }

                trans.Commit();

                return new
                {
                    Success = true,
                    MaterialId = mat.Id.GetIdValue(),
                    MaterialName = matName,
                    AssignedCount = successCount,
                    RequestedCount = typeIdsArray.Count,
                    ErrorCount = errors.Count,
                    Errors = errors,
                    AssignedTypes = assignedTypes,
                    Message = $"已將材質 '{matName}' 套用到 {successCount} 個類型"
                };
            }
        }

        /// <summary>
        /// 把 CompoundStructure 所有層設為同一材質（用於復原）
        /// </summary>
        private void SetCompoundStructureAllLayers(ElementType type, ElementId newMatId)
        {
            CompoundStructure cs = null;
            if (type is WallType wt)
                cs = wt.GetCompoundStructure();
            else if (type is FloorType ft)
                cs = ft.GetCompoundStructure();
            else if (type is CeilingType ct)
                cs = ct.GetCompoundStructure();

            if (cs == null)
            {
                Parameter matParam = type.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM);
                if (matParam != null && !matParam.IsReadOnly)
                    matParam.Set(newMatId);
                return;
            }

            for (int i = 0; i < cs.LayerCount; i++)
                cs.SetMaterialId(i, newMatId);

            if (type is WallType wt2) wt2.SetCompoundStructure(cs);
            else if (type is FloorType ft2) ft2.SetCompoundStructure(cs);
            else if (type is CeilingType ct2) ct2.SetCompoundStructure(cs);
        }

        #endregion

        #region Material Helpers

        /// <summary>
        /// 建立或找到指定名稱與顏色的材質（僅 Graphics 頁籤顏色）。
        /// Appearance Asset 由 UpdateAppearanceAsset 另行處理，必須在此 Transaction 之外。
        /// </summary>
        private Material FindOrCreateMaterial(Document doc, string name, Color color)
        {
            Material mat = new FilteredElementCollector(doc)
                .OfClass(typeof(Material))
                .Cast<Material>()
                .FirstOrDefault(m => m.Name == name);

            if (mat == null)
            {
                ElementId id = Material.Create(doc, name);
                mat = doc.GetElement(id) as Material;
            }

            // Graphics 頁籤顏色（Revit Shaded/Realistic 視圖）
            mat.Color = color;
            mat.Transparency = 0;

            return mat;
        }

        /// <summary>
        /// 確保材質有獨立的 AppearanceAssetElement，並設定其 diffuse color 與（選填）roughness。
        /// 此方法會開自己的 Transaction，呼叫時**必須不在任何 Transaction 內**。
        /// AppearanceAssetEditScope.Commit(true) 需要外層 Transaction 才能運作。
        /// roughness: null = 不動；0.0 = 完全鏡面；1.0 = 完全粗糙/啞光
        /// </summary>
        private void UpdateAppearanceAsset(Document doc, Material mat, Color color, double? roughness = null)
        {
            using (Transaction t = TransactionHelper.Begin(doc, "更新 Appearance Asset"))
            {
                t.Start();

                ElementId assetId = mat.AppearanceAssetId;

                // 情況 A：材質沒有 appearance asset → 複製任一 generic asset
                if (assetId == ElementId.InvalidElementId)
                {
                    var sourceAsset = new FilteredElementCollector(doc)
                        .OfClass(typeof(AppearanceAssetElement))
                        .Cast<AppearanceAssetElement>()
                        .FirstOrDefault();

                    if (sourceAsset != null)
                    {
                        string newName = GenerateUniqueAssetName(doc, mat.Name + "_Appearance");
                        AppearanceAssetElement newAsset = sourceAsset.Duplicate(newName);
                        mat.AppearanceAssetId = newAsset.Id;
                        assetId = newAsset.Id;
                    }
                }
                else
                {
                    // 情況 B：檢查是否與其他材質共用 asset → 複製一份
                    bool shared = new FilteredElementCollector(doc)
                        .OfClass(typeof(Material))
                        .Cast<Material>()
                        .Any(m => m.Id != mat.Id && m.AppearanceAssetId == assetId);

                    if (shared)
                    {
                        AppearanceAssetElement original = doc.GetElement(assetId) as AppearanceAssetElement;
                        if (original != null)
                        {
                            string newName = GenerateUniqueAssetName(doc, mat.Name + "_Appearance");
                            AppearanceAssetElement duplicated = original.Duplicate(newName);
                            mat.AppearanceAssetId = duplicated.Id;
                            assetId = duplicated.Id;
                        }
                    }
                }

                if (assetId == ElementId.InvalidElementId)
                {
                    t.RollBack();
                    return; // 沒有可用 asset
                }

                // 編輯 asset 的 diffuse color（EditScope.Commit 需要外層 Transaction）
                using (AppearanceAssetEditScope editScope = new AppearanceAssetEditScope(doc))
                {
                    Asset editableAsset = editScope.Start(assetId);
                    SetAssetDiffuseColor(editableAsset, color);
                    if (roughness.HasValue)
                        SetAssetRoughness(editableAsset, roughness.Value);
                    editScope.Commit(true);
                }

                t.Commit();
            }
        }

        /// <summary>
        /// 嘗試設定 Asset 的 diffuse/color 屬性。不同 shader 類型有不同屬性名稱，
        /// 這裡採取「全掃描 + 名稱比對」策略以最大化相容性。
        /// </summary>
        private void SetAssetDiffuseColor(Asset asset, Color color)
        {
            // 常見 color 屬性名稱（Generic/Metal/Ceramic/Concrete/Wood/Masonry/Plastic 等）
            string[] candidateNames = new[]
            {
                "generic_diffuse",
                "common_Tint_color",
                "ceramic_color",
                "plastic_color",
                "wood_color",
                "concrete_color_by_object",
                "masonrycmu_color",
                "stone_color_by_object",
                "metal_f0",
            };

            foreach (string propName in candidateNames)
            {
                TrySetColorProperty(asset.FindByName(propName), color);
            }

            // Fallback：掃描所有屬性，比對名稱包含 color/diffuse/tint 的
            for (int i = 0; i < asset.Size; i++)
            {
                AssetProperty prop = asset[i];
                string n = prop.Name?.ToLowerInvariant() ?? "";
                if (n.Contains("color") || n.Contains("diffuse") || n.Contains("tint"))
                {
                    TrySetColorProperty(prop, color);
                }
            }
        }

        private void TrySetColorProperty(AssetProperty prop, Color color)
        {
            if (prop is AssetPropertyDoubleArray4d colorProp)
            {
                try { colorProp.SetValueAsColor(color); }
                catch { /* read-only 或 connected 等情況，忽略 */ }
            }
        }

        /// <summary>
        /// 設定 Asset 的 roughness（0.0=鏡面，1.0=完全粗糙/啞光）。
        /// 不同 shader 用的屬性名稱不同，這裡直接設定 roughness 類，
        /// 並對 glossiness 類設反值（glossiness = 1 - roughness）。
        /// </summary>
        private void SetAssetRoughness(Asset asset, double roughness)
        {
            if (roughness < 0) roughness = 0;
            if (roughness > 1) roughness = 1;
            double glossiness = 1.0 - roughness;

            // 掃描所有屬性
            for (int i = 0; i < asset.Size; i++)
            {
                AssetProperty prop = asset[i];
                string n = prop.Name?.ToLowerInvariant() ?? "";

                // roughness 類（直接設）
                if (n.Contains("roughness"))
                {
                    TrySetDoubleProperty(prop, roughness);
                }
                // glossiness 類（設反值）
                else if (n.Contains("glossiness") || n.Contains("shininess"))
                {
                    TrySetDoubleProperty(prop, glossiness);
                }
            }
        }

        private void TrySetDoubleProperty(AssetProperty prop, double value)
        {
            try
            {
                if (prop is AssetPropertyDouble d)
                {
                    d.Value = value;
                }
                else if (prop is AssetPropertyFloat f)
                {
                    f.Value = (float)value;
                }
            }
            catch { /* read-only 或 connected 等情況，忽略 */ }
        }

        private string GenerateUniqueAssetName(Document doc, string baseName)
        {
            var existing = new HashSet<string>(
                new FilteredElementCollector(doc)
                    .OfClass(typeof(AppearanceAssetElement))
                    .Cast<AppearanceAssetElement>()
                    .Select(a => a.Name));

            if (!existing.Contains(baseName)) return baseName;

            int n = 1;
            while (existing.Contains($"{baseName}_{n}")) n++;
            return $"{baseName}_{n}";
        }

        /// <summary>
        /// 取得 CompoundStructure 各層的材質名稱（牆/樓板/天花板）
        /// </summary>
        private List<string> GetCompoundStructureMaterials(Document doc, ElementType type)
        {
            var materials = new List<string>();

            CompoundStructure cs = null;
            if (type is WallType wt)
                cs = wt.GetCompoundStructure();
            else if (type is FloorType ft)
                cs = ft.GetCompoundStructure();
            else if (type is CeilingType ct)
                cs = ct.GetCompoundStructure();

            if (cs == null)
            {
                // 沒有 CompoundStructure（如疊層牆等），嘗試讀取 MATERIAL_ID_PARAM
                Parameter matParam = type.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM);
                if (matParam != null && matParam.HasValue)
                {
                    Material mat = doc.GetElement(matParam.AsElementId()) as Material;
                    if (mat != null)
                        materials.Add(mat.Name);
                }
                return materials;
            }

            for (int i = 0; i < cs.LayerCount; i++)
            {
                ElementId matId = cs.GetMaterialId(i);
                if (matId != ElementId.InvalidElementId)
                {
                    Material mat = doc.GetElement(matId) as Material;
                    materials.Add(mat?.Name ?? $"(ID:{matId.GetIdValue()})");
                }
                else
                {
                    materials.Add("<By Category>");
                }
            }

            return materials;
        }

        /// <summary>
        /// 設定 CompoundStructure 所有層的材質，回傳原始材質名稱列表
        /// </summary>
        private List<string> SetCompoundStructureMaterial(Document doc, ElementType type, ElementId newMatId)
        {
            var originalMats = new List<string>();

            CompoundStructure cs = null;
            if (type is WallType wt)
                cs = wt.GetCompoundStructure();
            else if (type is FloorType ft)
                cs = ft.GetCompoundStructure();

            if (cs == null)
            {
                // 沒有 CompoundStructure，嘗試設定 MATERIAL_ID_PARAM
                Parameter matParam = type.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM);
                if (matParam != null && !matParam.IsReadOnly)
                {
                    Material origMat = doc.GetElement(matParam.AsElementId()) as Material;
                    originalMats.Add(origMat?.Name ?? "<none>");
                    matParam.Set(newMatId);
                }
                return originalMats;
            }

            for (int i = 0; i < cs.LayerCount; i++)
            {
                ElementId origMatId = cs.GetMaterialId(i);
                if (origMatId != ElementId.InvalidElementId)
                {
                    Material origMat = doc.GetElement(origMatId) as Material;
                    originalMats.Add(origMat?.Name ?? $"(ID:{origMatId.GetIdValue()})");
                }
                else
                {
                    originalMats.Add("<By Category>");
                }
                cs.SetMaterialId(i, newMatId);
            }

            if (type is WallType wt2)
                wt2.SetCompoundStructure(cs);
            else if (type is FloorType ft2)
                ft2.SetCompoundStructure(cs);

            return originalMats;
        }

        /// <summary>
        /// 設定 FamilySymbol 的 Structural Material 參數，回傳原始材質名稱
        /// </summary>
        private string SetStructuralMaterial(FamilySymbol symbol, ElementId newMatId)
        {
            Document doc = symbol.Document;
            string origMatName = "<none>";

            // 嘗試 STRUCTURAL_MATERIAL_PARAM
            Parameter matParam = symbol.get_Parameter(BuiltInParameter.STRUCTURAL_MATERIAL_PARAM);

            // 備用：MATERIAL_ID_PARAM
            if (matParam == null || matParam.IsReadOnly)
                matParam = symbol.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM);

            if (matParam != null && !matParam.IsReadOnly)
            {
                ElementId origId = matParam.AsElementId();
                if (origId != ElementId.InvalidElementId)
                {
                    Material origMat = doc.GetElement(origId) as Material;
                    origMatName = origMat?.Name ?? $"(ID:{origId.GetIdValue()})";
                }
                matParam.Set(newMatId);
            }
            else
            {
                throw new Exception("找不到可修改的材質參數 (STRUCTURAL_MATERIAL_PARAM / MATERIAL_ID_PARAM)");
            }

            return origMatName;
        }

        /// <summary>
        /// 收集 FamilySymbol 類型資訊（柱/梁）
        /// </summary>
        private void CollectFamilySymbolTypes(Document doc, BuiltInCategory[] categories, List<object> typeInfos)
        {
            var allInstances = new List<Element>();
            foreach (var cat in categories)
            {
                allInstances.AddRange(
                    new FilteredElementCollector(doc)
                        .OfCategory(cat)
                        .WhereElementIsNotElementType()
                        .ToList());
            }

            var symbols = new HashSet<ElementId>();
            foreach (var cat in categories)
            {
                var collector = new FilteredElementCollector(doc)
                    .OfCategory(cat)
                    .WhereElementIsElementType()
                    .Cast<FamilySymbol>();

                foreach (var fs in collector)
                {
                    if (symbols.Contains(fs.Id)) continue;
                    symbols.Add(fs.Id);

                    int instanceCount = allInstances.Count(e => e.GetTypeId() == fs.Id);

                    string structuralMat = "<none>";
                    Parameter matParam = fs.get_Parameter(BuiltInParameter.STRUCTURAL_MATERIAL_PARAM);
                    if (matParam == null)
                        matParam = fs.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM);

                    if (matParam != null && matParam.HasValue)
                    {
                        Material mat = doc.GetElement(matParam.AsElementId()) as Material;
                        if (mat != null)
                            structuralMat = mat.Name;
                    }

                    typeInfos.Add(new
                    {
                        TypeId = fs.Id.GetIdValue(),
                        TypeName = fs.Name,
                        FamilyName = fs.FamilyName,
                        InstanceCount = instanceCount,
                        Materials = new List<string> { structuralMat }
                    });
                }
            }
        }

        #endregion
    }
}
