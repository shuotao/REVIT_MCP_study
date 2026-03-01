$settingsPath = "$env:USERPROFILE\.gemini\settings.json"
$targetDir = Split-Path $settingsPath -Parent
if (-not (Test-Path $targetDir)) { New-Item -ItemType Directory -Path $targetDir -Force }

$currentSettings = @{}
if (Test-Path $settingsPath) {
    try {
        $content = Get-Content $settingsPath -Raw
        if (-not [string]::IsNullOrWhiteSpace($content)) {
            $currentSettings = $content | ConvertFrom-Json
        }
    }
    catch {
        Write-Host "Warning: Could not parse existing settings. overwriting."
    }
}

# Ensure mcpServers object exists
if (-not $currentSettings.PSObject.Properties['mcpServers']) {
    $currentSettings | Add-Member -Name "mcpServers" -Value @{} -MemberType NoteProperty
}

$revitMcpConfig = @{
    command = "node"
    args    = @("d:\David\BIM MCP\REVIT_MCP_study\MCP-Server\build\index.js")
    env     = @{ REVIT_VERSION = "2024" }
}

# Add or Update revit-mcp
# Note: In PowerShell, updating a property on a PSCustomObject can be done by simply assigning or using Add-Member -Force
try {
    $currentSettings.mcpServers."revit-mcp" = $revitMcpConfig
}
catch {
    $currentSettings.mcpServers | Add-Member -Name "revit-mcp" -Value $revitMcpConfig -MemberType NoteProperty -Force
}

$currentSettings | ConvertTo-Json -Depth 10 | Set-Content $settingsPath -Encoding UTF8
Write-Host "Settings updated successfully at $settingsPath"
