# 相機管理面板 路線圖

> 狀態標記：✅ 完成（已編譯、已部署或待部署）｜🔄 進行中｜⏳ 規劃中。「完成」不等於「已實機驗證」——實機驗證狀態看最後的「未驗證清單」。
> 現行行為見 [spec.zh-TW.md](spec.zh-TW.md)；決策見 [adr.zh-TW.md](adr.zh-TW.md)；歷史見 [changelog.zh-TW.md](changelog.zh-TW.md)。

## 原始三階段（2026-09-24 使用者拍板）

| 階段 | 內容 | 狀態 |
|---|---|---|
| 1. 停靠面板 | 面板、清單、就地編輯、雙向連動、人視高預設值、複製到其他樓層、匯出 Excel、③ 位置圖＋視野扇形、並排／還原、預覽視窗 | ✅ 完成（使用者實機回饋多輪，未完成最終驗收） |
| 2. Key Plan 與相機標記 | 一鍵 4 視圖（平面／剖面／等角）、B 型標記、表現法、室內／室外、每層總覽、A 型族群標記、拖標記連動相機 | ✅ 完成（Wave 2 於 2026-09-25 補齊；未實機驗證） |
| 3. 出圖與版本 | 一鍵排圖紙、相機視角版本、視覺精修 | ✅ 完成（Wave 3 於 2026-09-25；未實機驗證） |

## 收尾波次（2026-09-25 起）

### Wave 1｜MCP 測試工具　✅ 完成（2026-09-25）

讓 AI 能自動測試面板背後的同一套服務程式碼（D-023）。

| 工具 | 狀態 |
|---|---|
| `list_cameras` | ✅ |
| `run_camera_keyplan` | ✅ |
| `set_camera_pose` | ✅ |
| `copy_camera_to_level` | ✅ |
| `get_view_extents` | ✅ |

- ✅ 面板內「開啟並排」「編輯請求組裝」移到服務層共用。
- ✅ 規格書拆成四份文件。
- ✅ 工具已於 2026-09-26 由主線以 MCP 實際呼叫並複測，見「MCP 自測已驗證（2026-09-26）」。

### Wave 2｜Key Plan 完整版　✅ 完成（2026-09-25，已編譯、未實機驗證）

| 項目 | 狀態 |
|---|---|
| 1. 每層總覽平面 `Key Plan-總覽-<樓層名>`（④ ▾ 更新樓層總覽、一鍵 4 視圖自動更新、MCP `run_camera_overview`） | ✅（spec §10.10、D-028） |
| 2. A 型相機標記（族群「RevitMCP 相機標記」＋扇形 DirectShape、透視圖隱藏子類別、⋯ 相機標記 B／A／A+B） | ✅ 部分：扇形**不是**族群參數驅動，改用 DirectShape；族群不隨俯仰角傾斜（D-026） |
| 3. 拖標記 → 相機 | ✅ 部分：只支援 A 型實例；B 型詳圖標記沒有群組，不支援（D-027） |

- MCP 工具數 217 → 218（`run_camera_overview`）。

### Wave 3｜出圖與版本　✅ 完成（2026-09-25，已編譯、未實機驗證）

| 項目 | 狀態 |
|---|---|
| 1. 一鍵出圖紙 `Key Plan-<相機名>`（④ ▾、右鍵、MCP `create_camera_sheet`；④ ▾ 圖紙圖框 ▸） | ✅（spec §10.13、D-029） |
| 2. 視角版本（右鍵 視角版本 ▸ 儲存／還原／刪除；MCP `camera_version`） | ✅（spec §11.5、D-030） |
| 3. 視覺精修：過時 ToolTip 全面更新、右鍵選單加 ToolTip、8 px 間距、清單提示縮短、⑥ 狀態列保留最近一次動作＋時間 | ✅ 部分：字體沿用既有 `Segoe UI, Microsoft JhengHei UI` 12／11／13 px，未另調；主要／次要按鈕沿用既有 Primary／Secondary 樣式 |

- MCP 工具數 218 → 220（`create_camera_sheet`、`camera_version`）。
- 版面比例（左 60%、右欄三等分）與圖紙邊界 15 mm 是常數／本機設定，沒有 UI；實測後再決定要不要開放。

### 樓層／房間判定修正第五輪（2026-09-26 17:30）　⏳ 已編譯（R22–R26 皆 0 錯誤）、未部署（Revit 執行中）、待 MCP 複測（build R24 SHA256 F77FC34B…8832F025）

MCP 回歸（公開範例 Snowdon Towers，20 台）17 台正確、3 台錯誤，依下列規則修正：

| # | 相機 | 舊結果 | 修正規則 | 預期（部署後 `list_cameras`） |
|---|---|---|---|---|
| 1 | Camera at Kitchen 01 | 眼睛在夾層房間底下 1826 mm，仍以 nearestZ 判進夾層 → L2、−261 cm | nearestZ 只接受「眼睛低於房間底 ≤ 600 mm」，眼睛在房間頂以上一律不接受；低於房間底時若眼下站立面在房間範圍外（< 底−600 mm）也不接受 | 夾層被排除 → 室外（或眼睛實際所在的房間），站立面 −1803 mm → 樓層 L1 - Block 35、≈165 cm |
| 2 | Camera in Stair 3 - Parking Level | 房間範圍底 −5144、Parking −5156（低 12 mm）不列候選 → 退回房間 Level L2、−500 cm | 房間範圍收候選 Level 容差 ±300 mm；都不符合時改取全模型 ≤ 眼睛−300 mm 的最高建築樓層（優先有平面），不再盲目用房間 Level | Parking、≈262 cm（LevelSource=roomSpan） |
| 3 | Camera - Fifth Floor Roof Garden | 眼睛在 403 房間頂上 1911 mm 仍判進 → 室內 L4、582 cm | 同 #1（房間上方不接受）→ 室外；射線打到的站立面（12729 mm，機房樓板）比眼下房間頂（13716 mm）低 > 300 mm → 推定站立面＝房間頂（LevelSource=roomTop），並記射線診斷（隱藏類別、連結、不限類別的較近命中） | 室外；樓層＝≤ 13716+300 mm 的最高有平面 Level（預期 L5／屋頂層）、≈191 cm。若仍為 L4，看 log「射線診斷」判斷屋頂版漏打原因 |
| 4 | 全部 | — | 合理性檢查：離樓層 < 0 或 > 6 m、且眼下 6 m 內有站立面 → 改以站立面判定（log「高度不合理 → 改以站立面判定」）；空拍相機維持高程規則 | 17 台原本正確者不變 |

- 射線類別加 `OST_Hardscape`（有此列舉的版本才加）；原本已含 Floors／Roofs／Topography／Site／BuildingPad／Toposolid 與連結模型。
- 快取值鍵加規則版本（`StoreyRuleVersion`=5），舊判定一律失效。

### 全面稽核修正（2026-09-26 15:19）　⏳ 已編譯（R22–R26 皆 0 錯誤）、未部署（Revit 執行中）、待 MCP 複測（build R24 SHA256 79368FB8…1A239BAC2）

三路唯讀稽核（核心 Key Plan／標記與版本／面板與事件）後一次修正。每項都「已編譯、未經 Revit 實機驗證」；可用 MCP 驗的見下方「MCP 回歸測試腳本」，只能人工驗的見表末。

| # | 問題（稽核確認） | 修正 | 狀態 |
|---|---|---|---|
| 1 | 關檔再開同一檔（未編輯）時房間／樓層快取命中，回傳已關閉文件的 Room → 一鍵 4 視圖／標記 `InvalidObjectException` | 文件開啟／關閉／另存新檔清掉全部以路徑為鍵的快取並 bump epoch；快取命中時檢查 Room `IsValidObject` | 待 MCP 複測（腳本 R-8） |
| 2 | `Commit()` 回傳 RolledBack 不丟例外（SilentFailuresPreprocessor），呼叫端回報成功、甚至讀回滾元素而崩 | 相機相關 26 處改 `TransactionHelper.CommitOrThrow`，未提交即走既有 catch | 待 MCP 複測（間接：腳本全程無假成功） |
| 3 | 面板佇列動作以「執行當下」作用中文件套用排入時的 Id（兩份同模型複本會改錯檔） | `EnqueueForLoadedDoc`：排入時記來源文件，執行時不符就取消並清進行中旗標；DocumentChanged 只收面板來源文件；`set_camera_pose` 後續更新、ZoomToFit 排程同樣綁文件 | 人工（兩份文件） |
| 4 | 另存新檔後文件鍵改變，導覽監看／標記計時器／DocumentClosing 靜默失效 | DocumentSavingAs／SavedAs 改認新鍵 | 人工 |
| 5 | Undo／Redo 我們的交易：標記停在舊狀態且被記為已畫；Undo 相機又觸發新交易清掉 Redo | Undo／Redo 時清「已畫」紀錄＋bump epoch，不排會開交易的後續更新 | 人工 |
| 6 | 導覽監看把本工具自己的編輯（面板／MCP／還原版本／拖 A）當成使用者導覽，1 秒後重跑後續更新 | 這些交易 Commit 時重設導覽基準；面板隱藏時不監看 | 待 MCP 複測（腳本 R-3 log） |
| 7 | 剖面移動路徑中途失敗且剖面在圖紙上 → 半移動狀態被提交，之後被誤判「手動移過」永久錯位 | 移動路徑包 SubTransaction，失敗先回滾再走退路 | 待 MCP 複測（腳本 R-5 無「移動路徑失敗」；失敗路徑需釘選觸發） |
| 8 | 剖面對位時被刪除重建 → 新剖面沒樣式／樓層記號 | 偵測到重建即補跑完整 Key Plan 更新 | 待 MCP 複測 |
| 9 | 只改高度時 A 標記不動（實例與扇形在眼睛高度） | 高度分級改為「剖面標記＋A」 | 待 MCP 複測（腳本 R-3） |
| 10 | 標記畫失敗（子交易回滾）仍記為「已畫」→ 之後永遠略過 | `DrawMarkerSafely` 回傳成敗，只記成功者；重畫組失敗不中斷其他組 | 待 MCP 複測 |
| 11 | 拖 A 標記到鎖定的相機 → 整批回滾、每次拖都重演 | 鎖定相機略過並回報；每台相機獨立子交易 | 人工（鎖定視圖） |
| 12 | 版本 JSON 損毀時讀成空清單、下一次儲存覆蓋全部版本；單筆壞資料讓整份清單變空 | 損毀拒寫；單筆壞資料只略過該筆 | 人工（需手改 ES） |
| 13 | 還原版本：透視圖「裁剪啟用」設定丟例外時連裁剪框也沒還原 | 兩者分開 try、各自列警告 | 待 MCP 複測（腳本 R-2） |
| 14 | `set_camera_pose`／面板接受 NaN、俯仰角 ±90°、遠裁剪 ≤ 0 | `ValidatePoseEdit`（俯仰 < ±89.5°、遠裁剪 > 0、高度 ±1 km） | 待 MCP 複測（腳本 R-2） |
| 15 | 匯出 Excel：任一相機缺高度／FOV／遠裁剪 → ClosedXML 拒收 NaN，整份匯出失敗 | 缺值寫空白 | 人工（面板 ⋯ 匯出） |
| 16 | 視圖／圖紙名稱含 Revit 不允許字元 | `SanitizeRevitName`（`MakeUniqueName`／類型名／圖紙名／複製到樓層）；清單改名遇到非法字元明確拒絕 | 人工（改名輸入 `a:b`） |
| 17 | A 扇形樣式讀回永遠「不符」／「讀不到扇形幾何」（診斷誤報） | 以面樣式為準（DirectShape 實體層固定 -1）；讀不回元素幾何時改驗建立的實體 | 待 MCP 複測（腳本 R-6） |
| 18 | 其他 | 面板回中性狀態時清所有進行中旗標；過期底圖回呼不再清新匯出的旗標；預覽視窗建立中文件關閉則丟棄；遠裁剪開關列入姿態雜湊；清單高度／遠裁剪欄一律小數點；⑤ 人視高接受小數逗號；`set_section_box` 容許 `viewId: null`；Key Plan／標記顏色交易不再觸發樓層記號重畫；R22／R23 `get/set_link_display_settings` 以版本常數保護（回報「需要 Revit 2024 以上」） | 部分人工 |

### 面板改高度確實重繪＋底圖排除自產視圖　⏳ 已編譯、未部署（Revit 執行中）、待複測（2026-09-26 14:47，build R24 SHA256 E2A8B3BF…F4E80D11）

| # | 項目 | 複測方式 | 預期 |
|---|---|---|---|
| a | 面板改高度（並排 4 視圖、焦點在面板） | 清單「離樓板 cm」改值按 Enter，重複 3 次 | 透視圖每次都變；log 重繪欄列出 `UpdateAllOpenViews …ms＋ZoomNudge「相機名」…ms`（＋作用中時 `RefreshActiveView`）；total < 150 ms |
| b | 作用中為 Key Plan 視窗 | 點選並排的平面視窗後，再從面板改高度 | log 寫「相機非作用中（作用中＝「Key Plan-…」）」，透視圖仍重繪 |
| c | 相機視圖未開啟 | 關閉透視圖後改高度 | log「相機視圖「…」未開啟，未重繪」 |
| d | 底圖不選自產視圖 | `list_cameras` 看 `BaseMap.PlanName`；③ 底圖 combo 開「全」 | 不出現任何「Key Plan-」開頭的視圖 |

### 編輯快速路徑＋後續更新分級（核心：改離樓板高度後透視圖要馬上變）　⏳ 已編譯、已部署 Revit 2024、待複測（2026-09-26 12:58，R24 SHA256 32D74031…190814EA）

| # | 項目 | 複測方式 | 預期 |
|---|---|---|---|
| a | MCP 快速路徑計時 | 透視圖開著（作用中）→ `set_camera_pose(cameraId=X, heightAboveLevelCm=原值+20)` 連做 3 次（每次不同值） | 回傳 `Timing.totalMs` < 150（記下 transactionMs／refreshMs）；`ViewRefresh=RefreshActiveView`；`After.HeightAboveLevelCm` 正確；log「⏱ 高度編輯 → 視圖更新 total=…ms」 |
| b | 非作用中但開著 | 一鍵 4 視圖並排後，作用中切到平面 → 同上 | `ViewRefresh=UpdateAllOpenViews`；記下 refreshMs（若明顯變大＝此路徑是瓶頸） |
| c | 後續更新只畫剖面 | a 之後等 1 秒看 log | 「⏱ 相機變動後續更新（去抖；1 台）」＋「→ 只重畫剖面標記」；**沒有**「一鍵 4 視圖（Key Plan 建立/更新）」；3 次連續編輯只合併成 1 次後續更新 |
| d | 面板改高度 | 清單「離樓板 cm」輸入新值按 Enter，重複 3 次（含改回原值） | 透視圖立刻變；log「⏱ 高度編輯 → 視圖更新 total=…ms [排隊, 交易, 重繪]」與「→ 清單列就地更新 …ms」；清單不整份重讀（無「live sync 讀取快照」）；改回原值也會套用 |
| e | 連改兩欄 | 改高度後直接點俯仰角格輸入（不按 Enter 離開） | 第二次編輯不會把高度改回去（舊版用清單列補齊的問題）；後續更新＝剖面標記＋A |
| f | 水平角／同房間移動／換房間 | 改水平角；地圖拖曳相機 1 m（同房間）；拖到另一個房間 | 分級分別為「平面＋剖面標記」「標記＋剖面重新對位」「完整 Key Plan 更新」 |

### Key Plan 平面樓層不符重建＋剖面位置「相機位置」　⏳ 已編譯、未部署（Revit 執行中）、待複測（2026-09-26 12:45，build R24 SHA256 41B39F18…EFA7B810）

| # | 項目 | 複測方式 | 預期 |
|---|---|---|---|
| a | 樓層不符重建 | 部署後 `list_cameras`「Green Roof - By Stair 1」→ 看 `KeyPlan.PlanLevelMismatch`（應為 true）→ `run_camera_keyplan` → 再 `list_cameras` | log「Key Plan 平面樓層不符（舊 X → 新 Green Roof Hardscape）→ 重建」＋舊平面已刪除（或在圖紙上＝改名「-舊樓層」、無 ES）；新平面 GenLevel＝相機樓層、`PlanLevelMismatch`＝false；平面上只見本相機剖面線、標記在新平面 |
| b | 剖面位置＝相機位置 | 預設設定 `run_camera_keyplan` 一台室內相機（room 與 sight 各一次） | `Decisions.SectionAnchor`＝Camera；`SectionSpecLog` 有「剖面位置＝相機位置：原點＝眼睛 … 深度[−R,0]」；平面上剖面線通過相機標記圓心；剖面裡看得到相機前方的扇形區域 |
| c | 視野中心＝舊行為 | `sectionAnchor="center"`；④ ▾ 切換兩個選項看勾號 | 與舊版相同原點／深度；切換設定寫入本機設定並保留；手動拖過的剖面仍保留，重設剖面位置依目前設定 |

### 使用者實測第四輪修正（D-039–D-043）　⏳ 已編譯、已部署 Revit 2024、待複測（2026-09-26）

> 使用者 Revit 2024 實測回報（Snowdon Towers Sample Architectural）後的修正。程式已編譯並部署到 Revit 2024（RevitMCP.dll SHA256 AE3B1488…0B24EF，建置＝部署）。以下每一項都要重新測；效能項目請看 RevitMCP log 的 `⏱` 行。

| # | 項目 | 複測方式 | 預期 |
|---|---|---|---|
| 1 | A 標記模式不再全量重畫 | ⋯ 相機標記＝A → 在清單改一台相機的水平角 → 看 log | 「⏱ 更新相機標記（要求 1 台）」只重畫 1 台；「A 標記（即時，只處理變動相機）」；總覽只動該相機；總時間明顯低於舊版（舊：每台約 3 s、19 台） |
| 1b | 點視圖不卡 | 專案瀏覽器連續點幾台相機的透視圖 | `⏱ 視圖啟動跟隨（Idling 端）`＋`⏱ 跟隨作用中透視圖（UI）` 合計 < 300 ms；同樓層不換底圖；換過的樓層第二次「⏱ 底圖快取命中」不匯出；沒有「更新相機標記」交易 |
| 1c | 快照快取 | 按一次重新整理後再做任何清單編輯 | `⏱ 讀相機快照 … 房間/樓層快取 命中 20、重算 1` 類似比例 |
| 2 | 多層房間樓層 | `list_cameras` 看「Walkthrough Lobby Stair」「Spiral Stair Loft」 | Level＝L3／上層（LevelSource=roomSpan），StoreyLog 列出候選；剖面樓層記號與地圖跟隨也用該層 |
| 3 | 屋頂花園相機 | `list_cameras`「Camera - Fifth Floor Roof Garden」→ `run_camera_keyplan` | 室外；StandingSurface.ZMm≈屋面；Level＝屋面那層；Key Plan 平面剖切面＝屋面 +1200、底／深度＝−1000（log「室外平面…視圖範圍」）；平面看得到屋面與女兒牆 |
| 4 | Navigation Wheel | 開一台已有 Key Plan 的相機透視圖，用 Full Navigation Wheel 走動 > 0.3 m 後停 1 秒 | log「導覽監看…排入相機更新」；標記／扇形跟上；Key Plan 範圍重算（「相對…移動 … → 重算 Key Plan 範圍」）；沒有版本的相機多出「A案」＝移動前視角 |
| 5 | 清單／MCP 編輯後透視圖與 Key Plan 跟著動 | 透視圖開著時改清單水平角 10°；再用 `set_camera_pose` 移 1 m | 透視圖立即更新；兩次都重算 Key Plan；`set_camera_pose` 回傳 `ViewRefresh`、`CameraUpdate` |
| 6 | 僅打開透視圖 | 右鍵 → 僅打開透視圖 | 只剩該透視圖一張開著 |
| 7 | 重複點同一台相機 | 同一台相機雙擊名稱兩次 | 第二次 log「這 4 張視圖已開啟並排 → 不關閉／不重新並排」，視窗不閃 |
| 8 | 面板寬度 | 連續一鍵 4 視圖不同相機 5 次，觀察面板寬度 | 不再每次變窄（至少重複同一台時不變）；若仍變窄，回報是否停在 300 px（MinimumWidth 是否生效未驗證） |
| 9 | 一鍵出圖紙 | ④ ▾ 一鍵出圖紙 | 完成後只剩該圖紙開著且為作用中 |
| 10 | 視角版本 | 對沒有版本的相機改一次清單數字 → 右鍵視角版本 | 出現「A案」＝改之前的視角；儲存建議「B案」；清單新到舊，目前符合者「✓ 目前」；改動後還原 A案 → 多一筆「自動備份 HH:mm」；`camera_version` list 有 `CurrentMatchesVersion` |
| 11 | 目前檢視中的相機 | 點不同相機的透視圖分頁 | 清單該列粗體＋左側藍條；切到平面視圖時標示消失 |
| 12 | 橘色列說明 | 滑過橘色列；開 ① ⋯ | ToolTip 寫「橘色列：離樓板高度不在 120–200 cm」；⋯ 選單有圖例 |
| 13 | 等角近側 | 預設設定 `run_camera_keyplan` 一台室內相機；再用 `isoNearWalls="wall"`、`"keep"` | 預設 log「mode=Camera … MinX→眼睛X＋…」，近側兩面在眼睛座標；wall＝房間邊界內 100 mm；keep＝不切 |
| 14 | 剖面樓層記號 | 室內相機的剖面；室外相機的剖面 | 室內：下一層＋本層＋上一層（裁剪框外的會被裁掉）；室外：剖面高度內所有建築樓層 |
| A | 單擊／雙擊 | 清單單擊名稱、雙擊名稱；地圖單擊、雙擊標記、雙擊空白 | 單擊只選取（不跑 4 視圖）；雙擊跑一鍵 4 視圖；雙擊空白＝符合畫面 |
| B | 地圖拖曳 | 選取一台相機 → 拖圓點 2 m 放開；Shift＋拖或拖把手旋轉；拖到一半按 Esc | 拖曳中 log 沒有任何 Revit 交易；放開只有一次「編輯相機」；離樓板高度不變；同樓層不重新匯出底圖；Esc 還原 |

### 等角近側牆切除（D-038）＋`set_section_box`　⏳ 已編譯、待 MCP 複測（2026-09-26；近側預設已由 D-039 改為「相機位置」，下表 a 請改用 `isoNearWalls="wall"` 測）

| # | 情境 | 複測方式 | 預期 |
|---|---|---|---|
| a | 室內相機等角近側牆 | 對一台室內相機 `run_camera_keyplan`（預設設定）→ 讀 `Decisions.IsoNearWallsLog` 與 `Iso.SectionBox` | log 含「切 MinX／MaxX＋MinY／MaxY」；近側兩面＝房間邊界往內 100 mm、遠側＝房間 +0.6 m、Z 與前次相同；截圖看得到室內、近側兩道牆消失 |
| b | 保留 | `run_camera_keyplan(isoNearWalls="keep")` | 四面皆房間 +0.6 m，log「mode=Keep」 |
| c | 室外相機 | 室外相機 `run_camera_keyplan` | 等角範圍與前一版相同，log「不切」 |
| d | `set_section_box` | 絕對模式設一個框、再用 `offsetsMm` 縮 minX −500 → 讀回 | `After_mm` 與要求一致；正交視圖 `CropBoxActive=false`；min ≥ max 時報錯且不修改 |

### MCP 自測第四輪修正　✅ 已由 MCP 複測（2026-09-26；a 通過，b 本輪未觸發，c 待重新截圖）

第三輪實測：移動路徑方向／原點正確但深度偏差 2374 mm → 退到刪除重建 → `InvalidObjectException` 整筆回滾。第四輪 3 項已改、已編譯，並於 2026-09-26 由主線以 MCP 複測。

| # | 情境 | 複測方式 | 複測結果 |
|---|---|---|---|
| a | 相機移 1 m＋轉 180°（移動路徑） | 用 `set_camera_pose` 把相機水平移 1 m、yaw 轉 180° → `run_camera_keyplan`：`Decisions.SectionApplyLog` 含「已旋轉／已移動／已沿深度移動」、**不含「移動路徑失敗」「刪除重建」**；`SectionExtentDeviationMm` ≤ 5（深度偏差 0）；剖面 Id 不變 | ✅ 通過：旋轉 180°＋水平移動 5.2 m＋深度移動 1.5 m，世界範圍偏差 0 mm；另以全新相機建立剖面，偏差亦為 0 mm |
| b | 刪除重建路徑（不得加除錯旗標） | 觸發方式：讓移動路徑失敗且剖面不在圖紙上——在 Key Plan 平面選取該剖面標記按「釘選（Pin）」（被釘選的元素 Rotate/MoveElement 會失敗；此行為未實測，若沒失敗請記下 log 回報），再移動／旋轉相機並 `run_camera_keyplan`（**不要**用刪除剖面標記來觸發——那會把剖面視圖一起刪掉）：`SectionApplyLog` 含「已刪除舊剖面 … 並重建為 …」、**沒有 `InvalidObjectException`、沒有回滾**；`Result.SectionViewId`＝新 Id；新剖面有 ES 連結（再跑一次會走移動路徑而非新建第二張）、B 標記畫在新剖面上 | ⏳ 本輪移動路徑全部成功，未觸發此路徑，仍待專門測試（釘選觸發是否真的失敗也未實測） |
| c | 圖框線讀不到的圖紙 | `create_camera_sheet`（E1 30 x 42）：`DrawAreaSource` 含「未偵測到圖框線 → 四邊內縮改為 … ≈43 mm」；截圖透視圖不壓左側內框 | ⏳ margin fix（4 % 寬度退路）已部署，邏輯已生效，但本輪未針對「E1 30 x 42 Horizontal_Snowdon」重新截圖確認實際版面 |

### MCP 自測第三輪修正　✅ 已由 MCP 複測（2026-09-26；第 5 項見複測結果的診斷字串備註）

第二輪已由主線 MCP 驗證通過：最近房間 fallback、樣板層隱藏子類別、B 模式刪 A、不再誤判「手動移過」、圖紙不 Overflow、標題欄 20 % 退路、透視複本無標記、總覽裁剪讀回＝預期。第三輪 5 項已改、已編譯，並於 2026-09-26 由主線以 MCP 複測。

| # | 問題 | 修正 | 複測方式 | 複測結果 |
|---|---|---|---|---|
| 1 | （回歸）更新既有剖面：CropBox 保留舊 Transform、只換範圍 → 內容跑到樓梯核／下層 | 旋轉／移動 viewer → 讀回座標系設範圍 → 後檢 5 mm；做不到且不在圖紙上才刪除重建（D-037） | 對「Unit 304 - Toward Core」連跑兩次 `run_camera_keyplan`（第二次換 `sectionMode`）：`Decisions.SectionApplyLog` 含「已旋轉／已移動」或「新建」、`SectionExtentDeviationMm` ≤ 5；`Section.Extents.CropBox` 的 Transform 原點＝新原點；`capture_view_image` 看不到鄰近樓梯核與下層 | ✅ 通過（見第四輪 a：偏差 0 mm） |
| 2 | 圖框幾何 0 條線 | 三組 Options、SymbolGeometry 退路、實體邊、類型統計 | `create_camera_sheet`：`DrawAreaSource` 的「圖框幾何線 N 條」N > 0，且列出採用的 Options 與幾何類型；N 仍為 0 時記下類型統計回報 | ⏳ 邏輯已生效，E1 30x42 圖框仍待重新截圖確認（見第四輪 c） |
| 3 | 圖紙約 40 % 空白、Key Plan 被迫 1:75 | 透視格＝實際視埠寬；右側自動版面（D-035） | `create_camera_sheet`：`DrawAreaSource` 含「透視格寬」與「右側版面＝…（試過 …）」；Key Plan 比例比 1:75 細或版面空白明顯減少；`Overflow=false`；截圖檢查 | ✅ 通過：`Overflow=false`，視埠類型挑到「Title w Line」，版面「上二下一」（平面 1:50／剖面 1:25／等角 1:75） |
| 4 | 總覽裁剪切到「Unit 302 - Toward Interior」扇形 | 共用半徑函式＋讀回標記外接框外擴（D-036） | `run_camera_overview(L3)`：`ExtentsMode` 列每台半徑與「標記讀回外接框…皆在裁剪內」或「已外擴」；截圖扇形完整 | ✅ 通過：4 台相機皆有名稱標示，裁剪自動外擴以含所有標記 |
| 5 | A 扇形子類別讀回「讀不到扇形幾何」 | Regenerate 後讀、Fine＋NonVisible、展開實例 | `run_camera_keyplan(markerMode="A")`：`AMarkerLog`「不符 0」、沒有「讀不到扇形幾何」 | ⚠️ 部分：視覺／透視圖複測（含套用視圖樣板「Cover」「Architectural Presentation 3D (No Shadows)」）0 失敗、扇形確實隱藏；但 `AMarkerLog` 診斷字串本身仍讀到「讀不到扇形幾何」，字串來源待查（不影響實際隱藏效果） |

使用者實測加一項：圖紙上的透視圖大小在 Revit 內以「裁剪區域大小 → 縮放（鎖定比例）」手動調整後，重跑一鍵出圖紙時右側 Key Plan 會跟著重排（見下方使用者實測 #7）。

### MCP 自測第二輪修正　✅ 已由 MCP 複測（2026-09-25 22:50）

第一輪已由主線 MCP 驗證通過：Scope Box 解除、房間 Level 當樓層、圖紙透視複本、視埠類型挑選。第二輪 6 項全部已改、已編譯；**新 DLL 尚未部署**（Revit 執行中）。

| # | 問題 | 修正 | 複測方式 |
|---|---|---|---|
| 1 | 相機在未放房間的區域（Loft Unit 旁），`Room.Log`＝`none (bboxHits=0, xyHits=0)` | 最近房間 fallback：水平 ≤ 1.5 m、垂直含眼睛或 ≤ 2 m（spec §10.2 步驟 3a） | `list_cameras`：該相機 `IndoorOutdoor`＝室內、`Room.Log` 含「最近房間 d=」且 d ≤ 1500 mm |
| 2 | 16 張透視圖因樣板控制 V/G 無法隱藏「相機標記」 | 改在樣板隱藏子類別（D-033） | `run_camera_keyplan(markerMode="A")` 的 `AMarkerLog` 含「改在視圖樣板隱藏：「…」」、失敗 0；`capture_view_image`（該透視圖與 `…-Key Plan 圖紙`）看不到相機體與扇形 |
| 3 | 圖紙透視複本看得到扇形 | GraphicsStyle 取得＋讀回驗證 | `AMarkerLog`「子類別樣式 gs=<正數>，不符 0」；複本截圖無紅色扇形 |
| 4 | `markerMode=B` 留著 A 實例／扇形 | 一鍵 4 視圖刪除本相機 A 標記 | 先 `markerMode="A"` 再 `markerMode="B"`：`AMarkerLog`「已刪除本相機 A 實例／扇形 2 個」、三張視圖 `TaggedElements` 無 `MarkerA`／`MarkerAFan` |
| 5 | 第二次跑同一相機 `SectionPreserved=true` | ES 存讀回值、容許 5 mm／0.5° | 同一相機連跑兩次 `run_camera_keyplan`：都 `SectionPreserved=false`；手動移剖面後再跑＝true 且 log 有兩組值 |
| 6 | 圖紙壓標題欄、透視圖太小、平面超出 | 扣標題欄、55 % 版面、列高依內容、Key Plan 自動比例、透視複本等比放大（D-034） | `create_camera_sheet`（G002 那台）：`DrawAreaSource` 有標題欄判定、右緣在標題欄線左側；透視視埠約佔格子 90 %、`Note` 寫「FOV 不變」；平面 `Note` 有比例變更；四個視埠 `Overflow=false`；截圖檢查 |

### MCP 自測第一輪修正　✅ 已由 MCP 複測（2026-09-25 22:20）

主線以 MCP 在 Revit 2024（公開範例 Snowdon Towers）自測後回報 6 項，全部已改、已編譯；**新 DLL 尚未部署**（Revit 執行中），以下皆「待 MCP 複測」。

| # | 問題 | 修正 | 複測方式 |
|---|---|---|---|
| 1 | 複製來的 Key Plan 平面／樓層總覽鎖在 Scope Box，裁剪停在 78.6×64.0 m、比例被迫 1:200、圖紙 Overflow；圖紙標題沿用來源 | 每次先解除 Scope Box、專案北、轉正、圖紙標題改自己的，再設裁剪；讀回比對 > 1 % 記 WARNING；總覽回報讀回值（spec §10.4、§10.10） | `run_camera_keyplan` → `get_view_extents(平面)`：`ScopeBox=null`、`TitleOnSheet`＝「Key Plan 平面」、裁剪≈房間 +0.6 m；`Warnings` 為空；`run_camera_overview`：`ExtentsMm`≈`IntendedMm` |
| 2 | 室內相機（閣樓、廚房眼睛低於房間底、大廳）被判室外 | 房間搜尋改輪廓＋垂直範圍（≤ 2 m 容差）（D-031） | `list_cameras`：三台 `IndoorOutdoor`＝室內、`Room.Log` 的 `foundBy` |
| 3 | 室內相機樓層被判成女兒牆 | 室內樓層＝房間的 Level（D-031） | `list_cameras`：`LevelSource=room`、`Level`＝房間樓層；`HeightAboveLevelCm` 以該樓層計算 |
| 4 | 剖面 Key Plan 出現其他剖面標頭（「-/---」） | 平面／剖面／總覽逐元素隱藏其他 viewer，隱藏立面／詳圖索引類別 | `get_view_extents(剖面)`：`VisibleViewers` 全 0；平面：`Sections`＝1（本相機） |
| 5 | 一鍵出圖紙略過已在 3D 圖紙上的透視圖；`ViewportTypeLog` 自相矛盾 | 出圖用透視複本並同步（D-032）；視埠類型候選改聯集、列「實際使用」 | `create_camera_sheet`：`Perspective.Placed`＝透視複本（新建）、4 個視埠「新放置」；重跑＝沿用並同步；`list_cameras` 不含複本 |
| 6 | 測試工具不足 | `run_camera_keyplan` 加 `markerMode`；`get_view_extents` 加 Scope Box／方位／圖紙標題／可見 viewer 數（工具數仍 220） | `run_camera_keyplan(markerMode="A")`：`MarkerModeUsed=A`、`AMarker` 非 null |

### 下一步｜MCP 自測　⏳（Revit 重新啟動、面板按過「重新整理」、MCP 已連線後，由主線依序執行）

前置：Revit 2024 開一個有透視相機、有圖框類型的測試模型（公開範例或「實測專案」的複本，**不**用正式檔）；確認 MCP 工具清單含本功能 8 支工具。每一步的「預期」不符就停下來記錄，不往下跑。

| # | 呼叫 | 預期結果 |
|---|---|---|
| 1 | `list_cameras` | `Count` ≥ 1；每台有 `Level`、`HeightAboveLevelCm`、`Fov.Source`（記下 C／A／B）、`IndoorOutdoor`；`BaseMap.Step` 0–4 之一。選一台室內相機 X、一台室外相機 Y（若有）供後續使用 |
| 2 | `run_camera_keyplan(cameraId=X)` | `Success=true`；`Plan`／`Section`／`Iso` 都有 id；`Decisions.IndoorOutdoor`＝室內、`Room.Id` 非 null；平面 `TaggedElements` 含 MarkerPlan；`OverviewViewId` 非 null |
| 3 | `get_view_extents(viewId=步驟 2 的平面)` | `CropBox.Active=true`；裁剪框約＝房間外接框 ±600 mm（與步驟 2 的 `Room.Log` 對照）；`HiddenCategories` 含軸線／尺寸；`ViewTemplate=null` |
| 4 | `get_view_extents(viewId=步驟 2 的剖面)` | 裁剪框垂直範圍包含平面剖切面高程；`ViewOwnedElements.DetailLines` > 0（標記＋樓層記號） |
| 5 | `run_camera_keyplan(cameraId=X, sectionMode="sight")` 再 `sectionMode="room"` | 兩次 `Decisions.SectionModeUsed` 分別為 AlongSightLine／AlongRoomLongAxis；`SectionPreserved=false` |
| 6 | `run_camera_keyplan(cameraId=X, isoMode="current")` 再 `"full"` | `IsoExtentMode` 對應；current 的 `Iso.Extents.SectionBox` 上緣 ≤ full 的上緣 |
| 7 | `set_camera_pose(cameraId=X, heightAboveLevelCm=150, yawDeg=原值+30)` | `After.HeightAboveLevelCm=150`、`After.YawDeg`＝原值+30（±0.1）；XY 不變 |
| 8 | `list_cameras(nameContains=X 名稱)` | 同步驟 7 的值（面板服務層讀回一致） |
| 9 | `camera_version(cameraId=X, action="save", name="MCP 測試 A")` | `Versions` 含「MCP 測試 A」，`YawDeg` ＝步驟 7 之後的值 |
| 10 | `set_camera_pose(cameraId=X, yawDeg=原值)` → `camera_version(action="restore", name="MCP 測試 A")` | restore 後 `Camera.Current.YawDeg` ＝步驟 9 存的值（±0.1）、`EyeMm` 相同（±1 mm）；`Warnings` 為空（有值＝記下裁剪框／遠裁剪還原問題） |
| 11 | `camera_version(cameraId=X, action="save", name="MCP 測試 A")`（同名） | `Message` 為「已覆蓋」；版本數不增加 |
| 12 | `create_camera_sheet(cameraId=X)` | `Sheet.Created=true`、`Name`＝「Key Plan-<X 名>」；`TitleBlock.Source` 為「最常用…」或「面板設定」；4 個 `Viewports` 都是「新放置」；每個 `CenterMm` 落在自己的 `CellMm` 內；記下 `Overflow` 與 `ViewportTypeLog` |
| 13 | `create_camera_sheet(cameraId=X)`（重跑） | 同一個 `Sheet.Id`、`Created=false`；4 個都是「保留原位」；不新增圖紙 |
| 14 | `get_all_sheets` | 只多一張「Key Plan-<X 名>」 |
| 15 | 新相機 Z（`copy_camera_to_level(cameraId=X, levelName=另一層)`）→ `create_camera_sheet(cameraId=Z)` | `RanFourViews=true`（自動先建 Key Plan）；新圖紙 4 個視埠「新放置」 |
| 16 | `create_camera_sheet(cameraId=X, titleBlockTypeId=另一個圖框類型)` | `TitleBlock.Source`＝指定；`Warnings` 含「已把既有圖紙的圖框換成…」 |
| 17 | 室外相機 Y：`run_camera_keyplan(cameraId=Y)` | `IndoorOutdoor`＝室外、`SectionModeUsed`＝AlongSightLine、`StoreyTop.Outdoor=true` |
| 18 | `run_camera_overview(all=true)` | 每個有相機的樓層一筆、`Error=null`；`TaggedElements` 的 MarkerName 數＝該層相機數 |
| 19 | `camera_version(cameraId=X, action="delete", name="MCP 測試 A")` → `list` | 版本清單不含「MCP 測試 A」；相機姿態不變 |
| 20 | 收尾 | 用 Revit 復原或關檔不存，測試模型不保留本輪變更 |

### 最後｜使用者實測清單（只有人能判斷的項目）　⏳

> MCP 自測已完成；以下只剩人工項目。

1. 面板手感：清單單擊選取、雙擊名稱開 4 視圖（2026-09-26 起）、點數字直接改（Enter／Esc）、樓層下拉複製、右鍵選單，操作不會互相搶。
2. ③ 位置圖：滾輪縮放、拖曳平移、單擊／雙擊相機圓點、拖曳已選取相機（移動／旋轉），手勢不衝突；標記對位目視無偏移（開「框」檢查）；拖曳放下的位置與 Revit 內相機位置一致。
3. 300 px 寬：① 工具列、④ 動作列、② 下方提示都不被截斷、不出現水平捲軸；8 px 間距看起來整齊。
4. ⑥ 狀態列：做完一個動作後，結果訊息會留著（不被自動刷新蓋掉）；時間戳看得懂。
5. ToolTip：滑過每個按鈕／選單項目，說明與實際行為一致（特別是 Key Plan 樣式、剖面方向、等角範圍）。
6. Key Plan 畫面：淡化程度、外牆輪廓清楚度、B 標記大小與透明度、簡化樓層記號好不好讀。
7. **一鍵出圖紙**：用自己常用的圖框出一張——透視圖大小是否合適、右欄三張是否壓到圖框標題欄、視埠標題是否顯示、整體是否可以直接列印。**透視圖大小程式改不動**（放大裁剪框會改變 FOV）：請選透視視埠 → 裁剪區域大小（Size Crop）→「縮放（鎖定比例）」手動放大，再重跑一鍵出圖紙讓右側 Key Plan 重排，確認版面合理。
8. 圖紙圖框下拉：換一個圖框再出圖，既有相機圖紙的圖框是否正確換掉、視埠位置是否仍合理。
9. **視角版本**：在透視圖裡轉一個角度（第一次修改前會自動存「A案」）→ 右鍵存成「B案」→ 再轉 → 切回 A案：畫面（含裁剪框範圍）是否完全回到原樣、是否多了「自動備份 HH:mm」；Key Plan 標記有沒有跟著動。
10. 視角版本存檔後關閉再開啟（或請同事開同一檔）：版本還在、可以切換。
11. A 型標記（⋯ 相機標記 → A）：族群能否建立、平面／3D 看得到、透視圖看不到、拖 A 標記相機跟著動。
12. **Enscape**：A 型標記（相機體＋扇形）是否出現在 Enscape 畫面（程式只在透視圖隱藏子類別）。
13. 並排視窗在「使用索引標籤檢視」開／關兩種設定下的實際表現。
14. 長時間使用是否再崩潰（開檔時面板開著／關著兩種情況）；若崩潰，把 `%APPDATA%\RevitMCP\Logs\CameraManagerCrash_*.log` 交給主線。

## MCP 回歸測試腳本（2026-09-26 全面稽核後；主線以 MCP 實跑）

前置：Revit 2024 開 Snowdon Towers Sample Architectural（公開範例），部署本輪 DLL 後重啟 Revit、面板按一次「重新整理」、MCP 已連線。**全部跑完用 Revit 復原或關檔不存**。任一「預期」不符就停下記錄 log（`%APPDATA%\RevitMCP\Logs`）與回傳 JSON，不往下跑。

六類相機（Id 取自前幾輪 `list_cameras`，若不符以名稱查）：

| 代號 | 類型 | 相機 | Id |
|---|---|---|---|
| IN | 室內房間 | Camera - Unit 304 - Toward Core | 2076996 |
| OUT | 室外街景 | Camera - Exterior Street View (Real) | 705685 |
| ROOF1 | 屋頂花園 | Camera - Fifth Floor Roof Garden | 1991000 |
| ROOF2 | 屋頂（樓梯旁） | Green Roof - By Stair 1 | 1413473 |
| STAIR | 樓梯／挑空大廳 | Walkthrough Lobby Stair | 2304727 |
| LOFT | 樓中樓 | Loft Unit | 851667 |
| DBL | 挑高（旋轉梯） | Spiral Stair Loft | 851735 |

### R-1｜讀取與分類（唯讀）

| # | 呼叫 | 預期／斷言 |
|---|---|---|
| 1 | `list_cameras` | `Count` ≥ 7 個目標皆在；log 無例外 |
| 2 | `list_cameras(nameContains="Unit 304")` | IN：`IndoorOutdoor`＝室內、`Room.Id` 非 null、`LevelSource`＝room 或 roomSpan |
| 3 | `list_cameras(nameContains="Exterior Street")` | OUT：室外；`StandingSurface.ZMm` 有值（地面／基地） |
| 4 | `list_cameras(nameContains="Roof Garden")`、`(nameContains="By Stair 1")` | ROOF1／ROOF2：室外；`Level`＝屋面那層（站立面規則）；`KeyPlan.PlanLevelMismatch`＝false（跑過 R-4 後） |
| 5 | `list_cameras(nameContains="Lobby Stair")`、`(nameContains="Spiral Stair")`、`(nameContains="Loft Unit")` | STAIR／DBL：`LevelSource`＝roomSpan、`Level`＝上層（L3／樓中樓上層）；LOFT：室內（最近房間 fallback 時 `Room.Log` 含「最近房間 d=」且 d ≤ 1500） |
| 6 | 同一呼叫連做兩次 `list_cameras` | 第二次 log「⏱ 讀相機快照 … 房間/樓層快取 命中 N」N 接近相機數 |

### R-2｜姿態編輯與數值檢查（IN）

| # | 呼叫 | 預期／斷言 |
|---|---|---|
| 1 | `set_camera_pose(cameraId=2076996, pitchDeg=90)` | **回錯誤**「俯仰角必須介於 −89.5° 與 +89.5° 之間」；相機不變（`list_cameras` 讀回與前一致） |
| 2 | `set_camera_pose(cameraId=2076996, farClipCm=0)` | 回錯誤「遠裁剪距離必須大於 0 cm」（若該相機遠裁剪唯讀則回唯讀錯誤，亦可） |
| 3 | `set_camera_pose(cameraId=2076996, heightAboveLevelCm=<原值+20>)` | `After.HeightAboveLevelCm`＝原值+20（±0.1）；`Timing.totalMs` 記下；1 秒後 log「→ 剖面標記＋A」（本輪改：高度也移 A 標記），**沒有**「導覽監看…排入相機更新」 |
| 4 | `camera_version(cameraId=2076996, action="save", name="回歸 A")` | `Versions` 含「回歸 A」 |
| 5 | `set_camera_pose(cameraId=2076996, yawDeg=<原值+30>)` → `camera_version(action="restore", name="回歸 A")` | `Camera.Current.YawDeg`＝存檔值（±0.1）；`Warnings` 若有「裁剪啟用狀態還原失敗」須同時**沒有**「裁剪框還原失敗」（本輪分開處理） |
| 6 | `camera_version(cameraId=2076996, action="delete", name="回歸 A")` | 清單不含「回歸 A」 |

### R-3｜標記後續更新（IN，A 標記）

| # | 呼叫 | 預期／斷言 |
|---|---|---|
| 1 | `run_camera_keyplan(cameraId=2076996, markerMode="A")` | `Success`；`AMarkerLog`「不符 0」且**沒有**「讀不到扇形幾何」（本輪修正診斷） |
| 2 | `set_camera_pose(cameraId=2076996, heightAboveLevelCm=<原值>)`（改回） | 1 秒後 log 分級「剖面標記＋A」；`get_view_extents(Key Plan 剖面)` 的 `ViewOwnedElements.DetailLines` > 0 |
| 3 | 同一值再 `set_camera_pose` 一次 | 回傳 `Changed=false` 類訊息「數值與相機目前相同」，不排後續更新 |

### R-4｜Key Plan（六類各一次）

對 IN、OUT、ROOF1、ROOF2、STAIR、LOFT、DBL 依序 `run_camera_keyplan(cameraId=…)`：

| 斷言 | 適用 |
|---|---|
| `Success=true`；`Plan`／`Section`／`Iso` 三個 Id 非 null；`Warnings` 無「未提交」「InvalidObject」 | 全部 |
| `Decisions.IndoorOutdoor`＝室內、`Room.Id` 非 null | IN、STAIR、LOFT、DBL |
| `Decisions.IndoorOutdoor`＝室外、`SectionModeUsed`＝AlongSightLine | OUT、ROOF1、ROOF2 |
| log「室外平面…視圖範圍」剖切＝站立面 +1200、底／深度＝−1000；`get_view_extents(平面)` 讀回一致 | ROOF1、ROOF2 |
| `get_view_extents(平面)`：`ScopeBox=null`、`CropBox.Active=true`、`VisibleViewers` 的剖面只有本相機 1 條 | 全部 |
| 平面 GenLevel＝`list_cameras` 的 `Level`（`KeyPlan.PlanLevelMismatch=false`） | 全部，特別是 ROOF2、STAIR、DBL |
| `Decisions.SectionApplyLog` 不含「移動路徑失敗」；`SectionExtentDeviationMm` ≤ 5 | 第二次跑同一台（先 `set_camera_pose` 移 `planXcm` +100） |

### R-5｜剖面移動路徑（STAIR）

| # | 呼叫 | 預期／斷言 |
|---|---|---|
| 1 | `set_camera_pose(cameraId=2304727, planXcm=<原值+100>, yawDeg=<原值+180>)` → `run_camera_keyplan(cameraId=2304727)` | `SectionApplyLog` 含「已旋轉」「已移動」；剖面 Id 與前次相同；偏差 ≤ 5 mm |
| 2 | 改回原值再跑一次 | 同上；`SectionPreserved=false` |

### R-6｜樓層總覽與 A 標記

| # | 呼叫 | 預期／斷言 |
|---|---|---|
| 1 | `run_camera_overview(all=true)` | 每層一筆、`Error=null`；每層 `TaggedElements` 的 MarkerName 數＝該層相機數 |
| 2 | `run_camera_keyplan(cameraId=851735, markerMode="AB")` | `AMarkerLog`「不符 0」、「失敗 0」；透視圖隱藏新增數或「改在視圖樣板隱藏」皆可 |
| 3 | `run_camera_keyplan(cameraId=851735, markerMode="B")` | `AMarkerLog`「已刪除本相機 A 實例／扇形 2 個」 |

### R-7｜圖紙（LOFT、OUT）

| # | 呼叫 | 預期／斷言 |
|---|---|---|
| 1 | `create_camera_sheet(cameraId=851667)` | `Sheet.Name`＝「Key Plan-Loft Unit」；4 個視埠；`Overflow=false`；`Error=null` |
| 2 | 重跑同一呼叫 | 同一 `Sheet.Id`、`Created=false`；4 個「保留原位」 |
| 3 | `delete_element(<LOFT 的 Key Plan 等角視圖 Id>)` → `create_camera_sheet(cameraId=851667)` | `RanFourViews=true`（自動補建）；新等角視埠「新放置」；`Error=null`（本輪加：補建後讀不回視圖會明確報錯而非丟例外） |
| 4 | `create_camera_sheet(cameraId=705685)` | 室外相機圖紙成功、`Error=null` |

### R-8｜文件生命週期（快取）

| # | 動作 | 預期／斷言 |
|---|---|---|
| 1 | `save_document` → `close_document` → `open_document(同一檔)` → `list_cameras` → `run_camera_keyplan(cameraId=2076996)` | log 有「文件關閉 → 清除房間／樓層、姿態、總覽快取」「文件開啟 → …」；`run_camera_keyplan` `Success=true`、無 `InvalidObjectException`（本輪修正的崩潰路徑）。⚠️ 會存檔：用範例檔的複本做 |

### R-9｜雜項

| # | 呼叫 | 預期／斷言 |
|---|---|---|
| 1 | `set_section_box(viewId=null, offsetsMm={minX:-100})`（作用中為 3D 等角） | 正常套用或清楚錯誤「目前作用中的視圖不是非樣板 3D 視圖」；不得是 NullReference |
| 2 | `get_link_display_settings()` | Revit 2024：正常回傳（行為不變） |

**只能人工驗**：兩份文件同時開（佇列取消訊息「作用中文件已切換，已取消上一個動作」）、另存新檔後面板仍連動、Undo／Redo 後標記不自動開交易（Redo 仍可用）、鎖定相機拖 A 標記、面板 ⋯ 匯出 Excel（含室外相機）、清單改名輸入含 `:` 的名稱被拒、⑤ 人視高輸入「162,5」。

## MCP 自測已驗證（2026-09-26）

主線在 Revit 2024（公開範例 Snowdon Towers Sample Architectural）以 MCP 實際呼叫，對四輪修正（commit `afeddfb`、`c541423`、`8cc721d`、`325e215`）逐項複測，以下項目已確認可用，原列在下方「未驗證清單」，現移出：

| 項目 | 驗證證據 |
|---|---|
| 8 個 MCP 工具皆回傳正確資料 | `list_cameras`（20 台相機）、`run_camera_keyplan`、`set_camera_pose`、`copy_camera_to_level`、`get_view_extents`、`run_camera_overview`、`create_camera_sheet`、`camera_version`（save／restore）皆正常；restore 還原出與存檔時完全相同的姿態（162.9 cm／200.67°） |
| FOV 候選 C（裁剪形狀）正確 | 新放相機讀到 h=50.2°；拖曳裁剪框後左右不對稱邊界（L −44°／R +25°）正確反映在讀回值 |
| 拖曳透視裁剪框會觸發標記更新 | log 顯示「更新相機標記」，角度隨拖曳變化 |
| 複製出的 Key Plan／樓層總覽平面已解除 Scope Box | 裁剪讀回 11.5×12.2 m @ 1:50；樓層總覽讀回值＝預期值 |
| 室內判定含最近房間 fallback；室內樓層＝房間所在樓層 | Loft Unit 旁的相機以最近房間 fallback（d=36 mm）判定為室內；storey 正確取自 room.Level |
| 剖面更新走移動路徑（旋轉＋平移＋深度校正） | 旋轉 180°＋水平移動 5.2 m＋深度移動 1.5 m，世界範圍偏差 0 mm；另以全新相機建立剖面，偏差亦為 0 mm |
| Key Plan 平面／剖面只顯示本相機的剖面線，其他 viewer 已隱藏 | 逐元素隱藏其他剖面／立面／詳圖索引標記，複測通過 |
| 相機標記子類別在透視圖（含套用視圖樣板）全部隱藏 | 「Cover」「Architectural Presentation 3D (No Shadows)」等樣板複測 0 失敗；圖紙上的透視複本亦看不到標記 |
| markerMode=B 會移除 A 標記；A 型實例可在實機建立 | 族群於 Revit 2024 用 `Metric Generic Model.rft` 成功建立／載入 |
| 不再出現「剖面被誤判手動移過」的假警報 | 複測未觸發 |
| 圖紙排版：出圖用透視複本、不 Overflow、視埠類型挑到「Title w Line」、版面「上二下一」 | `create_camera_sheet` 複測：相機在別張圖紙時正確放透視複本；四個視埠 `Overflow=false`；平面 1:50／剖面 1:25／等角 1:75 |
| 淡化 Key Plan 樣式正確渲染 | 截圖確認半色調背景、外牆加粗、扇形 90% 填滿、眼睛楔形皆正確 |
| 等角視圖為真標準等角、範圍只含房間、樓板可見 | 截圖確認 |
| 簡化樓層記號（L3/L4，透明背景）正確渲染 | 截圖確認 |
| 樓層總覽：4 台相機皆有名稱標示；裁剪自動外擴以含所有標記 | `run_camera_overview` 複測確認 |

未列在上表、原「未驗證清單」中仍待確認的項目（含本輪複測發現的新細節，如 A 扇形 `AMarkerLog` 診斷字串、圖框幾何 margin fallback 尚未重新截圖等），見下方各表與備註。

## 未驗證清單（2026-09-25，Wave 3 後）

以下行為已寫成程式並通過編譯，但**沒有在 Revit 內實際確認過**：

| 項目 | 備註 |
|---|---|
| 透視圖內滑鼠導覽是否觸發 DocumentChanged | 保留「從透視圖同步」按鈕；導覽監看改為面板隱藏時停止、本工具自己的編輯不再誤判為導覽（2026-09-26 稽核修正，待 MCP 複測） |
| `ExportImage` 在 TransactionGroup 開著時是否可用 | 有備援路徑 |
| 底圖對位是否完全無偏移 | 只驗證長寬比；以「框」模式目視 |
| 並排（TileViews）在索引標籤檢視模式下 | Revit 設定限制，可能只切分頁 |
| `ZoomToFit` 會不會移動透視相機 | 採防護式還原 |
| `FilledRegion` 表面透明度的元素取代 | 依社群已解答帖推斷 |
| 剖面比例門檻參數值的語意（5000 → … → 200） | 依名稱解析參數 |
| Display Model 數值 0／1／2 對應 | 以 `AsValueString` 記 log |
| 房間 BoundingBox 頂是否反映上限＋上限偏移 | log 另記 UnboundedHeight 供核對 |
| 平面剖面線看不到的真正原因（比例門檻／類別／隱藏／半色調） | 逐項記 log，待實機 |
| 連結模型「依主體視圖」後可見樓層計數是否反映實際顯示 | Revit 2024+ API |
| 簡化樓層記號在 Level 改高程／改名後自動重畫 | 去抖交易「更新 Key Plan 樓層線」；本工具 Key Plan／標記顏色交易不再誤觸發（2026-09-26 稽核修正，待 MCP 複測）；樓層變更時依相機目前樓層重算保留集合（新插入／移動的樓層會加入或移除） |
| 相機放置平面（步驟 0）的記錄時序 | DocumentChanged 讀作用中視圖 |
| 平面扇形在俯仰 ≠ 0 時與 Revit 視錐線的投影差 | 未實機比對 |
| 標記在極端俯仰角（接近 ±90°）的退化處理 | 有 try/catch 跳過 |
| 清單常駐 TextBox 在停靠面板內是否確實收到鍵盤輸入 | log 記第一個按鍵／文字輸入 |
| 300 px 寬度下提示文字與欄寬是否不出現水平捲軸 | 只做常數估算（Wave 3 提示縮短到約 20 字） |
| 開檔後無操作崩潰是否已根治 | 直接起因已修（ComboBox 樣式）；診斷 log 常駐；關檔再開的房間快取失效崩潰路徑已修（2026-09-26 稽核修正，待 MCP 複測） |
| **Enscape 是否排除 A 型標記**（相機體＋扇形 DirectShape） | **使用者實測**：程式只在透視圖隱藏子類別「相機標記」；Enscape 讀的是視圖可見性還是整個模型，未查證 |
| 拖 A 實例 → 相機（反向同步）與「相機→標記」是否互相觸發 | 以 1 mm／0.1° 門檻＋交易名稱防護，未實機確認；鎖定相機略過、每台獨立子交易、Undo／Redo 不再觸發反向同步（2026-09-26 稽核修正，待 MCP 複測） |
| 刪除相機後孤兒標記清理 | 依賴面板 live sync 已啟用；Undo／Redo 時不再排清理交易（保留 Redo）（2026-09-26 稽核修正，待 MCP 複測） |
| `VIEWPORT_ATTR_SHOW_LABEL` 的整數與「是／否／多個視埠時」對應 | 先看顯示字串，取不到才以 0＝否；log 記原始值 |
| `ViewSheet.Create` 自動編號、`sheet.Name` 設定在實機的結果 | 未實機 |
| 視角版本還原：perspective 相機 `CropBox` 設定（含 Transform）是否被 Revit 接受 | 失敗只列警告、眼睛與方向照常套用；「裁剪啟用」與「裁剪框」改分開嘗試（2026-09-26 稽核修正，待 MCP 複測） |
| 視角版本還原：`VIEWER_BOUND_ACTIVE_FAR` 參數在透視相機上是否存在／可寫 | 失敗只列警告；遠裁剪開關已列入姿態雜湊（切換會觸發標記更新）（2026-09-26 稽核修正，待 MCP 複測） |
| 還原視角後 DocumentChanged → 標記去抖更新是否觸發 | 交易名稱「還原相機視角：…」不在忽略名單；還原後導覽監看重設基準（不重複排更新）（2026-09-26 稽核修正，待 MCP 複測） |
| 圖紙圖框下拉清單只在「重新整理」時更新 | 新載入的圖框要再按一次重新整理 |

### 2026-09-25 22:20 MCP 自測第一輪修正（大部分已由 MCP 複測，餘項見下表備註）

| 項目 | 備註 |
|---|---|
| 旋轉裁剪框轉正（以 Identity Transform 設 CropBox）是否被 Revit 接受 | log 記「已轉正／未接受」 |
| 室內樓層改房間 Level 後，清單樓層下拉（非建築樓層 Level）與 ③ 位置圖底圖挑選是否正常 | storey＝room.Level 已由 MCP 複測確認；清單下拉／③ 底圖挑選仍待使用者實測 |
| 大模型（數百房間）下每次幾何計算都跑房間搜尋的效能 | 未實測 |

### 2026-09-25 22:50 MCP 自測第二輪修正（大部分已由 MCP 複測，餘項見下表備註）

| 項目 | 備註 |
|---|---|
| 剖面 CropBox 套用後 Revit 實際調整了多少（原點位移） | log「套用後原點被 Revit 調整 N mm」 |
| 標題欄偵測（長直線啟發式）對 Snowdon 以外圖框的準確度 | 見 `DrawAreaSource` |
| 透視複本裁剪框放大：「X/Y/Z×k」或「X/Y×k」哪個能維持 FOV | 見透視視埠 `Note`；兩者都不行會還原並警告 |
| 改 Key Plan 視圖比例時是否被樣板擋住 | 列警告 |

## 已知限制（非本功能缺陷）

- ~~R22／R23 編譯有 11 個既有錯誤~~ → 2026-09-26 已以 `REVIT2024_OR_GREATER` 保護：R22／R23 0 錯誤，`get/set_link_display_settings` 在 2022／2023 回報「需要 Revit 2024 以上」。
- Project Browser「顯示相機」無對應 API，不做（D-022）。
