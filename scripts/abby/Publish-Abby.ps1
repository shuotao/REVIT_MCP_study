#Requires -Version 5.1
[CmdletBinding()]param()
. (Join-Path $PSScriptRoot 'Abby.Common.ps1')
$sourceRoot=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
& (Join-Path $PSScriptRoot 'Sync-AbbySource.ps1') -Action Push -SourceRoot $sourceRoot
Write-Host 'The central branch builds Revit2024/2025/2026 once. Both PCs receive the same stable Abby channel release.'
