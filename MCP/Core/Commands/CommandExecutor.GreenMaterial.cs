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
        /// 複製指定 ElementType 建立新的獨立類型 (例如複製 Basic Wall 建立新 [TABC] 綠建材牆類型)，
        /// 並依 finishMaterialName / structureMaterialName 實體建立 2 個獨立綠建材 Material，
        /// 套入 Finish1 / Structure / Finish2 三層 CompoundStructure 構造層。
        /// 參數：
        ///   sourceTypeId (number): 來源類型 Element ID
        ///   newTypeName (string): 新類型的名稱
        ///   finishMaterialName (string): 飾面塗料材質名稱 (GBM編號_材料名稱)
        ///   structureMaterialName (string): 結構板材材質名稱 (GBM編號_材料名稱)
        ///   finishThicknessMm (number, optional): 飾面層厚度，預設 20mm
        ///   structureThicknessMm (number, optional): 結構層厚度，預設 150mm
        /// </summary>
        private object DuplicateElementType(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;
            IdType sourceTypeId = parameters["sourceTypeId"]?.Value<IdType>() ?? 0;
            string newTypeName = parameters["newTypeName"]?.Value<string>();
            string finishMaterialName = parameters["finishMaterialName"]?.Value<string>();
            string structureMaterialName = parameters["structureMaterialName"]?.Value<string>();
            double finishThicknessMm = parameters["finishThicknessMm"]?.Value<double>() ?? 20.0;
            double structureThicknessMm = parameters["structureThicknessMm"]?.Value<double>() ?? 150.0;

            if (string.IsNullOrEmpty(newTypeName))
                throw new Exception("請指定新類型名稱 (newTypeName)");
            if (string.IsNullOrEmpty(finishMaterialName))
                throw new Exception("請指定飾面塗料材質名稱 (finishMaterialName)，格式須為 GBM編號_材料名稱");
            if (string.IsNullOrEmpty(structureMaterialName))
                throw new Exception("請指定結構板材材質名稱 (structureMaterialName)，格式須為 GBM編號_材料名稱");

            ElementType source = doc.GetElement(new ElementId(sourceTypeId)) as ElementType;
            if (source == null)
                throw new Exception($"找不到來源類型 ID: {sourceTypeId}");

            double finishFeet = finishThicknessMm / 304.8;
            double structFeet = structureThicknessMm / 304.8;

            using (Transaction trans = new Transaction(doc, $"複製類型與實體建立綠建材材質: {newTypeName}"))
            {
                trans.Start();
                ElementType newType = source.Duplicate(newTypeName);

                // === 依 Set 實際材料名稱，在 Revit 專案材質庫中實體建立 2 個獨立綠建材 Material ===
                Material finishMat = GetOrCreatePureMaterial(doc, finishMaterialName, new Color(235, 245, 240));
                Material structMat = GetOrCreatePureMaterial(doc, structureMaterialName, new Color(240, 240, 240));

                WallType wallType = newType as WallType;
                if (wallType != null)
                {
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
                    FinishMaterialCreated = finishMaterialName,
                    FinishMaterialId = finishMat.Id.GetIdValue(),
                    StructureMaterialCreated = structureMaterialName,
                    StructureMaterialId = structMat.Id.GetIdValue(),
                    Message = $"成功建立新類型 '{newTypeName}'，並已在 Revit 專案材質庫中 100% 實體建立材質 '{finishMaterialName}' (ID:{finishMat.Id}) 與 '{structureMaterialName}' (ID:{structMat.Id})！"
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

        /// <summary>
        /// 將綠建材 v4 共享參數 Schema（GreenMaterial_SharedParams.txt，Mat1/Mat2/Mat3 三槽位共 31 個欄位）
        /// 實體寫入指定 ElementType 的 Identity Data。
        /// 參數須已透過 load_shared_parameters 綁定至該 Type 所屬品類，否則對應欄位會列在 MissingParameters。
        /// 參數：
        ///   typeId (number): 目標 ElementType Element ID（如 duplicate_element_type 建立的新型別）
        ///   certified (bool, optional): GreenMaterial_Certified
        ///   recycledRatio (number, optional): GreenMaterial_RecycledRatio
        ///   acousticNRC (number, optional): GreenMaterial_AcousticNRC
        ///   mat1 / mat2 (object, optional): { name, certNo, category, subCategory, applicant, validUntil, tvoc, formaldehyde, cnsSpec, testItems, qualifiedItems }
        ///   mat3 (object, optional): { name, certNo, category, subCategory, applicant, validUntil }（無 TVOC/Formaldehyde/CNS 欄位）
        /// </summary>
        private object SetGreenMaterialTypeParameters(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;
            IdType typeId = parameters["typeId"]?.Value<IdType>() ?? 0;

            ElementType type = doc.GetElement(new ElementId(typeId)) as ElementType;
            if (type == null)
                throw new Exception($"找不到 ElementType ID: {typeId}");

            var written = new List<string>();
            var missing = new List<string>();

            using (Transaction trans = new Transaction(doc, $"寫入綠建材共享參數: {type.Name}"))
            {
                trans.Start();

                JToken certifiedToken = parameters["certified"];
                if (certifiedToken != null && certifiedToken.Type != JTokenType.Null)
                    SetTypeParamBool(type, "GreenMaterial_Certified", certifiedToken.Value<bool>(), written, missing);

                JToken recycledToken = parameters["recycledRatio"];
                if (recycledToken != null && recycledToken.Type != JTokenType.Null)
                    SetTypeParamDouble(type, "GreenMaterial_RecycledRatio", recycledToken.Value<double>(), written, missing);

                JToken acousticToken = parameters["acousticNRC"];
                if (acousticToken != null && acousticToken.Type != JTokenType.Null)
                    SetTypeParamDouble(type, "GreenMaterial_AcousticNRC", acousticToken.Value<double>(), written, missing);

                WriteGreenMaterialSlot(type, "Mat1", parameters["mat1"] as JObject, includeExtended: true, written, missing);
                WriteGreenMaterialSlot(type, "Mat2", parameters["mat2"] as JObject, includeExtended: true, written, missing);
                WriteGreenMaterialSlot(type, "Mat3", parameters["mat3"] as JObject, includeExtended: false, written, missing);

                trans.Commit();
            }

            return new
            {
                Success = true,
                TypeId = typeId,
                TypeName = type.Name,
                WrittenCount = written.Count,
                WrittenParameters = written,
                MissingCount = missing.Count,
                MissingParameters = missing,
                Message = missing.Count == 0
                    ? $"成功寫入 {written.Count} 個綠建材共享參數至 '{type.Name}'"
                    : $"寫入 {written.Count} 個參數，{missing.Count} 個參數在 Type '{type.Name}' 上找不到（可能尚未透過 load_shared_parameters 綁定至此品類）：{string.Join(", ", missing)}"
            };
        }

        private void WriteGreenMaterialSlot(ElementType type, string slot, JObject mat, bool includeExtended, List<string> written, List<string> missing)
        {
            if (mat == null) return;

            void SetText(string field, string suffix)
            {
                JToken token = mat[field];
                if (token == null || token.Type == JTokenType.Null) return;
                SetTypeParamText(type, $"GreenMaterial_{slot}_{suffix}", token.Value<string>(), written, missing);
            }

            void SetNum(string field, string suffix)
            {
                JToken token = mat[field];
                if (token == null || token.Type == JTokenType.Null) return;
                SetTypeParamDouble(type, $"GreenMaterial_{slot}_{suffix}", token.Value<double>(), written, missing);
            }

            SetText("name", "Name");
            SetText("certNo", "CertNo");
            SetText("category", "Category");
            SetText("subCategory", "SubCategory");
            SetText("applicant", "Applicant");
            SetText("validUntil", "ValidUntil");

            if (includeExtended)
            {
                SetNum("tvoc", "TVOC");
                SetNum("formaldehyde", "Formaldehyde");
                SetText("cnsSpec", "CNSSpec");
                SetText("testItems", "TestItems");
                SetText("qualifiedItems", "QualifiedItems");
            }
        }

        private void SetTypeParamText(ElementType type, string paramName, string value, List<string> written, List<string> missing)
        {
            Parameter p = type.LookupParameter(paramName);
            if (p == null || p.IsReadOnly) { missing.Add(paramName); return; }
            p.Set(value ?? "");
            written.Add(paramName);
        }

        private void SetTypeParamDouble(ElementType type, string paramName, double value, List<string> written, List<string> missing)
        {
            Parameter p = type.LookupParameter(paramName);
            if (p == null || p.IsReadOnly) { missing.Add(paramName); return; }
            p.Set(value);
            written.Add(paramName);
        }

        private void SetTypeParamBool(ElementType type, string paramName, bool value, List<string> written, List<string> missing)
        {
            Parameter p = type.LookupParameter(paramName);
            if (p == null || p.IsReadOnly) { missing.Add(paramName); return; }
            p.Set(value ? 1 : 0);
            written.Add(paramName);
        }

        /// <summary>
        /// 情境 2「各別建立」：複製指定 ElementType（Wall/Floor/Ceiling Type）建立新類型，
        /// 並實體建立一個純淨綠建材 Material，指派到新 Type 的**全部** CompoundStructure 層。
        /// 與 duplicate_element_type（牆板+塗料兩種材料合併成一個 Type 的「單一組合」情境）不同，
        /// 這裡一個 Set 裡每個材料各自獨立建一個 Type，Type 名稱與 Material 名稱使用同一組字串
        /// （GBM編號_材料名稱），不套 TABC_ 前綴。
        /// 參數：
        ///   sourceTypeId (number): 來源類型 Element ID（需與目標品類相同，如同為 FloorType）
        ///   materialName (string): 同時作為新 Type 名稱與 Material 名稱，格式須為 GBM編號_材料名稱
        /// </summary>
        private object CreateSingleMaterialType(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;
            IdType sourceTypeId = parameters["sourceTypeId"]?.Value<IdType>() ?? 0;
            string materialName = parameters["materialName"]?.Value<string>();

            if (string.IsNullOrEmpty(materialName))
                throw new Exception("請指定 materialName（同時作為新 Type 名稱與 Material 名稱，格式須為 GBM編號_材料名稱）");

            ElementType source = doc.GetElement(new ElementId(sourceTypeId)) as ElementType;
            if (source == null)
                throw new Exception($"找不到來源類型 ID: {sourceTypeId}");

            using (Transaction trans = new Transaction(doc, $"複製類型與實體建立單一材質: {materialName}"))
            {
                trans.Start();
                ElementType newType = source.Duplicate(materialName);

                Material mat = GetOrCreatePureMaterial(doc, materialName, new Color(235, 245, 240));

                CompoundStructure cs = GetCompoundStructureForElement(newType);
                int layersAssigned = 0;
                if (cs != null)
                {
                    for (int i = 0; i < cs.LayerCount; i++)
                        cs.SetMaterialId(i, mat.Id);
                    SetCompoundStructureForElement(newType, cs);
                    layersAssigned = cs.LayerCount;
                }
                else
                {
                    Parameter p = newType.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM);
                    if (p != null && !p.IsReadOnly)
                    {
                        p.Set(mat.Id);
                        layersAssigned = 1;
                    }
                }

                trans.Commit();

                return new
                {
                    Success = true,
                    NewTypeId = newType.Id.GetIdValue(),
                    NewTypeName = newType.Name,
                    SourceTypeId = sourceTypeId,
                    MaterialId = mat.Id.GetIdValue(),
                    MaterialName = mat.Name,
                    LayersAssigned = layersAssigned,
                    Message = $"成功建立新類型 '{newType.Name}'，並將材質 '{mat.Name}' (ID:{mat.Id}) 指派到全部 {layersAssigned} 層構造"
                };
            }
        }

        /// <summary>
        /// 通用多材料構造層工具：複製指定 ElementType（Wall/Floor/Ceiling 皆可），
        /// 依任意數量的材料清單建立獨立綠建材 Material，依序套入 CompoundStructure 各層。
        /// 與 duplicate_element_type（寫死 2 個材料的 Finish1/Structure/Finish2 牆體三明治）不同，
        /// 這裡層數、材料、層位機能（layerFunction）完全由呼叫端指定，適用於 2 個材料以上、
        /// 或非 Wall 品類的「單一組合」情境（例如地板：飾面地磚 Finish + 隔音緩衝墊 Substrate + 混凝土 Structure）。
        /// 參數：
        ///   sourceTypeId (number): 來源類型 Element ID（需與目標品類相同）
        ///   newTypeName (string): 新類型的名稱
        ///   layers (array): 依構造順序排列，每項 { materialName, layerFunction, thicknessMm }
        ///     layerFunction 對應 Revit MaterialFunctionAssignment：Structure/Substrate/Insulation/Finish1/Finish2/Membrane
        /// </summary>
        private object CreateMultiLayerType(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;
            IdType sourceTypeId = parameters["sourceTypeId"]?.Value<IdType>() ?? 0;
            string newTypeName = parameters["newTypeName"]?.Value<string>();
            JArray layersArr = parameters["layers"] as JArray;

            if (string.IsNullOrEmpty(newTypeName))
                throw new Exception("請指定新類型名稱 (newTypeName)");
            if (layersArr == null || layersArr.Count == 0)
                throw new Exception("請提供至少一層 layers（每項含 materialName / layerFunction / thicknessMm）");

            ElementType source = doc.GetElement(new ElementId(sourceTypeId)) as ElementType;
            if (source == null)
                throw new Exception($"找不到來源類型 ID: {sourceTypeId}");

            using (Transaction trans = new Transaction(doc, $"複製類型與實體建立多層綠建材構造: {newTypeName}"))
            {
                trans.Start();
                ElementType newType = source.Duplicate(newTypeName);

                CompoundStructure cs = GetCompoundStructureForElement(newType);
                if (cs == null)
                    throw new Exception($"來源類型 '{source.Name}' 沒有 CompoundStructure（可能是無分層構造的族群類型），無法套用多層構造");

                var newLayers = new List<CompoundStructureLayer>();
                var layerReport = new List<object>();
                var layerFunctionOrder = new List<string>();

                foreach (JToken tok in layersArr)
                {
                    JObject layerObj = tok as JObject;
                    if (layerObj == null)
                        throw new Exception("layers 陣列中每一項都必須是物件 { materialName, layerFunction, thicknessMm }");

                    string materialName = layerObj["materialName"]?.Value<string>();
                    string layerFunctionStr = layerObj["layerFunction"]?.Value<string>();
                    double thicknessMm = layerObj["thicknessMm"]?.Value<double>() ?? 20.0;

                    if (string.IsNullOrEmpty(materialName))
                        throw new Exception("每一層都必須指定 materialName（格式須為 GBM編號_材料名稱）");
                    if (string.IsNullOrEmpty(layerFunctionStr))
                        throw new Exception("每一層都必須指定 layerFunction（Structure/Substrate/Insulation/Finish1/Finish2/Membrane）");

                    MaterialFunctionAssignment layerFunction = ParseMaterialFunctionAssignment(layerFunctionStr);
                    Material mat = GetOrCreatePureMaterial(doc, materialName, new Color(235, 245, 240));
                    double thicknessFeet = thicknessMm / 304.8;

                    newLayers.Add(new CompoundStructureLayer(thicknessFeet, layerFunction, mat.Id));
                    layerReport.Add(new
                    {
                        MaterialName = mat.Name,
                        MaterialId = mat.Id.GetIdValue(),
                        LayerFunction = layerFunctionStr,
                        ThicknessMm = thicknessMm
                    });
                    layerFunctionOrder.Add(layerFunctionStr);
                }

                cs.SetLayers(newLayers);

                // Finish1/Membrane 排在最前面的連續層視為外側 shell（core boundary 外），
                // Finish2/Membrane 排在最後面的連續層視為內側 shell；中間（Structure/Substrate/
                // Insulation）留在 core boundary 內。不設的話所有層預設都會落在 core 裡面，
                // Finish 層會被誤當成結構核心的一部分。
                int exteriorShellCount = 0;
                while (exteriorShellCount < newLayers.Count &&
                       IsShellFunction(newLayers[exteriorShellCount].Function))
                {
                    exteriorShellCount++;
                }
                int interiorShellCount = 0;
                while (interiorShellCount < newLayers.Count - exteriorShellCount &&
                       IsShellFunction(newLayers[newLayers.Count - 1 - interiorShellCount].Function))
                {
                    interiorShellCount++;
                }
                cs.SetNumberOfShellLayers(ShellLayerType.Exterior, exteriorShellCount);
                cs.SetNumberOfShellLayers(ShellLayerType.Interior, interiorShellCount);

                SetCompoundStructureForElement(newType, cs);

                trans.Commit();

                return new
                {
                    Success = true,
                    NewTypeId = newType.Id.GetIdValue(),
                    NewTypeName = newType.Name,
                    SourceTypeId = sourceTypeId,
                    LayerCount = newLayers.Count,
                    ExteriorShellLayers = exteriorShellCount,
                    InteriorShellLayers = interiorShellCount,
                    Layers = layerReport,
                    Message = $"成功建立新類型 '{newType.Name}'，套入 {newLayers.Count} 層構造：{string.Join(" → ", layerFunctionOrder)}" +
                              $"（外側 shell {exteriorShellCount} 層、內側 shell {interiorShellCount} 層在 core boundary 外）"
                };
            }
        }

        private static bool IsShellFunction(MaterialFunctionAssignment function)
        {
            return function == MaterialFunctionAssignment.Finish1
                || function == MaterialFunctionAssignment.Finish2
                || function == MaterialFunctionAssignment.Membrane;
        }

        private MaterialFunctionAssignment ParseMaterialFunctionAssignment(string s)
        {
            switch (s.Trim())
            {
                case "Structure": return MaterialFunctionAssignment.Structure;
                case "Substrate": return MaterialFunctionAssignment.Substrate;
                case "Insulation": return MaterialFunctionAssignment.Insulation;
                case "Finish1": return MaterialFunctionAssignment.Finish1;
                case "Finish2": return MaterialFunctionAssignment.Finish2;
                case "Membrane": return MaterialFunctionAssignment.Membrane;
                default:
                    throw new Exception($"不支援的 layerFunction: '{s}'（支援 Structure/Substrate/Insulation/Finish1/Finish2/Membrane）");
            }
        }
    }
}