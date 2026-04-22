---
name: check-wall
description: |
  牆體內外方向檢查，偵測 Revit 牆壁的 Exterior/Interior 面是否正確。
  TRIGGER when: 牆體方向, wall orientation, 內外側, 牆壁檢查, 外牆方向, 飾面方向
user-invocable: true
---

依據 `domain/wall-check.md` 執行牆體方向檢查。

## Steps

1. 讀取 `domain/wall-check.md` 取得判斷邏輯
2. `get_active_view` — 確認目前樓層與視圖
3. `query_elements` (category: Walls) — 取得目標樓層牆體
4. 區分內牆與外牆：
   - 檢查 Function 參數（Exterior / Interior）
   - 檢查牆體類型名稱
5. 對外牆進行方向判斷：
   - 射線檢測：從 Exterior 側發射射線，檢查是否碰到其他元素
   - 房間檢測：Exterior 側不應有房間
6. `override_element_graphics` — 依狀態上色：
   - 綠色：方向正確
   - 紅色：方向可能錯誤
   - 黃色：需人工確認
   - 藍色：內牆（不檢查方向）
7. 輸出牆體方向檢查報告

## Error Handling

| 情況 | 處理 |
|------|------|
| Function 參數未設定 | 依類型名稱推斷，標記為黃色「不確定」 |
| 無法射線偵測 | 降級為僅依參數判斷，標記精度限制 |

## Related Skills

| 條件 | 建議技能 | 原因 |
|------|---------|------|
| 確認方向後需上色 | `/colorize` | 依其他參數做更細緻的視覺化 |
| 外牆需防火檢查 | `/check-fire-rating` | 牆體方向正確後才能準確判斷防火需求 |
| 外牆有開口需檢討 | `/check-openings` | 方向確認後再檢查開口合規性 |
