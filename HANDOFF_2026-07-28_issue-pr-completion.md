# HANDOFF — 2026-07-28 Issue/PR 收攏（跨機接手）

---

## ✅ Mac session 執行結果（2026-07-28，回拋 Windows）

> Mac 端（`gh` 登入 `shuotao`，具 `repo`+`workflow` write）已把 handoff 的 **GitHub 寫入部分全部完成**。Mac **無 Revit**，凡需 build/runtime 的都原樣留給 Windows。

**Mac 已完成：**
- ✅ **§2 fix PR → 已開 [PR #101](https://github.com/shuotao/REVIT_MCP_study/pull/101)**（head `fix/issues-90-93-and-87-2026-07-28` → base `main`；標題/body 照 §2）。
- ✅ **§3 六則 issue 回覆全部照貼**：#98 / #99 / #100 / #74 / #75 / #62（維護者語氣、繁中、credit 貢獻者，逐字照 §3）。
- ✅ **§4 #56 reclaim（部分）**：逐 chain 比對 `preview/lesley-pr30-snapshot` vs `main`，找出**唯一尚未進 main** 的工具 chain = `auto_dimension_walls`（劉可 commit `f84d3d3`，其餘 chain 先前波次已收編）。已 cherry-pick 至分支 `reclaim/pr30-lesley-auto-dimension`（**保留 Author=lesleyliuke**），開 **draft [PR #102](https://github.com/shuotao/REVIT_MCP_study/pull/102)**。Mac 端已驗：cherry-pick 零衝突、TS `npm run build` exit 0、C# 靜態檢查（大括號平衡、ID 全用 `IdType`+`.Id.GetIdValue()` 跨版本安全、helper 在 main 已存在）。

**Windows 待完成（本 Mac 做不到的）：**
- ⏳ **#56 reclaim 收尾（draft PR #102）**：`dotnet build Release.R24 + R26` 0 error → Revit runtime 實測三模式 → **工具計數 166→167 同步**（`CLAUDE.md`/`README.md`/`README.zh-TW.md`/`docs/DOCUMENT_AUDIENCE_INVENTORY.md`/`docs/BIM_MCP/**` grep `166` 逐處）→ `verify-qaqc.ps1 -SkipDeploy` 全綠 → draft 轉正、merge → 於 **#56 回覆感謝並明列 credit 劉可**（真名＝劉可，非劉啟祥）。
- ⏳ **§5 fix PR #101 的 Revit runtime 實測**（#87 柱偵測 0→>0、柱頂對齊、幾何射線；#90 樑 StructuralUsage、零長 PolyLine 不 crash）→ red→green 後 merge PR #101。

> 下方為原始 handoff 全文（供對照，Windows 照做即可）。

---

> 目的：本機 session 已完成**程式碼側 + PR#95 合併**，但 **GitHub 寫入受限**（github MCP 的 PAT 唯讀，`merge`/`create PR`/`comment` 全 403；瀏覽器自動化貼文遇 React 受控 textarea + 擴充無回應）。此文件讓**另一台有 GitHub write 權限的機器**（`gh` 已登入、或 PAT 具 repo write、或直接用 GitHub UI）完成剩餘工作。所有全文、分支、SHA、指令都在此，**可直接照做**。

接手方式：
```
git fetch origin
git checkout fix/issues-90-93-and-87-2026-07-28   # 本 handoff 就在這分支根目錄
gh auth status                                     # 確認有 write 權限的帳號
```

---

## 1. 目前 Git 狀態（已 push）

- Repo：`shuotao/REVIT_MCP_study`（本機路徑 `C:\Users\01102088\Desktop\REVIT_MCP\REVIT_MCP_study`）
- `main = 0e2e2f5`（已 push）— 已含 **PR#95 合併**（`d1d85b6` by @Roy-y111，docs-only，GitHub 應已標 Merged）
- 分支 `fix/issues-90-93-and-87-2026-07-28 = 753acf2`（已 push），6 個 commit：
  | SHA | Issue | 內容 |
  |---|---|---|
  | `d0d3263` | #87 | cherry-pick @Jacky820507 `6ff7a7e`：IFC 柱來源 union 掃 `OST_Columns`+`OST_StructuralColumns`；`TrySetColumnTopAttachment` 改 built-in 參數；射線/幾何 floor-hit 雙向搜尋+fallback |
  | `1a32c4e` | #90 | `PlaceBeamInstance` 傳入 beamRole→設 `StructuralUsage`（大樑/地樑→Girder、次樑→Joist）+ PolyLine 零長守衛 + TS 描述 |
  | `9fe432e` | #92 | 柱號 `MatchSymbolByLabel` 三段 `OrdinalIgnoreCase`→`Ordinal` |
  | `5fa582f` | #91 | `install-addon.ps1` DLL 複製到 `RevitMCP\` 子夾（符合 `.addin`） |
  | `a342662` | #93 | `ezdxf_worker.py` 匯入失敗訊息補 `sys.executable`/版本、放寬 `except` |
  | `753acf2` | — | log 記錄 |
- 驗證：**R24 + R26 build 0 error**；`tsc` / `py_compile` / PowerShell 語法 OK；QAQC `-SkipBuild -SkipDeploy` **54 PASS / 0 FAIL**。
- 未完成：Revit runtime 實測（見 §5，老師指示暫緩）。

---

## 2. 開 fix PR

- head：`fix/issues-90-93-and-87-2026-07-28` → base：`main`
- compare URL：<https://github.com/shuotao/REVIT_MCP_study/pull/new/fix/issues-90-93-and-87-2026-07-28>
- `gh` 指令：`gh pr create --base main --head fix/issues-90-93-and-87-2026-07-28 --title "<下方標題>" --body-file <把下方 body 存檔>`

**標題**：`fix: #90/#92/#91/#93 bug 修正 + #87 IFC helper 收攏（待 Revit runtime 實測）`

**Body**：
```markdown
收攏處理 @Roy-y111（#90/#91/#92/#93）回報的 bug 與 #87 的 reclaimed helper 複核。每個 issue 一個 commit。

## 變更
- **#87** `d0d3263`（@Jacky820507 原作，cherry-pick）：IFC 柱來源改 union 掃 `OST_Columns` + `OST_StructuralColumns`（跨版本 `IdType` 去重）；`TrySetColumnTopAttachment` 改用 built-in `COLUMN_TOP_ATTACHED_PARAM` / `COLUMN_TOP_ATTACHMENT_OFFSET_PARAM`；射線/幾何 floor-hit 改基準高度上下雙向搜尋 + fallback。
- **#90** `1a32c4e`：`PlaceBeamInstance` 傳入 `beamRole` → 設 `FamilyInstance.StructuralUsage`（大樑/地樑→Girder、次樑→Joist）；PolyLine 零長守衛；`dwg-beam-tools.ts` 描述同步。
- **#92** `9fe432e`：柱號 `MatchSymbolByLabel` 三段 `OrdinalIgnoreCase` → `Ordinal`（台灣柱號全大寫，避免 C1 誤配 c1）。
- **#91** `5fa582f`：`install-addon.ps1` 將 DLL + Newtonsoft 複製到 `RevitMCP\` 子夾，符合 `.addin` 的相對路徑。
- **#93** `a342662`：`ezdxf_worker.py` 匯入失敗訊息附 `sys.executable`/版本、放寬 `except Exception`。

## 驗證狀態
- [x] R24 build 0 error
- [x] R26 build 0 error（跨版本 int/long）
- [x] TS `tsc` / Python `py_compile` / PowerShell 語法
- [ ] Revit 2024 runtime 實測待補：#87 柱偵測數（0→>0）、柱頂對齊、幾何射線命中；#90 樑 StructuralUsage、零長 PolyLine 不 crash

Closes #90, #91, #92, #93. Refs #87.
```
（合併前請完成 §5 的 Revit 實測再 merge，維持 red→green。）

---

## 3. 六則 issue 回覆全文（照貼）

> 語氣＝維護者，繁中，credit 貢獻者。貼法：GitHub UI 直接貼，或 `gh issue comment <n> -R shuotao/REVIT_MCP_study --body-file <檔>`。

### #98 @Archwiz-boss（Archicad backend）
```markdown
@Archwiz-boss 感謝這份完整的 RFC，也謝謝你已經跑到 fork 端的實機連線與 pilot 規格。方向我認同，逐一回你六個確認點：

1. **接受「Revit 預設不變、Archicad opt-in 共存」** — 這是保證現有使用者零風險的前提。git 追蹤的 `.mcp.json` / `.vscode/mcp.json` 必須維持 Revit-only（你已列為保護條件，讚）。
2. **接受三種可攜性分級**（backend-neutral / adapter-required / backend-specific）。這剛好對應我們的落差：很多 skill 綁 Revit `ElementId`／internal feet／Family／ViewTemplate，不能只換名詞。
3. **需要正式 portability matrix**，但請先以 `domain/*.md` 形式提交 —— 依 CONTRIBUTING，`MCP/`、`MCP-Server/src/` 由維護者管理，domain 文件才是貢獻者可直接提的區塊。
4. `.agents/skills` mirror **只加已完成 live test 的跨 backend workflow**，同意；不要一次宣稱全部 skill 都支援 Archicad。
5. **接受三個 pilot**（element-query／room-numbering／quantity-takeoff-excel）—— 選得好，涵蓋唯讀查詢、寫入+read-back、量算三種風險面。
6. **同意拆小 PR**，建議順序：
   - PR-1：`domain/` 下的 adapter 邊界 + 名詞／能力對照 + portability matrix（純文件，先合）
   - PR-2：三個 pilot 的 backend routing／驗證規格（仍以 domain 文件為主；adapter 程式碼由維護者依規格實作，介面在本 issue 對）
   - PR-3：Agy／Codex client 支援與後續 live-tested workflow

一個提醒：你 fork 是 `BIM_MCP_study`、上游是 `REVIT_MCP_study`，canonical skill 數這邊是 **50**（不是 52）—— PR 時請以上游實際檔案為準對齊，免得計數 QA 卡關。先從 PR-1 的 matrix + adapter 邊界開始，我優先看。🙏
```

### #99 @NicheSam（開孔定位掃描）
```markdown
@NicheSam 謝謝，尤其謝謝你把「掃描成功 ≠ 全部可自動建立」講清楚（13 筆只有 1 筆正常候選、12 筆穿梁需複核）—— 這正是我們要的分辨力。方向確認：

1. **同意建在既有 `detect_clashes` 核心上**，不要再做第二套幾何演算法。我們已有 `domain/mep-csa-clash-detection.md` 與 `domain/beam-penetration-*` 一整組，scan 應沿用。
2. **`detect_clashes.csaSource.linkInstanceId` 的 schema 缺口**：C# 端既然已能讀，補 TS schema 是明確且低風險，這塊我可以直接補（屬 `MCP-Server/src/`，維護者管理）。
3. **新增唯讀 `scan_opening_candidates`**：回傳每筆 MEP／Host ElementId、LinkId、entry/exit/center、建議開孔尺寸、`candidate`／`review_required` + `warningCodes`。第一版**只掃描，不建套管／開孔族群／預覽標記** —— 同意這個邊界。
4. 尺寸規則（Pipe/Conduit 直徑+雙側 clearance；Duct/CableTray 寬高+雙側；樑柱／斜穿／過短交集→review_required）請寫進一份 `domain/*.md` SOP，依 CONTRIBUTING 你可直接提這份文件，我依規格接程式碼。

你發現 SC listener 依賴 Revit `Idling` 那點也很重要 —— 正式 MCP 不該依賴隱藏互動狀態；我們的 bridge 走 `ExternalEventManager`，沒有這問題。先請你提 domain SOP 草稿，`linkInstanceId` schema 我這邊補。🙏
```

### #100 @NicheSam（CAD 點位放置）
```markdown
@NicheSam 這個跟現有 DWG 柱／樑工具確實不同（那兩個解矩形輪廓／雙線，你這是 Block insertion point → FamilyInstance），不會重疊，方向 OK。三工具拆法（discover／preview／create）也對。重點回應：

1. **最關鍵的是你實測發現的 transform 偏移** —— 我完全同意：預覽階段不建十字／群組、不吃人工拖曳後座標，`preview` 必須回傳**可檢查的座標鏈**（Block insertion point、Block transform、ImportInstance TotalTransform），transform 不可信時**回傳明確警告並停止建立**，而不是叫 AI 猜 correction。這是這個工具能不能成立的分水嶺。
2. **第一版邊界**（non-hosted、level-based、point-placement family；不含 hosted／face-based／work-plane-based、不自動選 symbol／level、不轉輪廓、不人工校正）—— 同意，清楚。
3. **`create` 用與 `preview` 相同參數重新掃描來源 Block 驗證**、主 Transaction + 逐筆 SubTransaction（單筆失敗不回滾其他）—— 交易模型正確。
4. `bt11_10` 目前只驗技術流程、未證工程語意 —— 收到，mapping 語意留待後續，不寫死。

流程同 #99：請先提一份 `domain/*.md` SOP（discover／preview／create 的欄位與驗證合約），我依規格接 `MCP/` 程式碼。🙏
```

### #74 @yunchen-kt（門窗圖例 Key A/B）
```markdown
@yunchen-kt 這個對照很有價值，尤其你點出 A 方案的脆弱點是**結構性的** —— Key 活在 view 內 text note 時，更新得逐一解析 note + 位置配對，`window_ffl_missing_same_type_still_used` 那類 skip 規則其實都在補這個缺口。B 把字串匹配換成**容器邊界匹配**（view 名唯一、使用者改不掉），讓 regenerate 天然冪等 —— 對「里程碑整批重生」確實更穩，代價是 view 數變多。

我的看法：**兩者不必二選一**，而是不同使用模式的取捨（A 省 view 數、B 省更新期匹配脆弱度）。最有幫助的形式是：先在 `domain/door-window-legend-workflow.md` 補一節談這個 trade-off（A／B 各自適用情境 + B 的 view-name=Key 冪等更新流程）；若之後 B 的跨環境 SOP 夠完整，再獨立成檔。

你列的 Revit API 事實（Legend Component 無法 tag、無法從零建 Legend view、Generic Annotation 可入 Legend／Section 當模板欄位載體）都正確，很適合寫進 SOP 當設計約束。依 CONTRIBUTING 這份 domain 文件你可直接提 PR，我很樂意 review。🙏
```

### #75 @CyberPotato0416（試車排氣點檢查）
```markdown
@CyberPotato0416 這題很實際 —— 高點排氣閥在前期 PID 沒有高程維度，只能在竣工前靠幾何檢查補。把它變成 rule check，我的想法：

- **數學上**是沿「連通管段」找**局部高程極大值**（elevation 先升後降的頂點），而非全域最高點 —— 你舉的ㄇ型走向就是最典型的 local maximum。
- 你點到的難處（分歧管、連續攀升）正是關鍵：純幾何不夠，需要**管路拓樸**（Pipe／Fitting connector 連通關係）。沿每條連通路徑走訪、在每個 connector 節點比較上下游高程，找出「上游較低 + 下游較低」的 apex 節點，再檢查該節點附近有無排氣閥件 → 沒有就警示；分歧處對每條分支各自判斷。
- 連續攀升（單調上升到端點）則端點本身就是高點候選。

建議先開一份 `domain/*.md` 把**規則、公式與邊界案例**（分歧、連續攀升、AAV vs 手動排氣）寫清楚（domain 文件先於程式碼是我們的慣例）。有了明確判定合約，再評估要不要做成 MCP 工具（需要 connector／topology 讀取，我們有 `get_connector_info` 可先探）。依 CONTRIBUTING 你可直接提這份 domain SOP，我們一起把規則收斂。🙏
```

### #62 @EricKuo123（DWG 版本檢查器；已 ping 過、輕 follow-up）
```markdown
@EricKuo123 再輕輕 ping 一下 👋 上面兩個確認點（要檢查的是 DWG/DXF **檔案格式版本** AC1027 之類，還是 CAD 軟體／匯出相容性？觸發時機是**匯入前掃描**還是**連結後驗證**？）有空回一下，我們就能評估要不要做成正式工具。若已在試作，哪怕只是 DXF header `$ACADVER` 解析的小 prototype PR 都歡迎。這個 issue 先保持 OPEN 當追蹤，不急 🙂
```

---

## 4. #56 reclaim 任務（@lesleyliuke＝劉可，PR#30）

老師指示：**不是單純 ping/關閉，而是「回溯 fork 內容 → 檢查劉可做的 code → 評估可 merge 的部分 → merge 並 credit 劉可 → 寫感謝」。**

背景與起點：
- PR#30 原始規模巨大（23 commits / 83 files / 15,317 行 / 6+ chain），已 close 待拆分；劉可 fork = `lesleyliuke/REVIT_MCP_study`（PR#30 head）。
- 上游有快照分支 **`preview/lesley-pr30-snapshot`**（維護者留言提及，commit 都在）。
- **重要**：PR#30 多數 chain 疑似已在先前 harvest 進 main —— 下列工具現已存在於 166 工具中：`hide_elements`/`unhide_elements`/`set_category_visibility`、`unjoin_element_joins`、`batch_set_material`/`batch_set_room_height`、`dedup_detail_elements_in_view`、`read_excel_tables`/`create_legends`/`import_excel_to_drafting_views`/`scale_drafting_view_height`/`width`/`move_text_notes_in_views`、`copy_sheets_from_file`/`batch_apply_view_template`/`copy_detail_items_to_views`/`align_titleblocks_on_sheets`/`position_viewports_on_sheet`/`move_viewport_titles`、`create_floor_plans_from_template`。

接手步驟：
1. `git fetch origin 'refs/heads/preview/lesley-pr30-snapshot:pr30-snap'`（或抓 `lesleyliuke` fork 的 PR#30 head）。
2. `git log --oneline main..pr30-snap` 與 `git diff --stat main pr30-snap` 逐 chain 比對，**找出真正尚未進 main 的部分**（sketch-to-revit chain、`SilentFailuresPreprocessor`/`TransactionHelper` 基礎設施、`auto_dimension_walls`、`CreateWall` 多參數等最可能是缺口）。
3. 對每塊未收編的：評估可 merge 性 → build R24+R26 驗 → 需要時 Revit 實測。
4. 收編時**保留劉可 authorship**（cherry-pick 她的 commit，或 commit 標 `Co-Authored-By`/`Author=劉可`），並在 #56 回覆感謝、明列已收編/credit（真名＝**劉可**；記憶：`lesleyliuke=劉可`，非劉啟祥）。
5. #56 依收編結果收尾（可留 OPEN 追蹤剩餘 chain，或結案並感謝）。

> 注意：`MCP/`、`MCP-Server/src/` 由維護者管理，收編是維護者行為（非要求劉可重提），符合 CONTRIBUTING。

---

## 5. Revit runtime 實測（#87 / #90，red→green）

部署（Revit **關閉**時）：
- build：於 `MCP/` 執行 `dotnet build -c Release.R24 RevitMCP.csproj`
- 複製：`MCP/bin/Release.R24/RevitMCP.dll` → `%APPDATA%\Autodesk\Revit\Addins\2024\RevitMCP\RevitMCP.dll`（**子夾**！這是 manifest 實際載入處；父層那顆 Revit 會忽略）。或跑修好的 `scripts/install-addon.ps1`（互動式，會停在 `Read-Host`，需人工按 Enter）。
- 重開 Revit → ribbon 啟用 MCP 服務（`localhost:8964`）。

驗證項：
- **#87**：`sync_ifc_structural_to_native` 對標準結構模型 → **柱偵測數應 > 0**（修正前為 0，`ColumnPlans:0`）；柱頂 `COLUMN_TOP_ATTACHED_PARAM` 已設、對齊正確；幾何射線（`CollectGeometryFloorBottomHitsAtPoint`）命中樓板底。
- **#90**：`create_beams_from_dwg`（帶 `beamRole=大樑`）→ 建立的樑 `StructuralUsage = Girder`（次樑批次應為 `Joist`）；含零長（重合頂點）PolyLine 的圖層不再 crash。

---

## 6. 權限/環境注意

- **github MCP 的 PAT 唯讀** → `merge_pull_request`/`create_pull_request`/`add_issue_comment` 皆回 **403**。API 寫入請改用有 repo write 的 `gh`/PAT，或 GitHub UI。
- 本機 **git push 有效**（Windows 憑證管理員快取），`main` 與 fix 分支皆已 push。
- 記憶：跨版本 C# 改動必 build **R24 且 R26**（int/long 差異）；sequential git ops；貢獻者用真名回覆。
