#Requires -Version 5.1
$ErrorActionPreference='Stop'
$packages=@(Get-ChildItem -LiteralPath $PSScriptRoot -Filter 'Abby-MCP-*.zip' -File)
if($packages.Count -ne 1){throw 'Keep exactly one Abby-MCP release ZIP beside this installer.'}
$repo='s7814322-ops/REVIT_MCP_study'
$reply=Read-Host 'Check the my-revit-tool release channel automatically every10minutes? (Y/N)';$auto=$reply -match '^[Yy]$'
& (Join-Path $PSScriptRoot 'Update-Abby.ps1') -Package $packages[0].FullName -Repo $repo -EnableAutoUpdate:$auto
