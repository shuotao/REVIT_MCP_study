using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;

namespace RevitMCP.Core.Review
{
    /// <summary>
    /// 監聽 DocumentChanged，把每筆交易（使用者手動或 AI）記成精簡紀錄，
    /// 讓 AI 透過 get_recent_changes 知道「上次看過之後模型被改了什麼」，
    /// 不必截螢幕，也避免 AI 拿過期狀態做決策。
    /// 只保留最近 Capacity 筆，記憶體固定。
    /// </summary>
    internal static class ChangeTracker
    {
        private const int Capacity = 500;
        private const int SampleIds = 20;
        private static readonly object Gate = new object();
        private static readonly LinkedList<ChangeEntry> Entries = new LinkedList<ChangeEntry>();
        private static long _seq;

        internal sealed class ChangeEntry
        {
            public long Seq;
            public DateTime Time;
            public string Document;
            public string Operation;
            public List<string> Transactions;
            public bool ByAI;
            public int Added, Modified, Deleted;
            public List<long> AddedIds, ModifiedIds, DeletedIds;
            public List<long> CategoryProbeIds; // 供 get_recent_changes 惰性解析類別（handler 內不碰 GetElement）
        }

        private static bool _subscribed;

        /// <summary>
        /// 首次呼叫 get_model_snapshot / get_recent_changes 時才訂閱 DocumentChanged（惰性訂閱），
        /// 不在 OnStartup 掛事件，未使用本功能的 session 不承擔此開銷。紀錄從訂閱當下開始。
        /// </summary>
        public static void EnsureSubscribed(Autodesk.Revit.ApplicationServices.Application app)
        {
            lock (Gate)
            {
                if (_subscribed || app == null) return;
                app.DocumentChanged += OnDocumentChanged;
                _subscribed = true;
            }
        }

        public static void Unsubscribe(Autodesk.Revit.ApplicationServices.Application app)
        {
            lock (Gate)
            {
                if (!_subscribed || app == null) return;
                app.DocumentChanged -= OnDocumentChanged;
                _subscribed = false;
            }
        }

        public static void OnDocumentChanged(object sender, DocumentChangedEventArgs e)
        {
            try
            {
                Document doc = e.GetDocument();
                if (doc == null || doc.IsFamilyDocument) return;

                var added = e.GetAddedElementIds();
                var modified = e.GetModifiedElementIds();
                var deleted = e.GetDeletedElementIds();
                var names = e.GetTransactionNames()?.ToList() ?? new List<string>();

                var entry = new ChangeEntry
                {
                    Time = DateTime.Now,
                    Document = doc.Title,
                    Operation = SafeOperation(e),
                    Transactions = names,
                    ByAI = names.Any(n => n.StartsWith("AI:", StringComparison.Ordinal) || n.StartsWith("MCP", StringComparison.OrdinalIgnoreCase)),
                    Added = added.Count,
                    Modified = modified.Count,
                    Deleted = deleted.Count,
                    AddedIds = added.Take(SampleIds).Select(i => (long)i.GetIdValue()).ToList(),
                    ModifiedIds = modified.Take(SampleIds).Select(i => (long)i.GetIdValue()).ToList(),
                    DeletedIds = deleted.Take(SampleIds).Select(i => (long)i.GetIdValue()).ToList(),
                    CategoryProbeIds = added.Concat(modified).Take(2000).Select(i => (long)i.GetIdValue()).ToList()
                };

                lock (Gate)
                {
                    entry.Seq = ++_seq;
                    Entries.AddLast(entry);
                    while (Entries.Count > Capacity) Entries.RemoveFirst();
                }
            }
            catch (Exception ex)
            {
                // 事件處理器絕不能丟例外，否則會干擾使用者在 Revit 的操作
                try { Logger.Error("[ChangeTracker] " + ex.Message); } catch { }
            }
        }

        private static string SafeOperation(DocumentChangedEventArgs e)
        {
            try { return e.Operation.ToString(); } catch { return null; }
        }

        public static long CurrentSeq { get { lock (Gate) return _seq; } }

        public static List<ChangeEntry> Since(long afterSeq, int limit, string documentTitle)
        {
            lock (Gate)
            {
                return Entries.Where(x => x.Seq > afterSeq && (documentTitle == null || x.Document == documentTitle))
                              .Reverse().Take(limit).Reverse().ToList();
            }
        }
    }
}
