---
name: check-parking-clearance
description: |
  停車空間淨高檢查，驗證停車位上方淨高是否達到 210cm 以上。
  TRIGGER when: 停車淨高, parking clearance, 車位高度, 淨高檢查, 停車場高度
user-invocable: true
---

依據 `domain/parking-clearance-check.md` 執行停車淨高檢查。

## Steps

1. 讀取 `domain/parking-clearance-check.md` 取得檢查標準（> 210cm）
2. `get_active_view` — 確認目前為地下層視圖
3. `query_elements` (category: Parking) — 取得停車位元素
4. 對每個停車位：
   - 取得 BoundingBox 中心點
   - 計算該點到上方最近障礙物的距離（天花板、樑、管線、上層樓板）
   - 淨高 = 障礙物距離
5. 合規判定：
   - > 210cm → 合格（保持原色）
   - <= 210cm → 不合格
6. `override_element_graphics` — 不合格車位標示紅色 (255, 0, 0)
7. 輸出淨高檢查報告

## Error Handling

| 情況 | 處理 |
|------|------|
| 找不到 Parking 類別元素 | 提示使用者確認停車位元件類別 |
| 射線未命中障礙物 | 標記為「無上方遮蔽」，可能為戶外車位 |

## Related Skills

| 條件 | 建議技能 | 原因 |
|------|---------|------|
| 需要同時檢討車位數量 | `/check-parking` | 淨高與數量通常一併檢討 |
| 有 MEP 管線影響淨高 | `/detect-clashes` | 管線穿越結構可能降低淨高 |
