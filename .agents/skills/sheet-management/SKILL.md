---
name: sheet-management
description: "圖紙與視埠管理：批次建立圖紙、自動修正圖號衝突、語意化重新排序、依網格裁切建立從屬視圖，以及依視圖比例同步圖紙視埠標題類型。觸發條件包含：圖紙、圖號、titleblock、viewport、renumber、重新編號、從屬視圖、dependent view、分區出圖、網格裁切、視圖比例、視埠類型。工具：get_all_sheets、get_titleblocks、create_sheets、auto_renumber_sheets、get_viewport_map、calculate_grid_bounds、create_dependent_views、get_viewport_types、sync_viewport_types_by_view_scale。"
---

# 圖紙與視埠管理

## 可用工具

| 工具 | 用途 |
|------|------|
| `get_all_sheets` | 取得目前專案中的圖紙清單。 |
| `get_titleblocks` | 取得可用圖框族型。 |
| `create_sheets` | 批次建立圖紙。 |
| `auto_renumber_sheets` | 自動修正圖號衝突並重新排序。 |
| `get_viewport_map` | 取得圖紙、視圖與視埠對應關係。 |
| `calculate_grid_bounds` | 依指定 X/Y 網格範圍計算 BoundingBox。 |
| `create_dependent_views` | 批次建立從屬視圖並套用裁切範圍。 |
| `get_viewport_types` | 列出專案中可用的視埠標題類型。 |
| `sync_viewport_types_by_view_scale` | 依已放置視圖比例預覽或套用視埠類型變更。 |

## Workflow 1：批次建立圖紙

1. 執行 `get_titleblocks`，確認 `titleBlockId`。
2. 執行 `get_all_sheets`，確認既有圖紙編號與命名狀態。
3. 執行 `create_sheets`，傳入 `titleBlockId` 與圖紙資料 `[{number, name}]`。

## Workflow 2：圖號衝突與重新編號

1. 執行 `get_all_sheets`，找出帶有 `-1` 或其他衝突尾碼的圖紙。
2. 執行 `auto_renumber_sheets` 前，先用 dry-run 或預覽資料確認排序邏輯。
3. 保留圖紙系列、樓層、分張序號等語意，不要只做字串排序。
4. 若圖號已由使用者手動修正，後續對應應優先使用圖紙名稱，而不是舊圖號。

## Workflow 3：依網格裁切建立從屬視圖

1. 執行 `calculate_grid_bounds`，傳入 `xGrids`、`yGrids` 與 `offset_mm`。
2. 執行 `create_dependent_views`，傳入母視圖 ID 與 BoundingBox 清單。
3. 命名時依既有專案規則建立 `(1/4)`、`(2/4)` 等分張名稱。
4. 建立後若需要放置到圖紙，應以參考樓層/參考圖紙上的視埠座標進行對齊。

### 網格裁切原則

- 可用 2 條以上網格線界定一個方向的範圍。
- 可用 1 條網格線搭配另一方向或偏移量界定範圍。
- Z 範圍可用足夠大的上下界，確保平面視圖裁切不受高度不足影響。

## Naming Convention

```text
[母視圖名稱]([分張序號]/[總張數])
範例：鮮食中心 一層平面圖(1/4)
```

## 依視圖比例同步視埠類型

當使用者要求偵測視圖比例、替換視埠標題類型，或同步圖紙上的視埠類型時，使用此流程。

預設行為：

- 只處理圖紙上已放置視圖類型為 `FloorPlan`、`Elevation` 或 `Section` 的視埠。
- 依命名規則 `附圖號的有比例標題_A1({scale})A3({doubleScale})` 精確比對視埠標題類型。
- 比例為 1:100 時，目標類型為 `附圖號的有比例標題_A1(100)A3(200)`。
- 若找不到精確比例類型，改用名稱包含 `有線條的標題` 的視埠類型。
- 若已放置視圖名稱或 `Title on Sheet` 包含 `圖例`，略過該視埠。

執行流程：

1. 先執行 `get_viewport_types`，確認目標比例類型與備援類型都存在。
2. 以 `dryRun=true` 執行 `sync_viewport_types_by_view_scale`。
3. 檢查 `ChangedCount`、`SkippedByTitleKeywordCount`，以及任何 `MatchedExactScaleType=false` 的列。
4. 確認 dry-run 結果符合預期後，才以 `dryRun=false` 套用。
5. 再執行一次 `dryRun=true`；成功判定為 `ChangedCount = 0`。

若 Codex 可見的 MCP tool schema 尚未刷新，但 Revit DLL 已包含 command，使用 repository wrapper，不要另寫新的 WebSocket script：

```powershell
$env:REVIT_MCP_PARAMS_JSON = '{"dryRun":true}'
node MCP-Server\scripts\run_command.js sync_viewport_types_by_view_scale
```

## 參考文件

- `domain/sheet-viewport-management.md`
- `domain/dependent-view-crop-workflow.md`
- `domain/viewport-type-scale-sync.md`
