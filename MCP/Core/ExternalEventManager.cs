using System;
using System.Collections.Concurrent;
using Autodesk.Revit.UI;

namespace RevitMCP.Core
{
    /// <summary>
    /// 外部事件管理器
    /// 確保命令在 Revit UI 執行緒中執行
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
        /// 執行命令
        /// ExternalEvent.Raise() 會合併多次呼叫為單次 Execute，
        /// 因此以佇列保存每個命令，避免連續命令互相覆蓋而靜默遺失。
        /// </summary>
        public void ExecuteCommand(Action<UIApplication> action)
        {
            _eventHandler.EnqueueAction(action);
            _externalEvent.Raise();
        }

        /// <summary>
        /// 命令事件處理器
        /// </summary>
        private class CommandEventHandler : IExternalEventHandler
        {
            private readonly ConcurrentQueue<Action<UIApplication>> _actions =
                new ConcurrentQueue<Action<UIApplication>>();

            public void EnqueueAction(Action<UIApplication> action)
            {
                _actions.Enqueue(action);
            }

            public void Execute(UIApplication app)
            {
                // 一次清空佇列；Execute 執行中若有新命令進來，
                // 其 EnqueueAction 先於 Raise()，不是被本迴圈消化
                // 就是觸發下一次 Execute，兩者皆不遺失。
                while (_actions.TryDequeue(out var action))
                {
                    try
                    {
                        action?.Invoke(app);
                    }
                    catch (Exception ex)
                    {
                        TaskDialog.Show("命令執行錯誤", ex.Message);
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
