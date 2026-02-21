# 從 `/domain` 到 Skill：升級遷移指南

> 📎 本文件是 [Learning_Notes.md](./Learning_Notes.md) 第二章的延伸閱讀。
> 建議先完成第二章的 `skill-creator` 實戰後再閱讀本文。

---

## 你可能正在擔心的事

完成第二章後，你已經知道怎麼用 `skill-creator` 把 `domain/` 的知識打包成一個標準 Skill。但你的腦中一定浮出一個問題：

> **「打包好 Skill 之後，我日常還是會用 `/domain` 來記錄新的經驗啊。那這些新經驗會跑到 `domain/` 目錄，但 Skill 裝的是舊版——我是不是要定期手動同步？這不就變成雙軌維護了嗎？」**

這個擔心**完全合理**。如果不處理，確實會出問題。

---

## 問題分析：雙軌維護的風險

升級到 Skill 後，如果不調整 `/domain` 的行為，你的知識就會分裂在兩個地方：

```
知識分裂：

┌────────────────────────────────────────────────┐
│  domain/                                        │
│  ├── fire-rating-check.md      ← 舊的（已封裝） │
│  ├── corridor-analysis.md      ← 舊的（已封裝） │
│  └── new-experience-2026.md    ← 新的！但 Skill │
│                                   不知道它存在   │
├────────────────────────────────────────────────┤
│  revit-code-reviewer/                           │
│  ├── SKILL.md                  ← 舊版內容       │
│  └── references/               ← 舊版法規       │
│       └── building-code-tw.md                   │
└────────────────────────────────────────────────┘

Claude 觸發 Skill 時讀的是 SKILL.md → 永遠看不到 domain/ 中的新經驗
```

更嚴重的是，如果有**多位使用者**在不同時間點加入：

| 使用者    | 進入時間  | 習慣                                | 知識存在位置             |
| --------- | --------- | ----------------------------------- | ------------------------ |
| A（元老） | 2025 年   | 用 `/domain` 產出 `domain/*.md`     | `domain/`                |
| B（中期） | 2026 年初 | 學了 Skill，但也還在用 `/domain`    | `domain/` + Skill 各一半 |
| C（新人） | 2026 年中 | 只認識 Skill，不知道 `domain/` 存在 | Skill                    |

→ **三個人的知識散落在不同的地方，沒有單一的事實來源 (Single Source of Truth)。**

---

## 解法：不是「替換」，而是「重新導向」

核心原則：**不要改變使用者的操作習慣，只改變背後的輸出目的地。**

```
升級前：
  /domain → 寫入 domain/*.md → GEMINI.md 觸發規則手動指向它

升級後（重新導向）：
  /domain → 寫入 revit-code-reviewer/SKILL.md 或 references/ → 自動生效
```

使用者打 `/domain` 的動作**完全不變**。但 AI 把新知識直接更新到 Skill 結構中，而不是建立新的 `domain/*.md` 檔案。

### 具體做法：修改 GEMINI.md 中的 `/domain` 指令定義

**升級前**（你現在的 GEMINI.md）：

```markdown
| /domain | SOP 轉換：將成功的對話工作流程轉換為標準 SOP 格式的
           domain/*.md 檔案。步驟：(1) 確認對象 (2) 提取工具和步驟
           (3) 用 YAML frontmatter + MD 格式撰寫 (4) 儲存至 domain/
           (5) 更新觸發表 |
```

**升級後**（修改為）：

```markdown
| /domain | Skill 更新：將成功的對話工作流程更新到 revit-code-reviewer
           Skill 中。步驟：
           (1) 確認對象
           (2) 提取工具和步驟
           (3) 判斷更新位置：
               - 新的檢討流程 → 追加到 SKILL.md 的對應流程章節
               - 新的法規資料 → 更新 references/building-code-tw.md
               - 修正既有流程 → 直接編輯 SKILL.md 中的對應步驟
           (4) 儲存至 revit-code-reviewer/ 目錄
           (5) 不需要更新觸發表（description 已自動處理） |
```

**對使用者來說的差異：**

|                | 升級前                                          | 升級後                                                         |
| -------------- | ----------------------------------------------- | -------------------------------------------------------------- |
| 使用者輸入     | `/domain`                                       | `/domain`（一樣）                                              |
| AI 的回應      | 「我已將此流程儲存到 `domain/corridor-xxx.md`」 | 「我已將此流程更新到 `revit-code-reviewer` Skill 的流程 C 中」 |
| 檔案變化       | 新增一個 `domain/*.md`                          | 修改 `SKILL.md` 或 `references/` 中的內容                      |
| 需要額外維護嗎 | 需要手動更新 GEMINI.md 觸發表                   | **不需要**（Skill 的 description 自動生效）                    |

---

## 其他斜線指令的重新導向

不只是 `/domain`，你原有的四個指令都可以無痛切換：

| 指令       | 升級前的行為            | 升級後的行為                                              | 使用者感受                     |
| ---------- | ----------------------- | --------------------------------------------------------- | ------------------------------ |
| `/domain`  | 寫入 `domain/*.md`      | 更新 `SKILL.md` 或 `references/`                          | **操作一樣**                   |
| `/lessons` | 追加到 GEMINI.md 末尾   | 追加到 SKILL.md 的「注意事項」段落                        | **操作一樣**                   |
| `/review`  | 檢查 GEMINI.md 是否太胖 | 檢查 SKILL.md 是否超過 5,000 字，過長的搬到 `references/` | **操作一樣，但有明確字數指標** |
| `/check`   | 對比當前做法和過去經驗  | 對比當前做法和 SKILL.md 中的 SOP                          | **操作一樣**                   |

---

## 一次性部署升級步驟

以下是初學者可以照著做的升級步驟：

### 第 1 步：盤點現有知識

確認你的 `domain/` 目錄中有哪些檔案。在你的 REVIT_MCP_study 中，這些就是你的知識資產：

```
domain/
├── fire-rating-check.md            ← 防火等級檢查 SOP
├── corridor-analysis-protocol.md   ← 走廊分析 SOP
├── floor-area-review.md            ← 容積檢討 SOP
├── element-coloring-workflow.md    ← 元素上色 SOP
├── wall-check.md                   ← 牆壁方向檢查
├── room-boundary.md                ← 房間邊界計算
├── qa-checklist.md                 ← 品質檢查清單
├── path-maintenance-qa.md          ← 路徑維護 QA
└── references/
    └── building-code-tw.md         ← 台灣建築法規摘要
```

### 第 2 步：用 `skill-creator` 打包成 Skill

按照第二章 Step 1～7 的流程，把上述 `domain/` 的內容整合成一個 `revit-code-reviewer` Skill。最終目錄結構：

```
revit-code-reviewer/
├── SKILL.md                        ← 整合所有 SOP 的核心指令
└── references/
    └── building-code-tw.md         ← 法規參考資料
```

### 第 3 步：安裝 Skill

將 Skill 安裝到你的 Claude Code 環境：

```bash
# 如果使用 ccpm（Claude Code Plugin Manager）
ccpm install revit-code-reviewer

# 或者手動放到 Skill 目錄
# 位置取決於你的 AI 助手環境
```

### 第 4 步：修改 GEMINI.md 的斜線指令定義

將 `/domain` 的輸出目的地從 `domain/` 重新導向到 `revit-code-reviewer/`。具體修改方式參考上方「具體做法」段落。

### 第 5 步：將 `domain/` 標記為歸檔

在 `domain/` 目錄中新增一個醒目的說明：

```markdown
<!-- domain/README.md 更新為 -->

# ⚠️ 本目錄已遷移至 Skill

本目錄中的知識文件已整合到 `revit-code-reviewer` Skill 中。

- **查看最新版本**：請參考 `revit-code-reviewer/SKILL.md`
- **新增經驗**：請使用 `/domain` 指令（已自動導向 Skill）
- **本目錄保留為歷史存檔**，不再更新
```

### 第 6 步：驗證

對 Claude 說以下幾句話，確認 Skill 正常運作：

1. 「幫我檢查走廊寬度」→ 預期：Claude 載入 Skill，按照流程 C 執行
2. 「幫我做防火等級檢查」→ 預期：Claude 載入 Skill，按照流程 B 執行
3. `/domain`（描述一段新經驗）→ 預期：Claude 更新 SKILL.md，而非建立新的 `domain/*.md`

---

## 升級後的持續維護

升級完成後，你的日常工作流程幾乎不會改變：

```
升級後的日常（使用者視角）：

1. 在 Revit + MCP 中工作
2. 發現新的經驗 → /domain → AI 自動更新 SKILL.md
3. 記錄教訓 → /lessons → AI 追加到 SKILL.md 注意事項
4. 定期回顧 → /review → AI 檢查 SKILL.md 是否需要整理
5. 執行檢討 → 直接說「幫我檢查走廊寬度」→ Skill 自動觸發

唯一的改變：不再需要維護 GEMINI.md 的觸發規則表了。
```

### 需要手動做的事有多少？

| 動作                                 | 頻率                 | 需要的技術能力         |
| ------------------------------------ | -------------------- | ---------------------- |
| 日常使用（`/domain`、`/lessons` 等） | 每天                 | 零——和以前一樣打指令   |
| 檢查 SKILL.md 有沒有太長             | 每月                 | 低——`/review` 會提醒你 |
| 打包新版本（`package_skill.py`）     | 需要分享給別人時才做 | 中——但個人使用不需要   |

---

## 關於斜線指令要不要改名

| 選項                        | 優點               | 缺點                 |
| --------------------------- | ------------------ | -------------------- |
| **A：保留 `/domain` 名稱**  | 老使用者零學習成本 | 名稱和實際行為有落差 |
| **B：改名 `/skill-update`** | 名稱精確反映新行為 | 老使用者要重新習慣   |

**建議選 A**：保留 `/domain` 名稱，但讓 AI 在執行時自然提示——「正在將此經驗更新到 `revit-code-reviewer` Skill 中…」。使用者會在操作過程中自然理解背後的變化，不需要額外學習。

---

## 總結

```
升級的核心原則：

  ┌─────────────────────────────────────────────────┐
  │                                                   │
  │   不要改變使用者的操作習慣                         │
  │   只改變背後的輸出目的地                           │
  │                                                   │
  │   /domain 還是 /domain                            │
  │   但輸出從 domain/*.md → SKILL.md                  │
  │                                                   │
  └─────────────────────────────────────────────────┘

  ✅ 操作習慣不變
  ✅ 沒有雙軌維護
  ✅ 新舊使用者都能無痛使用
  ✅ 知識有單一事實來源（Single Source of Truth）
```
