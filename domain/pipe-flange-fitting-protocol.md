---
title: 管段法蘭自動配對與安裝流程 (Pipe Flange Auto-Fitting Protocol)
description: 定義如何根據管徑自動選擇對應尺寸的法蘭元件並進行管末端安裝。
category: 族群與元件 (Families & Components)
status: 已驗證 (Verified)
version: 1.1
---

# 管段法蘭自動配對與安裝流程 (V1.1)

此流程結合了 Revit MCP 工具與業務邏輯，旨在解決手動放置法蘭並調整連接頭的繁瑣過程。

## 🛠 準備工作
1. **載入族群**：確保專案中已載入 Georg Fischer (GF) 的法蘭族群。
   - 範例族群：`PIF_PROGEF Plus bf - outlet flange adaptor_GF`
2. **連線檢查**：啟動 Revit 中的 MCP Service (Port: 11111)。

## 📋 執行步驟

### 第一步：選取並辨識管段
使用 `get_selected_elements` 取得目前選取的管段資訊。
```bash
# 取得選取項
get_selected_elements()
```

### 第二步：確認管線外徑 (Outer Diameter)
確認管段的 `外徑` (Outside Diameter) 參數。此數值將作為選取法蘭類型的關鍵依據。
- ** Georg Fischer 特性**：例如外徑 50mm 通常對應 `d50` 類型。

### 第三步：連接法蘭 連接點1 connect1 接點
確保法蘭的 `連接點1 connect1` 接點 (通常是與管線連接的端點) 精確連接至管段的開放接點 (Open Connectors)。
- 自動執行時應遍歷法蘭的 `ConnectorManager` 並尋找名稱包含 `connect1` 的接點。

### 第四步：執行自動收頭並匹配尺寸 ( add_pipe_cap )
執行 `add_pipe_cap`。工具應自動將法蘭大小調整為與管線外徑一致。
```javascript
// 範例：針對 ID 1852405 的管道安裝指定族群法蘭
add_pipe_cap({
  pipeId: 1852405,
  familyName: "PIF_PROGEF Plus bf - outlet flange adaptor_GF",
  // 程式將自動匹配 d(外徑) 或調整實例參數
});
```
- **方向校正**：注意管線左/右端的法蘭方向必須相反（面向管心）。

## ⚠️ 常見問題與修正 (Lessons Learned)
- **尺寸不匹配**：若未指定 `typeName`，Revit 會預設使用族群中找到的第一個類型。V1.1 強化了自動匹配邏輯，優先尋找 `d + [外徑]`。
- **接點方向 (Flip Issue)**：**重要！** 僅靠 `ConnectTo` 有時會導致左端法蘭方向錯誤（面向右方）。
  - *修正方案*：應使用 `doc.Create.NewFamilyInstance(PipeConnector, FamilySymbol)` 以確保 Revit 自動計算方向，或在放置後檢查向量並執行翻轉。
- **命名規範**：族群接點應統稱為 `連接點1 connect1` 以利腳本辨識。
- **自動對位**：本工具會自動計算 `Connector.Origin` 並執行 `ConnectTo`，不需要手動平移元件。

## 📝 pyRevit 封裝建議 (For Future Use)
未來封裝至 pyRevit 時，應實作以下邏輯：
1. 尋找 `!connector.IsConnected` 的管末端。
2. 根據 `RBS_PIPE_OUTER_DIAMETER` 尋找 `d + 外徑` 的 `FamilySymbol`。
3. **關鍵：** 使用 `doc.Create.NewFamilyInstance(connector, symbol)` 而非點座標。

---
最後更新: 2026-02-22 (V1.1)
