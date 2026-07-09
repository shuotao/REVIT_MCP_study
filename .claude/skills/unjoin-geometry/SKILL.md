---
name: unjoin-geometry
description: "批次解除 Revit 幾何接合（Join Geometry）。以任一類別為中心解除其與指定 target 類別的接合關係，預設 target 涵蓋 8 大類（牆、樓板、建築柱、結構柱、結構構架、基礎、屋頂、天花板）。適用於渲染白模前置、幾何分析、上色前清理接合。提供「跑兩次驗證」、「交叉檢查來源數量」的 SOP，提醒 session-scoped 還原限制。觸發條件：使用者提到解除接合、取消結合、取消接合、解除幾何結合、批次斷開接合、unjoin、unjoin geometry、白模準備、render preparation。"
---

# 解除幾何接合批次處理

執行前請先讀取 `domain/unjoin-geometry-workflow.md` 了解三代工具演化、搜尋策略與還原限制。

## Prerequisites

1. Revit 已開啟專案
2. MCP Server 已連線（`ToolSearch select:mcp__revit-mcp__unjoin_element_joins`）
3. 確認使用者目標：白模渲染 / 上色前置 / 幾何分析 / 出圖準備

## Workflow

### 步驟 1：確認範圍

與使用者對齊兩個維度：

1. **Source Category**（來源類別）：
   - 柱 → `StructuralColumns` 或 `Columns`
   - 樑 → `StructuralFraming`
   - 牆 → `Walls`
   - 樓板 → `Floors`
   - 其他 → 使用者指定，或請其在 Revit 選一個元素查 Category

2. **Target Categories**（目標類別陣列，選填）：
   - 預設 8 類涵蓋絕大部分結構/建築元件
   - 保守選擇：只留 `Walls`, `Floors`, `StructuralFraming`, `StructuralColumns`
   - 徹底選擇：使用預設（8 類全納入）
   - 若使用者不確定，預設 8 類即可

**必須明示還原限制：** 跟使用者說「解除後的還原資料只存在當前 Revit session，**關掉 Revit 就無法還原**，要永久保留狀態直接存檔」。

### 步驟 2：執行解除

呼叫 `unjoin_element_joins`：

```
unjoin_element_joins({
  sourceCategory: "StructuralFraming",
  // targetCategories: 省略則使用 8 類預設
})
```

回應關鍵欄位：
- `UnjoinedCount` / `SourceCount` / `PairsByCategory` / `StoredPairs`

### 步驟 3：驗證（強制兩次呼叫）

**立刻再呼叫一次相同參數**：

```
unjoin_element_joins({ sourceCategory: "StructuralFraming" })
```

- `UnjoinedCount: 0` → 搜尋範圍內無殘留 ✅
- `UnjoinedCount > 0` → 第一次漏掉，本次補上（罕見）

### 步驟 4：交叉檢查來源數量

呼叫 `query_elements_with_filter(category=<sourceCategory>)`，比對：
- 查詢回傳的 `Count` 應等於第一次 `unjoin_element_joins` 的 `SourceCount`
- 不一致 → 檢查是否有 in-place family 或特殊 family 不在類別內，跟使用者討論

### 步驟 5：回報結果與存檔建議

產出三段摘要給使用者：

1. **數字摘要**：處理元素數、解除接合總數、各類別拆解
2. **交叉驗證結果**：兩次呼叫的 count 與查詢總數是否一致
3. **存檔建議**：
   - 要保留「全解除」狀態 → 直接存 `.rvt`
   - 反悔 → 立刻呼叫 `rejoin_wall_joins`（**不要關 Revit**）
   - 想做局部還原 → 無法，現有機制是 all-or-nothing

### 步驟 6（選配）：延伸類別

若使用者接著想處理其他類別（例如做完樑之後想做牆），直接重複步驟 1-5，`sourceCategory` 換成新類別。PairKey 會自動去重已處理過的配對。

## 組合使用

| 目的 | 搭配 Skill |
|------|-----------|
| 渲染白模（Enscape/V-Ray） | `/batch-material` — 先解除接合，再用複製原材質模式換白 |
| 元素上色（平面圖切割） | `/element-coloring` — 上色前解除接合避免顏色混淆 |
| 防火時效視覺化 | `/fire-safety-check` — 若接合遮蔽顏色邊界可先解除 |

## 工具

| 工具名稱 | 用途 |
|---------|------|
| `unjoin_element_joins` | 通用版解除（預設 8 類 target） |
| `unjoin_column_joins` | 柱本位特化版（保留向後相容） |
| `unjoin_wall_joins` | 牆-柱雙邊特化版（保留向後相容） |
| `rejoin_wall_joins` | 還原所有累計的 `_unjoinedPairs`（session-scoped） |
| `query_elements_with_filter` | 交叉檢查來源類別總數 |

## 注意事項

- **Session-scoped 還原**：`_unjoinedPairs` 靜態欄位僅存活於 Revit process 記憶體
- **PairKey 去重跨呼叫生效**：多次不同 source 不會重複解除同一對
- **連結模型不處理**：只作用於當前 doc
- **大專案**：上千元素可能需要數十秒，呼叫期間不要介入其他寫入型工具

## Reference

詳見 `domain/unjoin-geometry-workflow.md`（包含 BoundingBox 策略、實戰數據、與其他 Skill 的互動矩陣）。
