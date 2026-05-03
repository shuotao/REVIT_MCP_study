using System.Linq;
using Autodesk.Revit.DB;

namespace RevitMCP.Core
{
    /// <summary>
    /// IFailuresPreprocessor：吞掉 Transaction 中所有 Warning 級別訊息（避免 Revit 跳對話框
    /// 阻擋自動化流程），同時把 Error 級別寫入 Logger 留下軌跡，但不主動 rollback —
    /// 由各 Transaction 的呼叫端自行決定是否要 commit / rollback。
    ///
    /// 與既有的 <see cref="WarningSwallower"/> 差別：
    ///   • WarningSwallower 只 DeleteWarning，不記 Error
    ///   • SilentFailuresPreprocessor 會把 Error 內容打進 RevitMCP_*.log
    ///
    /// 使用方式建議透過 <see cref="TransactionHelper.BeginTransaction"/>，
    /// 已自動包好 SetFailuresPreprocessor。
    ///
    /// 對應 domain/lessons.md [L-013]。
    /// </summary>
    public class SilentFailuresPreprocessor : IFailuresPreprocessor
    {
        public FailureProcessingResult PreprocessFailures(FailuresAccessor accessor)
        {
            var msgs = accessor.GetFailureMessages();
            foreach (var msg in msgs)
            {
                var sev = msg.GetSeverity();
                if (sev == FailureSeverity.Warning)
                {
                    accessor.DeleteWarning(msg);
                }
                else if (sev == FailureSeverity.Error)
                {
                    // 留下軌跡但不 rollback — 由 Transaction 呼叫端決定
                    string desc = "";
                    try
                    {
                        var def = msg.GetFailureDefinitionId();
                        var ids = msg.GetFailingElementIds();
                        desc = $"FailureDefinitionId={def.Guid}, msg={msg.GetDescriptionText()}, elementIds=[{string.Join(",", ids.Select(i => i.GetIdValue().ToString()))}]";
                    }
                    catch
                    {
                        desc = msg.GetDescriptionText();
                    }
                    Logger.Error($"[SilentFailuresPreprocessor] Error in transaction \"{accessor.GetTransactionName()}\": {desc}");
                }
            }
            return FailureProcessingResult.Continue;
        }
    }
}
