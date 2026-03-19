---
name: dependent-view-crop
description: "從屬視圖批次裁剪：依網格線為邊界，批次建立從屬視圖並設定裁剪範圍。適用於大型專案分區出圖。觸發條件：使用者提到從屬視圖、dependent view、分區出圖、網格裁剪、視圖分割、batch crop。工具：get_all_grids、get_all_views、get_active_view。"
---

# 從屬視圖依網格裁剪批次建立

執行前請先讀取 domain/dependent-view-crop-workflow.md 了解邊界計算邏輯。

## 工作流程

### 步驟 1：取得網格資訊
`get_all_grids` → 列出所有網格線名稱與座標

### 步驟 2：確認目標視圖
`get_all_views` 或 `get_active_view` → 確認要建立從屬視圖的母視圖

### 步驟 3：定義裁剪範圍
使用者指定 X 軸網格（如 B27, B23）和 Y 軸網格（如 BE）→ 計算 BoundingBox

### 步驟 4：批次建立
依序建立從屬視圖，自動命名為 [母視圖名稱]-1, -2, -3...

### 注意
- 單一網格線時以 Offset 推算範圍
- Z 軸設定極大值以涵蓋所有樓層
- 從屬視圖繼承母視圖的比例與圖面設定
