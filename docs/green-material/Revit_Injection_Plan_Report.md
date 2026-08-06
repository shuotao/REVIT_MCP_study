# 🤖 AI 建築 Agent：Revit 綠建材推送執行計畫書 (v3 專業版)

- **計畫編號 (Plan ID)**: `PLAN-20260804235120`
- **材料 Set 名稱**: `走道地坪`
- **擬訂時間**: `2026-08-04 23:51:20`
- **執行 Agent**: `antigravity (建築 Agent)`

---

## 1. 受影響 Revit 元件品類與對映架構
- **`OST_Floors`**
- **`OST_Materials`**

---

## 2. 材料與 Revit 19個共享參數對映清單

### [1] 室內地板舖設用聚胺酯 (`GBM0103182(續)`)
- **製造廠商**: 慶泰樹脂化學股份有限公司
- **標章分類**: 健康綠建材 (地板類)
- **目標 Revit 品類**: `OST_Floors`
- **建議構造層**: `Finish 1 [4]` ｜ **預設厚度**: `15 mm (飾面地磚) + 20mm 打底`
- **BIM 建議命名**: `F_INT_FloorTile`
- **CNS 依據**: 依 CNS1349 / CNS16083 試驗，符合規定。
- **合格項目**: 健康綠建材 (地板類)
- **試驗數據**: ① 游離甲醛釋出量：0.02 mg/m²·h (F1等級)。② TVOC逸散率：0.05 mg/m²·h。③ 吸水厚度膨脹率：<0.5%。

### [2] eFoamlay POD60多功能地板隔音緩衝材(6mm)樓板隔音系統 (`GBM0103751(續)`)
- **製造廠商**: 泉碩科技股份有限公司
- **標章分類**: 高性能綠建材 (地板類)
- **目標 Revit 品類**: `OST_Floors`
- **建議構造層**: `Substrate [2]` ｜ **預設厚度**: `10 mm (防音墊)`
- **BIM 建議命名**: `F_INT_FloorTile`
- **CNS 依據**: 依 CNS1349 / CNS16083 試驗，符合規定。
- **合格項目**: 健康綠建材 (地板類)
- **試驗數據**: ① 游離甲醛釋出量：0.02 mg/m²·h (F1等級)。② TVOC逸散率：0.05 mg/m²·h。③ 吸水厚度膨脹率：<0.5%。

### [3] 綠混凝土G類 (`GBM0104181`)
- **製造廠商**: 慶龍預拌混凝土股份有限公司
- **標章分類**: 再生綠建材 (綜合建材類)
- **目標 Revit 品類**: `OST_Materials`
- **建議構造層**: `Unclassified - Manual Review Required` ｜ **預設厚度**: `N/A`
- **BIM 建議命名**: `UNCLASSIFIED`
- **CNS 依據**: 依 CNS3090 試驗，符合規定。
- **合格項目**: 再生綠建材
- **試驗數據**: ① 28天抗壓強度：343kgf/cm2。② 56天氯離子滲透電量：942庫倫。

---

## 3. 預備執行動作 SOP
1. 載入 GreenMaterial_SharedParams.txt (包含 19 個共享參數) 至 Revit 專案
2. 掃描專案模型對應品類：OST_Floors, OST_Materials
3. 依據 TASK-003 規範自動配置構造層位階 (Finish 1 / Substrate / Structure) 與預設厚度推判
4. 批量將 TABC 履歷與 CNS 試驗數據寫入 OST_Materials 與 Type Identity Data
5. 自動匯出綠建材明細表 (Schedule) 至 Excel 歸檔