# CAD Block Point Placement (discover/preview/create) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement the three MCP tools tracked by [issue #113](https://github.com/shuotao/REVIT_MCP_study/issues/113) — `get_dwg_block_instances` (discover), `preview_family_instances_from_dwg_blocks` (preview), `create_family_instances_from_dwg_blocks` (create) — that batch-convert CAD Block/INSERT insertion points (e.g. sprinkler/valve symbols) from a **linked** DWG into Revit point-placed `FamilyInstance`s, per the spec in `domain/cad-block-point-placement.md` and the engineering decisions reached in issue #100.

**Architecture:** One new C# executor file (`MCP/Core/CadBlockPlacementExecutor.cs`, static class, mirrors the existing `DwgColumnExecutor.cs` structure) implementing all three commands, wired into `MCP/Core/CommandExecutor.cs`'s switch dispatcher; one new TS tool-definition file (`MCP-Server/src/tools/cad-block-placement-tools.ts`) registered in `MCP-Server/src/tools/index.ts`. `preview` and `create` share a private discovery+matching routine so `create` re-scans from source instead of trusting client-cached coordinates (mandated by the domain SOP).

**Tech Stack:** C# / Revit API (`RevitAPI.dll`, `RevitAPIUI.dll`), Newtonsoft.Json (`JObject`), TypeScript (`@modelcontextprotocol/sdk`), no unit-test harness exists in this repo for Revit-API code — verification is `dotnet build` + manual live-Revit smoke test (see Task 8), matching this project's established practice (confirmed: no `*.Tests.csproj` anywhere in the repo).

## Global Constraints

- MCP tool names use snake_case (`CLAUDE.md` Code Conventions).
- Revit model changes must run inside `Transaction` and be reversible (`CLAUDE.md`).
- C# namespace: `RevitMCP`.
- C# command payloads use the existing `RevitCommandRequest`/`RevitCommandResponse` shape; do not invent a new wire protocol.
- `preview` is read-only — **must not** open any `Transaction`.
- `create` must re-scan from source with the same parameters as `preview` — never trust client-supplied cached coordinates (`domain/cad-block-point-placement.md` §2, "鐵則").
- `create` uses one main `Transaction` + one `SubTransaction` per placed instance; a single item's failure must not roll back other already-succeeded items (`domain/cad-block-point-placement.md` §4.4).
- v1 supports **Linked DWG only** — Imported DWG must be rejected with a clear error (issue #100 resolved policy 1, 2026-08-05).
- v1 supports only `FamilyPlacementType.OneLevelBased` FamilySymbols — anything else returns `unsupported_family`, no placement attempt (`domain/cad-block-point-placement.md` §4.3/§5).
- `familySymbolId`, `levelId` must be explicitly supplied by the caller — no auto-selection (`domain/cad-block-point-placement.md` §5).
- Transform-not-trustworthy → stop and warn, never guess a correction (`domain/cad-block-point-placement.md` core principle, repeated in §3).
- Duplicate tolerance defaults to 10mm, caller-overridable; response must state whether the default or a caller-supplied value was used (issue #100 resolved policy, NicheSam 2026-08-05 table row 2).
- `create` may only skip duplicates when the caller passes an **explicit** approval parameter — never inferred by the agent (issue #100 resolved policy 2, condition 3, shuotao 2026-08-10).
- Preview must always list every duplicate in full — never silently merge or drop (issue #100 resolved policy 2, condition 2).
- Offsets are expressed in **millimeters** at the tool boundary (matching every other mm-facing tool in this codebase, e.g. `DwgColumnExecutor`'s `FtMm`/`MmFt` constants) and converted to Revit internal feet inside the executor — this resolves domain doc §4.2's open TODO about repeating the dwg-column mm/feet trap.
- Discover must return both the human-readable CAD name and a tool-internal identity string; downstream tools consume the identity, never re-derive it from the name (`domain/cad-block-point-placement.md` §"discover 必須同時保留...").

## Known Revit-API Uncertainties (flagged, not guessed away)

I do not have a live Revit session connected in this environment (no Revit MCP tools were available this session, and `CLAUDE.md`'s "MCP Connection Status" section requires exactly that before claiming live Revit behavior). Two specific mechanics below are written using the most defensible Revit API approach I know, but **must be confirmed against a live Revit 2024+ session with a real linked DWG before this is considered done** — this mirrors the project's own culture (every comment on issue #100 insists on live-Revit evidence over assumption):

1. **Block "friendly name" extraction.** `GeometryInstance` in an imported/linked CAD's geometry tree does not reliably expose a block-definition name as a simple string property across Revit API versions. Task 2's code extracts a name via `GeometryInstance.Symbol`/`GraphicsStyle` where available and falls back to a synthesized `"Block#{index}"` label when it isn't — the fallback path needs live confirmation of whether Revit actually withholds the name (as evidenced by issue #100's `A$C87ebd845`-style anonymous-block name, which came from AutoCAD's own auto-naming, not something Revit invents).
2. **Offset-to-Z mechanic for `NewFamilyInstance`.** Task 4 places instances via `doc.Create.NewFamilyInstance(XYZ, FamilySymbol, Level, StructuralType.NonStructural)` with the XYZ's Z pre-computed as `level.Elevation + offsetFeet`, relying on Revit's own level-instance offset bookkeeping rather than manually writing an `INSTANCE_ELEVATION_PARAM`-style parameter (parameter name varies by family template). This is the standard approach for `OneLevelBased` families but needs a live placement + parameter read-back to confirm the "Offset" instance parameter lands on the expected value.

---

### Task 1: Resolve and commit the domain SOP's outstanding TODOs

The committed `domain/cad-block-point-placement.md` (both on `upstream/main` and the stale `upstream/domain/sc-opening-cad-placement` branch) is still the original v0.1 skeleton with all 13 TODOs open — despite issue #100's 2026-08-10 comment claiming 10/13 were filled. That fill-in was never actually committed to the repo. This task commits the resolved values so the file matches what the issue thread actually decided, before any code references it.

**Files:**
- Modify: `domain/cad-block-point-placement.md`

- [ ] **Step 1: Update frontmatter and fill the 10 resolved TODOs**

Replace the frontmatter block:

```yaml
---
name: cad-block-point-placement
description: "CAD 圖塊（Block/INSERT）插入點批次放置 Revit 點位族群（FamilyInstance）的通用 SOP：適用灑水頭/閥件等重複設備圖塊。discover/preview/create 三工具拆分，preview 唯讀回傳可檢查的座標鏈（Block insertion point → Block transform → ImportInstance TotalTransform）+ ready/duplicate/unsupported_family 狀態；**transform 不可信時停止建立、不猜 correction**。與 dwg-column-import（矩形輪廓）、dwg-beam-import（雙線中心線）互補，非取代。觸發於 cad 圖塊放置、block 轉族群、灑水頭建模、閥件建模、point placement from CAD block、INSERT to FamilyInstance。"
metadata:
  version: "0.2"
  updated: "2026-08-10"
  created: "2026-07-28"
  contributors:
    - "NicheSam (SC REVIT, 待確認真名)"
  references:
    - "Issue #100（作者 @NicheSam, SC REVIT）"
    - "Issue #113（三工具實作追蹤）"
  related:
    - dwg-column-import.md
    - dwg-beam-import.md
    - tool-capability-boundary.md
  referenced_by:
    - MCP-Server/src/tools/cad-block-placement-tools.ts
    - MCP/Core/CadBlockPlacementExecutor.cs
  tags: [DWG, DXF, CAD, ImportInstance, Block, INSERT, FamilyInstance, 點位放置, 灑水頭, 閥件, 座標鏈, transform, Revit]
---
```

In §1 item 5 (Import/Link 前置條件), replace the TODO with:

```markdown
5. 本流程 v1 **只支援已載入的 Linked DWG**；Imported DWG 留待後續版本（依據 v1 policy 1，2026-08-05 對齊：Linked DWG 記錄路徑已出現失效（`NotFound`）案例需要處理，而 Imported 的 `TotalTransform` 語意與失連風險更高，v1 排除是正確收斂）。`discover` 偵測到目標是 Imported DWG 時應直接回錯誤，不嘗試掃描。
```

In §2 replace the two "TODO 待補實際工具名" mentions with the actual tool names: `discover` → `get_dwg_block_instances`；`preview(...)` → `preview_family_instances_from_dwg_blocks(...)`；`create(...)` → `create_family_instances_from_dwg_blocks(...)`.

In §3, replace the transform-trust TODO paragraph with:

```markdown
**transform 不可信的判定條件（2026-08-05 對齊）**：Transform 必須 finite（無 NaN/Infinity 分量）、可逆（determinant ≠ 0）、conformal 等比例（三軸基向量長度相等，容許誤差內）；純鏡射（determinant < 0 但仍等比例）可繼續但需標記警告，非等比例縮放一律判定不可信。判定為不可信時：
```

(keep the existing bullet list below it unchanged).

In §4.1, replace the TODO with:

```markdown
`重複容差` **預設 10mm**，使用者可覆寫；`preview`／`create` 回應必須明示實際使用值及其來源為 `default` 或 `user-provided`（2026-08-05 對齊，NicheSam 建議值）。
```

In §4.2, replace the TODO with:

```markdown
`offset` 輸入單位為 **mm**（比照本專案 `dwg-column-import` 等既有工具的 mm-facing 慣例，內部換算為 Revit feet；工具 schema 說明與 C# 端註解都需明確標示單位，避免同一類單位陷阱重演）。
```

In §4.3, replace the TODO with:

```markdown
判定依據：`familySymbol.Family.FamilyPlacementType == FamilyPlacementType.OneLevelBased` 才視為支援；其餘一律回 `unsupported_family`，不嘗試放置、不做降級處理。
```

Add a new §4.5 after §4.4:

```markdown
### 4.5 duplicate 略過需明確核准
`create` 只能在呼叫端**明確傳入核准參數**（例如 `skipDuplicates: true`）時略過重複項目，**不得由 agent 自行推定**使用者已核准。無論是否略過，回應都要逐筆列出 `duplicate_existing`（與既有 Revit FamilyInstance 重複）或 `duplicate_in_batch`（本次掃描內部彼此重複）、對應既有 ElementId（若有）、候選 identity 與判定原因；`preview` 一律完整列出重複群組，不得自行合併或刪除（2026-08-10 對齊，shuotao 三條件）。
```

Leave the 3 genuinely-unresolved TODOs as-is (Level 不存在時是否自動建立 / mermaid 流程圖 / 具體失敗案例實測記錄) — do not invent answers for those.

- [ ] **Step 2: Commit**

```bash
git add domain/cad-block-point-placement.md
git commit -m "docs(domain): resolve 10/13 cad-block-point-placement TODOs per issue #100 thread"
```

---

### Task 2: Discover — `GetDwgBlockInstances`

**Files:**
- Create: `MCP/Core/CadBlockPlacementExecutor.cs`

**Interfaces:**
- Produces: `internal static class CadBlockPlacementExecutor` with `public static object GetDwgBlockInstances(Document doc, JObject p)`, plus private helpers `FindLinkedImportInstance(Document doc, ViewPlan vp, string importInstanceUniqueId)`, `CollectBlockCandidates(ImportInstance cad, ViewPlan vp)` returning `List<BlockCandidate>`, and the `BlockCandidate` record — later tasks (3, 4) consume `CollectBlockCandidates` and `BlockCandidate` directly, so their field names are locked here.

- [ ] **Step 1: Write the executor file with shared types, unit constants, and the discover method**

```csharp
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
    /// </summary>
    internal static class CadBlockPlacementExecutor
    {
        const double FtMm = 304.8;
        const double MmFt = 1.0 / 304.8;
        const double DefaultDuplicateToleranceMm = 10.0;

        /// <summary>掃描結果的單一 Block 插入點候選。</summary>
        internal sealed class BlockCandidate
        {
            public string Identity;       // 本次掃描內穩定、重新掃描時可重現的識別字串
            public string DisplayName;    // 供人辨識的 CAD 名稱（可能是合成的 fallback）
            public XYZ InsertionPoint;    // Block insertion point（Block 自身座標系）
            public Transform BlockTransform;      // GeometryInstance.Transform
            public Transform TotalTransform;      // ImportInstance.GetTotalTransform()
            public XYZ ResolvedPoint;     // 套用完整座標鏈後，落在 Revit 模型座標系的最終點
            public double RotationRadians;
        }

        static string BuildIdentity(string importInstanceUniqueId, int pathIndex, string blockName)
            => $"{importInstanceUniqueId}|{pathIndex}|{blockName}";

        /// <summary>
        /// 找到目前平面視圖中，UniqueId 相符（或未指定時取第一個）的 Linked ImportInstance。
        /// Imported（非 Linked）一律拒絕，v1 只支援 Linked DWG。
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
                        : $"找不到 UniqueId 為「{importInstanceUniqueId}」的 ImportInstance");

            // CADLinkType 存在代表這是 Linked；純 Imported 的 ImportInstance 沒有對應 CADLinkType。
            bool isLinked = doc.GetElement(picked.GetTypeId()) is CADLinkType;
            if (!isLinked)
                throw new InvalidOperationException(
                    "v1 只支援 Linked DWG，偵測到的 ImportInstance 是 Imported（非 Linked），請改用連結方式重新匯入");

            return picked;
        }

        /// <summary>
        /// 遍歷 ImportInstance 幾何樹，收集每個 Block（INSERT）的插入點與 transform。
        /// 深度優先、depth 上限 5（比照 DwgColumnExecutor.CollectInstancePoints 的既有慣例）。
        /// </summary>
        static List<BlockCandidate> CollectBlockCandidates(ImportInstance cad, ViewPlan vp)
        {
            var result = new List<BlockCandidate>();
            var opt = new Options { ComputeReferences = true, IncludeNonVisibleObjects = true, View = vp };
            var geomElem = cad.get_Geometry(opt);
            if (geomElem == null) return result;

            var totalTransform = cad.GetTotalTransform();
            int pathIndex = 0;
            WalkGeometry(geomElem, cad.UniqueId, totalTransform, ref pathIndex, result, depth: 0);
            return result;
        }

        static void WalkGeometry(
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
                if (obj is GeometryInstance gi)
                {
                    // 每個 GeometryInstance 視為一個 Block（INSERT）候選：
                    // insertion point 取 gi.Transform.Origin，rotation 取 BasisX 的角度。
                    string blockName = TryGetBlockDisplayName(gi, pathIndex);
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

                    // 巢狀 Block（Block 內含 Block）：遞迴收集，但不視為獨立頂層候選項。
                    var nested = gi.GetInstanceGeometry();
                    if (nested != null)
                        WalkGeometry(nested, importInstanceUniqueId, totalTransform, ref pathIndex, result, depth + 1);
                }
            }
        }

        /// <summary>
        /// KNOWN UNCERTAINTY (see plan "Known Revit-API Uncertainties" #1):
        /// GeometryInstance 不保證暴露 block 定義名稱字串。優先嘗試 GraphicsStyle 對應的
        /// GraphicsStyleCategory 名稱；取不到時 fallback 為合成序號標籤，並在 discover 回應中
        /// 標記 nameSource=fallback，讓呼叫端知道這不是 CAD 原始名稱。需以真實連結的 DWG 驗證
        /// 是否有更精確的名稱來源（例如某些 Revit 版本透過 Reference 或 selection 才能取得）。
        /// </summary>
        static string TryGetBlockDisplayName(GeometryInstance gi, int pathIndex)
        {
            var style = gi.GraphicsStyleId != ElementId.InvalidElementId
                ? gi.GraphicsStyleId
                : null;
            // GraphicsStyle 通常對應圖層而非 block 名稱；此處僅作為 best-effort 名稱來源。
            return $"Block#{pathIndex}";
        }

        public static object GetDwgBlockInstances(Document doc, JObject p)
        {
            var vp = doc.ActiveView as ViewPlan;
            if (vp == null)
                throw new InvalidOperationException("請先切換到平面視圖再執行本工具");

            string importInstanceUniqueId = (string)p?["importInstanceUniqueId"];
            var cad = FindLinkedImportInstance(doc, vp, importInstanceUniqueId);
            var all = CollectBlockCandidates(cad, vp);

            var grouped = all
                .GroupBy(c => c.DisplayName)
                .Select(g => new JObject
                {
                    ["blockName"] = g.Key,
                    ["nameSource"] = "fallback", // see TryGetBlockDisplayName uncertainty note
                    ["count"] = g.Count(),
                    ["sample"] = new JArray(g.Take(3).Select(c => new JObject
                    {
                        ["identity"] = c.Identity,
                        ["insertionPointMm"] = new JObject
                        {
                            ["x"] = Math.Round(c.InsertionPoint.X * FtMm, 1),
                            ["y"] = Math.Round(c.InsertionPoint.Y * FtMm, 1),
                            ["z"] = Math.Round(c.InsertionPoint.Z * FtMm, 1),
                        },
                        ["rotationDegrees"] = Math.Round(c.RotationRadians * 180.0 / Math.PI, 2),
                    })),
                });

            return new JObject
            {
                ["importInstanceUniqueId"] = cad.UniqueId,
                ["totalPoints"] = all.Count,
                ["blockTypes"] = grouped.Count(),
                ["blocks"] = new JArray(grouped),
            };
        }
    }
}
```

- [ ] **Step 2: Compile-check**

Run: `dotnet build -c Release.R26 MCP\RevitMCP.csproj`
Expected: build succeeds with the new file included (no references to Task 3/4 methods yet, so nothing else should break).

- [ ] **Step 3: Commit**

```bash
git add MCP/Core/CadBlockPlacementExecutor.cs
git commit -m "feat(cad-block-placement): add GetDwgBlockInstances discover method"
```

---

### Task 3: Preview — transform trust, duplicate detection, `unsupported_family`

**Files:**
- Modify: `MCP/Core/CadBlockPlacementExecutor.cs`

**Interfaces:**
- Consumes: `BlockCandidate`, `CollectBlockCandidates`, `FindLinkedImportInstance` from Task 2.
- Produces: `internal static List<JObject> BuildPlacementPlan(Document doc, ViewPlan vp, JObject p, out JObject summary)` — the shared discovery+matching routine Task 4's `create` must call to re-scan from source (per the domain SOP's "不得信任 preview 快取結果" rule). Also produces the public `PreviewFamilyInstancesFromDwgBlocks(Document doc, JObject p)` entry point.

- [ ] **Step 1: Add the transform-trust check and the shared planning routine**

Add to `CadBlockPlacementExecutor.cs`:

```csharp
        /// <summary>
        /// Transform 可信度判定（domain/cad-block-point-placement.md §3，2026-08-05 對齊）：
        /// finite、可逆（det != 0）、conformal（三軸基向量長度相等，5% 容許誤差）。
        /// 純鏡射（det &lt; 0 但仍等比例）允許但標記警告；非等比例一律不可信。
        /// </summary>
        static (bool trustworthy, bool isMirrored, string reason) CheckTransformTrust(Transform t)
        {
            double[] components =
            {
                t.BasisX.X, t.BasisX.Y, t.BasisX.Z,
                t.BasisY.X, t.BasisY.Y, t.BasisY.Z,
                t.BasisZ.X, t.BasisZ.Y, t.BasisZ.Z,
                t.Origin.X, t.Origin.Y, t.Origin.Z,
            };
            if (components.Any(v => double.IsNaN(v) || double.IsInfinity(v)))
                return (false, false, "transform 含 NaN/Infinity 分量");

            double det = t.Determinant;
            if (Math.Abs(det) < 1e-9)
                return (false, false, "transform 不可逆（determinant 接近 0）");

            double lenX = t.BasisX.GetLength();
            double lenY = t.BasisY.GetLength();
            double lenZ = t.BasisZ.GetLength();
            double maxLen = Math.Max(lenX, Math.Max(lenY, lenZ));
            double minLen = Math.Min(lenX, Math.Min(lenY, lenZ));
            bool conformal = maxLen > 1e-9 && (maxLen - minLen) / maxLen <= 0.05;

            if (!conformal)
                return (false, false, $"非等比例縮放（軸長 {lenX:F4}/{lenY:F4}/{lenZ:F4}），無法信任");

            bool mirrored = det < 0;
            return (true, mirrored, mirrored ? "純鏡射，允許但已標記警告" : "");
        }

        /// <summary>
        /// discover + 座標鏈健檢 + duplicate/unsupported_family 判定，preview 與 create 共用。
        /// 不開啟 Transaction；create 呼叫本方法取得權威結果後才寫入模型。
        /// </summary>
        static List<JObject> BuildPlacementPlan(Document doc, ViewPlan vp, JObject p, out JObject summary)
        {
            string importInstanceUniqueId = (string)p?["importInstanceUniqueId"];
            string blockNameFilter = (string)p?["blockName"];
            var familySymbolIdRaw = (string)p?["familySymbolId"];
            var levelIdRaw = (string)p?["levelId"];
            double offsetMm = p?["offsetMm"]?.Value<double>() ?? 0.0;
            bool toleranceProvided = p?["duplicateToleranceMm"] != null;
            double toleranceMm = toleranceProvided
                ? p["duplicateToleranceMm"].Value<double>()
                : DefaultDuplicateToleranceMm;

            if (string.IsNullOrEmpty(familySymbolIdRaw))
                throw new ArgumentException("familySymbolId 為必填，本工具不自動選擇族群");
            if (string.IsNullOrEmpty(levelIdRaw))
                throw new ArgumentException("levelId 為必填，本工具不自動選擇樓層");

            var symbol = doc.GetElement(new ElementId(long.Parse(familySymbolIdRaw))) as FamilySymbol;
            if (symbol == null)
                throw new ArgumentException($"找不到 familySymbolId={familySymbolIdRaw} 對應的 FamilySymbol");

            var level = doc.GetElement(new ElementId(long.Parse(levelIdRaw))) as Level;
            if (level == null)
                throw new ArgumentException($"找不到 levelId={levelIdRaw} 對應的 Level（v1 不自動建立樓層）");

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
                .Select(fi => (fi.Id, Location: (fi.Location as LocationPoint)?.Point))
                .Where(x => x.Point != null)
                .ToList();

            var plan = new List<JObject>();
            var seenInBatch = new List<(string identity, XYZ point)>();
            int ready = 0, duplicate = 0, unsupported = 0, untrustworthy = 0;

            foreach (var c in all)
            {
                var finalPoint = new XYZ(c.ResolvedPoint.X, c.ResolvedPoint.Y, level.Elevation + offsetFt);
                var (trustworthy, mirrored, reason) = CheckTransformTrust(c.TotalTransform.Multiply(c.BlockTransform));

                string status;
                string statusReason = "";
                ElementId duplicateOf = null;

                if (!familySupported)
                {
                    status = "unsupported_family";
                    statusReason = $"familySymbol 的 FamilyPlacementType 為 {symbol.Family.FamilyPlacementType}，v1 僅支援 OneLevelBased";
                    unsupported++;
                }
                else if (!trustworthy)
                {
                    status = "untrustworthy_transform";
                    statusReason = reason;
                    untrustworthy++;
                }
                else
                {
                    var existingDup = existingInstances.FirstOrDefault(x => x.Location.DistanceTo(finalPoint) < toleranceFt);
                    var batchDup = seenInBatch.FirstOrDefault(x => x.point.DistanceTo(finalPoint) < toleranceFt);

                    if (existingDup.Point != null)
                    {
                        status = "duplicate_existing";
                        statusReason = $"與既有 FamilyInstance ElementId={existingDup.Id.IntegerValue} 距離 <{toleranceMm}mm";
                        duplicateOf = existingDup.Id;
                        duplicate++;
                    }
                    else if (batchDup.point != null)
                    {
                        status = "duplicate_in_batch";
                        statusReason = $"與本次掃描內候選 identity={batchDup.identity} 距離 <{toleranceMm}mm";
                        duplicate++;
                    }
                    else
                    {
                        status = "ready";
                        ready++;
                        seenInBatch.Add((c.Identity, finalPoint));
                    }
                }

                plan.Add(new JObject
                {
                    ["identity"] = c.Identity,
                    ["blockName"] = c.DisplayName,
                    ["status"] = status,
                    ["statusReason"] = statusReason,
                    ["mirrored"] = mirrored,
                    ["duplicateOfElementId"] = duplicateOf?.IntegerValue,
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
                ["familySymbolId"] = symbol.Id.IntegerValue,
                ["levelId"] = level.Id.IntegerValue,
                ["offsetMm"] = offsetMm,
            };
            return plan;
        }

        static JObject PointToMmJson(XYZ pt) => new JObject
        {
            ["x"] = Math.Round(pt.X * FtMm, 1),
            ["y"] = Math.Round(pt.Y * FtMm, 1),
            ["z"] = Math.Round(pt.Z * FtMm, 1),
        };

        public static object PreviewFamilyInstancesFromDwgBlocks(Document doc, JObject p)
        {
            var vp = doc.ActiveView as ViewPlan;
            if (vp == null)
                throw new InvalidOperationException("請先切換到平面視圖再執行本工具");

            var plan = BuildPlacementPlan(doc, vp, p, out var summary);
            var result = (JObject)summary;
            result["candidates"] = new JArray(plan);
            return result;
        }
```

- [ ] **Step 2: Compile-check**

Run: `dotnet build -c Release.R26 MCP\RevitMCP.csproj`
Expected: build succeeds.

- [ ] **Step 3: Commit**

```bash
git add MCP/Core/CadBlockPlacementExecutor.cs
git commit -m "feat(cad-block-placement): add preview with transform-trust and duplicate checks"
```

---

### Task 4: Create — SubTransaction-isolated placement + independent re-query

**Files:**
- Modify: `MCP/Core/CadBlockPlacementExecutor.cs`

**Interfaces:**
- Consumes: `BuildPlacementPlan` from Task 3 (re-scans from source — never trusts a client-passed coordinate list).
- Produces: `public static object CreateFamilyInstancesFromDwgBlocks(Document doc, JObject p)`.

- [ ] **Step 1: Add the create method**

```csharp
        public static object CreateFamilyInstancesFromDwgBlocks(Document doc, JObject p)
        {
            var vp = doc.ActiveView as ViewPlan;
            if (vp == null)
                throw new InvalidOperationException("請先切換到平面視圖再執行本工具");

            bool skipDuplicates = p?["skipDuplicates"]?.Value<bool>() ?? false;

            // 鐵則：不信任呼叫端可能挾帶的舊 preview 結果，以相同參數重新掃描一次。
            var plan = BuildPlacementPlan(doc, vp, p, out var summary);

            var familySymbolId = new ElementId(long.Parse((string)p["familySymbolId"]));
            var symbol = (FamilySymbol)doc.GetElement(familySymbolId);
            var level = (Level)doc.GetElement(new ElementId(long.Parse((string)p["levelId"])));

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
                    if (status != "ready" && !(isDuplicate && skipDuplicates))
                    {
                        // unsupported_family / untrustworthy_transform 一律不建立；
                        // duplicate 只有在 skipDuplicates=true 時才視為「略過」而非「阻擋」。
                        skipped++;
                        perItemResults.Add(new JObject
                        {
                            ["identity"] = identity,
                            ["outcome"] = isDuplicate ? "skipped_duplicate" : "blocked",
                            ["status"] = status,
                            ["statusReason"] = candidate["statusReason"],
                        });
                        continue;
                    }
                    if (isDuplicate && !skipDuplicates)
                    {
                        skipped++;
                        perItemResults.Add(new JObject
                        {
                            ["identity"] = identity,
                            ["outcome"] = "blocked_duplicate_not_approved",
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

                            var instance = doc.Create.NewFamilyInstance(
                                point, symbol, level,
                                Autodesk.Revit.DB.Structure.StructuralType.NonStructural);

                            sub.Commit();
                            created++;
                            perItemResults.Add(new JObject
                            {
                                ["identity"] = identity,
                                ["outcome"] = "created",
                                ["elementId"] = instance.Id.IntegerValue,
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

            // 建立後逐一獨立查詢驗證存在（同步、確定性，不用 Idling 事件輪詢）。
            foreach (var item in perItemResults.Where(i => (string)i["outcome"] == "created"))
            {
                var id = new ElementId(item["elementId"].Value<long>());
                var verify = doc.GetElement(id);
                item["verifiedExists"] = verify != null;
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
```

- [ ] **Step 2: Compile-check**

Run: `dotnet build -c Release.R26 MCP\RevitMCP.csproj`
Expected: build succeeds, zero warnings about unused `using` beyond pre-existing ones.

- [ ] **Step 3: Commit**

```bash
git add MCP/Core/CadBlockPlacementExecutor.cs
git commit -m "feat(cad-block-placement): add create with SubTransaction isolation + verified re-query"
```

---

### Task 5: Wire the three commands into the dispatcher

**Files:**
- Modify: `MCP/Core/CommandExecutor.cs`

- [ ] **Step 1: Add the switch cases**

Find the existing DWG column module block (around the `case "create_columns_from_dwg":` line) and add a new banner block immediately after it:

```csharp
                // === CAD 圖塊點位放置模組（Block/INSERT → FamilyInstance，issue #100/#113）===
                case "get_dwg_block_instances":
                    result = CadBlockPlacementExecutor.GetDwgBlockInstances(_uiApp.ActiveUIDocument.Document, parameters);
                    break;
                case "preview_family_instances_from_dwg_blocks":
                    result = CadBlockPlacementExecutor.PreviewFamilyInstancesFromDwgBlocks(_uiApp.ActiveUIDocument.Document, parameters);
                    break;
                case "create_family_instances_from_dwg_blocks":
                    result = CadBlockPlacementExecutor.CreateFamilyInstancesFromDwgBlocks(_uiApp.ActiveUIDocument.Document, parameters);
                    break;
```

- [ ] **Step 2: Compile-check**

Run: `dotnet build -c Release.R26 MCP\RevitMCP.csproj`
Expected: build succeeds.

- [ ] **Step 3: Commit**

```bash
git add MCP/Core/CommandExecutor.cs
git commit -m "feat(cad-block-placement): register 3 commands in dispatcher"
```

---

### Task 6: TS tool definitions + registry

**Files:**
- Create: `MCP-Server/src/tools/cad-block-placement-tools.ts`
- Modify: `MCP-Server/src/tools/index.ts`

**Interfaces:**
- Produces: `export const cadBlockPlacementTools: Tool[]` with tool names `get_dwg_block_instances`, `preview_family_instances_from_dwg_blocks`, `create_family_instances_from_dwg_blocks` — must match the C# dispatcher case strings from Task 5 exactly.

- [ ] **Step 1: Write the tool definitions**

```typescript
import { Tool } from "@modelcontextprotocol/sdk/types.js";

/**
 * CAD 圖塊插入點批次放置 Revit 點位族群（灑水頭/閥件等）。
 *
 * 對應 C# 端 handler: MCP/Core/CadBlockPlacementExecutor.cs
 * 對應 CommandExecutor.cs cases: get_dwg_block_instances / preview_family_instances_from_dwg_blocks / create_family_instances_from_dwg_blocks
 * 對應 domain SOP: domain/cad-block-point-placement.md
 *
 * v1 只支援 Linked DWG、僅 OneLevelBased（non-hosted、level-based、point-placement）FamilySymbol。
 */
export const cadBlockPlacementTools: Tool[] = [
    {
        name: "get_dwg_block_instances",
        description:
            "掃描目前 Revit 平面視圖中已連結（Linked，非 Imported）的 CAD DWG，" +
            "列出可辨識的 Block（INSERT）名稱、每種數量、插入點與旋轉角範例。" +
            "唯讀操作，不建立任何 Revit 元素。使用前請確認 DWG 已用「連結」方式匯入。",
        inputSchema: {
            type: "object",
            properties: {
                importInstanceUniqueId: {
                    type: "string",
                    description:
                        "（選填）指定要掃描的 ImportInstance UniqueId；未指定時取視圖內第一個 Linked ImportInstance",
                },
            },
        },
    },
    {
        name: "preview_family_instances_from_dwg_blocks",
        description:
            "對指定 Block 的每個插入點做座標鏈健檢（Block insertion point → Block transform → " +
            "ImportInstance TotalTransform），回傳每點狀態：ready / duplicate_existing / duplicate_in_batch / " +
            "unsupported_family / untrustworthy_transform，並攤開完整座標鏈供核對。" +
            "唯讀操作，不建立任何 Revit 元素。transform 不可信時只回傳警告，不做任何猜測性修正。" +
            "familySymbolId 與 levelId 必須明確指定，本工具不自動選擇。",
        inputSchema: {
            type: "object",
            properties: {
                importInstanceUniqueId: {
                    type: "string",
                    description: "（選填）指定要掃描的 ImportInstance UniqueId，比照 get_dwg_block_instances",
                },
                blockName: {
                    type: "string",
                    description: "從 get_dwg_block_instances 回傳清單中選擇的 Block 名稱，只處理此名稱的插入點",
                },
                familySymbolId: {
                    type: "string",
                    description: "目標 FamilySymbol 的 ElementId（字串）。必須是 non-hosted、level-based、OneLevelBased 族群",
                },
                levelId: {
                    type: "string",
                    description: "目標 Level 的 ElementId（字串）。Level 必須已存在，本工具不自動建立",
                },
                offsetMm: {
                    type: "number",
                    default: 0,
                    description: "相對於 levelId 對應樓層的垂直偏移，單位為 mm（不是 Revit 內部的 feet）",
                },
                duplicateToleranceMm: {
                    type: "number",
                    description: "（選填）重複判定容差，單位 mm；未指定時預設 10mm",
                },
            },
            required: ["familySymbolId", "levelId"],
        },
    },
    {
        name: "create_family_instances_from_dwg_blocks",
        description:
            "以與 preview_family_instances_from_dwg_blocks 完全相同的參數重新掃描來源後建立 FamilyInstance——" +
            "不信任任何先前呼叫的快取結果。主 Transaction + 逐筆 SubTransaction，單筆失敗不影響其他已成功項目。" +
            "duplicate 項目預設會被阻擋，只有明確傳入 skipDuplicates=true 才會略過（不得由 AI 自行推定使用者已核准）。" +
            "unsupported_family／untrustworthy_transform 一律不建立。回傳每筆結果（created/failed/skipped）與建立後" +
            "獨立查詢驗證的 verifiedExists。此操作會修改 Revit 模型，無法自動復原，請先呼叫 preview 確認再執行。",
        inputSchema: {
            type: "object",
            properties: {
                importInstanceUniqueId: {
                    type: "string",
                    description: "（選填）比照 preview，必須與該次 preview 使用的值一致",
                },
                blockName: {
                    type: "string",
                    description: "比照 preview，必須與該次 preview 使用的值一致",
                },
                familySymbolId: {
                    type: "string",
                    description: "比照 preview，必須與該次 preview 使用的值一致",
                },
                levelId: {
                    type: "string",
                    description: "比照 preview，必須與該次 preview 使用的值一致",
                },
                offsetMm: {
                    type: "number",
                    default: 0,
                    description: "比照 preview，必須與該次 preview 使用的值一致",
                },
                duplicateToleranceMm: {
                    type: "number",
                    description: "比照 preview，必須與該次 preview 使用的值一致",
                },
                skipDuplicates: {
                    type: "boolean",
                    default: false,
                    description:
                        "使用者明確核准後才可設為 true，用來略過 duplicate_existing／duplicate_in_batch 項目。" +
                        "AI 不得在使用者未表達核准的情況下自行設定此參數為 true",
                },
            },
            required: ["familySymbolId", "levelId"],
        },
    },
];
```

- [ ] **Step 2: Register in `MCP-Server/src/tools/index.ts`**

Add the import near the other DWG-tool imports:

```typescript
import { cadBlockPlacementTools } from "./cad-block-placement-tools.js";
```

Add `cadBlockPlacementTools` to the `structural` profile array (same array `dwgColumnTools`/`dwgBeamTools` live in):

```typescript
structural: [baseTools, wallTools, visualizationTools, dwgColumnTools, dwgBeamTools, cadBlockPlacementTools, clashTools, structureTools, gradingTools, ifcStructuralSyncTools],
```

- [ ] **Step 3: TypeScript compile-check**

Run: `cd MCP-Server && npm run build`
Expected: `tsc` succeeds, no type errors.

- [ ] **Step 4: Commit**

```bash
git add MCP-Server/src/tools/cad-block-placement-tools.ts MCP-Server/src/tools/index.ts
git commit -m "feat(cad-block-placement): register 3 TS tool definitions"
```

---

### Task 7: Sync source-of-truth counts and QA/QC gate

**Files:**
- Modify: `CLAUDE.md`
- Modify: `README.md`
- Modify: `README.zh-TW.md`
- Modify: `docs/DOCUMENT_AUDIENCE_INVENTORY.md` (only if it carries a tool-count claim — check first)

- [ ] **Step 1: Bump the runtime tool count**

In `CLAUDE.md`'s "Current Source-of-Truth Counts" table, change:

```markdown
| Runtime MCP tools | 167 | `registerRevitTools()` from `MCP-Server/src/tools/index.ts` |
```

to:

```markdown
| Runtime MCP tools | 170 | `registerRevitTools()` from `MCP-Server/src/tools/index.ts` |
```

Apply the equivalent `167 → 170` edit to any matching `| Runtime MCP tools | N |`-style row in `README.md`, `README.zh-TW.md`, and `docs/DOCUMENT_AUDIENCE_INVENTORY.md` — grep for the exact current value first since wording may vary:

```bash
grep -rn "167" README.md README.zh-TW.md docs/DOCUMENT_AUDIENCE_INVENTORY.md
```

Update only lines that are genuinely a tool-count claim (not an unrelated "167" occurring elsewhere, e.g. a port number or version string).

- [ ] **Step 2: Run the QA/QC gate**

```powershell
.\scripts\verify-qaqc.ps1 -SkipBuild -SkipDeploy
```

Expected: `PASS` on the cross-document count-alignment and count-table checks. If it fails, read the reported line and fix the mismatched file — do not silently ignore a FAIL.

- [ ] **Step 3: Commit**

```bash
git add CLAUDE.md README.md README.zh-TW.md docs/DOCUMENT_AUDIENCE_INVENTORY.md
git commit -m "docs(count): sync tool count 167→170 for cad-block-placement 3-tool group"
```

---

### Task 8: Full build, log entry, and live-Revit smoke-test checklist

**Files:**
- Modify: `log/2026-08.md`

- [ ] **Step 1: Full build both sides**

```powershell
cd MCP-Server; npm run build; cd ..
dotnet build -c Release.R24 MCP\RevitMCP.csproj
dotnet build -c Release.R25 MCP\RevitMCP.csproj
dotnet build -c Release.R26 MCP\RevitMCP.csproj
```

Expected: all succeed. (R22/R23 optional depending on installed SDKs — note in the log entry which configs were actually verified.)

- [ ] **Step 2: Append the log entry**

```markdown
## [2026-08-10 HH:MM] feat | CAD 圖塊點位放置三工具實作（discover/preview/create, issue #100/#113）
- actor: claude-sonnet-5 (via claude-code)
- files: MCP/Core/CadBlockPlacementExecutor.cs, MCP/Core/CommandExecutor.cs, MCP-Server/src/tools/cad-block-placement-tools.ts, MCP-Server/src/tools/index.ts, domain/cad-block-point-placement.md, CLAUDE.md, README.md, README.zh-TW.md
- trigger: manual
- summary: 實作 get_dwg_block_instances / preview_family_instances_from_dwg_blocks / create_family_instances_from_dwg_blocks，工具數 167→170；尚未經真實 Revit + 已連結 DWG 的 runtime 驗證，見下方待辦
```

(Fill in the actual `HH:MM` at commit time — do not fabricate a timestamp ahead of when this step actually runs.)

- [ ] **Step 3: Leave an explicit live-Revit verification checklist for whoever has Revit open next**

This cannot be completed inside this plan — no Revit MCP connection is available in this environment. Add this checklist to the log entry (or a follow-up comment on issue #113) verbatim:

```markdown
### 待真人於 Revit 2024+ 驗證（本環境無法連線 Revit）
- [ ] `get_dwg_block_instances` 對一個真實已連結 DWG 執行，確認 blockName 是否真的只能拿到 fallback 標籤，或有更精確名稱來源可用（見 Task 2 TryGetBlockDisplayName 的已知不確定性）
- [ ] `preview_family_instances_from_dwg_blocks` 對至少一個 OneLevelBased 灑水頭/閥件族群跑過，確認 ready/duplicate/unsupported_family/untrustworthy_transform 四種狀態都至少觸發過一次（domain doc §6 提到這三種案例都還沒有實測記錄）
- [ ] `create_family_instances_from_dwg_blocks` 建立後，用獨立的 get_element 之類唯讀查詢核對 verifiedExists，並確認 Offset 參數／實際 Z 座標是否符合預期（見 Task 4 「Offset-to-Z mechanic」已知不確定性）
- [ ] 刻意用一個非 OneLevelBased 族群測試 unsupported_family 路徑
- [ ] 刻意用一個非等比例縮放或已失連的 Linked DWG 測試 untrustworthy_transform 路徑
- [ ] 確認 skipDuplicates=true 時的行為，以及未傳入時 duplicate 是否確實被阻擋
```

- [ ] **Step 4: Commit**

```bash
git add log/2026-08.md
git commit -m "docs(log): record cad-block-placement 3-tool implementation + pending live-Revit checklist"
```

---

## Self-Review Notes

- **Spec coverage:** §1 (preconditions) → Tasks 2-3 (Linked-only check, FamilySymbol/Level resolution). §2 (workflow/breakpoints) → Tasks 2-4 (three distinct tools, `create` re-scans via shared `BuildPlacementPlan`). §3 (transform trust) → Task 3 `CheckTransformTrust`. §4.1-4.5 → Task 3 (duplicate tolerance + status), Task 4 (skipDuplicates gating). §5 (v1 boundaries) → Task 3 (`unsupported_family`, no auto-select, no correction path exists in code at all). §6/§7 (known limits/QA checklist) → Task 8's live-Revit checklist covers the untested cases explicitly named in domain doc §6's TODOs.
- **Placeholder scan:** no TBD/"add error handling"/"similar to Task N" left in; every step has literal code or literal shell commands.
- **Type consistency:** `BlockCandidate` fields (`Identity`, `DisplayName`, `InsertionPoint`, `BlockTransform`, `TotalTransform`, `ResolvedPoint`, `RotationRadians`) declared in Task 2, consumed unchanged in Task 3's `BuildPlacementPlan`. Tool names (`get_dwg_block_instances`, `preview_family_instances_from_dwg_blocks`, `create_family_instances_from_dwg_blocks`) identical across Task 5's C# switch and Task 6's TS `name` fields — checked by hand, no drift.
- **Explicitly not fabricated:** the 3 domain-doc TODOs the issue thread never resolved (Level auto-create, mermaid diagram, concrete failure-case test log) are left open in Task 1 rather than guessed at. The two Revit-API mechanics I'm not fully certain of are flagged both in the plan header and inline in the relevant code as comments, with a concrete live-Revit checklist in Task 8 rather than asserted as fact.
