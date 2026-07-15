---
name: ifc-structural-sync
description: "IFC 結構同步至 Revit 原生結構構架與結構柱的工作流程。當使用者提到 IFC 結構模型、依 IFC 建立梁柱、sync_ifc_structural_to_native、StructuralFraming、StructuralColumns、UB-通用樑、AE-鋼柱、SHS/SHC/RC 柱判斷、b/h 參數顛倒、柱頂或梁頂貼齊樓板底、replaceExisting 重建、IFC_STRUCT_SYNC 註記時使用。"
---

# IFC Structural Sync

先讀 `domain/ifc-structural-native-sync.md`，再執行任何同步或修正。需要歷史背景時再讀 `domain/ifc-structural-sync.md`；若需求涉及梁頂貼樓板底，也讀 `domain/beam-slab-alignment.md`。

## 操作順序

1. 先用 `get_linked_models` 確認目前專案實際的 `LinkInstanceId`。不要沿用前一個 Revit 專案或前一輪對話的連結 ID。
2. 先 dry-run `sync_ifc_structural_to_native`，至少帶入 `linkInstanceId`、`includeFraming`、`includeColumns`、`columnCategory`、`replaceExisting`、`alignColumnTopsToFloorBottom`、`autoColumnBaseType`、`sizeRoundMm`。
3. 檢查 dry-run 的 `TypePlan`、`PlannedCounts`、`ColumnTopAlignment`、`Samples`。確認柱 b/h、梁型名稱、貼板目標、skip reason 都合理後才 `apply=true`。
4. 若要修正已建立的 IFC 同步元素，使用 `replaceExisting=true`。它會用 `IFC_STRUCT_SYNC|Link:<id>|Kind:<kind>|Source:<id>` 刪除並重建同源元素。
5. apply 後必須回讀驗證。用 `query_elements_with_filter` 查 `備註 contains Source:<sourceId>`，再用 `get_element_info` 或 `get_element_geometry` 驗證類型名稱、b/h、BoundingBox、樓層偏移。

## 建立結構構架

- IFC 梁常沒有可靠 `LocationCurve`。同步工具應從 linked geometry 讀 solids/edges，估算主軸，建立 Revit `StructuralFraming`。
- 原生梁以 `UB-通用樑` 或使用者指定的 `baseFramingType` 為 duplication base。
- 梁類型名稱以截面參數生成，例如 `IFC-BEAM-H{h}xB{b}`，有板厚時包含 `s/r`。
- 梁應設定 `起始樓層偏移` 與 `結束樓層偏移`。要貼樓板底時用 `align_beams_top_to_floor_bottom`，優先 `floorSelectionMode="auto_by_beam"`、`alignWhenTopAboveFloorBottom=true`、`disallowJoinsBeforeAlign=true`、`postAlignGeometryCorrection=true`。
- 梁若有斜板，工具應自行偵測斜率並讓起終點各取對應樓板底 Z；水平板則共用水平目標。

## 建立結構柱

- IFC 柱同步到 Revit `StructuralColumns`，以 `AE-鋼柱`、`SHS-正方形空心剖面-柱`、或 RC 方柱 base type 重建，不要把所有柱都硬套同一族。
- 柱截面 b/h 不可直接綁全域 X/Y。已驗證規則是 `b=短邊`、`h=長邊`，類型名稱為 `IFC-COL-H{h}xB{b}`。例：IFC 實體平面 400x700 應建立 `IFC-COL-H700xB400`，參數 `b=0.4`、`h=0.7`。
- SHS/RC 判斷要看幾何是否實心。近似實心方柱不可只因為方形就歸類 SHS；需要使用 solid volume ratio 輔助判斷。
- 柱頂必須貼齊樓板底。用 `alignColumnTopsToFloorBottom=true` 建立，或用 `align_columns_top_to_floor_bottom` 批次修正既有柱。工具必須在柱截面範圍內多點取樣，避免柱中心落在板邊或洞口時找不到樓板。

## 驗證與避坑

- MCP 工具表可能比已 build 的 TypeScript 舊。若新工具未出現，重啟 MCP Server/Codex MCP 連線，不要只重開 Revit。
- Revit DLL 被鎖時停止部署，等使用者關閉 Revit 後再複製 DLL。
- 批次 apply 可能超過 30 秒工具等待視窗。timeout 後不要假設失敗；先用查詢工具回讀 ElementId、備註、數量與幾何。
- 所有具體 ElementId、數量、座標、尺寸都必須來自本 turn 的工具回應。

## 參考

- `domain/ifc-structural-native-sync.md`
- `domain/ifc-structural-sync.md`
- `domain/beam-slab-alignment.md`
- `domain/lessons.md`
- `MCP/Core/Commands/CommandExecutor.IfcStructuralSync.cs`
- `MCP/Core/Commands/CommandExecutor.StructuralFraming.cs`
- `MCP/Core/Commands/CommandExecutor.StructuralColumns.cs`
- `MCP-Server/src/tools/structural-tools.ts`
