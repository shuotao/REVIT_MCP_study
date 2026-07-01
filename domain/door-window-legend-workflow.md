---
name: door-window-legend-workflow
description: 門窗圖例表 seed-based Legend Component 建立流程，主入口為 door-window-legend-tools，缺少 seed 時透過 list_seeds 取得候選並等待使用者選擇。
metadata:
  version: "1.5"
  updated: "2026-06-18"
  created: "2026-05-20"
  references: []
  related:
    - element-query-workflow.md
    - tool-capability-boundary.md
  referenced_by:
    - door-window-legend-tools
    - list_seeds
  contributors:
    - "OpenAI Codex"
  tags: [door, window, legend, type-mark, legend-component, seed, workflow]
---

# 門窗圖例表 Workflow

## 目的

此 SOP 定義門表與窗表的建立流程：

- `mode=list`：列出專案中已放置實例使用到的 door/window type。
- `mode=create`：使用指定的 seed Legend 視圖建立門表或窗表。
- `mode=update`：更新既有門表或窗表，補新增 type、刪除已不使用 type，並同步 Type Mark。
- 若 create 缺少 seed，tool 必須回 workflow state，對話層再呼叫 `list_seeds` 並等待使用者選擇。
- 若 create 缺少 `layoutDirection` 或 `maxPerLine`，tool 必須回 workflow state，要求對話層詢問使用者。
- 若 create/update 缺少 `dimensionTypeId` 且無法由既有視圖推斷，tool 必須要求對話層呼叫 `list_dimension_types`。
- 不得自動選 seed。
- 不得自動補排版方向或每排/欄數量。
- 不得自動輪流測試 seed。

## Tool Contract

### `door-window-legend-tools`

輸入：

- `targetType`: `door` 或 `window`
- `mode`: `list`、`create` 或 `update`
- `layoutDirection`: `horizontal` 或 `vertical`，create/update 必填
- `maxPerLine`: 大於等於 1，create/update 必填
- `seedLegendViewId`: create 使用的 seed Legend 視圖 ID
- `legendViewId`: update 使用的既有 Legend 視圖 ID
- `dimensionTypeId`: create/update 使用的 Revit `DimensionType` ID

create 若缺少 `seedLegendViewId`，回傳：

- `WorkflowState = "awaiting_seed_selection"`
- `NextAction = "call_list_seeds"`
- `SeedTypeRequired = "legend"`
- `RequiresUserInput = true`
- `DoNotAutoSelectSeed = true`
- `DoNotRetryWithOtherSeeds = true`
- `PromptToUser = "請先從 list_seeds 的結果中選擇一個 ViewName 作為 seed。"`

此狀態不是錯誤，也不是 fallback 訊號。對話層只能呼叫 `list_seeds` 後停下來問使用者。

create 若 seed 已有，但缺少 `layoutDirection` 或 `maxPerLine`，回傳：

- `WorkflowState = "awaiting_layout_preferences"`
- `NextAction = "ask_layout_preferences"`
- `RequiresUserInput = true`
- `DoNotAutoAssignLayout = true`
- `DoNotRetryCreateWithoutLayout = true`
- `MissingFields`
- `PromptToUser = "請選擇排版方向（horizontal 或 vertical），並提供每排/欄數量（maxPerLine）。"`

create/update 若 `layoutDirection` 或 `maxPerLine` 有值但不合法，回傳：

- `WorkflowState = "awaiting_valid_layout_preferences"`
- `NextAction = "ask_layout_preferences"`
- `RequiresUserInput = true`
- `DoNotAutoAssignLayout = true`
- `DoNotRetryCreateWithoutLayout = true`
- `InvalidFields`
- `PromptToUser = "請提供有效的排版方向（horizontal 或 vertical），以及大於等於 1 的 maxPerLine。"`

create/update 若缺少 `dimensionTypeId` 且無法由既有視圖推斷，回傳：

- `WorkflowState = "awaiting_dimension_type_selection"`
- `NextAction = "call_list_dimension_types"`
- `RequiresUserInput = true`
- `DoNotAutoSelectDimensionType = true`
- `DoNotRetryCreateWithoutDimensionType = true`
- `MissingFields = ["dimensionTypeId"]`
- `PromptToUser = "請先從 list_dimension_types 的結果中選擇一個 dimensionTypeName 作為標註類型。"`

只有 `seedLegendViewId`、`layoutDirection`、`maxPerLine`、`dimensionTypeId` 都齊全且合法時，create 才會進入 Revit 建立流程。

### `list_seeds`

輸入：

- `seedType = "legend"`

回傳全部非樣板 Legend 視圖：

- `viewId`
- `viewName`
- `legendComponentCount`
- `isUsableSeed`

回傳 workflow metadata：

- `WorkflowState = "awaiting_user_choice"`
- `SelectionMode = "user_must_choose"`
- `SelectionField = "ViewName"`
- `RequiresUserInput = true`
- `DoNotAutoSelect = true`
- `DoNotAutoRetryCreate = true`
- `PromptToUser = "請從以下 Legend 視圖中選一個 ViewName 作為 seed。"`

### `list_dimension_types`

輸入：無。

回傳專案內可用 Revit `DimensionType`：

- `dimensionTypeId`
- `dimensionTypeName`
- `familyName`
- `isDefault`

回傳 workflow metadata：

- `WorkflowState = "awaiting_user_choice"`
- `SelectionMode = "user_must_choose"`
- `SelectionField = "dimensionTypeName"`
- `RequiresUserInput = true`
- `DoNotAutoSelect = true`
- `DoNotAutoRetryCreate = true`
- `PromptToUser = "請從以下標註類型中選一個 dimensionTypeName 作為門窗圖例尺寸標註類型。"`

對話層必須把清單交給使用者選擇，不得自動挑 `isDefault = true` 的類型。

## Used Types

- `door` 使用 `BuiltInCategory.OST_Doors`
- `window` 使用 `BuiltInCategory.OST_Windows`
- 只收已放置 instance 的 type
- `door` 以 `GetTypeId()` 去重，再回查 `FamilySymbol`
- `window` 以 `typeId + sillHeightCm` 分組；同一窗型若窗台高不同，會建立多筆圖例項目
- `Type Mark` 優先讀 `ALL_MODEL_TYPE_MARK`
- 空白 Type Mark 顯示 `(未填)`，排序排最後

## 排序

1. 主鍵為 `Type Mark`
2. 非空白 Type Mark 使用自然排序，例如 `D1 < D2 < D10`
3. 空白 Type Mark 排最後
4. 次鍵為 `Type Name`

## 建立流程

create 前置 gating：

1. 若缺 `seedLegendViewId`，回 `awaiting_seed_selection`。
2. 若 seed 已有，但缺 `layoutDirection` 或 `maxPerLine`，回 `awaiting_layout_preferences`。
3. 若 seed 已有，但 `layoutDirection` 或 `maxPerLine` 不合法，回 `awaiting_valid_layout_preferences`。
4. 若缺 `dimensionTypeId`，回 `awaiting_dimension_type_selection`。
5. 只有必要參數齊全且合法，才進入下列建立流程。

正式建立流程：

1. 驗證 view 存在、是 `Legend`、不是 template。
2. 用 `ViewDuplicateOption.WithDetailing` 複製 seed Legend。
3. 在 duplicated view 內收集 `seedOriginalElementIds`。
4. 在 duplicated view 內找第一個 `OST_LegendComponents` 作為 source seed component。
5. 對每個 used type，從 source seed component 做 same-view copy。
6. 對 copy 出來的 component 設定 `BuiltInParameter.LEGEND_COMPONENT = FamilySymbol.Id`。
7. 對齊 Legend Component；窗表會依該 instance 的窗台高調整 component 高度。
8. 建立 Type Mark `TextNote`，文字只放 `typeMarkDisplay`。
9. 建立 Revit 原生寬高 `Dimension`。
10. 門表建立門底部 FFL 線與文字；窗表維持既有窗台高與 FFL 流程。
11. 完成後執行安全清理，只嘗試刪 duplicated seed 原始內容。
12. 切換到新建立的 Legend 視圖。

## 門表底部對齊與 FFL

門表排列使用門圖例 bbox 的底部中心作為 placement anchor：

- anchor = `((bbox.Min.X + bbox.Max.X) / 2, bbox.Min.Y)`。
- seed component 與每個 copied/applied component 都用同一個底部中心基準對齊。
- 同一列門圖例會以底部切齊，不受門高差異影響。
- 窗表不套用此門專用對齊規則，既有窗表流程不變。

每個成功建立的門 Legend Component 會新增底部 FFL 元素：

- FFL line 為 `DetailCurve`，Y = `bbox.Min.Y`。
- 線長 = 門圖例 bbox 寬度 `2.0` 倍。
- 線段中心對齊門圖例底部中心；端點為 `centerX ± width * 0.75`。
- FFL 文字使用預設 `TextNoteType`，內容固定為 `FFL`。
- FFL 文字使用 `HorizontalTextAlignment.Left` 與 `VerticalTextAlignment.Bottom`，插入點在線段左端點上方 5cm；文字框底部離細部線 5cm，靠左端切齊且不得壓到細部線。
- 門 Type Mark 文字放在 FFL 線中心上方 400cm，置中對齊。
- FFL line/text 加入 generated/protected ids，安全清理不能刪。
- FFL 建立失敗不 rollback 該門圖例，只寫入 `AttemptDebug.DoorFflFailureReason`。

## 原生寬高標註

每個成功放置的 Legend Component 都會嘗試建立 Revit 原生寬高 Dimension：

- 優先使用 Legend Component 既有 2D geometry reference。
- 掃描 `appliedComponent.get_Geometry(new Options { View = legendView, ComputeReferences = true })`。
- 遞迴處理 `GeometryInstance`、`Curve`、`Line`、`Edge`。
- 寬度：找接近 component bbox 左右邊界的垂直線/邊 reference。
- 高度：找接近 component bbox 上下邊界的水平線/邊 reference。
- direct geometry reference 找不到或 `NewDimension` 失敗時，fallback 建短 `DetailCurve` 作為 reference。
- fallback reference stub 必須貼近 component bbox：寬度 stub 位於 bbox 左右邊界、y 範圍為 `bounds.Max.Y - 12cm` 到 `bounds.Max.Y`；高度 stub 位於 bbox 上下邊界、x 範圍為 `bounds.Max.X - 12cm` 到 `bounds.Max.X`。
- fallback reference stub 不得貼近或跨越 Dimension line，避免 Revit witness line 從尺寸線附近反向延伸。
- fallback detail curves 是 Dimension 依附元素，必須加入 generated/protected ids，安全清理不能刪。

位置規則：

- 寬度 Dimension 線放在 component bbox 上方 50cm。
- 高度 Dimension 線放在 component bbox 右側 50cm。
- Type Mark 放在寬度 Dimension 線上方 80cm；目前使用 `max(LabelOffsetCm, DimensionOffsetCm + LabelAboveDimensionOffsetCm)`，會落在 component bbox 上方 130cm，避免與尺寸線/尺寸文字重疊。
- direct geometry reference 搜尋容差為 3cm。
- fallback reference stub 長度為 12cm。

注意：

- `BoundingBoxXYZ` 只用於定位與候選 reference 篩選，不作為 Dimension reference。
- Dimension 使用 Revit 原生 `Dimension`，跟隨選定 `DimensionType` 與專案單位，不手動覆寫文字。
- 寬高 Dimension 建立失敗不阻斷該 Legend Component，也不 rollback 整張門表/窗表；只累計統計並寫入 `AttemptDebug`。

## 安全清理

清理只使用 ElementId 集合，不依賴生成流程內部 id 名稱：

- `seedOriginalElementIds`：duplicate 後當下視圖內所有元素 id。
- `finalViewElementIds`：生成完成後視圖內所有元素 id。
- `protectedElementIds = finalViewElementIds - seedOriginalElementIds`，代表本次新生成的元素。

清理規則：

1. 對每個 `seedOriginalElementId` 開 `SubTransaction`。
2. 呼叫 `doc.Delete(originalId)`。
3. 讀取 Revit 實際回傳的 `deletedIds`。
4. 若 `deletedIds` 會刪到任何 `protectedElementIds`，rollback 該筆刪除。
5. 若沒有碰到 protected ids，commit 該筆刪除。

這樣可以避免 Revit cascade delete 把新生成的 Legend Component 一起刪掉。

注意：

- 這版清理是「逐一嘗試刪除」，不是保證完全清空 seed 原始內容。
- 若某個 seed 原始元素會連帶刪掉新生成元素，該筆會被 skip 並保留。
- 因此最終圖例視圖可能仍殘留部分 seed 原始元素，這是目前部署版的預期保護行為。
- fallback Dimension reference curves 屬於本次生成元素，必須被 protected，不能被清理。

## 錯誤規則

- `create_mode_requires_layout_direction_and_max_per_line`：create 缺少 `layoutDirection` 或 `maxPerLine`，或數值不合法。
- `invalid_seed_type`：`list_seeds` 的 `seedType` 不是 `legend`。
- `legend_seed_view_not_found`：指定 seed view 不存在、不是 Legend，或是 template。
- `legend_seed_component_not_found`：指定 seed view 沒有任何 Legend Component。
- `legend_seed_component_type_mismatch`：seed view 存在且有 Legend Component，但 duplicated view 內找不到可讀取的 source component，或建立流程在 seed/source component 階段失敗。
- `legend_component_type_swap_failed`：copy 後無法設定成目標 door/window type。
- `dimension_type_not_found`：指定 `dimensionTypeId` 找不到有效的 Revit DimensionType。
- `legend_view_target_type_mismatch`：update 指定的 Legend 視圖不含目標 door/window Legend Component，或選到相反類型的圖例表。

其中 `create_mode_requires_layout_direction_and_max_per_line` 在目前流程中屬於內部 fallback validation，不是正常互動主路徑；正常互動應優先回 `awaiting_layout_preferences` 或 `awaiting_valid_layout_preferences`。

若指定 seed 失敗：

- 不提供其他 seed 建議。
- 不自動改試其他 seed。
- 交還使用者重新選擇。

## 輸出重點

create 成功時回傳：

- `legendViewId`
- `legendViewName`
- `seedLegendViewId`
- `seedLegendViewName`
- `usedTypeCount`
- `placedCount`
- `failedTypes[]`
- `CleanupMode`
- `CleanupDeletedCount`
- `CleanupSkippedCount`
- `CleanupSkippedOriginalIds[]`
- `CleanupProtectedElementCount`
- `CleanupDeletedElementIds[]`
- `CleanupSkipped`
- `CleanupReason`
- `SeedOriginalElementCount`
- `GeneratedElementCount`
- `DimensionTypeId`
- `DimensionTypeName`
- `DimensionCreatedCount`
- `DimensionFailedCount`
- `FinalViewElementCountBeforeCleanup`
- `FinalViewElementCountAfterCleanup`
- `AttemptDebug`

## 窗表 FFL 基準、窗台高與窗台高標註

窗表以 FFL 線作為 row baseline：

- window used entries 以 `typeId + sillHeightCm` 分組；同一窗型若窗台高不同，必須產生多個窗圖例項目。
- 窗台高讀取順序為 placed window instance 參數優先，找不到才讀 type 參數。
- 窗台高參數名稱匹配 `Sill Height`、`窗台高`、`窗台高度`；找不到時使用 `0cm`，並在 debug 記錄 `missing_default_zero`。
- 排序維持 Type Mark 自然排序；同一 Type Mark 下再依 `sillHeightCm` 由低到高排序。
- window placement anchor 使用 bbox bottom center：`((bbox.Min.X + bbox.Max.X) / 2, bbox.Min.Y)`。
- window component 套用 type 後，先二次對齊到 row 的 FFL anchor，再依 `sillHeightFeet` 往 view Y 正方向上移。
- FFL 線維持在 row anchor，不跟著窗本體上移；同列窗圖例的 FFL 線應切齊。

每個 window item 會建立 FFL 元素：

- FFL line 為 `DetailCurve`，線長 = moved window bbox 寬度 `2.0` 倍。
- FFL line 中心對齊 FFL anchor X，Y = FFL anchor Y。
- FFL `TextNote` 內容固定為 `FFL`，使用 `HorizontalTextAlignment.Left` 與 `VerticalTextAlignment.Bottom`，插入點在線段左端點上方 5cm；文字框底部離細部線 5cm，靠左端切齊且不得壓到細部線。
- FFL line/text 必須加入 generated/protected ids，安全清理不能刪。

window Type Mark 與窗台高 Dimension：

- window Type Mark 不放在 component bbox 上方，固定放在 FFL 線中心上方 400cm。
- window sill-height Dimension 使用 Revit 原生 `Dimension`，套用使用者選定的同一個 `dimensionTypeId`。
- 窗台高 Dimension 從 FFL reference 標到上移後 window bbox 底部 reference。
- Dimension line X 與右側 height Dimension 對齊：`movedBounds.Max.X + 50cm`。
- 優先使用 window geometry bottom horizontal reference；失敗時 fallback 建短 `DetailCurve` reference。
- FFL line、fallback reference curve、sill Dimension 都必須加入 generated/protected ids，安全清理不能刪。
- 窗台高 Dimension 建立失敗只寫入 `AttemptDebug.WindowSillDimensionFailureReason`，不 rollback 該窗圖例。

window list/create 額外輸出重點：

- `SillHeightCm`
- `SillHeightFeet`
- `SillHeightSource`
- `RepresentativeInstanceId`
- `AttemptDebug.PlacementAnchor = "ffl_bottom_center"`
- `AttemptDebug.WindowSillHeightCm`
- `AttemptDebug.WindowSillHeightSource`
- `AttemptDebug.WindowFflLineId`
- `AttemptDebug.WindowFflTextId`
- `AttemptDebug.WindowFflLineLengthFactor`
- `AttemptDebug.WindowFflTextOffsetCm`
- `AttemptDebug.WindowSillDimensionId`
- `AttemptDebug.WindowSillDimensionReferenceSource`
- `AttemptDebug.WindowSillDimensionReferenceCurveIds`
- `AttemptDebug.WindowSillDimensionFailureReason`
- `AttemptDebug.WindowTypeMarkOffsetCm = 400`

## Update 既有門窗圖例表

`door-window-legend-tools` 支援 `mode=update`，用於更新既有門表/窗表，不重新生成整張表。

Tool contract：

- `mode`: `list | create | update`
- `legendViewId`: update 使用的既有 Legend View ID
- `layoutDirection`: update 必填，`horizontal | vertical`
- `maxPerLine`: update 必填，每列或每欄數量
- `dimensionTypeId`: update optional；優先從 selected Legend View 內既有 `Dimension` 推斷，推斷不到才要求使用者選

Update gating：

- 缺 `targetType`：回 `WorkflowState = "awaiting_target_type_selection"`，`SelectionField = "targetType"`，要求使用者選 `door` 或 `window`。
- 缺 `legendViewId`：回 `WorkflowState = "awaiting_legend_view_selection"`，`NextAction = "call_list_legend_views"`。
- 缺 `layoutDirection` 或 `maxPerLine`：回 `awaiting_layout_preferences`，不自動猜排列規則。
- `dimensionTypeId` 有提供但不是有效 `DimensionType`：回 `dimension_type_not_found`。
- `dimensionTypeId` 未提供且 selected Legend View 內找不到既有 `DimensionType`：回 `awaiting_dimension_type_selection`。
- update 指定窗表但 selected Legend View 內 `window Legend Component = 0` 且 `door Legend Component > 0`：回 `awaiting_legend_view_selection` / `legend_view_target_type_mismatch`，要求重新選窗圖例表。
- update 指定門表但 selected Legend View 內 `door Legend Component = 0` 且 `window Legend Component > 0`：回 `awaiting_legend_view_selection` / `legend_view_target_type_mismatch`，要求重新選門圖例表。
- selected Legend View 內 target type 與 other type 都是 0 時也會擋下，訊息改為該視圖內沒有可更新的門/窗 Legend Component。
- selected Legend View 同時包含門與窗時先允許；update 只操作本次 `targetType`，並在 `AttemptDebug` 回 `WarningCode = "mixed_target_types_in_legend_view"`。

`list_legend_views`：

- 列出所有非 template Legend View。
- 回傳 `viewId`、`viewName`、`legendComponentCount`、`doorLegendComponentCount`、`windowLegendComponentCount`。
- 同時保留 `ViewId`、`ViewName` 等 PascalCase alias，兼容既有回傳格式。
- workflow metadata：`SelectionMode = "user_must_choose"`，`SelectionField = "viewName"`。

Update 比對：

- desired door items 依 `typeId` 去重。
- desired window items 依 `typeId + sillHeightCm` 去重。
- existing items 從 selected Legend View 的 `OST_LegendComponents` 讀 `BuiltInParameter.LEGEND_COMPONENT` 反查 `FamilySymbol` 類別。
- 圖例 item 身份來源永遠是 Legend Component 反查到的 `FamilySymbol.TypeId`，不是圖例表內的 `TextNote` 文字。
- door existing key = `typeId`。
- window existing key = `typeId + detectedSillHeightCm`；`detectedSillHeightCm` 由 component bbox bottom 與附近 FFL line Y 差推回，四捨五入到 `0.1cm`。
- window 若找不到 FFL line，且同 type 專案仍有使用，stale delete 會跳過並記錄 `window_ffl_missing_same_type_still_used`，避免誤刪同 type 不同窗台高項目。

Update 行為：

- stale delete 判斷完成後，missing append 前，會同步既有圖例的 Type Mark `TextNote`。
- Type Mark 同步會重新讀取目前 `FamilySymbol.ALL_MODEL_TYPE_MARK`；使用者手動改圖例文字不視為 override，下次 update 會被專案 Type Mark 覆蓋。
- 重新編碼案例：若 `D3 -> D2`、`D4 -> D3`，仍使用中的 type 會保留圖例本體並更新文字；已不用的 type 才整組刪除。
- Type Mark `TextNote` 偵測優先使用 metadata：`tool = door-window-legend`、`role = type_mark`、`targetType`、`componentId`、`typeId`、`itemKey`。
- 舊圖例沒有 metadata 時，fallback 用位置推斷：新規則找 FFL anchor 上方 400cm 且水平置中的 `TextNote`；舊規則找 component bbox 上方 label 區域；文字為 `FFL` 的 `TextNote` 會排除。
- fallback 候選超過一個時選距離預期點最近者；若距離過大會跳過並回 debug，避免誤改其他文字。
- 新生成與 update append 的 item 會替 Type Mark `TextNote` 寫入 `role = type_mark` metadata，並替 FFL `TextNote` 寫入 `role = ffl_label` metadata。
- 缺少的 item 只 append 到目前最大 grid index 後面。
- 不重新排版既有項目。
- 不填補中間空洞。
- append 使用 selected Legend View 內最後一個 matching targetType Legend Component 作為 copy source。
- append 後沿用 create placement：bottom-center / FFL anchor、窗台高位移、寬高 Dimension、窗台高 Dimension、Type Mark、FFL line/text、cleanup protected ids。
- stale item 用整組刪除，不只刪 Legend Component。
- stale 關聯元素包含 Legend Component、Type Mark、FFL line/text、寬高 Dimension、窗台高 Dimension、fallback detail curves。
- 每個 stale item 使用 `SubTransaction` 刪除；若刪除集合會波及其他非 stale Legend Component，rollback 並記錄 skip reason。
- update door 不碰 window 圖例；update window 不碰 door 圖例。

Update 回傳重點：

- `WorkflowState = "updated"`
- `TargetType`
- `LegendViewId`, `LegendViewName`
- `DesiredCount`, `ExistingCount`
- `AddedCount`, `DeletedCount`, `SkippedDeleteCount`
- `MissingItems[]`, `DeletedItems[]`, `SkippedItems[]`
- `AttemptDebug[]`
- `DimensionTypeId`, `DimensionTypeName`
- `DimensionTypeSource = "existing_view_dimension" | "user_selected"`
- `TypeMarkUpdatedCount`
- `TypeMarkSkippedCount`

`AttemptDebug` update 欄位：

- `ExistingKey`
- `DetectedGridIndex`
- `DetectedFflLineId`
- `DetectedSillHeightCm`
- `UpdateAction = "add" | "delete" | "keep" | "skip_delete"`
- `DeleteElementIds`
- `SkipReason`
- `DuplicatedViewDebug`
- `WarningCode`
- `MixedTargetTypesInLegendView`
- `CurrentTypeMark`
- `ExistingTypeMarkText`
- `TypeMarkTextNoteId`
- `TypeMarkSyncAction = "updated" | "unchanged" | "skip"`
- `TypeMarkSyncSkipReason`

`AttemptDebug` 每個成功 type 會包含：

- `WidthDimensionId`
- `HeightDimensionId`
- `DimensionReferenceSource`
- `WidthDimensionReferenceSource`
- `HeightDimensionReferenceSource`
- `DimensionReferenceCurveIds`
- `DimensionFailureReason`

create 失敗時可能額外回傳：

- `SeedViewDebug`
- `DuplicatedViewDebug`
- `AttemptDebug`
