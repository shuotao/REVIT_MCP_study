# 產出物目錄（Artifacts）

填寫完成的流程產出物（功能規格書、畫面規格、mockup、任務卡、驗證報告）都存在這裡，**一個 Epic 一個資料夾**。

`ai/templates/` 底下的檔案是**唯讀的範本母本，不得覆寫**——產出時一律「以範本為底，另存到本目錄」。看板卡片的 `links` 欄位（featureSpec / screenSpec / mockupDecision / taskCard / verificationReport）也填這裡的路徑。

## 結構

```text
ai/artifacts/<Epic 名稱>/
  feature-spec.md               # 以 ai/templates/feature-spec.md 為範本
  screen-spec-<畫面>.md         # 以 ai/templates/screen-spec.md 為範本，一畫面一份
  mockups/                      # mockup 變體（HTML 或圖檔），檔名含畫面與變體，如 dashboard-variant-a.html
  mockup-decision-<畫面>.md     # 以 ai/templates/mockup-decision.md 為範本
  task-cards/<卡片id>.md        # 以 ai/templates/task-card.md 為範本，一卡一份
  verification/<卡片id>.md      # 以 ai/templates/verification-report.md 為範本
```

Epic 資料夾名稱與 `tools/kanban/epics.json` 的 Epic `name` 一致（例如 `ai/artifacts/專案設置/`、`ai/artifacts/預約管理/`）。

## 規則

- 產出物跟程式碼一樣走 git：commit 就是核准紀錄的一部分。
- 不確定該放哪時，回來讀這份 README，不要就地發明新位置。
- 範本更新不回溯：已產出的文件維持產出當下的結構即可。
