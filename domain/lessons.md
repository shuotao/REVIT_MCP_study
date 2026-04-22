---
name: lessons
description: "Lessons Learned：由 /lessons 指令自動維護的專案避坑經驗集。記錄高階開發規則與實作教訓，採 Append-only 追加、禁止修改或刪除已有條目。當使用者提到 lessons、開發經驗、避坑、經驗、教訓時觸發。"
metadata:
  version: "1.1"
  updated: "2026-04-22"
  created: "2026-03-13"
  contributors:
    - "Admin"
    - "shuotao"
    - "unknown"
  references: []  # TODO: 月小聚補法規條號或外部依據
  related: []  # TODO: 月小聚補相關 domain（檔名）
  referenced_by:
    - auto-dimension
    - element-query
    - fire-safety-check
  tags: [lessons, 開發經驗, 避坑, 經驗, 教訓, append-only]
---

# Lessons Learned

> 此檔案由 `/lessons` 指令自動維護，記錄專案特定的高階開發規則與避坑經驗。
> 規則以 Append 方式追加，嚴禁修改或刪除已有條目。

---

## [L-001] 走廊識別策略

- **規則**：Revit 中的區域功能查詢應具備語言容錯性。
- **實踐**：篩選房間應包含 `走廊`, `Corridor`, `廊道`, `通道`, `廊下`（日文）。

## [L-002] 自動尺寸標註定位原則

- **規則**：建立 `Dimension` 必須依附於宿主元素的中心幾何，且必須匹配正確的「視圖 ID」。
- **座標轉換**：
  - 取得元素的 `BoundingBox`。
  - 標註位置線應定義在 `(max + min) / 2` 的中心軌跡上，以確保標註文字不與邊界牆重疊。
  - **警告**：嚴禁在 3D 視圖中直接建立平面標註，必須先查詢 `ActiveView`。

## [L-003] Revit 增益集部署與 AddInId 衝突排除

- **問題現象**：Revit 啟動時發生「無法初始化增益集，因為應用程式已存在此 AddInId 節點」錯誤。
- **原因分析**：
  - 歷史遺留問題：專案曾使用手動命名的 `.addin` 檔（如 `RevitMCP.2024.addin`），後改用 SDK 自動生成的 `RevitMCP.addin`。
  - 兩者指向不同的 DLL 路徑但使用相同的 GUID，導致 Revit 衝突。
- **避坑規則**：
  - 全版本統一使用 `RevitMCP.addin` 作為入口名稱。
  - 執行部署腳本或 `dotnet build` 前，應確保環境中無重複的 `.addin`。
  - **專案結構**：DLL 必須統一放置於 `Addins\{version}\RevitMCP\` 子資料夾內，避免與根目錄的舊版檔混淆。
  - **版本相容**：Revit 2022-2023 的 `Category` 缺乏 `.BuiltInCategory` 屬性，必須使用 `GetBuiltInCategoryCompat()` 擴充方法。
  - **DeployAddin 必須關閉**：Nice3point SDK 的 `<DeployAddin>true</DeployAddin>` 會在 build 時自動產生 `RevitMCP.{version}.addin`，與手動的 `RevitMCP.addin` 衝突。csproj 中必須設為 `false`。
  - **setup.ps1 自動清理**：部署步驟內建 `Get-ChildItem -Filter "RevitMCP.*.addin"` 清理邏輯，防止殘黨累積。

## [L-004] setup.ps1 PowerShell 5.1 相容性

- **問題現象**：`setup.ps1` 在 Windows PowerShell 5.1 下多處報錯。
- **根因與修復**：
  - `Join-Path` 只接受 2 個參數（PS 5.1），三段以上路徑需巢狀呼叫 `Join-Path (Join-Path a b) c`。
  - `-split` 單一值回傳字串非陣列，`Set-StrictMode` 下無 `.Count`，需用 `@()` 包裹。
  - 空 `PSCustomObject` 的 `.PSObject.Properties.Name` 在 StrictMode 下報錯，改用 `.PSObject.Properties.Match('key').Count`。
- **避坑規則**：所有 PowerShell 腳本必須在 5.1 下測試，不可假設 7.x 語法可用。

## [L-005] 走廊寬度標註需使用邊界線段而非 BoundingBox

- **問題現象**：用 `create_dimension` 的 BoundingBox 座標標註走廊寬度，得到的是外接矩形尺寸（7.29m），非實際淨寬。
- **根因**：L 型或不規則走廊的 BoundingBox 包含大量空白區域。
- **解法**：新增 `create_corridor_dimension` 命令，使用 Room BoundarySegments 的 Segment-First 演算法找平行牆對，在精確的牆面位置建立標註。
- **實測驗證**：L5 走廊 9 個區段，寬度 516mm–3045mm，兩處不合格（< 1200mm）。

## [L-009] WebSocket 大型數據處理與分片拼接機制

- **避坑經驗**：在 Revit MCP Add-in 中，隨附的 SocketService.cs 預設緩衝區（如 4096 bytes）若不具備拼接邏輯，將導致大型 JSON 指令（如 100+ 條詳圖線 ≈ 50KB+）在傳輸時被截斷，造成 JSON 解析靜默失敗。
- **規則**：
  - **接收端 (C#)**：必須使用 MemoryStream 並循環讀取 WebSocket.ReceiveAsync 直到 result.EndOfMessage 為真。
  - **緩衝區優化**：對於 BIM 數據傳輸，建議將接收緩衝區基礎大小提升至 64KB (65536 bytes) 以減少 frame 讀取次數。

## [L-010] 批次寫入的「順序執行 (Sequential Async)」原則

- **避坑經驗**：一次性向 WebSocket 送出數十個寫入指令（如 rename_element）時，若不等待回應直接關閉或繼續發送，容易發生指令遺失或 Revit 處理衝突。
- **實踐**：應在腳本中實作 sendCommand 包裝函式，利用 Promise 等待單一指令的 RequestId 回傳後，再執行下一個動作。

## [L-011] Revit 名稱正規化 (Normalization) 策略

- **規則**：Revit 中的人為命名（圖紙名稱、類型名稱）常包含不可控的符號與空格。
- **比對實踐**：
  - 統一將全形英數轉為半形。
  - 移除所有括號、減號、空格與常用修飾詞。
  - 優先提取數位部分進行 ID 比對，若 ID 無法辨識則改用正規化後的名稱進行 includes 模糊比對。

## [L-012] Revit 元件空間座標提取策略

- **避坑經驗**：Revit MCP 內建的 query_elements 預設僅回傳參數字串，缺乏幾何座標。對於需要「排序」或「對齊」的工具，這將導致邏輯失效。
- **實踐**：在 C# 核心端擴充 get_element_location 指令，判斷 Location 屬性（Point 或 Curve）並 fallback 到 BoundingBox.Center。

## [L-013] 自動化寫入時的「靜默處理 (Silent Failure Handling)」

- **避坑經驗**：修改「群組 (Group)」內元件的參數時，Revit 會強制彈出警告對話框，中斷自動化流程。
- **實踐**：在 Transaction 中套用 IFailuresPreprocessor（如 DismissWarningsPreprocessor），自動關閉警告，確保腳本能在無人值守情況下完成批次變更。

## [L-014] MCP 寫入工具的並行限制與大 payload 拆分

- **規則**：同時修改 Revit 狀態的 MCP 工具（colorize_clashes、export_clash_report、create_*、override_*）**不可並行呼叫**；回傳大物件的工具不可鏈式 pipe 給下一個工具——中間必須落盤或縮量重跑。
- **避坑經驗**：
  1. `colorize_clashes` + `export_clash_report` 一次送兩個 MCP call 時，兩個都 timeout——皆競爭 `ExternalEventManager` 的 UI thread single-threaded slot。序列化呼叫後雙雙 PASS。
  2. `detect_clashes` 全量 1000 筆結果 937KB，超過 tool output token 限制；而且即使拿到，也無法 inline 當 `clashData` 參數傳給下游（payload > 10KB 時 `format=both` 會 timeout，拆 `format=csv` 單跑 5 筆才通）。
- **實踐**：
  - **寫入類工具永遠序列化**：`await tool_A; then tool_B`，不要塞進同一個 parallel block。讀取類（`get_*` / `query_*`）可安全並行。
  - **大結果鏈式分析時**：第一次跑 `detect_clashes maxResults=1000` 取統計總覽 → 分析後**重跑小 maxResults 或窄 csaSource.categories**（例如只 `["Columns"]`）拿到可 inline 的 ~5KB 物件 → 再 pipe 給 colorize / export。
  - **payload 臨界點**：單一 MCP 工具的 input JSON **> 10KB 就降格**（format=csv 而非 both、clashes 陣列 ≤ 10 筆）。
- **警告**：Revit API 的 UI thread 限制是**結構性**的，不是 bug——MCP-Server 不會替你排隊，client 側必須自律序列化。
