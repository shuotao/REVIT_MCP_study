using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace RevitMCP.Core
{
    /// <summary>
    /// 相機管理面板的崩潰診斷（2026-09-24 第二輪崩潰調查新增）。
    ///
    /// 背景：開檔後約 5–10 秒 Revit 以 0xe0434352（未處理 CLR 例外）崩潰，但 Windows 事件記錄檔
    /// 對最近兩次崩潰沒有留下任何 .NET 堆疊，RevitMCP 自己的 log 也在崩潰前停住——我們手上完全
    /// 沒有例外型別與堆疊。這個類別的唯一目的就是「下一次崩潰時一定要留下真正的堆疊」：
    ///   • AppDomain.UnhandledException：最後一道，任何執行緒（含 GC finalizer 執行緒）上的未處理
    ///     例外，記錄 ExceptionObject.ToString()、執行緒 id/名稱、IsTerminating。
    ///   • AppDomain.FirstChanceException：只在以下情況記錄（平時完全不寫，成本僅一次旗標判斷）：
    ///       ① 目前有相機管理操作正在進行（<see cref="BeginOperation"/> 計數 &gt; 0）；
    ///       ② 位於「觀察窗」內（面板 Loaded／文件開啟／任何面板操作後的一段時間，見 <see cref="ExtendWatch"/>）；
    ///       ③ 丟出例外的方法本身屬於 RevitMCP 命名空間。
    ///     第一機會例外當下 Exception.StackTrace 只有丟出點那一層，所以另外用
    ///     <see cref="System.Diagnostics.StackTrace"/> 抓「此刻整條呼叫堆疊」一起寫入。
    ///     總筆數上限 <see cref="MaxRecords"/>、同簽章上限 <see cref="MaxPerSignature"/>，避免洗版。
    ///   • TaskScheduler.UnobservedTaskException。
    ///   • WPF Dispatcher.UnhandledException（由面板呼叫 <see cref="RecordDispatcherException"/>，
    ///     不論是不是本外掛的例外都記錄，但只有本外掛的才標記 Handled）。
    ///   • 版面風暴偵測（<see cref="AttachLayoutStormDetector"/>）：WPF 版面若陷入無限重算迴圈，
    ///     UI 執行緒會一直忙、Revit 永遠進不了 Idle（journal 顯示崩潰前 5 秒內沒有任何
    ///     ExternalEvent 被執行，與此現象相符但未證實），這裡記錄每秒版面次數異常的時間點。
    ///
    /// 寫入方式：一律「同步、直寫到磁碟」（FileOptions.WriteThrough + Flush(true)），寫進
    /// %APPDATA%\RevitMCP\Logs\CameraManagerCrash_yyyyMMdd.log——程序下一刻就可能被終止，
    /// 不能依賴任何緩衝。每個 handler 都自我保護：絕不再拋例外，且用 [ThreadStatic] 旗標防止
    /// 「記錄時自己又觸發第一機會例外」的遞迴。
    /// </summary>
    internal static class CameraManagerDiagnostics
    {
        private const int MaxRecords = 1000;
        private const int MaxPerSignature = 3;

        private static readonly object FileLock = new object();
        private static readonly Dictionary<string, int> SignatureCounts = new Dictionary<string, int>();
        private static string _crashLogPath;
        private static bool _installed;
        private static int _recordCount;
        private static int _operationDepth;
        private static long _watchUntilUtcTicks;
        private static volatile string _currentOperation;

        [ThreadStatic] private static bool _inHandler;

        /// <summary>崩潰紀錄檔完整路徑（給報告／日誌引用）。</summary>
        public static string CrashLogPath
        {
            get
            {
                if (_crashLogPath == null)
                {
                    try
                    {
                        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RevitMCP", "Logs");
                        Directory.CreateDirectory(dir);
                        _crashLogPath = Path.Combine(dir, $"CameraManagerCrash_{DateTime.Now:yyyyMMdd}.log");
                    }
                    catch
                    {
                        _crashLogPath = Path.Combine(Path.GetTempPath(), "RevitMCP_CameraManagerCrash.log");
                    }
                }
                return _crashLogPath;
            }
        }

        /// <summary>在 Application.OnStartup 最前面呼叫一次。</summary>
        public static void Install()
        {
            if (_installed) return;
            _installed = true;
            try
            {
                AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
                AppDomain.CurrentDomain.FirstChanceException += OnFirstChanceException;
                TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
                Write("INSTALL", "崩潰診斷已安裝（pid=" + System.Diagnostics.Process.GetCurrentProcess().Id + "）。");
            }
            catch
            {
                // 安裝失敗不能影響外掛載入。
            }
        }

        /// <summary>包住每一個相機管理操作（重新整理、匯出底圖、畫地圖、一鍵 4 視圖…）。
        /// 期間任何第一機會例外都會被記錄；結束後再延長觀察窗 30 秒（延遲發生的版面／渲染例外）。</summary>
        public static IDisposable BeginOperation(string name)
        {
            Interlocked.Increment(ref _operationDepth);
            _currentOperation = name;
            ExtendWatch(30, null);
            return new OperationToken(name);
        }

        /// <summary>延長觀察窗：在這段時間內「任何」第一機會例外都會被記錄（仍受筆數上限約束）。</summary>
        public static void ExtendWatch(int seconds, string reason)
        {
            try
            {
                long until = DateTime.UtcNow.AddSeconds(seconds).Ticks;
                long current = Interlocked.Read(ref _watchUntilUtcTicks);
                if (until > current) Interlocked.Exchange(ref _watchUntilUtcTicks, until);
                if (reason != null) Write("WATCH", $"觀察窗延長 {seconds} 秒：{reason}");
            }
            catch { }
        }

        private sealed class OperationToken : IDisposable
        {
            private readonly string _name;
            private int _disposed;
            public OperationToken(string name) { _name = name; }
            public void Dispose()
            {
                if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
                if (Interlocked.Decrement(ref _operationDepth) <= 0)
                {
                    Interlocked.Exchange(ref _operationDepth, 0);
                    _currentOperation = null;
                }
                ExtendWatch(30, null);
            }
        }

        // ══════════════════════════════════════════════════════════
        // handlers
        // ══════════════════════════════════════════════════════════

        private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (_inHandler) return;
            _inHandler = true;
            try
            {
                Write("UNHANDLED", $"IsTerminating={e.IsTerminating} {ThreadInfo()} op={_currentOperation ?? "-"}\n" +
                    (e.ExceptionObject?.ToString() ?? "(null ExceptionObject)"));
            }
            catch { }
            finally { _inHandler = false; }
        }

        private static void OnUnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
        {
            if (_inHandler) return;
            _inHandler = true;
            try
            {
                Write("UNOBSERVED_TASK", ThreadInfo() + "\n" + (e.Exception?.ToString() ?? "(null)"));
            }
            catch { }
            finally { _inHandler = false; }
        }

        private static void OnFirstChanceException(object sender, FirstChanceExceptionEventArgs e)
        {
            if (_inHandler) return;
            _inHandler = true;
            try
            {
                if (_recordCount >= MaxRecords) return;

                bool inOperation = Volatile.Read(ref _operationDepth) > 0;
                bool inWatch = DateTime.UtcNow.Ticks < Interlocked.Read(ref _watchUntilUtcTicks);
                bool fromOurCode = false;
                if (!inOperation && !inWatch)
                {
                    string ns = e.Exception?.TargetSite?.DeclaringType?.Namespace;
                    fromOurCode = ns != null && ns.StartsWith("RevitMCP", StringComparison.Ordinal);
                    if (!fromOurCode) return;
                }

                Exception ex = e.Exception;
                string signature = (ex?.GetType().FullName ?? "?") + "|" + (ex?.TargetSite?.DeclaringType?.FullName ?? "?") + "." + (ex?.TargetSite?.Name ?? "?") + "|" + (ex?.Message ?? "");
                lock (FileLock)
                {
                    SignatureCounts.TryGetValue(signature, out int n);
                    if (n >= MaxPerSignature) return;
                    SignatureCounts[signature] = n + 1;
                }

                string callStack;
                try { callStack = new System.Diagnostics.StackTrace(1, false).ToString(); }
                catch { callStack = "(無法取得呼叫堆疊)"; }

                string why = inOperation ? "操作中" : inWatch ? "觀察窗" : "RevitMCP 丟出";
                Write("FIRST_CHANCE", $"[{why}] {ThreadInfo()} op={_currentOperation ?? "-"}\n" +
                    $"{ex?.GetType().FullName}: {ex?.Message}\n" +
                    $"  丟出點: {ex?.TargetSite?.DeclaringType?.FullName}.{ex?.TargetSite?.Name}\n" +
                    "  呼叫堆疊（第一機會當下）:\n" + callStack);
            }
            catch { }
            finally { _inHandler = false; }
        }

        /// <summary>由面板的 Dispatcher.UnhandledException 呼叫：不論是不是本外掛的例外都完整記錄。</summary>
        public static void RecordDispatcherException(Exception ex, bool willHandle)
        {
            if (_inHandler) return;
            _inHandler = true;
            try
            {
                Write("DISPATCHER_UNHANDLED", $"willHandle={willHandle} {ThreadInfo()} op={_currentOperation ?? "-"}\n" + (ex?.ToString() ?? "(null)"));
            }
            catch { }
            finally { _inHandler = false; }
        }

        /// <summary>由佇列／UI 回呼的 catch 呼叫：已攔下的例外也完整記錄（含 InnerException）。</summary>
        public static void RecordCaught(string where, Exception ex)
        {
            if (_inHandler) return;
            _inHandler = true;
            try
            {
                Write("CAUGHT", $"{where} {ThreadInfo()}\n" + (ex?.ToString() ?? "(null)"));
            }
            catch { }
            finally { _inHandler = false; }
        }

        // ══════════════════════════════════════════════════════════
        // 版面風暴偵測
        // ══════════════════════════════════════════════════════════

        private static int _layoutCountInWindow;
        private static int _layoutWindowStartTick;
        private static int _lastStormReportTick;
        private const int LayoutStormThresholdPerSecond = 300;

        /// <summary>掛在面板根元件上。LayoutUpdated 在同一 Dispatcher 每完成一次版面計算就觸發一次，
        /// 正常情況每秒最多幾十次；超過門檻代表版面陷入重算迴圈，記錄當下尺寸（每 10 秒最多一筆）。</summary>
        public static void AttachLayoutStormDetector(FrameworkElement root, Func<string> describe)
        {
            if (root == null) return;
            root.LayoutUpdated += (s, e) =>
            {
                try
                {
                    int now = Environment.TickCount;
                    if (unchecked(now - _layoutWindowStartTick) > 1000)
                    {
                        _layoutWindowStartTick = now;
                        _layoutCountInWindow = 0;
                    }
                    if (++_layoutCountInWindow != LayoutStormThresholdPerSecond) return;
                    if (_lastStormReportTick != 0 && unchecked(now - _lastStormReportTick) < 10000) return;
                    _lastStormReportTick = now;
                    string detail;
                    try { detail = describe?.Invoke() ?? ""; } catch (Exception dex) { detail = "(describe 失敗: " + dex.Message + ")"; }
                    Write("LAYOUT_STORM", $"1 秒內版面計算 ≥ {LayoutStormThresholdPerSecond} 次（疑似版面無限重算）。op={_currentOperation ?? "-"} {detail}");
                }
                catch { }
            };
        }

        // ══════════════════════════════════════════════════════════
        // 同步直寫
        // ══════════════════════════════════════════════════════════

        public static void Write(string kind, string text)
        {
            try
            {
                string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{kind}] {text}{Environment.NewLine}";
                byte[] bytes = Encoding.UTF8.GetBytes(line);
                lock (FileLock)
                {
                    _recordCount++;
                    using (var fs = new FileStream(CrashLogPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite, 4096, FileOptions.WriteThrough))
                    {
                        fs.Write(bytes, 0, bytes.Length);
                        fs.Flush(true);
                    }
                }
            }
            catch
            {
                // 診斷寫入失敗絕不能拋出。
            }
        }

        private static string ThreadInfo()
        {
            try
            {
                Thread t = Thread.CurrentThread;
                bool isUi = System.Windows.Application.Current?.Dispatcher?.CheckAccess() ?? false;
                return $"thread={t.ManagedThreadId}({t.Name ?? (t.IsThreadPoolThread ? "threadpool" : "unnamed")}{(isUi ? ",wpf-app-ui" : "")})";
            }
            catch
            {
                return "thread=?";
            }
        }
    }
}
