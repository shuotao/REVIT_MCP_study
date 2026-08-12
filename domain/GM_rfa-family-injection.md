---
name: GM_rfa-family-injection
description: "門窗／獨立元件（載入式 Family, .rfa）綠建材導入 SOP：以既有相似元件為基底另存備份、在 Family 文件內建立新 Type 並寫入 Identity Data 與遮陽/隔音等綠建材參數、再載回專案且不覆蓋非目標 Family Type。觸發關鍵字：門窗綠建材、獨立元件、RFA 導入、loadable family、防音門窗、Low-E玻璃、遮陽係數、隔音Rw、TASK-005.7。"
metadata:
  version: "1.0"
  updated: "2026-08-12"
  created: "2026-08-12"
  references:
    - "tools/green-material/archive/reports/Revit_Element_GreenMaterial_Mapping_Analysis.md 情境 7"
    - "MCP/Core/Commands/CommandExecutor.FamilyExport.cs（EditFamily/SaveAs/Close 既有前例）"
    - "GreenMaterial_SharedParams.txt（v4/v5 Schema）"
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

### 規則 3：Type Identity Data 與遮陽/隔音欄位落點

寫入分兩層，兩層都要落實，不可只寫一層：

| 資料 | 落點 | 內容 |
|---|---|---|
| **產品識別** | Family Type 內建 Identity Data 參數（`Manufacturer`／`Model`／`Description`／`URL`，依 Revit 版本與類別實際存在的欄位為準，不是每個都保證存在） | 綠建材標章字號、廠商、產品名稱（可與下方共享參數重複，Identity Data 是給人看的，共享參數是給明細表/QAQC 抓的） |
| **綠建材主資料** | `GreenMaterial_Mat1_*`（沿用 `GM_parameter-schema.md` 的 16 欄位 Mat1 槽位——門窗的玻璃或門扇視為該 Type 的主材料） | `Name`/`CertNo`/`Category`/`SubCategory`/`Applicant`/`ValidUntil`/`CNSSpec`/`TestItems`/`QualifiedItems`，只填有實際數據的欄位，不得杜撰 TVOC/甲醛 |
| **門窗專屬效能** | **新增專屬共享參數**（不沿用 Mat1 通用欄位，因為遮陽係數/隔音等級是門窗獨有、其他品類沒有對應意義）：`GreenMaterial_Window_ShadingCoefficient`（NUMBER，僅 Windows/Curtain Wall 適用）、`GreenMaterial_AcousticRw`（NUMBER，Windows 與 Doors 皆適用，對應型錄上的 Rw 隔音等級 dB） | 只在型錄/測試報告有明確數值時才寫，缺值就留空，不得估算填入 |

> **實作前必讀的編碼地雷**：`GreenMaterial_SharedParams.txt` 是 **cp950 (Big5) ANSI 編碼**，不是 UTF-8（檔案開頭註解已明講）。用一般 UTF-8 文字工具（含大多數程式碼編輯器的預設存檔）直接編輯或新增 PARAM 列，會把既有中文 GROUP/PARAM 說明重新存成亂碼，Revit 重新解析時會整批壞掉。新增 `GreenMaterial_Window_ShadingCoefficient`／`GreenMaterial_AcousticRw` 這兩個 PARAM 列時，必須用能指定 cp950 編碼寫檔的方式操作（例如 Python `open(path, "a", encoding="cp950")`），且新增前後都要驗證既有列的中文沒有變亂碼。GUID 沿用檔案既有的遞增慣例（目前最大到 `...111111111167`，新參數接續 `...168`/`...169`），並建議獨立一個 `GROUP 5「門窗專屬效能 (Window/Door Performance)」`，不要塞進既有 GROUP 1~4。

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
  → 寫 Identity Data + GreenMaterial_Mat1_* + 遮陽/隔音專屬參數（規則3）
  → SaveAs 為新家族檔名
  → 關閉家族文件（不覆寫原檔）
  → LoadFamily 回專案（規則4，帶 IFamilyLoadOptions）
  → 載回前後 Type 清單快照比對（規則4 強制驗證）
  → get_element_info 驗證新 Type 的共享參數值
  → 回報：新 Family/Type 名稱與 ID、備份檔路徑、寫入/缺漏欄位、受影響既有 Type 數量（應為 0）
```

## 驗證協議（對應驗收條件「至少一個 Window 與一個 Door 案例」）

兩個類別都要各跑一次完整流程並各自產出以下紀錄，不可只驗 Window 就當 Door 也通過（門窗的 Identity Data 欄位集合、隔音/遮陽適用性不同，見規則3表格）：

- 基底 Family+Type 名稱（使用者指定的）
- 備份檔案的絕對路徑（且檔案實際存在）
- 新 Type 的 `GreenMaterial_Mat1_*` 值 vs. 來源型錄資料的比對
- Window 案例：`GreenMaterial_Window_ShadingCoefficient` 有值；Door 案例：此欄位應留空（不適用）
- 兩案例都要有 `GreenMaterial_AcousticRw`（若型錄有數據）
- 載入前後同類別 Type 清單 diff = 只多一筆，其餘不變

## 與其他 domain 的邊界

- **不是** `GM_parameter-schema.md` 的 Mat1~Mat6 系統家族路徑——那個路徑操作對象是專案內的系統族群 Type/Material（Wall/Floor/Ceiling），不涉及 `.rfa` 檔案本身。本 SOP 是唯一涉及「開另一份 Revit 文件（家族文件）」的綠建材注入路徑。
- 家族/類型盤點的通用前置檢查（截斷防護、0 實例驗證、連帶刪除揭露）沿用 `family-inventory-cleanup.md`，但本 SOP 是**新增**而非清整/刪除，所以只借用其「Type 清單快照比對」的方法論，不套用它的 purge/merge 決策流程。
