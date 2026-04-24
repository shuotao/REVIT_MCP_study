---
name: stair-hidden-line-workflow
description: "剖面隱藏樓梯可視化：在剖面視圖中自動為組合樓梯被側板遮擋的梯級繪製虛線詳圖線。當使用者提到樓梯隱藏線、stair hidden line、剖面樓梯、虛線、梯級、stair visualization、組合式樓梯時觸發。"
metadata:
  version: "1.0"
  updated: "2026-03-16"
  created: "2026-03-16"
  contributors:
    - "unknown"
  references: []  # TODO: 月小聚補法規條號或外部依據
  related: []  # TODO: 月小聚補相關 domain（檔名）
  referenced_by:
    - stair-hidden-line
  tags: [樓梯, stair, hidden line, 虛線, 剖面, 組合樓梯]
---

# 樓梯被遮擋部分虛線自動產生 SOP

這個流程用於在**剖面圖**中，自動找尋被桁條遮擋的組合樓梯踏階，並為第一排踏階產生精確投影的虛線（例如 `虛線(極密)`）。

## 1. 核心邏輯與規則

此功能解決了三個在 Revit 剖面視圖下的幾何繪製難題：
1. **只挑選被遮擋的樓梯**：
   - 只有「組合樓梯 (Assembled Stairs)」兩側有桁條 (Stringers/Supports)，在剖面中會遮住梯段的踏板面。
   - 程式透過比對 `ElementType.FamilyName` 是否包含 `"組合"` 或 `"Assembled"` 來決定是否處理該樓梯。RC 樓梯 (如現場澆注、整體樓梯) 會因無側向遮擋而被忽略。
2. **只畫第一排梯段，且避開被切斷的前景**：
   - 透過計算幾何邊緣點相對於「視圖原點 (Origin)」與「觀察方向 (ViewDirection)」的內積 (DotProduct) 可得其深度 (Depth)。
   - `Depth <= 0.05`：代表切斷的邊或在切面前方，整座樓梯如果包含此類深度，判定為前景樓梯，直接略過以防畫出多餘線段。
   - 取該樓梯深度最淺的值為 `minDepth`，並只將 `Depth <= minDepth + 2.5ft (約75cm)` 範圍內的邊界視為「第一排」。
3. **完美接合突出席 (Nosing/Profile) 的細小缺口**：
   - 踏階判斷不過度依賴純水平或垂直。長度 `< 0.65ft (約20cm)` 的細小線段統一視為側剖面輪廓 (如倒角、收邊突出席) 並予以保留，這樣能保證生成的虛線不會在轉角處留白斷線。

## 2. 執行腳本

執行腳本已收錄於 `MCP-Server/scripts/draw_hidden_stairs.js`

### 步驟 1：取得樓梯並運算隱藏線 (`trace_stair_geometry`)
- 指令：`trace_stair_geometry`
- 不需帶參數。C# 會自動依照目前視圖的方位和切割深度去掃描 `OST_StairsRuns` 並即時推算各個邊緣坐標。

### 步驟 2：繪製 Detail Lines (`create_detail_lines`)
- 將步驟 1 回傳的直線群交由 `create_detail_lines` 繪製。
- 使用參數：`styleId`，推薦使用 `11911982` (`虛線(極密)`) 以獲得最佳剖面圖表現。
- C# 內部會自動使用視圖的 `ViewDirection` 和 `Origin` 定義平面並投影，不受原本 Z 軸高程限制。

## 3. 使用方式

1. 確認 Revit 已開啟目標的**剖面圖視圖** (如: `丙無障礙梯剖面圖(二)`)。
2. 啟動 `node build/index.js` (或於 VS Code Copilot 確保 MCP 已連線)。
3. 在終端機執行：
   ```bash
   node MCP-Server/scripts/draw_hidden_stairs.js
   ```
4. 觀察輸出，如：`Step 2: Creating 448 detail lines with style 虛線(極密) (11911982)... SUCCESS!`
