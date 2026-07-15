---
name: parking-check
description: "停車場檢討：停車位淨空高度檢查（>210cm）與停車位數量分類統計（法定、無障礙、增設等八類）。觸發條件：使用者提到停車場、停車位、車位淨空、車道寬度、parking、clearance、機車位、無障礙車位。工具：get_rooms_by_level、query_elements_with_filter、override_element_graphics、get_field_values。"
---

# 停車場檢討

## Sub-Workflows

### 1. 停車位淨空高度檢查（依車位種類）
執行前讀取 domain/parking-clearance-check.md

1. `get_field_values` 確認「停車位類型」參數值分佈
2. `query_elements_with_filter` 篩選 Parking 類別元素
3. 依車位種類查表對應最低淨高（一般 210cm / 裝卸 270cm / 大客車 380cm / 機車 190cm）
4. 計算每個車位上方淨空（到梁/管/天花板的距離）
5. `override_element_graphics` 標示不合格車位（紅色 = 淨空 ≤ 該類型最低淨高）
6. 回報各類型車位的合格/不合格統計

### 2. 停車位數量分類統計
執行前讀取 domain/parking-space-review.md

1. `get_category_fields` 確認「停車位類型」參數名稱
2. `get_field_values` 取得分類分佈（法定/無障礙/增設/裝卸/獎勵/機車/無障礙機車/大客車）
3. `query_elements_with_filter` 依類型統計數量
4. 與法定需求量比對，回報差異

### 3. 停車位自動編號 / 車位編碼
執行前讀取 domain/parking-auto-numbering.md 與 domain/lessons.md 的 L-021。

1. 先以 `get_active_schema` 確認當前視圖含 `Parking` 類別，並確認使用者要處理汽車、機車或大客車。
2. 使用 `MCP-Server/scripts/number_parking.js`，正式寫入前必須先跑 `--dry-run` 檢查排序預覽。
3. 若使用者指定起點 ElementId，加入 `--start-element {id}`；若未指定，腳本會自動使用排序後第一個元素，並在輸出中列出「自動判定 ElementId」。
4. 汽車位常用指令格式：`node scripts/number_parking.js --dry-run --only car --order yx --linear --car-start {startNumber} [--start-element {elementId}]`。
5. dry-run 確認後移除 `--dry-run` 正式寫入；寫入欄位為 Parking 例證參數「備註」。
6. 若 Revit 跳出「已在群組編輯模式之外變更群組」警告，需確認 Revit add-in 已部署含 `DismissWarningsPreprocessor` 的版本後再重跑。
