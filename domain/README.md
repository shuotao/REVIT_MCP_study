# domain/ 領域知識目錄

此目錄存放業務流程、工作流程和法規參考資料。

---

## 📂 目錄結構

| 檔案 | 說明 | 狀態 |
|-----|------|:----:|
| `floor-area-review.md` | 容積檢討流程 | ✅ |
| `fire-rating-check.md` | 防火等級檢查流程 | ✅ |
| `corridor-analysis-protocol.md` | 走廊分析流程 | ✅ |
| `element-coloring-workflow.md` | 元素上色流程 | ✅ |
| `room-boundary.md` | 房間邊界計算 | ✅ |
| `wall-check.md` | 牆壁檢查機制 | ✅ |
| `path-maintenance-qa.md` | 路徑維護 QA | ✅ |
| `references/building-code-tw.md` | 台灣建築法規摘要 | ✅ |

---

# 容積檢討工具開發計劃

> ⚠️ **注意**：以下為開發計劃，列出的工具**尚未全部實作**。目前已實作的工具請參考 README.md 的工具清單。

---

## 開發原則

1. **工具保持原子化**：不做高階整合工具，維持獨立工具讓 AI 自己判斷如何組合
2. **知識外掛化**：法規 RAG 提供提醒，不干預執行邏輯
3. **視覺化優先**：結果在圖面上呈現，讓使用者確認
4. **持續學習**：從成功經驗建立策略（3H 學習機制）

---

## 完整流程

### 階段 0：模型品質檢查

| 工具 | 功能 |
|-----|------|
| classify_walls | 區分內牆與外牆 |
| check_wall_orientation | 檢查外牆內外側方向 |
| highlight_walls_by_status | 用顏色標記牆壁狀態 |
| clear_wall_highlights | 清除顏色標記 |
| flip_wall | 翻轉牆的方向 |

### 階段 1：房間資料檢查

| 工具 | 功能 |
|-----|------|
| get_rooms_by_level | 取得某樓層所有房間 |
| get_room_boundaries | 取得房間的邊界元素 |
| check_room_usage | 檢查房間用途是否完整 |
| highlight_rooms_by_status | 用顏色標記房間狀態 |

### 階段 2：邊界調整

| 工具 | 功能 |
|-----|------|
| get_wall_thickness | 取得牆的厚度資訊 |
| calculate_boundary_offset | 計算邊界偏移量 |
| create_area_scheme | 建立面積方案 |
| create_area_boundary | 建立面積邊界線 |

### 階段 3：面積計算

| 工具 | 功能 |
|-----|------|
| calculate_floor_area | 計算樓層總面積 |
| generate_area_report | 產生面積報告 |

---

## 視覺化工具

| 工具 | 視覺輸出 |
|-----|---------|
| highlight_walls_by_status | 牆壁顏色覆寫 |
| highlight_rooms_by_status | 房間顏色覆寫 |
| show_area_boundary | 邊界線顯示 |
| place_area_tag | 面積標籤 |

---

## 3H 學習機制

| 指令 | 功能 |
|-----|------|
| /learn | 記錄成功的工具呼叫策略 |
| /review | 回顧過去的成功案例 |
| /check | 檢查目前做法是否符合過去經驗 |

---

## 開發優先順序

1. get_rooms_by_level（計算房間面積）
2. classify_walls（區分內外牆）
3. check_wall_orientation（檢查牆壁方向）
4. highlight_walls_by_status（視覺化標記）
