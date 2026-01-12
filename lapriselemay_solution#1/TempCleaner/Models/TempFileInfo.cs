using CommunityToolkit.Mvvm.ComponentModel;
using TempCleaner.Helpers;

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

    public string SizeFormatted => FileSizeHelper.Format(Size);

    public string RelativePath => System.IO.Path.GetDirectoryName(FullPath) ?? string.Empty;
}
