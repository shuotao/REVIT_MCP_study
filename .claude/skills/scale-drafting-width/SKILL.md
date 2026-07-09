---
name: scale-drafting-width
description: "DraftingView 表格縮放：支援寬度（X 軸）和行高（Y 軸）獨立縮放。寬度以左邊緣為錨點，行高以上邊緣為錨點，將 DetailCurve 和 TextNote 座標按比例縮放。適用於 Excel 匯入後微調表格欄寬、行高、統一多張表格的視覺比例。觸發條件：使用者提到縮放、scale、表格寬度、欄寬調整、行高、列高、縮小表格、放大表格、scale width、scale height、drafting view 寬度、drafting view 高度。工具：scale_drafting_view_width、scale_drafting_view_height。"
---

# DraftingView 表格縮放（寬度 / 行高）

支援 X 軸（寬度）和 Y 軸（行高）獨立縮放。

## Available Tools

| 工具 | 用途 |
|------|------|
| `scale_drafting_view_width` | 縮放寬度（僅 X 軸），以左邊緣為錨點 |
| `scale_drafting_view_height` | 縮放行高（僅 Y 軸），以上邊緣為錨點 |

## Workflow

### Step 1：確認目標圖紙

確認使用者要縮放的圖紙。若未指定，工具預設使用當前作用圖紙。

### Step 2：執行縮放

**寬度縮放（X 軸）：**
```
scale_drafting_view_width({
  scaleFactor: 0.9,        // 0.9 = 縮小到 90%，1.1 = 放大到 110%
  sheetId: 目標圖紙ID,      // 選填，不指定則用當前圖紙
  viewNames: ["view1"]     // 選填，不指定則處理圖紙上所有 DraftingView
})
```

**行高縮放（Y 軸）：**
```
scale_drafting_view_height({
  scaleFactor: 1.08,       // 1.08 = 放大到 108%，0.9 = 縮小到 90%
  sheetId: 目標圖紙ID,      // 選填，不指定則用當前圖紙
  viewNames: ["view1"]     // 選填，不指定則處理圖紙上所有 DraftingView
})
```

兩者可獨立使用，也可組合使用（先寬度再行高，或反過來）。

### Step 3：重新排列視埠

縮放後視埠尺寸改變，間距可能不對。提醒使用者是否需要重新排列：

```
arrange_viewports_on_sheet({
  viewNames: [...],
  direction: "horizontal",
  gapMm: -5,
  alignY: "top"
})
```

## Parameters

### `scale_drafting_view_width` / `scale_drafting_view_height`

兩個工具的參數完全相同：

| 參數 | 說明 | 預設 |
|------|------|------|
| `scaleFactor` | 縮放比例（0.9=縮小到90%，1.1=放大到110%） | width: `0.9` / height: `1.1` |
| `viewNames` | 只處理指定名稱的 DraftingView（選填） | 圖紙上所有 DraftingView |
| `sheetId` | 圖紙 Element ID（選填） | 當前作用圖紙 |

## 縮放機制

### 寬度（X 軸）
- **錨點**：每個 view 的左邊緣（最小 X 座標）
- **同步縮放**：DetailCurve 和 TextNote 的 X 座標
- **TextNote wrapping 寬度**也會等比縮放（`tn.Width *= scaleFactor`）

### 行高（Y 軸）
- **錨點**：每個 view 的上邊緣（最大 Y 座標）
- **同步縮放**：DetailCurve 和 TextNote 的 Y 座標
- TextNote 寬度不變（僅移動 Y 位置）

兩者皆採**逐 View 獨立 Transaction**，大量 view（20+）不會卡死 Revit。

## Notes

- 大量 view（20+）總耗時約 2 分鐘，MCP client 端可能 timeout，但 Revit 全程保持回應
- Timeout 不代表失敗，確認方式：`tail "$APPDATA/RevitMCP/Logs/RevitMCP_YYYYMMDD.log"`，看到 `[ScaleWidth] 完成` 或 `[ScaleHeight] 完成` 即成功
- 可用 Ctrl+Z 逐 view 復原
- 縮放後通常需要搭配 `arrange_viewports_on_sheet` 重新排列視埠
