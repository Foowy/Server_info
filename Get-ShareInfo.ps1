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
        Accept       = 'application/vnd.github.v3.raw'
        'User-Agent' = 'PowerShell-AutoUpdater'
    }

    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

    try {
        $remoteContent = Invoke-RestMethod -Uri $apiUrl -Headers $headers -ErrorAction Stop
    }
    catch {
        Write-Warning "Update check failed - could not reach GitHub: $_"
        (Get-Date).ToString('o') | Set-Content $Config.StampFile
        return
    }

    $localContent = Get-Content -Path $PSCommandPath -Raw
    $sha          = [System.Security.Cryptography.SHA256]::Create()
    $localHash    = ($sha.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($localContent))  | ForEach-Object { $_.ToString('x2') }) -join ''
    $remoteHash   = ($sha.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($remoteContent)) | ForEach-Object { $_.ToString('x2') }) -join ''
    $sha.Dispose()

    (Get-Date).ToString('o') | Set-Content $Config.StampFile

    if ($localHash -eq $remoteHash) {
        Write-Host 'Script is up to date.' -ForegroundColor Green
        return
    }

    Write-Host 'Update available - applying update...' -ForegroundColor Yellow
    $backupPath = "$PSCommandPath.bak"
    try {
        Copy-Item -Path $PSCommandPath -Destination $backupPath -Force
        [System.IO.File]::WriteAllText($PSCommandPath, $remoteContent, [System.Text.Encoding]::UTF8)
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
    [System.Diagnostics.Process]::Start($psi) | Out-Null
    [System.Diagnostics.Process]::GetCurrentProcess().Kill()
}

function Convert-BytesToReadable {
    param([long]$Bytes)

    if ($Bytes -eq 0) { return '0 B' }

    $sizes = 'B', 'KB', 'MB', 'GB', 'TB'
    $order = 0
    while ($Bytes -ge 1024 -and $order -lt $sizes.Count - 1) {
        $order++
        $Bytes = $Bytes / 1024
    }

    return '{0:N2} {1}' -f $Bytes, $sizes[$order]
}

function Get-RemoteShareSize {
    param(
        [string]$SharePath,
        [int]$TimeoutSeconds = 300
    )

    try {
        $job = Start-Job -ScriptBlock {
            param($Path)
            try {
                $size = (Get-ChildItem -Path $Path -Recurse -ErrorAction SilentlyContinue |
                         Measure-Object -Property Length -Sum).Sum
                return [long]($size -as [long])
            }
            catch {
                return 0
            }
        } -ArgumentList $SharePath

        $result = Wait-Job -Job $job -Timeout $TimeoutSeconds

        if ($null -eq $result) {
            Remove-Job -Job $job -Force
            return @{ Size = 0; Status = 'Timeout' }
        }

        $size = Receive-Job -Job $job
        Remove-Job -Job $job

        return @{ Size = [long]($size -as [long]); Status = 'OK' }
    }
    catch {
        if ($_.Exception.Message -like '*Access Denied*' -or $_.Exception.Message -like '*Access is denied*') {
            return @{ Size = 0; Status = 'Access Denied' }
        }
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

            if ($status -eq 'OK' -or $status -eq 'Partial Access') {
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

    if ($ComputerName.Count -eq 1 -and $ComputerName[0] -match ',') {
        $ComputerName = $ComputerName[0] -split ',' | ForEach-Object { $_.Trim() }
    }

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

    if ($OutputPath -and $allResults.Count -gt 0) {
        $allResults | Export-Csv -Path $OutputPath -NoTypeInformation -Force
        Write-Host "Results exported to: $OutputPath" -ForegroundColor Green
    }
}

# Runs at most once per calendar day, forwards ComputerName into relaunched process if an update is applied
Invoke-UpdateCheck -Config $updateConfig -ForwardedArgs $ComputerName

Invoke-ShareAudit -ComputerName $ComputerName -OutputPath $OutputPath -IncludeSystemShares $IncludeSystemShares

Write-Output ('SCRIPT RUN DATE/TIME: {0}' -f (Get-Date).ToString('yyyy-MM-dd HH:mm:ss'))
