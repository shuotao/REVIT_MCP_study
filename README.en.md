# Revit MCP - AI-Powered Revit Control

English | [繁體中文](README.md)

Revit MCP lets AI clients call Autodesk Revit tools through the Model Context Protocol (MCP). The MCP server forwards tool calls to a local Revit add-in, and the add-in executes the corresponding Revit API workflow.

- Demo video: [Revit MCP - AI-Powered BIM Workflow Demonstration](https://youtu.be/YpAYF-GxrhA)
- Knowledge site: <https://shuotao.github.io/REVIT_MCP_study/>
- Default WebSocket port: `8964`

## Current Project Counts

| Item | Count | Source |
|---|---:|---|
| Runtime MCP tools | 99 | `registerRevitTools()` in `MCP-Server/src/tools/index.ts` |
| Domain SOP files | 49 | `domain/*.md` except `README.md`, plus `domain/references/*.md` |
| Claude skills | 24 | `.claude/skills/*/SKILL.md` |

When these numbers change, update `CLAUDE.md`, `README.md`, this file, `docs/DOCUMENT_AUDIENCE_INVENTORY.md`, and run:

```powershell
.\scripts\verify-qaqc.ps1 -SkipBuild -SkipDeploy
```

## Architecture

```text
AI Client
  Claude Desktop / Claude Code / Gemini CLI / VS Code Copilot / Antigravity
        |
        | stdio
        v
MCP Server
  Node.js / TypeScript
  MCP-Server/build/index.js
        |
        | WebSocket ws://localhost:8964
        v
Revit Add-in
  C# / Revit API
  MCP/Application.cs
  MCP/Core/SocketService.cs
  MCP/Core/ExternalEventManager.cs
        |
        v
Autodesk Revit
```

External AI clients do not need an API key inside this repository. Their account and authorization are managed by the AI client itself. Only an embedded Revit chat feature that directly calls an AI API would need an API key.

## Requirements

| Item | Requirement |
|---|---|
| OS | Windows 10 or later |
| Revit | Autodesk Revit 2022, 2023, 2024, 2025, 2026 |
| .NET | .NET Framework 4.8 for Revit 2022-2024; .NET 8 for Revit 2025-2026 |
| Node.js | LTS, preferably 20.x or later |

## One-Click Setup

Recommended for new users:

```powershell
.\scripts\setup.ps1
```

For AI agents or non-interactive setup:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/setup.ps1 -NonInteractive -RevitVersions "2024,2025"
```

The setup script checks prerequisites, installs dependencies, builds the MCP server, builds and deploys the Revit add-in, and helps configure common AI clients.

## Manual Setup

### 1. Build the MCP Server

```powershell
cd MCP-Server
npm install
npm run build
```

AI clients launch:

```text
node MCP-Server/build/index.js
```

### 2. Build the Revit Add-in

Choose the configuration that matches your Revit version:

```powershell
cd MCP
dotnet build -c Release.R22 RevitMCP.csproj   # Revit 2022
dotnet build -c Release.R23 RevitMCP.csproj   # Revit 2023
dotnet build -c Release.R24 RevitMCP.csproj   # Revit 2024
dotnet build -c Release.R25 RevitMCP.csproj   # Revit 2025
dotnet build -c Release.R26 RevitMCP.csproj   # Revit 2026
```

Expected output:

```text
MCP/bin/Release.R{YY}/RevitMCP.dll
```

Example for Revit 2024:

```text
MCP/bin/Release.R24/RevitMCP.dll
```

### 3. Deploy the Add-in

Recommended:

```powershell
.\scripts\install-addon.ps1
```

For manual deployment, place the `.addin` file and DLL under the matching Revit Addins directory, and keep the relative assembly path in `RevitMCP.addin`:

```xml
<Assembly>RevitMCP\RevitMCP.dll</Assembly>
```

Do not create version-specific `.addin` files, and do not hardcode absolute DLL paths.

## AI Client Configuration

Project-level `.mcp.json`:

```json
{
  "mcpServers": {
    "revit-mcp": {
      "type": "stdio",
      "command": "node",
      "args": ["./MCP-Server/build/index.js"],
      "env": {}
    }
  }
}
```

VS Code config in `.vscode/mcp.json`:

```json
{
  "servers": {
    "revit-mcp": {
      "type": "stdio",
      "command": "node",
      "args": ["${workspaceFolder}/MCP-Server/build/index.js"],
      "env": {}
    }
  }
}
```

Other AI clients use the same concept: launch `MCP-Server/build/index.js` with `node`.

Config template per client:

| AI Client | Config location | Template |
|---|---|---|
| Claude Code | project root `.mcp.json` | built in, works out of the box |
| Claude Desktop | `%APPDATA%\Claude\claude_desktop_config.json` | `MCP-Server/claude_desktop_config.json` |
| Gemini CLI | `~/.gemini/settings.json` | `MCP-Server/gemini_mcp_config.json` |
| VS Code Copilot | `.vscode/mcp.json` | built in |
| Antigravity | UI settings | `Antigravity_MCP_Complete_Guide.md` |

Replace `<YOUR_PROJECT_PATH>` in the templates with the actual project path on your machine.

### Switching Between AI Clients

The Revit-side WebSocket service accepts only one MCP connection at a time: a newly connected MCP server replaces the previous connection. Multiple AI clients are therefore used by switching, not concurrently:

1. Close the current AI client (or disable its MCP server).
2. Start the other AI client; once its MCP server connects to `localhost:8964`, it takes over.
3. If the connection misbehaves, restart the MCP service from the Revit ribbon to reset it.

## Startup Flow

1. Start Revit.
2. Open or create a Revit project.
3. Enable the MCP service from the Revit ribbon.
4. Confirm the Revit add-in is listening on `localhost:8964`.
5. Start or restart the AI client so it loads the MCP server.
6. Call Revit MCP tools from the AI client.

If `localhost:8964` is unreachable, Revit may not be running, the MCP service may be off, the port may be occupied, or the AI client and Revit add-in may be using different port settings.

## Project Structure

```text
REVIT_MCP/
  MCP/                         Revit Add-in (C#)
    Application.cs             Revit add-in entry point
    RevitMCP.csproj            Single multi-version project
    RevitMCP.addin             Single add-in manifest
    Core/
      SocketService.cs         Revit-side WebSocket server
      ExternalEventManager.cs  UI-thread execution bridge
      RevitCompatibility.cs    Revit 2022-2026 compatibility helpers
      CommandExecutor.cs       Main command dispatcher
      Commands/*.cs            Command modules
  MCP-Server/                  MCP Server (Node.js / TypeScript)
    src/index.ts               stdio MCP server entry
    src/socket.ts              WebSocket client to Revit
    src/tools/*.ts             MCP tool definitions
  domain/                      Shared BIM SOPs; do not convert to English-only
  .claude/                     AI commands and skills
  docs/                        Human-facing docs and public knowledge site
  scripts/                     Setup, deployment, QA/QC scripts
  log/                         Append-only session and commit logs
```

## AI Docs and Human Docs

| Type | Location | Rule |
|---|---|---|
| AI-only | `CLAUDE.md`, `.claude/commands/`, `.claude/skills/` | English-first to avoid mojibake |
| Human-facing | `README.md`, `README.en.md`, `docs/`, `scripts/README.md` | Match the reader's language |
| Shared | `domain/*.md`, `log/README.md` | Domain files must remain Chinese-readable and must not become English-only |
| Historical | `docs/_archive/**`, old logs | Preserve by default |

See [docs/DOCUMENT_AUDIENCE_INVENTORY.md](./docs/DOCUMENT_AUDIENCE_INVENTORY.md).

## Domain, Skill, and Tool Responsibilities

- `domain/*.md`: BIM SOPs, regulatory logic, and calculation methods. Shared by humans and AI.
- `.claude/skills/*/SKILL.md`: AI workflow orchestration.
- `MCP-Server/src/tools/*.ts`: MCP tool definitions and input schemas.
- `MCP/Core/Commands/*.cs`: Revit API implementation.

If a Domain file and a Skill disagree on method, the Domain file wins.

## QA/QC

After documentation, tool, Domain, Skill, build, or deployment changes, run:

```powershell
.\scripts\verify-qaqc.ps1 -SkipBuild -SkipDeploy
```

Before deployment, run a full check:

```powershell
.\scripts\verify-qaqc.ps1 -Version 2024
```

QA/QC checks:

- forbidden legacy files and paths
- required file structure
- README / CLAUDE / docs count alignment
- Domain table forward and reverse coverage
- local Markdown link rot
- Domain frontmatter
- document audience classification
- mojibake risk in canonical docs

## Troubleshooting

### AI cannot find Revit tools

Check:

1. `npm run build` has been run in `MCP-Server`.
2. The AI client's MCP config points to the correct `MCP-Server/build/index.js`.
3. The AI client has been restarted or has reloaded MCP servers.

### MCP Server cannot connect to Revit

Check:

1. Revit is running.
2. The MCP service is enabled in the Revit ribbon.
3. `localhost:8964` is not occupied.
4. If HTTP.sys / PID 4 is holding the port, try:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\release-port.ps1
```

### Revit does not show the MCP Tools panel

Confirm the `.addin` file and DLL were deployed under the matching `%APPDATA%\Autodesk\Revit\Addins\{version}` directory, then restart Revit.

## Important Rules

- Keep one `MCP/RevitMCP.csproj`.
- Keep one `MCP/RevitMCP.addin`.
- Do not create version-specific `.csproj` or `.addin` files.
- Do not create nested `MCP/MCP/` directories.
- Do not change `.addin` `<Assembly>` to an absolute path.
- Do not convert Domain files to English-only.
- Do not bypass the MCP server with hand-written WebSocket JSON.
- For live Revit view, level, selection, or document state, AI must query live state in the current turn.

## Document Navigation

| Document | Purpose |
|---|---|
| [CLAUDE.md](./CLAUDE.md) | Main AI agent constitution and project map |
| [AGENTS.md](./AGENTS.md) | Redirect to `CLAUDE.md` |
| [GEMINI.md](./GEMINI.md) | Redirect to `CLAUDE.md` |
| [README.md](./README.md) | Traditional Chinese README |
| [CONTRIBUTING.md](./CONTRIBUTING.md) | Contribution guide |
| [CHANGELOG.md](./CHANGELOG.md) | Release history |
| [domain/README.md](./domain/README.md) | Domain SOP catalog |
| [domain/lessons.md](./domain/lessons.md) | Project lessons |
| [.claude/skills/](./.claude/skills/) | AI skills |
| [.claude/commands/](./.claude/commands/) | AI slash commands |
| [scripts/README.md](./scripts/README.md) | Script documentation |
| [docs/DOCUMENT_AUDIENCE_INVENTORY.md](./docs/DOCUMENT_AUDIENCE_INVENTORY.md) | Document audience inventory |
| [docs/DOCS_STRUCTURE.md](./docs/DOCS_STRUCTURE.md) | Docs directory guide |
| [log/README.md](./log/README.md) | Log append rules |

## License

MIT License
