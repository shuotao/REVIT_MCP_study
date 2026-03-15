# 管段收頭與法蘭銜接工作流程 (Pipe Capping & Flange Connection Workflow)

本文件定義了如何使用 MCP 工具在 Revit 中為管段新增自定義收頭（如法蘭）的標準作業程序。

## 🛠 核心工具
- `get_selected_elements`: 取得目前在 Revit 中選取的管段 ID。
- `add_pipe_cap`: 在管段開放末端新增指定的族群收頭。

## 📋 執行步驟

### 第一步：選取管段 (Step 1: Select Pipe)
在 Revit 中手動選取想要新增收頭的管段或風管。

### 第二步：辨識元素 (Step 2: Identify Element)
執行以下指令取得選取的元素 ID：
```bash
# 由 AI 執行
get_selected_elements()
```

### 第三步：執行收頭 (Step 3: Add Cap/Flange)
根據需求執行收頭指令：

#### A. 使用 Revit 內建收頭 (Built-in Cap)
```bash
add_pipe_cap(pipeId: 123456)
```

#### B. 使用指定法蘭族群 (Specific Flange Family)
```bash
add_pipe_cap(pipeId: 123456, familyName: "我的法蘭族群名稱", typeName: "預設")
```

## ⚠️ 注意事項
1. **族群名稱**：請確保 `familyName` 與專案中載入的族群名稱完全一致。
2. **開放末端**：該工具僅會處理第一個找到的「未連接 (Open)」連接頭。
3. **風管支援**：風管目前僅支援「指定族群」模式，不支援內建自動收頭。

## 📂 驗證過的族群範例 (Verified Family Examples)
以下為常用且經過驗證的管材與對應收頭資訊：

| 管材名稱 | 收頭/法蘭族群名稱 (`familyName`) | 說明 |
| :--- | :--- | :--- |
| **PROGEF Plus PP (bf) - SDR11** | **PIF_PROGEF Plus bf - outlet flange adaptor_GF** | Georg Fischer PP-H 法蘭轉接頭 |

---
最後更新: 2026-02-22
