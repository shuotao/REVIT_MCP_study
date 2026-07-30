# 建築 AGENT 規範：Revit 綠建材推送執行計畫 (Revit Injection Plan Specification)

## 1. 流程背景與架構 (Workflow Context)

根據 **TASK-004** 之規範，當使用者在地端檢索網頁中勾選材料或打包「專案材料組合 (Material Set)」並點擊推送至 Revit 時，本系統不會直接對模型進行盲目寫入，而是觸發 **AI 建築 Agent 需求對齊機制**：

1. **Web 端傳遞 Payload**：地端網頁輸出打包好的材料 ID、名稱與 TABC 履歷數據。
2. **AI Agent 需求詢問**：Agent 主動向使用者詢問具體施工與 BIM 檢討範疇（如：牆面綠建材面積檢討 $45\%/75\%$、樓板衝擊音 $\Delta L_w$ dB 評定、天花板降噪 NRC 評定等）。
3. **依 TASK-003 擬訂計畫**：Agent 結合 **TASK-003 元件品類對映法則** 與 **16 個共享參數結構 (`GreenMaterial_SharedParams.txt`)**，生成標準化 **「推送 Revit 執行計畫 (Revit Injection Plan)」**。
4. **使用者簽核確認 (User Sign-off)**：計畫經使用者核可後，方啟動後續 pyRevit 自動寫入指令與算量工具。

---

## 2. 推送計畫結構範本 (Revit Injection Plan JSON Schema)

```json
{
  "planId": "PLAN-20260730-001",
  "setName": "A棟標準客房裝修Set",
  "createdAt": "2026-07-30T16:25:00Z",
  "targetRevitCategories": [
    "OST_Walls",
    "OST_Floors",
    "OST_Ceilings"
  ],
  "intentScope": "健康綠建材率 45% 評定 + 樓板衝擊音 ΔLw 20dB 檢討",
  "materialsMapping": [
    {
      "licno": "GBM0104204",
      "title": "建築用薄塗紋理裝飾塗材(室內用薄塗材Si)",
      "targetRevitCategory": "OST_Walls",
      "targetFinishLayer": "Finish 1 [4]",
      "sharedParametersToInject": {
        "GreenMaterial_CertNo": "GBM0104204",
        "GreenMaterial_Category": "健康綠建材",
        "GreenMaterial_SubCategory": "塗料類",
        "GreenMaterial_Applicant": "中國製釉股份有限公司",
        "GreenMaterial_ValidUntil": "115/07/09 ~ 119/07/08",
        "GreenMaterial_CNSSpec": "依 CNS16082 / CNS15200 試驗，符合規定。",
        "GreenMaterial_QualifiedItems": "健康綠建材 (低揮發性有機物)",
        "GreenMaterial_TestItems": "① TVOC逸散率：0.08 mg/m²·h。② 游離甲醛逸散率：0.01 mg/m²·h。③ 4大重金屬：未檢出。",
        "GreenMaterial_TVOC": 0.08,
        "GreenMaterial_Formaldehyde": 0.01
      }
    },
    {
      "licno": "GBM0104194",
      "title": "複合木質地板",
      "targetRevitCategory": "OST_Floors",
      "targetFinishLayer": "Finish 1 [4]",
      "sharedParametersToInject": {
        "GreenMaterial_CertNo": "GBM0104194",
        "GreenMaterial_Category": "健康綠建材",
        "GreenMaterial_SubCategory": "地板類",
        "GreenMaterial_Applicant": "昇揚地板企業有限公司",
        "GreenMaterial_ValidUntil": "115/07/08 ~ 119/07/07",
        "GreenMaterial_CNSSpec": "依 CNS1349 / CNS16083 試驗，符合規定。",
        "GreenMaterial_QualifiedItems": "健康綠建材 (地板類)",
        "GreenMaterial_TestItems": "① 游離甲醛釋出量：0.02 mg/m²·h (F1等級)。② TVOC逸散率：0.05 mg/m²·h。",
        "GreenMaterial_TVOC": 0.05,
        "GreenMaterial_Formaldehyde": 0.02
      }
    }
  ],
  "verificationPlan": {
    "scheduleTablesToGenerate": [
      "綠建材標章材料明細表",
      "牆面綠建材面積算量表",
      "地坪綠建材面積算量表"
    ],
    "targetGreenRatioThreshold": "45%"
  }
}
```

---

## 3. 擬訂計畫之 4 大對齊原則 (4 Alignment Pillars)

1. **品類歸屬對齊 (Category Matching)**：
   - 塗料/牆板類 ➔ 指派至 `OST_Walls` / 牆面材料。
   - 地磚/木地板/防音墊 ➔ 指派至 `OST_Floors` / 地坪材料。
   - 吸音板/岩棉天花板 ➔ 指派至 `OST_Ceilings` / 天花材料。

2. **上下層參數對齊 (Param Layering)**：
   - 第一層：自動寫入 8 個標章履歷與 CNS 標準常數（`CertNo`, `CNSSpec`, `TestItems`...）。
   - 第二層：自動連結動態幾何算量屬性（`DecorArea`, `QualifyArea`, `RatioContribution`）。

3. **審查門檻對齊 (Threshold Alignment)**：
   - 一般供公眾使用建築物：綠建材率達裝修面積 **$45\%$** 以上。
   - 綠建築標章 / 鑽石級：綠建材率達 **$75\%$** 以上。

4. **圖冊明細表對齊 (Schedule Alignment)**：
   - 推送計畫需自動列出注入後需生成的 Revit 門窗/牆面明細表（Schedule）格式與名稱。
