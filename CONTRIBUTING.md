# 貢獻指南

感謝您參與這個專案！這份指南說明如何貢獻知識和經驗。

## 🎯 您可以貢獻什麼？

| 類型 | 目錄/檔案 | 說明 |
|:----|:---------|:----|
| ✅ **工作流程 SOP** | `domain/*.md` | 您驗證成功的工作流程 |
| ✅ **經驗規則** | `domain/lessons.md` | 高階規則和避坑經驗（Append-only） |
| ❌ 程式碼 | `MCP/`、`MCP-Server/src/` | 請勿修改（由維護者管理） |
| ❌ AI 規範文件 | `CLAUDE.md`、`GEMINI.md`、`AGENTS.md` | 請勿修改（由維護者管理） |

## 📝 貢獻步驟

### 1. Fork 並 Clone

```bash
# 在 GitHub 上 Fork 這個專案
# 然後 Clone 到本地
git clone https://github.com/你的帳號/REVIT_MCP_study.git
```

### 2. 建立新分支

```bash
git checkout -b add/我的新workflow
```

### 3. 使用 AI 幫你產生貢獻內容

```
/domain 我剛才做的 XXX 流程很成功，請幫我產生 SOP
```

或

```
/lessons 我發現了一個重要規則：XXX，請記錄到 domain/lessons.md
```

### 4. 提交變更

```bash
git add domain/my-new-workflow.md
# 或
git add domain/lessons.md

git commit -m "新增: XXX 工作流程"
git push origin add/我的新workflow
```

### 5. 發送 Pull Request

- 到 GitHub 上發送 PR
- 標題格式：`[Domain] 新增 XXX 工作流程` 或 `[Lessons] 新增 XXX 規則`
- 說明您的貢獻解決了什麼問題

## ⚠️ 重要提醒

1. **只提交知識內容**：不要修改程式碼（`MCP/`、`MCP-Server/src/`）
2. **遵循格式**：domain 檔案必須有 YAML frontmatter
3. **測試過才提交**：確保您的工作流程實際可用
4. **避免重複**：先查看現有的 domain 檔案

## 📬 聯絡維護者

如果您有程式碼相關的建議，請開 Issue 討論，不要直接提交程式碼 PR。
