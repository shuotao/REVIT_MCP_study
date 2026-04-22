---
name: parking-auto-numbering
description: "停車格自動編號 SOP：自動化對 Revit 停車格（汽車、機車、大客車）進行分類排序與編號，取代人工手動輸入「備註」參數的繁瑣流程。當使用者提到停車自動編號、parking numbering、停車備註、車位編碼時觸發。"
metadata:
  version: "1.0"
  updated: "2026-04-02"
  created: "2026-04-02"
  contributors:
    - "unknown"
  references: []  # TODO: 月小聚補法規條號或外部依據
  related: []  # TODO: 月小聚補相關 domain（檔名）
  referenced_by: []  # TODO: 月小聚補（被哪些 skill 引用）
  tags: [Revit, Automation, Parking, MCP, 停車自動編號, parking numbering]
---

# 停車格自動編號標準作業程序 (Auto Numbering SOP)

## 1. 目的
自動化地對 Revit 停車格進行分類排序與編號，取代人工手動輸入「備註」參數的繁瑣流程，確保數據一致性。

## 2. 適用對象
- 汽車停車格 (Car)
- 機車停車格 (Motorcycle)
- 大客車停車格 (Bus)

## 3. 前置準備
- Revit 專案已載入 MCP 外掛。
- 平面視圖已開啟且車位可見。
- 停車格元件需具備「備註」例證參數（或自定義參數名稱）。

## 4. 作業流程
1. **建立連結**：啟動 MCP Server 並與 Node.js 腳本連線。
2. **模擬驗證**：執行 `--dry-run` 模式，確認分類統計數量與預覽排序正確。
3. **正式執行**：
   - 腳本會依 Y 座標（由上至下）分群。
   - 同一群內依 X 座標（由左至右）排序。
   - 各類別獨立從 "1" 開始編號。
4. **排除例外**：腳本會自動排除非車位的標記（如導向箭頭），列入 `unknown` 群組。

## 5. 技術參數參考
- **分群容差 (Grouping Tolerance)**：`1500 mm` (適用於標準停車格寬度，確保同一排車位被正確分到同一組)。
- **排序規則**：1. 分類 (Car/Motor/Bus) ➔ 2. Y 座標（由上到下）➔ 3. X 座標（由左到右）。
- **蛇形排序 (Serpentine)**：預設採 Z 字型由左至右編序。

## 6. 常見問題與處理
- **群組警告**：若車位在群組內，外掛會自動呼叫 `DismissWarningsPreprocessor` 忽略提示。
- **座標提取失敗**：確保元件具有有效的實體幾何，否則無法計算 BoundingBox 中心。
