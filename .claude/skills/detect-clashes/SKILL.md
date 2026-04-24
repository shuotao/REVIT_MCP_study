---
name: detect-clashes
description: |
  MEP 管線與結構（CSA）碰撞偵測，使用 Curve-to-Solid 策略進行干涉分析、視覺化與報告匯出。
  TRIGGER when: 碰撞, 干涉, clash, MEP, 管線穿牆, 套管, 穿越, penetration, 碰撞偵測, 管線衝突
user-invocable: true
---

依據 `domain/mep-csa-clash-detection.md` 執行 MEP vs CSA 碰撞偵測。

## Prerequisites

- MEP 模型已連結至當前專案（CSA 為主模型）
- BEP 規定連結模式為「原點到原點」或「共用座標」

## Steps

1. 讀取 `domain/mep-csa-clash-detection.md` 取得完整 5 階段 SOP
2. **Phase 1 — 環境偵察**：
   - `get_linked_models` — 找到 MEP 連結的 LinkInstanceId
   - `query_linked_elements` — 取樣確認能讀取管線
   - `get_active_schema` — 確認 CSA 結構品類數量
3. **Phase 2 — 範圍界定**（與使用者互動）：
   - 確認 MEP 品類（Pipes / Ducts / CableTrays）
   - 確認管徑門檻、系統篩選、樓層範圍
   - 確認 CSA 對象（牆 / 樑 / 柱 / 板）
4. **Phase 3 — 碰撞運算**：
   - `detect_clashes` — 執行 Curve-to-Solid 碰撞偵測
5. **Phase 4 — 結果分析**：
   - 統計摘要、系統熱點、結構風險排序（柱 > 樑 > 板 > 牆）
   - 貫穿深度 > 500mm 特別警告
6. **Phase 5 — 視覺化與報告**：
   - `colorize_clashes` — 依 CSA 類別上色（柱紅/樑橘/板黃/牆藍）
   - `export_clash_report` — 匯出 CSV/JSON 報表
   - `select_element` + `zoom_to_element` — 逐筆導覽

## Error Handling

| 情況 | 處理 |
|------|------|
| 無連結模型 | 提示使用者先連結 MEP 模型 |
| 碰撞數量 > 1000 | 建議縮小範圍（限定樓層或系統） |
| 連結座標偏移 | 檢查 BEP 連結模式設定 |

## Related Skills

| 條件 | 建議技能 | 原因 |
|------|---------|------|
| 碰撞涉及防火牆 | `/check-fire-rating` | 管線穿越防火牆需設防火填塞 |
| 需要更精細上色 | `/colorize` | 依系統或嚴重度自訂配色 |
| 碰撞影響停車淨高 | `/check-parking-clearance` | 管線降低車位淨高 |
