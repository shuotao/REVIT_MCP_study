using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitMCP.Core;

#if REVIT2025_OR_GREATER
using IdType = System.Int64;
#else
using IdType = System.Int32;
#endif

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

        /// <summary>
        /// Transform 可信度判定（domain doc §3，2026-08-05 對齊）：
        /// finite、可逆（det != 0）、conformal 等比例（三軸基向量長度相等，5% 容差）。
        /// 純鏡射（det &lt; 0 但仍等比例）允許但標記警告；非等比例一律不可信。
        /// </summary>
        static bool CheckTransformTrust(Transform t, out bool isMirrored, out string reason)
        {
            isMirrored = false;
            reason = "";

            double[] components =
            {
                t.BasisX.X, t.BasisX.Y, t.BasisX.Z,
                t.BasisY.X, t.BasisY.Y, t.BasisY.Z,
                t.BasisZ.X, t.BasisZ.Y, t.BasisZ.Z,
                t.Origin.X, t.Origin.Y, t.Origin.Z,
            };
            if (components.Any(v => double.IsNaN(v) || double.IsInfinity(v)))
            {
                reason = "transform 含 NaN/Infinity 分量";
                return false;
            }

            double det = t.Determinant;
            if (Math.Abs(det) < 1e-9)
            {
                reason = "transform 不可逆（determinant 接近 0）";
                return false;
            }

            double lenX = t.BasisX.GetLength();
            double lenY = t.BasisY.GetLength();
            double lenZ = t.BasisZ.GetLength();
            double maxLen = Math.Max(lenX, Math.Max(lenY, lenZ));
            double minLen = Math.Min(lenX, Math.Min(lenY, lenZ));
            bool conformal = maxLen > 1e-9 && (maxLen - minLen) / maxLen <= 0.05;

            if (!conformal)
            {
                reason = string.Format("非等比例縮放（軸長 {0:F4}/{1:F4}/{2:F4}），無法信任", lenX, lenY, lenZ);
                return false;
            }

            isMirrored = det < 0;
            if (isMirrored) reason = "純鏡射，允許但已標記警告";
            return true;
        }

        /// <summary>
        /// discover + 座標鏈健檢 + duplicate/unsupported_family 判定，preview 與 create 共用。
        /// 不開啟 Transaction；create 呼叫本方法取得權威結果後才寫入模型
        /// （鐵則：create 不信任 preview 快取，以相同參數重新掃描）。
        /// </summary>
        static List<JObject> BuildPlacementPlan(Document doc, ViewPlan vp, JObject p, out JObject summary)
        {
            string importInstanceUniqueId = (string)p["importInstanceUniqueId"];
            string blockNameFilter = (string)p["blockName"];
            string familySymbolIdRaw = (string)p["familySymbolId"];
            string levelIdRaw = (string)p["levelId"];
            double offsetMm = p["offsetMm"] != null ? p["offsetMm"].Value<double>() : 0.0;
            bool toleranceProvided = p["duplicateToleranceMm"] != null;
            double toleranceMm = toleranceProvided
                ? p["duplicateToleranceMm"].Value<double>()
                : DefaultDuplicateToleranceMm;

            if (string.IsNullOrEmpty(familySymbolIdRaw))
                throw new ArgumentException("familySymbolId 為必填，本工具不自動選擇族群");
            if (string.IsNullOrEmpty(levelIdRaw))
                throw new ArgumentException("levelId 為必填，本工具不自動選擇樓層");

            IdType familySymbolIdVal, levelIdVal;
            if (!IdType.TryParse(familySymbolIdRaw, out familySymbolIdVal))
                throw new ArgumentException("familySymbolId 必須是數字字串，收到：" + familySymbolIdRaw);
            if (!IdType.TryParse(levelIdRaw, out levelIdVal))
                throw new ArgumentException("levelId 必須是數字字串，收到：" + levelIdRaw);

            var symbol = doc.GetElement(new ElementId(familySymbolIdVal)) as FamilySymbol;
            if (symbol == null)
                throw new ArgumentException("找不到 familySymbolId=" + familySymbolIdRaw + " 對應的 FamilySymbol");

            var level = doc.GetElement(new ElementId(levelIdVal)) as Level;
            if (level == null)
                throw new ArgumentException("找不到 levelId=" + levelIdRaw + " 對應的 Level（v1 不自動建立樓層）");

            bool familySupported = symbol.Family.FamilyPlacementType == FamilyPlacementType.OneLevelBased;

            var cad = FindLinkedImportInstance(doc, vp, importInstanceUniqueId);
            var all = CollectBlockCandidates(cad, vp);
            if (!string.IsNullOrEmpty(blockNameFilter))
                all = all.Where(c => c.DisplayName == blockNameFilter).ToList();

            double offsetFt = offsetMm * MmFt;
            double toleranceFt = toleranceMm * MmFt;

            var existingInstances = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilyInstance))
                .Cast<FamilyInstance>()
                .Where(fi => fi.LevelId == level.Id)
                .Select(fi => new { fi.Id, Point = (fi.Location as LocationPoint)?.Point })
                .Where(x => x.Point != null)
                .ToList();

            var plan = new List<JObject>();
            var seenInBatch = new List<KeyValuePair<string, XYZ>>();
            int ready = 0, duplicate = 0, unsupported = 0, untrustworthy = 0;

            foreach (var c in all)
            {
                var finalPoint = new XYZ(c.ResolvedPoint.X, c.ResolvedPoint.Y, level.Elevation + offsetFt);
                bool mirrored;
                string trustReason;
                bool trustworthy = CheckTransformTrust(c.TotalTransform.Multiply(c.BlockTransform), out mirrored, out trustReason);

                string status;
                string statusReason = "";
                ElementId duplicateOf = null;

                if (!familySupported)
                {
                    status = "unsupported_family";
                    statusReason = "familySymbol 的 FamilyPlacementType 為 " + symbol.Family.FamilyPlacementType
                        + "，v1 僅支援 OneLevelBased";
                    unsupported++;
                }
                else if (!trustworthy)
                {
                    status = "untrustworthy_transform";
                    statusReason = trustReason;
                    untrustworthy++;
                }
                else
                {
                    var existingDup = existingInstances.FirstOrDefault(x => x.Point.DistanceTo(finalPoint) < toleranceFt);
                    var batchDupIdx = seenInBatch.FindIndex(x => x.Value.DistanceTo(finalPoint) < toleranceFt);

                    if (existingDup != null)
                    {
                        status = "duplicate_existing";
                        statusReason = "與既有 FamilyInstance ElementId=" + existingDup.Id.GetIdValue()
                            + " 距離 <" + toleranceMm + "mm";
                        duplicateOf = existingDup.Id;
                        duplicate++;
                    }
                    else if (batchDupIdx >= 0)
                    {
                        status = "duplicate_in_batch";
                        statusReason = "與本次掃描內候選 identity=" + seenInBatch[batchDupIdx].Key
                            + " 距離 <" + toleranceMm + "mm";
                        duplicate++;
                    }
                    else
                    {
                        status = "ready";
                        ready++;
                        seenInBatch.Add(new KeyValuePair<string, XYZ>(c.Identity, finalPoint));
                    }
                }

                plan.Add(new JObject
                {
                    ["identity"] = c.Identity,
                    ["blockName"] = c.DisplayName,
                    ["status"] = status,
                    ["statusReason"] = statusReason,
                    ["mirrored"] = mirrored,
                    ["duplicateOfElementId"] = duplicateOf == null ? null : (long?)duplicateOf.GetIdValue(),
                    ["coordinateChain"] = new JObject
                    {
                        ["blockInsertionPointMm"] = PointToMmJson(c.InsertionPoint),
                        ["blockTransformOriginMm"] = PointToMmJson(c.BlockTransform.Origin),
                        ["totalTransformOriginMm"] = PointToMmJson(c.TotalTransform.Origin),
                        ["resolvedPointMm"] = PointToMmJson(finalPoint),
                    },
                });
            }

            summary = new JObject
            {
                ["totalCandidates"] = all.Count,
                ["ready"] = ready,
                ["duplicate"] = duplicate,
                ["unsupportedFamily"] = unsupported,
                ["untrustworthyTransform"] = untrustworthy,
                ["duplicateToleranceMm"] = toleranceMm,
                ["duplicateToleranceSource"] = toleranceProvided ? "user-provided" : "default",
                ["familySymbolId"] = symbol.Id.GetIdValue(),
                ["levelId"] = level.Id.GetIdValue(),
                ["offsetMm"] = offsetMm,
            };
            return plan;
        }

        public static object PreviewFamilyInstancesFromDwgBlocks(Document doc, JObject p)
        {
            var vp = doc.ActiveView as ViewPlan;
            if (vp == null)
                throw new InvalidOperationException("請先切換到平面視圖再執行本工具");
            if (p == null)
                throw new ArgumentException("缺少參數：familySymbolId 與 levelId 為必填");

            JObject summary;
            var plan = BuildPlacementPlan(doc, vp, p, out summary);
            summary["candidates"] = new JArray(plan);
            return summary;
        }

        public static object CreateFamilyInstancesFromDwgBlocks(Document doc, JObject p)
        {
            var vp = doc.ActiveView as ViewPlan;
            if (vp == null)
                throw new InvalidOperationException("請先切換到平面視圖再執行本工具");
            if (p == null)
                throw new ArgumentException("缺少參數：familySymbolId 與 levelId 為必填");

            bool skipDuplicates = p["skipDuplicates"] != null && p["skipDuplicates"].Value<bool>();

            // 鐵則：不信任呼叫端可能挾帶的舊 preview 結果，以相同參數重新掃描一次。
            JObject summary;
            var plan = BuildPlacementPlan(doc, vp, p, out summary);

            IdType familySymbolIdVal = IdType.Parse((string)p["familySymbolId"]);
            var symbol = (FamilySymbol)doc.GetElement(new ElementId(familySymbolIdVal));
            IdType levelIdVal = IdType.Parse((string)p["levelId"]);
            var level = (Level)doc.GetElement(new ElementId(levelIdVal));

            var perItemResults = new JArray();
            int created = 0, failed = 0, skipped = 0;

            using (var tx = TransactionHelper.Begin(doc, "從 CAD 圖塊建立點位族群"))
            {
                tx.Start();

                if (!symbol.IsActive)
                {
                    symbol.Activate();
                    doc.Regenerate();
                }

                foreach (var candidate in plan)
                {
                    string status = (string)candidate["status"];
                    string identity = (string)candidate["identity"];
                    bool isDuplicate = status == "duplicate_existing" || status == "duplicate_in_batch";

                    if (status != "ready")
                    {
                        // unsupported_family / untrustworthy_transform 一律不建立；
                        // duplicate 只有在呼叫端明確傳入 skipDuplicates=true 時視為「已核准略過」，
                        // 否則視為阻擋（domain doc §4.5：核准不得由 agent 自行推定）。
                        skipped++;
                        perItemResults.Add(new JObject
                        {
                            ["identity"] = identity,
                            ["outcome"] = isDuplicate
                                ? (skipDuplicates ? "skipped_duplicate_approved" : "blocked_duplicate_not_approved")
                                : "blocked",
                            ["status"] = status,
                            ["statusReason"] = candidate["statusReason"],
                        });
                        continue;
                    }

                    using (var sub = new SubTransaction(doc))
                    {
                        sub.Start();
                        try
                        {
                            var chain = (JObject)candidate["coordinateChain"];
                            var resolvedMm = (JObject)chain["resolvedPointMm"];
                            var point = new XYZ(
                                resolvedMm.Value<double>("x") * MmFt,
                                resolvedMm.Value<double>("y") * MmFt,
                                resolvedMm.Value<double>("z") * MmFt);

                            // KNOWN UNCERTAINTY（見 plan「Known Revit-API Uncertainties」#2）：
                            // OneLevelBased 族群以含 offset 的 Z 傳入 NewFamilyInstance，
                            // 依 Revit 自身的 level-instance offset 記帳；Offset 參數實際落值
                            // 需真機放置後 read-back 驗證。
                            var instance = doc.Create.NewFamilyInstance(
                                point, symbol, level,
                                Autodesk.Revit.DB.Structure.StructuralType.NonStructural);

                            sub.Commit();
                            created++;
                            perItemResults.Add(new JObject
                            {
                                ["identity"] = identity,
                                ["outcome"] = "created",
                                ["elementId"] = instance.Id.GetIdValue(),
                            });
                        }
                        catch (Exception ex)
                        {
                            sub.RollBack();
                            failed++;
                            perItemResults.Add(new JObject
                            {
                                ["identity"] = identity,
                                ["outcome"] = "failed",
                                ["error"] = ex.Message,
                            });
                        }
                    }
                }

                tx.Commit();
            }

            // 建立後逐一獨立查詢驗證存在（同步、確定性，不依賴 Idling 事件輪詢）。
            foreach (var item in perItemResults.OfType<JObject>().Where(i => (string)i["outcome"] == "created"))
            {
                var id = new ElementId((IdType)item["elementId"].Value<long>());
                item["verifiedExists"] = doc.GetElement(id) != null;
            }

            return new JObject
            {
                ["created"] = created,
                ["failed"] = failed,
                ["skipped"] = skipped,
                ["duplicateToleranceMm"] = summary["duplicateToleranceMm"],
                ["duplicateToleranceSource"] = summary["duplicateToleranceSource"],
                ["items"] = perItemResults,
            };
        }
    }
}
