# Gemini CLI MCP Server 設定完全指南

> **適用對象**：第一次使用 Gemini CLI 並想要掛載 MCP Server 的使用者  
> **最後更新**：2026-01-09

---

## 📋 目錄

1. [什麼是 MCP Server？](#什麼是-mcp-server)
2. [找到 .gemini 資料夾](#找到-gemini-資料夾)
3. [設定 settings.json](#設定-settingsjson)
4. [驗證 MCP Server 是否掛載成功](#驗證-mcp-server-是否掛載成功)
5. [一鍵部署 Prompt（複製貼上即可）](#一鍵部署-prompt)

---

## 什麼是 MCP Server？

**MCP (Model Context Protocol)** 是一種讓 AI 助手能夠與外部系統互動的協定。透過 MCP Server，您可以讓 Gemini CLI：

- 🔌 連接到專業軟體（如 Revit、AutoCAD 等）
- 📊 讀取和操作外部資料
- 🤖 執行自動化工作流程

當您成功設定 MCP Server 後：
- **啟動畫面**：Gemini CLI 啟動時會顯示已掛載的 MCP Server
- **斜線指令**：輸入 `/mcp` 可查看所有已掛載的伺服器清單

---

## 找到 .gemini 資料夾

### Windows 系統

`.gemini` 資料夾位於您的**使用者目錄**下，是一個**隱藏資料夾**。

#### 方法一：直接在檔案總管輸入路徑
1. 按下 `Win + E` 開啟檔案總管
2. 在網址列輸入：
   ```
   %USERPROFILE%\.gemini
   ```
3. 按下 `Enter` 即可進入

#### 方法二：顯示隱藏資料夾
1. 開啟檔案總管
2. 點選上方「**檢視**」標籤
3. 勾選「**隱藏的項目**」
4. 進入 `C:\Users\您的使用者名稱\` 目錄
5. 找到 `.gemini` 資料夾

#### 方法三：使用終端機
```powershell
# 開啟 .gemini 資料夾
explorer $env:USERPROFILE\.gemini
```

### macOS / Linux 系統

```bash
# 進入 .gemini 資料夾
cd ~/.gemini

# 或用 Finder/檔案管理器開啟
open ~/.gemini       # macOS
xdg-open ~/.gemini   # Linux
```

---

## 設定 settings.json

### 檔案位置

```
Windows:  C:\Users\您的使用者名稱\.gemini\settings.json
macOS:    ~/.gemini/settings.json
Linux:    ~/.gemini/settings.json
```

### 設定格式

打開 `settings.json`，加入您的 MCP Server 設定：

```json
{
  "security": {
    "auth": {
      "selectedType": "oauth-personal"
    }
  },
  "您的MCP伺服器名稱": {
    "command": "執行程式的路徑",
    "args": [
      "MCP Server 的入口檔案路徑"
    ],
    "env": {
      "環境變數名稱": "值"
    }
  },
  "general": {
    "previewFeatures": true
  }
}
```

### 實際範例：Revit MCP 2024

```json
{
  "security": {
    "auth": {
      "selectedType": "oauth-personal"
    }
  },
  "revit-mcp-2024": {
    "command": "C:\\Program Files\\nodejs\\node.exe",
    "args": [
      "C:\\Users\\Use\\Desktop\\REVIT_MCP\\REVIT_MCP_study\\MCP-Server\\build\\index.js"
    ],
    "env": {
      "REVIT_VERSION": "2024"
    }
  },
  "general": {
    "previewFeatures": true
  }
}
```

### 設定說明

| 欄位 | 說明 | 範例 |
|:-----|:-----|:-----|
| `伺服器名稱` | 您自訂的名稱，會顯示在 CLI 中 | `"revit-mcp-2024"` |
| `command` | 執行 MCP Server 的程式 | `"node"` 或完整路徑 |
| `args` | 傳遞給程式的參數（通常是入口檔案） | `["path/to/index.js"]` |
| `env` | 環境變數（選填） | `{"KEY": "VALUE"}` |

### 注意事項

⚠️ **Windows 路徑須使用雙反斜線**：
```json
// ✅ 正確
"C:\\Users\\Use\\Desktop\\project\\index.js"

// ❌ 錯誤
"C:\Users\Use\Desktop\project\index.js"
```

---

## 驗證 MCP Server 是否掛載成功

### 1. 重新啟動 Gemini CLI

設定完成後，關閉並重新開啟 Gemini CLI。啟動畫面會顯示：

```
✓ Loaded MCP servers: revit-mcp-2024
```

### 2. 使用斜線指令查看

在對話介面中輸入：

```
/mcp
```

選擇 `list` 選項，即可看到所有已掛載的 MCP Server 及其提供的工具 (Tools)。

### 3. 詢問 AI 確認

直接問 Gemini：

```
請列出你目前可以使用的 MCP 工具
```

---

## 一鍵部署 Prompt

### 🚀 適用情境

當您有一個 MCP Server 的 Git Repository，想要快速讓 Gemini CLI 幫您完成部署時，請複製以下 Prompt 並貼上：

---

### 📋 複製這段 Prompt

```markdown
請幫我完成 MCP Server 的部署作業。以下是我的 MCP Server 專案：

**Git Repository**: [在此貼上您的 Git Repo 網址]

請依照以下步驟執行：

1. **閱讀專案結構**：
   - 分析 Repository 的 README.md 和 package.json
   - 確認這是一個 MCP Server 專案
   - 找出入口檔案位置（通常是 build/index.js 或 dist/index.js）

2. **環境檢查**：
   - 確認我的系統已安裝 Node.js
   - 確認 .gemini 資料夾位置

3. **Clone 並建置專案**：
   - 將專案 clone 到合適的位置
   - 執行 npm install 安裝依賴
   - 執行 npm run build（如果需要）

4. **設定 settings.json**：
   - 找到 ~/.gemini/settings.json（Windows: %USERPROFILE%\.gemini\settings.json）
   - 加入這個 MCP Server 的設定
   - 使用正確的路徑格式（Windows 需雙反斜線）

5. **驗證部署**：
   - 告訴我如何重啟 Gemini CLI
   - 說明如何用 /mcp 指令確認掛載成功

請在每個步驟完成後回報進度，最後提供完整的 settings.json 設定範例。
```

---

### 📋 進階版 Prompt（含錯誤處理）

```markdown
我想要部署一個 MCP Server 到 Gemini CLI，這是我第一次操作。

**Git Repository**: [在此貼上您的 Git Repo 網址]

**我的環境資訊**：
- 作業系統：Windows 11 / macOS / Linux（請選擇）
- Node.js 版本：（如果不確定請輸入「不確定」）

請幫我完成以下作業：

## 第一階段：專案分析
1. 精讀 Repository 的文件（README、package.json、tsconfig.json）
2. 確認專案類型是否為 MCP Server
3. 列出此 MCP Server 提供的功能（Tools、Resources、Prompts）
4. 確認有無特殊的環境變數需求

## 第二階段：環境準備
1. 檢查我的系統是否具備必要的執行環境
2. 如有缺少，請提供安裝指令
3. 建議專案的最佳存放位置

## 第三階段：安裝與建置
1. 提供 git clone 指令
2. 提供 npm install 指令
3. 提供建置指令（如 npm run build）
4. 如果建置失敗，請分析錯誤原因並提供解決方案

## 第四階段：Gemini CLI 設定
1. 找到我的 settings.json 檔案
2. 產生完整的設定內容（包含正確的路徑）
3. 將設定寫入 settings.json
4. 確保 JSON 格式正確無誤

## 第五階段：驗證與測試
1. 告訴我如何重啟 Gemini CLI
2. 說明如何使用 /mcp 指令確認
3. 提供一個測試指令來驗證 MCP Server 正常運作

如果任何步驟失敗，請：
- 清楚說明錯誤原因
- 提供 2-3 種可能的解決方案
- 詢問我要選擇哪種方式繼續
```

---

## 常見問題 FAQ

### Q1: settings.json 不存在怎麼辦？
直接建立一個新的 `settings.json` 檔案，內容如下：
```json
{
  "security": {
    "auth": {
      "selectedType": "oauth-personal"
    }
  }
}
```

### Q2: MCP Server 沒有顯示在啟動畫面？
- 確認 `settings.json` 的 JSON 格式正確（使用線上 JSON 驗證工具）
- 確認 `command` 路徑指向正確的 Node.js 執行檔
- 確認 `args` 中的入口檔案路徑存在

### Q3: 如何移除 MCP Server？
編輯 `settings.json`，刪除對應的設定區塊，然後重啟 Gemini CLI。

### Q4: 可以同時掛載多個 MCP Server 嗎？
可以！在 `settings.json` 中新增多個設定區塊即可：
```json
{
  "mcp-server-1": { ... },
  "mcp-server-2": { ... },
  "mcp-server-3": { ... }
}
```

---

## 相關資源

- [Gemini CLI 官方文件](https://gemini-cli.gh.miniasp.com/)
- [MCP 協定規範](https://modelcontextprotocol.io/)
- [Gemini CLI 擴充套件說明](https://gemini-cli.gh.miniasp.com/extension.html)

---
