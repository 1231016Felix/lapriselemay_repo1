namespace TempCleaner.Models;

public class ScanResult
{
    public List<TempFileInfo> Files { get; set; } = [];
    public long TotalSize { get; set; }
    public int TotalCount { get; set; }
    public int AccessDeniedCount { get; set; }
    public TimeSpan ScanDuration { get; set; }
    public Dictionary<string, CategoryStats> CategoryStats { get; set; } = [];
}

public class CategoryStats
{
    public string Name { get; set; } = string.Empty;
    public int FileCount { get; set; }
    public long TotalSize { get; set; }
    public string Icon { get; set; } = "📁";
}
