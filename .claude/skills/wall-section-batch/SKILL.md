# wall-section-batch

## Purpose

批次建立牆面套管剖面視圖的完整 Workflow。先向使用者確認命名前綴與排序方式，再呼叫 `batch_create_wall_sections` 建立剖面，輸出建立結果摘要。

## Trigger

當使用者說：
- 「針對這張視圖建立牆面套管剖面」
- 「批次建立套管剖面」
- 「幫我對有開孔的牆建剖面」

## Workflow

### Step 1 — 詢問命名規則

在呼叫任何工具之前，先向使用者提問：

**問題 1：命名前綴**
> 剖面視圖的命名前綴是什麼？（例如：SEM、牆面套管剖面、或其他自訂文字）

**問題 2：流水號排序方式**
> 流水號要依什麼順序編排？
> - 先左後右、再由下往上（x_then_y）
> - 先下後上、再由左往右（y_then_x）
> - 依建立順序，不重新排列（creation）

等使用者回覆後再繼續。

### Step 2 — 確認當前視圖

呼叫 `get_active_view`，回報視圖名稱與樓層給使用者確認。

### Step 3 — 取得連結模型

呼叫 `get_linked_models`，找出結構模型的 LinkInstanceId（通常為名稱含 ST 或 RC 的連結模型）。
若有多個候選，列出讓使用者確認。

### Step 4 — 批次建立剖面

呼叫 `batch_create_wall_sections`，帶入：
- `wallLinkId`：Step 3 取得的結構連結模型 ID
- `viewNamePrefix`：Step 1 使用者輸入的前綴
- `sequentialNaming: true`
- `sortOrder`：Step 1 使用者選擇的排序方式
- `scale: 50`（預設 1:50）

### Step 5 — 回報結果

以表格或條列式回報：
- 檢查了幾面牆
- 有開孔的牆幾面
- 建立了幾個剖面視圖
- 命名範圍（例如：SEM-001 ～ SEM-043）

提醒使用者：
> 可接續執行剖面排序編號（第二段）或將剖面放置到圖紙（第三段）。

## Notes

- `sequentialNaming` 為 false 時，視圖名稱後綴為牆的 Element ID，適合除錯用途。
- 長牆若因開孔間距 > 3m 被分割，分割的剖面會在流水號後加 `-1`、`-2` 後綴（例如 SEM-007-1、SEM-007-2）。
- 若使用者未指定排序方式，預設使用 `x_then_y`。
