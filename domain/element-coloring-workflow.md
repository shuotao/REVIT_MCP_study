---
name: element-coloring-workflow
description: "元素上色工作流程：根據參數值對 Revit 元素進行顏色標記與視覺化。當使用者提到上色、顏色標示、colorize、視覺化標記、圖形覆寫時觸發。"
metadata:
  version: "1.0"
  updated: "2025-12-17"
  created: "2025-12-17"
  contributors:
    - "shuotao"
  references: []  # TODO: 月小聚補法規條號或外部依據
  related: []  # TODO: 月小聚補相關 domain（檔名）
  referenced_by:
    - element-coloring
    - element-query
  tags: [上色, 顏色標示, colorize, 視覺化, 圖形覆寫, override]
---

# 元素上色工作流程

執行元素上色任務時，**必須一步一步執行並與用戶討論**。

## 執行前確認事項

在開始任何上色操作前，必須先確認：

1. **視圖類型是什麼？**
   - 平面圖 → 使用切割樣式 (Cut Pattern)
   - 立面圖/剖面圖 → 使用表面樣式 (Surface Pattern)

2. **要用哪個參數分類？**
   - 確認參數的確切名稱（如 `s_CW_防火防煙性能`）

3. **顏色方案是什麼？**
   - 與用戶討論每個參數值對應的顏色

## 標準執行步驟

**必須依序執行，每步完成後等待用戶確認再繼續：**

### 步驟 1: 清除舊顏色
```bash
node clear_walls.js
```

### 步驟 2: 取消牆柱接合
```bash
node step_unjoin.js
```

### 步驟 3: 牆體上色
```bash
node fire_rating_full.js
```

### 步驟 4: 柱子上色（黑色）
```bash
node color_columns_black.js
```

### 步驟 5: 恢復牆柱接合
```bash
node step_rejoin.js
```

## 腳本目錄

`c:\Users\User\Desktop\REVIT MCP\MCP-Server\`

## 顏色方案範例（防火防煙性能）

| 參數值 | 顏色 | RGB |
|--------|------|-----|
| 2 | 綠色 | (0, 180, 0) |
| 1 | 黃色 | (255, 255, 0) |
| (空) | 紫色 | (200, 0, 200) |
| 柱子 | 黑色 | (30, 30, 30) |

## 注意事項

- 所有操作只影響當前視圖
- 操作可逆，使用 `clear_walls.js` 清除
- 修改 C# 後需重新啟動 Revit

## 相關工具

| 命令 | 功能 |
|------|------|
| `override_element_graphics` | 覆寫元素圖形顯示 |
| `clear_element_override` | 清除元素圖形覆寫 |
| `unjoin_wall_joins` | 取消牆柱接合 |
| `rejoin_wall_joins` | 恢復牆柱接合 |
| `query_elements` | 查詢元素 |

## 規範類型對應染色策略（2026-05-22 補）

當染色目的是「視覺化合規 FAIL」時，**規範類型決定染色策略**——同一套 prompt 不能跨類型通用：

### Wall-anchored 規範（限制施加在牆上的開口/段落）

範例：§45/§110 外牆開口距地界線、防火等級分區

**SOP**：
1. 從 `check_exterior_wall_openings` 等 wall-anchored 工具回的 violation list 拆出唯一 wallId（同一面牆多個違規開口算同一道牆）
2. 依 status 染色：Fail 紅 / Warning 黃 / PASS 不染（避免擴大宣稱）
3. 顯式帶 viewId（per 第四憲法 Active Re-Anchoring）

### Room-anchored 規範（限制施加在房間整體屬性）

範例：§41 採光比、§101/§188 排煙、停車淨高、燃料貯存量

**SOP**：
1. **不能直接染 Room** —— `override_element_graphics` 對 Room 在 FloorPlan 是 silent no-op（API Success 但視覺無變化，見 `tool-capability-boundary.md` L6）
2. 改用 **hosting walls proxy**：從 `get_room_daylight_info`（或對應的房間 raw data 工具）拿 FAIL 房 Openings 的 HostWallId 集合，去重後染色
3. 或更上游：在 Revit 設 Color Scheme（脫離 MCP 範圍，但 Revit 給設計師的標準作法）

**Anti-pattern**：直接 `override_element_graphics(roomId, ...)` —— 5/22 dry-run 實證 silent no-op。

### 判斷流程

染色前**先讀對應 domain 檔**（fire-rating-check.md / daylight-area-check.md / smoke-exhaust-review.md 等）→ 看規範是「對牆下限制」還是「對房間下限制」→ 走對應策略。

詳細的兩條 SOP + 失敗模式見 `domain/tool-capability-boundary.md` L8、`domain/lessons.md` L-027。
