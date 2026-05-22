# HJPLUS 台灣建築師知識庫 SKILL 無痛導入說明

## 用法

老師可以直接複製下面這段話，貼給 Claude Code / Copilot / Gemini CLI 使用：

```text
使用 hj-pr-proposal 幫我把我自己的 domain / skill / tools 內容，轉成 HJPLUS 台灣建築師知識庫可提交的 PR 草案。

請依序執行：
1. 先確認我是否已經 fork 目標 repo。
2. 再分析這次要貢獻的內容，判斷哪些屬於 domain、哪些屬於 skill、哪些屬於 tools。
3. 接著把內容轉譯成 HJ 需要的文件草案，但先不要直接 push 或送 PR。
4. 產出可讓我確認的檔案結構、內容草案與 PR 草稿。
5. 等我確認後，再整理成可 commit、可 push、可開 PR 的版本。

如果需要，我可以再提供來源內容、目標 repo、分支名稱與想貢獻的主題。
```

## 這份文件在做什麼

這份文件的用途，是把「我自己的專案內容」轉成「HJPLUS 台灣建築師知識庫可以接受的貢獻格式」，讓流程可以從頭走到尾，但中間保留作者確認點。

它不是用來自動發 PR，而是用來把提案流程變得穩定、可重複、可審核。

## 整體流程

### 第 1 步：先確認 fork 狀態

先看目標 repo 是否已經 fork，遠端是否已設定好，分支是否適合建立提案。

如果還沒 fork，就先 fork；如果已經 fork，就直接從現有 fork 繼續。

### 第 2 步：分析來源內容

把來源內容分成三類：

- `domain`：知識、背景、法規、SOP、原理
- `skill`：可執行流程、觸發條件、步驟順序
- `tools`：命令、腳本、API、MCP 工具、程式碼能力

這一步的目的，是先知道哪些內容適合轉到 HJ，哪些內容應該留在原專案。

### 第 3 步：轉譯成 HJ 的雙層結構

HJ 的資料結構不是單份文件，而是雙層：

- 上層中文 `domain.md`：給人看，寫背景、說明、實務情境
- 下層英文 `SKILL.md`：給 AI 看，寫觸發條件、步驟、參考來源

如果是 B 類內容，要保留適配註記；如果是 C 類內容，要補能對接工具或資料來源的說明。

### 第 4 步：整理成可提交草案

把內容整理成 HJ 期待的檔案格式，例如：

- `domain.md`
- `SKILL.md`
- `references/`（需要時）
- `scripts/`（需要時）
- `assets/`（需要時）

這個階段先不要提交，只要讓作者能快速看懂內容、結構和用途。

### 第 5 步：作者確認後再進入 PR

草案確認後，才做以下事情：

- 放進自己的 fork
- 建立工作分支
- commit
- push
- 開 PR

## 兩個 SKILL 的配合方式

這個流程通常會搭配兩個層次：

### 1. `hj-pr-proposal`

這是主控 Skill，負責：
- 驅動提問
- 分析來源內容
- 轉成 HJ 草案
- 產出 PR 文件草稿
- 保留作者確認點

### 2. `skill-creator` 或等效的技能建立流程

這是用來把內容真正整理成技能檔案結構的流程，負責：
- 產生 `SKILL.md`
- 整理 frontmatter
- 檢查命名與結構
- 幫助把草案變成可落地的技能內容

也就是說：

`hj-pr-proposal` 先問問題、做分析、出草案。
`skill-creator` 再幫你把草案整理成正式技能格式。

## 老師怎麼驅動

建議直接照這個順序說：

1. 我想把自己的 domain / skill / tools 貢獻到 HJPLUS 台灣建築師知識庫。
2. 先用 `hj-pr-proposal` 幫我確認 fork 狀態。
3. 再幫我分析來源內容要放 domain 還是 SKILL。
4. 先產出草案，不要直接送 PR。
5. 等我確認後，再整理成可 commit / push / PR 的版本。

## 你會看到的輸出

通常會包含：

- 內容分類結果
- 目標資料夾與檔案結構
- 需要補充或修正的地方
- PR 標題與內文草稿
- 下一步要做什麼

## 建議原則

- 不要把原專案實作碼直接搬過去，優先做知識轉譯
- 不要略過作者確認
- 不要自動送 PR，除非你明確要求
- 如果內容不適合 HJ，就改成建議題目或拆分主題

## 相關檔案

- [hj-pr-proposal](.claude/skills/hj-pr-proposal/SKILL.md)
- [skill-authoring-standard](domain/skill-authoring-standard.md)
- [frontmatter-standard](domain/frontmatter-standard.md)
