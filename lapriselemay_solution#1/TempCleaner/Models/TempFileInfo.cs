using CommunityToolkit.Mvvm.ComponentModel;

namespace TempCleaner.Models;

public partial class TempFileInfo : ObservableObject
{
    [ObservableProperty]
    private bool _isSelected = true;

    [ObservableProperty]
    private string _fullPath = string.Empty;

    [ObservableProperty]
    private string _fileName = string.Empty;

    [ObservableProperty]
    private long _size;

    [ObservableProperty]
    private DateTime _lastModified;

    [ObservableProperty]
    private string _category = string.Empty;

    [ObservableProperty]
    private bool _isAccessible = true;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    public string SizeFormatted => FormatSize(Size);

    public string RelativePath => System.IO.Path.GetDirectoryName(FullPath) ?? string.Empty;

    private static string FormatSize(long bytes)
    {
        string[] suffixes = ["B", "KB", "MB", "GB", "TB"];
        int suffixIndex = 0;
        double size = bytes;

        while (size >= 1024 && suffixIndex < suffixes.Length - 1)
        {
            size /= 1024;
            suffixIndex++;
        }

        return $"{size:N2} {suffixes[suffixIndex]}";
    }
}
