# Mockup 決策 - Set 需求對齊問答 Modal

## Metadata

- 功能：Task 004 Set 需求對齊 (問答對話框)
- 畫面：`assets/green-material-showcase.html`
- 決策負責人：shuotao / antigravity
- 狀態：已選定

## 變體

| 變體 | 說明 | 優點 | 風險 |
|---|---|---|---|
| A (Modal問答) | 在點擊推送/匯出時開啟用單選與勾選框的三題對話框 | 互動直覺、避免使用者遺漏需求對齊參數 | 無 |
| B (原生Alert) | 使用 prompt() 輸入框 | 開發簡單 | 體驗較差，無法多題勾選 |

## 設計系統對照

- 重用的 token／元件：`Modal-Backdrop`, `Button`, `Radio-Card-Group`
- 新做並登記回 inventory 的元件：`Questionnaire-Modal-Form`

## 選定的變體

- 變體：變體 A (Modal 問答)
- 為何選這個：符合使用者明確指定的 Q1/Q2/Q3 網頁問答互動需求。
- 實作前要求的修改：無。

## 人工核准

- 核准者：shuotao
- 日期：2026-08-02
- 備註：已通過 TASK-004 功能簽核。
