---
name: wall-orientation-check
description: "牆壁內外方向檢查：檢測外牆的 Exterior/Interior Side 是否正確，錯誤方向會影響飾面位置與容積計算。觸發條件：使用者提到牆壁方向、內外側、Exterior Side、Interior Side、牆壁檢查、wall orientation、wall check。工具：query_elements_with_filter、override_element_graphics、get_wall_info。"
---

# 牆壁內外方向檢查

執行前請先讀取 domain/wall-check.md 了解判斷邏輯。

## 工作流程

### 步驟 1：區分內牆與外牆
`query_elements_with_filter` 篩選牆體 → 依 Function 參數分類

### 步驟 2：檢查外牆方向
`get_wall_info` 取得每面外牆的位置線方向 → 與建築外輪廓比對判斷內外側

### 步驟 3：視覺化標記
`override_element_graphics` 上色：
- 綠色：方向正確
- 紅色：方向可能錯誤
- 黃色：需要人工確認
- 藍色：內牆（不檢查方向）
