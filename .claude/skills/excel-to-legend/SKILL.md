---
name: excel-to-legend
description: 將 Excel 中的 worksheet（或 named table）批次繪製到 Revit Legend 視圖，忠實還原合併儲存格與框線粗細。觸發條件：使用者提到 excel 表格、excel to legend、legend 建立、圖例表格、批次 legend、表格匯入 legend。工具：read_excel_tables、create_legends、create_detail_lines、create_text_note。
---

# Excel Table → Revit Legend 批次建立

## 適用場景

使用者有一份 `.xlsx`，含多個工作表（如 `A01`、`A02`），希望在 Revit 中：
- 為每個工作表建立同名的 Legend 視圖
- 把列印範圍內的儲存格內容（文字 + 框線 + 合併）繪製到對應 legend 中
- 框線粗細與 Excel 完全一致（細線 / 中等 / 粗）

## 前置條件

1. **Revit 專案/樣板中需有 seed legend**，建議命名 `_SEED_BLANK`
2. **每個目標 worksheet 必須設定列印範圍 (Page Setup → Print Area)**
   - 沒設的會被略過（warning）
   - 列印範圍可包含「純框線欄/列」（無內容但有粗外框），這正是常見表格設計
3. Revit 線型 `<Thin Lines>` / `<Medium Lines>` / `<Wide Lines>` 必須存在（樣板預設都有）

## SOP

### 步驟 1：讀取 Excel
呼叫 `read_excel_tables({ filePath, mode: "worksheets", tableNames? })`：
- **務必使用 `mode: "worksheets"`**（而非 named_table）— 此模式會以列印範圍為 source range，正確包含純框線欄/列
- 回傳每個 table：
  - `rows[r][c]`：儲存格文字（合併儲存格的非左上角為空）
  - `colCount` / `rowCount`：列印範圍尺寸
  - `merges[]`：`[r1, c1, r2, c2]` 0-based 相對座標
  - `borders[r][c]`：`[top, right, bottom, left]` 字串值（None / Thin / Medium / Thick / Hair / Dotted ...）

> 若 `Warnings` 非空，告知使用者哪些 worksheet 沒列印範圍。

### 步驟 2：批次建立 Legend
呼叫 `create_legends({ names: tables.map(t => t.name) })`：
- 預設用 `_SEED_BLANK` 為 seed、`duplicateMode: "withDetailing"`
- **找不到 seed legend → 立即中止**並要求使用者建立

### 步驟 3：座標系統與比例（極重要）

Legend 預設比例 1:48（檢查實際 view scale），座標單位有兩套：
- **detail line 與 text note 的 x/y 用「model mm」**
- **textSize 用「paper mm」**

預設儲存格大小（紙面）：`paperColW = 30 mm`、`paperRowH = 8 mm`
- Model 座標：`modelW = paperColW * scale`（30×48 = 1440）、`modelH = paperRowH * scale`（8×48 = 384）
- 第 (r, c) 格中心 = `((c + 0.5) * modelW, -(r + 0.5) * modelH)`
- 文字大小：`textSize: 2`（paper mm，約對應紙面 2mm）

### 步驟 4：產生框線（severity-aware merging）

對每條候選邊，計算其「嚴重度」（None=0, Thin=1, Medium=2, Thick=3），同一直線上連續同嚴重度的格邊合併成一條 detail line：

```
SEV = {None:0, Thin:1, Hair:1, Dotted:1, Dashed:1, Medium:2, Double:2, Thick:3}
STYLE = {1: "<Thin Lines>", 2: "<Medium Lines>", 3: "<Wide Lines>"}
```

**水平邊**（橫線，介於 row r-1 與 r 之間，r ∈ [0, rowCount]）：
- 內部邊（0 < r < rowCount）：對每個 col c 取 `max(borders[r-1][c].bottom, borders[r][c].top)`
- 跳過合併內部：若有 merge `[mr1, mc1, mr2, mc2]` 滿足 `mr1 ≤ r-1 ∧ mr2 ≥ r ∧ mc1 ≤ c ≤ mc2`，該段嚴重度設為 0
- 邊界邊（r=0 或 r=rowCount）：取對應一側的 top 或 bottom

**垂直邊**（縱線，介於 col c-1 與 c 之間）：對稱處理。

**合併連續段**：依 col 掃描時，連續同嚴重度（>0）的段落輸出為一條線；嚴重度改變或為 0 時切斷。

完成後 `create_detail_lines({ viewId, lines: [{ startX, startY, endX, endY, lineStyle }, ...] })` 一次送出。

### 步驟 5：填入文字

對每個非空 `rows[r][c]`：
- 跳過合併儲存格的「被覆蓋格」（merge 的非左上角）— `read_excel_tables` 已將其文字置空，自然會跳過
- **合併儲存格的左上角文字應置於合併區域中心**：
  - 若 (r, c) 是某 merge `[mr1, mc1, mr2, mc2]` 的左上角（`r==mr1 ∧ c==mc1`），中心 = `(((mc1+mc2+1)/2) * modelW, -((mr1+mr2+1)/2) * modelH)`
  - 否則用一般 cell 中心
- 呼叫 `create_text_note({ viewId, x, y, text, textSize: 2 })`

### 步驟 6：回報
總結每個 legend 的 ElementId、繪製的線段數、文字數。

## 重要陷阱與經驗

1. **`mode: "worksheet"` 才會用列印範圍當 source**（而非 ClosedXML 的 RangeUsed）。RangeUsed 會跳過僅含邊框、無內容的純框線欄，導致外框丟失。
2. **不要相信 ClosedXML 的 `cell.Style.Border` 對「相鄰格反向邊」的處理** — 邊框可能只設在外框欄（C/I），需用 print area 全範圍掃描。
3. **legend 比例 1:48** — 若使用者改了 view scale，model 座標必須對應調整（`scale = legendView.scale`）。
4. **textSize 是紙面 mm**，給太大值會看起來巨大；2~3 mm 為常用值。
5. **合併儲存格的內部邊不要畫**，否則合併效果消失。
6. **空儲存格也可能有粗邊框** — 純框線欄/列就是這種情況。
7. **舊版 ClosedXML 0.102.3 對某些 .xlsx 解析有 bug**，本專案已升級到 0.104.2（見 `MCP/RevitMCP.csproj`）。

## 工具速查

| 工具 | 來源 | 用途 |
|------|------|------|
| `read_excel_tables` | `CommandExecutor.Legend.cs` | 讀 Excel + 列印範圍裁切 + 合併/邊框 |
| `create_legends` | `CommandExecutor.Legend.cs` | 從 seed 批次複製 legend |
| `create_detail_lines` | `CommandExecutor.SmokeExhaust.cs` | 一次畫多條 detail line（支援 lineStyle） |
| `create_text_note` | `CommandExecutor.SmokeExhaust.cs` | 建立文字標註（textSize = paper mm） |

## 進階（v2，未實作）

- 從 `_SEED_DOOR_WINDOW` 衍生 legend，用 `BuiltInParameter.LEGEND_COMPONENT*` 改寫 placed legend component（門窗一覽表）
- 從 Excel 讀字型/粗體/背景色並映射到 Revit
- 自動偵測 view scale 並計算 model 座標
