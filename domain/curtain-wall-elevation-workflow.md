---
name: curtain-wall-elevation-workflow
description: "帷幕牆外立面視圖生成 SOP：當使用者要求帷幕立面、帷幕表、curtain wall elevation、create_curtain_wall_elevations 時觸發；規範如何用 Revit 原生 ElevationMarker 為每道 curtain wall 建立外側 elevation view、套用「帷幕立面」View Template、回報 skipped reason，並區分 MCP tool discovery / deployment 問題。"
metadata:
  version: "1.4"
  updated: "2026-07-31"
  created: "2026-07-12"
  references: []
  related:
    - curtain-wall-pattern.md
    - element-query-workflow.md
    - tool-capability-boundary.md
    - view-link-cleanup-workflow.md
    - wall-check.md
  referenced_by:
    - curtain-wall
    - create_curtain_wall_elevations
    - diagnose_curtain_wall_elevation_direction
    - diagnose_curtain_wall_elevation_directions
  contributors:
    - "gpt-5"
  tags: [帷幕牆, 帷幕立面, 帷幕表, curtain-wall, elevation, view-template, ElevationMarker, Revit API]
---

# 帷幕牆外立面視圖生成 Workflow

這份 domain 的目的，是讓 AI client 遇到「帷幕立面 / 帷幕表 / curtain wall elevation」需求時，直接呼叫 MCP tool，而不是掃專案檔案或自己猜 Revit API。

核心 tool：

```text
create_curtain_wall_elevations
```

Direction debug tool:
```text
diagnose_curtain_wall_elevation_direction
diagnose_curtain_wall_elevation_directions
```

## Trigger

使用者提到下列任一需求時，應優先使用本 workflow：

- 帷幕立面
- 帷幕表
- 帷幕牆外立面
- curtain wall elevation
- curtain schedule
- curtain wall legend
- `create_curtain_wall_elevations`
- `diagnose_curtain_wall_elevation_direction`
- `diagnose_curtain_wall_elevation_directions`

## 不要做的事

如果使用者只是要求「生成帷幕牆外立面」，不要先跑：

```powershell
Get-ChildItem
rg
dir
```

正確行為是直接呼叫 MCP tool。搜尋 repo 只適用於開發者要修改這個 tool 的原始碼時。

## Tool Contract

### Input

| 參數 | 型別 | 預設 | 說明 |
|---|---:|---:|---|
| `placementViewId` | number | optional | 指定用來建立 `ElevationMarker` 的 `ViewPlan`。 |
| `placementViewName` | string | optional | 指定 placement view 名稱。 |
| `scale` | number | `50` | 立面比例。 |
| `offsetMm` | number | `1500` | marker 放在牆外側的距離。 |
| `horizontalMarginMm` | number | `0` | crop 左右 margin；直線帷幕以投影後的 `Wall.LocationCurve` 端點為基準，非直線牆才使用可視幾何 fallback。 |
| `verticalMarginMm` | number | `0` | crop 上下 margin；底部套用於 Level-aware resolved bottom，頂部套用於帷幕幾何 top。 |
| `depthMm` | number | `1200` | 無有效目標點或無法解析視線方向時使用的 fallback far clip depth。 |
| `viewTemplateName` | string | `"帷幕立面"` | 要套用或建立的 View Template 名稱。 |
| `applyViewTemplate` | boolean | `true` | 是否套用 View Template。 |
| `nameSeparator` | string | `""` | `{LevelName}` 與 `{Mark}` 中間的分隔字串。 |
| `wallTagTypeId` | number | required | 已載入的 `OST_WallTags` `FamilySymbol` ID；未提供時先回傳選單，且不修改模型。 |
| `addDimensions` | boolean | `true` | 是否建立總寬、總高及 CurtainGrid 尺寸。 |
| `dimensionTypeSelectionMode` | string | `"auto"` | `auto` 自動解析 DimensionType；`prompt` 且未指定類型時要求使用者選擇。 |
| `dimensionTypeId` | number | optional | 指定線性 `DimensionType` ID，優先於名稱與自動解析。 |
| `dimensionTypeName` | string | optional | 指定線性 `DimensionType` 名稱。 |
| `dimensionOffsetMm` | number | `300` | 無法取得 DimensionType 輔助線長度時，帷幕邊界至內層尺寸線的模型距離 fallback。 |
| `dimensionStackOffsetMm` | number | `250` | 無法取得 DimensionType 輔助線長度時，內外層尺寸線間距的模型距離 fallback。 |
| `dryRun` | boolean | `false` | 只預覽結果，不建立 view。 |

Far clip 以 `view.Origin` 沿立面實際視線方向投影目標帷幕元素，取最遠正深度並加 `50 mm`。只有無有效目標點或無法解析視線時才使用 `depthMm`；全部投影深度為負時改用最大絕對深度並回傳警告。寫入後會以 `1 mm` 容差讀回驗證。

`wallTagTypeId` 是 dry run 與正式執行的共同必要前置輸入；未選擇有效牆標籤類型時，不會進入尺寸類型解析或建立任何模型元素。

### Output

| 欄位 | 說明 |
|---|---|
| `Success` | command 是否完成。 |
| `DryRun` | 是否 dry run。 |
| `TotalCurtainWalls` | 目前文件內 `Wall.CurtainGrid != null` 的牆數。 |
| `CreatedCount` | 成功建立的 elevation view 數。 |
| `SkippedCount` | 跳過或建立失敗的牆數。 |
| `ViewTemplateId` | 使用的 template id；dry run 為 `0`。 |
| `ViewTemplateName` | 使用的 template 名稱。 |
| `TemplateCreated` | 是否新建 template。 |
| `TemplateUpdated` | 是否更新 template category / non-controlled params。 |
| `AddDimensions` | 本批次是否要求建立尺寸。 |
| `DimensionTypeId` / `DimensionTypeName` / `DimensionTypeSource` | 本批次解析到的 DimensionType 與來源。 |
| `DimensionWarnings[]` | DimensionType 解析或尺寸批次警告。 |
| `WallTagTypeId` / `WallTagTypeName` | 本批次使用的牆標籤類型。 |
| `WallTagWarnings[]` | 牆標籤批次警告。 |
| `Created[]` | 每道成功牆的結果。 |
| `Skipped[]` | 每道失敗或跳過牆的原因。 |
| `TemplateWarnings[]` | category 或 template parameter 無法處理時的 warning。 |

`Created[]` 的穩定結果群組包含：基本 view／wall 識別、far clip readback、Level-aware crop、尺寸建立與 Reference 驗證、牆標籤結果，以及需要時的 active-view 診斷。內部除錯欄位可隨診斷需求增加，不應把範例視為完整固定欄位清單。

`Created[]` item 範例：

```json
{
  "WallId": 12345,
  "ViewId": 67890,
  "ViewName": "1FCW-01",
  "LevelName": "1F",
  "Mark": "CW-01",
  "MarkerId": 24680,
  "FarClipDepthMm": 1750.0,
  "FarClipMethod": "view_origin_to_target_max_depth",
  "FarClipRequestedDepthMm": 1750.0,
  "FarClipActualOffsetMm": 1750.0,
  "FarClipActualActive": 1,
  "FarClipActualMode": 2,
  "FarClipPass": true,
  "DirectionDot": 1.0,
  "DirectionFixApplied": false,
  "DesiredLookDirection": { "X": -1.0, "Y": 0.0, "Z": 0.0 },
  "FinalVisualLookDirection": { "X": -1.0, "Y": 0.0, "Z": 0.0 },
  "WallOrientation": { "X": 1.0, "Y": 0.0, "Z": 0.0 },
  "CropPointSource": "geometry",
  "CropPointCount": 128,
  "CropFallbackElementCount": 0,
  "CropFrameSource": "wall_location_curve",
  "CurtainHorizontalBoundarySource": "wall_location_curve_endpoints",
  "CurtainBoundaryMinXmm": -3469.0,
  "CurtainBoundaryMaxXmm": 3469.0,
  "CropBottomRule": "min(wall_level_y, curtain_geometry_min_y) - vertical_margin",
  "CropBottomBoundarySource": "wall_level",
  "CropBottomLevelId": 13579,
  "CropBottomLevelName": "1F",
  "CropBottomLevelViewYmm": 0.0,
  "CurtainGeometryMinYmm": 200.0,
  "CurtainGeometryMaxYmm": 3000.0,
  "CropBottomResolvedYmm": 0.0,
  "CropWarnings": [],
  "CropRightDirection": { "X": 0.0, "Y": 1.0, "Z": 0.0 },
  "CropUpDirection": { "X": 0.0, "Y": 0.0, "Z": 1.0 },
  "CropDepthDirection": { "X": -1.0, "Y": 0.0, "Z": 0.0 },
  "CropLocalMin": { "X": -8.0, "Y": 0.0, "Z": -0.2 },
  "CropLocalMax": { "X": 8.0, "Y": 3.2, "Z": 0.2 },
  "CropUsedRevitTransformFallback": false,
  "CropUsedHostWallFallback": false,
  "CropContributingElementIds": [12346, 12347],
  "CropFallbackElementIds": [],
  "CropExtremeContributors": {
    "MinX": { "ElementId": 12346, "Category": "Curtain Panels", "Source": "geometry" }
  },
  "CropRegionShapeApplied": false,
  "CropRegionShapeFallbackReason": "disabled_for_diagnostics",
  "MarkerPoint": { "X": 100.0, "Y": 20.0, "Z": 0.0 },
  "AddDimensions": true,
  "DimensionTypeId": 11223,
  "DimensionTypeName": "線性 - 2.5mm",
  "DimensionsCreatedCount": 4,
  "DimensionStatus": "created",
  "TotalWidthDimensionId": 67891,
  "TotalHeightDimensionId": 67892,
  "CurtainBottomToLevelDistanceMm": 200.0,
  "LevelOffsetDimensionMode": "enhanced_total_height_chain",
  "LevelOffsetDimensionElementId": 67892,
  "LevelOffsetDimensionStatus": "created",
  "LevelOffsetDimensionAreReferencesAvailable": false,
  "LevelOffsetDimensionReferenceSource": "wall_level_plane_reference",
  "PostCommitDimensionValidation": [],
  "WallTagId": 67895,
  "WallTagStatus": "created",
  "WallTagPositionMm": { "X": 0.0, "Y": 5200.0, "Z": 0.0 },
  "WasViewOpenBeforeDimensioning": false,
  "WasViewActiveBeforeDimensioning": false,
  "WallMidPoint": { "X": 95.0, "Y": 20.0, "Z": 0.0 }
}
```

`Skipped[]` item 範例：

```json
{
  "WallId": 12345,
  "LevelName": "1F",
  "Mark": "CW-01",
  "Reason": "立面方向驗證失敗，DirectionDot=0.1234"
}
```

## Revit API Method

### 1. Collect Curtain Walls

只處理目前 Revit document，不處理 linked models。

判斷條件：

```csharp
wall.CurtainGrid != null
```

排序：

1. `wall.LevelId`
2. `wall.Id`

### 2. Level and Mark

Level：

```csharp
doc.GetElement(wall.LevelId) as Level
```

Mark：

```csharp
wall.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString()
```

Mark 空白時 fallback：

```text
CW-{ElementId}
```

View name：

```text
{LevelName}{nameSeparator}{Mark}
```

重名時自動加：

```text
_2, _3, _4 ...
```

### 3. Placement View Resolution

`ElevationMarker.CreateElevation(...)` 需要一張 `ViewPlan` 作為 placement view。

解析順序：

1. `placementViewId`
2. `placementViewName`
3. active view，如果 active view 是 non-template `FloorPlan`
4. 該 wall `LevelId` 對應的第一張 non-template `FloorPlan`

找不到時該牆進入 `Skipped[]`。

### 4. Exterior Side and Direction Rule

`wall.Orientation` 是 API exterior normal，但不一定等於使用者在平面圖中視覺認定的雙箭頭 / flip control 所在側。不要用同樓層牆群中心、房間中心、bounding box 中心或平面幾何中心去猜外側，這些 heuristic 會在凹角、斜牆、突出量體或非凸平面中把原本正確的牆反轉。

目前經使用者標記的 regression sample 顯示，使用者認定的帷幕外側應用 `wall.Flipped` 解析：

```text
wall.Flipped == true  -> api_orientation      -> wall.Orientation
wall.Flipped == false -> opposite_orientation -> -wall.Orientation
```

這個規則只負責 marker placement side。marker 建立後仍要用 `markerPoint -> wallMid` 做視覺觀看方向對齊與 `DirectionDot >= 0.98` 驗證。若未來出現 `wall.Flipped` 無法解釋的反例，不要再改全域 heuristic；下一步應加 per-wall override。

方向錯誤時先跑 `diagnose_curtain_wall_elevation_direction`，取得可重複診斷資料，再決定是否更新生成邏輯。固定 regression sample：`WallId = 365849` 目前 API `wall.Orientation = (-1, 0)`，但使用者視覺期待外側在右側。

marker 放置點：

```text
markerPoint = wallMid + resolvedExteriorDirection * (wall.Width / 2 + offsetMm)
```

外立面的幾何觀看方向必須是：

```text
desiredLookDirection = wallMid - markerPoint
```

也就是從牆外側 marker 位置看回帷幕牆中心。

不要把 `View.ViewDirection` 直接當成口語上的「鏡頭看向哪裡」。Revit elevation marker 的可視方向與 API `ViewDirection` 容易相反。本 workflow 明確定義：

```csharp
visualLookDirection = FlattenAndNormalize(view.ViewDirection.Negate());
desiredLookDirection = FlattenAndNormalize(wallMid - markerPoint);
```

方向驗證：

```text
visualLookDirection · desiredLookDirection >= 0.98
```

如果第一次旋轉後 dot `< 0`，代表 marker 仍背對牆，應再旋轉 `Math.PI` 並重新 `doc.Regenerate()`。若最終 dot 仍 `< 0.98`，不可把該 view 當成功結果；應刪除剛建立的 view/marker，並把該牆寫入 `Skipped[]` 或回傳明確方向 warning。

### 5. Create Elevation

建立流程：

```csharp
ElevationMarker marker = ElevationMarker.CreateElevationMarker(doc, elevationType.Id, markerPoint, scale);
ViewSection view = marker.CreateElevation(doc, placementView.Id, 0);
doc.Regenerate();

XYZ desiredLookDirection = wallMid - markerPoint;
Align marker visual look direction to desiredLookDirection;
doc.Regenerate();

Validate visualLookDirection dot desiredLookDirection;
```

`doc.Regenerate()` 是必要步驟。剛建立的 elevation view / marker 若未 regenerate，`ViewDirection`、`CropBox`、可旋轉狀態可能尚未穩定。

### 6. Crop and Far Clip

> Current rule, use this first: crop is calculated after the elevation view is created, using the target curtain wall elements as they appear in that elevation view's 2D coordinate system.

正式 crop method：

```text
view_2d_visible_bounds
```

流程：

1. 先建立方向正確的 native `ElevationMarker` / elevation `ViewSection`。
2. 將 crop / far clip 暫時放大並 `doc.Regenerate()`，讓 view-specific bounds 穩定。
3. 只收集目標帷幕牆自己的元素：
   - `CurtainGrid.GetPanelIds()`
   - `CurtainGrid.GetMullionIds()`
   - `Wall.FindInserts(true, true, true, true)`，包含 hosted doors / windows / openings
4. 對每個元素優先取：
   - `element.get_BoundingBox(view)`，也就是該 elevation view 內的 view-specific bounds
   - 若取不到，再用 `element.get_Geometry(new Options { View = view, DetailLevel = Fine, IncludeNonVisibleObjects = false })`
   - 最後才 fallback 到 `element.get_BoundingBox(null)`
5. 使用 elevation view 的 2D 畫面座標計算最小矩形：
   - X 軸：`view.RightDirection`
   - Y 軸：`view.UpDirection`
   - `u = (point - origin) · view.RightDirection`
   - `v = (point - origin) · view.UpDirection`
6. 將 2D 矩形四角轉回 Revit 實際 `view.CropBox.Transform` local space，只設定 `CropBox.Min` / `CropBox.Max`。

重要限制：

- 不要把自訂 `BoundingBoxXYZ.Transform` 當作 crop frame 寫回。Revit 設定 `View.CropBox` 時會忽略 input transform，只吃 `Min` / `Max`。
- 不要用 `ViewCropRegionShapeManager.SetCropShape()` 做這個功能。先前實測斜向帷幕會更偏，正式生成必須維持 `CropRegionShapeApplied = false`。
- 不要用 world-axis-aligned `get_BoundingBox(null)` 當主要來源；它只允許作最後 fallback。
- host wall 只允許在 panel / mullion / insert 完全沒有任何可用 bounds/geometry 時作 fallback。

`Created[]` 需要回傳：

- `CropMethod`: `view_2d_visible_bounds`
- `Crop2DOrigin`
- `Crop2DRightDirection`
- `Crop2DUpDirection`
- `Crop2DMin`
- `Crop2DMax`
- `Crop2DPointCount`
- `Crop2DSource`
- `Crop2DExtremeContributors`
- `CropUsedHostWallFallback`
- `CropFallbackElementIds`
- `CropRegionShapeApplied = false`

### Level-aware Crop Bottom

垂直 crop bottom 使用 wall 所屬 `wall.LevelId`，不可用最近 Level 或幾何高度重新猜測：

```text
levelY = project(Level.Elevation into elevation view 2D frame)
cropBottomBeforeMarginY = min(levelY, curtainGeometryMinY)
cropBottomY = cropBottomBeforeMarginY - verticalMargin
cropTopY = curtainGeometryMaxY + verticalMargin
```

- 一般帷幕且 `verticalMarginMm = 0` 時，底界精確對齊所屬樓層線。
- 帷幕幾何低於樓層線時，底界保留幾何 MinY，避免裁掉負 Base Offset 或其他下伸部分。
- Level、牆中心或 2D frame 無法解析時，fallback 到幾何 MinY，並在 `CropWarnings` 與 `CropBottomBoundaryFallbackReason` 回報。
- `View2DMin` / `CropBottomResolvedYmm` 代表實際 crop bottom；尺寸與 grid reference 搜尋必須使用獨立的 `CurtainGeometryMinY` / `CurtainGeometryMaxY`，不可把樓層線當成尺寸邊界。
- 牆標籤仍以實際 crop bottom 加 `5200 mm` 定位。

`Created[]` 需回傳 `CropBottomBoundarySource`、Level ID/name/elevation、Level view Y、帷幕幾何 MinY/MaxY、margin 前後的 resolved bottom 與 `CropWarnings`。
debug SOP：

- 如果 crop 還是偏大，先跑 `diagnose_curtain_wall_elevation_directions` 並設定 `includeCropDiagnostics = true`。
- 先看 `View2DExtremeContributors` / `Crop2DExtremeContributors`，確認是哪個 element/category/source 造成 Min/Max。
- 若來源是 `view_bbox` 且包含不可見控制點，下一步只針對該 category/element 改用 view geometry points，不再全域改方向或 crop frame。

每張 view 建立後直接設定：

- `CropBoxActive = true`
- `CropBoxVisible = false`
- crop box 水平與頂部貼合帷幕幾何；底部使用 Level-aware 規則，並保留低於樓層線的帷幕幾何
- `VIEWER_BOUND_ACTIVE_FAR = 1`
- `VIEWER_BOUND_FAR_CLIPPING = 2`，也就是 UI 的「剪裁含線」
- `VIEWER_BOUND_OFFSET_FAR = autoDepthFt`

不要直接用 `element.get_BoundingBox(null)` 的 8 個角點當主要 crop 來源。`get_BoundingBox(null)` 是 world-axis-aligned bounding box；斜向帷幕會先被世界 X/Y 軸放大，再投影到立面座標，造成 crop 過寬。

Revit API 對 `View.CropBox` 有一個關鍵限制：設定 crop box 時，input `BoundingBoxXYZ.Transform` 會被忽略，只使用 `Min` / `Max`。因此正式生成不可嘗試改 `CropBox.Transform`，也不可把模型中的 wall-aligned transform 寫回 view。

正確流程是：建立並旋轉 elevation view 後，使用 `view.RightDirection` / `view.UpDirection` 計算帷幕相關元素在立面 2D 畫面中的最小矩形，再把該矩形轉回 Revit 實際 `view.CropBox.Transform` local space，只設定 `CropBox.Min` / `CropBox.Max`。

actual crop frame 語意：

```text
X = screen right
Y = screen up
Z = toward user
```

這代表斜向帷幕的 crop 不能靠 world AABB，也不能靠自訂 rotated BoundingBoxXYZ。業務 crop frame 是 elevation view 2D 畫面座標；Revit crop frame 只作為最後寫回 `Min` / `Max` 的座標系。

曾經嘗試用二次 crop region shape：

```text
ViewCropRegionShapeManager.SetCropShape(wallAlignedRectangleLoop)
```

但實測斜向帷幕可能更偏，因為 wall-aligned loop 投影到 Revit elevation crop plane 後不一定等於 UI 看到的裁切座標。正式生成目前必須停用 `SetCropShape`，回傳：

```text
CropRegionShapeApplied = false
CropRegionShapeFallbackReason = disabled_for_diagnostics
```

後續若要恢復 shape crop，必須先用 crop diagnostics 證明 `CandidateCropLoopIsValid` 且候選 loop 與 Revit 實際 crop plane 一致。

主要點來源應為實際 geometry，且不要把 host wall 本體當 primary source：

- `CurtainGrid.GetPanelIds()`
- `CurtainGrid.GetMullionIds()`
- `Wall.FindInserts(true, true, true, true)`，包含 hosted doors / windows / openings

host wall 只允許在完全沒有 panel / mullion / insert points 時作最後 fallback。這是為了避免 wall join、flip controls、不可見 host geometry 或 model-axis bbox 把斜向帷幕 crop 撐大。

每個元素先用 `element.get_Geometry(new Options { View = view, DetailLevel = Fine, IncludeNonVisibleObjects = false })` 收集：

- `Solid` edge tessellation points
- `Mesh` vertices
- `Curve` tessellated points / endpoints
- `GeometryInstance.GetInstanceGeometry()` 內部 geometry

只有當某個元素無法取得 geometry points 時，才 fallback 到該元素 `get_BoundingBox(null)` 的角點。所有點都必須先投影到 actual view crop frame 後再算 local min/max。

far clip 與 crop 的 X/Y 範圍分開計算。每個目標點的深度為：

```text
candidateDepth = (targetPoint - view.Origin) dot visualLookDirection
autoDepthFt = max(positive candidateDepth) + 50 mm
```

若所有 candidate depth 都為負，改用最大絕對深度加 `50 mm` 並回傳 warning；沒有有效目標點或無法解析視線方向時才使用 `depthMm`。寫入 `VIEWER_BOUND_OFFSET_FAR` 後必須 `Document.Regenerate()`，再讀回 active、mode 與 offset；requested/actual offset 差值必須小於等於 `1 mm`。

`Created[]` 需回傳 crop 診斷欄位：

- `CropPointSource`: `geometry` / `geometry_with_bbox_fallback` / `bbox_fallback`
- `CropPointCount`
- `CropFallbackElementCount`
- `CropFrameSource`: `wall_location_curve` / `revit_crop_transform`
- `CropRightDirection`
- `CropUpDirection`
- `CropDepthDirection`
- `CropLocalMin`
- `CropLocalMax`
- `CropUsedRevitTransformFallback`
- `CropUsedHostWallFallback`
- `CropContributingElementIds`
- `CropFallbackElementIds`
- `CropExtremeContributors`
- `CropRegionShapeApplied`
- `CropRegionShapeFallbackReason`

### Crop Diagnostics

方向診斷工具可加：

```json
{
  "includeCropDiagnostics": true
}
```

批次診斷範例：

```json
{
  "wallIds": [365849],
  "includeTemporaryMarker": true,
  "includeCropDiagnostics": true
}
```

crop diagnostics 在 rollback transaction 內建立 temporary elevation view，讀取後回滾，不留下永久 view / marker。回傳重點：

- `ViewCropTransform`
- `ViewRightDirection`
- `ViewUpDirection`
- `ViewDirection`
- `CropBoxTransform`
- `ViewCropMin`
- `ViewCropMax`
- `WallAlignedFrame`
- `GeometryPointCount`
- `GeometryLocalExtentsInActualCropFrame`
- `GeometryLocalExtentsInViewCropFrame`
- `GeometryLocalExtentsInWallAlignedFrame`
- `ExtremeContributors`
- `PointSourceCountsByCategory`
- `CandidateCropLoopPoints`
- `CandidateCropLoopIsValid`
- `CropRegionShapeManagerAvailable`
- `FallbackElementIds`

若 crop 過大，先看 `FallbackElementIds` 是否有 host wall 或異常 insert bbox 撐大，再決定是否排除該元素或改用 view 內可見 outline。

## View Template Rule

Template name 預設：

```text
帷幕立面
```

解析順序：

1. 找名稱相同且 `View.IsTemplate == true` 的 view template。
2. 找不到時，用第一張成功建立的 elevation view 呼叫 `CreateViewTemplate()`。
3. 將 template 命名為 `帷幕立面`。

### Visible Categories

template 只保留這些 category visible：

| BuiltInCategory | 說明 |
|---|---|
| `OST_Walls` | curtain wall host / wall body |
| `OST_CurtainWallPanels` | curtain wall panels |
| `OST_CurtainWallMullions` | curtain wall mullions |
| `OST_Doors` | 以門族實作的帷幕 panel |
| `OST_Windows` | 以窗族實作的帷幕 panel |
| `OST_Levels` | level lines |
| `OST_WallTags` | wall tags |
| `OST_Dimensions` | 帷幕總尺寸、CurtainGrid 尺寸與樓層偏移尺寸 |
| `OST_Lines` | 尺寸 Reference fallback 使用的不可見 view-specific 細部線 |

### Non-Controlled Template Parameters

template 不控制：

- crop box
- crop region visibility
- far clipping
- far clip offset / depth

因此每張帷幕立面仍可保留自己的 crop box 與 far clip depth。

## Wall Tag Selection and Placement

`create_curtain_wall_elevations` 在任何模型變更前必須取得 `wallTagTypeId`：

- 未提供時回傳 `WorkflowState = "awaiting_wall_tag_type_selection"`、`RequiresUserInput = true`、`NoModelChanges = true`。
- `AvailableWallTagTypes[]` 列出已載入的 `OST_WallTags` `FamilySymbol`，欄位為 `TypeId`、`FamilyName`、`TypeName`、`DisplayName`。
- 提示文字必須包含「應選擇具有[標記]參數的標籤類型」。Revit API 不可靠地公開 tag family label 綁定，因此不自動判斷族內 Label 是否綁定 `Mark`。
- 無已載入類型時回傳 `NextAction = "load_wall_tag_family"`；錯誤或非牆標籤的 ID 必須在 Transaction 前拒絕。

每張成功建立的帷幕立面在尺寸流程完成後，以 `IndependentTag` 建立一個無引線、水平的牆標籤：

```text
viewX = (curtainBoundaryMinX + curtainBoundaryMaxX) / 2
viewY = cropBottomY + 5200 mm
```

標籤 reference 必須指向該立面的目標 curtain wall。`OST_WallTags` 在 View Template 中保持可見，annotation crop 必須關閉；任一已建立立面的標籤失敗時回滾整個 `TransactionGroup`。

批次輸出增加 `WallTagTypeId`、`WallTagTypeName`、`WallTagWarnings`；每個 `Created[]` 增加 `WallTagId`、`WallTagStatus`、`WallTagPositionMm`（view 2D 座標）與 `WallTagWorldPositionMm`。

## Failure Semantics

### Delete Safety Rule

`create_curtain_wall_elevations` 建立的是永久 elevation views，不是 temporary artifact。

硬規則：

- 執行 `create_curtain_wall_elevations` 後，不得自動呼叫 `delete_element`。
- `FL1CW-*`、`{Level}{Mark}`、或 `Created[].ViewName` 回傳的 elevation views 都是成果，不能當作 cleanup target。
- `Created[].IsPersistentOutput = true`、`PersistentViewsCreated = true`、`CleanupRequired = false`、`DeleteGeneratedViews = false` 時，AI client 必須保留這些 views。
- 若使用者沒有明確說「刪除舊帷幕立面」或「清理剛生成的帷幕立面」，不得刪任何既有或新建帷幕 views。
- 若使用者要求清理，必須先列出 view id / view name / source wall id 對應表並取得確認；不能直接批次刪除。

唯一允許的自動刪除是 C# core 內部方向驗證失敗時，刪除同一 transaction 內剛建立但未通過 `DirectionDot >= 0.98` 的 marker/view。這不是 MCP `delete_element`，也不是清理成功成果。

### Command Not Implemented

如果 MCP 回：

```text
未知命令: create_curtain_wall_elevations
```

這通常是 Revit add-in DLL 還沒部署或 Revit 還載入舊 DLL。

檢查：

1. Revit 2024 是否重新啟動過。
2. `%APPDATA%\Autodesk\Revit\Addins\2024\RevitMCP.addin` 的 `<Assembly>` 指向哪裡。
3. 正確部署位置通常是：

```text
%APPDATA%\Autodesk\Revit\Addins\2024\RevitMCP\RevitMCP.dll
```

不是：

```text
%APPDATA%\Autodesk\Revit\Addins\2024\RevitMCP.dll
```

4. 關閉 Revit 後再覆蓋 DLL，因為 Revit 會鎖住已載入的 DLL。

### AI Client Runs `Get-ChildItem`

如果 Antigravity / AI client 看到 `create_curtain_wall_elevations` 後開始跑：

```powershell
Get-ChildItem
```

代表 client 沒有把需求對到 MCP tool。這不是 Revit 幾何問題。

檢查：

1. client 是否開在 repo root：

```text
D:\RevitMCP\REVIT_MCP_study
```

2. `.mcp.json` 是否被 client 載入。
3. `MCP-Server/build/tools/curtain-wall-tools.js` 是否包含 `create_curtain_wall_elevations`。
4. `MCP_PROFILE` 是否是 `full` 或 `architect`。
5. Revit add-in 是否正在 `localhost:8964` listening。

### CreatedCount = 0, SkippedCount > 0

優先讀 `Skipped[].Reason`。

常見原因：

| Reason | 原因 | 處理 |
|---|---|---|
| 找不到可用 placement view | 沒有 active floor plan，也沒有 wall level 對應 floor plan | 指定 `placementViewId` 或建立樓層平面。 |
| 沒有 LocationCurve | wall geometry 不支援 | 檢查該 `WallId`。 |
| 無法取得 BoundingBox | view/document 狀態無法提供 bbox | 檢查模型狀態。 |
| 無法判斷 wall.Orientation | 外側方向不可用 | 檢查該 `WallId`。 |
| 立面方向驗證失敗 | marker 視覺方向無法穩定對到 `markerPoint -> wallMid` | 讀 `DirectionDot`、`DesiredLookDirection`、`FinalVisualLookDirection`，回報模型案例。 |

### CreatedCount > 0 but Views Look Wrong

先看 `Created[].DirectionDot`：

- 接近 `1.0`：方向數學驗證通過，問題可能在 crop、template 或 Revit marker 顯示。
- `< 0.98`：不應出現在 `Created[]`；這代表 tool 有 bug，需修正。

再檢查：

1. marker 是否真的在牆外側。
2. 黑色三角是否看回帷幕牆。
3. crop 是否被 template 控制。
4. far clip 是否被 template 控制。
5. template category 是否誤藏必要元素。

### Direction Debug SOP

方向錯誤時不要盲改 `wall.Orientation`、`ViewDirection` 或 marker rotation。先執行非破壞診斷：

```json
{
  "wallId": 365849
}
```

`diagnose_curtain_wall_elevation_direction` 會在 rollback transaction 中建立暫時 `ElevationMarker` / `ViewSection`，讀取方向後回滾，不留下永久 view 或 marker。

診斷結果重點：
- `WallDirection`
- `WallOrientation`
- `ApiExteriorMarkerPoint`
- `ApiExteriorLookDirection`
- `UiArrowSideCandidate`
- `UiArrowMarkerPoint`
- `InitialViewDirection`
- `InitialVisualLookDirection`
- `TemporaryViewDirection`
- `TemporaryVisualLookDirection`
- `DirectionDot`
- `WouldPassDirectionCheck`

點座標單位為 mm，方向向量為 unitless。`UiArrowSideCandidate` 目前只是診斷假設，不可直接當生成邏輯來源；只有在多個錯誤案例驗證後，才可正式更新 `create_curtain_wall_elevations`。

正式生成結果若方向錯，優先比對：

- `WallFlipped`
- `ResolvedExteriorSide`
- `ResolvedExteriorDirection`
- `WallOrientation`
- 批次診斷中的 `KnownExteriorSide`
- 批次診斷中的 `AutoResolvedCandidate`
- 批次診斷中的 `AutoMatchesKnownExteriorSide`

如果 `AutoMatchesKnownExteriorSide = false`，代表目前 `wall.Flipped` 規則遇到反例，應收集該牆資料並改走 per-wall override。

### Batch Direction Debug SOP

不要用單一道牆推論全域外側規則。當同一模型裡有些帷幕 marker 方向正確、有些錯誤時，先跑批次診斷：

```json
{
  "wallIds": [365849],
  "knownExteriorSideByWallId": {
    "365849": "opposite_orientation"
  }
}
```

`diagnose_curtain_wall_elevation_directions` 是非破壞工具。它會在 rollback transaction 內建立暫時 `ElevationMarker` / `ViewSection`，並且對每道帷幕牆同時比較兩個候選外側：

| Candidate | Meaning |
|---|---|
| `api_orientation` | marker side is `wall.Orientation` |
| `opposite_orientation` | marker side is `-wall.Orientation` |

如果沒有提供 `wallIds`，tool 會診斷目前文件內所有 `CurtainGrid != null` 的 `Wall`。`knownExteriorSideByWallId` 只放使用者已確認的真值；沒有標記時，`Recommendation` 必須維持 `ambiguous_requires_user_label`，不能自動假設全部牆都用同一側。

批次診斷會額外回傳 `AutoResolvedCandidate`，使用與正式生成相同的 `wall.Flipped` 規則：

```text
Flipped = true  -> api_orientation
Flipped = false -> opposite_orientation
```

若有提供 `knownExteriorSideByWallId`，`AutoMatchesKnownExteriorSide` 應為 `true`。目前固定 regression samples：

| WallId | Flipped | KnownExteriorSide |
|---:|---:|---|
| 365849 | false | `opposite_orientation` |
| 366415 | true | `api_orientation` |
| 366541 | true | `api_orientation` |
| 365321 | false | `opposite_orientation` |
| 365569 | false | `opposite_orientation` |

後續若要再修改正式生成邏輯，至少要保留這些 regression labels：

- `365849 = opposite_orientation`
- 兩道目前生成方向正確的牆
- 兩道目前生成方向錯誤的牆

如果未來 regression labels 顯示 `wall.Flipped` 規則不穩定，下一步應該做 per-wall override，而不是繼續猜新的全域外側 heuristic。

## Manual Verification Checklist

執行 tool 後檢查：

- 每道 `CurtainGrid != null` 的 curtain wall 產生一張 elevation view。
- view name 符合 `{LevelName}{nameSeparator}{Mark}`。
- mark 空白 fallback 為 `CW-{ElementId}`。
- 重名 fallback 為 `_2`, `_3`。
- marker 在牆外側。
- 黑色三角方向看回帷幕牆。
- `Created[].DirectionDot` 應接近 `1.0`，至少 `>= 0.98`。
- View Template 名稱為 `帷幕立面`。
- template 只顯示 walls、curtain wall panels、curtain wall mullions、doors、windows、levels、wall tags、dimensions、lines。
- 直線帷幕的 crop box 左右界貼合 `Wall.LocationCurve` 投影端點；頂界貼合帷幕可視幾何，底界符合 `min(Level Y, curtain geometry MinY) - verticalMargin`。
- far clip mode 是「剪裁含線」。
- crop box / far clip depth 不被 template 鎖住。
- 每張成功立面恰有一個指向目標 curtain wall 的無引線水平牆標籤，位置為 resolved crop bottom + `5200 mm`。
- 帷幕底高於 Level 超過 `1 mm` 時，左側總高鏈同時顯示 Level 至帷幕底與帷幕總高；距離小於等於 `1 mm` 時不增加 Level 段；帷幕底低於 Level 時在總高尺寸外側建立獨立 Level offset 尺寸。
- `Skipped[]` 有具體 reason，沒有 silent failure。

## 尺寸標示規則

`create_curtain_wall_elevations` 預設以 `addDimensions = true` 建立四組 Revit `Dimension`：

- 上方內層：垂直帷幕網格投影形成的水平尺寸鏈。
- 上方外層：總寬。
- 左側內層：水平帷幕網格投影形成的垂直尺寸鏈。
- 左側外層：總高。

### 樓層線至帷幕底部尺寸

左側總高尺寸使用帷幕實際幾何底部、頂部與 `wall.LevelId` 的 Level Reference：

- 帷幕底高於樓層線超過 `1 mm`：在同一總高尺寸鏈加入樓層線，顯示樓層至帷幕底與帷幕總高兩段。
- 絕對距離小於等於 `1 mm`：視為零，不加入樓層距離段。
- 帷幕底低於樓層線超過 `1 mm`：保留原總高尺寸，並在其左側再偏移一個 `dimensionStackOffset` 建立獨立尺寸。
- Level 幾何 Reference 必須使用 `Level.GetPlaneReference()`，並驗證 stable representation 可 round-trip；禁止使用 `new Reference(level)`。
- Level plane Reference 無法使用或被 `NewDimension` 拒絕時，在投影後的 Level Y 建立 view-specific 細部線，強制套用 `OST_InvisibleLines`，再使用其幾何 Reference。
- 無法取得或套用 Invisible Lines 時必須刪除輔助線並保留原總高，不可留下可見細部線。
- 同鏈尺寸 commit 後驗證失敗時，回退為原總高加外側獨立尺寸，樓層端改用不可見細部線 Reference；獨立樓層距離尺寸驗證失敗時也採相同修復。
- `wall_level_plane_reference` 且為 `level_offset` 或包含 Level offset 的增強總高鏈時，Revit 若在 commit 後回傳 `AreReferencesAvailable = false`，只有 Dimension 存在、OwnerView、Reference 數量及 segment values（容差 `0.5 mm`）全部正確才保留，validation mode 為 `level_plane_segment_validation`；API 原始 false 值照實回傳。
- 上述例外不得套用至一般總高、總寬、CurtainGrid 或其他 geometry／DetailCurve 尺寸，這些尺寸維持 strict `AreReferencesAvailable` 驗證。
- Level offset 的 Invisible Lines 備援可額外按 `GraphicsStyleCategory.Id == OST_InvisibleLines` 尋找 Projection style；共用尺寸 fallback 的線型解析行為不變。
- `LevelOffsetDimensionReferenceSource` 區分 `wall_level_plane_reference` 與 `invisible_detail_curve_fallback`。
- Level Reference 解析與細部線建立只需要 `Document.Regenerate()`，不要求為此切換 `ActiveView`；既有 CurtainGridLine 原生 Reference 流程仍可依其自身需求啟用目標視圖。

`Created[]` 回傳 `CurtainBottomToLevelDistanceMm`、`LevelOffsetDimensionMode`、`LevelOffsetDimensionElementId`、`LevelOffsetDimensionStatus`、`LevelOffsetDimensionAreReferencesAvailable` 與 `LevelOffsetDimensionReferenceSource`。

直線帷幕的水平尺寸鏈以 `Wall.LocationCurve` 兩個端點投影至立面 `RightDirection` 後的原始邊界為準，不使用 panel、mullion 或 insert bounding box，也不包含 `horizontalMarginMm`。crop 左右界才會在原始邊界外加 `horizontalMarginMm`；預設為 `0` 時，crop 與帷幕左右端點一致。原生 geometry reference 必須位於端點 `1 mm` 內；找不到時使用端點座標建立 Invisible detail curve reference，不退回最近的豎框外緣。非直線 LocationCurve 保留既有可視幾何範圍並回傳 fallback 原因。

尺寸線的垂直座標仍以立面的 `UpDirection` 與已驗證的 `Crop2DMin`／`Crop2DMax` 為基準。帷幕可視邊界至內層網格尺寸線的模型間距，優先採用（DimensionType 的「輔助線長度」+ 圖紙 `3 mm`）乘以立面視圖比例；內層網格尺寸線至外層總尺寸線的模型間距，採用輔助線長度乘以立面視圖比例。只有無法讀取有效輔助線長度時，才分別使用 `dimensionOffsetMm`（預設模型距離 `300 mm`）與 `dimensionStackOffsetMm`（預設模型距離 `250 mm`）作為 fallback，並在回傳結果中說明來源與原因。即使某方向沒有足夠網格而略過網格尺寸，總尺寸仍保留在固定外層位置。

DimensionType 解析順序：

1. `dimensionTypeId`
2. `dimensionTypeName`
3. 本次 RevitMCP process 最近成功使用的帷幕立面標註類型
4. Revit 預設線性標註類型
5. 第一個可用的 `DimensionType`

`dimensionTypeSelectionMode = prompt` 且未提供類型時，工具回傳 `awaiting_dimension_type_selection`，不建立任何視圖。自動模式找不到類型時仍建立立面，並在 `DimensionWarnings` 回報。

Reference 策略：

- 總寬與總高只接受帷幕面板、豎框、門窗或其他插入物的真實 geometry reference。
- 網格尺寸優先使用 `CurtainGridLine` 與帷幕元素的真實 reference。
- 網格 reference 不足或 Revit 拒絕建立時，才建立套用 Invisible 線型的 detail curve 作為 fallback。
- 回傳欄位會標示 `geometry_reference`、`detail_curve_fallback_from_curtain_grid_coordinates`、`skipped` 或 `failed`。

除錯時使用 `diagnose_curtain_wall_elevation_dimensions`。預設 `rollback = true`，測試產生的尺寸、ReferencePlane 與 detail curve 都不會留在模型。應檢查 `AttemptedDimensions`、`VerifiedDimensionIds`、`OwnerViewId` 與 `Failures`，不要只依賴 API 未拋出例外就判定成功。


### 樓層偏移尺寸 Runtime 回歸測試

`testMode = "level_offset"` 必須指定 `wallId`，或在目標立面只選取一道 curtain wall；此模式禁止退回模型第一道帷幕牆。`viewId` 未提供時，目標視圖固定使用目前作用中的 elevation view。

測試以 production crop、view frame、帷幕底／頂 geometry Reference、`wall.LevelId` 與解析後的 DimensionType／尺寸堆疊間距建立資料，並比較 target view 未啟用與啟用兩種狀態。每種狀態依序測試：

- `Level.GetPlaneReference()` + 帷幕底的兩 Reference 尺寸。
- Level + 帷幕底 + 帷幕頂的三 Reference 尺寸鏈。
- `OST_InvisibleLines` 細部線 + 帷幕底的兩 Reference fallback。
- 細部線 + 帷幕底 + 帷幕頂的三 Reference fallback 尺寸鏈。
- 完整 production 建立與 post-commit repair 流程。

每個 raw attempt 都在已 commit 的 inner transaction 後讀取 `AreReferencesAvailable`、Reference 數量與 segment values，並回傳 Reference element ID、`ElementReferenceType`、stable representation round-trip、OwnerViewId、線型 read-back、`PostCommitValidationMode`、例外與 `FailureStage`。production attempt 同步回傳 validation mode、預期／實際 segment values 與既有 `LevelOffsetDimension*` 診斷欄位。

整個測試固定包在 outer `TransactionGroup` 並最終 rollback；呼叫端傳入的 `rollback` 不得關閉此保護。回傳 `RollbackCleanupPassed` 驗證所有暫時 Dimension 與 DetailCurve 已不存在。後續 production 修正只能依 `FirstFailure` 或 `FirstProductionFailure` 的第一個失敗 assertion 決定。



## Boundaries

這個 workflow 不處理：

- linked model curtain wall
- 自動修正 wall orientation
- sheet 排版
- 帷幕表編號
- panel / mullion 統計表
