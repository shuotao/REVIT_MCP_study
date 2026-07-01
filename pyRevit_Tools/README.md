# pyRevit_Tools — Revit 內建按鈕擴充（pyRevit extension）

本資料夾與專案主系統的關係是「**並列、互補、非取代**」。獨立於 `scripts/` 與 `MCP/` 而存在，原因如下。

## 用途

`MCP_Tools.extension/MCP_Schedules.tab/Standard.panel/CreateSchedules.pushbutton/script.py`
— 在 Revit 內按一個按鈕，依 MCP Protocol V1.1 一鍵建立 MEP 採購標準明細表（Pipes / Ducts / Cable Trays / Conduits）。Python 直接呼叫 `Autodesk.Revit.DB` API + `pyrevit.revit/forms`。

## 為什麼獨立在 root（不是 `scripts/`、不是 `MCP/`）

| 候選位置 | 為什麼不適合 |
|---------|------------|
| `scripts/` | 是 Add-in **安裝/部署 PowerShell/Bash 腳本**（build、deploy、port release）。pyRevit pushbutton 是「Revit 內 user-facing 按鈕」，執行環境與用戶完全不同 |
| `MCP/` | 是 **C# .NET Revit Add-in 源碼**（單一 `RevitMCP.csproj` 多版本 build）。pyRevit 用 Python+IronPython，技術棧不一致 |
| **root**（現狀）| pyRevit 載入 extension 用「user folder symlink → 此目錄」的標準慣例。`.extension/.tab/.panel/.pushbutton` 是 pyRevit 強制目錄結構，搬位置會破壞 pyRevit 發現邏輯 |

## 與 MCP 主系統的關係

- **非整合於 89 個 MCP tools**：本目錄的功能 *不是* 透過 `MCP-Server/src/tools/` 開放給 AI Client 的 tool。Claude / Gemini 看不到、也叫不到它
- **備援/參考實現**：當環境無法跑 MCP（如離線、Revit add-in 未部署），設計師仍可用此按鈕完成 MEP 採購明細表
- **被 domain 文件引用**：`domain/dependent-view-crop-workflow.md` 提到「AI 可參考此程式碼邏輯，引導使用者自行建立 Python 腳本」

## 部署方式

pyRevit 標準作法（使用者一次性設定）：

1. 安裝 pyRevit（<https://github.com/eirannejad/pyRevit>）
2. pyRevit CLI：`pyrevit extend ui MCP_Tools <絕對路徑>/pyRevit_Tools/MCP_Tools.extension`
3. 重啟 Revit，會看到「MCP_Schedules」tab 與「建立標準明細表」按鈕

## 維護

- 新增更多 pushbutton：依 pyRevit `.tab/.panel/.pushbutton/script.py` 慣例擴充
- 若功能成熟到值得整合進 MCP tools，再決定是否包成 `mcp__revit-mcp__create_mep_schedule` 並寫對應 C# command
