using System;
using System.Collections.Generic;
using System.IO;
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
        private object ListOpenDocuments()
        {
            Document activeDocument = _uiApp.ActiveUIDocument?.Document;
            var documents = _uiApp.Application.Documents
                .Cast<Document>()
                .Where(doc => !doc.IsLinked)
                .Select(doc => new
                {
                    Title = doc.Title,
                    PathName = doc.PathName,
                    IsActive = ReferenceEquals(doc, activeDocument),
                    IsFamilyDocument = doc.IsFamilyDocument,
                    IsWorkshared = doc.IsWorkshared,
                    IsModified = doc.IsModified
                })
                .ToList();

            return new
            {
                Count = documents.Count,
                ActiveDocumentTitle = activeDocument?.Title,
                Documents = documents
            };
        }

        private object OpenDocument(JObject parameters)
        {
            string filePath = parameters["filePath"]?.Value<string>();
            bool detachFromCentral = parameters["detachFromCentral"]?.Value<bool>() ?? false;

            if (string.IsNullOrWhiteSpace(filePath))
                throw new Exception("必須提供 filePath。" );

            if (!File.Exists(filePath))
                throw new Exception($"找不到要開啟的 Revit 文件：{filePath}");

            Document activeDocument = _uiApp.ActiveUIDocument?.Document;
            foreach (Document document in _uiApp.Application.Documents)
            {
                if (!document.IsLinked && string.Equals(document.PathName, filePath, StringComparison.OrdinalIgnoreCase))
                {
                    return new
                    {
                        Success = true,
                        AlreadyOpen = true,
                        IsActive = ReferenceEquals(document, activeDocument),
                        Title = document.Title,
                        Message = "文件已在目前 Revit 進程中開啟。Revit API 無法用程式切換已開啟文件的作用視窗，請使用者手動點選該文件視窗。"
                    };
                }
            }

            ModelPath modelPath = ModelPathUtils.ConvertUserVisiblePathToModelPath(filePath);
            OpenOptions openOpts = new OpenOptions();
            if (detachFromCentral)
                openOpts.DetachFromCentralOption = DetachFromCentralOption.DetachAndPreserveWorksets;

            UIDocument openedUiDocument = _uiApp.OpenAndActivateDocument(modelPath, openOpts, false);
            Document openedDocument = openedUiDocument?.Document;
            if (openedDocument == null)
                throw new Exception($"Revit 未能開啟文件：{filePath}");

            return new
            {
                Success = true,
                AlreadyOpen = false,
                Title = openedDocument.Title,
                PathName = openedDocument.PathName,
                IsActive = ReferenceEquals(openedDocument, _uiApp.ActiveUIDocument?.Document),
                Message = "已開啟並設為作用文件。"
            };
        }

        private object SaveDocument(JObject parameters)
        {
            string documentTitle = parameters["documentTitle"]?.Value<string>();
            var documents = _uiApp.Application.Documents
                .Cast<Document>()
                .Where(doc => !doc.IsLinked)
                .ToList();

            Document document;
            if (string.IsNullOrWhiteSpace(documentTitle))
            {
                document = _uiApp.ActiveUIDocument?.Document;
                if (document == null)
                    throw new Exception("目前沒有作用中的 Revit 文件可儲存。");
            }
            else
            {
                document = documents.FirstOrDefault(doc => doc.Title == documentTitle);
                if (document == null)
                {
                    string openTitles = documents.Count == 0
                        ? "（無）"
                        : string.Join("、", documents.Select(doc => doc.Title));
                    throw new Exception($"找不到標題為「{documentTitle}」的已開啟文件。已開啟文件：{openTitles}");
                }
            }

            if (document.IsReadOnly)
                throw new Exception($"文件「{document.Title}」為唯讀，無法儲存。");

            document.Save();

            return new
            {
                Success = true,
                Title = document.Title,
                PathName = document.PathName,
                Message = "文件已儲存。"
            };
        }

        private object ReloadLinks(JObject parameters)
        {
            Document document = _uiApp.ActiveUIDocument?.Document;
            if (document == null)
                throw new Exception("目前沒有作用中的 Revit 文件可重載連結。");

            var linkTypeIds = new HashSet<IdType>(
                (parameters["linkTypeIds"] as JArray ?? new JArray())
                    .Select(token => token.Value<IdType>()));
            var linkNames = new HashSet<string>(
                (parameters["linkNames"] as JArray ?? new JArray())
                    .Select(token => token.Value<string>())
                    .Where(name => !string.IsNullOrWhiteSpace(name)),
                StringComparer.OrdinalIgnoreCase);
            bool reloadAll = linkTypeIds.Count == 0 && linkNames.Count == 0;

            var reloaded = new List<object>();
            var failed = new List<object>();
            var linkTypes = new FilteredElementCollector(document)
                .OfClass(typeof(RevitLinkType))
                .Cast<RevitLinkType>()
                .Where(linkType => reloadAll
                    || linkTypeIds.Contains(linkType.Id.GetIdValue())
                    || linkNames.Contains(linkType.Name));

            foreach (RevitLinkType linkType in linkTypes)
            {
                try
                {
                    linkType.Reload();
                    reloaded.Add(new { Id = linkType.Id.GetIdValue(), Name = linkType.Name });
                }
                catch (Exception ex)
                {
                    failed.Add(new { Id = linkType.Id.GetIdValue(), Name = linkType.Name, Error = ex.Message });
                }
            }

            return new
            {
                Success = failed.Count == 0,
                ReloadedCount = reloaded.Count,
                FailedCount = failed.Count,
                Reloaded = reloaded,
                Failed = failed
            };
        }

        private object CloseDocument(JObject parameters)
        {
            string documentTitle = parameters["documentTitle"]?.Value<string>();
            bool save = parameters["save"]?.Value<bool>() ?? false;
            bool discardChanges = parameters["discardChanges"]?.Value<bool>() ?? false;

            if (string.IsNullOrWhiteSpace(documentTitle))
                throw new Exception("必須提供 documentTitle。");

            var allDocuments = _uiApp.Application.Documents.Cast<Document>().ToList();
            Document document = allDocuments.FirstOrDefault(doc => doc.Title == documentTitle);

            if (document == null)
            {
                var nonLinked = allDocuments.Where(doc => !doc.IsLinked).ToList();
                string openTitles = nonLinked.Count == 0
                    ? "（無）"
                    : string.Join("、", nonLinked.Select(doc => doc.Title));
                throw new Exception($"找不到標題為「{documentTitle}」的已開啟文件。已開啟的非連結文件：{openTitles}");
            }

            if (document.IsLinked)
                throw new Exception($"「{documentTitle}」是連結文件，無法用此工具直接關閉；連結文件會隨宿主文件一併關閉，若需重新載入請改用 reload_links，或在宿主文件的連結管理中處理。");

            Document activeDocument = _uiApp.ActiveUIDocument?.Document;
            if (ReferenceEquals(document, activeDocument))
                throw new Exception($"「{documentTitle}」是目前作用中的文件，Revit API 無法關閉作用文件（Document.Close 對作用文件會擲例外）。請先用 open_document 開啟或切換至另一份文件，或請使用者手動切換視窗後再重試關閉。");

            bool saved = false;
            if (document.IsModified)
            {
                if (save)
                {
                    if (document.IsReadOnly)
                        throw new Exception($"文件「{documentTitle}」為唯讀，無法儲存；如需放棄變更關閉，請改用 discardChanges: true。");
                    document.Save();
                    saved = true;
                }
                else if (!discardChanges)
                {
                    throw new Exception($"文件「{documentTitle}」有未儲存的修改。請指定 save: true 先儲存後關閉，或 discardChanges: true 放棄變更強制關閉。");
                }
            }
            else if (save && !document.IsReadOnly)
            {
                document.Save();
                saved = true;
            }

            string title = document.Title;
            string pathName = document.PathName;

            document.Close(false);

            return new
            {
                Success = true,
                Title = title,
                PathName = pathName,
                Saved = saved,
                Message = saved ? "文件已儲存並關閉。" : "文件已關閉（未儲存變更）。"
            };
        }
    }
}
