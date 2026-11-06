#Requires -Version 5.1
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
. (Join-Path $root 'scripts\abby\Abby.Common.ps1')
. (Join-Path $root 'scripts\abby\Bootstrap.Common.ps1')
$fixture=Join-Path $root ('tmp\bootstrap-test-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixture -Force|Out-Null
$config=Join-Path $fixture 'user\config.toml';$install=Join-Path $fixture 'AbbyMCP'
$other="[mcp_servers.other_plugin]`ncommand = 'existing-command'`nargs = ['preserve-me']`n"
New-Item -ItemType Directory -Path (Split-Path -Parent $config) -Force|Out-Null
[IO.File]::WriteAllText($config,$other)
$node=(Get-Command node).Source
Set-AbbyClientConfig $config $node $install
$text=[IO.File]::ReadAllText($config)
if(!$text.StartsWith($other) -or !$text.Contains((ConvertTo-Json -InputObject (Join-Path $install 'start-mcp.cjs') -Compress))){throw 'Other MCP configuration was not preserved'}
$hash=(Get-FileHash -LiteralPath $config).Hash;Set-AbbyClientConfig $config $node $install
if((Get-FileHash -LiteralPath $config).Hash -ne $hash){throw 'Repeated installation changed an identical configuration'}
[IO.File]::WriteAllText($config,"[mcp_servers.abby]`ncommand = 'another-owner'`n")
$hash=(Get-FileHash -LiteralPath $config).Hash;$rejected=$false
try{Set-AbbyClientConfig $config $node $install}catch{$rejected=$true}
if(!$rejected -or (Get-FileHash -LiteralPath $config).Hash -ne $hash){throw 'Conflicting MCP entry was overwritten'}
Write-Host 'PASS preserve other MCP entries, repeat safely, reject conflicting ownership'
$appData=$env:APPDATA;$programData=$env:PROGRAMDATA
try{
 $env:APPDATA=Join-Path $fixture 'roaming';$env:PROGRAMDATA=Join-Path $fixture 'machine'
 $addins=Join-Path $env:APPDATA 'Autodesk\Revit\Addins\2024'
 foreach($name in @('TYBIM','OtherPlugin','RevitMCP')){New-Item -ItemType Directory -Path (Join-Path $addins $name) -Force|Out-Null;[IO.File]::WriteAllText((Join-Path $addins "$name\example.dll"),'fixture')}
 $before=@(Get-AbbyOtherAddins)
 if($before.Count -ne 2){throw 'Other add-in inventory scope is wrong'}
 [IO.File]::WriteAllText((Join-Path $addins 'RevitMCP\example.dll'),'new Abby')
 if(($before|ConvertTo-Json -Compress)-ne (@(Get-AbbyOtherAddins)|ConvertTo-Json -Compress)){throw 'Abby writes affected other add-in inventory'}
 [IO.File]::WriteAllText((Join-Path $addins 'TYBIM\example.dll'),'changed')
 if(($before|ConvertTo-Json -Compress)-eq (@(Get-AbbyOtherAddins)|ConvertTo-Json -Compress)){throw 'Other add-in alteration was not detected'}
}finally{$env:APPDATA=$appData;$env:PROGRAMDATA=$programData}
Write-Host 'PASS inventory excludes only Abby and detects changes to other plugins'
$plan=& (Join-Path $root 'scripts\abby\Install-ThisPC.ps1') -PlanOnly -InstallRoot $install
if(!(($plan -join '')|ConvertFrom-Json).AutoUpdate){throw 'Installation plan is incomplete'}
Write-Host 'PASS clean-machine plan runs without installing apps, tasks or add-ins'
$global:AbbyNetshCalls=0
function netsh.exe {$global:AbbyNetshCalls++;$global:LASTEXITCODE=0;Write-Output 'Reserved URL: http://localhost:8964/'}
$null=Initialize-AbbyLoopback
if($global:AbbyNetshCalls -ne 1){throw 'An existing loopback reservation was modified'}
function netsh.exe {$global:AbbyNetshCalls++;$global:LASTEXITCODE=2;Write-Output 'Not found'}
function Get-NetTCPConnection {param($LocalPort,$State,$ErrorAction) return [pscustomobject]@{OwningProcess=42}}
$global:AbbyNetshCalls=0;$rejected=$false
try{Initialize-AbbyLoopback|Out-Null}catch{$rejected=$true}
if(!$rejected -or $global:AbbyNetshCalls -ne 1){throw 'An occupied port caused a permission change'}
Write-Host 'PASS existing loopback permissions are preserved and occupied ports stop without changes'
