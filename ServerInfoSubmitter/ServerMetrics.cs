using System;
using System.Collections.Generic;

namespace ServerInfoSubmitter
{
    public class MemoryMetric
    {
        public double TotalGB { get; set; }
        public double UsedGB  { get; set; }
        public double FreeGB  { get; set; }
        public double Percent { get; set; }
        public string? Error  { get; set; }
    }

    public class StorageDrive
    {
        public string Drive   { get; set; } = string.Empty;
        public string Label   { get; set; } = string.Empty;
        public double TotalGB { get; set; }
        public double UsedGB  { get; set; }
        public double FreeGB  { get; set; }
        public double Percent { get; set; }
    }

    public class StorageMetric
    {
        public double TotalGB              { get; set; }
        public double UsedGB               { get; set; }
        public double FreeGB               { get; set; }
        public double Percent              { get; set; }
        public List<StorageDrive> Drives   { get; set; } = new List<StorageDrive>();
        public string? Error               { get; set; }
    }

    public class CpuMetric
    {
        public double Load   { get; set; }
        public string? Error { get; set; }
    }

    public class UptimeMetric
    {
        public DateTime? LastBoot { get; set; }
        public int Days           { get; set; }
        public int Hours          { get; set; }
        public int Minutes        { get; set; }
        public int Seconds        { get; set; }
    }

    public class NetworkAdapter
    {
        public string Adapter   { get; set; } = string.Empty;
        public string IPAddress { get; set; } = string.Empty;
        public string Subnet    { get; set; } = string.Empty;
        public string DNS       { get; set; } = string.Empty;
        public string MAC       { get; set; } = string.Empty;
    }

    public class ServerMetrics
    {
        public string ComputerName                  { get; set; } = string.Empty;
        public MemoryMetric Memory                  { get; set; } = new MemoryMetric();
        public StorageMetric Storage                { get; set; } = new StorageMetric();
        public CpuMetric CPU                        { get; set; } = new CpuMetric();
        public UptimeMetric Uptime                  { get; set; } = new UptimeMetric();
        public List<NetworkAdapter> Network         { get; set; } = new List<NetworkAdapter>();
    }
}
