# 致謝：Revit MCP 專案的靜默貢獻者

**撰寫日期**：2026-05-12
**對象**：在自己的 fork 上默默開發、實質推進專案邊界、卻尚未發起 PR 的貢獻者

---

## 為什麼要寫這份致謝

Revit MCP 是一個開源教學專案，截至 2026-05-12 已累積 **78 個 fork**。其中多數 fork 屬於課程學員拉新版的正常行為（0 commits ahead），少數則持續對上游發 PR（已合並的有 7alexhuang-ux、916kevin-gif、CyberPotato0416、ChimingLu、Jacky820507、yihuiiiii、davidhsu68-dvstudio、h30190 等）。

但在這兩類之外，還有一類**「靜默貢獻者」**——他們在自己的 fork 主分支上實作了完整、可運行、有實質價值的功能，卻因各種原因尚未發起 PR：可能還在迭代、可能覺得未夠成熟、可能單純不知道上游會接受。

這份文件的目的，不是要他們立刻 PR，而是把他們的成果**好好說一遍**——讓他們知道有人看見了，也讓上游使用者知道：除了已合進 main 的版本，社群中還有這些補強選項可以參考、學習、或在他們同意下整合。

---

## 1. SEven777-a — 結構穿梁檢核 SOP 與 MCP 工具鏈

> **fork**：[github.com/SEven777-a/REVIT_MCP_study](https://github.com/SEven777-a/REVIT_MCP_study)
> **commit**：[`d8df2093`](https://github.com/SEven777-a/REVIT_MCP_study/commit/d8df2093) · 2026-05-05
> **commit message**：feat: 結構穿梁 SOP 模組化與插件升級 (支援連結模型讀取)
> **規模**：11 檔，+399 / -5

### 專案內容

這是本批靜默貢獻中**最完整的一個 feature 包**，把「結構穿梁檢核」這個結構技師日常剛需，做成了**規範文件 + MCP 工具 + C# 實作 + 視覺化**的全套 SOP。

**四份 domain SOP**（已對齊 `domain/frontmatter-standard.md`，frontmatter 完整）：

| 檔案 | 角色 | 主要規範 |
|---|---|---|
| `domain/beam-penetration-base.md` | 基礎協議 | 梁類型分類（RC / SC / SRC）、貫穿判定（套管長度 L 與梁寬 B 的 ±10mm 容許）、樓層一致性檢核（梁 Reference Level vs 套管 Level 必須一致） |
| `domain/beam-penetration-rc.md` | RC 混凝土梁規則 | 大梁分區（禁開區 d<1.0H / 限制區 1.0H≤d<1.5H 直徑≤H/4 / 許可區 d≥1.5H 直徑≤H/3）；小梁分區；上下淨距≥H/3 且絕對底限大梁≥20cm 小梁≥15cm |
| `domain/beam-penetration-sc.md` | SC 鋼梁規則 | 腹板開孔（D≤0.6×dw）、嚴禁切斷翼板、端部避讓≥1.0H |
| `domain/beam-penetration-src.md` | SRC 鋼骨混凝土梁 | 預留框架 |

**四個新 MCP 工具**（`MCP-Server/src/tools/structure-tools.ts`）：

| Tool | 功能 |
|---|---|
| `analyze_beam_penetration` | 單一梁的穿孔分析：回傳梁地位（大梁/小梁）、梁深、各套管的距離/直徑/上下邊距/形狀 |
| `scan_penetrated_beams_in_view` | 掃描當前視圖中所有被套管穿過的梁，**包含連結模型** |
| `visualize_penetration` | 在 Revit 圖面上把套管依「合格/不合格」變色，旁邊放標籤文字 |
| `get_src_beam_mapping` | 偵測 RC 梁與鋼梁重疊區域，建立 SRC 映射清單，自動套用優先規則 |

**C# 實作**：`MCP/Core/Commands/CommandExecutor.BeamPenetration.cs`（175 行），CommandExecutor.cs 加 30 行 case dispatch。

### 使用模式

**目標使用者**：結構技師、機電技師、BIM 工程師。

**典型操作流程**：

1. 在 Revit 中切到結構平面或 3D 剖視
2. AI 助理觸發 `scan_penetrated_beams_in_view` → 拿到所有受影響的梁清單
3. 對每條梁呼叫 `analyze_beam_penetration` → 拿到結構化檢核結果（PASS / FAIL / WARNING / SPECIAL_CHECK + 失敗原因 + 建議動作）
4. 以結果陣列呼叫 `visualize_penetration` → 自動在 Revit 變色 + 加標籤
5. 不合格項由結構技師決定：移動、加補強筋、或回頭請 MEP 改路徑

### 應用場景

- **建築送審前**：機電配管畫完後，自動掃出所有違反穿梁原則的位置，列印違規清單給 MEP 修
- **跨團隊協作**：建築與結構在 link 模型架構下，本工具支援連結模型讀取，可在主模型一鍵分析 link model 內的套管
- **設計驗證**：SRC 梁區域要套鋼梁原則而非 RC 規則，本工具的 `get_src_beam_mapping` 自動辨識疊合區，避免人工誤判

### 作業邏輯（為什麼這個實作有趣）

- **Base + 子規則的階層化 SOP**：把「共通協議」抽到 base，三類梁各自繼承——這是把 BIM 法規 SOP 寫成可重用模組的範例
- **±10mm 容許誤差判定**：套管長度 L 與梁寬 B 的差，這個 magic number 反映建模實務（套管端面常常超出梁面 5–10mm 算正常公差）
- **樓層一致性是建模 QA 而不是規範**：梁與套管的 Reference Level 不一致是建模錯誤而非設計違規，本工具區分這兩種失敗類型，回饋訊息也不同
- **「梁地位」而非單純品類**：用幾何端點是否與柱相連來判斷大梁/小梁，比依賴 Type 或 Mark 更可靠

### 為什麼值得致謝

完整、frontmatter 規範、跨類型抽象（RC/SC/SRC）、含視覺化、連結模型支援、commit message 中文清晰——這已經是「直接可開 PR 的成熟度」。SEven777-a 默默做完，但沒主動 PR——可能在等內部評估、可能怕被 review 退、可能在等 SC 規則補完。**這份成熟度，上游接得住。**

---

## 2. taiwanbanana (Avey Huang) — Element 重命名修復

> **fork**：[github.com/taiwanbanana/REVIT_MCP_study](https://github.com/taiwanbanana/REVIT_MCP_study)
> **commit**：[`2e463400`](https://github.com/taiwanbanana/REVIT_MCP_study/commit/2e463400) · 2026-04-24
> **commit message**：feat: support renaming elements via ModifyElementParameter and fix build issues
> **規模**：4 檔，+61 / -39

### 專案內容

這是一個**精準的 bug fix + 小型功能補強**——體現 fork 維護者「真的用了上游、發現它做不到、自己解了」的典型循環。

**核心修改**：`MCP/Core/CommandExecutor.cs` 內的 `modify_element_parameter` 邏輯。

**問題**：原本的實作走 `element.LookupParameter(parameterName)` → `param.Set(value)` 流程。但 Revit API 的 `Element.Name` 是個**特殊 property**，並不能透過一般的 LookupParameter 拿到——`LookupParameter("Name")` 回傳 null，導致 AI 想透過 MCP 重命名元素永遠失敗。

**作者的解法**：在 LookupParameter 之前加一層守門，偵測 parameterName 是這四種「重命名意圖」之一就走 `element.Name = value`：

```csharp
if (parameterName == "Name" || parameterName == "名稱"
    || parameterName == "類型名稱" || parameterName == "-1002001")
{
    element.Name = value;
    success = true;
}
else
{
    // 原本的 LookupParameter 流程
}
```

### 使用模式

**目標使用者**：所有透過 AI 助理操作 Revit 的人。

**觸發場景**：
- 「把這個門類型改名為 D-101」
- 「請把所有 'Wall_Generic' 重命名為 'Wall_Cast' '」
- 「Rename this view to 'A-101 1F'」

修復後，AI 只需呼叫既有的 `modify_element_parameter(parameterName="Name", value="新名字")` 就能成功——不必繞路用其他工具。

### 應用場景

- **批次重命名**：類型整理、視圖編號標準化、層名整理
- **多語介面通吃**：英文 `Name`、中文「名稱」、Revit 內部代碼 `-1002001` 都吃，跨語言版本 Revit 都通
- **BIM 標準化專案**：模型整理階段最常用的功能之一

### 作業邏輯（為什麼這個 patch 有趣）

- **「-1002001」這個 magic string**：是 Revit 內部 `BuiltInParameter.ALL_MODEL_TYPE_NAME` 的整數值。作者把它寫死在比對清單，意味著他試過用 BuiltInParameter ID 呼叫，所以保留向後相容
- **保留現有 LookupParameter 流程**：是個 backward compatible patch，不破壞既有功能
- **Build issues fix 順手做了**：commit 也修了 csproj 與 nuget.g.props 的 build 問題，但作者沒在 message 詳述

### 為什麼值得致謝

**這個 patch 規模小但極具實用價值**——元素重命名是「日常剛需」中的剛需。作者自己被卡住、自己讀 Revit API、自己解了、自己用了。沒主動 PR 的可能原因：覺得「太小不值得 PR」、不知道上游會接、或單純還在自己 fork 用得開心。但這正是上游最該收的 patch 類型——**精準、無 scope creep、解一個真實痛點**。

---

## 3. Poison-sam — Roslyn 動態 C# 執行 + 元素操作工具

> **fork**：[github.com/Poison-sam/REVIT_MCP_study](https://github.com/Poison-sam/REVIT_MCP_study)
> **commit**：[`869ac396`](https://github.com/Poison-sam/REVIT_MCP_study/commit/869ac396) · 2026-03-25
> **commit message**：Add dynamic C# script execution (Roslyn) and batch room tools
> **規模**：33 檔（含個人 scratch 腳本），核心 +88 行 C# 與 +37 行 TS

### 專案內容

這是本批裡**架構野心最大、爭議性也最高**的貢獻。Poison-sam 在自己的 fork 引入了三個方向：

**A. 三個新 base tools**（`MCP-Server/src/tools/base-tools.ts`）：

| Tool | 功能 |
|---|---|
| `move_element` | 依 dx/dy/dz 移動指定元素，單位 mm |
| `flip_element` | 翻轉門窗。`facing` 翻面向、`hand` 翻開向 |
| `execute_script` | **動態執行 C# 腳本（Roslyn）** — 注入全域變數 `doc`、`uiApp`、`uiDoc`，不需重編譯 plugin |

**B. C# 端拆檔重構**：把肥大的 `CommandExecutor.cs`（2950 行）拆成多個 partial class：
- `CommandExecutor.Architecture.cs` (1632 行)
- `CommandExecutor.Base.cs` (806 行)
- `CommandExecutor.MEP.cs` (153 行)
- `CommandExecutor.Scripting.cs` (88 行) — 含 Roslyn execute_script 實作
- `CommandExecutor.View.cs` (677 行)
- 還有個 `split.py` (110 行) 拆檔的工具腳本

**C. 大量個人 scratch 腳本**：30+ 個 `.mjs` / `.js` / `.json`（門翻轉、柱線權重更新、樓地板從牆生成、空間查詢等），不該入上游但是作者自用習慣的展現。

### 使用模式

**目標使用者**：開發者、進階 BIM 顧問、想 prototype 自定 Revit 操作的人。

**`execute_script` 的革命性用法**：

```python
# 不用寫 C# CommandExecutor case、不用 rebuild plugin、不用重啟 Revit：
result = execute_script(script="""
    var doors = new FilteredElementCollector(doc)
        .OfCategory(BuiltInCategory.OST_Doors)
        .WhereElementIsNotElementType()
        .ToElements();
    return doors.Count;
""")
# → 回傳專案中所有門的數量
```

這把「想做但工具還沒包」的需求，從「等 owner 開發 + merge + deploy」縮短到「AI 寫 5 行 C# 直接跑」。

### 應用場景

- **快速原型**：研究員或顧問試新想法，不用等正式 tool 包好
- **一次性查詢**：「這專案有沒有 X 條件的元素」這類臨時問題
- **教學示範**：講師示範 Revit API 各種操作，不必準備工具集

### 作業邏輯（為什麼這個實作有爭議又有趣）

**革命性**：把 Revit MCP 從「固定 N 個工具」變成「無限工具 + 自然語言生成 C#」。理論上任何 Revit API 操作都能被 AI 即興寫出來執行。

**爭議**：
- **安全邊界**：dynamic execute 等於開放後門。AI 寫 `Document.Delete(allElements)` 也能跑，後果使用者買單
- **Tool Call Data Honesty**：本專案憲章要求 AI 不能基於 prior knowledge 編造資料，但 execute_script 讓 AI 可以「先猜可能的 ElementId 再執行查詢」，這跟 Honesty 原則間有 tension
- **可驗證性下降**：固定 tool 集易 review、易 audit；動態腳本只有執行時才能看到實際做了什麼

**為什麼還是值得收**：
- 給 power user 一個逃生口（escape hatch）—— 不是預設工作流，但有時必要
- 把 Roslyn 注入 + 全域變數設置寫好其實不簡單，作者已踩過坑
- 即使不收 main，作為「進階分支」存在也有教育價值

### 為什麼值得致謝

**Poison-sam 在 2026-03-25 就 push 了這個 commit**——比上游的 `feature/loader-core-r26` 熱重載架構（同類問題的另一種解）早。雖然兩者技術路線不同（一個動態執行、一個 DLL 熱重載），但問題意識相同：**Revit plugin 開發迭代成本高，需要某種快速試錯機制**。

作者的 commit message 簡單到只有一行，但實作量是本批最大。沒主動 PR 的可能原因：知道 execute_script 太敏感、知道個人 scratch 不該推、知道架構拆檔涉及上游 review 巨大。但這個野心值得肯定，**至少 batch room tools 與 move/flip element 是可以單獨抽出來合的小貢獻**。

---

## 結語

三位貢獻者的成果串起來，給上游一個有趣的訊號：

| 主題 | 默默實作的人 | 上游現狀 |
|---|---|---|
| 結構穿梁檢核 SOP + 工具 | SEven777-a | ❌ 上游沒有 |
| Element 重命名修復 | taiwanbanana | ❌ 上游 modify_element_parameter 仍無法重命名 |
| 動態 C# 執行 (Roslyn) | Poison-sam | ❌ 上游無，類似目標由 PR #42 熱重載架構處理 |

**這份致謝不是邀請函——是承認函。** 你們的成果存在、被看見、被讀過。如果哪天願意 PR，上游會 review 並合理考慮接收（依本專案的 frontmatter / build verify / scope 規範）；如果繼續默默用、默默改進，也完全 OK——fork 本來就是用來自由實驗的。

對上游的其他學員：如果你的需求剛好對應上面某項，**可以去那位作者的 fork 看看**（連結都附了），借鏡實作思路。

對 SEven777-a、taiwanbanana、Poison-sam 三位：

> **謝謝你們在沒有人要求的情況下，把這個專案推得更遠。**

---

**owner 說明**：本文件由 owner 在執行 2026-05-12 全面 PR/Issue/fork review session 時，依使用者要求撰寫，作為對「靜默貢獻者」的公開致謝。fork audit 完整資料見本 session 計畫檔（`C:\Users\Admin\.claude\plans\git-fork-repo-issue-pr-encapsulated-turing.md`）Phase A。

未來如果出現新的靜默貢獻者，本文件會以類似格式 append。
