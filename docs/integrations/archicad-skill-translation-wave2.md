# Archicad Skill 轉譯 Wave 2 實作前審核清單

> 狀態：Draft for review
> 建立日期：2026-07-22
> 目前分支：`agent/archicad-portability-pilots`（user fork）
> 上游討論：[`shuotao/REVIT_MCP_study#98`](https://github.com/shuotao/REVIT_MCP_study/issues/98)
> 本文件只定義下一批轉譯範圍；尚未授權或完成 Skill、Domain、MCP runtime 實作。

## 0. 2026-08-19 實測重評（本節後補，原始內容未刪改）

本文件建立於 2026-07-22，當時的能力判斷基於 `tapir-archicad-mcp==0.4.3`。
2026-08-19 以 `0.5.3` + Archicad 28 + Tapir Add-On 1.5.8 實機重測後，
下列判斷需要修正。原始章節保留未動，以便對照。

| 原判斷 | 重測結果 |
|---|---|
| `wall-orientation-check` 只能走 capability-gap 人工檢查 | **前提不成立，建議升為 pilot**，見下方 (a) |
| `detect-clashes` 縮限為 GUID pairs + 兩個 boolean | **維持**，schema 已確認，見下方 (b) |
| Hotlink 不納入第一版 | **維持**，且已取得反面證據，見下方 (c) |
| `element-coloring` 走暫時 Highlight | **維持**，但新增單位相關風險，見下方 (d) |

### (a) `wall-orientation-check`：第 6.4 節的兩項「缺少的證據」其實都存在

1. **`Wall.Flipped` 等價值存在。** Archicad 的 `GetDetailsOfElements` 回應中確實含有
   `flipped` 與 `referenceLineLocation`。目前讀不到的原因是 MCP wrapper 的回應模型過期，
   把新欄位當成不允許的多餘欄位丟棄（上游
   [SzamosiMate/tapir-archicad-MCP#24](https://github.com/SzamosiMate/tapir-archicad-MCP/issues/24)，
   `0.4.3` 與 `0.5.3` 皆失敗）。這是 wrapper 缺陷，不是 Archicad 的 API 缺口。

2. **Revit Room-side 判斷有對應的 command chain。** `elements_get_relations_of_elements`
   對 Zone 回傳 `zoneRelations`，內含 `wallParts`：

   ```text
   {elementId, roomEdgeIndex, begDistance, endDistance}
   ```

   即「這面牆的哪一段構成這個 Zone 的第幾條邊」，等同 Revit 的 room boundary segment。
   「某牆某段的一側是否有 Zone」因此可直接判定，不需要 ray test，也不需要
   目前壞掉的 `GetDetailsOfElements`。

   實測：本專案目前樓層 7 個 Zone，其中 6 個回傳完整關係（含 Wall／Beam／Column／Door／
   Window 分組），1 個回傳空關係。**空結果必須與「確實無邊界」區分後回報，不可省略。**

   建議改寫方向：Archicad route 改走 Zone 關係路線，而非原規劃的 WallDetails 幾何路線；
   驗收條件仍維持「不自動宣稱 correct／incorrect」，但可從人工檢查表升級為
   有證據支撐的候選判定。

### (b) `detect-clashes`：schema 已確認，補一個原文未載明的細節

`elements_get_collisions` 的 `settings` 可以整個省略（預設 null），
但**一旦給定，`volumeTolerance`、`performSurfaceCheck`、`surfaceTolerance` 三者皆為必填**。
預設值分別為 `0.001`、`false`、`0.001`。其餘輸入與結果欄位與第 5.4 節記載一致。

### (c) Hotlink：維持排除，且現在有反面證據

`project_get_hotlinks` 只回傳 `location` 字串陣列，沒有模組名稱，也沒有任何
GUID ownership 資訊，無法判斷哪些元素屬於哪個 hotlink。第 5.7 節要求
「live test 同時證明 GUID ownership、project context 與 collision schema 可用」
才可納入 —— 其中 GUID ownership 這項仍不成立，因此排除的判斷維持。

附帶提醒：本次測試用的專案本身含 13 個 hotlink 節點，因此任何元素計數都必須
聲明是否包含 hotlink 內容。未過濾的 `Wall` 列舉中，約位移 570 之後 GUID 格式明顯不同，
研判即為 hotlink 模組內的元素。

### (d) `element-coloring`：新增一項單位相關風險

第 4.5 節第 5 步要求建立「Property value → GUID count」的分組表。
Archicad 的 property 值是**依專案計算單位格式化的顯示字串**，可能夾帶單位符號
（實測回傳 `"90°"`），小數位數也來自專案設定而非數值本身。

因此以 property 值當分組鍵有兩個陷阱：

- 兩個實際不同的數值可能格式化後相同而被併成同一組（例如 2 位小數下的 18.955 與 18.964）
- 專案計算單位一改，分組鍵全部改變

數值型 property 分組前必須先呼叫 `project_get_calculation_units` 並在報告中聲明單位基準。
詳見 `.claude/skills/archicad-skill-adapter/references/revit-archicad-terminology.md`
的「Units and the numeric contract」一節。

另外，property 的可用性由 classification 決定，所以「空值」是有歧義的：
可能是沒有值，也可能是該元素根本沒有這個 property。第 4.3 節第 3 點
「不把未查到的元素或值編入結果」在 Archicad 需要進一步區分這兩種情況。

## 1. 決策摘要

Wave 2 建議處理三個既有 canonical Skills：

| 優先序 | Skill | 建議狀態 | 本輪目標 | 主要風險 |
|---:|---|---|---|---|
| 1 | `element-coloring` | 正式 Pilot | 參數分組後進行可清除的 Archicad Highlight | Archicad Highlight 不等於 Revit View Graphic Override |
| 2 | `detect-clashes` | 縮限版 Pilot | 以兩組 GUID 做 body／clearance collision，產生可追溯結果 | Archicad 結果沒有 Revit Curve-to-Solid 的穿透幾何細節 |
| 3 | `wall-orientation-check` | Capability-gap route | 列出牆軸線與候選法線供人工確認 | 目前 schema 沒有可驗證的 Archicad Exterior Side |

建議核准邊界：

1. `element-coloring` 可以升級為 Archicad pilot，但 Archicad route 必須稱為「暫時 Highlight」。
2. `detect-clashes` 可以升級為縮限版 pilot，但不得宣稱取得穿透深度、入口／出口或體積。
3. `wall-orientation-check` 只加入能力缺口與人工檢查流程，不宣稱自動判定正確／錯誤。
4. 本輪只修改 BIM_MCP 的知識與 orchestration 文件，不修改 Revit 或 Archicad MCP runtime。

## 2. 現況基線

目前已完成的第一批 Archicad pilots：

- `element-query`
- `room-numbering`
- `quantity-takeoff-excel`

| 項目 | 現況 |
|---|---|
| Canonical Skills | 52，source of truth 為 `.claude/skills/*/SKILL.md` |
| Revit project config | `.mcp.json`、`.vscode/mcp.json` 預設維持 Revit-only |
| Archicad MCP | opt-in，固定 `tapir-archicad-mcp==0.4.3` |
| Revit identifier | `ElementId` |
| Archicad identifier | Element GUID |
| Archicad instance | 每個 operation chain 必須重新選定 live project／port |
| Archicad command | 必須先 discovery，再依當前 schema dispatch |

本輪不處理：

- `batch-room-height`：涉及寫入、transaction 與 rollback 邊界。
- `parking-check`：淨高幾何與專案 Property schema 尚未完成驗證。
- Layout、Drawing、View Template 類 Skill。
- 修改或 fork `tapir-archicad-mcp` runtime。
- 修改 Revit C#／TypeScript tool implementation。

## 3. 共通 Backend Routing

三個 Skill 都必須使用相同 routing 規則：

1. 使用者明確指定 Revit：走既有 Revit Skill 與 Domain，不載入 Archicad port、GUID 或 dynamic command。
2. 使用者明確指定 Archicad：讀取 `archicad-skill-adapter` 與對應 Wave 2 reference。
3. 兩個 backend 都可用但目標不明：先詢問使用者，不得自行選擇。
4. 每條 operation chain 只使用一個 backend。
5. Revit `ElementId` 與 Archicad GUID 不得轉型、比較或混入同一 payload。
6. 視覺變更後必須有可逆驗證；找不到驗證方式就停止。

### 3.1 Archicad 公開 MCP 工具

Skill 只能直接依賴三個公開工具：

| 公開 MCP 工具 | 用途 |
|---|---|
| `discovery_list_active_archicads` | 列出 live Archicad instances，選定 project 與 port |
| `archicad_discover_tools` | 以 application-neutral intent 搜尋當前 command 與 schema |
| `archicad_call_tool` | 依 discovery 回傳的名稱與 arguments 執行 command |

本文件中的 `elements_*`、`properties_*` 是 pinned runtime 目前已確認存在的 discovery hints，不是 Skill 可永久寫死的 API contract。

### 3.2 共通 trace contract

每次 live test 必須留下：

```text
backend: revit | archicad
canonical_skill: <skill name>
domain_method: <domain path>
adapter_reference: <Wave 2 reference>
project_port: <Archicad current-turn port>
discovered_commands: <names returned by discovery>
identifier_type: ElementId | GUID
input_count: <count>
output_count: <count>
verification: <read-back / cleared highlight / result reconciliation>
unsupported_steps: <explicit list>
warnings: <explicit list>
```

只證明 MCP command 成功不足以證明 Skill／Domain 已參與。Agent 必須指出 canonical Skill、Domain 與 adapter reference。

## 4. Skill 1：`element-coloring`

### 4.1 現有檔案

| 類型 | 路徑 |
|---|---|
| Canonical Skill | `.claude/skills/element-coloring/SKILL.md` |
| Authoritative Domain | `domain/element-coloring-workflow.md` |
| Related Domain | `domain/tool-capability-boundary.md` |
| Lessons | `domain/lessons.md` L-027 |
| Agy／Codex mirror | 目前不存在，Wave 2 預計新增 |

### 4.2 原本 Revit 功能與設定

原始目的：依 Revit 元素參數值分組，在指定 View 套用顏色覆寫，以協助檢查防火、法規、分類或模型資料。

前置設定：

1. 確認目標 Revit View。
2. 平面 View 使用 cut graphics；立面、剖面或 3D 視情況使用 surface graphics。
3. 使用工具取得參數的實際本地化名稱，不猜測參數名稱。
4. 先取得參數值分布，再與使用者確認分組與色碼。
5. 視覺化操作只影響目標 View，並可用 clear operation 復原。
6. 牆體上色可能先暫時取消接合，完成後恢復接合。

原本 Revit MCP tools：

| Revit tool | 原始功能 | Revit-only 資料 |
|---|---|---|
| `get_category_fields` | 取得 Category 的實際欄位名稱 | Revit Category、localized parameter name |
| `get_field_values` | 取得欄位值分布 | Revit parameter values |
| `query_elements_with_filter` | 依欄位與 operator 篩選元素 | ElementId、Category、Revit filter semantics |
| `override_element_graphics` | 在指定 View 套用 fill、line、transparency | ViewId、cut／surface pattern、ElementId |
| `clear_element_override` | 清除指定 View 的元素覆寫 | ViewId、ElementId |
| `unjoin_wall_joins` | 暫時取消牆接合 | Revit wall join graph |
| `rejoin_wall_joins` | 恢復牆接合 | Revit wall join graph |

### 4.3 預計保留的 Domain 方法

1. 先確認分類欄位與值分布。
2. 使用者先確認分組與色碼。
3. 不把未查到的元素或值編入結果。
4. Wall-anchored 與 Room／Zone-anchored 檢查分開處理。
5. 視覺變更必須可逆。
6. 回報每組元素數量、未分類值與清除方式。

### 4.4 Archicad command mapping

| Application-neutral intent | Discovery hint | 預計用途 |
|---|---|---|
| 依 element type 列出元素 | `elements_get_elements_by_type` | 以 Archicad element type 取代 Revit Category |
| 找出元素可用 Property IDs | `properties_get_all_property_ids_of_elements` | 確認實際可讀 Property |
| 讀取 Property values | `properties_get_property_values_of_elements` | 建立值分布與 GUID 分組 |
| 依條件篩選元素 | `elements_filter_elements` | schema 可表達條件時進行 server-side filter |
| 暫時標示 GUID | `elements_highlight_elements` | 以 RGBA Highlight 元素 |

目前已確認 `elements_highlight_elements` schema：

```text
elements: GUID list
highlightedColors: RGBA color list
wireframe3D: optional boolean
nonHighlightedColor: optional RGBA
```

以空的 `elements` array 呼叫可清除既有 Highlight。

### 4.5 Archicad 轉譯流程

1. 選定 live project／port。
2. 讀取使用者指定的 element type、Property intent 與色碼需求。
3. Discovery：列出 element type。
4. Discovery：取得實際 Property ID 與 values。
5. 建立 dry-run 分組表：Property value、GUID count、RGBA、未分類數量。
6. 使用者確認後，以相同 port 呼叫 Highlight command。
7. 回報被 Highlight 的 GUID count 與未處理元素。
8. 驗證 clear path；使用者要求復原時以空元素陣列清除。

### 4.6 不可直接轉譯的 Revit 行為

| Revit 行為 | Archicad Wave 2 處理 |
|---|---|
| View-specific Graphic Override | 改稱暫時 Highlight，不宣稱保存於特定 View |
| Cut／Surface Pattern | 不轉譯 |
| `unjoin_wall_joins`／`rejoin_wall_joins` | 不執行，列入 unsupported steps |
| Revit Room hosting-wall proxy | 重新發現 Zone 關係能力，不重用 Room ElementId 邏輯 |
| Revit parameter display name | 轉為 discovery 得到的 Property ID／name |

### 4.7 Stop conditions

- 同一個使用者欄位對應多個可能的 Archicad Properties。
- RGBA 數量與 GUID 分組數不一致。
- 找不到清除 Highlight 的可驗證路徑。
- 選定 port 在 dry-run 與 Highlight 之間改變。
- 使用者要求永久 Graphic Override，但 runtime 只提供 Highlight。

### 4.8 Live-test cases

| Case | 輸入 | 預期結果 |
|---|---|---|
| EC-01 | 依 Wall 的一個 Property 值分色 | 回報 Skill／Domain／port／Property ID／各組 GUID count |
| EC-02 | 包含空值 Property | 空值成為獨立分組或 warnings，不可省略 |
| EC-03 | 清除 Highlight | 呼叫空元素陣列，回報清除成功或失敗 |
| EC-04 | Revit／Archicad 都連接但未指定 | 先詢問 backend，不執行模型工具 |
| EC-05 | 使用者要求 Revit View Override | 保持既有 Revit route |

### 4.9 Pilot acceptance

- Revit route 的工具、ViewId、ElementId 與 cut／surface 邏輯原樣保留。
- Archicad route 使用 GUID 與 current-turn port。
- 回覆明確使用「Highlight」，不使用「Graphic Override」假裝等價。
- Highlight 有可驗證的 clear path。
- 不執行牆接合修改。

## 5. Skill 2：`detect-clashes`

### 5.1 現有檔案

| 類型 | 路徑 |
|---|---|
| Canonical Skill | `.claude/skills/detect-clashes/SKILL.md` |
| Authoritative Domain | `domain/mep-csa-clash-detection.md` |
| Agy／Codex mirror | 目前不存在，Wave 2 預計新增 |

### 5.2 原本 Revit 功能與設定

原始目的：在 CSA 主模型與 MEP 連結模型之間，以 Curve-to-Solid 方法找出穿越／碰撞，分析風險並匯出報表。

```text
Current Revit document = CSA host
Revit LinkInstance = MEP source
MEP curve = centerline
CSA element = Solid
collision = curve segment passing through solid
```

原本 Revit MCP tools：

| Revit tool | 原始功能 | 重要輸出 |
|---|---|---|
| `get_linked_models` | 列出 Revit Links 與 Transform | LinkInstanceId、path、origin |
| `query_linked_elements` | 查詢 Link 中的 MEP elements | ElementId、system、size、transformed coordinates |
| `get_active_schema` | 盤點 CSA host categories | Walls、Floors、Framing、Columns count |
| `get_element_geometry` | 取得 centerline、BoundingBox、Solid | Revit geometry evidence |
| `detect_clashes` | Curve-to-Solid collision | entrance、exit、depth、direction、area、volume |
| `colorize_clashes` | 對 CSA 元素做 View graphics | ElementId、ViewId、color scheme |
| `export_clash_report` | 匯出 CSV／JSON | Revit-specific report columns |
| `select_element` | 選取元素 | ElementId |
| `zoom_to_element` | 導覽至元素 | ElementId、active view |

原本互動設定：

- MEP categories：Pipes／Ducts／CableTrays。
- MEP system、size threshold、level scope。
- CSA categories：Walls／Floors／StructuralFraming／StructuralColumns。
- BoundingBox coarse filter、`maxResults` 上限。
- 穿透深度大於 500 mm 的嚴重度提示。

### 5.3 預計保留的 Domain 方法

1. 環境偵察。
2. 使用者界定兩組待檢元素。
3. 執行碰撞判斷。
4. 統計與風險分析。
5. 視覺化與可稽核報告。

演算法、資料欄位與可以宣稱的精度必須依 backend 分開。

### 5.4 Archicad command mapping

| Application-neutral intent | Discovery hint | 預計用途 |
|---|---|---|
| 依 element type 建立群組 | `elements_get_elements_by_type` | 建立 group 1／group 2 GUID lists |
| 依 Classification 建立群組 | `elements_get_elements_by_classification` | 使用專案 Classification 分組 |
| 取得 element type | `elements_get_types_of_elements` | 報表加入實際類型 |
| 讀取 system／size Properties | `properties_get_property_values_of_elements` | 依專案 Property 過濾與報告 |
| 執行 collision | `elements_get_collisions` | 檢查 body／clearance collision |
| 取得元素 detail | `elements_get_details_of_elements` | 補充 element-specific evidence |
| 取得 3D BoundingBox | `elements_get3_d_bounding_boxes` | 補充範圍與人工檢視資訊 |
| 暫時標示碰撞元素 | `elements_highlight_elements` | 可逆 Highlight |

目前已確認 `elements_get_collisions` input：

```text
elementsGroup1: GUID list
elementsGroup2: GUID list
settings:
  volumeTolerance
  performSurfaceCheck
  surfaceTolerance
```

目前已確認 result：

```text
collisions[]:
  elementId1
  elementId2
  hasBodyCollision
  hasClearenceCollision
```

### 5.5 Archicad 縮限版流程

1. 選定 live project／port。
2. 確認 group 1 與 group 2 使用的 element type／Classification／Property。
3. Discovery：列出兩組 GUID，保留每個 GUID 的來源條件。
4. 顯示 dry-run：兩組數量、類型分布、Property 篩選與 collision settings。
5. 呼叫 collision command。
6. Reconcile：每筆 collision GUID 必須存在於本 turn 的輸入群組。
7. 依 `hasBodyCollision`、`hasClearenceCollision` 統計。
8. 可選擇 Highlight 結果；提供 clear path。
9. 產生結構化表格；client 具檔案能力時才輸出 CSV／JSON。

### 5.6 Archicad 報表契約

第一版只允許：

```text
Sequence
Group1Guid
Group1ElementType
Group1PropertyEvidence
Group2Guid
Group2ElementType
Group2PropertyEvidence
HasBodyCollision
HasClearanceCollision
ProjectPort
Warnings
```

不得推論：

- 穿透入口／出口座標
- 貫穿深度
- 方向向量
- 截面積
- 佔用體積
- Revit LinkInstanceId／Transform

### 5.7 Hotlink 邊界

Wave 2 不預設 Archicad Hotlink 元素一定能被列出或碰撞。只有 live test 同時證明 GUID ownership、project context 與 collision schema 可用，才可納入後續版本。第一版必須把 Hotlink 列入 `unsupported_steps`。

### 5.8 Stop conditions

- 兩組篩選條件無法唯一轉成 element type、Classification 或 Property。
- result GUID 不屬於本 turn 兩組輸入。
- 使用者要求 Revit-specific 穿透幾何欄位。
- 使用者要求跨 Hotlink collision，但 ownership／座標未驗證。
- collision 數量超出使用者核准上限。
- port 在分組與 collision 之間改變。

### 5.9 Live-test cases

| Case | 輸入 | 預期結果 |
|---|---|---|
| DC-01 | 同一 project 的兩組小型元素 | 回傳 GUID pairs 與 body／clearance flags |
| DC-02 | 沒有碰撞的兩組元素 | 回傳 0 筆，不捏造幾何 |
| DC-03 | 要求穿透深度 | 回報 runtime 未提供，不以 BoundingBox 推測 |
| DC-04 | Highlight results | 只 Highlight 本 turn GUID，並可清除 |
| DC-05 | Hotlink MEP vs host structure | 第一版停止並回報未驗證邊界 |
| DC-06 | Revit backend | 原本 Curve-to-Solid workflow 完整保留 |

### 5.10 Pilot acceptance

- 不把 Archicad collision 描述成 Revit Curve-to-Solid。
- 結果只使用已回傳的 GUID 與 booleans。
- 兩組輸入與 result GUID 可 reconciliation。
- 報表不包含推測幾何。
- Revit Link／Transform／深度與原報表功能保持原路徑。

## 6. Skill 3：`wall-orientation-check`

### 6.1 現有檔案

| 類型 | 路徑 |
|---|---|
| Canonical Skill | `.claude/skills/wall-orientation-check/SKILL.md` |
| Authoritative Domain | `domain/wall-check.md` |
| Related Domain | `domain/lessons.md` L-027 |
| Agy／Codex mirror | 目前不存在，Wave 2 預計新增 |

### 6.2 原本 Revit 功能與設定

原始目的：找出外牆並判斷 Exterior／Interior side 是否正確，避免飾面、邊界與容積計算錯誤。

| Revit tool | 原始功能 | 關鍵 Revit 證據 |
|---|---|---|
| `query_elements_with_filter` | 依 Wall Function 找出內／外牆 | Category、Function、ElementId |
| `get_wall_info` | 取得牆詳細資訊 | `Wall.Flipped`、`Wall.Orientation.X/Y`、起終點 |
| `override_element_graphics` | 依狀態上色 | ViewId、ElementId |

Domain 還包含 Wall Type name、外皮接觸、exterior-side ray test、Room-side 檢查及建築外輪廓。

### 6.3 Archicad 已確認可讀資料

`elements_get_details_of_elements` 的 `WallDetails` 已確認包含：

```text
geometryType
begCoordinate
endCoordinate
zCoordinate
height
bottomOffset
offset
arcAngle
begThickness
endThickness
polygonOutline
polygonArcs
structureType
buildingMaterialId
compositeId
profileId
```

可以安全完成：

- 取得牆 GUID。
- 取得直牆 reference line 起點／終點。
- 計算軸線方向與兩個相反的候選法線。
- 依 Property／Classification 找出外牆候選。
- Highlight 需要人工確認的牆。

### 6.4 目前缺少的直接證據

- Revit `Wall.Flipped` 等價值。
- 明確指出 Exterior Side 的 normal。
- 能將兩個候選法線其中之一判定為 exterior 的穩定欄位。
- 已驗證的 building-envelope topology。
- 可直接重現 Revit Room-side ray test 的 command chain。

因此不能只用 `begCoordinate`、`endCoordinate` 與 `offset` 自動宣稱哪一側是 Exterior。

### 6.5 Wave 2 capability-gap route

1. 選定 live project／port。
2. Discovery：列出 Walls。
3. Discovery：取得 Classification／Property，辨識 exterior-wall candidates。
4. Discovery：讀取 WallDetails。
5. 對直牆計算 axis vector 與兩個候選 normals；曲牆／polygonal wall 分開標示。
6. 產生人工確認表，不輸出 `correct`／`incorrect` 自動判定。
7. 可選擇 Highlight 外牆候選或缺資料牆。
8. 若 discovery 找到直接、可驗證的 exterior-side Property，記錄為後續 pilot 候選。

```text
WallGuid
GeometryType
StartCoordinate
EndCoordinate
AxisVector
CandidateNormalA
CandidateNormalB
ExteriorClassificationEvidence
ManualDecision
Warnings
```

### 6.6 Stop conditions

- 使用者要求自動判定正確／錯誤，但沒有 exterior-side evidence。
- curved／polygonal wall 無法用直線 normal 表達。
- 外牆分類 Property 不明確。
- 建築有多個分離量體，不能以單一全域中心推定 exterior。
- 使用者要求自動 flip wall；Wave 2 不包含任何牆修改。

### 6.7 Live-test cases

| Case | 輸入 | 預期結果 |
|---|---|---|
| WO-01 | 直線外牆候選 | 列出軸線與兩個候選法線，不自動判定 exterior |
| WO-02 | curved wall | 標示 geometry type，不套用直牆演算法 |
| WO-03 | Property 明確標示 exterior | 保留 Property ID/value，但不假設 normal side |
| WO-04 | 要求自動 flip | 停止並回報不在 Wave 2 範圍 |
| WO-05 | Revit backend | 走既有 `Wall.Flipped`／`Wall.Orientation` route |

### 6.8 Capability-gap acceptance

- Skill 正確選擇 backend。
- Archicad route 不宣稱自動 orientation correctness。
- 人工表只包含 command 回傳或可重算向量。
- 曲牆與 polygonal wall 不套用直牆演算法。
- Revit route 的 `Flipped`、`OrientationX/Y` 與 View override 不變。

## 7. 預計 repository 變更

使用者核准後，建議建立新的 fork branch：

```text
agent/archicad-portability-wave2
```

### 7.1 預計修改

| 類別 | 路徑／內容 |
|---|---|
| Canonical Skills | `.claude/skills/{element-coloring,detect-clashes,wall-orientation-check}/SKILL.md` |
| Domains | `domain/element-coloring-workflow.md`、`domain/mep-csa-clash-detection.md`、`domain/wall-check.md` |
| Adapter references | `pilot-element-coloring.md`、`pilot-detect-clashes.md`、`gap-wall-orientation-check.md` |
| Agy／Codex mirrors | `.agents/skills/{element-coloring,detect-clashes,wall-orientation-check}/` |
| Integration docs | 本文件、`archicad-skill-portability.md`、`archicad-mcp.md` |
| Governance | `CLAUDE.md`、`CHANGELOG.md`、`docs/DOCUMENT_AUDIENCE_INVENTORY.md`、`log/2026-07.md` |

更新 `detect-clashes` 時會順便修正既有 frontmatter，使 canonical Skill 只保留合法欄位；不改變 Revit tool implementation。

### 7.2 明確禁止修改

- `.mcp.json`
- `.vscode/mcp.json`
- `MCP/`
- `MCP-Server/`
- `scripts/setup.ps1`
- `scripts/install-addon.ps1`
- Revit port `8964`
- Revit C# dispatcher
- Revit TypeScript tool registry
- `Archicad_mcp_Tapir` repository source

若 live test 證明 pinned runtime 缺少必要能力，先記錄 capability gap。任何 runtime 開發必須另開分支、Issue 與使用者授權。

## 8. Revit 非影響驗證

1. `.mcp.json` 與 `.vscode/mcp.json` 與 implementation 前 byte-identical。
2. `MCP/` 與 `MCP-Server/` 沒有 diff。
3. Canonical Skill 數仍為 52。
4. Revit target 不讀取 Archicad port 或 GUID。
5. 三個既有 Revit workflows 的工具名稱、輸入與驗證仍存在。
6. Agy mirrors 只 redirect canonical Skill。
7. 修改的 Skills 通過 `skill-creator` validator。
8. Markdown links、YAML、UTF-8、`git diff --check` 通過。
9. Windows／PowerShell 可用時執行：

```powershell
.\scripts\verify-qaqc.ps1 -SkipBuild -SkipDeploy
```

## 9. 建議實作順序

1. 建立 `agent/archicad-portability-wave2`。
2. 先做 `element-coloring`，完成 Highlight／clear live test。
3. 再做 `detect-clashes`，以小型同專案 GUID groups 驗證。
4. 最後加入 `wall-orientation-check` capability-gap route。
5. 三個 Skill 分開 commit，方便 upstream 拆 PR 或 cherry-pick。
6. 完成 Revit regression 後才 push fork。
7. 先在 Issue #98 更新 evidence，不直接建立 upstream PR。

## 10. 審核勾選清單

- [ ] 同意 `element-coloring` 在 Archicad 只提供暫時 Highlight，不是永久 Graphic Override。
- [ ] 同意 Archicad route 不執行牆 join／unjoin。
- [ ] 同意 `detect-clashes` 第一版只回傳 GUID pairs 與 body／clearance flags。
- [ ] 同意第一版不支援 Hotlink collision。
- [ ] 同意不推測穿透深度、入口／出口、截面積或體積。
- [ ] 同意 `wall-orientation-check` 第一版只提供人工檢查資料，不自動判定正確／錯誤。
- [ ] 同意本輪不修改任何 MCP runtime source。
- [ ] 同意 Revit workflows、configs、port、build 與 deploy 維持原樣。

建議核准文字：

```text
同意 Wave 2 清單：
1. element-coloring 做暫時 Highlight pilot；
2. detect-clashes 做縮限版 GUID collision pilot，不含 Hotlink 與穿透幾何；
3. wall-orientation-check 只做 capability-gap／人工檢查 route；
4. 不修改 Revit 或 Archicad MCP runtime。
```

## 11. 從另一台電腦取得本文件

若已 clone user fork：

```bash
git fetch origin
git switch agent/archicad-portability-pilots
git pull --ff-only origin agent/archicad-portability-pilots
```

若尚未 clone：

```bash
git clone https://github.com/Archwiz-boss/BIM_MCP_study.git
cd BIM_MCP_study
git switch agent/archicad-portability-pilots
```

文件位置：

```text
docs/integrations/archicad-skill-translation-wave2.md
```
