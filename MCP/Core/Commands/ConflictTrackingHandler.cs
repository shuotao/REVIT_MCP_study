using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace RevitMCP.Core
{
    /// <summary>
    /// IDuplicateTypeNamesHandler：批次 / 跨視圖複製元素時，若目標端已存在同名 Type，
    /// 一律採用目標端既有 Type（UseDestinationTypes），並把每次衝突記進 conflicts 清單供回報。
    ///
    /// 從 劉可 PR#30 的 CommandExecutor.CrossDocument.cs（bundle③，已於 Wave5 收編）抽出為獨立檔，
    /// 因 bundle② 的 copy_detail_items_to_views（CommandExecutor.DetailCopy.cs）亦依賴此 handler。
    /// 保留原簽章：actionStr 參數目前未使用，行為固定為 UseDestinationTypes。
    /// </summary>
    internal class ConflictTrackingHandler : IDuplicateTypeNamesHandler
    {
        private readonly List<object> _conflicts;

        public ConflictTrackingHandler(string actionStr, List<object> conflicts)
        {
            _conflicts = conflicts;
        }

        public DuplicateTypeAction OnDuplicateTypeNamesFound(DuplicateTypeNamesHandlerArgs args)
        {
            _conflicts.Add(new { Action = "used_destination" });
            return DuplicateTypeAction.UseDestinationTypes;
        }
    }
}
