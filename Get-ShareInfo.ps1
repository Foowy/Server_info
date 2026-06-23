# Copyright (c) 2026 Foowy. Licensed under the MIT License.
#Requires -Version 3.0

param(
    [string[]]$ComputerName,
    [string]$OutputPath = $null,
    [switch]$IncludeSystemShares
)

<#
.SYNOPSIS
    Audit SMB share disk usage on local or remote Windows servers.

.DESCRIPTION
    Enumerates all SMB shares on one or more servers, calculates disk usage per share with a 300-second timeout,
    and outputs results in human-readable format (MB/GB/TB). Handles access denied and timeout scenarios gracefully,
    reporting partial results without halting execution. Supports comma-separated server lists and optional CSV export
    for capacity planning and billing audits.

.PARAMETER ComputerName
    One or more server names to audit. Accepts comma-separated list (e.g., "Server01,Server02,Server03").
    If omitted, the user is prompted. Use '.' or 'localhost' for the local computer.

.PARAMETER OutputPath
    Optional. Path to export results as CSV. If provided, results are exported in addition to console output.
    Format: "C:\reports\shares.csv"

.PARAMETER IncludeSystemShares
    Optional. By default, system shares ($, IPC$, ADMIN$) are excluded. Use this flag to include them.

.EXAMPLE
    .\Get-ShareInfo.ps1 -ComputerName Server01
    Audit shares on Server01 and display results as formatted table.

.EXAMPLE
    .\Get-ShareInfo.ps1 -ComputerName Server01,Server02,Server03 -OutputPath .\shares.csv
    Audit multiple servers and export results to CSV.

.EXAMPLE
    .\Get-ShareInfo.ps1 -ComputerName . -IncludeSystemShares
    Audit local computer including system shares.

.EXAMPLE
    .\Get-ShareInfo.ps1
    Prompt user for server name(s) and audit.

.NOTES
    Version : 1.0.0
    Author  : Foowy
    GitHub  : https://github.com/Foowy/Server_info

    Requires administrative privileges on target servers for accurate share enumeration and size calculation.
    Target platforms: Windows Server 2012 or later, or any system with PowerShell 3.0+ and the SmbShare module.
    System shares (IPC$, ADMIN$, etc.) are excluded by default to reduce noise; use -IncludeSystemShares to include them.
    Share size calculation uses background jobs with a 300-second timeout per share to prevent hangs.
    Partial access (some files readable, some denied) reports readable file size; complete access denial reports "[Access Denied]".

#>

$script:Version = '1.0.0'

# Determine if we are already running as admin, elevate if not
if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Output 'Restarting with elevated privileges...'
    $scriptDir = Split-Path -Path $PSCommandPath -Parent
    if (-not $ComputerName) {
        $rawInput = Read-Host 'Enter one or more servers (comma-separated)'
        $ComputerName = $rawInput -split ',' | ForEach-Object { $_.Trim() }
    }
    $computerArgument = $ComputerName -join ','
    $extraArgs = ''
    if ($OutputPath)          { $extraArgs += " -OutputPath '$OutputPath'" }
    if ($IncludeSystemShares) { $extraArgs += ' -IncludeSystemShares' }
    $psi = [System.Diagnostics.ProcessStartInfo]::new()
    $psi.FileName  = 'powershell.exe'
    $psi.Arguments = "-NoExit -NoProfile -ExecutionPolicy Bypass -Command `"Set-Location '$scriptDir'; & '$PSCommandPath' -ComputerName '$computerArgument'$extraArgs`""
    $psi.Verb      = 'runas'
    [System.Diagnostics.Process]::Start($psi) | Out-Null
    exit
}

# GitHub Auto-Update Configuration
$updateConfig = @{
    Owner     = 'Foowy'
    Repo      = 'Server_info'
    Branch    = 'main'
    FilePath  = 'Get-ShareInfo.ps1'
    StampFile = Join-Path (Split-Path $PSCommandPath -Parent) '.GSI_share_update_check'
}

# Auto-Update Function
function Invoke-UpdateCheck {
    param(
        [hashtable]$Config,
        [string[]]$ForwardedArgs
    )
    $checkNeeded = $true
    if (Test-Path $Config.StampFile) {
        try {
            $lastCheck = Get-Content $Config.StampFile -Raw | Get-Date
            if ($lastCheck.Date -eq (Get-Date).Date) { $checkNeeded = $false }
        }
        catch { }
    }

    if (-not $checkNeeded) {
        Write-Verbose 'Update check skipped - already ran today.'
        return
    }

    Write-Host 'Checking for script updates...' -ForegroundColor Cyan

    $apiUrl = "https://api.github.com/repos/$($Config.Owner)/$($Config.Repo)/contents/$($Config.FilePath)?ref=$($Config.Branch)"
    $headers = @{
        Accept       = 'application/vnd.github.v3+json'
        'User-Agent' = 'PowerShell-AutoUpdater'
    }

    # Force TLS 1.2 -- required by GitHub, older PowerShell sessions default to TLS 1.0
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

    try {
        $response = Invoke-RestMethod -Uri $apiUrl -Headers $headers -ErrorAction Stop
    }
    catch {
        Write-Warning "Update check failed - could not reach GitHub: $_"
        (Get-Date).ToString('o') | Set-Content $Config.StampFile
        return
    }

    # Compute git blob SHA-1 of the local file: SHA1("blob {size}\0{bytes}")
    # Matches the sha field GitHub returns -- encoding-agnostic, no string normalization needed
    $localBytes = [System.IO.File]::ReadAllBytes($PSCommandPath)
    $header     = [System.Text.Encoding]::ASCII.GetBytes("blob $($localBytes.Length)`0")
    $ms         = [System.IO.MemoryStream]::new($header.Length + $localBytes.Length)
    $ms.Write($header, 0, $header.Length)
    $ms.Write($localBytes, 0, $localBytes.Length)
    $ms.Position = 0
    $sha1     = [System.Security.Cryptography.SHA1]::Create()
    $localSha = [System.BitConverter]::ToString($sha1.ComputeHash($ms)).Replace('-', '').ToLower()
    $sha1.Dispose()
    $ms.Dispose()

    (Get-Date).ToString('o') | Set-Content $Config.StampFile

    if ($localSha -eq $response.sha) {
        Write-Host 'Script is up to date.' -ForegroundColor Green
        return
    }

    Write-Host 'Update available - applying update...' -ForegroundColor Yellow

    # Decode raw bytes from base64 -- write exactly what GitHub has so the local git blob SHA matches next run
    $cleanB64    = $response.content -replace '\s', ''
    $remoteBytes = [System.Convert]::FromBase64String($cleanB64)

    try {
        Copy-Item -Path $PSCommandPath -Destination "$PSCommandPath.bak" -Force
        [System.IO.File]::WriteAllBytes($PSCommandPath, $remoteBytes)
        Remove-Item -Path "$PSCommandPath.bak" -Force -ErrorAction SilentlyContinue
        Write-Host 'Update applied successfully. Relaunching...' -ForegroundColor Green
    }
    catch {
        Write-Warning "Failed to write update to disk: $_"
        return
    }

    $scriptDir = Split-Path $PSCommandPath -Parent
    $argString = if ($ForwardedArgs) { "-ComputerName '$($ForwardedArgs -join ',')'" } else { '' }
    $psi = [System.Diagnostics.ProcessStartInfo]::new()
    $psi.FileName       = 'powershell.exe'
    $psi.Arguments      = "-NoExit -NoProfile -ExecutionPolicy Bypass -Command `"Set-Location '$scriptDir'; & '$PSCommandPath' $argString`""
    $psi.Verb           = 'runas'
    $psi.UseShellExecute = $true
    $newProc = [System.Diagnostics.Process]::Start($psi)
    if ($newProc) {
        [System.Diagnostics.Process]::GetCurrentProcess().Kill()
    } else {
        Write-Warning 'Relaunch failed -- continuing with the updated script in this window.'
    }
}

function Convert-BytesToReadable {
    param([long]$Bytes)

    if ($Bytes -eq 0) { return '0 B' }

    $sizes = 'B', 'KB', 'MB', 'GB', 'TB'
    $order = 0
    [double]$value = $Bytes
    while ($value -ge 1024 -and $order -lt $sizes.Count - 1) {
        $order++
        $value = $value / 1024
    }

    return '{0:N2} {1}' -f $value, $sizes[$order]
}

function Get-RemoteShareSize {
    param(
        [string]$SharePath,
        [int]$TimeoutSeconds = 300
    )

    try {
        $job = Start-Job -ScriptBlock {
            param($Path)
            # Capture errors separately so fully-denied shares (size=0, errors>0) can be distinguished from empty shares
            $errs = @()
            $size = (Get-ChildItem -Path $Path -Recurse -ErrorAction SilentlyContinue -ErrorVariable errs |
                     Measure-Object -Property Length -Sum).Sum
            [PSCustomObject]@{ Size = [long]($size -as [long]); HadErrors = ($errs.Count -gt 0) }
        } -ArgumentList $SharePath

        $result = Wait-Job -Job $job -Timeout $TimeoutSeconds

        if ($null -eq $result) {
            Remove-Job -Job $job -Force
            return @{ Size = 0; Status = 'Timeout' }
        }

        $received = Receive-Job -Job $job
        Remove-Job -Job $job

        if ($null -eq $received) {
            return @{ Size = 0; Status = 'Error: job returned no output' }
        }
        if ($received.Size -eq 0 -and $received.HadErrors) {
            return @{ Size = 0; Status = 'Access Denied' }
        }
        return @{ Size = $received.Size; Status = 'OK' }
    }
    catch {
        return @{ Size = 0; Status = "Error: $($_.Exception.Message)" }
    }
}

function Get-AllShareInfo {
    param(
        [string]$ComputerName,
        [bool]$IncludeSystemShares = $false
    )

    $isLocal = ($ComputerName -eq $env:COMPUTERNAME -or $ComputerName -eq '.' -or $ComputerName -eq 'localhost')

    try {
        if ($isLocal) {
            $shares = Get-SMBShare -ErrorAction Stop
        }
        else {
            $shares = Invoke-Command -ComputerName $ComputerName -ScriptBlock {
                Get-SMBShare
            } -ErrorAction Stop
        }

        if (-not $IncludeSystemShares) {
            $shares = $shares | Where-Object { $_.Name -notmatch '\$$' }
        }

        $results      = @()
        $totalSize    = 0
        $successCount = 0

        foreach ($share in $shares) {
            $sharePath = if ($isLocal) { "\\localhost\$($share.Name)" } else { "\\$ComputerName\$($share.Name)" }

            $sizeInfo = Get-RemoteShareSize -SharePath $sharePath -TimeoutSeconds 300
            $status   = $sizeInfo.Status

            $results += [PSCustomObject]@{
                ShareName = $share.Name
                SizeBytes = $sizeInfo.Size
                SizeHuman = Convert-BytesToReadable $sizeInfo.Size
                Status    = $status
                Path      = $share.Path
            }

            if ($status -eq 'OK') {
                $totalSize += $sizeInfo.Size
                $successCount++
            }
        }

        return @{
            ComputerName = $ComputerName
            Timestamp    = Get-Date -Format 'yyyy-MM-dd HH:mm:ss'
            Shares       = $results
            TotalSize    = $totalSize
            SuccessCount = $successCount
        }
    }
    catch {
        Write-Warning "Failed to enumerate shares on ${ComputerName}: $($_.Exception.Message)"
        return $null
    }
}

function Invoke-ShareAudit {
    param(
        [string[]]$ComputerName,
        [string]$OutputPath = $null,
        [bool]$IncludeSystemShares = $false
    )

    if (-not $ComputerName) {
        $rawInput     = Read-Host 'Enter one or more servers (comma-separated)'
        $ComputerName = $rawInput -split ',' | ForEach-Object { $_.Trim() }
    }

    # Always re-split every element -- elevation and prompt paths join with commas into one element
    $ComputerName = @($ComputerName | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ -ne '' })

    $allResults = @()

    foreach ($computer in $ComputerName) {
        Write-Host "`n===================== $computer =====================" -ForegroundColor Cyan

        $auditResult = Get-AllShareInfo -ComputerName $computer -IncludeSystemShares $IncludeSystemShares

        if ($auditResult) {
            Write-Host "Timestamp: $($auditResult.Timestamp)" -ForegroundColor Gray
            Write-Host "Total Shares: $($auditResult.Shares.Count) | Accessible: $($auditResult.SuccessCount)" -ForegroundColor Gray
            Write-Host ''

            $auditResult.Shares |
                Select-Object @{n='Share Name'; e='ShareName'}, @{n='Size'; e='SizeHuman'}, Status |
                Format-Table -AutoSize

            Write-Host "Total (accessible): $(Convert-BytesToReadable $auditResult.TotalSize)" -ForegroundColor Green
            Write-Host ''

            $allResults += $auditResult.Shares | Select-Object `
                @{n='ComputerName'; e={ $computer }},
                ShareName,
                SizeHuman,
                SizeBytes,
                Status,
                @{n='Timestamp'; e={ $auditResult.Timestamp }}
        }
    }

    if ($OutputPath) {
        if ($allResults.Count -eq 0) {
            Write-Warning "No results to export -- all share enumerations failed."
        }
        else {
            try {
                $allResults | Export-Csv -Path $OutputPath -NoTypeInformation -Force -ErrorAction Stop
                Write-Host "Results exported to: $OutputPath" -ForegroundColor Green
            }
            catch {
                Write-Warning "Failed to export CSV to '${OutputPath}': $_"
            }
        }
    }
}

# Runs at most once per calendar day, forwards ComputerName into relaunched process if an update is applied
Invoke-UpdateCheck -Config $updateConfig -ForwardedArgs $ComputerName

Invoke-ShareAudit -ComputerName $ComputerName -OutputPath $OutputPath -IncludeSystemShares $IncludeSystemShares

Write-Output ('SCRIPT RUN DATE/TIME: {0}' -f (Get-Date).ToString('yyyy-MM-dd HH:mm:ss'))
