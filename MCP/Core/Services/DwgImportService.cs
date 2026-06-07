using System;
using System.IO;
using Autodesk.Revit.DB;
using RevitMCP.Models;

namespace RevitMCP.Core.Services
{
    internal class DwgImportService
    {
        public DwgImportResult Import(Document doc, string dwgPath, Level level, View view, DwgImportSettings settings)
        {
            if (string.IsNullOrWhiteSpace(dwgPath))
            {
                throw new ArgumentException("dwgPath 不可為空。", nameof(dwgPath));
            }

            if (!File.Exists(dwgPath))
            {
                throw new FileNotFoundException("找不到 DWG 檔案。", dwgPath);
            }

            if (settings.ThisViewOnly && view.ViewType == ViewType.ThreeD)
            {
                throw new InvalidOperationException("ThisViewOnly=true 時，目標 View 不可為 3D View。");
            }

            DWGImportOptions options = new DWGImportOptions
            {
                Placement = ImportPlacement.Origin,
                ThisViewOnly = settings.ThisViewOnly,
                Unit = settings.Unit,
                VisibleLayersOnly = settings.VisibleLayersOnly,
                ColorMode = settings.ColorMode
            };

            ElementId importedElementId;
            bool imported = doc.Import(dwgPath, options, view, out importedElementId);
            Element? importedElement = importedElementId == null ? null : doc.GetElement(importedElementId);

            bool pinned = false;
            if (imported && importedElement != null && settings.PinAfterLoad)
            {
                importedElement.Pinned = true;
                pinned = importedElement.Pinned;
            }

            return new DwgImportResult
            {
                DwgPath = dwgPath,
                LevelName = level?.Name ?? string.Empty,
                LevelId = level == null ? 0 : Convert.ToInt64(level.Id.GetIdValue()),
                ViewName = view?.Name ?? string.Empty,
                ViewId = view == null ? 0 : Convert.ToInt64(view.Id.GetIdValue()),
                ElementId = importedElementId == null ? 0 : Convert.ToInt64(importedElementId.GetIdValue()),
                LoadMode = settings.LoadMode.ToString(),
                PlacementMode = settings.PlacementMode.ToString(),
                Success = imported && importedElement != null,
                Pinned = pinned,
                Message = imported && importedElement != null ? "DWG 匯入完成。" : "Document.Import 回傳失敗或沒有產生 ElementId。"
            };
        }
    }
}
