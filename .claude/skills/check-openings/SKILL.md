---
name: check-openings
description: |
  外牆開口法規檢討，依據建築技術規則第45條（鄰地距離）與第110條（防火間隔）進行合規驗證。
  TRIGGER when: 外牆開口, 鄰地距離, 防火間隔, 開口檢討, 第45條, 第110條, 境界線, opening
user-invocable: true
---

依據 `domain/exterior-wall-opening-check.md` 執行外牆開口檢討。

## Steps

1. 讀取 `domain/exterior-wall-opening-check.md` 取得第45條與第110條法規判定邏輯
2. `get_active_view` — 確認目前樓層與視圖
3. `query_elements` (category: Walls) — 識別外牆（Function = Exterior）
4. `query_elements` (category: Windows, Doors) — 取得外牆上的開口
5. 定義基地邊界（Property Line 或詢問使用者手動輸入座標）
6. 計算距離：每個開口中心點 → 基地境界線最短距離
7. 第45條判定：
   - 距境界線 >= 1.0m → 合格
   - < 1.0m 且非玻璃磚 → 違規
8. 第110條判定：
   - < 1.5m → 需1hr防火時效 + 防火門窗
   - 1.5m~3.0m → 需0.5hr防火時效
   - >= 6m 道路/空地 → 免除
9. `override_element_graphics` — 違規紅色、警告橘色、合格綠色
10. `create_dimension` — 標註開口到境界線距離
11. 輸出外牆開口檢討報告

## Error Handling

| 情況 | 處理 |
|------|------|
| 找不到 Property Line | 詢問使用者手動輸入基地邊界座標 |
| 無法區分內外牆 | 用 `/check-wall` 先確認牆體方向 |
| 開口無 HostId | 透過空間位置推算所屬牆體 |

## Related Skills

| 條件 | 建議技能 | 原因 |
|------|---------|------|
| 外牆防火時效不足 | `/check-fire-rating` | 第110條要求外牆本身也需防火時效 |
| 開口同時涉及採光 | `/check-daylight` | 同一開口可能同時是採光窗 |
| 需要視覺化結果 | `/colorize` | 更精細的距離分色標示 |
| 無法確認牆體內外側 | `/check-wall` | 先確認牆體方向再判斷外牆開口 |
