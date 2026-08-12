# GreenMaterial Injector（綠建材注入器）

原始資料：[台灣 TABC 綠建材標章資料查詢](https://tabcmgr.hopto.org/mgr/SearchCaseAction.aspx)

把台灣 TABC 綠建材標章資料帶進 Revit，讓材料選用、Type 建立、標章參數寫入與後續查核留在同一個 BIM 工作流程中。

你可以用它：

- 從綠建材資料庫搜尋並組合材料。
- 先產生「注入計畫」，確認 Revit 品類、構造層與目標 Type，再決定是否寫入模型。
- 將證書字號、廠商、TVOC、甲醛逸散率、CNS 試驗規範等資料寫入 Revit Type。
- 回查模型內已使用的綠建材，或依認證狀態進行標記。
- 定期比對既有材料 Set，找出缺件、過期或改名的資料。

> 這套流程會修改 Revit 模型，但規劃與寫入是分開的。`/GM_import` 只產生計畫；只有 `/GM_inject revit` 會實際寫入，且執行前會再次請你確認。

## 開始前準備

GreenMaterial Injector 是 Revit MCP 的工作流程，不是獨立安裝的應用程式。開始前請確認：

1. 已依專案根目錄的 [README](../../README.zh-TW.md) 安裝 Revit MCP。
2. Revit 已開啟目標專案，功能區中的 MCP 服務也已啟用。
3. AI Client 已載入本專案提供的綠建材 Skills。
4. 同一時間只有一個 AI Client 連接 Revit。
5. 若剛更新 Revit Add-in DLL，已重新啟動 Revit。

不確定連線是否正常時，先請 AI Agent 查詢目前的 Revit 專案。查詢成功後再進行匯入。

## 最短使用流程

日常匯入只需要以下三個階段：

| 階段 | 操作 | 結果 | 寫入 Revit |
|---|---|---|---|
| 1. 選材料 | 執行 `/GM_web open`，搜尋材料、建立 Set，回答品類與組合方式 | 網頁產生一段 `/GM_import ...` 指令文字 | 否 |
| 2. 看計畫 | 將網頁產生的完整文字貼給 AI Agent，執行 `/GM_import ...` | 產生注入計畫並說明目標品類、Type、構造與參數 | 否 |
| 3. 寫入模型 | 檢查計畫後執行 `/GM_inject revit`；有多個 Set 時可指定 Set 名稱 | 經確認後建立或更新 Revit Type／Material | **是** |

完成後，可以直接用自然語言詢問：

```text
查詢這面牆的綠建材資訊
列出 Walls 中已寫入綠建材資料的 Type
把有綠建材認證的元件標色
```

AI Agent 會透過 `GM_query` 讀取模型中的 `GreenMaterial_*` 參數；不需要重新執行匯入。

## 第一次操作範例

以下是示範流程，證書字號、材料與 Revit Type 請以實際查詢結果為準。

1. 執行 `/GM_web open`。
2. 在檢索頁搜尋「石膏板」，勾選板材與塗料，建立名為「辦公室隔間牆」的 Set。
3. 在「對齊需求與擬訂計畫」中選擇：
   - 組合方式：單一組合
   - 品類：Wall
   - 補充條件：輕隔間
4. 複製網頁產生的整段 `/GM_import ...` 文字並貼給 AI Agent。
5. 檢查 Agent 回報的材料、目標品類、構造層、厚度與來源 Type。
6. 確認內容正確後執行 `/GM_inject revit`。
7. Agent 會先列出預計建立或修改的內容；再次確認後才寫入 Revit。
8. 完成後直接詢問「查詢辦公室隔間牆的綠建材資訊」，核對寫入結果。

## 指令速查

### 日常使用

| 指令 | 用途 | 寫入 Revit |
|---|---|---|
| `/GM_web open` | 開啟綠建材檢索頁，搜尋材料並建立 Set | 否 |
| `/GM_import <網頁產生的文字>` | 對齊需求、比對資料並產生 `Revit_Injection_Plan.json` | 否 |
| `/GM_inject revit [SetName]` | 執行注入計畫；寫入前會先確認 | **是** |
| 自然語言詢問綠建材 | 回查 `GreenMaterial_*` 參數，彙整或標色 | 查詢不寫入；標色前應確認 |

### 資料維護

| 指令 | 何時使用 | 寫入 Revit |
|---|---|---|
| `/GM_update` | 需要從 TABC 官網更新本地資料庫快照時 | 否 |
| `/GM_set compare` | 定期檢查既有 Set 是否缺件、過期或改名時 | 否 |

## 寫入前你會確認什麼

`/GM_import` 與 `/GM_inject revit` 分開，是為了讓使用者在模型變更前檢查：

- TABC 材料與證書字號是否正確。
- 目標 Revit 品類是否正確，例如 Wall、Floor、Ceiling、Window 或 Door。
- 要建立新 Type，還是修改既有 Type。
- 系統家族的 Structure／Finish 層與厚度是否符合需求。
- 純材料應套用到哪一個既有 Type。
- 門窗等 Loadable Family 要以哪個來源 Type 建立新 Type。

若 Agent 列出候選 Type 並停下等待，這是正常的安全機制。請明確選擇目標，不要讓系統猜測。

## 支援範圍

| 類型 | 支援內容 |
|---|---|
| Wall | 板材與塗料的 Structure／Finish 複合構造、厚度推判與人工覆寫 |
| Floor | 地磚、打底與表面填充；支援表面 Pattern |
| Ceiling | 天花板系統 Type 的材料與參數寫入 |
| Window／Door | Loadable Family 的新 Type 建立與綠建材參數注入 |
| Column／Beam | 透過單一結構材質參數指派，不使用複合構造層 |
| 純材料 | 將填縫劑等非模型材料掛到使用者指定的既有 Type |
| 多材料 Type | 一個 Type 最多使用 Mat1～Mat6 槽位，並記錄輔助材料 |

系統家族可同時記錄 Type 層與 Material 層資料；接著劑、填縫劑、防水材料等非幾何材料則記錄於對應的輔助參數。

### Column／Beam 限制

部分既有族群會把「結構材質」設為 Instance 參數或綁定公式，使 Type 層寫入無法持久化。遇到此情況，請先在族群編輯器確認參數為可寫入的 Type 參數，再重新載入專案。技術背景見 [domain/lessons.md 的 L-031](../../domain/lessons.md)。

## 常見問題

### 指令沒有反應或逾時

依序確認 Revit 已開啟、MCP 服務已啟用，而且 AI Client 能查詢目前專案。若 `localhost:8964` 被占用，可執行：

```powershell
powershell -ExecutionPolicy Bypass -File scripts\release-port.ps1
```

### 換了 AI Client 後，原本的 Client 無法連線

這是目前的連線限制。Revit 端一次只接受一個 MCP 連線，新連線會取代舊連線。請關閉或停用舊 Client 的 MCP Server，再使用新的 Client。

### 更新 DLL 後仍是舊行為

Revit 只會在啟動時載入 Add-in DLL。關閉 Revit、確認程序已結束，再重新開啟。

### `/GM_import` 找不到材料

確認貼入的是網頁產生的完整文字。如果證書字號不在本地資料庫快照，先執行 `/GM_update` 再重試。

### 流程停在「請選擇 Type」

這不是當機。純材料與部分 Family 流程需要你從真實模型的候選清單中指定來源或目標 Type。直接回覆清單中的名稱或編號即可。

### 工具回報成功，但模型看不到變更

先請 Agent 重新查詢目標 Type，不要只看寫入工具的訊息。若是柱、樑或 Loadable Family，請檢查目標材質參數是否為可寫入的 Type 參數，以及是否被公式控制。

## 系統如何串接

```text
AI Client
  → MCP Server（Node.js）
  → WebSocket（localhost:8964）
  → Revit Add-in（C#）
  → Revit API
  → 目前開啟的 .rvt 模型
```

整個流程以「注入計畫」作為規劃與實際寫入之間的檢查點。共享參數統一使用 `GreenMaterial_` 前綴。

## 延伸文件

### 使用與規格

| 文件 | 適合何時閱讀 |
|---|---|
| [Revit 元件與綠建材對映分析](Revit_Element_GreenMaterial_Mapping_Analysis.md) | 想了解各 Revit 品類如何對應材料與參數時 |
| [Revit 綠建材注入計畫規格](Revit_GreenMaterial_Injection_Plan_Specification.md) | 想了解計畫 JSON、比對順序與資料結構時 |
| [注入邏輯與命名規範](revit_injection_logic_and_naming_spec.md) | 想確認 Type、Material、Family 命名規則時 |
| [最近一次注入計畫報告](Revit_Injection_Plan_Report.md) | 想查看目前產生器的最新工作產物時 |

### 權威方法與參數定義

- [綠建材目錄與採購方法](../../domain/GM_catalog.md)
- [共享參數 Schema](../../domain/GM_parameter-schema.md)
- [關鍵字檢索與同義詞規則](../../domain/GM_keyword-search.md)
- [RFA Family 注入方法](../../domain/GM_rfa-family-injection.md)

AI 工作流程定義位於 `.claude/skills/GM_web/`、`GM_import/`、`GM_inject/`、`GM_update/`、`GM_set/` 與 `GM_query/`。開發程式與歷史產物分類見 [綠建材開發歸檔索引](../../tools/green-material/README.md)。若文件描述不一致，以 `domain/GM_*.md` 的方法為準。
