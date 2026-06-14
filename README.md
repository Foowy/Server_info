# Server_info

PowerShell scripts to quickly collect and display Windows server health metrics (CPU, memory, disk, uptime, network) and audit SMB share disk usage for local or remote servers. Designed for support teams and system administrators who need to gather server information for documentation, troubleshooting, or incident response.

[![PSScriptAnalyzer](https://github.com/Foowy/Server_info/actions/workflows/powershell.yml/badge.svg)](https://github.com/Foowy/Server_info/actions/workflows/powershell.yml)

## Scripts

| Script | Version | Purpose |
|---|---|---|
| [Get_Server_Info.ps1](Get_Server_Info.ps1) | 1.2.0 | Server health metrics (CPU, memory, disk, uptime, network) |
| [Get-ShareInfo.ps1](Get-ShareInfo.ps1) | 1.0.0 | SMB share disk usage audit with CSV export |

---

## Get_Server_Info.ps1

### Features

- **Fast metric collection** — Gathers CPU load, memory usage, disk space, uptime, and network adapter info
- **Local and remote** — Works on the local computer or queries remote Windows servers
- **Multi-server support** — Poll multiple servers in a single run with comma-separated input
- **Dual-mode connectivity** — Uses PowerShell remoting (WinRM) when available; falls back to WMI/CIM for compatibility
- **Auto-update** — Checks GitHub once per day for script updates
- **Formatted output** — Clean summary tables and detailed breakdowns ready for copy/paste into support notes

### Requirements

- Windows PowerShell 3.0 or later
- Administrative privileges on the target computer(s)
- Windows Server 2008 and later (or any system with PowerShell 3.0+)
- Optional: PowerShell remoting (WinRM) enabled for fast remote collection

### Usage

```powershell
# Single server
.\Get_Server_Info.ps1 -ComputerName Server01

# Multiple servers
.\Get_Server_Info.ps1 -ComputerName Server01,Server02,Server03

# Local computer
.\Get_Server_Info.ps1 -ComputerName .
.\Get_Server_Info.ps1 -ComputerName $env:COMPUTERNAME

# Interactive prompt (leave blank or press Enter for local machine)
.\Get_Server_Info.ps1
```

### Output

```
================================= SERVER SUMMARY =================================
----------------------------------------------------------------------------------
Computer Name          |   CPU Load | Memory Used           | Overall Storage Used       | Uptime
----------------------------------------------------------------------------------
server01               |          5% | 8.45 GB (42.2%)      | 156.78 GB (45%)            | 42d 3h 15m 22s
----------------------------------------------------------------------------------

UPTIME BREAKDOWN:
  Last Boot: 03/15/2026 14:30:15
  Days     : 42
  Hours    : 3
  Minutes  : 15

MEMORY BREAKDOWN:
  Total   : 20.00 GB
  Used    : 8.45 GB
  Free    : 11.55 GB
  Percent : 42.2 %

STORAGE BREAKDOWN:
Drive C: (Windows)
  Used : 156.78 GB
  Free : 343.22 GB
  Total: 500.00 GB
  Util : 31.36%

NETWORK INFORMATION:
Adapter : Ethernet
IP Addr : 192.168.1.100
Subnet  : 255.255.255.0
DNS     : 8.8.8.8, 8.8.4.4
MAC     : 00-1A-2B-3C-4D-5E
```

### Auto-Update

The script checks GitHub once per day for updates using the GitHub Contents API. If an update is found it backs up the current version to `Get_Server_Info.ps1.bak`, writes the new version, and relaunches automatically.

Update detection compares the local file's git blob SHA-1 against the SHA returned by the API, so line-ending differences between platforms never trigger false updates.

---

## Get-ShareInfo.ps1

### Features

- **Share enumeration** — Lists all SMB shares on one or more servers
- **Disk usage per share** — Calculates used bytes for each share with a 300-second timeout per share
- **Human-readable sizes** — Outputs results in B/KB/MB/GB/TB automatically
- **Graceful error handling** — Reports `[Access Denied]` or `[Timeout]` without halting execution
- **Multi-server support** — Audit multiple servers in one run with comma-separated input
- **CSV export** — Optional `-OutputPath` parameter exports results for capacity planning or billing audits
- **System share filtering** — Excludes hidden system shares (`$`, `IPC$`, `ADMIN$`) by default; use `-IncludeSystemShares` to include them
- **Auto-update** — Checks GitHub once per day for script updates

### Requirements

- Windows PowerShell 3.0 or later
- Administrative privileges on the target computer(s)
- Windows Server 2012 or later (or any system with PowerShell 3.0+ and the `SmbShare` module)
- PowerShell remoting (WinRM) enabled on remote targets for share enumeration

### Usage

```powershell
# Audit a single server
.\Get-ShareInfo.ps1 -ComputerName Server01

# Audit multiple servers and export to CSV
.\Get-ShareInfo.ps1 -ComputerName Server01,Server02,Server03 -OutputPath .\shares.csv

# Audit local computer including system shares
.\Get-ShareInfo.ps1 -ComputerName . -IncludeSystemShares

# Interactive prompt
.\Get-ShareInfo.ps1
```

### Parameters

| Parameter | Type | Required | Description |
|---|---|---|---|
| `-ComputerName` | `string[]` | No | One or more server names. Prompts if omitted. Use `.` or `localhost` for local. |
| `-OutputPath` | `string` | No | Path for CSV export (e.g. `C:\reports\shares.csv`). |
| `-IncludeSystemShares` | `switch` | No | Include hidden system shares (`IPC$`, `ADMIN$`, etc.). |

### Output

```
===================== Server01 =====================
Timestamp: 2026-06-04 10:22:15
Total Shares: 4 | Accessible: 3

Share Name   Size       Status
----------   ----       ------
Data         12.50 GB   OK
Profiles     4.20 GB    OK
Backup       0 B        Access Denied
Software     1.83 GB    OK

Total (accessible): 18.53 GB
```

---

## Troubleshooting

**"Restarting with elevated privileges..."**
Both scripts require admin rights and will auto-elevate if needed.

**Remote server fails to connect**
- Ensure the target server is reachable (`Test-Connection`)
- Check that your account has admin rights on the remote computer
- `Get_Server_Info.ps1`: if WinRM isn't configured, the script falls back to WMI/CIM
- `Get-ShareInfo.ps1`: requires WinRM for remote share enumeration (`Invoke-Command`)

**No network adapter information**
This is normal if the server has no active network adapters. The script skips N/A adapters.

**Share size shows `[Timeout]`**
The share size calculation has a 300-second per-share timeout. Very large or slow shares may hit this limit; partial readable size is still reported where possible.

---

## License

BSD 3-Clause License — see [LICENSE](LICENSE) file for details.

## Contributing

Issues and pull requests welcome at https://github.com/Foowy/Server_info

## Author

**Foowy**
- GitHub: [@Foowy](https://github.com/Foowy)
