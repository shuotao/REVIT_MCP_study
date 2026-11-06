---
name: GM_rfa-family-injection
description: "門窗／獨立元件（載入式 Family, .rfa）綠建材導入 SOP：以既有相似元件為基底另存備份、在 Family 文件內建立新 Type 並寫入 Identity Data 與遮陽/隔音等綠建材參數、再載回專案且不覆蓋非目標 Family Type。觸發關鍵字：門窗綠建材、獨立元件、RFA 導入、loadable family、防音門窗、Low-E玻璃、遮陽係數、隔音Rw、TASK-005.7。"
metadata:
  version: "1.1"
  updated: "2026-08-31"
  created: "2026-08-12"
  references:
    - "tools/green-material/archive/reports/Revit_Element_GreenMaterial_Mapping_Analysis.md 情境 7（未隨本 repo 收編，屬原始 PR 分支的 archive/ 內容，見 tools/green-material/README.md）"
    - "MCP/Core/Commands/CommandExecutor.FamilyExport.cs（EditFamily/SaveAs/Close 既有前例）"
    - "GreenMaterial_SharedParams.txt（v4/v5 Schema，實測共 69 個 PARAM 列）"
    - "MCP/Core/Commands/CommandExecutor.GM_RfaFamilyInjection.cs（2026-08-31 根因修正：SharedParametersFilename 生命週期延長至涵蓋整個 Transaction）"
    - "MCP/Core/Commands/CommandExecutor.GM_GreenMaterial.cs（對照組：load_shared_parameters 正確順序，實測 69/69 成功）"
  related:
    - GM_parameter-schema.md
    - GM_catalog.md
    - family-inventory-cleanup.md
    - tool-capability-boundary.md
  referenced_by:
    - GM_inject
  tags: [rfa, family, window, door, 門窗, 獨立元件, 綠建材, green-material, loadable-family, TASK-005.7]
---

# 門窗／獨立元件 RFA 綠建材導入（RFA Family Green Material Injection）

## Purpose

針對門、窗、玻璃、幕牆嵌板等**載入式 Family**（Loadable Family，非系統族群），把綠建材資訊寫入 Family Type 層級。這條路徑與 `GM_parameter-schema.md` 描述的牆/地板/天花板系統家族路徑完全不同：系統家族的 Type/Material 都活在專案文件內，本 SOP 的操作對象是**獨立的 .rfa 家族文件**，必須透過 `Document.EditFamily` 開文件、`SaveAs` 備份、在家族文件內建 Type、`LoadFamily` 載回專案。

對應 kanban `TASK-005.7`（情境 7），驗收條件即本文件的四條硬性規則（下方「核心規則」）加上「至少一個 Window + 一個 Door 案例驗證」。

## 核心規則（四條硬性規則，缺一不可）

### 規則 1：禁止無型錄從零生成

使用者必須**明確指定一個既有的、相似的基底 Family+Type**（例如專案裡已載入的某防音窗、或型錄庫裡已有的相近窗型）。AI 不得自行臆測窗/門的幾何、材質分層、五金配置後從零建一個新家族。若使用者沒有指定基底，先問，不要猜。

「相似」的判準交給使用者/型錄，不是 AI 自由心證——AI 可以列出候選（`list_family_symbols` 篩該類別）供使用者挑，但最終選擇權在使用者。

### 規則 2：原始 .rfa 必須先建立可復原備份

任何寫入動作之前，必須先執行「開家族 → 另存備份」，且備份必須**先於**任何 Type 複製/參數寫入發生：

```text
Document.EditFamily(family)          // 開啟家族文件，不可在 Transaction 內呼叫（同 export_families 前例）
  → famDoc.SaveAs(backupPath, overwrite:false)   // 備份先寫，overwrite=false 避免誤蓋舊備份
  → （之後才開始複製 Type / 寫參數）
```

備份路徑慣例：`<備份根目錄>/<FamilyName>_backup_<yyyyMMdd_HHmmss>.rfa`。備份根目錄由使用者指定或預設專案旁的 `_rfa_backup/` 資料夾（需先 `Directory.CreateDirectory`）。備份完成後才可以繼續動 Type，任何步驟失敗都要能用這份備份還原，不依賴 Revit Undo（`LoadFamily` 之後 Undo 語意不可靠，見規則 4）。

> **警告（2026-08-31 實測發現，尚未修正——見下方〈已知缺陷〉(a)）**：專案檔位於唯讀或受保護目錄時，預設推導出來的備份路徑可能不可寫、甚至根本不該寫。呼叫端在這種情況下應**明確指定 `backupFolder` 參數**，不要依賴預設推導。

### 規則 3：Type Identity Data 與遮陽/隔音欄位落點

寫入分兩層，兩層都要落實，不可只寫一層：

| 資料 | 落點 | 內容 |
|---|---|---|
| **產品識別** | Family Type 內建 Identity Data 參數（`Manufacturer`／`Model`／`Description`／`URL`，依 Revit 版本與類別實際存在的欄位為準，不是每個都保證存在） | 綠建材標章字號、廠商、產品名稱（可與下方共享參數重複，Identity Data 是給人看的，共享參數是給明細表/QAQC 抓的） |
| **整體合格狀態** | `GreenMaterial_Certified`（YESNO 全域欄位，不分 Mat 槽位，語意與 Walls/Floors/Ceilings/Windows/Doors 系統家族路徑 `set_green_material_type_parameters` 的 `certified` 完全相同——見 `GM_parameter-schema.md` §1.1） | `inject_green_material_into_family` 的 `certified` 參數（選填布林值），透過 `FamilyManager.AddParameter` 加進家族文件、再 `Set()` 寫入，與 Mat1 槽位走同一套機制，只是欄位不分槽位 |
| **綠建材主資料** | `GreenMaterial_Mat1_*`（沿用 `GM_parameter-schema.md` 的 16 欄位 Mat1 槽位——門窗的玻璃或門扇視為該 Type 的主材料） | `Name`/`CertNo`/`Category`/`SubCategory`/`Applicant`/`ValidUntil`/`CNSSpec`/`TestItems`/`QualifiedItems`，只填有實際數據的欄位，不得杜撰 TVOC/甲醛 |
| **門窗專屬效能** | **新增專屬共享參數**（不沿用 Mat1 通用欄位，因為遮陽係數/隔音等級是門窗獨有、其他品類沒有對應意義）：`GreenMaterial_Window_ShadingCoefficient`（NUMBER，僅 Windows/Curtain Wall 適用）、`GreenMaterial_AcousticRw`（NUMBER，Windows 與 Doors 皆適用，對應型錄上的 Rw 隔音等級 dB） | 只在型錄/測試報告有明確數值時才寫，缺值就留空，不得估算填入 |

> **2026-08-13 原始記錄（歷史保留，結論已於下方「2026-08-31 根因追查與修正」推翻）**：v1.0 版本的 `inject_green_material_into_family` 只寫 Mat1 槽位與門窗專屬欄位，漏了 `GreenMaterial_Certified`（Walls/Floors/Ceilings/Columns/StructuralFraming 各路徑都有寫，唯獨門窗這條路徑沒有）。C# 端已補上 `SetFamilySharedBoolParam` 寫入邏輯（會先嘗試 `IdentityData` 群組、失敗再退而嘗試 `Data` 群組），呼叫端（`GM_inject` Scenario 7）可以連同 `certified: true` 一起傳入。**但實測發現這條路無法保證成功**：對既有案例家族「雙開落地窗- (2)_TABC_GBM0104092」執行時，兩個群組都以 Revit API 內部通用錯誤 `autodesk.parameter.group:data-1.0.0: Shared parameter creation failed.` 失敗，原因當時無法確定（同一支 API、同一個家族的 TEXT/NUMBER 型別 Mat1 欄位與 `GreenMaterial_AcousticRw` 都成功，只有這個 YESNO 布林欄位失敗，懷疑與 Revit 對 Loadable Family 新增 YesNo Type 參數的內部限制有關，但未證實）。當時的結論是：門窗這條路徑的 `GreenMaterial_Certified` 是 best-effort，不保證寫入成功；`MissingParameters` 出現這個欄位不代表整個注入失敗。
>
> 這段記錄是原作者在資訊不足時的誠實觀察，不是錯誤操作——保留在此作為歷史紀錄。但「懷疑與 YesNo Type 參數的內部限制有關」這個猜測，經下方 2026-08-31 追查證實**並不成立**，真正原因是一個生命週期 bug，與欄位型別、參數群組完全無關。

> **2026-08-31 根因追查與修正**：問題不是欄位型別限制，是 `MCP/Core/Commands/CommandExecutor.GM_RfaFamilyInjection.cs` 的**生命週期 bug**。舊版把 `app.SharedParametersFilename` 的還原點放在**所有 `AddParameter` 呼叫之前**：
>
> ```text
> 設定 app.SharedParametersFilename → OpenSharedParameterFile() 取得 defFile
>   → finally 立刻還原 SharedParametersFilename          ← 還原在這裡
>   → 之後才開 Transaction → fm.AddParameter(exDef, ...)  ← 使用在這裡
> ```
>
> `FamilyManager.AddParameter(ExternalDefinition, ForgeTypeId, bool)` 需要靠 Application **當下開著的**共享參數檔解析該 `ExternalDefinition`。檔名被還原後 `exDef` 成為懸空參照，Revit 只回傳通用錯誤訊息 `Shared parameter creation failed.`——**不是欄位型別或參數群組的問題**。
>
> **對照組**（同一份 codebase、同一位作者、實測 69/69 成功）：`CommandExecutor.GM_GreenMaterial.cs` 的 `load_shared_parameters` 用的是正確順序——設定 → 開檔 → **在 Transaction 內使用 `exDef`** → commit → **`finally` 才還原**。
>
> **為何原作者在 2026-08-13 只觀察到「部分失敗」**：這個 bug 是**環境相依**的。若執行機器上 `app.SharedParametersFilename` 原本就已經指向同一份 `GreenMaterial_SharedParams.txt`，那次「還原」等於沒還原，多數欄位仍會成功——這正是 2026-08-13 觀察到「只有 YESNO 失敗、TEXT/NUMBER 都成功」的成因。在乾淨環境（原值為空或指向別的檔案）則是全數失敗。
>
> **修正**：把 `SharedParametersFilename` 的設定/還原生命週期延長到涵蓋整個 Transaction（`finally` 移到 `t.Commit()` 之後）。還原的無條件保證未退化——任何例外路徑仍會回到原值。
>
> **實測證據**（Revit 2024，Autodesk 範例模型 Snowdon Towers，基底家族 `Window-Fixed-Transom` 的 `50" x 80"`，同一組測試資料、同一台機器，唯一變數是 `finally` 的位置）：
>
> | | 修正前 | 修正後 |
> |---|---|---|
> | `GreenMaterial_*` 寫入成功 | 0 / 12 | 12 / 12 |
> | `MissingParameters` | 12 個，全為 `Shared parameter creation failed.` | 空陣列 |
> | `GreenMaterial_Certified`（YESNO） | 失敗 | 成功，讀回 `Yes` |
> | `GreenMaterial_Window_ShadingCoefficient` | 失敗 | 成功，讀回 `0.42` |
> | `GreenMaterial_AcousticRw` | 失敗 | 成功，讀回 `35` |
> | Identity Data 三欄 | 成功 | 成功 |
> | 規則 4（`AffectedExistingTypes`） | 0 | 0 |
>
> 驗證方式是 `get_element_info` 逐欄讀回比對，不是採信工具回傳的 `Success: true`。
>
> **結論**：`GreenMaterial_Certified` 不再是 best-effort 欄位——它與其他 `GreenMaterial_*` 欄位是同等的正常必填欄位，缺漏即代表該次執行未通過（下方〈驗證協議〉已同步更新）。此次修正的實測**已涵蓋 Window 與 Door 兩個案例**（見〈驗證協議〉開頭的覆蓋紀錄）。

> **實作前必讀的編碼地雷**：`GreenMaterial_SharedParams.txt` 是 **cp950 (Big5) ANSI 編碼**，不是 UTF-8（檔案開頭註解已明講）。用一般 UTF-8 文字工具（含大多數程式碼編輯器的預設存檔）直接編輯或新增 PARAM 列，會把既有中文 GROUP/PARAM 說明重新存成亂碼，Revit 重新解析時會整批壞掉。新增 `GreenMaterial_Window_ShadingCoefficient`／`GreenMaterial_AcousticRw` 這兩個 PARAM 列時，必須用能指定 cp950 編碼寫檔的方式操作（例如 Python `open(path, "a", encoding="cp950")`），且新增前後都要驗證既有列的中文沒有變亂碼。GUID 沿用檔案既有的遞增慣例（目前最大到 `...111111111167`，新參數接續 `...168`/`...169`），並建議獨立一個 `GROUP 5「門窗專屬效能 (Window/Door Performance)」`，不要塞進既有 GROUP 1~4。

> **欄位總數：三個數字都對，範圍不同**——`GreenMaterial_SharedParams.txt` 目前實際共 **69** 個 PARAM 列（含本 SOP 新增的 GROUP 5 兩欄）。`GM_parameter-schema.md` 說的 **64** 是該檔定義範圍內的系統家族核心欄位（3 全域 + Mat1/2/4/5/6 各 11 欄 + Mat3（輔助材料）6 欄）；`inject_green_material_into_family` 的 TS 工具描述說的 **67** 是 64 再加上 Construction 群組的 `Adhesive`/`Sealant`/`Waterproofing` 3 欄；本檔案共享參數檔實際的 **69** 是 67 再加上本 SOP 專屬的 `GreenMaterial_Window_ShadingCoefficient`／`GreenMaterial_AcousticRw` 2 欄。三者是同一份 Schema 的不同統計範圍，不互相矛盾。實測佐證：`load_shared_parameters` 對 Walls 綁定回報 `TotalBound: 69`。

### 規則 4：載回專案時避免覆蓋非目標 Family Type

`LoadFamily` 的覆蓋語意（`IFamilyLoadOptions.OnFamilyFound`）容易誤傷同一個 Family 底下使用者手動調過的其他 Type。本 SOP 採**用新家族名稱迴避覆蓋歧義**、而不是硬控制 `overwriteParameterValues`：

1. 家族文件內只**新增**一個 Type（`ElementType.Duplicate`），絕不 rename/覆寫來源 Type。
2. `SaveAs` 這個家族文件時使用**新的家族檔名**（例如 `<OriginalFamilyName>_TABC_<licno>.rfa`），使它在專案裡是一個獨立的 Family 物件，不會與原家族同名衝突，`LoadFamily` 進專案時自然不會觸發「覆蓋既有 Type」的對話語意。
3. 若專案裡因先前執行已經載過同名的 `_TABC_<licno>` 家族（重跑同一個案例），才需要真的處理 `IFamilyLoadOptions`：這種情況下 `OnFamilyFound` 回傳 `overwriteParameterValues = true` 只用來更新這個「已知是自己產物」的家族本身，不影響其他任何家族。
4. **驗證覆蓋範圍（強制）**：`LoadFamily` 前後都要做「該類別 Type 清單快照」（`get_types_by_category` 或 `list_family_symbols`），比對載入後除了新增的那一個 Type，其他既有 Type 的名稱/參數簽章必須完全不變。有變動 → 停下來，不要當作成功回報。

## 執行順序（總覽）

```
使用者指定基底 Family+Type（規則1）
  → EditFamily 開家族文件
  → SaveAs 備份（規則2，先於任何修改）
  → 家族文件內 Duplicate Type
  → 寫 Identity Data + GreenMaterial_Certified + GreenMaterial_Mat1_* + 遮陽/隔音專屬參數（規則3）
  → SaveAs 為新家族檔名
  → 關閉家族文件（不覆寫原檔）
  → LoadFamily 回專案（規則4，帶 IFamilyLoadOptions）
  → 載回前後 Type 清單快照比對（規則4 強制驗證）
  → get_element_info 驗證新 Type 的共享參數值
  → 回報：新 Family/Type 名稱與 ID、備份檔路徑、寫入/缺漏欄位、受影響既有 Type 數量（應為 0）
```

## 已知缺陷（2026-08-31 實測發現，尚未修正）

以下兩項是 2026-08-31 根因驗證過程中另外實測發現的問題，**與上方 `GreenMaterial_Certified` 生命週期 bug 無關**，目前尚未修正，如實記錄以免掩蓋：

**(a) 預設備份資料夾可能推導到不可寫位置。** 規則 2 的預設備份路徑推導邏輯是「專案檔所在目錄旁的 `_rfa_backup/`」。實測時（Autodesk 範例模型位於 Revit 安裝目錄下）`PlannedBackupFolder` 被推導成 `C:\Program Files\Autodesk\Revit 2024\Samples\_rfa_backup`——因為範例模型本身就住在 `Program Files` 底下。那個位置通常需要管理員權限才能寫入，而且就算寫得進去，把備份檔倒進 Revit 安裝目錄本身也不是合理的落點。實測是靠明確傳入 `backupFolder` 參數迴避這個問題的，沒有驗證「推導失敗時的錯誤處理」路徑。**呼叫端規則**：專案檔位於唯讀、受保護、或 Revit 安裝目錄等非專案性質的路徑時，不要依賴預設推導，應明確指定 `backupFolder`。

**(b) 失敗時的錯誤訊息模板會誤導排查方向。** 工具在 `MissingParameters` 非空時回傳的訊息模板是「成功載入，但 N 個參數找不到（**可能該欄位不適用此類別，如 Door 案例的 `GreenMaterial_Window_ShadingCoefficient`**）」（`CommandExecutor.GM_RfaFamilyInjection.cs` 的回傳 `Message`）。但 2026-08-13／2026-08-31 兩次實測遇到的真實錯誤都是 `Shared parameter creation failed.`——與「欄位是否適用此類別」完全無關，是共享參數新增失敗，不是欄位選錯類別。照這個訊息去排查的人會被帶往錯誤方向（懷疑欄位定義而不是生命週期/環境問題）。此為待修項，訊息模板應改為如實反映 `missing` 清單中實際夾帶的錯誤字串，而不是套用一個固定的「可能不適用此類別」臆測句。

## 驗證協議（對應驗收條件「至少一個 Window 與一個 Door 案例」）

> **覆蓋紀錄（2026-08-31）**：下方「至少一個 Window 與一個 Door 案例」的要求**兩案例皆已實測通過**，均在 Revit 2024 / Autodesk 範例模型 Snowdon Towers 上執行（未存檔）：
>
> | | Window 案例 | Door 案例 |
> |---|---|---|
> | 基底 Family + Type | `Window-Fixed-Transom` `50" x 80"` | `Door-Passage-Single-Flush` `36" x 84"` |
> | 寫入 / 讀回 | **12 / 12** | **14 / 14**（含 Identity Data 3 欄） |
> | `MissingParameters` | 空陣列 | 空陣列 |
> | `GreenMaterial_Certified` | `Yes` | `Yes` |
> | `GreenMaterial_AcousticRw` | `35` | `32` |
> | `GreenMaterial_Window_ShadingCoefficient` | `0.42` | **不存在（Door 不適用，正確留空）** |
> | 規則 4 `AffectedExistingTypes` | 0（原家族 6 Type 未動） | 0（原家族 20 Type 未動，新家族 21 = 20 複本 + 1 新增） |
>
> 兩案例的驗證方式均為 `get_element_info` 逐欄讀回比對，不採信工具回傳的 `Success: true`。Door 案例特別確認了「`ShadingCoefficient` 對 Door 不適用應留空」這條**否定條件**——它不在 dryRun 的 `PlannedWrittenFields` 中，也不在實際寫入後的讀回結果中。

兩個類別都要各跑一次完整流程並各自產出以下紀錄，不可只驗 Window 就當 Door 也通過（門窗的 Identity Data 欄位集合、隔音/遮陽適用性不同，見規則3表格）：

- 基底 Family+Type 名稱（使用者指定的）
- 備份檔案的絕對路徑（且檔案實際存在）
- 新 Type 的 `GreenMaterial_Certified` = Yes（呼叫端傳 `certified: true`）：2026-08-31 根因修正後，此欄位與其他 `GreenMaterial_*` 欄位一樣是正常必填驗收欄位，不再是 best-effort——缺漏即代表該案例未通過，不可略過
- 新 Type 的 `GreenMaterial_Mat1_*` 值 vs. 來源型錄資料的比對
- Window 案例：`GreenMaterial_Window_ShadingCoefficient` 有值；Door 案例：此欄位應留空（不適用）
- 兩案例都要有 `GreenMaterial_AcousticRw`（若型錄有數據）
- 載入前後同類別 Type 清單 diff = 只多一筆，其餘不變

## 與其他 domain 的邊界

- **不是** `GM_parameter-schema.md` 的 Mat1~Mat6 系統家族路徑——那個路徑操作對象是專案內的系統族群 Type/Material（Wall/Floor/Ceiling），不涉及 `.rfa` 檔案本身。本 SOP 是唯一涉及「開另一份 Revit 文件（家族文件）」的綠建材注入路徑。
- 家族/類型盤點的通用前置檢查（截斷防護、0 實例驗證、連帶刪除揭露）沿用 `family-inventory-cleanup.md`，但本 SOP 是**新增**而非清整/刪除，所以只借用其「Type 清單快照比對」的方法論，不套用它的 purge/merge 決策流程。
