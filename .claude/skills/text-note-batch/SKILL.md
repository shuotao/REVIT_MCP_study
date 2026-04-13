---
name: text-note-batch
description: "TextNote 批次操作：在多個 DraftingView 中依文字內容搜尋 TextNote 並批次平移。適用於跨圖面統一調整法規文字、表格附註等重複出現的 TextNote 位置。觸發條件：使用者提到批次移動 TextNote、text note、跨視圖移動文字、批次文字調整、move text notes、batch text。工具：move_text_notes_in_views。"
---

# TextNote 批次操作

在多個 DraftingView 中依文字內容搜尋 TextNote 並批次平移。

## Available Tools

| 工具 | 用途 |
|------|------|
| `move_text_notes_in_views` | 依文字子字串搜尋 TextNote，跨 DraftingView 批次平移 |

## Workflow：批次移動 TextNote

### Step 1：確認目標文字

從使用者的截圖或描述中辨識要移動的 TextNote 內容，擷取一段**獨特的子字串**作為 `textMatch`。

- 選擇足夠長的子字串以避免誤命中
- 例如：使用者選了「依內政部101年07月23日台內營字第1010806554號...」→ 用 `"依內政部101年07月23日"` 作為 textMatch

### Step 2：dryRun 預覽

先用 `dryRun: true` 確認搜尋結果正確：

```
move_text_notes_in_views(
  textMatch: "目標文字子字串",
  deltaYMm: 5,
  dryRun: true
)
```

- 確認匹配的 view 數和 TextNote 數是否符合預期
- 若匹配過多，調整 textMatch 或加 viewNames 限縮範圍

### Step 3：執行移動

確認無誤後，去掉 dryRun 執行：

```
move_text_notes_in_views(
  textMatch: "目標文字子字串",
  deltaYMm: 5
)
```

### Step 4（選填）：限定特定 view

若只需要在部分 view 中操作：

```
move_text_notes_in_views(
  textMatch: "目標文字子字串",
  deltaYMm: 5,
  viewNames: ["面積比較表_A01", "面積比較表_A02"]
)
```

## Parameters

### `move_text_notes_in_views`

| 參數 | 說明 | 預設 |
|------|------|------|
| `textMatch` | TextNote 文字內容子字串（不區分大小寫） | **必填** |
| `deltaXMm` | 水平位移（mm），正=右、負=左 | `0` |
| `deltaYMm` | 垂直位移（mm），正=上、負=下 | `0` |
| `viewNames` | 限定搜尋的 DraftingView 名稱清單（選填） | 全部 DraftingView |
| `dryRun` | `true`=僅搜尋不移動 | `false` |

## Notes

- 搜尋範圍為所有 DraftingView（不含 template）
- 每個 view 開獨立 Transaction，可個別 Undo
- 子字串比對不區分大小寫
- 位移單位為 mm（內部自動轉換為 feet）
- 若 `deltaXMm` 和 `deltaYMm` 都為 0 且非 dryRun，會回報錯誤
