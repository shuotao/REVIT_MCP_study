---
name: deploy-addon
description: Deploy the built RevitMCP.dll to the correct Revit Add-ins folder for a specified version (Windows only)
user-invocable: true
---

Deploy `RevitMCP.dll` to the Revit Add-ins directory for the selected version.

## Platform Check

First, detect the OS. If **not Windows**, do not attempt deployment. Instead, show:

```
⚠️  This skill deploys to Windows only.
    Run the following command on your Windows machine:

    $version = "2024"  # change to your Revit version
    Copy-Item "MCP\bin\Release\RevitMCP.dll" `
      "$env:APPDATA\Autodesk\Revit\Addins\$version\RevitMCP\" -Force
```

Then stop.

## Usage

- **No args** → Show numbered version menu (2022-2026), ask user to choose
- **`--version {year}`** → Deploy directly to that version's directory

## Target Paths

| Version | Target Directory |
|---------|-----------------|
| 2022    | `%APPDATA%\Autodesk\Revit\Addins\2022\RevitMCP\` |
| 2023    | `%APPDATA%\Autodesk\Revit\Addins\2023\RevitMCP\` |
| 2024    | `%APPDATA%\Autodesk\Revit\Addins\2024\RevitMCP\` |
| 2025    | `%APPDATA%\Autodesk\Revit\Addins\2025\RevitMCP\` |
| 2026    | `%APPDATA%\Autodesk\Revit\Addins\2026\RevitMCP\` |

## Steps

1. **Check source DLL**: Verify `MCP/bin/Release/RevitMCP.dll` exists.
   - If missing → tell user to run `/build-revit --version {version}` first, then stop.

2. **Warn before copy**: Display:
   ```
   ⚠️  Close Revit before continuing.
       The DLL cannot be overwritten while Revit is running.
   ```
   Ask: `Ready to deploy? (y/n)` — stop if user says no.

3. **Create target directory** if it doesn't exist:
   ```powershell
   New-Item -ItemType Directory -Force -Path "$env:APPDATA\Autodesk\Revit\Addins\{version}\RevitMCP\"
   ```

4. **Copy DLL**:
   ```powershell
   Copy-Item "MCP\bin\Release\RevitMCP.dll" "$env:APPDATA\Autodesk\Revit\Addins\{version}\RevitMCP\" -Force
   ```

5. **Verify**: Confirm the file exists at the target path and show its timestamp.

6. **Report success**:
   ```
   ✅ Deployed to %APPDATA%\Autodesk\Revit\Addins\{version}\RevitMCP\RevitMCP.dll
      → Restart Revit to load the updated add-in.
   ```

## Error Handling

| Error | Response |
|-------|----------|
| Source DLL missing | Direct user to `/build-revit --version {version}` |
| Target directory creation failed | Suggest running terminal as Administrator |
| Copy failed (access denied) | Check if Revit is still running; suggest closing it or running as Administrator |
| Verification fails after copy | Show full error and suggest manual copy command |
