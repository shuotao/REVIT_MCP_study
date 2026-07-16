---
name: room-numbering-workflow
description: "Revit 房間重新排序編號 SOP。規範如何依樓層取得 Room、用中心點由上到下再由左到右排序、從使用者指定起始號碼批次寫入房間編號，並避免逐筆 modify_element_parameter 造成速度慢與交易風險。"
metadata:
  version: "1.1"
  updated: "2026-06-09"
  created: "2026-04-02"
  contributors:
    - "Codex"
  references: []
  related:
    - lessons.md
    - session-context-guard.md
  referenced_by:
    - "room-numbering"
  tags: [room-numbering, renumber, rooms, Revit, batch, dry-run, 房間編號, 重新排序]
---

# 房間重新排序編號工作流

## 目的

此 SOP 用於將指定樓層的 Revit Rooms 依圖面位置重新排序並批次寫入房間編號。典型需求包含：

- 「房間重新排序編號，只排 B1F，從 B134 開始」
- 「把 2F 房間重新編號」
- 「room numbering / renumber rooms」
- 「先 dry-run 看順序，再正式寫入」

## 核心原則

- 使用 `renumber_rooms_by_level` 作為標準工具。此工具在 Revit add-in 端一次查詢、排序、交易寫入，避免 MCP 層逐筆 `modify_element_parameter` 往返造成速度慢。
- 使用者指定的樓層、起始號碼、容差都屬於 runtime parameter，不得寫死在 Skill 或 Domain。
- 寫入前先 dry-run，寫入後再查詢驗證。
- 不直接呼叫 WebSocket 腳本，不自行組 `{ CommandName, Parameters, RequestId }` payload。

## 輸入參數

| 參數 | 來源 | 說明 |
|------|------|------|
| `level` | 使用者指定或本 turn 工具查詢 | 目標樓層，可為 `B1F`、`C-B1F` 等可唯一解析名稱 |
| `startNumber` | 使用者指定 | 起始房號，必須以數字結尾，例如 `B134` |
| `dryRun` | Agent 控制 | 預設先 `true` 預覽，再 `false` 寫入 |
| `yToleranceMm` | 使用者指定或預設 | Y 軸列分組容差，預設 `3000` |
| `includeUnnamed` | 使用者指定或預設 | 是否包含未命名但已放置房間，預設 `true` |

## 排序規則

1. 收集目標樓層所有已放置且 `Area > 0` 的 Rooms。
2. 取得房間中心點：
   - 優先使用 `LocationPoint`
   - 若無點位，使用 BoundingBox 中心點
   - 無中心點或未放置房間列入 skipped，不寫入
3. 依 `CenterY` 由大到小排序，代表由圖面上方往下。
4. 以 `yToleranceMm` 將相近 Y 值分為同一列。
5. 同一列內依 `CenterX` 由小到大排序，代表由左到右。
6. 從 `startNumber` 的文字前綴與數字尾碼開始連續遞增。例如 `B134` 產生 `B134`、`B135`、`B136`。

## 標準操作

### 1. Re-anchor

在任何會修改 Revit 的動作前，先呼叫：

```text
get_active_view()
```

用途是確認目前文件與視圖狀態。若使用者已明確指定樓層，仍以使用者指定樓層作為 `renumber_rooms_by_level.level`。

### 2. Dry-run

```text
renumber_rooms_by_level({
  level: "{level}",
  startNumber: "{startNumber}",
  dryRun: true,
  yToleranceMm: 3000
})
```

檢查：

- `Level` 是否為預期實際樓層，例如使用者輸入 `B1F`，工具解析為 `C-B1F`
- `Count` 是否合理
- `StartNumber` 與 `EndNumber` 是否合理
- `Rooms` 的順序是否符合由上到下、由左到右
- `SkippedRooms` 是否可接受
- `Conflicts` 是否為空

### 3. 正式寫入

dry-run 合理後才執行：

```text
renumber_rooms_by_level({
  level: "{level}",
  startNumber: "{startNumber}",
  dryRun: false,
  yToleranceMm: 3000
})
```

工具會在單一 Revit Transaction 中批次寫入。若任一房間無法寫入，工具應 rollback 並回報失敗原因。

### 4. 驗證

寫入後呼叫：

```text
get_rooms_by_level({
  level: "{level}",
  includeUnnamed: true
})
```

回覆時列出實際套用樓層、房間數、編號範圍與是否全部成功。不要列出未在本 turn 工具回傳中出現的房間 ID、名稱或數量。

## 風險與 fallback

- 若 `renumber_rooms_by_level` 不存在，先確認 Revit add-in DLL 與 MCP Server 是否都已更新並重啟。
- 若批次工具暫時不可用，才考慮逐筆 `modify_element_parameter`，但必須告知會較慢，且仍需使用本 SOP 排序與驗證。
- 若樓層名稱解析出多個候選，停止並請使用者指定完整樓層名稱。
- 若候選編號已存在於其他樓層，停止並回報衝突；除非使用者明確允許，否則不覆蓋。

## 相關文件

- `domain/lessons.md`
- `domain/session-context-guard.md`
