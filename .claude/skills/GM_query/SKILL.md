---
name: GM_query
description: "查詢與檢視 Revit 模型中已寫入的綠建材認證資訊：依品類（Walls/Floors/Ceilings/Windows/Doors 走 GreenMaterial_* 共享參數；Columns/StructuralFraming 柱樑除了單一結構材質參數指派，透過 /GM_inject Scenario 8 建立的 Type 也會有 GreenMaterial_Certified/Mat1_* 共享參數）列出已寫入的 Type、彙整證書字號與槽位內容（或柱樑的結構材質名稱），並可依認證狀態上色標記。觸發條件：使用者提到查詢綠建材、檢視綠建材、有沒有綠建材、綠建材標示、這面牆用了什麼綠建材、哪些元件有綠建材認證、柱子/樑用了什麼綠建材、green material query、find green material、green material certified、GreenMaterial_Mat。"
---

# 綠建材資訊查詢與檢視

本 Skill 只做**已寫入模型的綠建材資訊查詢與檢視**——列出、彙整、上色標記。不含綠建材率/面積算量與 Excel 明細表匯出：該方向已於 2026-08-12 定案不做（原 TASK-006 範圍改寫，見 `log/2026-08.md`）。若使用者要的是**新增**綠建材資訊到模型，改用 `/GM_import` → `/GM_inject revit`（見 `.claude/skills/GM_inject/SKILL.md`），不是本 Skill 的職責。

## Workflow

### 1. 確認品類與範圍

先問清楚（或從使用者原話判斷）：
- **品類**：Walls / Floors / Ceilings（系統族群，走步驟 2a）、Windows / Doors（載入式家族，走步驟 2b），還是 Columns / StructuralFraming（柱／樑，走步驟 2c，機制與前兩者不同——見下）？三組品類彼此獨立，使用者要「整個專案的綠建材」時三組都要查，不要只查前兩組就當作查完。
- **範圍**：整個專案的所有 Type，還是使用者目前選取的特定元素？若是特定元素，用 `get_selected_elements` 取得 ID 後直接跳到步驟 3（六個品類皆同），元素 ID 若是 Instance 要先取其 `TypeId`。
- **資料範圍（三選一，務必問清楚，不要預設）**：使用者說「綠建材」時語意常常含糊，實際上對應三種不同答案，資料量依序遞減（專案定義 ⊇ 模型實際使用 ⊇ 當前視圖）：
  1. **整個專案存在的綠建材**——只要 Type 上寫了 `GreenMaterial_*` 資料（或柱樑結構材質是 GBM 開頭）就算，不管這個 Type 有沒有被放置成任何實例，也算進去。適合「這個專案定義過哪些綠建材」「盤點資料庫」的問法。
  2. **模型中有用到的**——限定 Type 的實例數量 > 0，也就是真的被放到模型裡、會出現在明細表/算量裡的物件。Type 有填寫資料但 0 實例，只是庫存定義，不算「模型中存在」。使用者只說「模型中的綠建材」而未進一步說明時，優先假設是這個範圍，不要直接假設「有定義＝有用到」。
  3. **當前 VIEW 有的**——再進一步限定只計入目前作用中視圖可見/會出現的實例，不含其他樓層或視圖看不到的實例。需先依 `CLAUDE.md`「Active State Re-Anchoring」用 `get_active_view` 重新確認作用中視圖，不可沿用前幾輪對話裡的視圖 ID。

  三種範圍的篩選作法見步驟 3d；彙整回報時要註明實際用了哪一種範圍（見步驟 4），不要讓使用者誤以為是另一種。

### 2a. 列出候選 Type（Walls / Floors / Ceilings）

`get_types_by_category(category)` 取得該品類所有 Type 的 ID、名稱、族群、實例數量、目前材質。這一步只給出候選清單，不代表這些 Type 就有綠建材資訊。

### 2b. 列出候選 Type（Windows / Doors）

`list_family_symbols(filter)`（可用產品/家族關鍵字篩選）取得候選 FamilySymbol 清單。

**⚠️ 兩個實測缺陷，查詢前要注意**：
1. `list_family_symbols` 沒有分頁，單次最多回傳 100 筆，超過會被靜默捨棄（`MCP\Core\Commands\CommandExecutor.DetailComponent.cs:304` 的 `.Take(100)`）。專案總 FamilySymbol 數較多時，光靠不加 `filter` 的呼叫可能查不到已注入綠建材的窗/門 Type。
2. `filter` 是比對家族名稱的關鍵字，若用「Window」「Door」等英文關鍵字，中文命名的家族會完全篩不到。已注入綠建材的窗/門 Type 命名慣例含 `_TABC_GBM<編號>` 後綴（見 `domain/GM_rfa-family-injection.md`），改用 `filter: "GBM"` 或 `filter: "TABC"` 查詢，結果集小、不易觸及 100 筆上限，且能直接鎖定真正寫過資料的候選 Type，比對兩次結果交叉確認完整性。

### 2c. 列出候選 Type（Columns / StructuralFraming，柱／樑）

`get_types_by_category(category: "Columns")` 與 `get_types_by_category(category: "StructuralFraming")` 各取得該品類所有 Type，回傳的 `Materials` 欄位就是該 Type 目前指定的結構材質名稱。柱樑的綠建材**同時**透過兩個機制標記：(1) **單一結構材質參數指派**（`Structural Material`，不是複合構造層，這是柱樑獨有、Walls/Floors/Ceilings/Windows/Doors 沒有的機制）；(2) 若是透過 `/GM_inject` Scenario 8 建立的 Type，**也會**寫入 `GreenMaterial_Certified`/`GreenMaterial_Mat1_*` 這組 Type 層共享參數，與 Walls/Floors/Ceilings 走同一套機制（2026-08-13 修正：本文件先前版本誤寫柱樑完全不寫 `GreenMaterial_*`，已依實測 `TypeId 270136`/`272397` 訂正——兩者皆有完整的 `GreenMaterial_Certified: Yes` 與 `GreenMaterial_Mat1_*`）。柱樑讀取因此走下面統一的步驟 3，不再獨立分支。

### 3. 讀取每個候選 Type 的綠建材參數（Walls / Floors / Ceilings / Windows / Doors / Columns / StructuralFraming）

對每個候選 Type 呼叫 `get_element_info(elementId: <typeId>)`——GreenMaterial_\* 是 Type 層參數，直接對 TypeId（不是 Instance ID）查詢即可讀到完整值，這是本 Skill 讀取資料的核心步驟，**六個品類都適用**，柱樑不再是例外。

**⚠️ 2026-08-12 實測修正**：`get_element_info` 回傳的 `Parameters` 陣列只含有實際值的參數——完全沒填寫的 `GreenMaterial_*` 欄位不會出現在清單裡，即使該品類確實已綁定共享參數也一樣（實測：Walls 品類已綁定，`TypeId 263551` 完整回傳所有已填的 `GreenMaterial_Mat1_*`/`Mat2_*`，但同品類的 `TypeId 85268`「RC 牆 15cm」則完全沒有任何 `GreenMaterial_*` 欄位——不是「未綁定」，是「已綁定但這個 Type 沒填」）。因此**無法只憑單一 Type 的回應區分「品類從未綁定」vs「已綁定但這個 Type 沒填」**：

- 出現任何 `GreenMaterial_*` 欄位（不論是否為空） → 已寫入至少部分資料，往下彙整。
- 完全沒有 `GreenMaterial_*` 欄位 → 兩種可能：這個 Type 沒填資料，或整個品類從未綁定過共享參數。若同一輪查詢裡**其他** Type 有出現 `GreenMaterial_*` 欄位，代表品類確定已綁定，可判定為「這個 Type 沒填」；若整批候選 Type 全部都沒有任何 `GreenMaterial_*` 欄位，無法排除「品類從未綁定」，如使用者需要確定答案，可另外呼叫一次 `load_shared_parameters`（冪等操作，已綁定會回報「已存在相符綁定，跳過」，不會重複寫入或報錯）來確認，不要自行臆測。

**⚠️ 柱樑特有情形**：不是每個 `Materials` 欄位為 GBM 開頭的柱樑 Type 都保證有 `GreenMaterial_*` 欄位——只有透過 `/GM_inject` Scenario 8（或曾經手動呼叫過 `set_green_material_type_parameters`）建立的 Type 才會有；專案裡若有更早期、只單純在 Revit 手動指定結構材質、從未跑過共享參數寫入流程的 GBM 命名慣例 Type，`Materials` 欄位會對得上但 `get_element_info` 查不到任何 `GreenMaterial_*` 欄位。因此柱樑判讀要**同時**看兩個線索：步驟 2c 的 `Materials` 欄位是否為 `GBM` 開頭（判斷「已指定綠建材材質」，`domain/GM_catalog.md` 命名慣例：`GBM<編號>_<TABC材料完整名稱>`）、以及本步驟 `get_element_info` 是否有 `GreenMaterial_Certified`/`Mat1_*`（判斷「是否有完整證書資料可回報」）。前者有後者沒有時，回報「已指定材質但缺完整證書資料（可能是舊流程建立、未走 Scenario 8）」，不要當作「未指定」；也不要只憑 `Materials` 欄位就臆測證書廠商、有效期等細節——沒有 `GreenMaterial_*` 欄位就代表這些資料不存在，需另外用 `get_all_materials(searchKeyword: "<GBM編號>")` 確認材質存在，或請使用者對照 `domain/GM_catalog.md`／原始 TABC 資料。

**⚠️ 已知限制（柱樑結構材質參數本身，與上面的共享參數讀取是兩回事）**：部分既有族群把「結構材質」設為 Instance 參數或綁定公式，此時 Type 層的 `Materials` 可能不反映實際每個實例的材質（技術背景見 `domain/lessons.md` 的 L-031）。若使用者要求逐一核對，改用 `get_element_info(elementId: <instanceId>)` 查該 Instance 的 `Structural Material` 參數值，不要只信任 Type 層結果。

### 3d. 依資料範圍篩選（模型中有用到的 / 當前 VIEW 有的）

若步驟 1 確認使用者要的是「整個專案存在的綠建材」，步驟 2a-2c + 3 找到、且確實有填寫資料（或柱樑的 `Materials` 已為 GBM 開頭）的候選 Type 就是最終清單，不需要再篩選，直接跳到步驟 4。

若使用者要的是「模型中有用到的」或「當前 VIEW 有的」，在彙整前要用實例數把 0 實例的 Type 濾掉：

- **Walls / Floors / Ceilings / Columns / StructuralFraming**：`get_types_by_category` 已經回傳每個 Type 的 `InstanceCount`，`InstanceCount = 0` 的 Type 直接從結果剔除，不要放進最終表格。
- **Windows / Doors**：`list_family_symbols` **不回傳實例數**，不能只憑它判斷有無實例。對每個已確認有填寫綠建材資料的候選 FamilySymbol，另外呼叫 `query_elements_with_filter(category: "Windows"/"Doors", filters: [{field: "Type", operator: "equals", value: "<TypeName>"}])` 取得實際 `Count`；`Count = 0` 的 Type 一樣剔除，`Count > 0` 就把這個數字填進最終表格的實例數欄位（不要留空）。

若要進一步收斂到「當前 VIEW 有的」：
1. 先用 `get_active_view` 重新確認作用中視圖 ID（依 `CLAUDE.md`「Active State Re-Anchoring」，本回合內重新錨定，不可沿用舊值）。
2. 對每個候選 Type 呼叫 `query_elements_with_filter` 時帶上 `viewId: <目前視圖 ID>`，取得的 `Count` 才是「這個視圖看得到」的實例數；`Count = 0` 同樣剔除。這一步對所有品類（含 Walls/Floors/Ceilings/Columns/StructuralFraming）都要重新查，不能沿用步驟 2a/2c 未指定 `viewId` 的 `InstanceCount`（那是全專案數字，不是視圖範圍數字）。
3. 若目前作用中視圖不是模型視圖（例如圖紙、明細表），提醒使用者切換到模型視圖後再查，不要臆測「當前視圖」的內容。

### 4. 彙整並回報

- **Walls / Floors / Ceilings / Windows / Doors**：以表格呈現 Type 名稱 / TypeId / `GreenMaterial_Certified` / 各已填寫槽位（Mat1~Mat6）的證書字號＋產品名稱 / 非幾何輔助材料欄位（`GreenMaterial_Adhesive`/`Sealant`/`Waterproofing`，如有）。**Windows/Doors 若查不到 `GreenMaterial_Certified`（best-effort 欄位，見上方已知限制），該欄位填「無此欄位」而不是留空或當作未認證**，不要跟 Walls/Floors/Ceilings 沒填的空白欄位混淆。
- **Columns / StructuralFraming**：另立一張表，欄位為 Type 名稱 / TypeId / 結構材質名稱（含 GBM 編號）/ `GreenMaterial_Certified`（若 `get_element_info` 有回傳）/ 已填寫的 Mat1 證書字號＋產品名稱（若有）/ 實例數量。不要把柱樑併進上面那張表——即使柱樑 Type 這次剛好也有 Certified/Mat1 資料，資料結構仍與 Walls/Floors/Ceilings/Windows/Doors 不同（柱樑固定只有 Mat1、沒有 Mat2~Mat6，且多了結構材質這個獨有欄位），分開列才不會誤導使用者以為柱樑支援多槽位。若某柱樑 Type 只有 `Materials` 對得上 GBM 但 `get_element_info` 查不到 `GreenMaterial_*`，`Certified`/Mat1 欄位留空並在該列備註「缺完整證書資料」，不要留白讓人誤以為沒查。

兩種表格都**不要**換算或臆測任何面積、比例、百分比數字——每一個具體數字都必須直接來自本輪工具回應（per `CLAUDE.md`「Tool Call Data Honesty」）。使用者要「整個專案」時，兩張表都要給，不要只給其中一張。

回報開頭要註明步驟 1 確認的資料範圍（整個專案存在 / 模型中有用到 / 當前 VIEW 有的），例如「以下為模型中有用到的綠建材（已排除 0 實例的 Type）」，不要讓使用者誤以為是另一種範圍；三種範圍不是互斥的一次性選擇，使用者事後要求切換範圍（例如先給了專案定義範圍，之後追問「模型中存的話呢」）時，依步驟 3d 重新篩選並重新彙整，不要在既有表格上口頭打折扣。

### 5.（選用）上色標記

使用者要求視覺化時：
1. 依步驟 4 的結果，把 Type 分成「已認證/已指定綠建材」與「未認證/未填寫」兩組：Walls/Floors/Ceilings 看 `GreenMaterial_Certified: true`；Columns/StructuralFraming 優先看 `GreenMaterial_Certified: true`（若 Scenario 8 有寫），查不到才退回用 `Materials` 是否為 `GBM` 開頭判斷；**Windows/Doors 的 `GreenMaterial_Certified` 是 best-effort 欄位，部分家族會被 Revit 拒絕新增（見 `domain/GM_rfa-family-injection.md` 2026-08-13 已知限制）**，查不到時退回看該 Type 是否有填寫 `GreenMaterial_Mat1_Name`/`Mat1_CertNo`（有值即視為已指定），不要因為沒有 `Certified` 就當作未認證。
2. 對每個 Type，用 `query_elements_with_filter(category, filters: [{field: "Type", operator: "equals", value: "<TypeName>"}])` 或既有的品類查詢工具找出該 Type 的所有實例 ID（若 `get_types_by_category` 已回傳 Instance 統計但無逐一 ID，需另外查詢取得）。
3. `override_element_graphics` 對已認證/已指定實例上綠色、未認證維持預設或另一顏色；完成後提醒使用者可用 `clear_element_override` 復原。

## 工具

| 工具名稱 | 用途 |
|---------|------|
| `get_selected_elements` | 取得使用者目前選取的元素（限定查詢範圍時用） |
| `get_types_by_category` | 列出 Walls/Floors/Ceilings 品類的候選 Type（`category` 傳對應字串），回傳含全專案 `InstanceCount`；也支援 `Columns`／`StructuralFraming`，回傳的 `Materials` 即結構材質名稱 |
| `list_family_symbols` | 列出 Windows/Doors 等載入式家族的候選 Type（見步驟 2b 的關鍵字篩選注意事項）；**不回傳實例數**，模型/視圖範圍篩選需另用 `query_elements_with_filter`（見步驟 3d） |
| `get_element_info` | 讀取指定 TypeId 的完整 `GreenMaterial_*` 參數值（六個品類共用的核心讀取工具，柱樑也適用，見步驟 3），或讀取指定 Instance 的 `Structural Material` 參數（柱樑逐一核對用） |
| `load_shared_parameters` | 冪等檢查品類是否已綁定 `GreenMaterial_*`（Walls/Floors/Ceilings/Windows/Doors/Columns/StructuralFraming 皆可綁定，柱樑要透過 `/GM_inject` Scenario 8 走過才會實際有值） |
| `get_all_materials` | 依關鍵字查詢材質是否存在（柱樑補查證書細節時用） |
| `get_active_view` | 重新錨定目前作用中視圖 ID（「當前 VIEW 有的」範圍必用，見步驟 3d） |
| `query_elements_with_filter` | 依 Type 名稱找出所有實例 ID／實際數量；帶 `viewId` 可限定只算某視圖內的實例。用途有二：(1) 步驟 3d 判斷「模型中有用到的」/「當前 VIEW 有的」範圍時的實例數查核，(2) 上色標記時找出實例 ID |
| `override_element_graphics` | 依認證狀態上色標記 |
| `clear_element_override` | 清除上色標記 |

## Reference

詳見 `domain/GM_parameter-schema.md`（共享參數 Schema 權威定義，§4 明細表與 QAQC 審查相容性一節描述了本 Skill 涵蓋的查詢與上色能力）、`domain/GM_catalog.md`（`GBM<編號>_<名稱>` 材質命名慣例，柱樑判讀依據）、`domain/GM_rfa-family-injection.md`（窗/門 `_TABC_GBM<編號>` 家族命名慣例）、`domain/lessons.md` L-031（柱樑 Instance 參數限制）、`docs/green-material/README.md`（支援範圍與各品類機制對照）。
