#Copyright (c) 2026 Foowy. Licensed under the MIT License.
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
    Version : 1.1.0
    Author  : Foowy
    GitHub  : https://github.com/Foowy/Server_info

    Requires administrative privileges for accurate remote metrics and reliable access to system classes.
    Target platforms: Windows Server 2008 Non-R2 and later, or any system with PowerShell 3.0+.
    Supports comma-separated server names for polling multiple servers in one invocation.
    Auto-update checks once per day against the GitHub repository (unauthenticated; add a Token key to
    $updateConfig for authenticated access if rate limits become an issue).

#>

$script:Version = '1.1.0'

# Determine if we are already running as admin, elevate if not
if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    # Output what we are doing
    Write-Output 'Restarting with elevated privileges...'
    # Capture the current script directory
    $scriptDir = Split-Path -Path $PSCommandPath -Parent
    # Build the -ComputerName argument string to forward into the elevated session.
    if (-not $ComputerName) {
        $rawInput = Read-Host 'Enter one or more servers (comma-separated, or leave blank for local machine)'
        if ([string]::IsNullOrWhiteSpace($rawInput)) {
            $ComputerName = @($env:COMPUTERNAME)
        } else {
            $ComputerName = $rawInput -split ',' | ForEach-Object { $_.Trim() } | ForEach-Object { if ($_ -eq '.' -or $_ -eq '') { $env:COMPUTERNAME } else { $_ } }
        }
    }
    # Join the array into a single comma-separated string safe for argument passing
    $computerArgument = $ComputerName -join ','
    $psi = [System.Diagnostics.ProcessStartInfo]::new()
    $psi.FileName = 'powershell.exe'
    # Pass -ComputerName into the elevated session so the prompt is never reached again
    $psi.Arguments = "-NoExit -NoProfile -ExecutionPolicy Bypass -Command `"Set-Location '$scriptDir'; & '$PSCommandPath' -ComputerName '$computerArgument'`""
    # Run under administrative privileges
    $psi.Verb = 'runas'
    # Fire off the new elevated PowerShell process
    [System.Diagnostics.Process]::Start($psi) | Out-Null
    # Close the initial non-admin prompt
    exit
}

# Github Auto-Update Configuration
$updateConfig = @{
    Owner     = 'Foowy'
    Repo      = 'Server_info'
    Branch    = 'main'
    FilePath  = 'Get_Server_Info.ps1'
    StampFile = Join-Path (Split-Path $PSCommandPath -Parent) '.GSI_last_update_check'
}

#Auto-Update Function
function Invoke-UpdateCheck {
    param(
        [hashtable]$Config,
        [string[]]$ForwardedArgs
    )
    # Only run the check once per calendar day
    $checkNeeded = $true
    if (Test-Path $Config.StampFile) {
        try {
            $lastCheck = Get-Content $Config.StampFile -Raw | Get-Date
            if ($lastCheck.Date -eq (Get-Date).Date) {
                $checkNeeded = $false
            }
        }
        catch {
            # Stamp file is corrupt or unreadable, proceed with check
        }
    }

    if (-not $checkNeeded) {
        Write-Verbose 'Update check skipped — already ran today.'
        return
    }

    Write-Host 'Checking for script updates...' -ForegroundColor Cyan

    # Build the raw content API URL
    $apiUrl = "https://api.github.com/repos/$($Config.Owner)/$($Config.Repo)/contents/$($Config.FilePath)?ref=$($Config.Branch)"

    $headers = @{
        Accept       = 'application/vnd.github.v3.raw'
        'User-Agent' = 'PowerShell-AutoUpdater'
    }

    # Force TLS 1.2 — required by GitHub, older PowerShell sessions default to TLS 1.0
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

    try {
        $remoteContent = Invoke-RestMethod -Uri $apiUrl -Headers $headers -ErrorAction Stop
    }
    catch {
        Write-Warning "Update check failed — could not reach GitHub: $_"
        # Write stamp anyway to avoid hammering GitHub on every run when offline or unable to reach Github
        (Get-Date).ToString('o') | Set-Content $Config.StampFile
        return
    }

    # Compare remote to local using SHA256 hash
    $localContent = Get-Content -Path $PSCommandPath -Raw
    $sha = [System.Security.Cryptography.SHA256]::Create()
    $localHash = ($sha.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($localContent)) | ForEach-Object { $_.ToString('x2') }) -join ''
    $remoteHash = ($sha.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($remoteContent)) | ForEach-Object { $_.ToString('x2') }) -join ''
    $sha.Dispose()

    # Write the stamp regardless of whether an update was found, again to prevent hammering and also annoying user
    (Get-Date).ToString('o') | Set-Content $Config.StampFile

    if ($localHash -eq $remoteHash) {
        Write-Host 'Script is up to date.' -ForegroundColor Green
        return
    }

    # Update found — back up current version and write new one
    Write-Host 'Update available — applying update...' -ForegroundColor Yellow

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

    # Relaunch updated script, forwarding any already-collected arguments
    $scriptDir = Split-Path $PSCommandPath -Parent
    $argString = if ($ForwardedArgs) { "-ComputerName '$($ForwardedArgs -join ',')'" } else { '' }
    $psi = [System.Diagnostics.ProcessStartInfo]::new()
    $psi.FileName = 'powershell.exe'
    $psi.Arguments = "-NoExit -NoProfile -ExecutionPolicy Bypass -Command `"Set-Location '$scriptDir'; & '$PSCommandPath' $argString`""
    $psi.Verb = 'runas'
    $psi.UseShellExecute = $true

    [System.Diagnostics.Process]::Start($psi) | Out-Null

    # Explicitly kill the current process to force the window to be closed
    [System.Diagnostics.Process]::GetCurrentProcess().Kill()
}

# Runs at most once per calendar day, forwards ComputerName into relaunched process if an update is applied
Invoke-UpdateCheck -Config $updateConfig -ForwardedArgs $ComputerName

# Elevation and update relaunches pass multiple servers as a comma-joined single string — re-split it here
if ($ComputerName.Count -eq 1 -and $ComputerName[0] -match ',') {
    $ComputerName = $ComputerName[0] -split ',' | ForEach-Object { $_.Trim() }
}

# At this point we are elevated and up to date. If ComputerName was not passed as a parameter, prompt for it now.
if (-not $ComputerName) {
    $rawInput = Read-Host 'Enter one or more servers (comma-separated, or leave blank for local machine)'
    if ([string]::IsNullOrWhiteSpace($rawInput)) {
        $ComputerName = @($env:COMPUTERNAME)
    } else {
        $ComputerName = $rawInput -split ',' | ForEach-Object { $_.Trim() } | ForEach-Object { if ($_ -eq '.' -or $_ -eq '') { $env:COMPUTERNAME } else { $_ } }
    }
}

# Normalize any remaining blank or dot entries that may have arrived via the -ComputerName parameter directly.
$ComputerName = @($ComputerName | ForEach-Object { if ([string]::IsNullOrWhiteSpace($_) -or $_ -eq '.') { $env:COMPUTERNAME } else { $_ } })

# Make sure tasks can safely run and capture any errors without stopping the entire script.
function Start-SafeTask {
    param([scriptblock]$Task)
    try { & $Task }
    catch {
        [PSCustomObject]@{ Error = $_.Exception.Message }
    }
}

# If a reusable CimSession is available, it uses CIM. If CIM fails, it falls back to legacy WMI.
function Get-SafeCimOrWmi {
    param(
        [string]$Class,
        [string]$ComputerName,
        [string]$Filter = $null,
        [CimSession]$CimSession
    )

    # Trying CIM first as this is the preferred remote retrieval process
    if ($CimSession) {
        try {
            return Get-CimInstance -ClassName $Class -Namespace root/cimv2 -Filter $Filter -CimSession $CimSession -ErrorAction Stop
        }
        catch {
            # Throw this catch empty on purpose to null out any errors from printing to console (if Cim doesn't work, we don't want to see it since moving onto WMI as fallback)
        }
    }

    # WMI fallback - If at first CIM doesn't succeed, try... try... something older instead
    try {
        if ($Filter) {
            return Get-WmiObject -Class $Class -Filter $Filter -ComputerName $ComputerName -ErrorAction Stop
        }
        else {
            return Get-WmiObject -Class $Class -ComputerName $ComputerName -ErrorAction Stop
        }
    }
    catch {
        return $null
    }
}

# Function to convert GB to TB if over 1024 for cleanlier output
function Convert-GB {
    param([double]$GB)
    if ($GB -ge 1024) {
        $TB = [math]::Round($GB / 1024, 2)
        return "$TB TB"
    }
    else {
        return "$([math]::Round($GB, 2)) GB"
    }
}

# Function to get Memory Metrics, return as GB values with percentage used
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
        $freeKB = [double]$os.FreePhysicalMemory
        $usedKB = $totalKB - $freeKB
        [PSCustomObject]@{
            TotalGB = [math]::Round($totalKB / 1MB, 2)
            UsedGB  = [math]::Round($usedKB / 1MB, 2)
            FreeGB  = [math]::Round($freeKB / 1MB, 2)
            Percent = [math]::Round(($usedKB / $totalKB) * 100, 1)
            Error   = $null
        }
    }
    catch {
        Write-Warning "${ComputerName}: Memory metric failed - $($_.Exception.Message)"
        [PSCustomObject]@{
            TotalGB = 0; UsedGB = 0; FreeGB = 0; Percent = 0; Error = $_.Exception.Message
        }
    }
}

# Function to get Storage Metrics, return total/used/free in GB with percentage used, also return breakdown of each drive for detailed section
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

        $total = ($drives.Size | Measure-Object -Sum).Sum
        $free = ($drives.FreeSpace | Measure-Object -Sum).Sum
        $used = $total - $free
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
            UsedGB  = [math]::Round($used / 1GB, 2)
            FreeGB  = [math]::Round($free / 1GB, 2)
            Percent = if ($total -gt 0) { [math]::Round(($used / $total) * 100, 2) } else { 0 }
            Drives  = $details
            Error   = $null
        }
    }
    catch {
        Write-Warning "${ComputerName}: Storage metric failed - $($_.Exception.Message)"
        [PSCustomObject]@{
            TotalGB = 0; UsedGB = 0; FreeGB = 0; Percent = 0; Drives = @(); Error = $_.Exception.Message
        }
    }
}

# Function to get CPU Load Percentage, return as single average value for entire server
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

        [PSCustomObject]@{
            Load  = [math]::Round(($cpu.LoadPercentage | Measure-Object -Average).Average, 1)
            Error = $null
        }
    }
    catch {
        Write-Warning "${ComputerName}: CPU metric failed - $($_.Exception.Message)"
        [PSCustomObject]@{ Load = 0; Error = $_.Exception.Message }
    }
}

# Function to get Uptime of server, return as days/hours/minutes/seconds with last boot time for reference.
function Get-UptimeMetric {
    param(
        [string]$ComputerName,
        [CimSession]$CimSession,
        $RemoteData
    )
    try {
        if ($RemoteData) {
            $sys = $RemoteData.Perf
        }
        else {
            $sys = Get-SafeCimOrWmi -Class 'Win32_PerfFormattedData_PerfOS_System' -ComputerName $ComputerName -CimSession $CimSession
        }

        if (-not $sys) { throw 'Unable to retrieve uptime data.' }

        $uptimeSec = [int]$sys.SystemUpTime
        $uptime = New-TimeSpan -Seconds $uptimeSec
        $lastBoot = (Get-Date).AddSeconds(-$uptimeSec)
        [PSCustomObject]@{
            LastBoot = $lastBoot
            Days     = $uptime.Days
            Hours    = $uptime.Hours
            Minutes  = $uptime.Minutes
            Seconds  = $uptime.Seconds
        }
    }
    catch {
        [PSCustomObject]@{
            LastBoot = $null
            Days     = 0
            Hours    = 0
            Minutes  = 0
            Seconds  = 0
        }
    }
}

# Function to get Network Information, return IP address (IPv4), subnet, DNS servers, and MAC address for each network adapter. Handle multiple adapters and multiple IPs per adapter gracefully in output
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
        elseif ($ComputerName -eq $env:COMPUTERNAME) {
            $adapters = Get-CimInstance -ClassName Win32_NetworkAdapterConfiguration -Filter 'IPEnabled = True' -ErrorAction Stop
        }
        else {
            $adapters = Get-SafeCimOrWmi -Class 'Win32_NetworkAdapterConfiguration' -Filter 'IPEnabled = True' -ComputerName $ComputerName -CimSession $CimSession
        }

        if (-not $adapters) { throw 'Unable to retrieve network data.' }

        $output = foreach ($a in $adapters) {
            $ipv4 = $a.IPAddress | Where-Object { $_ -match '\d+\.\d+\.\d+\.\d+' }
            [PSCustomObject]@{
                Adapter   = $a.Description
                IPAddress = ($ipv4 -join ', ')
                Subnet    = ($a.IPSubnet -join ', ')
                DNS       = ($a.DNSServerSearchOrder -join ', ')
                MAC       = $a.MACAddress
                Error     = $null
            }
        }
        return $output
    }
    catch {
        Write-Warning "${ComputerName}: Network metric failed - $($_.Exception.Message)"
        [PSCustomObject]@{
            Adapter   = 'N/A'
            IPAddress = 'N/A'
            Subnet    = 'N/A'
            DNS       = 'N/A'
            MAC       = 'N/A'
            Error     = $_.Exception.Message
        }
    }
}

# Test whether PowerShell remoting is available on the target host.
# If WinRM is configured, this path allows us to gather multiple metrics in one remote invocation.
function Test-PowerShellRemoting {
    param([string]$ComputerName)
    try {
        Test-WSMan -ComputerName $ComputerName -ErrorAction Stop | Out-Null
        return $true
    }
    catch {
        return $false
    }
}

# Create a reusable CIM session for remote queries when remoting is not available.
# This avoids opening separate remote connections for each individual metric call.
function New-RemoteCimSession {
    param([string]$ComputerName)
    if ($ComputerName -eq $env:COMPUTERNAME) { return $null }
    try {
        return New-CimSession -ComputerName $ComputerName -SessionOption (New-CimSessionOption -OperationTimeoutSec 30) -ErrorAction Stop
    }
    catch {
        return $null
    }
}

# Gather all metrics in a single remote execution using PowerShell remoting.
# This provides a faster path when the target supports WinRM and remoting is enabled.
function Get-RemoteMetricsViaRemoting {
    param([string]$ComputerName)
    try {
        return Invoke-Command -ComputerName $ComputerName -ScriptBlock {
            $os = Get-CimInstance Win32_OperatingSystem -ErrorAction Stop | Select-Object TotalVisibleMemorySize, FreePhysicalMemory, LastBootUpTime
            $cpu = Get-CimInstance Win32_Processor -ErrorAction Stop | Select-Object LoadPercentage
            $drives = Get-CimInstance Win32_LogicalDisk -Filter 'DriveType=3' -ErrorAction Stop | Select-Object DeviceID, VolumeName, Size, FreeSpace
            $sys = Get-CimInstance Win32_PerfFormattedData_PerfOS_System -ErrorAction Stop | Select-Object SystemUpTime
            $network = Get-CimInstance Win32_NetworkAdapterConfiguration -Filter 'IPEnabled = True' -ErrorAction Stop | Select-Object Description, IPAddress, IPSubnet, DNSServerSearchOrder, MACAddress
            [PSCustomObject]@{
                OS      = $os
                CPU     = $cpu
                Drives  = $drives
                Perf    = $sys
                Network = $network
            }
        } -ErrorAction Stop
    }
    catch {
        return $null
    }
}

# Gather all metrics locally using direct CIM calls, returning the same structure as Get-RemoteMetricsViaRemoting.
function Get-LocalMetrics {
    try {
        $os      = Get-CimInstance Win32_OperatingSystem                                        -ErrorAction Stop | Select-Object TotalVisibleMemorySize, FreePhysicalMemory, LastBootUpTime
        $cpu     = Get-CimInstance Win32_Processor                                              -ErrorAction Stop | Select-Object LoadPercentage
        $drives  = Get-CimInstance Win32_LogicalDisk -Filter 'DriveType=3'                     -ErrorAction Stop | Select-Object DeviceID, VolumeName, Size, FreeSpace
        $sys     = Get-CimInstance Win32_PerfFormattedData_PerfOS_System                       -ErrorAction Stop | Select-Object SystemUpTime
        $network = Get-CimInstance Win32_NetworkAdapterConfiguration -Filter 'IPEnabled = True' -ErrorAction Stop | Select-Object Description, IPAddress, IPSubnet, DNSServerSearchOrder, MACAddress
        [PSCustomObject]@{ OS = $os; CPU = $cpu; Drives = $drives; Perf = $sys; Network = $network }
    }
    catch {
        return $null
    }
}

# Main function to collect server metrics and build output, handle progress display, server target, and overall flow of script
function Get-ServerMetric {
    param([string]$ComputerName)

    $progressId = Get-Random
    Write-Progress -Id $progressId -Activity 'Collecting server metrics' -Status 'Starting tasks' -PercentComplete 0

    # Use local CIM for local machine; otherwise attempt remoting, then fall back to a CIM session
    $isLocal = $ComputerName -eq $env:COMPUTERNAME -or $ComputerName -eq 'localhost' -or $ComputerName -eq '.'
    $remoteData = $null
    if ($isLocal) {
        $remoteData = Get-LocalMetrics
    }
    elseif (Test-PowerShellRemoting -ComputerName $ComputerName) {
        $remoteData = Get-RemoteMetricsViaRemoting -ComputerName $ComputerName
        if (-not $remoteData) {
            Write-Warning "PowerShell remoting to $ComputerName failed. Falling back to CIM/WMI."
        }
    }

    # Create CIM session only for remote targets when remoting is unavailable
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

    $summaryHeader = '{0,-22} | {1,10} | {2,-22} | {3,-26} | {4,-16}' -f 
    'Computer Name', 'CPU Load', 'Memory Used', 'Overall Storage Used', 'Uptime'
    $summaryDivider = ('-' * $summaryHeader.Length)
    $summaryLine = '{0,-22} | {1,10} | {2,-22} | {3,-26} | {4,-16}' -f
    $ComputerName,
    ('{0,5}' -f $results.CPU.Load),
    "$($results.Memory.UsedGB) GB ($($results.Memory.Percent)%)",
    ('{0} ({1}%)' -f (Convert-GB $results.Storage.UsedGB), $results.Storage.Percent),
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
    Write-Output ('  Percent : {0} %' -f $results.Memory.Percent)
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
