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
5. `牆高` 欄位填有效高度，讓範本公式仍能使用 `=牆長*牆高`。
6. 門窗開口按名稱、寬、高分組，輸出數量與扣除公式。

## 開口扣除硬規則

只扣除 host wall 可驗證為本次 TYPE 牆的開口：

- 對每個 Door / Window / Opening 讀 `get_element_info`。
- 從參數取得 `主體 ID` / `Host ID` / `Host Id`。
- 只有 host ElementId 等於本次計算的 TYPE 牆 ElementId 才扣除。
- 不得用最近牆、同樓層距離、房間內座標、BoundingBox 接近等方式推定開口所屬牆。
- host 是非 TYPE 牆、沒有 host、或 host 查不到時，不扣除；必要時列為人工複核。

## 面積與有效高度硬規則

若 Revit 牆有可信的 `面積` 參數，報表以 Revit 牆面積為基準，不直接用 `不連續高度` 相乘。

範本需保留 `牆長 × 牆高` 公式時：

```text
表內牆高 = (Revit 牆面積 + 已驗證 host 開口扣除面積) / 牆長
```

這樣 Excel 的毛面積欄會等於「Revit 牆面積 + 開口」，再由開口欄扣回，使總計對齊 Revit 牆面積。只有無法取得 Revit 牆面積時，才退回使用牆幾何高度或房間有效高度。

## 驗證清單

- 抽查使用者質疑的房間：確認鄰近但 host 非 TYPE 牆的門窗沒有被扣除。
- 抽查至少一個未變更或使用者認為穩定的牆型：比對舊表或 Revit 牆面積基準。
- 全表掃描 `#REF!`、`#VALUE!`、`#NAME?`、`#DIV/0!`。
- 檢查 CSV 每列欄位數一致。
- 總結時說明：開口扣除來源、牆高來源、是否有未能驗證而略過的資料。

## 參考

- `domain/revit-partition-takeoff.md`
- `domain/lessons.md` 的 L-041
- `MCP-Server/scripts/export_partition_takeoff_current.cjs`
