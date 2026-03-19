---
name: element-query
description: "元素查詢與視覺化：三階段查詢協議（探索→對齊→擷取），支援依參數篩選與上色標記。觸發條件：使用者提到查詢、篩選、element query、filter、參數查詢、color-code、元素屬性、find elements。工具：get_active_schema、get_category_fields、get_field_values、query_elements_with_filter、override_element_graphics、clear_element_override。"
---

# 元素查詢與視覺化

## Lessons Reference
- **L-001**：查詢房間時必須多語言容錯（走廊/Corridor/廊道/通道/廊下）。詳見 `domain/lessons.md`。

## 3-Phase Query Protocol (MANDATORY)

### Phase 1：探索
`get_active_schema` → 探索作用中視圖的所有類別與元素數量。
**必須先執行此步驟**以確認目標類別存在。

### Phase 2：對齊
`get_category_fields` → 取得類別的精確本地化參數名稱。
**嚴禁猜測參數名稱** — 名稱會因語言和專案樣版而異。

選用 Phase 2.5：`get_field_values` → 取得參數值分佈（有助於設定篩選條件）。

### Phase 3：擷取
`query_elements_with_filter` → 支援多重條件篩選查詢。
- `field` 必須使用 Phase 2 取得的名稱
- 運算子：`equals`、`contains`、`less_than`、`greater_than`、`not_equals`
- 單位通常為 mm

## Visualization

查詢後上色標記結果：
1. `override_element_graphics` → 設定填充色、線條色、透明度
2. `clear_element_override` → 恢復預設顯示

## Quick Reference

```
簡單查詢：      Phase 1 → Phase 3
篩選查詢：      Phase 1 → Phase 2 → Phase 3
值分佈探索：    Phase 1 → Phase 2 → Phase 2.5 → Phase 3
含上色標記：    Phase 1 → Phase 2 → Phase 3 → override_element_graphics
```

## Helper Tools

| 工具 | 用途 |
|------|------|
| `get_wall_types` | 列出牆體類型（支援搜尋篩選） |
| `change_element_type` | 依 ID 變更元素類型（2023+ 限定） |
| `list_family_symbols` | 瀏覽族群符號（支援名稱篩選） |
| `get_line_styles` | 列出可用線條樣式 |

## Common Scenarios

- **房間邊界檢查**：詳見 `domain/room-boundary.md`
- **牆體檢查**：詳見 `domain/wall-check.md`

## Reference

詳見 `domain/element-query-workflow.md`、`domain/element-coloring-workflow.md`、`domain/room-boundary.md`、`domain/wall-check.md`。
