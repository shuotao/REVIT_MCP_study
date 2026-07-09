---
name: unjoin-geometry-workflow
description: "批次解除 Revit 幾何接合（Join Geometry）的 SOP：三代工具演化（unjoin_wall_joins / unjoin_column_joins / unjoin_element_joins）、BoundingBox 外擴搜尋策略、跑兩次驗證與 session-scoped 還原限制。觸發：解除接合、unjoin geometry、白模準備、render preparation。"
metadata:
  version: "1.0"
  updated: "2026-04-21"
  created: "2026-04-21"
  contributors:
    - "lesleyliuke"
  references: []  # TODO: 月小聚補外部依據
  related:
    - dedup-detail-elements-workflow.md
  referenced_by:
    - unjoin-geometry
  tags: [幾何接合, unjoin, join-geometry, 白模, render, 上色, 接合]
---

# 解除幾何接合工作流程

在 Revit 中，「Join Geometry」讓兩個實體元素共享一條幾何邊界（例如柱埋入牆、樑切入柱）。批次解除接合是渲染白模、上色、幾何分析、出圖前置的常見需求。

## 工具演化（三代）

| 工具 | 範圍 | 對稱性 | 使用時機 |
|------|------|--------|----------|
| `unjoin_wall_joins` | 牆 → 鄰近柱（BBox 外擴 1ft） | 牆本位（單向） | 只處理牆-柱，已被下一代涵蓋 |
| `unjoin_column_joins` | 柱 → 鄰近牆/樓板/結構樑 | 柱本位（多 target） | 專注柱子，比 wall_joins 多 target |
| `unjoin_element_joins` | 任一類別 → 任一類別陣列 | **通用版** | 新任務優先用此 |

**本 domain 文件以 `unjoin_element_joins` 為核心**。前兩代保留是為向後相容與現有 `/element-coloring` skill 的依賴。

## 核心設計

### 搜尋策略：BoundingBox 外擴 1 ft

```
source element 的 bbox → 外擴 1 ft（30.48 cm）→ 作為搜尋範圍
  → WherePasses(BoundingBoxIntersectsFilter)
  → 對每個候選 neighbor 呼叫 AreElementsJoined()
  → 若為 true 則 UnjoinGeometry
```

**為什麼 1 ft？** 沿用 `unjoin_wall_joins` 的經驗值，可涵蓋絕大部分實際接合情境。

**盲點：** 若接合的兩元素 bbox 實際上完全不重疊（極罕見，通常是建模異常），會被搜尋漏掉。但 `AreElementsJoined` 的布林檢查仍會正確過濾 bbox 內的**假陽性**（碰到但沒 join）。

### PairKey 跨呼叫去重

靜態欄位 `_unjoinedPairs` 累計所有已解除的 pair。每次呼叫：
1. 從 `_unjoinedPairs` 建構 `existingPairs` HashSet
2. 以 `PairKey(a.Id, b.Id)`（排序後的 id-id 字串）做為識別
3. 迴圈中若 pair key 已存在則跳過

**效果：** 多次呼叫不同 source category，不會重複解除同一對接合（例如柱-樑從柱本位處理後，從樑本位再處理時會自動跳過）。

### 單一 Transaction

整次呼叫包在 `Transaction "Unjoin {source} Geometry"` 內，Revit Undo 堆疊只佔一格。

## 8 大預設 target 類別

若不傳 `targetCategories`，使用以下預設：

```
Walls, Floors, Columns, StructuralColumns,
StructuralFraming, StructuralFoundation, Roofs, Ceilings
```

對應 Revit BuiltInCategory：`OST_Walls`、`OST_Floors`、`OST_Columns`、`OST_StructuralColumns`、`OST_StructuralFraming`、`OST_StructuralFoundation`、`OST_Roofs`、`OST_Ceilings`。

## 常見範圍組合

| 目的 | sourceCategory | targetCategories |
|------|---------------|------------------|
| 白模渲染準備（保守） | `Walls` | 預設 |
| 白模渲染準備（徹底） | 依序對所有類別跑一次 | 預設 |
| 柱專屬清理 | `StructuralColumns` | 預設 |
| 樑專屬清理 | `StructuralFraming` | 預設 |
| 只斷開結構互接 | `StructuralFraming` | `["StructuralColumns", "StructuralFraming"]` |
| 避免基礎 / 屋頂 | 任一 | 從預設移除 `StructuralFoundation`, `Roofs`, `Ceilings` |

## 還原機制

| 機制 | 限制 |
|------|------|
| `rejoin_wall_joins` | 從 `_unjoinedPairs` 逐對 Join 回去 |
| 作用範圍 | 所有累計的 pair（來自任何 source category 的歷次呼叫） |
| **生命週期** | 僅存於當前 Revit session 的靜態欄位記憶體中 |
| **Revit 關閉後** | `_unjoinedPairs` 歸零，**無法再還原** |

**策略：**
- 需要反覆試錯 → 不要關 Revit，用 `rejoin_wall_joins` 還原
- 要保留「全解除」狀態 → 直接存檔，幾何狀態會隨 `.rvt` 寫入
- 要永久還原 → 在 Revit UI 手動逐對 Join Geometry（繁瑣），或存檔前先 rejoin

## 驗證策略

### 策略 1：跑第二次（必做）

呼叫相同 `unjoin_element_joins(sourceCategory=...)` 第二次。因 PairKey 去重：
- `UnjoinedCount: 0` → 搜尋範圍內無殘留 ✅
- `UnjoinedCount > 0` → 第一次漏掉，第二次捕捉到（罕見，通常是 bbox 邊界情況）

### 策略 2：交叉檢查 SourceCount

用 `query_elements_with_filter(category=...)` 查詢來源類別的總數，與工具回報的 `SourceCount` 比對：
- 一致 → 所有來源元素都有被處理 ✅
- 不一致 → 有漏掉（可能是自訂類別、in-place family、連結模型）

### 策略 3：Revit UI 目視

切到 3D 視圖隨機選幾個來源元素，觀察：
- 原本應該融合的邊界是否出現切割線
- Tab-select 時相鄰元素是否不再被自動含入

## 與其他 Skill 的互動

- **`/batch-material`（渲染白模）**：先解除接合再換材質，避免接合導致顏色混淆
- **`/element-coloring`**：`unjoin_wall_joins` 是既有依賴，新流程可用 `unjoin_element_joins` 替代
- **`/fire-safety-check`**：防火時效視覺化前，若接合導致顏色邊界不清，可先解除

## 實戰數據參考

某 19 根柱 + 170 根樑的小型專案，徹底清理接合的實際數據：

| 來源 | 解除數量 | 主要 target |
|------|---------|-------------|
| 柱 → 預設 8 類 | 738 對 | 樑 456、牆 282、樓板 0 |
| 樑 → 預設 8 類（柱-樑已去重） | 1211 對 | 牆 523、樑-樑 290、結構柱 277、樓板 121 |

**觀察：** 樑的接合密度遠高於柱（每根樑平均 7.1 對接合 vs 每根柱平均 38.8 對），因為樑與牆、樑與樑的 T/十字接頭眾多。

## 注意事項

- **Transaction 包覆**：呼叫中不可嵌套其他寫入型 MCP 工具
- **大專案效能**：數千元素的專案單次呼叫可能需要數十秒，依 bbox 搜尋密度而定
- **自訂類別**：工具透過 `Enum.TryParse<BuiltInCategory>` 解析類別字串，未來若 Revit 新增類別可直接傳入類別名稱（如 `"Parts"`），不需改程式碼
- **連結模型不處理**：只處理當前 doc 的元素，連結模型內部的 join geometry 不會被觸及
