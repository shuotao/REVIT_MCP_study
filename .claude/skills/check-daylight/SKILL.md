---
name: check-daylight
description: |
  居室採光面積檢討，依據建築技術規則第41條驗證採光比是否合規。
  TRIGGER when: 採光, daylight, 居室採光, 開窗面積, 第41條, 採光面積
user-invocable: true
---

依據 `domain/daylight-area-check.md` 執行採光面積檢討。

## Steps

1. 讀取 `domain/daylight-area-check.md` 取得第41條法規標準與計算規則
2. `get_active_view` — 確認目前樓層
3. `query_elements` (category: Rooms) — 取得居室清單（篩選臥室、起居室、教室等）
4. `query_elements` (category: Windows) — 取得窗戶清單
5. 對每個居室：
   - 找出圍繞牆體上的對外窗戶
   - 計算有效採光面積（扣除75cm以下、天窗×3、深陽台×0.7）
   - 計算採光比 = 有效採光面積 / 房間面積
6. 合規判定：
   - 學校/幼兒園：>= 1/5 (20%)
   - 住宅/其他：>= 1/8 (12.5%)
7. `override_element_graphics` — 合格綠色、不合格紅色
8. 輸出採光檢討報告

## Error Handling

| 情況 | 處理 |
|------|------|
| 無法判斷居室用途 | 詢問使用者確認（學校 or 住宅） |
| 窗戶無法關聯到房間 | 透過牆體 HostId 間接關聯 |
| 無對外窗戶 | 標記該居室為「無自然採光」 |

## Related Skills

| 條件 | 建議技能 | 原因 |
|------|---------|------|
| 需複查外牆開口合規性 | `/check-openings` | 採光開口同時受第45條距離限制 |
| 使用者提及送審 | `/check-floor-area` | 採光檢討常與容積檢討一併送審 |
| 需要視覺化標示 | `/colorize` | 更精細的採光比分色 |
