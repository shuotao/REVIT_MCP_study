# /qaqc — 全面品質驗證流程

你是 Revit MCP 專案的 QA/QC 執行者。當使用者呼叫 `/qaqc` 時，執行以下完整流程。

## 環境判斷

首先判斷目前執行環境：

### 在 Windows 上
1. 直接執行 `scripts/verify-qaqc.ps1`
2. 讀取腳本輸出，針對每個 FAIL 項目提出修復建議
3. 如果全部 PASS，回報「QA/QC 通過」

### 不在 Windows 上（macOS / Linux）
1. 告知使用者：**以下檢查項目中，標記 🖥️ 的項目必須在 Windows 機器上執行**
2. 執行所有可在本機完成的靜態檢查（Phase 1–3）
3. 輸出一份待辦清單，讓使用者帶到 Windows 上完成

---

## Phase 1：檔案結構完整性（任何 OS）

逐一檢查以下項目，每項標記 PASS / FAIL：

### 1-1. 禁止存在的檔案（違反統一架構）
- `MCP/RevitMCP.2024.csproj` → 不得存在
- `MCP/RevitMCP.2024.addin` → 不得存在
- `MCP/MCP/` 目錄 → 不得存在
- `scripts/fix_addin_path.ps1` → 不得存在

### 1-2. 必須存在的檔案
- `MCP/RevitMCP.csproj` → 統一建構檔
- `MCP/RevitMCP.addin` → 統一 addin 設定
- `MCP/Application.cs` → Add-in 進入點
- `MCP/Core/CommandExecutor.cs` → 命令分派器
- `MCP/Core/SocketService.cs` → WebSocket 服務
- `MCP/Core/ExternalEventManager.cs` → UI 執行緒管理
- `MCP/Core/RevitCompatibility.cs` → 跨版本相容層
- `MCP-Server/src/index.ts` → MCP Server 進入點
- `MCP-Server/src/tools/revit-tools.ts` → 工具定義
- `MCP-Server/package.json` → Node.js 依賴

### 1-3. AI 規範文件一致性
- `CLAUDE.md` → 必須存在且超過 100 行（主規範）
- `GEMINI.md` → 內容必須僅為 `CLAUDE.md`（一行）
- `AGENTS.md` → 內容必須僅為 `CLAUDE.md`（一行）

---

## Phase 2：交叉參照一致性（任何 OS）

掃描所有 `.md` 檔案，檢查以下模式：

### 2-1. 過時引用掃描
搜尋以下 pattern，排除 `CHANGELOG.md` 和 `docs/MIGRATION_GUIDE.md`（歷史文件）：
- `RevitMCP.2024.csproj` — 已刪除的舊建構檔
- `RevitMCP.2024.addin` — 已刪除的舊 addin
- `bin\Release.2024\` 或 `bin/Release.2024/` — 舊輸出路徑
- `MCP\MCP\` 或 `MCP/MCP/` — 舊巢狀目錄
- `fix_addin_path` — 已刪除的危險腳本
- `ARCHITECTURE.md` — 已合併到 README.md

例外：CLAUDE.md 中的 "DO NOT" 規則引用舊檔名作為禁止範例，這是正確的。

### 2-2. 文件導覽完整性
檢查 `README.md` 和 `README.en.md` 的文件導覽表：
- 必須包含 CLAUDE.md、GEMINI.md、AGENTS.md
- 必須包含 domain/lessons.md
- 必須包含 .claude/skills/ 和 .claude/commands/
- GEMINI.md 的說明必須標示為「重定向」

---

## Phase 3：建構設定驗證（任何 OS）

### 3-1. csproj 設定
讀取 `MCP/RevitMCP.csproj`，確認：
- 包含 `Nice3point.Revit.Sdk` 套件引用
- 包含 `<DeployAddin>false</DeployAddin>`
- Configurations 包含：`Release.R22;Release.R23;Release.R24;Release.R25;Release.R26`

### 3-2. addin 設定
讀取 `MCP/RevitMCP.addin`，確認：
- Assembly 路徑為相對路徑（`RevitMCP.dll`），不含絕對路徑
- 只有一個 `<AddInId>`，且為有效 GUID
- FullClassName 為 `RevitMCP.Application`

### 3-3. MCP Server 設定
讀取 `MCP-Server/package.json`，確認：
- 有 `build` script
- 有 `@anthropic-ai/sdk` 或 `@modelcontextprotocol/sdk` 依賴

---

## Phase 4：建構驗證 🖥️ （僅限 Windows）

### 4-1. C# 多版本建構
依序執行以下命令，每個都必須成功（exit code 0）：
```powershell
cd MCP
dotnet build -c Release.R22 RevitMCP.csproj
dotnet build -c Release.R24 RevitMCP.csproj
dotnet build -c Release.R25 RevitMCP.csproj
dotnet build -c Release.R26 RevitMCP.csproj
```
每次建構後確認 `MCP\bin\Release\RevitMCP.dll` 存在且大小 > 0。

### 4-2. MCP Server 建構
```powershell
cd MCP-Server
npm install
npm run build
```
確認 `MCP-Server/build/index.js` 存在。

### 4-3. 安裝腳本測試
```powershell
.\scripts\verify-installation.ps1
```
所有 Check 必須為 PASS。

---

## Phase 5：部署驗證 🖥️ （僅限 Windows + Revit）

### 5-1. Addin 部署位置
檢查 `%APPDATA%\Autodesk\Revit\Addins\` 下已安裝的版本：
- 每個版本目錄下，最多只能有 **1 個** RevitMCP 相關的 `.addin` 檔案
- `.addin` 中的 `<Assembly>` 路徑必須指向存在的 DLL

### 5-2. 重複 addin 檢測
掃描所有 Revit 版本的 Addins 目錄，搜尋 `*RevitMCP*` 或 `*revit-mcp*`，同一版本出現 2 個以上即為 FAIL。

### 5-3. WebSocket 連線測試（需要啟動 Revit）
1. 開啟 Revit
2. 點擊 MCP Tools → MCP 服務 (開/關)
3. 確認日誌顯示 `WebSocket 伺服器已啟動，監聽: localhost:8964`
4. 從另一個終端執行：
```powershell
# 簡易連線測試
node -e "const ws = new (require('ws'))('ws://localhost:8964'); ws.on('open', () => { console.log('PASS: WebSocket connected'); ws.close(); }); ws.on('error', (e) => { console.log('FAIL:', e.message); });"
```

---

## Phase 6：內容品質 Lint（任何 OS）

驗證 `domain/*.md` 的 frontmatter 完整性與交叉引用一致性。規範詳見 `domain/frontmatter-standard.md`。

### 掃描範圍
- 所有 `domain/*.md`
- **排除**：`domain/README.md`、`domain/frontmatter-standard.md`（meta 文件）

### 6-1 Frontmatter 存在性
每份檔案必須以 `---\n` 開頭（YAML frontmatter 分隔符）。
> 若否 → 執行 `python3 scripts/backfill-domain-metadata.py` 可自動補齊。

### 6-2 必填欄位完整性
Frontmatter 必須包含：
- `name`（對應檔名，不含副檔名）
- `description`（1-1024 字元，非空）

### 6-3 metadata 完整性
`metadata:` nested map 必須包含：
- `metadata.version`
- `metadata.updated`

### 6-4 `related` 指向驗證
`metadata.related` 列出的每個檔名，必須在 `domain/` 真實存在。

### 6-5 `referenced_by` 反向驗證
`metadata.referenced_by` 列出的每個 skill 名，對應的 `.claude/skills/{name}/SKILL.md` 必須存在，且其 `## Reference` 段落必須引用本 domain。

### 6-6 Staleness 警告（informational）
`metadata.updated` 距今 > 12 個月 → 在報告中標記為 ⚠️ 警告（**非 FAIL**，僅提醒月小聚時檢視是否需翻修）。

### 執行方式
- 手動：AI 執行本指令時，Read 每個 domain/*.md 的 frontmatter 部分（前 ~30 行），逐項核對
- 自動：未來可擴充為獨立 script（本 MVP 暫不做）

### 失敗類型
- **6-1 ~ 6-3 失敗** → FAIL（需要立即補 frontmatter）
- **6-4 ~ 6-5 失敗** → FAIL（交叉引用壞掉，資料一致性問題）
- **6-6 觸發** → ⚠️ WARN（不是 FAIL，但建議月小聚審視）

---

## 報告輸出格式

完成所有檢查後，輸出結構化報告：

```
╔══════════════════════════════════════════════╗
║        Revit MCP QA/QC Report               ║
║        Date: YYYY-MM-DD                     ║
║        Environment: Windows / macOS         ║
╠══════════════════════════════════════════════╣
║  Phase 1 — 檔案結構    : ✅ X/X PASS       ║
║  Phase 2 — 交叉參照    : ✅ X/X PASS       ║
║  Phase 3 — 建構設定    : ✅ X/X PASS       ║
║  Phase 4 — 建構驗證    : ⏳ 需要 Windows   ║
║  Phase 5 — 部署驗證    : ⏳ 需要 Windows   ║
║  Phase 6 — 內容品質    : ⚠️ A/B PASS (C⚠️) ║
╠══════════════════════════════════════════════╣
║  Total: XX/XX PASS | X FAIL | X PENDING     ║
╚══════════════════════════════════════════════╝
```

Phase 6 欄位說明：A = 通過 6-1 ~ 6-5 的檔數、B = 總檔數、C = 觸發 staleness 警告的檔數。

如有 FAIL 項目，每個都必須附上：
1. 失敗的具體檢查項
2. 預期值 vs 實際值
3. 建議修復步驟
