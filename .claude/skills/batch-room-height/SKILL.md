---
name: batch-room-height
description: "批次調整房間上限高度：依房間名稱或用途分組，指定目標高度(mm)後一次套用到所有符合的 Room（Upper Limit + Limit Offset），不動樓層。觸發條件：使用者提到房間高度、房高、room height、upper limit、limit offset、批次房高、統一房高、ceiling height、room top。"
---

# 批次修改房間上限高度

## Prerequisites
- Revit 已開啟專案且含可辨識的 Room（Name 或 Department 有資料）
- MCP Server 已連線

## Workflow

### 步驟 1：盤點樓層與房間

依使用者描述的範圍呼叫：
- `get_all_levels` — 列出所有樓層
- `get_rooms_by_level` — 逐層（或指定樓層）取得 Room 清單

整理成「依 Name / Department 分組」的表格：

| 分組（Name） | 樓層 | 房間數 | 目前 Upper Level | 目前 Limit Offset (mm) |
|---|---|---|---|---|
| 居室 | 2F | 8 | 2F | 3000 |
| 浴室 | 2F | 3 | 2F | 2400 |
| 走廊 | 2F | 2 | 3F | 0 |
| ... | ... | ... | ... | ... |

### 步驟 2：使用者確認

將表格展示給使用者，詢問：
- 哪些分組要修改？（可一次指定多組）
- 每組的目標高度(mm)？（預設：該用途的常用值，例 居室 3000、浴室 2400）
- 是否要指定 Upper Level？（預設沿用 Room 的 Base Level，房高 = heightMm）
- 是否限定單一樓層？（預設全專案）
- 比對欄位是 Name 還是 Department？（預設 Name）

**必須等使用者明確回覆後才進入步驟 3。**

### 步驟 3：執行批次修改

呼叫 `batch_set_room_height`，傳入：
```json
{
  "groups": [
    { "nameMatch": "居室", "heightMm": 3000 },
    { "nameMatch": "浴室", "heightMm": 2400 },
    { "nameMatch": "走廊", "heightMm": 2800, "upperLevelName": "3F" }
  ],
  "levelName": "2F",
  "matchField": "name"
}
```

### 步驟 4：確認結果

讀取回傳：
- `ModifiedCount` / `RequestedCount` — 成功 / 應修改數
- `Groups[]` — 每組的 MatchedCount、ModifiedCount
- `Errors[]` — 失敗清單
- `OriginalValues[]` — 舊值（供事後追溯）

提示使用者：
- 在 Revit 中選取其中一個 Room，從 Properties 面板確認 Upper Limit / Limit Offset
- 整批為單一 Transaction，**按 Ctrl+Z 可一次還原**
- 若要再修改其他樓層或分組，可重複步驟 2–3

## 工具

| 工具名稱 | 用途 |
|---------|------|
| `get_all_levels` | 列出專案所有樓層（供使用者挑選修改範圍） |
| `get_rooms_by_level` | 取得指定樓層的 Room 清單與基本參數 |
| `batch_set_room_height` | 依分組批次寫入 Room 的 Upper Limit + Limit Offset |

## 注意事項

- 此操作修改的是 **Room 實例參數**（`ROOM_UPPER_LEVEL`、`ROOM_UPPER_OFFSET`），不動 Level 本身
- 改動後 Room 的三維邊界會變，會影響：**體積計算、排煙有效帶判定、部分面積/表面積統計**
- 若原本 Upper Limit 在較高樓層、改成 Base Level，房高會縮短；反之放大
- **Group 內 Room**：Revit API 沒有公開 `Document.EditGroup`（UI-only command），改用 `WarningSwallower` 在 Transaction 上吞掉「modified outside group edit mode」警告；Revit 內部仍會正確套用變更並自動同步到同 GroupType 的所有 instance
- **Area = 0 Room**（未封閉）也會被納入修改（v2 移除了原本的 `Area > 0` 過濾）
- **回傳預設為 summary**：500+ Room 時只回計數 + Errors，不帶每筆明細；需要 audit 時傳 `summaryOnly: false`
- `heightMm` 合理範圍 1~10000 mm；超出會記入 Errors 並略過
- 單一 Transaction：一次 Ctrl+Z 全部還原
- Transaction 上註冊 `WarningSwallower`（domain/lessons.md [L-013] 實作），吞掉其他邊界 Warning；Error 仍會讓 Transaction 回滾

## Reference

詳見 `domain/room-height-limit.md`（房高參數概念、法規建議值、常見誤區）。
