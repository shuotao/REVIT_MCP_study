$ErrorActionPreference = "Stop"

$appData = $env:APPDATA
$targetDir = "$appData\Autodesk\Revit\Addins\2024"

if (-not (Test-Path $targetDir)) {
    Write-Host "Creating directory: $targetDir"
    New-Item -ItemType Directory -Path $targetDir -Force | Out-Null
}

$oldAddin = "$targetDir\revit-mcp-plugin.addin"
if (Test-Path $oldAddin) {
    Write-Host "Backing up old addin: $oldAddin"
    Move-Item -Path $oldAddin -Destination "$oldAddin.bak" -Force
}

$newAddinPath = "$targetDir\RevitMCP.addin"
$dllPath = "c:\D disk\MCP\REVIT_MCP_study\MCP\bin\Release.2024\RevitMCP.dll"

$content = @"
<?xml version="1.0" encoding="utf-8"?>
<RevitAddIns>
  <AddIn Type="Application">
    <Name>Revit MCP Plugin (Dev)</Name>
    <Assembly>$dllPath</Assembly>
    <FullClassName>RevitMCP.Application</FullClassName>
    <ClientId>090a4c8c-61dc-426d-87df-e4bae0f80ec1</ClientId>
    <VendorId>revit-mcp</VendorId>
    <VendorDescription>MCP Connection for Revit (Local Build)</VendorDescription>
  </AddIn>
</RevitAddIns>
"@

Write-Host "Writing new addin file verification to: $newAddinPath"
Set-Content -Path $newAddinPath -Value $content -Encoding UTF8

Write-Host "Done. Please restart Revit."
