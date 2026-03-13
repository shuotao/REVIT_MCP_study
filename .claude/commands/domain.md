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
