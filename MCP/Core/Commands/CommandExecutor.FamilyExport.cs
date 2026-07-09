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
        #region 族群匯出 (export_families)

        /// <summary>
        /// 把專案中已載入的可編輯族群另存為 .rfa 到指定資料夾，建立可重用元件庫。
        /// 預設匯出管配件(OST_PipeFitting)與管附件(OST_PipeAccessory)。
        /// 走 Document.EditFamily → Document.SaveAs → Close(false)；EditFamily 不可在 Transaction 內呼叫，故全程不開主文件交易。
        /// </summary>
        private object ExportFamilies(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;

            string outputFolder = parameters["outputFolder"]?.Value<string>();
            if (string.IsNullOrWhiteSpace(outputFolder))
            {
                throw new Exception("請指定 outputFolder（輸出根資料夾絕對路徑）");
            }

            bool subFolderBySeries = parameters["subFolderBySeries"]?.Value<bool>() ?? false;
            bool overwrite = parameters["overwrite"]?.Value<bool>() ?? true;

            // 解析目標類別；省略則預設管配件 + 管附件
            var targetCatIds = new HashSet<IdType>();
            var catArray = parameters["categories"] as JArray;
            if (catArray != null && catArray.Count > 0)
            {
                foreach (var token in catArray)
                {
                    string name = token?.Value<string>();
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    if (Enum.TryParse(name, out BuiltInCategory bic))
                    {
                        targetCatIds.Add((IdType)bic);
                    }
                    else
                    {
                        throw new Exception($"無法辨識的 BuiltInCategory: {name}");
                    }
                }
            }
            else
            {
                targetCatIds.Add((IdType)BuiltInCategory.OST_PipeFitting);
                targetCatIds.Add((IdType)BuiltInCategory.OST_PipeAccessory);
            }

            // 收集符合類別、可編輯、非現地的族群
            var families = new FilteredElementCollector(doc)
                .OfClass(typeof(Family))
                .Cast<Family>()
                .Where(f => f.FamilyCategory != null && targetCatIds.Contains(f.FamilyCategory.Id.GetIdValue()))
                .OrderBy(f => f.FamilyCategory.Name)
                .ThenBy(f => f.Name)
                .ToList();

            Directory.CreateDirectory(outputFolder);

            var saved = new List<object>();
            var skipped = new List<object>();
            var errors = new List<object>();

            foreach (Family fam in families)
            {
                string catName = SanitizeFileName(fam.FamilyCategory.Name);

                if (fam.IsInPlace || !fam.IsEditable)
                {
                    skipped.Add(new
                    {
                        FamilyName = fam.Name,
                        Category = fam.FamilyCategory.Name,
                        Reason = fam.IsInPlace ? "現地(in-place)族群,無法另存" : "不可編輯(系統族群)"
                    });
                    continue;
                }

                Document famDoc = null;
                try
                {
                    famDoc = doc.EditFamily(fam);

                    string dir = Path.Combine(outputFolder, catName);
                    if (subFolderBySeries)
                    {
                        dir = Path.Combine(dir, SanitizeFileName(FamilySeries(fam.Name)));
                    }
                    Directory.CreateDirectory(dir);

                    string filePath = Path.Combine(dir, SanitizeFileName(fam.Name) + ".rfa");
                    var opts = new SaveAsOptions { OverwriteExistingFile = overwrite };
                    famDoc.SaveAs(filePath, opts);

                    saved.Add(new
                    {
                        FamilyName = fam.Name,
                        Category = fam.FamilyCategory.Name,
                        Path = filePath
                    });
                }
                catch (Exception ex)
                {
                    errors.Add(new
                    {
                        FamilyName = fam.Name,
                        Category = fam.FamilyCategory.Name,
                        Error = ex.Message
                    });
                }
                finally
                {
                    if (famDoc != null)
                    {
                        try { famDoc.Close(false); } catch { }
                    }
                }
            }

            return new
            {
                Success = true,
                OutputFolder = outputFolder,
                MatchedCount = families.Count,
                SavedCount = saved.Count,
                SkippedCount = skipped.Count,
                ErrorCount = errors.Count,
                Saved = saved,
                Skipped = skipped,
                Errors = errors,
                Message = $"匯出完成：{saved.Count} 個族群已存到 {outputFolder}" +
                          (skipped.Count > 0 ? $"，略過 {skipped.Count} 個" : "") +
                          (errors.Count > 0 ? $"，失敗 {errors.Count} 個" : "")
            };
        }

        /// <summary>
        /// 依族群名稱推導系列子資料夾名：M_ 開頭歸 Revit通用；含 '-' 取第一段；否則用整名。
        /// </summary>
        private static string FamilySeries(string famName)
        {
            if (string.IsNullOrEmpty(famName)) return "其他";
            if (famName.StartsWith("M_")) return "Revit通用";
            int dash = famName.IndexOf('-');
            if (dash > 0) return famName.Substring(0, dash).Trim();
            return famName.Trim();
        }

        /// <summary>
        /// 把字串清成合法檔名/資料夾名（替換非法字元為底線）。
        /// </summary>
        private static string SanitizeFileName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "_";
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }
            return name.Trim();
        }

        #endregion
    }
}
