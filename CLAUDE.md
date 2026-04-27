# CLAUDE.md

本檔案為本專案的統一行為指引，供所有 AI Client 共用。GEMINI.md 內容為 `CLAUDE.md`（單行指向），Gemini CLI 會直接讀取本檔案。

## Project Overview

Revit MCP is a bridge between AI language models and Autodesk Revit via the Model Context Protocol (MCP). It enables AI-driven BIM workflows through natural language commands. The project has two main components that communicate over WebSocket on `localhost:8964`.

## Architecture (4+1 Pattern)

```
AI Client (Claude Desktop / Gemini CLI / VS Code Copilot / Antigravity)
  ↓ stdio
MCP Server (Node.js/TypeScript) — MCP-Server/src/index.ts
  ↓ WebSocket (ws://localhost:8964)
Revit Add-in (C# .NET 4.8) — MCP/Application.cs
  ↓ ExternalEventManager (UI thread)
CommandExecutor → Revit API
```

A 5th "embedded" option bypasses the MCP Server entirely — a WPF chat window inside the Revit Add-in calls the Gemini API directly.

## Multi-Client Unified Architecture

本專案支援多個 AI Client，採用「不同入口、同一目的地」的統一指向規範：

| 項目 | Claude Code | Gemini CLI | VS Code Copilot |
|------|-------------|------------|-----------------|
| 行為指引 | CLAUDE.md | GEMINI.md → CLAUDE.md | .github/copilot-instructions.md |
| Skills | `.claude/skills/SKILL.md` | `.gemini/skills/SKILL.md`（[官方文件](https://geminicli.com/docs/cli/skills/)） | instructions 引導 |
| Domain 文件 | 共用 `domain/` | 共用 `domain/` | 共用 `domain/` |
| MCP Tools | 共用 82 個工具 | 共用 82 個工具 | 共用 82 個工具 |
| Event Log | 共用 `log/` | 共用 `log/` | 共用 `log/` |

SKILL.md 格式遵循 [Agent Skills 開放標準](https://agentskills.io)（YAML frontmatter + Markdown body），Claude Code 與 Gemini CLI 皆原生支援。

## Session Start Protocol（跨 AI 通用）

AI Agent 啟動時，**應**讀取 `log/` 目錄下最新月份檔的末尾 ~60 行（約 20 筆事件），了解專案近期動態。這讓 AI 在失憶狀態下能快速延續工作。

```bash
ls log/*.md | grep -v README | sort | tail -1 | xargs tail -60
```

**特別注意最新的 `session-summary` 條目**——那是上次重要會話的精華濃縮（包含五段落：完成／對齊的概念／Pending／拒絕的選項／起手式）。讀到 session-summary 即可直接延續，不需要使用者重述脈絡。

首次接觸專案（`log/` 為空或不存在）可略過此步驟。

## Logging Protocol（跨 AI 通用）

所有改動專案的 AI（Claude / Gemini / Copilot / 未來模型）都必須遵守以下 log 維護規則。

### 三層機制

| 層級 | 觸發方式 | AI 是否需要主動配合 |
|------|---------|---------------------|
| Layer 1（`scripts/git-hooks/post-commit`）| 每次 commit 自動 fire | ❌ 不用，git 自己處理 |
| Layer 2（本憲章規則）| AI 執行重要命令後主動 append | ✅ 必須遵守 |
| Layer 3（`.claude/hooks/` 擴充）| Claude Code harness 事件 | 選配 |

### Layer 2：AI 何時要主動 append log

| 事件 | 動作 |
|------|------|
| 執行 `/lessons` 完成後 | append `lessons` 事件到 `log/YYYY-MM.md` |
| 執行 `/domain` 完成後 | append `domain` 事件 |
| 執行 `/qaqc` 完成後 | append `qaqc` 事件（含 PASS/FAIL 數字） |
| 執行 `/review` 完成後 | append `review` 事件 |
| 編輯 `.claude/skills/` 或 tool 相關檔（非 commit 驅動）| append 對應事件 |
| 編輯 CLAUDE.md 之 AI Guard Rails / 憲法規則 | append `domain` 事件，摘要新規則名稱與起因 |
| 日常對話、臨時決策 | ❌ **不要**記錄（避免 log 膨脹） |

### 格式

```markdown
## [YYYY-MM-DD HH:MM] {event-type} | {short-description}
- actor: {model-id} (via {client-name})
- files: {comma-separated list}
- trigger: {git-hook | claude-hook | manual}
- summary: {one-liner}
```

### 隱私規則

- ✅ 可記：檔名、時間、事件類型、一行摘要、commit sha
- ❌ 不可記：使用者原始訊息、AI 完整輸出、程式碼片段、API Keys、認證資訊

首次部署：執行 `./scripts/install-log-hooks.sh`（Mac/Linux）或 `.\scripts\install-log-hooks.ps1`（Windows）設定 git hook 路徑。詳見 `log/README.md`。

## Build Commands

### C# Revit Add-in (Unified Build via Nice3point.Revit.Sdk)

The project uses `Nice3point.Revit.Sdk/6.1.0` for unified multi-version builds.
A single `RevitMCP.csproj` supports Revit 2022–2026 via configuration suffixes.

```powershell
cd MCP
dotnet build -c Release.R{YY} RevitMCP.csproj   # YY = 22/23/24/25/26
```

> **Note:** Only `RevitMCP.csproj` exists. Legacy version-specific files (`RevitMCP.2024.csproj`, `RevitMCP.2024.addin`) have been removed. See Deployment Rules below.

After building, close Revit, then deploy DLL:
```powershell
Copy-Item "bin/Release.R{YY}/RevitMCP.dll" "$env:APPDATA\Autodesk\Revit\Addins\{version}\RevitMCP\" -Force
```
Or use `scripts/install-addon.ps1` for automated install.
Or use Skills: `/build-revit` and `/deploy-addon`

### MCP Server (Node.js)
```bash
cd MCP-Server
npm install
npm run build    # tsc && node build/index.js
npm run watch    # tsc --watch (development)
```

## Key Source Files

| File | Role |
|------|------|
| `MCP/Application.cs` | Revit IExternalApplication entry point, creates ribbon panel |
| `MCP/Core/CommandExecutor.cs` | Central command dispatcher (82+ commands), largest file |
| `MCP/Core/SocketService.cs` | HttpListener-based WebSocket server in Revit |
| `MCP/Core/RevitCompatibility.cs` | Cross-version compatibility layer (ElementId int→long for 2025+) |
| `MCP/Core/ExternalEventManager.cs` | Ensures commands execute on Revit UI thread |
| `MCP-Server/src/index.ts` | MCP Server entry (StdioServerTransport) |
| `MCP-Server/src/socket.ts` | RevitSocketClient — WebSocket client to Revit |
| `MCP-Server/src/tools/` | Tool definitions (82 tools, 分 14 個模組) |
| `scripts/setup.ps1` | One-click setup for new users (prereqs, build, deploy, AI config) |

## Code Conventions

- **C# namespace**: `RevitMCP` — all classes use this namespace
- **Revit API safety**: All Revit operations MUST use `Transaction` and be reversible. Commands run through `ExternalEventManager` to ensure UI thread execution.
- **Command pattern**: Commands in `CommandExecutor.cs` follow a `case "command_name":` switch pattern, each returning data objects wrapped in `RevitCommandResponse`.
- **Singletons**: `ConfigManager`, `ExternalEventManager`, `Logger` are all singletons
- **Config storage**: `%AppData%\RevitMCP\config.json` (default port 8964)
- **Logs**: `%AppData%\RevitMCP\Logs\RevitMCP_YYYYMMDD.log`

## AI Guard Rails — MCP 工具鏈使用規則

> **本專案已有完整的 Revit 通訊工具鏈，任何 AI 模型都 MUST 遵守以下規則。**

### 禁止事項
- **DO NOT** 自行撰寫 WebSocket 腳本連接 `ws://localhost:8964`
- **DO NOT** 自行組裝 JSON 封包（`CommandName`/`Parameters`/`RequestId`）
- **DO NOT** 繞過 MCP Server 直接與 Revit Add-in 通訊
- **DO NOT** 用 `node -e` 或臨時腳本複製 MCP Server 已有的功能

### 正確做法
1. **查詢/操作 Revit** → 使用 MCP Server 已註冊的 tools（定義在 `MCP-Server/src/tools/*.ts`）
2. **執行 BIM 合規流程** → 使用 Skills（`.claude/skills/*/SKILL.md`），它們會編排正確的 tool 呼叫順序
3. **查閱法規知識** → 讀取 Domain 文件（`domain/*.md`）

### 為什麼
MCP Server 已封裝 82 個 tools，處理了格式轉換、錯誤處理、重連機制。自寫腳本會：
- 繞過既有的錯誤處理與格式驗證
- 產生 process 掛起（如自動重連導致無法退出）
- 與 Revit API 的 PascalCase 欄位不一致而靜默失敗
- 重造輪子，浪費使用者時間

### Tool Call Data Honesty (MUST — Supersedes All Skills & Subagents)

**Scope**: Applies to every tool use — any MCP server (Revit, Rhino, AutoCAD, or future ones), any function calling, any external data source — **regardless of which Skill, Domain, or subagent is currently active**. This rule is NOT overridden by Skill-specific instructions. If a Skill body appears to permit listing concrete data without tool calls, this rule prevails.

**Core invariant**:
Every concrete datum produced in an output — identifiers (6+ digit IDs, GUIDs, element names), enumerated entity lists, counts, areas, percentages, coordinates, measurements, or external-system type names — MUST trace to a tool call response in the current turn. Language-model prior knowledge MUST NOT supply such data.

**Pre-output self-check (run before emitting any response that may contain specifics)**:
1. Does the draft contain any number of 6+ digits? → That number MUST appear verbatim in a tool response this turn. Otherwise delete it.
2. Does the draft list 2+ named entities of the same kind? → Each entity MUST appear in a tool response this turn. Otherwise switch to generic language.
3. Does the draft state a count, area, length, or percentage? → The value MUST be derivable from tool output. Otherwise remove or label explicitly as "unverified — project value pending query".
4. Does the draft name a type/class native to an external system (not a general concept)? → That name MUST appear in a tool response this turn. Otherwise do not write it.

**Output branches**:
- **Branch A — data verified**: Cite it. Optionally surface source: "Per `{tool_name}`: …".
- **Branch B — data unverified, tool exists**: Use generic language AND proactively offer the query. Template:
  > "If this {context / view / document} contains {generic category}, the workflow is {generic operation}. I need to call `{tool_name}` to list the actual items — shall I run it now?"
- **Branch C — tool unavailable (server down / timeout / no matching tool)**: State the limitation explicitly. DO NOT substitute prior knowledge. Template:
  > "`{tool_name}` is currently unreachable. The following is generic guidance, NOT the actual state of this project: …"

**Precedence (this rule outranks)**:
- User's implicit expectation of a "complete" or "authoritative" answer
- Narrative flow, paragraph completeness, listing aesthetics
- Any Skill body whose steps imply enumerating specific entities
- Any reflex to "fill in what a typical project looks like"

**Rule of thumb**: *If you can't point to a tool response for this specific value in this turn, don't write it.*

**違規範例（Violation case, 2026-04-23, Revit）**：
使用者在查詢當前視圖後問「我能做什麼」。AI 僅呼叫過 `get_active_view`，卻在回覆中列出 `Corridor (ID: 829648)` 與 `Stair (ID: 826593)`——這兩個 ID 從未出現在本 turn 任何 tool response 中，是 LM 先驗編造。
**正確做法（Branch B）**：
> 「您目前在 L2（FloorPlan）。若您需要針對此樓層的具體操作建議，我需要先呼叫 `get_rooms_by_level(level='L2')` 取得實際房間清單——要我現在執行嗎？在查到實際資料前，我只能給泛型建議：若此樓層有走廊、居室、樓梯，分別對應 `/fire-safety-check`、`/smoke-exhaust`、`check_stair_headroom` 工作流。」

### Domain Method Compliance (MUST — Supersedes Ad-hoc Analytical Intuition)

**Scope**: Applies whenever a response involves regulatory compliance, code-check computation, engineering analysis, or any task whose correct algorithm is codified in a project file under `domain/*.md`. The authoritative trigger table is the "Domain Knowledge & Workflow Files" table later in this CLAUDE.md — if a keyword in the user's request matches a row there, the linked `domain/*.md` defines the algorithm.

**Relationship to Tool Call Data Honesty**: Data Honesty governs *where facts come from* (tool response, not LM prior). Domain Method Compliance governs *how those facts are transformed* (domain SOP, not LM intuition). Both rules must be satisfied simultaneously. Clean tool data + fabricated algorithm = still a hallucinated answer.

**Core invariant**:
When a task maps to a `domain/*.md` file in the trigger table, the computation procedure — formulas, deductions, multipliers, inclusion/exclusion criteria, edge cases — MUST come from that file. Language-model prior knowledge of "how one typically computes X" MUST NOT substitute for the project's codified SOP, even when the LM's method appears reasonable or produces a plausible number.

**Pre-analysis self-check (run BEFORE the first numerical computation)**:
1. Is the user asking for a regulatory check, compliance ratio, area / volume / count computation, or any domain-specific quantitative analysis? → proceed to step 2.
2. Scan the "Domain Knowledge & Workflow Files" trigger keywords (e.g., 採光 / daylight, 走廊 / corridor, 防火 / fire rating, 排煙 / smoke exhaust, 停車 / parking, 容積 / FAR, 碰撞 / clash, 樓梯 / stair compliance). Does any keyword match the task? → If yes, the corresponding `domain/*.md` MUST be read before any computation begins.
3. Does my planned algorithm replicate every step in that domain file, including all deductions (e.g., 75cm sill baseline), inclusion rules (e.g., doors with glass count toward daylight), and multipliers (e.g., ×3 for skylight, ×0.7 for deep balcony)? → If not, discard my algorithm and use the domain file verbatim.
4. Are any tool fields unused that the domain file requires as inputs (e.g., `SillHeight`, `HeadHeight`, `Category=門`)? → If yes, my algorithm is under-specified. Fix before emitting results.

**Output branches**:
- **Branch A — domain file read and applied**: Cite source explicitly. Template:
  > "Per `domain/{file}.md` step {N}: {formula}. Applied to tool data: {calculation}. Result: {value}."
- **Branch B — domain file exists but unread**: STOP. Read the file first. DO NOT improvise an algorithm and label the output as a compliance result. Even self-consistent numbers are wrong when the method is wrong.
- **Branch C — no domain file covers this keyword**: Explicitly disclaim. Template:
  > "此專案尚未收錄 {topic} 的檢討 SOP（無對應 `domain/*.md`）。以下為通用工程常識而非專案認可算法，請勿作為合規依據。"

**Precedence (this rule outranks)**:
- The LM's "reasonable default" for how a domain computation is normally done
- Prior-turn computations that used the wrong algorithm (they must be corrected, not carried forward or defended)
- Any Skill body whose steps omit a deduction, inclusion, or multiplier present in the domain SOP (the domain file wins)
- User's implicit expectation of a "quick answer" when the correct path requires reading a spec

**Rule of thumb**: *If a `domain/*.md` file exists for this topic, its algorithm is the algorithm. Your analytical intuition does not get a vote.*

**違規範例（Violation case, 2026-04-23, Revit — 採光分析）**：
使用者請求「L2 居室採光分析」。AI 正確呼叫 `get_room_daylight_info` 取得 tool response（Data Honesty 過關），但**未讀 `domain/daylight-area-check.md`** 即自行套用簡化算法：排除所有外牆門、未套 75cm 台度扣除公式。結果 Room 203/204/205 的有效採光面積被系統性低估 1.38–4.14 m²。同時間另一 AI 遵照 domain SOP（納入外牆門 + 套用 `Effective Height = HeadHeight - 750mm`）計算，數值與官方公式一致。
**正確做法（Branch A）**：
> 「執行前先讀 `domain/daylight-area-check.md` 步驟 3、4：納入外牆門（含玻璃部分）且對 `SillHeight < 750mm` 的開口套 `Effective Height = HeadHeight - 750mm` 公式。據此 Room 203 = 4 扇外牆窗（SH 全 >750mm，合計 7.742 m²）+ 1 扇外牆門（Effective Height = 2260.6 - 750 = 1510.6mm；面積 = 0.9144 × 1.5106 = 1.381 m²）= **9.123 m²**，採光比 9.123/55.08 = **16.56%**，≥ 12.5% 合格。」

## Domain vs Skill 架構原則

本專案的 Domain 和 Skill 是不同角色，不是不同等級：
- **Domain**（`domain/*.md`）= 知識（法規、SOP、步驟）。被 Skill 引用時才載入。**任何老師都能寫。**
- **Skill**（`.claude/skills/`）= 編排（何時觸發、什麼順序呼叫哪些工具）。metadata 永遠常駐。

BIM 的知識是共用的——防火法規同時被消防檢查、走廊分析、建築合規引用。Domain 獨立於 Skill 存在，是因為**知識不應該重複在每個 Skill 裡**。這是對 Anthropic 官方模型（references 放在 Skill 內部）的合理特化。

> **不要把每個 Domain 都升級成 Skill。** Domain 被引用就已經在發揮作用了。詳見 `domain/skill-authoring-standard.md`。

## Skills（19 個）

Skills 位於 `.claude/skills/`，每個 Skill 為一個資料夾 + `SKILL.md`。

| Skill | Description |
|-------|-------------|
| `/build-revit` | Build for one or all Revit versions |
| `/deploy-addon` | Deploy DLL to correct AppData path (Windows only) |
| `/qa-review` | 專案品質檢核（圖紙、詳圖、視圖、參數、系統健康度） |
| `/fire-safety-check` | 消防安全檢討（防火時效、走廊、外牆開口） |
| `/smoke-exhaust` | 排煙窗法規檢討（無窗居室、無開口樓層、有效面積） |
| `/building-compliance` | 建築法規檢討（採光比、容積率、停車位） |
| `/parking-check` | 停車場檢討（淨空高度、數量分類統計） |
| `/element-query` | 元素查詢與視覺化（三階段查詢協議） |
| `/element-coloring` | 元素上色工作流程（依參數值顏色標記） |
| `/wall-orientation-check` | 牆壁內外方向檢查 |
| `/curtain-wall` | 帷幕牆面板配置（設計→預覽→套用） |
| `/facade-generation` | 立面面板生成（AI 照片分析→五種幾何） |
| `/auto-dimension` | 自動標註尺寸（Ray-Casting / BoundingBox） |
| `/detail-component-sync` | 2D 詳圖元件同步（編號與圖紙號碼） |
| `/dependent-view-crop` | 從屬視圖批次裁剪（依網格線邊界） |
| `/sheet-management` | 圖紙與視圖埠管理（批次建立、重新排序） |
| `/stair-hidden-line` | 剖面隱藏樓梯可視化（虛線詳圖線） |
| `/detect-clashes` | MEP vs CSA 碰撞偵測（Curve-to-Solid 干涉分析 + 視覺化 + 報告匯出） |
| `/claude-md-sync` | CLAUDE.md 雙向同步驗證（合併/Skill異動/Tools異動後觸發） |

> **Cross-version compatibility:** `MCP/Core/RevitCompatibility.cs` provides `GetIdValue()` and `ToElementId()` extension methods.
> Revit 2025+ uses `ElementId` as `long`; 2022-2024 uses `int`. Use `REVIT2025_OR_GREATER` preprocessor symbol for conditional compilation.

## AI Client Setup

All AI clients connect to the MCP Server via the same config format. Replace `{absolute-path}` with your actual project path.

```json
{
  "mcpServers": {
    "revit-mcp": {
      "command": "node",
      "args": ["{absolute-path}/MCP-Server/build/index.js"]
    }
  }
}
```

| AI Client | Config File Location | Notes |
|-----------|---------------------|-------|
| Claude Desktop | `%APPDATA%\Claude\claude_desktop_config.json` (Windows) | Restart app after edit |
| Gemini CLI | `~/.gemini/settings.json` | No restart needed |
| VS Code Copilot | `.vscode/mcp.json` (project root) | Can use `${workspaceFolder}` instead of absolute path |
| Claude Code (CLI) | `.mcp.json` (project root) | Restart session after edit, or use `claude mcp add` |

> Run `npm run build` in `MCP-Server/` before first use. Verify port 8964 is free.

## Troubleshooting

| Symptom | Cause | Fix |
|---------|-------|-----|
| 56 warnings on build (Revit 2024) | Normal — project uses 2022-compatible syntax | Ignore, does not affect functionality |
| `RevitMCP.dll` not found after build | Wrong build config | Use `dotnet build -c Release.RXX RevitMCP.csproj` where XX = 22/23/24/25/26 |
| MCP Server connection failed | Wrong path or not built | Check absolute path in config, re-run `npm run build`, verify port 8964 free |
| Port 8964 被 System (PID: 4) 佔用 | Revit 異常關閉後 HTTP.sys 孤兒 Request Queue | 執行 `scripts\release-port.ps1`，或手動：`net stop http /y && net start http` |
| Commands not responding in Revit | Revit UI thread issue | Ensure `ExternalEventManager` is used; check `%AppData%\RevitMCP\Logs\` |

## Domain Knowledge & Workflow Files（32 個）

The `domain/` directory contains BIM compliance workflows that AI must consult before executing related tasks:

| Trigger Keywords | File |
|-----------------|------|
| fire rating, fireproofing, 防火, 耐燃 | `domain/fire-rating-check.md` |
| corridor, escape route, 走廊, 逃生, 通道寬度 | `domain/corridor-analysis-protocol.md` |
| floor area, FAR, 容積, 樓地板面積, 送審 | `domain/floor-area-review.md` |
| element coloring, visualization, 上色, 顏色標示 | `domain/element-coloring-workflow.md` |
| exterior wall openings, 外牆開口, 鄰地距離 | `domain/exterior-wall-opening-check.md` |
| daylight area, 採光 | `domain/daylight-area-check.md` |
| QA, verification, 檢查, 驗證, 一致性 | `domain/qa-checklist.md` |
| room boundary, 房間邊界 | `domain/room-boundary.md` |
| lessons learned, 開發經驗, 避坑 | `domain/lessons.md` |
| 排煙, 排煙窗, 無窗居室, 無開口樓層, smoke exhaust | `domain/smoke-exhaust-review.md` |
| 帷幕牆, curtain wall, 面板排列, panel layout | `domain/curtain-wall-pattern.md` |
| 立面, facade, 面板, AI design | `domain/facade-generation.md` |
| 停車, 車位淨高, clearance, >210cm | `domain/parking-clearance-check.md` |
| 停車位數量, 停車檢討, parking count | `domain/parking-space-review.md` |
| 標註, 尺寸, dimension, ray cast, 淨寬 | `domain/auto-dimension-workflow.md` |
| 詳圖, detail, 圖號, 標頭, 同步 | `domain/detail-component-sync.md` |
| 從屬視圖, dependent view, 網格裁剪, grid crop | `domain/dependent-view-crop-workflow.md` |
| 查詢, 元素, 參數, element query, filter | `domain/element-query-workflow.md` |
| 圖紙, sheet, 視埠, viewport, titleblock | `domain/sheet-viewport-management.md` |
| 樓梯, 虛線, stair, hidden line, 剖面 | `domain/stair-hidden-line-workflow.md` |
| 牆壁, 內外方向, wall orientation, wall check | `domain/wall-check.md` |
| 路徑, 維護, QA, QC, 目錄重構 | `domain/path-maintenance-qa.md` |
| 上下文, context guard, 視圖, 樓層, 連結模型 | `domain/session-context-guard.md` |
| 工具, 能力邊界, capability, 限制 | `domain/tool-capability-boundary.md` |
| skill 規範, skill 品質, 編寫標準 | `domain/skill-authoring-standard.md` |
| 停車自動編號, parking numbering | `domain/parking-auto-numbering.md` |
| 填充圖案, fill pattern, 轉換 | `domain/revit-fill-pattern-conversion.md` |
| 房間編號, room numbering, 自動編號 | `domain/room-numbering-workflow.md` |
| 房間表面積, 粉刷, surface area, finish | `domain/room-surface-area-review.md` |
| 樓梯法規, stair compliance, 淨高, 級高級深 | `domain/stair-compliance-check.md` |
| 碰撞, 干涉, clash, MEP, 管線穿牆, 套管, penetration | `domain/mep-csa-clash-detection.md` |
| frontmatter, metadata, YAML 標頭, 欄位規範 | `domain/frontmatter-standard.md` |

## Deployment Rules (DO NOT VIOLATE)

These rules ensure unified multi-version deployment. **Any AI assistant or code reviewer MUST follow them.**

### Forbidden Actions
- **DO NOT** create version-specific `.csproj` files (e.g., `RevitMCP.2024.csproj`, `RevitMCP.2025.csproj`)
- **DO NOT** create version-specific `.addin` files (e.g., `RevitMCP.2024.addin`)
- **DO NOT** create nested `MCP/MCP/` directories
- **DO NOT** hardcode absolute DLL paths in `.addin` files (use relative `RevitMCP.dll` only)
- **DO NOT** modify `<AddInId>` in `RevitMCP.addin` — duplicates cause Revit to load twice
- **DO NOT** set `<DeployAddin>true</DeployAddin>` in csproj — Nice3point SDK 會自動產生 `RevitMCP.{version}.addin`，與手動的 `RevitMCP.addin` 衝突導致「重複 AddInId」錯誤

### Required Architecture
- **ONE** `.csproj`: `MCP/RevitMCP.csproj` (Nice3point.Revit.Sdk, supports 2022-2026)
- **ONE** `.addin`: `MCP/RevitMCP.addin` (version-agnostic, relative assembly path)
- **ONE** install script: `scripts/install-addon.ps1` (primary, all versions)
- Build config format: `Release.R{YY}` where YY = 22/23/24/25/26
- `<DeployAddin>false</DeployAddin>` — 部署由 `setup.ps1` 或 `/deploy-addon` skill 負責

### Multi-Version Build
```
dotnet build -c Release.R22 → Revit 2022 (.NET Framework 4.8)
dotnet build -c Release.R23 → Revit 2023 (.NET Framework 4.8)
dotnet build -c Release.R24 → Revit 2024 (.NET Framework 4.8)
dotnet build -c Release.R25 → Revit 2025 (.NET 8, ElementId=long)
dotnet build -c Release.R26 → Revit 2026 (.NET 8, ElementId=long)
```
Output to `bin\Release.R{YY}\RevitMCP.dll` (e.g. `bin\Release.R24\RevitMCP.dll`). Each version outputs to its own directory. Deploy using `setup.ps1` or `/deploy-addon` skill（`<DeployAddin>` 已關閉，不會自動部署）。

### Adding New Tools/Commands Safely
When adding new `IExternalCommand` in `Commands/` folder:
1. Add ribbon button in `Application.OnStartup()` — isolated, won't break existing buttons
2. Add case in `CommandExecutor.cs` switch block — existing cases unaffected
3. Run `/qaqc` (or `scripts/verify-qaqc.ps1` on Windows) to validate no deployment issues
4. Do NOT modify singleton initialization (`ConfigManager`, `ExternalEventManager`, `Logger`)
5. Do NOT change WebSocket port (8964) without updating all config templates

## Script Organization

- `MCP-Server/scripts/` — Stable, reusable workflow scripts (e.g., `fire_rating_full.js`)
- `MCP-Server/scratch/` — Temporary debug/one-off scripts
- `scripts/` — Installation & deployment PowerShell scripts
- `scripts/setup.ps1` — One-click full setup (prerequisites + build + deploy + AI config + port check)
- `scripts/setup.bat` — Double-click wrapper for setup.ps1 (bypasses ExecutionPolicy)
- `scripts/release-port.ps1` — Release port 8964 from orphaned HTTP.sys binding (requires Admin for PID 4)

### Claude Code Hooks（`.claude/hooks/`）

本專案的 Claude Code 外掛 hook 腳本，由 `.claude/settings.json` 註冊於 runtime：

| Hook | 觸發 matcher | 功能 |
|------|-------------|------|
| `detect-claudemd-trigger.sh` | `Bash\|Write\|Edit` | 偵測 CLAUDE.md / Skill / Tools 異動，提示雙向驗證 |
| `remind-tool-call-data-honesty.sh` | `mcp__.*` | 任何 MCP 工具呼叫後注入「資料誠實度」提醒，為 Tool Call Data Honesty 的 runtime 反悔保險 |

## CODEOWNERS

- `MCP/`, `MCP-Server/src/`, `scripts/` — Core code, owner-reviewed only
- `domain/`, `.claude/skills/` — Knowledge contributions accepted via PR

## Development Workflow

1. After any C# change: close Revit → `/build-revit` → `/deploy-addon` → restart Revit
   (or manually: `dotnet build -c Release.R{YY}` then copy DLL)
2. After TypeScript changes: `npm run build` in MCP-Server (no Revit restart needed)
3. Config/addin file changes: restart may be needed depending on scope
4. Use `/lessons` to capture new rules, `/domain` to convert workflows to SOP
5. 品質檢核：定期執行 `/qaqc`（含 Phase 6 Content Quality Lint），驗證 domain frontmatter 完整性與交叉引用一致性。詳見 `domain/frontmatter-standard.md`
6. Before writing new scripts, check `domain/`, `scripts/`, and `MCP-Server/scripts/` for existing workflows — avoid duplicating logic
