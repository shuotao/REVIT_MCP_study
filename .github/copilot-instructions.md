# Copilot Instructions

This project uses a unified guidance file. Please read and follow **CLAUDE.md** in the project root for all instructions, architecture, build commands, deployment rules, and domain knowledge references.

Key points:
- Build: `cd MCP && dotnet build -c Release.R{YY} RevitMCP.csproj` (YY = 22/23/24/25/26)
- MCP Server: `cd MCP-Server && npm install && npm run build`
- Deploy: `scripts/install-addon.ps1`
- Port: 8964 (WebSocket)
- Skills: `.claude/skills/*/SKILL.md` (Agent Skills open standard)
- Domain: `domain/*.md` (BIM compliance workflows)
- DO NOT create version-specific .csproj or .addin files
- DO NOT write raw WebSocket scripts to talk to Revit — use the existing MCP tools (`MCP-Server/src/tools/*.ts`)
- DO NOT bypass the MCP Server tool chain — use Skills (`.claude/skills/`) and Domain (`domain/`) for BIM workflows
