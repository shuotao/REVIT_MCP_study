# 在 Google Antigravity IDE 中無痛部署 Skills 指南

> 💡 **這份指南寫給誰？**
> 如果你沒有資訊工程背景，覺得在終端機（Terminal）打指令很痛苦，但又想**使用開源社群（如 daymade）已經寫好的標準化工具**來建立自己的 Skill，這篇就是為你準備的。

我們反覆強調過一個觀念：**技能（Skill）最大的價值在於「確定性」，而確定性來自於別人寫好的腳本與 SOP（巨人的肩膀），而不是每一次都跟 AI 單純對話請它重新發揮。**

在 Google Antigravity 這個視覺化的 AI 開發環境中，我們同樣要遵守這個原則。我們**不是**用聊天的方式叫 AI 隨便寫一個 SKILL.md，我們是要**把 `skill-creator` 這個別人寫好的 Meta-Skill 裝進 Antigravity**，然後讓它用標準程序幫我們產生高品質的 Skill。

---

## 觀念解析：Antigravity 中的 Skill 體系

在 Antigravity 中，Skill 就是存在於特定資料夾的檔案。總共有兩個層級：

| 範圍 (Scope)             | 資料夾位置                      | 在本案的作用                                                          |
| ------------------------ | ------------------------------- | --------------------------------------------------------------------- |
| **全域層級 (Global)**    | `~/.gemini/antigravity/skills/` | **這裡要用來放 `skill-creator`。** 讓它成為所有專案共用的強力工具。   |
| **專案專屬 (Workspace)** | `<你的專案目錄>/.agent/skills/` | **這裡用來放 `revit-code-reviewer`。** 這是屬於你當前專案的檢討規則。 |

---

## 實戰：無痛介接開源工具的三大步驟

### Step 1：將開源的 `skill-creator` 註冊為 Antigravity 的全域 Skill

我們要把 `daymade/claude-code-skills` 這個開源庫裡的工具，變成 Antigravity 取之不盡的武器庫。

1. 在你的電腦上，找到你從 GitHub 下載的 `claude-code-skills` 這個資料夾。
2. 進入其中，找到 `skill-creator` 資料夾。這個資料夾裡面有作者寫好的強大 Python 腳本（`init_skill.py`, `package_skill.py` 等）。
3. **對這個 `skill-creator` 資料夾按右鍵 → 複製。**
4. 打開你的 Antigravity 全域設定資料夾：
   - mac/Linux 請打開隱藏資料夾：`~/.gemini/antigravity/skills/`
   - （如果沒有這個資料夾，請自己新增）
5. **將 `skill-creator` 貼上到上述路徑中。**

完成！現在只要重開 Antigravity 視窗，你的 AI 就正式「學會」了如何使用這套標準生產線！你就有了一個專屬的 BIM Manager。

### Step 2：召喚 BIM 經理為你建立專案 Skill

回到你自己的專案（例如 `REVIT_MCP_study`）的 Antigravity 視窗。

現在，你**不要**自己手寫資料夾，也不要讓 AI 隨便生成。你直接在對話框對 AI 說：

> 「請執行剛剛全域安裝的 `skill-creator` 裡的 `init_skill.py` 腳本，幫我在本專案的 `.agent/skills/` 目錄下，建立一個名為 `revit-code-reviewer` 的 Skill。」

這個時候會發生什麼事？
- Antigravity 會自動去呼叫 `init_skill.py`。
- **它會精確地執行這支腳本！**（這是高確定性的操作，百分之百遵守原始開發者的標準格式）。
- 腳本執行完後，你的專案目錄裡就會自動長出包含 `SKILL.md` 模板、`references/` 等標準結構的資料夾。

### Step 3：依照標準格式填入你的領域知識

現在框架已經建好了，你只要把「靈魂」放進去：

1. 把你現有的法規（例如 `building-code-tw.md`）直接拖拉進剛建好的 `references/` 資料夾。
2. 在 Antigravity 對話框對 AI 說：
   > 「請幫我編輯剛建立好的 `revit-code-reviewer` 的 `SKILL.md`：
   > 1. YAML Frontmatter 的 `description` 請寫入：『針對 Revit 模型執行台灣建築法規檢討，包含容積檢討、防火等級檢查...』
   > 2. 將專案中的 `domain/floor-area-review.md` 內容整併到流程本文中。
   > 注意，既然我們有 `skill-creator`，請你完全遵守它制定的 YAML 格式規範。」

你可以隨時對 AI 說：「請用 `skill-creator` 裡的驗證腳本 (`quick_validate.py`) 檢查我這個 Skill 格式對不對？」

---

## 為什麼這很重要？

透過這個流程，我們完美融合了：
1. **Antigravity 的圖形易用性**：你不需要在終端機打 `claude plugin install...`，靠資料夾複製貼上和日常對話就能操作。
2. **開源工具的嚴謹性**：你不是依賴大語言模型那虛無飄渺的「自由創作能力」來寫規格，你是真金白銀地**執行了別人寫好、經得起考驗的標準腳本**。

這才是真正的技術傳承——**踩在開發者的肩膀上，省去了重造輪子的風險，然後專注產出你的 Revit 領域知識。**
