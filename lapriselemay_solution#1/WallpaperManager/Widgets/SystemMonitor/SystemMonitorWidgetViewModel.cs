using System.Diagnostics;
using System.IO;
using System.Management;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using WallpaperManager.Widgets.Base;

namespace WallpaperManager.Widgets.SystemMonitor;

/// <summary>
/// ViewModel pour le widget System Monitor.
/// </summary>
public class SystemMonitorWidgetViewModel : WidgetViewModelBase
{
    #region Native APIs
    
    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetPhysicallyInstalledSystemMemory(out long totalMemoryInKilobytes);
    
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private class MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }
    
    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);
    
    #endregion
    
    private static readonly string LogFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WallpaperManager", "widget_debug.log");
    
    private PerformanceCounter? _cpuCounter;
    private long _lastBytesReceived;
    private long _lastBytesSent;
    private DateTime _lastNetworkCheck;
    
    protected override int RefreshIntervalSeconds => 2;
    
    // CPU
    private double _cpuUsage;
    public double CpuUsage
    {
        get => _cpuUsage;
        private set => SetProperty(ref _cpuUsage, value);
    }
    
    private string _cpuName = "CPU";
    public string CpuName
    {
        get => _cpuName;
        private set => SetProperty(ref _cpuName, value);
    }
    
    // RAM
    private double _ramUsage;
    public double RamUsage
    {
        get => _ramUsage;
        private set => SetProperty(ref _ramUsage, value);
    }
    
    private double _ramUsedGb;
    public double RamUsedGb
    {
        get => _ramUsedGb;
        private set => SetProperty(ref _ramUsedGb, value);
    }
    
    private double _ramTotalGb;
    public double RamTotalGb
    {
        get => _ramTotalGb;
        private set => SetProperty(ref _ramTotalGb, value);
    }
    
    // GPU
    private double _gpuUsage;
    public double GpuUsage
    {
        get => _gpuUsage;
        private set => SetProperty(ref _gpuUsage, value);
    }
    
    private string _gpuName = "GPU";
    public string GpuName
    {
        get => _gpuName;
        private set => SetProperty(ref _gpuName, value);
    }
    
    private double _gpuMemoryUsedMb;
    public double GpuMemoryUsedMb
    {
        get => _gpuMemoryUsedMb;
        private set => SetProperty(ref _gpuMemoryUsedMb, value);
    }
    
    // Réseau
    private double _downloadSpeed;
    public double DownloadSpeed
    {
        get => _downloadSpeed;
        private set => SetProperty(ref _downloadSpeed, value);
    }
    
    private double _uploadSpeed;
    public double UploadSpeed
    {
        get => _uploadSpeed;
        private set => SetProperty(ref _uploadSpeed, value);
    }
    
    private string _downloadSpeedFormatted = "0 B/s";
    public string DownloadSpeedFormatted
    {
        get => _downloadSpeedFormatted;
        private set => SetProperty(ref _downloadSpeedFormatted, value);
    }
    
    private string _uploadSpeedFormatted = "0 B/s";
    public string UploadSpeedFormatted
    {
        get => _uploadSpeedFormatted;
        private set => SetProperty(ref _uploadSpeedFormatted, value);
    }
    
    public SystemMonitorWidgetViewModel()
    {
        Log("SystemMonitorWidgetViewModel créé");
        InitializeCounters();
    }
    
    private new static void Log(string message)
    {
        try
        {
            var dir = Path.GetDirectoryName(LogFile);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            File.AppendAllText(LogFile, $"[{DateTime.Now:HH:mm:ss.fff}] SYSMON: {message}\n");
        }
        catch { }
    }
    
    private void InitializeCounters()
    {
        try
        {
            _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
            _cpuCounter.NextValue();
            Log("CPU counter initialisé");
            
            GetCpuName();
            GetGpuName();
            GetTotalRam();
            
            _lastNetworkCheck = DateTime.Now;
            var stats = GetNetworkStats();
            _lastBytesReceived = stats.received;
            _lastBytesSent = stats.sent;
            Log($"Init OK - RamTotalGb={RamTotalGb}");
        }
        catch (Exception ex)
        {
            Log($"Init ERREUR: {ex.Message}");
        }
    }
    
    private void GetCpuName()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("select Name from Win32_Processor");
            foreach (var obj in searcher.Get())
            {
                var name = obj["Name"]?.ToString() ?? "CPU";
                CpuName = string.Join(" ", name.Split(' ', StringSplitOptions.RemoveEmptyEntries));
                break;
            }
        }
        catch { }
    }
    
    private void GetGpuName()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("select Name from Win32_VideoController");
            foreach (var obj in searcher.Get())
            {
                GpuName = obj["Name"]?.ToString() ?? "GPU";
                break;
            }
        }
        catch { }
    }
    
    private void GetTotalRam()
    {
        try
        {
            if (GetPhysicallyInstalledSystemMemory(out long totalKb))
            {
                RamTotalGb = Math.Round(totalKb / 1024.0 / 1024.0, 1);
                Log($"RAM Total: {RamTotalGb} GB");
            }
            else
            {
                Log("GetPhysicallyInstalledSystemMemory a échoué");
            }
        }
        catch (Exception ex)
        {
            Log($"GetTotalRam ERREUR: {ex.Message}");
        }
    }
    
    public override async Task RefreshAsync()
    {
        try
        {
            await Task.Run(() =>
            {
                // CPU
                if (_cpuCounter != null)
                {
                    CpuUsage = Math.Round(_cpuCounter.NextValue(), 1);
                }
                
                // RAM
                var memStatus = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX)) };
                if (GlobalMemoryStatusEx(memStatus))
                {
                    RamUsage = (double)memStatus.dwMemoryLoad;
                    var usedBytes = memStatus.ullTotalPhys - memStatus.ullAvailPhys;
                    RamUsedGb = Math.Round(usedBytes / 1024.0 / 1024.0 / 1024.0, 1);
                    
                    // Aussi mettre à jour RamTotalGb si pas encore fait
                    if (RamTotalGb == 0)
                    {
                        RamTotalGb = Math.Round(memStatus.ullTotalPhys / 1024.0 / 1024.0 / 1024.0, 1);
                    }
                }
                
                // GPU
                UpdateGpuStats();
                
                // Réseau
                UpdateNetworkStats();
            });
        }
        catch (Exception ex)
        {
            Log($"RefreshAsync ERREUR: {ex.Message}");
        }
    }
    
    private void UpdateGpuStats()
    {
        try
        {
            var category = new PerformanceCounterCategory("GPU Engine");
            var instanceNames = category.GetInstanceNames();
            
            double totalUsage = 0;
            int count = 0;
            
            foreach (var instance in instanceNames.Where(n => n.Contains("engtype_3D")))
            {
                using var counter = new PerformanceCounter("GPU Engine", "Utilization Percentage", instance);
                totalUsage += counter.NextValue();
                count++;
            }
            
            if (count > 0)
            {
                GpuUsage = Math.Round(totalUsage / count, 1);
            }
            
            var memCategory = new PerformanceCounterCategory("GPU Process Memory");
            var memInstances = memCategory.GetInstanceNames();
            double totalMemory = 0;
            
            foreach (var instance in memInstances)
            {
                using var counter = new PerformanceCounter("GPU Process Memory", "Dedicated Usage", instance);
                totalMemory += counter.NextValue();
            }
            
            GpuMemoryUsedMb = Math.Round(totalMemory / 1024 / 1024, 0);
        }
        catch
        {
            // GPU stats non disponibles
        }
    }
    
    private void UpdateNetworkStats()
    {
        var now = DateTime.Now;
        var elapsed = (now - _lastNetworkCheck).TotalSeconds;
        
        if (elapsed < 0.5) return;
        
        var stats = GetNetworkStats();
        
        var bytesReceivedDiff = stats.received - _lastBytesReceived;
        var bytesSentDiff = stats.sent - _lastBytesSent;
        
        var downloadBytesPerSec = bytesReceivedDiff / elapsed;
        var uploadBytesPerSec = bytesSentDiff / elapsed;
        
        DownloadSpeed = downloadBytesPerSec;
        UploadSpeed = uploadBytesPerSec;
        
        DownloadSpeedFormatted = FormatSpeed(downloadBytesPerSec);
        UploadSpeedFormatted = FormatSpeed(uploadBytesPerSec);
        
        _lastBytesReceived = stats.received;
        _lastBytesSent = stats.sent;
        _lastNetworkCheck = now;
    }
    
    private static (long received, long sent) GetNetworkStats()
    {
        long totalReceived = 0;
        long totalSent = 0;
        
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up)
                    continue;
                
                var stats = ni.GetIPv4Statistics();
                totalReceived += stats.BytesReceived;
                totalSent += stats.BytesSent;
            }
        }
        catch { }
        
        return (totalReceived, totalSent);
    }
    
    private static string FormatSpeed(double bytesPerSecond)
    {
        if (bytesPerSecond < 0) bytesPerSecond = 0;
        
        return bytesPerSecond switch
        {
            >= 1024 * 1024 * 1024 => $"{bytesPerSecond / 1024 / 1024 / 1024:F1} GB/s",
            >= 1024 * 1024 => $"{bytesPerSecond / 1024 / 1024:F1} MB/s",
            >= 1024 => $"{bytesPerSecond / 1024:F1} KB/s",
            _ => $"{bytesPerSecond:F0} B/s"
        };
    }
    
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _cpuCounter?.Dispose();
        }
        base.Dispose(disposing);
    }
}
