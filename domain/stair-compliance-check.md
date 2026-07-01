---
name: stair-compliance-check
description: 同時檢討一般樓梯與無障礙樓梯的標準流程。當用戶提到「樓梯檢查」「無障礙樓梯」「梯級」「扶手」「淨高」時啟用。
metadata:
  version: "1.0"
  updated: "2026-03-23"
  created: "2026-04-05"
  contributors:
    - "shuotao"
  references: []  # TODO: 月小聚補法規條號或外部依據
  related: []  # TODO: 月小聚補相關 domain（檔名）
  referenced_by: []  # TODO: 月小聚補（被哪些 skill 引用）
  tags: [樓梯, 無障礙, 法規, QA, 建築技術規則]
---

# 全樓梯法規檢討流程 (一般 + 無障礙)

## 目標

建立一套可重複執行的檢討流程，於同一次檢查中：
- 檢討所有樓梯是否符合一般樓梯法規
- 檢討標記為無障礙樓梯者是否符合無障礙規範
- 產生可追蹤的缺失清單與圖面標示結果

---

## 法規來源

### 一般樓梯
- 建築技術規則建築設計施工編 第 33 條（樓梯及平台寬度、級高、級深）
- 建築技術規則建築設計施工編 第 34 條（平台設置間距與深度）
- 建築技術規則建築設計施工編 第 35 條（垂直淨高 >= 190cm）
- 建築技術規則建築設計施工編 第 36 條（扶手）

### 無障礙樓梯
- 建築技術規則建築設計施工編 第 167 條、第 167-2 條
- 建築物無障礙設施設計規範 302.2、302.3、303.5.2
- 內政部營建署書函 102.03.18. 營署建管字第 1020012496 號

> 注意：本流程為 AI 輔助檢核，最終法規認定仍以主管機關最新公告與審查意見為準。

---

## 前置條件

- Revit 模型已開啟
- MCP 服務已啟動
- 樓梯類型參數已設定（建議使用 `類型標記`）
- 已建立樓梯用專案參數（建議名稱：`樓梯檢查成果`，資料類型：文字，實體：實體，綁定品類：樓梯）
- 專案具備基本房間命名（作為備援分流）
- 使用者已確認建築用途類組（影響第 33 條門檻）

若建築用途類組尚未確認，流程必須停在步驟 -1，不得直接進入步驟 0~6 的任何檢查流程。

### 強制閘門（Hard Gate，必須執行）

以下必要資訊未完整取得前，禁止呼叫任何模型查詢、標註、回寫工具：
- get_active_schema
- get_category_fields
- query_elements_with_filter
- get_all_levels
- get_rooms_by_level
- measure_distance
- get_field_values
- get_all_views
- get_active_view
- set_active_view
- create_dimension
- create_section_view
- create_stair_section_view
- create_text_note
- create_detail_lines
- create_filled_region
- get_stair_actual_width
- check_stair_headroom
- modify_element_parameter
- override_element_graphics
- clear_element_override

未通過 Hard Gate 時，唯一允許行為：
- 僅可向使用者追問缺漏資訊
- 僅可回覆「目前停在步驟 -1，待補齊必要資訊」

違反 Hard Gate 視為流程錯誤，必須立即中止並重新從步驟 -1 開始。

### 系統能力現況對齊（2026-03 更新）

為避免流程文件與實際工具能力脫鉤，執行前先套用以下對齊規則。本專案支援 **Revit 2022 至 2026** 版本（含 Revit 2025/2026 的 .NET 8 核心）。

#### 1. 已確認可用工具（本專案專屬）
| 工具名稱 | 分類 | 說明 | 替代做法 (若無此工具時) |
|---|---|---|---|
| `get_stair_actual_width` | 數據查詢 | 取代 `get_stair_run_widths`，直接讀取 `ActualRunWidth`。 | 使用 `get_field_values` 嘗試讀取，或以 `measure_distance` 手動量測。 |
| `check_stair_headroom` | 幾何檢核 | **新開發**：自動執行 190cm 淨高碰撞檢核，回傳結果。 | 以 `measure_distance` 於梯段起點、中點、終點執行垂直量測。 |
| `create_stair_section_view`| 視圖建立 | **新開發**：自動對齊梯段長向建立最佳剖面，用於標註。 | 使用 `create_section_view` 或手動在 Revit 建立剖面並命名供 AI 搜尋。 |
| `create_dimension` | 標記標註 | 在指定視圖建立標註線。 | 僅於報告中紀錄數值，或請使用者在對應位置手動標註以便確認。 |
| `create_text_note` | 標記標註 | 在指定視圖建立法規說明文字。 | 將結果寫入樓梯參數（如 `樓梯檢查成果`）或僅於對話中提示。 |
| `create_detail_lines` | 標記標註 | 繪製指示線（如淨高線、有效帶）。 | 無。須改為文字描述或請使用者自行繪製。 |
| `create_filled_region` | 標記標註 | 建立填充區域標示違規範圍。 | 使用 `override_element_graphics` 對元素上色（如紅色）來替代。 |

#### 2. 核心底層工具（標準 MCP 能力）
- `get_active_schema`, `get_category_fields` (確認品類與欄位)
- `query_elements_with_filter` (取得樓梯集合)
- `get_all_levels`, `get_rooms_by_level` (空間關係判斷)
- `get_all_views`, `get_active_view`, `set_active_view` (視圖切換)
- `measure_distance` (通用手動量測)
- `modify_element_parameter` (回寫檢核結果)
- `override_element_graphics`, `clear_element_override` (視覺化覆寫)

#### 3. 參數回寫邊界
- `modify_element_parameter` 僅能修改「既有參數值」，不包含自動建立新的「專案參數」或「共享參數」。請使用者預先建立 `樓梯檢查成果` 參數。

#### 4. Revit 版本特性
- **Revit 2022-2024**: 基於 .NET 4.8，ElementId 為 32 位元整數。
- **Revit 2025-2026**: 基於 .NET 8，ElementId 已升級為 64 位元長整數 (long)。系統已在相容層自動處理。

---

## 樓梯分流規則（使用者可自訂，類型參數優先）

### A. 類型參數分流（推薦）

優先使用樓梯類型參數做分流，避免幾何關聯不穩定。

| 參數 | 說明 | 範例 |
|---|---|---|
| classificationMode | 分流模式 | `typeParameter` |
| typeFieldName | 樓梯類型參數欄位名稱 | `類型標記` |
| accessibleTypeValues | 視為無障礙樓梯的值清單 | `A` |
| generalTypeValues | 視為一般樓梯的值清單 | `B` |
| typeConflictPolicy | 同時命中時優先類別 | `Accessible` 或 `General` |
| emptyTypePolicy | 類型值為空時處理方式 | `Manual-Review` 或 `General` |
| sourceConflictPolicy | 類型標記與房間名稱判定衝突時處理 | `AskUser`（建議） |

預設值（使用者未提供時）：
- classificationMode: `typeParameter`
- typeFieldName: `類型標記`
- accessibleTypeValues: `A`
- generalTypeValues: `B`
- typeConflictPolicy: `Accessible`
- emptyTypePolicy: `Manual-Review`
- sourceConflictPolicy: `AskUser`

### B. 房間命名分流（備援）

若模型未維護類型參數，才使用房間名稱分流。

| 參數 | 說明 | 範例 |
|---|---|---|
| accessibleKeywords | 無障礙樓梯關鍵字（任一命中） | `無障礙樓梯`, `Accessible Stair` |
| generalKeywords | 一般樓梯關鍵字（任一命中） | `樓梯`, `梯間`, `Stair` |
| conflictPriority | 同時命中時優先類別 | `Accessible` 或 `General` |
| unclassifiedPolicy | 未命中時處理方式 | `Manual-Review` 或 `General` |

備援預設值（使用者未提供時）：
- accessibleKeywords: `無障礙樓梯`, `Accessible Stair`
- generalKeywords: `樓梯`, `梯間`, `Stair`
- conflictPriority: `Accessible`
- unclassifiedPolicy: `Manual-Review`

分流邏輯：
1. 若 classificationMode = typeParameter，先比對類型參數值。
2. 類型值為空或未命中，再套用 emptyTypePolicy。
3. 若有房間命名資料，應同步執行 roomName 判定做交叉檢核。
4. 若同一樓梯出現「typeParameter 判定」與「roomName 判定」衝突，依 sourceConflictPolicy 執行：
  - AskUser：立即詢問使用者確認，並暫停該樓梯的自動分類。
5. 若 classificationMode = roomName，改用房間命名規則。
6. 若最終為 Manual-Review，不得直接視為合格。

---

## 執行步驟

### 步驟 -1：收集使用者分流定義

對話原則（必須遵守）：
1. 對使用者提問時，禁止直接丟出參數名（例如：`typeFieldName`、`accessibleTypeValues`、`sourceConflictPolicy`）。
2. 先問白話問題，拿到答案後再由系統轉成內部參數。
3. 一次最多問 1~2 題，避免長清單造成理解負擔。
4. 每題都要附可直接回覆的選項或範例。

#### 使用者提問腳本（白話優先，建議直接使用）

```text
AI：我先確認幾個重點，避免後面套錯法規。

Q1. 這個案子的主要用途是什麼？
（例如：住宅、學校、醫院、商場）

Q2. 這棟是單一用途，還是不同樓層/區域有不同用途？
1) 單一用途
2) 混合用途

Q3. 若是混合用途，內控檢查要不要另外用「最嚴格門檻」再看一次？
1) 要
2) 不要

Q4. 面積條件請幫我確認：
- 地上各層居室樓地板面積是否有任一層 > 200m2？（是/否/待確認）
- 地下層居室面積合計是否 > 200m2？（是/否/待確認）
- 以上資料來源是什麼？（法規檢討表 / 面積計算表 / 你口頭確認）

Q4-1. 地下層樓梯快速判定要不要啟用（預設啟用）？
1) 啟用：樓梯起始樓層（基準樓層）高程 < 0 時，先標記為「地下層高機率」
2) 停用

Q5. 這次檢查範圍要選哪個？
1) 全專案
2) 只檢查目前畫面（目前作用視圖）

Q6. 你們平常怎麼分「一般樓梯」和「無障礙樓梯」？
1) 用樓梯類型欄位（推薦）
2) 用房間名稱關鍵字

Q6-1. 你希望我怎麼標示不合法規的地方？
1) 尺寸 + 文字註記（建議）
2) 顏色/圖形覆寫
3) 混合（尺寸 + 文字 + 顏色）
4) 先只出報告，不落圖面標示

Q7. 上面都確認後，我就開始檢查。是否開始？
1) 開始
2) 先不要
```

```
內部參數（系統用，不直接這樣問使用者）：
  - 建築用途類組（第 33 條適用類別）
  - 用途適用範圍（全棟單一用途 / 分區分層混合用途）
  - 用途判定策略（建議：AskUser；混合用途時是否採最嚴格門檻僅作加嚴檢核，不可覆蓋法規基準門檻）
  - 第 33 條面積觸發資料（地上各層居室樓地板面積、地下層居室面積合計、資料來源）
  - 住宅/其他用途是否觸發「地上各層居室樓地板面積 > 200m2 或地下層居室面積合計 > 200m2」（是/否/待確認）
  - 是否啟用地下層高機率判定（baseLevelElevation < 0，預設：啟用）
  - 地下層高機率判定的高程基準（預設：< 0）
  - 專案基準高程是否可能非 1F=0（是/否；若是，需列 Manual-Review）
  - 檢查範圍（inspectionScope）：`Project`（全專案）或 `ActiveView`（僅當前視圖）
  - 若 inspectionScope = ActiveView：是否以「目前作用視圖」為唯一檢查範圍（是/否）
  - 是否有針對無障礙樓梯製作類型標記（是/否）
  - classificationMode (typeParameter / roomName)
    （白話：本專案要用「類型標記」還是「房間名稱」來區分一般樓梯與無障礙樓梯？）
  - typeFieldName（預設：類型標記）
    （白話：如果用類型標記，欄位名稱是什麼？）
  - accessibleTypeValues
    （白話：哪些值代表「無障礙樓梯」？例如 A）
  - generalTypeValues
    （白話：哪些值代表「一般樓梯」？例如 B）
  - typeConflictPolicy
    （白話：同時命中一般/無障礙時要優先哪一類？）
  - emptyTypePolicy
    （白話：類型值空白時要列為人工確認，還是先暫歸一般樓梯？）
  - sourceConflictPolicy（建議：AskUser）
    （白話：若「類型標記判定」與「房間名稱判定」互相矛盾，要不要先問你再決定？）
  - resultFieldName（建議：樓梯檢查成果）
  - markerMode（dimensionText / colorOverlay / hybrid / reportOnly）
    （白話：不合法規處要用尺寸+文字、顏色覆寫、混合，或只出報告）
  - markerFallbackPolicy（askUser / fallbackToReport）
    （白話：若所需工具不可用，要先問你，或先退回報告模式）
  - stairGroupingMode（auto / multistory / singleRun）
  - groupKeyField（選填，建議：群組編號 或 類型標記）
  - levelChainTolerance（選填，預設：僅允許頂部樓層=下一段基準樓層）

若使用 roomName 備援，另需：
  - accessibleKeywords
  - generalKeywords
  - conflictPriority
  - unclassifiedPolicy

目的：將樓梯分類規則明確化，避免分流誤判
```

步驟 -1 完成判定（全部同時成立才可進入步驟 0）：
1. 已取得建築用途類組。
2. 已取得用途適用範圍（全棟單一用途 / 混合用途）。
3. 若為混合用途，已取得用途判定策略（是否採最嚴格門檻）。
4. 已完成第 33 條面積觸發判定（含地上各層居室樓地板面積、地下層居室面積合計與來源）。
5. 已確認分類模式（typeParameter 或 roomName）與對應必要參數。
6. 已確認檢查範圍（Project / ActiveView），且 ActiveView 模式下已確認目前作用視圖。
7. 已確認地下層高機率判定是否啟用（預設啟用）。
8. 已確認違規標示策略（markerMode）與工具不可用時的回退策略（markerFallbackPolicy）。
9. 使用者已明確確認可開始檢查（可用「開始檢查」或等效語句）。

若任一條件未滿足：
- 不得進入步驟 0。
- 不得執行任何 Revit 查詢或標註工具。
- 必須先補問缺漏項目。

#### 用途確認對話範本

```text
AI：在套用第 33 條樓梯寬度/級高/級深門檻前，請先確認建築用途。
請提供：
1) 用途類組（例如：學校、醫院、商場、一般住宅等）
2) 適用範圍（全棟單一用途 或 分區分層混合用途）
3) 若為混合用途，是否採最嚴格門檻？（是/否）
4) 第 33 條面積觸發資料：
  - 地上各層「居室樓地板面積」是否有任一層 > 200m2？
  - 地下層居室面積合計是否 > 200m2？
  - 上述數據的來源（法規檢討表/面積計算表/使用者確認）

在你回覆前，我不會執行任何 Revit 檢查工具。
若未提供上述資訊，系統將停在步驟 -1，不得進入後續檢查。
```

#### 分流確認對話範本（白話版，建議優先）

```text
AI：我需要先知道你們案子怎麼分樓梯類型，才不會判錯。

先選一個：
1) 用「樓梯類型欄位」區分（推薦）
2) 用「房間名稱」區分

如果你選 1，請直接回：
- 欄位名稱是什麼（例如：類型標記）
- 哪個值代表無障礙樓梯（例如：A）
- 哪個值代表一般樓梯（例如：B）
- 碰到衝突時要優先哪一類，或先停下來問你

如果你選 2，請直接回：
- 哪些關鍵字代表無障礙樓梯
- 哪些關鍵字代表一般樓梯
- 同時命中時要優先哪一類

補充：若資料不足或衝突，我預設先列「人工確認」，再請你決定。
```

#### 檢查範圍確認對話範本（白話版，必問）

```text
AI：這次樓梯檢查，你希望我跑哪個範圍？
1) 全專案（會彙整多個視圖中的樓梯）
2) 只檢查目前畫面（你現在打開的視圖）

請直接回覆 1 或 2。
如果你選 2，我會把「目前作用視圖」當成唯一檢查範圍。
```

#### 衝突確認對話範本

```text
AI：偵測到樓梯分類衝突。
- 類型標記判定：Accessible
- 房間名稱判定：General

請確認此樓梯應歸類為哪一類？
1) Accessible（無障礙樓梯）
2) General（一般樓梯）
3) 暫列 Manual-Review，稍後人工判定
```

### 步驟 0：確認模型內有可檢查對象（僅限通過步驟 -1 後）

```
使用工具：get_active_schema
目的：確認目標範圍（Project 或 ActiveView）可找到 Stairs、Rooms 等品類
補充：
  - ActiveView 範圍：直接用目前作用視圖（或指定 viewId）。
  - Project 範圍：應先取得視圖清單，再逐視圖以 viewId 執行 get_active_schema 抽樣確認。
```

### 步驟 1：建立樓梯檢查母集合

```
使用工具：get_category_fields
參數：category = Stairs
目的：先取得 Stairs 參數名稱（禁止猜測參數名）
```

```
使用工具：query_elements_with_filter
參數：category = Stairs
目的：取得檢查母集合（依檢查範圍決定）
```

檢查範圍套用規則（inspectionScope）：
1. `inspectionScope = ActiveView`：
  - 僅以目前作用視圖查詢 Stairs。
  - 後續步驟 2~6 僅對此集合執行，不可外推到其他視圖。
2. `inspectionScope = Project`：
  - 先取得視圖清單（建議平面圖優先），逐視圖查詢 Stairs。
  - 合併後以 ElementId 去重，形成全專案母集合。
  - 報告需標示來源視圖數量與去重後元素數量。
3. 未明確指定 inspectionScope：
  - 一律停在步驟 -1，不得進入步驟 2。

### 步驟 2：依分流模式分類樓梯

#### 2A. 類型參數分流（預設）

```
使用工具：query_elements_with_filter
參數：
  - category = Stairs
  - returnFields 包含 typeFieldName（例如：類型標記）
目的：依 accessibleTypeValues / generalTypeValues 分為：
  - General（一般樓梯）
  - Accessible（無障礙樓梯）
  - Unclassified（未分類）
```

#### 2B. 房間命名分流（備援）

```
使用工具：get_all_levels
目的：取得樓層清單
```

```
使用工具：get_rooms_by_level
參數：level = 每一層
目的：取得房間名稱與範圍，依使用者定義將樓梯分為：
  - General（一般樓梯）
  - Accessible（無障礙樓梯）
  - Unclassified（未分類）
```

#### 2C. 樓梯群組與多層辨識（兼容不同建模方式）

```
使用工具：query_elements_with_filter
參數：
  - category = Stairs
  - returnFields 至少包含：基準樓層、頂部樓層、typeFieldName
目的：建立 StairGroup，避免同一座垂直交通系統被拆成多筆後重複或漏檢
```

辨識策略：
1. `stairGroupingMode = auto`（預設）
2. `stairGroupingMode = multistory`（使用者明確採多層樓梯建置）
3. `stairGroupingMode = singleRun`（使用者以單層/逐段樓梯建置）

多層樓梯建置（multistory）判定：
1. 以同一分類（General/Accessible）內元素建立樓層鏈。
2. 若 A 段之 `頂部樓層` = B 段之 `基準樓層`，視為同一 StairGroup。
3. 鏈長 >= 2 視為「多層樓梯群組」。

非多層樓梯建置（singleRun）判定：
1. 若有 `groupKeyField`（例如群組編號、類型標記）則優先依該值分組。
2. 若無群組欄位，改以「分類 + 相鄰樓層關係 + 使用者確認」建立群組。
3. 無法可靠分組時標記 `Manual-Review`，禁止自動合併判定。

注意事項：
1. 同一模型可同時存在 multistory 與 singleRun 兩種建法，應允許混合判定。
2. 法規檢核結果需同時保留「元素層級」與「群組層級」兩種輸出。

#### 2D. 地下層樓梯高機率判定（啟發式）

目的：在不改變法規分流結果（General/Accessible）的前提下，快速標記可能屬於地下層服務的樓梯。

判定規則：
1. 若 `baseLevelElevation < 0`，標記為 `Basement-Likely`。
2. 此標記僅為「高機率」提示，不可直接取代法規用途與面積判定。
3. 若專案基準非 `1F = 0`、存在大量標高偏移、或樓層命名與標高不一致，該標記需降級為 `Manual-Review`。
4. 若使用者在步驟 -1 選擇停用此規則，則本段不執行。

輸出要求：
1. 每座被標記樓梯需記錄：ElementId、基準樓層名稱、基準樓層高程。
2. 若降級為 Manual-Review，需記錄降級原因（例如：專案高程基準疑義）。

### 步驟 3：套用一般樓梯規則（範圍內所有樓梯都要跑）

| Rule ID | 檢核項目 | 基準值 | 檢核方式 | 自動化 |
|---|---|---|---|---|
| G-ST-001 | 淨寬 | 依第33條用途類組 | 實測淨寬比對 + 人工確認用途類組 | 半自動 |
| G-ST-002 | 梯級高度 R | 第33條上限（16/18/20cm） | 參數比對 | 自動 |
| G-ST-003 | 梯級深度 T | 第33條下限（26/24/21cm） | 參數比對 | 自動 |
| G-ST-004 | 平台設置 | 每3m或4m需設平台，且深度 >= 樓梯寬 | 參數/幾何比對 | 半自動 |
| G-ST-005 | 垂直淨高 | >= 190cm | 幾何量測 | 半自動 |
| G-ST-006 | 扶手高度與設置 | >= 75cm，且依第36條條件設置 | 參數比對 + 人工 | 半自動 |
| G-ST-007 | 安全梯門扇迴轉半徑干涉 | 不得相交 | 幾何判讀 | 人工 |

#### G-ST-001 防誤判規則（最小梯段寬度不可當實際淨寬）

1. `最小梯段寬度` 是樓梯類型允許的下限設定值，不代表該樓梯在模型中的實際淨寬。
2. 禁止用 `最小梯段寬度` 直接判定 Pass/Fail。
3. G-ST-001 一律以「實測淨寬」作為判定依據：
  - 以可通行淨空範圍為準（需排除扶手、牆面突出物等影響）。
  - 至少取 3 個測點（梯段下、中、上），以最小值作為該梯段淨寬。
  - 多梯段樓梯以各梯段最小值中的最小者作為該樓梯判定值。
4. 實測資料來源優先順序：
  - A. 尺寸標註或 `measure_distance` 量測值。
  - A-1. `get_stair_run_widths` 回傳的 `Runs[].ActualRunWidthMm`（梯段實際寬度，mm）。
  - B. 幾何推導值（若可明確證明量測位置與淨空邊界）。
  - C. 類型參數（僅可做參考，不可作為合規判定證據）。
5. 若無法取得可追溯的實測淨寬，該樓梯的 G-ST-001 必須標記 `Manual-Review`，不得自動判定為 Pass/Fail。
6. 報告需保留寬度證據：量測值、量測方法、視圖名稱或 ViewId、測點位置描述。

目前正式做法（依工具可用性分流）：
1. 若環境可用 `get_stair_actual_width`：
  - 以 `get_stair_actual_width` 查詢。
  - 讀取回傳的實際寬度（mm），參與法規比對。
2. 若不可用 `get_stair_actual_width`（通用/手動替代之路）：
  - **初階自動化**：使用 `get_field_values` 嘗試讀取 StairsRun 類型下的「ActualRunWidth」參數。
  - **手動確證**：若參數無法讀取，使用 `measure_distance` 進行點對點量測（需配合 `create_dimension` 或由使用者手動標註）。
  - 若無法形成可追溯證據（無點位資料），一律標記 `Manual-Review`。

用途套用規則（影響 G-ST-001 / G-ST-002 / G-ST-003）：
1. 未確認建築用途類組：不得進行自動合格判定，全部列入 `Manual-Review`。
2. 住宅或其他非第 33 條前二類用途：
  - 若觸發「地上各層居室樓地板面積 > 200m2 或地下層居室面積合計 > 200m2」：套用 1.20m / 20cm / 24cm。
  - 若未觸發：套用 0.75m / 20cm / 21cm。
3. 學校、醫院、戲院、商場等第 33 條前二類用途：依該類別門檻套用（含 1.40m 類別）。
4. 混合用途：應依樓梯服務範圍分別套用法規門檻，不得直接以「最嚴格門檻」取代法規判定。
5. 若使用者要求「最嚴格」作為內控檢查，報告必須標示為「加嚴檢核」，且與法規判定結果分欄呈現。

### 步驟 4：套用無障礙樓梯加嚴規則（僅 Accessible）

| Rule ID | 檢核項目 | 基準值 | 檢核方式 | 自動化 |
|---|---|---|---|---|
| A-ST-001 | 無障礙樓梯配置 | 直通樓梯至少一座為無障礙樓梯 | 設計標註/命名檢核 | 半自動 |
| A-ST-002 | 梯級高度 R | <= 18cm | 參數比對 | 自動 |
| A-ST-003 | 梯級深度 T | >= 24cm | 參數比對 | 自動 |
| A-ST-004 | 梯級比例 | 55cm <= 2R + T <= 65cm | 計算檢核 | 自動 |
| A-ST-005 | 樓梯淨高 | >= 190cm | 幾何量測 | 半自動 |
| A-ST-006 | 樓梯轉折與平台 | 平順轉折、平台不得有梯級或高低差 | 幾何判讀 | 人工 |
| A-ST-007 | 扶手形式 | 連續、平順轉折、端部處理符合規範 | 模型 + 圖說檢核 | 人工 |
| A-ST-008 | 異質地磚配置 | 起終點警示與導引連續性符合規範 | 材質/圖說檢核 | 人工 |

### 步驟 5：標註缺失與人工複核項目

#### Fail 尺寸標註與文字註記（G-ST-001 / G-ST-002 / G-ST-003 / A-ST-002 / A-ST-003）

當下列規則判定 Fail 時，不得只在報告列出，必須同步建立可讀的尺寸化標示：
- 淨寬不足（G-ST-001）
- 梯級高度超限（G-ST-002 / A-ST-002）
- 梯級深度不足（G-ST-003 / A-ST-003）

標示原則：
1. 先標實際值，再標應有值。
2. 實際值以尺寸標註呈現。
3. 應有值以文字註記標示於尺寸旁。
4. 同一樓梯若有多個 Fail，可分別標示，但不得互相遮擋。

標示策略（可配置）：
1. 本流程不強制單一標示方式，應由使用者於步驟 -1 選擇 `markerMode`。
2. 可選模式：
  - `dimensionText`：尺寸標註 + 文字註記（建議預設）
  - `colorOverlay`：顏色/圖形覆寫
  - `hybrid`：尺寸標註 + 文字註記 + 顏色/圖形覆寫
  - `reportOnly`：僅輸出報告，不落圖面標示
3. 若所需工具不可用，依 `markerFallbackPolicy` 執行：
  - `askUser`：先詢問使用者再決定替代方案
  - `fallbackToReport`：先退回 reportOnly，並記錄 `Annotation-Tool-Gap`
4. 無論採用哪種模式，結果都必須回寫報告與 `樓梯檢查成果`（或使用者指定欄位）。

建議文字格式：
- 淨寬不足：`應 >= 1200mm`
- 梯級高度超限：`應 <= 180mm`
- 梯級深度不足：`應 >= 240mm`

視圖選擇規則：
1. 淨寬不足：優先在平面圖或可清楚表達淨寬的視圖標示。
2. 梯級高度 / 梯級深度：優先在剖面圖、立面圖或可清楚表達梯級關係的視圖標示。
3. 若目前作用視圖無法清楚表達該項尺寸，不得硬標，必須改找對應視圖。

對應視圖搜尋順序：
1. 目前作用視圖。
2. 樓梯基準樓層視圖。
3. 樓梯頂部樓層視圖。
4. 專案中既有剖面圖 / 立面圖 / 樓梯詳圖。

若找不到對應視圖：
1. 應針對該樓梯建立一張剖面圖，作為尺寸標註與文字註記的確認視圖。
2. 新建立之剖面圖應納入報告，記錄其 ViewId / ViewName。
3. 未建立剖面圖前，不得宣告該項標示作業完成。

目前 MCP 工具能力（2026-03 更新）：
1. **樓梯專題工具**：`get_stair_actual_width` (實體寬度查詢)、`check_stair_headroom` (自動化碰撞檢核)、`create_stair_section_view` (自動對齊長向剖面)。
2. **標示與標記類**：`create_dimension` (尺寸線)、`create_text_note` (文字註解)、`create_detail_lines`與`create_filled_region` (詳圖線與色塊)。
3. **視覺效果**：`override_element_graphics` (物件著色覆寫)。
4. **無工具替代路徑 (Fallback)**：
   - 若無法自動建立剖面，建議請使用者手動建立剖面視圖（視圖名稱包含「樓梯檢核」字樣以便搜尋）。
   - 若無法建立標註，檢核報應紀錄數值證據，並引導使用者手動確認。
   - 若無法於圖面標註，將缺失訊息寫入 `樓梯檢查成果` 參數中，引導使用者依 ID 搜尋定位。

標註最佳實踐：
- 級高/級深標註：優先使用 `create_stair_section_view` 以確保剖面切在梯段正中間。
- 標示原則：保持圖面整潔，若工具衝突或無法正確標註，優先回退並記錄於報告。
- 參數回寫：始終保持 `樓梯檢查成果` 參數同步，作為最後一線的人機協作橋樑。

#### 尺寸標註與視圖建立 SOP（正式流程）

1. 先取得 Fail 樓梯與對應 Fail 規則清單。
2. 依規則類型選擇合適視圖：
   - 淨寬：平面圖優先。
   - 級高 / 級深：優先使用 `create_stair_section_view` 建立專用剖面圖（若現有視圖不足以清晰表達）。
3. 在選定視圖中建立尺寸標註，呈現實際值。
4. 於尺寸旁使用 `create_text_note` 建立文字說明，標示應有值與不合格原因。
5. 完成後，抽查 1~2 座樓梯，確認尺寸與文字可讀、位置不互相遮擋。

多視圖同步原則（防止只在單一視圖有標註）：
- 最低要求：標註結果需出現在「目前作用視圖」或「最適合表達該違規項目的對應視圖」。
- 若目前作用視圖不適合標該項尺寸，允許改用基準樓層視圖、頂部樓層視圖、既有剖面圖或樓梯詳圖。
- 若仍找不到對應視圖，**應強制使用** `create_stair_section_view` 建立樓梯剖面圖作為 Fail 標示視圖。

檢查結果寫入策略（正式專案建議）：
- 原則：不要預設寫入 `標記`、`備註`，避免與既有圖說流程衝突。
- 優先做法：寫入樓梯專案參數 `樓梯檢查成果`（或使用者指定的 resultFieldName）。
- 訊息格式：使用人類可讀短句，不僅是代碼。
- 建議格式：`不合格：<中文原因1>、<中文原因2>`
- 範例：`不合格：淨寬不足、級深不足`
- 禁用混合語句：同一欄位不可出現「不合格...（某子規則通過）」的混合訊息。
- 若需呈現通過資訊：請另寫入獨立欄位（例如 `樓梯檢查補充` 或 `樓梯檢查規則碼`）。
- 若需追蹤法規碼：另建專案參數 `樓梯檢查規則碼`，例如：`G-ST-001,G-ST-003`

### 步驟 6：產生整合報告

報告至少包含以下區塊：
- Scope Summary（本次檢查範圍：Project 或 ActiveView）
- General Summary（一般樓梯）
- Accessible Summary（無障礙樓梯）
- Basement-Likely Summary（基準樓層高程 < 0 的樓梯數量、清單、Manual-Review 數量）
- Unclassified Summary（未分類，需補命名）
- StairGroup Summary（群組數、multi-story 群組數、single-run 群組數）
- Violation List（依 Rule ID 分組）
- Width Evidence Summary（G-ST-001 實測淨寬證據與資料來源分佈）
- Annotation Summary（已標尺寸數量、已標文字註記數量、已標顏色覆寫數量、缺視圖數量、Annotation-Tool-Gap 數量）
- Manual Checklist（扶手形式、異質地磚、平台轉折等）

---

## 第 33 條門檻對照（一般樓梯）

| 用途類別 | 最小淨寬 | 級高上限 | 級深下限 |
|---|---:|---:|---:|
| 小學校舍等供兒童使用 | 1.40m | 16cm | 26cm |
| 學校、醫院、戲院、商場等 | 1.40m | 18cm | 26cm |
| 地上各層居室樓地板面積 > 200m2 或地下層居室面積合計 > 200m2 | 1.20m | 20cm | 24cm |
| 其他建築物 | 0.75m | 20cm | 21cm |

> 注意：類組判斷影響門檻，務必由使用者先確認建築用途，AI 不可自行假設。
> 重大修正：住宅用途不得直接套用 1.40m 類別。住宅應先依「居室」法規定義判定面積是否觸發 > 200m2 條件；觸發時套用 1.20m/20cm/24cm，未觸發時套用 0.75m/20cm/21cm。
> 補充：混合用途的「最嚴格」可作內控加嚴，但不可替代第 33 條法規基準判定。

---

## 工具映射與替代路徑建議

| 目的 | MCP 專用路徑 (推薦) | 替代路徑 (若不具備專用工具) |
|---|---|---|
| 確認元件品類 | `get_active_schema` | 直接嘗試 `query_elements` 若失敗則報錯。 |
| 建立檢查視圖 | `create_stair_section_view` | 使用者手冊：請手動建立剖面並命名為「樓梯檢核剖面」。 |
| 判定實測淨寬 | `get_stair_actual_width` | 讀取 `ActualRunWidth` 參數，若無效則用 `measure_distance`。 |
| 檢核垂直淨高 | `check_stair_headroom` | 使用 `measure_distance` 測量「起點、中點、終點」高度。 |
| 產生圖面標註 | `create_dimension` + `create_text_note` | 產生 CSV/Excel 報告，由人工回 Revit 手工標註。 |
| 高亮違規範圍 | `create_filled_region` | 使用 `override_element_graphics` 將違規樓梯變紅色。 |
| 法規條文標示 | `create_text_note` | 將條文 ID (如 G-ST-001) 寫入樓梯的「註釋」欄位。 |
| 手動證據留存 | `measure_distance` | 截圖當前畫面，並在對話中記錄數值。 |

待擴充工具：
- query_stair_effective_width（輸入 Stair ElementId，直接回傳最小有效寬度與證據）
- normalize_parameter_units（回傳 raw/internal/project 單位與 mm 統一值）

---

## 淨高檢核擴充註記（待實作）

目標：將 G-ST-005 / A-ST-005 從「點位量測」擴充為「幾何碰撞檢核」。

提案流程（概念）：
1. 提取踏階幾何面，過濾法線約為 +Z 的可行走面。
2. 將可行走面沿 +Z 偏移 `requiredHeadroomMm`（預設 1900mm，可由專案指定）。
3. 對偏移後幾何執行碰撞/干涉檢查。
4. 若發生碰撞，判定淨高不足；輸出最小淨高與碰撞元素。

目前 MCP 限制（截至本版本）：
1. 無通用工具可直接提取元素面（含法線）。
2. 無通用工具可直接進行 3D 幾何碰撞/干涉檢查。
3. `measure_distance` 僅能做兩點距離，不足以覆蓋整段樓梯淨高碰撞判定。

建議後續工具擴充：
1. `extract_element_faces`（可依法線方向過濾）。
2. `check_vertical_clearance_collision`（輸入偏移高度，回傳碰撞與最小淨高）。
3. 或整合為 `check_stair_headroom` 專用命令。

---

## 輸出範本

```
┌──────────────────────────────────────────────────┐
│                樓梯法規整合檢查報告              │
├──────────────────────────────────────────────────┤
│ 檢查日期：YYYY-MM-DD                             │
│ 專案名稱：XXXX                                    │
│ 總樓梯數：XX                                      │
│ General：Pass XX / Fail XX / Manual XX           │
│ Accessible：Pass XX / Fail XX / Manual XX        │
│ Unclassified：XX                                  │
├──────────────────────────────────────────────────┤
│ Fail 清單（節錄）                                  │
│ - Stair 12345：G-ST-005 淨高 186cm < 190cm       │
│ - Stair 12360：A-ST-004 2R+T = 68cm > 65cm       │
├──────────────────────────────────────────────────┤
│ Manual 清單（節錄）                                │
│ - Stair 12377：A-ST-007 扶手端部形式需人工確認    │
│ - Stair 12401：A-ST-008 異質地磚配置需人工確認    │
└──────────────────────────────────────────────────┘
```

---

## 測試建議

1. 先於單一樓層測試，確認房間命名分流是否正確。
2. 再全棟執行，觀察 Unclassified 數量是否過高。
3. 每次重跑前，依本次 markerMode 清理舊標示（尺寸、文字或顏色覆寫），避免圖面誤判。
4. 抽樣比對 3~5 座樓梯，確認 Auto 判定與人工判讀一致。

---

## 擴充接口（保留）

本流程可直接擴充至其他無障礙項目：
- 坡道（Slope、Landing、Surface）
- 電梯（Cabin Size、Door Width、Button Height）
- 無障礙通路（Width、Turning Space）

擴充原則：維持同一套 Rule ID、分流、分級與報告格式，不改主流程。

---

## 最後需求工具（未來進化：關聯查詢）

若執行環境已啟用 `get_stair_actual_width`，可完成 Stair -> Runs 關聯查詢；後續建議優先補齊以下進化能力：
1. 回傳每個 Run 的原始值與單位資訊（raw 值、內部單位、專案顯示值、mm）。
2. 一鍵回傳「該樓梯最小有效寬度」與取值明細（runId、寬度、採用原因）。
3. 批次查詢模式（一次輸入多個 Stair ElementId）。
4. 回傳可追溯證據欄位（ViewId、量測位置、時間戳）供報告引用。
5. 當關聯不完整時，自動標記 `Manual-Review`，禁止輸出自動 Pass。
