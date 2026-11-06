#Requires -Version 5.1
[CmdletBinding()]param([string]$InstallRoot=(Join-Path $env:LOCALAPPDATA 'AbbyMCP'),[string]$ExpectedUserSid='', [switch]$PlanOnly)
$ErrorActionPreference='Stop'
[Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12
. (Join-Path $PSScriptRoot 'Abby.Common.ps1')
. (Join-Path $PSScriptRoot 'Bootstrap.Common.ps1')
if($PlanOnly){[pscustomobject]@{InstallRoot=$InstallRoot;Node='Private Node.js22 if needed';Python='Private Python3.13 + ezdxf';Desktop='Official Microsoft Store app';Cad='ODA converter if missing';AutoUpdate='Every1hour';ClientConfig='Add only mcp_servers.abby';Models='Never opened';OtherAddins='SHA256 verified'}|ConvertTo-Json;return}
$sid=[Security.Principal.WindowsIdentity]::GetCurrent().User.Value
if($ExpectedUserSid -and $ExpectedUserSid -ne $sid){throw 'Elevation used a different Windows account. Run with the intended user account; no installation performed.'}
$principal=New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if(!$principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){
 $arguments='-NoProfile -ExecutionPolicy Bypass -File "'+$PSCommandPath+'" -InstallRoot "'+$InstallRoot+'" -ExpectedUserSid "'+$sid+'"'
 # This is an interactive installer; preserve visible progress after the Windows consent prompt.
 $p=Start-Process -FilePath (Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe') -Verb RunAs -ArgumentList $arguments -PassThru -Wait -WindowStyle Normal
 exit $p.ExitCode
}
if(!$env:PROCESSOR_ARCHITECTURE.Equals('AMD64',[StringComparison]::OrdinalIgnoreCase)){throw 'This Revit installer requires Windows x64'}
if(@(Get-AbbyRunningRevit).Count){throw 'Save and close Revit before installation; no processes will be terminated'}
$versions=@('2024','2025','2026'|Where-Object{Test-Path -LiteralPath (Join-Path $env:PROGRAMFILES "Autodesk\Revit $_\Revit.exe")})
if(!$versions.Count){throw 'Install a licensed Revit2024/2025/2026 first'}
$packages=@(Get-ChildItem -LiteralPath $PSScriptRoot -Filter 'Abby-MCP-*.zip' -File)
if($packages.Count -ne 1){throw 'Keep exactly one Abby-MCP release ZIP beside this installer'}
$otherBefore=@(Get-AbbyOtherAddins);$tybimBefore=@(Get-AbbyTybim)
New-Item -ItemType Directory -Path $InstallRoot -Force|Out-Null
$log=Join-Path $InstallRoot ('install-'+(Get-Date -Format yyyyMMdd-HHmmss)+'.log')
Start-Transcript -LiteralPath $log|Out-Null
try{
 Write-Host '[1/5] Preparing isolated Node.js'
 $node=Install-AbbyNode $InstallRoot;$env:PATH=(Split-Path -Parent $node)+';'+$env:PATH
 & (Join-Path $PSScriptRoot 'Update-Abby.ps1') -Package $packages[0].FullName -InstallRoot $InstallRoot -ValidateOnly
 if($LASTEXITCODE){throw 'Abby package validation failed'}
 Write-Host '[2/5] Preparing isolated Python and CAD reader'
 Install-AbbyPython $InstallRoot|Out-Host
 Write-Host '[3/5] Installing official desktop client and ODA if missing'
 Install-AbbyDesktopAndOda
 Write-Host '[4/5] Installing Abby and enabling automatic updates'
 $loopback=Initialize-AbbyLoopback;Write-Host $loopback
 & (Join-Path $PSScriptRoot 'Update-Abby.ps1') -Package $packages[0].FullName -InstallRoot $InstallRoot -EnableAutoUpdate -NonInteractive
 if($LASTEXITCODE){throw 'Abby update failed'}
 if(Test-Path -LiteralPath (Join-Path $InstallRoot 'pending.json')){throw 'Revit was started; deployment deferred. Close it and run this installer again.'}
 Write-Host '[5/5] Configuring only the Abby MCP entry'
 Set-AbbyClientConfig (Join-Path $env:USERPROFILE '.codex\config.toml') $node $InstallRoot
 $otherAfter=@(Get-AbbyOtherAddins);$tybimAfter=@(Get-AbbyTybim)
 if(($otherBefore|ConvertTo-Json -Depth 5 -Compress)-ne ($otherAfter|ConvertTo-Json -Depth 5 -Compress)){throw 'Other add-in inventory changed during installation'}
 if(($tybimBefore|ConvertTo-Json -Depth 5 -Compress)-ne ($tybimAfter|ConvertTo-Json -Depth 5 -Compress)){throw 'TYBIM inventory changed during installation'}
 $state=Read-AbbyJson (Join-Path $InstallRoot 'current.json')
 Write-AbbyJson (Join-Path $InstallRoot 'installation.json') @{Version=$state.Version;ToolCount=$state.ToolCount;Versions=$versions;AutoUpdate=$true;OtherAddinsUnchanged=$true;TybimUnchanged=$true;DesktopSignInRequired=$true;Log=$log}
 Write-Host "Installation complete: $($state.ToolCount) tools. Sign in to the desktop client, start the Abby service in Revit, then reload MCP. Other add-ins were unchanged."
}finally{Stop-Transcript|Out-Null}
