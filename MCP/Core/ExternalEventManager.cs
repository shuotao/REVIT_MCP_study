using System;
using System.Collections.Concurrent;
using Autodesk.Revit.UI;

namespace RevitMCP.Core
{
    /// <summary>
    /// 外部事件管理器
    /// 確保命令在 Revit UI 執行緒中執行
    ///
    /// 2026-10-22（補登 PR #71 fix 1）：原本只有「一個」_action 欄位——ExecuteCommand 是
    /// 「覆寫 _action 再 Raise」，Execute 真正執行前若又排入一個動作，前一個就被悄悄丟掉。
    /// 所有 MCP WebSocket 命令都走這裡（Application.OnCommandReceived），所以連續兩個命令
    /// 在同一個 Execute 之前抵達時，前一個命令不會執行、MCP 端只會等到逾時
    /// （同一個欄位覆寫問題的讀碼證據見 <see cref="CameraManagerEventQueue"/> 的註解）。
    /// 現在改為真正的 FIFO <see cref="ConcurrentQueue{T}"/>：
    ///   • 一個命令都不會掉；
    ///   • Execute 把佇列排空，每個動作各自 try/catch，一個命令丟例外不會中斷排在它後面的命令；
    ///   • 錯誤只寫 <see cref="Logger"/>，絕不跳 TaskDialog——
    ///     Autodesk.Windows.TaskDialog.PrivateShow 是 Revit 2024 裡唯一會建立
    ///     SxSActivationContext 的地方，兩次崩潰的堆疊都指向它（見 CameraManagerEventQueue 註解）。
    /// 本類別與 <see cref="CameraManagerEventQueue"/> 刻意分開、不可合併（CLAUDE.md Code Conventions）：
    /// 面板有自己的優先佇列與 budget 讓位策略，這裡是單純的命令請求／回應序列。
    /// </summary>
    public class ExternalEventManager
    {
        private static ExternalEventManager _instance;
        private static readonly object _lock = new object();
        
        private ExternalEvent _externalEvent;
        private CommandEventHandler _eventHandler;

        private ExternalEventManager()
        {
            _eventHandler = new CommandEventHandler();
            _externalEvent = ExternalEvent.Create(_eventHandler);
        }

        public static ExternalEventManager Instance
        {
            get
            {
                lock (_lock)
                {
                    if (_instance == null)
                    {
                        _instance = new ExternalEventManager();
                    }
                    return _instance;
                }
            }
        }

        /// <summary>
        /// 執行命令（排入 FIFO 佇列後 Raise，不覆寫任何尚未執行的命令）
        /// </summary>
        public void ExecuteCommand(Action<UIApplication> action)
        {
            if (action == null) return;

            _eventHandler.Enqueue(action);
            try
            {
                ExternalEventRequest r = _externalEvent.Raise();
                if (r != ExternalEventRequest.Accepted && r != ExternalEventRequest.Pending)
                    Logger.Info($"[ExternalEvent] Raise 回傳 {r}（命令仍在佇列中，下一次 Raise 時執行）。");
            }
            catch (Exception ex)
            {
                // Raise 失敗時命令留在佇列裡，下一次 Raise 會一併執行；只寫 log，不跳 TaskDialog。
                Logger.Error("[ExternalEvent] Raise 失敗", ex);
            }
        }

        /// <summary>
        /// 命令事件處理器
        /// </summary>
        private class CommandEventHandler : IExternalEventHandler
        {
            private readonly ConcurrentQueue<Action<UIApplication>> _queue =
                new ConcurrentQueue<Action<UIApplication>>();

            public void Enqueue(Action<UIApplication> action)
            {
                if (action == null) return;
                _queue.Enqueue(action);
            }

            public void Execute(UIApplication app)
            {
                // 排空佇列：每個動作各自 try/catch，前一個命令失敗不會讓排在後面的命令一起消失。
                while (_queue.TryDequeue(out Action<UIApplication> action))
                {
                    try
                    {
                        action(app);
                    }
                    catch (Exception ex)
                    {
                        // 絕不跳 TaskDialog（SxSActivationContext 崩潰來源），一律寫 log。
                        Logger.Error("[ExternalEvent] 命令執行錯誤", ex);
                    }
                }
            }

            public string GetName()
            {
                return "RevitMCP Command Handler";
            }
        }
    }
}
