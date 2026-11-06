# Revit 綠建材元件導入邏輯、命名規則與工具鏈對照規範

本文件彙整綠建材導入 Revit 之**元件建構邏輯**、**實際命名規則**、**共享參數 Schema 摘要**，以及**目前的 MCP 工具鏈開發狀態**。

> **2026-08-12 全面改版說明**：舊版描述的是一套「獨立 Python 腳本」（`scripts/inject_auxiliary_materials.py`、`scripts/inject_loadable_family.py` 等）架構，這些腳本從未被實際開發；實際落地的是 C#（`MCP/Core/Commands/*.cs`）+ TypeScript（`MCP-Server/src/tools/*.ts`）的 MCP 工具鏈，透過 `/GM_import` → `/GM_inject revit` 兩支 Skill 呼叫。命名規則與共享參數數量（舊版寫 31 個）也與實際檔案不符，本次一併改正。

---

## 1. 元件產生邏輯與模式 (Component Logic & Modes)

| 注入模式 | 適用範圍 / 建築情境 | 實際 Revit 處理邏輯 | 多槽位 / 屬性處置 |
| :--- | :--- | :--- | :--- |
| **組合式 Mode（2 材料固定）**<br>*(Scenario 1)* | 牆面 (Walls) 板材+塗料的固定 2 材料組合 | `duplicate_element_type` 複製既有 WallType，指定 `finishMaterialName`/`structureMaterialName` 分配至 `Structure [1]` 與 `Finish 1 [4]`/`Finish 2 [5]` | Mat1 固定對應板材（Structure）、Mat2 固定對應塗料（Finish），寫入 `GreenMaterial_Mat1_*`/`Mat2_*` |
| **組合式 Mode（通用多材料）**<br>*(Scenario 3)* | Wall/Floor/Ceiling 任意品類、3 個以上材料，或有 `layerComposition` 明確層級設定 | `create_multi_layer_type` 依 `layers` 陣列（`materialName`/`layerFunction`/`thicknessMm`）建立任意層數的 CompoundStructure，並依開頭/結尾的 Finish 層自動設定 Core Boundary（`ExteriorShellLayers`/`InteriorShellLayers`） | 依 `materialSlotAssignment`（Structure > Finish > Substrate > Other 優先序）分配至 Mat1~Mat6，輔助材料也佔槽位但不佔物理層 |
| **分立式 Mode**<br>*(Scenario 2，各別建立)* | 每個材料各自成為獨立 Type（Floor/Wall/Ceiling），Type 內每層都用同一材料 | `create_single_material_type` 複製 Type 並把材料指派到每個構造層；地板飾面另呼叫 `set_material_surface_pattern` 套用磁磚網格/木紋填滿圖案 | 單一 `Mat1` 槽位完整對接 |
| **純材料附掛既有 Type**<br>*(Scenario 5)* | 無實體構造層的填縫劑/接著劑/防水膜，附掛到使用者指定的既有 Type | Path A（預設）：`duplicate_element_type`/`create_single_material_type` 複製一份新 Type 再寫參數，不影響舊元件；Path B：直接對既有 `typeId` 寫參數，會影響所有既有實例 | 該材料佔一個 `matN` 槽位（無 CompoundStructure 層）+ 對應的 `GreenMaterial_Adhesive`/`Sealant`/`Waterproofing` 頂層欄位 |
| **載入式家族 RFA**<br>*(Scenario 7，TASK-005.7)* | 門 (Doors)、窗 (Windows) 等載入式 Family | `inject_green_material_into_family`：`EditFamily` 開啟使用者指定的既有 FamilySymbol → 立即 `SaveAs` 備份 → 在家族文件內新增一個 Type（絕不改動來源 Type）→ 寫入 Identity Data + `GreenMaterial_Mat1_*` + 遮陽係數/隔音等級 → `SaveAs` 為新家族檔名 → `LoadFamily` 載回專案，並用載入前後 Type 簽章快照比對防止覆蓋非目標 Type；目標家族檔已存在時直接中止，不覆寫 | 寫入 `.rfa` 家族 Type 的 `GreenMaterial_Mat1_*`、`GreenMaterial_Window_ShadingCoefficient`、`GreenMaterial_AcousticRw` |
| **非幾何輔助材料附著**<br>*(跨 Scenario 3/5)* | 接著劑 (Adhesive)、填縫劑/矽利康 (Sealant)、防水膜 (Waterproofing) | 無獨立幾何層，寫入附著核心元件（Walls/Floors/Ceilings）Type 的 Construction 屬性群組頂層欄位 | 寫入 `GreenMaterial_Adhesive`／`GreenMaterial_Sealant`／`GreenMaterial_Waterproofing`（TEXT，格式 `"產品名稱 (標章編號)"`） |

---

## 2. 元件與類型命名規則 (Naming Convention)

以下取自 `.claude/skills/GM_inject/SKILL.md`、`.agents/skills/combined-wall-set-import/domain.md` 與 `domain/GM_rfa-family-injection.md` 實際遵循的規則，並在 TASK-005.7 端到端實測中驗證過。

| 物件類型 | 命名格式 | 實際產出範例 |
| :--- | :--- | :--- |
| **Scenario 1/3 組合式 Type**（Wall/Floor/Ceiling） | `TABC_<Set名稱>`（不含中括號、不含標章編號） | `TABC_室內牆與塗料`、`TABC_地磚與填縫` |
| **Scenario 2 各別建立 Type / Material**（同名） | `<licno>_<title>`（與 Material 名稱相同字串，不加 `TABC_` 前綴） | `GBM0104088_SHERA斯納板1.3I普通纖維水泥板` |
| **Material 名稱**（所有情境共用） | `GBM標章編號 + "_" + TABC材料完整名稱` | `GBM0104106_水性漆(居室外用)`；嚴禁自行加 `Finish`/`Structure` 後綴、嚴禁 `預設牆_` 前綴、嚴禁多個標章串接 |
| **RFA 家族檔案 (.rfa)** | `<原家族名稱><newFamilySuffix>_<certNo>.rfa`（`newFamilySuffix` 預設 `_TABC`） | `雙開落地窗- (2)_TABC_GBM0104092.rfa` |
| **RFA 家族內新 Type** | 由呼叫端指定 `newTypeName`，實務上採 `<原Type名稱>_TABC_<certNo>` | `180 x 210 cm_TABC_GBM0104092` |

`buiNaming` 欄位（如 `W_INT_RC15`、`C_INT_Ceiling`）是 `GM_generate_revit_injection_plan.py` 計畫報告書「BIM 建議命名」欄位的參考標籤，**不是**實際寫入 Revit 的 Type 名稱，兩者不要混淆。

---

## 3. 共享參數 (Shared Parameters) 欄位結構摘要

`GreenMaterial_SharedParams.txt` 實際共 **69 個共享參數**，劃分為 **5 大群組**（Group 4/5 是 TASK-005.5/005.6/005.7 陸續新增，舊版文件只列了前 3 組）：

| Group | 群組名稱 | 內容概要 | 欄位數 |
| :---: | :--- | :--- | :---: |
| 1 | 綠建材認證與產品標示 (Green Material Certificate & Identity) | `GreenMaterial_Certified` + Mat1~Mat6 的 `_Name`/`_CertNo`/`_Category`/`_SubCategory`/`_Applicant`/`_ValidUntil` | 37 |
| 2 | 綠建材物理與化學性能 (Green Material Properties) | Mat1/Mat2/Mat4/Mat5/Mat6 的 `_TVOC`/`_Formaldehyde`（Mat3 無此欄位） + `RecycledRatio` + `AcousticNRC` | 12 |
| 3 | 國家標準與試驗驗證 (CNS Standard & Testing Verification) | Mat1/Mat2/Mat4/Mat5/Mat6 的 `_CNSSpec`/`_TestItems`/`_QualifiedItems`（Mat3 無此欄位） | 15 |
| 4 | 非幾何輔助材料 (Construction) | `GreenMaterial_Adhesive`／`Sealant`／`Waterproofing` | 3 |
| 5 | 門窗專屬效能 (Window/Door Performance) | `GreenMaterial_Window_ShadingCoefficient`（僅 Window/Curtain Wall）／`GreenMaterial_AcousticRw`（Window 與 Door 皆適用） | 2 |

**Mat1~Mat6 六槽位架構**：Group 1/2/3 內的欄位依材料槽位（Mat1、Mat2、Mat4、Mat5、Mat6 各 11 欄；Mat3 較輕量僅 6 欄，無 TVOC/Formaldehyde/CNS）重複 6 次，共 64 欄，加上 Group 4（3）+ Group 5（2）= 69。完整欄位定義、槽位分配規則（`materialSlotAssignment`）、雙階層責任對照（Material 層 vs Type 層）以 `domain/GM_parameter-schema.md` 為權威來源，本節僅供概覽，不重複列出全部 69 個欄位名稱。

---

## 4. Revit 導入工具鏈開發狀態總表 (MCP Tools & Scripts Status)

| 工具 / 模組名稱 | 位置 | 對應 Task | 開發狀態 | 功能說明 |
| :--- | :--- | :---: | :---: | :--- |
| **共享參數載入與綁定** | MCP 工具 `load_shared_parameters`（`MCP/Core/Commands/CommandExecutor.GM_GreenMaterial.cs`） | Task 005.1 / 005.8 | **已完成** | 把 `GreenMaterial_SharedParams.txt` 的參數綁定至指定品類 Type 層級；CategorySet 擴充會檢查 `BindingMap.ReInsert()` 回傳值，失敗不誤報成功（2026-08-12 code review 修正） |
| **推送計畫擬訂引擎** | [`GM_generate_revit_injection_plan.py`](../../tools/green-material/GM_generate_revit_injection_plan.py) | Task 004 / 005.5 / 005.6 | **已完成** | 讀取 `/GM_import` 需求對齊文字，比對 `tabc_master_database.json`，動態推判 Revit 品類、構造層、厚度、Mat1~6 槽位分配 |
| **組合式 Type 建立（2 材料固定）** | MCP 工具 `duplicate_element_type` | Task 005.1 / 005.3 | **已完成** | Scenario 1：牆體板材+塗料固定 2 材料組合 |
| **組合式 Type 建立（通用多層）** | MCP 工具 `create_multi_layer_type` | Task 005.10 / 011 | **已完成** | Scenario 3：任意品類、任意材料數的多層構造，含 Core Boundary 自動判斷 |
| **分立式 Type 建立** | MCP 工具 `create_single_material_type` | Task 005.2 | **已完成** | Scenario 2：各別建立，含地板 Surface Pattern 套用 |
| **純材料附掛既有 Type** | MCP 工具 `duplicate_element_type` / `create_single_material_type` / `set_green_material_type_parameters`（Path A/B） | Task 005.5 | **已完成** | Scenario 5：填縫劑/接著劑/防水膜附掛既有 Type，`/GM_import` 先做唯讀 `get_types_by_category` 查詢供使用者挑選 |
| **載入式家族 RFA 注入** | MCP 工具 `inject_green_material_into_family`（`MCP/Core/Commands/CommandExecutor.GM_RfaFamilyInjection.cs`） | Task 005.7 | **已完成** | Scenario 7：門窗 RFA 備份、新增 Type、寫參數、防覆蓋驗證；2026-08-12 已在真實 Revit 專案完成 Window + Door 端到端實測 |
| **Set 比對與計畫刷新** | `GM_generate_revit_injection_plan.py` 的 `compare_and_refresh_set()` / `compare_all_sets()`，由 `/GM_set compare` 驅動 | — | **已完成** | 比對既有 Set 與最新 `tabc_master_database.json`（缺件/過期/改名），有差異就刷新計畫檔 |
| **pyRevit 工具面板 UI 橋接** | `pyRevit_Tools/RevitGreen.extension/` | — | ⏳ **未開始** | 目前所有寫入都經由 Claude Code / MCP 對話流程執行，沒有 Revit 內建工具列面板 |
| **Revit 明細表與 Excel 匯出** | — | — | 🚫 **不需要（2026-08-12 定案）** | 早期構想是依裝修面積自動算綠建材率並匯出 Excel 明細表；`GreenMaterial_DecorArea`/`QualifyArea`/`RatioContribution` 佔位欄位與計畫執行步驟裡的匯出提示已從 `GM_generate_revit_injection_plan.py` 移除，不是「尚未實作」，是確認不需要。`create_view_schedule` 工具本身仍存在，供其他一般明細表需求使用，只是不會為此需求另寫專用流程 |

---

## 5. 相關文件

- 計畫結構與擬訂流程：`Revit_GreenMaterial_Injection_Plan_Specification.md`
- 共享參數完整權威定義：`domain/GM_parameter-schema.md`
- 綠建材採購與材料庫規範：`domain/GM_catalog.md`
- RFA 家族注入規則：`domain/GM_rfa-family-injection.md`
- 各情境 Revit 寫入 SOP：`.claude/skills/GM_inject/SKILL.md`
