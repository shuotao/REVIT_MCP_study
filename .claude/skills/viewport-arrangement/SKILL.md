---
name: viewport-arrangement
description: "視埠排列與管理：在圖紙上放置、排列、移動 DraftingView 視埠。支援自動放置（從專案查找 view 並放到圖紙）、跨圖紙移動、水平/垂直排列、邊緣對齊、負間距重疊。觸發條件：使用者提到排列視埠、viewport arrangement、排列 draft view、對齊視埠、align viewports、edge-to-edge、視埠位置、viewport layout、放到圖紙、移動視埠、move viewport、表格排列。工具：get_sheet_viewport_details、arrange_viewports_on_sheet、get_all_views、get_viewport_map、get_all_sheets、get_selected_elements、delete_element。"
---

# 視埠排列與管理

在圖紙上放置、排列、移動 DraftingView 視埠。

## Available Tools

| 工具 | 用途 |
|------|------|
| `get_sheet_viewport_details` | 取得圖紙上所有視埠的位置、邊界框、寬高（mm） |
| `arrange_viewports_on_sheet` | 放置 + 排列視埠（自動將未在圖紙上的 view 放上去） |
| `get_all_views` | 列出專案中所有視圖（可篩選 DraftingView） |
| `get_viewport_map` | 查詢 viewport 目前在哪張圖紙上 |
| `get_all_sheets` | 列出所有圖紙（查找目標圖紙 ID） |
| `get_selected_elements` | 取得使用者在 Revit 中選取的元素 |
| `delete_element` | 刪除 viewport（從圖紙移除，view 本身不會刪除） |
| `get_active_view` | 確認當前視圖是否為圖紙 |

## Workflow 1：放置 + 排列（主要流程）

使用者提供 drafting view 名稱清單，工具自動放到圖紙上並排列。
view 若不在圖紙上，會自動建立 viewport（auto-place）。

1. **查詢可用的 drafting view**
   ```
   get_all_views(viewType: "DraftingView") → 列出所有 drafting view 名稱
   ```

2. **使用者提供名稱和排列順序**

3. **一鍵放置 + 排列**
   ```
   arrange_viewports_on_sheet(
     viewNames: ["建照_A01", "變更後_A01", "建照_A02", ...],
     direction: "horizontal",
     gapMm: 0,
     alignY: "top",
     sheetId: 目標圖紙ID（選填，不指定則用當前圖紙）
   )
   ```
   - 已在圖紙上的 → 直接排列
   - 不在圖紙上的 → 自動建立 viewport 再排列
   - 回傳 `PlacedCount`（新放置數）和 `ArrangedCount`（排列總數）

4. **檢查 Warnings**
   - 若 view 已在**其他圖紙**上 → 無法放置，需先用 Workflow 2 移動
   - 若 view 名稱找不到 → warning 回報

## Workflow 2：跨圖紙移動 viewport

view 已經在其他圖紙上，需要先移除再放到目標圖紙。

1. **查找 viewport 在哪張圖紙**
   ```
   get_viewport_map → 找到 ViewportId 和所在的 SheetId
   ```

2. **從舊圖紙移除 viewport**
   ```
   delete_element(elementId: viewportId)  // 刪除 viewport，view 本身保留
   ```

3. **放到目標圖紙並排列**
   ```
   arrange_viewports_on_sheet(
     viewNames: ["view名稱"],
     sheetId: 目標圖紙ID
   )
   ```

## Workflow 3：移動選取的 viewport 到其他圖紙

使用者在 Revit 中選取 viewport，要移到另一張圖紙。

1. **取得選取的 viewport**
   ```
   get_selected_elements → 記下所有 viewport ID
   ```

2. **查找目標圖紙**
   ```
   get_all_sheets → 找到目標圖紙的 SheetId
   ```

3. **用 viewport map 對應 view 名稱**
   ```
   get_viewport_map → 將 ViewportId 對應到 ViewName
   ```

4. **從原圖紙移除**
   ```
   delete_element(elementId: vpId)  // 對每個選取的 viewport
   ```

5. **放到目標圖紙並排列**
   ```
   arrange_viewports_on_sheet(
     viewNames: [對應的 view 名稱清單],
     sheetId: 目標圖紙ID
   )
   ```

## Workflow 4：緊密排列（負間距）

viewport bounding box 包含邊距，`gapMm: 0` 時表格線之間仍有空隙。
使用負間距讓表格邊框線重疊。

```
arrange_viewports_on_sheet(
  viewNames: [...],
  gapMm: -5,       // 負值，讓邊框重疊
  direction: "horizontal",
  alignY: "top"
)
```

- 建議先用 `gapMm: -3` 到 `-5` 測試
- 實際值取決於 viewport type 的邊距設定

## Parameters

### `arrange_viewports_on_sheet`

| 參數 | 說明 | 預設 |
|------|------|------|
| `viewNames` | view 名稱陣列（與 viewportIds 二擇一） | - |
| `viewportIds` | 視埠 ID 陣列（與 viewNames 二擇一） | - |
| `direction` | `"horizontal"` 或 `"vertical"` | `"horizontal"` |
| `gapMm` | 視埠間距（mm），負值可重疊 | `0` |
| `alignY` | 垂直對齊：`"top"` / `"center"` / `"bottom"` | `"center"` |
| `sheetId` | 圖紙 ID（選填，預設用當前圖紙） | 當前圖紙 |

## Notes

- 第一個視埠的左邊緣（水平）或上邊緣（垂直）作為錨點
- 只處理 DraftingView 類型，其他類型自動跳過
- `delete_element` 刪除的是 viewport（圖紙上的參照），view 內容不受影響
- 所有排列操作為單一 Transaction，可用 Revit Undo 還原
- 若當前視圖不是圖紙，可用 `sheetId` 參數直接指定目標圖紙

## Reference

詳見 `domain/sheet-viewport-management.md`。
