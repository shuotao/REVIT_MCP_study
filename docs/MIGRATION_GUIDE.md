# 🔄 遷移指南 (Migration Guide)

> 本檔案記錄影響現有使用者 / fork 貢獻者的重大升級，依時間新到舊排列。每個段落各自獨立、標明適用對象與升級日期，方便直接跳讀。

---

## 🔀 MCP 2026-07-28 雙時代相容升級（Dual-Era Additive Upgrade）

> **適用對象**：所有現有 Revit MCP 使用者（stdio 連線）與 fork 貢獻者
> **升級日期**：2026-07-28
> **升級性質**：純加法（additive）、向下相容（backward-compatible）— 一般使用者不需要任何動作

MCP 官方協定公告了 2026-07-28 版的變更。本專案採取「雙時代並存」（dual-era）策略：**先採用可以獨立生效、對舊客戶端無害的 metadata 層變更，把真正改變連線方式（wire-level）的部分延後**，直到官方 SDK 正式發布支援該協定版本為止。實作細節與驗證紀錄見 `docs/mcp2026-upgrade/impl-log.md`。

### 1️⃣ 加法式升級、完全向下相容（無需任何動作）

- stdio 連線方式維持不變。
- 既有的 Revit 外掛（`MCP/`，`SocketService.cs` 提供的 `localhost:8964` WebSocket 服務）完全不受影響，不需要重新編譯或重新部署。
- **使用者不需要採取任何行動**：舊版 AI 客戶端、舊版 Revit 外掛與新版 MCP Server 可以直接互通，不會因這次升級而斷線或失效。

### 2️⃣ 工具新增 `title` + `readOnlyHint` / `destructiveHint`（僅 metadata，舊客戶端會忽略）

所有透過 `registerRevitTools()`（`MCP-Server/src/tools/index.ts`）註冊的工具，現在額外帶上：

- `title`：人類可讀的工具標題。
- `readOnlyHint` / `destructiveHint`：布林值提示，標示該工具是否唯讀、是否具破壞性（例如 `delete_element`、`dedup_detail_elements_in_view` 屬於 destructive 允許清單）。

這些欄位是**純 metadata**，不改變既有呼叫介面（工具名稱、input schema、執行結果都不變）。不認識這些欄位的舊版 MCP 客戶端會直接忽略，不會因此出錯。

### 3️⃣ `tools/list` 現在依名稱決定性排序（deterministic ordering）

`tools/list` 回傳的工具清單，現在會先依工具名稱做穩定排序（`localeCompare`）再回傳，確保同一份工具集在每次啟動、每次呼叫都得到相同順序，方便 diff、QA 與 AI 客戶端呈現穩定清單。此變更只影響清單順序，不影響工具本身的可用性或呼叫方式。

### 4️⃣ 協定層（wire-level）2026-07-28 新特性 — 全數延後（DEFERRED）

以下屬於 MCP 協定 2026-07-28 版中真正改變連線／協商方式的「wire-level」特性，本次**一律不實作、延後處理**：

- Stateless 連線模式
- `server/discover`
- `resultType`
- Tasks core（背景任務原生支援）
- HTTP / OAuth 傳輸與授權

**延後原因**：目前沒有官方 SDK 實作支援此協定版本；在官方 SDK 正式發布支援 2026-07-28 協定之前自行實作，等於是自行推測協定細節，日後極可能要重寫。

**追蹤機制**：是否已有官方 SDK 支援該協定版本，由**每日自動觀察（daily automated watcher）**持續追蹤，一旦條件成立即重新排入實作排程，不需使用者手動關注上游進度。

### 5️⃣ Fork 貢獻政策不變：不可手改維護者管理的程式碼檔、不開程式碼 PR

延續既有規則（見 `CONTRIBUTING.md`）：

- Fork 使用者**不可**手動修改由維護者管理的程式碼檔案，包含 `MCP-Server/src/`、`MCP/`。
- Fork PR **不應**開立程式碼變更的 PR；僅接受知識型貢獻，透過 `domain/*.md` 與 `.claude/skills/*/SKILL.md` 提交。
- 此限制由 CI（`check-pr.yml`）強制：偵測到 PR 來自 fork（`head.repo.fork == true`）時，只允許改動 `domain/`、`GEMINI.md` 等白名單路徑，其餘一律判定失敗。
- 維護者的同倉庫（same-repo）PR 不受此限制，可修改 `MCP-Server/`、`MCP/`、`scripts/`、`docs/`、`.github/`。

#### ❓ 常見問題

**Q: 我是現有使用者，需要重新安裝或重新設定嗎？**
A: 不需要。這是加法式升級，stdio 連線與既有 Revit 外掛都維持原樣可用。

**Q: 我的 AI 客戶端不認識新的 `title` / `readOnlyHint` / `destructiveHint` 欄位會出錯嗎？**
A: 不會。這些是選填 metadata，舊版客戶端會直接忽略。

**Q: `tools/list` 排序改變會不會影響我原本依索引呼叫工具的程式？**
A: 應一律依工具「名稱」呼叫，而非依清單索引；排序變更只影響顯示順序。

**Q: Tasks core / HTTP / OAuth 什麼時候會支援？**
A: 目前沒有固定時間表，取決於官方 SDK 何時發布支援 2026-07-28 協定版本；本專案以每日自動觀察追蹤上游進度，條件成立後才會排入實作。

**Q: 我是 fork 貢獻者，可以順手改一下 `MCP-Server/src/tools/` 嗎？**
A: 不行。Fork PR 僅接受 `domain/` 與 skills 的知識型貢獻；程式碼檔案由維護者管理，請勿手改，也不要開程式碼 PR。
