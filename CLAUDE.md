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
| MCP Tools | 共用 55 個工具 | 共用 55 個工具 | 共用 55 個工具 |

SKILL.md 格式遵循 [Agent Skills 開放標準](https://agentskills.io)（YAML frontmatter + Markdown body），Claude Code 與 Gemini CLI 皆原生支援。

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
| `MCP/Core/CommandExecutor.cs` | Central command dispatcher (59+ commands), largest file |
| `MCP/Core/SocketService.cs` | HttpListener-based WebSocket server in Revit |
| `MCP/Core/RevitCompatibility.cs` | Cross-version compatibility layer (ElementId int→long for 2025+) |
| `MCP/Core/ExternalEventManager.cs` | Ensures commands execute on Revit UI thread |
| `MCP-Server/src/index.ts` | MCP Server entry (StdioServerTransport) |
| `MCP-Server/src/socket.ts` | RevitSocketClient — WebSocket client to Revit |
| `MCP-Server/src/tools/` | Tool definitions (55 tools, 分 8 個模組) |
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
MCP Server 已封裝 55 個 tools，處理了格式轉換、錯誤處理、重連機制。自寫腳本會：
- 繞過既有的錯誤處理與格式驗證
- 產生 process 掛起（如自動重連導致無法退出）
- 與 Revit API 的 PascalCase 欄位不一致而靜默失敗
- 重造輪子，浪費使用者時間

## Domain vs Skill 架構原則

本專案的 Domain 和 Skill 是不同角色，不是不同等級：
- **Domain**（`domain/*.md`）= 知識（法規、SOP、步驟）。被 Skill 引用時才載入。**任何老師都能寫。**
- **Skill**（`.claude/skills/`）= 編排（何時觸發、什麼順序呼叫哪些工具）。metadata 永遠常駐。

BIM 的知識是共用的——防火法規同時被消防檢查、走廊分析、建築合規引用。Domain 獨立於 Skill 存在，是因為**知識不應該重複在每個 Skill 裡**。這是對 Anthropic 官方模型（references 放在 Skill 內部）的合理特化。

> **不要把每個 Domain 都升級成 Skill。** Domain 被引用就已經在發揮作用了。詳見 `domain/skill-authoring-standard.md`。

## Skills（18 個）

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
| Claude Desktop | `%APPDATA%\Claude\config.json` (Windows) | Restart app after edit |
| Gemini CLI | `~/.gemini/settings.json` | No restart needed |
| VS Code Copilot | `.vscode/mcp.json` (project root) | Can use `${workspaceFolder}` instead of absolute path |

> Run `npm run build` in `MCP-Server/` before first use. Verify port 8964 is free.

## Troubleshooting

| Symptom | Cause | Fix |
|---------|-------|-----|
| 56 warnings on build (Revit 2024) | Normal — project uses 2022-compatible syntax | Ignore, does not affect functionality |
| `RevitMCP.dll` not found after build | Wrong build config | Use `dotnet build -c Release.RXX RevitMCP.csproj` where XX = 22/23/24/25/26 |
| MCP Server connection failed | Wrong path or not built | Check absolute path in config, re-run `npm run build`, verify port 8964 free |
| Commands not responding in Revit | Revit UI thread issue | Ensure `ExternalEventManager` is used; check `%AppData%\RevitMCP\Logs\` |

## Domain Knowledge & Workflow Files（26 個）

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
- `scripts/setup.ps1` — One-click full setup (prerequisites + build + deploy + AI config)
- `scripts/setup.bat` — Double-click wrapper for setup.ps1 (bypasses ExecutionPolicy)

## CODEOWNERS

- `MCP/`, `MCP-Server/src/`, `scripts/` — Core code, owner-reviewed only
- `domain/`, `.claude/skills/` — Knowledge contributions accepted via PR

## Development Workflow

1. After any C# change: close Revit → `/build-revit` → `/deploy-addon` → restart Revit
   (or manually: `dotnet build -c Release.R{YY}` then copy DLL)
2. After TypeScript changes: `npm run build` in MCP-Server (no Revit restart needed)
3. Config/addin file changes: restart may be needed depending on scope
4. Use `/lessons` to capture new rules, `/domain` to convert workflows to SOP
5. Before writing new scripts, check `domain/`, `scripts/`, and `MCP-Server/scripts/` for existing workflows — avoid duplicating logic
