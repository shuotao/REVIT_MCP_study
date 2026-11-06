---
name: cad-block-point-placement
description: "CAD 圖塊（Block/INSERT）插入點批次放置 Revit 點位族群（FamilyInstance）的通用 SOP：適用灑水頭/閥件等重複設備圖塊。discover(get_dwg_block_instances)/preview(preview_family_instances_from_dwg_blocks)/create(create_family_instances_from_dwg_blocks) 三工具拆分，preview 唯讀回傳可檢查的座標鏈（Block insertion point → Block transform → ImportInstance TotalTransform → Revit model point）+ ready/duplicate/unsupported_family/review_required 狀態；**transform 不可信時停止建立、不猜 correction**。v1 僅支援已連結（Linked）DWG，Imported DWG 留待後續。與 dwg-column-import（矩形輪廓）、dwg-beam-import（雙線中心線）互補，非取代。觸發於 cad 圖塊放置、block 轉族群、灑水頭建模、閥件建模、point placement from CAD block、INSERT to FamilyInstance。"
metadata:
  version: "0.3"
  updated: "2026-08-28"
  created: "2026-07-28"
  contributors:
    - "NicheSam (SC REVIT, 待確認真名) —— issue #100 規格與實測資料提供者"
    - "@Rumi-3653 —— issue #113／PR #115 三工具實作者（PR #115 為 #114 reopen：原帳號被 GitHub 標記，同一貢獻者換新帳號重新提交，非內容問題）"
  references:
    - "Issue #100（作者 @NicheSam, SC REVIT）"
    - "Issue #100 留言 2026-08-05（id 5190268672）：四個 TODO 補值 + v1 政策裁決依據 + Revit 2024 A$C87ebd845 實測（484 筆命中／20 筆試放與獨立回查）"
    - "Issue #113（三工具實作追蹤）／PR #115（@Rumi-3653，實作 PR，reopen of #114）"
  related:
    - dwg-column-import.md
    - dwg-beam-import.md
    - tool-capability-boundary.md
  referenced_by: []
  tags: [DWG, DXF, CAD, ImportInstance, Block, INSERT, FamilyInstance, 點位放置, 灑水頭, 閥件, 座標鏈, transform, Revit]
---

# CAD 圖塊插入點放置 FamilyInstance SOP

> ## ⚠️ 實作狀態（2026-08-28）：已合併、尚未在真實 Revit 環境驗證
>
> 三工具已由 @Rumi-3653 依本規格於 issue #113／PR #115 實作合併（`MCP/Core/CadBlockPlacementExecutor.cs` + `MCP-Server/src/tools/cad-block-placement-tools.ts` + `MCP/Core/CommandExecutor.cs` 三個 dispatcher case）。**這台環境沒有連上 Revit（`localhost:8964` 未監聽），貢獻者本人也沒有對真實 Revit 執行過這三支工具**——目前只驗證過原始碼層面：C# 端 R22–R26 五組建置皆 0 error、`registerRevitTools()` 註冊無重複、工具標註（title / readOnlyHint / destructiveHint）符合規範。以下四項規格陳述**尚未被任何真實 Revit session 驗證過**，其中第 1 項是 issue #113 明列的**必要驗收項**：
>
> 1. **主 `Transaction` + 逐筆 `SubTransaction` 的部分失敗隔離**（§4.4／§7 已標記）——規格來源（issue #100 2026-08-05 留言）的實測通道並未套用 `SubTransaction`，20 筆試放是一次性全部建立、獨立回查 20/20 通過，因此「單筆失敗不回滾其他成功項目」這個行為從未被真實測試過，只是程式碼裡寫了 `SubTransaction` 迴圈。
> 2. **`TryGetBlockDisplayName` 的 blockName 來源可靠性**——目前以 `GeometryInstance.GraphicsStyleId` 對應的 `GraphicsStyleCategory` 名稱 best-effort 取得，取不到時 fallback 為合成序號標籤 `Block#N`；這個來源是否足以對應真實 DWG 的 Block 種類（例如 issue #100 提到的 `A$C87ebd845` 這類 AutoCAD 自動命名），需要對真實已連結 DWG 實測才能確認。
> 3. **`OneLevelBased` 的 offset 記帳**——`NewFamilyInstance` 目前以 `level.Elevation + offsetMm 換算後的 feet` 作為 Z 座標傳入，Revit 自身對 level-based instance 的 offset 記帳方式（是否等於這個 Z 值、還是另外疊加 instance 的 Offset 參數）需要真機放置後 read-back 驗證，尚未驗證過。
> 4. **`unsupported_family` / `untrustworthy_transform` / duplicate 三種負向路徑**——issue #100 的兩輪實測都只涵蓋全新、`OneLevelBased`、transform 可信的插入點；非 `OneLevelBased` 族群、非等比例縮放或已失連的 Linked DWG、以及 `duplicate_existing`／`duplicate_in_batch` 的實際觸發，目前都沒有真實案例紀錄，行為只存在於程式碼與規格文字裡。
>
> 在這四項對真實 Revit + 已連結 DWG 驗證完成前，**不要在回答、log 或任何文件中宣稱這三支工具「已測試」「已驗證」或「已通過 Revit 驗收」**。使用前請先在測試模型（不存檔）上，依 §2 的兩道斷點小批次試跑，並實際核對 §7 QA 清單逐項打勾。

把 CAD 圖面中重複出現的設備圖塊（Block/INSERT，例如灑水頭符號、閥件符號）批次轉成 Revit 點位式 `FamilyInstance`。來源 issue #100（作者 @NicheSam, SC REVIT）。對應工具 `discover`/`preview`/`create` 三段，工具名稱已定案：

- `get_dwg_block_instances`（discover，唯讀盤點）
- `preview_family_instances_from_dwg_blocks`（preview，唯讀）
- `create_family_instances_from_dwg_blocks`（create，寫入）

C# 端實作位於 `MCP/Core/CadBlockPlacementExecutor.cs`，對應 `MCP-Server/src/tools/cad-block-placement-tools.ts` 的工具定義與 `MCP/Core/CommandExecutor.cs` 的三個 dispatcher case，由 @Rumi-3653 依本規格於 issue #113／PR #115 提交（PR #115 是 #114 的 reopen——原帳號被 GitHub 標記，同一貢獻者換新帳號重新提交，非內容問題）。**已驗證**：R22–R26 五組建置皆 0 error、`registerRevitTools()` 註冊無重複、工具標註符合規範。**尚未驗證**：見文件開頭狀態橫幅列出的四項——正式對真實 Revit + 已連結 DWG 執行前仍視同未驗證。此流程是**通用 Block→FamilyInstance 點位放置**，與 `domain/dwg-column-import.md`（矩形輪廓翻模結構柱）、`domain/dwg-beam-import.md`（雙線中心線翻模結構樑）互補，不取代任一方。

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
2. 目標 DWG **必須是已連結（Linked）**，視圖內對應一個 `ImportInstance`（`IsLinked = true`）。**v1 不支援 Imported DWG**（`IsLinked = false`）——見本項第 5 點理由。
3. 目標 **FamilySymbol 已載入**到專案，且**必須是 non-hosted、level-based、point-placement**（例如 `OneLevelBased`）。hosted / face-based / work-plane-based 族群**第一版不支援**（見 §5）。
4. 目標 **Level 已存在**（`levelId` 對應樓層）；不存在需先建。TODO 待補：本流程是否共用 `create_level` 工具自動建 Level，或維持「Level 必須預先存在，本流程不建 Level」——issue #100 留言未涵蓋此點，待 @NicheSam 或維護者後續補充。
5. **v1 政策裁決（維護者確認，2026-08-10）：僅支援已連結（Linked）DWG，Imported DWG 留待後續版本。** 理由：
   - 2026-08-05 實測中已出現 Linked DWG 記錄路徑失效的案例——模型內第一個 Linked DWG `1F-動力.dwg` 的 Revit 記錄路徑已失效，實測狀態為 `NotFound`，證明「已連結」本身仍需前置健檢，不能假設連結必然有效。
   - Imported DWG 的 `TotalTransform` 語意與 Linked DWG 不同（Imported 幾何直接內嵌於當前文件，沒有獨立的外部參照/重新連結機制），一旦來源檔案變動或遺失，`TotalTransform` 的可追溯性與失連風險比 Linked 更高，與本流程「座標鏈必須可查驗」的核心原則衝突。
   - v1 排除 Imported DWG 是正確的範圍收斂，避免在座標鏈可信度判定（§3）尚未涵蓋 Imported 情境時貿然放行。

---

## 2. 工作流（強制斷點版）

| 步驟 | 工具 | 作用 | 斷點 |
|---|---|---|---|
| 1 掃描 | `get_dwg_block_instances(importInstanceId, blockNames?, maxResults?)` | 唯讀盤點指定 Linked DWG，列出 Block 名稱、project coordinates、rotation degrees；**同時回傳供人辨識的 CAD 名稱與供工具精確解析的 Revit identity**（`candidateKey`，對齊 Revit API 完整名稱格式，例如 `B1F消防撒水.dwg.A$C87ebd845`）——後續 preview/create 一律沿用 identity，**不由 Agent 自行拼接名稱** | — |
| 2 **座標鏈健檢**（唯讀） | `preview_family_instances_from_dwg_blocks(importInstanceId, blockNames, familySymbolId, levelId, offsetMm, duplicateToleranceMm)` | 回傳每個插入點的**可檢查座標鏈**（Block insertion point → Block Transform → ImportInstance TotalTransform → Revit model point）+ 狀態 `ready`/`duplicate_existing`/`duplicate_in_batch`/`unsupported_family`/`review_required`；**transform 不可信時回傳明確警告並停止**（不猜 correction）；**不建立任何 Revit 元素**（不建十字線、不建預覽群組、不啟動 Transaction） | ⛔ **斷點 1**：使用者確認 Block 選擇、familySymbolId、levelId、offsetMm，並核對座標鏈與狀態分佈（幾個 ready、幾個 duplicate、有無 unsupported_family／review_required 警告） |
| 3 建立 | `create_family_instances_from_dwg_blocks(...)`（與 preview 相同參數） | 以**與 preview 完全相同參數**重新掃描來源驗證（不可信任 preview 快取結果）；通過後才用主 `Transaction` + 逐筆 `SubTransaction` 建立（單筆失敗不回滾其他成功項目）；回傳 `createdElementIds`、`skippedDuplicates`、`failedItems` | ⛔ **斷點 2**：使用者對 preview 結果按「確認建立」後才呼叫；回傳 created 的每個 `ElementId`，逐一獨立查詢驗證存在 |

**鐵則**：
- `preview` 是唯讀操作，**不寫入模型**；只有 `create` 會寫入。
- `create` 不得信任 `preview` 的快取結果，必須以相同參數**重新掃描一次**再建立，避免兩次呼叫之間 CAD/專案狀態已變動而建出過期座標。
- **不依賴 Idling 事件**做非同步輪詢確認建立結果；`create` 回傳的 `ElementId` 要能被呼叫端**立即**、獨立查詢到（同步、確定性驗證），不得要求「等一下再查」。
- 掃描（discover）≠ 自動建立（create）：discover 只回報候選，任何寫入動作都要走過斷點 1、2。
- Discover 回傳的 CAD 名稱與 Revit identity **分開保留**：CAD 名稱（如 `A$C87ebd845`）給人核對用，Revit identity（如 `B1F消防撒水.dwg.A$C87ebd845`）是 preview/create 實際傳遞、比對的鍵值；Agent **不得**用字串組合自行推導 identity。

### 2.1 流程圖（`/domain-diagram` 腳本產出）

TODO 待補：待工具實作與參數定案後，用 `.claude/skills/domain-diagram/scripts/mermaid_from_spec.py` 產出確定性流程圖（比照 `domain/dwg-column-import.md` §2.1 的格式），並附流程健檢結論（迴圈有界退出、無死路、abort 出口可達等）。issue #100 留言（含 2026-08-05 補值）未涵蓋此項，待實作 PR 或後續月小聚補齊。

---

## 3. 座標鏈與 transform 信任邊界

本流程的座標鏈由三層組成，**preview 必須把三層都攤開給使用者核對**，而非只給最終結果：

1. **Block insertion point**：CAD 檔案內、Block 定義座標系下的插入點（原始 DWG/DXF 座標）。
2. **Block transform**：該 INSERT 實體相對於圖紙座標系的平移/旋轉/縮放（對應 CAD 內部的 block reference transform）。
3. **ImportInstance TotalTransform**：CAD 連結/匯入到 Revit 後，`ImportInstance` 疊加的整體變換（含連結時的 placement、unit、shared coordinates 等）。

最終落點 = Block insertion point 依序套用「Block transform」再套用「ImportInstance TotalTransform」後，落在 Revit 模型座標系的結果。preview 回傳的座標鏈必須是**可查驗的四點鏈**：Block insertion point → Block Transform → ImportInstance TotalTransform → Revit model point，每一層的中間值都要能被使用者或後續工具獨立核對，不能只給最終結果。

**transform 可信度判定條件（依 issue #100 2026-08-05 留言補值）**：

1. Transform 必須 **finite**（無 NaN/Infinity）、**可逆**（determinant ≠ 0）、**conformal**（角度保持，不做非等比例的剪切/扭曲）、**等比例縮放**（X/Y/Z 縮放係數一致）。
2. **鏡射（negative scale／reflection）可以繼續，但必須警告**；**非等比例縮放（non-uniform scale）判定不可信、必須停止**。
3. 可信度判定至少需要 **3 個分散且不共線（non-collinear）的 anchor 點**，每個 anchor 點的**殘差必須 ≤ 1 mm**（比較「Block insertion point 依序套用 Block Transform → ImportInstance TotalTransform」算出的模型座標，與該 anchor 在 Revit 模型中的獨立量測座標）。
4. **anchor 點少於 3 個**：不得判定可信，回傳狀態 `review_required` + 原因碼 `insufficient_anchor_points`。

一旦判定不可信（含 anchor 不足、非等比例縮放、殘差超標任一項），`preview` 必須：
- 明確標示哪些插入點受影響、原因（含對應的原因碼，例如 `insufficient_anchor_points`）。
- **回傳警告並拒絕該批次建立**，不得由 AI 或工具自行套用猜測性的 correction（例如自動假設某個縮放係數、自動假設某個旋轉修正量）。
- 交回使用者，由使用者回到連結對話框核對單位/比例/座標系後重新連結、重新 discover/preview。

這是本流程與 `dwg-column-import` 斷點 1（單位健檢 `preflight.unitSanity`）同一等級的「寫入前攔截」設計，但適用對象是**逐點**的座標鏈而非整批的尺寸/單位統計，因此判定粒度更細（可能整批多數 ready、少數幾點 transform 不可信）。

**重要警告（殘差 0 mm 不等於工程正確）**：2026-08-05 實測中，三個 anchor 點（`DB-001`、`DB-477`、`DB-106`，門檻 1 mm）的最大／平均殘差均為 **0 mm**。這**只證明 Revit API 兩條 Transform 路徑（Block Transform 疊加路徑 vs. Revit 模型中的獨立量測）計算結果一致**，**不代表原始 DWG 的外部真值（真實座標）正確**，也**不代表 CAD Block 的 insertion point 在工程用途上是對的**。這個方法讀的是 CAD Block 製作者設定的 insertion point；若原點離設備的真實幾何中心很遠，即使 Transform 誤差為 0 mm，結果也只是「精確地放到錯誤位置」。因此即使 transform 判定為可信，使用者仍須額外確認：Linked DWG 已正確對位、同名 Block 的原點在圖面上意義一致、Block／FamilySymbol 的 mapping 正確。這些屬於「transform 數學上可信」之外、無法由座標鏈健檢自動驗證的工程判斷，preview 的 `ready` 狀態不能被誤讀為「工程位置已確認」。

---

## 4. 關鍵工程確認點

### 4.1 重複容差與 duplicate 判定
`preview` 的重複容差（`duplicateToleranceMm`）**預設 10 mm**，使用者可覆寫；回傳結果必須**明示實際採用值**、以及該值是 `default` 還是 `user-provided`，不得讓使用者猜工具實際用了多少。

判定方式：**相同 FamilySymbol + 相同 Level**，以 XY 平面距離是否小於容差判定重複。兩種重複來源分開回報：
- `duplicate_existing`：與專案中**既有** FamilyInstance 重複（附既有 `ElementId`）。
- `duplicate_in_batch`：與**本次掃描批次內**其他候選點互相重複。

`duplicate` 不自動略過或自動合併——**preview 必須完整列出所有重複群組**，不得自行合併或刪除。

**v1 政策裁決（維護者確認，2026-08-10）：`create` 可以在使用者明確核准後略過 duplicate**，但必須同時滿足三個條件：

1. **逐筆回報**：對每一筆被略過的候選，回傳 `duplicate_existing` 或 `duplicate_in_batch`、對應的既有 `ElementId`（若為 `duplicate_existing`）、候選 identity、以及判定原因（容差值、距離）。
2. **Preview 一律完整列出、不得自行合併或刪除**：略過 duplicate 是 `create` 階段的行為，不改變 `preview` 必須攤開全部重複群組的鐵則。
3. **「使用者核准」必須是明確傳入的參數**（例如一個布林旗標或核准清單），**不得由 Agent 自行推定「使用者應該想略過」**——沒有明確核准參數時，`create` 必須維持既有行為（遇 duplicate 即回報、不建立該筆）。

### 4.2 offset 與 level 換算單位
`offset` 為相對於 `levelId` 對應樓層的垂直偏移，輸入參數名為 `offsetMm`，**單位為 mm**。此處比照 `dwg-column-import` 已知的 `modify_element_parameter` 陷阱：Revit 內部長度單位為 feet，呼叫端若直接把 mm 數值原封傳入 Revit API 會錯 304.8 倍。**工具內部必須把 `offsetMm` 換算為 feet 後才寫入 Revit API**，工具與呼叫端的參數命名（`offsetMm` 而非裸的 `offset`）本身就是明確標示單位的作法，避免同一類單位陷阱重演。

### 4.3 unsupported_family 判定條件
`familySymbolId` 對應的族群若不是 non-hosted / level-based / point-placement，`preview` 應回傳狀態 `unsupported_family` 並**不得嘗試放置**。判定依據（依 issue #100 2026-08-05 留言補值）：**v1 只接受 `FamilySymbol.Family.FamilyPlacementType == FamilyPlacementType.OneLevelBased`**；以下型別一律回 `unsupported_family`：hosted（face-based）、work-plane-based、view-based、curve-based。`familySymbolId`、`levelId`、`offsetMm` 必須由使用者在斷點 1 明確指定，工具**不自動選擇**最合適的族群或樓層。

### 4.4 SubTransaction 單筆失敗不回滾其他
`create` 用主 `Transaction` 包住整批，內部每個插入點各自開一個 `SubTransaction`：單筆放置失敗（例如該點 transform 邊界情況、族群放置例外）**只回滾該筆**，不影響同批其他已成功的 `SubTransaction`。回傳結果需列出每筆的成功/失敗與失敗原因，供使用者判斷是否需要針對失敗項目單獨重跑。

**⚠️ 尚未驗證項**：2026-08-05 那輪的 20 筆動態試放使用的是 SC REVIT 的動態試放通道，**沒有套用 SubTransaction**（全部 20 筆一次性建立、獨立回查 20/20 通過，但未測試「部分失敗」情境）。因此「主 Transaction + 逐筆 SubTransaction 的部分失敗隔離」目前**只是規格陳述，尚未被實測驗證**，仍是正式 `create_family_instances_from_dwg_blocks` command 實作完成後的**必要驗收項目**（見 §7 QA 清單）。

---

## 5. 第一版邊界

- **只支援** non-hosted、level-based、point-placement 的 FamilySymbol（如 `OneLevelBased`）。
- **不支援** hosted（face-based、work-plane-based、view-based、curve-based 等）族群放置——第一版偵測到即回 `unsupported_family`，不做特殊處理或降級嘗試。
- **不自動選擇** `familySymbolId` 或 `levelId`——必須由使用者在斷點 1 明確指定，本工具不猜測「哪個族群/樓層最合適」。
- **不轉輪廓**——本流程只處理點狀 Block 插入點，不處理多線段/多邊形幾何（那是 dwg-column/dwg-beam 的範疇）。
- **不做人工校正（correction）**——transform 不可信時只停止、警告，不提供自動修正選項（第一版刻意不做，避免掩蓋座標問題）。
- **v1 只支援已連結（Linked）DWG，Imported DWG 留待後續版本**（維護者 2026-08-10 裁決，理由與 §1 第 5 點相同：實測中出現 Linked DWG 記錄路徑失效的 `NotFound` 案例，且 Imported 的 `TotalTransform` 語意與失連風險更高，v1 排除是正確的範圍收斂）。
- **不含** AutoCAD Core Console 備援、Preview receipt／session 機制、50 筆以上的群組試放——這些是 issue #100 2026-08-05 留言明確排除於 v1 之外的項目，留待後續討論，不阻擋本 SOP 作為 v1 審閱基準。

---

## 6. 已知限制與實機驗證

本節數字均為 NicheSam 在 issue #100 提供的實測結果（Revit 2024）：

**第一輪（2026-07-23，SC REVIT 現有人工工具，僅供確認人工流程哪些步驟不能沿用）**：
- 測試模型：`20260520-水岸外掛測試_分離`。
- 已實測：使用已載入的 `B1F消防撒水.dwg`，掃描到 14 種 Block／693 個插入點；取 `bt11_10` 前 5 點，對應 `噴頭 - 直立型 : 25mm`、Level B1FL、offset 0 mm，建立 5 筆、Duplicate 0、Failed 0，`ElementId` 為 `13724492`–`13724496`。
- 驗證方式：`create` 回傳的每個 `ElementId` **逐一獨立查詢**（非批次假設全部成功），確認族群、型別、樓層、位置與旋轉皆與建立回傳一致。
- 該輪也發現預覽點與 CAD 幾何有明顯偏移，且既有人工工具靠使用者拖曳整個預覽群組校正——這正是 v1 AI 工具**不能**沿用的部分（見 §3）。

**第二輪（2026-08-05，改用畫面反白選取的 `A$C87ebd845`，供本次 domain 補值的實測基準）**：
- 沿用同一 Linked DWG（`B1F消防撒水.dwg`）與同一 Revit view（`SC 預留套管平面 - B1FL`）。
- Block `A$C87ebd845`（Revit API 完整 identity：`B1F消防撒水.dwg.A$C87ebd845`）精確命中 **484 筆**插入點（取代第一輪 14 種 Block／693 筆的粗基準，作為本輪判定依據的具體 Block）。
- Preview 唯讀：模型元素數 12,761 → 12,761，未啟動 Transaction。
- Transform 三點驗證：anchor `DB-001`、`DB-477`、`DB-106`，門檻 1 mm，最大／平均殘差 **0 mm**（見 §3 對此結果的正確解讀——0 mm 只證明兩條 Transform 路徑計算一致，不代表 CAD 原始真值或 insertion point 工程語意正確）。
- 試放：`DB-001` 至 `DB-020`（20 筆），`ElementId` `13729409`–`13729428`；獨立回查 requested 20／passed 20／failed 0；位置最大差約 `1.09 × 10⁻¹¹ mm`、旋轉最大差約 `3.41 × 10⁻¹³ 度`（浮點精度等級，非工程誤差）。
- 回查後模型元素數 12,781 → 12,781，未啟動 Transaction、模型未變更。
- 測試使用 `噴頭 - 直立型 : 25mm` 僅為讓結果在圖面中可辨識，**不代表** `A$C87ebd845` 已確認對應此族群（mapping 語意留待後續，不寫死）。

TODO 待補：目前實測紀錄中**沒有**具體的 `duplicate`（`duplicate_existing`／`duplicate_in_batch`）或 `unsupported_family` 失敗案例（兩輪測試都是全新插入點、且都用 `OneLevelBased` 族群，未觸發過這兩種狀態）；也沒有「transform 判定為不可信、`preview` 實際拒絕建立」的案例紀錄。issue #100 留言（含 2026-08-05 補值）未涵蓋這幾類具體案例，待後續實測補上（比照 `dwg-column-import.md` §6 的案例格式）。

---

## 7. QA／驗收清單

- [ ] `discover`（`get_dwg_block_instances`）有回傳 Block 名稱／插入點／旋轉清單，且 CAD 名稱與 Revit identity 分開保留
- [ ] 目標 DWG 為**已連結（Linked）**（`ImportInstance.IsLinked = true`）；若為 Imported DWG，v1 應直接拒絕
- [ ] `preview` 全部為 `ready`，或 `duplicate_existing`／`duplicate_in_batch`／`unsupported_family`／`review_required` 已與使用者協作處置（非自動略過、非自動合併）
- [ ] 座標鏈（Block insertion point → Block Transform → ImportInstance TotalTransform → Revit model point）已攤開供使用者核對
- [ ] Transform 可信度已依 §3 條件核對：finite、可逆、conformal、等比例縮放；≥ 3 個分散不共線 anchor、每點殘差 ≤ 1 mm
- [ ] 若有 transform 不可信警告（含 `insufficient_anchor_points`）：已停止建立、未套用任何猜測性 correction，已交回使用者處理
- [ ] **斷點 1**：familySymbolId、levelId、offsetMm、duplicateToleranceMm（含是否為 default 10 mm 或 user-provided）已與使用者確認
- [ ] 若 `create` 略過 duplicate：使用者核准為**明確傳入的參數**（非 Agent 自行推定），且每筆略過項目已回報既有 ElementId、候選 identity、原因
- [ ] **斷點 2**：使用者已明確確認建立，`create` 呼叫參數與 preview 完全一致
- [ ] `create` 回傳的每個 ElementId 已逐一獨立查詢驗證存在
- [ ] 未使用 Idling 事件做非同步輪詢確認結果（同步、確定性驗證）
- [ ] **尚未實測驗證項**：主 `Transaction` + 逐筆 `SubTransaction` 的「單筆失敗不回滾其他」——2026-08-05 動態試放通道未套用 SubTransaction；`create_family_instances_from_dwg_blocks` 已於 PR #115 實作（含 SubTransaction 迴圈），但**仍未對真實 Revit 執行過**，此項待真實 Revit 環境補測（見 §4.4、文件開頭狀態橫幅）
- [ ] transform 殘差 0 mm 不得被誤讀為工程位置正確：已額外確認 Linked DWG 對位、同名 Block 原點一致、Block／FamilySymbol mapping 正確（見 §3 警告）

---

## 參考 / Reference

- 相關 domain：`domain/dwg-column-import.md`（矩形輪廓翻模結構柱，互補而非取代）、`domain/dwg-beam-import.md`（雙線中心線翻模結構樑，互補而非取代）、`domain/tool-capability-boundary.md`（工具能力邊界原則）
- 相關既有工具（非本流程專屬，但同屬 CAD/ImportInstance 情境，供對照）：`link_cad_to_view`、`link_cads_by_floor`（`MCP-Server/src/tools/cad-link-tools.ts`）——負責把 DWG/DXF 連結到視圖，是本流程 §1 前置條件「CAD 已連結」的上游步驟，但**不做**本文件描述的 Block 插入點掃描／放置。
- SC REVIT 端參考路徑（供實作 PR 的工程邏輯對照，**只取邏輯、不移植 UI／預覽群組／人工拖曳狀態**）：`dwg_block_reader.py`、`revit_addin/src/RfaMetadataApplication.cs`、`revit_addin/src/Handlers/CadPointHandler.cs`。
- 本流程 `discover`/`preview`/`create` 的工具名稱已定案（見 §2）：`get_dwg_block_instances`／`preview_family_instances_from_dwg_blocks`／`create_family_instances_from_dwg_blocks`。實作路徑（issue #113／PR #115，@Rumi-3653）：C# executor `MCP/Core/CadBlockPlacementExecutor.cs`；dispatcher case `MCP/Core/CommandExecutor.cs`；MCP tool 定義 `MCP-Server/src/tools/cad-block-placement-tools.ts`（掛載於 `full`／`mep` profile，見 `MCP-Server/src/tools/index.ts` 的 `PROFILE_MODULES`）。**這些路徑已建立、R22–R26 建置皆通過，但尚未對真實 Revit 執行過**——見文件開頭狀態橫幅列出的四項未驗證項目。目前尚無 Skill 引用本 domain，frontmatter 的 `referenced_by` 維持 `[]`。
