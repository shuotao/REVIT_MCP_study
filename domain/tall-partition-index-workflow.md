---
name: tall-partition-index-workflow
description: "Revit 輕隔間牆高於 6m 的結構回饋索引圖 SOP，整合 TYPE 牆分析、房間排除、與細節一起複製(W)、藍色房間填滿與紅色牆線標示。"
metadata:
  version: "1.0"
  updated: "2026-07-03"
  created: "2026-07-03"
  references:
    - "MCP/Core/Commands/CommandExecutor.PartitionTakeoff.cs"
    - "MCP/Core/Commands/CommandExecutor.ViewDuplicate.cs"
    - "MCP/Core/Commands/CommandExecutor.RoomFilledRegions.cs"
  related:
    - "revit-partition-takeoff.md"
    - "element-coloring-workflow.md"
    - "dependent-view-crop-workflow.md"
  referenced_by:
    - "tall-partition-index"
  tags: [revit, partition, room, structural-review, filled-region, view-duplicate]
---

# 輕隔間高牆索引圖流程

本流程用來找出 TYPE 系列中高度高於 6.0 m 的輕隔間牆，並產出提供結構單位檢討的平面索引圖。

這不是單純的數量計算，交付成果必須清楚呈現：

- 受高牆影響的房間，以藍色標示；
- 高於 6 m 的 TYPE 輕隔間牆，以紅色標示；
- 所有標示都只落在複製後的交付視圖上，原始索引圖保持乾淨。

## 適用範圍

依 2026-07-03 的 R20 專案實作，預設範圍如下：

- 樓層：`B-1F`、`B-3F`
- 來源視圖：
  - `物流中心一層輕隔間平面索引圖`
  - `03-1物流中心三層輕隔間平面索引圖`
- 牆類型條件：類型名稱包含 `TYPE`
- 高度門檻：高於 `6000` mm
- 排除房間名稱：`管道間`、`樓梯間`、`電梯間`
- 排除牆類型名稱：
  - `Type-F 岩棉庫板`
  - `Type-F 岩棉庫板(柱封版)`

若使用者指定其他樓層或其他排除條件，應以當次需求為準，不要把 `B-1F`、`B-3F` 寫死成固定流程。

## 指令流程

### 1. 分析房間與牆

執行 `analyze_tall_partition_rooms`。

建議參數如下：

```json
{
  "levels": ["B-1F", "B-3F"],
  "wallTypeContains": "TYPE",
  "minWallHeightMm": 6000,
  "excludeRoomNameContains": ["管道間", "樓梯間", "電梯間"],
  "excludeWallTypeNames": ["Type-F 岩棉庫板", "Type-F 岩棉庫板(柱封版)"],
  "includeSharedRoomWalls": true,
  "includeSingleRoomBoundaryWalls": true,
  "includeRoomRayHeight": true
}
```

判定原則：

- 房間只要含有符合條件的高牆，就要列入。
- 若同一面高牆被兩個房間共同持有，兩邊房間都要列入。
- 房間高度可用自房間底部向上打射線到樓板底的方式判斷。
- 不可因牆位於兩房之間，就漏掉其中一間房。

### 2. 輸出 CSV

CSV 欄位使用繁體中文，且必須保留 Element ID，因為後續視覺化會用到。

房間 CSV 建議至少包含：

- 樓層
- 房間名稱
- 房間編號
- 房間 Element ID
- 關聯牆 Element IDs
- 房間射線高度或判定高度

牆 CSV 建議至少包含：

- 樓層
- 牆 Element ID
- 牆類型名稱
- 牆高度(mm)
- From Room
- To Room
- 關聯房間 Element IDs

### 3. 複製視圖

用 Revit `WithDetailing` 複製來源索引圖。

使用 `duplicate_views_with_detailing`。

規則如下：

- 這類交付圖不要用從屬視圖。
- 不要把生成的 FilledRegion 畫到原始來源視圖。
- 新視圖名稱應明確，例如：
  - `物流中心一層輕隔間平面索引圖-高於6m牆標示(W)`
  - `03-1物流中心三層輕隔間平面索引圖-高於6m牆標示(W)`

### 4. 以藍色填滿房間

只在複製後的交付視圖上執行 `create_room_filled_regions`。

建議參數如下：

```json
{
  "viewId": 13157498,
  "roomIds": [123, 456],
  "filledRegionTypeName": "MCP 高牆房間藍色填滿",
  "color": { "r": 0, "g": 92, "b": 255 },
  "transparency": 65,
  "clearExisting": true,
  "marker": "MCP_TALL_PARTITION_ROOM_FILL"
}
```

注意事項：

- 直接對房間元素做 graphic override，不一定能在平面視圖中穩定看到房間上色。
- 以房間邊界建立 FilledRegion，才是這個流程的優先做法。
- 使用 marker 可讓之後重跑時更容易清理舊成果。

### 5. 以紅色標示牆

若視圖表現允許，可先嘗試模型牆的 override；但在這個流程裡，藍色 FilledRegion 很容易蓋住牆的模型 override，所以最終交付圖應優先使用位於最上層的紅色 detail line。

建議做法：

1. 由牆 CSV 或 JSON 取得牆 Element ID。
2. 以 `get_wall_info` 取得牆的端點或曲線。
3. 在複製視圖中用 `create_detail_lines` 建立紅色線段。
4. 若模型內有可用的粗線樣式，可優先使用，例如本次 R20 專案觀察到的 `寬線` / style id `81`。

這些 detail line 是協調圖說明用途，不會修改模型牆本體。

## QA 檢核表

- 原始來源索引圖中，不應殘留生成的藍色 FilledRegion。
- 最終複製出的 `(W)` 視圖中，應有預期數量的藍色房間填滿。
- 高牆位置應能清楚以紅色線條顯示在藍色填滿上方。
- CSV 中的房間數量，應與各樓層藍色房間區域數量對得上。
- CSV 中的牆 ID，應與各交付視圖中的紅線標示對得上。
- 排除牆型 `Type-F 岩棉庫板` 與 `Type-F 岩棉庫板(柱封版)` 不應出現在最終成果。
- 排除房間如管道間、樓梯間、電梯間，不應被誤納入，除非使用者明確變更範圍。

## 與既有功能的邊界

本流程應與一般 `partition-takeoff` 分開。

當使用者要的是數量、牆面積與估算 CSV，使用 `partition-takeoff`；當使用者要判斷結構行為，或要產出高於 6m 輕隔間的檢討索引圖，使用 `tall-partition-index`。

本流程也應與通用 `element-coloring` 分開。

當使用者只要依參數上色時，使用 `element-coloring`；當任務要求房間必須以 FilledRegion 呈現、牆標示又必須顯示在填滿之上時，使用本流程。
