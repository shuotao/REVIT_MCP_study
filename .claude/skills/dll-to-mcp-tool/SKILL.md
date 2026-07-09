---
name: dll-to-mcp-tool
description: Wrap a standalone Revit IExternalCommand (.cs / DLL, WPF dialog allowed) into a revit-mcp MCP tool. Generates the C# command handler + switch case + TypeScript tool definition, builds, dual-path deploys, guides the restart, auto-verifies the tool is live, then commits (never pushes). Use when the user says "把這支 DLL/.cs 變成 MCP 工具", "封裝成 revit-mcp tool", "wrap this command as an MCP tool".
user-invocable: true
---

# DLL → revit-mcp tool

Convert a standalone Revit add-in command (single `.cs`, `IExternalCommand`, may contain a WPF dialog) into a revit-mcp MCP tool, following the established two-layer architecture.

This skill targets **Tier 0** (current architecture): every tool is compiled into `RevitMCP.dll`, so a C# change requires the user to restart Revit once. The skill automates everything except the Revit restart, and **auto-verifies** the tool is live so the user never has to guess.

## Core concept (explain to the user if they are non-expert)

A revit-mcp tool is **two layers**, not one DLL:
- **TS layer** (`MCP-Server/`) = the "waiter": defines the tool name/params, listens to the AI.
- **C# layer** (`MCP/`, compiled into `RevitMCP.dll`) = the "chef": does the real Revit work.

A WPF dialog in the source does **not** become a tool — it **disappears** and each field it collected becomes a **tool parameter**. The AI drives the tool headless; nobody clicks the dialog.

- One self-contained action → **one tool** with parameters.
- Several independently-useful steps → **split into multiple tools**.
- An *interactive* dialog (rubber-band selection, live preview, human-in-the-loop tuning) → **flag as "not recommended to convert"**; ask before forcing it.

## Inputs

A `.cs` file path (or pasted code) of a standalone `IExternalCommand`. Default assumptions: Revit 2026, C#, target `Release.R26`.

## Repo paths (REVIT_MCP_study)

| What | Path |
|---|---|
| C# command handlers (partials) | `MCP/Core/Commands/CommandExecutor.<Name>.cs` |
| C# dispatch switch | `MCP/Core/CommandExecutor.cs` |
| TS tool modules | `MCP-Server/src/tools/<area>-tools.ts` |
| TS registry (profiles) | `MCP-Server/src/tools/index.ts` (generic bridge in `revit-tools.ts`, no edit needed) |
| C# build config | `Release.R26` → output `MCP/bin/Release.R26/RevitMCP.dll` |
| Deploy targets (BOTH) | `%APPDATA%\Autodesk\Revit\Addins\2026\RevitMCP\` **and** `C:\ProgramData\Autodesk\Revit\Addins\2026\RevitMCP\` |

## Procedure

### 1. Read & analyze
Read the `.cs`. Separate the **WPF dialog** (the inputs it collects) from the **core logic** (the Revit work). List every dialog field and what it feeds.

### 2. Propose tool split + param mapping — ASK
Use `AskUserQuestion` to confirm:
- **How many tools** (one action vs several independent ones).
- **Dialog field → tool parameter** mapping table. Drop pure UI helpers (select-all, shift-range, OK button). The result the dialog showed → structured JSON return value.
- If an **interactive** dialog is detected, flag "not recommended" and ask whether to force a headless version.

### 3. Confirm tool name + description — ASK
snake_case name(s), one-line Chinese description per tool, and inputSchema (params, required vs optional, defaults).

### 4. Generate C# handler
Create `MCP/Core/Commands/CommandExecutor.<Name>.cs`. Template:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;

#if REVIT2025_OR_GREATER
using IdType = System.Int64;
#else
using IdType = System.Int32;
#endif

namespace RevitMCP.Core
{
    public partial class CommandExecutor
    {
        #region <Name>

        private object <Name>(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;
            // ... ported core logic; read params via parameters["x"]?.Value<T>() / as JArray
            // wrap model edits in: using (Transaction trans = new Transaction(doc, "...")) { trans.Start(); ... trans.Commit(); }
            // use FilteredElementCollector + native ElementFilter (not LINQ Where for the heavy filter)
            // use Id.GetIdValue() for cross-version ElementId values
            return new { Success = true, /* structured stats that the dialog used to show */ };
        }

        #endregion
    }
}
```

Porting rules:
- Strip `IExternalCommand`, `Execute(...)`, `commandData`, all WPF/`Window`/`TaskDialog`/`MessageBox` code.
- Levels/selection that came from the dialog → read from `parameters`. Empty/omitted list = sensible "all" default where it makes sense.
- Replace `Result.Succeeded/Cancelled` with a returned anonymous object. Throw `new Exception("...")` on hard errors (the dispatcher wraps it into `{Success:false, Error}`).
- Follow `domain/*.md` if the task matches a domain trigger (see repo CLAUDE.md).

### 5. Wire C# dispatch
In `MCP/Core/CommandExecutor.cs`, add inside the `switch`:

```csharp
                    case "<tool_name>":
                        result = <Name>(parameters);
                        break;
```

### 6. Generate TS tool
Add a `Tool` object to the most relevant `MCP-Server/src/tools/<area>-tools.ts` array (the bridge in `revit-tools.ts` maps `toolName → commandName` 1:1 automatically — no edit there). Match the existing inline style:

```ts
    {
        name: "<tool_name>",
        description: "<一句話中文描述>",
        inputSchema: {
            type: "object",
            properties: { /* params */ },
            // required: [...] only if truly required
        },
    },
```
If the tool belongs to a brand-new module file, also import + add it to the relevant profile arrays in `MCP-Server/src/tools/index.ts`.

### 7. Build both
- TS: `cd MCP-Server; npm run build` (must finish with no `tsc` errors).
- C#: `cd MCP; dotnet build -c Release.R26 RevitMCP.csproj` (must report **0 errors**; nullable warnings are fine). Confirm new `name: "<tool_name>"` is present in `MCP-Server/build/.../<area>-tools.js`.

### 8. Dual-path deploy (handle the Revit lock)
The active `RevitMCP.dll` (loaded from the **APPDATA** path) is **locked while Revit runs**. So:
1. Check `Get-Process Revit`. If running, tell the user to **save & close Revit**, then continue.
2. Copy `MCP/bin/Release.R26/RevitMCP.dll` to **both**:
   - `%APPDATA%\Autodesk\Revit\Addins\2026\RevitMCP\RevitMCP.dll`
   - `C:\ProgramData\Autodesk\Revit\Addins\2026\RevitMCP\RevitMCP.dll`
   (dual-path is required — otherwise an old copy can load first.)
3. Verify both copies have the new size + timestamp.

### 9. Restart guidance (Tier 0 — user does this)
Tell the user, in order:
1. **Reopen Revit** → open the model → **enable the MCP service** in the ribbon.
2. In Claude Code, run **`/mcp`** → **reconnect** `revit-mcp` (this restarts only the node subprocess and reloads the tool list — **no full Claude Code restart needed**, and the current conversation is preserved).

### 10. Auto-verify (don't make the user guess)
After the user is back, verify the tool is live: load its schema with `ToolSearch` (`select:mcp__revit-mcp__<tool_name>`) or list revit-mcp tools, and report:
- ✅ `<tool_name>` is online → proceed to live test.
- ❌ not found → the reconnect likely didn't take; ask the user to run `/mcp` reconnect again (or restart Claude Code).

### 11. Live test
Call the new tool on the active project with a real argument. First re-anchor active state (`get_project_info` / `get_all_levels` / `get_active_view`) per repo CLAUDE.md, then call the tool and read the structured result back to the user.

### 12. Commit — only after live test passes (NEVER push)
Per the user's standing rule (one tool = one commit) and confirmed preference:
- **Only after the live test confirms the tool works.**
- Stage **only this tool's files** (the new C# partial, `CommandExecutor.cs`, the TS tool file, and any count-sync docs from step 13). **Never `git add -A`.**
- Commit on the **current branch** (the user's backup model is push-fork-main).
- Commit message ends with the co-author trailer:

  ```
  feat: add <tool_name> MCP tool (wrapped from <source>)

  Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
  ```
- Show the commit hash + the `git reset --soft HEAD~1` undo command.
- **Do NOT push.** Pushing to the fork is always the user's explicit, separate action.

### 13. Housekeeping — REMIND, don't auto-churn
After commit, remind (and offer to do, but don't silently do):
- Tool count bump in `CLAUDE.md` / `README*.md` / `docs/DOCUMENT_AUDIENCE_INVENTORY.md` (the repo tracks "Runtime MCP tools").
- Run `scripts/verify-qaqc.ps1 -SkipBuild -SkipDeploy`.

## Guardrails
- **Never push.** Never `git push`, never open a PR. Local commit only.
- **Never auto-restart Revit** — only the user closes/opens Revit.
- **Never `git add -A`** — scope staging to this tool's files.
- Interactive WPF dialogs (live preview / human-in-the-loop) → flag as not-recommended and ask before forcing a headless version.
- Respect repo CLAUDE.md: snake_case tool names, Transaction-wrapped reversible edits, native `FilteredElementCollector` filters, `domain/*.md` methods win for compliance tasks.
