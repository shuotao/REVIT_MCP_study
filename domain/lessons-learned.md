# 🔬 Smart Refinement (Lessons Learned)

This document records high-level rules and experiences gained from successful collaborations.

### [L-001] Corridor Identification Strategy
- **Rule**: Queries for area functions in Revit should be language-tolerant.
- **Practice**: Filtering rooms should include `走廊`, `Corridor`, `廊道`, `通道`, `廊下` (Japanese).

### [L-002] dimension Placement Principles
- **Rule**: Creating a `Dimension` must attach to the host element's center geometry and match the correct "View ID".
- **Coordinate Transformation**:
    - Get the element's `BoundingBox`.
    - Dimension lines should be defined at the center `(max + min) / 2` to ensure text doesn't overlap boundaries.
    - **Warning**: Never create floor dimensions directly in 3D views; query `ActiveView` first.

### [L-003] MCP Connection Diagnosis
- **Rule**: When standard MCP tools fail, check Port settings and use scripts for verification.
- **Practice**:
    - Use `netstat -ano | findstr :11111` to confirm Revit Add-in is alive.
    - Use Node.js scripts to call `RevitSocketClient` directly, bypassing the MCP layer for testing.
    - Verify `ProjectName` returns correctly before fixing configuration files.

### [L-004] Revit 元件連接與方向性原則 (2026-02-22)
- **避坑經驗**：在自動化管線連接時，手動計算座標並執行 `ConnectTo`，配合手動 `flipFacing()` 容易觸發 Revit 報錯：「管線已修改成反方向，導致接頭無效」。
- **高階規則**：優先使用基於接點的放置方法 `doc.Create.NewFamilyInstance(Connector, FamilySymbol)`。
    *   **優點**：Revit 會自動處理族群的翻轉與向量對齊，確保 `c1` 等預設接點直接與管末端精確耦合，大幅降低幾何衝突。
- **參數匹配**：族群尺寸應優先匹配 `Nominal Diameter` (名義直徑)，其次才是 `Outer Diameter` (外徑)。

### [L-005] Node.js 執行環境相容性
- **規則**：在混和 ESM/CJS 的專案中，撰寫工具調用腳本時優先選用 `.cjs` 擴展名。
- **實踐**：這可以確保 `require('@modelcontextprotocol/sdk/client/index.js')` 等傳統模組加載方式在 Node.js 高版本環境下無需額外配置即可運行。
