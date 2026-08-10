using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;

namespace RevitMCP
{
    /// <summary>
    /// CAD 圖塊（Block/INSERT）插入點批次放置 Revit 點位式 FamilyInstance。
    /// 對應 domain/cad-block-point-placement.md；來源 issue #100 / #113。
    /// 對應 CommandExecutor.cs cases:
    ///   get_dwg_block_instances / preview_family_instances_from_dwg_blocks / create_family_instances_from_dwg_blocks
    /// </summary>
    internal static class CadBlockPlacementExecutor
    {
        const double FtMm = 304.8;
        const double MmFt = 1.0 / 304.8;
        const double DefaultDuplicateToleranceMm = 10.0;

        /// <summary>掃描結果的單一 Block 插入點候選。</summary>
        internal sealed class BlockCandidate
        {
            public string Identity;
            public string DisplayName;
            public XYZ InsertionPoint;
            public Transform BlockTransform;
            public Transform TotalTransform;
            public XYZ ResolvedPoint;
            public double RotationRadians;
        }

        static string BuildIdentity(string importInstanceUniqueId, int pathIndex, string blockName)
            => importInstanceUniqueId + "|" + pathIndex + "|" + blockName;

        /// <summary>
        /// 找到目前平面視圖中，UniqueId 相符（或未指定時取第一個）的 Linked ImportInstance。
        /// v1 只支援 Linked DWG，Imported 一律拒絕（domain doc §1 前置條件 5）。
        /// </summary>
        static ImportInstance FindLinkedImportInstance(Document doc, ViewPlan vp, string importInstanceUniqueId)
        {
            var candidates = new FilteredElementCollector(doc, vp.Id)
                .OfClass(typeof(ImportInstance))
                .Cast<ImportInstance>()
                .ToList();

            ImportInstance picked = string.IsNullOrEmpty(importInstanceUniqueId)
                ? candidates.FirstOrDefault()
                : candidates.FirstOrDefault(i => i.UniqueId == importInstanceUniqueId);

            if (picked == null)
                throw new InvalidOperationException(
                    string.IsNullOrEmpty(importInstanceUniqueId)
                        ? "目前平面視圖找不到任何 CAD ImportInstance，請確認已連結 DWG"
                        : "找不到 UniqueId 為「" + importInstanceUniqueId + "」的 ImportInstance");

            bool isLinked = doc.GetElement(picked.GetTypeId()) is CADLinkType && picked.IsLinked;
            if (!isLinked)
                throw new InvalidOperationException(
                    "v1 只支援 Linked DWG，偵測到的 ImportInstance 是 Imported（非 Linked），請改用連結方式重新匯入");

            return picked;
        }

        /// <summary>
        /// 遍歷 ImportInstance 幾何樹，收集每個 Block（INSERT，GeometryInstance）的插入點與 transform。
        /// 深度優先、depth 上限 5（比照 DwgColumnExecutor.CollectInstancePoints 慣例）。
        /// </summary>
        static List<BlockCandidate> CollectBlockCandidates(ImportInstance cad, ViewPlan vp)
        {
            var result = new List<BlockCandidate>();
            var opt = new Options { ComputeReferences = true, IncludeNonVisibleObjects = true, View = vp };
            var geomElem = cad.get_Geometry(opt);
            if (geomElem == null) return result;

            var totalTransform = cad.GetTotalTransform();
            int pathIndex = 0;
            WalkGeometry(cad.Document, geomElem, cad.UniqueId, totalTransform, ref pathIndex, result, 0);
            return result;
        }

        static void WalkGeometry(
            Document doc,
            GeometryElement geomElem,
            string importInstanceUniqueId,
            Transform totalTransform,
            ref int pathIndex,
            List<BlockCandidate> result,
            int depth)
        {
            if (depth > 5) return;

            foreach (var obj in geomElem)
            {
                var gi = obj as GeometryInstance;
                if (gi == null) continue;

                string blockName = TryGetBlockDisplayName(doc, gi, pathIndex);
                var candidate = new BlockCandidate
                {
                    Identity = BuildIdentity(importInstanceUniqueId, pathIndex, blockName),
                    DisplayName = blockName,
                    InsertionPoint = gi.Transform.Origin,
                    BlockTransform = gi.Transform,
                    TotalTransform = totalTransform,
                    RotationRadians = Math.Atan2(gi.Transform.BasisX.Y, gi.Transform.BasisX.X),
                };
                candidate.ResolvedPoint = totalTransform.OfPoint(gi.Transform.Origin);
                result.Add(candidate);
                pathIndex++;

                var nested = gi.GetInstanceGeometry();
                if (nested != null)
                    WalkGeometry(doc, nested, importInstanceUniqueId, totalTransform, ref pathIndex, result, depth + 1);
            }
        }

        /// <summary>
        /// KNOWN UNCERTAINTY（見 plan「Known Revit-API Uncertainties」#1）：
        /// GeometryInstance 不保證暴露 CAD block 定義名稱字串（issue #100 的 A$C87ebd845 式
        /// 名稱來自 AutoCAD 自動命名）。此處以 GraphicsStyleCategory 名稱（比照
        /// DwgColumnExecutor 的圖層名解析慣例）作 best-effort 顯示名，取不到時
        /// fallback 為合成序號標籤；呼叫端從 nameSource 欄位得知來源。
        /// 需以真實連結 DWG 實測是否能取得更精確的 block 名稱來源。
        /// </summary>
        static string TryGetBlockDisplayName(Document doc, GeometryInstance gi, int pathIndex)
        {
            try
            {
                if (gi.GraphicsStyleId != null && gi.GraphicsStyleId != ElementId.InvalidElementId)
                {
                    var gs = doc.GetElement(gi.GraphicsStyleId) as GraphicsStyle;
                    var name = gs?.GraphicsStyleCategory?.Name;
                    if (!string.IsNullOrEmpty(name)) return name;
                }
            }
            catch { }
            return "Block#" + pathIndex;
        }

        public static object GetDwgBlockInstances(Document doc, JObject p)
        {
            var vp = doc.ActiveView as ViewPlan;
            if (vp == null)
                throw new InvalidOperationException("請先切換到平面視圖再執行本工具");

            string importInstanceUniqueId = p == null ? null : (string)p["importInstanceUniqueId"];
            var cad = FindLinkedImportInstance(doc, vp, importInstanceUniqueId);
            var all = CollectBlockCandidates(cad, vp);

            var grouped = all
                .GroupBy(c => c.DisplayName)
                .Select(g => new JObject
                {
                    ["blockName"] = g.Key,
                    ["nameSource"] = g.Key.StartsWith("Block#") ? "fallback" : "graphics-style",
                    ["count"] = g.Count(),
                    ["sample"] = new JArray(g.Take(3).Select(c => new JObject
                    {
                        ["identity"] = c.Identity,
                        ["insertionPointMm"] = PointToMmJson(c.InsertionPoint),
                        ["rotationDegrees"] = Math.Round(c.RotationRadians * 180.0 / Math.PI, 2),
                    })),
                })
                .ToList();

            return new JObject
            {
                ["importInstanceUniqueId"] = cad.UniqueId,
                ["totalPoints"] = all.Count,
                ["blockTypes"] = grouped.Count,
                ["blocks"] = new JArray(grouped),
            };
        }

        static JObject PointToMmJson(XYZ pt) => new JObject
        {
            ["x"] = Math.Round(pt.X * FtMm, 1),
            ["y"] = Math.Round(pt.Y * FtMm, 1),
            ["z"] = Math.Round(pt.Z * FtMm, 1),
        };
    }
}
