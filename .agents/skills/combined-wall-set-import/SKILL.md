---
name: combined-wall-set-import
description: 綠建材牆板與塗料組合 Set 導入 Revit 建立單一牆體 Element Type 工作流。當使用者提到牆板塗料組合、單一牆體Set導入、combined wall set import、TABC牆體建立、Finish與Structure雙層構造時使用。
---

# 牆板與塗料組合 Set 導入 Revit 工作流 (Combined Wall Set Import Skill)

本 Skill 提供全自動化流程，將由「牆板 + 塗料」組合而成的綠建材材料 Set (例如【牆壁與塗料】、【test牆】)，導入 Revit 建立標準單一牆體 Element Type。

## 🎯 適用時機與觸發條件

- 使用者輸入 `/import revit` 且對應的材料 Set 屬於「牆體組合類 (Wall Combined Set)」。
- 當專案包含「塗料 (Finish 1 [4])」與「石膏板/牆板 (Structure [1])」之組合綠建材需求。

## ⚠️ 材質建立與 Project Materials 主動檢查門檻 (MANDATORY VERIFICATION)

1. **呼叫 `create_green_material` 工具專用建立**:
   - 傳入 `materialName`: 100% 符合 `GBM編號_材料名稱` 格式 (如 `GBM0104106_水性漆(居室外用)`)。
2. **Agent 必須到 Project Materials 進行實體檢查**:
   - 材質建立後，Agent **必須主動呼叫 `get_all_materials(searchKeyword: "GBM")`**。
   - 實體查詢 Revit Project Materials (材質瀏覽器) 資料庫，並在回覆中附上 Material ID 與 Name 的驗證證據！

---

## 🛠️ 材質建立與構造層三步驟核心邏輯

在進行 Material 材質建立與層位套入時，必須嚴格執行以下 **三步驟金科玉律**：

1. **判斷牆板與塗料的材料種類**:
   - 區分 Set 中屬於「牆板/石膏板/矽酸鈣板」類別與「塗料/水性漆」類別之材料。
2. **分別呼叫 `create_green_material` 建立各自獨立的 Material**:
   - 塗料材質: **`GBM0104106_水性漆(居室外用)`**
   - 板材材質: **`GBM0103810_無機質NICHIAS NA LUX矽酸鈣板(0.8FK)`**
3. **呼叫 `get_all_materials` 主動驗收檢查**:
   - 驗證兩筆材質已實體存在於 Revit 材質瀏覽器中。
4. **精確放入構造層 (Compound Structure)**:
   - 板材 material 放入 **`Structure [1]`** (核心結構層)
   - 塗料 material 放入 **`Finish 1 [4]`** / **`Finish 2 [5]`** (飾面層)

---

## 🛠️ 執行步驟與標準規範

### 1. 獨立材質建立與主動檢查 (Material Creation & Verification)
- 呼叫 `create_green_material` 建立純淨 Material。
- 呼叫 `get_all_materials(searchKeyword: "GBM")` 到 Project Materials 檢查並取得實體清單。

### 2. Element Type Duplicate 與合法命名
- **基底選擇**: 優先以包含粉刷層之型號 (`RC 牆加粉刷 15cm(2+2cm)`) 為來源。
- **命名規範**: 必須遵循 **`TABC_<SetName>`** (例如 `TABC_test牆`)。禁用中括號 `[` `]`。
- **工具**: `duplicate_element_type`。

### 3. 綠建材專屬 Type 共享欄位 100% 實體寫入
- 寫入 `GreenMaterial_Mat1_*` (塗料) 與 `GreenMaterial_Mat2_*` (板材) 的履歷、廠商、CNS 規範與試驗數據。

### 4. Active View 實體對焦與高亮選取 (無重複繪製)
- 呼叫 `query_elements` 取得圖面上既有牆體 `255000` (型號 `TABC_test牆`)。
- 呼叫 `select_element` 與 `zoom_to_element` 自動全螢幕縮放大對角。

---

## 📌 驗收 CheckList

- [x] 1. 呼叫 `create_green_material` 專用工具建立獨立材質。
- [x] 2. Agent 建立材質後，主動呼叫 `get_all_materials` 檢查 Project Materials 並提供驗證證據。
- [x] 3. 優先使用既有牆體，嚴禁重複繪製牆體避免引發 Overlap 視窗鎖死。
- [x] 4. 正確判斷牆板與塗料的材料種類，100% 依 `GBM編號_材料名稱` 公式建立。
- [x] 5. 板材 material 實體放入 Structure；塗料 material 實體放入 Finish。
- [x] 6. Edit Type 視窗的 `Identity Data` 下帶出全量 `GreenMaterial_*` 屬性與試驗數據。
