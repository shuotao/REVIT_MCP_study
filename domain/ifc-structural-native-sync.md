---
name: ifc-structural-native-sync
description: "IFC 結構模型轉 Revit 原生 StructuralFraming 與 StructuralColumns 的同步 SOP，涵蓋梁柱建立、b/h 參數、柱/梁貼樓板底、replaceExisting 重建與驗證流程。"
metadata:
  version: "1.0"
  updated: "2026-06-23"
  created: "2026-06-23"
  contributors:
    - "Codex"
  references:
    - "MCP/Core/Commands/CommandExecutor.IfcStructuralSync.cs"
    - "MCP/Core/Commands/CommandExecutor.StructuralFraming.cs"
    - "MCP/Core/Commands/CommandExecutor.StructuralColumns.cs"
    - "MCP-Server/src/tools/structural-tools.ts"
  related:
    - "ifc-structural-sync.md"
    - "beam-slab-alignment.md"
    - "lessons.md"
  referenced_by: []
  tags: [IFC, Revit, StructuralFraming, StructuralColumns, native sync, beam, column, b/h, slab underside, replaceExisting]
---

# IFC 原生結構梁柱同步 SOP

本文件記錄 IFC 結構模型轉 Revit 原生結構構架與結構柱的實作流程，以及 2026-06 小水力機械作業班同步過程中踩過的坑。後續遇到 IFC 結構同步，先讀本文件，再執行工具。

## 1. 核心原則

- IFC linked element 不一定有可靠 `LocationCurve`。梁柱都應優先從 solids/edges/BoundingBox 取得幾何，不要假設 Revit location 完整。
- Revit 原生元素必須使用 `StructuralFraming` 與 `StructuralColumns`，不要用 DirectShape 取代。
- 所有建立元素都要寫入 `IFC_STRUCT_SYNC|Link:<linkId>|Kind:<BEAM|COLUMN>|Source:<sourceId>|IfcGUID:<guid>`，讓後續 `replaceExisting` 可重建同源元素。
- 先 dry-run，確認 `TypePlan`、`Samples`、`ColumnTopAlignment`，再 apply。
- apply 後必須回讀驗證；ElementId 會因 `replaceExisting=true` 改變，驗證應以 `Source:<sourceId>` 追蹤。

## 2. 標準流程

1. 使用 `get_linked_models` 取得目前專案真正的 `LinkInstanceId`。使用者切換新專案後必須重查。
2. 使用 `sync_ifc_structural_to_native` dry-run：

```json
{
  "linkInstanceId": "<from get_linked_models>",
  "includeFraming": true,
  "includeColumns": true,
  "framingCategory": "StructuralFraming",
  "columnCategory": "Columns",
  "dryRun": true,
  "apply": false,
  "replaceExisting": false,
  "autoColumnBaseType": true,
  "alignColumnTopsToFloorBottom": true,
  "maxColumnTopSearchDistanceMm": 6000,
  "sizeRoundMm": 1,
  "batchSize": 100
}
```

3. 若 dry-run 合理，正式 apply。若要修正已建錯的元素，改為 `replaceExisting=true`。
4. 若 apply timeout，先回讀模型，不要直接重跑。Revit 交易可能已完成，只是 MCP 等待視窗到期。
5. 對使用者指出的代表元素做精查，再檢查整批數量與錯誤類型是否殘留。

## 3. 建立結構構架

IFC 梁建立流程：

- 從 linked IFC element 讀 solids/edges。
- 估算主軸，建立 local frame。
- 以主軸方向建立 Revit `Line`。
- 以 local side/up 範圍推估截面 `b/h`。
- 以 `UB-通用樑` 或使用者指定 `baseFramingType` duplication 建立類型。
- 建立 `FamilyInstance(line, symbol, level, StructuralType.Beam)`。
- 寫入 `起始樓層偏移`、`結束樓層偏移`，必要時再跑梁頂貼板工具。

梁型命名與參數：

- 類型名稱：`IFC-BEAM-H{h}xB{b}`。
- 若 IFC 有 `s/r` 或 `tw/tf`，類型名稱與類型參數要一併保留。
- `b/h/s/r` 應是類型參數，不應依每根梁寫成 instance 參數。

梁頂貼樓板底：

```json
{
  "floorSelectionMode": "auto_by_beam",
  "beamSampleCount": 9,
  "alignWhenTopAboveFloorBottom": true,
  "disallowJoinsBeforeAlign": true,
  "postAlignGeometryCorrection": true,
  "preserveVerticalStacks": true,
  "toleranceMm": 5,
  "maxSearchDistanceMm": 6000,
  "maxDeltaMm": 6000
}
```

梁的踩坑：

- 只依平面建立梁會漏掉立面/剖面梁。
- 只選最近樓板會選錯板，應沿梁多點取樣判斷主要覆蓋樓板。
- 斜板要自動讓起終點跟著斜，不應等使用者逐次提醒。
- 梁凸出樓板時，Revit join/cutback 可能干擾幾何，貼板前可先 disallow joins。
- 堆疊梁只以最上方梁貼板，其餘梁等量偏移。

## 4. 建立結構柱

IFC 柱建立流程：

- 從 linked IFC element 讀 solids/BoundingBox。
- 依幾何中心建立 `FamilyInstance(location, symbol, baseLevel, StructuralType.Column)`。
- 寫入 base/top level 與 offset。
- 依幾何與參數選擇鋼柱、SHS 或 RC base family。
- 建立時可直接 `alignColumnTopsToFloorBottom=true`，讓柱頂貼樓板底。

柱族判斷：

- 一般鋼柱：使用 `AE-鋼柱` 或 `baseSteelColumnType`。
- SHS：使用 `SHS-正方形空心剖面-柱` 或 `baseShsColumnType`。
- RC 方柱：如果幾何接近實心，不可歸類為 SHS，應使用 RC 方柱 base type。
- 方柱分類要看 solid volume ratio；不能只因為截面方形就判斷為 SHS。

柱 b/h 規則：

- 不可把 global X/Y 直接當作 `b/h`。
- 已驗證規則：`b=截面短邊`、`h=截面長邊`。
- 類型名稱：`IFC-COL-H{h}xB{b}`。
- 代表案例：IFC 實體平面 400mm x 700mm 應建立 `IFC-COL-H700xB400`，參數 `b=0.4`、`h=0.7`。

柱頂貼樓板底：

- 建立時用 `alignColumnTopsToFloorBottom=true`。
- 既有柱修正用 `align_columns_top_to_floor_bottom`。
- 不可只用柱中心射線找樓板。邊柱、洞口邊、斜板三角面邊界都可能使中心點打不到樓板。
- 應在柱 BoundingBox 平面範圍內多點取樣：中心、四邊、四角內縮點。
- 優先使用樓板 bottom face 幾何插值。斜板不可只用樓板 BoundingBox。

柱貼板驗證參數：

```json
{
  "columnIds": ["<column id>"],
  "floorIds": ["<optional floor id>"],
  "dryRun": true,
  "apply": false,
  "setTopAttachment": true,
  "postGeometryCorrection": true,
  "toleranceMm": 5,
  "maxDeltaMm": 6000,
  "maxSearchDistanceMm": 6000
}
```

判斷結果：

- `CanAlign=true` 才可套用。
- `Message=geometry-bottom-face` 代表使用幾何面命中，優於 BoundingBox fallback。
- `FinalResidualMm=0` 或在 tolerance 內，才算貼齊。
- `TopAttachmentSet=false` 不必然失敗；若 `FinalResidualMm=0`，代表 top level/offset 已讓幾何貼齊。

## 5. 回讀驗證

常用查詢：

```json
{
  "category": "StructuralColumns",
  "filters": [
    {
      "field": "備註",
      "operator": "contains",
      "value": "Source:<sourceId>"
    }
  ],
  "returnFields": ["備註", "標記", "b", "h", "類型", "族群與類型", "頂部偏移", "基準偏移"],
  "maxCount": 10
}
```

必查項目：

- 同源新元素是否存在。
- 舊 ElementId 是否因 `replaceExisting=true` 消失。
- 類型名稱與 b/h 是否一致。
- `get_element_geometry` BoundingBox 是否與 b/h 對得上。
- 整批同步數量是否與 dry-run planned count 一致。
- 錯誤舊類型是否殘留，例如 `H400xB700`。

## 6. 工具與部署坑

- MCP 工具 schema 已 build 不代表工具表已刷新；如果新工具查不到，需要重啟 MCP Server/Codex MCP 連線。
- Revit 開啟時 DLL 會被鎖住；部署失敗就停止，等使用者關閉 Revit。
- 批次 apply 可能 timeout；先回讀再判斷，不要直接重跑。
- 不要手寫 raw WebSocket JSON 繞過 MCP Server。
- 所有 ElementId、數量、尺寸、樓板 ID 都必須來自當 turn 工具回應。
