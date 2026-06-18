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
        #region 明細表讀取（儀錶板資料來源）

        /// <summary>
        /// 列出專案中所有可讀取的明細表（排除樣板、圖框修訂表、內部圖說註記表）
        /// </summary>
        private object ListSchedules(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;

            var schedules = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSchedule))
                .Cast<ViewSchedule>()
                .Where(vs => !vs.IsTemplate
                          && !vs.IsTitleblockRevisionSchedule
                          && !vs.IsInternalKeynoteSchedule)
                .Select(vs =>
                {
                    TableSectionData body = vs.GetTableData().GetSectionData(SectionType.Body);

                    string categoryName = "（多重品類 / 無品類）";
                    ElementId catId = vs.Definition?.CategoryId;
                    if (catId != null && catId != ElementId.InvalidElementId)
                    {
                        Category cat = Category.GetCategory(doc, catId);
                        if (cat != null) categoryName = cat.Name;
                    }

                    return new
                    {
                        ElementId = vs.Id.GetIdValue(),
                        Name = vs.Name,
                        Category = categoryName,
                        IsItemized = vs.Definition?.IsItemized ?? true,
                        RowCount = body.NumberOfRows,
                        ColumnCount = body.NumberOfColumns
                    };
                })
                .OrderBy(s => s.Category)
                .ThenBy(s => s.Name)
                .ToList();

            return new
            {
                Count = schedules.Count,
                Schedules = schedules
            };
        }

        /// <summary>
        /// 讀取單一明細表的完整表格內容（欄位標頭 + 逐格 body 資料，忠實呈現畫面顯示文字）
        /// </summary>
        private object ReadSchedule(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;

            IdType? scheduleId = parameters["scheduleId"]?.Value<IdType>();
            string scheduleName = parameters["scheduleName"]?.Value<string>();
            int maxRows = parameters["maxRows"]?.Value<int>() ?? 2000;

            ViewSchedule schedule = null;
            if (scheduleId.HasValue && scheduleId.Value > 0)
            {
                schedule = doc.GetElement(new ElementId(scheduleId.Value)) as ViewSchedule;
            }
            else if (!string.IsNullOrEmpty(scheduleName))
            {
                schedule = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSchedule))
                    .Cast<ViewSchedule>()
                    .FirstOrDefault(vs => !vs.IsTemplate && vs.Name == scheduleName)
                  ?? new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSchedule))
                    .Cast<ViewSchedule>()
                    .FirstOrDefault(vs => !vs.IsTemplate && vs.Name.Contains(scheduleName));
            }

            if (schedule == null)
            {
                throw new Exception(scheduleId.HasValue
                    ? $"找不到明細表 ID: {scheduleId}"
                    : $"找不到明細表名稱: {scheduleName}（請用 scheduleId 或 scheduleName 指定）");
            }

            // 欄位標頭：取定義中未隱藏的欄位，依欄位順序
            var headers = new List<string>();
            ScheduleDefinition def = schedule.Definition;
            for (int i = 0; i < def.GetFieldCount(); i++)
            {
                ScheduleField field = def.GetField(i);
                if (field.IsHidden) continue;
                string heading = field.ColumnHeading;
                headers.Add(string.IsNullOrEmpty(heading) ? field.GetName() : heading);
            }

            // Body 逐格讀取（GetCellText 回傳的是畫面實際顯示的文字，含群組標頭與合計列）
            TableSectionData body = schedule.GetTableData().GetSectionData(SectionType.Body);
            int totalRows = body.NumberOfRows;
            int totalCols = body.NumberOfColumns;
            int rowsToRead = Math.Min(totalRows, maxRows);

            var rows = new List<List<string>>();
            for (int r = 0; r < rowsToRead; r++)
            {
                int absRow = body.FirstRowNumber + r;
                var rowCells = new List<string>(totalCols);
                for (int c = 0; c < totalCols; c++)
                {
                    int absCol = body.FirstColumnNumber + c;
                    rowCells.Add(schedule.GetCellText(SectionType.Body, absRow, absCol));
                }
                rows.Add(rowCells);
            }

            string categoryName = "（多重品類 / 無品類）";
            if (def.CategoryId != null && def.CategoryId != ElementId.InvalidElementId)
            {
                Category cat = Category.GetCategory(doc, def.CategoryId);
                if (cat != null) categoryName = cat.Name;
            }

            return new
            {
                ElementId = schedule.Id.GetIdValue(),
                Name = schedule.Name,
                Category = categoryName,
                Headers = headers,
                ColumnCount = totalCols,
                RowCount = totalRows,
                ReturnedRows = rowsToRead,
                Truncated = totalRows > rowsToRead,
                Rows = rows
            };
        }

        #endregion
    }
}
