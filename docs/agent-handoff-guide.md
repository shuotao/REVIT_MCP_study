# Agent Handoff Guide — Code Review & 接續作業指引

> **產出日期**：2026-04-16
> **產出 Agent**：Claude Code (Opus 4.6)
> **目的**：供其他 GenAI Agent（Claude Code / Gemini CLI / VS Code Copilot）接續執行 code review 或未完成作業

---

## 一、當前狀態快照（2026-04-16）

### 已完成
| 項目 | 狀態 | 備註 |
|------|------|------|
| PR #34 bug fix | ✅ 已合併 | squash merge，修正 Logger.Instance → Logger.Debug |
| PR #30 rebase 通知 | ✅ 已留言 | 請作者解 4 個檔案衝突 + 統一 Logger.Error |
| PR #26 rebase 請求 | ✅ 已留言 | 期限 4/23，逾期維護者代處理 |
| Issue #33 條件式接受 | ✅ 已留言 | 需多版本驗證 + Deployment Rules 確認 |
| Issue #31 整合 parking-check | ✅ Domain + Skill 已更新 | 待 commit |
| 開會討論策略文件 | ✅ 已撰寫 | docs/meeting-strategy-pr30-pr32.md |

### 待處理
| 項目 | 狀態 | 接續方式 |
|------|------|----------|
| PR #30 code review | ⏳ 等作者 rebase + 開會決定拆分策略 | 見下方 Review 指引 |
| PR #32 code review | ⏳ 等開會 + 多版本驗證 | 見下方 Review 指引 |
| PR #26 code review | ⏳ 等作者 rebase 解衝突 | 衝突解決後可 review |
| 本地變更 commit | ⏳ 需 commit | parking-check 更新 + docs |
| CLAUDE.md tool module 數量修正 | ⏳ 小修 | 13→14（加入 stair-compliance-tools） |

---

## 二、各 PR Code Review 指引

### PR #30 Review Checklist（待作者 rebase 後使用）

```bash
# 1. 拉取最新 PR 分支
git fetch origin pull/30/head:pr-30
git checkout pr-30
```

**C# 核心審查（高優先）**：
- [ ] `MCP/Core/CommandExecutor.cs` — 新增的 case 是否遵循現有 switch pattern
- [ ] `MCP/Core/Commands/CommandExecutor.Legend.cs` — 新檔案，+1020 行，重點審查：
  - Transaction 使用是否正確（Start/Commit/Rollback）
  - 是否使用 ExternalEventManager 確保 UI thread
  - ClosedXML 呼叫是否有 try-catch
- [ ] `MCP/Core/Commands/CommandExecutor.TextNote.cs` — 新檔案，+168 行
- [ ] `MCP/Core/Commands/CommandExecutor.Sheet.cs` — +733 行新增邏輯
- [ ] `MCP/Core/Commands/CommandExecutor.SmokeExhaust.cs` — 確認修改不影響現有排煙功能
- [ ] `MCP/RevitMCP.csproj` — ClosedXML 依賴是否正確宣告

**TypeScript 工具審查（中優先）**：
- [ ] `MCP-Server/src/tools/legend-tools.ts` — inputSchema 型別是否正確
- [ ] `MCP-Server/src/tools/sheet-tools.ts` — 新增工具的 description 是否清晰
- [ ] `MCP-Server/src/tools/index.ts` — 新模組是否正確 import 和 concat

**配置與文件審查（低優先但敏感）**：
- [ ] `.claude/settings.json` — hook 註冊是否合理
- [ ] `.claude/hooks/preload-revit-tools.sh` — 腳本邏輯是否安全、效能影響
- [ ] `CLAUDE.md` — Skills/Tools 數量更新是否正確
- [ ] `issues/` 目錄 — 建議移除，改用 GitHub Issues

**安全性審查**：
- [ ] ClosedXML 讀取外部 Excel — 是否有路徑遍歷風險
- [ ] TextNote 批次操作 — 是否有未限制的迴圈（DoS 風險）

### PR #32 Review Checklist（待多版本驗證後使用）

```bash
git fetch origin pull/32/head:pr-32
git checkout pr-32
```

**架構審查（關鍵）**：
- [ ] `MCP/Application.cs` — OnStartup 改動是否有條件編譯保護
- [ ] `MCP/Core/CoreRuntimeManager.cs` — +190 行，核心邏輯：
  - AssemblyLoadContext 的 Unload 是否正確釋放資源
  - Shadow-copy 路徑是否安全（不在系統目錄）
  - 異常處理是否完善（Core 載入失敗不能 crash Revit）
- [ ] `MCP/Core/CoreLoadContext.cs` — Assembly 解析邏輯
- [ ] `MCP.Contracts/IRevitMcpRuntime.cs` — 介面設計是否足夠穩定（一旦定型很難改）

**部署審查**：
- [ ] `scripts/install-addon.ps1` — 改動是否向後相容
- [ ] `MCP/RevitMCP.csproj` — 子專案引用方式
- [ ] `MCP.Contracts/MCP.Contracts.csproj` — Target framework 設定
- [ ] `MCP.CoreRuntime/MCP.CoreRuntime.csproj` — Target framework + Revit API 引用

**跨版本驗證（必須在合併前完成）**：
- [ ] R26 (.NET 8) — Core reload 成功
- [ ] R25 (.NET 8) — Core reload 成功
- [ ] R24 (.NET Framework 4.8) — 確認 graceful fallback（不 crash）
- [ ] R22 (.NET Framework 4.8) — 確認 graceful fallback（不 crash）

### PR #26 Review Checklist（待作者 rebase 後使用）

```bash
git fetch origin pull/26/head:pr-26
git checkout pr-26   # 注意：headRefName 是 'master'
```

**MCP 整合審查**：
- [ ] `MCP/Core/CommandExecutor.cs` — batch_set_marks case 實作
- [ ] `MCP-Server/src/tools/marking-tools.ts` — inputSchema 定義

**pyRevit 審查（新方向，需額外關注）**：
- [ ] `pyRevit_Tools/` — 目錄結構是否符合 pyRevit extension 標準
- [ ] `script.py` — IronPython 語法相容性
- [ ] `.rar` 檔案 — 二進位檔案不應直接放在 repo 中，建議 .gitignore 或解壓

**文件審查**：
- [ ] `docs/lessons/` — 內容是否應整合至 `domain/lessons.md`

---

## 三、自檢報告

### 本次操作影響分析

| 操作 | 影響範圍 | CLAUDE.md 影響 | 風險 |
|------|----------|----------------|------|
| PR #34 merge | 4 個 C# 檔案 Logger 修正 | 無 | 極低 |
| parking-clearance-check.md 更新 | Domain 內容更新 | 無（數量不變） | 低 |
| parking-check SKILL.md 更新 | Skill 步驟調整 | 無（數量不變） | 低 |
| meeting-strategy 文件新增 | docs/ 新增 | 無 | 無 |
| agent-handoff-guide 新增 | docs/ 新增 | 無 | 無 |

### 已知待修正項（Pre-existing）
1. **CLAUDE.md tool module 數量**：記載「13 個模組」，實際 14 個（stair-compliance-tools.ts 未計入）。建議下次 CLAUDE.md sync 時一併修正。

### 驗證完整性
- [x] git pull 後 main 分支乾淨
- [x] Skills 數量 18 = CLAUDE.md 記載
- [x] Domain 數量 31 = CLAUDE.md 記載
- [x] 未引入新的安全風險
- [x] 未違反 Deployment Rules
- [x] 所有 GitHub 操作有留言記錄可追溯

---

## 四、快速接續命令

任何 Agent 想接續這個工作，可以直接執行：

```bash
# 1. 確認當前狀態
git status && gh pr list --state open

# 2. 檢查 PR 作者是否已回應
gh pr view 30 --json comments --jq '.comments[-1]'
gh pr view 26 --json comments --jq '.comments[-1]'
gh issue view 33 --json comments --jq '.comments[-1]'

# 3. 如果 PR 已 rebase，開始 code review
gh pr diff 30 --patch | head -100  # 確認是否已 rebase
gh pr diff 26 --patch | head -100

# 4. 讀取開會策略文件
cat docs/meeting-strategy-pr30-pr32.md

# 5. 讀取本指引
cat docs/agent-handoff-guide.md
```
