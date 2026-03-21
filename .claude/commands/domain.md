# /domain — SOP 轉換指令

將當前對話中成功的工作流程轉換為標準 SOP 格式，儲存至 `domain/` 目錄。

## 執行步驟

1. **確認對象**：與使用者確認要記錄的工作流程名稱
2. **提取內容**：從對話中萃取工具清單、執行步驟、注意事項
3. **撰寫格式**：使用以下 YAML frontmatter + Markdown 格式
4. **儲存檔案**：寫入 `domain/{workflow-name}.md`
5. **更新觸發表**：在 `CLAUDE.md` 的 Domain Knowledge 表格新增觸發關鍵字

## 輸出格式

```markdown
---
trigger: [關鍵字1, 關鍵字2]
tools: [tool_name_1, tool_name_2]
version: 1.0
---

# {工作流程名稱}

## 前提條件
...

## 步驟
1. ...
2. ...

## 注意事項
- ...
```

## 規則

- 儲存前先檢查 `domain/` 是否已有類似流程，避免重複
- 步驟必須可被其他 AI 直接執行，不得含有模糊描述

## 完成後：這份 Domain 需要變成 Skill 嗎？

**大多數情況下：不需要。**

Domain 和 Skill 是不同角色，不是不同等級：

| | Domain | Skill |
|---|--------|-------|
| 角色 | **知識**（法規、SOP、步驟） | **編排**（何時觸發、用什麼順序呼叫哪些工具） |
| 載入方式 | 被 Skill 引用時才讀取 | metadata 永遠常駐在 system prompt |
| 比喻 | 食譜內頁的步驟說明 | 食譜目錄的索引標籤 |

**Domain 不是 Skill 的半成品。** 一份好的 Domain 被現有的 Skill 引用就夠了，不需要自己變成 Skill。

例如：`domain/corridor-analysis-protocol.md` 被 `/fire-safety-check` Skill 引用，
它不需要自己變成一個獨立的 `/corridor-analysis` Skill。

### 什麼時候才需要升級？

只有當這份 Domain **無法被任何現有 Skill 覆蓋**，而且同時滿足以下條件時，才考慮升級：

1. **獨立觸發需求**：使用者會直接用這個主題的關鍵字開啟工作，而不是作為其他流程的子步驟
2. **特定工具編排**：涉及 3 個以上 MCP Tools 的特定調用順序，且這個順序不在任何現有 Skill 裡
3. **重複使用**：同樣的流程在不同對話中被重複使用 3 次以上

如果你不確定，**預設答案是「不升級」**。Domain 作為知識被引用就已經在發揮作用了。

### 如果確定要升級

使用 `skill-creator` plugin，並提供 `domain/skill-authoring-standard.md` 作為專案規範參照。
