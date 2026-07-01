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
        #region 從屬視圖與網格

        /// <summary>
        /// 計算網格範圍加上偏移的 BoundingBox
        /// </summary>
        private object CalculateGridBounds(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;

            var xGridsArray = parameters["xGrids"] as JArray;
            var yGridsArray = parameters["yGrids"] as JArray;
            double offsetMm = parameters["offset_mm"]?.Value<double>() ?? 0;
            double offsetFeet = offsetMm / 304.8;

            List<string> xGridNames = xGridsArray?.Select(x => x.Value<string>()).ToList() ?? new List<string>();
            List<string> yGridNames = yGridsArray?.Select(x => x.Value<string>()).ToList() ?? new List<string>();

            if (xGridNames.Count == 0 && yGridNames.Count == 0)
                throw new Exception("至少需要提供一組 X 軸或 Y 軸網格線名稱");

            var allGrids = new FilteredElementCollector(doc)
                .OfClass(typeof(Grid))
                .Cast<Grid>()
                .ToList();

            double minX = double.MaxValue, maxX = double.MinValue;
            double minY = double.MaxValue, maxY = double.MinValue;

            if (xGridNames.Count > 0)
            {
                foreach (string name in xGridNames)
                {
                    var grid = allGrids.FirstOrDefault(g => g.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                    if (grid != null)
                    {
                        var curve = grid.Curve;
                        double x = (curve.GetEndPoint(0).X + curve.GetEndPoint(1).X) / 2.0;
                        minX = Math.Min(minX, x);
                        maxX = Math.Max(maxX, x);
                    }
                }
            }

            if (yGridNames.Count > 0)
            {
                foreach (string name in yGridNames)
                {
                    var grid = allGrids.FirstOrDefault(g => g.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                    if (grid != null)
                    {
                        var curve = grid.Curve;
                        double y = (curve.GetEndPoint(0).Y + curve.GetEndPoint(1).Y) / 2.0;
                        minY = Math.Min(minY, y);
                        maxY = Math.Max(maxY, y);
                    }
                }
            }

            if (xGridNames.Count == 1) { minX -= offsetFeet; maxX += offsetFeet; }
            if (yGridNames.Count == 1) { minY -= offsetFeet; maxY += offsetFeet; }

            double finalMinX = (xGridNames.Count > 0 ? minX : -1000) - offsetFeet;
            double finalMaxX = (xGridNames.Count > 0 ? maxX : 1000) + offsetFeet;
            double finalMinY = (yGridNames.Count > 0 ? minY : -1000) - offsetFeet;
            double finalMaxY = (yGridNames.Count > 0 ? maxY : 1000) + offsetFeet;

            return new
            {
                min = new { x = finalMinX * 304.8, y = finalMinY * 304.8, z = -100 * 304.8 },
                max = new { x = finalMaxX * 304.8, y = finalMaxY * 304.8, z = 100 * 304.8 }
            };
        }

        /// <summary>
        /// 批次建立從屬視圖並套用邊界
        /// </summary>
        private object CreateDependentViews(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;

            var parentViewIdsArray = parameters["parentViewIds"] as JArray;
            List<IdType> parentViewIds = parentViewIdsArray?.Select(x => x.Value<IdType>()).ToList() ?? new List<IdType>();

            string suffixName = parameters["suffixName"]?.Value<string>();

            double minX = parameters["min"]?["x"]?.Value<double>() ?? 0;
            double minY = parameters["min"]?["y"]?.Value<double>() ?? 0;
            double minZ = parameters["min"]?["z"]?.Value<double>() ?? -100 * 304.8;
            double maxX = parameters["max"]?["x"]?.Value<double>() ?? 0;
            double maxY = parameters["max"]?["y"]?.Value<double>() ?? 0;
            double maxZ = parameters["max"]?["z"]?.Value<double>() ?? 100 * 304.8;

            XYZ min = new XYZ(minX / 304.8, minY / 304.8, minZ / 304.8);
            XYZ max = new XYZ(maxX / 304.8, maxY / 304.8, maxZ / 304.8);
            BoundingBoxXYZ bbox = new BoundingBoxXYZ { Min = min, Max = max };

            List<object> results = new List<object>();

            using (Transaction trans = new Transaction(doc, "批次建立從屬視圖"))
            {
                trans.Start();

                foreach (IdType viewId in parentViewIds)
                {
                    View parentView = doc.GetElement(viewId.ToElementId()) as View;
                    if (parentView == null || !parentView.CanViewBeDuplicated(ViewDuplicateOption.AsDependent))
                        continue;

                    ElementId newViewId = parentView.Duplicate(ViewDuplicateOption.AsDependent);
                    View newView = doc.GetElement(newViewId) as View;

                    string finalSuffix = suffixName;
                    if (string.IsNullOrEmpty(finalSuffix))
                    {
                        int childCount = parentView.GetDependentViewIds().Count();
                        finalSuffix = childCount.ToString();

                        string targetName = $"{parentView.Name}-{finalSuffix}";
                        int loopGuard = 0;
                        while (new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>().Any(v => v.Name == targetName) && loopGuard < 100)
                        {
                            childCount++;
                            finalSuffix = childCount.ToString();
                            targetName = $"{parentView.Name}-{finalSuffix}";
                            loopGuard++;
                        }
                    }

                    string newName = $"{parentView.Name}-{finalSuffix}";
                    try { newView.Name = newName; }
                    catch (Exception ex) { Logger.Debug($"視圖命名失敗: {ex.Message}"); }

                    newView.CropBoxActive = true;
                    newView.CropBoxVisible = true;
                    newView.CropBox = bbox;

                    results.Add(new
                    {
                        ParentName = parentView.Name,
                        NewViewId = newView.Id.GetIdValue(),
                        NewViewName = newView.Name
                    });
                }

                trans.Commit();
            }

            return results;
        }

        #endregion
    }
}
