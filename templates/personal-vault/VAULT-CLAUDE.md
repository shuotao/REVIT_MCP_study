---
name: personal-vault-schema
schema_version: "1.1"
declared_type: local-only template
usage: Copy this file VERBATIM to vault/CLAUDE.md inside your REVIT_MCP clone. Fill in only the Personal section. Do not paraphrase the Fixed Core.
---

# 個人 BIM 知識庫 Schema（vault/CLAUDE.md）

<!-- ============ FIXED CORE v1.1 — 由上游範本定義 ============
此區塊由 REVIT_MCP 上游 templates/personal-vault/VAULT-CLAUDE.md 統一定義。
任何 AI Agent（Claude Code / Gemini CLI / Codex CLI 等）都不得改寫、濃縮或
重新表述此區塊。升級方式：git pull 後比對上游範本的 schema_version，
若上游較新，將新版 Fixed Core 整段覆蓋進來（Personal 區保留不動）。
v1.1 變更：vault 改為住在 REVIT_MCP clone 內（repo 即 raw 層），
不再使用獨立 raw/ 資料夾。 -->

## 結構

vault 住在 REVIT_MCP clone 裡面。repo 本身就是 raw 層；vault/ 是個人區，
已被上游 .gitignore 排除（/vault/ 與 /.obsidian/），永遠不會被 push 上去。

```text
REVIT_MCP/               ← 唯一的 clone = raw 層（Obsidian 開這一層）
  domain/*.md            ← 公共智慧：Domain SOP（唯讀對待）
  domain/references/     ← 建築技術規則彙整
  .claude/skills/        ← 編排層 Skill
  CLAUDE.md              ← 公共 schema（本專案的 AI 憲法）
  log/                   ← 公共時間軸
  templates/personal-vault/VAULT-CLAUDE.md  ← 本檔的上游範本（lint 時比對）
  vault/                 ← 個人區（gitignored；建議自己 git init 備份）
    wiki/                ← 個人知識頁，AI 建立與維護，允許 [[雙向連結]]
    CLAUDE.md            ← 本檔（Fixed Core 逐字來自範本 + Personal 區）
    index.md             ← wiki 目錄：每頁一行連結＋一句摘要
    log.md               ← 個人 append-only 時間軸
```

## 操作

1. **Ingest**：在 repo 根目錄執行 `git pull`，用 `git diff` 找出上次 ingest
   之後變動的檔案，只消化變動部分：閱讀、與使用者討論重點、寫入或更新
   vault/wiki/ 頁、更新 vault/index.md、追加 vault/log.md。
2. **Query**：先讀 vault/index.md 找相關頁再深入。好的答案存回 wiki 成為新頁。
   本 repo 的 .mcp.json 已設定 Revit MCP —— 可直接對活的 Revit 模型提問
   （執行排煙檢討、查詢元素、跑法規檢核）；實際審查結果也 file 回 wiki。
3. **Lint**：檢查 (a) 哪些 wiki 頁的 source_version 落後上游現況；
   (b) 頁面間矛盾；(c) 無連入連結的孤兒頁；(d) 值得回饋上游的發現，
   整理成可提案清單；(e) 本檔 Fixed Core 的 schema_version 是否落後
   templates/personal-vault/VAULT-CLAUDE.md，落後則提示升級。

## 紀律

1. 個人操作**永不寫入 vault/ 以外的任何檔案**。上游 CLAUDE.md 的開發規則
   （如 append log/YYYY-MM.md、跑 QAQC）只適用於開發本專案，不適用於
   vault 操作；個人 log 一律寫 vault/log.md。
2. 永不在本 repo 執行 `git clean -x` 系列指令（會刪除整個 vault/）、
   永不 `git add -f` vault 內容。建議 vault/ 自己 `git init` 並推私人遠端備份。
3. 溯源：每個源自上游的 wiki 頁，frontmatter 必須記 `source`
   （如 domain/smoke-exhaust-review.md）與 `source_version`
   （抄該檔 frontmatter 的 version 與 updated）。
4. 答案中的具體數據（元素 ID、數量、面積）必須來自本回合的工具結果，
   不可憑記憶——與上游 CLAUDE.md 的「資料誠實」原則一致。
5. 法規與計算方法以 domain/*.md 為準；wiki 是個人理解層，
   與 Domain 衝突時，在 lint 報告中標記，不擅自改寫結論。

## 格式契約

- wiki 頁 frontmatter（最少）：

```yaml
---
source: domain/xxx.md          # 無上游來源的個人頁可省略
source_version: "1.0 / 2026-06-09"
updated: "YYYY-MM-DD"
tags: []
---
```

- vault/log.md 條目（與上游 log/ 格式一致，可 grep）：

```text
## [YYYY-MM-DD HH:MM] ingest|query|lint | 簡述
```

- vault/index.md 條目：`- [頁標題](wiki/檔名.md) — 一句摘要`

<!-- ============ /FIXED CORE ============ -->

## Personal（個人化區——由你與你的 AI Agent 共同演化）

- 專業領域：（例：消防審查 / 結構 / 機電 / 建築設計）
- 最常用的 Domain：
- 筆記語言與粒度偏好：
- 個人慣例（隨使用增補）：
