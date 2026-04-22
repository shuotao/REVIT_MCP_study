---
name: dependent-view-crop-workflow
description: "從屬視圖批次裁剪：依網格線為邊界，批次建立從屬視圖並設定裁剪範圍。適用於大型專案分區出圖。當使用者提到從屬視圖、dependent view、分區出圖、網格裁剪、視圖分割時觸發。"
metadata:
  version: "1.0"
  updated: "2026-04-02"
  created: "2026-03-16"
  contributors:
    - "unknown"
  references: []  # TODO: 月小聚補法規條號或外部依據
  related: []  # TODO: 月小聚補相關 domain（檔名）
  referenced_by:
    - dependent-view-crop
    - sheet-management
  tags: [從屬視圖, dependent view, 網格裁剪, 分區出圖, batch crop]
---

# 從屬視圖依網格裁剪批次建立流程 (Dependent View Crop by Grids)

## 1. 業務場景與需求
在大型專案中，平面圖需分為多個區塊放置於獨立圖紙上。此流程旨在自動化依據「特定網格線」為邊界，批次建立「從屬視圖 (Dependent View)」並設定正確的裁剪範圍。

**核心需求**:
1. **維持從屬關係**：建立出來的視圖為從屬視圖，繼承母視圖比例與圖面設定。
2. **網格容錯計算**：若某軸向（X 或 Y）缺一條網格線，以指定的「偏移距離 (Offset)」推算出一個方框邊界。
3. **流水號命名**：自動偵測現有從屬視圖，依序建立如 `[母視圖名稱]-1`, `[母視圖名稱]-2` 的命名。
4. **批次執行能力**：可一次針對多個母視圖（如 1F~10F）的相同網格區域，批次產生對應的從屬視圖。
5. **極致效能**：透過批次指令將原本數分鐘的任務縮短至數秒內。

---

## 2. 邊界計算邏輯 (Bounding Box Geometry)

### 2.1 網格幾何相交與容差
- **兩條網格線**：若 X 軸提供 `B27`, `B23`，系統會抓取這兩條網格的幾何線段 (Curve)，並以它們的 X 座標為範圍，向外各加 `Offset` 距離。
- **單一網格線**：若 Y 軸僅提供 `BE`，系統會以 `BE` 網格的 Y 座標為中心，向外加減 `Offset` 距離，形成該單軸的方框範圍容差。

### 2.2 3D 邊界方框 (BoundingBoxXYZ)
最終計算出一個涵蓋範圍的 `XYZ Min` 與 `XYZ Max`。由於平面視圖的裁切不看 Z 軸，Z 軸可設定極大值與極小值（例如 -1000 到 +1000）以確保涵蓋視圖範圍。

---

## 3. 系統架構設計 (C# + Node.js)

此流程採「高階 JS 流程控制」與「底層 C# 幾何運算」分離的架構。

### 3.1 Revit API (C#) - MCP 工具實作
需要在 `RevitMCP` 中新增或擴充兩個核心工具：

1. **`Tool: RevitMCP_CalculateGridBounds`**
   - **輸入參數**: 
     - `x_grids`: string[] (如 `["B23", "B27"]`)
     - `y_grids`: string[] (如 `["BE"]`)
     - `offset_mm`: number (往外偏移的公釐尺寸)
   - **輸出結果**: 回傳 `{ min: {x,y,z}, max: {x,y,z} }` 這個純資料的 BoundingBox 結構。

3. **`Tool: RevitMCP_CreateGridCroppedViewsBatch` (推薦使用)**
   - **輸入參數**:
     - `parentViewIds`: number[]
     - `x_grid_names`: string[]
     - `y_grid_names`: string[]
     - `offset_mm`: number
   - **執行動作**: 
     - 在單一 Transaction 中完成所有網格區間的計算、視圖複製、命名與裁剪。
     - **效能優勢**: 處理 16 個視圖僅需 ~2.5 秒。

---

### 3.2 Node.js 執行腳本
讓使用者能透過修改腳本開頭的配置檔，一鍵執行：

- **`scripts/batch_dependent_crop.js`**: 適用於跨樓層、相同網格區塊的批次裁切。
- **`scripts/crop_viewblocks.js`**: 適用於單一視圖內，切割成 N 個座標區塊的矩陣裁切。

```javascript
const config = {
  // 網格定義 (可給 1 條或 2 條)
  grids: {
    X: ["B23", "B27"], 
    Y: ["BE"]           
  },
  offset_mm: 2000,        // 缺少網格或外擴的距離 2M
  
  // 要批次處理的母視圖名稱特徵 (使用 Regex 或部分名稱比對)
  // 若只想要單個，寫 ["1F平面圖"] 即可
  target_views: ["1F平面", "2F平面", "3F平面", "4F平面"] 
};
```
執行腳本時將會依序呼叫上述兩個 MCP 工具完成自動化。
