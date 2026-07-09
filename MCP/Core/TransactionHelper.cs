using Autodesk.Revit.DB;

namespace RevitMCP.Core
{
    /// <summary>
    /// Transaction 工廠：建立的 Transaction 已預先註冊 <see cref="SilentFailuresPreprocessor"/>，
    /// 自動吞掉 Warning（避免 Revit 跳對話框阻擋自動化流程）並把 Error 寫進 Logger。
    ///
    /// 用法：
    /// <code>
    /// using (Transaction t = TransactionHelper.Begin(doc, "建立牆"))
    /// {
    ///     t.Start();
    ///     // ... 修改 ...
    ///     t.Commit();
    /// }
    /// </code>
    ///
    /// 注意：呼叫端仍需 <c>t.Start()</c>，這個 helper 只負責 ctor + SetFailureHandlingOptions
    /// 而不主動 Start —— 保留與既有 <c>new Transaction(doc, name)</c> 一致的呼叫慣例，
    /// 方便逐站替換不會混淆 Start 時機。
    /// </summary>
    internal static class TransactionHelper
    {
        internal static Transaction Begin(Document doc, string name)
        {
            var t = new Transaction(doc, name);
            var opts = t.GetFailureHandlingOptions();
            opts.SetFailuresPreprocessor(new SilentFailuresPreprocessor());
            t.SetFailureHandlingOptions(opts);
            return t;
        }
    }
}
