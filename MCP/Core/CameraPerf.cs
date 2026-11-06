using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace RevitMCP.Core
{
    /// <summary>
    /// 2026-09-26 第四輪（使用者：「每點一個視圖都會卡一下」「運算量很大」）：相機管理各步驟的 Stopwatch 計時，
    /// 寫進 RevitMCP log，格式「⏱ 名稱 總計 N ms [步驟1=a ms, 步驟2=b ms]」，讓實機量測知道時間花在哪裡。
    /// 用法：using (var perf = CameraPerf.Begin("一鍵 4 視圖")) { …; perf.Lap("讀相機參數"); … }。
    /// 任何失敗只吞掉（計時本身不可以讓流程出錯）。純受控程式，任何執行緒皆可用。
    /// </summary>
    internal sealed class CameraPerf : IDisposable
    {
        private readonly string _what;
        private readonly Stopwatch _total = Stopwatch.StartNew();
        private long _lastLapMs;
        private readonly List<string> _laps = new List<string>();
        private string _note;
        private bool _disposed;

        private CameraPerf(string what) { _what = what ?? "?"; }

        internal static CameraPerf Begin(string what) => new CameraPerf(what);

        /// <summary>記一段：自上一個 Lap（或開始）到現在的毫秒數。</summary>
        internal void Lap(string step)
        {
            try
            {
                long now = _total.ElapsedMilliseconds;
                _laps.Add($"{step}={now - _lastLapMs}ms");
                _lastLapMs = now;
            }
            catch { }
        }

        /// <summary>附加說明（例：「快取命中 18/21」）。</summary>
        internal void Note(string note)
        {
            try { _note = string.IsNullOrEmpty(_note) ? note : _note + "；" + note; } catch { }
        }

        internal long ElapsedMs => _total.ElapsedMilliseconds;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try
            {
                _total.Stop();
                Logger.Info($"⏱ {_what} 總計 {_total.ElapsedMilliseconds} ms" +
                            (_laps.Count > 0 ? " [" + string.Join(", ", _laps) + "]" : "") +
                            (string.IsNullOrEmpty(_note) ? "" : "（" + _note + "）"));
            }
            catch { }
        }
    }
}
