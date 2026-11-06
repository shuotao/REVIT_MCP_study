using System;
using System.Collections.Generic;
using System.IO;
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
        #region 門窗／獨立元件 RFA 綠建材導入 (inject_green_material_into_family, TASK-005.7)

        /// <summary>
        /// LoadFamily 覆蓋語意處理：這裡永遠回傳 true 讓覆蓋發生，
        /// 因為 InjectGreenMaterialIntoFamily 保證載入的是一個「新家族檔名」（規則4），
        /// 唯一會觸發 OnFamilyFound 的情境是重跑同一案例、專案內已存在同名家族——
        /// 那種情況下覆蓋的正是「已知是自己產物」的家族本身，不影響其他任何家族。
        /// 真正防止誤傷同 Family 底下其他 Type 的機制是呼叫端在同一個 Transaction 內
        /// 做的「載入前後 Type 簽章快照比對＋異動就 RollBack」，不是這個 callback。
        /// </summary>
        private class GreenMaterialFamilyLoadOptions : IFamilyLoadOptions
        {
            public bool OnFamilyFound(bool familyInUse, out bool overwriteParameterValues)
            {
                overwriteParameterValues = true;
                return true;
            }

            public bool OnSharedFamilyFound(Family sharedFamily, bool familyInUse, out FamilySource source, out bool overwriteParameterValues)
            {
                source = FamilySource.Family;
                overwriteParameterValues = true;
                return true;
            }
        }

        /// <summary>
        /// domain/GM_rfa-family-injection.md 的執行實作。對門/窗/獨立元件等載入式 Family (.rfa)
        /// 做綠建材注入：以使用者指定的既有相似 Family+Type 為基底 → EditFamily 開家族文件 →
        /// 立即 SaveAs 建立可復原備份（規則2，先於任何修改）→ 家族文件內只新增一個 Type，
        /// 絕不 rename/覆寫來源 Type（規則1/4）→ 寫入 Identity Data + GreenMaterial_Mat1_* +
        /// 門窗專屬遮陽/隔音欄位（規則3）→ SaveAs 為新家族檔名（規則4：用新檔名迴避 LoadFamily
        /// 覆蓋既有 Type 的歧義）→ LoadFamily 載回專案，且在同一個 Transaction 內做「載入前後
        /// 同名家族 Type 簽章快照比對」，一旦偵測到非目標 Type 被異動就整批 RollBack 並報錯，
        /// 不會靜默覆蓋（規則4強制驗證）。
        /// 參數：
        ///   sourceTypeId (number): 使用者指定的基底 FamilySymbol（門或窗）Element ID
        ///   newTypeName (string): 家族文件內新建 Type 的名稱
        ///   backupFolder (string, optional): 備份根目錄，預設 doc 所在目錄下的 _rfa_backup/
        ///   newFamilySuffix (string, optional): 新家族檔名後綴，預設 "_TABC"
        ///   sharedParamFilePath (string): GreenMaterial_SharedParams.txt 的絕對路徑
        ///   identityData (object, optional): { manufacturer, model, description, url }
        ///   mat1 (object): { name, certNo, category, subCategory, applicant, validUntil, tvoc, formaldehyde, cnsSpec, testItems, qualifiedItems }
        ///   certified (bool, optional): GreenMaterial_Certified（YESNO 全域欄位，語意與 set_green_material_type_parameters 的 certified 相同——這個 Type 整體的綠建材評定合格狀態）
        ///   shadingCoefficient (number, optional): GreenMaterial_Window_ShadingCoefficient，僅 Window/Curtain Wall 適用
        ///   acousticRw (number, optional): GreenMaterial_AcousticRw，Window 與 Door 皆適用
        /// </summary>
        private object InjectGreenMaterialIntoFamily(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;
            IdType sourceTypeId = parameters["sourceTypeId"]?.Value<IdType>() ?? 0;
            string newTypeName = parameters["newTypeName"]?.Value<string>();
            string backupFolder = parameters["backupFolder"]?.Value<string>();
            string newFamilySuffix = parameters["newFamilySuffix"]?.Value<string>() ?? "_TABC";
            string sharedParamFilePath = parameters["sharedParamFilePath"]?.Value<string>();
            JObject identityData = parameters["identityData"] as JObject;
            JObject mat1 = parameters["mat1"] as JObject;
            JToken certifiedToken = parameters["certified"];
            JToken shadingToken = parameters["shadingCoefficient"];
            JToken acousticToken = parameters["acousticRw"];
            bool dryRun = parameters["dryRun"]?.Value<bool>() ?? false;

            if (string.IsNullOrEmpty(newTypeName))
                throw new Exception("請指定新 Type 名稱 (newTypeName)");
            if (mat1 == null)
                throw new Exception("請提供主材料資料 (mat1)——門窗的玻璃或門扇視為該 Type 的主材料");
            if (string.IsNullOrEmpty(sharedParamFilePath))
                throw new Exception("請提供共享參數檔絕對路徑 (sharedParamFilePath)");
            if (!File.Exists(sharedParamFilePath))
                throw new Exception($"共享參數檔不存在: {sharedParamFilePath}");

            FamilySymbol sourceSymbol = doc.GetElement(new ElementId(sourceTypeId)) as FamilySymbol;
            if (sourceSymbol == null)
                throw new Exception($"找不到來源 FamilySymbol ID: {sourceTypeId}（規則1要求使用者先指定既有相似基底 Family+Type，不可由 AI 自行臆測）");

            Family sourceFamily = sourceSymbol.Family;
            if (sourceFamily.IsInPlace)
                throw new Exception("現地(in-place)族群無法另存編輯，請指定一個可載入式(loadable)家族的 Type");
            if (!sourceFamily.IsEditable)
                throw new Exception($"家族 '{sourceFamily.Name}' 不可編輯（可能是系統族群），無法走 RFA 注入路徑");

            string categoryName = sourceFamily.FamilyCategory?.Name ?? "Unknown";

            // === dryRun：本工具是 11 支寫入工具中風險最高的一支——真正的破壞性動作
            // （備份 SaveAs、新家族 SaveAs、LoadFamily）全部發生在 EditFamily() 開出的家族
            // 文件生命週期裡，那個生命週期橫跨 Transaction 邊界，RollBack 救不回已寫出的 .rfa
            // 檔案。因此這裡刻意選擇最保守的做法：dryRun=true 時完全不呼叫 doc.EditFamily()、
            // 不呼叫 Directory.CreateDirectory()、不做任何檔案系統寫入，只用「本專案文件內已
            // 載入的既有資訊」（sourceSymbol / sourceFamily / 其餘同名 FamilySymbol）回報路徑
            // 與欄位規劃。代價：像「這個共享參數欄位在家族裡是否已存在/會不會綁定失敗」這種
            // 只有打開家族文件才能確定的資訊，dryRun 無法提供，只能誠實列出「無法得知」。
            if (dryRun)
            {
                string plannedBackupFolder = backupFolder;
                if (string.IsNullOrWhiteSpace(plannedBackupFolder))
                {
                    string docDir = string.IsNullOrEmpty(doc.PathName) ? null : Path.GetDirectoryName(doc.PathName);
                    plannedBackupFolder = Path.Combine(string.IsNullOrEmpty(docDir) ? Path.GetTempPath() : docDir, "_rfa_backup");
                }

                string safeFamilyNameDry = SanitizeFileName(sourceFamily.Name);
                string plannedBackupFileNamePattern = $"{safeFamilyNameDry}_backup_<yyyyMMdd_HHmmss>.rfa";

                string licnoDry = mat1["certNo"]?.Value<string>();
                string familySuffixTagDry = string.IsNullOrWhiteSpace(licnoDry) ? newFamilySuffix : $"{newFamilySuffix}_{SanitizeFileName(licnoDry)}";
                string plannedNewFamilyFileName = $"{safeFamilyNameDry}{familySuffixTagDry}.rfa";
                string plannedNewFamilyPath = Path.Combine(plannedBackupFolder, plannedNewFamilyFileName);
                bool newFamilyFileAlreadyExists = File.Exists(plannedNewFamilyPath);

                // best-effort：只比對「目前已載入本專案文件」的同一家族其餘 Type 名稱，
                // 不等於家族檔案內部的完整 Type 清單（那需要 EditFamily 才能看到，dryRun 不做）。
                var siblingTypeNames = sourceFamily.GetFamilySymbolIds()
                    .Select(id => (doc.GetElement(id) as FamilySymbol)?.Name)
                    .Where(n => n != null)
                    .ToList();
                bool typeNameCollisionBestEffort = siblingTypeNames.Contains(newTypeName);

                var plannedFields = new List<string>();
                if (identityData != null)
                {
                    if (!string.IsNullOrEmpty(identityData["manufacturer"]?.Value<string>())) plannedFields.Add("IdentityData.Manufacturer");
                    if (!string.IsNullOrEmpty(identityData["model"]?.Value<string>())) plannedFields.Add("IdentityData.Model");
                    if (!string.IsNullOrEmpty(identityData["description"]?.Value<string>())) plannedFields.Add("IdentityData.Description");
                    if (!string.IsNullOrEmpty(identityData["url"]?.Value<string>())) plannedFields.Add("IdentityData.URL");
                }

                var mat1FieldSuffixes = new (string field, string suffix)[]
                {
                    ("name", "Name"), ("certNo", "CertNo"), ("category", "Category"),
                    ("subCategory", "SubCategory"), ("applicant", "Applicant"), ("validUntil", "ValidUntil"),
                    ("tvoc", "TVOC"), ("formaldehyde", "Formaldehyde"), ("cnsSpec", "CNSSpec"),
                    ("testItems", "TestItems"), ("qualifiedItems", "QualifiedItems"),
                };
                foreach (var (field, suffix) in mat1FieldSuffixes)
                {
                    JToken tok = mat1[field];
                    if (tok != null && tok.Type != JTokenType.Null)
                        plannedFields.Add($"GreenMaterial_Mat1_{suffix}");
                }

                if (certifiedToken != null && certifiedToken.Type != JTokenType.Null)
                    plannedFields.Add("GreenMaterial_Certified");
                if (shadingToken != null && shadingToken.Type != JTokenType.Null)
                    plannedFields.Add("GreenMaterial_Window_ShadingCoefficient");
                if (acousticToken != null && acousticToken.Type != JTokenType.Null)
                    plannedFields.Add("GreenMaterial_AcousticRw");

                return new
                {
                    Success = true,
                    DryRun = true,
                    SourceTypeId = sourceTypeId,
                    SourceTypeName = sourceSymbol.Name,
                    SourceFamilyName = sourceFamily.Name,
                    Category = categoryName,
                    PlannedNewTypeName = newTypeName,
                    PlannedNewTypeNameCollisionBestEffort = typeNameCollisionBestEffort,
                    PlannedBackupFolder = plannedBackupFolder,
                    PlannedBackupFileNamePattern = plannedBackupFileNamePattern,
                    PlannedNewFamilyPath = plannedNewFamilyPath,
                    NewFamilyFileAlreadyExists = newFamilyFileAlreadyExists,
                    PlannedWrittenFields = plannedFields,
                    Message = "[dryRun] 完全未開啟家族文件（沒有呼叫 EditFamily/SaveAs/LoadFamily），也沒有建立備份資料夾，保證未寫出任何 .rfa 檔案（含備份檔）。僅回報路徑與欄位規劃"
                        + (typeNameCollisionBestEffort ? $"；警告：本專案文件內已有同名 Type '{newTypeName}'（best-effort 檢查，僅比對目前已載入本專案的 Type，家族檔案內部真正的 Type 清單需要實際執行才能確認）" : "")
                        + (newFamilyFileAlreadyExists ? $"；警告：目標新家族檔已存在 '{plannedNewFamilyPath}'，實際執行時會直接中止" : "")
                        + "；哪些欄位會因家族尚未綁定對應共享參數而列入 MissingParameters，需要實際執行（開啟家族文件）才能確定，dryRun 無法預先得知"
                };
            }

            // === 規則2：備份必須先於任何修改 ===
            if (string.IsNullOrWhiteSpace(backupFolder))
            {
                string docDir = string.IsNullOrEmpty(doc.PathName) ? null : Path.GetDirectoryName(doc.PathName);
                backupFolder = Path.Combine(string.IsNullOrEmpty(docDir) ? Path.GetTempPath() : docDir, "_rfa_backup");
            }
            Directory.CreateDirectory(backupFolder);

            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string safeFamilyName = SanitizeFileName(sourceFamily.Name);
            string backupPath = Path.Combine(backupFolder, $"{safeFamilyName}_backup_{timestamp}.rfa");

            Document famDoc = null;
            string newFamilyPath;
            List<string> written;
            List<string> missing;

            try
            {
                // EditFamily 不可在 Transaction 內呼叫（同 export_families 前例）
                famDoc = doc.EditFamily(sourceFamily);

                var backupOpts = new SaveAsOptions { OverwriteExistingFile = false };
                famDoc.SaveAs(backupPath, backupOpts);

                FamilyManager fm = famDoc.FamilyManager;
                FamilyType sourceFamType = fm.Types.Cast<FamilyType>().FirstOrDefault(t => t.Name == sourceSymbol.Name);
                if (sourceFamType == null)
                    throw new Exception($"在家族文件中找不到來源 Type '{sourceSymbol.Name}'");
                if (fm.Types.Cast<FamilyType>().Any(t => t.Name == newTypeName))
                    throw new Exception($"來源家族 '{sourceFamily.Name}' 內已存在同名 Type '{newTypeName}'，請換一個新 Type 名稱");

                var app = doc.Application;
                string originalSharedParamFile = app.SharedParametersFilename;
                DefinitionFile defFile;

                // 2026-08-31 根因修正（實測：Revit 2024 / Window-Fixed-Transom，12 個
                // GreenMaterial_* 共享參數全數新增失敗，錯誤皆為 autodesk.parameter.group:
                // data-1.0.0: Shared parameter creation failed.）：
                // FamilyManager.AddParameter(ExternalDefinition, ...) 需要 Application 當下
                // 仍開著「建立該 ExternalDefinition 時所用的」共享參數檔，才能解析這個定義。
                // 舊版把 app.SharedParametersFilename 的設定/還原包在一個只涵蓋
                // OpenSharedParameterFile() 的小 try/finally 裡，還原發生在所有 AddParameter
                // 呼叫（在 WriteFamilyGreenMaterialSlot / SetFamilySharedBoolParam /
                // SetFamilySharedNumberParam 內部）之前，導致 defFile 取得的 exDef 變成懸空
                // 參照。現在把設定的生命週期延長到涵蓋整個 Transaction（含 Commit）——
                // 比照同一支 codebase 內已實測成功 69/69 的對照組 load_shared_parameters
                // （CommandExecutor.GM_GreenMaterial.cs：設定→開檔→在同一個 try 內用到底→
                // finally 才還原）。defFile/exDef 在本方法內的最後一次使用是下方 Transaction
                // 區塊中的 SetFamilySharedNumberParam(..., "GreenMaterial_AcousticRw", ...)，
                // 因此還原點放在 Transaction 完整 Commit 之後。
                try
                {
                    app.SharedParametersFilename = sharedParamFilePath;
                    defFile = app.OpenSharedParameterFile();
                    if (defFile == null)
                        throw new Exception($"無法開啟共享參數檔: {sharedParamFilePath}");

                    written = new List<string>();
                    missing = new List<string>();

                    using (Transaction t = new Transaction(famDoc, $"新增綠建材 Type: {newTypeName}"))
                    {
                        t.Start();

                        fm.CurrentType = sourceFamType;
                        fm.NewType(newTypeName); // 只新增，絕不改動來源 Type（規則1/4）

                        if (identityData != null)
                        {
                            SetFamilyBuiltInText(fm, BuiltInParameter.ALL_MODEL_MANUFACTURER, identityData["manufacturer"]?.Value<string>(), written, missing, "IdentityData.Manufacturer");
                            SetFamilyBuiltInText(fm, BuiltInParameter.ALL_MODEL_MODEL, identityData["model"]?.Value<string>(), written, missing, "IdentityData.Model");
                            SetFamilyBuiltInText(fm, BuiltInParameter.ALL_MODEL_DESCRIPTION, identityData["description"]?.Value<string>(), written, missing, "IdentityData.Description");
                            SetFamilyBuiltInText(fm, BuiltInParameter.ALL_MODEL_URL, identityData["url"]?.Value<string>(), written, missing, "IdentityData.URL");
                        }

                        WriteFamilyGreenMaterialSlot(fm, defFile, "Mat1", mat1, written, missing);

                        if (certifiedToken != null && certifiedToken.Type != JTokenType.Null)
                            SetFamilySharedBoolParam(fm, defFile, "GreenMaterial_Certified", certifiedToken.Value<bool>(), written, missing);

                        if (shadingToken != null && shadingToken.Type != JTokenType.Null)
                            SetFamilySharedNumberParam(fm, defFile, "GreenMaterial_Window_ShadingCoefficient", shadingToken.Value<double>(), written, missing);
                        if (acousticToken != null && acousticToken.Type != JTokenType.Null)
                            SetFamilySharedNumberParam(fm, defFile, "GreenMaterial_AcousticRw", acousticToken.Value<double>(), written, missing);

                        t.Commit();
                    }
                }
                finally
                {
                    // 無條件還原，包含原本為空字串的情況——否則執行前若為空，Revit 的全域共享
                    // 參數檔設定會被永久改成 GreenMaterial_SharedParams.txt，造成非預期的環境異動。
                    // 還原點刻意放在涵蓋上方整個 try（含 Transaction Commit）之後，確保任何例外
                    // 路徑（OpenSharedParameterFile 失敗、AddParameter 拋例外、Commit 失敗等）
                    // 都會執行到這裡，同時保證還原不會發生在 defFile/exDef 最後一次使用之前。
                    app.SharedParametersFilename = originalSharedParamFile ?? string.Empty;
                }

                // === 規則4：另存為新家族檔名，迴避與來源家族同名的 LoadFamily 覆蓋歧義 ===
                string licno = mat1["certNo"]?.Value<string>();
                string familySuffixTag = string.IsNullOrWhiteSpace(licno) ? newFamilySuffix : $"{newFamilySuffix}_{SanitizeFileName(licno)}";
                string newFamilyFileName = $"{safeFamilyName}{familySuffixTag}.rfa";
                newFamilyPath = Path.Combine(backupFolder, newFamilyFileName);

                // 目標家族檔已存在時終止，不覆寫——SaveAs 的覆蓋安全比對只驗證 Type 參數值，
                // 無法偵測幾何、公式、巢狀元件等變更，靜默覆寫既有家族可能遺失這些內容。
                if (File.Exists(newFamilyPath))
                {
                    throw new Exception($"目標家族檔案已存在，為避免覆寫既有內容（幾何/公式/巢狀元件等變更無法被安全比對偵測）而中止：{newFamilyPath}。請更換 newFamilySuffix 或確認 mat1.certNo 以產生不同檔名後再重試。");
                }

                var saveOpts = new SaveAsOptions { OverwriteExistingFile = false };
                famDoc.SaveAs(newFamilyPath, saveOpts);

                famDoc.Close(false);
                famDoc = null;
            }
            finally
            {
                if (famDoc != null)
                {
                    try { famDoc.Close(false); } catch { }
                }
            }

            // === 規則4 強制驗證：載入前先快照本專案內同名家族（若存在）的既有 Type 參數簽章 ===
            string newFamilyName = Path.GetFileNameWithoutExtension(newFamilyPath);
            var beforeTypes = SnapshotFamilyTypeSignatures(doc, newFamilyName);

            Family loadedFamily = null;

            using (Transaction t2 = new Transaction(doc, $"載入綠建材門窗家族: {newFamilyName}"))
            {
                t2.Start();

                bool loaded = doc.LoadFamily(newFamilyPath, new GreenMaterialFamilyLoadOptions(), out loadedFamily);
                if (!loaded || loadedFamily == null)
                {
                    t2.RollBack();
                    throw new Exception($"LoadFamily 失敗: {newFamilyPath}");
                }

                var afterTypes = SnapshotFamilyTypeSignatures(doc, newFamilyName);
                var unexpectedChanges = new List<string>();
                foreach (var kv in beforeTypes)
                {
                    if (kv.Key == newTypeName) continue; // 這是這次要新增/更新的目標 Type，允許有值
                    if (!afterTypes.TryGetValue(kv.Key, out string afterSig) || afterSig != kv.Value)
                    {
                        unexpectedChanges.Add(kv.Key);
                    }
                }

                if (unexpectedChanges.Count > 0)
                {
                    t2.RollBack();
                    throw new Exception($"LoadFamily 會異動非目標 Type，已整批回滾、未載入：{string.Join(", ", unexpectedChanges)}");
                }

                ElementId newSymbolId = loadedFamily.GetFamilySymbolIds()
                    .FirstOrDefault(id => (doc.GetElement(id) as FamilySymbol)?.Name == newTypeName);

                if (newSymbolId == null || newSymbolId == ElementId.InvalidElementId)
                {
                    t2.RollBack();
                    throw new Exception($"家族已載入但找不到新 Type '{newTypeName}'，已回滾");
                }

                FamilySymbol newSymbol = doc.GetElement(newSymbolId) as FamilySymbol;
                if (newSymbol != null && !newSymbol.IsActive)
                {
                    newSymbol.Activate();
                }

                t2.Commit();

                var afterTypesFinal = SnapshotFamilyTypeSignatures(doc, newFamilyName);

                return new
                {
                    Success = true,
                    BackupPath = backupPath,
                    NewFamilyPath = newFamilyPath,
                    NewFamilyName = loadedFamily.Name,
                    NewTypeId = newSymbolId.GetIdValue(),
                    NewTypeName = newTypeName,
                    SourceTypeId = sourceTypeId,
                    SourceTypeName = sourceSymbol.Name,
                    SourceFamilyName = sourceFamily.Name,
                    Category = categoryName,
                    SiblingTypesBeforeLoad = beforeTypes.Count,
                    SiblingTypesAfterLoad = afterTypesFinal.Count,
                    AffectedExistingTypes = 0,
                    WrittenParameters = written,
                    MissingParameters = missing,
                    Message = missing.Count == 0
                        ? $"成功備份 '{sourceFamily.Name}' 至 '{backupPath}'，新增 Type '{newTypeName}' 並以獨立家族檔 '{newFamilyName}.rfa' 載回專案（Element ID {newSymbolId}），未覆蓋任何既有 Type。"
                        : $"成功載入，但 {missing.Count} 個參數找不到（可能該欄位不適用此類別，如 Door 案例的 GreenMaterial_Window_ShadingCoefficient）：{string.Join(", ", missing)}"
                };
            }
        }

        /// <summary>
        /// 快照專案內指定家族名稱下所有 Type 的參數簽章，用於載入前後比對是否有非目標 Type 被異動。
        /// 家族不存在（例如第一次執行、新家族名稱在專案內全新）時回傳空字典，這是預期狀況，不是錯誤。
        /// </summary>
        private Dictionary<string, string> SnapshotFamilyTypeSignatures(Document doc, string familyName)
        {
            var result = new Dictionary<string, string>();
            Family fam = new FilteredElementCollector(doc)
                .OfClass(typeof(Family))
                .Cast<Family>()
                .FirstOrDefault(f => f.Name == familyName);
            if (fam == null) return result;

            foreach (ElementId symId in fam.GetFamilySymbolIds())
            {
                FamilySymbol sym = doc.GetElement(symId) as FamilySymbol;
                if (sym == null) continue;
                result[sym.Name] = BuildParameterSignature(sym);
            }
            return result;
        }

        /// <summary>
        /// 把一個 Element 的全部參數值串成一個字串，僅用於前後比對是否有變動，不作其他用途。
        /// </summary>
        private string BuildParameterSignature(Element elem)
        {
            var sb = new System.Text.StringBuilder();
            foreach (Parameter p in elem.Parameters)
            {
                string val;
                switch (p.StorageType)
                {
                    case StorageType.String: val = p.AsString(); break;
                    case StorageType.Double: val = p.AsDouble().ToString("F6"); break;
                    case StorageType.Integer: val = p.AsInteger().ToString(); break;
                    case StorageType.ElementId: val = p.AsElementId()?.GetIdValue().ToString(); break;
                    default: val = ""; break;
                }
                sb.Append(p.Id.GetIdValue()).Append('=').Append(val).Append(';');
            }
            return sb.ToString();
        }

        private void SetFamilyBuiltInText(FamilyManager fm, BuiltInParameter bip, string value, List<string> written, List<string> missing, string label)
        {
            if (string.IsNullOrEmpty(value)) return;
            FamilyParameter fp;
            try { fp = fm.get_Parameter(bip); } catch { fp = null; }
            if (fp == null || fp.IsReadOnly) { missing.Add(label); return; }
            fm.Set(fp, value);
            written.Add(label);
        }

        private void WriteFamilyGreenMaterialSlot(FamilyManager fm, DefinitionFile defFile, string slot, JObject mat, List<string> written, List<string> missing)
        {
            if (mat == null) return;

            void SetText(string field, string suffix)
            {
                JToken token = mat[field];
                if (token == null || token.Type == JTokenType.Null) return;
                SetFamilySharedTextParam(fm, defFile, $"GreenMaterial_{slot}_{suffix}", token.Value<string>(), written, missing);
            }

            void SetNum(string field, string suffix)
            {
                JToken token = mat[field];
                if (token == null || token.Type == JTokenType.Null) return;
                SetFamilySharedNumberParam(fm, defFile, $"GreenMaterial_{slot}_{suffix}", token.Value<double>(), written, missing);
            }

            SetText("name", "Name");
            SetText("certNo", "CertNo");
            SetText("category", "Category");
            SetText("subCategory", "SubCategory");
            SetText("applicant", "Applicant");
            SetText("validUntil", "ValidUntil");
            SetNum("tvoc", "TVOC");
            SetNum("formaldehyde", "Formaldehyde");
            SetText("cnsSpec", "CNSSpec");
            SetText("testItems", "TestItems");
            SetText("qualifiedItems", "QualifiedItems");
        }

        private FamilyParameter GetOrCreateFamilyParameter(FamilyManager fm, DefinitionFile defFile, string paramName, List<string> missing)
        {
            FamilyParameter fp;
            try { fp = fm.get_Parameter(paramName); } catch { fp = null; }
            if (fp != null) return fp;

            ExternalDefinition exDef = FindExternalDefinition(defFile, paramName);
            if (exDef == null) { missing.Add($"{paramName}（共享參數檔內找不到此定義，檢查 sharedParamFilePath 是否正確）"); return null; }

            // FamilyManager.AddParameter(ExternalDefinition, ForgeTypeId, bool) — 這個簽章在
            // Revit 2022~2026 API 一致存在（舊版 NewFamilyParameter 名稱與 BuiltInParameterGroup
            // 多載已由 AddParameter + GroupTypeId 取代），不需要版本分支。
            // 2026-08-13：GreenMaterial_Certified（YESNO）在 IdentityData 群組下 AddParameter 會失敗
            // （實測案例：Window 家族「雙開落地窗- (2)_TABC_GBM0104092」），但同一支 API 對 TEXT/NUMBER
            // 型別的 Mat1_* 欄位在同一群組下都能成功——當時記錄為「原因不明」。
            // 2026-08-31 追查出根因（實測：Revit 2024 / Window-Fixed-Transom，12 個
            // GreenMaterial_* 全數失敗，錯誤皆為 autodesk.parameter.group:data-1.0.0:
            // Shared parameter creation failed.）：呼叫端（InjectGreenMaterialIntoFamily）
            // 原本把 app.SharedParametersFilename 的還原點放在所有 AddParameter 呼叫「之前」，
            // 這裡的 exDef 因此是懸空參照，AddParameter 對懸空 ExternalDefinition 只回傳通用
            // 錯誤訊息，不是欄位型別或群組本身的問題。此 bug 是環境相依的——若呼叫端機器上
            // app.SharedParametersFilename 原本就已經是同一份共享參數檔，「還原」等於沒還原，
            // 多數欄位仍會成功，只有少數欄位（依 Revit 內部快取失效時機而定）失敗，這正是
            // 2026-08-13 觀察到「只有 YESNO 失敗、TEXT/NUMBER 都成功」的原因。呼叫端已在
            // 2026-08-31 修正共享參數檔設定的生命週期（延長到涵蓋整個 Transaction）。
            // 這裡的 IdentityData → Data 雙群組 fallback 與根因無關，但本身無害，在根因修好後
            // 仍保留作為額外防禦（例如某些家族類別確實不接受 IdentityData 群組下的特定欄位）。
            string lastError = null;
            foreach (var groupId in new[] { GroupTypeId.IdentityData, GroupTypeId.Data })
            {
                try
                {
                    fp = fm.AddParameter(exDef, groupId, false);
                    if (fp != null) return fp;
                }
                catch (Exception ex)
                {
                    lastError = $"{groupId.TypeId}: {ex.Message}";
                }
            }

            missing.Add(lastError != null ? $"{paramName}（新增失敗，最後錯誤：{lastError}）" : paramName);
            return null;
        }

        private ExternalDefinition FindExternalDefinition(DefinitionFile defFile, string paramName)
        {
            foreach (DefinitionGroup grp in defFile.Groups)
            {
                foreach (Definition def in grp.Definitions)
                {
                    if (def.Name == paramName) return def as ExternalDefinition;
                }
            }
            return null;
        }

        private void SetFamilySharedTextParam(FamilyManager fm, DefinitionFile defFile, string paramName, string value, List<string> written, List<string> missing)
        {
            FamilyParameter fp = GetOrCreateFamilyParameter(fm, defFile, paramName, missing);
            if (fp == null) return;
            if (fp.IsReadOnly) { missing.Add(paramName); return; }
            fm.Set(fp, value ?? "");
            written.Add(paramName);
        }

        private void SetFamilySharedNumberParam(FamilyManager fm, DefinitionFile defFile, string paramName, double value, List<string> written, List<string> missing)
        {
            FamilyParameter fp = GetOrCreateFamilyParameter(fm, defFile, paramName, missing);
            if (fp == null) return;
            if (fp.IsReadOnly) { missing.Add(paramName); return; }
            fm.Set(fp, value);
            written.Add(paramName);
        }

        private void SetFamilySharedBoolParam(FamilyManager fm, DefinitionFile defFile, string paramName, bool value, List<string> written, List<string> missing)
        {
            FamilyParameter fp = GetOrCreateFamilyParameter(fm, defFile, paramName, missing);
            if (fp == null) return;
            if (fp.IsReadOnly) { missing.Add(paramName); return; }
            fm.Set(fp, value ? 1 : 0);
            written.Add(paramName);
        }

        #endregion
    }
}
