# Revit 綠建材推送執行計畫規範 (Revit Injection Plan Specification)

> **2026-08-12 全面改版說明**：本文件舊版描述的是 pyRevit 執行、16 個共享參數、`intentScope`/`verificationPlan`（含 45%/75% 綠建材率門檻、`scheduleTablesToGenerate` 自動明細表匯出）的早期設計，從未被實際實作。本次改版改為記錄 `GM_generate_revit_injection_plan.py`（實際跑在生產路徑上的擬訂引擎）真正產出的計畫結構與流程，並如實標註哪些早期構想仍未落地。

---

## 1. 流程背景與架構 (Workflow Context)

使用者在 `assets/green-material-showcase.html` 檢索網頁勾選材料、打包成材料 Set，並在「對齊需求與擬訂計畫」彈窗回答 Q1（組合方式）/Q2（品類，及純材料情境的掛載品類）/Q3（補充條件自由文字）後，網頁會產生一段 `/GM_import` 指令文字供使用者貼給 AI Agent。實際流程分兩個獨立、可各自重跑的階段：

1. **`/GM_import`**（`.claude/skills/GM_import/SKILL.md`）：只做規劃，**不寫入 Revit**。解析 `/GM_import` 文字 → 呼叫 `GM_generate_revit_injection_plan.py` 的 `generate_injection_plan()` 比對 `tabc_master_database.json` → 寫出 `Revit_Injection_Plan.json` 與 `docs/green-material/Revit_Injection_Plan_Report.md` → 呼叫 `write_back_to_set_manager()` 把 `planStatus`/`planId`/`plannedActions` 回寫進 `exported_material_sets.json`。純材料（`品類: Material`）情境會額外做一次**唯讀** `get_types_by_category` 查詢，讓使用者從候選 Type 清單挑選要附掛的既有 Type，選擇結果存入該 Set 的 `pureMaterialTarget` 欄位。
2. **`/GM_inject revit`**（`.claude/skills/GM_inject/SKILL.md`）：讀取上一步的計畫，呼叫真正的 Revit MCP 工具（`duplicate_element_type` / `create_single_material_type` / `create_multi_layer_type` / `inject_green_material_into_family` 等）寫入模型，每個情境都要求使用者在實際下筆前明確確認一次。

沒有 pyRevit 執行層——所有 Revit 端寫入都經過 `MCP-Server/src/tools/*.ts` → `MCP/Core/Commands/*.cs` 的既有 MCP 工具鏈，符合 `CLAUDE.md`「不繞過 MCP」的規則。

---

## 2. 推送計畫結構 (Revit Injection Plan JSON Schema)

以下欄位取自 `GM_generate_revit_injection_plan.py::generate_injection_plan()` 實際組出的 `plan` dict（欄位名稱與巢狀結構為實測結果，非示意）：

```json
{
  "planId": "PLAN-20260812012155",
  "setName": "玻璃棉吸音板覆蓋測試",
  "generatedAt": "2026-08-12 01:21:55",
  "agentName": "antigravity (建築 Agent)",
  "userIntent": "（原始 /GM_import 文字，含 [需求對齊：...] 區塊）",
  "targetRevitCategories": ["OST_Ceilings"],
  "totalMaterialsCount": 1,
  "materialsMapping": [
    {
      "licno": "GBM0103862",
      "title": "Ecophon玻璃棉吸音板",
      "company": "弘駿實業股份有限公司",
      "category": "健康",
      "subCategory": "天花板類",
      "targetRevitCategory": "OST_Ceilings",
      "targetLayer": "Finish 1 [4]",
      "defaultThickness": "12 mm (飾面板)",
      "buiNaming": "C_INT_Ceiling",
      "mappingDetails": { "revitCategory": "OST_Ceilings", "layer": "Finish 1 [4]", "...": "..." },
      "sharedParameters": { "GreenMaterial_CertNo": "GBM0103862", "...": "..." },
      "assignedSlot": "mat1"
    }
  ],
  "executionSteps": ["1. 載入 GreenMaterial_SharedParams.txt ...", "2. 掃描專案模型對應品類：OST_Ceilings", "..."],
  "layerComposition": null,
  "layerCompositionSequenceLabels": null,
  "materialSlotAssignment": { "assignment": { "mat1": { "licno": "GBM0103862", "title": "...", "roleBucket": "Finish" } }, "unassigned": [] },
  "pureMaterialAttachCategory": null,
  "wallUsageHint": null
}
```

`buiNaming`（如 `W_INT_RC15`、`C_INT_Ceiling`）只是計畫報告書「BIM 建議命名」欄位的參考標籤，**不是**實際寫入 Revit 的 Type 名稱——真正的命名規則見 `revit_injection_logic_and_naming_spec.md` 第 2 節。

### 2.1 已移除的早期構想欄位（不需要，2026-08-12 定案）

早期版本的 `sharedParameters` 曾含 `GreenMaterial_DecorArea` / `GreenMaterial_QualifyArea` / `GreenMaterial_RatioContribution` 三個欄位（永遠固定 `0.0` 的佔位符），對應「依裝修面積自動算綠建材率、匯出 Excel 明細表」的構想；`executionSteps` 也曾固定附加一句「自動匯出綠建材明細表至 Excel 歸檔」。這個功能方向已確認不需要，兩處都已從 `GM_generate_revit_injection_plan.py` 移除——不是「尚未實作」，而是刻意不做。

---

## 3. 擬訂計畫的核心對齊邏輯 (Alignment Logic)

以下取代舊版「4 大對齊原則」，改為 `generate_injection_plan()` 實際採用、依優先序疊加的判斷鏈：

1. **`layerComposition` 明確覆寫（最高優先）**：使用者在 showcase 頁面材料層級設定視窗，為 Wall/Floor 單一組合 Set 手動指定每項材料的 `Structure`/`Substrate`/`Finish` 角色與 Core Boundary 位置時，這個設定存在 Set 的 `layerComposition.sequence`，一律覆寫下方的關鍵字啟發式判斷。輔助材料（接著劑/填縫劑/防水膜）若被拖曳到「🧴 輔助材料」區，記錄在 `layerComposition.auxiliary`，同樣優先於關鍵字判斷。
2. **`subCategory` 優先的關鍵字啟發式判斷**（`analyze_material_mapping()`）：Master DB 的 7 種 `subCategory`（天花板類/地板類/隔音緩衝類/塗料類/牆壁類/透水鋪面類/綜合建材類）決定目標品類；只有 `subCategory` 是「綜合建材類」這種 catch-all 時，才進一步用 `title` 關鍵字判斷（輔助材料關鍵字、門窗玻璃關鍵字、板材關鍵字），判斷不出來就誠實回報 `needsManualReview: true`，不強行猜測。
3. **Set 品類覆寫（`needsManualReview` 的退路）**：材料本身跨用途（如混凝土可能用於 Wall/Floor/Column/Beam）而判斷不出角色時，改用 Set 自己宣告的「品類」（`/GM_import` 文字裡的 `品類: Floor` 等）決定，並標記 `resolvedBySetCategoryOverride: true` 供人工複核。
4. **牆體用途厚度矩陣**（TASK-005.6）：Structure 層的牆體材料，厚度依 Q3 補充條件文字中偵測到的用途關鍵字決定——外牆 150mm、分戶牆 135mm、輕隔間 100mm；偵測不到就用保守預設 150mm，並標記 `wallUsageUnspecified: true`，`/GM_inject revit` 必須在確認步驟明確提示使用者覆寫，不得悄悄當作可信數值。
5. **Mat1~Mat6 六槽位分配**（`_assign_material_slots()`，確定性規則，非 AI 臨場判斷）：優先序固定 `Structure > Finish > Substrate > Other`（輔助材料歸 `Other`），同優先序內依材料在已排序清單中的原始順序；材料數 ≤ 6 全部分到槽位，超過 6 個才會有材料落入 `unassigned`（仍會建立實體構造層與獨立 Material，只是沒有 `GreenMaterial_Mat*` 共享參數紀錄）。完整規則見 `domain/GM_parameter-schema.md`「六槽位分配規則」。

---

## 4. `/GM_set compare`：既有 Set 與最新資料庫比對

`compare_and_refresh_set()` / `compare_all_sets()` 比對 `exported_material_sets.json` 裡每個 Set 的 `items`（licno 清單）與目前 `tabc_master_database.json`：

- **missing**：licno 在目前資料庫已找不到（含 `(續)`/`(增)` 後綴的正規化比對）。
- **expired**：憑證有效期限（民國年格式 `115/07/09 ~ 119/07/08`）已過今日。
- **changed**：材料名稱與上次擬訂計畫時、從 `purpose` 摘要文字還原出的快照不同。

有差異時，`/GM_set compare` 會重新執行 `generate_injection_plan()` + `write_back_to_set_manager()` 刷新計畫檔與 Set 狀態，但**不會寫入 Revit**——實際寫入仍需使用者另外執行 `/GM_inject revit`。

---

## 5. 相關文件

- 命名規則與共享參數完整表：`revit_injection_logic_and_naming_spec.md`
- 共享參數 Schema 權威定義：`domain/GM_parameter-schema.md`
- 各情境 Revit 寫入 SOP：`.claude/skills/GM_inject/SKILL.md`、`.agents/skills/combined-wall-set-import/SKILL.md`
- 最近一次生成的計畫報告快照（自動產生，勿手動編輯）：`Revit_Injection_Plan_Report.md`
