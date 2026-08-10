# Revit 綠建材元件導入邏輯、命名規則與腳本開發對照規範

本文件彙整綠建材導入 Revit 之**元件建構邏輯**、**專屬命名規則**、**共享參數 Schema**，以及**已開發／未開始開發之腳本模組清單**。

---

## 1. 元件產生邏輯與模式 (Component Logic & Modes)

| 注入模式 | 適用範圍 / 建築情境 | Revit 處理邏輯 | 多槽位 / 屬性處置 |
| :--- | :--- | :--- | :--- |
| **組合式 Mode**<br>*(Combined Set)* | 牆面 (Walls)、樓板地坪 (Floors)、天花板 (Ceilings) 等複合層結構 | 複製既有系統元件類型 (`Duplicate Element Type`)，根據 Task 003 規範自動分配至 Finish 1 (飾面層)、Substrate (襯底層) 與 Structure (結構層) | 將組合內的多項綠建材標章，以多槽位 (`Mat1_*`, `Mat2_*`, `Mat3_*`) 寫入 Type Identity Data |
| **分立式 Mode**<br>*(Separate Component)* | 獨立材料、單一地坪塊、非複合結構材料 | 獨立為各綠建材 Duplicate 一個 Element Type 或 Material，分別寫入單一標章履歷與物理性能 | 單一槽位完整對接 (`GreenMaterial_*`) |
| **非幾何輔助材料**<br>*(Auxiliary Materials)* | 接著劑 (Adhesive)、填縫劑/矽利康 (Sealant)、防水膜 (Waterproofing) | 無獨立幾何厚度，改為將標章與性能寫入附著核心元件 (Walls/Floors) 之 Construction 群組 | 寫入 `GreenMaterial_Adhesive`, `GreenMaterial_Sealant`, `GreenMaterial_Waterproofing` |
| **載入式家族**<br>*(Loadable Family RFA)* | 門 (Doors)、窗 (Windows)、帷幕玻璃 (Curtain Panels) | 另存既有 `.rfa` 家族備份，將標章履歷與性能參數寫入 Family Type Parameters 後重新載入 | 寫入 `.rfa` 家族 Type 屬性 |

---

## 2. 元件與類型命名規則 (Naming Convention)

為確保在 Revit 屬性面板 (Properties) 與材料庫清單中具備極高辨識度，採用標準前綴與命名規範：

| 物件類型 | 命名格式 | 實際產出範例 |
| :--- | :--- | :--- |
| **組合式牆體/樓板 Type** | `[TABC] <Set名稱> (<標章1>, <標章2>)` | `[TABC] 牆壁與塗料 (GBM0104204, GBM0103960)` |
| **分立式獨立元件 Type** | `[TABC_<品類>] <綠建材產品名稱> (<標章編號>)` | `[TABC_Floor] 複合木質地板 (GBM0104194)` |
| **非幾何輔助材料 Name** | `[AUX_<次分類>] <產品名稱> (<標章編號>)` | `[AUX_Sealant] 建築用單組份矽利康 (GBM0104192)` |
| **載入式家族檔案 .rfa** | `[TABC_RFA] <原家族名稱>_綠建材版.rfa` | `[TABC_RFA] M_Single-Flush_綠建材版.rfa` |

---

## 3. 共享參數 (Shared Parameters) 欄位結構

共計 **31 個標準綠建材共享參數**，劃分為 5 大群組：

| 共享參數名稱 | 資料型別 (Type) | 屬性群組 (Group) | 說明與填寫範例 |
| :--- | :--- | :--- | :--- |
| `GreenMaterial_Certified` | `YESNO` | `Identity Data` | 是否通過綠建材認證 (True / False) |
| `GreenMaterial_CertNo` | `TEXT` | `Identity Data` | 標章證書編號 (例: `GBM0104204`) |
| `GreenMaterial_Category` | `TEXT` | `Identity Data` | 標章四大分類 (例: `健康綠建材`, `高性能綠建材`) |
| `GreenMaterial_SubCategory` | `TEXT` | `Identity Data` | 子類別 (例: `塗料類`, `地坪材`, `石膏板`) |
| `GreenMaterial_Applicant` | `TEXT` | `Identity Data` | 申請廠商名稱 (例: `中國製釉股份有限公司`) |
| `GreenMaterial_ValidUntil` | `TEXT` | `Identity Data` | 標章有效期限 (例: `119/07/08`) |
| `GreenMaterial_TVOC` | `NUMBER` | `Green Building` | TVOC 逸散率 (mg/m²h) (例: `0.08`) |
| `GreenMaterial_Formaldehyde`| `NUMBER` | `Green Building` | 游離甲醛逸散率 (mg/m²h) (例: `0.01`) |
| `GreenMaterial_RecycledRatio`| `NUMBER` | `Green Building` | 再生綠建材回收率 (%) |
| `GreenMaterial_CNSSpec` | `TEXT` | `Green Building` | 國家標準試驗法規 (例: `CNS16082 / CNS15200`) |
| `GreenMaterial_QualifiedItems`| `TEXT` | `Green Building` | 合格試驗項目描述 |
| `Mat1_CertNo`, `Mat2_CertNo` | `TEXT` | `Identity Data` | 多層槽位標章號碼 (組合式專用) |
| `Mat1_Name`, `Mat2_Name` | `TEXT` | `Identity Data` | 多層槽位材料名稱 (組合式專用) |
| `GreenMaterial_Adhesive` | `TEXT` | `Construction` | 附著黏貼之接著劑標章資訊 |
| `GreenMaterial_Sealant` | `TEXT` | `Construction` | 附著填縫之矽利康/密封膠資訊 |
| `GreenMaterial_Waterproofing`| `TEXT` | `Construction` | 附著塗佈之防水膜資訊 |

---

## 4. Revit 導入腳本與模組開發狀態總表 (Scripts & Modules Status)

| 腳本 / 模組名稱 | 檔案路徑 / 模組位置 | 對應 Task | 開發狀態 | 功能說明 |
| :--- | :--- | :---: | :---: | :--- |
| **共享參數檔載入與驗證模組** | [`validate_shared_params.py`](../../validate_shared_params.py) | Task 003 / 005.1 | **已完成** | 驗證並批次一鍵載入 `GreenMaterial_SharedParams.txt` (v5 schema，Mat1~Mat6 六槽位共 64 個參數) 至 Revit 核心品類 |
| **推送計畫擬訂引擎 v3** | [`generate_revit_injection_plan.py`](../../generate_revit_injection_plan.py) | Task 004 | **已完成** | 讀取網頁 Set 需求對齊問答 (Q1/Q2/Q3)，動態推判 Revit 品類、構造層與厚度 |
| **標準材料層注入引擎 (機制 A)** | [`apply_revit_injection_plan.py`](../../apply_revit_injection_plan.py) | Task 005.2 / 005.3 | **已完成** | 執行牆面/地坪/天花板之組合式與分立式 Element Type 複製、寫入與舊材料衝突偵測 |
| **元件履歷摘要表格產出腳本** | [`apply_revit_injection_plan.py`](../../apply_revit_injection_plan.py) | Task 005.4 | **已完成** | 歷史異動履歷已歸檔至 [`revit_generated_elements_summary.md`](../../tools/green-material/archive/reports/revit_generated_elements_summary.md) |
| **非幾何輔助材料注入腳本** | `scripts/inject_auxiliary_materials.py` | Task 005 (機制 B) | ⏳ **未開始** | 負責將接著劑、填縫劑、防水膜專屬寫入 Construction 屬性群組 |
| **載入式家族 RFA 注入腳本** | `scripts/inject_loadable_family.py` | Task 005 (機制 C) | ⏳ **未開始** | 負責門、窗、幕牆玻璃 `.rfa` 家族備份另存與 Type 屬性批次寫入 |
| **pyRevit 工具面板 UI 橋接腳本** | `pyRevit_Tools/RevitGreen.extension/` | Task 005 (pyRevit) | ⏳ **未開始** | 在 Revit 工具列面板建立 pyRevit IronPython / C# 一鍵注入面板按鈕 |
| **Revit 明細表與 Excel 匯出腳本** | `scripts/export_green_schedule_excel.py` | Task 006 / 008 | ⏳ **未開始** | 呼叫 `create_view_schedule` 生成 BIM 綠建材率計算明細表並匯出 Excel 報表 |
