---
name: copy-detail-items
description: "批次複製詳圖項目（DetailCurve、TextNote、FilledRegion、DetailComponent、Dimension）從一個來源視圖到一或多個目標視圖（限同一專案內）。支援按類別過濾、指定特定元素 ID、XY 平移偏移、dryRun 預覽。適用於把標準節點、表格附註、詳圖標頭、尺寸標註從範本視圖一次散佈到整套圖紙；或將某張完成的詳圖複製到其他樓層的同類視圖。觸發條件：使用者提到批次複製詳圖、複製 detail items、copy details to views、複製標註到其他視圖、複製尺寸、複製 dimension、把這個 view 的內容複製到那些 view、套詳圖到多個視圖、batch copy annotations、batch copy dimensions、replicate detail elements、duplicate detail across views、把詳圖搬到別的視圖。"
---

# 批次複製詳圖項目

在同一 Revit 專案內，把詳圖項目（DetailCurve、TextNote、FilledRegion、DetailComponent、Dimension）從一個來源視圖批次複製到多個目標視圖。

## Available Tools

| 工具 | 用途 |
|------|------|
| `copy_detail_items_to_views` | 批次複製詳圖項目（一個 source view → 多個 target views），支援類別過濾、特定元素、平移偏移、dryRun |
| `get_active_view` | 取得使用者目前在 Revit 中開啟的視圖（常用於識別 source view） |
| `get_selected_elements` | 取得使用者目前選取的元素（用於指定 sourceElementIds 子集） |
| `get_all_views` | 列出所有視圖以解析目標 view IDs |

## Workflow：批次複製詳圖到多個視圖

### Step 1：辨識來源視圖

從使用者描述或目前狀態取得 `sourceViewId`：

- 使用者說「目前這個 view」→ 用 `get_active_view`
- 使用者點名某個 view 名稱 → 用 `get_all_views` 比對名稱取 ID
- 使用者直接給 ID → 直接使用

### Step 2：辨識目標視圖

`targetViewIds` 必須是陣列（即使只有一個目標）：

- 使用者給一組視圖名稱 → 用 `get_all_views` 篩選對應 ID
- 涵蓋整套圖紙時建議讓使用者明確列出，避免誤覆蓋

### Step 3（選填）：縮小複製範圍

預設複製來源視圖中**所有**符合 `elementCategories` 的詳圖項目。若只要複製部分元素：

- **按類別子集**：傳 `elementCategories: ["TextNotes"]` 只複製文字
- **按特定元素**：先讓使用者在 Revit 中選取 → 用 `get_selected_elements` 取得 IDs → 傳給 `sourceElementIds`

### Step 4（選填）：dryRun 預覽

可以先用 `dryRun: true` 看 `SourceElementSummary` 確認元素數量符合預期：

```
copy_detail_items_to_views(
  sourceViewId: 123456,
  targetViewIds: [234567, 234568, 234569],
  dryRun: true
)
```

⚠️ **大專案上 dryRun 可能 timeout**（group 收集邏輯較慢）。若 timeout：
- 不要重試，直接跳到 Step 5 實際執行 — 因為 `fallbackToIndividual: true` 預設會自動容錯，最壞情況也只是 `PartialSuccess` 給出失敗清單，不會破壞目標 view
- dryRun 不是必要步驟，是錦上添花的預覽

### Step 5：執行複製

直接執行（或從 dryRun 確認後）：

```
copy_detail_items_to_views(
  sourceViewId: 123456,
  targetViewIds: [234567, 234568, 234569]
)
```

### Step 6（選填）：平移後複製

若希望複製後的元素位置略微偏移（例如避免疊在原位置）：

```
copy_detail_items_to_views(
  sourceViewId: 123456,
  targetViewIds: [234567],
  offset: { x: 100, y: 0 }   // 向右平移 100 mm
)
```

## Parameters

### `copy_detail_items_to_views`

| 參數 | 說明 | 預設 |
|------|------|------|
| `sourceViewId` | 來源視圖 Element ID | **必填** |
| `targetViewIds` | 目標視圖 ID 陣列（不可空） | **必填** |
| `elementCategories` | 類別過濾子集，可選 `"DetailCurves"`/`"TextNotes"`/`"FilledRegions"`/`"DetailComponents"`/`"Dimensions"`/`"All"`。Dimensions 涵蓋 Linear/Aligned/Radial/Angular/ArcLength/SpotElevation 全部尺寸標註類型 | `["All"]` |
| `sourceElementIds` | 指定要複製的元素 ID（覆寫 `elementCategories`） | （省略則收集全部符合類別的） |
| `offset` | `{ x, y }` 平移偏移（mm） | `{ x: 0, y: 0 }` |
| `preserveGroups` | `true` = 收集到的 group 成員自動替換成 group instance 一起複製，保留 Detail Group / Model Group 結構；`false` = group 成員以個別元素複製，丟失 group 結構 | `true` |
| `fallbackToIndividual` | `true` = 批次失敗時自動逐個元素重試，記錄成功/失敗；`false` = 批次失敗則整批 rollback 回 Failed | `true` |
| `dryRun` | `true` = 只回傳統計，不實際複製 | `false` |

## 回傳結構

```
{
  Success: true,
  SourceViewId, SourceViewName,
  SourceElementSummary: {
    DetailCurves, TextNotes, FilledRegions, DetailComponents, Dimensions,
    Groups,    // 被替換進來的 group instance 數
    Total      // 實際送進 CopyElements 的元素數（已 dedupe，含 group instance）
  },
  Results: [
    {
      TargetViewId, TargetViewName,
      Status: "Success" | "PartialSuccess" | "Skipped" | "Failed",
      Mode: "Batch" | "Individual",  // 哪個階段成功的
      ElementsCopied,                  // 實際複製的元素數
      FailedCount, FailedElements,     // Individual fallback 後仍失敗的元素 IDs + 原因
      BatchErrorReason,                // 批次失敗時的 exception 訊息（成功時為 null）
      TypeConflicts,
      SuppressedWarnings, WarningSummary,
      Reason?                          // Skipped 才有
    },
    ...
  ],
  TotalElementsCopied
}
```

- `Mode` = `"Batch"` 表示一次批次成功；`"Individual"` 表示批次失敗後 fallback 為逐個重試。
- `Status = "PartialSuccess"` 表示 Individual fallback 後部分元素成功、部分失敗，`FailedElements` 列出失敗清單。
- `SuppressedWarnings` 是被 preprocessor 自動吞掉的 warning 數，`WarningSummary` 是前 5 種 warning 訊息的去重統計（依次數排序）。

## Multi-source 批次複製模式（一對一名稱配對）

當使用者要把**多個 source views** 的內容複製到**多個 target views**（一對一對應，例如「一層 → 一層、二層 → 二層」），**必須串行呼叫**，不可用單一訊息並行多個工具呼叫：

```
# 錯誤：5 個並行 → Revit ExternalEvent UI thread 排隊，後面的 timeout
copy_detail_items_to_views(source: A, target: [a])  ┐
copy_detail_items_to_views(source: B, target: [b])  ├ 同一訊息並行
copy_detail_items_to_views(source: C, target: [c])  ┘

# 正確：一次只呼叫一個，等回應後再呼叫下一個
copy_detail_items_to_views(source: A, target: [a])  → 等回應
copy_detail_items_to_views(source: B, target: [b])  → 等回應
...
```

**配對流程：**
1. 用 `get_selected_elements` 取得使用者選中的 source views
2. 用 `get_all_views` + `get_element_info` 找出符合條件（如套用某 view template）的候選 target views
3. 用 `get_element_info` 抽查目標 view 的 `View Template` 參數確認對應
4. 用 view 名稱 + `Associated Level` 做配對（例如「A-一層平面圖」配「一層平面圖(開審)」，兩者都是 1FL）
5. 把配對表給使用者確認後（可選 dryRun，但**也建議串行**因 group 邏輯偶有 timeout）**串行**執行實際複製
6. `fallbackToIndividual: true` 預設會處理失敗 dimension/element，最終結果若有 `Status: "PartialSuccess"` 就把 `FailedElements` 列給使用者手動補

## Notes

- **同一專案內**：跨專案複製詳圖請改用 `/copy-sheets-cross-project`
- 來源 view 不能是 ViewTemplate，會直接拋錯
- 若目標 view 與來源 view 相同 → 跳過並標記 Skipped
- 若目標 view 是 ViewTemplate → 跳過並標記 Skipped
- 每個 target view 用獨立 Transaction，單一目標失敗不會影響其他
- Type 衝突自動採 `use_destination` 策略（保留專案既有 type）
- 複製到不同 scale 的 view 時，元素以原始座標放置；視覺大小會因 view scale 不同而看似改變（這是 Revit 預設行為）
- DraftingView ↔ model view 複製：座標系不同，建議搭配 `offset` 微調或讓使用者手動驗位

### Group（Detail Group / Model Group）保留行為

- **預設 `preserveGroups: true`**：當 source view 的 detail item 是某 group 的成員時，工具會自動把這些 member id 替換成「group instance id」，讓 `ElementTransformUtils.CopyElements` 把整個 group 當作原子單位複製，目標 view 也會出現對應的 group instance（保留 group 結構，使用者點擊仍是一個 group）。
- 沒有這個處理的話，Revit 預設會把 group 攤平：成員以個別元素複製，目標 view 失去 group 結構。
- **副作用**：如果 group 內混合多種類別（例如同時含 Dimension + DetailLine + TextNote），就算使用者只指定 `elementCategories: ["Dimensions"]`，整個 group（連帶 line、text）都會一起被複製過去——這是保留 group 結構必然的代價。
- 若使用者**確實要攤平**：傳 `preserveGroups: false` 即可恢復舊行為。
- 回傳 `SourceElementSummary.Groups` 顯示這次有多少個 group 被當原子複製進去。

### Dimension 複製專屬注意事項

- **Warning 對話框已自動吞掉**：Transaction 內建 `SuppressWarningsPreprocessor` + `SetForcedModalHandling(false)`，類似「some dimensions don't have the host」、「Some Spot Dimensions were not copied because some References were lost」等 warning 不會跳對話框，會被計入回傳結構的 `SuppressedWarnings`。
- **Spot Dimension（高程點/座標點標註）跨 view 複製成功率低**：reference 元素若在目標 view 不可見/不存在會被自動 skip。回傳的 `WarningSummary` 會看到「Some Spot Dimensions were not copied because some References were lost」。一般 Linear/Aligned dimension 通常 OK。
- **批次失敗時的自動容錯**：預設 `fallbackToIndividual: true`，當 `ElementTransformUtils.CopyElements` 因為某些 dimension 的 host element 在目標 view 找不到（不同 phase / 被裁剪掉 / 連結模型不可見）整批 atomic rollback 時，工具會自動 fallback 為「逐個元素重試」，記錄哪些成功（`Mode: "Individual"`, `Status: "PartialSuccess"`）、哪些失敗（`FailedElements: [{ElementId, Reason}, ...]`）。**使用者只需把 `FailedElements` 清單拿給設計者，他們手動到 source view 看那些 ID 引用的 host 是什麼、決定怎麼補**——不需要 AI 端做二分法定位，這個流程在 v1 時花了 ~10 次 call，現在 1 次 call 直接給答案。
- **個別 fallback 慢於批次**：每個元素一個 transaction，N 個元素 = N 次 commit。資料量大時優先確保批次能成功（檢查 source / target view 配置一致），fallback 是最後保底。
- **不要在跑 dimension 複製時並行其他 MCP 命令**：Revit UI thread 序列化處理，並行會 queue 排隊，後面 30s timeout。

### Timeout 處理

- **timeout ≠ failed**：client 30s 超時但 Revit 端可能仍在處理或已完成。**不要立即重試**，會造成 queue 堆積。
- 處理 timeout：
  1. 切到 Revit 視窗，肉眼確認目標 view 上是否已出現複製的元素
  2. 如已出現 → 視為成功，不需重試
  3. 如未出現且沒對話框跳出 → 串行重試（一次一個 source）
  4. 如有對話框跳出 → 表示 preprocessor 沒生效（罕見），先 dismiss 對話框後重啟 Revit

### 重試前清理

工具不會 dedupe 已複製過去的元素。重試前若目標 view 已有先前失敗/部分成功留下的舊元素，請先在 Revit 手動刪除，避免疊加。
