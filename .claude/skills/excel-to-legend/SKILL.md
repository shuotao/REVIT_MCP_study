---
name: excel-to-legend
description: 將 Excel 中的 worksheet 批次匯入 Revit Drafting View，忠實還原合併儲存格、表格框線、Excel 原始 column/row 尺寸與長文字 wrap 行為，並在覆蓋舊 view 時自動還原所有 sheet 上的 viewport 位置。觸發條件：使用者提到 excel 表格、excel to drafting、excel to legend、表格匯入 revit、批次匯入 excel、面積計算表匯入。**永遠優先使用 `import_excel_to_drafting_views` 一站式命令**——不要先呼叫 `read_excel_tables` 再 chain `create_legends`，那是過時的多步驟模式，已被一站式命令取代。`create_legends` / `read_excel_tables` 僅在「需要 Legend View 而非 Drafting View」或「純讀取 Excel 結構不建 view」這兩種特殊情況下才使用。
---

# Excel → Revit Drafting Views（預設流程）

## 一站式：`import_excel_to_drafting_views`

對 99% 的場景，只需呼叫一次：

```
import_excel_to_drafting_views({
  filePath: "C:\\...\\面積計算表.xlsx",
  sheets: ["A01","A02"],            // 選填，不給=全部有列印範圍的 sheet
  namingPattern: "面積計算表_{name}", // {name} 取代為 sheet 名
  overwrite: true                    // 同名 view 已存在則刪除重建
})
```

C# 內部會：
1. 讀 Excel（用列印範圍裁切），同時擷取每個 column 的寬度（characters）與 row 高度（points）
2. 對每張 sheet 建立 Drafting View（不需 seed）
3. 計算 layout：將 Excel column/row 尺寸 ×EXCEL_SCALE，再依 wrap 行數補強 row 高
4. merge-aware 畫 detail lines（同方向連續段合併、跳過合併內部邊、套 `<Thin Lines>`）
5. 將每個非空 cell 寫成 TextNote 並 `Width = mergedCellWidth`，使長字串在 cell 內自動 wrap
6. 文字垂直置中（補上 `+textHeight/2` 偏移）、水平 `HorizontalAlignment.Center`
7. 自動正規化 `\r\n → \n`（Revit `TextNote.Create` 不接受 `\r\n`）

**固定常數**（不開放參數，避免 token 浪費；可在 `CommandExecutor.Legend.cs` 內微調）：
- `VIEW_SCALE = 1`（1:1，紙面 mm = model mm）
- `TEXT_SIZE_MM = 2.3`
- `LINE_H_MM = TEXT_SIZE_MM × 1.4 = 3.22 mm/行`
- `EXCEL_SCALE = 0.92`（配合 2.3mm 文字等比縮小）
- `MIN_ROW_H_MM = 3.8`，`MIN_COL_W_MM = 3.8`（保護隱藏列退化）

**前置條件**：
- worksheet 必須有列印範圍（沒設的會 warning 略過、Total=0、什麼都不會建）
- 不需 seed view

**自動補列印範圍（Auto Print Area）**：
若 `import_excel_to_drafting_views` 回傳結果中 `Total == 0` 且 Warnings 包含「沒有設定列印範圍」，
**不要問使用者，直接執行以下 Python 為所有缺少 print area 的 worksheet 補上 UsedRange 範圍，然後重新呼叫 import**：

```python
from openpyxl import load_workbook
from openpyxl.utils import get_column_letter
fp = r"<filePath>"  # 用使用者提供的原始路徑
wb = load_workbook(fp)
for name in wb.sheetnames:
    ws = wb[name]
    if not ws.print_area and ws.max_row and ws.max_column:
        ws.print_area = f"A1:{get_column_letter(ws.max_column)}{ws.max_row}"
wb.save(fp)
```

流程：
1. 第一次呼叫 `import_excel_to_drafting_views`
2. 若回傳 Warnings 含「沒有設定列印範圍」→ 用上方 Python 自動補 print area
3. 第二次呼叫 `import_excel_to_drafting_views`（相同參數）
4. 若仍失敗才回報使用者

注意：`get_column_letter` 是必要的（合併儲存格 A1 不能用 `.column_letter`）。

**回應**（精簡）：`{Total, Created:[{name,viewId,lines,texts}], Skipped, Errors, Warnings, ViewportsRestored, ViewportFailures}`

**匯入後互動：表格寬度縮放**

匯入完成且確認成功後，主動詢問使用者：
> 「表格已匯入完成。是否需要調整表格寬度比例？（例如縮小到 0.9 倍讓欄位更緊湊）」

若使用者需要，使用 `scale_drafting_view_width`：
```
scale_drafting_view_width({
  scaleFactor: 0.9,        // 依使用者指定的比例
  sheetId: 目標圖紙ID      // 選填，不指定則用當前圖紙
})
```
- 僅縮放 X 軸（寬度），高度不變
- 以每個 view 的左邊緣為錨點
- DetailCurve（表格線）和 TextNote（文字）同步縮放
- TextNote 的 wrapping 寬度也會等比縮放
- 內部採**逐 View 獨立 Transaction**，大量 view（20+）不會卡死 Revit
- 縮放後可能需要重新排列視埠（用 `arrange_viewports_on_sheet`）

**Viewport 自動還原**：overwrite 模式下，C# 會在 `doc.Delete(oldView)` 之前先掃描所有 sheet，
擷取舊 view 對應的 viewport 中心點（`vp.GetBoxCenter()`，紙面座標），
重建新 view 後在同一個 Transaction 內呼叫 `Viewport.Create(doc, sheetId, newViewId, center)` 放回原位。
使用者不需手動補放。`ViewportsRestored` 是成功還原的 viewport 數量，
`ViewportFailures` 列出個別失敗（例如 sheet 已被刪除、view 內容變大導致 Viewport.Create 拋例外等）。
個別 viewport 失敗不會 rollback 整批 Commit。

**Token 預估**：25 sheet 全匯入 ≈ 1.5 K token。比舊的多步驟流程省 95%+。

## 設計筆記：layout 演算法

匯入工具歷經數次迭代才得到正確的視覺呈現，以下是關鍵設計決策。

### 1. cell 尺寸：直接用 Excel column/row 值

舊方案是 auto-sizing（依文字內容估算最寬欄）→ 失敗，因為：
- 邊界 cell 會把欄寬剛好卡死，導致 `TextNote.Width` 觸發 mid-string wrap（例：`55.910` 被切成 `55.9` / `1` / `0`）
- 不符合使用者在 Excel 設計表格時的視覺意圖

正確做法：**讀 `ws.Column(c).Width`（characters）與 `ws.Row(r).Height`（points），轉成 mm 後 ×EXCEL_SCALE**。
- chars → mm：`(chars × 7 + 5) × 25.4 / 96`（假設 96 DPI、Calibri 11pt 約 7px/char）
- points → mm：`points × 25.4 / 72`

`EXCEL_SCALE = 0.92` 是配合 2.3mm 文字的經驗值：從原始 1.2（搭配 3.0mm 文字）等比縮小，讓表格與文字比例協調。

### 2. 文字 wrap：`TextNote.Width = mergedCellWidth`（不扣 padding）

設定 `Width` 後 Revit 會把超過寬度的字串自動 wrap 到下一行。**不要扣 padding**，因為 Excel 原始欄寬已隱含 padding，扣了會觸發不必要的 wrap。

長字串如 `屏東縣東港鎮大埔段939-25地號等1筆地號` 會自然 wrap；短字串如 `55.910` 則保持單行。

### 3. row 高自動補強

Excel 的 row height 是針對 11pt 字型設計的，當文字 wrap 成多行時，scaled row height 可能不夠。在 layout pass 加一段：

```csharp
int wrapLines = CountWrappedLines(text, mergedW, TEXT_SIZE_MM);
if (wrapLines > 1) {
    double need = wrapLines * LINE_H_MM + 1.5;
    int rowsSpan = mr2 - mr1 + 1;
    double perRow = need / rowsSpan;
    for (int rr = mr1; rr <= mr2; rr++)
        if (rowH[rr] < perRow) rowH[rr] = perRow;
}
```

### 4. 文字垂直置中：`+textHeight/2` 偏移

`TextNote.Create()` 的 `position` 是文字框「上邊緣」（Revit Y+ = up，故上邊緣 = 較大 Y）。若直接放在 cell 中線，文字會落到下半部。

正確做法：
```csharp
double cy = (top + bottom) / 2.0;
int wrapLines = CountWrappedLines(text, mergedW, TEXT_SIZE_MM);
double textH = wrapLines * LINE_H_MM;
double yInsert = cy + textH / 2.0;  // 上邊緣 = 中線 + 半個文字高度
```

### 5. CJK 字寬估算

Revit API 無法直接量測字串寬度，必須估算：
- CJK／全形（U+3000–9FFF, AC00–D7AF, FF00–FFEF）：`width ≈ textSize`
- ASCII／半形：`width ≈ textSize × 0.55`

`CountWrappedLines(text, cellWidth, textSize)` 用此公式累加 char width，遇到 `\n` 或超過 `cellWidth` 就換行。

### 6. `\r\n` 必須先正規化為 `\n`

`TextNote.Create()` 對 `\r\n` 會 throw（在 `text.Replace` 之前的版本曾整批失敗）。匯入流程的最早步驟就要：
```csharp
text = text.Replace("\r\n", "\n").Replace("\r", "\n");
```

### 7. Drafting View vs Legend

我們選 Drafting View 而非 Legend，因為：
- Drafting View 可由 `ViewDrafting.Create()` 直接建立，**不需 seed**
- Legend 必須從現有 legend 複製，且依賴使用者準備 `_SEED_BLANK`
- 兩者對 detail lines / text notes 的 API 完全相同

---

# 進階：手動多步驟模式（特殊需求才用）

僅在以下情境才用舊流程（需自行組裝多個 tool 呼叫）：
- 需要 Legend View 而非 Drafting View（Legend 可在多 sheet 重複放置同份內容）
- 需要保留 Excel 線粗細（Thin/Medium/Thick）— 一站式版本一律 `<Thin Lines>`
- 需要自訂 cell 尺寸或 textSize（一站式版本固定）
- 需要對單一 sheet 跑 Excel 內含的特殊樣式（背景色、字型）

對應工具：
- `read_excel_tables({ mode: "worksheet", tableNames, includeBorders, summary })` — 讀 Excel
- `create_legends({ names })` — 從 `_SEED_BLANK` 批次複製 legend（一站式版本不會用到）
- `create_detail_lines({ viewId, lines })` — 批次畫線
- `create_text_notes({ viewId, textSize, notes })` — **批次**寫文字（**永遠不要**用單筆 `create_text_note`，會炸 token）

手動模式的 layout 原則：套用上面「設計筆記」的同樣邏輯（垂直置中偏移、`\r\n → \n`、`Width = mergedCellWidth`、CJK 寬度估算等）。

## 重要陷阱與經驗（兩種模式共通）

1. **永遠先用 `summary: true` 探索**，再分批拉細節——對 25 sheet 直接全讀會回傳幾百 KB
2. **`mode: "worksheet"` 才會用列印範圍當 source**（而非 ClosedXML 的 RangeUsed）。RangeUsed 會跳過僅含邊框、無內容的純框線欄
3. **`\r\n` 必須正規化為 `\n`**——`TextNote.Create()` 對 `\r\n` 會 throw
4. **合併儲存格的內部邊不要畫**，否則合併效果消失
5. **長文字 wrap**：靠 `note.Width = mergedCellWidth(feet)`，不要扣 padding
6. **垂直置中**：`yInsert = cellCenterY + textHeight/2`（Revit Y+ = up，position 是文字框上邊緣）
7. **CJK 字寬估算**：CJK ≈ textSize；ASCII ≈ textSize × 0.55
8. **舊版 ClosedXML 0.102.3** 對某些 .xlsx 解析有 bug，本專案已升級到 0.104.2
9. **Active view = 即將被覆蓋的同名 view 才會踩雷**：Revit 不允許 `doc.Delete()` 當前 active view。判斷標準是「active view 的名字是否落在這次 import 的目標清單裡」——若 active view 是某張 sheet（即使該 sheet 上放著要被覆蓋的 viewport），或是任何不在 namingPattern 對應名單內的 view，**都不會踩雷**（實測 active view = 放著 5 張面積計算表 viewport 的 sheet，22 sheet 全部成功覆蓋 + viewport 自動還原）。只有當 active view 名字 = `namingPattern` 套用後的某個 viewName 時，才需要先 `set_active_view` 切走，否則該 sheet 會被外層 catch 歸到 `Errors`。
10. **Viewport 自動還原（已內建）**：`doc.Delete(view)` 會連帶刪除該 view 在 sheet 上的 viewport，位置永久遺失。本工具在 delete 之前已先擷取所有相關 viewport 的中心點，重建後自動 `Viewport.Create()` 放回原位（見 `ViewportsRestored` 計數）。但 viewport 用的是「擷取時的中心點」，若新 view 內容尺寸大幅改變，可視範圍可能裁掉部分內容，必要時手動調整 viewport 邊界。
11. **Bulk import 必 timeout，要靠 log 確認**：22 sheet + 5 viewport restore 約需 **7 分鐘**單一 Transaction，MCP client 端必定 timeout（看到 `Error: Command timed out` 是預期的，不是失敗）。確認方式：`tail "$APPDATA/RevitMCP/Logs/RevitMCP_YYYYMMDD.log"`，找到對應 RequestId 出現「已發送回應」就表示完成。完成後再呼叫 `get_viewport_map` 或 `get_all_views` 驗證結果，**不要重複呼叫 import**（會疊一個新請求進佇列、再多等 7 分鐘）。
12. **`scale_drafting_view_width` 大量 view 必 timeout 但不會卡死**：已改為逐 View 獨立 Transaction（每個 view ~300-500 元素，commit 約 5 秒）。23 個 view 總耗時約 2 分鐘，MCP client 端會 timeout 但 Revit UI 全程保持回應。確認方式同上：`tail "$APPDATA/RevitMCP/Logs/RevitMCP_YYYYMMDD.log"`，看到 `[ScaleWidth] 完成，共處理 N 個 view` 就表示成功。**舊版（單一 Transaction 包全部 view）會導致 Revit 凍結，已於 2026-04-11 修復。**

## Token 預算參考

- **一站式 `import_excel_to_drafting_views`**：25 sheet ≈ 1.5 K token（內部完成全部 layout/wrap 邏輯）
- 手動多步驟：1 sheet 約 30 cells ≈ 6 KB；25 sheets ≈ 150 KB
- 比節省前的單筆 `create_text_note` 流程（~5 MB）省 95%+
