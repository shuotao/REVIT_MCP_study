---
name: GM_query
description: "查詢與檢視 Revit 模型中已寫入的綠建材認證資訊：依品類（Walls/Floors/Ceilings/Windows/Doors）列出已掛載 GreenMaterial_* 共享參數的 Type、彙整證書字號與槽位內容，並可依認證狀態上色標記。觸發條件：使用者提到查詢綠建材、檢視綠建材、有沒有綠建材、綠建材標示、這面牆用了什麼綠建材、哪些元件有綠建材認證、green material query、find green material、green material certified、GreenMaterial_Mat。"
---

# 綠建材資訊查詢與檢視

本 Skill 只做**已寫入模型的綠建材資訊查詢與檢視**——列出、彙整、上色標記。不含綠建材率/面積算量與 Excel 明細表匯出：該方向已於 2026-08-12 定案不做（原 TASK-006 範圍改寫，見 `log/2026-08.md`）。若使用者要的是**新增**綠建材資訊到模型，改用 `/GM_import` → `/GM_inject revit`（見 `.claude/skills/GM_inject/SKILL.md`），不是本 Skill 的職責。

## Workflow

### 1. 確認品類與範圍

先問清楚（或從使用者原話判斷）：
- **品類**：Walls / Floors / Ceilings（系統族群，走步驟 2a）還是 Windows / Doors（載入式家族，走步驟 2b）？
- **範圍**：整個專案的所有 Type，還是使用者目前選取的特定元素？若是特定元素，用 `get_selected_elements` 取得 ID 後直接跳到步驟 3，元素 ID 若是 Instance 要先取其 `TypeId`。

### 2a. 列出候選 Type（Walls / Floors / Ceilings）

`get_types_by_category(category)` 取得該品類所有 Type 的 ID、名稱、族群、實例數量、目前材質。這一步只給出候選清單，不代表這些 Type 就有綠建材資訊。

### 2b. 列出候選 Type（Windows / Doors）

`list_family_symbols(filter)`（可用產品/家族關鍵字篩選）取得候選 FamilySymbol 清單。

### 3. 讀取每個候選 Type 的綠建材參數

對每個候選 Type 呼叫 `get_element_info(elementId: <typeId>)`——GreenMaterial_\* 是 Type 層參數，直接對 TypeId（不是 Instance ID）查詢即可讀到完整值，這是本 Skill 讀取資料的核心步驟。

**⚠️ 2026-08-12 實測修正**：`get_element_info` 回傳的 `Parameters` 陣列只含有實際值的參數——完全沒填寫的 `GreenMaterial_*` 欄位不會出現在清單裡，即使該品類確實已綁定共享參數也一樣（實測：Walls 品類已綁定，`TypeId 263551` 完整回傳所有已填的 `GreenMaterial_Mat1_*`/`Mat2_*`，但同品類的 `TypeId 85268`「RC 牆 15cm」則完全沒有任何 `GreenMaterial_*` 欄位——不是「未綁定」，是「已綁定但這個 Type 沒填」）。因此**無法只憑單一 Type 的回應區分「品類從未綁定」vs「已綁定但這個 Type 沒填」**：

- 出現任何 `GreenMaterial_*` 欄位（不論是否為空） → 已寫入至少部分資料，往下彙整。
- 完全沒有 `GreenMaterial_*` 欄位 → 兩種可能：這個 Type 沒填資料，或整個品類從未綁定過共享參數。若同一輪查詢裡**其他** Type 有出現 `GreenMaterial_*` 欄位，代表品類確定已綁定，可判定為「這個 Type 沒填」；若整批候選 Type 全部都沒有任何 `GreenMaterial_*` 欄位，無法排除「品類從未綁定」，如使用者需要確定答案，可另外呼叫一次 `load_shared_parameters`（冪等操作，已綁定會回報「已存在相符綁定，跳過」，不會重複寫入或報錯）來確認，不要自行臆測。

### 4. 彙整並回報

以表格呈現：Type 名稱 / TypeId / `GreenMaterial_Certified` / 各已填寫槽位（Mat1~Mat6）的證書字號＋產品名稱 / 非幾何輔助材料欄位（`GreenMaterial_Adhesive`/`Sealant`/`Waterproofing`，如有）。**不要**換算或臆測任何面積、比例、百分比數字——每一個具體數字都必須直接來自本輪工具回應（per `CLAUDE.md`「Tool Call Data Honesty」）。

### 5.（選用）上色標記

使用者要求視覺化時：
1. 依步驟 4 的結果，把 Type 分成「已認證」（`GreenMaterial_Certified: true`）與「未認證/未填寫」兩組。
2. 對每個 Type，用 `query_elements_with_filter(category, filters: [{field: "Type", operator: "equals", value: "<TypeName>"}])` 或既有的品類查詢工具找出該 Type 的所有實例 ID（若 `get_types_by_category` 已回傳 Instance 統計但無逐一 ID，需另外查詢取得）。
3. `override_element_graphics` 對已認證實例上綠色、未認證維持預設或另一顏色；完成後提醒使用者可用 `clear_element_override` 復原。

## 工具

| 工具名稱 | 用途 |
|---------|------|
| `get_selected_elements` | 取得使用者目前選取的元素（限定查詢範圍時用） |
| `get_types_by_category` | 列出 Walls/Floors/Ceilings 品類的候選 Type |
| `list_family_symbols` | 列出 Windows/Doors 等載入式家族的候選 Type |
| `get_element_info` | 讀取指定 TypeId 的完整 `GreenMaterial_*` 參數值（核心讀取工具） |
| `query_elements_with_filter` | 依 Type 名稱找出所有實例 ID，供上色標記使用 |
| `override_element_graphics` | 依認證狀態上色標記 |
| `clear_element_override` | 清除上色標記 |

## Reference

詳見 `domain/GM_parameter-schema.md`（共享參數 Schema 權威定義，§4 明細表與 QAQC 審查相容性一節描述了本 Skill 涵蓋的查詢與上色能力）、`domain/GM_catalog.md`。
