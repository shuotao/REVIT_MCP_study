---
name: personal-vault-schema
schema_version: "1.0"
declared_type: local-only template
usage: Copy this file VERBATIM to your vault root as CLAUDE.md. Fill in only the Personal section. Do not paraphrase the Fixed Core.
---

# 個人 BIM 知識庫 Schema（vault CLAUDE.md）

<!-- ============ FIXED CORE v1.0 — 由上游範本定義 ============
此區塊由 REVIT_MCP 上游 templates/personal-vault/VAULT-CLAUDE.md 統一定義。
任何 AI Agent（Claude Code / Gemini CLI / Codex CLI 等）都不得改寫、濃縮或
重新表述此區塊。升級方式：git pull 後比對上游範本的 schema_version，
若上游較新，將新版 Fixed Core 整段覆蓋進來（Personal 區保留不動）。 -->

## 結構

```text
vault 根目錄/
  raw/        REVIT_MCP 的 git clone。唯讀。只透過 git pull 更新。
  wiki/       個人知識頁。由 AI 建立與維護，允許 [[雙向連結]]。
  CLAUDE.md   本檔。Fixed Core 來自上游範本，Personal 區個人化。
  index.md    wiki 目錄：每頁一行連結＋一句摘要，每次 ingest 後更新。
  log.md      append-only 時間軸。
```

raw/ 內的重點來源：

- `domain/*.md`：Domain SOP（公共智慧），frontmatter 含 version、updated、
  related、referenced_by、references、tags。
- `domain/references/building-code-tw.md`：建築技術規則彙整。
- `.claude/skills/*/SKILL.md`：編排層 Skill。
- `CLAUDE.md`：公共 wiki 的 schema（Domain 觸發關鍵字表在此）。
- `log/YYYY-MM.md`：公共 wiki 的時間軸。
- `templates/personal-vault/VAULT-CLAUDE.md`：本檔的上游範本（lint 時比對）。

## 操作

1. **Ingest**：先在 raw/ 執行 `git pull`，用 `git diff` 找出上次 ingest 之後
   變動的檔案，只消化變動部分：閱讀、與使用者討論重點、寫入或更新 wiki 頁、
   更新 index.md、追加 log.md。
2. **Query**：先讀 index.md 找相關頁再深入。好的答案存回 wiki 成為新頁。
   若環境有 Revit MCP，可對活的 Revit 模型提問；實際審查結果也 file 回 wiki。
3. **Lint**：檢查 (a) 哪些 wiki 頁的 source_version 落後 raw/ 現況；
   (b) 頁面間矛盾；(c) 無連入連結的孤兒頁；(d) 值得回饋上游的發現，
   整理成可提案清單；(e) 本檔 Fixed Core 的 schema_version 是否落後
   raw/templates/personal-vault/VAULT-CLAUDE.md，落後則提示升級。

## 紀律

1. 永不修改 raw/ 內任何檔案；個人筆記永不 commit 到上游 repo。
2. 溯源：每個源自上游的 wiki 頁，frontmatter 必須記 `source`
   （如 domain/smoke-exhaust-review.md）與 `source_version`
   （抄該檔 frontmatter 的 version 與 updated）。
3. 答案中的具體數據（元素 ID、數量、面積）必須來自本回合的工具結果，
   不可憑記憶——與上游 CLAUDE.md 的「資料誠實」原則一致。
4. 法規與計算方法以 raw/domain/*.md 為準；wiki 是個人理解層，
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

- log.md 條目（與上游 log/ 格式一致，可 grep）：

```text
## [YYYY-MM-DD HH:MM] ingest|query|lint | 簡述
```

- index.md 條目：`- [頁標題](wiki/檔名.md) — 一句摘要`

<!-- ============ /FIXED CORE ============ -->

## Personal（個人化區——由你與你的 AI Agent 共同演化）

- 專業領域：（例：消防審查 / 結構 / 機電 / 建築設計）
- 最常用的 Domain：
- 筆記語言與粒度偏好：
- 個人慣例（隨使用增補）：
