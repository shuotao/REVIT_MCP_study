#Requires -Version 5.1
[CmdletBinding()]param([string]$Repo='',[string]$Package='',[string]$InstallRoot=(Join-Path $env:LOCALAPPDATA 'AbbyMCP'),[string]$StagingRoot='',[switch]$EnableAutoUpdate,[switch]$NonInteractive,[switch]$ValidateOnly)
. (Join-Path $PSScriptRoot 'Abby.Common.ps1')
$channel=Get-AbbyChannel
$mutex=New-Object Threading.Mutex($false,'Local\AbbyMCP-Update');if(!$mutex.WaitOne(0)){Write-Host 'Another Abby update is running.';exit 0}
try{
 $privateNode=Join-Path $InstallRoot 'tools\node\node.exe';if(Test-Path -LiteralPath $privateNode){$env:PATH=(Split-Path -Parent $privateNode)+';'+$env:PATH}
 $node=Get-Command node -ErrorAction Stop;$major=[int]((& $node.Source -p 'process.versions.node').Split('.')[0]);if($major -lt 22){throw 'Install Node.js22 or newer first.'}
 $configFile=Join-Path $InstallRoot 'client.json';$config=$null;if(Test-Path -LiteralPath $configFile){$config=Read-AbbyJson $configFile;if(!$Repo){$Repo=$config.Repo}};if(!$Repo){$Repo=$channel.Repository};if($Repo -ne $channel.Repository){throw 'Repository does not match this Abby channel'}
 if(!$StagingRoot){$StagingRoot=Join-Path $InstallRoot 'staging'}
 $temp=Join-Path $StagingRoot ('u-'+[guid]::NewGuid().ToString('N').Substring(0,12));New-Item -ItemType Directory -Path $temp -Force|Out-Null
 if(!$Package){
  Test-AbbyRepo $Repo;[Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12
  $headers=@{'User-Agent'='Abby-MCP-Updater';'Accept'='application/vnd.github+json'}
  $releases=Invoke-RestMethod -Uri "https://api.github.com/repos/$Repo/releases?per_page=100" -Headers $headers
  $release=$releases|Where-Object{!$_.draft -and !$_.prerelease -and $_.tag_name.StartsWith($channel.ReleasePrefix,[StringComparison]::Ordinal)}|Sort-Object published_at -Descending|Select-Object -First 1
  if(!$release){throw 'No stable my-revit-tool release is available yet.'}
  $assets=@($release.assets|Where-Object{$_.name -like 'Abby-MCP-*.zip' -or $_.name -like 'Abby-MCP-*.zip.sha256'})
  if($assets.Count -ne 2){throw 'Release ZIP/checksum assets are incomplete'}
  foreach($asset in $assets){if($asset.name -notmatch '^Abby-MCP-[A-Za-z0-9._-]+\.zip(?:\.sha256)?$'){throw 'Invalid release asset name'};Invoke-WebRequest -UseBasicParsing -Uri $asset.browser_download_url -Headers @{'User-Agent'='Abby-MCP-Updater'} -OutFile (Join-Path $temp $asset.name)}
  $zips=@(Get-ChildItem -LiteralPath $temp -Filter *.zip -File);if($zips.Count -ne 1){throw 'Release must contain exactly one Abby zip'};$Package=$zips[0].FullName
 }
 $Package=[IO.Path]::GetFullPath($Package);$sumFile=$Package+'.sha256';if(!(Test-Path -LiteralPath $sumFile)){throw 'Package SHA256 sidecar required'}
 $sum=([IO.File]::ReadAllText($sumFile).Trim()-split '\s+')[0];if($sum -notmatch '^[A-Fa-f0-9]{64}$' -or (Get-FileHash -LiteralPath $Package -Algorithm SHA256).Hash -ne $sum){throw 'ZIP checksum mismatch'}
 Add-Type -AssemblyName System.IO.Compression.FileSystem;$archive=[IO.Compression.ZipFile]::OpenRead($Package)
 $unpack=Join-Path $temp 'payload';try{foreach($e in $archive.Entries){if(!$e.FullName){continue};$null=Get-AbbyChild $unpack $e.FullName;if((($e.ExternalAttributes -shr 16)-band 61440)-eq 40960){throw 'Symbolic links are not accepted'}}}finally{$archive.Dispose()}
 [IO.Compression.ZipFile]::ExtractToDirectory($Package,$unpack);$manifest=Test-AbbyPayload $unpack
 if(!$manifest.PSObject.Properties['SourceBranch'] -or $manifest.SourceBranch -ne $channel.Branch -or $manifest.SourceRepository -ne $channel.Repository){throw 'Package source does not match my-revit-tool channel'}
 if($ValidateOnly){Write-Host "Verified package $($manifest.Version); no add-ins/configuration modified.";return}
 $root=[IO.Path]::GetFullPath($InstallRoot);New-Item -ItemType Directory -Path $root -Force|Out-Null
 if($Repo){Test-AbbyRepo $Repo;Write-AbbyJson $configFile @{Repo=$Repo}}
 foreach($name in @('Update-Abby.ps1','Abby.Common.ps1','start-mcp.cjs','channel.json')){$source=Join-Path $PSScriptRoot $name;$dest=Join-Path $root $name;if([IO.Path]::GetFullPath($source)-ne [IO.Path]::GetFullPath($dest)){Copy-Item -LiteralPath $source -Destination $dest -Force}}
 if($EnableAutoUpdate){
  if(!$Repo){throw 'Configure the private GitHub repository before enabling automatic updates'}
  $scriptPath=Join-Path $root 'Update-Abby.ps1';$action=New-ScheduledTaskAction -Execute 'powershell.exe' -Argument ('-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File "'+$scriptPath+'" -NonInteractive')
  $trigger=New-ScheduledTaskTrigger -Once -At (Get-Date).AddMinutes(1) -RepetitionInterval (New-TimeSpan -Minutes 10) -RepetitionDuration (New-TimeSpan -Days 3650)
  $principal=New-ScheduledTaskPrincipal -UserId ([Security.Principal.WindowsIdentity]::GetCurrent().Name) -LogonType Interactive -RunLevel Limited
  Register-ScheduledTask -TaskName 'AbbyMCP-AutoUpdate' -Action $action -Trigger $trigger -Principal $principal -Description 'Install stable Abby releases when Revit is closed; no model operations.' -Force|Out-Null
 }
 $running=@(Get-AbbyRunningRevit);if($running.Count -gt 0){Write-AbbyJson (Join-Path $root 'pending.json') @{Version=$manifest.Version;Reason='RevitRunning';Versions=$running};Write-Host ('Update deferred. Close Revit '+($running -join ', ')+'. The current version remains active.');return}
 $versions=@($manifest.Versions|Where-Object{Test-Path -LiteralPath (Join-Path $env:PROGRAMFILES "Autodesk\Revit $_\Revit.exe")});if($versions.Count -eq 0){throw 'No supported Revit2024/2025/2026 installation found'}
 $currentFile=Join-Path $root 'current.json';if(Test-Path -LiteralPath $currentFile){$current=Read-AbbyJson $currentFile;if($current.Version -eq $manifest.Version){$upToDate=$true;foreach($version in $versions){foreach($f in @(Get-ChildItem -LiteralPath (Join-Path $unpack "addin\$version") -File)){$dest=Join-Path $env:APPDATA "Autodesk\Revit\Addins\$version\RevitMCP\$($f.Name)";if(!(Test-Path -LiteralPath $dest)-or (Get-FileHash -LiteralPath $f.FullName).Hash -ne (Get-FileHash -LiteralPath $dest).Hash){$upToDate=$false}}};if($upToDate){Write-Host "Already installed: $($manifest.Version)";return}}}
 $runtime=Get-AbbyChild (Join-Path $root 'releases') $manifest.Version
 if(Test-Path -LiteralPath $runtime){if((Get-FileHash -LiteralPath (Join-Path $runtime 'manifest.json')).Hash -ne (Get-FileHash -LiteralPath (Join-Path $unpack 'manifest.json')).Hash){throw 'An existing version has different contents. Publish a new version; never replace release contents.'};$null=Test-AbbyPayload $runtime -AllowInstalledDependencies}else{New-Item -ItemType Directory -Path (Split-Path -Parent $runtime) -Force|Out-Null;Move-Item -LiteralPath $unpack -Destination $runtime}
 Push-Location (Join-Path $runtime 'server');try{Invoke-AbbyNative 'npm.cmd' @('ci','--omit=dev','--ignore-scripts','--no-audit','--no-fund')}finally{Pop-Location}
 $before=@(Get-AbbyTybim);$otherBefore=@(Get-AbbyOtherAddins);$backup=Join-Path $root ('backups\'+(Get-Date -Format yyyyMMdd-HHmmss)+'-'+$manifest.Version);New-Item -ItemType Directory -Path $backup -Force|Out-Null
 Write-AbbyJson (Join-Path $backup 'tybim.json') $before
 $expectedXml=[xml][IO.File]::ReadAllText((Join-Path $runtime 'addin\RevitMCP.addin'))
 $operations=@();foreach($version in $versions){$base=Join-Path $env:APPDATA "Autodesk\Revit\Addins\$version";$existing=Join-Path $base 'RevitMCP.addin';if(Test-Path -LiteralPath $existing){$xml=[xml][IO.File]::ReadAllText($existing);if([string]$xml.RevitAddIns.AddIn.AddInId -ne [string]$expectedXml.RevitAddIns.AddIn.AddInId -or [string]$xml.RevitAddIns.AddIn.FullClassName -ne [string]$expectedXml.RevitAddIns.AddIn.FullClassName){throw 'RevitMCP.addin belongs to another add-in; preserved'}}elseif(Test-Path -LiteralPath (Join-Path $base 'RevitMCP')){throw 'Existing RevitMCP directory has no identifying manifest; preserved'};foreach($src in @(Get-ChildItem -LiteralPath (Join-Path $runtime "addin\$version") -File)){$dest=Get-AbbyChild $base ('RevitMCP\'+$src.Name);$operations+=[pscustomobject]@{Source=$src.FullName;Dest=$dest;Backup=(Join-Path $backup "$version\$($src.Name)");Existed=(Test-Path -LiteralPath $dest)}};$operations+=[pscustomobject]@{Source=(Join-Path $runtime 'addin\RevitMCP.addin');Dest=(Get-AbbyChild $base 'RevitMCP.addin');Backup=(Join-Path $backup "$version\RevitMCP.addin");Existed=(Test-Path -LiteralPath $existing)}}
 $worker=Join-Path $env:APPDATA 'RevitMCP\ezdxf_worker.py';$operations+=[pscustomobject]@{Source=(Join-Path $runtime 'worker\ezdxf_worker.py');Dest=$worker;Backup=(Join-Path $backup 'ezdxf_worker.py');Existed=(Test-Path -LiteralPath $worker)}
 foreach($op in $operations){if($op.Existed){New-Item -ItemType Directory -Path (Split-Path -Parent $op.Backup) -Force|Out-Null;Copy-Item -LiteralPath $op.Dest -Destination $op.Backup}}
 $done=@();try{
  if(@(Get-AbbyRunningRevit).Count -gt 0){throw 'Revit was started during update; deployment cancelled'}
  foreach($op in $operations){New-Item -ItemType Directory -Path (Split-Path -Parent $op.Dest) -Force|Out-Null;$done+=$op;Copy-Item -LiteralPath $op.Source -Destination $op.Dest -Force;if((Get-FileHash -LiteralPath $op.Source).Hash -ne (Get-FileHash -LiteralPath $op.Dest).Hash){throw "Deployment hash mismatch: $($op.Dest)"}}
  $after=@(Get-AbbyTybim);if(($before|ConvertTo-Json -Depth 5 -Compress)-ne ($after|ConvertTo-Json -Depth 5 -Compress)){throw 'TYBIM inventory changed'}
  $otherAfter=@(Get-AbbyOtherAddins);if(($otherBefore|ConvertTo-Json -Depth 5 -Compress)-ne ($otherAfter|ConvertTo-Json -Depth 5 -Compress)){throw 'Other add-in inventory changed'}
 }catch{foreach($op in $done){if($op.Existed){Copy-Item -LiteralPath $op.Backup -Destination $op.Dest -Force}else{Remove-Item -LiteralPath $op.Dest -Force}};throw}
 foreach($name in @('Update-Abby.ps1','Abby.Common.ps1','start-mcp.cjs','channel.json')){Copy-Item -LiteralPath (Join-Path $runtime "client\$name") -Destination (Join-Path $root $name) -Force}
 $state=@{Version=$manifest.Version;SourceCommit=$manifest.SourceCommit;ToolCount=$manifest.ToolCount;Versions=$versions;InstalledUtc=[DateTime]::UtcNow.ToString('o');Backup=$backup;TybimUnchanged=$true};Write-AbbyJson ($currentFile+'.new') $state;Move-Item -LiteralPath ($currentFile+'.new') -Destination $currentFile -Force
 $launcher=(Join-Path $root 'start-mcp.cjs').Replace('\','/');$nodeCommand=$node.Source.Replace('\','/');$toml="[mcp_servers.abby]`ncommand = `"$nodeCommand`"`nargs = [`"$launcher`"]`n";[IO.File]::WriteAllText((Join-Path $root 'mcp-client.example.toml'),$toml)
 if(Test-Path -LiteralPath (Join-Path $root 'pending.json')){Remove-Item -LiteralPath (Join-Path $root 'pending.json') -Force}
 Write-AbbyJson (Join-Path $root 'last-update.json') @{Status='Installed';Version=$manifest.Version;Versions=$versions;TybimUnchanged=$true}
 Write-Host "Installed $($manifest.Version) for Revit $($versions -join ', '). TYBIM unchanged. Reload the MCP client; example config: $root\mcp-client.example.toml"
}catch{
 if(!$ValidateOnly -and (Test-Path -LiteralPath $InstallRoot)){Write-AbbyJson (Join-Path $InstallRoot 'last-update.json') @{Status='Failed';Message=$_.Exception.Message;AtUtc=[DateTime]::UtcNow.ToString('o')}}
 Write-Error $_;exit 1
}finally{$mutex.ReleaseMutex();$mutex.Dispose()}
