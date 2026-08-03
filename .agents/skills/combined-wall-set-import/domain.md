# Revit 綠建材 Material 獨立命名與生成 Domain 規範 (Material Creation Domain Rules)

本文件定義 Revit 綠建材專案中，Material (材質庫) 元件的獨立建立、命名公式與 Compound Structure 構造層落位邏輯。

---

## 📌 1. Material 命名標準公式 (Exact Naming Formula)

所有導入 Revit 材質庫 (`OST_Materials`) 的綠建材獨立材質，必須 100% 嚴格遵循以下語法公式：

$$\text{MaterialName} = \text{GBM標章編號} + \text{"\_"} + \text{TABC材料完整名稱}$$

### 🟢 正確範例 (Strictly Compliant Examples)
- **塗料材質**: **`GBM0104106_水性漆(居室外用)`**
- **板材材質**: **`GBM0103810_無機質NICHIAS NA LUX矽酸鈣板(0.8FK)`**

### 🔴 嚴格禁止事項 (Strict Prohibitions)
1. **禁止自己加英文後綴**: 嚴禁在名稱後方自作主張加上 `Finish` 或 `Structure`（例如禁止 `GBM0104106_水性漆Finish`）。
2. **禁止帶有預設牆體前綴**: 嚴禁出現 `預設牆_GBM...`。
3. **禁止組合串接多個材料標章**: 嚴禁將板材與塗料名稱串在一起。

---

## 🧱 2. 構造層 (Compound Structure) 業務對應邏輯

在組合牆 (Combined Wall) 元件中，依據材料種類精確落位至對應構造層：

| 材料種類 (Category) | 構造層位 (Function) | 實體指派材質名稱範例 | 說明 |
| :--- | :--- | :--- | :--- |
| **牆板 / 矽酸鈣板 / 石膏板** | **`Structure [1]`** (核心結構層) | `GBM0103810_無機質NICHIAS NA LUX矽酸鈣板(0.8FK)` | 放置於 Core Boundary 核心內部 ($150\,\text{mm}$) |
| **塗料 / 水性漆 / 飾面漆** | **`Finish 1 [4]` / `Finish 2 [5]`** (飾面層) | `GBM0104106_水性漆(居室外用)` | 放置於 Core Boundary 最外層/最內層 ($20\,\text{mm}$) |

---

## 🛠️ 3. 系統實作與防呆規則 (System Invariants)

1. **獨立性 (Uniqueness)**: 每筆綠建材標章在專案材質庫中為單一獨立 Element。
2. **無 `<By Category>` 殘留**: 所有構造層在建立時，必須實體指派 Material ID，絕不得殘留 `<By Category>`。
