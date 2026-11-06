# 綠建材導入 Revit：開發歸檔索引

本目錄集中管理綠建材工具的歷史開發程式、來源快照與中間產物。正式執行入口（四個 Python 引擎 + 共享參數檔）自 2026-08 起收斂到本目錄根層級（`tools/green-material/`，非 `archive/`），資料／狀態檔（`tabc_master_database.json`、`exported_material_sets.json`、`Revit_Injection_Plan.json`）與展示頁（`assets/green-material-showcase.html`）、本機伺服器（`local_server.py`）仍留在 repository 根目錄；Revit MCP 實作仍保留在 `MCP/` 與 `MCP-Server/`。以上三個資料／狀態檔與展示頁皆已加入 `.gitignore`，一律留在本機、不隨 repo 散布——但展示頁的 **UI 原始碼**（`assets/green-material-showcase.template.html`）是獨立的、git 追蹤的檔案，見下方「展示頁是產生檔」一節。

## 正式來源（持續維護）

| 類別 | 路徑 | 用途 |
|---|---|---|
| Revit C# 實作 | `MCP/Core/Commands/CommandExecutor.GM_GreenMaterial.cs` | 建立綠建材 Material、Type、複合構造與參數寫入 |
| MCP Tool 定義 | `MCP-Server/src/tools/visualization-tools.ts` | 對 AI Client 暴露綠建材工具 |
| 命令註冊 | `MCP/Core/CommandExecutor.cs` | 綠建材命令 dispatch |
| 計畫產生器 | `tools/green-material/GM_generate_revit_injection_plan.py` | Set 對映與 Revit Injection Plan 產生，並提供 `compare_all_sets()` / `compare_and_refresh_set()` 供 `/GM_set compare` 比對 Set 與最新資料庫差異 |
| 主資料庫更新 | `tools/green-material/GM_update_tabc_database.py` | 從 TABC 官網（`https://tabcmgr.hopto.org`）依 GBMTYPE 1~4 分頁抓取列表頁真實資料，合併回 `tabc_master_database.json`，並從 `assets/green-material-showcase.template.html` 重新產生 `assets/green-material-showcase.html`；由 `/GM_update` 驅動，也是全新 clone 後的首次建立入口（`--resync-html` 可在不連線 TABC 的情況下單獨重新套用最新樣板） |
| 注入入口 | `tools/green-material/GM_apply_revit_injection_plan.py` | Injection Plan 執行入口 |
| 本機 Showcase 服務 | `local_server.py` | 提供展示頁與 Set JSON 同步 API |
| 共享參數驗證 | `tools/green-material/GM_validate_shared_params.py` | 驗證 `GreenMaterial_SharedParams.txt` |
| 新鮮度／效期驗收測試 | `tools/green-material/GM_test_freshness_gate.py` | issue #128 三層（時間戳讀回／30 天門檻／過期標章硬擋）的 RED/GREEN 測試。不連網、不碰本機資料庫，全在 tempfile 合成 fixture 上跑，證號用 `GBM000000x` 佔位符。`python tools/green-material/GM_test_freshness_gate.py`，exit 0 為全過 |
| TABC 主資料 | `tabc_master_database.json` | 綠建材標章主資料庫（本機專屬，不入庫） |
| 抓取時間戳 | `tabc_master_database.meta.json` | 主資料庫上次真實抓取的時間與筆數（本機專屬，不入庫）。由 `GM_update_tabc_database.py` 的真實執行寫入（`--dry-run`／`--resync-html` 皆不寫），`GM_generate_revit_injection_plan.py` 的 `database_freshness()` 讀回，供 `/GM_import` 在擬訂計畫前告知使用者資料多舊；缺席時退回主資料庫檔案 mtime 的推估值 |
| Set 工作資料 | `exported_material_sets.json` | Showcase、Agent 與 Revit 匯入流程共享狀態（本機專屬，不入庫） |
| 產出計畫 | `Revit_Injection_Plan.json` | 最近一次產生的注入計畫（本機專屬，不入庫） |
| 共享參數 | `tools/green-material/GreenMaterial_SharedParams.txt` | Revit v4 多材料槽位 Schema |
| **展示頁 UI 樣板** | `assets/green-material-showcase.template.html` | 綠建材搜尋與 Set 管理 UI 的**唯一原始碼，git 追蹤**。要改 UI／JS／CSS 一律改這裡 |
| 展示頁（產生檔） | `assets/green-material-showcase.html` | 樣板 + 本機 `tabc_master_database.json` 拼接產生，本機專屬、不入庫，請勿手動編輯 |
| 專案看板 | `tools/green-material/GM_kanban.html` | Monstrare 專案看板；由 `/GM_kanban` 開啟，內嵌 `cardsData` 與 `tools/kanban/index.html` 互為鏡像，皆由 `tools/kanban/cards/*.json` 產生；頁內「🔗 連結專案資料夾」寫回功能靠 `getGmKanbanFileHandle()` 找到本檔案 |

以上路徑均相對於 repository 根目錄。

### TABC 資料未隨 repo 授權再散布

`tabc_master_database.json` 與 `assets/green-material-showcase.html` 內含財團法人臺灣建築中心（TABC）的綠建材標章資料，不屬於本 repo 的 MIT 授權範圍，因此自 2026-08 起已加入 `.gitignore`、不再被 git 追蹤（檔案仍保留在本機）。

首次 clone 本 repo 或需要更新資料時，執行 `GM_update_tabc_database.py`（或 `/GM_update`）從 TABC 官網重新抓取，即可在本機重建這兩個檔案：`tabc_master_database.json` 不存在時會自動視為空資料庫、走全量匯入，不會報錯；`local_server.py`、`GM_generate_revit_injection_plan.py` 等工具皆讀取本機檔案，不需要它們存在於 git 歷史中。

### 展示頁是產生檔，UI 改動請改樣板

`assets/green-material-showcase.html` 過去曾經整個檔案（含 UI／JS／CSS，不只資料）都被 `.gitignore` 排除，代表任何人在本機對 UI 做的修改都無法透過 git 傳到別台電腦——這是 2026-08 發現的實際問題：曾經有 UI 功能只存在於某台電腦的本機檔案裡，從未進過 git。

修法是把 UI 原始碼抽成獨立、git 追蹤的 `assets/green-material-showcase.template.html`；`GM_update_tabc_database.py` 每次執行（含 `--resync-html`）都會用這份樣板 + 當下的資料重新產生完整的 `assets/green-material-showcase.html`，並整份覆寫。因此：

- **要改 UI／JS／CSS：改 `assets/green-material-showcase.template.html`**，改完跑 `python tools/green-material/GM_update_tabc_database.py --resync-html`（不連線 TABC，秒級）套用。
- **不要手動編輯 `assets/green-material-showcase.html`**——下次任何一次 `/GM_update` 執行都會被整份覆寫掉，改動會消失且不會有警告。
- 別台電腦 `git pull` 到樣板更新後，跑一次 `--resync-html` 就能拿到最新 UI，不需要重新抓一次 TABC 資料。

### 注入計畫快照也不入庫

`Revit_Injection_Plan.json` 與 `docs/green-material/Revit_Injection_Plan_Report.md` 是 `/GM_import`／`/GM_set compare` 每次執行都會整份覆寫的「最新一次」計畫快照，沒有保存歷史版本的價值，自 2026-08 起同樣加入 `.gitignore`。執行 `/GM_import` 或 `/GM_set compare` 即可在本機重新產生。

### Set 工作資料也不入庫

`exported_material_sets.json`（透過 `local_server.py` 的 `POST /api/save-sets` 寫入）是使用者個人在本機累積的材料 Set，不是專案共用資料，2026-08 起加入 `.gitignore` 並從 git 追蹤移除（檔案仍保留在本機，不會被刪除）——避免多台電腦之間透過 `git pull` 互相看到彼此儲存的 Set。每台電腦的 Set 各自獨立，不會跨機同步。

### `archive/` 目錄未隨本 repo 收編

原始開發分支（PR #116）另有一個 `tools/green-material/archive/` 目錄，內含財團法人臺灣建築中心（TABC）的真實綠建材認證資料片段（真實證號、公司名稱、認證起訖期間、官方網站圖片 URL）、網站頁面快照（含 `tabc_search.html`），以及開發過程中的一次性抓取／同步／診斷／維護腳本與中間 JSON。收編進本 repo 時整批刻意排除——這些內容含第三方著作與真實案件資料，不得隨本 repo 的 MIT 授權再散布。

這與 `tabc_master_database.json` 那種「`.gitignore` 排除、但可在本機重新產生」的情況不同：`archive/` 從未進入本 repo 的任何一次 commit，`GM_update_tabc_database.py` 也不會、不能重建它——它不是可重新產生的本機快取，而是永久性的收編排除。

**本文件與其他 domain／skill 文件中如果出現 `archive/...` 或 `tabc_search.html` 這類路徑，指的都是原始 PR 分支裡的內容，不是本 repo 檢出後可以找到的檔案。** 保留這些路徑提及，是為了給想追溯歷史脈絡的人留下線索（例如某個設計決策的早期分析依據曾經寫在哪份報告），不是宣稱該檔案存在於本 repo。下方「目錄分類」表格描述的正是原始分支 `archive/` 的內容，於本 repo 中不存在。

## 目錄分類

以下各列描述的是原始開發分支 `archive/` 的內容（本 repo 未收編，見上方「`archive/` 目錄未隨本 repo 收編」一節），僅供追溯脈絡，本 repo 檢出後不存在這些路徑：

| 目錄 | 保存內容 | 維護狀態 |
|---|---|---|
| `archive/scripts/catalog/` | TABC 抓取、同步、資料補強腳本 | 歷史工具；執行前應先檢查來源網站與輸出路徑 |
| `archive/scripts/showcase/` | Showcase 生成與一次性資料注入腳本 | 歷史生成器；正式頁面以 `assets/green-material-showcase.html` 為準 |
| `archive/scripts/diagnostics/` | HTML 解析、除錯及抽樣腳本 | 僅供追溯，不屬正式流程 |
| `archive/scripts/maintenance/` | 綠建材看板資料維護腳本（含 `add_reset_func.py`：一次性 patch `kanban.html`／`tools/kanban/index.html` 加入 `resetKanbanState()`，patch 已生效） | 一次性維護工具 |
| `archive/data/` | 抓取與分類過程中的中間 JSON | 歷史資料，不作正式查詢來源 |
| `archive/reports/` | 開發期間的文字分析結果 | 歷史報告 |
| `archive/snapshots/` | 原始或重複 HTML 快照 | 歷史快照；不作公開入口 |
| `docs/green-material/` | 架構、命名、對映與最近一次計畫報告 | 人類與開發者文件 |

## 歸檔規則

1. 新的正式功能應寫入既有 `MCP/`、`MCP-Server/` 或本目錄根層級的四個 Python 入口，不要在 repository 根目錄或本目錄根層級新增一次性腳本。
2. 網站抓取或資料清理實驗放入對應的 `archive/scripts/` 子目錄，檔名需描述目的。
3. 中間資料、抽樣資料和除錯輸出放入 `archive/data/` 或 `archive/reports/`；不得取代 `tabc_master_database.json`。
4. 綠建材設計文件統一放在 `docs/green-material/`，Domain SOP 仍留在 `domain/`，Skill 仍留在 `.claude/skills/` 或 `.agents/skills/`。
5. 每次改動正式 Tool 時，同步檢查 C# 命令、TypeScript Tool Schema、共享參數 Schema、Skill 與本索引。
6. 提交前執行 `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/verify-qaqc.ps1 -SkipBuild -SkipDeploy`。

## 歷史腳本注意事項

歸檔腳本保留原始內容以便追溯，部分仍使用開發當時的相對路徑或依賴外部 TABC 網站。它們不是正式 runtime dependency；若要重新啟用，請先搬回受維護的工具區、改用明確的 repository-root 路徑，並補上測試。

日常資料更新請一律使用 `tools/green-material/GM_update_tabc_database.py`（`/GM_update` 驅動），不要直接執行 `archive/scripts/catalog/fetch_all_tabc_master.py` 或 `sync_full_1041_database.py`——後兩者是一次性、覆寫式腳本（會整批覆蓋 `tabc_master_database.json`，沒有合併/保留既有紀錄的機制），`GM_update_tabc_database.py` 才是採合併式更新（新增/更新/保留未再出現紀錄）的維護版本。`cnsSpec`/`testItems`/`qualifiedItems` 等試驗數據欄位沿用 `enrich_tabc_specs_database.py` 的關鍵字規則模板推論產生，並非逐筆從 TABC 詳細頁面（`CaseDataInfo.aspx`）抓取的真實試驗數據——這是既有資料庫本身的既定做法，`GM_update_tabc_database.py` 延續此做法，未改變資料真實性等級。
