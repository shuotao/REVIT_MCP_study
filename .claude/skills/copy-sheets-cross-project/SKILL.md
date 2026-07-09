---
name: copy-sheets-cross-project
description: "跨專案複製含 model views 的圖紙：Revit 原生「Insert Views from File」無法匯入含 model view 的 sheets，本 skill 用兩階段流程（讀取→確認衝突→執行）自動化重建。處理 view 四級分類（Tier 1 自動建立 / Tier 2 部分建立 / Tier 3 只匹配 / Tier 4 人工處理）、ScheduleSheetInstance 重建、DraftingView 內容跨文件複製、Sheet custom parameters（圖集/Discipline/繪圖人等）一併複製、衝突解決需使用者確認。內含補丁工具 sync_sheet_parameters_from_source 用於補齊舊 sheet 的 metadata。觸發條件：使用者提到跨專案複製圖紙、複製 sheets、Insert Views from File、轉移圖紙、source.rvt、import sheets、移植視圖、把另一個 rvt 的圖紙複製過來、cross-document、cross-project、補 sheet folder、修圖紙分組。工具：read_source_file_sheets、copy_sheets_from_file、sync_sheet_parameters_from_source。"
---

# 跨專案複製含 Model Views 的圖紙

執行前請先理解：Revit API 不支援跨文件直接複製 model views，本 skill 採「讀取 metadata → 匹配/重建 → 放置 viewport」策略。

## Prerequisites

1. Revit 已開啟**目標**專案（要接收圖紙的專案）
2. **來源** .rvt 檔案的絕對路徑可存取（同電腦或可讀的網路路徑）
3. MCP Server 已連線：
   ```
   ToolSearch select:mcp__revit-mcp__read_source_file_sheets,mcp__revit-mcp__copy_sheets_from_file,mcp__revit-mcp__sync_sheet_parameters_from_source
   ```

## ⚠️ 步驟 0：Pre-flight Checklist（容易踩雷必看）

### 0a. 確認方向（最常見錯誤）

工具設計：**Revit 中作用中的視窗 = 接收 sheets 的 target 檔；source 用檔案路徑指定**。

使用者常以為「目前作用中的就是 source」，要主動跟使用者確認：
1. 用 `get_project_info` 確認當前作用中的專案名稱
2. 跟使用者對話：「目前作用中的是 X，這就是要**接收** sheets 的目標檔嗎？」
3. 如果反了，請使用者在 Revit 的 **Window 選單** 切換到目標檔，再回報你

### 0b. 確認 source 已開啟在 Revit 中

要使用者在 Revit 同時開啟 source 和 target 檔。原因：
- 已開啟的 source doc 可被 reuse（程式碼會 check `app.Documents`）
- 從磁碟開啟 354MB 的 .rvt 約 30-90 秒，會吃掉大半個 socket timeout
- Reuse 已開啟的 doc → 開啟成本 0 秒

驗證方式：在 Revit 的 Window 選單應該看到兩個檔名都列在下拉清單裡。

### 0c. 存檔提醒

開始前提醒使用者：
- **target 檔**：執行前**先存檔**（萬一中途出狀況才能 rollback）
- **source 檔**：執行**過程中可能出現「已修改」星號**（因為 view metadata cache 更新等記憶體變化）。**完成後關 Revit 時務必選「不要儲存 source」**，磁碟原檔才不會被影響。

### 0d. 重啟 MCP server 的時機

如果剛部署新 DLL 或新 TS schema，必須重啟 Claude Code session 才能呼叫新工具。在這個 skill 執行前，先確認三件事：
- `read_source_file_sheets` schema 載入 ✅
- `copy_sheets_from_file` schema 載入 ✅  
- `sync_sheet_parameters_from_source` schema 載入 ✅

用 `ToolSearch select:mcp__revit-mcp__sync_sheet_parameters_from_source` 驗證最新 schema 有沒有載到。

## View Type 四級分類（必讀）

| Tier | 行為 | 包含的 ViewType |
|------|------|---------------|
| **Tier 1** 可自動建立 | 條件成熟可建立，**仍需先問使用者** | FloorPlan, CeilingPlan |
| **Tier 2** 可部分建立 | 框架建立 OK，內容/幾何可能要微調 | Section, Detail, ThreeD, DraftingView |
| **Tier 3** 建議只匹配 | 不自動建立，找不到時加入 manual_action | Legend, Schedule, ScheduleSheetInstance |
| **Tier 4** 必須人工 | API 無法可靠建立，只回報資訊 | Elevation, Callout, AreaPlan, Rendering |

## Workflow

### 步驟 1：讀取來源檔並偵測衝突

```
read_source_file_sheets({
  sourceFilePath: "C:/path/to/source.rvt",
  sheetNumbers: ["A101", "A102"],   // 省略 = 全部
  keepOpen: true                      // 預設 true，避免後續重複開啟
})
```

回傳重點欄位：
- `sheets[].viewports[].tier` — 每個 view 的 tier 分類
- `sheets[].scheduleInstances` — placed schedule 清單
- `conflicts.sheets` — 同編號圖紙
- `conflicts.views` — 同名 view（注意 tier）
- `conflicts.schedules` — `schedule_exists_in_target` 或 `schedule_not_found_in_target`
- `conflicts.viewTemplates[]` — 來源有但目標沒有的 ViewTemplate（status=`will_be_auto_copied`，執行 copy 時自動複製）
- `targetMatchPreview` — 目標檔現有資源（levels, view templates 等）

### 步驟 2：向使用者確認衝突處理方式（**強制執行**）

逐項列出衝突並請使用者決策。**不可自行假設**。

#### Sheet 衝突
> 「來源 sheet `A101` 在目標檔已存在同編號，要：
> - `skip`（跳過此 sheet）
> - `rename`（自動加後綴 `A101-1`）
> 哪一個？」

#### View 衝突
> 「來源 view `Level 1` (FloorPlan, Tier 1) 在目標檔已存在，要：
> - `use_existing`（直接用現有 view，最安全）
> - `overwrite`（刪除現有並重建，會破壞其他圖紙的引用）
> - `rename`（建立新 view 加後綴）
> 哪一個？」

**警告：`overwrite` 會刪除原 view，導致目標檔其他圖紙上的 viewport 失去引用。** 預設建議 `use_existing`。

#### Schedule 衝突
> 「來源 sheet 上有 `Room Schedule`，目標檔已有同名 schedule（欄位數不同：source 8 / target 6），要：
> - `use_existing`（用目標檔的版本，欄位以目標檔為準）
> - `skip`（不放置此 schedule）
> 哪一個？」

#### Type 衝突（CopyElements 全域設定）
> 「複製 DraftingView 內容時若遇到同名 family type（例 TextNoteType `2.5mm Arial`），要：
> - `use_destination`（用目標檔的 type，安全預設）
> - 來源 type 會被略過，視覺上可能略有差異
> 確認用 `use_destination` 嗎？」

> ⚠️ 注意：Revit API 的 `IDuplicateTypeNamesHandler` 只支援 `UseDestinationTypes` 或 `Abort`。本工具實作固定使用 `use_destination`（已在 C# 端鎖定，使用者選什麼都會用 destination）。

### 步驟 3：執行複製

帶入使用者決策呼叫 `copy_sheets_from_file`：

```
copy_sheets_from_file({
  sourceFilePath: "C:/path/to/source.rvt",
  sheetNumbers: ["A101", "A102"],
  viewMatchStrategy: "match_or_create",   // 或 "match_only" 更保守
  conflictResolution: {
    sheets: { "A101": "rename" },
    views: { "Level 1": "use_existing", "Detail_A": "rename" },
    schedules: { "Room Schedule": "use_existing" },
    typeConflicts: "use_destination"
  },
  copyDraftingContents: true,
  syncProperties: {
    scale: true, cropBox: true, viewTemplate: true,
    detailLevel: true, displayStyle: true
  },
  closeAfterCopy: true
})
```

回傳重點：
- `summary.{sheetsCreated, viewsMatched, viewsCreated, viewportsPlaced, viewTemplatesCopied, manualActionsRequired}`
- `viewTemplatesCopied[]` — 從來源自動複製到目標的 ViewTemplate 清單（目標檔不存在同名 template 時自動複製）
- `manualActionRequired[]` — 必看，使用者要手動處理
- `warnings[]` — 注意但不阻塞的問題
- `sheetErrors[]` — 失敗的 sheet（其他 sheet 仍會繼續）

### 步驟 4：處理 manual_action_required

每一筆告知使用者具體該做什麼：

| `type` | 處理方式 |
|--------|---------|
| `Elevation` | 在目標檔手動建 ElevationMarker → 命名相同 → 重跑用 `match_only` |
| `Callout` | 先確保 parent view 存在 → 手動建 Callout → 命名相同 → 重跑 |
| `AreaPlan` | 確認目標檔有對應 AreaScheme → 手動建立 |
| `schedule_not_found` | 用 `create_view_schedule` 工具或手動建 schedule → 重跑 |
| `Legend_not_found` | 從來源檔用 Revit 原生 Insert from File → 重跑 |
| `view_already_placed` | 該 view 已在另一張 sheet（非 Legend 不能重複放）→ 用 `View.Duplicate` 或從原 sheet 移除 |

### 步驟 5：驗證結果

1. 在 Revit 中確認新建立的 sheets 出現在 Project Browser
2. 開啟其中一張 sheet，比對 viewport 位置與來源是否吻合
3. 檢查 view scale / view template / crop box 是否正確同步
4. 若有 DraftingView：開啟確認 detail lines, text notes 是否完整複製

### 步驟 6（可選）：後處理對齊 + sheet folder 補丁（強烈建議）

複製完的新 view 通常會有三個問題：
1. **CropBox 範圍不對**：直接從來源複製，可能跟目標檔模型原點不對齊
2. **Viewport 在 sheet 上的位置不對**：複製是按來源 sheet 上的座標放置，跟目標檔的 title block 邊界可能對不齊
3. **Sheet folder 沒帶過來**（**舊版 bug，已修**）：在 2026-05-01 之前的版本，`copy_sheets_from_file` 不會複製 custom parameters，新 sheet 會跑到 Project Browser「??」folder。**新版 (`copySheetCustomParameters: true` 預設) 會自動帶**

對應修正流程：

**6a. 套 ScopeBox 到新 view（用 `align-views-on-sheets` skill）**
```
set_scope_box_for_views({
  scopeBoxName: "平面圖",
  viewNameContains: "(開審)",
  viewTypeFilter: ["FloorPlan"]
})
```

**6b. 統一 viewport 位置（用 `align-views-on-sheets` skill）**
```
position_viewports_on_sheet({
  viewAnchor: "top-left",
  sheetReference: "titleblock-top-left",
  offsetRightMm: 10, offsetDownMm: 10,
  viewNameContains: "(開審)",
  viewTypeFilter: ["FloorPlan"],
  dryRun: true     # 先預覽，確認後拿掉
})
```

**6c. 補 sheet folder（**僅針對舊版複製的 sheets**）**

若先前用舊版 `copy_sheets_from_file`（沒帶 custom param）建出的 sheets 沒有 folder，用補丁工具修：

```
sync_sheet_parameters_from_source({
  sourceFilePath: "X:\\path\\to\\source.rvt",
  sheetNumbers: ["A101", "A102", ...],   # 省略 = 補所有 target 中能在 source 找到同號的 sheet
  closeAfterSync: false
})
```

特性：
- **不重建 sheet、不動 viewport**，只比對同 SheetNumber 並補 custom parameters（圖集 / Discipline / 繪圖人 等）
- 同名 + 同 StorageType 的 writable parameter 才會被複製
- ElementId 類型跳過（跨文件 ID 不可移植）
- 用 target.SheetNumber 作主鍵；source 中找不到對應的會列在 NotMatched

詳細位置/範圍對齊流程見 `align-views-on-sheets` skill。

## 常見陷阱

### 1. 來源檔過大（>500MB）
工具會直接拒絕。建議使用者先在 Revit 開啟來源檔 → Purge Unused → 另存小檔。

### 2. 來源 = 目標
工具會 throw。確認使用者真的要從**另一個** .rvt 複製，不是同一個。

### 3. Worksharing 來源檔
工具自動偵測 `BasicFileInfo.IsWorkshared` 並 detach。不會修改 central model。

### 4. CropBox 在不同模型原點的專案間偏移
若兩個專案的 Project Base Point 不同，FloorPlan 的 CropBox 可能落在錯位置。
- 建議：先驗一張，確認位置後再批次跑
- 補救：之後用 `align_view_cropbox_to_element` 對齊到 grid 或 detail group

### 5. View 已在別張 sheet 上
非 Legend view 在 Revit 中只能放一張 sheet。若 `viewMatchStrategy=match_or_create` 且使用者選 `use_existing`，但該 view 已被佔用 → 進 `manual_action_required[type=view_already_placed]`。

### 6. Section 切面位置錯誤
Section 用來源的 CropBox.Transform 重建。若兩個專案模型幾何差異大（例如建築朝向不同），切面可能切到錯位置。建議手動微調 section line。

### 7. ScheduleSheetInstance 找不到對應 Schedule
工具不自動建立 ViewSchedule（field/filter/sorting 太複雜）。提示使用者：
1. 用 `create_view_schedule` 建空殼
2. 在 Revit 手動補欄位
3. 重跑此 skill

### 8. Sheet folder（圖集分組）是 custom parameter，不是 built-in
Revit Project Browser 的 sheet 分組是用 **Browser Organization** 規則 + **某個 project parameter 的值**（常見名稱：「圖集」、「Sheet Set」、「Discipline」、「Drawing Type」）。**新版 `copy_sheets_from_file` 預設 `copySheetCustomParameters: true` 會自動複製**，所以不會踩到。**舊版（2026-05-01 之前）漏了這段**，舊資料用 `sync_sheet_parameters_from_source` 補丁。

需要使用者先在 target 檔設定**同名 project parameter**才會有匹配對象 — 若 target 沒這 parameter，不會自動建立、會靜默跳過。

### 9. Socket timeout vs C# 處理時間（大型來源檔）
**癥狀**：`read_source_file_sheets` 或 `copy_sheets_from_file` 回傳「Command timed out」錯誤訊息。

**真相**：MCP socket timeout = 120s（這次部署已從 30s 提高），但 C# 端**仍在繼續處理**。Revit 完成後送回應，但 socket handler 已 delete，回應被丟棄。

**檢驗方式**：
1. 看 `%AppData%\RevitMCP\Logs\RevitMCP_YYYYMMDD.log` 找對應 RequestId 的「已發送回應」
2. 比對發送 vs 接收時間 → 知道實際耗時

**處理策略**：
- 確認 source 檔已開啟在 Revit 中（reuse open doc，省掉開檔的 30-90 秒）
- 縮小 sheetNumbers 範圍，分批跑
- 或修 C# 端讓 DetectConflicts 只掃指定的 sheets（已修，2026-05-01）

**polling log 範例（PowerShell）**：
```bash
until grep -q "<RequestId>.*已發送回應" "$env:APPDATA\RevitMCP\Logs\RevitMCP_*.log"; do sleep 5; done
```
若 C# 已完成（log 有「已發送回應」），代表 Revit 已寫入修改 — 即使 socket 收不到回應，**修改實際上已執行**。

### 10. 新工具部署後必須重啟 Claude Code session
C# DLL 更新只要重啟 Revit。但**新增的 TypeScript tool schema** 需要 MCP server process 重啟才會註冊。實務上 = 重啟 Claude Code session（Ctrl+C 退出 → 重新 `claude`）。對話 context 會丟，但計畫檔（`plans/`）和 SKILL.md 都是持久的。

### 11. ⚠️ Sheet 上「Sheet Number」/「Sheet Name」有同名 duplicate parameter（已修，留作警惕）
**癥狀**：執行 `sync_sheet_parameters_from_source` 或 `copy_sheets_from_file`（含 `copySheetCustomParameters: true`）時，Revit 跳出「Sheet Number is empty - cannot be ignored」錯誤 dialog，整個 transaction rollback，**所有 custom parameter 沒寫入**。

**根因**：Revit Sheet 元素上有兩個 parameter 叫做 `Sheet Number`：
- 真正的 `BuiltInParameter.SHEET_NUMBER`（值是 sheet 編號）
- `BuiltInParameter.VIEWPORT_SHEET_NUMBER` 之類的 viewport 跨參考 parameter（值通常是空字串）

兩個顯示名都是 `Sheet Number`。同樣，`Sheet Name` 也有同名 duplicate。

舊版 sync 只用 `BuiltInParameter` 跳過 SHEET_NUMBER/SHEET_NAME。但**第二個 duplicate 是不同的 BuiltInParameter，沒被跳過**。`LookupParameter("Sheet Number")` 在 target 上回傳真正的 SHEET_NUMBER（按名稱找第一個），於是 sync 把 target 的 sheet number 設成 ""，Revit 報錯，整個 transaction rollback。

**修復（已套用，2026-05-01）**：在 helper 內**用「名稱」雙保險**跳過：

```csharp
var skipNames = new HashSet<string>(StringComparer.Ordinal) {
    "Sheet Number", "Sheet Name",
};

if (skipNames.Contains(sourceParam.Definition.Name)) continue;
```

**通用教訓**：Revit element parameter 可能存在**同名 duplicate**（不同 BuiltInParameter 但顯示名相同）。寫批次 parameter 處理工具時：
- 不要只用 `BuiltInParameter` enum 做去重
- 用名稱 + value-empty 檢查雙重保險
- 處理跨文件 parameter sync 時，特別小心 `LookupParameter(name)` 對同名 duplicate 的不可預期回傳順序

### 12. ⚠️ 大批 sync 失敗時可能不會顯眼回報
舊版 bulk sync 用單一 transaction 包 72 張 sheet。Sheet 1 hit error → Revit 顯示 dialog → 使用者點 Cancel → transaction rollback **但回傳結果仍是 `Success: true`**（因為 C# 端認為 commit 路徑沒 throw exception）。

實際上**沒有任何 sheet 被寫入**，使用者看 Project Browser 才發現 folder 全空。

**對策**：
- 執行後**用 `get_element_info` 抽樣驗證**（不要相信 Success: true）
- 看 Properties 面板 / Project Browser 的 folder 結構
- 若有大批失敗，立即 stop 並 debug 而非繼續批次跑

### 13. ⚠️ change_element_type 不會搬動 instance origin（後處理 titleblock 換型常踩）

**情境**：跨專案複製完成後，使用者通常希望把新 sheet 的 titleblock 換成目標專案的標準型（例如 `A_基設圖框_A1: A1_一般`）。直覺做法 = 對每張 sheet 的 titleblock instance 跑 `change_element_type`。

**陷阱**：`change_element_type` 只改 type，**完全保留 instance origin（XYZ 位置）**。但不同 titleblock family 的「插入點 ↔ 圖框幾何」對應關係**通常不同**（有些插入點在外框左下角、有些在中心、有些在標題區）。

**結果**：換完 type 後，雖然所有 sheet 用同一個 type，但**圖框在 sheet-coord 上的位置仍然不一致**：
- 來源 family A 的插入點在外框左下 → 換 type 後外框 TL 在 sheet-coord (X1, Y1)
- 目標 family B 的插入點在外框中心 → 換 type 後外框 TL 在 sheet-coord (X1 + width/2, Y1 + height/2)

實際案例（2026-05-07）：13 張 sheet 換 titleblock type 後，A1-05 的 titleblock TL 在 (-41.38, 588.35)，其他 12 張在 (-541.5, 592.0)，差 500mm。

**雪上加霜**：`position_viewports_on_sheet` 用 titleblock-top-left 當錨點；titleblock 位置不一致 → viewport 即使跑 align 也會跟著 titleblock 跑 → 列印一致但 Revit UI 上絕對位置不同。

**SOP（換 titleblock type 後驗證+對齊）**：

1. 用 `get_sheet_viewport_details(sheetId)` 對每張 sheet 抓 titleblock TL（從 `Outline.MinX, Outline.MaxY`）
2. 比對所有 sheet 的 TL — 若有不一致 → titleblock instance origin 不齊
3. 找出多數 sheet 的「正確位置」當基準（或選任一張位置 OK 的 sheet 當 reference）
4. **直接呼叫 `align_titleblocks_on_sheets`**（2026-05-07 新增工具）：
   ```
   align_titleblocks_on_sheets({
     referenceSheetNumber: "A1-04",     # 任一張位置正確的 sheet
     targetSheetNumbers: ["A1-01", ..., "A1-14"],  # 全部要對齊的
     anchor: "top-left",
     dryRun: true                        # 先預覽 delta
   })
   ```
   或用絕對座標：`referencePositionMm: { x: -541.5, y: 592.0 }`
5. dryRun 看 `Moved[].DeltaMm` — reference sheet 應該 ≈ (0, 0)，其他 sheet 顯示要平移多少
6. 確認後拿掉 dryRun 正式跑
7. 完成後重跑 `position_viewports_on_sheet` 確認 viewport 一起對齊

**預防（首選）**：跨專案複製時，**讓目標檔的 titleblock family 和來源檔同名同族**，這樣就不需要換 type，instance origin 也保持一致。

## 兩階段為什麼必要

不要嘗試「一次跑完」。必須**先 read 再 copy**，原因：
1. 衝突清單必須由**使用者**決策，不是 AI 自行假設
2. Tier 4 view 需要使用者知情才能規劃手動處理
3. 來源檔開啟成本高（可能 10-30 秒），keepOpen=true 讓兩次操作共用同一個 doc

## Reference

- 計畫文件：`C:\Users\lesle\.claude\plans\revit-api-graceful-noodle.md`
- C# 實作：`MCP/Core/Commands/CommandExecutor.CrossDocument.cs`
- Tool schemas：`MCP-Server/src/tools/cross-document-tools.ts`
