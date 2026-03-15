# AI Assistant Deployment Guide

This guide details how to deploy the Revit MCP project for different environments.

## 📋 Environment Detection Protocol

When a user requests help with deployment, follow this sequence:

### 1. Collect Environment Information
Ask for:
- **Revit Version** (2022, 2023, 2024, etc.)
- **AI Client** (Claude Desktop, Gemini CLI, VS Code, etc.)
- **Project Location** (Absolute path)

### 2. Environment Matrix

| Revit Version | .csproj | DLL Output Path | Addins Path | Warnings |
|:----------|:--------|:------------|:-----------|:------|
| 2022 | `RevitMCP.csproj` | `bin\Release\` | `Addins\2022` | 0 |
| 2023 | `RevitMCP.csproj` | `bin\Release\` | `Addins\2023` | 0 |
| 2024 | `RevitMCP.2024.csproj` | `bin\Release.2024\` | `Addins\2024` | 56 (Normal) |

---

## 🚀 Deployment Instructions

### For Revit 2024 + Gemini CLI (Antigravity)

```powershell
# 1. Build C# Add-in
cd "ProjectRoot\MCP"
dotnet build -c Release RevitMCP.2024.csproj

# 2. Run Install Script
cd ..
.\scripts\install-addon-bom.ps1
# Choose: 2024

# 3. Build MCP Server
cd MCP-Server
npm install
npm run build

# 4. Configure settings.json
# Path: ~/.gemini/settings.json
{
  "mcpServers": {
    "revit-mcp": {
      "command": "node",
      "args": ["ABSOLUTE_PATH\\MCP-Server\\build\\index.js"]
    }
  }
}
```

### For Claude Desktop

Config file: `~/AppData/Roaming/Claude/config.json`

---

## 🔍 Troubleshooting

- **56 Warnings in 2024**: This is normal due to Revit 2022 compatibility code.
- **DLL Not Found**: Check if you built the correct `.csproj` for your Revit version.
- **Connection Failed**: Ensure WebSocket Port 11111 (Revit side) and Port 8964 (MCP Server) are not blocked.
