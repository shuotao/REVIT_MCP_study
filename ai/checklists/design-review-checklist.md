# 設計審查檢查清單

UI 變更交付前、mockup 關卡審查時，或使用者抱怨「醜」時逐項打勾。**不要憑感覺**，每一項都對著實際畫面與程式碼確認。原則出處見 `ai/skills/design-craft.md`。

## 一、字體與文字

- [ ] HTML lang 設成 `zh-Hant`（繁中）或對應語系，不是 `en` 跑中文。
- [ ] 字體 stack 含中文 fallback：`'PingFang TC', 'Microsoft JhengHei', '微軟正黑體'`。
- [ ] 字級全部來自 type scale（`11 / 12 / 13 / 14 / 16 / 18 / 20 / 24 / 30 / 36 / 48`），沒有 15px、17px、19px 這種奇怪字級。
- [ ] 字重至少用到 3 種（400 / 500 / 700），不是全 500。
- [ ] 行高：標題 1.2–1.3、內文 1.5–1.7；中文 letter-spacing 為 0 或 < 0.02em。

## 二、顏色

- [ ] 沒有孤立 hex 散落各檔案；全部來自 `ai/context/design-system.md` 的 design token。
- [ ] 主色有完整 9-10 階；hover / active / disabled 用同色系不同階。
- [ ] 灰階系統至少 5 階。
- [ ] 文字對背景對比達 WCAG AA（4.5:1）。
- [ ] 同一語意（如「警告」）全站只用一個顏色變數；accent 色不超過 2 個。
- [ ] 顏色不是唯一的資訊載體（配 icon 或 label，色盲也看得懂）。

## 三、間距

- [ ] 所有間距是 4 的倍數（`4 / 8 / 12 / 16 / 20 / 24 / 32 / 40 / 48 / 64`）。
- [ ] Card 內 padding 至少 16px 起手；section 間至少 32px。
- [ ] 沒有為了塞東西硬縮的 margin / padding。

## 四、視覺層次

- [ ] 主要／次要／三級資訊一眼分得出來；三級資訊主動弱化（小字＋灰色＋細體）。
- [ ] 標題與內文字級差至少 4px。
- [ ] 主按鈕跟次按鈕一眼分得出（顏色＋字重＋邊框至少一項明顯不同）。

## 五、深度

- [ ] 邊框、陰影、背景色**三選一**，沒有同時疊用。
- [ ] 陰影方向統一（y-offset > 0、blur > y-offset）。
- [ ] 邊框顏色不超過 2 種；背景色不超過 3 種。

## 六、互動五態

每個互動元素（button、link、input、可點擊的 card）都要有：

- [ ] Default、Hover、Focus（focus ring，無障礙必備）、Disabled（明顯弱化＋ `not-allowed`）、Loading（> 200ms 的動作有 spinner / skeleton）。

## 七、零內容狀態

每個會顯示資料的區塊（list、table、card grid）都要有：

- [ ] Loading（skeleton / spinner，不能空白）。
- [ ] Empty（「目前無資料」＋行動 CTA）。
- [ ] Error（明確訊息＋重試）。
- [ ] 無權限（友善說明，不要裸 403）。

## 八、Mockup 對齊（如有 mockup）

- [ ] 字級、顏色、間距、圓角、邊框逐項對齊選定的 mockup（用瀏覽器 inspect 對）。
- [ ] 不要自己「順手改善」mockup——先實作一致版本，改善另提。

## 九、響應式

- [ ] Mobile（< 640px）／Tablet／Desktop 排版皆正常；文字不截斷、不溢出。
- [ ] 表格在窄畫面有橫向捲動或重排策略。
- [ ] 觸控目標至少 44×44px。

## 十、最終檢查

- [ ] 瞇起眼看，視覺重心對嗎？
- [ ] 跟 `ai/skills/design-craft.md` 參考清單裡最像的 production 產品並排比，差在哪？
- [ ] 給人看 5 秒，能說出「這頁是做什麼的」嗎？

還是不對勁時：找最像的參考專案並排截圖比對；**最後手段**是放棄目前排版、照參考從零重排一次——常常比東補西補快。
