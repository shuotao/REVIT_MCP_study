---
name: skill-authoring-standard
description: "Revit MCP 專案的 Skill 編寫規範。供 skill-creator plugin 執行時參照，處理專案特有的約束（description 規則、觸發關鍵字、品質檢查）。當使用者提到 skill 規範、skill 品質、skill 編寫標準、description 規則時觸發。"
metadata:
  version: "1.0"
  updated: "2026-03-21"
  created: "2026-03-21"
  contributors:
    - "shuotao"
  references:
    - "https://agentskills.io/specification"
    - "https://platform.claude.com/docs/en/agents-and-tools/agent-skills/best-practices"
    - "https://github.com/anthropics/skills"
  related:
    - frontmatter-standard.md
  referenced_by: []  # TODO: 月小聚補（被哪些 skill 引用）
  tags: [skill, 規範, 品質, description, 觸發]
---

# Skill 編寫規範（Revit MCP 專案）

本文件定義本專案 Skill 的編寫規則，作為官方 `skill-creator` plugin 的**補充參照**。
官方 skill-creator 處理通用品質（格式、觸發率、eval），本文件處理專案特有的約束。

## 規範來源

每次建立或修改 Skill 前，先確認官方規格是否有更新：
- Spec：https://agentskills.io/specification
- Best practices：https://platform.claude.com/docs/en/agents-and-tools/agent-skills/best-practices
- 官方範例：https://github.com/anthropics/skills
- 五大設計模式：https://smartscope.blog/en/generative-ai/claude/claude-skills-design-patterns-official-guide/

## 1. Description 規則

### 格式

```yaml
description: "動詞開頭描述功能。說明何時使用。觸發條件：中文關鍵字、english keywords。"
```

### 必須

- 第三人稱動詞開頭（「在平面視圖中自動建立…」而非「自動標註尺寸：」）
- 包含「做什麼」+「什麼時候用」
- 包含中英文觸發關鍵字
- < 1024 字元

### 禁止

- **不要在 description 裡列工具名稱**（移到 SKILL.md body 的「工具」段落）
- 不要用第一人稱或第二人稱
- 不要使用模糊語（「處理資料」「幫助使用者」）

### 原因

description 被注入 system prompt，18 個 Skill × 每個列 3-7 個工具名 = ~100 個工具名永遠佔 context。
工具名稱改了但 description 沒同步 → 數字劣化。工具名稱放在 body 裡，觸發時才載入。

## 2. SKILL.md Body 結構

```markdown
---
name: kebab-case-name
description: "..."
---

# 標題

## Lessons Reference（如有相關 lesson）
- **L-XXX**：描述。詳見 `domain/lessons.md`。

## Sub-Workflows（或 Workflow / Steps）

### 1. 子流程名稱
步驟描述...

### 2. 子流程名稱
步驟描述...

## 工具
| 工具名稱 | 用途 |
|---------|------|
| `tool_name` | 做什麼 |

## Reference
詳見 `domain/xxx.md`。
```

### 規則

- Body < 500 行
- 必須有 `## Reference` 段落連結到對應的 domain 文件
- 每個 Skill 至少對應一個 `domain/*.md`（知識在 domain，編排在 Skill）
- 工具名稱只出現在 body 的「工具」表格，不出現在 description

## 3. 與 Domain 的關係

```
domain/*.md = 知識（法規、SOP、步驟、注意事項）
SKILL.md    = 編排（何時觸發、什麼順序、呼叫哪些工具）
```

- 一個 Skill 可以引用多個 domain（例如 fire-safety-check 引用 3 個 domain）
- 一個 domain 可以被多個 Skill 引用
- Skill 不應複製 domain 的內容，而是引用它

## 4. 設計模式分類

每個 Skill 應屬於以下之一：

| 模式 | 適用場景 | 本專案範例 |
|------|---------|-----------|
| Sequential Workflow | 有依賴順序的步驟鏈 | element-query（三階段協議） |
| Iterative Refinement | 品質改善迴圈 | claude-md-sync（雙向驗證） |
| Context-aware Tool Selection | 條件分支 | building-compliance（依法規類型選工具） |
| Domain-specific Intelligence | 內嵌領域規則 | element-coloring、wall-orientation-check |
| Utility | 純工具操作，不涉及 MCP Tools | build-revit、deploy-addon |

## 5. 升級流程：Domain → Skill

使用 `/domain` 產出 SOP 後，判斷是否需要升級為 Skill：

### 升級條件（滿足任一即可）

- 該 domain 被重複使用 3 次以上
- 流程涉及 3 個以上 MCP Tools 的特定順序
- 有明確的觸發關鍵字可以做語意匹配
- 流程有條件分支或驗證迴圈

### 升級步驟

1. 使用 `skill-creator` plugin 建立 Skill
2. 將本文件（`domain/skill-authoring-standard.md`）提供給 skill-creator 作為專案規則參照
3. skill-creator 會處理：SKILL.md 撰寫、description 觸發率優化、eval 測試
4. 完成後 `claude-md-sync` hook 自動觸發，驗證 CLAUDE.md 一致性

### 升級時的 prompt 範例

```
我要把 domain/smoke-exhaust-review.md 升級為 Skill。
請先讀 domain/skill-authoring-standard.md 了解本專案的 Skill 規範，
然後用 skill-creator 建立 Skill。
```

## 6. 現有 Skill 的待修正項目

以下是基於本規範審計後發現的問題，供後續逐一修正時參照：

| 問題 | 影響的 Skill | 修正方式 |
|------|-------------|---------|
| description 含工具名稱 | 全部 16 個 BIM Skill | 工具名移到 body 的「工具」表格 |
| description 非第三人稱動詞開頭 | 全部 16 個 BIM Skill | 改為動詞開頭 |
| 缺少 `## Reference` 連結 | 部分 Skill | 加入對應 domain 連結 |
| 缺少 `## 工具` 表格 | 部分 Skill | 從 description 移入 |
| 無 feedback loop | 法規檢查類（fire-safety, smoke-exhaust, building-compliance） | 考慮加入驗證迴圈 |

**不要一次全改。每次用 `/domain` 升級或 skill-creator 改善時，順便修正該 Skill 的上述問題。**

## 7. 檢查清單

在 Skill 建立或修改完成時，對照以下清單：

- [ ] description < 1024 chars，第三人稱動詞開頭
- [ ] description 不含工具名稱
- [ ] description 包含中英文觸發關鍵字
- [ ] body < 500 行
- [ ] 有 `## Reference` 連結到 domain
- [ ] 工具名稱在 body 的表格中，不在 description
- [ ] name 為 kebab-case，與資料夾名稱一致
- [ ] 已確認對應的 domain 文件存在
