# 測試驗證

在宣告任務完成前使用此流程。

## 步驟

1. 閱讀任務卡中的驗證契約。
2. 確定範圍最小但仍有效的檢查項目。
3. 先執行針對性的測試。
4. 於必要時執行 lint／型別檢查（typecheck）／建置。
5. 對於 UI 變更，若工具允許，擷取桌面版與行動裝置版的螢幕截圖。
6. 對照 `ai/checklists/testing-checklist.md` 逐項確認（測試是否會在沒修復前失敗、邊界案例與權限/錯誤路徑是否覆蓋等）；UI 變更另對照 `ai/checklists/design-review-checklist.md`。
7. 以 `ai/templates/verification-report.md` 為範本記錄證據，產出到 `ai/artifacts/<Epic>/verification/<卡片id>.md`（見 `ai/artifacts/README.md`）。

## 輸出

- 已執行的指令。
- 通過／失敗摘要。
- 螢幕截圖備註。
- 涵蓋範圍缺口。
- 殘留風險。
- 任務是否符合完成定義（definition of done）。
