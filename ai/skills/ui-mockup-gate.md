# UI Mockup 關卡

當任務涉及變更畫面、元件、互動流程或視覺狀態時，皆須使用此流程。

這個關卡管流程；視覺品質由 `ai/skills/design-craft.md` 把關——**產出任何變體之前先讀它**，套用其中的十大紀律（type scale、4 的倍數間距、色彩系統、depth 三選一、五態完整）與「先比對高品質參考專案」的做法。

## 先對照設計系統（強制，不可省略）

除了 Epic 0 尚未定案設計系統的階段外，跑這個關卡前**一律先讀 `ai/context/design-system.md`**：

- mockup **必須用 `design-system.md` 已定案的 design token 與元件庫拼出來**，不得憑空發明新色彩、新間距或新元件風格。變體之間的差異應該來自版型、資訊架構與元件組合方式，而不是各自另立一套視覺。
- 若這個畫面需要的元件在元件庫 inventory 裡還沒有：**強制走「查庫→照風格補做→登記回庫」**——依既有 token 與風格新做該元件（涵蓋必要狀態），完成後登記回 `design-system.md` 的元件庫 inventory，再繼續。不得就地捏一個不入庫的一次性元件。
- 若既有 token／元件確實不敷使用、需要擴充設計系統本身，停下來向人工確認，不要自行擴張風格。

## 步驟

1. 讀 `ai/context/design-system.md`，列出這個畫面會用到的既有 token 與元件；盤點是否有缺的元件需照上方規則補做。
2. 以 `ai/templates/screen-spec.md` 為範本，建立或更新 `ai/artifacts/<Epic>/screen-spec-<畫面>.md`（範本唯讀，不得覆寫；見 `ai/artifacts/README.md`）。
3. 列出所有必要狀態：預設、載入中、空狀態、錯誤、停用、權限不足、行動裝置版。
4. 產出 2-3 個 mockup 變體（皆以既有 token／元件組成），存到 `ai/artifacts/<Epic>/mockups/`，檔名含畫面與變體（例如 `dashboard-variant-a.html`）。
5. 依清晰度、資訊密度、實作複雜度與風險比較各變體。
6. 以 `ai/templates/mockup-decision.md` 為範本，將決策記錄於 `ai/artifacts/<Epic>/mockup-decision-<畫面>.md`。
7. 在人工選擇變體之前，停止進行實作。

## Style tile 變體（Epic 0 步驟 2a 的 S2 階段專用）

在 Epic 0 決定「視覺風格方向」時，本關卡產出的不是完整版面，而是 2-3 個 **style tile**：每個變體呈現色彩情緒、字體個性、圓角與陰影傾向、密度、亮／暗模式與參考產品，讓人工比較整體氣質。這是唯一「還沒有既有 token 可對照」的例外——此階段的目的正是要產生風格方向，供後續 S3 提煉出 design token。選定方向後寫進 `design-system.md` 的「風格方向」。

## 輸出

- 畫面規格書。
- 用到的既有 token／元件清單，以及本次新做並已登記回 inventory 的元件（如有）。
- 變體比較表。
- 建議採用的變體。
- 待釐清問題。
- 人工核准請求。
