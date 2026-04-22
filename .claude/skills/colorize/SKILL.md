---
name: colorize
description: |
  依參數值對 Revit 元素進行顏色標記與視覺化，支援牆體、柱子等元素的分色上色流程。
  TRIGGER when: 上色, 顏色標示, colorize, visualization, 著色, 色分, 圖形覆寫, 標記顏色
user-invocable: true
---

依據 `domain/element-coloring-workflow.md` 執行元素上色流程。

## 執行前確認

必須先與使用者確認：
1. **視圖類型**：平面圖用切割樣式 (Cut Pattern)，立面/剖面用表面樣式 (Surface Pattern)
2. **分類參數**：確認參數名稱（如 `s_CW_防火防煙性能`）
3. **顏色方案**：每個參數值對應的 RGB 顏色

## Steps

1. 讀取 `domain/element-coloring-workflow.md` 取得標準流程
2. `get_active_view` — 確認視圖類型
3. `query_elements` — 取得目標元素
4. `clear_element_override` — 清除舊顏色
5. `unjoin_wall_joins` — 取消牆柱接合（避免顏色被遮蓋）
6. `override_element_graphics` — 依參數值逐一上色
7. `override_element_graphics` — 柱子上黑色 (30, 30, 30)
8. `rejoin_wall_joins` — 恢復牆柱接合
9. 輸出上色結果摘要

## Error Handling

| 情況 | 處理 |
|------|------|
| 參數名稱不存在 | 用 `get_category_fields` 列出可用參數讓使用者選擇 |
| 元素無該參數值 | 使用預設顏色（如紫色）標示「未設定」 |
| 視圖為 3D | 提醒改用平面視圖以確保切割樣式生效 |

## Related Skills

| 條件 | 建議技能 | 原因 |
|------|---------|------|
| 上色參數為防火時效 | `/check-fire-rating` | 先執行防火檢查再上色更有意義 |
| 上色前需確認牆體方向 | `/check-wall` | 確保內外側正確再做視覺化 |
| 需要查詢元素參數分佈 | 使用 `get_field_values` | 參考 `domain/element-query-workflow.md` |
