# 🤖 AI 建築 Agent：Revit 綠建材推送執行計畫書 (v3 專業版)

- **計畫編號 (Plan ID)**: `PLAN-20260811021121`
- **材料 Set 名稱**: `地磚與填縫`
- **擬訂時間**: `2026-08-11 02:11:21`
- **執行 Agent**: `antigravity (建築 Agent)`

---

## 1. 受影響 Revit 元件品類與對映架構
- **`OST_Floors`**
- **`OST_Materials`**

---

## 1b. 材料層級順序（使用者於檢索平台明確指定，權威來源）

1. — Core Boundary 分界線（不填入實際材料）—
1. GBM0102995｜陶瓷面磚(II類-內裝地磚)（Structure）
1. — Core Boundary 分界線（不填入實際材料）—

---

## 2. 材料與 Revit 19個共享參數對映清單

### [1] 陶瓷面磚(II類-內裝地磚) (`GBM0102995`)
- **製造廠商**: 冠軍建材股份有限公司
- **標章分類**: 再生綠建材 (地板類)
- **目標 Revit 品類**: `OST_Floors`
- **建議構造層**: `Structure [1]` ｜ **預設厚度**: `150 mm（結構層，依材料層級設定指定，建議人工確認實際配比厚度）`
- **BIM 建議命名**: `F_INT_Structure`
- **CNS 依據**: 依 CNS3090 試驗，符合規定。
- **合格項目**: 再生綠建材
- **試驗數據**: ① 28天抗壓強度：343kgf/cm2。② 56天氯離子滲透電量：942庫倫。
- **層級來源**: 使用者於檢索平台材料層級設定明確指定（非關鍵字啟發式判斷）
- **GreenMaterial_Mat 槽位**: `MAT1`

### [2] CG2WA泰固美特耐磁磚填縫劑 (`GBM0104110`)
- **製造廠商**: 潤泰精密材料股份有限公司
- **標章分類**: 健康綠建材 (綜合建材類)
- **目標 Revit 品類**: `OST_Materials`
- **建議構造層**: `Attached Parameter (Construction)` ｜ **預設厚度**: `0 mm (非幾何屬性)`
- **BIM 建議命名**: `AUX_Adhesive_Sealant`
- **CNS 依據**: 依 CNS16082 試驗，符合規定。
- **合格項目**: 健康綠建材 (綜合建材類)
- **試驗數據**: ① TVOC逸散率：< 0.19 mg/m²·h。② 游離甲醛逸散率：< 0.05 mg/m²·h。③ 毒性化學物質：無。
- **層級來源**: 使用者於檢索平台材料層級設定明確指定（非關鍵字啟發式判斷）
- **非幾何欄位**: `GreenMaterial_Sealant` ➔ CG2WA泰固美特耐磁磚填縫劑 (GBM0104110)
- **GreenMaterial_Mat 槽位**: `MAT2`

---

## 3. 預備執行動作 SOP
1. 載入 GreenMaterial_SharedParams.txt (包含 64 個共享參數，Mat1~Mat6 六槽位) 至 Revit 專案
2. 掃描專案模型對應品類：OST_Floors, OST_Materials
3. 依據使用者於檢索平台明確指定的材料層級設定 (layerComposition) 配置構造層位階，不採用關鍵字啟發式推判
4. 偵測到非幾何輔助材料 (填縫劑/接著劑)，自動寫入 Type 的 Construction 自訂欄位
5. 批量將 TABC 履歷與 CNS 試驗數據寫入 OST_Materials 與 Type Identity Data
6. 自動匯出綠建材明細表 (Schedule) 至 Excel 歸檔