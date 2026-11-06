#Requires -Version 5.1
$ErrorActionPreference='Stop';$root=Split-Path -Parent $PSScriptRoot
. (Join-Path $root 'scripts\abby\Abby.Common.ps1')
$checks=0
function Assert-Rejected([scriptblock]$Action,$Name){$rejected=$false;try{& $Action|Out-Null}catch{$rejected=$true};if(!$rejected){throw "Expected rejection: $Name"};$script:checks++;Write-Host "PASS $Name"}
foreach($file in Get-ChildItem -LiteralPath (Join-Path $root 'scripts\abby') -Filter *.ps1){$tokens=$null;$errors=$null;$null=[Management.Automation.Language.Parser]::ParseFile($file.FullName,[ref]$tokens,[ref]$errors);if($errors.Count){throw ($errors|Out-String)};$checks++;Write-Host "PASS syntax $($file.Name)"}
$fixture=Join-Path $root ('tmp\abby-update-test-'+[guid]::NewGuid().ToString('N'));New-Item -ItemType Directory -Path $fixture -Force|Out-Null
foreach($p in @('..\escape','../escape','C:\escape','nested\..\escape')){Assert-Rejected {Get-AbbyChild $fixture $p} "reject path $p"}
Assert-Rejected {Test-AbbyRepo 'https://github.com/a/b?token=secret'} 'reject URL/token repository'
foreach($v in @('2024','2025','2026')){New-Item -ItemType Directory -Path (Join-Path $fixture "addin\$v") -Force|Out-Null;[IO.File]::WriteAllText((Join-Path $fixture "addin\$v\RevitMCP.dll"),'fixture')}
$files=@(Get-ChildItem -LiteralPath $fixture -File -Recurse|ForEach-Object{@{Path=$_.FullName.Substring($fixture.Length+1).Replace('\','/');Sha256=(Get-FileHash -LiteralPath $_.FullName).Hash}})
Write-AbbyJson (Join-Path $fixture 'manifest.json') @{Format=1;Version='test-1';Files=$files}
$null=Test-AbbyPayload $fixture;$checks++;Write-Host 'PASS valid payload'
$file=Join-Path $fixture 'addin\2024\RevitMCP.dll';[IO.File]::WriteAllText($file,'tampered');Assert-Rejected {Test-AbbyPayload $fixture} 'reject modified DLL';[IO.File]::WriteAllText($file,'fixture')
[IO.File]::WriteAllText((Join-Path $fixture 'extra.txt'),'not listed');Assert-Rejected {Test-AbbyPayload $fixture} 'reject unlisted files';Remove-Item -LiteralPath (Join-Path $fixture 'extra.txt')
$m=Read-AbbyJson (Join-Path $fixture 'manifest.json');$m.Files=@($m.Files)+@($m.Files[0]);Write-AbbyJson (Join-Path $fixture 'manifest.json') $m;Assert-Rejected {Test-AbbyPayload $fixture} 'reject duplicate manifest paths'
# Exercise the real online-selection path with the non-enumerated array returned by Invoke-RestMethod.
$m.Files=$files;$m|Add-Member SourceBranch 'my-revit-tool';$m|Add-Member SourceRepository 's7814322-ops/REVIT_MCP_study';Write-AbbyJson (Join-Path $fixture 'manifest.json') $m
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip=$fixture+'.zip';[IO.Compression.ZipFile]::CreateFromDirectory($fixture,$zip)
[IO.File]::WriteAllText($zip+'.sha256',((Get-FileHash -LiteralPath $zip).Hash+'  fixture.zip'))
$global:AbbyReleaseFixture=@(
 [pscustomobject]@{tag_name='other-channel';draft=$false;prerelease=$false;published_at='2099-01-01';assets=@()},
 [pscustomobject]@{tag_name='abby-my-revit-tool-draft';draft=$true;prerelease=$false;published_at='2099-01-01';assets=@()},
 [pscustomobject]@{tag_name='abby-my-revit-tool-old';draft=$false;prerelease=$false;published_at='2020-01-01';assets=@()},
 [pscustomobject]@{tag_name='abby-my-revit-tool-current';draft=$false;prerelease=$false;published_at='2021-01-01';assets=@(
  [pscustomobject]@{name='Abby-MCP-fixture.zip';browser_download_url='https://fixture.invalid/current.zip'},
  [pscustomobject]@{name='Abby-MCP-fixture.zip.sha256';browser_download_url='https://fixture.invalid/current.zip.sha256'}
 )}
)
$global:AbbyDownloadFixture=@();$global:AbbyFixtureZip=$zip
function Invoke-RestMethod($Uri,$Headers){return ,$global:AbbyReleaseFixture}
function Invoke-WebRequest($Uri,$Headers,$OutFile,[switch]$UseBasicParsing){$global:AbbyDownloadFixture+=$Uri;if($Uri.EndsWith('.sha256')){Copy-Item -LiteralPath ($global:AbbyFixtureZip+'.sha256') -Destination $OutFile}else{Copy-Item -LiteralPath $global:AbbyFixtureZip -Destination $OutFile}}
& (Join-Path $root 'scripts\abby\Update-Abby.ps1') -ValidateOnly -InstallRoot ($fixture+'-client') -StagingRoot ($fixture+'-staging')
if($global:AbbyDownloadFixture.Count -ne 2 -or @($global:AbbyDownloadFixture|Where-Object{$_ -notlike 'https://fixture.invalid/current.zip*'}).Count){throw 'Wrong release channel or assets selected'}
$checks++;Write-Host 'PASS online release array selects newest stable Abby channel without real network or deployment'
Write-Host "$checks safety checks passed. Fixture retained: $fixture; no add-in/config/task/model writes."
