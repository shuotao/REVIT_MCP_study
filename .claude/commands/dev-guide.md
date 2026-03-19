# /dev-guide — 開發前導指引

你正在為 Revit MCP 開發新工具或修改現有功能。在寫任何程式碼之前，請依序完成以下準備。

## Step 1：讀取踩坑經驗

讀取 `domain/lessons.md`，確認已知的開發陷阱。特別注意：
- L-001：查詢房間/走廊時的多語言容錯
- L-002：尺寸標註必須匹配視圖 ID，禁止在 3D 視圖建立平面標註

如果你要開發的功能涉及上述場景，必須在實作中納入這些規則。

## Step 2：確認工具能力邊界

讀取 `domain/tool-capability-boundary.md`，確認你要實作的功能是否在 MCP 工具的能力範圍內。如果超出範圍，應告知使用者限制而非強行實作。

## Step 3：確認跨版本相容

- 使用 `IdType`（不是 `int` 或 `long`）處理 ElementId
- 使用 `.GetIdValue()`（不是 `.IntegerValue` 或 `.Value`）取得 ElementId 數值
- 檔案頂部已有 `#if REVIT2025_OR_GREATER` 的 using alias，直接使用即可

## Step 4：確認架構規則

- C# 新工具放在 `MCP/Core/Commands/CommandExecutor.{Module}.cs`（partial class）
- TypeScript 工具定義放在 `MCP-Server/src/tools/{module}-tools.ts`
- 在 `CommandExecutor.cs` 的 switch 加入 case
- 在 `tools/index.ts` 的 profile 對照表加入模組
- 不要建立 version-specific 的 `.csproj` 或 `.addin`

## Step 5：開始開發

以上確認完畢後，開始寫程式碼。開發完成後用 `/qaqc` 做品質驗證。
