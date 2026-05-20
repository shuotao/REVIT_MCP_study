# 文檔目錄結構說明

## 目錄職責

| 目錄 | 用途 | 讀者 |
|------|------|------|
| **`docs/BIM_MCP/`** | 公開知識站（10 頁：架構、22 命題、決策框架、Skill/Domain 索引） | 任何人 |
| **`docs/_archive/`** | 開發歷程歸檔（design notes / bug post-mortems / 舊 handoff） | 維護者 |
| **`domain/`** | 領域知識與工作流程 SOP | AI Agent |
| **`教材/`** | 教學講義、投影片、學習筆記 | 學生 / 老師 |
| **`.claude/commands/`** | 斜線命令定義（`/lessons`、`/domain`、`/qaqc` 等） | AI Agent + 貢獻者 |
| **`.claude/skills/`** | AI 技能編排（19 個 Skill，關鍵字觸發） | AI Agent |
| **`log/`** | 事件日誌流水帳（跨 AI 自動維護） | AI Agent + 維護者 |

---

## docs/BIM_MCP/ - 公開知識站

**目的：** 5/23 demo 後沉澱的 MCP × BIM 公開知識門戶

**結構：**
- `index.html` — 入口
- `reference/` — 10 頁長期參考（架構、22 命題、三憲法、Skill/Domain 索引、部署、troubleshoot）
- `2026-MM/` — 月度封存（含當月簡報與 hands-on 教材）
- `_images/` — 25 SVG 視覺資產（極簡風）

公開 URL：<https://shuotao.github.io/REVIT_MCP_study/docs/BIM_MCP/index.html>

---

## docs/_archive/ - 開發歷程歸檔

**目的：** 已被 canonical 版本（`domain/` / `BIM_MCP/`）取代或一次性的開發紀錄

**目前內容（2026-q2/）：**
- 個人筆記與舊 PR 討論（jacky820507、0328 課程、meeting-strategy-pr30-pr32）
- 過時 handoff（handoff-pr-chiminlu、bim_mcp_handoff_codex）
- dev-notes/ — 工具 API 設計稿 + 走廊功能 code review/post-mortem（已被 `domain/corridor-analysis-protocol.md` 與 `domain/element-coloring-workflow.md` 取代）

---

## domain/ - 領域知識

**目的：** 給 AI 讀取的工作流程和業務知識

**內容類型：**
- 操作工作流程 SOP
- 業務規則與法規參考
- 品質檢查清單

**完整清單：** 請參考 `domain/README.md`

---

## 教材/ - 教學資源

**目的：** 24 小時深度課程的講義與學習材料

**內容類型：**
- 堂次講義（01~08）
- 投影片與圖片
- Skill 學習筆記與範例解說

**完整清單：** 請參考 `教材/README.md`

---

## .claude/ - AI 自動化（Claude Code / Gemini CLI）

**目的：** 提供 AI Agent 的可執行規則與自動化機制，對應 Karpathy「LLM Wiki」pattern 中的 Schema 操作層。

**子目錄職責：**

| 子目錄 | 用途 | 觸發方式 |
|--------|------|---------|
| `.claude/commands/` | 斜線命令定義（`/lessons`、`/domain`、`/qaqc`、`/review`、`/dev-guide`） | 使用者**手動**打斜線觸發 |
| `.claude/skills/` | AI 技能編排（19 個 Skill，例：`fire-safety-check`、`smoke-exhaust`） | 關鍵字**自動**觸發 |
| `.claude/hooks/` | 自動化守衛（例：偵測 `git merge` 後自動提示 CLAUDE.md 同步驗證） | 事件**自動**觸發 |

**與 `domain/` 的關係：**

- `domain/*.md` = **知識內容**（法規、SOP、步驟），被引用時才讀取
- `.claude/skills/*/SKILL.md` = **編排規則**（何時觸發、什麼順序呼叫哪些工具）
- `.claude/commands/*.md` = **手動儀式**（使用者主動呼叫的工作流程）

詳見 `CLAUDE.md` 的「Domain vs Skill 架構原則」段落。

---

## log/ - 事件日誌（Karpathy LLM Wiki pattern）

**目的：** 補 `git log` 和 `domain/lessons.md` 之間的空洞——紀錄「什麼時候做了什麼事」。

**維護機制（三層並行）：**

- **Layer 1**：`scripts/git-hooks/post-commit` 自動 append（AI-agnostic，跨 AI 保底）
- **Layer 2**：`CLAUDE.md` 的 Logging Protocol 要求 AI 執行重要命令後主動記錄
- **Layer 3**：`.claude/hooks/` 可擴充細粒度記錄（選配，目前未啟用）

**AI 啟動時應讀取最新月份檔的末尾 ~60 行**（Session Start Protocol），以延續工作脈絡。

**檔案結構：** 按月切檔 `log/YYYY-MM.md`，append-only，嚴禁修改已有條目。

**安裝 git hook：** 執行 `./scripts/install-log-hooks.sh`（Mac/Linux）或 `.\scripts\install-log-hooks.ps1`（Windows）。

詳見 `log/README.md`。

---

## 新增文檔時的選擇

| 如果要記錄... | 放在... |
|--------------|--------|
| 工具的 API 設計和參數 | TSDoc 註解在 `MCP-Server/src/tools/*.ts`（就近源碼）|
| 如何一步步執行某任務（給 AI） | `domain/` |
| 業務規則和法規注意事項 | `domain/` |
| 公開知識站新頁 | `docs/BIM_MCP/reference/` |
| 月度簡報 / hands-on 教材 | `docs/BIM_MCP/YYYY-MM/` |
| 教學講義或學習筆記 | `教材/` |
| 新的斜線命令儀式 | `.claude/commands/` |
| 新的關鍵字自動觸發流程 | `.claude/skills/` |
| 已棄用 / 一次性開發紀錄 | `docs/_archive/YYYY-qN/` |
