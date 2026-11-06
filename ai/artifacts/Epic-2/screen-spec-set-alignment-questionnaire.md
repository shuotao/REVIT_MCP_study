# 畫面規格 - Revit 材料 Set 需求對齊問答 Modal

## Metadata

- 功能：Task 004 Set 需求對齊（網頁問答互動引導）
- 畫面：`assets/green-material-showcase.html` -> `#setQuestionnaireModal`
- 狀態：已實作

## 目的

提供使用者在將專案材料 Set 匯出或推送至 AI Agent 之前，以網頁三題問答（組合方式、Revit品類、補充條件）明確表達 BIM 寫入需求，並將解答寫入 `exported_material_sets.json` 供 Agent 經由 `/GMimport` 指令直接解析。

## 版面配置

- 標題區：Modal 標題與簡短說明提示
- 主要問答區：
  - Q1: 此 Set 是否需要做組合元件 （單一組合 or 各別建立） [單選/勾選]
  - Q2: 此 Set 為哪種類型 (Floor / Wall / Ceiling / Door / Window / Material Only) [單選/勾選]
  - Q3: 是否補充條件 [無 / 其他(展開輸入框)]
- 動作按鈕區：
  - 取消
  - ✅ 完成問答並匯出給 AGENT

## 狀態

| 狀態 | 必要行為 | 空狀態／錯誤文案 | 驗證方式 |
|---|---|---|---|
| 預設 | 展開三題問答，Q1預設組合式，Q2預設Wall，Q3預設無 | 尚無選擇 | 螢幕截圖 |
| Q3勾選其他 | 動態展開 text input 輸入框 | 請輸入補充條件 | 手動操作驗證 |
| 送出 | 寫入 materialSets[key] 並觸發 exportSetsToAgent | 請至少勾選各題選項 | 檢查 console 與 JSON 產出 |

## 互動

| 動作 | 觸發條件 | 結果 | 失敗情境 |
|---|---|---|---|
| 打開對話框 | 點擊卡片上「🤖 回傳至 AGENT」或「📤 匯出至 Agent」 | 彈出 #setQuestionnaireModal 填寫問答 | 彈窗未開啟 |
| 完成送出 | 點擊「✅ 完成問答並匯出給 AGENT」 | 寫入 purpose 與 alignmentQuestionnaire，彈出 #pushNoticeModal 提供 /GMimport | JSON寫入失敗 |

## 設計系統對照

- 用到的既有 design token：`var(--bg-card)`, `var(--text-primary)`, `var(--accent-blue)`, `var(--accent-green)`
- 用到的既有元件：Modal Backdrop, Modal Content, Primary Button, Form Radio/Checkbox Groups
- 本畫面新做的元件：Set 需求對齊 3 題問答組件（登記至 `ai/context/design-system.md`）

## 視覺驗收標準

- 文字在手機版與桌面版都不會被截斷。
- 主要動作清楚明確。
- 色彩、字體、間距取自專案既有 dark mode 視覺風格。
