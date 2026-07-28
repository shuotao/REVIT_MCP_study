---
name: cad-block-point-placement
description: "CAD 圖塊（Block/INSERT）插入點批次放置 Revit 點位族群（FamilyInstance）的通用 SOP：適用灑水頭/閥件等重複設備圖塊。discover/preview/create 三工具拆分，preview 唯讀回傳可檢查的座標鏈（Block insertion point → Block transform → ImportInstance TotalTransform）+ ready/duplicate/unsupported_family 狀態；**transform 不可信時停止建立、不猜 correction**。與 dwg-column-import（矩形輪廓）、dwg-beam-import（雙線中心線）互補，非取代。觸發於 cad 圖塊放置、block 轉族群、灑水頭建模、閥件建模、point placement from CAD block、INSERT to FamilyInstance。TODO 待補：NicheSam 補齊實測細節與工具原始碼路徑。"
metadata:
  version: "0.1"
  updated: "2026-07-28"
  created: "2026-07-28"
  contributors:
    - "NicheSam (SC REVIT, 待確認真名)"
  references:
    - "Issue #100（作者 @NicheSam, SC REVIT）"
  related:
    - dwg-column-import.md
    - dwg-beam-import.md
    - tool-capability-boundary.md
  referenced_by: []
  tags: [DWG, DXF, CAD, ImportInstance, Block, INSERT, FamilyInstance, 點位放置, 灑水頭, 閥件, 座標鏈, transform, Revit]
---

# CAD 圖塊插入點放置 FamilyInstance SOP

把 CAD 圖面中重複出現的設備圖塊（Block/INSERT，例如灑水頭符號、閥件符號）批次轉成 Revit 點位式 `FamilyInstance`。來源 issue #100（作者 @NicheSam, SC REVIT）。對應工具 `discover`/`preview`/`create` 三段（TODO 待補：實際工具名稱、C# 對應實作路徑）。此流程是**通用 Block→FamilyInstance 點位放置**，與 `domain/dwg-column-import.md`（矩形輪廓翻模結構柱）、`domain/dwg-beam-import.md`（雙線中心線翻模結構樑）互補，不取代任一方。

> **核心原則（與 AI 協作）**：座標鏈（Block insertion point → Block transform → ImportInstance TotalTransform）**不可信時，一律回傳明確警告並停止建立，絕不由 AI 猜測或套用 correction**。這是本工具成立的分水嶺——寧可讓使用者手動核對重連 CAD，也不允許在座標鏈不可信的情況下批次落點。

---

## 0. 與現有 DWG 翻模工具的定位差異

| 面向 | `dwg-column-import` | `dwg-beam-import` | 本流程（cad-block-point-placement） |
|---|---|---|---|
| 幾何來源 | 矩形柱輪廓（PolyLine/Line 迴圈/block 皆可） | 平行雙線中心線 | **Block（INSERT）插入點 + 旋轉** |
| 產物 | 結構柱/建築柱 | 結構樑 | **點位式 FamilyInstance**（灑水頭、閥件等設備） |
| 型別對應 | 尺寸/柱號對應（模式 A/B/C） | 尺寸/名稱對應 | 單一 `familySymbolId`（第一版不含族群/型別自動選擇） |
| 適用族群限制 | 結構柱族（可 host） | 結構樑族 | **僅 non-hosted、level-based、point-placement**（如 `OneLevelBased`） |

三者共用 DWG/ImportInstance 前置條件與「強制斷點、不可一次建完」的協作文化，但解析對象與產出元件類型完全不同，因此以獨立 domain 文件記錄，並非合併對象。

---

## 1. 前置條件（缺一不可）

1. Revit 開在**平面視圖**（比照 dwg-column/dwg-beam 慣例）。
2. 目標 DWG **已匯入或連結**到該視圖，視圖內至少一個 `ImportInstance`。
3. 目標 **FamilySymbol 已載入**到專案，且**必須是 non-hosted、level-based、point-placement**（例如 `OneLevelBased`）。hosted / face-based / work-plane-based 族群**第一版不支援**（見 §5）。
4. 目標 **Level 已存在**（`levelId` 對應樓層）；不存在需先建（可比照 `create_level`，TODO 待補：本流程是否共用該工具）。
5. TODO 待補：CAD 需 Import 或 Link？是否比照 dwg-column 模式 C 那樣「讀文字才需 Link」，或本流程完全不讀文字（純幾何插入點）因此 Import/Link 皆可？

---

## 2. 工作流（強制斷點版）

| 步驟 | 工具 | 作用 | 斷點 |
|---|---|---|---|
| 1 掃描 | `discover`（TODO 待補實際工具名） | 掃描指定 DWG，列出可辨識 Block 名稱、插入點、旋轉 | — |
| 2 **座標鏈健檢**（唯讀） | `preview(familySymbolId, levelId, offset, 重複容差)` | 回傳每個插入點的**可檢查座標鏈**（Block insertion point、Block transform、ImportInstance TotalTransform）+ 狀態 `ready`/`duplicate`/`unsupported_family`；**transform 不可信時回傳明確警告並停止**（不猜 correction） | ⛔ **斷點 1**：使用者確認 Block 選擇、familySymbolId、levelId、offset，並核對座標鏈與狀態分佈（幾個 ready、幾個 duplicate、有無 unsupported_family／transform 警告） |
| 3 建立 | `create(...)` | 以**與 preview 完全相同參數**重新掃描驗證（不可信任 preview 快取結果）；主 `Transaction` + 逐筆 `SubTransaction`（單筆失敗不回滾其他） | ⛔ **斷點 2**：使用者對 preview 結果按「確認建立」後才呼叫；回傳 created 的每個 `ElementId`，逐一獨立查詢驗證存在 |

**鐵則**：
- `preview` 是唯讀操作，**不寫入模型**；只有 `create` 會寫入。
- `create` 不得信任 `preview` 的快取結果，必須以相同參數**重新掃描一次**再建立，避免兩次呼叫之間 CAD/專案狀態已變動而建出過期座標。
- **不依賴 Idling 事件**做非同步輪詢確認建立結果；`create` 回傳的 `ElementId` 要能被呼叫端**立即**、獨立查詢到（同步、確定性驗證），不得要求「等一下再查」。
- 掃描（discover）≠ 自動建立（create）：discover 只回報候選，任何寫入動作都要走過斷點 1、2。

### 2.1 流程圖（`/domain-diagram` 腳本產出）

TODO 待補：待工具實作與參數定案後，用 `.claude/skills/domain-diagram/scripts/mermaid_from_spec.py` 產出確定性流程圖（比照 `domain/dwg-column-import.md` §2.1 的格式），並附流程健檢結論（迴圈有界退出、無死路、abort 出口可達等）。

---

## 3. 座標鏈與 transform 信任邊界

本流程的座標鏈由三層組成，**preview 必須把三層都攤開給使用者核對**，而非只給最終結果：

1. **Block insertion point**：CAD 檔案內、Block 定義座標系下的插入點（原始 DWG/DXF 座標）。
2. **Block transform**：該 INSERT 實體相對於圖紙座標系的平移/旋轉/縮放（對應 CAD 內部的 block reference transform）。
3. **ImportInstance TotalTransform**：CAD 連結/匯入到 Revit 後，`ImportInstance` 疊加的整體變換（含連結時的 placement、unit、shared coordinates 等）。

最終落點 = Block insertion point 依序套用「Block transform」再套用「ImportInstance TotalTransform」後，落在 Revit 模型座標系的結果。

**transform 不可信的判定與處置（TODO 待補：確切的可信度判定條件，例如非正交/含異常縮放/行列式異常等）**：一旦判定不可信，`preview` 必須：
- 明確標示哪些插入點受影響、原因。
- **回傳警告並拒絕該批次建立**，不得由 AI 或工具自行套用猜測性的 correction（例如自動假設某個縮放係數、自動假設某個旋轉修正量）。
- 交回使用者，由使用者回到連結對話框核對單位/比例/座標系後重新連結、重新 discover/preview。

這是本流程與 `dwg-column-import` 斷點 1（單位健檢 `preflight.unitSanity`）同一等級的「寫入前攔截」設計，但適用對象是**逐點**的座標鏈而非整批的尺寸/單位統計，因此判定粒度更細（可能整批多數 ready、少數幾點 transform 不可信）。

---

## 4. 關鍵工程確認點

### 4.1 重複容差與 duplicate 判定
`preview` 依使用者提供的**重複容差**（TODO 待補：預設值、單位 mm 或 feet）判斷同一位置是否已存在對應的 FamilyInstance 或本次掃描內彼此重複的插入點，回傳狀態 `duplicate`。**duplicate 不自動略過或自動合併**——列在 preview 結果中交使用者裁決（比照 dwg-column「未來增強」精神，本版不做自動決策）。

### 4.2 offset 與 level 換算單位
`offset` 為相對於 `levelId` 對應樓層的垂直偏移。TODO 待補：`offset` 輸入單位（mm 或 feet）、是否比照 dwg-column 的 `modify_element_parameter` 陷阱（Revit 內部長度單位為 feet，呼叫端若直接傳 mm 數值會錯 304.8 倍）——**若比照，本工具與呼叫端都需明確標示單位，避免同一類單位陷阱重演**。

### 4.3 unsupported_family 判定條件
`familySymbolId` 對應的族群若不是 non-hosted / level-based / point-placement（例如是 hosted-on-face 或 work-plane-based），`preview` 應回傳狀態 `unsupported_family` 並**不得嘗試放置**。TODO 待補：判定依據（例如 `FamilySymbol.Family.FamilyPlacementType` 是否為 `OneLevelBased`／`ViewBased` 等對應到的 API 判斷式）。

### 4.4 SubTransaction 單筆失敗不回滾其他
`create` 用主 `Transaction` 包住整批，內部每個插入點各自開一個 `SubTransaction`：單筆放置失敗（例如該點 transform 邊界情況、族群放置例外）**只回滾該筆**，不影響同批其他已成功的 `SubTransaction`。回傳結果需列出每筆的成功/失敗與失敗原因，供使用者判斷是否需要針對失敗項目單獨重跑。

---

## 5. 第一版邊界

- **只支援** non-hosted、level-based、point-placement 的 FamilySymbol（如 `OneLevelBased`）。
- **不支援** hosted（face-based、work-plane-based 等）族群放置——第一版偵測到即回 `unsupported_family`，不做特殊處理或降級嘗試。
- **不自動選擇** `familySymbolId` 或 `levelId`——必須由使用者在斷點 1 明確指定，本工具不猜測「哪個族群/樓層最合適」。
- **不轉輪廓**——本流程只處理點狀 Block 插入點，不處理多線段/多邊形幾何（那是 dwg-column/dwg-beam 的範疇）。
- **不做人工校正（correction）**——transform 不可信時只停止、警告，不提供自動修正選項（第一版刻意不做，避免掩蓋座標問題）。

---

## 6. 已知限制與實機驗證

- TODO 待補：確認以下數字是否為 NicheSam 提供的實測結果，並補上測試日期、專案/圖面來源。
- 已實測：14 種 Block／693 個插入點掃描；放置 5 個 `OneLevelBased` 灑水頭族群實例。
- 驗證方式：`create` 回傳的每個 `ElementId` **逐一獨立查詢**（非批次假設全部成功），確認元素存在且參數正確。
- TODO 待補：是否有 duplicate／unsupported_family／transform 不可信案例的實測紀錄？若有，補上具體案例（比照 `dwg-column-import.md` §6 的案例格式）。

---

## 7. QA／驗收清單

- [ ] `discover` 有回傳 Block 名稱／插入點／旋轉清單
- [ ] `preview` 全部為 `ready`，或 `duplicate`／`unsupported_family` 已與使用者協作處置（非自動略過）
- [ ] 座標鏈（Block insertion point / Block transform / ImportInstance TotalTransform）已攤開供使用者核對
- [ ] 若有 transform 不可信警告：已停止建立、未套用任何猜測性 correction，已交回使用者處理
- [ ] **斷點 1**：familySymbolId、levelId、offset、重複容差已與使用者確認
- [ ] **斷點 2**：使用者已明確確認建立，`create` 呼叫參數與 preview 完全一致
- [ ] `create` 回傳的每個 ElementId 已逐一獨立查詢驗證存在
- [ ] 未使用 Idling 事件做非同步輪詢確認結果（同步、確定性驗證）

---

## 參考 / Reference

- 相關 domain：`domain/dwg-column-import.md`（矩形輪廓翻模結構柱，互補而非取代）、`domain/dwg-beam-import.md`（雙線中心線翻模結構樑，互補而非取代）、`domain/tool-capability-boundary.md`（工具能力邊界原則）
- 相關既有工具（非本流程專屬，但同屬 CAD/ImportInstance 情境，供對照）：`link_cad_to_view`、`link_cads_by_floor`（`MCP-Server/src/tools/cad-link-tools.ts`）——負責把 DWG/DXF 連結到視圖，是本流程 §1 前置條件「CAD 已匯入或連結」的上游步驟，但**不做**本文件描述的 Block 插入點掃描／放置。
- TODO 待補：本流程 discover/preview/create 對應的實際工具檔名（預期新增於 `MCP-Server/src/tools/`）與 C# 端 executor 路徑，待 @NicheSam 實作 PR 提供後補齊本節與 frontmatter 的 `referenced_by`。
