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
