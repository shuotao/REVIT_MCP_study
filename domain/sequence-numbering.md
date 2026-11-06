---
name: sequence-numbering
description: "圖面順序編號標註 SOP：在視圖上依順序對任意元件放置編號 TextNote（1,2,3 / a,b,c / A1,A2），支援跳過含 4 的號碼。當使用者提到編號插入、順序編號、圖面編號、車位編號標註、create_sequence_numbers 時觸發。要寫進元件參數而不是放文字的，看 parking-auto-numbering.md。"
metadata:
  version: "1.0"
  updated: "2026-09-07"
  created: "2026-09-07"
  references: []  # 跳 4 是圖面慣例不是法規；無法源可引
  related:
    - parking-auto-numbering.md
    - room-numbering-workflow.md
    - permit-floor-area-formula.md
  referenced_by: []
  tags: [編號插入, 順序編號, 圖面編號, sequence numbering, create_sequence_numbers, 跳4, 車位編號]
---

# 圖面順序編號標註

## 1. 目的

在圖面上依順序放置編號文字：停車位、設備、開口、檢討項目都用得到。原本要逐一建立文字並手動輸入號碼，本工具一次放完。

對應工具：`create_sequence_numbers`
實作：`MCP/Core/Commands/CommandExecutor.Numbering.cs`

## 2. 與 parking-auto-numbering 的分界

兩者**產出物不同，不要混用**：

| | 產出物 | 何時用 |
|---|---|---|
| `create_sequence_numbers` | 圖面上的 TextNote | 要在圖上看到編號，且該編號不需要進明細表 |
| `renumber_parking_spaces` | 元件的「備註」參數值 | 編號要能被明細表統計、被標籤讀取 |

車位同時要「參數有值」與「圖上有號」時，兩個都跑，且**用同一組排序參數**（`yToleranceMm`、`skipFour`、起始號），否則兩份編號會對不起來。

## 3. 順序決定

| `order` | 行為 |
|---|---|
| `given` | 照 `elementIds` 陣列的順序。給了 `elementIds` 時的預設值。 |
| `yx` | 依 Y 由上到下分排（`yToleranceMm` 容差）、同排 X 由左到右。沒給 `elementIds` 時的預設值。 |

`yx` 用的是 `CommandExecutor.RoomRenumber.cs` 的 `OrderRoomsTopDownLeftRight`，與房間重編號、停車格編號同一套演算法。分排錨點取該排第一個元件的 Y，不是跟前一筆比較 —— 避免容差鏈式漂移把不同排的元件拉進同一排。

## 4. 編號格式

| 需求 | `format` | `prefix` | 產出 |
|---|---|---|---|
| 1, 2, 3 | `number` | （空） | `1` `2` `3` |
| A1, A2, A3 | `number` | `A` | `A1` `A2` `A3` |
| a, b, c | `letter` | （空） | `a` `b` `c` |
| A, B, C | `letter` + `upperCase: true` | （空） | `A` `B` `C` |

字母序超過 z 會進位成 aa、ab（`ToLetterLabel`，序數從 1 起算）。

**`skipFour`**：跳過任何**含數字 4** 的號碼（4、14、24、40–49…），不只是個位數是 4。這是台灣圖面慣例，不是法規要求。只對 `format: number` 有意義。

## 5. 作業流程

1. `dryRun: true` 先跑，檢查：
   - `Count` 與你預期要編的元件數相同
   - `FirstLabel` / `LastLabel` 是不是你要的範圍
   - `Placements` 中前幾筆與最後幾筆的 `RowIndex` 是否合理（**只看前 10 筆會漏掉尾端接頭號的問題**）
   - `Skipped` 是空的
2. 確認後 `dryRun: false` 正式放置。

放置點是元件中心加上 `offsetXMm` / `offsetYMm`。中心取 `LocationPoint`，取不到則取 BoundingBox 中心；兩者都沒有的元件列入 `Skipped` 不編號。

## 6. 回滾行為

所有 TextNote 在**單一 Transaction** 內建立；任一筆失敗整批回滾，不會留下編到一半的圖面。

## 7. 常見問題

| 症狀 | 原因 | 處理 |
|---|---|---|
| `Count` 比預期少 | 有元件取不到中心點 | 看 `Skipped` 的 `Reason` |
| 用 `category` 收集但報找不到元件 | 該類別在此視圖不可見 | 先確認視圖可見性，或改用 `elementIds` |
| 同排元件編號不連續 | `yToleranceMm` 太小，同排被拆成兩排 | 調大容差重跑 dry-run |
| 不同排被併成一排 | `yToleranceMm` 太大 | 調小容差 |
| 編號文字疊在元件上看不清 | 沒設偏移 | 用 `offsetXMm` / `offsetYMm` 推開 |
