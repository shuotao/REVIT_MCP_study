# Revit MCP - AI-Powered Revit Control

[English](README.en.md) | 繁體中文

Revit MCP 透過 Model Context Protocol (MCP) 讓 AI Client 呼叫 Revit 工具，並由 Revit Add-in 在本機執行 Revit API 工作流程。

- 示範影片：[Revit MCP - AI 驅動的 BIM 工作流程示範](https://youtu.be/YpAYF-GxrhA)
- 知識站：<https://shuotao.github.io/REVIT_MCP_study/>
- 預設 WebSocket port：`8964`

## 目前專案狀態

| 項目 | 數量 | 來源 |
|---|---:|---|
| Runtime MCP tools | 99 | `MCP-Server/src/tools/index.ts` 的 `registerRevitTools()` |
| Domain SOP files | 49 | `domain/*.md` 扣除 `README.md`，加上 `domain/references/*.md` |
| Claude skills | 24 | `.claude/skills/*/SKILL.md` |

如果這些數字改變，請同步更新 `CLAUDE.md`、本 README、`README.en.md`、`docs/DOCUMENT_AUDIENCE_INVENTORY.md`，並執行：

```powershell
.\scripts\verify-qaqc.ps1 -SkipBuild -SkipDeploy
```

## 架構

```text
AI Client
  Claude Desktop / Claude Code / Gemini CLI / VS Code Copilot / Antigravity
        |
        | stdio
        v
MCP Server
  Node.js / TypeScript
  MCP-Server/build/index.js
        |
        | WebSocket ws://localhost:8964
        v
Revit Add-in
  C# / Revit API
  MCP/Application.cs
  MCP/Core/SocketService.cs
  MCP/Core/ExternalEventManager.cs
        |
        v
Autodesk Revit
```

外部 AI Client 不需要在本專案內設定 AI API Key；AI 帳號與授權由各 AI Client 自己管理。只有「Revit 內嵌 AI Chat」這種直接呼叫 AI API 的方案才需要 API Key。

## 系統需求

| 項目 | 需求 |
|---|---|
| OS | Windows 10 或更新版本 |
| Revit | Autodesk Revit 2022、2023、2024、2025、2026 |
| .NET | Revit 2022-2024 使用 .NET Framework 4.8；Revit 2025-2026 使用 .NET 8 |
| Node.js | LTS，建議 20.x 或更新版本 |

## 一鍵安裝

新手建議使用：

```powershell
.\scripts\setup.ps1
```

AI Agent 或非互動模式可使用：

```powershell
powershell -ExecutionPolicy Bypass -File scripts/setup.ps1 -NonInteractive -RevitVersions "2024,2025"
```

腳本會檢查環境、安裝相依套件、編譯 MCP Server、編譯並部署 Revit Add-in，並協助設定常見 AI Client。

## 手動安裝

### 1. 編譯 MCP Server

```powershell
cd MCP-Server
npm install
npm run build
```

AI Client 會啟動：

```text
node MCP-Server/build/index.js
```

### 2. 編譯 Revit Add-in

請依 Revit 版本選擇設定：

```powershell
cd MCP
dotnet build -c Release.R22 RevitMCP.csproj   # Revit 2022
dotnet build -c Release.R23 RevitMCP.csproj   # Revit 2023
dotnet build -c Release.R24 RevitMCP.csproj   # Revit 2024
dotnet build -c Release.R25 RevitMCP.csproj   # Revit 2025
dotnet build -c Release.R26 RevitMCP.csproj   # Revit 2026
```

輸出路徑是：

```text
MCP/bin/Release.R{YY}/RevitMCP.dll
```

例如 Revit 2024：

```text
MCP/bin/Release.R24/RevitMCP.dll
```

### 3. 部署 Add-in

建議使用：

```powershell
.\scripts\install-addon.ps1
```

手動部署時，`.addin` 與 DLL 必須放在對應版本的 Revit Addins 位置，並維持 `RevitMCP.addin` 內的相對 assembly path：

```xml
<Assembly>RevitMCP\RevitMCP.dll</Assembly>
```

不要建立版本專屬 `.addin`，也不要硬寫絕對 DLL 路徑。

## AI Client 設定

本專案已有 Claude Code / Codex 風格的 `.mcp.json`：

```json
{
  "mcpServers": {
    "revit-mcp": {
      "type": "stdio",
      "command": "node",
      "args": ["./MCP-Server/build/index.js"],
      "env": {}
    }
  }
}
```

VS Code 設定在 `.vscode/mcp.json`：

```json
{
  "servers": {
    "revit-mcp": {
      "type": "stdio",
      "command": "node",
      "args": ["${workspaceFolder}/MCP-Server/build/index.js"],
      "env": {}
    }
  }
}
```

其他 AI Client 的核心概念相同：使用 `node` 啟動 `MCP-Server/build/index.js`。

各 Client 的設定範本對照：

| AI Client | 設定位置 | 範本 |
|---|---|---|
| Claude Code | 專案根目錄 `.mcp.json` | 已內建，開箱即用 |
| Claude Desktop | `%APPDATA%\Claude\claude_desktop_config.json` | `MCP-Server/claude_desktop_config.json` |
| Gemini CLI | `~/.gemini/settings.json` | `MCP-Server/gemini_mcp_config.json` |
| VS Code Copilot | `.vscode/mcp.json` | 已內建 |
| Antigravity | UI 設定 | `Antigravity_MCP_Complete_Guide.md` |

範本中的 `<YOUR_PROJECT_PATH>` 需替換為本專案的實際路徑。

### AI Client 切換與並用限制

Revit 端的 WebSocket 服務一次只接受一條 MCP 連線：後連上的 MCP Server 會取代先前的連線。因此多個 AI Client 是「切換使用」而不是「同時並用」：

1. 關閉目前使用的 AI Client（或停用其 MCP server）。
2. 啟動另一個 AI Client，它的 MCP Server 連上 `localhost:8964` 後即接手。
3. 若連線狀態異常，於 Revit ribbon 重啟 MCP 服務即可重置。

## 啟動流程

1. 啟動 Revit。
2. 載入或建立專案。
3. 在 Revit ribbon 的 MCP Tools 面板啟動 MCP 服務。
4. 確認 Revit 顯示 WebSocket server 已監聽 `localhost:8964`。
5. 啟動或重啟 AI Client，讓它載入 MCP Server。
6. 在 AI Client 中呼叫 Revit MCP tools。

如果 `localhost:8964` 連不上，通常代表 Revit 沒開、MCP 服務沒開、port 被佔用，或 AI Client 的 `REVIT_MCP_PORT` 與 Revit 端設定不一致。

## 專案結構

```text
REVIT_MCP/
  MCP/                         Revit Add-in (C#)
    Application.cs             Revit add-in entry point
    RevitMCP.csproj            Single multi-version project
    RevitMCP.addin             Single add-in manifest
    Core/
      SocketService.cs         Revit-side WebSocket server
      ExternalEventManager.cs  UI-thread execution bridge
      RevitCompatibility.cs    Revit 2022-2026 compatibility helpers
      CommandExecutor.cs       Main command dispatcher
      Commands/*.cs            Command modules
  MCP-Server/                  MCP Server (Node.js / TypeScript)
    src/index.ts               stdio MCP server entry
    src/socket.ts              WebSocket client to Revit
    src/tools/*.ts             MCP tool definitions
  domain/                      Shared BIM SOPs; do not convert to English-only
  .claude/                     AI commands and skills
  docs/                        Human-facing docs and public knowledge site
  scripts/                     Setup, deployment, QA/QC scripts
  log/                         Append-only session and commit logs
```

## AI 文件與人類文件

本專案刻意分層：

| 類型 | 位置 | 原則 |
|---|---|---|
| AI-only | `CLAUDE.md`, `.claude/commands/`, `.claude/skills/` | 以英文為主，避免 mojibake |
| Human-facing | `README.md`, `README.en.md`, `docs/`, `scripts/README.md` | 依讀者語言撰寫 |
| Shared | `domain/*.md`, `log/README.md` | Domain 必須保留中文可讀性，不可全英文化 |
| Historical | `docs/_archive/**`, old logs | 預設保留，不拿來當現行規則 |

完整盤點見 [docs/DOCUMENT_AUDIENCE_INVENTORY.md](./docs/DOCUMENT_AUDIENCE_INVENTORY.md)。

## Domain、Skill、Tool 的分工

- `domain/*.md`：BIM SOP、法規邏輯、計算方法。這是人類與 AI 共用的知識層，不能全英文化。
- `.claude/skills/*/SKILL.md`：AI 執行工作流程的編排層。
- `MCP-Server/src/tools/*.ts`：MCP 工具定義與輸入 schema。
- `MCP/Core/Commands/*.cs`：真正呼叫 Revit API 的實作。

當 Domain 與 Skill 對方法描述不同時，以 Domain 為準。

## QA/QC

文件、工具、Domain、Skill、建置或部署相關修改後，至少執行：

```powershell
.\scripts\verify-qaqc.ps1 -SkipBuild -SkipDeploy
```

正式部署前執行完整檢查：

```powershell
.\scripts\verify-qaqc.ps1 -Version 2024
```

QA/QC 會檢查：

- 禁止的 legacy 檔案與路徑。
- 必要檔案是否存在。
- README / CLAUDE / docs 的統計是否同步。
- Domain table 與實際 Domain 檔案是否互相覆蓋。
- Markdown local link 是否失效。
- Domain frontmatter 是否完整。
- AI-only、人類文件、Shared Domain 的受眾分類與 mojibake 風險。

## 常見問題

### AI 說找不到 Revit tools

確認：

1. `MCP-Server` 已執行 `npm run build`。
2. AI Client 的 MCP 設定指向正確的 `MCP-Server/build/index.js`。
3. AI Client 已重啟或重新載入 MCP servers。

### MCP Server 連不上 Revit

確認：

1. Revit 已啟動。
2. Revit ribbon 中 MCP 服務已開啟。
3. `localhost:8964` 未被其他程式佔用。
4. 若 port 被 HTTP.sys / PID 4 卡住，可嘗試：

```powershell
powershell -ExecutionPolicy Bypass -File scripts\release-port.ps1
```

### Revit 看不到 MCP Tools 面板

確認 `.addin` 與 DLL 已部署到對應版本的 `%APPDATA%\Autodesk\Revit\Addins\{version}`，然後重新啟動 Revit。

## 重要規則

- 只保留一個 `MCP/RevitMCP.csproj`。
- 只保留一個 `MCP/RevitMCP.addin`。
- 不建立版本專屬 `.csproj` 或 `.addin`。
- 不建立 `MCP/MCP/` 巢狀目錄。
- 不把 `.addin` 的 `<Assembly>` 改成絕對路徑。
- 不把 Domain 改成全英文。
- 不用手寫 WebSocket JSON 繞過 MCP Server。
- 涉及 Revit 目前視圖、樓層、選取或文件狀態時，AI 必須重新查詢 live state，不能沿用舊記憶。

## 文件導覽

| 文件 | 用途 |
|---|---|
| [CLAUDE.md](./CLAUDE.md) | AI agent 的主要規則與專案地圖 |
| [AGENTS.md](./AGENTS.md) | redirect 到 `CLAUDE.md` |
| [GEMINI.md](./GEMINI.md) | redirect 到 `CLAUDE.md` |
| [README.en.md](./README.en.md) | English README |
| [CONTRIBUTING.md](./CONTRIBUTING.md) | 貢獻流程 |
| [CHANGELOG.md](./CHANGELOG.md) | 版本紀錄 |
| [domain/README.md](./domain/README.md) | Domain SOP 目錄 |
| [domain/lessons.md](./domain/lessons.md) | 專案經驗與教訓 |
| [.claude/skills/](./.claude/skills/) | AI skills |
| [.claude/commands/](./.claude/commands/) | AI slash commands |
| [scripts/README.md](./scripts/README.md) | 腳本說明 |
| [docs/DOCUMENT_AUDIENCE_INVENTORY.md](./docs/DOCUMENT_AUDIENCE_INVENTORY.md) | 文件受眾盤點 |
| [docs/DOCS_STRUCTURE.md](./docs/DOCS_STRUCTURE.md) | docs 目錄說明 |
| [log/README.md](./log/README.md) | log append 規則 |

## License

MIT License
