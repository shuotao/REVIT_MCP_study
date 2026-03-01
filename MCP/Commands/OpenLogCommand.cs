using System;
using System.Diagnostics;
using System.IO;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCP.Core;

namespace RevitMCP.Commands
{
    /// <summary>
    /// 開啟日誌檔案命令
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class OpenLogCommand : IExternalCommand
    {
        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            try
            {
                LogViewerWindow.ShowWindow();
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("錯誤", "無法開啟即時日誌視窗: " + ex.Message);
                return Result.Failed;
            }
        }
    }
}
