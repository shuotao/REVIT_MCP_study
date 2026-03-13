---
name: build-revit
description: Build the RevitMCP Revit add-in for one or all Revit versions (2022-2026) using the unified RevitMCP.csproj
user-invocable: true
---

Build the RevitMCP Revit add-in using the unified `RevitMCP.csproj` and `Nice3point.Revit.Sdk`.

## Version Matrix

| Version | Build Config | .NET        |
|---------|--------------|-------------|
| 2022    | Release.R22  | Framework 4.8 |
| 2023    | Release.R23  | Framework 4.8 |
| 2024    | Release.R24  | Framework 4.8 |
| 2025    | Release.R25  | .NET 8       |
| 2026    | Release.R26  | .NET 8       |

## Usage

- **No args** → Display the version matrix above as a numbered menu, ask user to choose
- **`--version {year}`** → Build that specific version directly (e.g. `--version 2024`)
- **`--all`** → Build all 5 versions sequentially, report pass/fail for each
- **`--list`** → Print the version matrix and exit

## Steps

1. Locate `MCP/RevitMCP.csproj` by searching from the current directory upward
2. `cd` into the `MCP/` directory
3. Run: `dotnet build -c Release.R{YY} RevitMCP.csproj` where `{YY}` = last 2 digits (e.g. 2024 → R24, 2022 → R22)
4. After build, check that `MCP/bin/Release/RevitMCP.dll` was created or updated
5. Report: ✅ success with DLL file size and timestamp, then suggest running `/deploy-addon --version {version}` as next step

## When Using `--all`

Build all versions in order: 2022 → 2023 → 2024 → 2025 → 2026.
Continue even if one fails (do not stop on failure).
Show a summary table at the end:

```
✅ 2022 (Release.R22) — succeeded
✅ 2023 (Release.R23) — succeeded
✅ 2024 (Release.R24) — succeeded
❌ 2025 (Release.R25) — FAILED
✅ 2026 (Release.R26) — succeeded
```

## Error Handling

| Error | Response |
|-------|----------|
| `RevitMCP.csproj` not found | Tell user to run from project root, not a subdirectory |
| `dotnet` not installed | Show: "Install .NET SDK: https://dot.net" |
| Build failed | Show last 30 lines of build output, highlight the error line |
| `bin/Release/RevitMCP.dll` missing after success | Warn user, show full build output |
