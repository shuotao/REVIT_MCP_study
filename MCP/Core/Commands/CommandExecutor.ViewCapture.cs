using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;

// Revit 2025+ ElementId: int → long
#if REVIT2025_OR_GREATER
using IdType = System.Int64;
#else
using IdType = System.Int32;
#endif

namespace RevitMCP.Core
{
    /// <summary>
    /// 視圖畫面擷取 — 把指定視圖（預設 active view）匯出成點陣圖並以 base64 回傳，
    /// 讓 AI 端能「看見」Revit 目前的畫面，而不必請使用者手動截圖。
    ///
    /// 只讀模型、寫入的是系統暫存目錄下的圖檔（預設用完即刪），不異動 Revit 模型。
    /// </summary>
    public partial class CommandExecutor
    {
        /// <summary>base64 字串的安全上限，超過就自動降解析度重匯一次（WebSocket 訊息與 AI context 都有上限）</summary>
        private const int CaptureMaxBase64Length = 4 * 1024 * 1024;

        /// <summary>允許的最小／最大像素邊長</summary>
        private const int CaptureMinPixelSize = 200;
        private const int CaptureMaxPixelSize = 4000;

        private object CaptureViewImage(JObject parameters)
        {
            var uiDoc = _uiApp.ActiveUIDocument;
            if (uiDoc == null)
            {
                throw new Exception("目前沒有開啟中的文件，無法擷取畫面");
            }

            Document doc = uiDoc.Document;

            // ── 目標視圖：viewId 省略時用 active view ──────────────────────────
            View view;
            IdType viewId = parameters["viewId"]?.Value<IdType>() ?? 0;
            if (viewId > 0)
            {
                view = doc.GetElement(new ElementId(viewId)) as View;
                if (view == null)
                {
                    throw new Exception($"找不到視圖 ID: {viewId}");
                }
            }
            else
            {
                view = uiDoc.ActiveView;
                if (view == null)
                {
                    throw new Exception("取不到 active view");
                }
            }

            // Schedule / Legend 之類不可匯出成影像的視圖，先擋下並給明確訊息
            if (view.ViewType == ViewType.Schedule ||
                view.ViewType == ViewType.ColumnSchedule ||
                view.ViewType == ViewType.PanelSchedule ||
                view.ViewType == ViewType.SystemBrowser ||
                view.ViewType == ViewType.ProjectBrowser)
            {
                throw new Exception($"視圖類型 {view.ViewType} 無法匯出成影像（明細表／瀏覽器類視圖不支援 ExportImage）");
            }

            if (view.IsTemplate)
            {
                throw new Exception($"'{view.Name}' 是視圖樣板，無法匯出成影像");
            }

            // ── 參數 ────────────────────────────────────────────────────────
            int pixelSize = parameters["pixelSize"]?.Value<int>() ?? 1600;
            pixelSize = Math.Max(CaptureMinPixelSize, Math.Min(CaptureMaxPixelSize, pixelSize));

            string format = (parameters["format"]?.Value<string>() ?? "png").Trim().ToLowerInvariant();
            bool isJpeg = format == "jpg" || format == "jpeg";

            string fitDirectionParam = (parameters["fitDirection"]?.Value<string>() ?? "horizontal").Trim().ToLowerInvariant();
            FitDirectionType fitDirection = fitDirectionParam == "vertical"
                ? FitDirectionType.Vertical
                : FitDirectionType.Horizontal;

            bool keepFile = parameters["keepFile"]?.Value<bool>() ?? false;

            // ── 匯出（必要時自動降一階解析度重試）──────────────────────────────
            CaptureResult capture = ExportViewToImage(doc, view, pixelSize, isJpeg, fitDirection);
            bool downscaled = false;

            if (capture.Base64.Length > CaptureMaxBase64Length && pixelSize > CaptureMinPixelSize * 2)
            {
                CleanupCaptureDir(capture.Directory);
                int reducedPixelSize = Math.Max(CaptureMinPixelSize, pixelSize / 2);
                capture = ExportViewToImage(doc, view, reducedPixelSize, isJpeg, fitDirection);
                pixelSize = reducedPixelSize;
                downscaled = true;
            }

            string savedPath = capture.FilePath;
            if (!keepFile)
            {
                CleanupCaptureDir(capture.Directory);
                savedPath = null;
            }

            var warnings = new List<string>();
            if (downscaled)
            {
                warnings.Add($"影像過大，已自動把 pixelSize 降到 {pixelSize} 重新匯出");
            }
            if (capture.Base64.Length > CaptureMaxBase64Length)
            {
                warnings.Add("影像仍超過 4MB，AI 端可能無法完整讀取；請改用較小的 pixelSize 或先裁剪視圖範圍");
            }

            return new
            {
                Success = true,
                ViewId = view.Id.GetIdValue(),
                ViewName = view.Name,
                ViewType = view.ViewType.ToString(),
                DocumentTitle = doc.Title,
                PixelSize = pixelSize,
                Format = isJpeg ? "jpg" : "png",
                MimeType = isJpeg ? "image/jpeg" : "image/png",
                ByteSize = capture.ByteSize,
                SavedPath = savedPath,
                Warnings = warnings,
                // MCP-Server 端會偵測這個欄位，改以 image content block 回傳給 AI client
                ImageBase64 = capture.Base64
            };
        }

        private class CaptureResult
        {
            public string Base64;
            public string FilePath;
            public string Directory;
            public long ByteSize;
        }

        /// <summary>
        /// 匯出到一個全新的空暫存目錄。Revit 的 ExportImage 會在基底檔名後面自動附加視圖名稱，
        /// 實際檔名無法事先得知，所以用「空目錄裡唯一的檔案」來取回。
        /// </summary>
        private CaptureResult ExportViewToImage(
            Document doc,
            View view,
            int pixelSize,
            bool isJpeg,
            FitDirectionType fitDirection)
        {
            string tempDir = Path.Combine(
                Path.GetTempPath(),
                "RevitMCP_Capture",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            ImageFileType fileType = isJpeg ? ImageFileType.JPEGMedium : ImageFileType.PNG;

            var options = new ImageExportOptions
            {
                FilePath = Path.Combine(tempDir, "view"),
                ExportRange = ExportRange.SetOfViews,
                FitDirection = fitDirection,
                HLRandWFViewsFileType = fileType,
                ShadowViewsFileType = fileType,
                ImageResolution = ImageResolution.DPI_150,
                ShouldCreateWebSite = false,
                ZoomType = ZoomFitType.FitToPage,
                PixelSize = pixelSize
            };
            options.SetViewsAndSheets(new List<ElementId> { view.Id });

            try
            {
                doc.ExportImage(options);
            }
            catch (Exception ex)
            {
                CleanupCaptureDir(tempDir);
                throw new Exception($"ExportImage 失敗（視圖 '{view.Name}'）: {ex.Message}");
            }

            string exportedFile = Directory
                .GetFiles(tempDir)
                .OrderByDescending(f => new FileInfo(f).Length)
                .FirstOrDefault();

            if (string.IsNullOrEmpty(exportedFile))
            {
                CleanupCaptureDir(tempDir);
                throw new Exception($"ExportImage 沒有產生任何檔案（視圖 '{view.Name}'）");
            }

            byte[] bytes = File.ReadAllBytes(exportedFile);

            return new CaptureResult
            {
                Base64 = Convert.ToBase64String(bytes),
                FilePath = exportedFile,
                Directory = tempDir,
                ByteSize = bytes.LongLength
            };
        }

        private void CleanupCaptureDir(string directory)
        {
            try
            {
                if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
                {
                    Directory.Delete(directory, true);
                }
            }
            catch
            {
                // 暫存清理失敗不影響主要結果
            }
        }
    }
}
