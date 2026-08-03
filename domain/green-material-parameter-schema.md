---
name: green-material-parameter-schema
description: "綠建材資訊在 Revit BIM 模型中的參數欄位定義與綁定規範。定義 v4 Multi-Material Slot 共享參數 Schema（GreenMaterial_Mat1/2/3_* 共 31 欄位）、資料型別、Revit 參數群組，以及 Type 層級綁定與寫入工具。"
metadata:
  version: "2.0"
  updated: "2026-08-03"
  created: "2026-07-27"
  references:
    - "Revit Shared Parameter File Specification"
    - "內政部建築研究所綠建材評定驗證標準"
    - "GreenMaterial_SharedParams.txt (v4 Schema — Multi-Material Slot Architecture)"
  related:
    - green-material-catalog.md
    - finish-schedule-governance.md
  referenced_by: []
  tags: [綠建材, Revit參數, SharedParameters, Material, ElementType, BIM資訊, 明細表]
---

# 綠建材 Revit 參數與標註規範 (`green-material-parameter-schema`)

本文件定義由 TABC 綠建材採購指南擷取之建材資訊，在 Revit BIM 專案模型中掛載之標準參數名稱、資料型別、參數群組及載體綁定層級（Binding Targets）。

> **v2.0 變更說明**：舊版（v1.0）定義的 `GBM_*` 7 欄位單槽位 Schema **從未被實際載入 Revit**，與現行 `GreenMaterial_SharedParams.txt`（已透過 `load_shared_parameters` 工具實際綁定）完全不同名。本版改為記錄實際生效的 v4 Multi-Material Slot Schema，避免 AI 依舊版寫入不存在的參數名稱。

---

## 1. 綠建材標準共享參數 Schema (GreenMaterial Shared Parameters, v4)

實際載入 Revit 的共享參數檔為 `GreenMaterial_SharedParams.txt`，採「多材料槽位」架構：一個 ElementType（如組合牆）最多可掛載 **Mat1（主體/牆板）、Mat2（面材/塗料）、Mat3（附屬/膠材）** 三個材料槽位，共 **31 個參數**，統一前綴 `GreenMaterial_`。

### 1.1 全域欄位（不分槽位，Group 1/2）

| 參數名稱 | 資料型別 | 說明 |
| :--- | :--- | :--- |
| `GreenMaterial_Certified` | YESNO | 全牆綠建材評定合格狀態 |
| `GreenMaterial_RecycledRatio` | NUMBER | 再生材料回收摻配率 (%) |
| `GreenMaterial_AcousticNRC` | NUMBER | 高性能吸音建材吸音係數 (NRC / SAA) |

### 1.2 Mat1（主體/牆板）與 Mat2（面材/塗料）欄位（各 11 個，共 22 個）

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

例：`GreenMaterial_Mat1_CertNo`、`GreenMaterial_Mat2_TVOC`。

### 1.3 Mat3（附屬/膠材）欄位（僅 6 個基本欄位，無 TVOC/Formaldehyde/CNS）

| 欄位後綴 | 資料型別 | 說明 |
| :--- | :--- | :--- |
| `_Name` / `_CertNo` / `_Category` / `_SubCategory` / `_Applicant` / `_ValidUntil` | TEXT | 同 1.2，僅識別資料（Group 1），無性能與試驗欄位 |

**⚠️ 槽位對應不可顛倒**：`combined-wall-set-import` 情境中，Mat1 固定對應**板材/牆板**（CompoundStructure 的 `Structure [1]` 層），Mat2 固定對應**塗料/面材**（`Finish 1 [4]` / `Finish 2 [5]` 層）。寫入前務必依此對應，不得依材料在 Set 清單中的順序隨意分配。

---

## 2. Revit 載體綁定規範 (Carrier Binding Rules)

* **綁定層級**：上述 31 個參數綁定於 **`ElementType`（Type 層級）**，不綁定 Instance，也不綁定 `Material` 物件本身——`Material.Name` 直接採用 `GBM編號_TABC材料完整名稱` 命名（見 `green-material-catalog.md` 與 `.agents/skills/combined-wall-set-import/domain.md`），不另外掛參數。
* **綁定品類**：依 Type 所屬品類決定（`WallType` → `Walls`，`FloorType` → `Floors`，`CeilingType` → `Ceilings` 等）。
* **綁定工具**：`load_shared_parameters`（`filePath` 指向 `GreenMaterial_SharedParams.txt`，`categories` 指定目標品類，`bindToInstance: false`）。同一品類只需綁定一次；重複呼叫會被冪等跳過。
* **寫入工具**：`set_green_material_type_parameters`（`typeId` + 選填的 `certified` / `recycledRatio` / `acousticNRC` / `mat1` / `mat2` / `mat3` 物件）。若品類尚未綁定，對應欄位會回傳於 `MissingParameters`，不會拋出例外。

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
* [綠建材動態篩選與 Revit 參數預覽 Showcase 頁面](../assets/green-material-showcase.html)
