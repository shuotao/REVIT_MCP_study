---
name: element-coloring-workflow
description: "元素上色工作流程：根據參數值對 Revit 元素進行顏色標記與視覺化。當使用者提到上色、顏色標示、colorize、視覺化標記、圖形覆寫時觸發。"
metadata:
  version: "1.0"
  updated: "2025-12-17"
  created: "2025-12-17"
  contributors:
    - "shuotao"
  references: []  # TODO: 月小聚補法規條號或外部依據
  related: []  # TODO: 月小聚補相關 domain（檔名）
  referenced_by:
    - element-coloring
    - element-query
  tags: [上色, 顏色標示, colorize, 視覺化, 圖形覆寫, override]
---

# 元素上色工作流程

執行元素上色任務時，**必須一步一步執行並與用戶討論**。

## 執行前確認事項

在開始任何上色操作前，必須先確認：

1. **視圖類型是什麼？**
   - 平面圖 → 使用切割樣式 (Cut Pattern)
   - 立面圖/剖面圖 → 使用表面樣式 (Surface Pattern)

2. **要用哪個參數分類？**
   - 確認參數的確切名稱（如 `s_CW_防火防煙性能`）

3. **顏色方案是什麼？**
   - 與用戶討論每個參數值對應的顏色

## 標準執行步驟

**必須依序執行，每步完成後等待用戶確認再繼續：**

### 步驟 1: 清除舊顏色
```bash
node clear_walls.js
```

### 步驟 2: 取消牆柱接合
```bash
node step_unjoin.js
```

### 步驟 3: 牆體上色
```bash
node fire_rating_full.js
```

### 步驟 4: 柱子上色（黑色）
```bash
node color_columns_black.js
```

### 步驟 5: 恢復牆柱接合
```bash
node step_rejoin.js
```

## 腳本目錄

`c:\Users\User\Desktop\REVIT MCP\MCP-Server\`

## 顏色方案範例（防火防煙性能）

| 參數值 | 顏色 | RGB |
|--------|------|-----|
| 2 | 綠色 | (0, 180, 0) |
| 1 | 黃色 | (255, 255, 0) |
| (空) | 紫色 | (200, 0, 200) |
| 柱子 | 黑色 | (30, 30, 30) |

## 注意事項

- 所有操作只影響當前視圖
- 操作可逆，使用 `clear_walls.js` 清除
- 修改 C# 後需重新啟動 Revit

## 相關工具

| 命令 | 功能 |
|------|------|
| `override_element_graphics` | 覆寫元素圖形顯示 |
| `clear_element_override` | 清除元素圖形覆寫 |
| `unjoin_wall_joins` | 取消牆柱接合 |
| `rejoin_wall_joins` | 恢復牆柱接合 |
| `query_elements` | 查詢元素 |
