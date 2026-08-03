# 綠建材導入 Revit：開發歸檔索引

本目錄集中管理綠建材工具的歷史開發程式、來源快照與中間產物。正式執行入口仍保留在 repository 根目錄，Revit MCP 實作仍保留在 `MCP/` 與 `MCP-Server/`，避免既有 Skill、MCP 設定與使用流程失效。

## 正式來源（持續維護）

| 類別 | 路徑 | 用途 |
|---|---|---|
| Revit C# 實作 | `MCP/Core/Commands/CommandExecutor.GreenMaterial.cs` | 建立綠建材 Material、Type、複合構造與參數寫入 |
| MCP Tool 定義 | `MCP-Server/src/tools/visualization-tools.ts` | 對 AI Client 暴露綠建材工具 |
| 命令註冊 | `MCP/Core/CommandExecutor.cs` | 綠建材命令 dispatch |
| 計畫產生器 | `generate_revit_injection_plan.py` | Set 對映與 Revit Injection Plan 產生 |
| 注入入口 | `apply_revit_injection_plan.py` | Injection Plan 執行入口 |
| 本機 Showcase 服務 | `local_server.py` | 提供展示頁與 Set JSON 同步 API |
| 共享參數驗證 | `validate_shared_params.py` | 驗證 `GreenMaterial_SharedParams.txt` |
| TABC 主資料 | `tabc_master_database.json` | 綠建材標章主資料庫 |
| Set 工作資料 | `exported_material_sets.json` | Showcase、Agent 與 Revit 匯入流程共享狀態 |
| 產出計畫 | `Revit_Injection_Plan.json` | 最近一次產生的注入計畫 |
| 共享參數 | `GreenMaterial_SharedParams.txt` | Revit v4 多材料槽位 Schema |
| 展示頁 | `assets/green-material-showcase.html` | 綠建材搜尋與 Set 管理 UI |

以上路徑均相對於 repository 根目錄。

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
