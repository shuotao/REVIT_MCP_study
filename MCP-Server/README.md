# @shuotao/revit-mcp-server

AI-powered control of **Autodesk Revit** through the [Model Context Protocol](https://modelcontextprotocol.io/). This npm package is the **Node.js stdio MCP server** — the bridge that lets an AI client (Claude, etc.) drive Revit with natural-language tool calls.

> Published on the MCP Registry as `io.github.shuotao/revit-mcp-server`.

## What it is

```
AI client (Claude Desktop / Claude Code / …)
  → stdio →  this package (@shuotao/revit-mcp-server)
  → WebSocket (localhost:8964) →  Revit add-in (C#)  →  Revit API
```

It exposes 160+ MCP tools for BIM workflows: element queries, curtain-wall elevations, quantity take-off, room/parking numbering, IFC structural sync, code-compliance checks, and more.

## ⚠️ Requires the Revit add-in

This package is only the stdio bridge. It does nothing on its own — the **C# Revit add-in must be installed separately** and running inside Revit (it opens the WebSocket server on `localhost:8964`).

👉 **Full install (add-in build + deploy, per Revit version) and documentation:**
https://github.com/shuotao/REVIT_MCP_study

## Usage

Run directly:

```bash
npx -y @shuotao/revit-mcp-server
```

Or wire it into an MCP client config:

```json
{
  "mcpServers": {
    "revit-mcp": {
      "command": "npx",
      "args": ["-y", "@shuotao/revit-mcp-server"]
    }
  }
}
```

Optional env: `REVIT_MCP_PORT` (default `8964`, must match the add-in), `MCP_PROFILE` (`full` | `architect` | `mep` | `structural` | `fire-safety`).

## Requirements

- Node.js 18+
- Autodesk Revit 2022–2026 with the Revit MCP add-in installed and its service enabled

## Links

- Repository & full docs: https://github.com/shuotao/REVIT_MCP_study
- Issues: https://github.com/shuotao/REVIT_MCP_study/issues

## License

MIT
