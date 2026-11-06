using System;
using System.Collections.Concurrent;
using Autodesk.Revit.UI;

namespace RevitMCP.Core
{
    /// <summary>
    /// 相機管理面板專用的 ExternalEvent 佇列（2026-09-24 第二輪崩潰調查新增）。
    ///
    /// 為什麼不再用共用的 <see cref="ExternalEventManager"/>：它只有「一個」_action 欄位——
    /// ExecuteCommand 是「覆寫 _action 再 Raise」，Execute 真正執行前如果又呼叫了一次，
    /// 前一個動作就被悄悄丟掉（讀碼證實）。面板一次刷新會連續排入「讀快照 → 匯出底圖 →
    /// 讀剖面線」三個動作，結果只有最後一個會跑，底圖匯出被吃掉、`_baseMapRefreshInFlight`
    /// 永遠不會被清掉（log 裡的「底圖匯出仍在進行中，忽略這次重複觸發」就是這樣來的）；
    /// 更糟的是 MCP WebSocket 命令（Application.OnCommandReceived）也共用同一個欄位，兩邊會
    /// 互相覆寫對方的動作。另外它的 catch 會呼叫 TaskDialog.Show——AdWindows 的 TaskDialog
    /// （Autodesk.Windows.TaskDialog.PrivateShow）正是整個 Revit 2024 裡唯一會建立
    /// SxSActivationContext 的地方（2026-09-24 以反射掃描 AdWindows.dll IL 確認），而 Windows
    /// 事件記錄檔兩次崩潰的堆疊就是 SxSActivationContext 在 finalizer 執行緒上 Deactivate 失敗。
    /// 所以這裡：
    ///   • 真正的 FIFO 佇列，一個動作都不會丟；
    ///   • 每個動作各自 try/catch，只寫 log／崩潰紀錄，絕不跳 TaskDialog；
    ///   • 每次 Execute 只處理「進來時已在佇列裡」的動作，途中新排入的留到下一次 Raise，
    ///     讓 Revit 在兩個重量級動作（讀快照、ExportImage）之間回到自己的訊息迴圈；
    ///   • 每個動作都包在 <see cref="CameraManagerDiagnostics.BeginOperation"/> 裡。
    /// ExternalEvent.Create 必須在有效的 API context 呼叫，所以 <see cref="Initialize"/> 由
    /// Application.OnStartup 呼叫。
    /// </summary>
    internal static class CameraManagerEventQueue
    {
        private static ExternalEvent _event;
        private static readonly ConcurrentQueue<QueuedAction> Queue = new ConcurrentQueue<QueuedAction>();
        /// <summary>2026-09-26 第六輪（使用者：「改離樓板高度後透視圖要馬上變」）：優先佇列。清單／地圖的相機編輯走這裡，
        /// 每次 Execute 先清空優先佇列再做一般佇列——不再排在「讀快照／匯出底圖／讀剖面線／相機變動後更新（可能數秒）」後面。</summary>
        private static readonly ConcurrentQueue<QueuedAction> PriorityQueue = new ConcurrentQueue<QueuedAction>();

        /// <summary>排入一個「使用者正在等畫面」的小動作（相機編輯快速路徑），插隊到一般佇列之前。</summary>
        public static void EnqueuePriority(string name, Action<UIApplication> action)
        {
            if (action == null) return;
            if (_event == null)
            {
                Logger.Error($"相機管理佇列尚未初始化，丟棄優先動作：{name}");
                return;
            }
            PriorityQueue.Enqueue(new QueuedAction { Name = name, Action = action });
            int waiting = Queue.Count;
            if (waiting > 0) Logger.Info($"相機管理佇列：優先動作 {name} 插隊（一般佇列尚有 {waiting} 個動作在等）。");
            try
            {
                ExternalEventRequest r = _event.Raise();
                if (r != ExternalEventRequest.Accepted && r != ExternalEventRequest.Pending)
                    Logger.Info($"相機管理佇列：Raise 回傳 {r}（優先動作 {name} 仍在佇列中，下一次 Raise 時執行）。");
            }
            catch (Exception ex)
            {
                Logger.Error($"相機管理佇列 Raise 失敗（{name}）: {ex.Message}");
            }
        }

        private sealed class QueuedAction
        {
            public string Name;
            public Action<UIApplication> Action;
        }

        public static void Initialize()
        {
            if (_event != null) return;
            _event = ExternalEvent.Create(new Handler());
        }

        /// <summary>排入一個需要 Revit API context 的動作。name 只用於 log／崩潰紀錄。</summary>
        public static void Enqueue(string name, Action<UIApplication> action)
        {
            if (action == null) return;
            if (_event == null)
            {
                Logger.Error($"相機管理佇列尚未初始化，丟棄動作：{name}");
                return;
            }
            Queue.Enqueue(new QueuedAction { Name = name, Action = action });
            try
            {
                ExternalEventRequest r = _event.Raise();
                if (r != ExternalEventRequest.Accepted && r != ExternalEventRequest.Pending)
                    Logger.Info($"相機管理佇列：Raise 回傳 {r}（動作 {name} 仍在佇列中，下一次 Raise 時執行）。");
            }
            catch (Exception ex)
            {
                Logger.Error($"相機管理佇列 Raise 失敗（{name}）: {ex.Message}");
            }
        }

        private sealed class Handler : IExternalEventHandler
        {
            public void Execute(UIApplication app)
            {
                // 優先佇列：先全部做完（相機編輯，每個只是一個小交易＋重繪）。
                while (PriorityQueue.TryDequeue(out QueuedAction p)) Run(app, p);

                int budget = Queue.Count;
                for (int i = 0; i < budget; i++)
                {
                    // 一般動作之間若又有優先動作進來（使用者在前一個重量級動作執行中按了 Enter）→ 先做優先的。
                    while (PriorityQueue.TryDequeue(out QueuedAction p)) Run(app, p);
                    if (!Queue.TryDequeue(out QueuedAction item)) break;
                    Run(app, item);
                }

                if (!Queue.IsEmpty || !PriorityQueue.IsEmpty)
                {
                    try { _event?.Raise(); } catch { }
                }
            }

            private static void Run(UIApplication app, QueuedAction item)
            {
                using (CameraManagerDiagnostics.BeginOperation("queue:" + item.Name))
                {
                    try
                    {
                        Logger.Info($"相機管理佇列：執行 {item.Name}。");
                        item.Action(app);
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"相機管理佇列動作失敗（{item.Name}）", ex);
                        CameraManagerDiagnostics.RecordCaught("queue:" + item.Name, ex);
                    }
                }
            }

            public string GetName() => "RevitMCP Camera Manager Queue";
        }
    }
}
