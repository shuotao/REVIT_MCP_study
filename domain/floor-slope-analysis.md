---
name: floor-slope-analysis
description: "樓板表面排水坡度分析 SOP：以 Solid→PlanarFace 法向量與 Z 軸夾角計算每片朝上頂面的坡度百分比，批次處理並回寫 Min/Max 坡度至指定參數。當使用者提到樓板坡度、排水坡度、floor slope、洩水、坡度檢討、找坡時觸發。"
metadata:
  version: "1.0"
  updated: "2026-06-01"
  created: "2026-06-01"
  contributors:
    - "yunchen-kt (原始需求、演算法與實測 — Issue #45)"
  references: []
  related: ["room-surface-area-review.md", "element-coloring-workflow.md"]
  referenced_by: []
  tags: [Revit, Floor, Slope, 排水, 坡度, drainage, MCP, analyze_floor_slopes]
---

# 樓板表面排水坡度分析標準作業程序 (Floor Slope Analysis SOP)

## 1. 目的
在建築法規與施工圖檢討流程中，室外樓板（陽台、露台、屋頂層、車道）需確認排水坡度是否符合設計標準（常見最小 1～2%）。本 SOP 將「逐片樓板手動量測坡度」自動化：批次計算每片樓板朝上頂面的坡度百分比，並回寫至參數供後續上色標示與出表。

> 來源：本 SOP 與對應工具 `analyze_floor_slopes` 之演算法、批次邏輯與實測由 **yunchen-kt** 於 [Issue #45](https://github.com/shuotao/REVIT_MCP_study/issues/45) 提出。

## 2. 適用對象
- Revit 中 `OST_Floors` 類別的樓板元素。
- 室外（`Function = Exterior`）樓板的洩水坡度檢討；亦可指定任意 `elementIds` 做局部分析。

## 3. 演算法（坡度計算原理）
1. 對每片樓板取 `get_Geometry(Options{ DetailLevel=Fine })` 的 `Solid`。
2. 走訪 `Solid.Faces`，僅取 `PlanarFace`（平面）。
3. 取面法向量 `FaceNormal` 並正規化，得 `n`。
4. **僅保留朝上排水頂面**：`n.Z > 0.7`（與水平夾角 45° 內；排除側面 `n.Z≈0`、底面 `n.Z<0`、以及近垂直的板側/倒角面，後者會吐出數百 % 的假坡度）。幾何走訪會遞迴進入 `GeometryInstance`，避免巢狀幾何的頂面被漏算。
5. `n.Z` 即法向量與垂直 Z 軸夾角的餘弦：
   - `θ = acos(n.Z)`
   - `坡度% = tan(θ) × 100`
   - 水平面 `n.Z=1 → θ=0 → 坡度 0%`。
6. 跨所有朝上面取 **Min / Max** 坡度。

## 4. 作業流程 (AI 執行步驟)
### Step 1: 決定分析範圍
- 指定樓板 → 傳 `elementIds`。
- 全室外樓板 → 省略 `elementIds`，工具自動以 `FUNCTION_PARAM == 1 (Exterior)` 收集。

### Step 2: 執行分析與回寫
- 呼叫 `analyze_floor_slopes(elementIds?, paramName?)`。
- `paramName` 預設 `Comments`（字串欄位寫入 `Slope {min}%~{max}%`）。若指定為數值欄位（StorageType.Double），則以比例值寫入最大坡度（例 2% → 0.02）。
- 回傳 `ProcessedCount` 與每片樓板 `{ ElementId, MinSlopePercent, MaxSlopePercent, UpwardFaceCount, Written }`。

### Step 3: 視覺化（選配）
- 搭配 `override_element_graphics`，將坡度 `< 設計最小值` 的不合規樓板染色，對應 `domain/element-coloring-workflow.md`。

## 5. 常見問題與處理
### 1. 曲面樓板回 null
- **現象**：彎曲或無 `PlanarFace` 的樓板無法取得平面法向量。
- **處理**：工具會將該樓板記入 `Errors` 而非中斷整批；曲面坡度需另以 UV 取樣方法評估（本 SOP 未涵蓋）。

### 2. 參數未回寫
- **現象**：指定 `paramName` 不存在 / 唯讀 / 型別不符。
- **處理**：工具仍於回傳結果提供數值，並在 `Errors` 標明原因；請改用既有可寫字串或數值參數（如 `Comments` 或自訂共享參數）。

### 3. 室外判定不如預期
- `Function` 屬於樓板「型別」參數。若專案未正確設定型別 Function，請改以顯式 `elementIds` 傳入目標樓板。
