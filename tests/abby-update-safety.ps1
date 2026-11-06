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
Write-Host "$checks safety checks passed. Fixture retained: $fixture; no add-in/config/task/model writes."
