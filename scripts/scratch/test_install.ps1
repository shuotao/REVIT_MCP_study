$Error.Clear()
try {
    . C:\Users\User\Desktop\REVIT_MCP_study\scripts\setup.ps1 -NonInteractive -SkipAIConfig -RevitVersions "2024"
} catch {
    Write-Host "Caught Exception:"
    $_ | Format-List -Property * -Force
}
if ($Error.Count -gt 0) {
    Write-Host "Errors count: $($Error.Count)"
    foreach ($err in $Error) {
        Write-Host "---"
        $err | Format-List -Property * -Force
        if ($err.ScriptStackTrace) {
            Write-Host "ScriptStackTrace:"
            Write-Host $err.ScriptStackTrace
        }
    }
}
