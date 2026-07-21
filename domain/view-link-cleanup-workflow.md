---
name: view-link-cleanup-workflow
description: "視圖連結雜訊清理工作流程：關閉當前視圖中所有連結模型（如結構、機電）的基準元件（樓層線、網格線、參考平面），以維持圖面整潔。當使用者提到清理視圖、隱藏連結、關閉連結基準、連結模型可見性時觸發。"
metadata:
  version: "2.0"
  updated: "2026-07-06"
  created: "2026-05-15"
  status: "closed-negative-result"
  contributors:
    - "AI Assistant"
  references: [] 
  related: [] 
  referenced_by: []
  tags: [清理視圖, 連結模型, 隱藏網格, 可見性, link visibility, clean view]
---

# 視圖連結雜訊清理工作流程 (View Link Cleanup)

> [!CAUTION]
> **技術結案：不採用（Negative Result / 2026-07-06）**
>
> 核心需求「**只隱藏連結模型的基準品類、保留本體自己的基準**」經 Revit API 實測，**在 2023–2026 任何版本皆無法透過公開 API 達成**。下方「## 🧪 技術結案」完整記錄驗證方法、嘗試過的每條路徑與失敗原因，供上游與後續開發者參考，避免重複投入時間與 token。
>
> 唯一可自動化的變體是「**整個視圖全關（本體＋所有連結一起）**」，用 `View.SetCategoryHidden` 即可，所有版本適用——但這不符合「保留本體」的需求。

---

## 🧪 技術結案：為何「只關連結、留本體」不可行（Negative Result）

### 需求回顧
使用者的本體網格是以 **Copy/Monitor（監視複製）** 從主體模型帶入、屬 host 元素，需**保留**；同時該視圖疊了 8 個連結模型（結構＋機電），各自帶入自己的網格/樓層，需**只把連結的關掉**。

### 驗證方法（可重現）
不需開 Revit。以 `System.Reflection.MetadataLoadContext` 對 Nice3point 提供的 `RevitAPI.dll` ref assembly 做 metadata 反射，逐版本列出型別與方法簽章。此法零 Revit 依賴、可在 CI 對未來新版快速重驗。

### 逐版本 API 事實
| 型別 / 方法 | 2023 | 2024 | 2025 | 2026 |
|---|:--:|:--:|:--:|:--:|
| `RevitLinkGraphicsSettings` | ❌ 無 | ✅ | ✅ | ✅ |
| `View.GetLinkOverrides` / `SetLinkOverrides` | ❌ 無 | ✅ | ✅ | ✅ |
| `LinkVisibility` enum | ❌ 無 | ✅ | ✅ | ✅ |

**關鍵限制**：即使在 2024+，`RevitLinkGraphicsSettings` 的公開成員只有
`LinkVisibilityType`（`ByHostView` / `ByLinkView` / `Custom`）、`LinkedViewId`、`IsValidObject`——
**沒有任何「逐品類隱藏」的方法**。UI 上 Custom 模式能逐品類勾選，但那組覆寫**未在公開 API 開放**。

### 嘗試過的路徑與失敗原因
1. **原文件提議的 API**（`RevitLinkGraphicsSettings` + 對其呼叫 `SetCategoryHidden` + `View.SetLinkOverrides`）
   → 2023 編譯失敗（型別不存在）；2024+ 亦無 `SetCategoryHidden`-on-settings。該 API 組合是**憑空臆造**，從不存在。
2. **View 層級 `View.SetCategoryHidden(OST_Grids, true)`**
   → 可執行，但作用是**全域**：會連本體 Copy/Monitor 的網格一起關。違反「保留本體」。
3. **把 link 設成 `Custom` 再靠 host 的 SetCategoryHidden 驅動**
   → Custom 模式下連結品類由（無法存取的）自訂覆寫決定，不吃 host 的 SetCategoryHidden。無效。
4. **`ByLinkView` + `LinkedViewId` 指向「網格已關的連結視圖」**（唯一理論可行路）
   → 需**每個連結模型內先存在一個基準關閉的視圖**；而連結文件為唯讀，**無法從主檔在 link 內建立視圖**。非一鍵、脆弱、實務不可行。

### 結論
- 「只關連結、留本體」＝ **公開 API 做不到（2023–2026）**。這也解釋了本工作流最初被判「辦不到」是正確的。
- 若 Autodesk 未來在 `RevitLinkGraphicsSettings` 開放逐品類覆寫、或提供 per-link 的 category hidden API，屆時可用上表方法重驗後重啟本功能。
- 可自動化的替代：**整個視圖全關**（`SetCategoryHidden`，全版本適用），但不符本需求。

---

## 🔁 原始需求與設計（背景保留）

### 真實需求（使用者澄清）
批次把「所有連結模型的基準品類」（`OST_Grids` / `OST_Levels` / `OST_ReferencePlanes`）設為隱藏。兩個情境本質同一操作，差在對象：
1. **樣板成立前**：先清當前視圖（手動關 N 個 link 很煩）→ target = 視圖。
2. **樣板成立後又新增 link**：樣板不會自動關新 link 的基準 → target = 視圖樣板本身，**重跑一次全套即可**（不用去找哪個是新 link，全 link 無腦重套）。

### 設計決策
- **不做兩個清理工具**：清理能力做「一個工具」；情境 2 做「一條 skill」去編排它（符合專案 Domain/Skill 原則）。
- **視圖樣板本身是 View 物件**。若 API 允許「就地對樣板套 link override 並自動傳遞到所有套用視圖」，則使用者原本設想的「拆樣板→清理→刪舊建同名→重套回」dance 全部省掉。**優先就地編輯**。
- 就地編輯不只簡單，還避免「從視圖重建樣板」的保真度風險（新樣板 include/exclude 勾選會 drift、可能控制範圍變多）。

### 關鍵未解風險（決定成敗，動工前必驗）
「在 host 視圖裡隱藏某個 link 的特定品類」的確切 Revit API 尚未驗證（`RevitLinkGraphicsSettings` / `LinkVisibilityType=Custom` / `View.SetLinkOverrides`）。這正是當初被判「繁瑣」的核心。

### 待辦測試（下次續作從這裡開始）
- **Spike 1**：一般視圖把「1 個 link 的網格」關掉 → API 能成功嗎？
- **Spike 2**：對「視圖樣板」這個 View 做同樣事 → 套用該樣板的視圖會不會跟著變？（決定走就地 or dance）
- Spike 需新增 C# 命令（現有唯讀工具做不到），要 build + 部署。

### 執行環境決策
- view-cleanup 決定「一路做完」→ 值得投資熱重載 `upstream/feature/core-reload-optin`（探索型 API、迭代多、模型重：Z:\ 網路碟 + 8 個 link）。
- 使用者電腦**尚未設定過 optin**；計畫：基於 core-reload-optin 建本機分支 → build 三專案 → 部署 → 熱重載開發 spike/正式版 → 完成後把命令搬回本分支（單一 csproj，namespace 同為 `RevitMCP.Core`）提 PR。
- 首次設定 optin 需關 Revit 重載重模型；使用者「之後關掉 Revit 時再用熱重載」。

---

## 🛑 執行前置防呆守衛 (Context & Guardrails)

在嘗試修改任何可見性之前，AI 必須嚴格執行以下檢查：

### 1. 視圖樣版檢查 (View Template Guard)
- **規則**：如果當前視圖受「視圖樣版（View Template）」控制，且該樣板啟用了「V/G 覆寫 RVT 連結」的控制權，則**絕對不可**直接修改視圖設定。
- **AI 應對策略**：必須主動攔截並詢問使用者：「*目前視圖受樣板 `{TemplateName}` 控制。請問您希望 (A) 直接修改樣板（將影響所有套用此樣板的視圖）？ 還是 (B) 暫時解除此視圖的樣板關聯再做修改？*」

### 2. 3D 視圖特例 (3D View Exception)
- **規則**：在 3D 視圖中，網格線（Grids）與樓層線（Levels）的預設可見性行為與 2D 視圖不同。
- **AI 應對策略**：若偵測到當前為 `ThreeD` 視圖，需提醒使用者：「*此操作通常針對 2D 平/立/剖面圖紙，是否確定要在 3D 視圖中執行？*」

### 3. 禁止單一元素隱藏 (No Element-Level Hiding)
- **規則**：**嚴禁**讓 AI 使用選取幾何元素並「Hide in View」的方式來隱藏連結模型內容。
- **原因**：當連結模型更新並產生新元素時，新元素仍會顯示。必須使用「品類覆寫（Category Override）」來處理。

---

## 🔄 標準執行步驟 (Standard Workflow)

**執行時，請依照以下順序呼叫對應工具：**

### 步驟 1：取得當前視圖與連結資訊
1. 呼叫 `get_active_view`：確認視圖類型與 View Template 狀態。
2. 呼叫 `get_linked_models`：取得所有已載入的 `RevitLinkInstance` 清單與其 ID。

### 步驟 2：確認關閉目標
與使用者確認要關閉的品類，預設目標為：
- **註解品類**：`OST_Grids` (網格線)、`OST_Levels` (樓層線)、`OST_ReferencePlanes` (參考平面)

### 步驟 3：執行可見性覆寫
使用專用工具 `set_link_category_visibility`（待開發），將目標連結模型的圖形顯示切換為「自訂 (Custom)」，並關閉上述品類。

### 步驟 4：結果回報
向使用者總結：「*已在視圖 '{ViewName}' 中，將 {N} 個連結模型（包含：XXX, YYY）的網格線、樓層線與參考平面設定為隱藏。*」

---

## 🛠️ 所需的 MCP 工具介面規範 (API Contract)

為了支撐此工作流程，C# 端 (`CommandExecutor.cs`) 必須提供以下工具：

**工具名稱**：`set_link_category_visibility`
**預期輸入 (Input Schema)**：
```json
{
  "viewId": 0, // 0 表示 Active View
  "linkInstanceIds": [12345, 67890], // 若傳入 ["ALL"] 則套用至所有連結
  "categories": ["OST_Grids", "OST_Levels", "OST_ReferencePlanes"],
  "visible": false // false 為隱藏
}
```
**預期 C# 行為**：
針對指定的 `viewId`，取得其 `RevitLinkGraphicsSettings`。設定 `LinkVisibilityType = Custom` 後，呼叫 `SetCategoryHidden(categoryId, !visible)`，然後以 `View.SetLinkOverrides()` 套用。
