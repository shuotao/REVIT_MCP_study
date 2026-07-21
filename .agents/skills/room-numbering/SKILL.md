---
name: room-numbering
description: "房間重新排序編號與批次自動編號工作流。使用者提到房間編號、房間重新排序、room numbering、renumber rooms、自動編號、從 B134 開始、只排 B1F 等需求時使用。優先工具：renumber_rooms_by_level、get_active_view、get_rooms_by_level。"
---

# 房間重新排序編號

使用此 Skill 時，先讀 `domain/room-numbering-workflow.md`，並遵守該 SOP 的排序、dry-run、寫入與驗證規則。

## 工具優先序

1. 先用 `get_active_view` 重新錨定目前 Revit 狀態，回報目前視圖與樓層，但不要只依賴目前視圖決定目標樓層。
2. 若使用者指定樓層與起始號碼，優先使用 `renumber_rooms_by_level`。
3. 若只是要查看目前房間狀態，使用 `get_rooms_by_level`。
4. 不要用外部 WebSocket 腳本或逐筆 `modify_element_parameter` 來做大量房間重編號，除非批次工具不可用且使用者同意慢速 fallback。

## 標準流程

1. 確認目標樓層與起始號碼來自使用者輸入或本 turn 的工具查詢。例如 `level="B1F"`、`startNumber="B134"`。
2. 執行 dry-run：
   - `renumber_rooms_by_level({ level, startNumber, dryRun: true })`
   - 檢查 `Level`、`Count`、`StartNumber`、`EndNumber`、`Rooms` 的排序是否符合期待。
3. 若 dry-run 結果合理，執行正式寫入：
   - `renumber_rooms_by_level({ level, startNumber, dryRun: false })`
4. 寫入後用 `get_rooms_by_level({ level })` 驗證房間編號範圍與筆數。

## 精準性規則

- 預設排序為房間中心點由上到下，再由左到右；Y 軸列分組容差預設 `3000 mm`。
- 起始號碼必須以數字結尾，例如 `B134`；工具會保留前綴並遞增數字。
- 若樓層名稱模糊或候選號碼已存在於其他樓層，工具應停止並回報，不要猜。
- 若使用者指定 `B1F` 而 Revit 實際樓層為 `C-B1F`，允許工具解析，但回覆時要明確說明實際套用樓層。

## 部署提醒

`renumber_rooms_by_level` 需要新版 Revit add-in 與 MCP Server tool schema。若工具不存在，先確認：

- C# 已建置並部署對應 Revit 版本的 `RevitMCP.dll`
- `MCP-Server` 已重新 build
- Revit 與 MCP Server 已重啟

## Reference

- `domain/room-numbering-workflow.md`
- `domain/lessons.md`
