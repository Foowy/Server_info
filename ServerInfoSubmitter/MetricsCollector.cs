using System;
using System.Collections.Generic;
using System.Management;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace ServerInfoSubmitter
{
    public static class MetricsCollector
    {
        public static Task<ServerMetrics> CollectAsync(string computerName)
        {
            return Task.Run(() => Collect(computerName));
        }

        private static ServerMetrics Collect(string computerName)
        {
            bool isLocal = string.IsNullOrWhiteSpace(computerName)
                || computerName == "."
                || string.Equals(computerName, Environment.MachineName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(computerName, "localhost", StringComparison.OrdinalIgnoreCase);

            ManagementScope scope;
            if (isLocal)
            {
                scope = new ManagementScope(@"root\cimv2");
            }
            else
            {
                scope = new ManagementScope($@"\\{computerName}\root\cimv2");
                scope.Options.Timeout       = TimeSpan.FromSeconds(30);
                scope.Options.Impersonation = ImpersonationLevel.Impersonate;
                scope.Options.Authentication = AuthenticationLevel.PacketPrivacy;
                scope.Connect();
            }

            return new ServerMetrics
            {
                ComputerName = isLocal ? Environment.MachineName : computerName,
                Memory       = GetMemory(scope),
                Storage      = GetStorage(scope),
                CPU          = GetCpu(scope),
                Uptime       = GetUptime(scope),
                Network      = GetNetwork(scope)
            };
        }

        private static MemoryMetric GetMemory(ManagementScope scope)
        {
            try
            {
                using (var s = new ManagementObjectSearcher(scope,
                    new ObjectQuery("SELECT TotalVisibleMemorySize, FreePhysicalMemory FROM Win32_OperatingSystem")))
                {
                    foreach (ManagementObject o in s.Get())
                    {
                        double totalKB = Convert.ToDouble(o["TotalVisibleMemorySize"]);
                        double freeKB  = Convert.ToDouble(o["FreePhysicalMemory"]);
                        double usedKB  = totalKB - freeKB;
                        return new MemoryMetric
                        {
                            TotalGB = Math.Round(totalKB / (1024 * 1024), 2),
                            UsedGB  = Math.Round(usedKB  / (1024 * 1024), 2),
                            FreeGB  = Math.Round(freeKB  / (1024 * 1024), 2),
                            Percent = Math.Round((usedKB / totalKB) * 100, 1)
                        };
                    }
                }
            }
            catch (Exception ex) { return new MemoryMetric { Error = ex.Message }; }
            return new MemoryMetric { Error = "No data returned" };
        }

        private static StorageMetric GetStorage(ManagementScope scope)
        {
            var result = new StorageMetric();
            try
            {
                using (var s = new ManagementObjectSearcher(scope,
                    new ObjectQuery("SELECT DeviceID, VolumeName, Size, FreeSpace FROM Win32_LogicalDisk WHERE DriveType=3")))
                {
                    double totalBytes = 0, freeBytes = 0;
                    foreach (ManagementObject o in s.Get())
                    {
                        double size = Convert.ToDouble(o["Size"]);
                        double free = Convert.ToDouble(o["FreeSpace"]);
                        double used = size - free;
                        totalBytes += size;
                        freeBytes  += free;
                        result.Drives.Add(new StorageDrive
                        {
                            Drive   = o["DeviceID"]?.ToString()   ?? "",
                            Label   = o["VolumeName"]?.ToString() ?? "",
                            TotalGB = Math.Round(size / 1e9, 2),
                            UsedGB  = Math.Round(used / 1e9, 2),
                            FreeGB  = Math.Round(free / 1e9, 2),
                            Percent = size > 0 ? Math.Round((used / size) * 100, 2) : 0
                        });
                    }
                    double usedTotal = totalBytes - freeBytes;
                    result.TotalGB = Math.Round(totalBytes / 1e9, 2);
                    result.UsedGB  = Math.Round(usedTotal  / 1e9, 2);
                    result.FreeGB  = Math.Round(freeBytes  / 1e9, 2);
                    result.Percent = totalBytes > 0 ? Math.Round((usedTotal / totalBytes) * 100, 2) : 0;
                }
            }
            catch (Exception ex) { result.Error = ex.Message; }
            return result;
        }

        private static CpuMetric GetCpu(ManagementScope scope)
        {
            try
            {
                using (var s = new ManagementObjectSearcher(scope,
                    new ObjectQuery("SELECT LoadPercentage FROM Win32_Processor")))
                {
                    double total = 0; int count = 0;
                    foreach (ManagementObject o in s.Get())
                    {
                        total += Convert.ToDouble(o["LoadPercentage"]);
                        count++;
                    }
                    return new CpuMetric { Load = count > 0 ? Math.Round(total / count, 1) : 0 };
                }
            }
            catch (Exception ex) { return new CpuMetric { Error = ex.Message }; }
        }

        private static UptimeMetric GetUptime(ManagementScope scope)
        {
            try
            {
                using (var s = new ManagementObjectSearcher(scope,
                    new ObjectQuery("SELECT LastBootUpTime FROM Win32_OperatingSystem")))
                {
                    foreach (ManagementObject o in s.Get())
                    {
                        DateTime lastBoot = ManagementDateTimeConverter.ToDateTime(o["LastBootUpTime"].ToString());
                        TimeSpan uptime   = DateTime.Now - lastBoot;
                        return new UptimeMetric
                        {
                            LastBoot = lastBoot,
                            Days     = uptime.Days,
                            Hours    = uptime.Hours,
                            Minutes  = uptime.Minutes,
                            Seconds  = uptime.Seconds
                        };
                    }
                }
            }
            catch { }
            return new UptimeMetric();
        }

        private static List<NetworkAdapter> GetNetwork(ManagementScope scope)
        {
            var result = new List<NetworkAdapter>();
            try
            {
                using (var s = new ManagementObjectSearcher(scope,
                    new ObjectQuery("SELECT Description, IPAddress, IPSubnet, DNSServerSearchOrder, MACAddress FROM Win32_NetworkAdapterConfiguration WHERE IPEnabled=True")))
                {
                    foreach (ManagementObject o in s.Get())
                    {
                        string[] ipAddresses = o["IPAddress"]            as string[] ?? Array.Empty<string>();
                        string[] ipSubnets   = o["IPSubnet"]             as string[] ?? Array.Empty<string>();
                        string[] dns         = o["DNSServerSearchOrder"] as string[] ?? Array.Empty<string>();

                        // IPSubnet mixes IPv4 masks and IPv6 prefix lengths -- keep only IPv4 masks
                        string[] ipv4    = Array.FindAll(ipAddresses, ip => Regex.IsMatch(ip ?? "", @"^\d+\.\d+\.\d+\.\d+$"));
                        string[] subnets = Array.FindAll(ipSubnets,   m  => Regex.IsMatch(m  ?? "", @"^\d+\.\d+\.\d+\.\d+$"));

                        result.Add(new NetworkAdapter
                        {
                            Adapter   = o["Description"]?.ToString() ?? "",
                            IPAddress = string.Join(", ", ipv4),
                            Subnet    = string.Join(", ", subnets),
                            DNS       = string.Join(", ", dns),
                            MAC       = o["MACAddress"]?.ToString() ?? ""
                        });
                    }
                }
            }
            catch { }
            return result;
        }
    }
}
