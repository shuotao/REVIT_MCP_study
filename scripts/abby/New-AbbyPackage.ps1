#Requires -Version 5.1
[CmdletBinding()]param([string]$Version=('abby-my-revit-tool-'+(Get-Date -Format yyyyMMdd-HHmmss)),[switch]$SkipBuild,[string]$OutputFolder='')
. (Join-Path $PSScriptRoot 'Abby.Common.ps1')
$repo=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if($Version -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]{0,79}$'){throw 'Invalid release version'}
if(!$OutputFolder){$OutputFolder=Join-Path $repo 'output\releases'}
if(!$SkipBuild){Push-Location (Join-Path $repo 'MCP-Server');try{Invoke-AbbyNative 'npm.cmd' @('ci','--ignore-scripts');Invoke-AbbyNative 'npm.cmd' @('run','build')}finally{Pop-Location};foreach($year in @('24','25','26')){Invoke-AbbyNative 'dotnet' @('build',(Join-Path $repo 'MCP\RevitMCP.csproj'),'-c',"Release.R$year",'-v:q')}}
$stage=Join-Path ([IO.Path]::GetTempPath()) ('AbbyPackage-'+[guid]::NewGuid().ToString('N'));New-Item -ItemType Directory -Path $stage|Out-Null
foreach($year in @('2024','2025','2026')){$build=Join-Path $repo ('MCP\bin\Release.R'+$year.Substring(2));if(!(Test-Path -LiteralPath (Join-Path $build 'RevitMCP.dll'))){throw "Build $year first"};$dest=Join-Path $stage "addin\$year";New-Item -ItemType Directory -Path $dest -Force|Out-Null;Get-ChildItem -LiteralPath $build -File|Where-Object{$_.Extension -eq '.dll' -or $_.Name -match '^RevitMCP\.(deps|runtimeconfig)\.json$'}|Copy-Item -Destination $dest}
Copy-Item -LiteralPath (Join-Path $repo 'MCP\RevitMCP.addin') -Destination (Join-Path $stage 'addin\RevitMCP.addin')
New-Item -ItemType Directory -Path (Join-Path $stage 'server')|Out-Null
foreach($name in @('build','package.json','package-lock.json')){Copy-Item -LiteralPath (Join-Path $repo "MCP-Server\$name") -Destination (Join-Path $stage 'server') -Recurse}
New-Item -ItemType Directory -Path (Join-Path $stage 'worker')|Out-Null;Copy-Item -LiteralPath (Join-Path $repo 'bridge\python\skills\ezdxf_worker.py') -Destination (Join-Path $stage 'worker\ezdxf_worker.py')
New-Item -ItemType Directory -Path (Join-Path $stage 'knowledge\.agents') -Force|Out-Null
Copy-Item -LiteralPath (Join-Path $repo '.claude\skills') -Destination (Join-Path $stage 'knowledge\.agents\skills') -Recurse
Copy-Item -LiteralPath (Join-Path $repo 'domain') -Destination (Join-Path $stage 'knowledge\domain') -Recurse
Copy-Item -LiteralPath (Join-Path $repo 'CLAUDE.md') -Destination (Join-Path $stage 'knowledge\CLAUDE.md')
New-Item -ItemType Directory -Path (Join-Path $stage 'client')|Out-Null;foreach($name in @('Update-Abby.ps1','Abby.Common.ps1','start-mcp.cjs','channel.json')){Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination (Join-Path $stage 'client')}
Invoke-AbbyNative node @((Join-Path $PSScriptRoot 'registry-snapshot.mjs'),(Join-Path $stage 'tools.json'));$inventory=Read-AbbyJson (Join-Path $stage 'tools.json')
$sha=(& git -C $repo rev-parse HEAD);if($LASTEXITCODE -ne 0){throw 'Source commit unavailable'};$dirty=(@(& git -C $repo status --porcelain).Count -gt 0)
$files=@(Get-ChildItem -LiteralPath $stage -File -Recurse|ForEach-Object{[pscustomobject]@{Path=$_.FullName.Substring($stage.Length+1).Replace('\','/');Sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash}})
$channel=Get-AbbyChannel
Write-AbbyJson (Join-Path $stage 'manifest.json') ([ordered]@{Format=1;Version=$Version;SourceCommit=$sha;SourceBranch=$channel.Branch;SourceRepository=$channel.Repository;SourceDirty=$dirty;ToolCount=$inventory.ToolCount;CreatedUtc=[DateTime]::UtcNow.ToString('o');Versions=@('2024','2025','2026');Files=$files})
$null=Test-AbbyPayload $stage
New-Item -ItemType Directory -Path $OutputFolder -Force|Out-Null;$zip=Join-Path $OutputFolder ('Abby-MCP-'+$Version+'.zip');if(Test-Path -LiteralPath $zip){throw 'Existing release packages are never overwritten'}
Add-Type -AssemblyName System.IO.Compression.FileSystem;[IO.Compression.ZipFile]::CreateFromDirectory($stage,$zip)
[IO.File]::WriteAllText($zip+'.sha256',((Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash+'  '+[IO.Path]::GetFileName($zip)))
Write-Host "Package ready: $zip";Write-Host "SourceDirty=$dirty; staging retained for verification: $stage"
