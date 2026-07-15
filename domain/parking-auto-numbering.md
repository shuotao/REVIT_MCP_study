---
name: parking-auto-numbering
description: "停車格自動編號 SOP：自動化對 Revit 停車格（汽車、機車、大客車）進行分類排序與編號，取代人工手動輸入「備註」參數的繁瑣流程。當使用者提到停車自動編號、parking numbering、停車備註、車位編碼時觸發。"
metadata:
  version: "1.0"
  updated: "2026-05-15"
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

## 7. 2026-05-15 更新：起點控制與排序驗證

### 起點模式
- **使用者指定起點**：若使用者提供 ElementId，必須傳入 `--start-element {elementId}`。該元素會成為該類別排序序列的第一個車位，並取得起始編號。
- **自動判定起點**：若未提供 ElementId，腳本以排序後第一個元素作為起點，並在輸出中列出「自動判定 ElementId」。正式寫入前需讓使用者確認此起點合理。
- **起始編號**：汽車、機車、大客車分別用 `--car-start`、`--motorcycle-start`、`--bus-start` 設定；未指定時使用 `--start` 或預設 `1`。

### 排序模式判準
- 停車場多排或多島配置時，避免直接使用全場中心點的 `--order clockwise` 作為正式編號依據；此模式可能在同一排中間切斷序列，造成相鄰車位出現尾號接頭號。
- 一般平面車位編碼優先使用 `--order yx --linear`：先依 Y 座標由上到下分排，再依 X 座標由左到右排序，確保同一排相鄰車位連續。
- 若使用者要求真正沿車道繞行的順時鐘路徑，必須先 dry-run 檢查局部相鄰車位是否連續；不應只看前 10 筆排序。

### 標準執行流程
1. 先跑 dry-run：`node scripts/number_parking.js --dry-run --only car --order yx --linear --car-start 433 --start-element 7449884`
2. 檢查輸出中是否列出正確起點，例如 `汽車 起點: 使用者指定 ElementId 7449884`。
3. 抽查相鄰車位是否連續，特別是使用者截圖或指出的問題區域。
4. 確認無誤後移除 `--dry-run` 正式寫入。

### Revit 群組警告
- 批次改寫「備註」時若出現「已在群組編輯模式之外變更群組。變更之所以被允許，是因為此類型只有一個實體。」表示目前載入的 add-in 版本未在 `modify_element_parameter` transaction 套用 warning preprocessor。
- 修正後需編譯並部署對應 Revit 版本，例如 Revit 2020 使用 `Release.R20`，再重啟 Revit 讓新 DLL 生效。
