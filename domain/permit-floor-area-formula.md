---
name: permit-floor-area-formula
description: "送照樓板面積計算式 SOP：把使用者已切分好的封閉區域（矩形／三角形）轉成建照送審圖面上的面積計算式文字，並與實際幾何面積對帳。當使用者提到送照面積計算式、樓板面積算式、面積計算式、create_floor_area_formula 時觸發。本文件不定義「哪些面積該計入」，那是使用者畫邊界時的法規判斷。"
metadata:
  version: "1.0"
  updated: "2026-09-07"
  created: "2026-09-07"
  references: []  # 刻意留空：本工具不做法規判定，見「不做什麼」章節
  related:
    - floor-area-review.md
    - room-boundary.md
    - sequence-numbering.md
  referenced_by: []
  tags: [送照, 建照, 樓板面積, 面積計算式, floor area formula, create_floor_area_formula, 區域邊界線, 對帳]
---

# 送照樓板面積計算式

## 1. 目的

建照送審圖上要逐塊列出樓地板面積的計算式（例如 `10.20×8.40 = 85.68`），是純手工、易錯、且改一次尺寸就要全部重算的作業。本工具把「量測 → 四捨五入 → 列式 → 加總」自動化，並且**每一項都與實際幾何面積對帳**。

對應工具：`create_floor_area_formula`
實作：`MCP/Core/Commands/CommandExecutor.AreaFormula.cs`

## 2. 不做什麼（先看這段）

- **不判定哪些面積該計入送審**。陽台折半、屋突免計、法定騎樓扣除這類判斷，全部發生在使用者「畫區域邊界線」的那一步。工具只計算你圍出來的東西。
- **不自行分解多邊形**。L 形、凹形、有弧邊的區域一律列入 `Undecomposed` 並跳過，不會自作主張切一種切法。送審圖上的切法是要能被審查員看懂的，那是人的決定。
- **不引用法規條號**。本文件 `references` 刻意留空，因為工具沒有任何法規判斷可以被引用。需要容積／樓地板面積的法規檢討流程，看 `floor-area-review.md`。

## 3. 前置作業（使用者要先做的）

1. 在**建地平面視圖**（Area Plan）或樓板平面視圖開啟作業。
2. 用「建築 → 區域邊界 → 線」或房間分隔線，把要計算的範圍圍出外框。
3. **把外框手動切成矩形與三角形**：
   - 正交的部分切成矩形。
   - 斜邊的部分切成三角形（非直角四邊形要再切成兩個三角形）。
4. 每個封閉區域放置 Area（或 Room）。

第 3 步是這套流程的核心前提。工具會檢查你切得對不對，但不會替你切。

## 4. 作業流程

1. **Dry-run 先看**：`create_floor_area_formula` 帶 `dryRun: true`，確認：
   - `Count` 與你切出來的區域數量相同（**這一步是防「比對了 0 個項目」的關卡**）
   - `Undecomposed` 是空的；若不是，照它的 `Reason` 回去補切
   - `Mismatches` 是空的
2. **確認算式文字**：看回傳的 `FormulaText`，確認項目順序、編號、合計。
3. **正式寫入**：帶 `x` / `y`（mm，專案座標）與 `dryRun: false`，在圖面上產生 TextNote。

## 5. 判形規則

| 化簡後頂點數 | 判定 | 算式 |
|---|---|---|
| 3 | 三角形 | `底×高÷2`。有一個內角落在 90° ± `angleToleranceDeg` 內（直角三角形）：直接取夾直角的兩股當底與高；其他三角形：底取最長邊、高由 `2×面積÷底` 反算 |
| 4 且四角皆直角（±`angleToleranceDeg`，預設 1°）| 矩形 | `長×寬` |
| 4 但有非直角 | Undecomposed | 回報「請再切成矩形與三角形」 |
| 其他 | Undecomposed | 回報實際頂點數 |

**共線點必須先化簡**：一道被切成兩段的牆會產生兩個 `BoundarySegment`，不化簡的話一個標準矩形會有 5、6 個頂點，直接被誤判成無法分解。化簡容差為 1 mm（重複點與共線偏移各 1 mm）。

**弧邊直接跳過**：邊界含非直線段時沒有「長×寬」可言，列入 `Undecomposed`。

## 6. 四捨五入與對帳（本工具的重點）

送審圖上印出來的算式**必須自洽**：讀者拿計算機按 `10.20×8.40` 要能得到印在旁邊的 `85.68`。所以：

- 算式印的是**四捨五入後的邊長**（`decimals`，送審慣例 2 位）
- 面積是**由四捨五入後的邊長相乘**再四捨五入，不是由幾何面積直接取位
- 一般四捨五入（`MidpointRounding.AwayFromZero`），不是銀行家捨入

代價是算式值與實際幾何面積會有微小差異。工具對每一項計算 `DeltaM2 = |算式值 − shoelace 幾何面積|`：

- `WithinTolerance` 逐項標示
- 超過 `areaToleranceM2`（預設 0.05 m²）列入 `Mismatches`
- `areaToleranceM2` **不接受 0**：四捨五入本身就會產生差異，設 0 會讓每一項都判為不符

差異若明顯超過捨入誤差，通常表示該區域根本不是矩形／三角形（例如四個角有一個是 89.2°、被 1° 容差放過），這時要回去看邊界。

## 7. 來源收集順序

`sourceIds` 有給就照給定順序，沒給就自動收集並依「Y 由上到下分排、同排 X 由左到右」排序（與 `room-numbering-workflow.md` 同一套排序）。自動收集的範圍會在 `SourceScope` 回報，三種可能：

| SourceScope | 意義 |
|---|---|
| `view:areas` | 該視圖內的 Area（`sourceType` 為 auto 或 area 時優先） |
| `view:rooms` | 該視圖內的 Room |
| `level:rooms` | 視圖範圍取不到，退回該視圖所屬樓層的 Room |

**退回這件事一定會回報**，不會靜默換來源 —— 換了來源卻不說，等於回傳一個你以為在算 A 其實在算 B 的結果。

## 8. 邊界取線位置

`boundaryLocation` 預設 `center`（牆心）。若你的區域邊界線本身就是自己畫的分割線而非牆，此參數不影響結果。改成 `finish` 會算到牆面完成面。

**牆心或牆面哪個才是送審要的，本工具不判定** —— 見第 2 節。

## 9. 常見問題

| 症狀 | 原因 | 處理 |
|---|---|---|
| `Count` 比預期少 | 有區域落在 `Undecomposed` | 看每一筆的 `Reason` 與 `VertexCount` |
| 全部落在 Undecomposed 且 VertexCount 是 5、6 | 牆被切段、共線點沒化簡到 | 檢查邊界是否有微小折角（>1 mm 偏移就不算共線） |
| `Mismatches` 有值但 Delta 只有 0.00x | 純捨入誤差 | 正常，可調高 `areaToleranceM2` |
| `Mismatches` 的 Delta 很大 | 該區域不是真的矩形 | 收緊 `angleToleranceDeg` 重跑，回去補切 |
| 建立 TextNote 報找不到文字類型 | 專案沒有 TextNoteType | 先在專案建立一個文字類型 |
| 合計列的 `㎡` 顯示成方框 | 字型缺字 | `areaUnitLabel` 改成 `m2` |
