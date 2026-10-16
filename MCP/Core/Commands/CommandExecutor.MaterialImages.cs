using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Visual;
using Newtonsoft.Json.Linq;

#if REVIT2025_OR_GREATER
using IdType = System.Int64;
#else
using IdType = System.Int32;
#endif

namespace RevitMCP.Core
{
    /// <summary>
    /// 材質外觀影像重連：以完整檔名在指定資料夾樹中尋找遺失的 Unified Bitmap。
    /// 方法規則以 domain/material-image-relinking.md 為準。
    /// </summary>
    public partial class CommandExecutor
    {
        private const int MaterialImageMaxAssetDepth = 64;
        private const string MaterialBitmapPropertyName = "unifiedbitmap_Bitmap";

        private sealed class MaterialImageAssetGroup
        {
            public ElementId AssetId { get; set; }
            public AppearanceAssetElement AppearanceAsset { get; set; }
            public List<Material> Materials { get; } = new List<Material>();
        }

        private sealed class MaterialImageFolderError
        {
            public string Folder { get; set; }
            public string Reason { get; set; }
        }

        private object RelinkMaterialImages(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;
            string rawFolderPath = parameters["folderPath"]?.Value<string>();
            bool dryRun = parameters["dryRun"]?.Value<bool>() ?? false;

            if (doc.IsReadOnly)
                throw new Exception("目前作用中的 Revit 文件是唯讀，無法執行材料影像重連。");
            if (string.IsNullOrWhiteSpace(rawFolderPath))
                throw new Exception("必須提供 folderPath。請先請使用者貼上新的材料影像根資料夾路徑。");

            string folderPath;
            try
            {
                folderPath = Path.GetFullPath(rawFolderPath.Trim().Trim('"'));
            }
            catch (Exception ex)
            {
                throw new Exception($"folderPath 不是有效的 Windows 路徑：{ex.Message}");
            }

            if (!Directory.Exists(folderPath))
                throw new DirectoryNotFoundException($"找不到材料影像資料夾：{folderPath}");

            var folderErrors = new List<MaterialImageFolderError>();
            Dictionary<string, List<string>> filenameIndex = BuildMaterialImageFilenameIndex(folderPath, folderErrors);

            List<Material> materials = new FilteredElementCollector(doc)
                .OfClass(typeof(Material))
                .Cast<Material>()
                .ToList();

            var groupedAssets = new Dictionary<IdType, MaterialImageAssetGroup>();
            var materialsWithoutAppearanceAsset = new List<object>();
            IdType invalidId = ElementId.InvalidElementId.GetIdValue();

            foreach (Material material in materials)
            {
                ElementId appearanceId = material.AppearanceAssetId;
                if (appearanceId == null || appearanceId.GetIdValue() == invalidId)
                {
                    materialsWithoutAppearanceAsset.Add(new
                    {
                        Material = material.Name,
                        MaterialId = material.Id.GetIdValue()
                    });
                    continue;
                }

                AppearanceAssetElement appearance = doc.GetElement(appearanceId) as AppearanceAssetElement;
                if (appearance == null)
                {
                    materialsWithoutAppearanceAsset.Add(new
                    {
                        Material = material.Name,
                        MaterialId = material.Id.GetIdValue()
                    });
                    continue;
                }

                IdType key = appearanceId.GetIdValue();
                if (!groupedAssets.TryGetValue(key, out MaterialImageAssetGroup group))
                {
                    group = new MaterialImageAssetGroup
                    {
                        AssetId = appearanceId,
                        AppearanceAsset = appearance
                    };
                    groupedAssets.Add(key, group);
                }
                group.Materials.Add(material);
            }

            var repaired = new List<object>();
            var valid = new List<object>();
            var unresolved = new List<object>();
            var conflicts = new List<object>();
            var writeFailures = new List<object>();
            var assetFailures = new List<object>();

            // IsValidValue() 只能對 AppearanceAssetEditScope.Start() 回傳的可編輯 Asset
            // 呼叫。因此 dry-run 也必須進入交易與 edit scope；差別是它不設定 Value，
            // 並在每個 Asset 及外層交易都 rollback，保證不留下模型變更。
            using (Transaction transaction = new Transaction(
                doc,
                dryRun ? "預覽材料影像重連" : "重新連結材料影像"))
            {
                transaction.Start();
                try
                {
                    foreach (MaterialImageAssetGroup group in groupedAssets.Values.OrderBy(g => g.AssetId.GetIdValue()))
                    {
                        var localRepaired = new List<object>();
                        var localValid = new List<object>();
                        var localUnresolved = new List<object>();
                        var localConflicts = new List<object>();
                        var localWriteFailures = new List<object>();
                        bool changed = false;

                        using (SubTransaction subTransaction = new SubTransaction(doc))
                        using (AppearanceAssetEditScope editScope = new AppearanceAssetEditScope(doc))
                        {
                            subTransaction.Start();
                            try
                            {
                                Asset editableAsset = editScope.Start(group.AssetId);
                                changed = ClassifyMaterialImageProperties(
                                    doc,
                                    group,
                                    editableAsset,
                                    filenameIndex,
                                    !dryRun,
                                    localRepaired,
                                    localValid,
                                    localUnresolved,
                                    localConflicts,
                                    localWriteFailures);

                                if (dryRun || !changed)
                                {
                                    if (editScope.IsActive)
                                        editScope.Cancel();
                                    subTransaction.RollBack();
                                }
                                else
                                {
                                    editScope.Commit(true);
                                    subTransaction.Commit();
                                }

                                repaired.AddRange(localRepaired);
                                valid.AddRange(localValid);
                                unresolved.AddRange(localUnresolved);
                                conflicts.AddRange(localConflicts);
                                writeFailures.AddRange(localWriteFailures);
                            }
                            catch (Exception ex)
                            {
                                if (editScope.IsActive)
                                    editScope.Cancel();
                                if (subTransaction.GetStatus() == TransactionStatus.Started)
                                    subTransaction.RollBack();
                                assetFailures.Add(CreateMaterialImageAssetFailure(group, ex));
                            }
                        }
                    }

                    if (dryRun)
                        transaction.RollBack();
                    else
                        transaction.Commit();
                }
                catch
                {
                    if (transaction.GetStatus() == TransactionStatus.Started)
                        transaction.RollBack();
                    throw;
                }
            }

            return new
            {
                Success = true,
                DryRun = dryRun,
                DocumentTitle = doc.Title,
                FolderPath = folderPath,
                Summary = new
                {
                    MaterialsScanned = materials.Count,
                    AppearanceAssetsScanned = groupedAssets.Count,
                    LinksRepaired = repaired.Count,
                    MissingFilesNotFound = unresolved.Count,
                    DuplicateFilenameConflicts = conflicts.Count,
                    ValidLinksPreserved = valid.Count,
                    MaterialsWithoutAppearanceAssets = materialsWithoutAppearanceAsset.Count,
                    WriteFailures = writeFailures.Count,
                    AssetFailures = assetFailures.Count,
                    UnreadableFolders = folderErrors.Count
                },
                Repaired = repaired,
                Valid = valid,
                Unresolved = unresolved,
                Conflicts = conflicts,
                MaterialsWithoutAppearanceAsset = materialsWithoutAppearanceAsset,
                WriteFailures = writeFailures,
                AssetFailures = assetFailures,
                FolderErrors = folderErrors,
                Message = dryRun
                    ? $"dryRun 完成：找到 {repaired.Count} 個可重連影像，未修改模型。"
                    : $"已重新連結 {repaired.Count} 個遺失的材料影像。"
            };
        }

        private Dictionary<string, List<string>> BuildMaterialImageFilenameIndex(
            string root,
            List<MaterialImageFolderError> errors)
        {
            var index = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var pending = new Stack<string>();
            pending.Push(root);

            while (pending.Count > 0)
            {
                string directory = pending.Pop();
                string[] files;
                try
                {
                    files = Directory.GetFiles(directory);
                }
                catch (Exception ex)
                {
                    errors.Add(new MaterialImageFolderError { Folder = directory, Reason = ex.Message });
                    continue;
                }

                foreach (string file in files.OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
                {
                    string filename = Path.GetFileName(file);
                    if (string.IsNullOrWhiteSpace(filename))
                        continue;
                    string fullPath = Path.GetFullPath(file);
                    if (!index.TryGetValue(filename, out List<string> paths))
                    {
                        paths = new List<string>();
                        index.Add(filename, paths);
                    }
                    paths.Add(fullPath);
                }

                string[] childDirectories;
                try
                {
                    childDirectories = Directory.GetDirectories(directory);
                }
                catch (Exception ex)
                {
                    errors.Add(new MaterialImageFolderError { Folder = directory, Reason = ex.Message });
                    continue;
                }

                foreach (string child in childDirectories.OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase))
                {
                    try
                    {
                        if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0)
                            continue;
                        pending.Push(child);
                    }
                    catch (Exception ex)
                    {
                        errors.Add(new MaterialImageFolderError { Folder = child, Reason = ex.Message });
                    }
                }
            }

            foreach (List<string> paths in index.Values)
                paths.Sort(StringComparer.OrdinalIgnoreCase);
            return index;
        }

        private bool ClassifyMaterialImageProperties(
            Document doc,
            MaterialImageAssetGroup group,
            Asset asset,
            Dictionary<string, List<string>> filenameIndex,
            bool writeChanges,
            List<object> repaired,
            List<object> valid,
            List<object> unresolved,
            List<object> conflicts,
            List<object> writeFailures)
        {
            bool changed = false;
            string materialNames = GetMaterialImageGroupLabel(group);
            IdType assetId = group.AssetId.GetIdValue();

            foreach (AssetPropertyString bitmap in WalkMaterialBitmapProperties(asset))
            {
                string oldPath = bitmap.Value ?? string.Empty;
                if (string.IsNullOrWhiteSpace(oldPath))
                    continue;

                string existingPath = ResolveExistingMaterialImagePath(oldPath, doc.PathName);
                if (existingPath != null)
                {
                    valid.Add(new
                    {
                        Materials = materialNames,
                        AppearanceAssetId = assetId,
                        StoredPath = oldPath,
                        ResolvedPath = existingPath
                    });
                    continue;
                }

                string filename = Path.GetFileName(oldPath.Trim().Trim('"').Replace('/', '\\'));
                if (string.IsNullOrWhiteSpace(filename) || !filenameIndex.TryGetValue(filename, out List<string> candidates))
                {
                    unresolved.Add(new
                    {
                        Materials = materialNames,
                        AppearanceAssetId = assetId,
                        OldPath = oldPath,
                        Filename = filename ?? string.Empty
                    });
                    continue;
                }

                if (candidates.Count > 1)
                {
                    conflicts.Add(new
                    {
                        Materials = materialNames,
                        AppearanceAssetId = assetId,
                        OldPath = oldPath,
                        Candidates = candidates.ToArray()
                    });
                    continue;
                }

                string replacement = candidates[0];
                if (!bitmap.IsValidValue(replacement))
                {
                    writeFailures.Add(new
                    {
                        Materials = materialNames,
                        AppearanceAssetId = assetId,
                        OldPath = oldPath,
                        Reason = $"Revit 拒絕路徑：{replacement}"
                    });
                    continue;
                }

                if (writeChanges)
                    bitmap.Value = replacement;
                repaired.Add(new
                {
                    Materials = materialNames,
                    AppearanceAssetId = assetId,
                    OldPath = oldPath,
                    NewPath = replacement
                });
                changed = true;
            }

            return changed;
        }

        private IEnumerable<AssetPropertyString> WalkMaterialBitmapProperties(Asset asset)
        {
            foreach (AssetProperty property in WalkMaterialAsset(asset, 0))
            {
                AssetPropertyString stringProperty = property as AssetPropertyString;
                if (stringProperty != null && string.Equals(
                    stringProperty.Name,
                    MaterialBitmapPropertyName,
                    StringComparison.Ordinal))
                {
                    yield return stringProperty;
                }
            }
        }

        private IEnumerable<AssetProperty> WalkMaterialAsset(Asset asset, int depth)
        {
            if (asset == null)
                yield break;
            if (depth > MaterialImageMaxAssetDepth)
                throw new Exception($"Appearance Asset 圖形超過 {MaterialImageMaxAssetDepth} 層。");

            for (int index = 0; index < asset.Size; index++)
            {
                AssetProperty property = asset[index];
                foreach (AssetProperty nested in WalkMaterialAssetProperty(property, depth + 1))
                    yield return nested;
            }
        }

        private IEnumerable<AssetProperty> WalkMaterialAssetProperty(AssetProperty property, int depth)
        {
            if (property == null)
                yield break;
            if (depth > MaterialImageMaxAssetDepth)
                throw new Exception($"Appearance Asset 圖形超過 {MaterialImageMaxAssetDepth} 層。");

            yield return property;

            Asset nestedAsset = property as Asset;
            if (nestedAsset != null)
            {
                foreach (AssetProperty nested in WalkMaterialAsset(nestedAsset, depth + 1))
                    yield return nested;
            }

            for (int index = 0; index < property.NumberOfConnectedProperties; index++)
            {
                AssetProperty connected = property.GetConnectedProperty(index);
                foreach (AssetProperty nested in WalkMaterialAssetProperty(connected, depth + 1))
                    yield return nested;
            }
        }

        private string ResolveExistingMaterialImagePath(string storedPath, string projectPath)
        {
            string value = storedPath.Trim().Trim('"');
            try
            {
                if (Path.IsPathRooted(value))
                    return File.Exists(value) ? Path.GetFullPath(value) : null;

                if (string.IsNullOrWhiteSpace(projectPath))
                    return null;
                string projectDirectory = Path.GetDirectoryName(Path.GetFullPath(projectPath));
                if (string.IsNullOrWhiteSpace(projectDirectory))
                    return null;
                string candidate = Path.GetFullPath(Path.Combine(projectDirectory, value));
                return File.Exists(candidate) ? candidate : null;
            }
            catch
            {
                return null;
            }
        }

        private string GetMaterialImageGroupLabel(MaterialImageAssetGroup group)
        {
            return string.Join(
                "; ",
                group.Materials
                    .Select(material => material.Name ?? string.Empty)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase));
        }

        private object CreateMaterialImageAssetFailure(MaterialImageAssetGroup group, Exception error)
        {
            return new
            {
                Materials = GetMaterialImageGroupLabel(group),
                AppearanceAssetId = group.AssetId.GetIdValue(),
                Reason = error.Message
            };
        }
    }
}
