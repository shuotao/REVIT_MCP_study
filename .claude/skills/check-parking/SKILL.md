---
name: check-parking
description: |
  停車位數量分類統計與法定需求計算，依建築技術規則驗證停車位設置是否合規。
  TRIGGER when: 停車位, 法定停車, parking, 車位數量, 停車檢討, 無障礙停車, 機車位
user-invocable: true
---

依據 `domain/parking-space-review.md` 執行停車位檢討。

## Steps

1. 讀取 `domain/parking-space-review.md` 取得分類標準與法定計算公式
2. `get_active_view` — 確認目前環境
3. `query_elements` (category: Parking) — 取得所有停車位元素
4. 分類統計：依 `停車位類型` 參數分為法定、無障礙、增設、裝卸、獎勵、機車、無障礙機車、大客車
5. 標記未分類車位（參數空白或不符標準）
6. 計算法定需求：
   - 詢問使用者：總樓地板面積、免計面積、建築用途類別、是否在都市計畫區
   - 公式：應設車位 = ceil((有效面積 - 免設門檻) / 設置基準)
7. 比對：設計數量 vs 法定需求
8. 輸出停車位檢討報告

## Error Handling

| 情況 | 處理 |
|------|------|
| 無 `停車位類型` 參數 | 建議使用 Comments 欄位替代，或提示需先建立參數 |
| 多用途建築 | 依 domain 說明分別計算，免設門檻擇一適用 |
| 國際觀光旅館 | 額外計算大客車需求（每50間客房1輛）|

## Related Skills

| 條件 | 建議技能 | 原因 |
|------|---------|------|
| 需要檢查淨高 | `/check-parking-clearance` | 數量合格但淨高不足仍不合規 |
| 法定停車依樓地板面積計算 | `/check-floor-area` | 需要先確認總樓地板面積 |
