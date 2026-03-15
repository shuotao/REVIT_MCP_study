# Claude Code Skill.md 開發學習筆記

## 第一章：核心概念 (Core Concepts)

### 1.1 什麼是 Claude Code Skill？—— 以及它與 System Prompt 的關係

#### 1.1.1 先搞清楚位階：System Prompt 是「憲法」

在討論 Skill 之前，我們必須先理解 **System Prompt 在 Claude 架構中的位階**。

根據 Anthropic 官方的 **Principal Hierarchy（委託人層級）**，Claude 的行為受到三層委託人的治理，**信任度由高至低**為：

1. **Anthropic（制定者）**：最高權限。透過訓練過程、Constitutional AI 和 Soul Spec 植入 Claude 的核心價值觀與安全機制。這是**不可覆寫的底層邏輯**。
2. **Operator（營運者）**：中層權限。透過 **System Prompt** 定義 Claude 在特定應用場景中的角色、行為邊界和指令。
3. **User（使用者）**：最低權限。透過對話中的訊息 (User Message) 與 Claude 互動。

**System Prompt 就是 Operator 層級的核心載體**。它是 Claude 模型在每一輪對話啟動時**最先讀取、最高優先級**的指令集。用法律體系來比喻：

| 層級      | 法律類比              | Claude 中的對應                          | 特性                     |
| --------- | --------------------- | ---------------------------------------- | ------------------------ |
| Anthropic | **憲法**              | Constitutional AI / Soul Spec / 訓練數據 | 不可修改，內建於模型     |
| Operator  | **立法（法律/命令）** | **System Prompt**                        | 可配置，但不得違反憲法   |
| User      | **人民請願/陳情**     | User Message（對話輸入）                 | 可被尊重，但不得超越法律 |

> **⚠️ 關鍵認知：System Prompt 是「憲法級以下、法律級以上」的存在。** 它不是一條隨便的指令，而是整個會話的**治理框架**。所有在 System Prompt 中設定的規則，對 Skill、Tool、User Message 都有約束力。

#### 1.1.2 Skill 是什麼？—— 在 System Prompt 框架下運作的「模組化知識包」

理解了位階之後，我們就能精確定義 Skill：

**Skill 是一組指令 (Instructions)、腳本 (Scripts)、參考資料 (References) 和資源 (Assets) 的集合**，旨在擴展 Claude 的能力以處理特定的、重複性的複雜任務。一個 Skill 通常包含：

- **SKILL.md**: Skill 的核心指令文件（含 YAML frontmatter 元數據）。
- **scripts/**: 執行特定動作的腳本（Python, Shell, Node.js 等）。
- **references/**: 相關的參考文件、模板或案例。
- **assets/**: 輸出模版、圖片、字體等資源。

但這裡的**關鍵要點**是：**Skill 不是 System Prompt 的替代品，而是被 System Prompt 管理的「被載入內容」**。

##### ⚠️ 重要辨析：CLAUDE.md ≠ System Prompt

在 Claude Code 的語境下，很多人會把 `CLAUDE.md` 和 System Prompt 畫等號。嚴格來說，這是不精確的：

|                | System Prompt（API 層面）                              | CLAUDE.md（Claude Code 層面）  |
| -------------- | ------------------------------------------------------ | ------------------------------ |
| **本質**       | API 呼叫中的 `system` 參數，是**最終組裝的完整指令集** | 一個位在專案根目錄的**設定檔** |
| **誰寫的**     | 由 Claude Code 程式自動組裝                            | **你**（專案使用者/開發者）    |
| **你能改嗎？** | 不能直接改（內建部分由 Anthropic 控制）                | ✅ 可以完全自訂                 |
| **關係**       | 是最終的「容器」                                       | 是容器中的**一塊拼圖**         |

Claude Code 啟動時，**真正的 System Prompt** 是由多個來源**自動組裝**而成的：

```
Claude Code 的實際 System Prompt（組裝後的完整版）
│
├── ① Claude Code 內建的核心指令                    ← 你改不了（Anthropic 寫的）
│     （身份定義、工具使用規則、安全機制…）
│
├── ② CLAUDE.md 的內容                              ← 你能自訂 ✏️
│     （專案特定的角色、規則、架構說明…）
│
├── ③ 已安裝 Skill 的 Metadata                      ← 自動注入
│     （每個 Skill 的 name + description，各 ~50 tokens）
│
└── ④ User Rules / Memory                           ← 你能自訂 ✏️
      （使用者偏好、語言設定等）
```

**所以更精確的說法是：CLAUDE.md 是 System Prompt 的「可配置區段」——你能自訂的那一塊。** 而 Skill 的 metadata 則是另一塊自動注入的拼圖。它們共同組成了最終的 System Prompt。

> **🏗️ 建築人視角：**
>
> 如果把整個 System Prompt 比作一份完整的**「專案執行計畫書 (BEP)」**：
>
> - **① 內建核心指令** = 事務所的**「公司內控標準」**（你進來就得遵守，改不了）
> - **② CLAUDE.md** = 你針對這個專案寫的**「專案設計準則 (Design Guidelines)」**（你能完全自訂）
> - **③ Skill Metadata** = 你在準則中列出的**「需要諮詢的顧問名冊」**（消防顧問、綠建築顧問…的名字和專長，但還沒請他們進場）
> - **④ User Rules** = 你個人的**工作習慣偏好**（例如：我習慣用公制、我偏好 AutoCAD 格式）

##### 觸發流程：以建築設計為例

我們可以將 Skill 的觸發機制，套用到實際的設計場景中：

```
🏛️ 場景：建築師事務所開始一個新建築設計專案

步驟 1：啟動（寫 CLAUDE.md + 安裝 Skill）
─────────────────────────────────────────
  你在 CLAUDE.md 中寫下專案規則：
    "這是一個住宅設計專案，需遵守建築技術規則…"

  你安裝了以下 Skill：
    ✅ 綠建築檢討 Skill（name + description 自動注入 System Prompt）
    ✅ 消防法規檢討 Skill（name + description 自動注入 System Prompt）
    ✅ 機電系統 Skill（name + description 自動注入 System Prompt）

  此時 Claude 知道：
    「我手上有綠建築、消防、機電三個顧問可以用。
      但他們還在辦公室待命，沒有進場。」

步驟 2：一般設計工作（Skill 未被觸發）
─────────────────────────────────────────
  使用者：「幫我規劃一個 3 房 2 廳的平面配置」

  Claude 判斷：→ 這是一般設計任務
               → 不需要呼叫任何 Skill
               → 直接用自己的知識處理

步驟 3：觸發消防 Skill（按需載入）
─────────────────────────────────────────
  使用者：「這個走廊寬度符合消防法規嗎？」

  Claude 判斷：→ 這和「消防法規檢討 Skill」的 description 吻合！
               → 載入消防 Skill 的完整 SKILL.md（Level 2）
               → 如果需要，再讀取 references/消防法規條文.md（Level 3）
               → 如果需要計算，執行 scripts/走廊寬度計算.py（Level 3）

  此時 Claude 的狀態：
    「消防顧問已經進場，帶著他的法規和計算工具。
      綠建築和機電顧問依然在辦公室待命。」
```

##### Skill 運作的官方佐證

根據 Anthropic 官方文件（[Agent Skills 文件](https://docs.anthropic.com/en/docs/agents-and-tools/agent-skills) 和 [Engineering Blog](https://www.anthropic.com/engineering/equipping-agents-for-the-real-world-with-agent-skills)），這個觸發機制的原文描述是：

> "Claude loads this metadata at startup and **includes it in the system prompt**. This lightweight approach means you can install many Skills without context penalty; Claude only knows each Skill exists and when to use it."
>
> （Claude 在啟動時載入 metadata，並**將其納入 system prompt 中**。這種輕量化的做法意味著你可以安裝很多 Skill 而不會造成 context 負擔；Claude 只知道每個 Skill 的存在和使用時機。）

完整的架構圖如下：

```
┌──────────────────────────────────────────────────────────┐
│          System Prompt（最終組裝的完整版）                  │
│                                                          │
│  ┌────────────────────────────────────────────────┐      │
│  │  ① Claude Code 核心指令（不可修改）              │      │
│  └────────────────────────────────────────────────┘      │
│                                                          │
│  ┌────────────────────────────────────────────────┐      │
│  │  ② CLAUDE.md 內容（你寫的專案規則）    ✏️ 可自訂 │      │
│  └────────────────────────────────────────────────┘      │
│                                                          │
│  ┌────────────────────────────────────────────────┐      │
│  │  ③ Skill Metadata（自動注入的顧問名冊）          │      │
│  │  ┌───────────┐ ┌───────────┐ ┌───────────┐    │      │
│  │  │ 綠建築    │ │ 消防法規   │ │ 機電系統   │    │      │
│  │  │ ~50 tok   │ │ ~50 tok   │ │ ~50 tok   │    │      │
│  │  └───────────┘ └───────────┘ └───────────┘    │      │
│  └────────────────────────────────────────────────┘      │
│                                                          │
│  ┌────────────────────────────────────────────────┐      │
│  │  ④ User Rules / Memory               ✏️ 可自訂 │      │
│  └────────────────────────────────────────────────┘      │
│                                                          │
└──────────────────────────────────────────────────────────┘
         ↑ 啟動時全部在 context 中（但很輕量）

         ↓ 觸發時才按需載入（Level 2 & 3）

┌──────────────────────────────────────────────────────────┐
│  被觸發的 Skill 完整內容：                                 │
│  SKILL.md Body + references/ + scripts/                  │
│  （只有被觸發的 Skill 才會載入，其他留在檔案系統中）         │
└──────────────────────────────────────────────────────────┘
```

**Skill 的 metadata 是「被納入」System Prompt 的，而不是「取代」它。** System Prompt 依然是整個架構的頂層框架，Skill 只是在其中註冊了一小段「目錄索引」（就像顧問名冊上的一行字）。

#### 1.1.3 修正比喻：不是「助理 vs 顧問」，而是「法律體系」

回到之前的建築人比喻，原始的說法——

> *「一般的 System Prompt 就像是你對你的助理說『幫我畫個圖』；但 Skill 就像是你請了一個專業顧問……」*

這個比喻**有落差**。因為這個描述把 System Prompt 矮化成了一句隨口的指令（「幫我畫個圖」），暗示 Skill 比 System Prompt 更高級、更專業。事實上恰好相反：

> **🏗️ 建築人視角（修正版）：國家法律體系**
>
> 把整個 Claude 架構想像成一個**國家的法律體系**：
>
> | 角色 | 法律類比 | 建築類比 | 功能 |
> |------|----------|----------|------|
> | **Anthropic** | 憲法 | **國土計畫法/建築法** | 不可逾越的最高準則（安全、倫理） |
> | **System Prompt** | 都市計畫法規/建築技術規則 | **都市設計審議準則 + 土地使用分區管制** | 定義這個「專案場域」的一切規則與角色 |
> | **Skill** | 技術規範 (CNS/ISO)/施工標準 | **專業顧問的 SOP + 標準圖說** | 在法規框架下，提供特定工作的專業知識 |
> | **User Message** | 建照申請書/變更設計申請 | **業主需求 (Brief)** | 在規則內提出具體請求 |
>
> **Skill 之所以能存在，是因為 System Prompt 允許它存在。** 就像消防法規顧問之所以能進場作業，是因為都市設計審議準則中有「防火區劃」的要求。如果 System Prompt 說「禁止使用外部腳本」，那 Skill 中的 `scripts/` 就不會被執行。
>
> —— **Skill 不會弱化 System Prompt，正如專業顧問的 SOP 不會弱化建築法規。它們是在法規框架下的「具體執行方案」。**

#### 1.1.4 小結：三者的「不衝突」關係

|              | System Prompt                   | Skill                              | User Message                   |
| ------------ | ------------------------------- | ---------------------------------- | ------------------------------ |
| **位階**     | 高（Operator 層級）             | 中（被 System Prompt 管理）        | 低（User 層級）                |
| **存在時機** | 會話開始即存在                  | Metadata 啟動時載入；Body 按需載入 | 每輪對話輸入                   |
| **約束力**   | 對 Skill 和 User 都有約束       | 受 System Prompt 約束              | 受 System Prompt 和 Skill 約束 |
| **性質**     | 治理框架（是什麼 + 不可做什麼） | 專業知識包（怎麼做）               | 任務請求（要做什麼）           |
| **類比**     | 法律/規章制度                   | 技術規範/SOP                       | 申請/請求                      |

**結論：Skill 架構不會弱化 System Prompt。** 它們的關係是：

- System Prompt 定義了**遊戲規則**（你是誰、你不能做什麼、你的工作方式）。
- Skill 提供了**遊戲裝備**（在規則允許下，附加的專業能力和知識）。
- User Message 是**下達任務**（在規則和裝備範圍內，提出具體請求）。

Skill 豐富了 Claude 的能力，但從未也不可能超越 System Prompt 的權威。這就是為什麼 Anthropic 設計 Skill 時，選擇將 Skill metadata **注入** System Prompt 中，而不是讓 Skill 與 System Prompt 平行——因為 **System Prompt 是容器，Skill 是被放入容器的內容物**。

### 1.2 Skill 的全架構 (Anatomy of a Skill)

#### 1.2.1 核心問題：Skill 可以只用一段 prompt 做到嗎？

**答案是：技術上可以——最簡形態的 Skill 就是一份文字檔。**

一個 Skill 目錄中，唯一**必要**的檔案是 `SKILL.md`。其餘三個目錄全部都是**選用**的：

```text
skill-name/
├── SKILL.md       (必要) - 核心指令與元數據（YAML frontmatter + Markdown 指令）
├── scripts/（腳本） (選用) - 可執行的代碼 (Python/Bash)，用於重複性高或需要確定性的任務
├── references/（參考資料）(選用) - 知識庫、API 文檔、架構定義
└── assets/（資源）  (選用) - 輸出模版、圖片、字體等（Claude 產出時使用，通常不載入 context）
```

> **📝 術語約定**：以下行文中，`scripts/`（腳本）、`references/`（參考資料）、`assets/`（資源）皆指上述目錄。首次提及時以雙語呈現，後續視語境使用中文或 `目錄名/` 格式。

也就是說，**一個 Skill 的最小可行版本 (MVP) 就是一個目錄裡放一個 SKILL.md**——裡面寫的就是結構化的 prompt。

那問題就來了：**如果 Skill 最簡形態就是一段 prompt，那它和「一般的 prompt」差在哪裡？**

#### 1.2.2 「一段 prompt」 vs 「一個 Skill」：五個關鍵差異

|              | 一般 Prompt（對話中的指令）            | Skill（SKILL.md）                                              |
| ------------ | -------------------------------------- | -------------------------------------------------------------- |
| **生命週期** | 寫了就用，用完即棄；下次對話要重新寫   | **持久化存在**於檔案系統，跨對話自動可用                       |
| **發現機制** | 無。Claude 不知道你曾經寫過這段 prompt | **自動發現**。metadata (name + description) 常駐 System Prompt |
| **載入方式** | 一寫入就佔用 context window            | **按需載入**。未觸發時只佔 ~50 tokens（metadata）              |
| **可擴展性** | 全部內容必須塞在一段文字中             | 可拆分為 SKILL.md + 參考資料 + 腳本，**無限擴展**              |
| **可重用性** | 必須複製貼上                           | **一次撰寫，永久使用**。可透過 marketplace 分享給別人          |

> **🏗️ 建築人視角：**
>
> 這就像「口頭交代」vs「寫成 SOP」的差別：
>
> - **一般 Prompt** = 你在工地現場口頭跟工人說「這面牆要用 2 小時防火時效的隔間」。說完就沒了，下次換一個工人你得重新說一遍。
> - **Skill** = 你把「防火隔間施工標準」寫成一份正式的 **SOP 文件**，放在事務所的共用資料夾裡。任何設計師遇到防火隔間需求時，自動知道到哪找這份文件，打開來就能照做。

**結論：Skill 確實可以只用 prompt 做到，但 Skill 框架賦予了它持久性、可發現性和可擴展性——這是裸 prompt 做不到的。**

#### 1.2.3 真實範例：從「純 prompt」到「完整配備」的光譜

在這個 repo 的 37 個 Skill 中，可以清楚看到一個**組成光譜**——從極簡到豐富：

##### 🟢 Level 1：純 SKILL.md（就只是結構化的指令）

| Skill           | 目錄內容                  | 本質                                     |
| --------------- | ------------------------- | ---------------------------------------- |
| `skills-search` | 只有 `SKILL.md`（179 行） | 純文字指令：教 Claude 怎麼用 `ccpm` CLI  |
| `fact-checker`  | 只有 `SKILL.md`（284 行） | 純文字 SOP：定義事實查核的五步驟工作流程 |

這些 Skill **沒有 `scripts/`（腳本）、沒有 `references/`（參考資料）、沒有 `assets/`（資源）**——它們就是一份結構化的 prompt，差別只在於被包在 Skill 框架裡，獲得了**持久化**和**自動發現**的能力。

> 📖 **逐行翻譯與解說**：這兩個 Skill 的完整中文翻譯、設計解讀和建築類比，請參閱 **[Level 1 Skill 範例解說](./Level1_Skill_Examples_Explained.md)**。
>
> 該文件包含：
> - `skills-search`：每個段落的翻譯 + 為什麼這樣寫的設計意圖分析
> - `fact-checker`：五步驟 SOP 的逐步翻譯 + 安全設計原則解讀
> - 兩者的結構對比和建築人視角總評

> **🏗️ 建築人視角：** 這就像一份**「設計檢查清單 (Design Checklist)」**——只有文字，沒有附表，沒有計算工具。例如：「交屋前檢查清單：1. 確認門窗氣密 2. 確認防水測試 3.…」。簡單但有用。

##### 🟡 Level 2：SKILL.md + `references/`（指令 + 參考資料）

| Skill                               | 目錄內容                               | 本質                        |
| ----------------------------------- | -------------------------------------- | --------------------------- |
| `docs-cleaner`                      | `SKILL.md` + `references/`（1 file）   | 指令 + 文檔整理原則         |
| `tunnel-doctor`                     | `SKILL.md` + `references/`（1 file）   | 指令 + Tailscale 診斷知識庫 |
| `claude-md-progressive-disclosurer` | `SKILL.md`（17K bytes）+ `references/` | 指令 + 漸進式揭露的最佳實踐 |

這些 Skill 的核心依然是 prompt（SKILL.md），但附帶了**額外的參考資料**。Claude 在需要時才會去讀取 `references/`，不會一開始就全部載入。

> **🏗️ 建築人視角：** 這就像一份**「設計準則 + 技術規範附件」**——主文件是準則（SKILL.md），後面附上了法規條文或標準圖說（`references/` 參考資料）。平時只看準則就夠，需要查法條時才翻附件。

##### 🔴 Level 3：SKILL.md + `references/` + `scripts/`（+ 可能再加 `assets/`）

| Skill              | 目錄內容                                                                | 本質                               |
| ------------------ | ----------------------------------------------------------------------- | ---------------------------------- |
| `qa-expert`        | `SKILL.md` + `references/`（5 files）+ `scripts/`（2 files）+ `assets/` | 完整 QA 基礎建設                   |
| `transcript-fixer` | `SKILL.md` + `references/`（14 files）+ `scripts/`（52 files!）         | 語音轉文字校正引擎                 |
| `skill-creator`    | `SKILL.md` + `references/` + `scripts/`（4 files）                      | Meta-skill，帶初始化/驗證/打包腳本 |

這些是**完整配備**的 Skill。SKILL.md 依然是「大腦」（指令），但 `scripts/`（腳本）提供了**確定性的自動化能力**，`references/`（參考資料）提供了**深度的知識支援**。

> **🏗️ 建築人視角：** 這就像一個**完整的專業顧問團隊進場**——不只帶了 SOP（SKILL.md），還帶了計算軟體（`scripts/` 腳本：例如 Dynamo 腳本算面積）、法規法條（`references/` 參考資料：例如建築技術規則全文），甚至出圖模板（`assets/` 資源：例如圖框和簡報底圖）。

#### 1.2.4 為什麼這樣設計？—— `scripts/`（腳本）的角色最值得深思

你可能會問：「既然 SKILL.md 已經可以用 prompt 教 Claude 做事，為什麼還需要腳本？」

這是因為 **prompt 和 code 擅長的事情不一樣**：

|                  | Prompt（SKILL.md 中的文字指令）    | Code（`scripts/` 中的腳本）                        |
| ---------------- | ---------------------------------- | -------------------------------------------------- |
| **適合**         | 需要判斷力、靈活性、理解語境的任務 | 需要精確、確定性、可重複的操作                     |
| **不適合**       | 數學計算、精確格式轉換、位元操作   | 需要主觀判斷、創意發想、理解曖昧需求               |
| **佔用 context** | ✅ 指令文字會佔用 context window    | ❌ 腳本程式碼**不會**進入 context（只有輸出結果會） |
| **可靠度**       | 有機率出錯（LLM 的特性）           | 100% 確定性（程式碼就是程式碼）                    |

> **🏗️ 建築人視角：**
>
> - **SKILL.md（文字指令）** = **建築師的設計判斷**。「這塊基地適合怎樣的量體配置？」——這需要經驗、直覺、對環境的理解。
> - **`scripts/`（腳本）** = **結構工程師的計算書**。「這根梁的載重計算是否符合規範？」——這不能靠「大概」，必須精確到小數點，而且同樣的輸入永遠要得到同樣的結果。
>
> 你不會讓結構工程師「靠感覺」算梁的載重，也不會用 Excel 公式來做建築設計的概念發想。**各有所長**。

Anthropic 官方 Blog 也特別強調了這一點：

> "Certain operations are better suited for traditional code execution. [...] Because code is deterministic, this workflow is consistent and repeatable."
>
> （某些操作更適合用傳統程式碼執行。[…] 因為程式碼是確定性的，這個工作流程是一致且可重複的。）

#### 1.2.5 小結：目錄結構 = 能力的「可選配」

```
                      低 ← ←  自動化程度  → → 高
                      高 ← ←  彈性/判斷力 → → 低

純 Prompt Skill              混合型 Skill             重度腳本 Skill
┌──────────────┐    ┌──────────────────────┐    ┌──────────────────────┐
│  SKILL.md    │    │  SKILL.md            │    │  SKILL.md            │
│  （純文字）   │    │  + references/       │    │  + references/ (14)  │
│              │    │  + scripts/ (少量)    │    │  + scripts/ (52!)    │
│              │    │                      │    │  + assets/           │
└──────────────┘    └──────────────────────┘    └──────────────────────┘
  fact-checker         skill-creator              transcript-fixer
  skills-search        qa-expert                  

  「設計檢查清單」      「設計準則+計算工具」       「完整專案顧問團隊」
```

**設計哲學：你的 Skill 需要多少配備，取決於任務的性質。**

- 如果任務是**靠判斷力和知識**（例如：程式碼審查、事實查核），一份 SKILL.md 就夠了。
- 如果任務需要**精確的計算和操作**（例如：PDF 轉換、語音校正），就需要 `scripts/`（腳本）。
- 如果任務需要**大量專業知識**（例如：消防法規全文），就需要 `references/`（參考資料）。

這就回到你一開始問的問題：**Skill 確實可以只用一連串 prompt 做到——但當任務需要確定性和大量知識時，腳本和參考資料讓 Skill 從「一段聰明的 prompt」進化為「一個完整的專業系統」。**

### 1.3 核心元件：SKILL.md 的 YAML Frontmatter 配置

#### 1.3.1 先回答基本問題：什麼是 YAML？

**YAML** (YAML Ain't Markup Language) 是一種**人類可讀的資料格式**——你可以把它想成「一種比 JSON 更好讀的填表格方式」。

舉個最簡單的例子，如果你要描述一棟建築的基本資料：

```yaml
# 這是 YAML 格式（# 開頭的是註解）
建物名稱: 台北 101
用途: 商業辦公
樓層數: 101
設計者: 李祖原
```

對應到更常見的格式比較：

| JSON（程式語言常用）                      | YAML（人類好讀）                    |
| ----------------------------------------- | ----------------------------------- |
| `{ "name": "Taipei 101", "floors": 101 }` | `name: Taipei 101`<br>`floors: 101` |

**YAML 的特色就是：不用大括號、不用引號（通常），靠縮排和冒號就能表達結構化資料。**

> **🏗️ 建築人視角：**
>
> YAML 就像建築牌照上的**「基本資料欄位」**——不是用長篇文字描述，而是用一格一格的欄位（名稱：XX、用途：XX、樓層：XX）來結構化地記錄資訊。電腦和人類都能一眼看懂。

#### 1.3.2 什麼是 YAML Frontmatter？

在 SKILL.md 中，YAML 出現在檔案**最開頭**，被兩行 `---` 包夾起來。這個區塊叫做 **Frontmatter**（前序資料）：

```yaml
---
name: fact-checker
description: Verifies factual claims in documents using web search
  and official sources, then proposes corrections with user confirmation.
---

# 以下是 Markdown 正文（SKILL.md Body）...
```

- `---` 上面的部分 → **什麼都沒有**（Frontmatter 必須在檔案最開頭）
- 兩個 `---` 之間 → **YAML Frontmatter**（元數據，Level 1，常駐 System Prompt）
- `---` 下面的部分 → **Markdown 正文**（指令內容，Level 2，按需載入）

> **🏗️ 建築人視角：**
>
> Frontmatter 就像建照申請書上的**「封面表格」**：
> - **兩個 `---`** = 表格的邊框
> - **YAML 內容** = 表格裡填的欄位（案名、用途、面積…）
> - **Markdown 正文** = 表格後面附的**設計說明書**
>
> 審查人員（Claude）拿到一疊建照申請書時，不會每份都翻開看設計說明書——**只看封面表格就能判斷這份申請跟當前案子有沒有相關**。有相關的才打開來細看。這就是 Progressive Disclosure 的運作方式。

#### 1.3.3 YAML Frontmatter 的完整欄位表

根據 `skill-creator` 的官方定義，SKILL.md 的 Frontmatter 可以使用以下欄位：

| 欄位                       | 必要？               | 說明                                                                                                   |
| -------------------------- | -------------------- | ------------------------------------------------------------------------------------------------------ |
| `name`                     | 否（會用目錄名替代） | Skill 的顯示名稱。只能用小寫字母、數字和連字號，最多 64 字元。                                         |
| **`description`**          | **✅ 建議必填**       | **最重要的欄位。** Claude 根據這段文字判斷「什麼時候要觸發這個 Skill」。如果不填，會用正文第一段替代。 |
| `context`                  | 否                   | 設為 `fork` 時，Skill 會在獨立的子代理 (subagent) 中執行，不會汙染主對話的 context。                   |
| `agent`                    | 否                   | 當 `context: fork` 時，指定使用哪種子代理類型：`Explore`、`Plan`、`general-purpose` 等。               |
| `allowed-tools`            | 否                   | 指定 Claude 在執行此 Skill 時可以**不需詢問使用者**就直接使用的工具。支援萬用字元。                    |
| `disable-model-invocation` | 否                   | 設為 `true` 時，Claude 不會自動觸發此 Skill，只能由使用者手動用 `/name` 呼叫。                         |
| `user-invocable`           | 否                   | 設為 `false` 時，Skill 不會出現在 `/` 選單中。用於背景知識類的 Skill。                                 |
| `model`                    | 否                   | 指定執行此 Skill 時使用的模型。                                                                        |
| `argument-hint`            | 否                   | 自動完成時顯示的參數提示。例如：`[issue-number]` 或 `[filename]`。                                     |
| `hooks`                    | 否                   | 綁定 Skill 生命週期的鉤子（hook）。例如在呼叫前執行某個指令。                                          |

> ⚠️ **注意**：在這 10 個可用欄位中，**只有 `description` 是真正建議必填的**。即使是 `name` 不填也會自動使用目錄名稱替代。其他欄位完全視需求選用。

#### 1.3.4 最重要的欄位：`description`

`description` 是 Skill 的**靈魂欄位**。它決定了 Claude 在什麼時機觸發你的 Skill。

來看 repo 中幾個真實範例的 `description` 寫法：

##### 範例 1：`macos-cleaner`（簡潔直接型）

```yaml
description: Analyze and reclaim macOS disk space through intelligent
  cleanup recommendations. This skill should be used when users report
  disk space issues, need to clean up their Mac, or want to understand
  what's consuming storage.
```

**📖 翻譯：** 透過智慧化的清理建議，分析並釋放 macOS 磁碟空間。當使用者回報磁碟空間不足、需要清理 Mac、或想了解什麼佔用了儲存空間時，應使用此 Skill。

**💡 設計解讀：** 先說「做什麼」（分析磁碟空間），再說「什麼時候觸發」（使用者報告空間不足時）。用 "This skill should be used when..." 的標準句式。

##### 範例 2：`prompt-optimizer`（列舉觸發詞型）

```yaml
description: Transform vague prompts into precise, well-structured
  specifications using EARS methodology. This skill should be used when
  users provide loose requirements, ambiguous feature descriptions, or
  need to enhance prompts. Triggers include requests to "optimize my prompt",
  "improve this requirement", "make this more specific"...
```

**📖 翻譯：** 使用 EARS 方法論將模糊的提示詞轉換為精確的、結構化的規格。當使用者提供鬆散的需求、模糊的功能描述，或需要改善提示詞時，應使用此 Skill。觸發關鍵字包括「優化我的 prompt」、「改善這個需求」、「讓它更具體」…

**💡 設計解讀：** 除了標準的 "should be used when..." 之外，還額外列出了 **"Triggers include..."**——具體寫出使用者可能會說的話。這讓 Claude 更容易做意圖匹配。

> **🏗️ 建築人視角：**
>
> `description` 就像顧問名冊上的**「專長描述 + 委託條件」**：
>
> | 寫法 | 建築類比 | 效果 |
> |------|----------|------|
> | 只寫「消防」 | 顧問名冊只寫「消防」 | ❌ 太籠統，不知道什麼時候該找他 |
> | 寫「消防法規檢討、避難路徑規劃、防火區劃分析。當設計涉及防火時效、走廊寬度、安全梯配置時使用」 | 精確的專長 + 委託條件 | ✅ 專案經理一看就知道什麼時候該找這個顧問 |
>
> —— **`description` 寫得越精確，Claude 就越知道什麼時候該觸發你的 Skill。**

#### 1.3.5 進階欄位：`context: fork` 和 `allowed-tools`

除了 `description` 之外，有兩個欄位特別值得理解：

##### `context: fork`（獨立子代理執行）

```yaml
---
name: competitors-analysis
description: Evidence-based competitor tracking and analysis...
context: fork
allowed-tools: Read, Grep, Glob, Bash(git *), Bash(mkdir *), Bash(ls *), Bash(wc *)
---
```

當 `context` 設為 `fork` 時，Claude 會把這個 Skill 的工作**分派給一個獨立的子代理 (subagent)**。子代理有自己的 context window，做完事後只回傳結果。

| 沒有 `context: fork`                 | 有 `context: fork`                   |
| ------------------------------------ | ------------------------------------ |
| Skill 在主對話中**直接執行**         | Skill 在**獨立的子代理**中執行       |
| 所有中間過程都會佔用主對話的 context | 只有最終結果回到主對話               |
| 適合：輕量的參考指南、規範說明       | 適合：多步驟的調研、分析、程式碼生成 |

> **🏗️ 建築人視角：**
>
> - **沒有 `fork`** = 你讓顧問**坐在你辦公桌旁**一起工作。他的所有草稿、計算紙、法規影本全都攤在你桌上（佔用你的 context）。
> - **有 `fork`** = 你把工作**發包給外部顧問事務所**。他們在自己的辦公室做完，最後只寄一份精美的報告書給你。你的桌面（context window）保持乾淨。

##### `allowed-tools`（授權工具清單）

```yaml
allowed-tools: Read, Grep, Bash(git *), Bash(mkdir *), Bash(ls *)
```

正常情況下，Claude 使用某些工具前會先問你：「我可以執行這個指令嗎？」。`allowed-tools` 讓你預先授權——指定的工具 Claude 可以**直接使用，不需要每次都問**。

> **🏗️ 建築人視角：**
>
> 這就是**「工地進場授權證」**。你跟保全說：「這個消防顧問可以自由進出 B1 機房和屋頂機械層，不用每次都打電話問我。」——但他不能進財務室（未授權的工具就不能用）。

### 1.4 漸進式引導設計原則 (Progressive Disclosure)

為了極大化 Context 效率，Skill 採用三層載入並且給予字數上的限制：（這樣的字數也在於讓你能夠正確且具體的寫出你的需求）

1. **Metadata (name + description)**: 始終在 context 中 (~100 字)。
2. **SKILL.md Body**: 當技能觸發時載入 (<5k 字)。
3. **Bundled resources (References/Scripts)**: 根據需要才載入（無限擴展）。

> **🏗️ 建築人視角:**
>
> 這就是 **LOD (Level of Development) 模型精細度**的概念！
>
> 1. **Metadata**: **LOD 100 (規劃/量體階段)** - 只有專案名稱和基本描述，知道有這棟樓，但還沒畫細節。
> 2. **SKILL.md Body**: **LOD 300 (設計/送審階段)** - 確定了詳細規格、構造形式，就像看到了平面圖和立面圖。
> 3. **Bundled resources**: **LOD 400/500 (施工/製造階段)** - 所有的細部大樣 (Details)、螺絲孔位、完整的法規條文都在這裡。只有真正要「進場施工」時才需要拿出來看，不然圖紙會太厚，像極了我們不希望把所有的大樣圖都塞在同一張平面圖裡（會導致 Context token 爆炸）。

---

## 第二章：Meta-Skill — `skill-creator` 實戰

### 2.1 `skill-creator` 是什麼？

`skill-creator` 是一個 **Meta-Skill（建立 Skill 的 Skill）**。它的作用是：**引導你（或 Claude 自己）把零散的經驗和知識，按照標準化的流程打包成一個可重複使用的 Skill。**

簡單說：你不需要自己從零開始建立目錄結構、寫 YAML、擔心格式對不對——`skill-creator` 會一步步帶你完成。

> **🏗️ 建築人視角：**
>
> `skill-creator` 就是事務所裡的 **BIM 經理（BIM Manager）**。
>
> 你（設計師）在工作中累積了很多檢討經驗，例如：「走廊寬度要看有沒有陽台」「外牆防火時效要依樓層高度判斷」。但這些知識散落在你的腦袋、筆記本、和同事間的口耳相傳中。
>
> BIM 經理的工作就是把這些散落的經驗**打包成標準作業程序**（Revit Family、檢核表、SOP 文件），讓事務所裡任何人都能按表操課。
>
> `skill-creator` 就是做這件事的工具。

### 2.2 你已經有什麼？——盤點現有資源

在動手建立 Skill 之前，先盤點你在 `REVIT_MCP_study` 專案中**已經做好的東西**。這些就是 Skill 的原料：

#### 📂 `domain/` 目錄（領域知識——你已有的 SOP 文件）

| 檔案                             | 內容                                           | Skill 中的角色              |
| -------------------------------- | ---------------------------------------------- | --------------------------- |
| `fire-rating-check.md`           | 防火等級 6 步驟檢查流程 + 法規比對表           | → SKILL.md 正文（核心 SOP） |
| `corridor-analysis-protocol.md`  | 走廊防火分析：識別→定位→分析→標註              | → SKILL.md 正文（核心 SOP） |
| `floor-area-review.md`           | 容積檢討 6 步驟流程 + 面積計算公式             | → SKILL.md 正文（核心 SOP） |
| `element-coloring-workflow.md`   | 元素上色 5 步驟流程 + 顏色方案                 | → SKILL.md 正文（核心 SOP） |
| `wall-check.md`                  | 牆壁內外方向檢查邏輯                           | → SKILL.md 正文（輔助流程） |
| `room-boundary.md`               | 房間邊界計算：3 種邊界位置的差異               | → SKILL.md 正文（輔助知識） |
| `references/building-code-tw.md` | 台灣建築法規摘要（容積率、防火、避難、無障礙） | → `references/`（參考資料） |

#### 📂 `GEMINI.md`（觸發規則——你已有的「門面」配置）

你在 `GEMINI.md` 的**「工作流程觸發規則」**中已經寫好了一張表：

```
| 關鍵字                           | 文件路徑                             |
| -------------------------------- | ------------------------------------ |
| 容積、樓地板面積、送審、法規檢討 | domain/floor-area-review.md          |
| 防火、耐燃、消防、防火時效       | domain/fire-rating-check.md          |
| 走廊、逃生、通道寬度、避難路徑   | domain/corridor-analysis-protocol.md |
| 牆壁上色、顏色標示、視覺化       | domain/element-coloring-workflow.md  |
| QA、檢查、驗證、一致性           | domain/qa-checklist.md               |
```

**💡 關鍵洞察：你已經在用「斜線指令 (`/domain`)」做的事，本質上就是手動版的 Skill 建立流程。**

對照一下：

| 你過去的做法（`/domain` 斜線指令）                    | Skill 框架的對應                                                  |
| ----------------------------------------------------- | ----------------------------------------------------------------- |
| 在 `GEMINI.md` 的觸發規則表中寫關鍵字                 | → YAML Frontmatter 的 `description` 欄位                          |
| 把 SOP 寫成 `domain/*.md` 檔案                        | → `SKILL.md` 正文                                                 |
| 把法規條文放在 `domain/references/`                   | → `references/` 目錄                                              |
| 在 `element-coloring-workflow.md` 中列出 MCP 工具組合 | → `scripts/` 目錄（如果要自動化）或 SKILL.md 正文中的工具使用指引 |
| 在 `GEMINI.md` 中定義 AI 的行為模式（如自動預檢）     | → YAML Frontmatter 的 `context: fork` + `allowed-tools`           |

**也就是說，你的 `/domain` 做法已經是 Skill 的雛形——只差用標準框架重新包裝。**

### 2.3 實戰：用 `skill-creator` 的 9 步流程建立 Revit 法規檢討 Skill

#### 前置準備：讓電腦認識 Skill

依照你的技術背景和使用的 AI 工具，我們提供兩種開始的方式：

**🌟 方式 A：在 Google Antigravity IDE 環境下（強力推薦建築與設計人！）**

如果你沒有資訊工程背景，看到黑底白字的「終端機指令（Terminal）」就會覺得痛苦，那麼**強烈建議你使用 Google Antigravity 這個視覺化的 IDE 來完成！**

Antigravity 已經**原生支援** Skill 架構，你**完全不需要安裝任何指令**！不需要終端機，只要在專案裡按右鍵建資料夾，然後直接跟 AI 聊天，就能把 Skill 建立起來。

> 📖 **詳細教學請看這篇：[在 Google Antigravity IDE 中無痛部署 Skills 指南](./Antigravity_Skill_Setup_Guide.md)** 
> （跟著這篇指南，你甚至不需要看下面的 9 步流程，直接在對話中就完成了！）

**⚙️ 方式 B：使用 Claude Code 終端機介面（適合有工程背景的開發者）**

如果你習慣使用終端機，必須先將 `skill-creator` 安裝到你的環境中：

```bash
# 加入 Marketplace
claude plugin marketplace add https://github.com/daymade/claude-code-skills

# 安裝 skill-creator
claude plugin install skill-creator@daymade-skills
```
*(安裝完成後重新啟動)*

#### 操作方式澄清：你不需要手動寫檔案

接下來的 Step 1～9 **不是要你自己打開編輯器手動撰寫 SKILL.md**。安裝了 `skill-creator` 之後，整個流程是透過**和 Claude 對話**來完成的：

- **你負責「說」**——描述經驗、回答 Claude 的引導性問題
- **Claude 負責「做」**——建立目錄、撰寫 YAML、寫 Markdown、驗證格式、打包

就像你不需要自己寫 C# 也能用 MCP 操作 Revit 一樣，你不需要自己寫 YAML 也能用 `skill-creator` 建立 Skill。

以下的每個 Step 都會標注：**你做什麼（對話）** 和 **Claude 做什麼（自動）**。

---

現在我們開始，用 `skill-creator` 定義的 9 步流程，把你的 `domain/` 資源打包成一個名為 `revit-code-reviewer` 的標準 Skill。

> 📎 `skill-creator` 的完整 9 步流程定義在 [`skill-creator/SKILL.md`](./skill-creator/SKILL.md)

---

#### Step 1：理解這個 Skill 的具體使用場景

> **`skill-creator` 的指引**：用具體的例子搞清楚——「使用者會怎麼用這個 Skill？」

**你的使用場景：**

1. 使用者在 Revit 中開啟一張平面圖（View），MCP 連線已就緒
2. 使用者對 Claude 說：「幫我檢查這層的走廊寬度有沒有符合法規」
3. Claude 觸發 `revit-code-reviewer` Skill
4. Claude 按照 SOP 流程：讀取房間資料 → 篩選走廊 → 取得寬度 → 比對法規 → 標色 → 產出報告
5. 使用者確認報告後，決定是否修改模型

**更多使用場景範例：**
- 「幫我做這個案子的防火等級檢查」
- 「幫我跑容積檢討，看有沒有超過法規上限」
- 「把牆壁依照防火時效上色」

> **🏗️ 建築人視角：** 這一步就像業主訪談——搞清楚這個顧問到底會被問什麼問題、在什麼情境下出場。

---

#### Step 2：規劃 Skill 的內容和資源

> **`skill-creator` 的指引**：分析每個使用場景，決定哪些東西遞重複出現、值得封裝。

根據你的 `domain/` 內容，我們來決定什麼東西「應該裝進 Skill」：

##### 資源盤點結果：

| 資源                                                       | 放哪裡            | 為什麼                                                                  |
| ---------------------------------------------------------- | ----------------- | ----------------------------------------------------------------------- |
| 走廊分析 SOP、防火檢查 SOP、容積檢討 SOP                   | **SKILL.md 正文** | 這是 Claude 每次被觸發時都需要遵循的核心流程（高自由度判斷）            |
| 元素上色流程、牆壁檢查邏輯、房間邊界知識                   | **SKILL.md 正文** | 這些是支援性的輔助流程，需要時才參考                                    |
| 台灣建築法規摘要 (`building-code-tw.md`)                   | **`references/`** | 這是大量的查詢型資料，不需要每次都載入 context，需要查法規時才讀取      |
| MCP 工具呼叫範例（如 `override_element_color` 的參數格式） | **SKILL.md 正文** | 工具呼叫方式是 Claude 需要知道的，但不是腳本——Claude 會根據情境組合工具 |

##### 為什麼這個 Skill **不需要** `scripts/` 和 `assets/`？

| 目錄               | 需要嗎？         | 原因                                                                                                                                                                             |
| ------------------ | ---------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `scripts/`（腳本） | **❌ 本案不需要** | 你的 MCP-Server 已經提供了原子化工具（`get_rooms_by_level`、`override_element_color` 等）。Claude 的工作是**判斷什麼時候用哪個工具、用什麼參數**——這是判斷力的事，不是腳本的事。 |
| `assets/`（資源）  | **❌ 本案不需要** | 這個 Skill 不產出檔案（如 PDF、PPT），它的輸出是「Revit 模型中的顏色標記 + 文字報告」。不需要模板或圖片。                                                                        |

> **🏗️ 建築人視角：**
>
> 這個 Skill 的定位是**「資深顧問（David）的檢討經驗 SOP + 法規手冊」**。David 進場時帶的是：
> - 一份檢核流程（SKILL.md） ← 他的腦中 SOP
> - 一本法規手冊（`references/`） ← 隨身攜帶的參考書
>
> 他**不需要**帶計算軟體（`scripts/`），因為 Revit + MCP 就是他的工具——工具已經在工地上了，他只需要知道「什麼時候該用哪個工具」。

---

#### Step 3：初始化 Skill 目錄結構

> **`skill-creator` 的指引**：執行 `init_skill.py` 建立標準目錄。

如果你安裝了 `skill-creator`，可以讓 Claude 自動執行：

```bash
# Claude 會呼叫 skill-creator 的初始化腳本
python scripts/init_skill.py revit-code-reviewer --path ./
```

這會自動建立以下目錄結構：

```text
revit-code-reviewer/
├── SKILL.md              ← 自動生成的模板（接下來要填充內容）
├── scripts/              ← 自動生成的範例目錄（本案用不到，可刪除）
├── references/           ← 自動生成的範例目錄（我們會放法規文件）
└── assets/               ← 自動生成的範例目錄（本案用不到，可刪除）
```

刪除不需要的目錄後：

```text
revit-code-reviewer/
├── SKILL.md              ← 核心指令
└── references/           ← 法規參考資料
    └── building-code-tw.md
```

---

#### Step 4：編輯 Skill 內容

> **`skill-creator` 的指引**：先處理 `references/`，再寫 `SKILL.md`。

##### 4a. 放入參考資料

把你現有的 `domain/references/building-code-tw.md` 複製到 `revit-code-reviewer/references/` 中。這份法規摘要包含：容積率規定、防火構造標準、防火區劃、走廊寬度、樓梯寬度、逃生距離、無障礙規定。

##### 4b. 撰寫 SKILL.md

以下是整合你所有 `domain/*.md` 之後的 SKILL.md 骨架：

```yaml
---
name: revit-code-reviewer
description: |
  針對 Revit 模型執行台灣建築法規檢討，涵蓋容積檢討、防火等級檢查、
  走廊避難寬度分析、元素上色標記。透過 MCP 連線操作 Revit 模型。
  當使用者提到「容積」「防火」「走廊寬度」「法規檢討」「送審」
  「防火時效」「避難路徑」「牆壁上色」時啟用。
---

# Revit 法規檢討工具

## 前置條件
- Revit 模型已開啟且 MCP 服務已連線
- 目標樓層的房間邊界已正確繪製
- 牆體材質和類型已正確設定

## 法規參考
詳細法規標準請查閱 references/building-code-tw.md

## 檢討流程

### 流程 A：容積檢討
（整合自 floor-area-review.md 的 6 步驟…）

### 流程 B：防火等級檢查
（整合自 fire-rating-check.md 的 6 步驟…）

### 流程 C：走廊避難寬度分析
（整合自 corridor-analysis-protocol.md 的 4 步驟…）

### 流程 D：元素上色標記
（整合自 element-coloring-workflow.md 的 5 步驟…）

## 輔助知識
### 牆壁方向檢查（wall-check.md 的內容）
### 房間邊界計算（room-boundary.md 的內容）

## MCP 工具參考
（列出可用的工具名稱和參數格式）

## 報告輸出格式
（定義標準的報告範本）
```

**💡 注意 `description` 的寫法——直接搬你 `GEMINI.md` 觸發規則表中的關鍵字：**

| 過去在 `GEMINI.md` 中              | 現在在 `description` 中 |
| ---------------------------------- | ----------------------- |
| `容積、樓地板面積、送審、法規檢討` | ✅ 直接寫進去            |
| `防火、耐燃、消防、防火時效`       | ✅ 直接寫進去            |
| `走廊、逃生、通道寬度、避難路徑`   | ✅ 直接寫進去            |
| `牆壁上色、顏色標示、視覺化`       | ✅ 直接寫進去            |

---

#### Step 5～6：清理審查與安全檢查

> 確認 SKILL.md 中沒有洩漏私人路徑、帳號、公司名稱等敏感資訊。

本案中需要特別注意：
- ❌ 不要把 `c:\Users\User\Desktop\REVIT MCP\MCP-Server\` 這種絕對路徑寫進 Skill
- ✅ 改用相對路徑或說明「透過 MCP 連線呼叫工具」

---

#### Step 7：打包

```bash
# skill-creator 會自動驗證 + 打包
python scripts/package_skill.py revit-code-reviewer/
```

打包腳本會檢查：
- YAML Frontmatter 格式是否正確
- `description` 是否足夠具體
- SKILL.md 中引用的 `references/building-code-tw.md` 是否真的存在

---

#### Step 8～9：發布與迭代

打包完成後可以安裝到 Claude Code 中使用。在實際使用時持續迭代——每次發現「Skill 沒處理好的狀況」就回來更新 SKILL.md。

---

### 2.4 完成後的全貌：Skill 如何融入工作流程

安裝 `revit-code-reviewer` Skill 之後，整個工作流程會變成這樣：

```
┌─────────────────────────────────────────────────────────────┐
│                    Claude Code 啟動時                        │
├─────────────────────────────────────────────────────────────┤
│                                                             │
│  System Prompt（自動組裝）                                   │
│  ├── ① Claude Code 核心指令                                 │
│  ├── ② CLAUDE.md / GEMINI.md 內容                           │
│  ├── ③ 已安裝 Skill 的 Metadata（~50 tokens/個）             │
│  │     └── revit-code-reviewer:                             │
│  │          name + description                              │
│  │          （「容積、防火、走廊…」這些觸發詞就在這裡）       │
│  └── ④ User Rules / Memory                                  │
│                                                             │
└────────────────────────┬────────────────────────────────────┘
                         │
                         ▼
┌─────────────────────────────────────────────────────────────┐
│  使用者：「幫我檢查 3F 的走廊寬度有沒有符合法規」            │
└────────────────────────┬────────────────────────────────────┘
                         │
                         ▼
┌─────────────────────────────────────────────────────────────┐
│  Claude 判斷：                                              │
│  「走廊」「法規」→ 匹配 revit-code-reviewer 的 description  │
│  → 載入完整 SKILL.md（Level 2）                              │
│  → 找到「流程 C：走廊避難寬度分析」                          │
└────────────────────────┬────────────────────────────────────┘
                         │
                         ▼
┌─────────────────────────────────────────────────────────────┐
│  Claude 按 SOP 執行（透過 MCP 呼叫 Revit 工具）：           │
│                                                             │
│  1. get_rooms_by_level(level="3F")                          │
│     → 取得 3F 所有房間                                      │
│                                                             │
│  2. 篩選名稱含「走廊/Corridor/廊道」的房間                   │
│     （參考 SKILL.md 中的識別規則）                            │
│                                                             │
│  3. 取得走廊的 BoundingBox → 計算淨寬                        │
│                                                             │
│  4. 讀取 references/building-code-tw.md                      │
│     → 查詢走廊寬度標準（住宅 1.2m、辦公 1.6m…）              │
│                                                             │
│  5. 比對 → 不合格的走廊用紅色標記                            │
│     override_element_color(elementId, color: red)           │
│                                                             │
│  6. 產出報告：「3F 共 2 條走廊，1 條合格、1 條不合格」        │
└─────────────────────────────────────────────────────────────┘
```

### 2.5 從 `/domain` 斜線指令到 Skill 框架：遷移對照表

| 你過去的做法                      | Skill 框架                            | 差異                               |
| --------------------------------- | ------------------------------------- | ---------------------------------- |
| 在 `GEMINI.md` 手動維護觸發規則表 | `description` 自動注入 System Prompt  | 不用手動管理 GEMINI.md 了          |
| 用 `/domain` 指令手動轉換 SOP     | 用 `skill-creator` 引導式建立         | 有標準化流程，不怕漏步驟           |
| `domain/*.md` 散落在專案目錄中    | 整合進一個 `SKILL.md` + `references/` | 攜帶式、可安裝到任何專案           |
| AI 的行為規範寫在 `GEMINI.md` 中  | 行為規範寫在 SKILL.md 正文中          | Skill 自帶行為規範，不依賴外部檔案 |
| 只能在這個專案中使用              | 打包後可安裝到任何 Claude Code 環境   | **可攜性**是最大的升級             |
| 換一個 AI 助手就要重新配置        | Skill 安裝後自動可用                  | 不需要重複寫觸發規則               |

### 2.6 任務風險與自由度匹配

在設計 Skill 的內容時，需要根據任務的性質決定「給 Claude 多大的自由度」：

| 自由度                       | 在 Skill 中的呈現方式                          | 建築類比                                       | 本案範例                                                                     |
| ---------------------------- | ---------------------------------------------- | ---------------------------------------------- | ---------------------------------------------------------------------------- |
| **高自由度**（文字指令）     | SKILL.md 中的流程描述，Claude 自己決定工具組合 | 草圖設計：業主說「大氣的入口」，設計師自由發揮 | 「分析走廊寬度是否合規」——Claude 自己決定先查哪層、怎麼篩選                  |
| **中自由度**（工具使用指引） | SKILL.md 中列出建議工具和參數範圍              | 細部設計：有法規限制，但內部格局可調           | 「上色時用紅色標記不合格、綠色標記合格」——顏色已定，但標記順序由 Claude 決定 |
| **低自由度**（精確腳本）     | `scripts/` 中的腳本，Claude 直接執行           | 預鑄施工：尺寸差 1mm 就裝不上                  | 本案不適用（MCP 工具本身就是原子化的精確操作）                               |

---

### 2.7 升級後的隱憂：雙軌維護怎麼辦？

完成上述的 `skill-creator` 部署後，你可能會擔心：

> **「打包好 Skill 之後，我日常還是會用 `/domain` 來記錄新經驗。那這些新經驗跑到了 `domain/` 目錄，但 Skill 裝的是舊版——我是不是要定期手動同步？不同使用者在不同時間加入，有人用舊的 `/domain` 流程、有人用新的 Skill，知識會不會散落在兩個地方？」**

這個擔心**完全合理**，而且如果不處理，真的會出問題。

我們已經為此準備了一份完整的遷移指南，涵蓋：
- 雙軌維護問題的根因分析
- 解法：將 `/domain` 重新導向到 Skill 目錄（操作習慣不變）
- 一次性部署升級的 6 個具體步驟
- 升級後的持續維護方案
- 新舊使用者共存的標準化策略

> 📖 **延伸閱讀**：**[從 `/domain` 到 Skill：升級遷移指南](./Domain_to_Skill_Migration_Guide.md)**

---

## 第三章：提示詞工程進階 - prompt-optimizer

`prompt-optimizer` 引入了專業的需求語法工具 EARS，將自然語言轉化為「可測試的需求」。

> **🏗️ 建築人視角:**
>
> `prompt-optimizer` 就像是**「規範寫手 (Specification Writer)」**。他把業主模糊的需求（"我要一個防滑的地板"）轉化為承包商不可抵賴的明確條文（"地磚防滑係數應大於 0.55 (CSR)"）。

### 3.1 EARS 的五種核心模式

EARS (Easy Approach to Requirements Syntax) 透過標準句式消除歧義：

1. **Ubiquitous (普適型)**: `The system shall <action>` (核心功能)
2. **Event-driven (事件驅動)**: `When <trigger>, the system shall <action>` (交互邏輯)
3. **State-driven (狀態驅動)**: `While <state>, the system shall <action>` (持續性業務)
4. **Conditional (條件型)**: `If <condition>, the system shall <action>` (分支邏輯)
5. **Unwanted behavior (不期望行為)**: `If <condition>, the system shall prevent <unwanted action>` (安全與容錯)

> **🏗️ 建築人視角 (規範寫作法):**
>
> 1. **Ubiquitous**: **通則 (General Requirements)**。整棟大樓都要遵守的標準（如：所有混凝土設計強度 fc' ≧ 3000psi）。
> 2. **Event-driven**: **觸發條件**。「當火警訊號 (Trigger) 啟動時，防火捲門應自動下降至地面 (Action)」。
> 3. **State-driven**: **狀態條件/施工中規範**。「在進行地下室開挖期間 (State)，應全程啟動抽水機保持水位低於開挖面 (Action)」。
> 4. **Conditional**: **特殊條款**。「若使用石材地坪 (Condition)，則需在表面施作防護劑 (Action)」。
> 5. **Unwanted behavior**: **除外責任/禁條**。「當系統偵測到衝突時，不得強制刪除既有元件 (Prevent Action)」，就像「承商不得使用含石綿之材料」。

### 3.2 領域理論基礎 (Domain Theory Grounding)

優化後的提示詞不應只是文字描述，而應植根於成熟的框架：（下面的四個技術文件，我們未來會進行介紹）

- **產品力**: GTD, 艾森豪矩陣
- **使用者心理**: BJ Fogg 行為模型 (B=MAT)
- **視覺設計**: 格式塔原則 (Gestalt Principles)
- **安全架構**: 零信任 (Zero Trust)

> **🏗️ 建築人視角:**
>
> 就像設計建築不只是畫圖，要有**「建築理論」**支撐：
>
> * **產品力**: **空間機能需求 (Program)**。符合業主的使用流程。
> * **使用者心理**: **環境行為學**。使用者走進大廳時，空間尺度如何影響他的感受？
> * **視覺設計**: **形式美學/黃金比例**。立面的開窗比例是否和諧？
> * **安全架構**: **結構安全/走火避難**。就像在 `REVIT_MCP_study` 中提到的**「防火區劃」**，這是不能妥協的底層邏輯。

### 3.3 優化的六路徑

1. **診斷**: 找出原始需求中「過寬」、「缺乏觸發點」或「表達含糊」的部分。
2. **EARS 轉換**: 將每一點需求原子化。
3. **注入理論**: 根據業務領域選擇 2-4 個互補理論。
4. **提取案例**: 使用真實數據而非佔位符。
5. **結構化產出**: 按照 Role -> Skills -> Workflows -> Examples -> Formats 框架封裝。
6. **呈現驗證**: 對比優化前後的差異。

> **🏗️ 建築人視角 (設計發展流程):**
>
> 1. **診斷**: **基地分析 (Site Analysis)**。找出基地的高層差、日照不足或業主需求不明確的地方。
> 2. **EARS**: **確立設計準則 (Design Guidelines)**。將分析結果轉化為明確的設計限制。
> 3. **注入理論**: **引入設計概念 (Concept)**。定調這棟建築的風格與靈魂（如：綠建築、參數化設計）。
> 4. **提取案例**: **案例分析 (Case Study)**。參考 `REVIT_MCP_study` 中的具體案例，如「走廊寬度檢討」或「法規檢討流程」。
> 5. **結構化產出**: **繪製建照圖/施工圖 (Production)**。將所有設計變成有系統的圖紙。
> 6. **呈現驗證**: **3D 透視圖對比 (Before/After)**。讓業主看到優化前後的效果差異。

---

## 第四章：部署模式與變體討論 (即將開始)

討論如何將這些 Skill 應用到目前的 Git 專案並進行變體。

> **🏗️ 建築人視角:**
>
> 這裡討論的是**「工地介面整合」**與**「版本控制」**。
>
> `REVIT_MCP_study` 中提到 Git 的 **Fork/Clone/Pull** 流程，就像是各種不同的設計方案 (Options) 與最終定案 (Main Branch) 的整合過程。如何確保大家都在同一個**「唯一檔案 (One File / CDE)」**邏輯下工作，而不會產生 `_final_v2_new_edit` 這種檔案災難。