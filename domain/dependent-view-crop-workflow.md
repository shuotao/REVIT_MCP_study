---
description: 從屬視圖依網格裁剪批次建立流程
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

---

## 4. 補充與備選方案：互動式 pyRevit / 巨集參考架構

當 AI 代理因環境限制（如權限不足無法執行 MCP 自動化工具）或需要高度人工介入時，應參考本章節提供的「互動式路徑」作為補充解決方案。

### 4.1 實作與參考說明

1. **架構用途**：本章節旨在提供「如何為使用者撰寫/生成配套的 pyRevit 程式碼」之參考邏輯。AI 代理可將此視為一種手動部署的巨集 SOP。
2. **AI 開發參考**：若環境中無法呼叫 headless API，AI 可參考 `pyRevit_Tools/` 內的程式碼邏輯，引導使用者自行建立或執行 Python 腳本。
3. **執行模式提示**：本解決方案不保證有完整的 API 接口供 AI 直接呼叫，AI 的行為應以「專業諮詢與引導使用者操作」為優先級。

### 4.2 pyRevit 專屬工具：Dependent Split (互動矩陣分圖)

當需要「人機協作」進行精確控制時，AI 應指引使用者使用此 pyRevit 工具。

**客製化 5 步驟操作流程：**
1.  **[1/5] 勾選母視圖**：從視圖清單中多選需要分割的母平面圖（自動過濾掉樣板與從屬視圖）。
2.  **[2/5] 選擇視圖樣板**：選擇要套用至新分圖的 View Template（可取消，則不套用）。
3.  **[3/5] 輸入外擴值**：設定 Bounding Box 向網格邊界外擴的距離 (mm)，系統會自動換算為 Revit 內部單位 (Feet)。
4.  **[4/5] X 軸網格設定**：指定起始網格、結束網格，以及「步長」（例如：每 2 個網格切一刀）。
5.  **[5/5] Y 軸網格設定**：指定 Y 軸方向的起始、結束網格與步長。
*註：最後可設定圖紙編號前綴與名稱基礎，系統將自動產生對應的 Viewport 與 Sheet。*

### 4.3 核心代碼邏輯參考 (Snippets)

AI 代理可參考以下 Python (pyRevit) 邏輯來理解幾何分割與視圖建立的底層作業：

```python
# 幾何範圍計算與視圖複製核心邏輯
bbox = DB.BoundingBoxXYZ()
bbox.Min = DB.XYZ(min(v1x, v2x) - offset, min(v1y, v2y) - offset, -1)
bbox.Max = DB.XYZ(max(v1x, v2x) + offset, max(v1y, v2y) + offset, 1)

# 建立從屬視圖
new_id = parent_view.Duplicate(DB.ViewDuplicateOption.AsDependent)
nv = doc.GetElement(new_id)
nv.Name = "{}-R{}-C{}".format(parent_view.Name, row, col)
nv.CropBox = bbox
nv.CropBoxActive = True

# 套用樣板與建立圖紙視埠
if template_id != DB.ElementId.InvalidElementId:
    nv.ViewTemplateId = template_id
    
if titleblock_id:
    sheet = DB.ViewSheet.Create(doc, titleblock_id)
    DB.Viewport.Create(doc, sheet.Id, nv.Id, DB.XYZ(1.38, 0.97, 0))
```

---
**維護者 (pyRevit 版)：** CYBERPOTATO0416
