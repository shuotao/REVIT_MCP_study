using System;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;

namespace RevitMCP.Core
{
    public partial class CommandExecutor
    {
        #region 樓層建立

        /// <summary>
        /// 建立樓層 (Level)。指定標高（公釐，自動轉 feet）與可選名稱；名稱已存在時 Revit 會自動附加尾號。
        /// 來源 fork: s9101800111-byte/restore-dwg-tools-merge:MCP/Core/CommandExecutor.cs（作者 lt0106）
        /// </summary>
        private object CreateLevel(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;

            if (parameters["elevation"] == null)
            {
                throw new Exception("請指定 elevation（公釐）");
            }

            double elevationMm = parameters["elevation"].Value<double>();
            double elevationFt = elevationMm / 304.8;
            string customName = parameters["name"]?.Value<string>();

            // 重複檢查（警告但仍建立）
            var existing = new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .ToList();

            string warning = null;
            var dupByElev = existing.FirstOrDefault(l => Math.Abs(l.Elevation - elevationFt) < 1e-6);
            if (dupByElev != null)
            {
                warning = $"已有樓層 '{dupByElev.Name}' 在相同標高 {elevationMm} mm";
            }
            if (!string.IsNullOrEmpty(customName) && existing.Any(l => l.Name == customName))
            {
                warning = (warning == null ? "" : warning + "；") +
                          $"已有樓層名稱 '{customName}'，Revit 會自動附加尾號";
            }

            Level newLevel;
            using (Transaction trans = new Transaction(doc, "建立樓層"))
            {
                trans.Start();

                newLevel = Level.Create(doc, elevationFt);

                if (!string.IsNullOrEmpty(customName))
                {
                    try
                    {
                        newLevel.Name = customName;
                    }
                    catch (Exception ex)
                    {
                        warning = (warning == null ? "" : warning + "；") +
                                  $"命名為 '{customName}' 失敗：{ex.Message}";
                    }
                }

                trans.Commit();
            }

            return new
            {
                ElementId = newLevel.Id.GetIdValue(),
                Name = newLevel.Name,
                ElevationMm = Math.Round(newLevel.Elevation * 304.8, 2),
                Warning = warning,
                Message = $"成功建立樓層 '{newLevel.Name}'，標高 {Math.Round(newLevel.Elevation * 304.8, 2)} mm"
            };
        }

        #endregion
    }
}
