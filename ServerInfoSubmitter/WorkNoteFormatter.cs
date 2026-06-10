using System;
using System.Text;

namespace ServerInfoSubmitter
{
    public static class WorkNoteFormatter
    {
        public static string Format(ServerMetrics m)
        {
            var sb = new StringBuilder();

            sb.AppendLine("[code]");
            sb.AppendLine("SERVER INFORMATION REPORT");
            sb.AppendLine($"Computer  : {m.ComputerName}");
            sb.AppendLine($"Collected : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"Tool      : ServerInfoSubmitter");
            sb.AppendLine();

            // CPU
            sb.AppendLine("CPU");
            if (m.CPU?.Error == null)
                sb.AppendLine($"  Load     : {m.CPU!.Load}%");
            else
                sb.AppendLine($"  Error    : {m.CPU.Error}");
            sb.AppendLine();

            // Uptime
            sb.AppendLine("UPTIME");
            if (m.Uptime?.LastBoot != null)
            {
                sb.AppendLine($"  Last Boot: {m.Uptime.LastBoot:MM/dd/yyyy HH:mm:ss}");
                sb.AppendLine($"  Duration : {m.Uptime.Days}d {m.Uptime.Hours}h {m.Uptime.Minutes}m {m.Uptime.Seconds}s");
            }
            else
            {
                sb.AppendLine("  Unavailable");
            }
            sb.AppendLine();

            // Memory
            sb.AppendLine("MEMORY");
            if (m.Memory?.Error == null)
            {
                sb.AppendLine($"  Total    : {m.Memory!.TotalGB} GB");
                sb.AppendLine($"  Used     : {m.Memory.UsedGB} GB ({m.Memory.Percent}%)");
                sb.AppendLine($"  Free     : {m.Memory.FreeGB} GB");
            }
            else
            {
                sb.AppendLine($"  Error    : {m.Memory!.Error}");
            }
            sb.AppendLine();

            // Storage
            sb.AppendLine("STORAGE");
            if (m.Storage?.Error == null)
            {
                foreach (StorageDrive d in m.Storage!.Drives)
                {
                    string label = string.IsNullOrWhiteSpace(d.Label) ? "No Label" : d.Label;
                    sb.AppendLine($"  Drive {d.Drive} ({label})");
                    sb.AppendLine($"    Used : {SizeStr(d.UsedGB)}  Free : {SizeStr(d.FreeGB)}  Total : {SizeStr(d.TotalGB)}  ({d.Percent}%)");
                }
            }
            else
            {
                sb.AppendLine($"  Error    : {m.Storage!.Error}");
            }
            sb.AppendLine();

            // Network
            sb.AppendLine("NETWORK");
            foreach (NetworkAdapter n in m.Network)
            {
                sb.AppendLine($"  Adapter  : {n.Adapter}");
                sb.AppendLine($"  IP       : {n.IPAddress}");
                sb.AppendLine($"  Subnet   : {n.Subnet}");
                sb.AppendLine($"  DNS      : {n.DNS}");
                sb.AppendLine($"  MAC      : {n.MAC}");
                sb.AppendLine();
            }

            sb.AppendLine("[/code]");
            return sb.ToString();
        }

        private static string SizeStr(double gb)
            => gb >= 1024 ? $"{Math.Round(gb / 1024, 2)} TB" : $"{gb} GB";
    }
}
