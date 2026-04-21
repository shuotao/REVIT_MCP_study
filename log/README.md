# log/ — 事件日誌

此目錄存放專案事件的流水帳，由 **git hooks** 和 **AI Agent** 共同自動維護。

## 為什麼需要？

配合 [Karpathy 的 LLM Wiki pattern](https://gist.github.com/karpathy/442a6bf555914893e9891c11519de94f)，補 `git log` 和 `domain/lessons.md` 之間的空洞：

- **git log** 粒度太粗（一個 commit 常包含多件事，且只有檔案異動）
- **domain/lessons.md** 粒度太高（只記已提煉的高階規則，不記過程）
- **log/** 補中間層——**紀錄「什麼時候做了什麼事」**（事件流水帳）

## 檔案結構

按月切檔避免單檔爆炸：

```
log/
├── README.md           # 本檔
├── 2026-04.md          # 2026 年 4 月事件
├── 2026-05.md          # 2026 年 5 月事件
└── ...
```

每份檔案按時間 append 事件，**嚴禁修改已有條目**。

## 三層維護機制

| 層級 | 觸發方式 | 依賴 | 角色 |
|------|---------|------|------|
| **Layer 1**：`scripts/git-hooks/post-commit` | git commit 時自動 fire | 無（git 原生事件）| **保底層**，任何 AI 或人 commit 都會記錄 |
| **Layer 2**：`CLAUDE.md` 的 Logging Protocol | AI 讀憲法後主動遵守 | 依 AI 遵守規範（透過 GEMINI.md / AGENTS.md redirect 達成跨 AI 通用）| **規則層**，細粒度事件補充 |
| **Layer 3**：`.claude/hooks/` 擴充（選配） | Claude Code harness 事件 | 僅 Claude Code | **細節層**，非必要 |

**設計原則**：Layer 1 永不缺席。任一層壞了都有其他層兜底。

## 事件格式

```markdown
## [YYYY-MM-DD HH:MM] {event-type} | {short-description}
- actor: {model-id} (via {client-name})
- files: {comma-separated list}
- trigger: {git-hook | claude-hook | manual}
- sha: {git-sha}  # 若為 git 事件
- summary: {one-liner}
```

### 事件類型

| 類型 | 觸發條件 |
|------|---------|
| `commit` | 一般 git commit |
| `merge` | 合併 commit |
| `lessons` | 執行 `/lessons` 命令 |
| `domain` | 執行 `/domain` 命令 |
| `qaqc` | 執行 `/qaqc` 命令 |
| `review` | 執行 `/review` 命令 |
| `skill-change` | `.claude/skills/` 被編輯 |
| `tool-change` | `MCP-Server/src/tools/` 或 `CommandExecutor` 被編輯 |

## 安裝

初次 clone 後執行一次：

```bash
# Mac / Linux
./scripts/install-log-hooks.sh

# Windows
.\scripts\install-log-hooks.ps1
```

這會設定 `git config core.hooksPath scripts/git-hooks`，讓 hooks 跟著 repo 走（不需每人各自 copy）。

## 提交 log 檔案

每次 commit 後，hook 會 append 新條目到 `log/YYYY-MM.md`，這份檔會變成「未暫存變更」。建議處理方式：

- **順手 commit**：`git add log/ && git commit -m "log: sync events"`
- **累積到下次 commit 一起帶上**（常見做法）

## 隱私規則

log **只記 metadata**，嚴禁記：
- 使用者原始訊息
- AI 完整輸出
- 程式碼片段
- API Keys / 認證資訊

**只記**：檔名、時間、事件類型、一行摘要。
