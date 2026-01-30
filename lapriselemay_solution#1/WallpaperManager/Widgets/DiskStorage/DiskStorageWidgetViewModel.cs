using System.Collections.ObjectModel;
using System.IO;
using WallpaperManager.Widgets.Base;

namespace WallpaperManager.Widgets.DiskStorage;

public class DiskInfo
{
    public string Name { get; set; } = "";
    public string Label { get; set; } = "";
    public long TotalBytes { get; set; }
    public long UsedBytes { get; set; }
    public long FreeBytes { get; set; }
    public double UsedPercent => TotalBytes > 0 ? (double)UsedBytes / TotalBytes * 100 : 0;
    public string TotalFormatted => FormatSize(TotalBytes);
    public string UsedFormatted => FormatSize(UsedBytes);
    public string FreeFormatted => FormatSize(FreeBytes);
    
    // Pour le graphique circulaire (arc)
    public double ArcEndAngle => UsedPercent * 3.6; // 0-360 degrés
    
    // Couleur selon utilisation
    public string Color => UsedPercent switch
    {
        >= 90 => "#EF4444", // Rouge
        >= 75 => "#F59E0B", // Orange
        _ => "#10B981"      // Vert
    };
    
    private static string FormatSize(long bytes)
    {
        string[] sizes = ["B", "KB", "MB", "GB", "TB"];
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }
        return $"{len:F1} {sizes[order]}";
    }
}

public class DiskStorageWidgetViewModel : WidgetViewModelBase
{
    protected override int RefreshIntervalSeconds => 30;
    
    private ObservableCollection<DiskInfo> _disks = [];
    public ObservableCollection<DiskInfo> Disks
    {
        get => _disks;
        set => SetProperty(ref _disks, value);
    }
    
    public override Task RefreshAsync()
    {
        try
        {
            var drives = DriveInfo.GetDrives()
                .Where(d => d.IsReady && d.DriveType == DriveType.Fixed)
                .Select(d => new DiskInfo
                {
                    Name = d.Name.TrimEnd('\\'),
                    Label = string.IsNullOrEmpty(d.VolumeLabel) ? "Disque local" : d.VolumeLabel,
                    TotalBytes = d.TotalSize,
                    FreeBytes = d.AvailableFreeSpace,
                    UsedBytes = d.TotalSize - d.AvailableFreeSpace
                })
                .ToList();
            
            Disks = new ObservableCollection<DiskInfo>(drives);
            ErrorMessage = null;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        
        return Task.CompletedTask;
    }
}
