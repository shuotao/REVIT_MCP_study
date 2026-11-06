# Abby 雙機一鍵安裝與同步

共用來源：[s7814322-ops/REVIT_MCP_study 的 my-revit-tool 分支](https://github.com/s7814322-ops/REVIT_MCP_study/tree/my-revit-tool)。目前基準253個工具，Revit2024／2025／2026。原版main維持獨立，所有同步與推送都限定my-revit-tool。

## 另一台先準備

| 用途 | 前置項目 |
|---|---|
| 日常使用及自動更新 | Windows、需要的Revit版本、Node.js22以上、Codex或其他MCP用戶端，以及可連線GitHub／npm |
| 同時開發工具 | 再安裝Git for Windows；本機要編譯時加.NET10 SDK（建置工具需求，Revit2025／2026仍以.NET8為目標）。GitHub登入必須有此庫的寫入權限 |
| CAD文字與DWG | Python3、ezdxf、ODA File Converter；不使用此功能可稍後安裝 |
| 族群生成 | 對應Revit版本的公制族群範本，路徑依該電腦實際位置指定 |

日常更新是公開Release下載，不需要GitHub CLI、帳號密碼或PAT。安裝器不修改Smart App Control、安全設定、TYBIM或模型。

## 首次安裝

1. 在[Releases](https://github.com/s7814322-ops/REVIT_MCP_study/releases)選擇名稱以`abby-my-revit-tool-`開頭的版本，下載`Abby-Second-PC-*.zip`並完整解壓縮。
2. 儲存並關閉Revit2024／2025／2026，雙擊`Abby-Install.cmd`。勾選／輸入Y可啟用目前使用者的每10分鐘更新排程。
3. 腳本只替已安裝的Revit版本部署。預設偵測標準安裝位置；自訂Revit安裝路徑需先調整偵測。
4. 將`%LOCALAPPDATA%\AbbyMCP\mcp-client.example.toml`提供的設定加入Codex一次。固定入口為`node`加上該電腦`start-mcp.cjs`的完整路徑；不複製別台的使用者路徑或憑證，不覆寫整份既有設定。
5. 啟動Revit及Abby服務，重新載入Codex MCP，確認工具數。

同一台Revit服務只連接一個MCP用戶端。更換設定時保留其他工具設定，避免同時啟用兩個連向同一Revit服務的Abby入口。

## 日後：兩台接收同一個版本

- `Abby-Update.cmd`：從設定好的庫取得最新的**Abby專屬頻道**。不使用整個庫的latest標籤，不混入main或其他分支的Release。
- 每個ZIP及內部檔案都驗SHA256，並檢查manifest的SourceRepository／SourceBranch。檔案來源不符、檔案遭改動、路徑越界時停止。
- Revit開啟時延後並保留原版；不強制關閉程式、不替換執行中的DLL。關閉後下次檢查再部署。
- 舊外掛與runtime保留備份；部署檔案失敗時回復已寫入檔案，成功後才切換固定MCP入口。
- `current.json`記錄Version／SourceCommit／ToolCount。兩台這些值一致才代表安裝同一版本；已開啟的MCP用戶端仍需重新載入才會採用新版工具清單。
- 排程為`AbbyMCP-AutoUpdate`，使用者登入期間每10分鐘執行。可在工作排程器停用。離線時沿用現有版本，不保證兩台執行中的程式瞬間同步。
- 共用skill／SOP隨包提供於`releases\版本\knowledge`。Codex可開啟此資料夾使用其中`.agents/skills`；自己的私人skill與本機設定不放到共用庫。

## 兩台都開發工具

1. 兩台各用`Abby-Clone.cmd`取得新的本機開發資料夾，或執行`git clone --branch my-revit-tool --single-branch https://github.com/s7814322-ops/REVIT_MCP_study.git`。
2. 開始前在乾淨的my-revit-tool使用`Abby-SyncSource.cmd`選1。它只允許快速前進；遇到未提交修改或分歧即停止，不覆蓋、不stash。
3. 可在`codex/...`分支開發。測試、審查公開內容並提交後，合併到本機my-revit-tool，再用`Abby-Publish.cmd`或同步選2推送。
4. 推送**只有my-revit-tool**。GitHub Actions自動建置、驗證並發佈三版安裝包，另一台接收現成Release，不重新編譯。
5. 同時修改相同程式時，Git拒絕非快速前進推送；需集中解決衝突一次，不自動強推。

私人資料不可先commit再移除；公開庫的歷史仍保留舊內容。腳本不會自動`git add .`，不收模型、資料匯出、憑證、本機設定、備份或工作日誌。

## 原版更新只處理一次

在其中一台乾淨的my-revit-tool，使用同步選3。它只fetch原版main，再準備合併到**目前Abby分支**，不更新本機或GitHub的main。確認衝突與自訂擴充、測試後提交並推送；Actions發佈一次，兩台共同接收。

## 維護與驗證

- 安全測試：`powershell -File tests/abby-update-safety.ps1`。
- 公開內容檢查：`node scripts/abby/check-public-source.cjs`。
- 僅驗證ZIP、不部署：`powershell -File scripts/abby/Update-Abby.ps1 -Package ZIP完整路徑 -ValidateOnly`。
- 暫存預設位於安裝目錄staging，避免過長TEMP路徑；可用`-StagingRoot`指定短路徑。
- 完整流程與產物見[Abby Release workflow](../../.github/workflows/abby-release.yml)。Actions中的編譯及靜態測試不等於每個工具都已在所有Revit版本的實際模型上測過。
- TYBIM在部署前後比對檔案雜湊；寫入僅限Abby的RevitMCP資料夾、manifest、worker及自己的使用者runtime目錄。

MCP設定格式參考[OpenAI官方文件](https://developers.openai.com/codex/mcp)。
