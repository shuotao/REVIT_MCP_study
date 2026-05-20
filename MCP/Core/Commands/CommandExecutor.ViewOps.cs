using System;
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
        /// 重新命名視圖（包含剖面圖、平面圖等）
        /// </summary>
        private object RenameView(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;
            IdType viewId = parameters["viewId"]?.Value<IdType>() ?? 0;
            string newName = parameters["newName"]?.Value<string>();

            if (string.IsNullOrEmpty(newName))
                throw new Exception("請指定新的視圖名稱");

            Element elem = doc.GetElement(new ElementId(viewId));
            if (elem == null)
                throw new Exception($"找不到元素 ID: {viewId}");

            View view = elem as View;
            if (view == null)
            {
                // 如果選取的元素是剖面標記等非 View 物件，利用其與視圖同名的特性，在模型中尋找同名的 View 物件
                string viewName = elem.Name;
                view = new FilteredElementCollector(doc)
                    .OfClass(typeof(View))
                    .Cast<View>()
                    .FirstOrDefault(v => v.Name == viewName);
            }

            if (view == null)
                throw new Exception($"找不到視圖 ID: {viewId}，且無法對應到同名的視圖物件");

            using (Transaction trans = new Transaction(doc, "重新命名視圖"))
            {
                trans.Start();
                view.Name = newName;
                trans.Commit();
            }

            return new
            {
                ViewId = viewId,
                NewName = newName,
                Message = $"成功將視圖重新命名為: {newName}"
            };
        }
    }
}
