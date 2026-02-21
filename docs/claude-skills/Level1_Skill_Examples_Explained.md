# Level 1 Skill 範例解說：純 SKILL.md 的兩個實例

> 本文件翻譯並解析 `skills-search` 和 `fact-checker` 兩個 **Level 1 Skill**（只有 SKILL.md，沒有 scripts/ 或 references/）。
> 目的是讓你逐條看懂：一個「純 prompt」的 Skill 到底寫了什麼、為什麼這樣寫。
>
> 📎 原始檔案位置：
> - [`skills-search/SKILL.md`](./skills-search/SKILL.md)
> - [`fact-checker/SKILL.md`](./fact-checker/SKILL.md)

---

## 一、skills-search（Skill 搜尋工具）

> **原始檔案**：[`skills-search/SKILL.md`](./skills-search/SKILL.md)（179 行，4.1 KB）
>
> **本質**：這是一份「教 Claude 怎麼使用 `ccpm` CLI 工具」的操作手冊。沒有腳本、沒有參考資料——純文字指令。

---

### YAML Frontmatter（元數據區塊）— 第 1-4 行

```yaml
---
name: skills-search
description: This skill should be used when users want to search, discover,
  install, or manage Claude Code skills from the CCPM registry. Triggers include
  requests like "find skills for PDF", "search for code review skills",
  "install cloudflare-troubleshooting", "list my installed skills",
  "what does skill-creator do", or any mention of finding/installing/managing
  Claude Code skills or plugins.
---
```

**📖 翻譯：**
- `name`：`skills-search`（Skill 名稱）
- `description`：當使用者想要**搜尋、發現、安裝或管理** Claude Code 的 Skill 時，應使用此 Skill。觸發關鍵字包括：「幫我找 PDF 相關的 Skill」、「搜尋程式碼審查 Skill」、「安裝 cloudflare-troubleshooting」、「列出我已安裝的 Skill」、「skill-creator 是做什麼的」，或任何提到尋找/安裝/管理 Claude Code Skill 或 Plugin 的請求。

**💡 設計解讀：**

> `description` 是整個 Skill **最重要的欄位**。它被注入到 System Prompt 中，Claude 根據這段文字來判斷「使用者的請求是否應該觸發這個 Skill」。
>
> 注意它刻意列出了**多個觸發範例**（"find skills for PDF", "list my installed skills"…），這是為了讓 Claude 能準確辨識各種不同的說法都指向同一個意圖。
>
> 🏗️ **建築類比**：這就像顧問名冊上的「專長欄位」——寫得越精確，專案經理就越知道什麼時候該找這個顧問。如果只寫「消防」太籠統；寫「消防法規檢討、避難路徑規劃、防火區劃分析」就很清楚。

---

### 概述 (Overview) — 第 6-10 行

```markdown
# Skills Search

## Overview
Search, discover, and manage Claude Code skills from the CCPM
(Claude Code Plugin Manager) registry. This skill wraps the `ccpm`
CLI to provide seamless skill discovery and installation.
```

**📖 翻譯：**

搜尋、發現和管理來自 CCPM（Claude Code Plugin Manager，Claude Code 外掛管理器）登錄庫的 Skill。此 Skill 封裝了 `ccpm` CLI 工具，提供無縫的 Skill 發現與安裝體驗。

**💡 設計解讀：**

> 這是 Claude 載入完整 SKILL.md（Level 2）之後才會讀到的內容。相比 frontmatter 中簡短的 description，這裡給出更完整的背景。告訴 Claude：「你的核心工具是 `ccpm` CLI」。

---

### 快速上手 (Quick Start) — 第 12-26 行

```bash
# 搜尋 Skill
ccpm search <query>

# 安裝 Skill
ccpm install <skill-name>

# 列出已安裝的 Skill
ccpm list

# 查看某個 Skill 的詳細資訊
ccpm info <skill-name>
```

**💡 設計解讀：**

> 這是「速查表」——Claude 載入這個 Skill 後，可以快速找到最常用的四個指令。不需要翻到後面的細節就能立即行動。
>
> 🏗️ **建築類比**：就像 SOP 文件最前面的「一頁摘要 (Executive Summary)」，讓人不需要看完整份文件就能開始作業。

---

### 指令參考 (Commands Reference) — 第 28-122 行

這一大段是每個指令的**詳細說明**，包含語法、選項、範例：

#### 搜尋 Skill (`ccpm search`)

```bash
ccpm search <query> [options]

# 選項：
#   --limit <n>    最多顯示幾筆結果（預設：10）
#   --json         輸出為 JSON 格式
```

**範例：**
```bash
ccpm search pdf              # 搜尋 PDF 相關的 Skill
ccpm search "code review"    # 搜尋程式碼審查 Skill
ccpm search cloudflare       # 搜尋 Cloudflare 工具
ccpm search --limit 20 react # 搜尋 React Skill，顯示 20 筆結果
```

#### 安裝 Skill (`ccpm install`)

```bash
ccpm install <skill-name> [options]

# 選項：
#   --project      僅安裝到目前專案（預設：使用者層級）
#   --force        強制重新安裝（即使已安裝）
```

**範例：**
```bash
ccpm install pdf-processor                    # 安裝 pdf-processor Skill
ccpm install @daymade/skill-creator           # 安裝有命名空間的 Skill
ccpm install cloudflare-troubleshooting       # 安裝障礙排除 Skill
ccpm install react-component-builder --project # 僅安裝到目前專案
```

> **⚠️ 重要**：安裝 Skill 後，必須重新啟動 Claude Code 才能使用。

#### 列出已安裝的 Skill (`ccpm list`)

```bash
ccpm list [options]

# 選項：
#   --json         輸出為 JSON 格式
```

**輸出內容包含：**
- Skill 名稱與版本
- 安裝範圍（使用者層級/專案層級）
- 安裝路徑

#### 查看 Skill 資訊 (`ccpm info`)

```bash
ccpm info <skill-name>
```

**輸出內容包含：**
- 名稱、描述、版本
- 作者與儲存庫
- 下載次數與標籤
- 相依套件（如有）

#### 移除 Skill (`ccpm uninstall`)

```bash
ccpm uninstall <skill-name> [options]

# 選項：
#   --global       從使用者層級移除
#   --project      從專案層級移除
```

---

### 工作流程 (Workflow) — 第 124-145 行

**📖 翻譯：** 當使用者需要的功能可能已有現成的 Skill 時：

1. **搜尋**相關的 Skill：`ccpm search <相關關鍵字>`
2. **審查**搜尋結果——查看下載次數和描述
3. **取得詳情**：`ccpm info <skill-name>`
4. **安裝**選定的 Skill：`ccpm install <skill-name>`
5. **通知使用者**需要重新啟動 Claude Code 才能使用新 Skill

**💡 設計解讀：**

> 這是定義 Claude 的**決策流程**——不只是告訴它有哪些指令可用，更告訴它**按什麼順序使用**。先搜尋、再審查、再安裝，而不是看到使用者說「安裝」就直接裝。
>
> 🏗️ **建築類比**：這就像採購 SOP——不是業主說要買什麼就直接買，而是：搜尋廠商 → 比價/審查 → 取得詳細報價 → 下單 → 通知到貨。

---

### 熱門 Skill (Popular Skills) — 第 147-159 行

| Skill                        | 用途                       |
| ---------------------------- | -------------------------- |
| `skill-creator`              | 建立新的 Claude Code Skill |
| `pdf-processor`              | PDF 操作與分析             |
| `docx`                       | Word 文件處理              |
| `xlsx`                       | Excel 試算表操作           |
| `pptx`                       | PowerPoint 簡報製作        |
| `cloudflare-troubleshooting` | 除錯 Cloudflare 問題       |
| `prompt-optimizer`           | 改善提示詞品質             |

**💡 設計解讀：**

> 當 Claude 不確定要推薦什麼 Skill 給使用者時，這張表提供了「預設推薦清單」。

---

### 疑難排解 (Troubleshooting) — 第 161-178 行

| 問題                                          | 解決方案                                                        |
| --------------------------------------------- | --------------------------------------------------------------- |
| `ccpm: command not found`（找不到 ccpm 指令） | 全域安裝 CCPM：`npm install -g @daymade/ccpm`                   |
| 安裝後 Skill 無法使用                         | 重新啟動 Claude Code（Skill 在啟動時載入）                      |
| 權限錯誤                                      | 使用預設的使用者層級安裝，或檢查 `~/.claude/skills/` 的寫入權限 |

---

### 📊 skills-search 結構總結

```
YAML Frontmatter (觸發條件)     ← Level 1：常駐 System Prompt
│
├── Overview (概述)              ← Level 2：被觸發後才載入
├── Quick Start (速查表)
├── Commands Reference (指令詳參)
│   ├── search (搜尋)
│   ├── install (安裝)
│   ├── list (列出)
│   ├── info (查詢)
│   └── uninstall (移除)
├── Workflow (工作流程)           ← 定義 Claude 的決策順序
├── Popular Skills (熱門推薦)
└── Troubleshooting (疑難排解)
```

**一句話總結：** 這份 SKILL.md 就是一本「ccpm CLI 使用手冊」——教 Claude 怎麼用這個工具幫使用者搜尋和安裝 Skill。沒有腳本、沒有自動化，純粹靠文字指令引導 Claude 的行為。

---
---

## 二、fact-checker（事實查核工具）

> **原始檔案**：[`fact-checker/SKILL.md`](./fact-checker/SKILL.md)（284 行，8.2 KB）
>
> **本質**：這是一份「事實查核五步驟 SOP」。教 Claude 如何系統性地辨識文件中的事實性聲明、搜尋權威來源進行驗證、產出報告、並在使用者同意後修正。純文字工作流程，沒有任何腳本。

---

### YAML Frontmatter（元數據區塊）— 第 1-4 行

```yaml
---
name: fact-checker
description: Verifies factual claims in documents using web search and official
  sources, then proposes corrections with user confirmation. Use when the user
  asks to fact-check, verify information, validate claims, check accuracy, or
  update outdated information in documents. Supports AI model specs, technical
  documentation, statistics, and general factual statements.
---
```

**📖 翻譯：**
- `name`：`fact-checker`（事實查核工具）
- `description`：使用網路搜尋和官方來源驗證文件中的事實性聲明，然後在使用者確認後提出修正。當使用者要求**事實查核、驗證資訊、驗證聲明、檢查準確性**或**更新文件中過時的資訊**時使用。支援 AI 模型規格、技術文件、統計數據和一般事實性陳述。

**💡 設計解讀：**

> 注意 description 中列出了多種觸發場景，而且最後一句說明了「支援的範圍」（AI 模型規格、技術文件…），讓 Claude 知道這個 Skill 不只是查新聞真假——它更擅長**技術性的事實驗證**。

---

### 何時使用 (When to use) — 第 10-17 行

**📖 翻譯：** 當使用者提出以下請求時觸發：

- 「幫我查核這份文件的事實」
- 「驗證這些 AI 模型的規格」
- 「檢查這些資訊是否仍然正確」
- 「更新這個檔案中過時的資料」
- 「驗證這個段落中的聲明」

**💡 設計解讀：**

> 這些是比 frontmatter 更具體的觸發範例。Frontmatter 中的 description 是給 **metadata 層（Level 1）** 用的——Claude 啟動時就能看到；而這裡的 "When to use" 是給 **SKILL.md body（Level 2）** 用的——Claude 被觸發後才會看到，用來進一步確認自己應該怎麼行動。

---

### 工作流程 (Workflow) — 第 19-141 行

這是整個 Skill 的**核心——五步驟 SOP**：

#### 步驟 1：辨識事實性聲明（Identify factual claims）

**📖 翻譯：** 掃描文件中可驗證的陳述：

**要查核的聲明類型：**
- 技術規格（context window 大小、定價、功能）
- 版本號碼和發布日期
- 統計數據和指標
- API 能力和限制
- 效能基準分數

**要跳過的主觀內容：**
- 意見和推薦
- 解釋性的論述
- 教學指示
- 架構討論

**💡 設計解讀：**

> 這裡精確地告訴 Claude **什麼該查、什麼不該查**。這是一個很好的 prompt 技巧——當你同時定義「要做什麼」和「不要做什麼」時，模型的行為會更精準。
>
> 🏗️ **建築類比**：就像建築師在做「法規檢討」時，知道要查的是「建蔽率、容積率、建築高度、退縮距離」這些**客觀數據**，而不是去評判「這個設計美不美」。

---

#### 步驟 2：搜尋權威來源（Search authoritative sources）

**📖 翻譯：** 針對每個聲明，搜尋官方來源：

**AI 模型相關：**
- 官方新聞頁面（anthropic.com/news, openai.com/index, blog.google）
- API 文件（platform.claude.com/docs, platform.openai.com/docs）
- 開發者指南和發行說明

**技術函式庫：**
- 官方文件網站
- GitHub 儲存庫（releases, README）
- 套件登錄庫（npm, PyPI, crates.io）

**一般性聲明：**
- 學術論文和研究
- 政府統計數據
- 產業標準組織

**搜尋策略：**
- 使用「模型名稱 + 規格」搜尋（例如："Claude Opus 4.5 context window"）
- 加入當前年份以獲取最新資訊
- 盡可能從多個來源交叉驗證

**💡 設計解讀：**

> 注意這裡列出了**具體的網站 URL**——這不是隨意的。它告訴 Claude 在做 web search 時，優先信任哪些來源。這是一個很重要的「知識引導」技巧。
>
> 🏗️ **建築類比**：就像你告訴新進的設計師：「查法規要去全國法規資料庫、營建署網站，不要隨便 Google 找部落格文章」。

---

#### 步驟 3：比對聲明與來源（Compare claims against sources）

**📖 翻譯：** 建立比對表格：

| 文件中的聲明                   | 來源資訊                       | 狀態               | 權威來源                 |
| ------------------------------ | ------------------------------ | ------------------ | ------------------------ |
| Claude 3.5 Sonnet: 200K tokens | Claude Sonnet 4.5: 200K tokens | ❌ 過時的模型名稱   | platform.claude.com/docs |
| GPT-4o: 128K tokens            | GPT-5.2: 400K tokens           | ❌ 版本和規格不正確 | openai.com/index/gpt-5-2 |

**狀態代碼：**
- ✅ 正確——聲明與來源一致
- ❌ 不正確——聲明與來源矛盾
- ⚠️ 過時——聲明曾經正確但已被取代
- ❓ 無法驗證——找不到權威來源

**💡 設計解讀：**

> 這裡提供了一個**結構化的輸出範本**，讓 Claude 知道報告應該長什麼樣子。而且用了 emoji 狀態碼（✅❌⚠️❓），既直觀又容易掃描。
>
> 🏗️ **建築類比**：就像法規檢討報告中的「檢核對照表」——每一條法規列一行，旁邊標示「符合 ✅ / 不符合 ❌ / 不適用 N/A」。

---

#### 步驟 4：產出修正報告（Generate correction report）

**📖 翻譯：** 以結構化格式呈現發現：

```markdown
## 事實查核報告

### 摘要
- 查核聲明總數：X
- 正確：Y
- 發現問題：Z

### 需要修正的問題

#### 問題 1：過時的 AI 模型引用
**位置：** docs/file.md 的第 77-80 行
**目前聲明：** "Claude 3.5 Sonnet: 200K tokens"
**修正為：** "Claude Sonnet 4.5: 200K tokens"
**來源：** https://platform.claude.com/docs/en/build-with-claude/context-windows
**原因：** Claude 3.5 Sonnet 已被 Claude Sonnet 4.5 取代（2025 年 9 月發布）
```

**💡 設計解讀：**

> 這個報告範本包含了所有關鍵資訊：**在哪裡（位置）、原來寫什麼、應該改成什麼、依據是什麼、為什麼要改**。這是一個完整的「變更請求 (Change Request)」格式。

---

#### 步驟 5：經使用者同意後套用修正（Apply corrections with user approval）

**📖 翻譯：**

**修改前：**
1. 向使用者展示修正報告
2. 等待明確同意：「需要我套用這些修正嗎？」
3. 只有在確認後才繼續

**套用修正時：**
```python
# 使用 Edit 工具更新文件
Edit(
    file_path="docs/file.md",
    old_string="- Claude 3.5 Sonnet: 200K tokens（约 15 万汉字）",
    new_string="- Claude Sonnet 4.5: 200K tokens（约 15 万汉字）"
)
```

**修正後：**
1. 驗證所有編輯已成功套用
2. 記錄修正摘要（例如：「在 2.1 節更新了 4 個聲明」）
3. 提醒使用者 commit 變更

**💡 設計解讀：**

> **⚠️ 這是最重要的設計決策之一：Claude 不會自動修改文件。** 它被明確指示要先展示報告、等待使用者同意，才能動手改。這體現了 Skill 設計中的**安全原則**——即使有能力自動修正，也要讓人類保持最終控制權。
>
> 🏗️ **建築類比**：就像建築師提出的「設計變更建議 (Design Change Proposal)」——必須經過業主或專案經理簽核後才能執行，不是設計師自己決定就改了。

---

### 搜尋最佳實踐 (Search best practices) — 第 143-188 行

#### 查詢建構 (Query construction)

**好的查詢**（具體、包含時間）：
- "Claude Opus 4.5 context window 2026"
- "GPT-5.2 official release announcement"
- "Gemini 3 Pro token limit specifications"

**差的查詢**（模糊、籠統）：
- "Claude context"
- "AI models"
- "Latest version"

#### 來源評估 (Source evaluation)

**優先使用官方來源：**
1. 產品官方頁面（最高權威）
2. API 文件
3. 官方部落格公告
4. GitHub releases（開源專案用）

**謹慎使用：**
- 第三方彙整網站（如 llm-stats.com）——需與官方來源交叉驗證
- 部落格文章——需交叉比對
- 社群媒體——僅用於公告，需在他處驗證

**避免使用：**
- 過時的文件
- 沒有引用來源的非官方 Wiki
- 推測和謠言

#### 處理模糊情況 (Handling ambiguity)

**當來源互相矛盾時：**
1. 以最新的官方文件為優先
2. 在報告中註明差異
3. 向使用者呈現兩個來源
4. 如果事關重大，建議聯繫廠商

**當找不到來源時：**
1. 標記為 ❓ 無法驗證
2. 建議替代措辭：「根據 [來源]，截至 [日期]…」
3. 建議添加限定詞：「大約」、「據報導」

---

### 特殊考量 (Special considerations) — 第 190-222 行

#### 時效性資訊

**好的修正：**
- 「截至 2026 年 1 月」
- "Claude Sonnet 4.5 (released September 2025)"

**差的修正：**
- 「最新版本」（很快就會過時）
- 「目前的模型」（時間框架模糊）

#### 數字精度

- 來源說「大約 100 萬 tokens」→ 寫成「1M tokens（大約）」
- 來源說「200,000 token context window」→ 寫成「200K tokens」（精確值）

#### 引用格式

修正中應包含引用：
```markdown
> **注**：具體上下文窗口以模型官方文檔為準，本書寫作時使用 Claude Sonnet 4.5 為主要工具。
```

---

### 範例 (Examples) — 第 224-257 行

| 範例         | 使用者請求                               | 流程                                                                   |
| ------------ | ---------------------------------------- | ---------------------------------------------------------------------- |
| 技術規格更新 | 「查核 2.1 節的 AI 模型 context window」 | 辨識聲明 → 搜尋官方文件 → 發現差異 → 產出報告 → 經同意後修正           |
| 統計數據驗證 | 「驗證第 5 章的效能基準分數」            | 提取數字聲明 → 搜尋官方基準發布 → 比較 → 標示差異 → 更新               |
| 版本號驗證   | 「檢查這些函式庫版本是否仍是最新的」     | 列出所有版本號 → 查套件登錄庫 → 辨識過時版本 → 建議更新 → 經確認後修正 |

---

### 品質清單 (Quality checklist) — 第 259-270 行

**📖 翻譯：** 完成事實查核前的確認項目：

- [ ] 所有事實性聲明已辨識並分類
- [ ] 每個聲明已透過權威來源驗證
- [ ] 來源具有權威性且為最新
- [ ] 修正報告清晰且可執行
- [ ] 已包含時效性脈絡（如適用）
- [ ] 已取得使用者同意才修改
- [ ] 所有編輯驗證成功
- [ ] 已向使用者提供摘要

---

### 限制 (Limitations) — 第 272-284 行

**📖 翻譯：**

**此 Skill 無法：**
- 驗證主觀意見或判斷
- 存取付費牆或受限的來源
- 在有爭議的聲明中判定「真相」
- 預測未來的規格或功能

**遇到這些情況時：**
- 在報告中註明限制
- 建議使用限定性語言
- 建議使用者自行研究或諮詢專家

---

### 📊 fact-checker 結構總結

```
YAML Frontmatter (觸發條件)         ← Level 1：常駐 System Prompt
│
├── When to use (觸發場景)           ← Level 2：被觸發後才載入
├── Workflow (五步驟 SOP)            ← 核心！整個 Skill 的靈魂
│   ├── Step 1: 辨識事實性聲明
│   ├── Step 2: 搜尋權威來源
│   ├── Step 3: 比對聲明與來源       ← 含狀態碼定義（✅❌⚠️❓）
│   ├── Step 4: 產出修正報告         ← 含報告範本
│   └── Step 5: 經同意後套用修正     ← 安全原則！先報告再修改
├── Search best practices (搜尋最佳實踐)
│   ├── 查詢建構
│   ├── 來源優先順序
│   └── 模糊情況處理
├── Special considerations (特殊考量)
│   ├── 時效性
│   ├── 數字精度
│   └── 引用格式
├── Examples (範例)                  ← 三個具體場景
├── Quality checklist (品質清單)     ← 完成前的自我檢查
└── Limitations (限制)               ← 明確告知不能做什麼
```

**一句話總結：** 這份 SKILL.md 是一本完整的「事實查核 SOP 手冊」——定義了從辨識聲明、搜尋來源、比對驗證、產出報告到套用修正的完整五步驟工作流程，而且嵌入了安全原則（使用者必須同意才修改）。沒有腳本、沒有自動化——所有的「智慧」都靠 Claude 自己的能力執行，SKILL.md 只負責**指導方向和定義流程**。

---
---

## 三、兩個 Skill 的比較觀察

| 比較維度                | `skills-search`                       | `fact-checker`                             |
| ----------------------- | ------------------------------------- | ------------------------------------------ |
| **行數**                | 179 行                                | 284 行                                     |
| **大小**                | 4.1 KB                                | 8.2 KB                                     |
| **本質**                | 工具操作手冊（教 Claude 用 ccpm）     | 工作流程 SOP（五步驟事實查核）             |
| **核心結構**            | 指令參考（syntax + examples）         | 流程定義（step-by-step workflow）          |
| **Claude 的角色**       | 翻譯使用者意圖 → 執行正確的 ccpm 指令 | 扮演「事實查核員」→ 搜尋、比對、報告、修正 |
| **有腳本嗎？**          | ❌                                     | ❌                                          |
| **有 references/ 嗎？** | ❌                                     | ❌                                          |
| **安全機制**            | 無特殊（ccpm 本身不危險）             | ✅ 修改前必須取得使用者同意                 |

### 🏗️ 建築人的總評

|                          | `skills-search`                                            | `fact-checker`                                                     |
| ------------------------ | ---------------------------------------------------------- | ------------------------------------------------------------------ |
| **建築類比**             | **產品型錄（Catalog）**                                    | **品管檢查 SOP**                                                   |
| **就像…**                | 告訴新設計師：「我們的材料資料庫在哪、怎麼搜尋、怎麼訂貨」 | 告訴品管工程師：「檢查時要看哪些項目、標準是什麼、不合格怎麼處理」 |
| **設計師需要什麼能力？** | 會用搜尋工具就好                                           | 需要判斷力、查資料能力、報告寫作能力                               |
