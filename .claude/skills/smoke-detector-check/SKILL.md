---
name: smoke-detector-check
description: "消防偵煙探測器設置法規檢討：出風口距離、壁樑距、天花板貼附、區格涵蓋、探測面積五項規定檢核並於 Revit 圖面上色標示。觸發條件：使用者提到偵煙探測器、偵煙設置、探測器違規、smoke detector check、消防探測器。工具：analyze_smoke_detectors、visualize_detector_results、get_active_view。"
metadata:
  references:
    - domain/smoke-detector-check.md
---

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

以 MCP 工具編排執行（不要繞過 MCP 直連 WebSocket）：

1. **確認視圖** — 呼叫 `get_active_view`，確認目前為平面視圖。

2. **萃取資料** — 呼叫 `analyze_smoke_detectors`，取得每個探測器的距出風口距離、距牆/樑距離、距天花板距離，以及各房間的探測器數量與樑區格數。若探測器或出風口的 Family 名稱不含預設關鍵字，以 `detectorKeywords` / `outletKeywords` 參數覆蓋。

3. **五項法規判定** — 依 `domain/smoke-detector-check.md` 的方法逐項判定（domain 檔定義方法，不用模型自身知識）：

   | # | 條件 | 規範值 |
   |---|------|--------|
   | 1 | 距出風口（FCU/冷風機） | ≥ 1,500 mm |
   | 2 | 距牆壁或樑 | ≥ 600 mm |
   | 3 | 探測器下端距天花板 | ≤ 600 mm |
   | 4 | 每個樑區格內探測器數 | ≥ 1 個 |
   | 5 | 每個探測器負責面積 | < 4m 天花板 ≤ 150㎡；≥ 4m ≤ 75㎡ |

   出風口資料缺失時，條件 1 標為 WARN 而非 FAIL。

4. **視覺化** — 將判定結果以 `results` 陣列（每筆含 `DetectorId`、`IsOk`，可選 `IsWarn`、`Message`）傳給 `visualize_detector_results` 上色：綠=PASS、紅=FAIL、橙=WARN。

5. **回報** — 輸出 PASS / FAIL / WARN 數量摘要與逐項違規清單，引用 `domain/smoke-detector-check.md` 的條號，並提醒使用者查看 Revit 中的顏色標示。

## Notes

- 區格涵蓋（條件 4）是以房間為單位統計，樑高度判定閾值為 600mm。
- `MCP-Server/scripts/checkSmokeDetector.js` 為開發期驗證腳本（raw WebSocket，dev-only）；AI 工作流一律走上述 MCP 工具，不執行該腳本。
