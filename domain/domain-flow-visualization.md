---
name: domain-flow-visualization
description: "把 domain/*.md 的流程邏輯轉成 Mermaid 圖的方法論：依 SOP 的形狀挑圖表型態、用流程健檢清單抓迴圈／死路／缺口、產出 GitHub 原生可渲染的 ```mermaid fence 並回嵌 domain。供剛寫完 domain 的老師把流程圖像化、檢查邏輯、統整資訊結構時使用。觸發：domain 流程圖、流程圖像化、mermaid、圖表型態、流程健檢、迴圈檢查、diagram、flowchart、visualize domain。"
metadata:
  version: "1.1"
  updated: "2026-06-21"
  created: "2026-06-19"
  references:
    - "https://github.com/mermaid-js/mermaid"
    - "https://mermaid.js.org/syntax/flowchart.html"
    - "https://mermaid.js.org/syntax/classDiagram.html"
  related:
    - skill-authoring-standard.md
    - frontmatter-standard.md
  referenced_by:
    - domain-diagram
  tags: [mermaid, 流程圖, 視覺化, domain, 健檢, diagram, flowchart]
---

# Domain 流程圖像化方法（Mermaid）

把一份剛寫好的 `domain/*.md` 的流程，轉成一張**人看得懂、GitHub 直接渲染、可回頭修正 domain** 的 Mermaid 圖。
這份文件是**知識（方法）**；互動編排由 `/domain-diagram` skill 負責。

## 設計前提：render 目標 = GitHub 原生 fence

本專案的 domain 圖**內嵌在 `domain/*.md`** 並依賴 GitHub markdown 原生渲染（` ```mermaid ` fence）。
因此只能用 GitHub 的 Mermaid 引擎支援的型態，**不要**用需要外掛或 beta 的型態。

| 可安全使用（GitHub 原生） | 避免（需 CDN/外掛/beta，GitHub 不渲染） |
|---|---|
| `flowchart` / `graph`、`sequenceDiagram`、`stateDiagram-v2`、`classDiagram`、`erDiagram`、`mindmap`、`timeline`、`gantt`、`pie`、`gitGraph`、`journey`、`quadrantChart` | `architecture-beta`、`kanban`、`treemap`、`radar`、`packet`、`zenuml`（需 `@mermaid-js/mermaid-zenuml` 外掛）、`block`、`C4*`（支援不穩） |

> 老師提到的 **zenUML** 很適合表達訊息往返，但它是 Mermaid 外掛、GitHub 不原生渲染。
> 在 GitHub-fence 目標下，凡是想用 zenUML 的場景，一律改用 **`sequenceDiagram`**（語意等價、原生支援）。

## 產圖機制：腳本驅動（flowchart 禁手寫）

為了讓每張 domain 圖長相一致、不在形狀/配色/跳脱/安全子集上漂移，**flowchart 一律由
`.claude/skills/domain-diagram/scripts/mermaid_from_spec.py` 產生**。模型只負責把流程整理成
結構化 spec（JSON），**不手刻 ` ```mermaid ` fence**。

```text
討論 → 寫流程 spec(JSON) → python3 mermaid_from_spec.py spec.json → 回嵌 domain
                              ├ audit : 自動跑健檢項 1–4
                              └ render: 確定性產出安全子集 fence
```

- spec 結構與 `kind` 對照見 `.claude/skills/domain-diagram/references/spec-schema.md`。
- 腳本自動處理：節點形狀、`classDef` 語意色、`<br/>` 換行、label 引號跳脱、只吐安全子集。
- 健檢項 1–4（迴圈/死路/不可達/決策完備）由腳本自動偵測；項 5（前置缺口）、項 6（原子性）仍須人工判斷。

**程式化進度**：目前僅 `flowchart` 程式化；`stateDiagram-v2` / `sequenceDiagram` /
`classDiagram` 等暫用下方速查表手寫模板，並貼 <https://mermaid.live> 目視驗證後回嵌。
進度與 log/lint 接續依據見 `.claude/skills/domain-diagram/references/programmatization-roadmap.md`。

## 步驟 a — 討論（先對齊意圖）

在畫圖前，和 domain 作者確認三件事：

1. **這份 domain 的「主流程」是什麼？** 一句話講出輸入 → 動作 → 輸出。
2. **有沒有迴圈或分支？** 例如「逐一處理每個變動檔」「依模式 A/B/C 分流」。
3. **這張圖給誰看？** 自己檢查邏輯（→ 詳盡）vs README 給讀者看（→ 精簡）。

## 步驟 b — 流程健檢清單（在畫圖時同步檢查）

把 domain 的步驟攤平成節點與邊之後，逐項檢查。這是本方法的**核心價值**——
圖只是手段，目的是讓作者看見自己 SOP 的邏輯漏洞。

| # | 檢查項 | 判準 | 典型漏洞 |
|---|---|---|---|
| 1 | **無窮迴圈** | 每個迴圈都要有「在有限集合上必然觸發」的退出條件 | 迴圈靠某旗標退出，但旗標沒人設 |
| 2 | **死路／不可達** | 每個節點都能走到某個終點；沒有孤立節點 | 寫了一個步驟但沒有任何邊指向它 |
| 3 | **終點覆蓋** | 所有宣告的出口都可達（正常結束／中止／無事可做） | 「中止」分支實際永遠走不到 |
| 4 | **決策完備** | 每個決策節點的**所有分支**都有定義（是/否都要畫） | 只畫了「是」，「否」懸空 |
| 5 | **前置缺口** | 流程依賴的基準／前置條件有被定義 | 「diff 上次之後」但沒記錄「上次」是什麼（baseline 未定義） |
| 6 | **原子性** | 多步寫入若中途崩潰，狀態是否一致 | `寫 wiki → 更新 index → 追加 log` 三段獨立寫，中間崩潰留半成品 |

> 範例（取自 `/ingest` 流程健檢）：迴圈 `取檔 → … → 還有變動檔? → 取檔` 是安全的（git diff 是有限集合）；
> 但「上次 ingest 的基準點」沒定義是真正的缺口（第 5 項）——這就是圖該幫作者抓出來的東西。

## 步驟 c — 建議圖表型態（依 domain 的形狀選）

| domain 流程的形狀 | 建議型態 | 為什麼 |
|---|---|---|
| 有分支與迴圈的步驟鏈（多數 SOP） | `flowchart TD` | 決策菱形 + 迴圈邊最直觀 |
| 有限的「狀態／模式」切換（如連線 on/off、dwg 建柱的 A/B/C 三模式） | `stateDiagram-v2` | 狀態與轉移是一等公民 |
| 跨角色的訊息往返（AI ↔ MCP Server ↔ Revit；tool call → command → response） | `sequenceDiagram` | 生命線清楚表達「誰呼叫誰、何時回」 |
| 資料／實體關係（Tool↔Command↔Domain↔Skill、category 欄位模型） | `classDiagram` 或 `erDiagram` | 表達結構與關聯，非流程 |
| 概念目錄／分類樹（domain 依專業分群、5 個 MCP_PROFILE） | `mindmap` | 階層展開、無方向性 |
| 時間排序的階段／里程碑（roadmap、版本節奏） | `timeline` 或 `gantt` | 時間軸 |

選型口訣：**有迴圈→flowchart；有模式→state；有往返→sequence；有結構→class/er；有階層→mindmap；有時間→timeline/gantt。**

## 最小語法速查（只列 GitHub 原生、domain 最常用的四種）

### flowchart（最常用）
```text
flowchart TD
  Start([開始]) --> D{決策?}
  D -- 是 --> A[動作 A]
  D -- 否 --> Stop([結束])
  A --> More{還有?}
  More -- 是 --> D
  More -- 否 --> Done([完成])
```
- 節點形狀：`([圓角=起訖])`、`{菱形=決策}`、`[方框=動作]`、`[(圓柱=資料庫)]`、`[/平行四邊形=I/O/]`
- 換行用 `<br/>`；中文直接寫即可。

### stateDiagram-v2（模式／生命週期）
```text
stateDiagram-v2
  [*] --> Idle
  Idle --> Connected: MCP 連入
  Connected --> Idle: 新連線取代 / 重啟服務
  Connected --> [*]
```

### sequenceDiagram（訊息往返，取代 zenUML）
```text
sequenceDiagram
  participant AI as AI Client
  participant S as MCP Server
  participant R as Revit Add-in
  AI->>S: tool call (snake_case)
  S->>R: WebSocket command
  R-->>S: response
  S-->>AI: result
```

### classDiagram / erDiagram（結構）
```text
classDiagram
  Skill --> Domain : references
  Domain --> Tool : 呼叫
```

## 配色與主題

- **GitHub 原生 fence**：不要自帶 theme/`themeVariables`——GitHub 用讀者的明暗模式自動配色，硬寫顏色反而在某一模式下不可讀。
- 只在「動作 vs 決策 vs 終止／中止」需要區分時，用 `classDef` + `class` 上**語意色**（綠=正常結束、紅=中止、藍=決策），這在 GitHub 可渲染。
- 若是要放進 `docs/*.html` 簡報（非 domain），才沿用專案 dark theme（見 `docs/deploy-flow.html` 的 `mermaid.initialize`）。

## 回嵌 domain 的慣例

1. 圖放在該 domain 的流程章節下，用 ` ```mermaid ` fence（不是貼圖片）——原始碼可 diff、可被下一位老師修。flowchart 直接貼腳本輸出，**不手改 fence**；要改回去改 spec 重跑。
2. 圖**之後**接一段「流程健檢結論」短文，把步驟 b 抓到的迴圈/缺口寫成幾條 finding（OK / 缺口 / 設計疑慮）。
3. 圖若揭露了 domain 本身的邏輯漏洞 → **先修 domain 文字，再讓圖與文字一致**。圖是檢查工具，不是裝飾。

## 與既有資產的關係

- 已有 7 個 domain 內嵌 `mermaid` fence（`auto-dimension-workflow`、`curtain-wall-pattern`、`detail-component-sync`、`exterior-wall-opening-check`、`facade-generation`、`sheet-viewport-management` 等）——本方法把這個做法標準化並擴及全部 domain。
- `docs/deploy-flow.html` 是 HTML viewer（CDN mermaid@11 + dark theme），屬「簡報/文件」surface，配色規則不同（見上）。

## Reference

- Mermaid 原始碼與型態註冊表：https://github.com/mermaid-js/mermaid
- 各型態語法：https://mermaid.js.org/syntax/flowchart.html 起（class、state、sequence、er、mindmap…）
- 流程健檢的具體範例：`docs/mermaid-test.html`（Obsidian Ingest 流程的迴圈/缺口分析）
