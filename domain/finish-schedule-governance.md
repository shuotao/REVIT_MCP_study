---
name: finish-schedule-governance
description: "粉刷明細表材料代碼治理 SOP：跨 Revit 專案盤點 F/B/W/C 代碼、檢查跳號、依材料表對照 CSV 重排代碼、批次回寫房間粉刷參數、同步 AE-材料版，並用粉刷比對 CSV 驗證房間欄位。適用於粉刷明細表、room finish schedule、finish code remap、material board family sync。"
metadata:
  version: "1.0"
  updated: "2026-07-03"
  created: "2026-07-03"
  contributors:
    - "Codex"
  references: []
  related:
    - room-surface-area-review.md
    - finish-legend-creation.md
    - room-numbering-workflow.md
    - user-specified-runtime-parameters.md
  referenced_by:
    - finish-schedule-governance
  tags: [finish-schedule, room-finish, powder-finish, 粉刷明細表, 材料編號, 材料表對照, AE-材料版, F, B, W, C, CSV]
---

# 粉刷明細材料代碼治理 SOP

## 目的

本 SOP 用於管理 Revit 房間粉刷明細表中的材料代碼，包含 F/B/W/C 代碼盤點、跨專案合併檢查、跳號重排、批次回寫房間參數、同步 `AE-材料版`，以及用外部 CSV 反查房間欄位是否填入正確。

此流程處理的是「代碼與名稱的治理」，不是粉刷面積計算。若任務是計算牆面、地坪或天花面積，先看 `domain/room-surface-area-review.md`；若任務是建立圖例，整理完代碼後再看 `domain/finish-legend-creation.md`。

## 輸入參數

以下值都是 runtime parameters，需由使用者指定、上傳檔案、或由工具在本 turn 查詢取得：

| 參數 | 說明 |
|------|------|
| `{scheduleNames}` | 要讀取的 Revit 粉刷明細表名稱，可為一張或多張 |
| `{floorFinishField}` | 地坪代碼欄位，預設 `樓板塗層` |
| `{baseboardField}` | 踢腳代碼欄位，預設 `踢腳` |
| `{wallFinishField}` | 牆面代碼欄位，預設 `牆面塗層` |
| `{ceilingFinishField}` | 天花代碼欄位，預設 `天花板塗層` |
| `{materialTableCsvPath}` | 材料表對照 CSV 路徑 |
| `{comparisonCsvPath}` | 粉刷明細表比對 CSV 路徑 |
| `{familyName}` | 要同步的材料版族群名稱，預設可為 `AE-材料版` |

除上述預設欄位語意外，不要把某一次專案的明細表名稱、CSV 檔名、房號、樓層或材料清單寫死到規則中。

## 代碼語意

| 前綴 | 類別 | 預設房間參數 |
|------|------|--------------|
| `F` | 地坪 / floor finish | `樓板塗層` |
| `B` | 踢腳 / baseboard | `踢腳` |
| `W` | 牆面 / wall finish | `牆面塗層` |
| `C` | 天花 / ceiling finish | `天花板塗層` |

多重代碼以 `+` 串接。空白、`-`、未填值都視為沒有材料代碼。

## 標準流程

### 1. 讀取 Revit 明細表

1. 先確認目前 Revit 專案是使用者指定要處理的專案。
2. 對 `{scheduleNames}` 逐一讀取明細表欄位與 body rows。
3. 用欄位名稱對齊房間編號、房間名稱、樓層與 F/B/W/C 對應參數；不要假設欄位索引固定。
4. 將每個房間的代碼值正規化：trim、全形空白轉半形、以 `+` 分割、去除空白 token、保留原代碼大小寫。
5. 本次回答中所有房間數、代碼清單、差異列表都必須來自本 turn 的工具結果。

### 2. 多專案合併與跳號檢查

使用者逐一開啟多個 Revit 專案時，每個專案都要重新讀取指定明細表，並保存該專案本 turn 的代碼集合。最後合併各專案的 F/B/W/C 集合後，依數字順序檢查缺號。

跳號檢查只判斷「代碼是否存在」，不要把房間筆數或數量統計混進判斷。若材料對照 CSV 中有「空白」名稱列，該列代表要移除的空號，不應保留為有效材料。

### 3. 建立重排映射

重排原則是「材料名稱不變，移除空白號後編號往前遞增」：

1. 讀取 `{materialTableCsvPath}`，取得每列 `材料編號` 與 `材料名稱`。
2. 依 F/B/W/C 分組，排除名稱為空白、空白列、或使用者指定要移除的代碼。
3. 依原本數字排序建立新連號。
4. 只有新舊編號不同的列才形成映射。
5. 映射輸出必須清楚列出「舊編號 -> 新編號」與材料名稱。

### 4. 批次回寫房間粉刷參數

回寫 Revit 房間參數時，優先使用批次工具在 Revit 端單一 Transaction 中完成。

安全規則：

- 先 dry-run，檢查 `ChangedRooms`、`PlannedChanges`、`UnusedMappings`、缺參數與唯讀參數。
- 只替換完整 token，不做 substring replace；`F1` 不可影響 `F11`。
- 保留 `+` 串接順序，僅替換命中的代碼。
- 正式 apply 後，重新讀取指定明細表驗證。
- 重排映射不是冪等操作；同一套 `舊碼 -> 新碼` 不可在已更新模型上重複執行。若需要第二次執行，必須先重新讀目前值再重算。

### 5. 同步材料表 CSV

材料表對照 CSV 是材料名稱與代碼的來源。讀寫 CSV 時：

- 優先以 UTF-8 讀寫；若要給 Excel 開啟，必要時可輸出 UTF-8 BOM 版本以避免繁體中文亂碼。
- 使用結構化 CSV parser，保留逗號、引號、全形符號與換行，不用字串 split 手拆。
- `材料名稱` 是身份來源；`材料編號` 是重排後要更新的屬性。
- 使用者指定的 CSV 路徑不可寫死，應以 `{materialTableCsvPath}` 傳入。

### 6. 同步 `AE-材料版`

同步 `AE-材料版` 時，以材料表 CSV 的材料名稱作為主索引：

1. 用 `材料名稱` 對應既有類型名稱尾碼、`@` 表面材料名稱，或既有 `標記`/Type Mark。
2. 類型名稱目標格式為 `材料編號-材料名稱`。
3. `描述`/Description 必須寫入 `材料名稱`。
4. `標記`/Type Mark 必須寫入 `材料編號`。
5. 可寫入的 `@*` 類型參數必須指向同名的 `@材料名稱`。
6. 若 Revit 類型名稱含非法字元，要轉成可用全形字元；半形冒號 `:` 必須轉成全形冒號 `：`。

重要避坑：不要只依舊編號找類型後直接改代碼。先用材料名稱確認同一個材料，再更新編號。類型名稱尾碼與 `@` 表面材料去掉 `@` 後必須是同一個名稱，避免把不同材料錯配。

### 7. 以粉刷比對 CSV 驗證房間欄位

比對任一 `{comparisonCsvPath}` 時：

1. 重新讀取 CSV，不沿用上一版檔案內容。
2. 動態尋找房間編號欄，例如 `編號`；動態尋找空間名稱欄，例如 `空間名稱`。
3. 依任務類型選取代碼欄：地坪讀 F 欄，踢腳讀 B 欄，牆面讀 W 欄，天花讀 C 欄。
4. CSV 中某列有填值的代碼欄組成 expected code set。
5. Revit 明細表中對應房間參數值以 `+` 分割成 actual code set。
6. 比對集合，輸出：
   - `值不一致`：同一房間 CSV 與 Revit 都存在但代碼集合不同。
   - `CSV 有但 Revit 明細表沒有`：CSV 房間編號在指定 Revit 明細表中找不到。
   - `Revit 明細表有但 CSV 沒有`：Revit 房間編號在 CSV 中找不到。

回報差異時，至少列出房間編號、CSV 空間名稱、Revit 房間名稱、樓層、CSV 預期代碼、Revit 目前代碼、來源明細表。

## 與既有功能的合併判斷

本流程應獨立成 `finish-schedule-governance` Skill，並與既有功能分工：

| 功能 | 是否合併 | 原因 |
|------|----------|------|
| `room-numbering` | 不合併 | 房間編號排序與材料代碼重排是不同治理面向 |
| `room-surface-area-review` | 不合併，必要時引用 | 該流程計算面積與偵測模型粉刷層，本流程治理明細表代碼與材料名稱 |
| `finish-legend-creation` | 不合併，後續串接 | 圖例應在材料代碼與材料版同步完成後建立 |
| `detail-component-sync` | 不合併 | `AE-材料版` 使用 detail component tool group，但工作語意屬於材料代碼治理 |
| `element-query` | 不合併 | 查詢元素是底層能力，不是這套材料重排 SOP |

## 輸出格式

一般回覆應分成以下區塊：

1. `讀取來源`：列出本 turn 讀取的明細表與 CSV。
2. `目前代碼`：列 F/B/W/C 存在代碼，不列數量除非使用者要求。
3. `跳號檢查`：列缺號或說明無跳號。
4. `重排映射`：列舊碼、新碼、材料名稱。
5. `dry-run 結果`：列預計改動房間數、欄位改動數、未使用映射與錯誤。
6. `apply 結果`：只在實際寫入後列出。
7. `比對差異`：分成值不一致、CSV 缺房間、Revit 缺房間。

若工具不可用或 Revit 未開啟，停止在 dry-run 前並說明缺少哪個前置條件；不要憑記憶產生專案差異清單。
