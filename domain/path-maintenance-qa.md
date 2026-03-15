# 路徑維護與 QA/QC 工作流程

> 專案目錄重構後的維護指南

## 📋 背景

2024-12-17 專案進行目錄重構：
- **舊結構**：`MCP/MCP/` (雙層嵌套)
- **新結構**：`MCP/` (單層)

此變更需要同步更新專案中所有相關的路徑參照。

---

## 🔍 路徑檢查清單

### 需要檢查的檔案類型

| 優先級 | 檔案類型 | 說明 |
|:---:|:---|:---|
| 🔴 | `*.ps1` | PowerShell 安裝腳本 |
| 🔴 | `*.sln` | Visual Studio 解決方案檔 |
| 🟠 | `README.md` | 專案說明文件 |
| 🟠 | `*.md` | 其他 Markdown 文件 |
| 🟡 | `*.json` | 設定檔 |
| ⚪ | `MCP/obj/*` | 建置快取（忽略，不追蹤） |

### 搜尋模式（必須全部執行）

```powershell
# 1. 基本路徑搜尋
grep -r "MCP\\MCP\\" --include="*.md" --include="*.ps1" --include="*.sln"
grep -r "MCP/MCP/" --include="*.md" --include="*.ps1" --include="*.sln"

# 2. 樹狀圖結構搜尋（關鍵！容易遺漏）
grep -r "└── MCP/" --include="*.md"
grep -r "├── MCP/" --include="*.md"

# 3. 範例路徑搜尋
grep -r "cd.*MCP.*MCP" --include="*.md"
```

---

## ⚠️ 容易遺漏的位置

### 1. 專案結構樹狀圖

**位置**：README.md、README.en.md 的「專案結構」章節

**錯誤範例**：
```
├── MCP/                    # Revit Add-in (C#)
│   └── MCP/                # ❌ 這是錯的！
│       ├── Application.cs
```

**正確範例**：
```
├── MCP/                    # Revit Add-in (C#)
│   ├── Application.cs      # ✅ 直接在 MCP/ 下
│   ├── RevitMCP.csproj
```

### 2. .sln 專案參照

**位置**：`REVIT_MCP_study.sln`

**錯誤**：
```
Project("...") = "RevitMCP", "MCP\MCP\RevitMCP.csproj", "..."
```

**正確**：
```
Project("...") = "RevitMCP", "MCP\RevitMCP.csproj", "..."
```

### 3. 應保留舊路徑的位置

**不要修改這些檔案**：
- `CHANGELOG.md` - 歷史記錄，說明修復了什麼
- `verify-installation.ps1` - 用於**檢測**舊路徑
- `check-structure.ps1` - 用於**檢測**舊路徑

---

## 🔧 QA/QC 工作流程

### 步驟 1：執行自動化搜尋

```powershell
# 使用專案中的檢查腳本
.\scripts\check-structure.ps1

# 或使用 grep 進行全面搜尋
$patterns = @(
    "MCP\\MCP\\",
    "MCP/MCP/",
    "└── MCP/",
    "cd.*MCP.*MCP"
)

foreach ($pattern in $patterns) {
    Write-Host "Searching: $pattern" -ForegroundColor Cyan
    Get-ChildItem -Recurse -Include "*.md","*.ps1","*.sln" |
        Select-String -Pattern $pattern
}
```

### 步驟 2：手動檢視結構性內容

**必須肉眼檢視的內容**：
1. README.md 專案結構樹狀圖
2. README.en.md 專案結構樹狀圖
3. 任何包含 ASCII 樹狀圖的文件

### 步驟 3：區分需修正與應保留

**判斷邏輯**：
```yaml
找到 MCP/MCP 參照時:
  如果是 CHANGELOG.md:
    保留原樣（歷史記錄）
  
  如果是 verify/check 腳本:
    保留原樣（檢測邏輯需要這個字串）
  
  如果是 MCP/obj/* 目錄:
    忽略（建置快取，被 .gitignore 排除）
  
  否則:
    需要修正
```

### 步驟 4：修正後驗證

```powershell
# 重新執行搜尋確認
$git = "C:\Users\01102088\AppData\Local\Programs\Git\cmd\git.exe"

# 檢查 git diff
& $git diff --stat

# 確認改動正確後提交
& $git add -A
& $git commit -m "fix: 修正路徑參照"
& $git push origin main
```

---

## 📊 Revit 版本與建置路徑對照

| Revit 版本 | .csproj 檔案 | 輸出路徑 | 警告數 |
|:---|:---|:---|:---:|
| 2022 | `MCP\RevitMCP.csproj` | `MCP\bin\Release\` | 0 |
| 2023 | `MCP\RevitMCP.csproj` | `MCP\bin\Release\` | 0 |
| 2024 | `MCP\RevitMCP.2024.csproj` | `MCP\bin\Release.2024\` | 56 |

> **注意**：Revit 2024 的 56 個警告是正常的（使用 Revit 2022 相容 API），不影響功能。

---

## 🎯 經驗教訓

### 為什麼容易遺漏？

1. **Regex 模式不夠全面**
   - `MCP[/\\]MCP[/\\]` 需要後續分隔符
   - 樹狀圖 `└── MCP/` 沒有後續分隔符

2. **只依賴自動化搜尋**
   - 視覺化內容（樹狀圖）需要肉眼檢視
   - 某些格式（如 ASCII art）不適合 regex

3. **沒有完整的檢查清單**
   - 應該建立並遵循標準化檢查流程

### 改進方案

1. **多種搜尋模式組合使用**
2. **手動檢視所有 README 文件的結構性內容**
3. **使用 `check-structure.ps1` 腳本自動化部分檢查**
4. **修正後重新執行完整搜尋確認**

---

**最後更新**：2024-12-18
**相關版本**：v1.4.1
