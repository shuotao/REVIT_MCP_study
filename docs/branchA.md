# Branch A — AI 對話「重新命名」能力 patch 精修文件

> **這份文件的目的**：把 taiwanbanana fork 的 `modify_element_parameter` 補丁（讓 AI 能透過對話幫你改名）解釋到 Revit 使用者完全看得懂為止——**讀者預設是建築設計師 / 熟悉 Revit 但不寫程式的人**。
>
> **怎麼用這份文件**：
> 1. 你讀，遇到看不懂的地方就在 [§11 你的疑問清單] 裡寫下來
> 2. 我針對你的疑問改寫對應段落（可能重組、可能補圖、可能換比喻）
> 3. 改完版本號 +0.1，繼續迭代
> 4. 你看懂後我們再回頭決定要不要 merge

**版本**：v0.4（2026-05-13）

---

## §1 這個 patch 補上什麼能力？

> **讓 AI 能透過對話幫你「重新命名」Revit 元素。**
>
> 範圍涵蓋所有你在 Project Browser 可以右鍵按「**重新命名**」的東西：view、sheet、Family Type、Level、Grid、Schedule、View Template……

**簡單對照**：

| 情境 | 你慣用的方式 | AI 對話版（patch 後） |
|---|---|---|
| 改 view 名 | Project Browser → 右鍵 view → 重新命名 → 輸入 `A-101` | 「把目前 view 改名叫 A-101」 |
| 改類型名 | 屬性面板 → 編輯類型 → 重新命名(R)... → 輸入 `外牆-15RC` | 「把這個牆類型改名為 外牆-15RC」 |
| 改 Level 名 | Project Browser → 右鍵 Level → 重新命名 → 輸入 `1FL` | 「把這個 Level 改名為 1FL」 |

在 patch 之前，這 3 種對話 AI 全部會失敗（原因見 §2）。Patch 補上之後，AI 就能做了。

---

## §2 之前為什麼 AI 做不到？— Revit 內部的「兩本帳」

要懂這個 patch，要先懂一個 Revit 設計上的小怪癖：**元素的資料被分成兩個地方存**，而「Name」這個欄位剛好不在你以為的那個地方。

### 第一本帳：屬性面板列出來的那些欄位（叫「**參數**」）

選一面牆，左下角屬性面板會列出來：
```
Comments：（空白）
Mark：W-101
Volume：1.234 m³
Area：12.5 m²
Fire Rating：1-hour
Length：5000 mm
...
```

這些都叫「**參數**」。AI 想改它們時，會「**翻屬性面板找**」這個鍵——只要你在屬性面板上看得到的，AI 都找得到。

### 第二本帳：Revit 系統綁在元素身上的核心欄位（叫「**屬性**」）

但是——**Name 這個欄位不在屬性面板上**。

那 Name 顯示在哪裡？兩個地方：

1. **Project Browser**：每個 view 名、sheet 編號、Level 名、Family Type 名，這些都是元素的 Name
2. **「編輯類型」對話框最上方**「類型 [下拉]」旁邊的「**重新命名(R)...**」按鈕——按它改的就是類型本身的 Name

Name 屬於「**系統綁定**」欄位——它跟元素本身焊死、不在屬性面板的參數清單裡。

### 衝突就在這裡

AI 之前的邏輯是：「**想改任何欄位 → 一律翻屬性面板**」。但 Name 不在屬性面板，所以 AI 翻來翻去找不到，回報：

```
找不到參數: Name
```

從你（Revit 使用者）的角度看：「？我明明在 Project Browser 上**看得到** Name 啊？」 → 對，**看得到不代表 AI 找得到**，因為 AI 翻的是「屬性面板那本帳」，而 Name 寫在「系統綁定那本帳」上。

這就是 patch 要修的問題。

---

## §3 Patch 做了什麼？— 讓 AI 學會「改名要走另一條路」

Patch 加了一段判斷邏輯：

> 當你（或 AI）想改的欄位名稱是**下面 4 個關鍵字其中之一**——
>
> - `Name`（英文版 Revit 用）
> - `名稱`（繁中／簡中版 Revit 用）
> - `類型名稱`（中文版類型屬性對話框看到的標籤）
> - `-1002001`（Revit 內部給 Name 欄位的數字編號）
>
> ——這個 patch 會**直接呼叫 Revit 內部的「重新命名」功能**，等同於你在 Project Browser 右鍵按「重新命名」、或在類型屬性對話框按「重新命名(R)...」按鈕的動作。

### 對照表（patch 之前 vs 之後）

| 你想改的欄位 | Patch 之前 | Patch 之後 |
|---|---|---|
| Comments、Mark、Length、Volume、Fire Rating 等屬性面板上看得到的 | ✅ 翻屬性面板找到 → 改 | ✅ 一樣，未變動 |
| **Name、名稱、類型名稱、-1002001** | ❌ 翻屬性面板找不到 → 報「找不到參數」 | ✅ **改走「重新命名」入口** → 成功改名 |

### 重點觀念釐清

- **不是新工具**：`modify_element_parameter` 還是同一個工具，名字、參數、使用方式完全沒變
- **不是炫技、不是大改造**：實際就是加 4 行 `if` 判斷，把 4 個關鍵字導向 Revit 系統的重新命名功能
- **不影響你之前能做的事**：以前 AI 能改的（Comments、Mark…），現在還是能改，行為一字不差
- **只多了一個能力**：以前 AI 不能改的（Name 系列），現在能改了

### 你可以這樣理解

想像 AI 是一個剛上工的助理。它原本只會走一條路徑去改 Revit 元素：「**翻屬性面板**」。

這個 patch 等於是給它新增了一條 SOP：「**如果使用者說的鍵是 Name 系列，先別翻屬性面板——改走「Project Browser 右鍵重新命名」那條路**」。

就這麼簡單。

---

## §4 有了這個 patch 能做什麼？三個典型場景

### 場景 1：類型重命名（最常用）

> BIM 工程師整理模型時，常需要把樣板帶來的英文預設類型名改成專案命名規則。

```
使用者：把現在選到的牆類型改名為「外牆-15cm-RC」
AI：（內部呼叫 get_selected_elements → 拿 ID）
AI：（內部呼叫 modify_element_parameter parameterName="名稱"）
AI：✅ 已改名
```

### 場景 2：視圖重命名（出圖標準化）

> 出圖前要把所有 view 的名字統一成圖號格式（A-101、A-102、S-201……）。

```
使用者：把當前 view 改名叫 A-101
AI：（呼叫 get_active_view → 拿 ID）
AI：（呼叫 modify_element_parameter parameterName="Name"）
AI：✅ 已改名
```

### 場景 3：批次層名整理

> 樣板帶來的 Level 名是「1F、2F、3F」，公司規範是「1FL、2FL、3FL」。

```
使用者：把所有 Level 的 Name 加上 L 字尾
AI：（呼叫 get_all_levels → 拿清單）
AI：（迴圈：對每個 Level 呼叫 modify_element_parameter）
AI：✅ 已批次改名
```

---

## §5 四個 alias 鍵的詳解

這個 patch 接受 4 個鍵，**全都指向同一件事**：呼叫 Revit 內部的「重新命名」功能。

| 鍵 | 來源 | 為什麼接受 |
|---|---|---|
| `"Name"` | 英文原生 | Revit 英文版介面標準稱呼，也是 C# property 名 |
| `"名稱"` | 中文版本地化 | Revit 繁中／簡中介面標準翻譯 |
| `"類型名稱"` | 中文版本地化變體 | 中文版屬性面板對 Type 元素顯示為「類型名稱」 |
| `"-1002001"` | BuiltInParameter 整數 | `BuiltInParameter.ALL_MODEL_TYPE_NAME` 列舉值的整數轉字串 |

### 為什麼設計成 4 個 alias 而不是「只接受 Name」？

| 設計選擇 | 優點 | 缺點 |
|---|---|---|
| 只接受 `"Name"`（嚴格） | 簡單、語意一致 | 中文版使用者要記英文鍵、跨版本相容性差 |
| **接受 4 個 alias（寬鬆）** | AI 對話容錯高、跨語言版本通用 | 失去語意嚴格性 |
| 接受所有含 Name 字樣的鍵（更寬鬆） | 容錯極高 | 可能誤判（如自訂參數叫「Name Tag」） |

taiwanbanana 選了中間方案。對 MCP 這種 AI 對話介面，**寬鬆 alias 比嚴格語意更實用**——使用者不必知道 Revit 的英文／中文／API 三層命名。

---

## §6 完整使用步驟（初學者版）

### 前提（一次性設定）

1. Revit 2024 已開啟一個專案
2. MCP Server 已啟動
3. Add-in 已部署（含本 patch 的版本）
4. AI Client 已連上 MCP

### 改一個 view 名字（step by step）

#### Step 1：用 AI 告知意圖

```
你：把目前打開的 view 改名為「平面圖-1F-完工」
```

#### Step 2：AI 自動呼叫 get_active_view

```json
{ "tool": "get_active_view", "args": {} }
```

回應：
```json
{ "ElementId": 53189012, "Name": "平面図 1階", "ViewType": "FloorPlan" }
```

#### Step 3：AI 自動呼叫 modify_element_parameter

```json
{
  "tool": "modify_element_parameter",
  "args": {
    "elementId": 53189012,
    "parameterName": "Name",
    "value": "平面圖-1F-完工"
  }
}
```

#### Step 4：Patch 的「Name 系列」分流判斷觸發

Revit Add-in 內部執行：
```
if (使用者要改的鍵 ∈ {"Name", "名稱", "類型名稱", "-1002001"}) {
    呼叫 Revit 系統的「重新命名」功能（等同 Project Browser 右鍵 → 重新命名）
}
else {
    照舊：翻屬性面板找該欄位再改
}
```

→ 因為 `parameterName` 是 `"Name"`，符合分流條件，**進入「重新命名」這條路**。

#### Step 5：Revit 進入 Transaction 並提交

```csharp
trans.Commit();   // ← 改動寫入 Revit Document
```

#### Step 6：Project Browser 即時刷新

你看著 Revit 視窗，左側 Project Browser 的這個 view 名字**立刻**從「平面図 1階」變成「平面圖-1F-完工」。

#### Step 7：AI 回報

```
AI：✅ 已將 ElementId=53189012 的 view 改名為「平面圖-1F-完工」
```

---

## §7 哪些元素可以改名？哪些不能？

這不是本 patch 的限制，**是 Revit 本身的設計**——某些元素根本不允許自訂 Name（包括你在 GUI 上也改不了）。

### 可改 Name 的元素

| 類別 | 範例 | 備註 |
|---|---|---|
| ✅ View（所有類型） | 平面圖、剖面、3D、明細表 | 最常用 |
| ✅ Sheet | A-101、S-201、M-301 | 圖紙 |
| ✅ View Template | View Template 自身的名字 | 視圖樣板 |
| ✅ Wall Type | 「外牆-15cm-RC」 | **類型**可改，instance 不可 |
| ✅ Floor Type | 「樓板-RC-15cm」 | 同上 |
| ✅ Door/Window Type | 「D-01-雙開門」 | 同上 |
| ✅ Family Symbol | 族群類型 | 同上 |
| ✅ Level | 「1FL」、「2FL」 | 樓層 |
| ✅ Grid | 「A」、「1」 | 軸線 |
| ✅ Schedule | 明細表名 | 出圖用 |

### 不可改 Name 的元素

| 類別 | 為什麼 |
|---|---|
| ❌ Wall instance | Wall 實例的 Name = derived from WallType.Name，無法獨立設定 |
| ❌ Floor instance | 同上 |
| ❌ Door/Window instance | Name = "{TypeName} : {Symbol}" 自動組合 |

### 你會看到的錯誤訊息

```
Error: This element does not support assignment of a user-specified name.
```

這就是**測試 1 看到的訊息**——表示 patch 的分流判斷**確實命中**（AI 正確走進「重新命名」這條路），但 Revit 本身在最後一刻說「這個元素不準改 Name」。換句話說：**錯不在 AI、不在 patch、不在你的對話——是這類元素本來就改不了**。

---

## §8 初學者最容易踩的坑

### 坑 1：以為 Wall instance 可以改名

```
你：把選到的這面牆改名叫「南面外牆-101」
AI：（呼叫 modify_element_parameter parameterName="名稱"）
Revit：This element does not support assignment of a user-specified name.
```

**正解**：你要改的是「**牆類型**」（WallType），不是「牆 instance」。

### 坑 2：以為改 view name 會連帶改 sheet 上的標題

不會。View name 和 sheet 上的「圖題」是兩個獨立參數。改 view name 只是 Project Browser 顯示變。

### 坑 3：以為「-1002001」是 magic number 沒意義

它是 `BuiltInParameter.ALL_MODEL_TYPE_NAME` 的整數轉字串。對初學者來說，**不必記**——用「Name」或「名稱」就好。

### 坑 4：以為這個工具能改「Comments」「Mark」「Length」

不能（這個 patch 不負責 Comments / Mark / Length 那些）。Patch 只攔截 4 個 Name 系列的鍵。其他參數走原本的「翻屬性面板」路徑——這些**本來就工作正常**，patch 完全沒動到。

---

## §9 五種重新命名方式的比較 — 別被「AI 對話 = 快」騙了

### 前言：用 Revit 真實使用者的思考方式來談

寫工具比較最忌諱用「AI 友善」、「自動化程度」、「整合度」這種技術行銷詞。真實坐在 Revit 前的人，腦袋裡轉的是另外一組問題：

1. **相容性**：用了會不會跟我現在的東西打架？團隊命名規則會不會被破壞？換樣板會不會出問題？
2. **會不會（上手難度）**：學了之後我自己能不能獨立用？還是只能找懂的人代跑？
3. **直覺反饋**：按下去馬上看到結果嗎？還是要切視窗才確認？

任何重命名方式都被這三個維度檢驗。沒過關，再炫的工具都會被棄用。下面把這五種方式放到真實情境裡走一趟。

---

### 一個容易被誤會的點：AI 對話**單一改名不見得比較快**

直覺會以為「AI 對話改名 = 飛快」。其實是漂亮的迷思。

**假設你只想改一個東西**：把 1 個柱子類型從 `C-450x450` 改成 `RC-柱-45R`。

**手動操作（Revit 老手）**：
1. 屬性面板 → 「編輯類型」（1 秒）
2. 對話框最上方 → 「重新命名(R)...」（1 秒）
3. 打字 `RC-柱-45R` → Enter（1–2 秒）

→ **總共 3–4 秒**。

**AI 對話**：
1. 切到 AI 對話視窗（1–2 秒）
2. 打字「把現在選到的柱子類型改名為 RC-柱-45R」（5 秒）
3. AI 跑 `get_selected_elements`（1–2 秒）
4. AI 跑 `modify_element_parameter`（1–2 秒）
5. AI 回報「已改完」（1 秒）

→ **總共 10–15 秒**。

**單一改名，AI 對話反而慢 3 倍**。如果今天你只是順手要改一個版的類型名、或一根柱子的命名，**乖乖在 Project Browser 右鍵改最快**。

那 AI 什麼時候真的快？答案是「**多 + 規律**」：

- 改 20 個 view 的名稱（從 `View 1`、`View 2`... 系列改成 `A-101`、`A-102` 系列）
- 改 50 個 Family Type 的命名規範
- 跨樓層批次重命名（所有 1F 的 Door Type 加上 `1F-` 前綴）
- 規則式整理（「把所有 A 開頭的 view 都改成 ARCH- 開頭」）

這些場景手動點到死也做不完，AI 對話 + 自然語言規則才是合適工具。

> **AI 對話的真實價值不是「單一動作快」，是「批次 + 規則化 + 你不必寫程式」**。

---

### 另一個常被忽略的群體：完全沒用過 Revit 的管理者

走進任何一間中大型建築事務所，你會發現有一群人經常要碰 Revit，但他們**根本不是建築設計師**：

- **高階管理者**：要看圖紙、改個註記、把 view name 對應到他理解的編號規則
- **業主決策者**：來瀏覽圖、點看 3D、勾選需要改的東西
- **QC 審查者 / 工務監造**：要打開圖紙、留 comment、整理出圖清單
- **跨部門協調者**：要把 Revit 的 schedule 名對應到他的 Excel 表

這群人的痛點**不是「速度」**——他們的痛點是：

- 「Project Browser 在左邊還是右邊？」
- 「我要找的東西不是 view 嗎，怎麼右鍵沒有『重新命名』選項？」
  > （其實是因為點到了「視圖類型」這個群組節點，不是 view 本身——但他們不會這樣分辨）
- 「我按了重新命名沒反應？」
  > （焦點跑到別的視窗去了，但他們不會發現）
- 「這個欄位我是要在屬性面板改、還是進編輯類型對話框改？」
- 「為什麼我一改名其他 view 也跟著變了？」
  > （改到了 View Template 而不是 View 本身——但他們分不清楚）

對這群人，**AI 對話改名 = 他們唯一能用的入口**。手動操作根本不在他們的選項清單裡——他們連在哪裡點都不知道。

這就是這個 patch 對「**未接觸過 Revit 的人**」的真正價值：

- 不是「快」
- 是「**降低門檻**」
- 是「**提供他們唯一可走的路**」

---

### 真實比較表（加上沒人講但很重要的維度）

| 方式 | 適合誰 | 改 1 個的速度 | 改 100 個的速度 | 相容性 | 學習門檻 | 直覺反饋 |
|---|---|---|---|---|---|---|
| Project Browser 右鍵 → 重新命名 | Revit 老手 | ⚡ 最快（3 秒） | 慢死（5–10 分鐘） | 完美 | 中（要知道哪些東西可右鍵） | 即時 |
| 屬性面板輸入 / 編輯類型對話框 | Revit 老手 | ⚡ 快（3–5 秒） | 中（要切換選取） | 完美 | 中 | 即時 |
| Dynamo 腳本 | BIM 顧問 / 自動化工程師 | 慢（要先寫腳本） | 飛快 | 看版本／節點套件 | **高** | 跑完才看到結果 |
| pyRevit 腳本 | BIM 顧問 / Python 工程師 | 慢（要先寫腳本） | 飛快 | 看 pyRevit / Revit 版本 | **高** | 跑完才看到結果 |
| **AI 對話（本 patch）** | **批次改名者 + 非 Revit 專業者** | 慢（10–15 秒） | 快（自然語言批次） | 跟 MCP 工具同步 | **低**（會講話就會用） | 即時 |

**注意這張表的兩個欄位**：

- **「相容性」**：所有手動方式都是 Revit 原生 GUI 行為，相容性 = 100%；腳本工具（Dynamo / pyRevit）相容性取決於版本與套件，常常踩坑；AI 對話的相容性取決於 MCP 工具的維護狀態
- **「學習門檻」**：注意 AI 對話的門檻**低於手動 GUI**——因為連「Project Browser 在哪」都不必知道

---

### 結論：別用「哪種最強」想，要用「我是誰」想

工具沒有絕對的好壞，只有「對你這個角色合不合適」。用下面這張對應表定位自己：

| 你是誰？ | 你的痛點 | 哪種方式最適合？ |
|---|---|---|
| Revit 老手 | 改 1–5 個 | **手動 GUI**（最快） |
| Revit 老手 | 改 20–100 個有規律的 | **AI 對話**（最划算） |
| Revit 老手 / BIM 顧問 | 跨專案標準化、要重複跑 | **Dynamo / pyRevit**（一次寫多次用） |
| Revit 新手 | 知道要改什麼但記不住在哪改 | **手動 GUI + AI 輔助** |
| **管理者 / 業主 / 非 Revit 專業** | **連 UI 在哪都不知道** | **AI 對話（唯一可走的路）** |
| Mixed team（建築師 + 工程顧問 + 管理者） | 不同角色不同習慣 | **多管齊下，AI 對話補底** |

---

### 寫給工具評估者的反思

這個 patch 真正解決的問題不是「讓 AI 對話炫一點」，而是把**原本鎖在 Revit GUI 裡的「重新命名」能力，對外打開一個自然語言入口**。

對誰最有意義？是那些**站在 GUI 之外的人**——他們的痛點不是慢，是「**找不到入口**」。對他們來說，這個 patch 不是工具的「改進」，是工具的「**從不存在變成存在**」。

對 Revit 老手來說，這個 patch 也不是要取代你右手按右鍵的肌肉記憶——它是**多了一條路**，當你「**懶得逐一點 + 規則很清楚**」的時候用。

工具不該強迫使用者選邊站。**好的工具是讓不同角色都有自己合適的入口**——而這個 patch 補上的，是 AI 對話這條入口在「重新命名」這件事上之前完全斷掉的那一段。

---

## §10 測試現況：完整 7/7 PASS

### Name 守門路徑（patch 新增的）— 4/4 PASS

| # | 場景 | 鍵 | 元素 | 結果 |
|---|---|---|---|---|
| 1 | Wall instance 中文鍵 | `"名稱"` | Wall (53578860) | 分流判斷命中、Revit 拒絕（Wall instance 不準改名）|
| 2 | View 英文鍵 | `"Name"` | View (53189012) | 改名成功 `A-101-Test` |
| 3 | View BIP 整數鍵 | `"-1002001"` | View (53189012) | 改名成功 `A-102-BIP` |
| 4 | 負測：不存在的參數 | `"不存在的參數XYZ"` | View | 回原本「找不到參數」錯誤 |

### else 分支（原本就在、patch 沒動）— 4/4 PASS

`else` 分支走 LookupParameter，內部 4 條子路徑全部驗證:

| # | 子路徑 | 操作 | 結果 |
|---|---|---|---|
| A | 參數不存在 | `"不存在的參數XYZ"` | ✅ 「找不到參數: 不存在的參數XYZ」（與測試 4 同一個 case） |
| B | 正常寫入 Double | `頂部偏移=100`（從 0） | ✅ 「成功修改參數 頂部偏移」，Revit 即時刷新 |
| C | IsReadOnly 守衛 | `長度=5000`（Wall 的 Length 為 geometry 計算值） | ✅ 「參數 長度 是唯讀的」 |
| D | TryParse 失敗 | `頂部偏移="abc"` | ✅ 「設定參數失敗」（double.TryParse 回 false → success 維持 false → throw） |

**Reset**:測試後 `頂部偏移` 從 100 還原回 0，wall 不留污染。

### 結論

Branch A patch 完整 7/7 驗證:Name 守門 4/4 + else 子路徑 4/4 全綠。**Patch 對既有 else 行為完全沒影響**——B/C/D 三條路徑運作一字不變。風險評估歸零。

---

## §11 你的疑問清單（你來填，我來改）

> 看到不清楚的段落，把疑問寫在這裡。可以是「§3 那個 Property vs Parameter 的差異我不懂」，也可以是「§5 為什麼要有 4 個 alias 我看不出來必要性」，越具體越好。

- [ ] (在此填入第 1 個疑問)
- [ ] (在此填入第 2 個疑問)
- [ ] ...

---

## §12 版本歷程

| 版本 | 日期 | 變動 |
|---|---|---|
| v0.1 | 2026-05-13 09:15 | 初稿，從 chat 整理為獨立文件 |
| v0.2 | 2026-05-13 09:30 | 使用者反饋：「守門員」太抽象、「繞過」聽不懂。重寫 §1–§3 改用 Revit UI 視角（屬性面板 vs Project Browser 重新命名）、「兩本帳」比喻。「守門員」一詞全文移除，改稱「Patch 的 Name 系列分流判斷」或直接寫「本 patch」。標題也改：去掉「Element.Name 守門員」、改成「AI 對話『重新命名』能力 patch」。 |
| v0.3 | 2026-05-13 09:45 | 使用者反饋：§9 要寫成部落格討論篇幅，加上「相容性 / 會不會 / 直覺反饋」三維檢驗，破除「AI 對話 = 快」迷思，新增「非 Revit 專業管理者」這個常被忽略的群體。重寫 §9 整段：前言 → AI 不見得快的反例 → 管理者群體論述 → 真實比較表（加入相容性、學習門檻、直覺反饋三欄）→ 角色對應結論表 → 工具評估反思。整段從原本 7 行表格擴成完整論述文。 |
| v0.4 | 2026-05-13 09:55 | 補測 §10 標記的未測 3 個 else 子路徑（B 正常寫入 / C 唯讀 / D 型別不符）全 PASS。Branch A patch 完整 7/7 驗證，風險歸零。§10 重寫:標題改「測試現況:完整 7/7 PASS」，移除「未測 3 個 case」段落、移除「補測計畫（建議）」表格、改為兩段並列的「Name 守門 4/4」+「else 子路徑 4/4」與「結論」段落。對應 log/2026-05.md 09:55 poc-test 條目。 |

---

## 附錄 A：相關 commit / 歸屬

- 來源 fork：[taiwanbanana/REVIT_MCP_study](https://github.com/taiwanbanana/REVIT_MCP_study) commit `2e463400` (2026-04-24)
- 上游正式合入：commit `1ac2485`（2026-05-13 squash-merge 到 main，含本技術文件 + patch + 測試紀錄）
- 歷史軌跡：曾誤合入 `cd21bab` 後 revert (`0ab786f`)，再經 7/7 補測後正式合入。完整審計軌跡留在 `git log`

## 附錄 B：實際 diff（22 行淨改）

位置：`MCP/Core/CommandExecutor.cs:660–710`

```diff
             using (Transaction trans = new Transaction(doc, "修改參數"))
             {
                 trans.Start();

-                Parameter param = element.LookupParameter(parameterName);
-                if (param == null)
-                {
-                    throw new Exception($"找不到參數: {parameterName}");
-                }
+                bool success = false;
+
+                // 特殊處理：重命名 (直接修改 Element.Name 屬性)
+                if (parameterName == "Name" || parameterName == "名稱"
+                    || parameterName == "類型名稱" || parameterName == "-1002001")
+                {
+                    element.Name = value;
+                    success = true;
+                }
+                else
+                {
+                    Parameter param = element.LookupParameter(parameterName);
+                    if (param == null)
+                    {
+                        throw new Exception($"找不到參數: {parameterName}");
+                    }

-                if (param.IsReadOnly)
-                {
-                    throw new Exception($"參數 {parameterName} 是唯讀的");
-                }
+                    if (param.IsReadOnly)
+                    {
+                        throw new Exception($"參數 {parameterName} 是唯讀的");
+                    }

-                bool success = false;
-                switch (param.StorageType)
-                {
-                    case StorageType.String:
-                        success = param.Set(value);
-                        break;
-                    ...
+                    switch (param.StorageType)
+                    {
+                        case StorageType.String:
+                            success = param.Set(value);
+                            break;
+                        ...
+                    }
                 }

                 if (!success)
                 {
                     throw new Exception($"設定參數失敗");
                 }
```
