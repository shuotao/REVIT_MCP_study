#Requires -Version 5.1
[CmdletBinding()]param([string]$Destination='')
. (Join-Path $PSScriptRoot 'Abby.Common.ps1');$channel=Get-AbbyChannel
if(!$Destination){$Destination=Read-Host 'New local source folder (for example D:\AbbyMCP-Source)'}
if(!$Destination -or !(Split-Path -IsAbsolute $Destination)){throw 'An absolute destination folder is required'}
if(Test-Path -LiteralPath $Destination){throw 'Destination already exists. No overwrite performed'}
Invoke-AbbyNative git @('clone','--branch',$channel.Branch,'--single-branch',('https://github.com/'+$channel.Repository+'.git'),$Destination)
Write-Host "Open $Destination in Codex for development."
