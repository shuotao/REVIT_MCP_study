using System;
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

            View view = doc.GetElement(new ElementId(viewId)) as View;
            if (view == null)
                throw new Exception($"找不到視圖 ID: {viewId}");

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
