---
name: tall-partition-index
description: "Revit 輕隔間牆高於 6m 的結構回饋索引圖工作流。當使用者要找 TYPE 系列高牆、排除管道間/樓梯間/電梯間或 Type-F 牆、輸出房間清單，並以藍色房間與紅色牆線標示時使用。"
---

# 輕隔間高牆索引圖

當任務不只是輕隔間數量統計，而是要做給結構單位看的高牆索引圖時，使用這個 skill。常見觸發詞包含：

- 輕隔間高於 6m
- TYPE 系列牆
- 結構回饋
- 挑空區房間
- 物流中心一層／三層輕隔間平面索引圖
- 與細節一起複製(W)
- 房間塗藍、牆塗紅、FilledRegion、detail line

## 分工判斷

這個 skill 應維持獨立，不直接併入既有 skill。

- `partition-takeoff` 負責一般 TYPE 輕隔間數量、面積與 CSV 報表。
- `element-coloring` 負責一般參數式元素上色。
- 本流程負責高於 6m 牆體的結構檢討、房間篩選、共牆判定、視圖複製，以及最終交付用索引圖。

## 工作流程

1. 用 `analyze_tall_partition_rooms` 分析高牆房間。
   - 依使用者指定樓層，常見為 `B-1F` 與 `B-3F`。
   - 篩選牆類型名稱包含 `TYPE`。
   - 高度門檻設為 `6000` mm。
   - 排除房間名稱包含 `管道間`、`樓梯間`、`電梯間`。
   - 依需求排除牆類型 `Type-F 岩棉庫板` 與 `Type-F 岩棉庫板(柱封版)`。
   - 若同一面高牆為兩個房間共用，兩側房間都要納入。
2. 輸出繁體中文 CSV。
   - 保留 Room ID 與 Wall ID，方便後續視覺化。
   - 分清楚「唯一牆 ID」與「各視圖中的牆標示次數」。
3. 用 Revit `WithDetailing` 複製索引圖來源視圖。
   - 使用 `duplicate_views_with_detailing`。
   - 這類交付圖不要用從屬視圖。
   - 視圖名稱加上清楚的 `(W)` 或 `與細節一起複製` 後綴。
4. 以藍色填滿房間。
   - 使用 `create_room_filled_regions`，從房間邊界建立 FilledRegion。
   - 使用固定 marker，例如 `MCP_TALL_PARTITION_ROOM_FILL`。
   - 只在交付用複製視圖上建立，不要畫回原始視圖。
5. 以紅色標示牆體。
   - 模型牆的 override 可能被 FilledRegion 蓋住。
   - 交付圖優先使用由牆端點生成的紅色 detail line。
6. 交付前做 QA。
   - 確認原始索引圖沒有殘留生成的 FilledRegion。
   - 確認複製視圖內有藍色房間與紅色牆標示。
   - 確認數量與 CSV/JSON 中的房間、牆 ID 一致。

## 參考文件

- `domain/tall-partition-index-workflow.md`
- `Lessons/tall-partition-index-lessons.md`
- `MCP/Core/Commands/CommandExecutor.PartitionTakeoff.cs`
- `MCP/Core/Commands/CommandExecutor.ViewDuplicate.cs`
- `MCP/Core/Commands/CommandExecutor.RoomFilledRegions.cs`
