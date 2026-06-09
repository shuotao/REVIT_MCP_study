# /status - Local Project Status

Show the current local project status.

## Command

```powershell
.\scripts\status.ps1
```

For machine-readable output:

```powershell
.\scripts\status.ps1 -Json
```

## What It Reports

- Current timestamp.
- Project root.
- Current assistant/model label when known from the active system context.
- Usage windows:
  - 5-hour usage: `unavailable`
  - 7-day usage: `unavailable`
  - total usage: `unavailable`
- Git branch and dirty-file count.
- Revit MCP port and whether the port is open locally.
- Runtime MCP tool count.
- Domain SOP file count.
- Skill count.
- QA/QC commands to run next.

## Usage Limitation

The repository cannot read account-level AI usage windows. If the AI client exposes 5-hour, 7-day, or total usage in its UI or API, read it there. This command keeps those fields visible but marks them unavailable when the data is not exposed locally.

## Response Style

When the user asks for status, run the script and summarize the output. Do not invent usage numbers.
