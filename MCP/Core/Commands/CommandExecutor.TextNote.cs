using System;
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
        #region TextNote 批次操作

        /// <summary>
        /// 在多個 DraftingView 中，依文字內容子字串搜尋 TextNote 並批次平移
        /// </summary>
        private object MoveTextNotesInViews(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;

            // 解析參數
            string textMatch = parameters["textMatch"]?.ToString() ?? "";
            double deltaXMm = parameters["deltaXMm"]?.Value<double>() ?? 0;
            double deltaYMm = parameters["deltaYMm"]?.Value<double>() ?? 0;
            bool dryRun = parameters["dryRun"]?.Value<bool>() ?? false;
            var viewNamesToken = parameters["viewNames"] as JArray;

            if (string.IsNullOrWhiteSpace(textMatch))
            {
                return new { Success = false, Error = "textMatch 不能為空" };
            }

            if (!dryRun && Math.Abs(deltaXMm) < 1e-9 && Math.Abs(deltaYMm) < 1e-9)
            {
                return new { Success = false, Error = "deltaXMm 和 deltaYMm 都為 0，沒有需要移動的量（或設定 dryRun: true 僅搜尋）" };
            }

            // mm → feet
            double deltaXFeet = deltaXMm / 304.8;
            double deltaYFeet = deltaYMm / 304.8;
            XYZ deltaVector = new XYZ(deltaXFeet, deltaYFeet, 0);

            // 建立 viewNames 過濾集合
            HashSet<string> nameFilter = null;
            if (viewNamesToken != null && viewNamesToken.Count > 0)
            {
                nameFilter = new HashSet<string>(
                    viewNamesToken.Select(t => t.ToString()),
                    StringComparer.OrdinalIgnoreCase
                );
            }

            // 收集所有 DraftingView
            var allDraftingViews = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewDrafting))
                .Cast<ViewDrafting>()
                .Where(v => !v.IsTemplate)
                .ToList();

            // 若指定 viewNames，過濾之
            var targetViews = nameFilter != null
                ? allDraftingViews.Where(v => nameFilter.Contains(v.Name)).ToList()
                : allDraftingViews;

            Logger.Info($"[MoveTextNotes] textMatch=\"{textMatch}\", delta=({deltaXMm}, {deltaYMm})mm, dryRun={dryRun}, 目標 view 數={targetViews.Count}");

            var results = new List<object>();
            int totalMoved = 0;
            int viewsWithMatches = 0;

            for (int i = 0; i < targetViews.Count; i++)
            {
                var view = targetViews[i];
                try
                {
                    // 收集此 view 中的 TextNote
                    var textNotes = new FilteredElementCollector(doc, view.Id)
                        .OfClass(typeof(TextNote))
                        .Cast<TextNote>()
                        .Where(tn => tn.Text.IndexOf(textMatch, StringComparison.OrdinalIgnoreCase) >= 0)
                        .ToList();

                    if (textNotes.Count == 0) continue;

                    viewsWithMatches++;
                    int movedCount = 0;
                    var viewWarnings = new List<string>();

                    if (dryRun)
                    {
                        // 僅回報，不移動
                        movedCount = textNotes.Count;
                    }
                    else
                    {
                        using (Transaction trans = new Transaction(doc, $"移動 TextNote in {view.Name}"))
                        {
                            trans.Start();

                            foreach (var tn in textNotes)
                            {
                                try
                                {
                                    ElementTransformUtils.MoveElement(doc, tn.Id, deltaVector);
                                    movedCount++;
                                }
                                catch (Exception ex)
                                {
                                    viewWarnings.Add($"TextNote {tn.Id.GetIdValue()}: {ex.Message}");
                                }
                            }

                            trans.Commit();
                        }
                    }

                    totalMoved += movedCount;

                    Logger.Info($"[MoveTextNotes] ({i + 1}/{targetViews.Count}) {view.Name}: {movedCount} notes {(dryRun ? "found" : "moved")}");

                    results.Add(new
                    {
                        ViewName = view.Name,
                        ViewId = view.Id.GetIdValue(),
                        MatchCount = textNotes.Count,
                        MovedCount = movedCount,
                        Warnings = viewWarnings
                    });
                }
                catch (Exception ex)
                {
                    Logger.Error($"[MoveTextNotes] {view.Name} 失敗: {ex.Message}");
                    results.Add(new
                    {
                        ViewName = view.Name,
                        ViewId = view.Id.GetIdValue(),
                        MatchCount = 0,
                        MovedCount = 0,
                        Warnings = new List<string> { ex.Message }
                    });
                }
            }

            Logger.Info($"[MoveTextNotes] 完成，{viewsWithMatches} 個 view 中共 {(dryRun ? "找到" : "移動")} {totalMoved} 個 TextNote");

            return new
            {
                Success = true,
                TextMatch = textMatch,
                DeltaXMm = deltaXMm,
                DeltaYMm = deltaYMm,
                DryRun = dryRun,
                ViewsScanned = targetViews.Count,
                ViewsWithMatches = viewsWithMatches,
                TotalMoved = totalMoved,
                Results = results
            };
        }

        #endregion
    }
}
