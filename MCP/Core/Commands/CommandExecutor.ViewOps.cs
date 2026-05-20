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
        /// <summary>
        /// 調整剖面視圖的網格線 (Grids) 與樓層線 (Levels) 2D 範圍與顯示
        /// </summary>
        private object AdjustSectionDatums(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;
            
            // 讀取傳入的視圖 ID 陣列
            JArray viewIdsArray = parameters["viewIds"] as JArray;
            if (viewIdsArray == null || viewIdsArray.Count == 0)
            {
                return new { Success = false, Message = "未提供有效的 viewIds 參數。" };
            }

            var processedViews = new List<string>();
            var errors = new List<string>();

            using (Transaction trans = new Transaction(doc, "自動調整剖面基準線"))
            {
                trans.Start();

                foreach (var token in viewIdsArray)
                {
                    try
                    {
                        IdType val = token.Value<IdType>();
                        ElementId id = new ElementId(val);
                        Element elem = doc.GetElement(id);
                        if (elem == null) continue;

                        // 嘗試尋找/轉為視圖
                        View view = elem as View;
                        if (view == null)
                        {
                            // 容錯：若傳入的是剖面標記，利用名稱尋找同名視圖
                            string name = elem.Name;
                            view = new FilteredElementCollector(doc)
                                .OfClass(typeof(View))
                                .Cast<View>()
                                .FirstOrDefault(v => v.Name == name && v.ViewType == ViewType.Section);
                        }

                        if (view == null)
                        {
                            errors.Add($"Element ID {val} 無法對應至剖面視圖。");
                            continue;
                        }

                        // 里程碑 1 暫行測試：只記錄視圖名稱
                        processedViews.Add(view.Name);
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"處理視圖時發生錯誤: {ex.Message}");
                    }
                }

                trans.Commit();
            }

            return new
            {
                Success = errors.Count == 0,
                ProcessedCount = processedViews.Count,
                ProcessedViews = processedViews,
                Errors = errors
            };
        }
    }
}
