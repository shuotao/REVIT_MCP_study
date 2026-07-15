---
name: partition-takeoff
description: "Revit 輕隔間牆數量計算與 CSV 報表更新。觸發條件：使用者提到輕隔間、隔間牆、partition wall takeoff、TYPE 牆、牆面積、扣除門窗開口、物流中心_ALL_輕隔間數量計算、更新既有輕隔間 CSV。"
---

# 輕隔間牆數量計算

## 必讀 SOP

開始計算前先讀 `domain/revit-partition-takeoff.md`，並遵守 Tool Call Data Honesty：本次輸出的房號、ElementId、牆型、長度、高度、面積、開口數量，都必須來自本 turn 的 Revit 工具或可追溯的 CSV 讀取結果。

## 核心流程

1. 以目前 Revit 專案為資料來源，重新查詢 Rooms、Walls、Doors、Windows、Wall Types。
2. 候選輕隔間牆以 Revit 牆類型名稱包含 `TYPE` 為主；若專案有其它命名規則，先查牆類型清單再決定是否納入。
3. 房間順序沿用舊 CSV 範本；沒有舊範本時依房號樓層排序。
4. 牆長分項保留在 `牆長` 欄位，例如 `=1.76+2.56`。
5. `牆高` 欄位優先填可解釋的幾何高度：全高牆為基準樓層到上層樓板底；使用 Revit「貼附頂/底」且幾何高度差異明顯者採貼附後幾何；低矮/襯板/特殊未到頂牆保留 Revit 牆高。
6. 門窗開口按名稱、寬、高分組，輸出數量與扣除公式。
7. TYPE 分欄只列實際存在牆實例的輕隔間類型；只有 Wall Type 定義但無實體牆時不得列出。
8. 表尾新增 `輕隔間數量總計` 列，左側固定資料欄跨欄置中，右側各 TYPE 欄以 `SUM` 加總。

## 開口扣除硬規則

只扣除 host wall 可驗證為本次 TYPE 牆的開口：

- 對每個 Door / Window / Opening 讀 `get_element_info`。
- 從參數取得 `主體 ID` / `Host ID` / `Host Id`。
- 只有 host ElementId 等於本次計算的 TYPE 牆 ElementId 才扣除。
- 不得用最近牆、同樓層距離、房間內座標、BoundingBox 接近等方式推定開口所屬牆。
- host 是非 TYPE 牆、沒有 host、或 host 查不到時，不扣除；必要時列為人工複核。

## 牆高與面積硬規則

報表牆高不得再逐列用 `Revit 面積 / 牆長` 反推。牆高欄是審查者會直接閱讀的施工算量高度，必須可解釋。

一般全高牆：

```text
表內牆高 = 上層樓層標高 - 上層樓板厚度 - 牆基準樓層標高 - 牆基準偏移
```

使用 Revit「貼附頂/底」的牆可作為斜板或特殊頂部條件訊號。若貼附後幾何平均高度與一般樓板底高度差異明顯，可採貼附後 Revit 幾何高度並標記來源；若差異很小，仍用樓層到上層樓板底。

低矮牆、襯板、腰牆或明顯未到頂牆保留 Revit 的 `不連續高度` / 幾何高度。預設可用 2.70m 作為全高門檻；若未使用「貼附頂/底」且 Revit 牆高比樓板底高度低超過約 0.35m，也保留 Revit 牆高。

Revit `面積` 只能作為總量驗證基準。若 `牆長 × 報表牆高 - host 開口` 與 Revit 面積有差，應檢查樓板厚度、特殊未到頂牆、開口扣除、斜板/局部降板或牆段分割；不得把差異藏進牆高欄。

斜板或局部降板若需要精準拆分高度，需另升級 Floor 幾何取樣 / raycast 並保留命中證據；未實作前不要硬猜局部樓板底。

## 驗證清單

- 抽查使用者質疑的房間：確認鄰近但 host 非 TYPE 牆的門窗沒有被扣除。
- 抽查至少一個未變更或使用者認為穩定的牆型：比對舊表、樓板厚度與 Revit 牆面積基準。
- 全表掃描 `#REF!`、`#VALUE!`、`#NAME?`、`#DIV/0!`。
- 檢查 CSV 每列欄位數一致。
- 確認無實體牆的 TYPE 不出現在分欄，表尾各 TYPE 欄有 `SUM` 總計。
- 總結時說明：開口扣除來源、牆高來源、是否有未能驗證而略過的資料。

## 參考

- `domain/revit-partition-takeoff.md`
- `domain/lessons.md` 的 L-041
- `MCP-Server/scripts/export_partition_takeoff_current.cjs`
- `pyRevit_Tools/MCP_Tools.extension/MCP_Macros.tab/Takeoff.panel/PartitionTakeoff.pushbutton/script.py`
