using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;

namespace RevitMCP.Core
{
    /// <summary>
    /// 2026-09-25（規格 §4.8 步驟 0）：記錄「相機是從哪張平面放置的」。Revit 本身不存這個資訊
    /// （主 session 以 MCP 查證：相機 View3D 沒有任何參數指向來源平面），所以自己記：
    ///   1. Application.OnCameraManagerDocumentChanged 看到新增的透視 View3D 時，讀當下的作用中視圖
    ///      （放置相機的交易 Commit 時 Revit 尚未切到新的 3D 視圖——實際時序以 log 驗證），
    ///      是 ViewPlan 就呼叫 <see cref="Enqueue"/>。DocumentChanged 內不可修改文件。
    ///   2. 下一次 Idling 呼叫 <see cref="FlushPending"/>，開一個小交易把平面 UniqueId 用 Extensible
    ///      Storage 寫在相機 View3D 上。
    /// Schema 只有一個 string 欄位（不需要 Spec／單位；double 欄位才需要，見 CameraKeyPlanService 的
    /// 「Units are required」教訓）。固定 GUID，不要改——寫進使用者 .rvt 後靠它辨識。
    /// </summary>
    internal static class CameraSourcePlanStore
    {
        private static readonly Guid SchemaGuid = new Guid("8D9C90BC-FF71-4EFB-904B-6E4B61053C98");
        private const string SchemaName = "RevitMCP_CameraSourcePlan";
        private const string FieldPlanUniqueId = "PlanUniqueId";
        internal const string TransactionName = "記錄相機放置平面";

        /// <summary>寫入交易進行中（含 Commit 當下觸發的 DocumentChanged）：Application 據此忽略自己的寫入。</summary>
        internal static bool IsWriting { get; private set; }

        private class PendingItem
        {
            public Document Doc;
            public ElementId CameraId;
            public ElementId PlanId;
        }
        private static readonly List<PendingItem> _pending = new List<PendingItem>();

        internal static bool HasPending => _pending.Count > 0;

        private static Schema GetOrCreateSchema()
        {
            Schema schema = Schema.Lookup(SchemaGuid);
            if (schema != null) return schema;
            SchemaBuilder builder = new SchemaBuilder(SchemaGuid);
            builder.SetSchemaName(SchemaName);
            builder.SetReadAccessLevel(AccessLevel.Public);
            builder.SetWriteAccessLevel(AccessLevel.Public);
            builder.AddSimpleField(FieldPlanUniqueId, typeof(string));
            return builder.Finish();
        }

        /// <summary>DocumentChanged 內呼叫：只排隊，不碰文件。</summary>
        internal static void Enqueue(Document doc, ElementId cameraId, ElementId planId)
        {
            if (doc == null || cameraId == null || planId == null) return;
            _pending.Add(new PendingItem { Doc = doc, CameraId = cameraId, PlanId = planId });
        }

        /// <summary>Idling 內呼叫（API context、沒有開啟中的交易）。任何失敗只記 log。</summary>
        internal static void FlushPending()
        {
            if (_pending.Count == 0) return;
            List<PendingItem> items = new List<PendingItem>(_pending);
            _pending.Clear();
            foreach (PendingItem it in items)
            {
                try
                {
                    Document doc = it.Doc;
                    if (doc == null || !doc.IsValidObject || doc.IsReadOnly || doc.IsModifiable)
                    {
                        Logger.Info($"相機管理：略過記錄相機放置平面（文件不可寫或正有交易） camera id={it.CameraId.GetIdValue()}");
                        continue;
                    }
                    View3D cam = doc.GetElement(it.CameraId) as View3D;
                    ViewPlan plan = doc.GetElement(it.PlanId) as ViewPlan;
                    if (cam == null || plan == null) { Logger.Info($"相機管理：記錄相機放置平面失敗（相機或平面已不存在） camera id={it.CameraId.GetIdValue()} plan id={it.PlanId.GetIdValue()}"); continue; }
                    IsWriting = true;
                    try
                    {
                        using (Transaction t = new Transaction(doc, TransactionName))
                        {
                            t.Start();
                            Schema schema = GetOrCreateSchema();
                            Entity e = new Entity(schema);
                            e.Set(schema.GetField(FieldPlanUniqueId), plan.UniqueId ?? "");
                            cam.SetEntity(e);
                            TransactionHelper.CommitOrThrow(t);
                        }
                    }
                    finally { IsWriting = false; }
                    Logger.Info($"相機管理：已記錄相機「{cam.Name}」id={cam.Id.GetIdValue()} 的放置平面「{plan.Name}」id={plan.Id.GetIdValue()}（ES {SchemaName}）");
                }
                catch (Exception ex)
                {
                    Logger.Error($"相機管理：記錄相機放置平面失敗 camera id={it.CameraId?.GetIdValue()}", ex);
                }
            }
        }

        /// <summary>讀相機記錄的放置平面（仍存在的 ViewPlan）；沒有記錄／平面已刪除＝null。API context 內呼叫。</summary>
        internal static ViewPlan TryGetSourcePlan(View3D camera)
        {
            try
            {
                if (camera == null) return null;
                Schema schema = Schema.Lookup(SchemaGuid);
                if (schema == null) return null;
                Entity e = camera.GetEntity(schema);
                if (e == null || !e.IsValid()) return null;
                string uid = e.Get<string>(schema.GetField(FieldPlanUniqueId));
                if (string.IsNullOrEmpty(uid)) return null;
                ViewPlan plan = camera.Document.GetElement(uid) as ViewPlan;
                // 2026-09-26 第七輪：相機若放置在本工具產生的 Key Plan 平面／總覽上，不拿它當底圖來源（步驟 0 略過）。
                if (plan != null && CameraKeyPlanService.IsToolGeneratedView(plan)) return null;
                return plan;
            }
            catch (Exception ex)
            {
                Logger.Error("相機管理：讀取相機放置平面失敗（略過步驟 0）", ex);
                return null;
            }
        }
    }
}
