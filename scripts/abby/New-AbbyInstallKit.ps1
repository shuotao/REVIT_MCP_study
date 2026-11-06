#Requires -Version 5.1
[CmdletBinding()]param([Parameter(Mandatory=$true)][string]$Package,[string]$OutputFolder='')
. (Join-Path $PSScriptRoot 'Abby.Common.ps1')
$repo=Split-Path -Parent (Split-Path -Parent $PSScriptRoot);$Package=[IO.Path]::GetFullPath($Package);if(!$OutputFolder){$OutputFolder=Join-Path $repo 'output\releases'}
if(!(Test-Path -LiteralPath ($Package+'.sha256'))){throw 'Release ZIP and checksum required'}
$stage=Join-Path ([IO.Path]::GetTempPath()) ('AbbyInstallKit-'+[guid]::NewGuid().ToString('N'));New-Item -ItemType Directory -Path $stage|Out-Null
foreach($name in @('Update-Abby.ps1','Abby.Common.ps1','start-mcp.cjs','Install-ThisPC.ps1','channel.json')){Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination $stage}
Copy-Item -LiteralPath $Package -Destination $stage;Copy-Item -LiteralPath ($Package+'.sha256') -Destination $stage
$install="@echo off`r`npowershell.exe -NoProfile -ExecutionPolicy Bypass -File `"%~dp0Install-ThisPC.ps1`"`r`nset `"abbyExit=%errorlevel%`"`r`npause`r`nexit /b %abbyExit%`r`n";[IO.File]::WriteAllText((Join-Path $stage 'Abby-Install.cmd'),$install,[Text.Encoding]::ASCII)
$update="@echo off`r`npowershell.exe -NoProfile -ExecutionPolicy Bypass -File `"%~dp0Update-Abby.ps1`" %*`r`nset `"abbyExit=%errorlevel%`"`r`npause`r`nexit /b %abbyExit%`r`n";[IO.File]::WriteAllText((Join-Path $stage 'Abby-Update.cmd'),$update,[Text.Encoding]::ASCII)
Copy-Item -LiteralPath (Join-Path $repo 'docs\ABBY-TWO-PC-SETUP.md') -Destination (Join-Path $stage 'README.md')
New-Item -ItemType Directory -Path $OutputFolder -Force|Out-Null;$zip=Join-Path $OutputFolder ('Abby-Second-PC-'+[IO.Path]::GetFileNameWithoutExtension($Package)+'.zip');if(Test-Path -LiteralPath $zip){throw 'Existing install kits are never overwritten'}
Add-Type -AssemblyName System.IO.Compression.FileSystem;[IO.Compression.ZipFile]::CreateFromDirectory($stage,$zip);Write-Host "Install kit: $zip"
