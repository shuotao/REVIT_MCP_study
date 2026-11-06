---
name: GM_parameter-schema
description: "綠建材資訊在 Revit BIM 模型中的參數欄位定義與綁定規範。定義 v5 Multi-Material Slot 共享參數 Schema（GreenMaterial_Mat1~Mat6_* 共 64 欄位）、資料型別、Revit 參數群組，以及 Type 層級綁定與寫入工具。"
metadata:
  version: "3.1"
  updated: "2026-08-31"
  created: "2026-07-27"
  references:
    - "Revit Shared Parameter File Specification"
    - "內政部建築研究所綠建材評定驗證標準"
    - "GreenMaterial_SharedParams.txt (v5 Schema — 6-Slot Multi-Material Architecture)"
  related:
    - GM_catalog.md
    - finish-schedule-governance.md
  referenced_by: []
  tags: [綠建材, Revit參數, SharedParameters, Material, ElementType, BIM資訊, 明細表]
---

# 綠建材 Revit 參數與標註規範 (`GM_parameter-schema`)

本文件定義由 TABC 綠建材採購指南擷取之建材資訊，在 Revit BIM 專案模型中掛載之標準參數名稱、資料型別、參數群組及載體綁定層級（Binding Targets）。

> **v2.0 變更說明**：舊版（v1.0）定義的 `GBM_*` 7 欄位單槽位 Schema **從未被實際載入 Revit**，與現行 `GreenMaterial_SharedParams.txt`（已透過 `load_shared_parameters` 工具實際綁定）完全不同名。v2.0 改為記錄實際生效的 v4 Multi-Material Slot Schema（Mat1/Mat2/Mat3 三槽位，31 欄位），避免 AI 依舊版寫入不存在的參數名稱。
>
> **v3.1 變更說明**：新增第 6 節「標章效期與資料新鮮度」，把「不得寫入已失效標章」由 skill 層的散文升格為方法層規則。既有 Schema、欄位、GUID 完全未變（issue #128）。
>
> **v3.0 變更說明**：Scenario 3（`create_multi_layer_type` 通用多材料單一組合）允許一個 Set 有 4 個以上材料，但 v4 schema 只有 3 個槽位，超過的材料只能建立實體構造層、沒有共享參數紀錄。v3.0 新增 Mat4/Mat5/Mat6 三個槽位（欄位形狀與 Mat1/Mat2 相同，含 TVOC/Formaldehyde/CNS），共 64 欄位，讓槽位數等於材料數（上限 6）。既有 Mat1/Mat2/Mat3 的 GUID 與欄位定義完全未變，純新增、非破壞性變更。

---

## 1. 綠建材標準共享參數 Schema (GreenMaterial Shared Parameters, v5)

實際載入 Revit 的共享參數檔為 `GreenMaterial_SharedParams.txt`，採「多材料槽位」架構：一個 ElementType（如組合牆/組合樓板）最多可掛載 **Mat1、Mat2、Mat3、Mat4、Mat5、Mat6** 六個材料槽位，共 **64 個參數**，統一前綴 `GreenMaterial_`。槽位數不等於一定要填滿 6 組——一個 Set 有幾種材料，就只填寫對應的 matN 物件（`set_green_material_type_parameters` 的 `mat1`..`mat6` 皆為選填），未使用的槽位保持空白，不強制寫入。

### 1.1 全域欄位（不分槽位，Group 1/2）

| 參數名稱 | 資料型別 | 說明 |
| :--- | :--- | :--- |
| `GreenMaterial_Certified` | YESNO | 全牆綠建材評定合格狀態 |
| `GreenMaterial_RecycledRatio` | NUMBER | 再生材料回收摻配率 (%) |
| `GreenMaterial_AcousticNRC` | NUMBER | 高性能吸音建材吸音係數 (NRC / SAA) |

### 1.2 Mat1、Mat2、Mat4、Mat5、Mat6 欄位（各 11 個，共 55 個，欄位形狀完全相同）

| 欄位後綴 | 資料型別 | 說明 | Group |
| :--- | :--- | :--- | :--- |
| `_Name` | TEXT | 綠建材產品名稱 | 1 |
| `_CertNo` | TEXT | 綠建材標章證書字號（如 `GBM0103810`） | 1 |
| `_Category` | TEXT | 標章大類（`健康` / `高性能` / `再生` / `生態`） | 1 |
| `_SubCategory` | TEXT | 綠建材細項品類 | 1 |
| `_Applicant` | TEXT | 標章申請廠商名稱 | 1 |
| `_ValidUntil` | TEXT | 標章有效期限 | 1 |
| `_TVOC` | NUMBER | TVOC 逸散率 (mg/m²·h) | 2 |
| `_Formaldehyde` | NUMBER | 甲醛逸散率 (mg/m²·h) | 2 |
| `_CNSSpec` | TEXT | CNS 國家標準與試驗規範 | 3 |
| `_TestItems` | TEXT | 試驗項目與檢測數據範疇 | 3 |
| `_QualifiedItems` | TEXT | 合格項目與評定結果 | 3 |

例：`GreenMaterial_Mat1_CertNo`、`GreenMaterial_Mat2_TVOC`、`GreenMaterial_Mat5_CNSSpec`。Mat1/Mat2 沿用 v4 既有語意（主體/牆板、面材/塗料），Mat4/Mat5/Mat6 是 v3.0 新增的「追加構造層」槽位，欄位形狀與 Mat1/Mat2 完全相同，供 Scenario 3 多材料組合中第 4 個以後的材料使用。

### 1.3 Mat3（附屬/膠材）欄位（僅 6 個基本欄位，無 TVOC/Formaldehyde/CNS）

| 欄位後綴 | 資料型別 | 說明 |
| :--- | :--- | :--- |
| `_Name` / `_CertNo` / `_Category` / `_SubCategory` / `_Applicant` / `_ValidUntil` | TEXT | 同 1.2，僅識別資料（Group 1），無性能與試驗欄位 |

Mat3 是唯一維持輕量 6 欄位形狀的槽位（沿用 v4 原始定義，未變更），語意仍是「附屬/膠材」，但在 Scenario 3 的六槽位分配規則中，第 3 順位材料一律進 Mat3，即使該材料是真實構造層而非附屬材料，也只會寫入這 6 個基本欄位（CNS/試驗數據等會遺漏）——這是唯一與其他槽位不對等的地方，見下方「六槽位分配規則」。

**⚠️ Mat1/Mat2 槽位對應不可顛倒（僅限 Scenario 1 固定 2 材料情境）**：`combined-wall-set-import` 情境中，Mat1 固定對應**板材/牆板**（CompoundStructure 的 `Structure [1]` 層），Mat2 固定對應**塗料/面材**（`Finish 1 [4]` / `Finish 2 [5]` 層）。寫入前務必依此對應，不得依材料在 Set 清單中的順序隨意分配。Scenario 3（3 個以上材料）改用下方「六槽位分配規則」決定槽位，不套用這條 Mat1=Structure/Mat2=Finish 的簡單假設。

**明確層級覆寫（`layerComposition`）**：當使用者在 green-material-showcase.html 的材料 Set 問答中，對 Wall 或 Floor 選擇「單一組合」，且該 Set 有 2 項以上材料時，網頁會請使用者為每項材料指定明確的複合構造角色與拖曳排序，並存入該 Set 物件的 `layerComposition` 欄位（同步匯出於 `exported_material_sets.json`）：

```json
"layerComposition": {
  "category": "Wall",
  "sequence": [
    { "type": "boundary" },
    { "type": "material", "licno": "GBM0104204", "role": "Structure" },
    { "type": "boundary" },
    { "type": "material", "licno": "GBM0104194", "role": "Finish" }
  ]
}
```

- `sequence` 是**有序陣列**，順序即為 Revit CompoundStructure 由外而內（或由上而下，依 category 而定）的實際層序，使用者可在網頁上直接拖曳整列調整。
- 陣列項目分兩種 `type`：
  - `material`：`role` 僅三種取值：`Structure`（結構層，對應 `Structure [1]`）、`Substrate`（底材層，對應 `Substrate [2]`）、`Finish`（面材層，對應 `Finish 1 [4]` / `Finish 2 [5]`）。
  - `boundary`：Core Boundary 分界線（Revit 結構材料與外部材料的分界，CompoundStructure 中不填入實際材料），純位置標記、無 `licno`。網頁預設會在 `Structure` 角色材料的緊上方與緊下方各放一個 `boundary` 項目（對應 Revit Edit Assembly 對話框固定出現的兩條 Core Boundary），使用者可拖曳調整其位置。
- **`layerComposition` 存在時，其 `sequence` 順序與角色即為權威來源，優先於上方「Mat1=Structure / Mat2=Finish」的預設順序假設**；`/GM_import` 擬訂寫入計畫與 `/GM_inject` 實際寫入時，若 Set 帶有 `layerComposition`，須依此欄位分配 Mat1~Mat6 槽位與 CompoundStructure 層順序，而非依材料在 `items` 陣列中的原始順序推測。`boundary` 項目本身不對應任何材料槽位，僅供標示 Core Boundary 在層序中的插入位置。
- 若 Set 沒有 `layerComposition`（例如各別建立模式、或 Wall/Floor 單一組合但使用者略過此設定），沿用既有的固定順序假設。

**六槽位分配規則（`materialSlotAssignment`）**：Scenario 3（`create_multi_layer_type` 的通用多材料單一組合）理論上可以有 7 個以上材料，但 Mat1~Mat6 只有 6 個槽位。槽位數原則上等於材料數（材料數 <= 6 時全部都會分到槽位）；只有超過 6 個材料時才需要判斷「哪些進槽位、哪些留空」。這個判斷**不是由執行 `/GM_inject revit` 的 AI Agent 臨場決定**，而是 `GM_generate_revit_injection_plan.py` 的 `_assign_material_slots()` 依固定規則計算，寫入 `plan['materialSlotAssignment']`（`{ assignment: { mat1..mat6 }, unassigned: [...] }`）與每個 `materialsMapping[i].assignedSlot`：

1. 優先序固定為 `Structure > Finish > Substrate > Other`（`Other` 為判斷不出角色的材料，優先序最低）。
2. 同優先序內，依材料在 `materialsMapping`（已依 `layerComposition.sequence` 或 Master DB 原始順序排列）中的先後順序決定。
3. 取排序後前 6 名依序進 `mat1`→`mat2`→`mat3`→`mat4`→`mat5`→`mat6`；超過 6 個才會有材料標記 `assignedSlot: null`，列在 `unassigned` 中——這些材料仍會被寫入真實的 CompoundStructure 層與獨立 Material，只是沒有 `GreenMaterial_Mat*` 共享參數紀錄。
4. 排序後第 3 名固定進 `mat3`（輕量 6 欄位槽位），因此即使是真實構造層材料，只要排到第 3 順位，CNS/試驗數據等欄位仍會遺漏——這是槽位形狀不對等（見 1.3）在分配規則上的直接影響，非分配規則本身的例外。

`/GM_import` 與 `/GM_inject revit` 一律讀取 `plan['materialSlotAssignment']` 的計算結果，不得重新自行判斷分配順序——同一個 Set 不論何時重跑，分配結果必須一致。

---

## 2. Revit 載體綁定規範 (Carrier Binding Rules)

* **綁定層級**：上述 64 個參數綁定於 **`ElementType`（Type 層級）**，不綁定 Instance，也不綁定 `Material` 物件本身——`Material.Name` 直接採用 `GBM編號_TABC材料完整名稱` 命名（見 `GM_catalog.md` 與 `.agents/skills/combined-wall-set-import/domain.md`），不另外掛參數。
* **綁定品類**：依 Type 所屬品類決定（`WallType` → `Walls`，`FloorType` → `Floors`，`CeilingType` → `Ceilings` 等）。
* **綁定工具**：`load_shared_parameters`（`filePath` 指向 `GreenMaterial_SharedParams.txt`，`categories` 指定目標品類，`bindToInstance: false`）。同一品類只需綁定一次；重複呼叫會被冪等跳過。
* **寫入工具**：`set_green_material_type_parameters`（`typeId` + 選填的 `certified` / `recycledRatio` / `acousticNRC` / `mat1`~`mat6` 物件）。一個 Set 有幾種材料就只傳幾個 matN 物件，不必六組全填。若品類尚未綁定，對應欄位會回傳於 `MissingParameters`，不會拋出例外。

### 2.1 雙階層欄位責任對照（TASK-005.3）

`tools/green-material/archive/reports/Revit_Element_GreenMaterial_Mapping_Analysis.md`（未隨本 repo 收編——原始 PR 分支的 `archive/` 目錄含第三方 TABC 資料，收編時整批排除，見 `tools/green-material/README.md`「`archive/` 目錄未隨本 repo 收編」一節）情境 3 是 TASK-003 階段的**早期分析提案**，曾建議「Material 層存放完整參數、Type 層只存摘要字串」；本節記錄的是**實際落地並生效**的相反安排（v2.0 定案，2.1 節已明文），TASK-005.3 的驗收基準以本節為準：

| 層級 | 承載物件 | 內容 | 範例 |
| :--- | :--- | :--- | :--- |
| **Material 層**（`OST_Materials`） | `Material` 元素 | **僅識別資料**：`Material.Name` = `GBM編號_TABC材料完整名稱`，不掛任何共享參數 | `GBM0104088_SHERA斯納板1.3I普通纖維水泥板` |
| **Type 層**（`ElementType.Identity Data`） | `WallType`/`FloorType`/`CeilingType` | **完整資料**：64 個 `GreenMaterial_*` 共享參數（Mat1~Mat6 六槽位 + Group 1/2/3 全域欄位），含證號、廠商、效期、TVOC/甲醛、CNS 試驗數據 | 見下方驗證案例 |

責任分工不重複、不衝突：Material 層只負責「這一層構造用的是哪個材質」（用名稱本身編碼識別碼），Type 層負責「這個元件完整的綠建材履歷」——同一份 CertNo/Applicant/TVOC 等資料只在 Type 層寫一次，Material 層不重複存放，避免兩處資料不同步。非幾何輔助材料（填縫劑/接著劑/防水材料）沒有實體構造層、因此**沒有對應的 Material 元素**，只存在於 Type 層的 `matN` 槽位 + `GreenMaterial_Adhesive`/`Sealant`/`Waterproofing` 欄位。

**2026-08-12 端到端交叉驗證**（現有模型既有案例，未新增寫入）：

- **Walls**：`TypeId 263551`（"TABC_室內牆與塗料"）— `get_element_info` 確認 `GreenMaterial_Mat1_*`（GBM0104088 板材，Structure）與 `GreenMaterial_Mat2_*`（GBM0104009 塗料，Finish）64 欄位子集完整無缺；`get_all_materials` 交叉比對兩個 `MaterialId`（263555、263553）的 `Name` 均精確等於 `GBM編號_材料名稱`，無多餘參數。
- **Floors**：`TypeId 264332`（"TABC_地磚與填縫"）— Mat1（GBM0102995 陶瓷面磚，實體構造層）與 Mat2（GBM0104110 填縫劑，`isAuxiliary`）皆完整寫入 Type 層，另有 `GreenMaterial_Sealant` 摘要欄位；`get_all_materials` 確認 GBM0102995 有對應 Material 元素（264334），GBM0104110 **查無 Material 元素**（`Count: 0`）——驗證輔助材料「無實體構造層、不建立 Material」的設計。
- **MissingParameters 機制**：兩案例的 `set_green_material_type_parameters` 呼叫（見 log/2026-08.md 與 exported_material_sets.json 歷史記錄）在對應品類已綁定共享參數時，`MissingParameters` 皆為空陣列；機制本身（品類未綁定時回傳缺漏清單而非拋例外靜默失敗）已由 `load_shared_parameters` 的冪等綁定與 `set_green_material_type_parameters` 的 `MissingParameters` 回傳欄位保證，不需要額外程式碼。

---

## 3. Revit 參數群組歸類 (Parameter Grouping)

在 Revit 屬性面板 (Properties Palette) 中，綠建材參數依 `GreenMaterial_SharedParams.txt` 定義歸類於下列群組：
* **`綠建材認證與產品標示` (Group 1)**：`GreenMaterial_Certified`、所有 `_Name` / `_CertNo` / `_Category` / `_SubCategory` / `_Applicant` / `_ValidUntil` 欄位。
* **`綠建材物理與化學性能` (Group 2)**：`_TVOC`、`_Formaldehyde`、`GreenMaterial_RecycledRatio`、`GreenMaterial_AcousticNRC`。
* **`國家標準與試驗驗證` (Group 3)**：`_CNSSpec`、`_TestItems`、`_QualifiedItems`。

---

## 4. 明細表 (Schedule) 與 QAQC 審查相容性

本規範定義之參數能無縫支援下列 Revit 自動化操作：
1. **綠建材數量統計明細表 (Green Material Takeoff)**：可依 `GreenMaterial_Mat1_Category` / `Mat2_Category` 與對應 `_CertNo` 分組統計全案綠建材總面積與使用率。
2. **顏色視覺化檢查 (Visual Coloring Review)**：可透過 `override_element_graphics` 自動對有/無綠建材認證（`GreenMaterial_Certified`）之牆面與地板進行彩繪標示（綠色=通過綠建材認證）。

---

## 5. 對話互動與資源導引規範 (Showcase Link Auto-Attachment)

當使用者詢問任何關於綠建材材料、Revit 共享參數 schema、標註規範或數量明細時，AI Agent 的回覆**必須於末尾自動貼出展示網頁連結**：
* `assets/green-material-showcase.html`（本機產生物，非版控檔；由 /GM_update 從 assets/green-material-showcase.template.html 產生，見 tools/green-material/README.md）

---

## 6. 標章效期與資料新鮮度 (License Validity & Data Freshness)

TABC 綠建材標章有有效期限，記錄在資料庫的 `period` 欄位（格式 `115/07/09 ~ 119/07/08`，民國年）。本規範定義寫入前的效期判定與資料新鮮度要求，適用於所有把標章資訊寫進 Revit 的路徑。

### 6.1 硬性規則：不得靜默寫入已失效標章

**規則**：任一材料的 `period` 結束日早於執行當日時，**不得在未經使用者明確核准的情況下**將其寫入 `GreenMaterial_Mat*_CertNo` / `_ValidUntil` 或任何綠建材參數。

**理由（為什麼是方法層規則，不只是便利性）**：寫入的 Type 會進入交付模型、綠建材數量明細表（第 4 節的統計用途），以及據此產出的送審文件。一個已失效的證號在這三處都會被當成有效憑證讀，錯誤會離開 BIM 團隊、進到行政程序，且不會在模型內部產生任何矛盾訊號可供事後察覺。

**判定方法**：`tools/green-material/GM_generate_revit_injection_plan.py` 的 `_period_end_expired()`，以 `period` 的結束日與 `datetime.date.today()` 比較。

**格式異常不視為過期**：`period` 缺漏、無 `~`、或民國年日期解析失敗時一律回傳「未過期」。把解析失敗當成過期會讓資料品質問題偽裝成合規問題，擋下實際有效的材料。這類材料在計畫報告的第 0 節會落在「效期格式無法解析，未被判定為過期」的敘述下，是明示的已知邊界，不是靜默通過。

**執行分工**：
| 層 | 責任 |
|---|---|
| 計畫引擎（Python） | **標記**：`generate_injection_plan()` 產出 `expiredLicenses` 清單、`hasExpiredLicense` 布林，以及每項材料的 `licenseExpired` / `licenseValidUntil` |
| `/GM_import` | **告知**：擬訂計畫後於摘要列出過期標章，並預告 `/GM_inject` 會擋下來 |
| `/GM_inject` | **擋下**：寫入任何 Revit 工具之前停止，列出過期清單，要求明確核准；核准後於回報與 `plannedActions` 明載哪些欄位帶著失效標章 |

擬訂計畫階段刻意**不**擋——擬訂是唯讀動作，使用者要先看得到是哪幾項過期，才能決定換料或重抓資料。

### 6.2 資料新鮮度：本機快照的年齡必須被讀回

`tabc_master_database.json` 是本機專屬、由 `/GM_update` 從 TABC 官網抓取產生的快照（見 `tools/green-material/README.md`），可能任意舊而不會有任何徵兆。

**規則**：任何依據該資料庫做判斷的流程，開始前必須讀回它的抓取時間並回報給使用者。超過 **30 天** 視為舊，應主動建議 `/GM_update`——但這是建議、不是硬擋（使用者可能正在離線環境作業，或明知資料舊仍要先看計畫形狀）。

30 天的依據：TABC 標章的核發與續證是月級節奏，一個月足以涵蓋一輪異動。

**時間戳兩級來源，必須明示採用了哪一級**：
1. `tabc_master_database.meta.json` 的 `fetchedAt` —— `/GM_update` 真實抓取後寫入的**確據**。（主資料庫本身是純 JSON 陣列，沒有可放中繼資料的外層物件，故時間戳寫在旁生檔；該檔與主資料庫同樣不入版控。）
2. 主資料庫檔案的 mtime —— 旁生檔不存在時的**推估值**。複製、rsync、還原備份都會讓它與真實抓取時間脫鉤。

回報時不得把第 2 級當成第 1 級陳述。`database_freshness()` 的 `fetchedAtSource` 欄位即為此而存在。

**資料庫不存在不是錯誤**：全新 clone 後本來就沒有這個檔，`/GM_update` 就是首次建立入口。應回報可執行的下一步，而非拋出檔案不存在的錯誤。
