---
name: finish-schedule-governance
description: "治理 Revit 房間粉刷明細表與材料代碼。當使用者提到粉刷明細表、F/B/W/C 材料編號、跳號、重排、材料表對照 CSV、AE-材料版、room finish schedule、finish code remap、material board family sync、粉刷比對 CSV 時使用。"
---

# 粉刷明細材料代碼治理

使用此 Skill 時，先讀 `domain/finish-schedule-governance.md`，並以使用者指定的明細表、CSV 與 Revit 專案狀態作為本次資料來源。

## 工作流

### 1. 讀取明細表與盤點代碼

1. 重新錨定目前 Revit 狀態；若本 turn 需要寫入模型，先確認目前專案已正確開啟。
2. 使用使用者指定的 `{scheduleNames}` 讀取房間明細表；不要把某次專案的明細表名稱寫死成永久規則。
3. 從 `{floorFinishField}`、`{baseboardField}`、`{wallFinishField}`、`{ceilingFinishField}` 讀取值，預設分別為 `樓板塗層`、`踢腳`、`牆面塗層`、`天花板塗層`。
4. 以 `+` 分割多重代碼；空白與 `-` 視為無代碼。盤點 F、B、W、C 目前存在的代碼，不計算數量。

### 2. 檢查跳號與建立重排映射

1. 多專案盤點時，每開一個專案都要重新讀取該專案明細表；最後用本 turn 工具結果合併，不靠記憶補資料。
2. 依材料對照 CSV 的代碼與名稱建立「保留名稱、移除空白號、代碼往前遞增」的映射。
3. 只更新被映射到的舊代碼，例如 `F11 -> F10`；不能把 `F1` 誤改到 `F11` 裡。
4. 先 dry-run，再 apply。代碼重排不是冪等操作；若使用者要求再跑一次，必須重新讀目前明細表後重算映射。

### 3. 同步材料表與 AE-材料版

1. 材料名稱以 `{materialTableCsvPath}` 中的 CSV 為準；CSV 的材料名稱是身份來源，代碼是要被更新的屬性。
2. `AE-材料版` 類型名稱格式為 `材料編號-材料名稱`。Revit 類型名稱不可使用半形冒號 `:`，需要改成全形冒號 `：`。
3. 類型參數中的表面材料必須對應同一個名稱的 `@材料名稱`。例如類型尾碼為 `地坪整體粉光` 時，表面材料應找 `@地坪整體粉光`。
4. 同步時同時檢查：類型名稱、`標記`/Type Mark、`描述`/Description、可寫入的 `@*` 類型參數，以及對應 Material 的名稱與參數。

### 4. 以 CSV 比對房間參數

1. 讀取使用者指定的 `{comparisonCsvPath}`；用結構化 CSV parser，不用手拆逗號。
2. 以欄名尋找房間編號欄與空間名稱欄，不固定欄位索引。
3. 依比對類型選擇代碼欄：地坪看 F 欄，踢腳看 B 欄，牆面看 W 欄，天花看 C 欄。
4. 將 CSV 中有填值的代碼集合，與 Revit 明細表對應房間參數的代碼集合比對。
5. 回報三類結果：值不一致、CSV 有但 Revit 明細表沒有、Revit 明細表有但 CSV 沒有。

## 工具

| 工具名稱 | 用途 |
|---------|------|
| `query_schedule_data` | 讀取使用者指定的 Revit 明細表欄位與列資料 |
| `remap_room_finish_codes` | 在單一 Revit Transaction 中批次更新房間粉刷代碼 |
| `sync_material_board_family_types` | 依材料表對照 CSV 同步 `AE-材料版` 類型、標記、描述與 `@*` 參數 |
| `list_family_symbols` | 查詢 `AE-材料版` 或其他目標族群類型 |
| `sync_room_ceiling_finish_from_ceilings` | 需要由天花板元素反推房間 `天花板塗層` 時使用 |
| `get_room_surface_areas` | 需要從模型粉刷層偵測或面積計算回補明細時使用 |
| `create_finish_legend` | 材料代碼整理完成後，建立粉刷／油漆材料圖例 |

## 與既有功能的關係

- 與 `room-numbering` 不合併：房間編號排序是房號治理；本 Skill 是材料代碼治理。
- 與 `room-surface-area-review` 不合併：該流程負責面積與模型粉刷層偵測；本 Skill 負責明細表代碼、CSV 對照與回寫治理。
- 與 `finish-legend-creation` 不合併：圖例建立應在材料代碼與 `AE-材料版` 整理後執行。
- `sync_material_board_family_types` 雖在 detail component tool group 中，遇到 `AE-材料版` 與材料表對照時仍由本 Skill 編排。

## Reference

- `domain/finish-schedule-governance.md`
- `domain/room-surface-area-review.md`
- `domain/finish-legend-creation.md`
- `domain/lessons.md`
