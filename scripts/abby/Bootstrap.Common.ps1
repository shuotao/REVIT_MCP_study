#Requires -Version 5.1
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
function Get-AbbyNode($InstallRoot){
 $private=Join-Path $InstallRoot 'tools\node\node.exe'
 if(Test-Path -LiteralPath $private){return $private}
 $system=Get-Command node -ErrorAction SilentlyContinue
 if($system -and [int]((& $system.Source -p 'process.versions.node').Split('.')[0]) -ge 22){return $system.Source}
 return $null
}
function Install-AbbyNode($InstallRoot){
 $node=Get-AbbyNode $InstallRoot;if($node){return $node}
 [Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12
 $cache=Join-Path $InstallRoot 'downloads';New-Item -ItemType Directory -Path $cache -Force|Out-Null
 $base='https://nodejs.org/dist/latest-v22.x/'
 $response=Invoke-WebRequest -UseBasicParsing -Uri ($base+'SHASUMS256.txt')
 $sums=if($response.Content -is [byte[]]){[Text.Encoding]::UTF8.GetString($response.Content)}else{[string]$response.Content}
 $match=[regex]::Match([string]$sums,'(?m)^([a-fA-F0-9]{64})\s+(node-v22\.[0-9]+\.[0-9]+-win-x64\.zip)\s*$')
 if(!$match.Success){throw 'Official Node.js x64 checksum not found'}
 $zip=Join-Path $cache $match.Groups[2].Value
 Invoke-WebRequest -UseBasicParsing -Uri ($base+$match.Groups[2].Value) -OutFile $zip
 if((Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash -ne $match.Groups[1].Value){throw 'Node.js checksum mismatch'}
 Add-Type -AssemblyName System.IO.Compression.FileSystem
 $stage=Join-Path $cache ('node-'+[guid]::NewGuid().ToString('N'))
 [IO.Compression.ZipFile]::ExtractToDirectory($zip,$stage)
 $extracted=Join-Path $stage ([IO.Path]::GetFileNameWithoutExtension($zip))
 if(!(Test-Path -LiteralPath (Join-Path $extracted 'node.exe')) -or !(Test-Path -LiteralPath (Join-Path $extracted 'npm.cmd'))){throw 'Node.js archive is incomplete'}
 $target=Join-Path $InstallRoot 'tools\node';New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force|Out-Null
 if(Test-Path -LiteralPath $target){throw 'Existing private Node.js is invalid; preserved for review'}
 Move-Item -LiteralPath $extracted -Destination $target
 $node=Join-Path $target 'node.exe';if([int]((& $node -p 'process.versions.node').Split('.')[0]) -lt 22){throw 'Unsupported Node.js archive'}
 return $node
}
function Set-AbbyClientConfig($ConfigPath,$Node,$InstallRoot){
 $text=if(Test-Path -LiteralPath $ConfigPath){[IO.File]::ReadAllText($ConfigPath)}else{''}
 $launcher=Join-Path $InstallRoot 'start-mcp.cjs';$nodeToml=ConvertTo-Json -InputObject $Node -Compress;$launcherToml=ConvertTo-Json -InputObject $launcher -Compress;$rootToml=ConvertTo-Json -InputObject $InstallRoot -Compress
 $section="[mcp_servers.abby]`ncommand = $nodeToml`nargs = [$launcherToml]`ncwd = $rootToml`nenabled = true`nstartup_timeout_sec = 30`ntool_timeout_sec = 120`n"
 $matches=[regex]::Matches($text,'(?ms)^\[mcp_servers\.abby\]\s*\r?\n.*?(?=^\[|\z)')
 if($matches.Count -gt 1){throw 'Duplicate Abby MCP sections; existing configuration preserved'}
 if($matches.Count){
  if(!$matches[0].Value.Contains($launcher) -and !$matches[0].Value.Contains($launcherToml)){throw 'Existing abby MCP entry has a different owner; configuration preserved'}
  $m=$matches[0];$new=$text.Substring(0,$m.Index)+$section+$text.Substring($m.Index+$m.Length)
 }else{$new=$text+"`n`n"+$section}
 if($new -eq $text){return}
 if(Test-Path -LiteralPath $ConfigPath){$backup=Join-Path $InstallRoot ('backups\client-config-'+[guid]::NewGuid().ToString('N')+'.toml');New-Item -ItemType Directory -Path (Split-Path -Parent $backup) -Force|Out-Null;Copy-Item -LiteralPath $ConfigPath -Destination $backup}
 New-Item -ItemType Directory -Path (Split-Path -Parent $ConfigPath) -Force|Out-Null
 [IO.File]::WriteAllText($ConfigPath,$new,(New-Object Text.UTF8Encoding($false)))
}
function Install-AbbyPython($InstallRoot){
 $target=Join-Path $InstallRoot 'tools\python';$python=Join-Path $target 'python.exe'
 if(!(Test-Path -LiteralPath $python)){
  if(Test-Path -LiteralPath $target){throw 'Incomplete private Python installation preserved for review'}
  $cache=Join-Path $InstallRoot 'downloads';New-Item -ItemType Directory -Path $cache -Force|Out-Null
  $archive=Join-Path $cache 'python-3.13.17-amd64.zip'
  Invoke-WebRequest -UseBasicParsing -Uri 'https://www.python.org/ftp/python/3.13.17/python-3.13.17-amd64.zip' -OutFile $archive
  if((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne 'B26CA4B4EE51E157836049934D2B8EABD55939BE296F3130254251B6DF96EC2F'){throw 'Python checksum mismatch'}
  Add-Type -AssemblyName System.IO.Compression.FileSystem
  New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force|Out-Null
  [IO.Compression.ZipFile]::ExtractToDirectory($archive,$target)
  if(!(Test-Path -LiteralPath $python)){throw 'Private Python archive is incomplete'}
 }
 & $python -m ensurepip --upgrade
 if($LASTEXITCODE){throw 'Private Python pip initialization failed'}
 & $python -m pip install --disable-pip-version-check ezdxf
 if($LASTEXITCODE){throw 'ezdxf installation failed'}
 & $python -c 'import ezdxf; print(ezdxf.__version__)'
 if($LASTEXITCODE){throw 'ezdxf verification failed'}
 return $python
}
function Install-AbbyDesktopAndOda {
 $app=Get-AppxPackage -Name OpenAI.Codex -ErrorAction SilentlyContinue
 $oda=@(Get-ChildItem -Path (Join-Path $env:PROGRAMFILES 'ODA\ODAFileConverter*\ODAFileConverter.exe') -ErrorAction SilentlyContinue)
 if($app -and $oda.Count){return}
 $winget=Get-Command winget -ErrorAction SilentlyContinue
 if(!$winget){
  Install-PackageProvider -Name NuGet -MinimumVersion 2.8.5.201 -Scope CurrentUser -Force|Out-Null
  Install-Module -Name Microsoft.WinGet.Client -Scope CurrentUser -Force -Repository PSGallery|Out-Null
  Import-Module Microsoft.WinGet.Client -Force
  Repair-WinGetPackageManager -Latest
  $winget=Get-Command winget -ErrorAction SilentlyContinue
  if(!$winget){$exe=Join-Path $env:LOCALAPPDATA 'Microsoft\WindowsApps\winget.exe';if(Test-Path -LiteralPath $exe){$winget=Get-Item -LiteralPath $exe}}
  if(!$winget){throw 'Windows could not register App Installer. See install log; no security or Store policies were changed.'}
 }
 $wingetExe=if($winget.PSObject.Properties['Source']){$winget.Source}else{$winget.FullName}
 if(!$app){& $wingetExe install --exact --id 9PLM9XGG6VKS --source msstore --accept-source-agreements --accept-package-agreements --disable-interactivity;if($LASTEXITCODE){throw 'Codex desktop installation failed; existing applications were not upgraded'}}
 if(!$oda.Count){& $wingetExe install --exact --id ODA.ODAFileConverter --source winget --silent --accept-source-agreements --accept-package-agreements --disable-interactivity;if($LASTEXITCODE){throw 'ODA installation failed; run the installer again after resolving the reported Windows error'}}
}
function Initialize-AbbyLoopback {
 $url='http://localhost:8964/'
 $existing=& netsh.exe http show urlacl ("url="+$url) 2>&1
 if($LASTEXITCODE -eq 0 -and ($existing -join "`n").Contains($url)){return 'Existing reservation preserved'}
 if(@(Get-NetTCPConnection -LocalPort 8964 -State Listen -ErrorAction SilentlyContinue).Count){throw 'Localhost8964 is already in use; no network permissions changed'}
 $sid=[Security.Principal.WindowsIdentity]::GetCurrent().User.Value
 & netsh.exe http add urlacl ("url="+$url) ("sddl=D:(A;;GX;;;"+$sid+")")|Out-Host
 if($LASTEXITCODE){throw 'Could not reserve the Abby localhost endpoint'}
 return 'Reserved localhost8964 for this user only'
}
