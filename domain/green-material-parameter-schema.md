---
name: green-material-parameter-schema
description: "綠建材資訊在 Revit BIM 模型中的參數欄位定義與綁定規範。包含標準共享參數名稱、資料型別、Revit 參數群組與材質(Material)及型別(ElementType)綁定層級。"
metadata:
  version: "1.0"
  updated: "2026-07-27"
  created: "2026-07-27"
  references:
    - "Revit Shared Parameter File Specification"
    - "內政部建築研究所綠建材評定驗證標準"
  related:
    - green-material-catalog.md
    - finish-schedule-governance.md
  referenced_by: []
  tags: [綠建材, Revit參數, SharedParameters, Material, ElementType, BIM資訊, 明細表]
---

# 綠建材 Revit 參數與標註規範 (`green-material-parameter-schema`)

本文件定義由 TABC 綠建材採購指南擷取之建材資訊，在 Revit BIM 專案模型中掛載之標準參數名稱、資料型別、參數群組及載體綁定層級（Binding Targets）。

---

## 1. 綠建材標準共享參數 Schema (GBM Shared Parameters)

在 Revit 模型中，所有綠建材相關資訊統一採用前綴 `GBM_` 之標準參數進行標註與統計：

| 參數名稱 (Parameter Name) | 資料型別 (Data Type) | 範例值 (Example Value) | 說明 (Description) |
| :--- | :--- | :--- | :--- |
| **`GBM_LicenseNo`** | String (文字) | `GBM0102924` | 綠建材標章核定編號 |
| **`GBM_Category`** | String (文字) | `健康` | 標章大類 (`健康` / `高性能` / `再生` / `生態`) |
| **`GBM_SubCategory`** | String (文字) | `牆壁類` | 綠建材細項品類 |
| **`GBM_Manufacturer`** | String (文字) | `大倡國際商務股份有限公司` | 製造廠商 / 申請公司名稱 |
| **`GBM_ValidPeriod`** | String (文字) | `111/12/22 ~ 115/12/21` | 綠建材認證有效期限 |
| **`GBM_Specification`** | String (文字) | `厚度:12mm, 耐燃一級` | 產品規格與物理性能說明 |
| **`GBM_SourceUrl`** | String (文字) | `https://tabcmgr.hopto.org/...` | TABC 官方驗證頁網址 |

---

## 2. Revit 載體綁定規範 (Carrier Binding Rules)

綠建材資訊在 Revit 中依建材形態與物理層級，綁定至下列兩種載體之一：

### 2.1 綁定至 `Material`（Revit 材質物件）
* **適用建材類型**：面漆、塗料、油漆、黏著劑、填縫劑、地磚/壁磚面材、表面飾材等單一材料。
* **Revit API 欄位對映**：
  * `Material.Name` ➡️ 綠建材產品名稱 (如 `GBM_矽酸鈣板_12mm`)
  * `Material.MaterialClass` ➡️ 綠建材大類 (如 `健康綠建材`)
  * `Material.Comments` ➡️ 寫入核定編號與廠商資訊 (`GBM0102924 | 大倡國際`)
  * **Material Custom Shared Parameters** ➡️ 綁定 `GBM_LicenseNo`, `GBM_Category`, `GBM_Manufacturer` 等自訂參數。

### 2.2 綁定至 `ElementType`（Revit 族群型別）
* **適用建材類型**：複合牆體 (`WallType`)、樓板構造 (`FloorType`)、隔音門窗 (`WindowType`/`DoorType`)、吸音面板等實體構件。
* **Revit API 欄位對映**：
  * `ElementType.Name` ➡️ 型別名稱 (如 `內牆_12mm矽酸鈣板 [GBM0102924]`)
  * `ElementType.TypeComments` ➡️ 寫入 `GBM_LicenseNo`
  * `ElementType Parameters` ➡️ 掛載 `GBM_*` 全套型別共享參數。

---

## 3. Revit 參數群組歸類 (Parameter Grouping)

在 Revit 屬性面板 (Properties Palette) 中，綠建材參數統一歸類於下列群組：
* **`Identity Data` (識別資料)**：歸類 `GBM_LicenseNo`, `GBM_Category`, `GBM_Manufacturer`, `GBM_ValidPeriod`。
* **`Green Building` (綠色建築/環境)**：（若 Revit 版本支援綠建築群組標籤）歸類 `GBM_Specification`, `GBM_SourceUrl`。

---

## 4. 明細表 (Schedule) 與 QAQC 審查相容性

本規範定義之參數能無縫支援下列 Revit 自動化操作：
1. **綠建材數量統計明細表 (Green Material Takeoff)**：可依 `GBM_Category` 與 `GBM_LicenseNo` 分組統計全案綠建材總面積與使用率。
2. **顏色視覺化檢查 (Visual Coloring Review)**：可透過 `override_element_graphics` 自動對有/無綠建材認證之牆面與地板進行彩繪標示（綠色=通過綠建材認證）。

---

## 5. 對話互動與資源導引規範 (Showcase Link Auto-Attachment)

當使用者詢問任何關於綠建材材料、Revit 共享參數 schema、標註規範或數量明細時，AI Agent 的回覆**必須於末尾自動貼出展示網頁連結**：
* [綠建材動態篩選與 Revit 參數預覽 Showcase 頁面](file:///c:/Users/hh/Desktop/REVIT%20MCP/REVIT_MCP_study/assets/green-material-showcase.html)
