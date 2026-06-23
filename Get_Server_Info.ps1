# Copyright (c) 2026 Foowy. Licensed under the MIT License.
#Requires -Version 3.0

param(
    [string[]]$ComputerName
)

<#
.SYNOPSIS
    Collect server uptime, memory, disk, CPU, and network information for local or remote Windows servers.

.DESCRIPTION
    This script gathers key system metrics from one or more target servers and formats the values for easy
    copy/paste into support notes. It supports PowerShell remoting (WinRM) where available and falls back
    to a reusable CIM session with WMI-style queries. Output includes a summary table, uptime breakdown,
    memory detail, drive storage detail, and network adapter information.

.PARAMETER ComputerName
    One or more server names to query. Accepts a comma-separated list. If omitted, the user is prompted.
    Enter a blank value or '.' to target the local computer.

.EXAMPLE
    .\Get_Server_Info.ps1 -ComputerName Server01

.EXAMPLE
    .\Get_Server_Info.ps1 -ComputerName Server01,Server02,Server03

.EXAMPLE
    .\Get_Server_Info.ps1

.INPUTS
    None. This script does not accept pipeline input.

.OUTPUTS
    PSCustomObject. A custom object with properties: TotalGB, UsedGB, FreeGB, Percent, Error (from each metric function).
    The final output is formatted as a text summary displayed to the console.

.NOTES
    Version : 1.3.0
    Author  : Foowy
    GitHub  : https://github.com/Foowy/Server_info

    Requires administrative privileges for accurate remote metrics and reliable access to system classes.
    Target platforms: Windows Server 2008 and later, or any system with PowerShell 3.0+.
    Supports comma-separated server names for polling multiple servers in one invocation.
    Auto-update checks once per day against the GitHub repository using the git blob SHA for comparison.

#>

$script:Version   = '1.3.0'
$script:ScriptDir = Split-Path -Path $PSCommandPath -Parent

# Shared CIM collection block -- used by both local and remote paths to avoid duplicating the five queries
$script:MetricsBlock = {
    $os      = Get-CimInstance Win32_OperatingSystem                                        -ErrorAction Stop | Select-Object TotalVisibleMemorySize, FreePhysicalMemory, LastBootUpTime
    $cpu     = Get-CimInstance Win32_Processor                                              -ErrorAction Stop | Select-Object LoadPercentage
    $drives  = Get-CimInstance Win32_LogicalDisk -Filter 'DriveType=3'                     -ErrorAction Stop | Select-Object DeviceID, VolumeName, Size, FreeSpace
    $sys     = Get-CimInstance Win32_PerfFormattedData_PerfOS_System                       -ErrorAction Stop | Select-Object SystemUpTime
    $network = Get-CimInstance Win32_NetworkAdapterConfiguration -Filter 'IPEnabled = True' -ErrorAction Stop | Select-Object Description, IPAddress, IPSubnet, DNSServerSearchOrder, MACAddress
    [PSCustomObject]@{ OS = $os; CPU = $cpu; Drives = $drives; Perf = $sys; Network = $network }
}

function Read-ComputerNames {
    $rawInput = Read-Host 'Enter one or more servers (comma-separated, or leave blank for local machine)'
    if ([string]::IsNullOrWhiteSpace($rawInput)) { return @($env:COMPUTERNAME) }
    $rawInput -split ',' | ForEach-Object { $s = $_.Trim(); if ($s -eq '.' -or $s -eq '') { $env:COMPUTERNAME } else { $s } }
}

function Start-ElevatedScript {
    param([string]$ScriptPath, [string]$ArgString = '')
    $dir = Split-Path $ScriptPath -Parent
    $psi = [System.Diagnostics.ProcessStartInfo]::new()
    $psi.FileName        = [System.Diagnostics.Process]::GetCurrentProcess().MainModule.FileName
    $psi.Arguments       = "-NoExit -NoProfile -ExecutionPolicy Bypass -Command `"Set-Location '$dir'; & '$ScriptPath' $ArgString`""
    $psi.Verb            = 'runas'
    $psi.UseShellExecute = $true
    return [System.Diagnostics.Process]::Start($psi)
}

if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Output 'Restarting with elevated privileges...'
    if (-not $ComputerName) { $ComputerName = Read-ComputerNames }
    # Pass collected names comma-joined so the elevated session skips the prompt
    $newProc = Start-ElevatedScript -ScriptPath $PSCommandPath -ArgString "-ComputerName '$($ComputerName -join ',')'"
    if (-not $newProc) { Write-Warning 'Failed to launch elevated session.' }
    exit
}

# GitHub Auto-Update Configuration
$updateConfig = @{
    Owner     = 'Foowy'
    Repo      = 'Server_info'
    Branch    = 'main'
    FilePath  = 'Get_Server_Info.ps1'
    StampFile = Join-Path $script:ScriptDir '.GSI_last_update_check'
}

function Invoke-UpdateCheck {
    param(
        [hashtable]$Config,
        [string[]]$ForwardedArgs
    )
    $checkNeeded = $true
    if (Test-Path $Config.StampFile) {
        try {
            $today     = (Get-Date).Date
            $lastCheck = Get-Content $Config.StampFile -Raw | Get-Date
            if ($lastCheck.Date -eq $today) { $checkNeeded = $false }
        }
        catch {
            # Stamp file is corrupt or unreadable; proceed with check
        }
    }

    if (-not $checkNeeded) {
        Write-Verbose 'Update check skipped -- already ran today.'
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
        Write-Warning "Update check failed -- could not reach GitHub: $_"
        # Write stamp to avoid re-checking on every run when offline
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

    # Write stamp regardless of result to prevent re-checking on every run
    (Get-Date).ToString('o') | Set-Content $Config.StampFile

    if ($localSha -eq $response.sha) {
        Write-Host 'Script is up to date.' -ForegroundColor Green
        return
    }

    Write-Host 'Update available -- applying update...' -ForegroundColor Yellow

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

    $argString = if ($ForwardedArgs) { "-ComputerName '$($ForwardedArgs -join ',')'" } else { '' }
    $newProc   = Start-ElevatedScript -ScriptPath $PSCommandPath -ArgString $argString
    if ($newProc) {
        # Kill current process to close this window after launching the updated version
        [System.Diagnostics.Process]::GetCurrentProcess().Kill()
    } else {
        Write-Warning 'Relaunch failed -- continuing with the updated script in this window.'
    }
}

# Runs at most once per calendar day, forwards ComputerName into relaunched process if an update is applied
Invoke-UpdateCheck -Config $updateConfig -ForwardedArgs $ComputerName

# Always re-split every element -- elevation and update relaunches join with commas, and users may too
$ComputerName = @($ComputerName | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ -ne '' })

if (-not $ComputerName) {
    $ComputerName = Read-ComputerNames
}

# Normalize any remaining blank or dot entries that may have arrived via the -ComputerName parameter directly
$ComputerName = @($ComputerName | ForEach-Object { if ([string]::IsNullOrWhiteSpace($_) -or $_ -eq '.') { $env:COMPUTERNAME } else { $_ } })

function Start-SafeTask {
    param([scriptblock]$Task)
    try { & $Task }
    catch { [PSCustomObject]@{ Error = $_.Exception.Message } }
}

function Get-SafeCimOrWmi {
    param(
        [string]$Class,
        [string]$ComputerName,
        [string]$Filter = $null,
        [CimSession]$CimSession
    )

    if ($CimSession) {
        try {
            $cimParams = @{ ClassName = $Class; Namespace = 'root/cimv2'; CimSession = $CimSession; ErrorAction = 'Stop' }
            if ($Filter) { $cimParams.Filter = $Filter }
            return Get-CimInstance @cimParams
        }
        catch {
            # Intentionally empty -- suppress the CIM error and fall through to the WMI fallback
        }
    }

    # WMI fallback -- used when CIM over WS-Management is unavailable (e.g. older targets)
    try {
        $wmiParams = @{ Class = $Class; ComputerName = $ComputerName; ErrorAction = 'Stop' }
        if ($Filter) { $wmiParams.Filter = $Filter }
        return Get-WmiObject @wmiParams
    }
    catch {
        return $null
    }
}

function Convert-GB {
    param([double]$GB)
    if ($GB -ge 1024) { return "$([math]::Round($GB / 1024, 2)) TB" }
    return "$([math]::Round($GB, 2)) GB"
}


function Get-MemoryMetric {
    param(
        [string]$ComputerName,
        [CimSession]$CimSession,
        $RemoteData
    )
    try {
        if ($RemoteData) {
            $os = $RemoteData.OS | Select-Object -First 1
        }
        else {
            $os = Get-SafeCimOrWmi -Class 'Win32_OperatingSystem' -ComputerName $ComputerName -CimSession $CimSession
        }

        if (-not $os) { throw 'Unable to retrieve memory data.' }

        $totalKB = [double]$os.TotalVisibleMemorySize
        $freeKB  = [double]$os.FreePhysicalMemory
        $usedKB  = $totalKB - $freeKB
        [PSCustomObject]@{
            TotalGB = [math]::Round($totalKB / 1MB, 2)
            UsedGB  = [math]::Round($usedKB  / 1MB, 2)
            FreeGB  = [math]::Round($freeKB  / 1MB, 2)
            Percent = if ($totalKB -gt 0) { [math]::Round(($usedKB / $totalKB) * 100, 1) } else { 0 }
            Error   = $null
        }
    }
    catch {
        Write-Warning "${ComputerName}: Memory metric failed - $($_.Exception.Message)"
        [PSCustomObject]@{ TotalGB = 0; UsedGB = 0; FreeGB = 0; Percent = 0; Error = $_.Exception.Message }
    }
}

function Get-StorageMetric {
    param(
        [string]$ComputerName,
        [CimSession]$CimSession,
        $RemoteData
    )
    try {
        if ($RemoteData) {
            $drives = $RemoteData.Drives
        }
        else {
            $drives = Get-SafeCimOrWmi -Class 'Win32_LogicalDisk' -Filter 'DriveType=3' -ComputerName $ComputerName -CimSession $CimSession
        }

        if (-not $drives) { throw 'Unable to retrieve storage data.' }

        $total = ($drives.Size      | Measure-Object -Sum).Sum
        $free  = ($drives.FreeSpace | Measure-Object -Sum).Sum
        $used  = $total - $free
        $details = foreach ($d in $drives) {
            [PSCustomObject]@{
                Drive   = $d.DeviceID
                Label   = $d.VolumeName
                UsedGB  = [math]::Round(($d.Size - $d.FreeSpace) / 1GB, 2)
                FreeGB  = [math]::Round($d.FreeSpace / 1GB, 2)
                TotalGB = [math]::Round($d.Size / 1GB, 2)
                Percent = if ($d.Size -gt 0) { [math]::Round((($d.Size - $d.FreeSpace) / $d.Size) * 100, 2) } else { 0 }
            }
        }
        [PSCustomObject]@{
            TotalGB = [math]::Round($total / 1GB, 2)
            UsedGB  = [math]::Round($used  / 1GB, 2)
            FreeGB  = [math]::Round($free  / 1GB, 2)
            Percent = if ($total -gt 0) { [math]::Round(($used / $total) * 100, 2) } else { 0 }
            Drives  = $details
            Error   = $null
        }
    }
    catch {
        Write-Warning "${ComputerName}: Storage metric failed - $($_.Exception.Message)"
        [PSCustomObject]@{ TotalGB = 0; UsedGB = 0; FreeGB = 0; Percent = 0; Drives = @(); Error = $_.Exception.Message }
    }
}

function Get-CpuMetric {
    param(
        [string]$ComputerName,
        [CimSession]$CimSession,
        $RemoteData
    )
    try {
        if ($RemoteData) {
            $cpu = $RemoteData.CPU
        }
        else {
            $cpu = Get-SafeCimOrWmi -Class 'Win32_Processor' -ComputerName $ComputerName -CimSession $CimSession
        }

        if (-not $cpu) { throw 'Unable to retrieve CPU data.' }

        $avg = ($cpu.LoadPercentage | Measure-Object -Average).Average
        if ($null -eq $avg) { throw 'CPU LoadPercentage unavailable (all sockets returned null).' }
        [PSCustomObject]@{
            Load  = [math]::Round($avg, 1)
            Error = $null
        }
    }
    catch {
        Write-Warning "${ComputerName}: CPU metric failed - $($_.Exception.Message)"
        [PSCustomObject]@{ Load = 0; Error = $_.Exception.Message }
    }
}

function Get-UptimeMetric {
    param(
        [string]$ComputerName,
        [CimSession]$CimSession,
        $RemoteData
    )
    try {
        # Prefer LastBootUpTime from Win32_OperatingSystem -- already collected, reliable on all targets
        # Fall back to Win32_PerfFormattedData_PerfOS_System.SystemUpTime (unavailable when perf subsystem is off)
        $lastBoot = $null
        if ($RemoteData) {
            $os = $RemoteData.OS | Select-Object -First 1
            if ($os -and $os.LastBootUpTime) { $lastBoot = [datetime]$os.LastBootUpTime }
        }
        else {
            $os = Get-SafeCimOrWmi -Class 'Win32_OperatingSystem' -ComputerName $ComputerName -CimSession $CimSession
            if ($os -and $os.LastBootUpTime) { $lastBoot = [datetime]($os | Select-Object -First 1).LastBootUpTime }
        }

        if (-not $lastBoot) {
            if ($RemoteData) { $sys = $RemoteData.Perf }
            else { $sys = Get-SafeCimOrWmi -Class 'Win32_PerfFormattedData_PerfOS_System' -ComputerName $ComputerName -CimSession $CimSession }
            if (-not $sys -or [long]$sys.SystemUpTime -eq 0) { throw 'Unable to retrieve uptime data.' }
            $lastBoot = (Get-Date).AddSeconds(-[long]$sys.SystemUpTime)
        }

        $uptime = (Get-Date) - [datetime]$lastBoot
        [PSCustomObject]@{
            LastBoot = $lastBoot
            Days     = [int]$uptime.Days
            Hours    = [int]$uptime.Hours
            Minutes  = [int]$uptime.Minutes
            Seconds  = [int]$uptime.Seconds
        }
    }
    catch {
        Write-Warning "${ComputerName}: Uptime metric failed - $($_.Exception.Message)"
        [PSCustomObject]@{ LastBoot = $null; Days = 0; Hours = 0; Minutes = 0; Seconds = 0 }
    }
}

function Get-NetworkInfo {
    param(
        [string]$ComputerName,
        [CimSession]$CimSession,
        $RemoteData
    )
    try {
        if ($RemoteData) {
            $adapters = $RemoteData.Network
        }
        else {
            $adapters = Get-SafeCimOrWmi -Class 'Win32_NetworkAdapterConfiguration' -Filter 'IPEnabled = True' -ComputerName $ComputerName -CimSession $CimSession
        }

        if (-not $adapters) { throw 'Unable to retrieve network data.' }

        $output = foreach ($a in $adapters) {
            $ipv4       = $a.IPAddress | Where-Object { $_ -match '^\d{1,3}(\.\d{1,3}){3}$' }
            # IPSubnet mixes IPv4 masks (e.g. "255.255.255.0") and IPv6 prefix lengths (e.g. "64") -- keep only IPv4 masks
            $ipv4Subnet = $a.IPSubnet | Where-Object { $_ -match '^\d{1,3}(\.\d{1,3}){3}$' }
            [PSCustomObject]@{
                Adapter   = $a.Description
                IPAddress = ($ipv4       -join ', ')
                Subnet    = ($ipv4Subnet -join ', ')
                DNS       = ($a.DNSServerSearchOrder -join ', ')
                MAC       = $a.MACAddress
                Error     = $null
            }
        }
        return $output
    }
    catch {
        Write-Warning "${ComputerName}: Network metric failed - $($_.Exception.Message)"
        [PSCustomObject]@{ Adapter = 'N/A'; IPAddress = 'N/A'; Subnet = 'N/A'; DNS = 'N/A'; MAC = 'N/A'; Error = $_.Exception.Message }
    }
}

# Test-WSMan probe -- if WinRM responds, all metrics can be gathered in a single Invoke-Command round-trip
function Test-PowerShellRemoting {
    param([string]$ComputerName)
    # Run in a job with a 5s timeout -- packet-dropping firewalls can stall Test-WSMan for 21s+ per host
    $job  = Start-Job -ScriptBlock { param($c) Test-WSMan -ComputerName $c -ErrorAction Stop } -ArgumentList $ComputerName
    $done = Wait-Job -Job $job -Timeout 5
    if ($null -eq $done) { Remove-Job -Job $job -Force; return $false }
    $ok = $true
    try { Receive-Job -Job $job -ErrorAction Stop | Out-Null } catch { $ok = $false }
    Remove-Job -Job $job -ErrorAction SilentlyContinue
    return $ok
}

# Opens a persistent CIM session to avoid a separate connection per metric call when remoting is unavailable
function New-RemoteCimSession {
    param([string]$ComputerName)
    if ($ComputerName -eq $env:COMPUTERNAME) { return $null }
    try {
        return New-CimSession -ComputerName $ComputerName -SessionOption (New-CimSessionOption -OperationTimeoutSec 30) -ErrorAction Stop
    }
    catch { return $null }
}

# Single Invoke-Command round-trip when the target supports WinRM -- faster than per-metric CIM/WMI calls
function Get-RemoteMetricsViaRemoting {
    param([string]$ComputerName)
    try { Invoke-Command -ComputerName $ComputerName -ScriptBlock $script:MetricsBlock -ErrorAction Stop }
    catch { $null }
}

function Get-LocalMetrics {
    try { & $script:MetricsBlock }
    catch { $null }
}

function Get-ServerMetric {
    param([string]$ComputerName)

    $progressId = Get-Random
    Write-Progress -Id $progressId -Activity 'Collecting server metrics' -Status 'Starting tasks' -PercentComplete 0

    # Local: direct CIM; remote: try WinRM first, fall back to per-metric CIM session
    $isLocal    = $ComputerName -eq $env:COMPUTERNAME -or $ComputerName -eq 'localhost' -or $ComputerName -eq '.'
    $remoteData = $null
    if ($isLocal) {
        $remoteData = Get-LocalMetrics
    }
    elseif (Test-PowerShellRemoting -ComputerName $ComputerName) {
        $remoteData = Get-RemoteMetricsViaRemoting -ComputerName $ComputerName
        if (-not $remoteData) { Write-Warning "PowerShell remoting to $ComputerName failed. Falling back to CIM/WMI." }
    }

    $cimSession = if (-not $isLocal -and -not $remoteData) { New-RemoteCimSession -ComputerName $ComputerName } else { $null }

    $tasks = [ordered]@{
        Memory  = { Get-MemoryMetric  -ComputerName $ComputerName -CimSession $cimSession -RemoteData $remoteData }
        Storage = { Get-StorageMetric -ComputerName $ComputerName -CimSession $cimSession -RemoteData $remoteData }
        CPU     = { Get-CpuMetric     -ComputerName $ComputerName -CimSession $cimSession -RemoteData $remoteData }
        Uptime  = { Get-UptimeMetric  -ComputerName $ComputerName -CimSession $cimSession -RemoteData $remoteData }
        Network = { Get-NetworkInfo   -ComputerName $ComputerName -CimSession $cimSession -RemoteData $remoteData }
    }

    $results = @{}
    $i = 0

    foreach ($key in $tasks.Keys) {
        $i++
        $pct = [math]::Round(($i / $tasks.Count) * 100, 0)
        Write-Progress -Id $progressId -Activity 'Collecting server metrics' -Status "Running task $i of $($tasks.Count) ($key)" -PercentComplete $pct
        $results[$key] = Start-SafeTask $tasks[$key]
    }

    Write-Progress -Id $progressId -Activity 'Collecting server metrics' -Completed

    if ($cimSession) { Remove-CimSession $cimSession }

    $summaryHeader = '{0,-22} | {1,10} | {2,-22} | {3,-26} | {4,-16}' -f `
        'Computer Name', 'CPU Load', 'Memory Used', 'Overall Storage Used', 'Uptime'
    $summaryDivider = '-' * $summaryHeader.Length
    $summaryLine = '{0,-22} | {1,10} | {2,-22} | {3,-26} | {4,-16}' -f `
        $ComputerName, `
        ('{0,4}%' -f $results.CPU.Load), `
        "$($results.Memory.UsedGB) GB ($($results.Memory.Percent)%)", `
        ('{0} ({1}%)' -f (Convert-GB $results.Storage.UsedGB), $results.Storage.Percent), `
        ('{0}d {1}h {2}m {3}s' -f $results.Uptime.Days, $results.Uptime.Hours, $results.Uptime.Minutes, $results.Uptime.Seconds)

    Write-Output ''
    Write-Output '================================= SERVER SUMMARY ================================='
    Write-Output $summaryDivider
    Write-Output $summaryHeader
    Write-Output $summaryDivider
    Write-Output $summaryLine
    Write-Output $summaryDivider
    Write-Output ''

    Write-Output 'UPTIME BREAKDOWN:'
    $lastBoot = if ($results.Uptime.LastBoot) { $results.Uptime.LastBoot.ToString('MM/dd/yyyy HH:mm:ss') } else { 'N/A' }
    Write-Output ('  Last Boot: {0}' -f $lastBoot)
    Write-Output ('  Days     : {0}' -f $results.Uptime.Days)
    Write-Output ('  Hours    : {0}' -f $results.Uptime.Hours)
    Write-Output ('  Minutes  : {0}' -f $results.Uptime.Minutes)
    Write-Output ''

    Write-Output 'MEMORY BREAKDOWN:'
    Write-Output ('  Total   : {0} GB' -f $results.Memory.TotalGB)
    Write-Output ('  Used    : {0} GB' -f $results.Memory.UsedGB)
    Write-Output ('  Free    : {0} GB' -f $results.Memory.FreeGB)
    Write-Output ('  Percent : {0} %'  -f $results.Memory.Percent)
    Write-Output ''

    Write-Output 'STORAGE BREAKDOWN:'
    foreach ($d in $results.Storage.Drives) {
        $label = if ($d.Label) { $d.Label } else { 'No Label' }
        Write-Output ('Drive {0} ({1})' -f $d.Drive, $label)
        Write-Output ('  Used : {0}' -f (Convert-GB $d.UsedGB))
        Write-Output ('  Free : {0}' -f (Convert-GB $d.FreeGB))
        Write-Output ('  Total: {0}' -f (Convert-GB $d.TotalGB))
        Write-Output ('  Util : {0}%' -f $d.Percent)
        Write-Output ''
    }

    Write-Output 'NETWORK INFORMATION:'
    foreach ($n in $results.Network) {
        Write-Output ('Adapter : {0}' -f $n.Adapter)
        Write-Output ('IP Addr : {0}' -f $n.IPAddress)
        Write-Output ('Subnet  : {0}' -f $n.Subnet)
        Write-Output ('DNS     : {0}' -f $n.DNS)
        Write-Output ('MAC     : {0}' -f $n.MAC)
        Write-Output ''
    }

    Write-Output ('SCRIPT RUN DATE/TIME: {0}' -f (Get-Date).ToString('yyyy-MM-dd HH:mm:ss'))
    Write-Output ''
}

foreach ($c in $ComputerName) {
    Write-Host "`n==================== Processing $c ====================`n" -ForegroundColor Cyan
    Get-ServerMetric -ComputerName $c
}
