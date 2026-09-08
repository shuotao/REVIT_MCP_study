---
name: archicad-structural-dwg-sync
description: "結構 DWG/DXF 進入 Archicad 後的逐層校準、柱梁板比對、Favorite 建模、Property 標記與人工覆核 SOP；適用於設計協調，不取代結構專業判定。"
metadata:
  version: "1.2"
  updated: "2026-09-08"
  created: "2026-09-08"
  references:
    - ".claude/skills/archicad-skill-adapter/references/archicad-runtime-facts.md"
    - ".claude/skills/archicad-skill-adapter/references/revit-archicad-terminology.md"
    - "docs/integrations/archicad-skill-portability.md"
    - "https://github.com/SzamosiMate/tapir-archicad-MCP"
    - "https://github.com/SzamosiMate/multiconn_archicad"
    - "https://github.com/SzamosiMate/tapir-archicad-MCP/issues/24"
  related:
    - archicad-adapter-boundary.md
    - archicad-terminology-map.md
    - archicad-portability-matrix.md
    - dwg-column-import.md
    - dwg-beam-import.md
    - ifc-structural-native-sync.md
  referenced_by: []  # 尚無任何 SKILL.md 引用；待 archicad-skill-adapter 接線
  tags: [archicad, dwg, dxf, structural, property, column, beam, slab, compare]
---

# Archicad 結構 DWG/DXF 逐層同步與檢核

## Purpose

本文件定義兩條受控流程：新建模型時由結構 CAD 輔助柱放樣，以及新版結構 CAD 與既有 Archicad 柱、梁、板逐層比對。預設只產生 dry-run 報表與覆核標記，不自動大規模修改模型幾何。

## 權威正本與衝突規則

本文件只負責「結構 DWG/DXF 逐層同步」的方法。下列既有文件是權威正本：

- 執行期、工具版本與已知故障：`.claude/skills/archicad-skill-adapter/references/archicad-runtime-facts.md`
- Revit／Archicad 名詞與單位：`.claude/skills/archicad-skill-adapter/references/revit-archicad-terminology.md`
- Archicad 可攜性分級：`docs/integrations/archicad-skill-portability.md`

內容衝突時以上述正本為準。本檔 `referenced_by: []` 是刻意保留的現況：目前沒有任何 `SKILL.md` 真正引用此檔，接線尚待後續 PR。

## 1. 驗證等級與證據

本文件只使用三種驗收用語：`verified`、`approximate mapping`、`unsupported`。其中 `verified` 只涵蓋表中明載的版本、日期與測試範圍，不可外推到其他 Archicad、Add-On、MCP wrapper 或專案狀態。

| ID | 能力／限制 | 等級 | 日期、版本與證據型態 | 證據範圍 |
|---|---|---|---|---|
| E-01 | Tapir 啟動與 session 連線前提 | verified | 2026-09-08；Tapir MCP 0.5.3；沿用 runtime-facts 實測 | 啟動須加 `--with "mcp<2"`；同一 MCP session 先呼叫 `discovery_list_active_archicads`。 |
| E-02 | 工具參數與分頁 | verified | 2026-09-08；Tapir MCP 0.5.3；沿用 runtime-facts 實測 | `archicad_call_tool.arguments` 為 `{port, params:{...}, page_token?}`；page size 100。`properties_get_all_properties` 首頁 100 筆，實際 1766 筆／18 頁。能力盤點必須走完全部頁。 |
| E-03 | 讀取 View Map tree | verified | 2026-09-08；Archicad 28 build 3001、Tapir MCP 0.5.3；本次實機 | 成功讀取目前專案的 View Map tree。 |
| E-04 | 新增 View Map folder／建立或複製 Saved View | approximate mapping | 2026-09-08；Archicad 28 build 3001、Tapir MCP 0.5.3；本次完整指令清單與相關 schema 盤點 | 找到 `view_create_view_map_folder`、`view_clone_project_map_item_to_view_map`、`navigator_create_view_map_folder`、`navigator_create_views_in_view_map`；本次未執行持久化新增。 |
| E-05 | 「顯示所選」檢視鏈 | approximate mapping | 2026-09-08；Archicad 28 build 3001、Tapir MCP 0.5.3；本次實機 | selection、切換 3D、highlight、fit 均 verified；與使用者 F5 快捷鍵語意不是單一 1:1 指令。 |
| E-06 | 柱 Element ID 寫入與還原 | verified | 2026-09-08；Archicad 28 build 3001、Tapir MCP 0.5.3；本次實機 | 對一支可編輯 Column 的 `General_ElementID` 寫入暫時後綴、read-back，再還原 `CRE - 001` 並 read-back。未涵蓋 Beam。 |
| E-07 | 梁 Element ID 寫入 | approximate mapping | 2026-09-08；Archicad 28 build 3001、Tapir MCP 0.5.3；本次 schema 盤點 | 與 Column 共用 built-in Property 路徑，但本次專案沒有可編輯 Beam 樣本，未做寫入 pilot。 |
| E-08 | Column／Beam／Slab Favorite 清單 | verified | 2026-09-08；Archicad 28 build 3001、Tapir MCP 0.5.3；本次實機 | 三類 Favorite 均可列出；未驗證套用後建立構件的結果。 |
| E-09 | Favorite 套用至 defaults／elements | approximate mapping | 2026-09-08；Archicad 28 build 3001、Tapir MCP 0.5.3；本次完整指令清單與 schema 盤點 | 找到套用指令；需另做 create pilot 驗證幾何、樓層、分類與 Property。 |
| E-10 | 自訂 Classification、Property 與單一柱寫入 | verified | 2026-09-08；Archicad 28 build 3001、Tapir MCP 0.5.3；本次實機 | 建立 `Archwiz Structural Check`、三個結構分類、`Archwiz_Structural_Check/Check_Message`；一支可編輯柱寫入後 read-back 成功。 |
| E-11 | Property 批次寫入回傳可信度 | unsupported | 2026-09-08；Tapir MCP 0.5.3；沿用 runtime-facts 實測 | 工具層成功不代表元素成功；失敗可能只存在每筆 `executionResults`，且鎖定元素可能全部未寫入。 |
| E-12 | `elements_get_details_of_elements` | unsupported | 2026-09-08；Tapir MCP 0.5.3；沿用 runtime-facts 實測及上游 issue #24 | Archicad 28 回傳造成 522 個 validation errors，不得用於樓層歸屬。 |
| E-13 | 直接解析工作圖 linked DWG 的內部 2D primitive | approximate mapping | 2026-09-08；Archicad 28 build 3001、Tapir MCP 0.5.3；本次完整指令清單、完整分頁與 Drawing／2D／navigator 相關 schema 盤點 | 找到 Archicad element／Drawing 與 navigator 查詢，但未找到直接列舉 linked DWG 內部 LINE／LWPOLYLINE／TEXT／CAD layer 的指令；本次無真實 DWG pilot，因此不得寫成「無法」或 `unsupported`。 |
| E-14 | DXF parser／AutoCAD MCP fallback | approximate mapping | 2026-09-08；流程設計；本次未提供真實結構圖且未接 AutoCAD MCP | ezdxf 與 AutoCAD MCP 皆須另做 fixture／真實圖 pilot，不能視為已驗證輸入。 |
| E-15 | `classifications_get_classifications_of_elements` | unsupported | 2026-09-08；Archicad 28 build 3001、Tapir MCP 0.5.3；本次實機 | Archicad 回傳形狀與 wrapper schema 不符而觸發 response validation error；不得據此聲稱 classification read-back 成功。 |

### 執行期硬條件

依 E-01、E-02 與 E-12，每次 run 必須：

1. 以 `uvx --from tapir-archicad-mcp==0.5.3 --with "mcp<2" archicad-server` 啟動。
2. 在同一 session 先執行 `discovery_list_active_archicads`，再把回傳 port 放進後續呼叫。
3. 參數放在 `params`；不要因 pydantic 的 `port` 錯誤訊息而誤判 port 本身有問題。
4. 盤點指令或 Property 時走完所有 `page_token`；未走完分頁不得下能力不存在的結論。
5. 不呼叫已知壞掉的 `elements_get_details_of_elements`。以 `elements_get3_d_bounding_boxes.zMin` 對照 `project_get_stories` 推斷 Story，並在輸出標成 inference。
6. 若高負載後 discovery 退化成 `[]`，立即停止並回報「Add-On 狀態無法判定」；不得重試迴圈，也不得記成某一指令的能力缺口。

## 2. 輸入策略：先 Archicad，後外部解析

### 2.1 工作圖 linked DWG pilot

第一優先仍是使用者把一份 linked DWG 放進指定 Worksheet／View，再提供精確名稱。依 E-13，目前只能進行 pilot，不可保證 Tapir 能穿透 Drawing 取得 CAD primitive。

Pilot 必須記錄：

- Worksheet／View 完整名稱與 navigator item ID。
- linked Drawing 的檔名、檔案 SHA-256、版次、mtime 與可見圖層。
- `archicad_list_commands` 是否已走完、頁數與總筆數；所有相關 schema 的名稱與版本。
- 是否可讀到 LINE、LWPOLYLINE、ARC、INSERT、TEXT／MTEXT、block 與原始 CAD layer。

若只能取得 Drawing 容器或畫面 bounding box，狀態維持 `approximate mapping`，不得把畫面辨識結果冒充 CAD 幾何。

### 2.2 DXF／AutoCAD fallback

依 E-14：

1. 可取得 DXF 時，以 ezdxf 做 deterministic parser，保留 handle、layout、layer、entity type、block path、raw coordinates 與文字。
2. 只有 DWG、含 Xref／proxy object／複雜 block 或需要 AutoCAD 解譯時，才接 AutoCAD MCP。
3. 兩條路都不可用時，run 狀態是 blocked；不做螢幕截圖估線，也不建立或比對構件。

## 3. 單位、座標與逐層輸入契約

### 3.1 單位正規化

Archicad 幾何 API 以 SI 公尺表示；本流程的唯一比較單位是毫米。DWG／DXF 的 `$INSUNITS` 不一定可靠，因此轉換順序固定如下：

1. 讀 `$INSUNITS`，並與圖框已知尺寸或兩個控制點距離交叉驗證。
2. 明確寫入 `cad_to_mm_factor`；例如 CAD 單位為公尺時是 1000，毫米時是 1。未知時停止，不猜單位。
3. CAD raw coordinate 先乘 `cad_to_mm_factor`，再套旋轉與平移，得到 `cad_project_mm`。
4. Archicad API coordinate 乘 1000，得到 `model_project_mm`。
5. 所有 matching、delta、tolerance 與 snap 只在這兩組 project-mm 座標上運算。

### 3.2 專案基準點與控制點

每個 run 至少需要兩個不重合的控制點；若圖面可能旋轉，建議三個不共線控制點。紀錄 Project Origin／Survey Point／Geolocation 的採用方式、旋轉角、平移量及每點 residual。`alignment_residual_mm` 是轉換後控制點的最大絕對平面距離，超過門檻即停止。

### 3.3 一次只處理一層

不論 CAD 是整棟排版或單層檔，每個 run 只接受一個 `target_story`。整棟圖先以使用者確認的 crop／layout／標題邊界切成逐層來源；AI 不可只靠版面位置猜樓層。

```yaml
source_file: S-STRUCT-R05.dxf
source_hash: <sha256>
revision: R05
target_story: 3F
source_layout: Model
source_crop_raw: [xmin, ymin, xmax, ymax]
cad_units: millimeter
cad_to_mm_factor: 1.0
transform:
  rotation_deg: 0.0
  translate_mm: [0.0, 0.0]
control_points:
  - {cad_raw: [0, 0], archicad_mm: [0, 0], residual_mm: 0.0}
  - {cad_raw: [10000, 0], archicad_mm: [10000, 0], residual_mm: 0.0}
layers:
  column: [S-COL]
  beam: [S-BEAM]
  slab: [S-SLAB]
tolerance:
  alignment_residual_mm: 2.0
  match_radius_mm: 20.0
  position_change_mm: 5.0
  size_change_mm: 5.0
snap:
  enabled: true
  increment_mm: 5.0
```

## 4. 幾何正規化與 5 mm 定義

raw、project-mm、snapped 三組幾何必須同時保留。Snap 是建模候選值，不得覆寫 raw evidence。

- 柱位置差：`position_delta_mm = hypot(cad_project_x - model_x, cad_project_y - model_y)`。
- 梁位置差：分別比較兩端點最佳配對後的最大平面距離；方向反轉視為同一條梁。
- 尺寸差：矩形柱／梁使用 `size_delta_mm = max(abs(cad_b - model_b), abs(cad_h - model_h))`。
- 高程差另列 `level_delta_mm`，不得混入平面距離。
- `position_delta_mm > 5.0` 或 `size_delta_mm > 5.0` 才列為 changed；`<= 5.0` 列為 unchanged。這是轉換完成後、模型比對層的**絕對誤差**，不是相對百分比。
- `snap.increment_mm: 5.0` 表示候選建模值取到最接近 5 mm（0.5 cm）；它不是變更門檻。若專案要取整數公分，必須另由使用者核定 10 mm increment。

5 mm 是本 SOP 的初始 QA 門檻，不是結構專業允收公差。控制點殘差、CAD 繪製習慣、模型建構約定或契約要求不同時，必須由專案負責人另行核定並留痕。

## 5. 正規化物件與 matching

| 類型 | 最小資料 |
|---|---|
| Column | story、source handle／block、layer、中心點、旋轉角、b/h、raw 與 snapped 幾何 |
| Beam | story、source handle、layer、兩端點、寬／深、方向、raw 與 snapped 幾何 |
| Slab | story、source handle、layer、外框 polygon、holes、厚度／高程、raw 與 snapped 幾何 |

Matching 先以明確 source ID／既有對照表，再以 type、story、幾何及 `match_radius_mm`。多個候選或資料不足一律 `AMBIGUOUS`，不得自動選最近者。結果只有 `ADDED`、`REMOVED`、`CHANGED_POSITION`、`CHANGED_SIZE`、`CHANGED_LEVEL`、`UNCHANGED`、`AMBIGUOUS`。

## 6. 流程 A：新建模型的柱放樣

依 E-08 與 E-09，Favorite 列表是 `verified`，但套用與建模仍是 `approximate mapping`；須先做單層小樣：

1. 完成第 3 節輸入契約與控制點驗證。
2. 解析柱、保留 raw 值，產生 project-mm 與 5 mm snapped 候選值。
3. 依圖層／標註映射到使用者確認的 Column Favorite；未知映射停止。
4. 先輸出 dry-run：來源、Favorite、story、分類、位置、b/h、高程與差值。
5. 經人工核定後，只建立單層小批次；read-back 幾何、Story、Classification 與 Property。
6. 小樣通過後才可擴大批次。不得因 CAD 線看似整齊而直接全棟落柱。

## 7. 流程 B：新版圖與既有模型比對

1. 驗證 source hash、revision、單位、transform、控制點與唯一 `target_story`。
2. 取目標 Story 的 Column／Beam／Slab；因 E-12，樓層若由 bounding-box `zMin` 對 Story 高程推斷，報表必須標 `story_assignment=inferred_from_zmin`。
3. 執行 matching 與 delta 計算，先輸出 JSON／CSV dry-run，不修改幾何與 Element ID。
4. 人工確認 `AMBIGUOUS`、門檻及漏讀圖層。
5. 只對核定且可編輯的 changed 元素寫入檢核 Property。
6. 選取／3D／highlight／fit 作為人工複核入口；依 E-05，此鏈是 F5 的近似映射，不聲稱等同快捷鍵。

`elements_modify_columns` 與 `elements_modify_beams` 在 2026-09-08、Tapir 0.5.3 指令盤點中存在，但本流程未做幾何修改 pilot，狀態是 `approximate mapping`。使用前必須先讀當版 schema 並做小樣。沒有 `elements_modify_zones`；Zone 若被其他流程納入，只能走 Property 路徑，不能假設有幾何 modify 指令。

## 8. Classification、Property 與 Element ID

### 8.1 本次已建立的測試 schema

依 E-10，目前開啟專案已建立：

- Classification system：`Archwiz Structural Check`，version `1.0`
- Items：`STR-COLUMN / Structural Column`、`STR-BEAM / Structural Beam`、`STR-SLAB / Structural Slab`
- Property group：`Archwiz_Structural_Check`
- String Property：`Check_Message`，availability 限上述三類

單一可編輯柱的已驗證值：

```text
STRCHK|REV=DEMO-20260908|STORY=NA|STATUS=TEST|CHANGE=PROPERTY_WRITE|REVIEW=UNREVIEWED
```

這只證明該元素、該 session 的 Property 寫入與 read-back 成功。依 E-15，分類讀回能力是 `unsupported`，不得用工具層 success 推論已成功分類。

### 8.2 Property 寫入的三道關卡

依 E-11，每次寫入必須全部通過：

1. **Pre-filter**：先取完整目標 GUID 集合，再用 `elements_get_elements_by_type` 的 `filters: ["IsEditable"]` 取得可編輯集合；差集逐筆報為 `SKIPPED_NOT_EDITABLE`。`propertyIsEditable: true` 只描述 Property 定義，不代表元素當下可寫。
2. **Per-element result**：寫入後逐筆檢查 `executionResults`。任何 failure 都必須進報表；不得把 MCP 工具層「成功」當成批次成功。
3. **Read-back**：逐筆重讀目標 Property，比對 expected／actual；缺值或不一致都是 `WRITE_VERIFY_FAILED`。

建議正式 schema 將欄位拆開，`Check_Message` 只作摘要：

| Property | 型別／值 |
|---|---|
| `Source_Revision` | string |
| `Check_Status` | `UNREVIEWED / REVIEWED / ACCEPTED / REJECTED` |
| `Change_Type` | 本文件的 compare status |
| `Delta_Position_mm` | number |
| `Delta_Size_mm` | number |
| `Source_Entity_ID` | string |
| `Check_Run_ID` | string |
| `Check_Message` | ASCII `KEY=VALUE` 摘要，不含 `|` 與換行的自由文字 |

### 8.3 Element ID 邊界

依 E-06，Column 的非 GUID Element ID 可經 built-in Property `General_ElementID` 修改；Beam 只達 E-07。Element ID 是使用者識別碼，不是穩定主鍵，可能重複，也不應承載完整稽核歷史。因此正式流程預設**不加後綴、不改 ID**，以 GUID 做 run 內關聯、Property 做檢核狀態。若專案仍要求 ID 後綴，必須先檢查唯一性、保存原值、限可編輯元素、逐筆 read-back，且不得用於 Beam，直到 Beam pilot 通過。

## 9. View Map 與「顯示所選」

依 E-03、E-04，讀 View Map 已 `verified`，建立／複製 Saved View 是 `approximate mapping`。建議 pilot 路徑為 `STR-CHECK/<revision>/<story>`；每個 Saved View 應固定 Story、Layer Combination、Graphic Override、2D／3D filter 與 zoom。建立後必須重新讀 View Map 確認位置與設定，未 read-back 不算成功。

依 E-05，人工複核鏈為：

1. `elements_change_selection_of_elements`
2. `app_change_window` 切至 `3DModel`
3. `elements_highlight_elements`
4. `navigator_fit_in_window`

這個結果可達成「看見並聚焦被選構件」，但不是保證呼叫 Archicad 預設 F5 command；文件與 UI 都應標示為 approximate mapping。

## 10. 沒有真實結構圖時的測試

建立兩份可版控的 synthetic DXF：

- R00：兩柱、一梁、一板，分置 `S-COL`、`S-BEAM`、`S-SLAB`，含尺寸文字、block、已知單位與三個控制點。
- R01：一柱移動 6 mm、一柱尺寸增加 10 mm、新增一梁、刪除一梁、板高程改變 10 mm；另放一個 4 mm 位移作 unchanged 邊界測試。

必測項目：parser entity／layer／handle、單位轉換、transform residual、逐層 crop、matching 七種狀態、5 mm 絕對門檻、raw 與 snapped 並存、Favorite 單層小樣、Property 三道寫入關卡、View Map read-back、3D review，以及同一 `run_id + source_hash` 重跑不重複附加資料。

Synthetic fixture 只驗證演算法與 API 流程，不代表真實結構圖可解析；上線前仍須取得至少一份匿名化真實 DWG／DXF 做 pilot。

## 11. 報表最小欄位

```text
run_id, source_file, source_hash, source_mtime, revision, target_story,
source_layout, source_crop_raw, cad_units, cad_to_mm_factor,
transform, control_points, alignment_residual_mm, source_entity_id, source_layer,
element_guid, original_element_id, element_type, story_assignment,
status, match_confidence, raw_geometry, project_mm_geometry, snapped_geometry,
model_geometry, delta_position_mm, delta_size_mm, level_delta_mm,
threshold_mm, editable, write_execution_result, property_expected,
property_readback, reviewer, reviewed_at, evidence_id, limitations
```

每份報表首頁必須同時寫明資料來源與 hash、模型建構約定、單位與座標基準、處理 Story／layout／layer／crop、忽略的 entity、門檻、推斷欄位、工具版本、證據日期及適用範圍。

## 12. 應用邊界

本流程產生的構件與報表可用於：設計階段的初步放樣、建築／結構模型協調、版次差異盤點、內部 QA 與人工覆核清單。

不得直接用於：結構分析或簽證、配筋與接頭設計、加工／製造、施工放樣、竣工認定、建照或其他法定送審結論。CAD 抽取到數值不表示數值專業上正確；Favorite 建出的構件也不表示已符合結構設計。未經結構專業人員核定，不得自動接受差異、批次改模型幾何或把結果當契約允收公差。

## 13. 停止條件

任一情況立即停止該 Story：

- DWG primitive 未能可信解析，且沒有可驗證的 DXF／AutoCAD fallback。
- 單位未知、控制點不足、`alignment_residual_mm` 超標或 Story／crop 未確認。
- 命中 `AMBIGUOUS`、元素不可編輯或 Teamwork／Hotlink／鎖定狀態不明。
- Classification／Property availability 不符，或任一 `executionResults`／read-back 失敗。
- discovery 退化為 `[]`，無法判定 Add-On 是否仍回應。
- 使用者要求跨層或大批次幾何寫入，但尚未通過單層小樣與人工 gate。

## 14. 建議落地順序

1. 版控 synthetic DXF、parser 與 normalized JSON schema。
2. 完成單層 dry-run compare 與 JSON／CSV report。
3. 驗證 Property 三道關卡與 View Map 建立後 read-back。
4. 驗證 Favorite 單層 create pilot，再測 Beam Element ID。
5. 取得匿名化真實結構圖，完成 linked-DWG／DXF／AutoCAD fallback pilot。
6. 最後才評估需不需要受控的模型幾何修改；預設仍由人工決定。
