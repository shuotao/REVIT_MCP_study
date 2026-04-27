# 文檔目錄結構說明

## 目錄職責

| 目錄 | 用途 | 讀者 |
|------|------|------|
| **`docs/tools/`** | 工具 API 技術文檔 | 開發者 |
| **`docs/workflows/`** | 工作流程設計文檔 | 開發者 |
| **`domain/`** | 領域知識與工作流程 SOP | AI Agent |
| **`教材/`** | 教學講義、投影片、學習筆記 | 學生 / 老師 |
| **`.claude/commands/`** | 斜線命令定義（`/lessons`、`/domain`、`/qaqc` 等） | AI Agent + 貢獻者 |
| **`.claude/skills/`** | AI 技能編排（19 個 Skill，關鍵字觸發） | AI Agent |
| **`log/`** | 事件日誌流水帳（跨 AI 自動維護） | AI Agent + 維護者 |

---

## docs/tools/ - 技術文檔

**目的：** 記錄 MCP 工具的技術設計和 API 使用方式

**內容類型：**
- 工具設計規格
- API 參數說明
- 使用範例代碼

**目前檔案：**
- `override_element_color_design.md` - 元素圖形覆寫工具設計
- `override_graphics_examples.md` - 圖形覆寫 API 範例

---

## docs/workflows/ - 工作流程設計

**目的：** 記錄特定功能的開發設計過程與 Code Review

**目前檔案：**
- `corridor_code_review.md` - 走廊分析程式碼審查
- `corridor_dimension_review.md` - 走廊標註審查

---

## docs/ 根目錄 - 歷史紀錄

- `QUICK_TEST.md` - 外牆開口檢討功能測試文件
- `Recent_Update_Review.md` - GitHub PR/Issue 解析報告

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
| 工具的 API 設計和參數 | `docs/tools/` |
| 如何一步步執行某任務（給 AI） | `domain/` |
| 業務規則和法規注意事項 | `domain/` |
| 代碼範例和技術細節 | `docs/tools/` |
| 教學講義或學習筆記 | `教材/` |
| 新的斜線命令儀式 | `.claude/commands/` |
| 新的關鍵字自動觸發流程 | `.claude/skills/` |
