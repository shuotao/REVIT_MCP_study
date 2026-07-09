---
name: dedup-detail-elements
description: "找出並刪除 Revit 視圖中的重複 detail element（同 Type + 位置量化後相同），優先保留 Detail Group 內的副本、刪除 group 外的副本，避免破壞共用標準詳圖。涵蓋 DetailComponent / DetailCurve / FilledRegion / TextNote / Dimension。採 dryRun 預覽 → 使用者確認 → 執行刪除的兩階段 SOP，邊界情形（全部都在 group / 全部都不在 group）會列出但不動，由使用者決定是否處理。觸發條件：使用者提到刪除重複詳圖、清除重複 detail、清重複元件、清理視圖、視圖重複、重複的標頭、重複的尺寸、重複的線、重複的填充、重複的標註、重複的詳圖元件、dedup detail、duplicate detail elements、deduplicate view、清理重複 annotation。"
---

# 視圖內重複 Detail Element 清理

執行前請先讀取 `domain/dedup-detail-elements-workflow.md` 了解三類重複狀態（`to_dedup` / `all_in_groups` / `ambiguous_no_group`）的判定邏輯與處理原則。

## Prerequisites

1. Revit 已開啟，且**目前 active view 是要清理的視圖**（FloorPlan / Section / Drafting / Detail 等都可以）
   - **不可在 ViewSheet 上呼叫**：直接掛在 sheet 上的 detail element 很少，要清 sheet 上看到的，請開啟 sheet 中 viewport 對應的視圖
2. MCP Server 已連線（`ToolSearch select:mcp__revit-mcp__dedup_detail_elements_in_view`）
3. 與使用者確認重複的定義 = 同 Category + 同 Type + 位置量化後相同（與 Family 無關）

## Workflow

### Step 1：確認 active view

呼叫 `get_active_view` 確認目前在哪個視圖。若使用者指定其他視圖：
- 用 `get_all_views` 找到 ID 後傳 `viewId` 參數

### Step 2：dryRun 預覽（強制）

```
dedup_detail_elements_in_view({
  // viewId: 省略 → 用 active view
  categories: ["All"],   // 或 ["DetailComponent", "DetailCurve", ...] 子集
  tolerance: 1.0,         // 公釐，預設 1
  dryRun: true            // 強制先 dryRun
})
```

回應關鍵欄位：
- `Scanned`：各類別掃描到的元素數
- `DuplicateGroupCount`：找到多少組重複
- `ToDedupGroupCount` / `AllInGroupsCount` / `AmbiguousNoGroupCount`：三類分佈
- `DuplicateGroups[]`：每組的 Status、MemberCount、InGroupCount、OutGroupCount、Members 清單

⚠️ **回應若超出 token 上限會被截斷或寫入檔案**。組數很多時可考慮：
- 縮小 `categories`（如只跑 `["DetailComponent"]`）分批處理
- 從寫入的 tool-result 檔案用 `Read` + `grep` 抽出統計

### Step 3：與使用者對齊處理範圍

依據 dryRun 結果，跟使用者確認三類各自的處理意願：

| Status | 預設行為 | 使用者選擇 |
|--------|---------|------------|
| `to_dedup`（部分在 group / 部分在外） | **建議刪 group 外的副本** | 接受 / 拒絕 |
| `all_in_groups`（全部在 group 中） | 不動 | 通常是 group 設計如此，無需處理；要動需進 Edit Group |
| `ambiguous_no_group`（全部都在 group 外） | 不動 | 若使用者要清，由 AI 在 Step 5 用 `delete_element` 逐個處理（保留 ID 最小者） |

### Step 4：執行刪除（僅 to_dedup）

使用者確認 `to_dedup` 要刪後：

```
dedup_detail_elements_in_view({
  categories: ["All"],
  tolerance: 1.0,
  dryRun: false   // 改為 false
})
```

只會刪除 Status=`to_dedup` 那些 group 外的副本，**不會碰** `all_in_groups` 或 `ambiguous_no_group`。回應 `DeletedCount` / `DeleteErrors[]` 確認結果。

### Step 5（選配）：處理 ambiguous_no_group

工具設計上保守不動 ambiguous 組。若使用者明確要清，由 AI 端逐個處理：

1. 從 dryRun 的 `DuplicateGroups` 抽出所有 `Status="ambiguous_no_group"` 的組
2. 每組保留 ID 最小者，其餘加入刪除清單
3. 用 `delete_element` 批次刪除（同一訊息可發 30 個並行 tool calls 加速）
4. 偶爾出現「找不到元素」錯誤 → 多半是 cascade-delete（前面刪的元件帶走關聯元件），視為**目標已達成**，不需重試

⚠️ 逐個 `delete_element` 是 N 次 transaction，事先告知使用者「執行後 undo 要按 N 次」。

### Step 6：再跑一次 dryRun 驗證

```
dedup_detail_elements_in_view({ dryRun: true })
```

確認 `DuplicateGroupCount` 已下降到符合預期。剩餘的若都是 `all_in_groups` → 屬於 group 設計，無需處理。

## 工具

| 工具名稱 | 用途 |
|---------|------|
| `dedup_detail_elements_in_view` | 找/刪重複 detail element 主要工具 |
| `get_active_view` | 確認目前 active view ID |
| `get_all_views` | 解析使用者指定的視圖名稱為 ID |
| `delete_element` | Step 5 處理 ambiguous 組逐個刪除 |

## 注意事項

- **強制兩階段 SOP**：dryRun → 使用者確認 → 執行。不要 skip dryRun 直接刪。
- **位置容差**：預設 1mm，BoundingBox 中心點和對角點都納入比對。實務上：1mm 嚴格 / 5mm 容忍微錯位 / 10mm 寬鬆 / >10mm 危險（可能誤殺非重複的相鄰元件）。
- **TypeKey 與 Family 無關**：兩個元件 Type 相同就視為同一類，即使屬於不同 Family。
- **Detail Group only**：不認 Model Group。元件在 Model Group 中等同於不在 group 中。
- **跨視圖不處理**：每次只處理一個視圖；多視圖請逐個呼叫。
- **連結模型不處理**：只作用於當前 doc。
- **執行時不要並行其他寫入工具**：Revit UI thread 序列化，並行會 timeout。
- **掃描端可能有假陽性**：相同 ElementId 在同一 group 出現兩次 → 是內部 collector 重複收集（多個 filter 同時抓到），不是真實重複。剩下的若都是這種，視為已清乾淨。

## 組合使用

| 場景 | 搭配 Skill |
|------|-----------|
| 詳圖匯入後重複 | `/excel-to-legend` 匯入 → 本 Skill 清理 |
| 跨專案複製造成重複 | `/copy-sheets-cross-project` 後若發現重複 → 本 Skill |
| 詳圖批次複製前先清乾淨 | 本 Skill → `/copy-detail-items` |
| 圖頭同步前先去重 | 本 Skill → `/detail-component-sync`（先確保只有一份再同步） |

## Reference

詳見 `domain/dedup-detail-elements-workflow.md`（包含三類狀態的設計理由、TypeKey/PositionKey 構造、cascade-delete 行為、實戰演化歷程）。
