# GreenMaterial Injector（綠建材注入器）

把台灣 TABC 綠建材標章資料帶進 Revit：搜尋材料、組成材料 Set、檢查注入計畫，再建立或更新 Revit Type 與 Material。

> `/GM_import` 只產生計畫，不修改模型。只有 `/GM_inject revit` 會寫入 Revit，而且執行前會再次請你確認。

## 快速開始

GreenMaterial Injector 不是獨立應用程式。請在已載入本專案 Skills、且已連接 Revit MCP 的 AI Client 對話中使用下列 `/GM_*` 指令。

### 1. 確認環境

- 已依[安裝說明](../../README.zh-TW.md)完成 Revit MCP 設定。
- Revit 已開啟目標專案，功能區中的 MCP 服務已啟用。
- 目前只有一個 AI Client 連接 Revit。
- 更新過 Add-in DLL 時，已重新啟動 Revit。

第一次使用，可直接貼給 AI Agent：

```text
我要把綠建材匯入目前開啟的 Revit 專案。請先確認連線，再帶我逐步操作；寫入模型前先讓我檢查注入計畫。
```

若 Agent 無法查詢目前的 Revit 專案，先不要開始匯入，請參考[常見問題](#常見問題)。

### 2. 選材料

執行：

```text
/GM_web open
```

在開啟的檢索頁：

1. 搜尋並勾選材料。
2. 建立材料 Set。
3. 選擇 Revit 品類與組合方式。
4. 複製網頁產生的完整 `/GM_import ...` 文字。

### 3. 檢查注入計畫

將剛才複製的完整文字貼回 AI Client。Agent 會產生注入計畫，但不會修改 Revit。

寫入前請確認：

- 材料名稱與 TABC 證書字號正確。
- 目標品類正確，例如 Wall、Floor、Ceiling、Window 或 Door。
- 要建立新 Type，還是使用既有 Type。
- Structure／Finish 構造層與厚度符合需求。
- 門窗等 Loadable Family 的來源 Type 正確。

若 Agent 列出候選 Type 並等待選擇，這是安全機制，不是當機。請明確回覆清單中的名稱或編號。

### 4. 寫入 Revit

確認計畫正確後執行：

```text
/GM_inject revit
```

有多個 Set 時，可指定名稱：

```text
/GM_inject revit [SetName]
```

Agent 會再次列出即將建立或修改的內容；只有在你確認後才會寫入模型。

### 5. 驗收結果

完成後，請 Agent 重新讀取目標 Type。至少確認：

- Type 名稱符合注入計畫。
- Material、構造層與厚度符合需求。
- `GreenMaterial_*` 參數包含預期的證書與材料資料。
- 原本不應修改的 Type 維持原狀。

可以直接詢問：

```text
查詢剛才建立的 Type，確認材料、構造層與綠建材參數是否符合注入計畫。
```

若結果不符，先停止後續批次操作，並保留 Type 名稱與 Agent 回報的錯誤訊息。

## 完成後會得到什麼

依材料與品類不同，流程會建立或更新：

- Revit Type。
- 對應的 Material 與複合構造層。
- 證書字號、廠商、TVOC、甲醛逸散率及 CNS 規範等 `GreenMaterial_*` 參數。
- 可供 `/GM_query` 後續回查的綠建材紀錄。

## 依目的選擇指令

| 目的 | 指令 | 是否寫入 Revit |
|---|---|---|
| 搜尋材料並建立 Set | `/GM_web open` | 否 |
| 產生並檢查注入計畫 | `/GM_import <網頁產生的文字>` | 否 |
| 執行已確認的注入計畫 | `/GM_inject revit [SetName]` | **是** |
| 查詢已寫入的綠建材 | `/GM_query`，或直接用自然語言詢問 | 查詢不寫入；標色前會確認 |
| 更新本機 TABC 資料 | `/GM_update` | 否 |
| 檢查 Set 是否缺件、過期或改名 | `/GM_set compare` | 否 |

`/GM_query` 用於查詢、彙整與上色標記；目前不計算面積、比例，也不匯出 Excel 明細表。

## 支援範圍

| 類型 | 支援內容 |
|---|---|
| Wall | 板材與塗料的 Structure／Finish 複合構造、厚度推判與人工覆寫 |
| Floor | 地磚、打底與表面填充；支援表面 Pattern |
| Ceiling | 天花板系統 Type 的材料與參數寫入 |
| Window／Door | 從來源 Type 建立 Loadable Family 新 Type，並注入 `GreenMaterial_Mat1_*` 綠建材參數；整體合格狀態 `GreenMaterial_Certified` 為 best-effort（2026-08-13 實測：部分家族會被 Revit 拒絕新增此欄位，屬已知限制，Mat1 資料不受影響） |
| Column／Beam | 指派單一結構材質（不使用複合構造層），並同時寫入 `GreenMaterial_Certified`／`GreenMaterial_Mat1_*` 共享參數 |
| 純材料 | 將填縫劑等非模型材料掛到指定的既有 Type |
| 多材料 Type | 使用 Mat1～Mat6 槽位記錄主要與輔助材料 |

### 已知限制

- 柱／樑若是透過 `/GM_inject` Scenario 8 建立，查詢結果會有完整的 `GreenMaterial_Certified`／證書廠商與有效期資料；若是更早期以其他方式手動指定結構材質、未走過 Scenario 8 的既有 Type，查詢結果就只有 Type、結構材質與實例數量。
- Window／Door 的 `GreenMaterial_Certified` 為 best-effort：實測至少一個既有窗家族會被 Revit 拒絕新增這個 YESNO 共享參數（`Shared parameter creation failed.`），原因未明。這種情況下該 Type 只會有 `GreenMaterial_Mat1_*`（含證書廠商、有效期等完整資料）而沒有 `Certified` 旗標，查詢/上色時不能假設門窗一定有這個欄位。
- 某些柱、樑或 Loadable Family 將材質設為 Instance 參數或以公式控制，可能無法從 Type 層寫入。此時須先在族群編輯器改為可寫入的 Type 參數，再重新載入專案。
- 大型專案的窗／門候選 Type 過多時，查詢可能不完整。若結果可疑，請 Agent 使用 `GBM` 或 `TABC` 關鍵字再次確認。

以上限制不影響其他已支援品類的正常匯入流程。柱樑參數問題的技術背景見 [L-031](../../domain/lessons.md)。

## 常見問題

### AI Client 找不到 `/GM_*` 指令

確認 AI Client 是從本專案啟動，且已載入 `.claude/skills/GM_*` 工作流程。若使用的 Client 不支援斜線指令，可直接用自然語言說明目的，例如「開啟綠建材檢索頁」或「執行綠建材注入計畫」。

### 指令沒有反應或逾時

依序確認 Revit 已開啟、MCP 服務已啟用，而且 Agent 能查詢目前專案。若 `localhost:8964` 被占用，可在專案根目錄執行：

```powershell
powershell -ExecutionPolicy Bypass -File scripts\release-port.ps1
```

### 換了 AI Client 後，原本的 Client 無法連線

Revit 端一次只接受一個 MCP 連線，新連線會取代舊連線。請停用舊 Client 的 MCP Server，再使用新的 Client。

### 更新 DLL 後仍是舊行為

Revit 只在啟動時載入 Add-in DLL。請完整關閉 Revit，確認程序已結束後再重新開啟。

### `/GM_import` 找不到材料

確認貼入的是網頁產生的完整文字。若證書不在本機資料庫，先執行 `/GM_update` 再重試。

### 工具回報成功，但模型看不到變更

請 Agent 重新讀取目標 Type，不要只依賴寫入訊息。若是柱、樑或 Loadable Family，另請確認材質參數是可寫入的 Type 參數，且未被公式控制。

## 延伸閱讀

- [Revit 綠建材注入計畫規格](Revit_GreenMaterial_Injection_Plan_Specification.md)：計畫 JSON、資料結構與比對順序。
- [注入邏輯與命名規範](revit_injection_logic_and_naming_spec.md)：Type、Material 與 Family 命名方式。
- [共享參數 Schema](../../domain/GM_parameter-schema.md)：`GreenMaterial_*` 欄位定義。
- [綠建材目錄與採購方法](../../domain/GM_catalog.md)：資料目錄與選材方法。
- [關鍵字檢索規則](../../domain/GM_keyword-search.md)：關鍵字與同義詞處理。
- [RFA Family 注入方法](../../domain/GM_rfa-family-injection.md)：Loadable Family 技術流程。
- [開發與歷史產物索引](../../tools/green-material/README.md)：程式、報告與歸檔資料。

資料來源：[台灣 TABC 綠建材標章資料查詢](https://tabcmgr.hopto.org/mgr/SearchCaseAction.aspx)。若文件描述不一致，以 `domain/GM_*.md` 的方法為準。
