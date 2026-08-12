# 🤖 AI 建築 Agent：Revit 綠建材推送執行計畫書 (v3 專業版)

- **計畫編號 (Plan ID)**: `PLAN-20260812152631`
- **材料 Set 名稱**: `混凝土_樑`
- **擬訂時間**: `2026-08-12 15:26:31`
- **執行 Agent**: `antigravity (建築 Agent)`

---

## 1. 受影響 Revit 元件品類與對映架構
- **`OST_StructuralFraming`**

---

## 2. 材料與 Revit 19個共享參數對映清單

### [1] 綠混凝土G類 (`GBM0104181`)
- **製造廠商**: 慶龍預拌混凝土股份有限公司
- **標章分類**: 再生綠建材 (綜合建材類)
- **目標 Revit 品類**: `OST_StructuralFraming`
- **建議構造層**: `Structural Material Parameter (單一材質參數，非構造層)` ｜ **預設厚度**: `N/A (依梁斷面尺寸)`
- **BIM 建議命名**: `BEAM_Structural`
- **CNS 依據**: 依 CNS3090 試驗，符合規定。
- **合格項目**: 再生綠建材
- **試驗數據**: ① 28天抗壓強度：343kgf/cm2。② 56天氯離子滲透電量：942庫倫。
- **層級來源**: 材料本身 subCategory='綜合建材類' 屬跨用途通用建材，無法單獨判斷；依 Set 宣告品類 'Beam' 解析，非材料自身資料判斷結果
- **GreenMaterial_Mat 槽位**: `MAT1`

---

## 3. 預備執行動作 SOP
1. 載入 GreenMaterial_SharedParams.txt (包含 64 個共享參數，Mat1~Mat6 六槽位) 至 Revit 專案
2. 掃描專案模型對應品類：OST_StructuralFraming
3. 依據 TASK-003 規範自動配置構造層位階 (Finish 1 / Substrate / Structure) 與預設厚度推判
4. 批量將 TABC 履歷與 CNS 試驗數據寫入 OST_Materials 與 Type Identity Data