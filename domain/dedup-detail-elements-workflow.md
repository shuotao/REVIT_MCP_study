---
name: dedup-detail-elements-workflow
description: "視圖內重複 detail element 清理 SOP：同 Category+Type+位置量化判定重複，優先保留 Detail Group 內副本、刪除 group 外副本；dryRun 預覽→確認→刪除的兩階段流程與邊界情形處理。觸發：刪除重複詳圖、dedup detail、清理視圖重複、duplicate detail elements。"
metadata:
  version: "1.0"
  updated: "2026-05-07"
  created: "2026-05-07"
  contributors:
    - "lesleyliuke"
  references: []  # TODO: 月小聚補外部依據
  related:
    - unjoin-geometry-workflow.md
  referenced_by:
    - dedup-detail-elements
  tags: [詳圖, detail, 重複, dedup, 清理, group, TextNote, FilledRegion, Dimension]
---

# 視圖內重複 Detail Element 清理 — 工作流程

本文件記錄 Revit 視圖中重複 detail element 的判定邏輯、處理策略，以及與既有 Detail Group 的互動原則。供 `/dedup-detail-elements` Skill 引用。

## Background

Revit 專案在以下情況容易產生重複的 detail element：
- 從 Excel/CAD/PDF 匯入詳圖時重複執行
- 複製貼上後忘記刪除原處的副本
- Group 編輯時意外把 group 內的元件複製到 group 外
- 多人協作時對同一視圖各自獨立加標註
- 從其他專案 `copy_sheets_from_file` / `copy_detail_items_to_views` 時 source view 已含重複

這些重複會在出圖時造成：
- 標註文字疊在一起（看起來像粗體）
- 雙重邊框（FilledRegion 疊兩層顏色變深）
- 編輯時改了一份另一份還在
- 模型體積膨脹、儲存變慢

## Definition：什麼算「重複」

兩個 detail element 視為重複的條件（全部要滿足）：
1. **同 Category**（OST_DetailComponents / Lines / FilledRegion / TextNote / Dimension）
2. **同 Type**（FamilySymbol ID 或 LineStyle ID 相同）
3. **位置量化後相同**（BoundingBox 中心點 + 對角點，按 tolerance 公釐量化後 hash 相同）

**不要求 Family 相同** — 兩個 Type 相同的 FamilyInstance 即使來自不同 Family 也算同一類。這是工具設計時的簡化選擇，避免 Family 命名歷史包袱（同一個邏輯類型在不同案件命名不一致）導致清不到。

## 三類重複狀態

dryRun 結果中 `DuplicateGroups[*].Status` 可能是：

### `to_dedup`（推薦處理）

部分副本在 Detail Group 內，部分在 group 外。
- **預設動作**：保留 group 內的，刪除 group 外的
- **理由**：Detail Group 通常是專案的標準件（圖頭、標題框、裝修圖例），共用且結構化；group 外的多半是後來複製貼上漏掉的散兵
- **風險低**：刪掉的不影響 group 結構，group 內的副本繼續維持原有的引用關係

### `all_in_groups`（保守不動）

全部副本都在 Detail Group 內。
- **預設動作**：不動
- **理由**：可能是同一個 Detail Group 設計時就有兩個位於同位置的元件（極少見但合法），或兩個不同的 Detail Group 在同位置重疊（例如使用者刻意把兩個圖例疊在一起標示「兩種狀態並存」）
- **處理方式**：要刪請進入 Edit Group 模式手動處理；或直接 `delete_element` 指定 ID

### `ambiguous_no_group`（保守不動）

全部副本都在 group 外（沒有任何一個在 Detail Group 內）。
- **預設動作**：不動
- **理由**：工具沒有「保留哪個」的判斷依據（沒有 group 可以當錨點）
- **處理方式**：使用者明確同意後，由 AI 在 Skill Step 5 用 `delete_element` 逐個處理（預設保留 ID 最小者，刪除其餘）

## TypeKey 構造

```
FamilyInstance:        Cat{categoryId}:T{typeId}
DetailCurve:           DetailCurve:LS{lineStyleId}
FilledRegion:          Cat{categoryId}:T{typeId}
TextNote:              Cat{categoryId}:T{typeId}
Dimension:             Cat{categoryId}:T{typeId}
```

`UnkeyedSkipped` 計數記錄那些拿不到 TypeKey 的元素（罕見，通常是已 corrupted 的 element）。

## PositionKey 構造

從 BoundingBox 取兩個點：
1. 中心點 (cx, cy, cz)
2. 對角點 (minx, miny / maxx, maxy)

每個座標除以 tolerance（mm）後取整，組成字串。例：
```
fr:467,13959,200|2492,51056,200
```
（`fr` 表示 framework prefix，分號分隔 [中心 vs 角點]，逗號分隔 x,y,z）

`tolerance` 預設 1.0 mm，意味著兩個元件位置差距 < 1mm 才算同位置。實務上：
| tolerance | 用途 | 風險 |
|-----------|------|------|
| 1mm（預設） | 嚴格比對，幾乎只認「真正同位置」 | 微小錯位（手動拖曳誤差）抓不到 |
| 5mm | 容忍小幅錯位 | 低 |
| 10mm | 容忍肉眼幾乎看不出的偏移 | 中 |
| > 10mm | 寬鬆 | 高，可能誤殺非重複的相鄰元件 |

## Cascade Delete 行為

刪除 detail element 時 Revit 可能會 cascade-delete 關聯的元件：
- Dimension 被刪 → 沒有其他元件依賴
- TextNote 被刪 → 沒有其他元件依賴
- DetailComponent (FamilyInstance) 被刪 → 若其他 FamilyInstance 有 reference 到它（罕見），會被一起刪
- FilledRegion 的某些 nested family 也可能 cascade

實務上，Step 5 逐個 `delete_element` 處理 ambiguous 組時偶爾會看到「找不到元素」錯誤。這通常表示：
- 該元素已被前面某個刪除動作 cascade 帶走
- 視為「目標已達成」，不需重試
- 不要把 cascade-already-deleted 計入失敗

## 與既有 Skill 的關係

| 場景 | 搭配 Skill |
|------|-----------|
| 詳圖匯入後重複 | 先 `/excel-to-legend` 匯入 → 出問題用本 Skill 清理 |
| 跨專案複製造成重複 | `/copy-sheets-cross-project` 後若發現重複 → 本 Skill |
| Detail items 複製到多視圖 | `/copy-detail-items` 若 source 已含重複 → 先用本 Skill 清乾淨再複製 |
| 圖頭同步前清理 | 本 Skill → `/detail-component-sync`（先確保只有一份再同步） |

## 演化歷程

### v1.0（初代，2026-05）

由實務需求觸發：使用者一張一層平面圖出現 132 組重複 detail element（多次匯入累積）。

設計重點：
- **保守是預設**：dryRun 強制預覽
- **三類狀態明確分開**：避免一律刪除引發誤殺
- **Detail Group 為錨**：用 group 區分「標準件」vs「散兵」
- **TypeKey 不認 Family**：簡化設計，避免歷史包袱

實戰結果（一層平面圖(開審)，FloorPlan，scale 1:200）：
- 132 → 9 組（93% 清理率）
- 清掉 156 個重複元件（42 個 to_dedup + 114 個 ambiguous）
- 剩 8 組為**掃描端假陽性**（同 ElementId 被收集兩次，待修）
- 1 組 all_in_groups 屬於合法保留

### 待修事項

- **掃描端假陽性**：同一 ElementId 不應在掃描清單中出現兩次。發生時是內部 collector 重複收集（例如多個 OfCategory + OfClass 過濾器同時抓到），需在 c# 端 `MCP/Core/Commands/CommandExecutor.DetailDedup.cs` 用 set-based dedupe 過濾
- **加 includeAmbiguous flag**：目前 ambiguous 只能透過外部逐個 `delete_element` 處理，造成 N 次 transaction（undo 代價高）。可在工具加 flag，內部一次 transaction 處理「保留 ID 最小者」

## Lessons

- **Active view 必須是有 detail elements 的 view**：在 ViewSheet 上呼叫通常會掃到 0 個（因為 detail element 直接掛在 sheet 上的很少）。要清 sheet 上看到的，需開啟對應的 viewport view。
- **不要在執行刪除時並行其他寫入工具**：Revit ExternalEvent UI thread 序列化，並行會 timeout。
- **大量 ambiguous 處理**：若 ambiguous 上百筆，逐個 `delete_element` 是 N 次 transaction。執行前提示使用者「執行後 undo 要按 N 次」。
- **Cascade-delete 不是錯誤**：刪除過程中「找不到元素」多半是 cascade，視為已達成目標，不要重試。
- **Tool result 太大會被截斷**：`DuplicateGroups` 詳細列表可能超出 token 上限，被自動寫入 tool-result 檔案。從檔案用 `Read` + `grep` 抽統計。
- **Quote 一致性**：TypeKey 中的 categoryId 是負數（OST_DetailComponents = -2002000）。用文字 grep 比對時記得包含負號。
