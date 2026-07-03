using System;
using System.Collections.Generic;
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
        #region 品類列舉 (list_categories)

        private class CategoryRow
        {
            public string Name;
            public string CategoryType;
            public IdType Id;
            public int SubcategoryCount;
            public List<string> Subcategories;
        }

        /// <summary>
        /// 列舉專案中所有品類 (Category) 及其 CategoryType。
        /// 資料來源 doc.Settings.Categories，唯讀查詢，不開交易。
        /// 可用 categoryType 參數過濾 (Model / Annotation / Internal / AnalyticalModel / Invalid)。
        /// </summary>
        private object ListCategories(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;

            string typeFilter = parameters?["categoryType"]?.Value<string>();
            bool includeSubcategories = parameters?["includeSubcategories"]?.Value<bool>() ?? false;

            var rows = new List<CategoryRow>();

            foreach (Category cat in doc.Settings.Categories)
            {
                if (cat == null) continue;

                string catTypeName = cat.CategoryType.ToString();
                if (!string.IsNullOrWhiteSpace(typeFilter) &&
                    !string.Equals(catTypeName, typeFilter, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var subNames = new List<string>();
                foreach (Category sub in cat.SubCategories)
                {
                    if (includeSubcategories) subNames.Add(sub.Name);
                    else subNames.Add(null); // 只計數
                }

                rows.Add(new CategoryRow
                {
                    Name = cat.Name,
                    CategoryType = catTypeName,
                    Id = cat.Id.GetIdValue(),
                    SubcategoryCount = subNames.Count,
                    Subcategories = includeSubcategories
                        ? subNames.OrderBy(n => n).ToList()
                        : null
                });
            }

            var sorted = rows
                .OrderBy(r => r.CategoryType)
                .ThenBy(r => r.Name)
                .ToList();

            var byType = sorted
                .GroupBy(r => r.CategoryType)
                .ToDictionary(g => g.Key, g => g.Count());

            return new
            {
                Success = true,
                TotalCount = sorted.Count,
                CountByType = byType,
                Categories = sorted
            };
        }

        #endregion
    }
}
