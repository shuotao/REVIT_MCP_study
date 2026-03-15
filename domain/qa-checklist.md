---
name: qa-checklist
description: 專案品質檢查流程。當用戶提到「QA」「檢查」「驗證」「確認一致性」時啟用。
tags: [QA, 品質, 檢查, 驗證]
---

# 專案品質檢查清單 (QA Checklist)

本文件定義專案的品質檢查標準和執行步驟。

---

## 何時執行 QA

- 提交 Pull Request 前
- 用戶要求「檢查」「驗證」「確認」時
- 新增或刪除檔案後
- 重構目錄結構後

---

## ✅ 檢查項目

### 1. Markdown 連結檢查

**目的**：確保所有 `[text](./file.md)` 連結的檔案都存在

**執行命令**：
```bash
grep -r "\.md)" *.md --include="*.md" | grep -o '\./[^)]*\.md' | sort -u
```

**驗證**：用 `ls` 確認每個檔案都存在

---

### 2. 路徑引用檢查

**目的**：確保所有 `domain/`、`docs/`、`scripts/`、`MCP-Server/` 引用的檔案都存在

**執行命令**：
```bash
# 搜尋 domain/ 引用
grep -r "domain/" --include="*.md" | grep -oE 'domain/[a-zA-Z0-9_/-]+\.md'

# 搜尋 scripts/ 引用
grep -r "scripts/" --include="*.md" | grep -oE 'scripts/[a-zA-Z0-9_/-]+\.(js|ps1)'

# 搜尋 docs/ 引用
grep -r "docs/" --include="*.md" | grep -oE 'docs/[a-zA-Z0-9_/-]+\.md'
```

**驗證**：用 `ls` 確認每個檔案都存在

---

### 3. 工具一致性檢查

**目的**：確保 README 工具清單 = TypeScript 定義 = C# 實作

**執行步驟**：

| 來源 | 檢查方法 |
|-----|---------|
| TypeScript | `grep "name:" MCP-Server/src/tools/revit-tools.ts` |
| C# | `grep 'case "' MCP/Core/CommandExecutor.cs` |
| README | 手動比對工具清單 |

**需一致的項目**：
- 工具名稱
- 工具數量

---

### 4. 已刪除檔案檢查

**目的**：確保沒有引用已刪除的檔案

**執行命令**：
```bash
# 列出最近刪除的檔案
git log --diff-filter=D --summary | grep "delete mode" | head -20

# 搜尋是否還有引用這些檔案
grep -r "已刪除的檔案名" --include="*.md"
```

---

### 5. 圖片/資源檢查

**目的**：確保所有引用的圖片、腳本都存在

**執行命令**：
```bash
grep -r "\.(png|jpg|gif|svg)" --include="*.md"
```

---

## 📋 快速檢查命令

一行執行所有基本檢查：

```bash
echo "=== Markdown 連結 ===" && \
grep -roh '\./[^)]*\.md' *.md 2>/dev/null | sort -u | while read f; do [ ! -f "$f" ] && echo "❌ 缺少: $f"; done && \
echo "=== domain/ 引用 ===" && \
grep -roh 'domain/[a-zA-Z0-9_/-]*\.md' . --include="*.md" 2>/dev/null | sort -u | while read f; do [ ! -f "$f" ] && echo "❌ 缺少: $f"; done && \
echo "=== 檢查完成 ==="
```

---

## ❌ 常見問題

| 問題 | 原因 | 解決方案 |
|-----|------|---------|
| 連結到不存在的 .md 檔案 | 檔案被刪除但引用未更新 | 更新引用或恢復檔案 |
| 工具數量不一致 | 新增工具後未更新 README | 同步更新 README |
| 路徑錯誤 | 目錄重構後未更新 | 全域搜尋替換 |

---

## 🔄 執行頻率

| 情境 | 頻率 |
|-----|------|
| 新增/刪除檔案 | 每次 |
| 目錄重構 | 每次 |
| 發布版本前 | 每次 |
| 日常開發 | 每週一次 |

---

**最後更新**：2026-01-04
