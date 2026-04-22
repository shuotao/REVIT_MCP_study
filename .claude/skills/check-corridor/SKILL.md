---
name: check-corridor
description: |
  走廊淨寬分析與法規合規檢查，自動偵測走廊、計算寬度、建立標註。
  TRIGGER when: 走廊, corridor, 廊道, 通道, 通道寬度, 逃生, 廊下, 淨寬
user-invocable: true
---

依據 `domain/corridor-analysis-protocol.md` 執行走廊分析。

## Steps

1. 讀取 `domain/corridor-analysis-protocol.md` 取得識別規則與寬度計算演算法
2. `get_active_view` — 確認目前為平面視圖（標註必須在 FloorPlan 建立）
3. `get_rooms_by_level` — 取得當前樓層所有房間
4. 篩選走廊：名稱包含 `走廊`、`Corridor`、`廊道`、`通道`、`廊下`
5. 取得每個走廊的 BoundingBox，計算估計寬度
6. 寬度合規判定：
   - >= 1.6m：合格（主要走廊）
   - >= 1.2m：合格（次要走廊）
   - < 1.2m：不合格
7. `create_dimension` — 在走廊中心線建立標註（需指定正確 ViewId 與 LevelId）
8. 輸出走廊寬度檢查報告

## Error Handling

| 情況 | 處理 |
|------|------|
| 找不到走廊房間 | 列出所有房間名稱，請使用者確認哪些是走廊 |
| 標註無法建立 | 確認視圖類型為 FloorPlan，非 3D 視圖 |
| 斜向走廊寬度誤差 | 標註 `method: bbox_estimate`，提醒精度不足 |

## Related Skills

| 條件 | 建議技能 | 原因 |
|------|---------|------|
| 走廊隔間牆需防火檢查 | `/check-fire-rating` | 走廊隔間牆常為防火區劃牆 |
| 需要視覺化合格/不合格 | `/colorize` | 依寬度值分色標示走廊 |
| 使用者提及容積或送審 | `/check-floor-area` | 走廊面積分類影響容積計算 |
