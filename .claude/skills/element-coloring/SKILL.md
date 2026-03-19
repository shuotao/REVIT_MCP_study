---
name: element-coloring
description: "元素上色工作流程：根據參數值對 Revit 元素進行顏色標記與視覺化。觸發條件：使用者提到上色、顏色標示、color code、highlight、視覺化標記、參數上色。工具：get_category_fields、get_field_values、query_elements_with_filter、override_element_graphics、clear_element_override、unjoin_wall_joins、rejoin_wall_joins。"
---

# 元素上色工作流程

執行前請先讀取 domain/element-coloring-workflow.md 了解完整流程與注意事項。

## Prerequisites

1. 視圖類型：平面圖用切割樣式、立面圖用表面樣式
2. 分類參數的確切名稱（用 `get_category_fields` 確認）
3. 與使用者討論顏色方案

## Workflow

### 步驟 1：清除舊覆寫
`clear_element_override` 清除目標元素的現有顏色

### 步驟 2：取消牆接合（如果上色對象是牆）
`unjoin_wall_joins` 避免接合導致顏色顯示不正確

### 步驟 3：查詢並分類
1. `get_category_fields` 確認參數名稱
2. `get_field_values` 取得參數值分佈
3. `query_elements_with_filter` 依參數值篩選元素

### 步驟 4：依分類上色
`override_element_graphics` 對每組元素套用對應顏色

### 步驟 5：恢復接合
`rejoin_wall_joins` 恢復牆體幾何接合
