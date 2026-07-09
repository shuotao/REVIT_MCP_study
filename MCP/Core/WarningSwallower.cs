using Autodesk.Revit.DB;

namespace RevitMCP.Core
{
    /// <summary>
    /// IFailuresPreprocessor：自動吞掉 Transaction 中的所有 Warning 級別訊息，
    /// 避免 Revit 彈出對話框阻擋自動化流程。Error 級別仍保留（會讓 Transaction 回滾）。
    ///
    /// 使用方式：
    ///   using (Transaction t = new Transaction(doc, "..."))
    ///   {
    ///       t.Start();
    ///       var opts = t.GetFailureHandlingOptions();
    ///       opts.SetFailuresPreprocessor(new WarningSwallower());
    ///       t.SetFailureHandlingOptions(opts);
    ///       // ... 修改 ...
    ///       t.Commit();
    ///   }
    ///
    /// 對應 domain/lessons.md [L-013] 靜默處理規範。
    /// </summary>
    public class WarningSwallower : IFailuresPreprocessor
    {
        public FailureProcessingResult PreprocessFailures(FailuresAccessor a)
        {
            var msgs = a.GetFailureMessages();
            foreach (var f in msgs)
            {
                if (f.GetSeverity() == FailureSeverity.Warning)
                {
                    a.DeleteWarning(f);
                }
            }
            return FailureProcessingResult.Continue;
        }
    }
}
