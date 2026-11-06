using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;

namespace RevitMCP.Core
{
    /// <summary>
    /// 相機管理面板：「並排（TileViews）之後把每個視窗縮放到符合裁剪範圍」。
    ///
    /// 為什麼不能在同一個 ExternalEvent 內 ZoomToFit：PostCommand(TileViews) 要等本次
    /// Execute() 結束、控制權回到 Revit 訊息迴圈後才執行，並排會「事後」改變每個視窗的大小，
    /// 先前做的 ZoomToFit 以舊視窗大小為準，並排後就失效了。
    ///
    /// 做法：排程時記下各視窗目前的 GetWindowRectangle()，訂閱一次 UIApplication.Idling；
    /// 每個 Idling tick 檢查：目標視圖是否都已開啟、視窗大小是否已變動（或已等了
    /// <see cref="AssumeTiledAfterTicks"/> 個 tick）且與上一個 tick 相同（穩定），條件成立就
    /// 對每個 UIView 呼叫 ZoomToFit() 並取消訂閱；最多等 <see cref="MaxTicks"/> 個 tick，
    /// 逾時仍照做一次再取消訂閱。全程 try/catch＋log，絕不拋出。
    ///
    /// 透視圖：Revit API 文件（UIView.ZoomToFit）沒有說明透視圖上 ZoomToFit 是否會移動相機
    /// 眼睛/目標——未查證。因此透視圖採「防護式」處理：ZoomToFit 前後比對
    /// View3D.GetOrientation()，若眼睛/前方向/上方向有任何變動，立刻用交易
    /// SetOrientation 還原、記 log，並在本次 Revit 工作階段內之後都跳過透視圖的 ZoomToFit。
    /// </summary>
    internal static class CameraViewZoomService
    {
        private const int AssumeTiledAfterTicks = 3;
        private const int MaxTicks = 20;

        /// <summary>曾偵測到透視圖 ZoomToFit 會改動相機方位 → 本工作階段內之後都跳過透視圖。</summary>
        private static bool _perspectiveZoomUnsafe;

        private class PendingZoom
        {
            public UIApplication UiApp;
            public List<ElementId> ViewIds;
            public Dictionary<ElementId, string> InitialRects;
            public Dictionary<ElementId, string> LastRects;
            public int Ticks;
            public bool ExpectTile;
            /// <summary>2026-09-26 稽核：排程當下的作用中文件（標題｜路徑）；Idling 時文件換了就取消，不把 Id 套到另一份文件的同號視圖。</summary>
            public string DocKey;
            public EventHandler<IdlingEventArgs> Handler;
            public System.Diagnostics.Stopwatch Watch = System.Diagnostics.Stopwatch.StartNew(); // 2026-09-26 第四輪：⏱
        }

        private static PendingZoom _pending;

        /// <summary>
        /// 必須在 Revit API context（ExternalEvent 內）呼叫。expectTile＝true 表示呼叫端剛
        /// PostCommand(TileViews)，要等視窗大小變動並穩定後才縮放；false（「全部符合畫面」按鈕）
        /// 只等目標視圖都在、視窗大小穩定即縮放。重複排程時，舊的待辦會被取代。
        /// </summary>
        internal static void ScheduleZoomToFit(UIApplication uiApp, IEnumerable<ElementId> viewIds, bool expectTile)
        {
            try
            {
                if (uiApp == null || viewIds == null) return;
                CancelPending();

                var p = new PendingZoom
                {
                    UiApp = uiApp,
                    ViewIds = viewIds.Where(id => id != null && id != ElementId.InvalidElementId).Distinct().ToList(),
                    ExpectTile = expectTile
                };
                if (p.ViewIds.Count == 0) return;
                p.DocKey = DocKey(uiApp);
                p.InitialRects = SnapshotRects(uiApp, p.ViewIds);
                p.Handler = (s, e) => OnIdling(p);
                _pending = p;
                uiApp.Idling += p.Handler;
                Logger.Info($"相機管理面板：已排程並排後 ZoomToFit（{p.ViewIds.Count} 張視圖，expectTile={expectTile}）。");
            }
            catch (Exception ex)
            {
                Logger.Error("相機管理面板：排程 ZoomToFit 失敗（略過，不影響視圖）", ex);
                CameraManagerDiagnostics.RecordCaught("ScheduleZoomToFit", ex);
            }
        }

        private static string DocKey(UIApplication uiApp)
        {
            try
            {
                Document d = uiApp?.ActiveUIDocument?.Document;
                return d == null ? null : (d.Title ?? "") + "|" + (d.PathName ?? "");
            }
            catch { return null; }
        }

        private static void CancelPending()
        {
            PendingZoom old = _pending;
            _pending = null;
            if (old == null) return;
            try { old.UiApp.Idling -= old.Handler; } catch { /* 忽略 */ }
        }

        private static void OnIdling(PendingZoom p)
        {
            try
            {
                if (!ReferenceEquals(_pending, p)) { try { p.UiApp.Idling -= p.Handler; } catch { } return; }
                if (DocKey(p.UiApp) != p.DocKey)
                {
                    CancelPending();
                    Logger.Info("相機管理面板：排程 ZoomToFit 期間作用中文件已切換 → 取消。");
                    return;
                }
                p.Ticks++;

                Dictionary<ElementId, string> rects = SnapshotRects(p.UiApp, p.ViewIds);
                bool allOpen = rects.Count == p.ViewIds.Count;
                bool timeout = p.Ticks >= MaxTicks;

                if (!allOpen && !timeout) { p.LastRects = rects; return; }

                bool changed = !p.ExpectTile || !SameRects(rects, p.InitialRects) || p.Ticks >= AssumeTiledAfterTicks;
                bool stable = p.LastRects != null && SameRects(rects, p.LastRects);
                p.LastRects = rects;
                if (!timeout && !(changed && stable)) return;

                CancelPending();
                ZoomAllNow(p.UiApp, p.ViewIds);
                Logger.Info($"相機管理面板：並排後 ZoomToFit 完成（tick={p.Ticks}, allOpen={allOpen}, timeout={timeout}）。⏱ 排程到完成 {p.Watch.ElapsedMilliseconds} ms");
            }
            catch (Exception ex)
            {
                CancelPending();
                Logger.Error("相機管理面板：Idling ZoomToFit 失敗（已取消訂閱）", ex);
                CameraManagerDiagnostics.RecordCaught("CameraViewZoomService.OnIdling", ex);
            }
        }

        /// <summary>立即對目前已開啟的目標視圖逐一 ZoomToFit（每張獨立 try/catch）。
        /// 必須在 API context 呼叫。回傳實際縮放的視圖數。</summary>
        internal static int ZoomAllNow(UIApplication uiApp, IEnumerable<ElementId> viewIds)
        {
            int done = 0;
            UIDocument uidoc = uiApp?.ActiveUIDocument;
            if (uidoc == null || viewIds == null) return 0;
            Document doc = uidoc.Document;

            IList<UIView> open;
            try { open = uidoc.GetOpenUIViews(); }
            catch (Exception ex) { Logger.Error("相機管理面板：GetOpenUIViews 失敗", ex); return 0; }

            foreach (ElementId id in viewIds)
            {
                try
                {
                    UIView uv = open?.FirstOrDefault(v => v.ViewId == id);
                    if (uv == null) continue;

                    View3D v3 = doc.GetElement(id) as View3D;
                    if (v3 != null && v3.IsPerspective)
                    {
                        if (_perspectiveZoomUnsafe) continue;
                        ZoomPerspectiveGuarded(doc, uv, v3);
                    }
                    else
                    {
                        uv.ZoomToFit();
                    }
                    done++;
                }
                catch (Exception ex)
                {
                    Logger.Error($"相機管理面板：視圖 {id.GetIdValue()} ZoomToFit 失敗（略過）", ex);
                }
            }
            return done;
        }

        private static void ZoomPerspectiveGuarded(Document doc, UIView uv, View3D persp)
        {
            ViewOrientation3D before = persp.GetOrientation();
            uv.ZoomToFit();
            ViewOrientation3D after = persp.GetOrientation();

            double tol = 1e-6;
            bool moved = before.EyePosition.DistanceTo(after.EyePosition) > CameraMath.MmToFeet(1.0)
                || before.ForwardDirection.DistanceTo(after.ForwardDirection) > tol * 1000
                || before.UpDirection.DistanceTo(after.UpDirection) > tol * 1000;
            if (!moved) return;

            _perspectiveZoomUnsafe = true;
            Logger.Error("相機管理面板：透視圖 ZoomToFit 改動了相機方位——已還原，本工作階段之後跳過透視圖縮放。");
            try
            {
                using (Transaction t = new Transaction(doc, "還原相機方位"))
                {
                    t.Start();
                    persp.SetOrientation(new ViewOrientation3D(before.EyePosition, before.UpDirection, before.ForwardDirection));
                    TransactionHelper.CommitOrThrow(t);
                }
            }
            catch (Exception ex)
            {
                Logger.Error("相機管理面板：還原透視圖方位失敗", ex);
                CameraManagerDiagnostics.RecordCaught("ZoomPerspectiveGuarded", ex);
            }
        }

        private static Dictionary<ElementId, string> SnapshotRects(UIApplication uiApp, List<ElementId> ids)
        {
            var map = new Dictionary<ElementId, string>();
            IList<UIView> open = uiApp?.ActiveUIDocument?.GetOpenUIViews();
            if (open == null) return map;
            foreach (ElementId id in ids)
            {
                UIView uv = open.FirstOrDefault(v => v.ViewId == id);
                if (uv == null) continue;
                try
                {
                    Rectangle r = uv.GetWindowRectangle();
                    map[id] = r.Left + "," + r.Top + "," + r.Right + "," + r.Bottom;
                }
                catch { map[id] = "?"; }
            }
            return map;
        }

        private static bool SameRects(Dictionary<ElementId, string> a, Dictionary<ElementId, string> b)
        {
            if (a == null || b == null || a.Count != b.Count) return false;
            foreach (var kv in a)
                if (!b.TryGetValue(kv.Key, out string other) || other != kv.Value) return false;
            return true;
        }
    }
}
