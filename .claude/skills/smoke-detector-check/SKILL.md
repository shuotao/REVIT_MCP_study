# 消防偵煙探測器設置法規檢討

## Purpose

自動執行偵煙探測器的五項法規檢核，並在 Revit 圖面以顏色標示結果。

## Trigger

當使用者說：
- "檢查偵煙探測器"
- "偵煙設置檢討"
- "探測器有沒有違規"
- "smoke detector check"

## Workflow

1. **確認視圖** — 呼叫 `get_active_view`，確認目前為平面視圖。

2. **執行核心腳本**
   ```
   cd MCP-Server && node scripts/checkSmokeDetector.js
   ```

3. **讀取並回報**
   - 讀取 `MCP-Server/scripts/smoke_detector_report.md`
   - 回報 PASS / FAIL / WARN 數量摘要
   - 提醒使用者查看 Revit 中的顏色標示（綠=通過、紅=違規、橙=缺資料警告）

## 五條法規（依 domain/smoke-detector-check.md）

| # | 條件 | 規範值 |
|---|------|--------|
| 1 | 距出風口（FCU/冷風機） | ≥ 1,500 mm |
| 2 | 距牆壁或樑 | ≥ 600 mm |
| 3 | 探測器下端距天花板 | ≤ 600 mm |
| 4 | 每個樑區格內探測器數 | ≥ 1 個 |
| 5 | 每個探測器負責面積 | < 4m 天花板 ≤ 150㎡；≥ 4m ≤ 75㎡ |

## 自訂關鍵字

若出風口或探測器的 Family 名稱不含預設關鍵字，可傳入覆蓋：

```
node scripts/checkSmokeDetector.js --outletKeywords "冷風機,FCU" --detectorKeywords "偵煙"
```

（目前腳本直接送固定關鍵字；若需自訂，修改腳本的 `Parameters` 物件）

## Notes

- 出風口無法找到時，條件1 標為 WARN（橙色）而非 FAIL，需人工確認。
- 區格涵蓋（條件4）是以房間為單位統計，樑高度判定閾值為 600mm。
- 報告儲存於 `MCP-Server/scripts/smoke_detector_report.md`。
