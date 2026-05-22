---
name: hj-pr-proposal
description: "將使用者自己的 domain、skill 與 tool 內容轉譯為 HJPLUS 台灣建築師知識庫可提交的 PR 草案，涵蓋 fork 檢查、內容對齊、檔案重編、作者確認與提交流程。觸發條件：HJ PR、fork、提案草案、domain 轉 skill、轉譯、貢獻草案、PR draft。"
metadata:
  references:
    - domain/skill-authoring-standard.md
    - domain/frontmatter-standard.md
    - domain/qa-checklist.md
  tags: [hjplus, pr, fork, proposal, skill, domain, translation]
---

# HJ 提案編排器

這個 Skill 用來把使用者自己的知識內容整理成 HJPLUS 台灣建築師知識庫可提交的 PR 草案。

它的目標不是直接替你發 PR，而是先把內容整理到「可審核、可 fork、可追蹤」的狀態，讓作者確認後再進入提交。

## 何時使用

當使用者想把自己的 domain、skill、tool、workflow、SOP 或研究成果，轉成 HJPLUS 可接受的貢獻內容時使用。

典型情境：
- 想先確認目標 repo 是否已 fork
- 想把既有研究內容整理成 HJ 的雙層知識結構
- 想先產出 PR draft，再由作者確認
- 想把原本專案中的流程，轉成適合 HJ 知識庫的版本

## 使用方式

### 一句話啟動

請以這個句型啟動：

> 使用 `hj-pr-proposal`，目標 repo 是 HJPLUS 台灣建築師知識庫，來源內容是我自己的 domain / skill / tools，請先檢查 fork 狀態，再幫我轉成可提交的 PR 草案。

### 標準輸入

最好一次提供以下資訊：
- 來源 repo 或來源資料夾
- 目標 repo URL
- 想貢獻的主題範圍
- 是否只做草案，或要一路做到 draft branch
- 是否已有 fork / remote / branch 名稱

## 工作流程

### 1. 確認 fork 與目標 repo

先確認：
- 是否已經 fork 目標 repo
- 目前工作區是否有對應 remote
- 目前分支是否適合建立提案分支

如果 fork 不存在，就先建立 fork 或提示使用者先 fork。

### 2. 分析來源內容

分析來源內容時，先區分三種資料：
- `domain`：知識、背景、法規、SOP、原理
- `skill`：可執行流程、觸發條件、步驟順序
- `tools`：實際工具、腳本、命令、API、MCP 工具

這一步的目標是找出哪些內容適合轉成 HJ 的知識模組，哪些內容應該保留在來源 repo。

### 3. 轉譯成 HJ 可提交草案

轉譯時遵守 HJ 的雙層結構：
- 中文層寫給人看，放在 `domain.md`
- 英文層寫給 AI 看，放在 `SKILL.md`
- `SKILL.md` 內要有清楚的 frontmatter、觸發情境、步驟與參考連結

若內容屬於 B 類適配，要保留台灣適配註記；若屬於 C 類台灣法規，要補上可對接的工具範例或資料來源。

### 4. 重新編碼成 HJ 需要的檔案資訊

將草案整理成 HJ 樣式的檔案結構，通常包含：
- `domain.md`
- `SKILL.md`
- `references/`（如需要）
- `scripts/`（如需要）
- `assets/`（如需要）

同時確認：
- 檔名與資料夾命名一致
- 英文 skill 名稱為 kebab-case
- 中文資料夾名稱清楚可搜尋
- 授權與來源說明不衝突

### 5. 產出可審核的 PR 草案

最後只輸出草案，不直接送出 PR。

草案應包含：
- 變更摘要
- 來源與目的
- 影響範圍
- 需要作者確認的地方
- 建議 PR 標題與內文
- 若需要，建議的 branch 名稱

## 驅動步驟

建議照這個順序驅動：

1. 先告訴 Skill 目標 repo 與來源內容
2. 要求它先確認 fork 狀態
3. 要求它分析內容屬性，分成 domain / skill / tools
4. 要求它產出 HJ 草案，不要直接推送
5. 你確認後，再讓它整理成可 commit 的檔案
6. 最後才進入 fork、push、PR

## 範例指令

### 範例 1：只做草案

請用 `hj-pr-proposal` 幫我把目前這份內容轉成 HJ 可提 PR 的草案，先不要 push，也不要 create PR。

### 範例 2：完整流程但保留確認點

請用 `hj-pr-proposal`：
1. 檢查目標 repo 是否已 fork
2. 分析我的來源內容
3. 轉成 HJ 格式草案
4. 列出需要我確認的地方
5. 等我確認後再往下一步走

### 範例 3：已經知道要做 PR

請用 `hj-pr-proposal` 幫我把這個主題整理成可 commit、可 push、可開 PR 的版本，並先顯示檔案結構與 PR 內文草稿。

## 注意事項

- 不要把來源 repo 的實作碼直接搬進 HJ，優先做知識轉譯
- 不要跳過作者確認
- 不要自動送 PR，除非使用者明確要求
- 如果內容不適合放進 HJ，就明講原因並改提議題型或拆分主題

## Reference

詳見 `domain/skill-authoring-standard.md`、`domain/frontmatter-standard.md`、`domain/qa-checklist.md`。