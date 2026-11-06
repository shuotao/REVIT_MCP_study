#Requires -Version 5.1
[CmdletBinding()]param([Parameter(Mandatory=$true)][string]$Package,[string]$OutputFolder='')
. (Join-Path $PSScriptRoot 'Abby.Common.ps1')
$repo=Split-Path -Parent (Split-Path -Parent $PSScriptRoot);$Package=[IO.Path]::GetFullPath($Package);if(!$OutputFolder){$OutputFolder=Join-Path $repo 'output\releases'}
if(!(Test-Path -LiteralPath ($Package+'.sha256'))){throw 'Release ZIP and checksum required'}
$stage=Join-Path ([IO.Path]::GetTempPath()) ('AbbyInstallKit-'+[guid]::NewGuid().ToString('N'));New-Item -ItemType Directory -Path $stage|Out-Null
foreach($name in @('Update-Abby.ps1','Abby.Common.ps1','Bootstrap.Common.ps1','start-mcp.cjs','Install-ThisPC.ps1','channel.json','Set-AbbyUpdateSchedule.ps1','Abby-Set-Hourly.cmd')){Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination $stage}
Copy-Item -LiteralPath $Package -Destination $stage;Copy-Item -LiteralPath ($Package+'.sha256') -Destination $stage
$install="@echo off`r`npowershell.exe -NoProfile -ExecutionPolicy Bypass -File `"%~dp0Install-ThisPC.ps1`"`r`nset `"abbyExit=%errorlevel%`"`r`npause`r`nexit /b %abbyExit%`r`n";[IO.File]::WriteAllText((Join-Path $stage 'Abby-Install.cmd'),$install,[Text.Encoding]::ASCII)
$update="@echo off`r`npowershell.exe -NoProfile -ExecutionPolicy Bypass -File `"%~dp0Update-Abby.ps1`" %*`r`nset `"abbyExit=%errorlevel%`"`r`npause`r`nexit /b %abbyExit%`r`n";[IO.File]::WriteAllText((Join-Path $stage 'Abby-Update.cmd'),$update,[Text.Encoding]::ASCII)
Copy-Item -LiteralPath (Join-Path $repo 'docs\ABBY-TWO-PC-SETUP.md') -Destination (Join-Path $stage 'README.md')
$baseName=[IO.Path]::GetFileNameWithoutExtension($Package);if(!$baseName.StartsWith('Abby-MCP-')){throw 'Unexpected package name'}
New-Item -ItemType Directory -Path $OutputFolder -Force|Out-Null;$zip=Join-Path $OutputFolder ('Abby-OneClick-Setup-'+$baseName.Substring(9)+'.zip');if(Test-Path -LiteralPath $zip){throw 'Existing install kits are never overwritten'}
Add-Type -AssemblyName System.IO.Compression.FileSystem;[IO.Compression.ZipFile]::CreateFromDirectory($stage,$zip)
[IO.File]::WriteAllText($zip+'.sha256',((Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash+'  '+[IO.Path]::GetFileName($zip)))
Write-Host "Install kit: $zip"
$hourlyStage=Join-Path ([IO.Path]::GetTempPath()) ('AbbyHourly-'+[guid]::NewGuid().ToString('N'));New-Item -ItemType Directory -Path $hourlyStage|Out-Null
foreach($name in @('Set-AbbyUpdateSchedule.ps1','Abby-Set-Hourly.cmd')){Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination $hourlyStage}
[IO.File]::WriteAllText((Join-Path $hourlyStage 'README.txt'),'Extract all files and run Abby-Set-Hourly.cmd on each installed PC. Approve Windows elevation using the same account if requested. Only the Abby updater trigger changes to every1hour. No installation, model operations or add-in deployment. Completion must show Interval=PT1H and Minutes=60.')
$hourlyZip=Join-Path $OutputFolder ('Abby-Update-Hourly-'+$baseName.Substring(9)+'.zip');if(Test-Path -LiteralPath $hourlyZip){throw 'Existing schedule utilities are never overwritten'}
[IO.Compression.ZipFile]::CreateFromDirectory($hourlyStage,$hourlyZip)
[IO.File]::WriteAllText($hourlyZip+'.sha256',((Get-FileHash -LiteralPath $hourlyZip -Algorithm SHA256).Hash+'  '+[IO.Path]::GetFileName($hourlyZip)))
Write-Host "Hourly schedule utility: $hourlyZip"
