---
name: beam-slab-alignment
description: "Revit StructuralFraming 樑頂貼齊樓板底工作流程。當使用者提到降樑、梁/樑貼齊樓板底、beam top to floor bottom、起始樓層偏移、結束樓層偏移、UB-通用樑，或需要依樓板底批次調整樑偏移時使用。優先以 align_beams_top_to_floor_bottom 進行 dry-run 驗證，並遵循 domain/beam-slab-alignment.md。"
---

# 樑頂貼齊樓板底

此 Skill 用於既有 Revit StructuralFraming 樑，透過寫入 `起始樓層偏移`、`結束樓層偏移` 等例證偏移參數，將樑頂降至或調整至正確樓板底。

變更模型資料前，必須先閱讀 `domain/beam-slab-alignment.md`。該 Domain 定義選板規則與已知校正案例。

## 工作流程

1. 執行指令前，先確認 RevitMCP 已連線。
2. 先查詢樓板候選，並把目標限制為真正的結構樓板。本專案預設只使用名稱包含 `樓板` 或以 `RC_` 開頭的 Floor 元素，除非使用者明確擴大候選集合。
3. 以 `dryRun=true` 執行 `align_beams_top_to_floor_bottom`。
4. 檢查每個預計調整的樑之 `StartFloor` 與 `EndFloor`。一般情況下，同一根樑兩端應使用同一片樓板作為共同目標。
5. 只有在校正案例或使用者指定的樣本樑選到預期樓板後，才進行寫入。
6. 寫入後再跑一次最終 dry-run。成功狀態是 `PlannedCount = 0`，或剩餘項目皆有可接受的 skip reason。

## 必要工具設定

除非使用者另有要求，使用以下預設值：

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

診斷時可以暫時提高 `maxDeltaMm`，用來預覽目標樓板；但不要在未確認的情況下套用大幅度位移。

## 選板規則

目標樓板不是單純取最近樓板，也不是取模型中面積最大的樓板。

依 `domain/beam-slab-alignment.md` 的規則執行：

- 沿樑長方向取樣。
- 依 Floor ElementId 分組樓板底命中結果。
- 將 sample-hit 數最高的群組視為此樑的主要樓板覆蓋範圍。
- 在該主要覆蓋群組內，選擇相對自身 Revit Level 樓板底偏移最低的樓板。
- 將選定樓板作為起點與終點共同目標。

## 安全注意事項

- 此流程的 Revit 指令必須循序執行，不要平行執行會修改或檢查此流程的 WebSocket 指令。
- 若需要修改 DLL，使用既有 build/deploy 流程。遇到 DLL lock 時立即停止，請使用者關閉 Revit 後再部署。
- 選板規則變更後，不要直接套用全棟；至少要先通過一個使用者提供的校正樑 dry-run。

## 參考資料

- `domain/beam-slab-alignment.md`
- `domain/lessons.md`
- `MCP/Core/Commands/CommandExecutor.StructuralFraming.cs`
- `MCP-Server/src/tools/structural-tools.ts`
