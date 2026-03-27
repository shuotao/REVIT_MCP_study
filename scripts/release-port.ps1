<#
.SYNOPSIS
    Release Port 8964 - Clean up orphaned HTTP.sys Request Queue
.DESCRIPTION
    When Revit crashes or shuts down abnormally, HttpListener's HTTP.sys binding
    may persist, causing PID 4 (System) to hold the port. This script releases it.
.USAGE
    Terminal:   powershell -ExecutionPolicy Bypass -File scripts/release-port.ps1
    AI Chat:    Ask AI to run this script
    setup.ps1:  Phase 7 calls this automatically
.NOTES
    For PID 4 (HTTP.sys orphan), requires Administrator privileges to restart
    the HTTP service. This briefly affects IIS/WSDAP but they auto-recover.
#>

# Silent switch: setup.ps1 calls with -Silent to suppress interactive output
param(
    [int]$Port = 8964,
    [switch]$Silent
)

$ErrorActionPreference = "Stop"

function Test-PortInUse {
    param([int]$Port)
    $listeners = [System.Net.NetworkInformation.IPGlobalProperties]::GetIPGlobalProperties().GetActiveTcpListeners()
    return ($listeners | Where-Object { $_.Port -eq $Port }).Count -gt 0
}

function Get-PortOccupant {
    param([int]$Port)
    $netstat = netstat -ano 2>$null | Select-String ":$Port "
    foreach ($line in $netstat) {
        $parts = ($line.ToString().Trim()) -split '\s+'
        if ($parts.Count -ge 5) {
            $procId = [int]$parts[-1]
            if ($procId -gt 0) {
                try {
                    $proc = Get-Process -Id $procId -ErrorAction SilentlyContinue
                    return @{ PID = $procId; Name = $proc.ProcessName }
                } catch {
                    return @{ PID = $procId; Name = "unknown" }
                }
            }
        }
    }
    return $null
}

function Release-HttpSysPort {
    param([int]$Port)

    # Step 1: Check if port is actually in use
    if (-not (Test-PortInUse -Port $Port)) {
        if (-not $Silent) {
            Write-Host "  [OK] Port $Port is available, no action needed" -ForegroundColor Green
        }
        return $true
    }

    # Step 2: Identify occupant
    $occupant = Get-PortOccupant -Port $Port
    if ($null -eq $occupant) {
        if (-not $Silent) {
            Write-Host "  [??] Port $Port appears occupied but no process found" -ForegroundColor Yellow
        }
    }
    else {
        $procId = $occupant.PID
        $name = $occupant.Name

        # Step 2a: Kill zombie node/revitmcp processes
        if ($name -match "node|revitmcp") {
            if (-not $Silent) {
                Write-Host "  [..] Found zombie process $name (PID: $procId), killing..." -ForegroundColor Yellow
            }
            try {
                Stop-Process -Id $procId -Force -ErrorAction SilentlyContinue
                Start-Sleep -Milliseconds 500
            } catch { }

            if (-not (Test-PortInUse -Port $Port)) {
                if (-not $Silent) {
                    Write-Host "  [OK] Killed $name, Port $Port released" -ForegroundColor Green
                }
                return $true
            }
        }

        # Step 2b: PID 4 (System/HTTP.sys) - restart HTTP service
        if ($procId -eq 4) {
            if (-not $Silent) {
                Write-Host "  [..] Port $Port held by HTTP.sys (PID: 4) - orphaned HttpListener binding" -ForegroundColor Yellow
                Write-Host "  [..] Restarting HTTP service to release orphan Request Queue..." -ForegroundColor Yellow
            }

            # Requires Administrator
            $isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
            if (-not $isAdmin) {
                Write-Host "  [!!] Administrator privileges required to restart HTTP service" -ForegroundColor Red
                Write-Host "  [!!] Please re-run as Administrator, or manually execute:" -ForegroundColor Red
                Write-Host "       net stop http /y && net start http" -ForegroundColor Cyan
                return $false
            }

            try {
                # Stop HTTP service (/y auto-confirms dependent services)
                $null = net stop http /y 2>&1
                Start-Sleep -Seconds 1

                # Restart HTTP service
                $null = net start http 2>&1
                Start-Sleep -Milliseconds 500

                if (-not (Test-PortInUse -Port $Port)) {
                    if (-not $Silent) {
                        Write-Host "  [OK] HTTP service restarted, Port $Port released" -ForegroundColor Green
                    }
                    return $true
                }
                else {
                    if (-not $Silent) {
                        Write-Host "  [!!] Port $Port still occupied after HTTP service restart" -ForegroundColor Red
                    }
                    return $false
                }
            }
            catch {
                if (-not $Silent) {
                    Write-Host "  [!!] Failed to restart HTTP service: $_" -ForegroundColor Red
                }
                return $false
            }
        }

        # Step 2c: Other processes - prompt user
        if (-not $Silent) {
            Write-Host "  [!!] Port $Port occupied by $name (PID: $procId)" -ForegroundColor Red
            Write-Host "  [!!] Please close it manually, or run: Stop-Process -Id $procId -Force" -ForegroundColor Red
        }
        return $false
    }

    return $false
}

# --- Main ---
if (-not $Silent) {
    Write-Host ""
    Write-Host "  ============================================" -ForegroundColor Cyan
    Write-Host "   Release Port $Port - MCP Port Cleanup Tool" -ForegroundColor Cyan
    Write-Host "  ============================================" -ForegroundColor Cyan
    Write-Host ""
}

$result = Release-HttpSysPort -Port $Port

if (-not $Silent) {
    Write-Host ""
    if ($result) {
        Write-Host "  Port $Port is ready. You can start Revit MCP service now." -ForegroundColor Green
    }
    else {
        Write-Host "  Port $Port release failed. See messages above for manual steps." -ForegroundColor Red
    }
    Write-Host ""
}

exit ([int](-not $result))
