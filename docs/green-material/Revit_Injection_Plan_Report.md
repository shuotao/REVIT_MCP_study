# 🤖 AI 建築 Agent：Revit 綠建材推送執行計畫書 (v3 專業版)

- **計畫編號 (Plan ID)**: `PLAN-20260812000945`
- **材料 Set 名稱**: `填縫劑`
- **擬訂時間**: `2026-08-12 00:09:45`
- **執行 Agent**: `antigravity (建築 Agent)`

---

## 1. 受影響 Revit 元件品類與對映架構
- **`OST_Materials`**

---

## 2. 材料與 Revit 19個共享參數對映清單

### [1] 五彩CG2W陶瓷面磚水泥質型填縫劑 (`GBM0104172`)
- **製造廠商**: 丸進貿易有限公司
- **標章分類**: 健康綠建材 (綜合建材類)
- **目標 Revit 品類**: `OST_Materials`
- **建議構造層**: `Attached Parameter (Construction)` ｜ **預設厚度**: `0 mm (非幾何屬性)`
- **BIM 建議命名**: `AUX_Adhesive_Sealant`
- **CNS 依據**: 依 CNS3090 試驗，符合規定。
- **合格項目**: 健康綠建材
- **試驗數據**: ① 28天抗壓強度：343kgf/cm2。② 56天氯離子滲透電量：942庫倫。
- **非幾何欄位**: `GreenMaterial_Sealant` ➔ 五彩CG2W陶瓷面磚水泥質型填縫劑 (GBM0104172)
- **GreenMaterial_Mat 槽位**: `MAT1`

---

## 3. 預備執行動作 SOP
1. 載入 GreenMaterial_SharedParams.txt (包含 64 個共享參數，Mat1~Mat6 六槽位) 至 Revit 專案
2. 掃描專案模型對應品類：OST_Materials
3. 依據 TASK-003 規範自動配置構造層位階 (Finish 1 / Substrate / Structure) 與預設厚度推判
4. 偵測到非幾何輔助材料 (填縫劑/接著劑)，自動寫入 Type 的 Construction 自訂欄位
5. 批量將 TABC 履歷與 CNS 試驗數據寫入 OST_Materials 與 Type Identity Data
6. 自動匯出綠建材明細表 (Schedule) 至 Excel 歸檔