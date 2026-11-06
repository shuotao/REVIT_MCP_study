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

        /// <summary>
        /// 2026-09-26 相機管理稽核：Commit 並確認真的提交。SilentFailuresPreprocessor 對「錯誤」回 Continue，
        /// 無法解決的錯誤會讓 Commit() 回傳 RolledBack 而不丟例外——呼叫端若不檢查，就會把已不存在的元素 Id
        /// 當成功結果回傳（或在回滾後的元素上讀屬性而丟例外）。未提交 → 丟 InvalidOperationException，交給既有 catch。
        /// </summary>
        internal static void CommitOrThrow(Transaction t)
        {
            TransactionStatus st = t.Commit();
            if (st != TransactionStatus.Committed)
                throw new System.InvalidOperationException($"交易「{t.GetName()}」未提交（狀態 {st}），Revit 已回滾本次變更。");
        }
    }
}
