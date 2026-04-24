---
name: revit-fill-pattern-conversion
description: "Revit 填滿圖案：製圖轉模型樣式 (Drafting to Model) 工作流程。解決 Drafting Pattern 在視圖旋轉時紋理方向不跟隨模型旋轉的問題，將其轉為 Model Pattern。當使用者提到填充圖案、fill pattern、製圖樣式、模型樣式、drafting、model pattern、磁磚紋理時觸發。"
metadata:
  version: "1.0"
  updated: "2026-04-02"
  created: "2026-04-02"
  contributors:
    - "unknown"
  references: []  # TODO: 月小聚補法規條號或外部依據
  related: []  # TODO: 月小聚補相關 domain（檔名）
  referenced_by: []  # TODO: 月小聚補（被哪些 skill 引用）
  tags: [Revit, FillPattern, Drafting, Model, Automation, 填充圖案, 製圖樣式, 模型樣式]
---

# Revit 填滿圖案：製圖轉模型樣式 (Drafting to Model)

## 1. 問題描述
在 Revit 中，「製圖樣式 (Drafting Pattern)」的填滿範圍在視圖旋轉時，其紋理方向會固定在圖紙坐標系中，不會跟著視圖旋轉。這在製作旋轉過的平面圖或剖面圖時會導致視覺錯誤（例如：磁磚縫或木地板方向不對）。

## 2. 解決方案：轉為模型樣式 (Model Pattern)
「模型樣式」會跟著模型幾何旋轉。本流程透過自動化腳本，將選取的製圖樣式物件轉換為模型樣式物件。

### 關鍵機制
- **視覺一致性**：模型樣式的間距必須根據視圖比例 (Scale) 進行縮放。
  - 公式：`模型間距 = 製圖間距 (mm) * 視圖比例`
  - 例如：1:50 視圖中，2mm 的製圖間距需轉為 100mm 的模型間距。

## 3. 操作流程 (SOP)
### A. 手動選取轉換
1. **選取目標**：在 Revit 中選取一個或多個「填滿範圍 (Filled Region)」。
2. **執行工具**：呼叫 `convert_drafting_to_model_pattern`。

### B. 全自動全專案轉換 (推薦)
1. **執行工具**：呼叫 `auto_convert_rotated_viewport_patterns`。
2. **自動處理**：
   - 遍歷所有圖紙 (Sheets) 上的視埠 (Viewports)。
   - 偵測具有旋轉（90° 順/逆時鐘）屬性的剖面圖與詳圖。
   - **群組檢查**：若物件位於群組內，則自動跳過，以確保原有的群組一致性不被破換。
   - **旋轉判定**：同時核對 `Viewport.Rotation` 與 `Rotate on Sheet` 參數，解決隱藏屬性問題。
   - 自動將製圖樣式按視圖比例複製並替換為模型樣式。

## 4. 注意事項與限制
- **群組保護**：目前自動化腳本會略過群組內的填滿區域。若需轉換群組內容，請手動進入群組編輯模式。
- **命名規範**：名稱中禁止包含冒號 `:`，程式會自動替換。
- **診斷工具**：若發現特定視窗未被轉換，可執行 `check_viewports_rotation` 檢查該視窗的旋轉參數值。
- **單向轉換**：此工具主要用於解決旋轉視圖的顯示問題，建議僅在需要旋轉的圖紙視圖中使用。
- **前景/背景**：目前工具優先處理「前景樣式 (Foreground Pattern)」。

