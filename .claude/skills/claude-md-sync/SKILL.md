---
name: claude-md-sync
description: "CLAUDE.md 雙向同步驗證：當合併外部 PR、Domain 升級 Skill、或 Tools 異動後，執行正向（作業→CLAUDE.md）與反向（CLAUDE.md→作業）的比對與修正迴圈。觸發條件：claude.md 同步、sync、驗證一致性、merge 後檢查、skill 新增後。"
---

# CLAUDE.md 雙向同步驗證

## 何時執行

本流程在以下三種事件完成後**必須執行**（由 PostToolUse hook 自動觸發）：

1. **合併外部 PR**：`git merge` / `git pull` / `git rebase`
2. **Domain 升級 Skill**：新建或修改 `.claude/skills/*/SKILL.md`
3. **Tools 檢討**：修改 `MCP-Server/src/tools/*.ts` 或 `MCP/Core/CommandExecutor*.cs`

## 流程：雙向驗證迴圈

### Phase 1：正向驗證（作業結果 → CLAUDE.md）

讀取 CLAUDE.md，逐項檢查：

| # | 檢查項目 | 怎麼驗證 |
|---|---------|---------|
| 1 | Skills 表格數量和名稱 | `ls .claude/skills/*/SKILL.md | wc -l` 比對 CLAUDE.md 裡的 `Skills（N 個）` |
| 2 | Domain 觸發關鍵字表 | 比對每個 SKILL.md 的 description 與 CLAUDE.md 的 Domain 表格，確認無重複或遺漏 |
| 3 | 工具數量 | `grep -c '"name":' MCP-Server/src/tools/*.ts` 比對 CLAUDE.md 裡的數字 |
| 4 | 檔案路徑引用 | grep 出 CLAUDE.md 裡所有 backtick 包裹的路徑，逐一確認檔案存在 |
| 5 | 架構描述 | 確認 Architecture 段落的元件名稱和通訊方式與 codebase 一致 |

**如果有不一致 → 修正 CLAUDE.md → 進入 Phase 2**
**如果全部一致 → 跳到 Phase 3**

### Phase 2：反向驗證（修正後的 CLAUDE.md → 回頭檢證作業結果）

修正 CLAUDE.md 後，回頭檢查：

| # | 檢查項目 | 怎麼驗證 |
|---|---------|---------|
| 6 | 修正後的 CLAUDE.md 是否與作業結果矛盾 | 重新讀取修正後的 CLAUDE.md，比對剛才的操作是否違反任何新規則 |
| 7 | Deployment Rules 是否阻止了剛才的操作 | 檢查 DO NOT 清單，確認沒有誤殺合法操作 |
| 8 | Build/Deploy 流程是否需要更新 | 如果修正了路徑或版本號，確認 build 指令仍可執行 |

**如果有衝突 → 修正 → 回到 Phase 1 重新驗證**
**如果無衝突 → 進入 Phase 3**

### Phase 3：確認完成

輸出驗證報告：

```
┌─────────────────────────────────────┐
│   CLAUDE.md 同步驗證報告             │
├─────────────────────────────────────┤
│  觸發事件：[事件名稱]                │
│  驗證迴圈：N 次                      │
├─────────────────────────────────────┤
│  正向檢查：                          │
│  [1] Skills 數量    ✓ / ✗ (修正)    │
│  [2] Domain 表格    ✓ / ✗ (修正)    │
│  [3] 工具數量       ✓ / ✗ (修正)    │
│  [4] 路徑引用       ✓ / ✗ (修正)    │
│  [5] 架構描述       ✓ / ✗ (修正)    │
├─────────────────────────────────────┤
│  反向檢查：                          │
│  [6] 規則矛盾       ✓ / ✗ (修正)   │
│  [7] DO NOT 誤殺    ✓ / ✗ (修正)   │
│  [8] Build 流程      ✓ / ✗ (修正)  │
├─────────────────────────────────────┤
│  結果：✓ 雙向一致 / ✗ 需人工介入    │
└─────────────────────────────────────┘
```

## 重要原則

- **不要只提醒，要執行**：偵測到觸發事件後，直接開始驗證，不要問「要不要執行」
- **迴圈直到一致**：正向修正 → 反向發現衝突 → 再修正 → 再驗證，直到兩邊完全一致
- **修正 CLAUDE.md 時遵守 200 行上限**：如果修正導致超過 200 行，考慮將內容移至 `.claude/rules/` 或刪除已被 Skill 覆蓋的重複資訊
- **記錄修正內容**：每次修正後用 git commit 記錄，commit message 格式：`chore: CLAUDE.md sync after [事件類型]`
