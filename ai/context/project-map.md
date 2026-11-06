# 專案地圖

狀態：已依 `CLAUDE.md` 與原始碼填寫（2026-08，loop-up Stage S4，PR #116／Monstrare 收編工作的一部分）。

本檔全部內容可追溯到本次填寫時實際讀取的 `CLAUDE.md`（Project Overview、Key Source Files、Current Source-of-Truth Counts、Single-Connection Limitation、Phase 5 design constraints 各節）、`MCP-Server/package.json` / `scripts/` 目錄列表、`MCP/RevitMCP.csproj`（多版本 build config 設定本身）與 `docs/core-reload-architecture.md`（4.1 節 .NET Runtime 對應表），未憑記憶編造。`CLAUDE.md` 才是唯一 canonical 事實來源；本檔只是把它整理成 Monstrare `context-protocol.md` 期待的「專案地圖」格式，兩者衝突以 `CLAUDE.md` 為準。

## 產品

- 名稱：Revit MCP —— 透過 Model Context Protocol（MCP）串接 AI 語言模型與 Autodesk Revit 的橋接器，讓 AI client 用自然語言工具呼叫驅動 BIM 工作流。
- 使用者：透過 Claude Desktop / Claude Code / Gemini CLI / VS Code Copilot / Antigravity 等 AI client 操作 Revit 的 BIM 工作者（建築、機電、結構等專業）。
- 核心工作流程：
  ```text
  AI Client（stdio）
    -> MCP Server（Node.js/TypeScript，MCP-Server/src/index.ts）
    -> WebSocket client（MCP-Server/src/socket.ts）
    -> Revit Add-in（C#，MCP/Application.cs）
    -> HttpListener WebSocket server on localhost:8964（MCP/Core/SocketService.cs）
    -> ExternalEventManager（MCP/Core/ExternalEventManager.cs）
    -> Revit API
  ```
  另有一條選用、彼此獨立的路徑：Revit WPF 視窗直接呼叫 AI API 的 embedded-chat 方向，不經過本 stdio MCP server。

## 技術棧

- 前端：本專案沒有單一傳統 SPA；有數個各自獨立的靜態／嵌入式 HTML 介面 —— MCP Apps（`MCP-Server/src/apps/*/app.ts`，由 `scripts/build-apps.mjs` 用 esbuild 打包成自足的 `MCP-Server/build/apps/*/index.html`，例如 clash-viewer）、Monstrare 看板（`tools/kanban/index.html`、`tools/green-material/GM_kanban.html`）、綠建材檢索頁（`assets/green-material-showcase.html`——**本機生成物，`.gitignore:147` 排除，全新 clone 不會有這個檔案**，由 `/GM_update`（`tools/green-material/GM_update_tabc_database.py`）從 `assets/green-material-showcase.template.html`（git 追蹤的樣板）重建；版控裡只有樣板檔）。
- 後端：Node.js/TypeScript（`MCP-Server/src/index.ts` 為 stdio server 入口）＋ C#（`MCP/RevitMCP.csproj`，單一多版本 build project，涵蓋 Revit 2022–2026，`Release.R22`/`R23`/`R24` 為 .NET Framework 4.8、`Release.R25`/`R26` 為 .NET 8）。另有選用的 Python 子行程 `bridge/python/skills/ezdxf_worker.py`（供 `dwg-column-import` 模式 C 讀取 DXF/DWG）。
- 資料庫：無傳統資料庫；資料層以檔案為主，例如 Monstrare 看板卡片 `tools/kanban/cards/*.json`、綠建材工具鏈的 `tabc_master_database.json`——**同樣是本機生成物，`.gitignore:146` 排除，不在版控中**，由 `/GM_update`（`tools/green-material/GM_update_tabc_database.py`）從 TABC 官網抓取／合併產生；全新 clone 後第一次執行會自動視為空資料庫並整批建立（bootstrap），不是錯誤狀態。
- 身分驗證：沒有使用者帳號系統。Revit 端 WebSocket 服務（`MCP/Core/SocketService.cs`）改用單一連線鎖把關：帶 `Origin` header 的握手一律先被拒絕（HTTP 403，無設定可關），其餘連線受 `ServiceSettings.ExclusiveLock`（預設 `true`）的獨占鎖保護，同一時間只允許一個 MCP client 連線。
- 測試：`CLAUDE.md` 未定義統一的單元測試框架；`MCP-Server/package.json` 有針對特定契約的腳本測試，例如 `npm run test:opening-candidate-contract`（執行 `scripts/test-opening-candidate-contract.mjs`）。主要的專案級品質關卡是 `scripts/verify-qaqc.ps1`（見下方常用指令），而非傳統的整合測試套件。
- 部署：`scripts/install-addon.ps1`（或 `/deploy-addon` skill）把建置好的 `MCP/bin/Release.R{22..26}/RevitMCP.dll` 部署到對應版本的 Revit Add-ins 資料夾。

## 重要目錄

| 路徑 | 用途 | 備註 |
|---|---|---|
| `MCP/Application.cs` | Revit `IExternalApplication` 進入點與 ribbon 設定 | 來源：`CLAUDE.md` Key Source Files |
| `MCP/Core/SocketService.cs` | Revit 端 WebSocket server（`HttpListener`） | 含單一連線鎖與 Origin-403 閘門，見「身分驗證」 |
| `MCP/Core/ExternalEventManager.cs` | 把 Revit API 呼叫排進 Revit UI 執行緒 | WebSocket 流程必經 |
| `MCP/Core/CommandExecutor.cs` + `MCP/Core/Commands/*.cs` | 主要命令分派器，依工作區域拆模組 | switch/dispatcher 慣例 |
| `MCP/RevitMCP.csproj` / `MCP/RevitMCP.addin` | 唯一的多版本 build project／唯一的版本無關 add-in manifest | 禁止新增版本專屬 csproj 或 addin |
| `MCP-Server/src/index.ts` | MCP stdio server 入口 | |
| `MCP-Server/src/socket.ts` | 連往 Revit 的 WebSocket client | |
| `MCP-Server/src/tools/index.ts` | 工具模組註冊（`registerRevitTools()`）與 `MCP_PROFILE` 過濾 | 目前 239 個 runtime tool（`CLAUDE.md` 計數表，會隨版本變動，勿硬記） |
| `MCP-Server/src/tools/revit-tools.ts` | 工具名稱 → Revit 命令的執行橋接 | |
| `domain/*.md` | 共用 BIM SOP／法規／公式，人類與 AI 共用 | 目前 90 個檔案（同上計數表） |
| `.claude/skills/*/SKILL.md` | Claude Code skill（AI 工作流程編排） | 目前 61 個（同上計數表）；`ai/skills/*.md` 不算在內，見 `.agents/AGENTS.md` 開頭聲明 |
| `scripts/verify-qaqc.ps1` | 全庫 QA/QC 閘門 | 改動文件／工具／skill／domain／build 設定後必跑 |
| `tools/kanban/`、`ai/**`、`.agents/AGENTS.md`、`scripts/monstrare_mcp_server.py` | Monstrare（選用的 AI 開發流程套件） | 2026-08 由 PR #116（@CWLin0518）收編；非本專案預設工作流，見 `.agents/AGENTS.md` 開頭的治理位階聲明 |

## 常用指令

| 指令 | 用途 | 備註 |
|---|---|---|
| `npm run build`（於 `MCP-Server/`） | `tsc` 編譯 + `scripts/build-apps.mjs` 打包 MCP Apps | 來源：`MCP-Server/package.json` |
| `npm run test:opening-candidate-contract`（於 `MCP-Server/`） | 跑 `scripts/test-opening-candidate-contract.mjs` 契約測試 | 目前 package.json 裡唯一具名的測試腳本 |
| `dotnet build -c Release.R{22,23,24,25,26} RevitMCP.csproj`（於 `MCP/`） | 建置各 Revit 版本專屬輸出 | 完整指令見 `/build-revit` skill、README.md Build 段 |
| `scripts/install-addon.ps1` | 部署 DLL 到 Revit Add-ins 資料夾 | 或用 `/deploy-addon` skill |
| `.\scripts\verify-qaqc.ps1 -SkipBuild -SkipDeploy` | 文件／工具／skill／domain／build 設定變更後的標準 QA/QC | `CLAUDE.md` 規定的必跑閘門 |
| `.\scripts\verify-qaqc.ps1 -Version 2024` | 完整版（含 build/deploy）QA/QC，發版前跑 | 需 Windows + 對應 Revit SDK |
| `scripts/release-port.ps1` | 釋放卡在 HTTP.sys 上的 `8964` 埠 | 見 `domain/tool-capability-boundary.md`（L9: MCP Failure Mode & Recovery，含 8964 佔用診斷步驟） |

## 維護備註

`CLAUDE.md` 的 Key Source Files、Current Source-of-Truth Counts（239 工具／94 domain／61 skill）與 Build Commands 若更新，本檔的對應內容需一併同步，否則會變成過期的第二份事實來源，違背 `context-protocol.md` 要求 agent 先讀本檔取得情境的用意。
