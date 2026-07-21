---
name: beam-slab-alignment
description: "Revit 既有結構樑降樑貼齊樓板底 SOP：使用 StructuralFraming 樑的起始樓層偏移、結束樓層偏移，依主要樓板覆蓋範圍與樓板相對樓層最低偏移選定目標樓板，支援 dry-run、校正案例與全棟批次調整。"
metadata:
  version: "1.0"
  updated: "2026-06-09"
  created: "2026-06-09"
  contributors:
    - "Codex"
  references:
    - "MCP/Core/Commands/CommandExecutor.StructuralFraming.cs"
    - "MCP-Server/src/tools/structural-tools.ts"
  related:
    - "ifc-structural-sync.md"
    - "tool-capability-boundary.md"
    - "session-context-guard.md"
    - "lessons.md"
  referenced_by: []
  tags: [Revit, StructuralFraming, beam, slab underside, floor bottom, offset, dry-run, 降樑, 樑, 梁, 樓板底, 起始樓層偏移, 結束樓層偏移]
---

# 樑頂貼齊樓板底 SOP

## 目的

當既有 Revit StructuralFraming 樑需要透過寫入例證偏移參數，使樑頂貼齊正確樓板底時，使用本 SOP。

本 SOP 適用於模型中已存在的原生樑，不處理 IFC 轉換。IFC 或 linked model 的轉換流程仍歸 `ifc-structural-sync.md` 管理。

## 適用範圍

當使用者提到下列需求時，使用此流程：

- 降樑、梁/樑貼齊樓板底、樑頂貼齊樓板底
- `起始樓層偏移`、`結束樓層偏移`
- UB-通用樑、通用樑、名稱含「樑」的 StructuralFraming
- beam top to floor bottom、slab underside alignment

## 目標參數

工具會寫入樑的例證參數：

- Start offset: `起始樓層偏移`，fallback `起點樓層偏移`、`Start Level Offset`
- End offset: `結束樓層偏移`，fallback `終點樓層偏移`、`End Level Offset`

除非使用者明確要求，否則不要為此流程建立新參數。優先使用既有可寫入的例證參數。

## 樓板候選過濾

執行貼齊前，先查詢 Floor 元素，並把目標樓板限制為真正的結構樓板。

本專案目前預設：

- 納入名稱包含 `樓板` 的 Floor
- 納入名稱以 `RC_` 開頭的 Floor
- 排除完成面、包板、面板，以及其他雖屬 Floor 類別但不是結構樓板的元素，除非使用者明確指定納入

此過濾可避免指令把樑對齊到完成面或立面板等 Floor 類別物件。

## 選板規則

正確樓板不是最近的樓板命中，也不是整個模型裡面積最大的樓板。

對每一根樑執行：

1. 沿樑的 LocationCurve 取樣，並從兩端向內縮一段距離。
2. 每個取樣點垂直投射，收集搜尋距離內有效的樓板底命中。
3. 依 Floor ElementId 分組命中結果。
4. 以 sample-hit 數最高的群組作為主要樓板覆蓋範圍。
5. 在主要覆蓋群組內，選擇樓板底相對自身 Revit Level 偏移最低的樓板。
6. 將這一片選定樓板同時作為該樑起點與終點的目標樓板。

排序條件：

1. `LevelOffsetMm` 最低
2. 樓板面積較大
3. 與目前樑頂距離較短

這個規則反映使用者意圖：樑應歸屬於它跨越的主要樓板範圍；在等效的樓板範圍候選中，應貼齊相對樓層較低的樓板底。

## 指令設定

使用 `align_beams_top_to_floor_bottom`，並採用：

```json
{
  "floorSelectionMode": "auto_by_beam",
  "beamSampleCount": 9,
  "toleranceMm": 5,
  "maxSearchDistanceMm": 3000,
  "maxDeltaMm": 1000,
  "requireBothEnds": true,
  "alignWhenTopAboveFloorBottom": true,
  "disallowJoinsBeforeAlign": true,
  "postAlignGeometryCorrection": true
}
```

先使用 `dryRun=true`。只有在目標樓板驗證通過後，才使用 `apply=true`。

診斷時可暫時提高 `maxDeltaMm` 來檢查預期目標；未經使用者確認，不要套用大幅度位移。

`alignWhenTopAboveFloorBottom=true` 表示只要樑頂仍高於目標樓板底，即使尚未超過樓板頂，也會被視為需要降至樓板底。這比只檢查「是否凸出樓板頂」更符合真正貼附樓板底的需求。

`disallowJoinsBeforeAlign=true` 會在實際套用前先取消 StructuralFraming 兩端接合，避免 Revit 的端部接合、剪裁或延伸影響可見幾何。dry-run 不會改變接合狀態。

`postAlignGeometryCorrection=true` 會在寫入起始/結束偏移後重新取樣實際樑幾何與樓板底；若仍有局部凸出，會在 `maxGeometryCorrectionMm` 安全上限內追加向下補償。此步驟用於處理形狀編輯樓板、斜板局部高度與梁端接合造成的殘餘誤差。

## 校正案例

下列案例定義本專案的預期行為：

| Beam ElementId | Expected Floor ElementId | 規則意義 |
|---:|---:|---|
| 8546314 | 8693275 | 主要樓板覆蓋範圍應解析到相對樓層較低的樓板底，而不是較大的相鄰樓板。 |
| 8543272 | 8115865 | 沿樑取樣後，起點與終點應共用同一片選定樓板。 |
| 8541251 | 8103066 | 類似樑應優先選擇主要覆蓋候選中相對樓層樓板底偏移最低者。 |

任何選板邏輯變更後，在執行全棟前，必須先 dry-run 這些案例或使用者最新指定的校正樑，並確認預期 FloorId。

## 執行流程

1. 確認 RevitMCP 已連線。
2. 查詢 Floor 元素，建立過濾後的 floor ID 清單。
3. 對使用者提供的校正樑執行 dry-run。
4. 檢查 `StartFloor.FloorId`、`EndFloor.FloorId`、`SampleHitCount`、`AreaM2`、`LevelOffsetMm`。
5. 只套用已確認的樑集合；若要全棟套用，需先取得使用者同意。
6. 最後再跑一次 dry-run。完成狀態應是處理過的樑 `PlannedCount = 0`，或只剩明確可接受的 skip reason。

## 安全規則

- 此流程的 WebSocket/Revit 指令必須循序執行。
- 每次指令執行使用單一 Revit Transaction。
- 全棟執行時保留 `maxDeltaMm` 作為安全上限。
- 若必須重新部署 DLL，且複製時因 lock 或 access denied 失敗，立即停止並請使用者關閉 Revit 後再重試。
- 不得悄悄把選板規則切回 nearest-floor 或 whole-model largest-area 行為。

## 已知失敗模式

最近樓板失敗：
射線可能命中附近樓板、完成面或相鄰樓板，但那些不一定是預期的結構樓板範圍。

最大面積失敗：
模型中或附近區域面積最大的樓板可能是上方或相鄰樓板；若選到它，會產生錯誤端點偏移。

端點分裂失敗：
若起點與終點獨立選板，同一根樑可能被對齊到兩片不同樓板。主要覆蓋共用目標可避免此問題，除非使用者明確要求 split-end 行為。

候選污染：
Floor 類別中的非樓板元素可能贏得射線命中。執行貼齊前必須使用樓板名稱過濾。

接合剪裁殘留：
即使偏移參數已寫入，StructuralFraming 端部接合仍可能讓實際可見幾何被延伸或剪裁到樓板上方。套用時預設先不允許接合，再以實際幾何取樣做殘餘補償。

形狀編輯樓板局部高度失準：
斜板或形狀編輯樓板不得用整體 bounding box 或三角面平均高程代表局部樓板底。工具應以取樣點所在三角面的平面內插 Z 值判定樓板頂/底。
