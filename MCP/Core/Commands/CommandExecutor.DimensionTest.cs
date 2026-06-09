using System;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;

namespace RevitMCP.Core
{
    public partial class CommandExecutor
    {
        private object TestCreateDimension(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;
            View activeView = doc.ActiveView;

            try
            {
                // 嘗試抓取網格線作為最穩定的標註示範
                var grids = new FilteredElementCollector(doc, activeView.Id)
                    .OfCategory(BuiltInCategory.OST_Grids)
                    .WhereElementIsNotElementType()
                    .Cast<Grid>()
                    .ToList();

                if (grids.Count >= 2)
                {
                    Grid g1 = grids[0];
                    Grid g2 = grids[1];
                    
                    Line line1 = g1.Curve as Line;
                    Line line2 = g2.Curve as Line;

                    if (line1 != null && line2 != null)
                    {
                        XYZ p1 = line1.Origin;
                        XYZ p2 = line2.Origin;
                        
                        // 建立尺寸線放置的基準線 (與網格線垂直)
                        XYZ direction = (p2 - p1).Normalize();
                        Line dimLine = Line.CreateBound(p1, p2);

                        ReferenceArray refArray = new ReferenceArray();
                        refArray.Append(new Reference(g1));
                        refArray.Append(new Reference(g2));

                        using (Transaction t = new Transaction(doc, "測試對齊標註 (網格)"))
                        {
                            t.Start();
                            doc.Create.NewDimension(activeView, dimLine, refArray);
                            t.Commit();
                        }
                        return new { Success = true, Message = "已成功在兩條網格線之間建立對齊標註。" };
                    }
                }

                return new { Success = false, Error = "視圖中找不到足夠的網格線來進行標註示範。" };
            }
            catch (Exception ex)
            {
                return new { Success = false, Error = "標註失敗: " + ex.Message };
            }
        }
    }
}
