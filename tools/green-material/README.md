# 綠建材導入 Revit：開發歸檔索引

本目錄集中管理綠建材工具的歷史開發程式、來源快照與中間產物。正式執行入口仍保留在 repository 根目錄，Revit MCP 實作仍保留在 `MCP/` 與 `MCP-Server/`，避免既有 Skill、MCP 設定與使用流程失效。

## 正式來源（持續維護）

| 類別 | 路徑 | 用途 |
|---|---|---|
| Revit C# 實作 | `MCP/Core/Commands/CommandExecutor.GM_GreenMaterial.cs` | 建立綠建材 Material、Type、複合構造與參數寫入 |
| MCP Tool 定義 | `MCP-Server/src/tools/visualization-tools.ts` | 對 AI Client 暴露綠建材工具 |
| 命令註冊 | `MCP/Core/CommandExecutor.cs` | 綠建材命令 dispatch |
| 計畫產生器 | `GM_generate_revit_injection_plan.py` | Set 對映與 Revit Injection Plan 產生，並提供 `compare_all_sets()` / `compare_and_refresh_set()` 供 `/GM_set compare` 比對 Set 與最新資料庫差異 |
| 主資料庫更新 | `GM_update_tabc_database.py` | 從 TABC 官網（`https://tabcmgr.hopto.org`）依 GBMTYPE 1~4 分頁抓取列表頁真實資料，合併回 `tabc_master_database.json` 並同步 `assets/green-material-showcase.html` 內嵌快取；由 `/GM_update` 驅動 |
| 注入入口 | `GM_apply_revit_injection_plan.py` | Injection Plan 執行入口 |
| 本機 Showcase 服務 | `local_server.py` | 提供展示頁與 Set JSON 同步 API |
| 共享參數驗證 | `GM_validate_shared_params.py` | 驗證 `GreenMaterial_SharedParams.txt` |
| TABC 主資料 | `tabc_master_database.json` | 綠建材標章主資料庫 |
| Set 工作資料 | `exported_material_sets.json` | Showcase、Agent 與 Revit 匯入流程共享狀態 |
| 產出計畫 | `Revit_Injection_Plan.json` | 最近一次產生的注入計畫 |
| 共享參數 | `GreenMaterial_SharedParams.txt` | Revit v4 多材料槽位 Schema |
| 展示頁 | `assets/green-material-showcase.html` | 綠建材搜尋與 Set 管理 UI |

以上路徑均相對於 repository 根目錄。

### TABC 資料未隨 repo 授權再散布

`tabc_master_database.json` 與 `assets/green-material-showcase.html` 內含財團法人臺灣建築中心（TABC）的綠建材標章資料，不屬於本 repo 的 MIT 授權範圍，因此自 2026-08 起已加入 `.gitignore`、不再被 git 追蹤（檔案仍保留在本機）。

首次 clone 本 repo 或需要更新資料時，執行 `GM_update_tabc_database.py`（或 `/GM_update`）從 TABC 官網重新抓取，即可在本機重建這兩個檔案；`local_server.py`、`GM_generate_revit_injection_plan.py` 等工具皆讀取本機檔案，不需要它們存在於 git 歷史中。

## 目錄分類

| 目錄 | 保存內容 | 維護狀態 |
|---|---|---|
| `archive/scripts/catalog/` | TABC 抓取、同步、資料補強腳本 | 歷史工具；執行前應先檢查來源網站與輸出路徑 |
| `archive/scripts/showcase/` | Showcase 生成與一次性資料注入腳本 | 歷史生成器；正式頁面以 `assets/green-material-showcase.html` 為準 |
| `archive/scripts/diagnostics/` | HTML 解析、除錯及抽樣腳本 | 僅供追溯，不屬正式流程 |
| `archive/scripts/maintenance/` | 綠建材看板資料維護腳本 | 一次性維護工具 |
| `archive/data/` | 抓取與分類過程中的中間 JSON | 歷史資料，不作正式查詢來源 |
| `archive/reports/` | 開發期間的文字分析結果 | 歷史報告 |
| `archive/snapshots/` | 原始或重複 HTML 快照 | 歷史快照；不作公開入口 |
| `docs/green-material/` | 架構、命名、對映與最近一次計畫報告 | 人類與開發者文件 |

## 歸檔規則

1. 新的正式功能應寫入既有 `MCP/`、`MCP-Server/` 或四個根目錄 Python 入口，不要在根目錄新增一次性腳本。
2. 網站抓取或資料清理實驗放入對應的 `archive/scripts/` 子目錄，檔名需描述目的。
3. 中間資料、抽樣資料和除錯輸出放入 `archive/data/` 或 `archive/reports/`；不得取代 `tabc_master_database.json`。
4. 綠建材設計文件統一放在 `docs/green-material/`，Domain SOP 仍留在 `domain/`，Skill 仍留在 `.claude/skills/` 或 `.agents/skills/`。
5. 每次改動正式 Tool 時，同步檢查 C# 命令、TypeScript Tool Schema、共享參數 Schema、Skill 與本索引。
6. 提交前執行 `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/verify-qaqc.ps1 -SkipBuild -SkipDeploy`。

## 歷史腳本注意事項

歸檔腳本保留原始內容以便追溯，部分仍使用開發當時的相對路徑或依賴外部 TABC 網站。它們不是正式 runtime dependency；若要重新啟用，請先搬回受維護的工具區、改用明確的 repository-root 路徑，並補上測試。

日常資料更新請一律使用根目錄的 `GM_update_tabc_database.py`（`/GM_update` 驅動），不要直接執行 `archive/scripts/catalog/fetch_all_tabc_master.py` 或 `sync_full_1041_database.py`——後兩者是一次性、覆寫式腳本（會整批覆蓋 `tabc_master_database.json`，沒有合併/保留既有紀錄的機制），`GM_update_tabc_database.py` 才是採合併式更新（新增/更新/保留未再出現紀錄）的維護版本。`cnsSpec`/`testItems`/`qualifiedItems` 等試驗數據欄位沿用 `enrich_tabc_specs_database.py` 的關鍵字規則模板推論產生，並非逐筆從 TABC 詳細頁面（`CaseDataInfo.aspx`）抓取的真實試驗數據——這是既有資料庫本身的既定做法，`GM_update_tabc_database.py` 延續此做法，未改變資料真實性等級。
