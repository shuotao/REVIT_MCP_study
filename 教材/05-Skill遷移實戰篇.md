# 第五堂：Skill 遷移實戰篇

## 把 `domain/` 升級為 Agent Skill 架構

---

> **本堂目標**：
> 1. 理解 Agent Skill 的核心概念與運作機制
> 2. 親手把現有的 `domain/` 知識庫升級為標準 SKILL.md 格式
> 3. 修改斜線指令，讓新舊流程無痛銜接

---

> **預計時數**：3 小時
>
> **前提條件**：已完成第四堂實務案例，熟悉 `domain/` 目錄中的 SOP 文件

---

## 5.1 為什麼要升級？

你在前幾堂課中已經學會了用 `/domain` 指令把成功的工作經驗記錄到 `domain/*.md` 檔案中。
這個做法很好，但隨著知識越來越多，你會開始遇到三個問題：

### 問題一：觸發規則要手動維護

```
目前的架構：

┌─────────────── GEMINI.md ──────────────────────────────┐
│                                                         │
│  觸發規則表（你寫的）：                                  │
│  「防火」→ domain/fire-rating-check.md                   │
│  「走廊」→ domain/corridor-analysis-protocol.md          │
│  「容積」→ domain/floor-area-review.md                   │
│  「上色」→ domain/element-coloring-workflow.md            │
│  ...每新增一個 SOP，就要手動加一行                        │
│                                                         │
└─────────────────────────────────────────────────────────┘
```

每次建立新的 `domain/*.md`，你都要**手動**回到 GEMINI.md 更新觸發規則表。
忘了更新，AI 就不知道新的 SOP 存在。

### 問題二：知識散落各處

如果有多位使用者在不同時間點加入專案：

| 使用者 | 加入時間 | 知識存在位置 |
|:------|:--------|:-----------|
| A（元老） | 早期 | GEMINI.md 末尾 + domain/ |
| B（中途） | 中期 | domain/ 裡一半、GEMINI.md 裡一半 |
| C（新人） | 最近 | 完全不知道 domain/ 有什麼 |

→ **沒有「單一事實來源 (Single Source of Truth)」**

### 問題三：AI 載入效率低

目前的做法是把觸發規則寫在 GEMINI.md 裡，AI 每次啟動都要讀整份 GEMINI.md。
當規則越來越多，GEMINI.md 越來越胖，**與專案無關的知識也一起被載入了**。

### Skill 如何解決這些問題？

```
升級後的架構（Agent Skill）：

┌─────────────── AI 啟動時 ────────────────────────┐
│                                                   │
│  只載入 Skill 的 metadata（~50 tokens）：          │
│  「revit-code-reviewer: 針對 Revit 模型執行        │
│   台灣建築法規合規檢討，涵蓋防火、走廊、容積...」   │
│                                                   │
│  → AI 自動判斷什麼時候該用它                       │
│  → 不需要手動維護觸發規則表                        │
│  → 被觸發時才載入完整內容                          │
│                                                   │
└─────────────────────────────────────────────────────┘
```

---

## 5.2 核心概念：Progressive Disclosure（漸進式揭露）

Skill 用三層結構來管理知識，就像建築事務所請顧問一樣：

```
Level 1 — Metadata（永遠在場）
  ≈ 顧問名冊上的一行字：「消防顧問王先生，專長是防火區劃」
  → 只佔 ~50 tokens，不影響效能
  → AI 看到這行字就知道「哦，我有消防顧問可以諮詢」

    ↓ 使用者問到防火相關問題時

Level 2 — SKILL.md 本文（按需載入）
  ≈ 消防顧問帶著他的 SOP 手冊進場
  → 載入完整的檢討流程（< 500 行）
  → AI 按照 SOP 執行

    ↓ 需要查法規條文時

Level 3 — references/（按需載入）
  ≈ 消防顧問翻開法規條文冊
  → 載入詳細的參考資料
  → 例如：建築技術規則的具體條文
```

> 💡 **建築人比喻**：
> 你的事務所不會讓消防顧問、綠建築顧問、機電顧問三位**同時坐在辦公室裡**等電話——
> 你只會在名冊上記錄他們的專長，有需要時才請他們帶資料進場。
> Skill 就是這個邏輯。

---

## 5.3 SKILL.md 的格式規範

一份 SKILL.md 由兩部分組成：

```markdown
---                                    ← YAML Frontmatter（Level 1 Metadata）
name: revit-code-reviewer              ← 必填：小寫 + 連字號
description: >                         ← 必填：< 1024 字元
  針對 Revit 模型執行台灣建築法規
  合規檢討...
---                                    ← Frontmatter 結束

# 以下是 Level 2 本文              ← 被觸發時才載入
                                       ← 目標 < 500 行
## 流程 A：容積檢討
...
## 流程 B：防火等級檢查
...
```

### 目錄結構

```
revit-code-reviewer/            ← Skill 根目錄
├── SKILL.md                    ← 核心指令（必要）
└── references/                 ← 參考資料（選用，Level 3）
    └── building-code-tw.md     ← 法規摘要
```

> 💡 **重點**：`name` 和 `description` 是最關鍵的兩個欄位。
> AI 就是靠 `description` 來判斷「什麼時候該載入這個 Skill」。
> 所以 description 要包含**所有可能的觸發關鍵字**（防火、走廊、容積...）。

---

## 5.4 盤點：你現在手上有什麼？

在動手之前，先清點你的「知識資產」。

### ✏️ 練習 1：閱讀現有架構（15 分鐘）

請打開以下檔案，逐一閱讀並回答問題：

#### 任務 A：閱讀 `domain/` 目錄

```bash
ls domain/
```

你應該會看到這些檔案：

| 檔案 | 內容主題 |
|:----|:--------|
| `fire-rating-check.md` | 防火等級檢查 SOP |
| `corridor-analysis-protocol.md` | 走廊分析 SOP |
| `floor-area-review.md` | 容積檢討 SOP |
| `element-coloring-workflow.md` | 元素上色 SOP |
| `room-boundary.md` | 房間邊界計算 |
| `wall-check.md` | 牆壁檢查機制 |
| `qa-checklist.md` | 品質檢查清單 |
| `path-maintenance-qa.md` | 路徑維護 QA |
| `references/building-code-tw.md` | 台灣建築法規摘要 |

> 📝 **思考題** ：這些檔案中，有幾個有 YAML frontmatter（檔案開頭的 `---` 區塊）？
> 它們的 `description` 欄位寫了什麼？

#### 任務 B：閱讀 GEMINI.md 的斜線指令

打開 `GEMINI.md`，找到「🧠 AI 協作指令」段落，回答：

1. 目前定義了哪幾個斜線命令？
2. `/domain` 指令的輸出目的地是什麼？
3. 「工作流程觸發規則」表中有幾條規則？

#### 任務 C：找出問題

> 📝 **思考題**：假設你剛完成了一個「停車位淨高檢查」的工作流程，
> 你用 `/domain` 把它儲存到了 `domain/parking-clearance-check.md`。
> 接下來你還需要做什麼，AI 才能在下次自動使用這個 SOP？
>
> （答案：你必須回到 GEMINI.md，在觸發規則表中手動新增一行。）

---

## 5.5 動手建立 Skill 骨架

### ✏️ 練習 2：建立目錄與 SKILL.md（20 分鐘）

#### 步驟 1：建立目錄結構

在專案根目錄下，手動建立以下結構：

```bash
mkdir -p .agent/skills/revit-code-reviewer/references
```

> 💡 **為什麼放在 `.agent/skills/`？**
> 這是 Gemini CLI / Antigravity 的專案級 Skill 標準路徑。
> Claude Code 則使用 `.claude/skills/`。放在 `.agent/skills/` 可以讓兩邊都能找到。

#### 步驟 2：複製法規參考資料

```bash
cp domain/references/building-code-tw.md .agent/skills/revit-code-reviewer/references/
```

#### 步驟 3：建立 SKILL.md 骨架

用文字編輯器建立 `.agent/skills/revit-code-reviewer/SKILL.md`，先寫入以下模板：

```yaml
---
name: revit-code-reviewer
description: >
  （你的任務：在這裡填寫 description）
---
```

#### 步驟 4：撰寫 description

這是整個練習中**最重要的一步**。`description` 決定了 AI 什麼時候會載入這個 Skill。

**撰寫原則**：

| 原則 | 說明 | 範例 |
|:----|:----|:----|
| 包含所有觸發關鍵字 | 把你希望觸發此 Skill 的詞彙都寫進去 | 防火、耐燃、走廊、容積 |
| 說明用途 | AI 要能判斷「這個 Skill 是做什麼的」 | 針對 Revit 模型執行法規合規檢討 |
| 列出涵蓋範圍 | AI 要知道這個 Skill 能處理哪些場景 | 容積率、防火等級、走廊寬度... |
| 壓在 1024 字元以內 | 規範限制 | — |

> 📝 **你的任務**：參考 `domain/` 目錄中所有 SOP 的主題，
> 寫出一段 description，涵蓋你希望 AI 自動觸發此 Skill 的所有場景。
>
> 💡 **提示**：回想 GEMINI.md 中的觸發規則表，
> 把那些關鍵字（容積、防火、走廊、上色、外牆開口、QA...）都融入 description 中。

完成後，你的 SKILL.md 應該看起來像這樣：

```yaml
---
name: revit-code-reviewer
description: >
  （你寫的 description，包含所有觸發關鍵字和功能描述）
---

# Revit 建築法規合規檢討

（本文待下一個練習填入）
```

#### 自我檢查

- [ ] `.agent/skills/revit-code-reviewer/SKILL.md` 檔案存在
- [ ] `.agent/skills/revit-code-reviewer/references/building-code-tw.md` 檔案存在
- [ ] YAML frontmatter 的 `name` 是小寫加連字號
- [ ] `description` 包含至少 5 個觸發關鍵字
- [ ] `description` 不超過 1024 字元

---

## 5.6 合併 domain/ 內容到 SKILL.md

### ✏️ 練習 3：精簡合併（30 分鐘）

這是最有挑戰性的一步。你需要把 `domain/` 中的多份 SOP **精簡整合**到一份 SKILL.md 中。

#### 合併原則

| 原則 | 做法 | 為什麼 |
|:----|:----|:------|
| **SKILL.md < 500 行** | 只保留「步驟摘要」，細節搬到 references/ | 載入速度快，AI 不會被資訊淹沒 |
| **保留步驟結構** | 每個 SOP 保留：步驟編號 + 工具名稱 + 關鍵參數 | AI 需要知道「用什麼工具、按什麼順序」 |
| **刪除冗餘說明** | 移除「前置條件」「情境說明」等通用文字 | 這些資訊 CLAUDE.md / GEMINI.md 已經有了 |
| **法規數據搬家** | 具體的法規數值表格搬到 references/ | SKILL.md 只說「參考 references/」 |

#### 實際操作

從以下 3 個 SOP 開始練習（你可以之後再加入其他的）：

**第 1 個：防火等級檢查**

打開 `domain/fire-rating-check.md`，將其精簡為以下格式後，貼入 SKILL.md：

```markdown
## 流程 A：防火等級檢查

觸發詞：防火、耐燃、消防、防火時效

### 步驟
1. `get_all_levels` → 取得樓層清單
2. `query_elements` category=Wall → 篩選牆體
3. `get_element_info` → 取得防火時效參數
4. 比對法規標準（參考 `references/building-code-tw.md`）
5. `override_element_color` → 不合格標紅、合格標綠
6. 產生檢查報告

### 注意事項
- 防火時效參數名稱可能因族群不同而異
- 先確認外牆 vs 內牆的不同標準
```

> 📝 **你的任務**：比較上面的精簡版和原始的 `domain/fire-rating-check.md`，
> 注意哪些內容被保留、哪些被移除了。

**第 2 個：走廊分析**

打開 `domain/corridor-analysis-protocol.md`，同樣精簡：

```markdown
## 流程 B：走廊寬度分析

觸發詞：走廊、逃生、通道寬度、避難路徑

### 步驟
1. `get_rooms_by_level` → 篩選名稱含「走廊/Corridor/廊道/通道/廊下」的房間
2. 取得房間 BoundingBox → 計算估計寬度
3. 比對法規：一般走廊 ≥ 1.2m，兩側居室走廊 ≥ 1.6m
4. `create_dimension` → 在 BoundingBox 中心線建立標註
5. `override_element_color` → 不合格標紅

### 注意事項
- 走廊識別需包含多語言：走廊、Corridor、廊道、通道、廊下
- 標註必須建在平面視圖 (FloorPlan)，不可在 3D 視圖建立
- 座標軸方向影響標註線定義方式
```

**第 3 個：容積檢討**

> 📝 **你的任務**：自己閱讀 `domain/floor-area-review.md`，
> 按照相同的精簡原則，寫出「流程 C：容積檢討」的精簡版。
>
> 完成後對照以下檢查項目：
> - [ ] 有「觸發詞」列表
> - [ ] 步驟中有標明工具名稱
> - [ ] 法規數值沒有直接寫在 SKILL.md 中（應指向 references/）
> - [ ] 包含至少一條「注意事項」

#### 合併更多 SOP（選做）

如果時間允許，繼續把以下 SOP 也精簡合併進 SKILL.md：

| 優先序 | domain/ 檔案 | 建議的流程標題 |
|:-----:|:------------|:-------------|
| 1 | `element-coloring-workflow.md` | 流程 D：元素上色與視覺化 |
| 2 | `exterior-wall-opening-check.md` | 流程 E：外牆開口檢討 |
| 3 | `room-boundary.md` | 流程 F：房間邊界計算 |
| 4 | `wall-check.md` | 流程 G：牆壁方向檢查 |
| 5 | `qa-checklist.md` | 流程 H：品質檢查清單 |

#### 自我檢查

- [ ] SKILL.md 包含至少 3 個流程（A、B、C）
- [ ] SKILL.md 總行數 < 500 行
- [ ] 每個流程都有「觸發詞」和「步驟」
- [ ] 具體法規數值在 `references/` 中，SKILL.md 只引用

---

## 5.7 重新導向斜線指令

### ✏️ 練習 4：修改 GEMINI.md（15 分鐘）

現在 Skill 已經建好了，但 GEMINI.md 裡的斜線指令還是指向舊的 `domain/`。
我們需要**重新導向**，讓使用者的操作習慣完全不變。

#### 修改原則

> **不要改變使用者的操作習慣，只改變背後的輸出目的地。**

#### 任務 A：修改 `/domain` 指令

找到 GEMINI.md 中 `/domain` 的定義，把輸出目的地從 `domain/` 改為 `revit-code-reviewer/`：

**修改前**：
```
/domain → 將工作流程轉換為 domain/*.md 檔案
```

**修改後**：
```
/domain → 將工作流程更新到 revit-code-reviewer Skill 中
         (1) 確認對象
         (2) 提取工具和步驟
         (3) 判斷更新位置：
             - 新流程 → 追加到 SKILL.md 的對應章節
             - 新法規 → 更新 references/building-code-tw.md
             - 修正既有流程 → 直接編輯 SKILL.md
         (4) 儲存至 .agent/skills/revit-code-reviewer/
```

#### 任務 B：修改 `/lessons` 指令

**修改前**：
```
/lessons → 追加到 GEMINI.md 末尾
```

**修改後**：
```
/lessons → 追加到 SKILL.md 的「注意事項」段落
```

#### 任務 C：修改 `/review` 指令

**修改前**：
```
/review → 檢查 GEMINI.md 是否超過 100 行
```

**修改後**：
```
/review → (1) 檢查 SKILL.md 是否超過 500 行（過長則拆到 references/）
          (2) 檢查 GEMINI.md 是否超過 100 行（維持原有規則）
```

#### 任務 D：`/explain` — 不動

`/explain` 是通用的輸出格式指令，跟 Skill 架構無關，維持現狀。

#### 任務 E：處理觸發規則表

找到 GEMINI.md 中的「🔄 工作流程觸發規則」表格。

由於 Skill 的 `description` 已經包含了所有觸發關鍵字，
AI 會自動判斷何時載入 Skill，**不再需要手動維護這張表**。

你有兩個選擇：

| 選項 | 做法 | 優點 | 缺點 |
|:---:|:-----|:----|:----|
| A | 刪除整張觸發表 | 乾淨，避免與 Skill 衝突 | 如果 description 寫漏，就沒有備援 |
| B | 保留但加註說明 | 有備援 | 「雙軌維護」風險 |

> 📝 **建議選 A**（刪除），但在表格位置留下一行說明：
> ```
> > 觸發規則已由 revit-code-reviewer Skill 的 description 自動處理，
> > 不再需要手動維護。
> ```

#### 任務 F：修改自動預檢行為

找到「核心行為義務 (不需要指令即可觸發)」段落，把：

```
自動預檢：檢索 domain/、scripts/ 以及 GEMINI.md
```

改為：

```
自動預檢：載入對應的 Skill（SKILL.md），並檢索 scripts/ 以及 GEMINI.md
```

#### 自我檢查

- [ ] `/domain` 的輸出目的地已改為 `revit-code-reviewer/`
- [ ] `/lessons` 的輸出目的地已改為 SKILL.md
- [ ] `/review` 的審計範圍包含 SKILL.md
- [ ] `/explain` 沒有被修改
- [ ] 觸發規則表已處理（刪除或加註）
- [ ] 自動預檢行為已更新

---

## 5.8 歸檔與驗證

### ✏️ 練習 5：收尾工作（10 分鐘）

#### 任務 A：更新 domain/README.md

打開 `domain/README.md`，在最上方加入歸檔說明：

```markdown
# ⚠️ 本目錄已遷移至 Skill

本目錄中的知識文件已整合到 `revit-code-reviewer` Skill 中。

- **查看最新版本**：`.agent/skills/revit-code-reviewer/SKILL.md`
- **新增經驗**：使用 `/domain` 指令（已自動導向 Skill）
- **本目錄保留為歷史存檔**，不再更新

---

（以下為原始內容，僅供參考）
```

> 💡 **重要**：不要刪除 `domain/` 目錄！
> 其他 fork 的使用者可能還在引用這些檔案。保留作為歷史存檔。

#### 任務 B：驗證清單

完成以上所有練習後，用以下清單確認一切就緒：

**檔案結構檢查**：

```bash
# 確認 Skill 目錄結構
ls -la .agent/skills/revit-code-reviewer/
# 預期看到：SKILL.md, references/

ls -la .agent/skills/revit-code-reviewer/references/
# 預期看到：building-code-tw.md
```

**內容檢查**：

| 檢查項目 | 預期結果 | ✓ |
|:--------|:--------|:-:|
| SKILL.md 有 YAML frontmatter | `name` + `description` 存在 | |
| SKILL.md 行數 < 500 | 精簡但完整 | |
| description 包含觸發關鍵字 | 防火、走廊、容積、上色... | |
| references/ 有法規檔案 | building-code-tw.md | |
| GEMINI.md 的 `/domain` 指向 Skill | 不再指向 domain/ | |
| GEMINI.md 觸發表已處理 | 已刪除或加註 | |
| domain/README.md 有歸檔說明 | 引導使用者去看 SKILL.md | |

#### 任務 C：功能測試（如有 AI 環境）

如果你的環境已經設定好 AI 助手，可以進行以下測試：

```
測試 1：向 AI 說「幫我檢查走廊寬度」
→ 預期：AI 載入 Skill，按流程 B 執行

測試 2：向 AI 說「幫我做防火等級檢查」
→ 預期：AI 載入 Skill，按流程 A 執行

測試 3：使用 /domain（描述一段新經驗）
→ 預期：AI 更新 SKILL.md，而非建立新的 domain/*.md
```

---

## 5.9 升級前後對照

完成所有練習後，回顧整個變化：

```
升級前                              升級後
──────                              ──────
domain/                             .agent/skills/revit-code-reviewer/
├── fire-rating-check.md            ├── SKILL.md（整合所有 SOP）
├── corridor-analysis-protocol.md   └── references/
├── floor-area-review.md                └── building-code-tw.md
├── element-coloring-workflow.md
├── ...                             domain/（保留為歷史存檔）
└── references/                     └── README.md（標記已遷移）
    └── building-code-tw.md

GEMINI.md                           GEMINI.md
├── /domain → domain/*.md           ├── /domain → SKILL.md ✅
├── /lessons → GEMINI.md 末尾       ├── /lessons → SKILL.md ✅
├── /review → 檢查 GEMINI.md        ├── /review → 檢查 SKILL.md ✅
├── /explain → 不變                 ├── /explain → 不變 ✅
└── 觸發規則表（6 條手動維護）       └── （由 description 自動觸發）✅
```

### 你得到了什麼？

| 改善項目 | 之前 | 之後 |
|:--------|:-----|:----|
| 新 SOP 的觸發 | 手動更新觸發表 | description 自動處理 |
| 知識來源 | 散落 GEMINI.md + domain/ | 集中在 SKILL.md |
| AI 載入效率 | 每次讀整份 GEMINI.md | 只載入 50 tokens metadata |
| 操作習慣 | `/domain`、`/lessons`... | **完全不變** ✅ |
| 跨 AI 平台 | 只有配了 GEMINI.md 的才能用 | Claude / Gemini / Copilot 通用 |

---

## 📎 附錄 A：YAML Frontmatter 欄位速查

| 欄位 | 必填 | 格式 | 說明 |
|:----|:---:|:-----|:----|
| `name` | ✅ | 小寫 + 連字號，1-64 字元 | Skill 的唯一識別名稱 |
| `description` | ✅ | 字串，< 1024 字元 | AI 用來判斷何時載入此 Skill |
| `license` | ❌ | SPDX 識別碼 | 例如 `MIT`、`Apache-2.0` |
| `compatibility` | ❌ | 清單 | 支援的 AI Client 列表 |
| `metadata` | ❌ | 鍵值對 | 自訂 metadata |
| `allowed-tools` | ❌ | 清單 | 限制 Skill 可使用的工具 |

## 📎 附錄 B：常見問題

**Q：我可以建立多個 Skill 嗎？**

可以。例如你可以把「法規檢討」和「模型品質檢查」分成兩個 Skill：

```
.agent/skills/
├── revit-code-reviewer/       ← 法規合規
│   ├── SKILL.md
│   └── references/
└── revit-model-qa/            ← 模型品質
    ├── SKILL.md
    └── references/
```

**Q：SKILL.md 超過 500 行怎麼辦？**

把詳細內容搬到 `references/` 目錄，在 SKILL.md 中寫 `參考 references/xxx.md`。
AI 在需要時會自動去讀取 references/ 中的檔案。

**Q：Skill 支援哪些 AI 平台？**

| 平台 | Skill 路徑 | 支援狀態 |
|:----|:----------|:--------|
| Gemini CLI / Antigravity | `.agent/skills/` | ✅ |
| Claude Code | `.claude/skills/` 或透過 Plugin Manager | ✅ |
| VS Code Copilot | 讀取 `.agent/skills/` | ✅ |

**Q：我練習做壞了怎麼辦？**

```bash
# 回到上一個 commit 的狀態（丟棄所有修改）
git checkout -- .

# 或者只恢復特定檔案
git checkout -- GEMINI.md
git checkout -- domain/README.md
```

> 💡 **這就是 Git 的好處**：你可以放心練習，隨時恢復原狀。

## 📎 附錄 C：Google Antigravity 部署路徑

如果你使用 Google Antigravity IDE，Skill 有兩個放置層級：

| 範圍 | 路徑 | 用途 |
|:----|:----|:----|
| **全域**（所有專案共用） | `~/.gemini/antigravity/skills/` | 放通用工具，如 `skill-creator` |
| **專案專屬** | `<專案目錄>/.agent/skills/` | 放專案知識，如 `revit-code-reviewer` |

設定步驟：
1. 確認 `~/.gemini/antigravity/skills/` 目錄存在（沒有就建立）
2. 把你的專案 Skill 放在 `.agent/skills/` 底下
3. 重新啟動 Antigravity 視窗

---

> **本堂結束** 🎉
>
> 你已經完成了從 `domain/` 到 Agent Skill 的完整遷移。
> 下一堂課將探討如何在團隊中共享 Skill、版本管理，以及未來的發展方向。
