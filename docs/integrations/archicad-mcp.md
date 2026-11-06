# Archicad MCP 安裝與環境設置

> **落地狀態（2026-08-10 補註）**：本文件是 fork 收編內容（issue [#98](https://github.com/shuotao/REVIT_MCP_study/issues/98)），僅落地了文件本身。文中提到的 `scripts/setup-archicad-mcp.{sh,ps1,bat,py}` 四支腳本、`.mcp.json` / `.vscode/mcp.json` 的 `archicad-mcp` entry 寫入邏輯，**均不在本次收編範圍內、目前此 repo 中不存在**——收編時的稽核紀錄明確把它們列為「code/config，非 domain 知識，不在 fork PR 允許路徑內」而未取回。在這些腳本被實際建立並通過驗證前，請勿依本文件的指令直接執行；「先做唯讀檢查」與「啟用 Archicad MCP」兩節目前僅供規劃參考。

本整合是 **opt-in**：BIM_MCP clone 完成後仍維持 Revit-only。只有主動執行 Archicad setup，才會把獨立的 `archicad-mcp` server 加到專案設定；不會覆寫 Revit MCP。

## 架構

```text
AI Client
  - revit-mcp    -> Node MCP Server -> Revit Add-in -> Revit API
  - archicad-mcp -> uvx -> tapir-archicad-mcp -> Archicad JSON API / Tapir
```

兩個 backend 的程序、連線、工具與識別碼彼此獨立。Revit `ElementId` 不得傳給 Archicad；Archicad GUID 也不得傳給 Revit。

## 前置需求

| 項目 | 說明 |
|---|---|
| Archicad | 需另外安裝，並開啟目標專案 |
| Archicad JSON API | 由 Archicad 提供 |
| [Tapir Add-On](https://github.com/ENZYME-APD/tapir-archicad-automation) | 完整社群指令集所需，必須選擇相容的 Archicad 版本 |
| [uv / uvx](https://docs.astral.sh/uv/getting-started/installation/) | 下載並隔離執行 Python MCP runtime |
| 網路 | 第一次解析套件及下載語意搜尋模型時需要 |
| MCP Client | Claude Code、Claude Desktop、Gemini CLI 或 VS Code |

setup 不會自動安裝 Archicad 或 Tapir Add-On，因為兩者涉及授權、作業系統與版本選擇。

## 先做唯讀檢查

macOS / Linux：

```bash
./scripts/setup-archicad-mcp.sh --check-only
```

Windows PowerShell：

```powershell
powershell -ExecutionPolicy Bypass -File scripts/setup-archicad-mcp.ps1 -CheckOnly
```

此檢查確認 `uv`、JSON 結構、Revit entry 與既有 Archicad entry（如果存在），不下載套件也不修改檔案。

## 啟用 Archicad MCP

macOS / Linux：

```bash
./scripts/setup-archicad-mcp.sh
```

Windows 可雙擊 `scripts\setup-archicad-mcp.bat`，或執行：

```powershell
powershell -ExecutionPolicy Bypass -File scripts/setup-archicad-mcp.ps1
```

setup 會依序：

1. 驗證預設 Revit MCP 設定仍存在。
2. 解析固定版本 `tapir-archicad-mcp==0.4.3`。
3. 核對套件版本與 `archicad-server` entry point。
4. 只在 `.mcp.json` 與 `.vscode/mcp.json` 加入 `archicad-mcp` key。
5. 確認記憶體中的 `revit-mcp` object 完全未改變後才寫檔。

如果套件解析失敗，專案設定不會被修改。

## 使用者層級 Client 設定

Claude Desktop 與 Gemini CLI 可以選擇安全合併使用者設定：

```bash
./scripts/setup-archicad-mcp.sh --configure-user --client all
```

```powershell
powershell -ExecutionPolicy Bypass -File scripts/setup-archicad-mcp.ps1 `
  -ConfigureUser -Client all
```

可用 client：`claude-desktop`、`gemini`、`all`。既有設定會先建立時間戳備份；無效 JSON 不會被覆寫。

## 啟動後驗證

1. 安裝並啟用相容的 Tapir Add-On。
2. 開啟 Archicad 與測試專案。
3. 重新啟動 MCP Client。
4. 呼叫 `discovery_list_active_archicads`。
5. 選定 instance port，再使用 `archicad_discover_tools` 搜尋命令。
6. 依回傳 schema 組 arguments，使用 `archicad_call_tool` 執行。

只看到三個公開 Archicad MCP tools 是正常設計；其他命令透過 discovery/call 動態使用。

第一次真正啟動可能下載 `all-MiniLM-L6-v2` 並在使用者目錄建立搜尋索引，因此會比後續啟動慢。

## 停用與復原

只移除 project configs 的 Archicad entry：

```bash
./scripts/setup-archicad-mcp.sh --disable-project
```

```powershell
powershell -ExecutionPolicy Bypass -File scripts/setup-archicad-mcp.ps1 -DisableProject
```

同時移除使用者層級設定：

```bash
./scripts/setup-archicad-mcp.sh --disable-project --remove-user --client all
```

停用流程不需要 `uv`，也不會移除或修改 `revit-mcp`。

## Skill 使用方式

- 安裝與環境問題：`setup-archicad-mcp`
- 把既有 BIM_MCP workflow 轉給 Archicad：`archicad-skill-adapter`
- 詳細名詞對照：`.claude/skills/archicad-skill-adapter/references/revit-archicad-terminology.md`
- 目前 Skill 的可攜性狀態與 live-test trace：[Revit／Archicad Skill 可攜性矩陣](archicad-skill-portability.md)
- 第一批 pilot：`element-query`、`room-numbering`、`quantity-takeoff-excel`
- 下一批轉譯審核：[Wave 2 實作前審核清單](archicad-skill-translation-wave2.md)

adapter 只轉譯可驗證的 BIM 意圖，不保證每個 Revit tool 都有 Archicad 等價命令。找不到能力時必須回報缺口，不可猜測 API。

### 確認是否真的載入 Skill／Domain

Archicad MCP 能讀取或操作模型，只能證明 MCP capability 已連通。若要確認 BIM_MCP Skill 有參與，要求 Agent 在操作前後回報：

```text
backend
canonical_skill
domain_method
adapter_reference
project_port
discovered_commands
identifier_type
verification
unsupported_steps
```

完整測試 prompt 與判定方式見[可攜性矩陣](archicad-skill-portability.md#如何證明有用到-skill-與-domain)。

## 版本與授權

- Package：[`tapir-archicad-mcp==0.4.3`](https://pypi.org/project/tapir-archicad-mcp/)
- Upstream：<https://github.com/SzamosiMate/tapir-archicad-MCP>
- 宣告授權：MIT

本 repository 只在 runtime 解析固定套件，沒有 vendor 上游原始碼。升級版本時必須同步更新 setup script、Skill runtime contract、本文件與 `THIRD_PARTY_NOTICES.md`，並重新完成實機 smoke test。
