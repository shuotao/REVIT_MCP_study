# 工作流程（Workflow）

這套流程把模糊的需求轉換成範圍有界、可審查、可驗證的 AI 工作。

若是全新專案、還沒有 Epic/User Story 待辦清單，先執行 `project-kickoff` skill。它會把專案拆解成 Epic → User Story → Task，並建進 `tools/kanban/epics.json` 與 `tools/kanban/cards/`。之後每張任務卡再個別走過下面的各個階段。

## Phase 0：收件（Intake）

先收下需求，不要實作。

輸出：

- 問題陳述。
- 使用者或商業目標。
- 已知限制。
- 未知事項。
- 是否涉及 UI、資料、身分驗證、金流、基礎設施或遷移。

關卡：

- 若需求不是小而明確的，進入釐清階段。

## Phase 1：情境探索（Context Discovery）

只搜尋到足以理解專案相關部分的程度。

必要輸出：

- 任務專屬的情境包。
- 相關檔案。
- 應遵循的既有模式。
- 風險區域。
- 驗證指令。
- 情境預算備註：讀了什麼、跳過了什麼、為什麼。

使用 `ai/process/context-protocol.md`。

## Phase 2：釐清（Clarification）

把模糊的意圖轉成產品層級的規格書。

必要輸出：

- 功能規格書：以 `ai/templates/feature-spec.md` 為範本，產出到 `ai/artifacts/<Epic>/feature-spec.md`（所有產出物的存放慣例見 `ai/artifacts/README.md`；範本唯讀，不得覆寫）。
- 待釐清問題。
- 明確的非目標。
- 驗收標準。

關卡：

- 進入架構規劃前需要人工核准。

## Phase 3：UI Mockup 關卡

涉及畫面、視覺狀態或互動流程時需要。

先讀 `ai/context/design-system.md`（Epic 0 產出的設計系統單一事實來源）。mockup 變體必須以其中已定案的 design token 與元件庫組成，不得憑空發明新風格；缺的元件依 `ai/skills/ui-mockup-gate.md` 的「查庫→照風格補做→登記回庫」規則處理。視覺品質套用 `ai/skills/design-craft.md` 的設計工藝紀律。

必要輸出：

- 畫面地圖。
- 狀態地圖。
- 2-3 個 mockup 變體（以既有 token／元件組成）。
- 重用的 token／元件清單，與需新做並登記的元件（如有）。
- 取捨比較表。
- 選定的變體。

關卡：

- 實作前需要人工選定。

## Phase 4：架構規劃（Architecture Plan）

建立技術做法。

必要輸出：

- 預期會變更的檔案。
- 資料與 API 契約。
- 遷移備註（如有）。
- 安全性與隱私備註。
- 測試策略。
- 高風險工作的回滾計畫。

關卡：

- 高風險工作需要 architect、security 與 test 審查。

## Phase 5：任務卡（Task Cards）

把工作拆成 AI-ready 的卡片。

每張卡都必須符合 `ai/process/definition-of-ready.md`。

建議的卡片大小：

- 一個畫面狀態。
- 一個 API endpoint。
- 一個元件行為。
- 一個 bug 的重現與修復。
- 一個測試缺口。

## Phase 6：實作（Implementation）

一次只實作一張已核准的任務卡。

規則：

- 寫新程式碼前，先確認是否已有現成的函式庫或框架能解決這個問題，避免重造已解決的輪子。
- 若有 2-3 個可行的函式庫或做法，詢問使用者要用哪一種並提出建議。重大選擇不要自己悶著頭選。
- 除非有新事證需要更新計畫，否則只能動允許範圍內的檔案。
- 遵循既有專案模式。
- UI 實作一律取用 `ai/context/design-system.md` 的 design token 與元件庫，不硬寫一次性的色彩、間距或字級；新做的元件要照既有 token 與風格製作，完成後登記回元件庫 inventory。視覺實作全程遵循 `ai/skills/design-craft.md`，交付前對照 `ai/checklists/design-review-checklist.md`。
- 保持 diff 小。
- 每個增量都必須讓系統維持在可運作、可執行的狀態，絕不能任務做到一半就停下、留下壞掉或無法啟動的系統。
- 隨著變更新增或更新測試。
- 進入驗證階段前自己先跑過測試（單元測試與整合測試），不要只憑「看起來是對的」。
- 撰寫程式碼的當下就要套用 `ai/checklists/security-checklist.md`，不是只有正式審查時才做。無論風險等級高低都適用（例如絕不以明文儲存密鑰或密碼）。
- 範圍改變時停下來詢問。
- 若這個任務有在 `tools/kanban/` 上追蹤，隨著進度把卡片的階段往前推（見 `ai/process/kanban.md`）。

## Phase 7：驗證（Verification）

完成需要證據佐證。

使用：

- 單元測試。
- 整合測試。
- E2E 測試。
- 型別檢查（Typecheck）。
- Lint。
- Build。
- 安全性掃描工具。
- UI 的螢幕截圖比對。

## Phase 8：審查（Review）

執行相關的審查關卡：

- 產品驗收。
- UX 審查。
- 架構審查。
- 安全性審查。
- 測試審查。
- Code review。

使用 `ai/process/review-gates.md`。

## Phase 9：人工驗收（Human Acceptance）

最終回覆必須包含：

- 變更了什麼。
- 證據。
- 殘留風險。
- 後續任務。

沒有驗證證據前，不得宣稱已可上生產環境。
