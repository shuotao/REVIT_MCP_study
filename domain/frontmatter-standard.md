---
name: frontmatter-standard
description: "Revit MCP 專案的 domain/*.md frontmatter 規範，對齊 Anthropic Agent Skills spec，使 domain 與 skill 結構相容。"
metadata:
  version: "1.0"
  updated: "2026-04-21"
  created: "2026-04-21"
  references:
    - "https://agentskills.io/specification"
    - "https://github.com/anthropics/skills"
  related:
    - skill-authoring-standard.md
  referenced_by:
    - "/domain command"
    - "/qaqc Phase 6"
  tags: [frontmatter, metadata, 規範, lint, quality]
---

# domain/*.md Frontmatter 規範

本文件定義 `domain/*.md` 的 YAML frontmatter 規範。供 `/domain` 指令產出新檔時依循，並作為 `/qaqc` Phase 6（Content Quality Lint）的判準。

## 規範來源與設計原則

本規範**對齊** Anthropic 官方 Agent Skills spec（`agentskills.io/specification`）：
- **Required 欄位**：`name`, `description`（與 skill 一致）
- **額外 metadata**：放進 `metadata:` nested map（遵循 Anthropic 的範例結構，不用 flat top-level fields）

**為何不用 top-level 的 `version:` / `updated:` 欄位？** Anthropic spec 的範例明確把額外欄位放在 `metadata:` map 中。採同一結構讓 domain 與 skill 的 frontmatter **mental model 一致**，貢獻者學一套就夠。

## Required 欄位

### `name`（必填）
- 長度：1–64 字元
- 字符：小寫字母、數字、連字號（`a-z`, `0-9`, `-`）
- 不可以連字號開頭／結尾，不可連續連字號
- **必須與檔名（去掉 `.md`）一致**

### `description`（必填）
- 長度：1–1024 字元
- 說明「**做什麼**」+「**什麼時候用**」
- 包含中英文關鍵字助於 Lint 搜尋

## `metadata` nested map 欄位（6 + 2 自動）

### 語意欄位（貢獻者手動維護）

| 欄位 | 型別 | 用途 | Lint 用途 |
|------|------|------|----------|
| `version` | string | 本 domain 的版本號，例 `"1.2"` | /qaqc 檢查是否存在 |
| `updated` | string (YYYY-MM-DD) | 最新實質修訂日期 | Staleness 偵測（>12 個月警告） |
| `references` | list of strings | 引用的外部依據（法規條號、URL、書目）| 供老師 review 時快速查源 |
| `related` | list of filenames | 相關的其他 domain（檔名，例 `fire-rating-check.md`）| 6-4 指向驗證 |
| `referenced_by` | list of strings | 被哪些 skill 引用（skill name）| 6-5 反向驗證 |
| `tags` | list of strings | 搜尋／分類關鍵字 | 供 domain 索引、grep |

### 自動欄位（git log 回推，不需手填）

| 欄位 | 型別 | 取得方式 |
|------|------|----------|
| `created` | string (YYYY-MM-DD) | `git log --follow --diff-filter=A --pretty=%ai` 的第一筆 |
| `contributors` | list of strings | `git log --follow --pretty=%an \| sort -u` |

由 `scripts/backfill-domain-metadata.py` 自動寫入，無需手動維護。

## 完整範例

```yaml
---
name: fire-rating-check
description: "防火時效檢討 SOP：建技規 §110 法規應用、走廊／逃生區劃耐燃等級判定、與 corridor/exterior-wall 的交叉驗證。"
metadata:
  version: "1.2"
  updated: "2026-03-15"
  created: "2025-09-12"
  references:
    - "建技規 §110"
    - "消防設備設置標準 §25"
  related:
    - corridor-analysis-protocol.md
    - exterior-wall-opening-check.md
  referenced_by:
    - fire-safety-check
    - building-compliance
  contributors:
    - "shuotao"
    - "jacky820507"
  tags: [fire, safety, 防火, corridor, §110]
---

# 防火時效檢核

## Purpose
...（body content starts here）
```

## 與 `skill-authoring-standard.md` 的關係

- **`skill-authoring-standard.md`** 管 `.claude/skills/*/SKILL.md` 的規範
- **本檔（frontmatter-standard.md）** 管 `domain/*.md` 的規範
- 兩者**採同一 mental model**（name/description required + metadata nested），差別在：
  - skill 有 `allowed-tools`、`license`、`compatibility` 等（Anthropic 額外欄位）
  - domain 有 `references`、`related`、`referenced_by`、`tags` 等（領域知識特有）

## /qaqc Phase 6 檢查項目

執行 `/qaqc` 時（macOS/Linux/Windows），Phase 6 會對每個 `domain/*.md`（除 README.md）做：

- **6-1 Frontmatter 存在性**：是否以 `---\n` 開頭
- **6-2 必填欄位**：`name` + `description` 都在
- **6-3 metadata 完整性**：`metadata.version` 與 `metadata.updated` 都在
- **6-4 related 指向驗證**：`metadata.related` 列出的檔是否存在
- **6-5 referenced_by 反向驗證**：`metadata.referenced_by` 列的 skill 確實在其 `## Reference` 段落引用本 domain
- **6-6 Staleness 提醒**：`metadata.updated` 距今 > 12 個月 → 黃色警告（informational，非 FAIL）

## 回補策略（Backfill）

### 自動回補
執行一次：
```bash
python3 scripts/backfill-domain-metadata.py
```

對**無 frontmatter** 的 domain：創建完整 frontmatter，auto 欄位從 git log 取、語意欄位留 `[]  # TODO: 月小聚補`。

對**已有 frontmatter** 的 domain：**跳過**，保留既有內容，由月小聚人工檢視是否補 nested metadata 結構。

### 人工補語意欄位
月小聚時，當月討論的 domain **順手補齊**：
- `references`、`related`、`tags` 由老師判斷填入
- 不強制一次補完所有 domain

## Migration from Partial Frontmatter

既有 21 個 domain 有部分 frontmatter 但不符 nested 規範，處理方式：

1. Backfill script **不自動改寫**這些檔（避免破壞既有結構）
2. /qaqc Phase 6 會標示它們為「未完整 metadata」
3. 月小聚時手動遷移：把 top-level 的 `version`、`tags` 等移到 `metadata:` map 內
4. 遷移後的檔就通過 Phase 6

目標：**一年內**全部 31 個 domain 符合本規範。
