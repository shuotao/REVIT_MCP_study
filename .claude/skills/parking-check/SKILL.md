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
