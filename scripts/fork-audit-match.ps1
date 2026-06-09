#requires -Version 5.1
<#
.SYNOPSIS
    Match a people roster (real name <-> email) onto a fork-audit CSV.

.DESCRIPTION
    Adds three columns to the fork-audit CSV:
      guessedPerson  best-guess real name (empty if no confident match)
      matchBasis     how the match was made:
                       commit-email      = a commit author email hit the roster (highest confidence)
                       login=localpart   = fork login equals an email's local-part
                       login~localpart   = fork login minus a trailing -suffix equals a local-part
                       email-no-name     = email matched roster but roster had no name
                       none              = no confident match (needs human input)
      matchEmail     the roster email that produced the match

    Roster format: lines of `Name <email>` or bare `email`, separated by the
    Chinese comma (、) and/or newlines. Read as UTF-8 so Chinese names survive.

    Conservative on purpose: prefers leaving guessedPerson empty over guessing wrong.

.EXAMPLE
    powershell -File scripts/fork-audit-match.ps1
#>
[CmdletBinding()]
param(
    [string]$Csv    = 'docs/fork-audit/fork-audit-2026-06-09.csv',
    [string]$Roster = 'docs/fork-audit/people-roster.txt'
)
$ErrorActionPreference = 'Stop'

if (-not (Test-Path $Csv))    { throw "CSV not found: $Csv" }
if (-not (Test-Path $Roster)) { throw "Roster not found: $Roster" }

# ---- parse roster (UTF-8) -------------------------------------------------
$raw = [System.IO.File]::ReadAllText((Resolve-Path $Roster), [System.Text.Encoding]::UTF8)
# Build the Chinese comma (U+3001) at runtime so script encoding can't corrupt the literal.
$ideographicComma = [char]0x3001
$raw = $raw.Replace($ideographicComma, "`n")
$tokens = $raw -split "[\r\n]+" | ForEach-Object { $_.Trim() } | Where-Object { $_ }

$people = New-Object System.Collections.ArrayList
foreach ($t in $tokens) {
    $name = ''; $email = ''
    if ($t -match '^(.*?)<\s*([^>]+?)\s*>\s*$') {
        $name  = $matches[1].Trim().Trim('"').Trim()
        $email = $matches[2].Trim()
    } elseif ($t -match '^"?\s*([^"<>\s,]+@[^"<>\s,]+)\s*"?$') {
        $email = $matches[1].Trim()
    } else {
        continue
    }
    $email = $email.ToLower()
    if (-not $email) { continue }
    $local = ($email -split '@')[0]
    [void]$people.Add([pscustomobject]@{ name = $name; email = $email; local = $local.ToLower() })
}
Write-Host "Roster parsed: $($people.Count) entries"

# lookups (first occurrence wins)
$byEmail = @{}
$byLocal = @{}
foreach ($p in $people) {
    if (-not $byEmail.ContainsKey($p.email)) { $byEmail[$p.email] = $p }
    if ($p.local -and -not $byLocal.ContainsKey($p.local)) { $byLocal[$p.local] = $p }
}

# ---- read fork-audit CSV --------------------------------------------------
$rows = @(Import-Csv -Path $Csv)
Write-Host "CSV rows: $($rows.Count)"

# backup the pristine CSV once
$bak = "$Csv.orig"
if (-not (Test-Path $bak)) { Copy-Item $Csv $bak; Write-Host "Backup written: $bak" }

$matched = 0
foreach ($r in $rows) {
    $guess = ''; $basis = 'none'; $via = ''

    # collect commit-author emails from the authors column ("Name <email> ; ...")
    $emails = @()
    if ($r.authors) {
        $emails = [regex]::Matches($r.authors, '<\s*([^>]+?)\s*>') | ForEach-Object { $_.Groups[1].Value.ToLower() }
    }

    # 1. commit-email exact (with a name)
    foreach ($e in $emails) {
        if ($byEmail.ContainsKey($e) -and $byEmail[$e].name) { $guess = $byEmail[$e].name; $basis = 'commit-email'; $via = $e; break }
    }
    # 1b. commit-email matched but roster had no name
    if (-not $guess) {
        foreach ($e in $emails) {
            if ($byEmail.ContainsKey($e)) { $basis = 'email-no-name'; $via = $e; break }
        }
    }

    $lk = "$($r.login)".ToLower()

    # 2. login == email local-part
    if ($basis -eq 'none' -or $basis -eq 'email-no-name') {
        if ($byLocal.ContainsKey($lk) -and $byLocal[$lk].name) {
            $guess = $byLocal[$lk].name; $basis = 'login=localpart'; $via = $byLocal[$lk].email
        }
    }

    # 3. login minus a trailing -suffix == local-part (len>=5 to avoid noise)
    if (-not $guess) {
        $stripped = ($lk -replace '-[a-z0-9]+$', '')
        if ($stripped.Length -ge 5 -and $byLocal.ContainsKey($stripped) -and $byLocal[$stripped].name) {
            $guess = $byLocal[$stripped].name; $basis = 'login~localpart'; $via = $byLocal[$stripped].email
        }
    }

    # 4. email-core: login (minus -suffix, minus digits) == local-part (minus digits)
    #    catches 111yihuiiiii<->yihuiiiii, yujiuncheng94<->yujiuncheng
    if (-not $guess) {
        $loginCore = (($lk -replace '-[a-z0-9]+$', '') -replace '\d', '')
        if ($loginCore.Length -ge 5) {
            foreach ($p in $people) {
                if (-not $p.name) { continue }
                if (($p.local -replace '\d', '') -eq $loginCore) {
                    $guess = $p.name; $basis = 'login~email-core'; $via = $p.email; break
                }
            }
        }
    }

    # 5. name-normalized: login (alnum, minus digits) == roster name (alnum).
    #    Chinese names reduce to empty here, so only English names can match -> safe.
    if (-not $guess) {
        $loginN = (($lk -replace '\d', '') -replace '[^a-z0-9]', '')
        if ($loginN.Length -ge 6) {
            foreach ($p in $people) {
                if (-not $p.name) { continue }
                $nameN = ($p.name.ToLower() -replace '[^a-z0-9]', '')
                if ($nameN.Length -ge 6 -and $nameN -eq $loginN) {
                    $guess = $p.name; $basis = 'login~name'; $via = $p.email; break
                }
            }
        }
    }

    if ($guess) { $matched++ }

    Add-Member -InputObject $r -NotePropertyName guessedPerson -NotePropertyValue $guess -Force
    Add-Member -InputObject $r -NotePropertyName matchBasis    -NotePropertyValue $basis -Force
    Add-Member -InputObject $r -NotePropertyName matchEmail    -NotePropertyValue $via   -Force
}

# ---- write back (overwrite the CSV, UTF-8) --------------------------------
$rows | Export-Csv -Path $Csv -NoTypeInformation -Encoding UTF8

# basis breakdown
Write-Host ""
Write-Host "==== Match summary ===="
Write-Host "  named matches : $matched / $($rows.Count)"
$rows | Group-Object matchBasis | Sort-Object Count -Descending | ForEach-Object {
    Write-Host ("  {0,-16} {1}" -f $_.Name, $_.Count)
}
Write-Host ""
Write-Host "CSV updated: $Csv"
