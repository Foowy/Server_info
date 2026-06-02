# Server_info

PowerShell script to quickly collect and display Windows server health metrics (CPU, memory, disk, uptime, network) for local or remote servers. Designed for support teams and system administrators who need to gather server information for documentation, troubleshooting, or incident response.

## Features

- **Fast metric collection** — Gathers CPU load, memory usage, disk space, uptime, and network adapter info
- **Local and remote** — Works on the local computer or queries remote Windows servers
- **Multi-server support** — Poll multiple servers in a single run with comma-separated input
- **Dual-mode connectivity** — Uses PowerShell remoting (WinRM) when available; falls back to WMI/CIM for compatibility
- **Auto-update** — Checks GitHub once per day for script updates (optional)
- **Formatted output** — Clean summary tables and detailed breakdowns ready for copy/paste into support notes

## Requirements

- Windows PowerShell 3.0 or later
- Administrative privileges on the target computer(s)
- Windows Server 2008 Non-R2 or later (or any system with PowerShell 3.0+)
- Optional: PowerShell remoting (WinRM) enabled for fast remote collection

## Installation

1. Download `Get_Server_Info.ps1`
2. Save it to a local directory
3. Run from PowerShell with admin privileges

```powershell
.\Get_Server_Info.ps1 -ComputerName Server01
```

## Usage

### Query a single server
```powershell
.\Get_Server_Info.ps1 -ComputerName Server01
```

### Query multiple servers
```powershell
.\Get_Server_Info.ps1 -ComputerName Server01,Server02,Server03
```

### Local computer
```powershell
# Pass dot or local machine name explicitly
.\Get_Server_Info.ps1 -ComputerName .
.\Get_Server_Info.ps1 -ComputerName $env:COMPUTERNAME

# Or run with no arguments and press Enter at the prompt
.\Get_Server_Info.ps1
```

### Interactive prompt
Run without parameters to be prompted for server names. Leave the prompt blank (or enter `.`) to target the local machine:
```powershell
.\Get_Server_Info.ps1
# Enter one or more servers (comma-separated, or leave blank for local machine): <Enter>
```

## Output

The script displays a formatted summary table followed by detailed breakdowns:

```
========================= SERVER SUMMARY =========================
Computer Name      |    CPU Load | Memory Used              | Overall Storage Used | Uptime
server01           |          5% | 8.45 GB (42.2%)          | 156.78 GB (45%)      | 42d 3h 15m 22s

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

## Auto-Update

The script checks GitHub once per day for updates. To enable auto-update with authenticated access (higher GitHub API rate limits), add a `Token` key to the `$updateConfig` hash in the script:

```powershell
$updateConfig = @{
    Owner     = 'Foowy'
    Repo      = 'Server_info'
    Branch    = 'main'
    FilePath  = 'Get_Server_Info.ps1'
    StampFile = Join-Path (Split-Path $PSCommandPath -Parent) '.GSI_last_update_check'
    Token     = 'your_github_pat_here'  # Optional: GitHub Personal Access Token
}
```

## Troubleshooting

**"Restarting with elevated privileges..."**  
The script requires admin rights. It will auto-elevate if needed.

**Remote server fails to connect**  
- Ensure the target server is reachable (ping or `Test-Connection`)
- Check that your user account has admin rights on the remote computer
- If WinRM isn't configured, the script falls back to WMI (may be slower)

**No network adapter information**  
This is normal if the server has no active network adapters. The script skips N/A adapters.

## License

MIT License — see [LICENSE](LICENSE) file for details.

## Contributing

Issues and pull requests welcome at https://github.com/Foowy/Server_info

## Author

**Foowy**
- GitHub: [@Foowy](https://github.com/Foowy)
