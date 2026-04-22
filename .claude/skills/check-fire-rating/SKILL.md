---
name: check-fire-rating
description: |
  驗證牆體防火時效是否符合建築技術規則。
  TRIGGER when: 防火, 耐燃, fire rating, fireproofing, 防火時效, 防火區劃, 消防
user-invocable: true
---

依據 `domain/fire-rating-check.md` 執行防火時效檢查。

## Steps

1. 讀取 `domain/fire-rating-check.md` 取得法規標準表（建築類型 vs 防火時效要求）
2. `get_active_view` — 確認目前視圖與樓層
3. `get_all_levels` — 取得所有樓層清單
4. `query_elements` (category: Walls) — 取得目標樓層的牆體
5. `get_element_info` — 逐牆取得材質、厚度、防火時效參數
6. 比對法規標準：依建築用途判定每面牆的時效是否合格
7. `override_element_graphics` — 不合格牆標示紅色 (255, 0, 0)
8. 輸出防火等級檢查報告（總牆數、合格數、不合格清單）

## Error Handling

| 情況 | 處理 |
|------|------|
| 牆體無防火時效參數 | 標記為「未設定」，列入報告警告區 |
| 無法判斷建築用途 | 詢問使用者確認用途類型 |
| 參考 `domain/tool-capability-boundary.md` | 查詢失敗超過 3 次應停止並回報 |

## Related Skills

當本技能完成後，根據結果建議使用者是否需要執行以下關聯技能：

| 條件 | 建議技能 | 原因 |
|------|---------|------|
| 發現不合格外牆 | `/check-openings` | 外牆防火不足時，開口部也需複查（第110條） |
| 檢查涉及走廊隔間牆 | `/check-corridor` | 防火區劃與走廊寬度常連動 |
| 需要更精細的視覺化 | `/colorize` | 依防火時效值分色標示（多色階） |
| 使用者提及「送審」 | `/check-floor-area` | 送審通常需要容積 + 防火一併檢查 |
