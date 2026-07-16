---
name: viewport-type-scale-sync
description: "依圖紙視埠中已放置視圖的比例，同步視埠標題類型。適用於平面圖、立面圖、剖面圖視埠，並包含圖例/標題關鍵字排除與安全 dry-run 驗證流程。"
metadata:
  version: "1.0"
  updated: "2026-06-04"
  created: "2026-06-04"
  contributors:
    - "codex"
  referenced_by:
    - sheet-management
  tags: [sheet, viewport, view-scale, title-type, dry-run, revit]
---

# 依視圖比例同步視埠類型

## 適用範圍

此流程會讀取圖紙上已放置視圖的比例，並同步更新該視埠的標題類型。

只處理已放置視圖類型為下列項目的視埠：

- `FloorPlan`
- `Elevation`
- `Section`

若已放置視圖名稱或 `Title on Sheet` 包含排除關鍵字，則略過該視埠。預設排除關鍵字為：

- `圖例`

這可以避免名稱或圖紙標題帶有圖例/參考圖性質的平面圖、立面圖、剖面圖被誤改視埠類型。

## 工具

- `get_viewport_types`：列出專案中可用的視埠標題類型。
- `sync_viewport_types_by_view_scale`：依已放置視圖比例預覽或套用視埠類型變更。

## 類型比對規則

對於比例為 `S` 的已放置視圖，使用以下精確視埠類型命名規則：

```text
附圖號的有比例標題_A1({scale})A3({doubleScale})
```

範例：

- `1:100` -> `附圖號的有比例標題_A1(100)A3(200)`
- `1:150` -> `附圖號的有比例標題_A1(150)A3(300)`
- `1:250` -> `附圖號的有比例標題_A1(250)A3(500)`

若找不到精確對應的視埠類型，則使用名稱包含下列文字的備援類型：

```text
有線條的標題
```

## Revit API 注意事項

不要只用 `OST_Viewports` 類別去收集視埠標題類型。在 Revit 2020 中，依專案的類別/類型 metadata 狀態不同，這種方式可能會回傳 0 個類型。

應從既有、已放置的 `Viewport` 出發並呼叫：

```csharp
viewport.GetValidTypes()
```

同時也要納入目前視埠類型：

```csharp
viewport.GetTypeId()
```

這是取得視埠實際可切換類型最可靠的來源。

## 執行 SOP

1. 確認 Revit 2020 已載入最新的 `RevitMCP.dll`。
2. 執行 `get_viewport_types`，確認目標比例類型與備援類型都存在。
3. 以 `dryRun=true` 執行 `sync_viewport_types_by_view_scale`。
4. 檢查：
   - `Count`
   - `ChangedCount`
   - `SkippedByTitleKeywordCount`
   - `FallbackTypeName`
   - 任何 `MatchedExactScaleType=false` 的項目
5. 以 `dryRun=false` 套用。
6. 再次以 `dryRun=true` 執行。完成判定標準為：

```text
ChangedCount = 0
```

## 直接指令 Wrapper

若 Codex 可見的 MCP tool schema 尚未刷新，但 Revit DLL 已包含該 command，可使用 repository wrapper：

```powershell
$env:REVIT_MCP_PARAMS_JSON = '{"dryRun":true}'
node MCP-Server\scripts\run_command.js sync_viewport_types_by_view_scale
```

Wrapper 必須送出目前的 socket payload 格式：

```json
{ "method": "command_name", "params": {}, "id": "request_id" }
```

Wrapper 也必須有硬性 timeout。若 Revit command 沒有回應，不可以無限等待。

## 部署安全規則

若部署 `RevitMCP.dll` 時因 DLL 被鎖定、使用中、拒絕存取，或無法覆蓋而失敗，必須立刻停止。不要在同一輪繼續重試或輪詢 Revit。請使用者關閉 Revit，並等待確認後再嘗試部署。
