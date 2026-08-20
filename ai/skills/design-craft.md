# 設計工藝（Design Craft）

任何產出 UI mockup 或前端視覺實作的任務都適用。`ui-mockup-gate` 管的是**流程**（幾個變體、哪些狀態、誰核准）；這份 skill 管的是**品質**——沒有它，流程走完也可能產出醜的東西。原則整理自《Refactoring UI》（Adam Wathan & Steve Schoger）。

## 何時使用

- Epic 0 的五階段設計流程（`ai/skills/project-kickoff.md` 步驟 2a 的 S2–S5）全程。
- `ui-mockup-gate` 產出任何 mockup 變體之前。
- 任何前端任務的實作過程中。
- Design review、或使用者抱怨「醜」「看起來怪」「不對齊」時。

## 十大核心紀律

1. **從 features 出發，不從 layout 出發**：先列出這一頁的 1-3 個主要動作，把它們做大做明顯；不要先畫 sidebar/header 再想中間塞什麼。
2. **層次靠對比建立**：size、weight、color 三個工具擇一加強；次要資訊主動弱化（小字＋灰色＋細體）。反例：所有文字都同字級同字重同顏色。
3. **留白起手就用過量**：card 內 padding 從 24px（`p-6`）起手，覺得太空再縮；不要從 8px 開始加。擠＝廉價，空＝精緻。
4. **間距只用 4 的倍數**：`4 / 8 / 12 / 16 / 20 / 24 / 32 / 40 / 48 / 64`。7px、13px、19px 會讓畫面「說不出哪裡怪」。
5. **字級從 type scale 取、字重活用**：字級只用 `11 / 12 / 13 / 14 / 16 / 18 / 20 / 24 / 30 / 36 / 48`；字重至少用到 400（body）／500（emphasis）／600（subheading）／700（heading、主按鈕）三種以上，不要全用 500。中文專案：字體 stack 必含 `'PingFang TC', 'Microsoft JhengHei'` fallback，HTML lang 設 `zh-Hant`；標題行高 1.2–1.3、內文 1.5–1.7；中文 letter-spacing 為 0。
6. **色彩是 system，不是單一 hex**：grey scale（最重要）＋ primary scale ＋ semantic 色（success/warning/danger/info），每色 9-10 階。hover/active/disabled 用同色系不同階。**先用 grayscale 把整個版面排好，最後才上色**——灰階版層次對了，加色就是錦上添花。
7. **邊框是新手最常見的視覺噪音**：區隔優先序＝ spacing → 背景色 → 陰影 → 最後才是邊框（表格列、input 這類需要硬邊界的才用）。
8. **Depth 三選一**：浮起用 shadow、平面區塊用背景色、硬邊界用 border，**不要疊用**。陰影模擬上方光源：y-offset > x-offset、blur > y-offset。
9. **五態完整才算做完**：每個互動元素要有 default / hover / focus / disabled / loading；每個資料區塊要有 loading / empty / error / 無權限。只做 default 是半成品。
10. **Polish 自問**：做完逐項對照 `ai/checklists/design-review-checklist.md`，每個 ❌ 都是修的機會。

## 實作前先比對高品質開源參考

**不要憑記憶設計。** 啟動視覺任務時先問三題：

1. 這個 feature 最像哪一類？（calendar / dashboard / form / list / detail / setting）
2. 下面哪個參考最像？打開它，找對應頁面。
3. 該頁面的字級、間距、配色、互動，依次抄；不確定的數值就抄參考，不要瞎猜。

| 類型 | 參考 | 學什麼 |
| --- | --- | --- |
| Admin dashboard | [shadcn-admin](https://github.com/satnaing/shadcn-admin)、[TailAdmin](https://github.com/TailAdmin/free-nextjs-admin-dashboard) | 版型配置、light/dark、表格／filter／pagination 模式 |
| 預約／排程 | [Cal.com](https://github.com/calcom/cal.com)、[open-salon](https://github.com/clawnify/open-salon) | booking 視覺語言、availability picker、日曆欄位 |
| SaaS 全套 | [ixartz/SaaS-Boilerplate](https://github.com/ixartz/SaaS-Boilerplate) | auth flow、multi-tenancy、landing/pricing/dashboard 三段風格 |
| 元件範例 | [shadcn/ui examples](https://ui.shadcn.com/examples)、[Tremor](https://github.com/tremorlabs/tremor)、[Radix primitives](https://www.radix-ui.com/primitives)、[Mantine UI](https://ui.mantine.dev/) | 直接照抄結構；data viz；a11y 互動細節 |
| 設計靈感（非程式） | [Mobbin](https://mobbin.com/)、[Dribbble](https://dribbble.com/) | 真實 production app 截圖；配色／字體／排版方向 |

## 實作中的固定處理順序

**不要跳過順序**，每一階段完成才進下一階段：

1. **Hierarchy**：定出主要／次要／三級資訊，三級要明顯比次要弱。
2. **Layout & Spacing**：間距全部取自 4 的倍數 scale。
3. **Typography**：字級從 type scale 取、字重活用、中文 fallback 與 lang 設好。
4. **Color**：先 grayscale 排好版面，再從 design token 上色。
5. **Depth**：邊框／陰影／背景三選一。
6. **Polish**：補齊互動五態與 empty/loading/error 畫面。

## 與治理流程的整合

- 所有色彩、字級、間距、圓角、陰影數值一律來自 `ai/context/design-system.md` 的 design token，不寫孤立 hex 或 inline 一次性數值；Epic 0 的 S3 階段就是用這份 skill 的 scale 紀律去定 token。
- mockup 關卡（`ai/skills/ui-mockup-gate.md`）產出的每個變體都要先過上面的十大紀律。
- 交付前與 design review 時，逐項對照 `ai/checklists/design-review-checklist.md`。

## 常見急救修法（使用者抱怨醜時）

- 字體 stack 沒中文 fallback → 加 PingFang TC／微軟正黑體；`lang="en"` 跑中文 → 改 `zh-Hant`。
- hex 散落各檔案 → 集中回 design token。
- 字重全 500 → 主要 700、次要 500、提示 400。
- 間距 7/13/19px → 改 8/12/16/20。
- 邊框＋陰影＋背景三疊 → 砍掉兩個。
- 沒有 empty／loading state → 補上。

## 不要做的事

- 不要憑記憶猜 hex、間距、字級。
- 不要看到 mockup 就直接寫 inline style 而不對齊到 design token。
- 不要在沒看過任何參考的情況下「自己設計」一個新版型。
- 不要等使用者抱怨醜才做 review——交付前就對照檢查清單。
