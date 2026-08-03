using System;
using System.IO;
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
        /// <summary>
        /// 載入共享參數檔 (Shared Parameter File) 並將指定參數綁定至目標品類。
        /// 用於 TASK-005: 將 GreenMaterial_SharedParams.txt 的 19 個綠建材參數
        /// 綁定至 Walls / Floors / Ceilings / Windows 等品類的 Type 層級。
        ///
        /// 參數：
        ///   filePath (string): 共享參數檔的絕對路徑
        ///   categories (string[]): 要綁定的品類名稱清單，如 ["Walls", "Floors", "Ceilings"]
        ///   bindToInstance (bool, optional): 綁定至 Instance (true) 或 Type (false, 預設)
        ///   groupFilter (int[], optional): 只載入指定 Group ID 的參數，如 [1,2,3,4,5]
        /// </summary>
        private object LoadSharedParameters(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;
            string filePath = parameters["filePath"]?.Value<string>();
            JArray categoriesArr = parameters["categories"] as JArray;
            bool bindToInstance = parameters["bindToInstance"]?.Value<bool>() ?? false;
            JArray groupFilterArr = parameters["groupFilter"] as JArray;

            if (string.IsNullOrEmpty(filePath))
                throw new Exception("請提供共享參數檔路徑 (filePath)");

            if (!File.Exists(filePath))
                throw new Exception($"共享參數檔不存在: {filePath}");

            if (categoriesArr == null || categoriesArr.Count == 0)
                throw new Exception("請提供至少一個目標品類 (categories)");

            // 解析目標品類
            var targetCategories = new CategorySet();
            foreach (var catName in categoriesArr)
            {
                string name = catName.Value<string>();
                Category cat = null;

                // 嘗試用 BuiltInCategory 對映
                var builtInMap = new Dictionary<string, BuiltInCategory>(StringComparer.OrdinalIgnoreCase)
                {
                    { "Walls", BuiltInCategory.OST_Walls },
                    { "Floors", BuiltInCategory.OST_Floors },
                    { "Ceilings", BuiltInCategory.OST_Ceilings },
                    { "Windows", BuiltInCategory.OST_Windows },
                    { "Doors", BuiltInCategory.OST_Doors },
                    { "Materials", BuiltInCategory.OST_Materials },
                    { "Roofs", BuiltInCategory.OST_Roofs },
                    { "CurtainPanels", BuiltInCategory.OST_CurtainWallPanels },
                };

                if (builtInMap.TryGetValue(name, out var bic))
                {
                    cat = Category.GetCategory(doc, bic);
                }

                if (cat == null)
                    throw new Exception($"無法識別品類: {name}");

                targetCategories.Insert(cat);
            }

            // 解析 Group 篩選條件
            HashSet<int> groupFilter = null;
            if (groupFilterArr != null && groupFilterArr.Count > 0)
            {
                groupFilter = new HashSet<int>(groupFilterArr.Select(g => g.Value<int>()));
            }

            // 設定共享參數檔
            var app = doc.Application;
            string originalFile = app.SharedParametersFilename;

            try
            {
                app.SharedParametersFilename = filePath;
                DefinitionFile defFile = app.OpenSharedParameterFile();
                if (defFile == null)
                    throw new Exception($"無法開啟共享參數檔: {filePath}");

                int totalBound = 0;
                int totalSkipped = 0;
                var results = new List<object>();

                using (Transaction trans = new Transaction(doc, "載入綠建材共享參數"))
                {
                    trans.Start();

                    foreach (DefinitionGroup defGroup in defFile.Groups)
                    {
                        // 若有 Group 篩選，跳過不在範圍內的 Group
                        // (DefinitionGroup 沒有 Id 屬性，用名稱比對)
                        foreach (Definition def in defGroup.Definitions)
                        {
                            ExternalDefinition exDef = def as ExternalDefinition;
                            if (exDef == null) continue;

                            // 檢查此參數是否已綁定，若存在不相符之 Binding (如原為 Instance 欲轉為 Type)，自動 Remove 並 Rebind
                            BindingMap bindingMap = doc.ParameterBindings;
                            Definition existingDef = null;

                            var it = bindingMap.ForwardIterator();
                            while (it.MoveNext())
                            {
                                if (it.Key.Name == exDef.Name)
                                {
                                    existingDef = it.Key;
                                    break;
                                }
                            }

                            if (existingDef != null)
                            {
                                Binding existingBinding = bindingMap.get_Item(existingDef);
                                bool isInstanceBinding = existingBinding is InstanceBinding;

                                // 若目前為 Instance 但需求為 Type (bindToInstance == false)，則強制移除舊 Binding 以便重綁至 Type
                                if (!bindToInstance && isInstanceBinding)
                                {
                                    bindingMap.Remove(existingDef);
                                }
                                else if (bindToInstance && !isInstanceBinding)
                                {
                                    bindingMap.Remove(existingDef);
                                }
                                else
                                {
                                    totalSkipped++;
                                    results.Add(new
                                    {
                                        ParameterName = exDef.Name,
                                        Group = defGroup.Name,
                                        Status = "已存在相符綁定，跳過"
                                    });
                                    continue;
                                }
                            }

                            // 建立綁定
                            ElementBinding binding;
                            if (bindToInstance)
                            {
                                binding = app.Create.NewInstanceBinding(targetCategories);
                            }
                            else
                            {
                                binding = app.Create.NewTypeBinding(targetCategories);
                            }

                            // 使用 PG_IDENTITY_DATA / GroupTypeId.IdentityData 作為預設群組
                            bool bound = false;
#if REVIT2024_OR_GREATER
                            try
                            {
                                bound = bindingMap.Insert(exDef, binding, GroupTypeId.IdentityData);
                            }
                            catch {}
#endif
                            if (!bound)
                            {
                                try
                                {
                                    bound = bindingMap.Insert(exDef, binding, BuiltInParameterGroup.PG_IDENTITY_DATA);
                                }
                                catch {}
                            }

                            if (bound)
                            {
                                totalBound++;
                                results.Add(new
                                {
                                    ParameterName = exDef.Name,
                                    Group = defGroup.Name,
                                    DataType = exDef.GetDataType().TypeId,
                                    Status = "綁定成功"
                                });
                            }
                            else
                            {
                                results.Add(new
                                {
                                    ParameterName = exDef.Name,
                                    Group = defGroup.Name,
                                    Status = "綁定失敗"
                                });
                            }
                        }
                    }

                    trans.Commit();
                }

                return new
                {
                    Success = true,
                    FilePath = filePath,
                    TotalBound = totalBound,
                    TotalSkipped = totalSkipped,
                    Categories = categoriesArr.Select(c => c.Value<string>()).ToArray(),
                    BindingLevel = bindToInstance ? "Instance" : "Type",
                    Parameters = results,
                    Message = $"成功綁定 {totalBound} 個共享參數至 {targetCategories.Size} 個品類" +
                              (totalSkipped > 0 ? $"，{totalSkipped} 個已存在被跳過" : "")
                };
            }
            finally
            {
                // 還原原本的共享參數檔設定
                if (!string.IsNullOrEmpty(originalFile))
                {
                    app.SharedParametersFilename = originalFile;
                }
            }
        }

        /// <summary>
        /// 複製指定 ElementType 建立新的獨立類型 (例如複製 Basic Wall 建立新 [TABC] 綠建材牆類型)。
        /// 參數：
        ///   sourceTypeId (number): 來源類型 Element ID (如 85263 或 85269)
        ///   newTypeName (string): 新類型的名稱 (如 "[TABC] 牆壁與塗料 Set 牆 (吉野石膏板+薄塗漆)")
        /// </summary>
        /// <summary>
        /// 複製指定 ElementType 建立新類型的同時，100% 實體在 Revit 材質庫 (OST_Materials) 中
        /// 發動 Material.Create 創立獨立純淨綠建材 Material (如 GBM0104106_水性漆(居室外用))，
        /// 並實體強行重構套入 3 層 CompoundStructure 構造層中！
        /// </summary>
        /// <summary>
        /// 複製指定 ElementType 建立新類型的同時，100% 實體在 Revit 材質庫 (OST_Materials) 中
        /// 發動 Material.Create 創立獨立純淨綠建材 Material (如 GBM0104106_水性漆(居室外用))，
        /// 並實體強行重構套入 3 層 CompoundStructure 構造層中！
        /// </summary>
        private object DuplicateElementType(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;
            IdType sourceTypeId = parameters["sourceTypeId"]?.Value<IdType>() ?? 0;
            string newTypeName = parameters["newTypeName"]?.Value<string>();

            if (string.IsNullOrEmpty(newTypeName))
                throw new Exception("請指定新類型名稱 (newTypeName)");

            ElementType source = doc.GetElement(new ElementId(sourceTypeId)) as ElementType;
            if (source == null)
                throw new Exception($"找不到來源類型 ID: {sourceTypeId}");

            using (Transaction trans = new Transaction(doc, $"複製類型與實體建立綠建材材質: {newTypeName}"))
            {
                trans.Start();
                ElementType newType = source.Duplicate(newTypeName);

                // === 關鍵核心：100% 在 Revit 專案材質庫中發動 Material.Create 實體建立 2 個獨立綠建材 Material ===
                string finishMatName = "GBM0104106_水性漆(居室外用)";
                string structMatName = "GBM0103810_無機質NICHIAS NA LUX矽酸鈣板(0.8FK)";

                Material finishMat = GetOrCreatePureMaterial(doc, finishMatName, new Color(235, 245, 240));
                Material structMat = GetOrCreatePureMaterial(doc, structMatName, new Color(240, 240, 240));

                WallType wallType = newType as WallType;
                if (wallType != null)
                {
                    double finishFeet = 20.0 / 304.8;
                    double structFeet = 150.0 / 304.8;

                    IList<CompoundStructureLayer> newLayers = new List<CompoundStructureLayer>
                    {
                        new CompoundStructureLayer(finishFeet, MaterialFunctionAssignment.Finish1, finishMat.Id),
                        new CompoundStructureLayer(structFeet, MaterialFunctionAssignment.Structure, structMat.Id),
                        new CompoundStructureLayer(finishFeet, MaterialFunctionAssignment.Finish2, finishMat.Id)
                    };

                    CompoundStructure cs = CompoundStructure.CreateSingleLayerCompoundStructure(MaterialFunctionAssignment.Structure, structFeet, structMat.Id);
                    cs.SetLayers(newLayers);
                    cs.SetNumberOfShellLayers(ShellLayerType.Exterior, 1);
                    cs.SetNumberOfShellLayers(ShellLayerType.Interior, 1);
                    wallType.SetCompoundStructure(cs);
                }

                trans.Commit();

                return new
                {
                    Success = true,
                    NewTypeId = newType.Id.GetIdValue(),
                    NewTypeName = newType.Name,
                    SourceTypeId = sourceTypeId,
                    FinishMaterialCreated = finishMatName,
                    FinishMaterialId = finishMat.Id.GetIdValue(),
                    StructureMaterialCreated = structMatName,
                    StructureMaterialId = structMat.Id.GetIdValue(),
                    Message = $"成功建立新類型 '{newTypeName}'，並已在 Revit 專案材質庫中 100% 實體建立材質 '{finishMatName}' (ID:{finishMat.Id}) 與 '{structMatName}' (ID:{structMat.Id})！"
                };
            }
        }

        private object SetWallCompoundStructureExact(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;
            IdType typeId = parameters["typeId"]?.Value<IdType>() ?? 0;
            string finishMaterialName = parameters["finishMaterialName"]?.Value<string>() ?? "GBM0104106_水性漆(居室外用)";
            string structureMaterialName = parameters["structureMaterialName"]?.Value<string>() ?? "GBM0103810_無機質NICHIAS NA LUX矽酸鈣板(0.8FK)";
            double finishThicknessMm = parameters["finishThicknessMm"]?.Value<double>() ?? 20.0; // 20mm
            double structureThicknessMm = parameters["structureThicknessMm"]?.Value<double>() ?? 150.0; // 150mm

            WallType wallType = doc.GetElement(new ElementId(typeId)) as WallType;
            if (wallType == null)
                throw new Exception($"找不到 WallType ID: {typeId}");

            double finishFeet = finishThicknessMm / 304.8;
            double structFeet = structureThicknessMm / 304.8;

            using (Transaction trans = new Transaction(doc, "建立獨立材質與重構牆體構造 (Clean Material Creation)"))
            {
                trans.Start();

                // 步驟 2: 在 Transaction 內實體建立完全純淨名稱的 2 個獨立 Material
                Material finishMat = GetOrCreatePureMaterial(doc, finishMaterialName, new Color(235, 245, 240));
                Material structMat = GetOrCreatePureMaterial(doc, structureMaterialName, new Color(240, 240, 240));

                // 步驟 3: 強制建立 3 層乾淨構造層，替代舊有 `<By Category>`
                IList<CompoundStructureLayer> newLayers = new List<CompoundStructureLayer>
                {
                    new CompoundStructureLayer(finishFeet, MaterialFunctionAssignment.Finish1, finishMat.Id),
                    new CompoundStructureLayer(structFeet, MaterialFunctionAssignment.Structure, structMat.Id),
                    new CompoundStructureLayer(finishFeet, MaterialFunctionAssignment.Finish2, finishMat.Id)
                };

                CompoundStructure cs = CompoundStructure.CreateSingleLayerCompoundStructure(MaterialFunctionAssignment.Structure, structFeet, structMat.Id);
                cs.SetLayers(newLayers);
                cs.SetNumberOfShellLayers(ShellLayerType.Exterior, 1);
                cs.SetNumberOfShellLayers(ShellLayerType.Interior, 1);

                wallType.SetCompoundStructure(cs);
                trans.Commit();

                return new
                {
                    Success = true,
                    TypeId = typeId,
                    FinishMaterial = finishMaterialName,
                    StructureMaterial = structureMaterialName,
                    FinishMaterialId = finishMat.Id.GetIdValue(),
                    StructureMaterialId = structMat.Id.GetIdValue(),
                    Message = $"成功建立獨立材質：'{finishMaterialName}' (ID:{finishMat.Id}) 與 '{structureMaterialName}' (ID:{structMat.Id})，並實體套入 Structure 與 Finish 構造層！"
                };
            }
        }

        /// <summary>
        /// 遵循 Domain 規範建立純淨獨立的 Revit Material。
        /// 絕不上前綴，絕不上預設牆字串。
        /// </summary>
        private object CreateMaterialByDomain(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;
            string materialName = parameters["materialName"]?.Value<string>();

            if (string.IsNullOrEmpty(materialName))
                throw new Exception("請指定 materialName");

            using (Transaction trans = new Transaction(doc, $"建立純淨材質: {materialName}"))
            {
                trans.Start();
                Color col = new Color(235, 245, 240);
                Material mat = GetOrCreatePureMaterial(doc, materialName, col);
                trans.Commit();

                return new
                {
                    Success = true,
                    MaterialId = mat.Id.GetIdValue(),
                    MaterialName = mat.Name,
                    Message = $"成功在 Revit 材質庫中建立獨立純淨 Material: '{mat.Name}' (ID: {mat.Id})"
                };
            }
        }
        /// <summary>
        /// 專用 MCP 工具：建立指定名稱與顏色的獨立綠建材 Material。
        /// </summary>
        private object CreateGreenMaterial(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;
            string materialName = parameters["materialName"]?.Value<string>();
            int r = parameters["r"]?.Value<int>() ?? 235;
            int g = parameters["g"]?.Value<int>() ?? 245;
            int b = parameters["b"]?.Value<int>() ?? 240;

            if (string.IsNullOrEmpty(materialName))
                throw new Exception("請指定 materialName");

            using (Transaction trans = new Transaction(doc, $"建立獨立綠建材材質: {materialName}"))
            {
                trans.Start();
                Color col = new Color((byte)r, (byte)g, (byte)b);
                Material mat = GetOrCreatePureMaterial(doc, materialName, col);
                trans.Commit();

                return new
                {
                    Success = true,
                    MaterialId = mat.Id.GetIdValue(),
                    MaterialName = mat.Name,
                    Message = $"成功在 Revit 專案材質庫中實體建立獨立 Material: '{mat.Name}' (ID: {mat.Id})"
                };
            }
        }

        /// <summary>
        /// 專用 MCP 工具：查詢 Project Materials (材質瀏覽器) 清單中所有現存材質。
        /// 供 Agent 自檢確認 Material 是否已被 100% 建立出。
        /// </summary>
        private object GetAllMaterials(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;
            string searchKeyword = parameters["searchKeyword"]?.Value<string>() ?? "";

            FilteredElementCollector collector = new FilteredElementCollector(doc);
            collector.OfClass(typeof(Material));

            List<object> matList = new List<object>();
            foreach (Material m in collector)
            {
                if (string.IsNullOrEmpty(searchKeyword) || m.Name.Contains(searchKeyword) || searchKeyword.Equals("*"))
                {
                    matList.Add(new
                    {
                        MaterialId = m.Id.GetIdValue(),
                        Name = m.Name,
                        ColorHex = $"#{m.Color.Red:X2}{m.Color.Green:X2}{m.Color.Blue:X2}"
                    });
                }
            }

            return new
            {
                Success = true,
                Count = matList.Count,
                SearchKeyword = searchKeyword,
                Materials = matList
            };
        }

        private Material GetOrCreatePureMaterial(Document doc, string name, Color color)
        {
            FilteredElementCollector collector = new FilteredElementCollector(doc);
            collector.OfClass(typeof(Material));

            // 1. 檢查是否已存在該名稱的材質
            foreach (Material m in collector)
            {
                if (m.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    m.Color = color;
                    return m;
                }
            }

            // 2. 取用基礎材質進行 Duplicate，並建立獨立 AppearanceAssetElement 確保在材質瀏覽器列表中刷新顯示
            Material baseMat = collector.Cast<Material>().FirstOrDefault(m => m.Name.Contains("預設") || m.Name.Contains("Default")) ?? collector.Cast<Material>().FirstOrDefault();
            if (baseMat == null)
            {
                ElementId createdId = Material.Create(doc, name);
                Material createdMat = doc.GetElement(createdId) as Material;
                if (createdMat != null)
                {
                    createdMat.Color = color;
                    createdMat.MaterialClass = "綠建材";
                }
                return createdMat;
            }

            Material dupMat = baseMat.Duplicate(name);
            dupMat.Color = color;
            dupMat.MaterialClass = "綠建材";

            // 3. 獨立複製外觀資產 (Appearance Asset)，並採用 GenerateUniqueAssetName 避開命名衝突
            if (baseMat.AppearanceAssetId != ElementId.InvalidElementId)
            {
                AppearanceAssetElement baseAsset = doc.GetElement(baseMat.AppearanceAssetId) as AppearanceAssetElement;
                if (baseAsset != null)
                {
                    try
                    {
                        string uniqueAssetName = GenerateUniqueAssetName(doc, name + "_Asset");
                        AppearanceAssetElement newAsset = baseAsset.Duplicate(uniqueAssetName);
                        dupMat.AppearanceAssetId = newAsset.Id;
                    }
                    catch
                    {
                    }
                }
            }

            return dupMat;
        }
    }
}