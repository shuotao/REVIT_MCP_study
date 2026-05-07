---
name: align-views-on-sheets
description: "批次對齊 view 範圍 + viewport 在 sheet 上的位置：三階段流程結合 ScopeBox、titleblock 對齊與 viewport 定位。階段一用 set_scope_box_for_views 把一組 view 的 CropBox 綁到指定 ScopeBox（範圍統一）；階段二（可選）用 align_titleblocks_on_sheets 對齊圖框 instance 位置（解決 change_element_type 後位置不一致）；階段三用 position_viewports_on_sheet 把這些 viewport 移到 sheet 上的指定錨點位置（位置統一）。適用於跨專案複製後對齊新 view、批次統一系列圖紙的 layout、出圖前最後對齊。觸發條件：使用者提到對齊 view、統一範圍、套用 scope box、批次定位 viewport、view 對齊 title block、對齊圖框、titleblock 對齊、align views to scope box、position viewports on sheet、align titleblock、sheet layout 對齊、批次出圖整理。工具：set_scope_box_for_views、align_titleblocks_on_sheets、position_viewports_on_sheet、get_viewport_map、get_sheet_viewport_details。"
---

# 批次對齊 view 範圍與 viewport 位置

兩個工具編排成統一流程，常見搭配 `copy-sheets-cross-project` 後續使用。

## Prerequisites

1. Revit 已開啟目標專案，且要對齊的 views 已存在
2. 目標專案有對應的 ScopeBox（會用名稱匹配）
3. 每張要對齊的 sheet 都有 title block（用作位置參考）
4. MCP Server 已連線：
   ```
   ToolSearch select:mcp__revit-mcp__set_scope_box_for_views,mcp__revit-mcp__position_viewports_on_sheet
   ```

## 概念：三階段對齊

| 階段 | 動作 | 結果 |
|------|------|------|
| **A. 統一範圍** | 套 ScopeBox 給 view | view 的 CropBox 自動跟隨 ScopeBox 範圍。所有套同個 ScopeBox 的 view 顯示範圍一致 |
| **B.（可選）統一圖框位置** | `align_titleblocks_on_sheets` 對齊 titleblock instance origin | 處理 change_element_type 後 titleblock 沒一起搬的情況。若所有 sheet 圖框已經對齊，跳過此階段 |
| **C. 統一 viewport 位置** | 移動 viewport 到 sheet 上指定錨點 | 不同 sheet 上的 viewport 都對齊到 title block 的同一個位置 |

階段順序：**先 A，再 B（如果需要），最後 C**。理由：
- A 改變 cropbox 範圍 → 影響 C 的計算結果
- B 改變 titleblock 位置 → C 的 `sheetReference: titleblock-*` 會跟著新位置算 offset，所以 B 必須在 C 前面

若所有 sheet 的圖框 instance origin 本來就一致（例如沒換過 type），可以直接跳過 B。

## Workflow

### 步驟 1：盤點要處理的 views

跟使用者對齊兩個維度：

1. **要套同一個 ScopeBox 的 view 集合**：通常是「同類型 + 同範圍意圖」的，例如：
   - 全部「(開審)」後綴的 FloorPlan
   - 全部 A1-XX 系列的樓層平面圖
   - 使用者明確列舉的 view names

2. **要對齊位置的 viewport 集合**：通常等於上一個集合（已經套 ScopeBox 的 view）

如果使用者不確定範圍，先用 `get_viewport_map` 列出 view → sheet 對應關係供參考。

### 步驟 2：確認目標 ScopeBox 名稱

使用者通常會直接告訴你 ScopeBox 名稱（例如「平面圖」）。

如果不確定 ScopeBox 是否存在於目標檔，**直接呼叫工具** — 找不到時錯誤訊息會列出所有可用 ScopeBox 名稱（discovery 用）。

### 步驟 2.5：階段 B — 對齊 titleblock 位置（可選）

僅在以下情境需要：
- 跨專案複製後對 sheet 跑過 `change_element_type` 換 titleblock 型號
- `get_sheet_viewport_details` 抓出來的 `Outline.MinX/MaxY` 各 sheet 不一致（兩群以上的 X、Y 座標）

呼叫 `align_titleblocks_on_sheets`：

```
align_titleblocks_on_sheets({
  referenceSheetNumber: "A1-04",       # 任一張位置正確的 sheet
  targetSheetNumbers: ["A1-01", ..., "A1-14"],
  anchor: "top-left",
  dryRun: true                          # 先預覽
})
```

或用絕對座標模式：`referencePositionMm: { x: -541.5, y: 592.0 }`。

**dryRun 回傳檢查重點**：
- `Moved[].DeltaMm` — reference sheet 應該 ≈ (0, 0)；其他 sheet 顯示要平移多少
- `AlreadyAligned: true` — 已在公差內（預設 0.1mm），這次跑也是 no-op
- `Skipped[]` — sheet 沒有 titleblock 會被跳過

確認後拿掉 dryRun 正式跑。

> 完成後再跑階段 C（`position_viewports_on_sheet`），viewport 會跟著新 titleblock 位置對齊。

### 步驟 3：執行階段 A — 套 ScopeBox

```
set_scope_box_for_views({
  scopeBoxName: "平面圖",                # 必填
  viewNameContains: "(開審)",            # 三選一識別
  viewTypeFilter: ["FloorPlan"]          # 可選過濾
})
```

**識別 view 的三種方式**（優先序 viewIds > viewNames > viewNameContains）：
- `viewIds`: 最精確（直接帶 ElementId）
- `viewNames`: 精確匹配名稱清單
- `viewNameContains`: 名稱包含子字串（最方便但需注意誤配）

**回傳重點**：
- `Applied` — 成功套用的 view 清單（含 ViewId, ViewName, ViewType）
- `Skipped` — 跳過的（view template / 不支援 ScopeBox 的類型）
- `Failed` — 失敗的（含錯誤訊息）

### 步驟 4：跟使用者對齊「位置三段組合」

位置定義是用三段組合：

```
[view 上的某個錨點] 移到 [sheet 上的某個參考點] + [offset]
```

| 組件 | 選項 |
|------|------|
| `viewAnchor` (view 的錨點) | top-left / top-right / bottom-left / bottom-right / center |
| `sheetReference` (sheet 的參考點) | titleblock-{top-left, top-right, bottom-left, bottom-right} 或 sheet-{...} |
| `offsetRightMm` | 正值往右、負值往左（mm） |
| `offsetDownMm` | 正值往下、負值往上（mm，**直覺 sheet 慣例**） |

**常見組合範例**：
- 「view 左上角貼齊 title block 左上角內側 1cm」
  ```
  viewAnchor: "top-left", sheetReference: "titleblock-top-left",
  offsetRightMm: 10, offsetDownMm: 10
  ```
- 「view 置中於 title block」
  ```
  viewAnchor: "center", sheetReference: "titleblock-bottom-left",
  offsetRightMm: <title block 寬/2>, offsetDownMm: <-title block 高/2>
  ```

### 步驟 5：強制 dryRun 預覽（重要 SOP）

**先用 `dryRun: true` 跑一次**，回報的 `Moved[].DeltaMm` 顯示每個 viewport 預期會移動多少：

```
position_viewports_on_sheet({
  viewAnchor: "top-left",
  sheetReference: "titleblock-top-left",
  offsetRightMm: 10,
  offsetDownMm: 10,
  viewNameContains: "(開審)",
  viewTypeFilter: ["FloorPlan"],
  dryRun: true                        # ← 預覽
})
```

**dryRun 回傳檢查重點**：
- `MovedCount` — 會處理的 viewport 數量是否符合預期
- `Moved[].DeltaMm` — 位移量是否合理（如果是「+1cm」變成「+10000mm」表示單位用錯了）
- `Skipped[]` — 哪些被跳過（無 title block 等）

### 步驟 6：確認後正式執行

把 `dryRun` 拿掉再跑一次（其他參數一樣）：

```
position_viewports_on_sheet({
  viewAnchor: "top-left",
  sheetReference: "titleblock-top-left",
  offsetRightMm: 10,
  offsetDownMm: 10,
  viewNameContains: "(開審)",
  viewTypeFilter: ["FloorPlan"]
})
```

### 步驟 7：視覺驗證（**強制 SOP，不要跳過**）

光看 dryRun 數字不夠，**必須在 Revit 視覺確認至少一張 sheet**。

1. 在 Revit 開任一張處理過的 sheet（建議挑大張的 floor plan 例如 04-4）
2. **量測 cropbox 左上角到 title block 左上角的距離**，看是不是真的 = 你指定的 offset
3. 確認 view 內容範圍 = ScopeBox 範圍（dashed 框線）

**若距離不對**：可能踩到 cropbox vs viewport box 的差異（見「常見陷阱 #8」）。立刻停止批次跑，先 debug 一張，避免錯誤大量複製。

## 常見陷阱

### 1. ScopeBox 找不到
找不到時錯誤會列出可用清單。檢查名稱是否一字不差（含中英文/全半形/空白）。

### 2. View 是 template
工具自動跳過 `view.IsTemplate == true` 的 view。從來源檔複製後若有 view 變成 template，要手動取消 template 狀態才會被處理。

### 3. View type 不支援 ScopeBox
Schedule, Legend, DraftingView 等不能綁 ScopeBox。會自動跳過記在 `Skipped[]`。

### 4. Sheet 沒有 title block
`position_viewports_on_sheet` 用 `sheetReference: "titleblock-*"` 時要靠 title block 計算位置。沒有 title block 的 sheet 會跳過。改用 `sheet-*` reference 用 sheet 印刷區邊界當基準（但通常 title block 才是視覺對齊基準）。

### 5. 多個 title block on 同一 sheet
工具用第一個 title block，不會 warning。若 sheet 設計特殊有多 title block，要用 `viewportIds` 明確指定避免影響。

### 6. Offset 單位混淆
工具一律用 **mm**，不是 cm。使用者說「1cm」要轉成 `10`。

### 7. dryRun 與正式執行的差異
dryRun **不會**啟動 Transaction，所以絕對不會修改檔案。但其他副作用（如 view metadata cache 更新）可能會發生 — 不過都只在記憶體，不存檔就沒事。

### 8. ⚠️ Cropbox vs Viewport Box 的差異（**核心觀念，必懂**）

`position_viewports_on_sheet` 的 `viewAnchor` 是**錨在 cropbox**（純 view 內容範圍），**不是錨在 viewport box outline**（含 view 標題、外溢 model 內容、annotation）。

兩者通常**不一樣**：
- `Viewport.GetBoxOutline()` = viewport 在 sheet 上的「整個可選範圍」，包含 view 標題、超出 cropbox 的標註
- View cropbox = `View.CropBox / view.Scale` 的範圍，純粹是設定為 view 顯示區的矩形

**舉例**：一個 Floor Plan view 用 ScopeBox 「平面圖」當 cropbox，但 model 的某些圖元（樹、停車格…）超出 ScopeBox 範圍。**Revit 仍然會把那些超出的內容繪在 sheet 上**，所以 viewport box outline 比 cropbox 大很多。視覺上：
- viewport box top-left ≈ view 標題「一層平面圖」位置
- cropbox top-left = ScopeBox 的左上角，在 sheet 上是中間偏上

如果 anchor 用 viewport box top-left，view 標題會跑到 (titleblock+30, 30)，但 cropbox 跑到更下面更右邊 → **看起來位置全錯**。

**修復（已套用，2026-05-01）**：用 `View.CropBox` (model 座標) + `view.Scale` 算 cropbox 在 sheet 上的尺寸，假設 cropbox 中心 = `Viewport.GetBoxCenter()`（Revit 慣例），再從 boxCenter 推算 cropbox 角落位置。

**回傳新欄位（驗證用）**：
- `CropboxWidthMm` / `CropboxHeightMm`：cropbox 在 sheet 上的尺寸
- `BoxWidthMm` / `BoxHeightMm`：viewport box outline 尺寸（通常比 cropbox 大）
- `DesiredCropboxTopLeftMm`：預期 cropbox 左上角座標（這個應該等於 reference 點 + offset）

**Debug 提示**：
- 若 dryRun 顯示 `DesiredCropboxTopLeftMm` 跟你預期的位置一致，但視覺上不對 → 可能 view.CropBox 跟 ScopeBox 沒同步（先跑一次 `set_scope_box_for_views` 確保 cropbox 是 ScopeBox 範圍）
- 若 BoxWidth >> CropboxWidth 但你想 anchor 的是 viewport box（不是 cropbox），目前工具不支援，要直接用 modify_element_parameter 或寫新工具

### 9. 多次 dryRun + execute 是冪等的
重跑 `position_viewports_on_sheet` 不會累積位移 — 每次都從當前狀態算到目標 anchor。所以即使前一次跑錯（用了錯的 offset），這次跑正確會直接覆蓋到正確位置，不需要先還原。

### 10. 兩階段順序「先 ScopeBox 後 Position」很重要
若順序反了：
- 先 Position：view 還沒套 ScopeBox，cropbox 是 view 預設範圍（可能很大或很小）
- 用此 cropbox 算 anchor → 位置正確
- 後套 ScopeBox：cropbox 範圍改變，但 viewport box center 不變
- 結果：cropbox 中心仍是原 boxCenter，但 cropbox 大小變了 → cropbox 角落位置 **改變了**，跟你 anchor 的位置不同了

**正確順序：先 A（ScopeBox）後 B（Position）**。若不小心反了，順序倒過來再跑一次 B 即可。

### 11. ⚠️ Cropbox 在 sheet 上的真實位置 ≠ viewport.GetBoxCenter（核心觀念）
**Bug 演化史**（2026-05-01 解決）：
- v1：假設 `cropbox center == viewport.GetBoxCenter` → 視覺驗證偏差 5-30mm
- v2：用 `view.Origin` model offset 推算 → 偏差超過 100mm，把 viewport 推出 sheet
- v3：用 `view.Outline.MinU/MinV ↔ boxOutline.MinX/MinY` 校準 → 偏差 3mm
- v4 (final)：用 `view.Outline.Center ↔ boxOutline.Center` 校準 → **偏差 < 0.5mm ✅**

**根因**：
- `Viewport.GetBoxOutline()` 是「所有可見元素的 sheet 上 bounding box」（含 view 標題、超出 cropbox 的 model 內容、annotation 等）
- 同一可見內容也由 `View.Outline` (BoundingBoxUV，sheet feet 單位) 表示
- **`Viewport.GetBoxOutline` 比 `View.Outline` 多了一圈固定 ~0.01 ft (3mm) 對稱 padding**
- `view.CropBox` 只是「視覺 cropbox 框」的 model 範圍, 不一定在 box outline 中心

**正確公式**：
```csharp
// 1. 用 CENTER 端校準 view.Origin 在 sheet 上的對應位置 (對稱 padding 自動抵消)
double sheetOriginX = boxCenter.X - (viewOutline.Min.U + viewOutline.Max.U) / 2.0;
double sheetOriginY = boxCenter.Y - (viewOutline.Min.V + viewOutline.Max.V) / 2.0;

// 2. 算 cropbox 中心在 model
XYZ cropCenterModel = cb.Transform.OfPoint(
    new XYZ((cb.Min.X + cb.Max.X)/2, (cb.Min.Y + cb.Max.Y)/2, ...));

// 3. 推算 cropbox 中心在 sheet
double cropCenterSheetX = sheetOriginX + (cropCenterModel.X - viewOrigin.X) / scale;
double cropCenterSheetY = sheetOriginY + (cropCenterModel.Y - viewOrigin.Y) / scale;
```

**為什麼用 CENTER 不用 Min**：boxOutline padding 在每邊各 ~3mm。`Min - Min` 校準會把 padding 算進 origin 位置，造成 3mm 系統性偏移。`Center - Center` 校準時，對稱 padding 在 center 中相互抵消。

**診斷工具 `debug_viewport_geometry`**：dump 一個 viewport 的所有座標資料（boxOutline / labelOutline / view.Origin / cropbox / cropbox.Transform / view.Outline UV）給人工分析時用。**遇到 anchor 位置不對時**，先跑這個工具 + 請使用者用 Revit 標註量實際距離 → 兩者比對能精確算出系統性偏差來源。

### 12. ⚠️ Socket Timeout vs 實際執行（非常重要）
**癥狀**：MCP 工具回 `Error: Command timed out`，但實際在 Revit 中操作已經執行成功。

**機制**：
- MCP socket client 的硬編碼 timeout（cross-doc 是 120s, 一般是 30s）
- C# 端在 ExternalEventManager queue 上排隊，可能被前面的長操作（例如 viewport 多次 dryRun）阻塞
- 即使 client 已經 timeout 拋錯，C# 端仍然會執行完並送回應
- 但 socket handler 已經 delete，回應被丟棄

**對策（依優先序）**：

**① 立刻 grep log 一次，不要等！**

很多操作 Revit 端只要 **3-10 秒就完成**（例如 25 個 viewport 移動只要 4 秒）。socket timeout 通常是其他 race condition，不是 Revit 真的慢。

```bash
# 找 RequestId（從錯誤訊息或 tail log）然後直接 grep
grep "<RequestId>" "/c/Users/lesle/AppData/Roaming/RevitMCP/Logs/RevitMCP_YYYYMMDD.log"
```
若有「已發送回應」 → 操作已完成，直接視覺驗證。

**② 不要因 timeout 重複呼叫**（會造成重複執行）

**③ 不要設長時間 background polling**（30+ 分鐘）

我這次踩過：socket timeout 後我 schedule 了 5 分鐘 polling 等回應，結果 Revit 4 秒就完成了。**Polling 只在預期 >30s 的真正長操作才有意義**（例如 cross-doc 開大 .rvt 檔）。

**④ 短操作 timeout 應該的處理流程**：
1. socket timeout 出現
2. **立刻** `tail` log 看最新一筆 RequestId 是否有對應回應
3. 有回應 → 完成了，繼續下一步
4. 無回應 → 等 5-10 秒再 grep 一次（不要長 polling）
5. 還是沒回應 → 可能 Revit 真的卡住，再 diagnose

**⑤ 視覺驗證 + 結果存檔**：在 Revit 中直接看結果，不要只看 MCP 回傳。

### 13. ⚠️ 視覺驗證 SOP（關鍵原則）
**位置/座標相關工具的數字精確 ≠ 視覺正確**。`DesiredCropboxTopLeftMm` 報告欄位可能因為公式 bug 顯示錯誤值。**必須在 Revit 用尺量**：
1. 跑完 position_viewports_on_sheet 後，挑一張 sheet（建議 04-X 平面圖系列大張）
2. **Annotate → Aligned Dimension** 標 cropbox 左上角到 title block 左上角內邊緣的 X、Y 距離
3. 用 mm 單位看數字 — 應該等於你指定的 offsetRightMm/offsetDownMm（誤差 < 0.5mm 算 OK）
4. 若誤差 > 1mm → 跑 `debug_viewport_geometry` + 提供實測距離給 AI 反推公式

**永遠不要假設「報告數字看起來對 = 實際位置就對」**，尤其是涉及 Revit 內部 transform 的工具。

### 14. ⚠️ 對齊到「指定 sheet 的當前 viewport 位置」workflow（常見需求）

使用者常說：「把所有 sheet 的 viewport 對齊到 A1-05 現在的位置」。這是**參考某張 sheet 的人工調整結果，把其他 sheet 都對齊過去**。

**錯誤做法（常踩坑）**：直接用 `get_sheet_viewport_details` 拿 viewport `Center`，然後用 `viewAnchor: "center"` 配對應 offset。

**為什麼錯**：`get_sheet_viewport_details` 回傳的 `Center` 是 **box outline center**，但 `position_viewports_on_sheet` 的 anchor 是 **cropbox 角落/中心**。兩者通常差 30-60mm（box 比 cropbox 大，且不對稱）。直接用 box center 算 offset → 結果會位移 ~53mm。

**正確 SOP**：

```
1. get_active_view → 取得當前 sheet ElementId
2. get_sheet_viewport_details(sheetId) → 取得目標 viewport 的 Outline (MinX/MaxY) 和 BoxWidth/BoxHeight
3. 用 dryRun 跑一次「top-left + offset(10, 10)」反查 BoxCenter 與 CropboxCenter 的偏移量 d
   （d.X = NewBoxCenter.X - DesiredCropboxTopLeftMm.X - cropboxWidth/2）
4. 推算目標 cropbox TL 在 sheet-coord：
   targetCropboxTL = (target.boxOutline.MinX + paddingLeft, target.boxOutline.MaxY - paddingTop)
   其中 padding 從步驟 3 的 d 推導
5. 算 offset：
   offsetRight = targetCropboxTL.X - titleblockTL.X
   offsetDown  = titleblockTL.Y - targetCropboxTL.Y
6. dryRun 驗證：目標 viewport 的 Delta 應該 ≈ (0, 0)；其他 viewport 的 Delta 是要移動量
7. 確認後 apply
```

**簡化版**（已知 ScopeBox 範圍一致，所有 cropbox 同尺寸）：

```
從之前任一次「top-left + offset(X, Y)」的 dryRun 抓 NewBoxCenter（這是 box 對齊到 offset(X,Y) 的位置），
比對目標 sheet 的 box center 差距 (dx, dy)。
新 offset = (X + dx, Y - dy)
```

**實例（2026-05-07）**：
- 已知 offset(10, 10) → 大部分 viewport NewBoxCenter = (-98.10, 345.64)
- 目標 A1-05 box center = (-68.10, 320.64)
- dx = -68.10 - (-98.10) = +30, dy = 320.64 - 345.64 = -25
- 新 offset = (10 + 30, 10 - (-25)) = (40, 35)
- 用 viewAnchor=top-left + offset(40, 35) → A1-05 Delta=0，其他 Delta=(+30, -25) ✅

**驗證點**：dryRun 結果裡，目標 sheet 的 `DeltaMm` 應該 ≈ (0, 0)。如果不是，offset 算錯了，重算。

## 與 copy-sheets-cross-project 的關係

跨專案複製剛建好新 view 後，新 view 通常需要：
1. 套 ScopeBox 統一範圍（階段 A）
2. 統一 sheet 上的位置（階段 B）

→ 直接接這個 skill 做後處理。流程：

```
copy-sheets-cross-project (執行 copy_sheets_from_file)
    ↓
align-views-on-sheets (本 skill)
    ├─ A: set_scope_box_for_views
    └─ B: position_viewports_on_sheet (dryRun → 正式)
```

## Reference

- C# 實作：
  - `MCP/Core/Commands/CommandExecutor.ScopeBox.cs`
  - `MCP/Core/Commands/CommandExecutor.ViewportPosition.cs`
- Tool schemas:
  - `MCP-Server/src/tools/scope-box-tools.ts`
  - `MCP-Server/src/tools/viewport-position-tools.ts`
