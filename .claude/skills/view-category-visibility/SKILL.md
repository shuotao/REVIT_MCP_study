---
name: view-category-visibility
description: "視圖類別可見性控制：在指定視圖中隱藏或顯示整個類別（含連結模型），支援批次多視圖操作。觸發條件：使用者提到隱藏、顯示、hide、unhide、show、類別可見性、category visibility、隱藏樹、隱藏植栽、隱藏傢俱、Planting、連結模型隱藏。工具：set_category_visibility、hide_elements、unhide_elements、get_all_views、get_active_view。"
---

# 視圖類別可見性控制

## 使用場景

- 隱藏/顯示特定類別（如植栽、傢俱、管線）以簡化視圖
- 連結模型中的元素無法用 `hide_elements` 單獨隱藏，必須用 `set_category_visibility` 整類別控制
- 批次對多個視圖套用相同的可見性設定

## 工具選擇指南

| 情境 | 工具 | 說明 |
|------|------|------|
| 隱藏整個類別（含連結模型） | `set_category_visibility` | 主模型 + 連結模型一起生效 |
| 隱藏主模型中的特定元素 | `hide_elements` | 依 ElementId 精確控制 |
| 取消隱藏特定元素 | `unhide_elements` | 依 ElementId 精確還原 |

> **關鍵判斷**：如果目標元素在 **連結模型（Revit Links）** 中，必須使用 `set_category_visibility`，因為連結模型元素沒有主模型中的 ElementId。

## Workflow

### 步驟 1：確認目標視圖

- 單一視圖：`get_active_view` 取得當前視圖 ID
- 多視圖：`get_all_views` 列出所有視圖，依名稱篩選目標視圖

### 步驟 2：確認類別名稱

常用類別名稱對照：

| 中文 | Revit 類別名稱 |
|------|---------------|
| 植栽/樹 | `Planting` |
| 傢俱 | `Furniture` |
| 門 | `Doors` |
| 窗 | `Windows` |
| 牆 | `Walls` |
| 樓板 | `Floors` |
| 柱 | `Columns` |
| 管線 | `Pipes` |
| 風管 | `Ducts` |
| 電纜架 | `Cable Trays` |
| 場地 | `Site` |
| 地形 | `Topography` |

### 步驟 3：執行隱藏/顯示

```
# 隱藏類別
set_category_visibility(category: "Planting", hidden: true, viewId: <視圖ID>)

# 顯示類別
set_category_visibility(category: "Planting", hidden: false, viewId: <視圖ID>)
```

批次多視圖時，**並行呼叫**以提高效率（每個視圖一次 tool call）。

### 步驟 4：確認結果

回報每個視圖的操作結果，格式：

| 視圖 | 類別 | 狀態 |
|------|------|------|
| 視圖名稱 | 類別名稱 | 已隱藏/已顯示 |

## 注意事項

1. **視圖樣版限制**：若視圖套用了視圖樣版（View Template）且樣版控制了類別可見性，`set_category_visibility` 可能無效。需先解除樣版或修改樣版設定。
2. **可逆操作**：`hidden: true` ↔ `hidden: false` 互為反操作，隨時可還原。
3. **連結模型 vs 主模型**：`set_category_visibility` 對兩者同時生效，無法分別控制。若需只隱藏連結模型的類別，目前需透過 Revit UI 的「連結模型顯示設定」。
4. **WebSocket 偶發錯誤**：若批次操作時出現空錯誤，逐一重試即可（通常是連線瞬斷）。

## Reference

詳見 `domain/tool-capability-boundary.md`。
