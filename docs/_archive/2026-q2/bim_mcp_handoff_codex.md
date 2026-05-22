# Handoff Plan · BIM_MCP 站點剩餘工程交接給 Codex

> **撰寫日期**：2026-05-18
> **目的**：把 BIM_MCP/ 站點剩餘 5 個頁面 + 互動升級 + 配圖 prompt 集移交另一引擎獨立執行。
> **目標完工**：5/23 月小聚前。
> **本檔位置**：`docs/BIM_MCP/_HANDOFF_CODEX.md`（這份）。
> **附屬參考**：
> - 設計 system：`docs/BIM_MCP/styles.css`（已完備，不要動）
> - 品質標準樣本：`docs/BIM_MCP/reference/philosophy-22-propositions.html`（GENAI 等級品質目標）
> - 結構樣本：`docs/BIM_MCP/reference/three-constitutions.html`（小而完整，模仿這個）
> - GENAI 借鏡來源：`C:/Users/Admin/Desktop/GENAI-main/GENAI-main/web/`

---

## 目錄

1. [Context — 已完成 vs 剩餘](#context)
2. [Part 1 — 剩餘 5 個頁面的逐頁規格](#part-1)
3. [Part 2 — GENAI/web 借鏡分析（drawer / 微互動 / 配圖）](#part-2)
4. [Part 3 — CODEX IMAGE2 配圖 prompt 集（Jack Butcher 風格）](#part-3)
5. [Part 4 — Drawer / 互動 / 配圖整合到既有 6 頁的計畫](#part-4)
6. [Part 5 — 驗收清單](#part-5)

---

<a id="context"></a>

## 1. Context — 已完成 vs 剩餘

### 已完成（6/8 頁）

| 路徑 | 行數 | 狀態 |
|---|---|---|
| `docs/BIM_MCP/styles.css` | 786 | ✅ 完備 |
| `docs/BIM_MCP/reference/architecture-v2.html` | 397 | ✅ |
| `docs/BIM_MCP/reference/philosophy-22-propositions.html` | ~2200 | ✅ 22 命題完整版 |
| `docs/BIM_MCP/reference/three-constitutions.html` | ~600 | ✅ 三憲法 |
| `docs/BIM_MCP/reference/industry-evidence.html` | 428 | ✅ 100×/70%/$15-25K + 方法論 + Early Mover timeline |
| `docs/BIM_MCP/reference/spectrum-decision.html` | 459 | ✅ A/B/C/D 框架 + 採光/排煙案例 |
| `docs/BIM_MCP/reference/skills-index.html` | ~460 | ✅ 19 Skill |
| `docs/BIM_MCP/2026-04/karpathy-wiki.html` | - | ✅ 已搬完 |

### 剩餘（要交給 Codex 做的）

| 優先級 | 任務 | 章節 |
|---|---|---|
| 🟡 | `reference/domain-index.html`（35+ Domain） | Part 1.1 |
| 🟢 | `reference/deployment-guide.html` | Part 1.2 |
| 🟢 | `reference/troubleshooting.html` | Part 1.3 |
| 🟢 | `reference/contributor-template.html` | Part 1.4 |
| 🟢 | `docs/BIM_MCP/index.html`（站點入口） | Part 1.5 |
| 🟡 | 把 GENAI/web 的 drawer / 微互動 / 圖層 借鏡套到既有 6 頁 + 新 5 頁 | Part 4 |
| 🟡 | 產出 Jack Butcher 風格配圖（CODEX IMAGE2） | Part 3 |

### 約束條件（不准違反）

1. **不動 `styles.css`**——所有頁面共用此 stylesheet。若需擴充樣式，加在頁面 `<style>` 區塊內、不污染共用層。
2. **不動既有 6 頁**——除了 Part 4 的「整合」改動需精準，不可重寫。
3. **不重新發明 layout 系統**——延用 `.bim-section` / `.bim-grid-{2,3,4}` / `.bim-card` / `.bim-hero`。
4. **每頁都要有 top nav + footer**——nav 含 `BIM_MCP / reference / [當前頁]` breadcrumb 三層，footer 含上一頁/下一頁/回首頁。
5. **繁體中文（zh-TW）**——所有 user-facing 文字都是繁中。技術術語可保英文（如 `Skill metadata`、`Tool Call`）。
6. **資料誠實度**：所有具體數字、ID、檔名都必須對應到 CLAUDE.md 或既有 domain/ 檔內容，不可編造。

---

<a id="part-1"></a>

## 2. Part 1 — 剩餘 5 個頁面的逐頁規格

每頁規格含：(a) 目標 / (b) 結構章節 / (c) 資料來源 / (d) 特殊樣式需求 / (e) 完工字數估計 / (f) Nav & footer 連結。

---

### 1.1 `reference/domain-index.html`（🟡 最高優先 — 與 skills-index 對偶）

**目標**：列出 `domain/` 目錄 35+ 個 `*.md`，每個含觸發關鍵字 + 被哪個 Skill 引用。

**結構章節**：
1. Hero：「Domain · 知識層」`35+ 個 · 法規 / SOP / Lessons`
2. Section 01：Frame — Skill vs Domain vs Tool 三層分工（沿用 skills-index 的 3-card grid，但這次 Domain card 高亮）
3. Section 02：依分類列 domain 卡片
   - **A · 法規檢核類**（採光、容積、防火、排煙、走廊、停車、外牆、停車淨高、停車自動編號）
   - **B · 工作流類**（element-coloring / element-query / auto-dimension / detail-component-sync / dependent-view-crop / sheet-viewport-management / stair-hidden-line / facade-generation / curtain-wall-pattern / wall-check / room-numbering / room-surface-area-review / stair-compliance / room-boundary）
   - **C · 系統治理類**（lessons / qa-checklist / path-maintenance-qa / session-context-guard / tool-capability-boundary / skill-authoring-standard / frontmatter-standard / claude-md-sync）
   - **D · 跨領域協作類**（mep-csa-clash-detection / mep-extension-guide / mechanical-part-doc / pdf-export-comparison / revit-fill-pattern-conversion / beam-penetration-base / beam-penetration-rc / beam-penetration-sc / beam-penetration-src）
   - **E · 外部 reference**（references/building-code-tw.md — 唯一一個 references/）
4. Section 03：快表（一張全列表）
5. Section 04：Frontmatter 規範簡述 + 連到 `frontmatter-standard.md`

**資料來源**：CLAUDE.md 中「Domain Knowledge & Workflow Files」表（第 ~570-620 行）。**完整 35+ 列表已在該表內**，**直接 1:1 抽出**。對應 Skill 從 skills-index.html 的 domain-link 反查（每個 Skill 卡片內已標 `→ domain-name`，把這個反查表整理出來）。

**特殊樣式**：與 skills-index 對稱——`.domain-card` 用同樣的 grid 樣式（左 220px 名稱、右文+ meta tags），但 accent 用 warn 黃色（`var(--warn)`、`var(--warn-bg)`），與 skills 的藍色形成對比。

**字數**：~600-700 行（35+ card × 平均 12 行 + sections）。

**Nav**：`BIM_MCP / reference / Domain 索引`。Top nav actions: `← Skills 索引` / `22 命題`。
**Footer**：`← Skills 索引` / `22 命題` / `回 BIM_MCP 首頁`（primary）。

**錨點**：每個 domain card 給 `id="<basename-without-md>"`（如 `id="daylight-area-check"`）——對應 skills-index 已寫好的 `domain-link href`。

---

### 1.2 `reference/deployment-guide.html`（🟢）

**目標**：升級自 0425-presentation Block C 八步驟，講 Nice3point 統一 build + Release.R{YY} 多版本 + setup.ps1 / install-addon.ps1 自動化。

**結構章節**：
1. Hero：「部署 · 從零到能用 Revit MCP」`5 個 Revit 版本 / 一條指令`
2. Section 01：前提條件（系統需求 / .NET / Node.js / Revit 版本表）
3. Section 02：一鍵安裝（setup.bat → setup.ps1）含截圖位（見 Part 3 配圖 #4）
4. Section 03：手動部署四步（檢查 Revit 關閉 / build / deploy DLL / 重啟 Revit）
5. Section 04：多版本 build matrix（Release.R22 ~ R26 一張表）
6. Section 05：AI Client 設定（Claude Desktop / Gemini CLI / VS Code Copilot / Claude Code）
7. Section 06：驗證安裝成功（Ribbon 顯示 / 8964 port 通 / smoke test）
8. Section 07：`how-tech` 4 欄 box——WHY/HOW/TECHNIQUE/CHECKLIST
9. Section 08：Further reading 外部 URL（Nice3point.Revit.Sdk、Revit API doc、MCP spec）

**資料來源**：
- CLAUDE.md「Build Commands」「AI Client Setup」段落
- `scripts/setup.ps1`、`scripts/install-addon.ps1`（指令範例）
- `docs/0425-presentation.html`（Block C 結構參考，但要重寫不要 copy）

**特殊樣式**：步驟卡用 numbered list（`<ol class="bim-steps">`）——需在頁面 `<style>` 內定義（圓圈數字編號 + 連線）。

**字數**：~500 行。

**Nav**：`BIM_MCP / reference / 部署指南`。Actions: `troubleshooting →` / `22 命題`。

---

### 1.3 `reference/troubleshooting.html`（🟢）

**目標**：升級自 0425 Block D 五大錯誤 + **本場（5/18）4 fix 歷程**作為「實戰修復案例」。

**結構章節**：
1. Hero：「Troubleshooting · 真實踩雷與修復」`5 經典 + 4 新案 = 9 個`
2. Section 01：診斷流程圖（先看 log / 再看 port / 再看 build config）
3. Section 02：5 個經典錯誤（從 CLAUDE.md「Troubleshooting」表抽）
   - 56 warnings on build → 正常忽略
   - RevitMCP.dll not found → 用 Release.RXX
   - MCP Server connection failed → 路徑 / build / port
   - Port 8964 被 System (PID:4) 佔用 → release-port.ps1
   - Commands not responding → ExternalEventManager
4. Section 03：⭐ **5/18 demo 4 修復歷程**（新增——這是本場獨家內容）
   - **Fix #1**：`check_exterior_wall_openings` 輸出超限——summary mode 新增
   - **Fix #2**：`check_smoke_exhaust_windows` 建 80+ 視圖污染——預設 `createAnnotatedViews=false`
   - **Fix #3**：`check_stair_headroom` 不支援 batch——加 batch 參數
   - **Fix #4**：`get_room_daylight_info` 只回 raw data——這是設計選擇，PASS/FAIL 留給 domain SOP（不算 bug，是 Spec by Design）
5. Section 04：log 怎麼看（`%AppData%\RevitMCP\Logs\` + revit-mcp 自家 log/ 月份檔）
6. Section 05：`how-tech` 4 欄 box
7. Section 06：Further reading

**資料來源**：
- CLAUDE.md Troubleshooting 表
- 4 個 fix 來自最新 commit message `4975ceb fix(tools): 4 工具修復 — 5/18 demo 實機測試暴露的問題`——`git show 4975ceb` 取 diff 抽說明
- 第三段「5/18 demo 4 修復歷程」要呼應 [P15「排程死線」](philosophy-22-propositions.html#p15) 與 [第二憲法 Tool Call Data Honesty](three-constitutions.html#constitution-2)——「真實測試暴露 tool 邊界」是 P15 的活體實證。

**特殊樣式**：
- Fix 卡用紅綠對照（`bim-card-bad` 描述問題 + `bim-card-ok` 描述解法）
- 加 `pitfalls` 樣式（已存在於 styles.css 第 629 行）強調「⚠️ 還沒測過的情境」
- 4 fix 歷程是本頁 hero 段落——用 `data-emph` 強調「4 個 fix / 1 場 demo」

**字數**：~550 行。

**Nav**：`BIM_MCP / reference / Troubleshooting`。Actions: `← Deployment` / `Contributor Template →`。

---

### 1.4 `reference/contributor-template.html`（🟢）

**目標**：升級自 0425 Block E 停車場範本，給「事務所要新增自己的法規 SOP」的人一個 step-by-step。

**結構章節**：
1. Hero：「貢獻新 SOP · 雙寫流程」`Domain 先 / Skill 後 / Tools 補`
2. Section 01：判斷你的需求是什麼（決策樹：純知識？要編排？要新 tool？）
   - 純法規條文 → 加 `domain/*.md`
   - 跨多 tool 編排 → 加 `.claude/skills/<name>/SKILL.md` + Domain
   - 沒有對應 tool → 先在 issue tracker 開 RFC，再考慮加 MCP tool
2. Section 02：Domain 寫作標準（連到 `domain/skill-authoring-standard.md` 與 `domain/frontmatter-standard.md`）
3. Section 03：Skill 寫作標準（YAML frontmatter + Markdown body 結構）
4. Section 04：⭐ **範例案例：停車場淨高檢查**（從零做到完成）
   - 步驟 1：建立 `domain/parking-clearance-check.md`（show 範本 frontmatter + body 大綱）
   - 步驟 2：（如需編排）建 `.claude/skills/parking-check/SKILL.md`
   - 步驟 3：寫測試（用 sample model 跑一遍）
   - 步驟 4：更新 CLAUDE.md 觸發表 + skill 表
   - 步驟 5：跑 `/claude-md-sync` 驗證雙向一致
   - 步驟 6：PR review 流程（CODEOWNERS）
5. Section 05：常見錯誤 & 學長提醒
   - ❌ 不要把 domain 升 Skill（除非有跨 tool 編排價值）
   - ❌ 不要把 SOP 寫進 Skill body（重複）
   - ❌ 不要省略 frontmatter（會被 qaqc Phase 6 攔下）
6. Section 06：Further reading

**資料來源**：
- `domain/skill-authoring-standard.md`
- `domain/frontmatter-standard.md`
- `.claude/skills/parking-check/SKILL.md`（作為範本）
- `domain/parking-clearance-check.md`（作為範本）

**特殊樣式**：
- 步驟卡用 numbered 樣式
- code block 用 `<pre><code>` 包 YAML / Markdown 片段
- 「常見錯誤」section 用 `pitfalls` 樣式

**字數**：~550 行。

**Nav**：`BIM_MCP / reference / Contributor Template`。Actions: `← Troubleshooting` / `回首頁`。

---

### 1.5 `docs/BIM_MCP/index.html`（站點入口）

**目標**：使用者打開 `docs/BIM_MCP/index.html` 看到的第一頁。月份時間軸 + reference 索引 + latest 入口。

**結構章節**：
1. Hero：「BIM_MCP · 月小聚知識站」`reference 永久層 + 月份檔案層 + 進行式`
2. Section 01：站點地圖（3 層架構視覺化—— reference / 2026-MM / hands-on-latest）
3. Section 02：reference 索引（8 個 reference 頁的卡片網格，含 1-line 描述 + 預估閱讀時間）
4. Section 03：月份檔案時間軸（2026-04 / 2026-05），每月份點進去看當月 presentation / hands-on
5. Section 04：hands-on-latest（指向最新月份的 hands-on 入口）
6. Section 05：給「第一次來的人」入口（建議閱讀順序：22 命題 → 三憲法 → 業界證據 → 光譜決策）
7. Footer：作者 / 授權 / commit sha

**資料來源**：自含。需列：
- 8 個 reference 頁：architecture-v2 / philosophy-22-propositions / three-constitutions / industry-evidence / spectrum-decision / skills-index / domain-index / deployment-guide / troubleshooting / contributor-template（10 個）
- 月份檔：`2026-04/karpathy-wiki.html`、`2026-05/presentation.html`（pending）、`2026-05/handson.html`（pending）、`2026-05/live-demo-log.html`（pending）

**特殊樣式**：
- Reference 索引用 grid-3（GENAI/web 風格的 hover scale + glow）
- 月份時間軸用同樣的 em-timeline 樣式（拷貝自 industry-evidence.html 內嵌 style）
- Hero 加 logo SVG（見 Part 3 配圖 #1）

**字數**：~400 行。

**Nav**：無需 breadcrumb（這就是首頁）——只放 logo + 主導航 actions（GitHub link / philosophy / contribute）。

---

<a id="part-2"></a>

## 3. Part 2 — GENAI/web 借鏡分析

> 來源：`C:/Users/Admin/Desktop/GENAI-main/GENAI-main/web/`（13 個 HTML 頁，11 張核心插畫）。
> 探察結果（已驗證）：

### 3.1 GENAI/web 的高品質做法

| 項目 | GENAI/web 做法 | BIM_MCP 目前狀態 | 該不該抄 |
|---|---|---|---|
| **drawer 互動** | Click `.tech-link-btn` → 右側 75vw drawer 滑入，含 overlay + Esc 關閉 + iframe postMessage | 沒有 drawer（內容全鋪在主頁） | ✅ **抄** — 22 命題用 drawer 揭露細節，主頁負擔降低 |
| **side TOC** | `position: fixed; right: 40px; top: 50%`，IntersectionObserver 50% threshold 自動更新 active | 有 `.bim-toc` 樣式（已定義在 styles.css 第 410 行）但**未啟用** | ✅ **抄** — 啟用既有樣式，加 IntersectionObserver |
| **hover micro** | 1.2-1.3× scale + 90° rotate + glow shadow | 已部分（`bim-card:hover translateY(-2px)` + `prop-badge:hover scale(1.08)`） | 🟡 **加強** — 給 hero stats card 加 hover scale；給 abcd-card hover glow |
| **scroll-snap** | `scroll-snap-type: y mandatory`（每節吸附） | 沒有 | 🟡 **選配** — 適用 index.html 站點入口，reference 頁不要（會妨礙閱讀） |
| **動態字型** | `Noto Sans TC + JetBrains Mono` | 已採用相同字型 | ✅ **已對齊** |
| **配圖系統** | 11 張核心插畫（左圖右文雙欄） | 0 張圖 | ⚠️ **缺口 — Part 3 補** |
| **easter egg / hidden** | `easter-egg.html` 含 QR Code、`mix-blend-mode: screen` 反相濾鏡 | 無 | ❌ **不抄** — 使用者明示「集中能量在正序內容」 |
| **studio.html 工作室** | 多步驟引導表單 | 無 | ❌ **不抄** — 同上 |
| **postMessage 父子通訊** | drawer iframe 用 postMessage 通知父頁關閉 | N/A | 🟡 **看情況** — 如果 drawer 不嵌 iframe 用不到 |

### 3.2 BIM_MCP 該補的 4 個互動升級

#### 升級 A：22 命題頁加 drawer
**現況**：philosophy-22-propositions.html 把每個 prop 的展開內容直接鋪在頁面（很長）。
**升級**：把每個 prop card 改成 clickable，click 後右側 75vw drawer 滑入顯示「使用者原文 + 違規範例 + 正確做法」。
**參考**：GENAI/web tech-link 的 drawer 實作（`.drawer.open` 切換）。
**JS 量**：~80 行 inline `<script>`。

#### 升級 B：所有 reference 頁啟用 side TOC（≥ 6 sections 的頁）
**現況**：`.bim-toc` 樣式已寫好但未啟用。
**升級**：在 industry-evidence / spectrum-decision / three-constitutions / philosophy-22-propositions / domain-index / skills-index / deployment-guide / troubleshooting 頁加 TOC sidebar，含 IntersectionObserver 自動 active。
**注意**：window ≥ 1000px 才顯示（小螢幕 hidden by `@media`，已在 styles.css 設好）。
**JS 量**：~30 行可重複 inline `<script>`，或抽成 `docs/BIM_MCP/_toc.js` 共用。

#### 升級 C：abcd-card / mega-stat / em-timeline 加 hover scale
**現況**：基本 hover 已有，但 mega-stat 沒 hover 反饋。
**升級**：mega-stat hover → `transform: scale(1.03)` + value 顏色加亮；timeline item hover → 左邊圓點 glow。
**改動位置**：industry-evidence.html / spectrum-decision.html 內嵌 `<style>` 追加 hover rule。

#### 升級 D：keyboard nav
**升級**：
- `Esc` 關閉 drawer（升級 A 已含）
- `←` / `→` 在 reference 頁切換上/下頁（footer nav 對應）
- `g` then `h` 跳回首頁（vim-style，選配）

**JS 量**：~40 行共用 `<script>`，可放 `_keynav.js`。

### 3.3 不抄的設計

1. **easter-egg.html / studio.html** — 與「集中正序內容」原則衝突
2. **scroll-snap on reference pages** — 干擾長文閱讀
3. **`mix-blend-mode: screen` 反相濾鏡** — 視覺效果太特殊，與本站「冷靜技術文件」氣質不合
4. **GENAI 紅色 accent (`#FF3333`)** — 本站延用藍色 (`#60a5fa`)，不換

---

<a id="part-3"></a>

## 4. Part 3 — 配圖 prompt 集 → 已移至 `_IMAGE_PLAN_CODEX.md` Part 7

> **2026-05-18 重構**：22 張 IMAGE 2 prompt（Jack Butcher / Visualize Value 風格）連同檔名、規格、Codex CLI 引用範本，全部統一在 **`_IMAGE_PLAN_CODEX.md` Part 7**。

### 配圖工作流（單一引用點）

1. **開 `docs/BIM_MCP/_IMAGE_PLAN_CODEX.md`**
2. Part 1（風格憲法）= 先決條件，6 條鐵律不可違反
3. Part 3（11 頁地圖）= 配圖位置與「為什麼放這裡」邏輯
4. **Part 7（22+3 張 prompt 集）= Codex CLI 直接引用**
   - 7.0 master 引用模板
   - 7.1 必備 5 張（Phase 1）
   - 7.2 應做 7 張（Phase 2）
   - 7.3 加分 13 張（Phase 3）
   - 7.4 攝影替代選項（hold）
   - 7.5 整批執行清單（依優先級）

### Codex CLI 單張範本

```bash
codex "讀 docs/BIM_MCP/_IMAGE_PLAN_CODEX.md 第 7 部分 #N 的規格與 prompt，用 IMAGE 2 生成 SVG 存到該 entry 指定的檔名。"
```

### 為什麼搬遷

- 本檔（`_HANDOFF_CODEX.md`）保留**頁面建構** + **互動升級**範疇
- 配圖獨立到 `_IMAGE_PLAN_CODEX.md`，Codex 處理圖片時只需開一份檔
- 風格憲法 + 置放邏輯 + prompt 在同一檔，避免跨檔對照
---

<a id="part-4"></a>

## 5. Part 4 — Drawer / 互動 / 配圖整合到既有 6 頁的計畫

### 5.1 整合動作清單

| 動作 | 目標頁 | 改動範圍 |
|---|---|---|
| 加 side TOC + IntersectionObserver | 既有 6 頁 + 新 5 頁（共 11 頁） | 每頁 `<body>` 結尾加 `<aside class="bim-toc">` + `<script>` |
| 在 philosophy-22-propositions 加 drawer | 1 頁 | 把每個 prop card 改 clickable + 加 drawer HTML + drawer JS |
| 在 hero 區塊嵌入配圖 | 既有 6 頁 + 新 5 頁 | 每頁 `<header class="bim-hero">` 內加 `<img>` 或 `<svg>` 背景 |
| 在 section 開頭嵌配圖 | industry / spectrum / philosophy / three-constitutions | section 第一個 `<h2>` 前加 `<figure>` |
| 加 keyboard navigation | 全站 | 共用 `_keynav.js` 引用 |

### 5.2 共用 JS 抽出（建議）

建立 `docs/BIM_MCP/_shared.js`，內含：
- `initTOC()` — 掃描 `.bim-anchor` 自動生成 TOC + IntersectionObserver
- `initDrawer()` — 通用 drawer 開關（給任何頁面用）
- `initKeyNav()` — Esc / ←→ / g+h

每頁底部加：
```html
<script src="/docs/BIM_MCP/_shared.js"></script>
<script>initTOC(); initKeyNav(); /* initDrawer(); 只在 philosophy 頁 */</script>
```

### 5.3 圖檔放置與引用

**目錄**：`docs/BIM_MCP/_images/`
**命名**：`<page>__<position>__<concept>.svg|webp`
**引用**：相對路徑——reference/ 頁面用 `<img src="../_images/...">`

---

<a id="part-5"></a>

## 6. Part 5 — 驗收清單

### 6.1 5 頁建構驗收

- [ ] `domain-index.html` 35+ domain 全部列出，每個有對應 Skill 反查連結（skills-index 已存在的 `domain-link href` 對應得上）
- [ ] `deployment-guide.html` 含 Nice3point + Release.R{YY} + setup.ps1 完整流程
- [ ] `troubleshooting.html` 含 5 經典 + 4 新案，4 新案資料來自 commit `4975ceb`
- [ ] `contributor-template.html` 含 6 步驟範例（停車場淨高為主軸）
- [ ] `index.html` 含 3 層架構視覺 + 8 reference 卡片 + 月份時間軸

### 6.2 樣式驗收

- [ ] 不修改 `styles.css`（共用層）
- [ ] 每頁有 `<link rel="stylesheet" href="../styles.css">`（index.html 是 `styles.css`）
- [ ] 每頁 `<head>` 含 JetBrains Mono + Noto Sans TC 字型載入
- [ ] 每頁 nav 有 breadcrumb 三層 + 右側 actions
- [ ] 每頁 footer 有上/下/首頁三按鈕 + 「最後更新」日期

### 6.3 互動驗收

- [ ] philosophy 頁 drawer 點 prop card 能滑出（含 Esc 關閉）
- [ ] 所有 ≥ 6 sections 頁面 side TOC 在 ≥ 1000px 視窗顯示且 active 自動切換
- [ ] hover 微互動：abcd-card / mega-stat / em-timeline item 都有反饋
- [ ] keyboard ←/→ 在 reference 頁切換上/下頁（順序：philosophy → three-const → industry → spectrum → skills → domain → deployment → troubleshooting → contributor）

### 6.4 配圖驗收

- [ ] 必做 5 張圖（#1/3/4/8/11）產出並嵌入對應 hero
- [ ] 圖檔放在 `docs/BIM_MCP/_images/`，命名規則一致
- [ ] 每張圖加 `alt` 屬性（無障礙）
- [ ] SVG 圖嵌入時用 `<img>` 或 inline `<svg>`，不要 background-image（會吃 print 模式）

### 6.5 內容誠實度驗收（Tool Call Data Honesty）

- [ ] 所有具體數字（19 / 35+ / 92 / 100× / 70% / $15-25K / Boehm 1981）都對應 CLAUDE.md 或外部 paper
- [ ] 不編造 commit sha / 檔案路徑 / Skill 名稱
- [ ] 4 個 fix 案例的 root cause 來自 `git show 4975ceb` 實際 diff，不杜撰

---

## Appendix A — 已存在但可重用的 components

從 styles.css 已定義（直接 class 用）：

| Class | 用途 |
|---|---|
| `.bim-topnav` + `.bim-topnav-left/btn` | 頂部導航 |
| `.bim-hero` + `.bim-hero-eyebrow/title/sub` | Hero 區塊 |
| `.bim-section` + `.bim-section-eyebrow/title/desc` | 主要 section |
| `.bim-grid` + `.bim-grid-{2,3,4}` | 等寬網格 |
| `.bim-card` + `.bim-card-{accent,warn,ok,bad}` | 卡片含色階變體 |
| `.bim-anchor` | 用於 IntersectionObserver 偵測的錨點 |
| `.prop-badge` + `.large/muted` | P1-P22 標籤 |
| `.pass-tag` / `.fail-tag` | 通過/未通過標籤 |
| `.user-quote` + `.user-quote-attr` | 使用者引述卡 |
| `.data-emph` + `.data-emph-label` | 大字數據 |
| `.hero-stats` + `.hero-stat` | 三欄統計 |
| `.bim-divider` | 章節間分隔線 |
| `.bim-toc` | Side TOC（已寫樣式，待啟用） |
| `.bim-footer` + `.bim-footer-actions/btn` | 底部 |
| `.how-tech` + `.how-tech-label/content` | WHY/HOW/TECHNIQUE/CHECKLIST 4 欄 box |
| `.empathy-quote` + label/text/context | 共感句 |
| `.further-reading` + label/list/fr-type/fr-desc | 進階閱讀 |
| `.pitfalls` + `.pitfalls-title` | 警告框 |
| `.lingo` | 行話標籤 |
| `.mono` | 等寬字 inline |

---

## Appendix B — 既有頁面的內嵌 style 已新增的特化 class（可參考）

來自 `industry-evidence.html`：
- `.em-timeline` / `.em-timeline-item/date/title/desc` / `.em-timeline-item.future`
- `.mega-stat` / `.mega-stat-value/unit/claim`

來自 `spectrum-decision.html`：
- `.abcd-grid` / `.abcd-card` / `.abcd-card.option-d` / `.abcd-letter` / `.abcd-title/desc/cost`
- `.vs-table` / `.vs-side.left/right` / `.vs-divider`
- `.hero-quote` / `.hero-quote-text/attr`

來自 `skills-index.html`：
- `.skill-card` / `.skill-name/cat/desc/meta`
- `.trigger-tag` / `.domain-link`
- `.cat-header`

新頁面寫類似元件時可以**抄這些定義**，但**不要重新發明命名**。

---

## Appendix C — 5/23 月小聚場景使用順序（demo flow）

Codex 完工後，5/23 demo 預計這樣用：

1. 開場放 `index.html`，講三層架構（reference/月份/latest）
2. 進 `reference/philosophy-22-propositions.html` 講 22 命題核心（用 drawer 揭露細節）
3. 進 `reference/three-constitutions.html` 講三憲法（重點 P4 + Tool Call Data Honesty + Domain Method Compliance）
4. 進 `reference/industry-evidence.html` 講 100×/70%/$15-25K + Early Mover
5. 進 `reference/spectrum-decision.html` 講 A/B/C/D，套到日本 sample 模型 FAIL 案例
6. 結束指向 `2026-05/handson.html`（活體練習頁——已存在 `docs/0523-handson.html`，待搬）
7. 後續觀眾可自行翻 skills/domain/deployment/troubleshooting

**所以新 5 頁的 5/23 demo 重要性**：
- index.html ⭐⭐⭐（場景必要）
- domain-index.html ⭐（觀眾事後翻）
- deployment / troubleshooting / contributor ⭐（觀眾事後翻）

但**配圖** + **drawer 互動**對 demo 視覺感染力影響最大——這是 Codex 應優先處理的。

---

**END OF HANDOFF**

如有疑問參考：
- `CLAUDE.md`（專案憲法 + Skill / Domain 對應表）
- `log/2026-05.md`（5 月事件日誌）
- `C:\Users\Admin\.claude\plans\brench-a-merge-floofy-owl.md`（完整原版規劃）
- 已存在 6 頁的原始碼（直接模仿結構）
