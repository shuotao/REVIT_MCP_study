# 從知識族譜到個人 LLM Wiki：一場人與 AI 的協作設計實錄

> 2026-06-12，使用者（專案維護者）與 AI agent（Claude Code）在一天之內，
> 從一個模糊的構想出發，經過七輪觀念攻防，最後把「個人 LLM Wiki」
> 完整落地上線。本文忠實記錄這個過程——包括誰提了什麼、誰被誰修正、
> 哪些直覺贏過了設計、哪些數據改變了判斷。

---

## 一、緣起：一個構想，和一個正確的第一個問題

使用者最初的提案是視覺化的：用 Karpathy 的概念，把 HTML 教學文件加上
各領域老師貢獻的 domain SOP，做成一個 Obsidian 式的「知識族譜」，
嵌進 BIM_MCP 知識站的入口，讓老師們看見族譜的應用模式與關係，
甚至讓他們做出自己的族譜。

但他沒有說「做」，而是先問：**「做這件事情有沒有價值，請你評估？」**

AI 的評估指出了一件多數團隊不具備的事實：這個 repo 的族譜資料底層
其實已經存在——44 個 domain 檔的 frontmatter 本來就有 related、
referenced_by、references、tags 欄位，而且 QAQC 已經在守護這些連結的
完整性。要做的是視覺化，不是從零建資料。同時誠實列出保留：圖會偏稀疏、
Obsidian 該是比喻不是依賴、「老師自建族譜」是另一個量級的東西。

## 二、先看數據再決定：密度報告

AI 的評估說「值得做」，使用者沒有照單全收。他的回覆是：

> 「先掃一遍給我密度報告再決定，因為真的動手下去有把 domain 稀釋的風險。」

這是整場協作的第一個方法論時刻：**對載重判斷，要求數據，不接受印象。**

實掃結果推翻了 AI 自己先前的印象（「related 只有個位數」）：
實際有 27 條 domain↔domain 血緣邊、42 條 skill→domain 引用邊，
連結度最高的節點不是工作流程，而是 building-code-tw（法規）、
lessons（經驗）、qa-checklist（治理）——族譜自然長出了「祖先層」，
「法規是根、SOP 是枝、skill 是果」的敘事不用人工編排就成立。

掃描還順手抓到一個真實缺口：stair-compliance-check.md（樓梯法規檢討）
完全孤立，連理應引用它的 building-compliance skill 都沒有接上——
**圖還沒畫，建圖的資料就先抓出了一個該連而未連的洞。**

對稀釋風險的回答也由此確立：所有的邊都從現有 frontmatter 唯讀推導，
一個 domain 檔都不用改。稀釋只發生在「往 domain 塞圖譜語法」或
「為了圖好看硬湊關聯」——兩者都畫為紅線。

## 三、使用者的第一次轉向：從中央圖譜到個人 vault

數據支持中央圖譜，但使用者在這裡提出了更深的東西：

> 「假設是要變成大家能夠手動操作，並且是在自己的本端把資料拉在本端，
> 在自己的 Obsidian 的三層資料夾 /raw /wiki /lint 製作自己的內部知識……
> 每次 pull 下來就可以更新 raw 來被要求 ingest……
> 因為每個人的使用上有不同，Obsidian 製造出來的 wiki 會更適合每個人。」

AI 的評估承認這個架構在「知識內化」上優於中央圖譜——個人筆記是
個人的壓縮（compression），結構技師和消防審查者對同一份 SOP 的
心智地圖本來就該不同；而且三層結構天然解掉了稀釋問題：wikilink 和
個人分類活在 /wiki，raw 一個字不動。

但 AI 也提出三個保留：受眾門檻（中央圖譜仍是零安裝的招募入口，
兩者是漏斗的兩層不是二選一）、漂移風險（需要溯源紀律）、
以及一個懸而未決的問題——**/lint 到底是什麼？**

## 四、Karpathy 原文進場：「官方本來有三層不是嗎？」

使用者沒有直接回答 /lint 的定義，而是把 Karpathy 的 LLM Wiki gist
全文貼了出來，指出 AI 把「層」和「操作」混在一起了：
三層是 **Raw / Wiki / Schema**，而 **Lint 是三個操作之一**，不是資料夾。

原文一攤開，浮現了整場討論最重要的一個對照——

**上游 repo 本來就是一個 Karpathy 式的公共 LLM Wiki：**

| Karpathy 概念 | REVIT_MCP 現況 |
|---|---|
| Raw sources | 建築法規、Revit 實機資料 |
| The wiki | domain/*.md（44 頁、frontmatter 互相引用） |
| The schema | CLAUDE.md（連檔名都一樣） |
| index.md | CLAUDE.md 觸發關鍵字表 |
| log.md（可 grep 的條目格式） | log/YYYY-MM.md（條目前綴格式完全相同） |
| Lint 操作 | verify-qaqc.ps1（制度化了） |

所以這不是「導入一個新模式」，而是「**把一個已經在運轉的公共 wiki
複製成個人版**」。老師不用學新東西。

## 五、發行模式：文件即產品

下一個問題是怎麼交付。使用者問：「應該是寫在網頁上？在 BIM_MCP 開一個
Obsidian 的專題？寫好直接讓老師們複製？然後就可以開始用嗎？」

答案是肯定的，而且這正是 Karpathy gist 自己的發行哲學——
"designed to be copy pasted to your own LLM Agent"。
一頁網頁、一鍵複製的「想法檔」（idea file），老師貼給自己的 agent，
agent 跟他協作把 vault 長出來——每個人長得不一樣，正是重點。

但通用 gist 不夠，網頁版必須加上 REVIT_MCP 特化的五件事：
raw 的定義（repo clone）、溯源紀律（source + source_version）、
MCP 加成（query 對象包含活的 Revit 模型，審查結果 file 回 wiki）、
回饋上游的路（/hj-pr-proposal）、起手 schema 種子。

頁面當天寫完上線。一個值得記錄的插曲：QAQC 第一輪就抓到新頁面裡
「2-3 個 Domain 檔」誤觸了總數宣稱的偵測格式——**守門的防線連寫它的
作者都不放過，這正是它該有的樣子。**

## 六、一致性課題：「怎麼 fix 大家的內容能夠逼近一致？」

頁面上線後，使用者提出了整場討論最尖銳的工程問題：

> 「我相信現在的模型都可以做出這樣的架構，但是在呼叫上面都會有些微落差。
> 今天由 Gemini CLI / Codex CLI 做，同時在驅動不同等級的模型，
> 都會做出略為有差異的狀態，該怎麼 fix 大家的內容和規則逼近一致？」

並且要求把 Karpathy 原文全文用三引號附在 idea file 尾部，
避免各家模型憑記憶腦補。

AI 的回答濃縮成一句話：**「一致性靠複製，不靠模型自律。」**
變異只發生在「生成」的地方——把該固定的部分改成「複製」，變異就不存在：

1. **Schema 必須完全一致** → 做成上游版本化範本
   （templates/personal-vault/VAULT-CLAUDE.md，schema_version），
   idea file 命令 agent 逐字複製、只填 Personal 區
2. **結構與格式必須一致** → 建置完 agent 必須跑驗收清單逐項回報
3. **概念理解逼近一致** → Karpathy 原文全文當錨點
4. **wiki 內容刻意不一致** → 個人壓縮是目的，差異是 feature 不是 bug

最巧的一環：範本住在 repo 裡，老師每次 pull 自動拿到新版，
lint 操作比對 schema_version 落後就提示升級——
**用來保鮮知識的同一個 pull + ingest 循環，順便保鮮了規則本身。**

## 七、使用者的第二次轉向：vault 住進 repo（提案勝過原設計）

AI 原本的設計是「vault 在外、clone 在內」（MyBIMVault/raw/ 是
另一份 clone）。使用者看著結構圖，問出了三個問題：

> 「raw 的來源要怎麼跟 clone 這個專案綁在一起？
> 這樣 REVIT_MCP 的資料夾結構會不會被破壞掉？
> Obsidian 怎麼能夠知道要隨時去拉取 pull 下來的這個關係？」

然後提出反提案：**vault 直接放進 REVIT_MCP repo 裡面，用 .gitignore
擋住個人區。** 並問：「這樣會不會太複雜且雜亂？」

AI 推演後的結論：**不是更複雜，是更簡單，而且修掉了原設計的兩個真實缺陷**——

- 原設計對已用 MCP 的老師意味著第二份 clone（兩處要 pull、版本會歪）；
  in-repo 方案下 raw 就是 repo 本身，綁定問題直接消失
- 原設計下 vault agent 拿不到 repo 根目錄的 .mcp.json，
  「對活模型提問、結果存回 wiki」這個主打功能會斷掉；
  in-repo 方案讓 vault agent 免費獲得全部 Revit MCP 工具
- 附帶紅利：Obsidian 開 repo 根目錄，graph view 同時呈現公共 domain
  與個人 wiki——**最初想做的知識族譜，就這樣在每個人本地免費實現**

三個風險也被明文化為規則：git clean -x 會刪掉 vault（禁令＋vault 自建
git 備份）、雙 CLAUDE.md 管轄（雙向護欄條款）、Obsidian 雜訊
（Excluded files 設定）。複雜度全部移到上游維護端，老師端從
「管兩個資料夾」變成「管一個」。範本升版 v1.1。

## 八、落地的最後一哩：使用者以「第一位老師」的身分連環提問

架構定了，使用者開始用「即將動手的人」的視角連續提問——
每一題都精準命中未來老師會踩的坑：

**「Obsidian 是開 REVIT_MCP 還是先建 vault 資料夾再貼 prompt？」**
→ 釐清：老師什麼都不用建，vault/ 是 agent 生成的；
Obsidian 開整個 repo。並揪出命名撞車：Obsidian 說的「vault」
（它打開的資料夾）和我們的 vault/ 子資料夾不是同一個東西。

**「不從 vault 資料夾進去，Obsidian 會不會讀錯 CLAUDE.md？」**
→ 釐清：Obsidian 根本不讀 CLAUDE.md——那是給 AI agent 的指令檔，
對 Obsidian 只是一篇普通筆記。真正讀它的 agent 那邊，
兩份 CLAUDE.md 的管轄權已有雙向條款。

**「ingest 和 lint 的指令寫在哪個 CLAUDE.md？會不會錯亂？」**
→ 誠實承認一個縫隙：全新 session 第一句喊「ingest」，agent 可能
還沒讀到 vault/CLAUDE.md 的定義。保險絲：根目錄憲法的指路條款＋
「新 session 第一句帶路標」的習慣。

**「AI 怎麼知道現在是問 wiki 還是要操作 Revit？是不是該有 /query？」**
→ 釐清：模式不是互斥而是疊加（「跑排煙檢討，結果存進我的筆記」
就是 MCP 行動＋wiki 歸檔一次完成），意圖判斷靠語意；
但確定性入口有價值——當場新增三個薄轉接指令
/ingest、/lint、/wiki（權威定義仍在 vault/CLAUDE.md，
指令只負責導向與護欄）。

**「哪些是老師 prompt 生成的？哪些是 pull 下來的？push 上去要考慮合理性。」**
→ 最終分界表：vault/ 與 .obsidian/ 本地生成、永不 push；
母版、閘門、斜線指令隨 pull 而來。三個指令的發布前合理性核查
（薄轉接、安全降落、scope guard、語言政策、計數無漂移）逐項過。

這一段有個隱形機制在運轉：**使用者每問一題，答案就回填成教學頁的
FAQ**——Q1 命名撞車、Q2 誰在讀 CLAUDE.md、Q3 意圖判斷與斜線指令。
第一位使用者的困惑，直接變成了後來者的教材。

## 九、方法論回顧：這場協作是怎麼運作的

回看整天，雙方的分工有清楚的形狀：

**使用者貢獻的，是方向、直覺與情境。**
- 最初的構想（族譜）與最終的目的（正向循環：集體→個人→變體→回饋開源）
- 兩次關鍵轉向都來自他：個人 vault 取代中央圖譜、vault 住進 repo
- 「先給我密度報告」「貼原文防腦補」「push 要考慮合理性」——
  三次把討論從「聽起來不錯」拉回「拿證據來」
- 連環情境提問，逼出每一個落地細節

**AI 貢獻的，是驗證、結構與機制。**
- 用實掃數據取代印象（密度報告推翻了自己的「個位數」預估）
- 把模糊概念對齊到可執行結構（上游即公共 wiki 的對照表、
  「一致性靠複製」、公私血管流程圖）
- 誠實列風險並給對策，包括承認自己原設計的缺陷
  （第二份 clone、MCP 工具斷線）並採納使用者的反提案
- 每一步用 QAQC 紅綠驗證收尾，每一個決策記入 log

**而把兩者黏在一起的，是一個重複出現的節奏：**

```text
提案 → 評估（誠實列風險）→ 數據驗證 → 決策 → 實作
→ QAQC 紅綠 → 上線 → 使用者以使用者身分提問 → 回填教材 → 下一輪
```

## 十、一天的產出清單

| 產出 | 說明 |
|---|---|
| 密度報告 | 79 條真實邊、5 個孤點、孤立的 stair-compliance-check 缺口 |
| templates/personal-vault/VAULT-CLAUDE.md | 版本化 schema 母版（v1.1，Fixed Core + Personal） |
| .gitignore 閘門 | /vault/ 與 /.obsidian/，個人區永不被 push |
| CLAUDE.md 新章節 | Personal Vault Protection（雙向護欄） |
| /ingest /lint /wiki | 三個薄轉接斜線指令，pull 完即用 |
| QAQC 新檢查 | 1-4 vault 閘門保護（3 項） |
| 教學頁 personal-llm-wiki.html | 8 步精靈式導覽、一鍵複製 idea file（含 Karpathy 原文錨點）、3 則 FAQ |
| 本文 | 過程本身的紀錄 |

最後值得記下的一句話，來自討論中對整個構想的定位：

> 由集體智慧，進入到個人，再由個人的深化和獨立變體後，
> 再次產生新的知識，並且回饋到共同智慧的開源上。

這不只是這個功能的設計目標——它也是這場對話本身的運作方式。

---

*actor: claude-fable-5 (via Claude Code)，與使用者共同回顧。*
*相關 commit：42e7efa → 9d59ae4（2026-06-12）。*
