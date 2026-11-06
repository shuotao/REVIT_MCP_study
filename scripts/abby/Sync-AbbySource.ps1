#Requires -Version 5.1
[CmdletBinding()]param([ValidateSet('Pull','Push','Upstream')][string]$Action='Pull',[string]$SourceRoot=(Split-Path -Parent (Split-Path -Parent $PSScriptRoot)))
. (Join-Path $PSScriptRoot 'Abby.Common.ps1');$channel=Get-AbbyChannel
if($channel.Branch -ne 'my-revit-tool'){throw 'Only my-revit-tool may be synchronized; main is never a target'}
if(!(Test-Path -LiteralPath (Join-Path $SourceRoot '.git'))){throw 'Clone the Abby my-revit-tool branch first'}
$status=@(& git -C $SourceRoot status --porcelain);if($LASTEXITCODE -ne 0){throw 'Cannot read Git status'};if($status.Count){throw 'Commit reviewed source changes first. No automatic overwrite, stash or git add is performed.'}
$origin=& git -C $SourceRoot remote get-url origin;if($LASTEXITCODE -ne 0 -or $origin -notmatch ('github\.com[:/]'+[regex]::Escape($channel.Repository)+'(?:\.git)?$')){throw 'origin does not match the configured Abby repository'}
$branch=& git -C $SourceRoot branch --show-current;if($branch -ne $channel.Branch){throw 'Switch to my-revit-tool first; other branches are left unchanged'}
$remoteRef='origin/'+$channel.Branch
if($Action -eq 'Upstream'){
 Invoke-AbbyNative git @('-C',$SourceRoot,'fetch','https://github.com/shuotao/REVIT_MCP_study.git','main')
 Invoke-AbbyNative git @('-C',$SourceRoot,'merge','--no-ff','--no-commit','FETCH_HEAD')
 Write-Host 'Upstream merge prepared on my-revit-tool only. Review extensions, test, then commit and push. main was not modified.'
}else{
 Invoke-AbbyNative git @('-C',$SourceRoot,'fetch','origin',($channel.Branch+':refs/remotes/origin/'+$channel.Branch))
 if($Action -eq 'Pull'){Invoke-AbbyNative git @('-C',$SourceRoot,'merge','--ff-only',$remoteRef);Write-Host 'Abby source synchronized. Use Abby-Update.cmd for installed tools.'}
 else{Invoke-AbbyNative git @('-C',$SourceRoot,'merge-base','--is-ancestor',$remoteRef,'HEAD');Push-Location $SourceRoot;try{Invoke-AbbyNative node @((Join-Path $SourceRoot 'scripts\abby\check-public-source.cjs'),'--range',($remoteRef+'..HEAD'))}finally{Pop-Location};Invoke-AbbyNative git @('-C',$SourceRoot,'push','origin',('HEAD:refs/heads/'+$channel.Branch));Write-Host 'Only my-revit-tool was pushed. GitHub Actions will build the Abby release.'}
}
