#Requires -Version 5.1
[CmdletBinding()]param([string]$ExpectedUserSid='', [switch]$ReadOnly)
$ErrorActionPreference='Stop'
$taskName='AbbyMCP-AutoUpdate'
$root=Join-Path $env:LOCALAPPDATA 'AbbyMCP'
$sid=[Security.Principal.WindowsIdentity]::GetCurrent().User.Value
if($ExpectedUserSid -and $ExpectedUserSid -ne $sid){throw 'Different Windows account after elevation; nothing changed'}
$task=Get-ScheduledTask -TaskName $taskName -TaskPath '\' -ErrorAction Stop
$expected=Join-Path $root 'Update-Abby.ps1'
if(@($task.Actions).Count -ne 1 -or !$task.Actions[0].Arguments.Contains($expected)){throw 'Task is not the installed Abby updater; preserved'}
if($ReadOnly){$info=Get-ScheduledTaskInfo -TaskName $taskName -TaskPath '\';[pscustomobject]@{Task=$taskName;Interval=@($task.Triggers|ForEach-Object{$_.Repetition.Interval});NextRun=$info.NextRunTime.ToString('o');Enabled=$task.Settings.Enabled}|ConvertTo-Json;return}
$principal=New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if(!$principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){
 $argsText='-NoProfile -ExecutionPolicy Bypass -File "'+$PSCommandPath+'" -ExpectedUserSid "'+$sid+'"'
 $process=Start-Process -FilePath (Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe') -Verb RunAs -ArgumentList $argsText -WindowStyle Hidden -Wait -PassThru
 if($process.ExitCode -ne 0){throw 'Windows did not complete the schedule change'}
 Get-Content -LiteralPath (Join-Path $root 'update-schedule.json') -Raw
 return
}
$before=[xml](Export-ScheduledTask -TaskName $taskName -TaskPath '\')
$fingerprint=$before.Task.Actions.OuterXml+$before.Task.Principals.OuterXml+$before.Task.Settings.OuterXml
$backup=Join-Path $root ('backups\schedule-'+(Get-Date -Format yyyyMMdd-HHmmss)+'.xml')
New-Item -ItemType Directory -Path (Split-Path -Parent $backup) -Force|Out-Null
[IO.File]::WriteAllText($backup,$before.OuterXml,(New-Object Text.UTF8Encoding($false)))
$trigger=New-ScheduledTaskTrigger -Once -At (Get-Date).AddHours(1) -RepetitionInterval (New-TimeSpan -Hours 1) -RepetitionDuration (New-TimeSpan -Days 3650)
Set-ScheduledTask -TaskName $taskName -TaskPath '\' -Trigger $trigger|Out-Null
$after=[xml](Export-ScheduledTask -TaskName $taskName -TaskPath '\')
if(($after.Task.Actions.OuterXml+$after.Task.Principals.OuterXml+$after.Task.Settings.OuterXml) -ne $fingerprint){throw 'Unexpected task property change; original task XML is backed up'}
$verified=Get-ScheduledTask -TaskName $taskName -TaskPath '\'
if(@($verified.Triggers).Count -ne 1 -or $verified.Triggers[0].Repetition.Interval -ne 'PT1H'){throw 'Hourly schedule verification failed'}
$info=Get-ScheduledTaskInfo -TaskName $taskName -TaskPath '\'
$report=@{Task=$taskName;Interval='PT1H';Minutes=60;NextRun=$info.NextRunTime.ToString('o');Enabled=$verified.Settings.Enabled;OnlyTriggerChanged=$true;Backup=$backup}
[IO.File]::WriteAllText((Join-Path $root 'update-schedule.json'),($report|ConvertTo-Json),(New-Object Text.UTF8Encoding($false)))
$report|ConvertTo-Json
