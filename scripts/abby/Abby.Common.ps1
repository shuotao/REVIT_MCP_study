#Requires -Version 5.1
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
function Get-AbbyChannel {return (Read-AbbyJson (Join-Path $PSScriptRoot 'channel.json'))}
function Write-AbbyJson($Path,$Value){$parent=Split-Path -Parent $Path;if(!(Test-Path -LiteralPath $parent)){New-Item -ItemType Directory -Path $parent -Force|Out-Null};[IO.File]::WriteAllText($Path,(ConvertTo-Json -InputObject $Value -Depth 12),(New-Object Text.UTF8Encoding($false)))}
function Read-AbbyJson($Path){return ([IO.File]::ReadAllText($Path,[Text.Encoding]::UTF8)|ConvertFrom-Json)}
function Invoke-AbbyNative($Program,[string[]]$Arguments){& $Program @Arguments;if($LASTEXITCODE -ne 0){throw "$Program failed ($LASTEXITCODE)"}}
function Test-AbbyRepo($Repo){if($Repo -notmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$'){throw 'Use GitHub owner/repository, without a token or URL.'}}
function Get-AbbyChild($Root,$Relative){
 if([IO.Path]::IsPathRooted($Relative)-or $Relative.Contains(':')-or $Relative -match '(^|[\\/])\.\.([\\/]|$)'){throw "Unsafe package path: $Relative"}
 $base=[IO.Path]::GetFullPath($Root).TrimEnd('\','/')+'\';$p=[IO.Path]::GetFullPath((Join-Path $Root $Relative));if(!$p.StartsWith($base,[StringComparison]::OrdinalIgnoreCase)){throw 'Path escapes package root'};return $p
}
function Get-AbbyTybim {
 $roots=@((Join-Path $env:APPDATA 'Autodesk\Revit\Addins'),(Join-Path $env:PROGRAMDATA 'Autodesk\Revit\Addins'))
 $files=@(foreach($root in $roots){if(Test-Path -LiteralPath $root){foreach($year in Get-ChildItem -LiteralPath $root -Directory){foreach($entry in Get-ChildItem -LiteralPath $year.FullName){if($entry.Name -match '(?i)TY.?BIM'){if($entry.PSIsContainer){Get-ChildItem -LiteralPath $entry.FullName -File -Recurse}else{$entry}}}}}})
 return @($files|Sort-Object FullName -Unique|ForEach-Object{[pscustomobject]@{Path=$_.FullName;Hash=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash}})
}
function Get-AbbyRunningRevit {
 $result=@();foreach($p in @(Get-Process Revit -ErrorAction SilentlyContinue)){try{$exe=$p.Path}catch{throw 'Cannot inspect running Revit. Update deferred.'};if(!$exe){throw 'Cannot identify Revit version. Update deferred.'};if($exe -match '(?i)\\Revit (2024|2025|2026)\\'){$result+=$Matches[1]}};return $result
}
function Get-AbbyOtherAddins {
 $roots=@((Join-Path $env:APPDATA 'Autodesk\Revit\Addins'),(Join-Path $env:PROGRAMDATA 'Autodesk\Revit\Addins'))
 $inventory=@(foreach($root in $roots){
  if(!(Test-Path -LiteralPath $root)){continue}
  foreach($year in Get-ChildItem -LiteralPath $root -Directory){
   foreach($entry in Get-ChildItem -LiteralPath $year.FullName){
    if($entry.Name -in @('RevitMCP','RevitMCP.addin')){continue}
    $files=if($entry.PSIsContainer){@(Get-ChildItem -LiteralPath $entry.FullName -Recurse -File)}else{@($entry)}
    foreach($file in $files){[pscustomobject]@{Path=$file.FullName;Hash=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash}}
   }
  }
 })
 return @($inventory|Sort-Object Path)
}
function Test-AbbyPayload($Root,[switch]$AllowInstalledDependencies){
 $manifest=Read-AbbyJson (Join-Path $Root 'manifest.json');if($manifest.Format -ne 1 -or $manifest.Version -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]{0,79}$'){throw 'Unsupported package manifest'}
 $seen=@{};foreach($file in $manifest.Files){$key=$file.Path.ToLowerInvariant();if($seen.ContainsKey($key)){throw 'Duplicate manifest path'};$seen[$key]=$true;$p=Get-AbbyChild $Root $file.Path;if(!(Test-Path -LiteralPath $p -PathType Leaf)){throw "Missing payload: $($file.Path)"};if((Get-FileHash -LiteralPath $p -Algorithm SHA256).Hash -ne $file.Sha256){throw "Payload hash mismatch: $($file.Path)"}}
 foreach($p in @(Get-ChildItem -LiteralPath $Root -File -Recurse)){if($p.FullName -eq (Join-Path $Root 'manifest.json')){continue};$rel=$p.FullName.Substring($Root.TrimEnd('\').Length+1).Replace('\','/').ToLowerInvariant();if($AllowInstalledDependencies -and $rel.StartsWith('server/node_modules/')){continue};if(!$seen.ContainsKey($rel)){throw "Unlisted payload file: $rel"}}
 foreach($v in @('2024','2025','2026')){if(!(Test-Path -LiteralPath (Join-Path $Root "addin\$v\RevitMCP.dll"))){throw "Missing Revit $v build"}}
 return $manifest
}
